// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Automation;
using UefiAccess.Core.Diagnostics;
using UefiAccess.UiaTrace.Recording;

namespace UefiAccess.UiaTrace;

/// <summary>How a recording ended.</summary>
/// <param name="WindowFound">True when the UefiAccess window was found and the subscriptions were made.</param>
/// <param name="ProcessId">The process of the UefiAccess window, or 0 when none was found.</param>
/// <param name="Recorded">How long the subscriptions were in place.</param>
/// <param name="StopReason">Why the recording stopped, for the trace and the console.</param>
internal sealed record RecorderOutcome(bool WindowFound, int ProcessId, TimeSpan Recorded, string StopReason);

/// <summary>
/// Records the UI Automation events a client in another process receives from the UefiAccess
/// window, with the managed UI Automation client (System.Windows.Automation), one UIA-RECV line per
/// event through the shared <see cref="TraceFileWriter"/>, so the lines merge with the
/// application's own trace-input file on the QPC column.
/// </summary>
/// <remarks>
/// <para>
/// Every subscription and the final <see cref="Automation.RemoveAllEventHandlers"/> are made on the
/// calling thread, the recorder's MTA main thread, never inside a handler. The window is found
/// without polling: a WindowOpened subscription on the desktop's children and one FindAll of the
/// windows already open; a match is a WPF window of a process named UefiAccess.App
/// (<see cref="IsUefiAccessWindow"/>). The handler of WindowOpened only queues the element and
/// wakes the main thread, which waits with no time-out. The element WindowOpened delivers cannot
/// anchor the subscriptions: subscribed on it, the window's subtree delivered no event at all,
/// while the same window taken again from its handle (<see cref="AutomationElement.FromHandle"/>)
/// delivered every one (measured on 1 October 2026 against .NET 8's client and a test window of
/// the same kind). So the window is taken again from its handle before the subscriptions are made.
/// The recording stops when that process exits (<see cref="Process.Exited"/>) or on Ctrl+C
/// (<see cref="Console.CancelKeyPress"/>); both only wake the main thread. A second Ctrl+C is not
/// held back and ends the program at once.
/// </para>
/// <para>
/// The event subscriptions are made under a cache request with the raw-view tree filter: each
/// event arrives with the element's Name, AutomationId, ControlType, ProcessId and FrameworkId
/// already fetched,
/// and the element is never moved to an ancestor that fits a narrower view. The subscriptions on
/// the window use <see cref="AutomationElementMode.None"/>, so their elements have no live
/// reference at all. The focus subscription cannot: the managed client registers it on the desktop
/// root taken under the active cache request, and a root without a live reference makes the
/// subscription fail (ArgumentNullException, measured on 1 October 2026 against .NET 8's client),
/// so it uses <see cref="AutomationElementMode.Full"/> with the same properties. Either way a
/// handler reads the cache only (<see cref="UiaElementFacts"/>), never a live property, so it cannot
/// call into the UefiAccess window and wait on its UI thread. A handler takes its time stamp, local
/// time and thread first, reads the cache, formats the details (<see cref="UiaEventText"/>), queues
/// one line and counts it; it catches everything, because an exception must never travel back into
/// UI Automation.
/// </para>
/// <para>
/// The cache is fetched when the event reaches the recorder, not when UefiAccess raised it, so the
/// name on a line is the element's name at arrival; after a variable list load that can be a later
/// sentence (measured in M4-H4 on 2 October 2026). The property-change and live-region handlers
/// therefore pass the cached facts through <see cref="ElementValues"/>, which keeps each element's
/// last Value received with a value and writes it with each LiveRegionChanged of that element as
/// sentence="...", and marks copy=yes on a LiveRegionChanged that followed a PropertyChanged
/// Value=(none) of the same element: the valueless copy most events of the window (the Status
/// box's among them, not the focus events) get once the Open baseline folder dialog has been
/// opened. The handlers count the copies for the summary.
/// </para>
/// <para>
/// A subscription that fails leaves the managed client's table of listeners damaged, so that
/// removing listeners afterwards can throw (NullReferenceException, measured the same day). The
/// failure is written as a TRACE line and the other subscriptions go ahead; the final removal
/// catches whatever it throws, because the recording is over by then and the process ends.
/// </para>
/// <para>
/// Observer effect: subscribing makes WPF register the events and raise some it otherwise skips
/// (structure and property changes, selection events), and UI Automation fetches the cached
/// properties from the window's UI thread at every event. <c>--events</c> limits the
/// subscriptions; the application's LISTENERS lines show what is being listened for.
/// </para>
/// </remarks>
internal sealed class UiaRecorder : IDisposable
{
    /// <summary>The process name of the UefiAccess application.</summary>
    public const string AppProcessName = "UefiAccess.App";

    /// <summary>The start of the class name of every WPF window's handle.</summary>
    private const string WpfWindowClassPrefix = "HwndWrapper[";

    /// <summary>The UI Automation framework id of WPF.</summary>
    private const string WpfFrameworkId = UiaEventText.WpfFrameworkId;

    private readonly TraceFileWriter _writer;
    private readonly RecorderEvents _events;
    private readonly EventCounts _counts;
    private readonly ElementValues _values = new();
    private readonly Action<string> _say;
    private readonly ConcurrentQueue<AutomationElement> _openedWindows = new();
    private readonly ManualResetEventSlim _wake = new(false);
    private readonly AutomationEventHandler _onWindowOpened;
    private readonly AutomationFocusChangedEventHandler _onFocusChanged;
    private readonly AutomationEventHandler _onEvent;
    private readonly StructureChangedEventHandler _onStructureChanged;
    private readonly AutomationPropertyChangedEventHandler _onPropertyChanged;
    private string? _stopReason;
    private int _targetProcessId;
    private Process? _target;

    /// <summary>Creates the recorder.</summary>
    /// <param name="writer">The trace file; the recorder writes its TRACE lines (a subscription that failed included) and one UIA-RECV line per event to it.</param>
    /// <param name="events">The event families to subscribe to.</param>
    /// <param name="counts">Counts every event received, for the summary.</param>
    /// <param name="say">Writes a sentence to the console (redacted by the caller).</param>
    public UiaRecorder(TraceFileWriter writer, RecorderEvents events, EventCounts counts, Action<string> say)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _counts = counts ?? throw new ArgumentNullException(nameof(counts));
        _say = say ?? throw new ArgumentNullException(nameof(say));
        _events = events;
        _onWindowOpened = OnWindowOpened;
        _onFocusChanged = OnFocusChanged;
        _onEvent = OnAutomationEvent;
        _onStructureChanged = OnStructureChanged;
        _onPropertyChanged = OnPropertyChanged;
    }

    /// <summary>The properties whose changes the recorder records, in the order of <see cref="UiaEventNames.Properties"/>.</summary>
    private static AutomationProperty[] WatchedProperties { get; } =
    {
        AutomationElement.NameProperty,
        ValuePattern.ValueProperty,
        SelectionItemPattern.IsSelectedProperty,
        AutomationElement.HasKeyboardFocusProperty,
        ExpandCollapsePattern.ExpandCollapseStateProperty,
    };

    private bool StopRequested => Volatile.Read(ref _stopReason) is not null;

    /// <summary>
    /// Waits for the UefiAccess window, subscribes, records until UefiAccess exits or Ctrl+C is
    /// pressed, and removes every subscription. Blocks the calling thread throughout.
    /// </summary>
    /// <returns>How the recording ended.</returns>
    public RecorderOutcome Run()
    {
        Console.CancelKeyPress += OnCancelKeyPress;
        try
        {
            _writer.Write(TraceKind.Trace, "recorder: started; events: " + RecorderOptions.Describe(_events));
            AutomationElement? discovered = WaitForWindow(out ElementFacts windowFacts, out int windowHandle);
            if (discovered is null)
            {
                return new RecorderOutcome(false, 0, TimeSpan.Zero, "stopped before the UefiAccess window was found (" + _stopReason + ")");
            }

            int processId = windowFacts.ProcessId ?? 0;
            _targetProcessId = processId;
            _writer.Write(TraceKind.Trace, "recorder: UefiAccess window found: process " + processId.ToString(CultureInfo.InvariantCulture) + ", " + UiaEventText.Element(windowFacts));
            WatchProcess(processId);
            AutomationElement? window = WindowFromHandle(discovered, windowHandle);
            if (window is null)
            {
                return new RecorderOutcome(false, processId, TimeSpan.Zero, _stopReason ?? "the UefiAccess window closed before the subscriptions were made");
            }

            long subscribedAt = Stopwatch.GetTimestamp();
            Subscribe(window);
            _say("Recording UefiAccess (process " + processId.ToString(CultureInfo.InvariantCulture) + "). Close UefiAccess, or press Ctrl+C, to stop.");
            WaitForStop();
            TimeSpan recorded = Stopwatch.GetElapsedTime(subscribedAt);
            return new RecorderOutcome(true, processId, recorded, _stopReason ?? "stopped");
        }
        finally
        {
            RemoveSubscriptions();
            Console.CancelKeyPress -= OnCancelKeyPress;
            if (_target is not null)
            {
                _target.Exited -= OnTargetExited;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _target?.Dispose();
        _target = null;
        _wake.Dispose();
    }

    /// <summary>
    /// Finds the UefiAccess window: subscribes to WindowOpened on the desktop's children, queues the
    /// windows already open, then examines each queued window on this thread and waits, without a
    /// time-out, for the next one.
    /// </summary>
    /// <returns>The window as discovered, or null when a stop was asked for first.</returns>
    private AutomationElement? WaitForWindow(out ElementFacts facts, out int handle)
    {
        facts = ElementFacts.Unavailable;
        handle = 0;
        CacheRequest discovery = new()
        {
            AutomationElementMode = AutomationElementMode.Full,
            TreeFilter = Automation.RawViewCondition,
        };
        discovery.Add(AutomationElement.NameProperty);
        discovery.Add(AutomationElement.ControlTypeProperty);
        discovery.Add(AutomationElement.ProcessIdProperty);
        discovery.Add(AutomationElement.AutomationIdProperty);
        discovery.Add(AutomationElement.ClassNameProperty);
        discovery.Add(AutomationElement.FrameworkIdProperty);
        discovery.Add(AutomationElement.NativeWindowHandleProperty);
        using (discovery.Activate())
        {
            Automation.AddAutomationEventHandler(WindowPattern.WindowOpenedEvent, AutomationElement.RootElement, TreeScope.Children, _onWindowOpened);
            AutomationElementCollection open = AutomationElement.RootElement.FindAll(
                TreeScope.Children,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));
            foreach (AutomationElement element in open)
            {
                _openedWindows.Enqueue(element);
            }
        }

        _writer.Write(TraceKind.Trace, "recorder: waiting for the UefiAccess window");
        _say("Waiting for the UefiAccess window: start UefiAccess now, or press Ctrl+C to stop.");
        try
        {
            while (true)
            {
                // Reset before looking: a window queued or a stop asked for after this point sets
                // the event again, so the wait below cannot miss it.
                _wake.Reset();
                if (StopRequested)
                {
                    return null;
                }

                HashSet<int>? appProcesses = null;
                while (_openedWindows.TryDequeue(out AutomationElement? candidate))
                {
                    appProcesses ??= AppProcessIds();
                    if (IsUefiAccessWindow(candidate, appProcesses, out facts, out handle))
                    {
                        return candidate;
                    }
                }

                _wake.Wait();
            }
        }
        finally
        {
            try
            {
                Automation.RemoveAutomationEventHandler(WindowPattern.WindowOpenedEvent, AutomationElement.RootElement, _onWindowOpened);
            }
            catch (Exception ex)
            {
                // RemoveAllEventHandlers at the end removes it in any case.
                _writer.Write(TraceKind.Trace, "recorder: removing the WindowOpened subscription failed: " + ex.GetType().Name);
            }

            _openedWindows.Clear();
        }
    }

    /// <summary>The ids of the running UefiAccess.App processes.</summary>
    private static HashSet<int> AppProcessIds()
    {
        Process[] processes = Process.GetProcessesByName(AppProcessName);
        try
        {
            return processes.Select(process => process.Id).ToHashSet();
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }

    /// <summary>
    /// True for a WPF window of a UefiAccess.App process, judged from the discovery cache;
    /// <paramref name="handle"/> is its window handle, 0 when not cached. A WPF window reports the
    /// framework WPF and, depending on the route it was found by, the class of its window handle
    /// (HwndWrapper[...], through WindowOpened) or the class of its automation peer ("Window",
    /// through FindAll; both measured on 1 October 2026), so either the framework or that class
    /// prefix will do. The application's other windows (an error message box, the folder dialog)
    /// are Win32 dialogs and do not match. A window of the process that does not match is written
    /// as a TRACE line, so a trace that never starts recording shows why.
    /// </summary>
    private bool IsUefiAccessWindow(AutomationElement candidate, HashSet<int> appProcesses, out ElementFacts facts, out int handle)
    {
        handle = 0;
        facts = UiaElementFacts.FromCached(candidate);
        if (!facts.Available || facts.ProcessId is not int processId || !appProcesses.Contains(processId))
        {
            return false;
        }

        string? className = CachedText(() => candidate.Cached.ClassName);
        string? framework = CachedText(() => candidate.Cached.FrameworkId);
        bool wpf = string.Equals(framework, WpfFrameworkId, StringComparison.Ordinal)
            || className?.StartsWith(WpfWindowClassPrefix, StringComparison.Ordinal) == true;
        if (facts.ControlType != "Window" || !wpf)
        {
            _writer.Write(
                TraceKind.Trace,
                "recorder: a window of process " + processId.ToString(CultureInfo.InvariantCulture) + " was not taken: "
                    + UiaEventText.Element(facts) + " class=" + TraceLine.Token(className) + " framework=" + TraceLine.Token(framework));
            return false;
        }

        try
        {
            handle = candidate.Cached.NativeWindowHandle;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException)
        {
            handle = 0;
        }

        return true;
    }

    /// <summary>One cached text property of a discovered window; null when it was not cached or the window is gone.</summary>
    private static string? CachedText(Func<string?> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// The window taken again from its handle, the element the subscriptions are anchored on (the
    /// class remarks say why the discovered element is not); the discovered element when there is
    /// no handle or it cannot be used; null when the window is gone.
    /// </summary>
    private AutomationElement? WindowFromHandle(AutomationElement discovered, int handle)
    {
        if (handle == 0)
        {
            _writer.Write(TraceKind.Trace, "recorder: the window's handle was not cached; subscribing on the element as discovered");
            return discovered;
        }

        try
        {
            return AutomationElement.FromHandle(new IntPtr(handle));
        }
        catch (ElementNotAvailableException)
        {
            RequestStop("the UefiAccess window closed before the subscriptions were made");
            return null;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _writer.Write(TraceKind.Trace, "recorder: the window could not be taken from its handle (" + ex.GetType().Name + "); subscribing on the element as discovered");
            return discovered;
        }
    }

    /// <summary>Stops the recording when the UefiAccess process exits.</summary>
    private void WatchProcess(int processId)
    {
        try
        {
            Process target = Process.GetProcessById(processId);
            target.EnableRaisingEvents = true;
            target.Exited += OnTargetExited;
            _target = target;
            if (target.HasExited)
            {
                RequestStop("UefiAccess closed");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            RequestStop("UefiAccess closed before the recording began");
        }
        catch (Win32Exception ex)
        {
            _writer.Write(TraceKind.Trace, "recorder: cannot watch process " + processId.ToString(CultureInfo.InvariantCulture) + " for its exit (error " + ex.NativeErrorCode.ToString(CultureInfo.InvariantCulture) + "); stop with Ctrl+C");
            _say("The recorder cannot tell when UefiAccess closes: press Ctrl+C to stop.");
        }
    }

    /// <summary>
    /// The cache request of the event subscriptions: the element's Name, AutomationId, ControlType,
    /// ProcessId and FrameworkId, raw view, in the given mode (<see cref="AutomationElementMode.None"/>
    /// for the window's events, <see cref="AutomationElementMode.Full"/> for focus; the class remarks
    /// say why).
    /// </summary>
    private static CacheRequest EventCache(AutomationElementMode mode)
    {
        CacheRequest request = new()
        {
            AutomationElementMode = mode,
            TreeFilter = Automation.RawViewCondition,
        };
        request.Add(AutomationElement.NameProperty);
        request.Add(AutomationElement.AutomationIdProperty);
        request.Add(AutomationElement.ControlTypeProperty);
        request.Add(AutomationElement.ProcessIdProperty);
        request.Add(AutomationElement.FrameworkIdProperty);
        return request;
    }

    /// <summary>Makes every subscription <see cref="_events"/> asks for, each under its cache request.</summary>
    private void Subscribe(AutomationElement window)
    {
        List<string> subscribed = new();
        if (Wants(RecorderEvents.Focus))
        {
            using (EventCache(AutomationElementMode.Full).Activate())
            {
                TrySubscribe(subscribed, UiaEventNames.FocusChanged + " (global; other processes and the dialogs of UefiAccess written without a name)", () => Automation.AddAutomationFocusChangedEventHandler(_onFocusChanged));
            }
        }

        using (EventCache(AutomationElementMode.None).Activate())
        {
            if (Wants(RecorderEvents.Selection))
            {
                TrySubscribe(subscribed, UiaEventNames.ElementSelected, () => Automation.AddAutomationEventHandler(SelectionItemPattern.ElementSelectedEvent, window, TreeScope.Subtree, _onEvent));
                TrySubscribe(subscribed, UiaEventNames.ElementAddedToSelection, () => Automation.AddAutomationEventHandler(SelectionItemPattern.ElementAddedToSelectionEvent, window, TreeScope.Subtree, _onEvent));
                TrySubscribe(subscribed, UiaEventNames.ElementRemovedFromSelection, () => Automation.AddAutomationEventHandler(SelectionItemPattern.ElementRemovedFromSelectionEvent, window, TreeScope.Subtree, _onEvent));
            }

            if (Wants(RecorderEvents.Structure))
            {
                TrySubscribe(subscribed, UiaEventNames.StructureChanged, () => Automation.AddStructureChangedEventHandler(window, TreeScope.Subtree, _onStructureChanged));
            }

            if (Wants(RecorderEvents.Live))
            {
                TrySubscribe(subscribed, UiaEventNames.LiveRegionChanged, () => Automation.AddAutomationEventHandler(AutomationElementIdentifiers.LiveRegionChangedEvent, window, TreeScope.Subtree, _onEvent));
            }

            if (Wants(RecorderEvents.Notification))
            {
                TrySubscribe(subscribed, UiaEventNames.Notification, () => Automation.AddAutomationEventHandler(AutomationElement.NotificationEvent, window, TreeScope.Subtree, _onEvent));
            }

            if (Wants(RecorderEvents.Async))
            {
                TrySubscribe(subscribed, UiaEventNames.AsyncContentLoaded, () => Automation.AddAutomationEventHandler(AutomationElement.AsyncContentLoadedEvent, window, TreeScope.Subtree, _onEvent));
            }

            if (Wants(RecorderEvents.Properties))
            {
                TrySubscribe(
                    subscribed,
                    UiaEventNames.PropertyChanged + " (" + string.Join(", ", UiaEventNames.Properties) + ")",
                    () => Automation.AddAutomationPropertyChangedEventHandler(window, TreeScope.Subtree, _onPropertyChanged, WatchedProperties));
            }
        }

        _writer.Write(TraceKind.Trace, "recorder: subscribed: " + (subscribed.Count == 0 ? "nothing" : string.Join("; ", subscribed)) + " (all but focus on the window's subtree)");
    }

    private bool Wants(RecorderEvents family) => (_events & family) == family;

    /// <summary>Makes one subscription; a failure is written as a TRACE line and the others still go ahead.</summary>
    private void TrySubscribe(List<string> subscribed, string name, Action subscribe)
    {
        try
        {
            subscribe();
            subscribed.Add(name);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            _writer.Write(TraceKind.Trace, "recorder: could not subscribe to " + name + ": " + ex.GetType().Name);
            if (ex is ElementNotAvailableException)
            {
                RequestStop("the UefiAccess window closed before the subscriptions were made");
            }
        }
    }

    /// <summary>Waits, without a time-out, until a stop is asked for.</summary>
    private void WaitForStop()
    {
        while (true)
        {
            _wake.Reset();
            if (StopRequested)
            {
                return;
            }

            _wake.Wait();
        }
    }

    /// <summary>
    /// Removes every subscription of this process; called on the main thread only. After a failed
    /// subscription the managed client can throw here (the class remarks); the recording is over, so
    /// whatever it throws is written as a TRACE line and the summary still follows.
    /// </summary>
    private void RemoveSubscriptions()
    {
        try
        {
            Automation.RemoveAllEventHandlers();
        }
        catch (Exception ex)
        {
            _writer.Write(TraceKind.Trace, "recorder: removing the subscriptions failed: " + ex.GetType().Name);
        }
    }

    /// <summary>Records why the recording stops (the first reason wins) and wakes the main thread.</summary>
    private void RequestStop(string reason)
    {
        Interlocked.CompareExchange(ref _stopReason, reason, null);
        try
        {
            _wake.Set();
        }
        catch (ObjectDisposedException)
        {
            // The recording is already over.
        }
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        if (e.SpecialKey == ConsoleSpecialKey.ControlC && !StopRequested)
        {
            // The first Ctrl+C ends the recording in order; a second one is not held back.
            e.Cancel = true;
        }

        RequestStop("Ctrl+C pressed");
    }

    private void OnTargetExited(object? sender, EventArgs e) => RequestStop("UefiAccess closed");

    /// <summary>WindowOpened on the desktop: queue the window for the main thread, nothing more.</summary>
    private void OnWindowOpened(object? sender, AutomationEventArgs e)
    {
        if (sender is AutomationElement element)
        {
            _openedWindows.Enqueue(element);
            try
            {
                _wake.Set();
            }
            catch (ObjectDisposedException)
            {
                // The recording is already over.
            }
        }
    }

    private void OnFocusChanged(object? sender, AutomationFocusChangedEventArgs e)
    {
        long timestamp = Stopwatch.GetTimestamp();
        DateTime local = DateTime.Now;
        int threadId = Environment.CurrentManagedThreadId;
        try
        {
            // Focus is subscribed for the whole desktop: a name is written only for WPF content of
            // UefiAccess (UiaEventText.FocusChanged says what is written for anything else).
            ElementFacts facts = UiaElementFacts.FromCached(sender as AutomationElement);
            string details = UiaEventText.FocusChanged(facts, _targetProcessId, out string countKey);
            _writer.Write(timestamp, local, threadId, TraceKind.UiaReceived, details);
            _counts.Add(countKey);
        }
        catch (Exception)
        {
            // Never back into UI Automation; counted for the summary.
            _counts.AddHandlerError();
        }
    }

    private void OnAutomationEvent(object? sender, AutomationEventArgs e)
    {
        long timestamp = Stopwatch.GetTimestamp();
        DateTime local = DateTime.Now;
        int threadId = Environment.CurrentManagedThreadId;
        try
        {
            ElementFacts facts = UiaElementFacts.FromCached(sender as AutomationElement);
            string name = EventName(e.EventId);
            bool copy = false;
            string details = e switch
            {
                NotificationEventArgs notification => UiaEventText.Notification(
                    notification.NotificationKind.ToString(),
                    notification.NotificationProcessing.ToString(),
                    notification.DisplayString,
                    notification.ActivityId,
                    facts),
                AsyncContentLoadedEventArgs loaded => UiaEventText.AsyncContentLoaded(loaded.AsyncContentLoadedState.ToString(), loaded.PercentComplete, facts),
                _ when string.Equals(name, UiaEventNames.LiveRegionChanged, StringComparison.Ordinal) => _values.LiveRegionChanged(facts, out copy),
                _ => UiaEventText.Event(name, facts),
            };
            _writer.Write(timestamp, local, threadId, TraceKind.UiaReceived, details);
            _counts.Add(name);
            if (copy)
            {
                _counts.AddLiveRegionCopy();
            }
        }
        catch (Exception)
        {
            // Never back into UI Automation; counted for the summary.
            _counts.AddHandlerError();
        }
    }

    private void OnStructureChanged(object? sender, StructureChangedEventArgs e)
    {
        long timestamp = Stopwatch.GetTimestamp();
        DateTime local = DateTime.Now;
        int threadId = Environment.CurrentManagedThreadId;
        try
        {
            ElementFacts facts = UiaElementFacts.FromCached(sender as AutomationElement);
            _writer.Write(timestamp, local, threadId, TraceKind.UiaReceived, UiaEventText.StructureChanged(e.StructureChangeType.ToString(), e.GetRuntimeId(), facts));
            _counts.Add(UiaEventNames.StructureChanged);
        }
        catch (Exception)
        {
            // Never back into UI Automation; counted for the summary.
            _counts.AddHandlerError();
        }
    }

    private void OnPropertyChanged(object? sender, AutomationPropertyChangedEventArgs e)
    {
        long timestamp = Stopwatch.GetTimestamp();
        DateTime local = DateTime.Now;
        int threadId = Environment.CurrentManagedThreadId;
        try
        {
            ElementFacts facts = UiaElementFacts.FromCached(sender as AutomationElement);
            string property = PropertyName(e.Property);
            _writer.Write(timestamp, local, threadId, TraceKind.UiaReceived, _values.PropertyChanged(property, e.OldValue, e.NewValue, facts));
            _counts.AddPropertyChange(property);
        }
        catch (Exception)
        {
            // Never back into UI Automation; counted for the summary.
            _counts.AddHandlerError();
        }
    }

    /// <summary>The recorder's name of an automation event.</summary>
    private static string EventName(AutomationEvent? eventId)
    {
        if (eventId is null)
        {
            return "?";
        }

        int id = eventId.Id;
        if (id == SelectionItemPattern.ElementSelectedEvent.Id)
        {
            return UiaEventNames.ElementSelected;
        }

        if (id == SelectionItemPattern.ElementAddedToSelectionEvent.Id)
        {
            return UiaEventNames.ElementAddedToSelection;
        }

        if (id == SelectionItemPattern.ElementRemovedFromSelectionEvent.Id)
        {
            return UiaEventNames.ElementRemovedFromSelection;
        }

        if (id == AutomationElementIdentifiers.LiveRegionChangedEvent.Id)
        {
            return UiaEventNames.LiveRegionChanged;
        }

        if (id == AutomationElement.NotificationEvent.Id)
        {
            return UiaEventNames.Notification;
        }

        if (id == AutomationElement.AsyncContentLoadedEvent.Id)
        {
            return UiaEventNames.AsyncContentLoaded;
        }

        return ShortName(eventId.ProgrammaticName, "Event");
    }

    /// <summary>The recorder's name of a watched property, as in <see cref="UiaEventNames.Properties"/>.</summary>
    private static string PropertyName(AutomationProperty? property)
    {
        if (property is null)
        {
            return "?";
        }

        for (int i = 0; i < WatchedProperties.Length; i++)
        {
            if (WatchedProperties[i].Id == property.Id)
            {
                return UiaEventNames.Properties[i];
            }
        }

        return ShortName(property.ProgrammaticName, "Property");
    }

    /// <summary>"SelectionItemPatternIdentifiers.ElementSelectedEvent" becomes "ElementSelected".</summary>
    private static string ShortName(string? programmaticName, string suffix)
    {
        string name = programmaticName ?? "?";
        int dot = name.LastIndexOf('.');
        if (dot >= 0)
        {
            name = name[(dot + 1)..];
        }

        return name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length ? name[..^suffix.Length] : name;
    }
}
