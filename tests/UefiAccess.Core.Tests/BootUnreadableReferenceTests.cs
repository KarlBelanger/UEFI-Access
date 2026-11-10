// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using System.Text.RegularExpressions;
using UefiAccess.Cli;
using UefiAccess.Core.Tests.Fixtures;
using UefiAccess.Core.Variables;
using Xunit;
using static UefiAccess.Core.Tests.CliTestHarness;

namespace UefiAccess.Core.Tests;

/// <summary>
/// "uefiaccess boot" never claims that something is missing or not present when it only could not
/// be read. After an incomplete enumeration (the known-names fallback, or an enumeration that
/// stopped early at a malformed entry and so lost everything after it) BootOrder, BootCurrent,
/// BootNext and Timeout are read directly when the snapshot does not list them, as are the
/// Boot#### entries BootCurrent and BootNext reference. ERROR_ENVVAR_NOT_FOUND still means "not
/// present" or "missing entry"; any other failure is "could not be read" with the status, in the
/// summary rows, the warnings and the JSON document.
/// </summary>
public sealed class BootUnreadableReferenceTests
{
    private const VariableAttributes NvBsRt = VariableAttributes.NonVolatile | VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess;
    private const VariableAttributes BsRt = VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess;
    private const string AccessDenied = "AccessDenied: ERROR_ACCESS_DENIED (5): access denied";
    private static readonly Guid Global = KnownGuids.EfiGlobalVariable;

    // ----- BootCurrent and BootNext referencing an entry the incomplete enumeration did not list -----

    [Fact]
    public void Text_BootCurrentAndBootNextOnAnUnreadableEntry_SayCouldNotBeReadWithTheStatus()
    {
        ScriptedStore store = ScriptedStore.Fallback(
            new FirmwareVariable("Boot0000", Global, NvBsRt, LoadOptionFixtures.WindowsBootManager()),
            new FirmwareVariable("BootOrder", Global, NvBsRt, UInt16s(0x0000)),
            new FirmwareVariable("BootCurrent", Global, BsRt, UInt16s(0x0006)),
            new FirmwareVariable("BootNext", Global, NvBsRt, UInt16s(0x0007)));
        store.Failures["Boot0006"] = ReadStatus.AccessDenied;
        store.Failures["Boot0007"] = ReadStatus.Failed;

        CliRun run = Run(CreatePlatform(store), "boot");

        Assert.Equal(ExitCodes.Partial, run.ExitCode);
        AssertRow(run.Output, "BootCurrent", Regex.Escape("Boot0006 (could not be read: " + AccessDenied + ")"));
        AssertRow(run.Output, "BootNext", Regex.Escape("Boot0007 (could not be read: Failed: scripted failure)"));
        Assert.DoesNotContain("missing", run.Output, StringComparison.Ordinal);
        Assert.Contains("Warning: BootCurrent references Boot0006, which could not be read: " + AccessDenied + ".\n", run.Error, StringComparison.Ordinal);
        Assert.Contains("Warning: BootNext references Boot0007, which could not be read: Failed: scripted failure.\n", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("does not exist", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_BootCurrentAndBootNextOnAnUnreadableEntry_AreNotedUnreadableWithTheStatus()
    {
        ScriptedStore store = ScriptedStore.Fallback(
            new FirmwareVariable("BootOrder", Global, NvBsRt, UInt16s(0x0006)),
            new FirmwareVariable("BootCurrent", Global, BsRt, UInt16s(0x0006)),
            new FirmwareVariable("BootNext", Global, NvBsRt, UInt16s(0x0007)));
        store.Failures["Boot0006"] = ReadStatus.AccessDenied;
        store.Failures["Boot0007"] = ReadStatus.AccessDenied;

        CliRun run = Run(CreatePlatform(store), "boot", "--json");

        JsonElement document = ParseSingleDocument(run.Output);
        foreach ((string property, string entry) in new[] { ("bootCurrent", "Boot0006"), ("bootNext", "Boot0007") })
        {
            JsonElement reference = document.GetProperty(property);
            Assert.True(reference.GetProperty("present").GetBoolean());
            Assert.Equal(entry, reference.GetProperty("name").GetString());
            Assert.False(reference.GetProperty("entryExists").GetBoolean());
            Assert.Equal("unreadable", reference.GetProperty("note").GetString());
            Assert.Equal("could not be read: " + AccessDenied, reference.GetProperty("error").GetString());
        }

        // Boot0006 is referenced twice but read once.
        Assert.Equal(1, store.Reads.Count(name => name == "Boot0006"));
        Assert.Equal("unreadable", document.GetProperty("bootOrder")[0].GetProperty("note").GetString());
    }

    [Fact]
    public void BootNextOnAnEntryTheDirectReadDidNotFind_IsAMissingEntry()
    {
        ScriptedStore store = ScriptedStore.Fallback(
            new FirmwareVariable("BootOrder", Global, NvBsRt, UInt16s(0x0000)),
            new FirmwareVariable("Boot0000", Global, NvBsRt, LoadOptionFixtures.WindowsBootManager()),
            new FirmwareVariable("BootNext", Global, NvBsRt, UInt16s(0x0008)));

        CliRun text = Run(CreatePlatform(store), "boot");
        CliRun json = Run(CreatePlatform(store), "boot", "--json");

        AssertRow(text.Output, "BootNext", Regex.Escape("Boot0008 (missing entry)"));
        Assert.Contains("Warning: BootNext references Boot0008, which does not exist.\n", text.Error, StringComparison.Ordinal);
        JsonElement bootNext = ParseSingleDocument(json.Output).GetProperty("bootNext");
        Assert.Equal("missing", bootNext.GetProperty("note").GetString());
        Assert.Equal(JsonValueKind.Null, bootNext.GetProperty("error").ValueKind);
    }

    [Fact]
    public void CompleteEnumeration_ReferenceToAnUnlistedEntry_IsMissingWithoutADirectRead()
    {
        ScriptedStore store = new(new EnumerationResult(
            new[]
            {
                new FirmwareVariable("BootOrder", Global, NvBsRt, UInt16s(0x0000)),
                new FirmwareVariable("Boot0000", Global, NvBsRt, LoadOptionFixtures.WindowsBootManager()),
                new FirmwareVariable("BootCurrent", Global, BsRt, UInt16s(0x0003)),
            },
            EnumerationMethod.NtEnumerate,
            3,
            null,
            Array.Empty<string>()));

        CliRun run = Run(CreatePlatform(store), "boot", "--json");

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Empty(store.Reads);
        JsonElement document = ParseSingleDocument(run.Output);
        Assert.Equal("missing", document.GetProperty("bootCurrent").GetProperty("note").GetString());
        Assert.False(document.GetProperty("bootNext").GetProperty("present").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("bootNext").GetProperty("note").ValueKind);
        Assert.False(document.GetProperty("timeoutPresent").GetBoolean());
        Assert.True(document.GetProperty("bootOrderPresent").GetBoolean());
    }

    // ----- BootOrder, BootCurrent, BootNext and Timeout themselves after an early stop -----

    [Fact]
    public void EarlyStop_ReadsTheUnlistedBootVariablesDirectly_AndUsesWhatItFinds()
    {
        ScriptedStore store = EarlyStop(new FirmwareVariable("Boot0000", Global, NvBsRt, LoadOptionFixtures.WindowsBootManager()));
        store.Readable.Add(new FirmwareVariable("BootOrder", Global, NvBsRt, UInt16s(0x0000, 0x0001)));
        store.Readable.Add(new FirmwareVariable("Boot0001", Global, NvBsRt, LoadOptionFixtures.UsbStick()));
        store.Readable.Add(new FirmwareVariable("BootCurrent", Global, BsRt, UInt16s(0x0001)));
        store.Readable.Add(new FirmwareVariable("Timeout", Global, NvBsRt, UInt16s(5)));

        CliRun run = Run(CreatePlatform(store), "boot");

        Assert.Equal(ExitCodes.Partial, run.ExitCode);
        Assert.Equal(new[] { "BootOrder", "BootCurrent", "BootNext", "Timeout", "Boot0001" }, store.Reads);
        AssertRow(run.Output, "BootCurrent", Regex.Escape("Boot0001 (" + LoadOptionFixtures.UsbStickDescription + ")"));
        AssertRow(run.Output, "BootNext", Regex.Escape("(not set)"));
        AssertRow(run.Output, "Timeout", "5 seconds");
        AssertRow(run.Output, "BootOrder", "Boot0000, Boot0001");
        Assert.DoesNotContain("not present", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("could not be read", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void EarlyStop_UnreadableBootVariables_SayCouldNotBeReadInsteadOfNotPresent()
    {
        ScriptedStore store = EarlyStop(new FirmwareVariable("Boot0000", Global, NvBsRt, LoadOptionFixtures.WindowsBootManager()));
        store.Failures["BootOrder"] = ReadStatus.AccessDenied;
        store.Failures["BootCurrent"] = ReadStatus.AccessDenied;
        store.Failures["BootNext"] = ReadStatus.Failed;
        store.Failures["Timeout"] = ReadStatus.TooLarge;

        CliRun run = Run(CreatePlatform(store), "boot");

        Assert.Equal(ExitCodes.Partial, run.ExitCode);
        AssertRow(run.Output, "BootCurrent", Regex.Escape("(could not be read: " + AccessDenied + ")"));
        AssertRow(run.Output, "BootNext", Regex.Escape("(could not be read: Failed: scripted failure)"));
        AssertRow(run.Output, "Timeout", Regex.Escape("(could not be read: TooLarge: scripted failure)"));
        AssertRow(run.Output, "BootOrder", Regex.Escape("(could not be read: " + AccessDenied + ")"));
        Assert.Contains("\nBoot order:\n(BootOrder could not be read)\n", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("not present", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("not set", run.Output, StringComparison.Ordinal);
        Assert.Contains("Warning: BootOrder could not be read: " + AccessDenied + ".\n", run.Error, StringComparison.Ordinal);
        Assert.Contains("Warning: BootCurrent could not be read: " + AccessDenied + ".\n", run.Error, StringComparison.Ordinal);
        Assert.Contains("Warning: BootNext could not be read: Failed: scripted failure.\n", run.Error, StringComparison.Ordinal);
        Assert.Contains("Warning: Timeout could not be read: TooLarge: scripted failure.\n", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("not present", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void EarlyStop_UnreadableBootVariables_AreNullNotFalseInJson()
    {
        ScriptedStore store = EarlyStop();
        store.Failures["BootOrder"] = ReadStatus.AccessDenied;
        store.Failures["BootCurrent"] = ReadStatus.AccessDenied;
        store.Failures["Timeout"] = ReadStatus.AccessDenied;

        CliRun run = Run(CreatePlatform(store), "boot", "--json");

        JsonElement document = ParseSingleDocument(run.Output);
        Assert.Equal(JsonValueKind.Null, document.GetProperty("bootOrderPresent").ValueKind);
        Assert.Equal("could not be read: " + AccessDenied, document.GetProperty("bootOrderError").GetString());
        Assert.Empty(document.GetProperty("bootOrder").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("timeoutPresent").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.GetProperty("timeoutSeconds").ValueKind);
        Assert.Equal("could not be read: " + AccessDenied, document.GetProperty("timeoutError").GetString());

        JsonElement bootCurrent = document.GetProperty("bootCurrent");
        Assert.Equal(JsonValueKind.Null, bootCurrent.GetProperty("present").ValueKind);
        Assert.Equal("could not be read: " + AccessDenied, bootCurrent.GetProperty("error").GetString());
        Assert.Equal(JsonValueKind.Null, bootCurrent.GetProperty("note").ValueKind);

        // BootNext was not found by the direct read: that is a confirmed absence.
        Assert.False(document.GetProperty("bootNext").GetProperty("present").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("bootNext").GetProperty("error").ValueKind);

        Assert.Equal(
            new[]
            {
                "BootOrder could not be read: " + AccessDenied + ".",
                "BootCurrent could not be read: " + AccessDenied + ".",
                "Timeout could not be read: " + AccessDenied + ".",
            },
            document.GetProperty("warnings").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    [Fact]
    public void EarlyStop_BootVariablesTheDirectReadDidNotFind_AreNotPresent()
    {
        ScriptedStore store = EarlyStop();

        CliRun run = Run(CreatePlatform(store), "boot");

        Assert.Equal(new[] { "BootOrder", "BootCurrent", "BootNext", "Timeout" }, store.Reads);
        AssertRow(run.Output, "BootCurrent", Regex.Escape("(not present)"));
        AssertRow(run.Output, "BootNext", Regex.Escape("(not set)"));
        AssertRow(run.Output, "Timeout", Regex.Escape("(not present)"));
        AssertRow(run.Output, "BootOrder", Regex.Escape("(not present)"));
        Assert.Contains("\nBoot order:\n(BootOrder not present)\n", run.Output, StringComparison.Ordinal);
        Assert.Contains("Warning: BootOrder is not present.\n", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("could not be read", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedBootCurrent_IsStillReportedAsMalformed()
    {
        ScriptedStore store = EarlyStop(new FirmwareVariable("BootCurrent", Global, BsRt, new byte[] { 0x01, 0x00, 0x00, 0x00 }));

        CliRun run = Run(CreatePlatform(store), "boot", "--json");
        CliRun text = Run(CreatePlatform(store), "boot");

        JsonElement bootCurrent = ParseSingleDocument(run.Output).GetProperty("bootCurrent");
        Assert.True(bootCurrent.GetProperty("present").GetBoolean());
        Assert.Equal(JsonValueKind.Null, bootCurrent.GetProperty("index").ValueKind);
        Assert.Equal("BootCurrent has 4 bytes; expected 2.", bootCurrent.GetProperty("error").GetString());
        Assert.Equal(JsonValueKind.Null, bootCurrent.GetProperty("note").ValueKind);
        AssertRow(text.Output, "BootCurrent", Regex.Escape("malformed: BootCurrent has 4 bytes; expected 2."));
    }

    /// <summary>An enumeration that stopped early at a malformed entry and kept <paramref name="listed"/>.</summary>
    private static ScriptedStore EarlyStop(params FirmwareVariable[] listed)
    {
        ScriptedStore store = new(new EnumerationResult(
            listed,
            EnumerationMethod.NtEnumerate,
            listed.Length,
            null,
            new[] { "enumeration stopped early at a malformed entry; the variables before it are reported: entry " + (listed.Length + 1) + " has a name that is not terminated" }));
        store.Readable.AddRange(listed);
        return store;
    }

    private static byte[] UInt16s(params ushort[] values)
    {
        return values.SelectMany(value => new[] { (byte)(value & 0xFF), (byte)(value >> 8) }).ToArray();
    }
}
