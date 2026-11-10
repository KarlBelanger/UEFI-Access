// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Globalization;
using System.Management;
using Serilog;

namespace UefiAccess.Core.Platform;

/// <summary>What one read-only Win32_EncryptableVolume method returned: its ReturnValue and the one output the inspector reads.</summary>
/// <typeparam name="T">The type of the output.</typeparam>
/// <param name="ReturnValue">The method's ReturnValue (0 on success, an HRESULT otherwise).</param>
/// <param name="Value">The output the inspector reads.</param>
internal readonly record struct VolumeMethodResult<T>(uint ReturnValue, T Value);

/// <summary>
/// The three read-only Win32_EncryptableVolume methods <see cref="BitLockerInspector"/> calls on
/// one volume. Its WMI implementation runs each call, the fetch of the method's parameter
/// definition included, under WmiHelper's 60-second bound; a test passes a fake. A call may
/// throw what System.Management throws, or <see cref="TimeoutException"/>.
/// </summary>
internal interface IEncryptableVolumeMethods
{
    /// <summary>GetKeyProtectors with KeyProtectorType 0 (every type).</summary>
    /// <returns>The ReturnValue and the protector IDs (VolumeKeyProtectorID).</returns>
    VolumeMethodResult<IReadOnlyList<string>> GetKeyProtectors();

    /// <summary>GetKeyProtectorType for one protector.</summary>
    /// <param name="protectorId">A protector ID that GetKeyProtectors returned.</param>
    /// <returns>The ReturnValue and the numeric protector type, or null when none was reported.</returns>
    VolumeMethodResult<int?> GetKeyProtectorType(string protectorId);

    /// <summary>GetKeyProtectorPlatformValidationProfile for one protector.</summary>
    /// <param name="protectorId">A protector ID that GetKeyProtectors returned.</param>
    /// <returns>The ReturnValue and the PCR indices of the profile.</returns>
    VolumeMethodResult<IReadOnlyList<int>> GetPlatformValidationProfile(string protectorId);
}

/// <summary>
/// Reads BitLocker state from root\CIMV2\Security\MicrosoftVolumeEncryption Win32_EncryptableVolume.
/// Only the ProtectionStatus and ConversionStatus properties and the read-only methods
/// GetKeyProtectors, GetKeyProtectorType and GetKeyProtectorPlatformValidationProfile are used;
/// nothing else is ever invoked on that class. Never throws; failures are collected in Errors.
/// </summary>
/// <remarks>
/// Every WMI round trip runs under WmiHelper's 60-second bound: the connect
/// (<see cref="WmiHelper.ConnectBounded(string)"/>), the enumeration as a whole (on the connected
/// scope, so the searcher does not connect again; each batch is also bounded by the enumeration
/// options), and each method call together with the fetch of its parameter definition, which
/// System.Management makes from WMI on the first GetMethodParameters for an object. Once one call
/// has timed out the provider is stuck, so the calls that would follow, on this volume and on the
/// next ones, are not made and each is reported as not called: a stuck provider costs one bound
/// for the enumeration and one for the method calls, not one per call.
/// </remarks>
public sealed class BitLockerInspector : IPlatformInspector<BitLockerResult>
{
    private const string Namespace = @"root\CIMV2\Security\MicrosoftVolumeEncryption";
    private const string Query = "SELECT DeviceID, DriveLetter, ProtectionStatus, ConversionStatus FROM Win32_EncryptableVolume";
    private const string GetKeyProtectorsMethod = "GetKeyProtectors";
    private const string GetKeyProtectorTypeMethod = "GetKeyProtectorType";
    private const string GetPlatformValidationProfileMethod = "GetKeyProtectorPlatformValidationProfile";
    private const string NotWindowsMessage = "BitLocker information requires WMI, which is only available on Windows";
    private const string ReturnValueProperty = "ReturnValue";

    /// <summary>FVE_E_NOT_ACTIVATED: BitLocker is not enabled on the volume, so there are no protectors to list.</summary>
    private const uint FveNotActivated = 0x80310008;

    private static readonly int[] Pcr7And11 = { 7, 11 };
    private static readonly int[] LegacyPcrProfile = { 0, 2, 4, 11 };

    private readonly ILogger _logger;

    /// <summary>Creates the inspector without a logger.</summary>
    public BitLockerInspector()
        : this(null)
    {
    }

    /// <summary>Creates the inspector that logs every WMI call to <paramref name="logger"/>.</summary>
    /// <param name="logger">Optional Serilog logger; null disables logging.</param>
    public BitLockerInspector(ILogger? logger)
    {
        _logger = logger ?? Serilog.Core.Logger.None;
    }

    /// <inheritdoc />
    public BitLockerResult Inspect()
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger.Debug("BitLockerInspector skipped: not running on Windows");
            return new BitLockerResult(Array.Empty<BitLockerVolume>(), new[] { NotWindowsMessage });
        }

        List<string> errors = new();
        List<BitLockerVolume> volumes = new();
        List<ManagementObject> found = new();
        string? timedOutCall = null;
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            // The class remarks say how each round trip is bounded. The searcher uses the scope
            // ConnectBounded connected, and the whole enumeration runs inside one bounded call; an
            // enumeration abandoned after its timeout disposes what it found when it finishes.
            ManagementScope scope = WmiHelper.ConnectBounded(Namespace);
            found = WmiHelper.RunBounded(
                () =>
                {
                    List<ManagementObject> fetched = new();
                    try
                    {
                        using ManagementObjectSearcher searcher = new(scope, new ObjectQuery(Query), WmiHelper.CreateEnumerationOptions());
                        using ManagementObjectCollection collection = searcher.Get();
                        foreach (ManagementBaseObject item in collection)
                        {
                            if (item is ManagementObject volume)
                            {
                                fetched.Add(volume);
                            }
                            else
                            {
                                item.Dispose();
                            }
                        }
                    }
                    catch (Exception)
                    {
                        DisposeAll(fetched);
                        throw;
                    }

                    return fetched;
                },
                WmiHelper.QueryTimeout,
                "query Win32_EncryptableVolume",
                DisposeAll);
            _logger.Debug("Win32_EncryptableVolume -> {Count} volume(s), {Elapsed:F1} ms", found.Count, stopwatch.Elapsed.TotalMilliseconds);

            foreach (ManagementObject volume in found)
            {
                volumes.Add(InspectVolume(volume, ref timedOutCall, errors));
            }
        }
        catch (Exception ex) when (WmiHelper.IsAbsent(ex))
        {
            _logger.Information("Win32_EncryptableVolume is not available: {Detail}", WmiHelper.Describe(ex));
            errors.Add("Win32_EncryptableVolume is not available (BitLocker WMI provider missing on this Windows edition): " + WmiHelper.Describe(ex));
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Win32_EncryptableVolume query failed");
            errors.Add("Win32_EncryptableVolume: " + WmiHelper.Describe(ex));
        }
        finally
        {
            // A method call abandoned after its timeout may still be using its volume object, so
            // after a timeout the volumes are left to the finalizer.
            if (timedOutCall is null)
            {
                DisposeAll(found);
            }
        }

        volumes.Sort((left, right) => string.CompareOrdinal(left.DriveLetter, right.DriveLetter));
        return new BitLockerResult(volumes, errors);
    }

    /// <summary>Human-readable ProtectionStatus: 0 Off, 1 On, 2 Unknown.</summary>
    /// <param name="protectionStatus">Win32_EncryptableVolume.ProtectionStatus.</param>
    /// <returns>The text.</returns>
    internal static string ProtectionStatusText(int protectionStatus)
    {
        return protectionStatus switch
        {
            0 => "Off",
            1 => "On",
            2 => "Unknown",
            _ => "Unknown (" + protectionStatus.ToString(CultureInfo.InvariantCulture) + ")",
        };
    }

    /// <summary>
    /// Human-readable protection status that takes the conversion into account. Windows reports
    /// ProtectionStatus 0 for a volume that is still being encrypted, so "Off" would read as a
    /// failure right after BitLocker was turned on: while encryption runs the text is
    /// "Pending until encryption completes", while it is paused "Pending, encryption paused".
    /// Every other combination is <see cref="ProtectionStatusText(int)"/>.
    /// </summary>
    /// <param name="protectionStatus">Win32_EncryptableVolume.ProtectionStatus.</param>
    /// <param name="conversionStatus">Win32_EncryptableVolume.ConversionStatus, or -1 when unknown.</param>
    /// <returns>The text.</returns>
    internal static string ProtectionStatusText(int protectionStatus, int conversionStatus)
    {
        return (protectionStatus, conversionStatus) switch
        {
            (0, 2) => "Pending until encryption completes",
            (0, 4) => "Pending, encryption paused",
            _ => ProtectionStatusText(protectionStatus),
        };
    }

    /// <summary>
    /// ConversionStatus in words: 0 Decrypted, 1 Encrypted, 2 Encrypting, 3 Decrypting,
    /// 4 Encryption paused, 5 Decryption paused; -1 (not reported) Unknown.
    /// </summary>
    /// <param name="conversionStatus">Win32_EncryptableVolume.ConversionStatus, or -1 when unknown.</param>
    /// <returns>The text.</returns>
    internal static string ConversionStatusText(int conversionStatus)
    {
        return conversionStatus switch
        {
            0 => "Decrypted",
            1 => "Encrypted",
            2 => "Encrypting",
            3 => "Decrypting",
            4 => "Encryption paused",
            5 => "Decryption paused",
            -1 => "Unknown",
            _ => "Unknown (" + conversionStatus.ToString(CultureInfo.InvariantCulture) + ")",
        };
    }

    /// <summary>Name of a KeyProtectorType value as returned by GetKeyProtectorType.</summary>
    /// <param name="keyProtectorType">The numeric type.</param>
    /// <returns>The name, for example "TPM" or "TPM+PIN".</returns>
    internal static string ProtectorTypeName(int keyProtectorType)
    {
        return keyProtectorType switch
        {
            0 => "Unknown or other",
            1 => "TPM",
            2 => "External key",
            3 => "Numerical password",
            4 => "TPM+PIN",
            5 => "TPM+startup key",
            6 => "TPM+PIN+startup key",
            7 => "Public key",
            8 => "Passphrase",
            9 => "TPM certificate",
            10 => "CryptoAPI",
            _ => "Unknown (" + keyProtectorType.ToString(CultureInfo.InvariantCulture) + ")",
        };
    }

    /// <summary>True for the protector types that involve the TPM (1, 4, 5, 6) and therefore have a platform validation profile.</summary>
    /// <param name="keyProtectorType">The numeric type.</param>
    /// <returns>Whether the protector is TPM-based.</returns>
    internal static bool IsTpmFamily(int keyProtectorType)
    {
        return keyProtectorType is 1 or 4 or 5 or 6;
    }

    /// <summary>
    /// Renders a PCR profile explicitly: "PCR 7+11" when it is exactly PCRs 7 and 11,
    /// "PCR 0,2,4,11 (legacy)" when it is exactly PCRs 0, 2, 4 and 11, otherwise "PCR " followed by
    /// the sorted list. Null when the profile is empty.
    /// </summary>
    /// <param name="pcrProfile">PCR indices in any order.</param>
    /// <returns>The text or null.</returns>
    internal static string? PcrProfileText(IReadOnlyList<int> pcrProfile)
    {
        int[] sorted = NormalizePcrProfile(pcrProfile);
        if (sorted.Length == 0)
        {
            return null;
        }

        if (sorted.SequenceEqual(Pcr7And11))
        {
            return "PCR 7+11";
        }

        if (sorted.SequenceEqual(LegacyPcrProfile))
        {
            return "PCR 0,2,4,11 (legacy)";
        }

        return "PCR " + string.Join(",", sorted.Select(pcr => pcr.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>Sorts a PCR profile ascending and removes duplicates.</summary>
    /// <param name="pcrProfile">PCR indices in any order.</param>
    /// <returns>The normalised array.</returns>
    internal static int[] NormalizePcrProfile(IReadOnlyList<int> pcrProfile)
    {
        return pcrProfile.Distinct().OrderBy(pcr => pcr).ToArray();
    }

    /// <summary>
    /// True when an inspection recorded that the key protector list of a volume could not be read:
    /// the list call failed, returned an error other than FVE_E_NOT_ACTIVATED, or was not made
    /// because an earlier call timed out. An empty protector list then means "not known", not
    /// "none". The write pipeline's BitLocker check uses this so that a volume whose protectors
    /// could not be read is never taken for one without protectors.
    /// </summary>
    /// <param name="result">The inspection's result.</param>
    /// <param name="driveLetter">The volume's drive letter, for example "C:" (compared ignoring case).</param>
    /// <returns>Whether the protector list of that volume is unknown.</returns>
    internal static bool ProtectorListUnread(BitLockerResult result, string driveLetter)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(driveLetter);

        // The errors of one call start with its label, method + " on " + drive, as TryCall and
        // GetKeyProtectorIds write them: followed by " returned 0x..." or by ": <failure>".
        string label = string.Concat(GetKeyProtectorsMethod, " on ", driveLetter);
        return result.Errors.Any(error =>
            error.StartsWith(label + " ", StringComparison.OrdinalIgnoreCase)
            || error.StartsWith(label + ":", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The read-only methods of one volume a query returned, for a caller outside the inspector
    /// that needs the same reads under the same bound: the write pipeline's one-restart suspend
    /// re-reads the protector list through it, so the three read-only
    /// method names stay declared in this file and nowhere else. Creating it makes no WMI round
    /// trip; each call is bounded as the class remarks say.
    /// </summary>
    /// <param name="volume">The volume object, from a query on a scope connected through WmiHelper.</param>
    /// <param name="driveLetter">The volume's drive letter, for the timeout messages.</param>
    /// <returns>The volume's read-only methods.</returns>
    internal static IEncryptableVolumeMethods MethodsFor(ManagementObject volume, string driveLetter)
    {
        ArgumentNullException.ThrowIfNull(volume);
        ArgumentNullException.ThrowIfNull(driveLetter);
        return new WmiVolumeMethods(volume, driveLetter);
    }

    /// <summary>
    /// Reads the key protectors of one volume through <paramref name="methods"/>: GetKeyProtectors,
    /// then GetKeyProtectorType for each protector, then the platform validation profile of the
    /// first TPM-based protector that has one. A failed call is reported in
    /// <paramref name="errors"/> and reading goes on. A call that times out is recorded in
    /// <paramref name="timedOutCall"/>; while that is set, no further call is made (here or for
    /// the volumes after this one) and each call that would have followed is reported as not
    /// called, because the provider is stuck. A protector whose type could not be read counts as
    /// "Unknown". Never throws.
    /// </summary>
    /// <param name="methods">The volume's methods.</param>
    /// <param name="driveLetter">The volume's drive letter or device ID, for messages.</param>
    /// <param name="timedOutCall">The label of the first call of this inspection that timed out, or null; set here when a call times out.</param>
    /// <param name="errors">Receives one line per failed or skipped call and per nonzero ReturnValue (FVE_E_NOT_ACTIVATED from GetKeyProtectors excepted).</param>
    /// <returns>The protector type names, and the PCR profile as reported (not yet normalised).</returns>
    internal (IReadOnlyList<string> ProtectorTypes, IReadOnlyList<int> PcrProfile) ReadProtectors(IEncryptableVolumeMethods methods, string driveLetter, ref string? timedOutCall, List<string> errors)
    {
        List<string> protectorTypes = new();
        List<int> pcrProfile = new();
        bool haveProfile = false;
        foreach (string protectorId in GetKeyProtectorIds(methods, driveLetter, ref timedOutCall, errors))
        {
            int? type = GetKeyProtectorType(methods, protectorId, driveLetter, ref timedOutCall, errors);
            if (type is null)
            {
                protectorTypes.Add("Unknown");
                continue;
            }

            protectorTypes.Add(ProtectorTypeName(type.Value));
            if (IsTpmFamily(type.Value) && !haveProfile)
            {
                IReadOnlyList<int>? profile = GetPlatformValidationProfile(methods, protectorId, driveLetter, ref timedOutCall, errors);
                if (profile is not null)
                {
                    pcrProfile.AddRange(profile);
                    haveProfile = true;
                }
            }
        }

        return (protectorTypes, pcrProfile);
    }

    private static void DisposeAll(List<ManagementObject> objects)
    {
        foreach (ManagementObject item in objects)
        {
            try
            {
                item.Dispose();
            }
            catch (Exception)
            {
                // Releasing a WMI object is best effort.
            }
        }
    }

    private BitLockerVolume InspectVolume(ManagementObject volume, ref string? timedOutCall, List<string> errors)
    {
        // The properties came with the enumeration; reading them makes no WMI round trip.
        string driveLetter = WmiValues.ToText(WmiHelper.GetProperty(volume, "DriveLetter"))
            ?? WmiValues.ToText(WmiHelper.GetProperty(volume, "DeviceID"))
            ?? "(unknown volume)";
        int protectionStatus = WmiValues.ToNullableInt(WmiHelper.GetProperty(volume, "ProtectionStatus")) ?? 2;
        int conversionStatus = WmiValues.ToNullableInt(WmiHelper.GetProperty(volume, "ConversionStatus")) ?? -1;

        (IReadOnlyList<string> protectorTypes, IReadOnlyList<int> pcrProfile) = ReadProtectors(new WmiVolumeMethods(volume, driveLetter), driveLetter, ref timedOutCall, errors);

        int[] normalized = NormalizePcrProfile(pcrProfile);
        string? pcrText = PcrProfileText(normalized);
        string protectionText = ProtectionStatusText(protectionStatus, conversionStatus);
        string conversionText = ConversionStatusText(conversionStatus);
        _logger.Information(
            "BitLocker {Drive}: protection {Protection} ({ProtectionText}), conversion {Conversion} ({ConversionText}), protectors [{Protectors}], {Pcr}",
            driveLetter,
            protectionStatus,
            protectionText,
            conversionStatus,
            conversionText,
            string.Join(", ", protectorTypes),
            pcrText ?? "no TPM profile");

        return new BitLockerVolume(
            DriveLetter: driveLetter,
            ProtectionStatus: protectionStatus,
            ProtectionStatusText: protectionText,
            ConversionStatus: conversionStatus,
            ConversionStatusText: conversionText,
            ProtectorTypes: protectorTypes,
            PcrProfile: normalized,
            PcrProfileText: pcrText);
    }

    private IReadOnlyList<string> GetKeyProtectorIds(IEncryptableVolumeMethods methods, string driveLetter, ref string? timedOutCall, List<string> errors)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        if (!TryCall(GetKeyProtectorsMethod, driveLetter, methods.GetKeyProtectors, ref timedOutCall, errors, out VolumeMethodResult<IReadOnlyList<string>> result))
        {
            return Array.Empty<string>();
        }

        IReadOnlyList<string> ids = result.Value ?? Array.Empty<string>();
        _logger.Debug("{Method}({Drive}) -> 0x{Return:X8}, {Count} protector(s), {Elapsed:F1} ms", GetKeyProtectorsMethod, driveLetter, result.ReturnValue, ids.Count, stopwatch.Elapsed.TotalMilliseconds);
        if (result.ReturnValue != 0)
        {
            if (result.ReturnValue != FveNotActivated)
            {
                errors.Add($"{GetKeyProtectorsMethod} on {driveLetter} returned 0x{result.ReturnValue:X8}");
            }

            return Array.Empty<string>();
        }

        return ids;
    }

    private int? GetKeyProtectorType(IEncryptableVolumeMethods methods, string protectorId, string driveLetter, ref string? timedOutCall, List<string> errors)
    {
        // A deliberate deviation: this read-only call names each protector ID GetKeyProtectors returns.
        Stopwatch stopwatch = Stopwatch.StartNew();
        if (!TryCall(GetKeyProtectorTypeMethod, driveLetter, () => methods.GetKeyProtectorType(protectorId), ref timedOutCall, errors, out VolumeMethodResult<int?> result))
        {
            return null;
        }

        _logger.Debug("{Method}({Drive}) -> 0x{Return:X8}, type {Type}, {Elapsed:F1} ms", GetKeyProtectorTypeMethod, driveLetter, result.ReturnValue, result.Value, stopwatch.Elapsed.TotalMilliseconds);
        if (result.ReturnValue != 0)
        {
            errors.Add($"{GetKeyProtectorTypeMethod} on {driveLetter} returned 0x{result.ReturnValue:X8}");
            return null;
        }

        return result.Value;
    }

    private IReadOnlyList<int>? GetPlatformValidationProfile(IEncryptableVolumeMethods methods, string protectorId, string driveLetter, ref string? timedOutCall, List<string> errors)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        if (!TryCall(GetPlatformValidationProfileMethod, driveLetter, () => methods.GetPlatformValidationProfile(protectorId), ref timedOutCall, errors, out VolumeMethodResult<IReadOnlyList<int>> result))
        {
            return null;
        }

        IReadOnlyList<int> pcrs = result.Value ?? Array.Empty<int>();
        _logger.Debug("{Method}({Drive}) -> 0x{Return:X8}, PCRs [{Pcrs}], {Elapsed:F1} ms", GetPlatformValidationProfileMethod, driveLetter, result.ReturnValue, string.Join(",", pcrs), stopwatch.Elapsed.TotalMilliseconds);
        if (result.ReturnValue != 0)
        {
            errors.Add($"{GetPlatformValidationProfileMethod} on {driveLetter} returned 0x{result.ReturnValue:X8}");
            return null;
        }

        return pcrs;
    }

    /// <summary>
    /// Makes one method call unless an earlier call of this inspection timed out. A failure is
    /// logged and reported in <paramref name="errors"/>; a timeout also sets
    /// <paramref name="timedOutCall"/>. Never throws.
    /// </summary>
    private bool TryCall<T>(string method, string driveLetter, Func<VolumeMethodResult<T>> call, ref string? timedOutCall, List<string> errors, out VolumeMethodResult<T> result)
    {
        result = default;
        string label = method + " on " + driveLetter;
        if (timedOutCall is not null)
        {
            _logger.Debug("{Method}({Drive}) not called: {TimedOut} timed out", method, driveLetter, timedOutCall);
            errors.Add($"{label}: not called, because {timedOutCall} timed out");
            return false;
        }

        try
        {
            result = call();
            return true;
        }
        catch (TimeoutException ex)
        {
            timedOutCall = label;
            _logger.Warning("{Method}({Drive}) timed out: {Detail}", method, driveLetter, ex.Message);
            errors.Add($"{label}: {WmiHelper.Describe(ex)}");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "{Method}({Drive}) failed", method, driveLetter);
            errors.Add($"{label}: {WmiHelper.Describe(ex)}");
            return false;
        }
    }

    /// <summary>
    /// The ReturnValue of a Win32_EncryptableVolume method's output, through the one helper the
    /// write pipeline's suspend shares: a value that is not a UINT32 is a failure, and a missing
    /// one is <paramref name="whenMissing"/> (0 for the three read-only methods, as in M1).
    /// </summary>
    /// <param name="outParameters">The method's output parameters.</param>
    /// <param name="whenMissing">The value for a missing ReturnValue.</param>
    /// <returns>The return value.</returns>
    internal static uint ReturnValue(ManagementBaseObject outParameters, uint whenMissing = 0u)
    {
        return WmiValues.ToReturnValue(WmiHelper.GetProperty(outParameters, ReturnValueProperty), whenMissing);
    }

    /// <summary>
    /// The WMI implementation of <see cref="IEncryptableVolumeMethods"/> for one volume the
    /// enumeration returned. Each call runs whole inside WmiHelper.RunBounded: GetMethodParameters,
    /// which fetches the class definition from WMI on its first use for an object, as well as
    /// InvokeMethod. The parameter objects are created and disposed inside the bounded delegate and
    /// only plain values leave it, so a call abandoned after its timeout releases its own objects
    /// when it finishes.
    /// </summary>
    private sealed class WmiVolumeMethods : IEncryptableVolumeMethods
    {
        private readonly ManagementObject _volume;
        private readonly string _driveLetter;

        public WmiVolumeMethods(ManagementObject volume, string driveLetter)
        {
            _volume = volume;
            _driveLetter = driveLetter;
        }

        public VolumeMethodResult<IReadOnlyList<string>> GetKeyProtectors()
        {
            return WmiHelper.RunBounded(
                () =>
                {
                    using ManagementBaseObject inParameters = _volume.GetMethodParameters(GetKeyProtectorsMethod);
                    inParameters["KeyProtectorType"] = 0u;
                    using ManagementBaseObject outParameters = _volume.InvokeMethod(GetKeyProtectorsMethod, inParameters, new InvokeMethodOptions());
                    return new VolumeMethodResult<IReadOnlyList<string>>(ReturnValue(outParameters), WmiValues.ToTextList(WmiHelper.GetProperty(outParameters, "VolumeKeyProtectorID")));
                },
                WmiHelper.QueryTimeout,
                GetKeyProtectorsMethod + " on " + _driveLetter);
        }

        public VolumeMethodResult<int?> GetKeyProtectorType(string protectorId)
        {
            return WmiHelper.RunBounded(
                () =>
                {
                    using ManagementBaseObject inParameters = _volume.GetMethodParameters(GetKeyProtectorTypeMethod);
                    inParameters["VolumeKeyProtectorID"] = protectorId;
                    using ManagementBaseObject outParameters = _volume.InvokeMethod(GetKeyProtectorTypeMethod, inParameters, new InvokeMethodOptions());
                    return new VolumeMethodResult<int?>(ReturnValue(outParameters), WmiValues.ToNullableInt(WmiHelper.GetProperty(outParameters, "KeyProtectorType")));
                },
                WmiHelper.QueryTimeout,
                GetKeyProtectorTypeMethod + " on " + _driveLetter);
        }

        public VolumeMethodResult<IReadOnlyList<int>> GetPlatformValidationProfile(string protectorId)
        {
            return WmiHelper.RunBounded(
                () =>
                {
                    using ManagementBaseObject inParameters = _volume.GetMethodParameters(GetPlatformValidationProfileMethod);
                    inParameters["VolumeKeyProtectorID"] = protectorId;
                    using ManagementBaseObject outParameters = _volume.InvokeMethod(GetPlatformValidationProfileMethod, inParameters, new InvokeMethodOptions());
                    return new VolumeMethodResult<IReadOnlyList<int>>(ReturnValue(outParameters), PcrList(WmiHelper.GetProperty(outParameters, "PlatformValidationProfile")));
                },
                WmiHelper.QueryTimeout,
                GetPlatformValidationProfileMethod + " on " + _driveLetter);
        }

        private static List<int> PcrList(object? raw)
        {
            List<int> pcrs = new();
            if (raw is Array array)
            {
                foreach (object? element in array)
                {
                    int? pcr = WmiValues.ToNullableInt(element);
                    if (pcr is not null)
                    {
                        pcrs.Add(pcr.Value);
                    }
                }
            }

            return pcrs;
        }
    }
}
