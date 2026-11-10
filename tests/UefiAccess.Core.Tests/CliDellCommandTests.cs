// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using System.Text.RegularExpressions;
using UefiAccess.Cli;
using UefiAccess.Cli.Commands;
using UefiAccess.Cli.Services;
using UefiAccess.Core.Baseline;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Privileges;
using UefiAccess.Core.Settings.Dell;
using UefiAccess.Core.Tests.Fixtures;
using UefiAccess.Core.Variables;
using Xunit;
using static UefiAccess.Core.Tests.CliTestHarness;

namespace UefiAccess.Core.Tests;

/// <summary>
/// "uefiaccess dell" and the Dell settings of "uefiaccess baseline", through the real command classes (Program.Run, System.CommandLine, CommandRunner,
/// Preconditions and the command bodies) over a <see cref="CliTestHarness.ScriptedDellSettingsReader"/>
/// that answers what each test scripts and records every read's options: the text table and the
/// one JSON document, the exit codes 0, 6, 5, 2 and 4, the fallback and its warning, the full
/// enumeration, the source option's mapping to the reader's preference, a reader that throws or is
/// missing, and the baseline's dell-settings.json, read through Dell Command | Monitor only with its
/// problems as warnings. The scripted reads are made by the Core provider's and reader's own pure
/// functions over the A18 fixture rows. No test reads Dell Command | Monitor or starts Dell Command
/// | Configure on this PC (DellSettingsGuardTests.TestsNeverRunTheRealDellReader).
/// </summary>
public sealed class CliDellCommandTests : IDisposable
{
    private const VariableAttributes BsRt = VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess;

    /// <summary>The problem sentence on a computer without the Dell tools.</summary>
    private const string NothingInstalledSentence =
        "Dell BIOS settings cannot be read. Dell Command Monitor is not installed. Dell Command Configure was not found. "
        + "Install the Microsoft Visual C++ 2015-2022 x64 runtime, then Dell Command Monitor, and try again; the Dell interface setup page of the documentation has the steps.";

    /// <summary>The problem sentence of a reader that failed unexpectedly.</summary>
    private const string UnexpectedSentence =
        "Dell BIOS settings cannot be read. An unexpected error occurred; the log has the details. Try again; if it fails again, restart Windows and try again.";

    /// <summary>The ten agreed settings' ids, in the order of the list.</summary>
    private static readonly string[] Ids =
    {
        "BootListOption", "SecureBoot", "SataOperation", "TpmSecurity", "TpmActivation", "Virtualization", "VtForDirectIo", "IntegratedNic", "WakeOnLan", "NumLock",
    };

    private readonly string _tempRoot;

    public CliDellCommandTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "cli-dell-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        TestFolders.DeleteQuietly(_tempRoot);
    }

    // ---- dell ------------------------------------------------------------------------------

    [Fact]
    public void Dell_Json_IsOneDocumentWithTenSettings()
    {
        ScriptedDellSettingsReader reader = new(DellCompleteA18);
        CliRun run = Run(Platform(reader), "dell", "--json");

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Equal(string.Empty, run.Error);
        JsonElement document = ParseSingleDocument(run.Output);
        Assert.Equal(
            new[] { "command", "ok", "outcome", "source", "readAt", "elapsedMs", "machine", "checkedAgainstBios", "listStatus", "nativeInterfacePresent", "settings", "allAttributes", "fallback", "problems", "warnings", "sentence" },
            document.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("dell", document.GetProperty("command").GetString());
        Assert.True(document.GetProperty("ok").GetBoolean());
        Assert.Equal("Complete", document.GetProperty("outcome").GetString());
        Assert.Equal("DellCommandMonitor", document.GetProperty("source").GetString());
        Assert.Equal("2026-10-05T09:12:44+02:00", document.GetProperty("readAt").GetString());
        Assert.Equal(1180, document.GetProperty("elapsedMs").GetInt64());
        Assert.Equal("Dell Inc.", document.GetProperty("machine").GetProperty("manufacturer").GetString());
        Assert.Equal("OptiPlex 7020", document.GetProperty("machine").GetProperty("model").GetString());
        Assert.Equal("A18", document.GetProperty("machine").GetProperty("biosVersion").GetString());
        Assert.Equal(new[] { "manufacturer", "model", "biosVersion" }, document.GetProperty("machine").EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("A18", document.GetProperty("checkedAgainstBios").GetString());
        Assert.Equal(DellAttributeCatalog.ListStatus, document.GetProperty("listStatus").GetString());
        Assert.False(document.GetProperty("nativeInterfacePresent").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("allAttributes").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.GetProperty("fallback").ValueKind);
        Assert.Empty(document.GetProperty("problems").EnumerateArray());
        Assert.Empty(document.GetProperty("warnings").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("sentence").ValueKind);

        JsonElement[] settings = document.GetProperty("settings").EnumerateArray().ToArray();
        Assert.Equal(Ids, settings.Select(setting => setting.GetProperty("id").GetString()).ToArray());
        Assert.Equal(DcmRowFixtures.UnitA18Texts, settings.Select(setting => setting.GetProperty("text").GetString()).ToArray());

        // The design's example row, whole.
        JsonElement numLock = settings[9];
        Assert.Equal(
            new[] { "id", "name", "displayName", "category", "setupLocation", "state", "current", "text", "possibleValues", "possibleValuesOrigin", "vendorMarksReadOnly", "displayFromVendor", "note", "sentence" },
            numLock.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("Num Lock", numLock.GetProperty("name").GetString());
        Assert.Equal("Num Lock at boot", numLock.GetProperty("displayName").GetString());
        Assert.Equal("POST Behavior", numLock.GetProperty("category").GetString());
        Assert.Equal("POST Behavior, Numlock LED", numLock.GetProperty("setupLocation").GetString());
        Assert.Equal("Read", numLock.GetProperty("state").GetString());
        Assert.Equal(("2", "Enable", "Enabled"), Value(numLock.GetProperty("current")));
        (string?, string?, string?)[] possible = numLock.GetProperty("possibleValues").EnumerateArray().Select(Value).ToArray();
        Assert.Equal(2, possible.Length);
        Assert.Equal(("1", "Disable", "Disabled"), possible[0]);
        Assert.Equal(("2", "Enable", "Enabled"), possible[1]);
        Assert.Equal("Interface", numLock.GetProperty("possibleValuesOrigin").GetString());
        Assert.False(numLock.GetProperty("vendorMarksReadOnly").GetBoolean());
        Assert.False(numLock.GetProperty("displayFromVendor").GetBoolean());
        Assert.Equal(JsonValueKind.Null, numLock.GetProperty("note").ValueKind);
        Assert.Equal("Num Lock at boot: Enabled. Possible values: Disabled, Enabled.", numLock.GetProperty("sentence").GetString());

        Assert.Equal(DellSentences.SecureBootDcmNote, settings[1].GetProperty("note").GetString());
        Assert.Equal(new DellReadOptions(DellSourcePreference.Automatic, false), Assert.Single(reader.Reads));
    }

    [Fact]
    public void Dell_Text_IsAPlainTableWithNoBoxDrawing()
    {
        CliRun run = Run(Platform(new ScriptedDellSettingsReader(DellCompleteA18)), "dell");

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Equal(string.Empty, run.Error);
        Assert.Equal(
            "Field   Value\n"
            + "Source  Dell Command Monitor\n"
            + "Loaded  2026-10-05 09:12:44 +02:00 (1180 ms)\n"
            + "BIOS    A18 (names and values checked against A18)\n"
            + "Changes none: this tool shows Dell BIOS settings and changes none of them\n"
            + "\n"
            + "Setting           Current value Possible values\n"
            + "Boot list option  UEFI          Legacy, UEFI\n"
            + "Secure Boot       Enabled       Enabled\n"
            + "SATA operation    AHCI          Disabled, ATA, AHCI\n"
            + "TPM security      Enabled       Enabled, Disabled\n"
            + "TPM activation    Activated     Deactivated, Activated\n"
            + "Virtualization    Enabled       Disabled, Enabled\n"
            + "VT for Direct I/O Enabled       Disabled, Enabled\n"
            + "Integrated NIC    Enabled       Disabled, Enabled with PXE, Enabled, Enabled with image server\n"
            + "Wake on LAN       Disabled      Disabled, LAN Only, LAN with PXE Boot\n"
            + "Num Lock at boot  Enabled       Disabled, Enabled\n"
            + "\n"
            + "Note: Secure Boot: Dell Command Monitor lists only Enabled; the BIOS setup also offers Disabled.\n",
            run.Output);

        // Plain text for a screen reader: no box drawing, no "|" (Dell's own spelling of its tool
        // names), no tab, no code, and no table of every attribute without --all.
        Assert.DoesNotContain(run.Output, character => (character >= (char)0x2500 && character <= (char)0x257F) || character is '|' or '\t');
        Assert.DoesNotContain("Attribute", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Dell_Complete_Exits0()
    {
        Assert.Equal(ExitCodes.Success, Run(Platform(new ScriptedDellSettingsReader(DellCompleteA18)), "dell").ExitCode);
        Assert.Equal(ExitCodes.Success, Run(Platform(new ScriptedDellSettingsReader(DellCompleteA18)), "dell", "--json").ExitCode);
    }

    [Fact]
    public void Dell_Partial_Exits6()
    {
        ScriptedDellSettingsReader reader = new(options => DellFromDcm(DcmRowFixtures.WithoutNumLock(), options));

        CliRun text = Run(Platform(reader), "dell");
        CliRun json = Run(Platform(reader), "dell", "--json");

        Assert.Equal(ExitCodes.Partial, text.ExitCode);
        Assert.Equal(string.Empty, text.Error);
        Assert.Contains("\nNum Lock at boot  not reported\n", text.Output, StringComparison.Ordinal);
        Assert.Equal(ExitCodes.Partial, json.ExitCode);
        JsonElement document = ParseSingleDocument(json.Output);
        Assert.True(document.GetProperty("ok").GetBoolean());
        Assert.Equal("Partial", document.GetProperty("outcome").GetString());
        JsonElement numLock = document.GetProperty("settings")[9];
        Assert.Equal("NotReported", numLock.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, numLock.GetProperty("current").ValueKind);
        Assert.Equal("not reported", numLock.GetProperty("text").GetString());
        Assert.Equal("Num Lock at boot: not reported.", numLock.GetProperty("sentence").GetString());
    }

    [Fact]
    public void Dell_Fallback_Exits6_WithTheFallbackWarningOnStandardError()
    {
        const string Warning = "Warning: Dell Command Monitor is not installed. Dell Command Configure was used instead.\n";
        CliRun text = Run(Platform(new ScriptedDellSettingsReader(_ => DellFallback())), "dell");
        CliRun json = Run(Platform(new ScriptedDellSettingsReader(_ => DellFallback())), "dell", "--json");

        Assert.Equal(ExitCodes.Partial, text.ExitCode);
        Assert.Equal(Warning, text.Error);
        Assert.Contains("\nSource  Dell Command Configure\n", text.Output, StringComparison.Ordinal);
        Assert.Contains("\nBoot list option  not reported\n", text.Output, StringComparison.Ordinal);
        Assert.Contains("\nTPM activation    Activated     Deactivated, Activated\n", text.Output, StringComparison.Ordinal);
        Assert.EndsWith("\nNote: Boot list option: " + DellSentences.NotReadByFallbackNote + "\n", text.Output, StringComparison.Ordinal);

        Assert.Equal(ExitCodes.Partial, json.ExitCode);
        Assert.Equal(Warning, json.Error);
        JsonElement document = ParseSingleDocument(json.Output);
        Assert.True(document.GetProperty("ok").GetBoolean());
        Assert.Equal("Partial", document.GetProperty("outcome").GetString());
        Assert.Equal("DellCommandConfigure", document.GetProperty("source").GetString());
        Assert.Equal("Dell Command Monitor is not installed. Dell Command Configure was used instead.", document.GetProperty("fallback").GetString());
        JsonElement problem = Assert.Single(document.GetProperty("problems").EnumerateArray());
        Assert.Equal("DellCommandMonitor", problem.GetProperty("interface").GetString());
        Assert.Equal("NotInstalled", problem.GetProperty("kind").GetString());
        Assert.Equal(new[] { "interface", "kind", "detail" }, problem.EnumerateObject().Select(property => property.Name).ToArray());
        JsonElement sata = document.GetProperty("settings")[2];
        Assert.Equal(("Ahci", JsonValueKind.Null, "AHCI"), (sata.GetProperty("current").GetProperty("token").GetString(), sata.GetProperty("current").GetProperty("vendorName").ValueKind, sata.GetProperty("text").GetString()));
        Assert.Equal("Catalog", sata.GetProperty("possibleValuesOrigin").GetString());
        Assert.Equal(JsonValueKind.Null, sata.GetProperty("vendorMarksReadOnly").ValueKind);
    }

    [Fact]
    public void Dell_Unavailable_Exits5_WithTheSentenceOnStandardError_AndOkFalse()
    {
        CliRun text = Run(Platform(), "dell");
        CliRun json = Run(Platform(), "dell", "--json");

        Assert.Equal(ExitCodes.NativeFailure, text.ExitCode);
        Assert.Equal(string.Empty, text.Output);
        Assert.Equal(NothingInstalledSentence + "\n", text.Error);

        Assert.Equal(ExitCodes.NativeFailure, json.ExitCode);
        Assert.Equal(NothingInstalledSentence + "\n", json.Error);
        JsonElement document = ParseSingleDocument(json.Output);
        Assert.Equal("dell", document.GetProperty("command").GetString());
        Assert.False(document.GetProperty("ok").GetBoolean());
        Assert.Equal("Unavailable", document.GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("source").ValueKind);
        Assert.Equal(NothingInstalledSentence, document.GetProperty("sentence").GetString());
        Assert.Equal(new[] { "NotInstalled", "ProgramNotFound" }, document.GetProperty("problems").EnumerateArray().Select(problem => problem.GetProperty("kind").GetString()).ToArray());
        JsonElement[] settings = document.GetProperty("settings").EnumerateArray().ToArray();
        Assert.Equal(Ids, settings.Select(setting => setting.GetProperty("id").GetString()).ToArray());
        Assert.All(settings, setting => Assert.Equal(("NotRead", "not read"), (setting.GetProperty("state").GetString(), setting.GetProperty("text").GetString())));
        Assert.False(document.TryGetProperty("exitCode", out _), "the dell document, not the error document");
    }

    [Theory]
    [InlineData(null, false, DellSourcePreference.Automatic)]
    [InlineData("auto", false, DellSourcePreference.Automatic)]
    [InlineData("dcm", false, DellSourcePreference.DellCommandMonitor)]
    [InlineData("cctk", false, DellSourcePreference.DellCommandConfigure)]
    [InlineData("auto", true, DellSourcePreference.Automatic)]
    [InlineData("dcm", true, DellSourcePreference.DellCommandMonitor)]
    [InlineData(null, true, DellSourcePreference.Automatic)]
    public void Dell_SourceOption_MapsToThePreference(string? source, bool all, DellSourcePreference expected)
    {
        ScriptedDellSettingsReader reader = new(DellCompleteA18);
        List<string> args = new() { "dell", "--json" };
        if (source is not null)
        {
            args.AddRange(new[] { "--source", source });
        }

        if (all)
        {
            args.Add("--all");
        }

        CliRun run = Run(Platform(reader), args.ToArray());

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Equal(new DellReadOptions(expected, all), Assert.Single(reader.Reads));
        Assert.Equal(expected, DellCommand.PreferenceOf(source));
    }

    [Theory]
    [InlineData("wmi")]
    [InlineData("CCTK")]
    [InlineData("")]
    public void Dell_UnknownSource_IsAUsageError(string source)
    {
        ScriptedDellSettingsReader reader = new(DellCompleteA18);

        CliRun text = Run(Platform(reader), "dell", "--source", source);
        CliRun json = Run(Platform(reader), "dell", "--source", source, "--json");

        Assert.Equal(ExitCodes.Usage, text.ExitCode);
        Assert.Equal(string.Empty, text.Output);
        Assert.EndsWith("Run 'uefiaccess --help' for usage.\n", text.Error, StringComparison.Ordinal);
        Assert.Equal(ExitCodes.Usage, json.ExitCode);
        JsonElement document = ParseSingleDocument(json.Output);
        Assert.Equal("dell", document.GetProperty("command").GetString());
        Assert.Equal(ExitCodes.Usage, document.GetProperty("exitCode").GetInt32());
        Assert.Empty(reader.Reads);
        Assert.Null(DellCommand.PreferenceOf(source));
    }

    [Fact]
    public void Dell_AllWithCctk_IsAUsageError()
    {
        ScriptedDellSettingsReader reader = new(DellCompleteA18);

        CliRun text = Run(Platform(reader), "dell", "--source", "cctk", "--all");
        CliRun json = Run(Platform(reader), "dell", "--all", "--json", "--source", "cctk");

        Assert.Equal(ExitCodes.Usage, text.ExitCode);
        Assert.Equal(string.Empty, text.Output);
        Assert.Equal(DellCommand.AllNeedsDcmMessage + "\nRun 'uefiaccess --help' for usage.\n", text.Error);
        Assert.Equal("The --all option needs Dell Command Monitor; leave out --source or choose dcm.", DellCommand.AllNeedsDcmMessage);
        Assert.Equal(ExitCodes.Usage, json.ExitCode);
        JsonElement document = ParseSingleDocument(json.Output);
        Assert.Equal("dell", document.GetProperty("command").GetString());
        Assert.Equal(DellCommand.AllNeedsDcmMessage, document.GetProperty("error").GetString());
        Assert.Empty(reader.Reads);
    }

    [Fact]
    public void Dell_All_AddsTheAttributeTable()
    {
        ScriptedDellSettingsReader reader = new(DellCompleteA18);

        CliRun text = Run(Platform(reader), "dell", "--all");
        CliRun json = Run(Platform(reader), "dell", "--source", "dcm", "--all", "--json");

        Assert.Equal(ExitCodes.Success, text.ExitCode);
        Assert.Equal(string.Empty, text.Error);
        string[] lines = text.Output.Split('\n');
        int header = Array.FindIndex(lines, line => line.StartsWith("Attribute ", StringComparison.Ordinal));
        Assert.True(header > 0, "the table of every attribute is missing");
        Assert.Matches("^Attribute +Current +Possible +Dell read-only flag$", lines[header]);
        Assert.Equal(string.Empty, lines[header - 1]);
        string[] rows = lines.Skip(header + 1).Where(line => line.Length > 0).ToArray();
        Assert.Equal(72, rows.Length);
        Assert.Matches(@"^AC Power Recovery Mode +1=Off +1=Off, 2=Last, 3=On +false$", rows[0]);
        Assert.Contains(rows, row => Regex.IsMatch(row, @"^Computrace +1=Disable +1=Disable, 2=Enable +true$"));
        Assert.Contains(rows, row => Regex.IsMatch(row, @"^Secure Boot +2=Enable +2=Enable +false$"));
        Assert.Contains(rows, row => Regex.IsMatch(row, @"^Auto On Hour +0 +0-23=0-23 +false$"));
        Assert.True(Array.IndexOf(lines, "Note: Secure Boot: " + DellSentences.SecureBootDcmNote) < header, "the notes belong to the settings table, before the table of every attribute");

        Assert.Equal(ExitCodes.Success, json.ExitCode);
        JsonElement[] attributes = ParseSingleDocument(json.Output).GetProperty("allAttributes").EnumerateArray().ToArray();
        Assert.Equal(72, attributes.Length);
        Assert.Equal(
            new[] { "name", "currentValues", "currentNames", "possibleValues", "possibleNames", "isReadOnly" },
            attributes[0].EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("AC Power Recovery Mode", attributes[0].GetProperty("name").GetString());
        Assert.Equal(new[] { "1", "2", "3" }, attributes[0].GetProperty("possibleValues").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(new[] { "Off", "Last", "On" }, attributes[0].GetProperty("possibleNames").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.All(reader.Reads, options => Assert.True(options.IncludeAllAttributes));
    }

    [Fact]
    public void Dell_NotElevated_Exits4BeforeAnyRead()
    {
        ScriptedDellSettingsReader reader = new(DellCompleteA18);
        CliPlatform elevated = Platform(reader);
        CliPlatform platform = new(
            new NotElevatedPrivilegeManager(),
            elevated.FirmwareTypeDetector,
            elevated.Store,
            elevated.Identity,
            elevated.Tpm,
            elevated.BitLocker,
            elevated.Dell,
            elevated.SecureBootPolicy,
            null,
            null,
            reader);

        CliRun text = Run(platform, "dell");
        CliRun json = Run(platform, "dell", "--json");

        Assert.Equal(ExitCodes.NotElevated, text.ExitCode);
        Assert.Equal(Preconditions.NotElevatedSentence + "\n", text.Error);
        Assert.Equal(string.Empty, text.Output);
        Assert.Equal(ExitCodes.NotElevated, json.ExitCode);
        JsonElement document = ParseSingleDocument(json.Output);
        Assert.Equal("dell", document.GetProperty("command").GetString());
        Assert.Equal("NotElevated", document.GetProperty("privilegeState").GetString());
        Assert.Empty(reader.Reads);
    }

    [Fact]
    public void Dell_ReaderThrows_Exits5()
    {
        ScriptedDellSettingsReader reader = new(_ => throw new InvalidOperationException("the reader broke its contract"));

        CliRun text = Run(Platform(reader), "dell");
        CliRun json = Run(Platform(reader), "dell", "--json");

        Assert.Equal(ExitCodes.NativeFailure, text.ExitCode);
        Assert.Equal(string.Empty, text.Output);
        Assert.Equal(UnexpectedSentence + "\n", text.Error);
        Assert.Equal(ExitCodes.NativeFailure, json.ExitCode);
        JsonElement document = ParseSingleDocument(json.Output);
        Assert.False(document.GetProperty("ok").GetBoolean());
        Assert.Equal(UnexpectedSentence, document.GetProperty("sentence").GetString());
        JsonElement problem = Assert.Single(document.GetProperty("problems").EnumerateArray());
        Assert.Equal(("Reader", "Unexpected"), (problem.GetProperty("interface").GetString(), problem.GetProperty("kind").GetString()));
        Assert.Contains("the reader broke its contract", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal(10, document.GetProperty("settings").GetArrayLength());
    }

    [Fact]
    public void Dell_WithoutAReader_Exits5()
    {
        CliPlatform composed = Platform();
        CliPlatform platform = new(
            composed.PrivilegeManager,
            composed.FirmwareTypeDetector,
            composed.Store,
            composed.Identity,
            composed.Tpm,
            composed.BitLocker,
            composed.Dell,
            composed.SecureBootPolicy);
        Assert.Null(platform.DellSettings);

        CliRun text = Run(platform, "dell");
        CliRun json = Run(platform, "dell", "--json");

        Assert.Equal(ExitCodes.NativeFailure, text.ExitCode);
        Assert.Equal(CliPlatform.NoDellSettingsReaderMessage + "\n", text.Error);
        Assert.Equal(string.Empty, text.Output);
        Assert.Equal(ExitCodes.NativeFailure, json.ExitCode);
        JsonElement document = ParseSingleDocument(json.Output);
        Assert.Equal(ExitCodes.NativeFailure, document.GetProperty("exitCode").GetInt32());
        Assert.Equal(CliPlatform.NoDellSettingsReaderMessage, document.GetProperty("error").GetString());
    }

    // ---- baseline --------------------------------------------------------------------------

    [Fact]
    public void Baseline_WritesDellSettingsJson()
    {
        string root = Path.Combine(_tempRoot, "baseline");
        CliRun run = Run(Platform(new ScriptedDellSettingsReader(DellCompleteA18)), "baseline", "--json", "--out", root);

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Equal(string.Empty, run.Error);
        string[] files = ParseSingleDocument(run.Output).GetProperty("filesWritten").EnumerateArray().Select(file => file.GetString()!).ToArray();
        Assert.Equal(new[] { "index.json", "decoded.json", "platform.json", "dell-settings.json", "log.txt" }, files.Skip(1).ToArray());

        string directory = Assert.Single(Directory.GetDirectories(root));
        string path = Path.Combine(directory, BaselineExporter.DellSettingsFileName);
        using (JsonDocument file = JsonDocument.Parse(File.ReadAllText(path)))
        {
            JsonElement document = file.RootElement;
            Assert.Equal(new[] { "Format", "ReadAtUtc", "Outcome", "Source", "Machine", "Settings", "Problems" }, document.EnumerateObject().Select(property => property.Name).ToArray());
            Assert.Equal(DellSettingsFile.FormatName, document.GetProperty("Format").GetString());
            Assert.Equal("2026-10-05T07:12:44Z", document.GetProperty("ReadAtUtc").GetString());
            Assert.Equal("Complete", document.GetProperty("Outcome").GetString());
            Assert.Equal("DellCommandMonitor", document.GetProperty("Source").GetString());
            Assert.Equal(Ids, document.GetProperty("Settings").EnumerateArray().Select(setting => setting.GetProperty("Id").GetString()).ToArray());
        }

        // The window's reader derives the same ten texts from the file again.
        DellSettingsFileResult read = DellSettingsFile.Read(directory);
        Assert.True(read.Present);
        Assert.Null(read.Problem);
        Assert.Equal(DcmRowFixtures.UnitA18Texts, read.Settings!.Settings.Select(DellSentences.ValueText).ToArray());
        Assert.Contains("DellSettingsReader completed in", File.ReadAllText(Path.Combine(directory, "log.txt")), StringComparison.Ordinal);
    }

    [Fact]
    public void Baseline_DellProblems_AreWarnings_AndDoNotChangeTheExitCode()
    {
        // A computer without the Dell tools: the file records the read that did not answer, and
        // the problem sentence is one warning.
        string nothing = Path.Combine(_tempRoot, "nothing");
        CliRun unavailable = Run(Platform(), "baseline", "--out", nothing);

        Assert.Equal(ExitCodes.Success, unavailable.ExitCode);
        Assert.Equal(
            "Warning: dell settings: Dell BIOS settings cannot be read. Dell Command Monitor is not installed. "
            + "Install the Microsoft Visual C++ 2015-2022 x64 runtime, then Dell Command Monitor, and try again; the Dell interface setup page of the documentation has the steps.\n",
            unavailable.Error);
        string folder = Assert.Single(Directory.GetDirectories(nothing));
        using (JsonDocument file = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, BaselineExporter.DellSettingsFileName))))
        {
            Assert.Equal("Unavailable", file.RootElement.GetProperty("Outcome").GetString());
            Assert.Equal(JsonValueKind.Null, file.RootElement.GetProperty("Source").ValueKind);
            JsonElement problem = Assert.Single(file.RootElement.GetProperty("Problems").EnumerateArray());
            Assert.Equal(("DellCommandMonitor", "NotInstalled"), (problem.GetProperty("Interface").GetString(), problem.GetProperty("Kind").GetString()));
        }

        // A usable read with a fallback sentence and a warning: one warning line each, in that
        // order, and the exit code is still the baseline's own.
        DellSettingsResult qualified = DellFallback() with { Warnings = new[] { "These names and values were checked against BIOS A18 only; this computer runs BIOS A17." } };
        string other = Path.Combine(_tempRoot, "other");
        CliRun partial = Run(Platform(new ScriptedDellSettingsReader(_ => qualified)), "baseline", "--out", other);

        Assert.Equal(ExitCodes.Success, partial.ExitCode);
        Assert.Equal(
            "Warning: dell settings: Dell Command Monitor is not installed. Dell Command Configure was used instead.\n"
            + "Warning: dell settings: These names and values were checked against BIOS A18 only; this computer runs BIOS A17.\n",
            partial.Error);

        // A reader that throws, or none at all, is a read that did not answer: a warning, the
        // file written, and still the baseline's own exit code.
        foreach (CliPlatform platform in new[] { Platform(new ScriptedDellSettingsReader(_ => throw new InvalidOperationException("broken"))), WithoutDellReader(Platform()) })
        {
            string broken = Path.Combine(_tempRoot, "broken-" + Guid.NewGuid().ToString("N"));
            CliRun run = Run(platform, "baseline", "--out", broken);

            Assert.Equal(ExitCodes.Success, run.ExitCode);
            Assert.Equal("Warning: dell settings: " + UnexpectedSentence + "\n", run.Error);
            Assert.True(File.Exists(Path.Combine(Assert.Single(Directory.GetDirectories(broken)), BaselineExporter.DellSettingsFileName)));
        }
    }

    [Fact]
    public void Baseline_ReadsThroughDellCommandMonitorOnly()
    {
        // The Dell settings read of a baseline, and so of step 05 of the gate script, never starts
        // Dell Command | Configure and never waits for the fallback; the Dell probe of platform.json keeps its own two read options (4.5).
        ScriptedDellSettingsReader reader = new(DellNothingInstalled);

        CliRun run = Run(Platform(reader), "baseline", "--out", Path.Combine(_tempRoot, "dcm-only"));

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        DellReadOptions options = Assert.Single(reader.Reads);
        Assert.Equal(DellSourcePreference.DellCommandMonitor, options.Preference);
        Assert.False(options.IncludeAllAttributes);
        Assert.Equal(new DellReadOptions(DellSourcePreference.DellCommandMonitor, false), BaselineCommand.DellOptions);
        Assert.DoesNotContain("Dell Command Configure", run.Error, StringComparison.Ordinal);
    }

    // ---- helpers ---------------------------------------------------------------------------

    /// <summary>A platform with one variable (SecureBoot), elevated, and the given Dell settings reader (by default a computer without the Dell tools).</summary>
    private static CliPlatform Platform(ScriptedDellSettingsReader? reader = null)
    {
        return CreatePlatform(
            new InMemoryFirmwareVariableStore(new[] { new FirmwareVariable("SecureBoot", KnownGuids.EfiGlobalVariable, BsRt, new byte[] { 0x01 }) }),
            dellSettings: reader);
    }

    /// <summary>The same platform composed without a Dell settings reader.</summary>
    private static CliPlatform WithoutDellReader(CliPlatform platform)
    {
        return new CliPlatform(
            platform.PrivilegeManager,
            platform.FirmwareTypeDetector,
            platform.Store,
            platform.Identity,
            platform.Tpm,
            platform.BitLocker,
            platform.Dell,
            platform.SecureBootPolicy);
    }

    /// <summary>A value of the document as (token, vendor name, text).</summary>
    private static (string?, string?, string?) Value(JsonElement value)
    {
        return (value.GetProperty("token").GetString(), value.GetProperty("vendorName").GetString(), value.GetProperty("text").GetString());
    }

    /// <summary>A process that is not elevated; the privilege is then not attempted.</summary>
    private sealed class NotElevatedPrivilegeManager : IPrivilegeManager
    {
        public bool IsElevated => false;

        public PrivilegeState EnableSystemEnvironmentPrivilege() => PrivilegeState.NotElevated;
    }
}
