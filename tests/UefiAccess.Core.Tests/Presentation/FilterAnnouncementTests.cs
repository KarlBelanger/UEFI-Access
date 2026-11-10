// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Platform;
using UefiAccess.Core.Variables;
using UefiAccess.Presentation.Accessibility;
using UefiAccess.Presentation.Resources;
using UefiAccess.Presentation.Services;
using UefiAccess.Presentation.ViewModels;
using Xunit;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// Alpha 2 finding A2-9 (the tester's A2-7, severity 2): "Change the value in the sort or group
/// dropdowns. Announced: Status message with number of variables is spoken. Expected: Group and
/// sort do not change variable quantity, so status message is unnecessary. This extra speech just
/// slows users down." <see cref="ShellViewModel.OnFilterApplied"/>, which the window calls after
/// every change of the filter text, Group by or Sort by, says "Showing N of M variables" only when
/// the filter text, the visible count or the total count differs from the list the view applied
/// last; a change of Group by or Sort by alone never makes the count news: only the filter text
/// and the two counts decide. Every load records the list it shows as the list the view applied:
/// a Loaded one (live or from a baseline folder) and a Refused or Failed one, which empties the
/// list, so the first Group by or Sort by change after any load says nothing. The single
/// announcement of Clear filter (also when the window's filter box echoes the emptied text back)
/// and of a filter change during a load are kept.
/// </summary>
public sealed class FilterAnnouncementTests
{
    private const string LoadedSentence = "Loaded 3 variables from Live firmware (InMemory)";

    private static readonly IReadOnlyList<FirmwareVariable> ThreeVariables = new[] { ShellTestRig.BootOrder, ShellTestRig.Timeout, ShellTestRig.Vendor };

    [Fact]
    public async Task AGroupByChangeAlone_AnnouncesNothing()
    {
        ShellTestRig rig = await LoadedRigAsync();
        rig.Shell.List.FilterText = "Acpi";
        rig.Shell.OnFilterApplied();
        int before = rig.Announcer.History.Count;
        int lines = rig.Shell.Log.Count;

        foreach (GroupBy groupBy in new[] { GroupBy.Kind, GroupBy.None, GroupBy.Namespace })
        {
            rig.Shell.List.GroupBy = groupBy;
            rig.Shell.OnFilterApplied();
        }

        Assert.Equal(before, rig.Announcer.History.Count);
        Assert.Equal(lines, rig.Shell.Log.Count);
        Assert.Equal("Showing 1 of 3 variables", rig.Shell.StatusText);
        Assert.Empty(rig.Focus.Requests.Skip(1));
    }

    [Fact]
    public async Task ASortByChangeAlone_AnnouncesNothing()
    {
        ShellTestRig rig = await LoadedRigAsync();
        int before = rig.Announcer.History.Count;

        foreach (SortBy sortBy in new[] { SortBy.Size, SortBy.Kind, SortBy.Namespace, SortBy.Name })
        {
            rig.Shell.List.SortBy = sortBy;
            rig.Shell.OnFilterApplied();
        }

        Assert.Equal(before, rig.Announcer.History.Count);
        Assert.Equal(LoadedSentence, rig.Shell.StatusText);
    }

    [Fact]
    public async Task AFilterTextChange_AnnouncesOnce()
    {
        ShellTestRig rig = await LoadedRigAsync();
        int before = rig.Announcer.History.Count;

        rig.Shell.List.FilterText = "Acpi";
        rig.Shell.OnFilterApplied();
        rig.Shell.OnFilterApplied();

        Assert.Equal(new[] { "Showing 1 of 3 variables" }, rig.Announcer.History.Skip(before));
        Assert.Equal("Showing 1 of 3 variables", rig.Shell.Log.Lines[^1].Message);

        // Every keystroke is a new filter text and is said, even when the count stays the same.
        rig.Shell.List.FilterText = "Acp";
        rig.Shell.OnFilterApplied();
        Assert.Equal(new[] { "Showing 1 of 3 variables", "Showing 1 of 3 variables" }, rig.Announcer.History.Skip(before));
    }

    [Fact]
    public async Task AGroupChangeThenAFilterChange_AnnouncesOnce_TheFilterChange()
    {
        ShellTestRig rig = await LoadedRigAsync();
        int before = rig.Announcer.History.Count;

        rig.Shell.List.GroupBy = GroupBy.None;
        rig.Shell.OnFilterApplied();
        rig.Shell.List.FilterText = "Acpi";
        rig.Shell.OnFilterApplied();

        Assert.Equal(new[] { "Showing 1 of 3 variables" }, rig.Announcer.History.Skip(before));

        // A sort change after the filter change leaves the filter text and the two counts as the
        // list the view applied last had them, so it says nothing.
        rig.Shell.List.SortBy = SortBy.Size;
        rig.Shell.OnFilterApplied();
        Assert.Single(rig.Announcer.History.Skip(before));
    }

    [Fact]
    public async Task ClearFilter_AnnouncesOnce_AlsoWithTheWindowsEcho_AndOncePerPress()
    {
        ShellTestRig rig = await LoadedRigAsync();
        rig.Shell.List.FilterText = "Acpi";
        rig.Shell.OnFilterApplied();

        // The window's filter box is bound TwoWay to FilterText: clearing the filter pushes the
        // empty text into the box, whose TextChanged handler calls OnFilterApplied again.
        rig.Shell.List.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VariableListViewModel.FilterText))
            {
                rig.Shell.OnFilterApplied();
            }
        };
        int before = rig.Announcer.History.Count;

        rig.Shell.ClearFilterCommand.Execute(null);

        Assert.Equal(new[] { "Showing 3 of 3 variables" }, rig.Announcer.History.Skip(before));

        // A second press changes nothing and still says the count once: it is the person's own action.
        rig.Shell.ClearFilterCommand.Execute(null);
        Assert.Equal(new[] { "Showing 3 of 3 variables", "Showing 3 of 3 variables" }, rig.Announcer.History.Skip(before));
    }

    [Fact]
    public async Task AGroupChangeRightAfterALoad_AnnouncesNothing()
    {
        ShellTestRig rig = await LoadedRigAsync();
        int before = rig.Announcer.History.Count;

        rig.Shell.List.GroupBy = GroupBy.Kind;
        rig.Shell.OnFilterApplied();

        Assert.Equal(before, rig.Announcer.History.Count);
        Assert.Equal(LoadedSentence, rig.Shell.StatusText);

        // The same after a load with a filter in effect: the Loaded sentence gave the visible count.
        rig.Shell.List.FilterText = "Acpi";
        rig.Shell.OnFilterApplied();
        await rig.Shell.RefreshAsync();
        Assert.Equal(LoadedSentence + ". Showing 1 of 3 variables", rig.Shell.StatusText);
        int afterLoad = rig.Announcer.History.Count;
        rig.Shell.List.GroupBy = GroupBy.None;
        rig.Shell.OnFilterApplied();
        Assert.Equal(afterLoad, rig.Announcer.History.Count);
    }

    [Fact]
    public async Task AnotherAnnouncementInBetween_DoesNotMakeTheSameRowsNewsAgain()
    {
        ShellTestRig rig = await LoadedRigAsync();
        rig.Shell.List.FilterText = "Acpi";
        rig.Shell.OnFilterApplied();
        rig.Shell.OnRegionArrived(Region.Filter);
        int before = rig.Announcer.History.Count;

        rig.Shell.OnFilterApplied();
        rig.Shell.List.SortBy = SortBy.Kind;
        rig.Shell.OnFilterApplied();

        Assert.Equal(before, rig.Announcer.History.Count);
        Assert.Equal("Filter region", rig.Shell.StatusText);
    }

    [Fact]
    public async Task AFilterChangeDuringALoad_IsSaidOnce_AndTheLoadSaysTheCountOnce()
    {
        ShellTestRig rig = new();
        rig.Live.Gate = new TaskCompletionSource<SourceResult>();
        Task loading = rig.Shell.RefreshAsync();
        int before = rig.Announcer.History.Count;

        // Typed while the load runs: said against the list shown then (nothing loaded yet).
        rig.Shell.List.FilterText = "Acpi";
        rig.Shell.OnFilterApplied();
        rig.Live.Gate.SetResult(ShellTestRig.Loaded(ThreeVariables));
        await loading;

        // The view's echo and a regrouping after the load add nothing: the Loaded sentence said the count.
        rig.Shell.OnFilterApplied();
        rig.Shell.List.GroupBy = GroupBy.Kind;
        rig.Shell.OnFilterApplied();

        Assert.Equal(new[] { "Showing 0 of 0 variables", LoadedSentence + ". Showing 1 of 3 variables" }, rig.Announcer.History.Skip(before));
    }

    /// <summary>
    /// A Refused or Failed load empties the list and records the empty list as the list the view
    /// applied, so the first Group by or Sort by change after it says nothing, with or without a
    /// filter. Without that record the list shown before the refusal (3 of 3, or 1 of 3 under the
    /// filter) would still be the one the change is compared with, and the change would say
    /// "Showing 0 of 0 variables".
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("Acpi")]
    public async Task AGroupOrSortChangeAfterARefusedOrAFailedLoad_AnnouncesNothing(string filter)
    {
        ShellTestRig rig = await LoadedRigAsync();
        rig.Shell.List.FilterText = filter;
        rig.Shell.OnFilterApplied();

        rig.Live.Result = ShellTestRig.Refused(Strings.NotElevatedSentence);
        await rig.Shell.RefreshAsync();
        Assert.Equal(Strings.NotElevatedSentence, rig.Shell.StatusText);
        Assert.Equal(0, rig.Shell.List.TotalCount);
        int before = rig.Announcer.History.Count;

        rig.Shell.List.GroupBy = GroupBy.Kind;
        rig.Shell.OnFilterApplied();
        rig.Shell.List.SortBy = SortBy.Size;
        rig.Shell.OnFilterApplied();

        Assert.Equal(before, rig.Announcer.History.Count);
        Assert.Equal(Strings.NotElevatedSentence, rig.Shell.StatusText);

        // The same after a load that showed rows and then a load that failed.
        rig.Live.Result = ShellTestRig.Loaded(ThreeVariables);
        await rig.Shell.RefreshAsync();
        Assert.Equal(3, rig.Shell.List.TotalCount);
        rig.Live.Result = Failed("the driver did not answer");
        await rig.Shell.RefreshAsync();
        Assert.Equal("Loading failed: the driver did not answer", rig.Shell.StatusText);
        Assert.Equal(0, rig.Shell.List.TotalCount);
        before = rig.Announcer.History.Count;

        rig.Shell.List.GroupBy = GroupBy.None;
        rig.Shell.OnFilterApplied();
        rig.Shell.List.SortBy = SortBy.Name;
        rig.Shell.OnFilterApplied();

        Assert.Equal(before, rig.Announcer.History.Count);
        Assert.Equal("Loading failed: the driver did not answer", rig.Shell.StatusText);
    }

    /// <summary>
    /// Open baseline folder records the list it shows in the same way, whether the folder loads
    /// (the Loaded sentence gives the count, 2 variables where the live list had 3) or fails (the
    /// list is emptied): the first Group by or Sort by change after it says nothing, with or
    /// without a filter.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("Acpi")]
    public async Task AGroupOrSortChangeAfterAnOpenBaselineFolderLoad_AnnouncesNothing_WhetherItLoadedOrFailed(string filter)
    {
        ShellTestRig rig = await LoadedRigAsync();
        rig.Shell.List.FilterText = filter;
        rig.Shell.OnFilterApplied();

        rig.Baseline.Result = ShellTestRig.Loaded(new[] { ShellTestRig.Timeout, ShellTestRig.Vendor }, ShellTestRig.BaselineDescription, isOffline: true);
        await rig.Shell.OpenBaselineAsync(ShellTestRig.BaselineFolder);
        Assert.StartsWith("Loaded 2 variables from " + ShellTestRig.BaselineDescription, rig.Shell.StatusText, StringComparison.Ordinal);
        Assert.Equal(2, rig.Shell.List.TotalCount);
        int before = rig.Announcer.History.Count;

        rig.Shell.List.GroupBy = GroupBy.Kind;
        rig.Shell.OnFilterApplied();
        rig.Shell.List.SortBy = SortBy.Size;
        rig.Shell.OnFilterApplied();

        Assert.Equal(before, rig.Announcer.History.Count);

        rig.Baseline.Result = Failed("index.json is missing", ShellTestRig.BaselineDescription, isOffline: true);
        await rig.Shell.OpenBaselineAsync(ShellTestRig.BaselineFolder);
        Assert.Equal("Loading failed: index.json is missing", rig.Shell.StatusText);
        Assert.Equal(0, rig.Shell.List.TotalCount);
        before = rig.Announcer.History.Count;

        rig.Shell.List.GroupBy = GroupBy.Namespace;
        rig.Shell.OnFilterApplied();
        rig.Shell.List.SortBy = SortBy.Kind;
        rig.Shell.OnFilterApplied();

        Assert.Equal(before, rig.Announcer.History.Count);
        Assert.Equal(new[] { ShellTestRig.BaselineFolder, ShellTestRig.BaselineFolder }, rig.FactoryFolders);
    }

    /// <summary>A result of status Failed, as a source gives it: no variables and the reason as its message.</summary>
    private static SourceResult Failed(string message, string description = Strings.LiveSourceName, bool isOffline = false)
    {
        return new SourceResult(
            SourceStatus.Failed,
            description,
            isOffline,
            FirmwareType.Unknown,
            null,
            Array.Empty<FirmwareVariable>(),
            EnumerationMethod.None,
            Array.Empty<string>(),
            null,
            message);
    }

    private static async Task<ShellTestRig> LoadedRigAsync()
    {
        ShellTestRig rig = new();
        rig.Live.Result = ShellTestRig.Loaded(ThreeVariables);
        await rig.Shell.RefreshAsync();
        Assert.Equal(LoadedSentence, rig.Shell.StatusText);
        return rig;
    }
}
