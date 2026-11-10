// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Globalization;
using System.Windows.Threading;
using Serilog;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Presentation.Accessibility;
using UefiAccess.Presentation.Diagnostics;

namespace UefiAccess.App.Platform;

/// <summary>
/// The WPF <see cref="IDetailScheduler"/> (Alpha 1 finding A1-6; Alpha 2 finding A2-7, the tester's
/// A2-5): the <see cref="CoalescingDetailScheduler"/> policy over the window's dispatcher, with a
/// quiet period between a post and the rebuild of the detail pane. A post
/// (<see cref="QuietPeriodPoster"/>, unit-tested in Presentation) starts this scheduler's one
/// <see cref="DispatcherTimer"/>, whose interval is <see cref="DetailRefreshTiming.QuietPeriod"/>
/// (300 ms), or starts it again from zero; when the quiet period passes, the timer stops itself in
/// its tick and queues the rebuild with one
/// <see cref="Dispatcher.BeginInvoke(DispatcherPriority, Delegate)"/> at
/// <see cref="DispatcherPriority.Background"/>, a priority the dispatcher serves only when no input
/// is waiting, so the rebuild never runs inside a key press and gives way to every key press already
/// queued; a withdrawal stops the timer and aborts that operation
/// (<see cref="DispatcherOperation.Abort"/>) when it was queued already. The window
/// <see cref="Hold">holds</see> the scheduler from the moment a key or a mouse button goes down in
/// the variable list and <see cref="Release">releases</see> it when it comes up (or when the window
/// is deactivated): the hold withdraws the post and the release posts again, so every press starts
/// the quiet period anew. While an arrow key is held down and repeating nothing is rebuilt, and a
/// burst of separate quick presses (the tester's, 150 to 250 ms apart) is rebuilt once, a quiet period
/// after the last key came up, instead of once right after each key came up while the screen reader
/// was still fetching or speaking the row; the refresh that runs shows the row selected at that
/// moment. A selection change that is not held (a filter that hides the selected row) posts through
/// the same path. <see cref="Flush"/> runs the pending refresh at once, held or not (the shell calls
/// it before focus moves into the pane, on an Alt key before any access key acts, the Change group's
/// among them, and before Copy value, Export and every Change command), so nothing the person asks for waits. The timer is the one timed behaviour of the application (the one exception to hard
/// rule 4, pinned by WpfShellAccessibilityTests): it runs only after a selection changed, and what
/// it runs says nothing and moves no focus. A refresh that throws is logged, never rethrown. Calls
/// from another thread are marshalled to the dispatcher's thread.
/// </summary>
/// <remarks>
/// With the input trace on (<see cref="InputTrace"/>, set when the application was started with
/// --trace-input; Alpha 2 finding A2-7, the tester's question about the continued A1-6 issue), every
/// step is traced as a DETAIL line, read from the public state of the policy and the poster around
/// the calls the summary describes, which stay as they are: a selection change scheduled, a hold
/// (and the post it withdrew, or the queued rebuild it aborted, with how long it had waited), a
/// release (and the post it made, with the quiet period it started), its tick (and how late it
/// came), the idle turn that serves the post (and how long it sat in the queue), and a flush (and
/// whether it ran a rebuild). The quiet period starts inside the step that posts, so the step's
/// own line carries it, written once the step's call has returned. After the idle turn, one more
/// operation at ApplicationIdle priority, queued only while tracing, writes an IDLE line with how
/// long the window was busy from the start of that turn until the dispatcher reached
/// ApplicationIdle; a render WPF defers to the next frame (while the frame before is still being
/// presented) can follow that moment, with the pane's layout in it. Off, the trace is
/// <see cref="NullInputTrace"/> and each step costs one property read.
/// </remarks>
public sealed class DispatcherDetailScheduler : IDetailScheduler
{
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _quietPeriod;
    private readonly QuietPeriodPoster _poster;
    private readonly CoalescingDetailScheduler _policy;
    private IInputTrace _inputTrace = NullInputTrace.Instance;
    private long _quietPeriodStartedAt;
    private long _queuedAt;
    private int _tracedSteps;
    private string? _quietPeriodNote;

    /// <summary>Creates the scheduler over the dispatcher of the window that shows the detail pane.</summary>
    /// <param name="dispatcher">The UI thread's dispatcher.</param>
    /// <param name="logger">Optional logger; null disables logging.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dispatcher"/> is null.</exception>
    public DispatcherDetailScheduler(Dispatcher dispatcher, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
        _quietPeriod = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = DetailRefreshTiming.QuietPeriod };
        _quietPeriod.Tick += OnQuietPeriodTick;
        _poster = new QuietPeriodPoster(RestartQuietPeriod, _quietPeriod.Stop, QueueAtIdle, logger);
        _policy = new CoalescingDetailScheduler(_poster.Post, logger);
    }

    /// <summary>True while a refresh is queued and has not run yet.</summary>
    public bool IsPending => _policy.IsPending;

    /// <summary>True between <see cref="Hold"/> and <see cref="Release"/>.</summary>
    public bool IsHeld => _policy.IsHeld;

    /// <summary>
    /// The input trace: <see cref="NullInputTrace.Instance"/> unless the application set it, once at
    /// start-up, because it was started with --trace-input. A null value sets
    /// <see cref="NullInputTrace.Instance"/>.
    /// </summary>
    public IInputTrace InputTrace
    {
        get => _inputTrace;
        set => _inputTrace = value ?? NullInputTrace.Instance;
    }

    /// <inheritdoc />
    public void Schedule(Action refresh)
    {
        ArgumentNullException.ThrowIfNull(refresh);
        if (!_dispatcher.CheckAccess())
        {
            // The shell lives on the UI thread; a call from elsewhere is queued to it.
            _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => Schedule(refresh)));
            return;
        }

        SchedulingState? before = TraceBefore();
        _policy.Schedule(refresh);
        TraceAfter("schedule (selection changed)", before);
    }

    /// <inheritdoc />
    public void Flush()
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(Flush);
            return;
        }

        SchedulingState? before = TraceBefore();
        _policy.Flush();
        TraceAfter("flush", before);
    }

    /// <summary>
    /// A key or a mouse button went down in the variable list: the pending refresh, and any
    /// scheduled until <see cref="Release"/>, waits; the quiet period stops, and a queued operation
    /// is aborted. UI thread only.
    /// </summary>
    public void Hold()
    {
        _dispatcher.VerifyAccess();
        SchedulingState? before = TraceBefore();
        _policy.Hold();
        TraceAfter("hold", before);
    }

    /// <summary>
    /// The key or button came up (or the window was deactivated): the pending refresh, when there
    /// is one, is posted, and the quiet period starts again from zero. UI thread only.
    /// </summary>
    public void Release()
    {
        _dispatcher.VerifyAccess();
        SchedulingState? before = TraceBefore();
        _policy.Release();
        TraceAfter("release", before);
    }

    /// <summary>
    /// Starts the quiet period again from zero (a running timer is stopped first: starting it alone
    /// would keep its old due time). With the trace on, what happened is kept for the DETAIL line of
    /// the step that posted (the poster calls this from inside that step's call), so the effect is
    /// not written before its cause; outside a traced step it is written at once.
    /// </summary>
    private void RestartQuietPeriod()
    {
        bool tracing = _inputTrace.IsEnabled;
        bool running = tracing && _quietPeriod.IsEnabled;
        _quietPeriod.Stop();
        _quietPeriod.Start();
        if (tracing)
        {
            long now = Stopwatch.GetTimestamp();
            string what = (running
                ? "quiet period restarted " + TraceLine.Milliseconds(_quietPeriodStartedAt, now) + " ms after it started"
                : "quiet period started")
                + " (" + DetailRefreshTiming.QuietPeriod.TotalMilliseconds.ToString(CultureInfo.InvariantCulture) + " ms, then an idle turn at Background priority)";
            _quietPeriodStartedAt = now;
            if (_tracedSteps > 0)
            {
                _quietPeriodNote = what;
            }
            else
            {
                _inputTrace.Write(TraceKind.Detail, what);
            }
        }
    }

    /// <summary>The quiet period has passed: the poster stops the timer and queues the waiting refresh at Background priority.</summary>
    private void OnQuietPeriodTick(object? sender, EventArgs e)
    {
        SchedulingState? before = _inputTrace.IsEnabled ? Capture() : null;
        long tickAt = before is null ? 0 : Stopwatch.GetTimestamp();
        _poster.OnQuietPeriodElapsed();
        if (before is SchedulingState was)
        {
            string what = was.Waiting ? "rebuild queued at Background priority" : "nothing waiting";
            _inputTrace.Write(TraceKind.Detail, "quiet period passed after " + TraceLine.Milliseconds(_quietPeriodStartedAt, tickAt) + " ms (timer tick); " + what + "; " + Capture().Describe());
        }
    }

    /// <summary>Queues the work at Background priority, below input; the returned delegate aborts it.</summary>
    private Action QueueAtIdle(Action work)
    {
        work = TraceServed(work);
        DispatcherOperation operation = _dispatcher.BeginInvoke(DispatcherPriority.Background, work);
        return () => operation.Abort();
    }

    /// <summary>
    /// With the input trace on, wraps the work of the idle turn so that the turn is traced: how
    /// long the post sat in the queue, then, once the turn's work is done, one operation at
    /// ApplicationIdle priority that writes how long the window stayed busy until the dispatcher
    /// reached that priority (a render WPF defers to the next frame can still follow). Off,
    /// returns the same delegate.
    /// </summary>
    private Action TraceServed(Action work)
    {
        if (!_inputTrace.IsEnabled)
        {
            return work;
        }

        IInputTrace trace = _inputTrace;
        long queuedAt = Stopwatch.GetTimestamp();
        _queuedAt = queuedAt;
        return () =>
        {
            long servedAt = Stopwatch.GetTimestamp();
            trace.Write(TraceKind.Detail, "idle turn after " + TraceLine.Milliseconds(queuedAt, servedAt) + " ms in the queue: serving the post");
            try
            {
                work();
            }
            finally
            {
                _dispatcher.BeginInvoke(
                    DispatcherPriority.ApplicationIdle,
                    new Action(() => trace.Write(TraceKind.Idle, "after rebuild: busy " + TraceLine.Milliseconds(servedAt, Stopwatch.GetTimestamp()) + " ms from the start of the idle turn until the dispatcher reached ApplicationIdle (a render WPF defers to the next frame can follow)")));
            }
        };
    }

    /// <summary>
    /// The scheduling state before a step, for the trace, and the start of a traced step (a quiet
    /// period started inside it goes into the step's own line); null (nothing read) when the trace
    /// is off.
    /// </summary>
    private SchedulingState? TraceBefore()
    {
        if (!_inputTrace.IsEnabled)
        {
            return null;
        }

        _tracedSteps++;
        return Capture();
    }

    /// <summary>
    /// Ends a traced step and writes its DETAIL line: what changed between <paramref name="before"/>
    /// and now (the quiet period the step started among it), and the state now. Does nothing when
    /// the trace is off.
    /// </summary>
    private void TraceAfter(string step, SchedulingState? before)
    {
        if (before is not SchedulingState was)
        {
            return;
        }

        _tracedSteps = Math.Max(0, _tracedSteps - 1);
        string? quietPeriod = _quietPeriodNote;
        _quietPeriodNote = null;
        if (!_inputTrace.IsEnabled)
        {
            return;
        }

        long now = Stopwatch.GetTimestamp();
        SchedulingState after = Capture();
        List<string> changes = new();
        if (!was.Held && after.Held)
        {
            changes.Add("held");
        }

        if (was.Held && !after.Held)
        {
            changes.Add("released");
        }

        if (was.Waiting && !after.Waiting && !after.Queued)
        {
            changes.Add("withdrew the post " + TraceLine.Milliseconds(_quietPeriodStartedAt, now) + " ms into the quiet period");
        }

        if (was.Queued && !after.Queued)
        {
            changes.Add("aborted the rebuild queued at Background priority " + TraceLine.Milliseconds(_queuedAt, now) + " ms before");
        }

        if (was.Pending && !after.Pending)
        {
            changes.Add("ran the pending rebuild");
        }

        if (!was.Posted && after.Posted)
        {
            changes.Add("posted");
        }

        if (quietPeriod is not null)
        {
            changes.Add(quietPeriod);
        }

        if (changes.Count == 0)
        {
            changes.Add(after.Held && after.Pending
                ? "the rebuild waits for the release"
                : after.Posted
                    ? "a post is outstanding and runs the newest refresh"
                    : after.Pending ? "nothing changed" : "nothing pending");
        }

        _inputTrace.Write(TraceKind.Detail, step + ": " + string.Join(", ", changes) + "; " + after.Describe());
    }

    /// <summary>The state of the policy and the poster now.</summary>
    private SchedulingState Capture()
    {
        return new SchedulingState(_policy.IsPending, _policy.IsPosted, _policy.IsHeld, _poster.IsWaiting, _poster.IsQueued);
    }

    /// <summary>The public state of the policy and the poster at one moment, for the input trace.</summary>
    private readonly record struct SchedulingState(bool Pending, bool Posted, bool Held, bool Waiting, bool Queued)
    {
        /// <summary>The state as the trace writes it.</summary>
        public string Describe()
        {
            return "pending=" + YesNo(Pending) + " posted=" + YesNo(Posted) + " held=" + YesNo(Held) + " waiting=" + YesNo(Waiting) + " queued=" + YesNo(Queued);
        }

        private static string YesNo(bool value) => value ? "yes" : "no";
    }
}
