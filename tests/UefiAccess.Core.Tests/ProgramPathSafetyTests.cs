// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Serilog.Core;
using UefiAccess.Core.Platform;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// Finding R5: an elevated run starts cctk.exe, so where it looks for the program must not be up
/// to whoever set the process's environment (per-user environment variables reach an elevated
/// process of the same account), and nothing may be started by a path Windows would resolve
/// against the current directory or PATH. The cctk.exe candidates come only from the Program
/// Files known folders and are all full paths, and <see cref="ProcessRunner"/> refuses any
/// executable that is not named by a full path, without starting anything. Only cmd.exe is
/// started here, by its full System32 path, to show that a full path still runs.
/// </summary>
public sealed class ProgramPathSafetyTests
{
    /// <summary>Serialises the test that changes this process's Program Files variables.</summary>
    private static readonly object EnvironmentGate = new();

    [Fact]
    public void CctkCandidates_AreTheProgramFilesKnownFoldersOnly()
    {
        string expectedX86 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Dell", "Command Configure", "X86_64", "cctk.exe");
        string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Dell", "Command Configure", "X86_64", "cctk.exe");

        IReadOnlyList<string> candidates = DellInterfaceProbe.CctkCandidatePaths();

        Assert.Equal(new[] { expectedX86, expected }.Distinct(StringComparer.OrdinalIgnoreCase), candidates);
        Assert.All(candidates, candidate => Assert.True(Path.IsPathFullyQualified(candidate), candidate));
    }

    [Fact]
    public void CctkCandidates_IgnoreTheProgramFilesEnvironmentVariables()
    {
        lock (EnvironmentGate)
        {
            string? programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
            string? programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            string planted = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "planted-" + Guid.NewGuid().ToString("N"));
            try
            {
                Environment.SetEnvironmentVariable("ProgramFiles", planted);
                Environment.SetEnvironmentVariable("ProgramFiles(x86)", "Planted");

                IReadOnlyList<string> candidates = DellInterfaceProbe.CctkCandidatePaths();

                Assert.Equal(2, candidates.Count);
                Assert.All(candidates, candidate =>
                {
                    Assert.DoesNotContain(planted, candidate, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("Planted", candidate, StringComparison.Ordinal);
                    Assert.True(Path.IsPathFullyQualified(candidate), candidate);
                });
            }
            finally
            {
                Environment.SetEnvironmentVariable("ProgramFiles", programFiles);
                Environment.SetEnvironmentVariable("ProgramFiles(x86)", programFilesX86);
            }
        }
    }

    [Fact]
    public void CctkCandidates_SkipFoldersThatAreNotFullPaths_AndDuplicates()
    {
        IReadOnlyList<string> candidates = DellInterfaceProbe.CctkCandidatePaths(new[]
        {
            null,
            string.Empty,
            "   ",
            "Program Files",
            @"relative\Program Files",
            @"C:Program Files",
            @"\Program Files",
            @"C:\Program Files (x86)",
            @"  C:\Program Files  ",
            @"c:\program files",
            @"\\server\share\Program Files",
        });

        Assert.Equal(
            new[]
            {
                @"C:\Program Files (x86)\Dell\Command Configure\X86_64\cctk.exe",
                @"C:\Program Files\Dell\Command Configure\X86_64\cctk.exe",
                @"\\server\share\Program Files\Dell\Command Configure\X86_64\cctk.exe",
            },
            candidates);
    }

    [Theory]
    [InlineData("cmd.exe")]
    [InlineData("cmd")]
    [InlineData(@"System32\cmd.exe")]
    [InlineData(@".\cmd.exe")]
    [InlineData(@"C:cmd.exe")]
    [InlineData(@"\Windows\System32\cmd.exe")]
    [InlineData("")]
    [InlineData("   ")]
    public void ProcessRunner_RefusesAnExecutableThatIsNotAFullPath_WithoutStartingIt(string fileName)
    {
        ProcessRunResult run = ProcessRunner.Run(fileName, null, TimeSpan.FromSeconds(30), Logger.None, "/d", "/c", "echo", "started");

        Assert.False(run.Started);
        Assert.Null(run.ExitCode);
        Assert.False(run.TimedOut);
        Assert.Equal(string.Empty, run.StandardOutput);
        Assert.Equal(string.Empty, run.StandardError);
        Assert.Equal($"'{fileName}' is not a full path, so it was not started", run.Error);
        Assert.Equal(ProcessRunner.NotAFullPathMessage(fileName), run.Error);
    }

    [Fact]
    public void ProcessRunner_RunsAnExecutableNamedByItsFullPath()
    {
        ProcessRunResult run = ProcessRunner.Run(Path.Combine(Environment.SystemDirectory, "cmd.exe"), null, TimeSpan.FromSeconds(30), Logger.None, "/d", "/c", "echo", "started");

        Assert.True(run.Started, run.Error);
        Assert.Null(run.Error);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("started", run.StandardOutput);
    }
}
