// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using System.Text.Json;
using UefiAccess.Core.Baseline;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Privileges;
using UefiAccess.Core.Variables;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// Tests for <see cref="BaselineReader"/>: the exporter's output reads back byte for byte, every
/// integrity check (missing file, size, SHA-256, GUID, path escaping the folder) skips exactly one
/// entry with a warning, and a missing or unparsable index.json is an error, never an exception.
/// Runs on any OS: the export goes to a temporary directory that is deleted afterwards.
/// </summary>
public sealed class BaselineReaderTests : IDisposable
{
    private const string RunId = "20260910-143005";
    private const string OddName = "Setup Name/With:Odd*Chars?";
    private const VariableAttributes NvBsRt = VariableAttributes.NonVolatile | VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess;

    private static readonly Guid Global = KnownGuids.EfiGlobalVariable;
    private static readonly Guid ImageSecurity = KnownGuids.EfiImageSecurityDatabase;
    private static readonly Guid Vendor = new("12345678-90ab-cdef-1234-567890abcdef");
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 10, 14, 30, 5, TimeSpan.FromHours(2));

    private readonly string _tempRoot;

    public BaselineReaderTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "reader-" + Guid.NewGuid().ToString("N"));
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
    public void Read_RoundTripsTheExporterOutputExactly()
    {
        FirmwareVariable[] originals = SampleVariables();
        string folder = Export(originals);

        BaselineReadResult result = BaselineReader.Read(folder);

        Assert.Null(result.Error);
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Index);
        Assert.Equal(Path.GetFullPath(folder), result.Directory);
        Assert.Empty(result.Warnings);
        Assert.Equal(FirmwareType.Uefi, result.FirmwareType);
        Assert.Equal(PrivilegeState.Enabled, result.Privilege);
        Assert.Equal(Timestamp, result.Timestamp);
        Assert.NotNull(result.Identity);
        Assert.Equal("OptiPlex 7020", result.Identity.Model);
        Assert.Equal("ABC1234", result.Identity.ServiceTag);
        Assert.True(result.Identity.ExpectedModelConfirmed);

        Assert.Equal(originals.Length, result.Variables.Count);
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

        // Index order (sorted by key, ordinal) is preserved.
        string[] keys = result.Variables.Select(v => v.Key).ToArray();
        Assert.Equal(keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(), keys);
        Assert.Equal(result.Index.Variables.Select(e => e.Key).ToArray(), keys);
    }

    [Fact]
    public void Read_PreservesUndefinedAttributeBitsAndZeroLengthData()
    {
        FirmwareVariable[] originals =
        {
            new("Odd", Vendor, (VariableAttributes)0x107, new byte[] { 1, 2, 3 }),
            new("Empty", Vendor, VariableAttributes.None, Array.Empty<byte>()),
        };
        string folder = Export(originals);

        BaselineReadResult result = BaselineReader.Read(folder);

        Assert.Null(result.Error);
        Assert.Empty(result.Warnings);
        Assert.Equal(2, result.Variables.Count);
        FirmwareVariable odd = Assert.Single(result.Variables, v => v.Name == "Odd");
        Assert.Equal((VariableAttributes)0x107, odd.Attributes);
        Assert.Equal(new byte[] { 1, 2, 3 }, odd.Data);
        FirmwareVariable empty = Assert.Single(result.Variables, v => v.Name == "Empty");
        Assert.Equal(VariableAttributes.None, empty.Attributes);
        Assert.NotNull(empty.Data);
        Assert.Empty(empty.Data);
    }

    [Fact]
    public void Read_HashMismatch_SkipsOnlyThatEntry()
    {
        string folder = Export(SampleVariables());
        BaselineEntry entry = EntryFor(folder, "BootOrder");
        File.WriteAllBytes(FullPath(folder, entry.File), new byte[] { 0x02, 0x00, 0x00, 0x00 });

        BaselineReadResult result = BaselineReader.Read(folder);

        Assert.Null(result.Error);
        Assert.Equal(4, result.Variables.Count);
        Assert.DoesNotContain(result.Variables, v => v.Name == "BootOrder");
        string warning = Assert.Single(result.Warnings);
        Assert.StartsWith(entry.Key, warning, StringComparison.Ordinal);
        Assert.Contains("SHA-256", warning, StringComparison.Ordinal);
        Assert.Contains(entry.File, warning, StringComparison.Ordinal);
        Assert.Contains("skipped", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_SizeMismatch_SkipsOnlyThatEntry()
    {
        string folder = Export(SampleVariables());
        BaselineEntry entry = EntryFor(folder, "Timeout");
        File.WriteAllBytes(FullPath(folder, entry.File), new byte[] { 0x05, 0x00, 0x00 });

        BaselineReadResult result = BaselineReader.Read(folder);

        Assert.Null(result.Error);
        Assert.Equal(4, result.Variables.Count);
        Assert.DoesNotContain(result.Variables, v => v.Name == "Timeout");
        string warning = Assert.Single(result.Warnings);
        Assert.StartsWith(entry.Key, warning, StringComparison.Ordinal);
        Assert.Contains("3 bytes but the index says 2", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_MissingFile_SkipsOnlyThatEntry()
    {
        string folder = Export(SampleVariables());
        BaselineEntry entry = EntryFor(folder, "dbx");
        File.Delete(FullPath(folder, entry.File));

        BaselineReadResult result = BaselineReader.Read(folder);

        Assert.Null(result.Error);
        Assert.Equal(4, result.Variables.Count);
        Assert.DoesNotContain(result.Variables, v => v.Name == "dbx");
        string warning = Assert.Single(result.Warnings);
        Assert.StartsWith(entry.Key, warning, StringComparison.Ordinal);
        Assert.Contains("missing", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_SeveralBrokenEntries_ReportEachAndKeepTheRest()
    {
        string folder = Export(SampleVariables());
        File.Delete(FullPath(folder, EntryFor(folder, "dbx").File));
        File.WriteAllBytes(FullPath(folder, EntryFor(folder, "Boot0001").File), new byte[] { 0xDE, 0xAD, 0xBE, 0xEE });

        BaselineReadResult result = BaselineReader.Read(folder);

        Assert.Null(result.Error);
        Assert.Equal(2, result.Warnings.Count);
        Assert.Equal(new[] { OddName, "BootOrder", "Timeout" }, result.Variables.Select(v => v.Name).ToArray());
    }

    [Fact]
    public void Read_AcceptsUppercaseHashAndCaseInsensitivePropertyNames()
    {
        string folder = Export(SampleVariables());
        string json = File.ReadAllText(Path.Combine(folder, "index.json"));
        BaselineIndex index = JsonSerializer.Deserialize<BaselineIndex>(json)!;
        BaselineIndex upper = index with { Variables = index.Variables.Select(e => e with { Sha256 = e.Sha256.ToUpperInvariant() }).ToArray() };
        string camel = JsonSerializer.Serialize(upper, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        File.WriteAllText(Path.Combine(folder, "index.json"), camel);

        BaselineReadResult result = BaselineReader.Read(folder);

        Assert.Null(result.Error);
        Assert.Empty(result.Warnings);
        Assert.Equal(5, result.Variables.Count);
        Assert.Equal(FirmwareType.Uefi, result.FirmwareType);
    }

    [Fact]
    public void Read_EntryWhosePathLeavesTheFolder_IsSkippedAndNeverRead()
    {
        string folder = Export(SampleVariables());
        string outside = Path.Combine(_tempRoot, "outside.bin");
        File.WriteAllBytes(outside, new byte[] { 0x05, 0x00 });
        RewriteIndex(folder, index => index with
        {
            Variables = index.Variables.Select(e => e.Name == "Timeout" ? e with { File = "../outside.bin" } : e).ToArray(),
        });

        BaselineReadResult result = BaselineReader.Read(folder);

        Assert.Null(result.Error);
        Assert.Equal(4, result.Variables.Count);
        Assert.DoesNotContain(result.Variables, v => v.Name == "Timeout");
        string warning = Assert.Single(result.Warnings);
        Assert.Contains("leaves the baseline folder", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_EntryWithAbsolutePath_IsSkipped()
    {
        string folder = Export(SampleVariables());
        BaselineEntry timeout = EntryFor(folder, "Timeout");
        string absolute = FullPath(folder, timeout.File);
        RewriteIndex(folder, index => index with
        {
            Variables = index.Variables.Select(e => e.Name == "Timeout" ? e with { File = absolute } : e).ToArray(),
        });

        BaselineReadResult result = BaselineReader.Read(folder);

        Assert.Null(result.Error);
        Assert.Equal(4, result.Variables.Count);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void Read_EntryWithInvalidGuidOrEmptyNameOrNegativeSize_IsSkipped()
    {
        string folder = Export(SampleVariables());
        RewriteIndex(folder, index => index with
        {
            Variables = index.Variables.Select(e => e.Name switch
            {
                "Timeout" => e with { Guid = "not-a-guid" },
                "BootOrder" => e with { Name = string.Empty },
                "dbx" => e with { Size = -1 },
                _ => e,
            }).ToArray(),
        });

        BaselineReadResult result = BaselineReader.Read(folder);

        Assert.Null(result.Error);
        Assert.Equal(2, result.Variables.Count);
        Assert.Equal(3, result.Warnings.Count);
        Assert.Contains(result.Warnings, w => w.Contains("'not-a-guid' is not valid", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, w => w.Contains("the name is empty", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, w => w.Contains("is negative", StringComparison.Ordinal));
    }

    [Fact]
    public void Read_KeyMismatch_WarnsButKeepsTheVariable()
    {
        string folder = Export(SampleVariables());
        RewriteIndex(folder, index => index with
        {
            Variables = index.Variables.Select(e => e.Name == "Timeout" ? e with { Key = "wrong:key" } : e).ToArray(),
        });

        BaselineReadResult result = BaselineReader.Read(folder);

        Assert.Null(result.Error);
        Assert.Equal(5, result.Variables.Count);
        string warning = Assert.Single(result.Warnings);
        Assert.StartsWith("wrong:key: the index key does not match", warning, StringComparison.Ordinal);
        FirmwareVariable timeout = Assert.Single(result.Variables, v => v.Name == "Timeout");
        Assert.Equal(Global.ToString("D") + ":Timeout", timeout.Key);
    }

    [Fact]
    public void Read_VariableCountMismatch_WarnsAndUsesTheEntries()
    {
        string folder = Export(SampleVariables());
        RewriteIndex(folder, index => index with { VariableCount = 99 });

        BaselineReadResult result = BaselineReader.Read(folder);

        Assert.Null(result.Error);
        Assert.Equal(5, result.Variables.Count);
        string warning = Assert.Single(result.Warnings);
        Assert.Contains("VariableCount 99 but lists 5 entries", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_UnknownFirmwareTypeAndPrivilege_WarnAndFallBack()
    {
        string folder = Export(SampleVariables());
        RewriteIndex(folder, index => index with { FirmwareType = "Coreboot", PrivilegeState = "Maybe" });

        BaselineReadResult result = BaselineReader.Read(folder);

        Assert.Null(result.Error);
        Assert.Equal(FirmwareType.Unknown, result.FirmwareType);
        Assert.Null(result.Privilege);
        Assert.Equal(2, result.Warnings.Count);
        Assert.Contains(result.Warnings, w => w.Contains("'Coreboot'", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, w => w.Contains("'Maybe'", StringComparison.Ordinal));
        Assert.Equal(5, result.Variables.Count);
    }

    [Fact]
    public void Read_MinimalHandWrittenIndex_LoadsWithWarnings()
    {
        string folder = Path.Combine(_tempRoot, "minimal");
        Directory.CreateDirectory(Path.Combine(folder, "vars"));
        File.WriteAllBytes(Path.Combine(folder, "vars", "x.bin"), new byte[] { 0xAA });
        File.WriteAllText(
            Path.Combine(folder, "index.json"),
            "{ \"Variables\": [ { \"Name\": \"X\", \"Guid\": \"" + Vendor.ToString("D") + "\", \"Attributes\": 7, \"Size\": 1, \"Sha256\": \"" + Sha256Hex(new byte[] { 0xAA }) + "\", \"File\": \"vars/x.bin\" } ] }");

        BaselineReadResult result = BaselineReader.Read(folder);

        Assert.Null(result.Error);
        Assert.True(result.Succeeded);
        FirmwareVariable variable = Assert.Single(result.Variables);
        Assert.Equal("X", variable.Name);
        Assert.Equal(Vendor, variable.VendorGuid);
        Assert.Equal(NvBsRt, variable.Attributes);
        Assert.Equal(new byte[] { 0xAA }, variable.Data);
        Assert.Equal(FirmwareType.Unknown, result.FirmwareType);
        Assert.Null(result.Privilege);
        Assert.Null(result.Timestamp);
        Assert.Null(result.Identity);
        // Firmware type, privilege state, VariableCount (0 vs 1) and the key (null) are all reported.
        Assert.Equal(4, result.Warnings.Count);
    }

    [Theory]
    [InlineData("{ \"ToolVersion\": \"0.1.0\", \"FirmwareType\": \"Uefi\", \"PrivilegeState\": \"Enabled\", \"Variables\": null }")]
    [InlineData("{}")]
    [InlineData("{ \"name\": \"another tool's index\", \"version\": 2 }")]
    public void Read_IndexWithoutVariablesList_IsNotABaselineAndFails(string content)
    {
        string folder = Export(SampleVariables());
        File.WriteAllText(Path.Combine(folder, "index.json"), content);

        BaselineReadResult result = BaselineReader.Read(folder);

        Assert.False(result.Succeeded);
        Assert.Equal($"index.json in '{Path.GetFullPath(folder)}' is not a baseline index: it has no Variables list.", result.Error);
        Assert.Null(result.Index);
        Assert.Empty(result.Variables);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Read_MissingIndex_IsAnErrorNotAnException()
    {
        string folder = Export(SampleVariables());
        File.Delete(Path.Combine(folder, "index.json"));

        BaselineReadResult? result = null;
        Exception? exception = Record.Exception(() => result = BaselineReader.Read(folder));

        Assert.Null(exception);
        Assert.NotNull(result);
        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error);
        Assert.Contains("has no index.json", result.Error, StringComparison.Ordinal);
        Assert.Null(result.Index);
        Assert.Empty(result.Variables);
        Assert.Empty(result.Warnings);
        Assert.Equal(FirmwareType.Unknown, result.FirmwareType);
    }

    [Theory]
    [InlineData("this is not json {")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    [InlineData("[1, 2, 3]")]
    [InlineData("\"a string\"")]
    public void Read_UnparsableIndex_IsAnErrorNotAnException(string content)
    {
        string folder = Export(SampleVariables());
        File.WriteAllText(Path.Combine(folder, "index.json"), content);

        BaselineReadResult? result = null;
        Exception? exception = Record.Exception(() => result = BaselineReader.Read(folder));

        Assert.Null(exception);
        Assert.NotNull(result);
        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error);
        Assert.Contains("index.json", result.Error, StringComparison.Ordinal);
        Assert.Empty(result.Variables);
    }

    [Fact]
    public void Read_MissingFolder_IsAnError()
    {
        string missing = Path.Combine(_tempRoot, "does-not-exist");

        BaselineReadResult result = BaselineReader.Read(missing);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error);
        Assert.Contains("does not exist", result.Error, StringComparison.Ordinal);
        Assert.Contains(missing, result.Error, StringComparison.Ordinal);
        Assert.Empty(result.Variables);
    }

    [Fact]
    public void Read_FileInsteadOfFolder_IsAnError()
    {
        string file = Path.Combine(_tempRoot, "a-file.txt");
        File.WriteAllText(file, "not a folder");

        BaselineReadResult result = BaselineReader.Read(file);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error);
        Assert.Contains("does not exist", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Read_NullOrBlankFolder_IsAnError(string? folder)
    {
        BaselineReadResult result = BaselineReader.Read(folder!);

        Assert.False(result.Succeeded);
        Assert.Equal("No baseline folder was given.", result.Error);
        Assert.Empty(result.Variables);
    }

    [Fact]
    public void Read_CancelledToken_ReportsCancellationAsAnError()
    {
        string folder = Export(SampleVariables());
        using CancellationTokenSource cts = new();
        cts.Cancel();

        BaselineReadResult? result = null;
        Exception? exception = Record.Exception(() => result = BaselineReader.Read(folder, cts.Token));

        Assert.Null(exception);
        Assert.NotNull(result);
        Assert.Equal(BaselineReader.CancelledMessage, result.Error);
        Assert.Empty(result.Variables);
    }

    [Fact]
    public void Read_IgnoresDecodedPlatformAndLogFiles()
    {
        string folder = Export(SampleVariables());
        File.Delete(Path.Combine(folder, "decoded.json"));
        File.Delete(Path.Combine(folder, "platform.json"));
        File.Delete(Path.Combine(folder, "log.txt"));

        BaselineReadResult result = BaselineReader.Read(folder);

        Assert.Null(result.Error);
        Assert.Empty(result.Warnings);
        Assert.Equal(5, result.Variables.Count);
    }

    [Fact]
    public void TryParseIndex_ReportsEveryFailureShape()
    {
        Assert.False(BaselineReader.TryParseIndex(string.Empty, out BaselineIndex? empty, out string? emptyError));
        Assert.Null(empty);
        Assert.Equal("the file is empty.", emptyError);

        Assert.False(BaselineReader.TryParseIndex("{ not json", out BaselineIndex? garbage, out string? garbageError));
        Assert.Null(garbage);
        Assert.NotNull(garbageError);
        Assert.StartsWith("the JSON could not be parsed", garbageError, StringComparison.Ordinal);

        Assert.False(BaselineReader.TryParseIndex("null", out BaselineIndex? nul, out string? nulError));
        Assert.Null(nul);
        Assert.Equal("the document is the JSON literal null.", nulError);

        Assert.False(BaselineReader.TryParseIndex("{}", out BaselineIndex? bare, out string? bareError));
        Assert.Null(bare);
        Assert.Equal("it has no Variables list.", bareError);

        Assert.True(BaselineReader.TryParseIndex("{ \"variableCount\": 3, \"firmwareType\": \"Uefi\", \"variables\": [] }", out BaselineIndex? camel, out _));
        Assert.NotNull(camel);
        Assert.Equal(3, camel.VariableCount);
        Assert.Equal("Uefi", camel.FirmwareType);
        Assert.Empty(camel.Variables);
    }

    [Fact]
    public void TryResolveFile_AcceptsRelativePathsInsideTheFolderOnly()
    {
        string folder = Path.Combine(_tempRoot, "resolve");
        Directory.CreateDirectory(folder);

        Assert.True(BaselineReader.TryResolveFile(folder, "vars/a.bin", out string inside));
        Assert.Equal(Path.Combine(Path.GetFullPath(folder), "vars", "a.bin"), inside);

        Assert.True(BaselineReader.TryResolveFile(folder, "vars\\b.bin", out string backslash));
        Assert.Equal(Path.Combine(Path.GetFullPath(folder), "vars", "b.bin"), backslash);

        Assert.True(BaselineReader.TryResolveFile(folder, "vars/../c.bin", out string dotDotInside));
        Assert.Equal(Path.Combine(Path.GetFullPath(folder), "c.bin"), dotDotInside);

        Assert.False(BaselineReader.TryResolveFile(folder, "../escape.bin", out _));
        Assert.False(BaselineReader.TryResolveFile(folder, "vars/../../escape.bin", out _));
        Assert.False(BaselineReader.TryResolveFile(folder, Path.Combine(_tempRoot, "absolute.bin"), out _));
        Assert.False(BaselineReader.TryResolveFile(folder, string.Empty, out _));
        Assert.False(BaselineReader.TryResolveFile(folder, null, out _));
        Assert.False(BaselineReader.TryResolveFile(folder, ".", out _));
        Assert.False(BaselineReader.TryResolveFile(string.Empty, "vars/a.bin", out _));
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

    private string Export(IReadOnlyList<FirmwareVariable> variables)
    {
        BaselineRequest request = new(
            _tempRoot,
            RunId,
            variables,
            FirmwareType.Uefi,
            PrivilegeState.Enabled,
            SampleIdentity(),
            new { Note = "decoded" },
            new { Note = "platform" },
            "log line 1\n",
            "0.1.0",
            Timestamp);
        BaselineExportResult result = new BaselineExporter().Export(request);
        Assert.Empty(result.Errors);
        return result.Directory;
    }

    private static BaselineEntry EntryFor(string folder, string name)
    {
        BaselineIndex index = JsonSerializer.Deserialize<BaselineIndex>(File.ReadAllText(Path.Combine(folder, "index.json")))!;
        return Assert.Single(index.Variables, e => e.Name == name);
    }

    private static void RewriteIndex(string folder, Func<BaselineIndex, BaselineIndex> change)
    {
        string path = Path.Combine(folder, "index.json");
        BaselineIndex index = JsonSerializer.Deserialize<BaselineIndex>(File.ReadAllText(path))!;
        File.WriteAllText(path, JsonSerializer.Serialize(change(index), new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string FullPath(string folder, string relative) => Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));

    private static string Sha256Hex(byte[] data) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant();
}
