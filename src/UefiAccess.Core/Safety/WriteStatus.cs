// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Core.Native;

namespace UefiAccess.Core.Safety;

/// <summary>
/// The class of a firmware write's result, derived from the Win32 error code the call left behind
///. The NTSTATUS observed beside the code is recorded, never used to
/// derive the class, because which Win32 code Windows produces for each EFI status is expected,
/// not documented.
/// </summary>
public enum WriteStatus
{
    /// <summary>The call returned success; the verification read says whether the firmware holds the new value.</summary>
    Success,

    /// <summary>Error 1314: SeSystemEnvironmentPrivilege was not held at the moment of the call.</summary>
    PrivilegeNotHeld,

    /// <summary>Error 1: the firmware does not implement the variable operation at run time (S1 already confirmed UEFI, so not legacy BIOS).</summary>
    NotImplemented,

    /// <summary>Error 19: the variable is write-protected in the firmware.</summary>
    WriteProtected,

    /// <summary>Error 5: the firmware refused the write (a security violation or a locked variable).</summary>
    AccessDenied,

    /// <summary>Error 50: the firmware does not support the operation at run time.</summary>
    NotSupported,

    /// <summary>Error 87: the firmware rejected the request as invalid (attributes or size).</summary>
    InvalidParameter,

    /// <summary>Error 203: the variable to update or delete was not found by the firmware.</summary>
    NotFound,

    /// <summary>Error 1470: the firmware's variable store has no room for the change; a restart may reclaim space.</summary>
    NoNvramRoom,

    /// <summary>Errors 8, 14 and 1450: Windows could not get the kernel memory or resources for the call (not NVRAM space).</summary>
    SystemResources,

    /// <summary>Errors 31, 998, 1117, every code without a class of its own, and a failed call that left the code 0: the firmware reported an error Windows did not name.</summary>
    FirmwareError,
}

/// <summary>
/// What one write call returned, as the writer observed it: the Win32 error with its name and
/// text, the NTSTATUS read beside it (with the source that produced it, never derived from the
/// Win32 code), the class, and the time the call took.
/// </summary>
/// <param name="Succeeded">True when the call returned success.</param>
/// <param name="Win32Error">The Win32 error code, 0 on success.</param>
/// <param name="Win32Name">The winerror.h name of the code, or an empty string on success or for a code without a fixed name.</param>
/// <param name="Win32Text">The fixed text of the code, or an empty string on success.</param>
/// <param name="NtStatus">The NTSTATUS observed after a failed call; null on success.</param>
/// <param name="NtStatusText">The description of <paramref name="NtStatus"/>; null on success.</param>
/// <param name="NtStatusSource">How the NTSTATUS was obtained: the name of the read that produced it (with a "may be stale" remark when it is not the status behind the Win32 code), or "not observed".</param>
/// <param name="Status">The class of the result.</param>
/// <param name="Elapsed">How long the call took.</param>
public sealed record WriteOutcome(
    bool Succeeded,
    int Win32Error,
    string Win32Name,
    string Win32Text,
    uint? NtStatus,
    string? NtStatusText,
    string NtStatusSource,
    WriteStatus Status,
    TimeSpan Elapsed);

/// <summary>
/// The Win32-to-class mapping of task 3.6 and the sentence for each class. Core owns the
/// sentences because the CLI prints them on standard error and the GUI shows them in Status. A
/// sentence that says "Nothing was changed" is stated only after the verification read confirmed
/// it; when the read shows the new bytes despite the error the transaction uses
/// <see cref="ChangedDespiteErrorFormat"/> instead.
/// </summary>
public static class WriteStatusMapping
{
    /// <summary>Error 19.</summary>
    public const string WriteProtectedSentence = "The firmware refused the change: the variable is write-protected (error 19). Nothing was changed.";

    /// <summary>Error 5.</summary>
    public const string AccessDeniedSentence = "The firmware refused the change: access denied (error 5). Nothing was changed.";

    /// <summary>Error 50.</summary>
    public const string NotSupportedSentence = "The firmware does not support this change at run time (error 50). Nothing was changed.";

    /// <summary>Error 87.</summary>
    public const string InvalidParameterSentence = "The firmware rejected the request as invalid (error 87): the attributes or size did not match. Nothing was changed.";

    /// <summary>Error 203.</summary>
    public const string NotFoundSentence = "The variable was not found by the firmware (error 203). Nothing was changed.";

    /// <summary>Error 1470.</summary>
    public const string NoNvramRoomSentence = "The firmware has no room left for the change (error 1470). Nothing was changed. Restart the machine and read the variables again before trying once more.";

    /// <summary>Errors 8, 14 and 1450; {0} the code, {1} its text.</summary>
    public const string SystemResourcesFormat = "Windows could not get the resources for the call (error {0}: {1}). Nothing was changed.";

    /// <summary>Error 1314. The process is elevated and S1 enabled the privilege, so "start as administrator" would be wrong advice.</summary>
    public const string PrivilegeNotHeldSentence = "Windows refused the write: the privilege was not held at the moment of the call (error 1314). Nothing was changed. The record shows the privilege state.";

    /// <summary>Error 1.</summary>
    public const string NotImplementedSentence = "The firmware does not implement this variable operation at run time (error 1). Nothing was changed.";

    /// <summary>Error 998, named by Microsoft as the firmware-specific failure for a GUID namespace the firmware does not know.</summary>
    public const string NamespaceUnknownSentence = "The firmware does not know the variable's namespace (error 998). Nothing was changed.";

    /// <summary>Errors 31, 1117 and every other code; {0} the code, {1} its text.</summary>
    public const string FirmwareErrorFormat = "The firmware reported an error it did not name (error {0}: {1}). Nothing was changed.";

    /// <summary>The API failed and the verification read shows the new bytes anyway; {0} the code, {1} the snapshot folder.</summary>
    public const string ChangedDespiteErrorFormat = "The firmware reported error {0} but the change is present. Snapshot {1}.";

    /// <summary>The text used in a sentence when a code has neither a fixed text nor a system message.</summary>
    public const string NoDescription = "no description";

    /// <summary>
    /// <see cref="WriteOutcome.NtStatusSource"/> when the NTSTATUS read after the call is not the
    /// one Windows turns into the call's Win32 code (or is not in the table): the thread's last
    /// NTSTATUS is not cleared before the call, so it may be left over from an earlier failure.
    /// {0} is the read that produced it, {1} the Win32 code.
    /// </summary>
    public const string NtStatusMayBeStaleFormat = "{0}; may be stale: not known to be the status behind error {1}";

    /// <summary>Classifies the Win32 error code a write call left behind (0 is <see cref="WriteStatus.Success"/>).</summary>
    /// <param name="win32Error">The Win32 error code.</param>
    /// <returns>The class.</returns>
    public static WriteStatus FromWin32(int win32Error)
    {
        return win32Error switch
        {
            0 => WriteStatus.Success,
            NativeConstants.ERROR_PRIVILEGE_NOT_HELD => WriteStatus.PrivilegeNotHeld,
            NativeConstants.ERROR_INVALID_FUNCTION => WriteStatus.NotImplemented,
            NativeConstants.ERROR_WRITE_PROTECT => WriteStatus.WriteProtected,
            NativeConstants.ERROR_ACCESS_DENIED => WriteStatus.AccessDenied,
            NativeConstants.ERROR_NOT_SUPPORTED => WriteStatus.NotSupported,
            NativeConstants.ERROR_INVALID_PARAMETER => WriteStatus.InvalidParameter,
            NativeConstants.ERROR_ENVVAR_NOT_FOUND => WriteStatus.NotFound,
            NativeConstants.ERROR_NO_NVRAM_RESOURCES => WriteStatus.NoNvramRoom,
            NativeConstants.ERROR_NOT_ENOUGH_MEMORY or NativeConstants.ERROR_OUTOFMEMORY or NativeConstants.ERROR_NO_SYSTEM_RESOURCES => WriteStatus.SystemResources,
            _ => WriteStatus.FirmwareError,
        };
    }

    /// <summary>
    /// The sentence of a failed outcome, for the Status box and standard error. Use it only after
    /// the verification read confirmed that the firmware still holds the old value; otherwise
    /// <see cref="ChangedDespiteErrorFormat"/> applies.
    /// </summary>
    /// <param name="outcome">A failed outcome.</param>
    /// <returns>The sentence.</returns>
    /// <exception cref="ArgumentException">The outcome succeeded, so there is no refusal to describe.</exception>
    public static string Sentence(WriteOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome.Succeeded || outcome.Status == WriteStatus.Success)
        {
            throw new ArgumentException("a successful outcome has no refusal sentence", nameof(outcome));
        }

        return Sentence(outcome.Status, outcome.Win32Error, outcome.Win32Text);
    }

    /// <summary>The sentence of a failed class, given the code and its text.</summary>
    /// <param name="status">The class; not <see cref="WriteStatus.Success"/>.</param>
    /// <param name="win32Error">The Win32 error code.</param>
    /// <param name="win32Text">The code's text; an empty string when there is none.</param>
    /// <returns>The sentence.</returns>
    /// <exception cref="ArgumentException">The class is <see cref="WriteStatus.Success"/>.</exception>
    public static string Sentence(WriteStatus status, int win32Error, string win32Text)
    {
        string code = win32Error.ToString(CultureInfo.InvariantCulture);
        string text = string.IsNullOrWhiteSpace(win32Text) ? NoDescription : win32Text.Trim();
        return status switch
        {
            WriteStatus.WriteProtected => WriteProtectedSentence,
            WriteStatus.AccessDenied => AccessDeniedSentence,
            WriteStatus.NotSupported => NotSupportedSentence,
            WriteStatus.InvalidParameter => InvalidParameterSentence,
            WriteStatus.NotFound => NotFoundSentence,
            WriteStatus.NoNvramRoom => NoNvramRoomSentence,
            WriteStatus.SystemResources => string.Format(CultureInfo.InvariantCulture, SystemResourcesFormat, code, text),
            WriteStatus.PrivilegeNotHeld => PrivilegeNotHeldSentence,
            WriteStatus.NotImplemented => NotImplementedSentence,
            WriteStatus.FirmwareError when win32Error == NativeConstants.ERROR_NOACCESS => NamespaceUnknownSentence,
            WriteStatus.FirmwareError => string.Format(CultureInfo.InvariantCulture, FirmwareErrorFormat, code, text),
            _ => throw new ArgumentException("a successful outcome has no refusal sentence", nameof(status)),
        };
    }

    /// <summary>The sentence when the API failed but the verification read shows the new bytes.</summary>
    /// <param name="win32Error">The Win32 error code the call returned.</param>
    /// <param name="snapshotFolder">The transaction's snapshot folder.</param>
    /// <returns>The sentence.</returns>
    public static string ChangedDespiteErrorSentence(int win32Error, string snapshotFolder)
    {
        return string.Format(CultureInfo.InvariantCulture, ChangedDespiteErrorFormat, win32Error, snapshotFolder);
    }

    /// <summary>Builds the outcome of a successful call: no error, no NTSTATUS observed.</summary>
    /// <param name="elapsed">How long the call took.</param>
    /// <returns>The outcome.</returns>
    public static WriteOutcome Succeeded(TimeSpan elapsed)
    {
        return new WriteOutcome(true, 0, string.Empty, string.Empty, null, null, FirmwareVariableWriter.NtStatusNotObserved, WriteStatus.Success, elapsed);
    }

    /// <summary>
    /// Builds the outcome of a failed call from the raw numbers the writer observed: the Win32
    /// code is classified and named with its write text, the NTSTATUS is described and attributed
    /// to its source. Both numbers are kept as read; when the NTSTATUS is not the one Windows turns
    /// into the Win32 code, the source says it may be stale (<see cref="NtStatusMayBeStaleFormat"/>).
    /// A failed call that left the code 0 is a <see cref="WriteStatus.FirmwareError"/>, never a
    /// success.
    /// </summary>
    /// <param name="win32Error">The Win32 error code the call left behind.</param>
    /// <param name="ntStatus">The NTSTATUS read beside it.</param>
    /// <param name="ntStatusSource">The name of the read that produced the NTSTATUS.</param>
    /// <param name="elapsed">How long the call took.</param>
    /// <returns>The outcome.</returns>
    public static WriteOutcome Failed(int win32Error, uint ntStatus, string ntStatusSource, TimeSpan elapsed)
    {
        string source = NativeError.NtStatusCorrespondsTo(ntStatus, win32Error)
            ? ntStatusSource
            : string.Format(CultureInfo.InvariantCulture, NtStatusMayBeStaleFormat, ntStatusSource, win32Error);
        return new WriteOutcome(
            false,
            win32Error,
            NativeError.Win32Name(win32Error),
            NativeError.Win32WriteText(win32Error),
            ntStatus,
            NativeError.DescribeWriteNtStatus(ntStatus),
            source,
            win32Error == 0 ? WriteStatus.FirmwareError : FromWin32(win32Error),
            elapsed);
    }
}
