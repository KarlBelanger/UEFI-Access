// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Windows.Input;
using UefiAccess.Core.Decoders;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Variables;
using UefiAccess.Presentation.Accessibility;
using UefiAccess.Presentation.Infrastructure;
using UefiAccess.Presentation.Resources;
using UefiAccess.Presentation.Services;
using UefiAccess.Presentation.ViewModels;
using Xunit;
using static UefiAccess.Core.Tests.Fixtures.LoadOptionFixtures;
using static UefiAccess.Core.Tests.Safety.SafetyFixtures;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// The Change group: which controls show for which variable,
/// only for the live source; the six commands, enabled while their control shows and no check or
/// write runs; a move and Ctrl+Z answered with one sentence each; Escape cancelling the checks;
/// and the Writability line and the row status the list and the detail pane take from Core's
/// policy. The write service is a fake that only answers Newest; nothing here writes.
/// </summary>
public sealed class ChangeViewModelTests
{
    private static readonly string BootOrderKey = Whitelist.BootOrder.Key;
    private static readonly string BootNextKey = Whitelist.BootNext.Key;

    // ---- which controls show -------------------------------------------------------------------

    [Fact]
    public void BootOrder_OnTheLiveSource_ShowsTheListTheMovesAndReview_ButNoOneTimeBootControls()
    {
        Harness harness = new(Store());

        harness.Show("BootOrder");

        ChangeViewModel change = harness.Change;
        Assert.True(change.IsVisible);
        Assert.True(change.ShowsBootOrderControls);
        Assert.False(change.ShowsBootOnce);
        Assert.False(change.ShowsClearBootNext);
        Assert.False(change.ShowsRestore);
        Assert.Equal(new[] { "Boot0002", "Boot0003" }, change.Editor.Entries.Select(entry => entry.Name));
    }

    [Fact]
    public void AnActiveBootCategoryEntry_ShowsBootOnce_AndNothingElse()
    {
        Harness harness = new(Store());

        harness.Show("Boot0003");

        Assert.True(harness.Change.IsVisible);
        Assert.True(harness.Change.ShowsBootOnce);
        Assert.False(harness.Change.ShowsBootOrderControls);
        Assert.False(harness.Change.ShowsClearBootNext);
        Assert.False(harness.Change.ShowsRestore);
    }

    [Theory]
    [InlineData("Boot0006")] // inactive
    [InlineData("Boot0004")] // category App
    [InlineData("Boot0008")] // malformed
    [InlineData("Timeout")]
    [InlineData("BootCurrent")]
    [InlineData("PK")]
    public void AnyOtherVariable_KeepsTheGroupCollapsed(string name)
    {
        Harness harness = new(Store());

        harness.Show(name);

        Assert.False(harness.Change.IsVisible);
        Assert.False(harness.Change.ShowsBootOrderControls);
        Assert.False(harness.Change.ShowsBootOnce);
        Assert.False(harness.Change.ShowsClearBootNext);
        Assert.False(harness.Change.ShowsRestore);
    }

    /// <summary>
    /// The group follows the Writability verdict for BootOrder and BootNext as it does for the
    /// Boot#### entries: a value the Writability line says cannot be changed (Malformed, or an
    /// authenticated-write attribute) shows no controls, since docs/Keyboard-Map.md section 7 puts the
    /// group only on a variable this application can change or choose.
    /// </summary>
    [Fact]
    public void ABootOrderOrBootNextTheWritabilityLineCallsReadOnly_KeepsTheGroupCollapsed()
    {
        (VariableSet Set, string Name, Writability Verdict)[] readOnly =
        {
            (StoreWithOrder(WindowsOnPartition1, MissingNumber), "BootOrder", Writability.Malformed),
            (Store(Var("BootOrder", BootList(WindowsOnPartition1, UsbNumber), NvBsRtTa)), "BootOrder", Writability.AuthenticatedWrite),
            (Store(Var("BootNext", U16(UsbNumber), NvBsRtTa)), "BootNext", Writability.AuthenticatedWrite),
        };

        foreach ((VariableSet set, string name, Writability verdict) in readOnly)
        {
            Harness harness = new(set);
            VariableItemViewModel row = harness.RowOf(name);
            Assert.Equal(verdict, row.Writability.Verdict);

            harness.Change.Show(row);

            Assert.False(harness.Change.IsVisible, name + " " + verdict);
            Assert.False(harness.Change.ShowsBootOrderControls);
            Assert.False(harness.Change.ShowsClearBootNext);
            Assert.False(harness.Change.ShowsRestore);
            Assert.All(harness.DialogCommands, command => Assert.False(command.CanExecute(null)));
        }
    }

    /// <summary>On a machine that is not the contracted one the controls work up to Confirm change, which refuses (the WrongMachine verdict).</summary>
    [Fact]
    public void OnAnotherMachine_BootOrderAndBootNext_KeepTheirControls()
    {
        Harness harness = new(Store(Var("BootNext", U16(UsbNumber))));

        foreach (string name in new[] { "BootOrder", "BootNext" })
        {
            VariableItemViewModel row = Row(harness.Set.Find(name, Global) ?? throw new InvalidOperationException(name), harness.Set, machineConfirmed: false);
            Assert.Equal(Writability.WrongMachine, row.Writability.Verdict);

            harness.Change.Show(row);

            Assert.True(harness.Change.IsVisible, name);
        }

        Assert.True(harness.Change.ShowsClearBootNext);
        harness.Change.Show(Row(harness.Set.Find("BootOrder", Global)!, harness.Set, machineConfirmed: false));
        Assert.True(harness.Change.ShowsBootOrderControls);
    }

    [Fact]
    public void DellsBootEntriesUnderTheirVendorGuid_AreNotBootEntries()
    {
        Harness harness = new(Store());
        FirmwareVariable dell = harness.Set.Find("BootFFFB", DellBootGuid) ?? throw new InvalidOperationException("fixture");

        harness.Change.Show(Row(dell, harness.Set));

        Assert.False(harness.Change.IsVisible);
    }

    [Fact]
    public void BootNext_ShowsClearOneTimeBoot_AndRestoreWhenASnapshotExists()
    {
        Harness harness = new(Store(Var("BootNext", U16(UsbNumber))));
        harness.Writes.Snapshots[BootNextKey] = Snapshot(BootNextKey, WriteRecordOutcome.Verified);

        harness.Show("BootNext");

        Assert.True(harness.Change.IsVisible);
        Assert.True(harness.Change.ShowsClearBootNext);
        Assert.True(harness.Change.ShowsRestore);
        Assert.False(harness.Change.ShowsBootOnce);
        Assert.False(harness.Change.ShowsBootOrderControls);
        Assert.Contains(BootNextKey, harness.Writes.NewestAsked);
    }

    [Fact]
    public void Restore_ShowsOnlyWhenTheWriteServiceFindsASnapshotOfThatVariable()
    {
        Harness harness = new(Store());

        harness.Show("BootOrder");
        Assert.False(harness.Change.ShowsRestore);

        harness.Writes.Snapshots[BootOrderKey] = Snapshot(BootOrderKey, WriteRecordOutcome.Pending);
        harness.Show("BootOrder");
        Assert.True(harness.Change.ShowsRestore);
        Assert.True(harness.Change.RestorePreviousCommand.CanExecute(null));
    }

    [Fact]
    public void ABaselineSource_NeverShowsTheGroup_AndLeavesTheEditorEmpty()
    {
        // FC-28: a baseline shown in the application, whatever the variable.
        Harness harness = new(Store(Var("BootNext", U16(UsbNumber))), live: false);
        harness.Writes.Snapshots[BootOrderKey] = Snapshot(BootOrderKey, WriteRecordOutcome.Verified);

        foreach (string name in new[] { "BootOrder", "BootNext", "Boot0003" })
        {
            harness.Show(name);
            Assert.False(harness.Change.IsVisible, name);
            Assert.False(harness.Change.ShowsRestore, name);
        }

        Assert.Empty(harness.Change.Editor.Entries);
        Assert.Empty(harness.Writes.NewestAsked);
    }

    [Fact]
    public void WithoutAWriteService_TheGroupNeverShows()
    {
        VariableSet set = Store();
        ChangeViewModel change = new(null, null, null, null);
        change.Load(set, live: true);

        change.Show(Row(set.Find("BootOrder", Global) ?? throw new InvalidOperationException("fixture"), set));

        Assert.False(change.IsVisible);
        Assert.Empty(change.Editor.Entries);
        Assert.False(new ChangeViewModel().IsVisible);
    }

    [Fact]
    public void Load_KeepsTheShownVariable_ButDropsTheProposedOrder()
    {
        Harness harness = new(Store());
        harness.Show("BootOrder");
        harness.Change.MoveDownCommand.Execute(null);
        Assert.True(harness.Change.Editor.HasChanges);

        harness.Change.Load(harness.Set, live: true);

        Assert.False(harness.Change.Editor.HasChanges);
        Assert.True(harness.Change.ShowsBootOrderControls);

        harness.Change.Load(null, live: false);

        Assert.False(harness.Change.IsVisible);
        Assert.Empty(harness.Change.Editor.Entries);
    }

    // ---- commands ------------------------------------------------------------------------------

    [Fact]
    public void EveryShownCommand_IsEnabled_WhileNothingRuns()
    {
        Harness harness = new(Store());
        harness.Show("BootOrder");

        Assert.True(harness.Change.MoveUpCommand.CanExecute(null));
        Assert.True(harness.Change.MoveDownCommand.CanExecute(null));
        Assert.True(harness.Change.ReviewChangeCommand.CanExecute(null));
        Assert.True(harness.Change.ResetOrderCommand.CanExecute(null));
        Assert.False(harness.Change.BootOnceCommand.CanExecute(null));
        Assert.False(harness.Change.ClearBootNextCommand.CanExecute(null));
        Assert.False(harness.Change.CancelChecksCommand.CanExecute(null));

        // Review change stays enabled with nothing to change: the shell answers with a sentence.
        Assert.False(harness.Change.Editor.HasChanges);
    }

    [Fact]
    public void TheMoves_NeedASelectedRow()
    {
        Harness harness = new(Store());
        harness.Show("BootOrder");
        int raised = 0;
        harness.Change.MoveUpCommand.CanExecuteChanged += (_, _) => raised++;

        harness.Change.Editor.SelectedEntry = null;

        Assert.False(harness.Change.MoveUpCommand.CanExecute(null));
        Assert.False(harness.Change.MoveDownCommand.CanExecute(null));
        Assert.True(harness.Change.ReviewChangeCommand.CanExecute(null));
        Assert.True(raised > 0);
    }

    [Fact]
    public void WhileTheChecksRun_EveryChangeCommandIsDisabled_AndEscapeCancelsThem()
    {
        Harness harness = new(Store());
        harness.Show("BootOrder");
        List<ICommand> raised = new();
        foreach (ICommand command in harness.AllCommands)
        {
            command.CanExecuteChanged += (sender, _) => raised.Add((ICommand)(sender ?? command));
        }

        CancellationToken token = harness.Change.BeginChecking(ChangeAction.ReviewChange);

        Assert.True(harness.Change.IsChecking);
        Assert.Equal(ChangeAction.ReviewChange, harness.Change.LastAction);
        Assert.All(harness.DialogCommands.Append(harness.Change.MoveUpCommand).Append(harness.Change.MoveDownCommand).Append(harness.Change.ResetOrderCommand), command => Assert.False(command.CanExecute(null)));
        Assert.True(harness.Change.CancelChecksCommand.CanExecute(null));
        Assert.NotEmpty(raised);

        Assert.True(harness.Change.CancelChecks());
        Assert.True(token.IsCancellationRequested);
        Assert.False(harness.Change.CancelChecks());

        harness.Change.EndChecking();

        Assert.False(harness.Change.IsChecking);
        Assert.False(harness.Change.CancelChecks());
        Assert.True(harness.Change.ReviewChangeCommand.CanExecute(null));
        Assert.False(harness.Change.CancelChecksCommand.CanExecute(null));
    }

    [Fact]
    public void WhileAChangeIsWritten_EveryChangeCommandIsDisabled()
    {
        Harness harness = new(Store());
        harness.Show("BootOrder");

        harness.Change.IsWriting = true;

        Assert.All(harness.AllCommands, command => Assert.False(command.CanExecute(null)));

        harness.Change.IsWriting = false;

        Assert.True(harness.Change.MoveUpCommand.CanExecute(null));
        Assert.True(harness.Change.ReviewChangeCommand.CanExecute(null));
    }

    [Fact]
    public void AMove_IsAnnouncedOnce_AndAsksForNoFocus()
    {
        Harness harness = new(Store());
        harness.Show("BootOrder");
        harness.Change.Editor.SelectedEntry = harness.Change.Editor.Entries[1];

        harness.Change.MoveUpCommand.Execute(null);

        Assert.Equal(new[] { "Boot0003 (" + UsbStickDescription + ") moved to position 1 of 2." }, harness.Announced);
        Assert.Empty(harness.Focus);

        harness.Change.MoveUpCommand.Execute(null);

        Assert.Equal("Boot0003 (" + UsbStickDescription + ") is already first.", harness.Announced[^1]);
        Assert.Equal(2, harness.Announced.Count);
        Assert.Empty(harness.Focus);
    }

    [Fact]
    public void CtrlZ_PutsTheFirmwaresOrderBack_SaysSoOnce_AndAsksForTheSelectedRow()
    {
        Harness harness = new(Store());
        harness.Show("BootOrder");
        harness.Change.MoveDownCommand.Execute(null);
        harness.Announced.Clear();

        harness.Change.ResetOrderCommand.Execute(null);

        Assert.Equal(new[] { Strings.OrderResetStatus }, harness.Announced);
        Assert.Equal(new[] { FocusTarget.BootOrderEntry }, harness.Focus);
        Assert.False(harness.Change.Editor.HasChanges);
    }

    [Fact]
    public async Task TheFourDialogControls_HandTheirActionToTheShell()
    {
        Harness harness = new(Store(Var("BootNext", U16(UsbNumber))));
        harness.Writes.Snapshots[BootOrderKey] = Snapshot(BootOrderKey, WriteRecordOutcome.Verified);

        harness.Show("BootOrder");
        await Assert.IsType<AsyncRelayCommand>(harness.Change.ReviewChangeCommand).ExecuteAsync();
        await Assert.IsType<AsyncRelayCommand>(harness.Change.RestorePreviousCommand).ExecuteAsync();
        harness.Show("Boot0003");
        await Assert.IsType<AsyncRelayCommand>(harness.Change.BootOnceCommand).ExecuteAsync();
        harness.Show("BootNext");
        await Assert.IsType<AsyncRelayCommand>(harness.Change.ClearBootNextCommand).ExecuteAsync();

        Assert.Equal(new[] { ChangeAction.ReviewChange, ChangeAction.RestorePrevious, ChangeAction.BootOnce, ChangeAction.ClearBootNext }, harness.Started);
    }

    // ---- the Writability line and the rows (section 5.4) ------------------------------------------

    [Fact]
    public void TheRows_CarryCoresItemStatus_AndTheWordsAfterTheName_OnlyForTheTwoChangeableVariables()
    {
        VariableListViewModel list = new();
        list.Load(Store(Var("BootNext", U16(UsbNumber))).All, offline: false, machineConfirmed: true);

        VariableItemViewModel bootOrder = Loaded(list, "BootOrder");
        VariableItemViewModel bootNext = Loaded(list, "BootNext");
        Assert.Equal(WritabilitySentences.ItemStatusReorder, bootOrder.ItemStatus);
        Assert.Equal(WritabilitySentences.ItemStatusOneTimeBoot, bootNext.ItemStatus);
        Assert.StartsWith("BootOrder, changeable: order of entries, EFI global variable, NV BS RT, 4 bytes, ", bootOrder.AccessibleName, StringComparison.Ordinal);
        Assert.StartsWith("BootNext, changeable: one-time boot, EFI global variable, NV BS RT, 2 bytes", bootNext.AccessibleName, StringComparison.Ordinal);
        Assert.True(bootOrder.AccessibleName.Length <= VariableItemViewModel.AccessibleNameMaxLength);

        foreach (VariableItemViewModel row in list.AllItems.Where(row => row.Name is not ("BootOrder" or "BootNext")))
        {
            Assert.Equal(string.Empty, row.ItemStatus);
            Assert.DoesNotContain("changeable", row.AccessibleName, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TheRowWords_AreCoresItemStatus_AsTheSentenceContinuesIt()
    {
        string reorder = WritabilitySentences.ItemStatusReorder;
        string oneTimeBoot = WritabilitySentences.ItemStatusOneTimeBoot;
        string elsewhere = WritabilitySentences.ItemStatusElsewhere;

        Assert.Equal(Strings.RowStatusOrderOfEntries, char.ToLowerInvariant(reorder[0]) + reorder[1..]);
        Assert.Equal(Strings.RowStatusOneTimeBoot, char.ToLowerInvariant(oneTimeBoot[0]) + oneTimeBoot[1..]);
        Assert.Equal(VariableItemViewModel.RowStatusElsewhere, char.ToLowerInvariant(elsewhere[0]) + elsewhere[1..]);
    }

    [Fact]
    public void ABaseline_GivesNoItemStatus_AndTheRowSentenceOfAlpha1()
    {
        VariableSet set = Store(Var("BootNext", U16(UsbNumber)));
        VariableListViewModel offline = new();
        offline.Load(set.All, offline: true, machineConfirmed: false);

        VariableItemViewModel bootOrder = Loaded(offline, "BootOrder");
        Assert.Equal(Writability.Offline, bootOrder.Writability.Verdict);
        Assert.StartsWith("BootOrder, EFI global variable, NV BS RT, 4 bytes", bootOrder.AccessibleName, StringComparison.Ordinal);
        Assert.StartsWith("BootNext, EFI global variable, NV BS RT, 2 bytes", Loaded(offline, "BootNext").AccessibleName, StringComparison.Ordinal);
        foreach (VariableItemViewModel row in offline.AllItems)
        {
            Assert.Equal(string.Empty, row.ItemStatus);
            Assert.DoesNotContain("changeable", row.AccessibleName, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Decided 25 September 2026: on a machine that is not the contracted one, BootOrder and
    /// BootNext carry Core's item status "Not changeable on this machine" and its
    /// words right after the name, as the two changeable rows do on the 7020, so a tester there can
    /// hear whether the item status is spoken. Every other row, a Boot#### included, is unchanged.
    /// </summary>
    [Fact]
    public void OnAnotherMachine_BootOrderAndBootNext_CarryTheItemStatusElsewhere_AndItsWordsAfterTheName()
    {
        VariableListViewModel list = new();
        list.Load(Store(Var("BootNext", U16(UsbNumber))).All, offline: false, machineConfirmed: false);

        VariableItemViewModel bootOrder = Loaded(list, "BootOrder");
        VariableItemViewModel bootNext = Loaded(list, "BootNext");
        Assert.Equal(Writability.WrongMachine, bootOrder.Writability.Verdict);
        Assert.Equal(Writability.WrongMachine, bootNext.Writability.Verdict);
        Assert.Equal(WritabilitySentences.ItemStatusElsewhere, bootOrder.ItemStatus);
        Assert.Equal(WritabilitySentences.ItemStatusElsewhere, bootNext.ItemStatus);
        Assert.StartsWith("BootOrder, not changeable on this machine, EFI global variable, NV BS RT, 4 bytes, ", bootOrder.AccessibleName, StringComparison.Ordinal);
        Assert.StartsWith("BootNext, not changeable on this machine, EFI global variable, NV BS RT, 2 bytes", bootNext.AccessibleName, StringComparison.Ordinal);
        Assert.InRange(bootOrder.AccessibleName.Length, 1, VariableItemViewModel.AccessibleNameMaxLength);
        Assert.InRange(bootNext.AccessibleName.Length, 1, VariableItemViewModel.AccessibleNameMaxLength);
        Assert.DoesNotContain("…", bootOrder.AccessibleName, StringComparison.Ordinal);

        VariableItemViewModel bootEntry = Loaded(list, "Boot0003");
        Assert.Equal(Writability.WrongMachine, bootEntry.Writability.Verdict);
        foreach (VariableItemViewModel row in list.AllItems.Where(row => row.Name is not ("BootOrder" or "BootNext")))
        {
            Assert.Equal(string.Empty, row.ItemStatus);
            Assert.DoesNotContain("changeable", row.AccessibleName, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void OnAnotherMachine_ALongBootOrderSentence_KeepsTheWords_AndIsCutAt200Characters()
    {
        VariableSet set = StoreWithOrder(
            WindowsOnPartition1, WindowsOnPartition2, UsbNumber, HiddenNumber, InactiveNumber, FallbackNumber,
            WindowsCopyNumber, FallbackSamePartitionNumber, WindowsWithoutPartitionNumber, PartitionWithoutFileNumber, NetworkNumber);
        VariableListViewModel list = new();
        list.Load(set.All, offline: false, machineConfirmed: false);

        VariableItemViewModel bootOrder = Loaded(list, "BootOrder");

        Assert.Equal(Writability.WrongMachine, bootOrder.Writability.Verdict);
        Assert.Equal(WritabilitySentences.ItemStatusElsewhere, bootOrder.ItemStatus);
        Assert.True(bootOrder.Summary.Length > VariableItemViewModel.AccessibleNameMaxLength);
        Assert.StartsWith("BootOrder, not changeable on this machine, EFI global variable, NV BS RT, 22 bytes, BootOrder: ", bootOrder.AccessibleName, StringComparison.Ordinal);
        Assert.Equal(VariableItemViewModel.AccessibleNameMaxLength, bootOrder.AccessibleName.Length);
        Assert.EndsWith("…", bootOrder.AccessibleName, StringComparison.Ordinal);
    }

    [Fact]
    public void ARowBuiltWithoutAVerdict_IsJudgedOnAnotherMachine_SoBootNextCarriesTheWords()
    {
        // A row built without a verdict is judged live, on a machine that is not the contracted
        // one, with no other variable known: BootNext is WrongMachine, BootOrder fails rule V5.
        FirmwareVariable next = Var("BootNext", U16(UsbNumber));
        VariableItemViewModel bootNext = new(next, NamespaceCatalog.Describe(Global), null);
        VariableItemViewModel bootOrder = new(Var("BootOrder", BootList(WindowsOnPartition1)), NamespaceCatalog.Describe(Global), null);

        Assert.Equal(Writability.WrongMachine, bootNext.Writability.Verdict);
        Assert.Equal(WritabilitySentences.ItemStatusElsewhere, bootNext.ItemStatus);
        Assert.Equal("BootNext, not changeable on this machine, EFI global variable, NV BS RT, 2 bytes", bootNext.AccessibleName);
        Assert.Equal(Writability.Malformed, bootOrder.Writability.Verdict);
        Assert.Equal(string.Empty, bootOrder.ItemStatus);
        Assert.StartsWith("BootOrder, EFI global variable, NV BS RT, 2 bytes", bootOrder.AccessibleName, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWritabilityLine_IsTheVerdictsSentence_ForEachKindOfVariable()
    {
        VariableSet set = Store();
        VariableListViewModel live = new();
        live.Load(set.All, offline: false, machineConfirmed: true);
        VariableListViewModel baseline = new();
        baseline.Load(set.All, offline: true, machineConfirmed: false);
        VariableDetailViewModel detail = new();

        detail.Show(Loaded(live, "BootOrder"));
        Assert.Equal(WritabilitySentences.ReorderOnly, detail.ReadOnlyStatus);
        detail.Show(Loaded(live, "Boot0003"));
        Assert.Equal(WritabilitySentences.OneTimeBootTarget, detail.ReadOnlyStatus);
        detail.Show(Loaded(live, "Timeout"));
        Assert.Equal(WritabilitySentences.NotWhitelisted, detail.ReadOnlyStatus);
        detail.Show(Loaded(baseline, "BootOrder"));
        Assert.Equal(WritabilitySentences.Offline, detail.ReadOnlyStatus);
    }

    [Fact]
    public void TheSecureBootVariables_AddTheFirmwareSetupInstruction_ToTheirWritabilityLine()
    {
        VariableSet set = Store();
        VariableListViewModel live = new();
        live.Load(set.All, offline: false, machineConfirmed: true);
        VariableDetailViewModel detail = new();

        detail.Show(Loaded(live, "PK"));

        Assert.Equal(CheckSentences.Format(WritabilitySentences.AuthenticatedWriteFormat, "TA") + " " + Strings.FirmwareSetupHint, detail.ReadOnlyStatus);
        Assert.True(detail.ReadOnlyStatus.Length < VariableDetailViewModel.LongValueLength);
        Assert.DoesNotContain(Strings.FirmwareSetupHint, VariableDetailViewModel.WritabilityLine(Loaded(live, "Timeout")), StringComparison.Ordinal);
    }

    [Fact]
    public void TheDetailPane_ShowsTheChangeGroupForTheRowItShows()
    {
        Harness harness = new(Store());
        VariableDetailViewModel detail = new(harness.Change);

        detail.Show(harness.RowOf("BootOrder"));
        Assert.True(detail.Change.ShowsBootOrderControls);
        Assert.Same(harness.Change, detail.Change);

        detail.Show(null);
        Assert.False(detail.Change.IsVisible);
        Assert.Null(detail.Change.Item);
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static VariableItemViewModel Loaded(VariableListViewModel list, string name)
    {
        return list.AllItems.Single(row => row.Name == name && row.Guid == Global);
    }

    private static VariableItemViewModel Row(FirmwareVariable variable, VariableSet set, bool machineConfirmed = true)
    {
        DecodedVariable? decoded = DecoderRegistry.CreateDefault().TryDecode(variable, set);
        return new VariableItemViewModel(variable, NamespaceCatalog.Describe(variable.VendorGuid), decoded, WritabilityPolicy.Describe(variable, set, offline: false, machineConfirmed: machineConfirmed));
    }

    private static SnapshotSummary Snapshot(string key, WriteRecordOutcome outcome)
    {
        return new SnapshotSummary(Path.Combine(Path.GetTempPath(), "snapshots", "20260930-141502-reorder"), "20260930-141502-reorder", "Reorder", "2026-09-30T14:15:02Z", key, outcome);
    }

    /// <summary>A Change group over a fake write service, loaded with a store, recording what it announces, where it sends focus and what it starts.</summary>
    private sealed class Harness
    {
        public Harness(VariableSet set, bool live = true)
        {
            Set = set;
            Change = new ChangeViewModel(
                Writes,
                action =>
                {
                    Started.Add(action);
                    return Task.CompletedTask;
                },
                Announced.Add,
                Focus.Add);
            Change.Load(set, live);
        }

        public VariableSet Set { get; }

        public FakeWriteService Writes { get; } = new();

        public ChangeViewModel Change { get; }

        public List<string> Announced { get; } = new();

        public List<FocusTarget> Focus { get; } = new();

        public List<ChangeAction> Started { get; } = new();

        public IEnumerable<ICommand> DialogCommands => new[] { Change.ReviewChangeCommand, Change.BootOnceCommand, Change.ClearBootNextCommand, Change.RestorePreviousCommand };

        public IEnumerable<ICommand> AllCommands => DialogCommands.Concat(new[] { Change.MoveUpCommand, Change.MoveDownCommand, Change.ResetOrderCommand, Change.CancelChecksCommand });

        public VariableItemViewModel RowOf(string name)
        {
            return Row(Set.Find(name, Global) ?? throw new InvalidOperationException(name + " is not in the fixture"), Set);
        }

        public void Show(string name) => Change.Show(RowOf(name));
    }
}

/// <summary>A write service that answers Newest from a table and refuses every pipeline call: nothing here may check or write.</summary>
internal sealed class FakeWriteService : IWriteService
{
    /// <summary>The newest snapshot per variable key.</summary>
    public Dictionary<string, SnapshotSummary> Snapshots { get; } = new(StringComparer.Ordinal);

    /// <summary>Every key Newest was asked for.</summary>
    public List<string> NewestAsked { get; } = new();

    /// <inheritdoc />
    public string SnapshotRoot => Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "snapshots");

    /// <inheritdoc />
    public Task<PrepareResult?> PrepareAsync(ChangeRequest request, CancellationToken token) => throw new InvalidOperationException("no checks in this test");

    /// <inheritdoc />
    public Task<TransactionResult> CommitAsync(ChangePlan plan, ConfirmationToken confirmation) => throw new InvalidOperationException("no commit in this test");

    /// <inheritdoc />
    public Task<TransactionResult> CancelAsync(ChangePlan plan) => throw new InvalidOperationException("no cancel in this test");

    /// <inheritdoc />
    public SnapshotSummary? Newest(string key)
    {
        NewestAsked.Add(key);
        return Snapshots.TryGetValue(key, out SnapshotSummary? summary) ? summary : null;
    }
}
