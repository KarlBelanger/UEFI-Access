// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.ObjectModel;
using System.Globalization;
using UefiAccess.Core.Decoders;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Variables;
using UefiAccess.Presentation.Infrastructure;
using UefiAccess.Presentation.Resources;
using UefiAccess.Presentation.Services;

namespace UefiAccess.Presentation.ViewModels;

/// <summary>
/// The filtered, grouped and sorted collection of rows plus the selection. <see cref="Load(IReadOnlyList{FirmwareVariable}, bool, bool)"/>
/// builds one <see cref="VariableItemViewModel"/> per variable through the namespace catalog and
/// <c>DecoderRegistry.CreateDefault()</c> over a <c>VariableSet</c>, exactly like the CLI, so the
/// GUI and the CLI can never disagree about a value. <see cref="Groups"/> and
/// <see cref="VisibleItems"/> are rebuilt synchronously on every change of <see cref="FilterText"/>,
/// <see cref="GroupBy"/> and <see cref="SortBy"/>; nothing here runs on a timer.
/// </summary>
/// <remarks>
/// Filtering is a case-insensitive substring match over the name, the GUID, the namespace label,
/// the kind and the decoded summary. Grouping is by namespace label, by kind, or off (one group
/// named <see cref="AllVariablesGroupLabel"/>); groups are ordered alphabetically with the EFI
/// global variable namespace first. Sorting inside every group is by name, by namespace then
/// name, by size (largest first) then name, or by kind then name; the key breaks every tie so the
/// order is deterministic. Selection: when a filter hides the selected row the selection becomes
/// null (the detail pane says "No variable selected") but the row's key is remembered;
/// <see cref="ClearFilter"/> restores that row, or selects the first visible row when there is
/// nothing to restore. <see cref="Load(IReadOnlyList{FirmwareVariable}, bool, bool)"/> keeps the selection by key when the same variable is
/// still present.
/// </remarks>
public sealed class VariableListViewModel : ObservableObject
{
    /// <summary>Label of the single group shown when <see cref="ViewModels.GroupBy.None"/> is chosen.</summary>
    public const string AllVariablesGroupLabel = "All variables";

    /// <summary>Key of the single group shown when <see cref="ViewModels.GroupBy.None"/> is chosen.</summary>
    public const string AllVariablesGroupKey = "";

    private readonly List<VariableItemViewModel> _allItems = new();
    private readonly Dictionary<string, bool> _expandedByGroupKey = new(StringComparer.Ordinal);
    private VariableSet _variables = new(Array.Empty<FirmwareVariable>());
    private string _filterText = string.Empty;
    private GroupBy _groupBy = GroupBy.Namespace;
    private SortBy _sortBy = SortBy.Name;
    private VariableItemViewModel? _selectedItem;
    private string? _rememberedKey;
    private int _totalCount;
    private int _visibleCount;
    private IReadOnlyList<VariableItemViewModel> _visibleItems = Array.Empty<VariableItemViewModel>();

    /// <summary>Creates an empty list.</summary>
    public VariableListViewModel()
    {
        Groups = new ObservableCollection<VariableGroupViewModel>();
    }

    /// <summary>Raised after <see cref="SelectedItem"/> changed, with the new selection (or null).</summary>
    public event EventHandler<VariableItemViewModel?>? SelectionChanged;

    /// <summary>The groups in display order; the view binds its ItemsSource to this and the list mutates it in place.</summary>
    public ObservableCollection<VariableGroupViewModel> Groups { get; }

    /// <summary>Every loaded row in key order, before filtering.</summary>
    public IReadOnlyList<VariableItemViewModel> AllItems => _allItems;

    /// <summary>
    /// Every row that passes the filter, flattened in display order, rows in collapsed groups
    /// included: what <see cref="VisibleCount"/> counts ("Showing N of M variables") and what Export
    /// visible list writes. Collapsing a group hides its rows from view, not from this list.
    /// </summary>
    public IReadOnlyList<VariableItemViewModel> VisibleItems
    {
        get => _visibleItems;
        private set => SetProperty(ref _visibleItems, value);
    }

    /// <summary>
    /// The filter text; applied synchronously on every change. Null is treated as empty;
    /// surrounding white space is ignored when matching. The groups, <see cref="VisibleItems"/>,
    /// <see cref="VisibleCount"/> and <see cref="ShowingText"/> are rebuilt before
    /// <c>PropertyChanged</c> is raised for this property, so a listener reacting to the change
    /// (the view's filter box, which announces the count) already sees the new state.
    /// </summary>
    public string FilterText
    {
        get => _filterText;
        set
        {
            string text = value ?? string.Empty;
            if (string.Equals(_filterText, text, StringComparison.Ordinal))
            {
                return;
            }

            _filterText = text;
            Rebuild();
            OnPropertyChanged();
        }
    }

    /// <summary>The grouping; applied synchronously on every change, before <c>PropertyChanged</c> is raised (see <see cref="FilterText"/>).</summary>
    public GroupBy GroupBy
    {
        get => _groupBy;
        set
        {
            if (_groupBy == value)
            {
                return;
            }

            _groupBy = value;
            Rebuild();
            OnPropertyChanged();
        }
    }

    /// <summary>The sort order inside each group; applied synchronously on every change, before <c>PropertyChanged</c> is raised (see <see cref="FilterText"/>).</summary>
    public SortBy SortBy
    {
        get => _sortBy;
        set
        {
            if (_sortBy == value)
            {
                return;
            }

            _sortBy = value;
            Rebuild();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The selected row, or null. Setting a row remembers its key so <see cref="ClearFilter"/> and
    /// <see cref="Load(IReadOnlyList{FirmwareVariable}, bool, bool)"/> can restore it; <see cref="SelectionChanged"/> is raised only when the
    /// value actually changed.
    /// </summary>
    public VariableItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (value is not null)
            {
                _rememberedKey = value.Key;
            }

            if (SetProperty(ref _selectedItem, value))
            {
                SelectionChanged?.Invoke(this, value);
            }
        }
    }

    /// <summary>The number of variables loaded.</summary>
    public int TotalCount
    {
        get => _totalCount;
        private set
        {
            if (SetProperty(ref _totalCount, value))
            {
                OnPropertyChanged(nameof(ShowingText));
            }
        }
    }

    /// <summary>The number of rows that pass the filter.</summary>
    public int VisibleCount
    {
        get => _visibleCount;
        private set
        {
            if (SetProperty(ref _visibleCount, value))
            {
                OnPropertyChanged(nameof(ShowingText));
            }
        }
    }

    /// <summary>"Showing N of M variables" ("Showing 1 of 1 variable" when one variable is loaded).</summary>
    public string ShowingText => string.Format(CultureInfo.InvariantCulture, Strings.ShowingFormat, VisibleCount, Strings.Variables(TotalCount));

    /// <summary>True when a filter is in effect (the trimmed filter text is not empty).</summary>
    public bool IsFiltered => EffectiveFilter.Length > 0;

    private string EffectiveFilter => _filterText.Trim();

    /// <summary>
    /// The variables of the last <see cref="Load(IReadOnlyList{FirmwareVariable}, bool, bool)"/> as
    /// one lookup (the set the decoders and the writability policy read), for the Change group.
    /// </summary>
    public IVariableLookup Variables => _variables;

    /// <summary>
    /// Replaces the content as <see cref="Load(IReadOnlyList{FirmwareVariable}, bool, bool)"/> does,
    /// with every row judged as a live variable on a machine that is not the M3 test unit: no row
    /// is changeable; a well-formed BootOrder or BootNext carries the item status
    /// <see cref="WritabilitySentences.ItemStatusElsewhere"/> and its words after the name, and no
    /// other row carries an item status.
    /// </summary>
    /// <param name="variables">The variables; null is treated as empty.</param>
    public void Load(IReadOnlyList<FirmwareVariable> variables) => Load(variables, offline: false, machineConfirmed: false);

    /// <summary>
    /// Replaces the content with the given variables and rebuilds the groups. Every variable is
    /// decoded through <c>DecoderRegistry.CreateDefault()</c> over a <c>VariableSet</c> of the whole
    /// list, so references such as BootOrder entries resolve to their Boot#### descriptions, and
    /// judged once by Core's <see cref="WritabilityPolicy"/> over the same set (the Writability line
    /// of the detail pane, the row's item status and, for BootOrder and BootNext, the item status's
    /// words in the row sentence). The selection is kept when the previously
    /// selected key is still present and visible; otherwise it becomes null. Never throws.
    /// </summary>
    /// <param name="variables">The variables; null is treated as empty.</param>
    /// <param name="offline">True when the variables come from a baseline folder.</param>
    /// <param name="machineConfirmed">True when the live machine is the M3 test unit: the 2014 OptiPlex 7020 on BIOS A18 (<see cref="PlatformPolicy.IsContractedMachine"/>) whose Windows machine ID the unit check confirmed.</param>
    public void Load(IReadOnlyList<FirmwareVariable> variables, bool offline, bool machineConfirmed)
    {
        VariableSet set = new(variables ?? Array.Empty<FirmwareVariable>());
        DecoderRegistry registry = DecoderRegistry.CreateDefault();
        _variables = set;

        _allItems.Clear();
        foreach (FirmwareVariable variable in set.All)
        {
            DecodedVariable? decoded = registry.TryDecode(variable, set);
            NamespaceInfo ns = NamespaceCatalog.Describe(variable.VendorGuid);
            WritabilityVerdict writability = WritabilityPolicy.Describe(variable, set, offline, machineConfirmed);
            _allItems.Add(new VariableItemViewModel(variable, ns, decoded, writability));
        }

        TotalCount = _allItems.Count;
        OnPropertyChanged(nameof(AllItems));

        // The old row objects are gone. Drop the reference silently, rebuild, then re-select the
        // remembered key in one step so the view sees a single selection change. A key that still
        // exists but is hidden by the active filter stays remembered for ClearFilter; a key that no
        // longer exists is forgotten.
        VariableItemViewModel? previous = _selectedItem;
        _selectedItem = null;
        Rebuild();

        VariableItemViewModel? restored = null;
        if (_rememberedKey is not null)
        {
            restored = FindVisible(_rememberedKey);
            if (restored is null && FindLoaded(_rememberedKey) is null)
            {
                _rememberedKey = null;
            }
        }

        if (previous is not null || restored is not null)
        {
            _selectedItem = restored;
            OnPropertyChanged(nameof(SelectedItem));
            SelectionChanged?.Invoke(this, restored);
        }
    }

    /// <summary>
    /// Empties the filter text (the Clear filter command and Escape in the filter box). The
    /// selection is kept when a row is selected; otherwise the row that was selected before a filter
    /// hid it is restored, and when there is no such row the first visible row is selected.
    /// </summary>
    public void ClearFilter()
    {
        FilterText = string.Empty;
        if (SelectedItem is not null)
        {
            return;
        }

        if (_rememberedKey is not null && TrySelectByKey(_rememberedKey))
        {
            return;
        }

        SelectedItem = VisibleItems.Count > 0 ? VisibleItems[0] : null;
    }

    /// <summary>Selects the row with the given key when it is visible.</summary>
    /// <param name="key">The variable key "guid:name" (the GUID part is compared case-insensitively, the name exactly).</param>
    /// <returns>True when a row was selected.</returns>
    public bool TrySelectByKey(string key)
    {
        VariableItemViewModel? item = FindVisible(key);
        if (item is null)
        {
            return false;
        }

        SelectedItem = item;
        return true;
    }

    /// <summary>Finds a visible row by key without selecting it.</summary>
    /// <param name="key">The variable key "guid:name".</param>
    /// <returns>The row, or null when no visible row has that key.</returns>
    public VariableItemViewModel? FindVisible(string key) => FindByKey(_visibleItems, key);

    /// <summary>Finds a loaded row by key, visible or not, without selecting it.</summary>
    /// <param name="key">The variable key "guid:name".</param>
    /// <returns>The row, or null when no loaded row has that key.</returns>
    public VariableItemViewModel? FindLoaded(string key) => FindByKey(_allItems, key);

    /// <summary>Tests whether a row passes the current filter.</summary>
    /// <param name="item">The row.</param>
    /// <returns>True when the filter is empty or any of name, GUID, namespace label, kind and summary contains the filter text (case-insensitive).</returns>
    public bool Matches(VariableItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        string filter = EffectiveFilter;
        if (filter.Length == 0)
        {
            return true;
        }

        return Contains(item.Name, filter)
            || Contains(item.GuidText, filter)
            || Contains(item.NamespaceLabel, filter)
            || Contains(item.Kind, filter)
            || Contains(item.Summary, filter);
    }

    /// <summary>The comparison used inside every group for a sort order.</summary>
    /// <param name="sortBy">The sort order.</param>
    /// <returns>A comparer; ties are broken by name and then by key so the order is deterministic.</returns>
    public static IComparer<VariableItemViewModel> ComparerFor(SortBy sortBy)
    {
        return sortBy switch
        {
            SortBy.Namespace => Comparer<VariableItemViewModel>.Create(CompareNamespace),
            SortBy.Size => Comparer<VariableItemViewModel>.Create(CompareSize),
            SortBy.Kind => Comparer<VariableItemViewModel>.Create(CompareKind),
            _ => Comparer<VariableItemViewModel>.Create(CompareName),
        };
    }

    /// <summary>
    /// The comparison of group labels: the EFI global variable namespace first, then alphabetical
    /// (ordinal, case-insensitive).
    /// </summary>
    /// <param name="left">One label.</param>
    /// <param name="right">The other label.</param>
    /// <returns>Negative when <paramref name="left"/> sorts first.</returns>
    public static int CompareGroupLabels(string left, string right)
    {
        bool leftFirst = string.Equals(left, NamespaceCatalog.EfiGlobalVariableLabel, StringComparison.Ordinal);
        bool rightFirst = string.Equals(right, NamespaceCatalog.EfiGlobalVariableLabel, StringComparison.Ordinal);
        if (leftFirst != rightFirst)
        {
            return leftFirst ? -1 : 1;
        }

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static VariableItemViewModel? FindByKey(IReadOnlyList<VariableItemViewModel> items, string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        foreach (VariableItemViewModel item in items)
        {
            if (KeysEqual(item.Key, key))
            {
                return item;
            }
        }

        return null;
    }

    private static bool Contains(string haystack, string needle)
    {
        return !string.IsNullOrEmpty(haystack) && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    private static bool KeysEqual(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return true;
        }

        // The GUID half of a key is case-insensitive; the name half is not.
        int leftColon = left.IndexOf(':', StringComparison.Ordinal);
        int rightColon = right.IndexOf(':', StringComparison.Ordinal);
        if (leftColon < 0 || rightColon < 0)
        {
            return false;
        }

        return string.Equals(left[..leftColon], right[..rightColon], StringComparison.OrdinalIgnoreCase)
            && string.Equals(left[(leftColon + 1)..], right[(rightColon + 1)..], StringComparison.Ordinal);
    }

    private static int CompareName(VariableItemViewModel left, VariableItemViewModel right)
    {
        int byName = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        return byName != 0 ? byName : string.CompareOrdinal(left.Key, right.Key);
    }

    private static int CompareNamespace(VariableItemViewModel left, VariableItemViewModel right)
    {
        int byLabel = CompareGroupLabels(left.NamespaceLabel, right.NamespaceLabel);
        return byLabel != 0 ? byLabel : CompareName(left, right);
    }

    private static int CompareSize(VariableItemViewModel left, VariableItemViewModel right)
    {
        int bySize = right.Size.CompareTo(left.Size);
        return bySize != 0 ? bySize : CompareName(left, right);
    }

    private static int CompareKind(VariableItemViewModel left, VariableItemViewModel right)
    {
        int byKind = string.Compare(left.Kind, right.Kind, StringComparison.OrdinalIgnoreCase);
        return byKind != 0 ? byKind : CompareName(left, right);
    }

    private void Rebuild()
    {
        List<VariableItemViewModel> visible = _allItems.Where(Matches).ToList();
        visible.Sort(ComparerFor(_sortBy));

        List<VariableGroupViewModel> groups = BuildGroups(visible);
        RememberExpansion();
        Groups.Clear();
        List<VariableItemViewModel> flattened = new(visible.Count);
        foreach (VariableGroupViewModel group in groups)
        {
            if (_expandedByGroupKey.TryGetValue(group.Key, out bool expanded))
            {
                group.IsExpanded = expanded;
            }

            Groups.Add(group);
            flattened.AddRange(group.Items);
        }

        VisibleItems = flattened;
        VisibleCount = flattened.Count;
        OnPropertyChanged(nameof(IsFiltered));

        if (_selectedItem is not null && !flattened.Contains(_selectedItem))
        {
            // The filter hid the selected row: nothing is selected, but the key stays remembered
            // so ClearFilter can bring the row back.
            _selectedItem = null;
            OnPropertyChanged(nameof(SelectedItem));
            SelectionChanged?.Invoke(this, null);
        }
    }

    private List<VariableGroupViewModel> BuildGroups(List<VariableItemViewModel> visible)
    {
        if (_groupBy == GroupBy.None)
        {
            return visible.Count == 0
                ? new List<VariableGroupViewModel>()
                : new List<VariableGroupViewModel> { new(AllVariablesGroupKey, AllVariablesGroupLabel, visible) };
        }

        Dictionary<string, (string Label, List<VariableItemViewModel> Items)> buckets = new(StringComparer.Ordinal);
        List<string> order = new();
        foreach (VariableItemViewModel item in visible)
        {
            string key = _groupBy == GroupBy.Kind ? item.Kind : item.GuidText;
            string label = _groupBy == GroupBy.Kind ? item.Kind : item.NamespaceLabel;
            if (!buckets.TryGetValue(key, out (string Label, List<VariableItemViewModel> Items) bucket))
            {
                bucket = (label, new List<VariableItemViewModel>());
                buckets.Add(key, bucket);
                order.Add(key);
            }

            bucket.Items.Add(item);
        }

        order.Sort((left, right) =>
        {
            int byLabel = CompareGroupLabels(buckets[left].Label, buckets[right].Label);
            return byLabel != 0 ? byLabel : string.CompareOrdinal(left, right);
        });

        List<VariableGroupViewModel> groups = new(order.Count);
        foreach (string key in order)
        {
            (string label, List<VariableItemViewModel> items) = buckets[key];
            groups.Add(new VariableGroupViewModel(key, label, items));
        }

        return groups;
    }

    private void RememberExpansion()
    {
        foreach (VariableGroupViewModel group in Groups)
        {
            _expandedByGroupKey[group.Key] = group.IsExpanded;
        }
    }
}
