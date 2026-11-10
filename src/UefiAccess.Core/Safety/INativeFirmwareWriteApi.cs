// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Core.Safety;

/// <summary>
/// The native entry points the writer depends on, behind a seam of its own so that the read seam
/// (<c>INativeFirmwareApi</c>) cannot write and the writer's outcome logic can be exercised with a
/// fake on any host. The production implementation, <see cref="WindowsNativeFirmwareWriteApi"/>,
/// forwards to the declarations under Native/ and is referenced only by the two composition
/// roots; the test project never names it.
/// </summary>
internal interface INativeFirmwareWriteApi
{
    /// <summary>The firmware write: one variable, its vendor GUID, the value and the attributes to pass with it.</summary>
    /// <param name="name">Variable name.</param>
    /// <param name="guid">Vendor GUID as braced uppercase text.</param>
    /// <param name="buffer">Buffer holding the value; only <paramref name="size"/> bytes are read.</param>
    /// <param name="size">Number of bytes to write; 0 deletes the variable.</param>
    /// <param name="attributes">The EFI variable attributes.</param>
    /// <returns>True on success; false on failure (see <see cref="GetLastError"/> and <see cref="GetLastNtStatus"/>).</returns>
    bool Write(string name, string guid, IntPtr buffer, uint size, uint attributes);

    /// <summary>The Win32 error code left by the last <see cref="Write"/> call.</summary>
    /// <returns>The Win32 error code.</returns>
    int GetLastError();

    /// <summary>The NTSTATUS value the last failed system call left in the calling thread, read after a failed <see cref="Write"/>.</summary>
    /// <returns>The NTSTATUS value.</returns>
    uint GetLastNtStatus();
}
