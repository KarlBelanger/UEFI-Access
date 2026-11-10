// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;
using System.Windows.Input;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Privileges;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Tests.Safety;
using UefiAccess.Core.Variables;
using UefiAccess.Presentation.Accessibility;
using UefiAccess.Presentation.Diagnostics;
using UefiAccess.Presentation.Infrastructure;
using UefiAccess.Presentation.Resources;
using UefiAccess.Presentation.Services;
using UefiAccess.Presentation.ViewModels;
using Xunit;
using static UefiAccess.Core.Tests.Fixtures.LoadOptionFixtures;
using static UefiAccess.Core.Tests.Safety.SafetyFixtures;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// The shell's change flows, end to end over a real
/// <see cref="WritePipeline"/> composed from the Safety test fakes (a scripted store, a writer and
/// a suspender that record their calls and never reach native code) with its data folder under
/// the test's temporary folder: every Status sentence and focus target of the table, the dialog
/// that shows the exact plan and returns the check box, Cancel writing nothing (FC-29), IsWriting
/// disabling the commands and keeping the window open, a load finishing behind the dialog or
/// during the write held, the live reload after a write (saying "Proposed order discarded." when it
/// drops an order the write did not put in), Escape cancelling the checks, a baseline showing the Offline
/// verdict with the Change group collapsed (FC-28), and the write service's lazy composition. Since
/// the M3 review: a plan that arrives with the Escape, a confirmation dialog that fails, Open
/// baseline folder and Export visible list disabled while the checks run, a plan that arrives while
/// another dialog is open, a load that finishes while the checks run (it cancels them), the commands
/// kept disabled through the reload after a write, and Ctrl+F while a change is written.
/// </summary>
public sealed class ShellWriteFlowTests
{
    private const string WindowsEntry = "Boot0002 (Windows Boot Manager)";

    /// <summary>The real writer composed over the Windows write seam, as LiveFirmwareSource.CreateWindows must write it once.</summary>
    private const string RealWriterConstruction = @"\bnew\s+FirmwareVariableWriter\s*\(\s*WindowsNativeFirmwareWriteApi\s*\.\s*Instance\b";

    /// <summary>The real writer constructed over any seam.</summary>
    private const string AnyWriterConstruction = @"\bnew\s+FirmwareVariableWriter\s*\(";

    /// <summary>The real suspender, as LiveFirmwareSource.CreateWindows must construct it once.</summary>
    private const string RealSuspenderConstruction = @"\bnew\s+BitLockerSuspender\s*\(";

    private static readonly string UsbEntry = "Boot0003 (" + UsbStickDescription + ")";

    // ---- Review change ----------------------------------------------------------------------------

    [Fact]
    public async Task ReviewChange_Confirmed_WritesTheShownPlanOnce_SaysItOnceWithTheLoadedSentence_AndFocusesTheRow()
    {
        using WriteFlowRig rig = new(bitLocker: TransactionFakes.BitLockerOn());
        await rig.LoadAndShow("BootOrder");
        rig.MoveUsbUp();
        string? statusAtDialog = null;
        bool? writingAtDialog = null;
        rig.Platform.ConfirmAnswer = dialog =>
        {
            statusAtDialog = rig.Shell.StatusText;
            writingAtDialog = rig.Shell.IsWriting;
            return dialog.Confirm();
        };
        int before = rig.Announcer.History.Count;
        rig.Focus.Clear();

        await Execute(rig.Change.ReviewChangeCommand);

        ConfirmChangeViewModel shown = Assert.Single(rig.Platform.ConfirmDialogs);
        Assert.Equal(Strings.CheckingStatus, statusAtDialog);
        Assert.False(writingAtDialog);
        ChangePlan plan = shown.Plan;
        Assert.Equal(ChangeOperation.Reorder, plan.Operation);
        Assert.Equal("Change the boot order: " + UsbEntry + " moves from position 2 to 1 of 2. And suspend BitLocker for one restart.", shown.WhatWillChangeText);

        // One call, of the plan the dialog showed; the ticked box suspended BitLocker once.
        WritePlan written = Assert.Single(rig.Transaction.Writer.Calls);
        Assert.Equal(plan.After.Bytes, written.Bytes);
        Assert.Single(rig.Transaction.Suspender.Calls);

        string sentence = CheckSentences.Format(TransactionSentences.VerifiedReorderFormat, UsbEntry + ", " + WindowsEntry, plan.SnapshotFolderName) + " " + TransactionSentences.SuspendedForOneRestart;
        string expected = sentence[..^1] + ". Loaded " + rig.VariableCount + " variables from Live firmware (NtEnumerate)";
        Assert.Equal(expected, rig.Shell.StatusText);
        Assert.Equal(new[] { Strings.CheckingStatus, Strings.WritingStatus, expected }, rig.Announcer.History.Skip(before));
        Assert.Equal(new[] { FocusTarget.Status, FocusTarget.ListRowByKey }, rig.Focus.Requests);
        Assert.Equal(Whitelist.BootOrder.Key, rig.Shell.FocusRowKey);
        Assert.Equal(Whitelist.BootOrder.Key, rig.Shell.List.SelectedItem?.Key);

        // The live reload: the list and the proposed order show the firmware's new order.
        Assert.Equal(2, rig.Source.LoadCalls);
        Assert.False(rig.Shell.IsWriting);
        Assert.False(rig.Change.Editor.HasChanges);
        Assert.Equal(new[] { "Boot0003", "Boot0002" }, rig.Change.Editor.Entries.Select(entry => entry.Name));
        Assert.True(rig.Change.ShowsRestore);
        Assert.Contains(rig.Transaction.Records.ReadAll(), record => record.Outcome == WriteRecordOutcome.Verified);
    }

    /// <summary>
    /// The input trace (alpha1.4, --trace-input) changes nothing in a change flow: with it on, a
    /// confirmed Review change writes the plan the dialog showed once and says the same sentences,
    /// and the shell's trace has only its SELECTION and DETAIL lines, among them the flush the
    /// Change command asks for, named after it, and nothing of the plan.
    /// </summary>
    [Fact]
    public async Task WithTheInputTraceOn_AConfirmedChangeIsWrittenAsWithout_AndTheTraceHoldsOnlyTheShellsOwnLines()
    {
        using WriteFlowRig rig = new(bitLocker: TransactionFakes.BitLockerOn());
        RecordingInputTrace trace = new();
        rig.Shell.InputTrace = trace;
        await rig.LoadAndShow("BootOrder");
        rig.MoveUsbUp();
        rig.Platform.ConfirmAnswer = dialog => dialog.Confirm();
        int before = rig.Announcer.History.Count;

        await Execute(rig.Change.ReviewChangeCommand);

        ConfirmChangeViewModel shown = Assert.Single(rig.Platform.ConfirmDialogs);
        WritePlan written = Assert.Single(rig.Transaction.Writer.Calls);
        Assert.Equal(shown.Plan.After.Bytes, written.Bytes);
        Assert.Single(rig.Transaction.Suspender.Calls);
        Assert.Contains(rig.Transaction.Records.ReadAll(), record => record.Outcome == WriteRecordOutcome.Verified);
        Assert.Equal(3, rig.Announcer.History.Count - before);
        Assert.Equal(new[] { Strings.CheckingStatus, Strings.WritingStatus }, rig.Announcer.History.Skip(before).Take(2));

        Assert.Contains((TraceKind.Detail, "flush requested by RunChangeAsync (a Change command)"), trace.Lines);
        Assert.All(trace.Lines, line => Assert.True(line.Kind is TraceKind.Selection or TraceKind.Detail, line.Kind + " " + line.Details));
        Assert.DoesNotContain(trace.Lines, line => line.Details.Contains(shown.Plan.SnapshotFolderName, StringComparison.Ordinal));
        Assert.DoesNotContain(trace.Lines, line => line.Details.Contains(UsbStickDescription, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReviewChange_ConfirmedWithTheBoxCleared_KeepsProtection_AndNeverSuspends()
    {
        using WriteFlowRig rig = new(bitLocker: TransactionFakes.BitLockerOn());
        await rig.LoadAndShow("BootOrder");
        rig.MoveUsbUp();
        string? buttonName = null;
        rig.Platform.ConfirmAnswer = dialog =>
        {
            dialog.SuspendChosen = false;
            buttonName = dialog.ConfirmButtonName;
            return dialog.Confirm();
        };

        await Execute(rig.Change.ReviewChangeCommand);

        Assert.Equal(Strings.ConfirmChangeName, buttonName);
        Assert.Single(rig.Transaction.Writer.Calls);
        Assert.Empty(rig.Transaction.Suspender.Calls);
        Assert.DoesNotContain(TransactionSentences.SuspendedForOneRestart, rig.Shell.StatusText, StringComparison.Ordinal);
        Assert.StartsWith("Boot order changed to " + UsbEntry + ", " + WindowsEntry + ". Restart to use the new order. Snapshot ", rig.Shell.StatusText, StringComparison.Ordinal);
        Assert.Equal(BitLockerChoice.KeepProtection, rig.Transaction.Records.ReadAll().Last().BitLockerChoice);
    }

    [Fact]
    public async Task FC29_TheDialogCancelled_WritesNothing_RecordsOneCancel_AndReturnsFocusToTheButton()
    {
        using WriteFlowRig rig = new(bitLocker: TransactionFakes.BitLockerOn());
        await rig.LoadAndShow("BootOrder");
        rig.MoveUsbUp();
        rig.Focus.Clear();

        await Execute(rig.Change.ReviewChangeCommand);

        Assert.Single(rig.Platform.ConfirmDialogs);
        Assert.Equal(Strings.DialogCancelledStatus, rig.Shell.StatusText);
        Assert.Equal("Cancelled; nothing changed.", rig.Shell.StatusText);
        Assert.Equal(new[] { FocusTarget.ChangeControl }, rig.Focus.Requests);
        Assert.Equal(ChangeAction.ReviewChange, rig.Change.LastAction);
        Assert.Empty(rig.Transaction.Writer.Calls);
        Assert.Empty(rig.Transaction.Suspender.Calls);
        Assert.Empty(rig.Transaction.TransactionFolders());
        IReadOnlyList<WriteRecord> records = rig.Transaction.Records.ReadAll();
        WriteRecord cancelled = Assert.Single(records, record => record.Outcome == WriteRecordOutcome.Cancelled);
        Assert.False(cancelled.ApiCalled);
        Assert.Null(cancelled.SnapshotFolder);
        Assert.DoesNotContain(records, record => record.ApiCalled);

        // Nothing was read again, and the proposed order is kept for another try.
        Assert.Equal(1, rig.Source.LoadCalls);
        Assert.True(rig.Change.Editor.HasChanges);
        Assert.True(rig.Change.ReviewChangeCommand.CanExecute(null));
    }

    [Fact]
    public async Task ReviewChange_WithTheFirmwaresOrder_SaysNothingToChange_AndChecksNothing()
    {
        using WriteFlowRig rig = new();
        await rig.LoadAndShow("BootOrder");
        rig.Focus.Clear();

        await Execute(rig.Change.ReviewChangeCommand);

        Assert.Equal("Nothing to change: the proposed order is the firmware's order.", rig.Shell.StatusText);
        Assert.Empty(rig.Focus.Requests);
        Assert.Empty(rig.Platform.ConfirmDialogs);
        Assert.Empty(rig.Transaction.Records.ReadAll());
        Assert.False(rig.Change.IsChecking);
    }

    [Fact]
    public async Task AMove_IsSaidOncePerMove_AndCtrlZ_PutsTheOrderBack()
    {
        using WriteFlowRig rig = new();
        await rig.LoadAndShow("BootOrder");
        int before = rig.Announcer.History.Count;
        rig.Focus.Clear();

        rig.MoveUsbUp();
        rig.Change.MoveUpCommand.Execute(null);
        rig.Change.ResetOrderCommand.Execute(null);

        string moved = UsbEntry + " moved to position 1 of 2.";
        Assert.Equal(new[] { moved, UsbEntry + " is already first.", Strings.OrderResetStatus }, rig.Announcer.History.Skip(before));
        Assert.Single(rig.Shell.Log.Lines, line => line.Message == moved);
        Assert.Equal(new[] { FocusTarget.BootOrderEntry }, rig.Focus.Requests);
        Assert.False(rig.Change.Editor.HasChanges);
    }

    // ---- refusals ---------------------------------------------------------------------------------

    [Fact]
    public async Task TheChecksRefuse_ChangeBlocked_WithThePipelinesReason_FocusOnStatus_AndNoDialog()
    {
        // Boot0000 is a second Windows Boot Manager on another partition: rule V13 refuses it while
        // BitLocker protectors exist (FC-16).
        using WriteFlowRig rig = new(bitLocker: TransactionFakes.BitLockerOn());
        await rig.LoadAndShow("Boot0000");
        Assert.True(rig.Change.ShowsBootOnce);
        rig.Focus.Clear();

        await Execute(rig.Change.BootOnceCommand);

        string reason = CheckSentences.Format(CheckSentences.OtherWindowsPathFormat, "Boot0000");
        Assert.Equal("Change blocked: " + reason + " Nothing was written.", rig.Shell.StatusText);
        Assert.Equal(new[] { FocusTarget.Status }, rig.Focus.Requests);
        Assert.Empty(rig.Platform.ConfirmDialogs);
        Assert.Empty(rig.Transaction.Writer.Calls);
        Assert.Equal(CheckNames.OtherWindowsPath, Assert.Single(rig.Transaction.Records.ReadAll()).BlockedBy);
    }

    [Fact]
    public async Task OnAnotherMachine_TheDialogOpens_AndConfirmChangeIsRefused_WithNothingWritten()
    {
        using WriteFlowRig rig = new(machineConfirmed: false);
        await rig.LoadAndShow("BootOrder");
        Assert.Equal(WritabilitySentences.WrongMachine, rig.Shell.Detail.ReadOnlyStatus);
        Assert.True(rig.Change.IsVisible);
        rig.MoveUsbUp();
        rig.Platform.ConfirmAnswer = dialog => dialog.Confirm();
        rig.Focus.Clear();

        await Execute(rig.Change.ReviewChangeCommand);

        ConfirmChangeViewModel shown = Assert.Single(rig.Platform.ConfirmDialogs);
        Assert.Contains(WritabilitySentences.WrongMachine, shown.WarningsText, StringComparison.Ordinal);
        Assert.Equal("Change blocked: " + CheckSentences.WrongMachine, rig.Shell.StatusText);
        Assert.Equal(new[] { FocusTarget.Status, FocusTarget.Status }, rig.Focus.Requests);
        Assert.Empty(rig.Transaction.Writer.Calls);
        Assert.Empty(rig.Transaction.TransactionFolders());
        Assert.Equal(1, rig.Source.LoadCalls);
    }

    /// <summary>
    /// the tester's case (decided 25 September 2026): on another OptiPlex 7020 on BIOS A18, BootOrder and
    /// BootNext carry the Writability line and the item status of a machine that is not the M3
    /// test unit; the same store and identity on the test unit's machine ID are changeable.
    /// </summary>
    [Fact]
    public async Task OnAnotherOptiPlex7020OnA18_BootOrderAndBootNext_AreChangeableOnlyOnTheTestUnit()
    {
        using WriteFlowRig other = new(Store(Var("BootNext", U16(UsbNumber))), machineId: OtherUnitMachineId);
        using WriteFlowRig unit = new(Store(Var("BootNext", U16(UsbNumber))));

        await other.LoadAndShow("BootOrder");
        await unit.LoadAndShow("BootOrder");

        VariableItemViewModel bootOrder = Assert.IsType<VariableItemViewModel>(other.Shell.List.FindLoaded(Whitelist.BootOrder.Key));
        VariableItemViewModel bootNext = Assert.IsType<VariableItemViewModel>(other.Shell.List.FindLoaded(Whitelist.BootNext.Key));
        Assert.Equal(WritabilitySentences.WrongMachine, other.Shell.Detail.ReadOnlyStatus);
        Assert.Equal("Cannot be changed on this machine: changes are enabled only on one specific OptiPlex 7020 with BIOS A18 (the enabled unit). The Change controls and the dialog work up to Confirm change, which refuses; nothing is written.", other.Shell.Detail.ReadOnlyStatus);
        Assert.Equal(Writability.WrongMachine, bootOrder.Writability.Verdict);
        Assert.Equal(Writability.WrongMachine, bootNext.Writability.Verdict);
        Assert.Equal("Not changeable on this machine", bootOrder.ItemStatus);
        Assert.Equal(WritabilitySentences.ItemStatusElsewhere, bootNext.ItemStatus);
        Assert.StartsWith("BootOrder, not changeable on this machine, ", bootOrder.AccessibleName, StringComparison.Ordinal);
        Assert.True(other.Change.IsVisible);

        Assert.Equal(WritabilitySentences.ReorderOnly, unit.Shell.Detail.ReadOnlyStatus);
        Assert.Equal(WritabilitySentences.ItemStatusReorder, unit.Shell.List.FindLoaded(Whitelist.BootOrder.Key)!.ItemStatus);
        Assert.Equal(WritabilitySentences.ItemStatusOneTimeBoot, unit.Shell.List.FindLoaded(Whitelist.BootNext.Key)!.ItemStatus);
    }

    /// <summary>
    /// the tester's case: Confirm change on another OptiPlex 7020 on A18 opens the dialog with the
    /// Writability line among its warnings and is then refused at the unit check, with BitLocker on
    /// and the suspend ticked: nothing is suspended, snapshotted or written, and the record says
    /// the model was confirmed and the unit was not.
    /// </summary>
    [Fact]
    public async Task OnAnotherOptiPlex7020OnA18_ConfirmChange_IsRefusedAtTheUnitCheck_WithNothingWritten()
    {
        using WriteFlowRig rig = new(bitLocker: TransactionFakes.BitLockerOn(), machineId: OtherUnitMachineId);
        await rig.LoadAndShow("BootOrder");
        rig.MoveUsbUp();
        rig.Platform.ConfirmAnswer = dialog => dialog.Confirm();
        rig.Focus.Clear();

        await Execute(rig.Change.ReviewChangeCommand);

        ConfirmChangeViewModel shown = Assert.Single(rig.Platform.ConfirmDialogs);
        Assert.Contains(WritabilitySentences.WrongMachine, shown.WarningsText, StringComparison.Ordinal);
        Assert.Equal("Change blocked: " + CheckSentences.NotEnabledUnit, rig.Shell.StatusText);
        Assert.Equal(new[] { FocusTarget.Status, FocusTarget.Status }, rig.Focus.Requests);
        Assert.Empty(rig.Transaction.Suspender.Calls);
        Assert.Empty(rig.Transaction.Writer.Calls);
        Assert.Empty(rig.Transaction.TransactionFolders());
        WriteRecord line = rig.Transaction.Records.ReadAll()[^1];
        Assert.Equal(WriteRecordOutcome.Blocked, line.Outcome);
        Assert.Equal(CheckNames.ExpectedUnit, line.BlockedBy);
        Assert.Equal(CheckSentences.NotEnabledUnit, line.Sentence);
        Assert.True(line.Machine!.Confirmed);
        Assert.False(line.Machine.UnitConfirmed);
        Assert.Equal(1, rig.Source.LoadCalls);
    }

    [Fact]
    public async Task BlockedAfterTheSuspend_TheSentenceEndsBySayingBitLockerStaysSuspended()
    {
        using WriteFlowRig rig = new(bitLocker: TransactionFakes.BitLockerOn());
        await rig.LoadAndShow("BootOrder");
        rig.MoveUsbUp();
        rig.Platform.ConfirmAnswer = dialog => dialog.Confirm();
        rig.Transaction.Suspender.DuringCall = () => rig.Transaction.Store.Set(Var("BootOrder", BootList(UsbNumber, WindowsOnPartition1)));
        rig.Focus.Clear();

        await Execute(rig.Change.ReviewChangeCommand);

        Assert.Equal(
            "Change blocked: " + CheckSentences.Format(CheckSentences.StaleReadFormat, "BootOrder") + " " + CheckSentences.BitLockerLeftSuspended,
            rig.Shell.StatusText);
        Assert.EndsWith("BitLocker is suspended until the next restart, which seals it to that start's boot path.", rig.Shell.StatusText, StringComparison.Ordinal);
        Assert.Single(rig.Transaction.Suspender.Calls);
        Assert.Empty(rig.Transaction.Writer.Calls);
        Assert.Equal(new[] { FocusTarget.Status, FocusTarget.Status }, rig.Focus.Requests);
        Assert.Equal(1, rig.Source.LoadCalls);
    }

    [Fact]
    public async Task TheFirmwareRefuses_TheWriteStatusSentenceAndTheSnapshot_ThenTheReload_FocusOnStatus()
    {
        using WriteFlowRig rig = new();
        await rig.LoadAndShow("BootOrder");
        rig.MoveUsbUp();
        rig.Platform.ConfirmAnswer = dialog => dialog.Confirm();
        rig.Transaction.Writer.Win32Error = 19;
        rig.Focus.Clear();

        await Execute(rig.Change.ReviewChangeCommand);

        WriteRecord record = rig.Transaction.Records.ReadAll().Last();
        Assert.Equal(WriteRecordOutcome.ApiRefused, record.Outcome);
        Assert.StartsWith("The firmware refused the change: the variable is write-protected (error 19). Nothing was changed. Snapshot ", rig.Shell.StatusText, StringComparison.Ordinal);

        // The reload shows the firmware's order again, so the proposed order that was not written is
        // dropped, and that is said between the write's sentence and the Loaded sentence.
        Assert.Equal(record.Sentence[..^1] + ". Proposed order discarded. Loaded " + rig.VariableCount + " variables from Live firmware (NtEnumerate)", rig.Shell.StatusText);
        Assert.Equal(new[] { FocusTarget.Status, FocusTarget.Status }, rig.Focus.Requests);
        Assert.Equal(2, rig.Source.LoadCalls);
        Assert.Single(rig.Transaction.Writer.Calls);
        Assert.False(rig.Change.Editor.HasChanges);
    }

    [Fact]
    public async Task AChangeThatCannotBeVerified_SaysSo_ThenTheReload_FocusOnStatus()
    {
        using WriteFlowRig rig = new();
        await rig.LoadAndShow("BootOrder");
        rig.MoveUsbUp();
        rig.Platform.ConfirmAnswer = dialog => dialog.Confirm();
        rig.Transaction.Writer.ChangesTheStore = false;
        rig.Focus.Clear();

        await Execute(rig.Change.ReviewChangeCommand);

        Assert.StartsWith("The change was written but could not be verified: ", rig.Shell.StatusText, StringComparison.Ordinal);

        // The firmware still holds the old order, so the reload drops the proposed order and says so.
        Assert.Contains("Use Restore previous value (Alt+P) or check with uefiaccess boot. Proposed order discarded. Loaded ", rig.Shell.StatusText, StringComparison.Ordinal);
        Assert.Equal(new[] { FocusTarget.Status, FocusTarget.Status }, rig.Focus.Requests);
        Assert.Equal(WriteRecordOutcome.Unverified, rig.Transaction.Records.ReadAll().Last().Outcome);
    }

    [Fact]
    public async Task TheReloadAfterAWriteFails_SaysTheWritesSentenceThenLoadingFailed()
    {
        using WriteFlowRig rig = new();
        await rig.LoadAndShow("BootOrder");
        rig.MoveUsbUp();
        rig.Platform.ConfirmAnswer = dialog => dialog.Confirm();
        rig.Source.FailNextLoad = "the reload failed";
        rig.Focus.Clear();

        await Execute(rig.Change.ReviewChangeCommand);

        string written = rig.Transaction.Records.ReadAll().Last().Sentence;
        Assert.StartsWith("Boot order changed to ", written, StringComparison.Ordinal);
        Assert.Equal(written[..^1] + ". Loading failed: the reload failed", rig.Shell.StatusText);
        Assert.Equal(new[] { FocusTarget.Status, FocusTarget.Status }, rig.Focus.Requests);
        Assert.Equal(ShellState.Failed, rig.Shell.State);
    }

    // ---- one-time boot and restore ------------------------------------------------------------------

    [Fact]
    public async Task BootOnce_Confirmed_WritesBootNext_AndFocusesItsNewRow_WhichOffersClearAndRestore()
    {
        using WriteFlowRig rig = new();
        await rig.LoadAndShow("Boot0003");
        Assert.Equal(WritabilitySentences.OneTimeBootTarget, rig.Shell.Detail.ReadOnlyStatus);
        rig.Platform.ConfirmAnswer = dialog => dialog.Confirm();
        rig.Focus.Clear();

        await Execute(rig.Change.BootOnceCommand);

        ConfirmChangeViewModel shown = Assert.Single(rig.Platform.ConfirmDialogs);
        Assert.Equal("Start " + UsbEntry + " once at the next restart, then use the normal order.", shown.WhatWillChangeText);
        Assert.False(shown.SuspendOffered);
        ChangePlan plan = shown.Plan;
        string sentence = CheckSentences.Format(TransactionSentences.VerifiedBootNextFormat, UsbEntry, plan.SnapshotFolderName);
        Assert.Equal(sentence[..^1] + ". Loaded " + rig.VariableCount + " variables from Live firmware (NtEnumerate)", rig.Shell.StatusText);
        Assert.Equal(new[] { FocusTarget.Status, FocusTarget.ListRowByKey }, rig.Focus.Requests);
        Assert.Equal(Whitelist.BootNext.Key, rig.Shell.FocusRowKey);
        Assert.Equal("BootNext", rig.Shell.List.SelectedItem?.Name);
        Assert.Equal(WritabilitySentences.OneTimeBoot, rig.Shell.Detail.ReadOnlyStatus);
        Assert.True(rig.Change.ShowsClearBootNext);
        Assert.True(rig.Change.ShowsRestore);
        Assert.Equal(WritabilitySentences.ItemStatusOneTimeBoot, rig.Shell.List.SelectedItem?.ItemStatus);
    }

    /// <summary>
    /// Alpha 2 finding A2-7: the detail pane follows the selection only once the list has been quiet
    /// for the quiet period, and the Change commands take their variable from the pane. A command
    /// reached before the pane has caught up (an access key pressed within the quiet period, a
    /// screen reader's Invoke) brings the pane up to date first and acts on the selected row, not on
    /// the row the pane showed before.
    /// </summary>
    [Fact]
    public async Task BootOnce_WhileThePaneStillShowsThePreviousRow_ActsOnTheSelectedRow()
    {
        RecordingDetailScheduler scheduler = new();
        using WriteFlowRig rig = new(detailScheduler: scheduler);
        await rig.LoadAndShow("Boot0000");
        Assert.True(rig.Change.ShowsBootOnce);
        Assert.True(rig.Shell.List.TrySelectByKey(FirmwareVariable.MakeKey(KnownGuids.EfiGlobalVariable, "Boot0003")));
        Assert.True(scheduler.IsPending);
        Assert.Equal("Boot0000", rig.Change.Item?.Name);

        await Execute(rig.Change.BootOnceCommand);

        Assert.False(scheduler.IsPending);
        Assert.Equal("Boot0003", rig.Shell.Detail.Item?.Name);
        Assert.Equal("Boot0003", rig.Change.Item?.Name);
        ConfirmChangeViewModel shown = Assert.Single(rig.Platform.ConfirmDialogs);
        Assert.Equal("Start " + UsbEntry + " once at the next restart, then use the normal order.", shown.WhatWillChangeText);
        Assert.Equal(Strings.DialogCancelledStatus, rig.Shell.StatusText);
        Assert.Empty(rig.Transaction.Writer.Calls);
    }

    /// <summary>
    /// The same, when the row selected meanwhile offers no such change (Timeout, which has no Change
    /// group): after the pane has caught up the command does nothing and says nothing, as the access
    /// key of a hidden button does, and no check runs.
    /// </summary>
    [Fact]
    public async Task BootOnce_WhenTheRowSelectedMeanwhileDoesNotOfferIt_ChecksNothing_AndSaysNothing()
    {
        RecordingDetailScheduler scheduler = new();
        using WriteFlowRig rig = new(detailScheduler: scheduler);
        await rig.LoadAndShow("Boot0003");
        Assert.True(rig.Change.BootOnceCommand.CanExecute(null));
        Assert.True(rig.Shell.List.TrySelectByKey(FirmwareVariable.MakeKey(KnownGuids.EfiGlobalVariable, "Timeout")));
        Assert.True(scheduler.IsPending);
        int before = rig.Announcer.History.Count;
        string status = rig.Shell.StatusText;
        rig.Focus.Clear();

        await Execute(rig.Change.BootOnceCommand);

        Assert.False(scheduler.IsPending);
        Assert.Equal("Timeout", rig.Shell.Detail.Item?.Name);
        Assert.False(rig.Change.ShowsBootOnce);
        Assert.False(rig.Change.IsVisible);
        Assert.Equal(before, rig.Announcer.History.Count);
        Assert.Equal(status, rig.Shell.StatusText);
        Assert.Empty(rig.Focus.Requests);
        Assert.Empty(rig.Platform.ConfirmDialogs);
        Assert.Empty(rig.Transaction.Records.ReadAll());
        Assert.False(rig.Change.IsChecking);
    }

    [Fact]
    public async Task ClearOneTimeBoot_Confirmed_RemovesBootNext_AndFocusGoesToTheFirstRow()
    {
        using WriteFlowRig rig = new(Store(Var("BootNext", U16(UsbNumber))), TransactionFakes.BitLockerOn());
        await rig.LoadAndShow("BootNext");
        rig.Platform.ConfirmAnswer = dialog => dialog.Confirm();
        rig.Focus.Clear();

        await Execute(rig.Change.ClearBootNextCommand);

        ConfirmChangeViewModel shown = Assert.Single(rig.Platform.ConfirmDialogs);
        Assert.False(shown.SuspendOffered);
        ChangePlan plan = shown.Plan;
        string sentence = CheckSentences.Format(TransactionSentences.VerifiedClearFormat, plan.SnapshotFolderName);
        Assert.Equal(sentence[..^1] + ". Loaded " + rig.VariableCount + " variables from Live firmware (NtEnumerate)", rig.Shell.StatusText);
        Assert.Equal(new[] { FocusTarget.Status, FocusTarget.ListFirstRow }, rig.Focus.Requests);
        Assert.Null(rig.Transaction.Store.Current("BootNext"));
        Assert.Empty(rig.Transaction.Suspender.Calls);
    }

    [Fact]
    public async Task RestorePrevious_PutsTheOrderBack_ThroughTheSameChecksAndDialog()
    {
        using WriteFlowRig rig = new();
        await rig.LoadAndShow("BootOrder");
        rig.MoveUsbUp();
        rig.Platform.ConfirmAnswer = dialog => dialog.Confirm();
        await Execute(rig.Change.ReviewChangeCommand);
        string first = Assert.Single(rig.Platform.ConfirmDialogs).Plan.SnapshotFolderName;
        Assert.True(rig.Change.ShowsRestore);
        rig.Focus.Clear();

        await Execute(rig.Change.RestorePreviousCommand);

        ConfirmChangeViewModel restore = rig.Platform.ConfirmDialogs[^1];
        Assert.Equal("Restore the boot order recorded in snapshot " + first + "; the After box lists it.", restore.WhatWillChangeText);
        string sentence = CheckSentences.Format(TransactionSentences.RestoredReorderFormat, WindowsEntry + ", " + UsbEntry, restore.Plan.SnapshotFolderName);
        Assert.Equal(sentence[..^1] + ". Loaded " + rig.VariableCount + " variables from Live firmware (NtEnumerate)", rig.Shell.StatusText);
        Assert.EndsWith("-restore", restore.Plan.SnapshotFolderName, StringComparison.Ordinal);
        Assert.Equal(new[] { FocusTarget.Status, FocusTarget.ListRowByKey }, rig.Focus.Requests);
        Assert.Equal(new[] { "Boot0002", "Boot0003" }, rig.Change.Editor.Entries.Select(entry => entry.Name));
        Assert.Equal(2, rig.Transaction.Writer.Calls.Count);
    }

    [Fact]
    public async Task RestorePrevious_WithoutASnapshot_SaysThisApplicationHasNotChangedTheVariable()
    {
        using WriteFlowRig rig = new();
        await rig.LoadAndShow("BootOrder");
        rig.MoveUsbUp();
        rig.Platform.ConfirmAnswer = dialog => dialog.Confirm();
        await Execute(rig.Change.ReviewChangeCommand);
        Assert.True(rig.Change.ShowsRestore);
        TestFolders.DeleteQuietly(rig.Transaction.SnapshotRoot);
        int dialogs = rig.Platform.ConfirmDialogs.Count;
        rig.Focus.Clear();

        await Execute(rig.Change.RestorePreviousCommand);

        Assert.Equal("No snapshot to restore: this application has not changed BootOrder on this machine.", rig.Shell.StatusText);
        Assert.Empty(rig.Focus.Requests);
        Assert.Equal(dialogs, rig.Platform.ConfirmDialogs.Count);
    }

    // ---- while a change is written ------------------------------------------------------------------

    [Fact]
    public async Task WhileAChangeIsWritten_EveryCommandThatCouldInterfereIsDisabled_AndTheWindowStaysOpen()
    {
        using WriteFlowRig rig = new();
        await rig.LoadAndShow("BootOrder");
        rig.MoveUsbUp();
        rig.Platform.ConfirmAnswer = dialog => dialog.Confirm();
        Dictionary<string, bool> during = new(StringComparer.Ordinal);
        Task? refreshWhileWriting = null;
        rig.Transaction.Writer.DuringCall = _ =>
        {
            during["IsWriting"] = rig.Shell.IsWriting;
            during["Refresh"] = rig.Shell.RefreshCommand.CanExecute(null);
            during["OpenBaseline"] = rig.Shell.OpenBaselineCommand.CanExecute(null);
            during["ExportVisibleList"] = rig.Shell.ExportVisibleListCommand.CanExecute(null);
            during["ClearFilter"] = rig.Shell.ClearFilterCommand.CanExecute(null);
            during["Filter"] = rig.Shell.IsFilterEnabled;
            during["CopyValue"] = rig.Shell.CopyValueCommand.CanExecute(null);
            during["Change"] = rig.ChangeCommands.Any(command => command.CanExecute(null));
            during["TryClose"] = rig.Shell.TryClose();
            refreshWhileWriting = rig.Shell.RefreshAsync();
        };
        List<string?> changed = new();
        rig.Shell.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await Execute(rig.Change.ReviewChangeCommand);

        Assert.True(during["IsWriting"]);
        Assert.False(during["Refresh"]);
        Assert.False(during["OpenBaseline"]);
        Assert.False(during["ExportVisibleList"]);
        Assert.False(during["ClearFilter"]);
        Assert.False(during["Filter"]);
        Assert.True(during["CopyValue"]);
        Assert.False(during["Change"]);
        Assert.False(during["TryClose"]);
        Assert.Contains("A change is being written; wait for its result.", rig.Announcer.History);
        Assert.True(Assert.IsAssignableFrom<Task>(refreshWhileWriting).IsCompletedSuccessfully);
        Assert.Equal(2, rig.Source.LoadCalls);

        // After the write everything is back, and the window may close.
        Assert.False(rig.Shell.IsWriting);
        Assert.True(rig.Shell.IsFilterEnabled);
        Assert.True(rig.Shell.RefreshCommand.CanExecute(null));
        Assert.True(rig.Shell.TryClose());
        Assert.Equal(2, changed.Count(name => name == nameof(ShellViewModel.IsWriting)));
        Assert.Contains(nameof(ShellViewModel.IsFilterEnabled), changed);
    }

    [Fact]
    public void TryClose_WhenNothingIsWritten_LetsTheWindowCloseAndSaysNothing()
    {
        using WriteFlowRig rig = new();
        int before = rig.Announcer.History.Count;

        Assert.True(rig.Shell.TryClose());
        Assert.Equal(before, rig.Announcer.History.Count);
    }

    [Fact]
    public async Task WhileTheListIsReadAgainAfterAWrite_NothingCanStartUntilTheResultIsSaid_ButTheWindowMayClose()
    {
        // The reload after a write says nothing until it ends (the machine identity alone may take
        // up to a minute), so Status still says the change is being written: the commands stay
        // disabled until the result is said, and no second change can start before it
        // (docs/Keyboard-Map.md section 9, the Confirm change row). The commit has returned, so the
        // window may close; nothing is held (the reload's own sentence is the result).
        using WriteFlowRig rig = new(synchronous: true);
        await rig.LoadAndShow("BootOrder");
        rig.MoveUsbUp();
        rig.Platform.ConfirmAnswer = dialog => dialog.Confirm();
        rig.Synchronous!.AfterCommit = () => rig.Source.Hold();
        List<string?> changed = new();
        rig.Shell.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        int before = rig.Announcer.History.Count;
        rig.Focus.Clear();

        Task review = Execute(rig.Change.ReviewChangeCommand);

        Assert.False(review.IsCompleted);
        Assert.Single(rig.Transaction.Writer.Calls);
        Assert.False(rig.Shell.IsWriting);
        Assert.True(rig.Shell.IsFinishingChange);
        Assert.True(rig.Shell.IsBusy);
        Assert.Equal(Strings.WritingStatus, rig.Shell.StatusText);
        Assert.All(rig.ChangeCommands, command => Assert.False(command.CanExecute(null)));
        Assert.False(rig.Shell.RefreshCommand.CanExecute(null));
        Assert.False(rig.Shell.OpenBaselineCommand.CanExecute(null));
        Assert.False(rig.Shell.ClearFilterCommand.CanExecute(null));
        Assert.False(rig.Shell.ExportVisibleListCommand.CanExecute(null));
        Assert.False(rig.Shell.IsFilterEnabled);
        Assert.True(rig.Shell.CopyValueCommand.CanExecute(null));
        int quiet = rig.Announcer.History.Count;
        Assert.True(rig.Shell.TryClose());
        Assert.Equal(quiet, rig.Announcer.History.Count);

        // Ctrl+F finds the filter box disabled: Status says why instead of nothing.
        rig.Shell.OnFilterUnavailable();
        Assert.Equal("The filter is unavailable while a change is written; wait for its result.", rig.Shell.StatusText);

        rig.Source.Release();
        await review;

        ChangePlan plan = Assert.Single(rig.Platform.ConfirmDialogs).Plan;
        string sentence = CheckSentences.Format(TransactionSentences.VerifiedReorderFormat, UsbEntry + ", " + WindowsEntry, plan.SnapshotFolderName);
        string expected = sentence[..^1] + ". Loaded " + rig.VariableCount + " variables from Live firmware (NtEnumerate)";
        Assert.Equal(expected, rig.Shell.StatusText);
        Assert.Equal(new[] { Strings.CheckingStatus, Strings.WritingStatus, Strings.FilterUnavailableStatus, expected }, rig.Announcer.History.Skip(before));
        Assert.Equal(new[] { FocusTarget.Status, FocusTarget.ListRowByKey }, rig.Focus.Requests);

        // Once the result is said, everything is back.
        Assert.False(rig.Shell.IsFinishingChange);
        Assert.True(rig.Shell.IsFilterEnabled);
        Assert.True(rig.Shell.RefreshCommand.CanExecute(null));
        Assert.True(rig.Shell.OpenBaselineCommand.CanExecute(null));
        Assert.True(rig.Shell.ClearFilterCommand.CanExecute(null));
        Assert.True(rig.Shell.ExportVisibleListCommand.CanExecute(null));
        Assert.True(rig.Change.ReviewChangeCommand.CanExecute(null));
        Assert.True(rig.Change.MoveUpCommand.CanExecute(null));
        Assert.Equal(2, changed.Count(name => name == nameof(ShellViewModel.IsFinishingChange)));
        Assert.Equal(2, rig.Source.LoadCalls);
    }

    [Fact]
    public async Task CtrlF_WhileAChangeIsWritten_SaysTheFilterIsUnavailable_AndOtherwiseSaysNothing()
    {
        using WriteFlowRig rig = new();
        await rig.LoadAndShow("BootOrder");
        int quiet = rig.Announcer.History.Count;

        // The filter box is enabled: a failed focus move is not the person's news.
        rig.Shell.OnFilterUnavailable();
        Assert.Equal(quiet, rig.Announcer.History.Count);

        rig.MoveUsbUp();
        rig.Platform.ConfirmAnswer = dialog => dialog.Confirm();
        string? statusDuringWrite = null;
        bool? filterDuringWrite = null;
        rig.Transaction.Writer.DuringCall = _ =>
        {
            filterDuringWrite = rig.Shell.IsFilterEnabled;
            rig.Shell.OnFilterUnavailable();
            statusDuringWrite = rig.Shell.StatusText;
        };
        rig.Focus.Clear();

        await Execute(rig.Change.ReviewChangeCommand);

        Assert.False(filterDuringWrite);
        Assert.Equal(Strings.FilterUnavailableStatus, statusDuringWrite);

        // It moves no focus, and the write's result is still said once, after it.
        Assert.Equal(new[] { FocusTarget.Status, FocusTarget.ListRowByKey }, rig.Focus.Requests);
        Assert.StartsWith("Boot order changed to " + UsbEntry, rig.Shell.StatusText, StringComparison.Ordinal);
        Assert.Single(rig.Transaction.Writer.Calls);
    }

    // ---- dialogs and loads ----------------------------------------------------------------------------

    [Fact]
    public async Task ALoadThatFinishesBehindTheDialog_IsHeld_AndSaidAfterCancel()
    {
        using WriteFlowRig rig = new(synchronous: true);
        await rig.LoadAndShow("BootOrder");
        rig.MoveUsbUp();
        rig.Source.Hold();
        Task loading = rig.Shell.RefreshAsync();
        string? statusWhileOpen = null;
        rig.Platform.WhileOpen = () =>
        {
            rig.Source.Release();
            statusWhileOpen = rig.Shell.StatusText;
        };
        rig.Focus.Clear();

        await Execute(rig.Change.ReviewChangeCommand);
        await loading;

        // The load finished behind the dialog: its sentence waited, and after Cancel it is the news
        // (it dropped the proposed order), with its own focus request.
        Assert.Equal(Strings.CheckingStatus, statusWhileOpen);
        string held = "Proposed order discarded. Loaded " + rig.VariableCount + " variables from Live firmware (NtEnumerate)";
        Assert.Equal(held, rig.Shell.StatusText);
        Assert.DoesNotContain(Strings.DialogCancelledStatus, rig.Announcer.History);
        Assert.Equal(new[] { FocusTarget.ListFirstRow }, rig.Focus.Requests);
        Assert.Empty(rig.Transaction.Writer.Calls);
        Assert.Single(rig.Transaction.Records.ReadAll(), record => record.Outcome == WriteRecordOutcome.Cancelled);
    }

    [Fact]
    public async Task ALoadThatFinishesBehindTheDialog_ThenConfirm_TheReloadAfterTheWriteIsWhatIsSaid()
    {
        using WriteFlowRig rig = new(synchronous: true);
        await rig.LoadAndShow("BootOrder");
        rig.MoveUsbUp();
        rig.Source.Hold();
        Task loading = rig.Shell.RefreshAsync();
        rig.Platform.WhileOpen = () => rig.Source.Release();
        rig.Platform.ConfirmAnswer = dialog => dialog.Confirm();
        rig.Focus.Clear();

        await Execute(rig.Change.ReviewChangeCommand);
        await loading;

        Assert.StartsWith("Boot order changed to " + UsbEntry, rig.Shell.StatusText, StringComparison.Ordinal);
        Assert.EndsWith(". Loaded " + rig.VariableCount + " variables from Live firmware (NtEnumerate)", rig.Shell.StatusText, StringComparison.Ordinal);
        Assert.DoesNotContain(rig.Announcer.History, text => text.StartsWith(Strings.ProposedOrderDiscarded, StringComparison.Ordinal));
        Assert.Equal(new[] { FocusTarget.Status, FocusTarget.ListRowByKey }, rig.Focus.Requests);
        Assert.Single(rig.Transaction.Writer.Calls);
    }

    [Fact]
    public async Task ALoadThatFinishesWhileTheChangeIsWritten_IsHeld_AndTheReloadAfterTheWriteIsWhatIsSaid()
    {
        // Refresh pressed while the checks ran (it stays enabled then) is still reading at Confirm
        // change, and its result arrives while the change is written. Status keeps the Writing
        // sentence and focus stays on the Status box until the result is in; the reload after the write replaces that load, so its sentence is never said.
        using WriteFlowRig rig = new(synchronous: true);
        await rig.LoadAndShow("BootOrder");
        rig.MoveUsbUp();
        rig.Source.Hold();
        Task loading = rig.Shell.RefreshAsync();
        rig.Platform.ConfirmAnswer = dialog => dialog.Confirm();
        string? statusDuringWrite = null;
        bool? writingDuringWrite = null;
        List<FocusTarget>? focusDuringWrite = null;
        rig.Synchronous!.AfterCommit = () =>
        {
            rig.Source.Release();
            statusDuringWrite = rig.Shell.StatusText;
            writingDuringWrite = rig.Shell.IsWriting;
            focusDuringWrite = rig.Focus.Requests.ToList();
        };
        int before = rig.Announcer.History.Count;
        rig.Focus.Clear();

        await Execute(rig.Change.ReviewChangeCommand);
        await loading;

        Assert.True(writingDuringWrite);
        Assert.Equal(Strings.WritingStatus, statusDuringWrite);
        Assert.Equal(new[] { FocusTarget.Status }, focusDuringWrite);

        ChangePlan plan = Assert.Single(rig.Platform.ConfirmDialogs).Plan;
        string sentence = CheckSentences.Format(TransactionSentences.VerifiedReorderFormat, UsbEntry + ", " + WindowsEntry, plan.SnapshotFolderName);
        string expected = sentence[..^1] + ". Loaded " + rig.VariableCount + " variables from Live firmware (NtEnumerate)";
        Assert.Equal(expected, rig.Shell.StatusText);
        Assert.Equal(new[] { Strings.CheckingStatus, Strings.WritingStatus, expected }, rig.Announcer.History.Skip(before));
        Assert.DoesNotContain(rig.Announcer.History, text => text.StartsWith(Strings.ProposedOrderDiscarded, StringComparison.Ordinal));
        Assert.Equal(new[] { FocusTarget.Status, FocusTarget.ListRowByKey }, rig.Focus.Requests);
        Assert.Equal(3, rig.Source.LoadCalls);
        Assert.Single(rig.Transaction.Writer.Calls);
        Assert.False(rig.Shell.IsBusy);
        Assert.Equal(ShellState.Loaded, rig.Shell.State);
    }

    [Fact]
    public async Task ALoadThatFinishesWhileTheChangeIsWritten_IsSaidAfterARefusalThatWroteNothing()
    {
        // On a machine that is not the contracted one Confirm change is refused before any call
        // (stage S11), so nothing is read again: the load that finished during the write is the
        // news after the refusal, and it dropped the proposed order.
        using WriteFlowRig rig = new(machineConfirmed: false, synchronous: true);
        await rig.LoadAndShow("BootOrder");
        rig.MoveUsbUp();
        rig.Source.Hold();
        Task loading = rig.Shell.RefreshAsync();
        rig.Platform.ConfirmAnswer = dialog => dialog.Confirm();
        string? statusDuringWrite = null;
        rig.Synchronous!.AfterCommit = () =>
        {
            rig.Source.Release();
            statusDuringWrite = rig.Shell.StatusText;
        };
        int before = rig.Announcer.History.Count;
        rig.Focus.Clear();

        await Execute(rig.Change.ReviewChangeCommand);
        await loading;

        Assert.Equal(Strings.WritingStatus, statusDuringWrite);
        string expected = "Change blocked: " + CheckSentences.WrongMachine + " Proposed order discarded. Loaded " + rig.VariableCount + " variables from Live firmware (NtEnumerate)";
        Assert.Equal(expected, rig.Shell.StatusText);
        Assert.Equal(new[] { Strings.CheckingStatus, Strings.WritingStatus, expected }, rig.Announcer.History.Skip(before));
        Assert.Equal(new[] { FocusTarget.Status, FocusTarget.Status }, rig.Focus.Requests);
        Assert.Empty(rig.Transaction.Writer.Calls);
        Assert.Equal(2, rig.Source.LoadCalls);
        Assert.False(rig.Change.Editor.HasChanges);
        Assert.False(rig.Shell.IsBusy);
    }

    [Fact]
    public async Task BootOnce_WhileAProposedOrderIsArranged_SaysTheOrderWasDiscarded_AfterTheWritesSentence()
    {
        // The proposed order is kept while another variable is shown; the reload after the one-time
        // boot drops it, and a load that drops an order says so (docs/Keyboard-Map.md section 7).
        using WriteFlowRig rig = new();
        await rig.LoadAndShow("BootOrder");
        rig.MoveUsbUp();
        Assert.True(rig.Shell.List.TrySelectByKey(FirmwareVariable.MakeKey(KnownGuids.EfiGlobalVariable, "Boot0003")));
        rig.Shell.EnsureDetailCurrent();
        Assert.True(rig.Change.Editor.HasChanges);
        Assert.True(rig.Change.ShowsBootOnce);
        rig.Platform.ConfirmAnswer = dialog => dialog.Confirm();
        rig.Focus.Clear();

        await Execute(rig.Change.BootOnceCommand);

        ChangePlan plan = Assert.Single(rig.Platform.ConfirmDialogs).Plan;
        Assert.Equal(ChangeOperation.SetBootNext, plan.Operation);
        string sentence = CheckSentences.Format(TransactionSentences.VerifiedBootNextFormat, UsbEntry, plan.SnapshotFolderName);
        Assert.Equal(sentence[..^1] + ". Proposed order discarded. Loaded " + rig.VariableCount + " variables from Live firmware (NtEnumerate)", rig.Shell.StatusText);
        Assert.Equal(new[] { FocusTarget.Status, FocusTarget.ListRowByKey }, rig.Focus.Requests);
        Assert.False(rig.Change.Editor.HasChanges);
        Assert.Equal(new[] { "Boot0002", "Boot0003" }, rig.Change.Editor.Entries.Select(entry => entry.Name));
    }

    [Fact]
    public async Task Escape_WhileTheChecksRun_CancelsThem_SaysCancelled_AndOpensNoDialog()
    {
        using WriteFlowRig rig = new();
        await rig.LoadAndShow("BootOrder");
        rig.MoveUsbUp();
        using ManualResetEventSlim entered = new(false);
        using ManualResetEventSlim release = new(false);
        rig.Transaction.Identity.DuringInspect = () =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(30));
        };
        rig.Focus.Clear();

        Task review = Execute(rig.Change.ReviewChangeCommand);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(30)), "the checks never reached the machine identity");
        Assert.True(rig.Change.IsChecking);
        Assert.Equal(Strings.CheckingStatus, rig.Shell.StatusText);
        Assert.False(rig.Change.ReviewChangeCommand.CanExecute(null));
        Assert.False(rig.Change.MoveUpCommand.CanExecute(null));
        Assert.True(rig.Change.CancelChecksCommand.CanExecute(null));

        Assert.True(rig.Change.CancelChecks());
        await review;

        Assert.Equal("Cancelled; nothing changed.", rig.Shell.StatusText);
        Assert.Empty(rig.Focus.Requests);
        Assert.Empty(rig.Platform.ConfirmDialogs);
        Assert.False(rig.Change.IsChecking);
        Assert.True(rig.Change.Editor.HasChanges);

        // The checks stop at their next stage boundary, where the pipeline records the cancel.
        release.Set();
        Assert.True(SpinWait.SpinUntil(() => CountOf(rig, WriteRecordOutcome.Cancelled) == 1, TimeSpan.FromSeconds(30)), "no cancelled record");
        Assert.Empty(rig.Transaction.TransactionFolders());
        Assert.Empty(rig.Transaction.Writer.Calls);
    }

    [Fact]
    public async Task APlanThatArrivesWithTheEscape_OpensNoDialog_AndIsRecordedAsCancelled()
    {
        // Escape after the checks' last stage boundary: the pipeline has made its plan, and the
        // service hands it back although the token is cancelled. It must neither open the dialog
        // nor stay pending.
        using WriteFlowRig rig = new(synchronous: true);
        await rig.LoadAndShow("BootOrder");
        rig.MoveUsbUp();
        PrepareResult? arrived = null;
        bool? cancelledByEscape = null;
        rig.Synchronous!.AfterPrepare = prepared =>
        {
            arrived = prepared;
            cancelledByEscape = rig.Change.CancelChecks();
        };
        rig.Platform.ConfirmAnswer = dialog => dialog.Confirm();
        int before = rig.Announcer.History.Count;
        rig.Focus.Clear();

        await Execute(rig.Change.ReviewChangeCommand);

        Assert.True(cancelledByEscape);
        Assert.IsType<ChangePlan>(arrived?.Plan);
        Assert.False(arrived?.IsCancelled);
        Assert.Empty(rig.Platform.ConfirmDialogs);
        Assert.Equal(new[] { Strings.CheckingStatus, Strings.DialogCancelledStatus }, rig.Announcer.History.Skip(before));
        Assert.Empty(rig.Focus.Requests);
        Assert.Equal(1, CountOf(rig, WriteRecordOutcome.Cancelled));
        Assert.Empty(rig.Transaction.TransactionFolders());
        Assert.Empty(rig.Transaction.Writer.Calls);
        Assert.Empty(rig.Transaction.Suspender.Calls);
        Assert.False(rig.Change.IsChecking);
        Assert.True(rig.Change.Editor.HasChanges);
        Assert.True(rig.Change.ReviewChangeCommand.CanExecute(null));
    }

    [Fact]
    public async Task TheConfirmationDialogFails_WritesNothing_RecordsOneCancel_AndSaysWhyOnTheStatusBox()
    {
        using WriteFlowRig rig = new(bitLocker: TransactionFakes.BitLockerOn());
        await rig.LoadAndShow("BootOrder");
        rig.MoveUsbUp();
        rig.Platform.ConfirmAnswer = dialog => dialog.Confirm();
        rig.Platform.DialogThrows = new InvalidOperationException("The dialog could not be shown.");
        int before = rig.Announcer.History.Count;
        rig.Focus.Clear();

        await Execute(rig.Change.ReviewChangeCommand);

        Assert.Single(rig.Platform.ConfirmDialogs);
        string expected = "Change blocked: The dialog could not be shown. Nothing was written.";
        Assert.Equal(ShellViewModel.BlockedSentence("The dialog could not be shown."), expected);
        Assert.Equal(expected, rig.Shell.StatusText);
        Assert.Equal(new[] { Strings.CheckingStatus, expected }, rig.Announcer.History.Skip(before));
        Assert.Equal(new[] { FocusTarget.Status }, rig.Focus.Requests);
        Assert.False(rig.Shell.IsWriting);
        Assert.False(rig.Shell.IsFinishingChange);
        Assert.Empty(rig.Transaction.Writer.Calls);
        Assert.Empty(rig.Transaction.Suspender.Calls);
        Assert.Empty(rig.Transaction.TransactionFolders());
        IReadOnlyList<WriteRecord> records = rig.Transaction.Records.ReadAll();
        WriteRecord cancelled = Assert.Single(records, record => record.Outcome == WriteRecordOutcome.Cancelled);
        Assert.False(cancelled.ApiCalled);
        Assert.Null(cancelled.SnapshotFolder);
        Assert.DoesNotContain(records, record => record.ApiCalled);
        Assert.Equal(1, rig.Source.LoadCalls);
        Assert.True(rig.Change.Editor.HasChanges);
    }

    [Fact]
    public async Task WhileTheChecksRun_OpenBaselineFolderAndExportAreDisabled_RefreshIsNot_AndBothComeBackAfterThem()
    {
        // Their dialogs could otherwise be open when the checks answer, and Confirm change would
        // open on top of them.
        using WriteFlowRig rig = new(synchronous: true);
        await rig.LoadAndShow("BootOrder");
        rig.MoveUsbUp();
        Dictionary<string, bool> during = new(StringComparer.Ordinal);
        rig.Synchronous!.AfterPrepare = _ =>
        {
            during["IsChecking"] = rig.Change.IsChecking;
            during["OpenBaseline"] = rig.Shell.OpenBaselineCommand.CanExecute(null);
            during["ExportVisibleList"] = rig.Shell.ExportVisibleListCommand.CanExecute(null);
            during["Refresh"] = rig.Shell.RefreshCommand.CanExecute(null);
            during["ClearFilter"] = rig.Shell.ClearFilterCommand.CanExecute(null);
            during["Filter"] = rig.Shell.IsFilterEnabled;
            during["CopyValue"] = rig.Shell.CopyValueCommand.CanExecute(null);
        };
        int openBaselineRequeries = 0;
        int exportRequeries = 0;
        rig.Shell.OpenBaselineCommand.CanExecuteChanged += (_, _) => openBaselineRequeries++;
        rig.Shell.ExportVisibleListCommand.CanExecuteChanged += (_, _) => exportRequeries++;

        await Execute(rig.Change.ReviewChangeCommand);

        Assert.True(during["IsChecking"]);
        Assert.False(during["OpenBaseline"]);
        Assert.False(during["ExportVisibleList"]);
        Assert.True(during["Refresh"]);
        Assert.True(during["ClearFilter"]);
        Assert.True(during["Filter"]);
        Assert.True(during["CopyValue"]);

        // Re-queried when the checks started and when they ended, so the buttons follow them.
        Assert.True(openBaselineRequeries >= 2, "Open baseline folder was re-queried " + openBaselineRequeries + " times");
        Assert.True(exportRequeries >= 2, "Export visible list was re-queried " + exportRequeries + " times");
        Assert.Single(rig.Platform.ConfirmDialogs);
        Assert.False(rig.Change.IsChecking);
        Assert.True(rig.Shell.OpenBaselineCommand.CanExecute(null));
        Assert.True(rig.Shell.ExportVisibleListCommand.CanExecute(null));
    }

    [Fact]
    public async Task APlanThatArrivesWhileAnotherDialogIsOpen_OpensNoDialogOnTopOfIt_AndIsSaidWhenThatDialogCloses()
    {
        // The window is disabled under a modal dialog, and Open baseline folder and Export visible
        // list are disabled while the checks run, so this cannot come from the keyboard; the shell
        // still never opens Confirm change on top of another dialog (the second guard).
        using WriteFlowRig rig = new(synchronous: true);
        await rig.LoadAndShow("BootOrder");
        rig.MoveUsbUp();
        rig.Platform.ConfirmAnswer = dialog => dialog.Confirm();
        Task? review = null;
        string? statusWhileOpen = null;
        rig.Platform.WhileOpen = () =>
        {
            rig.Platform.WhileOpen = null;
            review = Execute(rig.Change.ReviewChangeCommand);
            statusWhileOpen = rig.Shell.StatusText;
        };
        int before = rig.Announcer.History.Count;
        rig.Focus.Clear();

        await Execute(rig.Shell.OpenBaselineCommand);
        await Assert.IsAssignableFrom<Task>(review);

        string blocked = "Change blocked: Another dialog was open when the checks finished. Nothing was written.";
        Assert.Equal(ShellViewModel.BlockedSentence(Strings.AnotherDialogOpenReason), blocked);
        Assert.Empty(rig.Platform.ConfirmDialogs);
        Assert.Equal(Strings.CheckingStatus, statusWhileOpen);

        // The folder dialog was cancelled: its sentence, then the change's, once it has closed.
        string expected = Strings.DialogCancelledStatus + " " + blocked;
        Assert.Equal(expected, rig.Shell.StatusText);
        Assert.Equal(new[] { Strings.CheckingStatus, expected }, rig.Announcer.History.Skip(before));
        Assert.Equal(new[] { FocusTarget.Status }, rig.Focus.Requests);
        Assert.Equal(1, CountOf(rig, WriteRecordOutcome.Cancelled));
        Assert.Empty(rig.Transaction.TransactionFolders());
        Assert.Empty(rig.Transaction.Writer.Calls);
        Assert.False(rig.Shell.IsWriting);
        Assert.False(rig.Shell.IsOffline);
        Assert.Equal(0, rig.Baseline.LoadCalls);
        Assert.True(rig.Change.Editor.HasChanges);
    }

    [Fact]
    public async Task APlanThatArrivesWhileTheFolderDialogIsOpen_IsStillSaid_WhenAFolderIsPickedThere()
    {
        // The folder picked replaces what a load behind the dialog showed, but not the news that the
        // change was refused: that is said before the baseline load's own sentences.
        using WriteFlowRig rig = new(synchronous: true);
        await rig.LoadAndShow("BootOrder");
        rig.MoveUsbUp();
        rig.Baseline.Result = ShellTestRig.Loaded(rig.Transaction.Store.Enumerate().Variables, ShellTestRig.BaselineDescription, isOffline: true);
        rig.Platform.FolderToReturn = ShellTestRig.BaselineFolder;
        rig.Platform.ConfirmAnswer = dialog => dialog.Confirm();
        Task? review = null;
        rig.Platform.WhileOpen = () =>
        {
            rig.Platform.WhileOpen = null;
            review = Execute(rig.Change.ReviewChangeCommand);
        };
        int before = rig.Announcer.History.Count;
        rig.Focus.Clear();

        await Execute(rig.Shell.OpenBaselineCommand);
        await Assert.IsAssignableFrom<Task>(review);

        string blocked = ShellViewModel.BlockedSentence(Strings.AnotherDialogOpenReason);
        string loaded = "Proposed order discarded. Loaded " + rig.VariableCount + " variables from " + ShellTestRig.BaselineDescription + " (InMemory)";
        Assert.Equal(new[] { Strings.CheckingStatus, blocked, Strings.LoadingStatus, loaded }, rig.Announcer.History.Skip(before));
        Assert.Equal(new[] { FocusTarget.ListFirstRow }, rig.Focus.Requests);
        Assert.Empty(rig.Platform.ConfirmDialogs);
        Assert.Equal(1, CountOf(rig, WriteRecordOutcome.Cancelled));
        Assert.Empty(rig.Transaction.Writer.Calls);
        Assert.True(rig.Shell.IsOffline);
    }

    [Fact]
    public async Task ALoadThatFinishesBehindTheSameDialog_AfterAChangeWasRefusedThere_KeepsTheChangesSentenceFirst()
    {
        using WriteFlowRig rig = new(synchronous: true);
        await rig.LoadAndShow("BootOrder");
        rig.MoveUsbUp();
        rig.Source.Hold();
        Task loading = rig.Shell.RefreshAsync();
        rig.Platform.ConfirmAnswer = dialog => dialog.Confirm();
        Task? review = null;
        rig.Platform.WhileOpen = () =>
        {
            rig.Platform.WhileOpen = null;
            review = Execute(rig.Change.ReviewChangeCommand);
            rig.Source.Release();
        };
        int before = rig.Announcer.History.Count;
        rig.Focus.Clear();

        await Execute(rig.Shell.OpenBaselineCommand);
        await Assert.IsAssignableFrom<Task>(review);
        await loading;

        string expected = "Change blocked: Another dialog was open when the checks finished. Nothing was written. Proposed order discarded. Loaded " + rig.VariableCount + " variables from Live firmware (NtEnumerate)";
        Assert.Equal(expected, rig.Shell.StatusText);
        Assert.Equal(new[] { Strings.CheckingStatus, expected }, rig.Announcer.History.Skip(before));
        Assert.Equal(new[] { FocusTarget.ListFirstRow }, rig.Focus.Requests);
        Assert.Empty(rig.Platform.ConfirmDialogs);
        Assert.Equal(1, CountOf(rig, WriteRecordOutcome.Cancelled));
        Assert.Empty(rig.Transaction.Writer.Calls);
    }

    [Fact]
    public async Task ARefreshThatFinishesWhileTheChecksRun_CancelsThem_SaysSoBeforeItsOwnSentence_AndOpensNoDialog()
    {
        // Refresh stays available while the checks run. When its load replaces the list first, the
        // proposed order the checks were started for is gone ("Proposed order discarded."): the
        // checks are cancelled, and the plan that still arrives opens no dialog.
        using WriteFlowRig rig = new(synchronous: true);
        await rig.LoadAndShow("BootOrder");
        rig.MoveUsbUp();
        rig.Source.Hold();
        Task loading = rig.Shell.RefreshAsync();
        rig.Synchronous!.AfterPrepare = _ => rig.Source.Release();
        rig.Platform.ConfirmAnswer = dialog => dialog.Confirm();
        int before = rig.Announcer.History.Count;
        rig.Focus.Clear();

        await Execute(rig.Change.ReviewChangeCommand);
        await loading;

        string expected = "Cancelled; nothing changed. Proposed order discarded. Loaded " + rig.VariableCount + " variables from Live firmware (NtEnumerate)";
        Assert.Equal(expected, rig.Shell.StatusText);
        Assert.Equal(new[] { Strings.CheckingStatus, expected }, rig.Announcer.History.Skip(before));
        Assert.Equal(new[] { FocusTarget.ListFirstRow }, rig.Focus.Requests);
        Assert.Empty(rig.Platform.ConfirmDialogs);
        Assert.Equal(1, CountOf(rig, WriteRecordOutcome.Cancelled));
        Assert.Empty(rig.Transaction.TransactionFolders());
        Assert.Empty(rig.Transaction.Writer.Calls);
        Assert.False(rig.Change.IsChecking);
        Assert.False(rig.Change.Editor.HasChanges);
        Assert.Equal(2, rig.Source.LoadCalls);
        Assert.Equal(ShellState.Loaded, rig.Shell.State);
    }

    [Fact]
    public async Task ABaselineThatFinishesLoadingWhileTheChecksRun_CancelsThem_AndNoDialogOpensForTheLiveFirmware()
    {
        // A baseline folder picked before the checks started finishes loading while they run: the
        // window shows the baseline offline, so no dialog may offer to write the live firmware.
        using WriteFlowRig rig = new(synchronous: true);
        await rig.LoadAndShow("BootOrder");
        rig.MoveUsbUp();
        TaskCompletionSource<SourceResult> gate = new();
        rig.Baseline.Gate = gate;
        Task opening = rig.Shell.OpenBaselineAsync(ShellTestRig.BaselineFolder);
        SourceResult baseline = ShellTestRig.Loaded(rig.Transaction.Store.Enumerate().Variables, ShellTestRig.BaselineDescription, isOffline: true);
        rig.Synchronous!.AfterPrepare = _ => gate.SetResult(baseline);
        rig.Platform.ConfirmAnswer = dialog => dialog.Confirm();
        int before = rig.Announcer.History.Count;
        rig.Focus.Clear();

        await Execute(rig.Change.ReviewChangeCommand);
        await opening;

        string expected = "Cancelled; nothing changed. Proposed order discarded. Loaded " + rig.VariableCount + " variables from " + ShellTestRig.BaselineDescription + " (InMemory)";
        Assert.True(rig.Shell.IsOffline);
        Assert.Equal(expected, rig.Shell.StatusText);
        Assert.Equal(new[] { Strings.CheckingStatus, expected }, rig.Announcer.History.Skip(before));
        Assert.Equal(new[] { FocusTarget.ListFirstRow }, rig.Focus.Requests);
        Assert.Empty(rig.Platform.ConfirmDialogs);
        Assert.Equal(1, CountOf(rig, WriteRecordOutcome.Cancelled));
        Assert.Empty(rig.Transaction.Writer.Calls);
        Assert.False(rig.Change.IsVisible);
        Assert.Equal(1, rig.Source.LoadCalls);
    }

    [Fact]
    public async Task ALoadThatFailsWhileTheChecksRun_CancelsThem_Too()
    {
        using WriteFlowRig rig = new(synchronous: true);
        await rig.LoadAndShow("BootOrder");
        rig.MoveUsbUp();
        TaskCompletionSource<SourceResult> gate = new();
        rig.Baseline.Gate = gate;
        Task opening = rig.Shell.OpenBaselineAsync(ShellTestRig.BaselineFolder);
        SourceResult failed = new(SourceStatus.Failed, ShellTestRig.BaselineDescription, true, FirmwareType.Unknown, null, Array.Empty<FirmwareVariable>(), EnumerationMethod.None, Array.Empty<string>(), null, "index.json is missing");
        rig.Synchronous!.AfterPrepare = _ => gate.SetResult(failed);
        rig.Platform.ConfirmAnswer = dialog => dialog.Confirm();
        rig.Focus.Clear();

        await Execute(rig.Change.ReviewChangeCommand);
        await opening;

        Assert.Equal("Cancelled; nothing changed. Proposed order discarded. Loading failed: index.json is missing", rig.Shell.StatusText);
        Assert.Equal(ShellState.Failed, rig.Shell.State);
        Assert.Equal(new[] { FocusTarget.Status }, rig.Focus.Requests);
        Assert.Empty(rig.Platform.ConfirmDialogs);
        Assert.Equal(1, CountOf(rig, WriteRecordOutcome.Cancelled));
        Assert.Empty(rig.Transaction.Writer.Calls);
    }

    [Fact]
    public async Task ChecksThatStopOnAnError_AfterALoadCancelledThem_LeaveTheLoadsSentenceInStatus()
    {
        // The load has said "Cancelled; nothing changed." before its own sentence; an error of the
        // cancelled checks is logged, not said over it.
        using WriteFlowRig rig = new(synchronous: true);
        await rig.LoadAndShow("BootOrder");
        rig.MoveUsbUp();
        rig.Source.Hold();
        Task loading = rig.Shell.RefreshAsync();
        rig.Synchronous!.AfterPrepare = _ =>
        {
            rig.Source.Release();
            throw new InvalidOperationException("The checks stopped.");
        };
        int before = rig.Announcer.History.Count;
        rig.Focus.Clear();

        await Execute(rig.Change.ReviewChangeCommand);
        await loading;

        string expected = "Cancelled; nothing changed. Proposed order discarded. Loaded " + rig.VariableCount + " variables from Live firmware (NtEnumerate)";
        Assert.Equal(expected, rig.Shell.StatusText);
        Assert.Equal(new[] { Strings.CheckingStatus, expected }, rig.Announcer.History.Skip(before));
        Assert.Equal(new[] { FocusTarget.ListFirstRow }, rig.Focus.Requests);
        Assert.Empty(rig.Platform.ConfirmDialogs);
        Assert.Empty(rig.Transaction.Writer.Calls);
        Assert.False(rig.Change.IsChecking);
    }

    [Fact]
    public async Task Refresh_DropsAProposedOrder_AndSaysSoBeforeTheLoadedSentence()
    {
        using WriteFlowRig rig = new();
        await rig.LoadAndShow("BootOrder");
        rig.MoveUsbUp();

        await rig.Shell.RefreshAsync();

        string loaded = "Loaded " + rig.VariableCount + " variables from Live firmware (NtEnumerate)";
        Assert.Equal("Proposed order discarded. " + loaded, rig.Shell.StatusText);
        Assert.False(rig.Change.Editor.HasChanges);

        await rig.Shell.RefreshAsync();

        Assert.Equal(loaded, rig.Shell.StatusText);
    }

    [Fact]
    public async Task FC28_ABaseline_ShowsTheOfflineVerdict_WithTheChangeGroupCollapsed()
    {
        using WriteFlowRig rig = new();
        rig.Baseline.Result = ShellTestRig.Loaded(rig.Transaction.Store.Enumerate().Variables, ShellTestRig.BaselineDescription, isOffline: true);

        await rig.Shell.OpenBaselineAsync(ShellTestRig.BaselineFolder);
        Assert.True(rig.Shell.List.TrySelectByKey(Whitelist.BootOrder.Key));
        rig.Shell.EnsureDetailCurrent();

        Assert.True(rig.Shell.IsOffline);
        Assert.Equal(WritabilitySentences.Offline, rig.Shell.Detail.ReadOnlyStatus);
        Assert.Equal(Writability.Offline, rig.Shell.List.SelectedItem?.Writability.Verdict);
        Assert.Equal(string.Empty, rig.Shell.List.SelectedItem?.ItemStatus);
        Assert.False(rig.Change.IsVisible);
        Assert.False(rig.Change.ShowsBootOrderControls);
        Assert.Empty(rig.Change.Editor.Entries);
        Assert.All(rig.ChangeCommands, command => Assert.False(command.CanExecute(null)));
    }

    // ---- the blocked sentence ---------------------------------------------------------------------------

    [Theory]
    [InlineData("Boot0007 does not exist on this machine.", "Change blocked: Boot0007 does not exist on this machine. Nothing was written.")]
    [InlineData("a reason without its period", "Change blocked: a reason without its period. Nothing was written.")]
    [InlineData("This machine is not the supported OptiPlex 7020 with BIOS A18; nothing is written.", "Change blocked: This machine is not the supported OptiPlex 7020 with BIOS A18; nothing is written.")]
    public void BlockedSentence_WrapsTheBareReason_WithoutSayingAnythingTwice(string reason, string expected)
    {
        Assert.Equal(expected, ShellViewModel.BlockedSentence(reason));
    }

    [Fact]
    public void BlockedSentence_KeepsCoresOwnBlockedSentences_AndTheSuspendedEndingLast()
    {
        string restore = CheckSentences.Format(CheckSentences.RestoreOrderAddsFormat, "Boot0003");
        Assert.StartsWith(ShellViewModel.RestoreBlockedPrefix, CheckSentences.RestoreOrderAddsFormat, StringComparison.Ordinal);
        Assert.Equal(restore, ShellViewModel.BlockedSentence(restore));
        Assert.Equal("Change blocked: already said.", ShellViewModel.BlockedSentence("Change blocked: already said."));

        string stale = CheckSentences.Format(CheckSentences.StaleReadFormat, "BootOrder");
        Assert.Equal("Change blocked: " + stale + " " + CheckSentences.BitLockerLeftSuspended, ShellViewModel.BlockedSentence(stale + " " + CheckSentences.BitLockerLeftSuspended));

        string suspendFailed = CheckSentences.Format(CheckSentences.BitLockerSuspendFailedFormat, 0x80310000u);
        Assert.Equal("Change blocked: " + suspendFailed, ShellViewModel.BlockedSentence(suspendFailed));
        Assert.Equal("Change blocked: " + CheckSentences.NoBootNextToClear + " Nothing was written.", ShellViewModel.BlockedSentence(CheckSentences.NoBootNextToClear));
    }

    // ---- the write service --------------------------------------------------------------------------------

    [Fact]
    public async Task WriteService_ComposesThePipelineOnTheFirstCheck_AndNeverForNewest()
    {
        using TransactionRig transaction = new();
        int composed = 0;
        WriteService writes = new(
            () =>
            {
                composed++;
                return transaction.Pipeline;
            },
            transaction.Snapshots);

        Assert.False(writes.IsComposed);
        Assert.Null(writes.Newest(Whitelist.BootOrder.Key));
        Assert.Equal(transaction.Snapshots.RootDirectory, writes.SnapshotRoot);
        Assert.Equal(0, composed);

        PrepareResult? prepared = await writes.PrepareAsync(new ReorderRequest(ChangeRequest.AppHost, new[] { UsbNumber, WindowsOnPartition1 }), CancellationToken.None);
        ChangePlan plan = Assert.IsType<ChangePlan>(prepared?.Plan);
        await writes.CancelAsync(plan);

        Assert.True(writes.IsComposed);
        Assert.Equal(1, composed);
        Assert.Empty(transaction.Writer.Calls);
    }

    [Fact]
    public async Task WriteService_ACancelledTokenBeforeTheChecks_ReturnsNull_AndComposesNothing()
    {
        using TransactionRig transaction = new();
        int composed = 0;
        WriteService writes = new(
            () =>
            {
                composed++;
                return transaction.Pipeline;
            },
            transaction.Snapshots);
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Assert.Null(await writes.PrepareAsync(new ClearBootNextRequest(ChangeRequest.AppHost), cancelled.Token));
        Assert.Equal(0, composed);
    }

    [Fact]
    public void CreateWindows_OffersTheWriteService_WithoutComposingThePipeline()
    {
        LiveFirmwareSource source = LiveFirmwareSource.CreateWindows(null);

        WriteService writes = Assert.IsType<WriteService>(source.Writes);
        Assert.False(writes.IsComposed);
        Assert.Equal(Path.GetFullPath(SnapshotStore.DefaultRootDirectory), writes.SnapshotRoot);

        ShellViewModel shell = ShellViewModel.CreateWindows(new RecordingAnnouncer(), new RecordingFocusSink(), new NullPlatformInteractions(), null);
        Assert.False(shell.Detail.Change.IsVisible);
        Assert.False(shell.IsWriting);
        Assert.True(shell.TryClose());
    }

    [Fact]
    public void CreateWindows_ComposesTheRealWriterAndSuspender_ExactlyOnce_AndNowhereElse()
    {
        // The GUI's composition root of the real writer: LiveFirmwareSource.CreateWindows builds the writer
        // over the Windows write seam and the suspender exactly once, inside its lazy factory, and
        // no other file of the Presentation or the App project builds either. The source is read
        // as text; nothing here builds them.
        string source = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "src", "UefiAccess.Presentation", "Services", "LiveFirmwareSource.cs"));
        string body = Assert.Single(SourceCalls.MethodBodies(source, "CreateWindows"));
        Assert.Single(Regex.Matches(body, RealWriterConstruction));
        Assert.Single(Regex.Matches(body, RealSuspenderConstruction));

        string code = SourceCalls.WithoutCommentsAndStrings(source);
        Assert.Single(Regex.Matches(code, RealWriterConstruction));
        Assert.Single(Regex.Matches(code, RealSuspenderConstruction));

        IEnumerable<string> others = RepositoryPaths.Enumerate(Path.Combine("src", "UefiAccess.Presentation"), "*.cs")
            .Concat(RepositoryPaths.Enumerate(Path.Combine("src", "UefiAccess.App"), "*.cs"))
            .Where(file => !file.EndsWith(Path.Combine("Services", "LiveFirmwareSource.cs"), StringComparison.OrdinalIgnoreCase));
        foreach (string file in others)
        {
            string other = SourceCalls.WithoutCommentsAndStrings(File.ReadAllText(file));
            Assert.DoesNotMatch(RealSuspenderConstruction, other);
            Assert.DoesNotMatch(AnyWriterConstruction, other);
        }
    }

    // ---- helpers --------------------------------------------------------------------------------------------

    private static Task Execute(ICommand command) => Assert.IsType<AsyncRelayCommand>(command).ExecuteAsync();

    private static int CountOf(WriteFlowRig rig, WriteRecordOutcome outcome)
    {
        return rig.Transaction.Records.ReadAll().Count(record => record.Outcome == outcome);
    }

    /// <summary>
    /// The shell over a live source that reads the scripted store of a <see cref="TransactionRig"/>
    /// and over the rig's pipeline (through the application's <see cref="WriteService"/>, or,
    /// with <c>synchronous</c>, a service whose tasks complete before they are returned, so a
    /// dialog opens on the test's own thread), with a recording announcer, focus sink and the
    /// <see cref="DialogPlatform"/>, and the detail scheduler given (at once when none is).
    /// Disposing it deletes the rig's temporary data folder.
    /// </summary>
    private sealed class WriteFlowRig : IDisposable
    {
        public WriteFlowRig(VariableSet? set = null, BitLockerResult? bitLocker = null, bool machineConfirmed = true, bool synchronous = false, string machineId = TestUnitMachineId, IDetailScheduler? detailScheduler = null)
        {
            // The source and the pipeline read the same made-up machine ID, as the live source and
            // the pipeline both read the Windows one.
            Transaction = new TransactionRig(set, bitLocker, machineConfirmed) { MachineId = machineId };
            Source = new StoreSource(Transaction.Store, Identity(machineConfirmed), Unit(machineId));
            SynchronousWriteService? synchronousWrites = synchronous ? new SynchronousWriteService(Transaction.Pipeline, Transaction.Snapshots) : null;
            Synchronous = synchronousWrites;
            IWriteService writes = synchronousWrites is not null
                ? synchronousWrites
                : new WriteService(() => Transaction.Pipeline, Transaction.Snapshots);
            Shell = new ShellViewModel(Source, _ => Baseline, Announcer, Focus, Platform, null, detailScheduler, writes);
            Shell.AnnounceReady();
        }

        public TransactionRig Transaction { get; }

        /// <summary>The write service when the rig was made synchronous; null otherwise.</summary>
        public SynchronousWriteService? Synchronous { get; }

        public StoreSource Source { get; }

        public GatedSource Baseline { get; } = new(ShellTestRig.BaselineDescription, isOffline: true);

        public RecordingAnnouncer Announcer { get; } = new();

        public RecordingFocusSink Focus { get; } = new();

        public DialogPlatform Platform { get; } = new();

        public ShellViewModel Shell { get; }

        public ChangeViewModel Change => Shell.Detail.Change;

        public IEnumerable<ICommand> ChangeCommands => new[]
        {
            Change.MoveUpCommand,
            Change.MoveDownCommand,
            Change.ResetOrderCommand,
            Change.ReviewChangeCommand,
            Change.BootOnceCommand,
            Change.ClearBootNextCommand,
            Change.RestorePreviousCommand,
        };

        /// <summary>The number of variables the firmware holds now, as the Loaded sentence counts them.</summary>
        public int VariableCount => Transaction.Store.Enumerate().Variables.Count;

        public async Task LoadAndShow(string name)
        {
            await Shell.RefreshAsync();
            Assert.True(Shell.List.TrySelectByKey(FirmwareVariable.MakeKey(KnownGuids.EfiGlobalVariable, name)), name + " is not in the list");
            Shell.EnsureDetailCurrent();
        }

        /// <summary>Selects Boot0003 in the Boot order entries list and presses Move up.</summary>
        public void MoveUsbUp()
        {
            Change.Editor.SelectedEntry = Change.Editor.Entries.Single(entry => entry.Name == "Boot0003");
            Change.MoveUpCommand.Execute(null);
        }

        public void Dispose() => Transaction.Dispose();
    }

    /// <summary>
    /// The live source over a scripted store: every load enumerates it as it is now and runs the
    /// unit check, as <see cref="LiveFirmwareSource"/> does; a load can be held until released, or
    /// fail once.
    /// </summary>
    private sealed class StoreSource : IVariableSource
    {
        private readonly TransactionFakes.ScriptedStore _store;
        private readonly MachineIdentityResult _identity;
        private readonly EnabledUnit _unit;
        private TaskCompletionSource<SourceResult>? _gate;

        public StoreSource(TransactionFakes.ScriptedStore store, MachineIdentityResult identity, EnabledUnit unit)
        {
            _store = store;
            _identity = identity;
            _unit = unit;
        }

        public string Description => Strings.LiveSourceName;

        public bool IsOffline => false;

        public int LoadCalls { get; private set; }

        /// <summary>When set, the next load fails with this text.</summary>
        public string? FailNextLoad { get; set; }

        /// <summary>The next load waits until <see cref="Release"/>.</summary>
        public void Hold() => _gate = new TaskCompletionSource<SourceResult>();

        /// <summary>The held load answers with the store as it is now.</summary>
        public void Release()
        {
            TaskCompletionSource<SourceResult>? gate = _gate;
            _gate = null;
            gate?.SetResult(Current());
        }

        public Task<SourceResult> LoadAsync(CancellationToken cancellationToken)
        {
            LoadCalls++;
            if (_gate is not null)
            {
                return _gate.Task;
            }

            if (FailNextLoad is string message)
            {
                FailNextLoad = null;
                return Task.FromResult(new SourceResult(SourceStatus.Failed, Description, false, FirmwareType.Uefi, PrivilegeState.Enabled, Array.Empty<FirmwareVariable>(), EnumerationMethod.None, Array.Empty<string>(), _identity, message));
            }

            return Task.FromResult(Current());
        }

        private SourceResult Current()
        {
            EnumerationResult enumeration = _store.Enumerate();
            return new SourceResult(SourceStatus.Loaded, Description, false, FirmwareType.Uefi, PrivilegeState.Enabled, enumeration.Variables, enumeration.Method, Array.Empty<string>(), _identity, null)
            {
                UnitConfirmed = _unit.Check() == UnitMatch.Confirmed,
            };
        }
    }

    /// <summary>A write service whose tasks have completed when they are returned, so the shell's flow never leaves the test's thread.</summary>
    private sealed class SynchronousWriteService : IWriteService
    {
        private readonly IWritePipeline _pipeline;
        private readonly SnapshotStore _snapshots;

        public SynchronousWriteService(IWritePipeline pipeline, SnapshotStore snapshots)
        {
            _pipeline = pipeline;
            _snapshots = snapshots;
        }

        public string SnapshotRoot => _snapshots.RootDirectory;

        /// <summary>Runs on the caller's thread after the pipeline's commit has returned and before its result is handed back, so the shell is still writing.</summary>
        public Action? AfterCommit { get; set; }

        /// <summary>
        /// Runs on the caller's thread with the pipeline's answer to the checks, after their last
        /// stage boundary and before the answer is handed back, so the shell is still checking
        /// (an Escape or a load that finishes then meets a plan that is already made).
        /// </summary>
        public Action<PrepareResult>? AfterPrepare { get; set; }

        public Task<PrepareResult?> PrepareAsync(ChangeRequest request, CancellationToken token)
        {
            PrepareResult result = Completed(_pipeline.PrepareAsync(request, token));
            AfterPrepare?.Invoke(result);
            return Task.FromResult<PrepareResult?>(result);
        }

        public Task<TransactionResult> CommitAsync(ChangePlan plan, ConfirmationToken confirmation)
        {
            TransactionResult result = Completed(_pipeline.CommitAsync(plan, confirmation));
            AfterCommit?.Invoke();
            return Task.FromResult(result);
        }

        public Task<TransactionResult> CancelAsync(ChangePlan plan) => Task.FromResult(Completed(_pipeline.CancelAsync(plan)));

        public SnapshotSummary? Newest(string key) => _snapshots.Newest(key);

        private static T Completed<T>(Task<T> task) => task.GetAwaiter().GetResult();
    }
}
