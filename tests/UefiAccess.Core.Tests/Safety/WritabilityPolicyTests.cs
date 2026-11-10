// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;
using System.Text.RegularExpressions;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Variables;
using Xunit;
using static UefiAccess.Core.Tests.Fixtures.LoadOptionFixtures;
using static UefiAccess.Core.Tests.Safety.SafetyFixtures;

namespace UefiAccess.Core.Tests.Safety;

/// <summary>
/// Task 3.6: every verdict and sentence, and the item status values of
/// section 5.4, with the status BootOrder and BootNext carry on a machine that is not the
/// contracted one (decided 25 September 2026). The sentences are pinned verbatim because the
/// application quotes them.
/// </summary>
public sealed class WritabilityPolicyTests
{
    private static WritabilityVerdict Describe(VariableSet set, string name, bool offline = false, bool machineConfirmed = true, Guid? guid = null)
    {
        FirmwareVariable variable = set.Find(name, guid ?? Global) ?? throw new InvalidOperationException(name + " is not in the fixture store");
        return WritabilityPolicy.Describe(variable, set, offline, machineConfirmed);
    }

    [Fact]
    public void BootOrder_LiveAndConfirmed_IsReorderOnly()
    {
        WritabilityVerdict verdict = Describe(Store(), "BootOrder");

        Assert.Equal(Writability.ReorderOnly, verdict.Verdict);
        Assert.Equal("Changeable: the order of its entries can be changed from this application (Move up, Move down, Review change). Nothing else about it can be changed.", verdict.Sentence);
        Assert.Equal("Changeable: order of entries", verdict.ItemStatus);
        Assert.True(verdict.IsChangeable);
        Assert.False(verdict.FirmwareSetupHintApplies);
    }

    [Fact]
    public void BootNext_LiveAndConfirmed_IsOneTimeBoot()
    {
        WritabilityVerdict verdict = Describe(Store(Var("BootNext", U16(WindowsOnPartition1))), "BootNext");

        Assert.Equal(Writability.OneTimeBoot, verdict.Verdict);
        Assert.Equal("Changeable: this one-time boot choice can be removed (Clear one-time boot) or put back from a snapshot (Restore previous value).", verdict.Sentence);
        Assert.Equal("Changeable: one-time boot", verdict.ItemStatus);
        Assert.True(verdict.IsChangeable);
    }

    [Fact]
    public void ActiveBootEntry_IsAOneTimeBootTarget_AndReadOnly()
    {
        WritabilityVerdict verdict = Describe(Store(), "Boot0002");

        Assert.Equal(Writability.OneTimeBootTarget, verdict.Verdict);
        Assert.Equal("Cannot be changed. This entry can be chosen for one boot (Boot once from this entry).", verdict.Sentence);
        Assert.Equal(string.Empty, verdict.ItemStatus);
        Assert.False(verdict.IsChangeable);
    }

    [Theory]
    [InlineData("Boot0006")]
    [InlineData("Boot0004")]
    [InlineData("Boot0008")]
    public void InactiveApplicationOrMalformedBootEntry_IsNotWhitelisted(string name)
    {
        WritabilityVerdict verdict = Describe(Store(), name);

        Assert.Equal(Writability.NotWhitelisted, verdict.Verdict);
        Assert.Equal("Cannot be changed: this variable is not on the whitelist of this application. Only BootOrder (reorder) and BootNext can be changed.", verdict.Sentence);
        Assert.Equal(string.Empty, verdict.ItemStatus);
    }

    [Fact]
    public void HiddenActiveBootEntry_IsStillATarget()
    {
        Assert.Equal(Writability.OneTimeBootTarget, Describe(Store(), "Boot0005").Verdict);
    }

    [Fact]
    public void DellBootFFFB_UnderTheVendorGuid_IsNotABootEntryAndNotWhitelisted()
    {
        WritabilityVerdict verdict = Describe(Store(), "BootFFFB", guid: DellBootGuid);

        Assert.Equal(Writability.NotWhitelisted, verdict.Verdict);
        Assert.False(verdict.FirmwareSetupHintApplies);
    }

    [Theory]
    [InlineData("Timeout")]
    [InlineData("BootCurrent")]
    public void OtherGlobalVariables_AreNotWhitelisted(string name)
    {
        Assert.Equal(Writability.NotWhitelisted, Describe(Store(), name).Verdict);
    }

    [Fact]
    public void SignedVariable_IsAuthenticatedWrite_WithTheFlagAndTheFirmwareSetupHint()
    {
        WritabilityVerdict pk = Describe(Store(), "PK");

        Assert.Equal(Writability.AuthenticatedWrite, pk.Verdict);
        Assert.Equal("Cannot be changed: authenticated write (TA); only a signed update can change it. This application never writes it.", pk.Sentence);
        Assert.True(pk.FirmwareSetupHintApplies);
        Assert.Equal(string.Empty, pk.ItemStatus);
    }

    [Fact]
    public void SecureBootStateVariable_WithTheDeprecatedAwBit_IsAuthenticatedWrite()
    {
        WritabilityVerdict verdict = Describe(Store(), "SecureBoot");

        Assert.Equal(Writability.AuthenticatedWrite, verdict.Verdict);
        Assert.Equal("Cannot be changed: authenticated write (AW); only a signed update can change it. This application never writes it.", verdict.Sentence);
        Assert.True(verdict.FirmwareSetupHintApplies);
    }

    [Fact]
    public void ImageSecurityDatabase_GetsTheFirmwareSetupHint()
    {
        WritabilityVerdict verdict = Describe(Store(), "db", guid: KnownGuids.EfiImageSecurityDatabase);

        Assert.Equal(Writability.AuthenticatedWrite, verdict.Verdict);
        Assert.True(verdict.FirmwareSetupHintApplies);
    }

    [Fact]
    public void VendorVariableWithAnAuthenticatedBit_IsAuthenticatedWrite_WithoutTheHint()
    {
        FirmwareVariable vendor = new("Setting", DellBootGuid, NvBsRt | VariableAttributes.EnhancedAuthenticatedAccess, new byte[] { 1 });

        WritabilityVerdict verdict = WritabilityPolicy.Describe(vendor, Store(), offline: false, machineConfirmed: true);

        Assert.Equal(Writability.AuthenticatedWrite, verdict.Verdict);
        Assert.Contains("(EA)", verdict.Sentence, StringComparison.Ordinal);
        Assert.False(verdict.FirmwareSetupHintApplies);
    }

    [Fact]
    public void WhitelistedVariableWithAnAuthenticatedBit_IsAuthenticatedWrite_NotChangeable()
    {
        WritabilityVerdict verdict = Describe(Store(Var("BootOrder", U16(WindowsOnPartition1), NvBsRtTa)), "BootOrder");

        Assert.Equal(Writability.AuthenticatedWrite, verdict.Verdict);
        Assert.False(verdict.IsChangeable);
    }

    [Theory]
    [InlineData("BootOrder")]
    [InlineData("Boot0002")]
    public void Offline_IsReadOnlyForWhatWouldOtherwiseBeChangeable(string name)
    {
        WritabilityVerdict verdict = Describe(Store(), name, offline: true);

        Assert.Equal(Writability.Offline, verdict.Verdict);
        Assert.Equal("Cannot be changed: shown from a baseline folder; nothing can be changed offline.", verdict.Sentence);
        Assert.Equal(string.Empty, verdict.ItemStatus);
    }

    [Fact]
    public void Offline_KeepsTheIntrinsicReasonForOtherVariables()
    {
        Assert.Equal(Writability.NotWhitelisted, Describe(Store(), "Timeout", offline: true).Verdict);
        Assert.Equal(Writability.AuthenticatedWrite, Describe(Store(), "PK", offline: true).Verdict);
    }

    [Theory]
    [InlineData("BootOrder")]
    [InlineData("Boot0002")]
    public void MachineNotConfirmed_IsWrongMachine(string name)
    {
        WritabilityVerdict verdict = Describe(Store(), name, machineConfirmed: false);

        Assert.Equal(Writability.WrongMachine, verdict.Verdict);
        Assert.Equal("Cannot be changed on this machine: changes are enabled only on one specific OptiPlex 7020 with BIOS A18 (the enabled unit). The Change controls and the dialog work up to Confirm change, which refuses; nothing is written.", verdict.Sentence);
        Assert.False(verdict.IsChangeable);
        Assert.False(verdict.FirmwareSetupHintApplies);
    }

    /// <summary>
    /// Decided 25 September 2026: on a machine that is not the contracted one, BootOrder and
    /// BootNext carry an item status that says where they can be changed, so a tester there can
    /// report whether an item status is spoken; they stay read-only.
    /// </summary>
    [Theory]
    [InlineData("BootOrder")]
    [InlineData("BootNext")]
    public void MachineNotConfirmed_BootOrderAndBootNext_CarryTheItemStatusElsewhere(string name)
    {
        WritabilityVerdict verdict = Describe(Store(Var("BootNext", U16(UsbNumber))), name, machineConfirmed: false);

        Assert.Equal(Writability.WrongMachine, verdict.Verdict);
        Assert.Equal("Not changeable on this machine", verdict.ItemStatus);
        Assert.Equal(WritabilitySentences.WrongMachine, verdict.Sentence);
        Assert.False(verdict.IsChangeable);
        Assert.False(verdict.FirmwareSetupHintApplies);
    }

    [Theory]
    [InlineData("Boot0002")]
    [InlineData("Boot0000")]
    [InlineData("Boot0003")]
    public void MachineNotConfirmed_ABootEntry_IsWrongMachine_WithNoItemStatus(string name)
    {
        WritabilityVerdict verdict = Describe(Store(), name, machineConfirmed: false);

        Assert.Equal(Writability.WrongMachine, verdict.Verdict);
        Assert.Equal(string.Empty, verdict.ItemStatus);
    }

    [Fact]
    public void MachineNotConfirmed_OnlyBootOrderAndBootNextCarryAnItemStatus()
    {
        VariableSet set = Store(Var("BootNext", U16(UsbNumber)));

        foreach (FirmwareVariable variable in set.All)
        {
            WritabilityVerdict verdict = WritabilityPolicy.Describe(variable, set, offline: false, machineConfirmed: false);
            bool whitelisted = variable.VendorGuid == Global && (variable.Name is "BootOrder" or "BootNext");

            Assert.Equal(whitelisted ? WritabilitySentences.ItemStatusElsewhere : string.Empty, verdict.ItemStatus);
            Assert.False(verdict.IsChangeable);
        }
    }

    [Fact]
    public void MachineNotConfirmed_AnOfflineMalformedOrSignedBootOrder_KeepsNoItemStatus()
    {
        WritabilityVerdict offline = Describe(Store(), "BootOrder", offline: true, machineConfirmed: false);
        WritabilityVerdict malformed = Describe(Store(Var("BootOrder", new byte[] { 2, 0, 3 })), "BootOrder", machineConfirmed: false);
        WritabilityVerdict signed = Describe(Store(Var("BootOrder", U16(WindowsOnPartition1), NvBsRtTa)), "BootOrder", machineConfirmed: false);

        Assert.Equal(Writability.Offline, offline.Verdict);
        Assert.Equal(Writability.Malformed, malformed.Verdict);
        Assert.Equal(Writability.AuthenticatedWrite, signed.Verdict);
        Assert.All(new[] { offline, malformed, signed }, verdict => Assert.Equal(string.Empty, verdict.ItemStatus));
    }

    [Fact]
    public void OddByteCount_IsMalformed_WithTheV1Reason()
    {
        WritabilityVerdict verdict = Describe(Store(Var("BootOrder", new byte[] { 2, 0, 3 })), "BootOrder");

        Assert.Equal(Writability.Malformed, verdict.Verdict);
        Assert.Equal("Cannot be changed until fixed: BootOrder has an odd byte count (3); the firmware's list is malformed. Nothing can be changed until it is fixed in BIOS Setup.", verdict.Sentence);
        Assert.Equal(string.Empty, verdict.ItemStatus);
    }

    [Fact]
    public void FourByteBootNext_IsMalformed_WithTheV2Reason()
    {
        WritabilityVerdict verdict = Describe(Store(Var("BootNext", Concat(U16(2), U16(3)))), "BootNext");

        Assert.Equal(Writability.Malformed, verdict.Verdict);
        Assert.Equal("Cannot be changed until fixed: BootNext holds 4 bytes, not 2; it cannot be changed by this tool. Restart to let the firmware remove it.", verdict.Sentence);
    }

    [Fact]
    public void ListNamingAMissingEntry_IsMalformed_WithTheV5Reason()
    {
        WritabilityVerdict verdict = Describe(Store(Var("BootOrder", Concat(U16(WindowsOnPartition1), U16(MissingNumber)))), "BootOrder");

        Assert.Equal(Writability.Malformed, verdict.Verdict);
        Assert.Equal("Cannot be changed until fixed: BootOrder refers to Boot0010, which does not exist; the firmware may rewrite the list at the next start. Fix it in BIOS Setup first.", verdict.Sentence);
    }

    [Fact]
    public void AttributesOtherThan7_IsMalformed_WithTheV9Reason()
    {
        WritabilityVerdict verdict = Describe(Store(Var("BootOrder", U16(WindowsOnPartition1), VariableAttributes.NonVolatile | VariableAttributes.BootServiceAccess)), "BootOrder");

        Assert.Equal(Writability.Malformed, verdict.Verdict);
        Assert.Equal("Cannot be changed until fixed: BootOrder carries attributes NV BS, not NV BS RT; it is not writable from Windows.", verdict.Sentence);
    }

    [Fact]
    public void EmptyBootOrder_IsMalformed_WithTheV1Reason()
    {
        WritabilityVerdict verdict = Describe(Store(Var("BootOrder", Array.Empty<byte>())), "BootOrder");

        Assert.Equal(Writability.Malformed, verdict.Verdict);
        Assert.Equal("Cannot be changed until fixed: " + CheckSentences.BootOrderEmpty, verdict.Sentence);
    }

    [Fact]
    public void Malformed_NamesTheFirstProblemInTheValidatorsOrder_AttributesBeforeTheByteCount()
    {
        WritabilityVerdict verdict = Describe(Store(Var("BootOrder", new byte[] { 2, 0, 3 }, VariableAttributes.NonVolatile | VariableAttributes.BootServiceAccess)), "BootOrder");

        Assert.Equal(Writability.Malformed, verdict.Verdict);
        Assert.Equal("Cannot be changed until fixed: BootOrder carries attributes NV BS, not NV BS RT; it is not writable from Windows.", verdict.Sentence);
    }

    [Fact]
    public void Malformed_AgreesWithTheValidator_OnEveryStoreItBlocks()
    {
        VariableSet[] stores =
        {
            Store(Var("BootOrder", new byte[] { 2, 0, 3 })),
            Store(Var("BootOrder", Array.Empty<byte>())),
            Store(Var("BootOrder", BootList(WindowsOnPartition1, MissingNumber))),
            Store(Var("BootOrder", BootList(WindowsOnPartition1), BsRt)),
        };

        foreach (VariableSet set in stores)
        {
            WritabilityVerdict verdict = Describe(set, "BootOrder");
            ValidationResult check = Validate(Check("BootOrder"), set);

            Assert.Equal(Writability.Malformed, verdict.Verdict);
            Assert.False(check.Passed);
            Assert.Equal("Cannot be changed until fixed: " + check.BlockedReason, verdict.Sentence);
        }
    }

    [Fact]
    public void TheUnitsSecondWindowsBootManager_IsATarget_TheDryRunJudgesTheBootPath()
    {
        WritabilityVerdict verdict = Describe(Store(), "Boot0000");

        Assert.Equal(Writability.OneTimeBootTarget, verdict.Verdict);
    }

    [Fact]
    public void Malformed_TakesPrecedenceOverWrongMachine()
    {
        WritabilityVerdict verdict = Describe(Store(Var("BootOrder", new byte[] { 2, 0, 3 })), "BootOrder", machineConfirmed: false);

        Assert.Equal(Writability.Malformed, verdict.Verdict);
    }

    [Fact]
    public void OnTheContractedMachine_ItemStatus_IsNonEmptyOnlyForTheTwoChangeableVerdicts()
    {
        VariableSet set = Store(Var("BootNext", U16(WindowsOnPartition1)));

        foreach (FirmwareVariable variable in set.All)
        {
            WritabilityVerdict verdict = WritabilityPolicy.Describe(variable, set, offline: false, machineConfirmed: true);
            Assert.Equal(verdict.IsChangeable, verdict.ItemStatus.Length > 0);
            Assert.Equal(verdict.IsChangeable, verdict.Sentence.StartsWith("Changeable:", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ItemStatusConstants_AreTheSection54Values()
    {
        Assert.Equal("Changeable: order of entries", WritabilitySentences.ItemStatusReorder);
        Assert.Equal("Changeable: one-time boot", WritabilitySentences.ItemStatusOneTimeBoot);
        Assert.Equal("Not changeable on this machine", WritabilitySentences.ItemStatusElsewhere);
        Assert.Equal("", WritabilitySentences.ItemStatusNone);
    }

    /// <summary>
    /// Changed on 7 October 2026: on a machine where nothing can be
    /// changed, the status of BootOrder and BootNext was "Changeable on the M3 test unit only",
    /// which began with the first word of the two statuses of a changeable row. It now begins with
    /// what holds for this machine, so no refused row's status begins with "Changeable", in any case.
    /// </summary>
    [Theory]
    [InlineData("BootOrder")]
    [InlineData("BootNext")]
    public void OnAnotherMachine_TheItemStatus_NeverBeginsWithChangeable(string name)
    {
        WritabilityVerdict verdict = Describe(Store(Var("BootNext", U16(WindowsOnPartition1))), name, machineConfirmed: false);

        Assert.Equal(Writability.WrongMachine, verdict.Verdict);
        Assert.False(verdict.IsChangeable);
        Assert.StartsWith("Not changeable", verdict.ItemStatus, StringComparison.Ordinal);
        Assert.False(verdict.ItemStatus.StartsWith("changeable", StringComparison.OrdinalIgnoreCase));
        Assert.StartsWith("Cannot be changed on this machine:", verdict.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void EverySentence_IsOneLineAndWellUnderTheValueBoxLimit()
    {
        string[] sentences =
        {
            WritabilitySentences.ReorderOnly,
            WritabilitySentences.OneTimeBoot,
            WritabilitySentences.OneTimeBootTarget,
            WritabilitySentences.NotWhitelisted,
            WritabilitySentences.Offline,
            WritabilitySentences.WrongMachine,
        };
        Assert.All(sentences, sentence =>
        {
            Assert.DoesNotContain('\n', sentence);
            Assert.InRange(sentence.Length, 1, 400);
        });
    }

    /// <summary>
    /// Alpha 2 finding A2-6 (the tester's A2-4, "Read Only Read Only" on tabbing into a box of the detail
    /// pane), resolved on 28 September 2026: the Writability line is a read-only text box, whose
    /// state the screen reader says itself ("edit, read only"), so no sentence of the line, constant
    /// or format, begins with "Read-only" or "Read only" in any casing, or the box would say its
    /// own state again as the value's first word. The sentence of a variable this application
    /// cannot change begins "Cannot be changed"; the two changeable sentences begin "Changeable:".
    /// Every constant of <see cref="WritabilitySentences"/> is checked, so a sentence added later
    /// is too, and so is every sentence the policy gives over the fixture store (the formats
    /// filled), live and offline, on the M3 test unit and elsewhere, and for a malformed BootOrder
    /// and BootNext.
    /// </summary>
    [Fact]
    public void NoWritabilitySentence_BeginsWithReadOnly_SoTheBoxNeverSaysItsStateTwice()
    {
        Regex readOnlyStart = new(@"^\W*read[\s-]*only", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        Assert.Matches(readOnlyStart, "Read-only: not on the whitelist");
        Assert.Matches(readOnlyStart, "read only");
        Assert.Matches(readOnlyStart, "READ-ONLY until fixed");
        Assert.DoesNotMatch(readOnlyStart, WritabilitySentences.OneTimeBootTarget);

        List<(string Name, string Text)> constants = typeof(WritabilitySentences).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (field.Name, (string)field.GetRawConstantValue()!))
            .ToList();
        Assert.Contains(constants, constant => constant.Name == nameof(WritabilitySentences.WrongMachine));
        Assert.Contains(constants, constant => constant.Name == nameof(WritabilitySentences.MalformedFormat));
        Assert.Contains(constants, constant => constant.Name == nameof(WritabilitySentences.AuthenticatedWriteFormat));

        List<string> problems = constants
            .Where(constant => readOnlyStart.IsMatch(constant.Text))
            .Select(constant => "WritabilitySentences." + constant.Name + ": \"" + constant.Text + "\"")
            .ToList();

        VariableSet[] stores =
        {
            Store(Var("BootNext", U16(WindowsOnPartition1))),
            Store(Var("BootOrder", new byte[] { 2, 0, 3 }), Var("BootNext", Concat(U16(2), U16(3)))),
        };
        int verdicts = 0;
        foreach (VariableSet set in stores)
        {
            foreach (FirmwareVariable variable in set.All)
            {
                foreach ((bool offline, bool machineConfirmed) in new[] { (false, true), (false, false), (true, true), (true, false) })
                {
                    WritabilityVerdict verdict = WritabilityPolicy.Describe(variable, set, offline, machineConfirmed);
                    verdicts++;
                    string expectedStart = verdict.IsChangeable ? "Changeable:" : "Cannot be changed";
                    if (readOnlyStart.IsMatch(verdict.Sentence) || !verdict.Sentence.StartsWith(expectedStart, StringComparison.Ordinal))
                    {
                        problems.Add(variable.Name + " (" + verdict.Verdict + "): \"" + verdict.Sentence + "\"");
                    }
                }
            }
        }

        Assert.True(verdicts > 0);
        Assert.True(problems.Count == 0, "Writability sentences that begin with the box's own state:" + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void IsSecureBootVariable_NamesExactlyTheSix()
    {
        Assert.True(WritabilityPolicy.IsSecureBootVariable("SecureBoot", Global));
        Assert.True(WritabilityPolicy.IsSecureBootVariable("SetupMode", Global));
        Assert.True(WritabilityPolicy.IsSecureBootVariable("PK", Global));
        Assert.True(WritabilityPolicy.IsSecureBootVariable("KEK", Global));
        Assert.True(WritabilityPolicy.IsSecureBootVariable("db", KnownGuids.EfiImageSecurityDatabase));
        Assert.True(WritabilityPolicy.IsSecureBootVariable("dbx", KnownGuids.EfiImageSecurityDatabase));
        Assert.False(WritabilityPolicy.IsSecureBootVariable("db", Global));
        Assert.False(WritabilityPolicy.IsSecureBootVariable("dbt", KnownGuids.EfiImageSecurityDatabase));
        Assert.False(WritabilityPolicy.IsSecureBootVariable("pk", Global));
        Assert.False(WritabilityPolicy.IsSecureBootVariable("BootOrder", Global));
    }

    [Fact]
    public void Describe_NullVariable_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => WritabilityPolicy.Describe(null!, Store(), false, true));
    }
}
