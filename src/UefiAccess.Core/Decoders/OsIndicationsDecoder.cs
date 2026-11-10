// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Buffers.Binary;
using UefiAccess.Core.Variables;
using static UefiAccess.Core.Decoders.DecoderText;

namespace UefiAccess.Core.Decoders;

/// <summary>
/// Decodes OsIndications and OsIndicationsSupported (UINT64, EFI global variable GUID) into the
/// eight UEFI 2.10 bit names: 0x1 BootToFwUi, 0x2 TimestampRevocation, 0x4 FileCapsuleDelivery,
/// 0x8 FmpCapsule, 0x10 CapsuleResultVar, 0x20 StartOsRecovery, 0x40 StartPlatformRecovery,
/// 0x80 JsonConfigDataRefresh. Bits outside those eight are reported as unknown. Kind is "OsIndications". Never throws.
/// </summary>
public sealed class OsIndicationsDecoder : IVariableDecoder
{
    private const string Kind = "OsIndications";
    private const int ValueLength = 8;

    private static readonly string[] Names = { "OsIndications", "OsIndicationsSupported" };

    private static readonly (ulong Bit, string Name)[] Bits =
    {
        (0x1, "BootToFwUi"),
        (0x2, "TimestampRevocation"),
        (0x4, "FileCapsuleDelivery"),
        (0x8, "FmpCapsule"),
        (0x10, "CapsuleResultVar"),
        (0x20, "StartOsRecovery"),
        (0x40, "StartPlatformRecovery"),
        (0x80, "JsonConfigDataRefresh"),
    };

    private static readonly ulong KnownMask = Bits.Aggregate(0UL, (mask, bit) => mask | bit.Bit);

    /// <summary>Names the known bits that are set in an OsIndications value, in ascending bit order.</summary>
    /// <param name="value">The UINT64 value.</param>
    /// <returns>The bit names; empty when no known bit is set.</returns>
    public static IReadOnlyList<string> FlagNames(ulong value)
    {
        List<string> names = new();
        foreach ((ulong bit, string name) in Bits)
        {
            if ((value & bit) != 0)
            {
                names.Add(name);
            }
        }

        return names;
    }

    /// <inheritdoc />
    public bool CanDecode(FirmwareVariable variable)
    {
        return variable is not null
            && variable.VendorGuid == KnownGuids.EfiGlobalVariable
            && Array.IndexOf(Names, variable.Name) >= 0;
    }

    /// <inheritdoc />
    public DecodedVariable Decode(FirmwareVariable variable, IVariableLookup lookup)
    {
        _ = lookup;
        if (variable is null)
        {
            return new DecodedVariable(string.Empty, Kind, "no variable", Array.Empty<DecodedField>(), null, "variable is null");
        }

        string name = variable.Name;
        byte[] data = variable.Data ?? Array.Empty<byte>();
        if (data.Length < ValueLength)
        {
            string error = Invariant($"{name} is {data.Length} byte(s); expected {ValueLength}");
            return new DecodedVariable(
                variable.Key,
                Kind,
                Invariant($"{name}: malformed ({error})"),
                new[] { new DecodedField("Error", error) },
                new { Name = name, Value = (ulong?)null, Flags = Array.Empty<string>(), UnknownBits = (ulong?)null, Error = error },
                error);
        }

        ulong value = BinaryPrimitives.ReadUInt64LittleEndian(data);
        string? lengthError = data.Length == ValueLength ? null : Invariant($"{name} is {data.Length} bytes; expected {ValueLength} (first {ValueLength} bytes decoded)");
        IReadOnlyList<string> flags = FlagNames(value);
        ulong unknown = value & ~KnownMask;
        string flagText = flags.Count == 0 ? "none" : string.Join(", ", flags);

        List<DecodedField> fields = new()
        {
            new DecodedField("Value", Invariant($"0x{value:x16}")),
            new DecodedField("Flags", flagText),
        };
        if (unknown != 0)
        {
            fields.Add(new DecodedField("Unknown bits", Invariant($"0x{unknown:x16}")));
        }

        if (lengthError is not null)
        {
            fields.Add(new DecodedField("Error", lengthError));
        }

        string summary = Invariant($"{name}: {flagText} (0x{value:x16})");
        if (unknown != 0)
        {
            summary += Invariant($"; unknown bits 0x{unknown:x16}");
        }

        if (lengthError is not null)
        {
            summary += "; malformed: " + lengthError;
        }

        object json = new { Name = name, Value = (ulong?)value, Flags = flags, UnknownBits = (ulong?)unknown, Error = lengthError };
        return new DecodedVariable(variable.Key, Kind, summary, fields, json, lengthError);
    }
}
