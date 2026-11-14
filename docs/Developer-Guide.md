# UefiAccess developer guide

This page is the way in for a developer taking UefiAccess over: how to build, test and run it, how
it is put together, what it reads and can change, how its safety model works, and how to extend it.
It names the code's own types, methods, tests and files, and links to the detailed documents rather
than repeating them. Where this page and the code disagree, the code is right.

A first day's reading: this page; [README.md](../README.md); [BUILD.md](BUILD.md);
`src/UefiAccess.Core/Safety/README.md` (the short form of the write pipeline); the three rules at
the top of [Keyboard-Map.md](Keyboard-Map.md).

## 1. What the tool is and is not

UefiAccess is a Windows tool (C# 12, .NET 8, x64, WPF) that lets blind and low-vision users read the
UEFI firmware variables of their machine, and change two of them, through stock user interfaces that
NVDA and Narrator read. It is validated on one machine, the 2014 Dell OptiPlex 7020 with BIOS A18
(see the README's supported-hardware note). An installer, code signing, JAWS support and
localization are out of scope.

What it does:

- **Reads everything Windows can see.** Every UEFI variable with the runtime-access attribute (`RT`;
  boot-service-only variables are invisible to any Windows tool), with attributes and bytes. It
  decodes the boot variables, the Secure Boot state, `OsIndications` and Dell's `NetworkStackVar`
  (section 4), reports the platform (firmware type, elevation and privilege, identity, TPM,
  BitLocker, Secure Boot policy, Dell interfaces), writes timestamped baselines of the whole store
  and opens any baseline offline.
- **Shows ten Dell BIOS settings and changes none of them**, read through Dell Command | Monitor's
  WMI classes, or Dell Command | Configure's command line when that gives no answer.
- **Changes exactly two variables, through one pipeline.** BootOrder, put in a new order of its own
  entries, and BootNext, set to one existing, active boot entry or removed; "Restore previous value"
  puts a snapshot's value back the same way. As a stage of such a confirmed change, and only when
  the person ticks it, it suspends BitLocker for one restart. Changes are enabled only on a machine
  whose Windows installation matches the configured enabled unit (section 5.2).

What it never does, in any build: change a Secure Boot key or Secure Boot's state (report-only);
add, remove or edit a Boot#### entry or delete BootOrder; change a Dell BIOS setting; write the
registry, the boot configuration (BCD) or the TPM; flash firmware; resume BitLocker, call any
BitLocker method but three readers and the one suspend, or read key material; start or stop a
service or install software; start a process other than through its three pinned launchers (the Dell
probe's two cctk options, PowerShell's `Confirm-SecureBootUEFI`, the Dell reader's nine cctk read
options).

## 2. Build, test and run

[BUILD.md](BUILD.md) has the full details, reasons and a troubleshooting table; [CLI.md](CLI.md) has
every command, option, exit code and JSON document.

- **To build and test:** the .NET 8 SDK x64 (8.0.400 or later). NuGet packages restore during the
  build (`System.Management`, `Serilog` with its file and console sinks, `System.CommandLine`;
  `xunit`, `xunit.runner.visualstudio` and `Microsoft.NET.Test.Sdk`); no native DLL, no driver. The
  solution also builds and unit-tests on x64 Linux (`EnableWindowsTargeting`). Build from the
  repository root: `dotnet build UefiAccess.sln -c Release`. It must end with `0 Warning(s)` and
  `0 Error(s)` (`TreatWarningsAsErrors`); projects under `src/` generate XML documentation, so a
  public member without a doc comment (CS1591) fails the build.
- **To test:** `dotnet test UefiAccess.sln -c Release`. The suite needs no firmware, no Dell software
  and no elevation: it runs on fakes, recorded fixtures and static scans, writes only into temporary
  folders, never constructs the real writer or suspender
  (`WriteCallSiteGuardTests.TestsNeverConstructTheRealWriter`) and never reads Dell Command | Monitor
  or starts cctk on Windows (`DellSettingsGuardTests.TestsNeverRunTheRealDellReader`), so it is safe
  to run on the target machine itself. Set `$env:PSExecutionPolicyPreference = 'Bypass'` in the
  prompt first, for the Windows PowerShell harness tests. On Windows a run skips the tests that only
  mean something off Windows (`[NonWindowsFact]`) and the symbolic-link tests (which need the "Create
  symbolic links" privilege).
- **To run:** Windows 11 x64 (or Windows 10 22H2) with the **machine-wide** .NET 8 runtime, the
  ".NET Desktop Runtime 8.0 (x64)" for the application. Live reads need UEFI boot and elevation. The
  command-line tool needs an elevated prompt; the application has an `app.manifest` with
  `requireAdministrator`, so the UAC prompt comes at every start. `tools\sample-baseline` (13
  synthetic variables and ten Dell rows, identity `Sample (fixtures)`) lets anyone walk every screen
  on any PC, offline.

The version is `0.1.0+<commit>`, read by the tools from
`UefiAccess.Core.Diagnostics.ToolVersion.Current`; `info`, `gate.json` and `index.json` carry it,
and the application's log panel names it. A tree with uncommitted changes shows no commit marker.

## 3. Architecture overview

### 3.1 The projects

| Project | What it holds | References |
|---|---|---|
| `src/UefiAccess.Core` | Everything that touches the machine, no UI: `Native` (P/Invoke declarations only), `Privileges`, `Variables` (the store), `Decoders`, `Baseline`, `Platform` (inspectors, `WmiHelper`, `ProcessRunner`), `Safety` (whitelist and write pipeline), `Settings` and `Settings/Dell` (the read-only Dell provider), `Diagnostics` (logging, error texts, version, trace files), `Text` | nothing in the solution |
| `src/UefiAccess.Cli` | `uefiaccess.exe`: one class per command in `Commands/`, the composition root `Services/CliPlatform.CreateWindows`, `Services/CommandRunner` (preconditions, one JSON document, exit codes), `Output/` (aligned tables, JSON, console encoding) | Core |
| `src/UefiAccess.Presentation` | The application's logic without WPF: `ViewModels/`, `Services/` (sources, write service), `Accessibility/`, `Diagnostics/` (the input trace), and every user-visible string of the window (`Resources/Strings.cs`) | Core |
| `src/UefiAccess.App` | The WPF window: `MainWindow.xaml` and its code-behind (focus, live region, keys), `ConfirmChangeWindow.xaml`, `App.xaml.cs` (composition), and in `Platform/` the WPF implementations of Presentation's interfaces, `DispatcherDetailScheduler` (the one timer), `AnnouncingGridSplitter` (the one custom automation peer) and the trace hooks | Core, Presentation |
| `src/UefiAccess.UiaTrace` | `uefiaccess-uia-trace.exe`, a console recorder of the UI Automation events the window raises (finding A2-7); reads only, sends no input | Core |
| `tests/UefiAccess.Core.Tests` | Every test (xUnit): unit tests over fakes and fixtures, static guards, and XAML tests; `Presentation/` and `Recorder/` hold the application's and the recorder's | Core, Cli, Presentation; the recorder's `Recording/*.cs` as linked files |

**Why Presentation has no WPF.** A test project that references a WPF application builds anywhere,
but its test host then needs the Windows Desktop runtime, which exists only on Windows, so
`dotnet test` would stop before running anything elsewhere. Everything testable therefore lives in
`UefiAccess.Presentation`, a plain class library (no `UseWPF`, no `PresentationFramework`,
`WindowsBase` or `System.Xaml`; `INotifyPropertyChanged`, `ObservableCollection` and `ICommand` come
from `System.ObjectModel`), and the App keeps only what needs WPF; its XAML and code-behind are
checked by static tests that read them as text (`WpfShellAccessibilityTests`, `WpfShellLayoutTests`,
`WpfShellLowVisionTests`, `WpfValueBoxArrivalTests`, `ConfirmChangeWindowTests`,
`DetailPaneStableBindingTests`). `ScaffoldSmokeTests.PresentationDoesNotReferenceWpfAssemblies` and
`AppReferencesCoreAndPresentationOnly` keep the split.

Rules that hold everywhere: Core depends on nothing above it; native declarations live only in
`Core/Native` and are reached through interfaces, so everything above them is testable without
firmware; malformed firmware data never throws (results carry an error instead); no variable layout
is invented; WMI goes only through `WmiHelper` and processes only through `ProcessRunner`, every call
bounded; the composition roots give their logger to every Core service they build
(`CompositionRootLoggingTests`).

```
 UefiAccess.App (WPF)        uefiaccess.exe (Cli)       uefiaccess-uia-trace.exe
        |                            |                             |
 UefiAccess.Presentation             |                             |
 (view models, sources, Strings)     |                             |
        +----------------------------+-----------------------------+
                                     |
 UefiAccess.Core
   read:    Native -> Variables -> Decoders -> Baseline;  Platform (WMI, registry, processes)
   change:  Safety (Whitelist, ChangeValidator, WriteTransaction, SnapshotStore, WriteRecord)
   Dell:    Settings/Dell (catalog, Dell Command | Monitor, cctk reader, reader with a budget)
   logs:    Diagnostics (Serilog, NativeError, ToolVersion, trace writer and redactor)
```

### 3.2 Reading: from the firmware to the window

1. **Preconditions.** `Platform/FirmwareTypeDetector` (`GetFirmwareType`) and
   `Privileges/PrivilegeManager`, which enables `SeSystemEnvironmentPrivilege` and reports
   `NotElevated`, `ElevatedPrivilegeAbsent` or `Enabled`. The CLI runs them through
   `Services/Preconditions` in `CommandRunner.Run`; the application runs them in `LiveFirmwareSource`,
   with the CLI's sentences for the UEFI and elevation cases.
2. **The store.** `Variables/WindowsFirmwareVariableStore` (`IFirmwareVariableStore`) reaches the
   native layer through the internal seam `INativeFirmwareApi`:
   `NtEnumerateSystemEnvironmentValuesEx` (class 2, parsed by `EnumerationBufferParser`) and
   `GetFirmwareEnvironmentVariableExW` (64 KiB, doubled up to 4 MiB); when the enumeration fails it
   reads the curated `KnownVariableNames` one by one (`KnownNamesFallback`, which always warns). Every
   native call is logged. A variable is a `FirmwareVariable` keyed `<guid>:<Name>`, a read a
   `VariableSet` (`IVariableLookup`); `InMemoryFirmwareVariableStore` stands in for baselines and
   tests.
3. **Decoders.** An `IVariableDecoder` says whether it `CanDecode` a variable and returns a
   `DecodedVariable` (summary, fields, JSON object, error). `DecoderRegistry.CreateDefault()` is the
   one registry of the CLI, the baseline and the window, so they never disagree (section 4.1).
4. **Baselines.** `Baseline/BaselineExporter` writes the folder of section 6; `Baseline/BaselineReader`
   reads one back, refusing links and oversized folders and checking each file's length and SHA-256.
5. **Platform inspectors** (`Platform/`, each an `IPlatformInspector<T>`): `MachineIdentity` (with the
   expected-model check), `TpmInfo`, `BitLockerInspector` (three read-only WMI methods),
   `SecureBootPolicyInspector` (registry, variables and, outside the write pipeline, PowerShell's
   `Confirm-SecureBootUEFI`), `DellInterfaceProbe`. WMI goes through `WmiHelper`, processes through
   `ProcessRunner.Run` (full path, no shell, no window, bounded, process tree killed on timeout).
6. **Sources** (`Presentation/Services`): `IVariableSource`, implemented by `LiveFirmwareSource`
   (preconditions, then enumeration and identity side by side, and on every load the unit check of
   section 5.2) and `BaselineFolderSource`; `NamespaceCatalog` gives each vendor GUID its spoken
   label.
7. **View models** (`Presentation/ViewModels`): `ShellViewModel`, the root and state machine (Idle,
   Loading, Loaded, Refused, Failed) owning the commands, announcements and focus requests;
   `VariableListViewModel` (filter, group, sort, one sentence per row, each row's verdict from
   `WritabilityPolicy.Describe`); `VariableDetailViewModel` (updated in place by `DetailPaneReconciler`);
   `LogViewModel`; `ChangeViewModel`; `DellSettingsViewModel`.
8. **The window.** `App.xaml.cs` calls `ShellViewModel.CreateWindows(...)` with `WpfAnnouncer`,
   `WpfFocusSink`, `WpfPlatformInteractions` and `DispatcherDetailScheduler`; `MainWindow.xaml` binds
   to the shell.

### 3.3 The accessibility architecture

- **Regions and F6.** Seven regions, stock `GroupBox` controls exposed as named groups, in F6 order:
  Toolbar, Filter, List, Detail, Log, Dell settings, Status. WPF on .NET 8 cannot mark UI Automation
  landmarks, so `F6` and `Shift+F6` are the region navigation, and arriving says `<Region> region`
  (arriving in Status says nothing, so the box keeps its message). Every focusable control has an
  explicit `TabIndex`, 1 to 27. Pinned by `WpfShellAccessibilityTests`.
- **One live region, one announcer.** The Status box is the only polite live region and the log panel
  is `LiveSetting="Off"`. Every sentence goes through `IAnnouncer.Announce`: `WpfAnnouncer` puts it
  into the Status box and raises one LiveRegionChanged event (none while the box itself has focus; its
  name change is read then); tests use `RecordingAnnouncer`.
- **Focus.** View models never touch focus: they ask `IFocusSink.RequestFocus(FocusTarget)`
  (`WpfFocusSink`; `RecordingFocusSink` in tests). Focus moves only on a key or when an action the
  person started ends, and a Dell answer moves it only while the person has not moved on. Every rule
  is a row of [Keyboard-Map.md](Keyboard-Map.md), section 9.
- **No timers, one pinned exception.** Nothing announces or moves focus on a timer. The only timer of
  the window's code is the detail pane's 300 ms quiet period (`DetailRefreshTiming.QuietPeriod`,
  finding A2-7): one `DispatcherTimer` in `DispatcherDetailScheduler.cs`, driven by the tested policy
  of `CoalescingDetailScheduler` and `QuietPeriodPoster`, saying nothing and moving no focus.
  `WpfShellAccessibilityTests.NoTimedBehaviorAndNoCliReference` forbids every timed form in the App
  and Presentation and pins the exception to that file.
- **Strings.** Every user-visible string of the window is a constant of `Resources/Strings.cs`, read
  by the XAML as `{x:Static res:Strings.Name}`. A sentence the CLI prints too is a constant of Core
  (`CheckSentences`, `TransactionSentences`, `WritabilitySentences`, `WriteStatusMapping`,
  `BitLockerPreflight`, `DellSentences`) that the window quotes; the precondition sentences are the
  CLI's word for word.
- **Wording and stock controls.** No control label holds Apply, Save, Toggle, Edit, Write or Set; no
  read-only box says "read-only" in its help text or name (finding A2-6); a row is one sentence of at
  most 200 characters, name first. A name on every control, help text on every input, no template
  overrides, system colors and font only, one custom automation peer. A list being read is updated
  in place, never rebuilt.

### 3.4 The write pipeline

Every change goes through `IWritePipeline` (`Safety/WritePipeline.cs`), one `WriteTransaction` per
request. Two callers: the CLI (`Commands/WriteCommand.cs`) and the window (`ShellViewModel` through
`IWriteService`/`WriteService`). `PrepareAsync` is the dry run, S0 to S10; `CommitAsync` is S11 to
S17; `CancelAsync` records a cancel.

| Stage | What happens |
|---|---|
| S0 | a `RestoreRequest` is resolved by `SnapshotStore.DeriveRestore` into one of the three operations |
| S1 | preconditions again: UEFI, elevated, privilege enabled |
| S2 | identity (2014 OptiPlex 7020 on A18) and enabled-unit check (section 5.2), recorded only |
| S3 | one fresh enumeration (`NtEnumerate` only) and direct reads of what the change touches |
| S4 to S6 | `ChangeValidator.Validate`, rules V1 to V17; the after-image encoded from entry numbers |
| S7 | Secure Boot and baseline warnings only |
| S8 | `BitLockerPreflight.Assess`; On, Encrypting and Unknown need a choice |
| S9 | both data roots prepared; the writes of this start counted |
| S10 | a `ChangePlan` with its `PlanHash`; one record line; nothing written |
| S11 | token, plan integrity, BitLocker choice, then the machine checks again |
| S12 | commit lock and budget again, `Pending` intent line, snapshot written and read back, first drift read |
| S13 | only when chosen: the one-restart BitLocker suspend, confirmed by a re-read |
| S14 | the second drift read, just before the call |
| S15 | `BuildWritePlan`, the last shape check, then the one call `IFirmwareVariableWriter.CommitOnce` |
| S16 | one verification read: `Verified`, `ApiRefused`, `ChangedDespiteError` or `Unverified` |
| S17 | `record.json`, one line of `write-log.jsonl`, one log event, sentence and exit code |

Each attempt leaves a `WriteRecord` (schema `uefiaccess-write-record/1`); `WriteRecordLog` appends it
to `write-log.jsonl` under a cross-process append lock, holds the cross-process commit lock from S12
to S17 and counts the write budget. Exit codes are Core's `WriteExitCodes`: 0 passed or verified, 3
and 4 the preconditions, 7 blocked before any call, 8 refused by the firmware with nothing changed, 9
a read-back the call does not explain.

### 3.5 The Dell settings provider

- **The model** (`Core/Settings`): `FirmwareSetting`, `SettingValue`, `SettingState` and
  `PossibleValuesOrigin`, a vendor-neutral settings model. A Dell read is a `DellSettingsResult` whose
  `DellMachine` holds the manufacturer, model and BIOS version only, never a serial number.
- **The list** is data: `DellAttributeCatalog.Default`, ten `DellCatalogEntry` rows (section 4.3).
- **The reader**, `DellSettingsReader` (`IDellSettingsReader.Read(DellReadOptions)`, never throws): the
  machine through `DellMachineReader`; `DcmSettingsProvider`, one pinned `SELECT` of
  `DCIM_BIOSEnumeration` in `root\dcim\sysman` within 60 seconds; when that gives no answer under the
  automatic preference, or with `--source cctk`, `CctkSettingsProvider`, one cctk run per row, one at
  a time, 15 seconds each; detection, never reading, of the native interface of newer models; all
  within one 120-second `DellReadBudget`. Values are mapped by Dell's names, never codes
  (`DellSettingMapper`); every sentence is a constant of `DellSentences`.
- **cctk** is started only by `CctkReader`: `DellInterfaceProbe.FindCctk` finds it under the two
  Program Files known folders, one of nine pinned read options per run.
- **Callers**: `uefiaccess dell`; `uefiaccess baseline` (into `dell-settings.json`, Dell Command |
  Monitor only); the window's `LiveDellSettingsSource` (on `Alt+S` only) and `BaselineFolderSource`.
- **`NetworkStackVar`** is not part of the Dell view: a UEFI variable decoded by `NetworkStackDecoder`
  in the variable list.

### 3.6 Diagnostics

- **Logging** (`Core/Diagnostics/Logging.cs`): `Logging.Create(component, console)` gives a Serilog
  logger on the shared daily file at level Debug; `NativeError` turns Win32 and NTSTATUS codes into
  the tools' texts. The CLI mirrors the log to standard error with `--verbose`. The window's log panel
  shows the last 200 lines (`LogPanelSink`).
- **The input trace**, `UefiAccess.App.exe --trace-input` (`InputTraceSwitch`, `InputTraceFile`,
  `WpfInputTraceHooks`; Core's `TraceFileWriter`, `TraceLine`, `TraceRedactor`): keys, focus moves,
  selection changes, the detail pane's scheduling and the UI Automation events the window raises,
  written by one background thread with a blocking consumer, no timer.
- **The recorder**, `uefiaccess-uia-trace.exe` (`UiaRecorder`, `Recording/`): the events another
  process receives, read from UI Automation's event cache only. Details: [BUILD.md](BUILD.md),
  section 4a.

## 4. Supported settings

### 4.1 What is read and decoded

Every runtime-visible variable is listed and read with its attributes and bytes. The decoders, in
`src/UefiAccess.Core/Decoders/`, registered in this order by `DecoderRegistry.CreateDefault()`:

| Decoder | Variables | What it shows |
|---|---|---|
| `BootOrderDecoder.cs` | BootOrder, BootNext, BootCurrent, Timeout | entries named and described, missing ones flagged; the timeout in seconds |
| `LoadOptionDecoder.cs`, with `DevicePathFormatter.cs` | every Boot#### | attributes and category, description, device path in efibootmgr style, optional data |
| `SecureBootStateDecoder.cs`, with `SignatureListReader.cs` | SecureBoot, SetupMode, AuditMode, DeployedMode, PK, KEK; db, dbx, dbt | the Secure Boot mode, presence and sizes, signature lists and types |
| `OsIndicationsDecoder.cs` | OsIndications, OsIndicationsSupported | the bit names |
| `NetworkStackDecoder.cs` | NetworkStackVar | the five fields of the A18 setup forms (IFR) |

Every other variable is shown raw (attributes, size, hex dump). Platform reads are not variables:
firmware type, identity, TPM, BitLocker, Secure Boot policy, Dell interfaces ([CLI.md](CLI.md)).

### 4.2 What can be changed, and where that is decided

| Operation | Variable | What is written |
|---|---|---|
| W1 `Reorder` | BootOrder | a permutation of its current entries, same byte count, attributes `0x7` (NV BS RT) |
| W2 `SetBootNext` | BootNext | 2 bytes naming an existing, active entry of category Boot, attributes `0x7` |
| W3 `ClearBootNext` | BootNext | size 0, the removal of the one-time boot |

A restore is one of the three, derived from a snapshot and checked against the firmware as it is now.
Four places decide:

1. **`Whitelist`** (`Safety/Whitelist.cs`): two `WhitelistEntry` values, BootOrder with
   `WritableKind.ReorderOnly` and BootNext with `WritableKind.OneShot`, both requiring `0x7`. Every
   other variable is read-only, whatever a caller asks.
2. **`WritabilityPolicy.Describe(...)`** gives each variable a `WritabilityVerdict` (the `Writability`
   line, the row's item status, the firmware-setup hint). The Change group shows only what the verdict
   allows; `uefiaccess write check` prints it.
3. **`PlatformPolicy`**: `IsContractedMachine` (expected model and BIOS `A18`) and `ReadUnit` with
   `EnabledUnit.Check` are recorded at S2 and refused at S11, so the dry run, the Change controls and
   the dialog work on any UEFI machine and the refusal comes at `Confirm change`.
4. **`ChangeValidator.Validate`** (S4 to S6) and **`WriteTransaction.BuildWritePlan`** (S15) judge each
   request against the fresh read.

### 4.3 The ten Dell rows

Boot list option, Secure Boot, SATA operation, TPM security, TPM activation (`not applicable` while
TPM security is not Enabled), Virtualization, VT for Direct I/O, Integrated NIC, Wake on LAN and Num
Lock at boot, checked against BIOS A18 only. All ten are read through Dell Command |
Monitor, nine through the fallback (never Boot list option), none can be changed. Names, codes,
values and setup locations per row: [Dell-Attribute-Map.md](Dell-Attribute-Map.md).

## 5. Safety model

The README's "Write guarantee" is the promise and its "How that is enforced" the list of scans.
Below, each safeguard with its code and the tests that pin it (in `tests/UefiAccess.Core.Tests`, the
Safety ones in `Safety/`).

### 5.1 The safeguards

`WTT` is `WriteTransactionTests` and `CWC` is `CliWriteCommandTests`, whose `FC<n>_*` tests are the
failure cases.

| Safeguard | Where in the code | Pinned by |
|---|---|---|
| Whitelist at the Core boundary | `Safety/Whitelist.cs`, consulted at S4, again at S15 and, for a restore, at S0 | `WhitelistGuardTests`, `WhitelistTests`, `DefenceInDepthTests` |
| No bytes from a caller | requests carry entry numbers or a snapshot folder; the pipeline encodes every byte (`BootListCodec`); `WritePlan`'s one constructor is internal and refuses attributes other than `0x7` | `WhitelistGuardTests.NoChangeRequestCarriesBytes`, `WriteCallSiteGuardTests.CommitOnceHasExactlyOneCaller` |
| Format validation | `ChangeValidator` (rules V1 to V17), `CheckNames`, `CheckSentences` | `ChangeValidatorTests`, `BootListCodecTests`, `WTT.EveryValidationRuleThatRefuses_LeavesTheWriterUncalled` |
| A fresh, complete read | `ChangeValidator.IsCompleteEnumeration` (`NtEnumerate` only); a pipeline over the real writer refuses any store but `WindowsFirmwareVariableStore` | `DefenceInDepthTests.TheRealWriter_OverAStoreThatIsNotTheFirmwares_IsRefused` |
| Dry run | `PrepareAsync` (S0 to S10) writes nothing: `write` without `--confirm`, and the first half of every Change button | `WTT.Prepare_WritesNothing_...`, `CWC.EveryRowTheDryRunRefuses_ExitsSeven_WithNothingWritten` |
| Explicit confirmation | only `ChangePlan.Confirm(BitLockerChoice)` issues a `ConfirmationToken`, bound to the `PlanHash`; `ConfirmChangeWindow` has no default button; the CLI needs `--confirm` | `WTT.ATokenIssuedForAnotherPlan_IsRefused...`, `ConfirmChangeWindowTests`, `ShellWriteFlowTests` |
| Exactly one write call | the import declared once (`Native/Kernel32.cs`), called once in `FirmwareVariableWriter.CommitOnce` through the writer's own seam `INativeFirmwareWriteApi` | `WriteCallSiteGuardTests`, `NoWriteGuardTests`, `CompiledStringGuardTests`, `NativeLocationGuardTests` |
| Verification read, no retry | S16; nothing loops, and a writer that throws is classed by the read | `WTT.FirmwareRefusal_IsRecorded_AndNeverFollowedByASecondCall`, `CWC.FC12_*`, `CWC.FC24_*` |
| No drift | the reads of S12 and S14 | `WTT.AnEntryTheRulesRead_ChangedAfterTheDryRun_IsStale`, `CWC.FC22_*` |
| A snapshot before every write | S12: `SnapshotStore.Create` then `SnapshotStore.Read`, hashes compared | `SnapshotStoreTests`, `SnapshotFolderTests`, `CWC.FC21_*` |
| Restore through the same pipeline | `RestoreRequest`, resolved at S0 by `SnapshotStore.DeriveRestore` | `WTT.Restore_RunsThroughTheSamePipeline...`, `CWC.FC25_*` |
| Platform policy | Secure Boot report-only; authenticated variables read-only (rule V10); firmware write protection recognised after the call by `WriteStatusMapping.FromWin32` | `PlatformPolicyTests`, `WritabilityPolicyTests`, `WriteStatusTests` |
| BitLocker | `BitLockerPreflight.Assess`; `BitLockerSuspender.SuspendForOneRestart` at S13 only when chosen; no resume; no key material read | `BitLockerPreflightTests`, `BitLockerSuspenderTests`, `CWC.FC15_*`, `CWC.FC16_*` |
| A structured record of every attempt | `WriteRecord`, `WriteRecordLog`, `record.json`; no service tag, machine ID or key material | `WriteRecordTests`, `WriteRecordLogTests` |
| A write budget per start | `WriteRecordLog.WriteBudget` (the eighth write since Windows last started is refused) | `WriteRecordLogTests`, `WTT.TheEighthWriteSinceWindowsStarted_IsRefused`, `CWC.FC31_*` |
| An uninterrupted commit | the CLI ignores Ctrl+C from S12 to S17; the window cancels close while `IsWriting` | `CliCtrlCHandlerTests`, `WpfShellAccessibilityTests` |
| The enabled-unit check (5.2) | `Safety/EnabledUnit.cs` and `PlatformPolicy` | `PlatformPolicyTests`, `CWC.FC19_*`, `CWC.FC33_*` |

### 5.2 The enabled-unit check, and its caveat

`PlatformPolicy.ReadWindowsMachineId` reads the `MachineGuid` value under
`HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography` (64-bit view, key opened read-only, no elevation
needed); `PlatformPolicy.MachineIdSha256` hashes it trimmed and lower-cased; `EnabledUnit.Check`
compares the hash with the one constant `PlatformPolicy.EnabledUnitMachineIdSha256` and answers
`Confirmed`, `OtherUnit` or `MachineIdNotRead` (an ID that cannot be read never confirms). Neither the
ID nor its hash is printed, logged, recorded or documented; only `unitConfirmed` reaches the record. A
reinstall of Windows creates a new machine ID, and every change is then refused until the constant is
updated in a reviewed commit.

**The check recognises a Windows installation, not hardware.** Machines installed from the same
deployment image share the `MachineGuid` and therefore pass, as would a disk clone, a moved drive or a
restored system image. **Before deploying the tool for anyone else, tie the check to the hardware.** A
next developer replaces the check with one tied to the machine, for example a one-way hash of the Dell
service tag (read from `Win32_BIOS`, as `MachineIdentity` already does) beside or instead of the
machine ID, with the same rules: the value never printed, logged or recorded, only the hash in the
code, a reviewed commit, and the guard and policy tests updated.

### 5.3 The static guard tests

They run in every `dotnet test` and print `file:line` for each hit. A guard is changed only in a
commit of its own, reviewed line by line.

- **`NoWriteGuardTests`**: every line of `src/**/*.cs`, `src/**/*.xaml`, `tools/**/*.ps1` and
  `tools/**/*.psm1`, comments included, against every firmware, Secure Boot, BitLocker, TPM, registry,
  boot-configuration and Dell write path, any WMI method but the three BitLocker readers, PowerShell
  run from a string, and any cctk option but `--version` and `--secureboot` (the full list is the
  README's "How that is enforced"). Its only exceptions, `WriteSiteAllowances`, are the write import in
  `Kernel32.cs` and `FirmwareVariableWriter.cs` and `DisableKeyProtectors` in `BitLockerSuspender.cs`,
  each on an exact number of lines. It also pins by reflection the constants the scan trusts and the
  nine read options of `CctkReader.cs` by a reviewed digest (`ReviewedCctkReaderCodeSha256`).
- **`CompiledStringGuardTests`**: the same checks over every string compiled into Core, the CLI and
  Presentation, so a name split in the source is caught.
- **`WriteCallSiteGuardTests`**: one write import, one call, one caller of `CommitOnce`, no loop, no
  public `WritePlan` constructor, no test constructing the real writer or suspender.
- **`WhitelistGuardTests`**: exactly BootOrder and BootNext with `0x7` and their two kinds; no request
  type that carries bytes.
- **`NativeLocationGuardTests`**: native declarations only under `src/UefiAccess.Core/Native`, no
  run-time binding of native code.
- **`ProcessLaunchGuardTests`**: only `Platform/ProcessRunner.cs` creates a process, and only
  `DellInterfaceProbe.cs`, `SecureBootPolicyInspector.cs` and `Settings/Dell/CctkReader.cs` call it.
- **`DellSettingsGuardTests`**: the settings folder names no Dell Command | Monitor service or string
  class and no WMI method call, runs only its three pinned queries; nothing under `src/` or `tools/`
  controls a service or installs software; no public settings member begins with `Set`, `Write`,
  `Apply`, `Change`, `Enable`, `Disable`, `Reset` or `Save`; no test on Windows runs the real Dell
  reader.
- **`WmiCallsAreBoundedTests`**: every WMI round trip under `src/` is bounded.
- **No timers**: `WpfShellAccessibilityTests.NoTimedBehaviourAndNoCliReference`, and the input trace's
  own `InputTraceStaticTests`.
- **`UiaTraceRecorderGuardTests`**: the recorder sends no input, changes nothing, has no timer, reads
  the event cache only and writes its file only through Core.
- **`CompositionRootLoggingTests`**: every Core service a composition root builds gets the logger.

### 5.4 What the guards cannot see

The scans cannot see a name assembled at run time, reflection, `dynamic`, a PowerShell command run
through a variable, or a registry path set on another line (the README lists them). Review is the real
control: the three allow-listed files (`Native/Kernel32.cs`, `Safety/FirmwareVariableWriter.cs`,
`Safety/BitLockerSuspender.cs`) and `CctkReader.cs` are read whole at every change.

## 6. Log, snapshot and data locations

The data root is `%ProgramData%\UefiAccess` on Windows (and `UefiAccess` under the .NET local
application data folder on a non-Windows build host). [BUILD.md](BUILD.md), section 5, has the full
layout and the protected-access-list rules; in short: logs of elevated runs go to
`%ProgramData%\UefiAccess\logs`, runs that cannot write there fall back to
`%LOCALAPPDATA%\UefiAccess\logs`, baselines to `%ProgramData%\UefiAccess\baseline\<timestamp>\`, and
write snapshots and records live under the data root too. The first elevated run creates the data root
with an explicit, protected access list and refuses a data root that is a link or is owned by an
untrusted account.

A baseline folder holds `index.json` (deterministic, a SHA-256 per variable), `vars/<guid>_<name>.bin`
(raw data), `decoded.json`, `platform.json`, `dell-settings.json` and `log.txt`
([BUILD.md](BUILD.md), section 5). `tools/sample-baseline/` is one such folder committed to the
repository, checked on every `dotnet test` by `SampleBaselineGeneratorTests`
(regenerate with `UEFIACCESS_WRITE_SAMPLE_BASELINE=1`).

## 7. Known limitations

- **Read-only Dell settings.** The Dell view only reads; it changes no BIOS setting, by design.
- **Only two variables are writable.** BootOrder and BootNext, and only on an enabled unit.
- **Validated on one machine.** The 2014 Dell OptiPlex 7020 with BIOS A18. Reads may work on any UEFI
  Windows 11 machine; everything else is unsupported (see the README).
- **The enabled-unit check is installation-bound, not hardware-bound** (section 5.2): tie it to the
  hardware before deploying for anyone else.
- **Unsigned build.** SmartScreen may warn and Smart App Control can block it; signing is out of scope.
- **No installer, no JAWS, no localization.** All out of scope.
- **Boot-service-only variables are invisible.** No Windows tool can read them at runtime.
- **BootOrder may not persist across a restart.** Some firmware or Windows configurations restore the
  boot order after a restart (for example Windows Boot Manager re-asserting first place); a lost
  reorder is expected, not a fault.
- **No user-mode NVRAM free-space check.** There is no way to read the firmware's remaining variable
  storage from Windows, so the write budget (the eighth write since Windows last started is refused)
  and the same-size rule stand in; deleting `write-log.jsonl` resets the budget.

## 8. Extending the tool

Every extension keeps the invariants of section 5: the static guard tests are the floor, the
allow-listed write files and `CctkReader.cs` are read whole at every change, a guard is changed only
in a commit of its own, and every `dotnet test` stays green with `0 Warning(s)`.

### 8.1 A new decoder (read-only)

Follow `NetworkStackDecoder`:

1. Confirm the variable is visible at run time (`RT`, in `uefiaccess list` and a baseline's
   `index.json`), and take its name, GUID, size and attributes from there.
2. Take the layout from the firmware's IFR (the setup forms) for that BIOS version, and decode only the
   questions the IFR shows; other bytes stay in the hex dump; a size the layout does not expect is an
   `Error` (nothing decoded, nothing thrown). Never invent a layout, or take one from another model or
   BIOS version.
3. Add the GUID to `KnownGuids` and its spoken label to `NamespaceCatalog`; labels are spoken in every
   row and group header, so none may hold a word that implies a change
   (`NamespaceCatalogTests.Labels_ContainNoWriteImplyingWords`).
4. Implement `CanDecode` (by GUID and name) and `Decode` (returning a `DecodedVariable`); no value
   begins with "Read-only", and no number is bare when its unit is unknown.
5. Register it in `DecoderRegistry.CreateDefault()` (the first decoder that accepts wins). Nothing else
   is wired: the window, the command line and the baseline all take the registry as it is. Add a test
   beside `NetworkStackDecoderTests.cs` (the bytes, a disabled dependency, a wrong size, empty data, an
   unknown value), and update the decoder list in section 4.1 and the count-bearing tests
   (`DecoderRegistryTests`, `NamespaceCatalogTests`).

### 8.2 A new Dell field (an attribute row)

[Dell-Attribute-Map.md](Dell-Attribute-Map.md), section 8, is the short form. In order:

1. **The catalog entry**, in `Entries` of `DellAttributeCatalog.cs`: a stable `Id`; a `DisplayName` in
   words a person uses (no `|`, `>` or "read-only", never a bare number, never a word that sounds like
   a command: TPM activation says `Activated`, not F2's `Activate`); the `Category` and `SetupLocation`
   of the BIOS setup screen; Dell Command | Monitor's `DcmAttributeName`; the `Values`, each a
   `DellValueText(Text, DcmName, CctkTokens)`. Map by Dell's names, never by codes. No new WMI query:
   the row is filtered from the one pinned enumeration.
2. **A cctk read option, only if the fallback must read the row.** Without one the row reads
   `not reported`. Adding one is a guard change of its own: a `CctkReadOption` member, a constant in
   `CctkReader.cs` (on a line without the word cctk), its arm in `ArgumentFor`, the entry's `CctkOption`
   and `CctkOutputNames`, the test's list `CctkReadOptionArguments` in `NoWriteGuardTests`, and the
   reviewed digest `ReviewedCctkReaderCodeSha256`. The line scan after the word cctk still allows only
   `--version` and `--secureboot`; never pass a value, `-i`, `--infile`, `-o`, `--outfile` or the
   boot-order option.
3. **The fixtures**: `Fixtures/DcmRowFixtures.cs` holds the 72 A18 attributes; add the row's expected
   text, and a cctk line to `CctkOutputFixtures.cs` when read through cctk.
4. **The tests that change with the list** (`DellAttributeCatalogTests`, `DcmSettingsProviderTests`,
   `CctkSettingsProviderTests`, `DellSentencesTests`, the view-model and CLI Dell tests), then
   regenerate the sample baseline (section 2).
5. **The attribute map**: a section with its two tables, and the attribute removed from the "not shown"
   list.
6. **F2 verification**: check the row's value against the BIOS setup screen on the target machine.

### 8.3 A new CLI command, or a window region

**A command** follows `Commands/DellCommand.cs`: an `internal static class` in
`src/UefiAccess.Cli/Commands/` whose `Create(...)` runs the body through `CommandRunner.Run(...)`
(which brings the preconditions, the one JSON document and the exit codes of [CLI.md](CLI.md) section 2
with it); its document an `internal sealed record <Name>Document` starting with `Command` and `Ok`;
tables through `Output/TableWriter`; every sentence a constant. Register it in
`Program.BuildRootCommand`; pass the services factory on so the tests' fakes are used. A new Core
service joins `CliPlatform` and `CliServices`. Tests: a class like `CliDellCommandTests`,
`CliCommandTests.Help_ListsEveryCommand`, the document in `CliDocumentationTests.Schemas` with an
example in `docs/CLI.md`. A command that changes anything is not a new command but a change of the
safety model (sections 5 and 8.5).

**A region** follows the `Dell settings` region: a `Region` member in its F6 position
(`RegionOrder.Order`/`Name`, a `FocusTarget`, a `WindowFit` rule if it needs a height); a view model
(rows built once and updated in place, so a read raises no burst of UI Automation events; no command
that changes anything); every label, help text and Status sentence in `Strings`; a stock `GroupBox`
named `<Name>Region` in F6 order in `MainWindow.xaml` with an explicit `TabIndex` on every control (the
Status box renumbered), access keys, a keyed item style, system colors only; tests in
`WpfShellAccessibilityTests`, `WpfShellLayoutTests`, `WpfShellLowVisionTests`, `ScaffoldSmokeTests` and
a shell test class; and [Keyboard-Map.md](Keyboard-Map.md) updated.

### 8.4 Supporting another firmware vendor (HP, Lenovo, and others)

**What already holds on any UEFI machine** (it follows the UEFI specification and Windows, not Dell):
the store (`WindowsFirmwareVariableStore`); the standard decoders (`BootOrderDecoder`,
`LoadOptionDecoder`, `SecureBootStateDecoder`, `OsIndicationsDecoder`); baselines; the inspectors
`FirmwareTypeDetector`, `TpmInfo`, `BitLockerInspector`, `SecureBootPolicyInspector`; the write
pipeline's logic (BootOrder and BootNext under the EFI global GUID with `0x7`, rules V1 to V17, snapshots,
restore); and the vendor-neutral settings model in `Core/Settings` (`FirmwareSetting`, `SettingValue`,
`SettingState`, `PossibleValuesOrigin`). Following the specification is not the same as verifying on
hardware: boot-order persistence, firmware write-protection behavior and NVRAM reclamation differ by
firmware.

**What is Dell-specific** (re-done per vendor):

| Part | Code and files |
|---|---|
| Settings provider | `Core/Settings/Dell/`: `DellSettingsReader`, `DellMachineReader`, `DcmSettingsProvider` (Dell Command Monitor's `DCIM_BIOSEnumeration` in `root\dcim\sysman`), `CctkSettingsProvider`/`CctkReader` (the cctk fallback), `DellAttributeCatalog`, `DellSettingMapper`, `DellSentences`, `DellModel.cs`, and the seam `IDellSettingsProvider` |
| Interface probe | `Platform/DellInterfaceProbe`, `uefiaccess gate`, `tools/gate/Run-Gate.ps1` |
| Expected model | `MachineIdentity.EvaluateExpectedModel`, `PlatformPolicy.IsContractedMachine` (`ContractedBiosVersion`), the WrongMachine sentences |
| Enabled unit | `EnabledUnit`, `PlatformPolicy.EnabledUnitMachineIdSha256` |
| Setup-variable decoder | `NetworkStackDecoder`, `KnownGuids.NetworkStackSetup`, its `NamespaceCatalog` label |
| Window / CLI / files | the `Dell settings` region and its `Strings`, `uefiaccess dell`, `dell-settings.json` |

Then, in order:

1. **A feasibility gate on the target model**, read-only, with someone at the machine: UEFI boot and
   Secure Boot (`uefiaccess info`, `secureboot`); the variables readable (`list`, a baseline); the BIOS
   version; which vendor interface answers; BitLocker and the TPM; that BIOS version's setup forms
   (`gate --ifr-varstores` takes any vendor's `Name;GUID;Size` list). If no vendor interface answers,
   that model gets no settings view.
2. **A read-only settings provider**, in a folder of its own beside `Settings/Dell` (for example
   `Settings/Hp`), built like Dell's and mapping into `FirmwareSetting`. The guard changes come first,
   reviewed on their own: `DellSettingsGuardTests` scans every file under `src/UefiAccess.Core/Settings`,
   so a new query must be added to `PinnedQueries`, the vendor's write classes and methods to
   `SettingsFolderBannedNames` and `NoWriteGuardTests` (`ForbiddenLiterals`, `ForbiddenMethodNames`),
   and a new reader to the "tests never run the real reader" rule, or the folder gets its own guard
   class. Then the provider: the machine first (never the serial number), ending the read on another
   manufacturer; pinned `SELECT` constants through `WmiHelper.Query` inside `RunBounded`; one read
   budget; every failure a problem of the result, nothing thrown. A vendor tool only as a fallback, as
   cctk: one file, a full path under the Program Files known folders, pinned read arguments. Its own
   catalog, mapped by the vendor's names, never codes.
3. **The window and the command line.** The region, its button, the list, the source line and the
   Status sentences all say Dell; either generalize them so one region names the vendor of the machine
   it runs on, or add a sibling region and command (section 8.3). Either way update the accessibility
   tests, [Keyboard-Map.md](Keyboard-Map.md) and [CLI.md](CLI.md).
4. **Identity and write policy are a decision, not a refactor.** Reading comes first and needs no change
   to the identity checks, because `PlatformPolicy.IsContractedMachine` and `EnabledUnit` refuse every
   write at `Confirm change` on any other model. Which models, BIOS versions and units the write
   pipeline accepts is a deliberate, reviewed change, with a hardware binding for the unit check (a
   Windows machine ID recognises an installation, not a machine; section 5.2), the vendor's own
   recovery path written and printed first ([Recovery.md](Recovery.md)), and a hardware write-test
   protocol run on that model.

**Starting points (unverified here)**, from the vendors' published management interfaces. Confirm each
on the target model, read-only and by hand first
(`Get-CimInstance -Namespace <namespace> -ClassName <class>`, never `Invoke-CimMethod`):

- **HP business PCs**: the WMI namespace `root\HP\InstrumentedBIOS`, classes `HP_BIOSEnumeration`,
  `HP_BIOSString`, `HP_BIOSInteger`, `HP_BIOSOrderedList`, `HP_BIOSPassword`. Settings are changed
  through `HP_BIOSSettingInterface`, which a read-only provider never calls and the guards ban by name.
- **Lenovo (ThinkPad, ThinkCentre, ThinkStation)**: `Lenovo_BiosSetting` in `root\WMI`, whose
  `CurrentSetting` reads "Name,Value". Changes go through `Lenovo_SetBiosSetting` /
  `Lenovo_SaveBiosSettings`, never called by a read-only provider. `root\WMI` exists on every Windows
  machine, so there an absent class, not an absent namespace, says the interface is missing.
- **Newer Dell models**: `root\dcim\sysman\biosattributes`, which `DellInterfaceProbe` detects and
  never reads; `IDellSettingsProvider` leaves a seam for a third provider there.

### 8.5 A new changeable (writable) setting

This is the most safety-sensitive change. Treat it as a change request: give it a design note in the
shape of the write pipeline's rules (the operation, the bytes, the rules, what can never happen, the
failure modes), and prove it on hardware with snapshots, a restore and someone at the machine before it
is trusted. In order:

1. **The guard tests, in a commit of their own, reviewed first.** `WhitelistGuardTests` and
   `WhitelistTests` (two entries today, BootOrder and BootNext under the EFI global GUID with `0x7`)
   fail on purpose until changed; `WriteCallSiteGuardTests` stays as it is (a new setting reuses the one
   `CommitOnce` call and adds no call site, import or process).
2. **The whitelist** (`Safety/Whitelist.cs`): a `WhitelistEntry` in `Entries`, and a `WritableKind`
   member if the operation is new. Attributes other than `0x7` also change `WritePlan.RequiredAttributes`,
   the write path itself.
3. **The request** (`Safety/ChangeRequest.cs`): a sealed record carrying numbers or typed values, never
   bytes; `SnapshotStore.DeriveRestore` must resolve a snapshot into it.
4. **The validation** (`Safety/ChangeValidator.cs`): the target in `Validate`'s switch, the rules in
   `Run.Execute`, the after-image encoded by the pipeline (as `BootListCodec` does), identifiers in
   `CheckNames` and reasons in `CheckSentences`.
5. **The verdicts** (`Safety/WritabilityPolicy.cs`): a `Writability` member, its sentence and item
   status, the branch in `Describe`; no sentence begins with "Read-only".
6. **The transaction** (`Safety/WriteTransaction.cs`): the direct reads, the BitLocker read,
   `BuildWritePlan` (kind, attributes, size rule), `Classify`, the outcome sentences, the snapshot
   folder word.
7. **The record and snapshot** (`WriteRecord.cs`, `SnapshotStore.cs`): the operation in the record; a
   changed record field needs a new schema version.
8. **The command line and Presentation**: a `write` subcommand like `CreateBootOrder`; a `ChangeAction`
   and dialog texts (each single-line box under 200 characters, no write-implying label); the App
   control in the Change group with an explicit `TabIndex` and a unique access key.
9. **The failure cases**: a unit test per refusal (the writer never called), a rig line in
   `tools/write-tests/Run-FailureCases.ps1` refused before any call (never `--confirm`).
10. **The documents**: the Safety README, the README's "Write guarantee", [CLI.md](CLI.md),
    [Keyboard-Map.md](Keyboard-Map.md), and, if the setting can affect a start, [Recovery.md](Recovery.md).
11. **The hardware test**: on the target machine, with BitLocker handled as the recovery procedure says,
    write / restart / check / restore. Until then the setting is unverified on hardware.
