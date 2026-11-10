// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using UefiAccess.Presentation.Infrastructure;

namespace UefiAccess.Presentation.ViewModels;

/// <summary>
/// One line of the log panel: level, time and message. The level is spoken, so the panel uses one
/// short vocabulary (<see cref="LogViewModel.InfoLevel"/>, <see cref="LogViewModel.WarnLevel"/>,
/// <see cref="LogViewModel.ErrorLevel"/>, ...) and the constructor maps Serilog's level names
/// ("Information", "Warning", "Error", ...) onto it, so a line mirrored from the application
/// logger reads like a line the shell wrote itself. The level, the timestamp and the message never
/// change. What the panel shows of the timestamp does: the spoken line keeps it short with the
/// time alone, and the log adds the date (<see cref="ShowsDate"/>) to every line from an earlier
/// day than its newest line, so a session that runs past midnight (or through a clock change)
/// never reads as out of order. Copy log (<see cref="ToString"/>) always writes the full date,
/// time and offset, so a pasted log can be matched to the dated log file.
/// </summary>
public sealed class LogLineViewModel : ObservableObject
{
    /// <summary>The format of <see cref="Time"/> for a line of the newest line's day.</summary>
    public const string TimeFormat = "HH:mm:ss";

    /// <summary>The format of <see cref="Time"/> for a line from an earlier day.</summary>
    public const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>The format of the timestamp Copy log writes: date, time and UTC offset, like the log file without its milliseconds.</summary>
    public const string CopyTimestampFormat = "yyyy-MM-dd HH:mm:ss zzz";

    private bool _showsDate;

    /// <summary>Creates the line.</summary>
    /// <param name="time">When the line was written; shown in the offset it carries.</param>
    /// <param name="level">The level: a panel level such as "INFO", or a Serilog level name such as "Information" (normalized, see <see cref="NormalizeLevel"/>); null is treated as an empty string.</param>
    /// <param name="message">The message; null is treated as an empty string.</param>
    public LogLineViewModel(DateTimeOffset time, string level, string message)
    {
        Timestamp = time;
        Level = NormalizeLevel(level);
        Message = message ?? string.Empty;
    }

    /// <summary>The full timestamp.</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    /// True when the panel shows the date with the time: the log sets it on a line whose day is
    /// earlier (or later, after the clock was set back) than the day of its newest line.
    /// </summary>
    public bool ShowsDate
    {
        get => _showsDate;
        internal set
        {
            if (SetProperty(ref _showsDate, value))
            {
                OnPropertyChanged(nameof(Time));
                OnPropertyChanged(nameof(AccessibleName));
            }
        }
    }

    /// <summary>The time as the panel shows it: "HH:mm:ss", or "yyyy-MM-dd HH:mm:ss" when <see cref="ShowsDate"/> is true.</summary>
    public string Time => Timestamp.ToString(ShowsDate ? DateTimeFormat : TimeFormat, CultureInfo.InvariantCulture);

    /// <summary>The level, in the panel's vocabulary.</summary>
    public string Level { get; }

    /// <summary>The message.</summary>
    public string Message { get; }

    /// <summary>The spoken line: "level time: message".</summary>
    public string AccessibleName => Level + " " + Time + ": " + Message;

    /// <summary>
    /// Maps a level name onto the panel's vocabulary: Serilog's "Verbose", "Debug",
    /// "Information", "Warning", "Error" and "Fatal" (any casing) become "VERBOSE", "DEBUG",
    /// "INFO", "WARN", "ERROR" and "FATAL"; a name already in the vocabulary, or any other
    /// text, is kept as given (trimmed); null becomes an empty string.
    /// </summary>
    /// <param name="level">The level name.</param>
    /// <returns>The panel level.</returns>
    public static string NormalizeLevel(string? level)
    {
        string text = level?.Trim() ?? string.Empty;
        return text.ToUpperInvariant() switch
        {
            "VERBOSE" => "VERBOSE",
            "DEBUG" => "DEBUG",
            "INFORMATION" or "INFO" => LogViewModel.InfoLevel,
            "WARNING" or "WARN" => LogViewModel.WarnLevel,
            "ERROR" => LogViewModel.ErrorLevel,
            "FATAL" => "FATAL",
            _ => text,
        };
    }

    /// <summary>The line as written by Copy log: "yyyy-MM-dd HH:mm:ss zzz level message", whatever the panel shows.</summary>
    /// <returns>The line.</returns>
    public override string ToString() => Timestamp.ToString(CopyTimestampFormat, CultureInfo.InvariantCulture) + " " + Level + " " + Message;
}
