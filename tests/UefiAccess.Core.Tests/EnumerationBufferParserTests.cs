// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Buffers.Binary;
using UefiAccess.Core.Tests.Fixtures;
using UefiAccess.Core.Variables;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>Tests for the pure VARIABLE_NAME_AND_VALUE buffer parser.</summary>
public sealed class EnumerationBufferParserTests
{
    [Fact]
    public void ThreeEntries_ParseCompletely()
    {
        byte[] buffer = EnumerationBuffers.ThreeEntries();

        ParsedEnumeration result = EnumerationBufferParser.Parse(buffer);

        Assert.Null(result.Error);
        Assert.Equal(3, result.EntriesParsed);
        Assert.Equal(3, result.Variables.Count);

        FirmwareVariable bootOrder = result.Variables[0];
        Assert.Equal("BootOrder", bootOrder.Name);
        Assert.Equal(KnownGuids.EfiGlobalVariable, bootOrder.VendorGuid);
        Assert.Equal(VariableAttributes.NonVolatile | VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess, bootOrder.Attributes);
        Assert.Equal(EnumerationBuffers.BootOrderValue, bootOrder.Data);
        Assert.Equal("8be4df61-93ca-11d2-aa0d-00e098032b8c:BootOrder", bootOrder.Key);

        FirmwareVariable japanese = result.Variables[1];
        Assert.Equal(EnumerationBuffers.JapaneseName, japanese.Name);
        Assert.Equal(EnumerationBuffers.VendorGuid, japanese.VendorGuid);
        Assert.Equal(VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess, japanese.Attributes);
        Assert.Empty(japanese.Data);
        Assert.Equal(0, japanese.Size);

        FirmwareVariable dbx = result.Variables[2];
        Assert.Equal("dbx", dbx.Name);
        Assert.Equal(KnownGuids.EfiImageSecurityDatabase, dbx.VendorGuid);
        Assert.Equal(
            VariableAttributes.NonVolatile | VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess | VariableAttributes.TimeBasedAuthenticatedWriteAccess,
            dbx.Attributes);
        Assert.Equal(EnumerationBuffers.DbxValue, dbx.Data);
    }

    [Fact]
    public void ThreeEntries_FixtureChainsNextEntryOffsets_AndLastIsZero()
    {
        byte[] buffer = EnumerationBuffers.ThreeEntries();

        uint first = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(0));
        Assert.True(first >= EnumerationBuffers.HeaderSize);
        uint second = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan((int)first));
        Assert.True(second >= EnumerationBuffers.HeaderSize);
        uint third = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan((int)(first + second)));
        Assert.Equal(0u, third);
        Assert.True(first + second < buffer.Length);
    }

    [Fact]
    public void CorruptedValueOffset_KeepsGoodEntries_AndReportsError()
    {
        byte[] buffer = EnumerationBuffers.CorruptedValueOffset();

        ParsedEnumeration result = EnumerationBufferParser.Parse(buffer);

        Assert.NotNull(result.Error);
        Assert.Contains("entry index 1", result.Error, StringComparison.Ordinal);
        Assert.Contains("ValueOffset", result.Error, StringComparison.Ordinal);
        Assert.Equal(1, result.EntriesParsed);
        FirmwareVariable only = Assert.Single(result.Variables);
        Assert.Equal("BootOrder", only.Name);
        Assert.Equal(EnumerationBuffers.BootOrderValue, only.Data);
    }

    [Fact]
    public void EmptyBuffer_YieldsNoEntriesAndNoError()
    {
        ParsedEnumeration result = EnumerationBufferParser.Parse(ReadOnlySpan<byte>.Empty);

        Assert.Null(result.Error);
        Assert.Equal(0, result.EntriesParsed);
        Assert.Empty(result.Variables);
    }

    [Fact]
    public void TruncatedHeader_ReportsErrorForEntryZero()
    {
        byte[] buffer = EnumerationBuffers.ThreeEntries().AsSpan(0, 20).ToArray();

        ParsedEnumeration result = EnumerationBufferParser.Parse(buffer);

        Assert.NotNull(result.Error);
        Assert.Contains("entry index 0", result.Error, StringComparison.Ordinal);
        Assert.Contains("header truncated", result.Error, StringComparison.Ordinal);
        Assert.Empty(result.Variables);
    }

    [Fact]
    public void TrailingBytesAfterLastEntry_AreIgnored()
    {
        byte[] buffer = EnumerationBuffers.Concat(EnumerationBuffers.ThreeEntries(), new byte[] { 0xFF, 0xFF, 0xFF });

        ParsedEnumeration result = EnumerationBufferParser.Parse(buffer);

        Assert.Null(result.Error);
        Assert.Equal(3, result.EntriesParsed);
    }

    [Fact]
    public void NextEntryOffsetSmallerThanHeader_StopsWithError()
    {
        byte[] buffer = EnumerationBuffers.Concat(
            EnumerationBuffers.Entry("BootOrder", KnownGuids.EfiGlobalVariable, 0x7, EnumerationBuffers.BootOrderValue, last: false),
            EnumerationBuffers.Entry("Timeout", KnownGuids.EfiGlobalVariable, 0x7, new byte[] { 5, 0 }, last: false, nextEntryOffsetOverride: 16),
            EnumerationBuffers.Entry("dbx", KnownGuids.EfiImageSecurityDatabase, 0x27, EnumerationBuffers.DbxValue, last: true));

        ParsedEnumeration result = EnumerationBufferParser.Parse(buffer);

        Assert.NotNull(result.Error);
        Assert.Contains("entry index 1", result.Error, StringComparison.Ordinal);
        Assert.Contains("NextEntryOffset 16", result.Error, StringComparison.Ordinal);
        Assert.Equal(1, result.EntriesParsed);
    }

    [Fact]
    public void NextEntryOffsetBeyondBuffer_StopsWithError()
    {
        byte[] buffer = EnumerationBuffers.Concat(
            EnumerationBuffers.Entry("BootOrder", KnownGuids.EfiGlobalVariable, 0x7, EnumerationBuffers.BootOrderValue, last: false),
            EnumerationBuffers.Entry("Timeout", KnownGuids.EfiGlobalVariable, 0x7, new byte[] { 5, 0 }, last: false, nextEntryOffsetOverride: 0x7FFF_FFFF));

        ParsedEnumeration result = EnumerationBufferParser.Parse(buffer);

        Assert.NotNull(result.Error);
        Assert.Contains("entry index 1", result.Error, StringComparison.Ordinal);
        Assert.Contains("beyond", result.Error, StringComparison.Ordinal);
        Assert.Equal(1, result.EntriesParsed);
    }

    [Fact]
    public void NextEntryOffsetPointingExactlyAtBufferEnd_ReportsTruncatedNextEntry()
    {
        byte[] buffer = EnumerationBuffers.Entry("BootOrder", KnownGuids.EfiGlobalVariable, 0x7, EnumerationBuffers.BootOrderValue, last: false);

        ParsedEnumeration result = EnumerationBufferParser.Parse(buffer);

        Assert.NotNull(result.Error);
        Assert.Contains("entry index 1", result.Error, StringComparison.Ordinal);
        Assert.Equal(1, result.EntriesParsed);
    }

    [Fact]
    public void MissingNameTerminator_StopsWithError()
    {
        byte[] buffer = EnumerationBuffers.Entry("PK", KnownGuids.EfiGlobalVariable, 0x27, Array.Empty<byte>(), last: true, omitTerminator: true, alignment: 1);

        ParsedEnumeration result = EnumerationBufferParser.Parse(buffer);

        Assert.NotNull(result.Error);
        Assert.Contains("entry index 0", result.Error, StringComparison.Ordinal);
        Assert.Contains("terminator", result.Error, StringComparison.Ordinal);
        Assert.Empty(result.Variables);
    }

    [Fact]
    public void ValueOverlappingHeader_StopsWithError()
    {
        byte[] buffer = EnumerationBuffers.Entry("Timeout", KnownGuids.EfiGlobalVariable, 0x7, new byte[] { 5, 0 }, last: true, valueOffsetOverride: 8);

        ParsedEnumeration result = EnumerationBufferParser.Parse(buffer);

        Assert.NotNull(result.Error);
        Assert.Contains("overlaps", result.Error, StringComparison.Ordinal);
        Assert.Empty(result.Variables);
    }

    [Fact]
    public void ValueOverlappingName_StopsWithError()
    {
        byte[] buffer = EnumerationBuffers.Entry("Timeout", KnownGuids.EfiGlobalVariable, 0x7, new byte[] { 5, 0 }, last: true, valueOffsetOverride: 34);

        ParsedEnumeration result = EnumerationBufferParser.Parse(buffer);

        Assert.NotNull(result.Error);
        Assert.Contains("overlaps the name", result.Error, StringComparison.Ordinal);
        Assert.Empty(result.Variables);
    }

    [Fact]
    public void ValueLengthPastEntryEnd_StopsWithError()
    {
        byte[] buffer = EnumerationBuffers.Concat(
            EnumerationBuffers.Entry("Timeout", KnownGuids.EfiGlobalVariable, 0x7, new byte[] { 5, 0 }, last: false, valueLengthOverride: 20),
            EnumerationBuffers.Entry("dbx", KnownGuids.EfiImageSecurityDatabase, 0x27, EnumerationBuffers.DbxValue, last: true));

        ParsedEnumeration result = EnumerationBufferParser.Parse(buffer);

        Assert.NotNull(result.Error);
        Assert.Contains("entry index 0", result.Error, StringComparison.Ordinal);
        Assert.Contains("entry end", result.Error, StringComparison.Ordinal);
        Assert.Empty(result.Variables);
    }

    [Fact]
    public void ValueLengthPastBufferEnd_OnLastEntry_StopsWithError()
    {
        byte[] buffer = EnumerationBuffers.Entry("dbx", KnownGuids.EfiImageSecurityDatabase, 0x27, EnumerationBuffers.DbxValue, last: true, valueLengthOverride: 0xFFFF_FFF0);

        ParsedEnumeration result = EnumerationBufferParser.Parse(buffer);

        Assert.NotNull(result.Error);
        Assert.Contains("beyond", result.Error, StringComparison.Ordinal);
        Assert.Empty(result.Variables);
    }

    [Fact]
    public void ZeroLengthValue_IgnoresValueOffset()
    {
        byte[] buffer = EnumerationBuffers.Entry("SetupMode", KnownGuids.EfiGlobalVariable, 0x6, Array.Empty<byte>(), last: true, valueOffsetOverride: 0xFFFF_FF00);

        ParsedEnumeration result = EnumerationBufferParser.Parse(buffer);

        Assert.Null(result.Error);
        FirmwareVariable only = Assert.Single(result.Variables);
        Assert.Equal("SetupMode", only.Name);
        Assert.Empty(only.Data);
    }

    [Fact]
    public void UndefinedAttributeBits_ArePreserved()
    {
        byte[] buffer = EnumerationBuffers.Entry("Vendor", EnumerationBuffers.VendorGuid, 0x0000_0107, new byte[] { 1 }, last: true);

        ParsedEnumeration result = EnumerationBufferParser.Parse(buffer);

        Assert.Null(result.Error);
        FirmwareVariable only = Assert.Single(result.Variables);
        Assert.Equal(0x107u, (uint)only.Attributes);
    }

    [Fact]
    public void UnalignedEntries_ParseAsWell()
    {
        byte[] buffer = EnumerationBuffers.Concat(
            EnumerationBuffers.Entry("A", KnownGuids.EfiGlobalVariable, 0x7, new byte[] { 1 }, last: false, alignment: 1),
            EnumerationBuffers.Entry("BB", KnownGuids.EfiGlobalVariable, 0x7, new byte[] { 1, 2, 3 }, last: false, alignment: 1),
            EnumerationBuffers.Entry("CCC", KnownGuids.EfiGlobalVariable, 0x7, Array.Empty<byte>(), last: true, alignment: 1));

        ParsedEnumeration result = EnumerationBufferParser.Parse(buffer);

        Assert.Null(result.Error);
        Assert.Equal(new[] { "A", "BB", "CCC" }, result.Variables.Select(v => v.Name).ToArray());
    }

    [Fact]
    public void RandomAndMutatedBuffers_NeverThrow()
    {
        Random random = new(0x5EED);
        byte[] valid = EnumerationBuffers.ThreeEntries();

        for (int iteration = 0; iteration < 500; iteration++)
        {
            byte[] buffer;
            if (iteration % 2 == 0)
            {
                buffer = (byte[])valid.Clone();
                int mutations = 1 + random.Next(8);
                for (int m = 0; m < mutations; m++)
                {
                    buffer[random.Next(buffer.Length)] = (byte)random.Next(256);
                }

                if (random.Next(4) == 0)
                {
                    buffer = buffer.AsSpan(0, random.Next(buffer.Length + 1)).ToArray();
                }
            }
            else
            {
                buffer = new byte[random.Next(200)];
                random.NextBytes(buffer);
            }

            byte[] input = buffer;
            ParsedEnumeration? result = null;
            Exception? exception = Record.Exception(() => result = EnumerationBufferParser.Parse(input));

            Assert.Null(exception);
            Assert.NotNull(result);
            Assert.Equal(result.Variables.Count, result.EntriesParsed);
        }
    }
}
