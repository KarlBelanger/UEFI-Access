// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Variables;
using static UefiAccess.Core.Decoders.DecoderText;

namespace UefiAccess.Core.Decoders;

/// <summary>
/// Decodes NetworkStackVar under <see cref="KnownGuids.NetworkStackSetup"/>, a runtime-readable
/// Dell setup variable (task 4.6, gate outcome C). The layout is the one
/// the OptiPlex 7020 BIOS A18 setup forms give it: IFR formset Main, varstore 0x1A (6 bytes), form
/// 0x4C9 "Network Stack Configuration", a form no setup page links to. Exactly its five questions
/// are decoded: byte 0 the UEFI network stack (also the "Enable UEFI Network Stack" box on the
/// setup screen's Integrated NIC page), bytes 1 and 2 IPv4 and IPv6 PXE support, byte 4 the PXE
/// boot wait time (0 to 5, for which the setup forms name no unit) and byte 5 the media detect
/// time (0 to 50 seconds). Byte 3 has no question and is not a field. The form hides the other
/// four questions while byte 0 is 0, so they then read as not applicable; the JSON keeps every
/// byte's value either way. Any other value reads as unknown, in hexadecimal. A size other than 6
/// is an error and nothing is decoded. Kind is "NetworkStack". Never throws.
/// </summary>
public sealed class NetworkStackDecoder : IVariableDecoder
{
    /// <summary>The variable's name, as the IFR varstore names it.</summary>
    internal const string VariableName = "NetworkStackVar";

    private const string Kind = "NetworkStack";
    private const int ValueLength = 6;
    private const int MaximumPxeBootWaitTime = 5;
    private const int MaximumMediaDetectTime = 50;
    private const string NotApplicable = "not applicable: the UEFI network stack is Disabled";

    /// <inheritdoc />
    public bool CanDecode(FirmwareVariable variable)
    {
        return variable is not null
            && variable.VendorGuid == KnownGuids.NetworkStackSetup
            && variable.Name == VariableName;
    }

    /// <inheritdoc />
    public DecodedVariable Decode(FirmwareVariable variable, IVariableLookup lookup)
    {
        _ = lookup;
        if (variable is null)
        {
            return new DecodedVariable(string.Empty, Kind, "no variable", Array.Empty<DecodedField>(), null, "variable is null");
        }

        byte[] data = variable.Data ?? Array.Empty<byte>();
        if (data.Length != ValueLength)
        {
            string error = Invariant($"{VariableName} is {data.Length} byte(s); expected {ValueLength}");
            return new DecodedVariable(
                variable.Key,
                Kind,
                Invariant($"{VariableName}: malformed ({error})"),
                new[] { new DecodedField("Error", error) },
                new { NetworkStack = (string?)null, Ipv4Pxe = (string?)null, Ipv6Pxe = (string?)null, PxeBootWaitTime = (int?)null, MediaDetectTimeSeconds = (int?)null, Error = error },
                error);
        }

        string networkStack = OnOff(data[0]);
        string ipv4Pxe = OnOff(data[1]);
        string ipv6Pxe = OnOff(data[2]);
        bool applies = data[0] != 0;

        List<DecodedField> fields = new()
        {
            new DecodedField("UEFI network stack", networkStack),
            new DecodedField("IPv4 PXE support", applies ? ipv4Pxe : NotApplicable),
            new DecodedField("IPv6 PXE support", applies ? ipv6Pxe : NotApplicable),
            new DecodedField("PXE boot wait time", applies ? PxeBootWaitTimeText(data[4]) : NotApplicable),
            new DecodedField("Media detect time", applies ? MediaDetectTimeText(data[5]) : NotApplicable),
        };

        string summary = applies
            ? Invariant($"{VariableName}: UEFI network stack {networkStack}; IPv4 PXE {ipv4Pxe}; IPv6 PXE {ipv6Pxe}")
            : Invariant($"{VariableName}: UEFI network stack {networkStack}");
        object json = new { NetworkStack = networkStack, Ipv4Pxe = ipv4Pxe, Ipv6Pxe = ipv6Pxe, PxeBootWaitTime = (int?)data[4], MediaDetectTimeSeconds = (int?)data[5], Error = (string?)null };
        return new DecodedVariable(variable.Key, Kind, summary, fields, json, null);
    }

    /// <summary>A Disabled/Enabled question: the IFR's two options, 0 and 1.</summary>
    private static string OnOff(byte value)
    {
        return value switch
        {
            0 => "Disabled",
            1 => "Enabled",
            _ => Unknown(value),
        };
    }

    /// <summary>
    /// The PXE boot wait time with what it is, never a bare number: the IFR's help says "Wait time
    /// to press ESC key to abort the PXE boot" and names no unit.
    /// </summary>
    private static string PxeBootWaitTimeText(byte value)
    {
        return value <= MaximumPxeBootWaitTime
            ? Invariant($"{value} (the time to press Escape to stop a PXE boot; the BIOS setup names no unit)")
            : Unknown(value);
    }

    /// <summary>The media detect time in seconds, the unit of the IFR's help ("Wait time in sec to detect media").</summary>
    private static string MediaDetectTimeText(byte value)
    {
        return value <= MaximumMediaDetectTime ? Plural(value, "second") : Unknown(value);
    }

    private static string Unknown(byte value) => Invariant($"unknown value 0x{value:X2}");
}
