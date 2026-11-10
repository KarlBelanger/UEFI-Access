// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using UefiAccess.Presentation.Resources;

namespace UefiAccess.Presentation.Accessibility;

/// <summary>
/// How a splitter's position is stepped and said (Alpha 1 round 2; Alpha 2 finding A2-8): as the
/// share, in whole percent, that one of the two panes beside the splitter has of the space the two
/// share (the list's share of the list and the detail pane, the log panel's share of the list/detail
/// row and the log panel), a whole percent from <see cref="Minimum"/> to <see cref="Maximum"/>,
/// limits that never move. Each arrow press moves the splitter to the next whole percent
/// (<see cref="SmallChange"/>) in the direction of the arrow (<see cref="Direction"/>), so every
/// press that moves it changes the share by exactly one, and a press at a limit moves nothing. The
/// share is said as a sentence that names both panes (<see cref="ValueText"/>): alpha1.2 exposed the
/// bare number as a UI Automation range value, which NVDA 2026.2 spoke as "63" and Narrator not at
/// all (the tester's A2-6, the developer's A2-5), so the splitter now exposes that sentence as its value
/// and the shell says it through Status after each press or drag (<see cref="Announcement"/>). The
/// WPF splitter (UefiAccess.App, Platform/AnnouncingGridSplitter) is a thin adapter over this
/// policy; lengths are device-independent pixels.
/// </summary>
public static class SplitterPosition
{
    /// <summary>The smallest share: the pane has none of the space.</summary>
    public const int Minimum = 0;

    /// <summary>The largest share: the pane has all of the space.</summary>
    public const int Maximum = 100;

    /// <summary>The change of one arrow press, in percent.</summary>
    public const int SmallChange = 1;

    /// <summary>
    /// Ten arrow presses, in percent: the large change of the range value alpha1.2 exposed. No key
    /// takes it and nothing exposes it since A2-8; it stays for the pages that still describe that
    /// range value until they change with it.
    /// </summary>
    public const int LargeChange = 10;

    /// <summary>
    /// The splitter's position as a sentence that names both panes, without a closing period: the
    /// value the splitter exposes through the UI Automation Value pattern, read on arrival ("List 63
    /// percent of the width, details 37 percent", "Log panel 30 percent of the height, list and
    /// details 70 percent"). The share is kept within 0 to 100; the other pane has what is left of 100.
    /// </summary>
    /// <param name="kind">Which splitter.</param>
    /// <param name="share">The share of the pane the splitter speaks for (the list's, or the log panel's), in whole percent.</param>
    /// <returns>The sentence.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a defined splitter.</exception>
    public static string ValueText(SplitterKind kind, int share)
    {
        string format = kind switch
        {
            SplitterKind.ListAndDetails => Strings.ListDetailSplitterValueFormat,
            SplitterKind.LogPanel => Strings.LogSplitterValueFormat,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        int own = Math.Clamp(share, Minimum, Maximum);
        return string.Format(CultureInfo.InvariantCulture, format, own, Maximum - own);
    }

    /// <summary>
    /// What Status says after an arrow press moved the splitter, or at the end of a drag that changed
    /// its share: <see cref="ValueText"/> with a closing period. After a press that could not move it
    /// further that way, the same sentence after "Limit reached: ", so a press at a limit is not silent.
    /// </summary>
    /// <param name="kind">Which splitter.</param>
    /// <param name="share">The pane's share after the press or drag, in whole percent.</param>
    /// <param name="atLimit">True when the press could not move the splitter.</param>
    /// <returns>The announcement.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a defined splitter.</exception>
    public static string Announcement(SplitterKind kind, int share, bool atLimit)
    {
        string sentence = ValueText(kind, share) + ".";
        return atLimit ? Strings.SplitterLimitPrefix + sentence : sentence;
    }

    /// <summary>
    /// The share of <paramref name="length"/> in <paramref name="total"/> as a whole percent,
    /// rounded half away from zero and kept within 0 to 100; 0 when there is no space to share.
    /// </summary>
    /// <param name="length">The pane's length.</param>
    /// <param name="total">The length the two panes share.</param>
    /// <returns>The whole percent.</returns>
    public static int Percent(double length, double total)
    {
        if (!(total > 0) || double.IsNaN(length))
        {
            return Minimum;
        }

        double percent = Math.Round(100.0 * length / total, MidpointRounding.AwayFromZero);
        return (int)Math.Clamp(percent, Minimum, Maximum);
    }

    /// <summary>
    /// Which way an arrow key moves the spoken pane: +1 when it grows it, -1 when it shrinks it,
    /// 0 for an arrow across the splitter's axis (Up or Down on a splitter between columns, Left
    /// or Right on one between rows). The splitter moves toward the arrow, as the stock splitter
    /// does: Right and Down grow the pane before it and shrink the pane after it, and in a
    /// right-to-left layout Left and Right swap for a splitter between columns. So Left shrinks
    /// the list (the pane before the list/detail splitter) and Up grows the log panel (the pane
    /// after the log splitter).
    /// </summary>
    /// <param name="key">The arrow pressed.</param>
    /// <param name="resizesColumns">True for a splitter between columns, false for one between rows.</param>
    /// <param name="tracksFirstDefinition">True when the spoken pane is the one before the splitter, false for the one after it.</param>
    /// <param name="rightToLeft">True when the splitter is laid out right to left.</param>
    /// <returns>+1, -1 or 0.</returns>
    public static int Direction(ArrowKey key, bool resizesColumns, bool tracksFirstDefinition, bool rightToLeft)
    {
        int firstGrows = (resizesColumns, key) switch
        {
            (true, ArrowKey.Right) => 1,
            (true, ArrowKey.Left) => -1,
            (false, ArrowKey.Down) => 1,
            (false, ArrowKey.Up) => -1,
            _ => 0,
        };
        if (resizesColumns && rightToLeft)
        {
            firstGrows = -firstGrows;
        }

        return tracksFirstDefinition ? firstGrows : -firstGrows;
    }

    /// <summary>
    /// Where one arrow press moves the pane: the length of the next whole percent in
    /// <paramref name="direction"/> (positive: the pane grows), kept within the pane's limits.
    /// Null when the press must move nothing: at a limit (the next whole percent is out of reach,
    /// so the spoken share would not change), when the limits leave no room, or when there is
    /// no space to share.
    /// </summary>
    /// <param name="length">The pane's current length.</param>
    /// <param name="total">The length the two panes share.</param>
    /// <param name="minimumLength">The pane's smallest length.</param>
    /// <param name="maximumLength">The pane's largest length (what leaves the other pane its minimum).</param>
    /// <param name="direction">Positive to grow the pane, negative to shrink it.</param>
    /// <returns>The new length, or null.</returns>
    public static double? Step(double length, double total, double minimumLength, double maximumLength, int direction)
    {
        if (direction == 0)
        {
            return null;
        }

        int current = Percent(length, total);
        double? target = LengthFor(current + Math.Sign(direction), total, minimumLength, maximumLength);
        if (target is not double next || Percent(next, total) == current)
        {
            return null;
        }

        bool movesTheRightWay = direction > 0 ? next > length : next < length;
        return movesTheRightWay ? next : null;
    }

    /// <summary>
    /// The length that gives the pane <paramref name="percent"/> of <paramref name="total"/>
    /// (kept within 0 to 100), then within the pane's limits; null when the limits leave no room
    /// or there is no space to share. <see cref="Step"/> asks it for the next whole percent.
    /// </summary>
    /// <param name="percent">The wanted share.</param>
    /// <param name="total">The length the two panes share.</param>
    /// <param name="minimumLength">The pane's smallest length.</param>
    /// <param name="maximumLength">The pane's largest length.</param>
    /// <returns>The length, or null.</returns>
    public static double? LengthFor(double percent, double total, double minimumLength, double maximumLength)
    {
        if (!(total > 0) || double.IsNaN(percent) || !(minimumLength <= maximumLength))
        {
            return null;
        }

        double length = Math.Clamp(percent, Minimum, Maximum) * total / 100.0;
        return Math.Clamp(length, minimumLength, maximumLength);
    }
}

/// <summary>The four arrow keys, as a splitter sees them (<see cref="SplitterPosition.Direction"/>).</summary>
public enum ArrowKey
{
    /// <summary>Left arrow.</summary>
    Left,

    /// <summary>Right arrow.</summary>
    Right,

    /// <summary>Up arrow.</summary>
    Up,

    /// <summary>Down arrow.</summary>
    Down,
}
