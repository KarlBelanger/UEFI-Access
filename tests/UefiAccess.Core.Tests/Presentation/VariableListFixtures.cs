// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Tests.Fixtures;
using UefiAccess.Core.Variables;
using UefiAccess.Presentation.Services;
using static UefiAccess.Core.Tests.Fixtures.LoadOptionFixtures;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// A synthetic snapshot for the list view model tests: the load-option fixtures as Boot####
/// variables, BootOrder, BootCurrent, Timeout, the Secure Boot flags, PK, KEK, db, dbx, an
/// OsIndicationsSupported, two raw global variables, and one raw variable each under the Windows
/// Boot Manager GUID, the shim GUID and an unknown vendor GUID. Sizes follow from the fixture
/// bytes (Boot0001 is the 179-byte Windows Boot Manager entry documented in docs/CLI.md).
/// </summary>
internal static class VariableListFixtures
{
    /// <summary>A vendor GUID no catalog entry knows.</summary>
    public static readonly Guid UnknownVendor = new("12345678-1234-1234-1234-123456789abc");

    /// <summary>NV BS RT.</summary>
    public const VariableAttributes NvBsRt = VariableAttributes.NonVolatile | VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess;

    /// <summary>BS RT.</summary>
    public const VariableAttributes BsRt = VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess;

    /// <summary>NV BS RT TA.</summary>
    public const VariableAttributes NvBsRtTa = NvBsRt | VariableAttributes.TimeBasedAuthenticatedWriteAccess;

    /// <summary>The number of variables in <see cref="Snapshot"/>.</summary>
    public const int Count = 19;

    /// <summary>The number of variables under the EFI global variable GUID in <see cref="Snapshot"/>.</summary>
    public const int GlobalCount = 14;

    public static FirmwareVariable Global(string name, byte[] data, VariableAttributes attributes = NvBsRt) => new(name, KnownGuids.EfiGlobalVariable, attributes, data);

    public static FirmwareVariable Security(string name, byte[] data) => new(name, KnownGuids.EfiImageSecurityDatabase, NvBsRtTa, data);

    public static FirmwareVariable Boot0001() => Global("Boot0001", WindowsBootManager());

    public static FirmwareVariable Boot0002() => Global("Boot0002", Pxe());

    public static FirmwareVariable Boot0003() => Global("Boot0003", UsbStick());

    public static FirmwareVariable Boot0004() => Global("Boot0004", LegacyBbs());

    /// <summary>BootOrder = Boot0001, Boot0003, Boot0002 (6 bytes).</summary>
    public static FirmwareVariable BootOrder() => Global("BootOrder", Concat(U16(1), U16(3), U16(2)));

    public static FirmwareVariable VendorConfig() => new("VendorConfig", UnknownVendor, NvBsRt, new byte[] { 1, 2, 3 });

    /// <summary>The snapshot in no particular order (the list sorts it).</summary>
    public static IReadOnlyList<FirmwareVariable> Snapshot()
    {
        return new[]
        {
            Global("Timeout", U16(2)),
            Boot0003(),
            Security("dbx", SignatureListFixtures.Sha256List(3)),
            Global("SecureBoot", new byte[] { 1 }, BsRt),
            Boot0001(),
            BootOrder(),
            Global("PlatformLang", AsciiZ("en-US")),
            new FirmwareVariable("MokListRT", NamespaceCatalog.ShimMachineOwnerKeys, BsRt, new byte[] { 0xA1, 0x59, 0xC0, 0xA5, 0xE4 }),
            Global("KEK", Concat(SignatureListFixtures.X509List(150), SignatureListFixtures.X509List(160)), NvBsRtTa),
            Boot0004(),
            Global("BootCurrent", U16(1), BsRt),
            Global("OsIndicationsSupported", U64(0x41), BsRt),
            Security("db", Concat(SignatureListFixtures.X509List(120), SignatureListFixtures.Sha256List(3))),
            Global("SetupMode", new byte[] { 0 }, BsRt),
            new FirmwareVariable("WindowsBootChainSvn", NamespaceCatalog.MicrosoftVendor, NvBsRt, U32(1)),
            Global("PK", SignatureListFixtures.X509List(200), NvBsRtTa),
            Boot0002(),
            VendorConfig(),
            Global("ConOut", new byte[] { 0x02, 0x01, 0x0C, 0x00, 0xD0, 0x41, 0x03, 0x0A, 0x00, 0x00 }),
        };
    }
}
