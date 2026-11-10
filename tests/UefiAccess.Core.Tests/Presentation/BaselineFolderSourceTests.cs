// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using System.Text.Json;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using UefiAccess.Core.Baseline;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Privileges;
using UefiAccess.Core.Settings.Dell;
using UefiAccess.Core.Tests.Fixtures;
using UefiAccess.Core.Variables;
using UefiAccess.Presentation.Resources;
using UefiAccess.Presentation.Services;
using Xunit;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// Round-trip and robustness tests for <see cref="BaselineFolderSource"/>: a folder written by
/// <see cref="BaselineExporter"/> loads back with every name, GUID, attribute set and byte
/// identical; a corrupted or missing raw file skips exactly that variable with a warning; a
/// missing, unparsable or absent index.json (or a missing folder) yields a failed result and never
/// an exception. Runs on any OS; the export goes to a temporary directory deleted afterwards.
/// </summary>
public sealed class BaselineFolderSourceTests : IDisposable
{
    private const string RunId = "20260910-143005";
    private const string OddName = "Setup Name/With:Odd*Chars?";
    private const string LocalTimestampText = "2026-09-10T14:30:05+02:00";
    private const VariableAttributes NvBsRt = VariableAttributes.NonVolatile | VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess;

    private static readonly Guid Global = KnownGuids.EfiGlobalVariable;
    private static readonly Guid ImageSecurity = KnownGuids.EfiImageSecurityDatabase;
    private static readonly Guid Vendor = new("12345678-90ab-cdef-1234-567890abcdef");
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 10, 14, 30, 5, TimeSpan.FromHours(2));

    private readonly string _tempRoot;

    public BaselineFolderSourceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "source-" + Guid.NewGuid().ToString("N"));
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
    public void Constructor_DescribesTheFolderAsOfflineAndRejectsNull()
    {
        BaselineFolderSource source = new(@"C:\ProgramData\UefiAccess\baseline\20260910-143005");

        Assert.Equal(@"C:\ProgramData\UefiAccess\baseline\20260910-143005", source.Folder);
        Assert.Equal(Strings.BaselinePrefix + @"C:\ProgramData\UefiAccess\baseline\20260910-143005", source.Description);
        Assert.StartsWith("Baseline (offline): ", source.Description, StringComparison.Ordinal);
        Assert.True(source.IsOffline);
        Assert.Throws<ArgumentNullException>(() => new BaselineFolderSource(null!));
    }

    [Fact]
    public async Task LoadAsync_RoundTripsEveryVariableFromTheExporterOutput()
    {
        FirmwareVariable[] originals = SampleVariables();
        string folder = Export(originals);
        BaselineFolderSource source = new(folder);

        SourceResult result = await source.LoadAsync(CancellationToken.None);

        Assert.Equal(SourceStatus.Loaded, result.Status);
        Assert.Null(result.Message);
        Assert.True(result.IsOffline);
        Assert.Equal(Strings.BaselinePrefix + folder + " (" + LocalTimestampText + ")", result.SourceDescription);
        Assert.Equal(FirmwareType.Uefi, result.FirmwareType);
        Assert.Null(result.Privilege);
        Assert.Equal(EnumerationMethod.InMemory, result.Method);
        Assert.Empty(result.Warnings);
        Assert.NotNull(result.Identity);
        Assert.Equal("Dell Inc.", result.Identity.Manufacturer);
        Assert.Equal("OptiPlex 7020", result.Identity.Model);
        Assert.Equal("A18", result.Identity.BiosVersion);
        Assert.Equal("ABC1234", result.Identity.ServiceTag);
        Assert.True(result.Identity.ExpectedModelConfirmed);

        Assert.Equal(5, result.Variables.Count);
        VariableSet loaded = new(result.Variables);
        foreach (FirmwareVariable original in originals)
        {
            FirmwareVariable? found = loaded.Find(original.Name, original.VendorGuid);
            Assert.NotNull(found);
            Assert.Equal(original.Name, found.Name);
            Assert.Equal(original.VendorGuid, found.VendorGuid);
            Assert.Equal(original.Attributes, found.Attributes);
            Assert.Equal(original.Data, found.Data);
            Assert.Equal(original.Key, found.Key);
        }

        FirmwareVariable odd = Assert.Single(result.Variables, v => v.Name == OddName);
        Assert.Equal("abc", Encoding.ASCII.GetString(odd.Data));
        FirmwareVariable empty = Assert.Single(result.Variables, v => v.Name == "dbx");
        Assert.Empty(empty.Data);
        Assert.Equal(NvBsRt | VariableAttributes.TimeBasedAuthenticatedWriteAccess, empty.Attributes);
    }

    [Fact]
    public async Task LoadAsync_ReturnsVariablesInIndexOrder()
    {
        string folder = Export(SampleVariables());

        SourceResult result = await new BaselineFolderSource(folder).LoadAsync(CancellationToken.None);

        Assert.Equal(
            new[] { OddName, "Boot0001", "BootOrder", "Timeout", "dbx" },
            result.Variables.Select(v => v.Name).ToArray());
    }

    [Fact]
    public async Task LoadAsync_HashMismatch_SkipsThatVariableWithAWarning()
    {
        string folder = Export(SampleVariables());
        BaselineEntry entry = EntryFor(folder, "BootOrder");
        File.WriteAllBytes(FullPath(folder, entry.File), new byte[] { 0x02, 0x00, 0x00, 0x00 });

        SourceResult result = await new BaselineFolderSource(folder).LoadAsync(CancellationToken.None);

        Assert.Equal(SourceStatus.Loaded, result.Status);
        Assert.Equal(4, result.Variables.Count);
        Assert.DoesNotContain(result.Variables, v => v.Name == "BootOrder");
        string warning = Assert.Single(result.Warnings);
        Assert.StartsWith(entry.Key, warning, StringComparison.Ordinal);
        Assert.Contains("SHA-256", warning, StringComparison.Ordinal);
        Assert.Contains("skipped", warning, StringComparison.Ordinal);
        Assert.Contains(result.Variables, v => v.Name == "Timeout");
        Assert.Contains(result.Variables, v => v.Name == "dbx");
    }

    [Fact]
    public async Task LoadAsync_MissingRawFile_SkipsThatVariableWithAWarning()
    {
        string folder = Export(SampleVariables());
        BaselineEntry entry = EntryFor(folder, "Timeout");
        File.Delete(FullPath(folder, entry.File));

        SourceResult result = await new BaselineFolderSource(folder).LoadAsync(CancellationToken.None);

        Assert.Equal(SourceStatus.Loaded, result.Status);
        Assert.Equal(4, result.Variables.Count);
        Assert.DoesNotContain(result.Variables, v => v.Name == "Timeout");
        string warning = Assert.Single(result.Warnings);
        Assert.StartsWith(entry.Key, warning, StringComparison.Ordinal);
        Assert.Contains("missing", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_WrongFileLength_SkipsThatVariableWithAWarning()
    {
        string folder = Export(SampleVariables());
        BaselineEntry entry = EntryFor(folder, "Boot0001");
        File.WriteAllBytes(FullPath(folder, entry.File), new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x00 });

        SourceResult result = await new BaselineFolderSource(folder).LoadAsync(CancellationToken.None);

        Assert.Equal(SourceStatus.Loaded, result.Status);
        Assert.Equal(4, result.Variables.Count);
        string warning = Assert.Single(result.Warnings);
        Assert.Contains("5 bytes but the index says 4", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_MissingIndex_Fails()
    {
        string folder = Export(SampleVariables());
        File.Delete(Path.Combine(folder, "index.json"));
        BaselineFolderSource source = new(folder);

        SourceResult? result = null;
        Exception? exception = await Record.ExceptionAsync(async () => result = await source.LoadAsync(CancellationToken.None));

        Assert.Null(exception);
        Assert.NotNull(result);
        Assert.Equal(SourceStatus.Failed, result.Status);
        Assert.NotNull(result.Message);
        Assert.Contains("index.json", result.Message, StringComparison.Ordinal);
        Assert.Equal(source.Description, result.SourceDescription);
        Assert.True(result.IsOffline);
        Assert.Empty(result.Variables);
        Assert.Empty(result.Warnings);
        Assert.Null(result.Identity);
        Assert.Null(result.Privilege);
        Assert.Equal(EnumerationMethod.None, result.Method);
        Assert.Equal(FirmwareType.Unknown, result.FirmwareType);
    }

    [Theory]
    [InlineData("this is not json {")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    public async Task LoadAsync_GarbageIndex_Fails(string content)
    {
        string folder = Export(SampleVariables());
        File.WriteAllText(Path.Combine(folder, "index.json"), content);
        BaselineFolderSource source = new(folder);

        SourceResult? result = null;
        Exception? exception = await Record.ExceptionAsync(async () => result = await source.LoadAsync(CancellationToken.None));

        Assert.Null(exception);
        Assert.NotNull(result);
        Assert.Equal(SourceStatus.Failed, result.Status);
        Assert.NotNull(result.Message);
        Assert.Contains("index.json", result.Message, StringComparison.Ordinal);
        Assert.Empty(result.Variables);
        Assert.Equal(source.Description, result.SourceDescription);
    }

    [Fact]
    public async Task LoadAsync_MissingFolder_Fails()
    {
        string missing = Path.Combine(_tempRoot, "no-such-folder");
        BaselineFolderSource source = new(missing);

        SourceResult? result = null;
        Exception? exception = await Record.ExceptionAsync(async () => result = await source.LoadAsync(CancellationToken.None));

        Assert.Null(exception);
        Assert.NotNull(result);
        Assert.Equal(SourceStatus.Failed, result.Status);
        Assert.NotNull(result.Message);
        Assert.Contains("does not exist", result.Message, StringComparison.Ordinal);
        Assert.Contains(missing, result.Message, StringComparison.Ordinal);
        Assert.Empty(result.Variables);
        Assert.Equal(Strings.BaselinePrefix + missing, result.SourceDescription);
    }

    [Fact]
    public async Task LoadAsync_DoesNotRequireDecodedPlatformOrLogFiles()
    {
        string folder = Export(SampleVariables());
        File.Delete(Path.Combine(folder, "decoded.json"));
        File.Delete(Path.Combine(folder, "platform.json"));
        File.Delete(Path.Combine(folder, "log.txt"));

        SourceResult result = await new BaselineFolderSource(folder).LoadAsync(CancellationToken.None);

        Assert.Equal(SourceStatus.Loaded, result.Status);
        Assert.Empty(result.Warnings);
        Assert.Equal(5, result.Variables.Count);
    }

    [Fact]
    public async Task LoadAsync_EmptyBaseline_LoadsZeroVariables()
    {
        string folder = Export(Array.Empty<FirmwareVariable>());

        SourceResult result = await new BaselineFolderSource(folder).LoadAsync(CancellationToken.None);

        Assert.Equal(SourceStatus.Loaded, result.Status);
        Assert.Empty(result.Variables);
        Assert.Empty(result.Warnings);
        Assert.Equal(FirmwareType.Uefi, result.FirmwareType);
        Assert.EndsWith(" (" + LocalTimestampText + ")", result.SourceDescription, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_IndexWithoutIdentity_LoadsWithNullIdentity()
    {
        string folder = Export(SampleVariables(), withIdentity: false);

        SourceResult result = await new BaselineFolderSource(folder).LoadAsync(CancellationToken.None);

        Assert.Equal(SourceStatus.Loaded, result.Status);
        Assert.Null(result.Identity);
        Assert.Equal(5, result.Variables.Count);
    }

    [Fact]
    public async Task LoadAsync_LegacyBiosBaseline_ReportsTheRecordedFirmwareType()
    {
        string folder = Export(Array.Empty<FirmwareVariable>(), firmwareType: FirmwareType.LegacyBios);

        SourceResult result = await new BaselineFolderSource(folder).LoadAsync(CancellationToken.None);

        Assert.Equal(SourceStatus.Loaded, result.Status);
        Assert.Equal(FirmwareType.LegacyBios, result.FirmwareType);
        Assert.Null(result.Privilege);
    }

    [Fact]
    public async Task LoadAsync_CancelledToken_FailsInsteadOfThrowing()
    {
        string folder = Export(SampleVariables());
        using CancellationTokenSource cts = new();
        cts.Cancel();

        SourceResult? result = null;
        Exception? exception = await Record.ExceptionAsync(async () => result = await new BaselineFolderSource(folder).LoadAsync(cts.Token));

        Assert.Null(exception);
        Assert.NotNull(result);
        Assert.Equal(SourceStatus.Failed, result.Status);
        Assert.Equal(BaselineReader.CancelledMessage, result.Message);
        Assert.Empty(result.Variables);
    }

    [Fact]
    public async Task LoadAsync_LogsWarningsAndTheOutcomeAtDebug_BelowTheLogPanelsMirror()
    {
        // The shell records the warnings (WARN), the outcome (INFO) and a failure (ERROR) in the
        // log panel once; the application mirrors the logger into that panel from Information up,
        // so the source's own account of the same events stays at Debug (in the file only).
        string folder = Export(SampleVariables());
        File.Delete(FullPath(folder, EntryFor(folder, "dbx").File));
        CollectingSink sink = new();
        ILogger logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();

        SourceResult result = await new BaselineFolderSource(folder, logger).LoadAsync(CancellationToken.None);

        Assert.Equal(SourceStatus.Loaded, result.Status);
        Assert.Contains(sink.Events, e => e.Level == LogEventLevel.Debug && e.RenderMessage().Contains("dbx", StringComparison.Ordinal));
        Assert.Contains(sink.Events, e => e.Level == LogEventLevel.Debug && e.RenderMessage().Contains("Loaded 4 variables", StringComparison.Ordinal));
        Assert.DoesNotContain(sink.Events, e => e.Level >= LogEventLevel.Information);

        File.Delete(Path.Combine(folder, "index.json"));
        sink.Events.Clear();
        SourceResult failed = await new BaselineFolderSource(folder, logger).LoadAsync(CancellationToken.None);

        Assert.Equal(SourceStatus.Failed, failed.Status);
        Assert.Contains(sink.Events, e => e.Level == LogEventLevel.Debug && e.RenderMessage().Contains("index.json", StringComparison.Ordinal));
        Assert.DoesNotContain(sink.Events, e => e.Level >= LogEventLevel.Information);
    }

    [Fact]
    public void DescribeLoaded_UsesTheLocalTimestampThenUtcThenNothing()
    {
        BaselineIndex full = new("0.1.0", "2026-09-10T12:30:05Z", LocalTimestampText, "Uefi", "Enabled", null, 0, Array.Empty<BaselineEntry>());
        BaselineIndex utcOnly = full with { TimestampLocal = string.Empty };
        BaselineIndex none = full with { TimestampLocal = null!, TimestampUtc = "  " };

        Assert.Equal("Baseline (offline): D:\\b (" + LocalTimestampText + ")", BaselineFolderSource.DescribeLoaded("D:\\b", full));
        Assert.Equal("Baseline (offline): D:\\b (2026-09-10T12:30:05Z)", BaselineFolderSource.DescribeLoaded("D:\\b", utcOnly));
        Assert.Equal("Baseline (offline): D:\\b", BaselineFolderSource.DescribeLoaded("D:\\b", none));
        Assert.Equal("Baseline (offline): D:\\b", BaselineFolderSource.DescribeLoaded("D:\\b", null));
        Assert.Equal(Strings.BaselinePrefix, BaselineFolderSource.DescribeLoaded(null!, null));
    }

    // ---- dell-settings.json ----------------------------------

    [Fact]
    public async Task DellFile_IsReadAndReturned()
    {
        DellSettingsResult read = DellResults.CompleteA18();
        string folder = Export(SampleVariables(), dell: DellSettingsFile.ToDocument(read));
        Assert.True(File.Exists(Path.Combine(folder, BaselineExporter.DellSettingsFileName)));

        SourceResult result = await new BaselineFolderSource(folder).LoadAsync(CancellationToken.None);

        Assert.Equal(SourceStatus.Loaded, result.Status);
        Assert.Empty(result.Warnings);
        Assert.Equal(5, result.Variables.Count);
        Assert.Equal(RunId, result.BaselineFolderName);
        Assert.NotNull(result.DellBaseline);
        Assert.True(result.DellBaseline.Present);
        Assert.Null(result.DellBaseline.Problem);
        DellSettingsResult recorded = Assert.IsType<DellSettingsResult>(result.DellBaseline.Settings);
        Assert.Equal(DellReadOutcome.Complete, recorded.Outcome);
        Assert.Equal(DellSettingsSource.DellCommandMonitor, recorded.Source);
        Assert.Equal(read.ReadAt.ToUniversalTime(), recorded.ReadAt);
        Assert.Equal(read.Machine, recorded.Machine);
        Assert.Equal(read.Settings.Select(DellSentences.RowSentence), recorded.Settings.Select(DellSentences.RowSentence));
        Assert.Equal(DcmRowFixtures.UnitA18Texts, recorded.Settings.Select(DellSentences.ValueText));

        // A trailing separator does not change the name the state line says.
        SourceResult again = await new BaselineFolderSource(folder + Path.DirectorySeparatorChar).LoadAsync(CancellationToken.None);
        Assert.Equal(RunId, again.BaselineFolderName);
        Assert.Equal(RunId, BaselineFolderSource.FolderName(folder + Path.AltDirectorySeparatorChar));
    }

    [Fact]
    public async Task NoDellFile_IsNotAWarning()
    {
        string folder = Export(SampleVariables());
        Assert.False(File.Exists(Path.Combine(folder, BaselineExporter.DellSettingsFileName)));

        SourceResult result = await new BaselineFolderSource(folder).LoadAsync(CancellationToken.None);

        Assert.Equal(SourceStatus.Loaded, result.Status);
        Assert.Empty(result.Warnings);
        Assert.Equal(5, result.Variables.Count);
        Assert.Equal(RunId, result.BaselineFolderName);
        Assert.NotNull(result.DellBaseline);
        Assert.False(result.DellBaseline.Present);
        Assert.Null(result.DellBaseline.Settings);
        Assert.Null(result.DellBaseline.Problem);

        // A folder that does not load carries neither, and no Dell file is read from it.
        File.Delete(Path.Combine(folder, "index.json"));
        SourceResult failed = await new BaselineFolderSource(folder).LoadAsync(CancellationToken.None);
        Assert.Equal(SourceStatus.Failed, failed.Status);
        Assert.Null(failed.DellBaseline);
        Assert.Null(failed.BaselineFolderName);
    }

    [Theory]
    [InlineData("{ this is not JSON", DellSettingsFile.UnknownFormatProblem)]
    [InlineData("{ \"Format\": \"uefiaccess-dell-settings/2\" }", DellSettingsFile.UnknownFormatProblem)]
    [InlineData(null, DellSettingsFile.TooLargeProblem)]
    public async Task UnusableDellFile_NeverFailsTheLoad(string? content, string problem)
    {
        string folder = Export(SampleVariables());
        string path = Path.Combine(folder, BaselineExporter.DellSettingsFileName);
        if (content is null)
        {
            File.WriteAllText(path, "{\"Format\":\"uefiaccess-dell-settings/1\",\"Pad\":\"" + new string('x', DellSettingsFile.MaximumFileSize) + "\"}");
        }
        else
        {
            File.WriteAllText(path, content);
        }

        CollectingSink sink = new();
        ILogger logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        SourceResult result = await new BaselineFolderSource(folder, logger).LoadAsync(CancellationToken.None);

        Assert.Equal(SourceStatus.Loaded, result.Status);
        Assert.Null(result.Message);
        Assert.Empty(result.Warnings);
        Assert.Equal(5, result.Variables.Count);
        Assert.NotNull(result.DellBaseline);
        Assert.True(result.DellBaseline.Present);
        Assert.Null(result.DellBaseline.Settings);
        Assert.Equal(problem, result.DellBaseline.Problem);

        // Recorded at Debug only: the Dell state line says it, so the log panel does not.
        Assert.Contains(sink.Events, e => e.Level == LogEventLevel.Debug && e.RenderMessage().Contains(problem, StringComparison.Ordinal));
        Assert.DoesNotContain(sink.Events, e => e.Level >= LogEventLevel.Information);
    }

    private static FirmwareVariable[] SampleVariables() => new[]
    {
        new FirmwareVariable("Timeout", Global, NvBsRt, new byte[] { 0x05, 0x00 }),
        new FirmwareVariable("BootOrder", Global, NvBsRt, new byte[] { 0x01, 0x00, 0x00, 0x00 }),
        new FirmwareVariable("dbx", ImageSecurity, NvBsRt | VariableAttributes.TimeBasedAuthenticatedWriteAccess, Array.Empty<byte>()),
        new FirmwareVariable(OddName, Vendor, VariableAttributes.RuntimeAccess, Encoding.ASCII.GetBytes("abc")),
        new FirmwareVariable("Boot0001", Global, NvBsRt, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }),
    };

    private static MachineIdentityResult SampleIdentity() => new(
        "Dell Inc.",
        "OptiPlex 7020",
        "OptiPlex 7020",
        "A18",
        "2015-03-01",
        "ABC1234",
        "0F5C5X",
        "Intel(R) Core(TM) i5-4590 CPU @ 3.30GHz",
        "Microsoft Windows 11 Pro",
        "10.0.22631",
        "22631",
        true,
        "Matches the planned 2014 OptiPlex 7020.",
        Array.Empty<string>());

    private string Export(IReadOnlyList<FirmwareVariable> variables, FirmwareType firmwareType = FirmwareType.Uefi, bool withIdentity = true, DellSettingsFileDocument? dell = null)
    {
        BaselineRequest request = new(
            _tempRoot,
            RunId,
            variables,
            firmwareType,
            PrivilegeState.Enabled,
            withIdentity ? SampleIdentity() : null,
            new { Note = "decoded" },
            new { Note = "platform" },
            "log line 1\n",
            "0.1.0",
            Timestamp)
        {
            DellSettings = dell,
        };
        BaselineExportResult result = new BaselineExporter().Export(request);
        Assert.Empty(result.Errors);
        return result.Directory;
    }

    private static BaselineEntry EntryFor(string folder, string name)
    {
        BaselineIndex index = JsonSerializer.Deserialize<BaselineIndex>(File.ReadAllText(Path.Combine(folder, "index.json")))!;
        return Assert.Single(index.Variables, e => e.Name == name);
    }

    private static string FullPath(string folder, string relative) => Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Collects Serilog events in memory so a test can assert on what the source logged.</summary>
    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
