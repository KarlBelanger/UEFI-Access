// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Serilog;
using Serilog.Core;
using Serilog.Events;
using UefiAccess.Presentation.Accessibility;
using Xunit;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// The policy the WPF detail scheduler runs (Alpha 1 finding A1-6), exercised on this host through
/// a fake posting that records what was posted and withdrawn, so the coalescing, the hold and
/// the flush that ship are the ones tested, not a second implementation written for the tests.
/// The recording scheduler the shell tests use wraps the same class.
/// </summary>
public sealed class CoalescingDetailSchedulerTests
{
    /// <summary>A host that keeps the posted work until the test serves it, like a dispatcher's idle turn.</summary>
    private sealed class FakeHost
    {
        public List<Action> Posted { get; } = new();

        public int Posts { get; private set; }

        public int Withdrawals { get; private set; }

        public bool ThrowOnPost { get; set; }

        public Action Post(Action work)
        {
            Posts++;
            if (ThrowOnPost)
            {
                throw new InvalidOperationException("no message loop");
            }

            Posted.Add(work);
            return () =>
            {
                Withdrawals++;
                Posted.Remove(work);
            };
        }

        /// <summary>The idle turn: runs every post outstanding now (the policy never has more than one).</summary>
        public int Serve()
        {
            Action[] work = Posted.ToArray();
            Posted.Clear();
            foreach (Action action in work)
            {
                action();
            }

            return work.Length;
        }
    }

    [Fact]
    public void Constructor_RejectsANullPost_AndScheduleRejectsANullRefresh()
    {
        Assert.Throws<ArgumentNullException>(() => new CoalescingDetailScheduler(null!));
        CoalescingDetailScheduler scheduler = new(new FakeHost().Post);
        Assert.Throws<ArgumentNullException>(() => scheduler.Schedule(null!));
        Assert.False(scheduler.IsPending);
        Assert.False(scheduler.IsPosted);
        Assert.False(scheduler.IsHeld);
    }

    [Fact]
    public void ASecondScheduleWhileAPostIsOutstanding_DoesNotPostAgain_AndTheTurnRunsTheNewestRefreshOnce()
    {
        FakeHost host = new();
        CoalescingDetailScheduler scheduler = new(host.Post);
        List<int> ran = new();

        scheduler.Schedule(() => ran.Add(1));
        Assert.True(scheduler.IsPending);
        Assert.True(scheduler.IsPosted);
        Assert.Equal(1, host.Posts);

        scheduler.Schedule(() => ran.Add(2));
        scheduler.Schedule(() => ran.Add(3));
        Assert.Equal(1, host.Posts);
        Assert.Single(host.Posted);
        Assert.Empty(ran);

        Assert.Equal(1, host.Serve());
        Assert.Equal(new[] { 3 }, ran);
        Assert.False(scheduler.IsPending);
        Assert.False(scheduler.IsPosted);
        Assert.Equal(0, host.Withdrawals);

        // The turn came and went: the next selection posts anew (the outstanding post was cleared before the refresh ran).
        scheduler.Schedule(() => ran.Add(4));
        Assert.Equal(2, host.Posts);
        Assert.Equal(1, host.Serve());
        Assert.Equal(new[] { 3, 4 }, ran);
    }

    [Fact]
    public void ARefreshThatSchedulesAnotherRefresh_GetsANewPost()
    {
        // The outstanding post is cleared before the refresh runs, so a refresh scheduled from
        // inside a refresh is not swallowed by the post that is being served.
        FakeHost host = new();
        CoalescingDetailScheduler scheduler = new(host.Post);
        List<int> ran = new();
        scheduler.Schedule(() =>
        {
            ran.Add(1);
            scheduler.Schedule(() => ran.Add(2));
        });

        Assert.Equal(1, host.Serve());
        Assert.Equal(new[] { 1 }, ran);
        Assert.True(scheduler.IsPending);
        Assert.True(scheduler.IsPosted);
        Assert.Equal(2, host.Posts);

        Assert.Equal(1, host.Serve());
        Assert.Equal(new[] { 1, 2 }, ran);
        Assert.False(scheduler.IsPending);
    }

    [Fact]
    public void Flush_WithdrawsThePost_RunsThePendingRefreshOnce_AndLeavesNothingPending()
    {
        FakeHost host = new();
        CoalescingDetailScheduler scheduler = new(host.Post);
        List<int> ran = new();
        scheduler.Schedule(() => ran.Add(1));
        scheduler.Schedule(() => ran.Add(2));

        scheduler.Flush();

        Assert.Equal(new[] { 2 }, ran);
        Assert.Equal(1, host.Withdrawals);
        Assert.Empty(host.Posted);
        Assert.False(scheduler.IsPending);
        Assert.False(scheduler.IsPosted);

        // The withdrawn turn never comes; a later turn finds nothing; a flush with nothing pending does nothing.
        Assert.Equal(0, host.Serve());
        scheduler.Flush();
        Assert.Equal(new[] { 2 }, ran);
        Assert.Equal(1, host.Withdrawals);
    }

    [Fact]
    public void WhileHeld_NothingIsPosted_AndTheReleasePostsTheNewestRefresh()
    {
        // A key goes down in the list, repeats, and comes up: one post, after the release, one rebuild.
        FakeHost host = new();
        CoalescingDetailScheduler scheduler = new(host.Post);
        List<int> ran = new();

        scheduler.Hold();
        Assert.True(scheduler.IsHeld);
        scheduler.Hold();
        scheduler.Schedule(() => ran.Add(1));
        scheduler.Schedule(() => ran.Add(2));
        scheduler.Schedule(() => ran.Add(3));
        Assert.True(scheduler.IsPending);
        Assert.False(scheduler.IsPosted);
        Assert.Equal(0, host.Posts);
        Assert.Equal(0, host.Serve());
        Assert.Empty(ran);

        scheduler.Release();

        Assert.False(scheduler.IsHeld);
        Assert.True(scheduler.IsPosted);
        Assert.Equal(1, host.Posts);
        Assert.Empty(ran);
        Assert.Equal(1, host.Serve());
        Assert.Equal(new[] { 3 }, ran);
        Assert.False(scheduler.IsPending);
    }

    [Fact]
    public void Hold_WithdrawsAnOutstandingPost_AndTheRefreshWaitsForTheRelease()
    {
        // A click posted a refresh; a key goes down before the dispatcher's turn: the post is
        // withdrawn, the refresh stays pending, and the key up posts it again.
        FakeHost host = new();
        CoalescingDetailScheduler scheduler = new(host.Post);
        List<int> ran = new();
        scheduler.Schedule(() => ran.Add(1));
        Assert.True(scheduler.IsPosted);

        scheduler.Hold();

        Assert.Equal(1, host.Withdrawals);
        Assert.Empty(host.Posted);
        Assert.True(scheduler.IsPending);
        Assert.False(scheduler.IsPosted);
        scheduler.Schedule(() => ran.Add(2));
        Assert.Equal(1, host.Posts);

        scheduler.Release();
        Assert.Equal(2, host.Posts);
        Assert.Equal(1, host.Serve());
        Assert.Equal(new[] { 2 }, ran);
    }

    [Fact]
    public void Flush_RunsThePendingRefreshEvenWhileHeld_AndTheReleaseThenPostsNothing()
    {
        // Enter, F6, Copy value and Export flush while the key that caused them is still down.
        FakeHost host = new();
        CoalescingDetailScheduler scheduler = new(host.Post);
        List<int> ran = new();
        scheduler.Hold();
        scheduler.Schedule(() => ran.Add(1));

        scheduler.Flush();

        Assert.Equal(new[] { 1 }, ran);
        Assert.True(scheduler.IsHeld);
        Assert.False(scheduler.IsPending);
        Assert.Equal(0, host.Posts);

        scheduler.Release();
        Assert.False(scheduler.IsHeld);
        Assert.Equal(0, host.Posts);
        Assert.Equal(0, host.Serve());
    }

    [Fact]
    public void Release_WithoutAHoldOrWithNothingPending_PostsNothing()
    {
        FakeHost host = new();
        CoalescingDetailScheduler scheduler = new(host.Post);

        scheduler.Release();
        Assert.Equal(0, host.Posts);

        scheduler.Hold();
        scheduler.Release();
        Assert.Equal(0, host.Posts);
        Assert.False(scheduler.IsHeld);
        Assert.False(scheduler.IsPending);
    }

    [Fact]
    public void APostServedDespiteTheHold_KeepsTheRefreshPendingUntilTheRelease()
    {
        // A host whose withdrawal does not take effect: the served turn runs nothing while held.
        List<Action> posted = new();
        CoalescingDetailScheduler scheduler = new(work =>
        {
            posted.Add(work);
            return () => { };
        });
        List<int> ran = new();
        scheduler.Schedule(() => ran.Add(1));
        scheduler.Hold();
        Assert.Single(posted);

        posted[0]();

        Assert.Empty(ran);
        Assert.True(scheduler.IsPending);
        Assert.False(scheduler.IsPosted);

        scheduler.Release();
        Assert.Equal(2, posted.Count);
        posted[1]();
        Assert.Equal(new[] { 1 }, ran);
    }

    [Fact]
    public void ARefreshThatThrows_IsLogged_AndTheSchedulerStaysUsable()
    {
        FakeHost host = new();
        List<LogEvent> events = new();
        ILogger logger = new LoggerConfiguration().WriteTo.Sink(new ListSink(events)).CreateLogger();
        CoalescingDetailScheduler scheduler = new(host.Post, logger);
        List<int> ran = new();
        scheduler.Schedule(() => throw new InvalidOperationException("boom"));

        Assert.Equal(1, host.Serve());

        LogEvent error = Assert.Single(events);
        Assert.Equal(LogEventLevel.Error, error.Level);
        Assert.Contains("Refreshing the detail pane failed", error.MessageTemplate.Text, StringComparison.Ordinal);
        Assert.IsType<InvalidOperationException>(error.Exception);
        Assert.False(scheduler.IsPending);
        Assert.False(scheduler.IsPosted);

        scheduler.Schedule(() => ran.Add(1));
        scheduler.Flush();
        Assert.Equal(new[] { 1 }, ran);
        Assert.Single(events);
    }

    [Fact]
    public void APostThatCannotBeQueued_RunsTheRefreshNow_AndIsLoggedAsAWarning()
    {
        FakeHost host = new() { ThrowOnPost = true };
        List<LogEvent> events = new();
        ILogger logger = new LoggerConfiguration().WriteTo.Sink(new ListSink(events)).CreateLogger();
        CoalescingDetailScheduler scheduler = new(host.Post, logger);
        List<int> ran = new();

        scheduler.Schedule(() => ran.Add(1));

        Assert.Equal(new[] { 1 }, ran);
        Assert.False(scheduler.IsPending);
        Assert.False(scheduler.IsPosted);
        LogEvent warning = Assert.Single(events);
        Assert.Equal(LogEventLevel.Warning, warning.Level);
        Assert.Contains("Queuing the detail refresh failed", warning.MessageTemplate.Text, StringComparison.Ordinal);

        // The next one is tried again, not given up on.
        scheduler.Schedule(() => ran.Add(2));
        Assert.Equal(new[] { 1, 2 }, ran);
        Assert.Equal(2, host.Posts);
    }

    [Fact]
    public void AWithdrawalThatThrows_IsLogged_AndTheFlushStillRuns()
    {
        List<LogEvent> events = new();
        ILogger logger = new LoggerConfiguration().WriteTo.Sink(new ListSink(events)).CreateLogger();
        CoalescingDetailScheduler scheduler = new(_ => () => throw new InvalidOperationException("already running"), logger);
        List<int> ran = new();
        scheduler.Schedule(() => ran.Add(1));

        scheduler.Flush();

        Assert.Equal(new[] { 1 }, ran);
        Assert.False(scheduler.IsPosted);
        LogEvent warning = Assert.Single(events);
        Assert.Equal(LogEventLevel.Warning, warning.Level);
        Assert.Contains("Withdrawing the queued detail refresh failed", warning.MessageTemplate.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRecordingScheduler_RunsThisPolicy()
    {
        // The shell tests assert on RecordingDetailScheduler; this pins that it is the same policy
        // (post once, newest wins, flush withdraws) rather than a second implementation.
        RecordingDetailScheduler scheduler = new();
        List<int> ran = new();
        Assert.Same(scheduler.Policy, scheduler.Policy);

        scheduler.Schedule(() => ran.Add(1));
        scheduler.Schedule(() => ran.Add(2));
        Assert.True(scheduler.IsPosted);
        Assert.True(scheduler.Policy.IsPosted);
        Assert.True(scheduler.RunPending());
        Assert.Equal(new[] { 2 }, ran);
        Assert.False(scheduler.IsPosted);

        scheduler.Policy.Hold();
        scheduler.Schedule(() => ran.Add(3));
        Assert.False(scheduler.IsPosted);
        Assert.False(scheduler.RunPending());
        Assert.True(scheduler.IsPending);
        scheduler.Policy.Release();
        Assert.True(scheduler.IsPosted);
        Assert.True(scheduler.RunPending());
        Assert.Equal(new[] { 2, 3 }, ran);
        Assert.Equal(2, scheduler.RefreshesRun);

        scheduler.Schedule(() => ran.Add(4));
        scheduler.Flush();
        Assert.False(scheduler.IsPosted);
        Assert.False(scheduler.RunPending());
        Assert.Equal(new[] { 2, 3, 4 }, ran);
        Assert.Equal(3, scheduler.RefreshesRun);
        Assert.Equal(1, scheduler.FlushCalls);
        Assert.Equal(4, scheduler.ScheduleCalls);

        scheduler.Schedule(() => ran.Add(5));
        CoalescingDetailScheduler before = scheduler.Policy;
        scheduler.Clear();
        Assert.NotSame(before, scheduler.Policy);
        Assert.False(scheduler.IsPending);
        Assert.False(scheduler.IsPosted);
        Assert.False(scheduler.RunPending());
        Assert.Equal(new[] { 2, 3, 4 }, ran);
    }

    /// <summary>Collects Serilog events in memory so a test can assert on what the scheduler logged.</summary>
    private sealed class ListSink : ILogEventSink
    {
        private readonly List<LogEvent> _events;

        public ListSink(List<LogEvent> events) => _events = events;

        public void Emit(LogEvent logEvent) => _events.Add(logEvent);
    }
}
