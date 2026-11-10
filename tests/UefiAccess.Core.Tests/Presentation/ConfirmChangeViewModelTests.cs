// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Platform;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Tests.Safety;
using UefiAccess.Core.Variables;
using UefiAccess.Presentation.Resources;
using UefiAccess.Presentation.ViewModels;
using Xunit;
using static UefiAccess.Core.Tests.Fixtures.LoadOptionFixtures;
using static UefiAccess.Core.Tests.Safety.SafetyFixtures;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// The confirmation dialog's texts, built from real plans of the
/// validator over the Safety fixtures: What will change in each of its forms and with the suspend
/// sentence while the box is ticked, Before and After one line per position with "(was N)", the
/// Pending snapshot variant, the three BitLocker boxes, the Warnings box whose first line counts the
/// lines, the Snapshot folder box, the check box offered only for On, Encrypting and Unknown and
/// never for a removal of the one-time boot choice, the confirm button's two names, and the
/// decision and the BitLocker choice it stands for. Every single-line box stays under 200 characters.
/// </summary>
public sealed class ConfirmChangeViewModelTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "snapshots");
    private static readonly string NewLine = Environment.NewLine;
    private static readonly string UsbEntry = "Boot0003 (" + UsbStickDescription + ")";

    // ---- What will change ----------------------------------------------------------------------

    [Fact]
    public void ASingleMove_NamesTheEntryAndBothPositions_AndTheSuspendWhileTicked()
    {
        ConfirmChangeViewModel dialog = Dialog(Reorder(UsbNumber, WindowsOnPartition1));

        Assert.True(dialog.SuspendOffered);
        Assert.True(dialog.SuspendChosen);
        Assert.Equal("Change the boot order: " + UsbEntry + " moves from position 2 to 1 of 2. And suspend BitLocker for one restart.", dialog.WhatWillChangeText);
        Assert.Equal(Strings.ConfirmChangeAndSuspendName, dialog.ConfirmButtonName);
        Assert.Equal("Confirm change and suspend BitLocker for one restart", dialog.ConfirmButtonName);
    }

    [Fact]
    public void ClearingTheCheckBox_ChangesWhatWillChangeAndTheButtonsName_AndNothingElse()
    {
        ConfirmChangeViewModel dialog = Dialog(Reorder(UsbNumber, WindowsOnPartition1));
        string before = dialog.BeforeText;
        List<string?> changed = new();
        dialog.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        dialog.SuspendChosen = false;

        Assert.Equal("Change the boot order: " + UsbEntry + " moves from position 2 to 1 of 2.", dialog.WhatWillChangeText);
        Assert.Equal(Strings.ConfirmChangeName, dialog.ConfirmButtonName);
        Assert.Equal(new[] { nameof(ConfirmChangeViewModel.SuspendChosen), nameof(ConfirmChangeViewModel.WhatWillChangeText), nameof(ConfirmChangeViewModel.ConfirmButtonName) }, changed);
        Assert.Equal(before, dialog.BeforeText);

        dialog.SuspendChosen = true;
        Assert.EndsWith(Strings.SuspendSuffix, dialog.WhatWillChangeText, StringComparison.Ordinal);
    }

    [Fact]
    public void AnyOtherReorder_SaysHowManyEntries_AndPointsToTheBoxes()
    {
        VariableSet set = StoreWithOrder(WindowsOnPartition1, UsbNumber, HiddenNumber, NetworkNumber);
        ConfirmChangeViewModel dialog = Dialog(Reorder(UsbNumber, WindowsOnPartition1, NetworkNumber, HiddenNumber), set, TransactionFakes.BitLockerOff());

        Assert.Equal("Change the boot order of 4 entries; the Before and After boxes list it.", dialog.WhatWillChangeText);
        Assert.False(dialog.SuspendOffered);
    }

    [Fact]
    public void AOneTimeBoot_NamesTheEntry()
    {
        ConfirmChangeViewModel dialog = Dialog(Next(UsbNumber));

        Assert.Equal("Start " + UsbEntry + " once at the next restart, then use the normal order. And suspend BitLocker for one restart.", dialog.WhatWillChangeText);
        Assert.True(dialog.SuspendOffered);
    }

    [Fact]
    public void TheRemovalOfTheOneTimeBoot_NamesIt_AndNeverOffersTheSuspend()
    {
        ConfirmChangeViewModel dialog = Dialog(Clear(), Store(Var("BootNext", U16(WindowsOnPartition1))));

        Assert.Equal("Remove the one-time boot choice Boot0002 (Windows Boot Manager).", dialog.WhatWillChangeText);
        Assert.False(dialog.SuspendOffered);
        Assert.False(dialog.SuspendChosen);
        dialog.SuspendChosen = true;
        Assert.False(dialog.SuspendChosen);
        Assert.Equal(Strings.ConfirmChangeName, dialog.ConfirmButtonName);
        Assert.Equal(BitLockerChoice.NotNeeded, dialog.ChoiceFor(dialog.Confirm()));
    }

    [Fact]
    public void ARestore_NamesTheSnapshot_InEachOfItsThreeForms()
    {
        string folder = Path.Combine(Root, "20260929-101500-reorder");
        ConfirmChangeViewModel order = Dialog(new ReorderRequest(ChangeRequest.AppHost, new[] { UsbNumber, WindowsOnPartition1 }) { RestoredFrom = folder });
        ConfirmChangeViewModel oneTime = Dialog(new BootNextRequest(ChangeRequest.AppHost, UsbNumber) { RestoredFrom = folder });
        ConfirmChangeViewModel clear = Dialog(new ClearBootNextRequest(ChangeRequest.AppHost) { RestoredFrom = folder }, Store(Var("BootNext", U16(UsbNumber))));

        Assert.Equal("Restore the boot order recorded in snapshot 20260929-101500-reorder; the After box lists it. And suspend BitLocker for one restart.", order.WhatWillChangeText);
        Assert.Equal("Restore snapshot 20260929-101500-reorder: start " + UsbEntry + " once at the next restart. And suspend BitLocker for one restart.", oneTime.WhatWillChangeText);
        Assert.Equal("Restore snapshot 20260929-101500-reorder: remove the one-time boot choice.", clear.WhatWillChangeText);
        Assert.False(clear.SuspendOffered);
        Assert.Equal("20260929-101500-reorder", order.Summary.RestoredFromName);
    }

    // ---- Before and After ----------------------------------------------------------------------

    [Fact]
    public void BeforeAndAfter_SayHowManyPositionsFollow_ThenListOnePerLine_AndMarkTheMovedRows()
    {
        ConfirmChangeViewModel dialog = Dialog(Reorder(UsbNumber, WindowsOnPartition1));

        // A screen reader reads a multi-line box only to its first line on Tab, so that line says
        // what follows, as the Warnings box does.
        Assert.Equal("2 positions; Down arrow reads each." + NewLine + "1. Boot0002, Windows Boot Manager, active" + NewLine + "2. Boot0003, " + UsbStickDescription + ", active", dialog.BeforeText);
        Assert.Equal("2 positions; Down arrow reads each." + NewLine + "1. Boot0003, " + UsbStickDescription + ", active (was 2)" + NewLine + "2. Boot0002, Windows Boot Manager, active (was 1)", dialog.AfterText);
        Assert.StartsWith(string.Format(System.Globalization.CultureInfo.InvariantCulture, Strings.PositionsHeaderFormat, Strings.Positions(2)) + NewLine, dialog.BeforeText, StringComparison.Ordinal);
        Assert.Equal("1 position", Strings.Positions(1));
    }

    [Fact]
    public void AnUnmovedRow_CarriesNoMark_AndInactiveAndHiddenEntriesSaySo()
    {
        VariableSet set = StoreWithOrder(WindowsOnPartition1, InactiveNumber, HiddenNumber);
        ConfirmChangeViewModel dialog = Dialog(Reorder(WindowsOnPartition1, HiddenNumber, InactiveNumber), set, TransactionFakes.BitLockerOff());

        Assert.Equal(
            "3 positions; Down arrow reads each." + NewLine + "1. Boot0002, Windows Boot Manager, active" + NewLine + "2. Boot0005, Hidden tool, active, hidden (was 3)" + NewLine + "3. Boot0006, Old system, inactive (was 2)",
            dialog.AfterText);
    }

    [Fact]
    public void BootNext_ShowsNotSetOrTheEntry()
    {
        ConfirmChangeViewModel set = Dialog(Next(UsbNumber));
        ConfirmChangeViewModel clear = Dialog(Clear(), Store(Var("BootNext", U16(UsbNumber))));

        Assert.Equal(Strings.NotSetText, set.BeforeText);
        Assert.Equal("Boot0003, " + UsbStickDescription + ", active", set.AfterText);
        Assert.Equal("Boot0003, " + UsbStickDescription + ", active", clear.BeforeText);
        Assert.Equal("Not set.", clear.AfterText);
    }

    [Fact]
    public void AnIncompleteSnapshotsRestore_SaysSoFirst_ThenItsValue_ThenWhatTheFirmwareHoldsNow()
    {
        string folder = Path.Combine(Root, "20260929-101500-reorder");
        ChangePlan plan = Plan(new ReorderRequest(ChangeRequest.AppHost, new[] { UsbNumber, WindowsOnPartition1 }) { RestoredFrom = folder });

        ConfirmChangeViewModel dialog = new(plan, Root, fromPendingSnapshot: true);

        Assert.True(dialog.Summary.FromPendingSnapshot);
        Assert.Equal(
            string.Join(
                NewLine,
                Strings.PendingBeforeLine,
                "1. Boot0003, " + UsbStickDescription + ", active",
                "2. Boot0002, Windows Boot Manager, active",
                Strings.PendingNowLine,
                "1. Boot0002, Windows Boot Manager, active",
                "2. Boot0003, " + UsbStickDescription + ", active"),
            dialog.BeforeText);
        Assert.StartsWith("This change did not complete; the value before it was:", dialog.BeforeText, StringComparison.Ordinal);
        Assert.False(new ConfirmChangeViewModel(Plan(Reorder(UsbNumber, WindowsOnPartition1)), Root, fromPendingSnapshot: true).Summary.FromPendingSnapshot);
    }

    // ---- BitLocker boxes -------------------------------------------------------------------------

    [Fact]
    public void WithProtectionOn_TheThreeBoxesCarryCoresSentences()
    {
        ChangePlan plan = Plan(Reorder(UsbNumber, WindowsOnPartition1));
        ConfirmChangeViewModel dialog = new(plan, Root);

        Assert.Equal(plan.Policy.BitLocker.StateSentence, dialog.BitLockerStateText);
        Assert.StartsWith("BitLocker protects drive ", dialog.BitLockerStateText, StringComparison.Ordinal);
        Assert.Equal(BitLockerPreflight.ReorderRiskSentence, dialog.RiskText);
        Assert.Equal(BitLockerPreflight.RecoveryKeySentence, dialog.RecoveryKeyText);
    }

    [Fact]
    public void AReorderThatKeepsTheCurrentEntryFirst_SaysSoInTheRiskBox()
    {
        VariableSet set = StoreWithOrder(WindowsOnPartition1, UsbNumber, HiddenNumber);
        ConfirmChangeViewModel dialog = Dialog(Reorder(WindowsOnPartition1, HiddenNumber, UsbNumber), set);

        Assert.Equal(CheckSentences.Format(BitLockerPreflight.ReorderRiskFirstEntryStaysFormat, "Boot0002"), dialog.RiskText);
        Assert.EndsWith("The first entry stays Boot0002.", dialog.RiskText, StringComparison.Ordinal);
    }

    [Fact]
    public void AOneTimeBootAndARemoval_HaveTheirOwnRisk()
    {
        Assert.Equal(CheckSentences.Format(BitLockerPreflight.BootNextRiskFormat, "Boot0003"), Dialog(Next(UsbNumber)).RiskText);
        Assert.Equal(Strings.ClearRiskText, Dialog(Clear(), Store(Var("BootNext", U16(UsbNumber)))).RiskText);
    }

    [Fact]
    public void WithProtectionOff_NoRiskNoReminderNoCheckBox()
    {
        ConfirmChangeViewModel dialog = Dialog(Reorder(UsbNumber, WindowsOnPartition1), bitLocker: TransactionFakes.BitLockerOff());

        Assert.StartsWith("BitLocker protection is off on ", dialog.BitLockerStateText, StringComparison.Ordinal);
        Assert.Equal("None: BitLocker protection is off.", dialog.RiskText);
        Assert.Equal("Not needed: BitLocker protection is off.", dialog.RecoveryKeyText);
        Assert.False(dialog.SuspendOffered);
        Assert.False(dialog.SuspendChosen);
        Assert.Equal("Change the boot order: " + UsbEntry + " moves from position 2 to 1 of 2.", dialog.WhatWillChangeText);
        Assert.Equal(BitLockerChoice.NotNeeded, dialog.ChoiceFor(dialog.Confirm()));
    }

    [Fact]
    public void ASuspendedVolume_KeepsTheWarningAndTheReminder_ButOffersNoSecondSuspend()
    {
        BitLockerResult suspended = new(
            new[] { new BitLockerVolume(BitLockerPreflight.OsDriveLetter, 0, "Off", 1, "Encrypted", new[] { "TPM", "Numerical password" }, new[] { 0, 2, 4, 11 }, "PCR 0,2,4,11 (legacy)") },
            Array.Empty<string>());

        ConfirmChangeViewModel dialog = Dialog(Reorder(UsbNumber, WindowsOnPartition1), bitLocker: suspended);

        Assert.False(dialog.SuspendOffered);
        Assert.Equal(BitLockerPreflight.ReorderRiskSentence, dialog.RiskText);
        Assert.Equal(BitLockerPreflight.RecoveryKeySentence, dialog.RecoveryKeyText);
        Assert.StartsWith("BitLocker is suspended on ", dialog.BitLockerStateText, StringComparison.Ordinal);
    }

    // ---- Warnings and Snapshot folder ----------------------------------------------------------------

    [Fact]
    public void Warnings_FirstSayHowManyLinesFollow_ThenOneLineEach_TheReorderLineLast()
    {
        ChangePlan plan = Plan(Reorder(UsbNumber, WindowsOnPartition1));
        ConfirmChangeViewModel dialog = new(plan, Root);

        List<string> lines = plan.Policy.Warnings.ToList();
        lines.Add(Strings.ReorderMayBeLostWarning);
        Assert.Equal(string.Join(NewLine, new[] { Strings.Lines(lines.Count) + "; Down arrow reads each." }.Concat(lines)), dialog.WarningsText);
        Assert.Contains("Secure Boot: on, user mode. This application never changes it.", dialog.WarningsText, StringComparison.Ordinal);
        Assert.StartsWith("3 lines; Down arrow reads each.", dialog.WarningsText, StringComparison.Ordinal);
    }

    [Fact]
    public void Warnings_OfAOneTimeBoot_HaveNoReorderLine_AndSayOneLineInTheSingular()
    {
        ChangePlan plan = Plan(Next(WindowsOnPartition1), bitLocker: TransactionFakes.BitLockerOff());
        ConfirmChangeViewModel dialog = new(plan, Root);

        Assert.Equal("1 line; Down arrow reads each." + NewLine + "Secure Boot: on, user mode. This application never changes it.", dialog.WarningsText);
        Assert.DoesNotContain(Strings.ReorderMayBeLostWarning, dialog.WarningsText, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSnapshotFolderBox_NamesTheFolderTheChangeWillWrite()
    {
        ChangePlan plan = Plan(Reorder(UsbNumber, WindowsOnPartition1));
        ConfirmChangeViewModel dialog = new(plan, Root);

        Assert.Equal("Before writing, a snapshot is saved to " + Path.Combine(Root, plan.SnapshotFolderName) + ". Restore previous value (Alt+P) puts it back.", dialog.SnapshotFolderText);
        Assert.Equal(plan.SnapshotFolderName, dialog.Summary.SnapshotFolderName);
    }

    // ---- the decision ----------------------------------------------------------------------------

    [Fact]
    public void TheDecision_CarriesTheCheckBox_AndStandsForTheChoiceTheStateNeeds()
    {
        ConfirmChangeViewModel dialog = Dialog(Reorder(UsbNumber, WindowsOnPartition1));

        ConfirmDecision ticked = dialog.Confirm();
        Assert.Equal(new ConfirmDecision(true, true), ticked);
        Assert.Equal(BitLockerChoice.SuspendForOneRestart, dialog.ChoiceFor(ticked));

        dialog.SuspendChosen = false;
        ConfirmDecision cleared = dialog.Confirm();
        Assert.Equal(new ConfirmDecision(true, false), cleared);
        Assert.Equal(BitLockerChoice.KeepProtection, dialog.ChoiceFor(cleared));

        Assert.Equal(ConfirmDecision.Cancelled, dialog.Cancel());
        Assert.False(dialog.Cancel().Confirmed);
        Assert.Same(dialog.Summary.Plan, dialog.Plan);
        Assert.Equal(Strings.ConfirmChangeTitle, dialog.Title);
    }

    [Fact]
    public void EverySingleLineBox_StaysUnder200Characters_OverTheLongestFixture()
    {
        // A three-entry order whose moved entry has the longest description of the fixtures.
        VariableSet set = StoreWithOrder(WindowsOnPartition1, UsbNumber, NetworkNumber);
        List<ConfirmChangeViewModel> dialogs = new()
        {
            Dialog(Reorder(NetworkNumber, WindowsOnPartition1, UsbNumber), set),
            Dialog(Reorder(WindowsOnPartition1, NetworkNumber, UsbNumber), set),
            Dialog(Next(NetworkNumber), set),
            Dialog(Clear(), Store(Var("BootNext", U16(NetworkNumber)))),
            Dialog(new ReorderRequest(ChangeRequest.AppHost, new[] { NetworkNumber, WindowsOnPartition1, UsbNumber }) { RestoredFrom = Path.Combine(Root, "20260929-101500-reorder-restore") }, set),

            // The longest What will change: a restore that starts the PXE entry once, from the
            // longest snapshot folder name, with the suspend sentence ticked.
            Dialog(new BootNextRequest(ChangeRequest.AppHost, NetworkNumber) { RestoredFrom = Path.Combine(Root, "20260930-141502-bootnext-clear-restore") }, set),
        };

        foreach (ConfirmChangeViewModel dialog in dialogs)
        {
            foreach (string box in new[] { dialog.WhatWillChangeText, dialog.BitLockerStateText, dialog.RiskText, dialog.RecoveryKeyText })
            {
                Assert.True(box.Length < 200, box.Length + ": " + box);
                Assert.DoesNotContain(NewLine, box, StringComparison.Ordinal);
            }
        }

        Assert.Contains(PxeDescription, dialogs[0].WhatWillChangeText, StringComparison.Ordinal);
        Assert.Equal(
            "Restore snapshot 20260930-141502-bootnext-clear-restore: start Boot000D (" + PxeDescription + ") once at the next restart. And suspend BitLocker for one restart.",
            dialogs[^1].WhatWillChangeText);
    }

    [Fact]
    public void SingleMove_NamesTheEntryThatMovedUp_WhenNeighboursSwapped()
    {
        Assert.Equal<(int From, int To)?>((1, 0), ChangeSummary.SingleMove(new[] { "A", "B" }, new[] { "B", "A" }));
        Assert.Equal<(int From, int To)?>((0, 2), ChangeSummary.SingleMove(new[] { "A", "B", "C" }, new[] { "B", "C", "A" }));
        Assert.Equal<(int From, int To)?>((2, 0), ChangeSummary.SingleMove(new[] { "A", "B", "C" }, new[] { "C", "A", "B" }));
        Assert.False(ChangeSummary.SingleMove(new[] { "A", "B", "C", "D" }, new[] { "B", "A", "D", "C" }).HasValue);
        Assert.False(ChangeSummary.SingleMove(new[] { "A", "B" }, new[] { "A", "B" }).HasValue);
        Assert.False(ChangeSummary.SingleMove(new[] { "A" }, new[] { "A" }).HasValue);
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private static ConfirmChangeViewModel Dialog(ChangeRequest request, VariableSet? set = null, BitLockerResult? bitLocker = null)
    {
        return new ConfirmChangeViewModel(Plan(request, set, bitLocker), Root);
    }

    /// <summary>
    /// A plan as the pipeline makes it: the validator's result over the store, the BitLocker
    /// assessment (on unless given), Secure Boot on in user mode, the contracted machine, and the
    /// warnings in the pipeline's order (Secure Boot first, then the rules' warnings).
    /// </summary>
    private static ChangePlan Plan(ChangeRequest request, VariableSet? set = null, BitLockerResult? bitLocker = null)
    {
        VariableSet store = set ?? Store();
        BitLockerAssessment assessment = BitLockerPreflight.Assess(bitLocker ?? TransactionFakes.BitLockerOn());
        ValidationResult validation = Validate(request, store, assessment.State);
        Assert.True(validation.IsPlan, validation.BlockedReason);
        SecureBootPolicyResult secureBoot = TransactionFakes.SecureBootOn();
        string[] warnings = new[] { PlatformPolicy.DescribeSecureBoot(secureBoot) }.Concat(validation.Warnings).ToArray();
        PolicyFindings findings = new(secureBoot, Identity(true), true, assessment, null, null, warnings);
        return new ChangePlan(validation, findings, validation.Checks, PlanTime);
    }
}
