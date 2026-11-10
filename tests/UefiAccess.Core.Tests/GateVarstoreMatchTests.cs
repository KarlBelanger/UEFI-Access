// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using System.Text.RegularExpressions;
using UefiAccess.Cli;
using UefiAccess.Core.Variables;
using Xunit;
using static UefiAccess.Core.Tests.CliTestHarness;

namespace UefiAccess.Core.Tests;

/// <summary>
/// Gate question (c), the varstore match of "uefiaccess gate --ifr-varstores": a varstore the
/// enumeration did not list is "absent" only when the enumeration was complete. After the
/// known-names fallback, an early stop or a failed enumeration the gate reads the varstore
/// directly: found is present, not found is absent, and any other failure is "unknown" with the
/// reason, counted apart from the absent ones. Also the file itself: quoted names are unquoted,
/// 0x sizes accepted, and a file that cannot be read stops the gate with exit 2.
/// </summary>
public sealed class GateVarstoreMatchTests : IDisposable
{
    private const VariableAttributes NvBsRt = VariableAttributes.NonVolatile | VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess;
    private static readonly Guid SetupGuid = new("EC87D643-EBA4-4BB5-A1E5-3F3E36B20DA9");

    private readonly string _tempRoot;

    public GateVarstoreMatchTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "gate-varstores-" + Guid.NewGuid().ToString("N"));
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
    public void CompleteEnumeration_MissingVarstoreIsAbsent_WithoutADirectRead()
    {
        ScriptedStore store = new(new EnumerationResult(new[] { BootOrder() }, EnumerationMethod.NtEnumerate, 1, null, Array.Empty<string>()));
        store.Readable.Add(Setup(741));
        string outFile = Path.Combine(_tempRoot, "gate.json");

        CliRun run = Run(CreatePlatform(store), "gate", "--out", outFile, "--ifr-varstores", WriteVarstores("Setup;EC87D643-EBA4-4BB5-A1E5-3F3E36B20DA9;0x2E5"));

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Empty(store.Reads);
        JsonElement match = ReadMatch(outFile);
        Assert.True(match.GetProperty("enumerationComplete").GetBoolean());
        Assert.Equal(JsonValueKind.Null, match.GetProperty("incompleteReason").ValueKind);
        Assert.Equal(1, match.GetProperty("absentCount").GetInt32());
        Assert.Equal(0, match.GetProperty("unknownCount").GetInt32());
        JsonElement entry = match.GetProperty("entries")[0];
        Assert.False(entry.GetProperty("present").GetBoolean());
        Assert.Equal("absent", entry.GetProperty("status").GetString());
        Assert.Equal("absent at runtime: boot-service only (no EFI_VARIABLE_RUNTIME_ACCESS) or not stored as a UEFI variable", entry.GetProperty("note").GetString());
        AssertRow(run.Output, "(c) Enumeration complete", "true");
        AssertRow(run.Output, "(c) Status unknown", "0");
    }

    [Fact]
    public void FallbackEnumeration_ReadsUnlistedVarstoresDirectly_AndNeverCallsAFailedReadAbsent()
    {
        ScriptedStore store = ScriptedStore.Fallback(BootOrder());
        store.Readable.Add(Setup(741));
        store.Failures["SaSetup"] = ReadStatus.AccessDenied;
        string outFile = Path.Combine(_tempRoot, "gate.json");
        string ifr = WriteVarstores(
            "Setup;EC87D643-EBA4-4BB5-A1E5-3F3E36B20DA9;741",
            "PchSetup;4570B7F1-ADE8-4943-8DC3-406472842384;51",
            "SaSetup;72C5E28C-7783-43A1-8767-FAD73FCCAFA4;44");

        CliRun run = Run(CreatePlatform(store), "gate", "--out", outFile, "--ifr-varstores", ifr);

        // The fallback always warns, so the enumeration alone makes the run partial.
        Assert.Equal(ExitCodes.Partial, run.ExitCode);
        Assert.Equal(new[] { "Setup", "PchSetup", "SaSetup" }, store.Reads);
        Assert.Contains(
            "Warning: ifr-varstores: 1 varstore could not be checked because the known-names fallback lists only the curated names and Boot#### entries and the direct read failed; their status is unknown\n",
            run.Error,
            StringComparison.Ordinal);

        JsonElement match = ReadMatch(outFile);
        Assert.False(match.GetProperty("enumerationComplete").GetBoolean());
        Assert.Equal("the known-names fallback lists only the curated names and Boot#### entries", match.GetProperty("incompleteReason").GetString());
        Assert.Equal(3, match.GetProperty("entryCount").GetInt32());
        Assert.Equal(1, match.GetProperty("presentCount").GetInt32());
        Assert.Equal(1, match.GetProperty("absentCount").GetInt32());
        Assert.Equal(1, match.GetProperty("unknownCount").GetInt32());

        JsonElement[] entries = match.GetProperty("entries").EnumerateArray().ToArray();
        Assert.True(entries[0].GetProperty("present").GetBoolean());
        Assert.Equal("present", entries[0].GetProperty("status").GetString());
        Assert.Equal("NV BS RT", entries[0].GetProperty("attributeFlags").GetString());
        Assert.True(entries[0].GetProperty("sizeMatches").GetBoolean());
        Assert.Equal("present at runtime (found by a direct read; the incomplete enumeration did not list it); size matches the IFR", entries[0].GetProperty("note").GetString());

        Assert.False(entries[1].GetProperty("present").GetBoolean());
        Assert.Equal("absent", entries[1].GetProperty("status").GetString());
        Assert.StartsWith("absent at runtime (confirmed by a direct read, because the enumeration was incomplete): ", entries[1].GetProperty("note").GetString(), StringComparison.Ordinal);

        Assert.Equal(JsonValueKind.Null, entries[2].GetProperty("present").ValueKind);
        Assert.Equal("unknown", entries[2].GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, entries[2].GetProperty("sizeMatches").ValueKind);
        Assert.Equal(
            "unknown: the enumeration was incomplete (the known-names fallback lists only the curated names and Boot#### entries) and a direct read failed (AccessDenied: ERROR_ACCESS_DENIED (5): access denied)",
            entries[2].GetProperty("note").GetString());

        AssertRow(run.Output, "(c) Present at runtime", "1");
        AssertRow(run.Output, "(c) Absent at runtime", "1");
        AssertRow(run.Output, "(c) Status unknown", "1");
        AssertRow(run.Output, "(c) Enumeration complete", Regex.Escape("false: the known-names fallback lists only the curated names and Boot#### entries; unlisted names were read directly"));
        Assert.Matches(@"(?m)^SaSetup +72c5e28c-7783-43a1-8767-fad73fccafa4 +44 +unknown +- +- +-$", run.Output);
    }

    [Fact]
    public void FailedEnumeration_StillWritesGateJson_WithEveryVarstoreReadDirectly()
    {
        ScriptedStore store = new(new EnumerationResult(Array.Empty<FirmwareVariable>(), EnumerationMethod.None, 0, "NtEnumerateSystemEnvironmentValuesEx failed: STATUS_INVALID_PARAMETER (0xC000000D)", Array.Empty<string>()));
        store.Readable.Add(Setup(745));
        string outFile = Path.Combine(_tempRoot, "gate.json");

        CliRun run = Run(CreatePlatform(store), "gate", "--out", outFile, "--ifr-varstores", WriteVarstores("Setup;EC87D643-EBA4-4BB5-A1E5-3F3E36B20DA9;741"));

        Assert.Equal(ExitCodes.NativeFailure, run.ExitCode);
        JsonElement match = ReadMatch(outFile);
        Assert.Equal("no variables could be enumerated", match.GetProperty("incompleteReason").GetString());
        Assert.Equal(1, match.GetProperty("presentCount").GetInt32());
        Assert.Equal(1, match.GetProperty("sizeMismatchCount").GetInt32());
        Assert.Equal(
            "present at runtime (found by a direct read; the incomplete enumeration did not list it); runtime size 745 differs from IFR size 741",
            match.GetProperty("entries")[0].GetProperty("note").GetString());
    }

    [Fact]
    public void QuotedNameAsIfrExtractorPrintsIt_MatchesTheVariable()
    {
        ScriptedStore store = new(new EnumerationResult(new[] { Setup(741) }, EnumerationMethod.NtEnumerate, 1, null, Array.Empty<string>()));
        string outFile = Path.Combine(_tempRoot, "gate.json");

        CliRun run = Run(CreatePlatform(store), "gate", "--out", outFile, "--ifr-varstores", WriteVarstores("\"Setup\";EC87D643-EBA4-4BB5-A1E5-3F3E36B20DA9;0x2E5"));

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Equal(string.Empty, run.Error);
        JsonElement entry = ReadMatch(outFile).GetProperty("entries")[0];
        Assert.Equal("Setup", entry.GetProperty("name").GetString());
        Assert.Equal("present", entry.GetProperty("status").GetString());
        Assert.Equal(741, entry.GetProperty("ifrSize").GetInt32());
    }

    [Fact]
    public void UnreadableVarstoreFile_IsAUsageError_WithOneSentence_AndNoReport()
    {
        ScriptedStore store = new(new EnumerationResult(new[] { Setup(741) }, EnumerationMethod.NtEnumerate, 1, null, Array.Empty<string>()));
        string ifr = WriteVarstores("Setup;EC87D643-EBA4-4BB5-A1E5-3F3E36B20DA9;741");
        string outFile = Path.Combine(_tempRoot, "gate.json");

        CliRun run;
        using (new FileStream(ifr, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            run = Run(CreatePlatform(store), "gate", "--json", "--out", outFile, "--ifr-varstores", ifr);
        }

        Assert.Equal(ExitCodes.Usage, run.ExitCode);
        string[] errorLines = run.Error.TrimEnd('\n').Split('\n');
        string line = Assert.Single(errorLines);
        Assert.StartsWith("The file '" + ifr + "' given to --ifr-varstores could not be read: IOException: ", line, StringComparison.Ordinal);
        Assert.False(File.Exists(outFile));
        Assert.Empty(store.Reads);
        JsonElement document = ParseSingleDocument(run.Output);
        Assert.Equal(ExitCodes.Usage, document.GetProperty("exitCode").GetInt32());
        Assert.Equal(line, document.GetProperty("error").GetString());
    }

    private static FirmwareVariable BootOrder() => new("BootOrder", KnownGuids.EfiGlobalVariable, NvBsRt, new byte[] { 0, 0 });

    private static FirmwareVariable Setup(int size) => new("Setup", SetupGuid, NvBsRt, new byte[size]);

    private static JsonElement ReadMatch(string outFile)
    {
        using JsonDocument gate = JsonDocument.Parse(File.ReadAllText(outFile));
        return gate.RootElement.GetProperty("varstoreMatch").Clone();
    }

    private string WriteVarstores(params string[] lines)
    {
        string path = Path.Combine(_tempRoot, "ifr-varstores-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllLines(path, lines);
        return path;
    }
}
