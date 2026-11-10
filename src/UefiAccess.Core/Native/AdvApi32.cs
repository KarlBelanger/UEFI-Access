// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Runtime.InteropServices;

namespace UefiAccess.Core.Native;

/// <summary>Locally unique identifier used to identify a privilege (winnt.h LUID).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct LUID
{
    /// <summary>Low-order bits.</summary>
    public uint LowPart;

    /// <summary>High-order bits.</summary>
    public int HighPart;
}

/// <summary>A privilege LUID together with its attributes (winnt.h LUID_AND_ATTRIBUTES).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct LUID_AND_ATTRIBUTES
{
    /// <summary>The privilege identifier.</summary>
    public LUID Luid;

    /// <summary>Privilege attributes, for example <see cref="NativeConstants.SE_PRIVILEGE_ENABLED"/>.</summary>
    public uint Attributes;
}

/// <summary>
/// Token privilege set holding exactly one privilege (winnt.h TOKEN_PRIVILEGES with PrivilegeCount 1).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TOKEN_PRIVILEGES
{
    /// <summary>Number of entries in <see cref="Privileges"/>; always 1 for this layout.</summary>
    public uint PrivilegeCount;

    /// <summary>The single privilege entry.</summary>
    public LUID_AND_ATTRIBUTES Privileges;
}

/// <summary>
/// P/Invoke declarations for advapi32.dll used to enable SeSystemEnvironmentPrivilege.
/// </summary>
internal static class AdvApi32
{
    /// <summary>Opens the access token associated with a process.</summary>
    /// <param name="processHandle">Process handle, typically from <see cref="Kernel32.GetCurrentProcess"/>.</param>
    /// <param name="desiredAccess">Access mask such as TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY.</param>
    /// <param name="tokenHandle">Receives the token handle; close it with <see cref="Kernel32.CloseHandle"/>.</param>
    /// <returns>True on success; otherwise false.</returns>
    [DllImport("advapi32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    /// <summary>Retrieves the LUID that represents a privilege name on the local system.</summary>
    /// <param name="systemName">Null for the local system.</param>
    /// <param name="name">Privilege name such as <see cref="NativeConstants.SE_SYSTEM_ENVIRONMENT_NAME"/>.</param>
    /// <param name="luid">Receives the privilege LUID.</param>
    /// <returns>True on success; otherwise false.</returns>
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool LookupPrivilegeValueW(string? systemName, string name, out LUID luid);

    /// <summary>
    /// Enables or disables privileges in an access token. Returns true even when not every privilege
    /// could be adjusted; callers must check for ERROR_NOT_ALL_ASSIGNED afterwards.
    /// </summary>
    /// <param name="tokenHandle">Token opened with TOKEN_ADJUST_PRIVILEGES.</param>
    /// <param name="disableAllPrivileges">True to disable every privilege; false to apply <paramref name="newState"/>.</param>
    /// <param name="newState">The privileges to adjust.</param>
    /// <param name="bufferLength">Size of the previous-state buffer; 0 when none is supplied.</param>
    /// <param name="previousState">Optional buffer for the previous state; <see cref="IntPtr.Zero"/> when not needed.</param>
    /// <param name="returnLength">Optional pointer that receives the required previous-state size; <see cref="IntPtr.Zero"/> when not needed.</param>
    /// <returns>True when the call itself succeeded; otherwise false.</returns>
    [DllImport("advapi32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AdjustTokenPrivileges(
        IntPtr tokenHandle,
        [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
        ref TOKEN_PRIVILEGES newState,
        uint bufferLength,
        IntPtr previousState,
        IntPtr returnLength);
}
