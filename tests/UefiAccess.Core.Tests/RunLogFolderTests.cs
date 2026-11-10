// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Reflection;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Core.Privileges;
using Xunit;
using static UefiAccess.Core.Tests.MainWindowMarkup;

namespace UefiAccess.Core.Tests;

/// <summary>
/// Final review, finding R12: the application's start-up log line and its crash message before
/// the window named C:\ProgramData\UefiAccess\logs even when Logging had moved this run's log to
/// the per-user folder (an elevated run that refuses a data root made by an older build or test
/// run, or a run that cannot write the shared folder), and the note that explains the move was only
/// inside the per-user file. Now the application finds the folder its log is really in
/// (src/UefiAccess.App/Platform/RunLogFolder.cs), names it in both places, and writes the note into
/// the log panel after the version line. The App cannot be loaded by this test project, so its
/// sources are read; the premise its check rests on (after the first event, today's file in the
/// shared folder can be opened for writing exactly when Logging is writing there, and opening it
/// that way creates nothing) is tested against the real Logging over temporary folders.
/// </summary>
public sealed class RunLogFolderTests : IDisposable
{
    private readonly string _tempRoot;

    public RunLogFolderTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "runlog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        TestFolders.DeleteQuietly(_tempRoot);
    }

    [Fact]
    public void TheVersionLineAndTheCrashMessage_NameTheFolderTheLogIsIn()
    {
        string app = Source("App.xaml.cs");
        Assert.DoesNotContain("Logging.LogDirectory", app, StringComparison.Ordinal);

        // Found once, right after the logger is created, before anything names the folder.
        string constructor = Method(app, "public App()");
        int created = constructor.IndexOf("_fileLogger = Logging.Create(\"app\", console: false);", StringComparison.Ordinal);
        int found = constructor.IndexOf("_logFolder = RunLogFolder.Find(_fileLogger);", StringComparison.Ordinal);
        Assert.True(created >= 0 && found > created, "the folder is found right after the logger is created");

        string startup = Method(app, "protected override void OnStartup(");
        Assert.Contains("\"UefiAccess.App {Version} starting on {Os}, 64-bit process {Is64Bit}, log directory {LogDirectory}\"", startup, StringComparison.Ordinal);
        Assert.Contains("_logFolder.Directory);", startup, StringComparison.Ordinal);
        Assert.Contains("_logFolder.Directory, Strings.WindowTitle, MessageBoxButton.OK, MessageBoxImage.Error);", Method(app, "private void AnnounceFailure("), StringComparison.Ordinal);

        // The note goes to the log panel only, after the version line: the per-user log already
        // starts with the same note.
        int version = startup.IndexOf("Logger.Information(", StringComparison.Ordinal);
        int note = startup.IndexOf("_logFolder.WriteNote(panelOnly);", StringComparison.Ordinal);
        Assert.True(version >= 0 && note > version, "the note follows the version line");
        Assert.Contains("ILogger panelOnly = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(_panelSink).CreateLogger();", startup, StringComparison.Ordinal);
        Assert.DoesNotContain("using (", startup, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFolderIsFoundWithLoggingsOwnCheck_AndAProbeThatCreatesAndWritesNothing()
    {
        string folder = Source("Platform", "RunLogFolder.cs");
        string find = Method(folder, "public static RunLogFolder Find(");

        // The first event is written before anything is checked: Logging opens its file then.
        int debug = find.IndexOf("fileLogger.Debug(", StringComparison.Ordinal);
        int elevated = find.IndexOf("if (new PrivilegeManager(fileLogger).IsElevated)", StringComparison.Ordinal);
        int refusal = find.IndexOf("Logging.PrepareDataFolderFor(shared);", StringComparison.Ordinal);
        int probe = find.IndexOf("CanWriteTodaysLog(shared)", StringComparison.Ordinal);
        Assert.True(debug >= 0 && debug < elevated && elevated < refusal && refusal < probe, "write one event; an elevated run asks Logging's check first; only then the probe");
        Assert.Contains("return new RunLogFolder(user, true, refusal);", find, StringComparison.Ordinal);
        Assert.Contains("new RunLogFolder(shared, false, null) : new RunLogFolder(user, true, null)", find, StringComparison.Ordinal);
        Assert.Contains("catch (Exception)", find, StringComparison.Ordinal);

        // The probe opens today's file, never creates one (FileMode.Open), and writes nothing.
        string canWrite = Method(folder, "private static bool CanWriteTodaysLog(");
        Assert.Contains("DateTime.Now.ToString(\"yyyyMMdd\", CultureInfo.InvariantCulture) + \".log\"", canWrite, StringComparison.Ordinal);
        Assert.Contains("new(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)", canWrite, StringComparison.Ordinal);
        Assert.DoesNotContain(".Write(", canWrite, StringComparison.Ordinal);
        Assert.DoesNotContain("FileMode.Create", folder, StringComparison.Ordinal);
        Assert.DoesNotContain("FileMode.Append", folder, StringComparison.Ordinal);
        Assert.DoesNotContain("FileMode.OpenOrCreate", folder, StringComparison.Ordinal);

        // The daily file name the probe builds is Logging's.
        Assert.Contains("private const string DailyFilePrefix = \"uefiaccess-\";", folder, StringComparison.Ordinal);
        FieldInfo? pattern = typeof(Logging).GetField("FileNamePattern", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(pattern);
        Assert.Equal("uefiaccess-.log", pattern!.GetRawConstantValue());
    }

    [Fact]
    public void ThePanelNote_IsWordedAsTheNoteAtTheTopOfThePerUserLog()
    {
        string folder = Source("Platform", "RunLogFolder.cs");
        const string Shared = @"C:\ProgramData\UefiAccess\logs";
        const string User = @"C:\Users\karl\AppData\Local\UefiAccess\logs";
        const string Reason = "the reason.";

        Assert.Contains("\"Note: this run logs to {LogDirectory}, not to the shared log folder {SharedLogDirectory}: {Reason}\"", folder, StringComparison.Ordinal);
        Assert.Equal(
            FallbackLogSink.MovedNotice(Shared, User, Reason),
            "Note: this run logs to {LogDirectory}, not to the shared log folder {SharedLogDirectory}: {Reason}".Replace("{LogDirectory}", User, StringComparison.Ordinal).Replace("{SharedLogDirectory}", Shared, StringComparison.Ordinal).Replace("{Reason}", Reason, StringComparison.Ordinal));
        Assert.Contains("\"Note: the shared log folder {SharedLogDirectory} cannot be written, so this run logs to {LogDirectory}.\"", folder, StringComparison.Ordinal);
        Assert.Equal(
            FallbackLogSink.MovedNotice(Shared, User),
            "Note: the shared log folder {SharedLogDirectory} cannot be written, so this run logs to {LogDirectory}.".Replace("{LogDirectory}", User, StringComparison.Ordinal).Replace("{SharedLogDirectory}", Shared, StringComparison.Ordinal));

        string note = Method(folder, "public void WriteNote(");
        Assert.Contains("if (!Moved)", note, StringComparison.Ordinal);
        Assert.Contains("panelOnly.Warning(MovedTemplate, Directory, Logging.LogDirectory, Refusal);", note, StringComparison.Ordinal);
        Assert.Contains("panelOnly.Warning(UnwritableTemplate, Logging.LogDirectory, Directory);", note, StringComparison.Ordinal);
    }

    [Fact]
    public void Probe_WritableSharedFolder_SaysShared_WhileTheLoggerHoldsTheFile()
    {
        LogLocations locations = Locations();
        Directory.CreateDirectory(locations.SharedLogDirectory);

        Serilog.ILogger logger = Logging.Create("app", console: false, locations, elevated: false, TextWriter.Null);
        try
        {
            logger.Debug("first event");
            Assert.True(CanWriteTodaysLog(locations.SharedLogDirectory), "today's shared file is open in the logger and can be opened for writing beside it");
            logger.Information("second event");
        }
        finally
        {
            (logger as IDisposable)?.Dispose();
        }

        Assert.False(Directory.Exists(locations.UserLogDirectory));
        string file = Assert.Single(Directory.GetFiles(locations.SharedLogDirectory, "uefiaccess-*.log"));
        Assert.Equal(TodaysFile(locations.SharedLogDirectory), file);
        string text = File.ReadAllText(file);
        Assert.Contains("first event", text, StringComparison.Ordinal);
        Assert.Contains("second event", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Probe_NoSharedFolder_SaysPerUser_AndCreatesNothing()
    {
        LogLocations locations = Locations();

        Serilog.ILogger logger = Logging.Create("app", console: false, locations, elevated: false, TextWriter.Null);
        try
        {
            logger.Debug("first event");
            Assert.False(CanWriteTodaysLog(locations.SharedLogDirectory));
        }
        finally
        {
            (logger as IDisposable)?.Dispose();
        }

        Assert.False(Directory.Exists(locations.DataDirectory));
        Assert.Single(Directory.GetFiles(locations.UserLogDirectory, "uefiaccess-*.log"));
    }

    [Fact]
    public void Probe_SharedFolderOnlyAdministratorsMayWrite_AgreesWithWhereLoggingWrote()
    {
        LogLocations locations = Locations();
        Directory.CreateDirectory(locations.DataDirectory);
        Logging.CreateProtectedDirectory(locations.SharedLogDirectory);
        bool elevated = PrivilegeManager.ComputeIsElevated();

        Serilog.ILogger logger = Logging.Create("app", console: false, locations, elevated, TextWriter.Null);
        bool probe;
        try
        {
            logger.Debug("first event");
            probe = CanWriteTodaysLog(locations.SharedLogDirectory);
        }
        finally
        {
            (logger as IDisposable)?.Dispose();
        }

        // A standard user (this host, normally) cannot write there, so Logging moved to the
        // per-user folder and the probe says so; an elevated host writes the shared folder.
        bool wroteShared = Directory.GetFiles(locations.SharedLogDirectory, "uefiaccess-*.log").Length == 1;
        Assert.Equal(elevated, wroteShared);
        Assert.Equal(wroteShared, probe);
        Assert.Equal(!wroteShared, Directory.Exists(locations.UserLogDirectory));
    }

    /// <summary>The application's probe (RunLogFolder.CanWriteTodaysLog), which the source check above holds to these exact arguments.</summary>
    private static bool CanWriteTodaysLog(string folder)
    {
        try
        {
            using FileStream stream = new(TodaysFile(folder), FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string TodaysFile(string folder) => Path.Combine(folder, "uefiaccess-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");

    /// <summary>A stand-in for ProgramData\UefiAccess and the per-user folder; the ProgramData stand-in exists, as the real one always does.</summary>
    private LogLocations Locations()
    {
        string programData = Path.Combine(_tempRoot, "ProgramData");
        Directory.CreateDirectory(programData);
        string data = Path.Combine(programData, "UefiAccess");
        return new LogLocations(data, Path.Combine(data, "logs"), Path.Combine(_tempRoot, "LocalAppData", "UefiAccess", "logs"));
    }
}
