Output helpers shared by the commands.

- `TableWriter.cs`: plain aligned text tables (header row, then rows; spaces only, no box-drawing characters, no truncation).
- `JsonOutput.cs`: the single System.Text.Json configuration (indented, camelCase, enums as strings) used for `--json` output and gate.json.
- `HexDump.cs`: 16 bytes per line with offsets and an ASCII column, for `read --hex`; a thin delegate to `UefiAccess.Core.Text.HexDump`, which the GUI detail pane shares.
- `StandardStreams.cs`: makes standard output and standard error lossless before any command runs: UTF-8 without a byte order mark when redirected, UTF-16 through WriteConsoleW on a console window (the console code page is left alone).
