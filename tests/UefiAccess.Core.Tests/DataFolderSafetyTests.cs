// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.AccessControl;
using System.Security.Principal;
using UefiAccess.Core.Baseline;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Privileges;
using UefiAccess.Core.Variables;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The checks an elevated run makes before it writes under the data root (ProgramData\UefiAccess),
/// so a standard user cannot redirect its log or baseline files: every existing folder on the way
/// must be a plain folder, not a junction or symbolic link, owned by SYSTEM, Administrators or
/// TrustedInstaller, and every missing folder is created with the protected access list. The
/// shared log then moves to the per-user folder and an export is refused, each with the reason.
/// A temporary folder stands in for ProgramData; junctions are real ("mklink /J"). Folder owners
/// cannot be changed without elevation, so the owner check is driven through its predicate, and
/// where the result depends on whether the test host itself is elevated, both outcomes are
/// asserted precisely.
/// </summary>
public sealed class DataFolderSafetyTests : IDisposable
{
    private const string RunId = "20260922-101500";
    private const string TrustedInstaller = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";

    private readonly string _tempRoot;

    public DataFolderSafetyTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "data-folder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    private static bool HostIsElevated => PrivilegeManager.ComputeIsElevated();

    /// <summary>The stand-in for ProgramData\UefiAccess; its parent (the stand-in for ProgramData) exists.</summary>
    private string Root => Path.Combine(_tempRoot, "UefiAccess");

    public void Dispose()
    {
        TestFolders.DeleteQuietly(_tempRoot);
    }

    // ----- Logging.PrepareDataFolderFor -----

    [Fact]
    public void Prepare_NotElevated_ChecksAndCreatesNothing()
    {
        Assert.Null(Logging.PrepareDataFolderFor(Path.Combine(Root, "baseline", RunId), Root, elevated: false, _ => false));

        Assert.False(Directory.Exists(Root));
    }

    [Theory]
    [InlineData("Evidence")]
    [InlineData("UefiAccessOther")]
    public void Prepare_OutsideTheDataRoot_ChecksAndCreatesNothing(string sibling)
    {
        string elsewhere = Path.Combine(_tempRoot, sibling, RunId);

        Assert.Null(Logging.PrepareDataFolderFor(elsewhere, Root, elevated: true, _ => false));

        Assert.False(Directory.Exists(Root));
        Assert.False(Directory.Exists(Path.Combine(_tempRoot, sibling)));
    }

    [Fact]
    public void Prepare_MissingRoot_IsCreatedWithTheProtectedAccessList()
    {
        Assert.Null(Logging.PrepareDataFolderFor(Root, Root, elevated: true, _ => true));

        LoggingFallbackTests.AssertProtectedAccessList(new DirectoryInfo(Root).GetAccessControl());
    }

    [Fact]
    public void Prepare_MissingFolderUnderAnExistingRoot_IsCreatedProtectedAndTheRootIsLeftAsItIs()
    {
        Directory.CreateDirectory(Root);
        string before = new DirectoryInfo(Root).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        string logs = Path.Combine(Root, "logs");

        Assert.Null(Logging.PrepareDataFolderFor(logs, Root, elevated: true, _ => true));

        LoggingFallbackTests.AssertProtectedAccessList(new DirectoryInfo(logs).GetAccessControl());
        DirectorySecurity after = new DirectoryInfo(Root).GetAccessControl();
        Assert.Equal(before, after.GetSecurityDescriptorSddlForm(AccessControlSections.Access));
        Assert.False(after.AreAccessRulesProtected);
    }

    [Fact]
    public void Prepare_MissingChain_CreatesEveryFolderProtected()
    {
        string baseline = Path.Combine(Root, "baseline");
        string run = Path.Combine(baseline, RunId);

        string? refusal = Logging.PrepareDataFolderFor(run, Root, elevated: true, _ => true);

        LoggingFallbackTests.AssertProtectedAccessList(new DirectoryInfo(Root).GetAccessControl());
        if (HostIsElevated)
        {
            Assert.Null(refusal);
            LoggingFallbackTests.AssertProtectedAccessList(new DirectoryInfo(baseline).GetAccessControl());
            LoggingFallbackTests.AssertProtectedAccessList(new DirectoryInfo(run).GetAccessControl());
        }
        else
        {
            // The access list is real: a standard-user host may not add a folder to the root it
            // just protected, and the refusal says which folder could not be created.
            Assert.NotNull(refusal);
            Assert.StartsWith($"{baseline} cannot be created: ", refusal, StringComparison.Ordinal);
            Assert.False(Directory.Exists(baseline));
        }
    }

    [Fact]
    public void Prepare_RootOwnedByAnUntrustedAccount_IsRefusedWithTheOwnerAndWhatToDo()
    {
        Directory.CreateDirectory(Root);
        SecurityIdentifier owner = OwnerOf(Root);
        List<SecurityIdentifier?> asked = new();

        string? refusal = Logging.PrepareDataFolderFor(Path.Combine(Root, "baseline", RunId), Root, elevated: true, sid =>
        {
            asked.Add(sid);
            return false;
        });

        Assert.Equal(new SecurityIdentifier?[] { owner }, asked);
        Assert.Equal(OwnerRefusal(Root, owner), refusal);
        Assert.False(Directory.Exists(Path.Combine(Root, "baseline")));
    }

    [Fact]
    public void Prepare_UntrustedSubfolderUnderATrustedRoot_IsRefused()
    {
        string baseline = Path.Combine(Root, "baseline");
        Directory.CreateDirectory(baseline);
        int asked = 0;

        string? refusal = Logging.PrepareDataFolderFor(Path.Combine(baseline, RunId), Root, elevated: true, _ => asked++ == 0);

        Assert.Equal(2, asked);
        Assert.Equal(OwnerRefusal(baseline, OwnerOf(baseline)), refusal);
        Assert.False(Directory.Exists(Path.Combine(baseline, RunId)));
    }

    [Fact]
    public void Prepare_JunctionOnTheWay_IsRefusedAndNothingIsCreatedThroughIt()
    {
        Directory.CreateDirectory(Root);
        string outside = Path.Combine(_tempRoot, "Windows-stand-in");
        Directory.CreateDirectory(outside);
        string baseline = Path.Combine(Root, "baseline");
        Assert.True(TestLinks.TryCreateJunction(baseline, outside), "mklink /J could not create the junction");

        string? refusal = Logging.PrepareDataFolderFor(Path.Combine(baseline, RunId), Root, elevated: true, _ => true);

        Assert.Equal(LinkRefusal(baseline), refusal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
    }

    [Fact]
    public void Prepare_TargetFolderIsAJunction_IsRefused()
    {
        Directory.CreateDirectory(Root);
        string outside = Path.Combine(_tempRoot, "outside-logs");
        Directory.CreateDirectory(outside);
        string logs = Path.Combine(Root, "logs");
        Assert.True(TestLinks.TryCreateJunction(logs, outside), "mklink /J could not create the junction");

        Assert.Equal(LinkRefusal(logs), Logging.PrepareDataFolderFor(logs, Root, elevated: true, _ => true));
    }

    [Fact]
    public void Prepare_RootIsAJunction_IsRefused()
    {
        string outside = Path.Combine(_tempRoot, "outside-root");
        Directory.CreateDirectory(outside);
        Assert.True(TestLinks.TryCreateJunction(Root, outside), "mklink /J could not create the junction");

        string? refusal = Logging.PrepareDataFolderFor(Path.Combine(Root, "logs"), Root, elevated: true, _ => true);

        Assert.Equal(LinkRefusal(Root), refusal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
    }

    // ----- Logging.IsTrustedOwner -----

    [Fact]
    public void TrustedOwners_AreSystemAdministratorsAndTrustedInstallerOnly()
    {
        Assert.True(Logging.IsTrustedOwner(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null)));
        Assert.True(Logging.IsTrustedOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)));
        Assert.True(Logging.IsTrustedOwner(new SecurityIdentifier(TrustedInstaller)));

        // Not this token's default owner as such (finding R18; ElevatedLogFallbackSafetyTests).
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        Assert.Equal(identity.User!.IsWellKnown(WellKnownSidType.LocalSystemSid), Logging.IsTrustedOwner(identity.User));

        Assert.False(Logging.IsTrustedOwner(null));
        Assert.False(Logging.IsTrustedOwner(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null)));
        Assert.False(Logging.IsTrustedOwner(new SecurityIdentifier(WellKnownSidType.WorldSid, null)));
        Assert.False(Logging.IsTrustedOwner(new SecurityIdentifier("S-1-5-21-1111111111-2222222222-3333333333-1001")));
    }

    // ----- Logging.Create under a data root it may not trust -----

    [Fact]
    public void Create_ElevatedWithAnUntrustedDataRoot_LogsPerUserAndRecordsWhyThere()
    {
        LogLocations locations = Locations();
        Directory.CreateDirectory(locations.DataDirectory);
        StringWriter notices = new();

        WriteOneEvent(locations, notices, _ => false);

        Assert.Equal(string.Empty, notices.ToString());
        Assert.False(Directory.Exists(locations.SharedLogDirectory));
        string log = ReadSingleLog(locations.UserLogDirectory);
        string refusal = OwnerRefusal(locations.DataDirectory, OwnerOf(locations.DataDirectory));
        Assert.Contains("[WRN] [cli] " + FallbackLogSink.MovedNotice(locations.SharedLogDirectory, locations.UserLogDirectory, refusal), log, StringComparison.Ordinal);
        Assert.Contains("event 1", log, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_ElevatedWithTheLogsFolderAJunction_WritesNothingThroughIt()
    {
        LogLocations locations = Locations();
        Directory.CreateDirectory(locations.DataDirectory);
        string outside = Path.Combine(_tempRoot, "outside-logs");
        Directory.CreateDirectory(outside);
        Assert.True(TestLinks.TryCreateJunction(locations.SharedLogDirectory, outside), "mklink /J could not create the junction");
        StringWriter notices = new();

        WriteOneEvent(locations, notices, _ => true);

        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
        Assert.Equal(string.Empty, notices.ToString());
        string log = ReadSingleLog(locations.UserLogDirectory);
        Assert.Contains(FallbackLogSink.MovedNotice(locations.SharedLogDirectory, locations.UserLogDirectory, LinkRefusal(locations.SharedLogDirectory)), log, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_ElevatedWithATrustedDataRoot_CreatesTheLogsFolderProtected()
    {
        LogLocations locations = Locations();
        Directory.CreateDirectory(locations.DataDirectory);
        StringWriter notices = new();

        WriteOneEvent(locations, notices, _ => true);

        LoggingFallbackTests.AssertProtectedAccessList(new DirectoryInfo(locations.SharedLogDirectory).GetAccessControl());
        Assert.Equal(string.Empty, notices.ToString());
        if (HostIsElevated)
        {
            Assert.Contains("event 1", ReadSingleLog(locations.SharedLogDirectory), StringComparison.Ordinal);
            Assert.False(Directory.Exists(locations.UserLogDirectory));
        }
        else
        {
            // Protected means a standard user may not add the day's file: the folder was accepted,
            // the first write failed, and the run moved on as any standard-user run would.
            Assert.Empty(Directory.GetFiles(locations.SharedLogDirectory));
            string log = ReadSingleLog(locations.UserLogDirectory);
            Assert.Contains(FallbackLogSink.MovedNotice(locations.SharedLogDirectory, locations.UserLogDirectory), log, StringComparison.Ordinal);
        }
    }

    // ----- BaselineExporter -----

    [Fact]
    public void Export_RefusedRunFolder_WritesNothingAndReportsTheRefusal()
    {
        string root = Path.Combine(_tempRoot, "baseline");
        string directory = Path.Combine(root, RunId);
        List<string> asked = new();
        BaselineExporter exporter = new(path =>
        {
            asked.Add(path);
            return path + " is refused for this test.";
        });

        BaselineExportResult result = exporter.Export(Request(root));

        Assert.Equal(new[] { directory }, asked);
        Assert.Equal(directory, result.Directory);
        Assert.Empty(result.FilesWritten);
        Assert.Equal(new[] { $"Cannot create export directory '{Path.Combine(directory, "vars")}': {directory} is refused for this test." }, result.Errors);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void Export_AskedBeforeAnythingIsCreated_ThenWritesAsBefore()
    {
        string root = Path.Combine(_tempRoot, "baseline");
        bool existedWhenAsked = true;
        BaselineExporter exporter = new(path =>
        {
            existedWhenAsked = Directory.Exists(path);
            return null;
        });

        BaselineExportResult result = exporter.Export(Request(root));

        Assert.False(existedWhenAsked);
        Assert.Empty(result.Errors);
        Assert.True(File.Exists(Path.Combine(root, RunId, "index.json")));
    }

    [Fact]
    public void Export_IntoTheDataRootThroughAJunction_IsRefusedAndNothingLandsOutside()
    {
        Directory.CreateDirectory(Root);
        string outside = Path.Combine(_tempRoot, "Windows-stand-in");
        Directory.CreateDirectory(outside);
        string baseline = Path.Combine(Root, "baseline");
        Assert.True(TestLinks.TryCreateJunction(baseline, outside), "mklink /J could not create the junction");
        BaselineExporter exporter = new(path => Logging.PrepareDataFolderFor(path, Root, elevated: true, _ => true));

        BaselineExportResult result = exporter.Export(Request(baseline));

        string vars = Path.Combine(baseline, RunId, "vars");
        Assert.Equal(new[] { $"Cannot create export directory '{vars}': {LinkRefusal(baseline)}" }, result.Errors);
        Assert.Empty(result.FilesWritten);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
    }

    private static string OwnerRefusal(string folder, SecurityIdentifier owner) =>
        $"{folder} is owned by {owner.Translate(typeof(NTAccount)).Value}, not by Administrators or SYSTEM, so a standard user could redirect what this elevated run writes there. Rename or delete {folder} from an administrator prompt; the next elevated run creates it again with safe permissions.";

    private static string LinkRefusal(string folder) =>
        $"{folder} is a junction, symbolic link or other reparse point, not a plain folder, so a standard user could redirect what this elevated run writes there. Rename or delete {folder} from an administrator prompt; the next elevated run creates it again with safe permissions.";

    private static SecurityIdentifier OwnerOf(string folder) =>
        (SecurityIdentifier)new DirectoryInfo(folder).GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier))!;

    private static string ReadSingleLog(string directory)
    {
        string file = Assert.Single(Directory.GetFiles(directory, "uefiaccess-*.log"));
        return File.ReadAllText(file);
    }

    private static void WriteOneEvent(LogLocations locations, TextWriter notices, Func<SecurityIdentifier?, bool> isTrustedOwner)
    {
        Serilog.ILogger logger = Logging.Create("cli", console: false, locations, elevated: true, notices, isTrustedOwner);
        logger.Information("event {Number}", 1);
        (logger as IDisposable)?.Dispose();
    }

    private static BaselineRequest Request(string root) => new(
        root,
        RunId,
        new[] { new FirmwareVariable("Timeout", KnownGuids.EfiGlobalVariable, VariableAttributes.NonVolatile | VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess, new byte[] { 0x05, 0x00 }) },
        FirmwareType.Uefi,
        PrivilegeState.Enabled,
        null,
        null,
        null,
        null,
        "0.1.0",
        new DateTimeOffset(2026, 9, 22, 10, 15, 0, TimeSpan.FromHours(2)));

    private LogLocations Locations() =>
        new(Root, Path.Combine(Root, "logs"), Path.Combine(_tempRoot, "LocalAppData", "UefiAccess", "logs"));
}
