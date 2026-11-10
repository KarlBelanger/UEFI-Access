// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Platform;
using UefiAccess.Core.Variables;
using UefiAccess.Presentation.Accessibility;
using UefiAccess.Presentation.Infrastructure;
using UefiAccess.Presentation.Resources;
using UefiAccess.Presentation.Services;
using UefiAccess.Presentation.ViewModels;
using Xunit;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// Finding F066, the way out of a slow live load: while Refresh is still reading, Open baseline
/// folder stays available, and a folder picked starts a load that replaces the running one. The
/// replaced load is cancelled, and its result, when it arrives, is dropped without a word: no
/// announcement, no focus request, no log line, no change to the list or the busy state. Refresh
/// itself stays disabled while any load runs.
/// </summary>
public sealed class ShellLoadReplacementTests
{
    [Fact]
    public async Task OpenBaselineCommand_WhileALiveLoadRuns_IsAvailable_AndTheFolderReplacesTheLiveLoad()
    {
        ShellTestRig rig = new();
        rig.Live.Gate = new TaskCompletionSource<SourceResult>();
        rig.Baseline.Result = ShellTestRig.Loaded(new[] { ShellTestRig.BootOrder }, ShellTestRig.BaselineDescription, isOffline: true);
        rig.Platform.FolderToReturn = ShellTestRig.BaselineFolder;

        Task live = rig.Shell.RefreshAsync();
        Assert.True(rig.Shell.IsBusy);
        Assert.False(rig.Shell.RefreshCommand.CanExecute(null));
        Assert.True(rig.Shell.OpenBaselineCommand.CanExecute(null));

        await Assert.IsType<AsyncRelayCommand>(rig.Shell.OpenBaselineCommand).ExecuteAsync();

        Assert.Equal(new[] { ShellTestRig.BaselineFolder }, rig.FactoryFolders);
        Assert.True(rig.Live.Tokens.Single().IsCancellationRequested, "the replaced live load is cancelled");
        Assert.False(rig.Shell.IsBusy);
        Assert.Equal(ShellState.Loaded, rig.Shell.State);
        Assert.True(rig.Shell.IsOffline);
        Assert.Equal("UefiAccess – " + ShellTestRig.BaselineDescription, rig.Shell.WindowTitle);
        string baselineLoaded = "Loaded 1 variable from " + ShellTestRig.BaselineDescription + " (InMemory)";
        Assert.Equal(baselineLoaded, rig.Shell.StatusText);

        // The live result arrives later and is dropped unseen.
        int linesBefore = rig.Shell.Log.Count;
        rig.Live.Gate.SetResult(ShellTestRig.Loaded(new[] { ShellTestRig.BootOrder, ShellTestRig.Timeout, ShellTestRig.Vendor }));
        await live;

        Assert.Equal(ShellState.Loaded, rig.Shell.State);
        Assert.True(rig.Shell.IsOffline);
        Assert.Equal(1, rig.Shell.List.TotalCount);
        Assert.Equal(baselineLoaded, rig.Shell.StatusText);
        Assert.Equal(linesBefore, rig.Shell.Log.Count);
        Assert.DoesNotContain(rig.Announcer.History, text => text.Contains("from Live firmware", StringComparison.Ordinal));
        Assert.Equal(new[] { FocusTarget.ListFirstRow }, rig.Focus.Requests);
        Assert.True(rig.Shell.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public async Task AReplacedLoad_DoesNotEndTheBusyStateOfTheLoadThatReplacedIt()
    {
        ShellTestRig rig = new();
        rig.Live.Gate = new TaskCompletionSource<SourceResult>();
        rig.Baseline.Gate = new TaskCompletionSource<SourceResult>();

        Task live = rig.Shell.RefreshAsync();
        Task baseline = rig.Shell.OpenBaselineAsync(ShellTestRig.BaselineFolder);
        Assert.Equal(new[] { Strings.IdleStatus, Strings.LoadingStatus, Strings.LoadingStatus }, rig.Announcer.History);

        rig.Live.Gate.SetResult(ShellTestRig.Refused(Strings.NotElevatedSentence));
        await live;

        Assert.True(rig.Shell.IsBusy);
        Assert.Equal(ShellState.Loading, rig.Shell.State);
        Assert.Equal(Strings.LoadingStatus, rig.Shell.StatusText);
        Assert.Empty(rig.Focus.Requests);
        Assert.False(rig.Shell.RefreshCommand.CanExecute(null));

        rig.Baseline.Gate.SetResult(ShellTestRig.Loaded(new[] { ShellTestRig.Timeout }, ShellTestRig.BaselineDescription, isOffline: true));
        await baseline;

        Assert.False(rig.Shell.IsBusy);
        Assert.Equal(ShellState.Loaded, rig.Shell.State);
        Assert.Equal(new[] { FocusTarget.ListFirstRow }, rig.Focus.Requests);
        Assert.DoesNotContain(rig.Announcer.History, text => text == Strings.NotElevatedSentence);
    }

    [Fact]
    public async Task ABaselineFactoryThatThrows_WhileALiveLoadRuns_ReplacesTheLiveLoadToo()
    {
        GatedSource live = new(Strings.LiveSourceName, isOffline: false) { Gate = new TaskCompletionSource<SourceResult>() };
        ShellTestRig rig = new(live, _ => throw new DirectoryNotFoundException("no such folder"));

        Task loading = rig.Shell.RefreshAsync();
        await rig.Shell.OpenBaselineAsync(ShellTestRig.BaselineFolder);

        Assert.Equal(ShellState.Failed, rig.Shell.State);
        Assert.False(rig.Shell.IsBusy);
        Assert.Equal("Loading failed: no such folder", rig.Shell.StatusText);
        Assert.True(live.Tokens.Single().IsCancellationRequested);

        live.Gate.SetResult(ShellTestRig.Loaded(new[] { ShellTestRig.BootOrder }));
        await loading;

        Assert.Equal(ShellState.Failed, rig.Shell.State);
        Assert.Equal("Loading failed: no such folder", rig.Shell.StatusText);
        Assert.Equal(0, rig.Shell.List.TotalCount);
        Assert.Equal(new[] { FocusTarget.Status }, rig.Focus.Requests);
    }

    [Fact]
    public async Task Refresh_WhileABaselineLoadRuns_IsDisabledAndIgnored()
    {
        ShellTestRig rig = new();
        rig.Baseline.Gate = new TaskCompletionSource<SourceResult>();

        Task baseline = rig.Shell.OpenBaselineAsync(ShellTestRig.BaselineFolder);
        Assert.False(rig.Shell.RefreshCommand.CanExecute(null));
        await rig.Shell.RefreshAsync();

        Assert.Equal(0, rig.Live.LoadCalls);
        rig.Baseline.Gate.SetResult(ShellTestRig.Loaded(new[] { ShellTestRig.BootOrder }, ShellTestRig.BaselineDescription, isOffline: true));
        await baseline;
        Assert.Equal(ShellState.Loaded, rig.Shell.State);
        Assert.False(rig.Baseline.Tokens.Single().IsCancellationRequested);
    }

    [Fact]
    public async Task EveryLoadGetsItsOwnToken_AndACompletedLoadIsNotCancelledAfterwards()
    {
        ShellTestRig rig = new();
        rig.Live.Result = ShellTestRig.Loaded(new[] { ShellTestRig.BootOrder });

        await rig.Shell.RefreshAsync();
        await rig.Shell.RefreshAsync();

        Assert.Equal(2, rig.Live.Tokens.Count);
        Assert.All(rig.Live.Tokens, token => Assert.True(token.CanBeCanceled));
        Assert.All(rig.Live.Tokens, token => Assert.False(token.IsCancellationRequested));
    }
}
