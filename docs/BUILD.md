# Building, testing, and running UEFI-Access

## 1. Prerequisites

### To build and unit-test (any OS)

- .NET 8 SDK, 8.0.400 or later (`dotnet --version`; the solution is verified with 8.0.425).
  Download from https://dotnet.microsoft.com/download/dotnet/8.0. On Windows, install the x64 SDK
  with its installer, which installs it machine-wide: the built executables need the machine-wide
  .NET 8 runtime (section "To run the tools" below). A per-user install (the `dotnet-install`
  script, into `%LOCALAPPDATA%\Microsoft\dotnet`) builds and tests, but the executables it builds
  then stop with "You must install or update .NET to run this application."
- Nothing else. All NuGet packages restore from nuget.org during `dotnet build`. The solution uses
  only `System.Management`, `System.CommandLine`, `Serilog` (file and console sinks), `xunit`,
  `xunit.runner.visualstudio`, and `Microsoft.NET.Test.Sdk`; no native DLLs, no kernel drivers.
- Linux and macOS: every project targets `net8.0-windows` and `Directory.Build.props` sets
  `EnableWindowsTargeting=true`, so the solution builds (the WPF application included) and the
  unit tests run on a non-Windows host. Windows-only APIs (the ntdll/kernel32/advapi32 P/Invokes,
  WMI, the registry) throw `PlatformNotSupportedException` or `DllNotFoundException` there;
  production code guards every such call with `OperatingSystem.IsWindows()` and returns a result
  object with an `Errors` list instead, and the unit tests never call them. WPF itself cannot
  start on a non-Windows host, which is why the tests reference the presentation library and not
  the application (section 3). This is verified on x64 Linux; macOS has not been tried.
  `Directory.Build.props` also sets `PlatformTarget` to x64 for every project, the test project
  included, so on an arm64 host (an Apple Silicon Mac, arm64 Linux) `dotnet test` must start an
  x64 test host and needs the x64 .NET 8 runtime installed next to the arm64 SDK; without it the
  test run stops with "Could not find 'dotnet' host for the 'X64' architecture".
- Optional editors: Visual Studio 2022 17.8 or later with the ".NET desktop development" workload,
  or Visual Studio Code with the C# Dev Kit. Neither is required; the command line is enough.

### To run the tools

- Windows 11 x64 or Windows 10 22H2 x64. The reference 2014 OptiPlex 7020 runs Windows 11 Pro
  25H2, build 26200, on hardware Microsoft does not support for Windows 11 (TPM 1.2, a
  4th-generation CPU).
- The machine-wide .NET 8 runtime (x64). The .NET 8 SDK x64 installed with its installer brings
  it; a machine that only runs the tools needs the ".NET Desktop Runtime 8.0 (x64)" (the
  "Desktop" runtime includes WPF, which the application needs; the plain ".NET Runtime" is enough
  for the CLI only). A per-user SDK install is not enough: `uefiaccess.exe` then prints
  "You must install or update .NET to run this application." and names the missing framework
  (`Microsoft.NETCore.App`, version `8.0.0` (x64)), and `UefiAccess.App.exe` shows the same
  message in a dialog. The fix is the machine-wide SDK installer or the Desktop Runtime installer.
  A .NET 9 or 10 runtime does not help, because the projects do not roll forward to another major
  version. With only a per-user SDK and the .NET 9 and 10 runtimes installed, both executables
  refuse to start until the machine-wide .NET Desktop Runtime 8.0 (x64) is installed.
- For live firmware access the machine must have booted in UEFI mode (msinfo32, "BIOS Mode" =
  UEFI). In legacy BIOS mode every CLI command prints one explanatory sentence and exits with
  code 3, and the application's `Refresh` shows the same sentence in its Status box. The
  application's offline mode (a baseline folder) works on any Windows machine.
- An elevated prompt for the CLI (see section 4); the application asks for elevation itself.
- For accessibility testing: NVDA 2026.2 or Narrator, and optionally Accessibility Insights for
  Windows. NVDA must be the installed copy: a portable NVDA cannot read the window of an elevated
  application, and the application always runs elevated.
- For the gate on the target machine: Dell Command | Monitor and Dell Command | Configure (both
  free downloads from Dell support), installed before `uefiaccess gate` runs; Windows PowerShell 5.1
  (built into Windows) for `tools/gate/Run-Gate.ps1`.
- For the Dell settings view (the `Dell settings` region and `uefiaccess dell`): the
  Microsoft Visual C++ 2015-2022 x64 runtime, installed first, then Dell Command | Monitor
  10.13.2.56 with its data service `dcstor64` running; optionally Dell Command | Configure 5.2.2,
  the fallback. None of it is needed to build or test, and the tools never install or start any of
  it. `docs/Dell-Interface-Setup.md` has the order, the read-only checks and what the tool says
  when a part is missing; without them the view says so in one sentence and the rest of the tools
  work as before.
- For gate question (c) on the developer workstation: BIOSUtilities ("Dell PFS Update Extractor"),
  UEFITool NE, and IFRExtractor-RS. They never run on the target unit and are not part of the build.

## 2. Build

From the repository root:

```
dotnet build UefiAccess.sln -c Release
```

On Linux or macOS, put the SDK on the path first if it is not there already, for example:

```
export PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
dotnet build UefiAccess.sln -c Release
```

The build must end with `0 Warning(s)` and `0 Error(s)`. `Directory.Build.props` turns on
`TreatWarningsAsErrors` for every project and `GenerateDocumentationFile` for every project under
`src/`, so a missing XML doc comment on a public member (CS1591) is a build error there. Write the
comment; do not suppress the warning.

Outputs:

| Project | Output |
|---|---|
| `src/UefiAccess.Core` | `src/UefiAccess.Core/bin/Release/net8.0-windows/UefiAccess.Core.dll` |
| `src/UefiAccess.Cli` | `src/UefiAccess.Cli/bin/Release/net8.0-windows/uefiaccess.exe` (assembly name `uefiaccess`) |
| `src/UefiAccess.Presentation` | `src/UefiAccess.Presentation/bin/Release/net8.0-windows/UefiAccess.Presentation.dll` (view models, services, strings; no WPF) |
| `src/UefiAccess.App` | `src/UefiAccess.App/bin/Release/net8.0-windows/UefiAccess.App.exe` (the WPF application; copy the whole folder to run it elsewhere) |
| `src/UefiAccess.UiaTrace` | `src/UefiAccess.UiaTrace/bin/Release/net8.0-windows/uefiaccess-uia-trace.exe` (assembly name `uefiaccess-uia-trace`; the console recorder of the UI Automation events for the input trace, section 4a; copy the whole folder to run it elsewhere) |
| `tests/UefiAccess.Core.Tests` | `tests/UefiAccess.Core.Tests/bin/Release/net8.0-windows/UefiAccess.Core.Tests.dll` |

`dotnet build -c Debug` (or plain `dotnet build`) works the same way and writes to `bin/Debug`.
`Run-Gate.ps1` looks for the Release output by default; pass `-ExePath` for anything else.

## 3. Test

```
dotnet test UefiAccess.sln -c Release
```

The unit tests need no firmware access and run on any OS. They cover the enumeration buffer
parser (synthetic buffers, including a corrupted one), the load option decoder and the device path
formatter (byte-array fixtures for Windows Boot Manager, a USB stick, a legacy BBS entry, a PXE
entry, and a truncated entry), attribute flag rendering and parsing, the baseline exporter round
trip through `InMemoryFirmwareVariableStore` into a temporary directory (including exporting the
same request twice and comparing the bytes), the Windows store over a scripted native seam, the CLI
commands (`CliCommandTests`: `info`, `list`, `read`, `boot`, `secureboot`, `baseline` and `gate`
run through the real command classes against an in-memory store built from the load-option
fixtures, with a fake UEFI firmware type, a fake elevated privilege state and platform inspectors
that return fixed records, so nothing touches WMI, the registry or PowerShell; exit codes, the
single `--json` document, the boot table, the baseline folder and `gate.json` are asserted), the
shared version string (`ToolVersionTests`: the CLI and the application both read
`ToolVersion.Current`), the no-write guard, and the application
tests (the view-model and service tests under `tests/UefiAccess.Core.Tests/Presentation/`, the
static XAML checks in `WpfShellAccessibilityTests.cs`): the shell state machine (Idle, Loading,
Loaded, Refused, Failed) with the Status text and the focus requests for a successful load, each
refusal, a failure and a baseline load, asserted against a recording announcer and a recording
focus sink; filtering, grouping, sorting and the row sentence of the variable list; the detail
pane sections and the hex dump; the baseline folder round trip (`BaselineExporter` output loaded
back byte for byte, and malformed folders that yield errors, not exceptions); the namespace
catalog; and the static XAML checks of `MainWindow.xaml` (a name on every control, help text on
every input, the six named region groups in F6 order and the headings, unique tab indices,
exactly one polite live region, no template overrides, no hard-coded colors, no write-implying
labels).

Why the test project references `UefiAccess.Presentation` and not `UefiAccess.App`: the M2 prompt
asked for a project reference to the WPF application, and that builds on Linux, but the test host
then needs the Windows Desktop runtime (`Microsoft.WindowsDesktop.App`) to start, which does not
exist on a non-Windows host, so `dotnet test` fails before running anything. Everything testable
therefore lives in `UefiAccess.Presentation`, a plain class library (`net8.0-windows`, no
`UseWPF`, no reference to `PresentationFramework`, `WindowsBase` or `System.Xaml`), and the App
keeps only the XAML, the focus and live-region code-behind, and the WPF implementations of the
dialog, clipboard, announcer and focus-sink interfaces. `ScaffoldSmokeTests` asserts that the
Presentation assembly references no WPF assembly, so the split cannot erode unnoticed. For the
same reason the test project has no reference to the recorder `UefiAccess.UiaTrace` (its managed
UI Automation client comes with the Windows Desktop runtime): it compiles the recorder's pure
files, `src/UefiAccess.UiaTrace/Recording/*.cs`, as linked files, and `UiaTraceRecorderGuardTests`
keeps that folder free of `System.Windows.Automation`. The input trace's tests are
`TraceLineTests`, `TraceRedactorTests`, `TraceFileWriterTests`, `MachineIdentityServiceTagTests`,
`LoggingRunFileFolderTests`, the three `InputTrace*` classes, `ShellInputTraceTests`,
`UiaTraceRecorderGuardTests` and the recorder's own under `Recorder/`. The in-place update of the
detail pane (finding `A2-7`) is tested by `DetailPaneReconcilerTests` and
`DetailPaneInPlaceUpdateTests` under `Presentation/`, and by `DetailPaneStableBindingTests`, which
reads `MainWindow.xaml`, its code-behind and the view models' source.

The Dell settings provider is tested with
hand-written fakes and recorded outputs only, so no test reads Dell Command | Monitor or starts
Dell Command | Configure on the machine that runs the suite (`DellSettingsGuardTests` fails on a
test that would): `DellAttributeCatalogTests`, `DcmSettingsProviderTests` (over the 72 attributes of
the A18 export, typed in `Fixtures/DcmRowFixtures.cs`),
`CctkSettingsProviderTests` (over `Fixtures/CctkOutputFixtures.cs`), `CctkReaderTests`,
`DellMachineReaderTests`, `DellSettingsReaderTests`, `DellSentencesTests`, `DellSettingsFileTests`,
`NetworkStackDecoderTests`, `CliDellCommandTests`, and under `Presentation/`
`DellSettingsViewModelTests`, `ShellDellTests` and `LiveDellSettingsSourceTests`.

Apart from the opt-in regeneration of `tools/sample-baseline` (section 5), the tests write only
into temporary folders. A full `dotnet test` run creates nothing under
`%ProgramData%` or `%LOCALAPPDATA%` (some earlier builds' logging smoke test wrote into the real
`C:\ProgramData\UefiAccess\logs`, and created `C:\ProgramData\UefiAccess` when it did not exist;
section 5 says what to do with a folder made that way). On Windows a run reports some
skipped tests: the 11 that only mean something on a non-Windows host, and two symbolic-link
tests, which need the "Create symbolic links" privilege (Developer Mode on, or an elevated
prompt): the baseline reader's refusal of a `vars` file or an `index.json` that is a symbolic link, and the elevated
per-user log fallback's refusal of a log file that is a symbolic link.

To run only the read-only guards:

```
dotnet test UefiAccess.sln -c Release --filter "FullyQualifiedName~NoWriteGuardTests|FullyQualifiedName~CompiledStringGuardTests|FullyQualifiedName~ProcessLaunchGuardTests|FullyQualifiedName~NativeLocationGuardTests|FullyQualifiedName~DellSettingsGuardTests"
```

`NoWriteGuardTests` scans every `*.cs` and `*.xaml` file under `src/` and every `*.ps1` and
`*.psm1` under `tools/` (excluding `bin/` and `obj/`) for the write paths listed in `README.md`, in code,
comments and string literals alike. It also pins exactly what the cctk and PowerShell launches
pass. `CompiledStringGuardTests` applies the same checks to every string compiled into the Core,
CLI and Presentation assemblies. `ProcessLaunchGuardTests` keeps process creation inside
`ProcessRunner`. `NativeLocationGuardTests` keeps native declarations in `src/UefiAccess.Core/Native`
and forbids binding native code at run time. `DellSettingsGuardTests` keeps the
Dell settings folder to its three pinned queries, away from Dell Command | Monitor's service and
string classes and from any method call, keeps service control and install commands out of `src/`
and `tools/`, and keeps the tests from running the real Dell reader. Each prints what it found
(`file:line` for the source scans). Keep them green.

Hardware checks are deliberately not unit tests; see section 6.

## 4. Running the CLI elevated (and why)

Every firmware variable call on Windows (`NtEnumerateSystemEnvironmentValuesEx` for enumeration,
`GetFirmwareEnvironmentVariableExW` for reading) requires `SeSystemEnvironmentPrivilege`. That
privilege exists only in an elevated (administrator) token; a standard token does not contain it,
so it cannot be enabled there. `uefiaccess` enables the privilege itself with `OpenProcessToken`,
`LookupPrivilegeValueW`, and `AdjustTokenPrivileges`, and reports one of three states:
`NotElevated`, `ElevatedPrivilegeAbsent` (elevated, but `AdjustTokenPrivileges` reported
`ERROR_NOT_ALL_ASSIGNED` (1300) or an earlier step failed), or `Enabled`. Only `Enabled` lets a
command proceed; the other two print one sentence and exit with code 4.

To run elevated:

1. Press Start, type `cmd` (or `Terminal` / `PowerShell`), and choose "Run as administrator"
   (with a screen reader: arrow to the result, press the Applications key or Shift+F10, choose
   "Run as administrator", then accept the UAC prompt with Alt+Y or Enter).
2. Change to the repository root and run the tool:

```
cd C:\src\UefiAccess
src\UefiAccess.Cli\bin\Release\net8.0-windows\uefiaccess.exe info
```

From an already open, non-elevated PowerShell you can also start an elevated one with
`Start-Process powershell -Verb RunAs`.

The application (`UefiAccess.App.exe`) carries an `app.manifest` with
`requestedExecutionLevel level="requireAdministrator"`, so Windows shows the UAC prompt on start;
no manual elevation is needed (section 4a).

Redirecting output: standard output carries only the tables or, with `--json`, exactly one JSON
document, so `uefiaccess list --json > list.json` produces a clean file, UTF-8 without a byte
order mark, in `cmd.exe`. Windows PowerShell 5.1 re-reads captured program output in the
console's code page, so characters outside ASCII arrive garbled there unless you first run
`[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)` in that session. On a
console window the tool writes Unicode text and does not change the code page. The precondition
sentence, failure messages and `Warning:` lines go to standard error; the log is mirrored there
only with `--verbose` (`-v`), otherwise it goes to the log file alone (`docs/CLI.md`, section 1).
An elevated run writes the shared daily log; a run without elevation writes the per-user log
(section 5).

## 4a. Running the application

```
src\UefiAccess.App\bin\Release\net8.0-windows\UefiAccess.App.exe
```

Start it from Explorer, a prompt or `Win+R`; Windows shows the UAC prompt (the manifest requests
`requireAdministrator`, because reading firmware variables needs `SeSystemEnvironmentPrivilege`,
which exists only in an elevated token). Accept it with `Alt+Y`. The prompt appears even when
you only intend to open a baseline folder offline. With a screen reader, use the installed copy of
NVDA: a portable copy cannot read an elevated application's window.

The build is unsigned, so Windows may object the first time:

- SmartScreen may show "Windows protected your PC": choose "More info", then "Run anyway".
- Smart App Control, where it is on, blocks an unsigned build outright and offers no
  "Run anyway". A rebuild produces a new file that Windows judges afresh, which may clear it, but
  any build can be blocked. Signing the build would be the lasting answer.

If neither executable starts and Windows says "You must install or update .NET to run this
application.", the machine has no machine-wide .NET 8 runtime; see section 1, "To run the tools".

The window opens with focus on `Refresh` and the Status box reading
`Ready. Press Refresh (Alt+R) to read the firmware variables, or Open baseline folder (Alt+O) to view a baseline offline.`
Nothing is loaded and nothing happens until you choose one of the two:

- `Refresh` (`Alt+R`) runs the same precondition checks as the CLI (firmware type UEFI, process
  elevated, privilege enabled), then enumerates the live store with the Core
  `WindowsFirmwareVariableStore` and reads the machine identity. On success focus goes to the
  first row, Status says `Loaded <N> variables from Live firmware (NtEnumerate)` (or
  `KnownNamesFallback` when the enumeration call failed and the curated names were read one by
  one) and the window title becomes `UefiAccess – Live firmware` (it is `UefiAccess` alone only
  before the first load). On a refusal focus goes to the Status box; the UEFI and elevation sentences are the
  CLI's word for word, and the privilege-absent sentence is the application's own.
- `Open baseline folder` (`Alt+O`) shows the standard folder dialog; pick a folder written by
  `uefiaccess baseline` (the timestamped one that contains `index.json`; section 5 shows the
  layout). The GUI reads `index.json` and `vars\*.bin`, reconstructs every variable with its
  attributes, decodes the raw bytes with `DecoderRegistry.CreateDefault()` exactly like the CLI,
  and shows the identity recorded in the index. The window title becomes
  `UefiAccess – Baseline (offline): <folder> (<timestamp>)` (the export time recorded in
  `index.json`; left out, with a WARN line, when it is not a valid date and time) and the Status
  box names the same source, so the offline view can never be
  mistaken for the live machine. A folder that does not exist, has no `index.json`, or whose
  `index.json` is not a baseline index (no `Variables` list, or a symbolic link) yields
  `Loading failed: <reason>` in Status, never an exception, and so does a folder larger than any
  baseline can be: an `index.json` above 4 MiB (4,194,304 bytes), more than 4,096 entries, or
  entries whose sizes add up to more than 64 MiB (67,108,864 bytes); nothing of such a folder is
  read. An entry whose `vars` file is missing,
  does not match the index (length, SHA-256), is larger than 1 MiB or is a symbolic link or
  junction is skipped with one WARN line in the log panel and the rest of the baseline loads.
  `Open baseline folder` stays available while `Refresh` is reading; a folder picked then replaces
  that read.

Everything else (the filter, grouping and sorting, the list and its group headers, the detail
pane, `Copy value`, `Export visible list`, the log panel, `Copy log`, `F6` between regions,
`Ctrl+F`, `Escape`) is in `docs/Keyboard-Map.md`, which also lists every rule that moves focus.
No focus moves on a timer, nothing announces on its own, and the application cannot change any
setting. The application always runs elevated, so it logs to the shared daily file (section 5)
with component `app`; the log panel shows the last 200 lines of the current run.

The window has a seventh region, `Dell settings`, below the log panel. Nothing
in it is read until `Read Dell settings` (`Alt+S`) is pressed: it then reads this computer's agreed
Dell BIOS settings through Dell Command | Monitor (Dell Command | Configure when that gives no
answer), shows them in the list `Dell BIOS settings`, and changes nothing. On a machine without the
Dell software it says so in one sentence (`docs/Dell-Interface-Setup.md`, section 4). A baseline
folder with a `dell-settings.json` shows the settings it recorded instead, labeled as recorded.
`docs/Keyboard-Map.md` has its keys and focus rules.

To run the application on a machine without the SDK, copy the whole
`src\UefiAccess.App\bin\Release\net8.0-windows` folder (not the `.exe` alone) to that machine
and install the ".NET Desktop Runtime 8.0 (x64)" there with its installer. There is no installer
for UEFI-Access itself: installer packaging is out of scope.

### The input trace and the UI Automation recorder

The application can write a trace of what happens in its window, and a separate recorder writes the
UI Automation events that another process receives from that window. Both are opt-in and write
nothing but their trace files (and the recorder's start and stop lines in the day's log).

`UefiAccess.App.exe --trace-input`. The switch is the one command-line argument the
application reads (in any case); any other argument changes nothing, and without the switch no
hook is attached, no file is created and no thread is started. With it the application creates
`trace-input-<yyyyMMdd-HHmmss>.log` in the folder of its log, after the same folder checks as the
log, without overwriting anything (a second trace in the same second gets `-2` to `-9` before
`.log`), and names the file in an INFO line of the log panel and the log file, never in speech;
when no file can be created, a WARN line, `Input trace not started: <reason>`, and the application
runs on without it. A line is stamped and queued on the thread where the event happens, and one
background thread, below normal priority, formats, redacts and writes the lines in order with a
blocking consumer: no timer, no polling. At exit, or when an unhandled exception ends the process,
a count per kind and `input trace: stopped` are written and the file is flushed and closed. Because
the detail pane is updated in place (finding `A2-7`), the `DETAIL` line of a
finished update, `rebuild finished in <ms> ms: row <name>, <n> sections` (`direct rebuild` when a
request to bring the pane up to date finds nothing pending but the pane shows another row), goes on
with the counts of that update (`DetailPaneUpdate`),
`; sections <k> kept, <a> added, <r> removed; fields <k> kept, <u> updated, <a> added, <r> removed`,
for example `sections 5 kept, 0 added, 0 removed; fields 21 kept, 4 updated, 0 added, 0 removed`;
the step keeps the name rebuild, so that traces from before and after the change compare.

`uefiaccess-uia-trace.exe`, run from an elevated prompt (UEFI-Access runs elevated, and a
client that is not elevated receives none of its events; without elevation the recorder refuses
and records nothing), before the application is started:

```
src\UefiAccess.UiaTrace\bin\Release\net8.0-windows\uefiaccess-uia-trace.exe [--out <path>] [--events <list>]
```

It waits for the UEFI-Access window, subscribes with the managed UI Automation client (an MTA
program) to focus changes, the SelectionItem events, structure changes, live region changes,
notifications, asynchronous content loaded and changes of `Name`, `Value`, `IsSelected`,
`HasKeyboardFocus` and `ExpandCollapseState` on the window's subtree, and writes one `UIA-RECV`
line per event until the application's process exits or `Ctrl+C` is pressed, then a count per
event. Its handlers read only what UI Automation cached with the event, never a live property, so
they never wait on the application's UI thread. `--out <path>` is the file to create, or an
existing folder to create it in, as a full path (a relative one is refused: an elevated prompt
starts in `C:\Windows\System32`); without it the file is `uia-trace-<yyyyMMdd-HHmmss>.log` in the
shared log folder, or in the per-user one when the shared folder is refused or cannot be written.
An existing file is never overwritten. `--events <list>` takes `focus`, `selection`, `structure`,
`live`, `notification`, `async` and `properties`, separated by commas, or `all`, the default.
`--help` (`-h`, `/?`) shows the usage. An option and its value are two arguments, and an unknown
argument is refused. Exit codes: 0 recorded (or the help shown), 1 an unexpected error, 2 the
command line could not be read, 3 not elevated, 4 no trace file could be created or writing it
failed. The recorder sends no input, has no keyboard hook, sets no focus and changes nothing; its
subscriptions do make WPF raise some events it otherwise skips, so a trace with `--trace-input`
alone is the comparison.

The cache is filled when the event reaches the recorder, so the name on a `UIA-RECV` line is the
element's name at arrival. A `LiveRegionChanged` event carries no text, and after a variable list
load the window's events can reach other processes late, by up to several seconds, so the Status
box's name on such a line can already be a later sentence.
A `LiveRegionChanged` line therefore also gives, before its element,
`sentence="..."`: the element's value as the last `PropertyChanged Value` with a value before the
event gave it, written as that line writes a value (`sentence=(unknown)` when none came, as with
`--events` without `properties`). It adds `copy=yes` when the event came right after a
`PropertyChanged Value=(none)` of the same element: once Open baseline folder (`Alt+O`) has opened
its dialog, even if cancelled, most events of the window reach another process a second time, the
copy without a value (in those runs the Status box's value, name and live-region events among
them; not the focus events, and not the baseline load's own `Loaded ...` sentence). The summary
counts those copies on a `LiveRegionChanged copies` line, right after `LiveRegionChanged` and part
of it, so the received count less the copies can be set against the application's `UIA-RAISED`
lines for the time the recorder listened. The name stays on the line as before.

Both files have the same `# ` header lines and the same line format: six fields separated by tabs,
the local time `HH:mm:ss.fff` (the form of NVDA's log), the milliseconds since the trace started
with one decimal, the raw `Stopwatch.GetTimestamp()` value (QPC, the same clock in every process,
so the two files merge exactly when sorted by this field), `T` and the managed thread id, the event
kind, and the details (a `LiveRegionChanged` line's `sentence=` and `copy=yes` are part of its
details). They are UTF-8 without a byte order mark, with CRLF line ends. The service
tag (`Win32_BIOS.SerialNumber`, read into memory once, through the bounded WMI query of the machine
identity) is replaced by `[service tag]` in every line, and so is the tag of every baseline the
application loads; the recorder also replaces whatever follows a `Service tag:` label. No firmware
value reaches either file beyond what a row's accessible name already holds.

## 5. Log and baseline directories

| What | Windows | Non-Windows host (build and test only) |
|---|---|---|
| Data root | `%ProgramData%\UefiAccess` (normally `C:\ProgramData\UefiAccess`) | `UefiAccess` under the .NET local application data folder: `~/.local/share/UefiAccess` on Linux (`$XDG_DATA_HOME/UefiAccess` when that variable is set), `~/Library/Application Support/UefiAccess` on macOS |
| Logs of elevated runs (the application; the CLI from an administrator prompt) | `%ProgramData%\UefiAccess\logs\uefiaccess-<yyyyMMdd>.log` | `logs/` under the data root |
| Logs of runs that cannot write the shared folder (the CLI without elevation, or an elevated run whose data root is refused, below) | `%LOCALAPPDATA%\UefiAccess\logs\uefiaccess-<yyyyMMdd>.log` (normally `C:\Users\<name>\AppData\Local\UefiAccess\logs`) | the same folder as above |
| Baselines | `%ProgramData%\UefiAccess\baseline\<yyyyMMdd-HHmmss>\` | `baseline/` under the data root |
| Gate output from `Run-Gate.ps1` | `%ProgramData%\UefiAccess\gate\<yyyyMMdd-HHmmss>\` | not applicable |
| Trace files, only when asked for (section 4a) | `trace-input-<yyyyMMdd-HHmmss>.log` (the application with `--trace-input`) in the folder of its log; `uia-trace-<yyyyMMdd-HHmmss>.log` (`uefiaccess-uia-trace`) in `%ProgramData%\UefiAccess\logs`, the per-user log folder when that one cannot be used, or where `--out` says | not applicable (both programs need Windows) |

If the special folder cannot be resolved the tool falls back to the temp directory.

The data root is protected. The first elevated run creates `C:\ProgramData\UefiAccess` with an
explicit, protected access list: SYSTEM and Administrators full control, Users read and execute,
inherited by everything inside and not inheriting ProgramData's "Users may add files" entry. Every
folder an elevated run creates inside it (`logs`, `baseline`, each run folder, and the folders of
a `gate --out` inside it such as `gate\<yyyyMMdd-HHmmss>`) gets the same list; an existing
folder's access list is never changed. Before it writes, an elevated run checks the data root and
every existing folder down to the one it writes, and refuses a folder that is a junction, symbolic
link or other reparse point, or that is owned by anyone other than SYSTEM, Administrators or
TrustedInstaller, because a standard user who owns a folder could redirect what the elevated tool
writes there. The elevated run's own account is not trusted as an owner either: under the
non-default policy "System objects: Default owner for objects created by members of the
Administrators group" = "Object creator" that account would own the folders, and its programs
running without elevation could then redirect them. The folders an elevated run creates are
therefore given Administrators as their owner explicitly. The check looks at the owner only,
never at the access list. In a data root created
this way, standard users can read `baseline` and `logs` but not add or delete files there: copying
a baseline folder into `%ProgramData%\UefiAccess\baseline` from Explorer asks for administrator
approval, and `Open baseline folder` works from any folder anyway.

A process without elevation never creates anything under `%ProgramData%`. When the shared log
folder is missing or cannot be written, the run logs to `%LOCALAPPDATA%\UefiAccess\logs` instead,
and the first line of that run in the per-user log is a `[WRN]` line:
`Note: the shared log folder C:\ProgramData\UefiAccess\logs cannot be written, so this run logs to C:\Users\<name>\AppData\Local\UefiAccess\logs.`
Nothing is written to standard error for the move, so a command's own first message (for example
the sentence that refuses a command without elevation) is the first thing a screen reader reads.
Only when no log file can be kept at all does standard error get one line:
`Note: neither the shared log folder <shared> nor <per-user folder> can be written, so this run keeps no log file.`
The command still runs.

An elevated run that logs to the per-user folder checks it for links. `%LOCALAPPDATA%`,
`%LOCALAPPDATA%\UefiAccess` and its `logs` folder belong to your account, so before an elevated
run that falls back to the per-user folder writes its log there, it checks that each is a plain folder and
that no `uefiaccess-*.log` file in it is a symbolic link. If one is a junction, symbolic link or
other reparse point, the run keeps no log file and standard error gets one line:
`Note: neither the shared log folder <shared> nor <per-user folder> is used, so this run keeps no log file: <folder> is a junction, symbolic link or other reparse point, not a plain folder, so a program running without elevation could redirect what this elevated run writes there. Rename or delete <folder>; the next run creates it again.` (for a log file that is a link, the reason reads `<file> is a symbolic link or other reparse point, not a plain file, ...`)
The command still runs. The check is made when the log is opened; a program of the same account
could still swap a folder after that moment, which is the boundary between an account's elevated
and non-elevated programs that Windows does not treat as a security boundary.

A data root made by an older build or an older test run. Some earlier builds created
`C:\ProgramData\UefiAccess` and its `logs` folder on their first run, elevated or not, and a
`dotnet test` run of them on Windows did the same (the logging smoke test). Such folders carry
ProgramData's inherited access list, under which any user may add files and folders, and they
belong to the account whose token created them. That owner decides what the elevated tool does,
for the data root and for each folder in it that a run writes through (`logs`, `baseline`); a
refusal names the folder it refused.

Created without elevation (an older build's CLI, or `dotnet test`, from a normal prompt): the
folder is owned by your own account, and the elevated tool refuses it. Its log then goes to the
administrator's `%LOCALAPPDATA%\UefiAccess\logs`, whose first line reads `Note: this run logs to <per-user folder>, not to the shared log folder C:\ProgramData\UefiAccess\logs: <reason>`,
and `uefiaccess baseline` into the default folder writes nothing and exits 5 with
`Export error: Cannot create export directory 'C:\ProgramData\UefiAccess\baseline\<run>\vars': C:\ProgramData\UefiAccess is owned by <DOMAIN\account>, not by Administrators or SYSTEM, so a standard user could redirect what this elevated run writes there. Rename or delete C:\ProgramData\UefiAccess from an administrator prompt; the next elevated run creates it again with safe permissions.`
The application's start-up log line (the first line of its log panel) and its crash message name
that per-user folder, and the log panel's next line is the same note as a WRN line. The fix, in an
elevated PowerShell:

```
if (Test-Path C:\ProgramData\UefiAccess) { Rename-Item C:\ProgramData\UefiAccess UefiAccess.old }
```

(or delete the folder). The next elevated `uefiaccess` command or application start creates it
again with the protected access list.

Created while elevated (an older build's application, which always ran elevated, or its CLI or
`dotnet test` from an administrator prompt): the folder is owned by Administrators, the elevated
token's default owner, so the tool accepts it and keeps its access list, ProgramData's inherited
one, as it is. On a machine whose owner policy is "Object creator" such a folder is owned by the
administrator's own account and is refused like the one above. Otherwise nothing is refused and
nothing says so. Rename it the same way if you want the protected list. `(Get-Acl C:\ProgramData\UefiAccess).Owner` shows the owner, and
`icacls C:\ProgramData\UefiAccess` shows the protected list as exactly three entries,
`NT AUTHORITY\SYSTEM:(OI)(CI)(F)`, `BUILTIN\Administrators:(OI)(CI)(F)` and
`BUILTIN\Users:(OI)(CI)(RX)`, none marked `(I)` (inherited).

The one policy setting that matters: where the local security policy "System objects: Default
owner for objects created by members of the Administrators group" is set to "Object creator", the
elevated token's default owner is the administrator's own account. The tool does not trust that
account as an owner, so a folder it created, elevated or not, is refused; the folders the tool
creates itself are given Administrators as their owner explicitly and pass the check under either
policy.

Logging: one file per day, shared between processes, minimum level Debug. Every native call is
logged with the API name, its arguments (variable name, GUID, sizes), the result, the Win32 error
or NTSTATUS, and the elapsed time, for example:

```
2026-09-10 14:30:12.123 +02:00 [DBG] [cli] GetFirmwareEnvironmentVariableExW(BootOrder, {8BE4DF61-93CA-11D2-AA0D-00E098032B8C}, size=65536) -> 8 bytes, attributes 0x7, error 0, 0.4123 ms
```

With `--verbose` (`-v`) the same events are also written to standard error as `[DBG] ...` and
`[INF] ...` lines without the timestamp and component; without it nothing from the log reaches the
console.

No secrets are logged; no BitLocker key material is ever read.

A baseline folder contains:

```
index.json          ToolVersion, TimestampUtc, TimestampLocal, FirmwareType, PrivilegeState,
                    Identity (machine identity), VariableCount, and one entry per variable in
                    Variables: { Key, Name, Guid, Attributes, AttributeNames, Size, Sha256, File }
vars/<guid>_<name>.bin    raw data, one file per variable; names sanitised to [A-Za-z0-9_.-]
decoded.json        boot entries, boot order, Secure Boot state, OsIndicationsSupported
platform.json       TPM, BitLocker, Secure Boot policy, Dell interface probe results
dell-settings.json  the agreed Dell BIOS settings of one read through Dell
                    Command | Monitor (never Dell Command | Configure), as Dell's codes and names,
                    or the causes when it gave no answer; the application shows it offline
log.txt             the run log (the lines of this run that went to the daily log file), followed
                    by the exporter's outcome block: the line "Baseline export outcome", the number
                    of variables indexed, the files written before log.txt (one per line) and the
                    export errors (one per line); written last, so it reflects what the export produced
```

`index.json` is deterministic in its entries: they are sorted by key, with a fixed property order,
a SHA-256 per variable and no per-entry timestamps, so the `Variables` array of `index.json` and
every `vars/*.bin` file are byte-identical for an unchanged store. The header (`ToolVersion`,
`TimestampUtc`, `TimestampLocal`, `Identity`) differs per export, so two whole `index.json` files
never compare byte for byte; compare the `Variables` array or the per-variable `Sha256` values
instead. Exporting the same request twice (same timestamp) gives the same bytes. The JSON
files of the folder (four) use PascalCase member names, as shown;
`gate.json` and every `--json` document use camelCase (`docs/CLI.md`, sections 3 and 4). The
application's `Open baseline folder` reads `index.json` and the `vars` files (section 4a), and
`dell-settings.json` for its `Dell settings` region; `decoded.json` and
`platform.json` are for people and for M3.

`tools/sample-baseline/` is one such folder committed to the repository: 13 variables built from
the test fixtures (the four `Boot####` load options, `BootOrder`, `BootNext`, `Timeout`,
`SecureBoot`, `SetupMode`, `PK`, `KEK`, `db`, `dbx`), a fixed timestamp and an identity that
reads `Sample (fixtures)` in every field, and a `dell-settings.json` with the ten
Dell rows of the A18 export, its machine fields reading `Sample (fixtures)` too. `tests/UefiAccess.Core.Tests/Presentation/SampleBaselineGeneratorTests.cs`
asserts on every `dotnet test` that the folder still loads through `BaselineFolderSource` with
zero warnings and matches what the exporter produces; run that test with the environment
variable `UEFIACCESS_WRITE_SAMPLE_BASELINE=1` to regenerate the folder after a fixture change.

## 6. Running the hardware checks

The hardware checks run only on the target machine and are not unit tests. They live in
`tools/gate/Run-Gate.ps1`, which runs `uefiaccess info`, `list`, `boot`, `secureboot`, `baseline`,
and `gate` (every one of them read-only), captures all output into a timestamped folder, checks
what can be checked automatically (exit codes, variable count above zero, BootOrder present,
baseline folder complete, `gate.json` written and parseable), and then prints the manual
checklist: compare the boot entries with the F12 boot menu, compare the Secure Boot state with
msinfo32, confirm the baseline folder, and note the BIOS version and boot mode for the memo.
The script sets `UEFIACCESS_HW=1` in the environment of the `uefiaccess.exe` processes it starts,
the marker reserved for hardware checks. No product code reads it, so it changes nothing about what
the commands do; it only tags those processes as a gate run (the script's `.NOTES` header says the
same).

On the Dell, from an elevated Windows PowerShell 5.1 prompt in the repository root:

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools\gate\Run-Gate.ps1
```

With the varstore list from gate question (c) and a custom output folder:

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools\gate\Run-Gate.ps1 -IfrVarstores C:\work\ifr-varstores.txt -OutputRoot C:\work\gate
```

Parameters: `-ExePath` (default `..\..\src\UefiAccess.Cli\bin\Release\net8.0-windows\uefiaccess.exe`
relative to the script), `-IfrVarstores` (optional; passed through as `--ifr-varstores`),
`-OutputRoot` (default `%ProgramData%\UefiAccess\gate`). The script refuses to run without
elevation and stops after `info` when it reports legacy BIOS boot (exit 3) or a missing privilege
(exit 4), because nothing else can succeed then. The output folder is always new. Inside
`C:\ProgramData\UefiAccess` the script follows the tool's rule for that folder (section 5). When
the folder is missing, it runs `uefiaccess info` once first, with the output discarded, so that
the tool creates it with the protected access list. It creates `gate` and the timestamped folder
with that list. It stops with exit 2 and one sentence, naming the folder to rename or delete from
an administrator prompt, when an existing folder on the way is a junction or other link, when one
is owned by any account other than Administrators, SYSTEM or TrustedInstaller, or when the
timestamped folder already exists. A folder outside the data root, given with `-OutputRoot`, only
has to be new. Its evidence files are UTF-8 without a byte order mark, and its folder and file
dates are Gregorian whatever the Windows region setting.

The full gate procedure covers (a) boot mode, (b) Dell interface, (c) firmware image and IFR, and
(d) BitLocker and Secure Boot policy; `tools/gate/ifr-varstores.example.txt` shows the input file
for (c).

## 7. Troubleshooting

| Symptom | Cause and fix |
|---|---|
| `NETSDK1100: To build a project targeting Windows on this operating system, set the EnableWindowsTargeting property to true` | You are building a project outside this solution's `Directory.Build.props`. Build `UefiAccess.sln` from the repository root. |
| `error CS1591: Missing XML comment for publicly visible type or member` | Every public type and member under `src/` needs a doc comment (including record positional parameters via `<param>`). Add it. |
| `uefiaccess` exits with 3 | The machine booted in legacy BIOS mode (gate question (a)). Convert the disk first, from the running Windows in an elevated prompt: suspend or turn off BitLocker before anything else (save the recovery key; a BitLocker volume makes the conversion fail), then `mbr2gpt /validate /allowFullOS` and `mbr2gpt /convert /allowFullOS` (without `/allowFullOS`, MBR2GPT refuses to run outside Windows PE). Only then switch the firmware's "Boot List Option" to UEFI; switching first leaves "no boot device". Or reinstall Windows in UEFI mode. (The reference OptiPlex 7020 already boots in UEFI mode.) |
| `uefiaccess` exits with 4 | Not elevated (`This command needs administrator rights; open an elevated command prompt (Run as administrator) and run uefiaccess again.`), or elevated but `SeSystemEnvironmentPrivilege` could not be enabled. Open the prompt with "Run as administrator". |
| `uefiaccess baseline` exits with 5 and prints `Export error: Cannot create export directory '...': C:\ProgramData\UefiAccess is owned by ...` (or `... is a junction, symbolic link or other reparse point ...`) | The data root was made without elevation, by an older build or an older `dotnet test` run, so it belongs to that account (a root an older build made while elevated belongs to Administrators and is accepted), or it was replaced by a link. Rename or delete `C:\ProgramData\UefiAccess` from an elevated prompt (section 5); the next elevated run creates it again with the protected access list. |
| `uefiaccess gate` exits with 5 and prints `Writing '...\gate.json' failed: C:\ProgramData\UefiAccess\gate is owned by ...` (or `... is a junction, symbolic link or other reparse point ...`) | The same data-root check as `baseline` refused the `--out` folder; nothing was written, and the table on standard output still holds the findings. Rename or delete the folder the message names from an elevated prompt (section 5) and run the gate again. |
| `Run-Gate.ps1` stops with exit code 2 before its first command, with a sentence that names a folder under `C:\ProgramData\UefiAccess` (`... is owned by ...`, `... is a junction, symbolic link or other reparse point ...` or `The output folder ... already exists ...`) | The script applies the tool's data-root check to its own output folder (section 6); none of the checks was run (only the quiet `uefiaccess info` that creates a missing data root may have run). The fourth sentence, `"uefiaccess info" (exit code N) did not create the data folder ...`, means that first `info` failed: run `uefiaccess info` from the same prompt to see why. Rename or delete the folder the sentence names from an administrator prompt and run the script again. |
| `uefiaccess` exits with 6 | The command completed, but with `Warning:` lines on standard error: the enumeration stopped early at a malformed entry, the known-names fallback ran or one of its reads failed (an unreadable variable, for example larger than 4 MiB), an identity WMI query failed (`info`), a Secure Boot policy source failed (`secureboot`: registry, PowerShell not runnable, the 10-second timeout, or unparsed output), or the export reported errors but wrote some files (`baseline`). Read the `Warning:` lines; `docs/CLI.md` section 2 lists them. |
| No log file appears under `%ProgramData%\UefiAccess\logs` | A run without elevation, or an elevated run whose data root was refused, logs to `%LOCALAPPDATA%\UefiAccess\logs` instead (for an elevated run, the administrator's own folder); the first line of that run there says why (section 5). Only when neither folder can be written, or an elevated run finds a link in the per-user folder, does standard error say `Note: ... so this run keeps no log file` (with the reason); the command still runs. |
| `uefiaccess.exe` prints "You must install or update .NET to run this application.", or `UefiAccess.App.exe` shows that message in a dialog | The machine has no machine-wide .NET 8 runtime (x64); a per-user SDK install or a .NET 9 or 10 runtime does not count. Install the .NET 8 SDK x64 with its installer, or the ".NET Desktop Runtime 8.0 (x64)" (section 1). |
| Windows blocks `UefiAccess.App.exe` or `uefiaccess.exe` with no "Run anyway" (`An Application Control policy has blocked this file. (0x800711C7)`) | Smart App Control. Rebuild (every build is a new file that Windows judges afresh), or weigh it carefully before switching Smart App Control off, which cannot be undone without reinstalling Windows. |
| NVDA says nothing in the application's window | A portable NVDA cannot read an elevated application's window. Use the installed copy of NVDA. |
| `dotnet test` fails on Linux or macOS before any test runs, with a message about `Microsoft.WindowsDesktop.App` not being found | The test project references the WPF application (`UefiAccess.App`). It must reference `UefiAccess.Presentation` instead; everything testable lives there (section 3). |
| `dotnet test` on an Apple Silicon Mac or arm64 Linux stops with "Could not find 'dotnet' host for the 'X64' architecture" | The test project is built for x64 (`PlatformTarget` in `Directory.Build.props`). Install the x64 .NET 8 runtime next to the arm64 SDK (section 1). |
| `dotnet test` on Windows fails 21 of the 23 tests of `RunGateOutputFolderTests` and `RunFailureCasesOutputFolderTests` | They run the scripts' functions in Windows PowerShell 5.1, and the scripts import `tools\common\DataFolder.psm1`, which the execution policy `Restricted` refuses (on the unit every scope is `Undefined`, so `Restricted` applies). Set `$env:PSExecutionPolicyPreference = 'Bypass'` in that prompt before `dotnet test` (`docs/Developer-Guide.md`, section 2). |
| `dotnet test` from PowerShell 7 fails 12 of those tests with `The owner of ... cannot be read` | Windows PowerShell 5.1 inherited PowerShell 7's `PSModulePath` and loaded PowerShell 7's modules. The tests remove the variable for the child process. If you hit this, run from cmd or Windows PowerShell 5.1, or first set `$env:PSModulePath` to Windows PowerShell's three module folders (`docs/Developer-Guide.md`, section 2). |
| `UefiAccess.App.exe` shows the UAC prompt even though you only want to open a baseline folder | By design: the manifest requests `requireAdministrator` once for the whole process, and there is no unelevated mode in M2. |
| `UefiAccess.App.exe` does not start on a machine that has only the ".NET Runtime 8.0" | WPF needs the ".NET Desktop Runtime 8.0 (x64)". Install it, or copy the SDK-built folder to a machine that has the SDK installed machine-wide. |
| `Refresh` says `This system did not boot in UEFI mode ...` | The machine is still in legacy boot mode; see the exit-3 row above and gate question (a). `Open baseline folder` works meanwhile. |

## 8. The other documents

- `docs/CLI.md`: the full `uefiaccess` command-line reference.
- `docs/Keyboard-Map.md`: every key, region and focus rule of the application.
- `docs/Developer-Guide.md`: architecture, the safety model, and how to extend the tool.
- `docs/Dell-Interface-Setup.md`: what the Dell settings view needs on a machine, how to check each
  part read-only, and what the tool says for each missing part.
- `docs/Dell-Attribute-Map.md`: the ten Dell BIOS settings the tool shows, with their attribute
  names, source interfaces, values and verification; how to add a setting.
- `docs/Recovery.md`: what to do if the machine does not start normally after a boot-order change.
