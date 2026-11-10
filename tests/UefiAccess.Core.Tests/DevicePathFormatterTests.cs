// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Decoders;
using Xunit;
using static UefiAccess.Core.Tests.Fixtures.LoadOptionFixtures;

namespace UefiAccess.Core.Tests;

/// <summary>Node-by-node rendering tests for <see cref="DevicePathFormatter"/> plus malformed-input behaviour.</summary>
public sealed class DevicePathFormatterTests
{
    private static readonly Guid SampleGuid = new("0f7a1b2c-3d4e-4f60-8172-93a4b5c6d7e8");

    private static string RenderPath(params byte[][] nodes)
    {
        DevicePathResult result = DevicePathFormatter.Format(PathOf(nodes));
        Assert.Null(result.Error);
        return result.Text;
    }

    [Fact]
    public void Pci_RendersDeviceThenFunctionInHex()
    {
        Assert.Equal("Pci(0x1f,0x2)", RenderPath(Pci(0x1F, 2)));
        Assert.Equal("Pci(0x14,0x0)", RenderPath(Pci(0x14, 0)));
    }

    [Theory]
    [InlineData(Pnp0A03, 0u, "PciRoot(0x0)")]
    [InlineData(Pnp0A08, 1u, "PciRoot(0x1)")]
    [InlineData(0x0A0541D0u, 2u, "Acpi(PNP0A05,0x2)")]
    [InlineData(0x12345678u, 0u, "Acpi(0x12345678,0x0)")]
    public void Acpi_RendersPciRootForHostBridgesAndAcpiOtherwise(uint hid, uint uid, string expected)
    {
        Assert.Equal(expected, RenderPath(Acpi(hid, uid)));
    }

    [Fact]
    public void Usb_RendersPortAndInterfaceInDecimal()
    {
        Assert.Equal("USB(3,0)", RenderPath(Usb(3, 0)));
        Assert.Equal("USB(12,1)", RenderPath(Usb(12, 1)));
    }

    [Fact]
    public void Mac_Ethernet_RendersSixColonSeparatedBytesAndInterfaceType()
    {
        Assert.Equal("MAC(00:11:22:33:44:55,1)", RenderPath(Mac(new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55 }, 1)));
        Assert.Equal("MAC(a0:b1:c2:d3:e4:f5,0)", RenderPath(Mac(new byte[] { 0xA0, 0xB1, 0xC2, 0xD3, 0xE4, 0xF5 }, 0)));
    }

    [Fact]
    public void Mac_OtherInterfaceType_RendersAllThirtyTwoBytesColonSeparated()
    {
        string text = RenderPath(Mac(new byte[] { 0xAA, 0xBB }, 6));
        Assert.Equal("MAC(aa:bb" + string.Concat(Enumerable.Repeat(":00", 30)) + ",6)", text);
    }

    [Fact]
    public void IPv4_RendersRemoteAddress()
    {
        Assert.Equal("IPv4(0.0.0.0)", RenderPath(IPv4Zero()));
        Assert.Equal("IPv4(192.168.1.10)", RenderPath(IPv4(new byte[] { 10, 0, 0, 5 }, new byte[] { 192, 168, 1, 10 }, 0, 67, 17)));
    }

    [Fact]
    public void IPv6_RendersRemoteAddressAsEightHexGroups()
    {
        byte[] remote = new byte[16];
        remote[0] = 0xFE;
        remote[1] = 0x80;
        remote[15] = 0x01;
        Assert.Equal("IPv6(fe80:0:0:0:0:0:0:1)", RenderPath(IPv6(new byte[16], remote)));
        Assert.Equal("IPv6(0:0:0:0:0:0:0:0)", RenderPath(IPv6(new byte[16], new byte[16])));
    }

    [Fact]
    public void Sata_RendersPortMultiplierAndLunInDecimal()
    {
        Assert.Equal("Sata(0,65535,0)", RenderPath(Sata(0, 0xFFFF, 0)));
        Assert.Equal("Sata(2,0,1)", RenderPath(Sata(2, 0, 1)));
    }

    [Fact]
    public void Nvme_RendersNamespaceAndEui()
    {
        byte[] eui = { 0x00, 0x25, 0x38, 0xB1, 0x11, 0x22, 0x33, 0x44 };
        Assert.Equal("NVMe(1,00-25-38-b1-11-22-33-44)", RenderPath(Nvme(1, eui)));
    }

    [Fact]
    public void Uri_RendersUtf8Text()
    {
        Assert.Equal("Uri(http://boot.example/ipxe.efi)", RenderPath(UriNode("http://boot.example/ipxe.efi")));
        Assert.Equal("Uri()", RenderPath(UriNode(string.Empty)));
    }

    [Fact]
    public void HardDrive_Gpt_RendersPartitionSignatureStartAndSize()
    {
        Assert.Equal(
            "HD(1,GPT,6f6e5d1c-8f7a-4d33-9a2b-3c4d5e6f7081,0x800,0x32000)",
            RenderPath(HardDriveGpt(1, 0x800, 0x32000, EspSignature)));
    }

    [Fact]
    public void HardDrive_Mbr_RendersThirtyTwoBitSignature()
    {
        Assert.Equal("HD(2,MBR,0x1a2b3c4d,0x3f,0x1000)", RenderPath(HardDriveMbr(2, 0x3F, 0x1000, 0x1A2B3C4D)));
    }

    [Fact]
    public void HardDrive_UnknownTableAndSignatureTypes_RenderNumerically()
    {
        Assert.Equal("HD(3,0x7,0,0x0,0x10)", RenderPath(HardDrive(3, 0, 0x10, new byte[16], 7, 0)));
    }

    [Fact]
    public void Cdrom_RendersEntryStartAndSize()
    {
        Assert.Equal("CDROM(1,0x20,0x800)", RenderPath(Cdrom(1, 0x20, 0x800)));
    }

    [Fact]
    public void File_RendersUtf16PathWithoutTerminator()
    {
        Assert.Equal("File(\\EFI\\BOOT\\BOOTX64.EFI)", RenderPath(FilePath(@"\EFI\BOOT\BOOTX64.EFI")));
    }

    [Fact]
    public void VenHw_RendersGuidAndOptionalData()
    {
        Assert.Equal("VenHw(0f7a1b2c-3d4e-4f60-8172-93a4b5c6d7e8)", RenderPath(VenHw(SampleGuid)));
        Assert.Equal("VenHw(0f7a1b2c-3d4e-4f60-8172-93a4b5c6d7e8,0102ab)", RenderPath(VenHw(SampleGuid, new byte[] { 0x01, 0x02, 0xAB })));
    }

    [Fact]
    public void VenMedia_RendersGuidAndOptionalData()
    {
        Assert.Equal("VenMedia(0f7a1b2c-3d4e-4f60-8172-93a4b5c6d7e8)", RenderPath(VenMedia(SampleGuid)));
        Assert.Equal("VenMedia(0f7a1b2c-3d4e-4f60-8172-93a4b5c6d7e8,ff)", RenderPath(VenMedia(SampleGuid, new byte[] { 0xFF })));
    }

    [Fact]
    public void FvFile_And_Fv_RenderGuid()
    {
        Assert.Equal("FvFile(0f7a1b2c-3d4e-4f60-8172-93a4b5c6d7e8)", RenderPath(FvFile(SampleGuid)));
        Assert.Equal("Fv(0f7a1b2c-3d4e-4f60-8172-93a4b5c6d7e8)", RenderPath(Fv(SampleGuid)));
    }

    [Theory]
    [InlineData(0x01, "Floppy")]
    [InlineData(0x02, "HD")]
    [InlineData(0x03, "CDROM")]
    [InlineData(0x04, "PCMCIA")]
    [InlineData(0x05, "USB")]
    [InlineData(0x06, "Network")]
    [InlineData(0x80, "BEV")]
    [InlineData(0x42, "0x42")]
    public void Bbs_RendersDeviceTypeNameAndDescription(int deviceType, string expectedType)
    {
        Assert.Equal("BBS(" + expectedType + ",ST500DM002)", RenderPath(Bbs((ushort)deviceType, 0, "ST500DM002")));
    }

    [Fact]
    public void UnknownNode_RendersTypeSubTypeAndHexPayload()
    {
        Assert.Equal("Type(0x03,0x14,0102ab)", RenderPath(Node(3, 0x14, new byte[] { 0x01, 0x02, 0xAB })));
        Assert.Equal("Type(0x09,0x01,)", RenderPath(Node(9, 1)));
    }

    [Fact]
    public void KnownNodeWithShortPayload_FallsBackToTypeForm()
    {
        Assert.Equal("Type(0x01,0x01,05)", RenderPath(Node(1, 1, new byte[] { 0x05 })));
        Assert.Equal("Type(0x04,0x01,0100000000)", RenderPath(Node(4, 1, new byte[] { 0x01, 0x00, 0x00, 0x00, 0x00 })));
    }

    [Fact]
    public void FullPath_JoinsNodesWithSlash_AndListsNodes()
    {
        DevicePathResult result = DevicePathFormatter.Format(PathOf(PciRoot(0), Pci(0x14, 0), Usb(3, 0)));

        Assert.Null(result.Error);
        Assert.Equal("PciRoot(0x0)/Pci(0x14,0x0)/USB(3,0)", result.Text);
        Assert.Equal(new[] { "PciRoot(0x0)", "Pci(0x14,0x0)", "USB(3,0)" }, result.Nodes);
    }

    [Fact]
    public void EndInstance_RendersAsComma()
    {
        DevicePathResult result = DevicePathFormatter.Format(PathOf(PciRoot(0), Pci(1, 0), EndInstance(), PciRoot(0), Pci(2, 0)));

        Assert.Null(result.Error);
        Assert.Equal("PciRoot(0x0)/Pci(0x1,0x0),PciRoot(0x0)/Pci(0x2,0x0)", result.Text);
        Assert.Equal(new[] { "PciRoot(0x0)", "Pci(0x1,0x0)", ",", "PciRoot(0x0)", "Pci(0x2,0x0)" }, result.Nodes);
    }

    [Fact]
    public void SecondPathInList_RendersAfterSemicolon()
    {
        byte[] list = Concat(PathOf(PciRoot(0), Pci(1, 0)), PathOf(FilePath(@"\x.efi")));
        DevicePathResult result = DevicePathFormatter.Format(list);

        Assert.Null(result.Error);
        Assert.Equal("PciRoot(0x0)/Pci(0x1,0x0);File(\\x.efi)", result.Text);
        Assert.Equal(new[] { "PciRoot(0x0)", "Pci(0x1,0x0)", ";", "File(\\x.efi)" }, result.Nodes);
    }

    [Fact]
    public void Empty_ReportsError()
    {
        DevicePathResult result = DevicePathFormatter.Format(ReadOnlySpan<byte>.Empty);

        Assert.NotNull(result.Error);
        Assert.Contains("empty", result.Error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, result.Text);
        Assert.Empty(result.Nodes);
    }

    [Fact]
    public void LengthUnderFour_ReportsErrorAndKeepsEarlierNodes()
    {
        byte[] path = Concat(PciRoot(0), RawNode(1, 1, 2, new byte[] { 0, 0x14 }), EndEntire());
        DevicePathResult result = DevicePathFormatter.Format(path);

        Assert.NotNull(result.Error);
        Assert.Contains("invalid length 2", result.Error, StringComparison.Ordinal);
        Assert.Contains("offset 12", result.Error, StringComparison.Ordinal);
        Assert.Equal("PciRoot(0x0)", result.Text);
        Assert.Single(result.Nodes);
    }

    [Fact]
    public void LengthBeyondBuffer_ReportsError()
    {
        byte[] path = Concat(PciRoot(0), RawNode(4, 4, 200, Utf16Z(@"\a.efi")));
        DevicePathResult result = DevicePathFormatter.Format(path);

        Assert.NotNull(result.Error);
        Assert.Contains("length 200", result.Error, StringComparison.Ordinal);
        Assert.Contains("exceeds", result.Error, StringComparison.Ordinal);
        Assert.Equal("PciRoot(0x0)", result.Text);
    }

    [Fact]
    public void TruncatedHeader_ReportsError()
    {
        byte[] path = Concat(PciRoot(0), new byte[] { 0x01, 0x01 });
        DevicePathResult result = DevicePathFormatter.Format(path);

        Assert.NotNull(result.Error);
        Assert.Contains("truncated", result.Error, StringComparison.Ordinal);
        Assert.Equal("PciRoot(0x0)", result.Text);
    }

    [Fact]
    public void MissingEndNode_ReportsError()
    {
        DevicePathResult result = DevicePathFormatter.Format(Concat(PciRoot(0), Pci(0x14, 0)));

        Assert.NotNull(result.Error);
        Assert.Contains("End node", result.Error, StringComparison.Ordinal);
        Assert.Equal("PciRoot(0x0)/Pci(0x14,0x0)", result.Text);
    }

    [Fact]
    public void PathEndingWithInstanceSeparator_ReportsMissingEndNode()
    {
        DevicePathResult result = DevicePathFormatter.Format(Concat(PciRoot(0), EndInstance()));

        Assert.NotNull(result.Error);
        Assert.Equal("PciRoot(0x0),", result.Text);
    }

    [Fact]
    public void Format_NeverThrows_ForEveryPrefixOfARealPath()
    {
        byte[] path = PathOf(PciRoot(0), Pci(0x14, 0), Usb(3, 0), HardDriveGpt(1, 0x800, 0x32000, EspSignature), FilePath(@"\EFI\BOOT\BOOTX64.EFI"));
        for (int length = 0; length <= path.Length; length++)
        {
            byte[] prefix = path[..length];
            Exception? exception = Record.Exception(() => DevicePathFormatter.Format(prefix));
            Assert.Null(exception);
        }
    }

    [Fact]
    public void Format_NeverThrows_ForRandomBytes()
    {
        Random random = new(20260910);
        for (int iteration = 0; iteration < 500; iteration++)
        {
            byte[] bytes = new byte[random.Next(0, 96)];
            random.NextBytes(bytes);
            Exception? exception = Record.Exception(() => DevicePathFormatter.Format(bytes));
            Assert.Null(exception);
        }
    }
}
