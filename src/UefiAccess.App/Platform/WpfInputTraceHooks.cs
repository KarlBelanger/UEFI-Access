// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Presentation.Diagnostics;
using UefiAccess.Presentation.ViewModels;

namespace UefiAccess.App.Platform;

/// <summary>
/// The window's side of the input trace (UefiAccess.App started with --trace-input; Alpha 2
/// finding A2-7, the tester's question about the continued A1-6 issue): attached only when the trace is
/// on, from outside the window, so neither the window nor its handlers change. It writes:
/// <list type="bullet">
/// <item><description>KEY: every key going down or coming up in the window, numbered (#n), with the
/// system key of an Alt combination, whether it repeats, the modifiers, how long its message waited
/// in the window's queue before WPF took it (queued=, from the key's message time, so in steps of
/// the tick count, about 15.6 ms) and the element that has keyboard focus; taken from the input
/// manager's PreNotifyInput, before the key is routed, so a KEY line comes before everything the
/// key causes; and when the key's handling is finished (PostProcessInput of its KeyDown), how long
/// it took and whether it was handled;</description></item>
/// <item><description>MOUSE: every mouse button going down or coming up in the window, and the control under it;</description></item>
/// <item><description>FOCUS: every keyboard focus change in the window, from and to (a row with its variable name), and focus leaving the window;</description></item>
/// <item><description>SELECTION: a row of a per-group list selected or unselected (by variable name), taken from the row's container when its IsSelected changes, which is before the list raises SelectionChanged and before the window's handler hands the row to the shell (the shell writes its own SELECTION line, before it schedules the rebuild); and a group header getting focus;</description></item>
/// <item><description>IDLE: after a key goes down, when the UI thread next reaches ApplicationIdle priority, how long the window was busy since WPF took the key and, when its message time is known, since it was pressed (one operation queued at a time; the keys that went down meanwhile are counted in it); a render WPF defers to the next frame can still follow;</description></item>
/// <item><description>LISTENERS: which UI Automation events a client listens for, as WPF knows it (AutomationPeer.ListenerExists for every AutomationEvents value, and whether any client listens at all): when the trace starts, when the window has loaded, whenever it changed at a key press, and at exit;</description></item>
/// <item><description>TRACE: the trace started, the window activated or deactivated (deactivated is written after the window's own handler, whose release of the detail pane's hold is the DETAIL line before it), the hooks detached.</description></item>
/// </list>
/// The detail pane's scheduling, the announcements and the events the application raises are
/// traced where they happen (DispatcherDetailScheduler, WpfAnnouncer, ShellViewModel). Every handler
/// only reads and writes a line: none marks an event handled, moves focus, speaks or creates an
/// automation peer, and each catches its own failure, so a trace that fails never reaches input.
/// <see cref="Detach"/> removes every handler. UI thread only.
/// </summary>
internal sealed class WpfInputTraceHooks
{
    /// <summary>Every UI Automation event WPF can be listened for; at most 63, one bit each in the listener mask.</summary>
    private static readonly AutomationEvents[] AutomationEventValues = Enum.GetValues<AutomationEvents>();

    /// <summary>The bit of the listener mask for "a UI Automation client listens".</summary>
    private const int ClientsBit = 63;

    /// <summary>The longest wait in the message queue a KEY line reports; anything else (a key a program made up, with no message time) is shown as "?".</summary>
    private const int MaxQueuedMilliseconds = 600_000;

    private readonly MainWindow _window;
    private readonly IInputTrace _trace;
    private readonly InputManager _input;
    private readonly KeyboardFocusChangedEventHandler _focusGotHandler;
    private readonly KeyboardFocusChangedEventHandler _focusLostHandler;
    private readonly RoutedEventHandler _rowSelectionHandler;
    private readonly Action _idleProbe;
    private readonly Dictionary<Key, KeyPress> _keysDown = new();
    private long _keyCount;
    private KeyPress? _unfinishedKey;
    private ulong _listeners;
    private bool _listenersWritten;
    private bool _idleQueued;
    private KeyPress _idleFirst;
    private KeyPress _idleLast;
    private bool _attached;

    private WpfInputTraceHooks(MainWindow window, IInputTrace trace)
    {
        _window = window;
        _trace = trace;
        _input = InputManager.Current;
        _focusGotHandler = OnFocusGot;
        _focusLostHandler = OnFocusLost;
        _rowSelectionHandler = OnRowSelectionChanged;
        _idleProbe = OnIdle;
    }

    /// <summary>
    /// Attaches the hooks to the window and writes the first TRACE and LISTENERS lines. The
    /// application calls this only when the trace is on, after the shell is attached to the window
    /// (the shell writes its own SELECTION and DETAIL lines) and before the window is shown.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="trace">The input trace.</param>
    /// <returns>The hooks, for <see cref="Detach"/>.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static WpfInputTraceHooks Attach(MainWindow window, IInputTrace trace)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(trace);
        WpfInputTraceHooks hooks = new(window, trace);
        hooks.Subscribe();
        return hooks;
    }

    /// <summary>Removes every handler <see cref="Attach"/> added and writes the LISTENERS line at exit. Idempotent.</summary>
    public void Detach()
    {
        if (!_attached)
        {
            return;
        }

        _attached = false;
        _input.PreNotifyInput -= OnPreNotifyInput;
        _input.PostProcessInput -= OnPostProcessInput;
        _window.RemoveHandler(Keyboard.GotKeyboardFocusEvent, _focusGotHandler);
        _window.RemoveHandler(Keyboard.LostKeyboardFocusEvent, _focusLostHandler);
        _window.GroupsControl.RemoveHandler(Selector.SelectedEvent, _rowSelectionHandler);
        _window.GroupsControl.RemoveHandler(Selector.UnselectedEvent, _rowSelectionHandler);
        _window.Loaded -= OnWindowLoaded;
        _window.Activated -= OnWindowActivated;
        _window.Deactivated -= OnWindowDeactivated;
        try
        {
            WriteListeners("at exit", always: true);
            _trace.Write(TraceKind.Trace, "input trace: hooks detached after " + Number(_keyCount) + " key presses");
        }
        catch (Exception ex)
        {
            Failed(ex);
        }
    }

    private void Subscribe()
    {
        _attached = true;
        _trace.Write(TraceKind.Trace, "input trace: started; UefiAccess.App " + ToolVersion.Current + ", process " + Number(Environment.ProcessId) + ", switch " + InputTraceSwitch.Name);
        WriteListeners("at start", always: true);
        _input.PreNotifyInput += OnPreNotifyInput;
        _input.PostProcessInput += OnPostProcessInput;
        _window.AddHandler(Keyboard.GotKeyboardFocusEvent, _focusGotHandler, true);
        _window.AddHandler(Keyboard.LostKeyboardFocusEvent, _focusLostHandler, true);
        _window.GroupsControl.AddHandler(Selector.SelectedEvent, _rowSelectionHandler, true);
        _window.GroupsControl.AddHandler(Selector.UnselectedEvent, _rowSelectionHandler, true);
        _window.Loaded += OnWindowLoaded;
        _window.Activated += OnWindowActivated;
        _window.Deactivated += OnWindowDeactivated;
    }

    /// <summary>Every input item, before it is routed: keys and mouse buttons of this window only.</summary>
    private void OnPreNotifyInput(object sender, NotifyInputEventArgs e)
    {
        try
        {
            InputEventArgs input = e.StagingItem.Input;
            RoutedEvent? routed = input.RoutedEvent;
            if (routed == Keyboard.PreviewKeyDownEvent)
            {
                if (input is KeyEventArgs keyDown && IsThisWindow(input))
                {
                    OnKeyDown(keyDown);
                }
            }
            else if (routed == Keyboard.PreviewKeyUpEvent)
            {
                if (input is KeyEventArgs keyUp && IsThisWindow(input))
                {
                    OnKeyUp(keyUp);
                }
            }
            else if (routed == Mouse.PreviewMouseDownEvent || routed == Mouse.PreviewMouseUpEvent)
            {
                if (input is MouseButtonEventArgs button && IsThisWindow(input))
                {
                    _trace.Write(TraceKind.Mouse, (routed == Mouse.PreviewMouseDownEvent ? "down " : "up ") + button.ChangedButton + " over " + WpfTraceText.DescribeTarget(Mouse.DirectlyOver));
                }
            }
        }
        catch (Exception ex)
        {
            Failed(ex);
        }
    }

    /// <summary>Every input item, after it was routed: the end of the handling of the key that went down last.</summary>
    private void OnPostProcessInput(object sender, ProcessInputEventArgs e)
    {
        try
        {
            if (_unfinishedKey is not KeyPress key)
            {
                return;
            }

            InputEventArgs input = e.StagingItem.Input;
            RoutedEvent? routed = input.RoutedEvent;

            // The handling ends after KeyDown; a PreviewKeyDown that was handled may raise no KeyDown.
            bool inPreview = routed == Keyboard.PreviewKeyDownEvent;
            if (input is not KeyEventArgs || !(routed == Keyboard.KeyDownEvent || (inPreview && input.Handled)))
            {
                return;
            }

            _unfinishedKey = null;
            _trace.Write(
                TraceKind.Key,
                "down " + key.Name + " #" + Number(key.Number) + " done in " + TraceLine.Milliseconds(key.At, Stopwatch.GetTimestamp()) + " ms handled=" + YesNo(input.Handled) + (inPreview ? " (in its preview)" : string.Empty));
        }
        catch (Exception ex)
        {
            Failed(ex);
        }
    }

    private void OnKeyDown(KeyEventArgs e)
    {
        long at = Stopwatch.GetTimestamp();
        int? queued = Queued(e);
        Key key = RealKey(e);
        KeyPress press = new(++_keyCount, KeyName(e), at, queued);
        if (!e.IsRepeat || !_keysDown.ContainsKey(key))
        {
            _keysDown[key] = press;
        }

        _unfinishedKey = press;
        _trace.Write(
            TraceKind.Key,
            "down " + press.Name + " #" + Number(press.Number) + " repeat=" + YesNo(e.IsRepeat) + " modifiers=" + Modifiers() + " queued=" + QueuedText(queued) + " focus=" + WpfTraceText.Describe(Keyboard.FocusedElement));
        WriteListeners("changed at key #" + Number(press.Number), always: false);
        QueueIdleProbe(press);
    }

    private void OnKeyUp(KeyEventArgs e)
    {
        long at = Stopwatch.GetTimestamp();
        int? queued = Queued(e);
        string wentDown = _keysDown.Remove(RealKey(e), out KeyPress down)
            ? " (went down at #" + Number(down.Number) + ", " + TraceLine.Milliseconds(down.At, at) + " ms before)"
            : string.Empty;
        _trace.Write(TraceKind.Key, "up " + KeyName(e) + wentDown + " queued=" + QueuedText(queued) + " focus=" + WpfTraceText.Describe(Keyboard.FocusedElement, withName: false));
    }

    /// <summary>
    /// How long the key's message waited in the window's queue before WPF took it, in
    /// milliseconds: the tick count now minus the key's time stamp, which WPF takes from the
    /// message time (Environment.TickCount and GetMessageTime are one clock, so the value moves in
    /// steps of about 15.6 ms); null when the stamp is no message time (a key a program made up,
    /// stamped 0 or later than now).
    /// </summary>
    private static int? Queued(KeyEventArgs e)
    {
        int queued = unchecked(Environment.TickCount - e.Timestamp);
        return queued is >= 0 and <= MaxQueuedMilliseconds ? queued : null;
    }

    private static string QueuedText(int? queued) => queued is int milliseconds ? Number(milliseconds) + " ms" : "?";

    /// <summary>Queues one probe at ApplicationIdle priority, unless one is queued already (it then covers this key too).</summary>
    private void QueueIdleProbe(KeyPress press)
    {
        _idleLast = press;
        if (_idleQueued)
        {
            return;
        }

        _idleQueued = true;
        _idleFirst = press;
        _window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, _idleProbe);
    }

    /// <summary>
    /// The UI thread reached ApplicationIdle priority: how long it was busy since WPF took the keys
    /// that went down meanwhile, and since the first of them was pressed (its message time) when
    /// that is known. A render WPF defers to the next frame can still follow this moment.
    /// </summary>
    private void OnIdle()
    {
        try
        {
            _idleQueued = false;
            long now = Stopwatch.GetTimestamp();
            KeyPress first = _idleFirst;
            KeyPress last = _idleLast;
            string details = first.Number == last.Number
                ? "after key #" + Number(first.Number) + " (" + first.Name + "): busy " + TraceLine.Milliseconds(first.At, now) + " ms"
                : "after keys #" + Number(first.Number) + "..#" + Number(last.Number) + " (last " + last.Name + "): busy "
                    + TraceLine.Milliseconds(first.At, now) + " ms since #" + Number(first.Number) + ", " + TraceLine.Milliseconds(last.At, now) + " ms since #" + Number(last.Number);
            string sincePressed = first.QueuedMilliseconds is int queued
                ? "; " + TraceLine.Milliseconds(first.At - (queued * Stopwatch.Frequency / 1000), now) + " ms since #" + Number(first.Number) + " was pressed (its message time)"
                : string.Empty;
            _trace.Write(TraceKind.Idle, details + sincePressed);
        }
        catch (Exception ex)
        {
            Failed(ex);
        }
    }

    private void OnFocusGot(object sender, KeyboardFocusChangedEventArgs e)
    {
        try
        {
            _trace.Write(TraceKind.Focus, WpfTraceText.Describe(e.OldFocus, withName: false) + " -> " + WpfTraceText.Describe(e.NewFocus));
            if (e.NewFocus is ToggleButton { DataContext: VariableGroupViewModel group })
            {
                _trace.Write(TraceKind.Selection, "header " + TraceLine.Quote(group.Label) + " focused");
            }
        }
        catch (Exception ex)
        {
            Failed(ex);
        }
    }

    private void OnFocusLost(object sender, KeyboardFocusChangedEventArgs e)
    {
        try
        {
            if (e.NewFocus is DependencyObject next && ReferenceEquals(Window.GetWindow(next), _window))
            {
                // Inside the window: the FOCUS line of the element that gets it says both.
                return;
            }

            _trace.Write(TraceKind.Focus, WpfTraceText.Describe(e.OldFocus, withName: false) + " -> outside the window" + (e.NewFocus is null ? string.Empty : " (" + e.NewFocus.GetType().Name + ")"));
        }
        catch (Exception ex)
        {
            Failed(ex);
        }
    }

    /// <summary>
    /// A row's container in a per-group list was selected or unselected: Selector.SelectedEvent or
    /// UnselectedEvent, which the container raises when its IsSelected changes and which bubbles to
    /// the groups control (the list marks it handled, so the handler is added for handled events
    /// too). The container's IsSelected changes before the list raises SelectionChanged, so this
    /// line comes before the window's handler hands the row to the shell and before everything that
    /// causes.
    /// </summary>
    private void OnRowSelectionChanged(object sender, RoutedEventArgs e)
    {
        try
        {
            if (e.OriginalSource is not ListViewItem { DataContext: VariableItemViewModel row } item)
            {
                return;
            }

            string group = ItemsControl.ItemsControlFromItemContainer(item)?.DataContext is VariableGroupViewModel owner ? TraceLine.Quote(owner.Label) : "?";
            string what = e.RoutedEvent == Selector.SelectedEvent ? "selected" : "unselected";
            _trace.Write(TraceKind.Selection, "list " + group + ": row " + TraceLine.Token(row.Name) + " " + what);
        }
        catch (Exception ex)
        {
            Failed(ex);
        }
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            WriteListeners("window loaded", always: true);
        }
        catch (Exception ex)
        {
            Failed(ex);
        }
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        try
        {
            _trace.Write(TraceKind.Trace, "input trace: window activated");
        }
        catch (Exception ex)
        {
            Failed(ex);
        }
    }

    /// <summary>The window lost activation. This handler runs after the window's own, so the DETAIL release that handler makes is written before this line.</summary>
    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        try
        {
            _trace.Write(TraceKind.Trace, "input trace: window deactivated (written after the window's own handler, whose release is the DETAIL line before)");
        }
        catch (Exception ex)
        {
            Failed(ex);
        }
    }

    /// <summary>
    /// Writes the UI Automation events a client listens for, as WPF knows them, and whether any
    /// client listens: always when <paramref name="always"/> is true, otherwise only when that
    /// changed since the last line.
    /// </summary>
    private void WriteListeners(string when, bool always)
    {
        ulong mask = 0;
        for (int i = 0; i < AutomationEventValues.Length && i < ClientsBit; i++)
        {
            if (AutomationPeer.ListenerExists(AutomationEventValues[i]))
            {
                mask |= 1UL << i;
            }
        }

        if (AutomationInteropProvider.ClientsAreListening)
        {
            mask |= 1UL << ClientsBit;
        }

        if (!always && _listenersWritten && mask == _listeners)
        {
            return;
        }

        _listeners = mask;
        _listenersWritten = true;
        List<string> names = new();
        for (int i = 0; i < AutomationEventValues.Length && i < ClientsBit; i++)
        {
            if ((mask & (1UL << i)) != 0)
            {
                names.Add(AutomationEventValues[i].ToString());
            }
        }

        _trace.Write(
            TraceKind.Listeners,
            when + ": " + (names.Count == 0 ? "none" : string.Join(" ", names)) + "; a UI Automation client listens: " + YesNo((mask & (1UL << ClientsBit)) != 0));
    }

    /// <summary>True when the input belongs to this window (its device's active source is the window), not to a popup or another window.</summary>
    private bool IsThisWindow(InputEventArgs input)
    {
        return ReferenceEquals(input.Device?.ActiveSource?.RootVisual, _window);
    }

    /// <summary>Writes that a hook failed; the failure goes no further.</summary>
    private void Failed(Exception ex)
    {
        try
        {
            _trace.Write(TraceKind.Trace, "input trace: a hook failed: " + ex.GetType().Name + ": " + ex.Message);
        }
        catch (Exception)
        {
            // The trace must never reach input.
        }
    }

    /// <summary>The key itself: for an Alt combination (Key.System) the key pressed with Alt.</summary>
    private static Key RealKey(KeyEventArgs e)
    {
        return e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key,
        };
    }

    private static string KeyName(KeyEventArgs e)
    {
        Key real = RealKey(e);
        return real == e.Key ? e.Key.ToString() : e.Key + "(" + real + ")";
    }

    private static string Modifiers()
    {
        return Keyboard.Modifiers.ToString().Replace(", ", "+", StringComparison.Ordinal);
    }

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string YesNo(bool value) => value ? "yes" : "no";

    /// <summary>A key that went down: its number in this trace, its name, its QPC stamp when WPF took it, and how long its message had waited before that (null when not known).</summary>
    private readonly record struct KeyPress(long Number, string Name, long At, int? QueuedMilliseconds);
}
