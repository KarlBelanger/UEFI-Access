# Dell attribute map: the Dell BIOS settings of the OptiPlex 7020

This page is the attribute map for the Dell BIOS settings the application and the command-line tool
show: for each setting, its attribute name, its display name, the Dell interface it is read through,
its possible values, and the BIOS version it was verified against. The list itself is one data table
in the code, `src/UefiAccess.Core/Settings/Dell/DellAttributeCatalog.cs`.

The list is the eight items of the agreed scope expressed as ten rows, because TPM and
Virtualization each map to two Dell attributes. The tool reports the list's status in
every `uefiaccess dell --json` document as `Approved list of the Dell BIOS settings this tool shows`.

**Checked against BIOS A18 only.** The names, codes and values below come from the Dell Command |
Monitor export of the OptiPlex 7020 (Dell Command | Monitor 10.13.2.56, 72 attributes, BIOS A18),
from the text of the A18 setup forms (the IFR), and from `syscfg.xml` of Dell Command | Configure
5.2.2. On another BIOS version the tool still reads and shows the values, and adds the warning
`These names and values were checked against BIOS A18 only; this computer runs BIOS A17.` (naming the
version found).

## 1. How to read this page

Each setting has a heading `n. <Shown as> (<Id>)` and two small tables, which a screen reader can
read cell by cell more easily than one wide table:

- The first table, two columns: how the setting is shown, where it is in the BIOS setup (F2), its
  category, the attribute it is read from in each Dell interface, Dell's own read-only flag, what it
  depends on, and the BIOS version it was verified against.
- The second table, the values: `Code` is Dell Command | Monitor's code for the value on A18 (never
  spoken and never part of a row or the window, kept in the JSON and in the `--all` table only),
  `Dell Command Monitor name` is its name for the code (its `PossibleValuesDescription`),
  `Dell Command Configure value` lists the words cctk is expected to print (the 5.2.2 word first,
  then the word of the older 3.x versions), `F2 label` is what the A18 setup forms show for the
  value, and `Shown as` is the text the application and the command-line tool give.

Three vocabularies meet here and are never mapped by number: Dell Command | Monitor's codes, the
setup forms' values and Dell Command | Configure's words. For Wake on LAN, Dell Command | Monitor's
code 4 is `LAN`, while the setup forms' value 4 is `LAN or WLAN` and their `LAN Only` is value 5.
The tool maps every value by Dell's name, never by its code.

"Dell read-only flag on A18" is Dell Command | Monitor's `IsReadOnly`, carried as reported
(`vendorMarksReadOnly` in the JSON). For these ten settings, which are all
enumeration-type attributes, Dell's own conversion code sets it by the attribute's name, so it is
`false` for each of them. No row ever speaks the flag (finding A2-6): that nothing can be changed in
the tool is said once, on the button's help and on the state line.

## 2. The ten settings

### 1. Boot list option (BootListOption)

| Field | Value |
|---|---|
| Shown as | `Boot list option` |
| BIOS setup page and label | `General, Boot Sequence, Boot List Option` (the setup forms' question is `Boot List` on the `Boot Sequence` page) |
| Category | `General` |
| Source: Dell Command Monitor | class `DCIM_BIOSEnumeration`, `AttributeName` `Boot Mode` |
| Source: Dell Command Configure | none: not read through Dell Command Configure. Its only read form is a second argument on the boot-order option, whose other forms change the boot order, and this build never passes that option. In the fallback the row reads `not reported`, with the note `This version reads it through Dell Command Monitor only.` |
| Dell read-only flag on A18 | `false` (`vendorMarksReadOnly` in the JSON), as Dell's rule for enumeration attributes gives it |
| Depends on | nothing |
| BIOS version verified against | A18 |

| Code | Dell Command Monitor name | Dell Command Configure value | F2 label | Shown as |
|---|---|---|---|---|
| 1 | `BIOS` | none (not read) | `Legacy` | `Legacy` |
| 2 | `UEFI` | none (not read) | `UEFI` | `UEFI` |

### 2. Secure Boot (SecureBoot)

| Field | Value |
|---|---|
| Shown as | `Secure Boot`; after a value read through Dell Command Monitor the row ends with the note `Dell Command Monitor lists only Enabled; the BIOS setup also offers Disabled.`, and when Dell Command Monitor reports no value, the row reads `no value reported` with the note `Dell Command Monitor reports no value while Secure Boot is off.` |
| BIOS setup page and label | `Secure Boot, Secure Boot Enable` |
| Category | `Secure Boot` |
| Source: Dell Command Monitor | class `DCIM_BIOSEnumeration`, `AttributeName` `Secure Boot` |
| Source: Dell Command Configure | option `--secureboot`, output names `SecureBoot` and `secureboot`; cctk 5.2.2 prints `SecureBoot=Enabled`, cctk 3.3 prints `secureboot=enable` |
| Dell read-only flag on A18 | `false` |
| Depends on | nothing |
| BIOS version verified against | A18 |

| Code | Dell Command Monitor name | Dell Command Configure value | F2 label | Shown as |
|---|---|---|---|---|
| not listed | `Disable` (inferred: Dell Command Monitor lists only `Enable`, and a different word would be shown as Dell's own) | `Disabled`, `disable`, `off` | `Disabled` | `Disabled` |
| 2 | `Enable` | `Enabled`, `enable`, `on` | `Enabled` | `Enabled` |

Secure Boot stays report-only. The UEFI variable `SecureBoot` (`uefiaccess secureboot`, and the
variable list) stays the authority on whether Secure Boot is on.

### 3. SATA operation (SataOperation)

| Field | Value |
|---|---|
| Shown as | `SATA operation` |
| BIOS setup page and label | `System Configuration, SATA Operation` |
| Category | `System Configuration` |
| Source: Dell Command Monitor | class `DCIM_BIOSEnumeration`, `AttributeName` `Embedded SATA Controller` |
| Source: Dell Command Configure | option `--embsataraid`, output names `EmbSataRaid` and `embsataraid`; cctk 5.2.2 prints `EmbSataRaid=Ahci` for an AHCI controller |
| Dell read-only flag on A18 | `false` |
| Depends on | nothing |
| BIOS version verified against | A18 |

| Code | Dell Command Monitor name | Dell Command Configure value | F2 label | Shown as |
|---|---|---|---|---|
| 1 | `Off` | `Disabled`, `off` | `Disabled` | `Disabled` |
| 3 | `ATA` | `Ata` | `ATA` | `ATA` |
| 4 | `AHCI` | `Ahci` | `AHCI` | `AHCI` |

Keep this on AHCI: changing it can leave the machine unable to start. The tool only reads it.

### 4. TPM security (TpmSecurity)

| Field | Value |
|---|---|
| Shown as | `TPM security` |
| BIOS setup page and label | `Security, TPM Security` (a box named `TPM Security`) |
| Category | `Security` |
| Source: Dell Command Monitor | class `DCIM_BIOSEnumeration`, `AttributeName` `Trusted Platform Module` |
| Source: Dell Command Configure | option `--tpm`, output names `TpmSecurity` and `tpm`; cctk 5.2.2 prints `TpmSecurity=Enabled` for a TPM that is on |
| Dell read-only flag on A18 | `false` |
| Depends on | nothing; `TPM activation` depends on it |
| BIOS version verified against | A18 |

| Code | Dell Command Monitor name | Dell Command Configure value | F2 label | Shown as |
|---|---|---|---|---|
| 1 | `Enable` | `Enabled`, `enable`, `on` | the box `TPM Security` ticked | `Enabled` |
| 2 | `Disable` | `Disabled`, `disable`, `off` | the box `TPM Security` not ticked | `Disabled` |

The polarity of Dell Command | Monitor's codes differs from setting to setting (here 1 is
`Enable`; for Num Lock at boot 2 is), which is why the tool never maps by code.

### 5. TPM activation (TpmActivation)

| Field | Value |
|---|---|
| Shown as | `TPM activation`; while TPM security reads anything but `Enabled`, the row reads `not applicable while TPM security is Disabled`, because the setup screen hides the choice then (the value read is kept in the JSON) |
| BIOS setup page and label | `Security, TPM Security, TPM State` (the choice `TPM State` on the `TPM Security` page) |
| Category | `Security` |
| Source: Dell Command Monitor | class `DCIM_BIOSEnumeration`, `AttributeName` `Trusted Platform Module Activation` |
| Source: Dell Command Configure | option `--tpmactivation`, output names `TpmActivation` and `tpmactivation`; `syscfg.xml` writes the values as `Enabled[activate]` and `Disabled[deactivated]`, so cctk 5.2.2 prints `TpmActivation=Enabled` for an activated TPM (shown as `Activated`) |
| Dell read-only flag on A18 | `false` (D8: Dell's rule makes the flag true only for "Trusted Platform Module Clear", which is not on A18) |
| Depends on | `TPM security` reads `Enabled` |
| BIOS version verified against | A18 |

| Code | Dell Command Monitor name | Dell Command Configure value | F2 label | Shown as |
|---|---|---|---|---|
| 1 | `Deactivate` | `Disabled`, `deactivated`, `Deactivate` | `Deactivate` | `Deactivated` |
| 2 | `Activate` | `Enabled`, `activate`, `Activated` | `Activate` | `Activated` |

### 6. Virtualization (Virtualization)

| Field | Value |
|---|---|
| Shown as | `Virtualization` |
| BIOS setup page and label | `Virtualization Support, Virtualization` (a box named `Enable Intel Virtualization Technology`) |
| Category | `Virtualization Support` |
| Source: Dell Command Monitor | class `DCIM_BIOSEnumeration`, `AttributeName` `Processor Virtualization Technology` |
| Source: Dell Command Configure | option `--virtualization`, output names `Virtualization` and `virtualization`; cctk 5.2.2 prints `Virtualization=Enabled` when it is on |
| Dell read-only flag on A18 | `false` |
| Depends on | nothing |
| BIOS version verified against | A18 |

| Code | Dell Command Monitor name | Dell Command Configure value | F2 label | Shown as |
|---|---|---|---|---|
| 1 | `Disable` | `Disabled`, `disable`, `off` | the box not ticked | `Disabled` |
| 2 | `Enable` | `Enabled`, `enable`, `on` | the box ticked | `Enabled` |

### 7. VT for Direct I/O (VtForDirectIo)

| Field | Value |
|---|---|
| Shown as | `VT for Direct I/O` |
| BIOS setup page and label | `Virtualization Support, VT for Direct I/O` (a box named `Enable VT for Direct I/O`) |
| Category | `Virtualization Support` |
| Source: Dell Command Monitor | class `DCIM_BIOSEnumeration`, `AttributeName` `VT for Direct IO` |
| Source: Dell Command Configure | option `--vtfordirectio`, output names `VtForDirectIo` and `vtfordirectio`; cctk 5.2.2 prints `VtForDirectIo=Enabled` when it is on |
| Dell read-only flag on A18 | `false` |
| Depends on | nothing |
| BIOS version verified against | A18 |

| Code | Dell Command Monitor name | Dell Command Configure value | F2 label | Shown as |
|---|---|---|---|---|
| 1 | `Disable` | `Disabled`, `disable`, `off` | the box not ticked | `Disabled` |
| 2 | `Enable` | `Enabled`, `enable`, `on` | the box ticked | `Enabled` |

### 8. Integrated NIC (IntegratedNic)

| Field | Value |
|---|---|
| Shown as | `Integrated NIC` |
| BIOS setup page and label | `System Configuration, Integrated NIC` (the same page holds the box `Enable UEFI Network Stack`, which is NetworkStackVar's byte 0, section 3) |
| Category | `System Configuration` |
| Source: Dell Command Monitor | class `DCIM_BIOSEnumeration`, `AttributeName` `NIC` |
| Source: Dell Command Configure | option `--embnic1`, output names `EmbNic1` and `embnic1`; cctk 5.2.2 prints `EmbNic1=Enabled` when it is on |
| Dell read-only flag on A18 | `false` |
| Depends on | nothing |
| BIOS version verified against | A18 |

| Code | Dell Command Monitor name | Dell Command Configure value | F2 label | Shown as |
|---|---|---|---|---|
| 1 | `Disable` | `Disabled`, `off` | `Disabled` | `Disabled` |
| 2 | `EnabledwithPXE` | `EnabledPxe`, `on` | `Enabled w/PXE` | `Enabled with PXE` |
| 3 | `Enable` | `Enabled`, `onnopxe` | `Enabled` | `Enabled` |
| 6 | `Enable image server` | `OnWithImageServerBoot` | `Enabled w/Cloud Desktop` (inferred) | `Enabled with image server` |

### 9. Wake on LAN (WakeOnLan)

| Field | Value |
|---|---|
| Shown as | `Wake on LAN` |
| BIOS setup page and label | `Power Management, Wake on LAN` (the setup forms' question is `Wake on LAN/WLAN`; without WLAN hardware it offers exactly `Disabled`, `LAN Only` and `LAN with PXE Boot`) |
| Category | `Power Management` |
| Source: Dell Command Monitor | class `DCIM_BIOSEnumeration`, `AttributeName` `Wake-On-LAN` |
| Source: Dell Command Configure | option `--wakeonlan`, output names `WakeOnLan` and `wakeonlan`; cctk 5.2.2 prints `WakeOnLan=Disabled` when it is off (the 3.x word for LAN Only is `enable`) |
| Dell read-only flag on A18 | `false` |
| Depends on | nothing |
| BIOS version verified against | A18 |

| Code | Dell Command Monitor name | Dell Command Configure value | F2 label | Shown as |
|---|---|---|---|---|
| 1 | `Disable` | `Disabled`, `disable` | `Disabled` | `Disabled` |
| 4 | `LAN` | `LanOnly`, `enable` | `LAN Only` (inferred) | `LAN Only` |
| 7 | `LAN with PXE Boot` | `LanWithPxeBoot`, `lanwithpxeboot` | `LAN with PXE Boot` | `LAN with PXE Boot` |

### 10. Num Lock at boot (NumLock)

| Field | Value |
|---|---|
| Shown as | `Num Lock at boot` |
| BIOS setup page and label | `POST Behavior, Numlock LED` (a box named `Enable Numlock LED`) |
| Category | `POST Behavior` |
| Source: Dell Command Monitor | class `DCIM_BIOSEnumeration`, `AttributeName` `Num Lock` |
| Source: Dell Command Configure | option `--numlock`, output names `NumLock` and `numlock`; cctk 5.2.2 prints `NumLock=Enabled` when it is on |
| Dell read-only flag on A18 | `false` |
| Depends on | nothing |
| BIOS version verified against | A18 |

| Code | Dell Command Monitor name | Dell Command Configure value | F2 label | Shown as |
|---|---|---|---|---|
| 1 | `Disable` | `Disabled`, `disable`, `off` | the box not ticked | `Disabled` |
| 2 | `Enable` | `Enabled`, `enable`, `on` | the box ticked | `Enabled` |

On the reference OptiPlex 7020 the ten values are, in this order: UEFI, Enabled, AHCI, Enabled,
Activated, Enabled, Enabled, Enabled, Disabled, Enabled.

## 3. NetworkStackVar

Besides the Dell interfaces, the tool decodes one Dell setup variable that Windows can read at
runtime, `NetworkStackVar` (GUID `d1405d16-7afc-4695-bb12-41459d3695a2`, 6 bytes, attributes
`NV BS RT`). It is decoded in the variable list and by `uefiaccess read`, like every other variable,
and it never appears in the Dell settings region. Its namespace is labelled `Network stack
configuration` in the variable list (the label avoids a word that implies a change, because labels
are spoken in every row sentence and group header).

The A18 setup forms hold five fields, in a form named "Network Stack Configuration" that no setup
page links to, so the decoder decodes five fields (bytes 0, 1, 2, 4 and 5), exactly what the forms
show:

| Byte | Field | Values | While byte 0 is 0 |
|---|---|---|---|
| 0 | `UEFI network stack` (also F2's box `Enable UEFI Network Stack` on the Integrated NIC page) | 0 `Disabled`, 1 `Enabled` | |
| 1 | `IPv4 PXE support` | 0 `Disabled`, 1 `Enabled` | `not applicable: the UEFI network stack is Disabled` |
| 2 | `IPv6 PXE support` | 0 `Disabled`, 1 `Enabled` | as byte 1 |
| 3 | none: no setup question; the hex dump shows it | | |
| 4 | `PXE boot wait time` | 0 to 5, said as `<n> (the time to press Escape to stop a PXE boot; the BIOS setup names no unit)`, never a bare number | as byte 1 |
| 5 | `Media detect time` | 0 to 50, `<n> seconds` | as byte 1 |

Any other value reads `unknown value 0x<hex>`; a size other than 6 bytes is an error and nothing is
decoded. On the reference unit the bytes are `01 01 01 00 00 00`: the stack, IPv4 PXE and IPv6 PXE
`Enabled`, a wait time of 0 and a media detect time of `0 seconds`. Dell Command | Monitor's
attribute `UEFI Network Stack` (not one of the ten rows) reads `Enable`, which agrees with byte 0.

## 4. Deliberate differences and inferences

- **TPM activation says `Deactivated` and `Activated`.** F2's `TPM State` offers `Deactivate` and
  `Activate`. A row that says `Activate` sounds like a command, so the shown words differ on purpose.
- **Dell Command | Monitor's `LAN` is shown as `LAN Only`.** Inferred: without WLAN hardware, F2's
  Wake on LAN question offers only `Disabled`, `LAN Only` and `LAN with PXE Boot`, while the setup
  forms' value 4 is `LAN or WLAN`.
- **`Enable image server` is shown as `Enabled with image server`.** It is probably F2's
  `Enabled w/Cloud Desktop` (inferred, the setup forms' only fourth option).
- **`w/` is written out.** F2's `Enabled w/PXE` is shown as `Enabled with PXE`: a screen reader says
  "w slash".
- **Secure Boot's `Disable` is inferred.** Dell Command | Monitor lists only `Enable`; a disabled
  Secure Boot under another name would be shown as Dell's own word and marked as such.
- **Boxes are shown as values.** Where F2 has a box (TPM Security, Virtualization, VT for Direct
  I/O, Numlock LED), the tool says `Enabled` for a ticked box and `Disabled` for one not ticked.
- **The display names split where Dell groups two attributes**, and differ from F2's labels where
  F2's would not say what the setting is in a list (`Num Lock at boot` for `Numlock LED`).

## 5. Failure cases

The Dell reader classifies every way a read can fail into one cause with a clear sentence
(`Dell-Interface-Setup.md`, section 4). Each case is covered by the unit tests named beside it (all
in `tests/UefiAccess.Core.Tests`):

| Case | Situation | Unit tests |
|---|---|---|
| DF3 | Dell Command \| Monitor not installed | `DcmSettingsProviderTests.Absent_WithoutTheNamespace_IsNotInstalled`, `DellSettingsReaderTests.Automatic_DcmNotInstalled_FallsBackToCctk_WithTheFallbackSentence` |
| DF4 | its namespace present, its class missing | `DcmSettingsProviderTests.Absent_WithTheNamespace_IsClassMissing` |
| DF5 | the Visual C++ runtime missing | `DcmSettingsProviderTests.ProviderLoadFailure_IsClassified`, `DellSettingsReaderTests.Automatic_ProviderLoadFailure_FallsBack` |
| DF6 | access refused | `DcmSettingsProviderTests.AccessDenied_IsClassified` |
| DF7 | the data service `dcstor64` stopped | `DcmSettingsProviderTests.ZeroRows_IsNoInstancesNotAnEmptyList`, `DellSettingsReaderTests.Automatic_NoInstances_FallsBack` |
| DF8 | Dell Command \| Monitor hangs | `DcmSettingsProviderTests.Timeout_IsTimedOut`, `DcmSettingsProviderTests.Timeout_ThenReadAgain_DoesNotQueryWhileTheFirstIsRunning`, `DellSettingsReaderTests.Automatic_DcmTimesOut_FallsBackWithinTheBudget` |
| DF9 | any other WMI error | `DcmSettingsProviderTests.OtherError_IsQueryFailed` |
| DF10 | an agreed attribute missing | `DcmSettingsProviderTests.MissingAttribute_IsNotReported` |
| DF11 | a code without a name | `DcmSettingsProviderTests.UnknownCode_IsUnnamedAndSpeaksNoNumber` |
| DF12 | a name the list does not know | `DcmSettingsProviderTests.UnmappedDescription_ShowsDellsWordAndIsMarked`, `DellAttributeCatalogTests.EveryDescriptionSeenOnA18_IsMapped` |
| DF13 | Secure Boot without a current value | `DcmSettingsProviderTests.SecureBootWithoutCurrentValue_IsNoValueReportedWithItsNote` |
| DF14 | cctk not found | `CctkSettingsProviderTests.CctkNotFound_IsProgramNotFound` |
| DF15 | cctk needs administrator rights | `CctkSettingsProviderTests.Exit95_StopsWithNeedsAdministrator` |
| DF16 | cctk finds no BIOS interface | `CctkSettingsProviderTests.Exit12And140_StopWithNoBiosInterface` |
| DF17 | an option not offered on this computer | `CctkSettingsProviderTests.Exit118And42And96_MakeOnlyThatRowNotReported` |
| DF18 | a cctk run times out | `CctkSettingsProviderTests.Timeout_StopsAndLeavesTheRestNotRead`, `CctkSettingsProviderTests.AfterATimeout_NoLaterReadStartsCctk` |
| DF19 | an answer in a form the parser does not know | `CctkSettingsProviderTests.FindValue_TwoMatchingLines_IsNoValue` |
| DF20 | any other exit code, or a run that did not start | `CctkSettingsProviderTests.OtherExitCode_StopsAndLeavesTheRestNotRead` |
| DF21 | the 120-second budget used up | `DellSettingsReaderTests.Budget_IsSharedAcrossSteps`, `CctkSettingsProviderTests.Budget_BelowOneRunTimeout_StartsNoRun` |
| DF22 | only the built-in interface of newer models present | `DellSettingsReaderTests.NativeNamespace_IsCheckedOnlyWhenDcmIsNotInstalledAndNothingAnswered` |
| DF23 | a BIOS other than A18 | `DellSettingsReaderTests.OtherBios_AddsTheWarning` |

DF1 (not Windows), DF2 (not a Dell), DF24 (a provider that throws) and DF25 (a baseline's Dell file
that cannot be used) are covered by their own tests. A machine that never had the Dell software
typically shows DF3 with DF14: the sentence the user hears there is the first real one.

## 6. What is not shown, and why

- **The boot order.** The BootOrder decoder shows it in the variable list, from the firmware's own
  variable; the "boot sequence mode" is row 1.
- **USB boot support.** Its only candidate, Dell Command | Monitor's `USB Emulation`, has a meaning
  nobody has verified on this BIOS.
- **The admin password state.** Not on the agreed list; whether a BIOS password is set is read by
  the write pipeline's preflight, never by this view.
- **Fast Boot.** BIOS A18 has no such setting.
- **The string class's one value.** Dell Command | Monitor's string class holds one constant that
  its own conversion file writes, a placeholder, not data from the machine. It is never queried.
- **The other 62 attributes of the 72.** They are read and kept as Dell's own records (name, codes,
  Dell's names, Dell's flag) and listed by `uefiaccess dell --all`, in text and JSON. They get no display name, no category and no setup-screen words, because they have not been
  checked against F2; mapping all 72 is out of scope. The integer-type ones among them
  (`Auto On Hour`, `Auto On Minute`, `Intel Rapid Start Timer`, `Primary Video Device Slot`) carry
  the BIOS's own read-only flag, not Dell's rule. They are, in Dell Command | Monitor's order on A18:
  - `AC Power Recovery Mode`
  - `Active State Power Management`
  - `Admin Setup Lockout`
  - `Allow BIOS Downgrade`
  - `Auto On`
  - `Auto on Friday`
  - `Auto On Hour`
  - `Auto On Minute`
  - `Auto on Monday`
  - `Auto on Saturday`
  - `Auto on Sunday`
  - `Auto on Thursday`
  - `Auto on Tuesday`
  - `Auto on Wednesday`
  - `BlockS3`
  - `Chassis Intrusion`
  - `Chassis Intrusion Status`
  - `Clear System Event Log`
  - `Computrace` (Dell's flag is true for it, by name)
  - `Deep Sleep Control`
  - `Enable Legacy Option ROMs`
  - `Fan Control override`
  - `Force PXE on Next Boot`
  - `Front Panel USB Ports`
  - `Front USB 3 Port`
  - `HDD Protection`
  - `Intel Rapid Start Technology`
  - `Intel Rapid Start Timer`
  - `Intel Smart Connect Technology`
  - `Keyboard Error Reporting`
  - `Limit CPUID Value`
  - `Master Password Lockout`
  - `OROM Keyboard Access`
  - `Password Bypass`
  - `Password Status Lock`
  - `POST MEBx Key Setting`
  - `Primary Video Device Slot`
  - `Processor C State Control`
  - `Processor CMP`
  - `Processor Core Based Turbo Mode`
  - `Processor Execute Disable`
  - `Processor Logical Processor (HyperThreading)`
  - `Rear Dual USB`
  - `Rear Dual USB 2nd stack`
  - `Rear USB 3 Port`
  - `SATA Port 0`
  - `Serial Port 1`
  - `SERR DMI Message`
  - `Signed Firmware Update`
  - `SMART Errors`
  - `Sound Device`
  - `SpeedStep`
  - `Strong Password`
  - `TPM PPI ACPI Support`
  - `TPM PPI Deprovision Override`
  - `TPM PPI Provision Override`
  - `Trusted Execution`
  - `Uefi Boot Path Security`
  - `UEFI Network Stack` (compared with NetworkStackVar's byte 0, section 3)
  - `USB 3.0`
  - `USB Emulation`
  - `USB Wake Support`

## 7. The built-in Dell BIOS interface of newer models

Newer Dell models expose native WMI classes (`root\dcim\sysman\biosattributes`) beside Dell Command
| Monitor's classes of the 2014 model. The OptiPlex 7020 is the 2014 model, and that namespace is
absent on it, so the Dell settings view is served through Dell Command | Monitor. For the native
interface this build does three things and no more:

1. It detects it: when no Dell interface answered and Dell Command | Monitor is not installed, the
   reader checks whether that namespace exists, and if it does, the problem sentence adds
   `This computer has the built-in Dell BIOS interface of newer models, which this version does not read.`
   instead of advice to install Dell Command | Monitor.
2. It leaves a seam: a third provider can be added later without a change to the window, the command
   line or the list.
3. It reads nothing there. That namespace's interface class carries methods that change settings,
   which this build never names.

## 8. How to add a setting

A setting is added, removed or renamed in these places, and nowhere else:

1. **One catalog entry** in `src/UefiAccess.Core/Settings/Dell/DellAttributeCatalog.cs`: its id,
   display name, category, setup location, Dell Command | Monitor attribute name, and its values in
   the setup screen's words with Dell Command | Monitor's name and Dell Command | Configure's words
   for each. Never a mapping by code.
2. **Its fixture row**: the Dell Command | Monitor row in
   `tests/UefiAccess.Core.Tests/Fixtures/DcmRowFixtures.cs` (already there for the 72 attributes of
   A18), and a Dell Command | Configure output line in `Fixtures/CctkOutputFixtures.cs` when it is
   read through cctk. The tests that expect the rows in order change with it.
3. **Its section on this page**, with both tables.
4. **Its F2 check**: verify its value against the BIOS setup screen on the target machine.

A setting that Dell Command | Configure would read with a new option is a guard change of its own: a
member of `CctkReadOption`, a constant in `CctkReader.cs`, the test's own list of read options in
`NoWriteGuardTests` (`CctkReadOptionArguments`), and the reviewed digest of the reader's code there
(`ReviewedCctkReaderCodeSha256`); the guard's line scan keeps allowing only `--version` and
`--secureboot` after the word cctk everywhere else (README.md, "How that is enforced"). A setting
read through Dell Command | Monitor only needs no guard change.
