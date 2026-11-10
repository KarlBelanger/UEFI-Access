// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Core.Platform;

/// <summary>
/// Machine identity from Win32_ComputerSystem, Win32_BIOS, Win32_BaseBoard, Win32_Processor and
/// Win32_OperatingSystem, plus the expected-model check for the 2014 Dell OptiPlex 7020.
/// </summary>
/// <param name="Manufacturer">Win32_ComputerSystem.Manufacturer.</param>
/// <param name="Model">Win32_ComputerSystem.Model.</param>
/// <param name="SystemSku">Win32_ComputerSystem.SystemSKUNumber.</param>
/// <param name="BiosVersion">Win32_BIOS.SMBIOSBIOSVersion (for example "A18").</param>
/// <param name="BiosReleaseDate">Win32_BIOS.ReleaseDate, formatted as text.</param>
/// <param name="ServiceTag">Win32_BIOS.SerialNumber (the Dell service tag).</param>
/// <param name="BaseBoard">Win32_BaseBoard.Product.</param>
/// <param name="Cpu">Win32_Processor.Name.</param>
/// <param name="OsCaption">Win32_OperatingSystem.Caption.</param>
/// <param name="OsVersion">Win32_OperatingSystem.Version.</param>
/// <param name="OsBuild">Win32_OperatingSystem.BuildNumber.</param>
/// <param name="ExpectedModelConfirmed">True when model, BIOS version and CPU generation match the planned 2014 OptiPlex 7020.</param>
/// <param name="ExpectedModelNotes">Description of what was found, including a warning when the unit differs from the plan.</param>
/// <param name="Errors">Errors encountered while querying; empty on success.</param>
public sealed record MachineIdentityResult(
    string? Manufacturer,
    string? Model,
    string? SystemSku,
    string? BiosVersion,
    string? BiosReleaseDate,
    string? ServiceTag,
    string? BaseBoard,
    string? Cpu,
    string? OsCaption,
    string? OsVersion,
    string? OsBuild,
    bool ExpectedModelConfirmed,
    string ExpectedModelNotes,
    IReadOnlyList<string> Errors);

/// <summary>TPM facts from root\CIMV2\Security\MicrosoftTpm Win32_Tpm.</summary>
/// <param name="Present">True when a Win32_Tpm instance exists.</param>
/// <param name="SpecVersion">Win32_Tpm.SpecVersion (for example "1.2, 2, 3").</param>
/// <param name="Enabled">Win32_Tpm.IsEnabled_InitialValue, or null when unknown.</param>
/// <param name="Activated">Win32_Tpm.IsActivated_InitialValue, or null when unknown.</param>
/// <param name="Errors">Errors encountered while querying; empty on success.</param>
public sealed record TpmInfoResult(
    bool Present,
    string? SpecVersion,
    bool? Enabled,
    bool? Activated,
    IReadOnlyList<string> Errors);

/// <summary>
/// Secure Boot policy from the registry, the UEFI variables, and the optional PowerShell cross-check.
/// </summary>
/// <param name="RegistryEnabled">HKLM\SYSTEM\CurrentControlSet\Control\SecureBoot\State\UEFISecureBootEnabled, or null when unreadable.</param>
/// <param name="VariableSecureBoot">Value of the SecureBoot variable, or null when unreadable.</param>
/// <param name="VariableSetupMode">Value of the SetupMode variable, or null when unreadable.</param>
/// <param name="VariableDeployedMode">Value of the DeployedMode variable, or null when unreadable or absent.</param>
/// <param name="VariableAuditMode">Value of the AuditMode variable, or null when unreadable or absent.</param>
/// <param name="PowerShellConfirm">Result of Confirm-SecureBootUEFI, or null when not run or failed.</param>
/// <param name="Capable">True when the firmware exposes Secure Boot: PK is present or the SetupMode variable could be read.</param>
/// <param name="SourcesAgree">True when at least one of RegistryEnabled, VariableSecureBoot and PowerShellConfirm is available and every available one reports the same enabled state.</param>
/// <param name="Errors">Errors encountered while querying; empty on success.</param>
public sealed record SecureBootPolicyResult(
    bool? RegistryEnabled,
    bool? VariableSecureBoot,
    bool? VariableSetupMode,
    bool? VariableDeployedMode,
    bool? VariableAuditMode,
    bool? PowerShellConfirm,
    bool Capable,
    bool SourcesAgree,
    IReadOnlyList<string> Errors);

/// <summary>One BitLocker-capable volume from Win32_EncryptableVolume (read-only queries only).</summary>
/// <param name="DriveLetter">Drive letter such as "C:".</param>
/// <param name="ProtectionStatus">0 off, 1 on, 2 unknown.</param>
/// <param name="ProtectionStatusText">Human-readable protection status: "On", "Off", "Unknown", or "Pending until encryption completes" / "Pending, encryption paused" while the volume is still being encrypted (ProtectionStatus is 0 then, but the protection is not off).</param>
/// <param name="ConversionStatus">Win32_EncryptableVolume.ConversionStatus (0 to 5), or -1 when it could not be read.</param>
/// <param name="ConversionStatusText">ConversionStatus in words: "Decrypted", "Encrypted", "Encrypting", "Decrypting", "Encryption paused", "Decryption paused" or "Unknown".</param>
/// <param name="ProtectorTypes">Key protector type names: GetKeyProtectors lists the protector IDs, GetKeyProtectorType names each one.</param>
/// <param name="PcrProfile">PCR indices from GetKeyProtectorPlatformValidationProfile for TPM protectors; empty when none.</param>
/// <param name="PcrProfileText">Explicit rendering such as "PCR 7+11" or "PCR 0,2,4,11", or null when no TPM protector exists.</param>
public sealed record BitLockerVolume(
    string DriveLetter,
    int ProtectionStatus,
    string ProtectionStatusText,
    int ConversionStatus,
    string ConversionStatusText,
    IReadOnlyList<string> ProtectorTypes,
    IReadOnlyList<int> PcrProfile,
    string? PcrProfileText);

/// <summary>BitLocker state of every encryptable volume.</summary>
/// <param name="Volumes">The volumes found; empty when none or when the query failed.</param>
/// <param name="Errors">Errors encountered while querying; empty on success.</param>
public sealed record BitLockerResult(
    IReadOnlyList<BitLockerVolume> Volumes,
    IReadOnlyList<string> Errors);

/// <summary>
/// A sample Dell BIOS attribute read through one of the Dell interfaces. Dell Command | Monitor
/// returns the values as codes (for example "1" and "2"); the names ("Disabled", "Enabled") are in
/// the parallel PossibleValuesDescription array, index for index.
/// </summary>
/// <param name="Name">Attribute name.</param>
/// <param name="CurrentValue">Current value as the interface returns it (a code for DCM), or null when unavailable.</param>
/// <param name="CurrentValueDescription">The name of the current value, looked up at the same index in PossibleValuesDescription, or null when it cannot be mapped.</param>
/// <param name="PossibleValues">Possible values (codes for DCM), in the interface's order; empty when unknown.</param>
/// <param name="PossibleValuesDescription">The name of each possible value, index for index with PossibleValues; empty when the interface gave none.</param>
/// <param name="Source">Which interface produced the sample: "DCM" for the Dell Command | Monitor DCIM_BIOSEnumeration class.</param>
public sealed record DellAttributeSample(
    string Name,
    string? CurrentValue,
    string? CurrentValueDescription,
    IReadOnlyList<string> PossibleValues,
    IReadOnlyList<string> PossibleValuesDescription,
    string Source);

/// <summary>Result of probing the Dell BIOS settings interfaces (gate question b).</summary>
/// <param name="DcmClassesPresent">
/// True when at least one of the four Dell Command | Monitor classes is defined in root\dcim\sysman.
/// A definition only proves the classes are installed; whether the provider answers is
/// <see cref="DellInterfaceProbeResult.DcmClassesResponding"/>.
/// </param>
/// <param name="DcmClassesFound">Names of the four DCIM classes that are defined in root\dcim\sysman (a class listing, not an instance query).</param>
/// <param name="Samples">Up to three sample attributes read to prove the interface works.</param>
/// <param name="CctkFound">True when cctk.exe exists under one of the Dell Command | Configure paths.</param>
/// <param name="CctkPath">Full path of cctk.exe when found.</param>
/// <param name="CctkVersion">Output of "cctk --version" when run.</param>
/// <param name="CctkSecureBootOutput">Output of "cctk --secureboot" when run.</param>
/// <param name="NativeBiosAttributesNamespacePresent">True when root\dcim\sysman\biosattributes exists (not expected on the 2014 unit).</param>
/// <param name="NativeWmiSecurityNamespacePresent">True when root\dcim\sysman\wmisecurity exists (not expected on the 2014 unit).</param>
/// <param name="Errors">Errors encountered while probing; empty on success.</param>
public sealed record DellInterfaceProbeResult(
    bool DcmClassesPresent,
    IReadOnlyList<string> DcmClassesFound,
    IReadOnlyList<DellAttributeSample> Samples,
    bool CctkFound,
    string? CctkPath,
    string? CctkVersion,
    string? CctkSecureBootOutput,
    bool NativeBiosAttributesNamespacePresent,
    bool NativeWmiSecurityNamespacePresent,
    IReadOnlyList<string> Errors)
{
    /// <summary>Number of DCIM_* classes enumerated in root\dcim\sysman; 0 when the namespace is absent.</summary>
    public int DcimClassCount { get; init; }

    /// <summary>
    /// The Secure Boot state parsed from <see cref="CctkSecureBootOutput"/> ("secureboot=enable" true,
    /// "secureboot=disable" false), or null when cctk was not run or its output was not recognised.
    /// </summary>
    public bool? CctkSecureBootEnabled { get; init; }

    /// <summary>
    /// Names of the defined Dell Command | Monitor classes whose instance query returned at least one
    /// instance within the time limit, in the order of <see cref="DcmClassesFound"/>. Outcome A
    /// needs DCIM_BIOSEnumeration here and readable <see cref="Samples"/>; a class that is defined
    /// but missing here did not answer, and the reason is in <see cref="Errors"/>.
    /// </summary>
    public IReadOnlyList<string> DcmClassesResponding { get; init; } = Array.Empty<string>();
}

/// <summary>All platform inspection results gathered for platform.json and the gate.</summary>
/// <param name="Identity">Machine identity.</param>
/// <param name="Tpm">TPM facts.</param>
/// <param name="SecureBootPolicy">Secure Boot policy.</param>
/// <param name="BitLocker">BitLocker volumes.</param>
/// <param name="Dell">Dell interface probe.</param>
public sealed record PlatformSnapshot(
    MachineIdentityResult Identity,
    TpmInfoResult Tpm,
    SecureBootPolicyResult SecureBootPolicy,
    BitLockerResult BitLocker,
    DellInterfaceProbeResult Dell);
