// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Buffers.Binary;
using System.Text;
using UefiAccess.Core.Variables;
using static UefiAccess.Core.Decoders.DecoderText;

namespace UefiAccess.Core.Decoders;

/// <summary>
/// The hard drive media node of a load option's device path (UEFI 2.10, section 10.3.5.1), as
/// the structured values the write pipeline compares: the partition and
/// its signature, never the rendered text.
/// </summary>
/// <param name="PartitionNumber">The partition number; 0 names the whole disk.</param>
/// <param name="SignatureType">0 no signature, 1 a 32-bit MBR disk signature, 2 a GPT partition GUID.</param>
/// <param name="Signature">
/// The 16 signature bytes as stored, read as a GUID: the partition's unique GUID for GPT; for MBR
/// the disk signature in the first four bytes and zeros after.
/// </param>
public sealed record HardDriveNode(uint PartitionNumber, byte SignatureType, Guid Signature)
{
    /// <summary>SignatureType 1: a 32-bit MBR disk signature.</summary>
    public const byte MbrSignatureType = 1;

    /// <summary>SignatureType 2: a GPT partition GUID.</summary>
    public const byte GuidSignatureType = 2;

    /// <summary>True when the node carries a signature that names a disk (MBR) or a partition (GPT); false for SignatureType 0 or an unknown type.</summary>
    public bool HasSignature => SignatureType is MbrSignatureType or GuidSignatureType;
}

/// <summary>A parsed EFI_LOAD_OPTION (UEFI 2.10, section 3.1.3).</summary>
/// <param name="Attributes">Raw UINT32 attributes.</param>
/// <param name="Active">LOAD_OPTION_ACTIVE (0x1).</param>
/// <param name="ForceReconnect">LOAD_OPTION_FORCE_RECONNECT (0x2).</param>
/// <param name="Hidden">LOAD_OPTION_HIDDEN (0x8).</param>
/// <param name="Category">Category from mask 0x1F00: "Boot" (0) or "App" (0x100); other values rendered as hex.</param>
/// <param name="Description">The null-terminated CHAR16 description (partial text when the terminator is missing).</param>
/// <param name="DevicePath">The device path list rendered as efibootmgr-style text.</param>
/// <param name="DevicePathNodes">The device path rendered node by node.</param>
/// <param name="OptionalDataLength">Number of optional data bytes after the device path list.</param>
/// <param name="OptionalDataText">
/// The optional data as text when it starts with at least four printable ASCII or UTF-16LE
/// characters (the longer of the two prefixes wins); otherwise null.
/// </param>
/// <param name="Error">Description of malformed data, or null when the option parsed completely.</param>
public sealed record LoadOption(
    uint Attributes,
    bool Active,
    bool ForceReconnect,
    bool Hidden,
    string Category,
    string Description,
    string DevicePath,
    IReadOnlyList<string> DevicePathNodes,
    int OptionalDataLength,
    string? OptionalDataText,
    string? Error)
{
    /// <summary>
    /// The first hard drive media node of the first device path in the FilePathList (the path the
    /// boot manager loads from), parsed from the same bytes <see cref="DevicePath"/> renders; null
    /// when that path has none (a USB stick, a network entry, a firmware application) or the
    /// option did not reach its device path.
    /// </summary>
    public HardDriveNode? HardDriveNode { get; init; }

    /// <summary>
    /// The file the first device path names: the text of its File nodes, joined with one
    /// backslash between them when a path is split over several nodes, which is the one File
    /// node's text on an ordinary entry ("\EFI\Microsoft\Boot\bootmgfw.efi"). Null when the path
    /// has no File node. The text is as stored; a comparison ignores case, as FAT does.
    /// </summary>
    public string? FilePath { get; init; }

    /// <summary>
    /// True when the first device path passes through a USB, SD or eMMC messaging node: a device
    /// the person can take out, even when the path also names a partition (a stick written with a
    /// full Pci/USB/HD/File path). Rule V17 treats such an entry as removable.
    /// </summary>
    public bool HasRemovableMessagingNode { get; init; }
}

/// <summary>
/// Pure parser for EFI_LOAD_OPTION bytes: UINT32 Attributes, UINT16 FilePathListLength,
/// null-terminated CHAR16 Description, a device path list of exactly FilePathListLength bytes,
/// then OptionalData. Validation: minimum length 6, description terminator present,
/// FilePathListLength fits, device path ends with an End node. On any problem the result carries
/// whatever was parsed plus an Error. The first device path is also read into
/// <see cref="LoadOption.HardDriveNode"/>, <see cref="LoadOption.FilePath"/> and
/// <see cref="LoadOption.HasRemovableMessagingNode"/> by the formatter's own node walk
/// (<see cref="DevicePathFormatter.ReadFirstPath"/>), from the same bytes it renders; the rendered
/// text is unchanged by it. Never throws.
/// </summary>
public static class LoadOptionParser
{
    /// <summary>LOAD_OPTION_ACTIVE.</summary>
    public const uint ActiveFlag = 0x1;

    /// <summary>LOAD_OPTION_FORCE_RECONNECT.</summary>
    public const uint ForceReconnectFlag = 0x2;

    /// <summary>LOAD_OPTION_HIDDEN.</summary>
    public const uint HiddenFlag = 0x8;

    /// <summary>LOAD_OPTION_CATEGORY mask.</summary>
    public const uint CategoryMask = 0x1F00;

    /// <summary>LOAD_OPTION_CATEGORY_BOOT.</summary>
    public const uint CategoryBoot = 0x0;

    /// <summary>LOAD_OPTION_CATEGORY_APP.</summary>
    public const uint CategoryApp = 0x100;

    private const int MinimumLength = 6;
    private const int DescriptionOffset = 6;
    private const int MinimumPrintableCharacters = 4;

    /// <summary>Parses a load option.</summary>
    /// <param name="data">The raw Boot#### variable data.</param>
    /// <returns>The parsed option; Error is set for malformed input.</returns>
    public static LoadOption Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < MinimumLength)
        {
            return new LoadOption(
                0, false, false, false, CategoryName(0), string.Empty, string.Empty, Array.Empty<string>(), 0, null,
                Invariant($"load option is {data.Length} byte(s); the minimum is {MinimumLength}"));
        }

        uint attributes = BinaryPrimitives.ReadUInt32LittleEndian(data);
        int filePathListLength = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(4, 2));
        bool active = (attributes & ActiveFlag) != 0;
        bool forceReconnect = (attributes & ForceReconnectFlag) != 0;
        bool hidden = (attributes & HiddenFlag) != 0;
        string category = CategoryName(attributes);

        int terminator = -1;
        for (int i = DescriptionOffset; i + 1 < data.Length; i += 2)
        {
            if (data[i] == 0 && data[i + 1] == 0)
            {
                terminator = i;
                break;
            }
        }

        if (terminator < 0)
        {
            int even = (data.Length - DescriptionOffset) & ~1;
            string partial = Encoding.Unicode.GetString(data.Slice(DescriptionOffset, even));
            return new LoadOption(
                attributes, active, forceReconnect, hidden, category, partial, string.Empty, Array.Empty<string>(), 0, null,
                Invariant($"description is not null-terminated within the {data.Length} byte(s) of the variable"));
        }

        string description = Encoding.Unicode.GetString(data.Slice(DescriptionOffset, terminator - DescriptionOffset));
        int pathStart = terminator + 2;
        int available = data.Length - pathStart;
        string? error = null;
        ReadOnlySpan<byte> devicePath;
        ReadOnlySpan<byte> optionalData;
        if (filePathListLength > available)
        {
            error = Invariant($"FilePathListLength {filePathListLength} exceeds the {available} byte(s) after the description");
            devicePath = data.Slice(pathStart);
            optionalData = ReadOnlySpan<byte>.Empty;
        }
        else
        {
            devicePath = data.Slice(pathStart, filePathListLength);
            optionalData = data.Slice(pathStart + filePathListLength);
        }

        DevicePathResult pathResult = DevicePathFormatter.Format(devicePath);
        if (pathResult.Error is not null)
        {
            string pathError = "device path: " + pathResult.Error;
            error = error is null ? pathError : error + "; " + pathError;
        }

        FirstDevicePath firstPath = DevicePathFormatter.ReadFirstPath(devicePath);
        return new LoadOption(
            attributes,
            active,
            forceReconnect,
            hidden,
            category,
            description,
            pathResult.Text,
            pathResult.Nodes,
            optionalData.Length,
            ExtractPrintableText(optionalData),
            error)
        {
            HardDriveNode = firstPath.HardDrive,
            FilePath = firstPath.FilePath,
            HasRemovableMessagingNode = firstPath.RemovableMessagingNode,
        };
    }

    /// <summary>Names the category encoded in the attributes: "Boot", "App", or the hexadecimal value.</summary>
    /// <param name="attributes">The raw attributes.</param>
    /// <returns>The category name.</returns>
    public static string CategoryName(uint attributes)
    {
        uint category = attributes & CategoryMask;
        return category switch
        {
            CategoryBoot => "Boot",
            CategoryApp => "App",
            _ => Invariant($"0x{category:x}"),
        };
    }

    private static string? ExtractPrintableText(ReadOnlySpan<byte> data)
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
            ? Encoding.Unicode.GetString(data.Slice(0, utf16 * 2))
            : Encoding.ASCII.GetString(data.Slice(0, ascii));
    }

    private static bool IsPrintableAscii(byte value) => value is >= 0x20 and <= 0x7E;
}

/// <summary>
/// Decodes Boot#### variables (EFI global variable GUID) through <see cref="LoadOptionParser"/>.
/// Kind is "LoadOption". Never throws.
/// </summary>
public sealed class LoadOptionDecoder : IVariableDecoder
{
    private const string Kind = "LoadOption";

    /// <inheritdoc />
    public bool CanDecode(FirmwareVariable variable)
    {
        return variable is not null
            && variable.VendorGuid == KnownGuids.EfiGlobalVariable
            && KnownVariableNames.IsBootEntryName(variable.Name, out _);
    }

    /// <inheritdoc />
    public DecodedVariable Decode(FirmwareVariable variable, IVariableLookup lookup)
    {
        _ = lookup;
        if (variable is null)
        {
            return new DecodedVariable(string.Empty, Kind, "no variable", Array.Empty<DecodedField>(), null, "variable is null");
        }

        LoadOption option = LoadOptionParser.Parse(variable.Data ?? Array.Empty<byte>());
        KnownVariableNames.IsBootEntryName(variable.Name, out int index);

        List<DecodedField> fields = new()
        {
            new DecodedField("Description", option.Description),
            new DecodedField("Attributes", Invariant($"0x{option.Attributes:x8} ({DescribeAttributes(option)})")),
            new DecodedField("Category", option.Category),
            new DecodedField("Device path", option.DevicePath),
            new DecodedField("Optional data", DescribeOptionalData(option)),
        };
        if (option.Error is not null)
        {
            fields.Add(new DecodedField("Error", option.Error));
        }

        string description = string.IsNullOrEmpty(option.Description) ? "(no description)" : option.Description;
        StringBuilder summary = new();
        summary.Append(variable.Name).Append(": ").Append(description).Append("; ").Append(DescribeAttributes(option));
        if (option.DevicePath.Length > 0)
        {
            summary.Append("; ").Append(option.DevicePath);
        }

        if (option.Error is not null)
        {
            summary.Append("; malformed: ").Append(option.Error);
        }

        object json = new
        {
            variable.Name,
            Index = index,
            option.Attributes,
            option.Active,
            option.ForceReconnect,
            option.Hidden,
            option.Category,
            option.Description,
            option.DevicePath,
            option.DevicePathNodes,
            option.OptionalDataLength,
            option.OptionalDataText,
            option.Error,
        };

        return new DecodedVariable(variable.Key, Kind, summary.ToString(), fields, json, option.Error);
    }

    private static string DescribeAttributes(LoadOption option)
    {
        List<string> parts = new() { option.Active ? "active" : "inactive" };
        if (option.Hidden)
        {
            parts.Add("hidden");
        }

        if (option.ForceReconnect)
        {
            parts.Add("force reconnect");
        }

        parts.Add("category " + option.Category);
        return string.Join(", ", parts);
    }

    private static string DescribeOptionalData(LoadOption option)
    {
        string size = Plural(option.OptionalDataLength, "byte");
        return option.OptionalDataText is null
            ? size
            : Invariant($"{size}, text \"{option.OptionalDataText}\"");
    }
}
