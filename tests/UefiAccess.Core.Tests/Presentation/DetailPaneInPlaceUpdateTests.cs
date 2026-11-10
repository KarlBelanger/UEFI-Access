// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using UefiAccess.Core.Decoders;
using UefiAccess.Core.Tests.Fixtures;
using UefiAccess.Core.Variables;
using UefiAccess.Presentation.Resources;
using UefiAccess.Presentation.Services;
using UefiAccess.Presentation.ViewModels;
using Xunit;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// Fix 1 of the A16 measurement (round 4, finding A1-6): the detail pane is updated in place.
/// Measured on the OptiPlex 7020, a row change that removed and re-created the pane's five
/// sections and their fields made WPF raise about 80 StructureChanged and 70 PropertyChanged UI
/// Automation events, and the next row's focus event reached NVDA up to about 215 ms late. Now
/// <see cref="VariableDetailViewModel.Show"/> keeps the sections collection and each section's
/// fields collection for good, keeps every section and field the old and the new row both have
/// (the same objects, so WPF keeps their containers and automation peers) and changes only their
/// values, and inserts or removes, one notification each, only what differs. These tests check
/// that a row change between rows of the same kind changes values only, that a row change between
/// kinds adds and removes only the differing sections and fields at their positions, that nothing
/// is ever reset, replaced or moved, and above all that the pane updated in place reads exactly
/// like a pane freshly built for the row (every field's label, spoken name, value, shape, help text
/// with its character count and arrival selection), for every pair of many rows, the sample
/// baseline's rows and a long seeded walk. Every variable is synthetic; nothing reads firmware.
/// </summary>
public sealed class DetailPaneInPlaceUpdateTests
{
    private const VariableAttributes NvBsRt = VariableAttributes.NonVolatile | VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess;
    private const VariableAttributes NvBsRtTa = NvBsRt | VariableAttributes.TimeBasedAuthenticatedWriteAccess;

    private static readonly Guid VendorGuid = new("0A1B2C3D-4E5F-4A6B-8C7D-9E8F7A6B5C4D");

    /// <summary>The pane's own properties a row change may raise (since M3 the Writability line, ReadOnlyStatus, too); never Sections.</summary>
    private static readonly string[] PaneProperties =
    {
        nameof(VariableDetailViewModel.HexDump),
        nameof(VariableDetailViewModel.Heading),
        nameof(VariableDetailViewModel.CopyValueText),
        nameof(VariableDetailViewModel.ReadOnlyStatus),
        nameof(VariableDetailViewModel.Item),
        nameof(VariableDetailViewModel.HasSelection),
    };

    // ---- The collections ------------------------------------------------------------------

    [Fact]
    public void TheSectionsAndEachFieldsCollection_AreTheSameInstancesForTheLifeOfThePane()
    {
        List<(string Name, VariableItemViewModel? Row)> rows = AllRows();
        VariableDetailViewModel detail = new();
        ReadOnlyObservableCollection<DetailSectionViewModel> sections = detail.Sections;
        Dictionary<DetailSectionViewModel, ReadOnlyObservableCollection<DetailFieldViewModel>> fieldsOf = new(ReferenceEqualityComparer.Instance);
        using DetailPaneRecorder recorder = new(detail);
        Random random = new(7);
        IEnumerable<int> walk = Enumerable.Range(0, rows.Count)
            .Concat(Enumerable.Range(0, rows.Count).Reverse())
            .Concat(Enumerable.Range(0, 300).Select(_ => random.Next(rows.Count)));

        int kept = 0;
        foreach (int index in walk)
        {
            detail.Show(rows[index].Row);
            Assert.Same(sections, detail.Sections);
            foreach (DetailSectionViewModel section in detail.Sections)
            {
                if (fieldsOf.TryGetValue(section, out ReadOnlyObservableCollection<DetailFieldViewModel>? fields))
                {
                    Assert.Same(fields, section.Fields);
                    kept++;
                }
                else
                {
                    fieldsOf[section] = section.Fields;
                }
            }
        }

        Assert.True(kept > 500, "the walk kept sections only " + kept + " times");
        Assert.DoesNotContain(nameof(VariableDetailViewModel.Sections), recorder.PaneProperties);
        Assert.All(recorder.PaneProperties, name => Assert.Contains(name, PaneProperties));
        Assert.Empty(recorder.ForbiddenCollectionChanges);
        Assert.Contains(recorder.SectionChanges, change => change.Action == NotifyCollectionChangedAction.Add);
        Assert.Contains(recorder.SectionChanges, change => change.Action == NotifyCollectionChangedAction.Remove);
        Assert.Contains(recorder.FieldCollectionChanges, change => change.Change.Action == NotifyCollectionChangedAction.Add);
        Assert.Contains(recorder.FieldCollectionChanges, change => change.Change.Action == NotifyCollectionChangedAction.Remove);
    }

    // ---- Rows of the same kind: values only ------------------------------------------------

    [Fact]
    public void TwoBootRows_KeepEverySectionAndField_AndRaiseOnlyTheValuesThatDiffer()
    {
        IReadOnlyList<VariableItemViewModel> rows = SnapshotRows();
        int updated = AssertSameKindWalk(Row(rows, "Boot0001"), Row(rows, "Boot0003"), Row(rows, "Boot0002"), Row(rows, "Boot0004"), Row(rows, "Boot0001"));
        Assert.True(updated > 4, "the Boot#### rows differ in more than their names");
    }

    [Fact]
    public void DbAndDbx_KeepEverySectionAndField_AndRaiseOnlyTheValuesThatDiffer()
    {
        IReadOnlyList<VariableItemViewModel> rows = SnapshotRows();
        int updated = AssertSameKindWalk(Row(rows, "db"), Row(rows, "dbx"), Row(rows, "db"), Dbx(100), Dbx(120), Row(rows, "dbx"));
        Assert.True(updated > 4, "db and dbx differ in more than their names");
    }

    // ---- Rows of different kinds: only the differing sections and fields --------------------

    [Fact]
    public void ADecodedRowAndARawRow_AddAndRemoveOnlyTheDecodedSection()
    {
        IReadOnlyList<VariableItemViewModel> rows = SnapshotRows();
        VariableItemViewModel boot = Row(rows, "Boot0001");
        VariableItemViewModel vendor = Row(rows, "VendorConfig");
        Assert.NotNull(boot.Decoded);
        Assert.Null(vendor.Decoded);

        VariableDetailViewModel detail = new();
        detail.Show(boot);
        DetailSectionViewModel[] before = detail.Sections.ToArray();
        DetailSectionViewModel decoded = before[3];
        Assert.Equal(Strings.SectionDecoded, decoded.Title);
        using DetailPaneRecorder recorder = new(detail);

        detail.Show(vendor);

        Assert.Equal(new[] { "Sections Remove 3" }, StructureEvents(recorder));
        Assert.Same(decoded, recorder.SectionChanges[0].OldItems![0]);
        Assert.Equal(new[] { before[0], before[1], before[2], before[4] }, detail.Sections);
        AssertIdentical(Describe(Fresh(vendor)), detail, vendor, "Boot0001 then VendorConfig");

        recorder.Clear();
        detail.Show(boot);

        Assert.Equal(new[] { "Sections Add 3" }, StructureEvents(recorder));
        Assert.Equal(Strings.SectionDecoded, detail.Sections[3].Title);
        Assert.NotSame(decoded, detail.Sections[3]);
        Assert.Equal(new[] { before[0], before[1], before[2] }, detail.Sections.Take(3));
        Assert.Same(before[4], detail.Sections[4]);
        AssertIdentical(Describe(Fresh(boot)), detail, boot, "VendorConfig then Boot0001");
    }

    [Fact]
    public void RowsOfDifferentKinds_AddAndRemoveOnlyTheDifferingFields_AtTheirPositions()
    {
        IReadOnlyList<VariableItemViewModel> rows = SnapshotRows();
        (VariableItemViewModel From, VariableItemViewModel To, string[] Expected)[] cases =
        {
            // A raw NV BS RT variable to db: the Attributes section gets the TA flag's field
            // before Short flags, then the Decoded section comes in at 3 (the sections are walked
            // in order).
            (Row(rows, "VendorConfig"), Row(rows, "db"), new[] { "Fields of " + Strings.SectionAttributes + " Add 3", "Sections Add 3" }),

            // A load option to BootOrder: the Summary is kept, the load option's fields go, the
            // BootOrder entries come.
            (Row(rows, "Boot0001"), Row(rows, "BootOrder"), Array.Empty<string>()),
        };

        foreach ((VariableItemViewModel from, VariableItemViewModel to, string[] pinned) in cases)
        {
            VariableDetailViewModel detail = new();
            detail.Show(from);
            List<string> expected = ExpectedStructureEvents(Fresh(from), Fresh(to));
            if (pinned.Length > 0)
            {
                Assert.Equal(pinned, expected);
            }

            List<(string Title, List<DetailFieldViewModel> Fields)> before = detail.Sections.Select(section => (section.Title, section.Fields.ToList())).ToList();
            using DetailPaneRecorder recorder = new(detail);

            DetailPaneUpdate update = detail.Show(to);

            string pair = from.Name + " then " + to.Name;
            Assert.Equal(expected, StructureEvents(recorder));
            Assert.Empty(recorder.ForbiddenCollectionChanges);
            Assert.All(recorder.SectionChanges.Concat(recorder.FieldCollectionChanges.Select(change => change.Change)), change => Assert.True(DetailPaneRecorder.IsSingleAddOrRemove(change)));
            Assert.True(update.HasStructureChange, pair);
            AssertIdentical(Describe(Fresh(to)), detail, to, pair);

            // Every field both rows have in a kept section is the same object as before.
            foreach (DetailSectionViewModel section in detail.Sections)
            {
                List<DetailFieldViewModel>? old = before.Where(entry => entry.Title == section.Title).Select(entry => entry.Fields).SingleOrDefault();
                if (old is null)
                {
                    continue;
                }

                List<string> oldKeys = FieldKeys(old);
                List<string> newKeys = FieldKeys(section.Fields);
                foreach (string key in oldKeys.Intersect(newKeys))
                {
                    Assert.Same(old[oldKeys.IndexOf(key)], section.Fields[newKeys.IndexOf(key)]);
                }
            }
        }

        // The BootOrder case, spelled out: the Decoded section stays, its Summary stays, and only
        // the fields of the two decoders change places.
        VariableDetailViewModel pane = new();
        pane.Show(Row(rows, "Boot0001"));
        DetailSectionViewModel decoded = pane.Sections.Single(section => section.Title == Strings.SectionDecoded);
        DetailFieldViewModel summary = decoded.Fields[0];
        pane.Show(Row(rows, "BootOrder"));
        Assert.Same(decoded, pane.Sections.Single(section => section.Title == Strings.SectionDecoded));
        Assert.Same(summary, decoded.Fields[0]);
        Assert.Equal(Row(rows, "BootOrder").Decoded!.Summary, summary.Value);
        Assert.DoesNotContain(decoded.Fields, field => field.Label == "Device path");
    }

    [Fact]
    public void ShowNull_RemovesTheSectionsFromTheLast_AndARowInsertsThemInOrder()
    {
        VariableItemViewModel boot = Row(SnapshotRows(), "Boot0001");
        VariableDetailViewModel detail = new();
        using DetailPaneRecorder recorder = new(detail);

        detail.Show(boot);
        Assert.Equal(new[] { "Sections Add 0", "Sections Add 1", "Sections Add 2", "Sections Add 3", "Sections Add 4" }, StructureEvents(recorder));
        Assert.Empty(recorder.FieldChanges);

        recorder.Clear();
        detail.Show(null);
        Assert.Equal(new[] { "Sections Remove 4", "Sections Remove 3", "Sections Remove 2", "Sections Remove 1", "Sections Remove 0" }, StructureEvents(recorder));
        Assert.Empty(recorder.FieldChanges);
        Assert.Empty(detail.Sections);
        Assert.Null(detail.Item);

        recorder.Clear();
        detail.Show(null);
        Assert.Empty(recorder.Events);

        detail.Show(boot);
        Assert.Equal(new[] { "Sections Add 0", "Sections Add 1", "Sections Add 2", "Sections Add 3", "Sections Add 4" }, StructureEvents(recorder));
        Assert.Empty(recorder.ForbiddenCollectionChanges);
        AssertIdentical(Describe(Fresh(boot)), detail, boot, "no row then Boot0001");
    }

    [Fact]
    public void ShowingTheSameRowAgain_ChangesNothingButTheCopyText()
    {
        foreach (VariableItemViewModel row in SnapshotRows())
        {
            VariableDetailViewModel detail = new();
            detail.Show(row);
            using DetailPaneRecorder recorder = new(detail);

            DetailPaneUpdate again = detail.Show(row);
            Assert.Empty(recorder.Events);
            Assert.False(again.HasStructureChange);
            Assert.Equal(0, again.FieldsUpdated);

            detail.CopyValueText = "a field the view picked";
            recorder.Clear();
            detail.Show(row);
            Assert.Equal(new[] { "Detail." + nameof(VariableDetailViewModel.CopyValueText) }, recorder.Events);
            Assert.Equal(string.IsNullOrEmpty(row.Summary) ? detail.HexDump : row.Summary, detail.CopyValueText);
        }
    }

    // ---- Identical to a fresh pane -----------------------------------------------------------

    [Fact]
    public void InPlace_IsIdenticalToAFreshPane_ForEveryPairOfRows()
    {
        List<(string Name, VariableItemViewModel? Row)> rows = AllRows();
        Assert.True(rows.Count >= VariableListFixtures.Count + 20, "expected the snapshot and the edge rows, found " + rows.Count);
        List<List<string>> fresh = rows.Select(row => Describe(Fresh(row.Row))).ToList();

        int pairs = 0;
        for (int a = 0; a < rows.Count; a++)
        {
            for (int b = 0; b < rows.Count; b++)
            {
                VariableDetailViewModel detail = new();
                detail.Show(rows[a].Row);
                detail.Show(rows[b].Row);
                AssertIdentical(fresh[b], detail, rows[b].Row, rows[a].Name + " then " + rows[b].Name);
                pairs++;
            }
        }

        Assert.Equal(rows.Count * rows.Count, pairs);
    }

    [Fact]
    public async Task InPlace_IsIdenticalToAFreshPane_ForTheSampleBaseline()
    {
        SourceResult result = await new BaselineFolderSource(Path.Combine(RepositoryPaths.Root, "tools", "sample-baseline")).LoadAsync(CancellationToken.None);
        Assert.Equal(SourceStatus.Loaded, result.Status);
        VariableListViewModel list = new();
        list.Load(result.Variables);
        List<VariableItemViewModel?> rows = list.AllItems.Cast<VariableItemViewModel?>().Append(null).ToList();
        Assert.Equal(result.Variables.Count + 1, rows.Count);
        Assert.True(result.Variables.Count >= 10, "the sample baseline holds " + result.Variables.Count + " variables");
        List<List<string>> fresh = rows.Select(row => Describe(Fresh(row))).ToList();

        for (int a = 0; a < rows.Count; a++)
        {
            for (int b = 0; b < rows.Count; b++)
            {
                VariableDetailViewModel detail = new();
                detail.Show(rows[a]);
                detail.Show(rows[b]);
                AssertIdentical(fresh[b], detail, rows[b], NameOf(rows[a]) + " then " + NameOf(rows[b]));
            }
        }
    }

    [Fact]
    public void ASeededWalk_ThroughEveryRowAndNoRow_StaysIdenticalToAFreshPane()
    {
        List<(string Name, VariableItemViewModel? Row)> rows = AllRows();
        List<List<string>> fresh = rows.Select(row => Describe(Fresh(row.Row))).ToList();
        Random random = new(20261001);
        VariableDetailViewModel detail = new();
        using DetailPaneRecorder recorder = new(detail);
        string previous = "(new pane)";
        int structureChanges = 0;
        int valueOnly = 0;

        for (int step = 0; step < 500; step++)
        {
            int next = random.Next(rows.Count);
            string pair = "step " + step.ToString(CultureInfo.InvariantCulture) + ": " + previous + " then " + rows[next].Name;
            List<(string Title, List<string> Fields)> oldKeys = PaneKeys(detail);
            recorder.Clear();

            DetailPaneUpdate update = detail.Show(rows[next].Row);

            AssertIdentical(fresh[next], detail, rows[next].Row, pair);
            List<(string Title, List<string> Fields)> newKeys = PaneKeys(detail);

            // The counts are the events.
            List<NotifyCollectionChangedEventArgs> sectionsAdded = recorder.SectionChanges.Where(change => change.Action == NotifyCollectionChangedAction.Add).ToList();
            List<NotifyCollectionChangedEventArgs> sectionsRemoved = recorder.SectionChanges.Where(change => change.Action == NotifyCollectionChangedAction.Remove).ToList();
            Assert.Empty(recorder.ForbiddenCollectionChanges);
            Assert.DoesNotContain(nameof(VariableDetailViewModel.Sections), recorder.PaneProperties);
            Assert.Equal(update.SectionsAdded, sectionsAdded.Count);
            Assert.Equal(update.SectionsRemoved, sectionsRemoved.Count);
            Assert.Equal(
                update.FieldsAdded,
                recorder.FieldCollectionChanges.Count(change => change.Change.Action == NotifyCollectionChangedAction.Add)
                + sectionsAdded.Sum(change => ((DetailSectionViewModel)change.NewItems![0]!).Fields.Count));
            Assert.Equal(
                update.FieldsRemoved,
                recorder.FieldCollectionChanges.Count(change => change.Change.Action == NotifyCollectionChangedAction.Remove)
                + sectionsRemoved.Sum(change => ((DetailSectionViewModel)change.OldItems![0]!).Fields.Count));
            Assert.Equal(update.FieldsUpdated, recorder.FieldChanges.Count(change => change.Property == nameof(DetailFieldViewModel.Value)));
            Assert.Equal(update.FieldsUpdated, recorder.FieldChanges.Where(change => change.Property == nameof(DetailFieldViewModel.Value)).Select(change => change.Field).Distinct(ReferenceEqualityComparer.Instance).Count());

            // Kept plus added is the new pane, kept plus removed the old one.
            Assert.Equal(newKeys.Count, update.SectionsKept + update.SectionsAdded);
            Assert.Equal(oldKeys.Count, update.SectionsKept + update.SectionsRemoved);
            Assert.Equal(newKeys.Sum(section => section.Fields.Count), update.FieldsKept + update.FieldsAdded);
            Assert.Equal(oldKeys.Sum(section => section.Fields.Count), update.FieldsKept + update.FieldsRemoved);

            // As few insertions and removals as any order-keeping edit can make (an LCS of their own).
            (int sections, int fields) = MinimalEdits(oldKeys, newKeys);
            Assert.True(sections == update.SectionsAdded + update.SectionsRemoved, pair + ": " + update + ", but " + sections + " section edits suffice");
            Assert.True(fields == update.FieldsAdded + update.FieldsRemoved, pair + ": " + update + ", but " + fields + " field edits suffice");

            if (update.HasStructureChange)
            {
                structureChanges++;
            }
            else if (update.FieldsUpdated > 0)
            {
                valueOnly++;
            }

            previous = rows[next].Name;
        }

        Assert.True(structureChanges > 50, "the walk changed the structure only " + structureChanges + " times");
        Assert.True(valueOnly > 5, "the walk changed values only " + valueOnly + " times");
    }

    // ---- The long-value help on a kept field ---------------------------------------------------

    [Fact]
    public void AReusedLongValue_KeepsItsHelpTextAndCountCurrent()
    {
        VariableItemViewModel dbx100 = Dbx(100);
        VariableItemViewModel dbx120 = Dbx(120);
        VariableItemViewModel db = Row(SnapshotRows(), "db");
        VariableDetailViewModel detail = new();
        detail.Show(dbx100);
        DetailFieldViewModel types = DecodedField(detail, "Signature types");
        Assert.Equal(798, types.Value.Length);
        string help798 = LongHelp(798);
        Assert.Equal(help798, types.HelpText);
        List<string> helps = new();
        List<string> raised = new();
        types.PropertyChanged += (_, e) =>
        {
            raised.Add(e.PropertyName ?? string.Empty);
            if (e.PropertyName == nameof(DetailFieldViewModel.HelpText))
            {
                helps.Add(types.HelpText);
            }
        };

        // 120 lists: the same field, a longer value, and its own count in the help.
        detail.Show(dbx120);
        Assert.Same(types, DecodedField(detail, "Signature types"));
        int length120 = types.Value.Length;
        Assert.True(length120 > 798, "120 lists give " + length120 + " characters");
        Assert.Equal(LongHelp(length120), types.HelpText);
        Assert.Equal(VariableDetailViewModel.HelpTextFor(types), types.HelpText);

        // db: the same field, a short value, the ordinary help.
        detail.Show(db);
        Assert.Same(types, DecodedField(detail, "Signature types"));
        Assert.False(VariableDetailViewModel.IsLongValue(types.Value, types.IsMultiline));
        Assert.Equal(Strings.DetailFieldHelp, types.HelpText);

        // Back to 100 lists: long again, with 798.
        detail.Show(dbx100);
        Assert.Same(types, DecodedField(detail, "Signature types"));
        Assert.Equal(help798, types.HelpText);

        Assert.Equal(new[] { LongHelp(length120), Strings.DetailFieldHelp, help798 }, helps);
        Assert.Equal(new[] { "Value", "HelpText", "Value", "HelpText", "Value", "HelpText" }, raised);

        // The same dbx again: nothing at all.
        raised.Clear();
        detail.Show(Dbx(100));
        Assert.Empty(raised);
    }

    [Fact]
    public void AFieldWhoseShapeChanges_IsReplaced_NotReused()
    {
        VariableItemViewModel single = Synthetic("ShapeA", "summary", ("Shape", "one line"));
        VariableItemViewModel multi = Synthetic("ShapeB", "summary", ("Shape", "two" + Environment.NewLine + "lines"));
        VariableDetailViewModel detail = new();
        detail.Show(single);
        DetailFieldViewModel before = DecodedField(detail, "Shape");
        DetailFieldViewModel summary = DecodedField(detail, VariableDetailViewModel.SummaryLabel);
        Assert.False(before.IsMultiline);
        using DetailPaneRecorder recorder = new(detail);

        DetailPaneUpdate update = detail.Show(multi);

        DetailFieldViewModel after = DecodedField(detail, "Shape");
        Assert.NotSame(before, after);
        Assert.True(after.IsMultiline);
        Assert.False(after.SelectsAllOnKeyboardFocus);
        Assert.Same(summary, DecodedField(detail, VariableDetailViewModel.SummaryLabel));
        Assert.Equal(new[] { "Fields of " + Strings.SectionDecoded + " Remove 1", "Fields of " + Strings.SectionDecoded + " Add 1" }, StructureEvents(recorder));
        Assert.Equal(1, update.FieldsAdded);
        Assert.Equal(1, update.FieldsRemoved);

        // The replaced field was left as it was: a box that still showed it is never restyled.
        Assert.Equal("one line", before.Value);
        Assert.DoesNotContain(recorder.FieldChanges, change => ReferenceEquals(change.Field, before));
        AssertIdentical(Describe(Fresh(multi)), detail, multi, "single-line then multi-line");
    }

    // ---- The counts --------------------------------------------------------------------------

    [Fact]
    public void TheUpdateCounts_MatchTheEvents_AndTheirTextIsPinned()
    {
        Assert.Equal("sections 5 kept, 0 added, 0 removed; fields 21 kept, 4 updated, 0 added, 0 removed", new DetailPaneUpdate(5, 0, 0, 21, 4, 0, 0).ToString());
        Assert.Equal("sections 4 kept, 1 added, 2 removed; fields 3 kept, 4 updated, 5 added, 6 removed", new DetailPaneUpdate(4, 1, 2, 3, 4, 5, 6).ToString());
        Assert.False(new DetailPaneUpdate(5, 0, 0, 21, 4, 0, 0).HasStructureChange);
        Assert.False(default(DetailPaneUpdate).HasStructureChange);
        Assert.True(new DetailPaneUpdate(4, 1, 0, 0, 0, 0, 0).HasStructureChange);
        Assert.True(new DetailPaneUpdate(4, 0, 1, 0, 0, 0, 0).HasStructureChange);
        Assert.True(new DetailPaneUpdate(5, 0, 0, 20, 0, 1, 0).HasStructureChange);
        Assert.True(new DetailPaneUpdate(5, 0, 0, 20, 0, 0, 1).HasStructureChange);

        IReadOnlyList<VariableItemViewModel> rows = SnapshotRows();
        VariableItemViewModel boot = Row(rows, "Boot0001");
        VariableItemViewModel vendor = Row(rows, "VendorConfig");
        VariableDetailViewModel detail = new();

        // From the empty pane: every section and field added.
        DetailPaneUpdate first = detail.Show(boot);
        int bootFields = detail.Sections.Sum(section => section.Fields.Count);
        int decodedFields = detail.Sections[3].Fields.Count;
        Assert.Equal(new DetailPaneUpdate(0, 5, 0, 0, 0, bootFields, 0), first);

        // To a raw variable: the Decoded section and its fields removed, every other field kept.
        DetailPaneUpdate second = detail.Show(vendor);
        int vendorFields = detail.Sections.Sum(section => section.Fields.Count);
        Assert.Equal(4, second.SectionsKept);
        Assert.Equal(0, second.SectionsAdded);
        Assert.Equal(1, second.SectionsRemoved);
        Assert.Equal(decodedFields, second.FieldsRemoved);
        Assert.Equal(0, second.FieldsAdded);
        Assert.Equal(vendorFields, second.FieldsKept);
        Assert.True(second.FieldsUpdated > 0);

        // To no row: everything removed, from the last section to the first.
        DetailPaneUpdate third = detail.Show(null);
        Assert.Equal(new DetailPaneUpdate(0, 0, 4, 0, 0, 0, vendorFields), third);

        // Numbers only, never a value, a label or a name.
        foreach (DetailPaneUpdate update in new[] { first, second, third })
        {
            Assert.Matches(@"^sections \d+ kept, \d+ added, \d+ removed; fields \d+ kept, \d+ updated, \d+ added, \d+ removed$", update.ToString());
        }
    }

    // ---- The field ---------------------------------------------------------------------------

    [Fact]
    public void DetailField_UpdateValue_RaisesValueAndOnlyARealHelpTextChange()
    {
        DetailFieldViewModel field = new("Long", new string('v', 10));
        List<string> raised = new();
        field.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);
        Assert.IsAssignableFrom<INotifyPropertyChanged>(field);
        Assert.Equal(Strings.DetailFieldHelp, field.HelpText);

        (string? Value, bool Changed, string[] Raised, string Help)[] steps =
        {
            (new string('v', 10), false, Array.Empty<string>(), Strings.DetailFieldHelp),
            (new string('v', 511), true, new[] { "Value" }, Strings.DetailFieldHelp),
            (new string('v', 512), true, new[] { "Value", "HelpText" }, LongHelp(512)),
            (new string('w', 512), true, new[] { "Value" }, LongHelp(512)),
            (new string('v', 600), true, new[] { "Value", "HelpText" }, LongHelp(600)),
            (null, true, new[] { "Value", "HelpText" }, Strings.DetailFieldHelp),
            (string.Empty, false, Array.Empty<string>(), Strings.DetailFieldHelp),
            (new string('v', 1000), true, new[] { "Value", "HelpText" }, LongHelp(1000)),
        };
        foreach ((string? value, bool changed, string[] expected, string help) in steps)
        {
            raised.Clear();
            Assert.Equal(changed, field.UpdateValue(value));
            Assert.Equal(expected, raised);
            Assert.Equal(value ?? string.Empty, field.Value);
            Assert.Equal(help, field.HelpText);
            Assert.Equal(VariableDetailViewModel.HelpTextFor(field), field.HelpText);
        }

        // A multi-line box is never a long value: its help never changes.
        DetailFieldViewModel hex = new(VariableDetailViewModel.HexDumpLabel, "00", isMultiline: true, accessibleName: Strings.HexDumpName);
        raised.Clear();
        hex.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);
        Assert.True(hex.UpdateValue(new string('0', 4000)));
        Assert.Equal(new[] { "Value" }, raised);
        Assert.Equal(Strings.DetailFieldHelp, hex.HelpText);
        Assert.Equal(Strings.HexDumpName, hex.AccessibleName);
        Assert.True(hex.IsMultiline);
    }

    [Fact]
    public void DetailField_LabelNameAndShape_HaveNoSetter()
    {
        foreach (string name in new[] { nameof(DetailFieldViewModel.Label), nameof(DetailFieldViewModel.AccessibleName), nameof(DetailFieldViewModel.IsMultiline), nameof(DetailFieldViewModel.SelectsAllOnKeyboardFocus) })
        {
            PropertyInfo property = typeof(DetailFieldViewModel).GetProperty(name)!;
            Assert.Null(property.SetMethod);
        }

        // The value and the help text change only through the pane's update, never from outside.
        foreach (string name in new[] { nameof(DetailFieldViewModel.Value), nameof(DetailFieldViewModel.HelpText) })
        {
            PropertyInfo property = typeof(DetailFieldViewModel).GetProperty(name)!;
            Assert.Null(property.GetSetMethod(nonPublic: false));
        }

        Assert.Null(typeof(DetailSectionViewModel).GetProperty(nameof(DetailSectionViewModel.Title))!.SetMethod);
        Assert.Null(typeof(DetailSectionViewModel).GetProperty(nameof(DetailSectionViewModel.Fields))!.SetMethod);
        Assert.Null(typeof(VariableDetailViewModel).GetProperty(nameof(VariableDetailViewModel.Sections))!.SetMethod);
        MethodInfo? update = typeof(DetailFieldViewModel).GetMethod("UpdateValue", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(update);
        Assert.True(update.IsAssembly, "UpdateValue is internal: only the pane changes a value");
    }

    // ---- Helpers -----------------------------------------------------------------------------

    /// <summary>
    /// Walks rows of one kind and checks each step: the same section and field objects by
    /// position, no collection change at all, a Value change for exactly the fields whose value
    /// differs and a HelpText change for exactly those whose help differs, in pane order, the
    /// pane's own changes limited to its scalar properties, and the result identical to a fresh pane.
    /// </summary>
    /// <returns>How many field values the walk changed.</returns>
    private static int AssertSameKindWalk(params VariableItemViewModel[] walk)
    {
        VariableDetailViewModel detail = new();
        detail.Show(walk[0]);
        int updated = 0;
        for (int k = 1; k < walk.Length; k++)
        {
            VariableItemViewModel next = walk[k];
            string pair = walk[k - 1].Name + " then " + next.Name;
            VariableDetailViewModel fresh = Fresh(next);

            // The guard: the two rows really are of one kind (the same sections and field keys).
            Assert.Equal(PaneKeys(fresh).Select(Flatten), PaneKeys(detail).Select(Flatten));

            DetailSectionViewModel[] sections = detail.Sections.ToArray();
            DetailFieldViewModel[][] fields = detail.Sections.Select(section => section.Fields.ToArray()).ToArray();
            List<string> expected = new();
            int differ = 0;
            for (int s = 0; s < sections.Length; s++)
            {
                for (int f = 0; f < fields[s].Length; f++)
                {
                    DetailFieldViewModel old = fields[s][f];
                    DetailFieldViewModel wanted = fresh.Sections[s].Fields[f];
                    if (!string.Equals(old.Value, wanted.Value, StringComparison.Ordinal))
                    {
                        expected.Add(sections[s].Title + "/" + old.Label + ".Value");
                        differ++;
                    }

                    if (!string.Equals(old.HelpText, wanted.HelpText, StringComparison.Ordinal))
                    {
                        expected.Add(sections[s].Title + "/" + old.Label + ".HelpText");
                    }
                }
            }

            using DetailPaneRecorder recorder = new(detail);
            DetailPaneUpdate update = detail.Show(next);

            Assert.Equal(sections, detail.Sections);
            for (int s = 0; s < sections.Length; s++)
            {
                Assert.Equal(fields[s], detail.Sections[s].Fields);
            }

            Assert.Empty(recorder.SectionChanges);
            Assert.Empty(recorder.FieldCollectionChanges);
            Assert.Equal(expected, recorder.Events.Where(line => !line.StartsWith("Detail.", StringComparison.Ordinal)));
            Assert.All(recorder.PaneProperties, name => Assert.Contains(name, PaneProperties));
            Assert.False(update.HasStructureChange, pair);
            Assert.Equal(new DetailPaneUpdate(sections.Length, 0, 0, fields.Sum(section => section.Length), differ, 0, 0), update);
            AssertIdentical(Describe(fresh), detail, next, pair);
            updated += differ;
        }

        return updated;
    }

    /// <summary>
    /// The collection changes a recorder saw, in order, as "Sections Add 3" or "Fields of
    /// Attributes Remove 2": the structure of an update without its value changes.
    /// </summary>
    private static List<string> StructureEvents(DetailPaneRecorder recorder)
    {
        return recorder.Events.Where(line => line.StartsWith("Sections ", StringComparison.Ordinal) || line.StartsWith("Fields of ", StringComparison.Ordinal)).ToList();
    }

    /// <summary>
    /// The collection changes the in-place update from pane <paramref name="from"/> to pane
    /// <paramref name="to"/> should make, computed independently with an O(n·m) longest common
    /// subsequence of the section titles and of each kept section's field keys: the removed
    /// sections from the last, then in the new order each added section or, for a kept section,
    /// its removed fields from the last and its added fields from the first. Used where the
    /// longest common subsequences are unique.
    /// </summary>
    private static List<string> ExpectedStructureEvents(VariableDetailViewModel from, VariableDetailViewModel to)
    {
        List<(string Title, List<string> Fields)> oldPane = PaneKeys(from);
        List<(string Title, List<string> Fields)> newPane = PaneKeys(to);
        (bool[] keepOld, bool[] keepNew) = Lcs(oldPane.Select(section => section.Title).ToList(), newPane.Select(section => section.Title).ToList());
        List<string> events = new();
        for (int i = oldPane.Count - 1; i >= 0; i--)
        {
            if (!keepOld[i])
            {
                events.Add("Sections Remove " + i.ToString(CultureInfo.InvariantCulture));
            }
        }

        for (int j = 0; j < newPane.Count; j++)
        {
            if (!keepNew[j])
            {
                events.Add("Sections Add " + j.ToString(CultureInfo.InvariantCulture));
                continue;
            }

            List<string> oldFields = oldPane.Single(section => section.Title == newPane[j].Title).Fields;
            List<string> newFields = newPane[j].Fields;
            (bool[] keepOldField, bool[] keepNewField) = Lcs(oldFields, newFields);
            for (int i = oldFields.Count - 1; i >= 0; i--)
            {
                if (!keepOldField[i])
                {
                    events.Add("Fields of " + newPane[j].Title + " Remove " + i.ToString(CultureInfo.InvariantCulture));
                }
            }

            for (int f = 0; f < newFields.Count; f++)
            {
                if (!keepNewField[f])
                {
                    events.Add("Fields of " + newPane[j].Title + " Add " + f.ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        return events;
    }

    /// <summary>
    /// The fewest section and field insertions plus removals that turn one pane into the other
    /// while keeping the order: |old| + |new| − 2·LCS for the section titles, and for the fields,
    /// the same over the field keys of each section both panes have plus every field of a section
    /// only one of them has. An O(n·m) dynamic programme, independent of the reconciler.
    /// </summary>
    private static (int Sections, int Fields) MinimalEdits(List<(string Title, List<string> Fields)> oldPane, List<(string Title, List<string> Fields)> newPane)
    {
        List<string> oldTitles = oldPane.Select(section => section.Title).ToList();
        List<string> newTitles = newPane.Select(section => section.Title).ToList();
        int sections = oldTitles.Count + newTitles.Count - (2 * LcsLength(oldTitles, newTitles));
        int fields = 0;
        foreach ((string title, List<string> keys) in oldPane)
        {
            List<string>? other = newPane.Where(section => section.Title == title).Select(section => section.Fields).SingleOrDefault();
            fields += other is null ? keys.Count : keys.Count + other.Count - (2 * LcsLength(keys, other));
        }

        fields += newPane.Where(section => !oldTitles.Contains(section.Title)).Sum(section => section.Fields.Count);
        return (sections, fields);
    }

    private static int LcsLength(List<string> a, List<string> b)
    {
        int[,] table = new int[a.Count + 1, b.Count + 1];
        for (int i = a.Count - 1; i >= 0; i--)
        {
            for (int j = b.Count - 1; j >= 0; j--)
            {
                table[i, j] = string.Equals(a[i], b[j], StringComparison.Ordinal) ? table[i + 1, j + 1] + 1 : Math.Max(table[i + 1, j], table[i, j + 1]);
            }
        }

        return table[0, 0];
    }

    private static (bool[] KeepA, bool[] KeepB) Lcs(List<string> a, List<string> b)
    {
        int[,] table = new int[a.Count + 1, b.Count + 1];
        for (int i = a.Count - 1; i >= 0; i--)
        {
            for (int j = b.Count - 1; j >= 0; j--)
            {
                table[i, j] = string.Equals(a[i], b[j], StringComparison.Ordinal) ? table[i + 1, j + 1] + 1 : Math.Max(table[i + 1, j], table[i, j + 1]);
            }
        }

        bool[] keepA = new bool[a.Count];
        bool[] keepB = new bool[b.Count];
        for (int i = 0, j = 0; i < a.Count && j < b.Count;)
        {
            if (string.Equals(a[i], b[j], StringComparison.Ordinal))
            {
                keepA[i++] = true;
                keepB[j++] = true;
            }
            else if (table[i + 1, j] >= table[i, j + 1])
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return (keepA, keepB);
    }

    /// <summary>The pane's section titles and, per section, its field keys: label, spoken name and shape, with the occurrence of that triple.</summary>
    private static List<(string Title, List<string> Fields)> PaneKeys(VariableDetailViewModel detail)
    {
        return detail.Sections.Select(section => (section.Title, FieldKeys(section.Fields))).ToList();
    }

    private static List<string> FieldKeys(IEnumerable<DetailFieldViewModel> fields)
    {
        Dictionary<string, int> seen = new(StringComparer.Ordinal);
        List<string> keys = new();
        foreach (DetailFieldViewModel field in fields)
        {
            string identity = field.Label + "\u001F" + field.AccessibleName + "\u001F" + (field.IsMultiline ? "multi-line" : "single-line");
            seen.TryGetValue(identity, out int occurrence);
            seen[identity] = occurrence + 1;
            keys.Add(identity + "#" + occurrence.ToString(CultureInfo.InvariantCulture));
        }

        return keys;
    }

    private static string Flatten((string Title, List<string> Fields) section) => section.Title + ": " + string.Join(" | ", section.Fields);

    /// <summary>
    /// Everything a screen reader or a sighted user can get from the pane, as lines: heading,
    /// selection state, hex dump, copy text, the Writability line, and per field its section,
    /// label, spoken name, shape, whole-value arrival, help text (the cached one and the policy's
    /// own for the field), long-value state, arrival selection and value.
    /// </summary>
    private static List<string> Describe(VariableDetailViewModel detail)
    {
        List<string> lines = new()
        {
            "Heading: " + detail.Heading,
            "HasSelection: " + detail.HasSelection,
            "HexDump: " + detail.HexDump,
            "CopyValueText: " + (detail.CopyValueText ?? "(null)"),
            "ReadOnlyStatus: " + detail.ReadOnlyStatus,
            "Sections: " + detail.Sections.Count.ToString(CultureInfo.InvariantCulture),
        };
        foreach (DetailSectionViewModel section in detail.Sections)
        {
            lines.Add("Section: " + section.Title + ", " + section.Fields.Count.ToString(CultureInfo.InvariantCulture) + " fields");
            foreach (DetailFieldViewModel field in section.Fields)
            {
                (int Start, int Length)? arrival = VariableDetailViewModel.SelectionOnKeyboardArrival(field.IsMultiline, field.Value.Length, clicked: false, returning: false, 0, 0);
                lines.Add(string.Join(
                    " | ",
                    "Field: " + section.Title,
                    field.Label,
                    field.AccessibleName,
                    field.IsMultiline ? "multi-line" : "single-line",
                    field.SelectsAllOnKeyboardFocus ? "selects all" : "does not select all",
                    field.HelpText,
                    VariableDetailViewModel.HelpTextFor(field),
                    VariableDetailViewModel.IsLongValue(field.Value, field.IsMultiline) ? "long" : "not long",
                    arrival is null ? "no arrival selection" : arrival.Value.Start.ToString(CultureInfo.InvariantCulture) + "+" + arrival.Value.Length.ToString(CultureInfo.InvariantCulture),
                    field.Value));
            }
        }

        return lines;
    }

    /// <summary>The pane must read exactly as the fresh one, line by line, and show the row itself.</summary>
    private static void AssertIdentical(List<string> fresh, VariableDetailViewModel detail, VariableItemViewModel? row, string pair)
    {
        Assert.Same(row, detail.Item);
        List<string> actual = Describe(detail);
        for (int i = 0; i < Math.Max(fresh.Count, actual.Count); i++)
        {
            string expected = i < fresh.Count ? fresh[i] : "(nothing)";
            string found = i < actual.Count ? actual[i] : "(nothing)";
            if (!string.Equals(expected, found, StringComparison.Ordinal))
            {
                Assert.Fail(pair + ": line " + i.ToString(CultureInfo.InvariantCulture) + " differs from a fresh pane" + Environment.NewLine + "fresh:    " + Shorten(expected) + Environment.NewLine + "in place: " + Shorten(found));
            }
        }

        // Every field's cached help is the policy's help for its value now.
        foreach (DetailFieldViewModel field in detail.Sections.SelectMany(section => section.Fields))
        {
            Assert.Equal(VariableDetailViewModel.HelpTextFor(field), field.HelpText);
        }
    }

    private static string Shorten(string text) => text.Length <= 400 ? text : text[..400] + "… (" + text.Length.ToString(CultureInfo.InvariantCulture) + " characters)";

    private static VariableDetailViewModel Fresh(VariableItemViewModel? row)
    {
        VariableDetailViewModel detail = new();
        detail.Show(row);
        return detail;
    }

    private static string LongHelp(int length) => string.Format(CultureInfo.InvariantCulture, Strings.LongDetailFieldHelpFormat, length);

    private static DetailFieldViewModel DecodedField(VariableDetailViewModel detail, string label)
    {
        return detail.Sections.Single(section => section.Title == Strings.SectionDecoded).Fields.Single(field => field.Label == label);
    }

    private static string NameOf(VariableItemViewModel? row) => row is null ? "(no row)" : row.Name;

    /// <summary>The list fixtures' 19 rows, decoded by the list exactly as the application decodes a load.</summary>
    private static IReadOnlyList<VariableItemViewModel> SnapshotRows()
    {
        VariableListViewModel list = new();
        list.Load(VariableListFixtures.Snapshot());
        Assert.Equal(VariableListFixtures.Count, list.AllItems.Count);
        return list.AllItems;
    }

    private static VariableItemViewModel Row(IReadOnlyList<VariableItemViewModel> rows, string name) => rows.Single(row => row.Name == name);

    /// <summary>The snapshot rows, the edge rows and no row.</summary>
    private static List<(string Name, VariableItemViewModel? Row)> AllRows()
    {
        List<(string Name, VariableItemViewModel? Row)> rows = SnapshotRows().Select(row => (row.Name, (VariableItemViewModel?)row)).ToList();
        rows.AddRange(EdgeRows());
        rows.Add(("(no row)", null));
        return rows;
    }

    /// <summary>
    /// Rows at the edges of what the pane shows: empty and null data, the registry's exception
    /// fallback (a Summary and an Error), a truncated load option (the decoder's own Error),
    /// decoded values of 511, 512 and 1000 characters under one label, a multi-line Summary and
    /// field, duplicate decoded labels, one label single-line in one row and multi-line in the
    /// next, no flags, every flag, an undefined bit, a 300-character name, ASCII, UTF-16 and long
    /// "First bytes as text", and dbx with 100 and 120 one-hash lists (a "Signature types" of 798
    /// characters and longer).
    /// </summary>
    private static List<(string Name, VariableItemViewModel? Row)> EdgeRows()
    {
        Guid global = KnownGuids.EfiGlobalVariable;
        DecodedVariable fallback = new(FirmwareVariable.MakeKey(global, "BootOrder"), "BootOrderDecoder", "decoding failed: boom", Array.Empty<DecodedField>(), null, "InvalidOperationException: boom");
        return new List<(string Name, VariableItemViewModel? Row)>
        {
            ("empty data", Raw("Empty", VendorGuid, NvBsRt, Array.Empty<byte>())),
            ("null data", Item(new FirmwareVariable("Odd", VendorGuid, NvBsRt, null!), null)),
            ("registry fallback", Item(new FirmwareVariable("BootOrder", global, NvBsRt, new byte[] { 1 }), fallback)),
            ("truncated Boot0002", Decoded(new FirmwareVariable("Boot0002", global, NvBsRt, LoadOptionFixtures.Truncated()))),
            ("decoded 511", Synthetic("Long511", "summary", ("Text", new string('v', 511)), ("Other", "short"))),
            ("decoded 512", Synthetic("Long512", "summary", ("Text", new string('v', 512)), ("Other", "short"))),
            ("decoded 1000", Synthetic("Long1000", "summary", ("Text", new string('w', 1000)), ("Other", "short"))),
            ("multi-line summary and field", Synthetic("Lines", "first line" + Environment.NewLine + "second line", ("Text", "one\ntwo"), ("Other", "short"))),
            ("duplicate labels", Synthetic("Dups", "summary", ("Dup", "a"), ("Dup", "b"), ("Other", "c"), ("Dup", "d"))),
            ("duplicate labels reordered", Synthetic("Dups2", "summary", ("Other", "c"), ("Dup", "a"), ("Dup", "e"))),
            ("shape single-line", Synthetic("ShapeA", "summary", ("Shape", "one line"))),
            ("shape multi-line", Synthetic("ShapeB", "summary", ("Shape", "two" + Environment.NewLine + "lines"))),
            ("decoded error, multi-line", Synthetic("Broken", "summary", "one error" + Environment.NewLine + "and its detail", ("Text", "short"))),
            ("no flags", Raw("NoFlags", VendorGuid, VariableAttributes.None, new byte[] { 1 })),
            ("every flag", Raw("AllFlags", VendorGuid, (VariableAttributes)0xFF, new byte[] { 1 })),
            ("undefined bit 0x100", Raw("Undefined", VendorGuid, NvBsRt | (VariableAttributes)0x100, new byte[] { 1 })),
            ("300-character name", Raw(new string('N', 300), VendorGuid, NvBsRt, new byte[] { 1 })),
            ("ASCII text", Raw("PlatformLang", global, NvBsRt, LoadOptionFixtures.AsciiZ("en-US"))),
            ("UTF-16 text", Raw("Wide", VendorGuid, NvBsRt, LoadOptionFixtures.Utf16Z("Hello"))),
            ("long text", Raw("Text", VendorGuid, NvBsRt, LoadOptionFixtures.AsciiZ(new string('t', 600)))),
            ("dbx, 100 lists", Dbx(100)),
            ("dbx, 120 lists", Dbx(120)),
        };
    }

    private static VariableItemViewModel Dbx(int lists)
    {
        byte[] data = LoadOptionFixtures.Concat(Enumerable.Range(0, lists).Select(_ => SignatureListFixtures.Sha256List(1)).ToArray());
        return Decoded(new FirmwareVariable("dbx", KnownGuids.EfiImageSecurityDatabase, NvBsRtTa, data));
    }

    private static VariableItemViewModel Raw(string name, Guid guid, VariableAttributes attributes, byte[] data)
    {
        return Item(new FirmwareVariable(name, guid, attributes, data), null);
    }

    /// <summary>A variable decoded through the default registry, as the list decodes it.</summary>
    private static VariableItemViewModel Decoded(FirmwareVariable variable)
    {
        DecodedVariable? decoded = DecoderRegistry.CreateDefault().TryDecode(variable, new VariableSet(new[] { variable }));
        Assert.NotNull(decoded);
        return Item(variable, decoded);
    }

    private static VariableItemViewModel Synthetic(string name, string summary, params (string Label, string Value)[] fields)
    {
        return Synthetic(name, summary, null, fields);
    }

    private static VariableItemViewModel Synthetic(string name, string summary, string? error, params (string Label, string Value)[] fields)
    {
        DecodedVariable decoded = new(
            FirmwareVariable.MakeKey(VendorGuid, name),
            "Synthetic",
            summary,
            fields.Select(field => new DecodedField(field.Label, field.Value)).ToArray(),
            null,
            error);
        return Item(new FirmwareVariable(name, VendorGuid, NvBsRt, new byte[] { 1, 2, 3 }), decoded);
    }

    private static VariableItemViewModel Item(FirmwareVariable variable, DecodedVariable? decoded)
    {
        return new VariableItemViewModel(variable, NamespaceCatalog.Describe(variable.VendorGuid), decoded);
    }
}
