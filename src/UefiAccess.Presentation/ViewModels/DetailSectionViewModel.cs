// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.ObjectModel;

namespace UefiAccess.Presentation.ViewModels;

/// <summary>
/// A headed section of the detail pane (Identity, Attributes, Size, Decoded, Raw) and its fields.
/// The title never changes. The fields are one collection for the life of the section: when the
/// pane shows another row and keeps this section, <see cref="VariableDetailViewModel.Show"/>
/// updates the fields in place (a field both rows have, in the same order, keeps its object and
/// only its value changes; every other field is inserted or removed, see
/// <see cref="UpdateFields"/>), so a view
/// bound to <see cref="Fields"/> keeps the controls of every kept field.
/// </summary>
public sealed class DetailSectionViewModel
{
    private readonly ObservableCollection<DetailFieldViewModel> _fields;

    /// <summary>Creates the section.</summary>
    /// <param name="title">The section heading (AutomationProperties.HeadingLevel Level2).</param>
    /// <param name="fields">The fields in display order; the list is copied.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public DetailSectionViewModel(string title, IReadOnlyList<DetailFieldViewModel> fields)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(fields);
        Title = title;
        _fields = new ObservableCollection<DetailFieldViewModel>(fields);
        Fields = new ReadOnlyObservableCollection<DetailFieldViewModel>(_fields);
    }

    /// <summary>The heading.</summary>
    public string Title { get; }

    /// <summary>
    /// The fields in display order: the same collection for the life of the section, never
    /// replaced, cleared or reset; a row change inserts or removes only the fields that differ.
    /// </summary>
    public ReadOnlyObservableCollection<DetailFieldViewModel> Fields { get; }

    /// <summary>
    /// Makes the fields those of <paramref name="wanted"/>, the same section freshly built for the
    /// row now shown, in place (<see cref="DetailPaneReconciler"/>): a field is identified by its
    /// label, its spoken name, whether it is multi-line and its occurrence among the fields with
    /// those three; a field the old and the new row both have, in the same order relative to the
    /// other kept fields, is kept and takes the new value
    /// (<see cref="DetailFieldViewModel.UpdateValue"/>); every other field is inserted or removed
    /// (a shared field whose place changed is removed and its twin inserted, never moved).
    /// </summary>
    /// <param name="wanted">The fields of the freshly built section, in order.</param>
    /// <returns>How many fields were kept, updated, inserted and removed.</returns>
    internal DetailPaneReconciler.Counts UpdateFields(IReadOnlyList<DetailFieldViewModel> wanted)
    {
        return DetailPaneReconciler.Apply(_fields, wanted, FieldIdentity, static (existing, next) => existing.UpdateValue(next.Value));
    }

    /// <summary>
    /// What makes two fields the same field: the label, the spoken name and the shape. A field that
    /// turns from single-line to multi-line is another field (replaced, never restyled in place).
    /// </summary>
    private static (string Label, string AccessibleName, bool IsMultiline) FieldIdentity(DetailFieldViewModel field)
    {
        return (field.Label, field.AccessibleName, field.IsMultiline);
    }
}
