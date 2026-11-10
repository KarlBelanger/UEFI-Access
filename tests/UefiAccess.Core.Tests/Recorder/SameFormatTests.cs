// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Text.RegularExpressions;
using UefiAccess.Core.Diagnostics;
using UefiAccess.UiaTrace.Recording;
using Xunit;

namespace UefiAccess.Core.Tests.Recorder;

/// <summary>
/// The recorder's lines and the application's input-trace lines have one format, so the tester's two
/// files merge exactly: a UIA-RECV line written through <see cref="TraceFileWriter"/> has the same
/// six tab-separated fields as an in-app KEY line (local time HH:mm:ss.fff, milliseconds since the
/// trace started with one decimal, the raw QPC value, "T" and the thread id, the kind, the
/// details), the third column is the event's own QPC value in both files, so sorting the event
/// lines of both by it interleaves them in the order the events happened, and the recorder's TRACE
/// and SUMMARY lines start with "recorder:". Both writers run over a text writer.
/// </summary>
public sealed class SameFormatTests
{
    private static readonly Regex EventLine = new(
        @"^(?<time>\d{2}:\d{2}:\d{2}\.\d{3})\t(?<elapsed>-?\d+\.\d)\t(?<qpc>-?\d+)\tT(?<thread>\d+)\t(?<kind>[A-Z-]+)\t(?<details>[^\t]+)$",
        RegexOptions.CultureInvariant);

    [Fact]
    public void AUiaRecvLine_HasTheSixFieldsOfAKeyLine()
    {
        DateTime start = new(2026, 10, 1, 14, 30, 5, 205, DateTimeKind.Local);
        long startTimestamp = 1_000_000_000;
        long tick = System.Diagnostics.Stopwatch.Frequency / 1000;

        string app = Written("UefiAccess input trace", startTimestamp, start, writer =>
            writer.Write(startTimestamp + (2207 * tick), start.AddMilliseconds(2207), 1, TraceKind.Key, "down Down #12 repeat=no modifiers=None focus=ListItem/ListViewItem id=- row=Boot0001 name=\"Boot0001, EFI global variable\""));
        ElementFacts row = new("ListItem", null, "Boot0002, EFI global variable", 4242, true);
        string recorder = Written(TraceOutput.Title, startTimestamp + (5 * tick), start.AddMilliseconds(5), writer =>
            writer.Write(startTimestamp + (2215 * tick), start.AddMilliseconds(2215), 7, TraceKind.UiaReceived, UiaEventText.Event(UiaEventNames.ElementSelected, row)));

        Match key = Assert.Single(EventLines(app));
        Match received = Assert.Single(EventLines(recorder));

        Assert.Equal(6, key.Value.Split(TraceLine.Separator).Length);
        Assert.Equal(6, received.Value.Split(TraceLine.Separator).Length);
        Assert.Equal("14:30:07.412", key.Groups["time"].Value);
        Assert.Equal("14:30:07.420", received.Groups["time"].Value);
        Assert.Equal("2207.0", key.Groups["elapsed"].Value);
        Assert.Equal("2210.0", received.Groups["elapsed"].Value);
        Assert.Equal((startTimestamp + (2207 * tick)).ToString(CultureInfo.InvariantCulture), key.Groups["qpc"].Value);
        Assert.Equal((startTimestamp + (2215 * tick)).ToString(CultureInfo.InvariantCulture), received.Groups["qpc"].Value);
        Assert.Equal("1", key.Groups["thread"].Value);
        Assert.Equal("7", received.Groups["thread"].Value);
        Assert.Equal("KEY", key.Groups["kind"].Value);
        Assert.Equal("UIA-RECV", received.Groups["kind"].Value);
        Assert.Equal("ElementSelected ListItem id=- name=\"Boot0002, EFI global variable\"", received.Groups["details"].Value);
    }

    [Fact]
    public void TheRecordersLine_IsTraceLineFormatOfTheEventsOwnStamp()
    {
        DateTime start = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Local);
        DateTime local = start.AddSeconds(3.25);
        long startTimestamp = 77_000;
        long timestamp = startTimestamp + (System.Diagnostics.Stopwatch.Frequency * 3);
        string details = UiaEventText.FocusOutside(1234);

        string recorder = Written(TraceOutput.Title, startTimestamp, start, writer => writer.Write(timestamp, local, 9, TraceKind.UiaReceived, details));

        Assert.Equal(TraceLine.Format(local, timestamp, startTimestamp, 9, TraceKind.UiaReceived, details), Assert.Single(EventLines(recorder)).Value);
    }

    [Fact]
    public void SortingBothFilesByTheQpcColumn_InterleavesTheEventsInTheOrderTheyHappened()
    {
        DateTime start = new(2026, 10, 1, 14, 30, 0, DateTimeKind.Local);
        long startTimestamp = 5_000_000;
        long tick = System.Diagnostics.Stopwatch.Frequency / 1000;
        ElementFacts row = new("ListItem", null, "Boot0002", 1, true);

        string app = Written("UefiAccess input trace", startTimestamp, start, writer =>
        {
            writer.Write(startTimestamp + (10 * tick), start, 1, TraceKind.Key, "down Down #1");
            writer.Write(startTimestamp + (14 * tick), start, 1, TraceKind.Selection, "shell: row Boot0002");
            writer.Write(startTimestamp + (30 * tick), start, 1, TraceKind.Key, "up Down #1");
        });
        string recorder = Written(TraceOutput.Title, startTimestamp - (100 * tick), start, writer =>
        {
            writer.Write(startTimestamp + (16 * tick), start, 7, TraceKind.UiaReceived, UiaEventText.Event(UiaEventNames.ElementSelected, row));
            writer.Write(startTimestamp + (18 * tick), start, 7, TraceKind.UiaReceived, UiaEventText.Event(UiaEventNames.FocusChanged, row));
        });

        string[] merged = EventLines(app).Concat(EventLines(recorder))
            .OrderBy(line => long.Parse(line.Groups["qpc"].Value, CultureInfo.InvariantCulture))
            .Select(line => line.Groups["kind"].Value + " " + line.Groups["details"].Value)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "KEY down Down #1",
                "SELECTION shell: row Boot0002",
                "UIA-RECV ElementSelected ListItem id=- name=\"Boot0002\"",
                "UIA-RECV FocusChanged ListItem id=- name=\"Boot0002\"",
                "KEY up Down #1",
            },
            merged);
    }

    [Fact]
    public void TheRecordersSummaryAndTraceLines_StartWithRecorder()
    {
        EventCounts counts = new();
        counts.Add(UiaEventNames.FocusChanged);

        string recorder = Written(TraceOutput.Title, 0, DateTime.Now, writer =>
        {
            foreach (string line in counts.SummaryLines(TimeSpan.FromSeconds(1)))
            {
                writer.Write(TraceKind.Summary, line);
            }

            writer.Write(TraceKind.Trace, "recorder: stopped: UefiAccess closed");
        });

        Match[] lines = EventLines(recorder).ToArray();
        Assert.Equal(1 + 1 + UiaEventNames.SummaryOrder.Count, lines.Length);
        Assert.All(lines, line => Assert.StartsWith("recorder: ", line.Groups["details"].Value, StringComparison.Ordinal));
        Assert.All(lines, line => Assert.Contains(line.Groups["kind"].Value, new[] { TraceKind.Summary, TraceKind.Trace }));
    }

    [Fact]
    public void TheRecordersHeader_IsTheSharedHeaderWithItsOwnTitle()
    {
        string recorder = Written(TraceOutput.Title, 0, DateTime.Now, _ => { });
        string[] lines = Lines(recorder);

        Assert.Equal("# UefiAccess UI Automation trace", lines[0]);
        Assert.All(lines, line => Assert.StartsWith(TraceLine.CommentPrefix, line, StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("QPC frequency " + System.Diagnostics.Stopwatch.Frequency.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains(TraceRedactor.Replacement, StringComparison.Ordinal));
    }

    /// <summary>What a writer over a text writer wrote, after <paramref name="write"/> and Dispose.</summary>
    private static string Written(string title, long startTimestamp, DateTime startLocal, Action<TraceFileWriter> write)
    {
        StringWriter output = new();
        using (TraceFileWriter writer = new(output, title, () => "TESTTAG7", "0.1.0+test", startTimestamp, startLocal, 4242))
        {
            write(writer);
        }

        return output.ToString();
    }

    private static string[] Lines(string text) =>
        text.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0).ToArray();

    private static IEnumerable<Match> EventLines(string text)
    {
        foreach (string line in Lines(text).Where(line => !line.StartsWith(TraceLine.CommentPrefix, StringComparison.Ordinal)))
        {
            Match match = EventLine.Match(line);
            Assert.True(match.Success, "not an event line: " + line);
            yield return match;
        }
    }
}
