// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Runtime.InteropServices;

namespace UefiAccess.Core.Native;

/// <summary>
/// P/Invoke declarations for kernel32.dll: the firmware read, the one firmware write import of the
/// solution (M3; the Safety layer's writer is its only caller, through its own seam), process
/// handles and the system's OEM code page.
/// </summary>
internal static class Kernel32
{
    /// <summary>
    /// Reads a firmware environment variable and its attributes.
    /// </summary>
    /// <param name="name">Variable name (case-sensitive).</param>
    /// <param name="guid">Vendor GUID as a braced, uppercase string, for example "{8BE4DF61-93CA-11D2-AA0D-00E098032B8C}".</param>
    /// <param name="buffer">Buffer that receives the variable data.</param>
    /// <param name="size">Size of <paramref name="buffer"/> in bytes.</param>
    /// <param name="attributes">Receives the EFI variable attributes.</param>
    /// <returns>The number of bytes stored in the buffer, or 0 on failure (call <see cref="Marshal.GetLastWin32Error"/>).</returns>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    public static extern uint GetFirmwareEnvironmentVariableExW(string name, string guid, IntPtr buffer, uint size, out uint attributes);

    /// <summary>
    /// Writes a firmware environment variable with explicit attributes: the one write import in the
    /// solution. Called from exactly one expression, in the Safety layer's writer, and never from a
    /// test. A size of 0 deletes the variable unless an authenticated attribute is passed; the
    /// attributes must equal the ones the variable already carries, or the firmware refuses the call.
    /// </summary>
    /// <param name="name">Variable name (case-sensitive).</param>
    /// <param name="guid">Vendor GUID as a braced, uppercase string, for example "{8BE4DF61-93CA-11D2-AA0D-00E098032B8C}".</param>
    /// <param name="buffer">Buffer holding the new value; only <paramref name="size"/> bytes of it are read.</param>
    /// <param name="size">Number of bytes of <paramref name="buffer"/> to write; 0 deletes the variable.</param>
    /// <param name="attributes">EFI variable attributes to pass with the value.</param>
    /// <returns>True on success; otherwise false (call <see cref="Marshal.GetLastWin32Error"/>).</returns>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetFirmwareEnvironmentVariableExW(string name, string guid, IntPtr buffer, uint size, uint attributes);

    /// <summary>
    /// Retrieves the firmware type of the local computer.
    /// </summary>
    /// <param name="firmwareType">Receives 1 for legacy BIOS, 2 for UEFI (see <see cref="NativeConstants"/>).</param>
    /// <returns>True on success; otherwise false (call <see cref="Marshal.GetLastWin32Error"/>).</returns>
    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetFirmwareType(out uint firmwareType);

    /// <summary>Closes an open object handle such as a process token.</summary>
    /// <param name="handle">The handle to close.</param>
    /// <returns>True on success; otherwise false.</returns>
    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr handle);

    /// <summary>Returns the pseudo handle of the current process. The handle does not need to be closed.</summary>
    /// <returns>A pseudo handle to the current process.</returns>
    [DllImport("kernel32.dll", ExactSpelling = true)]
    public static extern IntPtr GetCurrentProcess();

    /// <summary>
    /// Returns the system's OEM code page: the code page a new console starts with, and so the
    /// encoding a console child process started without a window writes to redirected output.
    /// </summary>
    /// <returns>The code page identifier, for example 437 or 850.</returns>
    [DllImport("kernel32.dll", ExactSpelling = true)]
    public static extern uint GetOEMCP();
}
