// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.ObjectModel;
using System.Text;
using UefiAccess.Presentation.Infrastructure;

namespace UefiAccess.Presentation.ViewModels;

/// <summary>
/// The bounded list of log lines shown in the log panel (oldest first; the oldest line is dropped
/// when the capacity is exceeded). The panel is a live region set to Off, so adding lines never
/// interrupts the screen reader. The shell writes every announcement here as an
/// <see cref="InfoLevel"/> line and every source warning as a <see cref="WarnLevel"/> line, so
/// Copy log (Alt+L) yields the whole sequence of what the Status box said; the application
/// mirrors its logger into the same list by the <see cref="LogPanelMirror"/> rule (Information
/// and above, so failures logged at Error), and <see cref="LogLineViewModel"/> maps the logger's
/// level names onto the panel's vocabulary (<see cref="InfoLevel"/>, <see cref="WarnLevel"/>,
/// <see cref="ErrorLevel"/>).
/// </summary>
/// <remarks>
/// <para>
/// Dates: a line shows the time alone while it belongs to the same day as the newest line; when a
/// line of another day arrives, every older line of another day starts to show its date as well
/// (<see cref="LogLineViewModel.ShowsDate"/>), so the panel reads in order across midnight and
/// through a clock change while today's lines stay short. Copy log always writes the full date.
/// </para>
/// <para>
/// Trimming never takes keyboard focus away (a removed row that had focus would drop it on the
/// window, and the screen reader would announce the window instead of a log line). Before the
/// oldest line is dropped, the log asks the view which line has focus
/// (<see cref="FocusedLineProvider"/>); lines older than that one are dropped as usual, but that
/// line and the lines after it stay until focus moves on, up to twice the capacity; past that the
/// oldest line after the focused one is dropped instead. The next line added after focus has moved
/// on trims the list back to the capacity.
/// </para>
/// </remarks>
public sealed class LogViewModel : ObservableObject
{
    /// <summary>The default capacity.</summary>
    public const int DefaultCapacity = 200;

    /// <summary>The level of a line that mirrors an announcement or records a normal event.</summary>
    public const string InfoLevel = "INFO";

    /// <summary>The level of a line that records a non-fatal observation, for example a source warning.</summary>
    public const string WarnLevel = "WARN";

    /// <summary>The level of a line that records a failure.</summary>
    public const string ErrorLevel = "ERROR";

    private DateTime? _newestDate;

    /// <summary>Creates the log.</summary>
    /// <param name="capacity">The maximum number of lines kept; values below 1 are treated as 1.</param>
    public LogViewModel(int capacity = DefaultCapacity)
    {
        Capacity = Math.Max(1, capacity);
        Lines = new ObservableCollection<LogLineViewModel>();
    }

    /// <summary>The maximum number of lines kept while keyboard focus is not on the oldest line.</summary>
    public int Capacity { get; }

    /// <summary>The most lines ever kept: twice <see cref="Capacity"/>, reached only while focus stays on the oldest line.</summary>
    public int MaximumCount => Capacity * 2;

    /// <summary>The lines, oldest first; the view binds its ItemsSource to this.</summary>
    public ObservableCollection<LogLineViewModel> Lines { get; }

    /// <summary>The number of lines currently kept.</summary>
    public int Count => Lines.Count;

    /// <summary>
    /// Supplied by the view: returns the line whose row has keyboard focus, or null when focus is
    /// not on a line. Asked only when the capacity is exceeded, on the thread that adds the line
    /// (the UI thread). Null (the default, and every test that does not set it) means no line has
    /// focus; a provider that throws counts as null.
    /// </summary>
    public Func<LogLineViewModel?>? FocusedLineProvider { get; set; }

    /// <summary>Adds a line stamped with the current local time.</summary>
    /// <param name="level">The level, for example <see cref="InfoLevel"/> or <see cref="WarnLevel"/>.</param>
    /// <param name="message">The message.</param>
    public void Add(string level, string message) => Add(new LogLineViewModel(DateTimeOffset.Now, level, message));

    /// <summary>Adds an <see cref="InfoLevel"/> line.</summary>
    /// <param name="message">The message.</param>
    public void Info(string message) => Add(InfoLevel, message);

    /// <summary>Adds a <see cref="WarnLevel"/> line.</summary>
    /// <param name="message">The message.</param>
    public void Warn(string message) => Add(WarnLevel, message);

    /// <summary>Adds an <see cref="ErrorLevel"/> line.</summary>
    /// <param name="message">The message.</param>
    public void Error(string message) => Add(ErrorLevel, message);

    /// <summary>
    /// Adds a line, then drops the oldest lines while the capacity is exceeded, never the line
    /// that has keyboard focus (see the remarks). A null line is ignored.
    /// </summary>
    /// <param name="line">The line.</param>
    public void Add(LogLineViewModel line)
    {
        if (line is null)
        {
            return;
        }

        DateTime date = line.Timestamp.Date;
        if (_newestDate != date)
        {
            // A new day (or the clock was set back): every line of another day now shows its date.
            _newestDate = date;
            foreach (LogLineViewModel existing in Lines)
            {
                existing.ShowsDate = existing.Timestamp.Date != date;
            }
        }

        line.ShowsDate = false;
        Lines.Add(line);
        Trim();
    }

    /// <summary>Every line as text, one per line, with its full date, time and offset, for Copy log.</summary>
    /// <returns>The text; empty when there are no lines.</returns>
    public string CopyText()
    {
        StringBuilder builder = new();
        foreach (LogLineViewModel line in Lines)
        {
            builder.AppendLine(line.ToString());
        }

        return builder.ToString();
    }

    private void Trim()
    {
        if (Lines.Count <= Capacity)
        {
            return;
        }

        LogLineViewModel? focused = FocusedLine();
        while (Lines.Count > Capacity)
        {
            int index = 0;
            if (focused is not null && ReferenceEquals(Lines[0], focused))
            {
                if (Lines.Count <= MaximumCount)
                {
                    // The oldest line has focus: it stays, and so do the lines after it.
                    return;
                }

                index = 1;
            }

            Lines.RemoveAt(index);
        }
    }

    private LogLineViewModel? FocusedLine()
    {
        try
        {
            return FocusedLineProvider?.Invoke();
        }
        catch (Exception)
        {
            // The provider asks the view; a view that cannot answer means no line has focus.
            return null;
        }
    }
}
