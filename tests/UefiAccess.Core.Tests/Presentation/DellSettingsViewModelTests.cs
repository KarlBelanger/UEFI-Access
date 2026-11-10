// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Input;
using UefiAccess.Core.Baseline;
using UefiAccess.Core.Settings;
using UefiAccess.Core.Settings.Dell;
using UefiAccess.Core.Tests.Fixtures;
using UefiAccess.Presentation.Resources;
using UefiAccess.Presentation.ViewModels;
using Xunit;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// The Dell settings region's view models: the ten rows
/// exist from the start, built from the catalog, the first selected; every read, baseline folder
/// and return to the live view changes their texts in place, raising a change notification only
/// for a text that really changes, and the collection is never changed (Alpha 1 finding A1-6,
/// Alpha 2 finding A2-7); the state line says when and where the values were loaded, or why they
/// could not be; the session cache keeps the last read that answered; no row has an item status or
/// says it is read-only (A2-6); the view model exposes no command. Every result is made from the
/// fixtures by Core's own pure functions; nothing reads a Dell interface.
/// </summary>
public sealed class DellSettingsViewModelTests
{
    private const string FolderName = "sample-baseline";

    /// <summary>The unit's ten rows on A18 through Dell Command Monitor, as the window says them.</summary>
    private static readonly string[] UnitA18Sentences =
    {
        "Boot list option: UEFI. Possible values: Legacy, UEFI.",
        "Secure Boot: Enabled. Possible values: Enabled. Dell Command Monitor lists only Enabled; the BIOS setup also offers Disabled.",
        "SATA operation: AHCI. Possible values: Disabled, ATA, AHCI.",
        "TPM security: Enabled. Possible values: Enabled, Disabled.",
        "TPM activation: Activated. Possible values: Deactivated, Activated.",
        "Virtualization: Enabled. Possible values: Disabled, Enabled.",
        "VT for Direct I/O: Enabled. Possible values: Disabled, Enabled.",
        "Integrated NIC: Enabled. Possible values: Disabled, Enabled with PXE, Enabled, Enabled with image server.",
        "Wake on LAN: Disabled. Possible values: Disabled, LAN Only, LAN with PXE Boot.",
        "Num Lock at boot: Enabled. Possible values: Disabled, Enabled.",
    };

    private static readonly DateTimeOffset RecordedAt = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Rows_AreTheCatalogAtConstruction_AndSayNotLoadedYet()
    {
        DellSettingsViewModel dell = new();

        Assert.Equal(DellAttributeCatalog.Default.Select(entry => entry.Id), dell.Rows.Select(row => row.Id));
        Assert.Equal(DellAttributeCatalog.Default.Select(entry => entry.DisplayName), dell.Rows.Select(row => row.DisplayName));
        Assert.Equal(10, dell.Rows.Count);
        Assert.All(dell.Rows, row =>
        {
            Assert.Equal(Strings.DellNotLoadedYetValue, row.CurrentText);
            Assert.Equal(string.Empty, row.PossibleText);
            Assert.Equal(row.DisplayName + ": not loaded yet.", row.AccessibleName);
        });
        Assert.Equal("Boot list option: not loaded yet.", dell.Rows[0].AccessibleName);
        Assert.Equal(Strings.DellNotLoadedLine, dell.StateText);
        Assert.Equal(DellViewMode.Live, dell.Mode);
        Assert.Null(dell.ArrivalClause);
        Assert.False(dell.IsLoading);
    }

    [Fact]
    public void FirstRowIsSelectedAtConstruction()
    {
        DellSettingsViewModel dell = new();
        List<string> raised = Record(dell);

        Assert.Same(dell.Rows[0], dell.SelectedRow);

        // Two-way bound: the list sets it, once per change.
        dell.SelectedRow = dell.Rows[8];
        dell.SelectedRow = dell.Rows[8];
        Assert.Same(dell.Rows[8], dell.SelectedRow);
        Assert.Equal(new[] { nameof(DellSettingsViewModel.SelectedRow) }, raised);

        // A read keeps the selection.
        dell.ApplyLive(DellResults.CompleteA18());
        Assert.Same(dell.Rows[8], dell.SelectedRow);
    }

    [Fact]
    public void ApplyLive_FirstLoad_UpdatesRowsInPlace_SameInstances()
    {
        DellSettingsViewModel dell = new();
        DellSettingRowViewModel[] before = dell.Rows.ToArray();
        DellSettingsResult result = DellResults.CompleteA18();

        DellApplyOutcome outcome = dell.ApplyLive(result);

        Assert.Equal(DellApplyKind.FirstLoad, outcome.Kind);
        Assert.Empty(outcome.Changes);
        Assert.Null(outcome.KeptReadTime);
        Assert.Equal(before.Length, dell.Rows.Count);
        for (int i = 0; i < before.Length; i++)
        {
            Assert.Same(before[i], dell.Rows[i]);
        }

        Assert.Equal(DcmRowFixtures.UnitA18Texts, dell.Rows.Select(row => row.CurrentText));
        Assert.Equal(UnitA18Sentences, dell.Rows.Select(row => row.AccessibleName));
        Assert.Equal("Disabled, LAN Only, LAN with PXE Boot", dell.Rows[8].PossibleText);
        Assert.Equal(
            "Loaded at " + DellResults.LiveTime(result) + " from Dell Command Monitor on this computer, BIOS A18. This application shows Dell BIOS settings and never changes them; they are changed in the BIOS setup (F2).",
            dell.StateText);
        Assert.Equal(DellViewMode.Live, dell.Mode);
        Assert.Null(dell.ArrivalClause);
    }

    [Fact]
    public void ApplyLive_RaisesPropertyChangedOnlyForChangedValues()
    {
        DellSettingsViewModel dell = new();
        Dictionary<DellSettingRowViewModel, List<string>> raised = RecordRows(dell);

        dell.ApplyLive(DellResults.CompleteA18());

        // At most about 30 property events for the first read: three texts on each of ten rows.
        Assert.All(raised.Values, names => Assert.Equal(
            new[] { nameof(DellSettingRowViewModel.CurrentText), nameof(DellSettingRowViewModel.PossibleText), nameof(DellSettingRowViewModel.AccessibleName) },
            names));
        Assert.Equal(30, raised.Values.Sum(names => names.Count));

        foreach (List<string> names in raised.Values)
        {
            names.Clear();
        }

        dell.ApplyLive(DellResults.NumLockDisabled(DateTimeOffset.Now));

        // Only Num Lock at boot changed, and only its value and sentence: the possible values are the same.
        DellSettingRowViewModel numLock = dell.Rows.Single(row => row.Id == "NumLock");
        Assert.Equal(new[] { nameof(DellSettingRowViewModel.CurrentText), nameof(DellSettingRowViewModel.AccessibleName) }, raised[numLock]);
        Assert.All(raised.Where(pair => !ReferenceEquals(pair.Key, numLock)), pair => Assert.Empty(pair.Value));
        Assert.Equal("Num Lock at boot: Disabled. Possible values: Disabled, Enabled.", numLock.AccessibleName);

        // Update itself reports a change of the value only.
        Assert.False(numLock.Update(numLock.CurrentText, "Disabled", null));
        Assert.True(numLock.Update("Enabled", "Disabled", null));
    }

    [Fact]
    public void ApplyLive_Unchanged_RaisesNoRowEvents()
    {
        DellSettingsViewModel dell = new();
        dell.ApplyLive(DellResults.CompleteA18());
        Dictionary<DellSettingRowViewModel, List<string>> raised = RecordRows(dell);

        DellApplyOutcome outcome = dell.ApplyLive(DellResults.CompleteA18() with { ReadAt = DellResults.CompleteA18().ReadAt.AddMinutes(3) });

        Assert.Equal(DellApplyKind.Unchanged, outcome.Kind);
        Assert.Empty(outcome.Changes);
        Assert.All(raised.Values, names => Assert.Empty(names));
        Assert.Equal(UnitA18Sentences, dell.Rows.Select(row => row.AccessibleName));

        // Showing the live view again, as every live load does, raises nothing on the rows either.
        dell.ShowLive();
        Assert.All(raised.Values, names => Assert.Empty(names));
    }

    [Fact]
    public void ApplyLive_Changed_NamesEachChange()
    {
        DellSettingsViewModel dell = new();
        dell.ApplyLive(DellResults.CompleteA18());

        DellApplyOutcome numLock = dell.ApplyLive(DellResults.NumLockDisabled(DateTimeOffset.Now));

        Assert.Equal(DellApplyKind.Changed, numLock.Kind);
        Assert.Equal(new[] { "Num Lock at boot, now Disabled" }, numLock.Changes);

        DellApplyOutcome tpm = dell.ApplyLive(CliTestHarness.DellFromDcm(DcmRowFixtures.WithTpmDisabled(), DellReadOptions.Default));

        Assert.Equal(DellApplyKind.Changed, tpm.Kind);
        Assert.Equal(
            new[] { "TPM security, now Disabled", "TPM activation, now not applicable while TPM security is Disabled", "Num Lock at boot, now Enabled" },
            tpm.Changes);
        Assert.Equal("TPM activation: not applicable while TPM security is Disabled. Possible values: Deactivated, Activated.", dell.Rows.Single(row => row.Id == "TpmActivation").AccessibleName);

        // A read through the other interface is a first load again.
        Assert.Equal(DellApplyKind.FirstLoad, dell.ApplyLive(DellResults.Fallback()).Kind);
    }

    [Fact]
    public void Collection_IsNeverChangedAfterConstruction()
    {
        DellSettingsViewModel dell = new();
        DellSettingRowViewModel[] rows = dell.Rows.ToArray();
        int collectionEvents = 0;
        ((INotifyCollectionChanged)dell.Rows).CollectionChanged += (_, _) => collectionEvents++;

        dell.ApplyLive(DellResults.CompleteA18());
        dell.ApplyLive(DellResults.NothingInstalled());
        dell.ApplyLive(DellResults.Fallback());
        dell.ShowBaseline(Usable(DellResults.CompleteA18()), FolderName);
        dell.ShowBaseline(null, FolderName);
        dell.ShowBaseline(new DellSettingsFileResult(true, null, DellSettingsFile.TooLargeProblem), FolderName);
        dell.ShowBaselineNotLoaded();
        dell.ApplyLive(DellResults.NumLockDisabled(DateTimeOffset.Now));
        dell.ShowLive();

        Assert.Equal(0, collectionEvents);
        Assert.Equal(rows, dell.Rows.ToArray());
        Assert.IsType<System.Collections.ObjectModel.ReadOnlyObservableCollection<DellSettingRowViewModel>>(dell.Rows);
    }

    [Fact]
    public void Unavailable_WithoutCache_SaysNotLoaded()
    {
        DellSettingsViewModel dell = new();

        DellApplyOutcome outcome = dell.ApplyLive(DellResults.NothingInstalled());

        Assert.Equal(DellApplyKind.Unavailable, outcome.Kind);
        Assert.Null(outcome.KeptReadTime);
        Assert.All(dell.Rows, row =>
        {
            Assert.Equal(Strings.DellNotLoadedValue, row.CurrentText);
            Assert.Equal(string.Empty, row.PossibleText);
        });
        Assert.Equal("Boot list option: not loaded.", dell.Rows[0].AccessibleName);
        Assert.Equal(DellResults.NothingInstalledSentence, dell.StateText);
        Assert.Equal(Strings.DellArrivalUnavailable, dell.ArrivalClause);
    }

    [Fact]
    public void Unavailable_WithCache_KeepsTheValuesAndSaysWhen()
    {
        DellSettingsViewModel dell = new();
        DellSettingsResult first = DellResults.CompleteA18();
        dell.ApplyLive(first);
        Dictionary<DellSettingRowViewModel, List<string>> raised = RecordRows(dell);

        DellApplyOutcome outcome = dell.ApplyLive(DellResults.NothingInstalled());

        Assert.Equal(DellApplyKind.UnavailableKept, outcome.Kind);
        Assert.Equal(DellResults.LiveTime(first), outcome.KeptReadTime);
        Assert.Equal(UnitA18Sentences, dell.Rows.Select(row => row.AccessibleName));
        Assert.All(raised.Values, names => Assert.Empty(names));
        Assert.Equal(DellResults.NothingInstalledSentence + " The list still shows the values loaded at " + DellResults.LiveTime(first) + ".", dell.StateText);
        Assert.Null(dell.ArrivalClause);

        // The next read that answers replaces the cache and the state line says when again.
        Assert.Equal(DellApplyKind.Unchanged, dell.ApplyLive(first).Kind);
        Assert.StartsWith("Loaded at " + DellResults.LiveTime(first) + " from Dell Command Monitor", dell.StateText, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowBaseline_ShowsTheFileRows_AndTheBaselineLine()
    {
        DellSettingsViewModel dell = new();
        DellSettingsResult recorded = Recorded(DellResults.CompleteA18(), new DellMachine("Sample (fixtures)", "Sample (fixtures)", "Sample (fixtures)"));

        // Num Lock at boot is not in the file: the file's reader marks it not read.
        FirmwareSetting[] settings = recorded.Settings.Select(setting => setting.Id == "NumLock" ? DellSettingMapper.NotRead(DellAttributeCatalog.Find("NumLock")!, null) : setting).ToArray();
        dell.ShowBaseline(Usable(recorded with { Settings = settings, Outcome = DellReadOutcome.Partial }), FolderName);

        Assert.Equal(DellViewMode.Baseline, dell.Mode);
        Assert.Equal(UnitA18Sentences.Take(9), dell.Rows.Take(9).Select(row => row.AccessibleName));
        Assert.Equal(Strings.DellNotRecordedValue, dell.Rows[9].CurrentText);
        Assert.Equal("Num Lock at boot: not recorded in this baseline folder.", dell.Rows[9].AccessibleName);
        Assert.Equal(
            "Recorded at 15 September 2026 12:00 UTC from Dell Command Monitor in baseline folder sample-baseline, BIOS Sample (fixtures). Offline: nothing is loaded from this computer.",
            dell.StateText);
        Assert.Equal("from baseline folder sample-baseline", dell.ArrivalClause);
    }

    [Fact]
    public void ShowBaseline_EveryOtherCase_HasItsLine()
    {
        DellSettingsViewModel dell = new();
        dell.ApplyLive(DellResults.CompleteA18());

        // No file: folders saved before Alpha 2.
        dell.ShowBaseline(null, FolderName);
        Assert.Equal("Baseline folder sample-baseline holds no Dell BIOS settings; folders saved before Alpha 2 never do.", dell.StateText);
        AssertEveryRow(dell, Strings.DellNotRecordedValue);
        dell.ShowBaseline(new DellSettingsFileResult(false, null, null), FolderName);
        Assert.Equal("Baseline folder sample-baseline holds no Dell BIOS settings; folders saved before Alpha 2 never do.", dell.StateText);

        // A file that cannot be used.
        dell.ShowBaseline(new DellSettingsFileResult(true, null, DellSettingsFile.TooLargeProblem), FolderName);
        Assert.Equal("Baseline folder sample-baseline has a Dell settings file that cannot be used: it is larger than 256 KiB.", dell.StateText);
        AssertEveryRow(dell, Strings.DellNotRecordedValue);

        // A file that recorded a read that gave no answer: the recorded causes in Core's words.
        dell.ShowBaseline(Usable(Recorded(DellResults.NothingInstalled(), new DellMachine("Dell Inc.", "OptiPlex 7020", null))), FolderName);
        Assert.Equal(
            "Baseline folder sample-baseline holds no Dell BIOS settings: they could not be read when it was saved. Dell Command Monitor is not installed. Dell Command Configure was not found.",
            dell.StateText);
        AssertEveryRow(dell, Strings.DellNotRecordedValue);
        Assert.Equal("from baseline folder sample-baseline", dell.ArrivalClause);

        // A baseline load that was refused or failed: nothing is shown, never the live values.
        dell.ShowBaselineNotLoaded();
        Assert.Equal(DellViewMode.Baseline, dell.Mode);
        Assert.Equal(Strings.DellBaselineNotLoadedLine, dell.StateText);
        Assert.Equal("No Dell BIOS settings are shown: the baseline folder was not loaded.", dell.StateText);
        AssertEveryRow(dell, Strings.DellNotLoadedValue);

        // A file that answered with no BIOS version names it as not known.
        dell.ShowBaseline(Usable(Recorded(DellResults.CompleteA18(), new DellMachine(null, null, " "))), FolderName);
        Assert.EndsWith("in baseline folder sample-baseline, BIOS not known. Offline: nothing is loaded from this computer.", dell.StateText, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowLive_AfterBaseline_ShowsTheCacheWithoutReading()
    {
        DellSettingsViewModel dell = new();
        DellSettingsResult first = DellResults.CompleteA18();
        dell.ApplyLive(first);
        dell.ShowBaseline(Usable(Recorded(DellResults.NumLockDisabled(RecordedAt), new DellMachine("Sample (fixtures)", "Sample (fixtures)", "A18"))), FolderName);
        Assert.Equal("Num Lock at boot: Disabled. Possible values: Disabled, Enabled.", dell.Rows[9].AccessibleName);

        // A live answer while the baseline is shown goes to the cache only.
        DellSettingsResult later = DellResults.NumLockDisabled(first.ReadAt.AddMinutes(5));
        Dictionary<DellSettingRowViewModel, List<string>> raised = RecordRows(dell);
        string baselineLine = dell.StateText;
        Assert.Equal(DellApplyKind.Changed, dell.ApplyLive(later).Kind);
        Assert.All(raised.Values, names => Assert.Empty(names));
        Assert.Equal(baselineLine, dell.StateText);
        Assert.Equal(DellViewMode.Baseline, dell.Mode);

        // Back to the live view: the cache, the latest read that answered.
        dell.ShowLive();
        Assert.Equal(DellViewMode.Live, dell.Mode);
        Assert.Equal("Num Lock at boot: Disabled. Possible values: Disabled, Enabled.", dell.Rows[9].AccessibleName);
        Assert.Equal(UnitA18Sentences.Take(9), dell.Rows.Take(9).Select(row => row.AccessibleName));
        Assert.StartsWith("Loaded at " + DellResults.LiveTime(later) + " from Dell Command Monitor on this computer, BIOS A18.", dell.StateText, StringComparison.Ordinal);
        Assert.Null(dell.ArrivalClause);

        // Before any read the live view says so again.
        DellSettingsViewModel fresh = new();
        fresh.ShowBaselineNotLoaded();
        fresh.ShowLive();
        AssertEveryRow(fresh, Strings.DellNotLoadedYetValue);
        Assert.Equal(Strings.DellNotLoadedLine, fresh.StateText);
    }

    [Fact]
    public void NoRowHasAnItemStatus()
    {
        string[] properties = typeof(DellSettingRowViewModel).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name).ToArray();
        Assert.Equal(
            new[] { nameof(DellSettingRowViewModel.AccessibleName), nameof(DellSettingRowViewModel.CurrentText), nameof(DellSettingRowViewModel.DisplayName), nameof(DellSettingRowViewModel.Id), nameof(DellSettingRowViewModel.PossibleText) },
            properties.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(properties, name => name.Contains("Status", StringComparison.Ordinal) || name.Contains("Help", StringComparison.Ordinal));

        // No row text says read-only, in any state (A2-6: the screen reader says it once, from the control).
        DellSettingsViewModel dell = new();
        List<string> texts = Texts(dell);
        dell.ApplyLive(DellResults.CompleteA18());
        texts.AddRange(Texts(dell));
        dell.ApplyLive(DellResults.Fallback());
        texts.AddRange(Texts(dell));
        dell.ApplyLive(DellResults.NothingInstalled());
        texts.AddRange(Texts(dell));
        dell.ShowBaseline(null, FolderName);
        texts.AddRange(Texts(dell));
        dell.ShowBaselineNotLoaded();
        texts.AddRange(Texts(dell));
        Assert.All(texts, text => Assert.DoesNotMatch(@"(?i)read[\s-]only", text));
    }

    [Fact]
    public void ExposesNoCommand()
    {
        foreach (Type type in new[] { typeof(DellSettingsViewModel), typeof(DellSettingRowViewModel) })
        {
            PropertyInfo[] commands = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Where(property => typeof(ICommand).IsAssignableFrom(property.PropertyType))
                .ToArray();
            Assert.True(commands.Length == 0, type.Name + " exposes a command: " + string.Join(", ", commands.Select(property => property.Name)));
            Assert.DoesNotContain(type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static), field => typeof(ICommand).IsAssignableFrom(field.FieldType));
        }

        // The read command is the shell's.
        Assert.True(typeof(ICommand).IsAssignableFrom(typeof(ShellViewModel).GetProperty(nameof(ShellViewModel.ReadDellSettingsCommand))!.PropertyType));
    }

    [Fact]
    public void RowSentence_StartsWithTheName()
    {
        DellSettingsViewModel dell = new();
        AssertSentencesStartWithTheName(dell);

        DellSettingsResult a18 = DellResults.CompleteA18();
        dell.ApplyLive(a18);
        AssertSentencesStartWithTheName(dell);
        Assert.Equal(a18.Settings.Select(DellSentences.RowSentence), dell.Rows.Select(row => row.AccessibleName));

        DellSettingsResult fallback = DellResults.Fallback();
        dell.ApplyLive(fallback);
        AssertSentencesStartWithTheName(dell);
        Assert.Equal(fallback.Settings.Select(DellSentences.RowSentence), dell.Rows.Select(row => row.AccessibleName));
        Assert.Equal("Boot list option: not reported. This version reads it through Dell Command Monitor only.", dell.Rows[0].AccessibleName);
        Assert.Equal("Secure Boot: Enabled. Possible values: Disabled, Enabled.", dell.Rows[1].AccessibleName);

        // Dell's own words, as long as a baseline file may hold them, are cut at 200 characters
        // with the name first and "and N more" for what does not fit.
        string word = new('W', 60);
        IReadOnlyDictionary<string, object?>[] rows = DcmRowFixtures.UnitA18()
            .Select(row => string.Equals((string?)row["AttributeName"], "NIC", StringComparison.Ordinal)
                ? DcmRowFixtures.Row("NIC", DcmRowFixtures.Texts("1"), DcmRowFixtures.Texts("1", "2", "3", "4"), DcmRowFixtures.Texts(word + "1", word + "2", word + "3", word + "4"))
                : row)
            .ToArray();
        dell.ApplyLive(CliTestHarness.DellFromDcm(rows, DellReadOptions.Default));
        AssertSentencesStartWithTheName(dell);
        DellSettingRowViewModel nic = dell.Rows.Single(row => row.Id == "IntegratedNic");
        Assert.StartsWith("Integrated NIC: " + word + "1. Possible values: " + word + "1, ", nic.AccessibleName, StringComparison.Ordinal);
        Assert.Contains("more.", nic.AccessibleName, StringComparison.Ordinal);
        Assert.True(nic.AccessibleName.Length <= DellSentences.RowSentenceLimit);
    }

    [Fact]
    public void ArrivalClause_FollowsTheMode()
    {
        DellSettingsViewModel dell = new();
        Assert.Null(dell.ArrivalClause);

        dell.ApplyLive(DellResults.NothingInstalled());
        Assert.Equal("could not be loaded", dell.ArrivalClause);

        dell.ApplyLive(DellResults.CompleteA18());
        Assert.Null(dell.ArrivalClause);

        dell.ApplyLive(DellResults.NothingInstalled());
        Assert.Null(dell.ArrivalClause);

        dell.ShowBaseline(null, "optiplex-20260922");
        Assert.Equal("from baseline folder optiplex-20260922", dell.ArrivalClause);

        // A live answer while the baseline is shown leaves the clause of the baseline.
        dell.ApplyLive(DellResults.CompleteA18());
        Assert.Equal("from baseline folder optiplex-20260922", dell.ArrivalClause);

        dell.ShowBaselineNotLoaded();
        Assert.Equal("could not be loaded", dell.ArrivalClause);

        dell.ShowLive();
        Assert.Null(dell.ArrivalClause);
    }

    [Fact]
    public void LiveLine_NamesTheFallbackAndAnotherBios()
    {
        DellSettingsViewModel dell = new();
        DellSettingsResult fallback = DellResults.Fallback();

        dell.ApplyLive(fallback);

        Assert.Equal(
            "Loaded at " + DellResults.LiveTime(fallback) + " from Dell Command Configure on this computer, BIOS A18. This application shows Dell BIOS settings and never changes them; they are changed in the BIOS setup (F2). Dell Command Configure reports current values only, so the possible values shown are those of the attribute map for BIOS A18.",
            dell.StateText);

        string warning = "These names and values were checked against BIOS A18 only; this computer runs BIOS A20.";
        DellSettingsResult otherBios = DellResults.CompleteA18() with { Machine = new DellMachine("Dell Inc.", "OptiPlex 7020", "A20"), Warnings = new[] { warning } };
        dell.ApplyLive(otherBios);
        Assert.Equal(
            "Loaded at " + DellResults.LiveTime(otherBios) + " from Dell Command Monitor on this computer, BIOS A20. This application shows Dell BIOS settings and never changes them; they are changed in the BIOS setup (F2). " + warning,
            dell.StateText);

        dell.ApplyLive(DellResults.CompleteA18() with { Machine = new DellMachine(null, null, null) });
        Assert.Contains("on this computer, BIOS not known.", dell.StateText, StringComparison.Ordinal);
        Assert.True(dell.StateText.Length < 512);
        Assert.DoesNotContain('\n', dell.StateText);
    }

    /// <summary>A recorded read as dell-settings.json's reader returns it: the time of the sample baseline and the given machine.</summary>
    private static DellSettingsResult Recorded(DellSettingsResult read, DellMachine machine) => read with { ReadAt = RecordedAt, Machine = machine, ElapsedMs = 0 };

    private static DellSettingsFileResult Usable(DellSettingsResult recorded) => new(true, recorded, null);

    private static void AssertEveryRow(DellSettingsViewModel dell, string value)
    {
        Assert.All(dell.Rows, row =>
        {
            Assert.Equal(value, row.CurrentText);
            Assert.Equal(string.Empty, row.PossibleText);
            Assert.Equal(row.DisplayName + ": " + value + ".", row.AccessibleName);
        });
    }

    private static void AssertSentencesStartWithTheName(DellSettingsViewModel dell)
    {
        Assert.All(dell.Rows, row =>
        {
            Assert.StartsWith(row.DisplayName + ": ", row.AccessibleName, StringComparison.Ordinal);
            Assert.True(row.AccessibleName.Length <= DellSentences.RowSentenceLimit, row.AccessibleName);
        });
    }

    private static List<string> Texts(DellSettingsViewModel dell)
    {
        return dell.Rows.SelectMany(row => new[] { row.CurrentText, row.PossibleText, row.AccessibleName }).ToList();
    }

    private static List<string> Record(INotifyPropertyChanged source)
    {
        List<string> raised = new();
        source.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);
        return raised;
    }

    private static Dictionary<DellSettingRowViewModel, List<string>> RecordRows(DellSettingsViewModel dell)
    {
        Dictionary<DellSettingRowViewModel, List<string>> raised = new(ReferenceEqualityComparer.Instance);
        foreach (DellSettingRowViewModel row in dell.Rows)
        {
            raised[row] = Record(row);
        }

        return raised;
    }
}
