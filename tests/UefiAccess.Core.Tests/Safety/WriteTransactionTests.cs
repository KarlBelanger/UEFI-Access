// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Baseline;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Privileges;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Variables;
using Xunit;
using static UefiAccess.Core.Tests.Fixtures.LoadOptionFixtures;
using static UefiAccess.Core.Tests.Safety.SafetyFixtures;

namespace UefiAccess.Core.Tests.Safety;

/// <summary>
/// The transaction over <see cref="TransactionFakes"/>: the dry run
/// never writes and makes no transaction folder; the commit writes exactly once, after the
/// snapshot, the first drift read, the optional suspend and the second drift read, then reads
/// back once; every stage that refuses leaves the writer uncalled; the suspend never follows a
/// refusal; a firmware refusal is never followed by a second call; the verification read decides
/// among the four outcomes, also after a writer that throws; the record is complete and on disk
/// before the result returns, after its S12 intent line; a list that cannot take that line
/// refuses the commit; the eighth write of a Windows start is refused, by the clock or by the
/// boot identity; a reorder is refused while a one-time boot is set and BitLocker protectors may
/// exist, and a BootNext changed after its dry run is stale. The writer, the suspender and the
/// store are fakes: nothing here can reach the firmware or BitLocker.
/// </summary>
public sealed class WriteTransactionTests
{
    private const string BootOrder = "BootOrder";
    private const string BootNext = "BootNext";
    private const string ReorderFolder = "20260930-141502-reorder";

    [Theory]
    [InlineData("reorder", BootOrder, "03000200", "20260930-141502-reorder")]
    [InlineData("bootnext", BootNext, "0200", "20260930-141502-bootnext")]
    [InlineData("clear", BootNext, "", "20260930-141502-bootnext-clear")]
    public async Task HappyPath_WritesOnce_AfterTwoDriftReads_AndReadsBackOnce(string operation, string target, string writtenHex, string folderName)
    {
        (VariableSet set, ChangeRequest request) = Operation(operation);
        using TransactionRig rig = new(set);

        PrepareResult prepared = await rig.Prepare(request);
        Assert.True(prepared.Passed, prepared.Sentence);
        ChangePlan plan = Assert.IsType<ChangePlan>(prepared.Plan);
        int readsAfterPrepare = rig.Store.ReadsOf(target);
        int bootNextReadsAfterPrepare = rig.Store.ReadsOf(BootNext);

        TransactionResult result = await rig.Pipeline.CommitAsync(plan, plan.Confirm(BitLockerChoice.NotNeeded));

        Assert.Equal(TransactionOutcome.Verified, result.Outcome);
        Assert.Equal(WriteExitCodes.Success, result.ExitCode);
        WritePlan call = Assert.Single(rig.Writer.Calls);
        Assert.Equal(target, call.Name);
        Assert.Equal(Global, call.Guid);
        Assert.Equal(0x7u, call.Attributes);
        Assert.Equal(writtenHex, Convert.ToHexString(call.Bytes).ToLowerInvariant());

        // Two drift reads before the call, one verification read after it, nothing else.
        Assert.Equal(readsAfterPrepare + 2, rig.Writer.ReadsAtCall!.Count(name => name == target));
        Assert.Equal(readsAfterPrepare + 3, rig.Store.ReadsOf(target));
        if (operation == "reorder")
        {
            // BootNext beside BootOrder: with BitLocker off no rule reads it, so these two reads are
            // the drift check's own (the one-time boot starts before the order at the next start).
            Assert.DoesNotContain(plan.Dependencies, dependency => dependency.Name == BootNext);
            Assert.Equal(bootNextReadsAfterPrepare + 2, rig.Writer.ReadsAtCall!.Count(name => name == BootNext));
        }
        Assert.True(rig.Writer.SnapshotReadyAtCall);
        Assert.Empty(rig.Suspender.Calls);

        string folder = Path.Combine(rig.SnapshotRoot, folderName);
        Assert.Equal(folder, result.SnapshotFolder);
        Assert.NotNull(result.Verification);
        Assert.True(result.Verification!.MatchesAfter);
        Assert.Equal(WriteRecordOutcome.Verified, ReadRecordFile(folder)!.Outcome);
        SnapshotReadResult snapshot = rig.Snapshots.Read(folder);
        Assert.True(snapshot.Succeeded, snapshot.Error);
        Assert.NotNull(snapshot.Document!.After);
        Assert.True(snapshot.Document.After!.MatchesProposed);
        Assert.Equal(operation != "clear", Directory.Exists(Path.Combine(folder, SnapshotStore.AfterDirectoryName)));
        Assert.Equal(ExpectedVerifiedSentence(operation, folderName), result.Sentence);
    }

    [Fact]
    public async Task Prepare_WritesNothing_MakesNoTransactionFolder_AndAppendsOneRecordLine()
    {
        using TransactionRig rig = new();

        PrepareResult prepared = await rig.Prepare(Reorder(UsbNumber, WindowsOnPartition1));

        Assert.True(prepared.Passed, prepared.Sentence);
        Assert.Equal(TransactionSentences.ChecksPassed, prepared.Sentence);
        Assert.Empty(rig.Writer.Calls);
        Assert.Empty(rig.Suspender.Calls);
        Assert.True(Directory.Exists(rig.SnapshotRoot));
        Assert.True(Directory.Exists(rig.WritesDirectory));
        Assert.Empty(rig.TransactionFolders());
        WriteRecord line = Assert.Single(rig.Records.ReadAll());
        Assert.Equal(WriteRecordOutcome.Prepared, line.Outcome);
        Assert.False(line.ApiCalled);
        Assert.Equal(ReorderFolder, line.TransactionId);
        Assert.Equal(ReorderFolder, prepared.Plan!.SnapshotFolderName);

        string[] names = prepared.Checks.Select(check => check.Name).ToArray();
        Assert.Equal(new[] { CheckNames.Preconditions, CheckNames.ExpectedModel, CheckNames.ExpectedUnit, CheckNames.Enumeration }, names.Take(4));
        Assert.Equal(new[] { CheckNames.SecureBoot, CheckNames.Baseline, CheckNames.BitLocker, CheckNames.SnapshotRoot, CheckNames.WriteBudget }, names.TakeLast(5));
        Assert.All(prepared.Checks, check => Assert.True(check.Passed, check.Name + ": " + check.Reason));
    }

    [Fact]
    public async Task Prepare_PutsTheFindingsInThePlan()
    {
        using TransactionRig rig = new(bitLocker: TransactionFakes.BitLockerOn());

        PrepareResult prepared = await rig.Prepare(Reorder(UsbNumber, WindowsOnPartition1));

        ChangePlan plan = Assert.IsType<ChangePlan>(prepared.Plan);
        Assert.True(plan.Policy.MachineConfirmed);
        Assert.Equal(BitLockerState.On, plan.Policy.BitLocker.State);
        Assert.Same(plan.Policy.BitLocker, prepared.BitLocker);
        Assert.Equal("BitLocker protects drive " + BitLockerPreflight.OsDriveLetter + " with the TPM and checks the boot path (PCR 0, 2, 4, 11; TPM 1.2).", plan.Policy.BitLocker.StateSentence);
        Assert.Equal("Secure Boot: on, user mode. This application never changes it.", prepared.Warnings[0]);
        Assert.Equal(prepared.Warnings, plan.Policy.Warnings);
        Assert.Null(plan.Policy.BaselineFolder);
        Assert.Contains(prepared.Checks, check => check.Name == CheckNames.Baseline && check.Reason == TransactionSentences.BaselineNotCompared);
    }

    /// <summary>
    /// A check makes no plan, so no confirmation follows to refuse it: on a machine that is not
    /// the contracted one it runs its stages (S9 included) and is then refused with the
    /// Writability sentence, and the record appended to write-log.jsonl says so (Blocked, exit 7,
    /// expectedModel), as the command-line document does.
    /// </summary>
    [Theory]
    [InlineData(BootOrder)]
    [InlineData(BootNext)]
    public async Task ACheckOnAnotherMachine_IsRefusedAtTheMachineCheck_AndTheRecordSaysSo(string name)
    {
        using TransactionRig rig = new(machineConfirmed: false);

        PrepareResult prepared = await rig.Prepare(Check(name));

        Assert.False(prepared.Passed);
        Assert.Null(prepared.Plan);
        Assert.Equal(CheckNames.ExpectedModel, prepared.BlockedBy);
        Assert.Equal(WriteExitCodes.Blocked, prepared.ExitCode);
        Assert.Equal(WritabilitySentences.WrongMachine, prepared.Sentence);
        CheckResult last = prepared.Checks[^1];
        Assert.Equal(CheckNames.ExpectedModel, last.Name);
        Assert.False(last.Passed);
        Assert.Contains(prepared.Checks, check => check.Name == CheckNames.WriteBudget && check.Passed);
        Assert.Empty(rig.Writer.Calls);
        Assert.Empty(rig.Suspender.Calls);
        Assert.Empty(rig.TransactionFolders());
        WriteRecord line = Assert.Single(rig.Records.ReadAll());
        Assert.Equal(WriteRecordOutcome.Blocked, line.Outcome);
        Assert.Equal(WriteExitCodes.Blocked, line.ExitCode);
        Assert.Equal(CheckNames.ExpectedModel, line.BlockedBy);
        Assert.False(line.ApiCalled);
    }

    // ---- the unit (decided 25 September 2026: M3 writes only on the developer's OptiPlex 7020) ----

    /// <summary>
    /// the tester's case: another OptiPlex 7020 on BIOS A18. The dry run passes with the machine not
    /// confirmed (the model check passes, the unit check warns), and Confirm change is refused at
    /// expectedUnit, before the snapshot, the suspend and the call; the record says which failed.
    /// </summary>
    [Fact]
    public async Task OnAnotherUnit_TheDryRunPasses_AndTheCommitIsRefusedAtTheUnitCheck_WithNothingTouched()
    {
        using TransactionRig rig = new(bitLocker: TransactionFakes.BitLockerOn()) { MachineId = OtherUnitMachineId };

        PrepareResult prepared = await rig.Prepare(Reorder(UsbNumber, WindowsOnPartition1));

        Assert.True(prepared.Passed, prepared.Sentence);
        ChangePlan plan = Assert.IsType<ChangePlan>(prepared.Plan);
        Assert.False(plan.Policy.MachineConfirmed);
        Assert.True(plan.Policy.ModelConfirmed);
        Assert.Equal(UnitMatch.OtherUnit, plan.Policy.Unit);
        Assert.Contains(prepared.Checks, check => check.Name == CheckNames.ExpectedModel && check.Passed && !check.IsWarning);
        Assert.Contains(prepared.Checks, check => check.Name == CheckNames.ExpectedUnit && check.Passed && check.IsWarning && check.Reason == CheckSentences.NotEnabledUnit);
        Assert.Single(prepared.Warnings, warning => warning == WritabilitySentences.WrongMachine);
        Assert.Equal(new WriteRecordMachine("OptiPlex 7020", "A18", true, false), prepared.Record.Machine);

        TransactionResult result = await rig.Pipeline.CommitAsync(plan, plan.Confirm(BitLockerChoice.SuspendForOneRestart));

        Assert.Equal(TransactionOutcome.Blocked, result.Outcome);
        Assert.Equal(WriteExitCodes.Blocked, result.ExitCode);
        Assert.Equal(CheckNames.ExpectedUnit, result.BlockedBy);
        Assert.Equal(CheckSentences.NotEnabledUnit, result.Sentence);
        Assert.Empty(rig.Writer.Calls);
        Assert.Empty(rig.Suspender.Calls);
        Assert.False(result.ApiCalled);
        Assert.Null(result.SnapshotFolder);
        Assert.Empty(rig.TransactionFolders());
        Assert.False(result.Record.Confirmed);
        Assert.Equal(new[] { CheckNames.Confirmation, CheckNames.ExpectedModel, CheckNames.ExpectedUnit }, result.Record.Checks.Select(check => check.Name).TakeLast(3));
        Assert.True(result.Record.Checks[^2].Passed);
        Assert.False(result.Record.Checks[^1].Passed);
        Assert.Equal(new WriteRecordMachine("OptiPlex 7020", "A18", true, false), result.Record.Machine);
        WriteRecord line = rig.Records.ReadAll()[^1];
        Assert.Equal(CheckNames.ExpectedUnit, line.BlockedBy);
        Assert.False(line.Machine!.UnitConfirmed);
        Assert.True(line.Machine.Confirmed);
    }

    /// <summary>
    /// The unit check at S11 does not depend on the operation: a new order, a one-time boot, the
    /// removal of a one-time boot and a restore (of a snapshot made on the test unit, prepared
    /// after the machine ID has become another unit's) are all refused at expectedUnit on another
    /// OptiPlex 7020 on A18, with no further write call, no suspend and no new transaction folder.
    /// So a later shortcut that skipped the machine checks for a restore or a clear would fail here.
    /// </summary>
    [Theory]
    [InlineData("reorder")]
    [InlineData("bootnext")]
    [InlineData("clear")]
    [InlineData("restore")]
    public async Task EveryOperationOnAnotherUnit_IsRefusedAtTheUnitCheck_WithNothingTouched(string operation)
    {
        (VariableSet set, ChangeRequest request) = Operation(operation == "restore" ? "reorder" : operation);
        using TransactionRig rig = new(set);
        if (operation == "restore")
        {
            // The snapshot is made on the test unit; the machine ID changes before the restore is prepared.
            (ChangePlan _, TransactionResult written) = await rig.PrepareAndCommit(request);
            Assert.Equal(TransactionOutcome.Verified, written.Outcome);
            SnapshotSummary newest = Assert.IsType<SnapshotSummary>(rig.Snapshots.Newest(Whitelist.BootOrder.Key));
            request = new RestoreRequest(ChangeRequest.AppHost, newest.Folder);
        }

        int writesBefore = rig.Writer.Calls.Count;
        int foldersBefore = rig.TransactionFolders().Length;
        int suspendsBefore = rig.Suspender.Calls.Count;
        rig.MachineId = OtherUnitMachineId;

        PrepareResult prepared = await rig.Prepare(request);

        Assert.True(prepared.Passed, prepared.Sentence);
        ChangePlan plan = Assert.IsType<ChangePlan>(prepared.Plan);
        Assert.False(plan.Policy.MachineConfirmed);
        Assert.Equal(UnitMatch.OtherUnit, plan.Policy.Unit);
        Assert.Equal(operation == "restore", plan.RestoredFrom is not null);

        TransactionResult result = await rig.Pipeline.CommitAsync(plan, plan.Confirm(BitLockerChoice.NotNeeded));

        Assert.Equal(TransactionOutcome.Blocked, result.Outcome);
        Assert.Equal(WriteExitCodes.Blocked, result.ExitCode);
        Assert.Equal(CheckNames.ExpectedUnit, result.BlockedBy);
        Assert.Equal(CheckSentences.NotEnabledUnit, result.Sentence);
        Assert.Equal(writesBefore, rig.Writer.Calls.Count);
        Assert.Equal(suspendsBefore, rig.Suspender.Calls.Count);
        Assert.Equal(foldersBefore, rig.TransactionFolders().Length);
        Assert.False(result.ApiCalled);
        Assert.Null(result.SnapshotFolder);
        Assert.False(result.Record.Machine!.UnitConfirmed);
        WriteRecord line = rig.Records.ReadAll()[^1];
        Assert.Equal(CheckNames.ExpectedUnit, line.BlockedBy);
        Assert.False(line.Machine!.UnitConfirmed);
    }

    /// <summary>A machine ID that cannot be read (missing, empty, not a GUID, or a read that throws) never confirms the unit: the commit is refused at expectedUnit and says why.</summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("not-a-guid", false)]
    [InlineData(TestUnitMachineId, true)]
    public async Task AMachineIdThatCannotBeRead_IsRefusedAtTheUnitCheck_WithItsOwnSentence(string? machineId, bool readThrows)
    {
        using TransactionRig rig = new() { MachineId = machineId };
        if (readThrows)
        {
            rig.MachineIdFailure = new UnauthorizedAccessException("registry denied");
        }

        PrepareResult prepared = await rig.Prepare(Reorder(UsbNumber, WindowsOnPartition1));
        ChangePlan plan = Assert.IsType<ChangePlan>(prepared.Plan);
        Assert.False(plan.Policy.MachineConfirmed);
        Assert.Equal(UnitMatch.MachineIdNotRead, plan.Policy.Unit);
        Assert.Contains(prepared.Checks, check => check.Name == CheckNames.ExpectedUnit && check.IsWarning && check.Reason == CheckSentences.MachineIdNotRead);

        TransactionResult result = await rig.Pipeline.CommitAsync(plan, plan.Confirm(BitLockerChoice.NotNeeded));

        Assert.Equal(TransactionOutcome.Blocked, result.Outcome);
        Assert.Equal(CheckNames.ExpectedUnit, result.BlockedBy);
        Assert.Equal(CheckSentences.MachineIdNotRead, result.Sentence);
        Assert.Empty(rig.Writer.Calls);
        Assert.Empty(rig.Suspender.Calls);
        Assert.Empty(rig.TransactionFolders());
        Assert.False(result.Record.Machine!.UnitConfirmed);
        Assert.False(rig.Records.ReadAll()[^1].Machine!.UnitConfirmed);
    }

    /// <summary>The model and BIOS version are checked before the unit: the OptiPlex 7020 on A17 with the unit's machine ID is refused at expectedModel, and the record says the unit was confirmed.</summary>
    [Fact]
    public async Task AnOptiPlexOnA17_WithTheUnitsMachineId_IsRefusedAtTheModelCheck()
    {
        using TransactionRig rig = new();
        rig.Identity.Result = rig.Identity.Result with { BiosVersion = "A17" };

        PrepareResult prepared = await rig.Prepare(Reorder(UsbNumber, WindowsOnPartition1));
        ChangePlan plan = Assert.IsType<ChangePlan>(prepared.Plan);
        Assert.False(plan.Policy.MachineConfirmed);
        Assert.False(plan.Policy.ModelConfirmed);
        Assert.Equal(UnitMatch.Confirmed, plan.Policy.Unit);

        TransactionResult result = await rig.Pipeline.CommitAsync(plan, plan.Confirm(BitLockerChoice.NotNeeded));

        Assert.Equal(CheckNames.ExpectedModel, result.BlockedBy);
        Assert.Equal(CheckSentences.WrongMachine, result.Sentence);
        Assert.DoesNotContain(result.Record.Checks.SkipWhile(check => check.Name != CheckNames.Confirmation), check => check.Name == CheckNames.ExpectedUnit);
        Assert.Equal(new WriteRecordMachine("OptiPlex 7020", "A17", false, true), result.Record.Machine);
        Assert.Empty(rig.Writer.Calls);
    }

    [Fact]
    public async Task OnTheTestUnit_BothMachineChecksPass_AtTheDryRunAndTheConfirmation_AndTheChangeIsVerified()
    {
        using TransactionRig rig = new();

        (ChangePlan plan, TransactionResult result) = await rig.PrepareAndCommit(Reorder(UsbNumber, WindowsOnPartition1));

        Assert.True(plan.Policy.MachineConfirmed);
        Assert.Equal(UnitMatch.Confirmed, plan.Policy.Unit);
        Assert.Equal(TransactionOutcome.Verified, result.Outcome);
        Assert.Single(rig.Writer.Calls);
        Assert.Equal(2, result.Record.Checks.Count(check => check.Name == CheckNames.ExpectedUnit && check.Passed && !check.IsWarning));
        Assert.Equal(new WriteRecordMachine("OptiPlex 7020", "A18", true, true), result.Record.Machine);
        Assert.True(ReadRecordFile(result.SnapshotFolder!)!.Machine!.UnitConfirmed);
    }

    /// <summary>
    /// A check makes no plan, so on another OptiPlex 7020 on A18 it is refused after S9 at
    /// expectedUnit, exactly as on another model at expectedModel: the Writability sentence, exit
    /// 7, the WrongMachine verdict, and a record that says so.
    /// </summary>
    [Theory]
    [InlineData(BootOrder)]
    [InlineData(BootNext)]
    public async Task ACheckOnAnotherUnit_IsRefusedAtTheUnitCheck_AndTheRecordSaysSo(string name)
    {
        using TransactionRig rig = new() { MachineId = OtherUnitMachineId };

        PrepareResult prepared = await rig.Prepare(Check(name));

        Assert.False(prepared.Passed);
        Assert.Equal(CheckNames.ExpectedUnit, prepared.BlockedBy);
        Assert.Equal(WriteExitCodes.Blocked, prepared.ExitCode);
        Assert.Equal(WritabilitySentences.WrongMachine, prepared.Sentence);
        Assert.Equal(CheckNames.ExpectedUnit, prepared.Checks[^1].Name);
        Assert.False(prepared.Checks[^1].Passed);
        if (name == BootOrder)
        {
            Assert.Equal(Writability.WrongMachine, prepared.Writability!.Verdict);
            Assert.Equal(WritabilitySentences.ItemStatusElsewhere, prepared.Writability.ItemStatus);
        }

        WriteRecord line = Assert.Single(rig.Records.ReadAll());
        Assert.Equal(WriteRecordOutcome.Blocked, line.Outcome);
        Assert.Equal(CheckNames.ExpectedUnit, line.BlockedBy);
        Assert.Equal(new WriteRecordMachine("OptiPlex 7020", "A18", true, false), line.Machine);
        Assert.Empty(rig.Writer.Calls);
    }

    /// <summary>
    /// The machine ID and its hash never leave the unit check: not in write-log.jsonl, not in
    /// record.json, not in any file of the snapshot folder (before\platform.json and log.txt
    /// included), on the test unit after a verified write and on another unit after a refusal.
    /// </summary>
    [Theory]
    [InlineData(TestUnitMachineId)]
    [InlineData(OtherUnitMachineId)]
    public async Task TheMachineIdAndItsHash_AreInNoRecordAndNoSnapshotFile(string machineId)
    {
        using TransactionRig rig = new() { MachineId = machineId };

        (ChangePlan _, TransactionResult result) = await rig.PrepareAndCommit(Reorder(UsbNumber, WindowsOnPartition1));

        Assert.Equal(machineId == TestUnitMachineId ? TransactionOutcome.Verified : TransactionOutcome.Blocked, result.Outcome);
        string hash = Assert.IsType<string>(PlatformPolicy.MachineIdSha256(machineId));
        string[] files = Directory.GetFiles(rig.DataDirectory, "*", SearchOption.AllDirectories);
        Assert.Contains(files, file => file.EndsWith(WriteRecordLog.FileName, StringComparison.Ordinal));
        Assert.Equal(machineId == TestUnitMachineId, files.Any(file => file.EndsWith(SnapshotStore.SnapshotFileName, StringComparison.Ordinal)));
        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            Assert.DoesNotContain(machineId, text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(machineId.Replace("-", string.Empty, StringComparison.Ordinal), text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(hash, text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(PlatformPolicy.EnabledUnitMachineIdSha256, text, StringComparison.OrdinalIgnoreCase);
        }

        string json = result.Record.ToJson();
        Assert.DoesNotContain(machineId, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(hash, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ACheckOnTheContractedMachine_Passes_AndIsRecordedAsPrepared()
    {
        using TransactionRig rig = new();

        PrepareResult prepared = await rig.Prepare(Check(BootOrder));

        Assert.True(prepared.Passed, prepared.Sentence);
        Assert.Null(prepared.Plan);
        Assert.Equal(WritabilitySentences.ReorderOnly, prepared.Sentence);
        WriteRecord line = Assert.Single(rig.Records.ReadAll());
        Assert.Equal(WriteRecordOutcome.Prepared, line.Outcome);
        Assert.Equal(WriteExitCodes.Success, line.ExitCode);
        Assert.Null(line.BlockedBy);
    }

    [Theory]
    [InlineData("notUefi", CheckNames.Preconditions, WriteExitCodes.NotUefi)]
    [InlineData("notElevated", CheckNames.Preconditions, WriteExitCodes.NotElevated)]
    [InlineData("privilegeAbsent", CheckNames.Preconditions, WriteExitCodes.NotElevated)]
    [InlineData("fallbackEnumeration", CheckNames.Enumeration, WriteExitCodes.Blocked)]
    [InlineData("directReadFails", CheckNames.Enumeration, WriteExitCodes.Blocked)]
    [InlineData("notAPermutation", CheckNames.Permutation, WriteExitCodes.Blocked)]
    [InlineData("choiceMissing", CheckNames.BitLocker, WriteExitCodes.Blocked)]
    [InlineData("suspendNotOffered", CheckNames.BitLocker, WriteExitCodes.Blocked)]
    [InlineData("snapshotRootRefused", CheckNames.SnapshotRoot, WriteExitCodes.Blocked)]
    [InlineData("writeBudget", CheckNames.WriteBudget, WriteExitCodes.Blocked)]
    [InlineData("restoreUnreadable", CheckNames.Snapshot, WriteExitCodes.Blocked)]
    public async Task EveryDryRunStageThatRefuses_LeavesTheWriterUncalled(string scenario, string blockedBy, int exitCode)
    {
        using TransactionRig rig = new(bitLocker: scenario == "choiceMissing" ? TransactionFakes.BitLockerOn() : null);
        ChangeRequest request = Reorder(UsbNumber, WindowsOnPartition1);
        BitLockerChoice? choice = null;
        switch (scenario)
        {
            case "notUefi":
                rig.Detector.Type = FirmwareType.LegacyBios;
                break;
            case "notElevated":
                rig.Privileges.Elevated = false;
                break;
            case "privilegeAbsent":
                rig.Privileges.State = PrivilegeState.ElevatedPrivilegeAbsent;
                break;
            case "fallbackEnumeration":
                rig.Store.Method = EnumerationMethod.KnownNamesFallback;
                rig.Store.EnumerationError = "NtEnumerateSystemEnvironmentValuesEx failed";
                break;
            case "directReadFails":
                rig.Store.Failures[BootOrder] = ReadStatus.AccessDenied;
                break;
            case "notAPermutation":
                request = Reorder(WindowsOnPartition1, WindowsOnPartition2);
                break;
            case "choiceMissing":
                choice = BitLockerChoice.NotNeeded;
                break;
            case "suspendNotOffered":
                choice = BitLockerChoice.SuspendForOneRestart;
                break;
            case "snapshotRootRefused":
                rig.RefuseSnapshotFolders = true;
                break;
            case "writeBudget":
                rig.AddPastWrites(7, TransactionRig.StartOfWindows.AddMinutes(10));
                break;
            case "restoreUnreadable":
                request = new RestoreRequest(ChangeRequest.CliHost, Path.Combine(rig.SnapshotRoot, "20260930-120000-reorder"));
                break;
        }

        PrepareResult prepared = await rig.Prepare(request, choice);

        Assert.Null(prepared.Plan);
        Assert.False(prepared.Passed);
        Assert.Equal(blockedBy, prepared.BlockedBy);
        Assert.Equal(exitCode, prepared.ExitCode);
        CheckResult last = prepared.Checks[^1];
        Assert.Equal(blockedBy, last.Name);
        Assert.False(last.Passed);
        Assert.Equal(prepared.Sentence, last.Reason);
        Assert.Empty(rig.Writer.Calls);
        Assert.Empty(rig.Suspender.Calls);
        Assert.Empty(rig.TransactionFolders());
        Assert.Equal(WriteRecordOutcome.Blocked, prepared.Record.Outcome);
        Assert.Equal(blockedBy, prepared.Record.BlockedBy);
        Assert.False(prepared.Record.ApiCalled);
        Assert.Equal(prepared.Record.ToJsonLine(), rig.Records.ReadAll()[^1].ToJsonLine());
    }

    [Theory]
    [InlineData("choiceMissing", CheckNames.Confirmation, false, false)]
    [InlineData("wrongMachine", CheckNames.ExpectedModel, false, false)]
    [InlineData("otherUnit", CheckNames.ExpectedUnit, false, false)]
    [InlineData("snapshotRootRefused", CheckNames.Snapshot, false, false)]
    [InlineData("recordRootRefused", CheckNames.Snapshot, false, false)]
    [InlineData("changedBeforeTheSnapshotRead", CheckNames.Stale, true, false)]
    [InlineData("suspendRefused", CheckNames.BitLockerSuspend, true, true)]
    [InlineData("suspendTimedOut", CheckNames.BitLockerSuspend, true, true)]
    [InlineData("changedBeforeTheCall", CheckNames.Stale, true, true)]
    public async Task EveryCommitStageThatRefuses_LeavesTheWriterUncalled(string scenario, string blockedBy, bool folderExpected, bool suspendCalled)
    {
        using TransactionRig rig = new(bitLocker: TransactionFakes.BitLockerOn(), machineConfirmed: scenario != "wrongMachine")
        {
            MachineId = scenario == "otherUnit" ? OtherUnitMachineId : TestUnitMachineId,
        };
        PrepareResult prepared = await rig.Prepare(Reorder(UsbNumber, WindowsOnPartition1));
        ChangePlan plan = Assert.IsType<ChangePlan>(prepared.Plan);
        BitLockerChoice choice = BitLockerChoice.SuspendForOneRestart;
        FirmwareVariable changed = Var(BootOrder, Concat(U16(UsbNumber), U16(WindowsOnPartition1)));
        switch (scenario)
        {
            case "choiceMissing":
                choice = BitLockerChoice.NotNeeded;
                break;
            case "snapshotRootRefused":
                rig.RefuseSnapshotFolders = true;
                break;
            case "recordRootRefused":
                rig.RefuseRecordFolder = true;
                break;
            case "changedBeforeTheSnapshotRead":
                rig.Store.Set(changed);
                break;
            case "suspendRefused":
                rig.Suspender.Result = TransactionFakes.FakeSuspender.Refused(0x80310000);
                break;
            case "suspendTimedOut":
                rig.Suspender.Result = TransactionFakes.FakeSuspender.TimedOut();
                break;
            case "changedBeforeTheCall":
                rig.Suspender.DuringCall = () => rig.Store.Set(changed);
                break;
        }

        TransactionResult result = await rig.Pipeline.CommitAsync(plan, plan.Confirm(choice));

        Assert.Equal(TransactionOutcome.Blocked, result.Outcome);
        Assert.Equal(WriteExitCodes.Blocked, result.ExitCode);
        Assert.Equal(blockedBy, result.BlockedBy);
        Assert.Empty(rig.Writer.Calls);
        Assert.False(result.ApiCalled);
        Assert.Null(result.Api);
        Assert.Null(result.Verification);
        Assert.Equal(suspendCalled ? 1 : 0, rig.Suspender.Calls.Count);
        Assert.Equal(WriteRecordOutcome.Blocked, result.Record.Outcome);
        Assert.Equal(blockedBy, result.Record.Checks[^1].Name);
        Assert.False(result.Record.Checks[^1].Passed);
        Assert.Equal(folderExpected, result.SnapshotFolder is not null);
        Assert.Equal(folderExpected ? 1 : 0, rig.TransactionFolders().Length);
        if (folderExpected)
        {
            // A folder whose write never happened says so, so Restore previous value skips it.
            Assert.Equal(WriteRecordOutcome.Blocked, ReadRecordFile(result.SnapshotFolder!)!.Outcome);
            Assert.Null(rig.Snapshots.Newest(Whitelist.BootOrder.Key));
        }

        if (scenario != "recordRootRefused")
        {
            Assert.Equal(result.Record.ToJsonLine(), rig.Records.ReadAll()[^1].ToJsonLine());
        }

        switch (scenario)
        {
            case "wrongMachine":
                Assert.Equal(CheckSentences.WrongMachine, result.Sentence);
                break;
            case "otherUnit":
                Assert.Equal(CheckSentences.NotEnabledUnit, result.Sentence);
                Assert.False(result.Record.Machine!.UnitConfirmed);
                break;
            case "choiceMissing":
                Assert.Equal(CheckSentences.BitLockerChoiceMissing, result.Sentence);
                break;
            case "changedBeforeTheSnapshotRead":
                Assert.Equal(CheckSentences.Format(CheckSentences.StaleReadFormat, BootOrder), result.Sentence);
                break;
            case "suspendTimedOut":
                Assert.Equal(CheckSentences.BitLockerSuspendTimedOut, result.Sentence);
                Assert.Equal(BitLockerState.Unknown, result.Record.BitLocker!.State);
                Assert.Equal(BitLockerSuspender.TimeoutResultText, result.Record.BitLocker.SuspendResult);
                Assert.False(result.BitLockerSuspended);
                break;
            case "suspendRefused":
                Assert.Equal(rig.Suspender.Result.Sentence, result.Sentence);
                Assert.Equal("0x80310000", result.Record.BitLocker!.SuspendResult);
                break;
            case "changedBeforeTheCall":
                Assert.True(result.BitLockerSuspended);
                Assert.Equal(CheckSentences.Format(CheckSentences.StaleReadFormat, BootOrder) + " " + CheckSentences.BitLockerLeftSuspended, result.Sentence);
                break;
        }
    }

    [Fact]
    public async Task ATokenIssuedForAnotherPlan_IsRefusedAtTheConfirmation()
    {
        using TransactionRig rig = new(ThreeEntries());
        PrepareResult first = await rig.Prepare(Reorder(UsbNumber, WindowsOnPartition1, WindowsCopyNumber));
        PrepareResult other = await rig.Prepare(Reorder(WindowsCopyNumber, WindowsOnPartition1, UsbNumber));
        Assert.NotEqual(first.Plan!.PlanHash, other.Plan!.PlanHash);

        TransactionResult result = await rig.Pipeline.CommitAsync(first.Plan, other.Plan.Confirm(BitLockerChoice.NotNeeded));

        Assert.Equal(TransactionOutcome.Blocked, result.Outcome);
        Assert.Equal(CheckNames.Confirmation, result.BlockedBy);
        Assert.Equal(CheckSentences.TokenDoesNotMatchPlan, result.Sentence);
        Assert.Empty(rig.Writer.Calls);
        Assert.Empty(rig.TransactionFolders());
    }

    [Fact]
    public async Task APlan_IsCommittedAtMostOnce()
    {
        using TransactionRig rig = new();
        (ChangePlan plan, TransactionResult first) = await rig.PrepareAndCommit(Reorder(UsbNumber, WindowsOnPartition1));
        Assert.Equal(TransactionOutcome.Verified, first.Outcome);

        TransactionResult second = await rig.Pipeline.CommitAsync(plan, plan.Confirm(BitLockerChoice.NotNeeded));

        Assert.Equal(TransactionOutcome.Blocked, second.Outcome);
        Assert.Equal(CheckNames.Confirmation, second.BlockedBy);
        Assert.Equal(TransactionSentences.PlanNotPrepared, second.Sentence);
        Assert.Single(rig.Writer.Calls);
    }

    [Fact]
    public async Task Suspend_RunsAfterTheSnapshotAndTheFirstDriftRead_AndBeforeTheSecond()
    {
        using TransactionRig rig = new(bitLocker: TransactionFakes.BitLockerOn());
        PrepareResult prepared = await rig.Prepare(Reorder(UsbNumber, WindowsOnPartition1));
        ChangePlan plan = Assert.IsType<ChangePlan>(prepared.Plan);
        int reads = rig.Store.ReadsOf(BootOrder);

        TransactionResult result = await rig.Pipeline.CommitAsync(plan, plan.Confirm(BitLockerChoice.SuspendForOneRestart));

        Assert.Equal(TransactionOutcome.Verified, result.Outcome);
        Assert.Equal(BitLockerPreflight.OsDriveLetter, Assert.Single(rig.Suspender.Calls));
        Assert.True(rig.Suspender.SnapshotReadyAtCall);
        Assert.Equal(reads + 1, rig.Suspender.ReadsAtCall!.Count(name => name == BootOrder));
        Assert.Equal(reads + 2, rig.Writer.ReadsAtCall!.Count(name => name == BootOrder));
        Assert.True(result.BitLockerSuspended);
        Assert.EndsWith(" " + TransactionSentences.SuspendedForOneRestart, result.Sentence);
        Assert.Equal(
            new[]
            {
                CheckNames.Confirmation, CheckNames.ExpectedModel, CheckNames.ExpectedUnit, CheckNames.WriteBudget, CheckNames.Snapshot, CheckNames.Stale,
                CheckNames.BitLocker, CheckNames.BitLockerSuspend, CheckNames.Stale,
            },
            result.Record.Checks.Select(check => check.Name).TakeLast(9));
        WriteRecordBitLocker bitLocker = Assert.IsType<WriteRecordBitLocker>(result.Record.BitLocker);
        Assert.Equal(BitLockerState.On, bitLocker.State);
        Assert.True(bitLocker.SuspendOffered);
        Assert.True(bitLocker.SuspendChosen);
        Assert.Equal("0", bitLocker.SuspendResult);
        Assert.Equal(0, bitLocker.ProtectionStatusAfterSuspend);
        Assert.Equal(BitLockerChoice.SuspendForOneRestart, result.Record.BitLockerChoice);
    }

    [Fact]
    public async Task Suspend_IsNotOfferedForAClear_AndRefusedWhenChosen()
    {
        using TransactionRig rig = new(Store(Var(BootNext, U16(WindowsOnPartition1))), TransactionFakes.BitLockerOn());
        PrepareResult prepared = await rig.Prepare(Clear());
        ChangePlan plan = Assert.IsType<ChangePlan>(prepared.Plan);

        TransactionResult result = await rig.Pipeline.CommitAsync(plan, plan.Confirm(BitLockerChoice.SuspendForOneRestart));

        Assert.Equal(TransactionOutcome.Blocked, result.Outcome);
        Assert.Equal(CheckNames.Confirmation, result.BlockedBy);
        Assert.Equal(TransactionSentences.SuspendNotOffered, result.Sentence);
        Assert.Empty(rig.Suspender.Calls);
        Assert.Empty(rig.Writer.Calls);
    }

    [Theory]
    [InlineData(19)]
    [InlineData(5)]
    [InlineData(50)]
    [InlineData(87)]
    [InlineData(203)]
    [InlineData(1470)]
    [InlineData(31)]
    public async Task FirmwareRefusal_IsRecorded_AndNeverFollowedByASecondCall(int win32Error)
    {
        using TransactionRig rig = new();
        rig.Writer.Win32Error = win32Error;
        rig.Writer.NtStatus = StatusBehind(win32Error);

        (ChangePlan plan, TransactionResult result) = await rig.PrepareAndCommit(Reorder(UsbNumber, WindowsOnPartition1));

        Assert.Single(rig.Writer.Calls);
        Assert.Equal(TransactionOutcome.ApiRefused, result.Outcome);
        Assert.Equal(WriteExitCodes.ApiRefused, result.ExitCode);
        WriteOutcome api = Assert.IsType<WriteOutcome>(result.Api);
        Assert.False(api.Succeeded);
        Assert.Equal(win32Error, api.Win32Error);
        Assert.Equal(WriteStatusMapping.FromWin32(win32Error), api.Status);
        Assert.True(result.Verification!.MatchesBefore);
        Assert.Equal(WriteStatusMapping.Sentence(api) + " Snapshot " + ReorderFolder + ".", result.Sentence);
        WriteRecordApi recorded = Assert.IsType<WriteRecordApi>(result.Record.Api);
        Assert.Equal(win32Error, recorded.Win32Error);
        Assert.Equal(StatusBehind(win32Error), recorded.NtStatus);
        Assert.Equal(FirmwareVariableWriter.NtStatusSourceName, recorded.NtStatusSource);
        Assert.True(result.Record.ApiCalled);
        Assert.Equal(plan.Before.Hex, Convert.ToHexString(rig.Store.Current(BootOrder)!.Data).ToLowerInvariant());
    }

    [Theory]
    [InlineData(0, true, false, TransactionOutcome.Verified, WriteExitCodes.Success)]
    [InlineData(19, false, false, TransactionOutcome.ApiRefused, WriteExitCodes.ApiRefused)]
    [InlineData(5, true, false, TransactionOutcome.ChangedDespiteError, WriteExitCodes.Unverified)]
    [InlineData(0, false, false, TransactionOutcome.Unverified, WriteExitCodes.Unverified)]
    [InlineData(0, true, true, TransactionOutcome.Unverified, WriteExitCodes.Unverified)]
    public async Task TheVerificationRead_DecidesTheOutcome(int win32Error, bool firmwareChanges, bool readBackFails, TransactionOutcome expected, int exitCode)
    {
        using TransactionRig rig = new();
        rig.Writer.Win32Error = win32Error;
        rig.Writer.ChangesTheStore = firmwareChanges;
        int readsAtCall = 0;
        rig.Writer.DuringCall = _ =>
        {
            readsAtCall = rig.Store.ReadsOf(BootOrder);
            if (readBackFails)
            {
                rig.Store.Failures[BootOrder] = ReadStatus.Failed;
            }
        };

        (_, TransactionResult result) = await rig.PrepareAndCommit(Reorder(UsbNumber, WindowsOnPartition1));

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(exitCode, result.ExitCode);
        Assert.Single(rig.Writer.Calls);
        Assert.Equal(readsAtCall + 1, rig.Store.ReadsOf(BootOrder));
        VerificationResult verification = Assert.IsType<VerificationResult>(result.Verification);
        Assert.Equal(readBackFails ? ReadStatus.Failed : ReadStatus.Success, verification.ReadStatus);
        Assert.Equal(verification.ReadStatus, result.Record.Verification!.ReadStatus);
        switch (expected)
        {
            case TransactionOutcome.ChangedDespiteError:
                Assert.Equal(WriteStatusMapping.ChangedDespiteErrorSentence(win32Error, ReorderFolder), result.Sentence);
                break;
            case TransactionOutcome.Unverified:
                string reason = readBackFails
                    ? CheckSentences.Format(TransactionSentences.ReadBackFailedFormat, ReadStatus.Failed)
                    : TransactionSentences.ReadBackDiffers;
                Assert.Equal(CheckSentences.Format(TransactionSentences.UnverifiedFormat, reason, ReorderFolder), result.Sentence);
                break;
        }

        string folder = Assert.IsType<string>(result.SnapshotFolder);
        Assert.Equal(result.Record.Outcome, ReadRecordFile(folder)!.Outcome);
    }

    [Theory]
    [InlineData(WriteRecordOutcome.Blocked)]
    [InlineData(WriteRecordOutcome.Cancelled)]
    [InlineData(WriteRecordOutcome.Verified)]
    [InlineData(WriteRecordOutcome.ApiRefused)]
    [InlineData(WriteRecordOutcome.Unverified)]
    [InlineData(WriteRecordOutcome.ChangedDespiteError)]
    public async Task TheRecord_IsCompleteInEveryOutcome_AndOnDiskBeforeTheResultReturns(WriteRecordOutcome outcome)
    {
        const string ServiceTagMarker = "TAGMARK7";
        using TransactionRig rig = new(bitLocker: TransactionFakes.BitLockerOn(), machineConfirmed: outcome != WriteRecordOutcome.Blocked);
        rig.Identity.Result = rig.Identity.Result with { ServiceTag = ServiceTagMarker };
        rig.Writer.Win32Error = outcome is WriteRecordOutcome.ApiRefused or WriteRecordOutcome.ChangedDespiteError ? 19 : 0;
        rig.Writer.ChangesTheStore = outcome is WriteRecordOutcome.Verified or WriteRecordOutcome.ChangedDespiteError;
        PrepareResult prepared = await rig.Prepare(Reorder(UsbNumber, WindowsOnPartition1));
        ChangePlan plan = Assert.IsType<ChangePlan>(prepared.Plan);

        TransactionResult result = outcome == WriteRecordOutcome.Cancelled
            ? await rig.Pipeline.CancelAsync(plan)
            : await rig.Pipeline.CommitAsync(plan, plan.Confirm(BitLockerChoice.KeepProtection));

        WriteRecord record = result.Record;
        Assert.Equal(outcome, record.Outcome);
        Assert.Equal(WriteRecord.SchemaId, record.Schema);
        Assert.Equal(WriteRecord.FormatTimestamp(TransactionRig.Now), record.TimestampUtc);
        Assert.Equal(ReorderFolder, record.TransactionId);
        Assert.False(string.IsNullOrEmpty(record.ToolVersion));
        Assert.Equal(ChangeRequest.CliHost, record.Host);
        Assert.Equal("Reorder", record.Operation);
        Assert.False(record.IsRestore);
        Assert.Null(record.RestoredFrom);
        Assert.Equal(new WriteRecordVariable(Whitelist.BootOrder.Key, BootOrder, Global.ToString("D")), record.Variable);
        Assert.Equal(7u, record.AttributesBefore);
        Assert.Equal(7u, record.AttributesRequested);
        Assert.Equal("NV BS RT", record.AttributeNames);
        Assert.Equal(4, record.SizeBefore);
        Assert.Equal(4, record.SizeRequested);
        Assert.Equal(plan.Before.Sha256, record.BeforeSha256);
        Assert.Equal(plan.After.Sha256, record.ProposedSha256);
        Assert.Equal(new[] { "Boot0002", "Boot0003" }, record.Before!.Entries);
        Assert.Equal("02000300", record.Before.Hex);
        Assert.Equal(new[] { "Boot0003", "Boot0002" }, record.After!.Entries);
        Assert.Equal("03000200", record.After.Hex);
        Assert.NotEmpty(record.Checks);
        Assert.Equal(result.BlockedBy, record.BlockedBy);
        Assert.Equal(outcome is not (WriteRecordOutcome.Blocked or WriteRecordOutcome.Cancelled), record.Confirmed);
        Assert.Equal(0, record.WritesThisStart);
        WriteRecordBitLocker bitLocker = Assert.IsType<WriteRecordBitLocker>(record.BitLocker);
        Assert.Equal(BitLockerState.On, bitLocker.State);
        Assert.Equal(1, bitLocker.ProtectionStatus);
        Assert.Equal(new[] { "TPM", "Numerical password" }, bitLocker.ProtectorTypes);
        Assert.Equal("PCR 0,2,4,11 (legacy)", bitLocker.PcrProfileText);
        Assert.True(bitLocker.SuspendOffered);
        Assert.False(bitLocker.SuspendChosen);
        Assert.Null(bitLocker.SuspendResult);
        Assert.Equal(new WriteRecordSecureBoot(true, false, true), record.SecureBoot);
        WriteRecordMachine expectedMachine = outcome == WriteRecordOutcome.Blocked
            ? new WriteRecordMachine("Other model", "1.0", false, true)
            : new WriteRecordMachine("OptiPlex 7020", "A18", true, true);
        Assert.Equal(expectedMachine, record.Machine);
        Assert.Equal(result.ExitCode, record.ExitCode);
        Assert.Equal(result.Sentence, record.Sentence);
        Assert.False(string.IsNullOrEmpty(record.Sentence));

        bool written = outcome is not (WriteRecordOutcome.Blocked or WriteRecordOutcome.Cancelled);
        Assert.Equal(written, record.ApiCalled);
        Assert.Equal(written, record.Api is not null);
        Assert.Equal(written, record.Verification is not null);
        Assert.Equal(written, record.SnapshotFolder is not null);
        Assert.Equal(written, record.AfterSha256 is not null);
        if (written)
        {
            Assert.Equal(BitLockerChoice.KeepProtection, record.BitLockerChoice);
            Assert.Equal(outcome, ReadRecordFile(record.SnapshotFolder!)!.Outcome);
        }

        // The line is in write-log.jsonl by the time the result is in the caller's hands, and it
        // carries no service tag (the snapshot's identity does, and stays on the unit). A commit
        // that reached S12 appended its Pending intent line first, which the budget counts until
        // this line follows it: the write is counted once.
        IReadOnlyList<WriteRecord> lines = rig.Records.ReadAll();
        Assert.Equal(WriteRecordOutcome.Prepared, lines[0].Outcome);
        Assert.Equal(record.ToJsonLine(), lines[^1].ToJsonLine());
        if (written)
        {
            Assert.Equal(3, lines.Count);
            WriteRecord intent = lines[1];
            Assert.Equal(WriteRecordOutcome.Pending, intent.Outcome);
            Assert.False(intent.ApiCalled);
            Assert.True(intent.Confirmed);
            Assert.Equal(record.TransactionId, intent.TransactionId);
            Assert.Equal(record.SnapshotFolder, intent.SnapshotFolder);
        }
        else
        {
            Assert.Equal(2, lines.Count);
        }

        Assert.Equal(written ? 1 : 0, rig.Records.CountSinceStart(TransactionRig.StartOfWindows).Count);
        Assert.DoesNotContain(ServiceTagMarker, File.ReadAllText(rig.Records.FilePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARecordFailureAfterAVerifiedWrite_KeepsTheOutcomeAndTheExitCode_AndSaysSo()
    {
        using TransactionRig rig = new();
        rig.Writer.DuringCall = _ => rig.RefuseRecordFolder = true;

        (_, TransactionResult result) = await rig.PrepareAndCommit(Reorder(UsbNumber, WindowsOnPartition1));

        Assert.Equal(TransactionOutcome.Verified, result.Outcome);
        Assert.Equal(WriteExitCodes.Success, result.ExitCode);
        Assert.StartsWith(ExpectedVerifiedSentence("reorder", ReorderFolder), result.Sentence);
        Assert.EndsWith(" " + CheckSentences.Format(CheckSentences.RecordNotSavedVerifiedFormat, TransactionRig.Refusal), result.Sentence);
        Assert.Equal(WriteRecordOutcome.Verified, ReadRecordFile(result.SnapshotFolder!)!.Outcome);

        // The S17 line is missing, but the intent S12 appended before the call is in the list, so
        // the write budget still counts the write.
        Assert.Equal(new[] { WriteRecordOutcome.Prepared, WriteRecordOutcome.Pending }, rig.Records.ReadAll().Select(line => line.Outcome));
        Assert.Equal(new WriteBudgetCount(1, null), rig.Records.CountSinceStart(TransactionRig.StartOfWindows));
    }

    [Fact]
    public async Task AWriteLogThatCannotTakeALine_BlocksTheCommitAtS12_BeforeTheSuspendAndTheCall()
    {
        using TransactionRig rig = new(bitLocker: TransactionFakes.BitLockerOn());
        PrepareResult prepared = await rig.Prepare(Reorder(UsbNumber, WindowsOnPartition1));
        ChangePlan plan = Assert.IsType<ChangePlan>(prepared.Plan);

        // Another program holds the list open for reading and lets no one write it (a viewer, a
        // copy): the budget can still be counted, but no line can be appended, so the write could
        // be made and never counted. The intent line of S12 is a pre-condition of the write.
        TransactionResult result;
        using (new FileStream(rig.Records.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            result = await rig.Pipeline.CommitAsync(plan, plan.Confirm(BitLockerChoice.SuspendForOneRestart));
        }

        Assert.Equal(TransactionOutcome.Blocked, result.Outcome);
        Assert.Equal(CheckNames.WriteBudget, result.BlockedBy);
        Assert.StartsWith(CheckSentences.Format(TransactionSentences.RecordRootRefusedFormat, string.Empty), result.Sentence, StringComparison.Ordinal);
        Assert.Contains(WriteRecordLog.FileName, result.Sentence, StringComparison.Ordinal);
        Assert.Empty(rig.Writer.Calls);
        Assert.Empty(rig.Suspender.Calls);
        Assert.False(result.ApiCalled);
        Assert.False(result.BitLockerSuspended);
        Assert.Empty(rig.TransactionFolders());
        Assert.Equal(new[] { WriteRecordOutcome.Prepared }, rig.Records.ReadAll().Select(line => line.Outcome));
    }

    [Fact]
    public async Task ACommitBlockedAfterItsIntent_IsNotCountedAsAWrite()
    {
        using TransactionRig rig = new(bitLocker: TransactionFakes.BitLockerOn());
        PrepareResult prepared = await rig.Prepare(Reorder(UsbNumber, WindowsOnPartition1));
        ChangePlan plan = Assert.IsType<ChangePlan>(prepared.Plan);
        rig.Suspender.DuringCall = () => rig.Store.Set(Var(BootOrder, Concat(U16(UsbNumber), U16(WindowsOnPartition1))));

        TransactionResult result = await rig.Pipeline.CommitAsync(plan, plan.Confirm(BitLockerChoice.SuspendForOneRestart));

        Assert.Equal(CheckNames.Stale, result.BlockedBy);
        Assert.Empty(rig.Writer.Calls);
        IReadOnlyList<WriteRecord> lines = rig.Records.ReadAll();
        Assert.Equal(new[] { WriteRecordOutcome.Prepared, WriteRecordOutcome.Pending, WriteRecordOutcome.Blocked }, lines.Select(line => line.Outcome));
        Assert.Equal(plan.SnapshotFolderName, lines[1].TransactionId);
        Assert.Equal(plan.SnapshotFolderName, lines[2].TransactionId);
        Assert.Equal(new WriteBudgetCount(0, null), rig.Records.CountSinceStart(TransactionRig.StartOfWindows));
    }

    [Fact]
    public async Task WritesOfThisBoot_CountEvenWhenAChangedClockPutsThemBeforeTheStart()
    {
        // Seven writes of this boot, stamped before the start the clock now gives (the clock was
        // stepped forward after them), and five of an earlier boot at the same time.
        using TransactionRig rig = new() { BootId = 41 };
        rig.AddPastWrites(7, TransactionRig.StartOfWindows.AddMinutes(-30), bootId: 41);
        rig.AddPastWrites(5, TransactionRig.StartOfWindows.AddMinutes(-30), bootId: 40);

        PrepareResult prepared = await rig.Prepare(Reorder(UsbNumber, WindowsOnPartition1));

        Assert.Null(prepared.Plan);
        Assert.Equal(CheckNames.WriteBudget, prepared.BlockedBy);
        Assert.Equal("This start has already made 7 changes to the firmware. Restart Windows before more changes.", prepared.Sentence);
        Assert.Equal(7, prepared.Record.WritesThisStart);
        Assert.Equal(41u, prepared.Record.BootId);
    }

    [Fact]
    public async Task ARefusedBudgetThatCountsLinesThatAreNotRecords_SaysHowMany()
    {
        using TransactionRig rig = new();
        rig.AddPastWrites(6, TransactionRig.StartOfWindows.AddMinutes(10));
        File.AppendAllText(rig.Records.FilePath, "{\"schema\":\"uefiaccess-write-record/1\",\"timestampUtc\":\"" + WriteRecord.FormatTimestamp(TransactionRig.StartOfWindows.AddMinutes(20)) + "\",\"transac");

        PrepareResult prepared = await rig.Prepare(Reorder(UsbNumber, WindowsOnPartition1));

        Assert.Equal(CheckNames.WriteBudget, prepared.BlockedBy);
        Assert.Equal(
            "This start has already made 7 changes to the firmware. Restart Windows before more changes. "
            + CheckSentences.Format(CheckSentences.WriteLogFragmentsFormat, 1),
            prepared.Sentence);
    }

    [Theory]
    [InlineData(7, true)]
    [InlineData(6, false)]
    public async Task TheEighthWriteSinceWindowsStarted_IsRefused(int writesThisStart, bool refused)
    {
        using TransactionRig rig = new();
        rig.AddPastWrites(writesThisStart, TransactionRig.StartOfWindows.AddMinutes(10));
        rig.AddPastWrites(5, TransactionRig.StartOfWindows.AddMinutes(-10));
        rig.AddPastWrites(3, TransactionRig.StartOfWindows.AddMinutes(10), apiCalled: false);

        PrepareResult prepared = await rig.Prepare(Reorder(UsbNumber, WindowsOnPartition1));

        Assert.Equal(writesThisStart, prepared.Record.WritesThisStart);
        if (refused)
        {
            Assert.Equal(CheckNames.WriteBudget, prepared.BlockedBy);
            Assert.Equal("This start has already made 7 changes to the firmware. Restart Windows before more changes.", prepared.Sentence);
            Assert.Null(prepared.Plan);
        }
        else
        {
            Assert.True(prepared.Passed, prepared.Sentence);
        }
    }

    [Fact]
    public async Task Cancel_RecordsCancelled_WithNoFolder_AndThePlanCannotBeCommittedAfterwards()
    {
        using TransactionRig rig = new(bitLocker: TransactionFakes.BitLockerOn());
        PrepareResult prepared = await rig.Prepare(Reorder(UsbNumber, WindowsOnPartition1));
        ChangePlan plan = Assert.IsType<ChangePlan>(prepared.Plan);

        TransactionResult cancelled = await rig.Pipeline.CancelAsync(plan);

        Assert.Equal(TransactionOutcome.Cancelled, cancelled.Outcome);
        Assert.Equal(TransactionSentences.Cancelled, cancelled.Sentence);
        Assert.Equal(WriteExitCodes.Success, cancelled.ExitCode);
        Assert.Null(cancelled.SnapshotFolder);
        Assert.Empty(rig.TransactionFolders());
        Assert.Equal(WriteRecordOutcome.Cancelled, rig.Records.ReadAll()[^1].Outcome);

        TransactionResult late = await rig.Pipeline.CommitAsync(plan, plan.Confirm(BitLockerChoice.SuspendForOneRestart));

        Assert.Equal(TransactionOutcome.Blocked, late.Outcome);
        Assert.Equal(TransactionSentences.PlanNotPrepared, late.Sentence);
        Assert.Empty(rig.Writer.Calls);
        Assert.Empty(rig.Suspender.Calls);
    }

    [Fact]
    public async Task ACancelBetweenDryRunStages_GivesACancelledRecord_AndStopsBeforeTheNextStage()
    {
        using TransactionRig rig = new();
        using CancellationTokenSource cancel = new();
        rig.Identity.DuringInspect = cancel.Cancel;

        PrepareResult prepared = await rig.Pipeline.PrepareAsync(Reorder(UsbNumber, WindowsOnPartition1), cancel.Token);

        Assert.True(prepared.IsCancelled);
        Assert.Null(prepared.Plan);
        Assert.Null(prepared.BlockedBy);
        Assert.Equal(TransactionSentences.Cancelled, prepared.Sentence);
        Assert.Empty(rig.Store.Reads);
        Assert.Empty(rig.Writer.Calls);
        Assert.Equal(WriteRecordOutcome.Cancelled, Assert.Single(rig.Records.ReadAll()).Outcome);
    }

    [Fact]
    public async Task Restore_RunsThroughTheSamePipeline_AndPutsTheOrderBack()
    {
        using TransactionRig rig = new();
        (ChangePlan plan, TransactionResult written) = await rig.PrepareAndCommit(Reorder(UsbNumber, WindowsOnPartition1));
        Assert.Equal(TransactionOutcome.Verified, written.Outcome);
        SnapshotSummary newest = Assert.IsType<SnapshotSummary>(rig.Snapshots.Newest(Whitelist.BootOrder.Key));
        Assert.Equal(written.SnapshotFolder, newest.Folder);

        PrepareResult restore = await rig.Prepare(new RestoreRequest(ChangeRequest.AppHost, newest.Folder));
        Assert.True(restore.Passed, restore.Sentence);
        ChangePlan restorePlan = Assert.IsType<ChangePlan>(restore.Plan);
        Assert.Equal(newest.Folder, restorePlan.RestoredFrom);
        Assert.Equal(plan.Before.Hex, restorePlan.After.Hex);
        Assert.Equal(ReorderFolder + "-restore", restorePlan.SnapshotFolderName);

        TransactionResult restored = await rig.Pipeline.CommitAsync(restorePlan, restorePlan.Confirm(BitLockerChoice.NotNeeded));

        Assert.Equal(TransactionOutcome.Verified, restored.Outcome);
        Assert.StartsWith("Boot order restored to Boot0002 (" + WindowsBootManagerDescription + "), Boot0003 (", restored.Sentence);
        Assert.Equal(plan.Before.Hex, Convert.ToHexString(rig.Store.Current(BootOrder)!.Data).ToLowerInvariant());
        Assert.True(restored.Record.IsRestore);
        Assert.Equal(newest.Folder, restored.Record.RestoredFrom);
        Assert.Equal(2, rig.Writer.Calls.Count);
    }

    [Fact]
    public async Task ABaselineThatDiffers_IsAWarning_NotARefusal()
    {
        using TransactionRig rig = new();
        VariableSet older = Store(Var(BootOrder, Concat(U16(UsbNumber), U16(WindowsOnPartition1))));
        BaselineExportResult exported = new BaselineExporter(_ => null).Export(new BaselineRequest(
            rig.BaselineRoot, "20260923-111550", older.All, FirmwareType.Uefi, PrivilegeState.Enabled, null, null, null, null, "test", PlanTime));
        Assert.Empty(exported.Errors);

        PrepareResult prepared = await rig.Prepare(Reorder(UsbNumber, WindowsOnPartition1));

        Assert.True(prepared.Passed, prepared.Sentence);
        string warning = "BootOrder differs from the baseline of 20260923-111550; the firmware or Windows changed it since.";
        Assert.Contains(prepared.Checks, check => check.Name == CheckNames.Baseline && check.IsWarning && check.Reason == warning);
        Assert.Contains(warning, prepared.Warnings);
        Assert.False(prepared.Plan!.Policy.BaselineMatches);
        Assert.Equal(exported.Directory, prepared.Plan.Policy.BaselineFolder);
    }

    // ---- one blocking case per rule, through the transaction ---------------------------------

    [Theory]
    [InlineData("V1", CheckNames.Format)]
    [InlineData("V2", CheckNames.Format)]
    [InlineData("V3", CheckNames.Format)]
    [InlineData("V4", CheckNames.EntriesExist)]
    [InlineData("V5", CheckNames.CurrentListIntact)]
    [InlineData("V6", CheckNames.Permutation)]
    [InlineData("V7", CheckNames.NoChange)]
    [InlineData("V9", CheckNames.Attributes)]
    [InlineData("V10", CheckNames.Attributes)]
    [InlineData("V11", CheckNames.BootNextTarget)]
    [InlineData("V12", CheckNames.BootNextTarget)]
    [InlineData("V13", CheckNames.OtherWindowsPath)]
    [InlineData("V14", CheckNames.BootNextTarget)]
    [InlineData("V15", CheckNames.Enumeration)]
    [InlineData("V15-inMemory", CheckNames.Enumeration)]
    [InlineData("V16", CheckNames.BootCurrent)]
    [InlineData("S4", CheckNames.Whitelist)]
    public async Task EveryValidationRuleThatRefuses_LeavesTheWriterUncalled(string rule, string blockedBy)
    {
        (VariableSet set, ChangeRequest request) = rule switch
        {
            "V1" => (Store(Var(BootOrder, new byte[] { 2, 0, 3 })), Reorder(UsbNumber, WindowsOnPartition1)),
            "V2" => (Store(Var(BootNext, BootList(UsbNumber, WindowsOnPartition1))), Clear()),
            "V3" => (StoreWithOrder(WindowsOnPartition1, MalformedNumber), Reorder(MalformedNumber, WindowsOnPartition1)),
            "V4" => (Store(), Next(MissingNumber)),
            "V5" => (StoreWithOrder(WindowsOnPartition1, MissingNumber), Reorder(MissingNumber, WindowsOnPartition1)),
            "V6" => (Store(), Reorder(WindowsOnPartition1, WindowsOnPartition2)),
            "V7" => (Store(), (ChangeRequest)Reorder(WindowsOnPartition1, UsbNumber)),
            "V9" => (Store(Var(BootOrder, BootList(WindowsOnPartition1, UsbNumber), BsRt)), Reorder(UsbNumber, WindowsOnPartition1)),
            "V10" => (Store(Var(BootOrder, BootList(WindowsOnPartition1, UsbNumber), NvBsRtTa)), Reorder(UsbNumber, WindowsOnPartition1)),
            "V11" => (Store(), Next(InactiveNumber)),
            "V12" => (Store(), Next(AppNumber)),
            "V13" => (Store(), Next(WindowsOnPartition2)),
            "V14" => (Store(), Clear()),
            "V16" => (Store().Without("BootCurrent"), Next(WindowsOnPartition1)),
            "S4" => (Store(), Check("Timeout")),
            _ => (Store(), Reorder(UsbNumber, WindowsOnPartition1)),
        };
        using TransactionRig rig = new(set, rule is "V13" or "V16" ? TransactionFakes.BitLockerOn() : null);
        if (rule == "V15")
        {
            rig.Store.Method = EnumerationMethod.KnownNamesFallback;
            rig.Store.EnumerationError = "NtEnumerateSystemEnvironmentValuesEx failed";
        }
        else if (rule == "V15-inMemory")
        {
            rig.Store.Method = EnumerationMethod.InMemory;
        }

        PrepareResult prepared = await rig.Prepare(request, rule is "V13" or "V16" ? BitLockerChoice.SuspendForOneRestart : null);

        Assert.Null(prepared.Plan);
        Assert.Equal(blockedBy, prepared.BlockedBy);
        Assert.Equal(WriteExitCodes.Blocked, prepared.ExitCode);
        Assert.Empty(rig.Writer.Calls);
        Assert.Empty(rig.Suspender.Calls);
        Assert.Empty(rig.TransactionFolders());
        Assert.False(prepared.Record.ApiCalled);
        if (rule == "V10")
        {
            Assert.Contains(prepared.Checks, check => check.Name == CheckNames.AuthenticatedWrite && !check.Passed);
        }
    }

    // ---- the confirmation, the snapshot and the drift reads -----------------------------------

    [Fact]
    public async Task AConfirmationWithAnotherBitLockerChoiceThanTheChecks_IsRefused()
    {
        using TransactionRig rig = new(bitLocker: TransactionFakes.BitLockerOn());
        PrepareResult prepared = await rig.Prepare(Reorder(UsbNumber, WindowsOnPartition1), BitLockerChoice.SuspendForOneRestart);
        ChangePlan plan = Assert.IsType<ChangePlan>(prepared.Plan);

        TransactionResult result = await rig.Pipeline.CommitAsync(plan, plan.Confirm(BitLockerChoice.KeepProtection));

        Assert.Equal(TransactionOutcome.Blocked, result.Outcome);
        Assert.Equal(CheckNames.Confirmation, result.BlockedBy);
        Assert.Equal(TransactionSentences.ChoiceDiffersFromChecks, result.Sentence);
        Assert.Empty(rig.Suspender.Calls);
        Assert.Empty(rig.Writer.Calls);
        Assert.Empty(rig.TransactionFolders());
    }

    [Fact]
    public async Task ADriftReadThatFails_IsRefused_BeforeBitLockerIsTouched()
    {
        using TransactionRig rig = new(bitLocker: TransactionFakes.BitLockerOn());
        PrepareResult prepared = await rig.Prepare(Reorder(UsbNumber, WindowsOnPartition1));
        ChangePlan plan = Assert.IsType<ChangePlan>(prepared.Plan);
        rig.Store.Failures[BootOrder] = ReadStatus.AccessDenied;

        TransactionResult result = await rig.Pipeline.CommitAsync(plan, plan.Confirm(BitLockerChoice.SuspendForOneRestart));

        Assert.Equal(TransactionOutcome.Blocked, result.Outcome);
        Assert.Equal(CheckNames.Stale, result.BlockedBy);
        Assert.Equal(CheckSentences.Format(TransactionSentences.DriftReadFailedFormat, BootOrder, ReadStatus.AccessDenied), result.Sentence);
        Assert.Empty(rig.Suspender.Calls);
        Assert.Empty(rig.Writer.Calls);
        Assert.Equal(WriteRecordOutcome.Blocked, ReadRecordFile(Assert.IsType<string>(result.SnapshotFolder))!.Outcome);
    }

    [Fact]
    public async Task ASnapshotThatDoesNotReadBackAsWritten_IsRefused_BeforeBitLockerIsTouched()
    {
        using TransactionRig rig = new(bitLocker: TransactionFakes.BitLockerOn());
        PrepareResult prepared = await rig.Prepare(Reorder(UsbNumber, WindowsOnPartition1));
        ChangePlan plan = Assert.IsType<ChangePlan>(prepared.Plan);

        // The set the checks read changes in memory under the transaction, so before\ is exported
        // from other bytes than the plan's: the read-back must see it.
        FirmwareVariable current = Assert.IsType<FirmwareVariable>(rig.Store.Current(BootOrder));
        current.Data[0] ^= 0xFF;

        TransactionResult result = await rig.Pipeline.CommitAsync(plan, plan.Confirm(BitLockerChoice.SuspendForOneRestart));

        Assert.Equal(TransactionOutcome.Blocked, result.Outcome);
        Assert.Equal(CheckNames.Snapshot, result.BlockedBy);
        string expected = CheckSentences.Format(
            TransactionSentences.SnapshotNotConfirmedFormat,
            ReorderFolder,
            CheckSentences.Format(SnapshotStore.HashMismatchFormat, BootOrder));
        Assert.Equal(expected, result.Sentence);
        Assert.Empty(rig.Suspender.Calls);
        Assert.Empty(rig.Writer.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnEntryTheRulesRead_ChangedAfterTheDryRun_IsStale(bool beforeTheSnapshot)
    {
        using TransactionRig rig = new(bitLocker: TransactionFakes.BitLockerOn());
        PrepareResult prepared = await rig.Prepare(Reorder(UsbNumber, WindowsOnPartition1));
        ChangePlan plan = Assert.IsType<ChangePlan>(prepared.Plan);
        Assert.Contains(plan.Dependencies, dependency => dependency.Name == "Boot0003");
        FirmwareVariable changed = Var("Boot0003", BuildLoadOption(1, "Another stick", PathOf(PciRoot(0), Pci(0x14, 0), Usb(4, 0))));
        if (beforeTheSnapshot)
        {
            rig.Store.Set(changed);
        }
        else
        {
            rig.Suspender.DuringCall = () => rig.Store.Set(changed);
        }

        TransactionResult result = await rig.Pipeline.CommitAsync(plan, plan.Confirm(BitLockerChoice.SuspendForOneRestart));

        Assert.Equal(TransactionOutcome.Blocked, result.Outcome);
        Assert.Equal(CheckNames.Stale, result.BlockedBy);
        Assert.StartsWith(CheckSentences.Format(CheckSentences.StaleReadFormat, "Boot0003"), result.Sentence, StringComparison.Ordinal);
        Assert.Empty(rig.Writer.Calls);
        Assert.Equal(beforeTheSnapshot ? 0 : 1, rig.Suspender.Calls.Count);
        Assert.Equal(!beforeTheSnapshot, result.BitLockerSuspended);
    }

    /// <summary>
    /// BootNext beside BootOrder (design 3.3, S12 and S14): the firmware starts a one-time boot
    /// before the order, so a BootNext set, changed or removed between the dry run and the call
    /// refuses a reorder. With BitLocker off no rule reads BootNext for a reorder, so only the drift
    /// check's own comparison can catch it; with BitLocker on the rules read it too (V13 refuses a
    /// reorder while one is set), and a BootNext set to the unit's second Windows Boot Manager
    /// after the dialog must never be followed by the call, before or after the suspend.
    /// </summary>
    [Theory]
    [InlineData(false, false, "set", true)]
    [InlineData(false, false, "set", false)]
    [InlineData(false, true, "changed", true)]
    [InlineData(false, true, "changed", false)]
    [InlineData(false, true, "removed", true)]
    [InlineData(false, true, "removed", false)]
    [InlineData(true, false, "set", true)]
    [InlineData(true, false, "set", false)]
    public async Task ABootNextChangedAfterTheDryRunOfAReorder_IsStale(bool bitLockerOn, bool storeHasBootNext, string mutation, bool atTheFirstDriftRead)
    {
        VariableSet set = storeHasBootNext ? Store(Var(BootNext, U16(WindowsOnPartition1))) : Store();
        using TransactionRig rig = new(set, bitLockerOn ? TransactionFakes.BitLockerOn() : null);
        PrepareResult prepared = await rig.Prepare(Reorder(UsbNumber, WindowsOnPartition1));
        ChangePlan plan = Assert.IsType<ChangePlan>(prepared.Plan);
        if (bitLockerOn)
        {
            Assert.Contains(plan.Dependencies, dependency => dependency.Name == BootNext && !dependency.Image.Present);
        }
        else
        {
            Assert.DoesNotContain(plan.Dependencies, dependency => dependency.Name == BootNext);
        }

        FirmwareVariable? replacement = mutation switch
        {
            "set" => Var(BootNext, U16(WindowsOnPartition2)),
            "changed" => Var(BootNext, U16(UsbNumber)),
            _ => null,
        };
        BitLockerChoice choice = bitLockerOn ? BitLockerChoice.SuspendForOneRestart : BitLockerChoice.NotNeeded;
        if (bitLockerOn && atTheFirstDriftRead)
        {
            rig.Store.Set(replacement!);
        }
        else if (bitLockerOn)
        {
            rig.Suspender.DuringCall = () => rig.Store.Set(replacement!);
        }
        else
        {
            // BootNext's reads after the dry run: the first drift read of S12, then the second of S14.
            rig.Store.ChangeBeforeRead(BootNext, atTheFirstDriftRead ? 1 : 2, replacement);
        }

        TransactionResult result = await rig.Pipeline.CommitAsync(plan, plan.Confirm(choice));

        Assert.Equal(TransactionOutcome.Blocked, result.Outcome);
        Assert.Equal(CheckNames.Stale, result.BlockedBy);
        Assert.StartsWith(CheckSentences.Format(CheckSentences.StaleReadFormat, BootNext), result.Sentence, StringComparison.Ordinal);
        Assert.Empty(rig.Writer.Calls);
        Assert.False(result.ApiCalled);
        bool suspended = bitLockerOn && !atTheFirstDriftRead;
        Assert.Equal(suspended ? 1 : 0, rig.Suspender.Calls.Count);
        Assert.Equal(suspended, result.BitLockerSuspended);
        Assert.Equal(suspended, result.Sentence.EndsWith(" " + CheckSentences.BitLockerLeftSuspended, StringComparison.Ordinal));
    }

    /// <summary>
    /// F7 through BootNext (design V13): a one-time boot to the unit's second Windows Boot Manager,
    /// set outside the tool, would take the next start before any entry of the order, and the
    /// one-restart suspend would be sealed to that path. The reorder is refused at the dry run,
    /// with nothing suspended and nothing written, whatever the one-time boot names.
    /// </summary>
    [Theory]
    [InlineData(WindowsOnPartition2)]
    [InlineData(UsbNumber)]
    public async Task AReorderWhileAOneTimeBootIsSet_IsRefused_BeforeAnySuspendOrWrite(ushort target)
    {
        using TransactionRig rig = new(Store(Var(BootNext, U16(target))), TransactionFakes.BitLockerOn());

        PrepareResult prepared = await rig.Prepare(Reorder(UsbNumber, WindowsOnPartition1), BitLockerChoice.SuspendForOneRestart);

        Assert.Null(prepared.Plan);
        Assert.Equal(CheckNames.OtherWindowsPath, prepared.BlockedBy);
        Assert.Equal(CheckSentences.Format(CheckSentences.PendingOneTimeBootFormat, KnownVariableNames.BootName(target)), prepared.Sentence);
        Assert.Empty(rig.Suspender.Calls);
        Assert.Empty(rig.Writer.Calls);
        Assert.False(prepared.Record.ApiCalled);
        Assert.Empty(rig.TransactionFolders());

        // With protection off the one-time boot seals nothing, and the reorder is judged as before.
        using TransactionRig off = new(Store(Var(BootNext, U16(target))));
        Assert.True((await off.Prepare(Reorder(UsbNumber, WindowsOnPartition1))).Passed);
    }

    // ---- BitLocker read again at S12 ----------------------------------------------------------

    [Theory]
    [InlineData("suspendedMeanwhile")]
    [InlineData("turnedOnMeanwhile")]
    public async Task ABitLockerStateOtherThanTheDryRuns_IsRefused_BeforeAnySuspend(string scenario)
    {
        bool onAtTheDryRun = scenario == "suspendedMeanwhile";
        using TransactionRig rig = new(bitLocker: onAtTheDryRun ? TransactionFakes.BitLockerOn() : TransactionFakes.BitLockerOff());
        PrepareResult prepared = await rig.Prepare(Reorder(UsbNumber, WindowsOnPartition1));
        ChangePlan plan = Assert.IsType<ChangePlan>(prepared.Plan);
        rig.BitLocker.Result = onAtTheDryRun ? SuspendedVolume() : TransactionFakes.BitLockerOn();

        TransactionResult result = await rig.Pipeline.CommitAsync(plan, plan.Confirm(onAtTheDryRun ? BitLockerChoice.SuspendForOneRestart : BitLockerChoice.NotNeeded));

        Assert.Equal(TransactionOutcome.Blocked, result.Outcome);
        Assert.Equal(CheckNames.BitLocker, result.BlockedBy);
        string expected = onAtTheDryRun
            ? "BitLocker's state changed after the dry run (it was on, it is now suspended); check the change again. Nothing was written."
            : "BitLocker's state changed after the dry run (it was off, it is now on); check the change again. Nothing was written.";
        Assert.Equal(expected, result.Sentence);
        Assert.Empty(rig.Suspender.Calls);
        Assert.Empty(rig.Writer.Calls);
        Assert.Equal(onAtTheDryRun ? BitLockerState.Suspended : BitLockerState.On, result.Record.BitLocker!.State);
    }

    [Fact]
    public async Task AClear_DoesNotReadBitLockerAgain()
    {
        using TransactionRig rig = new(Store(Var(BootNext, U16(WindowsOnPartition1))), TransactionFakes.BitLockerOn());
        PrepareResult prepared = await rig.Prepare(Clear());
        ChangePlan plan = Assert.IsType<ChangePlan>(prepared.Plan);
        int reads = rig.BitLocker.Calls;

        TransactionResult result = await rig.Pipeline.CommitAsync(plan, plan.Confirm(BitLockerChoice.NotNeeded));

        Assert.Equal(TransactionOutcome.Verified, result.Outcome);
        Assert.Equal(reads, rig.BitLocker.Calls);
    }

    // ---- the write budget and the commit lock -------------------------------------------------

    [Fact]
    public async Task TheBudget_IsCountedAgainUnderTheCommitLock_BeforeTheSnapshot()
    {
        using TransactionRig rig = new();
        rig.AddPastWrites(6, TransactionRig.StartOfWindows.AddMinutes(10));
        PrepareResult prepared = await rig.Prepare(Reorder(UsbNumber, WindowsOnPartition1));
        ChangePlan plan = Assert.IsType<ChangePlan>(prepared.Plan);
        Assert.Equal(6, prepared.Record.WritesThisStart);

        // Another plan, or the other host, wrote in the meantime.
        rig.AddPastWrites(1, TransactionRig.StartOfWindows.AddMinutes(20));
        TransactionResult result = await rig.Pipeline.CommitAsync(plan, plan.Confirm(BitLockerChoice.NotNeeded));

        Assert.Equal(TransactionOutcome.Blocked, result.Outcome);
        Assert.Equal(CheckNames.WriteBudget, result.BlockedBy);
        Assert.Equal("This start has already made 7 changes to the firmware. Restart Windows before more changes.", result.Sentence);
        Assert.Equal(7, result.Record.WritesThisStart);
        Assert.Empty(rig.Writer.Calls);
        Assert.Empty(rig.TransactionFolders());
    }

    [Fact]
    public async Task AWriteLogThatCannotBeRead_RefusesTheDryRun()
    {
        using TransactionRig rig = new();
        rig.AddPastWrites(1, TransactionRig.StartOfWindows.AddMinutes(10));

        PrepareResult prepared;
        using (new FileStream(rig.Records.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            prepared = await rig.Prepare(Reorder(UsbNumber, WindowsOnPartition1));
        }

        Assert.Null(prepared.Plan);
        Assert.Equal(CheckNames.WriteBudget, prepared.BlockedBy);
        Assert.StartsWith("The list of changes already made (write-log.jsonl) could not be read (the file exists but could not be read: ", prepared.Sentence, StringComparison.Ordinal);
        Assert.EndsWith("), so the changes of this start cannot be counted; nothing is written.", prepared.Sentence, StringComparison.Ordinal);
        Assert.Empty(rig.Writer.Calls);
    }

    [Fact]
    public async Task ACommitWhileAnotherHoldsTheLock_IsRefused_AndTheLockIsFreeAfterACommit()
    {
        using TransactionRig rig = new();
        PrepareResult prepared = await rig.Prepare(Reorder(UsbNumber, WindowsOnPartition1));
        ChangePlan plan = Assert.IsType<ChangePlan>(prepared.Plan);
        WriteRecordLog otherProcess = new(rig.WritesDirectory, _ => null);
        using ManualResetEventSlim taken = new();
        using ManualResetEventSlim release = new();
        Thread holder = new(() =>
        {
            using IDisposable? held = otherProcess.TryEnterCommit(out _);
            taken.Set();
            release.Wait();
        });
        holder.Start();
        taken.Wait();

        TransactionResult refused = await rig.Pipeline.CommitAsync(plan, plan.Confirm(BitLockerChoice.NotNeeded));
        release.Set();
        holder.Join();

        Assert.Equal(TransactionOutcome.Blocked, refused.Outcome);
        Assert.Equal(CheckNames.WriteBudget, refused.BlockedBy);
        Assert.Equal(CheckSentences.AnotherChangeBeingWritten, refused.Sentence);
        Assert.Empty(rig.Writer.Calls);
        Assert.Empty(rig.TransactionFolders());

        // A commit that ran to its end released the lock: the next one takes it.
        (_, TransactionResult written) = await rig.PrepareAndCommit(Reorder(UsbNumber, WindowsOnPartition1));
        Assert.Equal(TransactionOutcome.Verified, written.Outcome);
        using IDisposable? after = otherProcess.TryEnterCommit(out string? problem);
        Assert.NotNull(after);
        Assert.Null(problem);
    }

    // ---- after a call that failed -------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AFailedCallWhoseValueIsNeitherOldNorNew_IsUnverified_WithoutClaimingTheChangeWasWritten(bool readBackFails)
    {
        using TransactionRig rig = new();
        rig.Writer.Win32Error = 19;
        rig.Writer.NtStatus = 0xC00000A2;
        rig.Writer.ChangesTheStore = false;
        rig.Writer.DuringCall = _ =>
        {
            rig.Store.Set(Var(BootOrder, Concat(U16(UsbNumber), U16(UsbNumber))));
            if (readBackFails)
            {
                rig.Store.Failures[BootOrder] = ReadStatus.Failed;
            }
        };

        (_, TransactionResult result) = await rig.PrepareAndCommit(Reorder(UsbNumber, WindowsOnPartition1));

        Assert.Equal(TransactionOutcome.Unverified, result.Outcome);
        Assert.Equal(WriteExitCodes.Unverified, result.ExitCode);
        string reason = readBackFails
            ? CheckSentences.Format(TransactionSentences.ReadBackFailedFormat, ReadStatus.Failed)
            : TransactionSentences.NeitherValue;
        Assert.Equal(CheckSentences.Format(TransactionSentences.FailedUnverifiedFormat, 19, reason, ReorderFolder), result.Sentence);
        Assert.DoesNotContain("was written", result.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailureAfterTheCall_GivesUnverified_NeverAnException_AndTheLockIsReleased()
    {
        using TransactionRig rig = new();
        rig.Writer.DuringCall = _ => rig.ClockFailure = new InvalidOperationException("the clock stopped");

        (ChangePlan plan, TransactionResult result) = await rig.PrepareAndCommit(Reorder(UsbNumber, WindowsOnPartition1));
        rig.ClockFailure = null;

        Assert.Equal(TransactionOutcome.Unverified, result.Outcome);
        Assert.Equal(WriteExitCodes.Unverified, result.ExitCode);
        Assert.Single(rig.Writer.Calls);
        Assert.True(result.ApiCalled);
        string reason = "InvalidOperationException: the clock stopped";
        Assert.Equal(
            CheckSentences.Format(TransactionSentences.UnverifiedFormat, reason, ReorderFolder) + " " + CheckSentences.Format(CheckSentences.RecordNotSavedUnverifiedFormat, reason),
            result.Sentence);
        WriteRecord line = rig.Records.ReadAll()[^1];
        Assert.Equal(WriteRecordOutcome.Unverified, line.Outcome);
        Assert.True(line.ApiCalled);
        Assert.Equal(plan.SnapshotFolderName, line.TransactionId);
        using IDisposable? after = rig.Records.TryEnterCommit(out _);
        Assert.NotNull(after);
    }

    /// <summary>
    /// The last resort after the call, after a confirmed suspend: the sentence says BitLocker stays
    /// suspended until the next restart, before the record-not-saved part, as every other outcome
    /// after the call does; and the line in write-log.jsonl keeps what was already known, the
    /// firmware's answer, the verification read, the checks and the BitLocker facts.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AFailureAfterTheCall_AfterASuspend_SaysBitLockerStaysSuspended_AndKeepsWhatWasKnownInTheRecord(bool callFails)
    {
        using TransactionRig rig = new(bitLocker: TransactionFakes.BitLockerOn());
        if (callFails)
        {
            rig.Writer.Win32Error = 19;
            rig.Writer.NtStatus = 0xC00000A2;
        }

        rig.Writer.DuringCall = _ => rig.ClockFailure = new InvalidOperationException("the clock stopped");

        (ChangePlan plan, TransactionResult result) = await rig.PrepareAndCommit(Reorder(UsbNumber, WindowsOnPartition1), BitLockerChoice.SuspendForOneRestart);
        rig.ClockFailure = null;

        Assert.Equal(TransactionOutcome.Unverified, result.Outcome);
        Assert.Equal(WriteExitCodes.Unverified, result.ExitCode);
        Assert.Single(rig.Writer.Calls);
        Assert.Single(rig.Suspender.Calls);
        Assert.True(result.BitLockerSuspended);
        string reason = "InvalidOperationException: the clock stopped";
        string expected = callFails
            ? CheckSentences.Format(TransactionSentences.FailedUnverifiedFormat, 19, reason, ReorderFolder)
                + " " + CheckSentences.BitLockerLeftSuspended
                + " " + CheckSentences.Format(CheckSentences.RecordNotSavedUnknownFormat, reason)
            : CheckSentences.Format(TransactionSentences.UnverifiedFormat, reason, ReorderFolder)
                + " " + CheckSentences.BitLockerLeftSuspended
                + " " + CheckSentences.Format(CheckSentences.RecordNotSavedUnverifiedFormat, reason);
        Assert.Equal(expected, result.Sentence);

        WriteRecord line = rig.Records.ReadAll()[^1];
        Assert.Equal(result.Record.ToJsonLine(), line.ToJsonLine());
        Assert.Equal(WriteRecordOutcome.Unverified, line.Outcome);
        Assert.Equal(plan.SnapshotFolderName, line.TransactionId);
        Assert.True(line.ApiCalled);
        WriteRecordApi api = Assert.IsType<WriteRecordApi>(line.Api);
        Assert.Equal(!callFails, api.Succeeded);
        Assert.Equal(callFails ? 19 : 0, api.Win32Error);
        WriteRecordVerification verification = Assert.IsType<WriteRecordVerification>(line.Verification);
        Assert.Equal(ReadStatus.Success, verification.ReadStatus);
        Assert.Equal(verification.Sha256, line.AfterSha256);
        Assert.NotEmpty(line.Checks);
        Assert.Equal(BitLockerChoice.SuspendForOneRestart, line.BitLockerChoice);
        WriteRecordBitLocker bitLocker = Assert.IsType<WriteRecordBitLocker>(line.BitLocker);
        Assert.True(bitLocker.SuspendOffered);
        Assert.True(bitLocker.SuspendChosen);
        Assert.Equal("0", bitLocker.SuspendResult);
        Assert.Equal(0, bitLocker.ProtectionStatusAfterSuspend);

        // One line for the transaction after its intent, and the write counted once.
        Assert.Equal(new[] { WriteRecordOutcome.Prepared, WriteRecordOutcome.Pending, WriteRecordOutcome.Unverified }, rig.Records.ReadAll().Select(record => record.Outcome));
        Assert.Equal(new WriteBudgetCount(1, null), rig.Records.CountSinceStart(TransactionRig.StartOfWindows));
    }

    /// <summary>
    /// A writer that throws (the real one passes on what its seam throws): the transaction keeps
    /// the one call, classes it FirmwareError with the exception's text and no NTSTATUS, lets the
    /// one verification read decide the outcome, records it in record.json and write-log.jsonl,
    /// releases the commit lock, and says the call ended unexpectedly, never that the firmware
    /// reported an error.
    /// </summary>
    [Theory]
    [InlineData("unchanged", TransactionOutcome.ApiRefused, WriteExitCodes.ApiRefused)]
    [InlineData("changed", TransactionOutcome.ChangedDespiteError, WriteExitCodes.Unverified)]
    [InlineData("unreadable", TransactionOutcome.Unverified, WriteExitCodes.Unverified)]
    public async Task AWriterThatThrows_IsOneCall_AndTheReadBackDecidesTheOutcome(string scenario, TransactionOutcome expected, int exitCode)
    {
        using TransactionRig rig = new();
        rig.Writer.Throws = new InvalidOperationException("the seam failed");
        rig.Writer.ChangesTheStore = scenario == "changed";
        int readsAtCall = 0;
        rig.Writer.DuringCall = _ =>
        {
            readsAtCall = rig.Store.ReadsOf(BootOrder);
            if (scenario == "unreadable")
            {
                rig.Store.Failures[BootOrder] = ReadStatus.Failed;
            }
        };

        (ChangePlan plan, TransactionResult result) = await rig.PrepareAndCommit(Reorder(UsbNumber, WindowsOnPartition1));

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(exitCode, result.ExitCode);
        Assert.Single(rig.Writer.Calls);
        Assert.Equal(readsAtCall + 1, rig.Store.ReadsOf(BootOrder));
        Assert.True(result.ApiCalled);
        WriteOutcome api = Assert.IsType<WriteOutcome>(result.Api);
        Assert.False(api.Succeeded);
        Assert.Equal(WriteStatus.FirmwareError, api.Status);
        Assert.Equal(0, api.Win32Error);
        Assert.Null(api.NtStatus);
        Assert.Equal(FirmwareVariableWriter.NtStatusNotObserved, api.NtStatusSource);
        Assert.Equal(CheckSentences.Format(TransactionSentences.WriterEndedFormat, nameof(InvalidOperationException), "the seam failed"), api.Win32Text);

        string ended = "InvalidOperationException: the seam failed";
        string sentence = scenario switch
        {
            "unchanged" => CheckSentences.Format(TransactionSentences.WriterEndedUnchangedFormat, ended, ReorderFolder),
            "changed" => CheckSentences.Format(TransactionSentences.WriterEndedChangedFormat, ended, ReorderFolder),
            _ => CheckSentences.Format(TransactionSentences.WriterEndedUnverifiedFormat, ended, CheckSentences.Format(TransactionSentences.ReadBackFailedFormat, ReadStatus.Failed), ReorderFolder),
        };
        Assert.Equal(sentence, result.Sentence);
        Assert.DoesNotContain("firmware reported", result.Sentence, StringComparison.Ordinal);

        string folder = Assert.IsType<string>(result.SnapshotFolder);
        WriteRecord onDisk = Assert.IsType<WriteRecord>(ReadRecordFile(folder));
        Assert.Equal(result.Record.Outcome, onDisk.Outcome);
        Assert.NotEqual(WriteRecordOutcome.Pending, onDisk.Outcome);
        WriteRecord line = rig.Records.ReadAll()[^1];
        Assert.Equal(result.Record.ToJsonLine(), line.ToJsonLine());
        Assert.True(line.ApiCalled);
        Assert.Equal(plan.SnapshotFolderName, line.TransactionId);
        Assert.Equal(api.Win32Text, line.Api!.Win32Text);
        using IDisposable? after = rig.Records.TryEnterCommit(out string? problem);
        Assert.NotNull(after);
        Assert.Null(problem);
    }

    private static BitLockerResult SuspendedVolume() => new(
        new[] { new BitLockerVolume(BitLockerPreflight.OsDriveLetter, 0, "Off", 1, "Encrypted", new[] { "TPM", "Numerical password" }, new[] { 0, 2, 4, 11 }, "PCR 0,2,4,11 (legacy)") },
        Array.Empty<string>());

    /// <summary>The NTSTATUS Windows turns into each Win32 code of the refusal theory.</summary>
    private static uint StatusBehind(int win32Error) => win32Error switch
    {
        19 => 0xC00000A2,
        5 => 0xC0000022,
        50 => 0xC00000BB,
        87 => 0xC000000D,
        203 => 0xC0000100,
        1470 => 0xC0000454,
        _ => 0xC0000001,
    };

    private static (VariableSet Set, ChangeRequest Request) Operation(string operation)
    {
        return operation switch
        {
            "reorder" => (Store(), Reorder(UsbNumber, WindowsOnPartition1)),
            "bootnext" => (Store(), Next(WindowsOnPartition1)),
            "clear" => (Store(Var(BootNext, U16(WindowsOnPartition1))), Clear()),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "not an operation of this test"),
        };
    }

    private static string ExpectedVerifiedSentence(string operation, string folderName)
    {
        string usb = "Boot0003 (" + UsbStickDescription + ")";
        string windows = "Boot0002 (" + WindowsBootManagerDescription + ")";
        return operation switch
        {
            "reorder" => "Boot order changed to " + usb + ", " + windows + ". Restart to use the new order. Snapshot " + folderName + ".",
            "bootnext" => "One-time boot: " + windows + " will start once at the next restart. Snapshot " + folderName + ".",
            _ => "One-time boot choice removed. Snapshot " + folderName + ".",
        };
    }

    private static VariableSet ThreeEntries() => Store(Var(BootOrder, Concat(U16(WindowsOnPartition1), U16(UsbNumber), U16(WindowsCopyNumber))));

    private static WriteRecord? ReadRecordFile(string folder) => WriteRecord.TryParse(File.ReadAllText(Path.Combine(folder, SnapshotStore.RecordFileName)));
}
