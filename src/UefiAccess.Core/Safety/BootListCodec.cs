// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Buffers.Binary;
using UefiAccess.Core.Decoders;
using UefiAccess.Core.Variables;

namespace UefiAccess.Core.Safety;

/// <summary>A boot list (BootOrder, BootNext) read from its bytes.</summary>
/// <param name="Entries">The UINT16 entry numbers in order; a trailing odd byte contributes no entry.</param>
/// <param name="ByteCount">The number of bytes the variable held, odd or not.</param>
public sealed record DecodeResult(IReadOnlyList<ushort> Entries, int ByteCount)
{
    /// <summary>True when the variable held no bytes at all.</summary>
    public bool IsEmpty => ByteCount == 0;

    /// <summary>True when the byte count is odd, which rule V1 refuses.</summary>
    public bool HasOddByteCount => ByteCount % 2 != 0;

    /// <summary>The entries as Boot#### names, in order.</summary>
    public IReadOnlyList<string> Names => Entries.Select(index => KnownVariableNames.BootName(index)).ToArray();
}

/// <summary>
/// The one place the pipeline turns entry numbers into bytes and back: little-endian UINT16
/// arrays, as the UEFI specification lays BootOrder and BootNext out. Decoding goes through
/// <see cref="BootOrderDecoder.ReadUInt16Array"/>, the reader the decoders use, so the tool has one
/// parser, not two. Encoding is the only way the pipeline produces the bytes it writes.
/// </summary>
public static class BootListCodec
{
    /// <summary>The size of one entry in bytes.</summary>
    public const int EntrySize = 2;

    /// <summary>Reads a boot list. Never throws; null reads as empty.</summary>
    /// <param name="bytes">The raw variable data.</param>
    /// <returns>The entries and the byte count.</returns>
    public static DecodeResult Decode(byte[]? bytes)
    {
        byte[] data = bytes ?? Array.Empty<byte>();
        return new DecodeResult(BootOrderDecoder.ReadUInt16Array(data), data.Length);
    }

    /// <summary>Writes entry numbers as a little-endian UINT16 array: 2 bytes per entry, no padding.</summary>
    /// <param name="entries">The entry numbers in order.</param>
    /// <returns>The bytes; empty for no entries.</returns>
    public static byte[] Encode(IReadOnlyList<ushort> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        byte[] bytes = new byte[entries.Count * EntrySize];
        for (int i = 0; i < entries.Count; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * EntrySize, EntrySize), entries[i]);
        }

        return bytes;
    }
}
