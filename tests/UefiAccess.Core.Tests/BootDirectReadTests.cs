// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using UefiAccess.Cli;
using UefiAccess.Core.Tests.Fixtures;
using UefiAccess.Core.Variables;
using Xunit;
using static UefiAccess.Core.Tests.CliTestHarness;

namespace UefiAccess.Core.Tests;

/// <summary>
/// "uefiaccess boot" after an incomplete enumeration: the known-names fallback stops probing
/// Boot#### after three consecutive misses, so an entry past a gap is not listed. Every Boot####
/// that BootOrder, BootCurrent or BootNext references but the snapshot lacks is read directly:
/// found entries are decoded, "not found" stays missing, and any other failure is "unreadable",
/// never "does not exist". A complete enumeration reads nothing directly.
/// </summary>
public sealed class BootDirectReadTests
{
    private const VariableAttributes NvBsRt = VariableAttributes.NonVolatile | VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess;
    private static readonly Guid Global = KnownGuids.EfiGlobalVariable;

    [Fact]
    public void Fallback_ReadsReferencedEntriesPastTheProbeGap()
    {
        ScriptedStore store = ScriptedStore.Fallback(
            new FirmwareVariable("Boot0000", Global, NvBsRt, LoadOptionFixtures.WindowsBootManager()),
            new FirmwareVariable("BootOrder", Global, NvBsRt, UInt16s(0x0000, 0x0005, 0x0006, 0x0007)),
            new FirmwareVariable("BootNext", Global, NvBsRt, UInt16s(0x0005)));
        store.Readable.Add(new FirmwareVariable("Boot0005", Global, NvBsRt, LoadOptionFixtures.UsbStick()));
        store.Failures["Boot0006"] = ReadStatus.AccessDenied;

        CliRun run = Run(CreatePlatform(store), "boot", "--json");

        Assert.Equal(ExitCodes.Partial, run.ExitCode);
        // BootCurrent and Timeout are not listed either, so they are read directly first (both not found).
        Assert.Equal(new[] { "BootCurrent", "Timeout", "Boot0005", "Boot0006", "Boot0007" }, store.Reads);
        Assert.Contains("Warning: BootOrder position 3 references Boot0006, which could not be read: AccessDenied: ERROR_ACCESS_DENIED (5): access denied.\n", run.Error, StringComparison.Ordinal);
        Assert.Contains("Warning: BootOrder position 4 references Boot0007, which does not exist.\n", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("Boot0005, which", run.Error, StringComparison.Ordinal);

        JsonElement document = ParseSingleDocument(run.Output);
        JsonElement[] order = document.GetProperty("bootOrder").EnumerateArray().ToArray();
        Assert.True(order[1].GetProperty("entryExists").GetBoolean());
        Assert.Equal(JsonValueKind.Null, order[1].GetProperty("note").ValueKind);
        Assert.Equal("unreadable", order[2].GetProperty("note").GetString());
        Assert.Equal("missing", order[3].GetProperty("note").GetString());
        Assert.Equal(new[] { "Boot0000", "Boot0005" }, document.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("name").GetString()).ToArray());
        Assert.True(document.GetProperty("bootNext").GetProperty("entryExists").GetBoolean());
    }

    [Fact]
    public void Fallback_TextMarksUnreadableEntriesApartFromMissingOnes()
    {
        ScriptedStore store = ScriptedStore.Fallback(new FirmwareVariable("BootOrder", Global, NvBsRt, UInt16s(0x0006, 0x0007)));
        store.Failures["Boot0006"] = ReadStatus.AccessDenied;

        CliRun run = Run(CreatePlatform(store), "boot");

        AssertRow(run.Output, "BootOrder", "Boot0006 \\(unreadable\\), Boot0007 \\(missing\\)");
        Assert.Matches(@"(?m)^1 +Boot0006 +\(unreadable: Boot0006 could not be read\)$", run.Output);
        Assert.Matches(@"(?m)^2 +Boot0007 +\(missing: no Boot0007 variable\)$", run.Output);
    }

    [Fact]
    public void CompleteEnumeration_NeverReadsDirectly()
    {
        ScriptedStore store = new(new EnumerationResult(
            new[] { new FirmwareVariable("BootOrder", Global, NvBsRt, UInt16s(0x0005)) },
            EnumerationMethod.NtEnumerate,
            1,
            null,
            Array.Empty<string>()));
        store.Readable.Add(new FirmwareVariable("Boot0005", Global, NvBsRt, LoadOptionFixtures.UsbStick()));

        CliRun run = Run(CreatePlatform(store), "boot");

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Empty(store.Reads);
        Assert.Equal("Warning: BootOrder position 1 references Boot0005, which does not exist.\n", run.Error);
    }

    private static byte[] UInt16s(params ushort[] values)
    {
        return values.SelectMany(value => new[] { (byte)(value & 0xFF), (byte)(value >> 8) }).ToArray();
    }
}
