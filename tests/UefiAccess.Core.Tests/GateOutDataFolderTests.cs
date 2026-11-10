// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Cli;
using UefiAccess.Cli.Services;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Core.Variables;
using Xunit;
using static UefiAccess.Core.Tests.CliTestHarness;

namespace UefiAccess.Core.Tests;

/// <summary>
/// "uefiaccess gate --out" goes through the same data-folder check as the baseline export
/// (<see cref="Logging.PrepareDataFolderFor(string)"/>) before it creates the folder or writes
/// gate.json, so an elevated gate run that writes into ProgramData\UefiAccess (Run-Gate.ps1's
/// default, gate\&lt;run&gt;) never writes through a folder a standard user could redirect. A refusal
/// writes nothing, prints the report anyway, says why on standard error and exits 5. A temporary
/// folder stands in for the data root; the junction is real ("mklink /J").
/// </summary>
public sealed class GateOutDataFolderTests : IDisposable
{
    private const VariableAttributes BsRt = VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess;

    private readonly string _tempRoot;

    public GateOutDataFolderTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "gate-out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    /// <summary>The stand-in for ProgramData\UefiAccess.</summary>
    private string Root => Path.Combine(_tempRoot, "UefiAccess");

    public void Dispose()
    {
        TestFolders.DeleteQuietly(_tempRoot);
    }

    [Fact]
    public void Refused_WritesNothing_PrintsTheReportAndExits5()
    {
        const string Refusal = "C:\\ProgramData\\UefiAccess\\gate is owned by EXAMPLE\\user, not by Administrators or SYSTEM, so a standard user could redirect what this elevated run writes there. Rename or delete C:\\ProgramData\\UefiAccess\\gate from an administrator prompt; the next elevated run creates it again with safe permissions.";
        List<string> asked = new();
        string folder = Path.Combine(Root, "gate", "20260922-101500");
        string outFile = Path.Combine(folder, "gate.json");

        CliRun run = Run(PlatformWith(path => { asked.Add(path); return Refusal; }), "gate", "--out", outFile);

        Assert.Equal(ExitCodes.NativeFailure, run.ExitCode);
        Assert.Equal(new[] { folder }, asked);
        Assert.Equal("Writing '" + outFile + "' failed: " + Refusal + "\n", run.Error);
        AssertRow(run.Output, "(a) Firmware type", "Uefi");
        Assert.EndsWith("\nGate report could not be written to " + outFile + "\n", run.Output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Root));
    }

    [Fact]
    public void Refused_WithJson_StillPrintsTheReportAndExits5()
    {
        string outFile = Path.Combine(Root, "gate", "run", "gate.json");

        CliRun run = Run(PlatformWith(_ => "refused for the test."), "gate", "--json", "--out", outFile);

        Assert.Equal(ExitCodes.NativeFailure, run.ExitCode);
        Assert.Equal("Writing '" + outFile + "' failed: refused for the test.\n", run.Error);
        Assert.Equal("Uefi", ParseSingleDocument(run.Output).GetProperty("bootMode").GetProperty("firmwareType").GetString());
        Assert.False(File.Exists(outFile));
    }

    [Fact]
    public void Allowed_TheCheckRunsBeforeTheFolderExists_ThenGateJsonIsWritten()
    {
        string folder = Path.Combine(_tempRoot, "reports", "run");
        string outFile = Path.Combine(folder, "gate.json");
        bool? existedWhenAsked = null;

        CliRun run = Run(PlatformWith(path => { existedWhenAsked = Directory.Exists(path); return null; }), "gate", "--out", outFile);

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.False(existedWhenAsked);
        Assert.True(File.Exists(outFile));
        Assert.EndsWith("\nGate report written to " + outFile + "\n", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void IntoTheDataRootThroughAJunction_IsRefusedAndNothingLandsOutside()
    {
        Directory.CreateDirectory(Root);
        string outside = Path.Combine(_tempRoot, "Windows-stand-in");
        Directory.CreateDirectory(outside);
        string gate = Path.Combine(Root, "gate");
        Assert.True(TestLinks.TryCreateJunction(gate, outside), "mklink /J could not create the junction");
        string outFile = Path.Combine(gate, "20260922-101500", "gate.json");

        CliRun run = Run(PlatformWith(path => Logging.PrepareDataFolderFor(path, Root, elevated: true, _ => true)), "gate", "--out", outFile);

        Assert.Equal(ExitCodes.NativeFailure, run.ExitCode);
        Assert.Equal(
            "Writing '" + outFile + "' failed: " + gate + " is a junction, symbolic link or other reparse point, not a plain folder, so a standard user could redirect what this elevated run writes there. Rename or delete " + gate + " from an administrator prompt; the next elevated run creates it again with safe permissions.\n",
            run.Error);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
    }

    private static CliPlatform PlatformWith(Func<string, string?> prepareDataFolder)
    {
        CliPlatform platform = CreatePlatform(new InMemoryFirmwareVariableStore(new[]
        {
            new FirmwareVariable("SecureBoot", KnownGuids.EfiGlobalVariable, BsRt, new byte[] { 0x01 }),
        }));
        return new CliPlatform(
            platform.PrivilegeManager,
            platform.FirmwareTypeDetector,
            platform.Store,
            platform.Identity,
            platform.Tpm,
            platform.BitLocker,
            platform.Dell,
            platform.SecureBootPolicy,
            prepareDataFolder);
    }
}
