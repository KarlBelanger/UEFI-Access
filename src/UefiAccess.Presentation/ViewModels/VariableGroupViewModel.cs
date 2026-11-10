// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.ObjectModel;
using System.Globalization;
using UefiAccess.Presentation.Infrastructure;
using UefiAccess.Presentation.Resources;

namespace UefiAccess.Presentation.ViewModels;

/// <summary>
/// One group of the variable list (a namespace, a kind, or the single group when grouping is
/// off) with its header text and its rows. The header is a real, focusable item that announces
/// the label and the count ("EFI global variable, 41 items"; "Raw, 1 item").
/// </summary>
public sealed class VariableGroupViewModel : ObservableObject
{
    private bool _isExpanded = true;

    /// <summary>Creates the group.</summary>
    /// <param name="key">The stable group key (a GUID text, a kind, or an empty string).</param>
    /// <param name="label">The header label.</param>
    /// <param name="items">The rows in display order.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public VariableGroupViewModel(string key, string label, IReadOnlyList<VariableItemViewModel> items)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(items);
        Key = key;
        Label = label;
        Items = new ObservableCollection<VariableItemViewModel>(items);
    }

    /// <summary>The stable group key.</summary>
    public string Key { get; }

    /// <summary>The header label.</summary>
    public string Label { get; }

    /// <summary>The rows.</summary>
    public ObservableCollection<VariableItemViewModel> Items { get; }

    /// <summary>The number of rows.</summary>
    public int Count => Items.Count;

    /// <summary>The spoken header: "&lt;label&gt;, &lt;n&gt; items" ("&lt;label&gt;, 1 item" for a single row).</summary>
    public string AccessibleName => string.Format(CultureInfo.InvariantCulture, Strings.GroupHeaderFormat, Label, Strings.Items(Count));

    /// <summary>
    /// The name of the grid of rows under the header, by the label alone ("&lt;label&gt; rows"):
    /// the header sentence is the group header's and is not spoken a second time on the rows.
    /// </summary>
    public string RowsAccessibleName => string.Format(CultureInfo.InvariantCulture, Strings.GroupRowsFormat, Label);

    /// <summary>Whether the group's rows are shown (Left collapses, Right expands).</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
}
