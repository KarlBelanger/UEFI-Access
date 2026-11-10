// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Serilog;

namespace UefiAccess.Presentation.Accessibility;

/// <summary>
/// The posting of the WPF detail scheduler (Alpha 2 finding A2-7, the tester's A2-5), kept free of any
/// dispatcher so that it is tested on any host with a clock of the test's own: a post waits for
/// the quiet period (<see cref="DetailRefreshTiming.QuietPeriod"/>) before its work is queued for
/// the host's idle turn. <see cref="Post"/> is the posting a <see cref="CoalescingDetailScheduler"/>
/// takes: it keeps the work, starts the host's one timer (or starts it again from zero) and returns
/// the delegate that withdraws the post. When the timer elapses (<see cref="OnQuietPeriodElapsed"/>,
/// the timer's tick), the timer is stopped first, then the work is queued for the host's idle turn
/// (the WPF adapter queues it at Background priority, below input). A withdrawal stops the timer
/// and withdraws the queued work when it was queued already. The policy withdraws whenever a key or
/// a mouse button goes down in the variable list (its hold) and posts again when it comes up (its
/// release), so every press starts the quiet period anew, and a burst of separate quick presses
/// ends in one refresh, a quiet period after the last key came up; the policy's flush withdraws and
/// runs the refresh at once (focus into the pane, an Alt key, Copy value, Export, a Change command),
/// so nothing the person asks for waits. One post is outstanding at a
/// time: a new post replaces one that is still outstanding. The work queued at the idle turn runs
/// once, or not at all when it was withdrawn. Not thread-safe: the host serialises the calls (the
/// WPF adapter makes them on the UI thread).
/// </summary>
public sealed class QuietPeriodPoster
{
    private readonly Action _restartTimer;
    private readonly Action _stopTimer;
    private readonly Func<Action, Action> _queueAtIdle;
    private readonly ILogger _logger;
    private PendingPost? _pending;

    /// <summary>Creates the posting over the host's timer and idle turn.</summary>
    /// <param name="restartTimer">Starts the host's one timer, whose interval is the quiet period, or starts it again from zero when it runs.</param>
    /// <param name="stopTimer">Stops the timer; stopping a timer that does not run changes nothing.</param>
    /// <param name="queueAtIdle">Queues the work for the host's idle turn and returns the delegate that withdraws it.</param>
    /// <param name="logger">Optional logger; null disables logging.</param>
    /// <exception cref="ArgumentNullException">A delegate is null.</exception>
    public QuietPeriodPoster(Action restartTimer, Action stopTimer, Func<Action, Action> queueAtIdle, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(restartTimer);
        ArgumentNullException.ThrowIfNull(stopTimer);
        ArgumentNullException.ThrowIfNull(queueAtIdle);
        _restartTimer = restartTimer;
        _stopTimer = stopTimer;
        _queueAtIdle = queueAtIdle;
        _logger = logger ?? Serilog.Core.Logger.None;
    }

    /// <summary>True while a post waits for the quiet period to pass.</summary>
    public bool IsWaiting => _pending is { IsQueued: false };

    /// <summary>True while the work of a post is queued for the host's idle turn.</summary>
    public bool IsQueued => _pending is { IsQueued: true };

    /// <summary>
    /// Keeps the work until the quiet period has passed and starts the timer again from zero; a
    /// post still outstanding is withdrawn first.
    /// </summary>
    /// <param name="work">The work to run at the host's idle turn once the quiet period has passed.</param>
    /// <returns>The delegate that withdraws this post; it does nothing once the work has run or another post replaced it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="work"/> is null.</exception>
    public Action Post(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (_pending is PendingPost outstanding)
        {
            Withdraw(outstanding);
        }

        PendingPost pending = new(work);
        _pending = pending;
        try
        {
            _restartTimer();
        }
        catch
        {
            // The caller runs the work itself when the post fails (CoalescingDetailScheduler).
            _pending = null;
            throw;
        }

        return () => Withdraw(pending);
    }

    /// <summary>
    /// The timer's tick: the quiet period has passed. The timer is stopped first, then the waiting
    /// work is queued for the host's idle turn. Work that cannot be queued runs now (logged as a
    /// warning). Nothing happens when no post waits.
    /// </summary>
    public void OnQuietPeriodElapsed()
    {
        _stopTimer();
        PendingPost? pending = _pending;
        if (pending is null || pending.IsQueued)
        {
            return;
        }

        try
        {
            pending.WithdrawQueued = _queueAtIdle(() => Serve(pending));
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Queuing the detail refresh after the quiet period failed; running it now");
            Serve(pending);
        }
    }

    /// <summary>The host's idle turn: runs the work of the post, unless it was withdrawn or replaced meanwhile.</summary>
    private void Serve(PendingPost pending)
    {
        if (!ReferenceEquals(_pending, pending))
        {
            return;
        }

        _pending = null;
        pending.Work();
    }

    /// <summary>Withdraws the post when it is still the outstanding one: the timer stops and queued work is withdrawn.</summary>
    private void Withdraw(PendingPost pending)
    {
        if (!ReferenceEquals(_pending, pending))
        {
            return;
        }

        _pending = null;
        _stopTimer();
        pending.WithdrawQueued?.Invoke();
    }

    /// <summary>One post: its work, and, once the quiet period has passed, the delegate that withdraws it from the idle turn.</summary>
    private sealed class PendingPost
    {
        public PendingPost(Action work) => Work = work;

        public Action Work { get; }

        public Action? WithdrawQueued { get; set; }

        public bool IsQueued => WithdrawQueued is not null;
    }
}
