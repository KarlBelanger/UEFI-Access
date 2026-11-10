// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Core.Variables;

/// <summary>
/// Variable store that serves a fixed set of variables; used by unit tests and the WPF design-time.
/// <see cref="Enumerate"/> reports <see cref="EnumerationMethod.InMemory"/> and returns the
/// snapshot sorted by key; <see cref="Read"/> matches the name ordinally (case-sensitive) and the
/// GUID exactly. Never throws.
/// </summary>
public sealed class InMemoryFirmwareVariableStore : IFirmwareVariableStore
{
    private readonly VariableSet _variables;

    /// <summary>Creates a store over the given variables.</summary>
    /// <param name="variables">The variables to serve; null entries are skipped and a null sequence yields an empty store.</param>
    public InMemoryFirmwareVariableStore(IEnumerable<FirmwareVariable> variables)
    {
        _variables = new VariableSet(variables);
    }

    /// <summary>Number of variables in the store.</summary>
    public int Count => _variables.Count;

    /// <inheritdoc />
    public EnumerationResult Enumerate()
    {
        return new EnumerationResult(_variables.All, EnumerationMethod.InMemory, _variables.Count, null, Array.Empty<string>());
    }

    /// <inheritdoc />
    public ReadResult Read(string name, Guid vendorGuid)
    {
        FirmwareVariable? variable = _variables.Find(name, vendorGuid);
        if (variable is null)
        {
            string key = FirmwareVariable.MakeKey(vendorGuid, name ?? string.Empty);
            return new ReadResult(null, ReadStatus.NotFound, "variable " + key + " is not in the in-memory store", 0);
        }

        return new ReadResult(variable, ReadStatus.Success, null, variable.Size);
    }
}
