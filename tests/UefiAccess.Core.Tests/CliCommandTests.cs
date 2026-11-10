// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using UefiAccess.Cli;
using UefiAccess.Cli.Services;
using UefiAccess.Core.Baseline;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Privileges;
using UefiAccess.Core.Tests.Fixtures;
using UefiAccess.Core.Variables;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// Runs the seven CLI commands through the real command classes (Program.Run, System.CommandLine,
/// CommandRunner, Preconditions and the command bodies) against a <see cref="CliPlatform"/> built
/// from an <see cref="InMemoryFirmwareVariableStore"/> and fakes: a firmware type detector that
/// answers Uefi, a privilege manager that reports an elevated token with the privilege enabled,
/// platform inspectors that return fixed records, and a scripted Dell settings reader that answers
/// the unit's values through Dell Command | Monitor (the dell command itself is tested in
/// CliDellCommandTests). None of them touches WMI, the registry, PowerShell, a Dell interface or a
/// native call, so the tests run on any host. The store holds the load-option
/// fixtures (Windows Boot Manager, USB stick, legacy BBS, PXE) as Boot0000 to Boot0003 plus
/// BootOrder (with a dangling reference), BootCurrent, Timeout, SecureBoot, SetupMode, PK, KEK, db
/// and dbx. Standard output and standard error are captured separately, so the tests can assert
/// that --json prints exactly one document and that warnings go to standard error only.
/// </summary>
public sealed class CliCommandTests : IDisposable
{
    private const VariableAttributes NvBsRt = VariableAttributes.NonVolatile | VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess;
    private const VariableAttributes BsRt = VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess;
    private const VariableAttributes NvBsRtTa = NvBsRt | VariableAttributes.TimeBasedAuthenticatedWriteAccess;
    private const int VariableCount = 13;
    private const string GlobalGuidText = "8be4df61-93ca-11d2-aa0d-00e098032b8c";
    private const string ImageSecurityGuidText = "d719b2cb-3d3a-4596-a3bc-dad00e67656f";

    /// <summary>PK is one X509 list: 28-byte header + 16-byte owner + 600-byte certificate.</summary>
    private const int PkSize = 644;

    private static readonly Guid Global = KnownGuids.EfiGlobalVariable;
    private static readonly Guid ImageSecurity = KnownGuids.EfiImageSecurityDatabase;

    /// <summary>BootOrder: Boot0000, Boot0003, Boot0001, Boot0002, then Boot0005 which does not exist.</summary>
    private static readonly byte[] BootOrderData = UInt16Array(0x0000, 0x0003, 0x0001, 0x0002, 0x0005);

    private readonly string _tempRoot;

    public CliCommandTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "cli-" + Guid.NewGuid().ToString("N"));
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

    // ---- info ------------------------------------------------------------------------------

    [Fact]
    public void Info_PrintsVersionPreconditionsAndIdentity()
    {
        CliRun run = Run("info");

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Equal(string.Empty, run.Error);
        AssertRow(run.Output, "Tool version", Regex.Escape(ToolVersion.Current));
        AssertRow(run.Output, "Firmware type", "Uefi");
        AssertRow(run.Output, "Elevated", "true");
        AssertRow(run.Output, "Privilege state", "Enabled");
        AssertRow(run.Output, "Manufacturer", "Dell Inc.");
        AssertRow(run.Output, "Model", "OptiPlex 7020");
        AssertRow(run.Output, "BIOS version", "A18");
        AssertRow(run.Output, "Expected model confirmed", "true");
    }

    [Fact]
    public void Info_Json_IsOneDocumentWithTheSharedVersion()
    {
        CliRun run = Run("info", "--json");

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Equal(string.Empty, run.Error);
        JsonElement document = ParseSingleDocument(run.Output);
        Assert.Equal("info", document.GetProperty("command").GetString());
        Assert.True(document.GetProperty("ok").GetBoolean());
        Assert.Equal(ToolVersion.Current, document.GetProperty("toolVersion").GetString());
        Assert.Equal("Uefi", document.GetProperty("firmwareType").GetString());
        Assert.True(document.GetProperty("elevated").GetBoolean());
        Assert.Equal("Enabled", document.GetProperty("privilegeState").GetString());
        Assert.Equal("OptiPlex 7020", document.GetProperty("identity").GetProperty("model").GetString());
        Assert.Equal("Microsoft Windows 11 Pro 10.0.22631 build 22631", document.GetProperty("os").GetString());
    }

    [Fact]
    public void Info_IdentityErrors_MakeItPartial()
    {
        MachineIdentityResult identity = Identity() with { Errors = new[] { "Win32_BIOS: query timed out" } };
        CliRun run = Run(Platform(identity: identity), "info");

        Assert.Equal(ExitCodes.Partial, run.ExitCode);
        Assert.Equal("Warning: machine identity: Win32_BIOS: query timed out" + "\n", run.Error);
        AssertRow(run.Output, "Model", "OptiPlex 7020");
    }

    // ---- list ------------------------------------------------------------------------------

    [Fact]
    public void List_PrintsEveryVariableSortedByKeyWithFlagsAndSize()
    {
        CliRun run = Run("list");

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Equal(string.Empty, run.Error);
        string[] lines = Lines(run.Output);
        Assert.Matches(@"^Key +Flags +Size$", lines[0]);

        string[] keys = lines.Skip(1).TakeWhile(line => line.Length > 0).Select(line => line.Split(' ')[0]).ToArray();
        string[] expectedKeys = SampleVariables().Select(v => v.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.Equal(expectedKeys, keys);
        Assert.Equal(VariableCount, keys.Length);

        Assert.Matches("^" + Regex.Escape(GlobalGuidText + ":BootOrder") + @" +NV BS RT +10$", lines.Single(line => line.Contains(":BootOrder ", StringComparison.Ordinal)));
        Assert.Matches("^" + Regex.Escape(GlobalGuidText + ":SecureBoot") + @" +BS RT +1$", lines.Single(line => line.Contains(":SecureBoot ", StringComparison.Ordinal)));
        Assert.Matches("^" + Regex.Escape(GlobalGuidText + ":PK") + @" +NV BS RT TA +" + PkSize + "$", lines.Single(line => line.Contains(":PK ", StringComparison.Ordinal)));
        Assert.Equal(VariableCount + " of " + VariableCount + " variables listed (method InMemory, " + VariableCount + " entries parsed).", lines[^1]);
    }

    [Fact]
    public void List_Json_ListsEveryVariable()
    {
        CliRun run = Run("list", "--json");

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Equal(string.Empty, run.Error);
        JsonElement document = ParseSingleDocument(run.Output);
        Assert.Equal("list", document.GetProperty("command").GetString());
        Assert.True(document.GetProperty("ok").GetBoolean());
        Assert.Equal("InMemory", document.GetProperty("enumeration").GetProperty("method").GetString());
        Assert.Equal(VariableCount, document.GetProperty("enumeration").GetProperty("count").GetInt32());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("filter").ValueKind);
        Assert.Equal(VariableCount, document.GetProperty("listed").GetInt32());

        JsonElement[] variables = document.GetProperty("variables").EnumerateArray().ToArray();
        Assert.Equal(VariableCount, variables.Length);
        JsonElement bootOrder = variables.Single(v => v.GetProperty("name").GetString() == "BootOrder");
        Assert.Equal(GlobalGuidText + ":BootOrder", bootOrder.GetProperty("key").GetString());
        Assert.Equal(GlobalGuidText, bootOrder.GetProperty("guid").GetString());
        Assert.Equal(7u, bootOrder.GetProperty("attributes").GetUInt32());
        Assert.Equal("NV BS RT", bootOrder.GetProperty("flags").GetString());
        Assert.Equal(new[] { "NonVolatile", "BootServiceAccess", "RuntimeAccess" }, bootOrder.GetProperty("attributeNames").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.Equal(10, bootOrder.GetProperty("size").GetInt32());
    }

    [Fact]
    public void List_FilterAndGuidNarrowTheTable()
    {
        CliRun filtered = Run("list", "--filter", "boot0");
        Assert.Equal(ExitCodes.Success, filtered.ExitCode);
        Assert.Equal(4, Lines(filtered.Output).Count(line => line.StartsWith(GlobalGuidText + ":Boot0", StringComparison.Ordinal)));
        Assert.Contains("4 of " + VariableCount + " variables listed", filtered.Output, StringComparison.Ordinal);

        CliRun byGuid = Run("list", "--guid", "{D719B2CB-3D3A-4596-A3BC-DAD00E67656F}", "--json");
        Assert.Equal(ExitCodes.Success, byGuid.ExitCode);
        JsonElement document = ParseSingleDocument(byGuid.Output);
        Assert.Equal(ImageSecurityGuidText, document.GetProperty("guid").GetString());
        Assert.Equal(new[] { "db", "dbx" }, document.GetProperty("variables").EnumerateArray().Select(v => v.GetProperty("name").GetString()).ToArray());

        CliRun badGuid = Run("list", "--guid", "not-a-guid");
        Assert.Equal(ExitCodes.Usage, badGuid.ExitCode);
        Assert.Contains("is not a GUID", badGuid.Error, StringComparison.Ordinal);
    }

    // ---- read ------------------------------------------------------------------------------

    [Fact]
    public void Read_BootOrderHex_PrintsAttributesSizeAndTheDump()
    {
        CliRun run = Run("read", "BootOrder", "--hex");

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Equal(string.Empty, run.Error);
        AssertRow(run.Output, "Name", "BootOrder");
        AssertRow(run.Output, "GUID", GlobalGuidText);
        AssertRow(run.Output, "Key", GlobalGuidText + ":BootOrder");
        AssertRow(run.Output, "Status", "Success");
        AssertRow(run.Output, "Attributes", "0x00000007 NV BS RT");
        AssertRow(run.Output, "Attribute names", "NonVolatile, BootServiceAccess, RuntimeAccess");
        AssertRow(run.Output, "Size", "10 bytes");
        Assert.Matches(@"(?m)^00000000  00 00 03 00 01 00 02 00  05 00 +\.{10}$", run.Output);
        Assert.DoesNotContain("Use --hex", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_Json_CarriesBase64AndHexLines()
    {
        CliRun run = Run("read", "BootOrder", "--hex", "--json");

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        JsonElement document = ParseSingleDocument(run.Output);
        Assert.Equal("read", document.GetProperty("command").GetString());
        Assert.True(document.GetProperty("ok").GetBoolean());
        Assert.Equal("Success", document.GetProperty("status").GetString());
        Assert.Equal(7u, document.GetProperty("attributes").GetUInt32());
        Assert.Equal("NV BS RT", document.GetProperty("flags").GetString());
        Assert.Equal(10, document.GetProperty("size").GetInt32());
        Assert.Equal(BootOrderData, Convert.FromBase64String(document.GetProperty("dataBase64").GetString()!));
        Assert.Single(document.GetProperty("hex").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("outFile").ValueKind);
    }

    [Fact]
    public void Read_DefaultsToTheImageSecurityGuidForDbx_AndWritesOut()
    {
        string outFile = Path.Combine(_tempRoot, "dbx.bin");
        CliRun run = Run("read", "dbx", "--out", outFile);

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        AssertRow(run.Output, "GUID", ImageSecurityGuidText);
        Assert.Equal(SignatureListFixtures.Sha256List(3), File.ReadAllBytes(outFile));
        Assert.Contains("Wrote " + SignatureListFixtures.Sha256List(3).Length + " bytes to ", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_MissingVariable_FailsWithNativeFailure()
    {
        CliRun run = Run("read", "Boot0005", "--json");

        Assert.Equal(ExitCodes.NativeFailure, run.ExitCode);
        Assert.Contains("Reading Boot0005 (" + GlobalGuidText + ") failed: NotFound", run.Error, StringComparison.Ordinal);
        JsonElement document = ParseSingleDocument(run.Output);
        Assert.False(document.GetProperty("ok").GetBoolean());
        Assert.Equal("NotFound", document.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("dataBase64").ValueKind);
    }

    // ---- boot ------------------------------------------------------------------------------

    [Fact]
    public void Boot_ListsEveryEntryInBootOrderOrderAndFlagsTheMissingOne()
    {
        CliRun run = Run("boot");

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Equal("Warning: BootOrder position 5 references Boot0005, which does not exist." + "\n", run.Error);

        AssertRow(run.Output, "BootCurrent", Regex.Escape("Boot0000 (Windows Boot Manager)"));
        AssertRow(run.Output, "BootNext", Regex.Escape("(not set)"));
        AssertRow(run.Output, "Timeout", "2 seconds");
        AssertRow(run.Output, "BootOrder", Regex.Escape("Boot0000, Boot0003, Boot0001, Boot0002, Boot0005 (missing)"));
        AssertRow(run.Output, "Boot entries", "4");

        string[] expectedOrderRows =
        {
            @"^1 +Boot0000 +" + Regex.Escape(LoadOptionFixtures.WindowsBootManagerDescription) + "$",
            @"^2 +Boot0003 +" + Regex.Escape(LoadOptionFixtures.PxeDescription) + "$",
            @"^3 +Boot0001 +" + Regex.Escape(LoadOptionFixtures.UsbStickDescription) + "$",
            @"^4 +Boot0002 +" + Regex.Escape(LoadOptionFixtures.LegacyBbsDescription) + "$",
            @"^5 +Boot0005 +" + Regex.Escape("(missing: no Boot0005 variable)") + "$",
        };
        AssertInOrder(run.Output, expectedOrderRows);

        AssertInOrder(
            run.Output,
            "^Boot0000  " + Regex.Escape(LoadOptionFixtures.WindowsBootManagerDescription) + "$",
            "^  Device path:         " + Regex.Escape(LoadOptionFixtures.WindowsBootManagerPath) + "$",
            "^  Optional data:       15 bytes, text \"WINDOWS\"$",
            "^Boot0001  " + Regex.Escape(LoadOptionFixtures.UsbStickDescription) + "$",
            "^  Device path:         " + Regex.Escape(LoadOptionFixtures.UsbStickPath) + "$",
            "^Boot0002  " + Regex.Escape(LoadOptionFixtures.LegacyBbsDescription) + "$",
            "^  Device path:         " + Regex.Escape(LoadOptionFixtures.LegacyBbsPath) + "$",
            "^Boot0003  " + Regex.Escape(LoadOptionFixtures.PxeDescription) + "$",
            "^  Device path:         " + Regex.Escape(LoadOptionFixtures.PxePath) + "$");
        Assert.Contains("MAC(00:11:22:33:44:55,1)", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("  Error:", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Boot_Json_ResolvesBootOrderAndDecodesEveryEntry()
    {
        CliRun run = Run("boot", "--json");

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        JsonElement document = ParseSingleDocument(run.Output);
        Assert.Equal("boot", document.GetProperty("command").GetString());
        Assert.True(document.GetProperty("bootOrderPresent").GetBoolean());
        Assert.Equal(2, document.GetProperty("timeoutSeconds").GetInt32());
        Assert.Equal("Windows Boot Manager", document.GetProperty("bootCurrent").GetProperty("description").GetString());
        Assert.False(document.GetProperty("bootNext").GetProperty("present").GetBoolean());

        JsonElement[] order = document.GetProperty("bootOrder").EnumerateArray().ToArray();
        Assert.Equal(new[] { "Boot0000", "Boot0003", "Boot0001", "Boot0002", "Boot0005" }, order.Select(e => e.GetProperty("name").GetString()).ToArray());
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, order.Select(e => e.GetProperty("position").GetInt32()).ToArray());
        Assert.Equal(
            new[] { LoadOptionFixtures.WindowsBootManagerDescription, LoadOptionFixtures.PxeDescription, LoadOptionFixtures.UsbStickDescription, LoadOptionFixtures.LegacyBbsDescription, null },
            order.Select(e => e.GetProperty("description").GetString()).ToArray());
        Assert.All(order.Take(4), e => Assert.True(e.GetProperty("entryExists").GetBoolean()));
        Assert.All(order.Take(4), e => Assert.Equal(JsonValueKind.Null, e.GetProperty("note").ValueKind));
        Assert.False(order[4].GetProperty("entryExists").GetBoolean());
        Assert.Equal("missing", order[4].GetProperty("note").GetString());

        JsonElement[] entries = document.GetProperty("entries").EnumerateArray().ToArray();
        Assert.Equal(new[] { "Boot0000", "Boot0001", "Boot0002", "Boot0003" }, entries.Select(e => e.GetProperty("name").GetString()).ToArray());
        Assert.Equal(LoadOptionFixtures.PxePath, entries[3].GetProperty("devicePath").GetString());
        Assert.Equal("WINDOWS", entries[0].GetProperty("optionalDataText").GetString());
        Assert.All(entries, e => Assert.True(e.GetProperty("active").GetBoolean()));
        Assert.All(entries, e => Assert.True(e.GetProperty("inBootOrder").GetBoolean()));
        Assert.All(entries, e => Assert.Equal(JsonValueKind.Null, e.GetProperty("error").ValueKind));

        string[] warnings = document.GetProperty("warnings").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(new[] { "BootOrder position 5 references Boot0005, which does not exist." }, warnings);
    }

    // ---- secureboot ------------------------------------------------------------------------

    [Fact]
    public void SecureBoot_Json_ReportsTheVariablesAndThePolicy()
    {
        CliRun run = Run("secureboot", "--json");

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Equal(string.Empty, run.Error);
        JsonElement document = ParseSingleDocument(run.Output);
        Assert.Equal("secureboot", document.GetProperty("command").GetString());
        Assert.True(document.GetProperty("ok").GetBoolean());

        JsonElement state = document.GetProperty("state");
        Assert.True(state.GetProperty("secureBoot").GetBoolean());
        Assert.False(state.GetProperty("setupMode").GetBoolean());
        Assert.Equal(JsonValueKind.Null, state.GetProperty("auditMode").ValueKind);
        Assert.True(state.GetProperty("pkPresent").GetBoolean());
        Assert.Equal(PkSize, state.GetProperty("pkSize").GetInt32());
        Assert.True(state.GetProperty("kekPresent").GetBoolean());
        Assert.Equal(1, state.GetProperty("db").GetProperty("listCount").GetInt32());
        Assert.Equal(1, state.GetProperty("db").GetProperty("signatureCount").GetInt32());
        Assert.Equal(3, state.GetProperty("dbx").GetProperty("signatureCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, state.GetProperty("dbt").ValueKind);

        JsonElement policy = document.GetProperty("policy");
        Assert.True(policy.GetProperty("registryEnabled").GetBoolean());
        Assert.True(policy.GetProperty("variableSecureBoot").GetBoolean());
        Assert.True(policy.GetProperty("powerShellConfirm").GetBoolean());
        Assert.True(policy.GetProperty("capable").GetBoolean());
        Assert.True(document.GetProperty("sourcesAgree").GetBoolean());
        Assert.Empty(document.GetProperty("warnings").EnumerateArray());
    }

    [Fact]
    public void SecureBoot_Text_PrintsBothTables()
    {
        CliRun run = Run("secureboot");

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Equal(string.Empty, run.Error);
        AssertRow(run.Output, "SecureBoot", Regex.Escape("enabled (1)"));
        AssertRow(run.Output, "SetupMode", Regex.Escape("user mode (0)"));
        AssertRow(run.Output, "AuditMode", "absent");
        AssertRow(run.Output, "PK", "present, " + PkSize + " bytes");
        AssertRow(run.Output, "dbx", Regex.Escape("172 bytes, 1 signature list, 3 signatures (SHA256)"));
        AssertRow(run.Output, "dbt", "absent");
        AssertRow(run.Output, "Registry UEFISecureBootEnabled", "true");
        AssertRow(run.Output, "PowerShell Confirm-SecureBootUEFI", "true");
        AssertRow(run.Output, "Sources agree", "true");
    }

    [Theory]
    [InlineData(new byte[] { 1 }, "deployed mode (0)", "deployed mode (1)")]
    [InlineData(new byte[] { 0 }, "user mode (0)", "not deployed mode (0)")]
    public void SecureBoot_Text_WordsSetupModeWithTheModeItImplies(byte[] deployedMode, string setupModeRow, string deployedModeRow)
    {
        List<FirmwareVariable> variables = SampleVariables().ToList();
        variables.Add(new FirmwareVariable("DeployedMode", Global, BsRt, deployedMode));
        CliRun run = Run(Platform(store: new InMemoryFirmwareVariableStore(variables)), "secureboot");

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        AssertRow(run.Output, "SetupMode", Regex.Escape(setupModeRow));
        AssertRow(run.Output, "DeployedMode", Regex.Escape(deployedModeRow));
    }

    [Fact]
    public void SecureBoot_PolicyErrors_MakeItPartialAndWarn()
    {
        SecureBootPolicyResult policy = Policy() with { PowerShellConfirm = null, Errors = new[] { "Confirm-SecureBootUEFI timed out after 10 seconds" } };
        CliRun run = Run(Platform(policy: policy), "secureboot", "--json");

        Assert.Equal(ExitCodes.Partial, run.ExitCode);
        Assert.Equal("Warning: policy: Confirm-SecureBootUEFI timed out after 10 seconds" + "\n", run.Error);
        JsonElement document = ParseSingleDocument(run.Output);
        Assert.Equal(new[] { "policy: Confirm-SecureBootUEFI timed out after 10 seconds" }, document.GetProperty("warnings").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    // ---- baseline --------------------------------------------------------------------------

    [Fact]
    public void Baseline_WritesTheFiveItemsUnderOut()
    {
        string root = Path.Combine(_tempRoot, "baseline");
        CliRun run = Run("baseline", "--out", root);

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Equal(string.Empty, run.Error);

        string[] runFolders = Directory.GetDirectories(root);
        string directory = Assert.Single(runFolders);
        string runId = Path.GetFileName(directory);
        Assert.Matches(@"^\d{8}-\d{6}$", runId);
        AssertRow(run.Output, "Baseline directory", Regex.Escape(directory));
        AssertRow(run.Output, "Run id", runId);
        AssertRow(run.Output, "Variables enumerated", VariableCount.ToString(CultureInfo.InvariantCulture));
        AssertRow(run.Output, "Variables exported", VariableCount.ToString(CultureInfo.InvariantCulture));
        AssertRow(run.Output, "Files written", (VariableCount + 5).ToString(CultureInfo.InvariantCulture));
        AssertRow(run.Output, "Export errors", "0");

        string[] topLevel = Directory.GetFiles(directory).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray()!;
        Assert.Equal(new[] { "decoded.json", "dell-settings.json", "index.json", "log.txt", "platform.json" }, topLevel);
        Assert.Equal(new[] { "vars" }, Directory.GetDirectories(directory).Select(Path.GetFileName).ToArray());
        Assert.Equal(VariableCount, Directory.GetFiles(Path.Combine(directory, "vars"), "*.bin").Length);

        using JsonDocument index = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "index.json")));
        Assert.Equal(ToolVersion.Current, index.RootElement.GetProperty("ToolVersion").GetString());
        Assert.Equal("Uefi", index.RootElement.GetProperty("FirmwareType").GetString());
        Assert.Equal("Enabled", index.RootElement.GetProperty("PrivilegeState").GetString());
        Assert.Equal("OptiPlex 7020", index.RootElement.GetProperty("Identity").GetProperty("Model").GetString());
        Assert.Equal(VariableCount, index.RootElement.GetProperty("VariableCount").GetInt32());
        Assert.Equal(VariableCount, index.RootElement.GetProperty("Variables").GetArrayLength());

        using JsonDocument platform = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "platform.json")));
        Assert.Equal("PCR 7+11", platform.RootElement.GetProperty("BitLocker").GetProperty("Volumes")[0].GetProperty("PcrProfileText").GetString());
        Assert.Equal("DCIM_BIOSEnumeration", platform.RootElement.GetProperty("Dell").GetProperty("DcmClassesFound")[0].GetString());
        Assert.True(platform.RootElement.GetProperty("SecureBootPolicy").GetProperty("SourcesAgree").GetBoolean());

        string log = File.ReadAllText(Path.Combine(directory, "log.txt"));
        Assert.Contains("command baseline started", log, StringComparison.Ordinal);
        Assert.Contains(BaselineExporter.LogOutcomeHeader, log, StringComparison.Ordinal);
        Assert.Contains("Files written before log.txt: " + (VariableCount + 4), log, StringComparison.Ordinal);
    }

    [Fact]
    public void Baseline_Json_ListsTheFilesInWriteOrder()
    {
        string root = Path.Combine(_tempRoot, "baseline-json");
        CliRun run = Run("baseline", "--json", "--out", root);

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        JsonElement document = ParseSingleDocument(run.Output);
        Assert.Equal("baseline", document.GetProperty("command").GetString());
        Assert.True(document.GetProperty("ok").GetBoolean());
        Assert.StartsWith(Path.GetFullPath(root), document.GetProperty("directory").GetString(), StringComparison.Ordinal);
        Assert.Equal(VariableCount, document.GetProperty("variableCount").GetInt32());
        Assert.True(document.GetProperty("decodedCount").GetInt32() > 0);
        Assert.Empty(document.GetProperty("errors").EnumerateArray());

        string[] files = document.GetProperty("filesWritten").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(VariableCount + 5, files.Length);
        Assert.All(files.Take(VariableCount), file => Assert.StartsWith("vars/", file, StringComparison.Ordinal));
        Assert.Equal(new[] { "index.json", "decoded.json", "platform.json", "dell-settings.json", "log.txt" }, files.Skip(VariableCount).ToArray());
    }

    // ---- gate ------------------------------------------------------------------------------

    [Fact]
    public void Gate_WritesGateJsonWithEverySection()
    {
        string ifrFile = WriteVarstoreFile();
        string outFile = Path.Combine(_tempRoot, "reports", "gate.json");
        CliRun run = Run("gate", "--out", outFile, "--ifr-varstores", ifrFile);

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Equal("Warning: ifr-varstores: line 5: expected 'Name;GUID;Size' but found 1 field(s)" + "\n", run.Error);
        Assert.Contains("Gate report written to " + Path.GetFullPath(outFile), run.Output, StringComparison.Ordinal);
        AssertRow(run.Output, "(a) Firmware type", "Uefi");
        AssertRow(run.Output, "(a) Secure Boot enabled", "true");
        AssertRow(run.Output, "(b) Dell Command Monitor classes defined", "DCIM_BIOSEnumeration, DCIM_BIOSService");
        AssertRow(run.Output, "(b) Dell Command Monitor classes responding", "DCIM_BIOSEnumeration, DCIM_BIOSService");
        AssertRow(run.Output, "(c) Varstores in file", "3");
        AssertRow(run.Output, "(c) Present at runtime", "2");
        AssertRow(run.Output, "(c) Absent at runtime", "1");
        AssertRow(run.Output, "(c) Size mismatches", "1");
        AssertRow(run.Output, "(d) C:", Regex.Escape("Encrypted; protection On; protectors TPM; PCR 7+11"));
        AssertRow(run.Output, "Recommendation", Regex.Escape("(left for the memo author)"));

        using JsonDocument gate = JsonDocument.Parse(File.ReadAllText(outFile));
        JsonElement report = gate.RootElement;
        string[] expectedSections = { "toolVersion", "timestampUtc", "timestampLocal", "identity", "bootMode", "dellInterface", "varstoreMatch", "bitLocker", "secureBootPolicy", "tpm", "recommendation" };
        Assert.Equal(expectedSections, report.EnumerateObject().Select(p => p.Name).ToArray());

        Assert.Equal(ToolVersion.Current, report.GetProperty("toolVersion").GetString());
        Assert.True(report.GetProperty("identity").GetProperty("expectedModelConfirmed").GetBoolean());
        Assert.Equal("Uefi", report.GetProperty("bootMode").GetProperty("firmwareType").GetString());
        Assert.Equal("Enabled", report.GetProperty("bootMode").GetProperty("privilegeState").GetString());
        Assert.True(report.GetProperty("bootMode").GetProperty("secureBootCapable").GetBoolean());
        Assert.True(report.GetProperty("bootMode").GetProperty("secureBootEnabled").GetBoolean());
        Assert.Equal("InMemory", report.GetProperty("bootMode").GetProperty("enumerationMethod").GetString());
        Assert.Equal(VariableCount, report.GetProperty("bootMode").GetProperty("variableCount").GetInt32());
        Assert.True(report.GetProperty("dellInterface").GetProperty("dcmClassesPresent").GetBoolean());
        Assert.True(report.GetProperty("dellInterface").GetProperty("cctkSecureBootEnabled").GetBoolean());
        Assert.Equal("PCR 7+11", report.GetProperty("bitLocker").GetProperty("volumes")[0].GetProperty("pcrProfileText").GetString());
        Assert.True(report.GetProperty("secureBootPolicy").GetProperty("sourcesAgree").GetBoolean());
        Assert.Equal("1.2, 2, 3", report.GetProperty("tpm").GetProperty("specVersion").GetString());
        Assert.Equal(string.Empty, report.GetProperty("recommendation").GetString());

        JsonElement match = report.GetProperty("varstoreMatch");
        Assert.Equal(Path.GetFullPath(ifrFile), match.GetProperty("file").GetString());
        Assert.Equal(3, match.GetProperty("entryCount").GetInt32());
        Assert.Equal(2, match.GetProperty("presentCount").GetInt32());
        Assert.Equal(1, match.GetProperty("absentCount").GetInt32());
        Assert.Equal(1, match.GetProperty("sizeMismatchCount").GetInt32());
        JsonElement[] entries = match.GetProperty("entries").EnumerateArray().ToArray();
        Assert.Equal(new[] { "BootOrder", "Setup", "PK" }, entries.Select(e => e.GetProperty("name").GetString()).ToArray());
        Assert.True(entries[0].GetProperty("present").GetBoolean());
        Assert.True(entries[0].GetProperty("sizeMatches").GetBoolean());
        Assert.Equal("NV BS RT", entries[0].GetProperty("attributeFlags").GetString());
        Assert.False(entries[1].GetProperty("present").GetBoolean());
        Assert.Equal(JsonValueKind.Null, entries[1].GetProperty("sizeMatches").ValueKind);
        Assert.True(entries[2].GetProperty("present").GetBoolean());
        Assert.False(entries[2].GetProperty("sizeMatches").GetBoolean());
        Assert.Equal(PkSize, entries[2].GetProperty("runtimeSize").GetInt32());
        Assert.Single(match.GetProperty("parseErrors").EnumerateArray());
    }

    [Fact]
    public void Gate_Json_PrintsTheSameDocumentItWrites()
    {
        string outFile = Path.Combine(_tempRoot, "gate.json");
        CliRun run = Run("gate", "--json", "--out", outFile);

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Equal(string.Empty, run.Error);
        JsonElement document = ParseSingleDocument(run.Output);
        Assert.Equal(JsonValueKind.Null, document.GetProperty("varstoreMatch").ValueKind);
        Assert.Equal(File.ReadAllText(outFile).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd(), run.Output.TrimEnd());
    }

    [Fact]
    public void Gate_MissingVarstoreFile_IsAUsageError()
    {
        string missing = Path.Combine(_tempRoot, "absent.txt");
        CliRun run = Run("gate", "--ifr-varstores", missing, "--out", Path.Combine(_tempRoot, "gate.json"));

        Assert.Equal(ExitCodes.Usage, run.ExitCode);
        Assert.Contains("does not exist", run.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_tempRoot, "gate.json")));
    }

    // ---- preconditions and usage -------------------------------------------------------------

    [Theory]
    [InlineData(FirmwareType.LegacyBios)]
    [InlineData(FirmwareType.Unknown)]
    public void NotUefi_PrintsTheSentenceAndExitsThree(FirmwareType firmwareType)
    {
        CliRun run = Run(Platform(firmwareType: firmwareType), "list");

        Assert.Equal(ExitCodes.NotUefi, run.ExitCode);
        Assert.Equal(Preconditions.NotUefiSentence + "\n", run.Error);
        Assert.Equal(string.Empty, run.Output);
    }

    [Fact]
    public void NotUefi_Json_WritesTheErrorDocument()
    {
        CliRun run = Run(Platform(firmwareType: FirmwareType.LegacyBios), "boot", "--json");

        Assert.Equal(ExitCodes.NotUefi, run.ExitCode);
        JsonElement document = ParseSingleDocument(run.Output);
        Assert.Equal("boot", document.GetProperty("command").GetString());
        Assert.False(document.GetProperty("ok").GetBoolean());
        Assert.Equal(ExitCodes.NotUefi, document.GetProperty("exitCode").GetInt32());
        Assert.Equal(Preconditions.NotUefiSentence, document.GetProperty("error").GetString());
        Assert.Equal("LegacyBios", document.GetProperty("firmwareType").GetString());
        Assert.True(document.GetProperty("elevated").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("privilegeState").ValueKind);
    }

    [Fact]
    public void NotElevated_ExitsFour()
    {
        CliRun run = Run(Platform(elevated: false, privilege: PrivilegeState.NotElevated), "secureboot");

        Assert.Equal(ExitCodes.NotElevated, run.ExitCode);
        Assert.Equal(Preconditions.NotElevatedSentence + "\n", run.Error);
        Assert.Equal(string.Empty, run.Output);
    }

    [Fact]
    public void PrivilegeAbsent_ExitsFourWithTheElevatedSentence()
    {
        CliRun run = Run(Platform(privilege: PrivilegeState.ElevatedPrivilegeAbsent), "info");

        Assert.Equal(ExitCodes.NotElevated, run.ExitCode);
        Assert.Contains("elevated but SeSystemEnvironmentPrivilege could not be enabled", run.Error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, run.Output);
    }

    [Fact]
    public void UnknownCommand_IsAUsageError()
    {
        CliRun run = Run("bogus");

        Assert.Equal(ExitCodes.Usage, run.ExitCode);
        Assert.Contains("Run 'uefiaccess --help' for usage.", run.Error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, run.Output);
    }

    [Fact]
    public void Help_ListsEveryCommand()
    {
        CliRun run = Run("--help");

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        foreach (string command in new[] { "info", "list", "read", "boot", "secureboot", "dell", "baseline", "gate" })
        {
            Assert.Matches(@"(?m)^\s+" + command + @"\b", run.Output);
        }
    }

    [Fact]
    public void EnumerationFailure_ExitsFive()
    {
        FailingStore store = new();
        CliRun run = Run(Platform(store: store), "list", "--json");

        Assert.Equal(ExitCodes.NativeFailure, run.ExitCode);
        Assert.Contains("No variables could be enumerated: NtEnumerateSystemEnvironmentValuesEx failed", run.Error, StringComparison.Ordinal);
        JsonElement document = ParseSingleDocument(run.Output);
        Assert.Equal(ExitCodes.NativeFailure, document.GetProperty("exitCode").GetInt32());
    }

    // ---- harness -----------------------------------------------------------------------------

    /// <summary>Captured outcome of one command line; line endings are normalised to "\n".</summary>
    /// <param name="ExitCode">The exit code Program.Run returned.</param>
    /// <param name="Output">Standard output.</param>
    /// <param name="Error">Standard error.</param>
    private sealed record CliRun(int ExitCode, string Output, string Error);

    private static CliRun Run(params string[] args)
    {
        return Run(Platform(), args);
    }

    private static CliRun Run(CliPlatform platform, params string[] args)
    {
        using StringWriter output = new(CultureInfo.InvariantCulture);
        using StringWriter error = new(CultureInfo.InvariantCulture);
        int exitCode = Program.Run(
            args,
            verbose => CliServices.Create(verbose, _ => platform, output, error, Serilog.Core.Logger.None),
            output,
            error);
        return new CliRun(exitCode, Normalize(output.ToString()), Normalize(error.ToString()));
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>The output split into lines without the trailing line break's empty element.</summary>
    private static string[] Lines(string text)
    {
        string[] lines = text.Split('\n').Select(line => line.TrimEnd()).ToArray();
        return lines.Length > 0 && lines[^1].Length == 0 ? lines[..^1] : lines;
    }

    /// <summary>Parses standard output as exactly one JSON document; trailing content fails the parse.</summary>
    private static JsonElement ParseSingleDocument(string text)
    {
        Assert.False(string.IsNullOrWhiteSpace(text), "expected one JSON document on standard output");
        using JsonDocument document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    /// <summary>Asserts a two-column table row "field   value" (value given as a regular expression).</summary>
    private static void AssertRow(string output, string field, string valuePattern)
    {
        Assert.Matches("(?m)^" + Regex.Escape(field) + " +" + valuePattern + "$", output);
    }

    /// <summary>Asserts that every pattern matches one line and that the matches appear in the given order.</summary>
    private static void AssertInOrder(string output, params string[] linePatterns)
    {
        int last = -1;
        foreach (string pattern in linePatterns)
        {
            Match match = Regex.Match(output, "(?m)" + pattern);
            Assert.True(match.Success, "expected a line matching " + pattern);
            Assert.True(match.Index > last, "line matching " + pattern + " appeared out of order");
            last = match.Index;
        }
    }

    private string WriteVarstoreFile()
    {
        string path = Path.Combine(_tempRoot, "ifr-varstores.txt");
        File.WriteAllLines(path, new[]
        {
            "# varstores extracted from the A18 image",
            "BootOrder;8BE4DF61-93CA-11D2-AA0D-00E098032B8C;10",
            "Setup;{12345678-90AB-CDEF-1234-567890ABCDEF};0x2E5",
            "PK;8be4df61-93ca-11d2-aa0d-00e098032b8c;100",
            "a line without separators",
        });
        return path;
    }

    // ---- fixtures ----------------------------------------------------------------------------

    private static byte[] UInt16Array(params ushort[] values)
    {
        byte[] bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2, 2), values[i]);
        }

        return bytes;
    }

    private static IReadOnlyList<FirmwareVariable> SampleVariables()
    {
        return new[]
        {
            new FirmwareVariable("Boot0000", Global, NvBsRt, LoadOptionFixtures.WindowsBootManager()),
            new FirmwareVariable("Boot0001", Global, NvBsRt, LoadOptionFixtures.UsbStick()),
            new FirmwareVariable("Boot0002", Global, NvBsRt, LoadOptionFixtures.LegacyBbs()),
            new FirmwareVariable("Boot0003", Global, NvBsRt, LoadOptionFixtures.Pxe()),
            new FirmwareVariable("BootOrder", Global, NvBsRt, BootOrderData),
            new FirmwareVariable("BootCurrent", Global, BsRt, UInt16Array(0x0000)),
            new FirmwareVariable("Timeout", Global, NvBsRt, UInt16Array(2)),
            new FirmwareVariable("SecureBoot", Global, BsRt, new byte[] { 1 }),
            new FirmwareVariable("SetupMode", Global, BsRt, new byte[] { 0 }),
            new FirmwareVariable("PK", Global, NvBsRtTa, SignatureListFixtures.X509List(600)),
            new FirmwareVariable("KEK", Global, NvBsRtTa, SignatureListFixtures.X509List(900)),
            new FirmwareVariable("db", ImageSecurity, NvBsRtTa, SignatureListFixtures.X509List(700)),
            new FirmwareVariable("dbx", ImageSecurity, NvBsRtTa, SignatureListFixtures.Sha256List(3)),
        };
    }

    private static CliPlatform Platform(
        IFirmwareVariableStore? store = null,
        FirmwareType firmwareType = FirmwareType.Uefi,
        bool elevated = true,
        PrivilegeState privilege = PrivilegeState.Enabled,
        MachineIdentityResult? identity = null,
        SecureBootPolicyResult? policy = null)
    {
        SecureBootPolicyResult policyResult = policy ?? Policy();
        return new CliPlatform(
            new FakePrivilegeManager(elevated, privilege),
            new FakeFirmwareTypeDetector(firmwareType),
            store ?? new InMemoryFirmwareVariableStore(SampleVariables()),
            new FixedInspector<MachineIdentityResult>(identity ?? Identity()),
            new FixedInspector<TpmInfoResult>(new TpmInfoResult(true, "1.2, 2, 3", true, true, Array.Empty<string>())),
            new FixedInspector<BitLockerResult>(BitLocker()),
            new FixedInspector<DellInterfaceProbeResult>(Dell()),
            _ => new FixedInspector<SecureBootPolicyResult>(policyResult),
            null,
            null,
            new CliTestHarness.ScriptedDellSettingsReader(CliTestHarness.DellCompleteA18));
    }

    private static MachineIdentityResult Identity()
    {
        return new MachineIdentityResult(
            "Dell Inc.",
            "OptiPlex 7020",
            "OptiPlex 7020",
            "A18",
            "2019-04-05",
            "ABC1234",
            "0N4YC8",
            "Intel(R) Core(TM) i5-4590 CPU @ 3.30GHz",
            "Microsoft Windows 11 Pro",
            "10.0.22631",
            "22631",
            true,
            "Expected model confirmed: 2014 Dell OptiPlex 7020 (model, BIOS version and CPU generation all match).",
            Array.Empty<string>());
    }

    private static SecureBootPolicyResult Policy()
    {
        return new SecureBootPolicyResult(true, true, false, null, null, true, true, true, Array.Empty<string>());
    }

    private static BitLockerResult BitLocker()
    {
        BitLockerVolume volume = new("C:", 1, "On", 1, "Encrypted", new[] { "TPM" }, new[] { 7, 11 }, "PCR 7+11");
        return new BitLockerResult(new[] { volume }, Array.Empty<string>());
    }

    private static DellInterfaceProbeResult Dell()
    {
        return new DellInterfaceProbeResult(
            true,
            new[] { "DCIM_BIOSEnumeration", "DCIM_BIOSService" },
            new[] { new DellAttributeSample("Secure Boot", "2", "Enabled", new[] { "1", "2" }, new[] { "Disabled", "Enabled" }, "DCM") },
            true,
            @"C:\Program Files (x86)\Dell\Command Configure\X86_64\cctk.exe",
            "Dell Command | Configure Version 4.8.0",
            "secureboot=enable",
            false,
            false,
            Array.Empty<string>())
        {
            DcimClassCount = 4,
            CctkSecureBootEnabled = true,
            DcmClassesResponding = new[] { "DCIM_BIOSEnumeration", "DCIM_BIOSService" },
        };
    }

    // ---- fakes -------------------------------------------------------------------------------

    private sealed class FakePrivilegeManager : IPrivilegeManager
    {
        private readonly PrivilegeState _state;

        public FakePrivilegeManager(bool elevated, PrivilegeState state)
        {
            IsElevated = elevated;
            _state = state;
        }

        public bool IsElevated { get; }

        public PrivilegeState EnableSystemEnvironmentPrivilege() => _state;
    }

    private sealed class FakeFirmwareTypeDetector : IFirmwareTypeDetector
    {
        private readonly FirmwareType _type;

        public FakeFirmwareTypeDetector(FirmwareType type)
        {
            _type = type;
        }

        public FirmwareType Detect() => _type;
    }

    /// <summary>An inspector that returns a fixed record and never touches WMI, the registry or a process.</summary>
    private sealed class FixedInspector<T> : IPlatformInspector<T>
    {
        private readonly T _result;

        public FixedInspector(T result)
        {
            _result = result;
        }

        public T Inspect() => _result;
    }

    /// <summary>A store whose enumeration fails outright, as it does when the native call is refused.</summary>
    private sealed class FailingStore : IFirmwareVariableStore
    {
        public EnumerationResult Enumerate()
        {
            return new EnumerationResult(Array.Empty<FirmwareVariable>(), EnumerationMethod.None, 0, "NtEnumerateSystemEnvironmentValuesEx failed: STATUS_INVALID_PARAMETER (0xC000000D)", Array.Empty<string>());
        }

        public ReadResult Read(string name, Guid vendorGuid)
        {
            return new ReadResult(null, ReadStatus.Failed, "the store is failing", 0);
        }
    }
}
