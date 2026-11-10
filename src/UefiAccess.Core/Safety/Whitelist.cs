// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics.CodeAnalysis;
using UefiAccess.Core.Variables;

namespace UefiAccess.Core.Safety;

/// <summary>What the pipeline may do to a whitelisted variable.</summary>
public enum WritableKind
{
    /// <summary>The entries may be put in a different order; none is added, removed or duplicated (BootOrder, operation W1).</summary>
    ReorderOnly,

    /// <summary>A one-shot value naming an existing entry may be written or removed (BootNext, operations W2 and W3).</summary>
    OneShot,
}

/// <summary>One variable the pipeline may write, and the one way it may write it.</summary>
/// <param name="Name">The variable name, matched ordinally (case-sensitive).</param>
/// <param name="Guid">The vendor GUID, matched exactly.</param>
/// <param name="Kind">The operation the entry allows.</param>
/// <param name="RequiredAttributes">
/// The attributes the variable must carry when it is read, and that are passed back unchanged:
/// 0x7, NV BS RT. Anything else is refused before any call.
/// </param>
public sealed record WhitelistEntry(string Name, Guid Guid, WritableKind Kind, uint RequiredAttributes)
{
    /// <summary>The display key "guid:name", as <see cref="FirmwareVariable.Key"/> renders it.</summary>
    public string Key => FirmwareVariable.MakeKey(Guid, Name);
}

/// <summary>
/// The whitelist at the core-library boundary (task 3.1): exactly two variables under the EFI
/// global GUID, BootOrder (reorder only) and BootNext (one shot, and its removal). Every other
/// variable, the Secure Boot variables and Dell's BootFFFx under their vendor GUID included, is
/// read-only whatever a caller asks. A guard test reads <see cref="Entries"/> by reflection and
/// fails on a third entry, another GUID, other attributes or another kind.
/// </summary>
public static class Whitelist
{
    /// <summary>BootOrder: the order of its entries may change, nothing else.</summary>
    public static readonly WhitelistEntry BootOrder = new("BootOrder", KnownGuids.EfiGlobalVariable, WritableKind.ReorderOnly, RequiredAttributes: 0x7);

    /// <summary>BootNext: a one-time boot choice may be written or removed.</summary>
    public static readonly WhitelistEntry BootNext = new("BootNext", KnownGuids.EfiGlobalVariable, WritableKind.OneShot, RequiredAttributes: 0x7);

    /// <summary>The two entries, and no other; a read-only wrapper, so no caller can replace one by casting the list back to an array.</summary>
    public static IReadOnlyList<WhitelistEntry> Entries { get; } = Array.AsReadOnly(new[] { BootOrder, BootNext });

    /// <summary>Looks a variable up on the whitelist by ordinal name and exact GUID.</summary>
    /// <param name="name">The variable name (case-sensitive).</param>
    /// <param name="guid">The vendor GUID.</param>
    /// <param name="entry">Receives the entry when the variable is whitelisted; otherwise null.</param>
    /// <returns>True when the variable is one of the two.</returns>
    public static bool TryDescribe(string name, Guid guid, [NotNullWhen(true)] out WhitelistEntry? entry)
    {
        entry = null;
        if (name is null)
        {
            return false;
        }

        foreach (WhitelistEntry candidate in Entries)
        {
            if (candidate.Guid == guid && string.Equals(candidate.Name, name, StringComparison.Ordinal))
            {
                entry = candidate;
                return true;
            }
        }

        return false;
    }
}
