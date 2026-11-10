// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using System.Text.RegularExpressions;
using UefiAccess.Cli;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Variables;
using Xunit;
using static UefiAccess.Core.Tests.CliTestHarness;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The gate's (b) and (d) findings as the tester reads them: Dell Command | Monitor classes that
/// are defined but do not answer are shown apart from the ones that respond (outcome A depends on
/// the second list), the samples carry the value names next to the codes, and a volume that is
/// still being encrypted reads "Encrypting" with a pending protection, never "Off".
/// </summary>
public sealed class GateReportRowsTests : IDisposable
{
    private readonly string _tempRoot;

    public GateReportRowsTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "gate-rows-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // Best effort; a leftover temp folder must not fail the test run.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort; a leftover temp folder must not fail the test run.
        }
    }

    [Fact]
    public void DefinedButSilentDcmClasses_AreNotReportedAsResponding()
    {
        DellInterfaceProbeResult dell = new(
            true,
            new[] { "DCIM_BIOSEnumeration", "DCIM_BIOSString", "DCIM_BIOSInteger", "DCIM_BIOSService" },
            Array.Empty<DellAttributeSample>(),
            true,
            @"C:\Program Files (x86)\Dell\Command Configure\X86_64\cctk.exe",
            "Dell Command | Configure Version 4.8.0",
            "secureboot=enable",
            false,
            false,
            new[] { "DCIM_BIOSEnumeration: ManagementException ProviderLoadFailure: Provider load failure" })
        {
            DcimClassCount = 38,
            CctkSecureBootEnabled = true,
        };
        string outFile = Path.Combine(_tempRoot, "gate.json");

        CliRun run = Run(CreatePlatform(Store(), dell: dell), "gate", "--out", outFile);

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        AssertRow(run.Output, "(b) Dell Command Monitor classes defined", "DCIM_BIOSEnumeration, DCIM_BIOSString, DCIM_BIOSInteger, DCIM_BIOSService");
        AssertRow(run.Output, "(b) Dell Command Monitor classes responding", "none");
        AssertRow(run.Output, "(b) Dell sample attributes", "0");
        Assert.Contains("Warning: dell probe: DCIM_BIOSEnumeration: ManagementException ProviderLoadFailure: Provider load failure\n", run.Error, StringComparison.Ordinal);

        using JsonDocument gate = JsonDocument.Parse(File.ReadAllText(outFile));
        JsonElement dellInterface = gate.RootElement.GetProperty("dellInterface");
        Assert.True(dellInterface.GetProperty("dcmClassesPresent").GetBoolean());
        Assert.Empty(dellInterface.GetProperty("dcmClassesResponding").EnumerateArray());
    }

    [Fact]
    public void Samples_CarryTheValueNamesNextToTheCodes()
    {
        DellInterfaceProbeResult dell = new(
            true,
            new[] { "DCIM_BIOSEnumeration" },
            new[] { new DellAttributeSample("Secure Boot", "2", "Enabled", new[] { "1", "2" }, new[] { "Disabled", "Enabled" }, "DCM") },
            false,
            null,
            null,
            null,
            false,
            false,
            Array.Empty<string>())
        {
            DcmClassesResponding = new[] { "DCIM_BIOSEnumeration" },
        };

        CliRun run = Run(CreatePlatform(Store(), dell: dell), "gate", "--json", "--out", Path.Combine(_tempRoot, "gate.json"));

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        JsonElement dellInterface = ParseSingleDocument(run.Output).GetProperty("dellInterface");
        Assert.Equal(new[] { "DCIM_BIOSEnumeration" }, dellInterface.GetProperty("dcmClassesResponding").EnumerateArray().Select(e => e.GetString()).ToArray());
        JsonElement sample = dellInterface.GetProperty("samples")[0];
        Assert.Equal(
            new[] { "name", "currentValue", "currentValueDescription", "possibleValues", "possibleValuesDescription", "source" },
            sample.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("2", sample.GetProperty("currentValue").GetString());
        Assert.Equal("Enabled", sample.GetProperty("currentValueDescription").GetString());
        Assert.Equal(new[] { "Disabled", "Enabled" }, sample.GetProperty("possibleValuesDescription").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    [Fact]
    public void VolumeStillEncrypting_ReadsEncryptingWithPendingProtection_NotOff()
    {
        BitLockerVolume encrypting = new(
            "C:",
            0,
            BitLockerInspector.ProtectionStatusText(0, 2),
            2,
            BitLockerInspector.ConversionStatusText(2),
            new[] { "TPM", "Numerical password" },
            new[] { 7, 11 },
            "PCR 7+11");
        BitLockerResult bitLocker = new(new[] { encrypting }, Array.Empty<string>());
        string outFile = Path.Combine(_tempRoot, "gate.json");

        CliRun run = Run(CreatePlatform(Store(), bitLocker: bitLocker), "gate", "--out", outFile);

        AssertRow(run.Output, "(d) C:", Regex.Escape("Encrypting; protection Pending until encryption completes; protectors TPM, Numerical password; PCR 7+11"));
        Assert.DoesNotMatch(@"(?m)^\(d\) C: .*\bOff\b", run.Output);

        using JsonDocument gate = JsonDocument.Parse(File.ReadAllText(outFile));
        JsonElement volume = gate.RootElement.GetProperty("bitLocker").GetProperty("volumes")[0];
        Assert.Equal(0, volume.GetProperty("protectionStatus").GetInt32());
        Assert.Equal("Pending until encryption completes", volume.GetProperty("protectionStatusText").GetString());
        Assert.Equal(2, volume.GetProperty("conversionStatus").GetInt32());
        Assert.Equal("Encrypting", volume.GetProperty("conversionStatusText").GetString());
    }

    private static InMemoryFirmwareVariableStore Store()
    {
        VariableAttributes nvBsRt = VariableAttributes.NonVolatile | VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess;
        return new InMemoryFirmwareVariableStore(new[] { new FirmwareVariable("BootOrder", KnownGuids.EfiGlobalVariable, nvBsRt, new byte[] { 0, 0 }) });
    }
}
