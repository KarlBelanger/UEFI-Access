// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Core.Settings.Dell;

/// <summary>
/// The time one Dell read may take, shared by its steps: the
/// machine read, Dell Command | Monitor's query and namespace checks, and Dell Command | Configure's
/// runs each wait at most what is left, so the whole read ends within the total plus the few
/// seconds a timed-out run takes to be killed. The elapsed time is the caller's (a stopwatch in
/// production, a test's clock in tests).
/// </summary>
internal sealed class DellReadBudget
{
    private readonly Func<TimeSpan> _elapsed;

    /// <summary>Creates a budget of <paramref name="total"/> measured by <paramref name="elapsed"/>.</summary>
    /// <param name="total">The time the whole read may take; a negative total is taken as zero.</param>
    /// <param name="elapsed">The time spent so far.</param>
    public DellReadBudget(TimeSpan total, Func<TimeSpan> elapsed)
    {
        ArgumentNullException.ThrowIfNull(elapsed);
        Total = total > TimeSpan.Zero ? total : TimeSpan.Zero;
        _elapsed = elapsed;
    }

    /// <summary>The time the whole read may take.</summary>
    public TimeSpan Total { get; }

    /// <summary>What is left: the total less the time spent, never below zero.</summary>
    public TimeSpan Remaining
    {
        get
        {
            TimeSpan left = Total - _elapsed();
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    /// <summary>True when nothing is left.</summary>
    public bool Exhausted => Remaining == TimeSpan.Zero;

    /// <summary>The bound of one step: its own timeout, or what is left of the budget when that is less.</summary>
    /// <param name="step">The step's own timeout.</param>
    /// <returns>The smaller of the two.</returns>
    public TimeSpan Bound(TimeSpan step)
    {
        TimeSpan remaining = Remaining;
        return step < remaining ? step : remaining;
    }
}
