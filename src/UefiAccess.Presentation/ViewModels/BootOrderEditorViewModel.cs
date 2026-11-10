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

namespace UefiAccess.Presentation.ViewModels;

/// <summary>
/// One row of the Boot order entries list: a Boot#### the firmware's BootOrder names, with its
/// description and flags as the load option holds them, and its position in the firmware's order.
/// <see cref="AccessibleName"/> is "name, description" with ", inactive" and ", hidden" appended
/// when so; the position is left to the list item's automation peer, which screen readers speak
/// as "1 of 2", so the name never repeats it. Immutable: a move changes the row's place in the
/// collection, never the row.
/// </summary>
public sealed class BootOrderEntryViewModel
{
    /// <summary>Creates the row.</summary>
    /// <param name="number">The entry number (Boot0003 is 3).</param>
    /// <param name="firmwarePosition">The 0-based position of this entry in the firmware's BootOrder.</param>
    /// <param name="description">The load option's description, or the placeholder for a missing or malformed entry.</param>
    /// <param name="active">LOAD_OPTION_ACTIVE.</param>
    /// <param name="hidden">LOAD_OPTION_HIDDEN.</param>
    /// <param name="present">False when BootOrder names an entry the firmware does not hold.</param>
    public BootOrderEntryViewModel(ushort number, int firmwarePosition, string description, bool active, bool hidden, bool present)
    {
        Number = number;
        FirmwarePosition = firmwarePosition;
        Name = KnownVariableNames.BootName(number);
        Description = description ?? string.Empty;
        Active = active;
        Hidden = hidden;
        Present = present;
        string name = string.Format(CultureInfo.InvariantCulture, Strings.BootOrderEntryFormat, Name, Description);
        if (present && !active)
        {
            name += Strings.BootOrderEntryInactive;
        }

        if (present && hidden)
        {
            name += Strings.BootOrderEntryHidden;
        }

        AccessibleName = name;
    }

    /// <summary>The entry number, the value the request carries.</summary>
    public ushort Number { get; }

    /// <summary>The 0-based position of this entry in the firmware's BootOrder when it was read.</summary>
    public int FirmwarePosition { get; }

    /// <summary>The Boot#### name.</summary>
    public string Name { get; }

    /// <summary>The load option's description, or the placeholder for a missing or malformed entry.</summary>
    public string Description { get; }

    /// <summary>LOAD_OPTION_ACTIVE.</summary>
    public bool Active { get; }

    /// <summary>LOAD_OPTION_HIDDEN.</summary>
    public bool Hidden { get; }

    /// <summary>False when BootOrder names an entry the firmware does not hold.</summary>
    public bool Present { get; }

    /// <summary>The row's automation name: "Boot0002, Windows Boot Manager", with ", inactive" and ", hidden" appended when so.</summary>
    public string AccessibleName { get; }

    /// <inheritdoc />
    public override string ToString() => AccessibleName;
}

/// <summary>
/// The proposed boot order of the Change group: a copy of the
/// firmware's BootOrder that Move up, Move down and Ctrl+Z rearrange. Every rearrangement is an
/// <see cref="ObservableCollection{T}.Move"/>, never a rebuild, so the row keeps its item
/// container and its focus and the Status sentence is the one announcement of a move. Nothing
/// here writes: <see cref="BuildRequest"/> turns the proposed order into a
/// <see cref="ReorderRequest"/> of entry numbers, which the write pipeline checks again against a
/// fresh read. The proposed order lives until the next load (Refresh, Open baseline folder or the
/// reload after a write) or Ctrl+Z; moving to another variable and back keeps it.
/// </summary>
public sealed class BootOrderEditorViewModel : ObservableObject
{
    private IReadOnlyList<ushort> _firmwareOrder = Array.Empty<ushort>();
    private BootOrderEntryViewModel? _selectedEntry;
    private bool _hasChanges;

    /// <summary>Creates an empty editor.</summary>
    public BootOrderEditorViewModel()
    {
        Entries = new ObservableCollection<BootOrderEntryViewModel>();
    }

    /// <summary>The proposed order, one row per entry; the list binds its ItemsSource to this and only moves change it.</summary>
    public ObservableCollection<BootOrderEntryViewModel> Entries { get; }

    /// <summary>The selected row (the list's SelectedItem, two-way), or null.</summary>
    public BootOrderEntryViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set => SetProperty(ref _selectedEntry, value);
    }

    /// <summary>True when the proposed order differs from the firmware's.</summary>
    public bool HasChanges
    {
        get => _hasChanges;
        private set => SetProperty(ref _hasChanges, value);
    }

    /// <summary>The entry numbers of the firmware's BootOrder as read by the last load.</summary>
    public IReadOnlyList<ushort> FirmwareOrder => _firmwareOrder;

    /// <summary>
    /// Replaces the proposed order with the firmware's: one row per entry of
    /// <paramref name="bootOrder"/>, each described from its Boot#### in
    /// <paramref name="variables"/>, and selects the first row. A null BootOrder empties the editor.
    /// </summary>
    /// <param name="bootOrder">BootOrder as read, or null.</param>
    /// <param name="variables">The variables it was read with, for the Boot#### entries; null makes every entry missing.</param>
    public void Load(FirmwareVariable? bootOrder, IVariableLookup? variables)
    {
        IReadOnlyList<ushort> order = bootOrder is null ? Array.Empty<ushort>() : BootListCodec.Decode(bootOrder.Data).Entries;
        _firmwareOrder = order.ToArray();
        SelectedEntry = null;
        Entries.Clear();
        for (int position = 0; position < _firmwareOrder.Count; position++)
        {
            Entries.Add(Row(_firmwareOrder[position], position, variables));
        }

        OnPropertyChanged(nameof(FirmwareOrder));
        HasChanges = false;
        SelectedEntry = Entries.Count > 0 ? Entries[0] : null;
    }

    /// <summary>Empties the editor (a baseline view, a refused or failed load).</summary>
    public void Clear() => Load(null, null);

    /// <summary>
    /// Moves the selected entry one position up with <see cref="ObservableCollection{T}.Move"/>.
    /// At the top nothing moves. Returns the Status sentence either way.
    /// </summary>
    /// <returns><see cref="Strings.MovedFormat"/> or <see cref="Strings.AlreadyFirstFormat"/>; empty when no row is selected.</returns>
    public string MoveUp() => MoveSelected(-1);

    /// <summary>
    /// Moves the selected entry one position down with <see cref="ObservableCollection{T}.Move"/>.
    /// At the bottom nothing moves. Returns the Status sentence either way.
    /// </summary>
    /// <returns><see cref="Strings.MovedFormat"/> or <see cref="Strings.AlreadyLastFormat"/>; empty when no row is selected.</returns>
    public string MoveDown() => MoveSelected(1);

    /// <summary>
    /// Puts the firmware's order back (Ctrl+Z), one <see cref="ObservableCollection{T}.Move"/> per
    /// row out of place, so every row keeps its container and the selection stays on the same entry.
    /// </summary>
    /// <returns><see cref="Strings.OrderResetStatus"/>.</returns>
    public string ResetToFirmwareOrder()
    {
        for (int position = 0; position < Entries.Count; position++)
        {
            int current = IndexOfFirmwarePosition(position, position);
            if (current > position)
            {
                Entries.Move(current, position);
            }
        }

        HasChanges = ComputeHasChanges();
        return Strings.OrderResetStatus;
    }

    /// <summary>The request for the proposed order: its entry numbers, from the application, never bytes.</summary>
    /// <returns>A <see cref="ReorderRequest"/> with <see cref="ChangeRequest.AppHost"/>; the application never allows an unchanged order.</returns>
    public ReorderRequest BuildRequest()
    {
        return new ReorderRequest(ChangeRequest.AppHost, Entries.Select(entry => entry.Number).ToArray());
    }

    private static BootOrderEntryViewModel Row(ushort number, int position, IVariableLookup? variables)
    {
        FirmwareVariable? entry = variables?.Find(KnownVariableNames.BootName(number), KnownGuids.EfiGlobalVariable);
        if (entry is null)
        {
            return new BootOrderEntryViewModel(number, position, CheckSentences.MissingEntryDescription, active: false, hidden: false, present: false);
        }

        LoadOption option = LoadOptionParser.Parse(entry.Data ?? Array.Empty<byte>());
        string description = option.Description;
        if (string.IsNullOrEmpty(description))
        {
            description = option.Error is null ? CheckSentences.NoDescription : CheckSentences.MalformedEntryDescription;
        }

        return new BootOrderEntryViewModel(number, position, description, option.Active, option.Hidden, present: true);
    }

    private string MoveSelected(int step)
    {
        BootOrderEntryViewModel? entry = _selectedEntry;
        int index = entry is null ? -1 : Entries.IndexOf(entry);
        if (entry is null || index < 0)
        {
            return string.Empty;
        }

        int target = index + step;
        if (target < 0)
        {
            return string.Format(CultureInfo.InvariantCulture, Strings.AlreadyFirstFormat, entry.Name, entry.Description);
        }

        if (target >= Entries.Count)
        {
            return string.Format(CultureInfo.InvariantCulture, Strings.AlreadyLastFormat, entry.Name, entry.Description);
        }

        Entries.Move(index, target);
        HasChanges = ComputeHasChanges();
        return string.Format(CultureInfo.InvariantCulture, Strings.MovedFormat, entry.Name, entry.Description, target + 1, Entries.Count);
    }

    /// <summary>The index of the row that was at <paramref name="firmwarePosition"/> in the firmware's order, searching from <paramref name="start"/>.</summary>
    private int IndexOfFirmwarePosition(int firmwarePosition, int start)
    {
        for (int index = start; index < Entries.Count; index++)
        {
            if (Entries[index].FirmwarePosition == firmwarePosition)
            {
                return index;
            }
        }

        return -1;
    }

    private bool ComputeHasChanges()
    {
        for (int index = 0; index < Entries.Count; index++)
        {
            if (Entries[index].FirmwarePosition != index)
            {
                return true;
            }
        }

        return false;
    }
}
