// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Serilog.Core;
using Serilog.Events;
using UefiAccess.Presentation.ViewModels;

namespace UefiAccess.App.Platform;

/// <summary>
/// A Serilog sink that mirrors the application's log events into the log panel's
/// <see cref="LogViewModel"/> by the <see cref="LogPanelMirror"/> rule (Information and above,
/// except the events of the Core services, which reach the panel through the source result and
/// stay in the file), rendered as the log file renders them, so the last 200 lines are readable in
/// the window without opening the log file. Events raised on a background thread are queued to
/// the UI dispatcher with <see cref="Dispatcher.BeginInvoke(DispatcherPriority, Delegate)"/> at
/// <see cref="DispatcherPriority.Normal"/>, the priority the continuation of a load runs at, so a
/// line logged on the load's thread lands before the lines the shell writes when the load
/// completes, in the order the file has them; nothing polls and nothing runs on a timer. Events
/// that arrive before <see cref="Attach"/> (the application's own "starting" line is logged before
/// the shell, and so the panel, exists) are held back, at most <see cref="PendingCapacity"/> of
/// them, and written into the panel first thing when it is attached, so the panel opens with the
/// version line the log file starts with. The panel is a live region set to Off, so new lines
/// never interrupt a screen reader. Attaching also tells the log which line has keyboard focus
/// (<see cref="LogViewModel.FocusedLineProvider"/>), so dropping the oldest line never takes focus
/// away from a reader who is on it.
/// </summary>
public sealed class LogPanelSink : ILogEventSink
{
    /// <summary>How many events are held back before <see cref="Attach"/>; older ones are dropped (they are still in the log file).</summary>
    public const int PendingCapacity = 200;

    private readonly Dispatcher _dispatcher;
    private readonly object _gate = new();
    private readonly Queue<LogLineViewModel> _pending = new();
    private LogViewModel? _target;

    /// <summary>Creates the sink over the UI dispatcher.</summary>
    /// <param name="dispatcher">The dispatcher of the window that owns the log panel.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dispatcher"/> is null.</exception>
    public LogPanelSink(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
    }

    /// <summary>The lowest level mirrored into the panel; Debug and Verbose stay in the file only.</summary>
    public static LogEventLevel MinimumLevel => LogEventLevel.Information;

    /// <summary>
    /// Starts mirroring events into the given log view model, after writing every event held
    /// back since the sink was created into it, in order, and lets the log ask which of its lines
    /// has keyboard focus.
    /// </summary>
    /// <param name="target">The log panel's view model.</param>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is null.</exception>
    public void Attach(LogViewModel target)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.FocusedLineProvider = FocusedLine;
        LogLineViewModel[] pending;
        lock (_gate)
        {
            _target = target;
            pending = _pending.ToArray();
            _pending.Clear();
        }

        foreach (LogLineViewModel line in pending)
        {
            Deliver(target, line);
        }
    }

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        if (logEvent is null || logEvent.Level < MinimumLevel)
        {
            return;
        }

        LogLineViewModel? line = LogPanelMirror.ToLine(logEvent);
        if (line is null)
        {
            return;
        }

        LogViewModel? target;
        lock (_gate)
        {
            target = _target;
            if (target is null)
            {
                _pending.Enqueue(line);
                while (_pending.Count > PendingCapacity)
                {
                    _pending.Dequeue();
                }

                return;
            }
        }

        Deliver(target, line);
    }

    private void Deliver(LogViewModel target, LogLineViewModel line)
    {
        if (_dispatcher.CheckAccess())
        {
            target.Add(line);
        }
        else
        {
            _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => target.Add(line)));
        }
    }

    /// <summary>
    /// The log line whose row in the log panel has keyboard focus, or null (asked on the UI thread
    /// before the oldest line is dropped). While the window is not active there is no keyboard
    /// focus, so the row the window will give focus back to on reactivation counts instead.
    /// </summary>
    private LogLineViewModel? FocusedLine()
    {
        if (!_dispatcher.CheckAccess())
        {
            return null;
        }

        object? focused = Keyboard.FocusedElement
            ?? (Application.Current?.MainWindow is Window window ? FocusManager.GetFocusedElement(window) : null);
        return focused is ListBoxItem { DataContext: LogLineViewModel line } ? line : null;
    }
}
