// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using Serilog;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Variables;

namespace UefiAccess.Core.Safety;

/// <summary>
/// The platform facts a change is checked against, read through the M1 inspectors with their
/// bounds: the machine identity and the A18 check, then the unit check against the Windows
/// machine ID (stage S2), the Secure Boot policy from the registry and the flag variables (stage
/// S7; the PowerShell cross-check is never run by the pipeline), and the BitLocker state with the
/// TPM version that completes its sentence (stage S8). Each inspector read runs under one bound of
/// <see cref="InspectorBound"/> on top of the inspector's own per-call bound, so a WMI provider
/// that never answers costs the pipeline one bound and gives a result that says so: an identity
/// that was not read never confirms the machine, a machine ID that was not read never confirms
/// the unit, and a BitLocker result with no volumes and an error is assessed as unknown, which the
/// pipeline treats as on. No member throws.
/// </summary>
public sealed class PlatformPolicy
{
    /// <summary>The BIOS version the contract covers; any other version makes <see cref="IsContractedMachine"/> false.</summary>
    public const string ContractedBiosVersion = "A18";

    /// <summary>
    /// The one unit M3 enables changes on: the lowercase hexadecimal SHA-256 of the canonical
    /// Windows machine ID of the developer's 2014 OptiPlex 7020, computed on 25 September 2026.
    /// The machine ID is the MachineGuid value that Windows creates at installation (a random
    /// 128-bit ID, readable without elevation, not derived from the Dell service tag); its
    /// canonical form is the value with surrounding white space removed, in lower case, hashed as
    /// UTF-8 (<see cref="MachineIdSha256"/>). The constant binds the M3 writes to that unit's
    /// Windows installation, not to its hardware: on every machine whose Windows installation is
    /// not a copy of this unit's, another OptiPlex 7020 on BIOS A18 included, stage S11 refuses
    /// the change at check expectedUnit. A disk clone, a moved drive, a restored system image or
    /// a shared deployment image can carry the same MachineGuid onto another machine, which then
    /// passes this check; so this unit's disk or system image is not cloned or restored onto
    /// another machine while this constant is in place. A reinstall of Windows creates a new
    /// MachineGuid, and from then on every change is refused until this constant is updated in a
    /// reviewed commit. Enabling a second unit is an M4 decision. Only
    /// this hash is in the code; the machine ID itself and this hash are never written to a
    /// record, a log, a snapshot or a document.
    /// </summary>
    public const string EnabledUnitMachineIdSha256 = "2cb591022287e00142629bc234babbe9aebf9b8a71a676483c64fe005e34590d";

    /// <summary>The Secure Boot line of the dialog's Warnings box and the record; {0} is one of the four state texts below.</summary>
    public const string SecureBootSentenceFormat = "Secure Boot: {0}. This application never changes it.";

    /// <summary>State text when Secure Boot is on and the firmware is in user mode.</summary>
    public const string SecureBootOnUserMode = "on, user mode";

    /// <summary>State text when Secure Boot is on and the firmware reports setup mode.</summary>
    public const string SecureBootOnSetupMode = "on, setup mode";

    /// <summary>State text when Secure Boot is off.</summary>
    public const string SecureBootOff = "off";

    /// <summary>State text when neither the registry nor the SecureBoot variable could be read.</summary>
    public const string SecureBootNotRead = "state not read";

    /// <summary>Prefix of the error recorded on an identity that was not read (a timeout or an inspector that threw).</summary>
    public const string IdentityNotReadPrefix = "identity not read: ";

    /// <summary>The expected-model note of an identity that was not read; {0} the error.</summary>
    public const string IdentityNotReadNoteFormat = "Expected model not confirmed: {0}";

    /// <summary>Prefix of the error recorded on a BitLocker result that was not read.</summary>
    public const string BitLockerNotReadPrefix = "BitLocker state not read: ";

    /// <summary>The reason when an inspector returned nothing at all.</summary>
    public const string NoResultReason = "the inspector returned no result";

    /// <summary>The key, under HKEY_LOCAL_MACHINE in the 64-bit registry view, that holds the Windows machine ID; read only, never opened for writing.</summary>
    private const string MachineIdKeyPath = @"SOFTWARE\Microsoft\Cryptography";

    /// <summary>The string value of the Windows machine ID, a GUID Windows creates at installation.</summary>
    private const string MachineIdValueName = "MachineGuid";

    private readonly IPlatformInspector<MachineIdentityResult> _identity;
    private readonly IPlatformInspector<SecureBootPolicyResult> _secureBoot;
    private readonly IPlatformInspector<BitLockerResult> _bitLocker;
    private readonly IPlatformInspector<TpmInfoResult>? _tpm;
    private readonly ILogger _logger;

    /// <summary>Creates the policy over three inspectors, without the TPM read, each bounded by 60 seconds, with the production unit check.</summary>
    /// <param name="identity">The machine identity inspector.</param>
    /// <param name="secureBoot">The Secure Boot policy inspector; the pipeline composes it without the PowerShell cross-check.</param>
    /// <param name="bitLocker">The BitLocker inspector.</param>
    /// <param name="logger">Optional Serilog logger; null disables logging.</param>
    public PlatformPolicy(
        IPlatformInspector<MachineIdentityResult> identity,
        IPlatformInspector<SecureBootPolicyResult> secureBoot,
        IPlatformInspector<BitLockerResult> bitLocker,
        ILogger? logger)
        : this(identity, secureBoot, bitLocker, null, logger, WmiHelper.QueryTimeout, new EnabledUnit())
    {
    }

    /// <summary>
    /// Creates the policy over four inspectors, each bounded by 60 seconds, with the production unit
    /// check (a new <see cref="EnabledUnit"/>: the Windows machine ID against <see cref="EnabledUnitMachineIdSha256"/>).
    /// </summary>
    /// <param name="identity">The machine identity inspector.</param>
    /// <param name="secureBoot">The Secure Boot policy inspector; the pipeline composes it without the PowerShell cross-check.</param>
    /// <param name="bitLocker">The BitLocker inspector.</param>
    /// <param name="tpm">The TPM inspector, whose version completes the BitLocker state sentence; null leaves it out.</param>
    /// <param name="logger">Optional Serilog logger; null disables logging.</param>
    public PlatformPolicy(
        IPlatformInspector<MachineIdentityResult> identity,
        IPlatformInspector<SecureBootPolicyResult> secureBoot,
        IPlatformInspector<BitLockerResult> bitLocker,
        IPlatformInspector<TpmInfoResult>? tpm,
        ILogger? logger)
        : this(identity, secureBoot, bitLocker, tpm, logger, WmiHelper.QueryTimeout, new EnabledUnit())
    {
    }

    /// <summary>
    /// Creates the policy over four inspectors, each bounded by 60 seconds, and a given unit check.
    /// Outside Core an <see cref="EnabledUnit"/> can only be the production check, so this
    /// constructor too compares with <see cref="EnabledUnitMachineIdSha256"/>; the command-line
    /// tool passes the one its composition root made.
    /// </summary>
    /// <param name="identity">The machine identity inspector.</param>
    /// <param name="secureBoot">The Secure Boot policy inspector; the pipeline composes it without the PowerShell cross-check.</param>
    /// <param name="bitLocker">The BitLocker inspector.</param>
    /// <param name="tpm">The TPM inspector, whose version completes the BitLocker state sentence; null leaves it out.</param>
    /// <param name="unit">The unit check of stage S2.</param>
    /// <param name="logger">Optional Serilog logger; null disables logging.</param>
    public PlatformPolicy(
        IPlatformInspector<MachineIdentityResult> identity,
        IPlatformInspector<SecureBootPolicyResult> secureBoot,
        IPlatformInspector<BitLockerResult> bitLocker,
        IPlatformInspector<TpmInfoResult>? tpm,
        EnabledUnit unit,
        ILogger? logger)
        : this(identity, secureBoot, bitLocker, tpm, logger, WmiHelper.QueryTimeout, unit)
    {
    }

    /// <summary>Creates the policy with an explicit bound per inspector read and a unit check over another machine ID source, for tests.</summary>
    /// <param name="identity">The machine identity inspector.</param>
    /// <param name="secureBoot">The Secure Boot policy inspector.</param>
    /// <param name="bitLocker">The BitLocker inspector.</param>
    /// <param name="tpm">The TPM inspector, or null.</param>
    /// <param name="logger">Optional Serilog logger; null disables logging.</param>
    /// <param name="inspectorBound">How long one inspector read may take before it counts as not read.</param>
    /// <param name="unit">The unit check of stage S2; a test builds it over a fake machine ID and that ID's hash.</param>
    internal PlatformPolicy(
        IPlatformInspector<MachineIdentityResult> identity,
        IPlatformInspector<SecureBootPolicyResult> secureBoot,
        IPlatformInspector<BitLockerResult> bitLocker,
        IPlatformInspector<TpmInfoResult>? tpm,
        ILogger? logger,
        TimeSpan inspectorBound,
        EnabledUnit unit)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(secureBoot);
        ArgumentNullException.ThrowIfNull(bitLocker);
        ArgumentNullException.ThrowIfNull(unit);
        _identity = identity;
        _secureBoot = secureBoot;
        _bitLocker = bitLocker;
        _tpm = tpm;
        _logger = logger ?? Serilog.Core.Logger.None;
        InspectorBound = inspectorBound < TimeSpan.Zero ? TimeSpan.Zero : inspectorBound;
        Unit = unit;
    }

    /// <summary>The bound one inspector read runs under, on top of the inspector's own per-call bound.</summary>
    public TimeSpan InspectorBound { get; }

    /// <summary>The unit check of stage S2; the production check unless a test built the policy.</summary>
    internal EnabledUnit Unit { get; }

    /// <summary>
    /// Composes the production inspectors: <see cref="MachineIdentity"/>, a
    /// <see cref="SecureBootPolicyInspector"/> over <paramref name="store"/> without the PowerShell
    /// cross-check, <see cref="BitLockerInspector"/> and <see cref="TpmInfo"/>, every one logging
    /// through <paramref name="logger"/>, and the production unit check (the Windows machine ID
    /// against <see cref="EnabledUnitMachineIdSha256"/>). Every one of them only reads.
    /// </summary>
    /// <param name="store">The variable store the Secure Boot inspector reads the flag variables from.</param>
    /// <param name="logger">Optional Serilog logger; null disables logging.</param>
    /// <returns>The policy.</returns>
    public static PlatformPolicy CreateWindows(IFirmwareVariableStore store, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ILogger log = logger ?? Serilog.Core.Logger.None;
        return new PlatformPolicy(
            new MachineIdentity(log),
            new SecureBootPolicyInspector(store, runPowerShellCrossCheck: false, log),
            new BitLockerInspector(log),
            new TpmInfo(log),
            log);
    }

    /// <summary>
    /// The A18 check of stage S2: the identity confirms the 2014 OptiPlex 7020
    /// (<see cref="MachineIdentityResult.ExpectedModelConfirmed"/>) and its BIOS version is
    /// exactly <see cref="ContractedBiosVersion"/>, ignoring case and surrounding spaces. An
    /// identity that was not read gives false.
    /// </summary>
    /// <param name="identity">The identity read at S2, or null.</param>
    /// <returns>True on the contracted machine and BIOS version only.</returns>
    public static bool IsContractedMachine(MachineIdentityResult? identity)
    {
        return identity is not null
            && identity.ExpectedModelConfirmed
            && string.Equals(identity.BiosVersion?.Trim(), ContractedBiosVersion, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether changes are enabled on this machine, the machine check of stage S2 that S11 enforces
    /// (<see cref="PolicyFindings.MachineConfirmed"/>): the contracted model on BIOS A18
    /// (<see cref="IsContractedMachine"/>) and the M3 test unit (<see cref="UnitMatch.Confirmed"/>).
    /// </summary>
    /// <param name="identity">The identity read at S2, or null.</param>
    /// <param name="unit">What the unit check of S2 found.</param>
    /// <returns>True on the M3 test unit only.</returns>
    public static bool IsEnabledMachine(MachineIdentityResult? identity, UnitMatch unit)
    {
        return IsContractedMachine(identity) && unit == UnitMatch.Confirmed;
    }

    /// <summary>
    /// The hash the unit check compares: the lowercase hexadecimal SHA-256 of the UTF-8 bytes of
    /// the canonical machine ID, the value with surrounding white space removed and in lower case.
    /// Text that is null, empty, only white space or not a GUID has no hash, so it never confirms
    /// the unit.
    /// </summary>
    /// <param name="machineId">The machine ID as Windows stores it, or null.</param>
    /// <returns>64 lowercase hexadecimal characters, or null.</returns>
    public static string? MachineIdSha256(string? machineId)
    {
        if (string.IsNullOrWhiteSpace(machineId))
        {
            return null;
        }

        string canonical = machineId.Trim().ToLowerInvariant();
        if (!Guid.TryParse(canonical, out _))
        {
            return null;
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    /// <summary>
    /// The Windows machine ID: the string MachineGuid under
    /// HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography, read in the 64-bit registry view (so a
    /// 32-bit process reads the same value) with the key opened read-only; it needs no elevation.
    /// Null when the value cannot be read or is not a string; never throws. The value is handed to
    /// the unit check only and is never logged or recorded.
    /// </summary>
    /// <returns>The machine ID, or null.</returns>
    public static string? ReadWindowsMachineId()
    {
        try
        {
            using RegistryKey hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using RegistryKey? key = hive.OpenSubKey(MachineIdKeyPath, writable: false);
            return key?.GetValue(MachineIdValueName) as string;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The unit check of stage S2 (<see cref="EnabledUnit.Check"/>): whether this Windows
    /// installation is the M3 test unit. Logs what it found, never the machine ID or its hash.
    /// </summary>
    /// <returns>What the check found; <see cref="UnitMatch.MachineIdNotRead"/> when it could not tell. Never throws.</returns>
    public UnitMatch ReadUnit()
    {
        UnitMatch unit;
        try
        {
            unit = Unit.Check();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Platform policy: the unit check threw");
            unit = UnitMatch.MachineIdNotRead;
        }

        _logger.Debug("Platform policy: unit check {Unit}", unit);
        return unit;
    }

    /// <summary>
    /// The enabled state the Secure Boot findings report: the SecureBoot variable when it was read,
    /// else the registry value, else null.
    /// </summary>
    /// <param name="policy">The Secure Boot findings, or null when they were not read.</param>
    /// <returns>True, false, or null when neither source was available.</returns>
    public static bool? SecureBootEnabled(SecureBootPolicyResult? policy)
    {
        return policy?.VariableSecureBoot ?? policy?.RegistryEnabled;
    }

    /// <summary>The Secure Boot sentence of the dialog's Warnings box and the record, from the findings.</summary>
    /// <param name="policy">The Secure Boot findings, or null when they were not read.</param>
    /// <returns>The sentence, built from <see cref="SecureBootSentenceFormat"/>.</returns>
    public static string DescribeSecureBoot(SecureBootPolicyResult? policy)
    {
        string state = SecureBootEnabled(policy) switch
        {
            true => policy?.VariableSetupMode == true ? SecureBootOnSetupMode : SecureBootOnUserMode,
            false => SecureBootOff,
            null => SecureBootNotRead,
        };
        return string.Format(CultureInfo.InvariantCulture, SecureBootSentenceFormat, state);
    }

    /// <summary>
    /// Reads the machine identity under the bound. A timeout, an inspector that threw or one that
    /// returned nothing gives an identity with every field null, the model not confirmed and one
    /// error starting with <see cref="IdentityNotReadPrefix"/>.
    /// </summary>
    /// <returns>The identity; never null.</returns>
    public MachineIdentityResult ReadIdentity()
    {
        try
        {
            MachineIdentityResult? result = WmiHelper.RunBounded(() => _identity.Inspect(), InspectorBound, "machine identity");
            if (result is null)
            {
                return IdentityNotRead(NoResultReason);
            }

            _logger.Debug("Platform policy: identity read; model {Model}, BIOS {Bios}, expected model confirmed {Confirmed}", result.Model, result.BiosVersion, result.ExpectedModelConfirmed);
            return result;
        }
        catch (TimeoutException ex)
        {
            _logger.Warning("Platform policy: {Reason}", IdentityNotReadPrefix + ex.Message);
            return IdentityNotRead(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Platform policy: the identity inspector threw");
            return IdentityNotRead(ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>Reads the Secure Boot policy under the bound. Informational: a timeout or a throw gives null.</summary>
    /// <returns>The findings, or null when they could not be read.</returns>
    public SecureBootPolicyResult? ReadSecureBoot()
    {
        try
        {
            return WmiHelper.RunBounded(() => _secureBoot.Inspect(), InspectorBound, "Secure Boot policy");
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Platform policy: the Secure Boot policy could not be read");
            return null;
        }
    }

    /// <summary>
    /// Reads the BitLocker state under the bound. A timeout, an inspector that threw or one that
    /// returned nothing gives a result with no volume and one error starting with
    /// <see cref="BitLockerNotReadPrefix"/>, which the pre-flight assesses as unknown.
    /// </summary>
    /// <returns>The result; never null.</returns>
    public BitLockerResult ReadBitLocker()
    {
        try
        {
            BitLockerResult? result = WmiHelper.RunBounded(() => _bitLocker.Inspect(), InspectorBound, "BitLocker state");
            return result ?? BitLockerNotRead(NoResultReason);
        }
        catch (TimeoutException ex)
        {
            _logger.Warning("Platform policy: {Reason}", BitLockerNotReadPrefix + ex.Message);
            return BitLockerNotRead(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Platform policy: the BitLocker inspector threw");
            return BitLockerNotRead(ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>Reads the TPM facts under the bound. Informational: no inspector, a timeout or a throw gives null.</summary>
    /// <returns>The TPM facts, or null when they could not be read.</returns>
    public TpmInfoResult? ReadTpm()
    {
        if (_tpm is not IPlatformInspector<TpmInfoResult> tpm)
        {
            return null;
        }

        try
        {
            return WmiHelper.RunBounded(() => tpm.Inspect(), InspectorBound, "TPM");
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Platform policy: the TPM facts could not be read");
            return null;
        }
    }

    private static MachineIdentityResult IdentityNotRead(string reason)
    {
        string error = IdentityNotReadPrefix + reason;
        return new MachineIdentityResult(
            Manufacturer: null,
            Model: null,
            SystemSku: null,
            BiosVersion: null,
            BiosReleaseDate: null,
            ServiceTag: null,
            BaseBoard: null,
            Cpu: null,
            OsCaption: null,
            OsVersion: null,
            OsBuild: null,
            ExpectedModelConfirmed: false,
            ExpectedModelNotes: string.Format(CultureInfo.InvariantCulture, IdentityNotReadNoteFormat, error),
            Errors: new[] { error });
    }

    private static BitLockerResult BitLockerNotRead(string reason)
    {
        return new BitLockerResult(Array.Empty<BitLockerVolume>(), new[] { BitLockerNotReadPrefix + reason });
    }
}
