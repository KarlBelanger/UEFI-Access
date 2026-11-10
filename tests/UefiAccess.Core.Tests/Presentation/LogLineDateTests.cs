// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.ComponentModel;
using UefiAccess.Presentation.ViewModels;
using Xunit;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// Findings F068, F093 and F136: the tester's round-1 panel read 06:57:04, 13:16:33, then 06:21:21,
/// because the session ran past midnight and the lines carried the time alone. The panel keeps
/// its spoken lines short (the time alone for the day of the newest line) and gives every line of
/// another day its date, "yyyy-MM-dd HH:mm:ss"; Copy log always writes the full date, time and
/// offset, so a pasted log can be matched to the dated log file.
/// </summary>
public sealed class LogLineDateTests
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(2);
    private static readonly DateTimeOffset Starting = new(2026, 9, 20, 6, 57, 4, Offset);
    private static readonly DateTimeOffset LoadedAt = new(2026, 9, 20, 13, 16, 33, Offset);
    private static readonly DateTimeOffset NextMorning = new(2026, 9, 21, 6, 21, 21, Offset);

    [Fact]
    public void LinesOfOneDay_ShowTheTimeAlone()
    {
        LogViewModel log = new();

        log.Add(new LogLineViewModel(Starting, "Information", "UefiAccess.App 0.1.0 starting"));
        log.Add(new LogLineViewModel(LoadedAt, LogViewModel.InfoLevel, "Loaded 77 variables from Live firmware (NtEnumerate)"));

        Assert.Equal(new[] { "06:57:04", "13:16:33" }, log.Lines.Select(line => line.Time));
        Assert.Equal("INFO 13:16:33: Loaded 77 variables from Live firmware (NtEnumerate)", log.Lines[1].AccessibleName);
        Assert.All(log.Lines, line => Assert.False(line.ShowsDate));
    }

    [Fact]
    public void RoundOneSequence_ReadsInOrder_OnceTheNextDayArrives()
    {
        LogViewModel log = new();
        log.Add(new LogLineViewModel(Starting, LogViewModel.InfoLevel, "UefiAccess.App 0.1.0 starting"));
        log.Add(new LogLineViewModel(LoadedAt, LogViewModel.InfoLevel, "Loaded 77 variables from Live firmware (NtEnumerate)"));
        List<string> changed = new();
        ((INotifyPropertyChanged)log.Lines[1]).PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

        log.Add(new LogLineViewModel(NextMorning, LogViewModel.InfoLevel, "Showing 77 of 77 variables"));

        Assert.Equal(
            new[]
            {
                "INFO 2026-09-20 06:57:04: UefiAccess.App 0.1.0 starting",
                "INFO 2026-09-20 13:16:33: Loaded 77 variables from Live firmware (NtEnumerate)",
                "INFO 06:21:21: Showing 77 of 77 variables",
            },
            log.Lines.Select(line => line.AccessibleName));
        Assert.Contains(nameof(LogLineViewModel.Time), changed);
        Assert.Contains(nameof(LogLineViewModel.AccessibleName), changed);

        // Copy log carries the date and the offset on every line, whatever the panel shows.
        Assert.Equal(
            "2026-09-20 06:57:04 +02:00 INFO UefiAccess.App 0.1.0 starting" + Environment.NewLine +
            "2026-09-20 13:16:33 +02:00 INFO Loaded 77 variables from Live firmware (NtEnumerate)" + Environment.NewLine +
            "2026-09-21 06:21:21 +02:00 INFO Showing 77 of 77 variables" + Environment.NewLine,
            log.CopyText());
    }

    [Fact]
    public void AClockSetBack_DatesTheLinesOfTheOtherDay()
    {
        LogViewModel log = new();
        log.Add(new LogLineViewModel(NextMorning, LogViewModel.InfoLevel, "later day"));
        log.Add(new LogLineViewModel(LoadedAt, LogViewModel.InfoLevel, "clock set back"));

        Assert.Equal("2026-09-21 06:21:21", log.Lines[0].Time);
        Assert.Equal("13:16:33", log.Lines[1].Time);
    }

    [Fact]
    public void ANewLineOfTheNewestDay_NeverShowsItsDate_AndOlderDatedLinesStayDated()
    {
        LogViewModel log = new();
        log.Add(new LogLineViewModel(LoadedAt, LogViewModel.InfoLevel, "a"));
        log.Add(new LogLineViewModel(NextMorning, LogViewModel.InfoLevel, "b"));
        log.Add(new LogLineViewModel(NextMorning.AddMinutes(1), LogViewModel.InfoLevel, "c"));

        Assert.Equal(new[] { true, false, false }, log.Lines.Select(line => line.ShowsDate));
    }

    [Fact]
    public void ALineOutsideALog_ShowsTheTimeAlone_AndCopiesWithTheFullDate()
    {
        LogLineViewModel line = new(NextMorning, LogViewModel.WarnLevel, "one warning");

        Assert.Equal("06:21:21", line.Time);
        Assert.Equal("WARN 06:21:21: one warning", line.AccessibleName);
        Assert.Equal("2026-09-21 06:21:21 +02:00 WARN one warning", line.ToString());
        Assert.Equal("yyyy-MM-dd HH:mm:ss zzz", LogLineViewModel.CopyTimestampFormat);
    }
}
