// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Core.Variables;

/// <summary>Well-known vendor GUIDs from the UEFI 2.10 specification, and the one Dell setup GUID the tool decodes.</summary>
public static class KnownGuids
{
    /// <summary>EFI_GLOBAL_VARIABLE: BootOrder, Boot####, SecureBoot, SetupMode, PK, KEK, and the other global variables.</summary>
    public static Guid EfiGlobalVariable { get; } = new("8BE4DF61-93CA-11D2-AA0D-00E098032B8C");

    /// <summary>EFI_IMAGE_SECURITY_DATABASE_GUID: db, dbx, dbt, dbr.</summary>
    public static Guid EfiImageSecurityDatabase { get; } = new("D719B2CB-3D3A-4596-A3BC-DAD00E67656F");

    /// <summary>
    /// The vendor GUID of NetworkStackVar, the network stack setup variable of the OptiPlex 7020
    /// BIOS A18 (IFR formset Main, varstore 0x1A), which <see cref="Decoders.NetworkStackDecoder"/>
    /// decodes. Not a UEFI specification GUID.
    /// </summary>
    public static Guid NetworkStackSetup { get; } = new("D1405D16-7AFC-4695-BB12-41459D3695A2");

    /// <summary>
    /// The default vendor GUID for a variable name: the image security database GUID for
    /// db, dbx, dbt and dbr (case-sensitive), otherwise the EFI global variable GUID.
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <returns>The default vendor GUID.</returns>
    public static Guid DefaultGuidFor(string name)
    {
        return name is "db" or "dbx" or "dbt" or "dbr" ? EfiImageSecurityDatabase : EfiGlobalVariable;
    }

    /// <summary>
    /// Formats a GUID the way GetFirmwareEnvironmentVariableExW expects it: braces and uppercase hex,
    /// for example "{8BE4DF61-93CA-11D2-AA0D-00E098032B8C}".
    /// </summary>
    /// <param name="guid">The GUID to format.</param>
    /// <returns>The braced, uppercase text.</returns>
    public static string ToBracedUpper(Guid guid) => guid.ToString("B").ToUpperInvariant();
}
