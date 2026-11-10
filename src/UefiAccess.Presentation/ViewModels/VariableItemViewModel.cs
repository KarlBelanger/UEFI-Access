// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Text;
using UefiAccess.Core.Decoders;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Variables;
using UefiAccess.Presentation.Infrastructure;
using UefiAccess.Presentation.Resources;
using UefiAccess.Presentation.Services;

namespace UefiAccess.Presentation.ViewModels;

/// <summary>
/// One row of the variable list: the variable, its namespace label, its decoding (when a decoder
/// accepted it), its writability (Core's <see cref="WritabilityPolicy"/> verdict, set once when the
/// list loads), and the display strings the columns bind to. <see cref="AccessibleName"/> is the
/// one sentence a screen reader speaks for the row; <see cref="ItemStatus"/> is the row's item
/// status. Immutable after construction; every string is computed once so binding and filtering
/// never re-decode.
/// </summary>
public sealed class VariableItemViewModel : ObservableObject
{
    /// <summary>The longest <see cref="AccessibleName"/>; longer sentences end with an ellipsis.</summary>
    public const int AccessibleNameMaxLength = 200;

    /// <summary>The separator between the parts of <see cref="AccessibleName"/>.</summary>
    public const string AccessibleNameSeparator = ", ";

    /// <summary>
    /// The words the BootOrder and BootNext rows' sentences carry right after the name on a machine
    /// that is not the M3 test unit: Core's item status
    /// <see cref="WritabilitySentences.ItemStatusElsewhere"/> as the sentence continues it (first
    /// letter in lower case), as <see cref="Strings.RowStatusOrderOfEntries"/> and
    /// <see cref="Strings.RowStatusOneTimeBoot"/> are for the two changeable rows. Since 7 October
    /// 2026 they begin with what holds for this machine.
    /// </summary>
    public const string RowStatusElsewhere = "not changeable on this machine";

    /// <summary>
    /// Creates the row. Without a verdict the row is judged as a live variable on a machine that
    /// is not the M3 test unit, with no other variable known: never changeable.
    /// </summary>
    /// <param name="variable">The variable.</param>
    /// <param name="ns">Its namespace label.</param>
    /// <param name="decoded">Its decoding, or null when no decoder accepted it.</param>
    /// <param name="writability">Its writability verdict from <see cref="WritabilityPolicy.Describe"/>, or null for the verdict without context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="variable"/> or <paramref name="ns"/> is null.</exception>
    public VariableItemViewModel(FirmwareVariable variable, NamespaceInfo ns, DecodedVariable? decoded, WritabilityVerdict? writability = null)
    {
        ArgumentNullException.ThrowIfNull(variable);
        ArgumentNullException.ThrowIfNull(ns);
        Variable = variable;
        Namespace = ns;
        Decoded = decoded;
        Writability = writability ?? WritabilityPolicy.Describe(variable, null, offline: false, machineConfirmed: false);

        Name = variable.Name ?? string.Empty;
        GuidText = variable.VendorGuid.ToString("D");
        Flags = VariableAttributeFlags.ToShortFlags(variable.Attributes);
        Size = variable.Size;
        SizeText = FormatSize(Size);
        Kind = decoded?.Kind is { Length: > 0 } kind ? kind : Strings.RawKind;
        Summary = OneLine(decoded?.Summary);
        AccessibleName = BuildAccessibleName();
    }

    /// <summary>The variable.</summary>
    public FirmwareVariable Variable { get; }

    /// <summary>The namespace description.</summary>
    public NamespaceInfo Namespace { get; }

    /// <summary>The decoding, or null when no decoder accepted the variable.</summary>
    public DecodedVariable? Decoded { get; }

    /// <summary>The stable key "guid:name" (lowercase GUID).</summary>
    public string Key => Variable.Key;

    /// <summary>The variable name.</summary>
    public string Name { get; }

    /// <summary>The vendor GUID.</summary>
    public Guid Guid => Variable.VendorGuid;

    /// <summary>The vendor GUID in lowercase "D" format.</summary>
    public string GuidText { get; }

    /// <summary>The namespace label, for example "EFI global variable".</summary>
    public string NamespaceLabel => Namespace.Label;

    /// <summary>The short attribute flags, for example "NV BS RT"; empty when no attribute is set.</summary>
    public string Flags { get; }

    /// <summary>The data size in bytes.</summary>
    public int Size { get; }

    /// <summary>The size as text: "1 byte" or "N bytes".</summary>
    public string SizeText { get; }

    /// <summary>The decoder kind ("BootOrder", "LoadOption", "SecureBootState", "OsIndications"), or "Raw" when no decoder accepted the variable.</summary>
    public string Kind { get; }

    /// <summary>The decoder's one-line summary, or an empty string. Line breaks are flattened to spaces.</summary>
    public string Summary { get; }

    /// <summary>
    /// The sentence a screen reader speaks for the row: "&lt;name&gt;, &lt;namespace label&gt;,
    /// &lt;flags&gt;, &lt;size&gt;" followed by ", &lt;summary&gt;" when a decoder produced one, for
    /// example "BootOrder, EFI global variable, NV BS RT, 4 bytes, BootOrder: Boot0001 (Windows Boot
    /// Manager), Boot0002 (UEFI: SanDisk Cruzer)". While BootOrder or BootNext has an item status,
    /// its words follow the name, because what a screen reader speaks for an item status is not
    /// known: "BootOrder, changeable: order of entries, EFI global
    /// variable, ..." while it is changeable, and "BootOrder, not changeable on this machine,
    /// EFI global variable, ..." on a machine that is not the M3 test unit (the WrongMachine
    /// verdict). Every other row is unchanged. An empty part (a variable without
    /// attributes) is left out rather than spoken as a pause. The sentence is capped at
    /// <see cref="AccessibleNameMaxLength"/> characters and then ends with an ellipsis; the detail
    /// pane carries the rest.
    /// </summary>
    public string AccessibleName { get; }

    /// <summary>The writability verdict: the Writability line of the detail pane and the item status come from it.</summary>
    public WritabilityVerdict Writability { get; }

    /// <summary>
    /// The row's item status (AutomationProperties.ItemStatus): "Changeable: order of entries" on
    /// BootOrder and "Changeable: one-time boot" on BootNext while they are changeable, "Not
    /// changeable on this machine" on both on a machine that is not the M3 test unit, empty on every
    /// other row, live or baseline.
    /// </summary>
    public string ItemStatus => Writability.ItemStatus;

    /// <summary>Renders a byte count as "1 byte" or "N bytes" (invariant culture).</summary>
    /// <param name="size">The size in bytes.</param>
    /// <returns>The text.</returns>
    public static string FormatSize(int size)
    {
        return size == 1 ? "1 byte" : size.ToString(CultureInfo.InvariantCulture) + " bytes";
    }

    /// <inheritdoc />
    public override string ToString() => Key;

    private static string OneLine(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
    }

    private string BuildAccessibleName()
    {
        StringBuilder sentence = new();
        AppendPart(sentence, Name);
        AppendPart(sentence, RowStatusWords(Writability));
        AppendPart(sentence, NamespaceLabel);
        AppendPart(sentence, Flags);
        AppendPart(sentence, SizeText);
        AppendPart(sentence, Summary);
        return DetailFieldViewModel.Truncate(sentence.ToString(), AccessibleNameMaxLength);
    }

    /// <summary>
    /// The item status as the row sentence continues it after the name: for the two changeable
    /// rows, and for BootOrder and BootNext on another machine (the verdict carries
    /// <see cref="WritabilitySentences.ItemStatusElsewhere"/>; a Boot#### under the same verdict
    /// carries none); empty for every other row.
    /// </summary>
    private static string RowStatusWords(WritabilityVerdict writability)
    {
        return writability.Verdict switch
        {
            Core.Safety.Writability.ReorderOnly => Strings.RowStatusOrderOfEntries,
            Core.Safety.Writability.OneTimeBoot => Strings.RowStatusOneTimeBoot,
            Core.Safety.Writability.WrongMachine
                when string.Equals(writability.ItemStatus, WritabilitySentences.ItemStatusElsewhere, StringComparison.Ordinal) => RowStatusElsewhere,
            _ => string.Empty,
        };
    }

    private static void AppendPart(StringBuilder sentence, string part)
    {
        if (string.IsNullOrWhiteSpace(part))
        {
            return;
        }

        if (sentence.Length > 0)
        {
            sentence.Append(AccessibleNameSeparator);
        }

        sentence.Append(part);
    }
}
