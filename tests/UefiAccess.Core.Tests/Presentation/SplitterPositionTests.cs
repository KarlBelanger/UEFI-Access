// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using UefiAccess.Presentation.Accessibility;
using UefiAccess.Presentation.Resources;
using Xunit;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// The policy the two splitters run (Platform/AnnouncingGridSplitter): the position is a whole
/// percent from 0 to 100 (limits that never move), each arrow press moves it to the next whole
/// percent, so the share changes by exactly one per press, and a press at a limit moves nothing.
/// Since Alpha 2 (finding A2-8, the tester's A2-6: the bare number NVDA spoke related to nothing) the
/// share is said as a sentence that names both panes: the splitter's value, and with a closing
/// period, preceded by "Limit reached: " after a press at a limit, what Status says. The lengths
/// are close to the default window's (the list and the detail pane share about 1150
/// device-independent pixels; each keeps at least 280).
/// </summary>
public sealed class SplitterPositionTests
{
    private const double Total = 1146;
    private const double ListMinimum = 280;
    private const double ListMaximum = Total - 280;

    /// <summary>
    /// The share runs from 0 to 100 in steps of one per arrow press.
    /// <see cref="SplitterPosition.LargeChange"/>, 10, is the large change of the range value
    /// alpha1.2 exposed: no key takes it and nothing exposes it since Alpha 2 finding A2-8, and the
    /// keyboard map gives it as alpha1.2's.
    /// </summary>
    [Fact]
    public void TheRangeIsZeroToOneHundredInStepsOfOne_AndAlpha12sLargeChangeWasTen()
    {
        Assert.Equal(0, SplitterPosition.Minimum);
        Assert.Equal(100, SplitterPosition.Maximum);
        Assert.Equal(1, SplitterPosition.SmallChange);
        Assert.Equal(10, SplitterPosition.LargeChange);
    }

    [Theory]
    [InlineData(716.25, 1146, 63)] // the list's 5 of 8 stars: 62.5 percent, rounded half away from zero
    [InlineData(430, 1146, 38)]
    [InlineData(150, 500, 30)]
    [InlineData(0, 500, 0)]
    [InlineData(500, 500, 100)]
    [InlineData(600, 500, 100)]
    [InlineData(-5, 500, 0)]
    [InlineData(100, 0, 0)]
    [InlineData(100, -1, 0)]
    [InlineData(double.NaN, 500, 0)]
    [InlineData(100, double.NaN, 0)]
    public void PercentIsTheWholeShareOfTheTotal(double length, double total, int expected)
    {
        Assert.Equal(expected, SplitterPosition.Percent(length, total));
    }

    [Fact]
    public void EveryPressChangesTheSpokenNumberByExactlyOne_UntilTheLimit_ThenNothingMoves()
    {
        double length = 716.25;
        List<int> spoken = new();
        for (int press = 0; press < 60; press++)
        {
            double? next = SplitterPosition.Step(length, Total, ListMinimum, ListMaximum, -1);
            if (next is null)
            {
                break;
            }

            Assert.True(next < length);
            length = next.Value;
            spoken.Add(SplitterPosition.Percent(length, Total));
        }

        // From 63 down to the limit: 280 of 1146 is 24.4 percent, heard as 24.
        Assert.Equal(Enumerable.Range(24, 39).Reverse(), spoken);
        Assert.Equal(ListMinimum, length);
        Assert.Null(SplitterPosition.Step(length, Total, ListMinimum, ListMaximum, -1));

        // And back up to the other limit: 866 of 1146 is 75.6 percent, heard as 76.
        spoken.Clear();
        double? up;
        while ((up = SplitterPosition.Step(length, Total, ListMinimum, ListMaximum, +1)) is double grown)
        {
            Assert.True(grown > length);
            Assert.Equal(SplitterPosition.Percent(length, Total) + 1, SplitterPosition.Percent(grown, Total));
            length = grown;
            spoken.Add(SplitterPosition.Percent(length, Total));
        }

        Assert.Null(up);
        Assert.Equal(25, spoken[0]);
        Assert.Equal(76, spoken[^1]);
        Assert.Equal(ListMaximum, length);
    }

    [Fact]
    public void AStepLandsOnTheWholePercent()
    {
        // 25 percent of 1146 is 286.5; the next press down lands on 24 percent's length or the
        // minimum, whichever is larger.
        Assert.Equal(286.5, SplitterPosition.Step(300, Total, ListMinimum, ListMaximum, -1));
        Assert.Equal(ListMinimum, SplitterPosition.Step(286.5, Total, ListMinimum, ListMaximum, -1));
        Assert.Equal(297.96, SplitterPosition.Step(286.5, Total, ListMinimum, ListMaximum, +1)!.Value, 6);
    }

    [Fact]
    public void APressAtALimitOrWithNoRoomMovesNothing()
    {
        // At the limit, and half a pixel off it (still heard as 24, and 23 is out of reach): the
        // clamped move would not change the spoken number, so nothing moves.
        Assert.Null(SplitterPosition.Step(ListMinimum, Total, ListMinimum, ListMaximum, -1));
        Assert.Null(SplitterPosition.Step(ListMinimum + 0.5, Total, ListMinimum, ListMaximum, -1));
        Assert.Null(SplitterPosition.Step(ListMaximum, Total, ListMinimum, ListMaximum, +1));

        // A pane already past a limit (the window shrank) is never moved further the wrong way.
        Assert.Null(SplitterPosition.Step(ListMaximum + 50, Total, ListMinimum, ListMaximum, +1));
        Assert.Equal(ListMaximum, SplitterPosition.Step(ListMaximum + 50, Total, ListMinimum, ListMaximum, -1));

        // No direction, no space to share, or limits that leave no room.
        Assert.Null(SplitterPosition.Step(500, Total, ListMinimum, ListMaximum, 0));
        Assert.Null(SplitterPosition.Step(0, 0, 0, 0, +1));
        Assert.Null(SplitterPosition.Step(200, 400, 250, 150, +1));
    }

    /// <summary>
    /// The list/detail splitter speaks the list's share (the pane before it): Right grows the
    /// list, Left shrinks it, and Up and Down do nothing. In a right-to-left layout Left and Right
    /// swap, as they do for the stock splitter.
    /// </summary>
    [Theory]
    [InlineData(ArrowKey.Right, false, 1)]
    [InlineData(ArrowKey.Left, false, -1)]
    [InlineData(ArrowKey.Up, false, 0)]
    [InlineData(ArrowKey.Down, false, 0)]
    [InlineData(ArrowKey.Right, true, -1)]
    [InlineData(ArrowKey.Left, true, 1)]
    [InlineData(ArrowKey.Up, true, 0)]
    public void ListDetailSplitter_RightGrowsTheList_LeftShrinksIt(ArrowKey key, bool rightToLeft, int expected)
    {
        Assert.Equal(expected, SplitterPosition.Direction(key, resizesColumns: true, tracksFirstDefinition: true, rightToLeft));
    }

    /// <summary>
    /// The log splitter speaks the log panel's share, the pane after it (TracksNextDefinition):
    /// Up moves the splitter up and so grows the log panel, Down shrinks it, Left and Right do
    /// nothing, and a right-to-left layout changes nothing for rows. A wrong sign here would make
    /// the spoken number fall on the key that visibly grows the panel.
    /// </summary>
    [Theory]
    [InlineData(ArrowKey.Up, false, 1)]
    [InlineData(ArrowKey.Down, false, -1)]
    [InlineData(ArrowKey.Left, false, 0)]
    [InlineData(ArrowKey.Right, false, 0)]
    [InlineData(ArrowKey.Up, true, 1)]
    [InlineData(ArrowKey.Down, true, -1)]
    public void LogSplitter_UpGrowsTheLogPanel_DownShrinksIt(ArrowKey key, bool rightToLeft, int expected)
    {
        Assert.Equal(expected, SplitterPosition.Direction(key, resizesColumns: false, tracksFirstDefinition: false, rightToLeft));
    }

    [Fact]
    public void TrackingThePaneBeforeOrAfterTheSplitterReversesTheDirection()
    {
        foreach (ArrowKey key in Enum.GetValues<ArrowKey>())
        {
            foreach (bool columns in new[] { true, false })
            {
                foreach (bool rightToLeft in new[] { true, false })
                {
                    int before = SplitterPosition.Direction(key, columns, tracksFirstDefinition: true, rightToLeft);
                    int after = SplitterPosition.Direction(key, columns, tracksFirstDefinition: false, rightToLeft);
                    Assert.Equal(-before, after);
                    Assert.InRange(before, -1, 1);
                }
            }
        }

        // A splitter between rows above its tracked pane: Down grows the pane before it.
        Assert.Equal(1, SplitterPosition.Direction(ArrowKey.Down, resizesColumns: false, tracksFirstDefinition: true, rightToLeft: false));
    }

    /// <summary>
    /// A2-8: the value of each splitter is a sentence that names both panes, the other pane's share
    /// being what is left of 100: the list's share of the width for "Resize list and details", the
    /// log panel's share of the height for "Resize log panel". No closing period: it is read as the
    /// value, after the name and "thumb".
    /// </summary>
    [Theory]
    [InlineData(SplitterKind.ListAndDetails, 63, "List 63 percent of the width, details 37 percent")]
    [InlineData(SplitterKind.ListAndDetails, 24, "List 24 percent of the width, details 76 percent")]
    [InlineData(SplitterKind.ListAndDetails, 0, "List 0 percent of the width, details 100 percent")]
    [InlineData(SplitterKind.ListAndDetails, 100, "List 100 percent of the width, details 0 percent")]
    [InlineData(SplitterKind.LogPanel, 30, "Log panel 30 percent of the height, list and details 70 percent")]
    [InlineData(SplitterKind.LogPanel, 0, "Log panel 0 percent of the height, list and details 100 percent")]
    [InlineData(SplitterKind.LogPanel, 100, "Log panel 100 percent of the height, list and details 0 percent")]
    public void TheValueIsASentenceThatNamesBothPanes(SplitterKind kind, int share, string expected)
    {
        Assert.Equal(expected, SplitterPosition.ValueText(kind, share));
        string format = kind == SplitterKind.ListAndDetails ? Strings.ListDetailSplitterValueFormat : Strings.LogSplitterValueFormat;
        Assert.Equal(string.Format(CultureInfo.InvariantCulture, format, share, 100 - share), SplitterPosition.ValueText(kind, share));
    }

    [Fact]
    public void AShareOutsideZeroToOneHundred_IsKeptWithinIt()
    {
        Assert.Equal("List 0 percent of the width, details 100 percent", SplitterPosition.ValueText(SplitterKind.ListAndDetails, -5));
        Assert.Equal("Log panel 100 percent of the height, list and details 0 percent", SplitterPosition.ValueText(SplitterKind.LogPanel, 140));
        Assert.Throws<ArgumentOutOfRangeException>(() => SplitterPosition.ValueText((SplitterKind)7, 50));
    }

    /// <summary>
    /// What Status says: the value sentence with a closing period after a press that moved the
    /// splitter or a drag that changed its share, and the same sentence after "Limit reached: "
    /// after a press that could not move it (at 24 and 76, the default window's limits of the list,
    /// and at 0 or 100 when a pane has no minimum).
    /// </summary>
    [Theory]
    [InlineData(SplitterKind.ListAndDetails, 64, false, "List 64 percent of the width, details 36 percent.")]
    [InlineData(SplitterKind.ListAndDetails, 24, true, "Limit reached: List 24 percent of the width, details 76 percent.")]
    [InlineData(SplitterKind.ListAndDetails, 76, true, "Limit reached: List 76 percent of the width, details 24 percent.")]
    [InlineData(SplitterKind.ListAndDetails, 0, true, "Limit reached: List 0 percent of the width, details 100 percent.")]
    [InlineData(SplitterKind.LogPanel, 31, false, "Log panel 31 percent of the height, list and details 69 percent.")]
    [InlineData(SplitterKind.LogPanel, 56, true, "Limit reached: Log panel 56 percent of the height, list and details 44 percent.")]
    [InlineData(SplitterKind.LogPanel, 100, true, "Limit reached: Log panel 100 percent of the height, list and details 0 percent.")]
    public void TheAnnouncementIsTheSentenceWithAPeriod_AfterLimitReachedAtALimit(SplitterKind kind, int share, bool atLimit, string expected)
    {
        string announcement = SplitterPosition.Announcement(kind, share, atLimit);
        Assert.Equal(expected, announcement);
        Assert.Equal((atLimit ? Strings.SplitterLimitPrefix : string.Empty) + SplitterPosition.ValueText(kind, share) + ".", announcement);
        Assert.Equal("Limit reached: ", Strings.SplitterLimitPrefix);
        Assert.DoesNotMatch(@"^\d+$", announcement);
    }

    /// <summary>
    /// The length of a share, kept within the pane's limits: what <see cref="SplitterPosition.Step"/>
    /// asks for the next whole percent. Nothing sets a share from outside since Alpha 2 finding A2-8:
    /// the splitter's value is a read-only sentence, and its SetValue throws.
    /// </summary>
    [Fact]
    public void LengthForAShare_IsThatShareOfTheTotal_WithinThePanesLimits()
    {
        Assert.Equal(573, SplitterPosition.LengthFor(50, Total, ListMinimum, ListMaximum));
        Assert.Equal(ListMinimum, SplitterPosition.LengthFor(0, Total, ListMinimum, ListMaximum));
        Assert.Equal(ListMaximum, SplitterPosition.LengthFor(100, Total, ListMinimum, ListMaximum));
        Assert.Equal(ListMaximum, SplitterPosition.LengthFor(250, Total, ListMinimum, ListMaximum));
        Assert.Null(SplitterPosition.LengthFor(50, 0, 0, 0));
        Assert.Null(SplitterPosition.LengthFor(double.NaN, Total, ListMinimum, ListMaximum));
        Assert.Null(SplitterPosition.LengthFor(50, 400, 250, 150));
    }
}
