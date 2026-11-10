One class per command. Each `Create(Option<bool> verbose)` builds the System.CommandLine command
with its options and runs its body through `Services/CommandRunner`, which creates the services,
runs the shared precondition check (firmware type, elevation, privilege) and maps exceptions to
exit codes. The `Create(verbose, servicesFactory)` overload takes the `CliServices` factory instead
of the production one, so `tests/UefiAccess.Core.Tests/CliCommandTests.cs` runs every command
against an `InMemoryFirmwareVariableStore` and fake inspectors (`Services/CliPlatform`), with a
scripted Dell settings reader in place of the real one, which only `CliPlatform.CreateWindows`
composes. Every command is read-only except `write`, which changes BootOrder (reorder) and
BootNext only, through Core's write pipeline (`src/UefiAccess.Core/Safety`): a dry run unless
`--confirm` is given, and with `--confirm` exactly one write call after a snapshot, then one
verification read. `dell` shows Dell BIOS settings and changes none of them.
`tests/UefiAccess.Core.Tests/CliWriteCommandTests.cs` runs it over a scripted store, a fake writer
and a fake BitLocker suspender, with the snapshot root and write-log.jsonl under the test's own
temporary folder; the real writer and suspender are composed only in `CliPlatform.CreateWindows`.

- `InfoCommand.cs`: firmware type, elevation and privilege state, machine identity, expected-model check, OS.
- `ListCommand.cs`: key, attribute flags and size of every variable; `--filter`, `--guid`.
- `ReadCommand.cs`: one variable with attributes and size; `--guid`, `--hex`, `--out`.
- `BootCommand.cs`: BootOrder resolved to descriptions, BootNext, BootCurrent, Timeout, every Boot#### decoded.
- `SecureBootCommand.cs`: Secure Boot state from the variables, the registry and PowerShell, with the agreement flag.
- `DellCommand.cs`: the agreed Dell BIOS settings, read once through Core's Dell settings reader: Dell Command Monitor, and Dell Command Configure only when Dell Command Monitor gives no answer; `--source auto|dcm|cctk` and `--all` (every attribute Dell Command Monitor reports; a usage error with the cctk source). Exit 0 Complete, 6 Partial, 5 Unavailable (the problem sentence on standard error, and with `--json` the document with `ok` false). It changes nothing. `tests/UefiAccess.Core.Tests/CliDellCommandTests.cs` runs it over a scripted reader, never the real one.
- `BaselineCommand.cs`: the timestamped baseline export, with `dell-settings.json` read through Dell Command Monitor only (the Dell settings read of a baseline never starts Dell Command Configure, while the Dell probe of `platform.json` still runs its two read options when that program is installed; Dell problems are warnings and never change the exit code); `--out`.
- `GateCommand.cs`: the feasibility gate report gate.json; `--ifr-varstores`, `--out`.
- `WriteCommand.cs`: `write bootorder --order`, `write bootnext <Boot####>` or `--clear`, `write restore <folder>`, each a dry run unless `--confirm`, with `--dry-run`, `--bitlocker suspend|continue`, `--snapshot-root` (dry run only: with `--confirm` it is a usage error) and (bootorder) `--allow-no-change`; `write check <name>` (the pre-flight and the Writability verdict; exit 7 names the check that refused, `expectedModel` on another model or BIOS version, `expectedUnit` on another OptiPlex 7020 on A18); `write log --last N` (an unreadable `write-log.jsonl` is exit 5, never an empty list; the lines that are not whole records are counted, `notRecords`, and named in one line when there are any). Ctrl+C is ignored while a confirmed change is written. Exit codes 7, 8 and 9 come from Core's `WriteExitCodes`.
- `SnapshotsCommand.cs`: the snapshot folders, newest first (by the UTC time in each `record.json`, then by folder name), with operation, time, target and outcome.
- `CommonOptions.cs`: the `--json` and `--guid` option factories.
