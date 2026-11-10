// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Decoders;
using UefiAccess.Core.Variables;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The Secure Boot mode of UEFI 2.10 section 32.3 as <see cref="SecureBootStateReader.DeriveMode(bool?, bool?, bool?)"/>
/// names it, and the SetupMode wording of <see cref="SecureBootStateDecoder"/>, which used to call
/// every SetupMode=0 "user mode" even when DeployedMode=1 put the platform in Deployed Mode.
/// </summary>
public sealed class SecureBootModeTests
{
    private static readonly Guid Global = KnownGuids.EfiGlobalVariable;

    [Theory]
    [InlineData(null, null, null, SecureBootMode.Unknown)]
    [InlineData(true, null, null, SecureBootMode.Setup)]
    [InlineData(true, false, false, SecureBootMode.Setup)]
    [InlineData(true, true, false, SecureBootMode.Audit)]
    [InlineData(false, true, null, SecureBootMode.Audit)]
    [InlineData(false, null, null, SecureBootMode.User)]
    [InlineData(false, false, false, SecureBootMode.User)]
    [InlineData(false, null, true, SecureBootMode.Deployed)]
    [InlineData(false, false, true, SecureBootMode.Deployed)]
    [InlineData(null, null, true, SecureBootMode.Deployed)]
    public void DeriveMode_FollowsUefiSection32_3(bool? setupMode, bool? auditMode, bool? deployedMode, SecureBootMode expected)
    {
        Assert.Equal(expected, SecureBootStateReader.DeriveMode(setupMode, auditMode, deployedMode));
    }

    [Fact]
    public void DeriveMode_OfAState_UsesItsThreeFlags()
    {
        VariableSet deployed = new(new[] { Flag("SetupMode", 0), Flag("DeployedMode", 1) });

        Assert.Equal(SecureBootMode.Deployed, SecureBootStateReader.DeriveMode(SecureBootStateReader.Read(deployed)));
        Assert.Equal(SecureBootMode.Unknown, SecureBootStateReader.DeriveMode((SecureBootState?)null));
        Assert.Equal(SecureBootMode.Unknown, SecureBootStateReader.DeriveMode(SecureBootStateReader.Empty));
    }

    [Theory]
    [InlineData(SecureBootMode.Setup, "setup mode")]
    [InlineData(SecureBootMode.User, "user mode")]
    [InlineData(SecureBootMode.Audit, "audit mode")]
    [InlineData(SecureBootMode.Deployed, "deployed mode")]
    [InlineData(SecureBootMode.Unknown, "unknown mode")]
    public void DescribeMode_NamesEveryMode(SecureBootMode mode, string expected)
    {
        Assert.Equal(expected, SecureBootStateReader.DescribeMode(mode));
    }

    [Fact]
    public void SetupModeZero_WithDeployedModeOne_IsDeployedMode()
    {
        VariableSet snapshot = new(new[] { Flag("SecureBoot", 1), Flag("SetupMode", 0), Flag("DeployedMode", 1) });

        DecodedVariable decoded = Decode(snapshot, "SetupMode");

        Assert.Equal("SetupMode=0: deployed mode: a Platform Key is enrolled", decoded.Summary);
        Assert.Contains(decoded.Fields, f => f.Name == "Meaning" && f.Value == "deployed mode: a Platform Key is enrolled");
        Assert.Null(decoded.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SetupModeZero_WithoutDeployedMode_IsUserMode(bool deployedModeZeroPresent)
    {
        // The 2014 OptiPlex's A-series firmware predates UEFI 2.5 and has no DeployedMode at all.
        List<FirmwareVariable> variables = new() { Flag("SetupMode", 0) };
        if (deployedModeZeroPresent)
        {
            variables.Add(Flag("DeployedMode", 0));
        }

        DecodedVariable decoded = Decode(new VariableSet(variables), "SetupMode");

        Assert.Equal("SetupMode=0: user mode: a Platform Key is enrolled", decoded.Summary);
    }

    [Fact]
    public void SetupModeOne_WithAuditModeOne_IsAuditMode()
    {
        VariableSet snapshot = new(new[] { Flag("SetupMode", 1), Flag("AuditMode", 1) });

        DecodedVariable decoded = Decode(snapshot, "SetupMode");

        Assert.Equal("SetupMode=1: audit mode: the platform has no Platform Key enrolled", decoded.Summary);
    }

    [Fact]
    public void SetupModeOne_WithoutAuditMode_IsSetupMode()
    {
        VariableSet snapshot = new(new[] { Flag("SetupMode", 1), Flag("AuditMode", 0) });

        DecodedVariable decoded = Decode(snapshot, "SetupMode");

        Assert.Equal("SetupMode=1: setup mode: the platform has no Platform Key enrolled", decoded.Summary);
    }

    [Fact]
    public void SetupMode_WithANullOrFailingLookup_FallsBackWithoutThrowing()
    {
        SecureBootStateDecoder decoder = new();
        FirmwareVariable setupMode = Flag("SetupMode", 0);

        DecodedVariable withNull = decoder.Decode(setupMode, null!);
        DecodedVariable withFailing = decoder.Decode(setupMode, new ThrowingLookup());

        Assert.Equal("SetupMode=0: user mode: a Platform Key is enrolled", withNull.Summary);
        Assert.Equal("SetupMode=0: user mode: a Platform Key is enrolled", withFailing.Summary);
    }

    [Fact]
    public void OtherFlags_KeepTheirOwnWording()
    {
        VariableSet snapshot = new(new[] { Flag("SetupMode", 0), Flag("DeployedMode", 1), Flag("AuditMode", 0) });

        Assert.Equal("DeployedMode=1: deployed mode is on", Decode(snapshot, "DeployedMode").Summary);
        Assert.Equal("AuditMode=0: audit mode is off", Decode(snapshot, "AuditMode").Summary);
    }

    private static FirmwareVariable Flag(string name, byte value) => new(name, Global, VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess, new[] { value });

    private static DecodedVariable Decode(VariableSet snapshot, string name) =>
        new SecureBootStateDecoder().Decode(snapshot.Find(name, Global)!, snapshot);

    /// <summary>A lookup whose every call throws, standing in for a broken snapshot.</summary>
    private sealed class ThrowingLookup : IVariableLookup
    {
        public IReadOnlyList<FirmwareVariable> All => throw new InvalidOperationException("broken snapshot");

        public FirmwareVariable? Find(string name, Guid vendorGuid) => throw new InvalidOperationException("broken snapshot");
    }
}
