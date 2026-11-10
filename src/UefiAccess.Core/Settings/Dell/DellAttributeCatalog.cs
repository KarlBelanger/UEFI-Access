// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Core.Settings.Dell;

/// <summary>One value of an agreed setting: the words of the BIOS setup screen and the words each Dell interface uses for it.</summary>
/// <param name="Text">The words shown and spoken, as the BIOS setup screen says them.</param>
/// <param name="DcmName">Dell Command | Monitor's name for the value (its PossibleValuesDescription), matched case-insensitively; null when it has none.</param>
/// <param name="CctkTokens">The words Dell Command | Configure prints for the value, the 5.2.2 word first and then the legacy one, matched case-insensitively; empty when the setting is not read that way.</param>
public sealed record DellValueText(string Text, string? DcmName, IReadOnlyList<string> CctkTokens);

/// <summary>
/// One agreed Dell BIOS setting: how it is shown, where it lives in the
/// BIOS setup, and how each Dell interface names it. Values are always mapped by name, never by
/// code: Dell Command | Monitor's codes, the setup forms' values and Dell Command | Configure's
/// words are three separate vocabularies.
/// </summary>
/// <param name="Id">The catalog id, stable across versions, for example "NumLock".</param>
/// <param name="DisplayName">The name shown and spoken, the gate memo's.</param>
/// <param name="Category">The BIOS setup page group.</param>
/// <param name="SetupLocation">Where the setting is in the BIOS setup, page names joined with ", ".</param>
/// <param name="DcmAttributeName">Dell Command | Monitor's AttributeName.</param>
/// <param name="Values">The values, in the order of the BIOS setup.</param>
/// <param name="DependsOnId">The entry this one depends on: it is shown only while that entry reads <paramref name="DependsOnText"/>; null when it depends on none.</param>
/// <param name="DependsOnText">The text of <paramref name="DependsOnId"/> while which this entry is shown; null when it depends on none.</param>
/// <param name="DcmNote">A sentence said after the row when Dell Command | Monitor read it; null when none.</param>
/// <param name="NoValueNote">A sentence said after the row when the interface reported no current value; null when none.</param>
public sealed record DellCatalogEntry(
    string Id,
    string DisplayName,
    string Category,
    string SetupLocation,
    string DcmAttributeName,
    IReadOnlyList<DellValueText> Values,
    string? DependsOnId,
    string? DependsOnText,
    string? DcmNote,
    string? NoValueNote)
{
    /// <summary>The read option of Dell Command | Configure for this setting; null when the fallback does not read it (deviation D9).</summary>
    internal CctkReadOption? CctkOption { get; init; }

    /// <summary>The names the read option's output line may carry before its "=", the 5.2.2 name first and then the legacy one, matched case-insensitively.</summary>
    internal IReadOnlyList<string> CctkOutputNames { get; init; } = Array.Empty<string>();
}

/// <summary>
/// The agreed list of Dell BIOS settings as data: the
/// gate memo's eight items as ten rows, because TPM and Virtualization each have two Dell
/// attributes. Display names are the gate memo's; the values use the words of the BIOS setup
/// screen; the page paths come from the setup forms (IFR) and are confirmed by the photos of
/// protocol step M4-H5, where a correction is a one-line change here. Every Dell Command |
/// Configure name and word comes from syscfg.xml of version 5.2.2. Changing the list is this
/// table, one fixture row, one attribute-map section and one check in the BIOS setup.
/// </summary>
public static class DellAttributeCatalog
{
    /// <summary>The BIOS version the names and values were checked against.</summary>
    public const string VerifiedBiosVersion = "A18";

    /// <summary>Whether the list is agreed.</summary>
    public const string ListStatus = "Approved list of the Dell BIOS settings this tool shows";

    /// <summary>The value texts of a plain off and on setting, in the order of the BIOS setup.</summary>
    private static readonly IReadOnlyList<DellValueText> DisabledEnabled = Values(
        Value("Disabled", "Disable", "Disabled", "disable", "off"),
        Value("Enabled", "Enable", "Enabled", "enable", "on"));

    private static readonly DellCatalogEntry[] Entries =
    {
        // Boot list option has no read option of its own (deviation D9).
        new(
            "BootListOption",
            "Boot list option",
            "General",
            "General, Boot Sequence, Boot List Option",
            "Boot Mode",
            Values(Value("Legacy", "BIOS"), Value("UEFI", "UEFI")),
            null,
            null,
            null,
            null),
        new(
            "SecureBoot",
            "Secure Boot",
            "Secure Boot",
            "Secure Boot, Secure Boot Enable",
            "Secure Boot",
            DisabledEnabled,
            null,
            null,
            DellSentences.SecureBootDcmNote,
            DellSentences.SecureBootNoValueNote)
        {
            CctkOption = CctkReadOption.SecureBoot,
            CctkOutputNames = Names("SecureBoot", "secureboot"),
        },
        new(
            "SataOperation",
            "SATA operation",
            "System Configuration",
            "System Configuration, SATA Operation",
            "Embedded SATA Controller",
            Values(Value("Disabled", "Off", "Disabled", "off"), Value("ATA", "ATA", "Ata"), Value("AHCI", "AHCI", "Ahci")),
            null,
            null,
            null,
            null)
        {
            CctkOption = CctkReadOption.SataOperation,
            CctkOutputNames = Names("EmbSataRaid", "embsataraid"),
        },
        new(
            "TpmSecurity",
            "TPM security",
            "Security",
            "Security, TPM Security",
            "Trusted Platform Module",
            Values(Value("Enabled", "Enable", "Enabled", "enable", "on"), Value("Disabled", "Disable", "Disabled", "disable", "off")),
            null,
            null,
            null,
            null)
        {
            CctkOption = CctkReadOption.TpmSecurity,
            CctkOutputNames = Names("TpmSecurity", "tpm"),
        },

        // The setup screen's options are "Deactivate" and "Activate"; the row says "Deactivated"
        // and "Activated", because a row that says "Activate" sounds like a command.
        new(
            "TpmActivation",
            "TPM activation",
            "Security",
            "Security, TPM Security, TPM State",
            "Trusted Platform Module Activation",
            Values(Value("Deactivated", "Deactivate", "Disabled", "deactivated", "Deactivate"), Value("Activated", "Activate", "Enabled", "activate", "Activated")),
            "TpmSecurity",
            "Enabled",
            null,
            null)
        {
            CctkOption = CctkReadOption.TpmActivation,
            CctkOutputNames = Names("TpmActivation", "tpmactivation"),
        },
        new(
            "Virtualization",
            "Virtualization",
            "Virtualization Support",
            "Virtualization Support, Virtualization",
            "Processor Virtualization Technology",
            DisabledEnabled,
            null,
            null,
            null,
            null)
        {
            CctkOption = CctkReadOption.Virtualization,
            CctkOutputNames = Names("Virtualization", "virtualization"),
        },
        new(
            "VtForDirectIo",
            "VT for Direct I/O",
            "Virtualization Support",
            "Virtualization Support, VT for Direct I/O",
            "VT for Direct IO",
            DisabledEnabled,
            null,
            null,
            null,
            null)
        {
            CctkOption = CctkReadOption.VtForDirectIo,
            CctkOutputNames = Names("VtForDirectIo", "vtfordirectio"),
        },

        // "Enable image server" is probably the setup screen's "Enabled w/Cloud Desktop"
        // (inferred); the text stays "Enabled with image server" until step M4-H5.
        new(
            "IntegratedNic",
            "Integrated NIC",
            "System Configuration",
            "System Configuration, Integrated NIC",
            "NIC",
            Values(
                Value("Disabled", "Disable", "Disabled", "off"),
                Value("Enabled with PXE", "EnabledwithPXE", "EnabledPxe", "on"),
                Value("Enabled", "Enable", "Enabled", "onnopxe"),
                Value("Enabled with image server", "Enable image server", "OnWithImageServerBoot")),
            null,
            null,
            null,
            null)
        {
            CctkOption = CctkReadOption.IntegratedNic,
            CctkOutputNames = Names("EmbNic1", "embnic1"),
        },

        // Dell Command | Monitor's "LAN" is shown as the setup screen's "LAN Only" (inferred:
        // without WLAN hardware the setup question offers only these three options); the
        // attribute map marks the mapping NOT VERIFIED until step M4-H5.
        new(
            "WakeOnLan",
            "Wake on LAN",
            "Power Management",
            "Power Management, Wake on LAN",
            "Wake-On-LAN",
            Values(
                Value("Disabled", "Disable", "Disabled", "disable"),
                Value("LAN Only", "LAN", "LanOnly", "enable"),
                Value("LAN with PXE Boot", "LAN with PXE Boot", "LanWithPxeBoot", "lanwithpxeboot")),
            null,
            null,
            null,
            null)
        {
            CctkOption = CctkReadOption.WakeOnLan,
            CctkOutputNames = Names("WakeOnLan", "wakeonlan"),
        },
        new(
            "NumLock",
            "Num Lock at boot",
            "POST Behavior",
            "POST Behavior, Numlock LED",
            "Num Lock",
            DisabledEnabled,
            null,
            null,
            null,
            null)
        {
            CctkOption = CctkReadOption.NumLock,
            CctkOutputNames = Names("NumLock", "numlock"),
        },
    };

    private static readonly IReadOnlyDictionary<string, DellCatalogEntry> ById = Entries.ToDictionary(entry => entry.Id, StringComparer.Ordinal);

    /// <summary>The ten agreed settings, in the order they are shown.</summary>
    public static IReadOnlyList<DellCatalogEntry> Default { get; } = Array.AsReadOnly(Entries);

    /// <summary>The agreed setting with an id, compared ordinally.</summary>
    /// <param name="id">The catalog id, for example "NumLock".</param>
    /// <returns>The entry, or null when no agreed setting has that id.</returns>
    public static DellCatalogEntry? Find(string id)
    {
        return id is not null && ById.TryGetValue(id, out DellCatalogEntry? entry) ? entry : null;
    }

    private static DellValueText Value(string text, string? dcmName, params string[] cctkTokens) => new(text, dcmName, Array.AsReadOnly(cctkTokens));

    private static IReadOnlyList<DellValueText> Values(params DellValueText[] values) => Array.AsReadOnly(values);

    private static IReadOnlyList<string> Names(params string[] names) => Array.AsReadOnly(names);
}
