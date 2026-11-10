// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace UefiAccess.Core.Diagnostics;

/// <summary>
/// The event kinds of the two trace files the tester's A2-7 question (the continued A1-6 issue) asked
/// for: the input trace that UefiAccess.App writes when it is started with --trace-input, and the
/// UI Automation trace of the out-of-process recorder (uefiaccess-uia-trace). The kind is the fifth
/// column of every event line (<see cref="TraceLine.Format"/>). TRACE and SUMMARY lines start
/// their details with "input trace:" or "recorder:", so they stay apart when the two files are
/// merged.
/// </summary>
public static class TraceKind
{
    /// <summary>The trace itself: started, stopped, the window activated or deactivated, a hook that failed, an unhandled exception that ends the process.</summary>
    public const string Trace = "TRACE";

    /// <summary>A key went down or came up in the application's window, and when its handling finished.</summary>
    public const string Key = "KEY";

    /// <summary>A mouse button went down or came up in the application's window.</summary>
    public const string Mouse = "MOUSE";

    /// <summary>Keyboard focus moved inside the window, or out of it.</summary>
    public const string Focus = "FOCUS";

    /// <summary>The variable list's selection changed, or a group header got focus.</summary>
    public const string Selection = "SELECTION";

    /// <summary>A step of the detail pane's scheduling: hold, release, post, quiet period, idle turn, flush and rebuild.</summary>
    public const string Detail = "DETAIL";

    /// <summary>A sentence the application wrote into its Status box (the polite live region).</summary>
    public const string Announce = "ANNOUNCE";

    /// <summary>A UI Automation event the application raised itself, or chose not to raise, and why.</summary>
    public const string UiaRaised = "UIA-RAISED";

    /// <summary>The window's UI thread reached ApplicationIdle priority after a key went down or a rebuild ran: how long it was busy.</summary>
    public const string Idle = "IDLE";

    /// <summary>The UI Automation events a client in any process listens for, as WPF sees them.</summary>
    public const string Listeners = "LISTENERS";

    /// <summary>A UI Automation event the out-of-process recorder received.</summary>
    public const string UiaReceived = "UIA-RECV";

    /// <summary>The counts written when a trace stops.</summary>
    public const string Summary = "SUMMARY";
}

/// <summary>
/// The one line format of both trace files, so that a line of the application's input trace and a
/// line of the UI Automation recorder have the same shape and the two files merge exactly: sort
/// both by the third column. An event line is six fields separated by single tab characters, in
/// the invariant culture and with no tab after the last field:
/// <list type="number">
/// <item><description>the local time of the event, HH:mm:ss.fff (the form of NVDA's log, so the files can be laid side by side with it);</description></item>
/// <item><description>the milliseconds since this file's trace started, measured on the QPC clock, with one decimal (0.1 ms);</description></item>
/// <item><description>the raw <see cref="Stopwatch.GetTimestamp"/> value (QueryPerformanceCounter, one clock for every process on the machine);</description></item>
/// <item><description>"T" and the managed thread id;</description></item>
/// <item><description>the event kind (<see cref="TraceKind"/>);</description></item>
/// <item><description>the details: carriage returns, line feeds, tabs and every other control character collapsed to one space, element names in double quotes with inner quotes doubled; "-" when there are none.</description></item>
/// </list>
/// Header and comment lines start with <see cref="CommentPrefix"/> and are never event lines.
/// Nothing here reads a clock: <see cref="Milliseconds(long, long)"/> and
/// <see cref="Header(string, string, DateTime, long, int)"/> read only <see cref="Stopwatch.Frequency"/>,
/// and the header the local time zone's offset; every time stamp is the caller's.
/// </summary>
public static class TraceLine
{
    /// <summary>The field separator: one tab character.</summary>
    public const char Separator = '\t';

    /// <summary>The start of every header and comment line.</summary>
    public const string CommentPrefix = "# ";

    /// <summary>The longest name an element other than a list row may contribute, in characters (a longer one is cut with "…").</summary>
    public const int ShortNameLength = 80;

    /// <summary>
    /// The longest name a list row may contribute: the cap of the row's accessible name
    /// (VariableItemViewModel.AccessibleNameMaxLength in UefiAccess.Presentation), which is all of a
    /// firmware value a trace line may ever hold.
    /// </summary>
    public const int RowNameLength = 200;

    /// <summary>The format of the first field, the local time: HH:mm:ss.fff.</summary>
    public const string TimeFormat = "HH:mm:ss.fff";

    /// <summary>The details field of an event without details.</summary>
    public const string NoDetails = "-";

    /// <summary>The characters that make <see cref="Token"/> quote a text.</summary>
    private static readonly char[] TokenBreakers = { ' ', '"', '=' };

    /// <summary>
    /// Formats one event line.
    /// </summary>
    /// <param name="localTime">The local time captured with the event.</param>
    /// <param name="timestamp">The <see cref="Stopwatch.GetTimestamp"/> value captured with the event.</param>
    /// <param name="startTimestamp">The <see cref="Stopwatch.GetTimestamp"/> value at which the trace started.</param>
    /// <param name="threadId">The managed id of the thread the event happened on.</param>
    /// <param name="kind">The event kind (<see cref="TraceKind"/>).</param>
    /// <param name="details">The details; cleaned of control characters here.</param>
    /// <returns>The line, without a line break.</returns>
    public static string Format(DateTime localTime, long timestamp, long startTimestamp, int threadId, string kind, string? details)
    {
        string cleanKind = Clean(kind);
        string cleanDetails = Clean(details);
        StringBuilder line = new(48 + cleanKind.Length + cleanDetails.Length);
        line.Append(localTime.ToString(TimeFormat, CultureInfo.InvariantCulture)).Append(Separator)
            .Append(Milliseconds(startTimestamp, timestamp)).Append(Separator)
            .Append(timestamp.ToString(CultureInfo.InvariantCulture)).Append(Separator)
            .Append('T').Append(threadId.ToString(CultureInfo.InvariantCulture)).Append(Separator)
            .Append(cleanKind.Length == 0 ? "?" : cleanKind).Append(Separator)
            .Append(cleanDetails.Length == 0 ? NoDetails : cleanDetails);
        return line.ToString();
    }

    /// <summary>
    /// The time between two <see cref="Stopwatch.GetTimestamp"/> values in milliseconds, with one
    /// decimal, in the invariant culture ("12.3"); negative when <paramref name="toTimestamp"/> is
    /// the earlier one.
    /// </summary>
    /// <param name="fromTimestamp">The earlier timestamp.</param>
    /// <param name="toTimestamp">The later timestamp.</param>
    /// <returns>The milliseconds as text.</returns>
    public static string Milliseconds(long fromTimestamp, long toTimestamp)
    {
        return Milliseconds(fromTimestamp, toTimestamp, Stopwatch.Frequency);
    }

    /// <summary>Testable core of <see cref="Milliseconds(long, long)"/> with the clock's frequency given.</summary>
    /// <param name="fromTimestamp">The earlier timestamp.</param>
    /// <param name="toTimestamp">The later timestamp.</param>
    /// <param name="frequency">Ticks per second; a value below 1 is taken as 1.</param>
    /// <returns>The milliseconds as text, "0.0" rather than "-0.0".</returns>
    internal static string Milliseconds(long fromTimestamp, long toTimestamp, long frequency)
    {
        double milliseconds = (toTimestamp - fromTimestamp) * 1000.0 / Math.Max(1, frequency);
        string text = milliseconds.ToString("F1", CultureInfo.InvariantCulture);
        return text == "-0.0" ? "0.0" : text;
    }

    /// <summary>
    /// The text with every run of control characters (carriage return, line feed, tab and the rest,
    /// and the Unicode line and paragraph separators) replaced by one space, trimmed.
    /// </summary>
    /// <param name="text">The text; null gives an empty string.</param>
    /// <returns>The cleaned text.</returns>
    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        int first = -1;
        for (int i = 0; i < text.Length; i++)
        {
            if (IsBreaking(text[i]))
            {
                first = i;
                break;
            }
        }

        if (first < 0)
        {
            return text.Trim();
        }

        StringBuilder cleaned = new(text.Length);
        cleaned.Append(text, 0, first);
        bool inRun = false;
        for (int i = first; i < text.Length; i++)
        {
            char c = text[i];
            if (IsBreaking(c))
            {
                if (!inRun)
                {
                    cleaned.Append(' ');
                    inRun = true;
                }

                continue;
            }

            inRun = false;
            cleaned.Append(c);
        }

        return cleaned.ToString().Trim();
    }

    /// <summary>
    /// The text cleaned (<see cref="Clean"/>), cut to <paramref name="maxLength"/> characters with
    /// "…" as the last one (the cut of DetailFieldViewModel.Truncate in UefiAccess.Presentation,
    /// which never splits a surrogate pair), and put in double quotes with every inner double quote
    /// doubled.
    /// </summary>
    /// <param name="text">The text, typically an accessible name.</param>
    /// <param name="maxLength">The longest text kept inside the quotes, at least 1; <see cref="ShortNameLength"/> by default, <see cref="RowNameLength"/> for a list row.</param>
    /// <returns>The quoted text, "\"\"" for an empty one.</returns>
    public static string Quote(string? text, int maxLength = ShortNameLength)
    {
        string cut = Truncate(Clean(text), Math.Max(1, maxLength));
        return "\"" + cut.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    /// <summary>
    /// The text as one token of a details field: unquoted when it is not empty, holds no space,
    /// double quote or equals sign and fits <paramref name="maxLength"/> (a variable name such as
    /// Boot0001, an AutomationId), otherwise <see cref="Quote"/>d.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="maxLength">The longest text kept, at least 1.</param>
    /// <returns>The token.</returns>
    public static string Token(string? text, int maxLength = ShortNameLength)
    {
        string cleaned = Clean(text);
        bool plain = cleaned.Length > 0
            && cleaned.Length <= Math.Max(1, maxLength)
            && cleaned.IndexOfAny(TokenBreakers) < 0;
        return plain ? cleaned : Quote(cleaned, maxLength);
    }

    /// <summary>
    /// The header of a trace file, every line starting with <see cref="CommentPrefix"/>: the title,
    /// the tool version, the process and the start date and time, the QPC frequency and the QPC
    /// value at the start, a legend of the columns, and the note that the service tag is replaced.
    /// </summary>
    /// <param name="title">The title, for example "UefiAccess input trace".</param>
    /// <param name="toolVersion">The tool version (<see cref="ToolVersion.Current"/>).</param>
    /// <param name="startLocal">The local date and time the trace started.</param>
    /// <param name="startTimestamp">The <see cref="Stopwatch.GetTimestamp"/> value the trace started at.</param>
    /// <param name="processId">The id of the process that writes the file.</param>
    /// <returns>The header lines, without line breaks.</returns>
    public static IReadOnlyList<string> Header(string title, string toolVersion, DateTime startLocal, long startTimestamp, int processId)
    {
        return Header(title, toolVersion, startLocal, startTimestamp, processId, Stopwatch.Frequency);
    }

    /// <summary>Testable core of <see cref="Header(string, string, DateTime, long, int)"/> with the clock's frequency given.</summary>
    /// <param name="title">The title.</param>
    /// <param name="toolVersion">The tool version.</param>
    /// <param name="startLocal">The local date and time the trace started.</param>
    /// <param name="startTimestamp">The QPC value the trace started at.</param>
    /// <param name="processId">The id of the writing process.</param>
    /// <param name="frequency">The QPC frequency in ticks per second.</param>
    /// <returns>The header lines.</returns>
    internal static IReadOnlyList<string> Header(string title, string toolVersion, DateTime startLocal, long startTimestamp, int processId, long frequency)
    {
        CultureInfo invariant = CultureInfo.InvariantCulture;
        return new[]
        {
            CommentPrefix + Clean(title),
            CommentPrefix + "Tool version " + Clean(toolVersion) + ", process " + processId.ToString(invariant)
                + ", started " + startLocal.ToString("yyyy-MM-dd HH:mm:ss.fff", invariant) + " local time (UTC" + UtcOffset(startLocal) + ").",
            CommentPrefix + "QPC frequency " + frequency.ToString(invariant) + " ticks per second; QPC at start " + startTimestamp.ToString(invariant) + ".",
            CommentPrefix + "Columns, separated by tabs: local time (" + TimeFormat + ", the form of NVDA's log); milliseconds since this trace started;"
                + " QPC timestamp (Stopwatch.GetTimestamp, the same clock in every process on this machine: sort two trace files together by this column to merge them);"
                + " thread (T and the managed thread id); event kind; details.",
            CommentPrefix + "Names in double quotes are accessible names, inner quotes doubled; a list row's is cut at "
                + RowNameLength.ToString(invariant) + " characters, any other at " + ShortNameLength.ToString(invariant) + ".",
            CommentPrefix + "The service tag is replaced by " + TraceRedactor.Replacement + " in every line.",
        };
    }

    /// <summary>
    /// Cuts text to <paramref name="maxLength"/> characters, the last one "…", never splitting a
    /// surrogate pair (the same cut as DetailFieldViewModel.Truncate in UefiAccess.Presentation).
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="maxLength">The longest result, at least 1.</param>
    /// <returns>The text, or its first characters followed by "…".</returns>
    internal static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text ?? string.Empty;
        }

        int keep = Math.Max(0, maxLength - 1);
        if (keep > 0 && char.IsHighSurrogate(text[keep - 1]))
        {
            keep--;
        }

        return text[..keep] + "…";
    }

    /// <summary>True for a character a details field must not hold: a control character or a Unicode line or paragraph separator.</summary>
    private static bool IsBreaking(char c) => char.IsControl(c) || c == (char)0x2028 || c == (char)0x2029;

    /// <summary>The offset of local time from UTC at <paramref name="local"/>, as "+hh:mm" or "-hh:mm".</summary>
    private static string UtcOffset(DateTime local)
    {
        TimeSpan offset;
        try
        {
            offset = TimeZoneInfo.Local.GetUtcOffset(local);
        }
        catch (Exception)
        {
            offset = TimeSpan.Zero;
        }

        string sign = offset < TimeSpan.Zero ? "-" : "+";
        TimeSpan magnitude = offset.Duration();
        return sign + magnitude.Hours.ToString("00", CultureInfo.InvariantCulture) + ":" + magnitude.Minutes.ToString("00", CultureInfo.InvariantCulture);
    }
}
