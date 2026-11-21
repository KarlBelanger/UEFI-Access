// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Platform;
using UefiAccess.Core.Settings;
using UefiAccess.Core.Settings.Dell;
using UefiAccess.Core.Variables;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// Tests for the pure logic inside the platform inspectors: the expected-model check, CPU
/// generation detection, BitLocker PCR profile text and protector names, the cctk and
/// PowerShell output parsers, Secure Boot source agreement, and WMI value conversions.
/// None of these tests touches WMI, the registry, or a child process.
/// </summary>
public sealed class PlatformLogicTests
{
    private const string HaswellI5 = "Intel(R) Core(TM) i5-4590 CPU @ 3.30GHz";
    private const string RaptorLakeI5 = "Intel(R) Core(TM) i5-14500";

    // ----- Expected-model check (2014 OptiPlex 7020 versus the 2024 relaunch) -----

    [Fact]
    public void ExpectedModel_2014OptiPlex7020_IsConfirmed()
    {
        ExpectedModelEvaluation evaluation = MachineIdentity.EvaluateExpectedModel("OptiPlex 7020", "A18", HaswellI5);

        Assert.True(evaluation.Confirmed);
        Assert.True(evaluation.ModelMatched);
        Assert.True(evaluation.BiosMatched);
        Assert.True(evaluation.CpuMatched);
        Assert.False(evaluation.LooksLikeRelaunch);
        Assert.Contains("Expected model confirmed", evaluation.Notes, StringComparison.Ordinal);
        Assert.Contains("contains 7020 (match)", evaluation.Notes, StringComparison.Ordinal);
        Assert.Contains("Dell A-series version (match)", evaluation.Notes, StringComparison.Ordinal);
        Assert.Contains("4th-generation Intel (Haswell) part (match)", evaluation.Notes, StringComparison.Ordinal);
        Assert.DoesNotContain("2024 relaunch", evaluation.Notes, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Intel(R) Pentium(R) CPU G3220 @ 3.00GHz")]
    [InlineData("Intel(R) Pentium(R) CPU G3450 @ 3.40GHz")]
    [InlineData("Intel(R) Celeron(R) CPU G1840 @ 2.80GHz")]
    [InlineData("Intel(R) Core(TM) i7-4790K CPU @ 4.00GHz")]
    [InlineData("Intel(R) Core(TM) i3-4160 CPU @ 3.60GHz")]
    [InlineData("intel core i5-4570")]
    public void ExpectedModel_OtherHaswellParts_AreConfirmed(string cpu)
    {
        ExpectedModelEvaluation evaluation = MachineIdentity.EvaluateExpectedModel("OptiPlex 7020", "A25", cpu);

        Assert.True(evaluation.CpuMatched);
        Assert.True(evaluation.Confirmed);
        Assert.False(evaluation.LooksLikeRelaunch);
    }

    [Theory]
    [InlineData("Intel(R) Core(TM) i5-3470 CPU @ 3.20GHz")]
    [InlineData("Intel(R) Core(TM) i5-6500 CPU @ 3.20GHz")]
    [InlineData("Intel(R) Pentium(R) CPU G2020 @ 2.90GHz")]
    [InlineData("Intel(R) Celeron(R) CPU G3900 @ 2.80GHz")]
    [InlineData("Intel(R) Pentium(R) Gold G5400 CPU @ 3.70GHz")]
    [InlineData("AMD Ryzen 5 5600G with Radeon Graphics")]
    public void ExpectedModel_NonHaswellCpu_IsNotConfirmed(string cpu)
    {
        ExpectedModelEvaluation evaluation = MachineIdentity.EvaluateExpectedModel("OptiPlex 7020", "A18", cpu);

        Assert.False(evaluation.CpuMatched);
        Assert.False(evaluation.Confirmed);
        Assert.Contains("CPU did not match", evaluation.Notes, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpectedModel_2024Relaunch_IsNotConfirmed_AndWarns()
    {
        ExpectedModelEvaluation evaluation = MachineIdentity.EvaluateExpectedModel("OptiPlex 7020", "1.5.0", RaptorLakeI5);

        Assert.False(evaluation.Confirmed);
        Assert.True(evaluation.ModelMatched);
        Assert.False(evaluation.BiosMatched);
        Assert.False(evaluation.CpuMatched);
        Assert.True(evaluation.LooksLikeRelaunch);
        Assert.Contains("this looks like the 2024 relaunch, not the planned unit", evaluation.Notes, StringComparison.Ordinal);
        Assert.Contains("x.y.z scheme", evaluation.Notes, StringComparison.Ordinal);
        Assert.Contains("14th generation", evaluation.Notes, StringComparison.Ordinal);
        Assert.Contains("Expected model not confirmed", evaluation.Notes, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpectedModel_TwelfthGenCpuAlone_Warns()
    {
        ExpectedModelEvaluation evaluation = MachineIdentity.EvaluateExpectedModel("OptiPlex 7020", "A18", "12th Gen Intel(R) Core(TM) i5-12500");

        Assert.False(evaluation.Confirmed);
        Assert.True(evaluation.LooksLikeRelaunch);
        Assert.Contains("2024 relaunch", evaluation.Notes, StringComparison.Ordinal);
        Assert.Contains("12th generation or newer", evaluation.Notes, StringComparison.Ordinal);
        Assert.DoesNotContain("x.y.z", evaluation.Notes, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpectedModel_RelaunchBiosAlone_Warns()
    {
        ExpectedModelEvaluation evaluation = MachineIdentity.EvaluateExpectedModel("OptiPlex 7020 Micro", "1.2.0", null);

        Assert.False(evaluation.Confirmed);
        Assert.True(evaluation.LooksLikeRelaunch);
        Assert.Contains("2024 relaunch", evaluation.Notes, StringComparison.Ordinal);
        Assert.Contains("x.y.z scheme", evaluation.Notes, StringComparison.Ordinal);
        Assert.Contains("CPU: not available", evaluation.Notes, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ASUS TUF Gaming F16 FX608JPR_FX608JPR", "FX608JPR.312", "Intel(R) Core(TM) i7-14650HX")]
    [InlineData("Latitude 5440", "1.12.0", "13th Gen Intel(R) Core(TM) i5-1345U")]
    public void ExpectedModel_OtherModelWithNewCpuOrXyzBios_DoesNotClaimRelaunch(string model, string bios, string cpu)
    {
        ExpectedModelEvaluation evaluation = MachineIdentity.EvaluateExpectedModel(model, bios, cpu);

        Assert.False(evaluation.Confirmed);
        Assert.False(evaluation.ModelMatched);
        Assert.False(evaluation.LooksLikeRelaunch);
        Assert.Contains("Expected model not confirmed: model did not match", evaluation.Notes, StringComparison.Ordinal);
        Assert.DoesNotContain("2024 relaunch", evaluation.Notes, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpectedModel_UnknownModelWithRelaunchSignals_StillWarns()
    {
        ExpectedModelEvaluation evaluation = MachineIdentity.EvaluateExpectedModel(null, "1.5.0", "13th Gen Intel(R) Core(TM) i5-13500");

        Assert.False(evaluation.Confirmed);
        Assert.True(evaluation.LooksLikeRelaunch);
        Assert.Contains("2024 relaunch", evaluation.Notes, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("", "", "")]
    [InlineData("   ", "\t", " ")]
    public void ExpectedModel_UnknownIdentity_IsNotConfirmed_WithoutWarning(string? model, string? bios, string? cpu)
    {
        ExpectedModelEvaluation evaluation = MachineIdentity.EvaluateExpectedModel(model, bios, cpu);

        Assert.False(evaluation.Confirmed);
        Assert.False(evaluation.ModelMatched);
        Assert.False(evaluation.BiosMatched);
        Assert.False(evaluation.CpuMatched);
        Assert.False(evaluation.LooksLikeRelaunch);
        Assert.Contains("Model: not available.", evaluation.Notes, StringComparison.Ordinal);
        Assert.Contains("BIOS version: not available.", evaluation.Notes, StringComparison.Ordinal);
        Assert.Contains("CPU: not available.", evaluation.Notes, StringComparison.Ordinal);
        Assert.Contains("Expected model not confirmed", evaluation.Notes, StringComparison.Ordinal);
        Assert.DoesNotContain("2024 relaunch", evaluation.Notes, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpectedModel_WrongModelWithMatchingBiosAndCpu_ExplainsEachSignal()
    {
        ExpectedModelEvaluation evaluation = MachineIdentity.EvaluateExpectedModel("OptiPlex 9020", "A25", "Intel(R) Core(TM) i7-4790 CPU @ 3.60GHz");

        Assert.False(evaluation.Confirmed);
        Assert.False(evaluation.ModelMatched);
        Assert.True(evaluation.BiosMatched);
        Assert.True(evaluation.CpuMatched);
        Assert.False(evaluation.LooksLikeRelaunch);
        Assert.Contains("model did not match, BIOS version matched, CPU matched", evaluation.Notes, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("optiplex 7020", true)]
    [InlineData("OPTIPLEX 7020 SFF", true)]
    [InlineData("Dell OptiPlex 7020", true)]
    [InlineData("OptiPlex 7010", false)]
    [InlineData("Precision 3660", false)]
    public void ExpectedModel_ModelCheck_IsCaseInsensitiveContains(string model, bool expected)
    {
        ExpectedModelEvaluation evaluation = MachineIdentity.EvaluateExpectedModel(model, "A18", HaswellI5);

        Assert.Equal(expected, evaluation.ModelMatched);
        Assert.Equal(expected, evaluation.Confirmed);
    }

    [Theory]
    [InlineData("A18", true)]
    [InlineData("A05", true)]
    [InlineData(" A18 ", true)]
    [InlineData("a18", false)]
    [InlineData("A1", false)]
    [InlineData("A180", false)]
    [InlineData("1.5.0", false)]
    [InlineData("A18 (revision)", false)]
    public void ExpectedModel_BiosCheck_RequiresAThenTwoDigits(string bios, bool expected)
    {
        ExpectedModelEvaluation evaluation = MachineIdentity.EvaluateExpectedModel("OptiPlex 7020", bios, HaswellI5);

        Assert.Equal(expected, evaluation.BiosMatched);
        Assert.Equal(expected, evaluation.Confirmed);
    }

    [Theory]
    [InlineData(HaswellI5, 4)]
    [InlineData("Intel(R) Core(TM) i7-4790K CPU @ 4.00GHz", 4)]
    [InlineData("Intel(R) Core(TM) i5-3470 CPU @ 3.20GHz", 3)]
    [InlineData("Intel(R) Core(TM) i7-8700 CPU @ 3.20GHz", 8)]
    [InlineData("Intel(R) Core(TM) i9-9900K CPU @ 3.60GHz", 9)]
    [InlineData("Intel(R) Core(TM) i5-10400F CPU @ 2.90GHz", 10)]
    [InlineData("11th Gen Intel(R) Core(TM) i7-1165G7 @ 2.80GHz", 11)]
    [InlineData("12th Gen Intel(R) Core(TM) i5-12500", 12)]
    [InlineData("13th Gen Intel(R) Core(TM) i7-13700", 13)]
    [InlineData(RaptorLakeI5, 14)]
    [InlineData("Intel(R) Core(TM) 5 processor 14500", 14)]
    [InlineData("Intel(R) Pentium(R) CPU G3220 @ 3.00GHz", null)]
    [InlineData("AMD Ryzen 5 5600G with Radeon Graphics", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void CpuGeneration_IsExtractedFromProcessorName(string? cpu, int? expected)
    {
        Assert.Equal(expected, MachineIdentity.TryGetIntelCoreGeneration(cpu));
    }

    [Theory]
    [InlineData(RaptorLakeI5, true)]
    [InlineData("12th Gen Intel(R) Core(TM) i5-12500", true)]
    [InlineData("13th Gen Intel(R) Core(TM) i7-13700", true)]
    [InlineData("Intel(R) Core(TM) Ultra 7 155H", true)]
    [InlineData("Intel(R) Core(TM) 7 150U", true)]
    [InlineData("Intel(R) Core(TM) 5 processor 14500", true)]
    [InlineData("11th Gen Intel(R) Core(TM) i7-1165G7 @ 2.80GHz", false)]
    [InlineData("Intel(R) Core(TM) i7-11700", false)]
    [InlineData(HaswellI5, false)]
    [InlineData("Intel(R) Pentium(R) CPU G3220 @ 3.00GHz", false)]
    [InlineData(null, false)]
    public void CpuGeneration_TwelfthGenOrNewerIsDetected(string? cpu, bool expected)
    {
        Assert.Equal(expected, MachineIdentity.IsTwelfthGenOrNewer(cpu));
    }

    // ----- BitLocker pure logic -----

    [Theory]
    [InlineData(new[] { 7, 11 }, "PCR 7+11")]
    [InlineData(new[] { 11, 7 }, "PCR 7+11")]
    [InlineData(new[] { 7, 7, 11 }, "PCR 7+11")]
    [InlineData(new[] { 0, 2, 4, 11 }, "PCR 0,2,4,11 (legacy)")]
    [InlineData(new[] { 11, 4, 2, 0 }, "PCR 0,2,4,11 (legacy)")]
    [InlineData(new[] { 0, 2, 4, 7, 11 }, "PCR 0,2,4,7,11")]
    [InlineData(new[] { 0, 2, 4, 8, 9, 10, 11 }, "PCR 0,2,4,8,9,10,11")]
    [InlineData(new[] { 7 }, "PCR 7")]
    [InlineData(new int[0], null)]
    public void PcrProfileText_RendersKnownProfilesExplicitly(int[] profile, string? expected)
    {
        Assert.Equal(expected, BitLockerInspector.PcrProfileText(profile));
    }

    [Fact]
    public void NormalizePcrProfile_SortsAndRemovesDuplicates()
    {
        Assert.Equal(new[] { 0, 2, 4, 11 }, BitLockerInspector.NormalizePcrProfile(new[] { 11, 2, 0, 4, 2 }));
        Assert.Empty(BitLockerInspector.NormalizePcrProfile(Array.Empty<int>()));
    }

    [Theory]
    [InlineData(0, "Unknown or other")]
    [InlineData(1, "TPM")]
    [InlineData(2, "External key")]
    [InlineData(3, "Numerical password")]
    [InlineData(4, "TPM+PIN")]
    [InlineData(5, "TPM+startup key")]
    [InlineData(6, "TPM+PIN+startup key")]
    [InlineData(7, "Public key")]
    [InlineData(8, "Passphrase")]
    [InlineData(9, "TPM certificate")]
    [InlineData(10, "CryptoAPI")]
    [InlineData(42, "Unknown (42)")]
    [InlineData(-1, "Unknown (-1)")]
    public void ProtectorTypeName_MapsEveryDocumentedType(int type, string expected)
    {
        Assert.Equal(expected, BitLockerInspector.ProtectorTypeName(type));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(7, false)]
    [InlineData(8, false)]
    [InlineData(9, false)]
    [InlineData(10, false)]
    public void IsTpmFamily_OnlyTpmBasedProtectorsHaveProfiles(int type, bool expected)
    {
        Assert.Equal(expected, BitLockerInspector.IsTpmFamily(type));
    }

    [Theory]
    [InlineData(0, "Off")]
    [InlineData(1, "On")]
    [InlineData(2, "Unknown")]
    [InlineData(7, "Unknown (7)")]
    public void ProtectionStatusText_MapsDocumentedValues(int status, string expected)
    {
        Assert.Equal(expected, BitLockerInspector.ProtectionStatusText(status));
    }

    // ----- Dell Command | Configure output parser -----

    [Theory]
    [InlineData("secureboot=enable", true)]
    [InlineData("secureboot=disable", false)]
    [InlineData("SecureBoot=Enabled", true)]
    [InlineData("SECUREBOOT=DISABLED", false)]
    [InlineData("secureboot = on", true)]
    [InlineData("secureboot=off", false)]
    [InlineData("Dell Command | Configure\r\n\r\nsecureboot=disable\r\n", false)]
    [InlineData("bootorder=hdd,usb\nsecureboot=enable", true)]
    [InlineData("secureboot=", null)]
    [InlineData("secureboot=maybe", null)]
    [InlineData("bootorder=hdd,usb", null)]
    [InlineData("Error: This option is not supported on this system", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ParseCctkSecureBoot_ReadsTheSecureBootLine(string? output, bool? expected)
    {
        Assert.Equal(expected, DellInterfaceProbe.ParseCctkSecureBoot(output));
    }

    [Fact]
    public void CctkPaths_FollowTheDellCommandConfigureLayout()
    {
        string path = DellInterfaceProbe.CctkPathUnder("ProgramFilesRoot");
        Assert.Equal(Path.Combine("ProgramFilesRoot", "Dell", "Command Configure", "X86_64", "cctk.exe"), path);

        IReadOnlyList<string> candidates = DellInterfaceProbe.CctkCandidatePaths();
        Assert.All(candidates, candidate => Assert.EndsWith("cctk.exe", candidate, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(candidates.Count, candidates.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // ----- Secure Boot policy pure logic -----

    [Theory]
    [InlineData("True", true)]
    [InlineData("True\r\n", true)]
    [InlineData("  false  ", false)]
    [InlineData("WARNING: some banner\nTrue", true)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("Confirm-SecureBootUEFI : Cmdlet not supported on this platform.", null)]
    [InlineData("1", null)]
    public void ParsePowerShellBoolean_AcceptsOnlyTrueOrFalse(string? output, bool? expected)
    {
        Assert.Equal(expected, SecureBootPolicyInspector.ParsePowerShellBoolean(output));
    }

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(false, false, false, true)]
    [InlineData(true, true, null, true)]
    [InlineData(true, null, null, true)]
    [InlineData(null, false, false, true)]
    [InlineData(null, null, true, true)]
    [InlineData(true, false, null, false)]
    [InlineData(false, false, true, false)]
    [InlineData(null, true, false, false)]
    [InlineData(null, null, null, false)]
    public void ComputeSourcesAgree_RequiresOneSourceAndUnanimity(bool? registry, bool? variable, bool? powerShell, bool expected)
    {
        Assert.Equal(expected, SecureBootPolicyInspector.ComputeSourcesAgree(registry, variable, powerShell));
    }

    [Fact]
    public void InterpretRead_MapsEveryReadStatus()
    {
        SecureBootVariableReading on = SecureBootPolicyInspector.InterpretRead("SecureBoot", Success("SecureBoot", 1));
        Assert.True(on.Value);
        Assert.True(on.Readable);
        Assert.Null(on.Note);

        SecureBootVariableReading off = SecureBootPolicyInspector.InterpretRead("SetupMode", Success("SetupMode", 0));
        Assert.False(off.Value);
        Assert.True(off.Readable);
        Assert.Null(off.Note);

        SecureBootVariableReading empty = SecureBootPolicyInspector.InterpretRead("SecureBoot", Success("SecureBoot"));
        Assert.Null(empty.Value);
        Assert.True(empty.Readable);
        Assert.Contains("zero-length", empty.Note, StringComparison.Ordinal);

        SecureBootVariableReading wide = SecureBootPolicyInspector.InterpretRead("SecureBoot", Success("SecureBoot", 1, 0));
        Assert.True(wide.Value);
        Assert.True(wide.Readable);
        Assert.Contains("expected 1 byte", wide.Note, StringComparison.Ordinal);

        SecureBootVariableReading missing = SecureBootPolicyInspector.InterpretRead("DeployedMode", new ReadResult(null, ReadStatus.NotFound, "not found", 0));
        Assert.Null(missing.Value);
        Assert.False(missing.Readable);
        Assert.Null(missing.Note);

        SecureBootVariableReading privilege = SecureBootPolicyInspector.InterpretRead("SecureBoot", new ReadResult(null, ReadStatus.PrivilegeNotHeld, "ERROR_PRIVILEGE_NOT_HELD (1314)", 0));
        Assert.Null(privilege.Value);
        Assert.False(privilege.Readable);
        Assert.Contains("ERROR_PRIVILEGE_NOT_HELD", privilege.Note, StringComparison.Ordinal);
        Assert.StartsWith("SecureBoot:", privilege.Note, StringComparison.Ordinal);

        SecureBootVariableReading failed = SecureBootPolicyInspector.InterpretRead("SecureBoot", new ReadResult(null, ReadStatus.Failed, null, 0));
        Assert.Null(failed.Value);
        Assert.False(failed.Readable);
        Assert.Contains("Failed", failed.Note, StringComparison.Ordinal);

        SecureBootVariableReading noVariable = SecureBootPolicyInspector.InterpretRead("SecureBoot", new ReadResult(null, ReadStatus.Success, null, 0));
        Assert.Null(noVariable.Value);
        Assert.False(noVariable.Readable);
        Assert.NotNull(noVariable.Note);

        SecureBootVariableReading nullRead = SecureBootPolicyInspector.InterpretRead("SecureBoot", null);
        Assert.Null(nullRead.Value);
        Assert.False(nullRead.Readable);
        Assert.NotNull(nullRead.Note);
    }

    [NonWindowsFact]
    public void SecureBootPolicyInspector_WithFakeStore_EvaluatesVariablesWithoutThrowing()
    {
        // The full Inspect path reads the registry on Windows; unit tests stay away from it.
        FakeStore store = new FakeStore(ReadStatus.NotFound).Add("SecureBoot", 1).Add("SetupMode", 0);
        SecureBootPolicyInspector inspector = new(store, runPowerShellCrossCheck: false);

        SecureBootPolicyResult result = inspector.Inspect();

        Assert.True(result.VariableSecureBoot);
        Assert.False(result.VariableSetupMode);
        Assert.Null(result.VariableDeployedMode);
        Assert.Null(result.VariableAuditMode);
        Assert.Null(result.RegistryEnabled);
        Assert.Null(result.PowerShellConfirm);
        Assert.True(result.Capable);
        Assert.True(result.SourcesAgree);
        Assert.Contains(result.Errors, error => error.Contains("registry", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Errors, error => error.Contains("Confirm-SecureBootUEFI", StringComparison.Ordinal));
    }

    [NonWindowsFact]
    public void SecureBootPolicyInspector_WhenVariablesUnreadable_IsNotCapableAndDoesNotAgree()
    {
        SecureBootPolicyInspector inspector = new(new FakeStore(ReadStatus.NotUefi), runPowerShellCrossCheck: true);

        SecureBootPolicyResult result = inspector.Inspect();

        Assert.Null(result.VariableSecureBoot);
        Assert.Null(result.VariableSetupMode);
        Assert.False(result.Capable);
        Assert.False(result.SourcesAgree);
        Assert.Contains(result.Errors, error => error.StartsWith("SecureBoot:", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.StartsWith("SetupMode:", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("Confirm-SecureBootUEFI", StringComparison.Ordinal));
    }

    [NonWindowsFact]
    public void SecureBootPolicyInspector_WhenStoreThrows_ReportsInsteadOfThrowing()
    {
        SecureBootPolicyInspector inspector = new(new ThrowingStore(), runPowerShellCrossCheck: false);

        SecureBootPolicyResult result = inspector.Inspect();

        Assert.False(result.Capable);
        Assert.Contains(result.Errors, error => error.Contains("boom", StringComparison.Ordinal));
    }

    // ----- Capable follows: PK present or SetupMode readable -----

    [NonWindowsFact]
    public void SecureBootPolicyInspector_WhenOnlyPkIsPresent_IsCapable()
    {
        FakeStore store = new FakeStore(ReadStatus.NotFound).Add("PK", new byte[64]);
        SecureBootPolicyInspector inspector = new(store, runPowerShellCrossCheck: false);

        SecureBootPolicyResult result = inspector.Inspect();

        Assert.True(result.Capable);
        Assert.Null(result.VariableSecureBoot);
        Assert.Null(result.VariableSetupMode);
        Assert.DoesNotContain(result.Errors, error => error.StartsWith("PK:", StringComparison.Ordinal));
    }

    [NonWindowsFact]
    public void SecureBootPolicyInspector_WhenOnlySecureBootIsReadable_IsNotCapable()
    {
        FakeStore store = new FakeStore(ReadStatus.NotFound).Add("SecureBoot", 1);
        SecureBootPolicyInspector inspector = new(store, runPowerShellCrossCheck: false);

        SecureBootPolicyResult result = inspector.Inspect();

        Assert.True(result.VariableSecureBoot);
        Assert.Null(result.VariableSetupMode);
        Assert.False(result.Capable);
    }

    [NonWindowsFact]
    public void SecureBootPolicyInspector_WhenPkReadFails_ReportsItAndIsNotCapable()
    {
        FakeStore store = new FakeStore(ReadStatus.AccessDenied).Add("SecureBoot", 1);
        SecureBootPolicyInspector inspector = new(store, runPowerShellCrossCheck: false);

        SecureBootPolicyResult result = inspector.Inspect();

        Assert.False(result.Capable);
        Assert.Contains(result.Errors, error => error.StartsWith("PK:", StringComparison.Ordinal));
    }

    [Fact]
    public void ComputeCapable_IsPkPresentOrSetupModeReadable()
    {
        Assert.True(SecureBootPolicyInspector.ComputeCapable(setupModeReadable: true, pkPresent: false));
        Assert.True(SecureBootPolicyInspector.ComputeCapable(setupModeReadable: false, pkPresent: true));
        Assert.True(SecureBootPolicyInspector.ComputeCapable(setupModeReadable: true, pkPresent: true));
        Assert.False(SecureBootPolicyInspector.ComputeCapable(setupModeReadable: false, pkPresent: false));
    }

    [Fact]
    public void InterpretPresence_TreatsAnyReadVariableAsPresent_AndNotFoundAsSilentlyAbsent()
    {
        SecureBootVariablePresence present = SecureBootPolicyInspector.InterpretPresence("PK", Success("PK", new byte[32]));
        Assert.True(present.Present);
        Assert.Null(present.Note);

        SecureBootVariablePresence empty = SecureBootPolicyInspector.InterpretPresence("PK", Success("PK"));
        Assert.True(empty.Present);
        Assert.Null(empty.Note);

        SecureBootVariablePresence notFound = SecureBootPolicyInspector.InterpretPresence("PK", new ReadResult(null, ReadStatus.NotFound, null, 0));
        Assert.False(notFound.Present);
        Assert.Null(notFound.Note);

        SecureBootVariablePresence denied = SecureBootPolicyInspector.InterpretPresence("PK", new ReadResult(null, ReadStatus.AccessDenied, "ERROR_ACCESS_DENIED", 0));
        Assert.False(denied.Present);
        Assert.Equal("PK: ERROR_ACCESS_DENIED", denied.Note);

        SecureBootVariablePresence noVariable = SecureBootPolicyInspector.InterpretPresence("PK", new ReadResult(null, ReadStatus.Success, null, 0));
        Assert.False(noVariable.Present);
        Assert.NotNull(noVariable.Note);

        SecureBootVariablePresence nullRead = SecureBootPolicyInspector.InterpretPresence("PK", null);
        Assert.False(nullRead.Present);
        Assert.NotNull(nullRead.Note);
    }

    // ----- Inspectors never throw on a host without WMI -----

    [NonWindowsFact]
    public void Inspectors_OnNonWindowsHost_ReturnErrorsInsteadOfThrowing()
    {
        // On Windows these would call WMI; hardware checks live in tools/gate, not in unit tests.
        MachineIdentityResult identity = new MachineIdentity().Inspect();
        Assert.NotEmpty(identity.Errors);
        Assert.Null(identity.Model);
        Assert.False(identity.ExpectedModelConfirmed);
        Assert.Contains("not available", identity.ExpectedModelNotes, StringComparison.Ordinal);

        TpmInfoResult tpm = new TpmInfo().Inspect();
        Assert.False(tpm.Present);
        Assert.NotEmpty(tpm.Errors);

        BitLockerResult bitLocker = new BitLockerInspector().Inspect();
        Assert.Empty(bitLocker.Volumes);
        Assert.NotEmpty(bitLocker.Errors);

        DellInterfaceProbeResult dell = new DellInterfaceProbe().Inspect();
        Assert.False(dell.DcmClassesPresent);
        Assert.Empty(dell.DcmClassesFound);
        Assert.Empty(dell.Samples);
        Assert.False(dell.CctkFound);
        Assert.Null(dell.CctkPath);
        Assert.False(dell.NativeBiosAttributesNamespacePresent);
        Assert.False(dell.NativeWmiSecurityNamespacePresent);
        Assert.Equal(0, dell.DcimClassCount);
        Assert.Null(dell.CctkSecureBootEnabled);
        Assert.NotEmpty(dell.Errors);

        // The Dell settings reader reads nothing off Windows, neither the machine nor an interface (DF1).
        DellSettingsResult dellSettings = new DellSettingsReader(null).Read(DellReadOptions.Default);
        Assert.Equal(DellReadOutcome.Unavailable, dellSettings.Outcome);
        Assert.Null(dellSettings.Source);
        DellInterfaceProblem notWindows = Assert.Single(dellSettings.Problems);
        Assert.Equal((DellInterface.Reader, DellProblemKind.NotWindows), (notWindows.Interface, notWindows.Kind));
        Assert.Equal(new DellMachine(null, null, null), dellSettings.Machine);
        Assert.Equal(DellAttributeCatalog.Default.Select(entry => entry.Id), dellSettings.Settings.Select(setting => setting.Id));
        Assert.All(dellSettings.Settings, setting => Assert.Equal(SettingState.NotRead, setting.State));
        Assert.Equal("Dell BIOS settings cannot be read. They can be read on Windows only.", DellSentences.UnavailableSentence(dellSettings));
    }

    // ----- WMI value conversions -----

    [Theory]
    [InlineData("20140723000000.000000+000", "2014-07-23")]
    [InlineData("  20200115120000.000000+060  ", "2020-01-15")]
    [InlineData("20240301", "2024-03-01")]
    [InlineData("A18", "A18")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void FormatCimDateTime_RendersIsoDates(string? raw, string? expected)
    {
        Assert.Equal(expected, WmiValues.FormatCimDateTime(raw));
    }

    [Fact]
    public void WmiValues_ToText_JoinsArraysAndTrimsText()
    {
        Assert.Equal("Enabled, Disabled", WmiValues.ToText(new[] { "Enabled", "Disabled" }));
        Assert.Equal("Enabled", WmiValues.ToText(new object?[] { " Enabled ", null, "" }));
        Assert.Equal("5", WmiValues.ToText(5u));
        Assert.Equal("True", WmiValues.ToText(true));
        Assert.Equal("A18", WmiValues.ToText("  A18 "));
        Assert.Null(WmiValues.ToText(null));
        Assert.Null(WmiValues.ToText("   "));
        Assert.Null(WmiValues.ToText(Array.Empty<string>()));
    }

    [Fact]
    public void WmiValues_ToTextList_ReturnsOneEntryPerElement()
    {
        Assert.Equal(new[] { "Enabled", "Disabled" }, WmiValues.ToTextList(new[] { "Enabled", "Disabled" }));
        Assert.Equal(new[] { "1", "2" }, WmiValues.ToTextList(new object?[] { 1, null, 2 }));
        Assert.Equal(new[] { "single" }, WmiValues.ToTextList("single"));
        Assert.Empty(WmiValues.ToTextList(null));
        Assert.Empty(WmiValues.ToTextList("  "));
    }

    [Fact]
    public void WmiValues_ToNullableBool_AcceptsBoolNumbersAndText()
    {
        Assert.True(WmiValues.ToNullableBool(true));
        Assert.False(WmiValues.ToNullableBool(false));
        Assert.False(WmiValues.ToNullableBool(0u));
        Assert.True(WmiValues.ToNullableBool(1));
        Assert.True(WmiValues.ToNullableBool((ushort)7));
        Assert.True(WmiValues.ToNullableBool("True"));
        Assert.False(WmiValues.ToNullableBool("false"));
        Assert.False(WmiValues.ToNullableBool("0"));
        Assert.True(WmiValues.ToNullableBool("1"));
        Assert.Null(WmiValues.ToNullableBool("maybe"));
        Assert.Null(WmiValues.ToNullableBool(2.5));
        Assert.Null(WmiValues.ToNullableBool(null));
    }

    [Fact]
    public void WmiValues_ToNullableInt_AcceptsIntegralTypesAndDigits()
    {
        Assert.Equal(3, WmiValues.ToNullableInt(3u));
        Assert.Equal(7, WmiValues.ToNullableInt((ushort)7));
        Assert.Equal(4, WmiValues.ToNullableInt(4L));
        Assert.Equal(12, WmiValues.ToNullableInt(" 12 "));
        Assert.Equal(-1, WmiValues.ToNullableInt(-1));
        Assert.Null(WmiValues.ToNullableInt(uint.MaxValue));
        Assert.Null(WmiValues.ToNullableInt("x"));
        Assert.Null(WmiValues.ToNullableInt(1.5));
        Assert.Null(WmiValues.ToNullableInt(null));
    }

    // ----- helpers -----

    private static ReadResult Success(string name, params byte[] data)
    {
        FirmwareVariable variable = new(name, KnownGuids.EfiGlobalVariable, VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess, data);
        return new ReadResult(variable, ReadStatus.Success, null, data.Length);
    }

    /// <summary>A store that serves EFI global variables from a dictionary and reports a fixed status for everything else.</summary>
    private sealed class FakeStore : IFirmwareVariableStore
    {
        private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);
        private readonly ReadStatus _missingStatus;

        public FakeStore(ReadStatus missingStatus)
        {
            _missingStatus = missingStatus;
        }

        public FakeStore Add(string name, params byte[] data)
        {
            _values[name] = data;
            return this;
        }

        public EnumerationResult Enumerate()
        {
            return new EnumerationResult(Array.Empty<FirmwareVariable>(), EnumerationMethod.InMemory, 0, null, Array.Empty<string>());
        }

        public ReadResult Read(string name, Guid vendorGuid)
        {
            if (vendorGuid == KnownGuids.EfiGlobalVariable && _values.TryGetValue(name, out byte[]? data))
            {
                return Success(name, data);
            }

            string? error = _missingStatus == ReadStatus.NotFound ? null : "simulated " + _missingStatus;
            return new ReadResult(null, _missingStatus, error, 0);
        }
    }

    /// <summary>A store whose Read throws, to prove the inspector converts exceptions into Errors.</summary>
    private sealed class ThrowingStore : IFirmwareVariableStore
    {
        public EnumerationResult Enumerate()
        {
            return new EnumerationResult(Array.Empty<FirmwareVariable>(), EnumerationMethod.InMemory, 0, null, Array.Empty<string>());
        }

        public ReadResult Read(string name, Guid vendorGuid)
        {
            throw new InvalidOperationException("boom");
        }
    }
}
