// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Globalization;
using System.Management;
using Serilog;
using UefiAccess.Core.Platform;

namespace UefiAccess.Core.Safety;

/// <summary>
/// The Win32_EncryptableVolume calls the suspender makes on the OS volume: a fresh read of the
/// protection status, the protector list, and the one state-changing call, a suspend of every key
/// protector for a number of restarts. The WMI implementation runs each call under WmiHelper's
/// 60-second bound and lists the protectors through the inspector's own read-only seam, so the
/// read-only method names stay in <see cref="BitLockerInspector"/>; a test passes a fake. A call
/// may throw what System.Management throws, or <see cref="TimeoutException"/>.
/// </summary>
internal interface IEncryptableVolumeSuspendMethods
{
    /// <summary>Re-reads the volume from WMI and returns its ProtectionStatus (0 off, 1 on, 2 unknown).</summary>
    /// <returns>The value, or null when the property was not reported.</returns>
    int? ReadProtectionStatus();

    /// <summary>The key protector list: GetKeyProtectors with KeyProtectorType 0 (every type).</summary>
    /// <returns>The ReturnValue and the protector IDs (VolumeKeyProtectorID).</returns>
    VolumeMethodResult<IReadOnlyList<string>> GetKeyProtectors();

    /// <summary>The suspend: every key protector disabled for <paramref name="disableCount"/> restarts.</summary>
    /// <param name="disableCount">The restart count; the suspender always passes <see cref="BitLockerSuspender.OneRestart"/>, and the WMI implementation refuses any other.</param>
    /// <returns>The method's ReturnValue (0 on success, an HRESULT otherwise).</returns>
    uint SuspendKeyProtectors(uint disableCount);
}

/// <summary>
/// Suspends BitLocker on the OS volume for exactly one restart (task 3.7): one method invocation on root\CIMV2\Security\MicrosoftVolumeEncryption
/// Win32_EncryptableVolume with an explicit DisableCount of 1, never omitted, because the omitted
/// behaviour is not documented on the method page. The confirmation follows the state read
/// immediately before the call: when the protection status was not 0 (On or Unknown) it must read
/// 0 afterwards; when it was 0 already (Encrypting) that test is vacuous, so the protector list is
/// read before and after the call and must be unchanged. Nothing here resumes protection, and
/// nothing makes the call a second time. Never throws.
/// </summary>
/// <remarks>
/// Every WMI round trip runs under WmiHelper's 60-second bound (the connect, the query of the one
/// volume, each re-read, and the method call together with the fetch of its parameter
/// definition), and the core bounds the suspend call of its seam once more, so a seam whose
/// suspend never answers costs one bound and gives the timeout outcome. A suspend that does not
/// answer within the bound keeps running and can still complete afterwards, so the volume may end
/// up suspended: the result says so and the state is recorded as Unknown. The blocking sentences
/// that the transaction's other stages share are <see cref="CheckSentences"/> constants. The
/// production instance is built only by the composition roots; a test exercises
/// <see cref="Suspend(IEncryptableVolumeSuspendMethods, string, ILogger, TimeSpan)"/> with a fake
/// seam and never constructs this class.
/// </remarks>
public sealed class BitLockerSuspender : IBitLockerSuspender
{
    /// <summary>The name of the one state-changing method this file invokes; pinned by reflection in the guard.</summary>
    internal const string DisableKeyProtectorsMethod = "DisableKeyProtectors";

    /// <summary>The restart count always passed with the suspend; the omitted behaviour is not documented.</summary>
    internal const uint OneRestart = 1u;

    /// <summary>The blocking sentence when the suspend was not made or not confirmed; {0} the reason. A nonzero ReturnValue and a timeout use the <see cref="CheckSentences"/> sentences instead.</summary>
    public const string CouldNotSuspendFormat = "BitLocker could not be suspended ({0}). Nothing was written.";

    /// <summary>The reason when the re-read after a return of 0 does not show protection off; {0} the value read, or <see cref="UnreadableText"/>.</summary>
    public const string ProtectionStatusAfterReasonFormat = "protection status {0} after the call";

    /// <summary>The reason when the protector list differs after the call (the Encrypting confirmation).</summary>
    public const string ProtectorsChangedReason = "the key protector list changed after the call";

    /// <summary>The reason when the state could not be read before the call, so the call was not made; {0} the failure.</summary>
    public const string StateNotReadReasonFormat = "the state could not be read before the call: {0}";

    /// <summary>The reason when the protector list returned an error before the call, so the call was not made; {0} the HRESULT.</summary>
    public const string ProtectorsNotListedReasonFormat = "the key protectors could not be listed before the call: {0}";

    /// <summary>The reason when protection is off and the volume has no key protector, so there is nothing to suspend.</summary>
    public const string NoProtectorReason = "the drive has no key protector to suspend";

    /// <summary>The reason when the call returned 0 and the confirmation re-read failed; {0} the failure.</summary>
    public const string ConfirmationFailedReasonFormat = "the call returned 0 but the confirmation read failed: {0}";

    /// <summary>The reason when the call returned 0 and the protector list returned an error on the confirmation re-read; {0} the HRESULT.</summary>
    public const string ConfirmationNotListedReasonFormat = "the call returned 0 but the key protectors could not be listed again: {0}";

    /// <summary>The reason when the query found no encryptable volume with the drive letter; {0} the drive.</summary>
    public const string VolumeNotFoundReasonFormat = "the volume {0} was not found";

    /// <summary>The reason when the argument is not a drive letter such as "C:"; {0} the argument.</summary>
    public const string NotADriveLetterReasonFormat = "{0} is not a drive letter";

    /// <summary>The reason on a host without WMI.</summary>
    public const string NotWindowsReason = "BitLocker requires WMI, which is only available on Windows";

    /// <summary>The text used in a reason when a value could not be read.</summary>
    public const string UnreadableText = "unreadable";

    /// <summary>The record's suspendResult for a call that returned 0.</summary>
    public const string SucceededResultText = "0";

    /// <summary>The record's suspendResult for a call that did not answer within its bound.</summary>
    public const string TimeoutResultText = "timeout";

    /// <summary>The record's suspendResult for a call that threw.</summary>
    public const string FailedResultText = "failed";

    /// <summary>The record's suspendResult when the call was not made.</summary>
    public const string NotCalledResultText = "not called";

    /// <summary>
    /// The value a suspend whose output lacks its ReturnValue gets, through the inspector's shared
    /// helper: a failure, never 0. For the one call that changes state, "not reported" must never
    /// pass for "succeeded", since on the Encrypting path the unchanged protector list is the only
    /// other confirmation.
    /// </summary>
    internal const uint MissingReturnValue = uint.MaxValue;

    /// <summary>An HRESULT as the record and the reasons write it.</summary>
    private const string ReturnValueFormat = "0x{0:X8}";

    /// <summary>The label of the suspend call in the log and the timeout message: {0} the method, {1} the drive.</summary>
    private const string CallLabelFormat = "{0} on {1}";

    /// <summary>The label of the protection status read in the timeout message; {0} the drive.</summary>
    private const string ProtectionStatusLabelFormat = "ProtectionStatus of {0}";

    /// <summary>The message of the exception the WMI implementation throws for any count but one restart.</summary>
    private const string OneRestartOnlyMessage = "the suspend is made for one restart and for no other count";

    private const string Namespace = @"root\CIMV2\Security\MicrosoftVolumeEncryption";
    private const string QueryFormat = "SELECT DeviceID, DriveLetter, ProtectionStatus, ConversionStatus FROM Win32_EncryptableVolume WHERE DriveLetter = '{0}'";
    private const string ProtectionStatusProperty = "ProtectionStatus";

    private readonly ILogger _logger;

    /// <summary>Creates the suspender over WMI. Called by the composition roots only.</summary>
    /// <param name="logger">Optional Serilog logger; null disables logging.</param>
    public BitLockerSuspender(ILogger? logger)
    {
        _logger = logger ?? Serilog.Core.Logger.None;
    }

    /// <inheritdoc />
    public SuspendResult SuspendForOneRestart(string driveLetter)
    {
        ArgumentNullException.ThrowIfNull(driveLetter);
        Stopwatch stopwatch = Stopwatch.StartNew();
        string drive = driveLetter.Trim().ToUpperInvariant();
        if (!IsDriveLetter(drive))
        {
            return NotCalled(null, string.Format(CultureInfo.InvariantCulture, NotADriveLetterReasonFormat, driveLetter), stopwatch.Elapsed);
        }

        if (!OperatingSystem.IsWindows())
        {
            _logger.Debug("BitLocker suspend skipped: not running on Windows");
            return NotCalled(null, NotWindowsReason, stopwatch.Elapsed);
        }

        ManagementObject? volume;
        try
        {
            // The class remarks say how each round trip is bounded: the connect, then the query of
            // the one volume as a whole; a query abandoned after its timeout releases what it found
            // when it finishes.
            ManagementScope scope = WmiHelper.ConnectBounded(Namespace);
            string query = string.Format(CultureInfo.InvariantCulture, QueryFormat, drive);
            volume = WmiHelper.RunBounded(
                () =>
                {
                    ManagementObject? found = null;
                    using ManagementObjectSearcher searcher = new(scope, new ObjectQuery(query), WmiHelper.CreateEnumerationOptions());
                    using ManagementObjectCollection collection = searcher.Get();
                    foreach (ManagementBaseObject item in collection)
                    {
                        if (found is null && item is ManagementObject candidate)
                        {
                            found = candidate;
                        }
                        else
                        {
                            item.Dispose();
                        }
                    }

                    return found;
                },
                WmiHelper.QueryTimeout,
                "query Win32_EncryptableVolume " + drive,
                late => late?.Dispose());
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Win32_EncryptableVolume query for {Drive} failed; the suspend was not made", drive);
            return NotCalled(null, string.Format(CultureInfo.InvariantCulture, StateNotReadReasonFormat, WmiHelper.Describe(ex)), stopwatch.Elapsed);
        }

        if (volume is null)
        {
            _logger.Warning("Win32_EncryptableVolume has no volume {Drive}; the suspend was not made", drive);
            return NotCalled(null, string.Format(CultureInfo.InvariantCulture, VolumeNotFoundReasonFormat, drive), stopwatch.Elapsed);
        }

        SuspendResult result = Suspend(new WmiVolumeSuspendMethods(volume, drive), drive, _logger, WmiHelper.QueryTimeout, out bool timedOutAnywhere);
        if (!timedOutAnywhere)
        {
            // A call abandoned after its timeout may still be using the volume object, so after a
            // timeout it is left to the finalizer.
            volume.Dispose();
        }

        return result with { Elapsed = stopwatch.Elapsed };
    }

    /// <summary>
    /// The suspender's whole behaviour as a function of the seam: the state before the call, the
    /// one suspend call with <see cref="OneRestart"/> under the WMI bound, and the confirmation
    /// re-read that the state before decides. Never throws; every failure is in the result.
    /// </summary>
    /// <param name="volume">The volume's methods (the WMI ones in production, a fake in a test).</param>
    /// <param name="driveLetter">The drive letter, for the log and the reasons.</param>
    /// <param name="logger">Logger.</param>
    /// <returns>The result.</returns>
    internal static SuspendResult Suspend(IEncryptableVolumeSuspendMethods volume, string driveLetter, ILogger logger)
    {
        return Suspend(volume, driveLetter, logger, WmiHelper.QueryTimeout, out _);
    }

    /// <summary>
    /// <see cref="Suspend(IEncryptableVolumeSuspendMethods, string, ILogger)"/> with an explicit
    /// bound on the suspend call, so a test can show that a seam that never answers gives the
    /// timeout outcome without waiting a minute.
    /// </summary>
    /// <param name="volume">The volume's methods.</param>
    /// <param name="driveLetter">The drive letter, for the log and the reasons.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="callTimeout">The longest wait for the suspend call.</param>
    /// <returns>The result.</returns>
    internal static SuspendResult Suspend(IEncryptableVolumeSuspendMethods volume, string driveLetter, ILogger logger, TimeSpan callTimeout)
    {
        return Suspend(volume, driveLetter, logger, callTimeout, out _);
    }

    /// <summary>True for a drive letter of the form "C:".</summary>
    /// <param name="text">The text.</param>
    /// <returns>Whether it is a drive letter.</returns>
    internal static bool IsDriveLetter(string text)
    {
        return text.Length == 2 && char.IsAsciiLetter(text[0]) && text[1] == ':';
    }

    private static SuspendResult Suspend(IEncryptableVolumeSuspendMethods volume, string driveLetter, ILogger logger, TimeSpan callTimeout, out bool timedOutAnywhere)
    {
        ArgumentNullException.ThrowIfNull(volume);
        ArgumentNullException.ThrowIfNull(driveLetter);
        ArgumentNullException.ThrowIfNull(logger);
        timedOutAnywhere = false;
        Stopwatch stopwatch = Stopwatch.StartNew();
        string suspendLabel = SuspendLabel(driveLetter);

        // The state immediately before the call decides the confirmation. The reads are bounded by
        // the seam itself (the WMI implementation runs each under WmiHelper's bound).
        int? before;
        try
        {
            before = volume.ReadProtectionStatus();
        }
        catch (Exception ex)
        {
            timedOutAnywhere = ex is TimeoutException;
            logger.Warning(ex, "BitLocker state of {Drive} could not be read before the suspend; the suspend was not made", driveLetter);
            return NotCalled(null, string.Format(CultureInfo.InvariantCulture, StateNotReadReasonFormat, WmiHelper.Describe(ex)), stopwatch.Elapsed);
        }

        // Protection off already (Encrypting): the protector list is what the confirmation compares.
        IReadOnlyList<string>? protectorsBefore = null;
        if (before == 0)
        {
            VolumeMethodResult<IReadOnlyList<string>> read;
            try
            {
                read = volume.GetKeyProtectors();
            }
            catch (Exception ex)
            {
                timedOutAnywhere = ex is TimeoutException;
                logger.Warning(ex, "The key protector list of {Drive} could not be read before the suspend; the suspend was not made", driveLetter);
                return NotCalled(before, string.Format(CultureInfo.InvariantCulture, StateNotReadReasonFormat, WmiHelper.Describe(ex)), stopwatch.Elapsed);
            }

            if (read.ReturnValue != 0)
            {
                logger.Warning("The key protector list of {Drive} returned 0x{Return:X8} before the suspend; the suspend was not made", driveLetter, read.ReturnValue);
                return NotCalled(before, string.Format(CultureInfo.InvariantCulture, ProtectorsNotListedReasonFormat, Hex(read.ReturnValue)), stopwatch.Elapsed);
            }

            protectorsBefore = SortedCopy(read.Value);
            if (protectorsBefore.Count == 0)
            {
                logger.Warning("The key protector list of {Drive} is empty; the suspend was not made", driveLetter);
                return NotCalled(before, NoProtectorReason, stopwatch.Elapsed);
            }
        }

        // The one call. No branch below makes it again.
        uint returnValue;
        Stopwatch callWatch = Stopwatch.StartNew();
        try
        {
            returnValue = WmiHelper.RunBounded(() => volume.SuspendKeyProtectors(OneRestart), callTimeout, suspendLabel);
        }
        catch (TimeoutException ex)
        {
            timedOutAnywhere = true;
            logger.Warning("{Call} with DisableCount {Count} did not answer within its bound and may still complete: {Detail}", suspendLabel, OneRestart, ex.Message);
            return new SuspendResult(false, true, OneRestart, null, true, before, null, BitLockerState.Unknown, TimeoutResultText, CheckSentences.BitLockerSuspendTimedOut, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "{Call} with DisableCount {Count} failed", suspendLabel, OneRestart);
            return new SuspendResult(false, true, OneRestart, null, false, before, null, BitLockerState.Unknown, FailedResultText, CouldNotSuspend(WmiHelper.Describe(ex)), stopwatch.Elapsed);
        }

        logger.Information("{Call} with DisableCount {Count} -> 0x{Return:X8}, {Elapsed:F1} ms", suspendLabel, OneRestart, returnValue, callWatch.Elapsed.TotalMilliseconds);
        if (returnValue != 0)
        {
            return NotConfirmed(returnValue, before, null, CheckSentences.Format(CheckSentences.BitLockerSuspendFailedFormat, returnValue), stopwatch.Elapsed);
        }

        if (protectorsBefore is null)
        {
            // On or Unknown: the protection status must read 0 now.
            int? after;
            try
            {
                after = volume.ReadProtectionStatus();
            }
            catch (Exception ex)
            {
                timedOutAnywhere = ex is TimeoutException;
                logger.Warning(ex, "BitLocker state of {Drive} could not be re-read after the suspend", driveLetter);
                return NotConfirmed(returnValue, before, null, CouldNotSuspend(string.Format(CultureInfo.InvariantCulture, ConfirmationFailedReasonFormat, WmiHelper.Describe(ex))), stopwatch.Elapsed);
            }

            logger.Information("BitLocker {Drive}: protection status {Before} before the suspend, {After} after", driveLetter, before, after);
            if (after != 0)
            {
                string value = after is null ? UnreadableText : after.Value.ToString(CultureInfo.InvariantCulture);
                return NotConfirmed(returnValue, before, after, CouldNotSuspend(string.Format(CultureInfo.InvariantCulture, ProtectionStatusAfterReasonFormat, value)), stopwatch.Elapsed);
            }

            return Confirmed(before, after, stopwatch.Elapsed);
        }

        // Encrypting: the status was 0 before the call, so the protector list must be unchanged.
        VolumeMethodResult<IReadOnlyList<string>> reread;
        try
        {
            reread = volume.GetKeyProtectors();
        }
        catch (Exception ex)
        {
            timedOutAnywhere = ex is TimeoutException;
            logger.Warning(ex, "The key protector list of {Drive} could not be re-read after the suspend", driveLetter);
            return NotConfirmed(returnValue, before, null, CouldNotSuspend(string.Format(CultureInfo.InvariantCulture, ConfirmationFailedReasonFormat, WmiHelper.Describe(ex))), stopwatch.Elapsed);
        }

        if (reread.ReturnValue != 0)
        {
            logger.Warning("The key protector list of {Drive} returned 0x{Return:X8} after the suspend", driveLetter, reread.ReturnValue);
            return NotConfirmed(returnValue, before, null, CouldNotSuspend(string.Format(CultureInfo.InvariantCulture, ConfirmationNotListedReasonFormat, Hex(reread.ReturnValue))), stopwatch.Elapsed);
        }

        IReadOnlyList<string> protectorsAfter = SortedCopy(reread.Value);
        logger.Information("BitLocker {Drive}: {Before} key protector(s) before the suspend, {After} after", driveLetter, protectorsBefore.Count, protectorsAfter.Count);
        if (!protectorsBefore.SequenceEqual(protectorsAfter, StringComparer.Ordinal))
        {
            return NotConfirmed(returnValue, before, null, CouldNotSuspend(ProtectorsChangedReason), stopwatch.Elapsed);
        }

        return Confirmed(before, null, stopwatch.Elapsed);
    }

    private static string CouldNotSuspend(string reason)
    {
        return string.Format(CultureInfo.InvariantCulture, CouldNotSuspendFormat, reason);
    }

    private static string Hex(uint value)
    {
        return string.Format(CultureInfo.InvariantCulture, ReturnValueFormat, value);
    }

    private static string Label(string format, string driveLetter)
    {
        return string.Format(CultureInfo.InvariantCulture, format, driveLetter);
    }

    private static string SuspendLabel(string driveLetter)
    {
        return string.Format(CultureInfo.InvariantCulture, CallLabelFormat, DisableKeyProtectorsMethod, driveLetter);
    }

    private static SuspendResult NotCalled(int? before, string reason, TimeSpan elapsed)
    {
        return new SuspendResult(false, false, OneRestart, null, false, before, null, BitLockerState.Unknown, NotCalledResultText, CouldNotSuspend(reason), elapsed);
    }

    private static SuspendResult NotConfirmed(uint returnValue, int? before, int? after, string sentence, TimeSpan elapsed)
    {
        string resultText = returnValue == 0 ? SucceededResultText : Hex(returnValue);
        return new SuspendResult(false, true, OneRestart, returnValue, false, before, after, BitLockerState.Unknown, resultText, sentence, elapsed);
    }

    private static SuspendResult Confirmed(int? before, int? after, TimeSpan elapsed)
    {
        return new SuspendResult(true, true, OneRestart, 0, false, before, after, BitLockerState.Suspended, SucceededResultText, null, elapsed);
    }

    private static IReadOnlyList<string> SortedCopy(IReadOnlyList<string>? ids)
    {
        return (ids ?? Array.Empty<string>()).Order(StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// The WMI implementation of <see cref="IEncryptableVolumeSuspendMethods"/> for the one volume
    /// the query returned. The protection status re-read (Get, a round trip) and the suspend (the
    /// fetch of the parameter definition, which WMI makes on its first use for an object, together
    /// with the invocation) each run whole inside WmiHelper.RunBounded, with the parameter objects
    /// created and disposed inside the bounded delegate so only plain values leave it. The
    /// protector list is read through <see cref="BitLockerInspector.MethodsFor"/>, the inspector's
    /// read-only seam for the same volume.
    /// </summary>
    private sealed class WmiVolumeSuspendMethods : IEncryptableVolumeSuspendMethods
    {
        private readonly ManagementObject _volume;
        private readonly IEncryptableVolumeMethods _readers;
        private readonly string _driveLetter;

        public WmiVolumeSuspendMethods(ManagementObject volume, string driveLetter)
        {
            _volume = volume;
            _readers = BitLockerInspector.MethodsFor(volume, driveLetter);
            _driveLetter = driveLetter;
        }

        public int? ReadProtectionStatus()
        {
            return WmiHelper.RunBounded(
                () =>
                {
                    _volume.Get();
                    return WmiValues.ToNullableInt(WmiHelper.GetProperty(_volume, ProtectionStatusProperty));
                },
                WmiHelper.QueryTimeout,
                Label(ProtectionStatusLabelFormat, _driveLetter));
        }

        public VolumeMethodResult<IReadOnlyList<string>> GetKeyProtectors()
        {
            return _readers.GetKeyProtectors();
        }

        public uint SuspendKeyProtectors(uint disableCount)
        {
            if (disableCount != OneRestart)
            {
                throw new ArgumentOutOfRangeException(nameof(disableCount), disableCount, OneRestartOnlyMessage);
            }

            return WmiHelper.RunBounded(
                () =>
                {
                    using ManagementBaseObject inParameters = _volume.GetMethodParameters(DisableKeyProtectorsMethod);
                    inParameters["DisableCount"] = OneRestart;
                    using ManagementBaseObject outParameters = _volume.InvokeMethod(DisableKeyProtectorsMethod, inParameters, new InvokeMethodOptions());
                    return BitLockerInspector.ReturnValue(outParameters, MissingReturnValue);
                },
                WmiHelper.QueryTimeout,
                SuspendLabel(_driveLetter));
        }
    }
}
