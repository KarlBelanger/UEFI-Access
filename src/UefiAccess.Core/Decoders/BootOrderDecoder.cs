// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Buffers.Binary;
using System.Text;
using UefiAccess.Core.Variables;
using static UefiAccess.Core.Decoders.DecoderText;

namespace UefiAccess.Core.Decoders;

/// <summary>
/// Decodes BootOrder, BootNext and BootCurrent (UINT16 arrays rendered as Boot%04X with the load
/// option description, flagging references to missing Boot#### variables) and Timeout (UINT16
/// seconds). All four live under the EFI global variable GUID. Kind is "BootOrder". Never throws.
/// </summary>
public sealed class BootOrderDecoder : IVariableDecoder
{
    /// <summary>The Timeout variable's name under the EFI global GUID.</summary>
    internal const string TimeoutName = "Timeout";

    private const string Kind = "BootOrder";
    private const ushort WaitForever = 0xFFFF;

    private static readonly string[] ArrayNames = { "BootOrder", "BootNext", "BootCurrent" };

    /// <inheritdoc />
    public bool CanDecode(FirmwareVariable variable)
    {
        return variable is not null
            && variable.VendorGuid == KnownGuids.EfiGlobalVariable
            && (variable.Name == TimeoutName || Array.IndexOf(ArrayNames, variable.Name) >= 0);
    }

    /// <inheritdoc />
    public DecodedVariable Decode(FirmwareVariable variable, IVariableLookup lookup)
    {
        if (variable is null)
        {
            return new DecodedVariable(string.Empty, Kind, "no variable", Array.Empty<DecodedField>(), null, "variable is null");
        }

        byte[] data = variable.Data ?? Array.Empty<byte>();
        return variable.Name == TimeoutName
            ? DecodeTimeout(variable, data)
            : DecodeBootArray(variable, data, lookup);
    }

    /// <summary>
    /// The seconds a Timeout variable holds: its first two bytes as a little-endian UINT16, or null
    /// for fewer than two bytes; the caller judges a longer value. The write pipeline's snapshot
    /// reads Timeout through this method too, and so does the CLI's boot command, so the tool has
    /// one reading of it.
    /// </summary>
    /// <param name="data">The raw variable data.</param>
    /// <returns>The seconds, or null.</returns>
    public static ushort? ReadTimeoutSeconds(ReadOnlySpan<byte> data)
    {
        return data.Length < 2 ? null : BinaryPrimitives.ReadUInt16LittleEndian(data);
    }

    private static DecodedVariable DecodeTimeout(FirmwareVariable variable, byte[] data)
    {
        if (ReadTimeoutSeconds(data) is not ushort seconds)
        {
            string error = Invariant($"Timeout is {data.Length} byte(s); expected 2");
            return new DecodedVariable(variable.Key, Kind, "Timeout: malformed (" + error + ")", Array.Empty<DecodedField>(), new { Name = TimeoutName, Seconds = (int?)null, Error = error }, error);
        }

        string? lengthError = data.Length == 2 ? null : Invariant($"Timeout is {data.Length} byte(s); expected 2 (first 2 bytes decoded)");
        string meaning = seconds switch
        {
            0 => "boot the default entry immediately",
            WaitForever => "wait for the user indefinitely",
            _ => "delay before booting the default entry",
        };

        List<DecodedField> fields = new()
        {
            new DecodedField("Seconds", Invariant($"{seconds}")),
            new DecodedField("Meaning", meaning),
        };
        if (lengthError is not null)
        {
            fields.Add(new DecodedField("Error", lengthError));
        }

        string summary = Invariant($"Timeout: {Plural(seconds, "second")} ({meaning})");
        object json = new { Name = TimeoutName, Seconds = (int)seconds, WaitsForever = seconds == WaitForever, Error = lengthError };
        return new DecodedVariable(variable.Key, Kind, summary, fields, json, lengthError);
    }

    private static DecodedVariable DecodeBootArray(FirmwareVariable variable, byte[] data, IVariableLookup lookup)
    {
        string name = variable.Name;
        string? error = null;
        if (data.Length % 2 != 0)
        {
            error = Invariant($"{name} is {data.Length} bytes, which is not a multiple of 2; the trailing byte was ignored");
        }
        else if (name != "BootOrder" && data.Length != 2)
        {
            error = Invariant($"{name} is {data.Length} bytes; expected 2");
        }

        ushort[] indexes = ReadUInt16Array(data);
        int count = indexes.Length;
        List<DecodedField> fields = new(count);
        List<string> parts = new(count);
        List<string> missing = new();
        List<object> entries = new(count);

        for (int i = 0; i < count; i++)
        {
            ushort index = indexes[i];
            string bootName = KnownVariableNames.BootName(index);
            FirmwareVariable? entry = FindEntry(lookup, bootName);
            if (entry is null)
            {
                missing.Add(bootName);
                fields.Add(new DecodedField(bootName, "missing: no such Boot#### variable"));
                parts.Add(bootName + " (missing)");
                entries.Add(new { Name = bootName, Index = (int)index, Description = (string?)null, Present = false });
                continue;
            }

            LoadOption option = LoadOptionParser.Parse(entry.Data ?? Array.Empty<byte>());
            string description = option.Description;
            if (string.IsNullOrEmpty(description))
            {
                description = option.Error is null ? "(no description)" : "(malformed load option)";
            }

            fields.Add(new DecodedField(bootName, description));
            parts.Add(Invariant($"{bootName} ({description})"));
            entries.Add(new { Name = bootName, Index = (int)index, Description = description, Present = true });
        }

        if (error is not null)
        {
            fields.Add(new DecodedField("Error", error));
        }

        StringBuilder summary = new(name);
        if (count == 0)
        {
            summary.Append(" is empty");
        }
        else
        {
            summary.Append(": ").Append(string.Join(", ", parts));
        }

        if (missing.Count > 0)
        {
            string missingText = missing.Count == 1 ? "1 missing entry" : Invariant($"{missing.Count} missing entries");
            summary.Append("; ").Append(missingText).Append(": ").Append(string.Join(", ", missing));
        }

        if (error is not null)
        {
            summary.Append("; malformed: ").Append(error);
        }

        object json = new { Name = name, Entries = entries, MissingCount = missing.Count, Missing = missing, Error = error };
        return new DecodedVariable(variable.Key, Kind, summary.ToString(), fields, json, error);
    }

    /// <summary>
    /// Reads the little-endian UINT16 entries of a boot list (BootOrder, BootNext, BootCurrent).
    /// A trailing odd byte is ignored; the caller judges the byte count. The write pipeline's
    /// codec parses through this method too, so the tool never has two readings of one list.
    /// </summary>
    /// <param name="data">The raw variable data.</param>
    /// <returns>The entries in order; empty for fewer than two bytes.</returns>
    internal static ushort[] ReadUInt16Array(ReadOnlySpan<byte> data)
    {
        ushort[] entries = new ushort[data.Length / 2];
        for (int i = 0; i < entries.Length; i++)
        {
            entries[i] = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(i * 2, 2));
        }

        return entries;
    }

    private static FirmwareVariable? FindEntry(IVariableLookup lookup, string bootName)
    {
        if (lookup is null)
        {
            return null;
        }

        try
        {
            return lookup.Find(bootName, KnownGuids.EfiGlobalVariable);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
