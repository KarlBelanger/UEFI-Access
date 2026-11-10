// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using UefiAccess.Core.Decoders;
using UefiAccess.Core.Tests.Safety;
using UefiAccess.Core.Variables;
using Xunit;
using static UefiAccess.Core.Tests.Fixtures.LoadOptionFixtures;

namespace UefiAccess.Core.Tests;

/// <summary>
/// Fixture-driven tests for <see cref="LoadOptionParser"/> and <see cref="LoadOptionDecoder"/>,
/// and for the structured <see cref="LoadOption.HardDriveNode"/> and <see cref="LoadOption.FilePath"/>
/// that rule V13 compares (section 3.1), read from the unit's own Boot0002 and
/// Boot0000 among others.
/// </summary>
public sealed class LoadOptionDecoderTests
{
    private static readonly Guid Global = KnownGuids.EfiGlobalVariable;

    private static FirmwareVariable BootVariable(string name, byte[] data) => new(name, Global, VariableAttributes.NonVolatile | VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess, data);

    [Fact]
    public void WindowsBootManager_ParsesEveryField()
    {
        LoadOption option = LoadOptionParser.Parse(WindowsBootManager());

        Assert.Null(option.Error);
        Assert.Equal(1u, option.Attributes);
        Assert.True(option.Active);
        Assert.False(option.ForceReconnect);
        Assert.False(option.Hidden);
        Assert.Equal("Boot", option.Category);
        Assert.Equal(WindowsBootManagerDescription, option.Description);
        Assert.Equal(WindowsBootManagerPath, option.DevicePath);
        Assert.Equal(new[] { "HD(1,GPT,6f6e5d1c-8f7a-4d33-9a2b-3c4d5e6f7081,0x800,0x32000)", "File(\\EFI\\Microsoft\\Boot\\bootmgfw.efi)" }, option.DevicePathNodes);
        Assert.Equal(WindowsOptionalData.Length, option.OptionalDataLength);
        Assert.Equal("WINDOWS", option.OptionalDataText);
    }

    [Fact]
    public void UsbStick_RendersPciRootPciUsb()
    {
        LoadOption option = LoadOptionParser.Parse(UsbStick());

        Assert.Null(option.Error);
        Assert.Equal(UsbStickDescription, option.Description);
        Assert.Equal(UsbStickPath, option.DevicePath);
        Assert.Equal(0, option.OptionalDataLength);
        Assert.Null(option.OptionalDataText);
    }

    [Fact]
    public void LegacyBbs_RendersBbsNode_AndShortOptionalDataHasNoText()
    {
        LoadOption option = LoadOptionParser.Parse(LegacyBbs());

        Assert.Null(option.Error);
        Assert.Equal(LegacyBbsDescription, option.Description);
        Assert.Equal(LegacyBbsPath, option.DevicePath);
        Assert.Equal(6, option.OptionalDataLength);
        Assert.Null(option.OptionalDataText);
    }

    [Fact]
    public void Pxe_RendersMacAndIPv4()
    {
        LoadOption option = LoadOptionParser.Parse(Pxe());

        Assert.Null(option.Error);
        Assert.Equal(PxeDescription, option.Description);
        Assert.Equal(PxePath, option.DevicePath);
    }

    [Fact]
    public void Truncated_ReportsErrorWithoutThrowing_AndKeepsWhatWasParsed()
    {
        LoadOption? option = null;
        Exception? exception = Record.Exception(() => option = LoadOptionParser.Parse(Truncated()));

        Assert.Null(exception);
        Assert.NotNull(option);
        Assert.NotNull(option.Error);
        Assert.Contains("FilePathListLength", option.Error, StringComparison.Ordinal);
        Assert.Contains("exceeds", option.Error, StringComparison.Ordinal);
        Assert.Equal(WindowsBootManagerDescription, option.Description);
        Assert.True(option.Active);
        Assert.Equal(0, option.OptionalDataLength);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    public void ShorterThanSixBytes_ReportsError(int length)
    {
        LoadOption option = LoadOptionParser.Parse(new byte[length]);

        Assert.NotNull(option.Error);
        Assert.Contains("minimum is 6", option.Error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, option.Description);
        Assert.Empty(option.DevicePathNodes);
    }

    [Fact]
    public void MissingDescriptionTerminator_ReportsErrorWithPartialDescription()
    {
        byte[] data = RawLoadOption(1, 4, Utf16("NoTerminator"), Array.Empty<byte>());
        LoadOption option = LoadOptionParser.Parse(data);

        Assert.NotNull(option.Error);
        Assert.Contains("null-terminated", option.Error, StringComparison.Ordinal);
        Assert.Equal("NoTerminator", option.Description);
        Assert.True(option.Active);
    }

    [Fact]
    public void FilePathListWithoutEndNode_ReportsError()
    {
        byte[] data = BuildLoadOption(1, "No end", Concat(PciRoot(0), Pci(1, 0)));
        LoadOption option = LoadOptionParser.Parse(data);

        Assert.NotNull(option.Error);
        Assert.Contains("End node", option.Error, StringComparison.Ordinal);
        Assert.Equal("PciRoot(0x0)/Pci(0x1,0x0)", option.DevicePath);
    }

    [Fact]
    public void EmptyFilePathList_ReportsError()
    {
        byte[] data = BuildLoadOption(1, "Empty", Array.Empty<byte>(), AsciiBytes("DATA"));
        LoadOption option = LoadOptionParser.Parse(data);

        Assert.NotNull(option.Error);
        Assert.Contains("empty", option.Error, StringComparison.Ordinal);
        Assert.Equal("Empty", option.Description);
        Assert.Equal(4, option.OptionalDataLength);
        Assert.Equal("DATA", option.OptionalDataText);
    }

    [Fact]
    public void MalformedNodeInsidePath_ReportsErrorAndKeepsEarlierNodes()
    {
        byte[] data = BuildLoadOption(1, "Bad node", Concat(PciRoot(0), RawNode(1, 1, 1), EndEntire()));
        LoadOption option = LoadOptionParser.Parse(data);

        Assert.NotNull(option.Error);
        Assert.Contains("invalid length", option.Error, StringComparison.Ordinal);
        Assert.Equal("PciRoot(0x0)", option.DevicePath);
    }

    [Theory]
    [InlineData(0x0u, false, false, false, "Boot")]
    [InlineData(0x1u, true, false, false, "Boot")]
    [InlineData(0x3u, true, true, false, "Boot")]
    [InlineData(0x9u, true, false, true, "Boot")]
    [InlineData(0x101u, true, false, false, "App")]
    [InlineData(0x201u, true, false, false, "0x200")]
    [InlineData(0x1F00u, false, false, false, "0x1f00")]
    public void Attributes_DecodeFlagsAndCategory(uint attributes, bool active, bool forceReconnect, bool hidden, string category)
    {
        LoadOption option = LoadOptionParser.Parse(BuildLoadOption(attributes, "Attrs", PathOf(PciRoot(0))));

        Assert.Null(option.Error);
        Assert.Equal(attributes, option.Attributes);
        Assert.Equal(active, option.Active);
        Assert.Equal(forceReconnect, option.ForceReconnect);
        Assert.Equal(hidden, option.Hidden);
        Assert.Equal(category, option.Category);
    }

    [Fact]
    public void OptionalDataText_Utf16Prefix()
    {
        byte[] optional = Concat(Utf16("BCDOBJECT=x"), new byte[] { 0xFF, 0xFE, 0x01 });
        LoadOption option = LoadOptionParser.Parse(BuildLoadOption(1, "Utf16", PathOf(PciRoot(0)), optional));

        Assert.Equal(optional.Length, option.OptionalDataLength);
        Assert.Equal("BCDOBJECT=x", option.OptionalDataText);
    }

    [Fact]
    public void OptionalDataText_FewerThanFourPrintableCharacters_IsNull()
    {
        LoadOption ascii = LoadOptionParser.Parse(BuildLoadOption(1, "Short", PathOf(PciRoot(0)), AsciiBytes("abc")));
        LoadOption utf16 = LoadOptionParser.Parse(BuildLoadOption(1, "Short", PathOf(PciRoot(0)), Utf16("abc")));
        LoadOption binary = LoadOptionParser.Parse(BuildLoadOption(1, "Binary", PathOf(PciRoot(0)), new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 }));

        Assert.Null(ascii.OptionalDataText);
        Assert.Equal(3, ascii.OptionalDataLength);
        Assert.Null(utf16.OptionalDataText);
        Assert.Null(binary.OptionalDataText);
        Assert.Equal(6, binary.OptionalDataLength);
    }

    [Fact]
    public void OptionalDataText_ExactlyFourAsciiCharacters_IsText()
    {
        LoadOption option = LoadOptionParser.Parse(BuildLoadOption(1, "Four", PathOf(PciRoot(0)), AsciiBytes("ABCD")));

        Assert.Equal("ABCD", option.OptionalDataText);
    }

    [Fact]
    public void Decoder_CanDecode_OnlyBootEntriesUnderTheGlobalGuid()
    {
        LoadOptionDecoder decoder = new();

        Assert.True(decoder.CanDecode(BootVariable("Boot0001", WindowsBootManager())));
        Assert.True(decoder.CanDecode(BootVariable("BootFFFF", Array.Empty<byte>())));
        Assert.False(decoder.CanDecode(BootVariable("BootOrder", new byte[] { 1, 0 })));
        Assert.False(decoder.CanDecode(BootVariable("boot0001", WindowsBootManager())));
        Assert.False(decoder.CanDecode(BootVariable("Boot001", WindowsBootManager())));
        Assert.False(decoder.CanDecode(new FirmwareVariable("Boot0001", KnownGuids.EfiImageSecurityDatabase, VariableAttributes.RuntimeAccess, WindowsBootManager())));
    }

    [Fact]
    public void Decoder_Decode_ProducesKindFieldsSummaryAndJson()
    {
        FirmwareVariable variable = BootVariable("Boot0001", WindowsBootManager());
        DecodedVariable decoded = new LoadOptionDecoder().Decode(variable, VariableSet.Empty);

        Assert.Equal(variable.Key, decoded.Key);
        Assert.Equal("LoadOption", decoded.Kind);
        Assert.Null(decoded.Error);
        Assert.NotNull(decoded.Json);
        Assert.StartsWith("Boot0001: Windows Boot Manager; active, category Boot; " + WindowsBootManagerPath, decoded.Summary, StringComparison.Ordinal);
        Assert.Contains(decoded.Fields, f => f.Name == "Description" && f.Value == WindowsBootManagerDescription);
        Assert.Contains(decoded.Fields, f => f.Name == "Attributes" && f.Value == "0x00000001 (active, category Boot)");
        Assert.Contains(decoded.Fields, f => f.Name == "Device path" && f.Value == WindowsBootManagerPath);
        Assert.Contains(decoded.Fields, f => f.Name == "Optional data" && f.Value == "15 bytes, text \"WINDOWS\"");
        Assert.DoesNotContain(decoded.Fields, f => f.Name == "Error");
    }

    [Fact]
    public void Decoder_Decode_Truncated_SetsErrorAndKeepsDescription()
    {
        DecodedVariable decoded = new LoadOptionDecoder().Decode(BootVariable("Boot0002", Truncated()), VariableSet.Empty);

        Assert.Equal("LoadOption", decoded.Kind);
        Assert.NotNull(decoded.Error);
        Assert.Contains("malformed", decoded.Summary, StringComparison.Ordinal);
        Assert.Contains("Windows Boot Manager", decoded.Summary, StringComparison.Ordinal);
        Assert.Contains(decoded.Fields, f => f.Name == "Error");
    }

    [Fact]
    public void Decoder_Decode_HiddenInactiveAppEntry_DescribesAttributes()
    {
        DecodedVariable decoded = new LoadOptionDecoder().Decode(BootVariable("Boot0003", BuildLoadOption(0x108, "Shell", PathOf(FvFile(EspSignature)))), VariableSet.Empty);

        Assert.Null(decoded.Error);
        Assert.Equal("Boot0003: Shell; inactive, hidden, category App; FvFile(6f6e5d1c-8f7a-4d33-9a2b-3c4d5e6f7081)", decoded.Summary);
    }

    [Fact]
    public void Decoder_Decode_NullData_ReportsErrorWithoutThrowing()
    {
        FirmwareVariable variable = new("Boot0004", Global, VariableAttributes.RuntimeAccess, null!);
        DecodedVariable? decoded = null;
        Exception? exception = Record.Exception(() => decoded = new LoadOptionDecoder().Decode(variable, VariableSet.Empty));

        Assert.Null(exception);
        Assert.NotNull(decoded);
        Assert.NotNull(decoded.Error);
    }

    [Fact]
    public void Parser_NeverThrows_ForEveryPrefixOfEveryFixture()
    {
        byte[][] fixtures = { WindowsBootManager(), UsbStick(), LegacyBbs(), Pxe() };
        foreach (byte[] fixture in fixtures)
        {
            for (int length = 0; length <= fixture.Length; length++)
            {
                byte[] prefix = fixture[..length];
                Exception? exception = Record.Exception(() => LoadOptionParser.Parse(prefix));
                Assert.Null(exception);
            }
        }
    }

    [Fact]
    public void Parser_NeverThrows_ForRandomBytes()
    {
        Random random = new(7020);
        for (int iteration = 0; iteration < 500; iteration++)
        {
            byte[] bytes = new byte[random.Next(0, 128)];
            random.NextBytes(bytes);
            Exception? exception = Record.Exception(() => LoadOptionParser.Parse(bytes));
            Assert.Null(exception);
        }
    }

    // ---- the structured nodes ------------------------------------

    [Fact]
    public void UnitBoot0002_HasTheSystemPartitionNodeAndTheBootManagerFile()
    {
        LoadOption option = LoadOptionParser.Parse(SafetyFixtures.UnitBoot0002());

        Assert.Null(option.Error);
        Assert.Equal(WindowsBootManagerDescription, option.Description);
        Assert.Equal(@"HD(1,GPT,45d24df4-90d6-4580-9dd9-75a19f842203,0x800,0x32000)/File(\EFI\Microsoft\Boot\bootmgfw.efi)", option.DevicePath);
        Assert.Equal("WINDOWS", option.OptionalDataText);
        Assert.Equal(136, option.OptionalDataLength);

        HardDriveNode node = Assert.IsType<HardDriveNode>(option.HardDriveNode);
        Assert.Equal(1u, node.PartitionNumber);
        Assert.Equal(HardDriveNode.GuidSignatureType, node.SignatureType);
        Assert.Equal(SafetyFixtures.UnitEspSignature, node.Signature);
        Assert.True(node.HasSignature);
        Assert.Equal(@"\EFI\Microsoft\Boot\bootmgfw.efi", option.FilePath);
    }

    [Fact]
    public void UnitBoot0000_HasPartitionTwoAndTheSameFile()
    {
        LoadOption option = LoadOptionParser.Parse(SafetyFixtures.UnitBoot0000());

        Assert.Null(option.Error);
        Assert.Equal(@"HD(2,GPT,3ee6207e-24b9-4724-899e-ffc5da70d55d,0x96800,0x32000)/File(\EFI\Microsoft\Boot\bootmgfw.efi)", option.DevicePath);
        Assert.Equal(new HardDriveNode(2, HardDriveNode.GuidSignatureType, SafetyFixtures.UnitSecondPartitionSignature), option.HardDriveNode);
        Assert.Equal(@"\EFI\Microsoft\Boot\bootmgfw.efi", option.FilePath);
    }

    [Fact]
    public void UnitEntries_DifferOnlyInTheirPartition()
    {
        LoadOption current = LoadOptionParser.Parse(SafetyFixtures.UnitBoot0002());
        LoadOption other = LoadOptionParser.Parse(SafetyFixtures.UnitBoot0000());
        HardDriveNode currentNode = Assert.IsType<HardDriveNode>(current.HardDriveNode);
        HardDriveNode otherNode = Assert.IsType<HardDriveNode>(other.HardDriveNode);

        Assert.Equal(current.FilePath, other.FilePath);
        Assert.Equal(current.Description, other.Description);
        Assert.Equal(currentNode.SignatureType, otherNode.SignatureType);
        Assert.NotEqual(currentNode.Signature, otherNode.Signature);
        Assert.NotEqual(currentNode.PartitionNumber, otherNode.PartitionNumber);
    }

    [Theory]
    [MemberData(nameof(NodeLessPaths))]
    public void NodeLessPath_HasNoHardDriveNodeAndNoFile(byte[] data)
    {
        LoadOption option = LoadOptionParser.Parse(data);

        Assert.Null(option.Error);
        Assert.Null(option.HardDriveNode);
        Assert.Null(option.FilePath);
    }

    public static TheoryData<byte[]> NodeLessPaths() => new()
    {
        UsbStick(),
        Pxe(),
        LegacyBbs(),
        BuildLoadOption(0x101, "Shell", PathOf(FvFile(EspSignature))),
    };

    [Fact]
    public void FileOnlyPath_HasAFileButNoHardDriveNode()
    {
        LoadOption option = LoadOptionParser.Parse(BuildLoadOption(1, "Short form", PathOf(FilePath(@"\EFI\Microsoft\Boot\bootmgfw.efi"))));

        Assert.Null(option.HardDriveNode);
        Assert.Equal(@"\EFI\Microsoft\Boot\bootmgfw.efi", option.FilePath);
    }

    [Fact]
    public void PartitionOnlyPath_HasAHardDriveNodeButNoFile()
    {
        LoadOption option = LoadOptionParser.Parse(BuildLoadOption(1, "Partition", PathOf(HardDriveGpt(2, 0x96800, 0x32000, EspSignature))));

        Assert.Equal(new HardDriveNode(2, HardDriveNode.GuidSignatureType, EspSignature), option.HardDriveNode);
        Assert.Null(option.FilePath);
    }

    [Fact]
    public void HardDriveNodeBehindAUsbController_IsStillRead()
    {
        LoadOption option = LoadOptionParser.Parse(BuildLoadOption(1, "Stick partition", PathOf(PciRoot(0), Pci(0x14, 0), Usb(3, 0), HardDriveGpt(1, 0x800, 0x32000, EspSignature), FilePath(@"\EFI\BOOT\BOOTX64.EFI"))));

        Assert.Equal(1u, option.HardDriveNode!.PartitionNumber);
        Assert.Equal(@"\EFI\BOOT\BOOTX64.EFI", option.FilePath);
        Assert.True(option.HasRemovableMessagingNode);
    }

    [Theory]
    [InlineData(0x05, true)]
    [InlineData(0x0F, true)]
    [InlineData(0x10, true)]
    [InlineData(0x1A, true)]
    [InlineData(0x1D, true)]
    [InlineData(0x12, false)]
    [InlineData(0x17, false)]
    public void RemovableMessagingNodes_AreUsbSdAndEmmc_NotSataOrNvme(byte subType, bool removable)
    {
        byte[] path = PathOf(PciRoot(0), Pci(0x14, 0), Node(3, subType, new byte[] { 1, 0, 0, 0 }), HardDriveGpt(1, 0x800, 0x32000, EspSignature), FilePath(@"\EFI\tools\tool.efi"));

        LoadOption option = LoadOptionParser.Parse(BuildLoadOption(1, "Entry", path));

        Assert.Equal(removable, option.HasRemovableMessagingNode);
        Assert.NotNull(option.HardDriveNode);
    }

    [Fact]
    public void TheUnitsFixedDiskEntriesAndThePxeEntry_AreNotRemovable_WhileTheStickIs()
    {
        Assert.False(LoadOptionParser.Parse(SafetyFixtures.UnitBoot0002()).HasRemovableMessagingNode);
        Assert.False(LoadOptionParser.Parse(SafetyFixtures.UnitBoot0000()).HasRemovableMessagingNode);
        Assert.True(LoadOptionParser.Parse(UsbStick()).HasRemovableMessagingNode);
        Assert.False(LoadOptionParser.Parse(Pxe()).HasRemovableMessagingNode);
    }

    [Fact]
    public void MbrNode_CarriesTheDiskSignatureInTheFirstFourBytes()
    {
        LoadOption option = LoadOptionParser.Parse(BuildLoadOption(1, "MBR", PathOf(HardDriveMbr(1, 0x800, 0x1000, 0x12345678), FilePath(@"\EFI\Boot\bootx64.efi"))));

        HardDriveNode node = Assert.IsType<HardDriveNode>(option.HardDriveNode);
        Assert.Equal(HardDriveNode.MbrSignatureType, node.SignatureType);
        Assert.True(node.HasSignature);
        byte[] expected = new byte[16];
        U32(0x12345678).CopyTo(expected, 0);
        Assert.Equal(new Guid(expected), node.Signature);
        Assert.StartsWith("HD(1,MBR,0x12345678,", option.DevicePath, StringComparison.Ordinal);
    }

    [Fact]
    public void NodeWithoutSignature_HasNoSignatureToCompare()
    {
        LoadOption option = LoadOptionParser.Parse(BuildLoadOption(1, "No signature", PathOf(HardDrive(1, 0x800, 0x1000, new byte[16], 2, 0), FilePath(@"\EFI\Boot\bootx64.efi"))));

        HardDriveNode node = Assert.IsType<HardDriveNode>(option.HardDriveNode);
        Assert.Equal((byte)0, node.SignatureType);
        Assert.False(node.HasSignature);
    }

    [Fact]
    public void ShortHardDrivePayload_IsNotReadAsANode_AndRendersAsUnknown()
    {
        byte[] shortNode = Node(4, 1, U32(1), U64(0x800));
        LoadOption option = LoadOptionParser.Parse(BuildLoadOption(1, "Short node", PathOf(shortNode, FilePath(@"\EFI\x.efi"))));

        Assert.Null(option.HardDriveNode);
        Assert.Equal(@"\EFI\x.efi", option.FilePath);
        Assert.StartsWith("Type(0x04,0x01,", option.DevicePath, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"\EFI\Microsoft", @"Boot\bootmgfw.efi", @"\EFI\Microsoft\Boot\bootmgfw.efi")]
    [InlineData(@"\EFI\Microsoft\", @"\Boot\bootmgfw.efi", @"\EFI\Microsoft\Boot\bootmgfw.efi")]
    [InlineData(@"\EFI\Microsoft\", @"Boot\bootmgfw.efi", @"\EFI\Microsoft\Boot\bootmgfw.efi")]
    [InlineData(@"\EFI", @"\Boot\bootx64.efi", @"\EFI\Boot\bootx64.efi")]
    public void SplitFilePath_IsJoinedWithOneSeparator(string first, string second, string expected)
    {
        LoadOption option = LoadOptionParser.Parse(BuildLoadOption(1, "Split", PathOf(HardDriveGpt(1, 0x800, 0x32000, EspSignature), FilePath(first), FilePath(second))));

        Assert.Null(option.Error);
        Assert.Equal(expected, option.FilePath);
    }

    [Fact]
    public void OnlyTheFirstDevicePathIsRead_AcrossAnInstanceEnd()
    {
        byte[] list = Concat(
            HardDriveGpt(1, 0x800, 0x32000, EspSignature),
            FilePath(@"\EFI\first.efi"),
            EndInstance(),
            HardDriveGpt(2, 0x96800, 0x32000, SafetyFixtures.UnitSecondPartitionSignature),
            FilePath(@"\EFI\second.efi"),
            EndEntire());

        LoadOption option = LoadOptionParser.Parse(BuildLoadOption(1, "Two instances", list));

        Assert.Null(option.Error);
        Assert.Equal(1u, option.HardDriveNode!.PartitionNumber);
        Assert.Equal(@"\EFI\first.efi", option.FilePath);
    }

    [Fact]
    public void OnlyTheFirstDevicePathIsRead_AcrossASecondPath()
    {
        byte[] list = Concat(
            PathOf(PciRoot(0), Pci(0x14, 0), Usb(3, 0)),
            PathOf(HardDriveGpt(2, 0x96800, 0x32000, EspSignature), FilePath(@"\initrd.img")));

        LoadOption option = LoadOptionParser.Parse(BuildLoadOption(1, "Two paths", list));

        Assert.Null(option.Error);
        Assert.Null(option.HardDriveNode);
        Assert.Null(option.FilePath);
        Assert.Contains(";", option.DevicePath, StringComparison.Ordinal);
    }

    [Fact]
    public void TruncatedOption_HasNoStructuredNodes()
    {
        LoadOption option = LoadOptionParser.Parse(Truncated());

        Assert.NotNull(option.Error);
        Assert.Null(option.HardDriveNode);
        Assert.Null(option.FilePath);
    }

    [Fact]
    public void OptionTooShortToHaveAPath_HasNoStructuredNodes()
    {
        Assert.Null(LoadOptionParser.Parse(new byte[4]).HardDriveNode);
        Assert.Null(LoadOptionParser.Parse(RawLoadOption(1, 4, Utf16("NoTerminator"), Array.Empty<byte>())).FilePath);
    }

    [Fact]
    public void FilePath_KeepsTheSpellingItWasStoredWith()
    {
        LoadOption option = LoadOptionParser.Parse(BuildLoadOption(1, "Capitals", PathOf(HardDriveGpt(1, 0x800, 0x32000, EspSignature), FilePath(@"\EFI\MICROSOFT\BOOT\BOOTMGFW.EFI"))));

        Assert.Equal(@"\EFI\MICROSOFT\BOOT\BOOTMGFW.EFI", option.FilePath);
        Assert.Equal(@"\EFI\Microsoft\Boot\bootmgfw.efi", option.FilePath, ignoreCase: true);
    }

    [Fact]
    public void DevicePathTextAndNodes_AreUnchangedByTheStructuredRead()
    {
        byte[] path = PathOf(HardDriveGpt(1, 0x800, 0x32000, EspSignature), FilePath(@"\EFI\Microsoft\Boot\bootmgfw.efi"));
        DevicePathResult rendered = DevicePathFormatter.Format(path);

        LoadOption option = LoadOptionParser.Parse(BuildLoadOption(1, WindowsBootManagerDescription, path, WindowsOptionalData));

        Assert.Equal(rendered.Text, option.DevicePath);
        Assert.Equal(rendered.Nodes, option.DevicePathNodes);
        Assert.Equal(WindowsBootManagerPath, option.DevicePath);
    }

    [Fact]
    public void Decoder_OutputForTheCliAndTheApplication_IsUnchanged()
    {
        DecodedVariable decoded = new LoadOptionDecoder().Decode(BootVariable("Boot0002", SafetyFixtures.UnitBoot0002()), VariableSet.Empty);

        JsonElement json = JsonSerializer.SerializeToElement(decoded.Json);
        string[] properties = json.EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(
            new[] { "Name", "Index", "Attributes", "Active", "ForceReconnect", "Hidden", "Category", "Description", "DevicePath", "DevicePathNodes", "OptionalDataLength", "OptionalDataText", "Error" },
            properties);
        Assert.Equal(new[] { "Description", "Attributes", "Category", "Device path", "Optional data" }, decoded.Fields.Select(field => field.Name));
        Assert.Equal(
            @"Boot0002: Windows Boot Manager; active, category Boot; HD(1,GPT,45d24df4-90d6-4580-9dd9-75a19f842203,0x800,0x32000)/File(\EFI\Microsoft\Boot\bootmgfw.efi)",
            decoded.Summary);
    }
}
