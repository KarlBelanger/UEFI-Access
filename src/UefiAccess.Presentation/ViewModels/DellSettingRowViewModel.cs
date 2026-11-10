// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Settings.Dell;
using UefiAccess.Presentation.Infrastructure;
using UefiAccess.Presentation.Resources;

namespace UefiAccess.Presentation.ViewModels;

/// <summary>
/// One row of the list of Dell BIOS settings: one agreed
/// setting of the catalog, built once when the window starts and kept for its whole life
/// (Alpha 1 finding A1-6, Alpha 2 finding A2-7: rows that are removed and re-created raise a
/// burst of UI Automation events that delays the screen reader). Every read, every baseline folder
/// and every return to the live view changes only its texts, through <see cref="Update"/>, which
/// raises a change notification only for a text that really changes. Its automation name is the
/// whole row as one sentence, name first (<see cref="DellSentences.RowSentence(string, string, IReadOnlyList{string}, string)"/>):
/// "Num Lock at boot: Enabled. Possible values: Disabled, Enabled." It has no item status and no
/// help text (D3: no Dell row can change), and it never says that it is read-only (A2-6). It
/// exposes no command.
/// </summary>
public sealed class DellSettingRowViewModel : ObservableObject
{
    /// <summary>How the possible values are joined in <see cref="PossibleText"/>, as in the row sentence.</summary>
    internal const string PossibleSeparator = ", ";

    private string _currentText;
    private string _possibleText;
    private string _accessibleName;

    /// <summary>Creates the row of one agreed setting, before any read: its value is <see cref="Strings.DellNotLoadedYetValue"/> and it has no possible values.</summary>
    /// <param name="id">The catalog id, for example "NumLock".</param>
    /// <param name="displayName">The name shown and spoken, for example "Num Lock at boot".</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    internal DellSettingRowViewModel(string id, string displayName)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(displayName);
        Id = id;
        DisplayName = displayName;
        _currentText = Strings.DellNotLoadedYetValue;
        _possibleText = string.Empty;
        _accessibleName = Sentence(displayName, _currentText, _possibleText, null);
    }

    /// <summary>The catalog id, for example "NumLock"; never shown.</summary>
    public string Id { get; }

    /// <summary>The name shown in the Setting column and first in the row sentence, for example "Num Lock at boot"; the list's letter search goes by it.</summary>
    public string DisplayName { get; }

    /// <summary>The words for the current value, in the vocabulary of the BIOS setup screen ("Enabled", "not loaded yet"); never a code.</summary>
    public string CurrentText
    {
        get => _currentText;
        private set => SetProperty(ref _currentText, value);
    }

    /// <summary>The possible values' words joined with ", " ("Disabled, Enabled"); empty when none were read.</summary>
    public string PossibleText
    {
        get => _possibleText;
        private set => SetProperty(ref _possibleText, value);
    }

    /// <summary>The row as one sentence, name first, at most <see cref="DellSentences.RowSentenceLimit"/> characters: the row's automation name and what Ctrl+C and Copy value copy.</summary>
    public string AccessibleName
    {
        get => _accessibleName;
        private set => SetProperty(ref _accessibleName, value);
    }

    /// <summary>
    /// Shows new texts in place: each of <see cref="CurrentText"/>, <see cref="PossibleText"/> and
    /// <see cref="AccessibleName"/> is set, and notified, only when it differs, so showing the same
    /// texts again raises nothing.
    /// </summary>
    /// <param name="currentText">The words for the current value.</param>
    /// <param name="possibleText">The possible values' words, joined with ", "; empty for none.</param>
    /// <param name="note">A sentence said after the row (Core's note of the setting), or null.</param>
    /// <returns>True when <see cref="CurrentText"/> changed.</returns>
    internal bool Update(string currentText, string possibleText, string? note)
    {
        string current = currentText ?? string.Empty;
        string possible = possibleText ?? string.Empty;
        bool changed = !string.Equals(_currentText, current, StringComparison.Ordinal);
        CurrentText = current;
        PossibleText = possible;
        AccessibleName = Sentence(DisplayName, current, possible, note);
        return changed;
    }

    /// <summary>The row sentence of Core (the name first, cut at its limit by replacing the possible values that do not fit).</summary>
    private static string Sentence(string displayName, string currentText, string possibleText, string? note)
    {
        string[] possible = possibleText.Length == 0 ? Array.Empty<string>() : possibleText.Split(PossibleSeparator);
        return DellSentences.RowSentence(displayName, currentText, possible, string.IsNullOrWhiteSpace(note) ? null : note.Trim());
    }
}
