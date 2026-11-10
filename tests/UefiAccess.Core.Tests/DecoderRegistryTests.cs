// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Decoders;
using UefiAccess.Core.Tests.Fixtures;
using UefiAccess.Core.Variables;
using Xunit;
using static UefiAccess.Core.Tests.Fixtures.LoadOptionFixtures;

namespace UefiAccess.Core.Tests;

/// <summary>End-to-end tests over <see cref="DecoderRegistry.CreateDefault"/> with a realistic snapshot.</summary>
public sealed class DecoderRegistryTests
{
    private static readonly Guid Global = KnownGuids.EfiGlobalVariable;
    private static readonly Guid Security = KnownGuids.EfiImageSecurityDatabase;

    private static FirmwareVariable Var(string name, Guid guid, byte[] data) => new(name, guid, VariableAttributes.RuntimeAccess, data);

    private static VariableSet Snapshot() => new(new[]
    {
        Var("PlatformLang", Global, AsciiZ("en-US")),
        Var("Boot0002", Global, UsbStick()),
        Var("Boot0001", Global, WindowsBootManager()),
        Var("Boot0003", Global, LegacyBbs()),
        Var("Boot0004", Global, Pxe()),
        Var("BootOrder", Global, Concat(U16(1), U16(4), U16(2), U16(3))),
        Var("Timeout", Global, U16(2)),
        Var("SecureBoot", Global, new byte[] { 1 }),
        Var("OsIndicationsSupported", Global, U64(1)),
        Var("db", Security, SignatureListFixtures.X509List(90)),
        Var("dbx", Security, SignatureListFixtures.Sha256List(3)),
        Var("NetworkStackVar", KnownGuids.NetworkStackSetup, new byte[] { 0x01, 0x01, 0x01, 0x00, 0x00, 0x00 }),
        Var("Setup", new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), new byte[] { 1, 2, 3 }),
    });

    [Fact]
    public void CreateDefault_RegistersTheFiveDecodersInOrder()
    {
        DecoderRegistry registry = DecoderRegistry.CreateDefault();

        Assert.Equal(
            new[] { typeof(BootOrderDecoder), typeof(LoadOptionDecoder), typeof(SecureBootStateDecoder), typeof(OsIndicationsDecoder), typeof(NetworkStackDecoder) },
            registry.Decoders.Select(d => d.GetType()));
    }

    [Fact]
    public void DecodeAll_DecodesEveryAcceptedVariableInKeyOrder()
    {
        VariableSet set = Snapshot();
        IReadOnlyList<DecodedVariable> decoded = DecoderRegistry.CreateDefault().DecodeAll(set);

        string[] expectedKeys =
        {
            "8be4df61-93ca-11d2-aa0d-00e098032b8c:Boot0001",
            "8be4df61-93ca-11d2-aa0d-00e098032b8c:Boot0002",
            "8be4df61-93ca-11d2-aa0d-00e098032b8c:Boot0003",
            "8be4df61-93ca-11d2-aa0d-00e098032b8c:Boot0004",
            "8be4df61-93ca-11d2-aa0d-00e098032b8c:BootOrder",
            "8be4df61-93ca-11d2-aa0d-00e098032b8c:OsIndicationsSupported",
            "8be4df61-93ca-11d2-aa0d-00e098032b8c:SecureBoot",
            "8be4df61-93ca-11d2-aa0d-00e098032b8c:Timeout",
            "d1405d16-7afc-4695-bb12-41459d3695a2:NetworkStackVar",
            "d719b2cb-3d3a-4596-a3bc-dad00e67656f:db",
            "d719b2cb-3d3a-4596-a3bc-dad00e67656f:dbx",
        };
        Assert.Equal(expectedKeys, decoded.Select(d => d.Key));
        Assert.All(decoded, d => Assert.Null(d.Error));
        Assert.All(decoded, d => Assert.NotNull(d.Json));
        Assert.All(decoded, d => Assert.False(string.IsNullOrWhiteSpace(d.Summary)));

        Dictionary<string, DecodedVariable> byName = decoded.ToDictionary(d => d.Key[(d.Key.IndexOf(':', StringComparison.Ordinal) + 1)..], StringComparer.Ordinal);
        Assert.Equal("LoadOption", byName["Boot0001"].Kind);
        Assert.Equal("BootOrder", byName["BootOrder"].Kind);
        Assert.Equal("BootOrder", byName["Timeout"].Kind);
        Assert.Equal("SecureBootState", byName["SecureBoot"].Kind);
        Assert.Equal("SecureBootState", byName["dbx"].Kind);
        Assert.Equal("OsIndications", byName["OsIndicationsSupported"].Kind);
        Assert.Equal("NetworkStack", byName["NetworkStackVar"].Kind);
        Assert.Equal("NetworkStackVar: UEFI network stack Enabled; IPv4 PXE Enabled; IPv6 PXE Enabled", byName["NetworkStackVar"].Summary);
        Assert.Equal(
            "BootOrder: Boot0001 (Windows Boot Manager), Boot0004 (" + PxeDescription + "), Boot0002 (" + UsbStickDescription + "), Boot0003 (" + LegacyBbsDescription + ")",
            byName["BootOrder"].Summary);
    }

    [Fact]
    public void TryDecode_ReturnsNullForVariablesNoDecoderAccepts()
    {
        VariableSet set = Snapshot();
        DecoderRegistry registry = DecoderRegistry.CreateDefault();

        Assert.Null(registry.TryDecode(set.Find("PlatformLang", Global)!, set));
        Assert.Null(registry.TryDecode(Var("NetworkStackVar", Global, new byte[] { 0x01, 0x01, 0x01, 0x00, 0x00, 0x00 }), set));
        Assert.Null(registry.TryDecode(set.Find("Setup", new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"))!, set));
        Assert.Null(registry.TryDecode(null!, set));
    }

    [Fact]
    public void TryDecode_ConvertsDecoderExceptionsIntoErrors()
    {
        DecoderRegistry registry = new(new IVariableDecoder[] { new ThrowingDecoder() });
        FirmwareVariable variable = Var("Anything", Global, Array.Empty<byte>());

        DecodedVariable? decoded = registry.TryDecode(variable, VariableSet.Empty);

        Assert.NotNull(decoded);
        Assert.NotNull(decoded.Error);
        Assert.Contains("boom", decoded.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void DecodeAll_NeverThrows_OnMalformedSnapshot()
    {
        Random random = new(1);
        List<FirmwareVariable> variables = new();
        string[] names = { "BootOrder", "BootNext", "Timeout", "Boot0001", "Boot0002", "SecureBoot", "SetupMode", "PK", "KEK", "OsIndications" };
        foreach (string name in names)
        {
            byte[] bytes = new byte[random.Next(0, 64)];
            random.NextBytes(bytes);
            variables.Add(Var(name, Global, bytes));
        }

        foreach (string name in new[] { "db", "dbx", "dbt" })
        {
            byte[] bytes = new byte[random.Next(0, 200)];
            random.NextBytes(bytes);
            variables.Add(Var(name, Security, bytes));
        }

        byte[] networkStack = new byte[random.Next(0, 12)];
        random.NextBytes(networkStack);
        variables.Add(Var("NetworkStackVar", KnownGuids.NetworkStackSetup, networkStack));

        IReadOnlyList<DecodedVariable>? decoded = null;
        Exception? exception = Record.Exception(() => decoded = DecoderRegistry.CreateDefault().DecodeAll(new VariableSet(variables)));

        Assert.Null(exception);
        Assert.NotNull(decoded);
        Assert.Equal(variables.Count, decoded.Count);
    }

    private sealed class ThrowingDecoder : IVariableDecoder
    {
        public bool CanDecode(FirmwareVariable variable) => true;

        public DecodedVariable Decode(FirmwareVariable variable, IVariableLookup lookup) => throw new InvalidOperationException("boom");
    }
}
