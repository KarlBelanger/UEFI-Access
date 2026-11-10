// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Globalization;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Presentation.ViewModels;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The one line format of the input trace (UefiAccess.App --trace-input) and of the UI Automation
/// recorder (Alpha 2 finding A2-7, the tester's question about the continued A1-6 issue): six fields
/// separated by tabs, in the invariant culture, so that the two files merge on the QPC column and
/// lie side by side with NVDA's log on the local time. The details are cleaned of every control
/// character, names are quoted with inner quotes doubled and cut as the row sentence is cut.
/// </summary>
public sealed class TraceLineTests
{
    /// <summary>U+2028, the Unicode line separator (kept as an escape: a raw one would end the source line).</summary>
    private const string LineSeparator = "\x2028";

    /// <summary>U+2029.</summary>
    private const string ParagraphSeparator = "\x2029";

    private static readonly DateTime At = new(2026, 10, 1, 14, 30, 7, 412, DateTimeKind.Local);

    [Fact]
    public void Format_WritesSixTabSeparatedFields_InOrder()
    {
        long start = 1_000_000;
        long timestamp = start + (2 * Stopwatch.Frequency);

        string line = TraceLine.Format(At, timestamp, start, 7, TraceKind.Key, "down Down #12");

        string[] fields = line.Split(TraceLine.Separator);
        Assert.Equal(6, fields.Length);
        Assert.Equal("14:30:07.412", fields[0]);
        Assert.Equal("2000.0", fields[1]);
        Assert.Equal(timestamp.ToString(CultureInfo.InvariantCulture), fields[2]);
        Assert.Equal("T7", fields[3]);
        Assert.Equal("KEY", fields[4]);
        Assert.Equal("down Down #12", fields[5]);
        Assert.False(line.EndsWith('\t'));
        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
    }

    [Fact]
    public void Format_CleansTheDetails_AndMarksEmptyOnes()
    {
        string line = TraceLine.Format(At, 5, 5, 1, TraceKind.Announce, "first line\r\nsecond\tthird");
        Assert.Equal("first line second third", line.Split(TraceLine.Separator)[5]);
        Assert.Equal(6, line.Split(TraceLine.Separator).Length);

        Assert.Equal(TraceLine.NoDetails, TraceLine.Format(At, 5, 5, 1, TraceKind.Trace, null).Split(TraceLine.Separator)[5]);
        Assert.Equal(TraceLine.NoDetails, TraceLine.Format(At, 5, 5, 1, TraceKind.Trace, " \t ").Split(TraceLine.Separator)[5]);
        Assert.Equal("0.0", TraceLine.Format(At, 5, 5, 1, TraceKind.Trace, "x").Split(TraceLine.Separator)[1]);
    }

    [Theory]
    [InlineData(500, 10_000_000, "0.1")]
    [InlineData(400, 10_000_000, "0.0")]
    [InlineData(123_456, 10_000_000, "12.3")]
    [InlineData(-15_000, 10_000_000, "-1.5")]
    [InlineData(-100, 10_000_000, "0.0")]
    [InlineData(1_000_000_000, 1_000_000_000, "1000.0")]
    public void Milliseconds_HasOneDecimal_RoundsHalfUp_AndAllowsNegatives(long ticks, long frequency, string expected)
    {
        Assert.Equal(expected, TraceLine.Milliseconds(1_000, 1_000 + ticks, frequency));
    }

    [Fact]
    public void Milliseconds_UsesTheStopwatchFrequency()
    {
        Assert.Equal("1000.0", TraceLine.Milliseconds(42, 42 + Stopwatch.Frequency));
        Assert.Equal("0.0", TraceLine.Milliseconds(42, 42));
    }

    [Fact]
    public void Format_IsInvariant_UnderAGermanCulture()
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            long start = 10;
            string line = TraceLine.Format(At, start + (Stopwatch.Frequency * 3 / 2), start, 1234, TraceKind.Idle, "after key #1");
            string[] fields = line.Split(TraceLine.Separator);
            Assert.Equal("14:30:07.412", fields[0]);
            Assert.Equal("1500.0", fields[1]);
            Assert.Equal("T1234", fields[3]);
            Assert.Equal("12.5", TraceLine.Milliseconds(0, 125_000, 10_000_000));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Theory]
    [InlineData("a\r\nb\tc", "a b c")]
    [InlineData("  padded  ", "padded")]
    [InlineData("\u0001start\u0085", "start")]
    [InlineData("one" + LineSeparator + "two" + ParagraphSeparator + "three", "one two three")]
    [InlineData("tab\t\t\ttabs", "tab tabs")]
    [InlineData("", "")]
    public void Clean_CollapsesEveryRunOfControlCharactersToOneSpace(string text, string expected)
    {
        Assert.Equal(expected, TraceLine.Clean(text));
    }

    [Fact]
    public void Clean_OfNull_IsEmpty()
    {
        Assert.Equal(string.Empty, TraceLine.Clean(null));
    }

    [Fact]
    public void Quote_DoublesInnerQuotes_AndCleans()
    {
        Assert.Equal("\"Boot0001, EFI global variable\"", TraceLine.Quote("Boot0001, EFI global variable"));
        Assert.Equal("\"say \"\"hi\"\"\"", TraceLine.Quote("say \"hi\""));
        Assert.Equal("\"a b\"", TraceLine.Quote("a\r\nb"));
        Assert.Equal("\"\"", TraceLine.Quote(null));
    }

    [Fact]
    public void Quote_CutsAt80ByDefault_AndAt200ForARow_WithAnEllipsis()
    {
        string longName = new('a', 300);

        string shortCut = TraceLine.Quote(longName);
        Assert.Equal(TraceLine.ShortNameLength + 2, shortCut.Length);
        Assert.EndsWith("…\"", shortCut, StringComparison.Ordinal);

        string rowCut = TraceLine.Quote(longName, TraceLine.RowNameLength);
        Assert.Equal(TraceLine.RowNameLength + 2, rowCut.Length);
        Assert.EndsWith("…\"", rowCut, StringComparison.Ordinal);

        Assert.Equal("\"" + new string('a', 80) + "\"", TraceLine.Quote(new string('a', 80)));
    }

    [Fact]
    public void Quote_NeverSplitsASurrogatePair()
    {
        // 78 letters, then an emoji (a surrogate pair at 78 and 79), then more: the cut at 80 keeps
        // 79 characters, which would end on the high surrogate, so it keeps 78.
        string text = new string('a', 78) + "\U0001F600" + "bbbb";

        string quoted = TraceLine.Quote(text);

        string inner = quoted[1..^1];
        Assert.Equal(new string('a', 78) + "…", inner);
        foreach (char c in inner)
        {
            Assert.False(char.IsSurrogate(c), "a lone surrogate was left in the cut");
        }
    }

    [Fact]
    public void Token_IsPlainForASimpleName_AndQuotedOtherwise()
    {
        Assert.Equal("Boot0001", TraceLine.Token("Boot0001"));
        Assert.Equal("\"Boot Order\"", TraceLine.Token("Boot Order"));
        Assert.Equal("\"a=b\"", TraceLine.Token("a=b"));
        Assert.Equal("\"\"", TraceLine.Token(""));
        Assert.Equal("\"" + new string('x', 79) + "…\"", TraceLine.Token(new string('x', 100)));
    }

    [Fact]
    public void TheNameCaps_AreTheRowSentencesCap_And80()
    {
        Assert.Equal(VariableItemViewModel.AccessibleNameMaxLength, TraceLine.RowNameLength);
        Assert.Equal(80, TraceLine.ShortNameLength);
        Assert.Equal("HH:mm:ss.fff", TraceLine.TimeFormat);
        Assert.Equal('\t', TraceLine.Separator);
        Assert.Equal("# ", TraceLine.CommentPrefix);
    }

    [Fact]
    public void Header_LinesAreComments_AndNameTheFrequencyTheStartAndTheRedaction()
    {
        IReadOnlyList<string> header = TraceLine.Header("UefiAccess input trace", "0.1.0+abc1234", At, 987_654_321, 4242);

        Assert.True(header.Count >= 5);
        Assert.All(header, line => Assert.StartsWith(TraceLine.CommentPrefix, line, StringComparison.Ordinal));
        Assert.All(header, line => Assert.DoesNotContain('\t', line));
        Assert.Equal("# UefiAccess input trace", header[0]);
        string all = string.Join("\n", header);
        Assert.Contains("0.1.0+abc1234", all, StringComparison.Ordinal);
        Assert.Contains("process 4242", all, StringComparison.Ordinal);
        Assert.Contains("2026-10-01 14:30:07.412", all, StringComparison.Ordinal);
        Assert.Contains("QPC frequency " + Stopwatch.Frequency.ToString(CultureInfo.InvariantCulture), all, StringComparison.Ordinal);
        Assert.Contains("QPC at start 987654321", all, StringComparison.Ordinal);
        Assert.Contains(TraceRedactor.Replacement, all, StringComparison.Ordinal);
        Assert.Contains("NVDA", all, StringComparison.Ordinal);

        IReadOnlyList<string> fixedFrequency = TraceLine.Header("T", "v", At, 1, 1, 10_000_000);
        Assert.Contains(fixedFrequency, line => line.Contains("QPC frequency 10000000 ticks per second", StringComparison.Ordinal));
    }

    [Fact]
    public void TheKinds_AreDistinctUpperCaseWords()
    {
        string[] kinds =
        {
            TraceKind.Trace, TraceKind.Key, TraceKind.Mouse, TraceKind.Focus, TraceKind.Selection, TraceKind.Detail,
            TraceKind.Announce, TraceKind.UiaRaised, TraceKind.Idle, TraceKind.Listeners, TraceKind.UiaReceived, TraceKind.Summary,
        };

        Assert.Equal(kinds.Length, kinds.Distinct(StringComparer.Ordinal).Count());
        Assert.All(kinds, kind => Assert.Matches("^[A-Z][A-Z-]*$", kind));
        Assert.Equal("UIA-RAISED", TraceKind.UiaRaised);
        Assert.Equal("UIA-RECV", TraceKind.UiaReceived);
    }
}
