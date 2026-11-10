// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;

namespace UefiAccess.Presentation.ViewModels;

/// <summary>
/// What one <see cref="VariableDetailViewModel.Show"/> did to the detail pane, in numbers only
/// (never a value, a label or a name, so the input trace can write it as it is). The pane is
/// updated in place: a section or field that both rows have, in the same order, is kept, as the
/// same object, and only its value is updated when it differs; every other section and field is
/// added or removed (<see cref="DetailPaneReconciler"/>). The fields of a section that is added or
/// removed count as added or removed fields.
/// </summary>
/// <param name="SectionsKept">The sections both rows have, kept as the same objects.</param>
/// <param name="SectionsAdded">The sections the new row has and the old row had not, inserted at their position.</param>
/// <param name="SectionsRemoved">The sections the old row had and the new row has not, removed from their position.</param>
/// <param name="FieldsKept">The fields of the kept sections that both rows have in the same order, kept as the same objects.</param>
/// <param name="FieldsUpdated">The kept fields whose value changed (each raised a change of its value).</param>
/// <param name="FieldsAdded">The fields inserted: into a kept section, or with an added section.</param>
/// <param name="FieldsRemoved">The fields removed: from a kept section, or with a removed section.</param>
public readonly record struct DetailPaneUpdate(int SectionsKept, int SectionsAdded, int SectionsRemoved, int FieldsKept, int FieldsUpdated, int FieldsAdded, int FieldsRemoved)
{
    /// <summary>
    /// True when the update added or removed a section or a field, so the view had to create or
    /// drop controls; false when it only changed values (or nothing at all), as between two rows
    /// of the same kind.
    /// </summary>
    public bool HasStructureChange => SectionsAdded != 0 || SectionsRemoved != 0 || FieldsAdded != 0 || FieldsRemoved != 0;

    /// <summary>
    /// The counts as the input trace writes them, for example "sections 5 kept, 0 added, 0
    /// removed; fields 21 kept, 4 updated, 0 added, 0 removed".
    /// </summary>
    /// <returns>The counts as text, numbers only.</returns>
    public override string ToString()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "sections {0} kept, {1} added, {2} removed; fields {3} kept, {4} updated, {5} added, {6} removed",
            SectionsKept,
            SectionsAdded,
            SectionsRemoved,
            FieldsKept,
            FieldsUpdated,
            FieldsAdded,
            FieldsRemoved);
    }
}
