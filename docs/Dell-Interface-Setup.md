# Dell interface setup: what the Dell settings view needs

This page is for whoever sets up or maintains a machine on which UefiAccess shows Dell BIOS
settings: what Dell's interface needs, the order to install it in, how to check each part without
changing anything, and what the tool says when a part is missing.

UefiAccess never installs, starts, or stops anything. It reads, reports what is missing, and says
what to do. A static test (`DellSettingsGuardTests.NoServiceControlOrInstallInSourceOrTools`) keeps
service control and install commands out of the code and scripts. Everything on this page that
changes a machine is done by a person, by hand.

The Dell settings view is the `Dell settings` region of the window (`Read Dell settings`, `Alt+S`;
see `Keyboard-Map.md`) and the command `uefiaccess dell` (see `CLI.md`). Both read the agreed list
in `Dell-Attribute-Map.md` and change nothing.

The names and values the tool shows were verified against Dell BIOS A18 on the OptiPlex 7020 (on
another BIOS version they are still read and shown, with the warning in section 1). See the
supported-hardware note in the README.

## 1. What is needed, and in which order

| Order | Part | Version verified | What it is for |
|---|---|---|---|
| 1 | Microsoft Visual C++ 2015-2022 Redistributable (x64), `vc_redist.x64.exe` | current supported release | first, because Dell Command \| Monitor installs without it and then fails: every query of its classes returns "Provider load failure" (0x80041013) within about 17 ms. Dell Command \| Configure 5.2.2 refuses to install without it |
| 2 | Dell Command \| Monitor | 10.13.2.56 | the main path: its WMI namespace `root\dcim\sysman`, class `DCIM_BIOSEnumeration`, holds the BIOS attributes (72 on A18), of which the tool shows ten |
| 3 | its data service `dcstor64` ("DSM SA Data Manager"), installed with it | start type Automatic | without it running, the class returns zero rows and no error |
| 4 (optional) | Dell Command \| Configure at `C:\Program Files (x86)\Dell\Command Configure\X86_64\cctk.exe` | 5.2.2.292 | the fallback: read only when Dell Command \| Monitor gives no answer, one read option per setting, nine of the ten settings. The tool looks for `cctk.exe` under the two Program Files folders only, never through a path variable |
| 5 | administrator rights | n/a | the application always runs elevated (its manifest); the command-line tool needs an elevated prompt. Dell Command \| Monitor registers its enumeration for administrators, and Dell Command \| Configure stops with exit code 95 without them |

On a BIOS version other than A18 the names and values are still read and shown, with the warning
`These names and values were checked against BIOS A18 only; this computer runs BIOS A17.` (naming the
version found).

## 2. Installing (by a person, never by the tool)

From an account with administrator rights, in this order:

1. The Visual C++ 2015-2022 x64 runtime: run `vc_redist.x64.exe` from Microsoft's download page,
   accept the UAC prompt and the license, and restart Windows if it asks.
2. Dell Command | Monitor 10.13.2.56: run its installer from Dell support, accept the UAC prompt,
   take the defaults. It installs `dcstor64` with start type Automatic. Restart Windows if it asks.
3. Optionally Dell Command | Configure 5.2.2: run its installer, take the defaults, so that
   `cctk.exe` is at the path in section 1.
4. Check every part as section 3 says.

Not these:

- Dell Command | Configure 3.3 or the driver package that goes with it. The tool reads both
  vocabularies of cctk's output, but 5.2.2 is the version this build was written against.
- Older Dell Command | Monitor versions (9.x). The tool was written against 10.13.2.56.
- Anything from a script in this repository: none installs anything, by design.

## 3. Checking each part, read-only

In a Windows PowerShell prompt; only the last two lines need elevation. Each line reads and changes
nothing.

The runtime:

```powershell
Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X64' | Format-List Installed, Version
```

Expected: `Installed : 1` and a version. No such key means the runtime is missing.

Dell Command | Monitor and Dell Command | Configure, as Windows lists installed programs:

```powershell
Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*' | Where-Object DisplayName -like 'Dell Command*' | Format-List DisplayName, DisplayVersion
```

The data service:

```powershell
Get-Service dcstor64 | Format-List Name, DisplayName, Status, StartType
```

Expected: `DisplayName : DSM SA Data Manager`, `Status : Running`, `StartType : Automatic`.

cctk, without running it (the file's version resource only):

```powershell
Test-Path 'C:\Program Files (x86)\Dell\Command Configure\X86_64\cctk.exe'
(Get-Item 'C:\Program Files (x86)\Dell\Command Configure\X86_64\cctk.exe').VersionInfo | Format-List FileVersion, ProductVersion
```

The whole path, in an elevated prompt, from a built checkout:

```
uefiaccess dell --source dcm
uefiaccess dell --source dcm --json
```

`--source dcm` reads through Dell Command | Monitor only and never starts cctk. Expected: exit code
0, the source `Dell Command Monitor`, and ten rows; on an A18 OptiPlex 7020 the values are `UEFI`,
`Enabled`, `AHCI`, `Enabled`, `Activated`, `Enabled`, `Enabled`, `Enabled`, `Disabled`, `Enabled`.
Exit code 5 with a sentence on standard error is section 4.

## 4. What the tool says, and what to do

When no Dell interface answers, the tool says one sentence, the same in the window (the Status box
and the state line `Dell settings source`) and on the command line (standard error, exit code 5). It
is built from parts joined with one space: `Dell BIOS settings cannot be read.`, then the cause for
each interface it tried (Dell Command | Monitor first, then Dell Command | Configure when the
fallback ran), `Reading stopped at the time limit of 120 seconds.` when the time ran out, the clause
on the built-in interface of newer models when it applies, and one piece of advice chosen by the
first problem. On a computer with neither Dell tool it reads:

`Dell BIOS settings cannot be read. Dell Command Monitor is not installed. Dell Command Configure was not found. Install the Microsoft Visual C++ 2015-2022 x64 runtime, then Dell Command Monitor, and try again; the Dell interface setup page of the documentation has the steps.`

That last advice points to this page. The causes, each with the advice it brings when it is the
first problem, and what to do:

| The tool says | It means | Advice it adds when this comes first | What to do |
|---|---|---|---|
| `They can be read on Windows only.` | the tool does not run on Windows | none | nothing; the tool reads Dell settings on Windows only |
| `This computer's manufacturer is <manufacturer>, not Dell, so there are no Dell BIOS settings to show.` | Windows names another manufacturer; neither Dell tool is tried | none | nothing: expected on a computer that is not a Dell |
| `Dell Command Monitor is not installed.` | the namespace `root\dcim\sysman` does not exist | `Install the Microsoft Visual C++ 2015-2022 x64 runtime, then Dell Command Monitor, and try again; the Dell interface setup page of the documentation has the steps.` | install sections 1 and 2's parts in that order, if Dell settings are wanted on this machine |
| `Dell Command Monitor is installed, but its BIOS settings class is missing.` | the namespace exists, the class `DCIM_BIOSEnumeration` does not | `Repair or reinstall Dell Command Monitor, then try again.` | repair it from Windows' list of installed programs, or reinstall it |
| `Dell Command Monitor is installed, but its WMI provider did not load.` | every query returns "Provider load failure": almost always the Visual C++ runtime is missing | `Install the Microsoft Visual C++ 2015-2022 x64 runtime, then try again.` | install the runtime (section 2, step 1); no reinstall of Dell Command \| Monitor is needed |
| `Windows refused access to Dell Command Monitor.` | the query was refused | `Run it as administrator, then try again.` | start the command-line tool from an elevated prompt (the application always runs elevated) |
| `Dell Command Monitor answered with no BIOS settings.` | the class answered with zero rows: its data service is not running | `Its data service, DSM SA Data Manager, is probably not running; restart Windows, then try again. This application never starts it.` | restart Windows; the service starts by itself. If `Get-Service dcstor64` still shows it stopped, repair Dell Command \| Monitor |
| `Dell Command Monitor did not answer within 60 seconds.` | the query hung; the tool sends no second query while it still runs | `Try again; if it fails again, restart Windows and try again.` | try again; then restart Windows |
| `Dell Command Monitor could not be queried.` | any other WMI error; the log has its text | `Try again; if it fails again, restart Windows and try again.` | as above; read the log line `Dell settings problem: <interface> <kind>: <detail>` |
| `Dell Command Configure was not found.` | no `cctk.exe` under the two Program Files folders | none | nothing, unless the fallback is wanted (section 2, step 3) |
| `Dell Command Configure needs administrator rights.` | cctk stopped with exit code 95 | `Run it as administrator, then try again.` | use an elevated prompt |
| `Dell Command Configure found no Dell BIOS interface on this computer.` | cctk stopped with exit code 12 or 140 | none | nothing: this computer's BIOS offers no interface to cctk |
| `Dell Command Configure does not offer these settings on this computer.` | cctk refused the options (exit code 118, 42 or 96) | none | nothing; on another BIOS, check the attribute map's cctk column |
| `Dell Command Configure did not answer within 15 seconds.` | a cctk run timed out; its process tree was ended, and no cctk runs again until the program is restarted | `Try again; if it fails again, restart Windows and try again.` | close and start the application (or the prompt's next command) again; then restart Windows |
| `Dell Command Configure stopped with an error; the log has the details.` | cctk ended with another exit code, or could not start; nothing more ran | `Try again; if it fails again, restart Windows and try again.` | read the log line that begins `Dell Command Configure run failed:` |
| `Dell Command Configure answered in a form this version does not recognize.` | the line `<name>=<value>` was missing or there were two | `Try again; if it fails again, restart Windows and try again.` | record the spelling cctk printed (the log keeps it); a spelling fix is a reviewed change of the attribute map and the list |
| `Reading stopped at the time limit of 120 seconds.` | the whole read has a budget of 120 seconds (a few seconds more at worst, while a timed-out run is ended) | `Try again; if it fails again, restart Windows and try again.` | as above |
| `An unexpected error occurred; the log has the details.` | the reader or a provider failed in a way it does not know | `Try again; if it fails again, restart Windows and try again.` | read the log and report it |
| `This computer has the built-in Dell BIOS interface of newer models, which this version does not read.` | Dell Command \| Monitor is not installed, and the namespace `root\dcim\sysman\biosattributes` of newer models exists | no install advice | nothing: this version reads that interface on no model (`Dell-Attribute-Map.md`, section 7) |

When the fallback answers, the read is not a failure: the tool says which settings it read and why
it used Dell Command | Configure, for example
`Loaded 9 of 10 Dell BIOS settings from Dell Command Configure; not reported: Boot list option. Dell Command Monitor is not installed. Dell Command Configure was used instead.`
in the window (the command line exits with 6 and prints
`Warning: Dell Command Monitor is not installed. Dell Command Configure was used instead.`), and the
state line ends with
` Dell Command Configure reports current values only, so the possible values shown are those of the attribute map for BIOS A18.`
Boot list option is never read through Dell Command | Configure (`Dell-Attribute-Map.md`, row 1).

The log of each read (the daily log file, and with `--verbose` standard error) has one line per
problem, `Dell settings problem: <interface> <kind>: <detail>`, and one summary line,
`Dell settings: <outcome> from <source> in <n> ms; <n> of 10 read; <n> problem(s); BIOS <version>`.
It never holds the service tag.

## 5. What not to do

- Never give cctk an option with `=` (a set), `-i`, `--infile`, `-o` or `--outfile`, by hand or in a
  script. The tool passes only one read option per run, with no value, and the repository's guard
  tests fail on any other cctk option in its code and scripts.
- Never call a method of Dell Command | Monitor's BIOS service class: its methods change BIOS
  settings. No code in this repository names it.
- Never start or stop `dcstor64` from a script.
- Never change a setting in the BIOS setup (F2) to make the tool's value "match": a value that
  differs is worth recording, not hiding.
- Do not install Dell Command | Configure 3.3 or an older Dell Command | Monitor (section 2).

## 6. Machines without the Dell software

Installing the Dell software is optional. Without it, `Read Dell settings` returns the
missing-interface sentence of section 4, which is the expected result on a machine that never had
the Dell software. The sample baseline under `tools\sample-baseline` lets you hear the ten Dell rows
with no Dell software at all. On a BIOS version other than A18, the `Dell settings source` state line
ends with the warning in section 1.
