// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.AccessControl;
using System.Security.Principal;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Core.Privileges;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The shared log and the data root under ProgramData. An elevated run that creates the data root
/// gives it a protected access list (SYSTEM and Administrators full control, Users read and
/// execute), so standard users cannot plant files the elevated tool later reads; an existing
/// folder's access list is never changed. A run that cannot write the shared log (a standard user
/// after an elevated run created the folder or the day's file) logs to the per-user folder instead
/// and records that once at the top of the per-user log, instead of losing its lines silently.
/// Standard error stays quiet unless no log file can be kept at all, so the refusal sentence of a
/// command run without elevation is the first thing a screen reader reads. The sink logic is
/// tested with recorded fakes; the folder tests use real folders under the temporary directory,
/// and where the result depends on whether the test host itself is elevated, both outcomes are
/// asserted precisely. <see cref="DataFolderSafetyTests"/> covers the checks an elevated run makes
/// before it writes under the data root.
/// </summary>
public sealed class LoggingFallbackTests : IDisposable
{
    private const string Shared = @"C:\ProgramData\UefiAccess\logs";
    private const string User = @"C:\Users\karl\AppData\Local\UefiAccess\logs";

    private static readonly MessageTemplate Template = new MessageTemplateParser().Parse("{Text}");

    private readonly string _tempRoot;

    public LoggingFallbackTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "logging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    private static bool HostIsElevated => PrivilegeManager.ComputeIsElevated();

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

    // ----- FallbackLogSink with recorded fakes -----

    [Fact]
    public void Sink_PrimaryWorks_WritesThereAndSaysNothing()
    {
        RecordingSink primary = new();
        RecordingSink fallback = new();
        StringWriter notices = new();
        using FallbackLogSink sink = new(primary, () => fallback, notices, Shared, User);

        sink.Emit(Event("one"));
        sink.Emit(Event("two"));

        Assert.Equal(new[] { "one", "two" }, primary.Texts);
        Assert.Empty(fallback.Events);
        Assert.Equal(string.Empty, notices.ToString());
        Assert.False(sink.UsesFallback);
    }

    [Fact]
    public void Sink_PrimaryFails_MovesToTheUserLogAndRecordsItThereOnly()
    {
        RecordingSink primary = new() { Failure = new UnauthorizedAccessException("Access to the path is denied.") };
        RecordingSink fallback = new();
        StringWriter notices = new();
        int opened = 0;
        using FallbackLogSink sink = new(primary, () => { opened++; return fallback; }, notices, Shared, User);

        sink.Emit(Event("one"));
        sink.Emit(Event("two"));
        sink.Emit(Event("three"));

        string notice = $"Note: the shared log folder {Shared} cannot be written, so this run logs to {User}.";
        Assert.Equal(string.Empty, notices.ToString());
        Assert.Equal(1, opened);
        Assert.Equal(1, primary.Attempts);
        Assert.True(primary.Disposed);
        Assert.True(sink.UsesFallback);
        Assert.Equal(new[] { notice, "one", "two", "three" }, fallback.Texts);

        LogEvent first = fallback.Events[0];
        Assert.Equal(LogEventLevel.Warning, first.Level);
        Assert.IsType<UnauthorizedAccessException>(first.Exception);
        Assert.Equal("\"cli\"", first.Properties["Component"].ToString());
    }

    [Fact]
    public void Sink_NoSharedLog_UsesTheUserLogFromTheFirstEvent()
    {
        RecordingSink fallback = new();
        StringWriter notices = new();
        using FallbackLogSink sink = new(null, () => fallback, notices, Shared, User);

        sink.Emit(Event("one"));
        sink.Emit(Event("two"));

        Assert.Equal(string.Empty, notices.ToString());
        Assert.Equal(new[] { FallbackLogSink.MovedNotice(Shared, User), "one", "two" }, fallback.Texts);
        Assert.Null(fallback.Events[0].Exception);
    }

    [Fact]
    public void Sink_SharedLogRefused_RecordsTheReasonAtTheTopOfTheUserLog()
    {
        const string Reason = @"C:\ProgramData\UefiAccess is owned by OPTIPLEX\karl, not by Administrators or SYSTEM, so a standard user could redirect what this elevated run writes there.";
        RecordingSink fallback = new();
        StringWriter notices = new();
        using FallbackLogSink sink = new(null, () => fallback, notices, Shared, User, Reason);

        sink.Emit(Event("one"));

        string notice = $"Note: this run logs to {User}, not to the shared log folder {Shared}: {Reason}";
        Assert.Equal(notice, FallbackLogSink.MovedNotice(Shared, User, Reason));
        Assert.Equal(new[] { notice, "one" }, fallback.Texts);
        Assert.Equal(LogEventLevel.Warning, fallback.Events[0].Level);
        Assert.Equal(string.Empty, notices.ToString());
    }

    [Fact]
    public void Sink_ReasonIsIgnoredWhenTheSharedLogWasOpened()
    {
        RecordingSink primary = new() { Failure = new UnauthorizedAccessException("denied") };
        RecordingSink fallback = new();
        using FallbackLogSink sink = new(primary, () => fallback, TextWriter.Null, Shared, User, "not used");

        sink.Emit(Event("one"));

        Assert.Equal(new[] { FallbackLogSink.MovedNotice(Shared, User), "one" }, fallback.Texts);
    }

    [Fact]
    public void Sink_UserLogCannotBeOpened_SaysNoLogOnceAndNeverThrows()
    {
        StringWriter notices = new();
        using FallbackLogSink sink = new(null, () => throw new IOException("disk full"), notices, Shared, User);

        Exception? exception = Record.Exception(() =>
        {
            sink.Emit(Event("one"));
            sink.Emit(Event("two"));
        });

        Assert.Null(exception);
        Assert.Equal(
            $"Note: neither the shared log folder {Shared} nor {User} can be written, so this run keeps no log file." + Environment.NewLine,
            notices.ToString());
    }

    [Fact]
    public void Sink_UserLogFailsToo_SaysNoLogOnceThenDropsQuietly()
    {
        RecordingSink primary = new() { Failure = new UnauthorizedAccessException("denied") };
        RecordingSink fallback = new() { Failure = new IOException("disk full") };
        StringWriter notices = new();
        using FallbackLogSink sink = new(primary, () => fallback, notices, Shared, User);

        sink.Emit(Event("one"));
        sink.Emit(Event("two"));

        string[] lines = notices.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(new[] { FallbackLogSink.NoLogNotice(Shared, User) }, lines);
        Assert.Equal(1, primary.Attempts);
        Assert.Equal(1, fallback.Attempts);
        Assert.True(fallback.Disposed);
    }

    [Fact]
    public void Sink_WithoutASeparateUserFolder_SaysNoLogOnce()
    {
        RecordingSink primary = new() { Failure = new UnauthorizedAccessException("denied") };
        StringWriter notices = new();
        using FallbackLogSink sink = new(primary, null, notices, Shared, User);

        sink.Emit(Event("one"));
        sink.Emit(Event("two"));

        Assert.Equal($"Note: the log folder {Shared} cannot be written, so this run keeps no log file." + Environment.NewLine, notices.ToString());
    }

    [Fact]
    public void Sink_Dispose_ClosesBothFiles()
    {
        RecordingSink primary = new() { Failure = new UnauthorizedAccessException("denied") };
        RecordingSink fallback = new();
        FallbackLogSink sink = new(primary, () => fallback, TextWriter.Null, Shared, User);
        sink.Emit(Event("one"));

        sink.Dispose();
        sink.Emit(Event("after dispose"));

        Assert.True(primary.Disposed);
        Assert.True(fallback.Disposed);
        Assert.DoesNotContain("after dispose", fallback.Texts);
    }

    // ----- Logging.Create over real folders -----

    [Fact]
    public void Create_NotElevatedAndNoSharedFolder_NeverCreatesTheDataRoot()
    {
        LogLocations locations = Locations();
        StringWriter notices = new();

        WriteThreeEvents(locations, elevated: false, notices);

        Assert.False(Directory.Exists(locations.DataDirectory));
        Assert.Equal(string.Empty, notices.ToString());
        string log = ReadSingleLog(locations.UserLogDirectory);
        Assert.Contains("event 1", log, StringComparison.Ordinal);
        Assert.Contains("event 3", log, StringComparison.Ordinal);
        string first = log.Split(Environment.NewLine)[0];
        Assert.EndsWith("[WRN] [tests] " + FallbackLogSink.MovedNotice(locations.SharedLogDirectory, locations.UserLogDirectory), first, StringComparison.Ordinal);
        Assert.Single(log.Split(Environment.NewLine), line => line.Contains("cannot be written", StringComparison.Ordinal));
    }

    [Fact]
    public void Create_NotElevatedAndWritableSharedFolder_LogsThereQuietly()
    {
        LogLocations locations = Locations();
        Directory.CreateDirectory(locations.SharedLogDirectory);
        StringWriter notices = new();

        WriteThreeEvents(locations, elevated: false, notices);

        Assert.Equal(string.Empty, notices.ToString());
        Assert.Contains("event 2", ReadSingleLog(locations.SharedLogDirectory), StringComparison.Ordinal);
        Assert.False(Directory.Exists(locations.UserLogDirectory));
    }

    [Fact]
    public void Create_SharedFolderOnlyAdministratorsMayWrite_FallsBackForAStandardUserWithoutAWordOnStandardError()
    {
        LogLocations locations = Locations();
        Directory.CreateDirectory(locations.DataDirectory);
        Logging.CreateProtectedDirectory(locations.SharedLogDirectory);
        StringWriter notices = new();

        WriteThreeEvents(locations, elevated: HostIsElevated, notices);

        Assert.Equal(string.Empty, notices.ToString());
        if (HostIsElevated)
        {
            Assert.Contains("event 3", ReadSingleLog(locations.SharedLogDirectory), StringComparison.Ordinal);
        }
        else
        {
            Assert.Empty(Directory.GetFiles(locations.SharedLogDirectory));
            string log = ReadSingleLog(locations.UserLogDirectory);
            Assert.Contains(FallbackLogSink.MovedNotice(locations.SharedLogDirectory, locations.UserLogDirectory), log, StringComparison.Ordinal);
            Assert.Contains("event 3", log, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Create_ElevatedAndNoDataRoot_CreatesItWithTheProtectedAccessList()
    {
        LogLocations locations = Locations();
        StringWriter notices = new();

        // The production owner check: the root this run creates is owned by Administrators when
        // the host is elevated (the owner is set explicitly), which is trusted, and by the host's
        // own account otherwise, which is not (finding R18).
        WriteThreeEvents(locations, elevated: true, notices);

        Assert.True(Directory.Exists(locations.DataDirectory));
        AssertProtectedAccessList(new DirectoryInfo(locations.DataDirectory).GetAccessControl());
        Assert.Equal(string.Empty, notices.ToString());
        if (HostIsElevated)
        {
            AssertProtectedAccessList(new DirectoryInfo(locations.SharedLogDirectory).GetAccessControl());
            Assert.Contains("event 1", ReadSingleLog(locations.SharedLogDirectory), StringComparison.Ordinal);
        }
        else
        {
            // This standard-user test host created the root under its own account, which an
            // elevated run does not trust, so it falls back to the per-user log, which says why.
            Assert.False(Directory.Exists(locations.SharedLogDirectory));
            string log = ReadSingleLog(locations.UserLogDirectory);
            string expected = $"Note: this run logs to {locations.UserLogDirectory}, not to the shared log folder {locations.SharedLogDirectory}: {locations.DataDirectory} is owned by ";
            Assert.Contains(expected, log, StringComparison.Ordinal);
            Assert.Contains(", not by Administrators or SYSTEM, so a standard user could redirect what this elevated run writes there.", log, StringComparison.Ordinal);
            Assert.Contains("event 1", log, StringComparison.Ordinal);
        }
    }

    // ----- The data root and its access list -----

    [Fact]
    public void DataDirectorySecurity_IsProtectedWithExactlyThreeInheritedGrants()
    {
        AssertProtectedAccessList(Logging.CreateDataDirectorySecurity());
    }

    [Fact]
    public void CreateProtectedDirectory_MissingParent_CreatesNothing()
    {
        string root = Path.Combine(_tempRoot, "NoProgramData", "UefiAccess");

        Assert.Throws<DirectoryNotFoundException>(() => Logging.CreateProtectedDirectory(root));
        Assert.False(Directory.Exists(Path.Combine(_tempRoot, "NoProgramData")));
    }

    [Fact]
    public void UserLogDirectory_IsUnderLocalApplicationData()
    {
        string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UefiAccess", "logs");

        Assert.Equal(expected, Logging.UserLogDirectory);
        Assert.NotEqual(Logging.LogDirectory, Logging.UserLogDirectory);
    }

    internal static void AssertProtectedAccessList(DirectorySecurity security)
    {
        Assert.True(security.AreAccessRulesProtected);
        FileSystemAccessRule[] rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();
        Assert.Equal(3, rules.Length);
        Assert.All(rules, rule =>
        {
            Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
            Assert.Equal(InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, rule.InheritanceFlags);
            Assert.Equal(PropagationFlags.None, rule.PropagationFlags);
            Assert.False(rule.IsInherited);
        });
        AssertGrant(rules, WellKnownSidType.LocalSystemSid, FileSystemRights.FullControl);
        AssertGrant(rules, WellKnownSidType.BuiltinAdministratorsSid, FileSystemRights.FullControl);
        AssertGrant(rules, WellKnownSidType.BuiltinUsersSid, FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize);
    }

    private static void AssertGrant(FileSystemAccessRule[] rules, WellKnownSidType sid, FileSystemRights rights)
    {
        SecurityIdentifier identity = new(sid, null);
        FileSystemAccessRule rule = Assert.Single(rules, r => identity.Equals(r.IdentityReference));
        Assert.Equal(rights, rule.FileSystemRights);
    }

    private static LogEvent Event(string text) => new(
        DateTimeOffset.Now,
        LogEventLevel.Information,
        null,
        Template,
        new[] { new LogEventProperty("Text", new ScalarValue(text)), new LogEventProperty("Component", new ScalarValue("cli")) });

    private static string ReadSingleLog(string directory)
    {
        string file = Assert.Single(Directory.GetFiles(directory, "uefiaccess-*.log"));
        return File.ReadAllText(file);
    }

    private static void WriteThreeEvents(LogLocations locations, bool elevated, TextWriter notices)
    {
        Serilog.ILogger logger = Logging.Create("tests", console: false, locations, elevated, notices);
        logger.Information("event {Number}", 1);
        logger.Information("event {Number}", 2);
        logger.Information("event {Number}", 3);
        (logger as IDisposable)?.Dispose();
    }

    /// <summary>A stand-in for ProgramData\UefiAccess and the per-user folder; the ProgramData stand-in exists, as the real one always does.</summary>
    private LogLocations Locations()
    {
        string programData = Path.Combine(_tempRoot, "ProgramData");
        Directory.CreateDirectory(programData);
        string data = Path.Combine(programData, "UefiAccess");
        return new LogLocations(data, Path.Combine(data, "logs"), Path.Combine(_tempRoot, "LocalAppData", "UefiAccess", "logs"));
    }

    /// <summary>Records what reaches it, and throws <see cref="Failure"/> from Emit when set.</summary>
    private sealed class RecordingSink : ILogEventSink, IDisposable
    {
        public List<LogEvent> Events { get; } = new();

        public Exception? Failure { get; init; }

        public int Attempts { get; private set; }

        public bool Disposed { get; private set; }

        /// <summary>The text of each event: the notice for a fallback notice, otherwise the test's own text.</summary>
        public IReadOnlyList<string> Texts => Events
            .Select(e => e.Properties.TryGetValue("Notice", out LogEventPropertyValue? notice) ? notice : e.Properties["Text"])
            .Select(value => (string)((ScalarValue)value).Value!)
            .ToArray();

        public void Emit(LogEvent logEvent)
        {
            Attempts++;
            if (Failure is not null)
            {
                throw Failure;
            }

            Events.Add(logEvent);
        }

        public void Dispose() => Disposed = true;
    }
}
