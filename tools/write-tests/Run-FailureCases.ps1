# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.

<#
.SYNOPSIS
    Runs the UefiAccess Milestone 3 failure cases on the target machine (2014 Dell OptiPlex 7020):
    dry runs and blocked requests only, so nothing is written to the firmware.

.DESCRIPTION
    Runs the rows of the failure-case matrix (M3-Design.md section 4.3) that can be shown on the
    machine without risk, one uefiaccess.exe command line per case, each with --json:
    FC-01 and FC-02 (variables off the whitelist, one of them signed), FC-03 and FC-04 (a Boot####
    that does not exist, in the order and as the one-time boot), FC-05 and FC-06 (an order that is
    not a permutation: an entry added, the first entry twice, an entry left out), FC-07 (the order
    unchanged), FC-15 (BitLocker on and no BitLocker choice given), FC-16 (a one-time boot to a
    Windows Boot Manager on another partition), FC-20 (a snapshot root outside the data folder),
    FC-26 (a clear with no one-time boot set) and FC-27 (two command lines that do not parse).
    Every line is a dry run or a request the safeguards block before any call to the firmware. No
    line confirms a change, so no line can write, whatever state the machine is in.

    Before the cases the script reads the machine: "boot --json" for the boot order, BootCurrent,
    BootNext and the Boot#### entries, from which the case lines are built so that each meets the
    check it is meant to meet on this machine; "read BootOrder --hex --json" for the bytes and
    their SHA-256; and "write check BootOrder --json" for the BitLocker state the write pipeline
    assesses. After the cases it runs "write log --last 100 --json" and checks that this run left
    one record per command line that reached the write pipeline, none with apiCalled true, then
    reads BootOrder and the boot list again to show that the hash, the attributes, the order and
    BootNext are unchanged.

    Each case keeps its JSON document as <case>.json (standard output) and its standard error as
    <case>.stderr.txt (the refusal sentence, the BitLocker warning lines, the hints). Per case the
    script compares the exit code with the expected one and blockedBy with the expected check, and
    asserts apiCalled false; a command line that does not parse prints the M1 error document
    instead, with exit code 2. Two rows check more, as section 4.3 lists: FC-02 that the
    authenticatedWrite check failed too, and FC-15 that suspendOffered is true and that standard
    error holds the three BitLocker lines (BitLocker state, Risk, Recovery key) and the hint to
    give --bitlocker. The records of this run in the write log are those at or after the start
    of the run, the times compared as times whatever form ConvertFrom-Json gives them. summary.txt holds one row per case,
    "Case | Command | Expected exit | Actual | BlockedBy | ApiCalled | Verdict", each verdict PASS,
    FAIL or NOT VERIFIED (<reason>); NOT VERIFIED never counts as PASS. It also carries a row for
    FC-13 (missing privilege), which no elevated script can run: the operator runs that line by
    hand from a standard prompt (protocol step H2, with whoami /groups beside it) and replaces the
    row. The WRITE-LOG and STORE rows hold the checks after the cases.

    How the lines are built on the machine:
    - The unknown entry of FC-03, FC-04 and the FC-13 row is Boot0007. When Boot0007 exists here,
      the first free Boot#### stands for it, and summary.txt says which.
    - FC-03 appends the unknown entry to the current order; FC-05 appends an existing Boot#### that
      is not in BootOrder (NOT VERIFIED when every entry is in BootOrder); FC-06 appends the first
      entry again and, when BootOrder has two or more entries, leaves the last one out (NOT
      VERIFIED with one entry); FC-07 and the first FC-27 line name the current order.
    - FC-15, FC-20 and the second FC-27 line name the first entry of BootOrder.
    - FC-16 names the first active boot entry, other than BootCurrent's, whose file is the Windows
      Boot Manager (\EFI\Microsoft\Boot\bootmgfw.efi) on another partition than BootCurrent's entry
      (NOT VERIFIED when there is none).
    - FC-15 is NOT VERIFIED, with the state recorded, when "write check BootOrder" reports BitLocker
      Off or Suspended (the dry run needs no BitLocker choice then), and FC-16 when it reports Off
      (rule V13 applies only while protectors exist). Both lines still run, and apiCalled must
      still be false.
    - FC-26 is NOT VERIFIED when a one-time boot (BootNext) is set.

    Two deviations from the command lines of section 4.3, both needed on the unit:
    - FC-20 carries "--bitlocker continue". Stage S8 (the BitLocker choice) runs before stage S9
      (the snapshot root), so with BitLocker on the design's line would be blocked by bitLocker and
      never reach snapshotRoot. "continue" keeps protection on and is an input of the dry run
      (section 4.1); without a confirmation nothing is written.
    - The first FC-27 line is "--dry-run --force" (an option that does not exist; no option of any
      build skips a safety check) instead of the dry-run option paired with the confirmation
      option. Section 4.4 keeps the confirmation option off every line this script runs, so that
      no line can write whatever the machine's state and whatever a build does with a line; the
      write command refuses that pairing while it parses the line (exit 2, before any check),
      which the CLI's unit tests show (FC27 in CliWriteCommandTests). This row shows the same
      parse-error path on the unit: exit 2 and the error document, with nothing run.

    The output folder is always new: C:\ProgramData\UefiAccess\write-tests\<yyyyMMdd-HHmmss>, made
    by New-OutputFolder of tools\common\DataFolder.psm1 (shared with tools\gate\Run-Gate.ps1) by
    the rule uefiaccess follows in its data root, found from the ProgramData known folder: when the
    data root is missing, "uefiaccess info" runs once first, its output not kept, so that the tool
    creates the root with its protected access list; every existing folder from the data root
    down must be a plain folder, not a junction, symbolic link or other reparse point, owned by
    SYSTEM, Administrators or TrustedInstaller; the missing ones are created with the tool's
    protected access list and checked again. When a folder fails, the script stops with exit code
    2 and one sentence; no case has run then. The first run on a machine also creates the
    snapshots and writes folders of the data root: the write pipeline creates both at its stage
    S9, dry runs included, and appends one line per request to writes\write-log.jsonl. summary.txt
    records whether they existed before the run.

    The script must run from an elevated Windows PowerShell 5.1 prompt, and refuses any other
    prompt with exit code 4 before it does anything else. The only program it starts is
    uefiaccess.exe; it reads and writes files with PowerShell and .NET only.

.PARAMETER ExePath
    Path of uefiaccess.exe. Default: ..\..\src\UefiAccess.Cli\bin\Release\net8.0-windows\uefiaccess.exe
    relative to this script (the output of "dotnet build UefiAccess.sln -c Release"), as
    tools\gate\Run-Gate.ps1 finds it.

.PARAMETER OutsideSnapshotRoot
    The --snapshot-root of FC-20: a folder outside C:\ProgramData\UefiAccess that does not exist.
    Default: C:\UefiAccess-Evidence\<yyyy-MM-dd>\m3\H1\snapshots, with today's date. The script
    refuses a folder inside the data root (exit code 2), never creates this folder, checks after
    FC-20 that it still does not exist, and marks FC-20 NOT VERIFIED when it exists before the run.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\write-tests\Run-FailureCases.ps1

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\write-tests\Run-FailureCases.ps1 -OutsideSnapshotRoot C:\UefiAccess-Evidence\2026-09-30\m3\H1\snapshots

.NOTES
    Windows PowerShell 5.1 compatible (no PowerShell 7 syntax).
    Every text file in the output folder is UTF-8 without a byte order mark: uefiaccess writes its
    redirected standard output and standard error that way, and the script writes summary.txt the
    same way. Dates use the Gregorian calendar of the invariant culture whatever the Windows
    display language.
    Exit codes: 0 no row FAILs (PASS, NOT VERIFIED and NOT RUN BY RIG rows only); 1 at least one
    row FAILs, which includes a machine that could not be read before the cases; 2 usage
    (uefiaccess.exe not found, a snapshot root inside the data root, or an output folder that
    already exists, cannot be created or fails the data-root check); 3 or 4 taken over from
    uefiaccess when a precondition failed before the cases; 4 also when the prompt is not elevated.
    Environment: the script sets UEFIACCESS_HW=1 for the uefiaccess.exe processes it starts, the
    marker M1-Design.md section 12 reserves for hardware checks, as Run-Gate.ps1 does.
    Evidence: copy the output folder into the evidence folder of protocol step H1 through the
    secret filter, as every text file (M3-Design.md section 7.4).
#>
#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$ExePath = '',

    [Parameter(Mandatory = $false)]
    [string]$OutsideSnapshotRoot = ''
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Test-IsElevated {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Before anything else: uefiaccess needs SeSystemEnvironmentPrivilege, which exists only in an
# administrator token, and the output folder lies in the protected data root.
if (-not (Test-IsElevated)) {
    Write-Host 'This script must run from an elevated prompt (Run as administrator): uefiaccess needs SeSystemEnvironmentPrivilege, which exists only in an administrator token.'
    Write-Host 'Open Windows PowerShell with "Run as administrator" and start the script again. The missing-privilege case (FC-13) is run by hand from a standard prompt, not by this script.'
    exit 4
}

# Dates in file names and in summary.txt use the invariant (Gregorian) calendar and ':' as the time
# separator whatever the Windows display language.
$Invariant = [System.Globalization.CultureInfo]::InvariantCulture

# uefiaccess writes redirected standard output and standard error as UTF-8 without a byte order
# mark; every file this script reads back is decoded as UTF-8, and summary.txt is written as UTF-8
# without a byte order mark too.
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

# The tool's data root, found the way uefiaccess finds it (Logging.DataDirectory): from the
# ProgramData known folder, never from the ProgramData environment variable, which a per-user
# setting can change.
$DataRoot = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'UefiAccess'

# The owners uefiaccess trusts for a folder an elevated run writes through under the data root
# (Logging.IsTrustedOwner): SYSTEM, Administrators and TrustedInstaller.
$TrustedOwnerSids = @('S-1-5-18', 'S-1-5-32-544', 'S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464')

# The access list uefiaccess gives every folder it creates under the data root
# (Logging.CreateDataDirectorySecurity). RunFailureCasesScriptTests keeps it equal to the tool's.
$ProtectedFolderSddl = 'D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1200a9;;;BU)'

# The Windows Boot Manager's file, spelled as rule V13 compares it (ChangeValidator.WindowsBootManagerFilePath).
$WindowsBootManagerFile = '\EFI\Microsoft\Boot\bootmgfw.efi'

# The entry the matrix names as one that does not exist on the unit (FC-03, FC-04, FC-13).
$DefaultUnknownEntry = 'Boot0007'

# The BitLocker states in which the write pipeline asks for a choice at stage S8, and those in
# which rule V13 applies (protectors exist and the volume is not decrypted); M3-Design.md 3.8.
$StatesNeedingAChoice = @('On', 'Encrypting', 'Unknown')
$StatesWithProtectors = @('On', 'Encrypting', 'Suspended', 'Unknown')

# The lines standard error must hold when BitLocker needs a choice and none was given (FC-15):
# the three BitLocker lines, each found by its label, and the hint, whole. RunFailureCasesScriptTests
# keeps them equal to the CLI's own (WriteCommand's line formats and BitLockerHint).
$BitLockerChoiceLines = @('BitLocker state: ', 'Risk: ', 'Recovery key: ', 'Add --bitlocker suspend to suspend it for one restart, or --bitlocker continue to write with protection on.')

# The functions that make the output folder by the tool's data-root rule (New-OutputFolder), and
# the path helpers Get-FullPathWithoutSeparator, Test-IsSameOrInside and Get-EntryAttributes.
Import-Module -Name (Join-Path $PSScriptRoot '..\common\DataFolder.psm1')

# ------------------------------------------------------------------------------------------------
# Helpers
# ------------------------------------------------------------------------------------------------

$script:Cases = @()
$script:Rows = @()
$script:PipelineRuns = @()

function Get-ExitCodeText {
    param([int]$Code)
    switch ($Code) {
        0 { return 'success' }
        2 { return 'usage error' }
        3 { return 'not UEFI (legacy BIOS boot)' }
        4 { return 'not elevated or SeSystemEnvironmentPrivilege missing' }
        5 { return 'native call failed' }
        6 { return 'partial (completed with Warning: lines on stderr)' }
        7 { return 'blocked by a safeguard before any call to the firmware; nothing written' }
        8 { return 'the firmware refused the change and the value read back is the old one; nothing changed, a snapshot exists' }
        9 { return 'the value read back is not what the outcome implies; a snapshot exists, a restore is advised' }
        default { return "unexpected exit code $Code" }
    }
}

function Resolve-FullPath {
    param([string]$Path)
    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

function ConvertTo-ArgumentLine {
    # One command line from $Arguments, quoted the way the C runtime and .NET split a command line:
    # an argument with a space, a tab or a double quote is enclosed in double quotes, a double
    # quote inside it is escaped, and the backslashes before a double quote or the closing quote
    # are doubled.
    param([string[]]$Arguments)
    $quoted = foreach ($argument in $Arguments) {
        if ($argument -eq '') {
            '""'
        }
        elseif ($argument -notmatch '[\s"]') {
            $argument
        }
        else {
            $escaped = [regex]::Replace($argument, '(\\*)"', '$1$1\"')
            $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
            '"' + $escaped + '"'
        }
    }
    return ($quoted -join ' ')
}

function Invoke-UefiAccess {
    # Runs uefiaccess.exe once with $Arguments. Standard output is kept in $Folder\$StdoutFileName
    # and standard error in $Folder\<Step>.stderr.txt, both as uefiaccess writes them (UTF-8
    # without a byte order mark). Shows the command, the standard error lines and the exit code,
    # and returns the step, the command, the exit code (-1 when uefiaccess could not start or
    # reported none), a note and the two file paths.
    param(
        [string]$Exe,
        [string]$Step,
        [string[]]$Arguments,
        [string]$Folder,
        [string]$StdoutFileName
    )

    $stdoutFile = Join-Path $Folder $StdoutFileName
    $stderrFile = Join-Path $Folder ($Step + '.stderr.txt')
    $argumentLine = ConvertTo-ArgumentLine -Arguments $Arguments

    Write-Host ''
    Write-Host ('==> ' + $Step + ': uefiaccess ' + $argumentLine)

    $code = -1
    $note = ''
    try {
        $process = Start-Process -FilePath $Exe -ArgumentList $argumentLine -NoNewWindow -Wait -PassThru `
            -RedirectStandardOutput $stdoutFile -RedirectStandardError $stderrFile
        if ($null -eq $process.ExitCode) {
            $note = 'uefiaccess reported no exit code'
        }
        else {
            $code = [int]$process.ExitCode
        }
    }
    catch {
        $note = 'could not start: ' + $_.Exception.Message
    }

    # uefiaccess writes the refusal sentence, the BitLocker warning lines and the hints to
    # standard error; show them so the operator hears why each case stopped.
    if (Test-Path -LiteralPath $stderrFile) {
        $stderrLines = @(Get-Content -LiteralPath $stderrFile -Encoding UTF8 | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        foreach ($line in $stderrLines) {
            Write-Host ('      ' + $line)
        }
    }
    if ($note -ne '') {
        Write-Host ('    ' + $note)
    }
    $meaning = if ($code -eq -1) { 'no exit code' } else { Get-ExitCodeText -Code $code }
    Write-Host ('    exit code ' + $code + ' (' + $meaning + ')')
    Write-Host ('    stdout: ' + $stdoutFile)

    return New-Object PSObject -Property @{
        Step     = $Step
        Command  = 'uefiaccess ' + $argumentLine
        ExitCode = $code
        Note     = $note
        Stdout   = $stdoutFile
        Stderr   = $stderrFile
    }
}

function Read-JsonDocument {
    # Reads the one JSON document in $Path (UTF-8). Returns Document (the parsed document, or
    # $null) and Problem ('' or why there is no document).
    param([string]$Path)
    $document = $null
    $problem = ''
    try {
        if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
            $problem = 'no standard output was kept'
        }
        else {
            $text = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
            if ([string]::IsNullOrWhiteSpace($text)) {
                $problem = 'standard output is empty'
            }
            else {
                $document = $text | ConvertFrom-Json
                if ($null -eq $document) {
                    $problem = 'standard output holds no JSON document'
                }
            }
        }
    }
    catch {
        $document = $null
        $problem = 'standard output is not one JSON document: ' + $_.Exception.GetBaseException().Message
    }
    return New-Object PSObject -Property @{
        Document = $document
        Problem  = $problem
    }
}

function Test-JsonProperty {
    # True when the parsed JSON object $Object has the property $Name (its value may be null).
    param($Object, [string]$Name)
    if ($null -eq $Object) {
        return $false
    }
    return ($null -ne $Object.PSObject.Properties[$Name])
}

function Get-JsonProperty {
    # The value of the property $Name of the parsed JSON object $Object, or $null when $Object is
    # null or has no such property. Read an array property through Get-JsonArray.
    param($Object, [string]$Name)
    if (-not (Test-JsonProperty -Object $Object -Name $Name)) {
        return $null
    }
    return $Object.PSObject.Properties[$Name].Value
}

function Get-JsonArray {
    # Writes the elements of the array property $Name of $Object one by one, nothing when the
    # property is missing or null; call it inside @() to get an array.
    param($Object, [string]$Name)
    $value = Get-JsonProperty -Object $Object -Name $Name
    if ($null -ne $value) {
        foreach ($item in $value) {
            $item
        }
    }
}

function Get-DocumentKind {
    # 'write' for a write document (it says whether the firmware was called, or carries the
    # record), 'error' for the M1 error document (a command line that does not parse, or a failed
    # precondition), '' for anything else.
    param($Document)
    if ((Test-JsonProperty -Object $Document -Name 'apiCalled') -or (Test-JsonProperty -Object $Document -Name 'record')) {
        return 'write'
    }
    if ((Test-JsonProperty -Object $Document -Name 'error') -and (Test-JsonProperty -Object $Document -Name 'exitCode') -and (Test-JsonProperty -Object $Document -Name 'privilegeState')) {
        return 'error'
    }
    return ''
}

function Get-WriteDocumentValue {
    # The value of $Name in a write document: the document's own property, else the one in its
    # record (the write record the pipeline appended to write-log.jsonl), else $null.
    param($Document, [string]$Name)
    if (Test-JsonProperty -Object $Document -Name $Name) {
        return (Get-JsonProperty -Object $Document -Name $Name)
    }
    return (Get-JsonProperty -Object (Get-JsonProperty -Object $Document -Name 'record') -Name $Name)
}

function Get-PipelineBitLockerState {
    # The BitLocker state a write document reports (Off, Suspended, On, Encrypting or Unknown),
    # from its bitLocker block or its record's; '' when neither holds one.
    param($Document)
    foreach ($holder in @($Document, (Get-JsonProperty -Object $Document -Name 'record'))) {
        $state = Get-JsonProperty -Object (Get-JsonProperty -Object $holder -Name 'bitLocker') -Name 'state'
        if ($null -ne $state -and [string]$state -ne '') {
            return [string]$state
        }
    }
    return ''
}

function Get-Sha256Hex {
    # The lowercase SHA-256 of $Bytes, as the tool writes it.
    param([byte[]]$Bytes)
    if ($null -eq $Bytes) {
        $Bytes = New-Object byte[] 0
    }
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $algorithm.ComputeHash($Bytes)
    }
    finally {
        $algorithm.Dispose()
    }
    return ([System.BitConverter]::ToString($hash) -replace '-', '').ToLowerInvariant()
}

function Get-FolderPresence {
    # 'present' or 'absent' for the folder $Path, or why it could not be looked at.
    param([string]$Path)
    try {
        if ($null -eq (Get-EntryAttributes -Path $Path)) {
            return 'absent'
        }
        return 'present'
    }
    catch {
        return 'not readable (' + $_.Exception.GetBaseException().Message + ')'
    }
}

function Get-AbsenceProblem {
    # '' when nothing exists at $Path; otherwise the sentence for the verdict.
    param([string]$Path)
    try {
        if ($null -eq (Get-EntryAttributes -Path $Path)) {
            return ''
        }
        return $Path + ' exists after the run; a snapshot root outside the data folder must never be created'
    }
    catch {
        return $Path + ' could not be checked after the run: ' + $_.Exception.GetBaseException().Message
    }
}

function Read-BootState {
    # What a "boot --json" run says about the boot list: the order (Boot#### names), the entries,
    # BootCurrent and BootNext. Problem is '' when the order holds at least one entry and every
    # entry of it exists; otherwise it says why the case lines cannot be built from it.
    param($Run)
    $problem = ''
    $order = @()
    $entries = @()
    $bootCurrent = ''
    $bootNextPresent = $null
    $bootNextName = ''
    $read = Read-JsonDocument -Path $Run.Stdout
    if ($Run.ExitCode -ne 0 -and $Run.ExitCode -ne 6) {
        $problem = $Run.Command + ' gave exit code ' + $Run.ExitCode
    }
    elseif ($null -eq $read.Document) {
        $problem = $Run.Command + ': ' + $read.Problem
    }
    else {
        $document = $read.Document
        $entries = @(Get-JsonArray -Object $document -Name 'entries')
        $bootCurrent = [string](Get-JsonProperty -Object (Get-JsonProperty -Object $document -Name 'bootCurrent') -Name 'name')
        $bootNext = Get-JsonProperty -Object $document -Name 'bootNext'
        $bootNextPresent = Get-JsonProperty -Object $bootNext -Name 'present'
        $bootNextName = [string](Get-JsonProperty -Object $bootNext -Name 'name')
        $rows = @(Get-JsonArray -Object $document -Name 'bootOrder')
        $order = @(foreach ($row in $rows) { [string](Get-JsonProperty -Object $row -Name 'name') })
        $broken = @($rows | Where-Object { (Get-JsonProperty -Object $_ -Name 'entryExists') -ne $true -or [string](Get-JsonProperty -Object $_ -Name 'name') -notmatch '^Boot[0-9A-F]{4}$' })
        if ((Get-JsonProperty -Object $document -Name 'bootOrderPresent') -ne $true -or $order.Count -eq 0) {
            $problem = 'BootOrder is absent, empty or unreadable, so no case line can name the current order'
        }
        elseif ($broken.Count -gt 0) {
            $problem = 'BootOrder names an entry that does not exist or could not be read, so the case lines would meet another check first'
        }
    }
    return New-Object PSObject -Property @{
        Problem         = $problem
        Order           = $order
        Entries         = $entries
        BootCurrent     = $bootCurrent
        BootNextPresent = $bootNextPresent
        BootNextName    = $bootNextName
    }
}

function Read-VariableState {
    # What a "read <name> --hex --json" run says: the attributes, the size and the SHA-256 of the
    # bytes, and Problem ('' when the read succeeded).
    param($Run)
    $problem = ''
    $attributes = ''
    $size = ''
    $sha256 = ''
    $read = Read-JsonDocument -Path $Run.Stdout
    if ($Run.ExitCode -ne 0) {
        $problem = $Run.Command + ' gave exit code ' + $Run.ExitCode
    }
    elseif ($null -eq $read.Document) {
        $problem = $Run.Command + ': ' + $read.Problem
    }
    elseif ([string](Get-JsonProperty -Object $read.Document -Name 'status') -ne 'Success') {
        $problem = $Run.Command + ' read status ' + [string](Get-JsonProperty -Object $read.Document -Name 'status')
    }
    else {
        $base64 = [string](Get-JsonProperty -Object $read.Document -Name 'dataBase64')
        $bytes = [System.Convert]::FromBase64String($base64)
        $attributes = '0x' + ([long](Get-JsonProperty -Object $read.Document -Name 'attributes')).ToString('X', $Invariant)
        $size = [string]$bytes.Length
        $sha256 = Get-Sha256Hex -Bytes $bytes
    }
    return New-Object PSObject -Property @{
        Problem    = $problem
        Attributes = $attributes
        Size       = $size
        Sha256     = $sha256
    }
}

function Get-HardDriveKey {
    # The partition an entry's device path names in its HD node ("HD(1,GPT,<signature>,...)"):
    # partition number, signature type and signature in lowercase, or '' when it names none.
    param($Entry)
    foreach ($node in @(Get-JsonArray -Object $Entry -Name 'devicePathNodes')) {
        $match = [regex]::Match([string]$node, '^HD\((?<number>[^,]*),(?<type>[^,]*),(?<signature>[^,)]*)')
        if ($match.Success) {
            return ($match.Groups['number'].Value + ',' + $match.Groups['type'].Value + ',' + $match.Groups['signature'].Value).ToLowerInvariant()
        }
    }
    return ''
}

function Test-StartsWindowsBootManager {
    # True when the last File node of an entry's device path is the Windows Boot Manager's file,
    # compared as rule V13 compares it: ignoring case, "/" read as "\", repeated separators as one,
    # a leading "\".
    param($Entry)
    $file = ''
    foreach ($node in @(Get-JsonArray -Object $Entry -Name 'devicePathNodes')) {
        $text = [string]$node
        if ($text.StartsWith('File(', [System.StringComparison]::Ordinal) -and $text.EndsWith(')', [System.StringComparison]::Ordinal)) {
            $file = $text.Substring(5, $text.Length - 6)
        }
    }
    if ($file -eq '') {
        return $false
    }
    $normalized = ($file.Trim() -replace '/', '\') -replace '\\+', '\'
    if (-not $normalized.StartsWith('\', [System.StringComparison]::Ordinal)) {
        $normalized = '\' + $normalized
    }
    return [string]::Equals($normalized, $WindowsBootManagerFile, [System.StringComparison]::OrdinalIgnoreCase)
}

function Add-Case {
    # Adds a case the script runs: its one uefiaccess line (always with --json, never confirming a
    # change), the exit code and the blocking check it must give ('' for a line that must not
    # parse), and, when this machine cannot show the case, why its verdict is NOT VERIFIED whatever
    # the line gives; the line still runs and must still show apiCalled false. $AbsentAfter names
    # a folder that must not exist after the line ran. $AlsoFailed names checks that must have
    # failed too in the document's checks, $SuspendOffered asks for bitLocker.suspendOffered true,
    # and $StderrStarts lists the starts of lines standard error must hold, each once at least.
    param(
        [string]$Id,
        [string[]]$Arguments,
        [int]$ExpectedExit,
        [string]$ExpectedCheck,
        [string[]]$AlsoFailed = @(),
        [switch]$SuspendOffered,
        [string[]]$StderrStarts = @(),
        [string]$NotVerifiedReason = '',
        [string]$AbsentAfter = ''
    )
    $script:Cases += New-Object PSObject -Property @{
        Kind              = 'Run'
        Id                = $Id
        Arguments         = $Arguments
        Command           = 'uefiaccess ' + (ConvertTo-ArgumentLine -Arguments $Arguments)
        ExpectedExit      = $ExpectedExit
        ExpectedCheck     = $ExpectedCheck
        AlsoFailed        = @($AlsoFailed)
        SuspendOffered    = [bool]$SuspendOffered
        StderrStarts      = @($StderrStarts)
        NotVerifiedReason = $NotVerifiedReason
        AbsentAfter       = $AbsentAfter
        Reason            = ''
    }
}

function Add-SkippedCase {
    # Adds a case whose line cannot be built on this machine: its row is NOT VERIFIED with $Reason,
    # and $Command describes the line it would have run.
    param(
        [string]$Id,
        [string]$Command,
        [int]$ExpectedExit,
        [string]$ExpectedCheck,
        [string]$Reason
    )
    $script:Cases += New-Object PSObject -Property @{
        Kind              = 'Skipped'
        Id                = $Id
        Arguments         = @()
        Command           = $Command
        ExpectedExit      = $ExpectedExit
        ExpectedCheck     = $ExpectedCheck
        AlsoFailed        = @()
        SuspendOffered    = $false
        StderrStarts      = @()
        NotVerifiedReason = ''
        AbsentAfter       = ''
        Reason            = $Reason
    }
}

function Add-ByHandCase {
    # Adds the row of a case the operator runs by hand and records in place of this row; the
    # script never runs its line.
    param(
        [string]$Id,
        [string]$Command,
        [int]$ExpectedExit,
        [string]$ExpectedCheck,
        [string]$Reason
    )
    $script:Cases += New-Object PSObject -Property @{
        Kind              = 'ByHand'
        Id                = $Id
        Arguments         = @()
        Command           = $Command
        ExpectedExit      = $ExpectedExit
        ExpectedCheck     = $ExpectedCheck
        AlsoFailed        = @()
        SuspendOffered    = $false
        StderrStarts      = @()
        NotVerifiedReason = ''
        AbsentAfter       = ''
        Reason            = $Reason
    }
}

function Get-ExpectedText {
    # The Expected exit column: the exit code and the check that must refuse the line.
    param($Case)
    if ($Case.ExpectedCheck -eq '') {
        return [string]$Case.ExpectedExit + ' (usage error)'
    }
    return [string]$Case.ExpectedExit + ' (' + $Case.ExpectedCheck + ')'
}

function Add-Row {
    param(
        [string]$Case,
        [string]$Command,
        [string]$Expected,
        [string]$Actual,
        [string]$BlockedBy,
        [string]$ApiCalled,
        [string]$Verdict
    )
    $script:Rows += New-Object PSObject -Property @{
        Case      = $Case
        Command   = $Command
        Expected  = $Expected
        Actual    = $Actual
        BlockedBy = $BlockedBy
        ApiCalled = $ApiCalled
        Verdict   = $Verdict
    }
}

function Add-PipelineRun {
    # Notes a command line whose document shows that it reached the write pipeline, which then
    # appended one record to write-log.jsonl.
    param([string]$Step, $Document)
    $script:PipelineRuns += New-Object PSObject -Property @{
        Step          = $Step
        TransactionId = [string](Get-WriteDocumentValue -Document $Document -Name 'transactionId')
    }
}

function Read-TextLines {
    # The lines of the UTF-8 text file $Path (a standard error kept by Invoke-UefiAccess); none
    # when it does not exist.
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return @()
    }
    return @(Get-Content -LiteralPath $Path -Encoding UTF8)
}

function Test-SuspendOffered {
    # True when a write document says that BitLocker needs a choice: suspendOffered true in its
    # bitLocker block, or in its record's when the document has none.
    param($Document)
    $block = Get-JsonProperty -Object $Document -Name 'bitLocker'
    if ($null -eq $block) {
        $block = Get-JsonProperty -Object (Get-JsonProperty -Object $Document -Name 'record') -Name 'bitLocker'
    }
    return ((Get-JsonProperty -Object $block -Name 'suspendOffered') -eq $true)
}

function Get-CaseDetailProblems {
    # What a case asks of its write document beyond the exit code, blockedBy and apiCalled
    # (M3-Design.md section 4.3): the checks that must have failed too ($Case.AlsoFailed),
    # bitLocker.suspendOffered true ($Case.SuspendOffered), and the lines standard error must
    # hold, each found by its start ($Case.StderrStarts). Returns one sentence per problem.
    param($Case, $Document, $Run)
    $problems = @()
    $checks = @(Get-JsonArray -Object $Document -Name 'checks')
    foreach ($name in @($Case.AlsoFailed)) {
        $failed = @($checks | Where-Object { [string](Get-JsonProperty -Object $_ -Name 'name') -ceq $name -and (Get-JsonProperty -Object $_ -Name 'passed') -eq $false })
        if ($failed.Count -eq 0) {
            $problems += 'checks does not show ' + $name + ' failed'
        }
    }
    if ($Case.SuspendOffered -and -not (Test-SuspendOffered -Document $Document)) {
        $problems += 'bitLocker.suspendOffered is not true'
    }
    $starts = @($Case.StderrStarts)
    if ($starts.Count -gt 0) {
        $stderrLines = @(Read-TextLines -Path $Run.Stderr)
        foreach ($start in $starts) {
            $found = @($stderrLines | Where-Object { ([string]$_).StartsWith([string]$start, [System.StringComparison]::Ordinal) })
            if ($found.Count -eq 0) {
                $problems += 'standard error has no line that starts with "' + ([string]$start).TrimEnd() + '"'
            }
        }
    }
    return $problems
}

function Get-CaseResult {
    # Judges one case from its run: the document it printed, the exit code, blockedBy and
    # apiCalled against what the case expects. Returns the Actual, BlockedBy, ApiCalled and
    # Verdict columns and the parsed document when it is a write document.
    param($Case, $Run)

    $read = Read-JsonDocument -Path $Run.Stdout
    $document = $read.Document
    $actual = [string]$Run.ExitCode
    $kind = ''
    $blockedByText = '-'
    $apiCalledText = '-'
    $documentExit = $null
    $problems = @()

    if ($Run.ExitCode -eq -1) {
        $actual = 'none'
        $problems += 'uefiaccess gave no exit code (' + $Run.Note + ')'
    }
    if ($null -eq $document) {
        $problems += $read.Problem
    }
    else {
        $kind = Get-DocumentKind -Document $document
        if ($kind -eq 'write') {
            $blockedBy = Get-WriteDocumentValue -Document $document -Name 'blockedBy'
            $blockedByText = if ($null -eq $blockedBy -or [string]$blockedBy -eq '') { '(none)' } else { [string]$blockedBy }
            $apiCalled = Get-WriteDocumentValue -Document $document -Name 'apiCalled'
            if ($apiCalled -is [bool]) {
                $apiCalledText = ([string]$apiCalled).ToLowerInvariant()
                if ($apiCalled) {
                    $problems += 'apiCalled is true: the firmware was called'
                }
            }
            else {
                $apiCalledText = '(missing)'
                $problems += 'the document does not say whether the firmware was called (apiCalled)'
            }
        }
        elseif ($kind -eq 'error') {
            $documentExit = Get-JsonProperty -Object $document -Name 'exitCode'
            $blockedByText = if ([string]$documentExit -eq '2') { '(usage error)' } else { '(precondition)' }
            $apiCalledText = 'false (error document)'
        }
        else {
            $problems += 'standard output is neither a write document nor the error document'
        }
    }

    if ($Case.NotVerifiedReason -eq '') {
        if ($Run.ExitCode -ne -1 -and $Run.ExitCode -ne $Case.ExpectedExit) {
            $problems += 'exit code ' + $Run.ExitCode + ', expected ' + $Case.ExpectedExit
        }
        if ($Case.ExpectedCheck -eq '') {
            if ($kind -eq 'write') {
                $problems += 'the line was parsed and reached the write pipeline'
            }
            elseif ($kind -eq 'error' -and [string]$documentExit -ne '2') {
                $problems += 'the error document gives exit code ' + [string]$documentExit + ', not 2'
            }
        }
        elseif ($kind -eq 'write' -and $blockedByText -cne $Case.ExpectedCheck) {
            $problems += 'blockedBy ' + $blockedByText + ', expected ' + $Case.ExpectedCheck
        }
        elseif ($kind -eq 'error') {
            $problems += 'the error document instead of a write document: ' + [string](Get-JsonProperty -Object $document -Name 'error')
        }
        if ($kind -eq 'write') {
            $problems += @(Get-CaseDetailProblems -Case $Case -Document $document -Run $Run)
        }
    }
    if ($Case.AbsentAfter -ne '') {
        $absence = Get-AbsenceProblem -Path $Case.AbsentAfter
        if ($absence -ne '') {
            $problems += $absence
        }
    }

    if ($problems.Count -gt 0) {
        $verdict = 'FAIL (' + ($problems -join '; ') + ')'
    }
    elseif ($Case.NotVerifiedReason -ne '') {
        $verdict = 'NOT VERIFIED (' + $Case.NotVerifiedReason + '; the line gave exit code ' + $actual + ', blockedBy ' + $blockedByText + ')'
    }
    else {
        $verdict = 'PASS'
    }

    $writeDocument = $null
    if ($kind -eq 'write') {
        $writeDocument = $document
    }
    return New-Object PSObject -Property @{
        Actual        = $actual
        BlockedBy     = $blockedByText
        ApiCalled     = $apiCalledText
        Verdict       = $verdict
        WriteDocument = $writeDocument
    }
}

function Get-LogRecords {
    # The write records a "write log --json" document lists: under "records", "lines" or "entries",
    # else in the first array whose elements carry a transactionId; an element that is a JSON line
    # as text is parsed (a line that does not parse stays as $null). Found is false when the
    # document holds no such list.
    param($Document)
    $list = $null
    foreach ($name in @('records', 'lines', 'entries')) {
        if (Test-JsonProperty -Object $Document -Name $name) {
            $list = @(Get-JsonArray -Object $Document -Name $name)
            break
        }
    }
    if ($null -eq $list) {
        foreach ($property in @($Document.PSObject.Properties)) {
            $items = @(Get-JsonArray -Object $Document -Name $property.Name)
            if ($items.Count -gt 0 -and (Test-JsonProperty -Object $items[0] -Name 'transactionId')) {
                $list = $items
                break
            }
        }
    }
    $records = @()
    if ($null -ne $list) {
        $records = @(foreach ($item in $list) {
                if ($item -is [string]) {
                    try {
                        $item | ConvertFrom-Json
                    }
                    catch {
                        $null
                    }
                }
                else {
                    $item
                }
            })
    }
    return New-Object PSObject -Property @{
        Found   = ($null -ne $list)
        Records = $records
    }
}

function Get-RecordTime {
    # The time a write record carries in timestampUtc, as a DateTimeOffset in UTC, or $null when
    # it carries none that can be read. Windows PowerShell 5.1's ConvertFrom-Json leaves the text
    # ("2026-09-30T12:15:02Z") as it is; PowerShell 7 turns it into a DateTime, taken as UTC when
    # it carries no zone. Either way the result is compared as a time, never as text.
    param($Record)
    $value = Get-JsonProperty -Object $Record -Name 'timestampUtc'
    if ($null -eq $value) {
        return $null
    }
    if ($value -is [DateTimeOffset]) {
        return $value.ToUniversalTime()
    }
    if ($value -is [DateTime]) {
        if ($value.Kind -eq [System.DateTimeKind]::Unspecified) {
            $value = [DateTime]::SpecifyKind($value, [System.DateTimeKind]::Utc)
        }
        return [DateTimeOffset]($value.ToUniversalTime())
    }
    $parsed = [DateTimeOffset]::MinValue
    $styles = [System.Globalization.DateTimeStyles]'AssumeUniversal, AdjustToUniversal'
    if ([DateTimeOffset]::TryParse([string]$value, $Invariant, $styles, [ref]$parsed)) {
        return $parsed
    }
    return $null
}

function Select-RunRecord {
    # Writes the records of $Records that this run left: written by uefiaccess (host cli) at or
    # after $Since. A record whose time cannot be read is left out, so it is counted as missing.
    param($Records, [DateTimeOffset]$Since)
    foreach ($record in @($Records)) {
        if ($null -eq $record) {
            continue
        }
        if ([string](Get-JsonProperty -Object $record -Name 'host') -ne 'cli') {
            continue
        }
        $at = Get-RecordTime -Record $record
        if ($null -ne $at -and $at -ge $Since) {
            $record
        }
    }
}

# ------------------------------------------------------------------------------------------------
# Preconditions
# ------------------------------------------------------------------------------------------------

$started = Get-Date
$timestamp = $started.ToString('yyyyMMdd-HHmmss', $Invariant)
# The write records carry their time as UTC "yyyy-MM-ddTHH:mm:ssZ", in whole seconds; the records
# of this run are those at or after the start of the run in whole seconds, compared as times
# (Select-RunRecord), whatever form ConvertFrom-Json gives the record's time.
$startedUtcText = $started.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ', $Invariant)
$startedUtc = [DateTimeOffset]::ParseExact($startedUtcText, "yyyy-MM-dd'T'HH:mm:ss'Z'", $Invariant, [System.Globalization.DateTimeStyles]'AssumeUniversal')

Write-Host 'UefiAccess Milestone 3 failure cases (dry runs and blocked requests only; nothing is written)'
Write-Host ('Started ' + $started.ToString('yyyy-MM-dd HH:mm:ss', $Invariant))

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

if ([string]::IsNullOrWhiteSpace($OutsideSnapshotRoot)) {
    $OutsideSnapshotRoot = 'C:\UefiAccess-Evidence\' + $started.ToString('yyyy-MM-dd', $Invariant) + '\m3\H1\snapshots'
}
try {
    $OutsideSnapshotRoot = Get-FullPathWithoutSeparator -Path (Resolve-FullPath -Path $OutsideSnapshotRoot)
}
catch {
    Write-Host ''
    Write-Host ('The FC-20 snapshot root ' + $OutsideSnapshotRoot + ' is not a usable path: ' + $_.Exception.GetBaseException().Message)
    exit 2
}
if (Test-IsSameOrInside -Path $OutsideSnapshotRoot -Folder (Get-FullPathWithoutSeparator -Path $DataRoot)) {
    Write-Host ''
    Write-Host ('The FC-20 snapshot root ' + $OutsideSnapshotRoot + ' lies inside the data folder ' + $DataRoot + ', and FC-20 needs one outside it; pass -OutsideSnapshotRoot with a folder outside ' + $DataRoot + '.')
    exit 2
}

$OutputRoot = Join-Path $DataRoot 'write-tests'
try {
    $outputFolder = Get-FullPathWithoutSeparator -Path ([System.IO.Path]::Combine($OutputRoot, $timestamp))
}
catch {
    Write-Host ''
    Write-Host ('No output folder can be made under ' + $OutputRoot + ': ' + $_.Exception.GetBaseException().Message)
    exit 2
}

# Marker reserved by M1-Design.md section 12 for hardware checks, inherited by the uefiaccess.exe
# processes started below (see .NOTES above).
$env:UEFIACCESS_HW = '1'

# The output folder is created before any command runs, and only by the rule in .DESCRIPTION.
$folderProblem = New-OutputFolder -Folder $outputFolder -DataRoot $DataRoot -Exe $ExePath -TrustedOwners $TrustedOwnerSids -Sddl $ProtectedFolderSddl
if ($folderProblem -ne '') {
    Write-Host ''
    Write-Host $folderProblem
    exit 2
}

Write-Host ('uefiaccess.exe:       ' + $ExePath)
Write-Host ('Output folder:        ' + $outputFolder)
Write-Host ('FC-20 snapshot root:  ' + $OutsideSnapshotRoot)

$snapshotsRoot = Join-Path $DataRoot 'snapshots'
$writesRoot = Join-Path $DataRoot 'writes'
$snapshotsBefore = Get-FolderPresence -Path $snapshotsRoot
$writesBefore = Get-FolderPresence -Path $writesRoot

# ------------------------------------------------------------------------------------------------
# The machine before the cases (read-only)
# ------------------------------------------------------------------------------------------------

$preconditionCode = 0
$readProblem = ''
$readCommand = ''
$readExit = ''
$bootBefore = $null
$orderBefore = $null
$bitLockerState = ''
$bitLockerText = 'not read'
$orderBeforeText = 'not read'
$orderAfterText = 'not read'
$bootCurrentText = 'not read'
$bootNextText = 'not read'
$unknownText = 'not chosen'

$bootBeforeRun = Invoke-UefiAccess -Exe $ExePath -Step 'boot-before' -Arguments @('boot', '--json') -Folder $outputFolder -StdoutFileName 'boot-before.json'
if ($bootBeforeRun.ExitCode -eq 3 -or $bootBeforeRun.ExitCode -eq 4) {
    $preconditionCode = $bootBeforeRun.ExitCode
    $readProblem = 'a precondition failed (' + (Get-ExitCodeText -Code $bootBeforeRun.ExitCode) + '); uefiaccess wrote the sentence to ' + $bootBeforeRun.Stderr
    $readCommand = $bootBeforeRun.Command
    $readExit = [string]$bootBeforeRun.ExitCode
}
else {
    $bootBefore = Read-BootState -Run $bootBeforeRun
    if ($bootBefore.Problem -ne '') {
        $readProblem = $bootBefore.Problem
        $readCommand = $bootBeforeRun.Command
        $readExit = [string]$bootBeforeRun.ExitCode
    }
}

if ($readProblem -eq '') {
    $orderBeforeRun = Invoke-UefiAccess -Exe $ExePath -Step 'bootorder-before' -Arguments @('read', 'BootOrder', '--hex', '--json') -Folder $outputFolder -StdoutFileName 'bootorder-before.json'
    $orderBefore = Read-VariableState -Run $orderBeforeRun
    if ($orderBefore.Problem -ne '') {
        $readProblem = $orderBefore.Problem
        $readCommand = $orderBeforeRun.Command
        $readExit = [string]$orderBeforeRun.ExitCode
    }
}

if ($readProblem -eq '') {
    $checkRun = Invoke-UefiAccess -Exe $ExePath -Step 'write-check-BootOrder' -Arguments @('write', 'check', 'BootOrder', '--json') -Folder $outputFolder -StdoutFileName 'write-check-BootOrder.json'
    if ($checkRun.ExitCode -eq 3 -or $checkRun.ExitCode -eq 4) {
        $preconditionCode = $checkRun.ExitCode
        $readProblem = 'a precondition failed (' + (Get-ExitCodeText -Code $checkRun.ExitCode) + '); uefiaccess wrote the sentence to ' + $checkRun.Stderr
        $readCommand = $checkRun.Command
        $readExit = [string]$checkRun.ExitCode
    }
    else {
        $checkDocument = (Read-JsonDocument -Path $checkRun.Stdout).Document
        if ((Get-DocumentKind -Document $checkDocument) -eq 'write') {
            Add-PipelineRun -Step $checkRun.Step -Document $checkDocument
            $bitLockerState = Get-PipelineBitLockerState -Document $checkDocument
        }
        if ($bitLockerState -eq '') {
            $bitLockerText = 'not reported by write check BootOrder (exit code ' + $checkRun.ExitCode + ')'
        }
        else {
            $bitLockerText = $bitLockerState + ' (write check BootOrder)'
        }
    }
}

# ------------------------------------------------------------------------------------------------
# The cases (M3-Design.md section 4.3): dry runs and blocked requests, each with --json
# ------------------------------------------------------------------------------------------------

if ($readProblem -eq '') {
    $order = @($bootBefore.Order)
    $firstEntry = $order[0]
    $entryNames = @(foreach ($entry in $bootBefore.Entries) { [string](Get-JsonProperty -Object $entry -Name 'name') })
    $orderBeforeText = ($order -join ',') + ' (attributes ' + $orderBefore.Attributes + ', ' + $orderBefore.Size + ' bytes, SHA-256 ' + $orderBefore.Sha256 + ')'
    $bootCurrentText = if ($bootBefore.BootCurrent -eq '') { 'not set or not read' } else { $bootBefore.BootCurrent }
    if ($bootBefore.BootNextPresent -eq $true) {
        $bootNextText = 'set to ' + $bootBefore.BootNextName
    }
    elseif ($bootBefore.BootNextPresent -eq $false) {
        $bootNextText = 'not set'
    }
    else {
        $bootNextText = 'could not be read'
    }

    # The entry that does not exist: Boot0007 as in the matrix, or the first free Boot#### when
    # Boot0007 exists on this machine.
    $unknownEntry = $DefaultUnknownEntry
    $unknownText = $unknownEntry + ' (no such entry on this machine)'
    if ($entryNames -contains $DefaultUnknownEntry) {
        for ($index = 0; $index -le 0xFFFF; $index++) {
            $candidate = 'Boot' + $index.ToString('X4', $Invariant)
            if ($entryNames -notcontains $candidate) {
                $unknownEntry = $candidate
                break
            }
        }
        $unknownText = $unknownEntry + ' (' + $DefaultUnknownEntry + ' exists on this machine; the first free Boot#### stands for it in FC-03, FC-04 and FC-13)'
    }

    # An existing Boot#### that is not in BootOrder (FC-05; Boot0000 on the unit).
    $outsideEntry = ''
    foreach ($name in $entryNames) {
        if ($order -notcontains $name) {
            $outsideEntry = $name
            break
        }
    }

    # A Windows Boot Manager entry on another partition than BootCurrent's (FC-16; Boot0000 on the unit).
    $otherWindowsEntry = ''
    $otherWindowsSkipReason = ''
    $currentEntries = @($bootBefore.Entries | Where-Object { [string](Get-JsonProperty -Object $_ -Name 'name') -eq $bootBefore.BootCurrent })
    $currentDisk = ''
    if ($currentEntries.Count -gt 0) {
        $currentDisk = Get-HardDriveKey -Entry $currentEntries[0]
    }
    if ($currentDisk -eq '') {
        $otherWindowsSkipReason = 'BootCurrent names no entry with a partition, so rule V13 has nothing to compare with'
    }
    else {
        foreach ($entry in $bootBefore.Entries) {
            $name = [string](Get-JsonProperty -Object $entry -Name 'name')
            if ($name -eq $bootBefore.BootCurrent) {
                continue
            }
            if ((Get-JsonProperty -Object $entry -Name 'active') -ne $true -or [string](Get-JsonProperty -Object $entry -Name 'category') -ne 'Boot') {
                continue
            }
            if (-not (Test-StartsWindowsBootManager -Entry $entry)) {
                continue
            }
            if ((Get-HardDriveKey -Entry $entry) -eq $currentDisk) {
                continue
            }
            $otherWindowsEntry = $name
            break
        }
        if ($otherWindowsEntry -eq '') {
            $otherWindowsSkipReason = 'no active Windows Boot Manager entry exists on another partition than BootCurrent''s'
        }
    }

    # The BitLocker state decides whether FC-15 and FC-16 can be shown on this machine.
    $bitLockerChoiceReason = ''
    $otherWindowsPathReason = ''
    if ($bitLockerState -eq '') {
        $bitLockerChoiceReason = 'the BitLocker state could not be read from write check BootOrder'
        $otherWindowsPathReason = $bitLockerChoiceReason
    }
    else {
        if ($StatesNeedingAChoice -notcontains $bitLockerState) {
            $bitLockerChoiceReason = 'BitLocker was ' + $bitLockerState + ' when the script ran (write check BootOrder), so the dry run needs no BitLocker choice'
        }
        if ($StatesWithProtectors -notcontains $bitLockerState) {
            $otherWindowsPathReason = 'BitLocker was ' + $bitLockerState + ' when the script ran (write check BootOrder), and rule V13 applies only while protectors exist'
        }
    }

    # FC-20 must show that nothing is created at the snapshot root, so the folder must not exist yet.
    $snapshotRootProblem = ''
    try {
        if ($null -ne (Get-EntryAttributes -Path $OutsideSnapshotRoot)) {
            $snapshotRootProblem = $OutsideSnapshotRoot + ' exists already, so the run cannot show that nothing is created there; pass -OutsideSnapshotRoot with a folder that does not exist'
        }
    }
    catch {
        $snapshotRootProblem = $OutsideSnapshotRoot + ' could not be checked: ' + $_.Exception.GetBaseException().Message
    }

    $bootNextReason = ''
    if ($bootBefore.BootNextPresent -eq $true) {
        $bootNextReason = 'a one-time boot is set (BootNext ' + $bootBefore.BootNextName + '), so a clear would not be refused'
    }
    elseif ($bootBefore.BootNextPresent -ne $false) {
        $bootNextReason = 'BootNext could not be read'
    }

    $currentOrder = $order -join ','
    $orderWithUnknown = (@($order) + @($unknownEntry)) -join ','
    $orderWithOutside = (@($order) + @($outsideEntry)) -join ','
    $orderWithDuplicate = (@($order) + @($firstEntry)) -join ','
    $orderWithOneLeftOut = ''
    if ($order.Count -ge 2) {
        $orderWithOneLeftOut = @($order)[0..($order.Count - 2)] -join ','
    }

    Add-Case -Id 'FC-01' -Arguments @('write', 'check', 'Timeout', '--json') -ExpectedExit 7 -ExpectedCheck 'whitelist'
    Add-Case -Id 'FC-02a' -Arguments @('write', 'check', 'PK', '--json') -ExpectedExit 7 -ExpectedCheck 'whitelist' -AlsoFailed @('authenticatedWrite')
    Add-Case -Id 'FC-02b' -Arguments @('write', 'check', 'db', '--guid', 'd719b2cb-3d3a-4596-a3bc-dad00e67656f', '--json') -ExpectedExit 7 -ExpectedCheck 'whitelist' -AlsoFailed @('authenticatedWrite')
    Add-Case -Id 'FC-03' -Arguments @('write', 'bootorder', '--order', $orderWithUnknown, '--json') -ExpectedExit 7 -ExpectedCheck 'entriesExist'
    Add-Case -Id 'FC-04' -Arguments @('write', 'bootnext', $unknownEntry, '--json') -ExpectedExit 7 -ExpectedCheck 'entriesExist'
    if ($outsideEntry -ne '') {
        Add-Case -Id 'FC-05' -Arguments @('write', 'bootorder', '--order', $orderWithOutside, '--json') -ExpectedExit 7 -ExpectedCheck 'permutation'
    }
    else {
        Add-SkippedCase -Id 'FC-05' -Command 'uefiaccess write bootorder --order <the current order>,<a Boot#### not in BootOrder> --json' -ExpectedExit 7 -ExpectedCheck 'permutation' -Reason 'every Boot#### on this machine is in BootOrder, so none can be added'
    }
    Add-Case -Id 'FC-06a' -Arguments @('write', 'bootorder', '--order', $orderWithDuplicate, '--json') -ExpectedExit 7 -ExpectedCheck 'permutation'
    if ($order.Count -ge 2) {
        Add-Case -Id 'FC-06b' -Arguments @('write', 'bootorder', '--order', $orderWithOneLeftOut, '--json') -ExpectedExit 7 -ExpectedCheck 'permutation'
    }
    else {
        Add-SkippedCase -Id 'FC-06b' -Command 'uefiaccess write bootorder --order <the current order without its last entry> --json' -ExpectedExit 7 -ExpectedCheck 'permutation' -Reason 'BootOrder holds one entry, and leaving it out leaves no order to name'
    }
    Add-Case -Id 'FC-07' -Arguments @('write', 'bootorder', '--order', $currentOrder, '--json') -ExpectedExit 7 -ExpectedCheck 'noChange'
    Add-ByHandCase -Id 'FC-13' -Command ('uefiaccess write bootnext ' + $unknownEntry + ' --confirm --json, by hand from a standard (not elevated) prompt, with whoami /groups beside it') -ExpectedExit 4 -ExpectedCheck 'preconditions' -Reason 'NOT RUN BY RIG (the operator replaces this row with protocol step H2: exit 4 before any call; exit 7 entriesExist means the prompt was elevated, and the line is repeated from a standard prompt)'
    Add-Case -Id 'FC-15' -Arguments @('write', 'bootnext', $firstEntry, '--json') -ExpectedExit 7 -ExpectedCheck 'bitLocker' -SuspendOffered -StderrStarts $BitLockerChoiceLines -NotVerifiedReason $bitLockerChoiceReason
    if ($otherWindowsEntry -ne '') {
        Add-Case -Id 'FC-16' -Arguments @('write', 'bootnext', $otherWindowsEntry, '--json') -ExpectedExit 7 -ExpectedCheck 'otherWindowsPath' -NotVerifiedReason $otherWindowsPathReason
    }
    else {
        Add-SkippedCase -Id 'FC-16' -Command 'uefiaccess write bootnext <a Windows Boot Manager entry on another partition> --json' -ExpectedExit 7 -ExpectedCheck 'otherWindowsPath' -Reason $otherWindowsSkipReason
    }
    if ($snapshotRootProblem -eq '') {
        Add-Case -Id 'FC-20' -Arguments @('write', 'bootnext', $firstEntry, '--bitlocker', 'continue', '--snapshot-root', $OutsideSnapshotRoot, '--json') -ExpectedExit 7 -ExpectedCheck 'snapshotRoot' -AbsentAfter $OutsideSnapshotRoot
    }
    else {
        Add-SkippedCase -Id 'FC-20' -Command ('uefiaccess write bootnext ' + $firstEntry + ' --bitlocker continue --snapshot-root ' + $OutsideSnapshotRoot + ' --json') -ExpectedExit 7 -ExpectedCheck 'snapshotRoot' -Reason $snapshotRootProblem
    }
    if ($bootNextReason -eq '') {
        Add-Case -Id 'FC-26' -Arguments @('write', 'bootnext', '--clear', '--json') -ExpectedExit 7 -ExpectedCheck 'bootNextTarget'
    }
    else {
        Add-SkippedCase -Id 'FC-26' -Command 'uefiaccess write bootnext --clear --json' -ExpectedExit 7 -ExpectedCheck 'bootNextTarget' -Reason $bootNextReason
    }
    Add-Case -Id 'FC-27a' -Arguments @('write', 'bootorder', '--order', $currentOrder, '--dry-run', '--force', '--json') -ExpectedExit 2 -ExpectedCheck ''
    Add-Case -Id 'FC-27b' -Arguments @('write', 'bootnext', $firstEntry, '--bitlocker', 'later', '--json') -ExpectedExit 2 -ExpectedCheck ''

    foreach ($case in $script:Cases) {
        if ($case.Kind -eq 'ByHand') {
            Add-Row -Case $case.Id -Command $case.Command -Expected (Get-ExpectedText -Case $case) -Actual 'NOT RUN BY RIG' -BlockedBy '-' -ApiCalled '-' -Verdict $case.Reason
            continue
        }
        if ($case.Kind -eq 'Skipped') {
            Write-Host ''
            Write-Host ('==> ' + $case.Id + ': not run: ' + $case.Reason)
            Add-Row -Case $case.Id -Command $case.Command -Expected (Get-ExpectedText -Case $case) -Actual 'not run' -BlockedBy '-' -ApiCalled '-' -Verdict ('NOT VERIFIED (' + $case.Reason + ')')
            continue
        }
        $run = Invoke-UefiAccess -Exe $ExePath -Step $case.Id -Arguments $case.Arguments -Folder $outputFolder -StdoutFileName ($case.Id + '.json')
        $result = Get-CaseResult -Case $case -Run $run
        if ($null -ne $result.WriteDocument) {
            Add-PipelineRun -Step $case.Id -Document $result.WriteDocument
        }
        Write-Host ('    ' + $result.Verdict)
        Add-Row -Case $case.Id -Command $run.Command -Expected (Get-ExpectedText -Case $case) -Actual $result.Actual -BlockedBy $result.BlockedBy -ApiCalled $result.ApiCalled -Verdict $result.Verdict
    }

    # --------------------------------------------------------------------------------------------
    # After the cases: one record per line that reached the pipeline, and the store unchanged
    # --------------------------------------------------------------------------------------------

    $logRun = Invoke-UefiAccess -Exe $ExePath -Step 'write-log' -Arguments @('write', 'log', '--last', '100', '--json') -Folder $outputFolder -StdoutFileName 'write-log.json'
    $logProblems = @()
    $logApiCalled = '-'
    if ($logRun.ExitCode -ne 0) {
        $logProblems += 'exit code ' + $logRun.ExitCode + ', expected 0'
    }
    $logRead = Read-JsonDocument -Path $logRun.Stdout
    if ($null -eq $logRead.Document) {
        $logProblems += $logRead.Problem
    }
    elseif ((Get-DocumentKind -Document $logRead.Document) -eq 'error') {
        # uefiaccess could not show the list (write-log.jsonl unreadable, or a precondition); say why.
        $logProblems += 'uefiaccess could not show the write log: ' + [string](Get-JsonProperty -Object $logRead.Document -Name 'error')
    }
    else {
        $log = Get-LogRecords -Document $logRead.Document
        if (-not $log.Found) {
            $logProblems += 'the document holds no list of write records'
        }
        else {
            $thisRun = @(Select-RunRecord -Records $log.Records -Since $startedUtc)
            $called = @($thisRun | Where-Object { (Get-JsonProperty -Object $_ -Name 'apiCalled') -ne $false })
            $logApiCalled = if ($called.Count -eq 0) { 'false in all ' + $thisRun.Count + ' records' } else { 'true or missing in ' + $called.Count + ' of ' + $thisRun.Count + ' records' }
            if ($thisRun.Count -ne $script:PipelineRuns.Count) {
                $logProblems += [string]$thisRun.Count + ' records of this run, expected ' + $script:PipelineRuns.Count + ' (one per line that reached the pipeline)'
            }
            if ($called.Count -gt 0) {
                $logProblems += [string]$called.Count + ' records of this run do not show apiCalled false'
            }
            $loggedIds = @($thisRun | ForEach-Object { [string](Get-JsonProperty -Object $_ -Name 'transactionId') })
            $unlogged = @($script:PipelineRuns | Where-Object { $_.TransactionId -eq '' -or $loggedIds -notcontains $_.TransactionId } | ForEach-Object { $_.Step })
            if ($unlogged.Count -gt 0) {
                $logProblems += 'no record with the transaction id of ' + ($unlogged -join ', ')
            }
        }
    }
    $logVerdict = if ($logProblems.Count -eq 0) { 'PASS (' + $script:PipelineRuns.Count + ' records of this run, one per line that reached the pipeline, each with apiCalled false)' } else { 'FAIL (' + ($logProblems -join '; ') + ')' }
    Write-Host ('    ' + $logVerdict)
    Add-Row -Case 'WRITE-LOG' -Command $logRun.Command -Expected '0 (one record per line that reached the pipeline)' -Actual ([string]$logRun.ExitCode) -BlockedBy '-' -ApiCalled $logApiCalled -Verdict $logVerdict

    $orderAfterRun = Invoke-UefiAccess -Exe $ExePath -Step 'bootorder-after' -Arguments @('read', 'BootOrder', '--hex', '--json') -Folder $outputFolder -StdoutFileName 'bootorder-after.json'
    $orderAfter = Read-VariableState -Run $orderAfterRun
    $bootAfterRun = Invoke-UefiAccess -Exe $ExePath -Step 'boot-after' -Arguments @('boot', '--json') -Folder $outputFolder -StdoutFileName 'boot-after.json'
    $bootAfter = Read-BootState -Run $bootAfterRun
    $storeProblems = @()
    if ($orderAfter.Problem -ne '') {
        $storeProblems += $orderAfter.Problem
    }
    else {
        $orderAfterText = (@($bootAfter.Order) -join ',') + ' (attributes ' + $orderAfter.Attributes + ', ' + $orderAfter.Size + ' bytes, SHA-256 ' + $orderAfter.Sha256 + ')'
        if ($orderAfter.Sha256 -ne $orderBefore.Sha256 -or $orderAfter.Attributes -ne $orderBefore.Attributes -or $orderAfter.Size -ne $orderBefore.Size) {
            $storeProblems += 'BootOrder changed: before ' + $orderBeforeText + ', after ' + $orderAfterText
        }
    }
    if ($bootAfter.Problem -ne '') {
        $storeProblems += $bootAfter.Problem
    }
    else {
        if ((@($bootAfter.Order) -join ',') -ne $currentOrder) {
            $storeProblems += 'the boot order reads ' + (@($bootAfter.Order) -join ',') + ' after the run, ' + $currentOrder + ' before it'
        }
        if ($bootAfter.BootNextPresent -ne $bootBefore.BootNextPresent -or $bootAfter.BootNextName -ne $bootBefore.BootNextName) {
            $storeProblems += 'BootNext changed during the run'
        }
    }
    $storeVerdict = if ($storeProblems.Count -eq 0) { 'PASS (BootOrder ' + $orderBeforeText + ' before and after; BootNext ' + $bootNextText + ' before and after)' } else { 'FAIL (' + ($storeProblems -join '; ') + ')' }
    Write-Host ''
    Write-Host ('==> BootOrder and BootNext before and after: ' + $storeVerdict)
    Add-Row -Case 'STORE' -Command 'uefiaccess read BootOrder --hex --json and uefiaccess boot --json, before and after' -Expected '0 and 0 (unchanged)' -Actual ([string]$orderBeforeRun.ExitCode + ' and ' + [string]$orderAfterRun.ExitCode) -BlockedBy '-' -ApiCalled '-' -Verdict $storeVerdict
}
else {
    Write-Host ''
    Write-Host ('The machine could not be read before the cases: ' + $readProblem + '. No case was run.')
    Add-Row -Case 'PRE-READ' -Command $readCommand -Expected '0' -Actual $readExit -BlockedBy '-' -ApiCalled '-' -Verdict ('FAIL (' + $readProblem + '; no case was run)')
}

# ------------------------------------------------------------------------------------------------
# Summary file
# ------------------------------------------------------------------------------------------------

$snapshotsAfter = Get-FolderPresence -Path $snapshotsRoot
$writesAfter = Get-FolderPresence -Path $writesRoot
$failCount = @($script:Rows | Where-Object { $_.Verdict.StartsWith('FAIL', [System.StringComparison]::Ordinal) }).Count
$passCount = @($script:Rows | Where-Object { $_.Verdict.StartsWith('PASS', [System.StringComparison]::Ordinal) }).Count
$notVerifiedCount = @($script:Rows | Where-Object { $_.Verdict.StartsWith('NOT VERIFIED', [System.StringComparison]::Ordinal) }).Count
$byHandCount = @($script:Rows | Where-Object { $_.Verdict.StartsWith('NOT RUN BY RIG', [System.StringComparison]::Ordinal) }).Count

$summaryLines = @()
$summaryLines += 'UefiAccess Milestone 3 failure cases (M3-Design.md section 4.3; dry runs and blocked requests only)'
$summaryLines += 'Started:          ' + $started.ToString('yyyy-MM-dd HH:mm:ss', $Invariant)
$summaryLines += 'Finished:         ' + (Get-Date).ToString('yyyy-MM-dd HH:mm:ss', $Invariant)
$summaryLines += 'uefiaccess.exe:   ' + $ExePath
$summaryLines += 'Output folder:    ' + $outputFolder
$summaryLines += 'BootOrder before: ' + $orderBeforeText
$summaryLines += 'BootOrder after:  ' + $orderAfterText
$summaryLines += 'BootCurrent:      ' + $bootCurrentText
$summaryLines += 'BootNext before:  ' + $bootNextText
$summaryLines += 'BitLocker state:  ' + $bitLockerText
$summaryLines += 'Unknown entry:    ' + $unknownText
$summaryLines += 'FC-20 root:       ' + $OutsideSnapshotRoot + ' (outside the data folder; this script never creates it)'
$summaryLines += 'Data folders:     snapshots ' + $snapshotsBefore + ' before the run, ' + $snapshotsAfter + ' after; writes ' + $writesBefore + ' before, ' + $writesAfter + ' after (the write pipeline creates both at stage S9, dry runs included)'
$summaryLines += 'Verdicts:         ' + $passCount + ' PASS, ' + $failCount + ' FAIL, ' + $notVerifiedCount + ' NOT VERIFIED, ' + $byHandCount + ' NOT RUN BY RIG'
$summaryLines += ''
$tableLines = @()
$tableLines += 'Case | Command | Expected exit | Actual | BlockedBy | ApiCalled | Verdict'
foreach ($row in $script:Rows) {
    $tableLines += (@($row.Case, $row.Command, $row.Expected, $row.Actual, $row.BlockedBy, $row.ApiCalled, $row.Verdict) -join ' | ')
}
$summaryLines += $tableLines
[System.IO.File]::WriteAllLines((Join-Path $outputFolder 'summary.txt'), [string[]]$summaryLines, $Utf8NoBom)

Write-Host ''
Write-Host 'Failure cases'
foreach ($line in $tableLines) {
    Write-Host ('  ' + $line)
}
Write-Host ''
Write-Host ('Summary: ' + (Join-Path $outputFolder 'summary.txt'))
Write-Host 'Replace the FC-13 row with the result of protocol step H2 (by hand, from a standard prompt).'
Write-Host 'Copy the output folder into the evidence folder of protocol step H1 through the secret filter.'

# ------------------------------------------------------------------------------------------------
# Exit code: 0 only when no row FAILs
# ------------------------------------------------------------------------------------------------

if ($preconditionCode -ne 0) {
    exit $preconditionCode
}
if ($failCount -eq 0) {
    exit 0
}
exit 1
