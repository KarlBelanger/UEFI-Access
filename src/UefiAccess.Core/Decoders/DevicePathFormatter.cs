// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Buffers.Binary;
using System.Text;
using static UefiAccess.Core.Decoders.DecoderText;

namespace UefiAccess.Core.Decoders;

/// <summary>A device path rendered as text.</summary>
/// <param name="Text">
/// The rendered path: nodes joined with "/". An end-of-instance node is rendered as ",". When a
/// FilePathList carries more than one device path (each terminated by its own End node) the paths
/// are separated by ";".
/// </param>
/// <param name="Nodes">One text per node in order. Instance and path boundaries appear as "," and ";" entries.</param>
/// <param name="Error">
/// Description of malformed input: a node length under 4 or beyond the buffer, a truncated node
/// header, an empty buffer, or a path that does not finish with an End node. Null when the whole
/// buffer was well formed. The nodes parsed before the problem are still returned.
/// </param>
public sealed record DevicePathResult(string Text, IReadOnlyList<string> Nodes, string? Error);

/// <summary>What the first device path of a FilePathList names, as structured values (see <see cref="DevicePathFormatter.ReadFirstPath"/>).</summary>
/// <param name="HardDrive">The first hard drive media node, or null when the path has none.</param>
/// <param name="FilePath">The joined text of the File nodes, or null when the path has none.</param>
/// <param name="RemovableMessagingNode">True when the path passes through a USB, SD or eMMC messaging node.</param>
internal readonly record struct FirstDevicePath(HardDriveNode? HardDrive, string? FilePath, bool RemovableMessagingNode);

/// <summary>
/// Renders EFI device paths efibootmgr-style (UEFI 2.10, section 10). Node header: Type (1 byte),
/// SubType (1 byte), Length (2 bytes little-endian). The nodes are
/// rendered by name; every other node prints Type(0xTT,0xSS,hex). Hexadecimal is lowercase, GUIDs
/// use the lowercase "D" format, and a known node whose payload is shorter than its documented
/// layout falls back to the Type(...) form. Parsing stops at the first malformed node and reports
/// it in <see cref="DevicePathResult.Error"/>; this class never throws.
/// </summary>
public static class DevicePathFormatter
{
    private const byte HardwareType = 0x01;
    private const byte AcpiType = 0x02;
    private const byte MessagingType = 0x03;
    private const byte MediaType = 0x04;
    private const byte BbsType = 0x05;
    private const byte EndType = 0x7F;
    private const byte EndEntireSubType = 0xFF;
    private const byte EndInstanceSubType = 0x01;
    private const int HeaderLength = 4;
    private const byte HardDriveSubType = 0x01;
    private const byte FileSubType = 0x04;

    // Type 4 / SubType 1 payload: UINT32 PartitionNumber, UINT64 PartitionStart, UINT64 PartitionSize,
    // UINT8 Signature[16], UINT8 MBRType (1 MBR, 2 GPT), UINT8 SignatureType (0 none, 1 MBR 32-bit, 2 GUID).
    private const int HardDrivePayloadLength = 38;
    private const int HardDriveStartOffset = 4;
    private const int HardDriveSizeOffset = 12;
    private const int HardDriveSignatureOffset = 20;
    private const int HardDriveSignatureLength = 16;
    private const int HardDriveMbrTypeOffset = 36;
    private const int HardDriveSignatureTypeOffset = 37;

    /// <summary>The separator of the File nodes' texts when a path is split over several.</summary>
    private const char FileSeparator = '\\';

    /// <summary>
    /// The messaging subtypes of a device the person can take out (rule V17): USB (0x05), USB
    /// class (0x0F), USB WWID (0x10), SD (0x1A) and eMMC (0x1D), which may name a partition too.
    /// </summary>
    private static readonly byte[] RemovableMessagingSubTypes = { 0x05, 0x0F, 0x10, 0x1A, 0x1D };

    // ACPI HIDs are EISA-encoded: vendor "PNP" is 0x41D0 in the low 16 bits, product in the high 16 bits.
    private const uint Pnp0A03 = 0x0A0341D0;
    private const uint Pnp0A08 = 0x0A0841D0;
    private const uint PnpVendorId = 0x41D0;

    private const string InstanceSeparator = ",";
    private const string PathSeparator = ";";

    /// <summary>Formats a device path list.</summary>
    /// <param name="path">The raw device path bytes (a Boot#### FilePathList or a single path).</param>
    /// <returns>The rendered path; Error is set for malformed input.</returns>
    public static DevicePathResult Format(ReadOnlySpan<byte> path)
    {
        List<string> nodes = new();
        if (path.Length == 0)
        {
            return new DevicePathResult(string.Empty, nodes, "device path is empty");
        }

        StringBuilder text = new();
        string? error = null;
        bool needSeparator = false;
        bool terminated = false;
        int offset = 0;

        while (offset < path.Length)
        {
            error = ReadHeader(path, offset, out byte type, out byte subType, out int length);
            if (error is not null)
            {
                break;
            }

            ReadOnlySpan<byte> payload = path.Slice(offset + HeaderLength, length - HeaderLength);
            offset += length;

            if (type == EndType && subType == EndEntireSubType)
            {
                terminated = true;
                if (offset < path.Length)
                {
                    // Another device path follows in the same FilePathList.
                    nodes.Add(PathSeparator);
                    text.Append(PathSeparator);
                    needSeparator = false;
                }

                continue;
            }

            terminated = false;
            if (type == EndType && subType == EndInstanceSubType)
            {
                nodes.Add(InstanceSeparator);
                text.Append(InstanceSeparator);
                needSeparator = false;
                continue;
            }

            string node = RenderNode(type, subType, payload);
            nodes.Add(node);
            if (needSeparator)
            {
                text.Append('/');
            }

            text.Append(node);
            needSeparator = true;
        }

        if (error is null && !terminated)
        {
            error = "device path does not end with an End node";
        }

        return new DevicePathResult(text.ToString(), nodes, error);
    }

    /// <summary>
    /// Reads the first device path of a FilePathList as the structured values the write pipeline
    /// compares, from the same bytes and with the same node walk
    /// <see cref="Format"/> renders: the first hard drive media node, the text of the File nodes
    /// joined with one backslash between them when a path is split over several, and whether the
    /// path passes through a USB, SD or eMMC messaging node. The walk stops at the first End node
    /// (of the instance or of the whole path); a truncated or malformed node ends it with what was
    /// read before it, and <see cref="Format"/> reports the problem. Never throws.
    /// </summary>
    /// <param name="path">The raw device path bytes (a Boot#### FilePathList).</param>
    /// <returns>What the first path names.</returns>
    internal static FirstDevicePath ReadFirstPath(ReadOnlySpan<byte> path)
    {
        HardDriveNode? hardDrive = null;
        string? filePath = null;
        bool removable = false;
        int offset = 0;
        while (offset < path.Length)
        {
            if (ReadHeader(path, offset, out byte type, out byte subType, out int length) is not null || type == EndType)
            {
                break;
            }

            ReadOnlySpan<byte> payload = path.Slice(offset + HeaderLength, length - HeaderLength);
            if (type == MediaType && subType == HardDriveSubType && hardDrive is null && payload.Length >= HardDrivePayloadLength)
            {
                hardDrive = new HardDriveNode(
                    BinaryPrimitives.ReadUInt32LittleEndian(payload),
                    payload[HardDriveSignatureTypeOffset],
                    new Guid(payload.Slice(HardDriveSignatureOffset, HardDriveSignatureLength)));
            }
            else if (type == MediaType && subType == FileSubType)
            {
                filePath = JoinFilePath(filePath, ReadUtf16Z(payload));
            }
            else if (type == MessagingType && Array.IndexOf(RemovableMessagingSubTypes, subType) >= 0)
            {
                removable = true;
            }

            offset += length;
        }

        return new FirstDevicePath(hardDrive, filePath, removable);
    }

    /// <summary>
    /// Reads the header of the node at <paramref name="offset"/> and checks its length against the
    /// buffer: the one node walk of this class. Returns null when the node is whole, otherwise the
    /// description of the problem that ends a walk.
    /// </summary>
    private static string? ReadHeader(ReadOnlySpan<byte> path, int offset, out byte type, out byte subType, out int length)
    {
        type = 0;
        subType = 0;
        length = 0;
        int remaining = path.Length - offset;
        if (remaining < HeaderLength)
        {
            return Invariant($"node header at offset {offset} is truncated: {remaining} byte(s) remain, 4 needed");
        }

        type = path[offset];
        subType = path[offset + 1];
        length = BinaryPrimitives.ReadUInt16LittleEndian(path.Slice(offset + 2, 2));
        if (length < HeaderLength)
        {
            return Invariant($"node at offset {offset} (type 0x{type:x2}, subtype 0x{subType:x2}) has invalid length {length}; the minimum is 4");
        }

        if (length > remaining)
        {
            return Invariant($"node at offset {offset} (type 0x{type:x2}, subtype 0x{subType:x2}) has length {length}, which exceeds the {remaining} remaining byte(s)");
        }

        return null;
    }

    /// <summary>Appends one File node's text to the path read so far, with exactly one backslash between the two parts.</summary>
    private static string JoinFilePath(string? path, string node)
    {
        if (string.IsNullOrEmpty(path))
        {
            return node;
        }

        bool pathEnds = path[^1] == FileSeparator;
        bool nodeStarts = node.Length > 0 && node[0] == FileSeparator;
        if (pathEnds && nodeStarts)
        {
            return path + node.Substring(1);
        }

        return pathEnds || nodeStarts || node.Length == 0 ? path + node : path + FileSeparator + node;
    }

    private static string RenderNode(byte type, byte subType, ReadOnlySpan<byte> payload)
    {
        string? text = (type, subType) switch
        {
            (HardwareType, 0x01) => RenderPci(payload),
            (HardwareType, 0x04) => RenderVendor("VenHw", payload),
            (AcpiType, 0x01) => RenderAcpi(payload),
            (MessagingType, 0x05) => RenderUsb(payload),
            (MessagingType, 0x0B) => RenderMac(payload),
            (MessagingType, 0x0C) => RenderIPv4(payload),
            (MessagingType, 0x0D) => RenderIPv6(payload),
            (MessagingType, 0x12) => RenderSata(payload),
            (MessagingType, 0x17) => RenderNvme(payload),
            (MessagingType, 0x18) => RenderUri(payload),
            (MediaType, 0x01) => RenderHardDrive(payload),
            (MediaType, 0x02) => RenderCdrom(payload),
            (MediaType, 0x03) => RenderVendor("VenMedia", payload),
            (MediaType, 0x04) => RenderFile(payload),
            (MediaType, 0x06) => RenderGuidNode("FvFile", payload),
            (MediaType, 0x07) => RenderGuidNode("Fv", payload),
            (BbsType, 0x01) => RenderBbs(payload),
            _ => null,
        };

        return text ?? RenderUnknown(type, subType, payload);
    }

    private static string RenderUnknown(byte type, byte subType, ReadOnlySpan<byte> payload)
    {
        return Invariant($"Type(0x{type:x2},0x{subType:x2},{Hex(payload)})");
    }

    // Type 1 / SubType 1: UINT8 Function, UINT8 Device.
    private static string? RenderPci(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 2)
        {
            return null;
        }

        return Invariant($"Pci(0x{payload[1]:x},0x{payload[0]:x})");
    }

    // Type 1 / SubType 4 and Type 4 / SubType 3: EFI_GUID, then vendor-defined data.
    private static string? RenderVendor(string name, ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 16)
        {
            return null;
        }

        Guid guid = new(payload.Slice(0, 16));
        ReadOnlySpan<byte> data = payload.Slice(16);
        return data.Length == 0
            ? Invariant($"{name}({guid:D})")
            : Invariant($"{name}({guid:D},{Hex(data)})");
    }

    // Type 2 / SubType 1: UINT32 HID (EISA-encoded), UINT32 UID.
    private static string? RenderAcpi(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 8)
        {
            return null;
        }

        uint hid = BinaryPrimitives.ReadUInt32LittleEndian(payload);
        uint uid = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
        if (hid is Pnp0A03 or Pnp0A08)
        {
            return Invariant($"PciRoot(0x{uid:x})");
        }

        if ((hid & 0xFFFF) == PnpVendorId)
        {
            return Invariant($"Acpi(PNP{hid >> 16:X4},0x{uid:x})");
        }

        return Invariant($"Acpi(0x{hid:x8},0x{uid:x})");
    }

    // Type 3 / SubType 5: UINT8 ParentPortNumber, UINT8 InterfaceNumber.
    private static string? RenderUsb(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 2)
        {
            return null;
        }

        return Invariant($"USB({payload[0]},{payload[1]})");
    }

    // Type 3 / SubType 0x0B: EFI_MAC_ADDRESS (32 bytes), UINT8 IfType. Ethernet (1) and "other" (0) use 6 bytes.
    // Rendered per the design table as colon-separated lowercase hex: MAC(00:11:22:33:44:55,1).
    private static string? RenderMac(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 33)
        {
            return null;
        }

        byte ifType = payload[32];
        int addressLength = ifType is 0 or 1 ? 6 : 32;
        return Invariant($"MAC({ColonHex(payload.Slice(0, addressLength))},{ifType})");
    }

    // Type 3 / SubType 0x0C: LocalIp (4), RemoteIp (4), LocalPort (2), RemotePort (2), Protocol (2), StaticIp (1), Gateway (4), Subnet (4).
    // Rendered in the display form: the remote address only.
    private static string? RenderIPv4(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 8)
        {
            return null;
        }

        return Invariant($"IPv4({payload[4]}.{payload[5]}.{payload[6]}.{payload[7]})");
    }

    // Type 3 / SubType 0x0D: LocalIp (16), RemoteIp (16), LocalPort (2), RemotePort (2), Protocol (2), Origin (1), PrefixLength (1), Gateway (16).
    // Rendered in the display form: the remote address as eight lowercase hexadecimal groups.
    private static string? RenderIPv6(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 32)
        {
            return null;
        }

        StringBuilder groups = new("IPv6(");
        for (int i = 0; i < 8; i++)
        {
            if (i > 0)
            {
                groups.Append(':');
            }

            ushort group = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(16 + (i * 2), 2));
            groups.Append(Invariant($"{group:x}"));
        }

        groups.Append(')');
        return groups.ToString();
    }

    // Type 3 / SubType 0x12: UINT16 HBAPortNumber, UINT16 PortMultiplierPortNumber, UINT16 Lun.
    private static string? RenderSata(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 6)
        {
            return null;
        }

        ushort port = BinaryPrimitives.ReadUInt16LittleEndian(payload);
        ushort pmp = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(2, 2));
        ushort lun = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2));
        return Invariant($"Sata({port},{pmp},{lun})");
    }

    // Type 3 / SubType 0x17: UINT32 NamespaceId, UINT64 NamespaceUuid (IEEE EUI-64, rendered byte by byte).
    private static string? RenderNvme(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 12)
        {
            return null;
        }

        uint namespaceId = BinaryPrimitives.ReadUInt32LittleEndian(payload);
        StringBuilder eui = new();
        for (int i = 0; i < 8; i++)
        {
            if (i > 0)
            {
                eui.Append('-');
            }

            eui.Append(Invariant($"{payload[4 + i]:x2}"));
        }

        return Invariant($"NVMe({namespaceId},{eui})");
    }

    // Type 3 / SubType 0x18: UTF-8 URI, not necessarily null-terminated.
    private static string RenderUri(ReadOnlySpan<byte> payload)
    {
        string uri = Encoding.UTF8.GetString(payload).TrimEnd('\0');
        return Invariant($"Uri({uri})");
    }

    // Type 4 / SubType 1: the layout of the HardDrive* constants.
    private static string? RenderHardDrive(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < HardDrivePayloadLength)
        {
            return null;
        }

        uint partition = BinaryPrimitives.ReadUInt32LittleEndian(payload);
        ulong start = BinaryPrimitives.ReadUInt64LittleEndian(payload.Slice(HardDriveStartOffset, 8));
        ulong size = BinaryPrimitives.ReadUInt64LittleEndian(payload.Slice(HardDriveSizeOffset, 8));
        ReadOnlySpan<byte> signature = payload.Slice(HardDriveSignatureOffset, HardDriveSignatureLength);
        byte mbrType = payload[HardDriveMbrTypeOffset];
        byte signatureType = payload[HardDriveSignatureTypeOffset];

        string tableType = mbrType switch
        {
            1 => "MBR",
            2 => "GPT",
            _ => Invariant($"0x{mbrType:x}"),
        };

        string signatureText = signatureType switch
        {
            2 => new Guid(signature).ToString("D"),
            1 => Invariant($"0x{BinaryPrimitives.ReadUInt32LittleEndian(signature):x8}"),
            _ => "0",
        };

        return Invariant($"HD({partition},{tableType},{signatureText},0x{start:x},0x{size:x})");
    }

    // Type 4 / SubType 2: UINT32 BootEntry, UINT64 PartitionStart, UINT64 PartitionSize.
    private static string? RenderCdrom(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 20)
        {
            return null;
        }

        uint entry = BinaryPrimitives.ReadUInt32LittleEndian(payload);
        ulong start = BinaryPrimitives.ReadUInt64LittleEndian(payload.Slice(4, 8));
        ulong size = BinaryPrimitives.ReadUInt64LittleEndian(payload.Slice(12, 8));
        return Invariant($"CDROM({entry},0x{start:x},0x{size:x})");
    }

    // Type 4 / SubType 4: null-terminated CHAR16 path.
    private static string RenderFile(ReadOnlySpan<byte> payload)
    {
        return Invariant($"File({ReadUtf16Z(payload)})");
    }

    // Type 4 / SubTypes 6 and 7: EFI_GUID (firmware file name or firmware volume name).
    private static string? RenderGuidNode(string name, ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 16)
        {
            return null;
        }

        return Invariant($"{name}({new Guid(payload.Slice(0, 16)):D})");
    }

    // Type 5 / SubType 1: UINT16 DeviceType, UINT16 StatusFlag, null-terminated ASCII description.
    private static string? RenderBbs(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 4)
        {
            return null;
        }

        ushort deviceType = BinaryPrimitives.ReadUInt16LittleEndian(payload);
        string typeName = deviceType switch
        {
            0x01 => "Floppy",
            0x02 => "HD",
            0x03 => "CDROM",
            0x04 => "PCMCIA",
            0x05 => "USB",
            0x06 => "Network",
            0x80 => "BEV",
            _ => Invariant($"0x{deviceType:x}"),
        };

        ReadOnlySpan<byte> description = payload.Slice(4);
        int terminator = description.IndexOf((byte)0);
        if (terminator >= 0)
        {
            description = description.Slice(0, terminator);
        }

        return Invariant($"BBS({typeName},{Encoding.ASCII.GetString(description)})");
    }

    private static string ReadUtf16Z(ReadOnlySpan<byte> payload)
    {
        int even = payload.Length & ~1;
        ReadOnlySpan<byte> text = payload.Slice(0, even);
        for (int i = 0; i + 1 < text.Length; i += 2)
        {
            if (text[i] == 0 && text[i + 1] == 0)
            {
                text = text.Slice(0, i);
                break;
            }
        }

        return Encoding.Unicode.GetString(text);
    }
}
