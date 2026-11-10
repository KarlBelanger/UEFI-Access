// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Serilog;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Presentation.Accessibility;
using UefiAccess.Presentation.Resources;
using UefiAccess.Presentation.ViewModels;

namespace UefiAccess.App;

/// <summary>
/// The Alpha 1 window. The XAML binds to <see cref="ShellViewModel"/> and its children; this
/// code-behind holds only what a view model cannot: the deliberate focus moves
/// (<see cref="MoveFocusTo"/>, driven by the view model through <see cref="Platform.WpfFocusSink"/>),
/// the F6 and Shift+F6 region cycle (a splitter counts as sitting between the two regions it
/// separates, and a region none of whose controls can take focus is skipped), Ctrl+F, Escape in
/// the filter box and the detail pane, the list keys that stock
/// controls do not provide (Enter or Space into the details, Ctrl+C to copy the row key, arrows
/// bridging the group headers and the per-group lists, Home and End across the whole list, Left
/// and Right on a group header, Ctrl+Left and Ctrl+Right to scroll the columns), the single
/// selection shared by the per-group lists, the focus move to the Status box when the Refresh,
/// Open baseline folder, Clear filter or Export visible list button is disabled under the focus it
/// holds, the focus kept inside the
/// list when a rebuild of the groups (Clear filter, Group by, Sort by, a load) replaces the
/// containers under it, the help text on each group header's toggle button, the Ready
/// announcement once the window is on screen, the detail field whose value Copy value copies, the
/// selection of a whole single-line value when keyboard focus arrives on its box from another
/// element (so a screen reader reads a wrapped value whole; a value too long for that gets the
/// caret at its start instead; a selection of the user's own is kept when focus comes back from
/// the box's context menu or another window, <see cref="ArriveInValueBox"/>),
/// the value boxes the in-place update of the detail pane kept, started afresh when the pane
/// shows another row (<see cref="StartValueBoxesAfresh"/>),
/// the splitters' reports, which the shell says through Status (finding A2-8,
/// <see cref="OnSplitterMoved"/>), and
/// the two things the deferred update of the detail pane needs from the view (finding A1-6: the
/// shell hands the update to <see cref="DetailScheduler"/> instead of running it inside the
/// selection change): the scheduler is held while a key or a mouse button is down in the
/// variable list and released when it comes up, so the pane is never updated while the key that
/// selected a row is still down, and each release starts its quiet period anew (finding A2-7);
/// and every entry of keyboard focus into the pane, by whatever
/// path (<see cref="FocusFirstDetailField"/> for Enter, Space and F6, and
/// <see cref="OnDetailPreviewGotKeyboardFocus"/> for Tab, a click and a screen reader's focus
/// request), runs the pending refresh first, so focus never lands in stale details, and so does an
/// Alt key anywhere in the window (<see cref="OnWindowPreviewKeyDown"/>), so every access key, the
/// Change group's among them, acts on the selected row. It also keeps
/// the window on the screen (round 2): the start-up and minimum sizes are reduced to the work area
/// (<see cref="WindowFit.ToWorkArea"/>), and the list/detail row gives up part of its minimum
/// height when the window has no room for it, after which the part above the Status region
/// scrolls (<see cref="FitPanesToWindow"/>), so the Status region, pinned to the bottom, is always
/// in view and nothing else is cut off. Since M3 it also holds the view's part of the Change group
///: the window does not close while a change is written
/// (<see cref="OnClosing"/>, through <see cref="ShellViewModel.TryClose"/>); Escape while the checks
/// of a change run cancels them, wherever focus is; Ctrl+Z in the boot order list puts the
/// firmware's order back, and Enter and Space on its rows do nothing (a write needs a button); a
/// Change button disabled under the focus it holds for the checks hands focus to the Status box,
/// as a load button does; the moved boot order row keeps its focus, and gets it back should WPF
/// regenerate its container; and the three focus targets of the change flows (the moved row, the
/// button that opened the Confirm change dialog, and the written variable's row after the reload).
/// Since M4 it holds the view's part of the Dell settings region: F6 and the shell's focus request land on the selected Dell row
/// (<see cref="FocusTarget.DellSelectedRow"/>), or on the Read Dell settings button should that row
/// not take focus; Ctrl+C on a Dell row copies its sentence through the shell, and Enter and Space
/// there do nothing; a Dell row that gets keyboard focus is selected; the state line is selected
/// whole on keyboard arrival like every single-line value box; the region tells the shell which of
/// its parts has keyboard focus (<see cref="ShellViewModel.OnDellFocus"/>), so Copy value copies
/// the Dell row or the state line there; every move of keyboard focus tells the shell that the
/// person moved on (<see cref="ShellViewModel.OnKeyboardFocusMoved"/>), so a Dell answer then moves
/// no focus; and the list's height is capped (<see cref="WindowFit.DellListMaximum"/>) so it scrolls
/// inside. The window never reads the Dell settings itself: the button's command is the shell's.
/// Nothing here runs on a timer (the detail pane's quiet period is the scheduler's own), and nothing here can change a firmware variable: every change goes
/// through the shell's write service after the person confirmed it in the dialog. Every focus rule
/// is documented in docs/Keyboard-Map.md.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ILogger _logger;
    private ShellViewModel? _shell;
    private string? _defaultCopyValueText;
    private bool _assigningCopyValue;
    private bool _refocusQueued;
    private string? _refocusHeaderKey;
    private bool _delegatingDetailFocus;
    private TextBox? _valueBoxLeft;
    private int _valueBoxLeftSelectionStart;
    private int _valueBoxLeftSelectionLength;
    private bool _bootOrderRefocusQueued;
    private BootOrderEntryViewModel? _bootOrderRowFocused;
    private Button? _changeControlWaiting;

    /// <summary>
    /// Creates the window and loads the XAML, then reduces its start-up and minimum sizes to the
    /// work area of the primary screen and centres it there, so that at a high display scale or
    /// on a small screen no part of it, the Status region least of all, starts below the bottom
    /// edge or under the taskbar. The shell is attached by <see cref="Attach"/>.
    /// </summary>
    public MainWindow()
    {
        _logger = (Application.Current as App)?.Logger ?? Serilog.Core.Logger.None;
        InitializeComponent();
        FitToWorkArea(SystemParameters.WorkArea);
        RefreshButton.IsEnabledChanged += OnLoadButtonIsEnabledChanged;
        OpenBaselineButton.IsEnabledChanged += OnLoadButtonIsEnabledChanged;
        ClearFilterButton.IsEnabledChanged += OnLoadButtonIsEnabledChanged;
        ExportVisibleListButton.IsEnabledChanged += OnLoadButtonIsEnabledChanged;
        foreach (Button button in ChangeButtons())
        {
            button.IsEnabledChanged += OnChangeButtonIsEnabledChanged;
        }

        ListDetailSplitter.Moved += OnSplitterMoved;
        LogSplitter.Moved += OnSplitterMoved;

        // Every move of keyboard focus, also one a control marked handled, tells the shell that the
        // person moved on. Focus that goes to a context menu raises its
        // GotKeyboardFocus outside the window's tree, so the LostKeyboardFocus of the control it left
        // is the window's only sign of that move.
        AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnWindowKeyboardFocusChanged), true);
        AddHandler(Keyboard.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnWindowKeyboardFocusChanged), true);
    }

    /// <summary>
    /// The converter of the Change group's visibility bindings: true shows a control, false
    /// collapses it, so a control the variable shown does not need takes no room and is no tab stop
    /// (the stock <see cref="BooleanToVisibilityConverter"/>).
    /// </summary>
    public static IValueConverter VisibleWhenTrue { get; } = new BooleanToVisibilityConverter();

    /// <summary>The attached shell, or null before <see cref="Attach"/>.</summary>
    public ShellViewModel? Shell => _shell;

    /// <summary>
    /// The scheduler the shell defers the detail pane's rebuild to (finding A1-6), set by the
    /// application before <see cref="Attach"/>; the window holds it while a key or a mouse
    /// button is down in the variable list and releases it when it comes up or the window is
    /// deactivated. Null (a shell with the immediate scheduler) means nothing to hold.
    /// </summary>
    public Platform.DispatcherDetailScheduler? DetailScheduler { get; set; }

    /// <summary>
    /// Attaches the shell: sets it as the data context and subscribes to the selection and detail
    /// changes the view has to mirror, and to the moves of the proposed boot order. Calling it again
    /// replaces the previous shell. Every clipboard and dialog access goes through the shell (Ctrl+C
    /// in the list calls <see cref="ShellViewModel.CopyKey"/>, the Confirm change dialog opens
    /// through the platform services the shell was composed with), so the window needs no platform
    /// service of its own.
    /// </summary>
    /// <param name="shell">The composed shell view model.</param>
    /// <exception cref="ArgumentNullException"><paramref name="shell"/> is null.</exception>
    public void Attach(ShellViewModel shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        if (_shell is not null)
        {
            _shell.List.SelectionChanged -= OnListSelectionChanged;
            _shell.List.Groups.CollectionChanged -= OnGroupsCollectionChanged;
            _shell.Detail.PropertyChanged -= OnDetailPropertyChanged;
            _shell.Detail.Change.Editor.Entries.CollectionChanged -= OnBootOrderEntriesCollectionChanged;
        }

        _shell = shell;
        _defaultCopyValueText = shell.Detail.CopyValueText;
        shell.List.SelectionChanged += OnListSelectionChanged;
        // Subscribed before the data context binds the groups control to the collection, so the
        // handler runs while the old containers (and the focus inside them) still exist; the same
        // for the boot order list, whose handler must see where focus was before a move.
        shell.List.Groups.CollectionChanged += OnGroupsCollectionChanged;
        shell.Detail.Change.Editor.Entries.CollectionChanged += OnBootOrderEntriesCollectionChanged;
        shell.Detail.PropertyChanged += OnDetailPropertyChanged;
        DataContext = shell;
    }

    /// <summary>
    /// Moves keyboard focus to a target (the <see cref="IFocusSink"/> contract). Every target has
    /// a fallback inside its region so the move lands somewhere useful, and the element is
    /// brought into view. The change flows add three targets: the selected row of the boot order
    /// list (after Ctrl+Z), the button that opened the Confirm change dialog (after Cancel or
    /// Escape there; <see cref="FocusChangeControl"/>), and the written variable's row after the
    /// live reload (<see cref="ShellViewModel.FocusRowKey"/>), or the first row when no visible row
    /// has that key (a filter hides it). Since M4 the selected row of the list of Dell BIOS settings
    /// (after a Dell read that answered, and on F6 into the region), or the Read Dell settings
    /// button when that row cannot take focus. Returns false when nothing could take focus.
    /// </summary>
    /// <param name="target">Where focus should go.</param>
    /// <returns>True when focus moved (or, for the button that opened the dialog, will move as soon as it is enabled again).</returns>
    public bool MoveFocusTo(FocusTarget target)
    {
        // A newer focus request replaces a wait for the button that opened the dialog.
        _changeControlWaiting = null;
        return target switch
        {
            FocusTarget.ListFirstRow => FocusListRow(FirstVisibleItem()) || FocusGroupHeader(0) || FocusElement(ListRegion),
            FocusTarget.ListSelectedRow => FocusListRow(_shell?.List.SelectedItem ?? FirstVisibleItem()) || FocusGroupHeader(0) || FocusElement(ListRegion),
            FocusTarget.Filter => FocusFilter(selectAll: true),
            FocusTarget.Status => FocusElement(StatusBox),
            FocusTarget.DetailFirstField => FocusFirstDetailField(),
            FocusTarget.Toolbar => FocusToolbar(),
            FocusTarget.LogPanel => FocusLogPanel(),
            FocusTarget.BootOrderEntry => FocusBootOrderRow(_shell?.Detail.Change.Editor.SelectedEntry) || FocusElement(BootOrderList),
            FocusTarget.ChangeControl => FocusChangeControl(),
            FocusTarget.ListRowByKey => FocusListRow(RowOfFocusKey()) || FocusListRow(FirstVisibleItem()) || FocusGroupHeader(0) || FocusElement(ListRegion),
            FocusTarget.DellSelectedRow => FocusDellRow(_shell?.Dell.SelectedRow ?? _shell?.Dell.Rows.FirstOrDefault()) || FocusElement(ReadDellSettingsButton),
            _ => false,
        };
    }

    // ---- Window-level keys ----------------------------------------------------------------

    /// <summary>
    /// Window loaded (once, when the user's opening of the application has put it on screen): the
    /// shell announces the Ready sentence now that the Status box has an automation peer for the
    /// live-region event, and the deliberate initial focus goes to the first toolbar button.
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _logger.Debug("Main window loaded, UefiAccess.App {Version}", ToolVersion.Current);
        _shell?.AnnounceReady();
        FocusToolbar();
    }

    /// <summary>
    /// The window is asked to close (its close button, Alt+F4, the window menu): while a change is
    /// written the shell refuses (<see cref="ShellViewModel.TryClose"/>), Status says so, and the
    /// window stays open with focus where it was; the write is never interrupted by the window's
    /// own means.
    /// </summary>
    /// <param name="e">The closing event; cancelled while a change is written.</param>
    protected override void OnClosing(CancelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (_shell is not null && !_shell.TryClose())
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }

    /// <summary>
    /// F6 and Shift+F6 cycle the regions, skipping a region none of whose controls can take focus
    /// (<see cref="FocusFirstRegionFrom"/>); Ctrl+F focuses the filter box and selects its text, and
    /// while the box is disabled (a change is written) the shell says so instead
    /// (<see cref="ShellViewModel.OnFilterUnavailable"/>). From
    /// a splitter, which belongs to no region, F6 goes to the region after it and Shift+F6 to the
    /// region before it (List and Detail around the first splitter, Detail and Log around the
    /// second); from anywhere else outside a region F6 starts at the Toolbar and Shift+F6 at Status.
    /// Escape while the checks of a change run cancels them, wherever focus is (the Change button
    /// that started them is disabled meanwhile and has handed focus to the Status box, so the
    /// Change group is often not where focus is); focus stays where it is and the shell says
    /// "Cancelled; nothing changed." Any other time Escape keeps its meaning where it is pressed.
    /// A key press is also the person's own next step, so it ends a wait for the button that
    /// opened the dialog (<see cref="FocusChangeControl"/>). An Alt key (Alt alone, or the letter of
    /// an Alt combination) runs the detail pane's pending refresh first
    /// (<see cref="ShellViewModel.EnsureDetailCurrent"/>): an access key clicks its button where it
    /// is, without moving focus into the pane, and the Change group's buttons shown and the variable
    /// they act on are the pane's, so after a selection change still waiting out the quiet period
    /// (finding A2-7) every access key, the Change group's among them, acts on the selected row, not
    /// on the row shown before. This handler tunnels before the variable list's hold and before the
    /// access key is resolved; nothing is handled, so the key keeps its meaning.
    /// </summary>
    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        _changeControlWaiting = null;
        if (e.Key == Key.System)
        {
            // Every access key, the Change group's among them, acts on the row the pane shows: bring
            // it up to date.
            _shell?.EnsureDetailCurrent();
        }

        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None && _shell is not null && _shell.Detail.Change.CancelChecks())
        {
            e.Handled = true;
            _logger.Debug("Escape cancelled the checks of a change");
            return;
        }

        if (e.Key == Key.F6 && (Keyboard.Modifiers & ~ModifierKeys.Shift) == ModifierKeys.None)
        {
            bool backwards = Keyboard.Modifiers == ModifierKeys.Shift;
            DependencyObject? focused = Keyboard.FocusedElement as DependencyObject;
            Region target;
            if (SplitterRegions(focused) is (Region before, Region after))
            {
                target = backwards ? before : after;
            }
            else
            {
                Region? current = RegionOf(focused);
                target = current is null
                    ? (backwards ? Region.Status : Region.Toolbar)
                    : (backwards ? RegionOrder.Previous(current.Value) : RegionOrder.Next(current.Value));
            }

            e.Handled = true;
            FocusFirstRegionFrom(target, backwards);
            return;
        }

        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            if (!FocusFilter(selectAll: true))
            {
                // The filter box is disabled while a change is written: say so, focus stays.
                _shell?.OnFilterUnavailable();
            }
        }
    }

    /// <summary>
    /// Keyboard focus moved, anywhere in the window and by whatever path (Tab, Shift+Tab, F6, a
    /// click, an arrow key that moves focus, a screen reader's focus request, a context menu
    /// opened, a switch to another window): the shell is told the person moved on
    /// (<see cref="ShellViewModel.OnKeyboardFocusMoved"/>), so a Dell read in flight is answered
    /// without a focus move. The shell says nothing about it.
    /// One move raises both events; the second call changes nothing. The Dell answer's own focus
    /// move comes after the read ended, so it does not count.
    /// </summary>
    private void OnWindowKeyboardFocusChanged(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OldFocus, e.NewFocus))
        {
            _shell?.OnKeyboardFocusMoved();
        }
    }

    /// <summary>
    /// F6 or Shift+F6: focus goes to <paramref name="target"/>, or, when no control of that region
    /// can take focus (the Filter region while a change is written, whose three controls are all
    /// disabled then), to the first region after it in the key's direction that can; only the
    /// region focus lands in is announced. The Status box can always take focus, so the walk ends
    /// within one round of the regions (seven since M4).
    /// </summary>
    /// <param name="target">The region the key aims at.</param>
    /// <param name="backwards">True for Shift+F6.</param>
    private void FocusFirstRegionFrom(Region target, bool backwards)
    {
        Region candidate = target;
        for (int step = 0; step < RegionOrder.All.Count; step++)
        {
            if (FocusRegion(candidate))
            {
                _shell?.OnRegionArrived(candidate);
                return;
            }

            _logger.Debug("F6: no control of the {Region} region can take focus; the next region is tried", candidate);
            candidate = backwards ? RegionOrder.Previous(candidate) : RegionOrder.Next(candidate);
        }
    }

    /// <summary>
    /// A key came up anywhere in the window: the hold a key press in the variable list put on
    /// the detail pane's rebuild ends, and the pending refresh, when there is one, is posted to
    /// run once the quiet period has passed with no key or button going down in the list
    /// (finding A2-7). Every key up releases, not only the one that held, because the key may have
    /// moved focus out of the list before it came up (Enter, F6, Tab).
    /// </summary>
    private void OnWindowPreviewKeyUp(object sender, KeyEventArgs e)
    {
        DetailScheduler?.Release();
    }

    /// <summary>
    /// A mouse button came up anywhere in the window: the same release as a key up. A click is
    /// the person's own next step, so it also ends a wait for the button that opened the dialog.
    /// </summary>
    private void OnWindowPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        DetailScheduler?.Release();
        _changeControlWaiting = null;
    }

    /// <summary>
    /// The window lost activation while a key or a button may still be down (Alt+Tab, a dialog
    /// opened by an access key): the key up will reach another window, so the hold ends here.
    /// </summary>
    private void OnDeactivated(object sender, EventArgs e)
    {
        DetailScheduler?.Release();
    }

    /// <summary>
    /// A splitter moved by an arrow press or a drag, or an arrow press met a limit (finding A2-8):
    /// the shell says the splitter's position as a sentence through the Status box, the live region
    /// NVDA and Narrator both speak (<see cref="ShellViewModel.OnSplitterMoved"/>). Focus stays on
    /// the splitter.
    /// </summary>
    private void OnSplitterMoved(object? sender, Platform.SplitterMovedEventArgs e)
    {
        _shell?.OnSplitterMoved(e.Kind, e.Share, e.AtLimit);
    }

    /// <summary>
    /// Refresh or Open baseline folder was disabled (a load started) while it held keyboard focus.
    /// That happens whenever a load starts with focus on either of the two buttons, whatever
    /// started it: Enter, Space or a click on the button, and equally Alt+R or Alt+O pressed while
    /// focus is on Refresh or Open baseline folder, which is where the window puts focus when it
    /// opens (both buttons are disabled while a load runs, and a disabled control cannot keep
    /// focus). WPF would otherwise throw the focus to the nearest focusable ancestor, an unnamed
    /// container; instead focus goes deliberately to the Status box, which reads "Loading
    /// variables, please wait." (docs/Keyboard-Map.md section 9). When the load finishes the
    /// shell moves focus to the first row or to Status as usual. Alt+R and Alt+O pressed with
    /// focus anywhere else leave focus untouched. Since M3 the same rule covers Open baseline
    /// folder and Export visible list, disabled while the checks of a change run (Alt+V, Alt+B,
    /// Alt+M or Alt+P pressed with focus on either: Status reads "Checking the change, please
    /// wait."), and Clear filter, disabled with them while a change is written and until its
    /// result is said.
    /// </summary>
    private void OnLoadButtonIsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is false && sender is UIElement { IsKeyboardFocused: true } && FocusElement(StatusBox))
        {
            _logger.Debug("A toolbar button was disabled while focused; focus moved to the Status box");
        }
    }

    /// <summary>
    /// A button of the Change group was disabled or enabled again. Disabled while it holds keyboard
    /// focus: the command it runs has started (a command is disabled while it runs, and every
    /// Change command while the checks of a change or a write run), which happens whenever the
    /// button is pressed with focus on it (Enter, Space, a click, or its access key there). The
    /// same rule as for the load buttons applies, with one difference: focus moves to the Status
    /// box, which reads "Checking the change, please wait.", only if the button is still disabled
    /// once the key press has been handled (one deferred step at the Loaded priority, above the
    /// priority at which WPF would itself move focus off a disabled control; not a timer). A
    /// command that answers at once and is enabled again in the same key press ("Nothing to change:
    /// ...", "No snapshot to restore: ...") leaves focus on the button, as docs/Keyboard-Map.md
    /// section 9 says. Enabled again: when it is the button that opened the Confirm change dialog
    /// and focus is waiting for it (<see cref="FocusChangeControl"/>), it takes focus now.
    /// </summary>
    private void OnChangeButtonIsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        if (e.NewValue is true)
        {
            if (ReferenceEquals(button, _changeControlWaiting))
            {
                _changeControlWaiting = null;
                if (FocusElement(button))
                {
                    button.BringIntoView();
                    _logger.Debug("The button that opened the dialog is enabled again and has focus");
                }
            }

            return;
        }

        if (button.IsKeyboardFocused)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => MoveFocusOffDisabledChangeButton(button)));
        }
    }

    /// <summary>The deferred half of <see cref="OnChangeButtonIsEnabledChanged"/>: a Change button still disabled under the focus hands it to the Status box.</summary>
    private void MoveFocusOffDisabledChangeButton(Button button)
    {
        if (!button.IsEnabled && button.IsKeyboardFocused && FocusElement(StatusBox))
        {
            _logger.Debug("A Change button stayed disabled while focused; focus moved to the Status box");
        }
    }

    // ---- Window size and layout -----------------------------------------------------------

    /// <summary>
    /// Reduces the start-up size (1200 × 800) and the minimum size (900 × 600) to the work area
    /// (<see cref="WindowFit.ToWorkArea"/>) and centres the window in it. At 150 percent display
    /// scale a 1080-line screen is 672 device-independent pixels high without the taskbar, less
    /// than the 800 the window asks for; at 200 percent it is 492, less than the minimum.
    /// </summary>
    private void FitToWorkArea(Rect workArea)
    {
        WindowSize size = WindowFit.ToWorkArea(workArea.Width, workArea.Height);
        MinWidth = size.MinWidth;
        MinHeight = size.MinHeight;
        Width = size.Width;
        Height = size.Height;
        if (!workArea.IsEmpty && workArea.Width > 0 && workArea.Height > 0)
        {
            Left = workArea.Left + ((workArea.Width - size.Width) / 2);
            Top = workArea.Top + ((workArea.Height - size.Height) / 2);
        }

        _logger.Debug("Window sized {Width} x {Height} (minimum {MinWidth} x {MinHeight}) for a work area of {WorkWidth} x {WorkHeight}", size.Width, size.Height, size.MinWidth, size.MinHeight, workArea.Width, workArea.Height);
    }

    /// <summary>The part above Status (a window resize, a Status region grown or shrunk by its sentence), the toolbar, the filter row (a text size change, the toolbar wrapping) or, since M4, the Dell settings region (a longer state line) changed size: the rows are fitted again.</summary>
    private void OnLayoutPartSizeChanged(object sender, SizeChangedEventArgs e)
    {
        FitPanesToWindow();
    }

    /// <summary>
    /// Fits the regions above Status into the height the Status region leaves them (the scroll
    /// viewer's height). First, since M4, the list of Dell BIOS settings is capped
    /// (<see cref="WindowFit.DellListMaximum"/>: a quarter of that height, never less than four
    /// lines at the list's text size, <see cref="DellLineHeight"/>), so the Dell settings region,
    /// whose row sizes to its content, does not push the rows below it down at a large text size;
    /// the list scrolls inside, and its rows stay in the UI Automation tree. The list/detail row
    /// keeps its minimum of 160 while there is room for it and gives way first when there is not,
    /// down to 80 (<see cref="WindowFit.ListDetailMinimum"/>): the room is that height minus the
    /// toolbar, the filter row, the log splitter's row, the Dell settings region and the log
    /// panel's minimum (its header and the Copy log button). The grid itself is as high as the
    /// scroll viewer but never lower than those rows need, so when even that does not fit (900 ×
    /// 600 at 200 percent text) the part above Status scrolls instead of being cut. Without this, a
    /// fixed 160 pushed the rows below it, the Status region last, out of the window at a high text
    /// size in a small window.
    /// </summary>
    private void FitPanesToWindow()
    {
        double dellList = WindowFit.DellListMaximum(PanesScroller.ActualHeight, DellLineHeight());
        if (Math.Abs(DellSettingsList.MaxHeight - dellList) >= 1)
        {
            DellSettingsList.MaxHeight = dellList;
        }

        double fixedRows = 0;
        foreach (RowDefinition row in PanesGrid.RowDefinitions)
        {
            if (!ReferenceEquals(row, ListDetailRow) && !ReferenceEquals(row, LogRow))
            {
                fixedRows += row.ActualHeight;
            }
        }

        double listDetail = WindowFit.ListDetailMinimum(PanesScroller.ActualHeight - fixedRows - LogRow.MinHeight);
        if (Math.Abs(ListDetailRow.MinHeight - listDetail) >= 1)
        {
            ListDetailRow.MinHeight = listDetail;
        }

        // Never more than the viewer's height while the rows fit (the row minimum is rounded down),
        // so the scroll bar appears only when they do not.
        double panes = fixedRows + ListDetailRow.MinHeight + LogRow.MinHeight;
        if (Math.Abs(PanesGrid.MinHeight - panes) > 0.01)
        {
            PanesGrid.MinHeight = panes;
        }
    }

    /// <summary>
    /// The height of one line of the list of Dell BIOS settings at its text size, in whole
    /// device-independent pixels, with the row's border and padding: what
    /// <see cref="WindowFit.DellListMaximum"/> counts its minimum of four lines in (the column
    /// header and three rows). It follows the system message font, as every text of the window does.
    /// </summary>
    private double DellLineHeight()
    {
        return Math.Ceiling(DellSettingsList.FontSize * DellSettingsList.FontFamily.LineSpacing) + 8;
    }

    // ---- Filter row -----------------------------------------------------------------------

    /// <summary>Every keystroke applies the filter synchronously (the binding does it); the shell then announces the count.</summary>
    private void OnFilterTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_shell is null || !IsLoaded)
        {
            return;
        }

        _shell.List.FilterText = FilterBox.Text;
        _shell.OnFilterApplied();
    }

    /// <summary>Escape in the filter box clears it and returns focus to the list at the selected (or first) row.</summary>
    private void OnFilterPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _shell is null)
        {
            return;
        }

        e.Handled = true;
        if (_shell.ClearFilterCommand.CanExecute(null))
        {
            _shell.ClearFilterCommand.Execute(null);
        }

        MoveFocusTo(FocusTarget.ListSelectedRow);
    }

    /// <summary>
    /// Group by or Sort by changed: the list regroups or re-sorts at once and the shell is told
    /// (<see cref="ShellViewModel.OnFilterApplied"/>), which says the count only when the filter
    /// text or a count changed; a new grouping or order alone shows the same rows, so Status says
    /// nothing (finding A2-9, the tester's A2-7) and the combo box's own speech of the new choice is all
    /// that is heard.
    /// </summary>
    private void OnGroupOrSortChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_shell is null || !IsLoaded)
        {
            return;
        }

        if (GroupByBox.SelectedValue is GroupBy groupBy)
        {
            _shell.List.GroupBy = groupBy;
        }

        if (SortByBox.SelectedValue is SortBy sortBy)
        {
            _shell.List.SortBy = sortBy;
        }

        _shell.OnFilterApplied();
    }

    // ---- Variable list --------------------------------------------------------------------

    /// <summary>
    /// The list keys stock controls do not provide, so the groups read as one list. Anywhere in
    /// the list: Home and End move to the start and the end of the whole list (the first group's
    /// first row, or its header when it is collapsed; the last group's last row, or its header),
    /// Ctrl+Left and Ctrl+Right scroll the columns by a page (the outer scroll viewer is not a tab
    /// stop). On a row: Down at the last row moves to the next group header, Up at the first row
    /// to this group's header, Left to this group's header, Enter or Space into the detail pane,
    /// Ctrl+C copies the row key. On a group header: Down enters the group (or moves to the next
    /// header), Up moves to the previous group's last row (or header), Left collapses, Right
    /// expands or enters the group. Page Up, Page Down and type-ahead stay the stock control's and
    /// work inside the current group.
    /// </summary>
    private void OnGroupsPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Before the stock control moves the selection: the detail pane's rebuild waits until this
        // key comes up (OnWindowPreviewKeyUp), and a repeating key keeps the hold, so the screen
        // reader fetches the row's name from an idle UI thread (finding A1-6). Any key: one that
        // changes no selection holds nothing pending, and Enter, Space and F6 flush on their own.
        DetailScheduler?.Hold();
        if (_shell is null)
        {
            return;
        }

        DependencyObject? focused = Keyboard.FocusedElement as DependencyObject;
        bool plain = Keyboard.Modifiers == ModifierKeys.None;

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.Left or Key.Right)
        {
            e.Handled = true;
            if (e.Key == Key.Left)
            {
                ListScroller.PageLeft();
            }
            else
            {
                ListScroller.PageRight();
            }

            return;
        }

        if (FindAncestor<ListViewItem>(focused) is { DataContext: VariableItemViewModel item } row
            && ItemsControl.ItemsControlFromItemContainer(row) is ListView listView
            && listView.DataContext is VariableGroupViewModel rowGroup)
        {
            int groupIndex = _shell.List.Groups.IndexOf(rowGroup);
            int rowIndex = listView.ItemContainerGenerator.IndexFromContainer(row);
            switch (e.Key)
            {
                case Key.Down when plain && rowIndex >= listView.Items.Count - 1:
                    e.Handled = FocusGroupHeader(groupIndex + 1);
                    break;
                case Key.Up when plain && rowIndex == 0:
                    e.Handled = FocusGroupHeader(groupIndex);
                    break;
                case Key.Home when plain:
                    e.Handled = FocusStartOfList();
                    break;
                case Key.End when plain:
                    e.Handled = FocusEndOfList();
                    break;
                case Key.Left when plain:
                    e.Handled = FocusGroupHeader(groupIndex);
                    break;
                case Key.Enter when plain:
                case Key.Space when plain:
                    e.Handled = true;
                    FocusFirstDetailField();
                    break;
                case Key.C when Keyboard.Modifiers == ModifierKeys.Control:
                    e.Handled = true;
                    _shell.CopyKey(item);
                    break;
            }

            return;
        }

        if (FindAncestor<ListView>(focused) is null
            && FindAncestor<Expander>(focused) is { DataContext: VariableGroupViewModel headerGroup } expander
            && plain)
        {
            int groupIndex = _shell.List.Groups.IndexOf(headerGroup);
            switch (e.Key)
            {
                case Key.Down:
                    e.Handled = true;
                    if (!(expander.IsExpanded && FocusRowAt(groupIndex, 0)))
                    {
                        FocusGroupHeader(groupIndex + 1);
                    }

                    break;
                case Key.Up:
                    e.Handled = true;
                    FocusEndOfPreviousGroup(groupIndex);
                    break;
                case Key.Home:
                    e.Handled = true;
                    FocusStartOfList();
                    break;
                case Key.End:
                    e.Handled = true;
                    FocusEndOfList();
                    break;
                case Key.Left:
                    e.Handled = true;
                    if (expander.IsExpanded)
                    {
                        expander.IsExpanded = false;
                    }

                    break;
                case Key.Right:
                    e.Handled = true;
                    if (!expander.IsExpanded)
                    {
                        expander.IsExpanded = true;
                    }
                    else
                    {
                        FocusRowAt(groupIndex, 0);
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// A mouse button went down in the variable list (a click selects a row on the way down):
    /// the same hold as a key press, released by <see cref="OnWindowPreviewMouseUp"/>.
    /// </summary>
    private void OnGroupsPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        DetailScheduler?.Hold();
    }

    /// <summary>
    /// The list is about to be rebuilt (the view model clears its groups before it re-adds them:
    /// Clear filter, a Group by or Sort by change, a load). Clearing destroys every expander and
    /// per-group list, and when keyboard focus was on one of their rows or headers WPF would drop
    /// it on the window. So, on the Reset that starts a rebuild, remember whether focus was inside
    /// the groups control and, when it was on a header, which group, and queue one re-focus for
    /// after the new containers are rendered (Dispatcher.BeginInvoke at Loaded priority, the same
    /// one-off deferral the focus sink uses; nothing timed). docs/Keyboard-Map.md section 9.
    /// </summary>
    private void OnGroupsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Reset || _refocusQueued || !GroupsControl.IsKeyboardFocusWithin)
        {
            return;
        }

        DependencyObject? focused = Keyboard.FocusedElement as DependencyObject;
        _refocusHeaderKey = FindAncestor<ListViewItem>(focused) is null
            && FindAncestor<Expander>(focused) is { DataContext: VariableGroupViewModel group }
            ? group.Key
            : null;
        _refocusQueued = true;
        _logger.Debug("The list is rebuilt under the focus; re-focusing after the rebuild ({Where})", _refocusHeaderKey is null ? "row" : "group header");
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(RefocusListAfterRebuild));
    }

    /// <summary>
    /// The rebuild that <see cref="OnGroupsCollectionChanged"/> saw is rendered: put focus back on
    /// the header of the group it was on when that group still exists, otherwise on the selected
    /// row, the first row, or the first header. Skipped when a deliberate move (Escape in the
    /// filter box, the shell's focus request after a load) already put focus somewhere else.
    /// </summary>
    private void RefocusListAfterRebuild()
    {
        _refocusQueued = false;
        string? headerKey = _refocusHeaderKey;
        _refocusHeaderKey = null;
        if (_shell is null)
        {
            return;
        }

        // Where focus is now: on the window or the list region (WPF's fallback for a focused
        // element that was removed), still on the removed container (no longer visible), or, when
        // something moved it on purpose meanwhile, on a visible element of another region.
        DependencyObject? focused = Keyboard.FocusedElement as DependencyObject;
        if (focused is UIElement { IsVisible: true } && RegionOf(focused) is Region region && region != Region.List)
        {
            _logger.Debug("Focus moved to the {Region} region during the rebuild on purpose; nothing to restore", region);
            return;
        }

        if (headerKey is not null)
        {
            for (int i = 0; i < _shell.List.Groups.Count; i++)
            {
                if (string.Equals(_shell.List.Groups[i].Key, headerKey, StringComparison.Ordinal) && FocusGroupHeader(i))
                {
                    return;
                }
            }
        }

        MoveFocusTo(FocusTarget.ListSelectedRow);
    }

    /// <summary>
    /// A group expander was created: its help text is copied onto the header toggle button inside
    /// the stock template, because that button, not the expander, is what takes keyboard focus,
    /// and a screen reader reads the help text of the focused element. NVDA also speaks the
    /// expander itself, with the same help text, whenever focus enters it from outside (for example
    /// Up from a group header into the previous group's last row), before the row: the group-edge
    /// speech of finding A2-7 (re-analysis of 3 October 2026).
    /// </summary>
    private void OnGroupHeaderLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Expander expander && FindDescendant<ToggleButton>(expander) is ToggleButton header)
        {
            AutomationProperties.SetHelpText(header, Strings.GroupHeaderHelp);
        }
    }

    /// <summary>
    /// A row was selected in one of the per-group lists: it becomes the shell's selection at once
    /// (removals are ignored), so the row is selected and Ctrl+C and Copy value see it on the same
    /// key press. Only the detail pane's rebuild is deferred, by the shell's scheduler, until the
    /// key has come up, the list has been quiet for the quiet period and the dispatcher is idle
    /// (findings A1-6 and A2-7); the window does not defer the selection itself.
    /// </summary>
    private void OnGroupListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_shell is null || e.AddedItems.Count == 0 || e.AddedItems[0] is not VariableItemViewModel item)
        {
            return;
        }

        _shell.List.SelectedItem = item;
    }

    /// <summary>
    /// The shell's selection changed: every per-group list that still shows another row clears
    /// it, so exactly one row is selected across the groups (a ListView keeps its old selection
    /// when the bound item is not one of its own).
    /// </summary>
    private void OnListSelectionChanged(object? sender, VariableItemViewModel? selected)
    {
        foreach (ListView listView in GroupListViews())
        {
            if (listView.SelectedItem is not null && !ReferenceEquals(listView.SelectedItem, selected))
            {
                listView.UnselectAll();
            }
        }
    }

    // ---- Detail pane ----------------------------------------------------------------------

    /// <summary>
    /// Escape anywhere in the detail pane returns focus to the list at the same row: the selected
    /// row, which is what the pane shows once focus is inside it (every entry refreshes it) and
    /// is never stale; the pane's own row is the fallback for a pane entered with nothing selected.
    /// Up and Down in a value box are the box's own, from the top of a value that is still
    /// selected whole (<see cref="StartLinesAtTheTop"/>).
    /// </summary>
    private void OnDetailPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down)
        {
            StartLinesAtTheTop(e);
            return;
        }

        if (e.Key != Key.Escape || _shell is null)
        {
            return;
        }

        e.Handled = true;
        VariableItemViewModel? item = _shell.List.SelectedItem ?? _shell.Detail.Item ?? FirstVisibleItem();
        if (!FocusListRow(item))
        {
            _ = FocusGroupHeader(0) || FocusElement(ListRegion);
        }
    }

    /// <summary>
    /// Up or Down (no modifier) in a single-line value box whose whole value is still selected,
    /// as keyboard arrival leaves it (<see cref="SelectWholeValueOnArrival"/>): the selection is
    /// collapsed at the start of the value first, so the key then moves the caret one visual
    /// line from the top and a screen reader reads that line (Down reads the second line, Up the
    /// first), instead of the stock jump to the end of the selection. The key itself is left to
    /// the text box. A multi-line box (the hex dump) is never selected on arrival and is untouched.
    /// </summary>
    private static void StartLinesAtTheTop(KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.None
            && e.OriginalSource is TextBox { AcceptsReturn: false } box
            && box.Text.Length > 0
            && box.SelectionLength == box.Text.Length)
        {
            box.CaretIndex = 0;
        }
    }

    /// <summary>
    /// Keyboard focus is about to enter or move inside the detail pane by any path: Tab from the
    /// list/detail splitter to the heading and on to the fields, Shift+Tab from the log splitter,
    /// a mouse click on a field, a screen reader's "move focus to navigator object" (a UI
    /// Automation SetFocus), and the paths that already run it (Enter, Space, F6 through
    /// <see cref="FocusFirstDetailField"/>). The refresh a selection change left pending runs
    /// first, so the pane focus lands in belongs to the selected row. When the element focus was
    /// heading for is a detail field and either that refresh changed the row the pane shows or the
    /// field is one the pane no longer shows (the click or the reader targeted the pane as it was),
    /// that focus change is cancelled and focus is delegated to the first field of the updated pane
    /// instead. A field the refresh removed is never focused, so WPF never drops focus on the
    /// window; a field the refresh kept for another row (the update is made in place, so a box can
    /// outlive the row it was aimed at) is not focused either. A refresh that shows the same row
    /// again changes no field, so focus goes where it was aimed (before the update was made in
    /// place, every box was replaced and such focus went to the first field). Delegating
    /// focus during PreviewGotKeyboardFocus is a pattern WPF's keyboard device provides for; the
    /// delegated move runs through this handler again, finds the pane current, and is never
    /// redirected itself (the guard below), so the delegation cannot recurse. Cheap when nothing is
    /// pending, which is every move between fields.
    /// </summary>
    private void OnDetailPreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_shell is null)
        {
            return;
        }

        VariableItemViewModel? shown = _shell.Detail.Item;
        _shell.EnsureDetailCurrent();
        bool rowChanged = !ReferenceEquals(shown, _shell.Detail.Item);
        if (_delegatingDetailFocus
            || e.NewFocus is not FrameworkElement { DataContext: DetailFieldViewModel field } target
            || (!rowChanged && (target.IsVisible && IsShown(field))))
        {
            return;
        }

        e.Handled = true;
        _logger.Debug("Focus was heading for a detail field of the pane before the refresh; delegated to the first field of the current pane");
        _delegatingDetailFocus = true;
        try
        {
            FocusFirstDetailField();
        }
        finally
        {
            _delegatingDetailFocus = false;
        }
    }

    /// <summary>True when the field belongs to the sections the detail pane shows now.</summary>
    private bool IsShown(DetailFieldViewModel field)
    {
        if (_shell is null)
        {
            return false;
        }

        foreach (DetailSectionViewModel section in _shell.Detail.Sections)
        {
            if (section.Fields.Contains(field))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A detail field got focus: Copy value now copies that field's value, and the box's selection
    /// is set as keyboard arrival sets it (<see cref="ArriveInValueBox"/>).
    /// </summary>
    private void OnDetailFieldGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.OriginalSource is TextBox { DataContext: DetailFieldViewModel field } box)
        {
            AssignCopyValue(field.Value);
            ArriveInValueBox(box, field.IsMultiline, e);
        }
    }

    /// <summary>The Writability line got focus: the same arrival as every single-line field (its short text is selected whole).</summary>
    private void OnReadOnlyStatusGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, ReadOnlyStatusBox))
        {
            ArriveInValueBox(ReadOnlyStatusBox, isMultiline: false, e);
        }
    }

    /// <summary>The Writability line lost focus: remembered when focus went to its context menu or to another window (<see cref="RememberValueBoxLeft"/>).</summary>
    private void OnReadOnlyStatusLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, ReadOnlyStatusBox))
        {
            RememberValueBoxLeft(ReadOnlyStatusBox, e);
        }
    }

    /// <summary>
    /// Keyboard focus reached a read-only value box of the detail pane (a field or the Writability
    /// line): its selection is set by <see cref="VariableDetailViewModel.SelectionOnKeyboardArrival"/>
    /// (docs/Keyboard-Map.md section 7).
    /// <list type="bullet">
    /// <item><description>
    /// From another element of the window (Tab, Shift+Tab, F6, Enter or Space on a row, a screen
    /// reader's "move focus", which is a UI Automation SetFocus): a single-line value is selected
    /// whole (<see cref="SelectWholeValueOnArrival"/>), so NVDA reads it whole as "selected" and
    /// the value; one of <see cref="VariableDetailViewModel.LongValueLength"/> characters or more,
    /// which NVDA would read only as "selected N characters", gets the caret at its start instead
    /// (<see cref="PutCaretAtTheStart"/>), so NVDA reads its first line on screen after its help
    /// text, which gives its length and says that Down reads it line by line.
    /// </description></item>
    /// <item><description>
    /// Back to the box focus left for the box's own context menu or for another window
    /// (<see cref="RememberValueBoxLeft"/>): a WPF text box's stock context menu takes keyboard
    /// focus while it is open and gives it back when it closes, which happens on the mouse-up of
    /// a menu item's click (no button is down then) or after Shift+F10, the Applications key and
    /// Escape or Enter; Alt+Tab back and a closed dialog give focus back the same way. A selection
    /// or caret position of the user's own that the box had when focus left it is kept, so the
    /// context menu's Copy copies the part the user selected; a box still as arrival left it gets
    /// the same selection again, which changes nothing.
    /// </description></item>
    /// <item><description>A click (a mouse button is down while it moves focus) and a multi-line box (the hex dump) are left alone: the click places the caret as usual.</description></item>
    /// </list>
    /// Nothing is written anywhere: the boxes are read-only and the selection is the box's own.
    /// </summary>
    /// <param name="box">The value box that got keyboard focus.</param>
    /// <param name="isMultiline">True for a multi-line box.</param>
    /// <param name="e">The focus change; its old focus tells a return from a context menu or another window.</param>
    private void ArriveInValueBox(TextBox box, bool isMultiline, KeyboardFocusChangedEventArgs e)
    {
        bool returning = ReferenceEquals(box, _valueBoxLeft) && IsOffTheWindow(e.OldFocus);
        _valueBoxLeft = null;
        (int Start, int Length)? selection = VariableDetailViewModel.SelectionOnKeyboardArrival(
            isMultiline,
            box.Text.Length,
            clicked: IsMouseButtonDown(),
            returning,
            _valueBoxLeftSelectionStart,
            _valueBoxLeftSelectionLength);
        if (selection is null)
        {
            if (returning)
            {
                _logger.Debug("Focus came back to a value box whose selection was the user's own; it is left as it was");
            }

            return;
        }

        if (selection.Value.Length == box.Text.Length)
        {
            SelectWholeValueOnArrival(box);
        }
        else
        {
            PutCaretAtTheStart(box);
        }
    }

    /// <summary>
    /// A value box lost keyboard focus: when focus went to the box's own context menu (a popup, not
    /// an element of the window) or to no element of the window at all (another window became
    /// active, a dialog opened), the box and its selection are remembered, so that when focus comes
    /// back to it <see cref="ArriveInValueBox"/> keeps a selection of the user's own. Focus that went
    /// to another element of the window forgets it: coming back from there is an arrival.
    /// </summary>
    /// <param name="box">The value box that lost keyboard focus.</param>
    /// <param name="e">The focus change.</param>
    private void RememberValueBoxLeft(TextBox box, KeyboardFocusChangedEventArgs e)
    {
        if (IsOffTheWindow(e.NewFocus))
        {
            _valueBoxLeft = box;
            _valueBoxLeftSelectionStart = box.SelectionStart;
            _valueBoxLeftSelectionLength = box.SelectionLength;
        }
        else
        {
            _valueBoxLeft = null;
        }
    }

    /// <summary>
    /// True when keyboard focus is not on an element of this window: no element (another
    /// application's window is active, or a dialog such as the folder picker), an element inside a
    /// context menu, which opens in a popup of its own, or an element of another window.
    /// </summary>
    private bool IsOffTheWindow(IInputElement? element)
    {
        return element is not DependencyObject target
            || FindAncestor<ContextMenu>(target) is not null
            || !ReferenceEquals(Window.GetWindow(target), this);
    }

    /// <summary>True while a mouse button is down: the focus change is a click's.</summary>
    private static bool IsMouseButtonDown()
    {
        return Mouse.LeftButton == MouseButtonState.Pressed || Mouse.RightButton == MouseButtonState.Pressed || Mouse.MiddleButton == MouseButtonState.Pressed;
    }

    /// <summary>
    /// Selects the whole text of a read-only value box that keyboard focus has just reached
    /// (<see cref="ArriveInValueBox"/>). The single-line boxes wrap, and NVDA reads a text box that
    /// gets focus only up to the end of the visual line at the caret unless text is selected,
    /// which it reads whole as "selected" and the text (below 512 characters): the Windows dialog
    /// convention of selecting an edit box's text on Tab. Round 1 heard Boot0000's wrapped Device
    /// path as "HD". The selection is made from the end of the value back to its start (the stock
    /// Ctrl+Shift+Home command), so its moving end, where the caret is and what the box scrolls
    /// into view after the layout, is the start of the value: a value taller than its box shows
    /// its first line, where a low-vision reader starts, and Up and Down read it line by line from
    /// there. SelectAll, whose moving end is the end, is the fallback should the command not run.
    /// </summary>
    private static void SelectWholeValueOnArrival(TextBox box)
    {
        box.CaretIndex = box.Text.Length;
        EditingCommands.SelectToDocumentStart.Execute(null, box);
        if (box.SelectionLength != box.Text.Length)
        {
            box.SelectAll();
        }
    }

    /// <summary>
    /// A long value (<see cref="VariableDetailViewModel.LongValueLength"/> characters or more) that
    /// keyboard focus has just reached: nothing is selected, the caret goes to the start of the value
    /// and the box shows its first line, so NVDA reads that line (after the help text that gives the
    /// value's length), and Down reads on line by line.
    /// </summary>
    private static void PutCaretAtTheStart(TextBox box)
    {
        box.Select(0, 0);
        box.ScrollToHome();
    }

    /// <summary>
    /// A detail field lost focus: Copy value falls back to the pane's default (the summary), and the
    /// box is remembered when focus went to its context menu or another window (<see cref="RememberValueBoxLeft"/>).
    /// </summary>
    private void OnDetailFieldLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.OriginalSource is TextBox { DataContext: DetailFieldViewModel } box)
        {
            AssignCopyValue(_defaultCopyValueText);
            RememberValueBoxLeft(box, e);
        }
    }

    /// <summary>
    /// Remembers the copy text the detail view model chose itself (after every Show), so it can be
    /// restored when a field loses focus; and when the pane shows another row, starts the value
    /// boxes it kept afresh (<see cref="StartValueBoxesAfresh"/>). Item is the last property Show
    /// assigns, so every value is the new row's by then.
    /// </summary>
    private void OnDetailPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_assigningCopyValue || _shell is null)
        {
            return;
        }

        if (e.PropertyName == nameof(VariableDetailViewModel.CopyValueText))
        {
            _defaultCopyValueText = _shell.Detail.CopyValueText;
        }
        else if (e.PropertyName == nameof(VariableDetailViewModel.Item))
        {
            StartValueBoxesAfresh();
        }
    }

    /// <summary>
    /// The pane shows another row and was updated in place, so the box of every field the update
    /// kept is the box that showed the old row, with the new value. Each one starts as a newly
    /// created box did when a row change still replaced every box:
    /// <list type="bullet">
    /// <item><description>
    /// A field box remembered as the one focus left for its context menu or another window is
    /// forgotten, so focus coming back to it is an arrival (<see cref="ArriveInValueBox"/>), not a
    /// return that keeps a selection made in the old row's value. The Writability line keeps that
    /// rule as it always had it: it is one box for the life of the window, never re-created on a
    /// row change (since M3 its text is the row's Writability sentence), and the in-place update
    /// leaves it as it was.
    /// </description></item>
    /// <item><description>
    /// Every box that is realised and does not have keyboard focus gets the caret at the start
    /// without a selection, and is scrolled back to its top: a multi-line box (the hex dump) is
    /// read from its caret, and a box whose text did not change would otherwise keep the old
    /// caret and scroll position. The box with keyboard focus is left as it is.
    /// </description></item>
    /// </list>
    /// Nothing is changed where nothing differs, so a box already at its start raises nothing. The
    /// boxes are found without applying a template (<see cref="RealisedValueBoxes"/>).
    /// </summary>
    private void StartValueBoxesAfresh()
    {
        if (_valueBoxLeft is not null && !ReferenceEquals(_valueBoxLeft, ReadOnlyStatusBox))
        {
            _valueBoxLeft = null;
        }

        foreach (TextBox box in RealisedValueBoxes(SectionsControl))
        {
            if (box.IsKeyboardFocused)
            {
                continue;
            }

            if (box.SelectionStart != 0 || box.SelectionLength != 0)
            {
                box.Select(0, 0);
            }

            if (box.VerticalOffset > 0 || box.HorizontalOffset > 0)
            {
                box.ScrollToHome();
            }
        }
    }

    /// <summary>
    /// The value boxes of the detail fields that exist now, in the visual tree under
    /// <paramref name="root"/>. Unlike <see cref="FindDescendants{T}"/>, it applies no template
    /// (which would create the parts of a box not yet shown, such as its collapsed scroll bars,
    /// and with them automation elements), and it never looks inside a text box.
    /// </summary>
    private static IEnumerable<TextBox> RealisedValueBoxes(DependencyObject root)
    {
        int count = root is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetChildrenCount(root) : 0;
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBox box)
            {
                if (box.DataContext is DetailFieldViewModel)
                {
                    yield return box;
                }

                continue;
            }

            foreach (TextBox nested in RealisedValueBoxes(child))
            {
                yield return nested;
            }
        }
    }

    private void AssignCopyValue(string? value)
    {
        if (_shell is null)
        {
            return;
        }

        _assigningCopyValue = true;
        try
        {
            _shell.Detail.CopyValueText = value;
        }
        finally
        {
            _assigningCopyValue = false;
        }
    }

    // ---- Change group -----------------------------------------------------------------------

    /// <summary>
    /// The keys of the boot order list that stock WPF does not give:
    /// Ctrl+Z puts the firmware's order back (the Change group's reset command, which announces
    /// "Order reset to the firmware's current order; nothing was written." and asks for focus on
    /// the selected row), and Enter and Space on a row do nothing, because a write needs a button.
    /// Up, Down, Home, End, Ctrl+Up and Ctrl+Down stay the stock list's, and Escape is the pane's
    /// (back to the variable list at the same row). Alt+U and Alt+D reach Move up and Move down
    /// through their access keys from anywhere in the window.
    /// </summary>
    private void OnBootOrderListPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_shell is null)
        {
            return;
        }

        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            ICommand reset = _shell.Detail.Change.ResetOrderCommand;
            if (reset.CanExecute(null))
            {
                reset.Execute(null);
            }

            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.None && e.Key is Key.Enter or Key.Space && FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is not null)
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// The proposed boot order changed. A move (Move up, Move down, each step of Ctrl+Z) is an
    /// <see cref="System.Collections.ObjectModel.ObservableCollection{T}.Move"/>: the row keeps its
    /// item container and its keyboard focus, no focus event is raised, and the Status sentence is
    /// the one announcement. Whether WPF keeps the container on every build is confirmed by ear
    ///, so when focus was on a row of the list at a move, the window
    /// remembers that row and checks once, in one deferred step at the end of the same key press
    /// (Dispatcher.BeginInvoke at Loaded priority, as the variable list's rebuild does; not a
    /// timer), that it still has focus, and puts focus back on it when WPF regenerated its
    /// container (<see cref="KeepBootOrderRowFocused"/>). A load that replaces the whole order is
    /// not a move; the load moves focus itself.
    /// </summary>
    private void OnBootOrderEntriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Move || _bootOrderRefocusQueued || !BootOrderList.IsKeyboardFocusWithin)
        {
            return;
        }

        _bootOrderRowFocused = FindAncestor<ListBoxItem>(Keyboard.FocusedElement as DependencyObject)?.DataContext as BootOrderEntryViewModel;
        if (_bootOrderRowFocused is null)
        {
            return;
        }

        _bootOrderRefocusQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(KeepBootOrderRowFocused));
    }

    /// <summary>
    /// The deferred check of <see cref="OnBootOrderEntriesCollectionChanged"/>: nothing happens
    /// while the row that had focus at the move still has it in its container (the expected case,
    /// so the move is heard once); when its container was regenerated, focus goes back to the row
    /// (<see cref="FocusTarget.BootOrderEntry"/> in docs/Keyboard-Map.md section 9), unless a
    /// deliberate move has put it on another visible control meanwhile.
    /// </summary>
    private void KeepBootOrderRowFocused()
    {
        _bootOrderRefocusQueued = false;
        BootOrderEntryViewModel? row = _bootOrderRowFocused;
        _bootOrderRowFocused = null;
        if (row is null || BootOrderList.ItemContainerGenerator.ContainerFromItem(row) is ListBoxItem { IsKeyboardFocused: true })
        {
            return;
        }

        // Where focus is now: still on the old container (no longer visible), on the window (WPF's
        // fallback for a focused element that was removed), or, when something moved it on purpose
        // meanwhile, on another visible control.
        DependencyObject? focused = Keyboard.FocusedElement as DependencyObject;
        if (focused is UIElement { IsVisible: true } && !ReferenceEquals(focused, this) && !ReferenceEquals(FindAncestor<ListBox>(focused), BootOrderList))
        {
            _logger.Debug("Focus moved on purpose while a boot order row moved; nothing to restore");
            return;
        }

        if (FocusBootOrderRow(row))
        {
            _logger.Debug("The moved boot order row got a new container; focus put back on it");
        }
    }

    /// <summary>
    /// Moves keyboard focus to a row of the boot order list without changing the selection (the
    /// selected row after Ctrl+Z, or the row that had focus when WPF regenerated its container).
    /// </summary>
    /// <param name="row">The row; null does nothing.</param>
    /// <returns>True when focus moved.</returns>
    private bool FocusBootOrderRow(BootOrderEntryViewModel? row)
    {
        if (row is null || !BootOrderList.IsVisible)
        {
            return false;
        }

        BootOrderList.UpdateLayout();
        ListBoxItem? container = BootOrderList.ItemContainerGenerator.ContainerFromItem(row) as ListBoxItem;
        if (container is null)
        {
            BootOrderList.ScrollIntoView(row);
            BootOrderList.UpdateLayout();
            container = BootOrderList.ItemContainerGenerator.ContainerFromItem(row) as ListBoxItem;
            if (container is null)
            {
                return false;
            }
        }

        if (!FocusElement(container))
        {
            return false;
        }

        container.BringIntoView();
        return true;
    }

    /// <summary>
    /// The button that opened the Confirm change dialog (<see cref="ChangeViewModel.LastAction"/>),
    /// after Cancel or Escape there. That button's own command is still finishing when the request
    /// arrives (it records the cancel before it ends, and a command is disabled while it runs), so
    /// a button that is shown but not yet enabled is waited for: it takes focus the moment it is
    /// enabled again (<see cref="OnChangeButtonIsEnabledChanged"/>), unless the person presses a
    /// key or clicks first, or another focus request comes; an event, not a timer. A button that is
    /// not shown any more hands the request to the detail pane's first field.
    /// </summary>
    /// <returns>True when focus moved or waits for the button.</returns>
    private bool FocusChangeControl()
    {
        Button? button = _shell?.Detail.Change.LastAction switch
        {
            ChangeAction.ReviewChange => ReviewChangeButton,
            ChangeAction.BootOnce => BootOnceButton,
            ChangeAction.ClearBootNext => ClearBootNextButton,
            ChangeAction.RestorePrevious => RestorePreviousButton,
            _ => null,
        };

        if (button is not null && FocusElement(button))
        {
            button.BringIntoView();
            return true;
        }

        if (button is { IsVisible: true, IsEnabled: false })
        {
            _changeControlWaiting = button;
            _logger.Debug("The button that opened the dialog is not enabled yet; focus goes to it when it is");
            return true;
        }

        return FocusFirstDetailField();
    }

    /// <summary>The visible row of the variable <see cref="ShellViewModel.FocusRowKey"/> names (the variable a change was just written to), or null.</summary>
    private VariableItemViewModel? RowOfFocusKey()
    {
        if (_shell is null || _shell.FocusRowKey is not string key)
        {
            return null;
        }

        return _shell.List.FindVisible(key);
    }

    /// <summary>The six buttons of the Change group, in tab order.</summary>
    private IEnumerable<Button> ChangeButtons()
    {
        return new[] { MoveUpButton, MoveDownButton, ReviewChangeButton, BootOnceButton, ClearBootNextButton, RestorePreviousButton };
    }

    // ---- Dell settings region (M4) ----------------------------------------------------------

    /// <summary>
    /// The keys of the list of Dell BIOS settings that stock WPF does not give: Ctrl+C copies the row's sentence through the shell (<see cref="ShellViewModel.CopyDellRow"/>,
    /// silent on success, "Copy failed: ..." otherwise), as Ctrl+C on a variable row copies its key;
    /// Enter and Space on a row do nothing, there being no detail pane for a Dell row, also with Ctrl
    /// or Shift down: Ctrl+Space would otherwise take the selection off the row (the stock list's
    /// toggle), and Alt+Space stays the window menu's. The arrows, Home, End, Page Up, Page Down and
    /// a letter (the next row whose name starts with it) stay the stock list's, and Escape does
    /// nothing here, as in the toolbar and the log.
    /// </summary>
    private void OnDellListPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_shell is null)
        {
            return;
        }

        DellSettingRowViewModel? row = FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject)?.DataContext as DellSettingRowViewModel;
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            _shell.CopyDellRow(row ?? _shell.Dell.SelectedRow);
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.None && e.Key is Key.Enter or Key.Space && row is not null)
        {
            e.Handled = true;
        }
    }

    /// <summary>The state line got focus: the same arrival as every single-line value box (its text is selected whole, so NVDA reads it to its end).</summary>
    private void OnDellSourceGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, DellSourceBox))
        {
            ArriveInValueBox(DellSourceBox, isMultiline: false, e);
        }
    }

    /// <summary>The state line lost focus: remembered when focus went to its context menu or to another window (<see cref="RememberValueBoxLeft"/>).</summary>
    private void OnDellSourceLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, DellSourceBox))
        {
            RememberValueBoxLeft(DellSourceBox, e);
        }
    }

    /// <summary>
    /// Keyboard focus reached the Dell settings region or moved inside it: the shell is told which
    /// part has it now, so Copy value (Alt+Y) copies the selected Dell
    /// row's sentence, or the state line's text, and never a firmware variable's value there. A Dell
    /// row that gets focus without being selected (Ctrl+Up or Ctrl+Down, which the stock list moves
    /// without selecting, or a screen reader's focus request) is selected first, so the row Alt+Y
    /// copies, F6 returns to and a Dell answer focuses is the row focus is on (there is always a selected row). A click is left to the stock list, which selects
    /// the row it focuses (a Ctrl+click would undo a selection made here).
    /// </summary>
    private void OnDellRegionGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is ListViewItem { IsSelected: false } item
            && ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(item), DellSettingsList)
            && Mouse.LeftButton == MouseButtonState.Released
            && Mouse.RightButton == MouseButtonState.Released)
        {
            item.IsSelected = true;
        }

        _shell?.OnDellFocus(DellFocusPartOf(e.NewFocus));
    }

    /// <summary>
    /// Keyboard focus left a control of the Dell settings region: the shell is told where it is
    /// now, <see cref="DellFocusPart.None"/> when it left the region (for another region, the state
    /// line's context menu, or another window).
    /// </summary>
    private void OnDellRegionLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _shell?.OnDellFocus(DellFocusPartOf(e.NewFocus));
    }

    /// <summary>The part of the Dell settings region an element belongs to, or <see cref="DellFocusPart.None"/> for an element outside it or off the window.</summary>
    private DellFocusPart DellFocusPartOf(IInputElement? element)
    {
        if (IsOffTheWindow(element))
        {
            return DellFocusPart.None;
        }

        for (DependencyObject? current = element as DependencyObject; current is not null; current = GetParent(current))
        {
            if (ReferenceEquals(current, ReadDellSettingsButton))
            {
                return DellFocusPart.Button;
            }

            if (ReferenceEquals(current, DellSourceBox))
            {
                return DellFocusPart.SourceLine;
            }

            if (ReferenceEquals(current, DellSettingsList))
            {
                return DellFocusPart.List;
            }

            if (ReferenceEquals(current, DellRegion))
            {
                break;
            }
        }

        return DellFocusPart.None;
    }

    /// <summary>
    /// Moves keyboard focus to a row of the list of Dell BIOS settings, selecting it when it is not
    /// selected yet. The list is not virtualised, so after a layout pass every row has its
    /// container. A row that already has focus raises no new focus event.
    /// </summary>
    /// <param name="row">The row; null does nothing.</param>
    /// <returns>True when focus is on the row.</returns>
    private bool FocusDellRow(DellSettingRowViewModel? row)
    {
        if (row is null || !DellSettingsList.IsVisible)
        {
            return false;
        }

        DellSettingsList.UpdateLayout();
        if (DellSettingsList.ItemContainerGenerator.ContainerFromItem(row) is not ListViewItem container)
        {
            return false;
        }

        if (!container.IsSelected)
        {
            container.IsSelected = true;
        }

        if (!FocusElement(container))
        {
            return false;
        }

        container.BringIntoView();
        return true;
    }

    // ---- Regions --------------------------------------------------------------------------

    private bool FocusRegion(Region region)
    {
        return region switch
        {
            Region.Toolbar => FocusToolbar(),
            Region.Filter => FocusFilter(selectAll: false),
            Region.List => MoveFocusTo(FocusTarget.ListSelectedRow),
            Region.Detail => FocusFirstDetailField(),
            Region.Log => FocusLogPanel(),
            Region.Dell => MoveFocusTo(FocusTarget.DellSelectedRow),
            Region.Status => FocusElement(StatusBox),
            _ => false,
        };
    }

    /// <summary>The regions before and after a splitter, or null when the element is not one of the two splitters.</summary>
    private (Region Before, Region After)? SplitterRegions(DependencyObject? element)
    {
        if (ReferenceEquals(element, ListDetailSplitter))
        {
            return (Region.List, Region.Detail);
        }

        if (ReferenceEquals(element, LogSplitter))
        {
            return (Region.Detail, Region.Log);
        }

        return null;
    }

    private Region? RegionOf(DependencyObject? element)
    {
        for (DependencyObject? current = element; current is not null; current = GetParent(current))
        {
            if (ReferenceEquals(current, ToolbarRegion))
            {
                return Region.Toolbar;
            }

            if (ReferenceEquals(current, FilterRegion))
            {
                return Region.Filter;
            }

            if (ReferenceEquals(current, ListRegion))
            {
                return Region.List;
            }

            if (ReferenceEquals(current, DetailRegion))
            {
                return Region.Detail;
            }

            if (ReferenceEquals(current, LogRegion))
            {
                return Region.Log;
            }

            if (ReferenceEquals(current, DellRegion))
            {
                return Region.Dell;
            }

            if (ReferenceEquals(current, StatusRegion))
            {
                return Region.Status;
            }
        }

        return null;
    }

    // ---- Focus helpers --------------------------------------------------------------------

    private bool FocusToolbar()
    {
        foreach (UIElement child in ToolbarPanel.Children)
        {
            if (child is Button { IsEnabled: true } button && FocusElement(button))
            {
                return true;
            }
        }

        return false;
    }

    private bool FocusFilter(bool selectAll)
    {
        if (!FocusElement(FilterBox))
        {
            return false;
        }

        if (selectAll)
        {
            FilterBox.SelectAll();
        }

        return true;
    }

    /// <summary>
    /// The deliberate ways into the detail pane (Enter or Space on a row, F6 to the Detail region,
    /// the shell's DetailFirstField request, and the delegation in
    /// <see cref="OnDetailPreviewGotKeyboardFocus"/>): the refresh a selection change left pending
    /// runs first, so the sections focus lands in belong to the selected row, then the first
    /// field, or the heading when there is none, takes focus.
    /// </summary>
    private bool FocusFirstDetailField()
    {
        _shell?.EnsureDetailCurrent();
        SectionsControl.UpdateLayout();
        TextBox? first = FindDescendants<TextBox>(SectionsControl).FirstOrDefault();
        FrameworkElement target = first ?? (FrameworkElement)DetailHeading;
        if (!FocusElement(target))
        {
            return false;
        }

        target.BringIntoView();
        return true;
    }

    private bool FocusLogPanel()
    {
        LogList.UpdateLayout();
        object? line = LogList.SelectedItem ?? (LogList.Items.Count > 0 ? LogList.Items[LogList.Items.Count - 1] : null);
        if (line is not null)
        {
            LogList.ScrollIntoView(line);
            LogList.UpdateLayout();
            if (LogList.ItemContainerGenerator.ContainerFromItem(line) is ListBoxItem container && FocusElement(container))
            {
                container.BringIntoView();
                return true;
            }
        }

        return FocusElement(LogList);
    }

    private VariableItemViewModel? FirstVisibleItem()
    {
        if (_shell is null)
        {
            return null;
        }

        foreach (VariableGroupViewModel group in _shell.List.Groups)
        {
            if (group.Items.Count > 0)
            {
                return group.Items[0];
            }
        }

        return null;
    }

    private bool FocusRowAt(int groupIndex, int rowIndex)
    {
        if (_shell is null || groupIndex < 0 || groupIndex >= _shell.List.Groups.Count)
        {
            return false;
        }

        VariableGroupViewModel group = _shell.List.Groups[groupIndex];
        return rowIndex >= 0 && rowIndex < group.Items.Count && FocusListRow(group.Items[rowIndex]);
    }

    /// <summary>Home: the first group's first row, or its header when the group is collapsed or empty.</summary>
    private bool FocusStartOfList()
    {
        if (_shell is null || _shell.List.Groups.Count == 0)
        {
            return false;
        }

        VariableGroupViewModel first = _shell.List.Groups[0];
        return (first.IsExpanded && first.Items.Count > 0 && FocusListRow(first.Items[0])) || FocusGroupHeader(0);
    }

    /// <summary>End: the last group's last row, or its header when the group is collapsed or empty.</summary>
    private bool FocusEndOfList()
    {
        return _shell is not null && FocusEndOfPreviousGroup(_shell.List.Groups.Count);
    }

    private bool FocusEndOfPreviousGroup(int groupIndex)
    {
        if (_shell is null || groupIndex <= 0 || groupIndex > _shell.List.Groups.Count)
        {
            return false;
        }

        VariableGroupViewModel previous = _shell.List.Groups[groupIndex - 1];
        if (previous.IsExpanded && previous.Items.Count > 0 && FocusListRow(previous.Items[previous.Items.Count - 1]))
        {
            return true;
        }

        return FocusGroupHeader(groupIndex - 1);
    }

    /// <summary>Selects a row and moves keyboard focus to its container, expanding its group when collapsed.</summary>
    private bool FocusListRow(VariableItemViewModel? item)
    {
        if (item is null || _shell is null)
        {
            return false;
        }

        int groupIndex = -1;
        for (int i = 0; i < _shell.List.Groups.Count; i++)
        {
            if (_shell.List.Groups[i].Items.Contains(item))
            {
                groupIndex = i;
                break;
            }
        }

        Expander? expander = ExpanderAt(groupIndex);
        if (expander is null)
        {
            return false;
        }

        if (!expander.IsExpanded)
        {
            expander.IsExpanded = true;
        }

        expander.UpdateLayout();
        ListView? listView = FindDescendant<ListView>(expander);
        if (listView is null)
        {
            return false;
        }

        listView.UpdateLayout();
        ListViewItem? container = listView.ItemContainerGenerator.ContainerFromItem(item) as ListViewItem;
        if (container is null)
        {
            listView.ScrollIntoView(item);
            listView.UpdateLayout();
            container = listView.ItemContainerGenerator.ContainerFromItem(item) as ListViewItem;
            if (container is null)
            {
                return false;
            }
        }

        if (!container.IsSelected)
        {
            container.IsSelected = true;
        }

        if (!FocusElement(container))
        {
            return false;
        }

        container.BringIntoView();
        return true;
    }

    /// <summary>Moves keyboard focus to a group header (the Expander's toggle button).</summary>
    private bool FocusGroupHeader(int groupIndex)
    {
        Expander? expander = ExpanderAt(groupIndex);
        if (expander is null)
        {
            return false;
        }

        expander.ApplyTemplate();
        ToggleButton? header = FindDescendant<ToggleButton>(expander);
        FrameworkElement target = header ?? (FrameworkElement)expander;
        if (!FocusElement(target))
        {
            return false;
        }

        target.BringIntoView();
        return true;
    }

    private Expander? ExpanderAt(int groupIndex)
    {
        if (groupIndex < 0 || groupIndex >= GroupsControl.Items.Count)
        {
            return null;
        }

        GroupsControl.UpdateLayout();
        DependencyObject? container = GroupsControl.ItemContainerGenerator.ContainerFromIndex(groupIndex);
        return container is null ? null : FindDescendant<Expander>(container);
    }

    private IEnumerable<ListView> GroupListViews()
    {
        for (int i = 0; i < GroupsControl.Items.Count; i++)
        {
            DependencyObject? container = GroupsControl.ItemContainerGenerator.ContainerFromIndex(i);
            if (container is not null && FindDescendant<ListView>(container) is ListView listView)
            {
                yield return listView;
            }
        }
    }

    private static bool FocusElement(UIElement element)
    {
        return element.Focusable && element.IsEnabled && ReferenceEquals(Keyboard.Focus(element), element);
    }

    // ---- Tree helpers ---------------------------------------------------------------------

    private static DependencyObject? GetParent(DependencyObject element)
    {
        DependencyObject? parent = element is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : null;
        return parent ?? LogicalTreeHelper.GetParent(element);
    }

    private static T? FindAncestor<T>(DependencyObject? element)
        where T : DependencyObject
    {
        for (DependencyObject? current = element; current is not null; current = GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        return FindDescendants<T>(root).FirstOrDefault();
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        (root as FrameworkElement)?.ApplyTemplate();
        int count = root is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetChildrenCount(root) : 0;
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (T descendant in FindDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
