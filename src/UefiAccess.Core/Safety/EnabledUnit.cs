// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Core.Safety;

/// <summary>What the unit check of stage S2 found about this Windows installation.</summary>
public enum UnitMatch
{
    /// <summary>The Windows machine ID could not be read, was empty or was not a GUID: the unit is not recognised (the default, so an unset value refuses).</summary>
    MachineIdNotRead,

    /// <summary>The machine ID was read and is not the one of the M3 test unit.</summary>
    OtherUnit,

    /// <summary>The machine ID hashes to <see cref="PlatformPolicy.EnabledUnitMachineIdSha256"/>: this is the M3 test unit's Windows installation (or a copy of it; see that constant).</summary>
    Confirmed,
}

/// <summary>
/// The unit check of stage S2: the Windows machine ID, read through a read-only seam, compared
/// with the hash of the one unit M3 enables changes on. The public constructor is the production
/// check, the registry read of <see cref="PlatformPolicy.ReadWindowsMachineId"/> against
/// <see cref="PlatformPolicy.EnabledUnitMachineIdSha256"/>, so every check a caller outside Core
/// can build is bound to that constant; only the tests pass another machine ID and its hash, so
/// they never read this unit's registry and never depend on its constant. The check keeps neither
/// the machine ID nor its hash anywhere a record, a log or a document can reach, and never throws.
/// </summary>
public sealed class EnabledUnit
{
    /// <summary>Creates the production check: the Windows machine ID against <see cref="PlatformPolicy.EnabledUnitMachineIdSha256"/>.</summary>
    public EnabledUnit()
        : this(PlatformPolicy.ReadWindowsMachineId, PlatformPolicy.EnabledUnitMachineIdSha256)
    {
    }

    /// <summary>Creates a check over another machine ID source and the hash it must give, for tests.</summary>
    /// <param name="machineId">Returns the machine ID as Windows stores it, or null when it cannot be read.</param>
    /// <param name="expectedSha256">The lowercase hexadecimal SHA-256 of the canonical machine ID of the unit the check accepts.</param>
    internal EnabledUnit(Func<string?> machineId, string expectedSha256)
    {
        ArgumentNullException.ThrowIfNull(machineId);
        ArgumentNullException.ThrowIfNull(expectedSha256);
        MachineIdSource = machineId;
        ExpectedSha256 = expectedSha256;
    }

    /// <summary>Where the machine ID is read from: <see cref="PlatformPolicy.ReadWindowsMachineId"/> for the production check.</summary>
    internal Func<string?> MachineIdSource { get; }

    /// <summary>The hash the canonical machine ID must have: <see cref="PlatformPolicy.EnabledUnitMachineIdSha256"/> for the production check.</summary>
    internal string ExpectedSha256 { get; }

    /// <summary>
    /// Reads the machine ID once and compares its canonical hash (<see cref="PlatformPolicy.MachineIdSha256"/>)
    /// with the expected one, by ordinal equality. A source that throws, returns nothing, an empty
    /// or blank text, or text that is not a GUID gives <see cref="UnitMatch.MachineIdNotRead"/>.
    /// </summary>
    /// <returns>What the check found; never throws.</returns>
    public UnitMatch Check()
    {
        string? machineId;
        try
        {
            machineId = MachineIdSource();
        }
        catch (Exception)
        {
            return UnitMatch.MachineIdNotRead;
        }

        string? hash = PlatformPolicy.MachineIdSha256(machineId);
        if (hash is null)
        {
            return UnitMatch.MachineIdNotRead;
        }

        return string.Equals(hash, ExpectedSha256, StringComparison.Ordinal) ? UnitMatch.Confirmed : UnitMatch.OtherUnit;
    }
}
