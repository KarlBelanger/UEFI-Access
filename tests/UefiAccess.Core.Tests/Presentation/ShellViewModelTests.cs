// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using UefiAccess.Core.Decoders;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Privileges;
using UefiAccess.Core.Variables;
using UefiAccess.Presentation.Accessibility;
using UefiAccess.Presentation.Diagnostics;
using UefiAccess.Presentation.Infrastructure;
using UefiAccess.Presentation.Resources;
using UefiAccess.Presentation.Services;
using UefiAccess.Presentation.ViewModels;
using Xunit;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// <see cref="ShellViewModel"/> over a fake <see cref="IVariableSource"/> for every
/// <see cref="SourceStatus"/>: the state machine, the Status text (through the recording
/// announcer), the window title and source description, the identity strings, the focus requests
/// per transition (asserted on the recorded sink, never on real focus), the log lines, the busy
/// gating of the load commands, the dialog-driven commands against <see cref="NullPlatformInteractions"/>,
/// the clipboard commands, the export file, the region and filter announcements, and the deferred
/// refresh of the detail pane (finding A1-6, asserted on a <see cref="RecordingDetailScheduler"/>,
/// the same <see cref="CoalescingDetailScheduler"/> policy the WPF scheduler runs, over a host
/// that keeps the posted work until the test runs it). The harness mirrors the shell's
/// Serilog logger into the log panel the way the application does (Information and above), so the
/// log-line assertions see the panel as the user sees it: every event once.
/// </summary>
public sealed class ShellViewModelTests : IDisposable
{
    private const VariableAttributes NvBsRt = VariableAttributes.NonVolatile | VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess;
    private const string BaselineFolder = "/baselines/optiplex-20260915";
    private const string BaselineDescription = Strings.BaselinePrefix + BaselineFolder;

    private static readonly FirmwareVariable BootOrder = new("BootOrder", KnownGuids.EfiGlobalVariable, NvBsRt, new byte[] { 0x01, 0x00, 0x00, 0x00 });
    private static readonly FirmwareVariable Timeout = new("Timeout", KnownGuids.EfiGlobalVariable, NvBsRt, new byte[] { 0x05, 0x00 });
    private static readonly FirmwareVariable Vendor = new("AcpiGlobalVariable", Guid.Parse("af9ffd67-ec10-488a-9dfc-6cbf5ee22c2e"), NvBsRt, new byte[] { 0x10, 0x20, 0x30 });
    private static readonly FirmwareVariable[] ThreeVariables = { BootOrder, Timeout, Vendor };

    private readonly string _tempRoot;

    public ShellViewModelTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "shell-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // Best effort; a leftover temp folder must not fail the test run.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort; a leftover temp folder must not fail the test run.
        }
    }

    // ---- construction ------------------------------------------------------------------------

    [Fact]
    public void Constructor_AnnouncesNothing_AndAnnounceReadyAnnouncesTheReadySentenceOnce()
    {
        // The window calls AnnounceReady from its Loaded handler, once it is on screen; before
        // that the announcer has no live region to raise its event on, so the constructor stays
        // silent (no announcement, no log line, no focus request).
        FakeSource live = new();
        RecordingAnnouncer announcer = new();
        RecordingFocusSink focus = new();
        ShellViewModel shell = new(live, _ => live, announcer, focus, new NullPlatformInteractions());

        Assert.Empty(announcer.History);
        Assert.Equal(string.Empty, announcer.Current);
        Assert.Equal(string.Empty, shell.StatusText);
        Assert.Empty(shell.Log.Lines);
        Assert.Empty(focus.Requests);
        Assert.Equal(ShellState.Idle, shell.State);

        List<string> changed = Record(shell);
        shell.AnnounceReady();

        Assert.Equal(new[] { Strings.IdleStatus }, announcer.History);
        Assert.Equal(Strings.IdleStatus, shell.StatusText);
        LogLineViewModel line = Assert.Single(shell.Log.Lines);
        Assert.Equal(LogViewModel.InfoLevel, line.Level);
        Assert.Equal(Strings.IdleStatus, line.Message);
        Assert.Equal(new[] { nameof(ShellViewModel.StatusText) }, changed);
        Assert.Empty(focus.Requests);
        Assert.Equal(ShellState.Idle, shell.State);
        Assert.Equal(0, live.LoadCalls);
    }

    [Fact]
    public void Constructor_StartsIdleWithTheReadyMessageAndNoFocusRequest()
    {
        Harness h = new();

        Assert.Equal(ShellState.Idle, h.Shell.State);
        Assert.Equal("Idle", h.Shell.StateText);
        Assert.Equal(Strings.IdleStatus, h.Shell.StatusText);
        Assert.Equal(new[] { Strings.IdleStatus }, h.Announcer.History);
        LogLineViewModel line = Assert.Single(h.Shell.Log.Lines);
        Assert.Equal(LogViewModel.InfoLevel, line.Level);
        Assert.Equal(Strings.IdleStatus, line.Message);
        Assert.Empty(h.Focus.Requests);
        Assert.False(h.Shell.IsBusy);
        Assert.False(h.Shell.IsOffline);
        Assert.Equal("Live firmware", h.Shell.SourceDescription);
        Assert.Equal("UefiAccess", h.Shell.WindowTitle);
        AssertIdentityPlaceholders(h.Shell, Strings.NotReadYet);
        Assert.Null(h.Shell.Detail.Item);
        Assert.Equal(0, h.Live.LoadCalls);
    }

    [Fact]
    public void Constructor_RejectsNullServices()
    {
        FakeSource live = new();
        Func<string, IVariableSource> factory = _ => live;
        RecordingAnnouncer announcer = new();
        RecordingFocusSink focus = new();
        NullPlatformInteractions platform = new();

        Assert.Throws<ArgumentNullException>(() => new ShellViewModel(null!, factory, announcer, focus, platform));
        Assert.Throws<ArgumentNullException>(() => new ShellViewModel(live, null!, announcer, focus, platform));
        Assert.Throws<ArgumentNullException>(() => new ShellViewModel(live, factory, null!, focus, platform));
        Assert.Throws<ArgumentNullException>(() => new ShellViewModel(live, factory, announcer, null!, platform));
        Assert.Throws<ArgumentNullException>(() => new ShellViewModel(live, factory, announcer, focus, null!));
    }

    // ---- Refresh: Loaded ---------------------------------------------------------------------

    [Fact]
    public async Task RefreshAsync_Loaded_AnnouncesTheCountAndMethod_FocusesTheFirstRow_AndShowsTheIdentity()
    {
        Harness h = new();
        h.Live.Result = Loaded(ThreeVariables);

        await h.Shell.RefreshAsync();

        Assert.Equal(ShellState.Loaded, h.Shell.State);
        Assert.Equal("Loaded", h.Shell.StateText);
        Assert.False(h.Shell.IsBusy);
        Assert.Equal("Loaded 3 variables from Live firmware (InMemory)", h.Shell.StatusText);
        Assert.Equal(new[] { Strings.IdleStatus, Strings.LoadingStatus, "Loaded 3 variables from Live firmware (InMemory)" }, h.Announcer.History);
        Assert.Equal(new[] { FocusTarget.ListFirstRow }, h.Focus.Requests);
        Assert.Equal("UefiAccess – Live firmware", h.Shell.WindowTitle);
        Assert.Equal(Strings.WindowTitle + Strings.WindowTitleSeparator + Strings.LiveSourceName, h.Shell.WindowTitle);
        Assert.Equal("Live firmware", h.Shell.SourceDescription);
        Assert.False(h.Shell.IsOffline);
        Assert.Equal(1, h.Live.LoadCalls);

        Assert.Equal("Manufacturer: Dell Inc.", h.Shell.Manufacturer);
        Assert.Equal("Model: OptiPlex 7020", h.Shell.Model);
        Assert.Equal("BIOS version: A18", h.Shell.BiosVersion);
        Assert.Equal("Service tag: ABC1234", h.Shell.ServiceTag);
        Assert.Equal("CPU: Intel(R) Core(TM) i5-4590 CPU @ 3.30GHz", h.Shell.Cpu);
        Assert.Equal("Operating system: Microsoft Windows 11 Pro 10.0.22631 (build 22631)", h.Shell.OperatingSystem);
        Assert.Equal("Expected model confirmed: Yes", h.Shell.ExpectedModelText);

        // Every announcement is mirrored into the log as an INFO line, in order; the expected model
        // reasoning is one INFO line before the Loaded line; nothing appears twice.
        Assert.Equal(
            new[]
            {
                Strings.IdleStatus,
                Strings.LoadingStatus,
                "Expected model confirmed: 2014 Dell OptiPlex 7020 (model, BIOS version and CPU generation all match).",
                "Loaded 3 variables from Live firmware (InMemory)",
            },
            h.Shell.Log.Lines.Select(line => line.Message));
        Assert.All(h.Shell.Log.Lines, line => Assert.Equal(LogViewModel.InfoLevel, line.Level));

        // The detail pane shows whatever the list selected after the load (nothing, or its first row).
        Assert.Same(h.Shell.List.SelectedItem, h.Shell.Detail.Item);
    }

    [Fact]
    public async Task RefreshAsync_LoadedWithWarnings_WritesWarnLinesBeforeTheLoadedLine()
    {
        Harness h = new();
        h.Live.Result = Loaded(ThreeVariables) with
        {
            Method = EnumerationMethod.KnownNamesFallback,
            Warnings = new[] { "known-names fallback in use", "Boot0002: ERROR_ENVVAR_NOT_FOUND (203)" },
        };

        await h.Shell.RefreshAsync();

        Assert.Equal(ShellState.Loaded, h.Shell.State);
        Assert.Equal("Loaded 3 variables from Live firmware (KnownNamesFallback)", h.Shell.StatusText);
        Assert.Equal(
            new[]
            {
                (LogViewModel.InfoLevel, Strings.IdleStatus),
                (LogViewModel.InfoLevel, Strings.LoadingStatus),
                (LogViewModel.WarnLevel, "known-names fallback in use"),
                (LogViewModel.WarnLevel, "Boot0002: ERROR_ENVVAR_NOT_FOUND (203)"),
                (LogViewModel.InfoLevel, "Expected model confirmed: 2014 Dell OptiPlex 7020 (model, BIOS version and CPU generation all match)."),
                (LogViewModel.InfoLevel, "Loaded 3 variables from Live firmware (KnownNamesFallback)"),
            },
            h.Shell.Log.Lines.Select(line => (line.Level, line.Message)));
        // Warnings are logged, never announced.
        Assert.DoesNotContain(h.Announcer.History, text => text.Contains("fallback", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefreshAsync_LoadedWithoutIdentity_ShowsUnknownInEveryIdentityField()
    {
        Harness h = new();
        h.Live.Result = Loaded(ThreeVariables) with { Identity = null };

        await h.Shell.RefreshAsync();

        Assert.Equal(ShellState.Loaded, h.Shell.State);
        AssertIdentityPlaceholders(h.Shell, Strings.Unknown);
    }

    [Fact]
    public async Task RefreshAsync_LoadedWithPartialIdentity_FillsTheGapsWithUnknown()
    {
        Harness h = new();
        MachineIdentityResult partial = new(
            Manufacturer: "  Dell Inc.  ",
            Model: null,
            SystemSku: null,
            BiosVersion: " ",
            BiosReleaseDate: null,
            ServiceTag: "ABC1234",
            BaseBoard: null,
            Cpu: null,
            OsCaption: "Microsoft Windows 11 Pro",
            OsVersion: null,
            OsBuild: null,
            ExpectedModelConfirmed: false,
            ExpectedModelNotes: string.Empty,
            Errors: Array.Empty<string>());
        h.Live.Result = Loaded(ThreeVariables) with { Identity = partial };

        await h.Shell.RefreshAsync();

        Assert.Equal("Manufacturer: Dell Inc.", h.Shell.Manufacturer);
        Assert.Equal("Model: (unknown)", h.Shell.Model);
        Assert.Equal("BIOS version: (unknown)", h.Shell.BiosVersion);
        Assert.Equal("Service tag: ABC1234", h.Shell.ServiceTag);
        Assert.Equal("CPU: (unknown)", h.Shell.Cpu);
        Assert.Equal("Operating system: Microsoft Windows 11 Pro", h.Shell.OperatingSystem);
        Assert.Equal("Expected model confirmed: No", h.Shell.ExpectedModelText);
        // Empty notes: nothing to log.
        Assert.DoesNotContain(h.Shell.Log.Lines, line => line.Message.StartsWith("Expected model", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefreshAsync_ExpectedModelNotConfirmed_SaysNoInTheStrip_AndLogsTheReasoningAsWarn()
    {
        Harness h = new();
        MachineIdentityResult relaunch = LiveFirmwareSourceTests.Identity() with
        {
            BiosVersion = "1.5.0",
            Cpu = "13th Gen Intel(R) Core(TM) i5-13500",
            ExpectedModelConfirmed = false,
            ExpectedModelNotes = "Model \"OptiPlex 7020\": contains 7020 (match). BIOS \"1.5.0\": x.y.z scheme (no match). Expected model not confirmed: model matches, BIOS version does not, CPU does not. Warning: this looks like the 2024 relaunch, not the planned unit (the 2014 OptiPlex 7020): the BIOS version uses the x.y.z scheme of the 2024 OptiPlex 7020. Record this in the gate memo.",
        };
        h.Live.Result = Loaded(ThreeVariables) with { Identity = relaunch };

        await h.Shell.RefreshAsync();

        // The strip stays short and never doubles its own label or ends with a CLI-process instruction.
        Assert.Equal("Expected model confirmed: No", h.Shell.ExpectedModelText);
        Assert.DoesNotContain("gate memo", h.Shell.ExpectedModelText, StringComparison.OrdinalIgnoreCase);
        LogLineViewModel notes = Assert.Single(h.Shell.Log.Lines, line => line.Message.Contains("2024 relaunch", StringComparison.Ordinal));
        Assert.Equal(LogViewModel.WarnLevel, notes.Level);
        Assert.Equal(relaunch.ExpectedModelNotes, notes.Message);
        Assert.True(h.Shell.Log.Lines.IndexOf(notes) < h.Shell.Log.Lines.Count - 1, "the notes come before the Loaded line");
        Assert.StartsWith("Loaded 3 variables", h.Shell.Log.Lines[^1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshAsync_LoadedTwice_AnnouncesEachLoadAndRequestsTheFirstRowEachTime()
    {
        Harness h = new();
        h.Live.Result = Loaded(ThreeVariables);
        await h.Shell.RefreshAsync();
        h.Live.Result = Loaded(new[] { BootOrder });

        await h.Shell.RefreshAsync();

        Assert.Equal("Loaded 1 variable from Live firmware (InMemory)", h.Shell.StatusText);
        Assert.Equal(new[] { FocusTarget.ListFirstRow, FocusTarget.ListFirstRow }, h.Focus.Requests);
        Assert.Equal(2, h.Live.LoadCalls);
    }

    [Fact]
    public async Task RefreshAsync_WithAnActiveFilter_AppendsTheVisibleCountToTheLoadedSentence_Once()
    {
        Harness h = new();
        h.Live.Result = Loaded(ThreeVariables);
        await h.Shell.RefreshAsync();
        h.Shell.List.FilterText = "Timeout";
        h.Shell.OnFilterApplied();
        Assert.Equal("Showing 1 of 3 variables", h.Shell.StatusText);
        int announcements = h.Announcer.History.Count;

        await h.Shell.RefreshAsync();

        // The filter still applies to the reloaded content: the user hears the total and the
        // visible count in one announcement (docs/Keyboard-Map.md section 9).
        Assert.Equal("Loaded 3 variables from Live firmware (InMemory). Showing 1 of 3 variables", h.Shell.StatusText);
        Assert.Equal(
            string.Format(CultureInfo.InvariantCulture, Strings.LoadedFormat, Strings.Variables(3), Strings.LiveSourceName, EnumerationMethod.InMemory)
                + Strings.SentenceSeparator
                + h.Shell.List.ShowingText,
            h.Shell.StatusText);
        Assert.Equal(new[] { Strings.LoadingStatus, h.Shell.StatusText }, h.Announcer.History.Skip(announcements));
        Assert.Equal("Timeout", h.Shell.List.FilterText);
        Assert.Equal(1, h.Shell.List.VisibleCount);
        Assert.Equal(h.Shell.StatusText, h.Shell.Log.Lines[^1].Message);

        // The view's filter box may echo the unchanged filter back; the count was announced already.
        h.Shell.OnFilterApplied();
        Assert.Equal(announcements + 2, h.Announcer.History.Count);

        // An empty filter after a reload keeps the plain Loaded sentence.
        h.Shell.ClearFilterCommand.Execute(null);
        await h.Shell.RefreshAsync();
        Assert.Equal("Loaded 3 variables from Live firmware (InMemory)", h.Shell.StatusText);
    }

    [Fact]
    public async Task ClearFilterAndRegrouping_RebuildTheGroups_ButKeepTheSelectedRowAndTheDetailPane()
    {
        // The window re-focuses the selected row after a rebuild replaces the group containers
        // (docs/Keyboard-Map.md section 9); that only works because the selection itself survives.
        Harness h = new();
        h.Live.Result = Loaded(ThreeVariables);
        await h.Shell.RefreshAsync();
        VariableItemViewModel timeout = h.Shell.List.FindLoaded(Timeout.Key)!;
        h.Shell.List.SelectedItem = timeout;
        h.Shell.List.FilterText = "Time";
        Assert.Same(timeout, h.Shell.List.SelectedItem);
        List<VariableItemViewModel?> selectionEvents = new();
        h.Shell.List.SelectionChanged += (_, item) => selectionEvents.Add(item);
        int resets = 0;
        h.Shell.List.Groups.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                resets++;
            }
        };

        h.Shell.ClearFilterCommand.Execute(null);
        h.Shell.List.GroupBy = GroupBy.Kind;
        h.Shell.OnFilterApplied();
        h.Shell.List.SortBy = SortBy.Size;
        h.Shell.OnFilterApplied();
        h.Shell.List.GroupBy = GroupBy.None;
        h.Shell.OnFilterApplied();

        Assert.Equal(4, resets);
        Assert.Same(timeout, h.Shell.List.SelectedItem);
        Assert.Same(timeout, h.Shell.Detail.Item);
        Assert.Equal("Timeout", h.Shell.Detail.Heading);
        Assert.Empty(selectionEvents);
        Assert.Contains(timeout, h.Shell.List.Groups.SelectMany(group => group.Items));
        Assert.Equal(new[] { FocusTarget.ListFirstRow }, h.Focus.Requests);
    }

    // ---- Refresh: Refused and Failed ---------------------------------------------------------

    [Theory]
    [InlineData(Strings.NotUefiSentence)]
    [InlineData(Strings.NotElevatedSentence)]
    [InlineData(Strings.PrivilegeAbsentSentence)]
    public async Task RefreshAsync_Refused_AnnouncesTheSentenceVerbatim_AndFocusesTheStatusBox(string sentence)
    {
        Harness h = new();
        h.Live.Result = Refused(sentence);

        await h.Shell.RefreshAsync();

        Assert.Equal(ShellState.Refused, h.Shell.State);
        Assert.Equal("Refused", h.Shell.StateText);
        Assert.False(h.Shell.IsBusy);
        Assert.Equal(sentence, h.Shell.StatusText);
        Assert.Equal(new[] { Strings.IdleStatus, Strings.LoadingStatus, sentence }, h.Announcer.History);
        Assert.Equal(new[] { FocusTarget.Status }, h.Focus.Requests);
        // The title names the source that was asked for, as it does for a failed baseline.
        Assert.Equal("UefiAccess – Live firmware", h.Shell.WindowTitle);
        AssertIdentityPlaceholders(h.Shell, Strings.NotReadYet);
        Assert.Null(h.Shell.Detail.Item);
        Assert.Equal(Strings.NoSelectionHeading, h.Shell.Detail.Heading);
        Assert.Empty(h.Shell.List.VisibleItems);
        Assert.Equal(sentence, h.Shell.Log.Lines[^1].Message);
        Assert.Equal(LogViewModel.InfoLevel, h.Shell.Log.Lines[^1].Level);
    }

    [Fact]
    public async Task RefreshAsync_RefusedWithWarnings_LogsTheDetailAsWarn()
    {
        Harness h = new();
        h.Live.Result = Refused(Strings.PrivilegeAbsentSentence) with { Warnings = new[] { "SeSystemEnvironmentPrivilege: AdjustTokenPrivileges reported ERROR_NOT_ALL_ASSIGNED (1300)" } };

        await h.Shell.RefreshAsync();

        Assert.Contains(h.Shell.Log.Lines, line => line.Level == LogViewModel.WarnLevel && line.Message.StartsWith("SeSystemEnvironmentPrivilege:", StringComparison.Ordinal));
        Assert.Equal(Strings.PrivilegeAbsentSentence, h.Shell.StatusText);
    }

    [Fact]
    public async Task RefreshAsync_Failed_AnnouncesLoadingFailed_AndFocusesTheStatusBox()
    {
        Harness h = new();
        h.Live.Result = Failed("No variables could be enumerated: STATUS_INVALID_PARAMETER (0xC000000D)");

        await h.Shell.RefreshAsync();

        Assert.Equal(ShellState.Failed, h.Shell.State);
        Assert.Equal("Failed", h.Shell.StateText);
        Assert.False(h.Shell.IsBusy);
        Assert.Equal("Loading failed: No variables could be enumerated: STATUS_INVALID_PARAMETER (0xC000000D)", h.Shell.StatusText);
        Assert.Equal(new[] { FocusTarget.Status }, h.Focus.Requests);
        AssertIdentityPlaceholders(h.Shell, Strings.NotReadYet);
        Assert.Null(h.Shell.Detail.Item);

        // The panel records the failure once as ERROR (through the mirrored logger) and the
        // announcement once as INFO; the levels use the panel's own vocabulary.
        Assert.Equal(
            new[]
            {
                (LogViewModel.InfoLevel, Strings.IdleStatus),
                (LogViewModel.InfoLevel, Strings.LoadingStatus),
                (LogViewModel.ErrorLevel, "Failed: No variables could be enumerated: STATUS_INVALID_PARAMETER (0xC000000D)"),
                (LogViewModel.InfoLevel, h.Shell.StatusText),
            },
            h.Shell.Log.Lines.Select(line => (line.Level, line.Message)));
    }

    [Fact]
    public async Task RefreshAsync_SourceThrows_FailsWithTheExceptionMessage_AndIsNotBusy()
    {
        Harness h = new();
        h.Live.Throws = new InvalidOperationException("kaboom");

        await h.Shell.RefreshAsync();

        Assert.Equal(ShellState.Failed, h.Shell.State);
        Assert.False(h.Shell.IsBusy);
        Assert.Equal("Loading failed: kaboom", h.Shell.StatusText);
        Assert.Equal(new[] { FocusTarget.Status }, h.Focus.Requests);
        Assert.True(h.Shell.RefreshCommand.CanExecute(null));

        // A thrown source is one ERROR line carrying the exception, not two.
        LogLineViewModel error = Assert.Single(h.Shell.Log.Lines, line => line.Level == LogViewModel.ErrorLevel);
        Assert.Equal("Failed: kaboom (InvalidOperationException: kaboom)", error.Message);
    }

    [Fact]
    public async Task RefreshAsync_SourceFaultsAsynchronously_FailsWithTheExceptionMessage()
    {
        Harness h = new();
        h.Live.FaultsAsync = new IOException("disk gone");

        await h.Shell.RefreshAsync();

        Assert.Equal(ShellState.Failed, h.Shell.State);
        Assert.Equal("Loading failed: disk gone", h.Shell.StatusText);
    }

    [Fact]
    public async Task RefreshAsync_SourceReturnsNull_FailsWithoutThrowing()
    {
        Harness h = new();
        h.Live.Result = null;

        await h.Shell.RefreshAsync();

        Assert.Equal(ShellState.Failed, h.Shell.State);
        Assert.Equal("Loading failed: " + ShellViewModel.NoResultMessage, h.Shell.StatusText);
    }

    [Fact]
    public async Task RefreshAsync_RefusedAfterALoad_EmptiesTheListAndTheDetailPane()
    {
        Harness h = new();
        h.Live.Result = Loaded(ThreeVariables);
        await h.Shell.RefreshAsync();
        VariableItemViewModel item = Item(BootOrder);
        h.Shell.List.SelectedItem = item;
        Assert.Same(item, h.Shell.Detail.Item);
        h.Live.Result = Refused(Strings.NotElevatedSentence);

        await h.Shell.RefreshAsync();

        Assert.Equal(ShellState.Refused, h.Shell.State);
        Assert.Null(h.Shell.Detail.Item);
        Assert.Equal(0, h.Shell.List.TotalCount);
        Assert.Empty(h.Shell.List.VisibleItems);
        Assert.Equal(new[] { FocusTarget.ListFirstRow, FocusTarget.Status }, h.Focus.Requests);
    }

    // ---- busy gating -------------------------------------------------------------------------

    [Fact]
    public async Task RefreshAsync_WhileLoading_IsBusy_DisablesRefresh_AndIgnoresASecondRefresh()
    {
        Harness h = new();
        h.Live.Gate = new TaskCompletionSource<SourceResult>();
        int refreshRaised = 0;
        h.Shell.RefreshCommand.CanExecuteChanged += (_, _) => refreshRaised++;

        Task first = h.Shell.RefreshAsync();

        Assert.False(first.IsCompleted);
        Assert.True(h.Shell.IsBusy);
        Assert.Equal(ShellState.Loading, h.Shell.State);
        Assert.Equal("Loading", h.Shell.StateText);
        Assert.Equal(Strings.LoadingStatus, h.Shell.StatusText);
        Assert.False(h.Shell.RefreshCommand.CanExecute(null));

        // Open baseline folder stays available: a folder picked replaces the running load (ShellLoadReplacementTests).
        Assert.True(h.Shell.OpenBaselineCommand.CanExecute(null));
        Assert.True(h.Shell.ClearFilterCommand.CanExecute(null));
        Assert.True(h.Shell.CopyValueCommand.CanExecute(null));
        Assert.True(h.Shell.ExportVisibleListCommand.CanExecute(null));
        Assert.True(h.Shell.CopyLogCommand.CanExecute(null));
        Assert.Equal(1, refreshRaised);

        Task second = h.Shell.RefreshAsync();
        Assert.True(second.IsCompletedSuccessfully);
        Assert.Equal(1, h.Live.LoadCalls);
        Assert.Empty(h.FactoryFolders);
        Assert.Empty(h.Focus.Requests);

        h.Live.Gate.SetResult(Loaded(ThreeVariables));
        await first;

        Assert.False(h.Shell.IsBusy);
        Assert.Equal(ShellState.Loaded, h.Shell.State);
        Assert.True(h.Shell.RefreshCommand.CanExecute(null));
        Assert.True(h.Shell.OpenBaselineCommand.CanExecute(null));
        Assert.Equal(2, refreshRaised);
        Assert.Equal(new[] { FocusTarget.ListFirstRow }, h.Focus.Requests);
    }

    [Fact]
    public async Task RefreshCommand_ExecuteAsync_LoadsTheLiveSource_AndIsDisabledMeanwhile()
    {
        Harness h = new();
        h.Live.Gate = new TaskCompletionSource<SourceResult>();
        AsyncRelayCommand refresh = Assert.IsType<AsyncRelayCommand>(h.Shell.RefreshCommand);

        Task running = refresh.ExecuteAsync();
        Assert.True(refresh.IsRunning);
        Assert.False(refresh.CanExecute(null));
        Assert.True(h.Shell.IsBusy);

        h.Live.Gate.SetResult(Loaded(new[] { BootOrder }));
        await running;

        Assert.False(refresh.IsRunning);
        Assert.False(h.Shell.IsBusy);
        Assert.Equal(ShellState.Loaded, h.Shell.State);
        Assert.Equal("Loaded 1 variable from Live firmware (InMemory)", h.Shell.StatusText);
        Assert.Equal(1, h.Live.LoadCalls);
    }

    // ---- Open baseline folder ----------------------------------------------------------------

    [Fact]
    public async Task OpenBaselineAsync_Loaded_ShowsTheOfflineTitleAndDescriptionEverywhere()
    {
        Harness h = new();
        h.Baseline.Result = Loaded(new[] { BootOrder, Timeout }, BaselineDescription, isOffline: true);

        await h.Shell.OpenBaselineAsync(BaselineFolder);

        Assert.Equal(new[] { BaselineFolder }, h.FactoryFolders);
        Assert.Equal(1, h.Baseline.LoadCalls);
        Assert.Equal(0, h.Live.LoadCalls);
        Assert.Equal(ShellState.Loaded, h.Shell.State);
        Assert.True(h.Shell.IsOffline);
        Assert.Equal(BaselineDescription, h.Shell.SourceDescription);
        Assert.Equal("UefiAccess – " + BaselineDescription, h.Shell.WindowTitle);
        Assert.Equal("Loaded 2 variables from " + BaselineDescription + " (InMemory)", h.Shell.StatusText);
        Assert.Equal(new[] { FocusTarget.ListFirstRow }, h.Focus.Requests);
        Assert.Equal("Manufacturer: Dell Inc.", h.Shell.Manufacturer);
    }

    [Fact]
    public async Task OpenBaselineAsync_ThenRefresh_RestoresTheLiveTitle()
    {
        Harness h = new();
        h.Baseline.Result = Loaded(new[] { BootOrder }, BaselineDescription, isOffline: true);
        h.Live.Result = Loaded(ThreeVariables);
        await h.Shell.OpenBaselineAsync(BaselineFolder);
        Assert.True(h.Shell.IsOffline);

        await h.Shell.RefreshAsync();

        Assert.False(h.Shell.IsOffline);
        Assert.Equal("UefiAccess – Live firmware", h.Shell.WindowTitle);
        Assert.Equal("Live firmware", h.Shell.SourceDescription);
        Assert.Equal("Loaded 3 variables from Live firmware (InMemory)", h.Shell.StatusText);
    }

    [Fact]
    public async Task OpenBaselineAsync_Failed_NamesTheBaselineInTheTitle_AndShowsNothingFromIt()
    {
        Harness h = new();
        h.Live.Result = Loaded(ThreeVariables);
        await h.Shell.RefreshAsync();
        h.Baseline.Result = Failed("index.json not found") with { SourceDescription = BaselineDescription, IsOffline = true };

        await h.Shell.OpenBaselineAsync(BaselineFolder);

        Assert.Equal(ShellState.Failed, h.Shell.State);
        Assert.True(h.Shell.IsOffline);
        Assert.Equal("UefiAccess – " + BaselineDescription, h.Shell.WindowTitle);
        Assert.Equal("Loading failed: index.json not found", h.Shell.StatusText);
        Assert.Equal(FocusTarget.Status, h.Focus.Last);
        Assert.Equal(0, h.Shell.List.TotalCount);
        Assert.Null(h.Shell.Detail.Item);
        AssertIdentityPlaceholders(h.Shell, Strings.NotReadYet);
    }

    [Fact]
    public async Task OpenBaselineAsync_ResultWithoutADescription_FallsBackToTheSourcesDescription()
    {
        Harness h = new();
        h.Baseline.Result = Loaded(new[] { BootOrder }, description: string.Empty, isOffline: true);

        await h.Shell.OpenBaselineAsync(BaselineFolder);

        Assert.Equal(BaselineDescription, h.Shell.SourceDescription);
        Assert.Equal("UefiAccess – " + BaselineDescription, h.Shell.WindowTitle);
        Assert.Equal("Loaded 1 variable from " + BaselineDescription + " (InMemory)", h.Shell.StatusText);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task OpenBaselineAsync_BlankFolder_DoesNothing(string folder)
    {
        Harness h = new();

        await h.Shell.OpenBaselineAsync(folder);

        Assert.Empty(h.FactoryFolders);
        Assert.Equal(ShellState.Idle, h.Shell.State);
        Assert.Equal(new[] { Strings.IdleStatus }, h.Announcer.History);
        Assert.Empty(h.Focus.Requests);
    }

    [Fact]
    public async Task OpenBaselineAsync_FactoryThrows_FailsWithTheMessage_AndIsNotBusy()
    {
        FakeSource live = new();
        RecordingAnnouncer announcer = new();
        RecordingFocusSink focus = new();
        ShellViewModel shell = new(live, _ => throw new DirectoryNotFoundException("no such folder"), announcer, focus, new NullPlatformInteractions());

        await shell.OpenBaselineAsync(BaselineFolder);

        Assert.Equal(ShellState.Failed, shell.State);
        Assert.False(shell.IsBusy);
        Assert.Equal("Loading failed: no such folder", shell.StatusText);
        Assert.True(shell.IsOffline);
        Assert.Equal("UefiAccess – " + BaselineDescription, shell.WindowTitle);
        Assert.Equal(new[] { FocusTarget.Status }, focus.Requests);
    }

    [Fact]
    public async Task OpenBaselineCommand_CancelledDialog_LoadsNothingAndChangesNoState()
    {
        Harness h = new();
        h.Platform.FolderToReturn = null;

        await Execute(h.Shell.OpenBaselineCommand);

        Assert.Empty(h.FactoryFolders);
        Assert.Equal(0, h.Live.LoadCalls);
        Assert.Equal(0, h.Baseline.LoadCalls);
        Assert.Equal(ShellState.Idle, h.Shell.State);
        Assert.False(h.Shell.IsBusy);
        Assert.False(h.Shell.IsOffline);
        Assert.Equal("UefiAccess", h.Shell.WindowTitle);
        Assert.Empty(h.Focus.Requests);
        Assert.Equal(Strings.DialogCancelledStatus, h.Shell.StatusText);
        Assert.True(h.Shell.OpenBaselineCommand.CanExecute(null));
    }

    [Fact]
    public async Task OpenBaselineCommand_PickedFolder_LoadsIt()
    {
        Harness h = new();
        h.Platform.FolderToReturn = BaselineFolder;
        h.Baseline.Result = Loaded(new[] { BootOrder }, BaselineDescription, isOffline: true);

        await Execute(h.Shell.OpenBaselineCommand);

        Assert.Equal(new[] { BaselineFolder }, h.FactoryFolders);
        Assert.Equal(ShellState.Loaded, h.Shell.State);
        Assert.Equal("UefiAccess – " + BaselineDescription, h.Shell.WindowTitle);
        Assert.Equal(new[] { FocusTarget.ListFirstRow }, h.Focus.Requests);
    }

    [Fact]
    public async Task OpenBaselineCommand_DialogThrows_SaysSo_AndLeavesTheStateAndFocusAlone()
    {
        FakeSource live = new();
        RecordingAnnouncer announcer = new();
        RecordingFocusSink focus = new();
        ShellViewModel shell = new(live, _ => live, announcer, focus, new ThrowingPlatform());

        await Execute(shell.OpenBaselineCommand);

        // Nothing was loaded, so this is not "Loading failed" and nothing moves (finding F088).
        Assert.Equal(ShellState.Idle, shell.State);
        Assert.False(shell.IsBusy);
        Assert.Equal("Open baseline folder failed: dialog exploded. Nothing changed.", shell.StatusText);
        Assert.Empty(focus.Requests);
        Assert.Equal(0, live.LoadCalls);
        Assert.True(shell.OpenBaselineCommand.CanExecute(null));
        Assert.True(shell.RefreshCommand.CanExecute(null));
    }

    // ---- Clear filter ------------------------------------------------------------------------

    [Fact]
    public void ClearFilterCommand_EmptiesTheFilter_AnnouncesTheCount_AndLeavesFocusWhereItIs()
    {
        Harness h = new();
        h.Shell.List.FilterText = "boot";

        h.Shell.ClearFilterCommand.Execute(null);

        Assert.Equal(string.Empty, h.Shell.List.FilterText);
        Assert.Equal(h.Shell.List.ShowingText, h.Shell.StatusText);
        Assert.Matches(@"^Showing \d+ of \d+ variables$", h.Shell.StatusText);
        // docs/Keyboard-Map.md section 9: Alt+C and the button leave focus where it is; the Escape
        // path moves focus to the list itself, in the view.
        Assert.Empty(h.Focus.Requests);
    }

    [Fact]
    public async Task ClearFilterCommand_ThenTheViewsFilterEcho_AnnouncesTheCountOnce_AndTheCountIsRight()
    {
        Harness h = new();
        h.Live.Result = Loaded(ThreeVariables);
        await h.Shell.RefreshAsync();
        h.Shell.List.FilterText = "Timeout";
        h.Shell.OnFilterApplied();
        Assert.Equal("Showing 1 of 3 variables", h.Shell.StatusText);

        // The window's filter box is bound TwoWay to FilterText; clearing the filter pushes the empty
        // text into the box, whose TextChanged handler calls OnFilterApplied again. A listener on
        // FilterText must already see the rebuilt list, and the second call must not repeat the count.
        string? textSeenByListener = null;
        h.Shell.List.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VariableListViewModel.FilterText))
            {
                textSeenByListener = h.Shell.List.ShowingText;
                h.Shell.OnFilterApplied();
            }
        };

        h.Shell.ClearFilterCommand.Execute(null);

        Assert.Equal("Showing 3 of 3 variables", textSeenByListener);
        Assert.Equal("Showing 3 of 3 variables", h.Shell.StatusText);
        Assert.Equal(1, h.Announcer.History.Count(text => text == "Showing 3 of 3 variables"));
        Assert.Equal(1, h.Shell.Log.Lines.Count(line => line.Message == "Showing 3 of 3 variables"));
        // The stale count was never announced during the clear: the one "1 of 3" is the filter itself.
        Assert.Equal(1, h.Announcer.History.Count(text => text == "Showing 1 of 3 variables"));
    }

    [Fact]
    public async Task OnFilterApplied_AnnouncesOncePerChangeOfTheRowsShown_NotForAGroupingNorAfterAnotherAnnouncement()
    {
        Harness h = new();
        h.Live.Result = Loaded(ThreeVariables);
        await h.Shell.RefreshAsync();
        int before = h.Announcer.History.Count;

        // "Boot" matches BootOrder by name and Timeout by its decoder kind (BootOrder): 2 of 3.
        h.Shell.List.FilterText = "Boot";
        h.Shell.OnFilterApplied();
        h.Shell.OnFilterApplied();
        Assert.Equal(before + 1, h.Announcer.History.Count);
        Assert.Equal("Showing 2 of 3 variables", h.Shell.StatusText);

        // A2-9 (the tester's A2-7): a change of grouping leaves the filter text and the counts as they
        // were, so it says nothing; the combo box speaks the new choice itself.
        h.Shell.List.GroupBy = GroupBy.None;
        h.Shell.OnFilterApplied();
        Assert.Equal(before + 1, h.Announcer.History.Count);

        // What counts is the list the view applied, not what was announced last: after another
        // announcement the same rows are not news again.
        h.Shell.OnRegionArrived(Region.List);
        h.Shell.OnFilterApplied();
        Assert.Equal(before + 2, h.Announcer.History.Count);
        Assert.Equal("List region", h.Shell.StatusText);

        // Pressing Clear filter twice announces twice: each is a user action, even when the
        // second press changes nothing.
        h.Shell.ClearFilterCommand.Execute(null);
        h.Shell.ClearFilterCommand.Execute(null);
        Assert.Equal(before + 4, h.Announcer.History.Count);
        Assert.Equal(new[] { "Showing 3 of 3 variables", "Showing 3 of 3 variables" }, h.Announcer.History.TakeLast(2));
    }

    // ---- Copy value --------------------------------------------------------------------------

    [Fact]
    public void CopyValueCommand_CopiesTheDetailPanesValueToTheClipboard()
    {
        Harness h = new();
        h.Shell.Detail.CopyValueText = "Boot0001 Windows Boot Manager";

        h.Shell.CopyValueCommand.Execute(null);

        Assert.Equal("Boot0001 Windows Boot Manager", h.Platform.LastClipboardText);
        Assert.Equal(Strings.CopiedValueStatus, h.Shell.StatusText);
        Assert.Empty(h.Focus.Requests);
    }

    // ---- Copy key (Ctrl+C in the list) -------------------------------------------------------

    [Fact]
    public void CopyKey_CopiesTheRowKey_Silently()
    {
        Harness h = new();
        VariableItemViewModel item = Item(Vendor);
        int announcements = h.Announcer.History.Count;
        int logLines = h.Shell.Log.Count;

        h.Shell.CopyKey(item);

        Assert.Equal(Vendor.Key, h.Platform.LastClipboardText);
        // docs/Keyboard-Map.md section 9: Ctrl+C in the list leaves Status unchanged on success.
        Assert.Equal(announcements, h.Announcer.History.Count);
        Assert.Equal(logLines, h.Shell.Log.Count);
        Assert.Equal(Strings.IdleStatus, h.Shell.StatusText);
        Assert.Empty(h.Focus.Requests);
    }

    [Fact]
    public void CopyKey_ClipboardThrows_AnnouncesCopyFailed_AndLogsOnce()
    {
        Harness h = new(new ThrowingPlatform());
        VariableItemViewModel item = Item(Vendor);

        Exception? escaped = Xunit.Record.Exception(() => h.Shell.CopyKey(item));

        Assert.Null(escaped);
        Assert.Equal("Copy failed: clipboard exploded", h.Shell.StatusText);
        Assert.Equal("Copy failed: clipboard exploded", h.Announcer.History[^1]);
        Assert.Equal(ShellState.Idle, h.Shell.State);
        Assert.Empty(h.Focus.Requests);
        LogLineViewModel error = Assert.Single(h.Shell.Log.Lines, line => line.Level == LogViewModel.ErrorLevel);
        Assert.Equal("Copy key failed (InvalidOperationException: clipboard exploded)", error.Message);
    }

    [Fact]
    public void CopyKey_Null_DoesNothing()
    {
        Harness h = new();
        int announcements = h.Announcer.History.Count;

        h.Shell.CopyKey(null);

        Assert.Null(h.Platform.LastClipboardText);
        Assert.Equal(announcements, h.Announcer.History.Count);
    }

    [Fact]
    public void CopyValueCommand_WithoutASelection_CopiesNothingAndSaysSo()
    {
        Harness h = new();

        h.Shell.CopyValueCommand.Execute(null);

        Assert.Null(h.Platform.LastClipboardText);
        Assert.Equal(Strings.NothingToCopyStatus, h.Shell.StatusText);
    }

    [Fact]
    public void CopyValueCommand_WithoutAFocusedField_FallsBackToTheSummary()
    {
        Harness h = new();
        DecodedVariable decoded = new(BootOrder.Key, "BootOrder", "Boot order: Boot0001 Windows Boot Manager", Array.Empty<DecodedField>(), null, null);
        VariableItemViewModel item = new(BootOrder, NamespaceCatalog.Describe(BootOrder.VendorGuid), decoded);
        h.Shell.List.SelectedItem = item;
        h.Shell.Detail.CopyValueText = null;

        h.Shell.CopyValueCommand.Execute(null);

        Assert.Equal("Boot order: Boot0001 Windows Boot Manager", h.Platform.LastClipboardText);
        Assert.Equal(Strings.CopiedValueStatus, h.Shell.StatusText);
    }

    [Fact]
    public void CopyValueCommand_ForARawVariableWithoutAFocusedField_FallsBackToTheKey()
    {
        Harness h = new();
        VariableItemViewModel item = Item(Vendor);
        h.Shell.List.SelectedItem = item;
        h.Shell.Detail.CopyValueText = null;

        h.Shell.CopyValueCommand.Execute(null);

        Assert.Equal(Vendor.Key, h.Platform.LastClipboardText);
        Assert.Equal("af9ffd67-ec10-488a-9dfc-6cbf5ee22c2e:AcpiGlobalVariable", h.Platform.LastClipboardText);
        Assert.Equal(Strings.CopiedValueStatus, h.Shell.StatusText);
    }

    [Fact]
    public void CopyValueCommand_ClipboardThrows_AnnouncesCopyFailed_AndChangesNoState()
    {
        Harness h = new(new ThrowingPlatform());
        h.Shell.Detail.CopyValueText = "Boot0001 Windows Boot Manager";

        Exception? escaped = Xunit.Record.Exception(() => h.Shell.CopyValueCommand.Execute(null));

        Assert.Null(escaped);
        Assert.Equal("Copy failed: clipboard exploded", h.Shell.StatusText);
        Assert.Equal(ShellState.Idle, h.Shell.State);
        Assert.Empty(h.Focus.Requests);
        LogLineViewModel error = Assert.Single(h.Shell.Log.Lines, line => line.Level == LogViewModel.ErrorLevel);
        Assert.Equal("Copy value failed (InvalidOperationException: clipboard exploded)", error.Message);
        Assert.Equal(h.Shell.StatusText, h.Shell.Log.Lines[^1].Message);
    }

    // ---- Export visible list -----------------------------------------------------------------

    [Fact]
    public async Task ExportVisibleListCommand_WritesPlainTextForATxtName_AndAnnouncesThePath()
    {
        Harness h = new();
        h.Live.Result = Loaded(ThreeVariables);
        await h.Shell.RefreshAsync();
        string path = Path.Combine(_tempRoot, "visible.txt");
        h.Platform.FileToReturn = path;

        h.Shell.ExportVisibleListCommand.Execute(null);

        Assert.True(File.Exists(path));
        Assert.Equal(VisibleListExporter.ToPlainText(h.Shell.List.VisibleItems), File.ReadAllText(path));
        Assert.Equal(
            string.Format(CultureInfo.InvariantCulture, Strings.ExportedFormat, Strings.VisibleVariables(h.Shell.List.VisibleItems.Count), path),
            h.Shell.StatusText);
        Assert.Equal("Exported 3 visible variables to " + path, h.Shell.StatusText);
        Assert.Equal(ShellState.Loaded, h.Shell.State);
        Assert.Equal(new[] { FocusTarget.ListFirstRow }, h.Focus.Requests);
    }

    [Fact]
    public async Task ExportVisibleListCommand_WritesJsonForAJsonName()
    {
        Harness h = new();
        h.Live.Result = Loaded(ThreeVariables);
        await h.Shell.RefreshAsync();
        string path = Path.Combine(_tempRoot, "Visible.JSON");
        h.Platform.FileToReturn = path;

        h.Shell.ExportVisibleListCommand.Execute(null);

        Assert.True(File.Exists(path));
        Assert.Equal(VisibleListExporter.ToJson(h.Shell.List.VisibleItems), File.ReadAllText(path));
        Assert.StartsWith("Exported ", h.Shell.StatusText, StringComparison.Ordinal);
        Assert.EndsWith(path, h.Shell.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportVisibleListCommand_WritesNoByteOrderMark()
    {
        Harness h = new();
        string path = Path.Combine(_tempRoot, "empty.txt");
        h.Platform.FileToReturn = path;

        h.Shell.ExportVisibleListCommand.Execute(null);

        byte[] bytes = File.ReadAllBytes(path);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "the export must not start with a UTF-8 byte order mark");
    }

    [Fact]
    public void ExportVisibleListCommand_CancelledDialog_WritesNothing()
    {
        Harness h = new();
        h.Platform.FileToReturn = null;

        h.Shell.ExportVisibleListCommand.Execute(null);

        Assert.Empty(Directory.GetFiles(_tempRoot));
        Assert.Equal(Strings.DialogCancelledStatus, h.Shell.StatusText);
        Assert.Empty(h.Focus.Requests);
    }

    [Fact]
    public void ExportVisibleListCommand_UnwritablePath_AnnouncesExportFailed_AndKeepsTheState()
    {
        Harness h = new();
        string path = Path.Combine(_tempRoot, "missing-folder", "deeper", "visible.txt");
        h.Platform.FileToReturn = path;

        h.Shell.ExportVisibleListCommand.Execute(null);

        Assert.False(File.Exists(path));
        Assert.StartsWith("Export failed: ", h.Shell.StatusText, StringComparison.Ordinal);
        Assert.Equal(ShellState.Idle, h.Shell.State);
        LogLineViewModel error = Assert.Single(h.Shell.Log.Lines, line => line.Level == LogViewModel.ErrorLevel);
        Assert.StartsWith("Export visible list to " + path + " failed (", error.Message, StringComparison.Ordinal);
        Assert.Equal(LogViewModel.InfoLevel, h.Shell.Log.Lines[^1].Level);
        Assert.Equal(h.Shell.StatusText, h.Shell.Log.Lines[^1].Message);
    }

    [Fact]
    public void ExportVisibleListCommand_SaveDialogThrows_AnnouncesExportFailed_NotLoadingFailed()
    {
        Harness h = new(new ThrowingPlatform());

        Exception? escaped = Xunit.Record.Exception(() => h.Shell.ExportVisibleListCommand.Execute(null));

        Assert.Null(escaped);
        Assert.Equal("Export failed: dialog exploded", h.Shell.StatusText);
        Assert.Equal(ShellState.Idle, h.Shell.State);
        Assert.Empty(h.Focus.Requests);
        Assert.Single(h.Shell.Log.Lines, line => line.Level == LogViewModel.ErrorLevel);
    }

    // ---- Copy log ----------------------------------------------------------------------------

    [Fact]
    public async Task CopyLogCommand_CopiesEveryLineAsWritten_ThenConfirms()
    {
        Harness h = new();
        h.Live.Result = Loaded(ThreeVariables) with { Warnings = new[] { "one warning" } };
        await h.Shell.RefreshAsync();
        string expected = h.Shell.Log.CopyText();

        h.Shell.CopyLogCommand.Execute(null);

        Assert.Equal(expected, h.Platform.LastClipboardText);
        Assert.Equal(Strings.CopiedLogStatus, h.Shell.StatusText);
        Assert.Equal(Strings.CopiedLogStatus, h.Shell.Log.Lines[^1].Message);
        Assert.DoesNotContain(Strings.CopiedLogStatus, expected, StringComparison.Ordinal);

        // Copy log writes the full date, time and offset on every line (the panel speaks the time alone).
        const string Stamp = @"^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d [+-]\d\d:\d\d ";
        string[] lines = expected.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(5, lines.Length);
        Assert.Matches(Stamp + "INFO " + Regex.Escape(Strings.IdleStatus) + "$", lines[0]);
        Assert.Matches(Stamp + "INFO " + Regex.Escape(Strings.LoadingStatus) + "$", lines[1]);
        Assert.Matches(Stamp + "WARN one warning$", lines[2]);
        Assert.Matches(Stamp + "INFO Expected model confirmed: 2014 Dell OptiPlex 7020 .*$", lines[3]);
        Assert.Matches(Stamp + @"INFO Loaded 3 variables from Live firmware \(InMemory\)$", lines[4]);
    }

    [Fact]
    public void CopyLogCommand_ClipboardThrows_AnnouncesCopyFailed()
    {
        Harness h = new(new ThrowingPlatform());

        Exception? escaped = Xunit.Record.Exception(() => h.Shell.CopyLogCommand.Execute(null));

        Assert.Null(escaped);
        Assert.Equal("Copy failed: clipboard exploded", h.Shell.StatusText);
        Assert.Equal(ShellState.Idle, h.Shell.State);
        Assert.Empty(h.Focus.Requests);
        LogLineViewModel error = Assert.Single(h.Shell.Log.Lines, line => line.Level == LogViewModel.ErrorLevel);
        Assert.Equal("Copy log failed (InvalidOperationException: clipboard exploded)", error.Message);
    }

    // ---- the log panel: one line per event ---------------------------------------------------

    [Fact]
    public async Task LogPanel_WithTheApplicationsLoggerMirror_RecordsEveryEventOnce_InOneVocabulary()
    {
        Harness h = new();
        h.Live.Result = Loaded(ThreeVariables) with
        {
            Method = EnumerationMethod.NtEnumerate,
            Warnings = new[] { "enumeration incomplete: buffer truncated" },
        };

        await h.Shell.RefreshAsync();
        h.Shell.List.FilterText = "Boot";
        h.Shell.OnFilterApplied();
        h.Shell.ClearFilterCommand.Execute(null);
        h.Shell.CopyValueCommand.Execute(null);

        (string Level, string Message)[] lines = h.Shell.Log.Lines.Select(line => (line.Level, line.Message)).ToArray();
        Assert.Equal(
            new[]
            {
                (LogViewModel.InfoLevel, Strings.IdleStatus),
                (LogViewModel.InfoLevel, Strings.LoadingStatus),
                (LogViewModel.WarnLevel, "enumeration incomplete: buffer truncated"),
                (LogViewModel.InfoLevel, "Expected model confirmed: 2014 Dell OptiPlex 7020 (model, BIOS version and CPU generation all match)."),
                (LogViewModel.InfoLevel, "Loaded 3 variables from Live firmware (NtEnumerate)"),
                (LogViewModel.InfoLevel, "Showing 2 of 3 variables"),
                (LogViewModel.InfoLevel, "Showing 3 of 3 variables"),
                (LogViewModel.InfoLevel, Strings.CopiedValueStatus),
            },
            lines);
        Assert.Equal(lines.Length, lines.Distinct().Count());
        Assert.All(lines, line => Assert.Contains(line.Level, new[] { LogViewModel.InfoLevel, LogViewModel.WarnLevel, LogViewModel.ErrorLevel }));
        // The logger was wired and used, but nothing it received at Information or above
        // duplicated a line the shell wrote itself: every mirrored line above came from nowhere.
        Assert.True(h.Mirror.Received > 0, "the mirrored logger received events");
        Assert.Equal(0, h.Mirror.Mirrored);
    }

    // ---- regions, filter, selection ----------------------------------------------------------

    [Theory]
    [InlineData(Region.Toolbar, "Toolbar region")]
    [InlineData(Region.Filter, "Filter region")]
    [InlineData(Region.List, "List region")]
    [InlineData(Region.Detail, "Detail region")]
    [InlineData(Region.Log, "Log region")]
    public void OnRegionArrived_AnnouncesTheRegionName_WithoutRequestingFocus(Region region, string expected)
    {
        Harness h = new();

        h.Shell.OnRegionArrived(region);

        Assert.Equal(expected, h.Shell.StatusText);
        Assert.Equal(expected, h.Shell.Log.Lines[^1].Message);
        Assert.Empty(h.Focus.Requests);
        Assert.Equal(ShellState.Idle, h.Shell.State);
    }

    [Fact]
    public async Task OnRegionArrived_Status_KeepsTheLastStatusMessage_SoLandingOnTheBoxReadsIt()
    {
        Harness h = new();
        h.Live.Result = Refused(Strings.NotElevatedSentence);
        await h.Shell.RefreshAsync();
        int announcements = h.Announcer.History.Count;
        int logLines = h.Shell.Log.Count;

        h.Shell.OnRegionArrived(Region.Status);

        Assert.Equal(Strings.NotElevatedSentence, h.Shell.StatusText);
        Assert.Equal(announcements, h.Announcer.History.Count);
        Assert.Equal(logLines, h.Shell.Log.Count);
        Assert.DoesNotContain("Status region", h.Announcer.History);
        Assert.Equal(new[] { FocusTarget.Status }, h.Focus.Requests);
    }

    [Fact]
    public void OnFilterApplied_AnnouncesTheShowingText_AndLeavesFocusWhereItIs()
    {
        Harness h = new();
        h.Shell.List.FilterText = "Boot";

        h.Shell.OnFilterApplied();

        Assert.Equal(h.Shell.List.ShowingText, h.Shell.StatusText);
        Assert.Matches(@"^Showing \d+ of \d+ variables$", h.Shell.StatusText);
        Assert.Empty(h.Focus.Requests);
        Assert.Equal("Boot", h.Shell.List.FilterText);
    }

    [Fact]
    public void SelectingARow_ShowsItInTheDetailPane_AndClearingTheSelectionEmptiesIt()
    {
        Harness h = new();
        VariableItemViewModel item = Item(Timeout);

        h.Shell.List.SelectedItem = item;
        Assert.Same(item, h.Shell.Detail.Item);
        Assert.Equal("Timeout", h.Shell.Detail.Heading);
        Assert.Empty(h.Focus.Requests);

        h.Shell.List.SelectedItem = null;
        Assert.Null(h.Shell.Detail.Item);
        Assert.Equal(Strings.NoSelectionHeading, h.Shell.Detail.Heading);
    }

    // ---- detail refresh scheduling (finding A1-6) --------------------------------------------

    [Fact]
    public async Task SelectingARow_SchedulesTheDetailRefresh_AndThePaneWaitsUntilItRuns()
    {
        // Under fast arrow-key repeat NVDA dropped the row's name: every SelectionChanged rebuilt
        // the whole pane synchronously on the UI thread. The selection itself is still immediate
        // (Ctrl+C and the row highlight need it); only Detail.Show goes through the scheduler.
        RecordingDetailScheduler scheduler = new();
        Harness h = new(detailScheduler: scheduler);
        h.Live.Result = Loaded(ThreeVariables);
        await h.Shell.RefreshAsync();
        VariableItemViewModel first = SelectAndSettle(h, scheduler, h.Shell.List.VisibleItems[0]);
        VariableItemViewModel timeout = h.Shell.List.FindLoaded(Timeout.Key)!;
        Assert.NotSame(first, timeout);
        string status = h.Shell.StatusText;
        int announcements = h.Announcer.History.Count;

        h.Shell.List.SelectedItem = timeout;

        Assert.Same(timeout, h.Shell.List.SelectedItem);
        Assert.Same(first, h.Shell.Detail.Item);
        Assert.Equal(first.Name, h.Shell.Detail.Heading);
        Assert.Equal(1, scheduler.ScheduleCalls);
        Assert.Equal(0, scheduler.FlushCalls);
        Assert.Equal(0, scheduler.RefreshesRun);
        Assert.True(scheduler.IsPending);

        Assert.True(scheduler.RunPending());

        Assert.Same(timeout, h.Shell.Detail.Item);
        Assert.Equal("Timeout", h.Shell.Detail.Heading);
        Assert.False(scheduler.IsPending);
        Assert.Equal(1, scheduler.RefreshesRun);
        Assert.False(scheduler.RunPending());
        Assert.Empty(h.Focus.Requests);
        Assert.Equal(status, h.Shell.StatusText);
        Assert.Equal(announcements, h.Announcer.History.Count);
    }

    [Fact]
    public async Task FiveRapidSelections_RunOneRefresh_ShowingTheLastRow()
    {
        RecordingDetailScheduler scheduler = new();
        Harness h = new(detailScheduler: scheduler);
        h.Live.Result = Loaded(ThreeVariables);
        await h.Shell.RefreshAsync();
        VariableItemViewModel[] rows = h.Shell.List.VisibleItems.ToArray();
        Assert.Equal(3, rows.Length);
        VariableItemViewModel first = SelectAndSettle(h, scheduler, rows[0]);

        // The pane is updated in place (fix 1 of the A16 measurement): the Sections property is
        // never raised, so one refresh is one change of Item and one "rebuild finished" line of the
        // input trace (the step the trace still calls a rebuild), and before the refresh runs no
        // property, collection or field of the pane changes at all.
        DetailPaneRecorder detailChanges = new(h.Shell.Detail);
        RecordingInputTrace trace = new();
        h.Shell.InputTrace = trace;

        // Down, Down, Up, Up, End: five selection changes in one burst of key repeat.
        VariableItemViewModel[] burst = { rows[1], rows[2], rows[1], rows[0], rows[2] };
        foreach (VariableItemViewModel row in burst)
        {
            h.Shell.List.SelectedItem = row;
        }

        Assert.Same(rows[2], h.Shell.List.SelectedItem);
        Assert.Equal(5, scheduler.ScheduleCalls);
        Assert.Equal(0, scheduler.RefreshesRun);
        Assert.Same(first, h.Shell.Detail.Item);
        Assert.Empty(detailChanges.Events);
        Assert.Equal(0, RebuildsTraced(trace));

        Assert.True(scheduler.RunPending());

        Assert.Same(rows[2], h.Shell.Detail.Item);
        Assert.Equal(rows[2].Name, h.Shell.Detail.Heading);
        Assert.Equal(1, scheduler.RefreshesRun);
        Assert.False(scheduler.IsPending);
        Assert.Equal(1, detailChanges.PaneProperties.Count(name => name == nameof(VariableDetailViewModel.Item)));
        Assert.DoesNotContain(nameof(VariableDetailViewModel.Sections), detailChanges.PaneProperties);
        Assert.Empty(detailChanges.ForbiddenCollectionChanges);
        Assert.Equal(1, RebuildsTraced(trace));

        // A second burst, ending on another row, shows that row, once.
        detailChanges.Clear();
        trace.Clear();
        foreach (VariableItemViewModel row in new[] { rows[1], rows[0], rows[2], rows[0], rows[1] })
        {
            h.Shell.List.SelectedItem = row;
        }

        Assert.Same(rows[2], h.Shell.Detail.Item);
        Assert.Empty(detailChanges.Events);
        Assert.True(scheduler.RunPending());
        Assert.Same(rows[1], h.Shell.Detail.Item);
        Assert.Equal(2, scheduler.RefreshesRun);
        Assert.Equal(1, detailChanges.PaneProperties.Count(name => name == nameof(VariableDetailViewModel.Item)));
        Assert.DoesNotContain(nameof(VariableDetailViewModel.Sections), detailChanges.PaneProperties);
        Assert.Empty(detailChanges.ForbiddenCollectionChanges);
        Assert.Equal(1, RebuildsTraced(trace));
    }

    [Fact]
    public async Task EnsureDetailCurrent_TheEnterToDetailPath_FlushesThePendingRefreshFirst()
    {
        // The window calls EnsureDetailCurrent before it moves focus into the pane (Enter or Space
        // on a row, F6 to Detail), so the first field belongs to the selected row.
        RecordingDetailScheduler scheduler = new();
        Harness h = new(detailScheduler: scheduler);
        h.Live.Result = Loaded(ThreeVariables);
        await h.Shell.RefreshAsync();
        VariableItemViewModel first = SelectAndSettle(h, scheduler, h.Shell.List.VisibleItems[0]);
        VariableItemViewModel timeout = h.Shell.List.FindLoaded(Timeout.Key)!;
        h.Shell.List.SelectedItem = timeout;
        Assert.Same(first, h.Shell.Detail.Item);

        h.Shell.EnsureDetailCurrent();

        Assert.Equal(1, scheduler.FlushCalls);
        Assert.Equal(1, scheduler.RefreshesRun);
        Assert.False(scheduler.IsPending);
        Assert.Same(timeout, h.Shell.Detail.Item);
        Assert.Equal("Timeout", h.Shell.Detail.Heading);
        Assert.Contains(h.Shell.Detail.Sections, section => section.Title == Strings.SectionIdentity);
        Assert.Empty(h.Focus.Requests);

        // Nothing pending: the pane is current already and is not rebuilt again. A redundant
        // in-place update of the same row would raise nothing either (the pane is updated in place,
        // fix 1 of the A16 measurement), so the input trace proves that no update ran at all.
        DetailPaneRecorder detailChanges = new(h.Shell.Detail);
        RecordingInputTrace trace = new();
        h.Shell.InputTrace = trace;
        h.Shell.EnsureDetailCurrent();
        Assert.Equal(2, scheduler.FlushCalls);
        Assert.Equal(1, scheduler.RefreshesRun);
        Assert.Empty(detailChanges.Events);
        Assert.Equal(0, RebuildsTraced(trace));
        Assert.Contains(trace.Lines, line => line.Details.StartsWith("flush requested by ", StringComparison.Ordinal));
        Assert.Same(timeout, h.Shell.Detail.Item);
    }

    [Fact]
    public async Task CopyValue_FlushesThePendingRefresh_SoItCopiesTheSelectedRowsValue()
    {
        RecordingDetailScheduler scheduler = new();
        Harness h = new(detailScheduler: scheduler);
        h.Live.Result = Loaded(ThreeVariables);
        await h.Shell.RefreshAsync();
        VariableItemViewModel first = SelectAndSettle(h, scheduler, h.Shell.List.VisibleItems[0]);
        VariableItemViewModel timeout = h.Shell.List.FindLoaded(Timeout.Key)!;
        Assert.NotSame(timeout, first);
        h.Shell.List.SelectedItem = timeout;
        string? staleText = h.Shell.Detail.CopyValueText;
        Assert.False(string.IsNullOrEmpty(staleText));

        h.Shell.CopyValueCommand.Execute(null);

        Assert.Equal(1, scheduler.FlushCalls);
        Assert.Same(timeout, h.Shell.Detail.Item);
        Assert.Equal(h.Shell.Detail.CopyValueText, h.Platform.LastClipboardText);
        Assert.NotEqual(staleText, h.Platform.LastClipboardText);
        Assert.Equal(Strings.CopiedValueStatus, h.Shell.StatusText);
    }

    [Fact]
    public async Task ExportVisibleList_FlushesThePendingRefresh_BeforeTheDialog()
    {
        RecordingDetailScheduler scheduler = new();
        Harness h = new(detailScheduler: scheduler);
        h.Live.Result = Loaded(ThreeVariables);
        await h.Shell.RefreshAsync();
        SelectAndSettle(h, scheduler, h.Shell.List.VisibleItems[0]);
        VariableItemViewModel timeout = h.Shell.List.FindLoaded(Timeout.Key)!;
        h.Shell.List.SelectedItem = timeout;
        Assert.True(scheduler.IsPending);

        // The dialog is cancelled; the pane was settled before it opened all the same.
        h.Shell.ExportVisibleListCommand.Execute(null);

        Assert.Equal(1, scheduler.FlushCalls);
        Assert.False(scheduler.IsPending);
        Assert.Same(timeout, h.Shell.Detail.Item);
        Assert.Equal(Strings.DialogCancelledStatus, h.Shell.StatusText);
    }

    [Fact]
    public async Task ALoad_ShowsThePaneAtOnce_AndLeavesNoRefreshPending()
    {
        // A load replaces the row objects and re-selects the remembered key; the pane must show
        // the new row before the Loaded sentence and the focus request, and the refresh that the
        // re-selection scheduled is the one that runs, not a second one on top of it.
        RecordingDetailScheduler scheduler = new();
        Harness h = new(detailScheduler: scheduler);
        h.Live.Result = Loaded(ThreeVariables);
        await h.Shell.RefreshAsync();
        Assert.Null(h.Shell.List.SelectedItem);
        Assert.Null(h.Shell.Detail.Item);
        Assert.False(scheduler.IsPending);
        Assert.Equal(0, scheduler.RefreshesRun);
        VariableItemViewModel timeout = SelectAndSettle(h, scheduler, h.Shell.List.FindLoaded(Timeout.Key)!);

        // One update of the pane: one change of Item and one traced rebuild (the Sections property
        // is never raised, the pane being updated in place; fix 1 of the A16 measurement).
        DetailPaneRecorder detailChanges = new(h.Shell.Detail);
        RecordingInputTrace trace = new();
        h.Shell.InputTrace = trace;

        await h.Shell.RefreshAsync();

        Assert.Equal(ShellState.Loaded, h.Shell.State);
        VariableItemViewModel reloaded = h.Shell.List.SelectedItem!;
        Assert.NotNull(reloaded);
        Assert.NotSame(timeout, reloaded);
        Assert.Equal(Timeout.Key, reloaded.Key);
        Assert.Same(reloaded, h.Shell.Detail.Item);
        Assert.False(scheduler.IsPending);
        Assert.Equal(1, scheduler.RefreshesRun);
        Assert.Equal(1, detailChanges.PaneProperties.Count(name => name == nameof(VariableDetailViewModel.Item)));
        Assert.DoesNotContain(nameof(VariableDetailViewModel.Sections), detailChanges.PaneProperties);
        Assert.Empty(detailChanges.ForbiddenCollectionChanges);
        Assert.Equal(1, RebuildsTraced(trace));
        Assert.Equal(new[] { FocusTarget.ListFirstRow }, h.Focus.Requests);

        // A refusal empties the pane at once as well.
        h.Live.Result = Refused(Strings.NotElevatedSentence);
        await h.Shell.RefreshAsync();

        Assert.Equal(ShellState.Refused, h.Shell.State);
        Assert.Null(h.Shell.List.SelectedItem);
        Assert.Null(h.Shell.Detail.Item);
        Assert.Equal(Strings.NoSelectionHeading, h.Shell.Detail.Heading);
        Assert.False(scheduler.IsPending);
        Assert.Equal(2, scheduler.RefreshesRun);
    }

    [Fact]
    public async Task AFilterThatHidesTheSelection_DefersTheEmptyPaneToo()
    {
        RecordingDetailScheduler scheduler = new();
        Harness h = new(detailScheduler: scheduler);
        h.Live.Result = Loaded(ThreeVariables);
        await h.Shell.RefreshAsync();
        VariableItemViewModel timeout = SelectAndSettle(h, scheduler, h.Shell.List.FindLoaded(Timeout.Key)!);

        h.Shell.List.FilterText = "AcpiGlobal";
        h.Shell.OnFilterApplied();

        Assert.Null(h.Shell.List.SelectedItem);
        Assert.Same(timeout, h.Shell.Detail.Item);
        Assert.True(scheduler.IsPending);
        Assert.True(scheduler.RunPending());
        Assert.Null(h.Shell.Detail.Item);
        Assert.Equal(Strings.NoSelectionHeading, h.Shell.Detail.Heading);
        Assert.Equal("Showing 1 of 3 variables", h.Shell.StatusText);
    }

    [Fact]
    public void WithoutAScheduler_ThePaneFollowsTheSelectionAtOnce()
    {
        // The default is the immediate scheduler: every existing assertion on Detail right after a
        // selection change, and every host without a message loop, sees the pane synchronously.
        Harness h = new();
        VariableItemViewModel item = Item(Timeout);

        h.Shell.List.SelectedItem = item;

        Assert.Same(item, h.Shell.Detail.Item);
        h.Shell.EnsureDetailCurrent();
        Assert.Same(item, h.Shell.Detail.Item);
    }

    // ---- change notification -----------------------------------------------------------------

    [Fact]
    public void Announcements_RaisePropertyChangedForStatusText()
    {
        Harness h = new();
        List<string> changed = Record(h.Shell);

        h.Shell.OnRegionArrived(Region.List);
        Assert.Equal(new[] { nameof(ShellViewModel.StatusText) }, changed);

        // A filter the view applied says the count (a call that changes no row says nothing, A2-9).
        changed.Clear();
        h.Shell.List.FilterText = "Boot";
        h.Shell.OnFilterApplied();
        Assert.Equal(new[] { nameof(ShellViewModel.StatusText) }, changed);

        changed.Clear();
        h.Shell.OnSplitterMoved(SplitterKind.ListAndDetails, 50, atLimit: false);
        Assert.Equal(new[] { nameof(ShellViewModel.StatusText) }, changed);
    }

    [Fact]
    public async Task ABaselineLoad_RaisesPropertyChangedForStateBusyTitleAndIdentity()
    {
        Harness h = new();
        h.Baseline.Result = Loaded(new[] { BootOrder }, BaselineDescription, isOffline: true);
        List<string> changed = Record(h.Shell);

        await h.Shell.OpenBaselineAsync(BaselineFolder);

        string[] expected =
        {
            nameof(ShellViewModel.IsBusy), nameof(ShellViewModel.State), nameof(ShellViewModel.StateText),
            nameof(ShellViewModel.StatusText), nameof(ShellViewModel.SourceDescription), nameof(ShellViewModel.IsOffline),
            nameof(ShellViewModel.WindowTitle), nameof(ShellViewModel.Manufacturer), nameof(ShellViewModel.Model),
            nameof(ShellViewModel.BiosVersion), nameof(ShellViewModel.ServiceTag), nameof(ShellViewModel.Cpu),
            nameof(ShellViewModel.OperatingSystem), nameof(ShellViewModel.ExpectedModelText),
        };
        foreach (string name in expected)
        {
            Assert.Contains(name, changed);
        }

        // IsBusy went true then false; State went Loading then Loaded.
        Assert.Equal(2, changed.Count(name => name == nameof(ShellViewModel.IsBusy)));
        Assert.Equal(2, changed.Count(name => name == nameof(ShellViewModel.State)));
        // The Loaded announcement comes after IsBusy dropped, so a listener hearing it may press Refresh at once.
        int lastBusy = changed.LastIndexOf(nameof(ShellViewModel.IsBusy));
        int lastStatus = changed.LastIndexOf(nameof(ShellViewModel.StatusText));
        Assert.True(lastBusy < lastStatus, "IsBusy must be false before the Loaded announcement");
    }

    // ---- composition -------------------------------------------------------------------------

    [Fact]
    public void CreateWindows_ComposesWithoutTouchingTheMachine()
    {
        RecordingAnnouncer announcer = new();
        RecordingFocusSink focus = new();

        ShellViewModel shell = ShellViewModel.CreateWindows(announcer, focus, new NullPlatformInteractions(), null);

        Assert.Equal(ShellState.Idle, shell.State);
        Assert.Equal("Live firmware", shell.SourceDescription);
        Assert.Equal("UefiAccess", shell.WindowTitle);
        Assert.Equal(string.Empty, announcer.Current);
        shell.AnnounceReady();
        Assert.Equal(Strings.IdleStatus, announcer.Current);
        Assert.Empty(focus.Requests);
    }

    [NonWindowsFact]
    public async Task CreateWindows_RefreshOnANonWindowsHost_IsRefusedAsNotUefi()
    {
        RecordingAnnouncer announcer = new();
        RecordingFocusSink focus = new();
        ShellViewModel shell = ShellViewModel.CreateWindows(announcer, focus, new NullPlatformInteractions(), null);

        await shell.RefreshAsync();

        Assert.Equal(ShellState.Refused, shell.State);
        Assert.Equal(Strings.NotUefiSentence, shell.StatusText);
        Assert.Equal(new[] { FocusTarget.Status }, focus.Requests);
    }

    // ---- helpers -----------------------------------------------------------------------------

    private static Task Execute(System.Windows.Input.ICommand command)
    {
        return Assert.IsType<AsyncRelayCommand>(command).ExecuteAsync();
    }

    /// <summary>The updates of the detail pane the input trace recorded: its "rebuild finished" and "direct rebuild finished" lines, one per Show.</summary>
    private static int RebuildsTraced(RecordingInputTrace trace)
    {
        return trace.Lines.Count(line => line.Kind == TraceKind.Detail && Regex.IsMatch(line.Details, @"^(?:direct )?rebuild finished in ", RegexOptions.CultureInvariant));
    }

    private static List<string> Record(INotifyPropertyChanged source)
    {
        List<string> changed = new();
        source.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);
        return changed;
    }

    private static void AssertIdentityPlaceholders(ShellViewModel shell, string placeholder)
    {
        Assert.Equal("Manufacturer: " + placeholder, shell.Manufacturer);
        Assert.Equal("Model: " + placeholder, shell.Model);
        Assert.Equal("BIOS version: " + placeholder, shell.BiosVersion);
        Assert.Equal("Service tag: " + placeholder, shell.ServiceTag);
        Assert.Equal("CPU: " + placeholder, shell.Cpu);
        Assert.Equal("Operating system: " + placeholder, shell.OperatingSystem);
        Assert.Equal("Expected model confirmed: " + placeholder, shell.ExpectedModelText);
    }

    /// <summary>
    /// What the window does after a load (it selects the row it focuses) and what the dispatcher
    /// does at its next idle turn: selects the row, runs the pending refresh, and forgets the
    /// scheduler's counts and the focus requests so the test starts from a shown row.
    /// </summary>
    private static VariableItemViewModel SelectAndSettle(Harness h, RecordingDetailScheduler scheduler, VariableItemViewModel row)
    {
        h.Shell.List.SelectedItem = row;
        Assert.True(scheduler.RunPending());
        Assert.Same(row, h.Shell.Detail.Item);
        scheduler.Clear();
        h.Focus.Clear();
        return row;
    }

    private static VariableItemViewModel Item(FirmwareVariable variable)
    {
        return new VariableItemViewModel(variable, NamespaceCatalog.Describe(variable.VendorGuid), null);
    }

    private static SourceResult Loaded(IReadOnlyList<FirmwareVariable> variables, string description = Strings.LiveSourceName, bool isOffline = false)
    {
        return new SourceResult(
            SourceStatus.Loaded,
            description,
            isOffline,
            FirmwareType.Uefi,
            PrivilegeState.Enabled,
            variables,
            EnumerationMethod.InMemory,
            Array.Empty<string>(),
            LiveFirmwareSourceTests.Identity(),
            null);
    }

    private static SourceResult Refused(string sentence)
    {
        return new SourceResult(
            SourceStatus.Refused,
            Strings.LiveSourceName,
            false,
            FirmwareType.Uefi,
            null,
            Array.Empty<FirmwareVariable>(),
            EnumerationMethod.None,
            Array.Empty<string>(),
            null,
            sentence);
    }

    private static SourceResult Failed(string message)
    {
        return new SourceResult(
            SourceStatus.Failed,
            Strings.LiveSourceName,
            false,
            FirmwareType.Uefi,
            PrivilegeState.Enabled,
            Array.Empty<FirmwareVariable>(),
            EnumerationMethod.None,
            Array.Empty<string>(),
            null,
            message);
    }

    // ---- fakes -------------------------------------------------------------------------------

    /// <summary>
    /// The shell with its recording announcer and focus sink, a null platform (or the one given),
    /// one fake source per role, and a Serilog logger mirrored into the shell's log panel exactly
    /// as App.xaml.cs composes it (Information and above, attached after construction), followed
    /// by the one <see cref="ShellViewModel.AnnounceReady"/> call the window's Loaded handler makes.
    /// </summary>
    private sealed class Harness
    {
        public Harness(IPlatformInteractions? platform = null, IDetailScheduler? detailScheduler = null)
        {
            Mirror = new PanelMirrorSink();
            Logger logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(Mirror).CreateLogger();
            Shell = new ShellViewModel(
                Live,
                folder =>
                {
                    FactoryFolders.Add(folder);
                    return Baseline;
                },
                Announcer,
                Focus,
                platform ?? Platform,
                logger,
                detailScheduler);
            Mirror.Attach(Shell.Log);
            Shell.AnnounceReady();
        }

        public RecordingAnnouncer Announcer { get; } = new();

        public RecordingFocusSink Focus { get; } = new();

        public NullPlatformInteractions Platform { get; } = new();

        public PanelMirrorSink Mirror { get; }

        public FakeSource Live { get; } = new();

        public FakeSource Baseline { get; } = new(BaselineDescription, isOffline: true);

        public List<string> FactoryFolders { get; } = new();

        public ShellViewModel Shell { get; }
    }

    /// <summary>
    /// A source that returns a configured result, throws synchronously, faults its task, or waits
    /// on a gate so a test can observe the Loading state.
    /// </summary>
    private sealed class FakeSource : IVariableSource
    {
        public FakeSource(string description = Strings.LiveSourceName, bool isOffline = false)
        {
            Description = description;
            IsOffline = isOffline;
        }

        public string Description { get; }

        public bool IsOffline { get; }

        public SourceResult? Result { get; set; }

        public Exception? Throws { get; set; }

        public Exception? FaultsAsync { get; set; }

        public TaskCompletionSource<SourceResult>? Gate { get; set; }

        public int LoadCalls { get; private set; }

        public Task<SourceResult> LoadAsync(CancellationToken cancellationToken)
        {
            LoadCalls++;
            if (Throws is not null)
            {
                throw Throws;
            }

            if (FaultsAsync is not null)
            {
                return Task.FromException<SourceResult>(FaultsAsync);
            }

            if (Gate is not null)
            {
                return Gate.Task;
            }

            return Task.FromResult(Result!);
        }
    }

    /// <summary>A platform whose dialogs and clipboard throw, to exercise the command error paths.</summary>
    private sealed class ThrowingPlatform : IPlatformInteractions
    {
        public string? PickFolder(string title) => throw new InvalidOperationException("dialog exploded");

        public string? PickSaveFile(string title, string defaultFileName, string filter) => throw new InvalidOperationException("dialog exploded");

        public void SetClipboardText(string text) => throw new InvalidOperationException("clipboard exploded");

        public ConfirmDecision ConfirmChange(ConfirmChangeViewModel viewModel) => throw new InvalidOperationException("dialog exploded");
    }

    /// <summary>
    /// The application's LogPanelSink without the dispatcher: mirrors Serilog events into the log
    /// panel by the same <see cref="LogPanelMirror"/> rule (Information and above, not file-only),
    /// rendered as the log file renders them, with an attached exception as " (Type: message)".
    /// </summary>
    private sealed class PanelMirrorSink : ILogEventSink
    {
        private LogViewModel? _target;

        /// <summary>Events received at any level.</summary>
        public int Received { get; private set; }

        /// <summary>Events that were added to the panel.</summary>
        public int Mirrored { get; private set; }

        public void Attach(LogViewModel target) => _target = target;

        public void Emit(LogEvent logEvent)
        {
            Received++;
            LogLineViewModel? line = LogPanelMirror.ToLine(logEvent);
            if (line is null || _target is null)
            {
                return;
            }

            Mirrored++;
            _target.Add(line);
        }
    }
}
