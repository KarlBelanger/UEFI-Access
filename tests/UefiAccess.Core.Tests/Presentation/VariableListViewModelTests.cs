// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.ComponentModel;
using System.Text.Json;
using UefiAccess.Core.Tests.Fixtures;
using UefiAccess.Core.Variables;
using UefiAccess.Presentation.Services;
using UefiAccess.Presentation.ViewModels;
using Xunit;
using static UefiAccess.Core.Tests.Fixtures.LoadOptionFixtures;
using static UefiAccess.Core.Tests.Presentation.VariableListFixtures;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// Tests for <see cref="VariableListViewModel"/>, <see cref="VariableItemViewModel"/>,
/// <see cref="VariableGroupViewModel"/> and <see cref="VisibleListExporter"/>: filtering by every
/// field, grouping with counts and labels, every sort order, the exact accessible-name sentences,
/// the 200-character cap, ShowingText, selection events, ClearFilter and the export formats.
/// Everything runs on the synthetic snapshot in <see cref="VariableListFixtures"/>.
/// </summary>
public sealed class VariableListViewModelTests
{
    private const string WindowsBootManagerSummary = "Boot0001: Windows Boot Manager; active, category Boot; " + WindowsBootManagerPath;

    private static VariableListViewModel Loaded(GroupBy groupBy = GroupBy.Namespace, SortBy sortBy = SortBy.Name)
    {
        VariableListViewModel list = new() { GroupBy = groupBy, SortBy = sortBy };
        list.Load(Snapshot());
        return list;
    }

    private static string[] VisibleNames(VariableListViewModel list) => list.VisibleItems.Select(item => item.Name).ToArray();

    private static VariableItemViewModel Item(VariableListViewModel list, string name) => list.AllItems.Single(item => item.Name == name);

    // ---- Load and decoding ---------------------------------------------------------------

    [Fact]
    public void Load_BuildsOneRowPerVariable_DecodedThroughTheDefaultRegistry()
    {
        VariableListViewModel list = Loaded();

        Assert.Equal(Count, list.TotalCount);
        Assert.Equal(Count, list.VisibleCount);
        Assert.Equal(Count, list.AllItems.Count);
        Assert.Equal(Count, list.VisibleItems.Count);
        Assert.Equal("Showing 19 of 19 variables", list.ShowingText);

        VariableItemViewModel bootOrder = Item(list, "BootOrder");
        Assert.Equal("BootOrder", bootOrder.Kind);
        Assert.Equal(
            "BootOrder: Boot0001 (Windows Boot Manager), Boot0003 (UEFI: SanDisk Cruzer), Boot0002 (UEFI: IP4 Intel(R) Ethernet Connection I217-LM)",
            bootOrder.Summary);

        Assert.Equal("LoadOption", Item(list, "Boot0001").Kind);
        Assert.Equal("SecureBootState", Item(list, "SecureBoot").Kind);
        Assert.Equal("SecureBootState", Item(list, "db").Kind);
        Assert.Equal("OsIndications", Item(list, "OsIndicationsSupported").Kind);
        Assert.Equal("Raw", Item(list, "PlatformLang").Kind);
        Assert.Equal(string.Empty, Item(list, "PlatformLang").Summary);
        Assert.Null(Item(list, "PlatformLang").Decoded);
        Assert.NotNull(Item(list, "Timeout").Decoded);

        // AllItems is in key order (ordinal), like a VariableSet.
        Assert.Equal(list.AllItems.Select(item => item.Key).OrderBy(key => key, StringComparer.Ordinal), list.AllItems.Select(item => item.Key));
    }

    [Fact]
    public void Load_NullOrEmpty_YieldsAnEmptyListWithoutThrowing()
    {
        VariableListViewModel list = new();
        list.Load(null!);
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Groups);
        Assert.Empty(list.VisibleItems);
        Assert.Equal("Showing 0 of 0 variables", list.ShowingText);

        list.Load(Array.Empty<FirmwareVariable>());
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Groups);
    }

    [Fact]
    public void Load_ReplacesThePreviousContentAndKeepsTheSelectionByKey()
    {
        VariableListViewModel list = Loaded();
        VariableItemViewModel oldBootOrder = Item(list, "BootOrder");
        list.SelectedItem = oldBootOrder;
        List<VariableItemViewModel?> events = new();
        list.SelectionChanged += (_, item) => events.Add(item);

        list.Load(Snapshot());

        Assert.Equal(Count, list.TotalCount);
        Assert.NotNull(list.SelectedItem);
        Assert.NotSame(oldBootOrder, list.SelectedItem);
        Assert.Equal(oldBootOrder.Key, list.SelectedItem!.Key);
        Assert.Contains(list.SelectedItem, list.VisibleItems);
        Assert.Equal(new[] { list.SelectedItem }, events); // one event, straight to the new row

        events.Clear();
        list.Load(new[] { Boot0003() });
        Assert.Equal(1, list.TotalCount);
        Assert.Null(list.SelectedItem);
        Assert.Equal(new VariableItemViewModel?[] { null }, events);
        Assert.Equal("Showing 1 of 1 variable", list.ShowingText);

        events.Clear();
        list.Load(Array.Empty<FirmwareVariable>());
        Assert.Empty(events); // nothing was selected before, nothing after
    }

    [Fact]
    public void Load_UnderAnActiveFilter_KeepsRememberingAHiddenRowForClearFilter()
    {
        VariableListViewModel list = Loaded();
        list.SelectedItem = Item(list, "BootOrder");
        list.FilterText = "db";
        Assert.Null(list.SelectedItem);

        list.Load(Snapshot());
        Assert.Null(list.SelectedItem);
        Assert.Equal(new[] { "db", "dbx" }, VisibleNames(list));

        list.ClearFilter();
        Assert.NotNull(list.SelectedItem);
        Assert.Equal("BootOrder", list.SelectedItem!.Name);
        Assert.NotNull(list.FindLoaded("8be4df61-93ca-11d2-aa0d-00e098032b8c:BootOrder"));
        Assert.Null(list.FindLoaded("no-such-key"));
    }

    // ---- Filtering -----------------------------------------------------------------------

    [Fact]
    public void Filter_ByName_IsCaseInsensitive()
    {
        VariableListViewModel list = Loaded();

        list.FilterText = "platformlang";

        Assert.Equal(new[] { "PlatformLang" }, VisibleNames(list));
        Assert.Equal(1, list.VisibleCount);
        Assert.Equal(Count, list.TotalCount);
        Assert.Equal("Showing 1 of 19 variables", list.ShowingText);
    }

    [Fact]
    public void Filter_ByGuid_MatchesTheLowercaseGuidText()
    {
        VariableListViewModel list = Loaded();

        list.FilterText = "D719B2CB";
        Assert.Equal(new[] { "db", "dbx" }, VisibleNames(list));

        list.FilterText = "12345678-1234";
        Assert.Equal(new[] { "VendorConfig" }, VisibleNames(list));
    }

    [Fact]
    public void Filter_ByNamespaceLabel_MatchesKnownAndUnknownLabels()
    {
        VariableListViewModel list = Loaded();

        list.FilterText = "image security";
        Assert.Equal(new[] { "db", "dbx" }, VisibleNames(list));

        list.FilterText = "unknown vendor";
        Assert.Equal(new[] { "VendorConfig" }, VisibleNames(list));

        list.FilterText = "machine owner";
        Assert.Equal(new[] { "MokListRT" }, VisibleNames(list));
    }

    [Fact]
    public void Filter_ByKind_MatchesTheDecoderKind()
    {
        VariableListViewModel list = Loaded();

        list.FilterText = "loadoption";
        Assert.Equal(new[] { "Boot0001", "Boot0002", "Boot0003", "Boot0004" }, VisibleNames(list));

        // "BootOrder" is the kind of BootOrder, BootCurrent and Timeout.
        list.FilterText = "bootorder";
        Assert.Equal(new[] { "BootCurrent", "BootOrder", "Timeout" }, VisibleNames(list));

        list.FilterText = "raw";
        Assert.Equal(new[] { "ConOut", "PlatformLang", "WindowsBootChainSvn", "MokListRT", "VendorConfig" }, VisibleNames(list));
    }

    [Fact]
    public void Filter_BySummary_MatchesTheDecodedText()
    {
        VariableListViewModel list = Loaded();

        // The USB stick's own summary and BootOrder's resolved description both mention SanDisk.
        list.FilterText = "sandisk";
        Assert.Equal(new[] { "Boot0003", "BootOrder" }, VisibleNames(list));

        list.FilterText = "Secure Boot is enabled";
        Assert.Equal(new[] { "SecureBoot" }, VisibleNames(list));

        list.FilterText = "signature list";
        Assert.Equal(new[] { "KEK", "PK", "db", "dbx" }, VisibleNames(list));
    }

    [Fact]
    public void Filter_NoMatch_ShowsNothing_AndEmptyOrWhiteSpaceShowsEverything()
    {
        VariableListViewModel list = Loaded();

        list.FilterText = "zzz-no-such-text";
        Assert.Empty(list.VisibleItems);
        Assert.Empty(list.Groups);
        Assert.Equal(0, list.VisibleCount);
        Assert.Equal("Showing 0 of 19 variables", list.ShowingText);
        Assert.True(list.IsFiltered);

        list.FilterText = "   ";
        Assert.Equal(Count, list.VisibleCount);
        Assert.False(list.IsFiltered);

        list.FilterText = null!;
        Assert.Equal(string.Empty, list.FilterText);
        Assert.Equal(Count, list.VisibleCount);

        list.FilterText = "  db  ";
        Assert.Equal(new[] { "db", "dbx" }, VisibleNames(list));
    }

    [Fact]
    public void Filter_DoesNotMatchFlagsSizeOrKey()
    {
        VariableListViewModel list = Loaded();

        list.FilterText = "NV BS RT";
        Assert.Empty(list.VisibleItems);

        list.FilterText = "179 bytes";
        Assert.Empty(list.VisibleItems);

        list.FilterText = "b8c:Boot";
        Assert.Empty(list.VisibleItems);
    }

    [Fact]
    public void Filter_RaisesPropertyChangedForTheDerivedProperties()
    {
        VariableListViewModel list = Loaded();
        List<string> changed = new();
        list.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

        list.FilterText = "db";

        Assert.Contains(nameof(VariableListViewModel.FilterText), changed);
        Assert.Contains(nameof(VariableListViewModel.VisibleItems), changed);
        Assert.Contains(nameof(VariableListViewModel.VisibleCount), changed);
        Assert.Contains(nameof(VariableListViewModel.ShowingText), changed);
        Assert.Contains(nameof(VariableListViewModel.IsFiltered), changed);

        changed.Clear();
        list.FilterText = "db";
        Assert.Empty(changed);
    }

    // ---- Grouping ------------------------------------------------------------------------

    [Fact]
    public void GroupByNamespace_OrdersEfiGlobalFirstThenAlphabetical_WithCounts()
    {
        VariableListViewModel list = Loaded(GroupBy.Namespace);

        string[] labels = list.Groups.Select(group => group.Label).ToArray();
        Assert.Equal(
            new[]
            {
                "EFI global variable",
                "Image security database",
                "Microsoft",
                "Shim / Machine Owner Keys",
                "Unknown vendor 12345678-1234-1234-1234-123456789abc",
            },
            labels);

        Assert.Equal(new[] { GlobalCount, 2, 1, 1, 1 }, list.Groups.Select(group => group.Count).ToArray());
        Assert.Equal("EFI global variable, 14 items", list.Groups[0].AccessibleName);
        Assert.Equal("Image security database, 2 items", list.Groups[1].AccessibleName);
        Assert.Equal(KnownGuids.EfiGlobalVariable.ToString("D"), list.Groups[0].Key);
        Assert.Equal("d719b2cb-3d3a-4596-a3bc-dad00e67656f", list.Groups[1].Key);
        Assert.All(list.Groups, group => Assert.True(group.IsExpanded));
        Assert.Equal(Count, list.Groups.Sum(group => group.Count));

        // VisibleItems is the groups flattened in display order.
        Assert.Equal(list.Groups.SelectMany(group => group.Items), list.VisibleItems);
    }

    [Fact]
    public void GroupByKind_OrdersKindsAlphabetically_WithCounts()
    {
        VariableListViewModel list = Loaded(GroupBy.Kind);

        Assert.Equal(new[] { "BootOrder", "LoadOption", "OsIndications", "Raw", "SecureBootState" }, list.Groups.Select(group => group.Label).ToArray());
        Assert.Equal(new[] { 3, 4, 1, 5, 6 }, list.Groups.Select(group => group.Count).ToArray());
        Assert.Equal("LoadOption, 4 items", list.Groups[1].AccessibleName);
        Assert.Equal("Raw", list.Groups[3].Key);
        Assert.Equal(new[] { "Boot0001", "Boot0002", "Boot0003", "Boot0004" }, list.Groups[1].Items.Select(item => item.Name).ToArray());
    }

    [Fact]
    public void GroupByNone_IsOneGroupNamedAllVariables()
    {
        VariableListViewModel list = Loaded(GroupBy.None);

        VariableGroupViewModel only = Assert.Single(list.Groups);
        Assert.Equal("All variables", only.Label);
        Assert.Equal(string.Empty, only.Key);
        Assert.Equal(Count, only.Count);
        Assert.Equal("All variables, 19 items", only.AccessibleName);

        list.FilterText = "nothing-matches";
        Assert.Empty(list.Groups);
    }

    [Fact]
    public void GroupBy_ChangesRebuildSynchronously_AndFilterAppliesInsideGroups()
    {
        VariableListViewModel list = Loaded(GroupBy.Namespace);
        list.FilterText = "boot";

        // "boot" matches the Boot#### names, BootOrder and BootCurrent, the kinds "BootOrder" (so Timeout)
        // and "SecureBootState" (so the six Secure Boot variables), WindowsBootChainSvn by name, and
        // OsIndicationsSupported by its BootToFwUi summary: 15 of the 19 rows.
        list.GroupBy = GroupBy.Kind;
        Assert.Equal(new[] { "BootOrder", "LoadOption", "OsIndications", "Raw", "SecureBootState" }, list.Groups.Select(group => group.Label).ToArray());
        Assert.Equal(new[] { "BootCurrent", "BootOrder", "Timeout" }, list.Groups[0].Items.Select(item => item.Name).ToArray());
        Assert.Equal(new[] { "OsIndicationsSupported" }, list.Groups[2].Items.Select(item => item.Name).ToArray());
        Assert.Equal(new[] { "WindowsBootChainSvn" }, list.Groups[3].Items.Select(item => item.Name).ToArray());
        Assert.Equal(new[] { "db", "dbx", "KEK", "PK", "SecureBoot", "SetupMode" }, list.Groups[4].Items.Select(item => item.Name).ToArray());
        Assert.Equal("Raw, 1 item", list.Groups[3].AccessibleName);

        list.GroupBy = GroupBy.None;
        Assert.Single(list.Groups);
        Assert.Equal(15, list.VisibleCount);
        Assert.Equal("Showing 15 of 19 variables", list.ShowingText);

        // "t" is in no GUID; VendorConfig is the only row whose name, label, kind and summary all lack it,
        // so its whole group disappears while the other four groups stay.
        list.GroupBy = GroupBy.Namespace;
        list.FilterText = "t";
        Assert.Equal(4, list.Groups.Count);
        Assert.DoesNotContain(list.Groups, group => group.Label.StartsWith("Unknown vendor", StringComparison.Ordinal));
        Assert.Equal(Count - 1, list.VisibleCount);
    }

    [Fact]
    public void GroupHeaderAndShowingText_UseTheSingularForOne()
    {
        VariableListViewModel list = new();
        list.Load(new[] { Boot0003() });

        VariableGroupViewModel only = Assert.Single(list.Groups);
        Assert.Equal(1, only.Count);
        Assert.Equal("EFI global variable, 1 item", only.AccessibleName);
        Assert.Equal("Showing 1 of 1 variable", list.ShowingText);

        list.FilterText = "nothing-matches";
        Assert.Equal("Showing 0 of 1 variable", list.ShowingText);

        list.Load(Snapshot());
        Assert.Equal("Showing 0 of 19 variables", list.ShowingText);
        list.FilterText = string.Empty;
        Assert.Equal("Showing 19 of 19 variables", list.ShowingText);
        Assert.Equal("Image security database, 2 items", list.Groups[1].AccessibleName);
    }

    [Fact]
    public void GroupExpansion_IsRememberedAcrossRebuilds()
    {
        VariableListViewModel list = Loaded(GroupBy.Namespace);
        list.Groups[1].IsExpanded = false;

        list.SortBy = SortBy.Size;
        Assert.True(list.Groups[0].IsExpanded);
        Assert.False(list.Groups[1].IsExpanded);

        list.FilterText = "db";
        VariableGroupViewModel security = Assert.Single(list.Groups);
        Assert.Equal("Image security database", security.Label);
        Assert.False(security.IsExpanded);

        list.FilterText = string.Empty;
        Assert.False(list.Groups[1].IsExpanded);
        list.Groups[1].IsExpanded = true;
        list.GroupBy = GroupBy.Kind;
        list.GroupBy = GroupBy.Namespace;
        Assert.True(list.Groups[1].IsExpanded);
    }

    // ---- Sorting -------------------------------------------------------------------------

    [Fact]
    public void SortByName_IsOrdinalIgnoreCase_InsideEveryGroup()
    {
        VariableListViewModel list = Loaded(GroupBy.Namespace, SortBy.Name);

        Assert.Equal(
            new[]
            {
                "Boot0001", "Boot0002", "Boot0003", "Boot0004", "BootCurrent", "BootOrder", "ConOut", "KEK",
                "OsIndicationsSupported", "PK", "PlatformLang", "SecureBoot", "SetupMode", "Timeout",
            },
            list.Groups[0].Items.Select(item => item.Name).ToArray());
        Assert.Equal(new[] { "db", "dbx" }, list.Groups[1].Items.Select(item => item.Name).ToArray());
    }

    [Fact]
    public void SortByNamespace_OrdersByLabelThenName()
    {
        VariableListViewModel list = Loaded(GroupBy.None, SortBy.Namespace);

        string[] names = VisibleNames(list);
        Assert.Equal("Boot0001", names[0]);
        Assert.Equal("Timeout", names[GlobalCount - 1]);
        Assert.Equal(new[] { "db", "dbx", "WindowsBootChainSvn", "MokListRT", "VendorConfig" }, names[GlobalCount..]);

        string[] labels = list.VisibleItems.Select(item => item.NamespaceLabel).Distinct().ToArray();
        Assert.Equal(
            new[]
            {
                "EFI global variable",
                "Image security database",
                "Microsoft",
                "Shim / Machine Owner Keys",
                "Unknown vendor 12345678-1234-1234-1234-123456789abc",
            },
            labels);
    }

    [Fact]
    public void SortBySize_IsDescendingThenName()
    {
        VariableListViewModel list = Loaded(GroupBy.None, SortBy.Size);

        string[] names = VisibleNames(list);
        Assert.Equal(new[] { "KEK", "db", "PK", "Boot0002", "Boot0001", "dbx", "Boot0003", "Boot0004", "ConOut", "OsIndicationsSupported" }, names[..10]);
        Assert.Equal(new[] { "BootOrder", "PlatformLang" }, names[10..12]);
        Assert.Equal(new[] { "SecureBoot", "SetupMode" }, names[^2..]);
        Assert.Equal(new[] { "BootCurrent", "Timeout" }, names[^4..^2]);

        int[] sizes = list.VisibleItems.Select(item => item.Size).ToArray();
        Assert.Equal(179, Item(list, "Boot0001").Size);
        Assert.Equal(sizes.OrderByDescending(size => size), sizes);
        Assert.Equal(sizes.Distinct().Count() + 3, sizes.Length); // three size ties: 6, 2 and 1 bytes
    }

    [Fact]
    public void SortByKind_OrdersByKindThenName()
    {
        VariableListViewModel list = Loaded(GroupBy.None, SortBy.Kind);

        Assert.Equal(
            new[]
            {
                "BootCurrent", "BootOrder", "Timeout",
                "Boot0001", "Boot0002", "Boot0003", "Boot0004",
                "OsIndicationsSupported",
                "ConOut", "MokListRT", "PlatformLang", "VendorConfig", "WindowsBootChainSvn",
                "db", "dbx", "KEK", "PK", "SecureBoot", "SetupMode",
            },
            VisibleNames(list));
    }

    [Fact]
    public void FilterGroupAndSortSetters_RebuildBeforeRaisingPropertyChanged()
    {
        // The window's filter box is bound TwoWay to FilterText and announces the count from its
        // TextChanged handler, which runs inside the PropertyChanged notification: the counts,
        // groups and ShowingText must already describe the new state at that moment.
        VariableListViewModel list = Loaded(GroupBy.Namespace, SortBy.Name);
        Dictionary<string, (string Showing, int Visible, int Groups, string FirstName)> seen = new(StringComparer.Ordinal);
        list.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(VariableListViewModel.FilterText) or nameof(VariableListViewModel.GroupBy) or nameof(VariableListViewModel.SortBy))
            {
                seen[e.PropertyName] = (list.ShowingText, list.VisibleCount, list.Groups.Count, list.VisibleItems.Count > 0 ? list.VisibleItems[0].Name : string.Empty);
            }
        };

        list.FilterText = "db";
        Assert.Equal(("Showing 2 of 19 variables", 2, 1, "db"), seen[nameof(VariableListViewModel.FilterText)]);

        list.FilterText = string.Empty;
        Assert.Equal(("Showing 19 of 19 variables", 19, 5, "Boot0001"), seen[nameof(VariableListViewModel.FilterText)]);

        list.GroupBy = GroupBy.None;
        Assert.Equal(("Showing 19 of 19 variables", 19, 1, "Boot0001"), seen[nameof(VariableListViewModel.GroupBy)]);

        list.SortBy = SortBy.Size;
        Assert.Equal(("Showing 19 of 19 variables", 19, 1, "KEK"), seen[nameof(VariableListViewModel.SortBy)]);

        // Assigning the same value again neither rebuilds nor notifies.
        seen.Clear();
        list.FilterText = string.Empty;
        list.FilterText = null!;
        list.GroupBy = GroupBy.None;
        list.SortBy = SortBy.Size;
        Assert.Empty(seen);
        Assert.Equal(string.Empty, list.FilterText);
    }

    [Fact]
    public void SortBy_ChangeRebuildsSynchronouslyAndRaisesPropertyChanged()
    {
        VariableListViewModel list = Loaded(GroupBy.Namespace, SortBy.Name);
        List<string> changed = new();
        list.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

        list.SortBy = SortBy.Size;

        Assert.Equal("KEK", list.Groups[0].Items[0].Name);
        Assert.Contains(nameof(VariableListViewModel.SortBy), changed);
        Assert.Contains(nameof(VariableListViewModel.VisibleItems), changed);
        Assert.Equal(SortBy.Size, list.SortBy);
        Assert.Equal(GroupBy.Namespace, list.GroupBy);
    }

    [Fact]
    public void ComparerFor_BreaksTiesByKey()
    {
        VariableListViewModel list = new();
        list.Load(new[]
        {
            new FirmwareVariable("Same", UnknownVendor, NvBsRt, new byte[] { 1 }),
            Global("Same", new byte[] { 1 }),
        });

        IComparer<VariableItemViewModel> byName = VariableListViewModel.ComparerFor(SortBy.Name);
        IComparer<VariableItemViewModel> bySize = VariableListViewModel.ComparerFor(SortBy.Size);
        VariableItemViewModel first = list.AllItems[0];
        VariableItemViewModel second = list.AllItems[1];

        Assert.True(byName.Compare(first, second) < 0);
        Assert.True(bySize.Compare(first, second) < 0);
        Assert.Equal(0, byName.Compare(first, first));
        Assert.StartsWith("12345678", first.Key, StringComparison.Ordinal);
        Assert.StartsWith("8be4df61", second.Key, StringComparison.Ordinal);

        Assert.True(VariableListViewModel.CompareGroupLabels("EFI global variable", "Aardvark") < 0);
        Assert.True(VariableListViewModel.CompareGroupLabels("Aardvark", "EFI global variable") > 0);
        Assert.True(VariableListViewModel.CompareGroupLabels("aardvark", "Beetle") < 0);
        Assert.Equal(0, VariableListViewModel.CompareGroupLabels("Raw", "Raw"));
    }

    // ---- Selection -----------------------------------------------------------------------

    [Fact]
    public void SelectedItem_RaisesSelectionChangedOnlyWhenTheValueChanges()
    {
        VariableListViewModel list = Loaded();
        List<VariableItemViewModel?> events = new();
        list.SelectionChanged += (_, item) => events.Add(item);
        List<string> changed = new();
        list.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);
        VariableItemViewModel bootOrder = Item(list, "BootOrder");

        list.SelectedItem = bootOrder;
        list.SelectedItem = bootOrder;
        list.SelectedItem = null;

        Assert.Equal(new VariableItemViewModel?[] { bootOrder, null }, events);
        Assert.Equal(2, changed.Count(name => name == nameof(VariableListViewModel.SelectedItem)));
    }

    [Fact]
    public void TrySelectByKey_SelectsVisibleRowsOnly()
    {
        VariableListViewModel list = Loaded();
        string bootOrderKey = "8be4df61-93ca-11d2-aa0d-00e098032b8c:BootOrder";

        Assert.True(list.TrySelectByKey(bootOrderKey));
        Assert.Equal("BootOrder", list.SelectedItem!.Name);

        Assert.True(list.TrySelectByKey("8BE4DF61-93CA-11D2-AA0D-00E098032B8C:Timeout"));
        Assert.Equal("Timeout", list.SelectedItem!.Name);

        Assert.False(list.TrySelectByKey("8be4df61-93ca-11d2-aa0d-00e098032b8c:timeout"));
        Assert.False(list.TrySelectByKey("no-such-key"));
        Assert.False(list.TrySelectByKey(string.Empty));
        Assert.False(list.TrySelectByKey(null!));
        Assert.Equal("Timeout", list.SelectedItem!.Name);

        list.FilterText = "db";
        Assert.Null(list.SelectedItem);
        Assert.False(list.TrySelectByKey(bootOrderKey));
        Assert.True(list.TrySelectByKey("d719b2cb-3d3a-4596-a3bc-dad00e67656f:dbx"));
        Assert.Equal("dbx", list.SelectedItem!.Name);
        Assert.NotNull(list.FindVisible("d719b2cb-3d3a-4596-a3bc-dad00e67656f:db"));
        Assert.Null(list.FindVisible(bootOrderKey));
    }

    [Fact]
    public void Filter_ThatHidesTheSelectedRow_ClearsTheSelectionAndRaisesTheEvent()
    {
        VariableListViewModel list = Loaded();
        list.SelectedItem = Item(list, "BootOrder");
        List<VariableItemViewModel?> events = new();
        list.SelectionChanged += (_, item) => events.Add(item);

        list.FilterText = "boot";
        Assert.NotNull(list.SelectedItem);
        Assert.Empty(events);

        list.FilterText = "db";
        Assert.Null(list.SelectedItem);
        Assert.Equal(new VariableItemViewModel?[] { null }, events);
    }

    [Fact]
    public void ClearFilter_KeepsTheSelectionWhenTheRowIsStillVisible()
    {
        VariableListViewModel list = Loaded();
        list.FilterText = "boot";
        VariableItemViewModel bootOrder = Item(list, "BootOrder");
        list.SelectedItem = bootOrder;
        List<VariableItemViewModel?> events = new();
        list.SelectionChanged += (_, item) => events.Add(item);

        list.ClearFilter();

        Assert.Equal(string.Empty, list.FilterText);
        Assert.Equal(Count, list.VisibleCount);
        Assert.Same(bootOrder, list.SelectedItem);
        Assert.Empty(events);
    }

    [Fact]
    public void ClearFilter_RestoresTheRowAFilterHadHidden()
    {
        VariableListViewModel list = Loaded();
        VariableItemViewModel bootOrder = Item(list, "BootOrder");
        list.SelectedItem = bootOrder;
        list.FilterText = "db";
        Assert.Null(list.SelectedItem);
        List<VariableItemViewModel?> events = new();
        list.SelectionChanged += (_, item) => events.Add(item);

        list.ClearFilter();

        Assert.Same(bootOrder, list.SelectedItem);
        Assert.Equal(new VariableItemViewModel?[] { bootOrder }, events);
    }

    [Fact]
    public void ClearFilter_SelectsTheFirstVisibleRowWhenNothingWasSelected()
    {
        VariableListViewModel list = Loaded();
        list.FilterText = "db";

        list.ClearFilter();

        Assert.NotNull(list.SelectedItem);
        Assert.Same(list.VisibleItems[0], list.SelectedItem);
        Assert.Equal("Boot0001", list.SelectedItem!.Name);
    }

    [Fact]
    public void ClearFilter_SelectsTheFirstVisibleRowWhenTheRememberedRowIsGone()
    {
        VariableListViewModel list = Loaded();
        list.SelectedItem = Item(list, "VendorConfig");
        list.FilterText = "db";
        Assert.Null(list.SelectedItem);

        list.Load(new[] { Boot0003(), BootOrder() });
        list.ClearFilter();

        Assert.NotNull(list.SelectedItem);
        Assert.Equal("Boot0003", list.SelectedItem!.Name);
        Assert.Same(list.VisibleItems[0], list.SelectedItem);
    }

    [Fact]
    public void ClearFilter_OnAnEmptyList_LeavesNothingSelected()
    {
        VariableListViewModel list = new();
        list.FilterText = "x";

        list.ClearFilter();

        Assert.Null(list.SelectedItem);
        Assert.Equal(string.Empty, list.FilterText);
    }

    // ---- Rows: accessible names and projections -------------------------------------------

    [Fact]
    public void AccessibleName_WindowsBootManagerFixture_IsTheExactSentenceCappedAt200()
    {
        VariableListViewModel list = Loaded();
        VariableItemViewModel row = Item(list, "Boot0001");

        string fullSentence = "Boot0001, EFI global variable, NV BS RT, 179 bytes, " + WindowsBootManagerSummary;
        Assert.Equal(206, fullSentence.Length);
        Assert.Equal(
            "Boot0001, EFI global variable, NV BS RT, 179 bytes, Boot0001: Windows Boot Manager; active, category Boot; HD(1,GPT,6f6e5d1c-8f7a-4d33-9a2b-3c4d5e6f7081,0x800,0x32000)/File(\\EFI\\Microsoft\\Boot\\bootmg…",
            row.AccessibleName);
        Assert.Equal(VariableItemViewModel.AccessibleNameMaxLength, row.AccessibleName.Length);
        Assert.Equal(fullSentence[..199] + "…", row.AccessibleName);

        Assert.Equal("Boot0001", row.Name);
        Assert.Equal("8be4df61-93ca-11d2-aa0d-00e098032b8c", row.GuidText);
        Assert.Equal("8be4df61-93ca-11d2-aa0d-00e098032b8c:Boot0001", row.Key);
        Assert.Equal("EFI global variable", row.NamespaceLabel);
        Assert.True(row.Namespace.IsKnown);
        Assert.Equal("NV BS RT", row.Flags);
        Assert.Equal(179, row.Size);
        Assert.Equal("179 bytes", row.SizeText);
        Assert.Equal("LoadOption", row.Kind);
        Assert.Equal(WindowsBootManagerSummary, row.Summary);
    }

    [Fact]
    public void AccessibleName_UsbStickFixture_IsTheExactUntruncatedSentence()
    {
        VariableListViewModel list = Loaded();

        Assert.Equal(
            "Boot0003, EFI global variable, NV BS RT, 76 bytes, Boot0003: UEFI: SanDisk Cruzer; active, category Boot; PciRoot(0x0)/Pci(0x14,0x0)/USB(3,0)",
            Item(list, "Boot0003").AccessibleName);

        // BootOrder as a baseline folder shows it (and as Alpha 1 did): no item status, so the
        // sentence of Alpha 1, uncut. Loaded live, it carries more words (next test).
        VariableListViewModel baseline = new();
        baseline.Load(Snapshot(), offline: true, machineConfirmed: false);
        Assert.Equal(
            "BootOrder, EFI global variable, NV BS RT, 6 bytes, BootOrder: Boot0001 (Windows Boot Manager), Boot0003 (UEFI: SanDisk Cruzer), Boot0002 (UEFI: IP4 Intel(R) Ethernet Connection I217-LM)",
            Item(baseline, "BootOrder").AccessibleName);
        Assert.Equal("SecureBoot, EFI global variable, BS RT, 1 byte, SecureBoot=1: Secure Boot is enabled", Item(list, "SecureBoot").AccessibleName);
        Assert.Equal("Timeout, EFI global variable, NV BS RT, 2 bytes, Timeout: 2 seconds (delay before booting the default entry)", Item(list, "Timeout").AccessibleName);
        Assert.Equal("PK, EFI global variable, NV BS RT TA, 244 bytes, PK: 244 bytes, 1 signature list, 1 signature (X509)", Item(list, "PK").AccessibleName);
    }

    /// <summary>
    /// <see cref="VariableListViewModel.Load(IReadOnlyList{FirmwareVariable})"/> judges the rows on
    /// a machine that is not the contracted one, so the well-formed BootOrder carries the item
    /// status "Not changeable on this machine" and its words after the name
    /// (decided 25 September 2026, worded so since 7 October 2026); the sentence is then 217
    /// characters and is cut at 200.
    /// </summary>
    [Fact]
    public void AccessibleName_BootOrderOnAnotherMachine_CarriesTheItemStatusWords_CappedAt200()
    {
        VariableListViewModel list = Loaded();
        VariableItemViewModel row = Item(list, "BootOrder");

        string fullSentence = "BootOrder, not changeable on this machine, EFI global variable, NV BS RT, 6 bytes, BootOrder: Boot0001 (Windows Boot Manager), Boot0003 (UEFI: SanDisk Cruzer), Boot0002 (UEFI: IP4 Intel(R) Ethernet Connection I217-LM)";
        Assert.Equal(217, fullSentence.Length);
        Assert.Equal(UefiAccess.Core.Safety.Writability.WrongMachine, row.Writability.Verdict);
        Assert.Equal(UefiAccess.Core.Safety.WritabilitySentences.ItemStatusElsewhere, row.ItemStatus);
        Assert.Equal(
            "BootOrder, not changeable on this machine, EFI global variable, NV BS RT, 6 bytes, BootOrder: Boot0001 (Windows Boot Manager), Boot0003 (UEFI: SanDisk Cruzer), Boot0002 (UEFI: IP4 Intel(R) Ethernet C…",
            row.AccessibleName);
        Assert.Equal(fullSentence[..199] + "…", row.AccessibleName);
        Assert.Equal(VariableItemViewModel.AccessibleNameMaxLength, row.AccessibleName.Length);

        // Every other row of the snapshot, the Boot#### entries included, carries no item status.
        Assert.All(list.AllItems.Where(item => item.Name != "BootOrder"), item => Assert.Equal(string.Empty, item.ItemStatus));
    }

    [Fact]
    public void AccessibleName_RawUnknownVendorVariable_HasNoSummaryPart()
    {
        VariableListViewModel list = Loaded();
        VariableItemViewModel row = Item(list, "VendorConfig");

        Assert.Equal("VendorConfig, Unknown vendor 12345678-1234-1234-1234-123456789abc, NV BS RT, 3 bytes", row.AccessibleName);
        Assert.Equal("Raw", row.Kind);
        Assert.Equal(string.Empty, row.Summary);
        Assert.False(row.Namespace.IsKnown);
        Assert.Equal("PlatformLang, EFI global variable, NV BS RT, 6 bytes", Item(list, "PlatformLang").AccessibleName);
        Assert.Equal("MokListRT, Shim / Machine Owner Keys, BS RT, 5 bytes", Item(list, "MokListRT").AccessibleName);
        Assert.Equal("WindowsBootChainSvn, Microsoft, NV BS RT, 4 bytes", Item(list, "WindowsBootChainSvn").AccessibleName);
    }

    [Fact]
    public void AccessibleName_IsCappedAt200CharactersWithAnEllipsis()
    {
        string longName = new('N', 300);
        VariableItemViewModel longRow = new(new FirmwareVariable(longName, UnknownVendor, NvBsRt, new byte[2]), NamespaceCatalog.Describe(UnknownVendor), null);
        Assert.Equal(200, longRow.AccessibleName.Length);
        Assert.EndsWith("…", longRow.AccessibleName, StringComparison.Ordinal);
        Assert.StartsWith(new string('N', 199), longRow.AccessibleName, StringComparison.Ordinal);

        // A 24-entry BootOrder produces a summary far beyond the cap; the row is still one sentence.
        byte[] order = Concat(Enumerable.Range(1, 24).Select(i => U16((ushort)i)).ToArray());
        VariableListViewModel list = new();
        list.Load(new[] { Global("BootOrder", order), Boot0001(), Boot0002(), Boot0003(), Boot0004() });
        VariableItemViewModel bootOrder = Item(list, "BootOrder");
        Assert.True(bootOrder.Summary.Length > 200);
        Assert.Equal(200, bootOrder.AccessibleName.Length);
        Assert.EndsWith("…", bootOrder.AccessibleName, StringComparison.Ordinal);
        Assert.StartsWith("BootOrder, EFI global variable, NV BS RT, 48 bytes, BootOrder: Boot0001 (Windows Boot Manager)", bootOrder.AccessibleName, StringComparison.Ordinal);

        // Exactly 200 characters is not truncated.
        string name200 = new('X', 200 - ", EFI global variable, NV BS RT, 1 byte".Length);
        VariableItemViewModel exact = new(Global(name200, new byte[1]), NamespaceCatalog.Describe(KnownGuids.EfiGlobalVariable), null);
        Assert.Equal(200, exact.AccessibleName.Length);
        Assert.DoesNotContain("…", exact.AccessibleName, StringComparison.Ordinal);
    }

    [Fact]
    public void Row_OmitsEmptyFlags_FlattensSummaryLines_AndValidatesArguments()
    {
        NamespaceInfo global = NamespaceCatalog.Describe(KnownGuids.EfiGlobalVariable);
        VariableItemViewModel noFlags = new(new FirmwareVariable("Plain", KnownGuids.EfiGlobalVariable, VariableAttributes.None, new byte[1]), global, null);
        Assert.Equal(string.Empty, noFlags.Flags);
        Assert.Equal("Plain, EFI global variable, 1 byte", noFlags.AccessibleName);
        Assert.Equal("1 byte", noFlags.SizeText);

        VariableItemViewModel empty = new(Global("Empty", Array.Empty<byte>()), global, null);
        Assert.Equal("0 bytes", empty.SizeText);
        Assert.Equal(0, empty.Size);

        Core.Decoders.DecodedVariable decoded = new("k", "Custom", "line one\r\nline two\tend", Array.Empty<Core.Decoders.DecodedField>(), null, null);
        VariableItemViewModel multi = new(Global("Multi", new byte[3]), global, decoded);
        Assert.Equal("line one line two end", multi.Summary);
        Assert.Equal("Custom", multi.Kind);
        Assert.Equal("Multi, EFI global variable, NV BS RT, 3 bytes, line one line two end", multi.AccessibleName);

        Core.Decoders.DecodedVariable blankKind = new("k", string.Empty, string.Empty, Array.Empty<Core.Decoders.DecodedField>(), null, null);
        VariableItemViewModel raw = new(Global("Blank", new byte[3]), global, blankKind);
        Assert.Equal("Raw", raw.Kind);
        Assert.Equal("Blank, EFI global variable, NV BS RT, 3 bytes", raw.AccessibleName);

        Assert.Throws<ArgumentNullException>(() => new VariableItemViewModel(null!, global, null));
        Assert.Throws<ArgumentNullException>(() => new VariableItemViewModel(Global("X", new byte[1]), null!, null));
        Assert.Equal("1 byte", VariableItemViewModel.FormatSize(1));
        Assert.Equal("21148 bytes", VariableItemViewModel.FormatSize(21148));
        Assert.Equal(raw.Key, raw.ToString());
    }

    [Fact]
    public void Group_ExposesLabelCountAndAccessibleName()
    {
        VariableListViewModel list = Loaded();
        VariableGroupViewModel group = new("kind:Raw", "Raw", list.AllItems.Where(item => item.Kind == "Raw").ToList());

        Assert.Equal("kind:Raw", group.Key);
        Assert.Equal("Raw", group.Label);
        Assert.Equal(5, group.Count);
        Assert.Equal("Raw, 5 items", group.AccessibleName);
        Assert.True(group.IsExpanded);

        List<string> changed = new();
        group.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);
        group.IsExpanded = false;
        group.IsExpanded = false;
        Assert.Equal(new[] { nameof(VariableGroupViewModel.IsExpanded) }, changed);
        Assert.False(group.IsExpanded);

        Assert.Throws<ArgumentNullException>(() => new VariableGroupViewModel(null!, "x", Array.Empty<VariableItemViewModel>()));
        Assert.Throws<ArgumentNullException>(() => new VariableGroupViewModel("x", null!, Array.Empty<VariableItemViewModel>()));
        Assert.Throws<ArgumentNullException>(() => new VariableGroupViewModel("x", "x", null!));
    }

    // ---- Export --------------------------------------------------------------------------

    [Fact]
    public void ToPlainText_RendersAlignedColumnsLikeTheCli()
    {
        VariableListViewModel list = Loaded(GroupBy.None, SortBy.Name);
        list.FilterText = "d719b2cb";
        Assert.Equal(new[] { "db", "dbx" }, VisibleNames(list));

        string text = VisibleListExporter.ToPlainText(list.VisibleItems);

        string[] lines = text.Split(Environment.NewLine);
        Assert.Equal(4, lines.Length);
        Assert.Equal(string.Empty, lines[3]);
        Assert.Equal("Name Namespace               Flags       Size Kind            Summary", lines[0]);
        Assert.Equal("db   Image security database NV BS RT TA 336  SecureBootState db: 336 bytes, 2 signature lists, 4 signatures (X509, SHA256)", lines[1]);
        Assert.Equal("dbx  Image security database NV BS RT TA 172  SecureBootState dbx: 172 bytes, 1 signature list, 3 signatures (SHA256)", lines[2]);
        Assert.All(lines, line => Assert.Equal(line.TrimEnd(), line));
        Assert.DoesNotContain('│', text);
        Assert.DoesNotContain("…", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ToPlainText_KeepsEveryRowOnOneLineAndNeverTruncates()
    {
        VariableListViewModel list = Loaded(GroupBy.None, SortBy.Name);

        string text = VisibleListExporter.ToPlainText(list.VisibleItems);

        string[] lines = text.TrimEnd().Split(Environment.NewLine);
        Assert.Equal(Count + 1, lines.Length);
        Assert.Contains(lines, line => line.StartsWith("Boot0001 ", StringComparison.Ordinal) && line.EndsWith(WindowsBootManagerPath, StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("VendorConfig ", StringComparison.Ordinal) && line.Contains("Unknown vendor 12345678-1234-1234-1234-123456789abc", StringComparison.Ordinal) && line.EndsWith(" Raw", StringComparison.Ordinal));

        int nameWidth = lines.Max(line => line.Split(' ')[0].Length) + 1;
        Assert.All(lines, line => Assert.Equal(' ', line[nameWidth - 1]));
        Assert.All(lines, line => Assert.NotEqual(' ', line[nameWidth]));
    }

    [Fact]
    public void ToPlainText_EmptyOrNull_IsEmpty()
    {
        Assert.Equal(string.Empty, VisibleListExporter.ToPlainText(Array.Empty<VariableItemViewModel>()));
        Assert.Equal(string.Empty, VisibleListExporter.ToPlainText(null!));
        Assert.Equal(new[] { "Name", "Namespace", "Flags", "Size", "Kind", "Summary" }, VisibleListExporter.Headers);
    }

    [Fact]
    public void ToJson_IsAnIndentedArrayOfObjectsWithTheColumnProperties()
    {
        VariableListViewModel list = Loaded(GroupBy.None, SortBy.Name);
        list.FilterText = "d719b2cb";

        string json = VisibleListExporter.ToJson(list.VisibleItems);

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Equal(2, document.RootElement.GetArrayLength());
        JsonElement db = document.RootElement[0];
        Assert.Equal(new[] { "key", "name", "guid", "namespace", "flags", "size", "kind", "summary" }, db.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("d719b2cb-3d3a-4596-a3bc-dad00e67656f:db", db.GetProperty("key").GetString());
        Assert.Equal("db", db.GetProperty("name").GetString());
        Assert.Equal("d719b2cb-3d3a-4596-a3bc-dad00e67656f", db.GetProperty("guid").GetString());
        Assert.Equal("Image security database", db.GetProperty("namespace").GetString());
        Assert.Equal("NV BS RT TA", db.GetProperty("flags").GetString());
        Assert.Equal(JsonValueKind.Number, db.GetProperty("size").ValueKind);
        Assert.Equal(336, db.GetProperty("size").GetInt32());
        Assert.Equal("SecureBootState", db.GetProperty("kind").GetString());
        Assert.Equal("db: 336 bytes, 2 signature lists, 4 signatures (X509, SHA256)", db.GetProperty("summary").GetString());
        Assert.Equal("dbx", document.RootElement[1].GetProperty("name").GetString());
        Assert.Contains(Environment.NewLine + "  {", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ToJson_WritesRawRowsWithAnEmptySummary_AndEscapesNothingUnnecessarily()
    {
        VariableListViewModel list = Loaded(GroupBy.None, SortBy.Name);

        string json = VisibleListExporter.ToJson(list.VisibleItems);

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(Count, document.RootElement.GetArrayLength());
        JsonElement vendor = document.RootElement.EnumerateArray().Single(element => element.GetProperty("name").GetString() == "VendorConfig");
        Assert.Equal("Raw", vendor.GetProperty("kind").GetString());
        Assert.Equal(string.Empty, vendor.GetProperty("summary").GetString());
        Assert.Equal("Unknown vendor 12345678-1234-1234-1234-123456789abc", vendor.GetProperty("namespace").GetString());
        Assert.Contains("\"summary\": \"Boot0001: Windows Boot Manager; active, category Boot; HD(1,GPT,", json, StringComparison.Ordinal);
        Assert.Contains("\\\\EFI\\\\Microsoft\\\\Boot\\\\bootmgfw.efi", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u002B", json, StringComparison.Ordinal);

        Assert.Equal("[]", VisibleListExporter.ToJson(Array.Empty<VariableItemViewModel>()));
        Assert.Equal("[]", VisibleListExporter.ToJson(null!));
    }

    [Fact]
    public void Export_UsesTheVisibleOrderOfTheList()
    {
        VariableListViewModel list = Loaded(GroupBy.Kind, SortBy.Size);

        string text = VisibleListExporter.ToPlainText(list.VisibleItems);
        string[] names = text.TrimEnd().Split(Environment.NewLine).Skip(1).Select(line => line.Split(' ')[0]).ToArray();

        Assert.Equal(VisibleNames(list), names);
        Assert.Equal(new[] { "BootOrder", "BootCurrent", "Timeout" }, names[..3]);
        Assert.Equal("Boot0002", names[3]);
    }
}
