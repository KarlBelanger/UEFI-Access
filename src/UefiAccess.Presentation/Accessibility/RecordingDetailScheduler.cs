// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Presentation.Accessibility;

/// <summary>
/// An <see cref="IDetailScheduler"/> for tests: the same <see cref="CoalescingDetailScheduler"/>
/// policy the WPF dispatcher scheduler runs, over a host that keeps the posted work until the
/// test runs it with <see cref="RunPending"/>, which stands in for the dispatcher's serving of the
/// post (in the application, the idle turn after the quiet period, <see cref="QuietPeriodPoster"/>);
/// and it counts what it was asked. A scheduled refresh stays pending, replacing the one before
/// it, until the test runs it or the shell calls <see cref="Flush"/>.
/// </summary>
public sealed class RecordingDetailScheduler : IDetailScheduler
{
    private CoalescingDetailScheduler _policy;
    private Action? _posted;

    /// <summary>Creates the scheduler with nothing pending and every count at zero.</summary>
    public RecordingDetailScheduler()
    {
        _policy = new CoalescingDetailScheduler(Post);
    }

    /// <summary>How many times <see cref="Schedule"/> was called.</summary>
    public int ScheduleCalls { get; private set; }

    /// <summary>How many times <see cref="Flush"/> was called, whether or not a refresh was pending.</summary>
    public int FlushCalls { get; private set; }

    /// <summary>How many refreshes actually ran, through <see cref="Flush"/> or <see cref="RunPending"/>.</summary>
    public int RefreshesRun { get; private set; }

    /// <summary>True while a scheduled refresh has not run yet.</summary>
    public bool IsPending => _policy.IsPending;

    /// <summary>True while the policy has posted work that <see cref="RunPending"/> has not run yet.</summary>
    public bool IsPosted => _posted is not null;

    /// <summary>The policy underneath, for a test that holds and releases it the way the window does.</summary>
    public CoalescingDetailScheduler Policy => _policy;

    /// <summary>Keeps the refresh as the one pending refresh, replacing any earlier one.</summary>
    /// <param name="refresh">The refresh to keep.</param>
    /// <exception cref="ArgumentNullException"><paramref name="refresh"/> is null.</exception>
    public void Schedule(Action refresh)
    {
        ArgumentNullException.ThrowIfNull(refresh);
        ScheduleCalls++;
        _policy.Schedule(() =>
        {
            RefreshesRun++;
            refresh();
        });
    }

    /// <inheritdoc />
    public void Flush()
    {
        FlushCalls++;
        _policy.Flush();
    }

    /// <summary>The dispatcher's idle turn: runs the posted work, which runs the pending refresh when there is one.</summary>
    /// <returns>True when a refresh ran; false when nothing was posted or nothing was pending.</returns>
    public bool RunPending()
    {
        Action? work = _posted;
        _posted = null;
        if (work is null)
        {
            return false;
        }

        int before = RefreshesRun;
        work();
        return RefreshesRun > before;
    }

    /// <summary>Forgets the counts and drops any pending refresh and posted work without running them.</summary>
    public void Clear()
    {
        _posted = null;
        _policy = new CoalescingDetailScheduler(Post);
        ScheduleCalls = 0;
        FlushCalls = 0;
        RefreshesRun = 0;
    }

    private Action Post(Action work)
    {
        _posted = work;
        return () =>
        {
            if (ReferenceEquals(_posted, work))
            {
                _posted = null;
            }
        };
    }
}
