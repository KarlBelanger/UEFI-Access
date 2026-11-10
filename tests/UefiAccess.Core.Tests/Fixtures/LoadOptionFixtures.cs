// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Buffers.Binary;
using System.Text;

namespace UefiAccess.Core.Tests.Fixtures;

/// <summary>
/// Builders and named fixtures for EFI_LOAD_OPTION and EFI_DEVICE_PATH bytes. Every fixture is
/// assembled from the UEFI 2.10 layouts so offsets and lengths are right
/// by construction; the tests then assert the exact rendered text.
/// </summary>
internal static class LoadOptionFixtures
{
    /// <summary>EISA-encoded PNP0A03 (PCI host bridge).</summary>
    public const uint Pnp0A03 = 0x0A0341D0;

    /// <summary>EISA-encoded PNP0A08 (PCI Express host bridge).</summary>
    public const uint Pnp0A08 = 0x0A0841D0;

    public const string WindowsBootManagerDescription = "Windows Boot Manager";
    public const string WindowsBootManagerPath = "HD(1,GPT,6f6e5d1c-8f7a-4d33-9a2b-3c4d5e6f7081,0x800,0x32000)/File(\\EFI\\Microsoft\\Boot\\bootmgfw.efi)";
    public const string UsbStickDescription = "UEFI: SanDisk Cruzer";
    public const string UsbStickPath = "PciRoot(0x0)/Pci(0x14,0x0)/USB(3,0)";
    public const string LegacyBbsDescription = "ST500DM002";
    public const string LegacyBbsPath = "BBS(HD,ST500DM002)";
    public const string PxeDescription = "UEFI: IP4 Intel(R) Ethernet Connection I217-LM";
    public const string PxePath = "PciRoot(0x0)/Pci(0x19,0x0)/MAC(00:11:22:33:44:55,1)/IPv4(0.0.0.0)";

    /// <summary>GPT partition signature used by the Windows Boot Manager fixture.</summary>
    public static readonly Guid EspSignature = new("6f6e5d1c-8f7a-4d33-9a2b-3c4d5e6f7081");

    /// <summary>ASCII "WINDOWS" followed by eight binary bytes, as Windows writes it.</summary>
    public static readonly byte[] WindowsOptionalData = Concat(AsciiBytes("WINDOWS"), new byte[] { 0x00, 0x01, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00 });

    // ---- primitives -------------------------------------------------------------------------

    public static byte[] U16(ushort value)
    {
        byte[] bytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        return bytes;
    }

    public static byte[] U32(uint value)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return bytes;
    }

    public static byte[] U64(ulong value)
    {
        byte[] bytes = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        return bytes;
    }

    public static byte[] Utf16(string text) => Encoding.Unicode.GetBytes(text);

    public static byte[] Utf16Z(string text) => Concat(Utf16(text), new byte[2]);

    public static byte[] AsciiBytes(string text) => Encoding.ASCII.GetBytes(text);

    public static byte[] AsciiZ(string text) => Concat(AsciiBytes(text), new byte[1]);

    public static byte[] Concat(params byte[][] parts)
    {
        byte[] result = new byte[parts.Sum(p => p.Length)];
        int offset = 0;
        foreach (byte[] part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }

    /// <summary>A node with a correct Length field: Type, SubType, Length (LE), payload.</summary>
    public static byte[] Node(byte type, byte subType, params byte[][] payload)
    {
        byte[] body = Concat(payload);
        return RawNode(type, subType, (ushort)(4 + body.Length), body);
    }

    /// <summary>A node with an explicit (possibly wrong) Length field, for malformed-input tests.</summary>
    public static byte[] RawNode(byte type, byte subType, ushort length, params byte[][] payload)
    {
        return Concat(new[] { type, subType }, U16(length), Concat(payload));
    }

    // ---- device path nodes (UEFI 2.10 section 10.3) ---------------------------------------

    public static byte[] Pci(byte device, byte function) => Node(1, 1, new[] { function, device });

    public static byte[] VenHw(Guid guid, byte[]? data = null) => Node(1, 4, guid.ToByteArray(), data ?? Array.Empty<byte>());

    public static byte[] Acpi(uint hid, uint uid) => Node(2, 1, U32(hid), U32(uid));

    public static byte[] PciRoot(uint uid) => Acpi(Pnp0A03, uid);

    public static byte[] Usb(byte parentPort, byte interfaceNumber) => Node(3, 5, new[] { parentPort, interfaceNumber });

    /// <summary>MAC node: the address is padded to the 32-byte EFI_MAC_ADDRESS, then IfType.</summary>
    public static byte[] Mac(byte[] address, byte ifType)
    {
        byte[] padded = new byte[32];
        address.CopyTo(padded, 0);
        return Node(3, 0x0B, padded, new[] { ifType });
    }

    public static byte[] IPv4(byte[] local, byte[] remote, ushort localPort = 0, ushort remotePort = 0, ushort protocol = 0, bool staticAddress = false, byte[]? gateway = null, byte[]? subnet = null)
    {
        return Node(3, 0x0C, local, remote, U16(localPort), U16(remotePort), U16(protocol), new[] { (byte)(staticAddress ? 1 : 0) }, gateway ?? new byte[4], subnet ?? new byte[4]);
    }

    public static byte[] IPv4Zero() => IPv4(new byte[4], new byte[4]);

    public static byte[] IPv6(byte[] local, byte[] remote, ushort localPort = 0, ushort remotePort = 0, ushort protocol = 0, byte origin = 0, byte prefixLength = 0, byte[]? gateway = null)
    {
        return Node(3, 0x0D, local, remote, U16(localPort), U16(remotePort), U16(protocol), new[] { origin, prefixLength }, gateway ?? new byte[16]);
    }

    public static byte[] Sata(ushort hbaPort, ushort portMultiplierPort, ushort lun) => Node(3, 0x12, U16(hbaPort), U16(portMultiplierPort), U16(lun));

    public static byte[] Nvme(uint namespaceId, byte[] eui64) => Node(3, 0x17, U32(namespaceId), eui64);

    public static byte[] UriNode(string uri) => Node(3, 0x18, Encoding.UTF8.GetBytes(uri));

    public static byte[] HardDrive(uint partition, ulong start, ulong size, byte[] signature, byte mbrType, byte signatureType)
    {
        return Node(4, 1, U32(partition), U64(start), U64(size), signature, new[] { mbrType, signatureType });
    }

    public static byte[] HardDriveGpt(uint partition, ulong start, ulong size, Guid signature) => HardDrive(partition, start, size, signature.ToByteArray(), 2, 2);

    public static byte[] HardDriveMbr(uint partition, ulong start, ulong size, uint signature)
    {
        byte[] bytes = new byte[16];
        U32(signature).CopyTo(bytes, 0);
        return HardDrive(partition, start, size, bytes, 1, 1);
    }

    public static byte[] Cdrom(uint bootEntry, ulong start, ulong size) => Node(4, 2, U32(bootEntry), U64(start), U64(size));

    public static byte[] VenMedia(Guid guid, byte[]? data = null) => Node(4, 3, guid.ToByteArray(), data ?? Array.Empty<byte>());

    public static byte[] FilePath(string path) => Node(4, 4, Utf16Z(path));

    public static byte[] FvFile(Guid guid) => Node(4, 6, guid.ToByteArray());

    public static byte[] Fv(Guid guid) => Node(4, 7, guid.ToByteArray());

    public static byte[] Bbs(ushort deviceType, ushort statusFlag, string description) => Node(5, 1, U16(deviceType), U16(statusFlag), AsciiZ(description));

    public static byte[] EndEntire() => Node(0x7F, 0xFF);

    public static byte[] EndInstance() => Node(0x7F, 0x01);

    /// <summary>Nodes followed by an End-entire node.</summary>
    public static byte[] PathOf(params byte[][] nodes) => Concat(Concat(nodes), EndEntire());

    // ---- load options (UEFI 2.10 section 3.1.3) -------------------------------------------

    /// <summary>UINT32 Attributes, UINT16 FilePathListLength, CHAR16 Description with terminator, FilePathList, OptionalData.</summary>
    public static byte[] BuildLoadOption(uint attributes, string description, byte[] filePathList, byte[]? optionalData = null)
    {
        return Concat(U32(attributes), U16((ushort)filePathList.Length), Utf16Z(description), filePathList, optionalData ?? Array.Empty<byte>());
    }

    /// <summary>A load option with an explicit FilePathListLength and raw description bytes, for malformed-input tests.</summary>
    public static byte[] RawLoadOption(uint attributes, ushort filePathListLength, byte[] descriptionBytes, byte[] rest)
    {
        return Concat(U32(attributes), U16(filePathListLength), descriptionBytes, rest);
    }

    /// <summary>Windows Boot Manager on the EFI system partition, with "WINDOWS" optional data.</summary>
    public static byte[] WindowsBootManager()
    {
        return BuildLoadOption(
            1,
            WindowsBootManagerDescription,
            PathOf(HardDriveGpt(1, 0x800, 0x32000, EspSignature), FilePath(@"\EFI\Microsoft\Boot\bootmgfw.efi")),
            WindowsOptionalData);
    }

    /// <summary>A USB stick behind the xHCI controller at device 0x14.</summary>
    public static byte[] UsbStick()
    {
        return BuildLoadOption(1, UsbStickDescription, PathOf(PciRoot(0), Pci(0x14, 0), Usb(3, 0)));
    }

    /// <summary>A legacy BBS hard disk entry with non-text optional data ("BBS" is only three printable bytes).</summary>
    public static byte[] LegacyBbs()
    {
        return BuildLoadOption(1, LegacyBbsDescription, PathOf(Bbs(2, 0, "ST500DM002")), new byte[] { 0x42, 0x42, 0x53, 0x00, 0x01, 0x00 });
    }

    /// <summary>A PXE entry: onboard NIC at device 0x19, MAC, IPv4 with no addresses.</summary>
    public static byte[] Pxe()
    {
        return BuildLoadOption(
            1,
            PxeDescription,
            PathOf(PciRoot(0), Pci(0x19, 0), Mac(new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55 }, 1), IPv4Zero()));
    }

    /// <summary>The Windows Boot Manager fixture cut 20 bytes into the device path list (inside the 42-byte HD node).</summary>
    public static byte[] Truncated()
    {
        byte[] full = WindowsBootManager();
        int cut = 6 + ((WindowsBootManagerDescription.Length + 1) * 2) + 20;
        return full[..cut];
    }
}
