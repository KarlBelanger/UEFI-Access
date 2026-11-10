// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UefiAccess.Core.Baseline;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Privileges;
using UefiAccess.Core.Settings.Dell;
using UefiAccess.Core.Variables;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// Round-trip, determinism and robustness tests for <see cref="BaselineExporter"/>. Runs on any
/// OS without firmware access: the variables come from an in-memory <see cref="VariableSet"/>
/// and the export goes to a temporary directory that is deleted afterwards.
/// </summary>
public sealed class BaselineExporterTests : IDisposable
{
    private const string RunId = "20260910-143005";
    private const string OddName = "Setup Name/With:Odd*Chars?";
    private const string OddFile = "vars/12345678-90ab-cdef-1234-567890abcdef_Setup_Name_With_Odd_Chars_.bin";
    private const string Sha256OfAbc = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
    private const string Sha256OfEmpty = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    private const VariableAttributes NvBsRt = VariableAttributes.NonVolatile | VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess;

    private static readonly Guid Global = KnownGuids.EfiGlobalVariable;
    private static readonly Guid ImageSecurity = KnownGuids.EfiImageSecurityDatabase;
    private static readonly Guid Vendor = new("12345678-90ab-cdef-1234-567890abcdef");
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 10, 14, 30, 5, TimeSpan.FromHours(2));

    private readonly string _tempRoot;

    public BaselineExporterTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "baseline-" + Guid.NewGuid().ToString("N"));
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
    public void Export_WritesExpectedFolderLayout()
    {
        BaselineExportResult result = new BaselineExporter().Export(CreateRequest(SampleVariables()));

        Assert.Empty(result.Errors);
        Assert.Equal(Path.GetFullPath(Path.Combine(_tempRoot, RunId)), result.Directory);
        Assert.Equal(5, result.VariableCount);

        string[] topLevelFiles = Directory.GetFiles(result.Directory).Select(p => Path.GetFileName(p)).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "decoded.json", "index.json", "log.txt", "platform.json" }, topLevelFiles);
        Assert.Equal(new[] { "vars" }, Directory.GetDirectories(result.Directory).Select(p => Path.GetFileName(p)).ToArray());

        string[] varFiles = Directory.GetFiles(Path.Combine(result.Directory, "vars"));
        Assert.Equal(5, varFiles.Length);
        Assert.All(varFiles, path => Assert.EndsWith(".bin", path, StringComparison.Ordinal));

        Assert.Equal(9, result.FilesWritten.Count);
        Assert.Equal(5, result.FilesWritten.Count(f => f.StartsWith("vars/", StringComparison.Ordinal)));
        Assert.Equal(new[] { "index.json", "decoded.json", "platform.json", "log.txt" }, result.FilesWritten.Skip(5).ToArray());
    }

    [Fact]
    public void Export_RoundTripsEveryVariable()
    {
        VariableSet set = new(SampleVariables());
        BaselineExportResult result = new BaselineExporter().Export(CreateRequest(set.All));
        Assert.Empty(result.Errors);

        BaselineIndex index = ReadIndex(result.Directory);
        Assert.Equal(5, index.VariableCount);
        Assert.Equal(5, index.Variables.Count);

        foreach (BaselineEntry entry in index.Variables)
        {
            FirmwareVariable? original = set.Find(entry.Name, Guid.Parse(entry.Guid));
            Assert.NotNull(original);
            byte[] bytes = File.ReadAllBytes(ToFullPath(result.Directory, entry.File));
            Assert.Equal(original.Data, bytes);
            Assert.Equal(original.Key, entry.Key);
            Assert.Equal(original.Data.Length, entry.Size);
            Assert.Equal((uint)original.Attributes, entry.Attributes);
            Assert.Equal(VariableAttributeFlags.ToShortFlags(original.Attributes), entry.AttributeNames);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(original.Data)).ToLowerInvariant(), entry.Sha256);
            Assert.StartsWith("vars/", entry.File, StringComparison.Ordinal);
            Assert.EndsWith(".bin", entry.File, StringComparison.Ordinal);
            Assert.Contains(entry.File, result.FilesWritten);
        }

        BaselineEntry abc = Assert.Single(index.Variables, e => e.Name == OddName);
        Assert.Equal(Sha256OfAbc, abc.Sha256);
        Assert.Equal(3, abc.Size);
        Assert.Equal(OddFile, abc.File);
        Assert.Equal("abc", Encoding.ASCII.GetString(File.ReadAllBytes(ToFullPath(result.Directory, abc.File))));

        BaselineEntry empty = Assert.Single(index.Variables, e => e.Name == "dbx");
        Assert.Equal(0, empty.Size);
        Assert.Equal(Sha256OfEmpty, empty.Sha256);
        Assert.Empty(File.ReadAllBytes(ToFullPath(result.Directory, empty.File)));
    }

    [Fact]
    public void Export_IndexIsSortedByKeyOrdinal_WithFixedPropertyOrder()
    {
        BaselineExportResult result = new BaselineExporter().Export(CreateRequest(SampleVariables()));
        Assert.Empty(result.Errors);

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.Directory, "index.json")));
        JsonElement root = document.RootElement;

        Assert.Equal(
            new[] { "ToolVersion", "TimestampUtc", "TimestampLocal", "FirmwareType", "PrivilegeState", "Identity", "VariableCount", "Variables" },
            root.EnumerateObject().Select(p => p.Name).ToArray());

        JsonElement[] entries = root.GetProperty("Variables").EnumerateArray().ToArray();
        string[] keys = entries.Select(e => e.GetProperty("Key").GetString()!).ToArray();
        Assert.Equal(keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(), keys);
        Assert.Equal(
            new[]
            {
                "12345678-90ab-cdef-1234-567890abcdef:" + OddName,
                "8be4df61-93ca-11d2-aa0d-00e098032b8c:Boot0001",
                "8be4df61-93ca-11d2-aa0d-00e098032b8c:BootOrder",
                "8be4df61-93ca-11d2-aa0d-00e098032b8c:Timeout",
                "d719b2cb-3d3a-4596-a3bc-dad00e67656f:dbx",
            },
            keys);

        foreach (JsonElement entry in entries)
        {
            Assert.Equal(
                new[] { "Key", "Name", "Guid", "Attributes", "AttributeNames", "Size", "Sha256", "File" },
                entry.EnumerateObject().Select(p => p.Name).ToArray());
            Assert.DoesNotContain(entry.EnumerateObject(), p => p.Name.Contains("Time", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Export_RendersTimestampsAndEnumsAsText()
    {
        BaselineExportResult result = new BaselineExporter().Export(CreateRequest(SampleVariables()));
        Assert.Empty(result.Errors);

        string indexText = File.ReadAllText(Path.Combine(result.Directory, "index.json"));
        Assert.Contains("\n", indexText, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(indexText);
        JsonElement root = document.RootElement;
        Assert.Equal("0.1.0", root.GetProperty("ToolVersion").GetString());
        Assert.Equal("2026-09-10T12:30:05Z", root.GetProperty("TimestampUtc").GetString());
        Assert.Equal("2026-09-10T14:30:05+02:00", root.GetProperty("TimestampLocal").GetString());
        Assert.Equal("Uefi", root.GetProperty("FirmwareType").GetString());
        Assert.Equal("Enabled", root.GetProperty("PrivilegeState").GetString());
        Assert.Equal(5, root.GetProperty("VariableCount").GetInt32());
        Assert.Equal("OptiPlex 7020", root.GetProperty("Identity").GetProperty("Model").GetString());
        Assert.True(root.GetProperty("Identity").GetProperty("ExpectedModelConfirmed").GetBoolean());

        string decodedText = File.ReadAllText(Path.Combine(result.Directory, "decoded.json"));
        Assert.Contains("\"Attributes\": \"NonVolatile, RuntimeAccess\"", decodedText, StringComparison.Ordinal);
        Assert.Contains("\"BootOrder\"", decodedText, StringComparison.Ordinal);
        Assert.Contains("\"Boot0001\"", decodedText, StringComparison.Ordinal);

        string platformText = File.ReadAllText(Path.Combine(result.Directory, "platform.json"));
        Assert.Contains("\"SpecVersion\": \"1.2\"", platformText, StringComparison.Ordinal);

        string logText = File.ReadAllText(Path.Combine(result.Directory, "log.txt"));
        Assert.StartsWith("log line 1\nlog line 2\n" + BaselineExporter.LogOutcomeHeader + Environment.NewLine, logText, StringComparison.Ordinal);
    }

    [Fact]
    public void Export_WritesLogLast_AndAppendsTheOutcomeToTheRequestLogText()
    {
        BaselineExportResult result = new BaselineExporter().Export(CreateRequest(SampleVariables()));
        Assert.Empty(result.Errors);
        Assert.Equal("log.txt", result.FilesWritten[^1]);

        string[] lines = File.ReadAllLines(Path.Combine(result.Directory, "log.txt"));
        string[] filesBeforeLog = result.FilesWritten.Take(result.FilesWritten.Count - 1).ToArray();
        string[] expected = new[] { "log line 1", "log line 2", BaselineExporter.LogOutcomeHeader, "Variables indexed: 5", "Files written before log.txt: 8" }
            .Concat(filesBeforeLog.Select(file => "  " + file))
            .Concat(new[] { "Errors: 0" })
            .ToArray();
        Assert.Equal(expected, lines);
        Assert.Equal(8, filesBeforeLog.Length);
        Assert.DoesNotContain(lines, line => line.Contains(result.Directory, StringComparison.Ordinal));
    }

    [Fact]
    public void Export_LogRecordsErrors_WhenAFileCannotBeWritten()
    {
        const string blockedRelative = "vars/8be4df61-93ca-11d2-aa0d-00e098032b8c_Timeout.bin";
        Directory.CreateDirectory(ToFullPath(Path.Combine(_tempRoot, RunId), blockedRelative));

        BaselineExportResult result = new BaselineExporter().Export(CreateRequest(SampleVariables()));

        string error = Assert.Single(result.Errors);
        Assert.Equal("log.txt", result.FilesWritten[^1]);
        string[] lines = File.ReadAllLines(Path.Combine(result.Directory, "log.txt"));
        Assert.Contains("Files written before log.txt: 7", lines);
        Assert.Contains("Errors: 1", lines);
        Assert.Contains("  " + error, lines);
        Assert.DoesNotContain("  " + blockedRelative, lines);
        Assert.Contains("  index.json", lines);
        Assert.Contains("  platform.json", lines);
        Assert.Equal(Array.IndexOf(lines, "Errors: 1") + 1, Array.IndexOf(lines, "  " + error));
    }

    [Fact]
    public void Export_LogTextWithoutTrailingLineBreak_IsSeparatedFromTheOutcome()
    {
        BaselineRequest request = CreateRequest(SampleVariables()) with { LogText = "last line without break" };

        BaselineExportResult result = new BaselineExporter().Export(request);

        Assert.Empty(result.Errors);
        string logText = File.ReadAllText(Path.Combine(result.Directory, "log.txt"));
        Assert.StartsWith("last line without break" + Environment.NewLine + BaselineExporter.LogOutcomeHeader + Environment.NewLine, logText, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposeLogText_IsPureAndCarriesNoTimestamps()
    {
        string text = BaselineExporter.ComposeLogText(null, 2, new[] { "vars/a.bin", "index.json" }, new[] { "Cannot write 'x': denied" });

        string[] lines = text.Split(Environment.NewLine, StringSplitOptions.None);
        Assert.Equal(
            new[] { BaselineExporter.LogOutcomeHeader, "Variables indexed: 2", "Files written before log.txt: 2", "  vars/a.bin", "  index.json", "Errors: 1", "  Cannot write 'x': denied", string.Empty },
            lines);
        Assert.Equal(text, BaselineExporter.ComposeLogText(string.Empty, 2, new[] { "vars/a.bin", "index.json" }, new[] { "Cannot write 'x': denied" }));
        Assert.DoesNotContain("20", text.Substring(0, BaselineExporter.LogOutcomeHeader.Length), StringComparison.Ordinal);
    }

    [Fact]
    public void Export_IsByteForByteDeterministic()
    {
        BaselineRequest first = CreateRequest(SampleVariables()) with { RunId = "run-a" };
        BaselineRequest second = first with { RunId = "run-b", Variables = first.Variables.Reverse().ToArray() };
        BaselineExporter exporter = new();

        BaselineExportResult resultA = exporter.Export(first);
        BaselineExportResult resultB = exporter.Export(second);

        Assert.Empty(resultA.Errors);
        Assert.Empty(resultB.Errors);
        Assert.NotEqual(resultA.Directory, resultB.Directory);
        Assert.Equal(resultA.FilesWritten, resultB.FilesWritten);
        foreach (string relative in resultA.FilesWritten)
        {
            byte[] bytesA = File.ReadAllBytes(ToFullPath(resultA.Directory, relative));
            byte[] bytesB = File.ReadAllBytes(ToFullPath(resultB.Directory, relative));
            Assert.True(bytesA.AsSpan().SequenceEqual(bytesB), relative + " differs between the two exports");
        }
    }

    [Fact]
    public void Export_AppendsNumericSuffixOnFileNameCollision()
    {
        FirmwareVariable[] variables =
        {
            new("Foo?Bar", Vendor, VariableAttributes.RuntimeAccess, new byte[] { 3 }),
            new("Foo/Bar", Vendor, VariableAttributes.RuntimeAccess, new byte[] { 2 }),
            new("Foo Bar", Vendor, VariableAttributes.RuntimeAccess, new byte[] { 1 }),
        };

        BaselineExportResult result = new BaselineExporter().Export(CreateRequest(variables));
        Assert.Empty(result.Errors);

        BaselineIndex index = ReadIndex(result.Directory);
        const string stem = "vars/12345678-90ab-cdef-1234-567890abcdef_Foo_Bar";
        Assert.Equal(new[] { "Foo Bar", "Foo/Bar", "Foo?Bar" }, index.Variables.Select(e => e.Name).ToArray());
        Assert.Equal(new[] { stem + ".bin", stem + "_2.bin", stem + "_3.bin" }, index.Variables.Select(e => e.File).ToArray());
        Assert.Equal(3, Directory.GetFiles(Path.Combine(result.Directory, "vars")).Length);
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(ToFullPath(result.Directory, stem + ".bin")));
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(ToFullPath(result.Directory, stem + "_2.bin")));
        Assert.Equal(new byte[] { 3 }, File.ReadAllBytes(ToFullPath(result.Directory, stem + "_3.bin")));
    }

    [Fact]
    public void Export_TreatsFileNamesCaseInsensitivelyForCollisions()
    {
        FirmwareVariable[] variables =
        {
            new("abc", Vendor, VariableAttributes.RuntimeAccess, new byte[] { 2 }),
            new("ABC", Vendor, VariableAttributes.RuntimeAccess, new byte[] { 1 }),
        };

        BaselineExportResult result = new BaselineExporter().Export(CreateRequest(variables));
        Assert.Empty(result.Errors);

        BaselineIndex index = ReadIndex(result.Directory);
        Assert.Equal(new[] { "ABC", "abc" }, index.Variables.Select(e => e.Name).ToArray());
        Assert.Equal(
            new[] { "vars/12345678-90ab-cdef-1234-567890abcdef_ABC.bin", "vars/12345678-90ab-cdef-1234-567890abcdef_abc_2.bin" },
            index.Variables.Select(e => e.File).ToArray());
        Assert.Equal(2, Directory.GetFiles(Path.Combine(result.Directory, "vars")).Length);
    }

    [Fact]
    public void Export_TrimsLongFileNames()
    {
        string longName = new('x', 200);
        FirmwareVariable[] variables = { new(longName, Vendor, VariableAttributes.RuntimeAccess, new byte[] { 7 }) };

        BaselineExportResult result = new BaselineExporter().Export(CreateRequest(variables));
        Assert.Empty(result.Errors);

        BaselineEntry entry = Assert.Single(ReadIndex(result.Directory).Variables);
        Assert.Equal(longName, entry.Name);
        Assert.Equal("vars/" + BaselineExporter.SanitizeFileName(Vendor.ToString("D") + "_" + longName) + ".bin", entry.File);
        Assert.Equal("vars/".Length + BaselineExporter.MaxFileNameLength + ".bin".Length, entry.File.Length);
        Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(ToFullPath(result.Directory, entry.File)));
    }

    [Fact]
    public void Export_WritesNullDocumentsAndOutcomeOnlyLogForMissingOptionalInputs()
    {
        BaselineRequest request = CreateRequest(SampleVariables()) with { Decoded = null, Platform = null, LogText = null, Identity = null };

        BaselineExportResult result = new BaselineExporter().Export(request);
        Assert.Empty(result.Errors);
        Assert.Equal(9, result.FilesWritten.Count);

        Assert.Equal("null", File.ReadAllText(Path.Combine(result.Directory, "decoded.json")));
        Assert.Equal("null", File.ReadAllText(Path.Combine(result.Directory, "platform.json")));
        string logText = File.ReadAllText(Path.Combine(result.Directory, "log.txt"));
        Assert.StartsWith(BaselineExporter.LogOutcomeHeader + Environment.NewLine, logText, StringComparison.Ordinal);
        Assert.EndsWith("Errors: 0" + Environment.NewLine, logText, StringComparison.Ordinal);

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.Directory, "index.json")));
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Identity").ValueKind);
    }

    [Fact]
    public void Export_EmptyVariableList_WritesEmptyIndex()
    {
        BaselineExportResult result = new BaselineExporter().Export(CreateRequest(Array.Empty<FirmwareVariable>()));

        Assert.Empty(result.Errors);
        Assert.Equal(0, result.VariableCount);
        Assert.Equal(new[] { "index.json", "decoded.json", "platform.json", "log.txt" }, result.FilesWritten);
        Assert.True(Directory.Exists(Path.Combine(result.Directory, "vars")));
        Assert.Empty(Directory.GetFiles(Path.Combine(result.Directory, "vars")));

        BaselineIndex index = ReadIndex(result.Directory);
        Assert.Equal(0, index.VariableCount);
        Assert.Empty(index.Variables);
    }

    [Fact]
    public void Export_BlankRunId_UsesTimestampFolderName()
    {
        BaselineRequest request = CreateRequest(SampleVariables()) with { RunId = " " };

        BaselineExportResult result = new BaselineExporter().Export(request);

        Assert.Empty(result.Errors);
        Assert.Equal(Path.GetFullPath(Path.Combine(_tempRoot, "20260910-143005")), result.Directory);
        Assert.True(File.Exists(Path.Combine(result.Directory, "index.json")));
    }

    [Fact]
    public void Export_NullRequest_ReportsErrorWithoutThrowing()
    {
        BaselineExportResult? result = null;
        Exception? exception = Record.Exception(() => result = new BaselineExporter().Export(null!));

        Assert.Null(exception);
        Assert.NotNull(result);
        Assert.Single(result.Errors);
        Assert.Empty(result.FilesWritten);
        Assert.Equal(0, result.VariableCount);
        Assert.Equal(string.Empty, result.Directory);
    }

    [Fact]
    public void Export_UnwritableRoot_ReportsErrorWithoutThrowing()
    {
        string blocker = Path.Combine(_tempRoot, "blocker");
        File.WriteAllText(blocker, "a file where a directory is needed");
        BaselineRequest request = CreateRequest(SampleVariables()) with { RootDirectory = blocker };

        BaselineExportResult? result = null;
        Exception? exception = Record.Exception(() => result = new BaselineExporter().Export(request));

        Assert.Null(exception);
        Assert.NotNull(result);
        string error = Assert.Single(result.Errors);
        Assert.Contains("Cannot create export directory", error, StringComparison.Ordinal);
        Assert.Empty(result.FilesWritten);
        Assert.Equal(0, result.VariableCount);
        Assert.Equal(Path.GetFullPath(Path.Combine(blocker, RunId)), result.Directory);
    }

    [Fact]
    public void Export_PerFileFailure_IsReportedAndTheRestIsStillWritten()
    {
        const string blockedRelative = "vars/8be4df61-93ca-11d2-aa0d-00e098032b8c_BootOrder.bin";
        Directory.CreateDirectory(ToFullPath(Path.Combine(_tempRoot, RunId), blockedRelative));

        BaselineExportResult? result = null;
        Exception? exception = Record.Exception(() => result = new BaselineExporter().Export(CreateRequest(SampleVariables())));

        Assert.Null(exception);
        Assert.NotNull(result);
        string error = Assert.Single(result.Errors);
        Assert.Contains(blockedRelative, error, StringComparison.Ordinal);
        Assert.Equal(5, result.VariableCount);
        Assert.Equal(8, result.FilesWritten.Count);
        Assert.DoesNotContain(blockedRelative, result.FilesWritten);
        Assert.Contains("index.json", result.FilesWritten);

        BaselineIndex index = ReadIndex(result.Directory);
        Assert.Equal(5, index.Variables.Count);
        Assert.Contains(index.Variables, e => e.File == blockedRelative);
    }

    [Fact]
    public void DellSettings_WrittenWhenSet_BeforeLogTxt()
    {
        DellSettingsFileDocument document = SampleDellSettings();
        BaselineRequest request = CreateRequest(SampleVariables()) with { DellSettings = document };

        BaselineExportResult result = new BaselineExporter().Export(request);

        Assert.Empty(result.Errors);
        Assert.Equal("dell-settings.json", BaselineExporter.DellSettingsFileName);
        Assert.Equal(10, result.FilesWritten.Count);
        Assert.Equal(new[] { "index.json", "decoded.json", "platform.json", "dell-settings.json", "log.txt" }, result.FilesWritten.Skip(5).ToArray());
        string[] topLevelFiles = Directory.GetFiles(result.Directory).Select(p => Path.GetFileName(p)).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "decoded.json", "dell-settings.json", "index.json", "log.txt", "platform.json" }, topLevelFiles);

        // Written with the exporter's settings (PascalCase, indented, no BOM), and listed in log.txt.
        byte[] written = File.ReadAllBytes(Path.Combine(result.Directory, "dell-settings.json"));
        Assert.Equal(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(document, BaselineExporter.JsonOptions)), written);
        string text = Encoding.UTF8.GetString(written);
        Assert.StartsWith("{", text, StringComparison.Ordinal);
        Assert.Contains("\n  \"Format\": \"uefiaccess-dell-settings/1\",", text, StringComparison.Ordinal);
        string[] lines = File.ReadAllLines(Path.Combine(result.Directory, "log.txt"));
        Assert.Contains("Files written before log.txt: 9", lines);
        Assert.Equal(Array.IndexOf(lines, "  platform.json") + 1, Array.IndexOf(lines, "  dell-settings.json"));

        // The other files are those of the same request without Dell settings: index.json stays deterministic.
        BaselineExportResult without = new BaselineExporter().Export(CreateRequest(SampleVariables()) with { RunId = "without-dell" });
        Assert.Empty(without.Errors);
        foreach (string relative in without.FilesWritten.Where(file => file != "log.txt"))
        {
            Assert.True(File.ReadAllBytes(ToFullPath(result.Directory, relative)).AsSpan().SequenceEqual(File.ReadAllBytes(ToFullPath(without.Directory, relative))), relative + " differs");
        }

        // The baseline folder's Dell file reads back.
        DellSettingsFileResult read = DellSettingsFile.Read(result.Directory);
        Assert.True(read.Present);
        Assert.Null(read.Problem);
        Assert.Equal(DellSettingsSource.DellCommandMonitor, read.Settings!.Source);
        Assert.Equal("Enabled", Assert.Single(read.Settings.Settings, setting => setting.Id == "NumLock").Current!.Text);
    }

    [Fact]
    public void DellSettings_NotWrittenWhenNull()
    {
        BaselineRequest request = CreateRequest(SampleVariables());
        Assert.Null(request.DellSettings);

        BaselineExportResult result = new BaselineExporter().Export(request);

        Assert.Empty(result.Errors);
        Assert.DoesNotContain("dell-settings.json", result.FilesWritten);
        Assert.False(File.Exists(Path.Combine(result.Directory, "dell-settings.json")));
        Assert.DoesNotContain(File.ReadAllLines(Path.Combine(result.Directory, "log.txt")), line => line.Contains("dell-settings", StringComparison.Ordinal));
        Assert.Equal(new DellSettingsFileResult(false, null, null), DellSettingsFile.Read(result.Directory));
    }

    [Fact]
    public void BuildIndex_IsPureAndSorted()
    {
        BaselineRequest request = CreateRequest(SampleVariables());

        BaselineIndex index = BaselineExporter.BuildIndex(request);

        Assert.False(Directory.Exists(Path.Combine(_tempRoot, RunId)));
        Assert.Equal(5, index.VariableCount);
        string[] keys = index.Variables.Select(e => e.Key).ToArray();
        Assert.Equal(keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(), keys);
        Assert.Equal("2026-09-10T12:30:05Z", index.TimestampUtc);
        Assert.Equal("2026-09-10T14:30:05+02:00", index.TimestampLocal);
        Assert.Equal("Uefi", index.FirmwareType);
        Assert.Equal("Enabled", index.PrivilegeState);
        Assert.Same(request.Identity, index.Identity);
        Assert.Throws<ArgumentNullException>(() => BaselineExporter.BuildIndex(null!));
    }

    [Theory]
    [InlineData("BootOrder", "BootOrder")]
    [InlineData("8be4df61-93ca-11d2-aa0d-00e098032b8c_Boot0001", "8be4df61-93ca-11d2-aa0d-00e098032b8c_Boot0001")]
    [InlineData("a b/c\\d:e*f?g\"h<i>j|k", "a_b_c_d_e_f_g_h_i_j_k")]
    [InlineData("name.with-dash_and.dots", "name.with-dash_and.dots")]
    [InlineData("Ünïcödé", "_n_c_d_")]
    [InlineData("tab\there", "tab_here")]
    [InlineData("", "_")]
    public void SanitizeFileName_ReplacesDisallowedCharacters(string input, string expected)
    {
        Assert.Equal(expected, BaselineExporter.SanitizeFileName(input));
    }

    [Fact]
    public void SanitizeFileName_HandlesNullAndTrimsTo120Characters()
    {
        Assert.Equal("_", BaselineExporter.SanitizeFileName(null!));

        string exact = new('a', 120);
        Assert.Equal(exact, BaselineExporter.SanitizeFileName(exact));

        string trimmed = BaselineExporter.SanitizeFileName(new string('b', 121));
        Assert.Equal(120, trimmed.Length);
        Assert.Equal(new string('b', 120), trimmed);

        string mixed = BaselineExporter.SanitizeFileName(string.Concat(Enumerable.Repeat("c ", 200)));
        Assert.Equal(120, mixed.Length);
        Assert.Equal(string.Concat(Enumerable.Repeat("c_", 60)), mixed);
    }

    [Fact]
    public void DefaultRunId_AndDefaultRootDirectory()
    {
        Assert.Equal("20260910-143005", BaselineExporter.DefaultRunId(Timestamp));
        Assert.Equal("20260101-000000", BaselineExporter.DefaultRunId(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.EndsWith(Path.Combine("UefiAccess", "baseline"), BaselineExporter.DefaultRootDirectory, StringComparison.Ordinal);
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

    /// <summary>A Dell settings document as the baseline command passes it: one agreed row read through Dell Command Monitor (codes and Dell's names only).</summary>
    private static DellSettingsFileDocument SampleDellSettings() => new(
        DellSettingsFile.FormatName,
        "2026-09-10T12:30:05Z",
        "Partial",
        "DellCommandMonitor",
        new DellMachine("Dell Inc.", "OptiPlex 7020", "A18"),
        new[] { new DellSettingsFileSetting("NumLock", "Read", "2", "Enable", new[] { new DellSettingsFileValue("1", "Disable"), new DellSettingsFileValue("2", "Enable") }, "Interface", false) },
        Array.Empty<DellSettingsFileProblem>());

    private static object SampleDecoded() => new
    {
        BootOrder = new[] { "Boot0001" },
        Attributes = VariableAttributes.NonVolatile | VariableAttributes.RuntimeAccess,
        Nested = new { Timeout = 5 },
    };

    private static object SamplePlatform() => new
    {
        Tpm = new TpmInfoResult(true, "1.2", true, true, Array.Empty<string>()),
    };

    private BaselineRequest CreateRequest(IReadOnlyList<FirmwareVariable> variables) => new(
        _tempRoot,
        RunId,
        variables,
        FirmwareType.Uefi,
        PrivilegeState.Enabled,
        SampleIdentity(),
        SampleDecoded(),
        SamplePlatform(),
        "log line 1\nlog line 2\n",
        "0.1.0",
        Timestamp);

    private static BaselineIndex ReadIndex(string directory)
    {
        BaselineIndex? index = JsonSerializer.Deserialize<BaselineIndex>(File.ReadAllText(Path.Combine(directory, "index.json")));
        Assert.NotNull(index);
        return index;
    }

    private static string ToFullPath(string directory, string relative) => Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
}
