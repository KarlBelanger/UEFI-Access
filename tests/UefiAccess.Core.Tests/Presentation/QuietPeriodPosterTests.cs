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
/// Alpha 2 finding A2-7 (the tester's A2-5, "A1-6 is not fixed. It likely needs a delay, say 100ms, before
/// populating the details pane when moving through the list"): the posting the WPF detail scheduler
/// runs (<see cref="QuietPeriodPoster"/>, over the scheduler's one timer and the dispatcher's
/// Background priority), exercised on this host with a clock of the test's own, so nothing here
/// waits: a post starts the timer again from zero, the timer's tick stops it and queues the work
/// for the idle turn, a withdrawal stops the timer and withdraws the queued work. Over the real
/// <see cref="CoalescingDetailScheduler"/> policy (the hold on a key or button going down in the
/// list, the post on its release, the flush that runs at once) separate quick presses cost one
/// rebuild of the pane, a quiet period after the last key came up, where they cost one after each
/// key before; Enter and the other ways into the pane never wait. The pace of those presses is
/// the developer's estimate, four to six a second (150 to 250 ms from one key down to the next):
/// neither of the tester's reports gives a pace, and the answer to round 2 asks the tester for it. The quiet
/// period is counted from each key-up, so the rebuild stays out of the gap between two presses
/// only while the time from a key-up to the next key-down is shorter than the quiet period, about
/// a third of a second; at about 2 to 2.5 presses a second the rebuild can run just before the
/// next key goes down, which a test below pins as the limit of the fix.
/// </summary>
public sealed class QuietPeriodPosterTests
{
    private const double QuietMilliseconds = 300;

    [Fact]
    public void TheQuietPeriodIs300Milliseconds_AndIsTheOneTheWpfSchedulerUses()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(QuietMilliseconds), DetailRefreshTiming.QuietPeriod);

        // The developer's estimate of quick separate presses, 150 to 250 ms from one key down to
        // the next (the tester gave no pace): the quiet period, counted from the key-up, outlasts the
        // longest such gap.
        Assert.True(DetailRefreshTiming.QuietPeriod > TimeSpan.FromMilliseconds(250));

        string scheduler = MainWindowMarkup.Source("Platform", "DispatcherDetailScheduler.cs");
        Assert.Contains("{ Interval = DetailRefreshTiming.QuietPeriod }", scheduler, StringComparison.Ordinal);
        Assert.Contains("new QuietPeriodPoster(RestartQuietPeriod, _quietPeriod.Stop, QueueAtIdle, logger)", scheduler, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_RejectsNullDelegates_AndPostRejectsNullWork()
    {
        Assert.Throws<ArgumentNullException>(() => new QuietPeriodPoster(null!, () => { }, _ => () => { }));
        Assert.Throws<ArgumentNullException>(() => new QuietPeriodPoster(() => { }, null!, _ => () => { }));
        Assert.Throws<ArgumentNullException>(() => new QuietPeriodPoster(() => { }, () => { }, null!));
        FakeHost host = new();
        Assert.Throws<ArgumentNullException>(() => host.Poster.Post(null!));
        Assert.False(host.Poster.IsWaiting);
        Assert.False(host.Poster.IsQueued);
    }

    [Fact]
    public void APost_RunsNothingUntilTheQuietPeriodHasPassed_ThenQueuesTheWorkForTheIdleTurn()
    {
        FakeHost host = new();
        List<int> ran = new();

        host.Poster.Post(() => ran.Add(1));
        Assert.True(host.Poster.IsWaiting);
        host.Advance(QuietMilliseconds - 1);
        Assert.True(host.Poster.IsWaiting);
        Assert.Empty(host.Queued);
        Assert.Equal(0, host.ServeIdle());

        host.Advance(1);

        Assert.False(host.Poster.IsWaiting);
        Assert.True(host.Poster.IsQueued);
        Assert.Null(host.Due);
        Assert.Empty(ran);
        Assert.Equal(1, host.ServeIdle());
        Assert.Equal(new[] { 1 }, ran);
        Assert.False(host.Poster.IsQueued);

        // The timer stopped itself in its tick, before the work was queued, and never ticks again.
        Assert.Equal(new[] { "restart", "stop", "queue" }, host.Calls);
        host.Advance(10 * QuietMilliseconds);
        Assert.Equal(new[] { "restart", "stop", "queue" }, host.Calls);
        Assert.Equal(new[] { 1 }, ran);
    }

    [Fact]
    public void EveryPost_StartsTheQuietPeriodAgainFromZero_AndReplacesAPostStillWaiting()
    {
        FakeHost host = new();
        List<int> ran = new();
        Action withdrawFirst = host.Poster.Post(() => ran.Add(1));
        host.Advance(200);

        host.Poster.Post(() => ran.Add(2));
        host.Advance(QuietMilliseconds - 1);
        Assert.Empty(host.Queued);

        // The replaced post's own withdrawal no longer touches the timer of the post that replaced it.
        withdrawFirst();
        Assert.NotNull(host.Due);

        host.Advance(1);
        Assert.Equal(1, host.ServeIdle());
        Assert.Equal(new[] { 2 }, ran);
    }

    [Fact]
    public void AWithdrawal_StopsTheTimer_OrWithdrawsTheWorkAlreadyQueued_AndNothingRuns()
    {
        FakeHost host = new();
        List<int> ran = new();

        Action withdraw = host.Poster.Post(() => ran.Add(1));
        host.Advance(100);
        withdraw();
        Assert.Null(host.Due);
        Assert.False(host.Poster.IsWaiting);
        host.Advance(10 * QuietMilliseconds);
        Assert.Empty(host.Queued);

        withdraw = host.Poster.Post(() => ran.Add(2));
        host.Advance(QuietMilliseconds);
        Assert.True(host.Poster.IsQueued);
        withdraw();
        Assert.Equal(1, host.Withdrawals);
        Assert.Empty(host.Queued);
        Assert.Equal(0, host.ServeIdle());
        Assert.Empty(ran);

        // A second call of the same withdrawal does nothing more.
        withdraw();
        Assert.Equal(1, host.Withdrawals);
    }

    [Fact]
    public void QueuedWorkThatRunsDespiteItsWithdrawal_FindsNothingToDo()
    {
        // A host whose withdrawal of queued work does not take effect: the served work runs nothing.
        FakeHost host = new() { WithdrawalsTakeEffect = false };
        List<int> ran = new();
        Action withdraw = host.Poster.Post(() => ran.Add(1));
        host.Advance(QuietMilliseconds);
        withdraw();

        Assert.Equal(1, host.ServeIdle());
        Assert.Empty(ran);
    }

    [Fact]
    public void WorkThatCannotBeQueued_RunsAtTheTick_AndIsLoggedAsAWarning()
    {
        List<LogEvent> events = new();
        ILogger logger = new LoggerConfiguration().WriteTo.Sink(new ListSink(events)).CreateLogger();
        FakeHost host = new(logger) { QueueThrows = true };
        List<int> ran = new();
        host.Poster.Post(() => ran.Add(1));

        host.Advance(QuietMilliseconds);

        Assert.Equal(new[] { 1 }, ran);
        Assert.False(host.Poster.IsWaiting);
        Assert.False(host.Poster.IsQueued);
        LogEvent warning = Assert.Single(events);
        Assert.Equal(LogEventLevel.Warning, warning.Level);
        Assert.Contains("Queuing the detail refresh after the quiet period failed", warning.MessageTemplate.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ATimerThatCannotStart_LeavesNothingWaiting_AndThePolicyRunsTheRefreshNow()
    {
        FakeHost host = new() { RestartThrows = true };
        CoalescingDetailScheduler policy = new(host.Poster.Post);
        List<int> ran = new();

        policy.Schedule(() => ran.Add(1));

        Assert.Equal(new[] { 1 }, ran);
        Assert.False(host.Poster.IsWaiting);
        Assert.False(policy.IsPosted);
    }

    /// <summary>
    /// Separate quick arrow presses at the pace the developer estimated for the tester's round-2 steps
    /// (the tester gave none): 150 to 250 ms from one key down to the next, each key down some 80 ms. The
    /// selection changes on every key down (the row is selected and its name is spoken at once);
    /// the pane is rebuilt once, 300 ms after the last key came up,
    /// and never between two presses of the burst, where alpha1.2 rebuilt it after every key.
    /// </summary>
    [Fact]
    public void SeparateQuickPresses_150To250MillisecondsApart_CostOneRefresh_AQuietPeriodAfterTheLastKeyCameUp()
    {
        FakeHost host = new();
        CoalescingDetailScheduler policy = new(host.Poster.Post);
        List<(int Row, double At)> shown = new();
        double[] downToDown = { 150, 250, 180, 220, 150, 200, 250 };

        double lastUp = 0;
        for (int row = 1; row <= downToDown.Length + 1; row++)
        {
            int current = row;
            policy.Hold();
            policy.Schedule(() => shown.Add((current, host.NowMilliseconds)));
            host.AdvanceAndServe(80);
            policy.Release();
            lastUp = host.NowMilliseconds;
            if (row <= downToDown.Length)
            {
                host.AdvanceAndServe(downToDown[row - 1] - 80);
                Assert.Empty(shown);
            }
        }

        host.AdvanceAndServe(QuietMilliseconds - 1);
        Assert.Empty(shown);
        host.AdvanceAndServe(1);

        (int shownRow, double shownAt) = Assert.Single(shown);
        Assert.Equal(downToDown.Length + 1, shownRow);
        Assert.Equal(lastUp + QuietMilliseconds, shownAt);
        Assert.False(policy.IsPending);
    }

    [Fact]
    public void AHeldRepeatingKey_CostsOneRefresh_AQuietPeriodAfterItIsReleased()
    {
        FakeHost host = new();
        CoalescingDetailScheduler policy = new(host.Poster.Post);
        List<int> shown = new();

        policy.Hold();
        for (int row = 1; row <= 20; row++)
        {
            int current = row;
            policy.Hold();
            policy.Schedule(() => shown.Add(current));
            host.AdvanceAndServe(33);
        }

        Assert.Empty(shown);
        Assert.Equal(0, host.Restarts);
        policy.Release();
        host.AdvanceAndServe(QuietMilliseconds);

        Assert.Equal(new[] { 20 }, shown);
        Assert.Equal(1, host.Restarts);
    }

    [Fact]
    public void Enter_RunsThePendingRefreshAtOnce_AndNothingWaitsAfterIt()
    {
        FakeHost host = new();
        CoalescingDetailScheduler policy = new(host.Poster.Post);
        List<int> shown = new();

        // A press that selected a row, released, and Enter pressed within the quiet period.
        policy.Hold();
        policy.Schedule(() => shown.Add(1));
        policy.Release();
        host.AdvanceAndServe(100);
        policy.Hold();
        policy.Flush();

        Assert.Equal(new[] { 1 }, shown);
        Assert.Null(host.Due);
        policy.Release();
        host.AdvanceAndServe(10 * QuietMilliseconds);
        Assert.Equal(new[] { 1 }, shown);
        Assert.Equal(1, host.Restarts);
    }

    [Fact]
    public void AKeyPressAfterTheQuietPeriodButBeforeTheIdleTurn_WithdrawsTheQueuedRefresh_AndStartsAnew()
    {
        FakeHost host = new();
        CoalescingDetailScheduler policy = new(host.Poster.Post);
        List<int> shown = new();
        policy.Schedule(() => shown.Add(1));
        host.Advance(QuietMilliseconds);
        Assert.True(host.Poster.IsQueued);

        // Input is waiting, so the Background operation was not served yet; the key goes down.
        policy.Hold();
        Assert.Equal(1, host.Withdrawals);
        policy.Schedule(() => shown.Add(2));
        policy.Release();
        host.AdvanceAndServe(QuietMilliseconds - 1);
        Assert.Empty(shown);
        host.AdvanceAndServe(1);

        Assert.Equal(new[] { 2 }, shown);
    }

    [Fact]
    public void ASelectionChangeThatIsNotHeld_PostsThroughTheSamePath()
    {
        // A filter that hides the selected row changes the selection with no key down in the list.
        FakeHost host = new();
        CoalescingDetailScheduler policy = new(host.Poster.Post);
        List<int> shown = new();

        policy.Schedule(() => shown.Add(1));
        Assert.Equal(1, host.Restarts);
        host.AdvanceAndServe(QuietMilliseconds - 1);
        Assert.Empty(shown);
        host.AdvanceAndServe(1);

        Assert.Equal(new[] { 1 }, shown);
    }

    /// <summary>
    /// The limit of the fix: the quiet period is counted from each key-up, so it keeps the rebuild
    /// out of the gap between two presses only while the time from a key-up to the next key-down
    /// is shorter than the quiet period. Presses 400 to 500 ms apart from key down to key down
    /// (2 to 2.5 a second), each key down some 80 ms, leave more than 300 ms after each key-up:
    /// the pane is rebuilt for every row, 300 ms after its key came up and shortly before the next
    /// key goes down. A rebuild still running when that key arrives delays the next row's sentence
    /// as every rebuild did in alpha1.2.
    /// </summary>
    [Fact]
    public void PressesMoreThanAQuietPeriodApartFromKeyUpToKeyDown_GetARebuildForEveryRow_TheLimitOfTheFix()
    {
        FakeHost host = new();
        CoalescingDetailScheduler policy = new(host.Poster.Post);
        List<(int Row, double At)> shown = new();
        double[] downToDown = { 400, 440, 500 };
        const double KeyDown = 80;

        List<(double Up, double NextDown)> gaps = new();
        for (int row = 1; row <= downToDown.Length; row++)
        {
            int current = row;
            double down = host.NowMilliseconds;
            policy.Hold();
            policy.Schedule(() => shown.Add((current, host.NowMilliseconds)));
            host.AdvanceAndServe(KeyDown);
            policy.Release();
            double up = host.NowMilliseconds;
            host.AdvanceAndServe(downToDown[row - 1] - KeyDown);
            gaps.Add((up, down + downToDown[row - 1]));
        }

        Assert.Equal(new[] { 1, 2, 3 }, shown.Select(entry => entry.Row));
        for (int index = 0; index < shown.Count; index++)
        {
            Assert.Equal(gaps[index].Up + QuietMilliseconds, shown[index].At);
            Assert.True(shown[index].At < gaps[index].NextDown, "row " + shown[index].Row + " was rebuilt after the next key went down");
        }
    }

    /// <summary>
    /// A host with a clock of the test's own: the one timer is a due time on that clock, its tick
    /// comes when <see cref="Advance"/> reaches it, and the idle turn serves what was queued when the
    /// test says so (<see cref="ServeIdle"/>), or at once after each step (<see cref="AdvanceAndServe"/>).
    /// </summary>
    private sealed class FakeHost
    {
        public FakeHost(ILogger? logger = null)
        {
            Poster = new QuietPeriodPoster(Restart, Stop, Queue, logger);
        }

        public QuietPeriodPoster Poster { get; }

        public double NowMilliseconds { get; private set; }

        public double? Due { get; private set; }

        public List<Action> Queued { get; } = new();

        public List<string> Calls { get; } = new();

        public int Restarts { get; private set; }

        public int Withdrawals { get; private set; }

        public bool RestartThrows { get; init; }

        public bool QueueThrows { get; init; }

        public bool WithdrawalsTakeEffect { get; init; } = true;

        /// <summary>Moves the clock on; the timer ticks once when its due time falls in the step.</summary>
        public void Advance(double milliseconds)
        {
            double target = NowMilliseconds + milliseconds;
            if (Due is double due && due <= target)
            {
                NowMilliseconds = due;
                Poster.OnQuietPeriodElapsed();
            }

            NowMilliseconds = target;
        }

        /// <summary>A step with no input waiting: the idle turn serves whatever the tick queued.</summary>
        public void AdvanceAndServe(double milliseconds)
        {
            double target = NowMilliseconds + milliseconds;
            if (Due is double due && due <= target)
            {
                NowMilliseconds = due;
                Poster.OnQuietPeriodElapsed();
                ServeIdle();
            }

            NowMilliseconds = target;
        }

        /// <summary>The dispatcher's idle turn: runs every queued piece of work.</summary>
        public int ServeIdle()
        {
            Action[] work = Queued.ToArray();
            Queued.Clear();
            foreach (Action action in work)
            {
                action();
            }

            return work.Length;
        }

        private void Restart()
        {
            if (RestartThrows)
            {
                throw new InvalidOperationException("the dispatcher has shut down");
            }

            Calls.Add("restart");
            Restarts++;
            Due = NowMilliseconds + QuietMilliseconds;
        }

        private void Stop()
        {
            Calls.Add("stop");
            Due = null;
        }

        private Action Queue(Action work)
        {
            if (QueueThrows)
            {
                throw new InvalidOperationException("the dispatcher has shut down");
            }

            Calls.Add("queue");
            Queued.Add(work);
            return () =>
            {
                Withdrawals++;
                if (WithdrawalsTakeEffect)
                {
                    Queued.Remove(work);
                }
            };
        }
    }

    /// <summary>Collects Serilog events in memory so a test can assert on what the poster logged.</summary>
    private sealed class ListSink : ILogEventSink
    {
        private readonly List<LogEvent> _events;

        public ListSink(List<LogEvent> events) => _events = events;

        public void Emit(LogEvent logEvent) => _events.Add(logEvent);
    }
}
