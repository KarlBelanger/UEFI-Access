// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Core.Variables;

/// <summary>A snapshot of variables that decoders use to resolve references (for example Boot#### from BootOrder).</summary>
public interface IVariableLookup
{
    /// <summary>Finds a variable by name and vendor GUID.</summary>
    /// <param name="name">Variable name (case-sensitive).</param>
    /// <param name="vendorGuid">Vendor GUID.</param>
    /// <returns>The variable, or null when absent.</returns>
    FirmwareVariable? Find(string name, Guid vendorGuid);

    /// <summary>Every variable in the snapshot, sorted by <see cref="FirmwareVariable.Key"/> (ordinal).</summary>
    IReadOnlyList<FirmwareVariable> All { get; }
}
