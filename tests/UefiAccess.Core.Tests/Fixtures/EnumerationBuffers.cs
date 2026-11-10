// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Buffers.Binary;
using System.Text;
using UefiAccess.Core.Variables;

namespace UefiAccess.Core.Tests.Fixtures;

/// <summary>
/// Builds synthetic NtEnumerateSystemEnvironmentValuesEx (VARIABLE_NAME_AND_VALUE) buffers as byte
/// arrays for the parser tests. Layout per entry: 0 NextEntryOffset, 4 ValueOffset, 8 ValueLength,
/// 12 Attributes, 16 VendorGuid, 32 UTF-16 name with terminator, then padding and the value.
/// </summary>
internal static class EnumerationBuffers
{
    /// <summary>Header size of one entry in bytes.</summary>
    public const int HeaderSize = 32;

    /// <summary>A name with non-ASCII (Japanese) characters used by the three-entry fixture.</summary>
    public const string JapaneseName = "日本語の変数";

    /// <summary>The GUID used for the vendor entry of the three-entry fixture.</summary>
    public static readonly Guid VendorGuid = new("6B2A9F9D-0C7F-4D8E-9C3B-1F6E2A7B5C4D");

    /// <summary>Value bytes of the BootOrder entry: Boot0001, Boot0002.</summary>
    public static readonly byte[] BootOrderValue = { 0x01, 0x00, 0x02, 0x00 };

    /// <summary>Value bytes of the dbx entry.</summary>
    public static readonly byte[] DbxValue = { 0xAA, 0xBB, 0xCC };

    /// <summary>
    /// Three chained entries: BootOrder with data, a Japanese-named zero-length variable, and dbx
    /// (image security database GUID) as the last entry with NextEntryOffset 0.
    /// </summary>
    public static byte[] ThreeEntries()
    {
        return Concat(
            Entry("BootOrder", KnownGuids.EfiGlobalVariable, 0x7, BootOrderValue, last: false),
            Entry(JapaneseName, VendorGuid, 0x6, Array.Empty<byte>(), last: false),
            Entry("dbx", KnownGuids.EfiImageSecurityDatabase, 0x27, DbxValue, last: true));
    }

    /// <summary>
    /// Three entries where the second one has a ValueOffset far beyond the buffer end; the first
    /// entry is good and must survive, the third is never reached.
    /// </summary>
    public static byte[] CorruptedValueOffset()
    {
        return Concat(
            Entry("BootOrder", KnownGuids.EfiGlobalVariable, 0x7, BootOrderValue, last: false),
            Entry("Timeout", KnownGuids.EfiGlobalVariable, 0x7, new byte[] { 5, 0 }, last: false, valueOffsetOverride: 0x1000),
            Entry("dbx", KnownGuids.EfiImageSecurityDatabase, 0x27, DbxValue, last: true));
    }

    /// <summary>Builds one entry.</summary>
    /// <param name="name">Variable name.</param>
    /// <param name="guid">Vendor GUID.</param>
    /// <param name="attributes">Raw attribute bits.</param>
    /// <param name="value">Value bytes.</param>
    /// <param name="last">True to write NextEntryOffset 0.</param>
    /// <param name="nextEntryOffsetOverride">Replaces the computed NextEntryOffset (ignored when <paramref name="last"/>).</param>
    /// <param name="valueOffsetOverride">Replaces the computed ValueOffset.</param>
    /// <param name="valueLengthOverride">Replaces the real value length.</param>
    /// <param name="omitTerminator">True to leave out the UTF-16 name terminator.</param>
    /// <param name="alignment">Entry alignment in bytes, mimicking the kernel's padding.</param>
    public static byte[] Entry(
        string name,
        Guid guid,
        uint attributes,
        byte[] value,
        bool last,
        uint? nextEntryOffsetOverride = null,
        uint? valueOffsetOverride = null,
        uint? valueLengthOverride = null,
        bool omitTerminator = false,
        int alignment = 8)
    {
        byte[] nameBytes = Encoding.Unicode.GetBytes(name);
        int nameLength = nameBytes.Length + (omitTerminator ? 0 : 2);
        int valueOffset = Align(HeaderSize + nameLength, 4);
        int entryLength = Align(valueOffset + value.Length, alignment);
        byte[] entry = new byte[entryLength];

        uint next = last ? 0 : nextEntryOffsetOverride ?? (uint)entryLength;
        BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(0), next);
        BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(4), valueOffsetOverride ?? (uint)valueOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(8), valueLengthOverride ?? (uint)value.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(12), attributes);
        guid.ToByteArray().CopyTo(entry, 16);
        nameBytes.CopyTo(entry, HeaderSize);
        value.CopyTo(entry, valueOffset);
        return entry;
    }

    /// <summary>Concatenates entries into one buffer.</summary>
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

    private static int Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;
}
