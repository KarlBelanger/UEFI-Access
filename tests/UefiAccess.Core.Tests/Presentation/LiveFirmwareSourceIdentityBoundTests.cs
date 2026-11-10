// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Serilog;
using Serilog.Events;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Privileges;
using UefiAccess.Core.Variables;
using UefiAccess.Presentation.Accessibility;
using UefiAccess.Presentation.Resources;
using UefiAccess.Presentation.Services;
using UefiAccess.Presentation.ViewModels;
using Xunit;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// Finding F066: a slow or hung WMI provider must not keep the live load in Loading for minutes.
/// The machine identity is read on its own thread while the variables are enumerated, and the load
/// waits for it at most <see cref="LiveFirmwareSource.IdentityTimeout"/> (60 seconds, Core's WMI
/// bound); past that the variables load anyway, the identity strip says "(unknown)" and one
/// warning says why. A cancelled load stops waiting at once. The refusals that come from a
/// throwing check carry that check's exception as a warning, and the source itself logs only at
/// Debug (findings F086 and F171: its findings reach the panel through the result, once). No
/// assertion depends on how soon a busy thread pool starts the identity read: a test that needs
/// the read to have started waits for it to start.
/// </summary>
public sealed class LiveFirmwareSourceIdentityBoundTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(30);
    private static readonly FirmwareVariable BootOrder = ShellTestRig.BootOrder;

    [Fact]
    public void TheDefaultBound_IsCoresWmiBoundOfSixtySeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), LiveFirmwareSource.IdentityTimeout);
    }

    [Fact]
    public async Task AHungIdentity_CostsTheStripItsValues_NotTheList()
    {
        // The enumeration returns only once the identity read has started, so the bounded wait that
        // follows it can end only because the identity never finishes, however long a busy thread
        // pool took to start the read (the 200 ms may be used up before it starts; the wait then
        // times out at once, as it should).
        using BlockingIdentity identity = new();
        Store store = new() { OnEnumerate = () => identity.Entered.Wait(Guard) };
        LiveFirmwareSource source = new(new Detector(), new Privileges(), store, identity, null, TimeSpan.FromMilliseconds(200));

        SourceResult result = await Within(source.LoadAsync(CancellationToken.None));

        Assert.Equal(SourceStatus.Loaded, result.Status);
        Assert.Equal(new[] { BootOrder }, result.Variables);
        Assert.Null(result.Identity);
        Assert.Equal(
            new[] { "machine identity: the WMI queries did not answer within 0.2 seconds, so the identity fields show (unknown)" },
            result.Warnings);
        Assert.True(identity.Entered.IsSet, "the identity read never started, so the load did not wait for it");
        Assert.Equal(1, identity.Calls);
    }

    [Fact]
    public async Task AHungIdentity_ThroughTheShell_LoadsTheList_AndShowsUnknownInTheStrip()
    {
        using BlockingIdentity identity = new();
        LiveFirmwareSource source = new(new Detector(), new Privileges(), new Store(), identity, null, TimeSpan.FromMilliseconds(100));
        ShellTestRig rig = new(source);

        await Within(rig.Shell.RefreshAsync());

        Assert.Equal(ShellState.Loaded, rig.Shell.State);
        Assert.False(rig.Shell.IsBusy);
        Assert.Equal("Loaded 1 variable from Live firmware (NtEnumerate)", rig.Shell.StatusText);
        Assert.Equal(new[] { FocusTarget.ListFirstRow }, rig.Focus.Requests);
        Assert.Equal("Model: " + Strings.Unknown, rig.Shell.Model);
        Assert.Equal("Expected model confirmed: " + Strings.Unknown, rig.Shell.ExpectedModelText);
        Assert.Contains(rig.Lines, line => line.Level == LogViewModel.WarnLevel && line.Message.StartsWith("machine identity: the WMI queries did not answer within 0.1 seconds", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheIdentity_IsReadWhileTheVariablesAreEnumerated()
    {
        // A meeting point: the identity read does not finish until the enumeration has started, and
        // the enumeration does not finish until the identity read has started. Read one after the
        // other, in either order, the first of the two gives up waiting for the other and says so.
        using Meeting meeting = new();
        Store store = new() { OnEnumerate = meeting.EnumerationArrives };
        FixedIdentity identity = new(LiveFirmwareSourceTests.Identity()) { OnInspect = meeting.IdentityArrives };
        LiveFirmwareSource source = new(new Detector(), new Privileges(), store, identity);

        SourceResult result = await Within(source.LoadAsync(CancellationToken.None));

        Assert.True(meeting.IdentitySawTheEnumeration, "the identity read ended before the enumeration started: the identity was read first");
        Assert.True(meeting.EnumerationSawTheIdentity, "the enumeration ended before the identity read started: the variables were enumerated first");
        Assert.NotNull(result.Identity);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void TheMeetingPoint_CatchesEitherOrderOneAfterTheOther()
    {
        // What the test above relies on, shown without the source: whichever side arrives alone
        // waits for the other in vain and records it. A short patience keeps this test quick.
        using Meeting enumerationFirst = new(TimeSpan.FromMilliseconds(50));
        enumerationFirst.EnumerationArrives();
        enumerationFirst.IdentityArrives();

        using Meeting identityFirst = new(TimeSpan.FromMilliseconds(50));
        identityFirst.IdentityArrives();
        identityFirst.EnumerationArrives();

        Assert.False(enumerationFirst.EnumerationSawTheIdentity);
        Assert.True(enumerationFirst.IdentitySawTheEnumeration);
        Assert.False(identityFirst.IdentitySawTheEnumeration);
        Assert.True(identityFirst.EnumerationSawTheIdentity);
    }

    [Fact]
    public async Task ACancelledLoad_StopsWaitingForTheIdentity()
    {
        using BlockingIdentity identity = new();
        using CancellationTokenSource cancellation = new();
        Store store = new() { OnEnumerate = cancellation.Cancel };
        LiveFirmwareSource source = new(new Detector(), new Privileges(), store, identity);

        SourceResult result = await Within(source.LoadAsync(cancellation.Token));

        Assert.Equal(SourceStatus.Failed, result.Status);
        Assert.Equal(LiveFirmwareSource.CancelledMessage, result.Message);
    }

    [Fact]
    public async Task AnIdentityThatFinishesInTime_IsUsed_WithItsErrorsAsWarnings()
    {
        MachineIdentityResult withErrors = LiveFirmwareSourceTests.Identity() with { Errors = new[] { "Win32_BIOS: ManagementException Timedout" } };
        LiveFirmwareSource source = new(new Detector(), new Privileges(), new Store(), new FixedIdentity(withErrors), null, TimeSpan.FromSeconds(20));

        SourceResult result = await Within(source.LoadAsync(CancellationToken.None));

        Assert.Same(withErrors, result.Identity);
        Assert.Equal(new[] { "machine identity: Win32_BIOS: ManagementException Timedout" }, result.Warnings);
    }

    [Fact]
    public async Task ADetectionOrElevationCheckThatThrows_IsRefusedWithTheExceptionAsAWarning()
    {
        SourceResult notUefi = await new LiveFirmwareSource(new Detector { Throws = true }, new Privileges(), new Store(), new FixedIdentity(LiveFirmwareSourceTests.Identity())).LoadAsync(CancellationToken.None);
        SourceResult notElevated = await new LiveFirmwareSource(new Detector(), new Privileges { Throws = true }, new Store(), new FixedIdentity(LiveFirmwareSourceTests.Identity())).LoadAsync(CancellationToken.None);

        Assert.Equal(Strings.NotUefiSentence, notUefi.Message);
        Assert.Equal(new[] { "firmware type detection: InvalidOperationException: detector exploded" }, notUefi.Warnings);
        Assert.Equal(Strings.NotElevatedSentence, notElevated.Message);
        Assert.Equal(new[] { "elevation check: InvalidOperationException: token exploded" }, notElevated.Warnings);
    }

    [Fact]
    public async Task TheSource_LogsOnlyAtDebug_OnEveryPath()
    {
        List<LogEvent> events = new();
        ILogger logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(new ListSink(events)).CreateLogger();
        IPlatformInspector<MachineIdentityResult> identity = new FixedIdentity(LiveFirmwareSourceTests.Identity());

        await new LiveFirmwareSource(new Detector { Throws = true }, new Privileges(), new Store(), identity, logger).LoadAsync(CancellationToken.None);
        await new LiveFirmwareSource(new Detector(), new Privileges { Throws = true }, new Store(), identity, logger).LoadAsync(CancellationToken.None);
        await new LiveFirmwareSource(new Detector(), new Privileges { EnableThrows = true }, new Store(), identity, logger).LoadAsync(CancellationToken.None);
        await new LiveFirmwareSource(new Detector(), new Privileges(), new Store { Throws = true }, identity, logger).LoadAsync(CancellationToken.None);
        await new LiveFirmwareSource(new Detector(), new Privileges(), new Store(), new FixedIdentity(null) { Throws = true }, logger).LoadAsync(CancellationToken.None);

        Assert.NotEmpty(events);
        Assert.All(events, logEvent => Assert.Equal(LogEventLevel.Debug, logEvent.Level));
        Assert.Contains(events, logEvent => logEvent.Exception is InvalidOperationException { Message: "native seam exploded" });
    }

    private static async Task<T> Within<T>(Task<T> task)
    {
        Assert.Same(task, await Task.WhenAny(task, Task.Delay(Guard)));
        return await task;
    }

    private static async Task Within(Task task)
    {
        Assert.Same(task, await Task.WhenAny(task, Task.Delay(Guard)));
        await task;
    }

    private sealed class Detector : IFirmwareTypeDetector
    {
        public bool Throws { get; init; }

        public FirmwareType Detect() => Throws ? throw new InvalidOperationException("detector exploded") : FirmwareType.Uefi;
    }

    private sealed class Privileges : IPrivilegeManager
    {
        public bool Throws { get; init; }

        public bool EnableThrows { get; init; }

        public bool IsElevated => Throws ? throw new InvalidOperationException("token exploded") : true;

        public PrivilegeState EnableSystemEnvironmentPrivilege() => EnableThrows ? throw new InvalidOperationException("privilege exploded") : PrivilegeState.Enabled;
    }

    private sealed class Store : IFirmwareVariableStore
    {
        public bool Throws { get; init; }

        public Action? OnEnumerate { get; init; }

        public EnumerationResult Enumerate()
        {
            OnEnumerate?.Invoke();
            if (Throws)
            {
                throw new InvalidOperationException("native seam exploded");
            }

            return new EnumerationResult(new[] { BootOrder }, EnumerationMethod.NtEnumerate, 1, null, Array.Empty<string>());
        }

        public ReadResult Read(string name, Guid vendorGuid) => new(null, ReadStatus.NotFound, null, 0);
    }

    private sealed class FixedIdentity : IPlatformInspector<MachineIdentityResult>
    {
        private readonly MachineIdentityResult? _result;

        public FixedIdentity(MachineIdentityResult? result) => _result = result;

        public bool Throws { get; init; }

        /// <summary>Runs first on every read, on the thread that reads the identity.</summary>
        public Action? OnInspect { get; init; }

        public MachineIdentityResult Inspect()
        {
            OnInspect?.Invoke();
            return Throws ? throw new InvalidOperationException("inspect failed") : _result!;
        }
    }

    /// <summary>
    /// An identity whose WMI never answers until the test ends (then it is released). It counts its
    /// reads and sets <see cref="Entered"/> once a read has started (after counting it), so a test
    /// can wait for the read to start instead of guessing how long the thread pool takes.
    /// </summary>
    private sealed class BlockingIdentity : IPlatformInspector<MachineIdentityResult>, IDisposable
    {
        private readonly ManualResetEventSlim _release = new();
        private int _calls;

        public ManualResetEventSlim Entered { get; } = new();

        public int Calls => Volatile.Read(ref _calls);

        public MachineIdentityResult Inspect()
        {
            Interlocked.Increment(ref _calls);
            Entered.Set();
            _release.Wait(Guard);
            return LiveFirmwareSourceTests.Identity();
        }

        public void Dispose() => _release.Set();
    }

    /// <summary>
    /// A meeting point between the identity read and the enumeration. Each side announces that it
    /// has started, then waits for the other side (at most the patience) and records whether it
    /// came: done side by side, both see each other; done one after the other, in either order,
    /// the side that runs first waits in vain.
    /// </summary>
    private sealed class Meeting : IDisposable
    {
        private readonly ManualResetEventSlim _identityStarted = new();
        private readonly ManualResetEventSlim _enumerationStarted = new();
        private readonly TimeSpan _patience;
        private volatile bool _identitySawTheEnumeration;
        private volatile bool _enumerationSawTheIdentity;

        /// <summary>Creates the meeting point; by default each side waits half of <see cref="Guard"/>, so a side that waits in vain gives up before the test's guard ends the load.</summary>
        public Meeting(TimeSpan? patience = null) => _patience = patience ?? Guard / 2;

        public bool IdentitySawTheEnumeration => _identitySawTheEnumeration;

        public bool EnumerationSawTheIdentity => _enumerationSawTheIdentity;

        public void IdentityArrives()
        {
            _identityStarted.Set();
            _identitySawTheEnumeration = _enumerationStarted.Wait(_patience);
        }

        public void EnumerationArrives()
        {
            _enumerationStarted.Set();
            _enumerationSawTheIdentity = _identityStarted.Wait(_patience);
        }

        /// <summary>Releases a side that is still waiting; the events themselves are left to the collector, as a released wait may still be returning.</summary>
        public void Dispose()
        {
            _identityStarted.Set();
            _enumerationStarted.Set();
        }
    }

    private sealed class ListSink : Serilog.Core.ILogEventSink
    {
        private readonly List<LogEvent> _events;

        public ListSink(List<LogEvent> events) => _events = events;

        public void Emit(LogEvent logEvent)
        {
            lock (_events)
            {
                _events.Add(logEvent);
            }
        }
    }
}
