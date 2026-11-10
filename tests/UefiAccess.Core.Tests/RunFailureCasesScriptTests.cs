// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;
using UefiAccess.Cli;
using UefiAccess.Cli.Commands;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Variables;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// Static checks on tools/write-tests/Run-FailureCases.ps1, the failure-case rig, and on tools/common/DataFolder.psm1, the module it shares with
/// tools/gate/Run-Gate.ps1. The rig needs an elevated prompt on the target machine and starts
/// uefiaccess.exe, so it never runs here; <see cref="RunFailureCasesOutputFolderTests"/> runs its
/// folder functions and its elevation check in Windows PowerShell 5.1. The checks mirror
/// <see cref="RunGateScriptTests"/>: every file the rig reads back is decoded as UTF-8 and the one
/// it writes has no byte order mark, every date uses the invariant culture, the output folder is
/// made by the tool's data-root rule through the shared module before any command runs, no folder
/// is created with New-Item -Force and no access list is changed. They also pin the rig's own
/// promises: a prompt that is not elevated is refused with exit code 4 before anything else, every
/// case line carries --json and none confirms a change, the case lines are the rig rows of the
/// matrix (section 4.3) with the exit codes and the check names Core defines, FC-02 and FC-15
/// check what section 4.3 lists beyond the exit code with the CLI's own texts, the records of the
/// run are chosen by time, the summary header is exact, the FC-13 row is left for the operator,
/// the exit code is 0 only when no row fails, and nothing the write guard forbids is named in the
/// rig or the module, comments included. <see cref="RunFailureCasesOutputFolderTests"/> also runs
/// the rig's verdict and write-log functions on canned documents.
/// </summary>
public sealed class RunFailureCasesScriptTests
{
    /// <summary>The one line through which each script imports the shared module.</summary>
    private const string ImportLine = "Import-Module -Name (Join-Path $PSScriptRoot '..\\common\\DataFolder.psm1')";

    /// <summary>The header of the verdict table in summary.txt.</summary>
    private const string SummaryHeader = "Case | Command | Expected exit | Actual | BlockedBy | ApiCalled | Verdict";

    private static readonly string ScriptPath = Path.Combine(RepositoryPaths.Root, "tools", "write-tests", "Run-FailureCases.ps1");

    private static readonly string ModulePath = Path.Combine(RepositoryPaths.Root, "tools", "common", "DataFolder.psm1");

    private static readonly string GatePath = Path.Combine(RepositoryPaths.Root, "tools", "gate", "Run-Gate.ps1");

    private static readonly string[] Lines = File.ReadAllLines(ScriptPath);

    /// <summary>The rig's lines without the comment-only ones (the comment-based help block included).</summary>
    private static readonly string[] CodeLines = WithoutComments(Lines);

    private static readonly string[] ModuleLines = File.ReadAllLines(ModulePath);

    private static readonly string[] ModuleCodeLines = WithoutComments(ModuleLines);

    private static readonly string[] GateCodeLines = WithoutComments(File.ReadAllLines(GatePath));

    /// <summary>The functions that moved from Run-Gate.ps1 into the shared module.</summary>
    private static readonly string[] ModuleFunctions =
    {
        "Invoke-UefiAccessQuietly",
        "Get-FullPathWithoutSeparator",
        "Test-IsSameOrInside",
        "Get-EntryAttributes",
        "Get-AccountName",
        "Get-FolderProblem",
        "New-ProtectedFolder",
        "New-OutputFolder",
    };

    /// <summary>The functions the module exports: the folder maker and the three path helpers the scripts call themselves.</summary>
    private static readonly string[] ExportedFunctions = { "New-OutputFolder", "Get-FullPathWithoutSeparator", "Test-IsSameOrInside", "Get-EntryAttributes" };

    /// <summary>
    /// The rows of the rig in summary order: the case, the exit code and
    /// the check that must refuse its line ("" for a line that must not parse). FC-13 is the row
    /// the operator fills in by hand; every other case is a line the rig runs or, where the
    /// machine cannot show it, a NOT VERIFIED row with the same expectation.
    /// </summary>
    private static readonly (string Id, string Kind, int Exit, string Check)[] Matrix =
    {
        ("FC-01", "Case", WriteExitCodes.Blocked, CheckNames.Whitelist),
        ("FC-02a", "Case", WriteExitCodes.Blocked, CheckNames.Whitelist),
        ("FC-02b", "Case", WriteExitCodes.Blocked, CheckNames.Whitelist),
        ("FC-03", "Case", WriteExitCodes.Blocked, CheckNames.EntriesExist),
        ("FC-04", "Case", WriteExitCodes.Blocked, CheckNames.EntriesExist),
        ("FC-05", "Case", WriteExitCodes.Blocked, CheckNames.Permutation),
        ("FC-06a", "Case", WriteExitCodes.Blocked, CheckNames.Permutation),
        ("FC-06b", "Case", WriteExitCodes.Blocked, CheckNames.Permutation),
        ("FC-07", "Case", WriteExitCodes.Blocked, CheckNames.NoChange),
        ("FC-13", "ByHandCase", WriteExitCodes.NotElevated, CheckNames.Preconditions),
        ("FC-15", "Case", WriteExitCodes.Blocked, CheckNames.BitLocker),
        ("FC-16", "Case", WriteExitCodes.Blocked, CheckNames.OtherWindowsPath),
        ("FC-20", "Case", WriteExitCodes.Blocked, CheckNames.SnapshotRoot),
        ("FC-26", "Case", WriteExitCodes.Blocked, CheckNames.BootNextTarget),
        ("FC-27a", "Case", ExitCodes.Usage, ""),
        ("FC-27b", "Case", ExitCodes.Usage, ""),
    };

    /// <summary>
    /// The argument array of every line the rig runs, by case.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> CaseArguments = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["FC-01"] = "@('write', 'check', 'Timeout', '--json')",
        ["FC-02a"] = "@('write', 'check', 'PK', '--json')",
        ["FC-02b"] = "@('write', 'check', 'db', '--guid', '" + KnownGuids.EfiImageSecurityDatabase.ToString("D") + "', '--json')",
        ["FC-03"] = "@('write', 'bootorder', '--order', $orderWithUnknown, '--json')",
        ["FC-04"] = "@('write', 'bootnext', $unknownEntry, '--json')",
        ["FC-05"] = "@('write', 'bootorder', '--order', $orderWithOutside, '--json')",
        ["FC-06a"] = "@('write', 'bootorder', '--order', $orderWithDuplicate, '--json')",
        ["FC-06b"] = "@('write', 'bootorder', '--order', $orderWithOneLeftOut, '--json')",
        ["FC-07"] = "@('write', 'bootorder', '--order', $currentOrder, '--json')",
        ["FC-15"] = "@('write', 'bootnext', $firstEntry, '--json')",
        ["FC-16"] = "@('write', 'bootnext', $otherWindowsEntry, '--json')",
        ["FC-20"] = "@('write', 'bootnext', $firstEntry, '--bitlocker', 'continue', '--snapshot-root', $OutsideSnapshotRoot, '--json')",
        ["FC-26"] = "@('write', 'bootnext', '--clear', '--json')",
        ["FC-27a"] = "@('write', 'bootorder', '--order', $currentOrder, '--dry-run', '--force', '--json')",
        ["FC-27b"] = "@('write', 'bootnext', $firstEntry, '--bitlocker', 'later', '--json')",
    };

    /// <summary>A case line: its kind, its identifier, and the exit code and check it expects.</summary>
    private static readonly Regex CaseLinePattern = new(
        @"^\s*Add-(?<kind>Case|SkippedCase|ByHandCase) -Id '(?<id>[^']+)' .*-ExpectedExit (?<exit>\d+) -ExpectedCheck '(?<check>[^']*)'",
        RegexOptions.CultureInvariant);

    [Fact]
    public void EveryGetContentDecodesUtf8()
    {
        string[] reads = CodeLines.Where(line => line.Contains("Get-Content", StringComparison.Ordinal)).ToArray();

        Assert.True(reads.Length >= 2, "expected the standard error and the JSON document reads");
        Assert.All(reads, line => Assert.Contains("-Encoding UTF8", line, StringComparison.Ordinal));
    }

    [Fact]
    public void EveryDateIsFormattedWithTheInvariantCulture()
    {
        Regex dateFormat = new(@"\.ToString\('[^']*(yyyy|HH)[^']*'(?<rest>[^)]*)\)", RegexOptions.CultureInvariant);
        Match[] formats = Lines.SelectMany(line => dateFormat.Matches(line)).ToArray();

        Assert.True(formats.Length >= 6, "expected the folder name, the record time, the FC-20 date and the three summary times");
        Assert.All(formats, match => Assert.Equal(", $Invariant", match.Groups["rest"].Value));
        Assert.Contains(CodeLines, line => line == "$Invariant = [System.Globalization.CultureInfo]::InvariantCulture");
        Assert.Contains(CodeLines, line => line == "$timestamp = $started.ToString('yyyyMMdd-HHmmss', $Invariant)");
        Assert.Contains(CodeLines, line => line == "$startedUtcText = $started.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ', $Invariant)");
        Assert.DoesNotContain(CodeLines, line => Regex.IsMatch(line, @"Get-Date\s+-U?Format", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase));
    }

    [Fact]
    public void TheOneFileTheRigWritesIsUtf8WithoutAByteOrderMark()
    {
        // uefiaccess writes the redirected streams itself; the rig writes summary.txt only, through
        // .NET with an encoding that has no byte order mark, never through a cmdlet whose encoding
        // depends on the PowerShell version.
        string[] cmdletWriters = { "Set-Content", "Add-Content", "Out-File", "Export-Csv", "Export-Clixml", "Tee-Object" };
        Assert.DoesNotContain(CodeLines.Concat(ModuleCodeLines), line => cmdletWriters.Any(writer => line.Contains(writer, StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(CodeLines, line => line == "$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)");
        string write = Assert.Single(CodeLines, line => line.Contains("[System.IO.File]::Write", StringComparison.Ordinal));
        Assert.Equal("[System.IO.File]::WriteAllLines((Join-Path $outputFolder 'summary.txt'), [string[]]$summaryLines, $Utf8NoBom)", write.Trim());
    }

    [Fact]
    public void DataRootComesFromTheKnownFolderTheToolUses_AndTheOutputGoesUnderWriteTests()
    {
        Assert.Contains(CodeLines, line => line == "$DataRoot = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'UefiAccess'");
        Assert.DoesNotContain(CodeLines, line => line.Contains("$env:ProgramData", StringComparison.OrdinalIgnoreCase) || line.Contains("C:\\ProgramData", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(CodeLines, line => line == "$OutputRoot = Join-Path $DataRoot 'write-tests'");
        string outputFolder = Assert.Single(CodeLines, line => line.TrimStart().StartsWith("$outputFolder = ", StringComparison.Ordinal));
        Assert.Equal("$outputFolder = Get-FullPathWithoutSeparator -Path ([System.IO.Path]::Combine($OutputRoot, $timestamp))", outputFolder.Trim());
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "UefiAccess"), Logging.DataDirectory);
    }

    [Fact]
    public void TrustedOwnersAndAccessList_AreTheToolsAndTheGateScripts()
    {
        string owners = Assert.Single(CodeLines, candidate => candidate.StartsWith("$TrustedOwnerSids = ", StringComparison.Ordinal));
        string sddlLine = Assert.Single(CodeLines, candidate => candidate.StartsWith("$ProtectedFolderSddl = ", StringComparison.Ordinal));

        Assert.Equal(Assert.Single(GateCodeLines, candidate => candidate.StartsWith("$TrustedOwnerSids = ", StringComparison.Ordinal)), owners);
        Assert.Equal(Assert.Single(GateCodeLines, candidate => candidate.StartsWith("$ProtectedFolderSddl = ", StringComparison.Ordinal)), sddlLine);

        string[] sids = Regex.Matches(owners, "'(?<sid>[^']*)'").Select(match => match.Groups["sid"].Value).ToArray();
        Assert.Equal(3, sids.Length);
        Assert.All(sids, sid => Assert.True(Logging.IsTrustedOwner(new SecurityIdentifier(sid)), sid + " is not an owner the tool trusts"));

        DirectorySecurity fromScript = new();
        fromScript.SetSecurityDescriptorSddlForm(Regex.Match(sddlLine, "^\\$ProtectedFolderSddl = '(?<sddl>[^']+)'$").Groups["sddl"].Value);
        LoggingFallbackTests.AssertProtectedAccessList(fromScript);
        Assert.Equal(
            Logging.CreateDataDirectorySecurity().GetSecurityDescriptorSddlForm(AccessControlSections.Access),
            fromScript.GetSecurityDescriptorSddlForm(AccessControlSections.Access));
    }

    [Fact]
    public void BothScriptsImportTheSharedModule_WhichHoldsEveryFolderFunction()
    {
        foreach ((string path, string[] code) in new[] { (ScriptPath, CodeLines), (GatePath, GateCodeLines) })
        {
            string import = Assert.Single(code, line => line.Contains("Import-Module", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(ImportLine, import);
            string resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, "..", "common", "DataFolder.psm1"));
            Assert.Equal(ModulePath, resolved);

            // The folder functions live in the module only, and the old name of New-OutputFolder is gone.
            Assert.All(ModuleFunctions.Append("New-GateOutputFolder"), name => Assert.DoesNotContain(code, line => line.Trim() == "function " + name + " {"));
        }

        Assert.True(File.Exists(ModulePath), ModulePath + " is missing");
        Assert.All(ModuleFunctions, name => Assert.Single(ModuleCodeLines, line => line == "function " + name + " {"));
        string exportLine = Assert.Single(ModuleCodeLines, line => line.StartsWith("Export-ModuleMember ", StringComparison.Ordinal));
        string[] exported = exportLine.Substring("Export-ModuleMember -Function ".Length).Split(',').Select(name => name.Trim()).ToArray();
        Assert.Equal(ExportedFunctions.OrderBy(name => name, StringComparer.Ordinal), exported.OrderBy(name => name, StringComparer.Ordinal));

        // A module does not see the preference variables of the script that imports it, so it sets
        // its own before its first function: New-OutputFolder relies on an error stopping it.
        int firstFunction = Array.FindIndex(ModuleCodeLines, line => line.StartsWith("function ", StringComparison.Ordinal));
        int strict = Array.IndexOf(ModuleCodeLines, "Set-StrictMode -Version 2.0");
        int stop = Array.IndexOf(ModuleCodeLines, "$ErrorActionPreference = 'Stop'");
        Assert.True(strict >= 0 && strict < firstFunction, "the module must set strict mode before its first function");
        Assert.True(stop >= 0 && stop < firstFunction, "the module must stop on every error before its first function");
    }

    [Fact]
    public void OutputFolder_IsMadeByTheDataRootRuleBeforeAnyCommandRuns()
    {
        int import = IndexOf(line => line == ImportLine);
        int marker = IndexOf(line => line == "$env:UEFIACCESS_HW = '1'");
        int prepare = IndexOf(line => line == "$folderProblem = New-OutputFolder -Folder $outputFolder -DataRoot $DataRoot -Exe $ExePath -TrustedOwners $TrustedOwnerSids -Sddl $ProtectedFolderSddl");
        int firstStep = IndexOf(line => line.Contains("Invoke-UefiAccess -Exe ", StringComparison.Ordinal));

        // The quiet "info" run that creates a missing data root is tagged as a hardware run too,
        // and nothing is written into the output folder before it has been made and checked.
        Assert.True(import < prepare, "the shared module must be imported before the output folder is prepared");
        Assert.True(marker < prepare, "UEFIACCESS_HW must be set before the output folder is prepared");
        Assert.True(prepare < firstStep, "the output folder must be prepared before any command writes into it");
        Assert.Equal(new[] { "if ($folderProblem -ne '') {", "Write-Host ''", "Write-Host $folderProblem", "exit 2" }, CodeLines.Skip(prepare + 1).Take(4).Select(line => line.Trim()));
        Assert.Single(CodeLines, line => line == "$env:UEFIACCESS_HW = '1'");
    }

    [Fact]
    public void NoFolderIsCreatedWithForce_AndNoAccessListIsChanged()
    {
        // New-Item -Force accepts an existing folder or junction. The rig creates no folder of its
        // own (the output folder comes from the module; the FC-20 root must never be created), and
        // the module's one New-Item is New-OutputFolder's, for a folder outside the data root.
        Regex folderCreation = new(@"New-Item|CreateDirectory|(?<![\w-])mkdir(?![\w-])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        Assert.DoesNotContain(CodeLines, line => folderCreation.IsMatch(line));
        string[] newItems = ModuleCodeLines.Where(line => line.Contains("New-Item", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.Equal(new[] { "$null = New-Item -ItemType Directory -Path $Folder" }, newItems.Select(line => line.Trim()));

        Regex newItemForce = new(@"New-Item[^\r\n]*-Force", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        Assert.DoesNotContain(Lines.Concat(ModuleLines), line => newItemForce.IsMatch(line));
        Assert.DoesNotContain(Lines.Concat(ModuleLines), line => line.Contains("Set-Acl", StringComparison.OrdinalIgnoreCase) || line.Contains("icacls", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void APromptThatIsNotElevated_IsRefusedWithExitCodeFourBeforeAnythingElse()
    {
        int stop = IndexOf(line => line == "$ErrorActionPreference = 'Stop'");
        int check = IndexOf(line => line == "if (-not (Test-IsElevated)) {");

        // Between the error preference and the check there is only the check's own function.
        string[] between = CodeLines.Skip(stop + 1).Take(check - stop - 1).Where(line => line.Trim().Length > 0).ToArray();
        Assert.Equal("function Test-IsElevated {", between.First());
        Assert.Equal("}", between.Last());
        Assert.All(between.Skip(1).SkipLast(1), line => Assert.StartsWith("    ", line, StringComparison.Ordinal));
        Assert.Contains(between, line => line.Trim() == "return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)");

        // The check only speaks and stops.
        string[] block = CodeLines.Skip(check + 1).TakeWhile(line => line != "}").Select(line => line.Trim()).ToArray();
        Assert.Equal("exit 4", block.Last());
        Assert.All(block.SkipLast(1), line => Assert.StartsWith("Write-Host '", line, StringComparison.Ordinal));

        // Everything else comes after it.
        foreach (Func<string, bool> later in new Func<string, bool>[]
        {
            line => line == ImportLine,
            line => line == "$started = Get-Date",
            line => line == "$env:UEFIACCESS_HW = '1'",
            line => line.StartsWith("$folderProblem = New-OutputFolder ", StringComparison.Ordinal),
            line => line.Contains("Invoke-UefiAccess -Exe ", StringComparison.Ordinal),
        })
        {
            Assert.True(check < IndexOf(later), "the elevation check must come before the imports, the output folder and every command");
        }
    }

    [Fact]
    public void EveryCaseLineCarriesJson_AndNoCaseLineConfirmsAChange()
    {
        string[] runLines = CodeLines.Where(line => line.TrimStart().StartsWith("Add-Case -Id ", StringComparison.Ordinal)).ToArray();
        string[] skippedLines = CodeLines.Where(line => line.TrimStart().StartsWith("Add-SkippedCase -Id ", StringComparison.Ordinal)).ToArray();

        Assert.NotEmpty(runLines);
        Assert.NotEmpty(skippedLines);
        Assert.All(runLines, line => Assert.Contains(", '--json') -ExpectedExit ", line, StringComparison.Ordinal));
        Assert.All(skippedLines, line => Assert.Contains(" --json", line, StringComparison.Ordinal));
        Assert.All(runLines.Concat(skippedLines), line => Assert.DoesNotContain("--confirm", line, StringComparison.OrdinalIgnoreCase));

        // Every uefiaccess run passes either a case's own arguments, once, or a literal array that
        // carries --json and does not confirm.
        string[] invocations = CodeLines.Where(line => line.Contains("Invoke-UefiAccess -Exe ", StringComparison.Ordinal)).ToArray();
        Assert.Single(invocations, line => line.Contains("-Arguments $case.Arguments ", StringComparison.Ordinal));
        Assert.All(
            invocations.Where(line => !line.Contains("-Arguments $case.Arguments ", StringComparison.Ordinal)),
            line =>
            {
                Match arguments = Regex.Match(line, @"-Arguments @\((?<list>[^)]*)\)", RegexOptions.CultureInvariant);
                Assert.True(arguments.Success, "a literal argument array is expected: " + line);
                Assert.EndsWith("'--json'", arguments.Groups["list"].Value, StringComparison.Ordinal);
                Assert.DoesNotContain("--confirm", arguments.Groups["list"].Value, StringComparison.OrdinalIgnoreCase);
            });

        // The one mention of the confirmation option in the whole script, comments included, is
        // the FC-13 row the operator runs by hand from a standard prompt; the rig never runs it.
        string mention = Assert.Single(Lines, line => line.Contains("--confirm", StringComparison.OrdinalIgnoreCase));
        Assert.StartsWith("Add-ByHandCase -Id 'FC-13' ", mention.TrimStart(), StringComparison.Ordinal);

        // The case loop skips the rows it does not run before it runs a line.
        int byHand = IndexOf(line => line.Trim() == "if ($case.Kind -eq 'ByHand') {");
        int skipped = IndexOf(line => line.Trim() == "if ($case.Kind -eq 'Skipped') {");
        int run = IndexOf(line => line.Contains("-Arguments $case.Arguments ", StringComparison.Ordinal));
        Assert.True(byHand < run && skipped < run, "the loop must leave the by-hand and skipped rows before it runs a line");
    }

    [Fact]
    public void Cases_AreTheRigRowsOfTheMatrix_WithTheExitCodesAndChecksCoreDefines()
    {
        Match[] cases = CodeLines.Select(line => CaseLinePattern.Match(line)).Where(match => match.Success).ToArray();
        Assert.NotEmpty(cases);

        // Every line of a case, run or skipped, expects the same; the order of first appearance is the summary order.
        string[] order = cases.Select(match => match.Groups["id"].Value).Distinct(StringComparer.Ordinal).ToArray();
        Assert.Equal(Matrix.Select(row => row.Id), order);
        foreach ((string id, string kind, int exit, string check) in Matrix)
        {
            Match[] lines = cases.Where(match => match.Groups["id"].Value == id).ToArray();
            Assert.All(lines, match => Assert.Equal(exit.ToString(System.Globalization.CultureInfo.InvariantCulture), match.Groups["exit"].Value));
            Assert.All(lines, match => Assert.Equal(check, match.Groups["check"].Value));
            Assert.Contains(lines, match => match.Groups["kind"].Value == kind);
        }

        // The cases a machine may not be able to show have a NOT VERIFIED branch; the others always run.
        string[] skippable = cases.Where(match => match.Groups["kind"].Value == "SkippedCase").Select(match => match.Groups["id"].Value).ToArray();
        Assert.Equal(new[] { "FC-05", "FC-06b", "FC-16", "FC-20", "FC-26" }, skippable);
    }

    [Fact]
    public void CaseLines_UseTheCommandLinesOfTheMatrix()
    {
        foreach ((string id, string arguments) in CaseArguments)
        {
            string line = Assert.Single(CodeLines, candidate => candidate.TrimStart().StartsWith("Add-Case -Id '" + id + "' ", StringComparison.Ordinal));
            Assert.Contains("-Arguments " + arguments + " -ExpectedExit ", line, StringComparison.Ordinal);
        }

        // The entry that does not exist on the unit, as the matrix names it, and the FC-15 and
        // FC-16 lines that may be NOT VERIFIED on another machine state, and FC-20's folder check.
        Assert.Contains(CodeLines, line => line == "$DefaultUnknownEntry = 'Boot0007'");
        Assert.Contains(CodeLines, line => line == "$WindowsBootManagerFile = '" + ChangeValidator.WindowsBootManagerFilePath + "'");
        Assert.EndsWith(" -NotVerifiedReason $bitLockerChoiceReason", Assert.Single(CodeLines, line => line.TrimStart().StartsWith("Add-Case -Id 'FC-15' ", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.EndsWith(" -NotVerifiedReason $otherWindowsPathReason", Assert.Single(CodeLines, line => line.TrimStart().StartsWith("Add-Case -Id 'FC-16' ", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.EndsWith(" -AbsentAfter $OutsideSnapshotRoot", Assert.Single(CodeLines, line => line.TrimStart().StartsWith("Add-Case -Id 'FC-20' ", StringComparison.Ordinal)), StringComparison.Ordinal);

        // The states in which the pipeline asks for a BitLocker choice, and those in which rule V13 applies.
        Assert.Contains(CodeLines, line => line == "$StatesNeedingAChoice = @('" + string.Join("', '", BitLockerState.On, BitLockerState.Encrypting, BitLockerState.Unknown) + "')");
        Assert.Contains(CodeLines, line => line == "$StatesWithProtectors = @('" + string.Join("', '", BitLockerState.On, BitLockerState.Encrypting, BitLockerState.Suspended, BitLockerState.Unknown) + "')");
    }

    [Fact]
    public void Fc02AndFc15_CheckWhatTheMatrixListsBeyondTheExitCode_WithTheClisOwnTexts()
    {
        // FC-02: the authenticatedWrite check failed too.
        foreach (string id in new[] { "FC-02a", "FC-02b" })
        {
            string line = Assert.Single(CodeLines, candidate => candidate.TrimStart().StartsWith("Add-Case -Id '" + id + "' ", StringComparison.Ordinal));
            Assert.EndsWith(" -ExpectedCheck '" + CheckNames.Whitelist + "' -AlsoFailed @('" + CheckNames.AuthenticatedWrite + "')", line, StringComparison.Ordinal);
        }

        // FC-15: suspendOffered true, the three BitLocker lines and the hint on standard error.
        string fc15 = Assert.Single(CodeLines, candidate => candidate.TrimStart().StartsWith("Add-Case -Id 'FC-15' ", StringComparison.Ordinal));
        Assert.Contains(" -ExpectedCheck '" + CheckNames.BitLocker + "' -SuspendOffered -StderrStarts $BitLockerChoiceLines -NotVerifiedReason ", fc15, StringComparison.Ordinal);

        // The starts are the CLI's: each line format up to its sentence, and the hint whole.
        string[] starts =
        {
            WriteCommand.BitLockerStateLineFormat.Replace("{0}", string.Empty, StringComparison.Ordinal),
            WriteCommand.RiskLineFormat.Replace("{0}", string.Empty, StringComparison.Ordinal),
            WriteCommand.RecoveryKeyLineFormat.Replace("{0}", string.Empty, StringComparison.Ordinal),
            WriteCommand.BitLockerHint,
        };
        Assert.All(starts, start => Assert.DoesNotContain("'", start, StringComparison.Ordinal));
        Assert.All(starts.Take(3), start => Assert.EndsWith(": ", start, StringComparison.Ordinal));
        Assert.Contains(CodeLines, line => line == "$BitLockerChoiceLines = @('" + string.Join("', '", starts) + "')");

        // The details are judged for a case the machine can show, on its write document only.
        Assert.Contains(CodeLines, line => line.Trim() == "$problems += @(Get-CaseDetailProblems -Case $Case -Document $document -Run $Run)");
        Assert.Contains(CodeLines, line => line.Trim() == "foreach ($name in @($Case.AlsoFailed)) {");
        Assert.Contains(CodeLines, line => line.Trim() == "if ($Case.SuspendOffered -and -not (Test-SuspendOffered -Document $Document)) {");
    }

    [Fact]
    public void TheRecordsOfTheRun_AreChosenByTimeNotByText()
    {
        // Windows PowerShell 5.1's ConvertFrom-Json leaves a record's time as text, PowerShell 7
        // makes it a DateTime; a text compare would drop every record under PowerShell 7.
        Assert.Contains(CodeLines, line => line == "$startedUtc = [DateTimeOffset]::ParseExact($startedUtcText, \"yyyy-MM-dd'T'HH:mm:ss'Z'\", $Invariant, [System.Globalization.DateTimeStyles]'AssumeUniversal')");
        Assert.Single(CodeLines, line => line.Trim() == "$thisRun = @(Select-RunRecord -Records $log.Records -Since $startedUtc)");
        Assert.DoesNotContain(CodeLines, line => line.Contains("CompareOrdinal", StringComparison.Ordinal));
        Assert.Contains(CodeLines, line => line.Trim() == "if ([DateTimeOffset]::TryParse([string]$value, $Invariant, $styles, [ref]$parsed)) {");

        // An error document instead of the list says why, instead of counting no record.
        Assert.Contains(CodeLines, line => line.Trim() == "elseif ((Get-DocumentKind -Document $logRead.Document) -eq 'error') {");
    }

    [Fact]
    public void Fc20SnapshotRoot_IsOutsideTheDataRoot_AndIsNeverCreated()
    {
        Assert.Contains(CodeLines, line => line.Trim() == "$OutsideSnapshotRoot = 'C:\\UefiAccess-Evidence\\' + $started.ToString('yyyy-MM-dd', $Invariant) + '\\m3\\H1\\snapshots'");

        int refusal = IndexOf(line => line == "if (Test-IsSameOrInside -Path $OutsideSnapshotRoot -Folder (Get-FullPathWithoutSeparator -Path $DataRoot)) {");
        Assert.Contains("exit 2", CodeLines.Skip(refusal + 1).Take(4).Select(line => line.Trim()));
        Assert.True(refusal < IndexOf(line => line.StartsWith("$folderProblem = New-OutputFolder ", StringComparison.Ordinal)), "a snapshot root inside the data root must be refused before anything is created");
    }

    [Fact]
    public void SummaryHeaderIsExact_AndTheFc13RowIsLeftForTheOperator()
    {
        Assert.Contains(CodeLines, line => line.Trim() == "$tableLines += '" + SummaryHeader + "'");
        Assert.Contains(CodeLines, line => line.Trim() == "$tableLines += (@($row.Case, $row.Command, $row.Expected, $row.Actual, $row.BlockedBy, $row.ApiCalled, $row.Verdict) -join ' | ')");

        // The FC-13 row names the matrix's line with the entry chosen on this machine, which
        // validation blocks even from a prompt that turns out to be elevated.
        string row = Assert.Single(CodeLines, line => line.TrimStart().StartsWith("Add-ByHandCase -Id 'FC-13' ", StringComparison.Ordinal));
        Assert.Contains("('uefiaccess write bootnext ' + $unknownEntry + ' --confirm --json, by hand from a standard (not elevated) prompt, with whoami /groups beside it')", row, StringComparison.Ordinal);
        Assert.Contains("-Reason 'NOT RUN BY RIG (", row, StringComparison.Ordinal);
        Assert.Contains(CodeLines, line => line.Contains("-Actual 'NOT RUN BY RIG' ", StringComparison.Ordinal));
    }

    [Fact]
    public void ExitCode_IsZeroOnlyWhenNoRowFails()
    {
        Assert.Contains(CodeLines, line => line == "$failCount = @($script:Rows | Where-Object { $_.Verdict.StartsWith('FAIL', [System.StringComparison]::Ordinal) }).Count");
        string[] ending = CodeLines.Where(line => line.Trim().Length > 0).TakeLast(7).ToArray();
        Assert.Equal(
            new[] { "if ($preconditionCode -ne 0) {", "    exit $preconditionCode", "}", "if ($failCount -eq 0) {", "    exit 0", "}", "exit 1" },
            ending);
        Assert.Single(CodeLines, line => line.Trim() == "exit 0");
    }

    [Fact]
    public void ExitCodeText_NamesTheWriteExitCodes()
    {
        foreach (int code in new[] { WriteExitCodes.Blocked, WriteExitCodes.ApiRefused, WriteExitCodes.Unverified })
        {
            Assert.Single(CodeLines, line => line.Trim().StartsWith(code.ToString(System.Globalization.CultureInfo.InvariantCulture) + " { return '", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void DefaultExePath_IsTheReleaseBuildRunGateUses_AndUefiAccessIsTheOnlyProgramStarted()
    {
        const string DefaultExe = "$ExePath = Join-Path $PSScriptRoot '..\\..\\src\\UefiAccess.Cli\\bin\\Release\\net8.0-windows\\uefiaccess.exe'";
        Assert.Contains(CodeLines, line => line.Trim() == DefaultExe);
        Assert.Contains(GateCodeLines, line => line.Trim() == DefaultExe);

        string start = Assert.Single(CodeLines, line => line.Contains("Start-Process", StringComparison.OrdinalIgnoreCase));
        Assert.StartsWith("$process = Start-Process -FilePath $Exe -ArgumentList $argumentLine -NoNewWindow -Wait -PassThru", start.Trim(), StringComparison.Ordinal);
        Regex otherLaunch = new(@"(?<![\w-])(?:Invoke-Item|Start-Job|Invoke-Command|cmd\.exe|powershell\.exe|pwsh\.exe)(?![\w-])|Diagnostics\.Process|(?:^|[\s(=;])&\s", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        Assert.DoesNotContain(CodeLines, line => otherLaunch.IsMatch(line));
    }

    [Fact]
    public void TheRigAndTheModuleNameNothingTheWriteGuardForbids()
    {
        // The guard scans tools/*.ps1, the rig included; the module is scanned here with the same
        // matcher, every line, comments included: no firmware, BitLocker, Secure Boot, TPM,
        // registry or boot-configuration write path, no BitLocker tool or command, no key material.
        List<string> violations = new();
        foreach ((string name, string[] lines) in new[] { ("Run-FailureCases.ps1", Lines), ("DataFolder.psm1", ModuleLines) })
        {
            for (int index = 0; index < lines.Length; index++)
            {
                violations.AddRange(NoWriteGuardTests.FindViolations(lines[index]).Select(violation => $"{name}:{index + 1}: {violation}"));
            }
        }

        Assert.True(violations.Count == 0, "Forbidden names in the rig or the module:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static int IndexOf(Func<string, bool> predicate)
    {
        int index = Array.FindIndex(CodeLines, line => predicate(line));
        Assert.True(index >= 0, "line not found in Run-FailureCases.ps1");
        return index;
    }

    private static string[] WithoutComments(string[] lines)
    {
        List<string> code = new();
        bool inBlock = false;
        foreach (string line in lines)
        {
            string trimmed = line.TrimStart();
            if (inBlock)
            {
                inBlock = !trimmed.StartsWith("#>", StringComparison.Ordinal);
                continue;
            }

            if (trimmed.StartsWith("<#", StringComparison.Ordinal))
            {
                inBlock = !trimmed.Contains("#>", StringComparison.Ordinal);
                continue;
            }

            if (!trimmed.StartsWith('#'))
            {
                code.Add(line);
            }
        }

        return code.ToArray();
    }
}
