// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Baseline;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Privileges;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Settings.Dell;
using UefiAccess.Core.Tests.Safety;
using UefiAccess.Core.Variables;
using UefiAccess.Presentation.Accessibility;
using UefiAccess.Presentation.Infrastructure;
using UefiAccess.Presentation.Resources;
using UefiAccess.Presentation.Services;
using UefiAccess.Presentation.ViewModels;
using Xunit;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// The shell's Read Dell settings (Alt+S) and the Dell settings region, over a <see cref="GatedDellSource"/> the test answers: nothing reads the Dell
/// settings at start-up, on Refresh or on Open baseline folder; Alt+S says "Loading the Dell BIOS
/// settings, please wait." once and the answer once, with focus on the selected Dell row after a
/// success and on the Status box otherwise; the refusals of section 5.6 in their order; the command
/// is never disabled; the quiet rule's two parts (a load, a dialog or the checks of a change started
/// after Alt+S: nothing said, no focus request, one WARN line for a failure; the person moved on:
/// said, no focus request); a baseline folder shows its own Dell settings and Refresh brings the
/// session cache back without a read; the arrival clause; Ctrl+C and Alt+Y on a Dell row; each
/// step of the M4-H4.14 sequence says its own sentences once. Nothing here reads a Dell interface
/// or the firmware of this PC.
/// </summary>
public sealed class ShellDellTests
{
    private const string BaselineFolderName = "optiplex-20260922";
    private const string LoadedA18 = "Loaded 10 Dell BIOS settings from Dell Command Monitor.";
    private const string NumLockSentence = "Num Lock at boot: Enabled. Possible values: Disabled, Enabled.";
    private const string UnexpectedSentence = "Dell BIOS settings cannot be read. An unexpected error occurred; the log has the details. Try again; if it fails again, restart Windows and try again.";

    [Fact]
    public async Task NothingReadsDellAtStartUp()
    {
        ShellTestRig rig = new();

        Assert.Equal(0, rig.Dell.ReadCalls);
        Assert.Equal(Strings.IdleStatus, rig.Shell.StatusText);
        Assert.Equal(new[] { Strings.IdleStatus }, rig.Announcer.History);
        Assert.Empty(rig.Focus.Requests);
        Assert.Equal(Strings.DellNotLoadedLine, rig.Shell.Dell.StateText);
        Assert.All(rig.Shell.Dell.Rows, row => Assert.Equal(Strings.DellNotLoadedYetValue, row.CurrentText));
        Assert.Same(rig.Shell.Dell.Rows[0], rig.Shell.Dell.SelectedRow);
        Assert.False(rig.Shell.Dell.IsLoading);

        // A shell composed without a source reads nothing either; asked, it says the settings cannot be read.
        RecordingAnnouncer announcer = new();
        ShellViewModel bare = new(rig.Live, _ => rig.Baseline, announcer, new RecordingFocusSink(), new NullPlatformInteractions());
        await bare.ReadDellSettingsAsync();
        Assert.Equal(new[] { Strings.DellLoadingStatus, UnexpectedSentence }, announcer.History);
    }

    [Fact]
    public async Task AltS_AnnouncesLoadingOnce_ThenTheResult()
    {
        ShellTestRig rig = new();
        rig.Dell.Gate = new TaskCompletionSource<DellSettingsResult>();
        int before = rig.Announcer.History.Count;

        Task read = rig.Shell.ReadDellSettingsAsync();

        Assert.False(read.IsCompleted);
        Assert.True(rig.Shell.Dell.IsLoading);
        Assert.Equal(new[] { Strings.DellLoadingStatus }, rig.Announcer.History.Skip(before));
        Assert.Equal("Loading the Dell BIOS settings, please wait.", rig.Shell.StatusText);
        Assert.Empty(rig.Focus.Requests);

        rig.Dell.Gate.SetResult(DellResults.CompleteA18());
        await read;

        Assert.False(rig.Shell.Dell.IsLoading);
        Assert.Equal(1, rig.Dell.ReadCalls);
        Assert.Equal(new[] { Strings.DellLoadingStatus, LoadedA18 }, rig.Announcer.History.Skip(before));
        Assert.Equal(LoadedA18, rig.Shell.StatusText);
        Assert.Equal(new[] { (LogViewModel.InfoLevel, Strings.DellLoadingStatus), (LogViewModel.InfoLevel, LoadedA18) }, rig.Lines.Skip(rig.Lines.Length - 2));
        Assert.DoesNotContain(rig.Lines, line => line.Level != LogViewModel.InfoLevel);
        Assert.Equal(NumLockSentence, rig.Shell.Dell.Rows[9].AccessibleName);

        // The variables' state is untouched: a Dell read is not a load.
        Assert.Equal(ShellState.Idle, rig.Shell.State);
    }

    [Fact]
    public async Task Success_FocusesTheSelectedDellRow()
    {
        ShellTestRig rig = new();
        rig.Dell.Result = DellResults.CompleteA18();

        await rig.Shell.ReadDellSettingsAsync();

        Assert.Equal(new[] { FocusTarget.DellSelectedRow }, rig.Focus.Requests);

        // A later read says whether anything changed, and asks for the selected row again (the
        // window raises no focus event when focus is already there).
        int before = rig.Announcer.History.Count;
        await rig.Shell.ReadDellSettingsAsync();
        rig.Dell.Result = DellResults.NumLockDisabled(DateTimeOffset.Now);
        await rig.Shell.ReadDellSettingsAsync();

        Assert.Equal(
            new[]
            {
                Strings.DellLoadingStatus,
                "Loaded the Dell BIOS settings again: nothing changed.",
                Strings.DellLoadingStatus,
                "Loaded the Dell BIOS settings again; changed: Num Lock at boot, now Disabled.",
            },
            rig.Announcer.History.Skip(before));
        Assert.Equal(new[] { FocusTarget.DellSelectedRow, FocusTarget.DellSelectedRow, FocusTarget.DellSelectedRow }, rig.Focus.Requests);
    }

    [Fact]
    public async Task Unavailable_FocusesStatus_WithTheCoreSentence()
    {
        ShellTestRig rig = new();
        DellSettingsResult unavailable = DellResults.NothingInstalled();
        rig.Dell.Result = unavailable;

        await rig.Shell.ReadDellSettingsAsync();

        Assert.Equal(DellSentences.UnavailableSentence(unavailable), rig.Shell.StatusText);
        Assert.Equal(DellResults.NothingInstalledSentence, rig.Shell.StatusText);
        Assert.Equal(new[] { FocusTarget.Status }, rig.Focus.Requests);
        Assert.Equal("Boot list option: not loaded.", rig.Shell.Dell.Rows[0].AccessibleName);
        Assert.Equal(DellResults.NothingInstalledSentence, rig.Shell.Dell.StateText);

        // Said once, as the Status box's INFO line; no second channel for the same failure.
        Assert.Single(rig.Lines, line => line.Message == DellResults.NothingInstalledSentence);
        Assert.DoesNotContain(rig.Lines, line => line.Level == LogViewModel.WarnLevel);

        // After a read that answered, the earlier values are kept and Status says when they were loaded.
        DellSettingsResult a18 = DellResults.CompleteA18();
        rig.Dell.Result = a18;
        await rig.Shell.ReadDellSettingsAsync();
        rig.Dell.Result = unavailable;
        rig.Focus.Clear();
        await rig.Shell.ReadDellSettingsAsync();

        Assert.Equal(DellResults.NothingInstalledSentence + " The list still shows the values loaded at " + DellResults.LiveTime(a18) + ".", rig.Shell.StatusText);
        Assert.Equal(rig.Shell.StatusText, rig.Shell.Dell.StateText);
        Assert.Equal(new[] { FocusTarget.Status }, rig.Focus.Requests);
        Assert.Equal(NumLockSentence, rig.Shell.Dell.Rows[9].AccessibleName);
    }

    [Fact]
    public async Task Fallback_SentenceFollowsTheLoadedSentence()
    {
        ShellTestRig rig = new();
        rig.Dell.Result = DellResults.Fallback();

        await rig.Shell.ReadDellSettingsAsync();

        Assert.Equal(
            "Loaded 9 of 10 Dell BIOS settings from Dell Command Configure; not reported: Boot list option. Dell Command Monitor is not installed. Dell Command Configure was used instead.",
            rig.Shell.StatusText);
        Assert.Equal(new[] { FocusTarget.DellSelectedRow }, rig.Focus.Requests);
        Assert.Equal("Boot list option: not reported. This version reads it through Dell Command Monitor only.", rig.Shell.Dell.Rows[0].AccessibleName);
        Assert.EndsWith(Strings.DellFallbackLineSuffix, rig.Shell.Dell.StateText, StringComparison.Ordinal);

        // No extra WARN line for the fallback: Status said it.
        Assert.DoesNotContain(rig.Lines, line => line.Level == LogViewModel.WarnLevel);
    }

    [Fact]
    public async Task AltS_WhileVariablesLoad_IsRefused()
    {
        ShellTestRig rig = new();
        rig.Live.Gate = new TaskCompletionSource<SourceResult>();
        Task refresh = rig.Shell.RefreshAsync();
        rig.Focus.Clear();
        int before = rig.Announcer.History.Count;

        await rig.Shell.ReadDellSettingsAsync();

        Assert.Equal(new[] { "Wait for the variables to finish loading, then press Read Dell settings (Alt+S) again." }, rig.Announcer.History.Skip(before));
        Assert.Equal(0, rig.Dell.ReadCalls);
        Assert.Empty(rig.Focus.Requests);
        Assert.False(rig.Shell.Dell.IsLoading);

        rig.Live.Gate.SetResult(ShellTestRig.Loaded(new[] { ShellTestRig.BootOrder }));
        await refresh;
        Assert.Equal(0, rig.Dell.ReadCalls);
    }

    [Fact]
    public async Task AltS_WhileLoading_SaysStillLoading()
    {
        ShellTestRig rig = new();
        rig.Dell.Gate = new TaskCompletionSource<DellSettingsResult>();
        Task first = rig.Shell.ReadDellSettingsAsync();
        int before = rig.Announcer.History.Count;

        await rig.Shell.ReadDellSettingsAsync();

        Assert.Equal(new[] { "The Dell BIOS settings are still loading; please wait." }, rig.Announcer.History.Skip(before));
        Assert.Equal(1, rig.Dell.ReadCalls);
        Assert.Empty(rig.Focus.Requests);

        rig.Dell.Gate.SetResult(DellResults.CompleteA18());
        await first;
        Assert.Equal(new[] { Strings.DellAlreadyLoadingStatus, LoadedA18 }, rig.Announcer.History.Skip(before));
        Assert.Equal(new[] { FocusTarget.DellSelectedRow }, rig.Focus.Requests);
    }

    [Fact]
    public async Task AltS_WhileAChangeIsCheckedOrWritten_IsRefused()
    {
        using ChangeRig rig = new();
        await rig.LoadBootOrderAndMoveUsbUp();

        // While the checks run.
        rig.Writes.PrepareGate = new TaskCompletionSource<PrepareResult?>();
        Task checks = Execute(rig.Shell.Detail.Change.ReviewChangeCommand);
        Assert.True(rig.Shell.Detail.Change.IsChecking);
        int before = rig.Announcer.History.Count;
        rig.Focus.Clear();
        await rig.Shell.ReadDellSettingsAsync();
        Assert.Equal(new[] { Strings.DellWaitForChangeStatus }, rig.Announcer.History.Skip(before));
        Assert.Equal("Wait for the change to finish, then press Read Dell settings (Alt+S) again.", rig.Shell.StatusText);
        rig.Writes.PrepareGate.SetResult(null);
        await checks;
        Assert.Equal(Strings.DialogCancelledStatus, rig.Shell.StatusText);

        // While the confirmed change is written, and until its result is said after the reload.
        rig.Writes.PrepareGate = null;
        rig.Writes.CommitGate = new TaskCompletionSource<TransactionResult>();
        rig.Platform.ConfirmAnswer = dialog => dialog.Confirm();
        Task change = Execute(rig.Shell.Detail.Change.ReviewChangeCommand);
        Assert.True(rig.Shell.IsWriting);
        before = rig.Announcer.History.Count;
        await rig.Shell.ReadDellSettingsAsync();
        Assert.Equal(new[] { Strings.DellWaitForChangeStatus }, rig.Announcer.History.Skip(before));

        rig.Live.Gate = new TaskCompletionSource<SourceResult>();
        rig.Writes.ReleaseCommit();
        Assert.False(rig.Shell.IsWriting);
        Assert.True(rig.Shell.IsFinishingChange);
        before = rig.Announcer.History.Count;
        await rig.Shell.ReadDellSettingsAsync();
        Assert.Equal(new[] { Strings.DellWaitForChangeStatus }, rig.Announcer.History.Skip(before));

        rig.Live.Gate.SetResult(rig.Current());
        await change;
        Assert.False(rig.Shell.IsFinishingChange);
        Assert.Equal(0, rig.Dell.ReadCalls);
        Assert.Single(rig.Transaction.Writer.Calls);
    }

    [Fact]
    public async Task AltS_WhileABaselineIsShown_SaysPressRefreshFirst()
    {
        ShellTestRig rig = new();
        rig.Baseline.Result = Baseline(null);
        await rig.Shell.OpenBaselineAsync(ShellTestRig.BaselineFolder);
        rig.Focus.Clear();
        int before = rig.Announcer.History.Count;

        await rig.Shell.ReadDellSettingsAsync();

        Assert.Equal(
            new[] { "The window shows a baseline folder, and Dell BIOS settings are loaded from this computer only. Press Refresh (Alt+R) first, then Read Dell settings (Alt+S)." },
            rig.Announcer.History.Skip(before));
        Assert.Equal(0, rig.Dell.ReadCalls);
        Assert.Empty(rig.Focus.Requests);

        // Also after a baseline load that failed: the window shows no live values then either.
        rig.Baseline.Result = ShellTestRig.Loaded(Array.Empty<FirmwareVariable>(), ShellTestRig.BaselineDescription, isOffline: true) with { Status = SourceStatus.Failed, Message = "index.json is missing" };
        await rig.Shell.OpenBaselineAsync(ShellTestRig.BaselineFolder);
        await rig.Shell.ReadDellSettingsAsync();
        Assert.Equal(Strings.DellBaselineShownStatus, rig.Shell.StatusText);
        Assert.Equal(0, rig.Dell.ReadCalls);
    }

    [Fact]
    public async Task AnswerAfterARefresh_IsAppliedQuietly()
    {
        ShellTestRig rig = new();
        rig.Live.Result = ShellTestRig.Loaded(new[] { ShellTestRig.BootOrder, ShellTestRig.Timeout });
        rig.Dell.Gate = new TaskCompletionSource<DellSettingsResult>();
        Task read = rig.Shell.ReadDellSettingsAsync();
        await rig.Shell.RefreshAsync();
        string loaded = rig.Shell.StatusText;
        int before = rig.Announcer.History.Count;
        rig.Focus.Clear();

        rig.Dell.Gate.SetResult(DellResults.CompleteA18());
        await read;

        // The load's sentence and focus stand; the rows and the state line took the answer.
        Assert.StartsWith("Loaded 2 variables from Live firmware", loaded, StringComparison.Ordinal);
        Assert.Equal(before, rig.Announcer.History.Count);
        Assert.Equal(loaded, rig.Shell.StatusText);
        Assert.Empty(rig.Focus.Requests);
        Assert.Equal(NumLockSentence, rig.Shell.Dell.Rows[9].AccessibleName);
        Assert.StartsWith("Loaded at ", rig.Shell.Dell.StateText, StringComparison.Ordinal);
        Assert.Contains(rig.Events.Snapshot(), e => e.Level == Serilog.Events.LogEventLevel.Debug && e.RenderMessage().Contains("taken quietly", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Lines, line => line.Level == LogViewModel.WarnLevel);

        // The next read is said again.
        rig.Dell.Gate = null;
        rig.Dell.Result = DellResults.CompleteA18();
        await rig.Shell.ReadDellSettingsAsync();
        Assert.Equal("Loaded the Dell BIOS settings again: nothing changed.", rig.Shell.StatusText);
        Assert.Equal(new[] { FocusTarget.DellSelectedRow }, rig.Focus.Requests);
    }

    [Fact]
    public async Task AnswerAfterADialog_IsAppliedQuietly()
    {
        ShellTestRig rig = new();
        rig.Dell.Gate = new TaskCompletionSource<DellSettingsResult>();
        Task read = rig.Shell.ReadDellSettingsAsync();
        rig.Platform.FileToReturn = null;
        rig.Shell.ExportVisibleListCommand.Execute(null);
        Assert.Equal(Strings.DialogCancelledStatus, rig.Shell.StatusText);
        int before = rig.Announcer.History.Count;

        rig.Dell.Gate.SetResult(DellResults.CompleteA18());
        await read;

        Assert.Equal(before, rig.Announcer.History.Count);
        Assert.Equal(Strings.DialogCancelledStatus, rig.Shell.StatusText);
        Assert.Empty(rig.Focus.Requests);
        Assert.Equal(NumLockSentence, rig.Shell.Dell.Rows[9].AccessibleName);
    }

    [Fact]
    public async Task AnswerAfterTheChecksStarted_IsAppliedQuietly()
    {
        using ChangeRig rig = new();
        await rig.LoadBootOrderAndMoveUsbUp();
        rig.Dell.Gate = new TaskCompletionSource<DellSettingsResult>();
        Task read = rig.Shell.ReadDellSettingsAsync();
        rig.Writes.PrepareGate = new TaskCompletionSource<PrepareResult?>();
        Task checks = Execute(rig.Shell.Detail.Change.ReviewChangeCommand);
        Assert.Equal(Strings.CheckingStatus, rig.Shell.StatusText);
        int before = rig.Announcer.History.Count;
        rig.Focus.Clear();

        rig.Dell.Gate.SetResult(DellResults.CompleteA18());
        await read;

        Assert.Equal(before, rig.Announcer.History.Count);
        Assert.Equal(Strings.CheckingStatus, rig.Shell.StatusText);
        Assert.Empty(rig.Focus.Requests);
        Assert.Equal(NumLockSentence, rig.Shell.Dell.Rows[9].AccessibleName);

        rig.Writes.PrepareGate.SetResult(null);
        await checks;
        Assert.Equal(Strings.DialogCancelledStatus, rig.Shell.StatusText);
    }

    [Fact]
    public async Task AnswerWhileABaselineIsShown_GoesToTheCacheOnly()
    {
        ShellTestRig rig = new();
        rig.Live.Result = ShellTestRig.Loaded(new[] { ShellTestRig.BootOrder });
        rig.Baseline.Result = Baseline(null);
        rig.Dell.Gate = new TaskCompletionSource<DellSettingsResult>();
        Task read = rig.Shell.ReadDellSettingsAsync();
        await rig.Shell.OpenBaselineAsync(ShellTestRig.BaselineFolder);
        string loaded = rig.Shell.StatusText;
        int before = rig.Announcer.History.Count;
        rig.Focus.Clear();

        rig.Dell.Gate.SetResult(DellResults.CompleteA18());
        await read;

        Assert.Equal(before, rig.Announcer.History.Count);
        Assert.Equal(loaded, rig.Shell.StatusText);
        Assert.Empty(rig.Focus.Requests);
        Assert.Equal(DellViewMode.Baseline, rig.Shell.Dell.Mode);
        Assert.All(rig.Shell.Dell.Rows, row => Assert.Equal(Strings.DellNotRecordedValue, row.CurrentText));
        Assert.StartsWith("Baseline folder " + BaselineFolderName + " holds no Dell BIOS settings", rig.Shell.Dell.StateText, StringComparison.Ordinal);

        // Refresh brings the live view back, from the cache.
        await rig.Shell.RefreshAsync();
        Assert.Equal(DellViewMode.Live, rig.Shell.Dell.Mode);
        Assert.Equal(NumLockSentence, rig.Shell.Dell.Rows[9].AccessibleName);
        Assert.Equal(1, rig.Dell.ReadCalls);
    }

    [Fact]
    public async Task OpenBaseline_ShowsItsDellSettings_WithoutAnExtraSentence()
    {
        ShellTestRig rig = new();
        DellSettingsResult recorded = DellResults.NumLockDisabled(new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero)) with { Machine = new DellMachine("Dell Inc.", "OptiPlex 7020", "A18") };
        rig.Baseline.Result = Baseline(new DellSettingsFileResult(true, recorded, null));
        int before = rig.Announcer.History.Count;

        await rig.Shell.OpenBaselineAsync(ShellTestRig.BaselineFolder);

        string[] said = rig.Announcer.History.Skip(before).ToArray();
        Assert.Equal(2, said.Length);
        Assert.Equal(Strings.LoadingStatus, said[0]);
        Assert.StartsWith("Loaded 1 variable from " + ShellTestRig.BaselineDescription, said[1], StringComparison.Ordinal);
        Assert.Equal(new[] { FocusTarget.ListFirstRow }, rig.Focus.Requests);
        Assert.Equal(0, rig.Dell.ReadCalls);
        Assert.Equal(DellViewMode.Baseline, rig.Shell.Dell.Mode);
        Assert.Equal("Num Lock at boot: Disabled. Possible values: Disabled, Enabled.", rig.Shell.Dell.Rows[9].AccessibleName);
        Assert.Equal(
            "Recorded at 15 September 2026 12:00 UTC from Dell Command Monitor in baseline folder " + BaselineFolderName + ", BIOS A18. Offline: nothing is loaded from this computer.",
            rig.Shell.Dell.StateText);
    }

    [Fact]
    public async Task Refresh_AfterBaseline_ShowsTheCache_WithoutReading()
    {
        ShellTestRig rig = new();
        rig.Live.Result = ShellTestRig.Loaded(new[] { ShellTestRig.BootOrder });
        rig.Dell.Result = DellResults.CompleteA18();
        await rig.Shell.ReadDellSettingsAsync();
        rig.Baseline.Result = Baseline(new DellSettingsFileResult(true, DellResults.NumLockDisabled(DateTimeOffset.Now), null));
        await rig.Shell.OpenBaselineAsync(ShellTestRig.BaselineFolder);
        Assert.Equal("Num Lock at boot: Disabled. Possible values: Disabled, Enabled.", rig.Shell.Dell.Rows[9].AccessibleName);
        int before = rig.Announcer.History.Count;

        await rig.Shell.RefreshAsync();

        Assert.Equal(1, rig.Dell.ReadCalls);
        Assert.Equal(DellViewMode.Live, rig.Shell.Dell.Mode);
        Assert.Equal(NumLockSentence, rig.Shell.Dell.Rows[9].AccessibleName);
        Assert.StartsWith("Loaded at " + DellResults.LiveTime(DellResults.CompleteA18()) + " from Dell Command Monitor", rig.Shell.Dell.StateText, StringComparison.Ordinal);
        string[] said = rig.Announcer.History.Skip(before).ToArray();
        Assert.Equal(2, said.Length);
        Assert.Equal(Strings.LoadingStatus, said[0]);
        Assert.StartsWith("Loaded 1 variable from Live firmware", said[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refresh_DoesNotReadDell()
    {
        ShellTestRig rig = new();
        rig.Live.Result = ShellTestRig.Loaded(new[] { ShellTestRig.BootOrder });

        await rig.Shell.RefreshAsync();
        await rig.Shell.RefreshAsync();
        rig.Live.Result = ShellTestRig.Refused(Strings.NotElevatedSentence);
        await rig.Shell.RefreshAsync();

        Assert.Equal(0, rig.Dell.ReadCalls);
        Assert.Equal(DellViewMode.Live, rig.Shell.Dell.Mode);
        Assert.All(rig.Shell.Dell.Rows, row => Assert.Equal(Strings.DellNotLoadedYetValue, row.CurrentText));
        Assert.Equal(Strings.DellNotLoadedLine, rig.Shell.Dell.StateText);
        Assert.DoesNotContain(rig.Announcer.History, sentence => sentence.Contains("Dell", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RegionArrival_Dell_SaysTheClauseOnlyForABaselineOrAFailure()
    {
        // Before the first read, and after any read that answered, cached or not: the bare name.
        ShellTestRig rig = new();
        rig.Shell.OnRegionArrived(Region.Dell);
        Assert.Equal("Dell settings region", rig.Shell.StatusText);
        rig.Dell.Result = DellResults.CompleteA18();
        await rig.Shell.ReadDellSettingsAsync();
        rig.Shell.OnRegionArrived(Region.Dell);
        Assert.Equal("Dell settings region", rig.Shell.StatusText);
        rig.Dell.Result = DellResults.NothingInstalled();
        await rig.Shell.ReadDellSettingsAsync();
        rig.Shell.OnRegionArrived(Region.Dell);
        Assert.Equal("Dell settings region", rig.Shell.StatusText);

        // A baseline folder shown.
        rig.Baseline.Result = Baseline(null);
        await rig.Shell.OpenBaselineAsync(ShellTestRig.BaselineFolder);
        rig.Focus.Clear();
        rig.Shell.OnRegionArrived(Region.Dell);
        Assert.Equal("Dell settings region, from baseline folder " + BaselineFolderName, rig.Shell.StatusText);
        Assert.Empty(rig.Focus.Requests);

        // The last live read gave no answer and nothing is cached.
        ShellTestRig failed = new();
        failed.Dell.Result = DellResults.NothingInstalled();
        await failed.Shell.ReadDellSettingsAsync();
        failed.Shell.OnRegionArrived(Region.Dell);
        Assert.Equal("Dell settings region, could not be loaded", failed.Shell.StatusText);

        // The other regions are as before, and the Dell region's name is its constant.
        failed.Shell.OnRegionArrived(Region.Log);
        Assert.Equal("Log region", failed.Shell.StatusText);
        Assert.Equal(Strings.RegionDell, RegionOrder.Name(Region.Dell));
    }

    [Fact]
    public async Task CopyDellRow_CopiesTheSentenceSilently()
    {
        ShellTestRig rig = new();
        rig.Dell.Result = DellResults.CompleteA18();
        await rig.Shell.ReadDellSettingsAsync();
        int before = rig.Announcer.History.Count;
        rig.Focus.Clear();

        rig.Shell.CopyDellRow(rig.Shell.Dell.Rows[9]);

        Assert.Equal(NumLockSentence, rig.Platform.LastClipboardText);
        Assert.Equal(before, rig.Announcer.History.Count);
        Assert.Empty(rig.Focus.Requests);

        rig.Shell.CopyDellRow(null);
        Assert.Equal(NumLockSentence, rig.Platform.LastClipboardText);
        Assert.Equal(before, rig.Announcer.History.Count);
    }

    [Fact]
    public void CopyDellRow_Failure_SaysCopyFailed()
    {
        RecordingAnnouncer announcer = new();
        RecordingFocusSink focus = new();
        ShellViewModel shell = new(new GatedSource(Strings.LiveSourceName, isOffline: false), _ => throw new InvalidOperationException("no baseline"), announcer, focus, new ThrowingClipboard());

        shell.CopyDellRow(shell.Dell.Rows[0]);

        Assert.Equal(new[] { "Copy failed: The clipboard is held by another process." }, announcer.History);
        Assert.Empty(focus.Requests);
    }

    [Fact]
    public async Task AltY_InTheDellRegion_CopiesTheDellRowNotAVariable()
    {
        ShellTestRig rig = new();
        rig.Live.Result = ShellTestRig.Loaded(new[] { ShellTestRig.BootOrder, ShellTestRig.Timeout });
        await rig.Shell.RefreshAsync();
        rig.Dell.Result = DellResults.CompleteA18();
        await rig.Shell.ReadDellSettingsAsync();
        rig.Shell.Dell.SelectedRow = rig.Shell.Dell.Rows[9];

        rig.Shell.OnDellFocus(DellFocusPart.List);
        rig.Shell.CopyValueCommand.Execute(null);
        Assert.Equal(NumLockSentence, rig.Platform.LastClipboardText);
        Assert.Equal(Strings.CopiedValueStatus, rig.Shell.StatusText);

        rig.Shell.OnDellFocus(DellFocusPart.Button);
        rig.Shell.Dell.SelectedRow = rig.Shell.Dell.Rows[8];
        rig.Shell.CopyValueCommand.Execute(null);
        Assert.Equal("Wake on LAN: Disabled. Possible values: Disabled, LAN Only, LAN with PXE Boot.", rig.Platform.LastClipboardText);

        rig.Shell.OnDellFocus(DellFocusPart.SourceLine);
        rig.Shell.CopyValueCommand.Execute(null);
        Assert.Equal(rig.Shell.Dell.StateText, rig.Platform.LastClipboardText);
        Assert.StartsWith("Loaded at ", rig.Platform.LastClipboardText, StringComparison.Ordinal);
        Assert.Equal(Strings.CopiedValueStatus, rig.Shell.StatusText);

        // Out of the region, Alt+Y copies the variable again, as before.
        rig.Shell.List.SelectedItem = rig.Shell.List.AllItems.Single(item => item.Name == "Timeout");
        rig.Shell.OnDellFocus(DellFocusPart.None);
        rig.Shell.CopyValueCommand.Execute(null);
        string? copied = rig.Platform.LastClipboardText;
        string? variableText = string.IsNullOrEmpty(rig.Shell.Detail.CopyValueText) ? rig.Shell.List.SelectedItem?.Summary : rig.Shell.Detail.CopyValueText;
        Assert.False(string.IsNullOrEmpty(variableText));
        Assert.Equal(variableText, copied);
        Assert.DoesNotContain("Dell", copied, StringComparison.Ordinal);
        Assert.Equal(Strings.CopiedValueStatus, rig.Shell.StatusText);
    }

    [Fact]
    public async Task ReadCommand_IsNeverDisabled()
    {
        ShellTestRig rig = new();
        Assert.IsType<RelayCommand>(rig.Shell.ReadDellSettingsCommand);
        int requeries = 0;
        rig.Shell.ReadDellSettingsCommand.CanExecuteChanged += (_, _) => requeries++;
        Assert.True(rig.Shell.ReadDellSettingsCommand.CanExecute(null));
        rig.Dell.Gate = new TaskCompletionSource<DellSettingsResult>();
        int before = rig.Announcer.History.Count;

        rig.Shell.ReadDellSettingsCommand.Execute(null);

        Assert.True(rig.Shell.Dell.IsLoading);
        Assert.True(rig.Shell.ReadDellSettingsCommand.CanExecute(null));
        rig.Shell.ReadDellSettingsCommand.Execute(null);
        Assert.Equal(new[] { Strings.DellLoadingStatus, Strings.DellAlreadyLoadingStatus }, rig.Announcer.History.Skip(before));
        Assert.Single(rig.Announcer.History, sentence => sentence == Strings.DellAlreadyLoadingStatus);
        Assert.Equal(1, rig.Dell.ReadCalls);

        rig.Dell.Gate.SetResult(DellResults.CompleteA18());
        await Task.Yield();

        Assert.False(rig.Shell.Dell.IsLoading);
        Assert.Equal(LoadedA18, rig.Shell.StatusText);
        Assert.True(rig.Shell.ReadDellSettingsCommand.CanExecute(null));
        Assert.Equal(0, requeries);

        // Neither a load, a write lock nor a baseline disables it: it answers with a sentence.
        rig.Live.Gate = new TaskCompletionSource<SourceResult>();
        Task refresh = rig.Shell.RefreshAsync();
        Assert.True(rig.Shell.ReadDellSettingsCommand.CanExecute(null));
        rig.Shell.ReadDellSettingsCommand.Execute(null);
        Assert.Equal(Strings.DellWaitForVariablesStatus, rig.Shell.StatusText);
        rig.Live.Gate.SetResult(ShellTestRig.Loaded(new[] { ShellTestRig.BootOrder }));
        await refresh;
    }

    [Fact]
    public async Task AnswerAfterF6OrTyping_IsAnnouncedWithoutAFocusMove()
    {
        // F6 or Tab to another region.
        await AssertAnnouncedWithoutFocus(rig => rig.Shell.OnRegionArrived(Region.Log), "Log region");
        await AssertAnnouncedWithoutFocus(rig => rig.Shell.OnRegionArrived(Region.Status), null);

        // Typing in the filter box.
        await AssertAnnouncedWithoutFocus(
            rig =>
            {
                rig.Shell.List.FilterText = "Acpi";
                rig.Shell.OnFilterApplied();
            },
            "Showing 1 of 3 variables");

        // A move in the variable list, and a move into the detail pane.
        await AssertAnnouncedWithoutFocus(rig => rig.Shell.List.SelectedItem = rig.Shell.List.AllItems.Single(item => item.Name == "Timeout"), null);
        await AssertAnnouncedWithoutFocus(rig => rig.Shell.EnsureDetailCurrent("FocusFirstDetailField"), null);
        await AssertAnnouncedWithoutFocus(rig => rig.Shell.EnsureDetailCurrent("OnDetailPreviewGotKeyboardFocus"), null);

        // A move within the Dell region (from the button to the state line).
        await AssertAnnouncedWithoutFocus(rig => rig.Shell.OnDellFocus(DellFocusPart.SourceLine), null);

        // An Alt key that only brings the pane up to date is no move: focus still goes to the row.
        ShellTestRig control = await LoadedRig();
        control.Dell.Gate = new TaskCompletionSource<DellSettingsResult>();
        Task read = control.Shell.ReadDellSettingsAsync();
        control.Shell.EnsureDetailCurrent("OnWindowPreviewKeyDown");
        control.Focus.Clear();
        control.Dell.Gate.SetResult(DellResults.CompleteA18());
        await read;
        Assert.Equal(LoadedA18, control.Shell.StatusText);
        Assert.Equal(new[] { FocusTarget.DellSelectedRow }, control.Focus.Requests);
    }

    [Fact]
    public async Task AnswerAfterTabOrAnyFocusMove_IsAnnouncedWithoutAFocusMove()
    {
        // Tab into another region or control, a click, or any other move of keyboard focus while
        // the read runs (the window reports every one): the move itself says nothing, and the answer
        // is said once and leaves focus where the person went.
        await AssertAnnouncedWithoutFocus(rig => rig.Shell.OnKeyboardFocusMoved(), null);
        await AssertAnnouncedWithoutFocus(
            rig =>
            {
                rig.Shell.OnKeyboardFocusMoved();
                rig.Shell.OnKeyboardFocusMoved();
            },
            null);

        // A failure keeps focus too: no request for the Status box.
        ShellTestRig failed = await LoadedRig();
        failed.Dell.Gate = new TaskCompletionSource<DellSettingsResult>();
        Task read = failed.Shell.ReadDellSettingsAsync();
        int before = failed.Announcer.History.Count;
        failed.Shell.OnKeyboardFocusMoved();
        Assert.Equal(before, failed.Announcer.History.Count);
        failed.Focus.Clear();
        failed.Dell.Gate.SetResult(DellResults.NothingInstalled());
        await read;
        Assert.Equal(new[] { DellResults.NothingInstalledSentence }, failed.Announcer.History.Skip(before));
        Assert.Empty(failed.Focus.Requests);

        // A move before Alt+S, or after the answer, counts for nothing: the next read's answer
        // still takes focus to the selected row.
        ShellTestRig rig = await LoadedRig();
        rig.Shell.OnKeyboardFocusMoved();
        rig.Dell.Result = DellResults.CompleteA18();
        before = rig.Announcer.History.Count;
        rig.Focus.Clear();
        await rig.Shell.ReadDellSettingsAsync();
        rig.Shell.OnKeyboardFocusMoved();
        Assert.Equal(new[] { Strings.DellLoadingStatus, LoadedA18 }, rig.Announcer.History.Skip(before));
        Assert.Equal(new[] { FocusTarget.DellSelectedRow }, rig.Focus.Requests);
        await rig.Shell.ReadDellSettingsAsync();
        Assert.Equal(new[] { FocusTarget.DellSelectedRow, FocusTarget.DellSelectedRow }, rig.Focus.Requests);
    }

    [Fact]
    public async Task QuietUnavailable_IsOneWarnLine()
    {
        // Live: a Refresh started after Alt+S.
        ShellTestRig rig = new();
        rig.Live.Result = ShellTestRig.Loaded(new[] { ShellTestRig.BootOrder });
        rig.Dell.Gate = new TaskCompletionSource<DellSettingsResult>();
        Task read = rig.Shell.ReadDellSettingsAsync();
        await rig.Shell.RefreshAsync();
        int lines = rig.Lines.Length;
        int before = rig.Announcer.History.Count;
        rig.Focus.Clear();

        rig.Dell.Gate.SetResult(DellResults.NothingInstalled());
        await read;

        Assert.Equal(before, rig.Announcer.History.Count);
        Assert.Empty(rig.Focus.Requests);
        Assert.Equal(new[] { (LogViewModel.WarnLevel, DellResults.NothingInstalledSentence) }, rig.Lines.Skip(lines));
        Assert.Equal(DellResults.NothingInstalledSentence, rig.Shell.Dell.StateText);

        // Baseline: an Open baseline folder started after Alt+S; the rows keep the baseline's.
        ShellTestRig offline = new();
        offline.Baseline.Result = Baseline(null);
        offline.Dell.Gate = new TaskCompletionSource<DellSettingsResult>();
        read = offline.Shell.ReadDellSettingsAsync();
        await offline.Shell.OpenBaselineAsync(ShellTestRig.BaselineFolder);
        lines = offline.Lines.Length;
        before = offline.Announcer.History.Count;

        offline.Dell.Gate.SetResult(DellResults.NothingInstalled());
        await read;

        Assert.Equal(before, offline.Announcer.History.Count);
        Assert.Equal(new[] { (LogViewModel.WarnLevel, DellResults.NothingInstalledSentence) }, offline.Lines.Skip(lines));
        Assert.All(offline.Shell.Dell.Rows, row => Assert.Equal(Strings.DellNotRecordedValue, row.CurrentText));
    }

    [Fact]
    public async Task Warnings_BecomeOneWarnLineEach()
    {
        ShellTestRig rig = new();
        string[] warnings =
        {
            "These names and values were checked against BIOS A18 only; this computer runs BIOS A20.",
            "A second warning of the read.",
        };
        rig.Dell.Result = DellResults.CompleteA18() with { Machine = new DellMachine("Dell Inc.", "OptiPlex 7020", "A20"), Warnings = warnings };
        int lines = rig.Lines.Length;

        await rig.Shell.ReadDellSettingsAsync();

        Assert.Equal(
            new[]
            {
                (LogViewModel.InfoLevel, Strings.DellLoadingStatus),
                (LogViewModel.WarnLevel, warnings[0]),
                (LogViewModel.WarnLevel, warnings[1]),
                (LogViewModel.InfoLevel, LoadedA18),
            },
            rig.Lines.Skip(lines));
        Assert.EndsWith(warnings[0] + " " + warnings[1], rig.Shell.Dell.StateText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SourceThrows_IsUnavailable_WithTheUnexpectedCause()
    {
        ShellTestRig rig = new();
        rig.Dell.Throws = new InvalidOperationException("the reader broke");

        await rig.Shell.ReadDellSettingsAsync();

        Assert.Equal(UnexpectedSentence, rig.Shell.StatusText);
        Assert.Equal(new[] { FocusTarget.Status }, rig.Focus.Requests);
        Assert.False(rig.Shell.Dell.IsLoading);
        Assert.Equal(ShellState.Idle, rig.Shell.State);
        Assert.Single(rig.Lines, line => line.Level == LogViewModel.ErrorLevel && line.Message.Contains("the reader broke", StringComparison.Ordinal));

        // The command never lets it out either.
        rig.Shell.ReadDellSettingsCommand.Execute(null);
        Assert.Equal(UnexpectedSentence, rig.Shell.StatusText);
        Assert.Equal(ShellState.Idle, rig.Shell.State);
    }

    /// <summary>
    /// M4-H4.14 (2 October 2026): after a variable list load and after the Open baseline folder
    /// dialog, Narrator said Detail region, Log region, the baseline refusal and the Loaded sentence
    /// more than once, and did not say others (the refusal of M4-H4.9, "Loading the Dell BIOS
    /// settings, please wait." of M4-H4.10, "Loading variables, please wait." of M4-H4.11). The
    /// cause is on the receiving side (a late LiveRegionChanged is read with a newer sentence, as
    /// WpfAnnouncer's remarks say; after the folder dialog most events, the Status box's among them,
    /// arrive a second time, without a value), and the window raises one event per announcement
    /// (InputTraceStaticTests.LiveRegionChanged_IsRaisedByOneCall_OncePerAnnouncement_AndNowhereElse).
    /// This pins the shell's half over the steps of M4-H4.11 and M4-H4.12 and of the A/B runs:
    /// Refresh, Refresh, Open baseline folder on tools\sample-baseline, Refresh, with F6 arrivals and
    /// Alt+S between them. Each step adds exactly its own sentences, each once: a load "Loading
    /// variables, please wait." and its Loaded sentence; an arrival its region's name; Alt+S while
    /// the baseline is shown its refusal, without a read.
    /// </summary>
    [Fact]
    public async Task RefreshRefreshBaselineRefresh_EachStepSaysItsOwnSentencesOnce()
    {
        string folder = SampleBaselineGeneratorTests.Folder;
        Assert.True(Directory.Exists(folder), folder + " is missing; run the tests once with " + SampleBaselineGeneratorTests.EnvironmentVariable + "=1 to generate it");
        ShellTestRig rig = new(baselineFactory: path => new BaselineFolderSource(path));
        rig.Live.Result = ShellTestRig.Loaded(new[] { ShellTestRig.BootOrder, ShellTestRig.Timeout, ShellTestRig.Vendor });
        string dellArrival = "Dell settings region, from baseline folder " + SampleBaselineGeneratorTests.FolderName;
        Assert.Equal(new[] { Strings.IdleStatus }, rig.Announcer.History);

        // Alt+R, then F6 to the detail pane and the log.
        string live = await AssertLoadSaysLoadingThenLoaded(rig, () => rig.Shell.RefreshAsync());
        Assert.StartsWith("Loaded 3 variables from Live firmware", live, StringComparison.Ordinal);
        AssertStepSays(rig, () => rig.Shell.OnRegionArrived(Region.Detail), "Detail region");
        AssertStepSays(rig, () => rig.Shell.OnRegionArrived(Region.Log), "Log region");

        // Alt+R again: the same sentence, said once more.
        Assert.Equal(live, await AssertLoadSaysLoadingThenLoaded(rig, () => rig.Shell.RefreshAsync()));
        AssertStepSays(rig, () => rig.Shell.OnRegionArrived(Region.Detail), "Detail region");

        // Alt+O on the sample, F6 into the detail pane and the Dell region, Alt+S there.
        string baseline = await AssertLoadSaysLoadingThenLoaded(rig, () => rig.Shell.OpenBaselineAsync(folder));
        Assert.Equal("Loaded 13 variables from Baseline (offline): " + folder + " (" + SampleBaselineGeneratorTests.LocalTimestampText + ") (InMemory)", baseline);
        AssertStepSays(rig, () => rig.Shell.OnRegionArrived(Region.Detail), "Detail region");
        AssertStepSays(rig, () => rig.Shell.OnRegionArrived(Region.Dell), dellArrival);
        await AssertStepSaysAsync(rig, () => rig.Shell.ReadDellSettingsAsync(), Strings.DellBaselineShownStatus);
        Assert.Equal(0, rig.Dell.ReadCalls);

        // Alt+R back to this computer, F6 to the detail pane.
        Assert.Equal(live, await AssertLoadSaysLoadingThenLoaded(rig, () => rig.Shell.RefreshAsync()));
        AssertStepSays(rig, () => rig.Shell.OnRegionArrived(Region.Detail), "Detail region");
        Assert.Equal(DellViewMode.Live, rig.Shell.Dell.Mode);

        // The whole history: every sentence as often as its steps said it, none left out.
        Assert.Equal(
            new[]
            {
                Strings.IdleStatus,
                Strings.LoadingStatus,
                live,
                "Detail region",
                "Log region",
                Strings.LoadingStatus,
                live,
                "Detail region",
                Strings.LoadingStatus,
                baseline,
                "Detail region",
                dellArrival,
                Strings.DellBaselineShownStatus,
                Strings.LoadingStatus,
                live,
                "Detail region",
            },
            rig.Announcer.History);
        Assert.Equal(0, rig.Dell.ReadCalls);
    }

    /// <summary>A Dell read in flight, the person's move, then the answer: said once, with no focus request.</summary>
    private static async Task AssertAnnouncedWithoutFocus(Action<ShellTestRig> move, string? moveSentence)
    {
        ShellTestRig rig = await LoadedRig();
        rig.Dell.Gate = new TaskCompletionSource<DellSettingsResult>();
        Task read = rig.Shell.ReadDellSettingsAsync();
        int before = rig.Announcer.History.Count;

        move(rig);
        rig.Focus.Clear();
        rig.Dell.Gate.SetResult(DellResults.CompleteA18());
        await read;

        List<string> said = rig.Announcer.History.Skip(before).ToList();
        Assert.Equal(moveSentence is null ? new[] { LoadedA18 } : new[] { moveSentence, LoadedA18 }, said);
        Assert.Empty(rig.Focus.Requests);
        Assert.Equal(NumLockSentence, rig.Shell.Dell.Rows[9].AccessibleName);
    }

    /// <summary>Runs one step and asserts that it added exactly <paramref name="sentences"/> to the announcer's history.</summary>
    private static void AssertStepSays(ShellTestRig rig, Action step, params string[] sentences)
    {
        int before = rig.Announcer.History.Count;
        step();
        Assert.Equal(sentences, rig.Announcer.History.Skip(before));
    }

    /// <summary>Runs one asynchronous step and asserts that it added exactly <paramref name="sentences"/> to the announcer's history.</summary>
    private static async Task AssertStepSaysAsync(ShellTestRig rig, Func<Task> step, params string[] sentences)
    {
        int before = rig.Announcer.History.Count;
        await step();
        Assert.Equal(sentences, rig.Announcer.History.Skip(before));
    }

    /// <summary>Runs a load and asserts that it added exactly "Loading variables, please wait." and one Loaded sentence, the Status text; returns that sentence.</summary>
    private static async Task<string> AssertLoadSaysLoadingThenLoaded(ShellTestRig rig, Func<Task> load)
    {
        int before = rig.Announcer.History.Count;
        await load();
        string[] said = rig.Announcer.History.Skip(before).ToArray();
        Assert.Equal(2, said.Length);
        Assert.Equal(Strings.LoadingStatus, said[0]);
        Assert.StartsWith("Loaded ", said[1], StringComparison.Ordinal);
        Assert.Equal(rig.Shell.StatusText, said[1]);
        return said[1];
    }

    private static async Task<ShellTestRig> LoadedRig()
    {
        ShellTestRig rig = new();
        rig.Live.Result = ShellTestRig.Loaded(new[] { ShellTestRig.BootOrder, ShellTestRig.Timeout, ShellTestRig.Vendor });
        await rig.Shell.RefreshAsync();
        return rig;
    }

    /// <summary>A loaded baseline folder of one variable, as <see cref="BaselineFolderSource"/> returns it, with the Dell file's result.</summary>
    private static SourceResult Baseline(DellSettingsFileResult? dell)
    {
        return ShellTestRig.Loaded(new[] { ShellTestRig.BootOrder }, ShellTestRig.BaselineDescription, isOffline: true) with
        {
            DellBaseline = dell,
            BaselineFolderName = BaselineFolderName,
        };
    }

    private static Task Execute(System.Windows.Input.ICommand command) => Assert.IsType<AsyncRelayCommand>(command).ExecuteAsync();

    /// <summary>Platform services whose clipboard is held by another process.</summary>
    private sealed class ThrowingClipboard : IPlatformInteractions
    {
        public string? PickFolder(string title) => null;

        public string? PickSaveFile(string title, string defaultFileName, string filter) => null;

        public void SetClipboardText(string text) => throw new InvalidOperationException("The clipboard is held by another process.");

        public ConfirmDecision ConfirmChange(ConfirmChangeViewModel viewModel) => ConfirmDecision.Cancelled;
    }

    /// <summary>
    /// The shell over the write pipeline of a <see cref="TransactionRig"/> (fakes only, data under a
    /// temporary folder) on the contracted machine and the M3 test unit, so a change can be checked
    /// and written; the checks and the commit can be held until the test releases them.
    /// </summary>
    private sealed class ChangeRig : IDisposable
    {
        public ChangeRig()
        {
            Transaction = new TransactionRig();
            Writes = new HeldWriteService(Transaction.Pipeline, Transaction.Snapshots);
            Shell = new ShellViewModel(Live, _ => throw new InvalidOperationException("no baseline here"), Announcer, Focus, Platform, null, null, Writes, Dell);
            Shell.AnnounceReady();
        }

        public TransactionRig Transaction { get; }

        public HeldWriteService Writes { get; }

        public GatedSource Live { get; } = new(Strings.LiveSourceName, isOffline: false);

        public GatedDellSource Dell { get; } = new();

        public RecordingAnnouncer Announcer { get; } = new();

        public RecordingFocusSink Focus { get; } = new();

        public DialogPlatform Platform { get; } = new();

        public ShellViewModel Shell { get; }

        /// <summary>The live machine as the store holds it now: the contracted model on the M3 test unit.</summary>
        public SourceResult Current()
        {
            EnumerationResult enumeration = Transaction.Store.Enumerate();
            return new SourceResult(SourceStatus.Loaded, Strings.LiveSourceName, false, FirmwareType.Uefi, PrivilegeState.Enabled, enumeration.Variables, enumeration.Method, Array.Empty<string>(), SafetyFixtures.Identity(true), null)
            {
                UnitConfirmed = true,
            };
        }

        /// <summary>Loads the live machine, shows BootOrder and moves Boot0003 up in the proposed order.</summary>
        public async Task LoadBootOrderAndMoveUsbUp()
        {
            Live.Result = Current();
            await Shell.RefreshAsync();
            Assert.True(Shell.List.TrySelectByKey(FirmwareVariable.MakeKey(KnownGuids.EfiGlobalVariable, "BootOrder")));
            Shell.EnsureDetailCurrent();
            ChangeViewModel change = Shell.Detail.Change;
            change.Editor.SelectedEntry = change.Editor.Entries.Single(entry => entry.Name == "Boot0003");
            change.MoveUpCommand.Execute(null);
            Assert.True(change.Editor.HasChanges);
        }

        public void Dispose() => Transaction.Dispose();
    }

    /// <summary>The pipeline's checks and commit on the test's thread, each held while its gate is set.</summary>
    private sealed class HeldWriteService : IWriteService
    {
        private readonly IWritePipeline _pipeline;
        private readonly SnapshotStore _snapshots;
        private ChangePlan? _plan;
        private ConfirmationToken? _confirmation;

        public HeldWriteService(IWritePipeline pipeline, SnapshotStore snapshots)
        {
            _pipeline = pipeline;
            _snapshots = snapshots;
        }

        public string SnapshotRoot => _snapshots.RootDirectory;

        /// <summary>When set, the checks answer only when the test completes it.</summary>
        public TaskCompletionSource<PrepareResult?>? PrepareGate { get; set; }

        /// <summary>When set, the commit answers only on <see cref="ReleaseCommit"/>.</summary>
        public TaskCompletionSource<TransactionResult>? CommitGate { get; set; }

        public Task<PrepareResult?> PrepareAsync(ChangeRequest request, CancellationToken token)
        {
            return PrepareGate is not null ? PrepareGate.Task : Task.FromResult<PrepareResult?>(_pipeline.PrepareAsync(request, token).GetAwaiter().GetResult());
        }

        public Task<TransactionResult> CommitAsync(ChangePlan plan, ConfirmationToken confirmation)
        {
            if (CommitGate is null)
            {
                return _pipeline.CommitAsync(plan, confirmation);
            }

            _plan = plan;
            _confirmation = confirmation;
            return CommitGate.Task;
        }

        /// <summary>Runs the held commit through the pipeline and hands its result back.</summary>
        public void ReleaseCommit()
        {
            CommitGate!.SetResult(_pipeline.CommitAsync(_plan!, _confirmation!).GetAwaiter().GetResult());
        }

        public Task<TransactionResult> CancelAsync(ChangePlan plan) => _pipeline.CancelAsync(plan);

        public SnapshotSummary? Newest(string key) => _snapshots.Newest(key);
    }
}
