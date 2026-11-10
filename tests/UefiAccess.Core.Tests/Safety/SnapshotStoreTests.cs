// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using UefiAccess.Core.Baseline;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Privileges;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Variables;
using Xunit;
using static UefiAccess.Core.Tests.Fixtures.LoadOptionFixtures;
using static UefiAccess.Core.Tests.Safety.SafetyFixtures;

namespace UefiAccess.Core.Tests.Safety;

/// <summary>
/// The snapshot store, in a data folder under the test's own
/// temporary folder (never ProgramData): a folder written by <see cref="SnapshotStore.Create"/>
/// reads back through <see cref="BaselineReader"/>; a tampered byte or hash and a junction inside
/// the folder are refused; a restore is derived for each of the three operations (and refused
/// for a variable off the whitelist); <see cref="SnapshotStore.Newest"/> offers Verified,
/// Unverified, ChangedDespiteError and Pending folders and skips Blocked, Cancelled and
/// ApiRefused ones, newest by the record's UTC time rather than the local folder name; a rewrite
/// after the call that fails leaves the S12 files whole; and nothing is created for a root
/// outside the data folder.
/// </summary>
public sealed class SnapshotStoreTests : IDisposable
{
    private const string FirstId = "20260930-141502-reorder";

    private readonly string _dataDirectory;
    private readonly SnapshotStore _store;

    public SnapshotStoreTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "snapshot-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDirectory);
        _store = new SnapshotStore(Path.Combine(_dataDirectory, SnapshotStore.FolderName), _dataDirectory, _ => null);
    }

    public void Dispose()
    {
        TestFolders.DeleteQuietly(_dataDirectory);
    }

    [Fact]
    public void Create_WritesAFolderThatReadsBackThroughTheBaselineReader()
    {
        VariableSet set = Store();

        SnapshotCreateResult created = _store.Create(Request(FirstId, set, Whitelist.BootOrder));

        Assert.True(created.Succeeded, created.Error);
        string folder = Assert.IsType<string>(created.Folder);
        Assert.Equal(Path.Combine(_store.RootDirectory, FirstId), folder);

        BaselineReadResult before = BaselineReader.Read(Path.Combine(folder, SnapshotStore.BeforeDirectoryName));
        Assert.Null(before.Error);
        Assert.Empty(before.Warnings);
        Assert.Equal(set.Count, before.Variables.Count);

        SnapshotReadResult read = _store.Read(folder);
        Assert.True(read.Succeeded, read.Error);
        SnapshotDocument document = Assert.IsType<SnapshotDocument>(read.Document);
        Assert.Equal(SnapshotStore.SchemaVersion, document.SchemaVersion);
        Assert.Equal(FirstId, document.TransactionId);
        Assert.Equal(Whitelist.BootOrder.Key, document.Target.Key);
        Assert.Equal("02000300", document.Before.Hex);
        Assert.Equal("before/vars/8be4df61-93ca-11d2-aa0d-00e098032b8c_BootOrder.bin", document.Before.File);
        Assert.Equal(set.Count, document.Firmware.VariableCount);
        Assert.Null(document.After);
        Assert.Equal(set.Find("BootOrder", Global)!.Data, read.TargetBefore!.Data);
        Assert.Equal(WriteRecordOutcome.Pending, read.Record!.Outcome);

        // Reading by name under the root finds the same folder.
        Assert.Equal(folder, _store.Read(FirstId).Folder);
    }

    [Fact]
    public void Create_RefusesAFolderThatAlreadyExists_AndANameThatIsNotAPlainFolderName()
    {
        Assert.True(_store.Create(Request(FirstId, Store(), Whitelist.BootOrder)).Succeeded);

        SnapshotCreateResult again = _store.Create(Request(FirstId, Store(), Whitelist.BootOrder));
        SnapshotCreateResult climbing = _store.Create(Request(@"..\outside", Store(), Whitelist.BootOrder));

        Assert.False(again.Succeeded);
        Assert.Null(again.Folder);
        Assert.Equal(CheckSentences.Format(SnapshotStore.FolderExistsFormat, Path.Combine(_store.RootDirectory, FirstId)), again.Error);
        Assert.False(climbing.Succeeded);
        Assert.Null(climbing.Folder);
        Assert.False(Directory.Exists(Path.Combine(_dataDirectory, "outside")));
    }

    [Fact]
    public void Root_OutsideTheDataFolder_IsRefusedAndNothingIsCreated()
    {
        string outside = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "outside-" + Guid.NewGuid().ToString("N"));
        SnapshotStore store = new(outside, _dataDirectory, _ => null);

        string? refusal = store.PrepareRoot();
        SnapshotCreateResult created = store.Create(Request(FirstId, Store(), Whitelist.BootOrder));

        Assert.Equal(CheckSentences.Format(SnapshotStore.OutsideDataFolderFormat, outside, _dataDirectory), refusal);
        Assert.Equal(refusal, created.Error);
        Assert.False(Directory.Exists(outside));
    }

    [Fact]
    public void Read_RefusesABeforeFileWhoseBytesNoLongerMatchTheIndex()
    {
        string folder = _store.Create(Request(FirstId, Store(), Whitelist.BootOrder)).Folder!;
        string file = Path.Combine(folder, SnapshotStore.BeforeDirectoryName, "vars", "8be4df61-93ca-11d2-aa0d-00e098032b8c_BootOrder.bin");
        File.WriteAllBytes(file, new byte[] { 0x03, 0x00, 0x02, 0x00 });

        SnapshotReadResult read = _store.Read(folder);

        Assert.False(read.Succeeded);
        Assert.StartsWith("the before folder does not match its index: ", read.Error);
        Assert.Null(SnapshotStore.DeriveRestore(read, ChangeRequest.AppHost).Request);
    }

    [Fact]
    public void Read_RefusesASnapshotWhoseRecordedHashIsNotTheBeforeFolders()
    {
        string folder = _store.Create(Request(FirstId, Store(), Whitelist.BootOrder)).Folder!;
        string path = Path.Combine(folder, SnapshotStore.SnapshotFileName);
        string tampered = File.ReadAllText(path).Replace(VariableImage.Sha256Of(new byte[] { 0x02, 0x00, 0x03, 0x00 }), new string('0', 64), StringComparison.Ordinal);
        File.WriteAllText(path, tampered);

        SnapshotReadResult read = _store.Read(folder);

        Assert.False(read.Succeeded);
        Assert.Equal(CheckSentences.Format(SnapshotStore.HashMismatchFormat, "BootOrder"), read.Error);
    }

    [Fact]
    public void Read_RefusesABeforeFolderThatIsAJunction()
    {
        string folder = _store.Create(Request(FirstId, Store(), Whitelist.BootOrder)).Folder!;
        string before = Path.Combine(folder, SnapshotStore.BeforeDirectoryName);
        string elsewhere = Path.Combine(_dataDirectory, "elsewhere");
        Directory.Move(before, elsewhere);
        Assert.True(TestLinks.TryCreateJunction(before, elsewhere), "mklink /J could not create the junction");

        SnapshotReadResult read = _store.Read(folder);

        Assert.False(read.Succeeded);
        Assert.Equal(CheckSentences.Format(SnapshotStore.LinkFormat, SnapshotStore.BeforeDirectoryName, folder), read.Error);
    }

    [Fact]
    public void Read_RefusesAFolderThatIsNotOneOfTheRootsOwn()
    {
        string folder = _store.Create(Request(FirstId, Store(), Whitelist.BootOrder)).Folder!;
        string elsewhere = Path.Combine(_dataDirectory, "elsewhere", FirstId);
        CopyFolder(folder, elsewhere);
        string nested = Path.Combine(folder, "nested");
        CopyFolder(folder, nested);

        SnapshotReadResult outside = _store.Read(elsewhere);
        SnapshotReadResult climbing = _store.Read(Path.Combine("..", "elsewhere", FirstId));
        SnapshotReadResult deeper = _store.Read(nested);

        Assert.Equal(CheckSentences.Format(SnapshotStore.OutsideRootFormat, elsewhere, _store.RootDirectory), outside.Error);
        Assert.Equal(CheckSentences.Format(SnapshotStore.OutsideRootFormat, elsewhere, _store.RootDirectory), climbing.Error);
        Assert.Equal(CheckSentences.Format(SnapshotStore.OutsideRootFormat, nested, _store.RootDirectory), deeper.Error);
        Assert.Null(SnapshotStore.DeriveRestore(outside, ChangeRequest.CliHost).Request);
        Assert.True(_store.Read(folder).Succeeded);
    }

    [Fact]
    public void Read_RefusesASnapshotWhoseTargetKeyIsNotItsNameAndGuid()
    {
        string folder = _store.Create(Request(FirstId, Store(Var("BootNext", U16(UsbNumber))), Whitelist.BootOrder)).Folder!;
        string path = Path.Combine(folder, SnapshotStore.SnapshotFileName);

        // The key names BootNext while the name and GUID say BootOrder: the bytes would be taken by the key.
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"Key\": \"" + Whitelist.BootOrder.Key + "\"", "\"Key\": \"" + Whitelist.BootNext.Key + "\"", StringComparison.Ordinal));

        SnapshotReadResult read = _store.Read(folder);

        Assert.False(read.Succeeded);
        Assert.Equal(CheckSentences.Format(SnapshotStore.TargetKeyMismatchFormat, folder, Whitelist.BootNext.Key, "BootOrder", Global.ToString("D")), read.Error);
        Assert.Null(SnapshotStore.DeriveRestore(read, ChangeRequest.CliHost).Request);
    }

    [Fact]
    public void Read_RefusesASnapshotWhoseRecordIsAboutAnotherVariable()
    {
        WriteRecord other = new()
        {
            TransactionId = FirstId,
            Variable = new WriteRecordVariable(Whitelist.BootNext.Key, Whitelist.BootNext.Name, Global.ToString("D")),
            Outcome = WriteRecordOutcome.Verified,
        };
        string folder = _store.Create(Request(FirstId, Store(), Whitelist.BootOrder, record: other)).Folder!;

        SnapshotReadResult read = _store.Read(folder);

        Assert.False(read.Succeeded);
        Assert.Equal(CheckSentences.Format(SnapshotStore.RecordKeyMismatchFormat, folder, Whitelist.BootNext.Key, Whitelist.BootOrder.Key), read.Error);
    }

    [Fact]
    public void DeriveRestore_OfABootOrderSnapshot_IsAReorderToTheRecordedEntries()
    {
        string folder = _store.Create(Request(FirstId, Store(), Whitelist.BootOrder)).Folder!;

        RestoreDerivation derived = SnapshotStore.DeriveRestore(_store.Read(folder), ChangeRequest.AppHost);

        Assert.Null(derived.Refusal);
        ReorderRequest reorder = Assert.IsType<ReorderRequest>(derived.Request);
        Assert.Equal(new ushort[] { WindowsOnPartition1, UsbNumber }, reorder.NewOrder);
        Assert.Equal(ChangeRequest.AppHost, reorder.Host);
        Assert.Equal(folder, reorder.RestoredFrom);
        Assert.False(reorder.AllowNoChange);
    }

    [Fact]
    public void DeriveRestore_OfAOneTimeBootWithAPreImage_PutsTheOldTargetBack()
    {
        VariableSet set = Store(Var("BootNext", U16(UsbNumber)));
        string folder = _store.Create(Request("20260930-141502-bootnext", set, Whitelist.BootNext, ChangeOperation.SetBootNext)).Folder!;

        RestoreDerivation derived = SnapshotStore.DeriveRestore(_store.Read(folder), ChangeRequest.CliHost);

        BootNextRequest next = Assert.IsType<BootNextRequest>(derived.Request);
        Assert.Equal(UsbNumber, next.Target);
        Assert.Equal(folder, next.RestoredFrom);
    }

    [Fact]
    public void DeriveRestore_OfAOneTimeBootWithoutAPreImage_IsAClear()
    {
        string folder = _store.Create(Request("20260930-141502-bootnext", Store(), Whitelist.BootNext, ChangeOperation.SetBootNext)).Folder!;

        SnapshotReadResult read = _store.Read(folder);
        RestoreDerivation derived = SnapshotStore.DeriveRestore(read, ChangeRequest.CliHost);

        Assert.True(read.Succeeded, read.Error);
        Assert.Null(read.TargetBefore);
        ClearBootNextRequest clear = Assert.IsType<ClearBootNextRequest>(derived.Request);
        Assert.Equal(folder, clear.RestoredFrom);
    }

    [Fact]
    public void DeriveRestore_OfAClear_PutsTheOneTimeBootBack()
    {
        VariableSet set = Store(Var("BootNext", U16(WindowsOnPartition1)));
        string folder = _store.Create(Request("20260930-141502-bootnext-clear", set, Whitelist.BootNext, ChangeOperation.ClearBootNext)).Folder!;

        RestoreDerivation derived = SnapshotStore.DeriveRestore(_store.Read(folder), ChangeRequest.CliHost);

        BootNextRequest next = Assert.IsType<BootNextRequest>(derived.Request);
        Assert.Equal(WindowsOnPartition1, next.Target);
    }

    [Fact]
    public void DeriveRestore_RefusesAVariableOffTheWhitelist_AndAMalformedBootOrder()
    {
        WhitelistEntry timeout = new("Timeout", Global, WritableKind.ReorderOnly, 7);
        string offList = _store.Create(Request("20260930-100000-reorder", Store(), timeout)).Folder!;
        string odd = _store.Create(Request("20260930-110000-reorder", Store(Var("BootOrder", new byte[] { 0x02, 0x00, 0x03 })), Whitelist.BootOrder)).Folder!;

        RestoreDerivation refused = SnapshotStore.DeriveRestore(_store.Read(offList), ChangeRequest.CliHost);
        RestoreDerivation malformed = SnapshotStore.DeriveRestore(_store.Read(odd), ChangeRequest.CliHost);

        Assert.Null(refused.Request);
        Assert.Equal(CheckSentences.Format(SnapshotStore.NotWhitelistedFormat, offList, FirmwareVariable.MakeKey(Global, "Timeout")), refused.Refusal);
        Assert.Null(malformed.Request);
        Assert.Equal(CheckSentences.Format(SnapshotStore.MalformedFormat, odd, "BootOrder", CheckSentences.Format(SnapshotStore.NotAListReasonFormat, 3)), malformed.Refusal);
    }

    [Fact]
    public void Newest_OffersPendingAndWrittenFolders_AndSkipsTheOnesThatWroteNothing()
    {
        CreateWithOutcome("20260930-100000-reorder", WriteRecordOutcome.Verified);
        CreateWithOutcome("20260930-110000-reorder", WriteRecordOutcome.Unverified);
        CreateWithOutcome("20260930-120000-reorder", WriteRecordOutcome.Pending);
        CreateWithOutcome("20260930-130000-reorder", WriteRecordOutcome.Blocked);
        CreateWithOutcome("20260930-140000-reorder", WriteRecordOutcome.Cancelled);
        CreateWithOutcome("20260930-150000-reorder", WriteRecordOutcome.ApiRefused);
        CreateWithOutcome("20260930-160000-bootnext", WriteRecordOutcome.Verified, Whitelist.BootNext);

        SnapshotSummary newest = Assert.IsType<SnapshotSummary>(_store.Newest(Whitelist.BootOrder.Key));

        Assert.Equal("20260930-120000-reorder", newest.Name);
        Assert.Equal(WriteRecordOutcome.Pending, newest.Outcome);
        Assert.Equal("20260930-160000-bootnext", _store.Newest(Whitelist.BootNext.Key)!.Name);
        Assert.Equal(7, _store.List().Count);
        Assert.Equal("20260930-160000-bootnext", _store.List()[0].Name);
    }

    /// <summary>
    /// The folder name is local time, so in the hour repeated when daylight saving time ends a
    /// later change can get a name that sorts before an earlier one's: a change at 02:50 summer
    /// time (00:50 UTC), then one at 02:15 winter time (01:15 UTC). Newest and List order by the
    /// record's UTC time, so Restore previous value offers the later one, the previous value; a
    /// folder whose record shows no time comes after every one that does.
    /// </summary>
    [Fact]
    public void Newest_AndList_OrderByTheRecordsUtcTime_NotByTheLocalFolderName()
    {
        CreateWithOutcome("20261025-025000-reorder", WriteRecordOutcome.Verified, at: new DateTimeOffset(2026, 10, 25, 0, 50, 0, TimeSpan.Zero));
        CreateWithOutcome("20261025-021500-reorder", WriteRecordOutcome.Verified, at: new DateTimeOffset(2026, 10, 25, 1, 15, 0, TimeSpan.Zero));
        CreateWithOutcome("20261025-030000-reorder", WriteRecordOutcome.Verified);

        SnapshotSummary newest = Assert.IsType<SnapshotSummary>(_store.Newest(Whitelist.BootOrder.Key));

        Assert.Equal("20261025-021500-reorder", newest.Name);
        Assert.Equal(new[] { "20261025-021500-reorder", "20261025-025000-reorder", "20261025-030000-reorder" }, _store.List().Select(summary => summary.Name));
    }

    [Fact]
    public void List_BreaksATieOfTimesByTheFolderName_NewestFirst()
    {
        DateTimeOffset sameSecond = new(2026, 9, 30, 12, 15, 2, TimeSpan.Zero);
        CreateWithOutcome("20260930-141502-bootnext", WriteRecordOutcome.Verified, Whitelist.BootNext, sameSecond);
        CreateWithOutcome("20260930-141502-reorder", WriteRecordOutcome.Verified, at: sameSecond);

        Assert.Equal(new[] { "20260930-141502-reorder", "20260930-141502-bootnext" }, _store.List().Select(summary => summary.Name));
    }

    /// <summary>
    /// The files rewritten after the call replace the S12 ones only once they are whole: a rewrite
    /// that fails after its temporary file was written (here the S12 file is read-only, so it
    /// cannot be replaced) reports the failure, removes the temporary file, and leaves the S12
    /// snapshot.json and record.json readable, so Restore previous value still offers the folder
    /// as Pending and can restore from it.
    /// </summary>
    [Fact]
    public void ARewriteThatFails_LeavesTheS12FilesWhole_AndTheFolderStillOffered()
    {
        SnapshotCreateResult created = _store.Create(Request(FirstId, Store(), Whitelist.BootOrder));
        string folder = Assert.IsType<string>(created.Folder);
        string snapshotPath = Path.Combine(folder, SnapshotStore.SnapshotFileName);
        string recordPath = Path.Combine(folder, SnapshotStore.RecordFileName);
        string snapshotBefore = File.ReadAllText(snapshotPath);
        string recordBefore = File.ReadAllText(recordPath);
        SnapshotAfter after = new(true, 7, 4, "03000200", new string('c', 64), null, nameof(ReadStatus.Success), true, false);
        WriteRecord verified = new() { TransactionId = FirstId, Variable = new WriteRecordVariable(Whitelist.BootOrder.Key, "BootOrder", Global.ToString("D")), Outcome = WriteRecordOutcome.Verified };

        string? afterProblem;
        string? recordProblem;
        File.SetAttributes(snapshotPath, FileAttributes.ReadOnly);
        File.SetAttributes(recordPath, FileAttributes.ReadOnly);
        try
        {
            afterProblem = _store.WriteAfter(folder, created.Document! with { After = after }, null);
            recordProblem = _store.WriteRecordFile(folder, verified);
        }
        finally
        {
            File.SetAttributes(snapshotPath, FileAttributes.Normal);
            File.SetAttributes(recordPath, FileAttributes.Normal);
        }

        Assert.StartsWith(SnapshotStore.SnapshotFileName + " could not be written in ", afterProblem, StringComparison.Ordinal);
        Assert.StartsWith(SnapshotStore.RecordFileName + " could not be written in ", recordProblem, StringComparison.Ordinal);
        Assert.Equal(snapshotBefore, File.ReadAllText(snapshotPath));
        Assert.Equal(recordBefore, File.ReadAllText(recordPath));
        Assert.Empty(Directory.GetFiles(folder, "*" + SnapshotStore.TemporaryFileSuffix));
        SnapshotReadResult read = _store.Read(folder);
        Assert.True(read.Succeeded, read.Error);
        Assert.Null(read.Document!.After);
        SnapshotSummary newest = Assert.IsType<SnapshotSummary>(_store.Newest(Whitelist.BootOrder.Key));
        Assert.Equal(FirstId, newest.Name);
        Assert.Equal(WriteRecordOutcome.Pending, newest.Outcome);
        Assert.IsType<ReorderRequest>(SnapshotStore.DeriveRestore(read, ChangeRequest.AppHost).Request);

        // Once nothing holds them, the same rewrites replace the files whole, with no temporary file left.
        Assert.Null(_store.WriteAfter(folder, created.Document! with { After = after }, null));
        Assert.Null(_store.WriteRecordFile(folder, verified));
        Assert.NotNull(_store.Read(folder).Document!.After);
        Assert.Equal(WriteRecordOutcome.Verified, _store.Newest(Whitelist.BootOrder.Key)!.Outcome);
        Assert.Empty(Directory.GetFiles(folder, "*" + SnapshotStore.TemporaryFileSuffix, SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(WriteRecordOutcome.Verified, true)]
    [InlineData(WriteRecordOutcome.Unverified, true)]
    [InlineData(WriteRecordOutcome.ChangedDespiteError, true)]
    [InlineData(WriteRecordOutcome.Pending, true)]
    [InlineData(WriteRecordOutcome.Blocked, false)]
    [InlineData(WriteRecordOutcome.Cancelled, false)]
    [InlineData(WriteRecordOutcome.ApiRefused, false)]
    public void Newest_OffersAFolderOnlyWhenTheValueMayHaveChanged(WriteRecordOutcome outcome, bool offered)
    {
        CreateWithOutcome(FirstId, outcome);

        SnapshotSummary? newest = _store.Newest(Whitelist.BootOrder.Key);

        Assert.Equal(offered, newest is not null);
    }

    [Fact]
    public void WriteAfter_KeepsTheReadBackBytes_AndCompletesTheDocument()
    {
        SnapshotCreateResult created = _store.Create(Request(FirstId, Store(), Whitelist.BootOrder));
        string folder = created.Folder!;
        FirmwareVariable readBack = Var("BootOrder", Concat(U16(UsbNumber), U16(WindowsOnPartition1)));
        SnapshotAfter after = new(true, 7, 4, "03000200", VariableImage.Sha256Of(readBack.Data), null, nameof(ReadStatus.Success), true, false);

        string? problem = _store.WriteAfter(folder, created.Document! with { After = after }, readBack);

        Assert.Null(problem);
        string file = Path.Combine(folder, SnapshotStore.AfterDirectoryName, "8be4df61-93ca-11d2-aa0d-00e098032b8c_BootOrder.bin");
        Assert.Equal(readBack.Data, File.ReadAllBytes(file));
        SnapshotDocument document = _store.Read(folder).Document!;
        Assert.Equal("after/8be4df61-93ca-11d2-aa0d-00e098032b8c_BootOrder.bin", document.After!.File);
        Assert.True(document.After.MatchesProposed);
    }

    [Fact]
    public void Document_IsPascalCase_WithEnumsAsStrings()
    {
        string folder = _store.Create(Request(FirstId, Store(), Whitelist.BootOrder)).Folder!;

        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, SnapshotStore.SnapshotFileName)));

        Assert.Equal("Off", json.RootElement.GetProperty("BitLocker").GetProperty("State").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("SchemaVersion").GetInt32());
        Assert.Equal(
            new[] { "SchemaVersion", "TransactionId", "ToolVersion", "TimestampUtc", "TimestampLocal", "Host", "Operation", "IsRestore", "RestoredFrom", "Identity", "Firmware", "Target", "Before", "Proposed", "After", "Context", "Baseline", "BitLocker", "SecureBoot", "PlanHash" },
            json.RootElement.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public void DefaultRoot_IsTheSnapshotsFolderUnderTheDataDirectory()
    {
        Assert.Equal(Path.Combine(Logging.DataDirectory, "snapshots"), SnapshotStore.DefaultRootDirectory);
    }

    private static void CopyFolder(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories).Where(file => !file.StartsWith(destination, StringComparison.OrdinalIgnoreCase)))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private void CreateWithOutcome(string id, WriteRecordOutcome outcome, WhitelistEntry? target = null, DateTimeOffset? at = null)
    {
        WhitelistEntry entry = target ?? Whitelist.BootOrder;
        WriteRecord record = new()
        {
            TimestampUtc = at is DateTimeOffset time ? WriteRecord.FormatTimestamp(time) : string.Empty,
            TransactionId = id,
            Variable = new WriteRecordVariable(entry.Key, entry.Name, entry.Guid.ToString("D")),
            Outcome = outcome,
        };
        SnapshotCreateResult created = _store.Create(Request(id, Store(Var("BootNext", U16(WindowsOnPartition1))), entry, record: record));
        Assert.True(created.Succeeded, created.Error);
    }

    /// <summary>A snapshot request over <paramref name="set"/> for <paramref name="target"/>, as stage S12 builds one; shared with DefenceInDepthTests.</summary>
    internal static SnapshotRequest Request(string id, VariableSet set, WhitelistEntry target, ChangeOperation operation = ChangeOperation.Reorder, WriteRecord? record = null, string? planHash = null)
    {
        FirmwareVariable? current = set.Find(target.Name, target.Guid);
        VariableImage before = current is null ? VariableImage.Absent(7) : VariableImage.FromBytes(current.Data, (uint)current.Attributes);
        return new SnapshotRequest(
            id,
            ChangeRequest.CliHost,
            operation.ToString(),
            false,
            null,
            PlanTime,
            "0.1.0+test",
            Identity(true),
            FirmwareType.Uefi,
            PrivilegeState.Enabled,
            EnumerationMethod.InMemory,
            set.All,
            new SnapshotTarget(target.Key, target.Name, target.Guid.ToString("D")),
            new SnapshotBefore(before.Present, before.Present ? before.Attributes : 0, before.Present ? "NV BS RT" : string.Empty, before.Size, before.Hex, before.Sha256, null, Array.Empty<EntryLine>()),
            new SnapshotProposed(7, before.Size, before.Hex, before.Sha256, Array.Empty<EntryLine>()),
            new SnapshotContext("Boot0002", null, 0, Array.Empty<SnapshotLoadOption>()),
            new SnapshotBaseline(null, null, null),
            new SnapshotBitLocker(BitLockerState.Off, 0, 0, Array.Empty<string>(), null, false, false, null, null, null),
            new SnapshotSecureBoot(true, false, true),
            planHash ?? new string('a', 64),
            null,
            null,
            "snapshot store test",
            record ?? new WriteRecord { TransactionId = id, Variable = new WriteRecordVariable(target.Key, target.Name, target.Guid.ToString("D")), Outcome = WriteRecordOutcome.Pending });
    }
}
