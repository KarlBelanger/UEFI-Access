# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.

<#
.SYNOPSIS
    The output-folder functions shared by tools\gate\Run-Gate.ps1 and
    tools\write-tests\Run-FailureCases.ps1.

.DESCRIPTION
    Both scripts run elevated and write their output into a new timestamped folder, by default
    inside the tool's data root, C:\ProgramData\UefiAccess. Inside the data root they follow the
    rule uefiaccess follows there (Logging.PrepareDataFolderFor), so that a standard user cannot
    redirect what an elevated script writes: when the data root is missing, "uefiaccess info" runs
    once first, its output not kept, so that the tool itself creates the root with its protected
    access list; every existing folder from the data root down must be a plain folder, not a
    junction, symbolic link or other reparse point, owned by SYSTEM, Administrators or
    TrustedInstaller; the missing ones are created with the tool's protected access list (SYSTEM
    and Administrators full control, Users read) and checked again; the output folder itself must
    be new. Outside the data root the folder is the operator's choice and is only required to be
    new.

    Exported:
    New-OutputFolder             makes a run's new output folder by that rule and returns '' or
                                 the one sentence that says why the script stops.
    Get-FullPathWithoutSeparator the full path with "." and ".." resolved and no trailing separator.
    Test-IsSameOrInside          whether a path is a folder or lies inside it, as Windows compares paths.
    Get-EntryAttributes          the attributes of a file-system entry itself, or $null when nothing is there.

    The helpers behind New-OutputFolder (Get-FolderProblem, New-ProtectedFolder, Get-AccountName
    and Invoke-UefiAccessQuietly) stay inside the module. The functions were moved here unchanged
    from Run-Gate.ps1; New-OutputFolder was New-GateOutputFolder there.

.NOTES
    Windows PowerShell 5.1 compatible (no PowerShell 7 syntax).
    Imported by path from each script:
        Import-Module -Name (Join-Path $PSScriptRoot '..\common\DataFolder.psm1')
    The module sets strict mode and stops on every error in its own scope. The functions of a
    module do not see the preference variables of the script that imports it, and New-OutputFolder
    relies on an error stopping it (a folder that cannot be created, an owner that cannot be read)
    so that it returns the sentence instead of going on.
    RunGateOutputFolderTests and RunFailureCasesOutputFolderTests run these functions in Windows
    PowerShell 5.1 against a temporary stand-in for the data root, never against ProgramData.
#>

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Invoke-UefiAccessQuietly {
    # Runs uefiaccess.exe for what it does, not for what it prints: nothing it writes to standard
    # output or standard error is shown or kept. Returns the exit code, or -1 when it could not start.
    param(
        [string]$Exe,
        [string[]]$Arguments
    )

    # Standard error merged into the discarded output is not an error of this script.
    $ErrorActionPreference = 'Continue'
    try {
        $null = & $Exe $Arguments 2>&1
        return [int]$LASTEXITCODE
    }
    catch {
        return -1
    }
}

function Get-FullPathWithoutSeparator {
    # The full path with "." and ".." resolved and no trailing separator (a drive root keeps its own).
    param([string]$Path)
    $full = [System.IO.Path]::GetFullPath($Path)
    if ($full.Length -gt [System.IO.Path]::GetPathRoot($full).Length) {
        $full = $full.TrimEnd([char]'\')
    }
    return $full
}

function Test-IsSameOrInside {
    # True when $Path is $Folder or lies inside it. Both are full paths without a trailing
    # separator, compared without regard to case, as Windows and uefiaccess compare them.
    param(
        [string]$Path,
        [string]$Folder
    )
    return ([string]::Equals($Path, $Folder, [System.StringComparison]::OrdinalIgnoreCase) -or
        $Path.StartsWith($Folder + '\', [System.StringComparison]::OrdinalIgnoreCase))
}

function Get-EntryAttributes {
    # The attributes of the file-system entry itself, never of what a link points to, or $null when
    # nothing is there.
    param([string]$Path)
    try {
        return [System.IO.File]::GetAttributes($Path)
    }
    catch [System.IO.FileNotFoundException], [System.IO.DirectoryNotFoundException] {
        return $null
    }
}

function Get-AccountName {
    # The account name of a security identifier, or the identifier itself when it cannot be translated.
    param([System.Security.Principal.SecurityIdentifier]$Sid)
    if ($null -eq $Sid) {
        return 'an unknown account'
    }
    try {
        return $Sid.Translate([System.Security.Principal.NTAccount]).Value
    }
    catch {
        return $Sid.Value
    }
}

function Get-FolderProblem {
    # '' when this elevated script may write through the existing $Folder by the rule uefiaccess
    # applies under its data root (Logging.PrepareDataFolderFor): a plain folder, not a junction,
    # symbolic link or other reparse point, owned by one of $TrustedOwners. Otherwise the one
    # sentence that says why not and what to do. Nothing is changed, only read.
    param(
        [string]$Folder,
        [string[]]$TrustedOwners
    )

    $redirect = ', so a standard user could redirect what this elevated script writes there; rename or delete ' + $Folder + ' from an administrator prompt and run this script again.'
    try {
        $attributes = Get-EntryAttributes -Path $Folder
        if ($null -eq $attributes) {
            return $Folder + ' disappeared while it was being checked; run this script again.'
        }
        if (($attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            return $Folder + ' is a junction, symbolic link or other reparse point, not a plain folder' + $redirect
        }
        if (($attributes -band [System.IO.FileAttributes]::Directory) -eq 0) {
            return $Folder + ' is a file, not a folder; rename or delete it from an administrator prompt and run this script again.'
        }

        try {
            $owner = (Get-Acl -LiteralPath $Folder).GetOwner([System.Security.Principal.SecurityIdentifier])
        }
        catch {
            return 'The owner of ' + $Folder + ' cannot be read (' + $_.Exception.GetBaseException().Message.TrimEnd('.') + ')' + $redirect
        }
        if ($null -ne $owner -and $TrustedOwners -contains $owner.Value) {
            return ''
        }
        return $Folder + ' is owned by ' + (Get-AccountName -Sid $owner) + ', not by Administrators or SYSTEM' + $redirect
    }
    catch {
        return $Folder + ' cannot be checked: ' + $_.Exception.GetBaseException().Message
    }
}

function New-ProtectedFolder {
    # Creates $Folder, whose parent exists, with the access list $Sddl applied at creation, as
    # uefiaccess creates its folders under the data root (Logging.CreateProtectedDirectory), so the
    # folder never carries the entries it would otherwise inherit. No existing access list is
    # changed: an existing folder is left as it is, and the caller checks the result either way.
    param(
        [string]$Folder,
        [string]$Sddl
    )

    $security = New-Object System.Security.AccessControl.DirectorySecurity
    $security.SetSecurityDescriptorSddlForm($Sddl)
    $directory = New-Object System.IO.DirectoryInfo($Folder)
    if ($PSVersionTable.PSEdition -eq 'Core') {
        # PowerShell 7 runs on .NET, where creation with an access list is an extension method.
        [System.IO.FileSystemAclExtensions]::Create($directory, $security)
    }
    else {
        $directory.Create($security)
    }
}

function New-OutputFolder {
    # Creates $Folder, this run's new output folder, and returns '' or the one sentence that says
    # why the script stops. Inside the data root it follows the tool's rule: the tool creates the
    # root itself when it is missing ($Exe runs "info" once for that), every existing folder from
    # the root down is checked, and each missing one is created protected and checked again (a
    # folder that appeared in the meantime was not created by this script). Outside the data root
    # the folder is the operator's choice, which uefiaccess does not check either; it must be new.
    param(
        [string]$Folder,
        [string]$DataRoot,
        [string]$Exe,
        [string[]]$TrustedOwners,
        [string]$Sddl
    )

    try {
        $Folder = Get-FullPathWithoutSeparator -Path $Folder
        $DataRoot = Get-FullPathWithoutSeparator -Path $DataRoot
        $exists = 'The output folder ' + $Folder + ' already exists, and this script writes only into a new one; rename or delete it from an administrator prompt and run this script again.'

        if (-not (Test-IsSameOrInside -Path $Folder -Folder $DataRoot)) {
            if ($null -ne (Get-EntryAttributes -Path $Folder)) {
                return $exists
            }
            $null = New-Item -ItemType Directory -Path $Folder
            return ''
        }

        if ($null -eq (Get-EntryAttributes -Path $DataRoot)) {
            Write-Host ('The data folder ' + $DataRoot + ' does not exist yet: "uefiaccess info" runs once first, so that the tool creates it with its protected access list; the output of that run is not kept.')
            $code = Invoke-UefiAccessQuietly -Exe $Exe -Arguments @('info')
            if ($null -eq (Get-EntryAttributes -Path $DataRoot)) {
                return '"uefiaccess info" (exit code ' + $code + ') did not create the data folder ' + $DataRoot + ', so the output folder cannot be created safely; run "uefiaccess info" from this elevated prompt to see why, then run this script again.'
            }
        }

        $problem = Get-FolderProblem -Folder $DataRoot -TrustedOwners $TrustedOwners
        if ($problem -ne '') {
            return $problem
        }

        $parts = @($Folder.Substring($DataRoot.Length).Split([char]'\') | Where-Object { $_ -ne '' })
        if ($parts.Count -eq 0) {
            return $exists
        }
        $current = $DataRoot
        for ($i = 0; $i -lt $parts.Count; $i++) {
            $current = [System.IO.Path]::Combine($current, $parts[$i])
            if ($null -ne (Get-EntryAttributes -Path $current)) {
                if ($i -eq $parts.Count - 1) {
                    return $exists
                }
            }
            else {
                try {
                    New-ProtectedFolder -Folder $current -Sddl $Sddl
                }
                catch {
                    return $current + ' cannot be created: ' + $_.Exception.GetBaseException().Message
                }
            }
            $problem = Get-FolderProblem -Folder $current -TrustedOwners $TrustedOwners
            if ($problem -ne '') {
                return $problem
            }
        }
        return ''
    }
    catch {
        return 'The output folder ' + $Folder + ' cannot be created: ' + $_.Exception.GetBaseException().Message
    }
}

Export-ModuleMember -Function New-OutputFolder, Get-FullPathWithoutSeparator, Test-IsSameOrInside, Get-EntryAttributes
