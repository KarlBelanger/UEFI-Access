// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using UefiAccess.Core.Decoders;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Variables;
using UefiAccess.Presentation.Infrastructure;
using UefiAccess.Presentation.Resources;

namespace UefiAccess.Presentation.ViewModels;

/// <summary>
/// The detail pane for the selected row: a heading, the sections Identity, Attributes, Size,
/// Decoded (only when a decoder accepted the variable) and Raw, the hex dump, the Writability line
/// (<see cref="ReadOnlyStatus"/>, the row's verdict from Core's <see cref="WritabilityPolicy"/>)
/// and the Change group (<see cref="Change"/>). <see cref="Show"/> updates the pane synchronously
/// from the row's <see cref="VariableItemViewModel.Variable"/> and
/// <see cref="VariableItemViewModel.Decoded"/>, and tells the Change group which variable it is for.
/// The sections are updated in place: <see cref="Show"/> builds the sections a fresh pane would
/// show, then keeps every section and field the shown row and the new row both have in the same
/// order (the same objects, so a view keeps their controls and their automation elements) and
/// changes only their values, and inserts or removes only the other sections and fields (finding
/// A1-6: measured on the OptiPlex 7020, removing and re-creating every section raised a burst of
/// about 150 UI Automation events that delayed the next row's focus event). Nothing here reads
/// firmware. Every field is a <see cref="DetailFieldViewModel"/> named by its label alone (the hex
/// dump by <see cref="Strings.HexDumpName"/>): the read-only box exposes the value through the
/// Value pattern, so a screen reader hears the label once and the value once.
/// Only a value that really spans several lines (the hex dump, a decoded value with a line break)
/// is multi-line and read line by line; every other value, however long, is single-line and wraps
/// inside its box. When keyboard focus arrives on a single-line value from another element, the
/// window selects it whole, so NVDA reads all of it as "selected" and the value
/// (<see cref="DetailFieldViewModel.SelectsAllOnKeyboardFocus"/>), unless it has
/// <see cref="LongValueLength"/> characters or more: NVDA speaks so long a selection only as
/// "selected N characters", so such a value is not selected, the caret goes to its start, its help
/// text says how long it is (<see cref="HelpTextFor"/>), and it is read line by line
/// (<see cref="SelectionOnKeyboardArrival"/>). The field labels and the attribute meanings are
/// public constants on this class so the XAML and the tests can refer to them by name.
/// </summary>
public sealed class VariableDetailViewModel : ObservableObject
{
    // ---- Field labels ---------------------------------------------------------------------

    /// <summary>Identity field label: the variable name.</summary>
    public const string NameLabel = "Name";

    /// <summary>Identity field label: the vendor GUID.</summary>
    public const string GuidLabel = "GUID";

    /// <summary>Identity field label: the namespace label.</summary>
    public const string NamespaceLabel = "Namespace";

    /// <summary>Identity field label: the stable key "guid:name".</summary>
    public const string KeyLabel = "Key";

    /// <summary>Attributes field label: the short flag tokens (NV BS RT ...).</summary>
    public const string ShortFlagsLabel = "Short flags";

    /// <summary>Attributes field label: bits outside the eight defined attributes.</summary>
    public const string UndefinedBitsLabel = "Undefined bits";

    /// <summary>Size field label: the data size as text ("6 bytes"), the same words as the row sentence and the CLI's Size row.</summary>
    public const string SizeLabel = "Size";

    /// <summary>Decoded field label: the decoder's one-line summary.</summary>
    public const string SummaryLabel = "Summary";

    /// <summary>Decoded field label: the decoder's error text when the data was malformed.</summary>
    public const string ErrorLabel = "Error";

    /// <summary>Raw field label: the hex dump.</summary>
    public const string HexDumpLabel = "Hex dump";

    /// <summary>Raw field label: the leading printable characters of the data.</summary>
    public const string FirstBytesAsTextLabel = "First bytes as text";

    // ---- Placeholder values ---------------------------------------------------------------

    /// <summary>Hex dump value for a zero-length variable.</summary>
    public const string EmptyDataText = "(empty)";

    /// <summary>Short flags value when no attribute bit is set.</summary>
    public const string NoFlagsText = "(none)";

    // ---- Attribute names and meanings (UEFI 2.10, section 8.2) ------------------------------

    /// <summary>Full UEFI name of the NV attribute.</summary>
    public const string NonVolatileName = "EFI_VARIABLE_NON_VOLATILE";

    /// <summary>Meaning of the NV attribute.</summary>
    public const string NonVolatileMeaning = "Non-volatile: survives reboot";

    /// <summary>Full UEFI name of the BS attribute.</summary>
    public const string BootServiceAccessName = "EFI_VARIABLE_BOOTSERVICE_ACCESS";

    /// <summary>Meaning of the BS attribute.</summary>
    public const string BootServiceAccessMeaning = "Boot-service access: readable before the OS starts";

    /// <summary>Full UEFI name of the RT attribute.</summary>
    public const string RuntimeAccessName = "EFI_VARIABLE_RUNTIME_ACCESS";

    /// <summary>Meaning of the RT attribute.</summary>
    public const string RuntimeAccessMeaning = "Runtime access: readable from Windows";

    /// <summary>Full UEFI name of the HR attribute.</summary>
    public const string HardwareErrorRecordName = "EFI_VARIABLE_HARDWARE_ERROR_RECORD";

    /// <summary>Meaning of the HR attribute.</summary>
    public const string HardwareErrorRecordMeaning = "Hardware error record: holds an error report the firmware saved";

    /// <summary>Full UEFI name of the AW attribute.</summary>
    public const string AuthenticatedAccessName = "EFI_VARIABLE_AUTHENTICATED_WRITE_ACCESS";

    /// <summary>Meaning of the AW attribute.</summary>
    public const string AuthenticatedAccessMeaning = "Authenticated write: only a signed update can change it";

    /// <summary>Full UEFI name of the TA attribute.</summary>
    public const string TimeBasedAuthenticatedAccessName = "EFI_VARIABLE_TIME_BASED_AUTHENTICATED_WRITE_ACCESS";

    /// <summary>Meaning of the TA attribute.</summary>
    public const string TimeBasedAuthenticatedAccessMeaning = "Time-based authenticated write: signed and timestamped updates only";

    /// <summary>Full UEFI name of the AP attribute.</summary>
    public const string AppendName = "EFI_VARIABLE_APPEND_WRITE";

    /// <summary>Meaning of the AP attribute.</summary>
    public const string AppendMeaning = "Append: an update adds to the existing data instead of replacing it";

    /// <summary>Full UEFI name of the EA attribute.</summary>
    public const string EnhancedAuthenticatedAccessName = "EFI_VARIABLE_ENHANCED_AUTHENTICATED_ACCESS";

    /// <summary>Meaning of the EA attribute.</summary>
    public const string EnhancedAuthenticatedAccessMeaning = "Enhanced authenticated access: changes need a certificate-based signature (UEFI 2.5 and later)";

    // ---- Thresholds -----------------------------------------------------------------------

    /// <summary>The fewest leading printable characters that make the "First bytes as text" field appear.</summary>
    public const int MinimumPrintableCharacters = 4;

    /// <summary>
    /// The length from which a single-line value is a long value: it is not selected when keyboard
    /// focus arrives (<see cref="SelectionOnKeyboardArrival"/>) and its help text gives its length
    /// (<see cref="HelpTextFor"/>). NVDA speaks a selection shorter than 512 characters as
    /// "selected" and the text, but a selection of 512 characters or more only as "selected N
    /// characters" (speech.py, speakSelectionMessage), which would hide the value itself; a long
    /// value such as a db or dbx "Signature types" list with one entry per signature list, a
    /// long BootOrder summary or a long "First bytes as text" is therefore read from its first
    /// line on screen, line by line.
    /// </summary>
    public const int LongValueLength = 512;

    /// <summary>Mask of the eight attribute bits the UEFI specification defines.</summary>
    private const uint DefinedAttributeMask = 0xFF;

    private static readonly char[] LineBreaks = { '\r', '\n' };

    /// <summary>The eight defined attributes in ascending bit order with their full UEFI name and meaning.</summary>
    private static readonly (VariableAttributes Flag, string Name, string Meaning)[] AttributeTable =
    {
        (VariableAttributes.NonVolatile, NonVolatileName, NonVolatileMeaning),
        (VariableAttributes.BootServiceAccess, BootServiceAccessName, BootServiceAccessMeaning),
        (VariableAttributes.RuntimeAccess, RuntimeAccessName, RuntimeAccessMeaning),
        (VariableAttributes.HardwareErrorRecord, HardwareErrorRecordName, HardwareErrorRecordMeaning),
        (VariableAttributes.AuthenticatedWriteAccess, AuthenticatedAccessName, AuthenticatedAccessMeaning),
        (VariableAttributes.TimeBasedAuthenticatedWriteAccess, TimeBasedAuthenticatedAccessName, TimeBasedAuthenticatedAccessMeaning),
        (VariableAttributes.AppendWrite, AppendName, AppendMeaning),
        (VariableAttributes.EnhancedAuthenticatedAccess, EnhancedAuthenticatedAccessName, EnhancedAuthenticatedAccessMeaning),
    };

    /// <summary>The separator between the Writability sentence and the firmware-setup instruction that follows it.</summary>
    private const string SentenceSpace = " ";

    private readonly ObservableCollection<DetailSectionViewModel> _sections = new();
    private VariableItemViewModel? _item;
    private string _heading = Strings.NoSelectionHeading;
    private string _hexDump = string.Empty;
    private string? _copyValueText;
    private string _readOnlyStatus = Strings.NoSelectionHeading;

    /// <summary>Creates an empty detail pane whose Change group never shows (no write pipeline behind it).</summary>
    public VariableDetailViewModel()
        : this(new ChangeViewModel())
    {
    }

    /// <summary>Creates an empty detail pane over the shell's Change group.</summary>
    /// <param name="change">The Change group the pane shows after the Writability line.</param>
    /// <exception cref="ArgumentNullException"><paramref name="change"/> is null.</exception>
    public VariableDetailViewModel(ChangeViewModel change)
    {
        ArgumentNullException.ThrowIfNull(change);
        Change = change;
        Sections = new ReadOnlyObservableCollection<DetailSectionViewModel>(_sections);
    }

    /// <summary>The Change group for the row shown (tab stops 14 to 20; collapsed unless the live source shows BootOrder, BootNext or an active category-Boot Boot####).</summary>
    public ChangeViewModel Change { get; }

    /// <summary>The row shown, or null.</summary>
    public VariableItemViewModel? Item
    {
        get => _item;
        private set
        {
            if (SetProperty(ref _item, value))
            {
                OnPropertyChanged(nameof(HasSelection));
            }
        }
    }

    /// <summary>True when a row is shown; false in the empty state.</summary>
    public bool HasSelection => _item is not null;

    /// <summary>The pane heading (AutomationProperties.HeadingLevel Level1): the variable name, or "No variable selected".</summary>
    public string Heading
    {
        get => _heading;
        private set => SetProperty(ref _heading, value);
    }

    /// <summary>
    /// The sections in display order: Identity, Attributes, Size, Decoded (only when a decoder
    /// accepted the variable) and Raw; empty when nothing is selected. One collection for the life
    /// of the pane, never replaced, cleared or reset: <see cref="Show"/> keeps every section the
    /// old and the new row both have and inserts or removes, one notification each, only the
    /// sections that differ (in practice Decoded, between a decoded and a raw variable). The
    /// property itself never changes, so it raises no property change.
    /// </summary>
    public ReadOnlyObservableCollection<DetailSectionViewModel> Sections { get; }

    /// <summary>
    /// The raw data as a hex dump (16 bytes per line, offsets, ASCII column), identical to the
    /// value of the "Hex dump" field in the Raw section; <see cref="EmptyDataText"/> for a
    /// zero-length variable; empty when nothing is selected.
    /// </summary>
    public string HexDump
    {
        get => _hexDump;
        private set => SetProperty(ref _hexDump, value);
    }

    /// <summary>
    /// The Writability line (tab 13, named <see cref="Strings.ReadOnlyStatusName"/>): the row's
    /// verdict sentence from Core's <see cref="WritabilityPolicy"/> (live or baseline, the machine
    /// confirmed or not), followed by <see cref="Strings.FirmwareSetupHint"/>
    /// for SecureBoot, SetupMode, PK, KEK, db and dbx; <see cref="Strings.NoSelectionHeading"/> when
    /// nothing is selected.
    /// </summary>
    public string ReadOnlyStatus
    {
        get => _readOnlyStatus;
        private set => SetProperty(ref _readOnlyStatus, value);
    }

    /// <summary>The Writability line of a row: its verdict's sentence, and the firmware-setup instruction when the verdict asks for it.</summary>
    /// <param name="item">The row.</param>
    /// <returns>The line.</returns>
    public static string WritabilityLine(VariableItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        WritabilityVerdict verdict = item.Writability;
        return verdict.FirmwareSetupHintApplies ? verdict.Sentence + SentenceSpace + Strings.FirmwareSetupHint : verdict.Sentence;
    }

    /// <summary>
    /// What Copy value copies: the value of the detail field that has focus (the view assigns it
    /// as focus moves), or, right after <see cref="Show"/>, the decoder's summary (the hex dump
    /// when no decoder accepted the variable); null when nothing is selected.
    /// </summary>
    public string? CopyValueText
    {
        get => _copyValueText;
        set => SetProperty(ref _copyValueText, value);
    }

    /// <summary>
    /// Shows a row: the heading, the sections, the hex dump, the Writability line, the Change group
    /// and the default copy text; or empties the pane for null (the Writability line then reads
    /// <see cref="Strings.NoSelectionHeading"/> and the Change group is told no variable is shown).
    /// The sections are updated in place (see the class summary): the sections a fresh pane would
    /// show for the row are built first, with every value, and only then is the live pane changed,
    /// by keeping each section and field the shown row and the new row both have in the same order
    /// (<see cref="DetailPaneReconciler"/>) and setting its value, and by inserting or removing,
    /// one at a time, the sections and fields that differ (for null, every section is removed,
    /// from the last to the first). The pane then reads exactly as a pane that showed only this
    /// row. Showing the row already shown leaves the sections as they are; the copy text goes back
    /// to its default. <see cref="Item"/> is assigned last. Never throws.
    /// </summary>
    /// <param name="item">The row, or null.</param>
    /// <returns>How many sections were kept, added and removed and how many fields were kept, updated, added and removed; numbers only.</returns>
    public DetailPaneUpdate Show(VariableItemViewModel? item)
    {
        // Everything is computed before the pane changes, so it is never left half updated.
        string hexDump = string.Empty;
        IReadOnlyList<DetailSectionViewModel> wanted = Array.Empty<DetailSectionViewModel>();
        if (item is not null)
        {
            byte[] data = item.Variable.Data ?? Array.Empty<byte>();
            hexDump = data.Length == 0 ? EmptyDataText : Core.Text.HexDump.Format(data);
            wanted = BuildSections(item, data, hexDump);
        }

        DetailPaneUpdate update = UpdateSections(wanted);

        // Item is assigned last so that a listener reacting to its change (for example to move
        // focus into the first field) already sees the new sections and values, the Writability
        // line and the Change group.
        HexDump = hexDump;
        Heading = item is null ? Strings.NoSelectionHeading : item.Name;
        CopyValueText = item is null ? null : (string.IsNullOrEmpty(item.Summary) ? hexDump : item.Summary);
        ReadOnlyStatus = item is null ? Strings.NoSelectionHeading : WritabilityLine(item);
        Change.Show(item);
        Item = item;
        return update;
    }

    /// <summary>
    /// Builds the fields of the Attributes section for a set of attributes: one field per set
    /// defined flag (full UEFI name, one-clause meaning) in ascending bit order, the short flag
    /// text, and the undefined bits when any are set.
    /// </summary>
    /// <param name="attributes">The attributes.</param>
    /// <returns>The fields in display order.</returns>
    public static IReadOnlyList<DetailFieldViewModel> AttributeFields(VariableAttributes attributes)
    {
        uint value = (uint)attributes;
        List<DetailFieldViewModel> fields = new(AttributeTable.Length + 2);
        foreach ((VariableAttributes flag, string name, string meaning) in AttributeTable)
        {
            if ((value & (uint)flag) != 0)
            {
                fields.Add(new DetailFieldViewModel(name, meaning));
            }
        }

        string shortFlags = VariableAttributeFlags.ToShortFlags(attributes);
        fields.Add(new DetailFieldViewModel(ShortFlagsLabel, shortFlags.Length == 0 ? NoFlagsText : shortFlags));

        uint undefined = value & ~DefinedAttributeMask;
        if (undefined != 0)
        {
            fields.Add(new DetailFieldViewModel(UndefinedBitsLabel, "0x" + undefined.ToString("X8", CultureInfo.InvariantCulture)));
        }

        return fields;
    }

    /// <summary>
    /// Extracts the leading printable text of a byte sequence the way the load-option decoder
    /// reads optional data: the longest prefix of printable ASCII (0x20 to 0x7E) or of printable
    /// UTF-16LE characters, whichever is longer, when it has at least
    /// <see cref="MinimumPrintableCharacters"/> characters; otherwise null.
    /// </summary>
    /// <param name="data">The bytes.</param>
    /// <returns>The text, or null when the data does not start with enough printable characters.</returns>
    public static string? LeadingPrintableText(ReadOnlySpan<byte> data)
    {
        int ascii = 0;
        while (ascii < data.Length && IsPrintableAscii(data[ascii]))
        {
            ascii++;
        }

        int utf16 = 0;
        while ((utf16 * 2) + 1 < data.Length && data[(utf16 * 2) + 1] == 0 && IsPrintableAscii(data[utf16 * 2]))
        {
            utf16++;
        }

        if (ascii < MinimumPrintableCharacters && utf16 < MinimumPrintableCharacters)
        {
            return null;
        }

        return utf16 > ascii
            ? Encoding.Unicode.GetString(data[..(utf16 * 2)])
            : Encoding.ASCII.GetString(data[..ascii]);
    }

    /// <summary>
    /// True for a long value: a single-line value of <see cref="LongValueLength"/> characters or
    /// more, which is not selected when keyboard focus arrives and is read line by line.
    /// </summary>
    /// <param name="value">The value; null counts as empty.</param>
    /// <param name="isMultiline">True for a multi-line box (the hex dump, a value with a line break), which is never a long value: it is read line by line anyway.</param>
    /// <returns>True for a long value.</returns>
    public static bool IsLongValue(string? value, bool isMultiline)
    {
        return !isMultiline && value is not null && value.Length >= LongValueLength;
    }

    /// <summary>
    /// The help text of a field's value box: <see cref="Strings.DetailFieldHelp"/>, or, for a long
    /// value (<see cref="IsLongValue"/>), <see cref="Strings.LongDetailFieldHelpFormat"/> with its
    /// length. A screen reader reads the help text before the value, so for a long value, which is
    /// heard from its first line on screen, it says that the value goes on and how to hear the rest.
    /// </summary>
    /// <param name="field">The field; null gives the ordinary help text.</param>
    /// <returns>The help text.</returns>
    public static string HelpTextFor(DetailFieldViewModel? field)
    {
        return field is not null && IsLongValue(field.Value, field.IsMultiline)
            ? string.Format(CultureInfo.InvariantCulture, Strings.LongDetailFieldHelpFormat, field.Value.Length)
            : Strings.DetailFieldHelp;
    }

    /// <summary>
    /// The selection the window makes in a read-only value box of the pane (a field, or the
    /// Writability line) when keyboard focus reaches it, or null to leave the box as it is
    /// (docs/Keyboard-Map.md section 7):
    /// <list type="bullet">
    /// <item><description>A click (a mouse button is down while focus moves): null; the click places the caret as usual.</description></item>
    /// <item><description>A multi-line box (the hex dump, a value with a line break): null; it is read line by line from its caret.</description></item>
    /// <item><description>
    /// Focus arriving from another element (Tab, Shift+Tab, F6, Enter or Space on a row, a screen
    /// reader's "move focus"): the whole value, (0, <paramref name="textLength"/>), which NVDA reads
    /// as "selected" and the value; for a long value (<see cref="LongValueLength"/> characters or
    /// more) the caret at the start instead, (0, 0), so NVDA reads its first line on screen and not
    /// "selected N characters".
    /// </description></item>
    /// <item><description>
    /// Focus returning to the box it left for the box's own context menu or for another window
    /// (Escape or a command in the context menu, Alt+Tab back, a dialog closed): when the box's
    /// selection at the moment focus left it was the user's own, anything but what arrival makes
    /// (part of the value selected, or the caret moved), null, so the user's selection or reading
    /// position is kept and a context menu's Copy copies the part the user selected; otherwise the
    /// arrival selection again, which changes nothing.
    /// </description></item>
    /// </list>
    /// </summary>
    /// <param name="isMultiline">True for a multi-line box.</param>
    /// <param name="textLength">The length of the box's text.</param>
    /// <param name="clicked">True when a mouse button is down while focus moves (a click).</param>
    /// <param name="returning">True when focus comes back to the box it left for its own context menu or for another window.</param>
    /// <param name="selectionStartWhenLeft">On a return, the start of the box's selection when focus left it.</param>
    /// <param name="selectionLengthWhenLeft">On a return, the length of the box's selection when focus left it.</param>
    /// <returns>The selection to make, always from the start of the value (the view selects from its end back to its start, so the caret is at the start); or null to leave the box as it is.</returns>
    public static (int Start, int Length)? SelectionOnKeyboardArrival(bool isMultiline, int textLength, bool clicked, bool returning, int selectionStartWhenLeft, int selectionLengthWhenLeft)
    {
        if (clicked || isMultiline)
        {
            return null;
        }

        int length = Math.Max(0, textLength);
        (int Start, int Length) arrival = length < LongValueLength ? (0, length) : (0, 0);
        if (returning && (selectionStartWhenLeft, selectionLengthWhenLeft) != arrival)
        {
            return null;
        }

        return arrival;
    }

    /// <summary>
    /// The sections a fresh pane shows for a row, with new objects: Identity, Attributes, Size,
    /// Decoded (when a decoder accepted the variable) and Raw. The in-place update and a fresh
    /// pane come from this one description.
    /// </summary>
    private static IReadOnlyList<DetailSectionViewModel> BuildSections(VariableItemViewModel item, byte[] data, string hexDump)
    {
        List<DetailSectionViewModel> sections = new(5)
        {
            new DetailSectionViewModel(Strings.SectionIdentity, IdentityFields(item)),
            new DetailSectionViewModel(Strings.SectionAttributes, AttributeFields(item.Variable.Attributes)),
            new DetailSectionViewModel(Strings.SectionSize, SizeFields(item)),
        };
        if (item.Decoded is not null)
        {
            sections.Add(new DetailSectionViewModel(Strings.SectionDecoded, DecodedFields(item.Decoded)));
        }

        sections.Add(new DetailSectionViewModel(Strings.SectionRaw, RawFields(data, hexDump)));
        return sections;
    }

    /// <summary>
    /// Makes the live sections those of <paramref name="wanted"/>, in place
    /// (<see cref="DetailPaneReconciler"/>): a section is identified by its title; a kept section
    /// updates its fields (<see cref="DetailSectionViewModel.UpdateFields"/>); an added section
    /// comes in with its fields already in it, and its fields count as added, as the fields of a
    /// removed section count as removed.
    /// </summary>
    private DetailPaneUpdate UpdateSections(IReadOnlyList<DetailSectionViewModel> wanted)
    {
        int fieldsKept = 0;
        int fieldsUpdated = 0;
        int fieldsAdded = 0;
        int fieldsRemoved = 0;
        DetailPaneReconciler.Counts sections = DetailPaneReconciler.Apply(
            _sections,
            wanted,
            static section => section.Title,
            (existing, next) =>
            {
                DetailPaneReconciler.Counts fields = existing.UpdateFields(next.Fields);
                fieldsKept += fields.Kept;
                fieldsUpdated += fields.Updated;
                fieldsAdded += fields.Added;
                fieldsRemoved += fields.Removed;
                return fields.Updated != 0 || fields.Added != 0 || fields.Removed != 0;
            },
            removed => fieldsRemoved += removed.Fields.Count,
            added => fieldsAdded += added.Fields.Count);
        return new DetailPaneUpdate(sections.Kept, sections.Added, sections.Removed, fieldsKept, fieldsUpdated, fieldsAdded, fieldsRemoved);
    }

    private static IReadOnlyList<DetailFieldViewModel> IdentityFields(VariableItemViewModel item)
    {
        return new[]
        {
            new DetailFieldViewModel(NameLabel, item.Name),
            new DetailFieldViewModel(GuidLabel, item.GuidText),
            new DetailFieldViewModel(NamespaceLabel, item.NamespaceLabel),
            new DetailFieldViewModel(KeyLabel, item.Key),
        };
    }

    private static IReadOnlyList<DetailFieldViewModel> SizeFields(VariableItemViewModel item)
    {
        return new[]
        {
            new DetailFieldViewModel(SizeLabel, item.SizeText),
        };
    }

    private static IReadOnlyList<DetailFieldViewModel> DecodedFields(DecodedVariable decoded)
    {
        IReadOnlyList<DecodedField> source = decoded.Fields ?? Array.Empty<DecodedField>();
        List<DetailFieldViewModel> fields = new(source.Count + 2)
        {
            new DetailFieldViewModel(SummaryLabel, decoded.Summary ?? string.Empty, IsMultilineValue(decoded.Summary)),
        };

        bool hasErrorField = false;
        foreach (DecodedField field in source)
        {
            if (field is null)
            {
                continue;
            }

            string label = field.Name ?? string.Empty;
            hasErrorField |= string.Equals(label, ErrorLabel, StringComparison.Ordinal);
            fields.Add(new DetailFieldViewModel(label, field.Value ?? string.Empty, IsMultilineValue(field.Value)));
        }

        // Decoders add their own "Error" field; the registry's exception fallback does not, so
        // surface the error text there too rather than losing it.
        if (!hasErrorField && !string.IsNullOrEmpty(decoded.Error))
        {
            fields.Add(new DetailFieldViewModel(ErrorLabel, decoded.Error, IsMultilineValue(decoded.Error)));
        }

        return fields;
    }

    private static IReadOnlyList<DetailFieldViewModel> RawFields(byte[] data, string hexDump)
    {
        List<DetailFieldViewModel> fields = new(2)
        {
            new DetailFieldViewModel(HexDumpLabel, hexDump, isMultiline: true, accessibleName: Strings.HexDumpName),
        };

        string? text = LeadingPrintableText(data);
        if (text is not null)
        {
            fields.Add(new DetailFieldViewModel(FirstBytesAsTextLabel, text, IsMultilineValue(text)));
        }

        return fields;
    }

    /// <summary>
    /// True only for a value that contains a line break. Length alone never makes a value
    /// multi-line: a long single-line value wraps in its box like a short one (a low-vision reader
    /// never scrolls sideways), while a multi-line box keeps its lines unwrapped in a monospace
    /// font (the hex dump's columns). A single-line value shorter than
    /// <see cref="LongValueLength"/> is selected whole on arrival and heard whole; a longer one is
    /// heard from its first line on screen and read line by line (<see cref="SelectionOnKeyboardArrival"/>).
    /// </summary>
    private static bool IsMultilineValue(string? value)
    {
        return value is not null && value.IndexOfAny(LineBreaks) >= 0;
    }

    private static bool IsPrintableAscii(byte value) => value is >= 0x20 and <= 0x7E;
}
