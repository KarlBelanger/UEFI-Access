// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Serilog;
using UefiAccess.Presentation.ViewModels;

namespace UefiAccess.App;

/// <summary>
/// The Confirm change dialog: shows the plan the checks of a change
/// made, in read-only boxes, with the suspend check box when the checks offer the suspend, and
/// closes with the person's answer. Confirm change (Alt+F, or Space or Enter on the button; there
/// is no default button) closes it with a true dialog result; Cancel, Escape and the close button
/// with any other. What that answer stands for is the view model's
/// (<see cref="ConfirmChangeViewModel.Confirm"/> or <see cref="ConfirmChangeViewModel.Cancel"/>,
/// which <see cref="Platform.WpfPlatformInteractions.ConfirmChange"/> returns to the shell); the
/// dialog itself writes nothing, reads nothing and announces nothing. This code-behind holds only
/// what the markup cannot: the start and minimum sizes reduced to the work area, focus on What will
/// change when the dialog opens, the arrival rule of the main window's value boxes (a single-line
/// box selected whole when keyboard focus arrives from another control, a value of 512 characters
/// or more given the caret at its start instead, a selection of the person's own kept when focus
/// comes back from the box's context menu or another window, a multi-line box left alone), Up and
/// Down reading a box selected whole from its top, and the click of Confirm change. Nothing here
/// runs on a timer.
/// </summary>
public partial class ConfirmChangeWindow : Window
{
    private readonly ILogger _logger;
    private TextBox? _boxLeft;
    private int _boxLeftSelectionStart;
    private int _boxLeftSelectionLength;

    /// <summary>
    /// Creates the dialog over its view model and reduces its start-up and minimum sizes to the
    /// work area of the primary screen, so at a high display scale its buttons are never below the
    /// bottom edge (the boxes above them scroll instead). The caller sets the owner and shows it.
    /// </summary>
    /// <param name="viewModel">The texts of the plan and the check box.</param>
    /// <param name="logger">Optional logger; null disables logging.</param>
    /// <exception cref="ArgumentNullException"><paramref name="viewModel"/> is null.</exception>
    public ConfirmChangeWindow(ConfirmChangeViewModel viewModel, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _logger = logger ?? Serilog.Core.Logger.None;
        ViewModel = viewModel;
        InitializeComponent();
        FitToWorkArea(SystemParameters.WorkArea);
        DataContext = viewModel;
    }

    /// <summary>The view model the dialog shows.</summary>
    public ConfirmChangeViewModel ViewModel { get; }

    /// <summary>
    /// Reduces the start-up size (760 × 720) and the minimum size (480 × 360) to the work area. At
    /// 200 percent display scale a 1080-line screen is 492 device-independent pixels high without
    /// the taskbar.
    /// </summary>
    private void FitToWorkArea(Rect workArea)
    {
        if (workArea.IsEmpty || workArea.Width <= 0 || workArea.Height <= 0)
        {
            return;
        }

        MinWidth = Math.Min(MinWidth, workArea.Width);
        MinHeight = Math.Min(MinHeight, workArea.Height);
        Width = Math.Min(Width, workArea.Width);
        Height = Math.Min(Height, workArea.Height);
    }

    /// <summary>
    /// The dialog is on screen: focus goes to What will change, whose sentence is then selected
    /// whole (the arrival rule), so the first thing a screen reader reads is what the change does.
    /// When the dialog is activated after this, WPF gives keyboard focus to the same box.
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        bool focused = WhatWillChangeBox.Focus();
        _logger.Debug("Confirm change dialog shown for {TransactionId}; focus on What will change: {Focused}", ViewModel.Plan.SnapshotFolderName, focused);
    }

    /// <summary>Confirm change: the dialog closes with a true result; the shell then confirms the plan it showed.</summary>
    private void OnConfirmChangeClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    /// <summary>
    /// A box got keyboard focus: a single-line box is selected whole, or given the caret at its
    /// start for a value of 512 characters or more, by the main window's rule
    /// (<see cref="VariableDetailViewModel.SelectionOnKeyboardArrival"/>); a multi-line box is left
    /// as it is, so a screen reader reads its first line.
    /// </summary>
    private void OnBoxGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.OriginalSource is TextBox box)
        {
            ArriveInBox(box, e);
        }
    }

    /// <summary>A box lost keyboard focus: remembered when focus went to its context menu or to another window (<see cref="RememberBoxLeft"/>).</summary>
    private void OnBoxLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.OriginalSource is TextBox box)
        {
            RememberBoxLeft(box, e);
        }
    }

    /// <summary>
    /// Up or Down (no modifier) in a single-line box whose whole value is still selected, as
    /// arrival leaves it: the selection is collapsed at the start first, so the key reads the box
    /// one visual line at a time from the top instead of jumping to the end of the selection. The
    /// key itself is left to the box. Enter does nothing in any box: there is no default button.
    /// </summary>
    private void OnBoxesPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down
            && Keyboard.Modifiers == ModifierKeys.None
            && e.OriginalSource is TextBox { AcceptsReturn: false } box
            && box.Text.Length > 0
            && box.SelectionLength == box.Text.Length)
        {
            box.CaretIndex = 0;
        }
    }

    /// <summary>
    /// Keyboard focus reached a box: its selection is set by
    /// <see cref="VariableDetailViewModel.SelectionOnKeyboardArrival"/>, exactly as in the main
    /// window's detail pane. A return to the same box from its context menu or another window keeps
    /// the person's own selection; a click places the caret as usual.
    /// </summary>
    /// <param name="box">The box that got keyboard focus.</param>
    /// <param name="e">The focus change; its old focus tells a return from a context menu or another window.</param>
    private void ArriveInBox(TextBox box, KeyboardFocusChangedEventArgs e)
    {
        bool returning = ReferenceEquals(box, _boxLeft) && IsOffTheDialog(e.OldFocus);
        _boxLeft = null;
        (int Start, int Length)? selection = VariableDetailViewModel.SelectionOnKeyboardArrival(
            box.AcceptsReturn,
            box.Text.Length,
            clicked: IsMouseButtonDown(),
            returning,
            _boxLeftSelectionStart,
            _boxLeftSelectionLength);
        if (selection is null)
        {
            return;
        }

        if (selection.Value.Length == box.Text.Length)
        {
            SelectWholeValue(box);
        }
        else
        {
            box.Select(0, 0);
            box.ScrollToHome();
        }
    }

    /// <summary>
    /// A box lost keyboard focus: when focus went to its context menu or to no element of the
    /// dialog, the box and its selection are remembered, so a return to it keeps the person's own
    /// selection; focus that went to another control of the dialog forgets it.
    /// </summary>
    /// <param name="box">The box that lost keyboard focus.</param>
    /// <param name="e">The focus change.</param>
    private void RememberBoxLeft(TextBox box, KeyboardFocusChangedEventArgs e)
    {
        if (IsOffTheDialog(e.NewFocus))
        {
            _boxLeft = box;
            _boxLeftSelectionStart = box.SelectionStart;
            _boxLeftSelectionLength = box.SelectionLength;
        }
        else
        {
            _boxLeft = null;
        }
    }

    /// <summary>True when keyboard focus is on no element of this dialog: none, an element inside a context menu (a popup of its own), or an element of another window.</summary>
    private bool IsOffTheDialog(IInputElement? element)
    {
        if (element is not DependencyObject target)
        {
            return true;
        }

        for (DependencyObject? current = target; current is not null; current = ParentOf(current))
        {
            if (current is ContextMenu)
            {
                return true;
            }
        }

        return !ReferenceEquals(GetWindow(target), this);
    }

    /// <summary>The visual parent of an element, or its logical parent where it has no visual one (the main window's rule).</summary>
    private static DependencyObject? ParentOf(DependencyObject element)
    {
        DependencyObject? parent = element is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : null;
        return parent ?? LogicalTreeHelper.GetParent(element);
    }

    /// <summary>True while a mouse button is down: the focus change is a click's.</summary>
    private static bool IsMouseButtonDown()
    {
        return Mouse.LeftButton == MouseButtonState.Pressed || Mouse.RightButton == MouseButtonState.Pressed || Mouse.MiddleButton == MouseButtonState.Pressed;
    }

    /// <summary>
    /// Selects the whole text of a box keyboard focus has just reached, from its end back to its
    /// start (the stock Ctrl+Shift+Home command), so the caret and what the box shows is the start
    /// of the value; SelectAll is the fallback should the command not run.
    /// </summary>
    private static void SelectWholeValue(TextBox box)
    {
        box.CaretIndex = box.Text.Length;
        EditingCommands.SelectToDocumentStart.Execute(null, box);
        if (box.SelectionLength != box.Text.Length)
        {
            box.SelectAll();
        }
    }
}
