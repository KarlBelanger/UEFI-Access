// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Platform;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The Dell Command | Monitor part of the Dell interface probe, without WMI: which of the four
/// defined classes answer an instance query (a class definition alone proves nothing when the
/// provider is stopped), how a timeout stops the remaining queries, and how the sample rows map
/// the value codes to their PossibleValuesDescription names. The queries run through a recorded
/// fake in place of WmiHelper.
/// </summary>
public sealed class DellInterfaceProbeTests
{
    private static readonly string[] AllClasses = { "DCIM_BIOSEnumeration", "DCIM_BIOSString", "DCIM_BIOSInteger", "DCIM_BIOSService" };

    [Fact]
    public void QueryDcmClasses_EveryClassAnswers_AllRespondAndTheSamplesCarryNames()
    {
        RecordedQueries queries = new();
        queries.Rows["DCIM_BIOSEnumeration"] = new[]
        {
            Row("Secure Boot", new[] { "2" }, new[] { "1", "2" }, new[] { "Disabled", "Enabled" }),
            Row("Boot List Option", new[] { "2" }, new[] { "1", "2" }, new[] { "Legacy", "UEFI" }),
        };
        queries.Rows["DCIM_BIOSString"] = new[] { Row("Asset Tag", null, null, null) };
        queries.Rows["DCIM_BIOSInteger"] = new[] { Row("Auto On Hour", null, null, null) };
        queries.Rows["DCIM_BIOSService"] = new[] { Row("DCIM:BIOSService", null, null, null) };
        List<DellAttributeSample> samples = new();
        List<string> errors = new();

        IReadOnlyList<string> responding = DellInterfaceProbe.QueryDcmClasses(AllClasses, queries.Run, samples, errors);

        Assert.Equal(AllClasses, responding);
        Assert.Empty(errors);
        Assert.Equal(
            new[]
            {
                ("SELECT AttributeName, CurrentValue, PossibleValues, PossibleValuesDescription FROM DCIM_BIOSEnumeration", 3),
                ("SELECT * FROM DCIM_BIOSString", 1),
                ("SELECT * FROM DCIM_BIOSInteger", 1),
                ("SELECT * FROM DCIM_BIOSService", 1),
            },
            queries.Calls);
        Assert.Equal(2, samples.Count);
        DellAttributeSample secureBoot = samples[0];
        Assert.Equal("Secure Boot", secureBoot.Name);
        Assert.Equal("2", secureBoot.CurrentValue);
        Assert.Equal("Enabled", secureBoot.CurrentValueDescription);
        Assert.Equal(new[] { "1", "2" }, secureBoot.PossibleValues);
        Assert.Equal(new[] { "Disabled", "Enabled" }, secureBoot.PossibleValuesDescription);
        Assert.Equal("DCM", secureBoot.Source);
    }

    [Fact]
    public void QueryDcmClasses_DefinedButStoppedProvider_NothingResponds_AndEveryReasonIsReported()
    {
        RecordedQueries queries = new();
        queries.Errors["DCIM_BIOSEnumeration"] = "DCIM_BIOSEnumeration: ManagementException ProviderLoadFailure: Provider load failure";
        queries.Rows["DCIM_BIOSString"] = Array.Empty<IReadOnlyDictionary<string, object?>>();
        queries.Absent.Add("DCIM_BIOSInteger");
        queries.Throws.Add("DCIM_BIOSService");
        List<DellAttributeSample> samples = new();
        List<string> errors = new();

        IReadOnlyList<string> responding = DellInterfaceProbe.QueryDcmClasses(AllClasses, queries.Run, samples, errors);

        Assert.Empty(responding);
        Assert.Empty(samples);
        Assert.Equal(
            new[]
            {
                "DCIM_BIOSEnumeration: ManagementException ProviderLoadFailure: Provider load failure",
                "DCIM_BIOSString: the class is defined but returned no instances",
                "DCIM_BIOSInteger: the class is defined but could not be queried",
                "DCIM_BIOSService: InvalidOperationException: scripted failure",
            },
            errors);
    }

    [Fact]
    public void QueryDcmClasses_AfterATimeout_TheRemainingClassesAreNotQueried()
    {
        RecordedQueries queries = new();
        queries.Rows["DCIM_BIOSEnumeration"] = new[] { Row("Secure Boot", new[] { "2" }, new[] { "1", "2" }, new[] { "Disabled", "Enabled" }) };
        queries.TimesOut.Add("DCIM_BIOSString");
        List<DellAttributeSample> samples = new();
        List<string> errors = new();

        IReadOnlyList<string> responding = DellInterfaceProbe.QueryDcmClasses(AllClasses, queries.Run, samples, errors);

        Assert.Equal(new[] { "DCIM_BIOSEnumeration" }, responding);
        Assert.Equal(2, queries.Calls.Count);
        Assert.Equal(
            new[]
            {
                "DCIM_BIOSString: WMI query of DCIM_BIOSString did not complete within 60 seconds",
                "DCIM_BIOSInteger: not queried, because the query of DCIM_BIOSString timed out",
                "DCIM_BIOSService: not queried, because the query of DCIM_BIOSString timed out",
            },
            errors);
        Assert.Equal(TimeSpan.FromSeconds(60), DellInterfaceProbe.DcmQueryTimeout);
    }

    [Fact]
    public void QueryDcmClasses_OnlyQueriesTheDefinedClasses()
    {
        RecordedQueries queries = new();
        queries.Rows["DCIM_BIOSService"] = new[] { Row("DCIM:BIOSService", null, null, null) };
        List<string> errors = new();

        IReadOnlyList<string> responding = DellInterfaceProbe.QueryDcmClasses(new[] { "DCIM_BIOSService" }, queries.Run, new List<DellAttributeSample>(), errors);

        Assert.Equal(new[] { "DCIM_BIOSService" }, responding);
        Assert.Single(queries.Calls);
        Assert.Empty(errors);
    }

    [Fact]
    public void ToSample_ScalarValuesAndMissingDescriptions_StayUsable()
    {
        DellAttributeSample sample = DellInterfaceProbe.ToSample(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["AttributeName"] = "Fast Boot",
            ["CurrentValue"] = "3",
            ["PossibleValues"] = new[] { "1", "2", "3" },
        });

        Assert.Equal("Fast Boot", sample.Name);
        Assert.Equal("3", sample.CurrentValue);
        Assert.Null(sample.CurrentValueDescription);
        Assert.Equal(new[] { "1", "2", "3" }, sample.PossibleValues);
        Assert.Empty(sample.PossibleValuesDescription);
    }

    [Fact]
    public void ToSample_BlankElementsKeepTheListsAligned()
    {
        DellAttributeSample sample = DellInterfaceProbe.ToSample(Row("SATA Operation", new[] { "3" }, new[] { "1", " ", "3" }, new[] { "Disabled", "AHCI", "RAID On" }));

        Assert.Equal(new[] { "1", string.Empty, "3" }, sample.PossibleValues);
        Assert.Equal("RAID On", sample.CurrentValueDescription);
    }

    [Theory]
    [InlineData(new[] { "2" }, "Enabled")]
    [InlineData(new[] { "1", "2" }, "Disabled, Enabled")]
    [InlineData(new[] { "9" }, null)]
    [InlineData(new[] { "" }, null)]
    public void DescribeValues_NamesEachCurrentCodeByIndex(string[] current, string? expected)
    {
        Assert.Equal(expected, DellInterfaceProbe.DescribeValues(current, new[] { "1", "2" }, new[] { "Disabled", "Enabled" }));
    }

    [Fact]
    public void DescribeValues_ADescriptionListShorterThanTheCodes_GivesNoName()
    {
        Assert.Null(DellInterfaceProbe.DescribeValues(new[] { "2" }, new[] { "1", "2" }, new[] { "Disabled" }));
        Assert.Equal("Enabled", DellInterfaceProbe.DescribeValues(new[] { "enabled" }, new[] { "Disabled", "Enabled" }, new[] { "Off", "Enabled" }));
    }

    private static IReadOnlyDictionary<string, object?> Row(string name, string[]? current, string[]? possible, string[]? descriptions)
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["AttributeName"] = name,
            ["CurrentValue"] = current,
            ["PossibleValues"] = possible,
            ["PossibleValuesDescription"] = descriptions,
        };
    }

    /// <summary>A recorded stand-in for the bounded WMI query: scripted rows, errors, absence, timeouts and throws per class.</summary>
    private sealed class RecordedQueries
    {
        public Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> Rows { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> Errors { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Absent { get; } = new(StringComparer.Ordinal);

        public HashSet<string> TimesOut { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Throws { get; } = new(StringComparer.Ordinal);

        public List<(string Wql, int MaxInstances)> Calls { get; } = new();

        public DcmQueryOutcome Run(string wql, string label, int maxInstances)
        {
            Calls.Add((wql, maxInstances));
            IReadOnlyList<IReadOnlyDictionary<string, object?>> none = Array.Empty<IReadOnlyDictionary<string, object?>>();
            if (Throws.Contains(label))
            {
                throw new InvalidOperationException("scripted failure");
            }

            if (TimesOut.Contains(label))
            {
                return new DcmQueryOutcome(new WmiQueryResult(none, label + ": WMI query of " + label + " did not complete within 60 seconds", false), true);
            }

            if (Errors.TryGetValue(label, out string? error))
            {
                return new DcmQueryOutcome(new WmiQueryResult(none, error, false), false);
            }

            if (Absent.Contains(label))
            {
                return new DcmQueryOutcome(new WmiQueryResult(none, null, true), false);
            }

            return new DcmQueryOutcome(new WmiQueryResult(Rows.TryGetValue(label, out IReadOnlyList<IReadOnlyDictionary<string, object?>>? rows) ? rows : none, null, false), false);
        }
    }
}
