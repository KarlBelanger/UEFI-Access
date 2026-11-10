// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Variables;
using Xunit;
using static UefiAccess.Core.Tests.Fixtures.LoadOptionFixtures;
using static UefiAccess.Core.Tests.Safety.SafetyFixtures;

namespace UefiAccess.Core.Tests.Safety;

/// <summary>
/// Rules V1 to V17, one test pair or theory per rule, passing and
/// blocking, over the fixtures of section 7.1: the unit's own Boot0002 and Boot0000, a USB entry,
/// an App entry, a hidden entry, an odd BootOrder, a 4-byte BootNext, a bootx64.efi entry on
/// another partition, a set without BootCurrent and a BootCurrent naming the USB entry. Every
/// blocked case names the check and its exact sentence, builds no after-image and cannot become
/// a <see cref="ChangePlan"/> (so no token and no commit can follow). The validator is handed no
/// writer at all, which <see cref="Validator_HoldsNothingThatCanWrite"/> pins by reflection; that
/// a blocking rule leaves the pipeline's writer uncalled is asserted through the transaction, one
/// case per rule, in WriteTransactionTests.EveryValidationRuleThatRefuses_LeavesTheWriterUncalled.
/// </summary>
public sealed class ChangeValidatorTests
{
    private const string RestoreFolder = @"C:\ProgramData\UefiAccess\snapshots\20260930-120000-reorder";

    /// <summary>Every state in which BitLocker protectors may exist: rules V13, V16 and V17 apply in each.</summary>
    public static readonly TheoryData<BitLockerState> ProtectedStates = new()
    {
        BitLockerState.On,
        BitLockerState.Encrypting,
        BitLockerState.Suspended,
        BitLockerState.Unknown,
    };

    // ---- V1: the current BootOrder has an even byte count and at least 2 bytes -------------------

    [Fact]
    public void V1_OddBootOrder_IsBlockedByFormat()
    {
        VariableSet set = Store(Var("BootOrder", new byte[] { 2, 0, 3 }));

        AssertBlocked(Run(Reorder(UsbNumber, WindowsOnPartition1), set), CheckNames.Format, "BootOrder has an odd byte count (3); the firmware's list is malformed. Nothing can be changed until it is fixed in BIOS Setup.");
        AssertBlocked(Run(Check("BootOrder"), set), CheckNames.Format, "BootOrder has an odd byte count (3); the firmware's list is malformed. Nothing can be changed until it is fixed in BIOS Setup.");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void V1_AbsentOrEmptyBootOrder_IsBlockedByFormat(bool absent)
    {
        VariableSet set = absent ? Store().Without("BootOrder") : Store(Var("BootOrder", Array.Empty<byte>()));

        AssertBlocked(Run(Reorder(UsbNumber, WindowsOnPartition1), set), CheckNames.Format, CheckSentences.BootOrderEmpty);
    }

    [Fact]
    public void V1_EvenBootOrder_PassesFormat()
    {
        ValidationResult result = Run(Reorder(UsbNumber, WindowsOnPartition1), Store());

        AssertCheckPassed(result, CheckNames.Format);
        AssertPlan(result);
    }

    [Theory]
    [InlineData("odd")]
    [InlineData("empty")]
    [InlineData("absent")]
    public void V1_JudgesTheCurrentBootOrderForAOneTimeBootToo_ButNotForAClear(string variant)
    {
        VariableSet set = variant switch
        {
            "odd" => Store(Var("BootOrder", new byte[] { 2, 0, 3 }), Var("BootNext", U16(UsbNumber))),
            "empty" => Store(Var("BootOrder", Array.Empty<byte>()), Var("BootNext", U16(UsbNumber))),
            _ => Store(Var("BootNext", U16(UsbNumber))).Without("BootOrder"),
        };
        string reason = variant == "odd"
            ? "BootOrder has an odd byte count (3); the firmware's list is malformed. Nothing can be changed until it is fixed in BIOS Setup."
            : CheckSentences.BootOrderEmpty;

        AssertBlocked(Run(Next(WindowsOnPartition1), set), CheckNames.Format, reason);
        AssertBlocked(Run(Next(WindowsOnPartition1), set, BitLockerState.Off), CheckNames.Format, reason);

        // A clear only removes what the firmware would remove at the next start: V14 and V2 judge it.
        AssertPlan(Run(Clear(), set));
    }

    // ---- V2: the current BootNext, when present, is exactly 2 bytes ------------------------------

    [Theory]
    [InlineData(4, false)]
    [InlineData(4, true)]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(3, true)]
    public void V2_BootNextNotTwoBytes_IsBlockedByFormat(int size, bool clear)
    {
        byte[] bootNext = new byte[size];
        VariableSet set = Store(Var("BootNext", bootNext));
        ChangeRequest request = clear ? Clear() : Next(WindowsOnPartition1);

        AssertBlocked(Run(request, set), CheckNames.Format, $"BootNext holds {size} bytes, not 2; it cannot be changed by this tool. Restart to let the firmware remove it.");
    }

    [Fact]
    public void V2_FourByteBootNext_IsBlockedInThePreflightToo()
    {
        AssertBlocked(Run(Check("BootNext"), Store(Var("BootNext", BootList(UsbNumber, WindowsOnPartition1)))), CheckNames.Format, "BootNext holds 4 bytes, not 2; it cannot be changed by this tool. Restart to let the firmware remove it.");
    }

    [Fact]
    public void V2_TwoByteBootNext_PassesFormat_ForAOneTimeBootAndAClear()
    {
        VariableSet set = Store(Var("BootNext", U16(UsbNumber)));

        ValidationResult next = Run(Next(WindowsOnPartition1), set);
        ValidationResult clear = Run(Clear(), set);

        AssertCheckPassed(next, CheckNames.Format);
        AssertPlan(next);
        AssertCheckPassed(clear, CheckNames.Format);
        AssertPlan(clear);
    }

    // ---- V3: every referenced Boot#### parses -----------------------------------------------------

    [Fact]
    public void V3_MalformedEntryInTheOrder_IsBlockedByFormat()
    {
        ValidationResult result = Run(Reorder(MalformedNumber, WindowsOnPartition1), StoreWithOrder(WindowsOnPartition1, MalformedNumber));

        AssertBlocked(result, CheckNames.Format, "Boot0008 exists but cannot be decoded as a load option; the firmware's entry is malformed.");
    }

    [Fact]
    public void V3_MalformedOneTimeTarget_IsBlockedByFormat()
    {
        AssertBlocked(Run(Next(MalformedNumber), Store()), CheckNames.Format, "Boot0008 exists but cannot be decoded as a load option; the firmware's entry is malformed.");
    }

    [Fact]
    public void V3_UnitEntriesParse_AndAClearOfABootNextNamingAMalformedEntryIsAllowed()
    {
        AssertCheckPassed(Run(Next(WindowsOnPartition1), Store()), CheckNames.Format);

        ValidationResult clear = Run(Clear(), Store(Var("BootNext", U16(MalformedNumber))));
        AssertPlan(clear);
    }

    // ---- V4: every requested entry exists under the EFI global GUID ------------------------------

    [Fact]
    public void V4_UnknownEntryInTheOrder_IsBlockedByEntriesExist()
    {
        AssertBlocked(Run(Reorder(WindowsOnPartition1, MissingNumber), Store()), CheckNames.EntriesExist, "Boot0010 does not exist on this machine.");
    }

    [Fact]
    public void V4_UnknownOneTimeTarget_IsBlockedByEntriesExist()
    {
        AssertBlocked(Run(Next(MissingNumber), Store()), CheckNames.EntriesExist, "Boot0010 does not exist on this machine.");
    }

    [Fact]
    public void V4_DellBootFFFBUnderItsVendorGuid_IsNotABootEntry()
    {
        AssertBlocked(Run(Next(0xFFFB), Store()), CheckNames.EntriesExist, "BootFFFB does not exist on this machine.");
    }

    [Fact]
    public void V4_ExistingEntries_PassEntriesExist()
    {
        AssertCheckPassed(Run(Reorder(UsbNumber, WindowsOnPartition1), Store()), CheckNames.EntriesExist);
        AssertCheckPassed(Run(Next(WindowsOnPartition1), Store()), CheckNames.EntriesExist);
    }

    // ---- V5: every entry of the current BootOrder exists -----------------------------------------

    [Fact]
    public void V5_CurrentListNamingAMissingEntry_IsBlockedByCurrentListIntact_BeforeV4()
    {
        VariableSet set = StoreWithOrder(WindowsOnPartition1, MissingNumber);
        const string Reason = "BootOrder refers to Boot0010, which does not exist; the firmware may rewrite the list at the next start. Fix it in BIOS Setup first.";

        AssertBlocked(Run(Reorder(MissingNumber, WindowsOnPartition1), set), CheckNames.CurrentListIntact, Reason);
        AssertBlocked(Run(Reorder(WindowsOnPartition1), set), CheckNames.CurrentListIntact, Reason);
        AssertBlocked(Run(Check("BootOrder"), set), CheckNames.CurrentListIntact, Reason);

        // Design section 2.3: an intact BootOrder is a pre-condition of a one-time boot too; a clear is not judged by it.
        AssertBlocked(Run(Next(WindowsOnPartition1), set), CheckNames.CurrentListIntact, Reason);
        AssertPlan(Run(Clear(), Store(Var("BootOrder", BootList(WindowsOnPartition1, MissingNumber)), Var("BootNext", U16(UsbNumber)))));
    }

    [Fact]
    public void V5_CurrentListNamingAMissingEntry_ShowsItAsMissingInTheBeforeLines()
    {
        ValidationResult result = Run(Reorder(MissingNumber, WindowsOnPartition1), StoreWithOrder(WindowsOnPartition1, MissingNumber));

        EntryLine missing = result.BeforeEntries[1];
        Assert.False(missing.Present);
        Assert.Equal("Boot0010", missing.Name);
        Assert.Equal(CheckSentences.MissingEntryDescription, missing.Description);
        Assert.Equal("missing", missing.Flags);
    }

    [Fact]
    public void V5_IntactList_PassesCurrentListIntact()
    {
        AssertCheckPassed(Run(Reorder(UsbNumber, WindowsOnPartition1), Store()), CheckNames.CurrentListIntact);
    }

    // ---- V6: the new order is a permutation of the current one -----------------------------------

    [Theory]
    [InlineData(new ushort[] { 2, 3 }, new ushort[] { 2, 3, 0 }, "The new order adds Boot0000, which is not in the firmware's list.")]
    [InlineData(new ushort[] { 2 }, new ushort[] { 2, 0 }, "The new order adds Boot0000, which is not in the firmware's list.")]
    [InlineData(new ushort[] { 2, 3 }, new ushort[] { 2 }, "The new order leaves out Boot0003.")]
    [InlineData(new ushort[] { 2, 3, 0 }, new ushort[] { 0, 2 }, "The new order leaves out Boot0003.")]
    [InlineData(new ushort[] { 2 }, new ushort[] { 2, 2 }, "Boot0002 appears twice.")]
    [InlineData(new ushort[] { 2, 3 }, new ushort[] { 3, 3 }, "Boot0003 appears twice.")]
    public void V6_NotAPermutation_IsBlockedByPermutation(ushort[] current, ushort[] requested, string reason)
    {
        AssertBlocked(Run(Reorder(requested), StoreWithOrder(current), BitLockerState.Off), CheckNames.Permutation, reason);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void V6_RestoreWhoseEntryLeftTheList_IsBlockedWithTheRestoreSentence(bool entryDeleted)
    {
        VariableSet set = StoreWithOrder(WindowsOnPartition1);
        if (entryDeleted)
        {
            set = set.Without("Boot0003");
        }

        ReorderRequest restore = Reorder(UsbNumber, WindowsOnPartition1) with { RestoredFrom = RestoreFolder };

        AssertBlocked(Run(restore, set), CheckNames.Permutation, "Restore blocked: the snapshot's order lists Boot0003, which is no longer in BootOrder; the list was changed by Windows or the firmware since the snapshot. Nothing was written.");
    }

    [Fact]
    public void V6_Permutation_Passes_ForAChangeAndForARestore()
    {
        AssertCheckPassed(Run(Reorder(UsbNumber, WindowsOnPartition1), Store()), CheckNames.Permutation);

        ValidationResult restore = Run(Reorder(UsbNumber, WindowsOnPartition1) with { RestoredFrom = RestoreFolder }, Store());
        AssertCheckPassed(restore, CheckNames.Permutation);
        AssertPlan(restore);
    }

    // ---- V7: the new order differs from the current one ------------------------------------------

    [Fact]
    public void V7_UnchangedOrder_IsBlockedByNoChange()
    {
        AssertBlocked(Run(Reorder(WindowsOnPartition1, UsbNumber), Store()), CheckNames.NoChange, "The order is unchanged; nothing to write.");
    }

    [Fact]
    public void V7_UnchangedOrder_WithNoChangeAllowed_IsAPlanWithAWarning()
    {
        ReorderRequest drill = new(ChangeRequest.CliHost, new ushort[] { WindowsOnPartition1, UsbNumber }, AllowNoChange: true);

        ValidationResult result = Run(drill, Store());

        ChangePlan plan = AssertPlan(result);
        Assert.Equal(plan.Before.Hex, plan.After.Hex);
        CheckResult noChange = Assert.Single(result.Checks, check => check.Name == CheckNames.NoChange);
        Assert.True(noChange.Passed);
        Assert.True(noChange.IsWarning);
        Assert.Equal(CheckSentences.OrderUnchangedAllowed, noChange.Reason);
        Assert.Contains(CheckSentences.OrderUnchangedAllowed, result.Warnings);
    }

    [Fact]
    public void V7_TheFirstWriteDrillOnTheUnit_IsTheIdentityOfItsOneEntryList()
    {
        ReorderRequest drill = new(ChangeRequest.CliHost, new ushort[] { WindowsOnPartition1 }, AllowNoChange: true);

        ChangePlan plan = AssertPlan(Run(drill, StoreWithOrder(WindowsOnPartition1)));

        Assert.Equal("0200", plan.Before.Hex);
        Assert.Equal("0200", plan.After.Hex);
    }

    [Fact]
    public void V7_ChangedOrder_PassesNoChangeWithoutAWarning()
    {
        ValidationResult result = Run(Reorder(UsbNumber, WindowsOnPartition1), Store(), BitLockerState.Off);

        CheckResult noChange = Assert.Single(result.Checks, check => check.Name == CheckNames.NoChange);
        Assert.True(noChange.Passed);
        Assert.False(noChange.IsWarning);
        Assert.Empty(result.Warnings);
    }

    // ---- V8: the proposed size follows the operation; the bytes come from the codec ---------------

    [Fact]
    public void V8_Reorder_KeepsTheByteCount_AndEncodesTheEntryNumbers()
    {
        ChangePlan plan = AssertPlan(Run(Reorder(UsbNumber, WindowsOnPartition2, WindowsOnPartition1), StoreWithOrder(WindowsOnPartition1, UsbNumber, WindowsOnPartition2), BitLockerState.Off));

        Assert.Equal(plan.Before.Size, plan.After.Size);
        Assert.Equal(6, plan.After.Size);
        Assert.Equal(BootListCodec.Encode(new ushort[] { UsbNumber, WindowsOnPartition2, WindowsOnPartition1 }), plan.After.Bytes);
        Assert.Equal("030000000200", plan.After.Hex);
        Assert.Equal(7u, plan.After.Attributes);
    }

    [Fact]
    public void V8_OneTimeBoot_IsTwoBytes_AndAClearIsNothing()
    {
        ChangePlan next = AssertPlan(Run(Next(WindowsOnPartition1), Store()));
        ChangePlan clear = AssertPlan(Run(Clear(), Store(Var("BootNext", U16(UsbNumber)))));

        Assert.True(next.After.Present);
        Assert.Equal(2, next.After.Size);
        Assert.Equal("0200", next.After.Hex);
        Assert.Equal(7u, next.After.Attributes);
        Assert.False(clear.After.Present);
        Assert.Equal(0, clear.After.Size);
        Assert.Equal(7u, clear.After.Attributes);
    }

    [Fact]
    public void V8_TheTripwireSentence_NamesTheSizes()
    {
        Assert.Equal(
            "The new value of BootOrder came out at 2 bytes where 4 are required; nothing is written.",
            CheckSentences.Format(CheckSentences.ProposedSizeWrongFormat, "BootOrder", 2, 4));
    }

    // ---- V9: the attributes are exactly NV BS RT ---------------------------------------------------

    [Fact]
    public void V9_BootOrderWithoutRuntimeAccess_IsBlockedByAttributes()
    {
        VariableSet set = Store(Var("BootOrder", BootList(WindowsOnPartition1, UsbNumber), VariableAttributes.NonVolatile | VariableAttributes.BootServiceAccess));

        AssertBlocked(Run(Reorder(UsbNumber, WindowsOnPartition1), set), CheckNames.Attributes, "BootOrder carries attributes NV BS, not NV BS RT; it is not writable from Windows.");
    }

    [Fact]
    public void V9_BootOrderWithTimeBasedAuthentication_IsBlockedByAttributes_AndRecordsV10Too()
    {
        VariableSet set = Store(Var("BootOrder", BootList(WindowsOnPartition1, UsbNumber), NvBsRtTa));

        ValidationResult result = Run(Reorder(UsbNumber, WindowsOnPartition1), set);

        AssertBlocked(result, CheckNames.Attributes, "BootOrder carries attributes NV BS RT TA, not NV BS RT; it is not writable from Windows.");
        Assert.Contains(result.Checks, check => check.Name == CheckNames.AuthenticatedWrite && !check.Passed && check.Reason == "BootOrder carries an authenticated-write attribute (TA): only a signed update can change it.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void V9_BootNextWithOtherAttributes_IsBlockedByAttributes(bool clear)
    {
        VariableSet set = Store(Var("BootNext", U16(UsbNumber), BsRt));

        AssertBlocked(Run(clear ? Clear() : Next(WindowsOnPartition1), set), CheckNames.Attributes, "BootNext carries attributes BS RT, not NV BS RT; it is not writable from Windows.");
    }

    [Fact]
    public void V9_AbsentBootNext_HasNothingToCheck_AndIsCreatedWith7()
    {
        ValidationResult result = Run(Next(WindowsOnPartition1), Store());

        AssertCheckPassed(result, CheckNames.Attributes);
        Assert.False(result.Before!.Present);
        Assert.Equal(7u, AssertPlan(result).After.Attributes);
    }

    [Fact]
    public void V9_AttributesOf7_PassAndArePassedBackUnchanged()
    {
        ChangePlan plan = AssertPlan(Run(Reorder(UsbNumber, WindowsOnPartition1), Store()));

        Assert.Equal(7u, plan.Before.Attributes);
        Assert.Equal(plan.Before.Attributes, plan.After.Attributes);
    }

    // ---- V10: no authenticated-write bit ----------------------------------------------------------

    [Theory]
    [InlineData("PK", "TA")]
    [InlineData("SecureBoot", "AW")]
    public void V10_SignedGlobalVariable_IsNotWhitelisted_AndReportedAsAuthenticatedWrite(string name, string flags)
    {
        ValidationResult result = Run(Check(name), Store());

        AssertBlocked(result, CheckNames.Whitelist, $"{name} is not on the whitelist of this application; only BootOrder (reorder) and BootNext can be changed.");
        Assert.Contains(result.Checks, check => check.Name == CheckNames.AuthenticatedWrite && !check.Passed && check.Reason == $"{name} carries an authenticated-write attribute ({flags}): only a signed update can change it.");
    }

    [Fact]
    public void V10_ImageSecurityDatabase_IsReportedAsAuthenticatedWrite()
    {
        ValidationResult result = Run(Check("db", KnownGuids.EfiImageSecurityDatabase), Store());

        AssertBlocked(result, CheckNames.Whitelist, "db is not on the whitelist of this application; only BootOrder (reorder) and BootNext can be changed.");
        Assert.Contains(result.Checks, check => check.Name == CheckNames.AuthenticatedWrite && !check.Passed && check.Reason.Contains("(TA)", StringComparison.Ordinal));
    }

    [Fact]
    public void V10_UnsignedVariableOffTheWhitelist_PassesAuthenticatedWrite_AndIsBlockedByTheWhitelistAlone()
    {
        ValidationResult result = Run(Check("Timeout"), Store());

        AssertBlocked(result, CheckNames.Whitelist, "Timeout is not on the whitelist of this application; only BootOrder (reorder) and BootNext can be changed.");
        AssertCheckPassed(result, CheckNames.AuthenticatedWrite);
    }

    [Theory]
    [InlineData(VariableAttributes.AuthenticatedWriteAccess, "AW")]
    [InlineData(VariableAttributes.EnhancedAuthenticatedAccess, "EA")]
    public void V10_WhitelistedVariableWithAnAuthenticatedBit_IsRefused(VariableAttributes bit, string flags)
    {
        VariableSet set = Store(Var("BootNext", U16(UsbNumber), NvBsRt | bit));

        ValidationResult result = Run(Next(WindowsOnPartition1), set);

        Assert.Equal(CheckNames.Attributes, result.BlockedBy);
        Assert.Contains(result.Checks, check => check.Name == CheckNames.AuthenticatedWrite && !check.Passed && check.Reason == $"BootNext carries an authenticated-write attribute ({flags}): only a signed update can change it.");
        AssertBlocked(result, CheckNames.Attributes, result.BlockedReason!);
    }

    [Fact]
    public void V10_PlainBootOrder_PassesAuthenticatedWrite_InThePreflight()
    {
        ValidationResult result = Run(Check("BootOrder"), Store());

        AssertCheckPassed(result, CheckNames.AuthenticatedWrite);
        Assert.True(result.Passed);
        Assert.False(result.IsPlan);
        Assert.Null(result.After);
        Assert.Equal(new[] { CheckNames.Enumeration, CheckNames.Whitelist, CheckNames.Attributes, CheckNames.AuthenticatedWrite, CheckNames.Format, CheckNames.CurrentListIntact }, result.Checks.Select(check => check.Name));
    }

    // ---- V11: the one-time target is active --------------------------------------------------------

    [Fact]
    public void V11_InactiveTarget_IsBlockedByBootNextTarget()
    {
        AssertBlocked(Run(Next(InactiveNumber), Store()), CheckNames.BootNextTarget, "Boot0006 is inactive; the boot manager would not start it.");
    }

    [Fact]
    public void V11_ActiveTarget_PassesBootNextTarget()
    {
        ValidationResult result = Run(Next(WindowsOnPartition1), Store());

        CheckResult target = Assert.Single(result.Checks, check => check.Name == CheckNames.BootNextTarget);
        Assert.True(target.Passed);
        Assert.False(target.IsWarning);
    }

    // ---- V12: the one-time target is category Boot; hidden is a warning ---------------------------

    [Fact]
    public void V12_ApplicationTarget_IsBlockedByBootNextTarget()
    {
        AssertBlocked(Run(Next(AppNumber), Store()), CheckNames.BootNextTarget, "Boot0004 is an application entry, not a boot entry.");
    }

    [Fact]
    public void V12_HiddenTarget_PassesWithAWarning()
    {
        ValidationResult result = Run(Next(HiddenNumber), Store());

        AssertPlan(result);
        CheckResult target = Assert.Single(result.Checks, check => check.Name == CheckNames.BootNextTarget);
        Assert.True(target.IsWarning);
        Assert.Equal("Boot0005 is hidden from the firmware's menu; it can still be started once.", target.Reason);
        Assert.Contains("Boot0005 is hidden from the firmware's menu; it can still be started once.", result.Warnings);
    }

    // ---- V13: no other Windows boot path while protectors may exist --------------------------------

    [Theory]
    [MemberData(nameof(ProtectedStates))]
    public void V13_OneTimeBootToTheUnitsSecondWindowsBootManager_IsBlocked(BitLockerState state)
    {
        ValidationResult result = Run(Next(WindowsOnPartition2), Store(), state);

        AssertBlocked(result, CheckNames.OtherWindowsPath, OtherWindowsPath("Boot0000"));
        Assert.Equal(new[] { "HD", "File" }, result.DeviceNodeTypes["Boot0000"]);
        Assert.Equal(new[] { "HD", "File" }, result.DeviceNodeTypes["Boot0002"]);
    }

    [Theory]
    [MemberData(nameof(ProtectedStates))]
    public void V13_ReorderPuttingTheSecondWindowsBootManagerFirst_IsBlocked(BitLockerState state)
    {
        VariableSet set = StoreWithOrder(WindowsOnPartition1, UsbNumber, WindowsOnPartition2);

        AssertBlocked(Run(Reorder(WindowsOnPartition2, WindowsOnPartition1, UsbNumber), set, state), CheckNames.OtherWindowsPath, OtherWindowsPath("Boot0000"));
    }

    [Theory]
    [MemberData(nameof(ProtectedStates))]
    public void V13_LooksAtEveryEntryAheadOfBootCurrent_NotOnlyTheFirst(BitLockerState state)
    {
        VariableSet set = StoreWithOrder(WindowsOnPartition1, UsbNumber, WindowsCopyNumber, WindowsOnPartition2);

        ValidationResult result = Run(Reorder(UsbNumber, WindowsCopyNumber, WindowsOnPartition2, WindowsOnPartition1), set, state);

        AssertBlocked(result, CheckNames.OtherWindowsPath, OtherWindowsPath("Boot0000"));
        Assert.Contains("Boot0003", result.DeviceNodeTypes.Keys);
        Assert.Contains("Boot0009", result.DeviceNodeTypes.Keys);
    }

    [Fact]
    public void V13_TheDesignsExample_UsbFirstThenTheSecondWindowsBootManager_IsBlocked()
    {
        VariableSet set = StoreWithOrder(WindowsOnPartition1, UsbNumber, WindowsOnPartition2);

        AssertBlocked(Run(Reorder(UsbNumber, WindowsOnPartition2, WindowsOnPartition1), set), CheckNames.OtherWindowsPath, OtherWindowsPath("Boot0000"));
    }

    [Fact]
    public void V13_WhenBootCurrentsEntryIsNotInTheList_EveryEntryIsAhead()
    {
        VariableSet set = StoreWithOrder(UsbNumber, WindowsOnPartition2);

        AssertBlocked(Run(Reorder(WindowsOnPartition2, UsbNumber), set), CheckNames.OtherWindowsPath, OtherWindowsPath("Boot0000"));
    }

    [Theory]
    [InlineData(FallbackNumber, "Boot0007")]
    [InlineData(WindowsWithoutPartitionNumber, "Boot000B")]
    [InlineData(PartitionWithoutFileNumber, "Boot000C")]
    public void V13_OtherWindowsBootFiles_AreBlocked(ushort target, string name)
    {
        AssertBlocked(Run(Next(target), Store()), CheckNames.OtherWindowsPath, OtherWindowsPath(name));
    }

    [Theory]
    [InlineData(@"\EFI\MICROSOFT\BOOT\BOOTMGFW.EFI")]
    [InlineData(@"/EFI/Microsoft/Boot/bootmgfw.efi")]
    [InlineData(@"EFI\Microsoft\Boot\bootmgfw.efi")]
    [InlineData(@"\efi\boot\BOOTX64.efi")]
    public void V13_ComparesTheFileIgnoringCaseAndSpelling(string file)
    {
        byte[] other = BuildLoadOption(1, WindowsBootManagerDescription, PathOf(HardDriveGpt(2, 0x96800, 0x32000, UnitSecondPartitionSignature), FilePath(file)));

        AssertBlocked(Run(Next(WindowsOnPartition2), Store(Var("Boot0000", other))), CheckNames.OtherWindowsPath, OtherWindowsPath("Boot0000"));
    }

    [Fact]
    public void V13_SameMbrDiskButAnotherPartition_IsBlocked_AndTheSamePartitionIsNot()
    {
        byte[] current = BuildLoadOption(1, WindowsBootManagerDescription, PathOf(HardDriveMbr(1, 0x800, 0x32000, 0x1234ABCD), FilePath(@"\EFI\Microsoft\Boot\bootmgfw.efi")));
        byte[] otherPartition = BuildLoadOption(1, "Windows Boot Manager", PathOf(HardDriveMbr(2, 0x40000, 0x32000, 0x1234ABCD), FilePath(@"\EFI\Microsoft\Boot\bootmgfw.efi")));
        byte[] samePartition = BuildLoadOption(1, "Windows Boot Manager (copy)", PathOf(HardDriveMbr(1, 0x800, 0x32000, 0x1234ABCD), FilePath(@"\EFI\Boot\bootx64.efi")));
        VariableSet set = Store(Var("Boot0002", current), Var("Boot0000", otherPartition), Var("Boot0009", samePartition));

        AssertBlocked(Run(Next(WindowsOnPartition2), set), CheckNames.OtherWindowsPath, OtherWindowsPath("Boot0000"));
        AssertPlan(Run(Next(WindowsCopyNumber), set));
    }

    [Fact]
    public void V13_WithoutASignatureToCompare_EveryOtherWindowsEntryIsBlocked()
    {
        byte[] noSignature = HardDrive(1, 0x800, 0x32000, new byte[16], 2, 0);
        byte[] current = BuildLoadOption(1, WindowsBootManagerDescription, PathOf(noSignature, FilePath(@"\EFI\Microsoft\Boot\bootmgfw.efi")));
        byte[] copy = BuildLoadOption(1, "Windows Boot Manager (copy)", PathOf(noSignature, FilePath(@"\EFI\Microsoft\Boot\bootmgfw.efi")));
        VariableSet set = Store(Var("Boot0002", current), Var("Boot0009", copy));

        AssertBlocked(Run(Next(WindowsCopyNumber), set), CheckNames.OtherWindowsPath, OtherWindowsPath("Boot0009"));
        AssertPlan(Run(Next(WindowsOnPartition1), set));
    }

    [Theory]
    [InlineData(WindowsOnPartition1)]
    [InlineData(WindowsCopyNumber)]
    [InlineData(FallbackSamePartitionNumber)]
    [InlineData(UsbNumber)]
    [InlineData(NetworkNumber)]
    [InlineData(HiddenNumber)]
    public void V13_SamePartitionOrNotAWindowsBootFile_PassesOtherWindowsPath(ushort target)
    {
        ValidationResult result = Run(Next(target), Store());

        AssertCheckPassed(result, CheckNames.OtherWindowsPath);
        AssertPlan(result);
    }

    [Fact]
    public void V13_SecondWindowsBootManagerBehindBootCurrent_PassesOtherWindowsPath()
    {
        VariableSet set = StoreWithOrder(WindowsOnPartition1, UsbNumber, WindowsOnPartition2);

        ValidationResult result = Run(Reorder(WindowsOnPartition1, WindowsOnPartition2, UsbNumber), set);

        AssertCheckPassed(result, CheckNames.OtherWindowsPath);
        AssertPlan(result);
    }

    [Fact]
    public void V13_DoesNotApplyWhenBitLockerIsOff()
    {
        ValidationResult result = Run(Next(WindowsOnPartition2), Store(), BitLockerState.Off);

        AssertPlan(result);
        Assert.DoesNotContain(result.Checks, check => check.Name == CheckNames.OtherWindowsPath);
        Assert.Empty(result.DeviceNodeTypes);
    }

    /// <summary>
    /// F7 through BootNext: the firmware starts a one-time boot before any BootOrder entry, so a
    /// reorder that keeps Boot0002 first still sends the next start through the unit's second
    /// Windows Boot Manager when BootNext names Boot0000, and a one-restart suspend would be sealed
    /// to that path. A reorder is refused while a one-time boot is set, whatever it names.
    /// </summary>
    [Theory]
    [MemberData(nameof(ProtectedStates))]
    public void V13_AReorderWhileAOneTimeBootToTheSecondWindowsBootManagerIsSet_IsBlocked(BitLockerState state)
    {
        VariableSet set = Store(Var("BootOrder", BootList(WindowsOnPartition1, UsbNumber, WindowsCopyNumber)), Var("BootNext", U16(WindowsOnPartition2)));

        ValidationResult result = Run(Reorder(WindowsOnPartition1, WindowsCopyNumber, UsbNumber), set, state);

        AssertBlocked(result, CheckNames.OtherWindowsPath, PendingOneTimeBoot("Boot0000"));
        VariableDependency bootNext = Assert.Single(result.Dependencies, dependency => dependency.Name == "BootNext");
        Assert.True(bootNext.Image.SameBytesAs(U16(WindowsOnPartition2)));
    }

    [Theory]
    [InlineData(UsbNumber, "Boot0003")]
    [InlineData(WindowsOnPartition1, "Boot0002")]
    public void V13_AReorderWhileAnyOneTimeBootIsSet_IsBlocked_ForTheOrderIsNotTheNextStartsPath(ushort target, string name)
    {
        // A one-time boot to a stick (V17's case, F15) or to BootCurrent's own entry is refused too:
        // the rule does not judge the target, the one-time boot is cleared first.
        VariableSet set = Store(Var("BootNext", U16(target)));

        AssertBlocked(Run(Reorder(UsbNumber, WindowsOnPartition1), set), CheckNames.OtherWindowsPath, PendingOneTimeBoot(name));
    }

    [Fact]
    public void V13_AReorderWhileABootNextOfAnotherSizeIsSet_IsBlocked_NamingBootNext()
    {
        VariableSet set = Store(Var("BootNext", BootList(UsbNumber, WindowsOnPartition1)));

        AssertBlocked(Run(Reorder(UsbNumber, WindowsOnPartition1), set, BitLockerState.Unknown), CheckNames.OtherWindowsPath, PendingOneTimeBoot("BootNext"));
    }

    [Fact]
    public void V13_APendingOneTimeBoot_DoesNotBlockAReorderWhenBitLockerIsOff_NorAOneTimeBootOrAClear()
    {
        VariableSet set = Store(Var("BootNext", U16(WindowsOnPartition2)));

        ValidationResult reorder = Run(Reorder(UsbNumber, WindowsOnPartition1), set, BitLockerState.Off);
        ValidationResult oneTime = Run(Next(WindowsOnPartition1), set);
        ValidationResult clear = Run(Clear(), set);

        AssertPlan(reorder);
        Assert.DoesNotContain(reorder.Dependencies, dependency => dependency.Name == "BootNext");
        AssertPlan(oneTime);
        AssertCheckPassed(oneTime, CheckNames.OtherWindowsPath);
        AssertPlan(clear);
    }

    [Fact]
    public void V13_AReorderWithNoOneTimeBootSet_ReadsBootNextAsAbsent_SoTheDriftReadsCompareIt()
    {
        ValidationResult result = Run(Reorder(UsbNumber, WindowsOnPartition1), Store());

        AssertPlan(result);
        AssertCheckPassed(result, CheckNames.OtherWindowsPath);
        VariableDependency bootNext = Assert.Single(result.Dependencies, dependency => dependency.Name == "BootNext");
        Assert.False(bootNext.Image.Present);
        Assert.Equal(Global, bootNext.Guid);
    }

    // ---- V14: a clear needs a BootNext ---------------------------------------------------------------

    [Fact]
    public void V14_ClearWithoutBootNext_IsBlockedByBootNextTarget()
    {
        AssertBlocked(Run(Clear(), Store()), CheckNames.BootNextTarget, "No one-time boot is set; nothing to clear.");
    }

    [Fact]
    public void V14_ClearWithBootNext_PassesBootNextTarget_BeforeTheAttributeRules()
    {
        ValidationResult result = Run(Clear(), Store(Var("BootNext", U16(UsbNumber))));

        AssertPlan(result);
        Assert.Equal(new[] { CheckNames.Enumeration, CheckNames.Whitelist, CheckNames.BootNextTarget, CheckNames.Attributes, CheckNames.AuthenticatedWrite, CheckNames.Format }, result.Checks.Select(check => check.Name));
        Assert.Equal("Boot0003", Assert.Single(result.Diff).Before);
        Assert.Null(Assert.Single(result.Diff).After);
    }

    // ---- V15: a complete enumeration -----------------------------------------------------------------

    [Fact]
    public void V15_KnownNamesFallback_IsBlockedByEnumeration()
    {
        VariableSet set = Store();

        AssertBlocked(
            ChangeValidator.Validate(Reorder(UsbNumber, WindowsOnPartition1), set, Fallback(set), BitLockerState.On),
            CheckNames.Enumeration,
            "The variable list could not be read completely (KnownNamesFallback: NtEnumerateSystemEnvironmentValuesEx failed); a change is not safe on an incomplete list.");
    }

    [Fact]
    public void V15_AWarningOrAnEarlyStop_IsBlockedByEnumeration()
    {
        VariableSet set = Store();

        AssertBlocked(
            ChangeValidator.Validate(Next(WindowsOnPartition1), set, WithWarning(set), BitLockerState.On),
            CheckNames.Enumeration,
            "The variable list could not be read completely (NtEnumerate, 1 warning); a change is not safe on an incomplete list.");
        AssertBlocked(
            ChangeValidator.Validate(Clear(), set, StoppedEarly(set), BitLockerState.On),
            CheckNames.Enumeration,
            "The variable list could not be read completely (NtEnumerate: the buffer ended inside an entry); a change is not safe on an incomplete list.");
        AssertBlocked(
            ChangeValidator.Validate(Check("BootOrder"), set, new EnumerationResult(set.All, EnumerationMethod.None, 0, null, Array.Empty<string>()), BitLockerState.On),
            CheckNames.Enumeration,
            "The variable list could not be read completely (None); a change is not safe on an incomplete list.");
    }

    [Fact]
    public void V15_CompleteEnumeration_PassesEnumeration()
    {
        VariableSet set = Store();

        ValidationResult native = ChangeValidator.Validate(Reorder(UsbNumber, WindowsOnPartition1), set, NtComplete(set), BitLockerState.On);

        Assert.Equal(CheckNames.Enumeration, native.Checks[0].Name);
        Assert.True(native.Checks[0].Passed);
        AssertPlan(native);
    }

    [Fact]
    public void V15_AnInMemorySet_ABaselineFolderOrAStoreThatIsNotTheFirmware_IsBlockedByEnumeration()
    {
        // The offline view and the in-memory store report InMemory with no error and no warning:
        // complete as a list, but not read from the firmware, so no plan is made over them.
        VariableSet set = Store();
        const string Reason = "The variable list could not be read completely (InMemory); a change is not safe on an incomplete list.";

        AssertBlocked(ChangeValidator.Validate(Reorder(UsbNumber, WindowsOnPartition1), set, InMemory(set), BitLockerState.On), CheckNames.Enumeration, Reason);
        AssertBlocked(ChangeValidator.Validate(Next(WindowsOnPartition1), set, InMemory(set), BitLockerState.Off), CheckNames.Enumeration, Reason);
        AssertBlocked(ChangeValidator.Validate(Check("BootOrder"), set, InMemory(set), BitLockerState.Off), CheckNames.Enumeration, Reason);
        Assert.False(ChangeValidator.IsCompleteEnumeration(InMemory(set)));
        Assert.True(ChangeValidator.IsCompleteEnumeration(NtComplete(set)));
    }

    [Fact]
    public void V15_TwoWarnings_AreCountedInTheSentence()
    {
        VariableSet set = Store();
        EnumerationResult enumeration = new(set.All, EnumerationMethod.NtEnumerate, set.Count, "stopped", new[] { "one", "two" });

        AssertBlocked(
            ChangeValidator.Validate(Clear(), set, enumeration, BitLockerState.On),
            CheckNames.Enumeration,
            "The variable list could not be read completely (NtEnumerate: stopped, 2 warnings); a change is not safe on an incomplete list.");
    }

    // ---- V16: BootCurrent names a fixed-disk entry ----------------------------------------------------

    public static TheoryData<string, BitLockerState, bool> UnusableBootCurrents()
    {
        TheoryData<string, BitLockerState, bool> data = new();
        foreach (string variant in new[] { "absent", "usb", "network", "fourBytes", "oneByte", "missingEntry", "malformedEntry" })
        {
            foreach (BitLockerState state in new[] { BitLockerState.On, BitLockerState.Encrypting, BitLockerState.Suspended, BitLockerState.Unknown })
            {
                data.Add(variant, state, false);
                data.Add(variant, state, true);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(UnusableBootCurrents))]
    public void V16_UnusableBootCurrent_IsBlockedByBootCurrent(string variant, BitLockerState state, bool reorder)
    {
        VariableSet set = variant switch
        {
            "absent" => Store().Without("BootCurrent"),
            "usb" => Store(Var("BootCurrent", U16(UsbNumber), BsRt)),
            "network" => Store(Var("BootCurrent", U16(NetworkNumber), BsRt)),
            "fourBytes" => Store(Var("BootCurrent", BootList(WindowsOnPartition1, UsbNumber), BsRt)),
            "oneByte" => Store(Var("BootCurrent", new byte[] { 2 }, BsRt)),
            "missingEntry" => Store(Var("BootCurrent", U16(MissingNumber), BsRt)),
            _ => Store(Var("BootCurrent", U16(MalformedNumber), BsRt)),
        };
        ChangeRequest request = reorder ? Reorder(UsbNumber, WindowsOnPartition1) : Next(WindowsOnPartition1);

        AssertBlocked(Run(request, set, state), CheckNames.BootCurrent, "The current boot entry (BootCurrent) could not be read or names no fixed disk, so the other-Windows-path check cannot run. Nothing was written.");
    }

    [Fact]
    public void V16_FixedDiskBootCurrent_PassesBootCurrent()
    {
        AssertCheckPassed(Run(Next(WindowsOnPartition1), Store()), CheckNames.BootCurrent);
    }

    [Fact]
    public void V16_DoesNotApplyWhenBitLockerIsOff_OrToAClear()
    {
        VariableSet withoutBootCurrent = Store(Var("BootNext", U16(UsbNumber))).Without("BootCurrent");

        AssertPlan(Run(Next(WindowsOnPartition1), withoutBootCurrent, BitLockerState.Off));
        AssertPlan(Run(Reorder(UsbNumber, WindowsOnPartition1), withoutBootCurrent, BitLockerState.Off));
        ValidationResult clear = Run(Clear(), withoutBootCurrent, BitLockerState.On);
        AssertPlan(clear);
        Assert.DoesNotContain(clear.Checks, check => check.Name == CheckNames.BootCurrent);
    }

    // ---- V17: a warning for an entry without a fixed-disk node ahead of BootCurrent's -------------------

    [Theory]
    [MemberData(nameof(ProtectedStates))]
    public void V17_UsbOneTimeTarget_PassesWithTheWarningAndTheNodeTypes(BitLockerState state)
    {
        ValidationResult result = Run(Next(UsbNumber), Store(), state);

        AssertPlan(result);
        CheckResult removable = Assert.Single(result.Checks, check => check.Name == CheckNames.RemovableAhead);
        Assert.True(removable.Passed);
        Assert.True(removable.IsWarning);
        Assert.Equal(RemovableAhead("Boot0003"), removable.Reason);
        Assert.Equal(RemovableAhead("Boot0003"), Assert.Single(result.Warnings));
        Assert.Equal(new[] { "PciRoot", "Pci", "USB" }, result.DeviceNodeTypes["Boot0003"]);
    }

    [Fact]
    public void V17_EveryRemovableEntryAheadOfBootCurrent_IsWarned()
    {
        VariableSet set = StoreWithOrder(WindowsOnPartition1, UsbNumber, NetworkNumber);

        ValidationResult result = Run(Reorder(UsbNumber, NetworkNumber, WindowsOnPartition1), set);

        AssertPlan(result);
        Assert.Equal(new[] { RemovableAhead("Boot0003"), RemovableAhead("Boot000D") }, result.Warnings);
        Assert.Equal(new[] { "PciRoot", "Pci", "MAC", "IPv4" }, result.DeviceNodeTypes["Boot000D"]);
    }

    [Theory]
    [MemberData(nameof(ProtectedStates))]
    public void V17_AStickWrittenWithAFullPathThatNamesAPartition_IsWarnedToo(BitLockerState state)
    {
        // Pci/USB/HD/File: the stick's own partition is named, so V16 would not tell it from a
        // fixed disk by the hard drive node alone; its USB node makes it removable for V17. Its
        // loader is not a Windows boot file, so V13 has nothing to say about it.
        byte[] stick = BuildLoadOption(1, "UEFI: stick (full path)", PathOf(PciRoot(0), Pci(0x14, 0), Usb(3, 0), HardDriveGpt(1, 0x800, 0x100000, ThirdPartitionSignature), FilePath(@"\EFI\tools\memtest.efi")));
        VariableSet set = Store(Var("Boot000E", stick), Var("BootOrder", BootList(WindowsOnPartition1, 0x000E)));

        ValidationResult oneTime = Run(Next(0x000E), set, state);
        ValidationResult ahead = Run(Reorder(0x000E, WindowsOnPartition1), set, state);

        AssertPlan(oneTime);
        AssertPlan(ahead);
        Assert.Equal(RemovableAhead("Boot000E"), Assert.Single(oneTime.Warnings));
        Assert.Equal(RemovableAhead("Boot000E"), Assert.Single(ahead.Warnings));
        Assert.Equal(new[] { "PciRoot", "Pci", "USB", "HD", "File" }, ahead.DeviceNodeTypes["Boot000E"]);
    }

    [Fact]
    public void V17_RemovableEntryBehindBootCurrent_IsNotWarned()
    {
        VariableSet set = StoreWithOrder(WindowsOnPartition1, UsbNumber, WindowsCopyNumber);

        ValidationResult result = Run(Reorder(WindowsOnPartition1, WindowsCopyNumber, UsbNumber), set);

        AssertPlan(result);
        CheckResult removable = Assert.Single(result.Checks, check => check.Name == CheckNames.RemovableAhead);
        Assert.False(removable.IsWarning);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void V17_DoesNotApplyWhenBitLockerIsOff()
    {
        ValidationResult result = Run(Next(UsbNumber), Store(), BitLockerState.Off);

        AssertPlan(result);
        Assert.DoesNotContain(result.Checks, check => check.Name == CheckNames.RemovableAhead);
        Assert.Empty(result.Warnings);
    }

    // ---- the run as a whole ---------------------------------------------------------------------------

    [Fact]
    public void Reorder_RunsTheChecksInStageOrder_AndDecodesTheListsByEntryName()
    {
        ValidationResult result = Run(Reorder(UsbNumber, WindowsOnPartition1), Store());

        Assert.Equal(
            new[]
            {
                CheckNames.Enumeration, CheckNames.Whitelist, CheckNames.Attributes, CheckNames.AuthenticatedWrite, CheckNames.Format,
                CheckNames.CurrentListIntact, CheckNames.EntriesExist, CheckNames.Permutation, CheckNames.NoChange,
                CheckNames.BootCurrent, CheckNames.OtherWindowsPath, CheckNames.RemovableAhead,
            },
            result.Checks.Select(check => check.Name));
        Assert.Equal(ChangeOperation.Reorder, result.Operation);
        Assert.Same(Whitelist.BootOrder, result.Variable);
        Assert.Equal(new[] { "Boot0002", "Boot0003" }, result.BeforeEntries.Select(line => line.Name));
        Assert.Equal(new[] { "Boot0003", "Boot0002" }, result.AfterEntries.Select(line => line.Name));
        Assert.Equal(new[] { WindowsBootManagerDescription, UsbStickDescription }, result.BeforeEntries.Select(line => line.Description));
        Assert.Equal("active, category Boot", result.BeforeEntries[0].Flags);
        Assert.Equal(new[] { 1, 2 }, result.Diff.Select(row => row.Position));
        Assert.All(result.Diff, row => Assert.True(row.Changed));
        Assert.Equal("Boot0002", result.Diff[0].Before);
        Assert.Equal("Boot0003", result.Diff[0].After);
        Assert.Equal(UsbStickDescription, result.Diff[0].Description);
    }

    [Fact]
    public void OneTimeBoot_RunsTheChecksInStageOrder()
    {
        ValidationResult result = Run(Next(WindowsOnPartition1), Store());

        Assert.Equal(
            new[]
            {
                CheckNames.Enumeration, CheckNames.Whitelist, CheckNames.Attributes, CheckNames.AuthenticatedWrite, CheckNames.Format,
                CheckNames.CurrentListIntact, CheckNames.EntriesExist, CheckNames.BootNextTarget, CheckNames.BootCurrent, CheckNames.OtherWindowsPath,
                CheckNames.RemovableAhead,
            },
            result.Checks.Select(check => check.Name));
        DiffRow row = Assert.Single(result.Diff);
        Assert.Null(row.Before);
        Assert.Equal("Boot0002", row.After);
    }

    [Fact]
    public void EveryVariableTheRulesRead_IsADependency_WithTheValueTheRulesSaw()
    {
        // A reorder is refused while a one-time boot is set (V13), so it runs over the set without one.
        VariableSet set = Store(Var("BootNext", U16(UsbNumber)));
        VariableSet withoutOneTimeBoot = Store();

        ValidationResult reorder = Run(Reorder(UsbNumber, WindowsOnPartition1), withoutOneTimeBoot);
        ValidationResult oneTime = Run(Next(WindowsOnPartition1), set);
        ValidationResult clear = Run(Clear(), set);

        // The target is compared by the drift reads on its own; everything else a rule read is here,
        // a variable found absent included (BootNext for a reorder).
        string[] reorderNames = reorder.Dependencies.Select(dependency => dependency.Name).ToArray();
        Assert.DoesNotContain("BootOrder", reorderNames);
        Assert.Contains("Boot0002", reorderNames);
        Assert.Contains("Boot0003", reorderNames);
        Assert.Contains("BootCurrent", reorderNames);
        Assert.Contains("BootNext", reorderNames);
        Assert.Equal(reorderNames.Distinct(), reorderNames);

        string[] oneTimeNames = oneTime.Dependencies.Select(dependency => dependency.Name).ToArray();
        Assert.DoesNotContain("BootNext", oneTimeNames);
        Assert.Contains("BootOrder", oneTimeNames);
        Assert.Contains("Boot0002", oneTimeNames);
        Assert.Contains("BootCurrent", oneTimeNames);

        // A clear reads only the entry the current BootNext names, for its line in the dialog.
        Assert.Equal(new[] { "Boot0003" }, clear.Dependencies.Select(dependency => dependency.Name));
        AssertTheValuesRead(reorder, withoutOneTimeBoot);
        AssertTheValuesRead(oneTime, set);
        AssertTheValuesRead(clear, set);
        Assert.Equal(reorder.Dependencies, AssertPlan(reorder).Dependencies);
    }

    /// <summary>Each dependency holds what the set holds: the same bytes and attributes, or absent for a variable the set lacks.</summary>
    private static void AssertTheValuesRead(ValidationResult result, VariableSet set)
    {
        Assert.All(result.Dependencies, dependency =>
        {
            FirmwareVariable? read = set.Find(dependency.Name, dependency.Guid);
            if (!dependency.Image.Present)
            {
                Assert.Null(read);
                return;
            }

            Assert.NotNull(read);
            Assert.True(dependency.Image.SameBytesAs(read.Data), dependency.Name);
            Assert.Equal((uint)read.Attributes, dependency.Image.Attributes);
        });
    }

    [Fact]
    public void TheFirstFailedStageStopsTheRun()
    {
        ValidationResult result = Run(Reorder(WindowsOnPartition1, MissingNumber), Store());

        Assert.Equal(CheckNames.EntriesExist, result.Checks[result.Checks.Count - 1].Name);
        Assert.DoesNotContain(result.Checks, check => check.Name == CheckNames.Permutation);
        Assert.Single(result.Checks, check => !check.Passed);
    }

    [Fact]
    public void RequestedOrder_IsCopied_SoTheCallersListCannotChangeThePlan()
    {
        ushort[] order = { UsbNumber, WindowsOnPartition1 };
        ValidationResult result = Run(new ReorderRequest(ChangeRequest.CliHost, order), Store());
        order[0] = WindowsOnPartition2;

        ChangePlan plan = AssertPlan(result);
        Assert.Equal("03000200", plan.After.Hex);
        Assert.Equal(new[] { "Boot0003", "Boot0002" }, plan.AfterEntries.Select(line => line.Name));
    }

    [Fact]
    public void UnresolvedRestore_IsRefusedWithAnArgumentException()
    {
        Assert.Throws<ArgumentException>(() => ChangeValidator.Validate(new RestoreRequest(ChangeRequest.AppHost, RestoreFolder), Store(), Complete(Store()), BitLockerState.On));
    }

    [Fact]
    public void UnknownRequestType_IsRefusedWithAnArgumentException()
    {
        Assert.Throws<ArgumentException>(() => ChangeValidator.Validate(new UnknownRequest(), Store(), Complete(Store()), BitLockerState.On));
    }

    [Fact]
    public void NullArguments_Throw()
    {
        VariableSet set = Store();
        Assert.Throws<ArgumentNullException>(() => ChangeValidator.Validate(null!, set, Complete(set), BitLockerState.On));
        Assert.Throws<ArgumentNullException>(() => ChangeValidator.Validate(Clear(), null!, Complete(set), BitLockerState.On));
        Assert.Throws<ArgumentNullException>(() => ChangeValidator.Validate(Clear(), set, null!, BitLockerState.On));
    }

    [Fact]
    public void Validator_NeverThrows_ForAnyVariableContent()
    {
        Random random = new(7020);
        BitLockerState[] states = Enum.GetValues<BitLockerState>();
        for (int iteration = 0; iteration < 300; iteration++)
        {
            VariableSet set = Store(
                Var("BootOrder", RandomBytes(random, 12), (VariableAttributes)random.Next(0, 0x100)),
                Var("BootNext", RandomBytes(random, 4), (VariableAttributes)random.Next(0, 0x100)),
                Var("BootCurrent", RandomBytes(random, 4), BsRt),
                Var("Boot0002", RandomBytes(random, 120)),
                Var("Boot0003", RandomBytes(random, 120)));
            ChangeRequest[] requests =
            {
                Reorder((ushort)random.Next(0, 5), (ushort)random.Next(0, 5)),
                Next((ushort)random.Next(0, 5)),
                Clear(),
                Check("BootOrder"),
                Check("BootNext"),
            };
            foreach (ChangeRequest request in requests)
            {
                BitLockerState state = states[random.Next(states.Length)];
                Exception? exception = Record.Exception(() => ChangeValidator.Validate(request, set, Complete(set), state));
                Assert.Null(exception);
            }
        }

    }

    [Fact]
    public void Validator_HoldsNothingThatCanWrite()
    {
        Type[] forbidden = { typeof(IFirmwareVariableWriter), typeof(INativeFirmwareWriteApi), typeof(IBitLockerSuspender), typeof(WritePlan), typeof(IFirmwareVariableStore) };
        Type[] types = typeof(ChangeValidator).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).Append(typeof(ChangeValidator)).Append(typeof(ValidationResult)).ToArray();
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        List<string> found = new();
        foreach (Type type in types)
        {
            found.AddRange(type.GetFields(All).Where(field => forbidden.Contains(field.FieldType)).Select(field => $"{type.Name}.{field.Name}"));
            found.AddRange(type.GetProperties(All).Where(property => forbidden.Contains(property.PropertyType)).Select(property => $"{type.Name}.{property.Name}"));
            foreach (MethodBase method in type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All)))
            {
                found.AddRange(method.GetParameters().Where(parameter => forbidden.Contains(parameter.ParameterType)).Select(parameter => $"{type.Name}.{method.Name}({parameter.Name})"));
            }
        }

        Assert.True(found.Count == 0, "The validator must not be able to reach a writer, a suspender, a write plan or the store: " + string.Join(", ", found));
        Assert.Contains(typeof(ChangeValidator).GetNestedTypes(BindingFlags.NonPublic), type => type.Name == "Run");
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private static string OtherWindowsPath(string name) =>
        $"{name} may start Windows through a different boot path. A one-restart suspend would seal BitLocker to that path and the following normal start would ask for the recovery key. Not offered while BitLocker protectors exist.";

    private static string PendingOneTimeBoot(string name) =>
        $"A one-time boot ({name}) is set, and the firmware starts it at the next start before the boot order; a one-restart suspend would be used on that start. Clear the one-time boot first, then check the new order again.";

    private static string RemovableAhead(string name) =>
        $"If {name} is not present at the next start, the firmware goes on to the normal list in that start; with the suspend ticked BitLocker is then sealed to that start, and a later start may ask for the recovery key.";

    private static byte[] RandomBytes(Random random, int maximumLength)
    {
        byte[] bytes = new byte[random.Next(0, maximumLength + 1)];
        random.NextBytes(bytes);
        return bytes;
    }

    private static ValidationResult Run(ChangeRequest request, VariableSet set, BitLockerState state = BitLockerState.On)
    {
        return ChangeValidator.Validate(request, set, Complete(set), state);
    }

    private static void AssertCheckPassed(ValidationResult result, string check)
    {
        Assert.Contains(result.Checks, recorded => recorded.Name == check && recorded.Passed);
        Assert.DoesNotContain(result.Checks, recorded => recorded.Name == check && !recorded.Passed);
    }

    private static ChangePlan AssertPlan(ValidationResult result)
    {
        Assert.True(result.IsPlan, result.BlockedReason);
        Assert.NotNull(result.After);
        return new ChangePlan(result, Findings(), result.Checks, PlanTime);
    }

    /// <summary>
    /// A refusal: the named check failed with exactly this sentence and named the block, nothing
    /// was built to write, and no plan (and so no token and no commit) can be made from the result.
    /// </summary>
    private static void AssertBlocked(ValidationResult result, string check, string reason)
    {
        Assert.False(result.Passed);
        Assert.Equal(check, result.BlockedBy);
        Assert.Equal(reason, result.BlockedReason);
        Assert.Contains(result.Checks, recorded => recorded.Name == check && !recorded.Passed && recorded.Reason == reason);
        Assert.Null(result.After);
        Assert.Empty(result.AfterEntries);
        Assert.Empty(result.Diff);
        Assert.False(result.IsPlan);
        Assert.Throws<ArgumentException>(() => new ChangePlan(result, Findings(), result.Checks, PlanTime));
    }

    /// <summary>A request type the product does not have, to show the validator refuses what it does not know.</summary>
    private sealed record UnknownRequest() : ChangeRequest(ChangeRequest.CliHost);
}
