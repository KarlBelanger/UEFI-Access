// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Specialized;
using UefiAccess.Presentation.ViewModels;
using Xunit;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// <see cref="LogViewModel"/>: the bounded ring (oldest line dropped first), the level helpers,
/// the Copy log text format "yyyy-MM-dd HH:mm:ss zzz LEVEL message", the capacity clamp and the null guard.
/// </summary>
public sealed class LogViewModelTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 15, 12, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void DefaultCapacityIsTwoHundred()
    {
        LogViewModel log = new();

        Assert.Equal(200, LogViewModel.DefaultCapacity);
        Assert.Equal(200, log.Capacity);
        Assert.Empty(log.Lines);
        Assert.Equal(0, log.Count);
        Assert.Equal(string.Empty, log.CopyText());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void CapacityBelowOneClampsToOne(int capacity)
    {
        LogViewModel log = new(capacity);

        Assert.Equal(1, log.Capacity);
        log.Info("first");
        log.Info("second");
        LogLineViewModel only = Assert.Single(log.Lines);
        Assert.Equal("second", only.Message);
    }

    [Fact]
    public void ExceedingTheCapacityDropsTheOldestLinesFirst()
    {
        LogViewModel log = new(3);
        for (int i = 1; i <= 5; i++)
        {
            log.Add(new LogLineViewModel(Noon.AddSeconds(i), LogViewModel.InfoLevel, "line " + i));
        }

        Assert.Equal(3, log.Count);
        Assert.Equal(new[] { "line 3", "line 4", "line 5" }, log.Lines.Select(line => line.Message));
    }

    [Fact]
    public void LevelHelpersStampTheDocumentedLevels()
    {
        LogViewModel log = new();

        log.Info("i");
        log.Warn("w");
        log.Error("e");

        Assert.Equal(new[] { "INFO", "WARN", "ERROR" }, log.Lines.Select(line => line.Level));
        Assert.Equal(LogViewModel.InfoLevel, log.Lines[0].Level);
        Assert.Equal(LogViewModel.WarnLevel, log.Lines[1].Level);
        Assert.Equal(LogViewModel.ErrorLevel, log.Lines[2].Level);
        Assert.All(log.Lines, line => Assert.True((DateTimeOffset.Now - line.Timestamp).Duration() < TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void CopyTextJoinsTimeLevelAndMessagePerLine()
    {
        LogViewModel log = new();
        log.Add(new LogLineViewModel(Noon, LogViewModel.InfoLevel, "Loaded 63 variables from Live firmware (NtEnumerate)"));
        log.Add(new LogLineViewModel(Noon.AddSeconds(7), LogViewModel.WarnLevel, "Boot0002: ERROR_ENVVAR_NOT_FOUND (203)"));

        string text = log.CopyText();

        Assert.Equal(
            "2026-09-15 12:00:00 +02:00 INFO Loaded 63 variables from Live firmware (NtEnumerate)" + Environment.NewLine +
            "2026-09-15 12:00:07 +02:00 WARN Boot0002: ERROR_ENVVAR_NOT_FOUND (203)" + Environment.NewLine,
            text);
    }

    [Fact]
    public void LinesIsObservableSoTheViewCanBindToIt()
    {
        LogViewModel log = new(2);
        List<NotifyCollectionChangedAction> actions = new();
        log.Lines.CollectionChanged += (_, e) => actions.Add(e.Action);

        log.Info("a");
        log.Info("b");
        log.Info("c");

        Assert.Equal(
            new[]
            {
                NotifyCollectionChangedAction.Add,
                NotifyCollectionChangedAction.Add,
                NotifyCollectionChangedAction.Add,
                NotifyCollectionChangedAction.Remove,
            },
            actions);
        Assert.Equal(new[] { "b", "c" }, log.Lines.Select(line => line.Message));
    }

    [Fact]
    public void NullLineIsIgnored()
    {
        LogViewModel log = new();

        log.Add(null!);

        Assert.Empty(log.Lines);
    }

    [Theory]
    [InlineData("Information", "INFO")]
    [InlineData("information", "INFO")]
    [InlineData("INFO", "INFO")]
    [InlineData("Warning", "WARN")]
    [InlineData("WARN", "WARN")]
    [InlineData("Error", "ERROR")]
    [InlineData("ERROR", "ERROR")]
    [InlineData("Fatal", "FATAL")]
    [InlineData("Debug", "DEBUG")]
    [InlineData("Verbose", "VERBOSE")]
    [InlineData(" Warning ", "WARN")]
    [InlineData("Custom", "Custom")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void LevelNames_AreNormalizedToThePanelsVocabulary(string? given, string expected)
    {
        Assert.Equal(expected, LogLineViewModel.NormalizeLevel(given));

        LogLineViewModel line = new(Noon, given!, "m");
        Assert.Equal(expected, line.Level);
        Assert.Equal(expected + " 12:00:00: m", line.AccessibleName);

        LogViewModel log = new();
        log.Add(given!, "m");
        Assert.Equal(expected, log.Lines[0].Level);
    }

    [Fact]
    public void LogLineViewModel_UsesTheOffsetItWasGiven_AndTreatsNullTextAsEmpty()
    {
        LogLineViewModel line = new(new DateTimeOffset(2026, 9, 15, 23, 59, 8, TimeSpan.FromHours(-7)), null!, null!);

        Assert.Equal("23:59:08", line.Time);
        Assert.Equal(string.Empty, line.Level);
        Assert.Equal(string.Empty, line.Message);
        Assert.Equal(" 23:59:08: ", line.AccessibleName);
        Assert.Equal("2026-09-15 23:59:08 -07:00  ", line.ToString());
    }
}
