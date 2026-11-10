// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace UefiAccess.Core.Variables;

/// <summary>Result of parsing an NtEnumerateSystemEnvironmentValuesEx buffer.</summary>
/// <param name="Variables">The variables parsed before the end of the list or the first bad entry.</param>
/// <param name="EntriesParsed">Number of entries successfully parsed.</param>
/// <param name="Error">Description of the first malformed entry, or null when the whole buffer parsed.</param>
public sealed record ParsedEnumeration(
    IReadOnlyList<FirmwareVariable> Variables,
    int EntriesParsed,
    string? Error);

/// <summary>
/// Pure parser for VARIABLE_NAME_AND_VALUE buffers: per entry, offset 0 NextEntryOffset (UINT32,
/// 0 ends the list), 4 ValueOffset (UINT32, relative to the entry), 8 ValueLength (UINT32),
/// 12 Attributes (UINT32), 16 VendorGuid (16 bytes), 32 null-terminated UTF-16 name.
/// Every offset is validated against the buffer; a bad entry stops parsing with an error that
/// names the zero-based entry index and the reason, keeping the entries parsed before it.
/// Never throws.
/// </summary>
public static class EnumerationBufferParser
{
    /// <summary>Size in bytes of the fixed entry header that precedes the name.</summary>
    public const int HeaderSize = 32;

    private const int NextEntryOffsetField = 0;
    private const int ValueOffsetField = 4;
    private const int ValueLengthField = 8;
    private const int AttributesField = 12;
    private const int VendorGuidField = 16;

    /// <summary>Parses the buffer.</summary>
    /// <param name="buffer">The raw buffer returned by the native call.</param>
    /// <returns>The parsed variables, the entry count, and an error for malformed input.</returns>
    public static ParsedEnumeration Parse(ReadOnlySpan<byte> buffer)
    {
        List<FirmwareVariable> variables = new();
        string? error;
        try
        {
            error = ParseCore(buffer, variables);
        }
        catch (Exception ex)
        {
            // Defensive: the loop below validates every access, so this is not expected to run.
            error = Describe(variables.Count, "unexpected parser failure: " + ex.GetType().Name + ": " + ex.Message);
        }

        return new ParsedEnumeration(variables, variables.Count, error);
    }

    private static string? ParseCore(ReadOnlySpan<byte> buffer, List<FirmwareVariable> variables)
    {
        if (buffer.IsEmpty)
        {
            return null;
        }

        long length = buffer.Length;
        long entryStart = 0;
        int index = 0;
        while (true)
        {
            if (entryStart + HeaderSize > length)
            {
                return Describe(index, $"header truncated: needs {HeaderSize} bytes at offset {entryStart} but the buffer has {length} bytes");
            }

            ReadOnlySpan<byte> header = buffer.Slice((int)entryStart, HeaderSize);
            uint nextEntryOffset = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(NextEntryOffsetField));
            uint valueOffset = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(ValueOffsetField));
            uint valueLength = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(ValueLengthField));
            uint attributes = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(AttributesField));
            Guid vendorGuid = new(header.Slice(VendorGuidField, 16));

            // The entry ends where the next one starts, or at the end of the buffer for the last entry.
            long entryEnd;
            if (nextEntryOffset == 0)
            {
                entryEnd = length;
            }
            else
            {
                if (nextEntryOffset < HeaderSize)
                {
                    return Describe(index, $"NextEntryOffset {nextEntryOffset} is smaller than the {HeaderSize}-byte entry header");
                }

                entryEnd = entryStart + nextEntryOffset;
                if (entryEnd > length)
                {
                    return Describe(index, $"NextEntryOffset {nextEntryOffset} points to offset {entryEnd}, beyond the {length}-byte buffer");
                }
            }

            // Name: UTF-16, null-terminated, must terminate inside the entry.
            long nameStart = entryStart + HeaderSize;
            long nameEnd = -1;
            for (long position = nameStart; position + 2 <= entryEnd; position += 2)
            {
                if (buffer[(int)position] == 0 && buffer[(int)position + 1] == 0)
                {
                    nameEnd = position;
                    break;
                }
            }

            if (nameEnd < 0)
            {
                return Describe(index, $"name starting at offset {nameStart} has no UTF-16 terminator before the entry ends at offset {entryEnd}");
            }

            string name = Encoding.Unicode.GetString(buffer.Slice((int)nameStart, (int)(nameEnd - nameStart)));

            // Value: only validated when there is data; a zero-length value carries no offset to check.
            byte[] data;
            if (valueLength == 0)
            {
                data = Array.Empty<byte>();
            }
            else
            {
                if (valueOffset < HeaderSize)
                {
                    return Describe(index, $"ValueOffset {valueOffset} overlaps the {HeaderSize}-byte entry header");
                }

                long valueStart = entryStart + valueOffset;
                long valueEnd = valueStart + valueLength;
                if (valueStart > length || valueEnd > length)
                {
                    return Describe(index, $"ValueOffset {valueOffset} with ValueLength {valueLength} reaches offset {valueEnd}, beyond the {length}-byte buffer");
                }

                if (valueEnd > entryEnd)
                {
                    return Describe(index, $"ValueOffset {valueOffset} with ValueLength {valueLength} reaches offset {valueEnd}, beyond the entry end at offset {entryEnd}");
                }

                if (valueStart < nameEnd + 2)
                {
                    return Describe(index, $"ValueOffset {valueOffset} overlaps the name that ends at offset {nameEnd + 2}");
                }

                data = buffer.Slice((int)valueStart, (int)valueLength).ToArray();
            }

            variables.Add(new FirmwareVariable(name, vendorGuid, (VariableAttributes)attributes, data));
            index++;

            if (nextEntryOffset == 0)
            {
                return null;
            }

            entryStart = entryEnd;
        }
    }

    private static string Describe(int index, string reason)
    {
        return string.Create(CultureInfo.InvariantCulture, $"entry index {index}: {reason}; {index} entries parsed before it");
    }
}
