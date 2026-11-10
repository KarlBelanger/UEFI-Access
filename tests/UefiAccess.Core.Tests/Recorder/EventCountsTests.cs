// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.UiaTrace.Recording;
using Xunit;

namespace UefiAccess.Core.Tests.Recorder;

/// <summary>
/// The recorder's counts (<see cref="EventCounts"/>): per event, per watched property for property
/// changes, and focus outside UefiAccess on its own; the LiveRegionChanged copies right after the
/// LiveRegionChanged count and not added to the total again; exact when UI Automation calls the
/// handlers from several threads at once; and a summary in a stable order, zeros included, every
/// line starting with "recorder:" so it stays recognisable in a merged file.
/// </summary>
public sealed class EventCountsTests
{
    [Fact]
    public void Add_CountsPerEventPerPropertyAndOutsideFocus()
    {
        EventCounts counts = new();

        counts.Add(UiaEventNames.FocusChanged);
        counts.Add(UiaEventNames.FocusChanged);
        counts.Add(UiaEventNames.ElementSelected);
        counts.AddPropertyChange("IsSelected");
        counts.AddPropertyChange("IsSelected");
        counts.AddPropertyChange("Name");
        counts.AddFocusOutside();
        counts.AddHandlerError();

        Assert.Equal(2, counts.Get(UiaEventNames.FocusChanged));
        Assert.Equal(1, counts.Get(UiaEventNames.ElementSelected));
        Assert.Equal(2, counts.Get("PropertyChanged IsSelected"));
        Assert.Equal(1, counts.Get("PropertyChanged Name"));
        Assert.Equal(1, counts.Get(UiaEventNames.FocusChangedOutside));
        Assert.Equal(0, counts.Get(UiaEventNames.StructureChanged));
        Assert.Equal(1, counts.Get(EventCounts.HandlerErrors));
        Assert.Equal(7, counts.Total);
    }

    [Fact]
    public void Add_IsExactFromSeveralThreads()
    {
        EventCounts counts = new();
        const int Threads = 8;
        const int PerThread = 5000;

        Parallel.For(0, Threads, new ParallelOptions { MaxDegreeOfParallelism = Threads }, thread =>
        {
            for (int i = 0; i < PerThread; i++)
            {
                counts.Add(UiaEventNames.FocusChanged);
                counts.AddPropertyChange(UiaEventNames.Properties[i % UiaEventNames.Properties.Count]);
                if (thread % 2 == 0)
                {
                    counts.AddFocusOutside();
                }
            }
        });

        Assert.Equal(Threads * PerThread, counts.Get(UiaEventNames.FocusChanged));
        Assert.Equal(Threads * PerThread / 2, counts.Get(UiaEventNames.FocusChangedOutside));
        Assert.Equal(Threads * PerThread / UiaEventNames.Properties.Count, counts.Get("PropertyChanged Value"));
        Assert.Equal((Threads * PerThread * 2) + (Threads * PerThread / 2), counts.Total);
    }

    [Fact]
    public void SummaryLines_ListTheKnownEventsInAFixedOrder_ZerosIncluded()
    {
        EventCounts counts = new();
        counts.Add(UiaEventNames.ElementSelected);
        counts.AddPropertyChange("HasKeyboardFocus");
        counts.AddPropertyChange("HasKeyboardFocus");

        IReadOnlyList<string> lines = counts.SummaryLines(TimeSpan.FromMilliseconds(12345));

        Assert.Equal("recorder: recorded 12.3 s, 3 events received", lines[0]);
        Assert.Equal(
            UiaEventNames.SummaryOrder.Select(key => "recorder: " + key + " " + (key == UiaEventNames.ElementSelected ? 1 : key == "PropertyChanged HasKeyboardFocus" ? 2 : 0)),
            lines.Skip(1));
        Assert.All(lines, line => Assert.StartsWith("recorder: ", line, StringComparison.Ordinal));
    }

    [Fact]
    public void SummaryLines_PutOtherKeysAfterTheKnownOnes_AndHandlerErrorsLast()
    {
        EventCounts counts = new();
        counts.Add("WindowClosed");
        counts.Add("AutomationPropertyChanged");
        counts.AddPropertyChange("Custom");
        counts.AddHandlerError();
        counts.AddHandlerError();

        IReadOnlyList<string> lines = counts.SummaryLines(TimeSpan.Zero);
        string[] tail = lines.Skip(1 + UiaEventNames.SummaryOrder.Count).ToArray();

        Assert.Equal("recorder: recorded 0.0 s, 3 events received", lines[0]);
        Assert.Equal(
            new[]
            {
                "recorder: AutomationPropertyChanged 1",
                "recorder: PropertyChanged Custom 1",
                "recorder: WindowClosed 1",
                "recorder: handler errors 2",
            },
            tail);
    }

    [Fact]
    public void SummaryLines_LeaveOutHandlerErrorsWhenThereAreNone_AndAreStable()
    {
        EventCounts first = new();
        EventCounts second = new();
        string[] keys = { UiaEventNames.StructureChanged, UiaEventNames.FocusChanged, "Zebra", "Alpha", UiaEventNames.Notification };
        foreach (string key in keys)
        {
            first.Add(key);
        }

        foreach (string key in keys.Reverse())
        {
            second.Add(key);
        }

        IReadOnlyList<string> lines = first.SummaryLines(TimeSpan.FromSeconds(2));

        Assert.Equal(lines, first.SummaryLines(TimeSpan.FromSeconds(2)));
        Assert.Equal(lines, second.SummaryLines(TimeSpan.FromSeconds(2)));
        Assert.DoesNotContain(lines, line => line.Contains(EventCounts.HandlerErrors, StringComparison.Ordinal));
        Assert.Equal(new[] { "recorder: Alpha 1", "recorder: Zebra 1" }, lines.Skip(1 + UiaEventNames.SummaryOrder.Count));
    }

    [Fact]
    public void LiveRegionCopies_AreListedAfterLiveRegionChanged_AndNotAddedToTheTotal()
    {
        // The ab2 check of 2 October 2026 on m4-hw0: 17 LiveRegionChanged received, 6 of them copies.
        EventCounts counts = new();
        for (int i = 0; i < 17; i++)
        {
            counts.Add(UiaEventNames.LiveRegionChanged);
            if (i % 2 == 1 && i < 12)
            {
                counts.AddLiveRegionCopy();
            }
        }

        IReadOnlyList<string> lines = counts.SummaryLines(TimeSpan.FromSeconds(1));
        int live = lines.ToList().IndexOf("recorder: LiveRegionChanged 17");

        Assert.Equal(6, counts.Get(UiaEventNames.LiveRegionChangedCopies));
        Assert.Equal(17, counts.Total);
        Assert.Equal("recorder: recorded 1.0 s, 17 events received", lines[0]);
        Assert.True(live > 0, "no LiveRegionChanged line");
        Assert.Equal("recorder: LiveRegionChanged copies 6", lines[live + 1]);
        Assert.Equal("recorder: LiveRegionChanged copies 0", new EventCounts().SummaryLines(TimeSpan.Zero).Single(line => line.StartsWith("recorder: LiveRegionChanged copies", StringComparison.Ordinal)));
    }

    [Fact]
    public void SummaryLines_ANegativeDurationIsShownAsZero()
    {
        Assert.Equal("recorder: recorded 0.0 s, 0 events received", new EventCounts().SummaryLines(TimeSpan.FromSeconds(-3))[0]);
    }
}
