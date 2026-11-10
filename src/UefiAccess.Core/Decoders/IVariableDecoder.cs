// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Variables;

namespace UefiAccess.Core.Decoders;

/// <summary>
/// Decodes one kind of variable into a <see cref="DecodedVariable"/>. Decoders never throw on
/// malformed data; they set <see cref="DecodedVariable.Error"/> instead.
/// </summary>
public interface IVariableDecoder
{
    /// <summary>Returns true when this decoder understands the variable (by name and vendor GUID).</summary>
    /// <param name="variable">The variable to test.</param>
    /// <returns>True when <see cref="Decode"/> may be called.</returns>
    bool CanDecode(FirmwareVariable variable);

    /// <summary>Decodes the variable, resolving references through <paramref name="lookup"/>.</summary>
    /// <param name="variable">The variable to decode.</param>
    /// <param name="lookup">Snapshot used to resolve referenced variables such as Boot####.</param>
    /// <returns>The decoded variable; its Error is set when the data is malformed.</returns>
    DecodedVariable Decode(FirmwareVariable variable, IVariableLookup lookup);
}
