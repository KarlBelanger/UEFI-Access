# Contributing to UEFI-Access

Thank you for your interest in UEFI-Access. This is a firmware tool for blind and low-vision users, so
accessibility and the safety of the write path matter as much as the feature itself. Please read this
page before opening a pull request.

## Developer Certificate of Origin (sign-off)

Contributions are accepted under the Developer Certificate of Origin (DCO). The DCO is a simple
statement that you wrote the contribution, or otherwise have the right to submit it under the
project's license. The full text is in [DCO.txt](DCO.txt).

You certify the DCO by signing off each commit:

```
git commit -s -m "Your commit message"
```

`-s` adds a line of the form `Signed-off-by: Your Name <your.email@example.com>` to the commit
message, using your `user.name` and `user.email` from Git. Every commit in a pull request must be
signed off. There is no separate CLA to sign.

## License of contributions

By contributing, you agree that your contributions are licensed under the Mozilla Public License,
version 2.0, the same license as the project (see [LICENSE](LICENSE)).

## Development

- Build and test from the repository root with the .NET 8 SDK (x64):

  ```
  dotnet build UefiAccess.sln -c Release
  dotnet test UefiAccess.sln -c Release
  ```

  The build must end with 0 warnings and 0 errors (`TreatWarningsAsErrors` is on), and every test
  must pass. Projects under `src/` generate XML documentation, so every public member needs a `///`
  doc comment. [docs/BUILD.md](docs/BUILD.md) has the prerequisites, the reasons and a troubleshooting
  table.
- Architecture and how to extend the tool are in [docs/Developer-Guide.md](docs/Developer-Guide.md),
  section 8 (adding a decoder, a Dell field, a command or a window region, supporting another firmware
  vendor, and adding a new changeable setting). Accessibility behavior and the keyboard contract are
  in [docs/Keyboard-Map.md](docs/Keyboard-Map.md).

## The safety discipline (read before touching the write path)

The tool can change exactly two firmware variables, through one guarded pipeline, and the whole write
path lives in three files: `Native/Kernel32.cs`, `Safety/FirmwareVariableWriter.cs` and
`Safety/BitLockerSuspender.cs`. Static guard tests pin this (see the README's "How that is enforced"
and [docs/Developer-Guide.md](docs/Developer-Guide.md), section 5).

- A change to the write pipeline, the whitelist, the guard tests, or the `cctk` allow-list goes in a
  commit of its own, reviewed line by line, separate from the rest of a change.
- Never widen the whitelist, the write path, the WMI method allow-list or the `cctk` option
  allow-list without that review. A change that adds a writable setting is the most sensitive change
  in the project; follow [docs/Developer-Guide.md](docs/Developer-Guide.md), section 8.5.
- Keep every reading path read-only: WMI only through `WmiHelper`, processes only through
  `ProcessRunner`, every call bounded, malformed data returned as an error object rather than thrown.
- Firmware writes are verified only on the supported hardware (see the README). Do not change the
  enabled-unit check, or enable writes on another model, without reading
  [docs/Developer-Guide.md](docs/Developer-Guide.md), section 5.2.

## Reporting issues

Please include the tool version (`uefiaccess --version`), the machine model and BIOS version, the
exact command or key pressed, and the relevant lines from the daily log under
`%ProgramData%\UefiAccess\logs`. For anything that affects whether a machine starts, see
[docs/Recovery.md](docs/Recovery.md) first.
