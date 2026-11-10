// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using UefiAccess.Core.Platform;

namespace UefiAccess.Core.Safety;

/// <summary>
/// The BitLocker state of the OS volume as the write pipeline sees it.
/// On, Encrypting and Unknown need a choice from the person; Suspended keeps the warning and the
/// other-Windows-path rule (V13) but offers no suspend; Off needs nothing.
/// </summary>
public enum BitLockerState
{
    /// <summary>ProtectionStatus 0 with ConversionStatus 0, or no key protector: nothing to warn about.</summary>
    Off,

    /// <summary>ProtectionStatus 0 with ConversionStatus 1 and protectors present: the protectors are disabled until a later restart re-seals them.</summary>
    Suspended,

    /// <summary>ProtectionStatus 1: the volume is protected and the TPM checks the boot path at every start.</summary>
    On,

    /// <summary>ProtectionStatus 0 with ConversionStatus 2 and protectors present: protection turns on by itself when encryption finishes.</summary>
    Encrypting,

    /// <summary>ProtectionStatus 2, a failed or timed-out query, a protector list that could not be read, or any other combination with protectors present: treated as on.</summary>
    Unknown,
}

/// <summary>
/// What <see cref="BitLockerPreflight.Assess(BitLockerResult)"/> found for the OS volume: the state,
/// the raw facts behind it, and the sentence of the dialog's <c>BitLocker state</c> box, which the
/// CLI's standard error carries too. The suspend offer and the warning follow from the state.
/// </summary>
/// <param name="State">The state.</param>
/// <param name="DriveLetter">The OS volume's drive letter, for example "C:".</param>
/// <param name="ProtectionStatus">Win32_EncryptableVolume.ProtectionStatus (0 off, 1 on, 2 unknown), or null when no volume was found.</param>
/// <param name="ConversionStatus">Win32_EncryptableVolume.ConversionStatus (0 to 5, -1 unread), or null when no volume was found.</param>
/// <param name="ProtectorTypes">The key protector type names; empty when there is none or the list could not be read.</param>
/// <param name="PcrProfile">The PCR indices of the TPM protector's validation profile, ascending; empty when none.</param>
/// <param name="PcrProfileText">The inspector's rendering, for example "PCR 0,2,4,11 (legacy)", or null.</param>
/// <param name="StateSentence">The one-line sentence of the <c>BitLocker state</c> box, under 200 characters.</param>
/// <param name="Reason">Why the state is <see cref="BitLockerState.Unknown"/>, as the sentence says it; null otherwise.</param>
public sealed record BitLockerAssessment(
    BitLockerState State,
    string DriveLetter,
    int? ProtectionStatus,
    int? ConversionStatus,
    IReadOnlyList<string> ProtectorTypes,
    IReadOnlyList<int> PcrProfile,
    string? PcrProfileText,
    string StateSentence,
    string? Reason)
{
    /// <summary>True for On, Encrypting and Unknown: the person must choose between the one-restart suspend and going on with protection.</summary>
    public bool NeedsChoice => State is BitLockerState.On or BitLockerState.Encrypting or BitLockerState.Unknown;

    /// <summary>True when the dialog shows the suspend checkbox (ticked by default) and the CLI needs the --bitlocker option: the same states as <see cref="NeedsChoice"/>.</summary>
    public bool SuspendOffered => NeedsChoice;

    /// <summary>
    /// True whenever protectors exist or may exist and the volume is not decrypted: the warning and
    /// the recovery-key reminder are shown, and rules V13, V16 and V17 apply (every state but Off;
    /// <see cref="BitLockerPreflight.ProtectorsMayExist"/>).
    /// </summary>
    public bool WarningShown => BitLockerPreflight.ProtectorsMayExist(State);

    /// <summary>The recovery-key reminder of task 3.7, present while protectors exist; null when the state is Off.</summary>
    public string? RecoveryKeySentence => WarningShown ? BitLockerPreflight.RecoveryKeySentence : null;
}

/// <summary>
/// Assesses the BitLocker state of the OS volume from the M1 inspector's result (task 3.7) and
/// owns the sentences of the dialog's three boxes: the state box, the risk box (formats; the
/// entry name is filled in by the plan) and the recovery-key reminder. Every sentence stays under
/// 200 characters so a screen reader speaks the whole box on arrival; for that reason an Unknown
/// state names a short reason, never the text of a WMI error, which stays in the inspector's
/// result and the log. No sentence names where the recovery password is kept: the tool cannot
/// know it, and a constant would be spoken on any machine. Pure logic; no WMI.
/// </summary>
public static class BitLockerPreflight
{
    /// <summary>The drive letter used when the system directory's drive cannot be determined.</summary>
    public const string DefaultOsDriveLetter = "C:";

    /// <summary>On, with a TPM protector; {0} the drive, {1} the boot path text, for example "PCR 0, 2, 4, 11; TPM 1.2".</summary>
    public const string OnSentenceFormat = "BitLocker protects drive {0} with the TPM and checks the boot path ({1}).";

    /// <summary>On, without a TPM protector; {0} the drive, {1} the protector types.</summary>
    public const string OnWithoutTpmSentenceFormat = "BitLocker protects drive {0} with {1} and no TPM protector; the boot path is not measured for it.";

    /// <summary>On, with a protector list that could not be read; {0} the drive.</summary>
    public const string OnProtectorsNotReadSentenceFormat = "BitLocker protects drive {0}; its key protectors could not be read; treated as checking the boot path.";

    /// <summary>Encrypting, with a TPM protector; {0} the drive, {1} the boot path text.</summary>
    public const string EncryptingSentenceFormat = "BitLocker is still encrypting drive {0}; protection turns on by itself when encryption finishes and then checks the boot path ({1}).";

    /// <summary>Encrypting, without a TPM protector; {0} the drive, {1} the protector types.</summary>
    public const string EncryptingWithoutTpmSentenceFormat = "BitLocker is still encrypting drive {0} with {1} and no TPM protector; protection turns on by itself when encryption finishes.";

    /// <summary>Unknown; {0} the reason.</summary>
    public const string UnknownSentenceFormat = "BitLocker's state could not be read ({0}); treated as on.";

    /// <summary>Suspended; {0} the drive.</summary>
    public const string SuspendedSentenceFormat = "BitLocker is suspended on {0}; protection resumes at a later restart and is then sealed to that start's boot path.";

    /// <summary>Off; {0} the drive.</summary>
    public const string OffSentenceFormat = "BitLocker protection is off on {0}.";

    /// <summary>The recovery-key reminder of task 3.7, shown whenever protectors exist.</summary>
    public const string RecoveryKeySentence = "If the next start asks for the recovery key, you need the 48-digit recovery password saved when BitLocker was turned on. Have it at hand before you confirm. This application never reads or shows it.";

    /// <summary>The risk box of a reorder that moves another entry ahead of BootCurrent's.</summary>
    public const string ReorderRiskSentence = "Microsoft lists a change of the firmware boot order on a TPM 1.2 machine as a cause of BitLocker recovery.";

    /// <summary>The risk box of a reorder that keeps BootCurrent's entry first; {0} the entry name alone.</summary>
    public const string ReorderRiskFirstEntryStaysFormat = ReorderRiskSentence + " The first entry stays {0}.";

    /// <summary>The risk box of a one-time boot; {0} the entry name alone.</summary>
    public const string BootNextRiskFormat = "The firmware tries {0} once at the next start, then removes BootNext. If it cannot start, the normal list is used in that start and BitLocker may ask for the recovery key.";

    /// <summary>The boot path text when a TPM protector exists but its profile could not be read.</summary>
    public const string PcrProfileNotReadText = "PCR profile not read";

    /// <summary>The protector text of a volume that reports protection on with no protector listed.</summary>
    public const string NoProtectorListedText = "no protector listed";

    /// <summary>The Unknown reason when the inspector found no volume and reported errors (the query failed or did not answer in time).</summary>
    public const string QueryFailedReason = "the query failed or did not answer in time";

    /// <summary>The Unknown reason when the query found no volume for the OS drive and reported no error; {0} the drive.</summary>
    public const string NoVolumeReasonFormat = "no BitLocker information for drive {0}";

    /// <summary>The Unknown reason when protection is off and the key protector list could not be read, so "no protector" cannot be told from "not read".</summary>
    public const string ProtectorsNotReadReason = "the key protectors could not be read";

    /// <summary>The Unknown reason for a ProtectionStatus other than 0 or 1; {0} the value.</summary>
    public const string ProtectionStatusReasonFormat = "protection status {0}";

    /// <summary>The Unknown reason for ProtectionStatus 0 with protectors and a conversion that is neither encrypted, encrypting nor decrypted; {0} the conversion text.</summary>
    public const string ConversionStatusReasonFormat = "conversion status {0}";

    /// <summary>The start of the PCR list in the boot path text, before the indices ("PCR 0, 2, 4, 11").</summary>
    public const string PcrListPrefix = "PCR ";

    /// <summary>The separator between the PCR indices in the boot path text.</summary>
    public const string PcrSeparator = ", ";

    /// <summary>The separator between the PCR list and the TPM version in the boot path text.</summary>
    private const string TpmVersionSeparator = "; TPM ";

    /// <summary>The protector types that seal the key to the TPM (KeyProtectorType 1, 4, 5 and 6), as the inspector names them.</summary>
    private static readonly string[] TpmProtectorNames = new[] { 1, 4, 5, 6 }.Select(BitLockerInspector.ProtectorTypeName).ToArray();

    /// <summary>The OS volume's drive letter, from the system directory ("C:" when it cannot be determined).</summary>
    public static string OsDriveLetter { get; } = DetectOsDriveLetter();

    /// <summary>
    /// True for every state in which BitLocker protectors exist or may exist and the volume is not
    /// decrypted (On, Encrypting, Suspended, Unknown): the warning and the reminder are shown, and
    /// rules V13, V16 and V17 apply. The one definition the assessment and the validator share.
    /// </summary>
    /// <param name="state">The assessed state.</param>
    /// <returns>False for Off only.</returns>
    public static bool ProtectorsMayExist(BitLockerState state) => state != BitLockerState.Off;

    /// <summary>Assesses the OS volume (<see cref="OsDriveLetter"/>) without TPM version information.</summary>
    /// <param name="result">The M1 inspector's result.</param>
    /// <returns>The assessment.</returns>
    public static BitLockerAssessment Assess(BitLockerResult result)
    {
        return Assess(result, OsDriveLetter, null);
    }

    /// <summary>
    /// Assesses one volume by drive letter (the OS volume in the pipeline) by the table An empty protector list counts as "no protector" only when the
    /// inspector read the list; when it could not, the state is On for ProtectionStatus 1, Off
    /// for a fully decrypted volume, and Unknown otherwise.
    /// </summary>
    /// <param name="result">The M1 inspector's result.</param>
    /// <param name="driveLetter">The drive letter, for example "C:" (compared ignoring case).</param>
    /// <param name="tpm">The TPM facts, whose specification version completes the boot path text; null leaves it out.</param>
    /// <returns>The assessment.</returns>
    public static BitLockerAssessment Assess(BitLockerResult result, string driveLetter, TpmInfoResult? tpm)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(driveLetter);

        BitLockerVolume? volume = result.Volumes.FirstOrDefault(candidate => string.Equals(candidate.DriveLetter, driveLetter, StringComparison.OrdinalIgnoreCase));
        if (volume is null)
        {
            string reason = result.Errors.Count > 0
                ? QueryFailedReason
                : string.Format(CultureInfo.InvariantCulture, NoVolumeReasonFormat, driveLetter);
            return Unknown(driveLetter, null, null, Array.Empty<string>(), Array.Empty<int>(), null, reason);
        }

        IReadOnlyList<string> protectorTypes = volume.ProtectorTypes;
        IReadOnlyList<int> pcrProfile = BitLockerInspector.NormalizePcrProfile(volume.PcrProfile);
        bool protectors = protectorTypes.Count > 0;
        bool protectorsUnread = !protectors && BitLockerInspector.ProtectorListUnread(result, volume.DriveLetter);
        string types = protectors ? string.Join(", ", protectorTypes) : NoProtectorListedText;
        bool tpmProtector = HasTpmProtector(protectorTypes);
        string bootPath = BootPathText(protectorTypes, pcrProfile, tpm);

        switch (volume.ProtectionStatus)
        {
            case 1:
                {
                    string sentence = protectorsUnread
                        ? string.Format(CultureInfo.InvariantCulture, OnProtectorsNotReadSentenceFormat, driveLetter)
                        : tpmProtector
                            ? string.Format(CultureInfo.InvariantCulture, OnSentenceFormat, driveLetter, bootPath)
                            : string.Format(CultureInfo.InvariantCulture, OnWithoutTpmSentenceFormat, driveLetter, types);
                    return Assessment(BitLockerState.On, driveLetter, volume, pcrProfile, sentence, null);
                }

            case 0 when volume.ConversionStatus == 0:
                return Assessment(BitLockerState.Off, driveLetter, volume, pcrProfile, string.Format(CultureInfo.InvariantCulture, OffSentenceFormat, driveLetter), null);
            case 0 when protectorsUnread:
                return Unknown(driveLetter, volume.ProtectionStatus, volume.ConversionStatus, protectorTypes, pcrProfile, volume.PcrProfileText, ProtectorsNotReadReason);
            case 0 when !protectors:
                return Assessment(BitLockerState.Off, driveLetter, volume, pcrProfile, string.Format(CultureInfo.InvariantCulture, OffSentenceFormat, driveLetter), null);
            case 0 when volume.ConversionStatus == 2:
                {
                    string sentence = tpmProtector
                        ? string.Format(CultureInfo.InvariantCulture, EncryptingSentenceFormat, driveLetter, bootPath)
                        : string.Format(CultureInfo.InvariantCulture, EncryptingWithoutTpmSentenceFormat, driveLetter, types);
                    return Assessment(BitLockerState.Encrypting, driveLetter, volume, pcrProfile, sentence, null);
                }

            case 0 when volume.ConversionStatus == 1:
                return Assessment(BitLockerState.Suspended, driveLetter, volume, pcrProfile, string.Format(CultureInfo.InvariantCulture, SuspendedSentenceFormat, driveLetter), null);
            case 0:
                return Unknown(
                    driveLetter,
                    volume.ProtectionStatus,
                    volume.ConversionStatus,
                    protectorTypes,
                    pcrProfile,
                    volume.PcrProfileText,
                    string.Format(CultureInfo.InvariantCulture, ConversionStatusReasonFormat, volume.ConversionStatusText));
            default:
                return Unknown(
                    driveLetter,
                    volume.ProtectionStatus,
                    volume.ConversionStatus,
                    protectorTypes,
                    pcrProfile,
                    volume.PcrProfileText,
                    string.Format(CultureInfo.InvariantCulture, ProtectionStatusReasonFormat, volume.ProtectionStatus));
        }
    }

    /// <summary>
    /// The boot path text of the state box: the PCR indices of the validation profile as
    /// "PCR 0, 2, 4, 11", followed by "; TPM 1.2" when the TPM's specification version is known.
    /// With a TPM protector whose profile was not read, <see cref="PcrProfileNotReadText"/>;
    /// without a TPM protector and without a profile, an empty string.
    /// </summary>
    /// <param name="protectorTypes">The protector type names.</param>
    /// <param name="pcrProfile">The PCR indices, in any order.</param>
    /// <param name="tpm">The TPM facts, or null.</param>
    /// <returns>The text.</returns>
    public static string BootPathText(IReadOnlyList<string> protectorTypes, IReadOnlyList<int> pcrProfile, TpmInfoResult? tpm)
    {
        ArgumentNullException.ThrowIfNull(protectorTypes);
        ArgumentNullException.ThrowIfNull(pcrProfile);
        if (!HasTpmProtector(protectorTypes) && pcrProfile.Count == 0)
        {
            return string.Empty;
        }

        string pcrs = pcrProfile.Count == 0
            ? PcrProfileNotReadText
            : PcrListPrefix + string.Join(PcrSeparator, BitLockerInspector.NormalizePcrProfile(pcrProfile).Select(pcr => pcr.ToString(CultureInfo.InvariantCulture)));
        string? version = TpmVersionText(tpm);
        return version is null ? pcrs : pcrs + TpmVersionSeparator + version;
    }

    /// <summary>
    /// True when one of the protector types seals the key to the TPM: "TPM", "TPM+PIN",
    /// "TPM+startup key" or "TPM+PIN+startup key", as the inspector names KeyProtectorType 1, 4, 5
    /// and 6. A TPM certificate protector (type 9) is not one of them.
    /// </summary>
    /// <param name="protectorTypes">The protector type names as the inspector renders them.</param>
    /// <returns>Whether a TPM protector exists.</returns>
    public static bool HasTpmProtector(IReadOnlyList<string> protectorTypes)
    {
        ArgumentNullException.ThrowIfNull(protectorTypes);
        return protectorTypes.Any(type => TpmProtectorNames.Contains(type, StringComparer.Ordinal));
    }

    /// <summary>The TPM's own version from Win32_Tpm.SpecVersion ("1.2, 2, 3" gives "1.2"), or null when unknown.</summary>
    /// <param name="tpm">The TPM facts, or null.</param>
    /// <returns>The version text or null.</returns>
    public static string? TpmVersionText(TpmInfoResult? tpm)
    {
        if (tpm?.SpecVersion is null)
        {
            return null;
        }

        string first = tpm.SpecVersion.Split(',', 2, StringSplitOptions.TrimEntries)[0];
        return first.Length == 0 ? null : first;
    }

    private static BitLockerAssessment Assessment(BitLockerState state, string driveLetter, BitLockerVolume volume, IReadOnlyList<int> pcrProfile, string sentence, string? reason)
    {
        return new BitLockerAssessment(
            state,
            driveLetter,
            volume.ProtectionStatus,
            volume.ConversionStatus,
            volume.ProtectorTypes,
            pcrProfile,
            volume.PcrProfileText,
            sentence,
            reason);
    }

    private static BitLockerAssessment Unknown(string driveLetter, int? protectionStatus, int? conversionStatus, IReadOnlyList<string> protectorTypes, IReadOnlyList<int> pcrProfile, string? pcrProfileText, string reason)
    {
        return new BitLockerAssessment(
            BitLockerState.Unknown,
            driveLetter,
            protectionStatus,
            conversionStatus,
            protectorTypes,
            pcrProfile,
            pcrProfileText,
            string.Format(CultureInfo.InvariantCulture, UnknownSentenceFormat, reason),
            reason);
    }

    private static string DetectOsDriveLetter()
    {
        try
        {
            string? root = Path.GetPathRoot(Environment.SystemDirectory);
            if (root is { Length: >= 2 } && root[1] == ':' && char.IsAsciiLetter(root[0]))
            {
                return root[..2].ToUpperInvariant();
            }
        }
        catch (Exception)
        {
            // The system directory is not a drive path on this host; the default applies.
        }

        return DefaultOsDriveLetter;
    }
}
