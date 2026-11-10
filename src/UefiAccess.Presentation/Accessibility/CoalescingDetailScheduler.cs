// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Serilog;

namespace UefiAccess.Presentation.Accessibility;

/// <summary>
/// The scheduling policy of the deferred detail refresh (Alpha 1 finding A1-6), kept free of any
/// dispatcher so that it is tested on any host and the WPF class is a thin adapter over it. The
/// rules: one refresh is pending at a time, the newest replacing the one before it; one post is
/// outstanding at a time, so a burst of selection changes queues one piece of work, which runs
/// whatever is pending at that moment; while the scheduler is <see cref="Hold">held</see> (the
/// window holds it from the moment a key or a mouse button goes down in the variable list until
/// it comes up) nothing is posted and an outstanding post is withdrawn, so the pane is not rebuilt
/// while the key that selected the row is still down and the screen reader is fetching the row's
/// name; <see cref="Release"/> posts the pending refresh, when there is one; <see cref="Flush"/>
/// withdraws the post and runs the pending refresh at once, held or not, so a command or a focus
/// move into the pane always sees the selected row. The host supplies the posting: a delegate
/// that queues the work to run later and returns the delegate that withdraws it (the WPF adapter
/// posts through <see cref="QuietPeriodPoster"/>, which waits for a quiet period and then queues
/// the work at Background priority, and withdraws by stopping its timer and aborting the queued
/// operation; the recording scheduler of the tests keeps the work until a test runs it). A post
/// that cannot be queued runs the refresh synchronously; a refresh that throws is logged and never
/// rethrown; and the policy itself is not timed: every step is the direct consequence of a
/// selection change, a key, a flush or the host's serving of the post. Not thread-safe: the host
/// serialises the calls (the WPF adapter marshals them to the UI thread).
/// </summary>
/// <remarks>
/// Measured against separate quick presses (Alpha 1 finding A1-6 as the tester reproduced it, typically
/// 150 to 250 ms apart): the policy has no delay of its own, so with a host that serves a post at
/// its first idle turn, the refresh posted at a key's release ran long before the next press, and
/// separate presses with an idle gap between them cost one refresh each while NVDA was still
/// reading the row (Alpha 2 finding A2-7, the tester's A2-5: "A1-6 is not fixed"). The WPF adapter's
/// posting therefore waits out a quiet period (<see cref="DetailRefreshTiming.QuietPeriod"/>)
/// before its idle turn: every hold withdraws the post and every release posts again, so the
/// quiet period starts anew with every press and a burst of presses costs one refresh, after the
/// last. <see cref="Flush"/> keeps Enter, Space and F6 into the pane immediate, and it runs as well
/// on the Alt key of an access key, and before Copy value, Export and every Change command.
/// </remarks>
public sealed class CoalescingDetailScheduler : IDetailScheduler
{
    private readonly Func<Action, Action> _post;
    private readonly ILogger _logger;
    private Action? _pending;
    private Action? _withdraw;
    private bool _held;

    /// <summary>Creates the policy over a host's posting.</summary>
    /// <param name="post">
    /// Queues the work to run later (when the host is idle; for the WPF adapter, once a quiet period
    /// has passed) and returns the delegate that withdraws the queued work; it is called with the
    /// same work at most once per outstanding post.
    /// </param>
    /// <param name="logger">Optional logger; null disables logging.</param>
    /// <exception cref="ArgumentNullException"><paramref name="post"/> is null.</exception>
    public CoalescingDetailScheduler(Func<Action, Action> post, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(post);
        _post = post;
        _logger = logger ?? Serilog.Core.Logger.None;
    }

    /// <summary>True while a scheduled refresh has not run yet.</summary>
    public bool IsPending => _pending is not null;

    /// <summary>True while a post is outstanding with the host: the pending refresh runs when the host serves it.</summary>
    public bool IsPosted => _withdraw is not null;

    /// <summary>True between <see cref="Hold"/> and <see cref="Release"/>: nothing is posted, and only <see cref="Flush"/> runs the pending refresh.</summary>
    public bool IsHeld => _held;

    /// <inheritdoc />
    public void Schedule(Action refresh)
    {
        ArgumentNullException.ThrowIfNull(refresh);
        _pending = refresh;
        if (_held || _withdraw is not null)
        {
            // Held: the release posts it. Posted: the outstanding post runs whatever is pending then.
            return;
        }

        Post();
    }

    /// <inheritdoc />
    public void Flush()
    {
        Withdraw();
        Run();
    }

    /// <summary>
    /// Holds the pending refresh back until <see cref="Release"/>: a refresh scheduled meanwhile
    /// stays pending, and an outstanding post is withdrawn. Calling it while held changes nothing.
    /// </summary>
    public void Hold()
    {
        _held = true;
        Withdraw();
    }

    /// <summary>Ends the hold and posts the pending refresh, when there is one. Does nothing when not held.</summary>
    public void Release()
    {
        if (!_held)
        {
            return;
        }

        _held = false;
        if (_pending is not null && _withdraw is null)
        {
            Post();
        }
    }

    private void Post()
    {
        try
        {
            _withdraw = _post(OnPostServed);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Queuing the detail refresh failed; running it now");
            _withdraw = null;
            Run();
        }
    }

    private void Withdraw()
    {
        Action? withdraw = _withdraw;
        _withdraw = null;
        if (withdraw is null)
        {
            return;
        }

        try
        {
            withdraw();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Withdrawing the queued detail refresh failed; it will find nothing to do");
        }
    }

    /// <summary>The host's idle turn: the post is served, so a refresh scheduled from inside the refresh posts anew.</summary>
    private void OnPostServed()
    {
        _withdraw = null;
        if (_held)
        {
            // A post the hold could not withdraw: the refresh stays pending until the release.
            return;
        }

        Run();
    }

    private void Run()
    {
        Action? refresh = _pending;
        _pending = null;
        if (refresh is null)
        {
            return;
        }

        try
        {
            refresh();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Refreshing the detail pane failed");
        }
    }
}
