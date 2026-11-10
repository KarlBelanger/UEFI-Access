// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Core.Variables;

/// <summary>
/// An immutable, sorted snapshot of variables with O(1) lookup by name and GUID.
/// <see cref="All"/> is sorted by <see cref="FirmwareVariable.Key"/> using ordinal comparison,
/// which makes exports and tables deterministic.
/// </summary>
public sealed class VariableSet : IVariableLookup
{
    private readonly FirmwareVariable[] _all;
    private readonly Dictionary<(Guid VendorGuid, string Name), FirmwareVariable> _byKey;

    /// <summary>Creates a snapshot from the given variables. Null entries are skipped; a null sequence yields an empty set.</summary>
    /// <param name="variables">The variables to include.</param>
    public VariableSet(IEnumerable<FirmwareVariable> variables)
    {
        IEnumerable<FirmwareVariable> source = variables ?? Enumerable.Empty<FirmwareVariable>();
        _all = source
            .Where(v => v is not null && v.Name is not null)
            .OrderBy(v => v.Key, StringComparer.Ordinal)
            .ToArray();
        _byKey = new Dictionary<(Guid, string), FirmwareVariable>(_all.Length);
        foreach (FirmwareVariable variable in _all)
        {
            // First occurrence wins when the same name/GUID appears twice.
            _byKey.TryAdd((variable.VendorGuid, variable.Name), variable);
        }
    }

    /// <summary>An empty snapshot.</summary>
    public static VariableSet Empty { get; } = new(Array.Empty<FirmwareVariable>());

    /// <inheritdoc />
    public IReadOnlyList<FirmwareVariable> All => _all;

    /// <summary>Number of variables in the snapshot.</summary>
    public int Count => _all.Length;

    /// <inheritdoc />
    public FirmwareVariable? Find(string name, Guid vendorGuid)
    {
        if (name is null)
        {
            return null;
        }

        return _byKey.TryGetValue((vendorGuid, name), out FirmwareVariable? variable) ? variable : null;
    }
}
