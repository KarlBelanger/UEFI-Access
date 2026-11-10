// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Presentation.Infrastructure;

namespace UefiAccess.Presentation.ViewModels;

/// <summary>
/// One labelled value in the detail pane, shown as a read-only TextBox (which exposes the Value
/// pattern). The automation name of a field is its label alone: the box itself carries the value
/// through the Value pattern, so a screen reader arriving on the field hears the label once and
/// the value once (a name of "label: value" would make it speak the value twice). A field may also
/// be given an explicit spoken name (the hex dump is "Raw data, hex dump"). Whether a field is
/// single-line or multi-line (<see cref="IsMultiline"/>) decides how the box is laid out and how
/// it is read on arrival: a single-line value wraps inside its box however long it is (nothing
/// to scroll sideways at a high text size or zoom) and is selected whole when keyboard focus
/// arrives (<see cref="SelectsAllOnKeyboardFocus"/>), so a screen reader reads all of it, not
/// only its first visual line; a multi-line value keeps its own lines and is read line by line.
/// The label, the spoken name and the shape never change. When the pane shows another row that
/// has the same field, in the same order relative to the other fields both rows have,
/// <see cref="VariableDetailViewModel.Show"/> keeps this object (so the view
/// keeps its box) and changes only <see cref="Value"/>, and with it <see cref="HelpText"/> when
/// that differs, each with a change notification.
/// </summary>
public sealed class DetailFieldViewModel : ObservableObject
{
    /// <summary>
    /// The cap shared with the row sentence (<see cref="VariableItemViewModel.AccessibleNameMaxLength"/>)
    /// for any spoken text built from a value with <see cref="Truncate"/>. Field names are the
    /// label alone and never include the value, so no field name reaches it.
    /// </summary>
    public const int AccessibleValueMaxLength = 200;

    private readonly string? _accessibleName;
    private string _value;
    private string _helpText;

    /// <summary>Creates the field.</summary>
    /// <param name="label">The label, for example "GUID".</param>
    /// <param name="value">The value as text; null is treated as an empty string.</param>
    /// <param name="isMultiline">True when the value spans several lines and is shown in a multi-line box (the hex dump, a decoded value with a line break); never for length alone.</param>
    /// <param name="accessibleName">An explicit spoken name, or null to use the label.</param>
    /// <exception cref="ArgumentNullException"><paramref name="label"/> is null.</exception>
    public DetailFieldViewModel(string label, string value, bool isMultiline = false, string? accessibleName = null)
    {
        ArgumentNullException.ThrowIfNull(label);
        Label = label;
        _value = value ?? string.Empty;
        IsMultiline = isMultiline;
        _accessibleName = string.IsNullOrWhiteSpace(accessibleName) ? null : accessibleName;
        _helpText = VariableDetailViewModel.HelpTextFor(this);
    }

    /// <summary>The label.</summary>
    public string Label { get; }

    /// <summary>
    /// The value. Only the pane changes it, when the row it shows changes and this field is kept
    /// (<see cref="UpdateValue"/>); the change is notified, so the box bound to it shows the new
    /// value.
    /// </summary>
    public string Value
    {
        get => _value;
        private set => SetProperty(ref _value, value);
    }

    /// <summary>
    /// The help text of the field's value box: <see cref="VariableDetailViewModel.HelpTextFor"/> of
    /// this field, that is the ordinary help, or for a long value the help that gives its length.
    /// Computed again whenever <see cref="Value"/> changes and notified only when the text itself
    /// changes (a long value turns short or the other way round, or a long value's length
    /// changes), so a kept box never keeps the help of the row shown before.
    /// </summary>
    public string HelpText
    {
        get => _helpText;
        private set => SetProperty(ref _helpText, value);
    }

    /// <summary>True when the value is shown in a multi-line box (its own lines, a monospace font); false for a box whose one line of text wraps.</summary>
    public bool IsMultiline { get; }

    /// <summary>
    /// True for a single-line value: the view selects the whole value when keyboard focus arrives
    /// on its box (Tab, F6, Enter from the list, a screen reader's focus request; a click places
    /// the caret as usual). NVDA reads a text box that gets focus only up to the end of the visual
    /// line at the caret, so a wrapped value was heard up to its first line (round 1: Boot0000's
    /// Device path heard as "HD"); with the value selected it reads the selection whole, as
    /// "selected" and the value. False for a multi-line value, which is read line by line (a
    /// selected hex dump would be announced as its number of characters instead).
    /// </summary>
    public bool SelectsAllOnKeyboardFocus => !IsMultiline;

    /// <summary>
    /// The spoken name: the explicit name when one was given, otherwise the label alone. The
    /// value is never part of the name; the read-only box exposes it through the Value pattern.
    /// </summary>
    public string AccessibleName => _accessibleName ?? Label;

    /// <summary>
    /// Takes the value this field has in the row the pane shows now. <see cref="Value"/> changes,
    /// with its notification, only when the text differs, and then <see cref="HelpText"/> is
    /// computed again (and notified only when it differs).
    /// </summary>
    /// <param name="value">The new value; null is treated as an empty string.</param>
    /// <returns>True when the value changed.</returns>
    internal bool UpdateValue(string? value)
    {
        string text = value ?? string.Empty;
        if (string.Equals(_value, text, StringComparison.Ordinal))
        {
            return false;
        }

        Value = text;
        HelpText = VariableDetailViewModel.HelpTextFor(this);
        return true;
    }

    /// <summary>
    /// Truncates text to a maximum length, replacing the tail with a single ellipsis character.
    /// The cut never splits a surrogate pair, so the result is always well-formed UTF-16.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="maxLength">The maximum length of the result, at least 1.</param>
    /// <returns>The text, or its first characters followed by "…".</returns>
    public static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text ?? string.Empty;
        }

        int keep = Math.Max(0, maxLength - 1);
        if (keep > 0 && char.IsHighSurrogate(text[keep - 1]))
        {
            keep--;
        }

        return text[..keep] + "…";
    }
}
