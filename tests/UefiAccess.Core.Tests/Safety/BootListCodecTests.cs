// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Decoders;
using UefiAccess.Core.Safety;
using Xunit;
using static UefiAccess.Core.Tests.Fixtures.LoadOptionFixtures;

namespace UefiAccess.Core.Tests.Safety;

/// <summary>The one codec between entry numbers and BootOrder/BootNext bytes: round trip, odd length, empty.</summary>
public sealed class BootListCodecTests
{
    [Fact]
    public void Encode_WritesLittleEndianUInt16Entries()
    {
        byte[] bytes = BootListCodec.Encode(new ushort[] { 0x0002, 0x0100, 0x1A2B });

        Assert.Equal(new byte[] { 0x02, 0x00, 0x00, 0x01, 0x2B, 0x1A }, bytes);
    }

    [Fact]
    public void Encode_TheUnitsOneEntryBootOrder_IsTheTwoBytesTheBaselineHolds()
    {
        Assert.Equal(new byte[] { 0x02, 0x00 }, BootListCodec.Encode(new ushort[] { 0x0002 }));
        Assert.Equal(BootListCodec.EntrySize, BootListCodec.Encode(new ushort[] { 0x0002 }).Length);
    }

    [Fact]
    public void Encode_ReturnsBytesThatALaterChangeToTheListDoesNotTouch()
    {
        ushort[] entries = { 3, 2 };
        byte[] bytes = BootListCodec.Encode(entries);
        entries[0] = 0;

        Assert.Equal(new byte[] { 0x03, 0x00, 0x02, 0x00 }, bytes);
    }

    [Fact]
    public void Encode_NoEntries_IsNoBytes()
    {
        Assert.Empty(BootListCodec.Encode(Array.Empty<ushort>()));
    }

    [Fact]
    public void Encode_NullEntries_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => BootListCodec.Encode(null!));
    }

    [Fact]
    public void Decode_ThenEncode_RoundTrips()
    {
        byte[] original = Concat(U16(2), U16(3), U16(0), U16(0xFFFF));

        DecodeResult decoded = BootListCodec.Decode(original);

        Assert.Equal(new ushort[] { 2, 3, 0, 0xFFFF }, decoded.Entries);
        Assert.Equal(8, decoded.ByteCount);
        Assert.False(decoded.HasOddByteCount);
        Assert.False(decoded.IsEmpty);
        Assert.Equal(original, BootListCodec.Encode(decoded.Entries));
    }

    [Fact]
    public void Encode_ThenDecode_RoundTripsEveryPermutationOfThreeEntries()
    {
        ushort[][] orders =
        {
            new ushort[] { 2, 3, 0 }, new ushort[] { 2, 0, 3 }, new ushort[] { 3, 2, 0 },
            new ushort[] { 3, 0, 2 }, new ushort[] { 0, 2, 3 }, new ushort[] { 0, 3, 2 },
        };
        foreach (ushort[] order in orders)
        {
            byte[] bytes = BootListCodec.Encode(order);
            Assert.Equal(6, bytes.Length);
            Assert.Equal(order, BootListCodec.Decode(bytes).Entries);
        }
    }

    [Fact]
    public void Decode_OddLength_KeepsTheByteCountAndDropsTheTrailingByte()
    {
        DecodeResult decoded = BootListCodec.Decode(new byte[] { 2, 0, 3 });

        Assert.Equal(new ushort[] { 2 }, decoded.Entries);
        Assert.Equal(3, decoded.ByteCount);
        Assert.True(decoded.HasOddByteCount);
        Assert.False(decoded.IsEmpty);
    }

    [Fact]
    public void Decode_SingleByte_HasNoEntryAndAnOddCount()
    {
        DecodeResult decoded = BootListCodec.Decode(new byte[] { 7 });

        Assert.Empty(decoded.Entries);
        Assert.Equal(1, decoded.ByteCount);
        Assert.True(decoded.HasOddByteCount);
    }

    [Fact]
    public void Decode_Empty_IsEmpty()
    {
        DecodeResult decoded = BootListCodec.Decode(Array.Empty<byte>());

        Assert.Empty(decoded.Entries);
        Assert.Equal(0, decoded.ByteCount);
        Assert.True(decoded.IsEmpty);
        Assert.False(decoded.HasOddByteCount);
    }

    [Fact]
    public void Decode_Null_ReadsAsEmptyWithoutThrowing()
    {
        DecodeResult? decoded = null;
        Exception? exception = Record.Exception(() => decoded = BootListCodec.Decode(null));

        Assert.Null(exception);
        Assert.NotNull(decoded);
        Assert.True(decoded.IsEmpty);
    }

    [Fact]
    public void Names_RenderFourUppercaseHexDigits()
    {
        DecodeResult decoded = BootListCodec.Decode(Concat(U16(0x1A2B), U16(0), U16(2)));

        Assert.Equal(new[] { "Boot1A2B", "Boot0000", "Boot0002" }, decoded.Names);
    }

    [Fact]
    public void Decode_ReadsThroughTheDecodersParser_SoBothSidesAgree()
    {
        byte[] bytes = Concat(U16(2), U16(3), U16(0x1A2B));

        Assert.Equal(BootOrderDecoder.ReadUInt16Array(bytes), BootListCodec.Decode(bytes).Entries);
        Assert.Equal(BootOrderDecoder.ReadUInt16Array(new byte[] { 2, 0, 3 }), BootListCodec.Decode(new byte[] { 2, 0, 3 }).Entries);
    }
}
