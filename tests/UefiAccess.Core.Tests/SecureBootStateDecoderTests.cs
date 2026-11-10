// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Decoders;
using UefiAccess.Core.Tests.Fixtures;
using UefiAccess.Core.Variables;
using Xunit;
using static UefiAccess.Core.Tests.Fixtures.LoadOptionFixtures;

namespace UefiAccess.Core.Tests;

/// <summary>Tests for <see cref="SecureBootStateReader"/> and <see cref="SecureBootStateDecoder"/>.</summary>
public sealed class SecureBootStateDecoderTests
{
    private static readonly Guid Global = KnownGuids.EfiGlobalVariable;
    private static readonly Guid Security = KnownGuids.EfiImageSecurityDatabase;

    private static FirmwareVariable GlobalVar(string name, byte[] data) => new(name, Global, VariableAttributes.RuntimeAccess, data);

    private static FirmwareVariable SecurityVar(string name, byte[] data) => new(name, Security, VariableAttributes.RuntimeAccess, data);

    private static VariableSet Snapshot() => new(new[]
    {
        GlobalVar("SecureBoot", new byte[] { 1 }),
        GlobalVar("SetupMode", new byte[] { 0 }),
        GlobalVar("DeployedMode", new byte[] { 1 }),
        GlobalVar("PK", SignatureListFixtures.X509List(200)),
        GlobalVar("KEK", Concat(SignatureListFixtures.X509List(150), SignatureListFixtures.X509List(160))),
        SecurityVar("db", Concat(SignatureListFixtures.X509List(120), SignatureListFixtures.Sha256List(3))),
        SecurityVar("dbx", SignatureListFixtures.Sha256List(3)),
    });

    [Fact]
    public void Reader_AssemblesStateFromSnapshot()
    {
        SecureBootState state = SecureBootStateReader.Read(Snapshot());

        Assert.True(state.SecureBoot);
        Assert.False(state.SetupMode);
        Assert.Null(state.AuditMode);
        Assert.True(state.DeployedMode);
        Assert.True(state.PkPresent);
        Assert.Equal(28 + 16 + 200, state.PkSize);
        Assert.True(state.KekPresent);
        Assert.Equal((28 + 16 + 150) + (28 + 16 + 160), state.KekSize);
        Assert.NotNull(state.Db);
        Assert.Equal(2, state.Db.ListCount);
        Assert.Equal(4, state.Db.SignatureCount);
        Assert.Equal(new[] { "X509", "SHA256" }, state.Db.SignatureTypes);
        Assert.NotNull(state.Dbx);
        Assert.Equal(3, state.Dbx.SignatureCount);
        Assert.Null(state.Dbt);
    }

    [Fact]
    public void Reader_EmptyOrNullSnapshot_YieldsEmptyState()
    {
        Assert.Equal(SecureBootStateReader.Empty, SecureBootStateReader.Read(VariableSet.Empty));
        Assert.Equal(SecureBootStateReader.Empty, SecureBootStateReader.Read(null!));
        Assert.Null(SecureBootStateReader.Empty.SecureBoot);
        Assert.False(SecureBootStateReader.Empty.PkPresent);
    }

    [Fact]
    public void Reader_ZeroLengthFlag_IsNull_AndNonZeroByteIsTrue()
    {
        VariableSet set = new(new[] { GlobalVar("SecureBoot", Array.Empty<byte>()), GlobalVar("SetupMode", new byte[] { 0x7F }) });
        SecureBootState state = SecureBootStateReader.Read(set);

        Assert.Null(state.SecureBoot);
        Assert.True(state.SetupMode);
    }

    [Fact]
    public void Reader_DoesNotUseSecurityGuidForGlobalNames()
    {
        VariableSet set = new(new[] { SecurityVar("SecureBoot", new byte[] { 1 }), GlobalVar("db", SignatureListFixtures.Sha256List(1)) });
        SecureBootState state = SecureBootStateReader.Read(set);

        Assert.Null(state.SecureBoot);
        Assert.Null(state.Db);
    }

    [Fact]
    public void CanDecode_MatchesNamesUnderTheRightGuids()
    {
        SecureBootStateDecoder decoder = new();

        Assert.True(decoder.CanDecode(GlobalVar("SecureBoot", new byte[] { 1 })));
        Assert.True(decoder.CanDecode(GlobalVar("SetupMode", new byte[] { 0 })));
        Assert.True(decoder.CanDecode(GlobalVar("AuditMode", new byte[] { 0 })));
        Assert.True(decoder.CanDecode(GlobalVar("DeployedMode", new byte[] { 0 })));
        Assert.True(decoder.CanDecode(GlobalVar("PK", Array.Empty<byte>())));
        Assert.True(decoder.CanDecode(GlobalVar("KEK", Array.Empty<byte>())));
        Assert.True(decoder.CanDecode(SecurityVar("db", Array.Empty<byte>())));
        Assert.True(decoder.CanDecode(SecurityVar("dbx", Array.Empty<byte>())));
        Assert.True(decoder.CanDecode(SecurityVar("dbt", Array.Empty<byte>())));
        Assert.False(decoder.CanDecode(SecurityVar("dbr", Array.Empty<byte>())));
        Assert.False(decoder.CanDecode(GlobalVar("db", Array.Empty<byte>())));
        Assert.False(decoder.CanDecode(SecurityVar("PK", Array.Empty<byte>())));
        Assert.False(decoder.CanDecode(GlobalVar("BootOrder", Array.Empty<byte>())));
    }

    [Theory]
    [InlineData("SecureBoot", 1, "SecureBoot=1: Secure Boot is enabled")]
    [InlineData("SecureBoot", 0, "SecureBoot=0: Secure Boot is disabled")]
    [InlineData("SetupMode", 1, "SetupMode=1: setup mode: the platform has no Platform Key enrolled")]
    [InlineData("SetupMode", 0, "SetupMode=0: user mode: a Platform Key is enrolled")]
    [InlineData("AuditMode", 0, "AuditMode=0: audit mode is off")]
    [InlineData("DeployedMode", 1, "DeployedMode=1: deployed mode is on")]
    public void Decode_SingleByteFlags(string name, int value, string expectedSummary)
    {
        DecodedVariable decoded = new SecureBootStateDecoder().Decode(GlobalVar(name, new byte[] { (byte)value }), VariableSet.Empty);

        Assert.Equal("SecureBootState", decoded.Kind);
        Assert.Null(decoded.Error);
        Assert.Equal(expectedSummary, decoded.Summary);
        Assert.Contains(decoded.Fields, f => f.Name == "Value" && f.Value == value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.NotNull(decoded.Json);
    }

    [Fact]
    public void Decode_Flag_EmptyData_ReportsError()
    {
        DecodedVariable decoded = new SecureBootStateDecoder().Decode(GlobalVar("SecureBoot", Array.Empty<byte>()), VariableSet.Empty);

        Assert.NotNull(decoded.Error);
        Assert.Contains("expected 1 byte", decoded.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Decode_Flag_WrongLength_DecodesFirstByteWithError()
    {
        DecodedVariable decoded = new SecureBootStateDecoder().Decode(GlobalVar("SecureBoot", new byte[] { 1, 0 }), VariableSet.Empty);

        Assert.NotNull(decoded.Error);
        Assert.Contains("expected 1", decoded.Error, StringComparison.Ordinal);
        Assert.StartsWith("SecureBoot=1: Secure Boot is enabled", decoded.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Decode_Flag_ValueAboveOne_IsTreatedAsSetWithNote()
    {
        DecodedVariable decoded = new SecureBootStateDecoder().Decode(GlobalVar("SecureBoot", new byte[] { 5 }), VariableSet.Empty);

        Assert.Null(decoded.Error);
        Assert.Equal("SecureBoot=5: Secure Boot is enabled", decoded.Summary);
        Assert.Contains(decoded.Fields, f => f.Name == "Note");
    }

    [Fact]
    public void Decode_Db_SummarisesSignatureLists()
    {
        VariableSet set = Snapshot();
        DecodedVariable decoded = new SecureBootStateDecoder().Decode(set.Find("db", Security)!, set);

        Assert.Equal("SecureBootState", decoded.Kind);
        Assert.Null(decoded.Error);
        int size = (28 + 16 + 120) + (28 + (3 * 48));
        Assert.Equal("db: " + size + " bytes, 2 signature lists, 4 signatures (X509, SHA256)", decoded.Summary);
        Assert.Contains(decoded.Fields, f => f.Name == "Signature lists" && f.Value == "2");
        Assert.Contains(decoded.Fields, f => f.Name == "Signatures" && f.Value == "4");
        Assert.Contains(decoded.Fields, f => f.Name == "Signature types" && f.Value == "X509, SHA256");
    }

    [Fact]
    public void Decode_Pk_UsesSignatureListReaderToo()
    {
        VariableSet set = Snapshot();
        DecodedVariable decoded = new SecureBootStateDecoder().Decode(set.Find("PK", Global)!, set);

        Assert.Null(decoded.Error);
        Assert.Equal("PK: 244 bytes, 1 signature list, 1 signature (X509)", decoded.Summary);
    }

    [Fact]
    public void Decode_EmptyDbx_IsPresentButEmpty()
    {
        DecodedVariable decoded = new SecureBootStateDecoder().Decode(SecurityVar("dbx", Array.Empty<byte>()), VariableSet.Empty);

        Assert.Null(decoded.Error);
        Assert.Equal("dbx: present but empty (0 bytes)", decoded.Summary);
        Assert.Contains(decoded.Fields, f => f.Name == "Signature types" && f.Value == "none");
    }

    [Fact]
    public void Decode_MalformedDb_PropagatesReaderError()
    {
        byte[] data = Concat(SignatureListFixtures.Sha256List(1), new byte[5]);
        DecodedVariable decoded = new SecureBootStateDecoder().Decode(SecurityVar("db", data), VariableSet.Empty);

        Assert.NotNull(decoded.Error);
        Assert.Contains("truncated", decoded.Error, StringComparison.Ordinal);
        Assert.Contains("malformed", decoded.Summary, StringComparison.Ordinal);
        Assert.Contains(decoded.Fields, f => f.Name == "Error");
    }
}
