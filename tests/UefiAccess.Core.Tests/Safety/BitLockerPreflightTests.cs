// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Platform;
using UefiAccess.Core.Safety;
using Xunit;

namespace UefiAccess.Core.Tests.Safety;

/// <summary>
/// The BitLocker assessment of stage S8: the five states from the
/// inspector's facts, ConversionStatus 3, 4 and 5 as Unknown, a failed or empty query as Unknown,
/// a protector list the inspector could not read never taken for "no protector", Unknown counting
/// as on (a choice is needed, the suspend is offered), Suspended keeping the warning without the
/// offer, Off needing nothing, the PCR and boot path texts, and every sentence under 200
/// characters with no secret in it. Pure logic; the inspector's error lines come from its own
/// reader over a fake volume, so no WMI.
/// </summary>
public sealed class BitLockerPreflightTests
{
    private const string Drive = "C:";
    private static readonly string[] TpmAndPassword = { "TPM", "Numerical password" };
    private static readonly string[] PasswordOnly = { "Numerical password" };
    private static readonly int[] LegacyProfile = { 0, 2, 4, 11 };

    [Fact]
    public void On_NeedsAChoice_OffersTheSuspend_ShowsTheWarningAndTheReminder()
    {
        BitLockerAssessment assessment = BitLockerPreflight.Assess(Result(Volume(1, 1, TpmAndPassword, LegacyProfile)), Drive, Tpm("1.2, 2, 3"));

        Assert.Equal(BitLockerState.On, assessment.State);
        Assert.True(assessment.NeedsChoice);
        Assert.True(assessment.SuspendOffered);
        Assert.True(assessment.WarningShown);
        Assert.True(BitLockerPreflight.ProtectorsMayExist(assessment.State));
        Assert.Equal(BitLockerPreflight.RecoveryKeySentence, assessment.RecoveryKeySentence);
        Assert.Equal("BitLocker protects drive C: with the TPM and checks the boot path (PCR 0, 2, 4, 11; TPM 1.2).", assessment.StateSentence);
        Assert.Null(assessment.Reason);
        Assert.Equal(1, assessment.ProtectionStatus);
        Assert.Equal(1, assessment.ConversionStatus);
        Assert.Equal(TpmAndPassword, assessment.ProtectorTypes);
        Assert.Equal(LegacyProfile, assessment.PcrProfile);
        Assert.Equal("PCR 0,2,4,11 (legacy)", assessment.PcrProfileText);
    }

    [Fact]
    public void On_WithoutTpmVersion_LeavesItOutOfTheBootPath()
    {
        BitLockerAssessment assessment = BitLockerPreflight.Assess(Result(Volume(1, 1, TpmAndPassword, LegacyProfile)), Drive, null);

        Assert.Equal("BitLocker protects drive C: with the TPM and checks the boot path (PCR 0, 2, 4, 11).", assessment.StateSentence);
    }

    [Fact]
    public void On_WithoutATpmProtector_SaysTheBootPathIsNotMeasured()
    {
        BitLockerAssessment assessment = BitLockerPreflight.Assess(Result(Volume(1, 1, PasswordOnly, Array.Empty<int>())), Drive, null);

        Assert.Equal(BitLockerState.On, assessment.State);
        Assert.True(assessment.NeedsChoice);
        Assert.Equal("BitLocker protects drive C: with Numerical password and no TPM protector; the boot path is not measured for it.", assessment.StateSentence);
    }

    [Fact]
    public void Encrypting_IsTreatedAsOn()
    {
        BitLockerAssessment assessment = BitLockerPreflight.Assess(Result(Volume(0, 2, TpmAndPassword, LegacyProfile)), Drive, Tpm("1.2, 2, 3"));

        Assert.Equal(BitLockerState.Encrypting, assessment.State);
        Assert.True(assessment.NeedsChoice);
        Assert.True(assessment.SuspendOffered);
        Assert.True(assessment.WarningShown);
        Assert.True(BitLockerPreflight.ProtectorsMayExist(assessment.State));
        Assert.NotNull(assessment.RecoveryKeySentence);
        Assert.Equal("BitLocker is still encrypting drive C:; protection turns on by itself when encryption finishes and then checks the boot path (PCR 0, 2, 4, 11; TPM 1.2).", assessment.StateSentence);
    }

    [Fact]
    public void Suspended_KeepsTheWarningAndTheRule_ButOffersNoSuspend()
    {
        BitLockerAssessment assessment = BitLockerPreflight.Assess(Result(Volume(0, 1, TpmAndPassword, LegacyProfile)), Drive, null);

        Assert.Equal(BitLockerState.Suspended, assessment.State);
        Assert.False(assessment.NeedsChoice);
        Assert.False(assessment.SuspendOffered);
        Assert.True(assessment.WarningShown);
        Assert.True(BitLockerPreflight.ProtectorsMayExist(assessment.State));
        Assert.Equal(BitLockerPreflight.RecoveryKeySentence, assessment.RecoveryKeySentence);
        Assert.Equal("BitLocker is suspended on C:; protection resumes at a later restart and is then sealed to that start's boot path.", assessment.StateSentence);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(0, 0, false)]
    [InlineData(0, 1, false)]
    [InlineData(0, 2, false)]
    [InlineData(0, 3, false)]
    [InlineData(0, -1, false)]
    public void Off_NeedsNothing(int protection, int conversion, bool withProtectors)
    {
        BitLockerVolume volume = Volume(protection, conversion, withProtectors ? TpmAndPassword : Array.Empty<string>(), withProtectors ? LegacyProfile : Array.Empty<int>());

        BitLockerAssessment assessment = BitLockerPreflight.Assess(Result(volume), Drive, null);

        Assert.Equal(BitLockerState.Off, assessment.State);
        Assert.False(assessment.NeedsChoice);
        Assert.False(assessment.SuspendOffered);
        Assert.False(assessment.WarningShown);
        Assert.False(BitLockerPreflight.ProtectorsMayExist(assessment.State));
        Assert.Null(assessment.RecoveryKeySentence);
        Assert.Equal("BitLocker protection is off on C:.", assessment.StateSentence);
        Assert.Null(assessment.Reason);
    }

    [Theory]
    [InlineData(3, "Decrypting")]
    [InlineData(4, "Encryption paused")]
    [InlineData(5, "Decryption paused")]
    [InlineData(-1, "Unknown")]
    [InlineData(9, "Unknown (9)")]
    public void ProtectionOffWithProtectors_AndAnyOtherConversion_IsUnknown(int conversion, string conversionText)
    {
        BitLockerAssessment assessment = BitLockerPreflight.Assess(Result(Volume(0, conversion, TpmAndPassword, LegacyProfile)), Drive, null);

        Assert.Equal(BitLockerState.Unknown, assessment.State);
        Assert.Equal("conversion status " + conversionText, assessment.Reason);
        Assert.Equal("BitLocker's state could not be read (conversion status " + conversionText + "); treated as on.", assessment.StateSentence);
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(2, false)]
    [InlineData(7, true)]
    public void ProtectionStatusOtherThanOffOrOn_IsUnknown(int protection, bool withProtectors)
    {
        BitLockerVolume volume = Volume(protection, 1, withProtectors ? TpmAndPassword : Array.Empty<string>(), withProtectors ? LegacyProfile : Array.Empty<int>());

        BitLockerAssessment assessment = BitLockerPreflight.Assess(Result(volume), Drive, null);

        Assert.Equal(BitLockerState.Unknown, assessment.State);
        Assert.Equal("protection status " + protection, assessment.Reason);
        Assert.Equal("BitLocker's state could not be read (protection status " + protection + "); treated as on.", assessment.StateSentence);
        Assert.True(assessment.NeedsChoice);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(-1)]
    public void ProtectionOff_WithAProtectorListThatCouldNotBeRead_IsUnknown_NotOff(int conversion)
    {
        // The inspector's own error lines, produced by its reader over a volume whose protector
        // list call fails: an empty list then means "not known", never "no protector".
        BitLockerResult result = new(new[] { Volume(0, conversion, Array.Empty<string>(), Array.Empty<int>()) }, ProtectorListErrors(Drive, new InvalidOperationException("Generic failure")));

        BitLockerAssessment assessment = BitLockerPreflight.Assess(result, Drive, null);

        Assert.Equal(BitLockerState.Unknown, assessment.State);
        Assert.True(assessment.NeedsChoice);
        Assert.True(assessment.SuspendOffered);
        Assert.True(assessment.WarningShown);
        Assert.Equal(BitLockerPreflight.ProtectorsNotReadReason, assessment.Reason);
        Assert.Equal("BitLocker's state could not be read (the key protectors could not be read); treated as on.", assessment.StateSentence);
    }

    [Fact]
    public void ProtectionOff_WithAProtectorListReturningAnError_IsUnknown()
    {
        BitLockerResult result = new(new[] { Volume(0, 1, Array.Empty<string>(), Array.Empty<int>()) }, ProtectorListErrors(Drive, returnValue: 0x80070005));

        Assert.Equal(BitLockerState.Unknown, BitLockerPreflight.Assess(result, Drive, null).State);
    }

    [Fact]
    public void ProtectionOff_WithTheListNotMadeAfterATimeout_IsUnknown()
    {
        // A timeout on another volume first: the inspector then makes no call for this one and
        // records it as not called.
        BitLockerResult result = new(new[] { Volume(0, 1, Array.Empty<string>(), Array.Empty<int>()) }, ProtectorListErrors(Drive, alreadyTimedOut: true));

        BitLockerAssessment assessment = BitLockerPreflight.Assess(result, Drive, null);

        Assert.Equal(BitLockerState.Unknown, assessment.State);
        Assert.Equal(BitLockerPreflight.ProtectorsNotReadReason, assessment.Reason);
    }

    [Fact]
    public void ProtectionOff_FullyDecrypted_IsOff_EvenWhenTheProtectorListCouldNotBeRead()
    {
        BitLockerResult result = new(new[] { Volume(0, 0, Array.Empty<string>(), Array.Empty<int>()) }, ProtectorListErrors(Drive, new InvalidOperationException("Generic failure")));

        Assert.Equal(BitLockerState.Off, BitLockerPreflight.Assess(result, Drive, null).State);
    }

    [Fact]
    public void ProtectionOff_BitLockerNotActivated_IsOff_BecauseTheInspectorRecordsNoError()
    {
        // FVE_E_NOT_ACTIVATED from the protector list is the inspector's "no protectors", not a failure.
        BitLockerResult result = new(new[] { Volume(0, 1, Array.Empty<string>(), Array.Empty<int>()) }, ProtectorListErrors(Drive, returnValue: 0x80310008));

        Assert.Empty(result.Errors);
        Assert.Equal(BitLockerState.Off, BitLockerPreflight.Assess(result, Drive, null).State);
    }

    [Fact]
    public void AnUnreadProtectorListOfAnotherDrive_DoesNotConcernThisOne()
    {
        BitLockerResult result = new(new[] { Volume(0, 1, Array.Empty<string>(), Array.Empty<int>()) }, ProtectorListErrors("D:", new InvalidOperationException("Generic failure")));

        Assert.Equal(BitLockerState.Off, BitLockerPreflight.Assess(result, Drive, null).State);
    }

    [Fact]
    public void ProtectionOn_WithAProtectorListThatCouldNotBeRead_IsOn_AndSaysSo()
    {
        BitLockerResult result = new(new[] { Volume(1, 1, Array.Empty<string>(), Array.Empty<int>()) }, ProtectorListErrors(Drive, new InvalidOperationException("Generic failure")));

        BitLockerAssessment assessment = BitLockerPreflight.Assess(result, Drive, Tpm("1.2, 2, 3"));

        Assert.Equal(BitLockerState.On, assessment.State);
        Assert.Equal("BitLocker protects drive C:; its key protectors could not be read; treated as checking the boot path.", assessment.StateSentence);
    }

    [Fact]
    public void Unknown_CountsAsOn()
    {
        BitLockerAssessment assessment = BitLockerPreflight.Assess(Result(Volume(2, -1, Array.Empty<string>(), Array.Empty<int>())), Drive, null);

        Assert.Equal(BitLockerState.Unknown, assessment.State);
        Assert.True(assessment.NeedsChoice);
        Assert.True(assessment.SuspendOffered);
        Assert.True(assessment.WarningShown);
        Assert.True(BitLockerPreflight.ProtectorsMayExist(assessment.State));
        Assert.Equal(BitLockerPreflight.RecoveryKeySentence, assessment.RecoveryKeySentence);
    }

    [Fact]
    public void AFailedQuery_IsUnknown_WithAShortReason_NeverTheErrorText()
    {
        // The error text stays in the inspector's result and the log; the box names a short reason,
        // so the sentence stays under 200 characters whatever WMI said.
        string longError = "Win32_EncryptableVolume: COMException 0x80041003: " + new string('x', 400);
        BitLockerResult result = new(Array.Empty<BitLockerVolume>(), new[] { longError, "second" });

        BitLockerAssessment assessment = BitLockerPreflight.Assess(result, Drive, null);

        Assert.Equal(BitLockerState.Unknown, assessment.State);
        Assert.True(assessment.NeedsChoice);
        Assert.Equal(BitLockerPreflight.QueryFailedReason, assessment.Reason);
        Assert.Equal("BitLocker's state could not be read (the query failed or did not answer in time); treated as on.", assessment.StateSentence);
        Assert.True(assessment.StateSentence.Length < 200);
        Assert.Null(assessment.ProtectionStatus);
        Assert.Null(assessment.ConversionStatus);
        Assert.Empty(assessment.ProtectorTypes);
        Assert.Empty(assessment.PcrProfile);
        Assert.Null(assessment.PcrProfileText);
    }

    [Fact]
    public void NoVolumeForTheDrive_IsUnknown()
    {
        BitLockerResult result = new(new[] { Volume(1, 1, TpmAndPassword, LegacyProfile) with { DriveLetter = "D:" } }, Array.Empty<string>());

        BitLockerAssessment assessment = BitLockerPreflight.Assess(result, Drive, null);

        Assert.Equal(BitLockerState.Unknown, assessment.State);
        Assert.Equal("no BitLocker information for drive C:", assessment.Reason);
        Assert.Equal("BitLocker's state could not be read (no BitLocker information for drive C:); treated as on.", assessment.StateSentence);
    }

    [Fact]
    public void TheDriveLetterIsMatchedIgnoringCase_AndTheOtherVolumesAreIgnored()
    {
        BitLockerResult result = new(
            new[] { Volume(0, 0, Array.Empty<string>(), Array.Empty<int>()) with { DriveLetter = "D:" }, Volume(1, 1, TpmAndPassword, LegacyProfile) },
            Array.Empty<string>());

        BitLockerAssessment assessment = BitLockerPreflight.Assess(result, "c:", null);

        Assert.Equal(BitLockerState.On, assessment.State);
        Assert.Equal("c:", assessment.DriveLetter);
    }

    [Fact]
    public void TheOsVolumeOverload_UsesTheSystemDrive()
    {
        Assert.True(BitLockerSuspender.IsDriveLetter(BitLockerPreflight.OsDriveLetter), BitLockerPreflight.OsDriveLetter);
        Assert.Equal(BitLockerPreflight.OsDriveLetter, BitLockerPreflight.OsDriveLetter.ToUpperInvariant());

        BitLockerAssessment assessment = BitLockerPreflight.Assess(Result(Volume(1, 1, TpmAndPassword, LegacyProfile) with { DriveLetter = BitLockerPreflight.OsDriveLetter }));

        Assert.Equal(BitLockerState.On, assessment.State);
        Assert.Equal(BitLockerPreflight.OsDriveLetter, assessment.DriveLetter);
    }

    [Theory]
    [InlineData(new[] { "TPM" }, new[] { 11, 7, 7 }, "2.0", "PCR 7, 11; TPM 2.0")]
    [InlineData(new[] { "TPM", "Numerical password" }, new[] { 0, 2, 4, 11 }, "1.2, 2, 3", "PCR 0, 2, 4, 11; TPM 1.2")]
    [InlineData(new[] { "TPM+PIN" }, new[] { 0, 2, 4, 11 }, null, "PCR 0, 2, 4, 11")]
    [InlineData(new[] { "TPM+PIN+startup key" }, new[] { 11, 4, 2, 0 }, "1.2", "PCR 0, 2, 4, 11; TPM 1.2")]
    [InlineData(new[] { "TPM" }, new int[0], "1.2, 2, 3", "PCR profile not read; TPM 1.2")]
    [InlineData(new[] { "Numerical password" }, new int[0], "1.2, 2, 3", "")]
    [InlineData(new[] { "TPM certificate" }, new int[0], "2.0", "")]
    [InlineData(new string[0], new int[0], null, "")]
    public void BootPathText_RendersThePcrListAndTheTpmVersion(string[] protectors, int[] pcrs, string? specVersion, string expected)
    {
        Assert.Equal(expected, BitLockerPreflight.BootPathText(protectors, pcrs, specVersion is null ? null : Tpm(specVersion)));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(8, false)]
    [InlineData(9, false)]
    [InlineData(10, false)]
    public void HasTpmProtector_IsTrueForTheFourTpmSealedTypesAsTheInspectorNamesThem(int keyProtectorType, bool expected)
    {
        Assert.Equal(expected, BitLockerPreflight.HasTpmProtector(new[] { BitLockerInspector.ProtectorTypeName(keyProtectorType) }));
        Assert.Equal(expected, BitLockerInspector.IsTpmFamily(keyProtectorType));
    }

    [Theory]
    [InlineData("1.2, 2, 3", "1.2")]
    [InlineData("2.0", "2.0")]
    [InlineData("2.0, 0, 1.38", "2.0")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void TpmVersionText_TakesTheFirstTokenOfSpecVersion(string? specVersion, string? expected)
    {
        Assert.Equal(expected, BitLockerPreflight.TpmVersionText(specVersion is null ? null : Tpm(specVersion)));
        Assert.Null(BitLockerPreflight.TpmVersionText(null));
    }

    [Fact]
    public void EverySentenceStaysUnder200Characters_AndNamesNoSecret()
    {
        string[] sentences =
        {
            BitLockerPreflight.Assess(Result(Volume(1, 1, TpmAndPassword, LegacyProfile)), Drive, Tpm("1.2, 2, 3")).StateSentence,
            BitLockerPreflight.Assess(Result(Volume(1, 1, PasswordOnly, Array.Empty<int>())), Drive, null).StateSentence,
            BitLockerPreflight.Assess(Result(Volume(0, 2, TpmAndPassword, LegacyProfile)), Drive, Tpm("1.2, 2, 3")).StateSentence,
            BitLockerPreflight.Assess(Result(Volume(0, 2, PasswordOnly, Array.Empty<int>())), Drive, null).StateSentence,
            BitLockerPreflight.Assess(Result(Volume(0, 1, TpmAndPassword, LegacyProfile)), Drive, null).StateSentence,
            BitLockerPreflight.Assess(Result(Volume(0, 0, TpmAndPassword, LegacyProfile)), Drive, null).StateSentence,
            BitLockerPreflight.Assess(Result(Volume(2, 1, TpmAndPassword, LegacyProfile)), Drive, null).StateSentence,
            BitLockerPreflight.Assess(Result(Volume(0, 4, TpmAndPassword, LegacyProfile)), Drive, null).StateSentence,
            BitLockerPreflight.Assess(Result(Volume(0, 9, TpmAndPassword, LegacyProfile)), Drive, null).StateSentence,
            BitLockerPreflight.Assess(new BitLockerResult(Array.Empty<BitLockerVolume>(), new[] { new string('x', 500) }), Drive, null).StateSentence,
            BitLockerPreflight.Assess(new BitLockerResult(Array.Empty<BitLockerVolume>(), Array.Empty<string>()), Drive, null).StateSentence,
            BitLockerPreflight.Assess(new BitLockerResult(new[] { Volume(0, 1, Array.Empty<string>(), Array.Empty<int>()) }, ProtectorListErrors(Drive, new InvalidOperationException("Generic failure"))), Drive, null).StateSentence,
            BitLockerPreflight.Assess(new BitLockerResult(new[] { Volume(1, 1, Array.Empty<string>(), Array.Empty<int>()) }, ProtectorListErrors(Drive, new InvalidOperationException("Generic failure"))), Drive, null).StateSentence,
            BitLockerPreflight.RecoveryKeySentence,
            BitLockerPreflight.ReorderRiskSentence,
            string.Format(BitLockerPreflight.ReorderRiskFirstEntryStaysFormat, "Boot0002"),
            string.Format(BitLockerPreflight.BootNextRiskFormat, "Boot0000"),
        };

        Assert.All(sentences, sentence =>
        {
            Assert.True(sentence.Length < 200, sentence.Length + ": " + sentence);
            Assert.DoesNotContain('\n', sentence);
            Assert.DoesNotContain("{", sentence, StringComparison.Ordinal);
            Assert.DoesNotContain("Recovery" + "Password", sentence, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void TheSentencesAreTheDesignsSentences()
    {
        Assert.Equal(
            "If the next start asks for the recovery key, you need the 48-digit recovery password saved when BitLocker was turned on. Have it at hand before you confirm. This application never reads or shows it.",
            BitLockerPreflight.RecoveryKeySentence);
        Assert.Equal(
            "Microsoft lists a change of the firmware boot order on a TPM 1.2 machine as a cause of BitLocker recovery. The first entry stays Boot0002.",
            string.Format(BitLockerPreflight.ReorderRiskFirstEntryStaysFormat, "Boot0002"));
        Assert.Equal(
            "The firmware tries Boot0000 once at the next start, then removes BootNext. If it cannot start, the normal list is used in that start and BitLocker may ask for the recovery key.",
            string.Format(BitLockerPreflight.BootNextRiskFormat, "Boot0000"));
    }

    [Fact]
    public void TheStateEnum_HasTheFiveStatesInTheDesignsOrder()
    {
        Assert.Equal(new[] { "Off", "Suspended", "On", "Encrypting", "Unknown" }, Enum.GetNames<BitLockerState>());
    }

    [Theory]
    [InlineData(1, 1, true, BitLockerState.On)]
    [InlineData(0, 2, true, BitLockerState.Encrypting)]
    [InlineData(0, 1, true, BitLockerState.Suspended)]
    [InlineData(0, 0, true, BitLockerState.Off)]
    [InlineData(0, 1, false, BitLockerState.Off)]
    [InlineData(2, 1, true, BitLockerState.Unknown)]
    [InlineData(0, 3, true, BitLockerState.Unknown)]
    [InlineData(0, 4, true, BitLockerState.Unknown)]
    [InlineData(0, 5, true, BitLockerState.Unknown)]
    public void TheTableOfTheDesign_MapsEveryRowToItsState(int protection, int conversion, bool withProtectors, BitLockerState expected)
    {
        BitLockerVolume volume = Volume(protection, conversion, withProtectors ? TpmAndPassword : Array.Empty<string>(), withProtectors ? LegacyProfile : Array.Empty<int>());

        BitLockerAssessment assessment = BitLockerPreflight.Assess(Result(volume), Drive, null);

        Assert.Equal(expected, assessment.State);
        Assert.Equal(expected is BitLockerState.On or BitLockerState.Encrypting or BitLockerState.Unknown, assessment.NeedsChoice);
        Assert.Equal(expected != BitLockerState.Off, assessment.WarningShown);
        Assert.Equal(expected == BitLockerState.Unknown, assessment.Reason is not null);
    }

    // ----- helpers -----

    /// <summary>
    /// The error lines the inspector itself records for this drive's protector list: its reader
    /// runs over a volume whose list call throws <paramref name="failure"/>, returns
    /// <paramref name="returnValue"/>, or is not made because another call timed out first.
    /// </summary>
    private static string[] ProtectorListErrors(string drive, Exception? failure = null, uint returnValue = 0, bool alreadyTimedOut = false)
    {
        string? timedOut = alreadyTimedOut ? "GetKeyProtectors on Z:" : null;
        List<string> errors = new();
        _ = new BitLockerInspector().ReadProtectors(new ProtectorListOnly(failure, returnValue), drive, ref timedOut, errors);
        return errors.ToArray();
    }

    private static BitLockerVolume Volume(int protection, int conversion, IReadOnlyList<string> protectors, IReadOnlyList<int> pcrs)
    {
        int[] profile = BitLockerInspector.NormalizePcrProfile(pcrs);
        return new BitLockerVolume(
            Drive,
            protection,
            BitLockerInspector.ProtectionStatusText(protection, conversion),
            conversion,
            BitLockerInspector.ConversionStatusText(conversion),
            protectors,
            profile,
            BitLockerInspector.PcrProfileText(profile));
    }

    private static BitLockerResult Result(BitLockerVolume volume)
    {
        return new BitLockerResult(new[] { volume }, Array.Empty<string>());
    }

    private static TpmInfoResult Tpm(string specVersion)
    {
        return new TpmInfoResult(true, specVersion, true, true, Array.Empty<string>());
    }

    /// <summary>A volume whose protector list call fails, returns a code with no protector, or is never reached; no other call is expected.</summary>
    private sealed class ProtectorListOnly : IEncryptableVolumeMethods
    {
        private readonly Exception? _failure;
        private readonly uint _returnValue;

        public ProtectorListOnly(Exception? failure, uint returnValue)
        {
            _failure = failure;
            _returnValue = returnValue;
        }

        public VolumeMethodResult<IReadOnlyList<string>> GetKeyProtectors()
        {
            if (_failure is not null)
            {
                throw _failure;
            }

            return new VolumeMethodResult<IReadOnlyList<string>>(_returnValue, Array.Empty<string>());
        }

        public VolumeMethodResult<int?> GetKeyProtectorType(string protectorId) => throw new InvalidOperationException("no protector is listed, so no type is read");

        public VolumeMethodResult<IReadOnlyList<int>> GetPlatformValidationProfile(string protectorId) => throw new InvalidOperationException("no protector is listed, so no profile is read");
    }
}
