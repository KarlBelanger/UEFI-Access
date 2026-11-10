// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using UefiAccess.Presentation.Accessibility;

namespace UefiAccess.App.Platform;

/// <summary>
/// The stock <see cref="GridSplitter"/> with two additions for screen readers (Alpha 1 round 1,
/// finding A1-5: a keyboard resize gave no accessible feedback; Alpha 2 finding A2-8, the tester's A2-6:
/// the number alpha1.2 spoke relates to nothing, and the developer's A2-5: Narrator said nothing
/// after a press). Its automation peer, <see cref="AnnouncingGridSplitterAutomationPeer"/>,
/// exposes the splitter's position through the UI Automation Value pattern, read-only, as a
/// sentence that names both panes (<see cref="SplitterPosition.ValueText"/>: "List 63 percent of
/// the width, details 37 percent"), which a screen reader reads on arrival after the name and
/// "thumb". After every arrow press along the splitter's axis, and once at the end of a mouse drag
/// that changed the share, the splitter raises <see cref="Moved"/> with its share and whether the
/// press met a limit; the window hands that to the shell, which says the sentence through Status,
/// the one live region NVDA and Narrator both speak. The splitter raises no automation event of its
/// own, so the sentence is heard once. The template, the mouse drag, Escape (which cancels a drag),
/// the focus behaviour and the Transform pattern are the base class's; the arrow keys along the
/// splitter's axis move it to the next whole percent (so every press that moves it changes the
/// share by one) through the stock keyboard move, and a press at a limit moves nothing and is
/// reported as one. This is a control subclass, not a template override, and the peer derives from
/// the stock <see cref="GridSplitterAutomationPeer"/>.
/// </summary>
/// <remarks>
/// The spoken pane is the definition before the splitter (the list column), or the one after it
/// when <see cref="TracksNextDefinition"/> is set (the log row); <see cref="Kind"/> says which
/// sentence names it. Its limits are its own minimum and the length that still leaves the other
/// definition its minimum: every arrow press and drag stays inside them (the two definitions of
/// each splitter are star-sized, so the stock splitter keeps both minimums as well). The share is
/// read after the layout has run (<see cref="UIElement.UpdateLayout"/>), so the report carries the
/// size a sighted user sees; nothing here is timed.
/// </remarks>
public sealed class AnnouncingGridSplitter : GridSplitter
{
    private int? _dragStartShare;

    /// <summary>Creates the splitter; a mouse drag is reported when it completes, like a key press.</summary>
    public AnnouncingGridSplitter()
    {
        DragStarted += OnDragStartedRemember;
        DragCompleted += OnDragCompletedReport;
    }

    /// <summary>
    /// Raised after every arrow press along the splitter's axis (with <see cref="SplitterMovedEventArgs.AtLimit"/>
    /// set when the press could not move it), and once at the end of a mouse drag that changed the
    /// share. The window forwards it to the shell, which says the position through Status.
    /// </summary>
    public event EventHandler<SplitterMovedEventArgs>? Moved;

    /// <summary>
    /// False (the default): the spoken share is the definition before the splitter (left of it or
    /// above it). True: the definition after it (the log panel below the log splitter).
    /// </summary>
    public bool TracksNextDefinition { get; set; }

    /// <summary>Which splitter this is, so its sentence names the right panes; the list/detail splitter by default.</summary>
    public SplitterKind Kind { get; set; }

    /// <summary>The range the splitter reports, or null when it does not sit between two definitions of a Grid.</summary>
    internal SplitterRange? Range => TryGetRange(out SplitterRange range) ? range : null;

    /// <summary>The value the peer exposes (<see cref="SplitterPosition.ValueText"/>), or null when the splitter has no range.</summary>
    internal string? ValueText => Range is SplitterRange range ? SplitterPosition.ValueText(Kind, range.Share) : null;

    /// <summary>The peer: the stock GridSplitter peer plus the read-only Value pattern.</summary>
    /// <returns>An <see cref="AnnouncingGridSplitterAutomationPeer"/>.</returns>
    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new AnnouncingGridSplitterAutomationPeer(this);
    }

    /// <summary>
    /// An arrow key along the splitter's axis (Left and Right for the list/detail splitter, Up and
    /// Down for the log splitter) moves it to the next whole percent in that direction, or not at
    /// all at a limit, and reports the result either way. Every other key, and every key during a
    /// mouse drag, is the stock splitter's (Escape cancels a drag; an arrow across the axis moves nothing).
    /// </summary>
    /// <param name="e">The key event.</param>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!IsDragging && TryGetRange(out SplitterRange range))
        {
            int direction = StepDirection(e.Key, range);
            if (direction != 0)
            {
                e.Handled = true;
                if (SplitterPosition.Step(range.Length, range.Total, range.MinimumLength, range.MaximumLength, direction) is double length)
                {
                    MoveToLength(range, length, StockMover());
                }

                ReportPress(range.Share);
                return;
            }
        }

        base.OnKeyDown(e);
    }

    /// <summary>
    /// The stock move (the peer's Transform.Move, which calls the splitter's keyboard move and
    /// applies its own limits) adds the change to the first definition and takes it from the
    /// second; the result is reported by the caller once the layout has run.
    /// </summary>
    private void MoveToLength(SplitterRange range, double length, ITransformProvider? mover)
    {
        double definitionDelta = range.TracksFirstDefinition ? length - range.Length : range.Length - length;
        if (mover is null || Math.Abs(definitionDelta) < 0.01)
        {
            return;
        }

        // The stock keyboard path negates a horizontal change in a right-to-left splitter.
        bool columns = range.Direction == GridResizeDirection.Columns;
        double keyboardDelta = columns && FlowDirection == FlowDirection.RightToLeft ? -definitionDelta : definitionDelta;
        if (columns)
        {
            mover.Move(keyboardDelta, 0.0);
        }
        else
        {
            mover.Move(0.0, keyboardDelta);
        }
    }

    /// <summary>The stock Transform pattern of this splitter's peer (created on demand; it only moves the splitter).</summary>
    private ITransformProvider? StockMover()
    {
        return UIElementAutomationPeer.CreatePeerForElement(this)?.GetPattern(PatternInterface.Transform) as ITransformProvider;
    }

    /// <summary>
    /// +1 when the key grows the spoken pane, -1 when it shrinks it, 0 for an arrow across the axis
    /// or any other key (<see cref="SplitterPosition.Direction"/>, unit-tested in Presentation).
    /// </summary>
    private int StepDirection(Key key, SplitterRange range)
    {
        ArrowKey? arrow = key switch
        {
            Key.Left => ArrowKey.Left,
            Key.Right => ArrowKey.Right,
            Key.Up => ArrowKey.Up,
            Key.Down => ArrowKey.Down,
            _ => null,
        };
        return arrow is ArrowKey pressed
            ? SplitterPosition.Direction(pressed, range.Direction == GridResizeDirection.Columns, range.TracksFirstDefinition, FlowDirection == FlowDirection.RightToLeft)
            : 0;
    }

    private void OnDragStartedRemember(object sender, DragStartedEventArgs e)
    {
        _dragStartShare = Range is SplitterRange range ? range.Share : null;
    }

    /// <summary>
    /// The end of a mouse drag (or its cancel with Escape, which puts the splitter back): reported
    /// once, when the share differs from the share at the start of the drag.
    /// </summary>
    private void OnDragCompletedReport(object sender, DragCompletedEventArgs e)
    {
        int? before = _dragStartShare;
        _dragStartShare = null;
        if (before is not int start)
        {
            return;
        }

        UpdateLayout();
        if (Range is SplitterRange range && range.Share != start)
        {
            Moved?.Invoke(this, new SplitterMovedEventArgs(Kind, range.Share, atLimit: false));
        }
    }

    /// <summary>
    /// An arrow press along the axis: runs the pending layout so the definitions carry their new
    /// actual lengths, then reports the share; a press after which the share is still
    /// <paramref name="before"/> moved nothing and is reported as a press at a limit.
    /// </summary>
    private void ReportPress(int before)
    {
        UpdateLayout();
        if (Range is SplitterRange range)
        {
            Moved?.Invoke(this, new SplitterMovedEventArgs(Kind, range.Share, atLimit: range.Share == before));
        }
    }

    /// <summary>
    /// Finds the two definitions the stock splitter would resize (its own rules for
    /// ResizeDirection Auto and ResizeBehavior BasedOnAlignment included) and describes the spoken
    /// one: its length, the length the two share, its limits and its whole percent.
    /// </summary>
    private bool TryGetRange(out SplitterRange range)
    {
        range = default;
        if (Parent is not Grid grid)
        {
            return false;
        }

        GridResizeDirection direction = EffectiveDirection();
        bool columns = direction == GridResizeDirection.Columns;
        if ((int)GetValue(columns ? Grid.ColumnSpanProperty : Grid.RowSpanProperty) != 1)
        {
            return false;
        }

        int splitterIndex = (int)GetValue(columns ? Grid.ColumnProperty : Grid.RowProperty);
        (int firstIndex, int secondIndex) = EffectiveBehavior(direction) switch
        {
            GridResizeBehavior.PreviousAndCurrent => (splitterIndex - 1, splitterIndex),
            GridResizeBehavior.CurrentAndNext => (splitterIndex, splitterIndex + 1),
            _ => (splitterIndex - 1, splitterIndex + 1),
        };

        int count = columns ? grid.ColumnDefinitions.Count : grid.RowDefinitions.Count;
        if (firstIndex < 0 || secondIndex >= count)
        {
            return false;
        }

        Definition first = Describe(grid, firstIndex, columns);
        Definition second = Describe(grid, secondIndex, columns);
        bool tracksFirst = !TracksNextDefinition;
        Definition tracked = tracksFirst ? first : second;
        Definition other = tracksFirst ? second : first;
        int trackedIndex = tracksFirst ? firstIndex : secondIndex;
        int otherIndex = tracksFirst ? secondIndex : firstIndex;

        // Stock rule: the definition the splitter itself sits in can never be narrower than the splitter.
        double splitterLength = Math.Min(ActualWidth, ActualHeight);
        double minimum = trackedIndex == splitterIndex ? Math.Max(tracked.Minimum, splitterLength) : tracked.Minimum;
        double otherMinimum = otherIndex == splitterIndex ? Math.Max(other.Minimum, splitterLength) : other.Minimum;

        double total = first.Actual + second.Actual;
        double maximum = Math.Min(tracked.Maximum, total - otherMinimum);
        int percent = SplitterPosition.Percent(tracked.Actual, total);
        range = new SplitterRange(percent, tracked.Actual, total, minimum, maximum, tracksFirst, direction);
        return true;
    }

    /// <summary>The stock conversion of ResizeDirection Auto to Columns or Rows.</summary>
    private GridResizeDirection EffectiveDirection()
    {
        GridResizeDirection direction = ResizeDirection;
        if (direction != GridResizeDirection.Auto)
        {
            return direction;
        }

        if (HorizontalAlignment != HorizontalAlignment.Stretch)
        {
            return GridResizeDirection.Columns;
        }

        if (VerticalAlignment != VerticalAlignment.Stretch)
        {
            return GridResizeDirection.Rows;
        }

        return ActualWidth <= ActualHeight ? GridResizeDirection.Columns : GridResizeDirection.Rows;
    }

    /// <summary>The stock conversion of ResizeBehavior BasedOnAlignment to the two definitions.</summary>
    private GridResizeBehavior EffectiveBehavior(GridResizeDirection direction)
    {
        GridResizeBehavior behavior = ResizeBehavior;
        if (behavior != GridResizeBehavior.BasedOnAlignment)
        {
            return behavior;
        }

        if (direction == GridResizeDirection.Columns)
        {
            return HorizontalAlignment switch
            {
                HorizontalAlignment.Left => GridResizeBehavior.PreviousAndCurrent,
                HorizontalAlignment.Right => GridResizeBehavior.CurrentAndNext,
                _ => GridResizeBehavior.PreviousAndNext,
            };
        }

        return VerticalAlignment switch
        {
            VerticalAlignment.Top => GridResizeBehavior.PreviousAndCurrent,
            VerticalAlignment.Bottom => GridResizeBehavior.CurrentAndNext,
            _ => GridResizeBehavior.PreviousAndNext,
        };
    }

    private static Definition Describe(Grid grid, int index, bool columns)
    {
        if (columns)
        {
            ColumnDefinition column = grid.ColumnDefinitions[index];
            return new Definition(column.ActualWidth, column.MinWidth, column.MaxWidth);
        }

        RowDefinition row = grid.RowDefinitions[index];
        return new Definition(row.ActualHeight, row.MinHeight, row.MaxHeight);
    }

    /// <summary>One row or column as the range calculation sees it.</summary>
    private readonly record struct Definition(double Actual, double Minimum, double Maximum);
}

/// <summary>
/// What <see cref="AnnouncingGridSplitter.Moved"/> reports (A2-8): which splitter, the spoken pane's
/// share after the press or drag, and whether an arrow press could not move the splitter further
/// that way. The window hands it to the shell as it is; the shell says the sentence.
/// </summary>
public sealed class SplitterMovedEventArgs : EventArgs
{
    /// <summary>Creates the report.</summary>
    /// <param name="kind">Which splitter moved.</param>
    /// <param name="share">The spoken pane's share now, in whole percent.</param>
    /// <param name="atLimit">True when an arrow press could not move the splitter.</param>
    public SplitterMovedEventArgs(SplitterKind kind, int share, bool atLimit)
    {
        Kind = kind;
        Share = share;
        AtLimit = atLimit;
    }

    /// <summary>Which splitter moved.</summary>
    public SplitterKind Kind { get; }

    /// <summary>The spoken pane's share now (the list's, or the log panel's), in whole percent.</summary>
    public int Share { get; }

    /// <summary>True when an arrow press could not move the splitter further that way.</summary>
    public bool AtLimit { get; }
}

/// <summary>
/// What <see cref="AnnouncingGridSplitter"/> reports and the keyboard steps: the spoken pane's
/// share as a whole percent, its length and limits and the length the two definitions share
/// (device-independent pixels), plus how a change maps onto the stock move (which adds to the
/// first definition and takes from the second).
/// </summary>
/// <param name="Share">The spoken pane's share of <paramref name="Total"/>, a whole percent from 0 to 100 (<see cref="SplitterPosition.Percent"/>).</param>
/// <param name="Length">The spoken pane's actual length.</param>
/// <param name="Total">The actual length of the two definitions together.</param>
/// <param name="MinimumLength">The spoken pane's smallest length.</param>
/// <param name="MaximumLength">The spoken pane's largest length: what leaves the other definition its minimum.</param>
/// <param name="TracksFirstDefinition">True when the spoken pane is the first of the two definitions (the one before the splitter).</param>
/// <param name="Direction">Columns or Rows, as the stock splitter resolved it.</param>
internal readonly record struct SplitterRange(int Share, double Length, double Total, double MinimumLength, double MaximumLength, bool TracksFirstDefinition, GridResizeDirection Direction);
