# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.

<#
.SYNOPSIS
    Runs the UefiAccess Milestone 1 hardware checks on the target machine (2014 Dell OptiPlex 7020)
    and prints the manual checklist for the gate memo.

.DESCRIPTION
    Runs the read-only commands of uefiaccess.exe in this order: info, list, boot, secureboot,
    baseline, gate. Standard output and standard error of every command are captured into a new
    timestamped folder (default C:\ProgramData\UefiAccess\gate\<yyyyMMdd-HHmmss>) together with the
    JSON forms of list, boot and secureboot, gate.json, a copy of today's log file, and summary.txt.
    Afterwards the script checks what can be checked automatically (exit codes, variable count above
    zero, BootOrder present, baseline folder complete, gate.json written and parseable) and prints
    the manual checklist: compare the boot entries with the F12 boot menu, compare the Secure Boot
    state with msinfo32, confirm the baseline folder, and note the BIOS version and boot mode for
    the memo (docs\M1-gate-memo-template.md).

    Everything this script runs is read-only. It never invokes anything that writes a firmware
    variable, changes a BIOS setting, or touches BitLocker state, and it never passes a set-style
    argument to any tool. The only programs it starts are uefiaccess.exe and the built-in
    PowerShell cmdlets used to copy and read files.

    The output folder is always new: the script stops when it already exists. Inside the tool's
    data root, C:\ProgramData\UefiAccess (found from the ProgramData known folder, as uefiaccess
    finds it, never from the ProgramData environment variable), the script follows the rule
    uefiaccess follows there, so that a standard user cannot redirect what this elevated script
    writes. When the data root is missing, "uefiaccess info" runs once first, its output not kept,
    so that the tool itself creates the root with its protected access list. Every existing folder
    from the data root down must be a plain folder, not a junction, symbolic link or other reparse
    point, owned by SYSTEM, Administrators or TrustedInstaller; the missing ones (gate and the
    timestamped folder) are created with the tool's protected access list (SYSTEM and
    Administrators full control, Users read) and checked again. When a folder fails, the script
    stops with exit code 2 and one sentence that names the folder and says to rename or delete it
    from an administrator prompt; none of the checks has run then. "uefiaccess gate --out" later
    writes gate.json into that checked folder, which the tool's own check for --out accepts.

    The script must run from an elevated Windows PowerShell 5.1 (or later) prompt: uefiaccess
    needs SeSystemEnvironmentPrivilege, which exists only in an administrator token. The machine
    must have booted in UEFI mode; when "uefiaccess info" reports legacy BIOS boot (exit code 3)
    or a missing privilege (exit code 4) the script stops after that command, because nothing else
    can succeed. Install Dell Command | Monitor and Dell Command | Configure before running so that
    gate question (b) can be answered.

.PARAMETER ExePath
    Path of uefiaccess.exe. Default: ..\..\src\UefiAccess.Cli\bin\Release\net8.0-windows\uefiaccess.exe
    relative to this script (the output of "dotnet build UefiAccess.sln -c Release").

.PARAMETER IfrVarstores
    Optional path of the Name;GUID;Size file produced from the IFR extraction of the Dell BIOS
    image (see ifr-varstores.example.txt in this folder). Passed through as
    "uefiaccess gate --ifr-varstores <file>".

.PARAMETER OutputRoot
    Parent folder for the timestamped output folder. Default: C:\ProgramData\UefiAccess\gate (the
    gate folder in the tool's data root). A folder inside the data root is checked as described
    above; a folder outside it is the operator's choice and is only required to be new.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\gate\Run-Gate.ps1

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\gate\Run-Gate.ps1 -IfrVarstores C:\work\ifr-varstores.txt -OutputRoot C:\work\gate

.NOTES
    Windows PowerShell 5.1 compatible (no PowerShell 7 syntax).
    Every text file in the output folder is UTF-8 without a byte order mark: uefiaccess writes its
    redirected standard output and standard error that way, and the script writes summary.txt the
    same way. The timestamped folder name and the log file name use the Gregorian calendar
    whatever the Windows display language.
    Exit codes: 0 every command returned 0 or 6; 2 usage (uefiaccess.exe or the varstore file not
    found, or the output folder already exists, cannot be created, or lies on a path that fails the
    data-root check); 3, 4 or 5 taken over from uefiaccess when a precondition or a native call
    failed; 1 when a command returned any other unexpected code or could not be started.
    Environment: the script sets UEFIACCESS_HW=1 for the uefiaccess.exe processes it starts. That is
    the marker M1-Design.md section 12 reserves for hardware checks; no M1 code reads it, so it
    changes nothing about what the commands do. It only tags those processes as a gate run (in a
    process list or a debugger), and later milestones may key hardware-only checks on it. The same
    statement is in docs/BUILD.md section 6.
#>
#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$ExePath = '',

    [Parameter(Mandatory = $false)]
    [string]$IfrVarstores = '',

    [Parameter(Mandatory = $false)]
    [string]$OutputRoot = ''
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

# Dates in file names and in summary.txt use the invariant (Gregorian) calendar and ':' as the time
# separator whatever the Windows display language, as the log file name that Serilog writes does.
$Invariant = [System.Globalization.CultureInfo]::InvariantCulture

# uefiaccess writes redirected standard output and standard error as UTF-8 without a byte order
# mark, and gate.json the same way; every file this script reads back is decoded as UTF-8, and
# summary.txt is written as UTF-8 without a byte order mark too.
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

# The tool's data root, found the way uefiaccess finds it (Logging.DataDirectory): from the
# ProgramData known folder, never from the ProgramData environment variable, which a per-user
# setting can change. Logs, baselines and, by default, the gate output live under it.
$DataRoot = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'UefiAccess'

# The owners uefiaccess trusts for a folder an elevated run writes through under the data root
# (Logging.IsTrustedOwner): SYSTEM, Administrators and TrustedInstaller. The owner of a folder can
# turn it into a link at any moment, so a folder any other account owns is refused.
$TrustedOwnerSids = @('S-1-5-18', 'S-1-5-32-544', 'S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464')

# The access list uefiaccess gives every folder it creates under the data root
# (Logging.CreateDataDirectorySecurity): protected from ProgramData's inherited entries, which let
# Users add files and folders; SYSTEM and Administrators full control, Users read and execute, each
# inherited by everything inside. RunGateScriptTests keeps it equal to the tool's.
$ProtectedFolderSddl = 'D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1200a9;;;BU)'

# ------------------------------------------------------------------------------------------------
# Helpers
# ------------------------------------------------------------------------------------------------

$script:Results = @()

function Test-IsElevated {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-ExitCodeText {
    param([int]$Code)
    switch ($Code) {
        0 { return 'success' }
        2 { return 'usage error' }
        3 { return 'not UEFI (legacy BIOS boot)' }
        4 { return 'not elevated or SeSystemEnvironmentPrivilege missing' }
        5 { return 'native call failed' }
        6 { return 'partial (completed with Warning: lines on stderr: an unreadable variable or an enumeration that stopped early, a failed WMI, registry or PowerShell source, or export errors)' }
        default { return "unexpected exit code $Code" }
    }
}

function Resolve-FullPath {
    param([string]$Path)
    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

function ConvertTo-ArgumentLine {
    param([string[]]$Arguments)
    $quoted = foreach ($argument in $Arguments) {
        if ($argument -match '[\s"]') {
            '"' + ($argument -replace '"', '\"') + '"'
        }
        else {
            $argument
        }
    }
    return ($quoted -join ' ')
}

function Invoke-UefiAccess {
    param(
        [string]$Exe,
        [string]$Step,
        [string[]]$Arguments,
        [string]$Folder,
        [string]$StdoutFileName = '',
        [switch]$Quiet
    )

    if ([string]::IsNullOrEmpty($StdoutFileName)) {
        $StdoutFileName = $Step + '.stdout.txt'
    }
    $stdoutFile = Join-Path $Folder $StdoutFileName
    $stderrFile = Join-Path $Folder ($Step + '.stderr.txt')
    $argumentLine = ConvertTo-ArgumentLine -Arguments $Arguments

    Write-Host ''
    Write-Host ('==> uefiaccess ' + $argumentLine)

    $code = -1
    $note = ''
    try {
        $process = Start-Process -FilePath $Exe -ArgumentList $argumentLine -NoNewWindow -Wait -PassThru `
            -RedirectStandardOutput $stdoutFile -RedirectStandardError $stderrFile
        $code = [int]$process.ExitCode
    }
    catch {
        $note = 'could not start: ' + $_.Exception.Message
        Write-Host ('    ' + $note)
    }

    if (-not $Quiet -and (Test-Path -LiteralPath $stdoutFile)) {
        foreach ($line in @(Get-Content -LiteralPath $stdoutFile -Encoding UTF8)) {
            Write-Host ('    ' + $line)
        }
    }
    # uefiaccess writes the precondition sentence, failure messages and "Warning:" lines to standard
    # error (the log is mirrored there only with --verbose, which this script does not pass). Show
    # them so the operator sees why a command stopped or why it exited with 6; the --json runs repeat
    # the text runs, so their standard error is shown only when they failed.
    if ((-not $Quiet -or ($code -ne 0 -and $code -ne 6)) -and (Test-Path -LiteralPath $stderrFile)) {
        $stderrLines = @(Get-Content -LiteralPath $stderrFile -Encoding UTF8 | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($stderrLines.Count -gt 0) {
            Write-Host '    standard error:'
            foreach ($line in $stderrLines) {
                Write-Host ('      ' + $line)
            }
        }
    }
    Write-Host ('    exit code ' + $code + ' (' + (Get-ExitCodeText -Code $code) + ')')
    Write-Host ('    stdout: ' + $stdoutFile)
    Write-Host ('    stderr (precondition sentence, failure messages, Warning: lines): ' + $stderrFile)

    $script:Results += New-Object PSObject -Property @{
        Step     = $Step
        Command  = 'uefiaccess ' + $argumentLine
        ExitCode = $code
        Note     = $note
        Stdout   = $stdoutFile
        Stderr   = $stderrFile
    }
    return $code
}

# ------------------------------------------------------------------------------------------------
# Output folder
# ------------------------------------------------------------------------------------------------

# The functions that make the output folder by the tool's data-root rule (New-OutputFolder, and
# Get-FullPathWithoutSeparator for the path) are in tools\common\DataFolder.psm1, shared with
# tools\write-tests\Run-FailureCases.ps1; .DESCRIPTION above says what the rule is.
Import-Module -Name (Join-Path $PSScriptRoot '..\common\DataFolder.psm1')

# ------------------------------------------------------------------------------------------------
# Preconditions
# ------------------------------------------------------------------------------------------------

$started = Get-Date
$timestamp = $started.ToString('yyyyMMdd-HHmmss', $Invariant)

Write-Host 'UefiAccess Milestone 1 hardware checks (read-only)'
Write-Host ('Started ' + $started.ToString('yyyy-MM-dd HH:mm:ss', $Invariant))

if (-not (Test-IsElevated)) {
    Write-Host ''
    Write-Host 'This script must run from an elevated prompt (Run as administrator): uefiaccess needs SeSystemEnvironmentPrivilege, which exists only in an administrator token.'
    Write-Host 'Open Windows PowerShell with "Run as administrator" and start the script again.'
    exit 4
}

if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $ExePath = Join-Path $PSScriptRoot '..\..\src\UefiAccess.Cli\bin\Release\net8.0-windows\uefiaccess.exe'
}
$ExePath = Resolve-FullPath -Path $ExePath
if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
    Write-Host ''
    Write-Host ('uefiaccess.exe was not found at ' + $ExePath)
    Write-Host 'Build it first with "dotnet build UefiAccess.sln -c Release" from the repository root, or pass -ExePath.'
    exit 2
}

if (-not [string]::IsNullOrWhiteSpace($IfrVarstores)) {
    $IfrVarstores = Resolve-FullPath -Path $IfrVarstores
    if (-not (Test-Path -LiteralPath $IfrVarstores -PathType Leaf)) {
        Write-Host ''
        Write-Host ('The varstore file was not found: ' + $IfrVarstores)
        Write-Host 'Produce it as described in ifr-varstores.example.txt (one Name;GUID;Size per line) or omit -IfrVarstores.'
        exit 2
    }
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $DataRoot 'gate'
}
try {
    $outputFolder = Get-FullPathWithoutSeparator -Path ([System.IO.Path]::Combine((Resolve-FullPath -Path $OutputRoot), $timestamp))
}
catch {
    Write-Host ''
    Write-Host ('No output folder can be made under ' + $OutputRoot + ': ' + $_.Exception.GetBaseException().Message)
    exit 2
}

# Marker reserved by M1-Design.md section 12 for hardware checks, inherited by the uefiaccess.exe
# processes started below. No M1 code reads it (see .NOTES above and docs/BUILD.md section 6).
$env:UEFIACCESS_HW = '1'

# The output folder is created before any command runs, and only by the rule in .DESCRIPTION;
# "gate --out" below writes into it.
$folderProblem = New-OutputFolder -Folder $outputFolder -DataRoot $DataRoot -Exe $ExePath -TrustedOwners $TrustedOwnerSids -Sddl $ProtectedFolderSddl
if ($folderProblem -ne '') {
    Write-Host ''
    Write-Host $folderProblem
    exit 2
}

Write-Host ('uefiaccess.exe: ' + $ExePath)
Write-Host ('Output folder:  ' + $outputFolder)
if (-not [string]::IsNullOrWhiteSpace($IfrVarstores)) {
    Write-Host ('Varstore file:  ' + $IfrVarstores)
    Copy-Item -LiteralPath $IfrVarstores -Destination (Join-Path $outputFolder 'ifr-varstores.txt') -Force
}
else {
    Write-Host 'Varstore file:  none (gate question (c) is skipped; pass -IfrVarstores after the IFR extraction)'
}

# ------------------------------------------------------------------------------------------------
# Commands (all read-only)
# ------------------------------------------------------------------------------------------------

$infoCode = Invoke-UefiAccess -Exe $ExePath -Step '01-info' -Arguments @('info') -Folder $outputFolder
if ($infoCode -eq 3 -or $infoCode -eq 4) {
    Write-Host ''
    Write-Host ('A precondition failed: uefiaccess wrote the sentence to standard error (shown above, saved in ' + (Join-Path $outputFolder '01-info.stderr.txt') + '); the remaining checks were not run.')
    if ($infoCode -eq 3) {
        Write-Host 'Gate question (a): the unit is in legacy BIOS mode. Record the boot mode on arrival in the memo. Turn BitLocker off (or suspend it) first. Then, from this elevated prompt, convert the disk while Windows is still in legacy mode (mbr2gpt /validate /allowFullOS, then mbr2gpt /convert /allowFullOS), or reinstall Windows in UEFI mode; only after that switch "Boot List Option" to UEFI in the BIOS setup (docs\M1-Hardware-Runbook.md, phase A4), and run this script again.'
    }
    else {
        Write-Host 'Open an elevated prompt (Run as administrator) and run this script again.'
    }
    exit $infoCode
}

$null = Invoke-UefiAccess -Exe $ExePath -Step '02-list' -Arguments @('list') -Folder $outputFolder
$null = Invoke-UefiAccess -Exe $ExePath -Step '02-list-json' -Arguments @('list', '--json') -Folder $outputFolder -StdoutFileName 'list.json' -Quiet
$null = Invoke-UefiAccess -Exe $ExePath -Step '03-boot' -Arguments @('boot') -Folder $outputFolder
$null = Invoke-UefiAccess -Exe $ExePath -Step '03-boot-json' -Arguments @('boot', '--json') -Folder $outputFolder -StdoutFileName 'boot.json' -Quiet
$null = Invoke-UefiAccess -Exe $ExePath -Step '04-secureboot' -Arguments @('secureboot') -Folder $outputFolder
$null = Invoke-UefiAccess -Exe $ExePath -Step '04-secureboot-json' -Arguments @('secureboot', '--json') -Folder $outputFolder -StdoutFileName 'secureboot.json' -Quiet
$null = Invoke-UefiAccess -Exe $ExePath -Step '05-baseline' -Arguments @('baseline') -Folder $outputFolder

$gateJson = Join-Path $outputFolder 'gate.json'
$gateArguments = @('gate', '--out', $gateJson)
if (-not [string]::IsNullOrWhiteSpace($IfrVarstores)) {
    $gateArguments += @('--ifr-varstores', $IfrVarstores)
}
$null = Invoke-UefiAccess -Exe $ExePath -Step '06-gate' -Arguments $gateArguments -Folder $outputFolder

# ------------------------------------------------------------------------------------------------
# Automatic checks
# ------------------------------------------------------------------------------------------------

Write-Host ''
Write-Host 'Automatic checks'

# list: variable count above zero and BootOrder present (design section 12).
$variableCount = 0
$bootOrderPresent = $false
$listStdout = Join-Path $outputFolder '02-list.stdout.txt'
if (Test-Path -LiteralPath $listStdout) {
    $keyPattern = '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}:'
    $keyLines = @(Get-Content -LiteralPath $listStdout -Encoding UTF8 | Where-Object { $_ -match $keyPattern })
    $variableCount = $keyLines.Count
    $bootOrderPresent = @($keyLines | Where-Object { $_ -cmatch ':BootOrder(\s|$)' }).Count -gt 0
}
$countText = if ($variableCount -gt 0) { 'yes (' + $variableCount + ' rows in the list table)' } else { 'NO (no variable rows found in ' + $listStdout + ')' }
$bootOrderText = if ($bootOrderPresent) { 'yes' } else { 'NO' }
Write-Host ('  list count above zero:  ' + $countText)
Write-Host ('  BootOrder present:      ' + $bootOrderText)

# baseline: the newest folder created since the script started, with the five expected items.
$baselineRoot = Join-Path $DataRoot 'baseline'
$baselineFolder = ''
$baselineMissing = @()
if (Test-Path -LiteralPath $baselineRoot) {
    $candidates = @(Get-ChildItem -LiteralPath $baselineRoot -Directory | Where-Object { $_.CreationTime -ge $started.AddMinutes(-1) } | Sort-Object CreationTime -Descending)
    if ($candidates.Count -gt 0) {
        $baselineFolder = $candidates[0].FullName
        foreach ($item in @('index.json', 'vars', 'decoded.json', 'platform.json', 'log.txt')) {
            if (-not (Test-Path -LiteralPath (Join-Path $baselineFolder $item))) {
                $baselineMissing += $item
            }
        }
    }
}
if ($baselineFolder -eq '') {
    Write-Host ('  baseline folder:        NO new folder under ' + $baselineRoot + ' (check 05-baseline.stdout.txt; a folder written with --out elsewhere is not detected)')
}
elseif ($baselineMissing.Count -eq 0) {
    Write-Host ('  baseline folder:        complete: ' + $baselineFolder)
    Copy-Item -LiteralPath (Join-Path $baselineFolder 'index.json') -Destination (Join-Path $outputFolder 'baseline-index.json') -Force -ErrorAction SilentlyContinue
}
else {
    Write-Host ('  baseline folder:        INCOMPLETE: ' + $baselineFolder + ' is missing ' + ($baselineMissing -join ', '))
}

# gate.json: written and parseable.
$gateNote = 'NO (not written)'
if (Test-Path -LiteralPath $gateJson) {
    try {
        $null = Get-Content -LiteralPath $gateJson -Raw -Encoding UTF8 | ConvertFrom-Json
        $gateNote = 'yes: ' + $gateJson
    }
    catch {
        $gateNote = 'written but not valid JSON: ' + $_.Exception.Message
    }
}
Write-Host ('  gate.json written:      ' + $gateNote)

# Copy today's log file next to the outputs (the file sink is shared, so copying while open is fine).
$logFile = Join-Path $DataRoot ('logs\uefiaccess-' + $started.ToString('yyyyMMdd', $Invariant) + '.log')
if (Test-Path -LiteralPath $logFile) {
    Copy-Item -LiteralPath $logFile -Destination (Join-Path $outputFolder 'uefiaccess-log.txt') -Force -ErrorAction SilentlyContinue
    Write-Host ('  log file copied:        ' + $logFile)
}
else {
    Write-Host ('  log file:               not found at ' + $logFile)
}

# ------------------------------------------------------------------------------------------------
# Summary file
# ------------------------------------------------------------------------------------------------

$summaryLines = @()
$summaryLines += 'UefiAccess Milestone 1 hardware checks'
$summaryLines += 'Started:         ' + $started.ToString('yyyy-MM-dd HH:mm:ss', $Invariant)
$summaryLines += 'Finished:        ' + (Get-Date).ToString('yyyy-MM-dd HH:mm:ss', $Invariant)
$summaryLines += 'uefiaccess.exe:  ' + $ExePath
$summaryLines += 'Output folder:   ' + $outputFolder
$summaryLines += 'Varstore file:   ' + $(if ([string]::IsNullOrWhiteSpace($IfrVarstores)) { 'none' } else { $IfrVarstores })
$summaryLines += 'Baseline folder: ' + $(if ($baselineFolder -eq '') { 'not detected' } else { $baselineFolder })
$summaryLines += 'Variable rows:   ' + $variableCount
$summaryLines += 'BootOrder:       ' + $bootOrderText
$summaryLines += 'gate.json:       ' + $gateNote
$summaryLines += ''
$tableLines = @()
$tableLines += 'Command                                   Exit  Meaning'
foreach ($result in $script:Results) {
    $meaning = Get-ExitCodeText -Code $result.ExitCode
    if ($result.Note -ne '') { $meaning = $meaning + '; ' + $result.Note }
    $tableLines += ('{0,-41} {1,4}  {2}' -f $result.Command, $result.ExitCode, $meaning)
}
$summaryLines += $tableLines
[System.IO.File]::WriteAllLines((Join-Path $outputFolder 'summary.txt'), [string[]]$summaryLines, $Utf8NoBom)

Write-Host ''
Write-Host 'Exit codes'
foreach ($line in $tableLines) {
    Write-Host ('  ' + $line)
}

# ------------------------------------------------------------------------------------------------
# Manual checklist
# ------------------------------------------------------------------------------------------------

$bootStdout = Join-Path $outputFolder '03-boot.stdout.txt'
$secureBootStdout = Join-Path $outputFolder '04-secureboot.stdout.txt'
$infoStdout = Join-Path $outputFolder '01-info.stdout.txt'
$baselineText = if ($baselineFolder -eq '') { '(see 05-baseline.stdout.txt for the folder)' } else { $baselineFolder }

Write-Host ''
Write-Host 'MANUAL CHECKLIST (record the answers in docs\M1-gate-memo-template.md)'
Write-Host ''
Write-Host '[ ] 1. Boot entries versus the F12 menu (acceptance criterion 1).'
Write-Host '       Restart, press F12 at the Dell logo, and compare the one-time boot menu with the "boot"'
Write-Host ('       output in ' + $bootStdout + '.')
Write-Host '       Every Boot#### description must appear in the F12 menu with the same wording, and the'
Write-Host '       BootOrder must match the menu order. Note any position the tool marked "(missing)" and any entry whose'
Write-Host '       "In BootOrder:" line says "no" (the Warning: lines are in 03-boot.stderr.txt).'
Write-Host ''
Write-Host '[ ] 2. Secure Boot state versus msinfo32 (acceptance criterion 3).'
Write-Host '       Run msinfo32 and compare "Secure Boot State" (On, Off, Unsupported) and "BIOS Mode" (UEFI, Legacy)'
Write-Host ('       with the "secureboot" output in ' + $secureBootStdout + ' and the "info" output in ' + $infoStdout + '.')
Write-Host '       The "Sources agree" row must be true; if it is false, record the differing "Secure Boot policy sources" rows in the memo.'
Write-Host ''
Write-Host '[ ] 3. Baseline folder (acceptance criterion 4).'
Write-Host ('       Confirm that ' + $baselineText)
Write-Host '       contains index.json, vars\ (one .bin per variable), decoded.json, platform.json, and log.txt,'
Write-Host '       and that the variable count in index.json equals the count printed by "list".'
Write-Host ''
Write-Host '[ ] 4. Notes for the memo (acceptance criterion 2).'
Write-Host '       From the "info" output note the BIOS version, the service tag, the CPU, the OS build, and whether the'
Write-Host '       "Expected model confirmed" row is true (quote the "Expected model notes" row). Note the boot mode on'
Write-Host '       arrival and after any conversion, and check on'
Write-Host '       the Dell support page for the service tag whether the BIOS is the latest A-series release.'
Write-Host ''
Write-Host '[ ] 5. Dell interface (gate question (b)).'
Write-Host '       Confirm Dell Command | Monitor and Dell Command | Configure were installed before this run. If the'
Write-Host '       gate output reports no DCIM classes defined and no cctk.exe, install them and run this script again.'
Write-Host '       If classes are defined but "(b) Dell Command Monitor classes responding" reads none, check that the'
Write-Host '       Dell Command | Monitor service is running, then run again; the reasons are the "Warning: dell probe:" lines.'
Write-Host ''
Write-Host '[ ] 6. Varstores (gate question (c)).'
if ([string]::IsNullOrWhiteSpace($IfrVarstores)) {
    Write-Host '       Not run. Extract the varstores on the developer workstation (ifr-varstores.example.txt explains how)'
    Write-Host '       and run this script again with -IfrVarstores <file>, or run "uefiaccess gate --ifr-varstores <file>" alone.'
}
else {
    Write-Host '       For every varstore marked present at runtime in gate.json, confirm the runtime size equals the IFR size.'
    Write-Host '       For every absent one, note that it is boot-service only or not a UEFI variable (the tool cannot tell which).'
    Write-Host '       If "(c) Status unknown" is above 0, those varstores could not be checked; outcome C is then not ruled out.'
}
Write-Host ''
Write-Host '[ ] 7. Attach this folder and gate.json to the memo:'
Write-Host ('       ' + $outputFolder)
Write-Host ''
Write-Host 'Nothing in this run changed a firmware variable, a BIOS setting, or BitLocker state.'

# ------------------------------------------------------------------------------------------------
# Exit code
# ------------------------------------------------------------------------------------------------

$failed = @($script:Results | Where-Object { $_.ExitCode -ne 0 -and $_.ExitCode -ne 6 })
if ($failed.Count -eq 0) {
    exit 0
}
$firstKnown = @($failed | Where-Object { $_.ExitCode -eq 3 -or $_.ExitCode -eq 4 -or $_.ExitCode -eq 5 })
if ($firstKnown.Count -gt 0) {
    exit $firstKnown[0].ExitCode
}
exit 1
