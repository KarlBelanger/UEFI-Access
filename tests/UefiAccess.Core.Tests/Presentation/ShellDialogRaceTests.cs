// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Presentation.Accessibility;
using UefiAccess.Presentation.Infrastructure;
using UefiAccess.Presentation.Resources;
using UefiAccess.Presentation.Services;
using UefiAccess.Presentation.ViewModels;
using Xunit;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// Final review, findings R15 and R16: two races between a load and the folder dialog of Open
/// baseline folder, which Alt+O can open while Refresh is reading. R15: a folder dialog that threw
/// after a load had finished behind it announced "Open baseline folder failed: ... Nothing
/// changed." followed by the load's own sentence, although that load had replaced the list, the
/// title and the identity; now the failure is said without "Nothing changed.". R16: a folder
/// picked after a load had finished behind the dialog dropped that load's focus request, and the
/// load had rebuilt the list the dialog gives focus back to, so focus could fall to the window
/// until the baseline load finished; now focus goes to the Status box ("Loading variables, please
/// wait.") while the baseline loads. The dialogs run the load's completion the way a modal
/// dialog's message loop does, through <see cref="DialogPlatform.WhileOpen"/>.
/// </summary>
public sealed class ShellDialogRaceTests
{
    private const string LiveLoaded = "Loaded 3 variables from Live firmware (InMemory)";

    [Fact]
    public async Task FolderDialogThatThrows_AfterALoadFinishedBehindIt_NeverSaysNothingChanged()
    {
        ShellTestRig rig = new();
        rig.Live.Gate = new TaskCompletionSource<SourceResult>();
        Task loading = rig.Shell.RefreshAsync();
        rig.Platform.DialogThrows = new InvalidOperationException("The folder picker is not available.");
        rig.Platform.WhileOpen = () => rig.Live.Gate.SetResult(ShellTestRig.Loaded(new[] { ShellTestRig.BootOrder, ShellTestRig.Timeout, ShellTestRig.Vendor }));

        await Assert.IsType<AsyncRelayCommand>(rig.Shell.OpenBaselineCommand).ExecuteAsync();
        await loading;

        // The load changed the list behind the dialog: the failure, then the load's own sentence.
        Assert.Equal("Open baseline folder failed: The folder picker is not available. " + LiveLoaded, rig.Shell.StatusText);
        Assert.DoesNotContain(rig.Announcer.History, text => text.Contains("Nothing changed", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(new[] { Strings.IdleStatus, Strings.LoadingStatus, "Open baseline folder failed: The folder picker is not available. " + LiveLoaded }, rig.Announcer.History);
        Assert.Equal(ShellState.Loaded, rig.Shell.State);
        Assert.Equal(3, rig.Shell.List.TotalCount);
        Assert.Equal(new[] { FocusTarget.ListFirstRow }, rig.Focus.Requests);
        Assert.Empty(rig.FactoryFolders);
        Assert.Single(rig.Shell.Log.Lines, line => line.Level == LogViewModel.ErrorLevel);
    }

    [Fact]
    public async Task FolderDialogThatThrows_AfterARefusalBehindIt_SaysTheFailureThenTheRefusal_AndFocusesStatus()
    {
        ShellTestRig rig = new();
        rig.Live.Gate = new TaskCompletionSource<SourceResult>();
        Task loading = rig.Shell.RefreshAsync();
        rig.Platform.DialogThrows = new InvalidOperationException("The folder picker is not available.");
        rig.Platform.WhileOpen = () => rig.Live.Gate.SetResult(ShellTestRig.Refused(Strings.NotElevatedSentence));

        await Assert.IsType<AsyncRelayCommand>(rig.Shell.OpenBaselineCommand).ExecuteAsync();
        await loading;

        Assert.Equal("Open baseline folder failed: The folder picker is not available. " + Strings.NotElevatedSentence, rig.Shell.StatusText);
        Assert.Equal(ShellState.Refused, rig.Shell.State);
        Assert.Equal(new[] { FocusTarget.Status }, rig.Focus.Requests);
    }

    [Fact]
    public async Task FolderDialogThatThrows_WithoutALoadBehindIt_StillSaysNothingChanged()
    {
        ShellTestRig rig = new();
        rig.Live.Result = ShellTestRig.Loaded(new[] { ShellTestRig.BootOrder });
        await rig.Shell.RefreshAsync();
        rig.Focus.Clear();
        rig.Platform.DialogThrows = new InvalidOperationException("The folder picker is not available.");

        await Assert.IsType<AsyncRelayCommand>(rig.Shell.OpenBaselineCommand).ExecuteAsync();

        Assert.Equal("Open baseline folder failed: The folder picker is not available. Nothing changed.", rig.Shell.StatusText);
        Assert.Empty(rig.Focus.Requests);
    }

    [Fact]
    public async Task FolderPicked_AfterALoadFinishedBehindTheDialog_SendsFocusToStatusWhileTheBaselineLoads()
    {
        ShellTestRig rig = new();
        rig.Live.Gate = new TaskCompletionSource<SourceResult>();
        rig.Baseline.Gate = new TaskCompletionSource<SourceResult>();
        Task loading = rig.Shell.RefreshAsync();
        rig.Platform.FolderToReturn = ShellTestRig.BaselineFolder;
        rig.Platform.WhileOpen = () => rig.Live.Gate.SetResult(ShellTestRig.Loaded(new[] { ShellTestRig.BootOrder, ShellTestRig.Timeout, ShellTestRig.Vendor }));

        Task opening = Assert.IsType<AsyncRelayCommand>(rig.Shell.OpenBaselineCommand).ExecuteAsync();
        await loading;

        // The live load rebuilt the list behind the dialog; its sentence is replaced by the
        // baseline load's, and its focus request is not dropped but sent to the Status box, which
        // reads the Loading sentence while the baseline loads.
        Assert.False(opening.IsCompleted);
        Assert.Equal(ShellState.Loading, rig.Shell.State);
        Assert.Equal(Strings.LoadingStatus, rig.Shell.StatusText);
        Assert.Equal(new[] { FocusTarget.Status }, rig.Focus.Requests);
        Assert.DoesNotContain(LiveLoaded, rig.Announcer.History);

        rig.Baseline.Gate.SetResult(ShellTestRig.Loaded(new[] { ShellTestRig.Timeout }, ShellTestRig.BaselineDescription, isOffline: true));
        await opening;

        Assert.True(rig.Shell.IsOffline);
        Assert.Equal("Loaded 1 variable from " + ShellTestRig.BaselineDescription + " (InMemory)", rig.Shell.StatusText);
        Assert.Equal(new[] { FocusTarget.Status, FocusTarget.ListFirstRow }, rig.Focus.Requests);
        Assert.DoesNotContain(LiveLoaded, rig.Announcer.History);
    }

    [Fact]
    public async Task FolderPicked_AfterALoadFinishedBehindTheDialog_WhenTheBaselineAnswersAtOnce_OnlyItsOwnFocusRequestIsMade()
    {
        ShellTestRig rig = new();
        rig.Live.Gate = new TaskCompletionSource<SourceResult>();
        rig.Baseline.Result = ShellTestRig.Loaded(new[] { ShellTestRig.Timeout }, ShellTestRig.BaselineDescription, isOffline: true);
        Task loading = rig.Shell.RefreshAsync();
        rig.Platform.FolderToReturn = ShellTestRig.BaselineFolder;
        rig.Platform.WhileOpen = () => rig.Live.Gate.SetResult(ShellTestRig.Loaded(new[] { ShellTestRig.BootOrder, ShellTestRig.Timeout, ShellTestRig.Vendor }));

        await Assert.IsType<AsyncRelayCommand>(rig.Shell.OpenBaselineCommand).ExecuteAsync();
        await loading;

        Assert.Equal(new[] { FocusTarget.ListFirstRow }, rig.Focus.Requests);
    }

    [Fact]
    public async Task FolderPicked_AfterALoadFinishedBehindTheDialog_WhenTheBaselineSourceCannotBeMade_FocusesStatusOnce()
    {
        ShellTestRig rig = new(baselineFactory: _ => throw new InvalidOperationException("no such folder"));
        rig.Live.Gate = new TaskCompletionSource<SourceResult>();
        Task loading = rig.Shell.RefreshAsync();
        rig.Platform.FolderToReturn = ShellTestRig.BaselineFolder;
        rig.Platform.WhileOpen = () => rig.Live.Gate.SetResult(ShellTestRig.Loaded(new[] { ShellTestRig.BootOrder }));

        await Assert.IsType<AsyncRelayCommand>(rig.Shell.OpenBaselineCommand).ExecuteAsync();
        await loading;

        Assert.Equal(ShellState.Failed, rig.Shell.State);
        Assert.Equal(new[] { FocusTarget.Status }, rig.Focus.Requests);
    }

    [Fact]
    public async Task ALoadThatFinishesBehindTheFolderDialog_AfterAnotherDialogOpenedAndClosedInsideIt_IsStillHeld()
    {
        // M3 review: the dialog guard was cleared when a dialog shown inside another one closed,
        // while the outer dialog was still open, so a load finishing after that spoke behind the
        // outer dialog and its Cancel then said "Cancelled; nothing changed." over it. The window is
        // disabled under a modal dialog, so the inner dialog here stands for any nesting.
        ShellTestRig rig = new();
        TaskCompletionSource<SourceResult> gate = new();
        rig.Live.Gate = gate;
        Task loading = rig.Shell.RefreshAsync();
        string? statusBehindTheDialog = null;
        rig.Platform.WhileOpen = () =>
        {
            rig.Platform.WhileOpen = null;
            rig.Shell.ExportVisibleListCommand.Execute(null);
            gate.SetResult(ShellTestRig.Loaded(new[] { ShellTestRig.BootOrder, ShellTestRig.Timeout, ShellTestRig.Vendor }));
            statusBehindTheDialog = rig.Shell.StatusText;
        };

        await Assert.IsType<AsyncRelayCommand>(rig.Shell.OpenBaselineCommand).ExecuteAsync();
        await loading;

        // The inner (save) dialog was cancelled; the load's sentence waited for the outer one.
        Assert.Equal(2, rig.Platform.DialogsShown);
        Assert.Equal(Strings.DialogCancelledStatus, statusBehindTheDialog);
        Assert.Equal(LiveLoaded, rig.Shell.StatusText);
        Assert.Equal(new[] { Strings.IdleStatus, Strings.LoadingStatus, Strings.DialogCancelledStatus, LiveLoaded }, rig.Announcer.History);
        Assert.Equal(new[] { FocusTarget.ListFirstRow }, rig.Focus.Requests);
        Assert.Equal(ShellState.Loaded, rig.Shell.State);
        Assert.Empty(rig.FactoryFolders);
    }

    [Fact]
    public async Task FolderPicked_WithoutALoadBehindTheDialog_MovesNoFocusUntilTheBaselineLoads()
    {
        ShellTestRig rig = new();
        rig.Live.Result = ShellTestRig.Loaded(new[] { ShellTestRig.BootOrder });
        await rig.Shell.RefreshAsync();
        rig.Focus.Clear();
        rig.Baseline.Gate = new TaskCompletionSource<SourceResult>();
        rig.Platform.FolderToReturn = ShellTestRig.BaselineFolder;

        Task opening = Assert.IsType<AsyncRelayCommand>(rig.Shell.OpenBaselineCommand).ExecuteAsync();

        Assert.Empty(rig.Focus.Requests);
        rig.Baseline.Gate.SetResult(ShellTestRig.Loaded(new[] { ShellTestRig.Timeout }, ShellTestRig.BaselineDescription, isOffline: true));
        await opening;
        Assert.Equal(new[] { FocusTarget.ListFirstRow }, rig.Focus.Requests);
    }
}
