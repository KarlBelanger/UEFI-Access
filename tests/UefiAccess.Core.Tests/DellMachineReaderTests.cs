// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Serilog.Core;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Settings.Dell;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The machine a Dell read runs on (R1): the manufacturer, the
/// model and the BIOS version, from the two pinned queries, and never the serial number. The
/// reader is composed through its seam with the test's queries, so nothing here reads WMI on this
/// PC, the OptiPlex 7020 itself.
/// </summary>
public sealed class DellMachineReaderTests
{
    private static readonly IReadOnlyDictionary<string, object?>[] NoRows = Array.Empty<IReadOnlyDictionary<string, object?>>();

    [Fact]
    public void FromRows_TakesManufacturerModelAndBiosVersion()
    {
        IReadOnlyDictionary<string, object?> system = Row(("Manufacturer", " Dell Inc. "), ("Model", "OptiPlex 7020"), ("__CLASS", "Win32_ComputerSystem"));
        IReadOnlyDictionary<string, object?> bios = Row(("SMBIOSBIOSVersion", "A18"), ("__CLASS", "Win32_BIOS"));

        DellMachine machine = DellMachineReader.FromRows(system, bios);

        Assert.Equal(new DellMachine("Dell Inc.", "OptiPlex 7020", "A18"), machine);

        // Property names are matched case-insensitively, as WmiHelper.Query returns its rows.
        Assert.Equal(new DellMachine("Dell Inc.", "OptiPlex 7020", "A18"), DellMachineReader.FromRows(Row(("manufacturer", "Dell Inc."), ("MODEL", "OptiPlex 7020")), Row(("smbiosbiosversion", "A18"))));
    }

    [Fact]
    public void FromRows_MissingRows_GiveNulls()
    {
        Assert.Equal(new DellMachine(null, null, null), DellMachineReader.FromRows(null, null));
        Assert.Equal(new DellMachine("LENOVO", null, null), DellMachineReader.FromRows(Row(("Manufacturer", "LENOVO")), null));
        Assert.Equal(new DellMachine(null, null, "A18"), DellMachineReader.FromRows(null, Row(("SMBIOSBIOSVersion", "A18"))));

        // A blank or missing value is not known either.
        Assert.Equal(new DellMachine(null, null, null), DellMachineReader.FromRows(Row(("Manufacturer", "   "), ("Model", null)), Row(("SMBIOSBIOSVersion", string.Empty))));
    }

    [Fact]
    public void Queries_AreThePinnedSelects()
    {
        Assert.Equal("SELECT Manufacturer, Model FROM Win32_ComputerSystem", DellMachineReader.ComputerSystemQuery);
        Assert.Equal("SELECT SMBIOSBIOSVersion FROM Win32_BIOS", DellMachineReader.BiosQuery);
        Assert.Equal(TimeSpan.FromSeconds(60), DellMachineReader.StepTimeout);
        foreach (string query in new[] { DellMachineReader.ComputerSystemQuery, DellMachineReader.BiosQuery })
        {
            Assert.DoesNotContain("Serial", query, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("*", query, StringComparison.Ordinal);
            Assert.DoesNotContain("Enclosure", query, StringComparison.OrdinalIgnoreCase);
        }

        // Read sends exactly these two, in this order, and nothing else.
        Fake fake = new();
        fake.Reader.Read(Budget());
        Assert.Equal(new[] { DellMachineReader.ComputerSystemQuery, DellMachineReader.BiosQuery }, fake.Queries.Select(query => query.Wql));
    }

    [Fact]
    public void Read_BoundsEachQueryByWhatIsLeftOfTheBudget()
    {
        Fake fake = new();

        DellMachine machine = fake.Reader.Read(Budget(TimeSpan.FromSeconds(80)));

        Assert.Equal(new DellMachine("Dell Inc.", "OptiPlex 7020", "A18"), machine);
        Assert.Equal(new[] { TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(40) }, fake.Queries.Select(query => query.Bound));

        Fake full = new();
        full.Reader.Read(Budget());
        Assert.Equal(new[] { TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60) }, full.Queries.Select(query => query.Bound));

        // With nothing left, no query is sent.
        Fake none = new();
        Assert.Equal(new DellMachine(null, null, null), none.Reader.Read(Budget(TimeSpan.FromSeconds(120))));
        Assert.Empty(none.Queries);
    }

    [Fact]
    public void Read_EachFailureLeavesItsPartNull_AndNeverThrows()
    {
        WmiQueryResult absent = new(NoRows, null, true);
        WmiQueryResult failed = new(NoRows, "Win32_BIOS: COMException 0x80041013: Provider load failure", false) { Failure = WmiFailureKind.ProviderLoadFailure };
        WmiQueryResult empty = new(NoRows, null, false);

        Assert.Equal(new DellMachine(null, null, "A18"), new Fake(system: absent).Reader.Read(Budget()));
        Assert.Equal(new DellMachine("Dell Inc.", "OptiPlex 7020", null), new Fake(bios: failed).Reader.Read(Budget()));
        Assert.Equal(new DellMachine(null, null, "A18"), new Fake(system: empty).Reader.Read(Budget()));

        // A timeout or any other exception of one query leaves only that part null.
        DellMachineReader timedOut = new(Logger.None, (wql, _) => wql == DellMachineReader.ComputerSystemQuery ? throw new TimeoutException("WMI query of Win32_ComputerSystem did not complete within 60 seconds") : Answer(Row(("SMBIOSBIOSVersion", "A18"))));
        Assert.Equal(new DellMachine(null, null, "A18"), timedOut.Read(Budget()));
        DellMachineReader throwing = new(Logger.None, (_, _) => throw new InvalidOperationException("scripted failure"));
        Assert.Equal(new DellMachine(null, null, null), throwing.Read(Budget()));
        DellMachineReader returnsNull = new(Logger.None, (_, _) => null!);
        Assert.Equal(new DellMachine(null, null, null), returnsNull.Read(Budget()));
        Assert.Equal(new DellMachine(null, null, null), new Fake().Reader.Read(null!));
    }

    private static DellReadBudget Budget() => Budget(TimeSpan.Zero);

    private static DellReadBudget Budget(TimeSpan elapsed) => new(TimeSpan.FromSeconds(120), () => elapsed);

    private static WmiQueryResult Answer(params IReadOnlyDictionary<string, object?>[] rows) => new(rows, null, false);

    private static IReadOnlyDictionary<string, object?> Row(params (string Name, object? Value)[] values)
    {
        Dictionary<string, object?> row = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, object? value) in values)
        {
            row[name] = value;
        }

        return row;
    }

    /// <summary>The reader composed through its seam over scripted answers (the unit's by default), recording every query and its bound.</summary>
    private sealed class Fake
    {
        public Fake(WmiQueryResult? system = null, WmiQueryResult? bios = null)
        {
            WmiQueryResult systemAnswer = system ?? Answer(Row(("Manufacturer", "Dell Inc."), ("Model", "OptiPlex 7020")));
            WmiQueryResult biosAnswer = bios ?? Answer(Row(("SMBIOSBIOSVersion", "A18")));
            Reader = new DellMachineReader(
                Logger.None,
                (wql, bound) =>
                {
                    Queries.Add((wql, bound));
                    return wql == DellMachineReader.BiosQuery ? biosAnswer : systemAnswer;
                });
        }

        public DellMachineReader Reader { get; }

        public List<(string Wql, TimeSpan Bound)> Queries { get; } = new();
    }
}
