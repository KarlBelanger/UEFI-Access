// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using UefiAccess.Cli;
using UefiAccess.Core.Variables;
using Xunit;
using static UefiAccess.Core.Tests.CliTestHarness;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The --json contract for a command line that does not parse: standard output still carries
/// exactly one document, the error document with exit code 2 and no precondition fields, so a
/// captured list.json is valid JSON. Without --json standard output stays empty as before.
/// </summary>
public sealed class CliJsonParseErrorTests
{
    [Fact]
    public void UnknownOption_WithJson_WritesTheErrorDocument()
    {
        CliRun run = Run(CreatePlatform(new InMemoryFirmwareVariableStore(Array.Empty<FirmwareVariable>())), "list", "--json", "--bogus");

        Assert.Equal(ExitCodes.Usage, run.ExitCode);
        Assert.Contains("Run 'uefiaccess --help' for usage.", run.Error, StringComparison.Ordinal);
        JsonElement document = ParseSingleDocument(run.Output);
        Assert.Equal(
            new[] { "command", "ok", "exitCode", "error", "firmwareType", "elevated", "privilegeState" },
            document.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("list", document.GetProperty("command").GetString());
        Assert.False(document.GetProperty("ok").GetBoolean());
        Assert.Equal(ExitCodes.Usage, document.GetProperty("exitCode").GetInt32());
        Assert.Contains("--bogus", document.GetProperty("error").GetString(), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, document.GetProperty("firmwareType").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.GetProperty("elevated").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.GetProperty("privilegeState").ValueKind);
    }

    [Fact]
    public void MissingArgument_WithJson_NamesTheCommand()
    {
        CliRun run = Run(CreatePlatform(new InMemoryFirmwareVariableStore(Array.Empty<FirmwareVariable>())), "read", "--json");

        Assert.Equal(ExitCodes.Usage, run.ExitCode);
        JsonElement document = ParseSingleDocument(run.Output);
        Assert.Equal("read", document.GetProperty("command").GetString());
        Assert.Equal(ExitCodes.Usage, document.GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public void UnknownCommand_WithJson_UsesTheToolName()
    {
        CliRun run = Run(CreatePlatform(new InMemoryFirmwareVariableStore(Array.Empty<FirmwareVariable>())), "bogus", "--json");

        Assert.Equal(ExitCodes.Usage, run.ExitCode);
        Assert.Equal(Program.ToolName, ParseSingleDocument(run.Output).GetProperty("command").GetString());
    }

    [Fact]
    public void ParseError_WithoutJson_LeavesStandardOutputEmpty()
    {
        CliRun run = Run(CreatePlatform(new InMemoryFirmwareVariableStore(Array.Empty<FirmwareVariable>())), "list", "--bogus");

        Assert.Equal(ExitCodes.Usage, run.ExitCode);
        Assert.Equal(string.Empty, run.Output);
    }
}
