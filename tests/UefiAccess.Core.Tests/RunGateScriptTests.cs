// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;
using UefiAccess.Core.Diagnostics;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// Static checks on tools/gate/Run-Gate.ps1 (the script needs an elevated prompt on the target
/// machine, so it cannot run here): every file it reads back is decoded as UTF-8, the encoding
/// uefiaccess writes to a redirected stream, and every date it formats uses the invariant culture,
/// so the log file name matches the one Serilog writes under any Windows display language. The
/// output folder is made by the tool's data-root rule before any command runs: the data root comes
/// from the known folder the tool uses, the trusted owners and the access list are the tool's, and
/// "gate --out" writes into that checked folder. The folder functions live in the shared module
/// tools/common/DataFolder.psm1, which the script imports (<see cref="RunFailureCasesScriptTests"/>
/// checks that both scripts do); <see cref="RunGateOutputFolderTests"/> runs them.
/// </summary>
public sealed class RunGateScriptTests
{
    private static readonly string[] Lines = File.ReadAllLines(Path.Combine(RepositoryPaths.Root, "tools", "gate", "Run-Gate.ps1"));

    /// <summary>The script's lines without the comment-only ones (the comment-based help block included).</summary>
    private static readonly string[] CodeLines = WithoutComments(Lines);

    /// <summary>The code lines of the shared module that holds the output-folder functions.</summary>
    private static readonly string[] ModuleCodeLines = WithoutComments(File.ReadAllLines(Path.Combine(RepositoryPaths.Root, "tools", "common", "DataFolder.psm1")));

    [Fact]
    public void EveryGetContentDecodesUtf8()
    {
        string[] reads = Lines.Where(line => line.Contains("Get-Content", StringComparison.Ordinal) && !line.TrimStart().StartsWith('#')).ToArray();

        Assert.True(reads.Length >= 4, "expected the stdout, stderr, list and gate.json reads");
        Assert.All(reads, line => Assert.Contains("-Encoding UTF8", line, StringComparison.Ordinal));
    }

    [Fact]
    public void EveryDateIsFormattedWithTheInvariantCulture()
    {
        Regex dateFormat = new(@"\.ToString\('[^']*(yyyy|HH)[^']*'(?<rest>[^)]*)\)", RegexOptions.CultureInvariant);
        Match[] formats = Lines.SelectMany(line => dateFormat.Matches(line)).ToArray();

        Assert.True(formats.Length >= 5, "expected the folder name, the log file name and the three summary times");
        Assert.All(formats, match => Assert.Equal(", $Invariant", match.Groups["rest"].Value));
        Assert.Contains(Lines, line => line.Contains("$Invariant = [System.Globalization.CultureInfo]::InvariantCulture", StringComparison.Ordinal));
        Assert.Contains(Lines, line => line.Contains("Join-Path $DataRoot ('logs\\uefiaccess-' + $started.ToString('yyyyMMdd', $Invariant) + '.log')", StringComparison.Ordinal));
    }

    [Fact]
    public void SummaryIsWrittenAsUtf8WithoutAByteOrderMark()
    {
        Assert.DoesNotContain(Lines, line => line.Contains("Set-Content", StringComparison.Ordinal) && line.Contains("-Encoding UTF8", StringComparison.Ordinal));
        Assert.Contains(Lines, line => line.Contains("[System.IO.File]::WriteAllLines(", StringComparison.Ordinal) && line.Contains("$Utf8NoBom", StringComparison.Ordinal));
    }

    [Fact]
    public void DataRootComesFromTheKnownFolderTheToolUses_NeverFromTheEnvironment()
    {
        // Logging.DataDirectory is the CommonApplicationData known folder plus UefiAccess; the
        // ProgramData environment variable can be changed per user, so it names no path here.
        Assert.Contains(CodeLines, line => line == "$DataRoot = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'UefiAccess'");
        Assert.DoesNotContain(CodeLines, line => line.Contains("$env:ProgramData", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(CodeLines, line => line.Trim() == "$OutputRoot = Join-Path $DataRoot 'gate'");
        Assert.Contains(CodeLines, line => line == "$baselineRoot = Join-Path $DataRoot 'baseline'");
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "UefiAccess"), Logging.DataDirectory);
    }

    [Fact]
    public void TrustedOwners_AreTheWellKnownAccountsTheToolTrusts()
    {
        string line = Assert.Single(CodeLines, candidate => candidate.StartsWith("$TrustedOwnerSids = ", StringComparison.Ordinal));
        string[] sids = Regex.Matches(line, "'(?<sid>[^']*)'").Select(match => match.Groups["sid"].Value).ToArray();

        Assert.Equal(
            new[] { "S-1-5-18", "S-1-5-32-544", "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464" },
            sids);
        Assert.Equal("S-1-5-18", new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value);
        Assert.Equal("S-1-5-32-544", new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value);
        Assert.All(sids, sid => Assert.True(Logging.IsTrustedOwner(new SecurityIdentifier(sid)), sid + " is not an owner the tool trusts"));
    }

    [Fact]
    public void ProtectedFolderAccessList_IsTheOneTheToolCreatesItsFoldersWith()
    {
        string line = Assert.Single(CodeLines, candidate => candidate.StartsWith("$ProtectedFolderSddl = ", StringComparison.Ordinal));
        string sddl = Regex.Match(line, "^\\$ProtectedFolderSddl = '(?<sddl>[^']+)'$").Groups["sddl"].Value;
        DirectorySecurity fromScript = new();
        fromScript.SetSecurityDescriptorSddlForm(sddl);

        LoggingFallbackTests.AssertProtectedAccessList(fromScript);
        Assert.Equal(
            Logging.CreateDataDirectorySecurity().GetSecurityDescriptorSddlForm(AccessControlSections.Access),
            fromScript.GetSecurityDescriptorSddlForm(AccessControlSections.Access));
    }

    [Fact]
    public void OutputFolder_IsMadeByTheDataRootRuleBeforeAnyCommandRuns()
    {
        int import = IndexOf(line => line == "Import-Module -Name (Join-Path $PSScriptRoot '..\\common\\DataFolder.psm1')");
        int marker = IndexOf(line => line == "$env:UEFIACCESS_HW = '1'");
        int prepare = IndexOf(line => line == "$folderProblem = New-OutputFolder -Folder $outputFolder -DataRoot $DataRoot -Exe $ExePath -TrustedOwners $TrustedOwnerSids -Sddl $ProtectedFolderSddl");
        int firstStep = IndexOf(line => line.StartsWith("$infoCode = Invoke-UefiAccess ", StringComparison.Ordinal));
        int firstCopy = IndexOf(line => line.Contains("Copy-Item", StringComparison.Ordinal));

        // The quiet "info" run that creates a missing data root is tagged as a gate run too, and
        // nothing is written into the output folder before it has been made and checked.
        Assert.True(import < prepare, "the shared module must be imported before the output folder is prepared");
        Assert.True(marker < prepare, "UEFIACCESS_HW must be set before the output folder is prepared");
        Assert.True(prepare < firstStep && prepare < firstCopy, "the output folder must be prepared before any command or copy writes into it");
        Assert.Equal(new[] { "if ($folderProblem -ne '') {", "Write-Host ''", "Write-Host $folderProblem", "exit 2" }, CodeLines.Skip(prepare + 1).Take(4).Select(line => line.Trim()));

        // No folder is created with New-Item -Force, which accepts an existing folder or junction;
        // the script creates no folder itself, and the one New-Item of the shared module is
        // New-OutputFolder's, for a folder outside the data root.
        Assert.DoesNotContain(CodeLines, line => line.Contains("New-Item", StringComparison.OrdinalIgnoreCase));
        string[] newItems = ModuleCodeLines.Where(line => line.Contains("New-Item", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.Equal(new[] { "$null = New-Item -ItemType Directory -Path $Folder" }, newItems.Select(line => line.Trim()));
        Assert.DoesNotContain(CodeLines.Concat(ModuleCodeLines), line => line.Contains("Set-Acl", StringComparison.OrdinalIgnoreCase) || line.Contains("icacls", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void QuietRun_IsInfoOnly()
    {
        // The quiet run belongs to New-OutputFolder, which the script takes from the shared module.
        string[] calls = ModuleCodeLines.Where(line => line.Contains("Invoke-UefiAccessQuietly -", StringComparison.Ordinal)).Select(line => line.Trim()).ToArray();

        Assert.Equal(new[] { "$code = Invoke-UefiAccessQuietly -Exe $Exe -Arguments @('info')" }, calls);
        Assert.DoesNotContain(CodeLines, line => line.Contains("Invoke-UefiAccessQuietly", StringComparison.Ordinal));
    }

    [Fact]
    public void GateOut_IsInsideTheCheckedOutputFolder()
    {
        // GateCommand checks an --out folder inside the data root as Logging.PrepareDataFolderFor
        // does; the folder passed here is the one New-OutputFolder just made and checked.
        Assert.Contains(CodeLines, line => line == "$gateJson = Join-Path $outputFolder 'gate.json'");
        Assert.Contains(CodeLines, line => line == "$gateArguments = @('gate', '--out', $gateJson)");
        Assert.Single(CodeLines, line => line.TrimStart().StartsWith("$outputFolder = ", StringComparison.Ordinal));
    }

    private static int IndexOf(Func<string, bool> predicate)
    {
        int index = Array.FindIndex(CodeLines, line => predicate(line));
        Assert.True(index >= 0, "line not found in Run-Gate.ps1");
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
