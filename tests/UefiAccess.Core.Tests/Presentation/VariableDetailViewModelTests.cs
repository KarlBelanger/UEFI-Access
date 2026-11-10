// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

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
/// Tests for <see cref="VariableDetailViewModel"/>: the section order for a decoded and for a raw
/// variable, the Identity, Attributes (full UEFI names and meanings), Size, Decoded and Raw
/// fields, the hex dump format (offset column, 16 bytes per line, ASCII column), the "First
/// bytes as text" rule, the empty state after Show(null), the read-only status line, the
/// accessible names of the fields (every field is named by its label alone, the hex box by "Raw
/// data, hex dump"; no value ever reaches a name), which values are multi-line (only those with a
/// line break, never a value for its length alone), the surrogate-safe truncation helper, and the
/// change notifications. Every variable is a synthetic buffer; nothing reads firmware.
/// </summary>
public sealed class VariableDetailViewModelTests
{
    private const VariableAttributes NvBsRt = VariableAttributes.NonVolatile | VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess;

    private static readonly Guid Global = KnownGuids.EfiGlobalVariable;
    private static readonly Guid VendorGuid = new("0A1B2C3D-4E5F-4A6B-8C7D-9E8F7A6B5C4D");

    // ---- Decoded variable (Windows Boot Manager Boot0001) --------------------------------

    [Fact]
    public void WindowsBootManager_ShowsAllFiveSectionsInOrder()
    {
        VariableDetailViewModel detail = new();
        detail.Show(BootEntry());

        Assert.Equal(
            new[] { Strings.SectionIdentity, Strings.SectionAttributes, Strings.SectionSize, Strings.SectionDecoded, Strings.SectionRaw },
            detail.Sections.Select(section => section.Title));
        Assert.Equal("Boot0001", detail.Heading);
        Assert.True(detail.HasSelection);
        Assert.NotNull(detail.Item);
        Assert.Equal("Boot0001", detail.Item.Name);
    }

    [Fact]
    public void WindowsBootManager_DecodedSectionCarriesSummaryThenEveryDecoderField()
    {
        VariableItemViewModel item = BootEntry();
        VariableDetailViewModel detail = new();
        detail.Show(item);

        DetailSectionViewModel decoded = Section(detail, Strings.SectionDecoded);
        Assert.NotNull(item.Decoded);
        Assert.Equal("LoadOption", item.Decoded.Kind);

        // The summary comes first, then the decoder's fields in their original order.
        Assert.Equal(VariableDetailViewModel.SummaryLabel, decoded.Fields[0].Label);
        Assert.Equal(item.Decoded.Summary, decoded.Fields[0].Value);
        Assert.Equal(
            item.Decoded.Fields.Select(field => field.Name),
            decoded.Fields.Skip(1).Select(field => field.Label));
        Assert.Equal(
            item.Decoded.Fields.Select(field => field.Value),
            decoded.Fields.Skip(1).Select(field => field.Value));

        Assert.Equal(LoadOptionFixtures.WindowsBootManagerDescription, Field(decoded, "Description").Value);
        Assert.Equal(LoadOptionFixtures.WindowsBootManagerPath, Field(decoded, "Device path").Value);
        Assert.Equal("15 bytes, text \"WINDOWS\"", Field(decoded, "Optional data").Value);
        Assert.Equal("0x00000001 (active, category Boot)", Field(decoded, "Attributes").Value);
        Assert.Equal("Boot", Field(decoded, "Category").Value);
        Assert.DoesNotContain(decoded.Fields, field => field.Label == VariableDetailViewModel.ErrorLabel);

        // Copy value defaults to the summary for a decoded variable.
        Assert.Equal(item.Summary, detail.CopyValueText);
        Assert.StartsWith("Boot0001: Windows Boot Manager; active, category Boot; HD(1,GPT,", item.Summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// Round-1 transcript: a wrapped value was read by NVDA only up to its first visual line
    /// (Boot0000's 102-character Device path was heard as "HD"), while the 157-character Summary,
    /// then shown unwrapped because it was over 120 characters, was read whole. So length no
    /// longer makes a value multi-line: every value without a line break is single-line (the view
    /// wraps it and selects it whole when keyboard focus arrives, so it is read whole), and this
    /// fixture's long summary and device path are single-line.
    /// </summary>
    [Fact]
    public void WindowsBootManager_LongSummaryAndDevicePath_AreSingleLine()
    {
        VariableItemViewModel item = BootEntry();
        VariableDetailViewModel detail = new();
        detail.Show(item);
        DetailSectionViewModel decoded = Section(detail, Strings.SectionDecoded);

        Assert.True(decoded.Fields[0].Value.Length > 120, "the fixture's summary is " + decoded.Fields[0].Value.Length + " characters");
        Assert.False(decoded.Fields[0].IsMultiline);
        DetailFieldViewModel devicePath = Field(decoded, "Device path");
        Assert.Equal(LoadOptionFixtures.WindowsBootManagerPath, devicePath.Value);
        Assert.DoesNotContain('\n', devicePath.Value);
        Assert.False(devicePath.IsMultiline);
        Assert.True(devicePath.SelectsAllOnKeyboardFocus);
        Assert.True(decoded.Fields[0].SelectsAllOnKeyboardFocus);
        Assert.False(Field(decoded, "Description").IsMultiline);
        Assert.False(Field(decoded, "Category").IsMultiline);

        // The Windows Boot Manager data starts with the UINT32 attributes, not with text.
        DetailSectionViewModel raw = Section(detail, Strings.SectionRaw);
        Assert.DoesNotContain(raw.Fields, field => field.Label == VariableDetailViewModel.FirstBytesAsTextLabel);
    }

    [Fact]
    public void DecodedField_IsMultilineOnlyWithALineBreak_NeverForItsLength()
    {
        DecodedVariable decoded = new(
            FirmwareVariable.MakeKey(VendorGuid, "X"),
            "Synthetic",
            "first line" + Environment.NewLine + "second line",
            new[]
            {
                new DecodedField("Long", new string('v', 1000)),
                new DecodedField("Lines", "first" + Environment.NewLine + "second"),
                new DecodedField("Feed", "first\nsecond"),
                new DecodedField("Return", "first\rsecond"),
                new DecodedField("Short", "short"),
                new DecodedField("Empty", string.Empty),
            },
            null,
            "one error" + Environment.NewLine + "and its detail");
        VariableDetailViewModel detail = new();
        detail.Show(Item(Variable("X", VendorGuid, NvBsRt, new byte[] { 1, 2, 3 }), decoded));

        DetailSectionViewModel section = Section(detail, Strings.SectionDecoded);
        Assert.True(Field(section, VariableDetailViewModel.SummaryLabel).IsMultiline);
        Assert.False(Field(section, "Long").IsMultiline);
        Assert.Equal(1000, Field(section, "Long").Value.Length);
        Assert.True(Field(section, "Lines").IsMultiline);
        Assert.True(Field(section, "Feed").IsMultiline);
        Assert.True(Field(section, "Return").IsMultiline);
        Assert.False(Field(section, "Short").IsMultiline);
        Assert.False(Field(section, "Empty").IsMultiline);
        Assert.True(Field(section, VariableDetailViewModel.ErrorLabel).IsMultiline);

        // A single-line value is selected whole on arrival (it wraps, and NVDA reads a selection
        // whole but otherwise only the visual line at the caret); a multi-line one is read line by line.
        Assert.All(section.Fields, field => Assert.Equal(!field.IsMultiline, field.SelectsAllOnKeyboardFocus));
        Assert.True(Field(section, "Long").SelectsAllOnKeyboardFocus);
        Assert.False(Field(section, "Lines").SelectsAllOnKeyboardFocus);

        // The only other multi-line box is the hex dump; "First bytes as text" is printable
        // characters only, so it never has a line break, however long it is.
        detail.Show(Item(Variable("Text", VendorGuid, NvBsRt, LoadOptionFixtures.AsciiZ(new string('t', 500))), null));
        DetailSectionViewModel raw = Section(detail, Strings.SectionRaw);
        Assert.True(Field(raw, VariableDetailViewModel.HexDumpLabel).IsMultiline);
        DetailFieldViewModel text = Field(raw, VariableDetailViewModel.FirstBytesAsTextLabel);
        Assert.Equal(500, text.Value.Length);
        Assert.False(text.IsMultiline);
        Assert.True(text.SelectsAllOnKeyboardFocus);
        Assert.False(Field(raw, VariableDetailViewModel.HexDumpLabel).SelectsAllOnKeyboardFocus);
    }

    [Fact]
    public void DecodedError_WithoutAnErrorField_IsAppended_AndNotDuplicatedWhenTheDecoderAddedOne()
    {
        // The registry's exception fallback yields empty fields plus an Error; the pane must show it.
        DecodedVariable fallback = new(FirmwareVariable.MakeKey(Global, "BootOrder"), "BootOrderDecoder", "decoding failed: boom", Array.Empty<DecodedField>(), null, "InvalidOperationException: boom");
        VariableDetailViewModel detail = new();
        detail.Show(Item(Variable("BootOrder", Global, NvBsRt, new byte[] { 1 }), fallback));
        DetailSectionViewModel section = Section(detail, Strings.SectionDecoded);
        Assert.Equal(new[] { VariableDetailViewModel.SummaryLabel, VariableDetailViewModel.ErrorLabel }, section.Fields.Select(field => field.Label));
        Assert.Equal("InvalidOperationException: boom", Field(section, VariableDetailViewModel.ErrorLabel).Value);

        // A real decoder already adds its own "Error" field for malformed data (truncated load option).
        VariableItemViewModel truncated = BootEntry("Boot0002", LoadOptionFixtures.Truncated());
        Assert.NotNull(truncated.Decoded);
        Assert.NotNull(truncated.Decoded.Error);
        detail.Show(truncated);
        DetailSectionViewModel malformed = Section(detail, Strings.SectionDecoded);
        Assert.Single(malformed.Fields, field => field.Label == VariableDetailViewModel.ErrorLabel);
        Assert.Equal(truncated.Decoded.Error, Field(malformed, VariableDetailViewModel.ErrorLabel).Value);
    }

    // ---- Raw variable (no decoder) --------------------------------------------------------

    [Fact]
    public void RawVariable_HasNoDecodedSection_AndCopiesTheHexDump()
    {
        VariableItemViewModel item = Item(Variable("VendorSetting", VendorGuid, NvBsRt, new byte[] { 0x01, 0x00, 0x02, 0x00, 0x03, 0x00 }), null);
        Assert.Equal(Strings.RawKind, item.Kind);
        Assert.Equal(string.Empty, item.Summary);

        VariableDetailViewModel detail = new();
        detail.Show(item);

        Assert.Equal(
            new[] { Strings.SectionIdentity, Strings.SectionAttributes, Strings.SectionSize, Strings.SectionRaw },
            detail.Sections.Select(section => section.Title));
        Assert.DoesNotContain(detail.Sections, section => section.Title == Strings.SectionDecoded);
        Assert.Equal("VendorSetting", detail.Heading);
        Assert.Equal(detail.HexDump, detail.CopyValueText);
        Assert.Equal("00000000  01 00 02 00 03 00                                 ......" + Environment.NewLine, detail.HexDump);
    }

    [Fact]
    public void IdentitySection_ListsNameLowercaseGuidNamespaceLabelAndKey()
    {
        VariableItemViewModel item = Item(Variable("BootOrder", Global, NvBsRt, new byte[] { 1, 0 }), null, new NamespaceInfo(Global, "EFI global variable", true));
        VariableDetailViewModel detail = new();
        detail.Show(item);

        DetailSectionViewModel identity = Section(detail, Strings.SectionIdentity);
        Assert.Equal(
            new[] { VariableDetailViewModel.NameLabel, VariableDetailViewModel.GuidLabel, VariableDetailViewModel.NamespaceLabel, VariableDetailViewModel.KeyLabel },
            identity.Fields.Select(field => field.Label));
        Assert.Equal("BootOrder", identity.Fields[0].Value);
        Assert.Equal("8be4df61-93ca-11d2-aa0d-00e098032b8c", identity.Fields[1].Value);
        Assert.Equal("EFI global variable", identity.Fields[2].Value);
        Assert.Equal("8be4df61-93ca-11d2-aa0d-00e098032b8c:BootOrder", identity.Fields[3].Value);
        // Named by the label alone: the read-only box speaks the value itself, once.
        Assert.Equal("GUID", identity.Fields[1].AccessibleName);
    }

    [Theory]
    [InlineData(6, "6 bytes")]
    [InlineData(1, "1 byte")]
    [InlineData(0, "0 bytes")]
    public void SizeSection_IsOneFieldSayingTheSizeInBytes(int size, string text)
    {
        VariableDetailViewModel detail = new();
        detail.Show(Item(Variable("X", VendorGuid, NvBsRt, new byte[size]), null));

        // One field, the same words as the row sentence and the CLI's Size row; no "Human" label.
        DetailSectionViewModel section = Section(detail, Strings.SectionSize);
        DetailFieldViewModel field = Assert.Single(section.Fields);
        Assert.Equal(VariableDetailViewModel.SizeLabel, field.Label);
        Assert.Equal(text, field.Value);
        Assert.Equal("Size", field.AccessibleName);
        Assert.False(field.IsMultiline);
    }

    // ---- Attributes -----------------------------------------------------------------------

    [Fact]
    public void Attributes_NvBsRt_GiveOneFieldPerFlagWithFullNameAndMeaning_ThenShortFlags()
    {
        VariableDetailViewModel detail = new();
        detail.Show(Item(Variable("BootOrder", Global, NvBsRt, new byte[] { 1, 0 }), null));

        DetailSectionViewModel section = Section(detail, Strings.SectionAttributes);
        Assert.Equal(4, section.Fields.Count);

        Assert.Equal("EFI_VARIABLE_NON_VOLATILE", section.Fields[0].Label);
        Assert.Equal("Non-volatile: survives reboot", section.Fields[0].Value);
        Assert.Equal("EFI_VARIABLE_BOOTSERVICE_ACCESS", section.Fields[1].Label);
        Assert.Equal("Boot-service access: readable before the OS starts", section.Fields[1].Value);
        Assert.Equal("EFI_VARIABLE_RUNTIME_ACCESS", section.Fields[2].Label);
        Assert.Equal("Runtime access: readable from Windows", section.Fields[2].Value);
        Assert.Equal(VariableDetailViewModel.ShortFlagsLabel, section.Fields[3].Label);
        Assert.Equal("NV BS RT", section.Fields[3].Value);
        Assert.Equal("Short flags", section.Fields[3].AccessibleName);
        Assert.All(section.Fields, field => Assert.False(field.IsMultiline));
    }

    [Fact]
    public void Attributes_EveryDefinedFlag_HasItsOwnNameAndMeaning_InBitOrder()
    {
        VariableAttributes all = (VariableAttributes)0xFF;
        IReadOnlyList<DetailFieldViewModel> fields = VariableDetailViewModel.AttributeFields(all);

        Assert.Equal(9, fields.Count);
        Assert.Equal(
            new[]
            {
                "EFI_VARIABLE_NON_VOLATILE",
                "EFI_VARIABLE_BOOTSERVICE_ACCESS",
                "EFI_VARIABLE_RUNTIME_ACCESS",
                "EFI_VARIABLE_HARDWARE_ERROR_RECORD",
                "EFI_VARIABLE_AUTHENTICATED_WRITE_ACCESS",
                "EFI_VARIABLE_TIME_BASED_AUTHENTICATED_WRITE_ACCESS",
                "EFI_VARIABLE_APPEND_WRITE",
                "EFI_VARIABLE_ENHANCED_AUTHENTICATED_ACCESS",
                VariableDetailViewModel.ShortFlagsLabel,
            },
            fields.Select(field => field.Label));
        Assert.Equal("Hardware error record: holds an error report the firmware saved", fields[3].Value);
        Assert.Equal("Authenticated write: only a signed update can change it", fields[4].Value);
        Assert.Equal("Time-based authenticated write: signed and timestamped updates only", fields[5].Value);
        Assert.Equal("Append: an update adds to the existing data instead of replacing it", fields[6].Value);
        Assert.Equal("Enhanced authenticated access: changes need a certificate-based signature (UEFI 2.5 and later)", fields[7].Value);
        Assert.Equal("NV BS RT HR AW TA AP EA", fields[8].Value);

        // Every meaning is one clause that says what the flag means ("short name: meaning"), not
        // just the flag's name in words: no sentence-ending period inside, non-empty, a colon
        // after the short name; each flag field is named by the full UEFI name, its label.
        foreach (DetailFieldViewModel field in fields.Take(8))
        {
            Assert.False(string.IsNullOrWhiteSpace(field.Value));
            Assert.DoesNotContain(". ", field.Value, StringComparison.Ordinal);
            Assert.Contains(": ", field.Value, StringComparison.Ordinal);
            Assert.True(field.Value.Length > field.Value.IndexOf(": ", StringComparison.Ordinal) + 2, field.Label + " has no meaning clause");
            Assert.Equal(field.Label, field.AccessibleName);
        }
    }

    [Fact]
    public void Attributes_TimeBasedAuthenticated_SecureBootDatabaseStyle()
    {
        VariableAttributes dbx = NvBsRt | VariableAttributes.TimeBasedAuthenticatedWriteAccess;
        VariableDetailViewModel detail = new();
        detail.Show(Item(Variable("dbx", KnownGuids.EfiImageSecurityDatabase, dbx, new byte[] { 0 }), null));

        DetailSectionViewModel section = Section(detail, Strings.SectionAttributes);
        Assert.Equal(
            new[] { "EFI_VARIABLE_NON_VOLATILE", "EFI_VARIABLE_BOOTSERVICE_ACCESS", "EFI_VARIABLE_RUNTIME_ACCESS", "EFI_VARIABLE_TIME_BASED_AUTHENTICATED_WRITE_ACCESS", VariableDetailViewModel.ShortFlagsLabel },
            section.Fields.Select(field => field.Label));
        Assert.Equal("NV BS RT TA", section.Fields[4].Value);
    }

    [Fact]
    public void Attributes_NoFlags_ShowsNonePlaceholder_UndefinedBitsGetTheirOwnField()
    {
        IReadOnlyList<DetailFieldViewModel> none = VariableDetailViewModel.AttributeFields(VariableAttributes.None);
        Assert.Single(none);
        Assert.Equal(VariableDetailViewModel.ShortFlagsLabel, none[0].Label);
        Assert.Equal(VariableDetailViewModel.NoFlagsText, none[0].Value);

        IReadOnlyList<DetailFieldViewModel> undefined = VariableDetailViewModel.AttributeFields(NvBsRt | (VariableAttributes)0x100);
        Assert.Equal(
            new[] { "EFI_VARIABLE_NON_VOLATILE", "EFI_VARIABLE_BOOTSERVICE_ACCESS", "EFI_VARIABLE_RUNTIME_ACCESS", VariableDetailViewModel.ShortFlagsLabel, VariableDetailViewModel.UndefinedBitsLabel },
            undefined.Select(field => field.Label));
        Assert.Equal("NV BS RT 0x00000100", undefined[3].Value);
        Assert.Equal("0x00000100", undefined[4].Value);
    }

    // ---- Hex dump and raw section ---------------------------------------------------------

    [Fact]
    public void HexDump_TwentyBytePayload_HasOffsetColumnSixteenBytesPerLineAndAsciiColumn()
    {
        byte[] sample = new byte[20];
        for (int i = 0; i < sample.Length; i++)
        {
            sample[i] = (byte)(0x41 + i); // "ABCDEFGHIJKLMNOPQRST"
        }

        sample[0] = 0x00;
        sample[19] = 0xFF;

        VariableDetailViewModel detail = new();
        detail.Show(Item(Variable("Sample", VendorGuid, NvBsRt, sample), null));

        string[] lines = detail.HexDump.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Equal("00000000  00 42 43 44 45 46 47 48  49 4A 4B 4C 4D 4E 4F 50  .BCDEFGHIJKLMNOP", lines[0]);
        Assert.Equal("00000010  51 52 53 FF                                       QRS.", lines[1]);

        // Offset column: eight hexadecimal digits, advancing by 16 per line.
        Assert.Equal("00000000", lines[0][..8]);
        Assert.Equal("00000010", lines[1][..8]);

        // ASCII column: same start position on both lines, one character per byte.
        int asciiColumn = lines[0].IndexOf(".BCDEFGHIJKLMNOP", StringComparison.Ordinal);
        Assert.Equal(asciiColumn, lines[1].IndexOf("QRS.", StringComparison.Ordinal));
        Assert.Equal(16, lines[0].Length - asciiColumn);
        Assert.Equal(4, lines[1].Length - asciiColumn);

        // The property and the Raw section's "Hex dump" field are the same text (Core's format).
        Assert.Equal(Core.Text.HexDump.Format(sample), detail.HexDump);
        DetailSectionViewModel raw = Section(detail, Strings.SectionRaw);
        DetailFieldViewModel hex = Field(raw, VariableDetailViewModel.HexDumpLabel);
        Assert.Equal(detail.HexDump, hex.Value);
        Assert.True(hex.IsMultiline);
        Assert.Equal(VariableDetailViewModel.HexDumpLabel, raw.Fields[0].Label);
    }

    [Fact]
    public void HexDump_LargePayload_Uses16BytesPerLine_AndSameAsCoreForEveryLine()
    {
        byte[] data = new byte[100];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)i;
        }

        VariableDetailViewModel detail = new();
        detail.Show(Item(Variable("Big", VendorGuid, NvBsRt, data), null));

        string[] lines = detail.HexDump.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(7, lines.Length);
        for (int i = 0; i < lines.Length; i++)
        {
            Assert.StartsWith((i * 16).ToString("X8", CultureInfo.InvariantCulture) + "  ", lines[i], StringComparison.Ordinal);
        }

        Assert.Equal(Core.Text.HexDump.Lines(data), lines);
        Assert.StartsWith("00000060  60 61 62 63", lines[6], StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyData_ShowsEmptyPlaceholder_AndNoTextField()
    {
        VariableDetailViewModel detail = new();
        detail.Show(Item(Variable("Empty", VendorGuid, NvBsRt, Array.Empty<byte>()), null));

        Assert.Equal(VariableDetailViewModel.EmptyDataText, detail.HexDump);
        DetailSectionViewModel raw = Section(detail, Strings.SectionRaw);
        Assert.Single(raw.Fields);
        Assert.Equal(VariableDetailViewModel.EmptyDataText, raw.Fields[0].Value);
        Assert.Equal(Strings.HexDumpName, raw.Fields[0].AccessibleName);
        Assert.Equal(VariableDetailViewModel.EmptyDataText, detail.CopyValueText);
    }

    [Fact]
    public void NullData_IsTreatedAsEmpty_WithoutThrowing()
    {
        FirmwareVariable variable = new("Odd", VendorGuid, NvBsRt, null!);
        VariableDetailViewModel detail = new();

        Exception? exception = Record.Exception(() => detail.Show(Item(variable, null)));

        Assert.Null(exception);
        Assert.Equal(VariableDetailViewModel.EmptyDataText, detail.HexDump);
        Assert.Equal("0 bytes", Section(detail, Strings.SectionSize).Fields[0].Value);
    }

    [Fact]
    public void FirstBytesAsText_AppearsForAsciiAndUtf16Prefixes_NotForShortOrBinaryOnes()
    {
        VariableDetailViewModel detail = new();

        // ASCII "en-US" followed by a terminator: five printable characters.
        detail.Show(Item(Variable("PlatformLang", Global, NvBsRt, LoadOptionFixtures.AsciiZ("en-US")), null));
        Assert.Equal("en-US", Field(Section(detail, Strings.SectionRaw), VariableDetailViewModel.FirstBytesAsTextLabel).Value);

        // UTF-16LE "Hello" with terminator: the wide prefix is longer than the one-character ASCII prefix.
        detail.Show(Item(Variable("Wide", VendorGuid, NvBsRt, LoadOptionFixtures.Utf16Z("Hello")), null));
        Assert.Equal("Hello", Field(Section(detail, Strings.SectionRaw), VariableDetailViewModel.FirstBytesAsTextLabel).Value);

        // "BBS" then binary: only three printable characters, so no text field.
        detail.Show(Item(Variable("Short", VendorGuid, NvBsRt, new byte[] { 0x42, 0x42, 0x53, 0x00, 0x01, 0x00 }), null));
        Assert.DoesNotContain(Section(detail, Strings.SectionRaw).Fields, field => field.Label == VariableDetailViewModel.FirstBytesAsTextLabel);

        // Exactly four printable characters is enough.
        detail.Show(Item(Variable("Four", VendorGuid, NvBsRt, new byte[] { 0x41, 0x42, 0x43, 0x44, 0x00, 0xFF }), null));
        Assert.Equal("ABCD", Field(Section(detail, Strings.SectionRaw), VariableDetailViewModel.FirstBytesAsTextLabel).Value);

        // A BootOrder (UINT16 array) has no printable prefix.
        detail.Show(Item(Variable("BootOrder", Global, NvBsRt, new byte[] { 0x01, 0x00, 0x02, 0x00, 0x03, 0x00, 0x04, 0x00 }), null));
        Assert.DoesNotContain(Section(detail, Strings.SectionRaw).Fields, field => field.Label == VariableDetailViewModel.FirstBytesAsTextLabel);
    }

    [Fact]
    public void LeadingPrintableText_MatchesTheLoadOptionDecoderRule()
    {
        Assert.Null(VariableDetailViewModel.LeadingPrintableText(ReadOnlySpan<byte>.Empty));
        Assert.Null(VariableDetailViewModel.LeadingPrintableText(new byte[] { 0x41, 0x42, 0x43 }));
        Assert.Equal("ABCD", VariableDetailViewModel.LeadingPrintableText(new byte[] { 0x41, 0x42, 0x43, 0x44 }));
        Assert.Equal("WINDOWS", VariableDetailViewModel.LeadingPrintableText(LoadOptionFixtures.WindowsOptionalData));
        Assert.Equal("Wide", VariableDetailViewModel.LeadingPrintableText(LoadOptionFixtures.Utf16("Wide")));
        Assert.Null(VariableDetailViewModel.LeadingPrintableText(LoadOptionFixtures.Utf16("Wid")));
    }

    // ---- Empty state and read-only status --------------------------------------------------

    [Fact]
    public void NewPane_IsInTheEmptyState()
    {
        VariableDetailViewModel detail = new();

        Assert.Null(detail.Item);
        Assert.False(detail.HasSelection);
        Assert.Equal(Strings.NoSelectionHeading, detail.Heading);
        Assert.Equal("No variable selected", detail.Heading);
        Assert.Empty(detail.Sections);
        Assert.Equal(string.Empty, detail.HexDump);
        Assert.Null(detail.CopyValueText);

        // M3 (design section 5.4): the Writability line belongs to the row shown; with none it
        // says no variable is selected, and the Change group of a pane without a pipeline never shows.
        Assert.Equal(Strings.NoSelectionHeading, detail.ReadOnlyStatus);
        Assert.False(detail.Change.IsVisible);
    }

    [Fact]
    public void ShowNull_ClearsEverything_AndTheWritabilityLineSaysNoVariableIsSelected()
    {
        VariableDetailViewModel detail = new();
        detail.Show(BootEntry());
        detail.CopyValueText = "a field the view picked";
        Assert.NotEmpty(detail.Sections);

        detail.Show(null);

        Assert.Null(detail.Item);
        Assert.False(detail.HasSelection);
        Assert.Equal("No variable selected", detail.Heading);
        Assert.Empty(detail.Sections);
        Assert.Equal(string.Empty, detail.HexDump);
        Assert.Null(detail.CopyValueText);
        Assert.Equal(Strings.NoSelectionHeading, detail.ReadOnlyStatus);
    }

    [Fact]
    public void ReadOnlyStatus_IsTheRowsWritabilitySentence_NoLongerTheSharedAlpha1Sentence()
    {
        // M3 changes what this test pinned: the line is Core's verdict for each row. A row built
        // without a verdict is judged live, on a machine that is not the contracted one.
        VariableDetailViewModel detail = new();

        detail.Show(BootEntry());
        Assert.Equal(UefiAccess.Core.Safety.WritabilitySentences.WrongMachine, detail.ReadOnlyStatus);
        detail.Show(Item(Variable("X", VendorGuid, VariableAttributes.None, new byte[] { 1 }), null));
        Assert.Equal(UefiAccess.Core.Safety.WritabilitySentences.NotWhitelisted, detail.ReadOnlyStatus);

        // A2-6 (the tester's A2-4): the line is a read-only box whose state the screen reader says
        // itself, so the sentence of a variable this application cannot change does not begin
        // with "Read-only" again, as Alpha 1's shared sentence did.
        Assert.StartsWith("Cannot be changed", detail.ReadOnlyStatus, StringComparison.Ordinal);
        Assert.False(detail.ReadOnlyStatus.StartsWith("Read", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Fix 1 of the A16 measurement: the pane is updated in place, so showing the row it already
    /// shows keeps the sections collection, every section and every field (the same objects, so
    /// the view keeps every control) and raises nothing but the copy text, which goes back to its
    /// default. (Until round 4 this test asserted the opposite: a new sections list on every Show.)
    /// </summary>
    [Fact]
    public void ShowingTheSameItemTwice_KeepsEverySectionAndField_AndResetsTheCopyText()
    {
        VariableItemViewModel item = BootEntry();
        VariableDetailViewModel detail = new();
        detail.Show(item);
        IReadOnlyList<DetailSectionViewModel> first = detail.Sections;
        DetailSectionViewModel[] sections = detail.Sections.ToArray();
        DetailFieldViewModel[][] fields = detail.Sections.Select(section => section.Fields.ToArray()).ToArray();
        detail.CopyValueText = "focused field";
        DetailPaneRecorder recorder = new(detail);

        DetailPaneUpdate update = detail.Show(item);

        Assert.Same(first, detail.Sections);
        Assert.Equal(sections.Length, detail.Sections.Count);
        for (int s = 0; s < sections.Length; s++)
        {
            Assert.Same(sections[s], detail.Sections[s]);
            Assert.Equal(fields[s].Length, detail.Sections[s].Fields.Count);
            for (int f = 0; f < fields[s].Length; f++)
            {
                Assert.Same(fields[s][f], detail.Sections[s].Fields[f]);
            }
        }

        Assert.Equal(new[] { "Detail.CopyValueText" }, recorder.Events);
        Assert.Equal(item.Summary, detail.CopyValueText);
        Assert.False(update.HasStructureChange);
        Assert.Equal(0, update.FieldsUpdated);
        Assert.Equal(sections.Length, update.SectionsKept);
        Assert.Equal(fields.Sum(section => section.Length), update.FieldsKept);
    }

    // ---- Accessible names -----------------------------------------------------------------

    [Fact]
    public void EveryField_IsNamedByItsLabelAlone_TheHexDumpByItsDocumentedName()
    {
        VariableDetailViewModel detail = new();
        detail.Show(BootEntry());

        List<DetailFieldViewModel> fields = detail.Sections.SelectMany(section => section.Fields).ToList();
        Assert.NotEmpty(fields);
        Assert.Contains(fields, field => field.IsMultiline);
        Assert.Contains(fields, field => !field.IsMultiline);
        foreach (DetailFieldViewModel field in fields)
        {
            Assert.False(string.IsNullOrWhiteSpace(field.Label));
            Assert.False(string.IsNullOrWhiteSpace(field.AccessibleName));
            if (field.Label == VariableDetailViewModel.HexDumpLabel)
            {
                // The hex box has the documented name (docs/Keyboard-Map.md section 7).
                Assert.Equal(Strings.HexDumpName, field.AccessibleName);
                Assert.Equal("Raw data, hex dump", field.AccessibleName);
            }
            else
            {
                // Single- or multi-line, the box is named by its label: the value is exposed
                // through the Value pattern and must not be spoken a second time as the name
                // (docs/Keyboard-Map.md section 7).
                Assert.Equal(field.Label, field.AccessibleName);
            }

            Assert.DoesNotContain(": " + field.Value, field.AccessibleName, StringComparison.Ordinal);
            if (field.Value.Length > 0 && field.Value != field.Label)
            {
                Assert.DoesNotContain(field.Value, field.AccessibleName, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void HexDumpBox_IsNamedRawDataHexDump_NeverWithHundredsOfHexCharacters()
    {
        // 244 bytes (a typical PK) is 16 lines of hex: the name must not carry them.
        byte[] data = new byte[244];
        VariableDetailViewModel detail = new();
        detail.Show(Item(Variable("PK", Global, NvBsRt, data), null));

        DetailFieldViewModel hex = Field(Section(detail, Strings.SectionRaw), VariableDetailViewModel.HexDumpLabel);
        Assert.True(hex.IsMultiline);
        Assert.True(hex.Value.Length > 1000);
        Assert.Equal(detail.HexDump, hex.Value);
        Assert.Equal(Strings.HexDumpName, hex.AccessibleName);
        Assert.DoesNotContain("00000000", hex.AccessibleName, StringComparison.Ordinal);
    }

    [Fact]
    public void LongValues_NeverReachTheAccessibleName_AndAreNeverCutInTheValue()
    {
        Assert.Equal(200, DetailFieldViewModel.AccessibleValueMaxLength);
        Assert.Equal(VariableItemViewModel.AccessibleNameMaxLength, DetailFieldViewModel.AccessibleValueMaxLength);

        // Identity fields are single-line whatever their length: a 300-character name stays whole
        // in the value and stays out of the name.
        string longName = new('N', 300);
        VariableDetailViewModel detail = new();
        detail.Show(Item(Variable(longName, VendorGuid, NvBsRt, new byte[] { 1 }), null));
        DetailFieldViewModel name = Field(Section(detail, Strings.SectionIdentity), VariableDetailViewModel.NameLabel);
        Assert.Equal(longName, name.Value);
        Assert.False(name.IsMultiline);
        Assert.Equal("Name", name.AccessibleName);
        Assert.DoesNotContain("…", name.AccessibleName, StringComparison.Ordinal);

        // A long decoded value is single-line (never multi-line for its length; the box wraps it) and named by its label.
        string longValue = new('x', 1000);
        DecodedVariable decoded = new(FirmwareVariable.MakeKey(VendorGuid, "Y"), "Synthetic", "s", new[] { new DecodedField("Text", longValue) }, null, null);
        detail.Show(Item(Variable("Y", VendorGuid, NvBsRt, new byte[] { 1 }), decoded));
        DetailFieldViewModel text = Field(Section(detail, Strings.SectionDecoded), "Text");
        Assert.Equal(longValue, text.Value);
        Assert.False(text.IsMultiline);
        Assert.Equal("Text", text.AccessibleName);
    }

    [Fact]
    public void DetailField_ExplicitAccessibleName_WinsOverTheLabel_BlankIsIgnored()
    {
        Assert.Equal("Spoken", new DetailFieldViewModel("Label", "value", isMultiline: false, accessibleName: "Spoken").AccessibleName);
        Assert.Equal("Spoken", new DetailFieldViewModel("Label", "value", isMultiline: true, accessibleName: "Spoken").AccessibleName);
        Assert.Equal("Label", new DetailFieldViewModel("Label", "value", accessibleName: "  ").AccessibleName);
        Assert.Equal("Label", new DetailFieldViewModel("Label", "value", isMultiline: true, accessibleName: null).AccessibleName);
        Assert.Equal("Label", new DetailFieldViewModel("Label", null!).AccessibleName);
        Assert.Equal(string.Empty, new DetailFieldViewModel("Label", null!).Value);
    }

    [Theory]
    [InlineData(198)]
    [InlineData(199)]
    public void Truncate_NeverSplitsASurrogatePair(int lettersBeforeTheEmoji)
    {
        // U+1F600 is a surrogate pair; place it so the cut would fall between its two halves
        // (198 letters: the pair sits at 198-199, keep = 199 would end on the high surrogate).
        string text = new string('a', lettersBeforeTheEmoji) + "\U0001F600" + "zzz";

        string truncated = DetailFieldViewModel.Truncate(text, 200);

        Assert.EndsWith("…", truncated, StringComparison.Ordinal);
        Assert.True(truncated.Length <= 200);
        Assert.True(IsWellFormedUtf16(truncated), "the accessible name must not end in a lone surrogate");
        Assert.False(char.IsHighSurrogate(truncated[^2]));
        if (lettersBeforeTheEmoji == 198)
        {
            Assert.Equal(new string('a', 198) + "…", truncated);
        }
        else
        {
            Assert.Equal(new string('a', 199) + "…", truncated);
        }

        // The row sentence goes through the same helper.
        VariableItemViewModel row = Item(Variable(text, VendorGuid, NvBsRt, new byte[] { 1 }), null);
        Assert.True(IsWellFormedUtf16(row.AccessibleName));
        Assert.True(row.AccessibleName.Length <= VariableItemViewModel.AccessibleNameMaxLength);
    }

    [Fact]
    public void Truncate_KeepsShortTextAndEmptyText_AndCapsAtOneCharacterMinimum()
    {
        Assert.Equal("short", DetailFieldViewModel.Truncate("short", 200));
        Assert.Equal(string.Empty, DetailFieldViewModel.Truncate(string.Empty, 200));
        Assert.Equal(string.Empty, DetailFieldViewModel.Truncate(null!, 200));
        Assert.Equal("abcd", DetailFieldViewModel.Truncate("abcd", 4));
        Assert.Equal("abc…", DetailFieldViewModel.Truncate("abcde", 4));
        Assert.Equal("…", DetailFieldViewModel.Truncate("abcde", 1));
        Assert.Equal("…", DetailFieldViewModel.Truncate("\U0001F600b", 2));
    }

    // ---- Change notifications -------------------------------------------------------------

    /// <summary>
    /// Every bound property that changes is notified. The sections are one collection for the life
    /// of the pane (fix 1 of the A16 measurement), so the Sections property itself is never
    /// notified (until round 4 it was replaced and notified on every Show); the collection
    /// notifies its own changes instead, one Add per section shown from the empty pane and one
    /// Remove per section on the way back to it, never a Reset.
    /// </summary>
    [Fact]
    public void Show_RaisesPropertyChangedForEveryBoundProperty()
    {
        VariableDetailViewModel detail = new();
        List<string?> changed = new();
        detail.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        List<NotifyCollectionChangedEventArgs> sectionChanges = new();
        ((INotifyCollectionChanged)detail.Sections).CollectionChanged += (_, e) => sectionChanges.Add(e);

        detail.Show(BootEntry());

        Assert.Contains(nameof(VariableDetailViewModel.Item), changed);
        Assert.Contains(nameof(VariableDetailViewModel.HasSelection), changed);
        Assert.Contains(nameof(VariableDetailViewModel.Heading), changed);
        Assert.DoesNotContain(nameof(VariableDetailViewModel.Sections), changed);
        Assert.Contains(nameof(VariableDetailViewModel.HexDump), changed);
        Assert.Contains(nameof(VariableDetailViewModel.CopyValueText), changed);
        Assert.Contains(nameof(VariableDetailViewModel.ReadOnlyStatus), changed);
        Assert.Equal(5, sectionChanges.Count);
        Assert.All(sectionChanges, e => Assert.Equal(NotifyCollectionChangedAction.Add, e.Action));
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, sectionChanges.Select(e => e.NewStartingIndex));

        changed.Clear();
        sectionChanges.Clear();
        detail.Show(null);
        Assert.Contains(nameof(VariableDetailViewModel.Item), changed);
        Assert.Contains(nameof(VariableDetailViewModel.HasSelection), changed);
        Assert.Contains(nameof(VariableDetailViewModel.Heading), changed);
        Assert.DoesNotContain(nameof(VariableDetailViewModel.Sections), changed);
        Assert.Contains(nameof(VariableDetailViewModel.HexDump), changed);
        Assert.Contains(nameof(VariableDetailViewModel.CopyValueText), changed);
        Assert.Contains(nameof(VariableDetailViewModel.ReadOnlyStatus), changed);
        Assert.Equal(5, sectionChanges.Count);
        Assert.All(sectionChanges, e => Assert.Equal(NotifyCollectionChangedAction.Remove, e.Action));
        Assert.Equal(new[] { 4, 3, 2, 1, 0 }, sectionChanges.Select(e => e.OldStartingIndex));
        Assert.IsAssignableFrom<INotifyPropertyChanged>(detail);
        Assert.IsAssignableFrom<INotifyCollectionChanged>(detail.Sections);
    }

    [Fact]
    public void Show_AssignsItemLast_SoAListenerSeesTheNewSections()
    {
        VariableDetailViewModel detail = new();
        int sectionsWhenItemChanged = -1;
        string? headingWhenItemChanged = null;
        detail.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VariableDetailViewModel.Item))
            {
                sectionsWhenItemChanged = detail.Sections.Count;
                headingWhenItemChanged = detail.Heading;
            }
        };

        detail.Show(BootEntry());
        Assert.Equal(5, sectionsWhenItemChanged);
        Assert.Equal("Boot0001", headingWhenItemChanged);

        detail.Show(null);
        Assert.Equal(0, sectionsWhenItemChanged);
        Assert.Equal(Strings.NoSelectionHeading, headingWhenItemChanged);
    }

    /// <summary>
    /// The in-place update of the sections (fix 1 of the A16 measurement) leaves M3's part of
    /// <see cref="VariableDetailViewModel.Show"/> as it was: every Show sets the Writability line to
    /// the row's sentence (<see cref="Strings.NoSelectionHeading"/> for none) and tells the Change
    /// group which variable is shown (null for none), also when the sections and fields are kept,
    /// and both are done before Item changes, so a listener on Item sees them.
    /// </summary>
    [Fact]
    public void Show_InPlace_StillSetsTheWritabilityLineAndTellsTheChangeGroup_BeforeItem()
    {
        VariableDetailViewModel detail = new();
        string? lineWhenItemChanged = null;
        VariableItemViewModel? changeItemWhenItemChanged = null;
        detail.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VariableDetailViewModel.Item))
            {
                lineWhenItemChanged = detail.ReadOnlyStatus;
                changeItemWhenItemChanged = detail.Change.Item;
            }
        };

        VariableItemViewModel first = BootEntry("Boot0001");
        VariableItemViewModel second = Item(Variable("X", VendorGuid, VariableAttributes.None, new byte[] { 1 }), null);
        VariableItemViewModel third = Item(Variable("Y", VendorGuid, VariableAttributes.None, new byte[] { 2 }), null);

        detail.Show(first);
        Assert.Equal(VariableDetailViewModel.WritabilityLine(first), detail.ReadOnlyStatus);
        Assert.Same(first, detail.Change.Item);
        Assert.Equal(VariableDetailViewModel.WritabilityLine(first), lineWhenItemChanged);
        Assert.Same(first, changeItemWhenItemChanged);

        detail.Show(second);
        Assert.Equal(VariableDetailViewModel.WritabilityLine(second), detail.ReadOnlyStatus);
        Assert.NotEqual(VariableDetailViewModel.WritabilityLine(first), detail.ReadOnlyStatus);
        Assert.Same(second, detail.Change.Item);
        Assert.Same(second, changeItemWhenItemChanged);

        // Two rows of the same shape: every section and field is kept, and the line and the Change
        // group still follow the row.
        DetailSectionViewModel[] sections = detail.Sections.ToArray();
        DetailPaneUpdate update = detail.Show(third);
        Assert.False(update.HasStructureChange);
        Assert.Equal(sections, detail.Sections);
        Assert.Equal(VariableDetailViewModel.WritabilityLine(third), detail.ReadOnlyStatus);
        Assert.Same(third, detail.Change.Item);
        Assert.Same(third, changeItemWhenItemChanged);
        Assert.False(detail.Change.IsVisible);

        detail.Show(null);
        Assert.Equal(Strings.NoSelectionHeading, detail.ReadOnlyStatus);
        Assert.Null(detail.Change.Item);
        Assert.Equal(Strings.NoSelectionHeading, lineWhenItemChanged);
        Assert.Null(changeItemWhenItemChanged);
        Assert.Empty(detail.Sections);
    }

    // ---- Label guard ----------------------------------------------------------------------

    /// <summary>
    /// Hard rule 1 forbids controls whose label implies a change. The pane's own field labels
    /// (the *Label constants and the section titles) must therefore contain none of the
    /// write-implying words, in any casing. The UEFI attribute names and their meanings
    /// ("EFI_VARIABLE_AUTHENTICATED_WRITE_ACCESS", "Authenticated write: only a signed update can
    /// change it") are values that describe the firmware's own write-access flags inside read-only
    /// boxes; they are deliberately exempt, and this test says so instead of passing by accident
    /// of a case-sensitive comparison.
    /// </summary>
    [Fact]
    public void NoFieldLabel_ContainsAWriteImplyingWord_InAnyCasing()
    {
        string[] forbidden = { "Apply", "Save", "Toggle", "Edit", "Write", "Set" };
        FieldInfo[] labelConstants = typeof(VariableDetailViewModel)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string) && field.Name.EndsWith("Label", StringComparison.Ordinal))
            .ToArray();
        Assert.True(labelConstants.Length >= 10, "expected the field label constants; found " + labelConstants.Length);

        foreach (FieldInfo constant in labelConstants)
        {
            string label = (string?)constant.GetRawConstantValue() ?? string.Empty;
            Assert.False(string.IsNullOrWhiteSpace(label), constant.Name + " is blank");
            foreach (string word in forbidden)
            {
                Assert.False(label.Contains(word, StringComparison.OrdinalIgnoreCase), constant.Name + " (\"" + label + "\") contains " + word);
            }
        }

        // The labels the pane produces for a fully attributed variable: every label that is not a
        // UEFI attribute name is clean in any casing; the attribute names are exempt (see above).
        VariableDetailViewModel detail = new();
        detail.Show(Item(Variable("All", VendorGuid, (VariableAttributes)0xFF, new byte[] { 1 }), null));
        List<DetailFieldViewModel> fields = detail.Sections.SelectMany(section => section.Fields).ToList();
        List<DetailFieldViewModel> attributeFields = fields.Where(field => field.Label.StartsWith("EFI_VARIABLE_", StringComparison.Ordinal)).ToList();
        Assert.Equal(8, attributeFields.Count);
        Assert.Contains(attributeFields, field => field.Label.Contains("WRITE", StringComparison.Ordinal));
        Assert.Contains(attributeFields, field => field.Value.Contains("write", StringComparison.OrdinalIgnoreCase));
        foreach (DetailFieldViewModel field in fields.Except(attributeFields))
        {
            foreach (string word in forbidden)
            {
                Assert.False(field.Label.Contains(word, StringComparison.OrdinalIgnoreCase), "\"" + field.Label + "\" contains " + word);
            }
        }
    }

    // ---- Helpers --------------------------------------------------------------------------

    private static FirmwareVariable Variable(string name, Guid guid, VariableAttributes attributes, byte[] data) => new(name, guid, attributes, data);

    private static VariableItemViewModel Item(FirmwareVariable variable, DecodedVariable? decoded, NamespaceInfo? ns = null)
    {
        return new VariableItemViewModel(variable, ns ?? NamespaceCatalog.Describe(variable.VendorGuid), decoded);
    }

    /// <summary>A Boot#### row decoded through the default registry, exactly as the list view model builds it.</summary>
    private static VariableItemViewModel BootEntry(string name = "Boot0001", byte[]? data = null)
    {
        FirmwareVariable variable = Variable(name, Global, NvBsRt, data ?? LoadOptionFixtures.WindowsBootManager());
        VariableSet set = new(new[] { variable });
        DecodedVariable? decoded = DecoderRegistry.CreateDefault().TryDecode(variable, set);
        return Item(variable, decoded, new NamespaceInfo(Global, "EFI global variable", true));
    }

    private static DetailSectionViewModel Section(VariableDetailViewModel detail, string title)
    {
        DetailSectionViewModel? section = detail.Sections.SingleOrDefault(candidate => candidate.Title == title);
        Assert.NotNull(section);
        return section;
    }

    private static DetailFieldViewModel Field(DetailSectionViewModel section, string label)
    {
        DetailFieldViewModel? field = section.Fields.SingleOrDefault(candidate => candidate.Label == label);
        Assert.NotNull(field);
        return field;
    }

    /// <summary>True when every surrogate in the text is one half of a complete pair.</summary>
    private static bool IsWellFormedUtf16(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                {
                    return false;
                }

                i++;
            }
            else if (char.IsLowSurrogate(text[i]))
            {
                return false;
            }
        }

        return true;
    }
}
