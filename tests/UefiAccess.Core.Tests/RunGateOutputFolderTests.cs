// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using UefiAccess.Core.Privileges;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// How tools/gate/Run-Gate.ps1 makes its output folder, run in Windows PowerShell 5.1 against a
/// temporary stand-in for C:\ProgramData\UefiAccess. The script itself needs an elevated prompt on
/// the target machine, so only its output-folder functions run: they come from the shared module
/// tools/common/DataFolder.psm1, which the script imports, with the trusted owners and the access
/// list the script declares (taken from its syntax tree), and New-OutputFolder is called once per
/// test. Inside the data root it follows the tool's rule
/// (Logging.PrepareDataFolderFor): a missing root is left to uefiaccess to create (a stand-in
/// .cmd plays uefiaccess.exe here), every existing folder must be a plain folder with a trusted
/// owner, a missing one is created with the tool's protected access list, and the output folder
/// must be new. Folder owners cannot be changed without elevation, so the test host's own account
/// is added to the trusted owners where a test is not about the owner; where the outcome depends
/// on whether the host is elevated, both outcomes are asserted precisely.
/// </summary>
[Collection(WindowsPowerShellCollection.Name)]
public sealed class RunGateOutputFolderTests : IDisposable
{
    private const string RunId = "20260922-101500";

    /// <summary>
    /// Imports the shared module, loads the two declarations from the script's syntax tree
    /// (nothing else in the script runs), calls New-OutputFolder with the folders from the
    /// environment, and prints its result with the number of objects it returned, which must be
    /// one string.
    /// </summary>
    private const string Harness = """
        Set-StrictMode -Version 2.0
        $ErrorActionPreference = 'Stop'
        [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
        Import-Module -Name $env:RUNGATE_MODULE
        $tokens = $null
        $parseErrors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($env:RUNGATE_SCRIPT, [ref]$tokens, [ref]$parseErrors)
        if (@($parseErrors).Count -gt 0) { throw ('Run-Gate.ps1 does not parse: ' + (@($parseErrors) -join '; ')) }
        $declarations = @($ast.EndBlock.Statements | Where-Object { $_ -is [System.Management.Automation.Language.AssignmentStatementAst] -and $_.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and @('TrustedOwnerSids', 'ProtectedFolderSddl') -contains $_.Left.VariablePath.UserPath })
        if ($declarations.Count -ne 2) { throw 'Run-Gate.ps1 does not declare $TrustedOwnerSids and $ProtectedFolderSddl' }
        . ([scriptblock]::Create((@($declarations) | ForEach-Object { $_.Extent.Text }) -join [Environment]::NewLine))
        $trusted = @($TrustedOwnerSids)
        if ($env:RUNGATE_TRUST_TEST_USER -eq '1') { $trusted += [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value }
        $result = New-OutputFolder -Folder $env:RUNGATE_FOLDER -DataRoot $env:RUNGATE_DATA_ROOT -Exe $env:RUNGATE_EXE -TrustedOwners $trusted -Sddl $ProtectedFolderSddl
        Write-Output ('RESULT(' + @($result).Count + '):' + (@($result) -join '|'))
        """;

    /// <summary>The stand-in for uefiaccess.exe: records its arguments, prints on both streams, creates the root when asked to, and exits with the code it is given.</summary>
    private const string FakeUefiAccess = """
        @echo off
        echo %*>"%RUNGATE_FAKE_MARK%"
        echo fake standard output line
        echo fake standard error line 1>&2
        if defined RUNGATE_FAKE_CREATES mkdir "%RUNGATE_FAKE_CREATES%"
        exit /b %RUNGATE_FAKE_EXIT%
        """;

    private static readonly string ScriptPath = Path.Combine(RepositoryPaths.Root, "tools", "gate", "Run-Gate.ps1");

    private static readonly string ModulePath = Path.Combine(RepositoryPaths.Root, "tools", "common", "DataFolder.psm1");

    private static readonly SecurityIdentifier[] ScriptTrustedOwners =
    {
        new(WellKnownSidType.LocalSystemSid, null),
        new(WellKnownSidType.BuiltinAdministratorsSid, null),
        new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464"),
    };

    private readonly string _tempRoot;

    public RunGateOutputFolderTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "run-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    private static bool HostIsElevated => PrivilegeManager.ComputeIsElevated();

    /// <summary>The stand-in for C:\ProgramData\UefiAccess; its parent (the stand-in for ProgramData) exists.</summary>
    private string Root => Path.Combine(_tempRoot, "UefiAccess");

    private string Gate => Path.Combine(Root, "gate");

    private string OutputFolder => Path.Combine(Gate, RunId);

    private string FakeMark => Path.Combine(_tempRoot, "fake-uefiaccess-arguments.txt");

    public void Dispose()
    {
        TestFolders.DeleteQuietly(_tempRoot);
    }

    [WindowsPowerShellFact]
    public void MissingOutputFolder_IsCreatedWithTheToolsProtectedAccessList_AndTheFoldersAboveAreLeftAsTheyAre()
    {
        Directory.CreateDirectory(Gate);
        string gateBefore = AccessListOf(Gate);

        HarnessRun run = Run(OutputFolder, trustTestUser: true);

        Assert.Equal(string.Empty, run.Result);
        LoggingFallbackTests.AssertProtectedAccessList(new DirectoryInfo(OutputFolder).GetAccessControl());
        Assert.Equal(gateBefore, AccessListOf(Gate));
        Assert.False(File.Exists(FakeMark), "uefiaccess must not run when the data root exists");
    }

    [WindowsPowerShellFact]
    public void MissingGateAndOutputFolders_AreCreatedProtectedOneByOne()
    {
        Directory.CreateDirectory(Root);

        HarnessRun run = Run(OutputFolder, trustTestUser: true);

        LoggingFallbackTests.AssertProtectedAccessList(new DirectoryInfo(Gate).GetAccessControl());
        if (HostIsElevated)
        {
            Assert.Equal(string.Empty, run.Result);
            LoggingFallbackTests.AssertProtectedAccessList(new DirectoryInfo(OutputFolder).GetAccessControl());
        }
        else
        {
            // The access list is real: a standard-user host may not add a folder to the gate
            // folder it just protected, and the sentence names the folder that could not be made.
            Assert.StartsWith(OutputFolder + " cannot be created: ", run.Result, StringComparison.Ordinal);
            Assert.False(Directory.Exists(OutputFolder));
        }
    }

    [WindowsPowerShellFact]
    public void ExistingOutputFolder_IsRefusedAndLeftAsItIs()
    {
        Directory.CreateDirectory(OutputFolder);
        string planted = Path.Combine(OutputFolder, "gate.json");
        File.WriteAllText(planted, "planted");
        string before = AccessListOf(OutputFolder);

        HarnessRun run = Run(OutputFolder, trustTestUser: true);

        Assert.Equal(ExistsSentence(OutputFolder), run.Result);
        Assert.Equal("planted", File.ReadAllText(planted));
        Assert.Equal(before, AccessListOf(OutputFolder));
    }

    [WindowsPowerShellFact]
    public void JunctionOnTheWay_IsRefusedAndNothingIsCreatedThroughIt_WhateverTheCaseOfTheRoot()
    {
        Directory.CreateDirectory(Root);
        string outside = Path.Combine(_tempRoot, "Windows-stand-in");
        Directory.CreateDirectory(outside);
        Assert.True(TestLinks.TryCreateJunction(Gate, outside), "mklink /J could not create the junction");

        // The output root spelled in capitals is still inside the data root, as Windows and the tool compare paths.
        HarnessRun run = Run(Root.ToUpperInvariant() + "\\gate\\" + RunId, trustTestUser: true);

        Assert.Equal(LinkSentence(Gate), run.Result);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
    }

    [WindowsPowerShellFact]
    public void DataRootIsAJunction_IsRefused()
    {
        string outside = Path.Combine(_tempRoot, "outside-root");
        Directory.CreateDirectory(outside);
        Assert.True(TestLinks.TryCreateJunction(Root, outside), "mklink /J could not create the junction");

        HarnessRun run = Run(OutputFolder, trustTestUser: true);

        Assert.Equal(LinkSentence(Root), run.Result);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
        Assert.False(File.Exists(FakeMark), "uefiaccess must not run when the data root exists, even as a link");
    }

    [WindowsPowerShellFact]
    public void FolderOwnedByAnotherAccount_IsRefusedWithTheOwnerAndWhatToDo()
    {
        Directory.CreateDirectory(Root);
        SecurityIdentifier owner = OwnerOf(Root);

        HarnessRun run = Run(OutputFolder, trustTestUser: false);

        if (ScriptTrustedOwners.Contains(owner))
        {
            // An elevated host creates folders owned by Administrators, which the rule trusts; the
            // run then goes on to create the gate and output folders.
            Assert.Equal(string.Empty, run.Result);
            LoggingFallbackTests.AssertProtectedAccessList(new DirectoryInfo(OutputFolder).GetAccessControl());
        }
        else
        {
            string account = owner.Translate(typeof(NTAccount)).Value;
            Assert.Equal(
                $"{Root} is owned by {account}, not by Administrators or SYSTEM, so a standard user could redirect what this elevated script writes there; rename or delete {Root} from an administrator prompt and run this script again.",
                run.Result);
            Assert.False(Directory.Exists(Gate));
        }
    }

    [WindowsPowerShellFact]
    public void MissingDataRoot_IsLeftToUefiAccessToCreate_AndNothingItPrintsIsShownOrKept()
    {
        string folder = Path.Combine(Root, RunId);

        HarnessRun run = Run(folder, trustTestUser: true, fakeCreatesRoot: true, fakeExitCode: 0);

        Assert.Equal(string.Empty, run.Result);
        Assert.Equal("info", File.ReadAllText(FakeMark).Trim());
        Assert.Contains($"The data folder {Root} does not exist yet: \"uefiaccess info\" runs once first, so that the tool creates it with its protected access list; the output of that run is not kept.", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("fake standard", run.Output + run.Error, StringComparison.Ordinal);
        Assert.True(Directory.Exists(Root));
        LoggingFallbackTests.AssertProtectedAccessList(new DirectoryInfo(folder).GetAccessControl());
    }

    [WindowsPowerShellFact]
    public void MissingDataRoot_ThatUefiAccessDoesNotCreate_StopsWithTheExitCode()
    {
        HarnessRun run = Run(OutputFolder, trustTestUser: true, fakeCreatesRoot: false, fakeExitCode: 5);

        Assert.Equal(
            $"\"uefiaccess info\" (exit code 5) did not create the data folder {Root}, so the output folder cannot be created safely; run \"uefiaccess info\" from this elevated prompt to see why, then run this script again.",
            run.Result);
        Assert.Equal("info", File.ReadAllText(FakeMark).Trim());
        Assert.False(Directory.Exists(Root));
    }

    [WindowsPowerShellFact]
    public void MissingDataRootWithoutAUefiAccessToRun_StopsWithTheSentence()
    {
        string missingExe = Path.Combine(_tempRoot, "no-such-uefiaccess.exe");

        HarnessRun run = Run(OutputFolder, trustTestUser: true, exe: missingExe);

        Assert.Equal(
            $"\"uefiaccess info\" (exit code -1) did not create the data folder {Root}, so the output folder cannot be created safely; run \"uefiaccess info\" from this elevated prompt to see why, then run this script again.",
            run.Result);
        Assert.False(Directory.Exists(Root));
    }

    [WindowsPowerShellFact]
    public void OutsideTheDataRoot_OnlyANewFolderIsRequired()
    {
        AssertOutsideTheDataRootIsOnlyCreated(Path.Combine(_tempRoot, "work", "gate", RunId));
    }

    [WindowsPowerShellFact]
    public void SiblingWhoseNameStartsLikeTheDataRoot_IsOutsideIt()
    {
        AssertOutsideTheDataRootIsOnlyCreated(Path.Combine(_tempRoot, "UefiAccessOther", RunId));
    }

    [WindowsPowerShellFact]
    public void OutsideTheDataRoot_AnExistingFolderIsRefused()
    {
        string folder = Path.Combine(_tempRoot, "work", RunId);
        Directory.CreateDirectory(folder);

        HarnessRun run = Run(folder, trustTestUser: false);

        Assert.Equal(ExistsSentence(folder), run.Result);
    }

    /// <summary>A folder outside the data root is the operator's choice: no owner check, no uefiaccess run, missing parents created.</summary>
    private void AssertOutsideTheDataRootIsOnlyCreated(string folder)
    {
        HarnessRun run = Run(folder, trustTestUser: false, fakeCreatesRoot: true, fakeExitCode: 0);

        Assert.Equal(string.Empty, run.Result);
        Assert.True(Directory.Exists(folder));
        Assert.False(Directory.Exists(Root));
        Assert.False(File.Exists(FakeMark), "uefiaccess must not run for a folder outside the data root");
    }

    private static string ExistsSentence(string folder) =>
        $"The output folder {folder} already exists, and this script writes only into a new one; rename or delete it from an administrator prompt and run this script again.";

    private static string LinkSentence(string folder) =>
        $"{folder} is a junction, symbolic link or other reparse point, not a plain folder, so a standard user could redirect what this elevated script writes there; rename or delete {folder} from an administrator prompt and run this script again.";

    private static string AccessListOf(string folder) =>
        new DirectoryInfo(folder).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);

    private static SecurityIdentifier OwnerOf(string folder) =>
        (SecurityIdentifier)new DirectoryInfo(folder).GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier))!;

    /// <summary>Runs <see cref="Harness"/> in Windows PowerShell 5.1 for <paramref name="folder"/> under the stand-in data root.</summary>
    private HarnessRun Run(string folder, bool trustTestUser, bool fakeCreatesRoot = false, int fakeExitCode = 0, string? exe = null)
    {
        string fake = Path.Combine(_tempRoot, "fake-uefiaccess.cmd");
        // A batch file wants CRLF line ends, whatever the ends of this source file are.
        string batch = FakeUefiAccess.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal) + "\r\n";
        File.WriteAllText(fake, batch, Encoding.ASCII);

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
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(Harness)) })
        {
            start.ArgumentList.Add(argument);
        }

        // Windows PowerShell 5.1 builds its own module path when none is given. A test run started
        // from PowerShell 7 would hand it PowerShell 7's, whose Get-Acl fails in 5.1 ("The owner of
        // ... cannot be read").
        start.Environment.Remove("PSModulePath");
        start.Environment["RUNGATE_SCRIPT"] = ScriptPath;
        start.Environment["RUNGATE_MODULE"] = ModulePath;
        start.Environment["RUNGATE_FOLDER"] = folder;
        start.Environment["RUNGATE_DATA_ROOT"] = Root;
        start.Environment["RUNGATE_EXE"] = exe ?? fake;
        start.Environment["RUNGATE_TRUST_TEST_USER"] = trustTestUser ? "1" : "0";
        start.Environment["RUNGATE_FAKE_MARK"] = FakeMark;
        if (fakeCreatesRoot)
        {
            start.Environment["RUNGATE_FAKE_CREATES"] = Root;
        }
        else
        {
            start.Environment.Remove("RUNGATE_FAKE_CREATES");
        }

        start.Environment["RUNGATE_FAKE_EXIT"] = fakeExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("powershell.exe did not start");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("Windows PowerShell did not finish the harness within two minutes.");
        }

        string outputText = output.GetAwaiter().GetResult();
        string errorText = error.GetAwaiter().GetResult();
        Assert.True(process.ExitCode == 0, $"the harness failed (exit code {process.ExitCode}):{Environment.NewLine}{outputText}{Environment.NewLine}{errorText}");

        string line = Assert.Single(outputText.Split('\n').Select(candidate => candidate.TrimEnd('\r')), candidate => candidate.StartsWith("RESULT(", StringComparison.Ordinal));
        Assert.StartsWith("RESULT(1):", line, StringComparison.Ordinal);
        return new HarnessRun(line.Substring("RESULT(1):".Length), outputText, errorText);
    }

    /// <summary>What New-OutputFolder returned, and everything the harness printed.</summary>
    private sealed record HarnessRun(string Result, string Output, string Error);
}
