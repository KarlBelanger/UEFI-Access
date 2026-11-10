// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Diagnostics;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The trace files of a run (the input trace of UefiAccess.App, the UI Automation recorder's) are
/// written next to the run's log, so the folder gets the checks Logging makes before it writes its
/// own log there (Logging.PrepareRunFileFolder): a run without elevation creates nothing under the
/// data root, an elevated run refuses a per-user folder chain with a junction in it (finding R18),
/// and no folder but the two log folders is accepted. Temporary folders stand in for the data root
/// and the per-user folder; junctions are real ("mklink /J"). The elevated shared-folder path, the
/// one UefiAccess.App takes for its input trace, is PrepareDataFolderFor, which DataFolderSafetyTests
/// cover in full; the tests here pin that PrepareRunFileFolder routes to it (a missing folder
/// created with the protected access list, a junction and an untrusted owner refused).
/// </summary>
public sealed class LoggingRunFileFolderTests : IDisposable
{
    private readonly string _tempRoot;

    public LoggingRunFileFolderTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "run-file-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    private string DataDirectory => Path.Combine(_tempRoot, "ProgramData", "UefiAccess");

    private string SharedLogs => Path.Combine(DataDirectory, "logs");

    private string LocalAppData => Path.Combine(_tempRoot, "LocalAppData");

    private string ToolFolder => Path.Combine(LocalAppData, "UefiAccess");

    private string UserLogs => Path.Combine(ToolFolder, "logs");

    public void Dispose()
    {
        TestFolders.DeleteQuietly(_tempRoot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnotherFolder_IsRefused_AndNothingIsCreated(bool elevated)
    {
        string other = Path.Combine(_tempRoot, "Desktop", "traces");

        string? refusal = Logging.PrepareRunFileFolder(other, Locations(), elevated, _ => true);

        Assert.NotNull(refusal);
        Assert.Contains("is not a log folder of UefiAccess", refusal, StringComparison.Ordinal);
        Assert.Contains(SharedLogs, refusal, StringComparison.Ordinal);
        Assert.Contains(UserLogs, refusal, StringComparison.Ordinal);
        Assert.False(Directory.Exists(other));
        Assert.False(Directory.Exists(DataDirectory));
    }

    [Fact]
    public void NotElevated_TheSharedFolderMissing_IsRefused_AndNothingIsCreatedUnderTheDataRoot()
    {
        string? refusal = Logging.PrepareRunFileFolder(SharedLogs, Locations(), elevated: false, _ => true);

        Assert.NotNull(refusal);
        Assert.Contains("creates nothing under", refusal, StringComparison.Ordinal);
        Assert.False(Directory.Exists(DataDirectory));
    }

    [Fact]
    public void NotElevated_TheSharedFolderExisting_IsAccepted()
    {
        Directory.CreateDirectory(SharedLogs);

        Assert.Null(Logging.PrepareRunFileFolder(SharedLogs + Path.DirectorySeparatorChar, Locations(), elevated: false, _ => false));
        Assert.Null(Logging.PrepareRunFileFolder(SharedLogs.ToUpperInvariant(), Locations(), elevated: false, _ => false));
    }

    [Fact]
    public void Elevated_TheSharedFolderMissing_IsCreatedUnderTheRootWithTheProtectedAccessList()
    {
        // The data root exists (as ProgramData\UefiAccess does once anything ran); a standard-user
        // test host could not add a folder to a root it had just protected itself.
        Directory.CreateDirectory(DataDirectory);

        Assert.Null(Logging.PrepareRunFileFolder(SharedLogs, Locations(), elevated: true, _ => true));

        Assert.True(Directory.Exists(SharedLogs));
        LoggingFallbackTests.AssertProtectedAccessList(new DirectoryInfo(SharedLogs).GetAccessControl());
    }

    [Fact]
    public void Elevated_ASharedFolderThatIsAJunction_IsRefused_AndNothingIsCreatedThroughIt()
    {
        Directory.CreateDirectory(DataDirectory);
        string outside = Path.Combine(_tempRoot, "outside-logs");
        Directory.CreateDirectory(outside);
        Assert.True(TestLinks.TryCreateJunction(SharedLogs, outside), "mklink /J could not create the junction");

        string? refusal = Logging.PrepareRunFileFolder(SharedLogs, Locations(), elevated: true, _ => true);

        Assert.NotNull(refusal);
        Assert.Contains("is a junction", refusal, StringComparison.Ordinal);
        Assert.Contains(SharedLogs, refusal, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
    }

    [Fact]
    public void Elevated_ASharedFolderUnderAnUntrustedRoot_IsRefused()
    {
        Directory.CreateDirectory(SharedLogs);
        List<string> asked = new();

        string? refusal = Logging.PrepareRunFileFolder(SharedLogs, Locations(), elevated: true, owner =>
        {
            asked.Add(owner?.Value ?? "(none)");
            return false;
        });

        Assert.NotNull(refusal);
        Assert.Contains("is owned by", refusal, StringComparison.Ordinal);
        Assert.Contains(DataDirectory, refusal, StringComparison.Ordinal);
        Assert.Single(asked);
    }

    [Fact]
    public void NotElevated_TheUserFolder_IsCreatedAndAccepted()
    {
        Assert.Null(Logging.PrepareRunFileFolder(UserLogs, Locations(), elevated: false, _ => true));

        Assert.True(Directory.Exists(UserLogs));
    }

    [Fact]
    public void Elevated_TheUserFolderChainPlain_IsCreatedAndAccepted()
    {
        Assert.Null(Logging.PrepareRunFileFolder(UserLogs, Locations(), elevated: true, _ => true));

        Assert.True(Directory.Exists(UserLogs));
    }

    [Fact]
    public void Elevated_AJunctionInTheUserFolderChain_IsRefused_AndNothingIsCreatedThroughIt()
    {
        Directory.CreateDirectory(LocalAppData);
        string outside = Path.Combine(_tempRoot, "System32-stand-in");
        Directory.CreateDirectory(outside);
        Assert.True(TestLinks.TryCreateJunction(ToolFolder, outside), "mklink /J could not create the junction");

        string? refusal = Logging.PrepareRunFileFolder(UserLogs, Locations(), elevated: true, _ => true);

        Assert.NotNull(refusal);
        Assert.Contains("junction", refusal, StringComparison.Ordinal);
        Assert.Contains(ToolFolder, refusal, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
    }

    [Fact]
    public void AMalformedPath_IsRefused_NotThrown()
    {
        string? refusal = null;
        Exception? thrown = Record.Exception(() => refusal = Logging.PrepareRunFileFolder("bad\0path", Locations(), elevated: true, _ => true));

        Assert.Null(thrown);
        Assert.NotNull(refusal);
    }

    private LogLocations Locations() => new(DataDirectory, SharedLogs, UserLogs);
}
