// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Platform;

namespace UefiAccess.Core.Settings.Dell;

/// <summary>
/// Builds one setting of the common model from a catalog entry and the interface's tokens
///, shared by the providers and the baseline file's reader so
/// that every text comes from the current catalog. Values are named by the interface's own name for
/// them, never by their code: Dell Command | Monitor's codes, the setup forms' values and Dell
/// Command | Configure's words are three separate vocabularies, and the polarity of on and off
/// codes differs from attribute to attribute.
/// </summary>
internal static class DellSettingMapper
{
    /// <summary>
    /// One setting from Dell Command | Monitor's codes and names. No
    /// current code gives <see cref="SettingState.NoValueReported"/> with the entry's no-value note;
    /// a code without a name gives <see cref="SettingState.Unnamed"/> (the code kept as the token,
    /// never as the text); a name the catalog knows (case-insensitive) gives
    /// <see cref="SettingState.Read"/> with the catalog's text and the entry's Dell Command |
    /// Monitor note; a name it does not know gives Read with Dell's own word, marked
    /// <see cref="FirmwareSetting.DisplayFromVendor"/>. The possible values follow Dell's order,
    /// each named the same way. Several current codes, which Dell Command | Monitor allows, are
    /// named one by one and joined with ", ".
    /// </summary>
    /// <param name="entry">The catalog entry.</param>
    /// <param name="codes">The current value codes, blank elements kept as empty strings.</param>
    /// <param name="possible">The possible value codes, blank elements kept as empty strings.</param>
    /// <param name="names">The possible values' names, index for index with <paramref name="possible"/>.</param>
    /// <param name="isReadOnly">Dell's IsReadOnly as reported; null when it is missing.</param>
    /// <returns>The setting.</returns>
    internal static FirmwareSetting FromDcm(DellCatalogEntry entry, IReadOnlyList<string> codes, IReadOnlyList<string> possible, IReadOnlyList<string> names, bool? isReadOnly)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(codes);
        ArgumentNullException.ThrowIfNull(possible);
        ArgumentNullException.ThrowIfNull(names);

        List<SettingValue> possibleValues = new();
        for (int i = 0; i < possible.Count; i++)
        {
            if (possible[i].Length == 0)
            {
                continue;
            }

            string? name = i < names.Count && names[i].Length > 0 ? names[i] : null;
            possibleValues.Add(name is null ? new SettingValue(possible[i], null, DellSentences.UnnamedValue) : DcmValue(entry, possible[i], name, out _));
        }

        string[] current = codes.Where(code => code.Length > 0).ToArray();
        if (current.Length == 0)
        {
            return Setting(entry, SettingState.NoValueReported, null, possibleValues, PossibleValuesOrigin.Interface, isReadOnly, false, entry.NoValueNote);
        }

        List<SettingValue> values = new();
        bool fromVendor = false;
        foreach (string code in current)
        {
            string? name = DellInterfaceProbe.DescribeValues(new[] { code }, possible, names);
            if (name is null)
            {
                SettingValue unnamed = new(string.Join(", ", current), null, DellSentences.UnnamedValue);
                return Setting(entry, SettingState.Unnamed, unnamed, possibleValues, PossibleValuesOrigin.Interface, isReadOnly, false, null);
            }

            values.Add(DcmValue(entry, code, name, out bool vendorWord));
            fromVendor |= vendorWord;
        }

        SettingValue value = values.Count == 1
            ? values[0]
            : new SettingValue(string.Join(", ", values.Select(item => item.Token)), string.Join(", ", values.Select(item => item.VendorName)), string.Join(", ", values.Select(item => item.Text)));
        return Setting(entry, SettingState.Read, value, possibleValues, PossibleValuesOrigin.Interface, isReadOnly, fromVendor, entry.DcmNote);
    }

    /// <summary>
    /// One setting from the word Dell Command | Configure printed for it. A word the catalog lists among the value's words (the 5.2.2 one or the legacy one,
    /// compared case-insensitively) gives <see cref="SettingState.Read"/> with the catalog's text;
    /// a word it does not list gives Read with the word as printed, marked
    /// <see cref="FirmwareSetting.DisplayFromVendor"/>. The program prints only the current value,
    /// so the possible values are the catalog's (<see cref="PossibleValuesOrigin.Catalog"/>, the
    /// attribute map's for the verified BIOS), each with its 5.2.2 word as the token. There is no
    /// vendor read-only flag and no note.
    /// </summary>
    /// <param name="entry">The catalog entry.</param>
    /// <param name="token">The value as printed after the "=" of its line, trimmed.</param>
    /// <returns>The setting.</returns>
    internal static FirmwareSetting FromCctk(DellCatalogEntry entry, string token)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(token);
        DellValueText? known = FindByCctkToken(entry, token);
        SettingValue current = new(token, null, known?.Text ?? token);
        SettingValue[] possibleValues = entry.Values.Select(value => new SettingValue(value.CctkTokens.Count > 0 ? value.CctkTokens[0] : value.Text, null, value.Text)).ToArray();
        return Setting(entry, SettingState.Read, current, possibleValues, PossibleValuesOrigin.Catalog, null, known is null, null);
    }

    /// <summary>A setting the interface answered for without this entry (DF10): no value and no possible values.</summary>
    /// <param name="entry">The catalog entry.</param>
    /// <param name="origin">Where the provider's possible values come from.</param>
    /// <param name="note">A sentence said after the row, or null.</param>
    /// <returns>The setting.</returns>
    internal static FirmwareSetting NotReported(DellCatalogEntry entry, PossibleValuesOrigin origin, string? note)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return Setting(entry, SettingState.NotReported, null, Array.Empty<SettingValue>(), origin, null, false, note);
    }

    /// <summary>A setting nothing was read for: no interface answered, or the read stopped before it.</summary>
    /// <param name="entry">The catalog entry.</param>
    /// <param name="note">A sentence said after the row, or null.</param>
    /// <returns>The setting.</returns>
    internal static FirmwareSetting NotRead(DellCatalogEntry entry, string? note)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return Setting(entry, SettingState.NotRead, null, Array.Empty<SettingValue>(), PossibleValuesOrigin.Catalog, null, false, note);
    }

    /// <summary>The entry's value whose Dell Command | Monitor name is <paramref name="dcmName"/>, compared case-insensitively; null when the catalog has none.</summary>
    /// <param name="entry">The catalog entry.</param>
    /// <param name="dcmName">Dell Command | Monitor's name for a value.</param>
    /// <returns>The value, or null.</returns>
    internal static DellValueText? FindByDcmName(DellCatalogEntry entry, string dcmName)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Values.FirstOrDefault(value => value.DcmName is not null && string.Equals(value.DcmName, dcmName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The entry's value that lists <paramref name="token"/> among its Dell Command | Configure words, compared case-insensitively; null when none does.</summary>
    /// <param name="entry">The catalog entry.</param>
    /// <param name="token">A word Dell Command | Configure printed for a value.</param>
    /// <returns>The value, or null.</returns>
    internal static DellValueText? FindByCctkToken(DellCatalogEntry entry, string token)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(token);
        string printed = token.Trim();
        return entry.Values.FirstOrDefault(value => value.CctkTokens.Any(word => string.Equals(word, printed, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>One Dell Command | Monitor value: the catalog's text for its name, or Dell's own word when the catalog has none.</summary>
    private static SettingValue DcmValue(DellCatalogEntry entry, string code, string name, out bool vendorWord)
    {
        DellValueText? known = FindByDcmName(entry, name);
        vendorWord = known is null;
        return new SettingValue(code, name, known?.Text ?? name);
    }

    private static FirmwareSetting Setting(
        DellCatalogEntry entry,
        SettingState state,
        SettingValue? current,
        IReadOnlyList<SettingValue> possibleValues,
        PossibleValuesOrigin origin,
        bool? vendorMarksReadOnly,
        bool displayFromVendor,
        string? note)
    {
        return new FirmwareSetting(
            entry.Id,
            entry.DcmAttributeName,
            entry.DisplayName,
            entry.Category,
            entry.SetupLocation,
            state,
            current,
            possibleValues,
            origin,
            vendorMarksReadOnly,
            displayFromVendor,
            note);
    }
}
