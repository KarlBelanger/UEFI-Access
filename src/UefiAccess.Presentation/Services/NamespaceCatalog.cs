// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Variables;
using UefiAccess.Presentation.Resources;

namespace UefiAccess.Presentation.Services;

/// <summary>
/// Maps vendor GUIDs to human labels: the EFI global variable GUID, the image security database
/// GUID and a small table of vendor GUIDs whose owner is certain, with "Unknown vendor" plus the
/// lowercase GUID as the fallback. The labels are what the Namespace column, the group headers and
/// the accessible row sentences say, so they are short noun phrases without the GUID. Every entry
/// is read-only data; nothing here touches the firmware.
/// </summary>
public static class NamespaceCatalog
{
    /// <summary>Label of the EFI global variable namespace (BootOrder, Boot####, SecureBoot, PK, KEK, ...).</summary>
    public const string EfiGlobalVariableLabel = "EFI global variable";

    /// <summary>Label of the image security database namespace (db, dbx, dbt, dbr).</summary>
    public const string ImageSecurityDatabaseLabel = "Image security database";

    /// <summary>
    /// Label of Microsoft's vendor namespace (MICROSOFT_VENDOR_GUID): on Windows it holds the kernel's
    /// and Code Integrity's variables (CurrentPolicy, Kernel_*SiStatus, WindowsBootChainSvn, ...), not
    /// the Windows Boot Manager, whose load option is a Boot#### variable in the EFI global variable namespace.
    /// </summary>
    public const string MicrosoftVendorLabel = "Microsoft";

    /// <summary>Label of the shim loader namespace (MokList, MokListRT, MokSBState, ...).</summary>
    public const string ShimMachineOwnerKeysLabel = "Shim / Machine Owner Keys";

    /// <summary>Label of the capsule update result namespace (Capsule####, UEFI specification EFI_CAPSULE_REPORT_GUID).</summary>
    public const string CapsuleReportLabel = "Capsule update results";

    /// <summary>Label of the hardware error record namespace (HwErrRec####, UEFI specification EFI_HARDWARE_ERROR_VARIABLE).</summary>
    public const string HardwareErrorRecordLabel = "Hardware error records";

    /// <summary>Label of the TCG MemoryOverwriteRequestControl namespace (a request to clear memory at the next reboot).</summary>
    public const string MemoryOverwriteRequestLabel = "Memory clearing request (TCG MOR)";

    /// <summary>
    /// Label of the Dell network stack namespace (<see cref="KnownGuids.NetworkStackSetup"/>, which
    /// holds NetworkStackVar), after the title of its setup form, "Network
    /// Stack Configuration". Not "setup": the labels are spoken in every row sentence and group
    /// header, and none holds even the letters of a word that implies a change.
    /// </summary>
    public const string NetworkStackSetupLabel = "Network stack configuration";

    /// <summary>
    /// MICROSOFT_VENDOR_GUID (EDK II OvmfPkg/Include/Guid/MicrosoftVendor.h): the vendor GUID of
    /// Microsoft-owned variables and the SignatureOwner of Microsoft's Secure Boot certificates.
    /// </summary>
    public static Guid MicrosoftVendor { get; } = new("77fa9abd-0359-4d32-bd60-28f4e78f784b");

    /// <summary>The shim loader vendor GUID (SHIM_LOCK_GUID) that scopes the Machine Owner Key variables.</summary>
    public static Guid ShimMachineOwnerKeys { get; } = new("605dab50-e046-4300-abb6-3dd810dd8b23");

    /// <summary>EFI_CAPSULE_REPORT_GUID, the vendor GUID of the Capsule#### result variables.</summary>
    public static Guid CapsuleReport { get; } = new("39b68c46-f7fb-441b-b6ec-16b0f69821f3");

    /// <summary>EFI_HARDWARE_ERROR_VARIABLE, the vendor GUID of the HwErrRec#### variables.</summary>
    public static Guid HardwareErrorRecord { get; } = new("414e6bdd-e47b-47cc-b244-bb61020cf516");

    /// <summary>MEMORY_ONLY_RESET_CONTROL_GUID, the vendor GUID of MemoryOverwriteRequestControl (TCG Platform Reset Attack Mitigation).</summary>
    public static Guid MemoryOverwriteRequest { get; } = new("e20939be-32d4-41be-a150-897f85d49829");

    private static readonly IReadOnlyList<NamespaceInfo> KnownList = new[]
    {
        new NamespaceInfo(KnownGuids.EfiGlobalVariable, EfiGlobalVariableLabel, true),
        new NamespaceInfo(KnownGuids.EfiImageSecurityDatabase, ImageSecurityDatabaseLabel, true),
        new NamespaceInfo(MicrosoftVendor, MicrosoftVendorLabel, true),
        new NamespaceInfo(ShimMachineOwnerKeys, ShimMachineOwnerKeysLabel, true),
        new NamespaceInfo(CapsuleReport, CapsuleReportLabel, true),
        new NamespaceInfo(HardwareErrorRecord, HardwareErrorRecordLabel, true),
        new NamespaceInfo(MemoryOverwriteRequest, MemoryOverwriteRequestLabel, true),
        new NamespaceInfo(KnownGuids.NetworkStackSetup, NetworkStackSetupLabel, true),
    };

    private static readonly Dictionary<Guid, NamespaceInfo> KnownByGuid = KnownList.ToDictionary(info => info.Guid);

    /// <summary>Every namespace the catalog knows, in catalog order (the EFI global variable namespace first).</summary>
    public static IReadOnlyList<NamespaceInfo> Known => KnownList;

    /// <summary>Describes a vendor GUID. Never throws.</summary>
    /// <param name="guid">The vendor GUID.</param>
    /// <returns>The label and whether the GUID is known; an unknown GUID gets "Unknown vendor" followed by the lowercase GUID.</returns>
    public static NamespaceInfo Describe(Guid guid)
    {
        if (KnownByGuid.TryGetValue(guid, out NamespaceInfo? known))
        {
            return known;
        }

        return new NamespaceInfo(guid, UnknownLabelFor(guid), false);
    }

    /// <summary>The label for a vendor GUID; shorthand for <see cref="Describe"/>.</summary>
    /// <param name="guid">The vendor GUID.</param>
    /// <returns>The label.</returns>
    public static string LabelFor(Guid guid) => Describe(guid).Label;

    /// <summary>True when the catalog knows the GUID.</summary>
    /// <param name="guid">The vendor GUID.</param>
    /// <returns>True for a catalogued GUID.</returns>
    public static bool IsKnown(Guid guid) => KnownByGuid.ContainsKey(guid);

    /// <summary>The fallback label: "Unknown vendor" followed by a space and the lowercase "D" GUID.</summary>
    /// <param name="guid">The vendor GUID.</param>
    /// <returns>The fallback label.</returns>
    public static string UnknownLabelFor(Guid guid) => Strings.UnknownVendorLabel + " " + guid.ToString("D");
}
