// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Decoders;
using UefiAccess.Core.Variables;
using Xunit;
using static UefiAccess.Core.Tests.Fixtures.LoadOptionFixtures;

namespace UefiAccess.Core.Tests;

/// <summary>Tests for <see cref="OsIndicationsDecoder"/>.</summary>
public sealed class OsIndicationsDecoderTests
{
    private static readonly Guid Global = KnownGuids.EfiGlobalVariable;

    private static FirmwareVariable Var(string name, byte[] data) => new(name, Global, VariableAttributes.RuntimeAccess, data);

    [Fact]
    public void CanDecode_AcceptsBothNamesUnderTheGlobalGuid()
    {
        OsIndicationsDecoder decoder = new();

        Assert.True(decoder.CanDecode(Var("OsIndications", U64(0))));
        Assert.True(decoder.CanDecode(Var("OsIndicationsSupported", U64(0))));
        Assert.False(decoder.CanDecode(Var("osindications", U64(0))));
        Assert.False(decoder.CanDecode(Var("BootOrder", U64(0))));
        Assert.False(decoder.CanDecode(new FirmwareVariable("OsIndications", KnownGuids.EfiImageSecurityDatabase, VariableAttributes.RuntimeAccess, U64(0))));
    }

    [Fact]
    public void Decode_NamesSetBitsInAscendingOrder()
    {
        DecodedVariable decoded = new OsIndicationsDecoder().Decode(Var("OsIndicationsSupported", U64(0x41)), VariableSet.Empty);

        Assert.Equal("OsIndications", decoded.Kind);
        Assert.Null(decoded.Error);
        Assert.Equal("OsIndicationsSupported: BootToFwUi, StartPlatformRecovery (0x0000000000000041)", decoded.Summary);
        Assert.Contains(decoded.Fields, f => f.Name == "Value" && f.Value == "0x0000000000000041");
        Assert.Contains(decoded.Fields, f => f.Name == "Flags" && f.Value == "BootToFwUi, StartPlatformRecovery");
        Assert.DoesNotContain(decoded.Fields, f => f.Name == "Unknown bits");
        Assert.NotNull(decoded.Json);
    }

    [Fact]
    public void Decode_AllEightBits()
    {
        DecodedVariable decoded = new OsIndicationsDecoder().Decode(Var("OsIndications", U64(0xFF)), VariableSet.Empty);

        Assert.Equal(
            "OsIndications: BootToFwUi, TimestampRevocation, FileCapsuleDelivery, FmpCapsule, CapsuleResultVar, StartOsRecovery, StartPlatformRecovery, JsonConfigDataRefresh (0x00000000000000ff)",
            decoded.Summary);
    }

    [Fact]
    public void Decode_Zero_ReportsNone()
    {
        DecodedVariable decoded = new OsIndicationsDecoder().Decode(Var("OsIndications", U64(0)), VariableSet.Empty);

        Assert.Null(decoded.Error);
        Assert.Equal("OsIndications: none (0x0000000000000000)", decoded.Summary);
    }

    [Fact]
    public void Decode_UnknownBits_AreReportedWithoutError()
    {
        DecodedVariable decoded = new OsIndicationsDecoder().Decode(Var("OsIndications", U64(0x8000000000000101)), VariableSet.Empty);

        Assert.Null(decoded.Error);
        Assert.Equal("OsIndications: BootToFwUi (0x8000000000000101); unknown bits 0x8000000000000100", decoded.Summary);
        Assert.Contains(decoded.Fields, f => f.Name == "Unknown bits" && f.Value == "0x8000000000000100");
    }

    [Fact]
    public void Decode_TooShort_ReportsError()
    {
        DecodedVariable decoded = new OsIndicationsDecoder().Decode(Var("OsIndications", U32(1)), VariableSet.Empty);

        Assert.NotNull(decoded.Error);
        Assert.Contains("expected 8", decoded.Error, StringComparison.Ordinal);
        Assert.Contains("malformed", decoded.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Decode_TooLong_DecodesFirstEightBytesWithError()
    {
        DecodedVariable decoded = new OsIndicationsDecoder().Decode(Var("OsIndications", Concat(U64(1), U32(0))), VariableSet.Empty);

        Assert.NotNull(decoded.Error);
        Assert.StartsWith("OsIndications: BootToFwUi (0x0000000000000001)", decoded.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void FlagNames_ListsOnlyKnownBits()
    {
        Assert.Equal(new[] { "TimestampRevocation", "JsonConfigDataRefresh" }, OsIndicationsDecoder.FlagNames(0x82 | 0x100));
        Assert.Empty(OsIndicationsDecoder.FlagNames(0));
    }
}
