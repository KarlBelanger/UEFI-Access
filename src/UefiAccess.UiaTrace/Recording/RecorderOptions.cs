// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;

namespace UefiAccess.UiaTrace.Recording;

/// <summary>The families of UI Automation events the recorder can subscribe to; <see cref="All"/> by default.</summary>
[Flags]
internal enum RecorderEvents
{
    /// <summary>No family.</summary>
    None = 0,

    /// <summary>Focus changes (a global subscription; focus in other processes and in the dialogs of UefiAccess is written without a name).</summary>
    Focus = 1,

    /// <summary>SelectionItem ElementSelected, ElementAddedToSelection and ElementRemovedFromSelection.</summary>
    Selection = 2,

    /// <summary>Structure changes.</summary>
    Structure = 4,

    /// <summary>Live region changes.</summary>
    Live = 8,

    /// <summary>Notification events.</summary>
    Notification = 16,

    /// <summary>Asynchronous content loaded.</summary>
    Async = 32,

    /// <summary>Changes of Name, Value, IsSelected, HasKeyboardFocus and ExpandCollapseState.</summary>
    Properties = 64,

    /// <summary>Every family.</summary>
    All = Focus | Selection | Structure | Live | Notification | Async | Properties,
}

/// <summary>The recorder's exit codes.</summary>
internal static class RecorderExitCodes
{
    /// <summary>Recorded and wrote the trace, or showed the help.</summary>
    public const int Success = 0;

    /// <summary>An unexpected error.</summary>
    public const int Failed = 1;

    /// <summary>The command line could not be read.</summary>
    public const int Usage = 2;

    /// <summary>Not elevated: nothing was recorded.</summary>
    public const int NotElevated = 3;

    /// <summary>No trace file could be created: nothing was recorded.</summary>
    public const int NoOutput = 4;
}

/// <summary>
/// The recorder's command line: <c>--out &lt;path&gt;</c>, <c>--events &lt;list&gt;</c> and
/// <c>--help</c> (also <c>-h</c> and <c>/?</c>). An option and its value are two arguments; the
/// joined "option, equals sign, value" form is refused, as is any argument the recorder does not
/// know. Option names are compared without regard to case. Pure, so the tests compile it as a
/// linked file.
/// </summary>
/// <param name="OutPath">The path given with --out, or null for the default file in the log folder.</param>
/// <param name="Events">The event families to subscribe to.</param>
/// <param name="Help">True when the help was asked for; the other values are then not used.</param>
/// <param name="Error">The sentence that says what is wrong with the command line, or null.</param>
internal sealed record RecorderOptions(string? OutPath, RecorderEvents Events, bool Help, string? Error)
{
    /// <summary>The option that names the trace file or the folder for it.</summary>
    public const string OutOption = "--out";

    /// <summary>The option that limits the subscriptions.</summary>
    public const string EventsOption = "--events";

    /// <summary>The recorder's executable name, as the usage shows it.</summary>
    public const string ToolName = "uefiaccess-uia-trace";

    /// <summary>The sentence added to every command-line error.</summary>
    public const string UsageHint = "Run " + ToolName + " --help for usage.";

    /// <summary>The arguments that ask for the help.</summary>
    public static IReadOnlyList<string> HelpOptions { get; } = new[] { "--help", "-h", "/?" };

    /// <summary>The names --events takes, with their families, in the order the help lists them.</summary>
    public static IReadOnlyList<KeyValuePair<string, RecorderEvents>> EventFamilies { get; } = new[]
    {
        new KeyValuePair<string, RecorderEvents>("focus", RecorderEvents.Focus),
        new KeyValuePair<string, RecorderEvents>("selection", RecorderEvents.Selection),
        new KeyValuePair<string, RecorderEvents>("structure", RecorderEvents.Structure),
        new KeyValuePair<string, RecorderEvents>("live", RecorderEvents.Live),
        new KeyValuePair<string, RecorderEvents>("notification", RecorderEvents.Notification),
        new KeyValuePair<string, RecorderEvents>("async", RecorderEvents.Async),
        new KeyValuePair<string, RecorderEvents>("properties", RecorderEvents.Properties),
    };

    /// <summary>The help text.</summary>
    public static string Usage { get; } = string.Join(
        Environment.NewLine,
        "Usage: " + ToolName + " [--out <path>] [--events <list>]",
        string.Empty,
        "Records the UI Automation events the UefiAccess window raises, as a screen reader in another",
        "process receives them, into a trace file, until UefiAccess closes or Ctrl+C is pressed.",
        "Run it from an elevated prompt (Run as administrator) and start it before UefiAccess.",
        "It sends no input, records no key presses and changes nothing. The Filter box's text is",
        "recorded each time it changes. Besides its trace file it adds a few lines (its start and",
        "stop, never the service tag) to the day's UefiAccess log, uefiaccess-<yyyyMMdd>.log.",
        string.Empty,
        "  --out <path>     The trace file to create, or an existing folder to create it in, as a",
        "                   full path such as C:\\Temp\\uia-trace.log (an elevated prompt starts in",
        "                   C:\\Windows\\System32, so a name alone is refused).",
        "                   Default: uia-trace-<yyyyMMdd-HHmmss>.log in the UefiAccess log folder",
        "                   (normally C:\\ProgramData\\UefiAccess\\logs). An existing file is never overwritten.",
        "  --events <list>  The events to record, separated by commas (default: all of them):",
        "                   " + string.Join(", ", EventFamilies.Select(family => family.Key)) + ".",
        "  --help, -h, /?   Shows this text.");

    /// <summary>The options when the command line is empty: the default file, every family.</summary>
    public static RecorderOptions Default { get; } = new(null, RecorderEvents.All, false, null);

    /// <summary>Reads the command line. Never throws; a problem is returned in <see cref="Error"/>.</summary>
    /// <param name="args">The arguments; null is read as none.</param>
    /// <returns>The options, the help request, or the error.</returns>
    public static RecorderOptions Parse(IReadOnlyList<string>? args)
    {
        if (args is null || args.Count == 0)
        {
            return Default;
        }

        if (args.Any(argument => HelpOptions.Contains(argument?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)))
        {
            return Default with { Help = true };
        }

        string? outPath = null;
        RecorderEvents? events = null;
        for (int i = 0; i < args.Count; i++)
        {
            string argument = args[i]?.Trim() ?? string.Empty;
            if (string.Equals(argument, OutOption, StringComparison.OrdinalIgnoreCase))
            {
                if (outPath is not null)
                {
                    return Failure(OutOption + " is given more than once.");
                }

                if (!TryValue(args, ref i, out string value))
                {
                    return Failure(OutOption + " needs a path after it, for example " + OutOption + " C:\\Temp\\uia-trace.log.");
                }

                outPath = value;
            }
            else if (string.Equals(argument, EventsOption, StringComparison.OrdinalIgnoreCase))
            {
                if (events is not null)
                {
                    return Failure(EventsOption + " is given more than once.");
                }

                if (!TryValue(args, ref i, out string value))
                {
                    return Failure(EventsOption + " needs a list after it, for example " + EventsOption + " focus,selection.");
                }

                string? error = TryParseEvents(value, out RecorderEvents parsed);
                if (error is not null)
                {
                    return Failure(error);
                }

                events = parsed;
            }
            else if (argument.StartsWith('-') && argument.Contains('=', StringComparison.Ordinal))
            {
                string name = argument[..argument.IndexOf('=', StringComparison.Ordinal)];
                return Failure("Give " + name + " and its value as two arguments separated by a space, not joined by an equals sign: " + argument + ".");
            }
            else
            {
                return Failure("Unknown argument: " + (argument.Length == 0 ? "(empty)" : argument) + ".");
            }
        }

        return new RecorderOptions(outPath, events ?? RecorderEvents.All, false, null);
    }

    /// <summary>The families as the trace and the console name them: "focus, selection, ..."; "none" for none.</summary>
    /// <param name="events">The families.</param>
    /// <returns>The names in the help's order.</returns>
    public static string Describe(RecorderEvents events)
    {
        string[] names = EventFamilies.Where(family => events.HasFlag(family.Value)).Select(family => family.Key).ToArray();
        return names.Length == 0 ? "none" : string.Join(", ", names);
    }

    /// <summary>Reads a comma-separated list of family names, or "all".</summary>
    /// <returns>Null on success; otherwise the error sentence.</returns>
    private static string? TryParseEvents(string list, out RecorderEvents events)
    {
        events = RecorderEvents.None;
        string[] names = list.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (names.Length == 0)
        {
            return EventsOption + " needs at least one of: " + string.Join(", ", EventFamilies.Select(family => family.Key)) + ".";
        }

        foreach (string name in names)
        {
            if (string.Equals(name, "all", StringComparison.OrdinalIgnoreCase))
            {
                events |= RecorderEvents.All;
                continue;
            }

            KeyValuePair<string, RecorderEvents> family = EventFamilies.FirstOrDefault(candidate => string.Equals(candidate.Key, name, StringComparison.OrdinalIgnoreCase));
            if (family.Key is null)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} does not know \"{1}\"; use {2} or all.",
                    EventsOption,
                    name,
                    string.Join(", ", EventFamilies.Select(candidate => candidate.Key)));
            }

            events |= family.Value;
        }

        return null;
    }

    /// <summary>Takes the argument after an option as its value; false when there is none or it is another option.</summary>
    private static bool TryValue(IReadOnlyList<string> args, ref int index, out string value)
    {
        value = string.Empty;
        if (index + 1 >= args.Count)
        {
            return false;
        }

        string candidate = args[index + 1]?.Trim() ?? string.Empty;
        if (candidate.Length == 0 || candidate.StartsWith("--", StringComparison.Ordinal) || HelpOptions.Contains(candidate, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        index++;
        value = candidate;
        return true;
    }

    private static RecorderOptions Failure(string error) => new(null, RecorderEvents.All, false, error + " " + UsageHint);
}
