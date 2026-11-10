// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using UefiAccess.Core.Diagnostics;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The writer both trace files share (the input trace of UefiAccess.App and the UI Automation
/// recorder): callers only queue a stamped line, one background thread writes the lines in queue
/// order through the redactor, the service tag is read on that thread before the first line,
/// nothing is lost at Dispose, nothing is thrown at a caller, and a file is created only where
/// nothing exists. Most tests run over a StringWriter; the file tests use a temporary folder.
/// </summary>
public sealed class TraceFileWriterTests : IDisposable
{
    private const string Tag = "TESTTAG7";
    private const long Start = 1_000;
    private static readonly DateTime StartLocal = new(2026, 10, 1, 14, 30, 0, DateTimeKind.Local);

    private readonly string _tempRoot;

    public TraceFileWriterTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "trace-writer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        TestFolders.DeleteQuietly(_tempRoot);
    }

    [Fact]
    public void Lines_AreWrittenInTheOrderTheyWereQueued_AndAllAreThereAfterDispose()
    {
        StringWriter output = new();
        TraceFileWriter writer = Over(output);
        for (int i = 0; i < 2000; i++)
        {
            writer.Write(TraceKind.Key, "line " + i.ToString(CultureInfo.InvariantCulture));
        }

        writer.Dispose();

        string[] events = EventLines(output.ToString());
        Assert.Equal(2000, events.Length);
        for (int i = 0; i < events.Length; i++)
        {
            string[] fields = events[i].Split(TraceLine.Separator);
            Assert.Equal(6, fields.Length);
            Assert.Equal(TraceKind.Key, fields[4]);
            Assert.Equal("line " + i.ToString(CultureInfo.InvariantCulture), fields[5]);
        }

        Assert.Null(writer.Failure);
        Assert.Equal(Lines(output.ToString()).Length, writer.LinesWritten);
    }

    [Fact]
    public void LinesFromSeveralThreads_KeepEachThreadsOrder_AndCarryTheirThreadId()
    {
        StringWriter output = new();
        TraceFileWriter writer = Over(output);
        ConcurrentDictionary<int, int> threadIds = new();
        Thread[] threads = Enumerable.Range(0, 4).Select(k => new Thread(() =>
        {
            threadIds[k] = Environment.CurrentManagedThreadId;
            for (int i = 0; i < 500; i++)
            {
                writer.Write(TraceKind.Focus, "t" + k.ToString(CultureInfo.InvariantCulture) + " " + i.ToString(CultureInfo.InvariantCulture));
            }
        })).ToArray();
        foreach (Thread thread in threads)
        {
            thread.Start();
        }

        foreach (Thread thread in threads)
        {
            thread.Join();
        }

        writer.Dispose();

        string[] events = EventLines(output.ToString());
        Assert.Equal(2000, events.Length);
        int[] last = { -1, -1, -1, -1 };
        foreach (string line in events)
        {
            string[] fields = line.Split(TraceLine.Separator);
            string[] details = fields[5].Split(' ');
            int k = int.Parse(details[0][1..], CultureInfo.InvariantCulture);
            int i = int.Parse(details[1], CultureInfo.InvariantCulture);
            Assert.Equal(last[k] + 1, i);
            last[k] = i;
            Assert.Equal("T" + threadIds[k].ToString(CultureInfo.InvariantCulture), fields[3]);
        }

        Assert.All(last, value => Assert.Equal(499, value));
    }

    [Fact]
    public void TheServiceTag_IsReadOnTheWriterThreadBeforeTheFirstLine_AndRedactedInEveryLine()
    {
        StringWriter output = new();
        int reads = 0;
        int writtenBeforeRead = -1;
        string? readerThread = null;
        bool readerThreadIsBackground = false;
        TraceFileWriter writer = Over(output, () =>
        {
            Interlocked.Increment(ref reads);
            writtenBeforeRead = output.ToString().Length;
            readerThread = Thread.CurrentThread.Name;
            readerThreadIsBackground = Thread.CurrentThread.IsBackground;
            return "  " + Tag + " ";
        });

        writer.Write(TraceKind.Focus, "row " + Tag + " and " + Tag.ToLowerInvariant());
        writer.Comment("a comment naming " + Tag);
        writer.Dispose();

        string text = output.ToString();
        Assert.Equal(1, reads);
        Assert.Equal(0, writtenBeforeRead);
        Assert.Equal(TraceFileWriter.ThreadName, readerThread);
        Assert.True(readerThreadIsBackground);
        Assert.DoesNotContain(Tag, text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("row [service tag] and [service tag]", text, StringComparison.Ordinal);
        Assert.Contains("# a comment naming [service tag]", text, StringComparison.Ordinal);
        Assert.DoesNotContain(TraceFileWriter.ServiceTagUnreadComment, text, StringComparison.Ordinal);
    }

    [Fact]
    public void AReaderThatThrows_GivesTheCommentLine_AndTheLinesStillCome()
    {
        StringWriter output = new();
        TraceFileWriter writer = Over(output, () => throw new InvalidOperationException("WMI is not there"));
        writer.Write(TraceKind.Key, "down Down #1");
        writer.Dispose();

        string text = output.ToString();
        Assert.Contains("# " + TraceFileWriter.ServiceTagUnreadComment, text, StringComparison.Ordinal);
        Assert.Single(EventLines(text));
        Assert.Null(writer.Failure);
    }

    [Fact]
    public void AReaderThatReturnsNothing_GivesTheCommentLine_AndALaterValueIsStillRedacted()
    {
        StringWriter output = new();
        TraceFileWriter writer = Over(output, () => null);
        writer.Redact("OTHERTAG9");
        writer.Write(TraceKind.Announce, "\"Loaded from a baseline of OTHERTAG9\"");
        writer.Dispose();

        string text = output.ToString();
        Assert.Contains("# " + TraceFileWriter.ServiceTagUnreadComment, text, StringComparison.Ordinal);
        Assert.Contains("[service tag]", Assert.Single(EventLines(text)), StringComparison.Ordinal);
        Assert.DoesNotContain("OTHERTAG9", text, StringComparison.Ordinal);
        Assert.Equal("console [service tag]", writer.RedactText("console othertag9"));
    }

    [Fact]
    public void OnlyTheTextFieldsAreRedacted_SoATagOfDigitsInsideANumberLeavesTheNumbersAlone()
    {
        // A synthetic tag of digits that occurs inside the QPC value of a line and of the header.
        const string Digits = "7654321";
        const long Stamp = 1_076_543_210;
        const long DigitsStart = 9_876_543_210;
        StringWriter output = new();
        TraceFileWriter writer = new(output, "Test trace", () => Digits, "0.1.0+test", DigitsStart, StartLocal, 4242);
        writer.Write(Stamp, StartLocal, 3, TraceKind.Focus, "a name holding " + Digits);
        writer.Comment("a comment holding " + Digits);
        writer.Dispose();

        string line = Assert.Single(EventLines(output.ToString()));
        string[] fields = line.Split(TraceLine.Separator);
        Assert.Equal(Stamp.ToString(CultureInfo.InvariantCulture), fields[2]);
        Assert.Equal(TraceLine.Milliseconds(DigitsStart, Stamp), fields[1]);
        Assert.Equal("a name holding [service tag]", fields[5]);
        Assert.Contains("# a comment holding [service tag]", output.ToString(), StringComparison.Ordinal);
        Assert.Contains(Lines(output.ToString()), header => header.Contains("QPC at start " + DigitsStart.ToString(CultureInfo.InvariantCulture) + ".", StringComparison.Ordinal));
    }

    [Fact]
    public void TheTitleAndTheToolVersionOfTheHeader_AreRedacted()
    {
        StringWriter output = new();
        TraceFileWriter writer = new(output, "Trace of " + Tag, () => Tag, "0.1.0+" + Tag, Start, StartLocal, 4242);
        writer.Dispose();

        string text = output.ToString();
        Assert.DoesNotContain(Tag, text, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("# Trace of [service tag]", text, StringComparison.Ordinal);
        Assert.Contains("Tool version 0.1.0+[service tag], process 4242", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ACallersOwnUnreadComment_ReplacesTheDefaultOne()
    {
        StringWriter output = new();
        TraceFileWriter writer = new(output, "Test trace", () => null, "0.1.0+test", Start, StartLocal, 4242, "Only labelled tags are replaced here.");
        writer.Dispose();

        string text = output.ToString();
        Assert.Contains("# Only labelled tags are replaced here.", text, StringComparison.Ordinal);
        Assert.DoesNotContain(TraceFileWriter.ServiceTagUnreadComment, text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHeaderComesFirst_EveryLineOfItAComment()
    {
        StringWriter output = new();
        TraceFileWriter writer = Over(output, () => Tag);
        writer.Write(TraceKind.Trace, "input trace: started");
        writer.Dispose();

        string[] lines = Lines(output.ToString());
        IReadOnlyList<string> header = TraceLine.Header("Test trace", "0.1.0+test", StartLocal, Start, 4242);
        Assert.Equal(header, lines.Take(header.Count));
        Assert.Equal(header.Count + 1, lines.Length);
        Assert.Equal(Start, writer.StartTimestamp);
        Assert.Equal(StartLocal, writer.StartLocal);
        Assert.Null(writer.FilePath);
    }

    [Fact]
    public void AStampTheCallerCaptured_IsWrittenAsIs()
    {
        StringWriter output = new();
        TraceFileWriter writer = Over(output);
        DateTime local = new(2026, 10, 1, 14, 30, 7, 420, DateTimeKind.Local);
        writer.Write(Start + 123_456, local, 77, TraceKind.UiaReceived, "ElementSelected ListItem id=- name=\"Boot0002\"");
        writer.Dispose();

        Assert.Equal(
            TraceLine.Format(local, Start + 123_456, Start, 77, TraceKind.UiaReceived, "ElementSelected ListItem id=- name=\"Boot0002\""),
            Assert.Single(EventLines(output.ToString())));
    }

    [Fact]
    public void WritesAfterDispose_AreIgnored_AndDisposeIsIdempotent()
    {
        StringWriter output = new();
        TraceFileWriter writer = Over(output);
        writer.Write(TraceKind.Key, "before");
        writer.Dispose();
        long written = writer.LinesWritten;

        writer.Write(TraceKind.Key, "after");
        writer.Write(Start, StartLocal, 1, TraceKind.Key, "after, stamped");
        writer.Comment("after, a comment");
        writer.Redact("LATETAG1");
        writer.Dispose();

        Assert.Equal(written, writer.LinesWritten);
        Assert.DoesNotContain("after", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AnOutputThatFails_StopsTheWriting_WithoutReachingTheCaller()
    {
        FailingWriter output = new();
        TraceFileWriter writer = Over(output);
        for (int i = 0; i < 100; i++)
        {
            writer.Write(TraceKind.Key, "line");
        }

        Exception? thrown = Record.Exception(writer.Dispose);

        Assert.Null(thrown);
        Assert.IsType<IOException>(writer.Failure);
        Assert.Equal(0, writer.LinesWritten);
    }

    [Fact]
    public void CreateNew_WritesUtf8WithoutAByteOrderMark_WithCrLf_AndFlushesWhenTheQueueIsEmpty()
    {
        string path = Path.Combine(_tempRoot, "trace-input-20261001-143000.log");
        TraceFileWriter writer = TraceFileWriter.CreateNew(path, "UefiAccess input trace", () => Tag);
        try
        {
            Assert.Equal(Path.GetFullPath(path), writer.FilePath);
            writer.Write(TraceKind.Key, "down Down #1 on " + Tag);

            // The writer flushes whenever its queue is empty: the line is in the file before Dispose.
            Assert.True(SpinWait.SpinUntil(() => ReadShared(path).Contains("down Down #1", StringComparison.Ordinal), TimeSpan.FromSeconds(10)), "the line was not flushed while the trace ran");
        }
        finally
        {
            writer.Dispose();
        }

        byte[] bytes = File.ReadAllBytes(path);
        Assert.Equal((byte)'#', bytes[0]);
        string text = Encoding.UTF8.GetString(bytes);
        Assert.StartsWith("# UefiAccess input trace\r\n", text, StringComparison.Ordinal);
        Assert.EndsWith("\r\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Tag, text, StringComparison.Ordinal);
        Assert.Contains("down Down #1 on [service tag]", text, StringComparison.Ordinal);
        Assert.Null(writer.Failure);
    }

    [Fact]
    public void CreateNew_RefusesAnExistingFile_AndLeavesItAsItWas()
    {
        string path = Path.Combine(_tempRoot, "trace-input-20261001-143000.log");
        File.WriteAllText(path, "keep");

        Assert.ThrowsAny<IOException>(() => TraceFileWriter.CreateNew(path, "UefiAccess input trace", () => null));

        Assert.Equal("keep", File.ReadAllText(path));
    }

    [Fact]
    public void CreateInFolder_TakesTheNextNameWhileTheNameBeforeExists()
    {
        DateTime now = new(2026, 10, 1, 14, 30, 7, DateTimeKind.Local);
        File.WriteAllText(Path.Combine(_tempRoot, TraceFileWriter.FileName("trace-input-", now, 1)), "first");
        Directory.CreateDirectory(Path.Combine(_tempRoot, TraceFileWriter.FileName("trace-input-", now, 2)));

        using TraceFileWriter writer = TraceFileWriter.CreateInFolder(_tempRoot, "trace-input-", now, "UefiAccess input trace", () => null);

        Assert.Equal(Path.Combine(_tempRoot, "trace-input-20261001-143007-3.log"), writer.FilePath);
        Assert.Equal("first", File.ReadAllText(Path.Combine(_tempRoot, "trace-input-20261001-143007.log")));
    }

    [Fact]
    public void CreateInFolder_InAMissingFolder_Throws()
    {
        string missing = Path.Combine(_tempRoot, "missing");

        Assert.ThrowsAny<IOException>(() => TraceFileWriter.CreateInFolder(missing, "trace-input-", DateTime.Now, "UefiAccess input trace", () => null));
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public void FileName_IsThePrefixTheDateAndTheTime_WithTheAttemptFromTheSecond()
    {
        DateTime now = new(2026, 10, 1, 9, 5, 3, DateTimeKind.Local);

        Assert.Equal("uia-trace-20261001-090503.log", TraceFileWriter.FileName("uia-trace-", now, 1));
        Assert.Equal("trace-input-20261001-090503-2.log", TraceFileWriter.FileName("trace-input-", now, 2));
        Assert.Equal("trace-input-20261001-090503-9.log", TraceFileWriter.FileName("trace-input-", now, 9));
    }

    private static TraceFileWriter Over(TextWriter output, Func<string?>? readServiceTag = null)
    {
        return new TraceFileWriter(output, "Test trace", readServiceTag ?? (() => null), "0.1.0+test", Start, StartLocal, 4242);
    }

    private static string[] Lines(string text)
    {
        return text.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0).ToArray();
    }

    private static string[] EventLines(string text)
    {
        return Lines(text).Where(line => !line.StartsWith(TraceLine.CommentPrefix, StringComparison.Ordinal)).ToArray();
    }

    /// <summary>The file's text, read while the writer still holds it open for writing.</summary>
    private static string ReadShared(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>A text writer whose every write fails, as a full or vanished disk would.</summary>
    private sealed class FailingWriter : StringWriter
    {
        public override void WriteLine(string? value) => throw new IOException("There is not enough space on the disk.");

        public override void Flush() => throw new IOException("There is not enough space on the disk.");
    }
}
