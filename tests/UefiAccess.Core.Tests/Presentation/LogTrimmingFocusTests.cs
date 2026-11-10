// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Specialized;
using UefiAccess.Presentation.Resources;
using UefiAccess.Presentation.ViewModels;
using Xunit;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// Finding F067: when the log is full, adding a line drops the oldest one, and a removed row that
/// has keyboard focus would drop focus on the window (NVDA then announces the window, and the tester
/// loses his place). The log asks the view which line has focus
/// (<see cref="LogViewModel.FocusedLineProvider"/>) and never removes that line: the lines older
/// than it go as usual; when it is itself the oldest, it and the lines after it stay until focus
/// moves on, up to twice the capacity, past which the line right after it goes instead.
/// </summary>
public sealed class LogTrimmingFocusTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 22, 9, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void FocusOnTheOldestLine_KeepsIt_AndTheLinesAfterIt_UntilFocusMovesOn()
    {
        LogViewModel log = Filled(3, out List<LogLineViewModel> lines);
        LogLineViewModel? focused = lines[0];
        log.FocusedLineProvider = () => focused;
        List<LogLineViewModel> removed = RemovedLines(log);

        log.Add(Line(4));
        log.Add(Line(5));

        Assert.Empty(removed);
        Assert.Same(lines[0], log.Lines[0]);
        Assert.Equal(5, log.Count);

        // Focus moved on (off the panel): the next line trims the log back to the capacity.
        focused = null;
        log.Add(Line(6));

        Assert.Equal(3, log.Count);
        Assert.Equal(new[] { "line 4", "line 5", "line 6" }, log.Lines.Select(line => line.Message));
    }

    [Fact]
    public void FocusOnALaterLine_DropsTheOlderLinesAsUsual_AndNeverTheFocusedOne()
    {
        LogViewModel log = Filled(3, out List<LogLineViewModel> lines);
        log.FocusedLineProvider = () => lines[1];
        List<LogLineViewModel> removed = RemovedLines(log);

        log.Add(Line(4));

        Assert.Equal(new[] { lines[0] }, removed);
        Assert.Same(lines[1], log.Lines[0]);
        Assert.Equal(3, log.Count);

        // Now the focused line is the oldest: it stays.
        log.Add(Line(5));
        Assert.Same(lines[1], log.Lines[0]);
        Assert.Equal(4, log.Count);
        Assert.DoesNotContain(lines[1], removed);
    }

    [Fact]
    public void PastTwiceTheCapacity_TheLineAfterTheFocusedOneGoes_AndTheFocusedLineStays()
    {
        LogViewModel log = Filled(3, out List<LogLineViewModel> lines);
        log.FocusedLineProvider = () => lines[0];
        List<LogLineViewModel> removed = RemovedLines(log);

        for (int i = 4; i <= 10; i++)
        {
            log.Add(Line(i));
        }

        Assert.Equal(6, log.MaximumCount);
        Assert.Equal(6, log.Count);
        Assert.Same(lines[0], log.Lines[0]);
        Assert.Equal("line 10", log.Lines[^1].Message);
        Assert.DoesNotContain(lines[0], removed);
        Assert.Equal(new[] { "line 2", "line 3", "line 4", "line 5" }, removed.Select(line => line.Message));
    }

    [Fact]
    public void AProviderThatThrowsOrAnswersWithAnotherLog_CountsAsNoFocus()
    {
        LogViewModel log = Filled(2, out _);
        log.FocusedLineProvider = () => throw new InvalidOperationException("no dispatcher");

        log.Add(Line(3));

        Assert.Equal(new[] { "line 2", "line 3" }, log.Lines.Select(line => line.Message));

        log.FocusedLineProvider = () => Line(99);
        log.Add(Line(4));
        Assert.Equal(new[] { "line 3", "line 4" }, log.Lines.Select(line => line.Message));
    }

    [Fact]
    public void TheProvider_IsAskedOnlyWhenALineMustGo()
    {
        LogViewModel log = new(3);
        int asked = 0;
        log.FocusedLineProvider = () =>
        {
            asked++;
            return null;
        };

        log.Add(Line(1));
        log.Add(Line(2));
        log.Add(Line(3));
        Assert.Equal(0, asked);

        log.Add(Line(4));
        Assert.Equal(1, asked);
    }

    [Fact]
    public void TheCopyLogPath_KeepsTheLineThatHasFocus_AtTheDefaultCapacity()
    {
        // The path of the finding: Home in a full log, then Alt+L; Copy log's announcement adds a line.
        ShellTestRig rig = new();
        for (int i = rig.Shell.Log.Count; i < LogViewModel.DefaultCapacity; i++)
        {
            rig.Shell.Log.Info("filler " + i);
        }

        LogLineViewModel oldest = rig.Shell.Log.Lines[0];
        rig.Shell.Log.FocusedLineProvider = () => oldest;

        rig.Shell.CopyLogCommand.Execute(null);

        Assert.Same(oldest, rig.Shell.Log.Lines[0]);
        Assert.Equal(LogViewModel.DefaultCapacity + 1, rig.Shell.Log.Count);
        Assert.Equal(Strings.CopiedLogStatus, rig.Shell.Log.Lines[^1].Message);
    }

    private static LogViewModel Filled(int capacity, out List<LogLineViewModel> lines)
    {
        LogViewModel log = new(capacity);
        lines = new List<LogLineViewModel>();
        for (int i = 1; i <= capacity; i++)
        {
            LogLineViewModel line = Line(i);
            lines.Add(line);
            log.Add(line);
        }

        return log;
    }

    private static List<LogLineViewModel> RemovedLines(LogViewModel log)
    {
        List<LogLineViewModel> removed = new();
        log.Lines.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems is not null)
            {
                removed.AddRange(e.OldItems.Cast<LogLineViewModel>());
            }
        };
        return removed;
    }

    private static LogLineViewModel Line(int number) => new(Start.AddSeconds(number), LogViewModel.InfoLevel, "line " + number);
}
