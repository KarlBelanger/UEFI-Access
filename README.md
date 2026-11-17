# UefiAccess

UefiAccess is a Windows tool that lets blind and low-vision users read, and safely change, UEFI
firmware settings from inside Windows through stock, screen-reader-friendly interfaces (NVDA and
Narrator are the reference environments). It reads every UEFI variable Windows can see, decodes the
boot and Secure Boot variables, shows a short read-only list of Dell BIOS settings, and can change
exactly two variables, the boot order (BootOrder) and the one-time boot (BootNext), through one
guarded write pipeline with a snapshot and a confirmation dialog.

It is built as a core library (`UefiAccess.Core`), a command-line tool (`uefiaccess.exe`), a
presentation library (`UefiAccess.Presentation`, no WPF), and a WPF application (`UefiAccess.App.exe`),
plus a small UI Automation recorder used for accessibility diagnostics.

## Supported hardware, and a safety warning

UefiAccess is validated and supported only on the 2014 Dell OptiPlex 7020 with BIOS A18. Reading
UEFI variables works on any UEFI Windows 11 machine, and the offline baseline view works on any
Windows PC, but everything else (the Dell BIOS settings view, checked against BIOS A18 only, and all
firmware writes) is verified on that one model and BIOS version only.

Writing firmware variables can affect whether the machine starts. This tool changes the boot
order and the one-time boot; a bad change, or firmware that behaves differently from the reference
unit, can leave a machine that starts the wrong entry or does not start Windows. Writes are restricted
by design to a configured "enabled unit" (see "Write guarantee"); on any other machine the write is
refused at the confirmation step. Using this tool to change firmware on any machine other than the
validated one is unsupported and at your own risk. If a machine does not start normally after a
change, see [docs/Recovery.md](docs/Recovery.md).

The build is unsigned; there is no installer, no JAWS support and no localization.

## Download

Prebuilt releases are on the [Releases page](https://github.com/KarlBelanger/UEFI-Access/releases/latest).
Each release includes a self-contained Windows x64 build with the .NET 8 runtime bundled, so nothing
else needs to be installed:

- `UefiAccess.App.exe`, the accessible application.
- `uefiaccess.exe`, the command-line tool.

Unzip it anywhere and run. The application requests administrator rights itself; the command-line tool
needs an elevated prompt. Reading UEFI variables works on any UEFI Windows 11 machine, but firmware
writes are refused on every machine except the supported unit (see the safety warning above and the
write guarantee below).

## Build from source

You need the .NET 8 SDK (x64) to build and test, and the machine-wide .NET 8 Desktop Runtime (x64) to
run. From the repository root:

```
dotnet build UefiAccess.sln -c Release
dotnet test UefiAccess.sln -c Release
```

The command-line tool needs an elevated prompt; the application requests elevation itself (its
manifest is `requireAdministrator`), because reading firmware variables needs
`SeSystemEnvironmentPrivilege`, which exists only in an elevated token:

```
src\UefiAccess.Cli\bin\Release\net8.0-windows\uefiaccess.exe info
src\UefiAccess.App\bin\Release\net8.0-windows\UefiAccess.App.exe
```

`tools\sample-baseline` is a synthetic baseline folder you can open offline on any PC, to walk every
screen without firmware access. Full details, log locations and troubleshooting are in
[docs/BUILD.md](docs/BUILD.md).

## Documentation

- [docs/BUILD.md](docs/BUILD.md): building, testing, running, log and baseline locations, troubleshooting.
- [docs/CLI.md](docs/CLI.md): the full `uefiaccess` command-line reference.
- [docs/Keyboard-Map.md](docs/Keyboard-Map.md): every key, region and focus rule of the application.
- [docs/Developer-Guide.md](docs/Developer-Guide.md): architecture, the safety model, and how to extend the tool.
- [docs/Dell-Interface-Setup.md](docs/Dell-Interface-Setup.md): what the Dell settings view needs on a machine.
- [docs/Dell-Attribute-Map.md](docs/Dell-Attribute-Map.md): the Dell BIOS settings the tool shows, with their attribute names and values.
- [docs/Recovery.md](docs/Recovery.md): what to do if the machine does not start normally after a boot change.

## Write guarantee

The tool can change exactly two firmware variables under the EFI global GUID, and nothing else:
BootOrder, put in a new order of its own entries (none added, removed or repeated, the same byte
count, attributes `NV BS RT`), and BootNext, set to one existing, active boot entry or removed. It can
also suspend BitLocker for one restart, and only when the person chooses that for the change in hand
(the box `Suspend BitLocker for one restart` in the `Confirm change` dialog, or `--bitlocker suspend`
with `uefiaccess write ... --confirm`). No Secure Boot key or state, Boot#### entry, BIOS setting,
TPM, registry value or boot-configuration entry is ever changed, and nothing is flashed. The Dell
BIOS settings view shows values and changes none of them.

Changes are enabled on a configured "enabled unit" only, recognized by its Windows installation. The
code holds a one-way hash of that installation's Windows machine ID and compares it with the machine
it runs on; neither the ID nor the hash is printed, logged or recorded. On every other machine the dry
run, the Change controls and the `Confirm change` dialog work, and the confirmation is refused before
anything is snapshotted, suspended or written (the check `expectedModel` for another model or BIOS
version, `expectedUnit` for another unit). The check recognizes the Windows installation, not the
hardware: a disk clone, a moved drive, a restored system image or a shared deployment image can carry
the same machine ID onto another machine, which would then be taken for the unit. Before deploying the
tool for anyone else, tie the check to the hardware (see
[docs/Developer-Guide.md](docs/Developer-Guide.md), section 5.2). A reinstall of Windows on the enabled
unit gives it a new machine ID, and every change is then refused until the code is updated in a
reviewed commit.

Every change goes through the one write pipeline in `src/UefiAccess.Core/Safety`
(`src/UefiAccess.Core/Safety/README.md` is its short form), which the command-line tool and the
application both call: a dry run of every check on a fresh read, a confirmation bound to that exact
plan, a snapshot folder under `C:\ProgramData\UefiAccess\snapshots` written and read back before
anything else, two drift reads, exactly one write call and one verification read, and one record in
`C:\ProgramData\UefiAccess\writes\write-log.jsonl` for every attempt. No stage retries, and no option
in any build skips a check. The write path lives in exactly three files, which a review of it must
read whole:

- `src/UefiAccess.Core/Native/Kernel32.cs`: the one write import, declared once.
- `src/UefiAccess.Core/Safety/FirmwareVariableWriter.cs`: the one call of it, in
  `CommitOnce(WritePlan)`, which `WriteTransaction.Commit` calls from exactly one line.
- `src/UefiAccess.Core/Safety/BitLockerSuspender.cs`: the one-restart suspend (`DisableKeyProtectors`
  with an explicit count of 1), the only BitLocker method besides the three read-only readers.

Everything else only reads the firmware. The application has no control whose label contains Apply,
Save, Toggle, Edit, Write or Set; it changes a variable only through its `Confirm change` dialog.

## How that is enforced

The guarantee above is pinned by static guard tests that run on every `dotnet test`, scan the source
and the compiled assemblies, and print `file:line` for each hit. A guard is changed only in a commit
of its own, reviewed line by line. The guards are the floor; review of the three write-path files is
the real control.

- `NoWriteGuardTests` scans every line of `src/**/*.cs`, `src/**/*.xaml`, `tools/**/*.ps1` and
  `tools/**/*.psm1` (not `bin/` or `obj/`), comments and string literals included, and fails on any
  line that: names a firmware, Secure Boot, BitLocker, TPM, registry or boot-configuration write API
  (for example `SetFirmwareEnvironmentVariable`, `NtSetSystemEnvironmentValue`, the registry writers,
  `bcdedit /set`, `reg add`, `Set-SecureBootUEFI`, the BitLocker and TPM cmdlets); invokes any WMI
  method other than the three read-only BitLocker readers (`GetKeyProtectors`, `GetKeyProtectorType`,
  `GetKeyProtectorPlatformValidationProfile`); runs PowerShell held in a string or encoded
  (`Invoke-Expression`, `-EncodedCommand`, and the like); or passes `cctk` any option other than
  exactly `--version` or `--secureboot`. The test also pins by reflection the constants the scan
  trusts and checks the source of the three process launches.
- The Dell settings reader (`src/UefiAccess.Core/Settings/Dell/CctkReader.cs`) runs cctk to read
  nine settings, one option name per run and never a value: `--secureboot`, `--embsataraid`, `--tpm`,
  `--tpmactivation`, `--virtualization`, `--vtfordirectio`, `--embnic1`, `--wakeonlan` and
  `--numlock`. These nine are pinned by reflection in `CctkReader.cs`, its single `RunCctk` call
  passes only the mapped option (never `=`, `-i`, `--infile`, `-o` or `--outfile`), the program path
  comes only from the two Program Files known folders, and the reader's code is pinned by a SHA-256
  digest so any change fails the tests until it has been reviewed again. The line scan above still
  allows only `--version` and `--secureboot` after the word cctk everywhere else.
- The three write-path files are the scan's only exceptions (`WriteSiteAllowances`):
  `SetFirmwareEnvironmentVariable` may stand in `Kernel32.cs` and `FirmwareVariableWriter.cs`, and
  `DisableKeyProtectors` in `BitLockerSuspender.cs`, each on an exact number of lines. Every other
  forbidden name stays forbidden in those files too.
- `CompiledStringGuardTests` applies the same checks to every string compiled into the Core, CLI
  and Presentation assemblies, so a name split in the source (`"Suspend-Bit" + "Locker"`) is caught.
- `WriteCallSiteGuardTests` and `WhitelistGuardTests` pin the write path itself: the import is
  declared once and called once from the writer, `CommitOnce` has exactly one caller, there is no loop
  around the call, `WritePlan` has no public constructor, the test project never constructs the real
  writer or suspender, and the whitelist holds exactly BootOrder and BootNext with attributes `0x7`
  and no request type that carries bytes.
- `ProcessLaunchGuardTests`: only `src/UefiAccess.Core/Platform/ProcessRunner.cs` may create a
  process, and only `DellInterfaceProbe.cs`, `SecureBootPolicyInspector.cs` and
  `Settings/Dell/CctkReader.cs` may call it.
- `DellSettingsGuardTests`: the settings folder names no Dell service or string class and no WMI
  method call, uses no `System.Management` type directly (WMI only through `WmiHelper`), and runs only
  its pinned queries; nothing under `src/` or `tools/` controls a service or installs software; no
  public settings member begins with `Set`, `Write`, `Apply`, `Change`, `Enable`, `Disable`, `Reset`
  or `Save`; and no test on Windows runs the real Dell reader, so the suite is safe to run on the
  target machine itself.
- `NativeLocationGuardTests`: native declarations live only in `src/UefiAccess.Core/Native` and
  are reached through interfaces; no file binds native code at run time; exactly one forbidden-named
  import exists, `SetFirmwareEnvironmentVariableExW` from kernel32.
- `WmiCallsAreBoundedTests`: every WMI round trip under `src/` is bounded.

What these scans cannot see (a name assembled at run time, reflection, `dynamic`, a PowerShell
command held in a variable, or a registry path set on another line) is why review of every change to
the write path remains the real control.

## License

UefiAccess is licensed under the Mozilla Public License, version 2.0. See [LICENSE](LICENSE).

Copyright © 2026 Roger Belanger and Karl Belanger.

## Contributing

Contributions are welcome, and so are bug reports, accessibility feedback, and testing on the
supported hardware, through the project's GitHub issues. By contributing code you certify the
Developer Certificate of Origin (see [DCO.txt](DCO.txt)) by signing off your commits with
`git commit -s`. Please read [CONTRIBUTING.md](CONTRIBUTING.md) first, in particular the safety
discipline around the write path and the guard tests described above.
