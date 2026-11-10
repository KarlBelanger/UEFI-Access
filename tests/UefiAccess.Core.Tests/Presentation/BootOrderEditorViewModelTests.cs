// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Specialized;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Variables;
using UefiAccess.Presentation.Resources;
using UefiAccess.Presentation.ViewModels;
using Xunit;
using static UefiAccess.Core.Tests.Fixtures.LoadOptionFixtures;
using static UefiAccess.Core.Tests.Safety.SafetyFixtures;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// The proposed boot order of the Change group: the rows as the
/// firmware's BootOrder and its Boot#### entries give them (name, description, inactive, hidden;
/// the position left to the item peer), Move up and Move down as one ObservableCollection.Move each
/// with the limit sentences, Ctrl+Z back to the firmware's order by moves, HasChanges, and the
/// request of entry numbers. Nothing here reads or writes the firmware.
/// </summary>
public sealed class BootOrderEditorViewModelTests
{
    [Fact]
    public void Load_MakesOneRowPerEntry_InTheFirmwaresOrder_AndSelectsTheFirst()
    {
        BootOrderEditorViewModel editor = Loaded(WindowsOnPartition1, UsbNumber);

        Assert.Equal(new[] { "Boot0002", "Boot0003" }, editor.Entries.Select(entry => entry.Name));
        Assert.Equal(new ushort[] { 2, 3 }, editor.Entries.Select(entry => entry.Number));
        Assert.Equal(new ushort[] { 2, 3 }, editor.FirmwareOrder);
        Assert.Equal("Boot0002, Windows Boot Manager", editor.Entries[0].AccessibleName);
        Assert.Equal("Boot0003, " + UsbStickDescription, editor.Entries[1].AccessibleName);
        Assert.Same(editor.Entries[0], editor.SelectedEntry);
        Assert.False(editor.HasChanges);
    }

    [Fact]
    public void RowNames_AppendInactiveAndHidden_AndNeverThePosition()
    {
        BootOrderEditorViewModel editor = Loaded(InactiveNumber, HiddenNumber, WindowsOnPartition1);

        Assert.Equal("Boot0006, Old system, inactive", editor.Entries[0].AccessibleName);
        Assert.Equal("Boot0005, Hidden tool, hidden", editor.Entries[1].AccessibleName);
        Assert.Equal("Boot0002, Windows Boot Manager", editor.Entries[2].AccessibleName);
        Assert.All(editor.Entries, entry => Assert.DoesNotContain(" of ", entry.AccessibleName, StringComparison.Ordinal));
        Assert.Equal(editor.Entries[0].AccessibleName, editor.Entries[0].ToString());
    }

    [Fact]
    public void AnEntryTheFirmwareDoesNotHold_IsListedWithCoresMissingText()
    {
        BootOrderEditorViewModel editor = Loaded(WindowsOnPartition1, MissingNumber);

        BootOrderEntryViewModel missing = editor.Entries[1];
        Assert.False(missing.Present);
        Assert.Equal("Boot0010, " + CheckSentences.MissingEntryDescription, missing.AccessibleName);
    }

    [Fact]
    public void MoveUp_IsOneCollectionMove_KeepsTheRowAndItsSelection_AndSaysTheNewPosition()
    {
        BootOrderEditorViewModel editor = Loaded(WindowsOnPartition1, UsbNumber);
        BootOrderEntryViewModel usb = editor.Entries[1];
        editor.SelectedEntry = usb;
        List<NotifyCollectionChangedEventArgs> changes = new();
        editor.Entries.CollectionChanged += (_, e) => changes.Add(e);

        string sentence = editor.MoveUp();

        Assert.Equal("Boot0003 (" + UsbStickDescription + ") moved to position 1 of 2.", sentence);
        NotifyCollectionChangedEventArgs change = Assert.Single(changes);
        Assert.Equal(NotifyCollectionChangedAction.Move, change.Action);
        Assert.Equal(1, change.OldStartingIndex);
        Assert.Equal(0, change.NewStartingIndex);
        Assert.Same(usb, editor.Entries[0]);
        Assert.Same(usb, editor.SelectedEntry);
        Assert.True(editor.HasChanges);
    }

    [Fact]
    public void MoveDown_IsOneCollectionMove_AndSaysTheNewPosition()
    {
        BootOrderEditorViewModel editor = Loaded(WindowsOnPartition1, UsbNumber, WindowsOnPartition2);
        List<NotifyCollectionChangedEventArgs> changes = new();
        editor.Entries.CollectionChanged += (_, e) => changes.Add(e);

        string sentence = editor.MoveDown();

        Assert.Equal("Boot0002 (Windows Boot Manager) moved to position 2 of 3.", sentence);
        Assert.Equal(NotifyCollectionChangedAction.Move, Assert.Single(changes).Action);
        Assert.Equal(new ushort[] { 3, 2, 0 }, editor.Entries.Select(entry => entry.Number));
    }

    [Fact]
    public void AtTheLimits_NothingMoves_AndTheSentenceSaysSo()
    {
        BootOrderEditorViewModel editor = Loaded(WindowsOnPartition1, UsbNumber);
        List<NotifyCollectionChangedEventArgs> changes = new();
        editor.Entries.CollectionChanged += (_, e) => changes.Add(e);

        Assert.Equal("Boot0002 (Windows Boot Manager) is already first.", editor.MoveUp());
        editor.SelectedEntry = editor.Entries[1];
        Assert.Equal("Boot0003 (" + UsbStickDescription + ") is already last.", editor.MoveDown());

        Assert.Empty(changes);
        Assert.False(editor.HasChanges);
    }

    [Fact]
    public void OnTheUnitAsItStands_OneEntry_IsAlreadyFirstAndAlreadyLast()
    {
        // BootOrder holds Boot0002 alone.
        BootOrderEditorViewModel editor = Loaded(WindowsOnPartition1);

        Assert.Equal("Boot0002 (Windows Boot Manager) is already first.", editor.MoveUp());
        Assert.Equal("Boot0002 (Windows Boot Manager) is already last.", editor.MoveDown());
        Assert.False(editor.HasChanges);
    }

    [Fact]
    public void WithNoRowSelected_AMoveSaysNothingAndMovesNothing()
    {
        BootOrderEditorViewModel editor = Loaded(WindowsOnPartition1, UsbNumber);
        editor.SelectedEntry = null;

        Assert.Equal(string.Empty, editor.MoveUp());
        Assert.Equal(string.Empty, editor.MoveDown());
        Assert.Equal(new ushort[] { 2, 3 }, editor.Entries.Select(entry => entry.Number));
    }

    [Fact]
    public void ResetToFirmwareOrder_PutsTheOrderBackByMoves_AndKeepsTheSelection()
    {
        BootOrderEditorViewModel editor = Loaded(WindowsOnPartition1, UsbNumber, WindowsOnPartition2);
        BootOrderEntryViewModel last = editor.Entries[2];
        editor.SelectedEntry = last;
        editor.MoveUp();
        editor.MoveUp();
        Assert.Equal(new ushort[] { 0, 2, 3 }, editor.Entries.Select(entry => entry.Number));
        List<NotifyCollectionChangedEventArgs> changes = new();
        editor.Entries.CollectionChanged += (_, e) => changes.Add(e);

        string sentence = editor.ResetToFirmwareOrder();

        Assert.Equal(Strings.OrderResetStatus, sentence);
        Assert.Equal(new ushort[] { 2, 3, 0 }, editor.Entries.Select(entry => entry.Number));
        Assert.NotEmpty(changes);
        Assert.All(changes, change => Assert.Equal(NotifyCollectionChangedAction.Move, change.Action));
        Assert.Same(last, editor.SelectedEntry);
        Assert.False(editor.HasChanges);
    }

    [Fact]
    public void HasChanges_FollowsTheOrder_BackAndForth()
    {
        BootOrderEditorViewModel editor = Loaded(WindowsOnPartition1, UsbNumber);
        List<string?> changed = new();
        editor.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        editor.MoveDown();
        Assert.True(editor.HasChanges);
        editor.MoveUp();
        Assert.False(editor.HasChanges);
        Assert.Equal(2, changed.Count(name => name == nameof(BootOrderEditorViewModel.HasChanges)));
    }

    [Fact]
    public void BuildRequest_CarriesTheEntryNumbersOfTheProposedOrder_FromTheApplication_NeverBytes()
    {
        BootOrderEditorViewModel editor = Loaded(WindowsOnPartition1, UsbNumber);
        editor.SelectedEntry = editor.Entries[1];
        editor.MoveUp();

        ReorderRequest request = editor.BuildRequest();

        Assert.Equal(ChangeRequest.AppHost, request.Host);
        Assert.Equal(new ushort[] { 3, 2 }, request.NewOrder);
        Assert.False(request.AllowNoChange);
        Assert.Null(request.RestoredFrom);
        Assert.DoesNotContain(typeof(ReorderRequest).GetProperties(), property => property.PropertyType == typeof(byte[]));
    }

    [Fact]
    public void Load_ReplacesAProposedOrder_AndClearEmptiesTheEditor()
    {
        BootOrderEditorViewModel editor = Loaded(WindowsOnPartition1, UsbNumber);
        editor.MoveDown();
        Assert.True(editor.HasChanges);

        VariableSet set = StoreWithOrder(UsbNumber, WindowsOnPartition1);
        editor.Load(set.Find("BootOrder", Global), set);

        Assert.False(editor.HasChanges);
        Assert.Equal(new ushort[] { 3, 2 }, editor.Entries.Select(entry => entry.Number));

        editor.Clear();

        Assert.Empty(editor.Entries);
        Assert.Null(editor.SelectedEntry);
        Assert.Empty(editor.FirmwareOrder);
        Assert.False(editor.HasChanges);
        Assert.Equal(string.Empty, editor.MoveUp());
    }

    /// <summary>An editor loaded from the Safety fixtures' store with BootOrder set to <paramref name="order"/>.</summary>
    private static BootOrderEditorViewModel Loaded(params ushort[] order)
    {
        VariableSet set = StoreWithOrder(order);
        BootOrderEditorViewModel editor = new();
        editor.Load(set.Find("BootOrder", Global), set);
        return editor;
    }
}
