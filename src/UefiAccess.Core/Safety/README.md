# Safety: the write pipeline

This folder is the only part of the solution that can change a firmware variable or BitLocker's state. Everything else in `UefiAccess.Core`, the CLI and the application only reads them (the files the tool writes, such as logs, baselines and this folder's records, change nothing on the firmware). This note is the short form of the safety model; `docs/Developer-Guide.md`, section 5, has the fuller description.

## What can be written

Exactly two variables under the EFI global GUID, in three operations, and nothing else:

| Operation | Variable | Bytes | Attributes |
|---|---|---|---|
| Reorder | BootOrder | a permutation of the current entries, the same byte count as read | `0x7`, equal to the attributes read |
| One-time boot | BootNext | 2 | `0x7` |
| Clear one-time boot | BootNext | 0 (the documented meaning of size 0 without an authenticated attribute: deletion) | `0x7` |

`Whitelist` holds the two entries. No request type carries a byte array: a request names Boot#### numbers or a snapshot folder, and `BootListCodec` builds the bytes. A restore is one of the three operations, derived from a snapshot. Deleting BootOrder or any Boot####, and every other variable (Secure Boot keys and state, Dell attributes, `OsIndications`), stay read-only whatever the caller asks. There is no registry, BCD, TPM or flash write and no process launch beyond its existing callers.

## The single call site

The write import is declared once, in `Native/Kernel32.cs`, and called from one expression in `FirmwareVariableWriter.cs`, through the writer's own seam (`INativeFirmwareWriteApi`) so the read seam cannot write. The writer's one method is called from exactly one line, in `WriteTransaction.cs`. `WritePlan` has an internal constructor used only by the transaction, so the CLI, the GUI and a test have no public way to build one from bytes. The real writer and the real BitLocker suspender may be constructed only in the two production composition roots (`CliPlatform.CreateWindows`, `LiveFirmwareSource.CreateWindows`), which hand them to `WritePipeline`; nothing else in the product constructs them. A pipeline over the real writer refuses any store but `WindowsFirmwareVariableStore`. The test project never constructs them and never names the real seam; one test holds a real writer made without its constructor (no seam, never called) to pin that refusal, and the guard test allows exactly that one site. The static guard tests are tripwires for each of these facts; review of the three allow-listed files is the control.

## The files

| File | What it holds |
|---|---|
| `Whitelist.cs` | the two entries and their kinds |
| `ChangeRequest.cs` | the request types (reorder, one-time boot, clear, restore, check); none carries bytes |
| `BootListCodec.cs` | entry numbers to bytes and back, through the decoders' own reader |
| `CheckNames.cs`, `CheckSentences.cs` | the check identifiers and the refusal sentences of the rules |
| `ChangeValidator.cs` | stages S4 to S6: the whitelist, the attribute rules and the format rules V1 to V17 |
| `ChangePlan.cs`, `ConfirmationToken.cs` | the dry-run result with its `PlanHash`, and the token only `ChangePlan.Confirm` issues |
| `WritabilityPolicy.cs` | the Writability line and the row's item status |
| `PlatformPolicy.cs` | the identity and the A18 check, the unit check, Secure Boot, BitLocker and the TPM version, each read under a bound |
| `EnabledUnit.cs` | the unit check: the Windows machine ID compared with the enabled unit's hash; outside Core only the production check can be built |
| `BitLockerPreflight.cs`, `IBitLockerSuspender.cs`, `BitLockerSuspender.cs` | the BitLocker assessment and the one-restart suspend |
| `WritePlan.cs`, `IFirmwareVariableWriter.cs`, `INativeFirmwareWriteApi.cs`, `FirmwareVariableWriter.cs`, `WriteStatus.cs` | the one call, its seam and the Win32-to-class mapping with its sentences |
| `SnapshotStore.cs` | the snapshot folders: create, read back, record what S16 read, the newest restorable folder, the restore derivation |
| `WriteRecord.cs` | the structured record and `write-log.jsonl` with the write budget |
| `WriteTransaction.cs` | stages S0 to S17, the transaction's own sentences and the clock |
| `IWritePipeline.cs`, `WritePipeline.cs` | the facade the CLI and the application call, its result types and the exit codes |

## The stages

`WritePipeline` (the facade `IWritePipeline`) runs one `WriteTransaction` per request. `PrepareAsync` runs S0 to S10 and writes nothing to the firmware; `CommitAsync` runs S11 to S17 on a plan the same pipeline prepared. A failed stage up to S15's last check ends the transaction with `Blocked`, a record naming the stage, and nothing written.

| Stage | What happens |
|---|---|
| S0 | The request is typed; a `RestoreRequest` is read back with `SnapshotStore.Read` and resolved by `SnapshotStore.DeriveRestore` into a reorder, a one-time boot or a clear that carries the folder in `RestoredFrom`. |
| S1 `preconditions` | UEFI firmware, elevation, the privilege enabled, run again whatever the host did at load (exit 3 or 4 when they fail). |
| S2 `expectedModel`, `expectedUnit` | The machine identity through `PlatformPolicy` (WMI, 60-second bound): the 2014 OptiPlex 7020 on BIOS A18; then the unit check (`EnabledUnit`): the SHA-256 of the Windows machine ID (the MachineGuid value under `HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography`, 64-bit view, key opened read-only; trimmed, in lower case) must equal `PlatformPolicy.EnabledUnitMachineIdSha256`, the enabled unit, and an ID that cannot be read never confirms it. Both together give `MachineConfirmed`. The stage records both; S11 refuses, so the dry run and the dialog work on any UEFI machine. The machine ID and its hash never reach a record, a log or a snapshot: the record's machine block says `confirmed` (model and BIOS) and `unitConfirmed`. |
| S3 `enumeration` | One fresh enumeration (`NtEnumerate` only, no error, no warning) plus a direct read of BootOrder, BootNext, BootCurrent and every Boot#### the request names. An incomplete list, a known-names fallback, an in-memory or baseline set, or a failed direct read refuses. |
| S4 to S6 | The whitelist, the attribute rules and the format rules V1 to V17 of `ChangeValidator`, over the fresh set; the after-image is built from entry numbers. V1 and V5 judge the current BootOrder for a one-time boot as well as for a reorder. Rules V13, V16 and V17 need the BitLocker state, so a reorder or a one-time boot reads it before them. While protectors may exist, V13 also refuses a reorder while a one-time boot (BootNext) is set, whatever it names: the firmware starts BootNext before the order at the next start, so the entries ahead of BootCurrent's in the new order are not that start's path (clear the one-time boot first). Every variable the rules read besides the target goes into the plan with its value, an absent one included (`ChangePlan.Dependencies`). |
| S7 `secureBoot`, `baseline` | Informational: the Secure Boot findings (registry and flag variables; no PowerShell) and the target compared with the newest baseline; a difference is a warning, never a refusal. |
| S8 `bitLocker` | `BitLockerPreflight.Assess`: On, Encrypting and Unknown need a choice; Suspended keeps the warning; Off needs nothing. The command-line tool gives its `--bitlocker` choice with the request (the choice overload of `PrepareAsync`), so a missing choice refuses the dry run as well; the application's choice comes with the confirmation. The suspend is never offered for a clear. |
| S9 `snapshotRoot`, `writeBudget` | Both data roots (`snapshots\`, `writes\`) through `Logging.PrepareDataFolderFor`, created here when missing; a snapshot root outside the data folder is refused before anything is created. Then the writes made since Windows last started are counted in `write-log.jsonl`, once per transaction, and the eighth is refused. A line is of this start when it carries the current boot's identity (`bootId`, Windows' read-only BootId counter) or its time is after now minus the kernel's tick count; either rule counts it. A record whose call was made counts, and so does the Pending intent line of S12 until the transaction's own line follows it. A list that exists but cannot be read whole refuses too, and a line that is not a record counts as a write unless it shows an earlier time, or the next line that shows a time gives one; the refusal says how many such lines it counted. |
| S10 | The `ChangePlan` with its diff, findings, warnings and `PlanHash`; one record line appended; nothing written; no transaction folder. |
| S11 `confirmation`, `expectedModel`, `expectedUnit` | The plan must be this pipeline's and not yet used; the token's hash must equal the plan's and the plan's bytes their hash; a missing BitLocker choice when one is needed, a suspend that was not offered, a machine that is not the expected model on BIOS A18 (`expectedModel`), and another unit, another OptiPlex 7020 on A18 included, or a machine ID that could not be read (`expectedUnit`) refuse. A cancel here is recorded as `Cancelled`, with no folder. |
| S12 `snapshot`, `writeBudget`, `stale`, `bitLocker` | The record folder checked again; the cross-process commit lock taken (held until S17, so the application and the command-line tool never commit at once) and the write budget counted again under it; the transaction's `Pending` intent line appended to `write-log.jsonl` (a list that cannot take it refuses at `writeBudget`, since the outcome could not be recorded either); `SnapshotStore.Create`: the folder, `before\` (a complete baseline export of the S3 enumeration), `snapshot.json`, `record.json` as `Pending`, every file flushed to the disk before the stage goes on; the folder read back through `BaselineReader` with the hashes checked; then the first drift read of the target (and of BootNext beside BootOrder, and of every variable in `Dependencies`); then, for a reorder or a one-time boot, BitLocker's state read again: a state other than the dry run's refuses before any suspend. |
| S13 `bitLockerSuspend` | Only when chosen: the one suspend method with an explicit count of 1, its result confirmed by a re-read; a timeout or a non-zero result blocks with nothing written. |
| S14 `stale` | The second drift read, of the same variables, immediately before the call. |
| S15 | The last check of the plan's shape (the whitelist entry, `0x7`, the size rule of each operation, the bytes), then the one native call. No stage retries, whatever happens. |
| S16 | One verification read: `Verified`, `ApiRefused` (failed, still the old value), `ChangedDespiteError` (failed, the new value is present) or `Unverified`; the bytes read go into `after\` and `snapshot.json` is completed. A writer that ended in an exception is classed the same way by the read, and its sentence says the call ended unexpectedly, never that the firmware reported an error. |
| S17 | `record.json` rewritten with the outcome, `log.txt`, one line appended to `write-log.jsonl`, one log event, the result with its sentence and exit code. A failure here never changes the outcome or the exit code; the sentence says the record could not be saved. A file rewritten after the call replaces the S12 one only once it is whole on the disk (a temporary file, flushed, then renamed over it), so a failure or a crash leaves the S12 file readable. |

The order of S12 to S14 is deliberate: the snapshot first, so a blocked check never touches BitLocker; the first drift read before the suspend, so a change between the dialog and the confirmation never costs a suspend; the second drift read last, so the bytes compared are the bytes overwritten. A write that is blocked at S14, refused at S15, or cut short by an unexpected error after the call, after a successful suspend, leaves BitLocker suspended until the next restart, and the sentence says so; the tool never resumes it.

Exit codes (`WriteExitCodes`): 0 the dry run passed, the change was verified, or the person cancelled; 3 and 4 the preconditions; 7 a safeguard refused before any call; 8 the firmware refused and the value is unchanged; 9 the value read back is not what the call's result implies.

## Threading

Both calls run their stages on a worker thread. The dry run can take minutes on a WMI that answers slowly (S2 and S8 each have 60-second bounds); its token is checked between stages and a cancel gives a `Cancelled` record, never an exception. The commit takes no token and is never cancelled: the application cancels `Closing` while a change is written, and the command-line tool ignores Ctrl+C from S12 to S17. A process killed or a power loss in that window leaves a `Pending` folder whose `before\` (flushed to the disk before the call) is the only record of the value before the write, and a `Pending` intent line in `write-log.jsonl` that the write budget counts as a write. Nothing in the pipeline runs on a timer.

## NVRAM: the size rule, not a space check

There is no free-space check and there cannot be one: no user-mode API exposes `QueryVariableInfo`. What protects the store is the size rule: a reorder is the same byte count as the value read, a one-time boot is the smallest variable the platform can hold, and nothing else is writable, so the store's live footprint never grows by a reorder. Every write still leaves a dead record until the firmware reclaims, and whether this firmware reclaims at run time is not known; the write budget of S9 (the eighth write since Windows last started is refused) is the second protection and error 1470 is the fallback, with a sentence that says to restart before trying again. The budget's limits: a start is recognized by Windows' boot counter or by the clock, so a record whose boot counter could not be read and whose time a forward change of the Windows clock moved before the computed start drops out of the count; a write whose S17 line is missing is still counted through its S12 intent line, but a list that another program deletes or edits is beyond the count. Which Win32 code this firmware returns for each EFI status is expected, not documented; `WriteOutcome` and the write record keep the raw numbers so a real refusal corrects the table. The thread's last NTSTATUS is not cleared before the call, so a status that is not the one Windows turns into the call's Win32 code is recorded as it was read, with the remark that it may be stale.

## Evidence

`snapshots\<yyyyMMdd-HHmmss>-<operation>\` (operation `reorder`, `bootnext`, `bootnext-clear`, with `-restore` appended for a restore; the folder name is the transaction id) holds `snapshot.json`, `before\`, `after\`, `record.json` and `log.txt`. The identity in `snapshot.json` and `before\index.json` includes the service tag, as every baseline does. Nothing under `snapshots\` or `writes\` is ever committed.

Every attempt, blocked, cancelled or written, leaves one `WriteRecord` (camelCase, enums as strings; a commit that reached S12 leaves its `Pending` intent line before it) in three places: a line of `writes\write-log.jsonl`, which is the reliable list because the daily log can fall back and drop events; `record.json` in the snapshot folder once S12 created one; and one log event (Information for a verified change or a passed dry run, Warning for a refusal or a cancel, Error for the three outcomes after a failed or unconfirmed call). The record's machine block carries the model, the BIOS version and the A18 verdict, never the service tag, and no field holds key material.

`Restore previous value` takes `SnapshotStore.Newest`: the newest folder of the variable whose record says `Verified`, `Unverified`, `ChangedDespiteError` or `Pending` (the value may have changed); `Blocked`, `Cancelled` and `ApiRefused` folders wrote nothing and are skipped. Newest is by the record's UTC time, then by folder name, because the folder name is local time and the hour repeated when daylight saving time ends would otherwise put an older change first; a backward step of the clock itself can still do so, and the dialog names the folder it offers. A restore reads only a folder directly under the snapshot root that is not a link, whose `snapshot.json` target key matches its name and GUID and whose `record.json` names the same variable. The restore runs every stage against the current firmware and makes its own snapshot; a restore whose order is no longer a permutation of the list is refused at V6, and one of a `Pending` folder whose write never happened is refused at V7.

## What the tripwires cannot see

A name assembled at run time, reflection, `dynamic` or a PowerShell command held in a variable is invisible to every text scan in the test project. Review of every change to this folder, with the single call site as the thing to read, remains the real control.
