// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Globalization;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using UefiAccess.Cli.Commands;
using UefiAccess.Core.Privileges;
using UefiAccess.Core.Safety;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// How tools/write-tests/Run-FailureCases.ps1 makes its output folder, and what it does before
/// anything else, run in Windows PowerShell 5.1 against a temporary stand-in for
/// C:\ProgramData\UefiAccess. The rig needs an elevated prompt on the target machine and starts
/// uefiaccess.exe, so it never runs whole: the folder functions come from the shared module
/// tools/common/DataFolder.psm1, which the rig imports, with the trusted owners and the access list
/// the rig declares (taken from its syntax tree), and New-OutputFolder is called once per test for
/// write-tests\&lt;yyyyMMdd-HHmmss&gt; under the stand-in data root, as the rig calls it. Inside
/// the data root it follows the tool's rule (Logging.PrepareDataFolderFor): a missing root is left
/// to uefiaccess to create (a stand-in .cmd plays uefiaccess.exe), every existing folder must be a
/// plain folder with a trusted owner, a missing one is created with the tool's protected access
/// list, and the output folder must be new. The module's own error preference, its exports, the
/// Windows PowerShell 5.1 parse of the rig and the module, and the rig's elevation check (its first
/// statement, run alone) are checked here too, and the rig's verdict and write-log functions are
/// run on canned documents and standard error files (nothing starts uefiaccess): the exit code,
/// blockedBy, apiCalled, the parse-error row, NOT VERIFIED, the FC-02 and FC-15 details, and the
/// records of the run chosen by time, with the time as text (Windows PowerShell 5.1) and as the
/// DateTime and DateTimeOffset values PowerShell 7 can give. Folder owners cannot be changed without elevation,
/// so the test host's own account is added to the trusted owners where a test is not about the
/// owner; where the outcome depends on whether the host is elevated, both outcomes are asserted
/// precisely.
/// </summary>
[Collection(WindowsPowerShellCollection.Name)]
public sealed class RunFailureCasesOutputFolderTests : IDisposable
{
    private const string RunId = "20260930-141500";

    /// <summary>
    /// Imports the shared module with the caller's error preference from the environment, loads
    /// the rig's two declarations from its syntax tree (nothing else in the rig runs), prints the
    /// functions the module exports, calls New-OutputFolder with the folders from the environment,
    /// and prints its result with the number of objects it returned, which must be one string.
    /// </summary>
    private const string FolderHarness = """
        Set-StrictMode -Version 2.0
        $ErrorActionPreference = $env:RUNFC_CALLER_PREFERENCE
        [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
        Import-Module -Name $env:RUNFC_MODULE
        $tokens = $null
        $parseErrors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($env:RUNFC_SCRIPT, [ref]$tokens, [ref]$parseErrors)
        if (@($parseErrors).Count -gt 0) { throw ('Run-FailureCases.ps1 does not parse: ' + (@($parseErrors) -join '; ')) }
        $declarations = @($ast.EndBlock.Statements | Where-Object { $_ -is [System.Management.Automation.Language.AssignmentStatementAst] -and $_.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and @('TrustedOwnerSids', 'ProtectedFolderSddl') -contains $_.Left.VariablePath.UserPath })
        if ($declarations.Count -ne 2) { throw 'Run-FailureCases.ps1 does not declare $TrustedOwnerSids and $ProtectedFolderSddl' }
        . ([scriptblock]::Create((@($declarations) | ForEach-Object { $_.Extent.Text }) -join [Environment]::NewLine))
        $trusted = @($TrustedOwnerSids)
        if ($env:RUNFC_TRUST_TEST_USER -eq '1') { $trusted += [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value }
        Write-Output ('EXPORTED:' + (@((Get-Module -Name DataFolder).ExportedFunctions.Keys) -join ','))
        $result = New-OutputFolder -Folder $env:RUNFC_FOLDER -DataRoot $env:RUNFC_DATA_ROOT -Exe $env:RUNFC_EXE -TrustedOwners $trusted -Sddl $ProtectedFolderSddl
        Write-Output ('RESULT(' + @($result).Count + '):' + (@($result) -join '|'))
        """;

    /// <summary>
    /// Finds the rig's first statement after its strict mode, its error preference and its
    /// function definitions, prints its first line, then runs the rig's Test-IsElevated and that
    /// statement alone (nothing else in the rig runs) and prints a marker when it let the run go on.
    /// </summary>
    private const string ElevationHarness = """
        Set-StrictMode -Version 2.0
        $ErrorActionPreference = 'Stop'
        [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
        $tokens = $null
        $parseErrors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($env:RUNFC_SCRIPT, [ref]$tokens, [ref]$parseErrors)
        if (@($parseErrors).Count -gt 0) { throw ('Run-FailureCases.ps1 does not parse: ' + (@($parseErrors) -join '; ')) }
        $first = $null
        foreach ($statement in @($ast.EndBlock.Statements)) {
            if ($statement -is [System.Management.Automation.Language.FunctionDefinitionAst]) { continue }
            $text = $statement.Extent.Text
            if ($text -eq 'Set-StrictMode -Version 2.0' -or $text -eq '$ErrorActionPreference = ''Stop''') { continue }
            $first = $statement
            break
        }
        if ($null -eq $first) { throw 'Run-FailureCases.ps1 has no statement to run' }
        Write-Output ('FIRST:' + $first.Extent.Text.Split([char]10)[0].TrimEnd())
        $functions = @($ast.EndBlock.Statements | Where-Object { $_ -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $_.Name -eq 'Test-IsElevated' })
        if ($functions.Count -ne 1) { throw 'Run-FailureCases.ps1 does not define Test-IsElevated once' }
        . ([scriptblock]::Create($functions[0].Extent.Text))
        . ([scriptblock]::Create($first.Extent.Text))
        Write-Output 'WENT-ON'
        exit 0
        """;

    /// <summary>
    /// Loads the rig's $Invariant and $BitLockerChoiceLines and every one of its functions from its
    /// syntax tree (nothing else in the rig runs), then judges canned case runs from the files in
    /// RUNFC_CASES with Add-Case and Get-CaseResult as the rig does, and chooses the records of a
    /// run with Get-LogRecords and Select-RunRecord, printing one line per result.
    /// </summary>
    private const string VerdictHarness = """
        Set-StrictMode -Version 2.0
        $ErrorActionPreference = 'Stop'
        [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
        Import-Module -Name $env:RUNFC_MODULE
        $tokens = $null
        $parseErrors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($env:RUNFC_SCRIPT, [ref]$tokens, [ref]$parseErrors)
        if (@($parseErrors).Count -gt 0) { throw ('Run-FailureCases.ps1 does not parse: ' + (@($parseErrors) -join '; ')) }
        $names = @('Invariant', 'BitLockerChoiceLines', 'WindowsBootManagerFile')
        $declarations = @($ast.EndBlock.Statements | Where-Object { $_ -is [System.Management.Automation.Language.AssignmentStatementAst] -and $_.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and $names -contains $_.Left.VariablePath.UserPath })
        if ($declarations.Count -ne $names.Count) { throw 'Run-FailureCases.ps1 does not declare $Invariant, $BitLockerChoiceLines and $WindowsBootManagerFile once each' }
        . ([scriptblock]::Create((@($declarations) | ForEach-Object { $_.Extent.Text }) -join [Environment]::NewLine))
        $functions = @($ast.EndBlock.Statements | Where-Object { $_ -is [System.Management.Automation.Language.FunctionDefinitionAst] })
        . ([scriptblock]::Create((@($functions) | ForEach-Object { $_.Extent.Text }) -join [Environment]::NewLine))
        $script:Cases = @()
        $folder = $env:RUNFC_CASES

        function Show-Verdict {
            param([string]$Name, [int]$ExitCode, [string]$Document, [string]$Stderr)
            $case = $script:Cases[$script:Cases.Count - 1]
            $run = New-Object PSObject -Property @{
                Step     = $case.Id
                Command  = $case.Command
                ExitCode = $ExitCode
                Note     = ''
                Stdout   = (Join-Path $folder $Document)
                Stderr   = (Join-Path $folder $Stderr)
            }
            $result = Get-CaseResult -Case $case -Run $run
            Write-Output ('VERDICT:' + $Name + ':' + $result.Verdict)
        }

        Add-Case -Id 'FC-02a' -Arguments @('write', 'check', 'PK', '--json') -ExpectedExit 7 -ExpectedCheck 'whitelist' -AlsoFailed @('authenticatedWrite')
        Show-Verdict -Name 'fc02-both-failed' -ExitCode 7 -Document 'fc02-both.json' -Stderr 'empty.txt'
        Show-Verdict -Name 'fc02-authenticated-passed' -ExitCode 7 -Document 'fc02-whitelist-only.json' -Stderr 'empty.txt'

        Add-Case -Id 'FC-15' -Arguments @('write', 'bootnext', 'Boot0002', '--json') -ExpectedExit 7 -ExpectedCheck 'bitLocker' -SuspendOffered -StderrStarts $BitLockerChoiceLines
        Show-Verdict -Name 'fc15-complete' -ExitCode 7 -Document 'fc15-offered.json' -Stderr 'fc15-complete.txt'
        Show-Verdict -Name 'fc15-no-hint' -ExitCode 7 -Document 'fc15-offered.json' -Stderr 'fc15-no-hint.txt'
        Show-Verdict -Name 'fc15-not-offered' -ExitCode 7 -Document 'fc15-not-offered.json' -Stderr 'fc15-complete.txt'
        Show-Verdict -Name 'fc15-no-stderr' -ExitCode 7 -Document 'fc15-offered.json' -Stderr 'missing.txt'

        Add-Case -Id 'FC-15' -Arguments @('write', 'bootnext', 'Boot0002', '--json') -ExpectedExit 7 -ExpectedCheck 'bitLocker' -SuspendOffered -StderrStarts $BitLockerChoiceLines -NotVerifiedReason 'BitLocker was Off when the script ran'
        Show-Verdict -Name 'fc15-off' -ExitCode 0 -Document 'fc15-off.json' -Stderr 'empty.txt'

        Add-Case -Id 'FC-03' -Arguments @('write', 'bootorder', '--order', 'Boot0002,Boot0007', '--json') -ExpectedExit 7 -ExpectedCheck 'entriesExist'
        Show-Verdict -Name 'fc03-pass' -ExitCode 7 -Document 'fc03.json' -Stderr 'empty.txt'
        Show-Verdict -Name 'fc03-other-check' -ExitCode 7 -Document 'fc03-permutation.json' -Stderr 'empty.txt'
        Show-Verdict -Name 'fc03-api-called' -ExitCode 7 -Document 'fc03-api-called.json' -Stderr 'empty.txt'
        Show-Verdict -Name 'fc03-wrong-exit' -ExitCode 0 -Document 'fc03.json' -Stderr 'empty.txt'

        Add-Case -Id 'FC-27a' -Arguments @('write', 'bootorder', '--order', 'Boot0002', '--dry-run', '--force', '--json') -ExpectedExit 2 -ExpectedCheck ''
        Show-Verdict -Name 'fc27-parse-error' -ExitCode 2 -Document 'usage-error.json' -Stderr 'empty.txt'
        Show-Verdict -Name 'fc27-parsed' -ExitCode 7 -Document 'fc03.json' -Stderr 'empty.txt'

        $since = [DateTimeOffset]::ParseExact('2026-09-30T12:15:02Z', "yyyy-MM-dd'T'HH:mm:ss'Z'", $Invariant, [System.Globalization.DateTimeStyles]'AssumeUniversal')
        $log = Get-LogRecords -Document (Read-JsonDocument -Path (Join-Path $folder 'write-log.json')).Document
        Write-Output ('FOUND:' + $log.Found + ':' + @($log.Records).Count)
        Write-Output ('RUN-TEXT:' + ((@(Select-RunRecord -Records $log.Records -Since $since) | ForEach-Object { [string](Get-JsonProperty -Object $_ -Name 'transactionId') }) -join ','))
        $utc = [DateTime]::SpecifyKind((New-Object DateTime(2026, 9, 30, 12, 15, 2)), [System.DateTimeKind]::Utc)
        $timed = @(
            (New-Object PSObject -Property @{ timestampUtc = $utc.AddSeconds(-1); transactionId = 'dt-before'; host = 'cli' }),
            (New-Object PSObject -Property @{ timestampUtc = $utc.ToLocalTime(); transactionId = 'dt-local'; host = 'cli' }),
            (New-Object PSObject -Property @{ timestampUtc = [DateTime]::SpecifyKind($utc.AddSeconds(1), [System.DateTimeKind]::Unspecified); transactionId = 'dt-unspecified'; host = 'cli' }),
            (New-Object PSObject -Property @{ timestampUtc = (New-Object DateTimeOffset($utc.AddMinutes(1))); transactionId = 'dt-offset'; host = 'cli' }),
            (New-Object PSObject -Property @{ timestampUtc = $utc.AddMinutes(2); transactionId = 'dt-app'; host = 'app' })
        )
        Write-Output ('RUN-TIMES:' + ((@(Select-RunRecord -Records $timed -Since $since) | ForEach-Object { [string]$_.transactionId }) -join ','))
        Write-Output ('LOG-ERROR-KIND:' + (Get-DocumentKind -Document (Read-JsonDocument -Path (Join-Path $folder 'usage-error.json')).Document))

        function New-BootRun {
            param([string]$Document)
            return New-Object PSObject -Property @{ Step = 'boot-before'; Command = 'uefiaccess boot --json'; ExitCode = 0; Note = ''; Stdout = (Join-Path $folder $Document); Stderr = (Join-Path $folder 'empty.txt') }
        }
        $boot = Read-BootState -Run (New-BootRun -Document 'boot.json')
        Write-Output ('BOOT:' + $boot.Problem + '|' + (@($boot.Order) -join ',') + '|' + $boot.BootCurrent + '|' + $boot.BootNextPresent)
        $current = @($boot.Entries | Where-Object { [string](Get-JsonProperty -Object $_ -Name 'name') -eq $boot.BootCurrent })
        Write-Output ('DISK:' + (Get-HardDriveKey -Entry $current[0]))
        Write-Output ('WINDOWS:' + ((@($boot.Entries | Where-Object { Test-StartsWindowsBootManager -Entry $_ }) | ForEach-Object { [string](Get-JsonProperty -Object $_ -Name 'name') }) -join ','))
        Write-Output ('BOOT-BROKEN:' + (Read-BootState -Run (New-BootRun -Document 'boot-broken.json')).Problem)
        """;

    /// <summary>Parses the rig and the module with the Windows PowerShell 5.1 parser and prints the number of parse errors of each; nothing runs.</summary>
    private const string ParseHarness = """
        $ErrorActionPreference = 'Stop'
        [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
        foreach ($path in @($env:RUNFC_SCRIPT, $env:RUNFC_MODULE)) {
            $tokens = $null
            $parseErrors = $null
            $null = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$parseErrors)
            Write-Output ('PARSED:' + [System.IO.Path]::GetFileName($path) + ':' + @($parseErrors).Count)
            foreach ($parseError in @($parseErrors)) { Write-Output ('ERROR:' + $parseError.Extent.StartLineNumber + ': ' + $parseError.Message) }
        }
        """;

    /// <summary>The stand-in for uefiaccess.exe: records its arguments, prints on both streams, creates the root when asked to, and exits with the code it is given.</summary>
    private const string FakeUefiAccess = """
        @echo off
        echo %*>"%RUNFC_FAKE_MARK%"
        echo fake standard output line
        echo fake standard error line 1>&2
        if defined RUNFC_FAKE_CREATES mkdir "%RUNFC_FAKE_CREATES%"
        exit /b %RUNFC_FAKE_EXIT%
        """;

    private static readonly string ScriptPath = Path.Combine(RepositoryPaths.Root, "tools", "write-tests", "Run-FailureCases.ps1");

    private static readonly string ModulePath = Path.Combine(RepositoryPaths.Root, "tools", "common", "DataFolder.psm1");

    private static readonly SecurityIdentifier[] ScriptTrustedOwners =
    {
        new(WellKnownSidType.LocalSystemSid, null),
        new(WellKnownSidType.BuiltinAdministratorsSid, null),
        new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464"),
    };

    private readonly string _tempRoot;

    public RunFailureCasesOutputFolderTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "run-failure-cases-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    private static bool HostIsElevated => PrivilegeManager.ComputeIsElevated();

    /// <summary>The stand-in for C:\ProgramData\UefiAccess; its parent (the stand-in for ProgramData) exists.</summary>
    private string Root => Path.Combine(_tempRoot, "UefiAccess");

    private string WriteTests => Path.Combine(Root, "write-tests");

    private string OutputFolder => Path.Combine(WriteTests, RunId);

    private string FakeMark => Path.Combine(_tempRoot, "fake-uefiaccess-arguments.txt");

    public void Dispose()
    {
        TestFolders.DeleteQuietly(_tempRoot);
    }

    [WindowsPowerShellFact]
    public void MissingOutputFolder_IsCreatedWithTheToolsProtectedAccessList_AndWriteTestsIsLeftAsItIs()
    {
        Directory.CreateDirectory(WriteTests);
        string writeTestsBefore = AccessListOf(WriteTests);

        HarnessRun run = RunFolderHarness(OutputFolder, trustTestUser: true);

        Assert.Equal(string.Empty, run.Result);
        LoggingFallbackTests.AssertProtectedAccessList(new DirectoryInfo(OutputFolder).GetAccessControl());
        Assert.Equal(writeTestsBefore, AccessListOf(WriteTests));
        Assert.False(File.Exists(FakeMark), "uefiaccess must not run when the data root exists");
    }

    [WindowsPowerShellFact]
    public void MissingWriteTestsAndOutputFolders_AreCreatedProtectedOneByOne()
    {
        Directory.CreateDirectory(Root);

        HarnessRun run = RunFolderHarness(OutputFolder, trustTestUser: true);

        LoggingFallbackTests.AssertProtectedAccessList(new DirectoryInfo(WriteTests).GetAccessControl());
        if (HostIsElevated)
        {
            Assert.Equal(string.Empty, run.Result);
            LoggingFallbackTests.AssertProtectedAccessList(new DirectoryInfo(OutputFolder).GetAccessControl());
        }
        else
        {
            // The access list is real: a standard-user host may not add a folder to the
            // write-tests folder it just protected, and the sentence names the folder.
            Assert.StartsWith(OutputFolder + " cannot be created: ", run.Result, StringComparison.Ordinal);
            Assert.False(Directory.Exists(OutputFolder));
        }
    }

    [WindowsPowerShellFact]
    public void ExistingOutputFolder_IsRefusedAndLeftAsItIs()
    {
        Directory.CreateDirectory(OutputFolder);
        string planted = Path.Combine(OutputFolder, "summary.txt");
        File.WriteAllText(planted, "planted");
        string before = AccessListOf(OutputFolder);

        HarnessRun run = RunFolderHarness(OutputFolder, trustTestUser: true);

        Assert.Equal(ExistsSentence(OutputFolder), run.Result);
        Assert.Equal("planted", File.ReadAllText(planted));
        Assert.Equal(before, AccessListOf(OutputFolder));
    }

    [WindowsPowerShellFact]
    public void WriteTestsAsAJunction_IsRefusedAndNothingIsCreatedThroughIt_WhateverTheCaseOfTheRoot()
    {
        Directory.CreateDirectory(Root);
        string outside = Path.Combine(_tempRoot, "Windows-stand-in");
        Directory.CreateDirectory(outside);
        Assert.True(TestLinks.TryCreateJunction(WriteTests, outside), "mklink /J could not create the junction");

        // The output root spelled in capitals is still inside the data root, as Windows and the tool compare paths.
        HarnessRun run = RunFolderHarness(Root.ToUpperInvariant() + "\\write-tests\\" + RunId, trustTestUser: true);

        Assert.Equal(LinkSentence(WriteTests), run.Result);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
    }

    [WindowsPowerShellFact]
    public void FolderOwnedByAnotherAccount_IsRefusedWithTheOwnerAndWhatToDo()
    {
        Directory.CreateDirectory(Root);
        SecurityIdentifier? owner = OwnerOf(Root);
        Assert.NotNull(owner);

        HarnessRun run = RunFolderHarness(OutputFolder, trustTestUser: false);

        if (ScriptTrustedOwners.Contains(owner))
        {
            // An elevated host creates folders owned by Administrators, which the rule trusts; the
            // run then goes on to create the write-tests and output folders.
            Assert.Equal(string.Empty, run.Result);
            LoggingFallbackTests.AssertProtectedAccessList(new DirectoryInfo(OutputFolder).GetAccessControl());
        }
        else
        {
            string account = owner.Translate(typeof(NTAccount)).Value;
            Assert.Equal(
                $"{Root} is owned by {account}, not by Administrators or SYSTEM, so a standard user could redirect what this elevated script writes there; rename or delete {Root} from an administrator prompt and run this script again.",
                run.Result);
            Assert.False(Directory.Exists(WriteTests));
        }
    }

    [WindowsPowerShellFact]
    public void MissingDataRoot_IsLeftToUefiAccessToCreate_AndNothingItPrintsIsShownOrKept()
    {
        // The rig's first run on a machine: the data root does not exist yet.
        string folder = Path.Combine(Root, RunId);

        HarnessRun run = RunFolderHarness(folder, trustTestUser: true, fakeCreatesRoot: true, fakeExitCode: 0);

        Assert.Equal(string.Empty, run.Result);
        Assert.Equal("info", File.ReadAllText(FakeMark).Trim());
        Assert.Contains($"The data folder {Root} does not exist yet: \"uefiaccess info\" runs once first, so that the tool creates it with its protected access list; the output of that run is not kept.", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("fake standard", run.Output + run.Error, StringComparison.Ordinal);
        LoggingFallbackTests.AssertProtectedAccessList(new DirectoryInfo(folder).GetAccessControl());
    }

    [WindowsPowerShellFact]
    public void AFolderThatCannotBeCreated_EndsInTheSentence_WhateverErrorPreferenceTheCallerSets()
    {
        // A module does not see the preference variables of the script that imports it, so the
        // module stops on errors by its own setting: a folder New-Item cannot create must end in
        // the sentence, never in an error record the caller lets pass and an empty result, which
        // would mean "created".
        string file = Path.Combine(_tempRoot, "a-file-in-the-way.txt");
        File.WriteAllText(file, "in the way");
        string folder = Path.Combine(file, "write-tests", RunId);

        HarnessRun run = RunFolderHarness(folder, trustTestUser: false, callerPreference: "Continue");

        Assert.StartsWith($"The output folder {folder} cannot be created: ", run.Result, StringComparison.Ordinal);
        Assert.Equal("in the way", File.ReadAllText(file));
        Assert.False(Directory.Exists(folder));
    }

    [WindowsPowerShellFact]
    public void TheModuleExportsTheFolderMakerAndThePathHelpersOnly()
    {
        Directory.CreateDirectory(WriteTests);

        HarnessRun run = RunFolderHarness(OutputFolder, trustTestUser: true);

        Assert.Equal(
            new[] { "Get-EntryAttributes", "Get-FullPathWithoutSeparator", "New-OutputFolder", "Test-IsSameOrInside" },
            run.Exported.Split(',', StringSplitOptions.RemoveEmptyEntries).OrderBy(name => name, StringComparer.Ordinal));
    }

    [WindowsPowerShellFact]
    public void TheRigAndTheModuleParseInWindowsPowerShell51()
    {
        PowerShellRun run = RunPowerShell(ParseHarness, new Dictionary<string, string?>());

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("PARSED:Run-FailureCases.ps1:0", run.Output, StringComparison.Ordinal);
        Assert.Contains("PARSED:DataFolder.psm1:0", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR:", run.Output, StringComparison.Ordinal);
    }

    [WindowsPowerShellFact]
    public void TheElevationCheckIsTheRigsFirstStatement_AndStopsAStandardPromptWithExitCodeFour()
    {
        PowerShellRun run = RunPowerShell(ElevationHarness, new Dictionary<string, string?>());

        Assert.Contains("FIRST:if (-not (Test-IsElevated)) {", run.Output, StringComparison.Ordinal);
        if (HostIsElevated)
        {
            Assert.Equal(0, run.ExitCode);
            Assert.Contains("WENT-ON", run.Output, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(4, run.ExitCode);
            Assert.Contains("This script must run from an elevated prompt (Run as administrator)", run.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("WENT-ON", run.Output, StringComparison.Ordinal);
        }
    }

    [WindowsPowerShellFact]
    public void TheRigsVerdictAndLogFunctions_JudgeCannedRunsAsTheMatrixSays()
    {
        string cases = Path.Combine(_tempRoot, "cases");
        Directory.CreateDirectory(cases);

        // Standard error of FC-15 on the unit, in the CLI's own words and order.
        string[] fc15Error =
        {
            "Warning: Secure Boot: on, user mode. This application never changes it.",
            CheckSentences.Format(WriteCommand.BitLockerStateLineFormat, "BitLocker protects drive C: with the TPM and checks the boot path (PCR 0, 2, 4, 11; TPM 1.2)."),
            CheckSentences.Format(WriteCommand.RiskLineFormat, CheckSentences.Format(BitLockerPreflight.BootNextRiskFormat, "Boot0002")),
            CheckSentences.Format(WriteCommand.RecoveryKeyLineFormat, BitLockerPreflight.RecoveryKeySentence),
            WriteCommand.BitLockerHint,
            CheckSentences.BitLockerChoiceMissing,
            WriteCommand.DryRunSentence,
        };
        Dictionary<string, string> files = new(StringComparer.Ordinal)
        {
            ["empty.txt"] = string.Empty,
            ["fc15-complete.txt"] = string.Join("\r\n", fc15Error) + "\r\n",
            ["fc15-no-hint.txt"] = string.Join("\r\n", fc15Error.Where(line => line != WriteCommand.BitLockerHint)) + "\r\n",
            ["fc02-both.json"] = CheckDocument(authenticatedWritePassed: false),
            ["fc02-whitelist-only.json"] = CheckDocument(authenticatedWritePassed: true),
            ["fc15-offered.json"] = BitLockerDocument(suspendOffered: true),
            ["fc15-not-offered.json"] = BitLockerDocument(suspendOffered: false),
            ["fc15-off.json"] = """
                {"command":"write","operation":"SetBootNext","ok":true,"exitCode":0,"blockedBy":null,"apiCalled":false,"checks":[{"name":"bitLocker","passed":true,"reason":"BitLocker protection is off on C:."}],"bitLocker":{"state":"Off","suspendOffered":false},"record":{"transactionId":"20260930-141800-bootnext","host":"cli","apiCalled":false}}
                """,
            ["fc03.json"] = ReorderDocument("entriesExist", apiCalled: false),
            ["fc03-permutation.json"] = ReorderDocument("permutation", apiCalled: false),
            ["fc03-api-called.json"] = ReorderDocument("entriesExist", apiCalled: true),
            ["usage-error.json"] = """
                {"command":"write","ok":false,"exitCode":2,"error":"Unrecognized command or argument '--force'.","firmwareType":null,"elevated":null,"privilegeState":null}
                """,
            ["write-log.json"] = """
                {"command":"write log","ok":true,"file":"write-log.jsonl","total":5,"records":[
                {"timestampUtc":"2026-09-30T12:15:01Z","transactionId":"before-start","host":"cli","apiCalled":false},
                {"timestampUtc":"2026-09-30T12:15:02Z","transactionId":"at-start","host":"cli","apiCalled":false},
                {"timestampUtc":"2026-09-30T12:15:03Z","transactionId":"from-the-app","host":"app","apiCalled":false},
                {"timestampUtc":"2026-09-30T12:16:00Z","transactionId":"after-start","host":"cli","apiCalled":false},
                {"timestampUtc":"not a time","transactionId":"unreadable-time","host":"cli","apiCalled":false}]}
                """,

            // The fields of "boot --json" the rig reads to build the case lines: the order, BootCurrent,
            // BootNext, and each entry's flags and device path nodes (FC-16 looks for a Windows Boot
            // Manager on another partition than BootCurrent's).
            ["boot.json"] = """
                {"command":"boot","ok":true,
                "bootCurrent":{"present":true,"index":2,"name":"Boot0002","description":"Windows Boot Manager","entryExists":true,"error":null,"note":null},
                "bootNext":{"present":false,"index":null,"name":null,"description":null,"entryExists":false,"error":null,"note":null},
                "bootOrderPresent":true,
                "bootOrder":[{"position":1,"index":2,"name":"Boot0002","entryExists":true,"description":"Windows Boot Manager","note":null}],
                "entries":[
                {"name":"Boot0000","active":true,"category":"Boot","devicePathNodes":["HD(2,GPT,11111111-2222-3333-4444-555555555555,0x96800,0x32000)","File(\\EFI\\Microsoft\\Boot\\bootmgfw.efi)"]},
                {"name":"Boot0002","active":true,"category":"Boot","devicePathNodes":["HD(1,GPT,6F6E5D1C-8F7A-4D33-9A2B-3C4D5E6F7081,0x800,0x32000)","File(\\EFI\\Microsoft\\Boot\\bootmgfw.efi)"]},
                {"name":"Boot0003","active":true,"category":"Boot","devicePathNodes":["HD(1,GPT,6f6e5d1c-8f7a-4d33-9a2b-3c4d5e6f7081,0x800,0x32000)","File(/efi/microsoft/boot//BOOTMGFW.EFI)"]},
                {"name":"Boot0005","active":true,"category":"Boot","devicePathNodes":["HD(1,GPT,6f6e5d1c-8f7a-4d33-9a2b-3c4d5e6f7081,0x800,0x32000)","File(\\EFI\\Boot\\bootx64.efi)"]}]}
                """,
            ["boot-broken.json"] = """
                {"command":"boot","ok":true,
                "bootCurrent":{"present":true,"index":2,"name":"Boot0002","description":"Windows Boot Manager","entryExists":true,"error":null,"note":null},
                "bootNext":{"present":false,"index":null,"name":null,"description":null,"entryExists":false,"error":null,"note":null},
                "bootOrderPresent":true,
                "bootOrder":[{"position":1,"index":2,"name":"Boot0002","entryExists":true,"description":"Windows Boot Manager","note":null},{"position":2,"index":7,"name":"Boot0007","entryExists":false,"description":null,"note":"missing"}],
                "entries":[{"name":"Boot0002","active":true,"category":"Boot","devicePathNodes":["HD(1,GPT,6f6e5d1c-8f7a-4d33-9a2b-3c4d5e6f7081,0x800,0x32000)","File(\\EFI\\Microsoft\\Boot\\bootmgfw.efi)"]}]}
                """,
        };
        foreach ((string name, string text) in files)
        {
            File.WriteAllText(Path.Combine(cases, name), text, new UTF8Encoding(false));
        }

        PowerShellRun run = RunPowerShell(VerdictHarness, new Dictionary<string, string?> { ["RUNFC_CASES"] = cases });

        Assert.True(run.ExitCode == 0, $"the harness failed (exit code {run.ExitCode}):{Environment.NewLine}{run.Output}{Environment.NewLine}{run.Error}");
        string[] lines = run.Output.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        Dictionary<string, string> verdicts = lines
            .Where(line => line.StartsWith("VERDICT:", StringComparison.Ordinal))
            .Select(line => line.Substring("VERDICT:".Length))
            .ToDictionary(line => line.Substring(0, line.IndexOf(':', StringComparison.Ordinal)), line => line.Substring(line.IndexOf(':', StringComparison.Ordinal) + 1), StringComparer.Ordinal);

        // FC-02: the authenticatedWrite check must have failed too.
        Assert.Equal("PASS", verdicts["fc02-both-failed"]);
        Assert.Equal("FAIL (checks does not show " + CheckNames.AuthenticatedWrite + " failed)", verdicts["fc02-authenticated-passed"]);

        // FC-15: suspendOffered, the three BitLocker lines and the hint; NOT VERIFIED is never PASS.
        Assert.Equal("PASS", verdicts["fc15-complete"]);
        Assert.Equal("FAIL (standard error has no line that starts with \"" + WriteCommand.BitLockerHint + "\")", verdicts["fc15-no-hint"]);
        Assert.Equal("FAIL (bitLocker.suspendOffered is not true)", verdicts["fc15-not-offered"]);
        Assert.StartsWith("FAIL (standard error has no line that starts with \"BitLocker state:\"; ", verdicts["fc15-no-stderr"], StringComparison.Ordinal);
        Assert.Equal("NOT VERIFIED (BitLocker was Off when the script ran; the line gave exit code 0, blockedBy (none))", verdicts["fc15-off"]);

        // The exit code, blockedBy and apiCalled of any case, and the parse-error row.
        Assert.Equal("PASS", verdicts["fc03-pass"]);
        Assert.Equal("FAIL (blockedBy permutation, expected entriesExist)", verdicts["fc03-other-check"]);
        Assert.Equal("FAIL (apiCalled is true: the firmware was called)", verdicts["fc03-api-called"]);
        Assert.Equal("FAIL (exit code 0, expected 7)", verdicts["fc03-wrong-exit"]);
        Assert.Equal("PASS", verdicts["fc27-parse-error"]);
        Assert.Equal("FAIL (exit code 7, expected 2; the line was parsed and reached the write pipeline)", verdicts["fc27-parsed"]);

        // The records of the run: host cli, at or after the start in whole seconds, compared as
        // times whether ConvertFrom-Json left the text (5.1) or made a DateTime or DateTimeOffset
        // (PowerShell 7); a time that does not read is left out.
        Assert.Contains("FOUND:True:5", lines);
        Assert.Contains("RUN-TEXT:at-start,after-start", lines);
        Assert.Contains("RUN-TIMES:dt-local,dt-unspecified,dt-offset", lines);
        Assert.Contains("LOG-ERROR-KIND:error", lines);

        // The machine read before the cases: the order, BootCurrent's partition (lowercase), and
        // the entries whose file is the Windows Boot Manager, compared as rule V13 compares it.
        Assert.Contains("BOOT:|Boot0002|Boot0002|False", lines);
        Assert.Contains("DISK:1,gpt,6f6e5d1c-8f7a-4d33-9a2b-3c4d5e6f7081", lines);
        Assert.Contains("WINDOWS:Boot0000,Boot0002,Boot0003", lines);
        Assert.Contains(lines, line => line.StartsWith("BOOT-BROKEN:BootOrder names an entry that does not exist", StringComparison.Ordinal));
    }

    /// <summary>A "write check PK" document blocked at the whitelist, with authenticatedWrite failed or passed.</summary>
    private static string CheckDocument(bool authenticatedWritePassed) =>
        "{\"command\":\"write\",\"operation\":\"Check\",\"ok\":false,\"exitCode\":7,\"blockedBy\":\"whitelist\",\"apiCalled\":false,"
        + "\"checks\":[{\"name\":\"preconditions\",\"passed\":true,\"reason\":\"\"},{\"name\":\"whitelist\",\"passed\":false,\"reason\":\"PK cannot be changed by this application.\"},"
        + "{\"name\":\"authenticatedWrite\",\"passed\":" + (authenticatedWritePassed ? "true" : "false") + ",\"reason\":\"\"}],"
        + "\"bitLocker\":null,\"record\":{\"transactionId\":\"20260930-141503-check\",\"host\":\"cli\",\"apiCalled\":false}}";

    /// <summary>A "write bootnext Boot0002" document blocked at the BitLocker check, with suspendOffered as given.</summary>
    private static string BitLockerDocument(bool suspendOffered)
    {
        string offered = suspendOffered ? "true" : "false";
        return "{\"command\":\"write\",\"operation\":\"SetBootNext\",\"ok\":false,\"exitCode\":7,\"blockedBy\":\"bitLocker\",\"apiCalled\":false,"
            + "\"checks\":[{\"name\":\"bitLocker\",\"passed\":false,\"reason\":\"BitLocker protection is on and no choice was made about it; nothing is written.\"}],"
            + "\"bitLocker\":{\"state\":\"On\",\"suspendOffered\":" + offered + "},"
            + "\"record\":{\"transactionId\":\"20260930-141733-bootnext\",\"host\":\"cli\",\"apiCalled\":false,\"bitLocker\":{\"state\":\"On\",\"suspendOffered\":" + offered + "}}}";
    }

    /// <summary>A "write bootorder" document blocked at <paramref name="blockedBy"/>, with apiCalled as given.</summary>
    private static string ReorderDocument(string blockedBy, bool apiCalled)
    {
        string called = apiCalled ? "true" : "false";
        return "{\"command\":\"write\",\"operation\":\"Reorder\",\"ok\":false,\"exitCode\":7,\"blockedBy\":\"" + blockedBy + "\",\"apiCalled\":" + called + ","
            + "\"checks\":[{\"name\":\"" + blockedBy + "\",\"passed\":false,\"reason\":\"refused\"}],"
            + "\"record\":{\"transactionId\":\"20260930-141504-reorder\",\"host\":\"cli\",\"apiCalled\":" + called + "}}";
    }

    private static string ExistsSentence(string folder) =>
        $"The output folder {folder} already exists, and this script writes only into a new one; rename or delete it from an administrator prompt and run this script again.";

    private static string LinkSentence(string folder) =>
        $"{folder} is a junction, symbolic link or other reparse point, not a plain folder, so a standard user could redirect what this elevated script writes there; rename or delete {folder} from an administrator prompt and run this script again.";

    private static string AccessListOf(string folder) =>
        new DirectoryInfo(folder).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);

    private static SecurityIdentifier? OwnerOf(string folder) =>
        new DirectoryInfo(folder).GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;

    /// <summary>Runs <see cref="FolderHarness"/> for <paramref name="folder"/> under the stand-in data root.</summary>
    private HarnessRun RunFolderHarness(string folder, bool trustTestUser, bool fakeCreatesRoot = false, int fakeExitCode = 0, string callerPreference = "Stop")
    {
        string fake = Path.Combine(_tempRoot, "fake-uefiaccess.cmd");
        // A batch file wants CRLF line ends, whatever the ends of this source file are.
        string batch = FakeUefiAccess.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal) + "\r\n";
        File.WriteAllText(fake, batch, Encoding.ASCII);

        Dictionary<string, string?> environment = new(StringComparer.Ordinal)
        {
            ["RUNFC_FOLDER"] = folder,
            ["RUNFC_DATA_ROOT"] = Root,
            ["RUNFC_EXE"] = fake,
            ["RUNFC_TRUST_TEST_USER"] = trustTestUser ? "1" : "0",
            ["RUNFC_FAKE_MARK"] = FakeMark,
            ["RUNFC_FAKE_CREATES"] = fakeCreatesRoot ? Root : null,
            ["RUNFC_FAKE_EXIT"] = fakeExitCode.ToString(CultureInfo.InvariantCulture),
            ["RUNFC_CALLER_PREFERENCE"] = callerPreference,
        };
        PowerShellRun run = RunPowerShell(FolderHarness, environment);
        Assert.True(run.ExitCode == 0, $"the harness failed (exit code {run.ExitCode}):{Environment.NewLine}{run.Output}{Environment.NewLine}{run.Error}");

        string[] lines = run.Output.Split('\n').Select(candidate => candidate.TrimEnd('\r')).ToArray();
        string exported = Assert.Single(lines, candidate => candidate.StartsWith("EXPORTED:", StringComparison.Ordinal));
        string line = Assert.Single(lines, candidate => candidate.StartsWith("RESULT(", StringComparison.Ordinal));
        Assert.StartsWith("RESULT(1):", line, StringComparison.Ordinal);
        return new HarnessRun(line.Substring("RESULT(1):".Length), exported.Substring("EXPORTED:".Length), run.Output, run.Error);
    }

    /// <summary>
    /// Runs <paramref name="script"/> in Windows PowerShell 5.1 with the rig and the module named in
    /// the environment beside <paramref name="environment"/> (a null value removes the variable).
    /// </summary>
    private PowerShellRun RunPowerShell(string script, IReadOnlyDictionary<string, string?> environment)
    {
        ProcessStartInfo start = new(WindowsPowerShellFactAttribute.PowerShellPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = _tempRoot,
        };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
        {
            start.ArgumentList.Add(argument);
        }

        // Windows PowerShell 5.1 builds its own module path when none is given. A test run started
        // from PowerShell 7 would hand it PowerShell 7's, whose Get-Acl fails in 5.1 ("The owner of
        // ... cannot be read").
        start.Environment.Remove("PSModulePath");
        start.Environment["RUNFC_SCRIPT"] = ScriptPath;
        start.Environment["RUNFC_MODULE"] = ModulePath;
        foreach ((string name, string? value) in environment)
        {
            if (value is null)
            {
                start.Environment.Remove(name);
            }
            else
            {
                start.Environment[name] = value;
            }
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("powershell.exe did not start");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("Windows PowerShell did not finish the harness within two minutes.");
        }

        return new PowerShellRun(process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }

    /// <summary>What New-OutputFolder returned, the functions the module exports, and everything the harness printed.</summary>
    private sealed record HarnessRun(string Result, string Exported, string Output, string Error);

    /// <summary>The exit code of a Windows PowerShell run and what it printed on each stream.</summary>
    private sealed record PowerShellRun(int ExitCode, string Output, string Error);
}
