# uefiaccess command-line reference

`uefiaccess.exe` (project `src/UefiAccess.Cli`, assembly name `uefiaccess`) is the command-line
harness of UEFI-Access. This page lists every command and option: the read commands, the
write harness (`write`, `snapshots`) and the Dell settings command (`dell`), the precondition
checks and their sentences, the exit codes, the
output conventions, one output example per command (text and `--json`; for `write` one per
outcome), and the layout of `gate.json`.

From the spec: "Assembly name `uefiaccess`. Commands `info`, `list`, `read`, `boot`, `secureboot`,
`baseline`, `gate` with the options and exit codes from design section 9. `--json` prints a single
JSON document to stdout and nothing else. Tables are plain text with aligned columns and no
box-drawing characters (screen-reader friendly). Every command first checks firmware type,
elevation, and privilege and prints one clear sentence when a precondition fails."

Every command is read-only except `write`. `write` changes two firmware variables and nothing
else, BootOrder (a new order of its own entries) and BootNext (a one-time boot, or its removal),
only through the write pipeline of the core library and only with `--confirm`; with the choice
`--bitlocker suspend` it also suspends BitLocker for one restart, as a stage of that confirmed
write and never on its own. No command changes a BIOS setting, any other variable, or BitLocker
state otherwise, and the build's guard tests (`NoWriteGuardTests`, `WriteCallSiteGuardTests`,
`CompiledStringGuardTests`, `ProcessLaunchGuardTests` and
`DellSettingsGuardTests`) fail if the source or the compiled product names a write path anywhere
but in the allow-listed files. `dell` shows Dell BIOS settings and
changes none of them.

Running `uefiaccess.exe` needs the machine-wide .NET 8 runtime (x64): the .NET 8 SDK x64 installed
with its installer, or the ".NET Desktop Runtime 8.0 (x64)" on a machine that only runs the tools.
With only a per-user SDK install it prints "You must install or update .NET to run this
application."; the fix is one of those two installers (`docs/BUILD.md`, section 1).

Two other programs of the solution take arguments and are not covered here: the application,
`UefiAccess.App.exe`, whose one switch `--trace-input` makes it write an input trace, and the UI
Automation recorder that goes with that trace, `uefiaccess-uia-trace.exe` (`--out <path>`,
`--events <list>`, `--help`). Both are for finding A2-7 and are described in `docs/BUILD.md`,
section 4a.

The examples below were rendered by the tool's own table and JSON writers from a synthetic
variable store that imitates a 2014 Dell OptiPlex 7020 (BIOS A18, booted in UEFI mode, Secure Boot
on, Windows 11 Pro build 22631, elevated prompt, 63 runtime variables). GUIDs, sizes, hashes and
counts differ from machine to machine; the labels, the layout and the JSON property names are
exactly what the tool prints. (The reference OptiPlex 7020, an i7-4790 with BIOS A18 on Windows 11
Pro build 26200, lists 76 variables.)

## 1. Conventions

### Output streams

- Standard output carries only the command's result: the plain-text tables, or with `--json`
  exactly one JSON document. Nothing else is ever written there, so
  `uefiaccess list --json > list.json` produces a clean file (a UTF-8 one in `cmd.exe`; see
  "Text encoding" below for Windows PowerShell).
- Standard error carries everything else: the precondition sentence, failure messages, one
  `Warning: <text>` line per observation (missing boot entries, unreadable variables, a WMI source
  that failed, ...), `Export error: <text>` lines from `baseline`, and, only when `--verbose` (`-v`)
  is given, a mirror of the log (`[DBG] ...`, `[INF] ...` lines). Without `--verbose` no log line
  reaches the console. The log file is written either way: the shared daily log under
  `%ProgramData%\UefiAccess\logs` for an elevated run, the per-user log under
  `%LOCALAPPDATA%\UefiAccess\logs` for a run without elevation (see `docs/BUILD.md` section 5).
  Only when neither can be written, or an elevated run finds a link in the per-user folder, does
  standard error get one `Note: ... so this run keeps no log file` line (with the reason).
- `--json` does not silence standard error: the warnings still go there, and a failure produces an
  error document on standard output plus the message on standard error. That includes a command
  line that does not parse: when `--json` is among the arguments, the error document (exit code 2)
  is written too.

### Text encoding

- On a console window, standard output and standard error are written as Unicode text
  (`WriteConsoleW`), so no character is turned into `?`, whatever the console's code page (a
  screen reader reads the text as written; the console font decides which glyphs are drawn). The
  tool does not change the console's code page.
- When a stream is redirected to a file or a pipe, it is UTF-8 without a byte order mark: in
  `cmd.exe`, `uefiaccess list --json > list.json` writes a UTF-8 JSON file, and so does
  PowerShell's `Start-Process -RedirectStandardOutput` (which `tools\gate\Run-Gate.ps1` uses).
  `gate.json` (`gate --out`) is UTF-8 without a byte order mark as well.
- Windows PowerShell 5.1 re-reads the output of a program it captures (`$x = uefiaccess ...`,
  `uefiaccess ... > file`, `| Out-File`) in the console's code page, so characters outside ASCII
  arrive garbled there. Run `[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)`
  first in that session, or redirect in `cmd.exe`. Plain ASCII output is the same either way.

### Tables

- A header row, then one row per item. Columns are left-aligned and padded with spaces so that the
  widest cell in a column is followed by exactly one space; trailing spaces are trimmed. No
  box-drawing characters, no color codes, no truncation of wide values.
- Key-value output is a two-column table too: `Field Value` (`info`, `read`, `dell`, `baseline`,
  the `boot` summary), `Variable Value` and `Source Value` (`secureboot`), `Finding Value` (`gate`).
- Booleans render as `true` and `false`. A value that contains a line break or a tab is flattened
  to one line so every row stays on one line for a screen reader.

### `--json`

- Exactly one JSON document, indented with two spaces, on standard output.
- Property names are the record member names in camelCase; enums are strings (`"Uefi"`,
  `"Enabled"`, `"NtEnumerate"`, `"Success"`); values the tool could not determine are `null`; lists
  are `[]` when empty; no property is ever omitted. `+` and quotation marks are written literally.
- The documents of `info`, `list`, `read`, `boot`, `secureboot`, `dell`, `baseline`, `write`,
  `write log` and `snapshots` start with `command` (the command name; `write` for every write
  subcommand but `log`) and `ok`. `gate --json` prints the same document that `gate` writes to
  `gate.json` (section 4), which has no `command` property.
- The write documents embed the write record exactly as `write-log.jsonl` holds it: its
  `deviceNodeTypes` object is keyed by Boot#### name, and those keys keep their spelling
  (`Boot0003`), while every property name is camelCase.
- The baseline files `index.json`, `decoded.json`, `platform.json` and `dell-settings.json` are
  written by the Core exporter, not by the CLI, and use PascalCase member names (`ToolVersion`,
  `Variables`) and the default JSON escaping (`+` becomes `\u002B`, `"` becomes `\u0022`); see the `baseline` command.

### Variable key

Every variable has the stable display key `<vendor GUID in lowercase>:<Name>`, used identically in
the tables, in the logs, in `index.json`, and later in the GUI. Example:
`8be4df61-93ca-11d2-aa0d-00e098032b8c:BootOrder`. Tables and exports are sorted by key with an
ordinal comparison, so `Boot0000` sorts before `BootCurrent` and `BootOrder`, uppercase names
before lowercase ones, and the global-variable GUID (`8be4...`) before the image security database
GUID (`d719...`).

### Attribute flags

| Flag | Attribute | Value | Meaning |
|---|---|---|---|
| `NV` | NonVolatile | 0x01 | survives a reset |
| `BS` | BootServiceAccess | 0x02 | readable before ExitBootServices |
| `RT` | RuntimeAccess | 0x04 | readable from the operating system; only these variables are visible to M1 |
| `HR` | HardwareErrorRecord | 0x08 | hardware error record |
| `AW` | AuthenticatedWriteAccess | 0x10 | deprecated authenticated write |
| `TA` | TimeBasedAuthenticatedWriteAccess | 0x20 | time-based authenticated write (PK, KEK, db, dbx) |
| `AP` | AppendWrite | 0x40 | append write |
| `EA` | EnhancedAuthenticatedAccess | 0x80 | enhanced authenticated access |

Flags are printed in that fixed order, space separated, only the set ones (`NV BS RT`, `BS RT`,
`NV BS RT TA`). Bits outside the eight defined flags are appended as one hexadecimal token
(`NV BS RT 0x00000100`). Where the raw value is shown as well it is hexadecimal with eight digits
followed by the flags (`0x00000007 NV BS RT`). In JSON the raw value is a number (`"attributes": 7`),
the flags a string (`"flags": "NV BS RT"`) and the full names a list
(`"attributeNames": ["NonVolatile", "BootServiceAccess", "RuntimeAccess"]`).

### GUID and name arguments

- `--guid` (`-g`) accepts a GUID with or without braces, in any letter case:
  `8BE4DF61-93CA-11D2-AA0D-00E098032B8C`, `8be4df61-93ca-11d2-aa0d-00e098032b8c`, or
  `{8BE4DF61-93CA-11D2-AA0D-00E098032B8C}`. A value that is not a GUID is a usage error (exit 2).
- Default GUID for `read`: the EFI global variable GUID `8BE4DF61-93CA-11D2-AA0D-00E098032B8C`;
  the names `db`, `dbx`, `dbt` and `dbr` default to the image security database GUID
  `D719B2CB-3D3A-4596-A3BC-DAD00E67656F`.
- Variable names are case-sensitive, as UEFI defines them: `bootorder` is not `BootOrder`.
- Boot entry names are `Boot` followed by exactly four uppercase hexadecimal digits (`Boot0001`).
- GUIDs in the output are always lowercase without braces (`8be4df61-93ca-11d2-aa0d-00e098032b8c`);
  only the log shows the braced uppercase form that `GetFirmwareEnvironmentVariableExW` receives.

## 2. Preconditions, exit codes and failure messages

### Exit codes

| Code | Meaning | Returned when |
|---|---|---|
| 0 | success | the command completed without warnings |
| 2 | usage error | unknown command or option, missing argument, a `--guid` value that is not a GUID, or an `--ifr-varstores` file that does not exist or cannot be read; for `write`, also `--dry-run` together with `--confirm`, a `--bitlocker` value other than `suspend` or `continue`, a name that is not a Boot#### name, `bootnext` with both an entry and `--clear` or with neither, a `--snapshot-root` that is not a folder path, `--snapshot-root` together with `--confirm`, and a `--last` below 1; for `dell`, a `--source` other than `auto`, `dcm` and `cctk`, and `--all` together with `--source cctk` |
| 3 | not UEFI | the firmware type is legacy BIOS, or it could not be determined (`GetFirmwareType` failed, or the tool is not running on Windows); also when `read` gets `ERROR_INVALID_FUNCTION (1)` |
| 4 | not elevated, or privilege missing | the process is not elevated, or it is elevated but `SeSystemEnvironmentPrivilege` could not be enabled; also when `read` gets `ERROR_PRIVILEGE_NOT_HELD (1314)` |
| 5 | a native call failed | nothing could be enumerated; `read` failed (not found, access denied, too large, or any other error); an `--out` target could not be created or written (a character Windows does not allow in a path, a missing drive or folder, no permission, or, for `baseline` and `gate`, a folder inside the data root `%ProgramData%\UefiAccess` that a standard user could redirect): `read` could not write its file, `baseline` wrote no file at all, or `gate` could not write `gate.json`; `write log` found a `write-log.jsonl` it could not read; `dell` found no Dell interface that answered (the problem sentence, see `dell`); or a command threw |
| 6 | partial | the command completed with warnings: the enumeration stopped early at a malformed entry, the known-names fallback was used (it always warns) or one of its reads failed (an unreadable variable), an identity WMI query failed (`info`), a Secure Boot policy source failed (`secureboot`: registry, PowerShell not runnable, 10-second timeout, or unparsed output), or the export reported errors but wrote some files (`baseline`). The `Warning:` lines on standard error name what was incomplete: a variable, a WMI, registry or PowerShell source, or an export error. `dell` exits 6 when a Dell interface answered for only some of the agreed settings (`"outcome": "Partial"`; Dell Command Configure, the fallback, never reads Boot list option); its rows say which (`not reported`, `not read`, `no value reported` or `a value Dell does not name`) |
| 7 | blocked by a safeguard | `write` only: a check of the write pipeline refused the change before any write call, and nothing was written; `blockedBy` names the check. A dry run that passes exits 0, one that is refused exits 7. `write check` also exits 7 for a variable this tool cannot change (on a machine that is not the enabled unit: `blockedBy` `expectedModel` on another model or BIOS version, `expectedUnit` on another OptiPlex 7020 on BIOS A18) |
| 8 | the firmware refused the write | `write --confirm` only: the one write call was made, the firmware refused it, and the value read back is the old one; nothing changed, and a snapshot folder exists |
| 9 | the value read back is not the expected one | `write --confirm` only: the value read back after the write call is not what the call's result implies (written but not verified, changed despite an error, or neither the old nor the new value); a snapshot folder exists and a restore (`write restore`) is advised |

Exit codes 7, 8 and 9 are Core's `WriteExitCodes`, the numbers the write record carries too.
`uefiaccess --help` prints the codes 0 and 2 to 9 with their short meanings. `boot`, `gate`
and `secureboot` do not turn their own observations (a missing boot entry, a failed Dell probe, a
varstore whose status is unknown, a disagreement between the decoded `SecureBoot` variable and the
policy inspector) into exit 6; they print `Warning:` lines and return the enumeration's code
(`secureboot`: 6 also when a policy source failed). `baseline` does not turn its Dell settings
into an exit code either: a Dell read that did not answer, the fallback sentence and the BIOS
warning are `Warning: dell settings: <sentence>` lines. `dell` takes its exit code from its
outcome alone: 0 Complete, 6 Partial, 5 Unavailable.

### Precondition checks

Every command, `info` included, runs the same three checks before doing anything else, in this
order: firmware type, elevation, privilege. When one fails, the command writes one sentence to
standard error, writes nothing to standard output (or, with `--json`, the error document shown
below), and exits with the code in the table.

| Condition | Exit | Sentence written to standard error |
|---|---|---|
| Firmware type is not UEFI: legacy BIOS boot, or unknown because `GetFirmwareType` failed or the OS is not Windows | 3 | `This system did not boot in UEFI mode (or the OS reports no UEFI runtime services); firmware variables are not reachable from Windows.` |
| Process not elevated | 4 | `This command needs administrator rights; open an elevated command prompt (Run as administrator) and run uefiaccess again.` |
| Elevated, but the privilege could not be enabled | 4 | `The process is elevated but SeSystemEnvironmentPrivilege could not be enabled (<detail>); run uefiaccess as administrator from an account that holds that privilege.` where `<detail>` names the failing step, for example `AdjustTokenPrivileges reported ERROR_NOT_ALL_ASSIGNED (1300): the privilege is absent from the process token` |

Example on a refurbished unit that is still in legacy mode (the sentence appears on the console
because standard error is shown there; `2>nul` would hide it):

```
C:\src\UefiAccess> src\UefiAccess.Cli\bin\Release\net8.0-windows\uefiaccess.exe list
This system did not boot in UEFI mode (or the OS reports no UEFI runtime services); firmware variables are not reachable from Windows.
C:\src\UefiAccess> echo %errorlevel%
3
```

With `--json` the same run writes the sentence to standard error and this document to standard
output. `firmwareType` is `Unknown`, `LegacyBios` or `Uefi`; `elevated` is what the elevation check
found; `privilegeState` is `null` when the firmware type check failed (exit 3; elevation and
privilege were not checked further), `NotElevated` when the process is not elevated (exit 4; the
privilege is then not attempted), `ElevatedPrivilegeAbsent` when enabling the privilege failed
(exit 4), and `Enabled` when the failure came after all three checks passed (for example exit 5,
nothing could be enumerated). When the command line does not parse (exit 2) or a command threw
(exit 5), no check ran and `firmwareType`, `elevated` and `privilegeState` are all `null`; `command`
is then the command named on the command line, spelled as that command's own documents spell it
(`write` for `write` and its `bootorder`, `bootnext`, `restore` and `check`, `write log` for
`write log`, the same as when a precondition fails), or `uefiaccess` when none was recognized, and
`error` holds the System.CommandLine messages joined with a space:

<!-- schema: ErrorDocument -->
```json
{
  "command": "list",
  "ok": false,
  "exitCode": 3,
  "error": "This system did not boot in UEFI mode (or the OS reports no UEFI runtime services); firmware variables are not reachable from Windows.",
  "firmwareType": "LegacyBios",
  "elevated": true,
  "privilegeState": null
}
```

### Failures after the preconditions

| Situation | Exit | Written to standard error |
|---|---|---|
| Enumeration produced no variables (`list`, `boot`, `secureboot`, `baseline`; `gate` still writes `gate.json`) | 5 | `No variables could be enumerated: <error>`, where `<error>` is the enumeration error. When the native call failed and the known-names fallback then stopped at a fatal read status, it names the variable it stopped at, for example `No variables could be enumerated: known-names fallback aborted while reading BootOrder: ERROR_INVALID_FUNCTION (1): invalid function; the machine booted in legacy BIOS mode or the firmware exposes no runtime variable services`. The suffix `; the firmware type is LegacyBios, so the known-names fallback does not apply` appears only when the firmware type is not UEFI, which the precondition check normally prevents (with `--json`: the error document above) |
| `read` failed | 5 (3 or 4 for the two statuses named in the exit code table) | `Reading <name> (<guid>) failed: <Status>: <error>`, for example `Reading Foo (8be4df61-93ca-11d2-aa0d-00e098032b8c) failed: NotFound: ERROR_ENVVAR_NOT_FOUND (203): the firmware variable was not found` (with `--json`: a `read` document with `"ok": false`, see the `read` command) |
| `read --out` or `gate --out` could not write its file | 5 | `Writing '<path>' failed: <exception>: <message>`, or `Writing '<path>' failed: <refusal>` when the folder check refused a `gate --out` inside the data root (see `gate`); the table is still printed, and its last line on standard output says so: `The data could not be written to <full path>.` (`read`) or `Gate report could not be written to <full path>` (`gate`) |
| `gate --ifr-varstores` names a file that cannot be read (locked, no permission) | 2 | `The file '<file>' given to --ifr-varstores could not be read: <exception>: <message>`; nothing is probed and no `gate.json` is written |
| `baseline` reported export errors | 6, or 5 when nothing was written | one `Export error: <error>` line per problem |
| `dell` found no Dell interface that answered (no Dell Command Monitor and no Dell Command Configure, a computer that is not a Dell, a time limit, an error) | 5 | the problem sentence alone, built from the causes and one piece of advice, for example `Dell BIOS settings cannot be read. Dell Command Monitor is not installed. Dell Command Configure was not found. Install the Microsoft Visual C++ 2015-2022 x64 runtime, then Dell Command Monitor, and try again; the Dell interface setup page of the documentation has the steps.` (with `--json`: the `dell` document with `"ok": false` and the same sentence in `sentence`, see `dell`); on a platform composed without the Dell settings reader (a test build), `This platform was composed without the Dell settings reader.` |
| A command threw unexpectedly | 5 | `uefiaccess <command> failed: <exception>: <message>` |
| Bad command line | 2 | the System.CommandLine message(s), then `Run 'uefiaccess --help' for usage.`; with `--json` among the arguments, also the error document on standard output (`firmwareType`, `elevated` and `privilegeState` `null`) |

### Warnings (exit 6)

Warnings are one line each, prefixed `Warning: `, on standard error; the normal output still goes
to standard output. The ones that make a command exit 6:

- `Warning: enumeration incomplete: <error>` when the enumeration returned variables but also an
  error, and `Warning: enumeration stopped early at a malformed entry; the variables before it are reported: <detail>`.
- `Warning: <failure>; falling back to reading the curated KnownVariableNames list one by one` and
  `Warning: the fallback only sees the curated names and Boot#### entries; vendor-specific variables are not listed by this path`
  whenever the fallback ran, plus `Warning: <name>: <error>` for every curated name that could not
  be read (for example a variable above the 4 MiB ceiling:
  `variable dbx {D719B2CB-3D3A-4596-A3BC-DAD00E67656F} exceeds the 4194304-byte buffer ceiling (last size tried 4194304): ERROR_INSUFFICIENT_BUFFER (122): the buffer is too small for the variable data`).
- `Warning: machine identity: <error>` (`info`) and `Warning: policy: <error>` (`secureboot`).

`boot`, `secureboot` and `gate` also print warnings that do not change the exit code:
`Warning: BootOrder position 4 references Boot0004, which does not exist.`,
`Warning: BootOrder position 4 references Boot0004, which could not be read: <Status>: <error>.`
and `Warning: BootOrder could not be read: <Status>: <error>.` (these two only after an incomplete
enumeration, see `boot`),
`Warning: The SecureBoot variable decoded as true but the policy inspector read false.`
(`secureboot`), `Warning: dell probe: <error>`, `Warning: ifr-varstores: line 7: 'x' is not a GUID`
and `Warning: ifr-varstores: 2 varstores could not be checked because <reason> and the direct read failed; their status is unknown`
(`gate`). `dell` prints the fallback sentence
(`Warning: Dell Command Monitor is not installed. Dell Command Configure was used instead.`) and
the BIOS warning
(`Warning: These names and values were checked against BIOS A18 only; this computer runs BIOS A17.`);
`baseline` prints each of those, and the problem sentence of a Dell read that did not answer,
after `Warning: dell settings: `.

### Win32 and NTSTATUS texts

Error texts are the ones `NativeError` produces, always with the numeric code:
`ERROR_INVALID_FUNCTION (1): invalid function; the machine booted in legacy BIOS mode or the firmware exposes no runtime variable services`,
`ERROR_ACCESS_DENIED (5): access denied`, `ERROR_INSUFFICIENT_BUFFER (122): the buffer is too small for the variable data`,
`ERROR_ENVVAR_NOT_FOUND (203): the firmware variable was not found`,
`ERROR_NOT_ALL_ASSIGNED (1300): the privilege is absent from the process token`,
`ERROR_PRIVILEGE_NOT_HELD (1314): SeSystemEnvironmentPrivilege is not held; run elevated`;
`STATUS_NOT_IMPLEMENTED (0xC0000002): not implemented; the machine booted in legacy BIOS mode`,
`STATUS_INVALID_PARAMETER (0xC000000D): invalid parameter`,
`STATUS_BUFFER_TOO_SMALL (0xC0000023): the buffer is too small; the required length was returned`,
`STATUS_PRIVILEGE_NOT_HELD (0xC0000061): SeSystemEnvironmentPrivilege is not held; run elevated`.
Anything else prints the numeric code: `Win32 error 1450 (0x000005AA): <system message>`,
`NTSTATUS 0xC0000001`.

### Enumeration method

`list`, `boot`, `secureboot`, `baseline`, and `gate` first enumerate the store with
`NtEnumerateSystemEnvironmentValuesEx` (information class 2). If that fails and the firmware type
is UEFI, they fall back to reading the curated list of known names one by one (BootOrder, BootNext,
BootCurrent, Timeout, SecureBoot, SetupMode, AuditMode, DeployedMode, PK, KEK, OsIndications,
OsIndicationsSupported, PlatformLang, ConIn, ConOut, ErrOut, db, dbx, dbt, and Boot#### probed
from 0000 upward until three consecutive misses). The `list` footer and the `enumeration.method`
JSON property name the method used (`NtEnumerate` or `KnownNamesFallback`); a fallback run always
exits 6 because it warns that vendor-specific variables are not listed by that path. Both paths are
logged.

Only variables with the `RT` (RuntimeAccess) attribute are visible to the operating system;
boot-service-only variables never appear in any output. The gate memo must say this when a
vendor varstore is missing.

A name that an incomplete enumeration (the known-names fallback, a stop at a malformed entry, or
no enumeration at all) did not list may still exist. `boot` and `gate` therefore read such a name
directly (the same read-only call as `read`) before they call it missing: `boot` for BootOrder,
BootCurrent, BootNext and Timeout and for every Boot#### that BootOrder, BootCurrent or BootNext
references, `gate` for every varstore in the `--ifr-varstores` file. `ERROR_ENVVAR_NOT_FOUND (203)`
then means missing; any other failure is reported as "could not be read" with the status (`boot`)
or status `unknown` (`gate`), never as missing or not present.

## 3. Commands

Every command accepts `--json`, the global `--verbose` (`-v`) and `--help` (`-h`, `-?`); for
`write`, every subcommand does.

### `info`

```
uefiaccess info [--json]
```

Prints the tool version, the firmware type, the elevation and privilege state, the machine
identity from WMI, the OS, and the expected-model check for the 2014 OptiPlex 7020 (model contains
`7020`, BIOS version matches `^A\d\d$`, 4th-generation CPU).

```
C:\src\UefiAccess> uefiaccess info
Field                    Value
Tool version             0.1.0+0123456789abcdef0123456789abcdef01234567
Firmware type            Uefi
Elevated                 true
Privilege state          Enabled
Manufacturer             Dell Inc.
Model                    OptiPlex 7020
System SKU               0620
BIOS version             A18
BIOS release date        2019-05-02
Service tag              5XK2P52
Base board               0F5C5X
CPU                      Intel(R) Core(TM) i5-4590 CPU @ 3.30GHz
OS                       Microsoft Windows 11 Pro
OS version               10.0.22631
OS build                 22631
Expected model confirmed true
Expected model notes     Model "OptiPlex 7020": contains 7020 (match). BIOS version "A18": Dell A-series version (match). CPU "Intel(R) Core(TM) i5-4590 CPU @ 3.30GHz": 4th-generation Intel (Haswell) part (match). Expected model confirmed: 2014 Dell OptiPlex 7020 (model, BIOS version and CPU generation all match).
```

When the unit is not the planned one the last two rows read, for example:

```
Expected model confirmed false
Expected model notes     Model "OptiPlex 7020": contains 7020 (match). BIOS version "1.12.0": x.y.z scheme, not a Dell A-series version (no match). CPU "13th Gen Intel(R) Core(TM) i5-13500": 13th-generation Intel Core, not 4th generation (no match). Expected model not confirmed: model matched, BIOS version did not match, CPU did not match. Warning: this looks like the 2024 relaunch, not the planned unit (the 2014 OptiPlex 7020): the BIOS version uses the x.y.z scheme of the 2024 OptiPlex 7020 and the CPU is 13th generation (12th generation or newer). Record this in the gate memo.
```

A WMI query that fails does not stop the command: the affected values print as `(unknown)`, one
`Warning: machine identity: <error>` line per failure goes to standard error, and the exit code is
6. `Expected model notes` then says which signal was `not available`.

`--json` (the `identity` object is the Core `MachineIdentityResult`; `os` joins the caption,
version and build on one line):

<!-- schema: InfoDocument -->
```json
{
  "command": "info",
  "ok": true,
  "toolVersion": "0.1.0+0123456789abcdef0123456789abcdef01234567",
  "firmwareType": "Uefi",
  "elevated": true,
  "privilegeState": "Enabled",
  "identity": {
    "manufacturer": "Dell Inc.",
    "model": "OptiPlex 7020",
    "systemSku": "0620",
    "biosVersion": "A18",
    "biosReleaseDate": "2019-05-02",
    "serviceTag": "5XK2P52",
    "baseBoard": "0F5C5X",
    "cpu": "Intel(R) Core(TM) i5-4590 CPU @ 3.30GHz",
    "osCaption": "Microsoft Windows 11 Pro",
    "osVersion": "10.0.22631",
    "osBuild": "22631",
    "expectedModelConfirmed": true,
    "expectedModelNotes": "Model \"OptiPlex 7020\": contains 7020 (match). BIOS version \"A18\": Dell A-series version (match). CPU \"Intel(R) Core(TM) i5-4590 CPU @ 3.30GHz\": 4th-generation Intel (Haswell) part (match). Expected model confirmed: 2014 Dell OptiPlex 7020 (model, BIOS version and CPU generation all match).",
    "errors": []
  },
  "os": "Microsoft Windows 11 Pro 10.0.22631 build 22631"
}
```

### `list`

```
uefiaccess list [--json] [--filter <text>] [--guid <guid>]
```

| Option | Effect |
|---|---|
| `--json` | one JSON document instead of the table |
| `--filter <text>`, `-f <text>` | keep only rows whose key (`guid:name`) contains `<text>`, case-insensitive |
| `--guid <guid>`, `-g <guid>` | keep only variables with that vendor GUID (any of the accepted spellings) |

Table columns: `Key`, `Flags` (the attribute flags), `Size` (bytes). Rows are sorted by key. The
footer gives the rows shown, the total, the enumeration method and the number of entries parsed
from the native buffer. The example keeps only the 18 rows under the two well-known GUIDs; a real
run also lists the vendor-specific variables, sorted into the same table by key.

```
C:\src\UefiAccess> uefiaccess list
Key                                                         Flags       Size
8be4df61-93ca-11d2-aa0d-00e098032b8c:Boot0000               NV BS RT    179
8be4df61-93ca-11d2-aa0d-00e098032b8c:Boot0001               NV BS RT    154
8be4df61-93ca-11d2-aa0d-00e098032b8c:Boot0002               NV BS RT    186
8be4df61-93ca-11d2-aa0d-00e098032b8c:Boot0003               NV BS RT    76
8be4df61-93ca-11d2-aa0d-00e098032b8c:BootCurrent            BS RT       2
8be4df61-93ca-11d2-aa0d-00e098032b8c:BootOrder              NV BS RT    8
8be4df61-93ca-11d2-aa0d-00e098032b8c:ConIn                  NV BS RT    64
8be4df61-93ca-11d2-aa0d-00e098032b8c:ConOut                 NV BS RT    62
8be4df61-93ca-11d2-aa0d-00e098032b8c:ErrOut                 NV BS RT    62
8be4df61-93ca-11d2-aa0d-00e098032b8c:KEK                    NV BS RT TA 1560
8be4df61-93ca-11d2-aa0d-00e098032b8c:OsIndicationsSupported BS RT       8
8be4df61-93ca-11d2-aa0d-00e098032b8c:PK                     NV BS RT TA 858
8be4df61-93ca-11d2-aa0d-00e098032b8c:PlatformLang           NV BS RT    6
8be4df61-93ca-11d2-aa0d-00e098032b8c:SecureBoot             BS RT       1
8be4df61-93ca-11d2-aa0d-00e098032b8c:SetupMode              BS RT       1
8be4df61-93ca-11d2-aa0d-00e098032b8c:Timeout                NV BS RT    2
d719b2cb-3d3a-4596-a3bc-dad00e67656f:db                     NV BS RT TA 3143
d719b2cb-3d3a-4596-a3bc-dad00e67656f:dbx                    NV BS RT TA 21148

63 of 63 variables listed (method NtEnumerate, 63 entries parsed).
```

`--filter boot` keeps every key that contains `boot` in any case: the four `Boot000x` rows,
`BootCurrent`, `BootOrder` and `SecureBoot`, with the footer
`7 of 63 variables listed (method NtEnumerate, 63 entries parsed).`. `--guid
D719B2CB-3D3A-4596-A3BC-DAD00E67656F` keeps `db` and `dbx`.

Exit code 6 when the enumeration was partial (see section 2); the table then holds the variables
that were obtained and the reasons are on standard error.

`--json` (here with `--guid D719B2CB-3D3A-4596-A3BC-DAD00E67656F`; `filter` and `guid` echo the
options as given, `null` when absent, `listed` counts the rows after filtering, and
`enumeration.count` is the total):

<!-- schema: ListDocument -->
```json
{
  "command": "list",
  "ok": true,
  "enumeration": {
    "method": "NtEnumerate",
    "entriesParsed": 63,
    "count": 63,
    "error": null,
    "warnings": []
  },
  "filter": null,
  "guid": "d719b2cb-3d3a-4596-a3bc-dad00e67656f",
  "listed": 2,
  "variables": [
    {
      "key": "d719b2cb-3d3a-4596-a3bc-dad00e67656f:db",
      "name": "db",
      "guid": "d719b2cb-3d3a-4596-a3bc-dad00e67656f",
      "attributes": 39,
      "flags": "NV BS RT TA",
      "attributeNames": [
        "NonVolatile",
        "BootServiceAccess",
        "RuntimeAccess",
        "TimeBasedAuthenticatedWriteAccess"
      ],
      "size": 3143
    },
    {
      "key": "d719b2cb-3d3a-4596-a3bc-dad00e67656f:dbx",
      "name": "dbx",
      "guid": "d719b2cb-3d3a-4596-a3bc-dad00e67656f",
      "attributes": 39,
      "flags": "NV BS RT TA",
      "attributeNames": [
        "NonVolatile",
        "BootServiceAccess",
        "RuntimeAccess",
        "TimeBasedAuthenticatedWriteAccess"
      ],
      "size": 21148
    }
  ]
}
```

The `enumeration` object is the same in the `boot`, `secureboot` and `baseline` documents. In a
partial run `error` holds the enumeration error and `warnings` the same texts that were printed
as `Warning:` lines; an unreadable variable in the fallback path appears there as
`"<name>: <error>"`. There is no per-variable data in the `list` document; use `read`.

### `read`

```
uefiaccess read <name> [--json] [--guid <guid>] [--hex] [--out <file>]
```

| Argument or option | Effect |
|---|---|
| `<name>` | the variable name (case-sensitive) |
| `--json` | one JSON document instead of the table; the data is included as base64 |
| `--guid <guid>`, `-g <guid>` | vendor GUID; default EFI global variable GUID, or the image security database GUID for `db`, `dbx`, `dbt`, `dbr` |
| `--hex`, `-x` | print the data as a hex dump after the table: eight-digit offset, 16 bytes per line in two groups of eight, ASCII column with `.` for non-printable bytes |
| `--out <file>`, `-o <file>` | write the raw data to `<file>` (created or overwritten) and report the byte count |

The table always shows the name, the GUID, the key, the read status, the attributes (hexadecimal
and flags), the full attribute names and the size. Without `--hex` or `--out`, and when the variable
has data, one more line says `Use --hex to show the data or --out <file> to save it.`. With `--out`
the next line is `Wrote <n> bytes to <full path>.`, or, when the file could not be written,
`The data could not be written to <full path>.` (standard error then carries
`Writing '<file>' failed: <exception>: <message>` and the exit code is 5). The read
starts with a 64 KiB buffer and doubles on `ERROR_INSUFFICIENT_BUFFER (122)` up to 4 MiB; a larger
variable fails with status `TooLarge` (exit 5). A missing variable fails with status `NotFound`
(exit 5). Zero-length variables are legal: the table shows `0 bytes`, `--hex` prints `(no data)`.

```
C:\src\UefiAccess> uefiaccess read BootOrder --hex
Field           Value
Name            BootOrder
GUID            8be4df61-93ca-11d2-aa0d-00e098032b8c
Key             8be4df61-93ca-11d2-aa0d-00e098032b8c:BootOrder
Status          Success
Attributes      0x00000007 NV BS RT
Attribute names NonVolatile, BootServiceAccess, RuntimeAccess
Size            8 bytes

00000000  00 00 01 00 02 00 04 00                           ........
```

```
C:\src\UefiAccess> uefiaccess read dbx --out C:\work\dbx.bin
Field           Value
Name            dbx
GUID            d719b2cb-3d3a-4596-a3bc-dad00e67656f
Key             d719b2cb-3d3a-4596-a3bc-dad00e67656f:dbx
Status          Success
Attributes      0x00000027 NV BS RT TA
Attribute names NonVolatile, BootServiceAccess, RuntimeAccess, TimeBasedAuthenticatedWriteAccess
Size            21148 bytes
Wrote 21148 bytes to C:\work\dbx.bin.
```

A failed read prints nothing on standard output (in text mode) and one line on standard error,
for example `Reading Foo (8be4df61-93ca-11d2-aa0d-00e098032b8c) failed: NotFound: ERROR_ENVVAR_NOT_FOUND (203): the firmware variable was not found`.
The status names are `Success`, `NotFound`, `PrivilegeNotHeld` (exit 4), `NotUefi` (exit 3),
`AccessDenied`, `TooLarge`, `Failed` and `NotSupportedOnThisOs` (exit 3).

`--json` for `read BootOrder --hex --json` (`hex` is `null` without `--hex`; `outFile` and
`bytesWritten` are set only with `--out`; `lastBufferSize` is the last buffer size the store tried):

<!-- schema: ReadDocument -->
```json
{
  "command": "read",
  "ok": true,
  "name": "BootOrder",
  "guid": "8be4df61-93ca-11d2-aa0d-00e098032b8c",
  "key": "8be4df61-93ca-11d2-aa0d-00e098032b8c:BootOrder",
  "status": "Success",
  "error": null,
  "lastBufferSize": 65536,
  "attributes": 7,
  "flags": "NV BS RT",
  "attributeNames": [
    "NonVolatile",
    "BootServiceAccess",
    "RuntimeAccess"
  ],
  "size": 8,
  "dataBase64": "AAABAAIABAA=",
  "hex": [
    "00000000  00 00 01 00 02 00 04 00                           ........"
  ],
  "outFile": null,
  "bytesWritten": null
}
```

The same document with `"ok": false` describes a failed read (`read Foo --json`, exit 5); the
message is also written to standard error:

<!-- schema: ReadDocument -->
```json
{
  "command": "read",
  "ok": false,
  "name": "Foo",
  "guid": "8be4df61-93ca-11d2-aa0d-00e098032b8c",
  "key": "8be4df61-93ca-11d2-aa0d-00e098032b8c:Foo",
  "status": "NotFound",
  "error": "ERROR_ENVVAR_NOT_FOUND (203): the firmware variable was not found",
  "lastBufferSize": 65536,
  "attributes": null,
  "flags": null,
  "attributeNames": null,
  "size": null,
  "dataBase64": null,
  "hex": null,
  "outFile": null,
  "bytesWritten": null
}
```

### `boot`

```
uefiaccess boot [--json]
```

Prints a summary table (BootCurrent and BootNext resolved to their descriptions, Timeout,
BootOrder, number of Boot#### entries), then the boot order as a `Position Entry Description`
table, then one block per Boot#### variable in index order: the key, the variable attributes and
size, the load option attributes (`Active`, `ForceReconnect`, `Hidden`, category `Boot` or `App`),
whether BootOrder references it, the device path, and the optional data (size, and its text when
it is printable). A BootOrder position that names a missing Boot#### is marked `(missing)`, and a
BootCurrent or BootNext that names one reads, for example, `Boot0005 (missing entry)`, only when
that entry does not exist (one that could not be read is never called missing, below); a
Boot#### that BootOrder does not reference shows `In BootOrder: no`. A malformed load option gets
an `Error:` line and the fields parsed before the problem; it never stops the command.

```
C:\src\UefiAccess> uefiaccess boot
Field        Value
BootCurrent  Boot0000 (Windows Boot Manager)
BootNext     (not set)
Timeout      2 seconds
BootOrder    Boot0000, Boot0001, Boot0002, Boot0004 (missing)
Boot entries 4

Boot order:
Position Entry    Description
1        Boot0000 Windows Boot Manager
2        Boot0001 UEFI: ST500DM002-1BD142, Partition 1
3        Boot0002 UEFI: IP4 Intel(R) Ethernet Connection I217-LM
4        Boot0004 (missing: no Boot0004 variable)

Boot entries:

Boot0000  Windows Boot Manager
  Key:                 8be4df61-93ca-11d2-aa0d-00e098032b8c:Boot0000
  Variable attributes: 0x00000007 NV BS RT, 179 bytes
  Option attributes:   0x00000001 Active, category Boot
  In BootOrder:        yes
  Device path:         HD(1,GPT,6f6e5d1c-8f7a-4d33-9a2b-3c4d5e6f7081,0x800,0x32000)/File(\EFI\Microsoft\Boot\bootmgfw.efi)
  Optional data:       15 bytes, text "WINDOWS"

Boot0001  UEFI: ST500DM002-1BD142, Partition 1
  Key:                 8be4df61-93ca-11d2-aa0d-00e098032b8c:Boot0001
  Variable attributes: 0x00000007 NV BS RT, 154 bytes
  Option attributes:   0x00000001 Active, category Boot
  In BootOrder:        yes
  Device path:         PciRoot(0x0)/Pci(0x1f,0x2)/Sata(0,65535,0)/HD(1,GPT,6f6e5d1c-8f7a-4d33-9a2b-3c4d5e6f7081,0x800,0x32000)
  Optional data:       none

Boot0002  UEFI: IP4 Intel(R) Ethernet Connection I217-LM
  Key:                 8be4df61-93ca-11d2-aa0d-00e098032b8c:Boot0002
  Variable attributes: 0x00000007 NV BS RT, 186 bytes
  Option attributes:   0x00000001 Active, category Boot
  In BootOrder:        yes
  Device path:         PciRoot(0x0)/Pci(0x19,0x0)/MAC(00:11:22:33:44:55,1)/IPv4(0.0.0.0)
  Optional data:       none

Boot0003  UEFI: SanDisk Cruzer
  Key:                 8be4df61-93ca-11d2-aa0d-00e098032b8c:Boot0003
  Variable attributes: 0x00000007 NV BS RT, 76 bytes
  Option attributes:   0x00000001 Active, category Boot
  In BootOrder:        no
  Device path:         PciRoot(0x0)/Pci(0x14,0x0)/USB(3,0)
  Optional data:       none
```

At the same time standard error receives `Warning: BootOrder position 4 references Boot0004, which does not exist.`
Other warnings of this kind: `BootNext references Boot0005, which does not exist.`,
`BootOrder is not present.`, `Timeout has 1 byte; expected 2.`, `BootCurrent has 4 bytes; expected 2.`,
`BootOrder: length 7 bytes is not a multiple of 2; the trailing byte was ignored.`, and
`Boot0002: <load option error>`; after an incomplete enumeration also
`BootCurrent references Boot0004, which could not be read: <Status>: <error>.` (likewise BootNext
and each BootOrder position) and `BootOrder could not be read: <Status>: <error>.` (likewise
BootCurrent, BootNext and Timeout). They do not change the exit code (which follows the
enumeration).

After an incomplete enumeration (section 2, "Enumeration method") BootOrder, BootCurrent, BootNext
and Timeout are read directly when the enumeration did not list them, and so is every Boot####
that BootOrder, BootCurrent or BootNext references and the enumeration did not list. A variable or
entry the direct read finds is used and decoded like the others; one it does not find
(`ERROR_ENVVAR_NOT_FOUND`) is `(not present)`, `(not set)` or missing as usual. One whose read
fails for any other reason is never called missing or not present: the summary shows
`(could not be read: <Status>: <error>)` for BootOrder, BootCurrent, BootNext or Timeout itself,
`Boot0004 (could not be read: <Status>: <error>)` when BootCurrent or BootNext references such an
entry, and `Boot0004 (unreadable)` in the BootOrder row; the boot order table shows
`(unreadable: Boot0004 could not be read)`, or `(BootOrder could not be read)` in place of the
table; in JSON the entry's `note` is `"unreadable"` and a variable's presence is `null` (below).
For example, after the known-names fallback on a machine where Boot0004 and Timeout could not be
read:

```
Field        Value
BootCurrent  Boot0004 (could not be read: AccessDenied: ERROR_ACCESS_DENIED (5): access denied)
BootNext     (not set)
Timeout      (could not be read: AccessDenied: ERROR_ACCESS_DENIED (5): access denied)
BootOrder    Boot0000, Boot0004 (unreadable)
Boot entries 1
```

Device paths use efibootmgr-style node renderings (`PciRoot`,
`Pci`, `USB`, `MAC`, `IPv4`, `IPv6`, `Sata`, `NVMe`, `Uri`, `HD`, `CDROM`, `File`, `VenHw`,
`VenMedia`, `FvFile`, `Fv`, `BBS`, `Acpi`), hexadecimal in lowercase and GUIDs without braces; an
unknown node prints `Type(0xTT,0xSS,<hex>)`. A `MAC` node prints the address as colon-separated
lowercase hexadecimal bytes followed by the interface type, `MAC(00:11:22:33:44:55,1)`: six bytes
for interface types 0 and 1 (Ethernet), all 32 bytes of the EFI_MAC_ADDRESS otherwise. Legacy
`BBS(HD,...)` entries appear only when the BIOS "Legacy Option ROMs" setting is on.

`--json` (`bootCurrent` and `bootNext` are objects; `present`, `timeoutPresent` and
`bootOrderPresent` are `true`, `false` when the variable does not exist, or `null` when it could
not be read after an incomplete enumeration, and then `error`, `timeoutError` or `bootOrderError`
is `"could not be read: <Status>: <error>"`; `note` in `bootCurrent`, `bootNext` and each
`bootOrder` position is `"missing"` for a dangling reference, `"unreadable"` for an entry that
could not be read after an incomplete enumeration (in `bootCurrent` and `bootNext`, `error` then
holds `"could not be read: <Status>: <error>"`), and `null` otherwise; the example is cut to the
first of the four `entries`):

<!-- schema: BootDocument -->
```json
{
  "command": "boot",
  "ok": true,
  "enumeration": {
    "method": "NtEnumerate",
    "entriesParsed": 63,
    "count": 63,
    "error": null,
    "warnings": []
  },
  "bootCurrent": {
    "present": true,
    "index": 0,
    "name": "Boot0000",
    "description": "Windows Boot Manager",
    "entryExists": true,
    "error": null,
    "note": null
  },
  "bootNext": {
    "present": false,
    "index": null,
    "name": null,
    "description": null,
    "entryExists": false,
    "error": null,
    "note": null
  },
  "timeoutPresent": true,
  "timeoutSeconds": 2,
  "timeoutError": null,
  "bootOrderPresent": true,
  "bootOrderError": null,
  "bootOrder": [
    {
      "position": 1,
      "index": 0,
      "name": "Boot0000",
      "entryExists": true,
      "description": "Windows Boot Manager",
      "note": null
    },
    {
      "position": 2,
      "index": 1,
      "name": "Boot0001",
      "entryExists": true,
      "description": "UEFI: ST500DM002-1BD142, Partition 1",
      "note": null
    },
    {
      "position": 3,
      "index": 2,
      "name": "Boot0002",
      "entryExists": true,
      "description": "UEFI: IP4 Intel(R) Ethernet Connection I217-LM",
      "note": null
    },
    {
      "position": 4,
      "index": 4,
      "name": "Boot0004",
      "entryExists": false,
      "description": null,
      "note": "missing"
    }
  ],
  "entries": [
    {
      "name": "Boot0000",
      "index": 0,
      "key": "8be4df61-93ca-11d2-aa0d-00e098032b8c:Boot0000",
      "size": 179,
      "variableAttributes": "0x00000007 NV BS RT",
      "attributes": 1,
      "active": true,
      "forceReconnect": false,
      "hidden": false,
      "category": "Boot",
      "description": "Windows Boot Manager",
      "devicePath": "HD(1,GPT,6f6e5d1c-8f7a-4d33-9a2b-3c4d5e6f7081,0x800,0x32000)/File(\\EFI\\Microsoft\\Boot\\bootmgfw.efi)",
      "devicePathNodes": [
        "HD(1,GPT,6f6e5d1c-8f7a-4d33-9a2b-3c4d5e6f7081,0x800,0x32000)",
        "File(\\EFI\\Microsoft\\Boot\\bootmgfw.efi)"
      ],
      "optionalDataLength": 15,
      "optionalDataText": "WINDOWS",
      "inBootOrder": true,
      "error": null
    }
  ],
  "warnings": [
    "BootOrder position 4 references Boot0004, which does not exist."
  ]
}
```

A malformed entry has `"error"` set, for example
`"error": "FilePathListLength 200 exceeds the 88 byte(s) after the description"`, and the other
fields hold whatever was parsed before the problem.

### `secureboot`

```
uefiaccess secureboot [--json]
```

Prints two tables. The first, `Secure Boot state from the variables`, decodes the variables in
the enumeration: `SecureBoot` (`enabled (1)` / `disabled (0)`); `SetupMode`, worded with the mode
the three mode variables together imply (UEFI 2.10 section 32.3), in the same words as the decoder
and the application, followed by SetupMode's own value: `audit mode (1)` when AuditMode is 1,
otherwise `setup mode (1)`, `deployed mode (0)` when DeployedMode is 1, or `user mode (0)` when
DeployedMode is 0 or absent, and `absent` when there is no SetupMode variable;
`AuditMode` (`audit mode (1)`, `not audit mode (0)` or `absent`) and `DeployedMode`
(`deployed mode (1)`, `not deployed mode (0)` or `absent`; on the 2014 firmware both are absent,
as the OptiPlex 7020 with BIOS A18 showed on 22 September 2026, which is reported, not treated as
an error), `PK` and `KEK` (`present, <n> bytes` or `absent`), and `db`, `dbx`, `dbt` (size, number of
EFI_SIGNATURE_LIST structures, number of signatures and the distinct signature types such as
`X509` and `SHA256`, or `absent`). The second, `Secure Boot policy sources`, is the policy
inspector's view: the registry value
`HKLM\SYSTEM\CurrentControlSet\Control\SecureBoot\State\UEFISecureBootEnabled`, the four
variables read one by one through the store, the PowerShell cmdlet `Confirm-SecureBootUEFI` (run
with `-NoProfile -NonInteractive`, 10-second timeout), whether the firmware is Secure Boot
capable, and whether the sources agree.

- `Secure Boot capable` is true when the `PK` variable exists or the `SetupMode` variable could be
  read (the "UEFI" half of that rule is implied because the store only
  answers on UEFI firmware).
- `Sources agree` is true when at least one of the registry value, the `SecureBoot` variable and
  the PowerShell result is available and every available one reports the same enabled state; it is
  false when none is available, so the gate never records an agreement that was not observed.
- An unavailable source prints `unreadable` (registry, `SecureBoot`, `SetupMode`), `absent`
  (`DeployedMode`, `AuditMode`) or `not available` (PowerShell) and is left out of the agreement.

```
C:\src\UefiAccess> uefiaccess secureboot
Secure Boot state from the variables:
Variable     Value
SecureBoot   enabled (1)
SetupMode    user mode (0)
AuditMode    absent
DeployedMode absent
PK           present, 858 bytes
KEK          present, 1560 bytes
db           3143 bytes, 2 signature lists, 2 signatures (X509)
dbx          21148 bytes, 1 signature list, 440 signatures (SHA256)
dbt          absent

Secure Boot policy sources:
Source                            Value
Registry UEFISecureBootEnabled    true
Variable SecureBoot               true
Variable SetupMode                false
Variable DeployedMode             absent
Variable AuditMode                absent
PowerShell Confirm-SecureBootUEFI true
Secure Boot capable               true
Sources agree                     true
```

With Secure Boot off the first rows read `SecureBoot   disabled (0)`, `Registry UEFISecureBootEnabled    false`,
`Variable SecureBoot               false` and `PowerShell Confirm-SecureBootUEFI false`. Every
problem the inspector met (registry read failed, PowerShell could not run, timed out, or did not
return `True`/`False`, a flag variable with unexpected data) is written as
`Warning: policy: <error>` to standard error and makes the exit code 6. A disagreement between the
decoded `SecureBoot` variable and the inspector's own read of it is an observation, not a failed
source: it is written as
`Warning: The SecureBoot variable decoded as <true|false> but the policy inspector read <true|false>.`
and does not change the exit code. The memo must record a `Sources agree` of `false` together with
the differing values, and quote a disagreement warning if one appears.

`--json` (`state` is the Core `SecureBootState`, `policy` the Core `SecureBootPolicyResult`;
`warnings` holds the same texts as the `Warning:` lines without the leading `Warning: `, so a
policy error reads `policy: <error>` and a disagreement `The SecureBoot variable decoded as ...`):

<!-- schema: SecureBootDocument -->
```json
{
  "command": "secureboot",
  "ok": true,
  "enumeration": {
    "method": "NtEnumerate",
    "entriesParsed": 63,
    "count": 63,
    "error": null,
    "warnings": []
  },
  "state": {
    "secureBoot": true,
    "setupMode": false,
    "auditMode": null,
    "deployedMode": null,
    "pkPresent": true,
    "pkSize": 858,
    "kekPresent": true,
    "kekSize": 1560,
    "db": {
      "size": 3143,
      "listCount": 2,
      "signatureTypes": [
        "X509",
        "X509"
      ],
      "signatureCount": 2,
      "error": null
    },
    "dbx": {
      "size": 21148,
      "listCount": 1,
      "signatureTypes": [
        "SHA256"
      ],
      "signatureCount": 440,
      "error": null
    },
    "dbt": null
  },
  "policy": {
    "registryEnabled": true,
    "variableSecureBoot": true,
    "variableSetupMode": false,
    "variableDeployedMode": null,
    "variableAuditMode": null,
    "powerShellConfirm": true,
    "capable": true,
    "sourcesAgree": true,
    "errors": []
  },
  "sourcesAgree": true,
  "warnings": []
}
```

### `dell`

```
uefiaccess dell [--source auto|dcm|cctk] [--all] [--json]
```

| Option | Effect |
|---|---|
| `--json` | one JSON document instead of the tables |
| `--source auto` | the default: Dell Command Monitor, and Dell Command Configure only when Dell Command Monitor gives no answer (the fallback) |
| `--source dcm` | Dell Command Monitor only; Dell Command Configure is never started |
| `--source cctk` | Dell Command Configure only |
| `--all` | also lists every attribute Dell Command Monitor reports (72 on BIOS A18) with its codes, Dell's names for them and Dell's read-only flag, as reported. Together with `--source cctk` it is a usage error (exit 2): `The --all option needs Dell Command Monitor; leave out --source or choose dcm.` Any other `--source` value is a usage error too |

Shows the Dell BIOS settings the tool reports (the ten rows of the agreed list) in the words of
the BIOS setup screen (F2), read through
Dell's own interface in Windows. It changes nothing: not a Dell BIOS setting, not a variable, not a
service. It reads, within a budget of 120 seconds for the whole read (a timed-out Dell Command
Configure run can add the few seconds its kill takes):

- Dell Command Monitor: one fixed query of its class `DCIM_BIOSEnumeration` in `root\dcim\sysman`,
  bounded at 60 seconds, and only when that class is absent, whether the namespace exists (to tell
  "not installed" from "installed, but its class is missing");
- Dell Command Configure (the fallback, or `--source cctk`): `cctk.exe` from the two Program Files
  folders only, one read option per setting and one run at a time, each bounded at 15 seconds; it
  reads nine of the ten settings (never Boot list option) and prints current values only, so the
  possible values shown are those of the attribute map for BIOS A18. Any unexpected exit code stops
  the runs;
- the manufacturer, the model and the BIOS version (never the serial number or service tag), so a
  computer that is not a Dell is told so and a BIOS other than A18 gets a warning.

Values are named from Dell's own names, never from codes, and are shown as the BIOS setup words
them (`Enable` becomes `Enabled`, Dell Command Monitor's `LAN` becomes `LAN Only`). The settings of
one run all come from one interface. `TPM activation` reads
`not applicable while TPM security is Disabled` while TPM security is Disabled, because the BIOS
setup hides it then. Dell Command Configure needs administrator rights, and Dell Command Monitor's
enumeration is expected to as well; the command runs the usual precondition checks first, so it
reads nothing unless the prompt is elevated.

```
C:\src\UefiAccess> uefiaccess dell
Field   Value
Source  Dell Command Monitor
Loaded  2026-10-05 09:12:44 +02:00 (1180 ms)
BIOS    A18 (names and values checked against A18)
Changes none: this tool shows Dell BIOS settings and changes none of them

Setting           Current value Possible values
Boot list option  UEFI          Legacy, UEFI
Secure Boot       Enabled       Enabled
SATA operation    AHCI          Disabled, ATA, AHCI
TPM security      Enabled       Enabled, Disabled
TPM activation    Activated     Deactivated, Activated
Virtualization    Enabled       Disabled, Enabled
VT for Direct I/O Enabled       Disabled, Enabled
Integrated NIC    Enabled       Disabled, Enabled with PXE, Enabled, Enabled with image server
Wake on LAN       Disabled      Disabled, LAN Only, LAN with PXE Boot
Num Lock at boot  Enabled       Disabled, Enabled

Note: Secure Boot: Dell Command Monitor lists only Enabled; the BIOS setup also offers Disabled.
```

`Loaded` is when the read started, local time with its offset, and how long it took. One `Note:`
line follows for each setting that has a note. A current value Dell does not name reads
`a value Dell does not name` (the code is in the JSON only), a setting the interface left out
reads `not reported`, one reported without a value reads `no value reported` (Dell Command Monitor's
Secure Boot while it is off), and a name the list does not know is shown as Dell's own word. When
Dell Command Monitor gives no answer and Dell Command Configure does, the source row reads
`Dell Command Configure`, Boot list option reads `not reported` with
`Note: Boot list option: This version reads it through Dell Command Monitor only.`, standard error
gets `Warning: Dell Command Monitor is not installed. Dell Command Configure was used instead.` (the
first part names Dell Command Monitor's cause), and the exit code is 6. On a BIOS other than A18,
standard error gets `Warning: These names and values were checked against BIOS A18 only; this computer runs BIOS A17.`

When no interface answers there is no table: the problem sentence goes to standard error alone and
the exit code is 5. The sentence names each cause and ends with one piece of advice, for example
(on a computer with neither Dell tool, the likely case on a PC without the Dell software)
`Dell BIOS settings cannot be read. Dell Command Monitor is not installed. Dell Command Configure was not found. Install the Microsoft Visual C++ 2015-2022 x64 runtime, then Dell Command Monitor, and try again; the Dell interface setup page of the documentation has the steps.`
Other causes: `Dell Command Monitor is installed, but its WMI provider did not load.` (the Visual
C++ runtime is missing), `Dell Command Monitor answered with no BIOS settings.` (its data service,
DSM SA Data Manager, is not running; the tool never starts it), `Windows refused access to Dell Command Monitor.`,
`Dell Command Monitor did not answer within 60 seconds.`, and the computer's manufacturer when it is
not a Dell. The Dell interface setup page lists every cause and its advice.

`--all` adds a third table after the notes, every attribute in Dell Command Monitor's order, each
value as `code=name` (cut here to three of the 72 rows):

```
Attribute                                    Current                      Possible                                                                              Dell read-only flag
AC Power Recovery Mode                       1=Off                        1=Off, 2=Last, 3=On                                                                   false
Active State Power Management                1=Auto                       1=Auto, 2=Disable, 3=L1 Only                                                          false
Admin Setup Lockout                          1=Disable                    1=Disable, 2=Enable                                                                   false
```

`--json` (`machine` is the Core `DellMachine`, three fields only; `current` and `possibleValues`
hold each value's `token` (Dell Command Monitor's code, or Dell Command Configure's word),
`vendorName` (Dell Command Monitor's name, `null` for Dell Command Configure) and `text` (the words
shown); `text` and `sentence` are what the table and a screen reader give for the row;
`vendorMarksReadOnly` is Dell's own flag, which Dell Command Monitor sets by name for these
settings, `null` for Dell Command Configure; `allAttributes` is `null` without `--all`; the example
is cut to two of the ten settings):

<!-- schema: DellSettingsDocument -->
```json
{
  "command": "dell",
  "ok": true,
  "outcome": "Complete",
  "source": "DellCommandMonitor",
  "readAt": "2026-10-05T09:12:44+02:00",
  "elapsedMs": 1180,
  "machine": {
    "manufacturer": "Dell Inc.",
    "model": "OptiPlex 7020",
    "biosVersion": "A18"
  },
  "checkedAgainstBios": "A18",
  "listStatus": "Approved list of the Dell BIOS settings this tool shows",
  "nativeInterfacePresent": false,
  "settings": [
    {
      "id": "SecureBoot",
      "name": "Secure Boot",
      "displayName": "Secure Boot",
      "category": "Secure Boot",
      "setupLocation": "Secure Boot, Secure Boot Enable",
      "state": "Read",
      "current": {
        "token": "2",
        "vendorName": "Enable",
        "text": "Enabled"
      },
      "text": "Enabled",
      "possibleValues": [
        {
          "token": "2",
          "vendorName": "Enable",
          "text": "Enabled"
        }
      ],
      "possibleValuesOrigin": "Interface",
      "vendorMarksReadOnly": false,
      "displayFromVendor": false,
      "note": "Dell Command Monitor lists only Enabled; the BIOS setup also offers Disabled.",
      "sentence": "Secure Boot: Enabled. Possible values: Enabled. Dell Command Monitor lists only Enabled; the BIOS setup also offers Disabled."
    },
    {
      "id": "NumLock",
      "name": "Num Lock",
      "displayName": "Num Lock at boot",
      "category": "POST Behavior",
      "setupLocation": "POST Behavior, Numlock LED",
      "state": "Read",
      "current": {
        "token": "2",
        "vendorName": "Enable",
        "text": "Enabled"
      },
      "text": "Enabled",
      "possibleValues": [
        {
          "token": "1",
          "vendorName": "Disable",
          "text": "Disabled"
        },
        {
          "token": "2",
          "vendorName": "Enable",
          "text": "Enabled"
        }
      ],
      "possibleValuesOrigin": "Interface",
      "vendorMarksReadOnly": false,
      "displayFromVendor": false,
      "note": null,
      "sentence": "Num Lock at boot: Enabled. Possible values: Disabled, Enabled."
    }
  ],
  "allAttributes": null,
  "fallback": null,
  "problems": [],
  "warnings": [],
  "sentence": null
}
```

With `--all`, `allAttributes` lists every attribute exactly as Dell Command Monitor reported it
(`currentNames` joins the current codes' names; `null` when a code has no name):

```json
"allAttributes": [
  {
    "name": "AC Power Recovery Mode",
    "currentValues": [ "1" ],
    "currentNames": "Off",
    "possibleValues": [ "1", "2", "3" ],
    "possibleNames": [ "Off", "Last", "On" ],
    "isReadOnly": false
  }
]
```

When no interface answered, the document has `"ok": false`, `"outcome": "Unavailable"`,
`"source": null`, every setting `NotRead` (`"text": "not read"`), each problem with its interface,
kind and technical detail, and the problem sentence in `sentence` (the same text as on standard
error); exit code 5 (cut to one of the ten settings):

<!-- schema: DellSettingsDocument -->
```json
{
  "command": "dell",
  "ok": false,
  "outcome": "Unavailable",
  "source": null,
  "readAt": "2026-10-05T09:12:44+02:00",
  "elapsedMs": 40,
  "machine": {
    "manufacturer": "Dell Inc.",
    "model": "OptiPlex 7020",
    "biosVersion": "A18"
  },
  "checkedAgainstBios": "A18",
  "listStatus": "Approved list of the Dell BIOS settings this tool shows",
  "nativeInterfacePresent": false,
  "settings": [
    {
      "id": "BootListOption",
      "name": "Boot Mode",
      "displayName": "Boot list option",
      "category": "General",
      "setupLocation": "General, Boot Sequence, Boot List Option",
      "state": "NotRead",
      "current": null,
      "text": "not read",
      "possibleValues": [],
      "possibleValuesOrigin": "Catalog",
      "vendorMarksReadOnly": null,
      "displayFromVendor": false,
      "note": null,
      "sentence": "Boot list option: not read."
    }
  ],
  "allAttributes": null,
  "fallback": null,
  "problems": [
    {
      "interface": "DellCommandMonitor",
      "kind": "NotInstalled",
      "detail": "the namespace root\\dcim\\sysman is absent"
    },
    {
      "interface": "DellCommandConfigure",
      "kind": "ProgramNotFound",
      "detail": "cctk.exe was not found in the Program Files folders"
    }
  ],
  "warnings": [],
  "sentence": "Dell BIOS settings cannot be read. Dell Command Monitor is not installed. Dell Command Configure was not found. Install the Microsoft Visual C++ 2015-2022 x64 runtime, then Dell Command Monitor, and try again; the Dell interface setup page of the documentation has the steps."
}
```

Exit codes: 0 when every agreed setting was read (`Complete`), 6 when an interface answered for
only some of them (`Partial`, as after the fallback), 5 when none answered (`Unavailable`), 2 for a
usage error, 3 and 4 for the preconditions.

### `baseline`

```
uefiaccess baseline [--json] [--out <dir>]
```

| Option | Effect |
|---|---|
| `--json` | one JSON document instead of the table |
| `--out <dir>`, `-o <dir>` | parent directory for the export; default `%ProgramData%\UefiAccess\baseline`. The timestamped run folder `<yyyyMMdd-HHmmss>` (local time) is created underneath it either way |

Enumerates the store, runs the platform inspectors (identity, TPM, Secure Boot policy with the
PowerShell cross-check, BitLocker, Dell probe), reads the Dell BIOS settings once through Dell
Command Monitor only (as `dell --source dcm` would: that read never starts Dell Command Configure
and never waits for the fallback, in a baseline and so in step 05 of `tools\gate\Run-Gate.ps1`,
which runs one; the Dell probe still runs only its two read options
`--version` and `--secureboot` when that program is installed), decodes every variable a decoder
understands, and writes the export. The table reports the folder, the run id, the counts and the
number of files written. Exit code 6 when the
enumeration was partial or the export reported errors (each one also printed as
`Export error: <error>` on standard error); exit 5 when nothing could be written. The Dell settings
never change the exit code: a Dell read that did not answer (no Dell Command Monitor, a computer
that is not a Dell) is recorded as such in `dell-settings.json`, and its problem sentence, like a
BIOS warning, goes to standard error as `Warning: dell settings: <sentence>`.

Inside the data root (`%ProgramData%\UefiAccess`, the default `--out` or any `--out` below it) an
elevated export writes only through folders that standard users cannot redirect: it creates the
missing folders with a protected access list (SYSTEM and Administrators full control, Users read)
and refuses a folder that is a junction, symbolic link or other reparse point, or that is owned by
anyone other than SYSTEM, Administrators or TrustedInstaller (the folders it creates are given
Administrators as their owner, whatever the machine's owner policy). A refused export writes nothing, shows
`Files written 0` and `Export errors 1`, exits 5 (`"ok": false` in JSON, with the same text in
`errors`), and prints, for a data root left by an older build run without elevation or by an
older test run:
`Export error: Cannot create export directory 'C:\ProgramData\UefiAccess\baseline\<run>\vars': C:\ProgramData\UefiAccess is owned by <DOMAIN\account>, not by Administrators or SYSTEM, so a standard user could redirect what this elevated run writes there. Rename or delete C:\ProgramData\UefiAccess from an administrator prompt; the next elevated run creates it again with safe permissions.`
An `--out` outside the data root is not checked. `docs/BUILD.md` section 5 has the rename command.

```
C:\src\UefiAccess> uefiaccess baseline
Field                Value
Baseline directory   C:\ProgramData\UefiAccess\baseline\20260910-143012
Run id               20260910-143012
Variables enumerated 63
Variables exported   63
Variables decoded    14
Files written        68
Export errors        0
```

`--json` (`filesWritten` lists every file relative to the folder, in write order: the `vars/`
files, then `index.json`, `decoded.json`, `platform.json`, `dell-settings.json`, `log.txt`; the
example is cut to seven of the 68 entries):

<!-- schema: BaselineDocument -->
```json
{
  "command": "baseline",
  "ok": true,
  "directory": "C:\\ProgramData\\UefiAccess\\baseline\\20260910-143012",
  "runId": "20260910-143012",
  "enumeration": {
    "method": "NtEnumerate",
    "entriesParsed": 63,
    "count": 63,
    "error": null,
    "warnings": []
  },
  "variableCount": 63,
  "decodedCount": 14,
  "filesWritten": [
    "vars/8be4df61-93ca-11d2-aa0d-00e098032b8c_Boot0000.bin",
    "vars/8be4df61-93ca-11d2-aa0d-00e098032b8c_Boot0001.bin",
    "index.json",
    "decoded.json",
    "platform.json",
    "dell-settings.json",
    "log.txt"
  ],
  "errors": []
}
```

The folder layout:

```
index.json          tool version, timestamp (UTC and local), firmware type, privilege state,
                    machine identity, variable count, and one entry per variable:
                    { Key, Name, Guid, Attributes, AttributeNames, Size, Sha256, File }
vars/<guid>_<name>.bin    raw data, one file per variable; names sanitised to [A-Za-z0-9_.-]
decoded.json        every variable a decoder understood: boot order, boot entries, Secure Boot
                    state, OsIndications
platform.json       machine identity, TPM, Secure Boot policy, BitLocker, Dell interface probe
dell-settings.json  the agreed Dell BIOS settings of one read through Dell Command Monitor (
                    folders saved before it have none): codes and Dell's names only
log.txt             the run log (the lines of this run that went to the daily log file), followed
                    by the exporter's outcome block: the line "Baseline export outcome", the number
                    of variables indexed, the files written before log.txt (one per line) and the
                    export errors (one per line); written last, so it records what the export produced
```

The four JSON files are written by the Core `BaselineExporter` with PascalCase member names and
the default System.Text.Json escaping (`+` as `\u002B`, `"` as `\u0022`; every JSON parser reads
them back unchanged), unlike the camelCase `--json` documents and `gate.json`. `index.json` is
deterministic in its entries: sorted by key, fixed property order, SHA-256 per variable, no
per-entry timestamps, so the `Variables` array and every `vars/*.bin` file are byte-identical for
an unchanged store. The header (`ToolVersion`, `TimestampUtc`, `TimestampLocal`, `Identity`)
differs per export, so two whole `index.json` files do not compare byte for byte; compare the
`Variables` array or the per-variable `Sha256` values. `AttributeNames` there is the short flag
text. An excerpt (cut to two of the 63 entries):

<!-- schema: BaselineIndex (exporter) -->
```json
{
  "ToolVersion": "0.1.0\u002B0123456789abcdef0123456789abcdef01234567",
  "TimestampUtc": "2026-09-10T12:30:12Z",
  "TimestampLocal": "2026-09-10T14:30:12\u002B02:00",
  "FirmwareType": "Uefi",
  "PrivilegeState": "Enabled",
  "Identity": {
    "Manufacturer": "Dell Inc.",
    "Model": "OptiPlex 7020",
    "SystemSku": "0620",
    "BiosVersion": "A18",
    "BiosReleaseDate": "2019-05-02",
    "ServiceTag": "5XK2P52",
    "BaseBoard": "0F5C5X",
    "Cpu": "Intel(R) Core(TM) i5-4590 CPU @ 3.30GHz",
    "OsCaption": "Microsoft Windows 11 Pro",
    "OsVersion": "10.0.22631",
    "OsBuild": "22631",
    "ExpectedModelConfirmed": true,
    "ExpectedModelNotes": "Model \u0022OptiPlex 7020\u0022: contains 7020 (match). BIOS version \u0022A18\u0022: Dell A-series version (match). CPU \u0022Intel(R) Core(TM) i5-4590 CPU @ 3.30GHz\u0022: 4th-generation Intel (Haswell) part (match). Expected model confirmed: 2014 Dell OptiPlex 7020 (model, BIOS version and CPU generation all match).",
    "Errors": []
  },
  "VariableCount": 63,
  "Variables": [
    {
      "Key": "8be4df61-93ca-11d2-aa0d-00e098032b8c:Boot0000",
      "Name": "Boot0000",
      "Guid": "8be4df61-93ca-11d2-aa0d-00e098032b8c",
      "Attributes": 7,
      "AttributeNames": "NV BS RT",
      "Size": 179,
      "Sha256": "d170c1c788586cad8c201d624ac5abf66878a4ed99f749f1912b1ee53907ba15",
      "File": "vars/8be4df61-93ca-11d2-aa0d-00e098032b8c_Boot0000.bin"
    },
    {
      "Key": "8be4df61-93ca-11d2-aa0d-00e098032b8c:BootOrder",
      "Name": "BootOrder",
      "Guid": "8be4df61-93ca-11d2-aa0d-00e098032b8c",
      "Attributes": 7,
      "AttributeNames": "NV BS RT",
      "Size": 8,
      "Sha256": "cd219a5ea3867d8b0ce828cc3ac7eef6a381ec1877fa530df71acc8263c6fca4",
      "File": "vars/8be4df61-93ca-11d2-aa0d-00e098032b8c_BootOrder.bin"
    }
  ]
}
```

`decoded.json` is an array with one `DecodedVariable` per variable a decoder understood, in key
order: `Key`, `Kind` (`BootOrder` for BootOrder, BootNext, BootCurrent and Timeout; `LoadOption`
for Boot####; `SecureBootState` for SecureBoot, SetupMode, AuditMode, DeployedMode, PK, KEK, db,
dbx, dbt; `OsIndications` for OsIndications and OsIndicationsSupported), a one-line `Summary`,
`Fields` (ordered `Name`/`Value` pairs), a decoder-specific `Json` object and `Error` (`null` when
the data was well formed). One element:

<!-- schema: DecodedVariable (exporter) -->
```json
{
  "Key": "8be4df61-93ca-11d2-aa0d-00e098032b8c:Timeout",
  "Kind": "BootOrder",
  "Summary": "Timeout: 2 seconds (delay before booting the default entry)",
  "Fields": [
    {
      "Name": "Seconds",
      "Value": "2"
    },
    {
      "Name": "Meaning",
      "Value": "delay before booting the default entry"
    }
  ],
  "Json": {
    "Name": "Timeout",
    "Seconds": 2,
    "WaitsForever": false,
    "Error": null
  },
  "Error": null
}
```

`platform.json` is the Core `PlatformSnapshot`: `Identity`, `Tpm`, `SecureBootPolicy`, `BitLocker`
and `Dell`, each with the same members as the `identity`, `tpm`, `secureBootPolicy`, `bitLocker`
and `dellInterface` sections of `gate.json` (section 4), in PascalCase.

`dell-settings.json` is the Core `DellSettingsFileDocument`: `Format`
(always `uefiaccess-dell-settings/1`), `ReadAtUtc`, `Outcome`, `Source` (`null` when nothing
answered), `Machine` (`Manufacturer`, `Model`, `BiosVersion`; never the serial number or service
tag), `Settings` (one per agreed setting: `Id`, `State`, `CurrentToken`, `CurrentVendorName`,
`PossibleValues` with `Token` and `VendorName`, `PossibleValuesOrigin`, `VendorMarksReadOnly`) and
`Problems` (`Interface` and `Kind`, never a detail). It holds tokens and Dell's names only, no
display text: `DellSettingsFile.Read` derives the words again through the current list of agreed
settings when a baseline folder is opened, and refuses a file that is a link, larger than 256 KiB,
of another format or over its limits.

### `gate`

```
uefiaccess gate [--json] [--ifr-varstores <file>] [--out <file>]
```

| Option | Effect |
|---|---|
| `--json` | print the gate report (the content of `gate.json`) on standard output instead of the table |
| `--ifr-varstores <file>` | text file with one `Name;GUID;Size` line per varstore from the IFR extraction (see `tools/gate/ifr-varstores.example.txt`; `#` lines and blank lines are skipped; the name may keep the double quotes IFRExtractor-RS prints around it, `"Setup"`, which are removed; the size is decimal or `0x` hexadecimal, so `0x2E5` can be copied as printed); each varstore is marked present at runtime (with attributes and whether the size matches), absent, or, after an incomplete enumeration whose direct read also failed, unknown. A file that does not exist or cannot be read is a usage error (exit 2) and stops the command before anything is probed |
| `--out <file>`, `-o <file>` | where to write the report; default `gate.json` in the current directory. A folder inside the data root `%ProgramData%\UefiAccess` (where `tools\gate\Run-Gate.ps1` has `gate` write `gate\<yyyyMMdd-HHmmss>\gate.json`) is checked and created like the `baseline` export folder (see below) |

Runs gate questions (a), (b), and (d), and the varstore match for (c) when `--ifr-varstores` is
given, writes `gate.json`, then prints a `Finding Value` table and, when varstores were matched, a
`Varstore match` table, and finally `Gate report written to <path>`. It does not compute a
recommendation; a human fills that in. Install Dell
Command | Monitor and Dell Command | Configure before running so that (b) can be answered. The Dell
probe looks for `cctk.exe` only under the Program Files folders Windows reports as known folders,
never under a folder an environment variable names
(`C:\Program Files (x86)\Dell\Command Configure\X86_64\cctk.exe`, then the same under
`C:\Program Files`), runs it by that full path only, and only as `cctk --version` and `cctk --secureboot`; the
`(b) cctk version` row shows cctk's own output flattened to one line, and gate.json keeps the
`--secureboot` output in `dellInterface.cctkSecureBootOutput`.

For Dell Command | Monitor the probe first lists which of `DCIM_BIOSEnumeration`,
`DCIM_BIOSString`, `DCIM_BIOSInteger` and `DCIM_BIOSService` are defined in `root\dcim\sysman`
(`(b) Dell Command Monitor classes defined`), then asks each defined class for one instance
(`DCIM_BIOSEnumeration` for three, which become the samples), each query bounded at 60 seconds
(`(b) Dell Command Monitor classes responding`). A class that is defined but does not answer (the
Dell Command | Monitor service stopped, its provider failing on this Windows build) is listed as
defined but not responding, with the reason as a `Warning: dell probe: ...` line and in
`dellInterface.errors`; after a query that timed out, the remaining classes are not queried and
say so. Outcome A needs `DCIM_BIOSEnumeration` among the responding classes and samples with
readable values.

```
C:\src\UefiAccess> uefiaccess gate --ifr-varstores C:\work\ifr-varstores.txt --out C:\work\gate.json
Finding                                     Value
Model                                       OptiPlex 7020
BIOS version                                A18
CPU                                         Intel(R) Core(TM) i5-4590 CPU @ 3.30GHz
Expected model confirmed                    true
Expected model notes                        Model "OptiPlex 7020": contains 7020 (match). BIOS version "A18": Dell A-series version (match). CPU "Intel(R) Core(TM) i5-4590 CPU @ 3.30GHz": 4th-generation Intel (Haswell) part (match). Expected model confirmed: 2014 Dell OptiPlex 7020 (model, BIOS version and CPU generation all match).
(a) Firmware type                           Uefi
(a) Secure Boot capable                     true
(a) Secure Boot enabled                     true
(a) Setup mode                              false
(a) Sources agree                           true
(a) Runtime variables                       63 (NtEnumerate)
(b) Dell Command Monitor classes defined    DCIM_BIOSEnumeration, DCIM_BIOSString, DCIM_BIOSInteger, DCIM_BIOSService
(b) Dell Command Monitor classes responding DCIM_BIOSEnumeration, DCIM_BIOSString, DCIM_BIOSInteger, DCIM_BIOSService
(b) Dell sample attributes                  3
(b) cctk found                              C:\Program Files (x86)\Dell\Command Configure\X86_64\cctk.exe
(b) cctk version                            Dell Command | Configure Version 4.8.0
(b) Native biosattributes namespace         false
(b) Native wmisecurity namespace            false
(c) Varstores in file                       3
(c) Present at runtime                      1
(c) Absent at runtime                       2
(c) Status unknown                          0
(c) Size mismatches                         0
(c) Enumeration complete                    true
(d) BitLocker volumes                       1
(d) C:                                      Encrypted; protection On; protectors TPM, Numerical password; PCR 7+11
(d) Registry Secure Boot enabled            true
(d) PowerShell Confirm-SecureBootUEFI       true
TPM present                                 true
TPM spec version                            1.2, 2, 3
Recommendation                              (left for the memo author)

Varstore match:
Name     GUID                                 IFR size Present Runtime size Flags    Size matches
Setup    ec87d643-eba4-4bb5-a1e5-3f3e36b20da9 741      yes     741          NV BS RT yes
PchSetup 4570b7f1-ade8-4943-8dc3-406472842384 51       no      -            -        -
SaSetup  72c5e28c-7783-43a1-8767-fad73fccafa4 44       no      -            -        -

Gate report written to C:\work\gate.json
```

Without `--ifr-varstores` the six `(c)` rows are replaced by
`(c) Varstore match                          not run (no --ifr-varstores file)` and there is no
`Varstore match` table. The two `(b) Dell Command Monitor classes` rows show `none` when no class
is defined or none answered, `(b) cctk found` shows `no` when `cctk.exe` is not installed and
`(b) cctk version` then `(not run)`. After an incomplete enumeration `(c) Enumeration complete`
reads `false: <reason>; unlisted names were read directly`, where `<reason>` is
`the known-names fallback lists only the curated names and Boot#### entries`,
`the enumeration stopped early or reported warnings` or `no variables could be enumerated`, and a
varstore whose direct read failed counts under `(c) Status unknown` and shows `unknown` in the
`Present` column. One `(d) <drive>` row appears per encryptable volume: the conversion status
(`Decrypted`, `Encrypted`, `Encrypting`, `Decrypting`, `Encryption paused`, `Decryption paused`,
`Unknown`), `protection` and the protection status (`On`, `Off`, `Unknown`, or, while the volume
is still being encrypted, `Pending until encryption completes` or `Pending, encryption paused`,
never `Off`), the protector type names and the PCR profile (`PCR 7+11`, `PCR 0,2,4,11 (legacy)`,
another `PCR ...` list, or `no TPM profile`). Right after BitLocker is turned on the row reads, for
example, `Encrypting; protection Pending until encryption completes; protectors TPM, Numerical password; PCR 7+11`.
The BitLocker enumeration runs inside one 60-second bound, and each of its three read-only method
calls, with the fetch of that method's parameter definition, inside another; after the first
call that times out, the remaining calls are not made, and each is reported as
`Warning: bitlocker: <method> on <drive>: not called, because <call> timed out`.

Every inspector error goes to standard error as `Warning: identity: ...`,
`Warning: secure boot policy: ...`, `Warning: dell probe: ...`, `Warning: bitlocker: ...`,
`Warning: tpm: ...` or `Warning: ifr-varstores: line <n>: ...` and is also stored in the
corresponding `errors` (or `parseErrors`) list of the report; these do not change the exit code,
and neither does `Warning: ifr-varstores: <n> varstores could not be checked because <reason> and the direct read failed; their status is unknown`.
If the enumeration produced nothing, `No variables could be enumerated: <error>` is written to
standard error, the report is still written with `variableCount` 0 (every varstore is then read
directly), and the exit code is 5. Exit 5 also when `gate.json` could not be written
(`Writing '<path>' failed: ...`, and the last line reads `Gate report could not be written to <path>`);
exit 6 when the enumeration was partial.

An `--out` inside the data root (`%ProgramData%\UefiAccess`) goes through the same check as a
`baseline` export there before anything is created: an elevated run creates the missing folders
with the protected access list and refuses a folder on the way that is a junction, symbolic link
or other reparse point, or that is owned by anyone other than SYSTEM, Administrators or
TrustedInstaller. A refused `gate` creates and writes
nothing, still prints the table, whose last line reads `Gate report could not be written to <path>`
(or, with `--json`, the report), exits 5, and writes the reason to standard error, for example:
`Writing 'C:\ProgramData\UefiAccess\gate\20260922-101500\gate.json' failed: C:\ProgramData\UefiAccess\gate is a junction, symbolic link or other reparse point, not a plain folder, so a standard user could redirect what this elevated run writes there. Rename or delete C:\ProgramData\UefiAccess\gate from an administrator prompt; the next elevated run creates it again with safe permissions.`
An `--out` outside the data root is not checked.

`--json` prints the document of section 4.

### `write`

```
uefiaccess write bootorder --order Boot0003,Boot0002 [--dry-run | --confirm] [--bitlocker suspend|continue] [--allow-no-change] [--snapshot-root <dir>] [--json]
uefiaccess write bootnext <Boot####>               [--dry-run | --confirm] [--bitlocker suspend|continue] [--snapshot-root <dir>] [--json]
uefiaccess write bootnext --clear                  [--dry-run | --confirm] [--bitlocker suspend|continue] [--snapshot-root <dir>] [--json]
uefiaccess write restore <snapshot folder>         [--dry-run | --confirm] [--bitlocker suspend|continue] [--snapshot-root <dir>] [--json]
uefiaccess write check <name> [--guid <guid>] [--json]
uefiaccess write log [--last <n>] [--json]
```

The one command that changes anything, and it changes two variables under the EFI global GUID
and nothing else: BootOrder, put in a new order of its own entries (none added, removed or
repeated, the same byte count, attributes `NV BS RT`), and BootNext, set to one existing, active
boot entry or removed. Every change goes through the write pipeline of the core library
(`src/UefiAccess.Core/Safety`), the same one the application calls: a
dry run first (stages S1 to S10: the three preconditions, the machine identity, one fresh
complete enumeration, the whitelist and format rules, Secure Boot and the newest baseline, the
BitLocker assessment, the two data roots and the write budget), and only with `--confirm` the
commit (stages S11 to S17: the confirmation, the snapshot folder written and read back, a first
drift read, the optional one-restart BitLocker suspend, a second drift read, exactly one write
call, one verification read, the record). No stage retries, and no option in any build skips a
check.

| Subcommand | What it asks for |
|---|---|
| `bootorder --order <Boot####,...>` | BootOrder's current entries in the order given; every current entry exactly once |
| `bootnext <Boot####>` | a one-time boot of that entry at the next start (BootNext, 2 bytes); the entry must exist, be active and be a boot entry, not an application |
| `bootnext --clear` | the one-time boot choice removed (a write of size 0); refused when none is set |
| `restore <snapshot folder>` | the value a snapshot folder recorded: its full path or its name under the snapshot root. The folder is read back and verified first; the value becomes one of the three changes above, checked against the firmware as it is now, and gets a snapshot folder of its own (`-restore`) |
| `check <name> [--guid <guid>]` | the checks of the dry run for any variable, without a plan: exit 0 when this tool can change it, 7 when it cannot, and the Writability verdict in the document. On a machine that is not the enabled unit the other checks run and the check is then refused (exit 7) at `expectedModel` for another model or BIOS version, or at `expectedUnit` for another OptiPlex 7020 on BIOS A18; the record in `write-log.jsonl` says so too. A malformed BootOrder or BootNext is never changeable. The default GUID follows the rule of `read` |
| `log [--last <n>]` | the newest records of `write-log.jsonl`, oldest first; 20 without `--last`. A `write-log.jsonl` that exists but cannot be read is a failure (exit 5), never an empty list |

| Option | Effect |
|---|---|
| `--dry-run` | `bootorder`, `bootnext` and `restore` only: run the checks and show the plan; this is also what those three do without `--confirm`. Together with `--confirm` it is a usage error (exit 2) |
| `--confirm` | after a dry run that passed, confirm exactly this plan with the `--bitlocker` choice and commit it once. Changes are enabled only on the enabled unit: the 2014 OptiPlex 7020 on BIOS A18 whose Windows machine ID hashes to the one value the code holds (the enabled unit). The check recognizes that unit's Windows installation, not its hardware: a disk clone, a moved drive, a restored system image or a shared deployment image can carry the same machine ID onto another machine, so the unit's disk or system image is not cloned or restored onto another machine while the code holds its value. On every machine whose Windows installation is not a copy of the unit's, the dry run passes and the confirmation is refused, exit 7: at `expectedModel` on another model or BIOS version (`This machine is not the supported OptiPlex 7020 with BIOS A18; nothing is written.`), at `expectedUnit` on another OptiPlex 7020 on BIOS A18 (`This OptiPlex 7020 is not the enabled unit; nothing is written.`) or when the machine ID cannot be read (`The Windows machine ID could not be read, so this unit cannot be recognized as the enabled unit; nothing is written.`) |
| `--bitlocker suspend`, `--bitlocker continue` | the BitLocker choice, needed while BitLocker is on, still encrypting, or in a state that could not be read (treated as on): `suspend` suspends it for one restart after the snapshot and before the write call, `continue` writes with protection on. It is an input of the dry run, so it is accepted without `--confirm`; `suspend` is refused when no suspend is offered (protection off or already suspended, or a clear). Any other value is a usage error (exit 2) |
| `--allow-no-change` | `bootorder` only: accept an order equal to the current one (the first-write drill). It turns off the unchanged-order refusal and nothing else |
| `--snapshot-root <dir>` | the snapshot root instead of `%ProgramData%\UefiAccess\snapshots`, in a dry run only: together with `--confirm` it is a usage error (exit 2), so a confirmed change always keeps its snapshot under the default root, where `snapshots` and the application's Restore previous value look for it. The option exists to show that a refused root blocks the change (failure case FC-20). It must lie under `%ProgramData%\UefiAccess` and goes through the same folder check as every folder the tool writes there: a root outside it is refused before anything is created, and a file or a link in its place is refused at the folder check; both block at `snapshotRoot` (exit 7). The root is checked at stage S9, after the BitLocker check (S8), so with BitLocker on the line needs `--bitlocker` to get that far |
| `--json` | one `WriteDocument` (`bootorder`, `bootnext`, `restore`, `check`) or `WriteLogDocument` (`log`) on standard output |

Rules:

- Without `--confirm`, `bootorder`, `bootnext` and `restore` are dry runs: nothing is written to
  the firmware, no snapshot folder is made, and standard error ends with
  `Dry run: nothing was written.`. `check` and `log` take no `--confirm`, never write and do not
  print that line. A run that reaches the data-root check (stage S9), dry or not, creates the two data roots
  `%ProgramData%\UefiAccess\snapshots` and `%ProgramData%\UefiAccess\writes` with the protected
  access list when they are missing, so the first such run on a machine creates them. Every dry
  run that gets past the preconditions appends one record line to `writes\write-log.jsonl`, and a
  commit that reaches stage S12 appends two more: its `Pending` intent line, before the snapshot
  folder, the suspend and the write call, and then the line with its outcome (see `log` below).
- With BitLocker on, still encrypting or unreadable and no `--bitlocker`, a dry run and a
  confirmed run alike stop at the `bitLocker` check (exit 7). Standard error then carries the
  three BitLocker lines and the hint
  `Add --bitlocker suspend to suspend it for one restart, or --bitlocker continue to write with protection on.`
- `--confirm` makes exactly one write call. Ctrl+C is ignored from the moment the commit starts
  until its result is in (standard error says `Writing the change, please wait. Do not restart the machine; Ctrl+C is ignored until the result is in.`);
  a process that is killed or a power loss in that window leaves a snapshot folder whose
  `record.json` says `Pending`, and its `before` folder is the record of the value before the write.
- A change that the rules refuse, a changed value between the dry run and the call, a snapshot
  that does not read back as written, a failed suspend and the eighth write since Windows last
  started (restart Windows before more changes) all stop before the write call, with nothing
  written (exit 7).
- While BitLocker protectors may exist, a new boot order is refused at `otherWindowsPath` as long
  as a one-time boot is set, whatever it names (a BootNext that is not 2 bytes included), because
  the firmware starts it before the boot order at the next start and a one-restart suspend would
  be used up on that start:
  `A one-time boot (<entry>) is set, and the firmware starts it at the next start before the boot order; a one-restart suspend would be used on that start. Clear the one-time boot first, then check the new order again.`
  `write bootnext --clear` is the way out. With BitLocker off the rule does not apply.
- `write check` and `write log` never write the firmware. `snapshots` lists what the commits left.
- `write log` reads `write-log.jsonl` whole. A missing file shows no record (exit 0); a file that
  exists but cannot be read (held open by another program, or its access list changed) is a
  failure, exit 5, with `The write log <file> could not be shown: the file exists but could not be read: <error>`
  on standard error and, with `--json`, the error document; the write budget refuses a change for
  the same reason (`writeBudget`).

What a run prints. Standard output carries the result only: the tables below, or with `--json`
the one document. Standard error carries, in this order: one `Warning: <text>` line per warning
of the dry run (Secure Boot's state is always one: `Secure Boot: on, user mode. This application never changes it.`;
a BootOrder that differs from the newest baseline, a hidden or removable one-time target, and a
removable entry placed ahead of the current one are others; beside the last, one
`Device path of <entry>: <node types>.` line per entry the checks looked at); when the dry run
reached the BitLocker check (stage S8) with the change's new value known, and BitLocker
protectors may exist, the three BitLocker lines `BitLocker state: <sentence>`, `Risk: <sentence>`
(for a reorder or a one-time boot) and `Recovery key: <sentence>` (a change that an earlier rule
refuses, such as the one-time boot to another Windows Boot Manager of failure case FC-16 at
`otherWindowsPath`, prints none); the hint when BitLocker needs a choice and none was given;
before a commit, the line that Ctrl+C is ignored; the sentence when the exit code is not 0 (with
exit code 0 the sentence is the last line of standard output, or the `sentence` of the document);
and `Dry run: nothing was written.` for every run of `bootorder`, `bootnext` or `restore` without
`--confirm`. The three BitLocker sentences come from Core and never name where the recovery password is kept:

- `BitLocker state: BitLocker protects drive C: with the TPM and checks the boot path (PCR 0, 2, 4, 11; TPM 1.2).`
  (or `BitLocker is still encrypting drive C: ...`, `BitLocker's state could not be read (<reason>); treated as on.`,
  `BitLocker is suspended on C:; protection resumes at a later restart and is then sealed to that start's boot path.`)
- `Risk: Microsoft lists a change of the firmware boot order on a TPM 1.2 machine as a cause of BitLocker recovery.`
  (with ` The first entry stays <entry>.` only when the entry BootCurrent names is first both before and after the change; an order that moves it up to first gets the plain sentence, because the last start went past the first entry and the new order changes the start), or for a one-time boot
  `Risk: The firmware tries <entry> once at the next start, then removes BootNext. If it cannot start, the normal list is used in that start and BitLocker may ask for the recovery key.`
- `Recovery key: If the next start asks for the recovery key, you need the 48-digit recovery password saved when BitLocker was turned on. Have it at hand before you confirm. This application never reads or shows it.`

Exit codes of `write`: 0 the dry run passed, the change was verified, or (`check`) the variable
can be changed; 2 a usage error, before any check runs; 3 and 4 the preconditions of section 2,
with the error document, before any stage of the pipeline; 5 an unexpected failure, or (`log`) a
`write-log.jsonl` that exists but cannot be read; 7 blocked by a
safeguard before any write call, nothing written (`blockedBy` names the check; for `check`, also a
variable this tool cannot change, `expectedModel` on another model or BIOS version, `expectedUnit`
on another OptiPlex 7020 on BIOS A18); 8 the write call was made, the firmware refused it and the
value read back is the old one, nothing changed and a snapshot folder exists; 9 the value read
back is not what the call's result implies (written but not verified, changed despite an error,
or neither the old nor the new value), a snapshot folder exists and a restore is advised. The
numbers are Core's `WriteExitCodes`, which the record carries too. The sentences are the
pipeline's own (Core's `CheckSentences`, `TransactionSentences` and `WriteStatusMapping`), the
same ones the application announces, without its `Change blocked: ` prefix; for the firmware's
error codes, for example: `The firmware refused the change: the variable is write-protected (error 19). Nothing was changed.`,
`The firmware has no room left for the change (error 1470). Nothing was changed. Restart the machine and read the variables again before trying once more.`
(each followed by `Snapshot <folder name>.`). When the write call ends inside this tool instead
of returning a result (an exception, `<type>: <message>`), the sentence says so instead of
naming a firmware error, and the verification read decides the exit code:
`The write call ended unexpectedly (<type>: <message>) and the value read back is the old one. Nothing was changed. Snapshot <folder name>.`
(exit 8), `The write call ended unexpectedly (<type>: <message>) but the change is present. Snapshot <folder name>.`
(exit 9), or
`The write call ended unexpectedly (<type>: <message>) and the change could not be verified: <reason>. Snapshot <folder name>. Use Restore previous value (Alt+P) or check with uefiaccess boot.`
(exit 9).

Every attempt, dry runs and blocked ones included, is one line of
`%ProgramData%\UefiAccess\writes\write-log.jsonl`, and a commit that reached the snapshot also
writes that record as `record.json` in its snapshot folder. The `record` of the document is that
line. It holds no key material, no service tag and no Windows machine ID (the machine block is
the model, the BIOS version, whether the machine is the supported OptiPlex 7020 on BIOS A18,
`confirmed`, and whether it is the enabled unit, `unitConfirmed`; a change is made only when both
are true). The checks `expectedModel` and `expectedUnit` are recorded at stage S2 and enforced at
S11, in that order.

A dry run on the supported unit after a second boot entry was added, with BitLocker on and the
choice given (`uefiaccess write bootorder --order Boot0003,Boot0002 --bitlocker suspend`); the
examples use `Boot0003` for that second entry, `Windows Boot Manager 2`:

```
Field       Value
Operation   Reorder
Variable    BootOrder (8be4df61-93ca-11d2-aa0d-00e098032b8c)
Requested   Boot0003,Boot0002
Transaction 20260930-141502-reorder
Dry run     true
Confirmed   false
Outcome     Prepared
Blocked by  (none)
Exit code   0
BitLocker   BitLocker protects drive C: with the TPM and checks the boot path (PCR 0, 2, 4, 11; TPM 1.2).

Before:
Position Entry    Description            Flags
1        Boot0002 Windows Boot Manager   active, category Boot
2        Boot0003 Windows Boot Manager 2 active, category Boot

After:
Position Entry    Description            Flags
1        Boot0003 Windows Boot Manager 2 active, category Boot
2        Boot0002 Windows Boot Manager   active, category Boot

Changes:
Position Before   After    Description            Flags
1        Boot0002 Boot0003 Windows Boot Manager 2 active, category Boot
2        Boot0003 Boot0002 Windows Boot Manager   active, category Boot

Checks:
Check              Result  Reason
preconditions      passed
expectedModel      passed
expectedUnit       passed
enumeration        passed
whitelist          passed
attributes         passed
authenticatedWrite passed
format             passed
currentListIntact  passed
entriesExist       passed
permutation        passed
noChange           passed
bootCurrent        passed
otherWindowsPath   passed
removableAhead     passed
secureBoot         warning Secure Boot: on, user mode. This application never changes it.
baseline           warning BootOrder differs from the baseline of 20260923-111550; the firmware or Windows changed it since.
bitLocker          warning BitLocker protects drive C: with the TPM and checks the boot path (PCR 0, 2, 4, 11; TPM 1.2).
snapshotRoot       passed
writeBudget        passed

Every check passed; nothing has been written.
```

and on standard error:

```
Warning: Secure Boot: on, user mode. This application never changes it.
Warning: BootOrder differs from the baseline of 20260923-111550; the firmware or Windows changed it since.
BitLocker state: BitLocker protects drive C: with the TPM and checks the boot path (PCR 0, 2, 4, 11; TPM 1.2).
Risk: Microsoft lists a change of the firmware boot order on a TPM 1.2 machine as a cause of BitLocker recovery.
Recovery key: If the next start asks for the recovery key, you need the 48-digit recovery password saved when BitLocker was turned on. Have it at hand before you confirm. This application never reads or shows it.
Dry run: nothing was written.
```

A committed run prints the same tables with `Dry run false`, `Confirmed true` and the outcome,
followed by the rows `Snapshot folder`, `Write call made`, `Write call result`, `NTSTATUS` and
`Verification read`, and a `Read back:` table of the entries the verification read found.

With `--json` the document holds the plan (`before`, `proposed`, `diff`), every check that ran in
stage order, the warnings, the BitLocker facts, what the call and the verification read returned,
the sentence, and the record. `before`, `proposed` and `diff` are `null` and `[]` when the dry run
stopped before a plan was made; `after` is the value the verification read found, `null` without
a write call. In the examples below the small objects of `checks`, `entries` and `diff` and the
short lists are written on one line each to keep the page short; the tool writes every property
on a line of its own.

The dry run above, with `--json`:

<!-- schema: WriteDocument -->
```json
{
  "command": "write",
  "operation": "Reorder",
  "transactionId": "20260930-141502-reorder",
  "ok": true,
  "exitCode": 0,
  "dryRun": true,
  "confirmed": false,
  "variable": {
    "key": "8be4df61-93ca-11d2-aa0d-00e098032b8c:BootOrder",
    "name": "BootOrder",
    "guid": "8be4df61-93ca-11d2-aa0d-00e098032b8c"
  },
  "requested": "Boot0003,Boot0002",
  "before": {
    "present": true,
    "attributes": 7,
    "flags": "NV BS RT",
    "size": 4,
    "sha256": "7d6e4762a22170651b49bef86a5bb9d5de987f5d5f3e546901291fd24c411d39",
    "hex": "02000300",
    "entries": [
      { "position": 1, "name": "Boot0002", "description": "Windows Boot Manager", "active": true, "hidden": false, "category": "Boot" },
      { "position": 2, "name": "Boot0003", "description": "Windows Boot Manager 2", "active": true, "hidden": false, "category": "Boot" }
    ]
  },
  "proposed": {
    "present": true,
    "attributes": 7,
    "flags": "NV BS RT",
    "size": 4,
    "sha256": "470002f447314d088f3b84b7d1291b250f758237eb766cda6e495c7741512041",
    "hex": "03000200",
    "entries": [
      { "position": 1, "name": "Boot0003", "description": "Windows Boot Manager 2", "active": true, "hidden": false, "category": "Boot" },
      { "position": 2, "name": "Boot0002", "description": "Windows Boot Manager", "active": true, "hidden": false, "category": "Boot" }
    ]
  },
  "after": null,
  "diff": [
    { "position": 1, "before": "Boot0002", "after": "Boot0003", "description": "Windows Boot Manager 2", "flags": "active, category Boot", "changed": true },
    { "position": 2, "before": "Boot0003", "after": "Boot0002", "description": "Windows Boot Manager", "flags": "active, category Boot", "changed": true }
  ],
  "checks": [
    { "name": "preconditions", "passed": true, "reason": "", "isWarning": false },
    { "name": "expectedModel", "passed": true, "reason": "", "isWarning": false },
    { "name": "expectedUnit", "passed": true, "reason": "", "isWarning": false },
    { "name": "enumeration", "passed": true, "reason": "", "isWarning": false },
    { "name": "whitelist", "passed": true, "reason": "", "isWarning": false },
    { "name": "attributes", "passed": true, "reason": "", "isWarning": false },
    { "name": "authenticatedWrite", "passed": true, "reason": "", "isWarning": false },
    { "name": "format", "passed": true, "reason": "", "isWarning": false },
    { "name": "currentListIntact", "passed": true, "reason": "", "isWarning": false },
    { "name": "entriesExist", "passed": true, "reason": "", "isWarning": false },
    { "name": "permutation", "passed": true, "reason": "", "isWarning": false },
    { "name": "noChange", "passed": true, "reason": "", "isWarning": false },
    { "name": "bootCurrent", "passed": true, "reason": "", "isWarning": false },
    { "name": "otherWindowsPath", "passed": true, "reason": "", "isWarning": false },
    { "name": "removableAhead", "passed": true, "reason": "", "isWarning": false },
    { "name": "secureBoot", "passed": true, "reason": "Secure Boot: on, user mode. This application never changes it.", "isWarning": true },
    { "name": "baseline", "passed": true, "reason": "BootOrder differs from the baseline of 20260923-111550; the firmware or Windows changed it since.", "isWarning": true },
    { "name": "bitLocker", "passed": true, "reason": "BitLocker protects drive C: with the TPM and checks the boot path (PCR 0, 2, 4, 11; TPM 1.2).", "isWarning": true },
    { "name": "snapshotRoot", "passed": true, "reason": "", "isWarning": false },
    { "name": "writeBudget", "passed": true, "reason": "", "isWarning": false }
  ],
  "blockedBy": null,
  "warnings": ["Secure Boot: on, user mode. This application never changes it.", "BootOrder differs from the baseline of 20260923-111550; the firmware or Windows changed it since."],
  "writability": null,
  "bitLocker": {
    "state": "On",
    "sentence": "BitLocker protects drive C: with the TPM and checks the boot path (PCR 0, 2, 4, 11; TPM 1.2).",
    "risk": "Microsoft lists a change of the firmware boot order on a TPM 1.2 machine as a cause of BitLocker recovery.",
    "recoveryKey": "If the next start asks for the recovery key, you need the 48-digit recovery password saved when BitLocker was turned on. Have it at hand before you confirm. This application never reads or shows it.",
    "suspendOffered": true,
    "choice": "SuspendForOneRestart",
    "suspendedForOneRestart": false
  },
  "secureBoot": {
    "enabled": true,
    "setupMode": false,
    "sourcesAgree": true
  },
  "snapshotFolder": null,
  "apiCalled": false,
  "api": null,
  "verification": null,
  "sentence": "Every check passed; nothing has been written.",
  "record": {
    "schema": "uefiaccess-write-record/1",
    "timestampUtc": "2026-09-30T12:15:02Z",
    "transactionId": "20260930-141502-reorder",
    "toolVersion": "0.1.0+0123456789abcdef0123456789abcdef01234567",
    "host": "cli",
    "operation": "Reorder",
    "isRestore": false,
    "restoredFrom": null,
    "variable": {
      "key": "8be4df61-93ca-11d2-aa0d-00e098032b8c:BootOrder",
      "name": "BootOrder",
      "guid": "8be4df61-93ca-11d2-aa0d-00e098032b8c"
    },
    "attributesBefore": 7,
    "attributesRequested": 7,
    "attributeNames": "NV BS RT",
    "sizeBefore": 4,
    "sizeRequested": 4,
    "beforeSha256": "7d6e4762a22170651b49bef86a5bb9d5de987f5d5f3e546901291fd24c411d39",
    "proposedSha256": "470002f447314d088f3b84b7d1291b250f758237eb766cda6e495c7741512041",
    "afterSha256": null,
    "before": {
      "present": true,
      "hex": "02000300",
      "entries": ["Boot0002", "Boot0003"]
    },
    "after": {
      "present": true,
      "hex": "03000200",
      "entries": ["Boot0003", "Boot0002"]
    },
    "checks": [
      { "name": "preconditions", "passed": true, "reason": "", "isWarning": false },
      { "name": "expectedModel", "passed": true, "reason": "", "isWarning": false },
      { "name": "expectedUnit", "passed": true, "reason": "", "isWarning": false },
      { "name": "enumeration", "passed": true, "reason": "", "isWarning": false },
      { "name": "whitelist", "passed": true, "reason": "", "isWarning": false },
      { "name": "attributes", "passed": true, "reason": "", "isWarning": false },
      { "name": "authenticatedWrite", "passed": true, "reason": "", "isWarning": false },
      { "name": "format", "passed": true, "reason": "", "isWarning": false },
      { "name": "currentListIntact", "passed": true, "reason": "", "isWarning": false },
      { "name": "entriesExist", "passed": true, "reason": "", "isWarning": false },
      { "name": "permutation", "passed": true, "reason": "", "isWarning": false },
      { "name": "noChange", "passed": true, "reason": "", "isWarning": false },
      { "name": "bootCurrent", "passed": true, "reason": "", "isWarning": false },
      { "name": "otherWindowsPath", "passed": true, "reason": "", "isWarning": false },
      { "name": "removableAhead", "passed": true, "reason": "", "isWarning": false },
      { "name": "secureBoot", "passed": true, "reason": "Secure Boot: on, user mode. This application never changes it.", "isWarning": true },
      { "name": "baseline", "passed": true, "reason": "BootOrder differs from the baseline of 20260923-111550; the firmware or Windows changed it since.", "isWarning": true },
      { "name": "bitLocker", "passed": true, "reason": "BitLocker protects drive C: with the TPM and checks the boot path (PCR 0, 2, 4, 11; TPM 1.2).", "isWarning": true },
      { "name": "snapshotRoot", "passed": true, "reason": "", "isWarning": false },
      { "name": "writeBudget", "passed": true, "reason": "", "isWarning": false }
    ],
    "blockedBy": null,
    "confirmed": false,
    "bitLockerChoice": "SuspendForOneRestart",
    "bitLocker": {
      "state": "On",
      "protectionStatus": 1,
      "conversionStatus": 1,
      "protectorTypes": ["TPM", "Numerical password"],
      "pcrProfileText": "PCR 0,2,4,11 (legacy)",
      "suspendOffered": true,
      "suspendChosen": true,
      "suspendResult": null,
      "protectionStatusAfterSuspend": null
    },
    "writesThisStart": 0,
    "bootId": 57,
    "deviceNodeTypes": {
      "Boot0003": ["HD", "File"]
    },
    "secureBoot": {
      "enabled": true,
      "setupMode": false,
      "sourcesAgree": true
    },
    "machine": {
      "model": "OptiPlex 7020",
      "biosVersion": "A18",
      "confirmed": true,
      "unitConfirmed": true
    },
    "apiCalled": false,
    "api": null,
    "verification": null,
    "snapshotFolder": null,
    "outcome": "Prepared",
    "exitCode": 0,
    "sentence": "Every check passed; nothing has been written."
  }
}
```

Blocked: BitLocker on and no `--bitlocker` (`uefiaccess write bootnext Boot0002 --json`, the rig's
case FC-15), exit 7:

<!-- schema: WriteDocument -->
```json
{
  "command": "write",
  "operation": "SetBootNext",
  "transactionId": "20260930-141733-bootnext",
  "ok": false,
  "exitCode": 7,
  "dryRun": true,
  "confirmed": false,
  "variable": {
    "key": "8be4df61-93ca-11d2-aa0d-00e098032b8c:BootNext",
    "name": "BootNext",
    "guid": "8be4df61-93ca-11d2-aa0d-00e098032b8c"
  },
  "requested": "Boot0002",
  "before": null,
  "proposed": null,
  "after": null,
  "diff": [],
  "checks": [
    { "name": "preconditions", "passed": true, "reason": "", "isWarning": false },
    { "name": "expectedModel", "passed": true, "reason": "", "isWarning": false },
    { "name": "expectedUnit", "passed": true, "reason": "", "isWarning": false },
    { "name": "enumeration", "passed": true, "reason": "", "isWarning": false },
    { "name": "whitelist", "passed": true, "reason": "", "isWarning": false },
    { "name": "attributes", "passed": true, "reason": "", "isWarning": false },
    { "name": "authenticatedWrite", "passed": true, "reason": "", "isWarning": false },
    { "name": "format", "passed": true, "reason": "", "isWarning": false },
    { "name": "currentListIntact", "passed": true, "reason": "", "isWarning": false },
    { "name": "entriesExist", "passed": true, "reason": "", "isWarning": false },
    { "name": "bootNextTarget", "passed": true, "reason": "", "isWarning": false },
    { "name": "bootCurrent", "passed": true, "reason": "", "isWarning": false },
    { "name": "otherWindowsPath", "passed": true, "reason": "", "isWarning": false },
    { "name": "removableAhead", "passed": true, "reason": "", "isWarning": false },
    { "name": "secureBoot", "passed": true, "reason": "Secure Boot: on, user mode. This application never changes it.", "isWarning": true },
    { "name": "baseline", "passed": true, "reason": "", "isWarning": false },
    { "name": "bitLocker", "passed": false, "reason": "BitLocker protection is on and no choice was made about it; nothing is written.", "isWarning": false }
  ],
  "blockedBy": "bitLocker",
  "warnings": ["Secure Boot: on, user mode. This application never changes it."],
  "writability": null,
  "bitLocker": {
    "state": "On",
    "sentence": "BitLocker protects drive C: with the TPM and checks the boot path (PCR 0, 2, 4, 11; TPM 1.2).",
    "risk": "The firmware tries Boot0002 once at the next start, then removes BootNext. If it cannot start, the normal list is used in that start and BitLocker may ask for the recovery key.",
    "recoveryKey": "If the next start asks for the recovery key, you need the 48-digit recovery password saved when BitLocker was turned on. Have it at hand before you confirm. This application never reads or shows it.",
    "suspendOffered": true,
    "choice": null,
    "suspendedForOneRestart": false
  },
  "secureBoot": {
    "enabled": true,
    "setupMode": false,
    "sourcesAgree": true
  },
  "snapshotFolder": null,
  "apiCalled": false,
  "api": null,
  "verification": null,
  "sentence": "BitLocker protection is on and no choice was made about it; nothing is written.",
  "record": {
    "schema": "uefiaccess-write-record/1",
    "timestampUtc": "2026-09-30T12:17:33Z",
    "transactionId": "20260930-141733-bootnext",
    "toolVersion": "0.1.0+0123456789abcdef0123456789abcdef01234567",
    "host": "cli",
    "operation": "SetBootNext",
    "isRestore": false,
    "restoredFrom": null,
    "variable": {
      "key": "8be4df61-93ca-11d2-aa0d-00e098032b8c:BootNext",
      "name": "BootNext",
      "guid": "8be4df61-93ca-11d2-aa0d-00e098032b8c"
    },
    "attributesBefore": null,
    "attributesRequested": 7,
    "attributeNames": null,
    "sizeBefore": 0,
    "sizeRequested": 2,
    "beforeSha256": null,
    "proposedSha256": "99be5efb88ca2013bd8e4eb035fd42d5245468fe9afa70d8ba9c1c419a48c4e8",
    "afterSha256": null,
    "before": {
      "present": false,
      "hex": "",
      "entries": []
    },
    "after": {
      "present": true,
      "hex": "0200",
      "entries": ["Boot0002"]
    },
    "checks": [
      { "name": "preconditions", "passed": true, "reason": "", "isWarning": false },
      { "name": "expectedModel", "passed": true, "reason": "", "isWarning": false },
      { "name": "expectedUnit", "passed": true, "reason": "", "isWarning": false },
      { "name": "enumeration", "passed": true, "reason": "", "isWarning": false },
      { "name": "whitelist", "passed": true, "reason": "", "isWarning": false },
      { "name": "attributes", "passed": true, "reason": "", "isWarning": false },
      { "name": "authenticatedWrite", "passed": true, "reason": "", "isWarning": false },
      { "name": "format", "passed": true, "reason": "", "isWarning": false },
      { "name": "currentListIntact", "passed": true, "reason": "", "isWarning": false },
      { "name": "entriesExist", "passed": true, "reason": "", "isWarning": false },
      { "name": "bootNextTarget", "passed": true, "reason": "", "isWarning": false },
      { "name": "bootCurrent", "passed": true, "reason": "", "isWarning": false },
      { "name": "otherWindowsPath", "passed": true, "reason": "", "isWarning": false },
      { "name": "removableAhead", "passed": true, "reason": "", "isWarning": false },
      { "name": "secureBoot", "passed": true, "reason": "Secure Boot: on, user mode. This application never changes it.", "isWarning": true },
      { "name": "baseline", "passed": true, "reason": "", "isWarning": false },
      { "name": "bitLocker", "passed": false, "reason": "BitLocker protection is on and no choice was made about it; nothing is written.", "isWarning": false }
    ],
    "blockedBy": "bitLocker",
    "confirmed": false,
    "bitLockerChoice": "NotNeeded",
    "bitLocker": {
      "state": "On",
      "protectionStatus": 1,
      "conversionStatus": 1,
      "protectorTypes": ["TPM", "Numerical password"],
      "pcrProfileText": "PCR 0,2,4,11 (legacy)",
      "suspendOffered": true,
      "suspendChosen": false,
      "suspendResult": null,
      "protectionStatusAfterSuspend": null
    },
    "writesThisStart": null,
    "bootId": 57,
    "deviceNodeTypes": {
      "Boot0002": ["HD", "File"]
    },
    "secureBoot": {
      "enabled": true,
      "setupMode": false,
      "sourcesAgree": true
    },
    "machine": {
      "model": "OptiPlex 7020",
      "biosVersion": "A18",
      "confirmed": true,
      "unitConfirmed": true
    },
    "apiCalled": false,
    "api": null,
    "verification": null,
    "snapshotFolder": null,
    "outcome": "Blocked",
    "exitCode": 7,
    "sentence": "BitLocker protection is on and no choice was made about it; nothing is written."
  }
}
```

Verified: `uefiaccess write bootorder --order Boot0003,Boot0002 --confirm --bitlocker suspend --json`,
exit 0:

<!-- schema: WriteDocument -->
```json
{
  "command": "write",
  "operation": "Reorder",
  "transactionId": "20260930-141502-reorder",
  "ok": true,
  "exitCode": 0,
  "dryRun": false,
  "confirmed": true,
  "variable": {
    "key": "8be4df61-93ca-11d2-aa0d-00e098032b8c:BootOrder",
    "name": "BootOrder",
    "guid": "8be4df61-93ca-11d2-aa0d-00e098032b8c"
  },
  "requested": "Boot0003,Boot0002",
  "before": {
    "present": true,
    "attributes": 7,
    "flags": "NV BS RT",
    "size": 4,
    "sha256": "7d6e4762a22170651b49bef86a5bb9d5de987f5d5f3e546901291fd24c411d39",
    "hex": "02000300",
    "entries": [
      { "position": 1, "name": "Boot0002", "description": "Windows Boot Manager", "active": true, "hidden": false, "category": "Boot" },
      { "position": 2, "name": "Boot0003", "description": "Windows Boot Manager 2", "active": true, "hidden": false, "category": "Boot" }
    ]
  },
  "proposed": {
    "present": true,
    "attributes": 7,
    "flags": "NV BS RT",
    "size": 4,
    "sha256": "470002f447314d088f3b84b7d1291b250f758237eb766cda6e495c7741512041",
    "hex": "03000200",
    "entries": [
      { "position": 1, "name": "Boot0003", "description": "Windows Boot Manager 2", "active": true, "hidden": false, "category": "Boot" },
      { "position": 2, "name": "Boot0002", "description": "Windows Boot Manager", "active": true, "hidden": false, "category": "Boot" }
    ]
  },
  "after": {
    "present": true,
    "attributes": 7,
    "flags": "NV BS RT",
    "size": 4,
    "sha256": "470002f447314d088f3b84b7d1291b250f758237eb766cda6e495c7741512041",
    "hex": "03000200",
    "entries": [
      { "position": 1, "name": "Boot0003", "description": "Windows Boot Manager 2", "active": true, "hidden": false, "category": "Boot" },
      { "position": 2, "name": "Boot0002", "description": "Windows Boot Manager", "active": true, "hidden": false, "category": "Boot" }
    ]
  },
  "diff": [
    { "position": 1, "before": "Boot0002", "after": "Boot0003", "description": "Windows Boot Manager 2", "flags": "active, category Boot", "changed": true },
    { "position": 2, "before": "Boot0003", "after": "Boot0002", "description": "Windows Boot Manager", "flags": "active, category Boot", "changed": true }
  ],
  "checks": [
    { "name": "preconditions", "passed": true, "reason": "", "isWarning": false },
    { "name": "expectedModel", "passed": true, "reason": "", "isWarning": false },
    { "name": "expectedUnit", "passed": true, "reason": "", "isWarning": false },
    { "name": "enumeration", "passed": true, "reason": "", "isWarning": false },
    { "name": "whitelist", "passed": true, "reason": "", "isWarning": false },
    { "name": "attributes", "passed": true, "reason": "", "isWarning": false },
    { "name": "authenticatedWrite", "passed": true, "reason": "", "isWarning": false },
    { "name": "format", "passed": true, "reason": "", "isWarning": false },
    { "name": "currentListIntact", "passed": true, "reason": "", "isWarning": false },
    { "name": "entriesExist", "passed": true, "reason": "", "isWarning": false },
    { "name": "permutation", "passed": true, "reason": "", "isWarning": false },
    { "name": "noChange", "passed": true, "reason": "", "isWarning": false },
    { "name": "bootCurrent", "passed": true, "reason": "", "isWarning": false },
    { "name": "otherWindowsPath", "passed": true, "reason": "", "isWarning": false },
    { "name": "removableAhead", "passed": true, "reason": "", "isWarning": false },
    { "name": "secureBoot", "passed": true, "reason": "Secure Boot: on, user mode. This application never changes it.", "isWarning": true },
    { "name": "baseline", "passed": true, "reason": "BootOrder differs from the baseline of 20260923-111550; the firmware or Windows changed it since.", "isWarning": true },
    { "name": "bitLocker", "passed": true, "reason": "BitLocker protects drive C: with the TPM and checks the boot path (PCR 0, 2, 4, 11; TPM 1.2).", "isWarning": true },
    { "name": "snapshotRoot", "passed": true, "reason": "", "isWarning": false },
    { "name": "writeBudget", "passed": true, "reason": "", "isWarning": false },
    { "name": "confirmation", "passed": true, "reason": "", "isWarning": false },
    { "name": "expectedModel", "passed": true, "reason": "", "isWarning": false },
    { "name": "expectedUnit", "passed": true, "reason": "", "isWarning": false },
    { "name": "writeBudget", "passed": true, "reason": "", "isWarning": false },
    { "name": "snapshot", "passed": true, "reason": "", "isWarning": false },
    { "name": "stale", "passed": true, "reason": "", "isWarning": false },
    { "name": "bitLocker", "passed": true, "reason": "BitLocker protects drive C: with the TPM and checks the boot path (PCR 0, 2, 4, 11; TPM 1.2).", "isWarning": true },
    { "name": "bitLockerSuspend", "passed": true, "reason": "", "isWarning": false },
    { "name": "stale", "passed": true, "reason": "", "isWarning": false }
  ],
  "blockedBy": null,
  "warnings": ["Secure Boot: on, user mode. This application never changes it.", "BootOrder differs from the baseline of 20260923-111550; the firmware or Windows changed it since."],
  "writability": null,
  "bitLocker": {
    "state": "On",
    "sentence": "BitLocker protects drive C: with the TPM and checks the boot path (PCR 0, 2, 4, 11; TPM 1.2).",
    "risk": "Microsoft lists a change of the firmware boot order on a TPM 1.2 machine as a cause of BitLocker recovery.",
    "recoveryKey": "If the next start asks for the recovery key, you need the 48-digit recovery password saved when BitLocker was turned on. Have it at hand before you confirm. This application never reads or shows it.",
    "suspendOffered": true,
    "choice": "SuspendForOneRestart",
    "suspendedForOneRestart": true
  },
  "secureBoot": {
    "enabled": true,
    "setupMode": false,
    "sourcesAgree": true
  },
  "snapshotFolder": "C:\\ProgramData\\UefiAccess\\snapshots\\20260930-141502-reorder",
  "apiCalled": true,
  "api": {
    "succeeded": true,
    "win32Error": 0,
    "win32Name": "",
    "win32Text": "",
    "ntStatus": null,
    "ntStatusText": null,
    "ntStatusSource": "not observed",
    "elapsedMs": 12
  },
  "verification": {
    "readStatus": "Success",
    "matchesAfter": true,
    "matchesBefore": false,
    "sha256": "470002f447314d088f3b84b7d1291b250f758237eb766cda6e495c7741512041",
    "attributes": 7
  },
  "sentence": "Boot order changed to Boot0003 (Windows Boot Manager 2), Boot0002 (Windows Boot Manager). Restart to use the new order. Snapshot 20260930-141502-reorder. BitLocker is suspended for one restart and turns back on by itself at the next start.",
  "record": {
    "schema": "uefiaccess-write-record/1",
    "timestampUtc": "2026-09-30T12:15:09Z",
    "transactionId": "20260930-141502-reorder",
    "toolVersion": "0.1.0+0123456789abcdef0123456789abcdef01234567",
    "host": "cli",
    "operation": "Reorder",
    "isRestore": false,
    "restoredFrom": null,
    "variable": {
      "key": "8be4df61-93ca-11d2-aa0d-00e098032b8c:BootOrder",
      "name": "BootOrder",
      "guid": "8be4df61-93ca-11d2-aa0d-00e098032b8c"
    },
    "attributesBefore": 7,
    "attributesRequested": 7,
    "attributeNames": "NV BS RT",
    "sizeBefore": 4,
    "sizeRequested": 4,
    "beforeSha256": "7d6e4762a22170651b49bef86a5bb9d5de987f5d5f3e546901291fd24c411d39",
    "proposedSha256": "470002f447314d088f3b84b7d1291b250f758237eb766cda6e495c7741512041",
    "afterSha256": "470002f447314d088f3b84b7d1291b250f758237eb766cda6e495c7741512041",
    "before": {
      "present": true,
      "hex": "02000300",
      "entries": ["Boot0002", "Boot0003"]
    },
    "after": {
      "present": true,
      "hex": "03000200",
      "entries": ["Boot0003", "Boot0002"]
    },
    "checks": [
      { "name": "preconditions", "passed": true, "reason": "", "isWarning": false },
      { "name": "expectedModel", "passed": true, "reason": "", "isWarning": false },
      { "name": "expectedUnit", "passed": true, "reason": "", "isWarning": false },
      { "name": "enumeration", "passed": true, "reason": "", "isWarning": false },
      { "name": "whitelist", "passed": true, "reason": "", "isWarning": false },
      { "name": "attributes", "passed": true, "reason": "", "isWarning": false },
      { "name": "authenticatedWrite", "passed": true, "reason": "", "isWarning": false },
      { "name": "format", "passed": true, "reason": "", "isWarning": false },
      { "name": "currentListIntact", "passed": true, "reason": "", "isWarning": false },
      { "name": "entriesExist", "passed": true, "reason": "", "isWarning": false },
      { "name": "permutation", "passed": true, "reason": "", "isWarning": false },
      { "name": "noChange", "passed": true, "reason": "", "isWarning": false },
      { "name": "bootCurrent", "passed": true, "reason": "", "isWarning": false },
      { "name": "otherWindowsPath", "passed": true, "reason": "", "isWarning": false },
      { "name": "removableAhead", "passed": true, "reason": "", "isWarning": false },
      { "name": "secureBoot", "passed": true, "reason": "Secure Boot: on, user mode. This application never changes it.", "isWarning": true },
      { "name": "baseline", "passed": true, "reason": "BootOrder differs from the baseline of 20260923-111550; the firmware or Windows changed it since.", "isWarning": true },
      { "name": "bitLocker", "passed": true, "reason": "BitLocker protects drive C: with the TPM and checks the boot path (PCR 0, 2, 4, 11; TPM 1.2).", "isWarning": true },
      { "name": "snapshotRoot", "passed": true, "reason": "", "isWarning": false },
      { "name": "writeBudget", "passed": true, "reason": "", "isWarning": false },
      { "name": "confirmation", "passed": true, "reason": "", "isWarning": false },
      { "name": "expectedModel", "passed": true, "reason": "", "isWarning": false },
      { "name": "expectedUnit", "passed": true, "reason": "", "isWarning": false },
      { "name": "writeBudget", "passed": true, "reason": "", "isWarning": false },
      { "name": "snapshot", "passed": true, "reason": "", "isWarning": false },
      { "name": "stale", "passed": true, "reason": "", "isWarning": false },
      { "name": "bitLocker", "passed": true, "reason": "BitLocker protects drive C: with the TPM and checks the boot path (PCR 0, 2, 4, 11; TPM 1.2).", "isWarning": true },
      { "name": "bitLockerSuspend", "passed": true, "reason": "", "isWarning": false },
      { "name": "stale", "passed": true, "reason": "", "isWarning": false }
    ],
    "blockedBy": null,
    "confirmed": true,
    "bitLockerChoice": "SuspendForOneRestart",
    "bitLocker": {
      "state": "On",
      "protectionStatus": 1,
      "conversionStatus": 1,
      "protectorTypes": ["TPM", "Numerical password"],
      "pcrProfileText": "PCR 0,2,4,11 (legacy)",
      "suspendOffered": true,
      "suspendChosen": true,
      "suspendResult": "0",
      "protectionStatusAfterSuspend": 0
    },
    "writesThisStart": 0,
    "bootId": 57,
    "deviceNodeTypes": {
      "Boot0003": ["HD", "File"]
    },
    "secureBoot": {
      "enabled": true,
      "setupMode": false,
      "sourcesAgree": true
    },
    "machine": {
      "model": "OptiPlex 7020",
      "biosVersion": "A18",
      "confirmed": true,
      "unitConfirmed": true
    },
    "apiCalled": true,
    "api": {
      "succeeded": true,
      "win32Error": 0,
      "win32Name": "",
      "win32Text": "",
      "ntStatus": null,
      "ntStatusText": null,
      "ntStatusSource": "not observed",
      "elapsedMs": 12
    },
    "verification": {
      "readStatus": "Success",
      "matchesAfter": true,
      "matchesBefore": false,
      "sha256": "470002f447314d088f3b84b7d1291b250f758237eb766cda6e495c7741512041",
      "attributes": 7
    },
    "snapshotFolder": "C:\\ProgramData\\UefiAccess\\snapshots\\20260930-141502-reorder",
    "outcome": "Verified",
    "exitCode": 0,
    "sentence": "Boot order changed to Boot0003 (Windows Boot Manager 2), Boot0002 (Windows Boot Manager). Restart to use the new order. Snapshot 20260930-141502-reorder. BitLocker is suspended for one restart and turns back on by itself at the next start."
  }
}
```

The firmware refused the call (error 19, `--bitlocker continue`), exit 8; the value read back is
the old one:

<!-- schema: WriteDocument -->
```json
{
  "command": "write",
  "operation": "Reorder",
  "transactionId": "20260930-142010-reorder",
  "ok": false,
  "exitCode": 8,
  "dryRun": false,
  "confirmed": true,
  "variable": {
    "key": "8be4df61-93ca-11d2-aa0d-00e098032b8c:BootOrder",
    "name": "BootOrder",
    "guid": "8be4df61-93ca-11d2-aa0d-00e098032b8c"
  },
  "requested": "Boot0003,Boot0002",
  "before": {
    "present": true,
    "attributes": 7,
    "flags": "NV BS RT",
    "size": 4,
    "sha256": "7d6e4762a22170651b49bef86a5bb9d5de987f5d5f3e546901291fd24c411d39",
    "hex": "02000300",
    "entries": [
      { "position": 1, "name": "Boot0002", "description": "Windows Boot Manager", "active": true, "hidden": false, "category": "Boot" },
      { "position": 2, "name": "Boot0003", "description": "Windows Boot Manager 2", "active": true, "hidden": false, "category": "Boot" }
    ]
  },
  "proposed": {
    "present": true,
    "attributes": 7,
    "flags": "NV BS RT",
    "size": 4,
    "sha256": "470002f447314d088f3b84b7d1291b250f758237eb766cda6e495c7741512041",
    "hex": "03000200",
    "entries": [
      { "position": 1, "name": "Boot0003", "description": "Windows Boot Manager 2", "active": true, "hidden": false, "category": "Boot" },
      { "position": 2, "name": "Boot0002", "description": "Windows Boot Manager", "active": true, "hidden": false, "category": "Boot" }
    ]
  },
  "after": {
    "present": true,
    "attributes": 7,
    "flags": "NV BS RT",
    "size": 4,
    "sha256": "7d6e4762a22170651b49bef86a5bb9d5de987f5d5f3e546901291fd24c411d39",
    "hex": "02000300",
    "entries": [
      { "position": 1, "name": "Boot0002", "description": "Windows Boot Manager", "active": true, "hidden": false, "category": "Boot" },
      { "position": 2, "name": "Boot0003", "description": "Windows Boot Manager 2", "active": true, "hidden": false, "category": "Boot" }
    ]
  },
  "diff": [
    { "position": 1, "before": "Boot0002", "after": "Boot0003", "description": "Windows Boot Manager 2", "flags": "active, category Boot", "changed": true },
    { "position": 2, "before": "Boot0003", "after": "Boot0002", "description": "Windows Boot Manager", "flags": "active, category Boot", "changed": true }
  ],
  "checks": [
    { "name": "preconditions", "passed": true, "reason": "", "isWarning": false },
    { "name": "expectedModel", "passed": true, "reason": "", "isWarning": false },
    { "name": "expectedUnit", "passed": true, "reason": "", "isWarning": false },
    { "name": "enumeration", "passed": true, "reason": "", "isWarning": false },
    { "name": "whitelist", "passed": true, "reason": "", "isWarning": false },
    { "name": "attributes", "passed": true, "reason": "", "isWarning": false },
    { "name": "authenticatedWrite", "passed": true, "reason": "", "isWarning": false },
    { "name": "format", "passed": true, "reason": "", "isWarning": false },
    { "name": "currentListIntact", "passed": true, "reason": "", "isWarning": false },
    { "name": "entriesExist", "passed": true, "reason": "", "isWarning": false },
    { "name": "permutation", "passed": true, "reason": "", "isWarning": false },
    { "name": "noChange", "passed": true, "reason": "", "isWarning": false },
    { "name": "bootCurrent", "passed": true, "reason": "", "isWarning": false },
    { "name": "otherWindowsPath", "passed": true, "reason": "", "isWarning": false },
    { "name": "removableAhead", "passed": true, "reason": "", "isWarning": false },
    { "name": "secureBoot", "passed": true, "reason": "Secure Boot: on, user mode. This application never changes it.", "isWarning": true },
    { "name": "baseline", "passed": true, "reason": "BootOrder differs from the baseline of 20260923-111550; the firmware or Windows changed it since.", "isWarning": true },
    { "name": "bitLocker", "passed": true, "reason": "BitLocker protects drive C: with the TPM and checks the boot path (PCR 0, 2, 4, 11; TPM 1.2).", "isWarning": true },
    { "name": "snapshotRoot", "passed": true, "reason": "", "isWarning": false },
    { "name": "writeBudget", "passed": true, "reason": "", "isWarning": false },
    { "name": "confirmation", "passed": true, "reason": "", "isWarning": false },
    { "name": "expectedModel", "passed": true, "reason": "", "isWarning": false },
    { "name": "expectedUnit", "passed": true, "reason": "", "isWarning": false },
    { "name": "writeBudget", "passed": true, "reason": "", "isWarning": false },
    { "name": "snapshot", "passed": true, "reason": "", "isWarning": false },
    { "name": "stale", "passed": true, "reason": "", "isWarning": false },
    { "name": "bitLocker", "passed": true, "reason": "BitLocker protects drive C: with the TPM and checks the boot path (PCR 0, 2, 4, 11; TPM 1.2).", "isWarning": true },
    { "name": "stale", "passed": true, "reason": "", "isWarning": false }
  ],
  "blockedBy": null,
  "warnings": ["Secure Boot: on, user mode. This application never changes it.", "BootOrder differs from the baseline of 20260923-111550; the firmware or Windows changed it since."],
  "writability": null,
  "bitLocker": {
    "state": "On",
    "sentence": "BitLocker protects drive C: with the TPM and checks the boot path (PCR 0, 2, 4, 11; TPM 1.2).",
    "risk": "Microsoft lists a change of the firmware boot order on a TPM 1.2 machine as a cause of BitLocker recovery.",
    "recoveryKey": "If the next start asks for the recovery key, you need the 48-digit recovery password saved when BitLocker was turned on. Have it at hand before you confirm. This application never reads or shows it.",
    "suspendOffered": true,
    "choice": "KeepProtection",
    "suspendedForOneRestart": false
  },
  "secureBoot": {
    "enabled": true,
    "setupMode": false,
    "sourcesAgree": true
  },
  "snapshotFolder": "C:\\ProgramData\\UefiAccess\\snapshots\\20260930-142010-reorder",
  "apiCalled": true,
  "api": {
    "succeeded": false,
    "win32Error": 19,
    "win32Name": "ERROR_WRITE_PROTECT",
    "win32Text": "the media is write protected",
    "ntStatus": 3221225634,
    "ntStatusText": "STATUS_MEDIA_WRITE_PROTECTED (0xC00000A2): the disk cannot be written to because it is write-protected",
    "ntStatusSource": "RtlGetLastNtStatus",
    "elapsedMs": 9
  },
  "verification": {
    "readStatus": "Success",
    "matchesAfter": false,
    "matchesBefore": true,
    "sha256": "7d6e4762a22170651b49bef86a5bb9d5de987f5d5f3e546901291fd24c411d39",
    "attributes": 7
  },
  "sentence": "The firmware refused the change: the variable is write-protected (error 19). Nothing was changed. Snapshot 20260930-142010-reorder.",
  "record": {
    "schema": "uefiaccess-write-record/1",
    "timestampUtc": "2026-09-30T12:20:10Z",
    "transactionId": "20260930-142010-reorder",
    "toolVersion": "0.1.0+0123456789abcdef0123456789abcdef01234567",
    "host": "cli",
    "operation": "Reorder",
    "isRestore": false,
    "restoredFrom": null,
    "variable": {
      "key": "8be4df61-93ca-11d2-aa0d-00e098032b8c:BootOrder",
      "name": "BootOrder",
      "guid": "8be4df61-93ca-11d2-aa0d-00e098032b8c"
    },
    "attributesBefore": 7,
    "attributesRequested": 7,
    "attributeNames": "NV BS RT",
    "sizeBefore": 4,
    "sizeRequested": 4,
    "beforeSha256": "7d6e4762a22170651b49bef86a5bb9d5de987f5d5f3e546901291fd24c411d39",
    "proposedSha256": "470002f447314d088f3b84b7d1291b250f758237eb766cda6e495c7741512041",
    "afterSha256": "7d6e4762a22170651b49bef86a5bb9d5de987f5d5f3e546901291fd24c411d39",
    "before": {
      "present": true,
      "hex": "02000300",
      "entries": ["Boot0002", "Boot0003"]
    },
    "after": {
      "present": true,
      "hex": "03000200",
      "entries": ["Boot0003", "Boot0002"]
    },
    "checks": [
      { "name": "preconditions", "passed": true, "reason": "", "isWarning": false },
      { "name": "expectedModel", "passed": true, "reason": "", "isWarning": false },
      { "name": "expectedUnit", "passed": true, "reason": "", "isWarning": false },
      { "name": "enumeration", "passed": true, "reason": "", "isWarning": false },
      { "name": "whitelist", "passed": true, "reason": "", "isWarning": false },
      { "name": "attributes", "passed": true, "reason": "", "isWarning": false },
      { "name": "authenticatedWrite", "passed": true, "reason": "", "isWarning": false },
      { "name": "format", "passed": true, "reason": "", "isWarning": false },
      { "name": "currentListIntact", "passed": true, "reason": "", "isWarning": false },
      { "name": "entriesExist", "passed": true, "reason": "", "isWarning": false },
      { "name": "permutation", "passed": true, "reason": "", "isWarning": false },
      { "name": "noChange", "passed": true, "reason": "", "isWarning": false },
      { "name": "bootCurrent", "passed": true, "reason": "", "isWarning": false },
      { "name": "otherWindowsPath", "passed": true, "reason": "", "isWarning": false },
      { "name": "removableAhead", "passed": true, "reason": "", "isWarning": false },
      { "name": "secureBoot", "passed": true, "reason": "Secure Boot: on, user mode. This application never changes it.", "isWarning": true },
      { "name": "baseline", "passed": true, "reason": "BootOrder differs from the baseline of 20260923-111550; the firmware or Windows changed it since.", "isWarning": true },
      { "name": "bitLocker", "passed": true, "reason": "BitLocker protects drive C: with the TPM and checks the boot path (PCR 0, 2, 4, 11; TPM 1.2).", "isWarning": true },
      { "name": "snapshotRoot", "passed": true, "reason": "", "isWarning": false },
      { "name": "writeBudget", "passed": true, "reason": "", "isWarning": false },
      { "name": "confirmation", "passed": true, "reason": "", "isWarning": false },
      { "name": "expectedModel", "passed": true, "reason": "", "isWarning": false },
      { "name": "expectedUnit", "passed": true, "reason": "", "isWarning": false },
      { "name": "writeBudget", "passed": true, "reason": "", "isWarning": false },
      { "name": "snapshot", "passed": true, "reason": "", "isWarning": false },
      { "name": "stale", "passed": true, "reason": "", "isWarning": false },
      { "name": "bitLocker", "passed": true, "reason": "BitLocker protects drive C: with the TPM and checks the boot path (PCR 0, 2, 4, 11; TPM 1.2).", "isWarning": true },
      { "name": "stale", "passed": true, "reason": "", "isWarning": false }
    ],
    "blockedBy": null,
    "confirmed": true,
    "bitLockerChoice": "KeepProtection",
    "bitLocker": {
      "state": "On",
      "protectionStatus": 1,
      "conversionStatus": 1,
      "protectorTypes": ["TPM", "Numerical password"],
      "pcrProfileText": "PCR 0,2,4,11 (legacy)",
      "suspendOffered": true,
      "suspendChosen": false,
      "suspendResult": null,
      "protectionStatusAfterSuspend": null
    },
    "writesThisStart": 1,
    "bootId": 57,
    "deviceNodeTypes": {
      "Boot0003": ["HD", "File"]
    },
    "secureBoot": {
      "enabled": true,
      "setupMode": false,
      "sourcesAgree": true
    },
    "machine": {
      "model": "OptiPlex 7020",
      "biosVersion": "A18",
      "confirmed": true,
      "unitConfirmed": true
    },
    "apiCalled": true,
    "api": {
      "succeeded": false,
      "win32Error": 19,
      "win32Name": "ERROR_WRITE_PROTECT",
      "win32Text": "the media is write protected",
      "ntStatus": 3221225634,
      "ntStatusText": "STATUS_MEDIA_WRITE_PROTECTED (0xC00000A2): the disk cannot be written to because it is write-protected",
      "ntStatusSource": "RtlGetLastNtStatus",
      "elapsedMs": 9
    },
    "verification": {
      "readStatus": "Success",
      "matchesAfter": false,
      "matchesBefore": true,
      "sha256": "7d6e4762a22170651b49bef86a5bb9d5de987f5d5f3e546901291fd24c411d39",
      "attributes": 7
    },
    "snapshotFolder": "C:\\ProgramData\\UefiAccess\\snapshots\\20260930-142010-reorder",
    "outcome": "ApiRefused",
    "exitCode": 8,
    "sentence": "The firmware refused the change: the variable is write-protected (error 19). Nothing was changed. Snapshot 20260930-142010-reorder."
  }
}
```

Unverified: the call succeeded but the value read back is the old one, exit 9:

<!-- schema: WriteDocument -->
```json
{
  "command": "write",
  "operation": "Reorder",
  "transactionId": "20260930-142311-reorder",
  "ok": false,
  "exitCode": 9,
  "dryRun": false,
  "confirmed": true,
  "variable": {
    "key": "8be4df61-93ca-11d2-aa0d-00e098032b8c:BootOrder",
    "name": "BootOrder",
    "guid": "8be4df61-93ca-11d2-aa0d-00e098032b8c"
  },
  "requested": "Boot0003,Boot0002",
  "before": {
    "present": true,
    "attributes": 7,
    "flags": "NV BS RT",
    "size": 4,
    "sha256": "7d6e4762a22170651b49bef86a5bb9d5de987f5d5f3e546901291fd24c411d39",
    "hex": "02000300",
    "entries": [
      { "position": 1, "name": "Boot0002", "description": "Windows Boot Manager", "active": true, "hidden": false, "category": "Boot" },
      { "position": 2, "name": "Boot0003", "description": "Windows Boot Manager 2", "active": true, "hidden": false, "category": "Boot" }
    ]
  },
  "proposed": {
    "present": true,
    "attributes": 7,
    "flags": "NV BS RT",
    "size": 4,
    "sha256": "470002f447314d088f3b84b7d1291b250f758237eb766cda6e495c7741512041",
    "hex": "03000200",
    "entries": [
      { "position": 1, "name": "Boot0003", "description": "Windows Boot Manager 2", "active": true, "hidden": false, "category": "Boot" },
      { "position": 2, "name": "Boot0002", "description": "Windows Boot Manager", "active": true, "hidden": false, "category": "Boot" }
    ]
  },
  "after": {
    "present": true,
    "attributes": 7,
    "flags": "NV BS RT",
    "size": 4,
    "sha256": "7d6e4762a22170651b49bef86a5bb9d5de987f5d5f3e546901291fd24c411d39",
    "hex": "02000300",
    "entries": [
      { "position": 1, "name": "Boot0002", "description": "Windows Boot Manager", "active": true, "hidden": false, "category": "Boot" },
      { "position": 2, "name": "Boot0003", "description": "Windows Boot Manager 2", "active": true, "hidden": false, "category": "Boot" }
    ]
  },
  "diff": [
    { "position": 1, "before": "Boot0002", "after": "Boot0003", "description": "Windows Boot Manager 2", "flags": "active, category Boot", "changed": true },
    { "position": 2, "before": "Boot0003", "after": "Boot0002", "description": "Windows Boot Manager", "flags": "active, category Boot", "changed": true }
  ],
  "checks": [
    { "name": "preconditions", "passed": true, "reason": "", "isWarning": false },
    { "name": "expectedModel", "passed": true, "reason": "", "isWarning": false },
    { "name": "expectedUnit", "passed": true, "reason": "", "isWarning": false },
    { "name": "enumeration", "passed": true, "reason": "", "isWarning": false },
    { "name": "whitelist", "passed": true, "reason": "", "isWarning": false },
    { "name": "attributes", "passed": true, "reason": "", "isWarning": false },
    { "name": "authenticatedWrite", "passed": true, "reason": "", "isWarning": false },
    { "name": "format", "passed": true, "reason": "", "isWarning": false },
    { "name": "currentListIntact", "passed": true, "reason": "", "isWarning": false },
    { "name": "entriesExist", "passed": true, "reason": "", "isWarning": false },
    { "name": "permutation", "passed": true, "reason": "", "isWarning": false },
    { "name": "noChange", "passed": true, "reason": "", "isWarning": false },
    { "name": "bootCurrent", "passed": true, "reason": "", "isWarning": false },
    { "name": "otherWindowsPath", "passed": true, "reason": "", "isWarning": false },
    { "name": "removableAhead", "passed": true, "reason": "", "isWarning": false },
    { "name": "secureBoot", "passed": true, "reason": "Secure Boot: on, user mode. This application never changes it.", "isWarning": true },
    { "name": "baseline", "passed": true, "reason": "BootOrder differs from the baseline of 20260923-111550; the firmware or Windows changed it since.", "isWarning": true },
    { "name": "bitLocker", "passed": true, "reason": "BitLocker protects drive C: with the TPM and checks the boot path (PCR 0, 2, 4, 11; TPM 1.2).", "isWarning": true },
    { "name": "snapshotRoot", "passed": true, "reason": "", "isWarning": false },
    { "name": "writeBudget", "passed": true, "reason": "", "isWarning": false },
    { "name": "confirmation", "passed": true, "reason": "", "isWarning": false },
    { "name": "expectedModel", "passed": true, "reason": "", "isWarning": false },
    { "name": "expectedUnit", "passed": true, "reason": "", "isWarning": false },
    { "name": "writeBudget", "passed": true, "reason": "", "isWarning": false },
    { "name": "snapshot", "passed": true, "reason": "", "isWarning": false },
    { "name": "stale", "passed": true, "reason": "", "isWarning": false },
    { "name": "bitLocker", "passed": true, "reason": "BitLocker protects drive C: with the TPM and checks the boot path (PCR 0, 2, 4, 11; TPM 1.2).", "isWarning": true },
    { "name": "stale", "passed": true, "reason": "", "isWarning": false }
  ],
  "blockedBy": null,
  "warnings": ["Secure Boot: on, user mode. This application never changes it.", "BootOrder differs from the baseline of 20260923-111550; the firmware or Windows changed it since."],
  "writability": null,
  "bitLocker": {
    "state": "On",
    "sentence": "BitLocker protects drive C: with the TPM and checks the boot path (PCR 0, 2, 4, 11; TPM 1.2).",
    "risk": "Microsoft lists a change of the firmware boot order on a TPM 1.2 machine as a cause of BitLocker recovery.",
    "recoveryKey": "If the next start asks for the recovery key, you need the 48-digit recovery password saved when BitLocker was turned on. Have it at hand before you confirm. This application never reads or shows it.",
    "suspendOffered": true,
    "choice": "KeepProtection",
    "suspendedForOneRestart": false
  },
  "secureBoot": {
    "enabled": true,
    "setupMode": false,
    "sourcesAgree": true
  },
  "snapshotFolder": "C:\\ProgramData\\UefiAccess\\snapshots\\20260930-142311-reorder",
  "apiCalled": true,
  "api": {
    "succeeded": true,
    "win32Error": 0,
    "win32Name": "",
    "win32Text": "",
    "ntStatus": null,
    "ntStatusText": null,
    "ntStatusSource": "not observed",
    "elapsedMs": 11
  },
  "verification": {
    "readStatus": "Success",
    "matchesAfter": false,
    "matchesBefore": true,
    "sha256": "7d6e4762a22170651b49bef86a5bb9d5de987f5d5f3e546901291fd24c411d39",
    "attributes": 7
  },
  "sentence": "The change was written but could not be verified: the value read back is not the value written. Snapshot 20260930-142311-reorder. Use Restore previous value (Alt+P) or check with uefiaccess boot.",
  "record": {
    "schema": "uefiaccess-write-record/1",
    "timestampUtc": "2026-09-30T12:23:11Z",
    "transactionId": "20260930-142311-reorder",
    "toolVersion": "0.1.0+0123456789abcdef0123456789abcdef01234567",
    "host": "cli",
    "operation": "Reorder",
    "isRestore": false,
    "restoredFrom": null,
    "variable": {
      "key": "8be4df61-93ca-11d2-aa0d-00e098032b8c:BootOrder",
      "name": "BootOrder",
      "guid": "8be4df61-93ca-11d2-aa0d-00e098032b8c"
    },
    "attributesBefore": 7,
    "attributesRequested": 7,
    "attributeNames": "NV BS RT",
    "sizeBefore": 4,
    "sizeRequested": 4,
    "beforeSha256": "7d6e4762a22170651b49bef86a5bb9d5de987f5d5f3e546901291fd24c411d39",
    "proposedSha256": "470002f447314d088f3b84b7d1291b250f758237eb766cda6e495c7741512041",
    "afterSha256": "7d6e4762a22170651b49bef86a5bb9d5de987f5d5f3e546901291fd24c411d39",
    "before": {
      "present": true,
      "hex": "02000300",
      "entries": ["Boot0002", "Boot0003"]
    },
    "after": {
      "present": true,
      "hex": "03000200",
      "entries": ["Boot0003", "Boot0002"]
    },
    "checks": [
      { "name": "preconditions", "passed": true, "reason": "", "isWarning": false },
      { "name": "expectedModel", "passed": true, "reason": "", "isWarning": false },
      { "name": "expectedUnit", "passed": true, "reason": "", "isWarning": false },
      { "name": "enumeration", "passed": true, "reason": "", "isWarning": false },
      { "name": "whitelist", "passed": true, "reason": "", "isWarning": false },
      { "name": "attributes", "passed": true, "reason": "", "isWarning": false },
      { "name": "authenticatedWrite", "passed": true, "reason": "", "isWarning": false },
      { "name": "format", "passed": true, "reason": "", "isWarning": false },
      { "name": "currentListIntact", "passed": true, "reason": "", "isWarning": false },
      { "name": "entriesExist", "passed": true, "reason": "", "isWarning": false },
      { "name": "permutation", "passed": true, "reason": "", "isWarning": false },
      { "name": "noChange", "passed": true, "reason": "", "isWarning": false },
      { "name": "bootCurrent", "passed": true, "reason": "", "isWarning": false },
      { "name": "otherWindowsPath", "passed": true, "reason": "", "isWarning": false },
      { "name": "removableAhead", "passed": true, "reason": "", "isWarning": false },
      { "name": "secureBoot", "passed": true, "reason": "Secure Boot: on, user mode. This application never changes it.", "isWarning": true },
      { "name": "baseline", "passed": true, "reason": "BootOrder differs from the baseline of 20260923-111550; the firmware or Windows changed it since.", "isWarning": true },
      { "name": "bitLocker", "passed": true, "reason": "BitLocker protects drive C: with the TPM and checks the boot path (PCR 0, 2, 4, 11; TPM 1.2).", "isWarning": true },
      { "name": "snapshotRoot", "passed": true, "reason": "", "isWarning": false },
      { "name": "writeBudget", "passed": true, "reason": "", "isWarning": false },
      { "name": "confirmation", "passed": true, "reason": "", "isWarning": false },
      { "name": "expectedModel", "passed": true, "reason": "", "isWarning": false },
      { "name": "expectedUnit", "passed": true, "reason": "", "isWarning": false },
      { "name": "writeBudget", "passed": true, "reason": "", "isWarning": false },
      { "name": "snapshot", "passed": true, "reason": "", "isWarning": false },
      { "name": "stale", "passed": true, "reason": "", "isWarning": false },
      { "name": "bitLocker", "passed": true, "reason": "BitLocker protects drive C: with the TPM and checks the boot path (PCR 0, 2, 4, 11; TPM 1.2).", "isWarning": true },
      { "name": "stale", "passed": true, "reason": "", "isWarning": false }
    ],
    "blockedBy": null,
    "confirmed": true,
    "bitLockerChoice": "KeepProtection",
    "bitLocker": {
      "state": "On",
      "protectionStatus": 1,
      "conversionStatus": 1,
      "protectorTypes": ["TPM", "Numerical password"],
      "pcrProfileText": "PCR 0,2,4,11 (legacy)",
      "suspendOffered": true,
      "suspendChosen": false,
      "suspendResult": null,
      "protectionStatusAfterSuspend": null
    },
    "writesThisStart": 2,
    "bootId": 57,
    "deviceNodeTypes": {
      "Boot0003": ["HD", "File"]
    },
    "secureBoot": {
      "enabled": true,
      "setupMode": false,
      "sourcesAgree": true
    },
    "machine": {
      "model": "OptiPlex 7020",
      "biosVersion": "A18",
      "confirmed": true,
      "unitConfirmed": true
    },
    "apiCalled": true,
    "api": {
      "succeeded": true,
      "win32Error": 0,
      "win32Name": "",
      "win32Text": "",
      "ntStatus": null,
      "ntStatusText": null,
      "ntStatusSource": "not observed",
      "elapsedMs": 11
    },
    "verification": {
      "readStatus": "Success",
      "matchesAfter": false,
      "matchesBefore": true,
      "sha256": "7d6e4762a22170651b49bef86a5bb9d5de987f5d5f3e546901291fd24c411d39",
      "attributes": 7
    },
    "snapshotFolder": "C:\\ProgramData\\UefiAccess\\snapshots\\20260930-142311-reorder",
    "outcome": "Unverified",
    "exitCode": 9,
    "sentence": "The change was written but could not be verified: the value read back is not the value written. Snapshot 20260930-142311-reorder. Use Restore previous value (Alt+P) or check with uefiaccess boot."
  }
}
```

ChangedDespiteError: the firmware reported an error (here error 5, access denied), but the value
read back is the new one, exit 9; the change is on the firmware, and the snapshot holds the old
value for a restore:

<!-- schema: WriteDocument -->
```json
{
  "command": "write",
  "operation": "Reorder",
  "transactionId": "20260930-142530-reorder",
  "ok": false,
  "exitCode": 9,
  "dryRun": false,
  "confirmed": true,
  "variable": {
    "key": "8be4df61-93ca-11d2-aa0d-00e098032b8c:BootOrder",
    "name": "BootOrder",
    "guid": "8be4df61-93ca-11d2-aa0d-00e098032b8c"
  },
  "requested": "Boot0003,Boot0002",
  "before": {
    "present": true,
    "attributes": 7,
    "flags": "NV BS RT",
    "size": 4,
    "sha256": "7d6e4762a22170651b49bef86a5bb9d5de987f5d5f3e546901291fd24c411d39",
    "hex": "02000300",
    "entries": [
      { "position": 1, "name": "Boot0002", "description": "Windows Boot Manager", "active": true, "hidden": false, "category": "Boot" },
      { "position": 2, "name": "Boot0003", "description": "Windows Boot Manager 2", "active": true, "hidden": false, "category": "Boot" }
    ]
  },
  "proposed": {
    "present": true,
    "attributes": 7,
    "flags": "NV BS RT",
    "size": 4,
    "sha256": "470002f447314d088f3b84b7d1291b250f758237eb766cda6e495c7741512041",
    "hex": "03000200",
    "entries": [
      { "position": 1, "name": "Boot0003", "description": "Windows Boot Manager 2", "active": true, "hidden": false, "category": "Boot" },
      { "position": 2, "name": "Boot0002", "description": "Windows Boot Manager", "active": true, "hidden": false, "category": "Boot" }
    ]
  },
  "after": {
    "present": true,
    "attributes": 7,
    "flags": "NV BS RT",
    "size": 4,
    "sha256": "470002f447314d088f3b84b7d1291b250f758237eb766cda6e495c7741512041",
    "hex": "03000200",
    "entries": [
      { "position": 1, "name": "Boot0003", "description": "Windows Boot Manager 2", "active": true, "hidden": false, "category": "Boot" },
      { "position": 2, "name": "Boot0002", "description": "Windows Boot Manager", "active": true, "hidden": false, "category": "Boot" }
    ]
  },
  "diff": [
    { "position": 1, "before": "Boot0002", "after": "Boot0003", "description": "Windows Boot Manager 2", "flags": "active, category Boot", "changed": true },
    { "position": 2, "before": "Boot0003", "after": "Boot0002", "description": "Windows Boot Manager", "flags": "active, category Boot", "changed": true }
  ],
  "checks": [
    { "name": "preconditions", "passed": true, "reason": "", "isWarning": false },
    { "name": "expectedModel", "passed": true, "reason": "", "isWarning": false },
    { "name": "expectedUnit", "passed": true, "reason": "", "isWarning": false },
    { "name": "enumeration", "passed": true, "reason": "", "isWarning": false },
    { "name": "whitelist", "passed": true, "reason": "", "isWarning": false },
    { "name": "attributes", "passed": true, "reason": "", "isWarning": false },
    { "name": "authenticatedWrite", "passed": true, "reason": "", "isWarning": false },
    { "name": "format", "passed": true, "reason": "", "isWarning": false },
    { "name": "currentListIntact", "passed": true, "reason": "", "isWarning": false },
    { "name": "entriesExist", "passed": true, "reason": "", "isWarning": false },
    { "name": "permutation", "passed": true, "reason": "", "isWarning": false },
    { "name": "noChange", "passed": true, "reason": "", "isWarning": false },
    { "name": "bootCurrent", "passed": true, "reason": "", "isWarning": false },
    { "name": "otherWindowsPath", "passed": true, "reason": "", "isWarning": false },
    { "name": "removableAhead", "passed": true, "reason": "", "isWarning": false },
    { "name": "secureBoot", "passed": true, "reason": "Secure Boot: on, user mode. This application never changes it.", "isWarning": true },
    { "name": "baseline", "passed": true, "reason": "BootOrder differs from the baseline of 20260923-111550; the firmware or Windows changed it since.", "isWarning": true },
    { "name": "bitLocker", "passed": true, "reason": "BitLocker protects drive C: with the TPM and checks the boot path (PCR 0, 2, 4, 11; TPM 1.2).", "isWarning": true },
    { "name": "snapshotRoot", "passed": true, "reason": "", "isWarning": false },
    { "name": "writeBudget", "passed": true, "reason": "", "isWarning": false },
    { "name": "confirmation", "passed": true, "reason": "", "isWarning": false },
    { "name": "expectedModel", "passed": true, "reason": "", "isWarning": false },
    { "name": "expectedUnit", "passed": true, "reason": "", "isWarning": false },
    { "name": "writeBudget", "passed": true, "reason": "", "isWarning": false },
    { "name": "snapshot", "passed": true, "reason": "", "isWarning": false },
    { "name": "stale", "passed": true, "reason": "", "isWarning": false },
    { "name": "bitLocker", "passed": true, "reason": "BitLocker protects drive C: with the TPM and checks the boot path (PCR 0, 2, 4, 11; TPM 1.2).", "isWarning": true },
    { "name": "stale", "passed": true, "reason": "", "isWarning": false }
  ],
  "blockedBy": null,
  "warnings": ["Secure Boot: on, user mode. This application never changes it.", "BootOrder differs from the baseline of 20260923-111550; the firmware or Windows changed it since."],
  "writability": null,
  "bitLocker": {
    "state": "On",
    "sentence": "BitLocker protects drive C: with the TPM and checks the boot path (PCR 0, 2, 4, 11; TPM 1.2).",
    "risk": "Microsoft lists a change of the firmware boot order on a TPM 1.2 machine as a cause of BitLocker recovery.",
    "recoveryKey": "If the next start asks for the recovery key, you need the 48-digit recovery password saved when BitLocker was turned on. Have it at hand before you confirm. This application never reads or shows it.",
    "suspendOffered": true,
    "choice": "KeepProtection",
    "suspendedForOneRestart": false
  },
  "secureBoot": {
    "enabled": true,
    "setupMode": false,
    "sourcesAgree": true
  },
  "snapshotFolder": "C:\\ProgramData\\UefiAccess\\snapshots\\20260930-142530-reorder",
  "apiCalled": true,
  "api": {
    "succeeded": false,
    "win32Error": 5,
    "win32Name": "ERROR_ACCESS_DENIED",
    "win32Text": "access denied",
    "ntStatus": 3221225506,
    "ntStatusText": "STATUS_ACCESS_DENIED (0xC0000022): access denied",
    "ntStatusSource": "RtlGetLastNtStatus",
    "elapsedMs": 10
  },
  "verification": {
    "readStatus": "Success",
    "matchesAfter": true,
    "matchesBefore": false,
    "sha256": "470002f447314d088f3b84b7d1291b250f758237eb766cda6e495c7741512041",
    "attributes": 7
  },
  "sentence": "The firmware reported error 5 but the change is present. Snapshot 20260930-142530-reorder.",
  "record": {
    "schema": "uefiaccess-write-record/1",
    "timestampUtc": "2026-09-30T12:25:30Z",
    "transactionId": "20260930-142530-reorder",
    "toolVersion": "0.1.0+0123456789abcdef0123456789abcdef01234567",
    "host": "cli",
    "operation": "Reorder",
    "isRestore": false,
    "restoredFrom": null,
    "variable": {
      "key": "8be4df61-93ca-11d2-aa0d-00e098032b8c:BootOrder",
      "name": "BootOrder",
      "guid": "8be4df61-93ca-11d2-aa0d-00e098032b8c"
    },
    "attributesBefore": 7,
    "attributesRequested": 7,
    "attributeNames": "NV BS RT",
    "sizeBefore": 4,
    "sizeRequested": 4,
    "beforeSha256": "7d6e4762a22170651b49bef86a5bb9d5de987f5d5f3e546901291fd24c411d39",
    "proposedSha256": "470002f447314d088f3b84b7d1291b250f758237eb766cda6e495c7741512041",
    "afterSha256": "470002f447314d088f3b84b7d1291b250f758237eb766cda6e495c7741512041",
    "before": {
      "present": true,
      "hex": "02000300",
      "entries": ["Boot0002", "Boot0003"]
    },
    "after": {
      "present": true,
      "hex": "03000200",
      "entries": ["Boot0003", "Boot0002"]
    },
    "checks": [
      { "name": "preconditions", "passed": true, "reason": "", "isWarning": false },
      { "name": "expectedModel", "passed": true, "reason": "", "isWarning": false },
      { "name": "expectedUnit", "passed": true, "reason": "", "isWarning": false },
      { "name": "enumeration", "passed": true, "reason": "", "isWarning": false },
      { "name": "whitelist", "passed": true, "reason": "", "isWarning": false },
      { "name": "attributes", "passed": true, "reason": "", "isWarning": false },
      { "name": "authenticatedWrite", "passed": true, "reason": "", "isWarning": false },
      { "name": "format", "passed": true, "reason": "", "isWarning": false },
      { "name": "currentListIntact", "passed": true, "reason": "", "isWarning": false },
      { "name": "entriesExist", "passed": true, "reason": "", "isWarning": false },
      { "name": "permutation", "passed": true, "reason": "", "isWarning": false },
      { "name": "noChange", "passed": true, "reason": "", "isWarning": false },
      { "name": "bootCurrent", "passed": true, "reason": "", "isWarning": false },
      { "name": "otherWindowsPath", "passed": true, "reason": "", "isWarning": false },
      { "name": "removableAhead", "passed": true, "reason": "", "isWarning": false },
      { "name": "secureBoot", "passed": true, "reason": "Secure Boot: on, user mode. This application never changes it.", "isWarning": true },
      { "name": "baseline", "passed": true, "reason": "BootOrder differs from the baseline of 20260923-111550; the firmware or Windows changed it since.", "isWarning": true },
      { "name": "bitLocker", "passed": true, "reason": "BitLocker protects drive C: with the TPM and checks the boot path (PCR 0, 2, 4, 11; TPM 1.2).", "isWarning": true },
      { "name": "snapshotRoot", "passed": true, "reason": "", "isWarning": false },
      { "name": "writeBudget", "passed": true, "reason": "", "isWarning": false },
      { "name": "confirmation", "passed": true, "reason": "", "isWarning": false },
      { "name": "expectedModel", "passed": true, "reason": "", "isWarning": false },
      { "name": "expectedUnit", "passed": true, "reason": "", "isWarning": false },
      { "name": "writeBudget", "passed": true, "reason": "", "isWarning": false },
      { "name": "snapshot", "passed": true, "reason": "", "isWarning": false },
      { "name": "stale", "passed": true, "reason": "", "isWarning": false },
      { "name": "bitLocker", "passed": true, "reason": "BitLocker protects drive C: with the TPM and checks the boot path (PCR 0, 2, 4, 11; TPM 1.2).", "isWarning": true },
      { "name": "stale", "passed": true, "reason": "", "isWarning": false }
    ],
    "blockedBy": null,
    "confirmed": true,
    "bitLockerChoice": "KeepProtection",
    "bitLocker": {
      "state": "On",
      "protectionStatus": 1,
      "conversionStatus": 1,
      "protectorTypes": ["TPM", "Numerical password"],
      "pcrProfileText": "PCR 0,2,4,11 (legacy)",
      "suspendOffered": true,
      "suspendChosen": false,
      "suspendResult": null,
      "protectionStatusAfterSuspend": null
    },
    "writesThisStart": 3,
    "bootId": 57,
    "deviceNodeTypes": {
      "Boot0003": ["HD", "File"]
    },
    "secureBoot": {
      "enabled": true,
      "setupMode": false,
      "sourcesAgree": true
    },
    "machine": {
      "model": "OptiPlex 7020",
      "biosVersion": "A18",
      "confirmed": true,
      "unitConfirmed": true
    },
    "apiCalled": true,
    "api": {
      "succeeded": false,
      "win32Error": 5,
      "win32Name": "ERROR_ACCESS_DENIED",
      "win32Text": "access denied",
      "ntStatus": 3221225506,
      "ntStatusText": "STATUS_ACCESS_DENIED (0xC0000022): access denied",
      "ntStatusSource": "RtlGetLastNtStatus",
      "elapsedMs": 10
    },
    "verification": {
      "readStatus": "Success",
      "matchesAfter": true,
      "matchesBefore": false,
      "sha256": "470002f447314d088f3b84b7d1291b250f758237eb766cda6e495c7741512041",
      "attributes": 7
    },
    "snapshotFolder": "C:\\ProgramData\\UefiAccess\\snapshots\\20260930-142530-reorder",
    "outcome": "ChangedDespiteError",
    "exitCode": 9,
    "sentence": "The firmware reported error 5 but the change is present. Snapshot 20260930-142530-reorder."
  }
}
```

`uefiaccess write check BootOrder --json` on the supported unit, exit 0:

<!-- schema: WriteDocument -->
```json
{
  "command": "write",
  "operation": "Check",
  "transactionId": "20260930-141200-check",
  "ok": true,
  "exitCode": 0,
  "dryRun": true,
  "confirmed": false,
  "variable": {
    "key": "8be4df61-93ca-11d2-aa0d-00e098032b8c:BootOrder",
    "name": "BootOrder",
    "guid": "8be4df61-93ca-11d2-aa0d-00e098032b8c"
  },
  "requested": "BootOrder",
  "before": null,
  "proposed": null,
  "after": null,
  "diff": [],
  "checks": [
    { "name": "preconditions", "passed": true, "reason": "", "isWarning": false },
    { "name": "expectedModel", "passed": true, "reason": "", "isWarning": false },
    { "name": "expectedUnit", "passed": true, "reason": "", "isWarning": false },
    { "name": "enumeration", "passed": true, "reason": "", "isWarning": false },
    { "name": "whitelist", "passed": true, "reason": "", "isWarning": false },
    { "name": "attributes", "passed": true, "reason": "", "isWarning": false },
    { "name": "authenticatedWrite", "passed": true, "reason": "", "isWarning": false },
    { "name": "format", "passed": true, "reason": "", "isWarning": false },
    { "name": "currentListIntact", "passed": true, "reason": "", "isWarning": false },
    { "name": "secureBoot", "passed": true, "reason": "Secure Boot: on, user mode. This application never changes it.", "isWarning": true },
    { "name": "baseline", "passed": true, "reason": "BootOrder differs from the baseline of 20260923-111550; the firmware or Windows changed it since.", "isWarning": true },
    { "name": "bitLocker", "passed": true, "reason": "BitLocker protects drive C: with the TPM and checks the boot path (PCR 0, 2, 4, 11; TPM 1.2).", "isWarning": true },
    { "name": "snapshotRoot", "passed": true, "reason": "", "isWarning": false },
    { "name": "writeBudget", "passed": true, "reason": "", "isWarning": false }
  ],
  "blockedBy": null,
  "warnings": ["Secure Boot: on, user mode. This application never changes it.", "BootOrder differs from the baseline of 20260923-111550; the firmware or Windows changed it since."],
  "writability": {
    "verdict": "ReorderOnly",
    "sentence": "Changeable: the order of its entries can be changed from this application (Move up, Move down, Review change). Nothing else about it can be changed.",
    "changeable": true
  },
  "bitLocker": {
    "state": "On",
    "sentence": "BitLocker protects drive C: with the TPM and checks the boot path (PCR 0, 2, 4, 11; TPM 1.2).",
    "risk": null,
    "recoveryKey": "If the next start asks for the recovery key, you need the 48-digit recovery password saved when BitLocker was turned on. Have it at hand before you confirm. This application never reads or shows it.",
    "suspendOffered": false,
    "choice": null,
    "suspendedForOneRestart": false
  },
  "secureBoot": {
    "enabled": true,
    "setupMode": false,
    "sourcesAgree": true
  },
  "snapshotFolder": null,
  "apiCalled": false,
  "api": null,
  "verification": null,
  "sentence": "Changeable: the order of its entries can be changed from this application (Move up, Move down, Review change). Nothing else about it can be changed.",
  "record": {
    "schema": "uefiaccess-write-record/1",
    "timestampUtc": "2026-09-30T12:12:00Z",
    "transactionId": "20260930-141200-check",
    "toolVersion": "0.1.0+0123456789abcdef0123456789abcdef01234567",
    "host": "cli",
    "operation": "Check",
    "isRestore": false,
    "restoredFrom": null,
    "variable": {
      "key": "8be4df61-93ca-11d2-aa0d-00e098032b8c:BootOrder",
      "name": "BootOrder",
      "guid": "8be4df61-93ca-11d2-aa0d-00e098032b8c"
    },
    "attributesBefore": 7,
    "attributesRequested": null,
    "attributeNames": "NV BS RT",
    "sizeBefore": 4,
    "sizeRequested": null,
    "beforeSha256": "7d6e4762a22170651b49bef86a5bb9d5de987f5d5f3e546901291fd24c411d39",
    "proposedSha256": null,
    "afterSha256": null,
    "before": {
      "present": true,
      "hex": "02000300",
      "entries": ["Boot0002", "Boot0003"]
    },
    "after": null,
    "checks": [
      { "name": "preconditions", "passed": true, "reason": "", "isWarning": false },
      { "name": "expectedModel", "passed": true, "reason": "", "isWarning": false },
      { "name": "expectedUnit", "passed": true, "reason": "", "isWarning": false },
      { "name": "enumeration", "passed": true, "reason": "", "isWarning": false },
      { "name": "whitelist", "passed": true, "reason": "", "isWarning": false },
      { "name": "attributes", "passed": true, "reason": "", "isWarning": false },
      { "name": "authenticatedWrite", "passed": true, "reason": "", "isWarning": false },
      { "name": "format", "passed": true, "reason": "", "isWarning": false },
      { "name": "currentListIntact", "passed": true, "reason": "", "isWarning": false },
      { "name": "secureBoot", "passed": true, "reason": "Secure Boot: on, user mode. This application never changes it.", "isWarning": true },
      { "name": "baseline", "passed": true, "reason": "BootOrder differs from the baseline of 20260923-111550; the firmware or Windows changed it since.", "isWarning": true },
      { "name": "bitLocker", "passed": true, "reason": "BitLocker protects drive C: with the TPM and checks the boot path (PCR 0, 2, 4, 11; TPM 1.2).", "isWarning": true },
      { "name": "snapshotRoot", "passed": true, "reason": "", "isWarning": false },
      { "name": "writeBudget", "passed": true, "reason": "", "isWarning": false }
    ],
    "blockedBy": null,
    "confirmed": false,
    "bitLockerChoice": null,
    "bitLocker": {
      "state": "On",
      "protectionStatus": 1,
      "conversionStatus": 1,
      "protectorTypes": ["TPM", "Numerical password"],
      "pcrProfileText": "PCR 0,2,4,11 (legacy)",
      "suspendOffered": false,
      "suspendChosen": false,
      "suspendResult": null,
      "protectionStatusAfterSuspend": null
    },
    "writesThisStart": 2,
    "bootId": 57,
    "deviceNodeTypes": {},
    "secureBoot": {
      "enabled": true,
      "setupMode": false,
      "sourcesAgree": true
    },
    "machine": {
      "model": "OptiPlex 7020",
      "biosVersion": "A18",
      "confirmed": true,
      "unitConfirmed": true
    },
    "apiCalled": false,
    "api": null,
    "verification": null,
    "snapshotFolder": null,
    "outcome": "Prepared",
    "exitCode": 0,
    "sentence": "Changeable: the order of its entries can be changed from this application (Move up, Move down, Review change). Nothing else about it can be changed."
  }
}
```

`uefiaccess write log --last 2 --json`: `total` is the number of records in the file, `notRecords`
the number of its lines that are not whole records (a crash while a line was written; they are
not shown), `records` the newest ones asked for, oldest first, each exactly the line of
`write-log.jsonl`. Without `--json` the command prints a table with the columns `Time (UTC)`,
`Transaction`, `Operation`, `Variable`, `Outcome`, `Exit`, `Write call made`, `Blocked by` and
`Sentence`, then `2 of 5 records in C:\ProgramData\UefiAccess\writes\write-log.jsonl.`; with no
record, `No write attempts are recorded in <file>.`; and, only when there are any,
`<n> lines of the file are not whole records (a crash while a line was written) and are not shown; the write budget counts each one of this start as a possible change.`

A commit shows as two lines of the same transaction: first its `Pending` intent line (`apiCalled`
false, exit code 0, the sentence
`The change is being written; this record is replaced when the result is known.`), appended at
stage S12 before the snapshot folder, the suspend and the write call, then the line with its
outcome. A commit that cannot append its intent line stops at `writeBudget` with nothing
written. A `Pending` line with no later line of its transaction is a commit
that ended without its result (a killed process or a power loss): read its snapshot folder with
`snapshots`, and the value with `boot`.

Every record carries `writesThisStart`, the writes the budget counted before this attempt
(`null` when the attempt stopped before the budget check), and `bootId`, Windows' boot counter at
the time (the `BootId` value under
`HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters`,
read with the key opened read-only; `null` when it could not be read). The budget counts, once
per transaction, every line of the current Windows start that made the write call, and a
`Pending` intent line until a later line of its transaction replaces it; a line is of the
current start when its `bootId` is the current one or its time is after the start, so an
unreadable counter never lowers the count. A line that is not a whole record counts as a
possible write unless its own time, or the time of the next line that shows one, is before the
start; the budget's refusal then says how many of the count are such lines.

<!-- schema: WriteLogDocument -->
```json
{
  "command": "write log",
  "ok": true,
  "file": "C:\\ProgramData\\UefiAccess\\writes\\write-log.jsonl",
  "total": 5,
  "notRecords": 0,
  "records": [
    {
      "schema": "uefiaccess-write-record/1",
      "timestampUtc": "2026-09-30T12:15:02Z",
      "transactionId": "20260930-141502-reorder",
      "toolVersion": "0.1.0+0123456789abcdef0123456789abcdef01234567",
      "host": "cli",
      "operation": "Reorder",
      "isRestore": false,
      "restoredFrom": null,
      "variable": {
        "key": "8be4df61-93ca-11d2-aa0d-00e098032b8c:BootOrder",
        "name": "BootOrder",
        "guid": "8be4df61-93ca-11d2-aa0d-00e098032b8c"
      },
      "attributesBefore": 7,
      "attributesRequested": 7,
      "attributeNames": "NV BS RT",
      "sizeBefore": 4,
      "sizeRequested": 4,
      "beforeSha256": "7d6e4762a22170651b49bef86a5bb9d5de987f5d5f3e546901291fd24c411d39",
      "proposedSha256": "470002f447314d088f3b84b7d1291b250f758237eb766cda6e495c7741512041",
      "afterSha256": null,
      "before": {
        "present": true,
        "hex": "02000300",
        "entries": ["Boot0002", "Boot0003"]
      },
      "after": {
        "present": true,
        "hex": "03000200",
        "entries": ["Boot0003", "Boot0002"]
      },
      "checks": [
        { "name": "preconditions", "passed": true, "reason": "", "isWarning": false },
        { "name": "expectedModel", "passed": true, "reason": "", "isWarning": false },
        { "name": "expectedUnit", "passed": true, "reason": "", "isWarning": false },
        { "name": "enumeration", "passed": true, "reason": "", "isWarning": false },
        { "name": "whitelist", "passed": true, "reason": "", "isWarning": false },
        { "name": "attributes", "passed": true, "reason": "", "isWarning": false },
        { "name": "authenticatedWrite", "passed": true, "reason": "", "isWarning": false },
        { "name": "format", "passed": true, "reason": "", "isWarning": false },
        { "name": "currentListIntact", "passed": true, "reason": "", "isWarning": false },
        { "name": "entriesExist", "passed": true, "reason": "", "isWarning": false },
        { "name": "permutation", "passed": true, "reason": "", "isWarning": false },
        { "name": "noChange", "passed": true, "reason": "", "isWarning": false },
        { "name": "bootCurrent", "passed": true, "reason": "", "isWarning": false },
        { "name": "otherWindowsPath", "passed": true, "reason": "", "isWarning": false },
        { "name": "removableAhead", "passed": true, "reason": "", "isWarning": false },
        { "name": "secureBoot", "passed": true, "reason": "Secure Boot: on, user mode. This application never changes it.", "isWarning": true },
        { "name": "baseline", "passed": true, "reason": "BootOrder differs from the baseline of 20260923-111550; the firmware or Windows changed it since.", "isWarning": true },
        { "name": "bitLocker", "passed": true, "reason": "BitLocker protects drive C: with the TPM and checks the boot path (PCR 0, 2, 4, 11; TPM 1.2).", "isWarning": true },
        { "name": "snapshotRoot", "passed": true, "reason": "", "isWarning": false },
        { "name": "writeBudget", "passed": true, "reason": "", "isWarning": false }
      ],
      "blockedBy": null,
      "confirmed": false,
      "bitLockerChoice": "SuspendForOneRestart",
      "bitLocker": {
        "state": "On",
        "protectionStatus": 1,
        "conversionStatus": 1,
        "protectorTypes": ["TPM", "Numerical password"],
        "pcrProfileText": "PCR 0,2,4,11 (legacy)",
        "suspendOffered": true,
        "suspendChosen": true,
        "suspendResult": null,
        "protectionStatusAfterSuspend": null
      },
      "writesThisStart": 0,
      "bootId": 57,
      "deviceNodeTypes": {
        "Boot0003": ["HD", "File"]
      },
      "secureBoot": {
        "enabled": true,
        "setupMode": false,
        "sourcesAgree": true
      },
      "machine": {
        "model": "OptiPlex 7020",
        "biosVersion": "A18",
        "confirmed": true,
        "unitConfirmed": true
      },
      "apiCalled": false,
      "api": null,
      "verification": null,
      "snapshotFolder": null,
      "outcome": "Prepared",
      "exitCode": 0,
      "sentence": "Every check passed; nothing has been written."
    },
    {
      "schema": "uefiaccess-write-record/1",
      "timestampUtc": "2026-09-30T12:17:33Z",
      "transactionId": "20260930-141733-bootnext",
      "toolVersion": "0.1.0+0123456789abcdef0123456789abcdef01234567",
      "host": "cli",
      "operation": "SetBootNext",
      "isRestore": false,
      "restoredFrom": null,
      "variable": {
        "key": "8be4df61-93ca-11d2-aa0d-00e098032b8c:BootNext",
        "name": "BootNext",
        "guid": "8be4df61-93ca-11d2-aa0d-00e098032b8c"
      },
      "attributesBefore": null,
      "attributesRequested": 7,
      "attributeNames": null,
      "sizeBefore": 0,
      "sizeRequested": 2,
      "beforeSha256": null,
      "proposedSha256": "99be5efb88ca2013bd8e4eb035fd42d5245468fe9afa70d8ba9c1c419a48c4e8",
      "afterSha256": null,
      "before": {
        "present": false,
        "hex": "",
        "entries": []
      },
      "after": {
        "present": true,
        "hex": "0200",
        "entries": ["Boot0002"]
      },
      "checks": [
        { "name": "preconditions", "passed": true, "reason": "", "isWarning": false },
        { "name": "expectedModel", "passed": true, "reason": "", "isWarning": false },
        { "name": "expectedUnit", "passed": true, "reason": "", "isWarning": false },
        { "name": "enumeration", "passed": true, "reason": "", "isWarning": false },
        { "name": "whitelist", "passed": true, "reason": "", "isWarning": false },
        { "name": "attributes", "passed": true, "reason": "", "isWarning": false },
        { "name": "authenticatedWrite", "passed": true, "reason": "", "isWarning": false },
        { "name": "format", "passed": true, "reason": "", "isWarning": false },
        { "name": "currentListIntact", "passed": true, "reason": "", "isWarning": false },
        { "name": "entriesExist", "passed": true, "reason": "", "isWarning": false },
        { "name": "bootNextTarget", "passed": true, "reason": "", "isWarning": false },
        { "name": "bootCurrent", "passed": true, "reason": "", "isWarning": false },
        { "name": "otherWindowsPath", "passed": true, "reason": "", "isWarning": false },
        { "name": "removableAhead", "passed": true, "reason": "", "isWarning": false },
        { "name": "secureBoot", "passed": true, "reason": "Secure Boot: on, user mode. This application never changes it.", "isWarning": true },
        { "name": "baseline", "passed": true, "reason": "", "isWarning": false },
        { "name": "bitLocker", "passed": false, "reason": "BitLocker protection is on and no choice was made about it; nothing is written.", "isWarning": false }
      ],
      "blockedBy": "bitLocker",
      "confirmed": false,
      "bitLockerChoice": "NotNeeded",
      "bitLocker": {
        "state": "On",
        "protectionStatus": 1,
        "conversionStatus": 1,
        "protectorTypes": ["TPM", "Numerical password"],
        "pcrProfileText": "PCR 0,2,4,11 (legacy)",
        "suspendOffered": true,
        "suspendChosen": false,
        "suspendResult": null,
        "protectionStatusAfterSuspend": null
      },
      "writesThisStart": null,
      "bootId": 57,
      "deviceNodeTypes": {
        "Boot0002": ["HD", "File"]
      },
      "secureBoot": {
        "enabled": true,
        "setupMode": false,
        "sourcesAgree": true
      },
      "machine": {
        "model": "OptiPlex 7020",
        "biosVersion": "A18",
        "confirmed": true,
        "unitConfirmed": true
      },
      "apiCalled": false,
      "api": null,
      "verification": null,
      "snapshotFolder": null,
      "outcome": "Blocked",
      "exitCode": 7,
      "sentence": "BitLocker protection is on and no choice was made about it; nothing is written."
    }
  ]
}
```

### `snapshots`

```
uefiaccess snapshots [--json]
```

Lists the snapshot folders under `%ProgramData%\UefiAccess\snapshots`, newest first, with the
operation, the time, the target and the outcome that each folder's `record.json` holds (a folder
without a readable `record.json` shows `(no record)`). Newest first means by the UTC time in
`record.json`, then by folder name, so the hour repeated when daylight saving time ends does not
put an older change first (the folder names keep the local time of the change); folders without
a readable time come last. A backward step of the clock itself can still put an older change
first, which is why the application's Restore previous value names the folder it offers: the
first folder of this order for that variable whose outcome is `Verified`, `Unverified`,
`ChangedDespiteError` or `Pending`. It only reads; a root that does not exist
lists nothing. One folder per committed change, named by the transaction id
`<yyyyMMdd-HHmmss>-<operation>` (`reorder`, `bootnext`, `bootnext-clear`, and `-restore` appended
for a restore), holding `snapshot.json`, `before\` (a complete baseline export of the variable list
the transaction enumerated, readable by `Open baseline folder` and by `write restore`), `after\` (the bytes the
verification read found), `record.json` and `log.txt`. The folders stay on the machine: `before\`
holds the machine identity with the service tag, as every baseline does.

```
Folder                  Operation Time (UTC)           Target    Outcome
20260930-142311-reorder Reorder   2026-09-30T12:23:11Z BootOrder Unverified
20260930-142010-reorder Reorder   2026-09-30T12:20:10Z BootOrder ApiRefused
20260930-141502-reorder Reorder   2026-09-30T12:15:09Z BootOrder Verified

3 snapshot folders under C:\ProgramData\UefiAccess\snapshots.
```

<!-- schema: SnapshotsDocument -->
```json
{
  "command": "snapshots",
  "ok": true,
  "root": "C:\\ProgramData\\UefiAccess\\snapshots",
  "count": 3,
  "snapshots": [
    { "folder": "C:\\ProgramData\\UefiAccess\\snapshots\\20260930-142311-reorder", "name": "20260930-142311-reorder", "operation": "Reorder", "timestampUtc": "2026-09-30T12:23:11Z", "targetKey": "8be4df61-93ca-11d2-aa0d-00e098032b8c:BootOrder", "outcome": "Unverified" },
    { "folder": "C:\\ProgramData\\UefiAccess\\snapshots\\20260930-142010-reorder", "name": "20260930-142010-reorder", "operation": "Reorder", "timestampUtc": "2026-09-30T12:20:10Z", "targetKey": "8be4df61-93ca-11d2-aa0d-00e098032b8c:BootOrder", "outcome": "ApiRefused" },
    { "folder": "C:\\ProgramData\\UefiAccess\\snapshots\\20260930-141502-reorder", "name": "20260930-141502-reorder", "operation": "Reorder", "timestampUtc": "2026-09-30T12:15:09Z", "targetKey": "8be4df61-93ca-11d2-aa0d-00e098032b8c:BootOrder", "outcome": "Verified" }
  ]
}
```

## 4. `gate.json` layout

From the spec: "`gate` writes `gate.json` containing: machine identity with
`ExpectedModelConfirmed`, boot mode findings (a), Dell interface probe (b), varstore match (c) if
`--ifr-varstores` is given (each line `Name;GUID;Size`; mark each as present at runtime with
attributes and size match, or absent), BitLocker and Secure Boot policy (d). It does not compute a
recommendation; a human fills that in."

The document is the CLI's `GateReport` record serialized with the same camelCase settings as every
`--json` document (indented, enums as strings, nulls kept). Its top-level properties, in this order:

| Property | Question | Content |
|---|---|---|
| `toolVersion` | | the tool version, `0.1.0+<commit>` (section 5) |
| `timestampUtc`, `timestampLocal` | | when the gate ran, `yyyy-MM-ddTHH:mm:ss.fffZ` and `yyyy-MM-ddTHH:mm:ss.fff+hh:mm` |
| `identity` | | the Core `MachineIdentityResult`: `manufacturer`, `model`, `systemSku`, `biosVersion`, `biosReleaseDate`, `serviceTag`, `baseBoard`, `cpu`, `osCaption`, `osVersion`, `osBuild`, `expectedModelConfirmed`, `expectedModelNotes`, `errors` |
| `bootMode` | (a) | `firmwareType`, `privilegeState`, `secureBootCapable`, `secureBootEnabled`, `setupMode`, `deployedMode`, `auditMode`, `sourcesAgree`, `variableState` (the Core `SecureBootState`, the same object as `state` in the `secureboot` document), `enumerationMethod`, `variableCount`, `entriesParsed`, `enumerationError`, `enumerationWarnings` |
| `dellInterface` | (b) | the Core `DellInterfaceProbeResult`: `dcmClassesPresent` (true when at least one of the four classes is defined), `dcmClassesFound` (the classes defined in `root\dcim\sysman`; a definition does not prove the provider answers), `samples` (up to three `{name, currentValue, currentValueDescription, possibleValues, possibleValuesDescription, source}` with `source` `"DCM"`), `cctkFound`, `cctkPath`, `cctkVersion`, `cctkSecureBootOutput`, `nativeBiosAttributesNamespacePresent`, `nativeWmiSecurityNamespacePresent`, `errors`, `dcimClassCount`, `cctkSecureBootEnabled`, `dcmClassesResponding` (the defined classes that returned an instance within 60 seconds; outcome A depends on this list, not on `dcmClassesPresent`) |
| `varstoreMatch` | (c) | `null` without `--ifr-varstores`; otherwise `file`, `entryCount`, `presentCount`, `absentCount`, `unknownCount`, `sizeMismatchCount`, `enumerationComplete`, `incompleteReason`, `entries` (one per varstore line: `line`, `name`, `guid`, `ifrSize`, `present`, `status`, `key`, `attributes`, `attributeFlags`, `attributeNames`, `runtimeSize`, `sizeMatches`, `note`), `parseErrors` |
| `bitLocker` | (d) | the Core `BitLockerResult`: `volumes` (`driveLetter`, `protectionStatus` 0/1/2, `protectionStatusText`, `conversionStatus` 0 to 5 or -1 when unreadable, `conversionStatusText`, `protectorTypes`, `pcrProfile`, `pcrProfileText`), `errors` |
| `secureBootPolicy` | (d) | the Core `SecureBootPolicyResult` (the same object as `policy` in the `secureboot` document) |
| `tpm` | (d) | the Core `TpmInfoResult`: `present`, `specVersion`, `enabled`, `activated`, `errors` |
| `recommendation` | | always the empty string `""`: the recommendation (outcome A, B, C, or none) is written by a person in the memo |

Notes on `varstoreMatch.entries`: `present` is true when a runtime-visible variable with that name
and GUID exists, false when it does not, and `null` when that could not be determined; `status`
says the same in one word, `present`, `absent` or `unknown`. For a present varstore `key`,
`attributes` (number), `attributeFlags` (`NV BS RT`), `attributeNames` (list) and `runtimeSize`
describe it and `sizeMatches` compares `runtimeSize` with `ifrSize`; for an absent or unknown one
those six properties are `null`. `note` repeats the finding in words. An absent varstore is either
boot-service only (no `RT` attribute, so invisible to the OS) or not stored as a UEFI variable at
all; the tool cannot tell which, and the memo says so. A line that does not parse is listed in
`parseErrors` with its line number and is skipped.

When the enumeration was complete (`enumerationComplete` true, `incompleteReason` `null`), a
varstore it did not list is absent. When it was not (the known-names fallback, a stop at a
malformed entry, or no variables at all; `incompleteReason` says which), each varstore it did not
list is read directly: found is present (`note`
`present at runtime (found by a direct read; the incomplete enumeration did not list it); size matches the IFR`),
`ERROR_ENVVAR_NOT_FOUND` is absent (`note`
`absent at runtime (confirmed by a direct read, because the enumeration was incomplete): boot-service only (no EFI_VARIABLE_RUNTIME_ACCESS) or not stored as a UEFI variable`),
and any other failure is `unknown` (`note`
`unknown: the enumeration was incomplete (<incompleteReason>) and a direct read failed (<Status>: <error>)`)
and counts in `unknownCount`, not in `absentCount`. Outcome C can be ruled out only when
`unknownCount` is 0.

The full document of the example run:

<!-- schema: GateReport -->
```json
{
  "toolVersion": "0.1.0+0123456789abcdef0123456789abcdef01234567",
  "timestampUtc": "2026-09-10T12:35:00.123Z",
  "timestampLocal": "2026-09-10T14:35:00.123+02:00",
  "identity": {
    "manufacturer": "Dell Inc.",
    "model": "OptiPlex 7020",
    "systemSku": "0620",
    "biosVersion": "A18",
    "biosReleaseDate": "2019-05-02",
    "serviceTag": "5XK2P52",
    "baseBoard": "0F5C5X",
    "cpu": "Intel(R) Core(TM) i5-4590 CPU @ 3.30GHz",
    "osCaption": "Microsoft Windows 11 Pro",
    "osVersion": "10.0.22631",
    "osBuild": "22631",
    "expectedModelConfirmed": true,
    "expectedModelNotes": "Model \"OptiPlex 7020\": contains 7020 (match). BIOS version \"A18\": Dell A-series version (match). CPU \"Intel(R) Core(TM) i5-4590 CPU @ 3.30GHz\": 4th-generation Intel (Haswell) part (match). Expected model confirmed: 2014 Dell OptiPlex 7020 (model, BIOS version and CPU generation all match).",
    "errors": []
  },
  "bootMode": {
    "firmwareType": "Uefi",
    "privilegeState": "Enabled",
    "secureBootCapable": true,
    "secureBootEnabled": true,
    "setupMode": false,
    "deployedMode": null,
    "auditMode": null,
    "sourcesAgree": true,
    "variableState": {
      "secureBoot": true,
      "setupMode": false,
      "auditMode": null,
      "deployedMode": null,
      "pkPresent": true,
      "pkSize": 858,
      "kekPresent": true,
      "kekSize": 1560,
      "db": {
        "size": 3143,
        "listCount": 2,
        "signatureTypes": [
          "X509",
          "X509"
        ],
        "signatureCount": 2,
        "error": null
      },
      "dbx": {
        "size": 21148,
        "listCount": 1,
        "signatureTypes": [
          "SHA256"
        ],
        "signatureCount": 440,
        "error": null
      },
      "dbt": null
    },
    "enumerationMethod": "NtEnumerate",
    "variableCount": 63,
    "entriesParsed": 63,
    "enumerationError": null,
    "enumerationWarnings": []
  },
  "dellInterface": {
    "dcmClassesPresent": true,
    "dcmClassesFound": [
      "DCIM_BIOSEnumeration",
      "DCIM_BIOSString",
      "DCIM_BIOSInteger",
      "DCIM_BIOSService"
    ],
    "samples": [
      {
        "name": "Secure Boot",
        "currentValue": "2",
        "currentValueDescription": "Enabled",
        "possibleValues": [
          "1",
          "2"
        ],
        "possibleValuesDescription": [
          "Disabled",
          "Enabled"
        ],
        "source": "DCM"
      },
      {
        "name": "Boot List Option",
        "currentValue": "2",
        "currentValueDescription": "UEFI",
        "possibleValues": [
          "1",
          "2"
        ],
        "possibleValuesDescription": [
          "Legacy",
          "UEFI"
        ],
        "source": "DCM"
      },
      {
        "name": "SATA Operation",
        "currentValue": "2",
        "currentValueDescription": "AHCI",
        "possibleValues": [
          "1",
          "2",
          "3"
        ],
        "possibleValuesDescription": [
          "Disabled",
          "AHCI",
          "RAID On"
        ],
        "source": "DCM"
      }
    ],
    "cctkFound": true,
    "cctkPath": "C:\\Program Files (x86)\\Dell\\Command Configure\\X86_64\\cctk.exe",
    "cctkVersion": "Dell Command | Configure Version 4.8.0",
    "cctkSecureBootOutput": "secureboot=enable",
    "nativeBiosAttributesNamespacePresent": false,
    "nativeWmiSecurityNamespacePresent": false,
    "errors": [],
    "dcimClassCount": 38,
    "cctkSecureBootEnabled": true,
    "dcmClassesResponding": [
      "DCIM_BIOSEnumeration",
      "DCIM_BIOSString",
      "DCIM_BIOSInteger",
      "DCIM_BIOSService"
    ]
  },
  "varstoreMatch": {
    "file": "C:\\work\\ifr-varstores.txt",
    "entryCount": 3,
    "presentCount": 1,
    "absentCount": 2,
    "unknownCount": 0,
    "sizeMismatchCount": 0,
    "enumerationComplete": true,
    "incompleteReason": null,
    "entries": [
      {
        "line": 1,
        "name": "Setup",
        "guid": "ec87d643-eba4-4bb5-a1e5-3f3e36b20da9",
        "ifrSize": 741,
        "present": true,
        "status": "present",
        "key": "ec87d643-eba4-4bb5-a1e5-3f3e36b20da9:Setup",
        "attributes": 7,
        "attributeFlags": "NV BS RT",
        "attributeNames": [
          "NonVolatile",
          "BootServiceAccess",
          "RuntimeAccess"
        ],
        "runtimeSize": 741,
        "sizeMatches": true,
        "note": "present at runtime; size matches the IFR"
      },
      {
        "line": 2,
        "name": "PchSetup",
        "guid": "4570b7f1-ade8-4943-8dc3-406472842384",
        "ifrSize": 51,
        "present": false,
        "status": "absent",
        "key": null,
        "attributes": null,
        "attributeFlags": null,
        "attributeNames": null,
        "runtimeSize": null,
        "sizeMatches": null,
        "note": "absent at runtime: boot-service only (no EFI_VARIABLE_RUNTIME_ACCESS) or not stored as a UEFI variable"
      },
      {
        "line": 3,
        "name": "SaSetup",
        "guid": "72c5e28c-7783-43a1-8767-fad73fccafa4",
        "ifrSize": 44,
        "present": false,
        "status": "absent",
        "key": null,
        "attributes": null,
        "attributeFlags": null,
        "attributeNames": null,
        "runtimeSize": null,
        "sizeMatches": null,
        "note": "absent at runtime: boot-service only (no EFI_VARIABLE_RUNTIME_ACCESS) or not stored as a UEFI variable"
      }
    ],
    "parseErrors": []
  },
  "bitLocker": {
    "volumes": [
      {
        "driveLetter": "C:",
        "protectionStatus": 1,
        "protectionStatusText": "On",
        "conversionStatus": 1,
        "conversionStatusText": "Encrypted",
        "protectorTypes": [
          "TPM",
          "Numerical password"
        ],
        "pcrProfile": [
          7,
          11
        ],
        "pcrProfileText": "PCR 7+11"
      }
    ],
    "errors": []
  },
  "secureBootPolicy": {
    "registryEnabled": true,
    "variableSecureBoot": true,
    "variableSetupMode": false,
    "variableDeployedMode": null,
    "variableAuditMode": null,
    "powerShellConfirm": true,
    "capable": true,
    "sourcesAgree": true,
    "errors": []
  },
  "tpm": {
    "present": true,
    "specVersion": "1.2, 2, 3",
    "enabled": true,
    "activated": true,
    "errors": []
  },
  "recommendation": ""
}
```

A present varstore whose runtime size differs from the IFR has `"sizeMatches": false` and the note
`present at runtime; runtime size 745 differs from IFR size 741`, and counts in `sizeMismatchCount`.
BitLocker protector type names are `TPM`, `External key`, `Numerical password`, `TPM+PIN`,
`TPM+startup key`, `TPM+PIN+startup key`, `Public key`, `Passphrase`, `TPM certificate`,
`CryptoAPI`, `Unknown or other`; `pcrProfile` is empty and `pcrProfileText` `null` when no TPM
protector exists. `conversionStatusText` is `Decrypted` (0), `Encrypted` (1), `Encrypting` (2),
`Decrypting` (3), `Encryption paused` (4), `Decryption paused` (5) or `Unknown` (-1, not readable).
Windows reports `protectionStatus` 0 while a volume is still being encrypted; `protectionStatusText`
then reads `Pending until encryption completes` (or `Pending, encryption paused`) instead of `Off`,
and `On` once encryption has finished with a protector in place.

In `dellInterface.samples`, Dell Command | Monitor returns `currentValue` and `possibleValues` as
codes; `possibleValuesDescription` holds the name of each code, index for index, and
`currentValueDescription` is the name of the current code (`null` when it cannot be matched), so the
memo can quote `Secure Boot: Enabled (code 2; possible Disabled, Enabled)`.

## 5. Global options and help

- `--help` (also `-h`, `-?`) on the root or on any command prints the usage and exits with 0. The
  root help also lists the exit codes.
- `--version` prints the tool version and exits with 0. The version is the informational version
  of the build, `0.1.0+<commit hash>` (for example
  `0.1.0+0123456789abcdef0123456789abcdef01234567`), where the suffix is the git commit the build
  was made from. A build of a working tree with uncommitted changes still shows the last commit,
  with no "modified" marker. The same string appears as `Tool version` in `info`, as `toolVersion` in the `info`
  document and in `gate.json`, and as `ToolVersion` in `index.json`; the application logs it at
  start-up, in the first line of its log panel (`UefiAccess.App 0.1.0+<commit> starting on ...`).
  Every executable reads it from one Core helper (`UefiAccess.Core.Diagnostics.ToolVersion.Current`),
  so the CLI and the application cannot disagree.
- `--verbose` (also `-v`) is accepted by every command, before or after the command name. It mirrors
  the log (every native call with its arguments, result, error and elapsed time, plus the command
  steps) to standard error as `[<level>] <message>` lines, for example:

```
[INF] uefiaccess 0.1.0+0123456789abcdef0123456789abcdef01234567 command list started: list -v
[INF] Preconditions: firmware type Uefi, elevated True
[DBG] OpenProcessToken(access=0x28) -> True, error 0, 0.0812 ms
[DBG] NtEnumerateSystemEnvironmentValuesEx(class=2, buffer=null, length=0) -> 0xC0000023 (STATUS_BUFFER_TOO_SMALL (0xC0000023): the buffer is too small; the required length was returned), length 48213, 0.4123 ms
```

  Standard output is unaffected, so `uefiaccess list --json -v > list.json` still produces a clean
  file. Without `--verbose` the log goes only to the daily log file.
- An unknown command or option, or a missing argument, prints the System.CommandLine message and
  `Run 'uefiaccess --help' for usage.` on standard error and exits with 2. When `--json` is among
  the arguments (for example `uefiaccess list --json --bogus`), standard output also receives the
  error document of section 2 with `"exitCode": 2`, so a captured `list.json` is still valid JSON.

## 6. A typical session

```
uefiaccess info
uefiaccess list
uefiaccess boot
uefiaccess secureboot
uefiaccess baseline
uefiaccess gate --ifr-varstores C:\work\ifr-varstores.txt --out C:\work\gate.json
```

`tools\gate\Run-Gate.ps1` runs exactly this sequence (plus the `--json` forms of `list`, `boot`
and `secureboot`, and, when `C:\ProgramData\UefiAccess` does not exist yet, one `info` before it
whose output is not kept, so that the tool creates that folder with its protected access list),
captures standard output and standard error of every command into its own
folder, and prints the manual checklist (F12 boot menu, msinfo32, baseline folder, BIOS version and
boot mode for the memo).
