// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Variables;
using UefiAccess.Presentation.Accessibility;
using UefiAccess.Presentation.Resources;
using UefiAccess.Presentation.Services;
using UefiAccess.Presentation.ViewModels;
using Xunit;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// The help texts a screen reader speaks on the toolbar and the log panel say exactly what the
/// commands do. Finding F091: Refresh names both endings, focus on the first row after a load and
/// focus on Status, which says why, after a refusal or a failure. Finding F090: Export visible list
/// writes every row that passes the filter, rows in collapsed groups included (the same rows
/// "Showing N of M variables" counts), and its help says so. Open baseline folder says it works
/// while Refresh is still reading (finding F066), and the log texts say where the date is.
/// </summary>
public sealed class ToolbarHelpTextTests : IDisposable
{
    private readonly string _tempRoot;

    public ToolbarHelpTextTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "help-" + Guid.NewGuid().ToString("N"));
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
    public void RefreshHelp_NamesTheFirstRowAfterALoad_AndStatusAfterARefusalOrFailure()
    {
        Assert.Contains("focus moves to the first row of the list and Status announces the count", Strings.RefreshHelp, StringComparison.Ordinal);
        Assert.Contains("if the machine refuses or the read fails, focus moves to Status, which says why", Strings.RefreshHelp, StringComparison.Ordinal);
        Assert.Contains("Alt+R", Strings.RefreshHelp, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshHelp_MatchesWhereARefusalAndAFailureSendFocus()
    {
        ShellTestRig refused = new();
        refused.Live.Result = ShellTestRig.Refused(Strings.NotElevatedSentence);
        await refused.Shell.RefreshAsync();

        ShellTestRig failed = new();
        failed.Live.Result = ShellTestRig.Loaded(Array.Empty<FirmwareVariable>()) with { Status = SourceStatus.Failed, Message = "boom" };
        await failed.Shell.RefreshAsync();

        Assert.Equal(new[] { FocusTarget.Status }, refused.Focus.Requests);
        Assert.Equal(new[] { FocusTarget.Status }, failed.Focus.Requests);
    }

    [Fact]
    public void ExportHelp_SaysCollapsedGroupsAreExported()
    {
        Assert.Contains("every row that passes the filter", Strings.ExportVisibleListHelp, StringComparison.Ordinal);
        Assert.Contains("rows in collapsed groups included", Strings.ExportVisibleListHelp, StringComparison.Ordinal);
        Assert.DoesNotContain("currently shown", Strings.ExportVisibleListHelp, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_WritesTheRowsOfACollapsedGroup_AndCountsThemLikeTheShowingSentence()
    {
        ShellTestRig rig = new();
        rig.Live.Result = ShellTestRig.Loaded(new[] { ShellTestRig.BootOrder, ShellTestRig.Timeout, ShellTestRig.Vendor });
        await rig.Shell.RefreshAsync();
        VariableGroupViewModel collapsed = Assert.Single(rig.Shell.List.Groups, group => group.Items.Any(item => item.Name == "AcpiGlobalVariable"));
        collapsed.IsExpanded = false;
        string path = Path.Combine(_tempRoot, "visible.txt");
        rig.Platform.FileToReturn = path;

        rig.Shell.ExportVisibleListCommand.Execute(null);

        Assert.Contains("AcpiGlobalVariable", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Equal("Exported 3 visible variables to " + path, rig.Shell.StatusText);
        Assert.Equal(3, rig.Shell.List.VisibleCount);
        Assert.Equal("Showing 3 of 3 variables", rig.Shell.List.ShowingText);
    }

    [Fact]
    public void OpenBaselineHelp_SaysItWorksWhileRefreshIsReading()
    {
        Assert.Contains("Also works while Refresh is still reading; the baseline then replaces that read.", Strings.OpenBaselineHelp, StringComparison.Ordinal);
        Assert.Contains("Alt+O", Strings.OpenBaselineHelp, StringComparison.Ordinal);
    }

    [Fact]
    public void LogHelp_SaysWhereTheDateIs()
    {
        Assert.Contains("a line from an earlier day also gives its date", Strings.LogPanelHelp, StringComparison.Ordinal);
        Assert.Contains("each with its full date and time", Strings.CopyLogHelp, StringComparison.Ordinal);
    }
}
