// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Decoders;
using UefiAccess.Core.Variables;
using Xunit;
using static UefiAccess.Core.Tests.Fixtures.LoadOptionFixtures;

namespace UefiAccess.Core.Tests;

/// <summary>Tests for <see cref="BootOrderDecoder"/>: BootOrder, BootNext, BootCurrent and Timeout.</summary>
public sealed class BootOrderDecoderTests
{
    private static readonly Guid Global = KnownGuids.EfiGlobalVariable;

    private static FirmwareVariable Var(string name, byte[] data) => new(name, Global, VariableAttributes.NonVolatile | VariableAttributes.RuntimeAccess, data);

    private static VariableSet Snapshot() => new(new[]
    {
        Var("Boot0001", WindowsBootManager()),
        Var("Boot0002", UsbStick()),
        Var("Boot0004", new byte[] { 1, 0, 0, 0, 0, 0, 0x41 }),
        Var("BootOrder", Concat(U16(1), U16(2), U16(3))),
        Var("BootNext", U16(2)),
        Var("BootCurrent", U16(1)),
        Var("Timeout", U16(5)),
    });

    [Fact]
    public void CanDecode_AcceptsTheFourNamesUnderTheGlobalGuidOnly()
    {
        BootOrderDecoder decoder = new();

        Assert.True(decoder.CanDecode(Var("BootOrder", Array.Empty<byte>())));
        Assert.True(decoder.CanDecode(Var("BootNext", Array.Empty<byte>())));
        Assert.True(decoder.CanDecode(Var("BootCurrent", Array.Empty<byte>())));
        Assert.True(decoder.CanDecode(Var("Timeout", Array.Empty<byte>())));
        Assert.False(decoder.CanDecode(Var("Boot0001", Array.Empty<byte>())));
        Assert.False(decoder.CanDecode(Var("bootorder", Array.Empty<byte>())));
        Assert.False(decoder.CanDecode(new FirmwareVariable("BootOrder", KnownGuids.EfiImageSecurityDatabase, VariableAttributes.RuntimeAccess, Array.Empty<byte>())));
    }

    [Fact]
    public void BootOrder_ResolvesDescriptionsAndFlagsMissingEntries()
    {
        VariableSet set = Snapshot();
        DecodedVariable decoded = new BootOrderDecoder().Decode(set.Find("BootOrder", Global)!, set);

        Assert.Equal("BootOrder", decoded.Kind);
        Assert.Null(decoded.Error);
        Assert.Equal(
            "BootOrder: Boot0001 (Windows Boot Manager), Boot0002 (UEFI: SanDisk Cruzer), Boot0003 (missing); 1 missing entry: Boot0003",
            decoded.Summary);
        Assert.Equal(new[] { "Boot0001", "Boot0002", "Boot0003" }, decoded.Fields.Select(f => f.Name));
        Assert.Equal("Windows Boot Manager", decoded.Fields[0].Value);
        Assert.Equal("UEFI: SanDisk Cruzer", decoded.Fields[1].Value);
        Assert.Contains("missing", decoded.Fields[2].Value, StringComparison.Ordinal);
        Assert.NotNull(decoded.Json);
    }

    [Fact]
    public void BootOrder_MalformedTarget_IsDescribedAsMalformed()
    {
        VariableSet set = Snapshot();
        DecodedVariable decoded = new BootOrderDecoder().Decode(Var("BootOrder", U16(4)), set);

        Assert.Null(decoded.Error);
        Assert.Equal("BootOrder: Boot0004 ((malformed load option))", decoded.Summary);
    }

    [Fact]
    public void BootOrder_Empty_IsValid()
    {
        DecodedVariable decoded = new BootOrderDecoder().Decode(Var("BootOrder", Array.Empty<byte>()), Snapshot());

        Assert.Null(decoded.Error);
        Assert.Equal("BootOrder is empty", decoded.Summary);
        Assert.Empty(decoded.Fields);
    }

    [Fact]
    public void BootOrder_OddLength_ReportsErrorAndDecodesThePairs()
    {
        DecodedVariable decoded = new BootOrderDecoder().Decode(Var("BootOrder", new byte[] { 1, 0, 2 }), Snapshot());

        Assert.NotNull(decoded.Error);
        Assert.Contains("not a multiple of 2", decoded.Error, StringComparison.Ordinal);
        Assert.Contains("Boot0001 (Windows Boot Manager)", decoded.Summary, StringComparison.Ordinal);
        Assert.Contains("malformed", decoded.Summary, StringComparison.Ordinal);
        Assert.Contains(decoded.Fields, f => f.Name == "Error");
    }

    [Fact]
    public void BootOrder_NullLookup_MarksEverythingMissingWithoutThrowing()
    {
        DecodedVariable? decoded = null;
        Exception? exception = Record.Exception(() => decoded = new BootOrderDecoder().Decode(Var("BootOrder", U16(1)), null!));

        Assert.Null(exception);
        Assert.NotNull(decoded);
        Assert.Null(decoded.Error);
        Assert.Contains("Boot0001 (missing)", decoded.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void BootOrder_RendersIndexesAsFourUppercaseHexDigits()
    {
        DecodedVariable decoded = new BootOrderDecoder().Decode(Var("BootOrder", Concat(U16(0x1A2B), U16(0xFFFF))), VariableSet.Empty);

        Assert.Equal("BootOrder: Boot1A2B (missing), BootFFFF (missing); 2 missing entries: Boot1A2B, BootFFFF", decoded.Summary);
    }

    [Fact]
    public void BootNext_And_BootCurrent_ResolveSingleEntry()
    {
        VariableSet set = Snapshot();
        DecodedVariable next = new BootOrderDecoder().Decode(set.Find("BootNext", Global)!, set);
        DecodedVariable current = new BootOrderDecoder().Decode(set.Find("BootCurrent", Global)!, set);

        Assert.Null(next.Error);
        Assert.Equal("BootNext: Boot0002 (UEFI: SanDisk Cruzer)", next.Summary);
        Assert.Null(current.Error);
        Assert.Equal("BootCurrent: Boot0001 (Windows Boot Manager)", current.Summary);
    }

    [Fact]
    public void BootNext_WrongLength_ReportsError()
    {
        DecodedVariable decoded = new BootOrderDecoder().Decode(Var("BootNext", Concat(U16(1), U16(2))), Snapshot());

        Assert.NotNull(decoded.Error);
        Assert.Contains("expected 2", decoded.Error, StringComparison.Ordinal);
        Assert.Contains("Boot0001", decoded.Summary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(5, "Timeout: 5 seconds (delay before booting the default entry)")]
    [InlineData(1, "Timeout: 1 second (delay before booting the default entry)")]
    [InlineData(0, "Timeout: 0 seconds (boot the default entry immediately)")]
    [InlineData(0xFFFF, "Timeout: 65535 seconds (wait for the user indefinitely)")]
    public void Timeout_DecodesSeconds(int seconds, string expectedSummary)
    {
        DecodedVariable decoded = new BootOrderDecoder().Decode(Var("Timeout", U16((ushort)seconds)), VariableSet.Empty);

        Assert.Equal("BootOrder", decoded.Kind);
        Assert.Null(decoded.Error);
        Assert.Equal(expectedSummary, decoded.Summary);
        Assert.Contains(decoded.Fields, f => f.Name == "Seconds" && f.Value == seconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Timeout_TooShort_ReportsError()
    {
        DecodedVariable decoded = new BootOrderDecoder().Decode(Var("Timeout", new byte[] { 5 }), VariableSet.Empty);

        Assert.NotNull(decoded.Error);
        Assert.Contains("expected 2", decoded.Error, StringComparison.Ordinal);
        Assert.Contains("malformed", decoded.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Timeout_TooLong_DecodesFirstTwoBytesWithError()
    {
        DecodedVariable decoded = new BootOrderDecoder().Decode(Var("Timeout", new byte[] { 7, 0, 0, 0 }), VariableSet.Empty);

        Assert.NotNull(decoded.Error);
        Assert.StartsWith("Timeout: 7 seconds", decoded.Summary, StringComparison.Ordinal);
    }
}
