// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using System.Text.RegularExpressions;
using UefiAccess.Cli;
using UefiAccess.Cli.Commands;
using UefiAccess.Cli.Services;
using UefiAccess.Core.Privileges;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Tests.Safety;
using UefiAccess.Core.Variables;
using Xunit;
using static UefiAccess.Core.Tests.CliTestHarness;
using static UefiAccess.Core.Tests.Fixtures.LoadOptionFixtures;
using static UefiAccess.Core.Tests.Safety.SafetyFixtures;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The command-line write harness run through <see cref="Program.Run"/>
/// in process, over <see cref="CliTestHarness.WriteRig"/>: the transaction fakes (a scripted
/// store shaped like the unit, a scripted writer and a fake suspender that never reach native
/// code or WMI), fixed inspectors, and the snapshot root, write-log.jsonl and the baseline root
/// under the test's own temporary folder. Every row of the failure-case matrix of section 4.3 that
/// has a double runs here (FC-01 to FC-27, FC-30 to FC-33; FC-33 is another OptiPlex 7020 on A18,
/// the tester's case, refused at expectedUnit), with the exit code, the check that
/// refused, the JSON document and the number of writer calls; then the verified writes, check,
/// log and snapshots, the usage errors, the Ctrl+C rule and the composition of the real writer.
/// FC-28 and FC-29 are GUI rows and belong to the application's tests. Nothing here can reach
/// the firmware, BitLocker or ProgramData.
/// </summary>
public sealed class CliWriteCommandTests
{
    private const string BootOrder = "BootOrder";
    private const string BootNext = "BootNext";
    private const string NewOrder = "Boot0003,Boot0002";
    private const string CurrentOrder = "Boot0002,Boot0003";
    private const string CurrentOrderHex = "02000300";
    private const string NewOrderHex = "03000200";
    private const string ImageSecurityGuidText = "d719b2cb-3d3a-4596-a3bc-dad00e67656f";

    /// <summary>The real writer composed over the Windows write seam, as CliPlatform.CreateWindows must write it once.</summary>
    private const string RealWriterConstruction = @"\bnew\s+FirmwareVariableWriter\s*\(\s*WindowsNativeFirmwareWriteApi\s*\.\s*Instance\b";

    /// <summary>The real suspender, as CliPlatform.CreateWindows must construct it once.</summary>
    private const string RealSuspenderConstruction = @"\bnew\s+BitLockerSuspender\s*\(";

    /// <summary>The commit's Ctrl+C handler put on Console.CancelKeyPress (ignored from S12 to S17).</summary>
    private const string CancelKeySubscription = @"\bConsole\s*\.\s*CancelKeyPress\s*\+=\s*IgnoreCancelKey\s*;";

    /// <summary>The same handler taken off Console.CancelKeyPress.</summary>
    private const string CancelKeyRemoval = @"\bConsole\s*\.\s*CancelKeyPress\s*-=\s*IgnoreCancelKey\s*;";

    /// <summary>Any mention of Console.CancelKeyPress in code.</summary>
    private const string CancelKeyEvent = @"\bCancelKeyPress\b";

    /// <summary>A call of the pipeline's commit (stages S11 to S17).</summary>
    private const string CommitCall = @"\bCommitAsync\s*\(";

    /// <summary>The handler's cancellation of the key, on its event arguments.</summary>
    private const string KeyCancelled = @"\b\w+\s*\.\s*Cancel\s*=\s*true\s*;";

    private static readonly string Windows = "Boot0002 (" + WindowsBootManagerDescription + ")";
    private static readonly string Usb = "Boot0003 (" + UsbStickDescription + ")";

    // ---- the verified writes ---------------------------------------------------------------

    [Fact]
    public void Reorder_WithConfirm_WritesOnceAfterTheSnapshot_VerifiesAndRecords()
    {
        using WriteRig rig = new();
        int handlersDuringCall = -1;
        rig.Writer.DuringCall = _ => handlersDuringCall = WriteCommand.CancelKeyHandlersInstalled;

        CliRun run = rig.Run("write", "bootorder", "--order", NewOrder, "--confirm", "--json");

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        WritePlan call = Assert.Single(rig.Writer.Calls);
        Assert.Equal(BootOrder, call.Name);
        Assert.Equal(Global, call.Guid);
        Assert.Equal(0x7u, call.Attributes);
        Assert.Equal(NewOrderHex, Hex(call.Bytes));
        Assert.True(rig.Writer.SnapshotReadyAtCall);
        Assert.Empty(rig.Suspender.Calls);

        // Ctrl+C is ignored while the commit runs, and the handler is gone afterwards. The counter
        // is bumped beside the subscription; the subscription itself is pinned by
        // CommitIgnoringCtrlC_SubscribesTheHandlerBeforeTheCommit_AndRemovesItInFinally, and the
        // handler's own work by CliCtrlCHandlerTests.
        Assert.Equal(1, handlersDuringCall);
        Assert.Equal(0, WriteCommand.CancelKeyHandlersInstalled);
        Assert.Contains(WriteCommand.CommitStarting, run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(WriteCommand.DryRunSentence, run.Error, StringComparison.Ordinal);

        JsonElement document = ParseSingleDocument(run.Output);
        Assert.Equal(WriteCommand.CommandName, document.GetProperty("command").GetString());
        Assert.Equal(nameof(ChangeOperation.Reorder), document.GetProperty("operation").GetString());
        Assert.True(document.GetProperty("ok").GetBoolean());
        Assert.Equal(ExitCodes.Success, document.GetProperty("exitCode").GetInt32());
        Assert.False(document.GetProperty("dryRun").GetBoolean());
        Assert.True(document.GetProperty("confirmed").GetBoolean());
        Assert.Equal(NewOrder, document.GetProperty("requested").GetString());
        Assert.Equal(Whitelist.BootOrder.Key, document.GetProperty("variable").GetProperty("key").GetString());
        Assert.Equal(CurrentOrderHex, document.GetProperty("before").GetProperty("hex").GetString());
        Assert.Equal(NewOrderHex, document.GetProperty("proposed").GetProperty("hex").GetString());
        Assert.Equal(NewOrderHex, document.GetProperty("after").GetProperty("hex").GetString());
        Assert.Equal(new[] { "Boot0003", "Boot0002" }, EntryNames(document.GetProperty("proposed")));
        Assert.Equal(UsbStickDescription, document.GetProperty("proposed").GetProperty("entries")[0].GetProperty("description").GetString());
        Assert.Equal(2, document.GetProperty("diff").GetArrayLength());
        Assert.True(document.GetProperty("apiCalled").GetBoolean());
        Assert.True(document.GetProperty("api").GetProperty("succeeded").GetBoolean());
        Assert.True(document.GetProperty("verification").GetProperty("matchesAfter").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("blockedBy").ValueKind);

        string transactionId = Assert.IsType<string>(document.GetProperty("transactionId").GetString());
        Assert.EndsWith("-reorder", transactionId, StringComparison.Ordinal);
        string folder = Assert.IsType<string>(document.GetProperty("snapshotFolder").GetString());
        Assert.Equal(Path.Combine(rig.SnapshotRoot, transactionId), folder);
        Assert.Equal(folder, Assert.Single(rig.TransactionFolders()));
        Assert.Equal(WriteRecordOutcome.Verified, RecordFile(folder).Outcome);
        Assert.Equal(
            CheckSentences.Format(TransactionSentences.VerifiedReorderFormat, Usb + ", " + Windows, transactionId),
            document.GetProperty("sentence").GetString());

        // In write-log.jsonl: the dry run's line, the commit's Pending intent line (S12, appended
        // before the snapshot and the call) and the commit's final line; the document embeds the last.
        IReadOnlyList<WriteRecord> records = rig.Records();
        Assert.Equal(3, records.Count);
        Assert.Equal(WriteRecordOutcome.Prepared, records[0].Outcome);
        Assert.Equal(WriteRecordOutcome.Pending, records[1].Outcome);
        Assert.False(records[1].ApiCalled);
        Assert.Equal(transactionId, records[1].TransactionId);
        Assert.Equal(WriteRecordOutcome.Verified, records[^1].Outcome);
        Assert.True(records[^1].ApiCalled);
        Assert.Equal(nameof(WriteRecordOutcome.Verified), document.GetProperty("record").GetProperty("outcome").GetString());
        Assert.Equal(records[^1].TransactionId, document.GetProperty("record").GetProperty("transactionId").GetString());
        Assert.Equal(NewOrderHex, Hex(Current(rig, BootOrder)));
    }

    [Fact]
    public void BootNext_WithConfirm_WritesOnce_AndVerifies()
    {
        using WriteRig rig = new();

        CliRun run = rig.Run("write", "bootnext", "Boot0002", "--confirm", "--json");

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        WritePlan call = Assert.Single(rig.Writer.Calls);
        Assert.Equal(BootNext, call.Name);
        Assert.Equal("0200", Hex(call.Bytes));
        JsonElement document = ParseSingleDocument(run.Output);
        Assert.Equal(nameof(ChangeOperation.SetBootNext), document.GetProperty("operation").GetString());
        Assert.False(document.GetProperty("before").GetProperty("present").GetBoolean());
        Assert.Equal("0200", document.GetProperty("after").GetProperty("hex").GetString());
        string transactionId = Assert.IsType<string>(document.GetProperty("transactionId").GetString());
        Assert.EndsWith("-bootnext", transactionId, StringComparison.Ordinal);
        Assert.Equal(CheckSentences.Format(TransactionSentences.VerifiedBootNextFormat, Windows, transactionId), document.GetProperty("sentence").GetString());
        Assert.Equal("0200", Hex(Current(rig, BootNext)));
        Assert.Equal(Path.Combine(rig.SnapshotRoot, transactionId), Assert.Single(rig.TransactionFolders()));
        Assert.Equal(WriteRecordOutcome.Verified, rig.Records()[^1].Outcome);
        Assert.Equal(0, WriteCommand.CancelKeyHandlersInstalled);
    }

    [Fact]
    public void Clear_WithConfirm_RemovesTheOneTimeBoot()
    {
        using WriteRig rig = new(Store(Var(BootNext, U16(WindowsOnPartition1))));

        CliRun run = rig.Run("write", "bootnext", "--clear", "--confirm", "--json");

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        WritePlan call = Assert.Single(rig.Writer.Calls);
        Assert.Equal(BootNext, call.Name);
        Assert.Empty(call.Bytes);
        JsonElement document = ParseSingleDocument(run.Output);
        Assert.Equal(nameof(ChangeOperation.ClearBootNext), document.GetProperty("operation").GetString());
        Assert.Equal(WriteCommand.ClearRequested, document.GetProperty("requested").GetString());
        Assert.False(document.GetProperty("proposed").GetProperty("present").GetBoolean());
        Assert.False(document.GetProperty("after").GetProperty("present").GetBoolean());
        string transactionId = Assert.IsType<string>(document.GetProperty("transactionId").GetString());
        Assert.Equal(CheckSentences.Format(TransactionSentences.VerifiedClearFormat, transactionId), document.GetProperty("sentence").GetString());
        Assert.Null(rig.Store.Current(BootNext));
        Assert.EndsWith("-bootnext-clear", transactionId, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(rig.SnapshotRoot, transactionId), Assert.Single(rig.TransactionFolders()));
        Assert.Equal(WriteRecordOutcome.Verified, rig.Records()[^1].Outcome);
    }

    [Fact]
    public void Restore_WithConfirm_PutsThePreviousOrderBack_ThroughTheSamePipeline()
    {
        using WriteRig rig = new();
        CliRun written = rig.Run("write", "bootorder", "--order", NewOrder, "--confirm", "--json");
        Assert.Equal(ExitCodes.Success, written.ExitCode);
        string folder = Assert.IsType<string>(ParseSingleDocument(written.Output).GetProperty("snapshotFolder").GetString());

        CliRun run = rig.Run("write", "restore", Path.GetFileName(folder), "--confirm", "--json");

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Equal(2, rig.Writer.Calls.Count);
        Assert.Equal(CurrentOrderHex, Hex(rig.Writer.Calls[1].Bytes));
        Assert.Equal(CurrentOrderHex, Hex(Current(rig, BootOrder)));
        JsonElement document = ParseSingleDocument(run.Output);
        Assert.Equal(nameof(ChangeOperation.Reorder), document.GetProperty("operation").GetString());
        Assert.Equal(Path.GetFileName(folder), document.GetProperty("requested").GetString());
        string transactionId = Assert.IsType<string>(document.GetProperty("transactionId").GetString());
        Assert.EndsWith("-reorder-restore", transactionId, StringComparison.Ordinal);
        JsonElement record = document.GetProperty("record");
        Assert.True(record.GetProperty("isRestore").GetBoolean());
        Assert.Equal(folder, record.GetProperty("restoredFrom").GetString());
        Assert.Equal(CheckSentences.Format(TransactionSentences.RestoredReorderFormat, Windows + ", " + Usb, transactionId), document.GetProperty("sentence").GetString());
        Assert.Equal(2, rig.TransactionFolders().Length);
        Assert.True(Directory.Exists(Path.Combine(rig.SnapshotRoot, transactionId)));
        WriteRecord line = rig.Records()[^1];
        Assert.Equal(WriteRecordOutcome.Verified, line.Outcome);
        Assert.True(line.IsRestore);
        Assert.Equal(folder, line.RestoredFrom);
    }

    [Fact]
    public void SuspendChosen_SuspendsOnceAfterTheSnapshot_WritesOnce_AndSaysSo()
    {
        using WriteRig rig = new(bitLocker: TransactionFakes.BitLockerOn());

        CliRun run = rig.Run("write", "bootnext", "Boot0002", "--confirm", "--bitlocker", "suspend", "--json");

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Single(rig.Suspender.Calls);
        Assert.True(rig.Suspender.SnapshotReadyAtCall);
        Assert.Single(rig.Writer.Calls);
        JsonElement document = ParseSingleDocument(run.Output);
        JsonElement bitLocker = document.GetProperty("bitLocker");
        Assert.Equal(nameof(BitLockerChoice.SuspendForOneRestart), bitLocker.GetProperty("choice").GetString());
        Assert.True(bitLocker.GetProperty("suspendOffered").GetBoolean());
        Assert.True(bitLocker.GetProperty("suspendedForOneRestart").GetBoolean());
        Assert.Equal("0", document.GetProperty("record").GetProperty("bitLocker").GetProperty("suspendResult").GetString());
        Assert.EndsWith(" " + TransactionSentences.SuspendedForOneRestart, document.GetProperty("sentence").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(WriteCommand.BitLockerHint, run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ContinueChosen_WritesWithProtectionOn_WithoutASuspend()
    {
        using WriteRig rig = new(bitLocker: TransactionFakes.BitLockerOn());

        CliRun run = rig.Run("write", "bootnext", "Boot0002", "--confirm", "--bitlocker", "continue", "--json");

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Empty(rig.Suspender.Calls);
        Assert.Single(rig.Writer.Calls);
        JsonElement document = ParseSingleDocument(run.Output);
        Assert.Equal(nameof(BitLockerChoice.KeepProtection), document.GetProperty("record").GetProperty("bitLockerChoice").GetString());
        Assert.False(document.GetProperty("bitLocker").GetProperty("suspendedForOneRestart").GetBoolean());
    }

    [Fact]
    public void FirstWriteDrill_AllowNoChange_WritesTheOrderAsItIs()
    {
        // Hardware step H3: the identity of the list, written once, with protection on.
        using WriteRig rig = new(bitLocker: TransactionFakes.BitLockerOn());

        CliRun run = rig.Run("write", "bootorder", "--order", CurrentOrder, "--allow-no-change", "--confirm", "--bitlocker", "continue", "--json");

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Equal(CurrentOrderHex, Hex(Assert.Single(rig.Writer.Calls).Bytes));
        JsonElement document = ParseSingleDocument(run.Output);
        Assert.Contains(CheckSentences.OrderUnchangedAllowed, Warnings(document));
        Assert.Equal(CurrentOrderHex, document.GetProperty("after").GetProperty("hex").GetString());
    }

    // ---- without --confirm nothing is written ------------------------------------------------

    [Theory]
    [InlineData(false, "write", "bootorder", "--order", NewOrder, "--json")]
    [InlineData(false, "write", "bootorder", "--order", NewOrder, "--dry-run", "--json")]
    [InlineData(false, "write", "bootnext", "Boot0002", "--json")]
    [InlineData(true, "write", "bootnext", "--clear", "--json")]
    [InlineData(false, "write", "bootorder", "--order", NewOrder, "--bitlocker", "continue", "--json")]
    public void WithoutConfirm_EveryWriteCommandIsADryRun_AndMakesNoWriterCall(bool bootNextSet, params string[] args)
    {
        using WriteRig rig = new(bootNextSet ? Store(Var(BootNext, U16(WindowsOnPartition1))) : null);

        CliRun run = rig.Run(args);

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Empty(rig.Writer.Calls);
        Assert.Empty(rig.Suspender.Calls);
        Assert.Empty(rig.TransactionFolders());
        Assert.True(Directory.Exists(rig.SnapshotRoot));
        Assert.EndsWith(WriteCommand.DryRunSentence + "\n", run.Error, StringComparison.Ordinal);
        JsonElement document = ParseSingleDocument(run.Output);
        Assert.True(document.GetProperty("dryRun").GetBoolean());
        Assert.False(document.GetProperty("confirmed").GetBoolean());
        Assert.False(document.GetProperty("apiCalled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("after").ValueKind);
        Assert.Equal(TransactionSentences.ChecksPassed, document.GetProperty("sentence").GetString());
        WriteRecord line = Assert.Single(rig.Records());
        Assert.Equal(WriteRecordOutcome.Prepared, line.Outcome);
        Assert.False(line.ApiCalled);
    }

    [Fact]
    public void Restore_WithoutConfirm_IsADryRun_AndMakesNoWriterCall()
    {
        using WriteRig rig = new();
        CliRun written = rig.Run("write", "bootorder", "--order", NewOrder, "--confirm", "--json");
        string folder = Assert.IsType<string>(ParseSingleDocument(written.Output).GetProperty("snapshotFolder").GetString());

        CliRun run = rig.Run("write", "restore", folder, "--json");

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Single(rig.Writer.Calls);
        Assert.Single(rig.TransactionFolders());
        Assert.EndsWith(WriteCommand.DryRunSentence + "\n", run.Error, StringComparison.Ordinal);
        JsonElement document = ParseSingleDocument(run.Output);
        Assert.Equal(CurrentOrderHex, document.GetProperty("proposed").GetProperty("hex").GetString());
        Assert.Equal(NewOrderHex, Hex(Current(rig, BootOrder)));
    }

    [Fact]
    public void DryRun_WithoutJson_PrintsThePlanTheChecksAndTheSentence()
    {
        using WriteRig rig = new();

        CliRun run = rig.Run("write", "bootorder", "--order", NewOrder);

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        AssertRow(run.Output, "Operation", "Reorder");
        AssertRow(run.Output, "Requested", Regex.Escape(NewOrder));
        AssertRow(run.Output, "Dry run", "true");
        AssertRow(run.Output, "Outcome", "Prepared");
        Assert.Contains("\nBefore:\n", run.Output, StringComparison.Ordinal);
        Assert.Contains("\nAfter:\n", run.Output, StringComparison.Ordinal);
        Assert.Contains("\nChanges:\n", run.Output, StringComparison.Ordinal);
        Assert.Contains("\nChecks:\n", run.Output, StringComparison.Ordinal);
        Assert.Matches("(?m)^1 +Boot0002 +" + Regex.Escape(WindowsBootManagerDescription) + " +active, category Boot$", run.Output);
        Assert.Matches("(?m)^1 +Boot0003 +" + Regex.Escape(UsbStickDescription) + " +active, category Boot$", run.Output);
        Assert.Matches("(?m)^permutation +passed$", run.Output);
        Assert.EndsWith("\n" + TransactionSentences.ChecksPassed + "\n", run.Output, StringComparison.Ordinal);
        Assert.EndsWith(WriteCommand.DryRunSentence + "\n", run.Error, StringComparison.Ordinal);
        Assert.Empty(rig.Writer.Calls);
    }

    // ---- the failure-case matrix, rows that the dry run refuses -------------------------------

    [Theory]
    [InlineData("FC-03", CheckNames.EntriesExist)]
    [InlineData("FC-04", CheckNames.EntriesExist)]
    [InlineData("FC-05", CheckNames.Permutation)]
    [InlineData("FC-06-duplicate", CheckNames.Permutation)]
    [InlineData("FC-06-removed", CheckNames.Permutation)]
    [InlineData("FC-07", CheckNames.NoChange)]
    [InlineData("FC-08-odd-bootorder", CheckNames.Format)]
    [InlineData("FC-08-four-byte-bootnext", CheckNames.Format)]
    [InlineData("FC-09-nv-bs", CheckNames.Attributes)]
    [InlineData("FC-09-authenticated", CheckNames.Attributes)]
    [InlineData("FC-10", CheckNames.CurrentListIntact)]
    [InlineData("FC-11-inactive", CheckNames.BootNextTarget)]
    [InlineData("FC-11-application", CheckNames.BootNextTarget)]
    [InlineData("FC-18", CheckNames.Enumeration)]
    [InlineData("FC-26", CheckNames.BootNextTarget)]
    public void EveryRowTheDryRunRefuses_ExitsSeven_WithNothingWritten(string row, string blockedBy)
    {
        (VariableSet? set, string[] args) = row switch
        {
            "FC-03" => Row(null, Change("bootorder", "--order", "Boot0002,Boot0010")),
            "FC-04" => Row(null, Change("bootnext", "Boot0010")),
            "FC-05" => Row(null, Change("bootorder", "--order", "Boot0002,Boot0000")),
            "FC-06-duplicate" => Row(null, Change("bootorder", "--order", "Boot0002,Boot0002")),
            "FC-06-removed" => Row(null, Change("bootorder", "--order", "Boot0002")),
            "FC-07" => Row(null, Change("bootorder", "--order", CurrentOrder)),
            "FC-08-odd-bootorder" => Row(Store(Var(BootOrder, new byte[] { 0x02, 0x00, 0x03 })), Change("bootorder", "--order", NewOrder)),
            "FC-08-four-byte-bootnext" => Row(Store(Var(BootNext, BootList(UsbNumber, WindowsOnPartition1))), Change("bootnext", "--clear")),
            "FC-09-nv-bs" => Row(Store(Var(BootOrder, BootList(WindowsOnPartition1, UsbNumber), VariableAttributes.NonVolatile | VariableAttributes.BootServiceAccess)), Change("bootorder", "--order", NewOrder)),
            "FC-09-authenticated" => Row(Store(Var(BootOrder, BootList(WindowsOnPartition1, UsbNumber), NvBsRtTa)), Change("bootorder", "--order", NewOrder)),
            "FC-10" => Row(StoreWithOrder(WindowsOnPartition1, MissingNumber), Change("bootorder", "--order", "Boot0010,Boot0002")),
            "FC-11-inactive" => Row(null, Change("bootnext", "Boot0006")),
            "FC-11-application" => Row(null, Change("bootnext", "Boot0004")),
            "FC-18" => Row(null, Change("bootorder", "--order", NewOrder)),
            "FC-26" => Row(null, Change("bootnext", "--clear")),
            _ => throw new ArgumentOutOfRangeException(nameof(row), row, "not a row of this test"),
        };
        using WriteRig rig = new(set);
        if (row == "FC-18")
        {
            rig.Store.Method = EnumerationMethod.KnownNamesFallback;
            rig.Store.EnumerationError = "NtEnumerateSystemEnvironmentValuesEx failed";
        }

        CliRun run = rig.Run(args);

        JsonElement document = AssertBlocked(run, blockedBy);
        Assert.Empty(rig.Writer.Calls);
        Assert.Empty(rig.Suspender.Calls);
        Assert.Empty(rig.TransactionFolders());
        Assert.True(document.GetProperty("dryRun").GetBoolean());
        Assert.EndsWith(WriteCommand.DryRunSentence + "\n", run.Error, StringComparison.Ordinal);
        WriteRecord line = Assert.Single(rig.Records());
        Assert.Equal(WriteRecordOutcome.Blocked, line.Outcome);
        Assert.Equal(blockedBy, line.BlockedBy);
        Assert.False(line.ApiCalled);
        if (row == "FC-09-authenticated")
        {
            Assert.Contains(document.GetProperty("checks").EnumerateArray(), check => IsFailed(check, CheckNames.AuthenticatedWrite));
        }
    }

    [Theory]
    [InlineData("Timeout", null, Writability.NotWhitelisted, false)]
    [InlineData("PK", null, Writability.AuthenticatedWrite, true)]
    [InlineData("db", ImageSecurityGuidText, Writability.AuthenticatedWrite, true)]
    public void FC01_FC02_CheckOfAVariableOffTheWhitelist_IsBlockedAtTheWhitelist(string name, string? guid, Writability verdict, bool authenticated)
    {
        using WriteRig rig = new();
        List<string> args = new() { "write", "check", name, "--json" };
        if (guid is not null)
        {
            args.Add("--guid");
            args.Add(guid);
        }

        CliRun run = rig.Run(args.ToArray());

        JsonElement document = AssertBlocked(run, CheckNames.Whitelist);
        Assert.Equal(CheckSentences.Format(CheckSentences.NotWhitelistedFormat, name), document.GetProperty("sentence").GetString());
        JsonElement writability = document.GetProperty("writability");
        Assert.Equal(verdict.ToString(), writability.GetProperty("verdict").GetString());
        Assert.False(writability.GetProperty("changeable").GetBoolean());
        Assert.Equal(authenticated, document.GetProperty("checks").EnumerateArray().Any(check => IsFailed(check, CheckNames.AuthenticatedWrite)));
        Assert.Equal(nameof(WriteRecordOutcome.Blocked), document.GetProperty("record").GetProperty("outcome").GetString());
        Assert.DoesNotContain(WriteCommand.DryRunSentence, run.Error, StringComparison.Ordinal);
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
    public void FC12_AFirmwareRefusal_ExitsEight_AfterExactlyOneCall_AndNoRetry(int win32Error)
    {
        using WriteRig rig = new();
        rig.Writer.Win32Error = win32Error;

        CliRun run = rig.Run("write", "bootorder", "--order", NewOrder, "--confirm", "--json");

        Assert.Equal(ExitCodes.ApiRefused, run.ExitCode);
        Assert.Single(rig.Writer.Calls);
        Assert.Equal(CurrentOrderHex, Hex(Current(rig, BootOrder)));
        JsonElement document = ParseSingleDocument(run.Output);
        Assert.Equal(ExitCodes.ApiRefused, document.GetProperty("exitCode").GetInt32());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("blockedBy").ValueKind);
        Assert.True(document.GetProperty("apiCalled").GetBoolean());
        JsonElement api = document.GetProperty("api");
        Assert.False(api.GetProperty("succeeded").GetBoolean());
        Assert.Equal(win32Error, api.GetProperty("win32Error").GetInt32());
        Assert.True(document.GetProperty("verification").GetProperty("matchesBefore").GetBoolean());
        Assert.Equal(CurrentOrderHex, document.GetProperty("after").GetProperty("hex").GetString());
        Assert.Equal(nameof(WriteRecordOutcome.ApiRefused), document.GetProperty("record").GetProperty("outcome").GetString());
        string refusal = WriteStatusMapping.Sentence(WriteStatusMapping.Failed(win32Error, rig.Writer.NtStatus, "RtlGetLastNtStatus", TimeSpan.Zero));
        string sentence = Assert.IsType<string>(document.GetProperty("sentence").GetString());
        Assert.StartsWith(refusal, sentence, StringComparison.Ordinal);
        Assert.Contains(sentence, run.Error, StringComparison.Ordinal);
        Assert.Single(rig.TransactionFolders());
        Assert.Equal(0, WriteCommand.CancelKeyHandlersInstalled);
    }

    [Theory]
    [InlineData(false, PrivilegeState.NotElevated)]
    [InlineData(true, PrivilegeState.ElevatedPrivilegeAbsent)]
    public void FC13_FC14_AMissingPrivilege_ExitsFour_BeforeAnyStageOfThePipeline(bool elevated, PrivilegeState state)
    {
        using WriteRig rig = new();
        rig.Privileges.Elevated = elevated;
        rig.Privileges.State = state;

        CliRun run = rig.Run("write", "bootnext", "Boot0010", "--confirm", "--json");

        Assert.Equal(ExitCodes.NotElevated, run.ExitCode);
        JsonElement document = ParseSingleDocument(run.Output);
        Assert.Equal(
            new[] { "command", "ok", "exitCode", "error", "firmwareType", "elevated", "privilegeState" },
            document.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(WriteCommand.CommandName, document.GetProperty("command").GetString());
        Assert.Equal(state.ToString(), document.GetProperty("privilegeState").GetString());
        Assert.Empty(rig.Store.Reads);
        Assert.Equal(0, rig.Identity.Calls);
        Assert.Equal(0, rig.BitLocker.Calls);
        Assert.Empty(rig.Writer.Calls);
        Assert.False(Directory.Exists(rig.WritesDirectory));
        Assert.False(Directory.Exists(rig.SnapshotRoot));
    }

    [Theory]
    [InlineData("bootnext", "Boot0002")]
    [InlineData("reorder", "Boot0003")]
    [InlineData("reorderFirstStays", "Boot0002")]
    public void FC15_BitLockerOnWithoutAChoice_BlocksTheDryRunAtBitLocker_WithTheWarningLinesAndTheHint(string scenario, string first)
    {
        using WriteRig rig = new(bitLocker: TransactionFakes.BitLockerOn());
        string[] args = scenario switch
        {
            "bootnext" => Change("bootnext", first),
            "reorder" => Change("bootorder", "--order", NewOrder),
            _ => Change("bootorder", "--order", CurrentOrder, "--allow-no-change"),
        };

        CliRun run = rig.Run(args);

        JsonElement document = AssertBlocked(run, CheckNames.BitLocker);
        Assert.Equal(CheckSentences.BitLockerChoiceMissing, document.GetProperty("sentence").GetString());
        Assert.Empty(rig.Writer.Calls);
        Assert.Empty(rig.Suspender.Calls);
        string risk = scenario switch
        {
            "bootnext" => CheckSentences.Format(BitLockerPreflight.BootNextRiskFormat, first),
            "reorder" => BitLockerPreflight.ReorderRiskSentence,
            _ => CheckSentences.Format(BitLockerPreflight.ReorderRiskFirstEntryStaysFormat, first),
        };
        string state = CheckSentences.Format(BitLockerPreflight.OnSentenceFormat, BitLockerPreflight.OsDriveLetter, "PCR 0, 2, 4, 11; TPM 1.2");
        string[] lines = run.Error.Split('\n');
        Assert.Contains(CheckSentences.Format(WriteCommand.BitLockerStateLineFormat, state), lines);
        Assert.Contains(CheckSentences.Format(WriteCommand.RiskLineFormat, risk), lines);
        Assert.Contains(CheckSentences.Format(WriteCommand.RecoveryKeyLineFormat, BitLockerPreflight.RecoveryKeySentence), lines);
        Assert.Contains(WriteCommand.BitLockerHint, lines);
        Assert.EndsWith(WriteCommand.DryRunSentence + "\n", run.Error, StringComparison.Ordinal);
        JsonElement bitLocker = document.GetProperty("bitLocker");
        Assert.Equal(nameof(BitLockerState.On), bitLocker.GetProperty("state").GetString());
        Assert.True(bitLocker.GetProperty("suspendOffered").GetBoolean());
        Assert.Equal(JsonValueKind.Null, bitLocker.GetProperty("choice").ValueKind);
        Assert.Equal(risk, bitLocker.GetProperty("risk").GetString());
        Assert.True(document.GetProperty("record").GetProperty("bitLocker").GetProperty("suspendOffered").GetBoolean());
    }

    [Fact]
    public void Risk_OfAReorderThatMovesBootCurrentsEntryUpToFirst_DoesNotSayTheFirstEntryStays()
    {
        // BootCurrent is Boot0002 but BootOrder starts with Boot0003: the last start went past the
        // first entry, so putting Boot0002 first changes the start, and the risk sentence must not
        // say that the first entry stays. Both the dry run that stops at
        // the BitLocker check (no plan) and the one that passes (a plan) are checked.
        using WriteRig rig = new(StoreWithOrder(UsbNumber, WindowsOnPartition1), TransactionFakes.BitLockerOn());

        CliRun blocked = rig.Run("write", "bootorder", "--order", CurrentOrder, "--json");
        CliRun planned = rig.Run("write", "bootorder", "--order", CurrentOrder, "--bitlocker", "continue", "--json");

        Assert.Equal(ExitCodes.Blocked, blocked.ExitCode);
        Assert.Equal(ExitCodes.Success, planned.ExitCode);
        foreach (CliRun run in new[] { blocked, planned })
        {
            JsonElement document = ParseSingleDocument(run.Output);
            Assert.Equal(BitLockerPreflight.ReorderRiskSentence, document.GetProperty("bitLocker").GetProperty("risk").GetString());
            Assert.Contains(CheckSentences.Format(WriteCommand.RiskLineFormat, BitLockerPreflight.ReorderRiskSentence), run.Error.Split('\n'));
        }

        Assert.Equal(JsonValueKind.Null, ParseSingleDocument(blocked.Output).GetProperty("proposed").ValueKind);
        Assert.Equal(NewOrderHex, ParseSingleDocument(planned.Output).GetProperty("before").GetProperty("hex").GetString());
        Assert.Empty(rig.Writer.Calls);
        Assert.Empty(rig.Suspender.Calls);
    }

    [Theory]
    [InlineData("on")]
    [InlineData("suspended")]
    public void FC16_ABootNextToTheOtherWindowsBootManager_IsRefused_WhileProtectorsExist(string state)
    {
        using WriteRig rig = new(bitLocker: state == "on" ? TransactionFakes.BitLockerOn() : BitLockerSuspended());

        CliRun run = rig.Run("write", "bootnext", "Boot0000", "--json");

        JsonElement document = AssertBlocked(run, CheckNames.OtherWindowsPath);
        Assert.Equal(CheckSentences.Format(CheckSentences.OtherWindowsPathFormat, "Boot0000"), document.GetProperty("sentence").GetString());
        Assert.DoesNotContain(WriteCommand.BitLockerHint, run.Error, StringComparison.Ordinal);
        Assert.Empty(rig.Writer.Calls);
        Assert.Empty(rig.Suspender.Calls);
    }

    [Fact]
    public void FC17_ABitLockerStateThatCannotBeRead_IsTreatedAsOn()
    {
        using WriteRig rig = new();
        rig.BitLocker.Failure = new TimeoutException("the BitLocker query did not answer within 60 seconds");

        CliRun run = rig.Run("write", "bootnext", "Boot0002", "--json");

        JsonElement document = AssertBlocked(run, CheckNames.BitLocker);
        string state = CheckSentences.Format(BitLockerPreflight.UnknownSentenceFormat, BitLockerPreflight.QueryFailedReason);
        Assert.Contains(CheckSentences.Format(WriteCommand.BitLockerStateLineFormat, state), run.Error.Split('\n'));
        Assert.Contains(WriteCommand.BitLockerHint, run.Error.Split('\n'));
        Assert.Equal(nameof(BitLockerState.Unknown), document.GetProperty("bitLocker").GetProperty("state").GetString());
        Assert.Empty(rig.Writer.Calls);
    }

    [Theory]
    [InlineData("otherModel")]
    [InlineData("optiPlexOnA17")]
    public void FC19_OnAnotherMachine_TheDryRunPasses_AndTheConfirmationIsRefused(string machine)
    {
        using WriteRig rig = new(identity: machine == "otherModel" ? OtherMachine() : OptiPlexOnA17());

        CliRun dryRun = rig.Run("write", "bootorder", "--order", NewOrder, "--json");
        CliRun run = rig.Run("write", "bootorder", "--order", NewOrder, "--confirm", "--json");

        Assert.Equal(ExitCodes.Success, dryRun.ExitCode);
        Assert.Contains(WritabilitySentences.WrongMachine, Warnings(ParseSingleDocument(dryRun.Output)));
        JsonElement document = AssertBlocked(run, CheckNames.ExpectedModel);
        Assert.Equal(CheckSentences.WrongMachine, document.GetProperty("sentence").GetString());
        Assert.False(document.GetProperty("record").GetProperty("machine").GetProperty("confirmed").GetBoolean());
        Assert.True(document.GetProperty("record").GetProperty("machine").GetProperty("unitConfirmed").GetBoolean());
        Assert.Empty(rig.Writer.Calls);
        Assert.Empty(rig.TransactionFolders());
    }

    /// <summary>
    /// FC-33, the tester's case (decided 25 September 2026): another OptiPlex 7020 on BIOS A18, the
    /// contracted model and BIOS version, whose Windows machine ID is not the M3 test unit's. The
    /// dry run passes with the Writability sentence as a warning; the confirmation is refused at
    /// expectedUnit (exit 7) before any call, and the record says the model was confirmed and the
    /// unit was not.
    /// </summary>
    [Fact]
    public void FC33_AnotherOptiPlex7020OnA18_TheDryRunPasses_AndTheConfirmationIsRefusedAtTheUnitCheck()
    {
        using WriteRig rig = AnotherOptiPlexOnA18();

        CliRun dryRun = rig.Run("write", "bootorder", "--order", NewOrder, "--json");
        CliRun run = rig.Run("write", "bootorder", "--order", NewOrder, "--confirm", "--json");

        Assert.Equal(ExitCodes.Success, dryRun.ExitCode);
        Assert.Contains(WritabilitySentences.WrongMachine, Warnings(ParseSingleDocument(dryRun.Output)));
        Assert.Equal(ExitCodes.Blocked, run.ExitCode);
        JsonElement document = AssertBlocked(run, CheckNames.ExpectedUnit);
        Assert.Equal(CheckSentences.NotEnabledUnit, document.GetProperty("sentence").GetString());
        Assert.False(document.GetProperty("apiCalled").GetBoolean());
        JsonElement machine = document.GetProperty("record").GetProperty("machine");
        Assert.True(machine.GetProperty("confirmed").GetBoolean());
        Assert.False(machine.GetProperty("unitConfirmed").GetBoolean());
        Assert.Empty(rig.Writer.Calls);
        Assert.Empty(rig.Suspender.Calls);
        Assert.Empty(rig.TransactionFolders());
        Assert.Equal(CheckNames.ExpectedUnit, rig.Records()[^1].BlockedBy);
        Assert.False(rig.Records()[^1].Machine!.UnitConfirmed);
    }

    /// <summary>FC-33 for write check: on another OptiPlex 7020 on A18 the check is refused at expectedUnit, exit 7, exactly as on another model at expectedModel.</summary>
    [Fact]
    public void FC33_CheckOnAnotherOptiPlex7020OnA18_ExitsSeven_AtTheUnitCheck()
    {
        using WriteRig rig = AnotherOptiPlexOnA18();

        CliRun run = rig.Run("write", "check", BootOrder, "--json");

        JsonElement document = AssertBlocked(run, CheckNames.ExpectedUnit);
        Assert.Equal(nameof(Writability.WrongMachine), document.GetProperty("writability").GetProperty("verdict").GetString());
        Assert.False(document.GetProperty("writability").GetProperty("changeable").GetBoolean());
        Assert.Equal(WritabilitySentences.WrongMachine, document.GetProperty("sentence").GetString());
        Assert.False(document.GetProperty("apiCalled").GetBoolean());
        JsonElement machine = document.GetProperty("record").GetProperty("machine");
        Assert.True(machine.GetProperty("confirmed").GetBoolean());
        Assert.False(machine.GetProperty("unitConfirmed").GetBoolean());
        Assert.Empty(rig.Writer.Calls);
        Assert.Equal(WriteRecordOutcome.Blocked, rig.Records()[^1].Outcome);
        Assert.Equal(CheckNames.ExpectedUnit, rig.Records()[^1].BlockedBy);
    }

    /// <summary>
    /// FC-33 for the BootNext operations: on another OptiPlex 7020 on A18, a one-time boot and its
    /// removal with --confirm are refused at expectedUnit (exit 7) exactly as a new order is, with
    /// no write call, no suspend and no transaction folder.
    /// </summary>
    [Theory]
    [InlineData("Boot0002")]
    [InlineData("--clear")]
    public void FC33_BootNextOnAnotherOptiPlex7020OnA18_WithConfirm_IsRefusedAtTheUnitCheck(string argument)
    {
        using WriteRig rig = AnotherOptiPlexOnA18(argument == "--clear" ? Store(Var(BootNext, U16(WindowsOnPartition1))) : null);

        CliRun run = rig.Run("write", "bootnext", argument, "--confirm", "--json");

        Assert.Equal(ExitCodes.Blocked, run.ExitCode);
        JsonElement document = AssertBlocked(run, CheckNames.ExpectedUnit);
        Assert.Equal(CheckSentences.NotEnabledUnit, document.GetProperty("sentence").GetString());
        Assert.False(document.GetProperty("apiCalled").GetBoolean());
        Assert.False(document.GetProperty("record").GetProperty("machine").GetProperty("unitConfirmed").GetBoolean());
        Assert.Empty(rig.Writer.Calls);
        Assert.Empty(rig.Suspender.Calls);
        Assert.Empty(rig.TransactionFolders());
        Assert.Equal(CheckNames.ExpectedUnit, rig.Records()[^1].BlockedBy);
    }

    /// <summary>A machine ID that cannot be read on the contracted model is refused at expectedUnit with its own sentence.</summary>
    [Fact]
    public void AMachineIdThatCannotBeRead_IsRefusedAtTheUnitCheck()
    {
        using WriteRig rig = new() { MachineId = null };

        CliRun run = rig.Run("write", "bootorder", "--order", NewOrder, "--confirm", "--json");

        JsonElement document = AssertBlocked(run, CheckNames.ExpectedUnit);
        Assert.Equal(CheckSentences.MachineIdNotRead, document.GetProperty("sentence").GetString());
        Assert.Empty(rig.Writer.Calls);
    }

    [Theory]
    [InlineData("outsideTheDataFolder", false)]
    [InlineData("aFileUnderTheDataFolder", false)]
    [InlineData("refusedByTheFolderCheck", false)]
    [InlineData("outsideTheDataFolder", true)]
    public void FC20_ARefusedSnapshotRoot_BlocksAtSnapshotRoot_AndCreatesNothing(string scenario, bool bitLockerOn)
    {
        // With BitLocker on, as on the unit, the line carries "--bitlocker continue", as the rig's
        // FC-20 line does: stage S8 needs the choice before stage S9 looks at the root.
        using WriteRig rig = new(bitLocker: bitLockerOn ? TransactionFakes.BitLockerOn() : null);
        string[] choice = bitLockerOn ? new[] { "--bitlocker", "continue" } : Array.Empty<string>();
        string outsideParent = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "cli-outside-" + Guid.NewGuid().ToString("N"));
        string root = Path.Combine(outsideParent, "snapshots");
        string expected = CheckSentences.Format(SnapshotStore.OutsideDataFolderFormat, root, rig.DataDirectory);
        if (scenario == "aFileUnderTheDataFolder")
        {
            Directory.CreateDirectory(rig.SnapshotRoot);
            root = Path.Combine(rig.SnapshotRoot, "not-a-folder.txt");
            File.WriteAllText(root, "not a folder");
            expected = CheckSentences.Format(SnapshotStore.FolderIsAFileFormat, root);
        }
        else if (scenario == "refusedByTheFolderCheck")
        {
            root = Path.Combine(rig.DataDirectory, "other-snapshots");
            rig.RefuseFolders = true;
            expected = WriteRig.Refusal;
        }

        try
        {
            CliRun run = rig.Run(new[] { "write", "bootnext", "Boot0002" }.Concat(choice).Concat(new[] { "--snapshot-root", root, "--json" }).ToArray());

            JsonElement document = AssertBlocked(run, CheckNames.SnapshotRoot);
            Assert.Equal(expected, document.GetProperty("sentence").GetString());
            Assert.False(Directory.Exists(outsideParent));
            Assert.False(Directory.Exists(Path.Combine(rig.DataDirectory, "other-snapshots")));
            Assert.Empty(rig.Writer.Calls);
            Assert.Empty(rig.Suspender.Calls);
        }
        finally
        {
            TestFolders.DeleteQuietly(outsideParent);
        }
    }

    [Fact]
    public void FC20_WithBitLockerOnAndNoChoice_TheLineStopsAtBitLocker_BeforeTheRootIsLookedAt()
    {
        // Why the rig's FC-20 line carries "--bitlocker continue": without it, on the unit, stage
        // S8 refuses the line before stage S9 would refuse the root.
        using WriteRig rig = new(bitLocker: TransactionFakes.BitLockerOn());
        string outsideParent = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "cli-outside-" + Guid.NewGuid().ToString("N"));
        string root = Path.Combine(outsideParent, "snapshots");

        try
        {
            CliRun run = rig.Run("write", "bootnext", "Boot0002", "--snapshot-root", root, "--json");

            JsonElement document = AssertBlocked(run, CheckNames.BitLocker);
            Assert.DoesNotContain(document.GetProperty("checks").EnumerateArray(), check => check.GetProperty("name").GetString() == CheckNames.SnapshotRoot);
            Assert.False(Directory.Exists(outsideParent));
            Assert.Empty(rig.Writer.Calls);
        }
        finally
        {
            TestFolders.DeleteQuietly(outsideParent);
        }
    }

    [Fact]
    public void FC21_ASnapshotThatDoesNotReadBackAsWritten_BlocksAtSnapshot_BeforeAnyCall()
    {
        using WriteRig rig = new();
        bool changed = false;

        // The moment stage S12 creates the transaction folder, the value the checks read changes in
        // memory, so before\ is exported from other bytes than the plan's and the read-back sees it.
        rig.OnPrepareFolder = folder =>
        {
            if (!changed
                && string.Equals(Path.GetDirectoryName(folder), rig.SnapshotRoot, StringComparison.OrdinalIgnoreCase)
                && rig.Store.Current(BootOrder) is FirmwareVariable current)
            {
                changed = true;
                current.Data[0] ^= 0xFF;
            }
        };

        CliRun run = rig.Run("write", "bootorder", "--order", NewOrder, "--confirm", "--json");

        JsonElement document = AssertBlocked(run, CheckNames.Snapshot);
        Assert.True(changed);
        string transactionId = Assert.IsType<string>(document.GetProperty("transactionId").GetString());
        string expected = CheckSentences.Format(
            TransactionSentences.SnapshotNotConfirmedFormat,
            transactionId,
            CheckSentences.Format(SnapshotStore.HashMismatchFormat, BootOrder));
        Assert.Equal(expected, document.GetProperty("sentence").GetString());
        Assert.Empty(rig.Writer.Calls);
        Assert.Equal(WriteRecordOutcome.Blocked, RecordFile(Assert.Single(rig.TransactionFolders())).Outcome);
    }

    [Fact]
    public void FC21_ARestoreFromASnapshotWhoseBeforeFolderWasTampered_BlocksAtSnapshot()
    {
        using WriteRig rig = new();
        CliRun written = rig.Run("write", "bootorder", "--order", NewOrder, "--confirm", "--json");
        string folder = Assert.IsType<string>(ParseSingleDocument(written.Output).GetProperty("snapshotFolder").GetString());
        string file = Assert.Single(Directory.GetFiles(Path.Combine(folder, SnapshotStore.BeforeDirectoryName, "vars"), "*_BootOrder.bin"));
        byte[] bytes = File.ReadAllBytes(file);
        bytes[0] ^= 0xFF;
        File.WriteAllBytes(file, bytes);

        CliRun run = rig.Run("write", "restore", Path.GetFileName(folder), "--confirm", "--json");

        JsonElement document = AssertBlocked(run, CheckNames.Snapshot);
        Assert.StartsWith(CheckSentences.Format(SnapshotStore.UnreadableFormat, folder, string.Empty), document.GetProperty("sentence").GetString(), StringComparison.Ordinal);
        Assert.Single(rig.Writer.Calls);
        Assert.Single(rig.TransactionFolders());
    }

    [Fact]
    public void FC22_AValueChangedBetweenTheDryRunAndTheCall_BlocksAtStale()
    {
        using WriteRig rig = new();

        // BootOrder's first read is the dry run's (stage S3); the second is the first drift read of stage S12.
        rig.Store.ChangeBeforeRead(BootOrder, 2, Var(BootOrder, BootList(UsbNumber, WindowsOnPartition1)));

        CliRun run = rig.Run("write", "bootorder", "--order", NewOrder, "--confirm", "--json");

        JsonElement document = AssertBlocked(run, CheckNames.Stale);
        Assert.Equal(CheckSentences.Format(CheckSentences.StaleReadFormat, BootOrder), document.GetProperty("sentence").GetString());
        Assert.Empty(rig.Writer.Calls);
        Assert.Single(rig.TransactionFolders());
    }

    [Theory]
    [InlineData("refused", "0x80310000")]
    [InlineData("timedOut", "timeout")]
    public void FC23_FC32_ASuspendThatFails_BlocksAtBitLockerSuspend_WithNothingWritten(string scenario, string suspendResult)
    {
        using WriteRig rig = new(bitLocker: TransactionFakes.BitLockerOn());
        rig.Suspender.Result = scenario == "refused"
            ? TransactionFakes.FakeSuspender.Refused(0x80310000)
            : TransactionFakes.FakeSuspender.TimedOut();

        CliRun run = rig.Run("write", "bootnext", "Boot0002", "--confirm", "--bitlocker", "suspend", "--json");

        JsonElement document = AssertBlocked(run, CheckNames.BitLockerSuspend);
        Assert.Equal(rig.Suspender.Result.Sentence, document.GetProperty("sentence").GetString());
        Assert.Single(rig.Suspender.Calls);
        Assert.Empty(rig.Writer.Calls);
        JsonElement recorded = document.GetProperty("record").GetProperty("bitLocker");
        Assert.Equal(suspendResult, recorded.GetProperty("suspendResult").GetString());
        if (scenario == "timedOut")
        {
            Assert.Equal(nameof(BitLockerState.Unknown), recorded.GetProperty("state").GetString());
        }
    }

    [Theory]
    [InlineData("unverified", 0, false, WriteRecordOutcome.Unverified, CurrentOrderHex)]
    [InlineData("changedDespiteError", 5, true, WriteRecordOutcome.ChangedDespiteError, NewOrderHex)]
    [InlineData("neither", 0, false, WriteRecordOutcome.Unverified, "02000200")]
    public void FC24_AReadBackThatIsNotWhatTheCallImplies_ExitsNine(string scenario, int win32Error, bool changesTheStore, WriteRecordOutcome outcome, string readBackHex)
    {
        using WriteRig rig = new();
        rig.Writer.Win32Error = win32Error;
        rig.Writer.ChangesTheStore = changesTheStore;
        if (scenario == "neither")
        {
            rig.Writer.DuringCall = _ => rig.Store.Set(Var(BootOrder, BootList(WindowsOnPartition1, WindowsOnPartition1)));
        }

        CliRun run = rig.Run("write", "bootorder", "--order", NewOrder, "--confirm", "--json");

        Assert.Equal(ExitCodes.Unverified, run.ExitCode);
        Assert.Single(rig.Writer.Calls);
        JsonElement document = ParseSingleDocument(run.Output);
        Assert.False(document.GetProperty("ok").GetBoolean());
        Assert.True(document.GetProperty("apiCalled").GetBoolean());
        Assert.Equal(outcome.ToString(), document.GetProperty("record").GetProperty("outcome").GetString());
        Assert.Equal(readBackHex, document.GetProperty("after").GetProperty("hex").GetString());
        Assert.Contains(Assert.IsType<string>(document.GetProperty("sentence").GetString()), run.Error, StringComparison.Ordinal);
        Assert.Equal(0, WriteCommand.CancelKeyHandlersInstalled);
    }

    [Fact]
    public void FC25_ARestoreWhoseOrderIsNoLongerAPermutation_IsRefusedWithTheRestoreSentence()
    {
        using WriteRig rig = new();
        CliRun written = rig.Run("write", "bootorder", "--order", NewOrder, "--confirm", "--json");
        string folder = Assert.IsType<string>(ParseSingleDocument(written.Output).GetProperty("snapshotFolder").GetString());
        rig.Store.Set(Var(BootOrder, BootList(WindowsOnPartition1)));

        CliRun run = rig.Run("write", "restore", folder, "--json");

        JsonElement document = AssertBlocked(run, CheckNames.Permutation);
        Assert.Equal(CheckSentences.Format(CheckSentences.RestoreOrderAddsFormat, "Boot0003"), document.GetProperty("sentence").GetString());
        Assert.Single(rig.Writer.Calls);
    }

    [Theory]
    [InlineData("--dry-run", "--confirm")]
    [InlineData("--bitlocker", "later")]
    [InlineData("--confirm", "--snapshot-root", "other-snapshots")]
    [InlineData("--snapshot-root", "other-snapshots", "--confirm", "--bitlocker", "continue")]
    public void FC27_AConflictOrAnUnknownValue_IsAUsageError_BeforeAnyCheck(params string[] extra)
    {
        using WriteRig rig = new();

        CliRun run = rig.Run(Change("bootorder", "--order", NewOrder).Concat(extra).ToArray());

        Assert.Equal(ExitCodes.Usage, run.ExitCode);
        JsonElement document = ParseSingleDocument(run.Output);
        Assert.Equal(WriteCommand.CommandName, document.GetProperty("command").GetString());
        Assert.Equal(ExitCodes.Usage, document.GetProperty("exitCode").GetInt32());
        if (extra[0] == "--dry-run")
        {
            Assert.Contains(WriteCommand.DryRunAndConfirmTogether, run.Error, StringComparison.Ordinal);
        }

        // A confirmed change keeps its snapshot under the default root, where snapshots and
        // Restore previous value find it; --snapshot-root exists for the dry run of FC-20.
        if (extra.Contains("--snapshot-root"))
        {
            Assert.Contains(WriteCommand.SnapshotRootWithConfirm, run.Error, StringComparison.Ordinal);
        }

        Assert.Empty(rig.Store.Reads);
        Assert.Empty(rig.Writer.Calls);
        Assert.Empty(rig.Suspender.Calls);
        Assert.False(Directory.Exists(rig.WritesDirectory));
        Assert.False(Directory.Exists(rig.SnapshotRoot));
    }

    [Theory]
    [InlineData(WriteCommand.CommandName, "write", "bootorder", "--order", "Boot2", "--json")]
    [InlineData(WriteCommand.CommandName, "write", "bootnext", "--json")]
    [InlineData(WriteCommand.CommandName, "write", "restore", "--json")]
    [InlineData(WriteCommand.CommandName, "write", "check", "BootOrder", "--guid", "not-a-guid", "--json")]
    [InlineData(WriteCommand.CommandName, "write", "--json")]
    [InlineData(WriteCommand.LogCommandName, "write", "log", "--last", "0", "--json")]
    public void AParseError_NamesTheCommandAsTheCommandsOwnDocumentsDo(string command, params string[] args)
    {
        using WriteRig rig = new();

        CliRun run = rig.Run(args);

        Assert.Equal(ExitCodes.Usage, run.ExitCode);
        Assert.Equal(command, ParseSingleDocument(run.Output).GetProperty("command").GetString());
    }

    [Theory]
    [InlineData(WriteCommand.CommandName, "write", "check", "BootOrder", "--json")]
    [InlineData(WriteCommand.LogCommandName, "write", "log", "--json")]
    public void AFailedPrecondition_NamesTheCommandAsAParseErrorDoes(string command, params string[] args)
    {
        using WriteRig rig = new();
        rig.Privileges.Elevated = false;
        rig.Privileges.State = PrivilegeState.NotElevated;

        CliRun run = rig.Run(args);

        Assert.Equal(ExitCodes.NotElevated, run.ExitCode);
        Assert.Equal(command, ParseSingleDocument(run.Output).GetProperty("command").GetString());
    }

    [Theory]
    [InlineData("write", "bootnext", "Boot0002", "--clear", "--json")]
    [InlineData("write", "bootnext", "--json")]
    [InlineData("write", "bootorder", "--order", "Boot2,Boot0003", "--json")]
    [InlineData("write", "bootorder", "--order", "boot0003,Boot0002", "--json")]
    [InlineData("write", "bootorder", "--json")]
    [InlineData("write", "restore", "--json")]
    [InlineData("write", "check", "BootOrder", "--guid", "not-a-guid", "--json")]
    [InlineData("write", "log", "--last", "0", "--json")]
    [InlineData("write", "--json")]
    public void OtherMalformedCommandLines_AreUsageErrors_BeforeAnyCheck(params string[] args)
    {
        using WriteRig rig = new();

        CliRun run = rig.Run(args);

        Assert.Equal(ExitCodes.Usage, run.ExitCode);
        Assert.Equal(ExitCodes.Usage, ParseSingleDocument(run.Output).GetProperty("exitCode").GetInt32());
        Assert.Empty(rig.Store.Reads);
        Assert.Empty(rig.Writer.Calls);
        Assert.False(Directory.Exists(rig.WritesDirectory));
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("usb")]
    public void FC30_ABootCurrentThatNamesNoFixedDisk_IsRefused_WhileProtectorsExist(string scenario)
    {
        VariableSet set = scenario == "absent" ? Store().Without("BootCurrent") : Store(Var("BootCurrent", U16(UsbNumber), BsRt));
        using WriteRig rig = new(set, TransactionFakes.BitLockerOn());

        CliRun run = rig.Run("write", "bootnext", "Boot0002", "--bitlocker", "suspend", "--json");

        JsonElement document = AssertBlocked(run, CheckNames.BootCurrent);
        Assert.Equal(CheckSentences.BootCurrentUnusable, document.GetProperty("sentence").GetString());
        Assert.Empty(rig.Writer.Calls);
        Assert.Empty(rig.Suspender.Calls);
    }

    [Fact]
    public void FC31_TheEighthWriteSinceWindowsStarted_IsRefusedAtWriteBudget()
    {
        // Eight writes after the start of Windows: the pipeline's own clock is used, and the start
        // (now minus the kernel's tick count) lies before them. Core's WriteRecordLogTests and
        // WriteTransactionTests.TheEighthWriteSinceWindowsStarted_IsRefused count against a fixed start.
        using WriteRig rig = new();
        rig.AddPastWrites(8, DateTimeOffset.Now);

        CliRun run = rig.Run("write", "bootorder", "--order", NewOrder, "--confirm", "--json");

        JsonElement document = AssertBlocked(run, CheckNames.WriteBudget);
        Assert.Equal(CheckSentences.Format(CheckSentences.WriteBudgetExceededFormat, 8), document.GetProperty("sentence").GetString());
        Assert.Equal(8, document.GetProperty("record").GetProperty("writesThisStart").GetInt32());
        Assert.Empty(rig.Writer.Calls);
        Assert.Empty(rig.TransactionFolders());
    }

    // ---- write check -------------------------------------------------------------------------

    [Fact]
    public void Check_OfBootOrderOnTheContractedMachine_IsChangeable_AndWritesNothing()
    {
        using WriteRig rig = new();

        CliRun run = rig.Run("write", "check", BootOrder, "--json");

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        JsonElement document = ParseSingleDocument(run.Output);
        Assert.Equal("Check", document.GetProperty("operation").GetString());
        Assert.True(document.GetProperty("ok").GetBoolean());
        Assert.True(document.GetProperty("dryRun").GetBoolean());
        Assert.Equal(BootOrder, document.GetProperty("requested").GetString());
        JsonElement writability = document.GetProperty("writability");
        Assert.Equal(nameof(Writability.ReorderOnly), writability.GetProperty("verdict").GetString());
        Assert.True(writability.GetProperty("changeable").GetBoolean());
        Assert.Equal(WritabilitySentences.ReorderOnly, document.GetProperty("sentence").GetString());
        Assert.Equal(nameof(BitLockerState.Off), document.GetProperty("bitLocker").GetProperty("state").GetString());
        Assert.Empty(rig.Writer.Calls);
        Assert.Empty(rig.TransactionFolders());
        Assert.DoesNotContain(WriteCommand.DryRunSentence, run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_OnAnotherMachine_ExitsSeven_WithTheWrongMachineVerdict()
    {
        using WriteRig rig = new(identity: OtherMachine());

        CliRun run = rig.Run("write", "check", BootOrder, "--json");

        Assert.Equal(ExitCodes.Blocked, run.ExitCode);
        JsonElement document = ParseSingleDocument(run.Output);
        Assert.False(document.GetProperty("ok").GetBoolean());
        Assert.Equal(ExitCodes.Blocked, document.GetProperty("exitCode").GetInt32());

        // Every exit 7 names the check that refused.
        Assert.Equal(CheckNames.ExpectedModel, document.GetProperty("blockedBy").GetString());
        Assert.Equal(nameof(Writability.WrongMachine), document.GetProperty("writability").GetProperty("verdict").GetString());
        Assert.Equal(WritabilitySentences.WrongMachine, document.GetProperty("sentence").GetString());
        Assert.Contains(WritabilitySentences.WrongMachine + "\n", run.Error, StringComparison.Ordinal);
        Assert.Empty(rig.Writer.Calls);

        // The record, in the document and in write-log.jsonl, says what the document says.
        JsonElement record = document.GetProperty("record");
        Assert.Equal(nameof(WriteRecordOutcome.Blocked), record.GetProperty("outcome").GetString());
        Assert.Equal(ExitCodes.Blocked, record.GetProperty("exitCode").GetInt32());
        Assert.Equal(CheckNames.ExpectedModel, record.GetProperty("blockedBy").GetString());
        Assert.Equal(WriteRecordOutcome.Blocked, rig.Records()[^1].Outcome);
        Assert.Equal(CheckNames.ExpectedModel, rig.Records()[^1].BlockedBy);
    }

    [Fact]
    public void Check_OfAnAbsentBootNextOnAnotherMachine_ExitsSeven_AtTheMachineCheck()
    {
        using WriteRig rig = new(identity: OtherMachine());

        CliRun run = rig.Run("write", "check", BootNext, "--json");

        Assert.Equal(ExitCodes.Blocked, run.ExitCode);
        JsonElement document = ParseSingleDocument(run.Output);
        Assert.Equal(CheckNames.ExpectedModel, document.GetProperty("blockedBy").GetString());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("writability").ValueKind);
        Assert.Empty(rig.Writer.Calls);
    }

    [Fact]
    public void Check_OfAMalformedBootOrder_IsNeverChangeable()
    {
        using WriteRig rig = new(Store(Var(BootOrder, new byte[] { 0x02, 0x00, 0x03 })));

        CliRun run = rig.Run("write", "check", BootOrder, "--json");

        JsonElement document = AssertBlocked(run, CheckNames.Format);
        Assert.Equal(nameof(Writability.Malformed), document.GetProperty("writability").GetProperty("verdict").GetString());
        Assert.False(document.GetProperty("writability").GetProperty("changeable").GetBoolean());
    }

    [Fact]
    public void Check_OfAnAbsentBootNextOnTheContractedMachine_IsChangeable()
    {
        using WriteRig rig = new();

        CliRun run = rig.Run("write", "check", BootNext, "--json");

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Equal(JsonValueKind.Null, ParseSingleDocument(run.Output).GetProperty("writability").ValueKind);
    }

    // ---- write log and snapshots -------------------------------------------------------------

    [Fact]
    public void Log_PrintsTheNewestRecords_AsTheDocumentAndAsATable()
    {
        using WriteRig rig = new();
        CliRun empty = rig.Run("write", "log");
        Assert.Equal(ExitCodes.Success, empty.ExitCode);
        Assert.Equal(CheckSentences.Format(WriteCommand.NoRecordsFormat, rig.WriteLogPath) + "\n", empty.Output);
        rig.Run("write", "bootorder", "--order", NewOrder, "--json");
        rig.Run("write", "bootnext", "Boot0010", "--json");

        CliRun all = rig.Run("write", "log", "--json");
        CliRun last = rig.Run("write", "log", "--last", "1", "--json");
        CliRun text = rig.Run("write", "log");

        Assert.Equal(ExitCodes.Success, all.ExitCode);
        JsonElement document = ParseSingleDocument(all.Output);
        Assert.Equal(WriteCommand.LogCommandName, document.GetProperty("command").GetString());
        Assert.Equal(rig.WriteLogPath, document.GetProperty("file").GetString());
        Assert.Equal(2, document.GetProperty("total").GetInt32());
        Assert.Equal(2, document.GetProperty("records").GetArrayLength());
        Assert.Equal(nameof(WriteRecordOutcome.Prepared), document.GetProperty("records")[0].GetProperty("outcome").GetString());
        Assert.Equal(nameof(WriteRecordOutcome.Blocked), document.GetProperty("records")[1].GetProperty("outcome").GetString());
        JsonElement newest = Assert.Single(ParseSingleDocument(last.Output).GetProperty("records").EnumerateArray());
        Assert.Equal(CheckNames.EntriesExist, newest.GetProperty("blockedBy").GetString());
        Assert.Matches("(?m)^Time \\(UTC\\) +Transaction +Operation +Variable +Outcome +Exit +Write call made +Blocked by +Sentence$", text.Output);
        Assert.Matches("(?m) SetBootNext +BootNext +Blocked +7 +false +entriesExist +", text.Output);
        Assert.EndsWith(CheckSentences.Format(WriteCommand.RecordsShownFormat, 2, 2, rig.WriteLogPath) + "\n", text.Output, StringComparison.Ordinal);
        Assert.Equal(0, document.GetProperty("notRecords").GetInt32());
        Assert.DoesNotContain(CheckSentences.Format(WriteCommand.NotRecordsFormat, 0), text.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Log_CountsTheLinesThatAreNotWholeRecords_AsTheBudgetDoes()
    {
        using WriteRig rig = new();
        rig.Run("write", "bootorder", "--order", NewOrder, "--json");
        Assert.Single(rig.Records());

        // A line cut short by a crash during its append, and the zero bytes a power loss can leave.
        File.AppendAllText(rig.WriteLogPath, "{\"schema\":\"uefiaccess-write-record/1\",\"timestampUtc\":\"2026-09-30T12:15:0\n\0\0\0\0\n");

        CliRun json = rig.Run("write", "log", "--json");
        CliRun text = rig.Run("write", "log");

        Assert.Equal(ExitCodes.Success, json.ExitCode);
        JsonElement document = ParseSingleDocument(json.Output);
        Assert.Equal(1, document.GetProperty("total").GetInt32());
        Assert.Equal(2, document.GetProperty("notRecords").GetInt32());
        Assert.Equal(1, document.GetProperty("records").GetArrayLength());

        Assert.Equal(ExitCodes.Success, text.ExitCode);
        Assert.EndsWith(
            CheckSentences.Format(WriteCommand.RecordsShownFormat, 1, 1, rig.WriteLogPath) + "\n"
            + CheckSentences.Format(WriteCommand.NotRecordsFormat, 2) + "\n",
            text.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Log_OfAListThatExistsButCannotBeRead_IsAFailure_NotAnEmptyList()
    {
        using WriteRig rig = new();
        rig.Run("write", "bootorder", "--order", NewOrder, "--json");
        Assert.Single(rig.Records());
        string prefix = CheckSentences.Format(WriteCommand.LogUnreadableFormat, rig.WriteLogPath, string.Empty);

        CliRun json;
        CliRun text;

        // Held open without sharing, the file exists and every read of it fails.
        using (new FileStream(rig.WriteLogPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            json = rig.Run("write", "log", "--json");
            text = rig.Run("write", "log");
        }

        Assert.Equal(ExitCodes.NativeFailure, json.ExitCode);
        JsonElement document = ParseSingleDocument(json.Output);
        Assert.Equal(WriteCommand.LogCommandName, document.GetProperty("command").GetString());
        Assert.False(document.GetProperty("ok").GetBoolean());
        Assert.Equal(ExitCodes.NativeFailure, document.GetProperty("exitCode").GetInt32());
        string error = Assert.IsType<string>(document.GetProperty("error").GetString());
        Assert.StartsWith(prefix, error, StringComparison.Ordinal);
        Assert.Contains(error, json.Error, StringComparison.Ordinal);

        Assert.Equal(ExitCodes.NativeFailure, text.ExitCode);
        Assert.Contains(prefix, text.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(CheckSentences.Format(WriteCommand.NoRecordsFormat, rig.WriteLogPath), text.Output + text.Error, StringComparison.Ordinal);

        // Once the file can be read again, the record is there.
        Assert.Equal(1, ParseSingleDocument(rig.Run("write", "log", "--json").Output).GetProperty("total").GetInt32());
    }

    [Fact]
    public void Snapshots_ListsTheFoldersWithOperationTimeTargetAndOutcome()
    {
        using WriteRig rig = new();
        CliRun none = rig.Run("snapshots");
        Assert.Equal(ExitCodes.Success, none.ExitCode);
        Assert.Equal(CheckSentences.Format(SnapshotsCommand.NoSnapshotsFormat, rig.SnapshotRoot) + "\n", none.Output);
        CliRun written = rig.Run("write", "bootorder", "--order", NewOrder, "--confirm", "--json");
        string transactionId = Assert.IsType<string>(ParseSingleDocument(written.Output).GetProperty("transactionId").GetString());

        CliRun json = rig.Run("snapshots", "--json");
        CliRun text = rig.Run("snapshots");

        Assert.Equal(ExitCodes.Success, json.ExitCode);
        JsonElement document = ParseSingleDocument(json.Output);
        Assert.Equal(SnapshotsCommand.CommandName, document.GetProperty("command").GetString());
        Assert.Equal(rig.SnapshotRoot, document.GetProperty("root").GetString());
        Assert.Equal(1, document.GetProperty("count").GetInt32());
        JsonElement snapshot = Assert.Single(document.GetProperty("snapshots").EnumerateArray());
        Assert.Equal(transactionId, snapshot.GetProperty("name").GetString());
        Assert.Equal(nameof(ChangeOperation.Reorder), snapshot.GetProperty("operation").GetString());
        Assert.Equal(Whitelist.BootOrder.Key, snapshot.GetProperty("targetKey").GetString());
        Assert.Equal(nameof(WriteRecordOutcome.Verified), snapshot.GetProperty("outcome").GetString());
        Assert.Matches("(?m)^" + Regex.Escape(transactionId) + " +Reorder +\\S+ +BootOrder +Verified$", text.Output);
        Assert.EndsWith(CheckSentences.Format(SnapshotsCommand.CountFormat, 1, rig.SnapshotRoot) + "\n", text.Output, StringComparison.Ordinal);
    }

    // ---- the Ctrl+C rule ----------------------------------------------------------------------

    /// <summary>
    /// The CLI ignores Ctrl+C from S12 to S17. No test can press Ctrl+C
    /// at the test host, and the handler counter only counts beside the subscription, so the
    /// subscription is pinned in source: CommitIgnoringCtrlC puts IgnoreCancelKey on
    /// Console.CancelKeyPress before its try block and the commit call inside it, and takes it off
    /// in its finally block; IgnoreCancelKey cancels the key before its try block, so a failing
    /// standard error cannot keep the key from being ignored; and no other code of the CLI
    /// commits or touches the event. What the handler does when raised is CliCtrlCHandlerTests.
    /// </summary>
    [Fact]
    public void CommitIgnoringCtrlC_SubscribesTheHandlerBeforeTheCommit_AndRemovesItInFinally()
    {
        string writeCommand = Path.Combine(RepositoryPaths.Root, "src", "UefiAccess.Cli", "Commands", "WriteCommand.cs");
        string source = File.ReadAllText(writeCommand);

        string commit = Assert.Single(SourceCalls.MethodBodies(source, "CommitIgnoringCtrlC"));
        int subscribed = SingleIndex(commit, CancelKeySubscription);
        int tryBlock = SingleIndex(commit, @"\btry\b");
        int call = SingleIndex(commit, CommitCall);
        int finallyBlock = SingleIndex(commit, @"\bfinally\b");
        int removed = SingleIndex(commit, CancelKeyRemoval);
        Assert.True(subscribed < tryBlock, "CommitIgnoringCtrlC must put IgnoreCancelKey on Console.CancelKeyPress before its try block.");
        Assert.True(tryBlock < call && call < finallyBlock, "CommitIgnoringCtrlC must call CommitAsync inside its try block, after the handler is on.");
        Assert.True(finallyBlock < removed, "CommitIgnoringCtrlC must take IgnoreCancelKey off Console.CancelKeyPress in its finally block.");
        Assert.Equal(2, Regex.Matches(commit, CancelKeyEvent).Count);

        string handler = Assert.Single(SourceCalls.MethodBodies(source, "IgnoreCancelKey"));
        int cancelled = SingleIndex(handler, KeyCancelled);
        int handlerTry = SingleIndex(handler, @"\btry\b");
        Assert.True(cancelled < handlerTry, "IgnoreCancelKey must set Cancel = true before its try block, so the key is ignored even when the line cannot be written.");
        Assert.DoesNotMatch(@"\bCancel\s*=\s*false\b", handler);

        // The commit's call and the event appear nowhere else in the CLI: a second commit path
        // would run without the handler.
        List<string> elsewhere = new();
        foreach (string file in RepositoryPaths.Enumerate(Path.Combine("src", "UefiAccess.Cli"), "*.cs"))
        {
            string code = SourceCalls.WithoutCommentsAndStrings(File.ReadAllText(file));
            if (string.Equals(Path.GetFullPath(file), Path.GetFullPath(writeCommand), StringComparison.OrdinalIgnoreCase))
            {
                // Blank CommitIgnoringCtrlC's own body (line breaks kept, so the lines reported stay right).
                int start = code.IndexOf(commit, StringComparison.Ordinal);
                Assert.True(start >= 0, "The body of CommitIgnoringCtrlC was not found in WriteCommand.cs.");
                code = string.Concat(code[..start], Regex.Replace(commit, @"[^\r\n]", " "), code[(start + commit.Length)..]);
            }

            elsewhere.AddRange(SourceCalls.LinesMatching(code, new Regex(CommitCall + "|" + CancelKeyEvent, RegexOptions.CultureInvariant))
                .Select(line => $"{RepositoryPaths.Relative(file)}:{line}"));
        }

        Assert.True(elsewhere.Count == 0, "Only CommitIgnoringCtrlC may commit or touch Console.CancelKeyPress in the CLI; found: " + string.Join(", ", elsewhere));
    }

    // ---- composition ---------------------------------------------------------------------------

    [Theory]
    [InlineData("write", "bootorder", "--order", NewOrder, "--json")]
    [InlineData("write", "check", BootOrder, "--json")]
    [InlineData("write", "log", "--json")]
    [InlineData("snapshots", "--json")]
    public void APlatformWithoutWriteServices_NeverReachesAPipeline(params string[] args)
    {
        CliRun run = Run(CreatePlatform(new InMemoryFirmwareVariableStore(Array.Empty<FirmwareVariable>())), args);

        Assert.Equal(ExitCodes.NativeFailure, run.ExitCode);
        Assert.Contains(CliPlatform.NoWriteServicesMessage, run.Error, StringComparison.Ordinal);
        Assert.Equal(ExitCodes.NativeFailure, ParseSingleDocument(run.Output).GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public void CreateWindows_ComposesTheRealWriterAndSuspender_ExactlyOnce_AndNowhereElse()
    {
        string source = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "src", "UefiAccess.Cli", "Services", "CliPlatform.cs"));
        string body = Assert.Single(SourceCalls.MethodBodies(source, "CreateWindows"));
        Assert.Single(Regex.Matches(body, RealWriterConstruction));
        Assert.Single(Regex.Matches(body, RealSuspenderConstruction));

        string code = SourceCalls.WithoutCommentsAndStrings(source);
        Assert.Single(Regex.Matches(code, RealWriterConstruction));
        Assert.Single(Regex.Matches(code, RealSuspenderConstruction));

        foreach (string file in RepositoryPaths.Enumerate(Path.Combine("src", "UefiAccess.Cli"), "*.cs"))
        {
            if (!file.EndsWith(Path.Combine("Services", "CliPlatform.cs"), StringComparison.OrdinalIgnoreCase))
            {
                string other = SourceCalls.WithoutCommentsAndStrings(File.ReadAllText(file));
                Assert.DoesNotMatch(RealSuspenderConstruction, other);
                Assert.DoesNotMatch(@"\bnew\s+FirmwareVariableWriter\s*\(", other);
            }
        }
    }

    // ---- helpers ---------------------------------------------------------------------------

    /// <summary>Asserts that <paramref name="pattern"/> matches <paramref name="code"/> exactly once; returns where.</summary>
    private static int SingleIndex(string code, string pattern)
    {
        MatchCollection matches = Regex.Matches(code, pattern, RegexOptions.CultureInvariant);
        Assert.True(matches.Count == 1, $"Expected exactly one match of {pattern}; found {matches.Count}.");
        return matches[0].Index;
    }

    /// <summary>One row of the refusal theory: the store (null for the unit-shaped default) and the command line.</summary>
    private static (VariableSet? Set, string[] Args) Row(VariableSet? set, string[] args) => (set, args);

    /// <summary>A write command line for a subcommand, with --json.</summary>
    private static string[] Change(params string[] subcommandAndArguments)
    {
        return new[] { "write" }.Concat(subcommandAndArguments).Append("--json").ToArray();
    }

    /// <summary>Asserts a blocked write document on standard output and its sentence on standard error; returns the document.</summary>
    private static JsonElement AssertBlocked(CliRun run, string blockedBy)
    {
        Assert.Equal(ExitCodes.Blocked, run.ExitCode);
        JsonElement document = ParseSingleDocument(run.Output);
        Assert.Equal(WriteCommand.CommandName, document.GetProperty("command").GetString());
        Assert.False(document.GetProperty("ok").GetBoolean());
        Assert.Equal(ExitCodes.Blocked, document.GetProperty("exitCode").GetInt32());
        Assert.Equal(blockedBy, document.GetProperty("blockedBy").GetString());
        Assert.False(document.GetProperty("apiCalled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("api").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.GetProperty("after").ValueKind);
        JsonElement record = document.GetProperty("record");
        Assert.Equal(nameof(WriteRecordOutcome.Blocked), record.GetProperty("outcome").GetString());
        Assert.Equal(blockedBy, record.GetProperty("blockedBy").GetString());
        Assert.False(record.GetProperty("apiCalled").GetBoolean());
        Assert.Contains(document.GetProperty("checks").EnumerateArray(), check => IsFailed(check, blockedBy));
        string sentence = Assert.IsType<string>(document.GetProperty("sentence").GetString());
        Assert.Contains(sentence + "\n", run.Error, StringComparison.Ordinal);
        Assert.Equal(0, WriteCommand.CancelKeyHandlersInstalled);
        return document;
    }

    private static bool IsFailed(JsonElement check, string name)
    {
        return check.GetProperty("name").GetString() == name && !check.GetProperty("passed").GetBoolean();
    }

    private static string[] EntryNames(JsonElement image)
    {
        return image.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("name").GetString() ?? string.Empty).ToArray();
    }

    private static string[] Warnings(JsonElement document)
    {
        return document.GetProperty("warnings").EnumerateArray().Select(warning => warning.GetString() ?? string.Empty).ToArray();
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    private static byte[] Current(WriteRig rig, string name)
    {
        return Assert.IsType<FirmwareVariable>(rig.Store.Current(name)).Data;
    }

    private static WriteRecord RecordFile(string folder)
    {
        return Assert.IsType<WriteRecord>(WriteRecord.TryParse(File.ReadAllText(Path.Combine(folder, SnapshotStore.RecordFileName))));
    }
}
