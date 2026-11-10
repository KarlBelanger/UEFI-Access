// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Runtime.InteropServices;

namespace UefiAccess.Core.Native;

/// <summary>
/// P/Invoke declarations for ntdll.dll: the read-only enumeration entry point and, since M3, a
/// read of the thread's last NTSTATUS. No firmware write function is declared here; the one write
/// import of the solution is in <see cref="Kernel32"/>.
/// </summary>
internal static class NtDll
{
    /// <summary>
    /// Enumerates the firmware environment variables visible at runtime.
    /// </summary>
    /// <param name="informationClass">
    /// 1 for names only, 2 (<see cref="NativeConstants.SYSTEM_ENVIRONMENT_VALUE_INFORMATION"/>) for names and values.
    /// </param>
    /// <param name="buffer">Output buffer, or <see cref="IntPtr.Zero"/> to query the required size.</param>
    /// <param name="bufferLength">On input the buffer size; on output the number of bytes required or written.</param>
    /// <returns>An NTSTATUS value; <see cref="NativeConstants.STATUS_BUFFER_TOO_SMALL"/> reports the required length.</returns>
    [DllImport("ntdll.dll", ExactSpelling = true)]
    public static extern uint NtEnumerateSystemEnvironmentValuesEx(uint informationClass, IntPtr buffer, ref uint bufferLength);

    /// <summary>
    /// Returns the NTSTATUS value the last failed system call left in the calling thread's
    /// environment block (the value kernel32 converts into the Win32 error code). A read of a
    /// thread field; it changes nothing. The Safety layer's writer records it beside the Win32
    /// error of a failed firmware write, and nothing else calls it.
    /// </summary>
    /// <returns>The thread's last NTSTATUS value.</returns>
    [DllImport("ntdll.dll", ExactSpelling = true)]
    public static extern uint RtlGetLastNtStatus();
}
