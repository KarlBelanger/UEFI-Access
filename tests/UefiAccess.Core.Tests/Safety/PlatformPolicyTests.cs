// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Variables;
using Xunit;
using static UefiAccess.Core.Tests.Safety.SafetyFixtures;

namespace UefiAccess.Core.Tests.Safety;

/// <summary>
/// The platform facts of stages S2, S7 and S8: only the
/// 2014 OptiPlex 7020 on BIOS A18 is the contracted machine; an identity that was not read, by a
/// timeout or an inspector that threw, never confirms it; since 25 September 2026 changes are
/// enabled only on the M3 test unit, whose Windows machine ID hashes to the constant, so another
/// OptiPlex 7020 on A18 and a machine ID that cannot be read are never enabled; a BitLocker read
/// that failed is assessed as unknown, which the pipeline treats as on; the Secure Boot sentence
/// of the Warnings box. Every inspector is a fake and every unit check reads a made-up machine
/// ID; nothing here touches WMI or the registry.
/// </summary>
public sealed class PlatformPolicyTests
{
    [Theory]
    [InlineData(true, "A18", true)]
    [InlineData(true, "a18", true)]
    [InlineData(true, " A18 ", true)]
    [InlineData(true, "A17", false)]
    [InlineData(true, "A18.1", false)]
    [InlineData(true, null, false)]
    [InlineData(false, "A18", false)]
    public void IsContractedMachine_NeedsTheExpectedModelAndBiosA18(bool expectedModelConfirmed, string? biosVersion, bool contracted)
    {
        MachineIdentityResult identity = Identity(true) with { ExpectedModelConfirmed = expectedModelConfirmed, BiosVersion = biosVersion };

        Assert.Equal(contracted, PlatformPolicy.IsContractedMachine(identity));
    }

    [Fact]
    public void IsContractedMachine_IsFalseForAnIdentityThatWasNotRead()
    {
        Assert.False(PlatformPolicy.IsContractedMachine(null));
    }

    [Fact]
    public void ReadIdentity_ReturnsWhatTheInspectorRead()
    {
        MachineIdentityResult identity = Identity(true);
        PlatformPolicy policy = Policy(identity: new TransactionFakes.FixedInspector<MachineIdentityResult>(identity));

        Assert.Same(identity, policy.ReadIdentity());
        Assert.True(PlatformPolicy.IsContractedMachine(policy.ReadIdentity()));
    }

    [Fact]
    public void ReadIdentity_OfAnInspectorThatThrows_NeverConfirmsTheMachine()
    {
        TransactionFakes.FixedInspector<MachineIdentityResult> inspector = new(Identity(true)) { Failure = new InvalidOperationException("provider failed") };
        PlatformPolicy policy = Policy(identity: inspector);

        MachineIdentityResult identity = policy.ReadIdentity();

        Assert.False(identity.ExpectedModelConfirmed);
        Assert.False(PlatformPolicy.IsContractedMachine(identity));
        Assert.Null(identity.BiosVersion);
        Assert.Null(identity.ServiceTag);
        string error = Assert.Single(identity.Errors);
        Assert.Equal(PlatformPolicy.IdentityNotReadPrefix + "InvalidOperationException: provider failed", error);
        Assert.Equal("Expected model not confirmed: " + error, identity.ExpectedModelNotes);
    }

    [Fact]
    public void ReadIdentity_ThatDoesNotAnswerWithinTheBound_NeverConfirmsTheMachine()
    {
        using ManualResetEventSlim release = new(false);
        TransactionFakes.FixedInspector<MachineIdentityResult> inspector = new(Identity(true)) { DuringInspect = () => release.Wait(TimeSpan.FromSeconds(30)) };
        PlatformPolicy policy = new(
            inspector,
            new TransactionFakes.FixedInspector<SecureBootPolicyResult>(TransactionFakes.SecureBootOn()),
            new TransactionFakes.FixedInspector<BitLockerResult>(TransactionFakes.BitLockerOn()),
            null,
            Serilog.Core.Logger.None,
            TimeSpan.FromMilliseconds(50),
            Unit());
        try
        {
            MachineIdentityResult identity = policy.ReadIdentity();

            Assert.False(PlatformPolicy.IsContractedMachine(identity));
            Assert.StartsWith(PlatformPolicy.IdentityNotReadPrefix, Assert.Single(identity.Errors));
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public void ReadBitLocker_OfAnInspectorThatThrows_IsAssessedAsUnknown_WhichCountsAsOn()
    {
        TransactionFakes.FixedInspector<BitLockerResult> inspector = new(TransactionFakes.BitLockerOff()) { Failure = new TimeoutException("WMI BitLocker state did not complete within 60 seconds") };
        PlatformPolicy policy = Policy(bitLocker: inspector);

        BitLockerResult result = policy.ReadBitLocker();
        BitLockerAssessment assessment = BitLockerPreflight.Assess(result);

        Assert.Empty(result.Volumes);
        Assert.StartsWith(PlatformPolicy.BitLockerNotReadPrefix, Assert.Single(result.Errors));
        Assert.Equal(BitLockerState.Unknown, assessment.State);
        Assert.True(assessment.NeedsChoice);
    }

    [Fact]
    public void ReadSecureBoot_OfAnInspectorThatThrows_IsNull_AndSaysNotRead()
    {
        TransactionFakes.FixedInspector<SecureBootPolicyResult> inspector = new(TransactionFakes.SecureBootOn()) { Failure = new InvalidOperationException("registry unreadable") };
        PlatformPolicy policy = Policy(secureBoot: inspector);

        SecureBootPolicyResult? result = policy.ReadSecureBoot();

        Assert.Null(result);
        Assert.Equal("Secure Boot: state not read. This application never changes it.", PlatformPolicy.DescribeSecureBoot(result));
    }

    [Fact]
    public void ReadTpm_IsNullWithoutAnInspector_AndTheFactsWithOne()
    {
        TpmInfoResult tpm = new(true, "1.2, 2, 3", true, true, Array.Empty<string>());

        Assert.Null(Policy().ReadTpm());
        Assert.Same(tpm, Policy(tpm: new TransactionFakes.FixedInspector<TpmInfoResult>(tpm)).ReadTpm());
    }

    [Theory]
    [InlineData(true, true, false, "Secure Boot: on, user mode. This application never changes it.")]
    [InlineData(true, true, true, "Secure Boot: on, setup mode. This application never changes it.")]
    [InlineData(false, false, false, "Secure Boot: off. This application never changes it.")]
    [InlineData(null, true, false, "Secure Boot: on, user mode. This application never changes it.")]
    public void DescribeSecureBoot_PrefersTheVariableOverTheRegistry(bool? variable, bool? registry, bool setupMode, string expected)
    {
        SecureBootPolicyResult policy = new(registry, variable, setupMode, null, null, null, true, true, Array.Empty<string>());

        Assert.Equal(expected, PlatformPolicy.DescribeSecureBoot(policy));
        Assert.Equal(variable ?? registry, PlatformPolicy.SecureBootEnabled(policy));
    }

    // ---- the unit check (decided 25 September 2026: M3 writes only on the developer's unit) ----

    [Fact]
    public void ReadUnit_OfTheTestUnitsMachineId_IsConfirmed()
    {
        Assert.Equal(UnitMatch.Confirmed, Policy(unit: Unit(TestUnitMachineId)).ReadUnit());
        Assert.True(PlatformPolicy.IsEnabledMachine(Identity(true), UnitMatch.Confirmed));
    }

    /// <summary>The tester's case: the contracted model on BIOS A18 with another Windows machine ID is not the unit, so changes are not enabled.</summary>
    [Fact]
    public void ReadUnit_OfAnotherMachineId_OnAnOptiPlex7020OnA18_IsAnotherUnit_AndNotEnabled()
    {
        MachineIdentityResult identity = Identity(true);
        PlatformPolicy policy = Policy(identity: new TransactionFakes.FixedInspector<MachineIdentityResult>(identity), unit: Unit(OtherUnitMachineId));

        UnitMatch unit = policy.ReadUnit();

        Assert.True(PlatformPolicy.IsContractedMachine(policy.ReadIdentity()));
        Assert.Equal(UnitMatch.OtherUnit, unit);
        Assert.False(PlatformPolicy.IsEnabledMachine(identity, unit));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    [InlineData("not a machine id")]
    [InlineData("3f2504e0-4f89-41d3-9a0c-0305e82c33")]
    [InlineData("3f2504e0-4f89-41d3-9a0c-0305e82c3301-00")]
    public void ReadUnit_OfAMachineIdThatIsMissingEmptyOrNotAGuid_IsNotRead_AndNeverConfirmed(string? machineId)
    {
        UnitMatch unit = Policy(unit: Unit(machineId)).ReadUnit();

        Assert.Equal(UnitMatch.MachineIdNotRead, unit);
        Assert.Null(PlatformPolicy.MachineIdSha256(machineId));
        Assert.False(PlatformPolicy.IsEnabledMachine(Identity(true), unit));
    }

    [Fact]
    public void ReadUnit_OfAMachineIdSourceThatThrows_IsNotRead()
    {
        EnabledUnit throwing = new(() => throw new UnauthorizedAccessException("registry denied"), TestUnitMachineIdSha256);

        Assert.Equal(UnitMatch.MachineIdNotRead, throwing.Check());
        Assert.Equal(UnitMatch.MachineIdNotRead, Policy(unit: throwing).ReadUnit());
    }

    /// <summary>The model, the BIOS version and the unit must all hold: the OptiPlex 7020 on A17 with the unit's machine ID is not enabled, nor is another model.</summary>
    [Theory]
    [InlineData(true, "A18", UnitMatch.Confirmed, true)]
    [InlineData(true, "A17", UnitMatch.Confirmed, false)]
    [InlineData(false, "A18", UnitMatch.Confirmed, false)]
    [InlineData(true, "A18", UnitMatch.OtherUnit, false)]
    [InlineData(true, "A18", UnitMatch.MachineIdNotRead, false)]
    [InlineData(true, null, UnitMatch.Confirmed, false)]
    public void IsEnabledMachine_NeedsTheModel_BiosA18_AndTheUnit(bool expectedModelConfirmed, string? biosVersion, UnitMatch unit, bool enabled)
    {
        MachineIdentityResult identity = Identity(true) with { ExpectedModelConfirmed = expectedModelConfirmed, BiosVersion = biosVersion };

        Assert.Equal(enabled, PlatformPolicy.IsEnabledMachine(identity, unit));
        Assert.False(PlatformPolicy.IsEnabledMachine(null, unit));
    }

    [Theory]
    [InlineData("3F2504E0-4F89-41D3-9A0C-0305E82C3301")]
    [InlineData("  3f2504e0-4f89-41d3-9a0c-0305e82c3301  ")]
    [InlineData("\t3F2504E0-4f89-41D3-9a0c-0305E82C3301\r\n")]
    public void MachineIdSha256_OfTheCanonicalForm_IgnoresCaseAndSurroundingWhiteSpace(string written)
    {
        string expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(TestUnitMachineId))).ToLowerInvariant();

        Assert.Equal(expected, PlatformPolicy.MachineIdSha256(TestUnitMachineId));
        Assert.Equal(expected, PlatformPolicy.MachineIdSha256(written));
        Assert.Equal(UnitMatch.Confirmed, Unit(written).Check());
    }

    [Fact]
    public void EnabledUnitMachineIdSha256_Is64LowercaseHexadecimalCharacters()
    {
        Assert.Matches("^[0-9a-f]{64}$", PlatformPolicy.EnabledUnitMachineIdSha256);
    }

    /// <summary>
    /// The production paths bind to the constant: the Windows composition, the public constructors
    /// and the public unit check compare with <see cref="PlatformPolicy.EnabledUnitMachineIdSha256"/>
    /// and read the machine ID through <see cref="PlatformPolicy.ReadWindowsMachineId"/>. Nothing
    /// here runs a check, so the registry is never read.
    /// </summary>
    [Fact]
    public void TheProductionPaths_CompareWithTheConstant_ThroughTheWindowsRead_WithoutReadingIt()
    {
        TransactionFakes.FixedInspector<MachineIdentityResult> identity = new(Identity(true));
        TransactionFakes.FixedInspector<SecureBootPolicyResult> secureBoot = new(TransactionFakes.SecureBootOn());
        TransactionFakes.FixedInspector<BitLockerResult> bitLocker = new(TransactionFakes.BitLockerOn());
        EnabledUnit[] production =
        {
            PlatformPolicy.CreateWindows(new InMemoryFirmwareVariableStore(Array.Empty<FirmwareVariable>()), null).Unit,
            new PlatformPolicy(identity, secureBoot, bitLocker, null).Unit,
            new PlatformPolicy(identity, secureBoot, bitLocker, null, null).Unit,
            new PlatformPolicy(identity, secureBoot, bitLocker, null, new EnabledUnit(), null).Unit,
            new EnabledUnit(),
        };
        MethodInfo windowsRead = typeof(PlatformPolicy).GetMethod(nameof(PlatformPolicy.ReadWindowsMachineId), BindingFlags.Public | BindingFlags.Static)!;

        Assert.All(production, unit =>
        {
            Assert.Equal(PlatformPolicy.EnabledUnitMachineIdSha256, unit.ExpectedSha256);
            Assert.Equal(windowsRead, unit.MachineIdSource.Method);
            Assert.Null(unit.MachineIdSource.Target);
        });
        Assert.Equal(0, identity.Calls);
    }

    /// <summary>
    /// The only constructor that takes another hash is internal; the public surface cannot build a
    /// unit check or a policy bound to anything but the constant.
    /// </summary>
    [Fact]
    public void NoPublicConstructorTakesAnExpectedHash()
    {
        foreach (Type type in new[] { typeof(EnabledUnit), typeof(PlatformPolicy) })
        {
            foreach (ConstructorInfo constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.DoesNotContain(constructor.GetParameters(), parameter => parameter.ParameterType == typeof(string) || parameter.ParameterType == typeof(Func<string?>));
            }
        }

        Assert.Single(typeof(EnabledUnit).GetConstructors(BindingFlags.Public | BindingFlags.Instance), constructor => constructor.GetParameters().Length == 0);
    }

    private static PlatformPolicy Policy(
        IPlatformInspector<MachineIdentityResult>? identity = null,
        IPlatformInspector<SecureBootPolicyResult>? secureBoot = null,
        IPlatformInspector<BitLockerResult>? bitLocker = null,
        IPlatformInspector<TpmInfoResult>? tpm = null,
        EnabledUnit? unit = null)
    {
        // The unit check is always the tests' own, over a made-up machine ID: no test reads the registry.
        return new PlatformPolicy(
            identity ?? new TransactionFakes.FixedInspector<MachineIdentityResult>(Identity(true)),
            secureBoot ?? new TransactionFakes.FixedInspector<SecureBootPolicyResult>(TransactionFakes.SecureBootOn()),
            bitLocker ?? new TransactionFakes.FixedInspector<BitLockerResult>(TransactionFakes.BitLockerOn()),
            tpm,
            Serilog.Core.Logger.None,
            WmiHelper.QueryTimeout,
            unit ?? Unit());
    }
}
