// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Runtime.InteropServices;
using UefiAccess.Core.Native;

namespace UefiAccess.Core.Diagnostics;

/// <summary>
/// Maps Win32 error codes and NTSTATUS values returned by the native layer to the one-line
/// messages defined in the M1 design (section 4), extended in M3 with the codes a failed firmware
/// write can leave behind. Every message includes the numeric code.
/// <see cref="DescribeWin32"/> and <see cref="DescribeNtStatus"/> keep the M1 messages of the read
/// path exactly: the codes M1 named are named, and every other code, the M3 ones included, keeps
/// the generic form it had. The M3 names and texts serve the write record: the symbolic name and
/// the text on their own (<see cref="Win32Name"/>, <see cref="Win32Text"/>,
/// <see cref="NtStatusName"/>, <see cref="NtStatusText"/>), the texts a failed write records
/// (<see cref="Win32WriteText"/>, <see cref="NtStatusWriteText"/>), the one-line NTSTATUS of a
/// failed write (<see cref="DescribeWriteNtStatus"/>), and whether an NTSTATUS is the one Windows
/// turns into a given Win32 code (<see cref="NtStatusCorrespondsTo"/>). The names are those of the
/// Windows SDK headers (winerror.h, ntstatus.h); the texts are the M1 texts for the codes M1
/// already knew and the header texts for the codes added in M3, so they read the same on every
/// machine. Three M1 texts give read-path advice (legacy BIOS mode, run elevated) that is wrong
/// after a write call, which runs on a UEFI machine with the privilege enabled at stage S1; the
/// write texts replace them. Only a Win32 code without a fixed text falls back to the system
/// message catalogue, which is localised.
/// </summary>
public static class NativeError
{
    /// <summary>Win32: the operation succeeded (winerror.h ERROR_SUCCESS).</summary>
    private const int ErrorSuccess = 0;

    /// <summary>The Win32 codes with a fixed name and text, in ascending order.</summary>
    private static readonly IReadOnlyDictionary<int, (string Name, string Text)> Win32Table = new Dictionary<int, (string Name, string Text)>
    {
        [ErrorSuccess] = ("ERROR_SUCCESS", "the operation succeeded"),
        [NativeConstants.ERROR_INVALID_FUNCTION] = ("ERROR_INVALID_FUNCTION", "invalid function; the machine booted in legacy BIOS mode or the firmware exposes no runtime variable services"),
        [NativeConstants.ERROR_ACCESS_DENIED] = ("ERROR_ACCESS_DENIED", "access denied"),
        [NativeConstants.ERROR_NOT_ENOUGH_MEMORY] = ("ERROR_NOT_ENOUGH_MEMORY", "not enough memory resources are available to process this command"),
        [NativeConstants.ERROR_OUTOFMEMORY] = ("ERROR_OUTOFMEMORY", "not enough storage is available to complete this operation"),
        [NativeConstants.ERROR_WRITE_PROTECT] = ("ERROR_WRITE_PROTECT", "the media is write protected"),
        [NativeConstants.ERROR_GEN_FAILURE] = ("ERROR_GEN_FAILURE", "a device attached to the system is not functioning"),
        [NativeConstants.ERROR_NOT_SUPPORTED] = ("ERROR_NOT_SUPPORTED", "the request is not supported"),
        [NativeConstants.ERROR_INVALID_PARAMETER] = ("ERROR_INVALID_PARAMETER", "the parameter is incorrect"),
        [NativeConstants.ERROR_INSUFFICIENT_BUFFER] = ("ERROR_INSUFFICIENT_BUFFER", "the buffer is too small for the variable data"),
        [NativeConstants.ERROR_ENVVAR_NOT_FOUND] = ("ERROR_ENVVAR_NOT_FOUND", "the firmware variable was not found"),
        [NativeConstants.ERROR_NOACCESS] = ("ERROR_NOACCESS", "invalid access to memory location"),
        [NativeConstants.ERROR_IO_DEVICE] = ("ERROR_IO_DEVICE", "the request could not be performed because of an I/O device error"),
        [NativeConstants.ERROR_NOT_ALL_ASSIGNED] = ("ERROR_NOT_ALL_ASSIGNED", "the privilege is absent from the process token"),
        [NativeConstants.ERROR_PRIVILEGE_NOT_HELD] = ("ERROR_PRIVILEGE_NOT_HELD", "SeSystemEnvironmentPrivilege is not held; run elevated"),
        [NativeConstants.ERROR_NO_SYSTEM_RESOURCES] = ("ERROR_NO_SYSTEM_RESOURCES", "insufficient system resources exist to complete the requested service"),
        [NativeConstants.ERROR_NO_NVRAM_RESOURCES] = ("ERROR_NO_NVRAM_RESOURCES", "insufficient NVRAM resources exist to complete the requested service; a reboot might be required"),
    };

    /// <summary>
    /// The NTSTATUS values with a fixed name and text, in ascending order, each with the Win32 code
    /// Windows turns it into (RtlNtStatusToDosError), which tells whether a status read after a
    /// failed call belongs to the code the call left.
    /// </summary>
    private static readonly IReadOnlyDictionary<uint, (string Name, string Text, int Win32)> NtStatusTable = new Dictionary<uint, (string Name, string Text, int Win32)>
    {
        [NativeConstants.STATUS_SUCCESS] = ("STATUS_SUCCESS", "the operation succeeded", ErrorSuccess),
        [NativeConstants.STATUS_UNSUCCESSFUL] = ("STATUS_UNSUCCESSFUL", "the requested operation was unsuccessful; the firmware returned an error Windows did not recognize", NativeConstants.ERROR_GEN_FAILURE),
        [NativeConstants.STATUS_NOT_IMPLEMENTED] = ("STATUS_NOT_IMPLEMENTED", "not implemented; the machine booted in legacy BIOS mode", NativeConstants.ERROR_INVALID_FUNCTION),
        [NativeConstants.STATUS_INVALID_PARAMETER] = ("STATUS_INVALID_PARAMETER", "invalid parameter", NativeConstants.ERROR_INVALID_PARAMETER),
        [NativeConstants.STATUS_NO_MEMORY] = ("STATUS_NO_MEMORY", "not enough virtual memory or paging file quota is available to complete the operation", NativeConstants.ERROR_NOT_ENOUGH_MEMORY),
        [NativeConstants.STATUS_ACCESS_DENIED] = ("STATUS_ACCESS_DENIED", "access denied", NativeConstants.ERROR_ACCESS_DENIED),
        [NativeConstants.STATUS_BUFFER_TOO_SMALL] = ("STATUS_BUFFER_TOO_SMALL", "the buffer is too small; the required length was returned", NativeConstants.ERROR_INSUFFICIENT_BUFFER),
        [NativeConstants.STATUS_PRIVILEGE_NOT_HELD] = ("STATUS_PRIVILEGE_NOT_HELD", "SeSystemEnvironmentPrivilege is not held; run elevated", NativeConstants.ERROR_PRIVILEGE_NOT_HELD),
        [NativeConstants.STATUS_INSUFFICIENT_RESOURCES] = ("STATUS_INSUFFICIENT_RESOURCES", "insufficient system resources exist to complete the API", NativeConstants.ERROR_NO_SYSTEM_RESOURCES),
        [NativeConstants.STATUS_MEDIA_WRITE_PROTECTED] = ("STATUS_MEDIA_WRITE_PROTECTED", "the disk cannot be written to because it is write-protected", NativeConstants.ERROR_WRITE_PROTECT),
        [NativeConstants.STATUS_NOT_SUPPORTED] = ("STATUS_NOT_SUPPORTED", "the request is not supported", NativeConstants.ERROR_NOT_SUPPORTED),
        [NativeConstants.STATUS_VARIABLE_NOT_FOUND] = ("STATUS_VARIABLE_NOT_FOUND", "the specified environment variable name was not found", NativeConstants.ERROR_ENVVAR_NOT_FOUND),
        [NativeConstants.STATUS_IO_DEVICE_ERROR] = ("STATUS_IO_DEVICE_ERROR", "the I/O device reported an I/O error", NativeConstants.ERROR_IO_DEVICE),
        [NativeConstants.STATUS_INSUFFICIENT_NVRAM_RESOURCES] = ("STATUS_INSUFFICIENT_NVRAM_RESOURCES", "insufficient NVRAM resources exist to complete the requested service; a reboot might be required", NativeConstants.ERROR_NO_NVRAM_RESOURCES),
    };

    /// <summary>
    /// The texts a failed firmware write records in place of the read path's: the call runs after
    /// stage S1 confirmed UEFI and enabled the privilege, so "legacy BIOS mode" and "run elevated"
    /// would be wrong, and a failed call that left no code is said to be one.
    /// </summary>
    private static readonly IReadOnlyDictionary<int, string> Win32WriteTexts = new Dictionary<int, string>
    {
        [ErrorSuccess] = "the call reported a failure without an error code",
        [NativeConstants.ERROR_INVALID_FUNCTION] = "invalid function; the firmware does not implement this variable operation at run time",
        [NativeConstants.ERROR_PRIVILEGE_NOT_HELD] = "SeSystemEnvironmentPrivilege was not held at the moment of the call",
    };

    /// <summary>The NTSTATUS texts a failed firmware write records in place of the read path's, for the same reason as <see cref="Win32WriteTexts"/>.</summary>
    private static readonly IReadOnlyDictionary<uint, string> NtStatusWriteTexts = new Dictionary<uint, string>
    {
        [NativeConstants.STATUS_NOT_IMPLEMENTED] = "not implemented; the firmware does not implement this variable operation at run time",
        [NativeConstants.STATUS_PRIVILEGE_NOT_HELD] = "SeSystemEnvironmentPrivilege was not held at the moment of the call",
    };

    /// <summary>The Win32 codes the M1 messages name; <see cref="DescribeWin32"/> gives every other code the generic form, as in M1.</summary>
    private static readonly HashSet<int> MilestoneOneWin32Codes = new()
    {
        ErrorSuccess,
        NativeConstants.ERROR_INVALID_FUNCTION,
        NativeConstants.ERROR_ACCESS_DENIED,
        NativeConstants.ERROR_INSUFFICIENT_BUFFER,
        NativeConstants.ERROR_ENVVAR_NOT_FOUND,
        NativeConstants.ERROR_NOT_ALL_ASSIGNED,
        NativeConstants.ERROR_PRIVILEGE_NOT_HELD,
    };

    /// <summary>The NTSTATUS values the M1 messages name; <see cref="DescribeNtStatus"/> gives every other value the generic form, as in M1.</summary>
    private static readonly HashSet<uint> MilestoneOneNtStatusValues = new()
    {
        NativeConstants.STATUS_SUCCESS,
        NativeConstants.STATUS_NOT_IMPLEMENTED,
        NativeConstants.STATUS_INVALID_PARAMETER,
        NativeConstants.STATUS_BUFFER_TOO_SMALL,
        NativeConstants.STATUS_PRIVILEGE_NOT_HELD,
    };

    /// <summary>
    /// Describes a Win32 error code such as the value returned by GetLastError after a
    /// firmware variable or token call, with the M1 message: a code M1 named is named, and every
    /// other code is given as its number and the system's message.
    /// </summary>
    /// <param name="code">The Win32 error code.</param>
    /// <returns>A one-line, human-readable description that always contains the numeric code.</returns>
    public static string DescribeWin32(int code)
    {
        if (MilestoneOneWin32Codes.Contains(code) && Win32Table.TryGetValue(code, out (string Name, string Text) known))
        {
            return known.Name + " (" + code.ToString(CultureInfo.InvariantCulture) + "): " + known.Text;
        }

        string detail = SystemMessage(code);
        if (detail.Length > 0)
        {
            detail = ": " + detail;
        }

        return $"Win32 error {code} (0x{code:X8}){detail}";
    }

    /// <summary>The winerror.h symbolic name of a Win32 error code, or an empty string when the code has no fixed name here.</summary>
    /// <param name="code">The Win32 error code.</param>
    /// <returns>The name, for example "ERROR_WRITE_PROTECT", or an empty string.</returns>
    public static string Win32Name(int code)
    {
        return Win32Table.TryGetValue(code, out (string Name, string Text) known) ? known.Name : string.Empty;
    }

    /// <summary>
    /// The fixed text of a Win32 error code without its name or number, or, when the code has no
    /// fixed text here, the system's message on Windows (localised) and an empty string elsewhere.
    /// </summary>
    /// <param name="code">The Win32 error code.</param>
    /// <returns>The text, or an empty string.</returns>
    public static string Win32Text(int code)
    {
        return Win32Table.TryGetValue(code, out (string Name, string Text) known) ? known.Text : SystemMessage(code);
    }

    /// <summary>
    /// The text a failed firmware write records for a Win32 code: <see cref="Win32Text"/>, except
    /// for 0 (a failed call that left no code), 1 and 1314, whose read-path texts advise legacy
    /// BIOS mode or elevation, which the write's stage S1 already ruled out.
    /// </summary>
    /// <param name="code">The Win32 error code the call left behind.</param>
    /// <returns>The text, or an empty string.</returns>
    public static string Win32WriteText(int code)
    {
        return Win32WriteTexts.TryGetValue(code, out string? text) ? text : Win32Text(code);
    }

    /// <summary>
    /// Describes an NTSTATUS value returned by NtEnumerateSystemEnvironmentValuesEx, with the M1
    /// message: a value M1 named is named, and every other value is given as its hexadecimal code.
    /// </summary>
    /// <param name="status">The NTSTATUS value.</param>
    /// <returns>A one-line, human-readable description that always contains the hexadecimal code.</returns>
    public static string DescribeNtStatus(uint status)
    {
        return MilestoneOneNtStatusValues.Contains(status) && NtStatusTable.TryGetValue(status, out (string Name, string Text, int Win32) known)
            ? Describe(status, known.Name, known.Text)
            : Generic(status);
    }

    /// <summary>
    /// Describes the thread's last NTSTATUS observed after a failed firmware write, for the write
    /// record: every value of the table, the M3 ones included, is named with its write text
    /// (<see cref="NtStatusWriteText"/>), and any other value is given as its hexadecimal code.
    /// </summary>
    /// <param name="status">The NTSTATUS value.</param>
    /// <returns>A one-line, human-readable description that always contains the hexadecimal code.</returns>
    public static string DescribeWriteNtStatus(uint status)
    {
        return NtStatusTable.TryGetValue(status, out (string Name, string Text, int Win32) known)
            ? Describe(status, known.Name, NtStatusWriteText(status))
            : Generic(status);
    }

    /// <summary>The ntstatus.h symbolic name of an NTSTATUS value, or an empty string when the value has no fixed name here.</summary>
    /// <param name="status">The NTSTATUS value.</param>
    /// <returns>The name, for example "STATUS_MEDIA_WRITE_PROTECTED", or an empty string.</returns>
    public static string NtStatusName(uint status)
    {
        return NtStatusTable.TryGetValue(status, out (string Name, string Text, int Win32) known) ? known.Name : string.Empty;
    }

    /// <summary>The fixed text of an NTSTATUS value without its name or number, or an empty string when the value has no fixed text here.</summary>
    /// <param name="status">The NTSTATUS value.</param>
    /// <returns>The text, or an empty string.</returns>
    public static string NtStatusText(uint status)
    {
        return NtStatusTable.TryGetValue(status, out (string Name, string Text, int Win32) known) ? known.Text : string.Empty;
    }

    /// <summary>
    /// The text a failed firmware write records for an NTSTATUS value: <see cref="NtStatusText"/>,
    /// except for STATUS_NOT_IMPLEMENTED and STATUS_PRIVILEGE_NOT_HELD, whose read-path texts
    /// advise legacy BIOS mode or elevation, which the write's stage S1 already ruled out.
    /// </summary>
    /// <param name="status">The NTSTATUS value.</param>
    /// <returns>The text, or an empty string when the value has no fixed text here.</returns>
    public static string NtStatusWriteText(uint status)
    {
        return NtStatusWriteTexts.TryGetValue(status, out string? text) ? text : NtStatusText(status);
    }

    /// <summary>
    /// True when <paramref name="status"/> is a value of the table that Windows turns into
    /// <paramref name="win32Code"/>. The thread's last NTSTATUS is not cleared before a call, so a
    /// failure kernel32 reports through the Win32 code alone leaves an older status behind; a
    /// status that does not correspond to the code, or is not in the table, may be such a stale
    /// value. Nothing is derived from the answer except that remark: both numbers stay as read.
    /// </summary>
    /// <param name="status">The NTSTATUS read after the call.</param>
    /// <param name="win32Code">The Win32 code the call left behind.</param>
    /// <returns>True when the two belong together.</returns>
    public static bool NtStatusCorrespondsTo(uint status, int win32Code)
    {
        return NtStatusTable.TryGetValue(status, out (string Name, string Text, int Win32) known) && known.Win32 == win32Code;
    }

    /// <summary>
    /// Returns true when an NTSTATUS value denotes success or an informational result
    /// (the NT_SUCCESS macro: severity bits 00 or 01).
    /// </summary>
    /// <param name="status">The NTSTATUS value.</param>
    /// <returns>True when the high bit is clear.</returns>
    public static bool IsNtSuccess(uint status) => (status & 0x80000000u) == 0;

    private static string Describe(uint status, string name, string text) => name + " (0x" + status.ToString("X8", CultureInfo.InvariantCulture) + "): " + text;

    private static string Generic(uint status) => $"NTSTATUS 0x{status:X8}";

    /// <summary>The system's message text of a Win32 code on Windows, trimmed; an empty string elsewhere or when there is none.</summary>
    private static string SystemMessage(int code)
    {
        if (!OperatingSystem.IsWindows())
        {
            return string.Empty;
        }

        return Marshal.GetPInvokeErrorMessage(code).Trim();
    }
}
