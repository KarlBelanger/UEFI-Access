// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.AccessControl;
using System.Security.Principal;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;
using UefiAccess.Core.Diagnostics;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// Finding R18. An elevated run whose shared log folder is refused writes its log to the per-user
/// folder, which belongs to the user: a program the same account runs without elevation could make
/// that folder a junction and redirect the elevated run's log file into a folder only
/// administrators may write. The elevated fallback therefore refuses a per-user folder (the local
/// application data folder, UefiAccess or logs) that is a junction, symbolic link or other reparse
/// point, and an existing log file that is one, and then keeps no log file and says why in one
/// line on standard error. And the data root trusts only SYSTEM, Administrators and
/// TrustedInstaller as owners, not the default owner of the run's own token, which under the
/// "Object creator" owner policy is the administrator's own account; the folders an elevated run
/// creates are given Administrators as their owner explicitly instead. A temporary folder stands in
/// for the per-user folder; junctions are real ("mklink /J").
/// </summary>
public sealed class ElevatedLogFallbackSafetyTests : IDisposable
{
    private const string Shared = @"C:\ProgramData\UefiAccess\logs";
    private const string User = @"C:\Users\karl\AppData\Local\UefiAccess\logs";

    private static readonly MessageTemplate Template = new MessageTemplateParser().Parse("{Text}");

    private readonly string _tempRoot;

    public ElevatedLogFallbackSafetyTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "user-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    /// <summary>The stand-in for LocalApplicationData.</summary>
    private string LocalAppData => Path.Combine(_tempRoot, "LocalAppData");

    private string ToolFolder => Path.Combine(LocalAppData, "UefiAccess");

    private string UserLogs => Path.Combine(ToolFolder, "logs");

    public void Dispose()
    {
        TestFolders.DeleteQuietly(_tempRoot);
    }

    // ----- Logging.PrepareUserLogFolderForElevatedRun -----

    [Fact]
    public void PrepareUserLog_MissingChain_IsCreatedAndAccepted()
    {
        Assert.Null(Logging.PrepareUserLogFolderForElevatedRun(UserLogs));

        Assert.True(Directory.Exists(UserLogs));
    }

    [Fact]
    public void PrepareUserLog_PlainExistingFolders_AreAccepted()
    {
        Directory.CreateDirectory(UserLogs);
        File.WriteAllText(Path.Combine(UserLogs, "uefiaccess-20260921.log"), "yesterday");

        Assert.Null(Logging.PrepareUserLogFolderForElevatedRun(UserLogs + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void PrepareUserLog_ToolFolderIsAJunction_IsRefusedAndNothingIsCreatedThroughIt()
    {
        Directory.CreateDirectory(LocalAppData);
        string outside = Path.Combine(_tempRoot, "System32-stand-in");
        Directory.CreateDirectory(outside);
        Assert.True(TestLinks.TryCreateJunction(ToolFolder, outside), "mklink /J could not create the junction");

        string? refusal = Logging.PrepareUserLogFolderForElevatedRun(UserLogs);

        Assert.Equal(LinkRefusal(ToolFolder), refusal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
    }

    [Fact]
    public void PrepareUserLog_LogsFolderIsAJunction_IsRefused()
    {
        Directory.CreateDirectory(ToolFolder);
        string outside = Path.Combine(_tempRoot, "outside-logs");
        Directory.CreateDirectory(outside);
        Assert.True(TestLinks.TryCreateJunction(UserLogs, outside), "mklink /J could not create the junction");

        Assert.Equal(LinkRefusal(UserLogs), Logging.PrepareUserLogFolderForElevatedRun(UserLogs));
    }

    [Fact]
    public void PrepareUserLog_LocalAppDataIsAJunction_IsRefused()
    {
        string outside = Path.Combine(_tempRoot, "outside-appdata");
        Directory.CreateDirectory(outside);
        Assert.True(TestLinks.TryCreateJunction(LocalAppData, outside), "mklink /J could not create the junction");

        Assert.Equal(LinkRefusal(LocalAppData), Logging.PrepareUserLogFolderForElevatedRun(UserLogs));
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
    }

    [Fact]
    public void PrepareUserLog_AFileWhereAFolderBelongs_IsRefused()
    {
        Directory.CreateDirectory(LocalAppData);
        File.WriteAllText(ToolFolder, "not a folder");

        Assert.Equal($"{ToolFolder} is a file, not a folder.", Logging.PrepareUserLogFolderForElevatedRun(UserLogs));
    }

    [FileSymbolicLinkFact]
    public void PrepareUserLog_ALogFileThatIsASymbolicLink_IsRefused()
    {
        Directory.CreateDirectory(UserLogs);
        string target = Path.Combine(_tempRoot, "hosts-stand-in");
        File.WriteAllText(target, "127.0.0.1 localhost");
        string link = Path.Combine(UserLogs, "uefiaccess-20260922.log");
        Assert.True(TestLinks.TryCreateFileSymbolicLink(link, target), "the file symbolic link could not be created");

        Assert.Equal(
            $"{link} is a symbolic link or other reparse point, not a plain file, so a program running without elevation could redirect what this elevated run writes there. Rename or delete {link}; the next run creates it again.",
            Logging.PrepareUserLogFolderForElevatedRun(UserLogs));
    }

    // ----- Logging.Create: the elevated fallback -----

    [Fact]
    public void Create_ElevatedWithTheUserFolderAJunction_KeepsNoLogAndSaysWhyOnce()
    {
        LogLocations locations = Locations();
        Directory.CreateDirectory(locations.DataDirectory);
        Directory.CreateDirectory(LocalAppData);
        string outside = Path.Combine(_tempRoot, "System32-stand-in");
        Directory.CreateDirectory(outside);
        Assert.True(TestLinks.TryCreateJunction(ToolFolder, outside), "mklink /J could not create the junction");
        StringWriter notices = new();

        // The shared folder is refused (an untrusted owner), so the run falls back to the user's.
        WriteTwoEvents(locations, elevated: true, notices, _ => false);

        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
        Assert.False(Directory.Exists(locations.SharedLogDirectory));
        Assert.Equal(
            FallbackLogSink.NoLogNotice(locations.SharedLogDirectory, locations.UserLogDirectory, LinkRefusal(ToolFolder)) + Environment.NewLine,
            notices.ToString());
        Assert.Equal(
            $"Note: neither the shared log folder {locations.SharedLogDirectory} nor {locations.UserLogDirectory} is used, so this run keeps no log file: {LinkRefusal(ToolFolder)}",
            FallbackLogSink.NoLogNotice(locations.SharedLogDirectory, locations.UserLogDirectory, LinkRefusal(ToolFolder)));
    }

    [Fact]
    public void Create_ElevatedWithAPlainUserFolder_LogsThereAsBefore()
    {
        LogLocations locations = Locations();
        Directory.CreateDirectory(locations.DataDirectory);
        StringWriter notices = new();

        WriteTwoEvents(locations, elevated: true, notices, _ => false);

        Assert.Equal(string.Empty, notices.ToString());
        string log = File.ReadAllText(Assert.Single(Directory.GetFiles(locations.UserLogDirectory, "uefiaccess-*.log")));
        Assert.Contains("event 2", log, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_NotElevated_FollowsTheUsersOwnFolder()
    {
        // A run without elevation gains nothing from a link in its own folder, so it is not checked.
        LogLocations locations = Locations();
        Directory.CreateDirectory(LocalAppData);
        string outside = Path.Combine(_tempRoot, "moved-logs");
        Directory.CreateDirectory(outside);
        Assert.True(TestLinks.TryCreateJunction(ToolFolder, outside), "mklink /J could not create the junction");
        StringWriter notices = new();

        WriteTwoEvents(locations, elevated: false, notices, _ => false);

        Assert.Equal(string.Empty, notices.ToString());
        Assert.Single(Directory.GetFiles(Path.Combine(outside, "logs"), "uefiaccess-*.log"));
    }

    // ----- FallbackLogSink with a refused per-user folder -----

    [Fact]
    public void Sink_UserFolderRefused_SaysWhyOnce_AndDropsEvents()
    {
        const string Refusal = @"C:\Users\karl\AppData\Local\UefiAccess is a junction, symbolic link or other reparse point, not a plain folder, so a program running without elevation could redirect what this elevated run writes there. Rename or delete C:\Users\karl\AppData\Local\UefiAccess; the next run creates it again.";
        StringWriter notices = new();
        int opened = 0;
        using FallbackLogSink sink = new(null, () =>
        {
            opened++;
            throw new LogFolderRefusedException(Refusal);
        }, notices, Shared, User, "the shared folder is refused");

        sink.Emit(Event("one"));
        sink.Emit(Event("two"));

        Assert.Equal(1, opened);
        Assert.Equal($"Note: neither the shared log folder {Shared} nor {User} is used, so this run keeps no log file: {Refusal}" + Environment.NewLine, notices.ToString());
    }

    [Fact]
    public void NoLogNotice_WithoutARefusal_IsUnchanged()
    {
        Assert.Equal($"Note: neither the shared log folder {Shared} nor {User} can be written, so this run keeps no log file.", FallbackLogSink.NoLogNotice(Shared, User));
        Assert.Equal($"Note: the log folder {Shared} cannot be written, so this run keeps no log file.", FallbackLogSink.NoLogNotice(Shared, null, "ignored without a per-user folder"));
    }

    // ----- Owners -----

    [Fact]
    public void TrustedOwners_AreTheThreeWellKnownAccountsOnly_NotAnAdministratorsOwnAccount()
    {
        // An administrator's own account (RID 500 here) and this host's user are not trusted: under
        // the "Object creator" policy that account is the elevated token's default owner, and its
        // processes without elevation own what it owns.
        Assert.False(Logging.IsTrustedOwner(new SecurityIdentifier("S-1-5-21-1111111111-2222222222-3333333333-500")));
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier user = identity.User!;
        Assert.Equal(user.IsWellKnown(WellKnownSidType.LocalSystemSid), Logging.IsTrustedOwner(user));
        Assert.Equal(IsOneOfTheThree(identity.Owner), Logging.IsTrustedOwner(identity.Owner));
    }

    [Fact]
    public void CreationOwner_IsAdministrators_ExactlyWhenTheTokenHoldsTheGroup()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        bool administrator = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);

        SecurityIdentifier? owner = Logging.CreationOwner();

        Assert.Equal(administrator ? new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) : null, owner);
    }

    [Fact]
    public void CreateProtectedDirectory_GivesTheFolderTheCreationOwner()
    {
        // Elevated, the folder is owned by Administrators whatever the owner policy says; without
        // elevation the token may not assign that owner, and the folder gets the token's default.
        string folder = Path.Combine(_tempRoot, "UefiAccess");
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();

        Logging.CreateProtectedDirectory(folder);

        SecurityIdentifier actual = (SecurityIdentifier)new DirectoryInfo(folder).GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier))!;
        Assert.Equal(Logging.CreationOwner() ?? identity.Owner, actual);
        Assert.True(Logging.IsTrustedOwner(actual) || Logging.CreationOwner() is null);
        LoggingFallbackTests.AssertProtectedAccessList(new DirectoryInfo(folder).GetAccessControl());
    }

    private static bool IsOneOfTheThree(SecurityIdentifier? sid) =>
        sid is not null
        && (sid.IsWellKnown(WellKnownSidType.LocalSystemSid)
            || sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid)
            || sid.Value == "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464");

    private static string LinkRefusal(string folder) =>
        $"{folder} is a junction, symbolic link or other reparse point, not a plain folder, so a program running without elevation could redirect what this elevated run writes there. Rename or delete {folder}; the next run creates it again.";

    private static LogEvent Event(string text) => new(
        DateTimeOffset.Now,
        LogEventLevel.Information,
        null,
        Template,
        new[] { new LogEventProperty("Text", new ScalarValue(text)) });

    private static void WriteTwoEvents(LogLocations locations, bool elevated, TextWriter notices, Func<SecurityIdentifier?, bool> isTrustedOwner)
    {
        Serilog.ILogger logger = Logging.Create("cli", console: false, locations, elevated, notices, isTrustedOwner);
        logger.Information("event {Number}", 1);
        logger.Information("event {Number}", 2);
        (logger as IDisposable)?.Dispose();
    }

    /// <summary>A stand-in for ProgramData\UefiAccess (its parent exists) and the per-user folder.</summary>
    private LogLocations Locations()
    {
        string programData = Path.Combine(_tempRoot, "ProgramData");
        Directory.CreateDirectory(programData);
        string data = Path.Combine(programData, "UefiAccess");
        return new LogLocations(data, Path.Combine(data, "logs"), UserLogs);
    }
}
