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
/// Finding F173: a load that finishes while the save dialog of Export visible list (or the folder
/// dialog of Open baseline folder) is open updates the list at once, but its sentence and its focus
/// request wait for the dialog to close; the command then announces its own result followed by
/// the load's sentence and repeats the load's focus request, so "Cancelled; nothing changed." never
/// replaces or hides the Loaded sentence, and the export writes the rows that passed the filter when
/// the command was given. Finding F088: a folder dialog that throws is reported as "Open baseline
/// folder failed: ... Nothing changed." with the state, the list, the title and the focus left as
/// they were, not as a failed load. The dialogs "run" the load's completion the way a modal
/// dialog's message loop does, through <see cref="DialogPlatform.WhileOpen"/>.
/// </summary>
public sealed class ShellDialogOutcomeTests : IDisposable
{
    private const string LiveLoaded = "Loaded 3 variables from Live firmware (InMemory)";

    private readonly string _tempRoot;

    public ShellDialogOutcomeTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "dialogs-" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public async Task ExportCancelled_AfterALoadFinishedBehindTheDialog_AnnouncesTheLoadedSentence_AndFocusesTheFirstRow()
    {
        ShellTestRig rig = new();
        rig.Live.Gate = new TaskCompletionSource<SourceResult>();
        Task loading = rig.Shell.RefreshAsync();
        bool loadedBehindTheDialog = false;
        rig.Platform.WhileOpen = () =>
        {
            rig.Live.Gate.SetResult(ShellTestRig.Loaded(new[] { ShellTestRig.BootOrder, ShellTestRig.Timeout, ShellTestRig.Vendor }));
            loadedBehindTheDialog = rig.Shell.State == ShellState.Loaded;

            // The list changed at once, but nothing was said or focused behind the dialog.
            Assert.Equal(Strings.LoadingStatus, rig.Shell.StatusText);
            Assert.Empty(rig.Focus.Requests);
        };

        rig.Shell.ExportVisibleListCommand.Execute(null);
        await loading;

        Assert.True(loadedBehindTheDialog, "the load's completion ran while the dialog was open");
        Assert.Equal(LiveLoaded, rig.Shell.StatusText);
        Assert.Equal(new[] { Strings.IdleStatus, Strings.LoadingStatus, LiveLoaded }, rig.Announcer.History);
        Assert.DoesNotContain(Strings.DialogCancelledStatus, rig.Announcer.History);
        Assert.Equal(new[] { FocusTarget.ListFirstRow }, rig.Focus.Requests);
        Assert.Equal(ShellState.Loaded, rig.Shell.State);
        Assert.Single(rig.Shell.Log.Lines, line => line.Message == LiveLoaded);
    }

    [Fact]
    public async Task ExportSaved_AfterALoadFinishedBehindTheDialog_WritesTheRowsShownWhenItWasPressed_AndAnnouncesBoth()
    {
        ShellTestRig rig = new();
        rig.Live.Result = ShellTestRig.Loaded(new[] { ShellTestRig.BootOrder });
        await rig.Shell.RefreshAsync();
        rig.Focus.Clear();

        rig.Live.Gate = new TaskCompletionSource<SourceResult>();
        Task reloading = rig.Shell.RefreshAsync();
        string path = Path.Combine(_tempRoot, "visible.txt");
        rig.Platform.FileToReturn = path;
        rig.Platform.WhileOpen = () => rig.Live.Gate.SetResult(ShellTestRig.Loaded(new[] { ShellTestRig.BootOrder, ShellTestRig.Timeout, ShellTestRig.Vendor }));

        rig.Shell.ExportVisibleListCommand.Execute(null);
        await reloading;

        string written = File.ReadAllText(path);
        Assert.Contains("BootOrder", written, StringComparison.Ordinal);
        Assert.DoesNotContain("Timeout", written, StringComparison.Ordinal);
        Assert.DoesNotContain("AcpiGlobalVariable", written, StringComparison.Ordinal);
        Assert.Equal("Exported 1 visible variable to " + path + ". " + LiveLoaded, rig.Shell.StatusText);
        Assert.Equal(new[] { FocusTarget.ListFirstRow }, rig.Focus.Requests);
        Assert.Equal(3, rig.Shell.List.TotalCount);
    }

    [Fact]
    public async Task ExportCancelled_AfterARefusalBehindTheDialog_AnnouncesTheRefusal_AndFocusesStatus()
    {
        ShellTestRig rig = new();
        rig.Live.Gate = new TaskCompletionSource<SourceResult>();
        Task loading = rig.Shell.RefreshAsync();
        rig.Platform.WhileOpen = () => rig.Live.Gate.SetResult(ShellTestRig.Refused(Strings.NotElevatedSentence));

        rig.Shell.ExportVisibleListCommand.Execute(null);
        await loading;

        Assert.Equal(Strings.NotElevatedSentence, rig.Shell.StatusText);
        Assert.Equal(ShellState.Refused, rig.Shell.State);
        Assert.Equal(new[] { FocusTarget.Status }, rig.Focus.Requests);
    }

    [Fact]
    public async Task ExportWithoutALoad_StillSaysCancelled_AndMovesNoFocus()
    {
        ShellTestRig rig = new();
        rig.Live.Result = ShellTestRig.Loaded(new[] { ShellTestRig.BootOrder });
        await rig.Shell.RefreshAsync();
        rig.Focus.Clear();

        rig.Shell.ExportVisibleListCommand.Execute(null);

        Assert.Equal(Strings.DialogCancelledStatus, rig.Shell.StatusText);
        Assert.Empty(rig.Focus.Requests);
    }

    [Fact]
    public async Task FolderDialogCancelled_AfterALiveLoadFinishedBehindIt_AnnouncesTheLoadedSentence()
    {
        ShellTestRig rig = new();
        rig.Live.Gate = new TaskCompletionSource<SourceResult>();
        Task loading = rig.Shell.RefreshAsync();
        rig.Platform.WhileOpen = () => rig.Live.Gate.SetResult(ShellTestRig.Loaded(new[] { ShellTestRig.BootOrder, ShellTestRig.Timeout, ShellTestRig.Vendor }));

        await Assert.IsType<AsyncRelayCommand>(rig.Shell.OpenBaselineCommand).ExecuteAsync();
        await loading;

        Assert.Equal(LiveLoaded, rig.Shell.StatusText);
        Assert.DoesNotContain(Strings.DialogCancelledStatus, rig.Announcer.History);
        Assert.Equal(new[] { FocusTarget.ListFirstRow }, rig.Focus.Requests);
        Assert.Empty(rig.FactoryFolders);
    }

    [Fact]
    public async Task FolderPicked_AfterALiveLoadFinishedBehindTheDialog_LoadsTheBaseline_AndNeverSpeaksTheLiveSentence()
    {
        ShellTestRig rig = new();
        rig.Live.Gate = new TaskCompletionSource<SourceResult>();
        rig.Baseline.Result = ShellTestRig.Loaded(new[] { ShellTestRig.Timeout }, ShellTestRig.BaselineDescription, isOffline: true);
        Task loading = rig.Shell.RefreshAsync();
        rig.Platform.FolderToReturn = ShellTestRig.BaselineFolder;
        rig.Platform.WhileOpen = () => rig.Live.Gate.SetResult(ShellTestRig.Loaded(new[] { ShellTestRig.BootOrder, ShellTestRig.Timeout, ShellTestRig.Vendor }));

        await Assert.IsType<AsyncRelayCommand>(rig.Shell.OpenBaselineCommand).ExecuteAsync();
        await loading;

        Assert.True(rig.Shell.IsOffline);
        Assert.Equal("Loaded 1 variable from " + ShellTestRig.BaselineDescription + " (InMemory)", rig.Shell.StatusText);
        Assert.DoesNotContain(LiveLoaded, rig.Announcer.History);
        Assert.Equal(new[] { FocusTarget.ListFirstRow }, rig.Focus.Requests);
    }

    [Fact]
    public async Task FolderDialogThatThrows_KeepsThePreviousSourceAndState_AndSaysSo()
    {
        ShellTestRig rig = new();
        rig.Live.Result = ShellTestRig.Loaded(new[] { ShellTestRig.BootOrder, ShellTestRig.Timeout, ShellTestRig.Vendor });
        await rig.Shell.RefreshAsync();
        rig.Focus.Clear();
        string title = rig.Shell.WindowTitle;
        string model = rig.Shell.Model;
        rig.Platform.DialogThrows = new InvalidOperationException("The folder picker is not available.");

        await Assert.IsType<AsyncRelayCommand>(rig.Shell.OpenBaselineCommand).ExecuteAsync();

        Assert.Equal("Open baseline folder failed: The folder picker is not available. Nothing changed.", rig.Shell.StatusText);
        Assert.Equal(ShellState.Loaded, rig.Shell.State);
        Assert.False(rig.Shell.IsBusy);
        Assert.Equal(3, rig.Shell.List.TotalCount);
        Assert.Equal(title, rig.Shell.WindowTitle);
        Assert.Equal(model, rig.Shell.Model);
        Assert.Empty(rig.Focus.Requests);
        Assert.Empty(rig.FactoryFolders);
        LogLineViewModel error = Assert.Single(rig.Shell.Log.Lines, line => line.Level == LogViewModel.ErrorLevel);
        Assert.Equal("Open baseline folder failed (InvalidOperationException: The folder picker is not available.)", error.Message);
        Assert.DoesNotContain(rig.Announcer.History, text => text.StartsWith("Loading failed", StringComparison.Ordinal));
        Assert.True(rig.Shell.OpenBaselineCommand.CanExecute(null));
    }

    [Fact]
    public async Task ALoadWithAFilterThatFinishesBehindTheDialog_KeepsTheShowingSentence_AndCountsItAsAnnounced()
    {
        ShellTestRig rig = new();
        rig.Shell.List.FilterText = "Acpi";
        rig.Live.Gate = new TaskCompletionSource<SourceResult>();
        Task loading = rig.Shell.RefreshAsync();
        rig.Platform.WhileOpen = () => rig.Live.Gate.SetResult(ShellTestRig.Loaded(new[] { ShellTestRig.BootOrder, ShellTestRig.Timeout, ShellTestRig.Vendor }));

        rig.Shell.ExportVisibleListCommand.Execute(null);
        await loading;
        int announcements = rig.Announcer.History.Count;
        rig.Shell.OnFilterApplied();

        Assert.Equal(LiveLoaded + ". Showing 1 of 3 variables", rig.Shell.StatusText);
        Assert.Equal(announcements, rig.Announcer.History.Count);
    }
}
