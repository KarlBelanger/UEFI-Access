// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Settings;
using UefiAccess.Core.Settings.Dell;
using UefiAccess.Core.Tests.Fixtures;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The Dell Command | Monitor provider: the one pinned query,
/// the mapping of the ten agreed rows by Dell's names (never by code) and the classification of
/// every way the interface can fail to answer (DF3 to DF13). The provider is composed through its
/// seam with the test's query and namespace check, so nothing here reads WMI or Dell Command |
/// Monitor on this PC, the OptiPlex 7020 itself (DellSettingsGuardTests.TestsNeverRunTheRealDellReader).
/// </summary>
public sealed class DcmSettingsProviderTests
{
    private static readonly IReadOnlyList<DellCatalogEntry> Catalog = DellAttributeCatalog.Default;

    private static readonly IReadOnlyDictionary<string, object?>[] NoRows = Array.Empty<IReadOnlyDictionary<string, object?>>();

    [Fact]
    public void SettingsQuery_IsTheFixedEnumerationSelect()
    {
        Assert.Equal("SELECT AttributeName, CurrentValue, PossibleValues, PossibleValuesDescription, IsReadOnly FROM DCIM_BIOSEnumeration", DcmSettingsProvider.SettingsQuery);
        Assert.Equal("Dell BIOS settings", DcmSettingsProvider.QueryLabel);
        Assert.Equal(TimeSpan.FromSeconds(60), DcmSettingsProvider.StepTimeout);
        Assert.Equal(DellInterfaceProbe.DcmQueryTimeout, DcmSettingsProvider.StepTimeout);
        Assert.Equal(@"root\dcim\sysman", DellInterfaceProbe.SysmanNamespace);
        Assert.Equal(DellSettingsSource.DellCommandMonitor, new Fake(DcmRowFixtures.UnitA18()).Provider.Source);
    }

    [Fact]
    public void Map_UnitA18_GivesTheTenExpectedTexts()
    {
        Fake fake = new(DcmRowFixtures.UnitA18());

        DellProviderRead read = fake.Provider.Read(Catalog, Budget(), false);

        Assert.True(read.Answered);
        Assert.Empty(read.Problems);
        Assert.Null(read.AllAttributes);
        Assert.Equal(Catalog.Select(entry => entry.Id), read.Settings.Select(setting => setting.Id));
        Assert.All(read.Settings, setting => Assert.Equal(SettingState.Read, setting.State));
        Assert.Equal(DcmRowFixtures.UnitA18Texts, read.Settings.Select(DellSentences.ValueText));
        Assert.Equal(new[] { "2", "2", "4", "1", "2", "2", "2", "3", "1", "2" }, read.Settings.Select(setting => setting.Current!.Token));
        Assert.Equal(
            new[]
            {
                "Legacy, UEFI",
                "Enabled",
                "Disabled, ATA, AHCI",
                "Enabled, Disabled",
                "Deactivated, Activated",
                "Disabled, Enabled",
                "Disabled, Enabled",
                "Disabled, Enabled with PXE, Enabled, Enabled with image server",
                "Disabled, LAN Only, LAN with PXE Boot",
                "Disabled, Enabled",
            },
            read.Settings.Select(setting => string.Join(", ", setting.PossibleValues.Select(value => value.Text))));
        Assert.All(read.Settings, setting =>
        {
            Assert.Equal(PossibleValuesOrigin.Interface, setting.PossibleValuesOrigin);
            Assert.False(setting.DisplayFromVendor);
            DellCatalogEntry entry = DellAttributeCatalog.Find(setting.Id)!;
            Assert.Equal((entry.DcmAttributeName, entry.DisplayName, entry.Category, entry.SetupLocation), (setting.Name, setting.DisplayName, setting.Category, setting.SetupLocation));
        });
        Assert.Equal("Num Lock at boot: Enabled. Possible values: Disabled, Enabled.", DellSentences.RowSentence(read.Settings[9]));

        // One query, bounded by the step timeout; the class answered, so the namespace is not checked.
        Assert.Equal(new[] { TimeSpan.FromSeconds(60) }, fake.QueryBounds);
        Assert.Empty(fake.NamespaceBounds);
    }

    [Fact]
    public void Map_NamesByDescriptionNeverByCode()
    {
        DellProviderRead unit = DcmSettingsProvider.FromOutcome(DcmRowFixtures.Answer(DcmRowFixtures.UnitA18()), null, Catalog, false);
        DellProviderRead swapped = DcmSettingsProvider.FromOutcome(DcmRowFixtures.Answer(DcmRowFixtures.WithSwappedCodes()), null, Catalog, false);

        // Every code of a two-valued row now means the other value; the names, and so the texts, stay.
        Assert.Equal(DcmRowFixtures.UnitA18Texts, swapped.Settings.Select(DellSentences.ValueText));
        Assert.Equal(
            unit.Settings.Select(setting => string.Join(", ", setting.PossibleValues.Select(value => value.Text))),
            swapped.Settings.Select(setting => string.Join(", ", setting.PossibleValues.Select(value => value.Text))));
        Assert.Equal(new[] { "1", "2", "1", "2", "1", "1", "1", "2", "7", "1" }, swapped.Settings.Select(setting => setting.Current!.Token));
        Assert.Equal(unit.Settings.Select(setting => setting.Current!.VendorName), swapped.Settings.Select(setting => setting.Current!.VendorName));
    }

    [Fact]
    public void OppositePolarities_AreReadFromDescriptions()
    {
        // Trusted Platform Module: 1 = Enable; Num Lock: 2 = Enable; TPM activation: 2 = Activate.
        DellProviderRead read = DcmSettingsProvider.FromOutcome(DcmRowFixtures.Answer(DcmRowFixtures.UnitA18()), null, Catalog, false);

        FirmwareSetting tpm = Setting(read, "TpmSecurity");
        FirmwareSetting numLock = Setting(read, "NumLock");
        FirmwareSetting activation = Setting(read, "TpmActivation");
        Assert.Equal(new SettingValue("1", "Enable", "Enabled"), tpm.Current);
        Assert.Equal(new SettingValue("2", "Enable", "Enabled"), numLock.Current);
        Assert.Equal(new SettingValue("2", "Activate", "Activated"), activation.Current);
        Assert.Equal(new[] { new SettingValue("1", "Enable", "Enabled"), new SettingValue("2", "Disable", "Disabled") }, tpm.PossibleValues);
        Assert.Equal(new[] { new SettingValue("1", "Disable", "Disabled"), new SettingValue("2", "Enable", "Enabled") }, numLock.PossibleValues);
    }

    [Fact]
    public void SecureBootWithoutCurrentValue_IsNoValueReportedWithItsNote()
    {
        DellProviderRead read = new Fake(DcmRowFixtures.WithoutSecureBootCurrent()).Provider.Read(Catalog, Budget(), false);

        Assert.True(read.Answered);
        FirmwareSetting secureBoot = Setting(read, "SecureBoot");
        Assert.Equal(SettingState.NoValueReported, secureBoot.State);
        Assert.Null(secureBoot.Current);
        Assert.Equal(DellSentences.SecureBootNoValueNote, secureBoot.Note);
        Assert.Equal(new[] { new SettingValue("2", "Enable", "Enabled") }, secureBoot.PossibleValues);
        Assert.Equal(DellSentences.NoValueReportedValue, DellSentences.ValueText(secureBoot));
        Assert.Equal("Secure Boot: no value reported. Possible values: Enabled. Dell Command Monitor reports no value while Secure Boot is off.", DellSentences.RowSentence(secureBoot));
    }

    [Fact]
    public void SecureBootEnabled_CarriesTheDcmNote()
    {
        DellProviderRead read = DcmSettingsProvider.FromOutcome(DcmRowFixtures.Answer(DcmRowFixtures.UnitA18()), null, Catalog, false);

        FirmwareSetting secureBoot = Setting(read, "SecureBoot");
        Assert.Equal(SettingState.Read, secureBoot.State);
        Assert.Equal(DellSentences.SecureBootDcmNote, secureBoot.Note);
        Assert.Equal("Secure Boot: Enabled. Possible values: Enabled. Dell Command Monitor lists only Enabled; the BIOS setup also offers Disabled.", DellSentences.RowSentence(secureBoot));
        Assert.All(read.Settings.Where(setting => setting.Id != "SecureBoot"), setting => Assert.Null(setting.Note));
    }

    [Fact]
    public void UnknownCode_IsUnnamedAndSpeaksNoNumber()
    {
        DellProviderRead read = new Fake(DcmRowFixtures.WithUnknownCode()).Provider.Read(Catalog, Budget(), false);

        FirmwareSetting numLock = Setting(read, "NumLock");
        Assert.Equal(SettingState.Unnamed, numLock.State);
        Assert.Equal(new SettingValue("9", null, DellSentences.UnnamedValue), numLock.Current);
        Assert.Null(numLock.Note);
        Assert.False(numLock.DisplayFromVendor);
        Assert.Equal("a value Dell does not name", DellSentences.ValueText(numLock));
        string sentence = DellSentences.RowSentence(numLock);
        Assert.Equal("Num Lock at boot: a value Dell does not name. Possible values: Disabled, Enabled.", sentence);
        Assert.DoesNotContain(sentence, char.IsDigit);
    }

    [Fact]
    public void SeveralCurrentCodes_AreNamedOneByOne()
    {
        // Dell Command Monitor allows several current codes: each is named, and they are joined with ", ".
        FirmwareSetting both = Setting(DcmSettingsProvider.FromOutcome(DcmRowFixtures.Answer(DcmRowFixtures.WithSeveralCodes()), null, Catalog, false), "NumLock");
        Assert.Equal(SettingState.Read, both.State);
        Assert.Equal(new SettingValue("1, 2", "Disable, Enable", "Disabled, Enabled"), both.Current);
        Assert.False(both.DisplayFromVendor);
        Assert.Null(both.Note);
        Assert.Equal("Disabled, Enabled", DellSentences.ValueText(both));
        Assert.Equal("Num Lock at boot: Disabled, Enabled. Possible values: Disabled, Enabled.", DellSentences.RowSentence(both));

        // One code without a name makes the whole value one Dell does not name; the codes stay in the token only.
        DellCatalogEntry numLock = DellAttributeCatalog.Find("NumLock")!;
        FirmwareSetting unnamed = DcmSettingsProvider.ToSetting(DcmRowFixtures.Row("Num Lock", DcmRowFixtures.Texts("2", "9"), DcmRowFixtures.Texts("1", "2"), DcmRowFixtures.Texts("Disable", "Enable")), numLock);
        Assert.Equal(SettingState.Unnamed, unnamed.State);
        Assert.Equal(new SettingValue("2, 9", null, DellSentences.UnnamedValue), unnamed.Current);
        Assert.False(unnamed.DisplayFromVendor);
        string sentence = DellSentences.RowSentence(unnamed);
        Assert.Equal("Num Lock at boot: a value Dell does not name. Possible values: Disabled, Enabled.", sentence);
        Assert.DoesNotContain(sentence, char.IsDigit);
    }

    [Fact]
    public void UnmappedDescription_ShowsDellsWordAndIsMarked()
    {
        Capture capture = new();
        DellProviderRead read = new Fake(DcmRowFixtures.WithUnmappedName(), logger: capture.Logger).Provider.Read(Catalog, Budget(), false);

        FirmwareSetting nic = Setting(read, "IntegratedNic");
        Assert.Equal(SettingState.Read, nic.State);
        Assert.True(nic.DisplayFromVendor);
        Assert.Equal(new SettingValue("3", DcmRowFixtures.UnmappedNicName, DcmRowFixtures.UnmappedNicName), nic.Current);
        Assert.Equal("Disabled, Enabled with PXE, Enabled w/Cloud Desktop, Enabled with image server", string.Join(", ", nic.PossibleValues.Select(value => value.Text)));
        Assert.All(read.Settings.Where(setting => setting.Id != "IntegratedNic"), setting => Assert.False(setting.DisplayFromVendor));

        LogEvent warning = Assert.Single(capture.Events, logEvent => logEvent.Level == LogEventLevel.Warning);
        Assert.Contains("IntegratedNic", warning.RenderMessage(), StringComparison.Ordinal);
        Assert.Contains(DcmRowFixtures.UnmappedNicName, warning.RenderMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void MissingAttribute_IsNotReported()
    {
        DellProviderRead read = new Fake(DcmRowFixtures.WithoutNumLock()).Provider.Read(Catalog, Budget(), false);

        Assert.True(read.Answered);
        Assert.Empty(read.Problems);
        FirmwareSetting numLock = Setting(read, "NumLock");
        Assert.Equal(SettingState.NotReported, numLock.State);
        Assert.Null(numLock.Current);
        Assert.Empty(numLock.PossibleValues);
        Assert.Null(numLock.VendorMarksReadOnly);
        Assert.Equal("Num Lock at boot: not reported.", DellSentences.RowSentence(numLock));
        Assert.All(read.Settings.Where(setting => setting.Id != "NumLock"), setting => Assert.Equal(SettingState.Read, setting.State));
    }

    [Fact]
    public void AttributeNameMatch_IsOrdinalThenIgnoreCase()
    {
        // Only another casing: matched case-insensitively.
        IReadOnlyDictionary<string, object?>[] lowerCase =
        {
            DcmRowFixtures.Row("num lock", DcmRowFixtures.Texts("2"), DcmRowFixtures.Texts("1", "2"), DcmRowFixtures.Texts("Disable", "Enable")),
        };
        Assert.Equal("Enabled", DellSentences.ValueText(Setting(DcmSettingsProvider.FromOutcome(DcmRowFixtures.Answer(lowerCase), null, Catalog, false), "NumLock")));

        // Both: the exact name wins, wherever it stands.
        IReadOnlyDictionary<string, object?>[] both =
        {
            DcmRowFixtures.Row("NUM LOCK", DcmRowFixtures.Texts("1"), DcmRowFixtures.Texts("1", "2"), DcmRowFixtures.Texts("Disable", "Enable")),
            DcmRowFixtures.Row("Num Lock", DcmRowFixtures.Texts("2"), DcmRowFixtures.Texts("1", "2"), DcmRowFixtures.Texts("Disable", "Enable")),
        };
        DellProviderRead read = DcmSettingsProvider.FromOutcome(DcmRowFixtures.Answer(both), null, Catalog, false);
        Assert.Equal("Enabled", DellSentences.ValueText(Setting(read, "NumLock")));
        Assert.Equal(SettingState.NotReported, Setting(read, "SecureBoot").State);
    }

    [Fact]
    public void ReadOnlyFlag_IsCarriedAsReported()
    {
        DellProviderRead unit = DcmSettingsProvider.FromOutcome(DcmRowFixtures.Answer(DcmRowFixtures.UnitA18()), null, Catalog, true);
        Assert.All(unit.Settings, setting => Assert.False(setting.VendorMarksReadOnly));
        Assert.True(Assert.Single(unit.AllAttributes!, attribute => attribute.Name == "Computrace").IsReadOnly);

        foreach ((object? reported, bool? carried) in new (object?, bool?)[] { (true, true), (false, false), (null, null), ("True", true), (1, true) })
        {
            IReadOnlyDictionary<string, object?> row = WithValue(DcmRowFixtures.Row("Num Lock", DcmRowFixtures.Texts("2"), DcmRowFixtures.Texts("1", "2"), DcmRowFixtures.Texts("Disable", "Enable")), "IsReadOnly", reported);
            FirmwareSetting numLock = DcmSettingsProvider.ToSetting(row, DellAttributeCatalog.Find("NumLock")!);
            Assert.Equal(carried, numLock.VendorMarksReadOnly);

            // The flag is carried, never spoken (deviation D8).
            Assert.Equal("Num Lock at boot: Enabled. Possible values: Disabled, Enabled.", DellSentences.RowSentence(numLock));
        }
    }

    [Fact]
    public void Absent_WithoutTheNamespace_IsNotInstalled()
    {
        Fake fake = new(Absent(), namespaceExists: false);

        DellProviderRead read = fake.Provider.Read(Catalog, Budget(TimeSpan.FromSeconds(10)), true);

        AssertNotAnswered(read, DellProblemKind.NotInstalled);
        Assert.Equal(@"the namespace root\dcim\sysman is absent", read.Problems[0].Detail);
        Assert.Equal(new[] { TimeSpan.FromSeconds(60) }, fake.QueryBounds);
        Assert.Equal(new[] { TimeSpan.FromSeconds(110) }, fake.NamespaceBounds);
    }

    [Fact]
    public void Absent_WithTheNamespace_IsClassMissing()
    {
        Fake fake = new(Absent(), namespaceExists: true);

        DellProviderRead read = fake.Provider.Read(Catalog, Budget(), false);

        AssertNotAnswered(read, DellProblemKind.ClassMissing);
        Assert.Single(fake.NamespaceBounds);

        // When whether the namespace exists cannot be told, the query failed.
        AssertNotAnswered(new Fake(Absent(), namespaceExists: null).Provider.Read(Catalog, Budget(), false), DellProblemKind.QueryFailed);
    }

    [Fact]
    public void ProviderLoadFailure_IsClassified()
    {
        const string Error = "Dell BIOS settings: COMException 0x80041013: Provider load failure";
        Fake fake = new(Failure(Error, WmiFailureKind.ProviderLoadFailure));

        DellProviderRead read = fake.Provider.Read(Catalog, Budget(), false);

        AssertNotAnswered(read, DellProblemKind.ProviderLoadFailure);
        Assert.Equal(Error, read.Problems[0].Detail);
        Assert.Empty(fake.NamespaceBounds);
    }

    [Fact]
    public void AccessDenied_IsClassified()
    {
        DellProviderRead read = new Fake(Failure("Dell BIOS settings: ManagementException AccessDenied: Access denied", WmiFailureKind.AccessDenied)).Provider.Read(Catalog, Budget(), false);

        AssertNotAnswered(read, DellProblemKind.AccessDenied);
    }

    [Fact]
    public void OtherError_IsQueryFailed()
    {
        AssertNotAnswered(new Fake(Failure("Dell BIOS settings: COMException 0x800706BA: The RPC server is unavailable.", WmiFailureKind.Other)).Provider.Read(Catalog, Budget(), false), DellProblemKind.QueryFailed);
        AssertNotAnswered(new Fake(Failure("Dell BIOS settings: WMI is only available on Windows", WmiFailureKind.None)).Provider.Read(Catalog, Budget(), false), DellProblemKind.QueryFailed);
    }

    [Fact]
    public void ZeroRows_IsNoInstancesNotAnEmptyList()
    {
        // The data service stopped: the class answers with nothing and no error.
        DellProviderRead read = new Fake(NoRows).Provider.Read(Catalog, Budget(), true);

        AssertNotAnswered(read, DellProblemKind.NoInstances);
        Assert.Equal(10, read.Settings.Count);
        Assert.Null(read.AllAttributes);
    }

    [Fact]
    public void Timeout_IsTimedOut()
    {
        const string Error = "Dell BIOS settings: WMI query of Dell BIOS settings did not complete within 60 seconds";
        DcmQueryOutcome bounded = new(new WmiQueryResult(NoRows, Error, false), true);
        DcmQueryOutcome enumeration = new(new WmiQueryResult(NoRows, "Dell BIOS settings: ManagementException Timedout: Timed out", false) { Failure = WmiFailureKind.TimedOut }, false);

        DellProviderRead read = new Fake(bounded).Provider.Read(Catalog, Budget(), false);

        AssertNotAnswered(read, DellProblemKind.TimedOut);
        Assert.Equal(Error, read.Problems[0].Detail);
        Assert.Equal(DellProblemKind.TimedOut, DcmSettingsProvider.Classify(enumeration, null));
        Assert.Equal(DellProblemKind.TimedOut, DcmSettingsProvider.Classify(bounded with { Result = bounded.Result with { Absent = true } }, false));
        Assert.Equal("Dell Command Monitor did not answer within 60 seconds.", DellSentences.Cause(read.Problems[0]));
    }

    [Fact]
    public void Timeout_ThenReadAgain_DoesNotQueryWhileTheFirstIsRunning()
    {
        Assert.Equal(0, DcmSettingsProvider.QueriesInFlight);
        using ManualResetEventSlim release = new(false);
        int queries = 0;
        DcmSettingsProvider provider = new(
            Logger.None,
            _ =>
            {
                Interlocked.Increment(ref queries);
                return DcmSettingsProvider.QueryBounded(
                    () =>
                    {
                        release.Wait(TimeSpan.FromSeconds(30));
                        return new WmiQueryResult(DcmRowFixtures.UnitA18(), null, false);
                    },
                    release.IsSet ? TimeSpan.FromSeconds(30) : TimeSpan.FromMilliseconds(50),
                    Logger.None);
            },
            _ => true);
        try
        {
            // The first query hangs past its bound: a timeout, and the query still runs.
            DellProviderRead first = provider.Read(Catalog, Budget(), false);
            AssertNotAnswered(first, DellProblemKind.TimedOut);
            Assert.Equal(1, DcmSettingsProvider.QueriesInFlight);

            // A second read sends no second query to the hung provider.
            DellProviderRead second = provider.Read(Catalog, Budget(), false);
            AssertNotAnswered(second, DellProblemKind.TimedOut);
            Assert.Equal("an earlier query has not finished", second.Problems[0].Detail);
            Assert.Equal(1, Volatile.Read(ref queries));
        }
        finally
        {
            release.Set();
            Assert.True(SpinWait.SpinUntil(() => DcmSettingsProvider.QueriesInFlight == 0, TimeSpan.FromSeconds(30)), "the released query did not end");
        }

        // Once it has ended, the next read queries again.
        DellProviderRead third = provider.Read(Catalog, Budget(), false);
        Assert.True(third.Answered);
        Assert.Equal(2, Volatile.Read(ref queries));
        Assert.Equal(0, DcmSettingsProvider.QueriesInFlight);
    }

    [Fact]
    public void TimeoutInsideWmiHelper_StaysCounted_SoNoLaterReadQueries()
    {
        // WmiHelper bounds its own connect and enumeration and returns once it gives up on them,
        // within the provider's bound, while the abandoned call may still be running on its pool
        // thread; nothing reports when that ends.
        const string Error = "Dell BIOS settings: TimeoutException: WMI query Dell BIOS settings did not complete within 60 seconds";
        Assert.Equal(0, DcmSettingsProvider.QueriesInFlight);
        int queries = 0;
        DcmSettingsProvider provider = new(
            Logger.None,
            bound =>
            {
                Interlocked.Increment(ref queries);
                return DcmSettingsProvider.QueryBounded(() => new WmiQueryResult(NoRows, Error, false) { Failure = WmiFailureKind.TimedOut }, bound, Logger.None);
            },
            _ => true);
        try
        {
            DellProviderRead first = provider.Read(Catalog, Budget(), false);
            AssertNotAnswered(first, DellProblemKind.TimedOut);
            Assert.Equal(Error, first.Problems[0].Detail);
            Assert.Equal(1, DcmSettingsProvider.QueriesInFlight);

            // No later read of this process sends the hung provider another query.
            for (int read = 0; read < 3; read++)
            {
                DellProviderRead again = provider.Read(Catalog, Budget(), false);
                AssertNotAnswered(again, DellProblemKind.TimedOut);
                Assert.Equal("an earlier query has not finished", again.Problems[0].Detail);
            }

            Assert.Equal(1, Volatile.Read(ref queries));
            Assert.Equal(1, DcmSettingsProvider.QueriesInFlight);
        }
        finally
        {
            ForgetTheQueriesInFlight();
        }

        // A query that answered, or failed in any other way, is counted down as it returns.
        foreach (WmiQueryResult result in new[]
        {
            new WmiQueryResult(DcmRowFixtures.UnitA18(), null, false),
            new WmiQueryResult(NoRows, null, true),
            new WmiQueryResult(NoRows, "Dell BIOS settings: COMException 0x80041013: Provider load failure", false) { Failure = WmiFailureKind.ProviderLoadFailure },
            new WmiQueryResult(NoRows, "Dell BIOS settings: COMException 0x800706BA: The RPC server is unavailable.", false) { Failure = WmiFailureKind.Other },
        })
        {
            DcmQueryOutcome outcome = DcmSettingsProvider.QueryBounded(() => result, TimeSpan.FromSeconds(30), Logger.None);
            Assert.Same(result, outcome.Result);
            Assert.False(outcome.TimedOut);
            Assert.Equal(0, DcmSettingsProvider.QueriesInFlight);
        }
    }

    [Fact]
    public void IncludeAll_KeepsEveryRow_WithoutLoggingTheirValues()
    {
        // A row outside the catalog with tag-like values (synthetic) must reach the enumeration and never the log.
        const string Tag = "TESTTAG7";
        IReadOnlyDictionary<string, object?>[] rows = DcmRowFixtures.UnitA18()
            .Append(DcmRowFixtures.Row("Synthetic Attribute", DcmRowFixtures.Texts(Tag), DcmRowFixtures.Texts(Tag, "1"), DcmRowFixtures.Texts(Tag + "NAME", "Other")))
            .ToArray();
        Capture capture = new();

        DellProviderRead read = new Fake(rows, logger: capture.Logger).Provider.Read(Catalog, Budget(), true);

        Assert.True(read.Answered);
        IReadOnlyList<DellVendorAttribute> all = read.AllAttributes!;
        Assert.Equal(73, all.Count);
        Assert.Equal(rows.Select(row => (string?)row["AttributeName"]), all.Select(attribute => attribute.Name));
        DellVendorAttribute synthetic = all[^1];
        Assert.Equal(new[] { Tag }, synthetic.CurrentValues);
        Assert.Equal(Tag + "NAME", synthetic.CurrentNames);
        Assert.Equal(new[] { Tag, "1" }, synthetic.PossibleValues);
        Assert.Equal(new[] { Tag + "NAME", "Other" }, synthetic.PossibleNames);
        DellVendorAttribute hour = Assert.Single(all, attribute => attribute.Name == "Auto On Hour");
        Assert.Equal(new[] { "0" }, hour.CurrentValues);
        Assert.Null(hour.CurrentNames);
        Assert.Equal("Off", Assert.Single(all, attribute => attribute.Name == "AC Power Recovery Mode").CurrentNames);

        Assert.NotEmpty(capture.Events);
        Assert.All(capture.Rendered, line =>
        {
            Assert.DoesNotContain(Tag, line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Door closed", line, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Read_NeverThrows()
    {
        DcmSettingsProvider throwingQuery = new(Logger.None, _ => throw new InvalidOperationException("scripted failure"), _ => true);
        DcmSettingsProvider throwingCheck = new(Logger.None, _ => Absent(), _ => throw new InvalidOperationException("scripted failure"));

        foreach (DcmSettingsProvider provider in new[] { throwingQuery, throwingCheck })
        {
            DellProviderRead result = provider.Read(Catalog, Budget(), false);
            AssertNotAnswered(result, DellProblemKind.Unexpected);
            Assert.Equal("InvalidOperationException: scripted failure", result.Problems[0].Detail);
        }

        AssertNotAnswered(throwingQuery.Read(Catalog, null!, false), DellProblemKind.Unexpected);

        DellProviderRead withoutCatalog = new Fake(DcmRowFixtures.UnitA18()).Provider.Read(null!, Budget(), false);
        Assert.True(withoutCatalog.Answered);
        Assert.Empty(withoutCatalog.Settings);
    }

    [Fact]
    public void Budget_Exhausted_QueriesNothing()
    {
        Fake fake = new(DcmRowFixtures.UnitA18());

        DellProviderRead read = fake.Provider.Read(Catalog, Budget(TimeSpan.FromSeconds(125)), false);

        // The budget is the whole read's, so its problem is the reader's (DF21).
        AssertNotAnswered(read, DellProblemKind.BudgetExhausted, DellInterface.Reader);
        Assert.Equal("Reading stopped at the time limit of 120 seconds.", DellSentences.Cause(read.Problems[0]));
        Assert.Empty(fake.QueryBounds);
        Assert.Empty(fake.NamespaceBounds);
    }

    [Fact]
    public void Read_BoundsEachStepByWhatIsLeftOfTheBudget()
    {
        TimeSpan elapsed = TimeSpan.FromSeconds(100);
        DellReadBudget budget = new(TimeSpan.FromSeconds(120), () => elapsed);
        Assert.Equal(TimeSpan.FromSeconds(120), budget.Total);
        Assert.Equal(TimeSpan.FromSeconds(20), budget.Remaining);
        Assert.Equal(TimeSpan.FromSeconds(20), budget.Bound(TimeSpan.FromSeconds(60)));
        Assert.Equal(TimeSpan.FromSeconds(5), budget.Bound(TimeSpan.FromSeconds(5)));
        Assert.False(budget.Exhausted);

        Fake fake = new(Absent(), namespaceExists: false);
        fake.Provider.Read(Catalog, budget, false);
        Assert.Equal(new[] { TimeSpan.FromSeconds(20) }, fake.QueryBounds);
        Assert.Equal(new[] { TimeSpan.FromSeconds(20) }, fake.NamespaceBounds);

        elapsed = TimeSpan.FromSeconds(500);
        Assert.Equal(TimeSpan.Zero, budget.Remaining);
        Assert.Equal(TimeSpan.Zero, budget.Bound(TimeSpan.FromSeconds(60)));
        Assert.True(budget.Exhausted);
    }

    private static DellReadBudget Budget() => Budget(TimeSpan.Zero);

    private static DellReadBudget Budget(TimeSpan elapsed) => new(TimeSpan.FromSeconds(120), () => elapsed);

    private static DcmQueryOutcome Absent() => new(new WmiQueryResult(NoRows, null, true), false);

    private static DcmQueryOutcome Failure(string error, WmiFailureKind kind) => new(new WmiQueryResult(NoRows, error, false) { Failure = kind }, false);

    private static FirmwareSetting Setting(DellProviderRead read, string id) => Assert.Single(read.Settings, setting => setting.Id == id);

    private static IReadOnlyDictionary<string, object?> WithValue(IReadOnlyDictionary<string, object?> row, string name, object? value)
    {
        return new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase) { [name] = value };
    }

    /// <summary>
    /// The count of queries in flight set back to none (<see cref="DcmSettingsProvider.QueriesInFlight"/>),
    /// which the product never does for a query that timed out inside WmiHelper, so the tests
    /// after this one start as a new process would.
    /// </summary>
    private static void ForgetTheQueriesInFlight()
    {
        FieldInfo? count = typeof(DcmSettingsProvider).GetField("s_queriesInFlight", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(count);
        count!.SetValue(null, 0);
        Assert.Equal(0, DcmSettingsProvider.QueriesInFlight);
    }

    /// <summary>A read that gave no answer: the one problem of the kind (Dell Command Monitor's unless said), every setting not read, in catalog order, no enumeration.</summary>
    private static void AssertNotAnswered(DellProviderRead read, DellProblemKind kind, DellInterface problemInterface = DellInterface.DellCommandMonitor)
    {
        Assert.False(read.Answered);
        DellInterfaceProblem problem = Assert.Single(read.Problems);
        Assert.Equal(problemInterface, problem.Interface);
        Assert.Equal(kind, problem.Kind);
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
        Assert.Equal(Catalog.Select(entry => entry.Id), read.Settings.Select(setting => setting.Id));
        Assert.All(read.Settings, setting =>
        {
            Assert.Equal(SettingState.NotRead, setting.State);
            Assert.Null(setting.Current);
            Assert.Empty(setting.PossibleValues);
        });
        Assert.Null(read.AllAttributes);
    }

    /// <summary>The provider composed through its seam over a scripted answer, recording the bound of every query and namespace check.</summary>
    private sealed class Fake
    {
        public Fake(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, bool? namespaceExists = null, ILogger? logger = null)
            : this(DcmRowFixtures.Answer(rows), namespaceExists, logger)
        {
        }

        public Fake(DcmQueryOutcome answer, bool? namespaceExists = null, ILogger? logger = null)
        {
            Provider = new(
                logger ?? Logger.None,
                bound =>
                {
                    QueryBounds.Add(bound);
                    return answer;
                },
                bound =>
                {
                    NamespaceBounds.Add(bound);
                    return namespaceExists;
                });
        }

        public DcmSettingsProvider Provider { get; }

        public List<TimeSpan> QueryBounds { get; } = new();

        public List<TimeSpan> NamespaceBounds { get; } = new();
    }

    /// <summary>Keeps every event, and each rendered with its properties as a log file would hold it.</summary>
    private sealed class Capture : ILogEventSink
    {
        private readonly List<LogEvent> _events = new();

        public Capture()
        {
            Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(this).CreateLogger();
        }

        public ILogger Logger { get; }

        public IReadOnlyList<LogEvent> Events
        {
            get
            {
                lock (_events)
                {
                    return _events.ToArray();
                }
            }
        }

        public IEnumerable<string> Rendered => Events.Select(logEvent => logEvent.RenderMessage() + " " + string.Join(" ", logEvent.Properties.Select(pair => pair.Key + "=" + pair.Value)) + " " + logEvent.Exception);

        public void Emit(LogEvent logEvent)
        {
            lock (_events)
            {
                _events.Add(logEvent);
            }
        }
    }
}
