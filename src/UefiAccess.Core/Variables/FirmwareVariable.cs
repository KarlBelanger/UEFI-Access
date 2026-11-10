// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Core.Variables;

/// <summary>A UEFI variable as read at runtime: name, vendor GUID, attributes and raw data.</summary>
/// <param name="Name">Variable name (case-sensitive, UTF-16 in firmware).</param>
/// <param name="VendorGuid">Vendor GUID that scopes the name.</param>
/// <param name="Attributes">EFI variable attributes.</param>
/// <param name="Data">Raw variable data; may be empty for zero-length variables.</param>
public sealed record FirmwareVariable(string Name, Guid VendorGuid, VariableAttributes Attributes, byte[] Data)
{
    /// <summary>
    /// Stable display key "guid:name" with the GUID in lowercase "D" format, for example
    /// "8be4df61-93ca-11d2-aa0d-00e098032b8c:BootOrder". Used in logs, exports, and the GUI.
    /// </summary>
    public string Key => MakeKey(VendorGuid, Name);

    /// <summary>Size of the data in bytes.</summary>
    public int Size => Data is null ? 0 : Data.Length;

    /// <summary>Builds the display key for a GUID and name without creating a variable.</summary>
    /// <param name="vendorGuid">Vendor GUID.</param>
    /// <param name="name">Variable name.</param>
    /// <returns>The key in the same format as <see cref="Key"/>.</returns>
    public static string MakeKey(Guid vendorGuid, string name) => vendorGuid.ToString("D") + ":" + name;
}
