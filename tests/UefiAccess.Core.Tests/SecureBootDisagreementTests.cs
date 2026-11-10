// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using UefiAccess.Cli;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Variables;
using Xunit;
using static UefiAccess.Core.Tests.CliTestHarness;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The documented contract of "uefiaccess secureboot" for a disagreement between the decoded
/// SecureBoot variable and the policy inspector's own read of it: one Warning line without the
/// "policy:" prefix, the same text in the JSON warnings, and no change to the exit code (only a
/// failed policy source makes it 6).
/// </summary>
public sealed class SecureBootDisagreementTests
{
    [Fact]
    public void Disagreement_WarnsWithoutThePolicyPrefix_AndKeepsExitZero()
    {
        VariableAttributes bsRt = VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess;
        InMemoryFirmwareVariableStore store = new(new[]
        {
            new FirmwareVariable("SecureBoot", KnownGuids.EfiGlobalVariable, bsRt, new byte[] { 1 }),
            new FirmwareVariable("SetupMode", KnownGuids.EfiGlobalVariable, bsRt, new byte[] { 0 }),
        });
        SecureBootPolicyResult policy = new(true, false, false, null, null, true, true, false, Array.Empty<string>());

        CliRun run = Run(CreatePlatform(store, policy: policy), "secureboot", "--json");

        const string Text = "The SecureBoot variable decoded as true but the policy inspector read false.";
        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Equal("Warning: " + Text + "\n", run.Error);
        JsonElement document = ParseSingleDocument(run.Output);
        Assert.Equal(new[] { Text }, document.GetProperty("warnings").EnumerateArray().Select(e => e.GetString()).ToArray());
    }
}
