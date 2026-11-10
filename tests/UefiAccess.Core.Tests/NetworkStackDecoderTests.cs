// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using UefiAccess.Core.Decoders;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Variables;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// Tests for <see cref="NetworkStackDecoder"/>: exactly the
/// five questions of the OptiPlex 7020 BIOS A18 setup form "Network Stack Configuration" (IFR
/// varstore 0x1A, 6 bytes), the four the form hides while byte 0 is 0 read as not applicable, any
/// other value as unknown in hexadecimal, any other size an error with nothing decoded, and the
/// variable stays off the whitelist.
/// </summary>
public sealed class NetworkStackDecoderTests
{
    private const VariableAttributes NvBsRt = VariableAttributes.NonVolatile | VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess;
    private const string NotApplicable = "not applicable: the UEFI network stack is Disabled";
    private const string WaitTimeWords = " (the time to press Escape to stop a PXE boot; the BIOS setup names no unit)";

    /// <summary>The bytes all nine baselines of the unit hold (gate memo, section 3).</summary>
    private static readonly byte[] UnitBytes = { 0x01, 0x01, 0x01, 0x00, 0x00, 0x00 };

    private static readonly string[] FieldNames = { "UEFI network stack", "IPv4 PXE support", "IPv6 PXE support", "PXE boot wait time", "Media detect time" };

    private static FirmwareVariable Var(params byte[] data) => new("NetworkStackVar", KnownGuids.NetworkStackSetup, NvBsRt, data);

    private static DecodedVariable Decode(params byte[] data) => new NetworkStackDecoder().Decode(Var(data), VariableSet.Empty);

    private static string Field(DecodedVariable decoded, string name) => Assert.Single(decoded.Fields, field => field.Name == name).Value;

    private static string Json(DecodedVariable decoded) => JsonSerializer.Serialize(decoded.Json);

    /// <summary>The unit's bytes with one byte replaced.</summary>
    private static byte[] UnitWith(int index, byte value)
    {
        byte[] data = (byte[])UnitBytes.Clone();
        data[index] = value;
        return data;
    }

    [Fact]
    public void DecodesTheUnitsBytes_010101000000()
    {
        DecodedVariable decoded = Decode(UnitBytes);

        Assert.Equal("d1405d16-7afc-4695-bb12-41459d3695a2:NetworkStackVar", decoded.Key);
        Assert.Equal("NetworkStack", decoded.Kind);
        Assert.Null(decoded.Error);
        Assert.Equal("NetworkStackVar: UEFI network stack Enabled; IPv4 PXE Enabled; IPv6 PXE Enabled", decoded.Summary);
        Assert.Equal(
            new[]
            {
                new DecodedField("UEFI network stack", "Enabled"),
                new DecodedField("IPv4 PXE support", "Enabled"),
                new DecodedField("IPv6 PXE support", "Enabled"),
                new DecodedField("PXE boot wait time", "0 (the time to press Escape to stop a PXE boot; the BIOS setup names no unit)"),
                new DecodedField("Media detect time", "0 seconds"),
            },
            decoded.Fields);
        Assert.Equal(
            "{\"NetworkStack\":\"Enabled\",\"Ipv4Pxe\":\"Enabled\",\"Ipv6Pxe\":\"Enabled\",\"PxeBootWaitTime\":0,\"MediaDetectTimeSeconds\":0,\"Error\":null}",
            Json(decoded));

        // Byte 3 has no setup question: whatever it holds, nothing it decodes changes.
        DecodedVariable byteThree = Decode(UnitWith(3, 0xFF));
        Assert.Equal(decoded.Summary, byteThree.Summary);
        Assert.Equal(decoded.Fields, byteThree.Fields);
        Assert.Equal(Json(decoded), Json(byteThree));
    }

    [Fact]
    public void NetworkStackDisabled_MakesTheOtherFieldsNotApplicable()
    {
        // The form suppresses the four questions while byte 0 is 0, whatever their bytes hold.
        foreach (byte[] data in new[] { new byte[] { 0x00, 0x01, 0x01, 0x00, 0x00, 0x00 }, new byte[] { 0x00, 0xFF, 0x07, 0xFF, 0xFF, 0xFF } })
        {
            DecodedVariable decoded = Decode(data);

            Assert.Null(decoded.Error);
            Assert.Equal("NetworkStackVar: UEFI network stack Disabled", decoded.Summary);
            Assert.Equal(FieldNames, decoded.Fields.Select(field => field.Name));
            Assert.Equal("Disabled", decoded.Fields[0].Value);
            Assert.All(decoded.Fields.Skip(1), field => Assert.Equal(NotApplicable, field.Value));
        }

        // The JSON keeps each byte's value; the fields say what the setup form shows.
        Assert.Equal(
            "{\"NetworkStack\":\"Disabled\",\"Ipv4Pxe\":\"Enabled\",\"Ipv6Pxe\":\"Disabled\",\"PxeBootWaitTime\":3,\"MediaDetectTimeSeconds\":20,\"Error\":null}",
            Json(Decode(0x00, 0x01, 0x00, 0x00, 0x03, 0x14)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(64)]
    public void WrongSize_IsAnErrorWithNoFields(int size)
    {
        byte[] data = new byte[size];
        Array.Fill(data, (byte)0x01);

        DecodedVariable decoded = Decode(data);

        string error = "NetworkStackVar is " + size.ToString(CultureInfo.InvariantCulture) + " byte(s); expected 6";
        Assert.Equal("NetworkStack", decoded.Kind);
        Assert.Equal(error, decoded.Error);
        Assert.Equal("NetworkStackVar: malformed (" + error + ")", decoded.Summary);
        Assert.Equal(new[] { new DecodedField("Error", error) }, decoded.Fields);
        Assert.Equal(
            "{\"NetworkStack\":null,\"Ipv4Pxe\":null,\"Ipv6Pxe\":null,\"PxeBootWaitTime\":null,\"MediaDetectTimeSeconds\":null,\"Error\":\"" + error + "\"}",
            Json(decoded));
    }

    [Fact]
    public void EmptyData_NeverThrows()
    {
        NetworkStackDecoder decoder = new();
        DecodedVariable? empty = null;
        DecodedVariable? nullData = null;
        DecodedVariable? noLookup = null;
        DecodedVariable? noVariable = null;
        bool canDecodeNull = true;

        Exception? exception = Record.Exception(() =>
        {
            empty = decoder.Decode(Var(), VariableSet.Empty);
            nullData = decoder.Decode(new FirmwareVariable("NetworkStackVar", KnownGuids.NetworkStackSetup, NvBsRt, null!), VariableSet.Empty);
            noLookup = decoder.Decode(Var(UnitBytes), null!);
            noVariable = decoder.Decode(null!, VariableSet.Empty);
            canDecodeNull = decoder.CanDecode(null!);
        });

        Assert.Null(exception);
        Assert.NotNull(empty);
        Assert.Equal("NetworkStackVar is 0 byte(s); expected 6", empty.Error);
        Assert.Equal("NetworkStackVar: malformed (NetworkStackVar is 0 byte(s); expected 6)", empty.Summary);
        Assert.NotNull(nullData);
        Assert.Equal(empty.Error, nullData.Error);
        Assert.NotNull(noLookup);
        Assert.Null(noLookup.Error);
        Assert.NotNull(noVariable);
        Assert.Equal("variable is null", noVariable.Error);
        Assert.False(canDecodeNull);
    }

    [Fact]
    public void UnknownByteValue_ShowsHex()
    {
        // Only 0 hides the other four questions (the form's SuppressIf compares byte 0 with 0).
        DecodedVariable stack = Decode(UnitWith(0, 0x02));
        Assert.Null(stack.Error);
        Assert.Equal("unknown value 0x02", Field(stack, "UEFI network stack"));
        Assert.Equal("Enabled", Field(stack, "IPv4 PXE support"));
        Assert.Equal("NetworkStackVar: UEFI network stack unknown value 0x02; IPv4 PXE Enabled; IPv6 PXE Enabled", stack.Summary);

        DecodedVariable pxe = Decode(0x01, 0xAB, 0xFF, 0x00, 0x00, 0x00);
        Assert.Null(pxe.Error);
        Assert.Equal("unknown value 0xAB", Field(pxe, "IPv4 PXE support"));
        Assert.Equal("unknown value 0xFF", Field(pxe, "IPv6 PXE support"));
        Assert.Equal("NetworkStackVar: UEFI network stack Enabled; IPv4 PXE unknown value 0xAB; IPv6 PXE unknown value 0xFF", pxe.Summary);
        Assert.Equal(
            "{\"NetworkStack\":\"Enabled\",\"Ipv4Pxe\":\"unknown value 0xAB\",\"Ipv6Pxe\":\"unknown value 0xFF\",\"PxeBootWaitTime\":0,\"MediaDetectTimeSeconds\":0,\"Error\":null}",
            Json(pxe));
    }

    [Fact]
    public void WaitAndMediaTimes_OutsideTheirRange_AreUnknown()
    {
        // The IFR's ranges: PXE boot wait time 0 to 5, media detect time 0 to 0x32 (50) seconds.
        Assert.Equal("5" + WaitTimeWords, Field(Decode(UnitWith(4, 5)), "PXE boot wait time"));
        Assert.Equal("unknown value 0x06", Field(Decode(UnitWith(4, 6)), "PXE boot wait time"));
        Assert.Equal("unknown value 0xFF", Field(Decode(UnitWith(4, 0xFF)), "PXE boot wait time"));
        Assert.Equal("1 second", Field(Decode(UnitWith(5, 1)), "Media detect time"));
        Assert.Equal("50 seconds", Field(Decode(UnitWith(5, 50)), "Media detect time"));
        Assert.Equal("unknown value 0x33", Field(Decode(UnitWith(5, 51)), "Media detect time"));
        Assert.Equal("unknown value 0xFF", Field(Decode(UnitWith(5, 0xFF)), "Media detect time"));

        DecodedVariable outside = Decode(0x01, 0x01, 0x01, 0x00, 0x06, 0x33);
        Assert.Null(outside.Error);
        Assert.Equal("NetworkStackVar: UEFI network stack Enabled; IPv4 PXE Enabled; IPv6 PXE Enabled", outside.Summary);
        Assert.Equal(
            "{\"NetworkStack\":\"Enabled\",\"Ipv4Pxe\":\"Enabled\",\"Ipv6Pxe\":\"Enabled\",\"PxeBootWaitTime\":6,\"MediaDetectTimeSeconds\":51,\"Error\":null}",
            Json(outside));
    }

    [Fact]
    public void CanDecode_NeedsNameAndGuid()
    {
        NetworkStackDecoder decoder = new();

        Assert.Equal(new Guid("d1405d16-7afc-4695-bb12-41459d3695a2"), KnownGuids.NetworkStackSetup);
        Assert.True(decoder.CanDecode(Var(UnitBytes)));
        Assert.False(decoder.CanDecode(new FirmwareVariable("NetworkStackVar", KnownGuids.EfiGlobalVariable, NvBsRt, UnitBytes)));
        Assert.False(decoder.CanDecode(new FirmwareVariable("NetworkStackVar", new Guid("ec87d643-eba4-4bb5-a1e5-3f3e36b20da9"), NvBsRt, UnitBytes)));
        Assert.False(decoder.CanDecode(new FirmwareVariable("networkstackvar", KnownGuids.NetworkStackSetup, NvBsRt, UnitBytes)));
        Assert.False(decoder.CanDecode(new FirmwareVariable("Setup", KnownGuids.NetworkStackSetup, NvBsRt, UnitBytes)));
        Assert.False(decoder.CanDecode(new FirmwareVariable(null!, KnownGuids.NetworkStackSetup, NvBsRt, UnitBytes)));
    }

    [Fact]
    public void SummaryAndFieldsNeverBeginWithReadOnly()
    {
        // Every value of every byte, one at a time, byte 0 both 1 and 0, and every size up to 8.
        List<byte[]> inputs = new();
        for (int index = 0; index < UnitBytes.Length; index++)
        {
            for (int value = 0; value <= byte.MaxValue; value++)
            {
                byte[] data = UnitWith(index, (byte)value);
                inputs.Add(data);
                byte[] disabled = (byte[])data.Clone();
                disabled[0] = 0;
                inputs.Add(disabled);
            }
        }

        for (int size = 0; size <= 8; size++)
        {
            inputs.Add(new byte[size]);
        }

        List<string> offending = new();
        foreach (byte[] data in inputs)
        {
            DecodedVariable decoded = Decode(data);
            IEnumerable<string> texts = new[] { decoded.Summary, decoded.Error ?? string.Empty }
                .Concat(decoded.Fields.SelectMany(field => new[] { field.Name, field.Value }));
            offending.AddRange(texts.Where(text => text.TrimStart().StartsWith("Read-only", StringComparison.OrdinalIgnoreCase)));
        }

        Assert.Empty(offending);

        // No string literal of the decoder begins with the words either.
        string source = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "src", "UefiAccess.Core", "Decoders", "NetworkStackDecoder.cs"));
        Assert.DoesNotMatch(new Regex("\"\\s*read-only", RegexOptions.IgnoreCase), source);
    }

    [Fact]
    public void Writability_StaysNotWhitelisted()
    {
        FirmwareVariable variable = Var(UnitBytes);
        VariableSet set = new(new[] { variable });

        Assert.False(Whitelist.TryDescribe(variable.Name, variable.VendorGuid, out _));
        foreach (bool offline in new[] { false, true })
        {
            foreach (bool machineConfirmed in new[] { true, false })
            {
                WritabilityVerdict verdict = WritabilityPolicy.Describe(variable, set, offline, machineConfirmed);

                Assert.Equal(Writability.NotWhitelisted, verdict.Verdict);
                Assert.False(verdict.IsChangeable);
            }
        }
    }

    [Fact]
    public void PxeWaitTime_IsNeverABareNumber()
    {
        for (int value = 0; value <= byte.MaxValue; value++)
        {
            DecodedVariable decoded = Decode(UnitWith(4, (byte)value));
            string text = Field(decoded, "PXE boot wait time");

            Assert.DoesNotMatch("^\\s*[0-9]+\\s*$", text);
            if (value <= 5)
            {
                Assert.Equal(value.ToString(CultureInfo.InvariantCulture) + WaitTimeWords, text);
            }

            // The JSON field keeps the plain number.
            Assert.Equal(value, JsonSerializer.SerializeToElement(decoded.Json).GetProperty("PxeBootWaitTime").GetInt32());
        }
    }
}
