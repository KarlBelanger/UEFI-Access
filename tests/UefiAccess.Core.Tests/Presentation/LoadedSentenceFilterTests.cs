// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Presentation.ViewModels;
using Xunit;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// Finding F089: the Loaded sentence continues with "Showing N of M variables" only when a filter
/// is in effect, by the same rule the list filters with (the trimmed text is not empty). A filter
/// box that holds only white space is no filter: the list shows every row and the sentence says
/// the count alone.
/// </summary>
public sealed class LoadedSentenceFilterTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t ")]
    public async Task AWhiteSpaceFilter_IsNoFilter_AndTheLoadedSentenceHasNoShowingPart(string filter)
    {
        ShellTestRig rig = new();
        rig.Shell.List.FilterText = filter;
        rig.Live.Result = ShellTestRig.Loaded(new[] { ShellTestRig.BootOrder, ShellTestRig.Timeout, ShellTestRig.Vendor });

        await rig.Shell.RefreshAsync();

        Assert.False(rig.Shell.List.IsFiltered);
        Assert.Equal(3, rig.Shell.List.VisibleCount);
        Assert.Equal("Loaded 3 variables from Live firmware (InMemory)", rig.Shell.StatusText);
    }

    [Fact]
    public async Task AFilterWithSurroundingSpaces_StillAddsTheShowingPart()
    {
        ShellTestRig rig = new();
        rig.Shell.List.FilterText = "  Acpi  ";
        rig.Live.Result = ShellTestRig.Loaded(new[] { ShellTestRig.BootOrder, ShellTestRig.Timeout, ShellTestRig.Vendor });

        await rig.Shell.RefreshAsync();

        Assert.True(rig.Shell.List.IsFiltered);
        Assert.Equal("Loaded 3 variables from Live firmware (InMemory). Showing 1 of 3 variables", rig.Shell.StatusText);
        Assert.Equal(ShellState.Loaded, rig.Shell.State);
    }
}
