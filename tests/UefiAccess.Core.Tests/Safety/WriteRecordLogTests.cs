// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Diagnostics;
using UefiAccess.Core.Safety;
using Xunit;

namespace UefiAccess.Core.Tests.Safety;

/// <summary>
/// write-log.jsonl, in a folder under the test's own
/// temporary folder: one line per record, created through the folder check and refused with
/// it, never lost to a concurrent append and never glued to a line cut short; the count of
/// writes since Windows started, against a fixed start time and a boot identity, which counts
/// the records that made the call once per transaction, counts the intent of S12 until the
/// transaction's own line follows it, counts a record of this boot whatever its time, counts a
/// line that is not a record as a possible write unless it shows, or the next line gives, an
/// earlier time, and gives no count at all for a file that exists but cannot be read; the commit
/// lock that keeps two commits apart; and the budget that refuses the eighth write of a start.
/// </summary>
public sealed class WriteRecordLogTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 9, 30, 8, 0, 0, TimeSpan.Zero);

    private readonly string _directory;

    public WriteRecordLogTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "write-log-" + Guid.NewGuid().ToString("N"), WriteRecordLog.FolderName);
    }

    public void Dispose()
    {
        TestFolders.DeleteQuietly(Path.GetDirectoryName(_directory)!);
    }

    [Fact]
    public void Append_CreatesTheFolderThroughTheCheck_AndWritesOneLinePerRecord()
    {
        List<string> prepared = new();
        WriteRecordLog log = new(_directory, folder =>
        {
            prepared.Add(folder);
            return null;
        });

        Assert.Null(log.Append(Record("first", Start.AddMinutes(1), apiCalled: true)));
        Assert.Null(log.Append(Record("second", Start.AddMinutes(2), apiCalled: false)));

        Assert.Equal(Path.GetFullPath(_directory), Assert.Single(prepared.Distinct()));
        string[] lines = File.ReadAllText(log.FilePath).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Equal(new[] { "first", "second" }, log.ReadAll().Select(record => record.TransactionId));
        Assert.Equal(Path.Combine(Path.GetFullPath(_directory), WriteRecordLog.FileName), log.FilePath);
    }

    [Fact]
    public void Append_RefusedByTheFolderCheck_WritesNothing_AndSaysWhy()
    {
        WriteRecordLog log = new(_directory, _ => "refused by the test");

        string? problem = log.Append(Record("refused", Start.AddMinutes(1), apiCalled: true));

        Assert.Equal("refused by the test", problem);
        Assert.False(Directory.Exists(_directory));
        Assert.Empty(log.ReadAll());
        Assert.Equal(new WriteBudgetCount(0, null), log.CountSinceStart(Start));
    }

    [Fact]
    public void CountSinceStart_CountsTheCallsMadeAfterTheStart_AndReadsWhatIsNotARecordAsAPossibleWrite()
    {
        WriteRecordLog log = new(_directory, _ => null);
        Assert.Null(log.Append(Record("before-the-start", Start.AddMinutes(-1), apiCalled: true)));
        Assert.Null(log.Append(Record("blocked", Start.AddMinutes(1), apiCalled: false)));
        Assert.Null(log.Append(Record("written-1", Start.AddMinutes(2), apiCalled: true)));
        Assert.Null(log.Append(Record("written-2", Start.AddMinutes(3), apiCalled: true)));

        Assert.Equal(new WriteBudgetCount(2, null), log.CountSinceStart(Start));
        Assert.Equal(3, log.CountSinceStart(Start.AddHours(-1)).Count);
        Assert.Equal(0, log.CountSinceStart(Start.AddHours(1)).Count);

        // A line that is not a record may be a write cut short while it was appended: it counts,
        // unless it shows a timestamp at or before the start, so the budget fails closed. One that
        // shows none was appended before the next line that shows one, and is judged by that time.
        File.AppendAllText(log.FilePath, "not a record\n{\"schema\": \"another-schema/1\", \"apiCalled\": true, \"timestampUtc\": \"2026-09-30T09:00:00Z\"}\n");

        WriteBudgetCount count = log.CountSinceStart(Start);
        Assert.Equal(4, count.Count);
        Assert.Equal(2, count.NotRecords);
        Assert.Equal(5, log.CountSinceStart(Start.AddHours(-1)).Count);
        Assert.Equal(0, log.CountSinceStart(Start.AddHours(1)).Count);
        Assert.Equal(4, log.ReadAll().Count);
        Assert.Equal(2, log.CountLinesThatAreNotRecords());
    }

    [Fact]
    public void CountSinceStart_AFragmentThatShowsNoTime_IsJudgedByTheNextLine_AndCountsOnlyWhileNoLineFollowsIt()
    {
        WriteRecordLog log = new(_directory, _ => null);
        Assert.Null(log.Append(Record("written-in-an-earlier-start", Start.AddMinutes(-30), apiCalled: true)));

        // A power loss during an append can leave a run of zero bytes where the line was; no
        // timestamp can be read from it, and it is not white space.
        File.AppendAllText(log.FilePath, "\0\0\0\0");

        // Nothing after it shows a time: it may be a write of this start, so it counts.
        WriteBudgetCount whileLast = log.CountSinceStart(Start);
        Assert.Equal(1, whileLast.Count);
        Assert.Equal(1, whileLast.NotRecords);

        // The next record starts on a line of its own; the fragment was written before it, so in a
        // later start it no longer takes one of the writes of the budget.
        Assert.Null(log.Append(Record("dry-run-of-an-earlier-start", Start.AddMinutes(-20), apiCalled: false)));

        Assert.Equal(new WriteBudgetCount(0, null), log.CountSinceStart(Start));
        WriteBudgetCount earlierStart = log.CountSinceStart(Start.AddHours(-1));
        Assert.Equal(2, earlierStart.Count);
        Assert.Equal(1, earlierStart.NotRecords);
        Assert.Equal(1, log.CountLinesThatAreNotRecords());
        Assert.Equal(new[] { "written-in-an-earlier-start", "dry-run-of-an-earlier-start" }, log.ReadAll().Select(record => record.TransactionId));
    }

    [Fact]
    public void CountSinceStart_ARecordOfThisBoot_CountsWhereverTheClockPutsTheStart()
    {
        // Three writes of this boot, then the Windows clock stepped forward: now minus the tick
        // count moved past them, but they carry this boot's identity.
        const uint ThisBoot = 41;
        WriteRecordLog log = new(_directory, _ => null, () => ThisBoot);
        for (int i = 0; i < 3; i++)
        {
            Assert.Null(log.Append(Record("written-" + i, Start.AddMinutes(1 + i), apiCalled: true) with { BootId = ThisBoot }));
        }

        Assert.Null(log.Append(Record("another-boot", Start.AddMinutes(4), apiCalled: true) with { BootId = ThisBoot - 1 }));
        DateTimeOffset startAfterTheStep = Start.AddHours(2);

        Assert.Equal(ThisBoot, log.CurrentBootId());
        Assert.Equal(3, log.CountSinceStart(startAfterTheStep).Count);
        Assert.Equal(3, log.CountSinceStart(startAfterTheStep, ThisBoot).Count);

        // Without a boot identity only the clock places a record in this start, as before.
        Assert.Equal(0, log.CountSinceStart(startAfterTheStep, null).Count);

        // Either rule counts a line: a record of another boot still counts by its time, and a boot
        // identity that cannot be read never lowers the count.
        Assert.Equal(4, log.CountSinceStart(Start, ThisBoot).Count);
        Assert.Equal(4, log.CountSinceStart(Start, null).Count);
    }

    [Fact]
    public void CountSinceStart_CountsATransactionOnce_AndItsIntentUntilItsOwnLineFollowsIt()
    {
        WriteRecordLog log = new(_directory, _ => null);

        // A commit that wrote: its intent at S12, then its outcome at S17. One write, not two.
        Assert.Null(log.Append(Intent("written", Start.AddMinutes(1))));
        Assert.Null(log.Append(Record("written", Start.AddMinutes(1), apiCalled: true)));

        // A commit blocked after S12 (a drift read, a suspend that failed): its intent, then Blocked.
        Assert.Null(log.Append(Intent("blocked-at-s14", Start.AddMinutes(2))));
        Assert.Null(log.Append(Record("blocked-at-s14", Start.AddMinutes(2), apiCalled: false)));

        Assert.Equal(new WriteBudgetCount(1, null), log.CountSinceStart(Start));

        // A write whose S17 line is missing (the process was killed after the call, or the list
        // could not take the line): its intent counts.
        Assert.Null(log.Append(Intent("cut-short", Start.AddMinutes(3))));

        Assert.Equal(new WriteBudgetCount(2, null), log.CountSinceStart(Start));

        // The line the transaction appends later replaces its intent; an intent of an earlier
        // start never counts.
        Assert.Null(log.Append(Record("cut-short", Start.AddMinutes(3), apiCalled: true)));

        Assert.Equal(new WriteBudgetCount(2, null), log.CountSinceStart(Start));
        Assert.Equal(new WriteBudgetCount(0, null), log.CountSinceStart(Start.AddMinutes(5)));
    }

    [Fact]
    public void TheDefaultLog_TakesTheBootIdentityFromWindowsBootCounter_AndItsReadNeverThrows()
    {
        // Constructing the default log only computes paths; the boot counter is read from the
        // registry, read-only, each time it is asked for, and the same start gives the same value.
        uint? first = WriteRecordLog.ReadWindowsBootId();

        Assert.Equal(first, WriteRecordLog.ReadWindowsBootId());
        Assert.Equal(first, new WriteRecordLog().CurrentBootId());
        Assert.Null(new WriteRecordLog(_directory, _ => null).CurrentBootId());
        Assert.Null(new WriteRecordLog(_directory, _ => null, () => throw new InvalidOperationException("no counter")).CurrentBootId());
    }

    [Fact]
    public void CountSinceStart_ALineCutShortAfterItsTimestamp_CountsOnlyWhenItIsAfterTheStart()
    {
        WriteRecordLog log = new(_directory, _ => null);
        Assert.Null(log.Append(Record("written", Start.AddMinutes(2), apiCalled: true)));
        string torn = Record("torn", Start.AddMinutes(5), apiCalled: true).ToJsonLine();
        File.AppendAllText(log.FilePath, torn[..(torn.IndexOf("\"transactionId\"", StringComparison.Ordinal) + 20)]);

        Assert.Equal(2, log.CountSinceStart(Start).Count);
        Assert.Equal(0, log.CountSinceStart(Start.AddMinutes(10)).Count);

        // The next record starts on a line of its own, so it is read whole and counted.
        Assert.Null(log.Append(Record("after-the-torn-line", Start.AddMinutes(6), apiCalled: true)));

        Assert.Equal(3, log.CountSinceStart(Start).Count);
        Assert.Equal(new[] { "written", "after-the-torn-line" }, log.ReadAll().Select(record => record.TransactionId));
    }

    [Fact]
    public void CountSinceStart_ARecordOfACallWithoutAReadableTimestamp_Counts()
    {
        WriteRecordLog log = new(_directory, _ => null);
        Assert.Null(log.Append(Record("written", Start.AddMinutes(2), apiCalled: true) with { TimestampUtc = "not a time" }));

        Assert.Equal(1, log.CountSinceStart(Start.AddDays(1)).Count);
    }

    [Fact]
    public void CountSinceStart_OfAFileThatExistsButCannotBeRead_GivesNoCount()
    {
        WriteRecordLog log = new(_directory, _ => null);
        Assert.Null(log.Append(Record("written", Start.AddMinutes(2), apiCalled: true)));

        WriteBudgetCount count;
        int? notRecords;
        using (new FileStream(log.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            count = log.CountSinceStart(Start);
            notRecords = log.CountLinesThatAreNotRecords();
        }

        Assert.Null(notRecords);
        Assert.False(count.IsKnown);
        Assert.NotNull(count.Problem);
        Assert.StartsWith("the file exists but could not be read: ", count.Problem, StringComparison.Ordinal);
        Assert.Equal(new WriteBudgetCount(1, null), log.CountSinceStart(Start));
    }

    [Fact]
    public void CountSinceStart_OfAMissingFile_IsZero()
    {
        WriteRecordLog log = new(_directory, _ => null);

        Assert.Equal(new WriteBudgetCount(0, null), log.CountSinceStart(Start));
        Assert.Empty(log.ReadLast(5));
        Assert.Equal(0, log.CountLinesThatAreNotRecords());
    }

    [Fact]
    public void Append_WaitsForAnotherAppend_InsteadOfLosingItsLine()
    {
        WriteRecordLog log = new(_directory, _ => null);
        WriteRecordLog other = new(_directory, _ => null);
        Assert.Null(log.Append(Record("first", Start, apiCalled: true)));

        Parallel.For(0, 16, i => Assert.Null((i % 2 == 0 ? log : other).Append(Record("parallel-" + i, Start, apiCalled: true))));

        Assert.Equal(17, log.ReadAll().Count);
        Assert.Equal(17, log.CountSinceStart(Start.AddMinutes(-1)).Count);
    }

    [Fact]
    public void TheCommitLock_IsHeldByOneCommitAtATime_AndReleasedOnDispose()
    {
        WriteRecordLog log = new(_directory, _ => null);
        WriteRecordLog sameFile = new(_directory, _ => null);
        WriteRecordLog otherFile = new(Path.Combine(Path.GetDirectoryName(_directory)!, "other-writes"), _ => null);

        IDisposable? first = log.TryEnterCommit(out string? firstProblem);
        Assert.NotNull(first);
        Assert.Null(firstProblem);

        // Another thread (another commit) meets the lock held; a log of another file does not.
        string? secondProblem = null;
        IDisposable? second = null;
        Thread thread = new(() => second = sameFile.TryEnterCommit(out secondProblem));
        thread.Start();
        thread.Join();
        Assert.Null(second);
        Assert.Equal(CheckSentences.AnotherChangeBeingWritten, secondProblem);
        using (IDisposable? unrelated = otherFile.TryEnterCommit(out _))
        {
            Assert.NotNull(unrelated);
        }

        first.Dispose();
        Thread after = new(() =>
        {
            using IDisposable? again = sameFile.TryEnterCommit(out secondProblem);
            second = again;
        });
        after.Start();
        after.Join();
        Assert.NotNull(second);
        Assert.Null(secondProblem);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(6, true)]
    [InlineData(7, false)]
    [InlineData(8, false)]
    public void TheBudget_RefusesTheEighthWriteOfAStart(int writesThisStart, bool withinBudget)
    {
        Assert.Equal(8, WriteRecordLog.WriteBudget);
        Assert.Equal(withinBudget, WriteRecordLog.IsWithinBudget(writesThisStart));
    }

    [Fact]
    public void ReadLast_GivesTheNewestRecordsOldestFirst()
    {
        WriteRecordLog log = new(_directory, _ => null);
        foreach (string id in new[] { "a", "b", "c", "d" })
        {
            Assert.Null(log.Append(Record(id, Start, apiCalled: false)));
        }

        Assert.Equal(new[] { "c", "d" }, log.ReadLast(2).Select(record => record.TransactionId));
        Assert.Equal(4, log.ReadLast(10).Count);
        Assert.Empty(log.ReadLast(0));
    }

    [Fact]
    public void CurrentStartTime_IsNowMinusTheKernelTickCount()
    {
        DateTimeOffset now = new(2026, 9, 30, 14, 15, 2, TimeSpan.Zero);
        long before = Environment.TickCount64;

        DateTimeOffset start = WriteRecordLog.CurrentStartTime(now);

        long after = Environment.TickCount64;
        Assert.InRange(start, now - TimeSpan.FromMilliseconds(after), now - TimeSpan.FromMilliseconds(before));
    }

    [Fact]
    public void DefaultDirectory_IsTheWritesFolderUnderTheDataDirectory()
    {
        Assert.Equal(Path.Combine(Logging.DataDirectory, "writes"), WriteRecordLog.DefaultDirectory);
    }

    private static WriteRecord Record(string id, DateTimeOffset at, bool apiCalled)
    {
        return new WriteRecord
        {
            TimestampUtc = WriteRecord.FormatTimestamp(at),
            TransactionId = id,
            ApiCalled = apiCalled,
            Outcome = apiCalled ? WriteRecordOutcome.Verified : WriteRecordOutcome.Blocked,
        };
    }

    /// <summary>The line stage S12 appends before the call: Pending, the call not made.</summary>
    private static WriteRecord Intent(string id, DateTimeOffset at)
    {
        return new WriteRecord
        {
            TimestampUtc = WriteRecord.FormatTimestamp(at),
            TransactionId = id,
            ApiCalled = false,
            Outcome = WriteRecordOutcome.Pending,
        };
    }
}
