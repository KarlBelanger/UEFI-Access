// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Cli;
using UefiAccess.Core.Variables;
using Xunit;
using static UefiAccess.Core.Tests.CliTestHarness;

namespace UefiAccess.Core.Tests;

/// <summary>
/// "uefiaccess read --out": when the file cannot be written, standard output says so in one
/// sentence and never tells the user to use --out, which they just did; without --out the hint
/// stays.
/// </summary>
public sealed class ReadOutFileTests : IDisposable
{
    private readonly string _tempRoot;

    public ReadOutFileTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "read-out-" + Guid.NewGuid().ToString("N"));
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
    public void OutFileThatCannotBeWritten_SaysSo_AndDropsTheHint()
    {
        string target = Path.Combine(_tempRoot, "missing-folder", "bootorder.bin");

        CliRun run = Run(CreatePlatform(Store()), "read", "BootOrder", "--out", target);

        Assert.Equal(ExitCodes.NativeFailure, run.ExitCode);
        Assert.Contains("The data could not be written to " + target + ".\n", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Use --hex", run.Output, StringComparison.Ordinal);
        Assert.StartsWith("Writing '" + target + "' failed: DirectoryNotFoundException: ", run.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(target));
    }

    [Fact]
    public void WithoutOut_TheHintStays()
    {
        CliRun run = Run(CreatePlatform(Store()), "read", "BootOrder");

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Contains("Use --hex to show the data or --out <file> to save it.\n", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void OutFileWritten_ReportsTheBytes_WithoutTheHint()
    {
        string target = Path.Combine(_tempRoot, "bootorder.bin");

        CliRun run = Run(CreatePlatform(Store()), "read", "BootOrder", "--out", target);

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Contains("Wrote 4 bytes to " + target + ".\n", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Use --hex", run.Output, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 0, 0, 1, 0 }, File.ReadAllBytes(target));
    }

    private static InMemoryFirmwareVariableStore Store()
    {
        VariableAttributes nvBsRt = VariableAttributes.NonVolatile | VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess;
        return new InMemoryFirmwareVariableStore(new[] { new FirmwareVariable("BootOrder", KnownGuids.EfiGlobalVariable, nvBsRt, new byte[] { 0, 0, 1, 0 }) });
    }
}
