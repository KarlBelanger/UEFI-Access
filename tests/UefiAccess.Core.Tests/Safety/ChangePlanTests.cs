// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Cryptography;
using System.Text;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Variables;
using Xunit;
using static UefiAccess.Core.Tests.Fixtures.LoadOptionFixtures;
using static UefiAccess.Core.Tests.Safety.SafetyFixtures;

namespace UefiAccess.Core.Tests.Safety;

/// <summary>The dry-run result: the plan hash, the snapshot folder name, and the token that Confirm binds to the hash.</summary>
public sealed class ChangePlanTests
{
    private static ChangePlan Plan(ChangeRequest request, VariableSet? set = null, DateTimeOffset? createdAt = null)
    {
        VariableSet store = set ?? Store();
        ValidationResult validation = Validate(request, store);
        Assert.True(validation.IsPlan, validation.BlockedReason);
        return new ChangePlan(validation, Findings(), validation.Checks, createdAt ?? PlanTime);
    }

    [Fact]
    public void Reorder_CarriesTheImagesEntriesAndDiff()
    {
        ChangePlan plan = Plan(Reorder(UsbNumber, WindowsOnPartition1));

        Assert.Equal(ChangeOperation.Reorder, plan.Operation);
        Assert.Same(Whitelist.BootOrder, plan.Variable);
        Assert.Equal("02000300", plan.Before.Hex);
        Assert.Equal("03000200", plan.After.Hex);
        Assert.Equal(7u, plan.After.Attributes);
        Assert.Equal(new[] { "Boot0002", "Boot0003" }, plan.BeforeEntries.Select(e => e.Name));
        Assert.Equal(new[] { "Boot0003", "Boot0002" }, plan.AfterEntries.Select(e => e.Name));
        Assert.Equal(2, plan.Diff.Count);
        Assert.Null(plan.RestoredFrom);
        Assert.Equal(ChangeRequest.CliHost, plan.Request.Host);
    }

    [Fact]
    public void PlanHash_IsTheSha256OfTheFiveLineLayout()
    {
        ChangePlan plan = Plan(Reorder(UsbNumber, WindowsOnPartition1));

        string text = "Reorder\n8be4df61-93ca-11d2-aa0d-00e098032b8c:BootOrder\n02000300\n03000200\n7";
        string expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

        Assert.Equal(expected, plan.PlanHash);
        Assert.Equal(64, plan.PlanHash.Length);
        Assert.Equal(plan.PlanHash, plan.PlanHash.ToLowerInvariant());
    }

    [Fact]
    public void PlanHash_UsesAbsentForAnImageThatDoesNotExist()
    {
        ChangePlan next = Plan(Next(WindowsOnPartition1));
        ChangePlan clear = Plan(Clear(), Store(Var("BootNext", U16(WindowsOnPartition1))));

        Assert.Equal(Hash("SetBootNext\n8be4df61-93ca-11d2-aa0d-00e098032b8c:BootNext\nabsent\n0200\n7"), next.PlanHash);
        Assert.Equal(Hash("ClearBootNext\n8be4df61-93ca-11d2-aa0d-00e098032b8c:BootNext\n0200\nabsent\n7"), clear.PlanHash);
    }

    [Fact]
    public void PlanHash_IsTheSameForTheSameBytes_AndDiffersForOtherBytes()
    {
        VariableSet three = Store(Var("BootOrder", Concat(U16(WindowsOnPartition1), U16(UsbNumber), U16(WindowsOnPartition2))));

        ChangePlan first = Plan(Reorder(UsbNumber, WindowsOnPartition1, WindowsOnPartition2), three);
        ChangePlan again = Plan(Reorder(UsbNumber, WindowsOnPartition1, WindowsOnPartition2), three, PlanTime.AddMinutes(5));
        ChangePlan other = Plan(Reorder(WindowsOnPartition1, WindowsOnPartition2, UsbNumber), three);

        Assert.Equal(first.PlanHash, again.PlanHash);
        Assert.NotEqual(first.PlanHash, other.PlanHash);
    }

    [Fact]
    public void Confirm_ReturnsATokenBoundToThePlanHash()
    {
        ChangePlan plan = Plan(Reorder(UsbNumber, WindowsOnPartition1));
        DateTimeOffset before = DateTimeOffset.Now;

        ConfirmationToken token = plan.Confirm(BitLockerChoice.SuspendForOneRestart);

        Assert.Equal(plan.PlanHash, token.PlanHash);
        Assert.Equal(BitLockerChoice.SuspendForOneRestart, token.Choice);
        Assert.InRange(token.IssuedAt, before, DateTimeOffset.Now);
        Assert.True(token.Matches(plan));
    }

    [Fact]
    public void Token_DoesNotMatchAPlanForOtherBytes()
    {
        VariableSet three = Store(Var("BootOrder", Concat(U16(WindowsOnPartition1), U16(UsbNumber), U16(WindowsOnPartition2))));
        ChangePlan confirmed = Plan(Reorder(UsbNumber, WindowsOnPartition1, WindowsOnPartition2), three);
        ChangePlan other = Plan(Reorder(WindowsOnPartition1, WindowsOnPartition2, UsbNumber), three);

        ConfirmationToken token = confirmed.Confirm(BitLockerChoice.KeepProtection);

        Assert.False(token.Matches(other));
        Assert.True(token.Matches(confirmed));
    }

    [Fact]
    public void Confirm_IsTheOnlyPublicWayToATokenAndCarriesEachChoice()
    {
        Assert.Empty(typeof(ConfirmationToken).GetConstructors());

        ChangePlan plan = Plan(Reorder(UsbNumber, WindowsOnPartition1));
        Assert.Equal(BitLockerChoice.NotNeeded, plan.Confirm(BitLockerChoice.NotNeeded).Choice);
        Assert.Equal(BitLockerChoice.KeepProtection, plan.Confirm(BitLockerChoice.KeepProtection).Choice);
    }

    [Theory]
    [InlineData(ChangeOperation.Reorder, "20260930-141502-reorder")]
    [InlineData(ChangeOperation.SetBootNext, "20260930-141502-bootnext")]
    [InlineData(ChangeOperation.ClearBootNext, "20260930-141502-bootnext-clear")]
    public void SnapshotFolderName_IsTheTimestampAndTheOperationWord(ChangeOperation operation, string expected)
    {
        ChangeRequest request = operation switch
        {
            ChangeOperation.Reorder => Reorder(UsbNumber, WindowsOnPartition1),
            ChangeOperation.SetBootNext => Next(WindowsOnPartition1),
            _ => Clear(),
        };
        VariableSet set = operation == ChangeOperation.ClearBootNext ? Store(Var("BootNext", U16(WindowsOnPartition1))) : Store();

        ChangePlan plan = Plan(request, set);

        Assert.Equal(operation, plan.Operation);
        Assert.Equal(expected, plan.SnapshotFolderName);
        Assert.Equal(PlanTime, plan.CreatedAt);
    }

    [Fact]
    public void SnapshotFolderName_OfARestore_EndsWithRestore_AndThePlanNamesTheFolder()
    {
        ReorderRequest restore = Reorder(UsbNumber, WindowsOnPartition1) with { RestoredFrom = @"C:\ProgramData\UefiAccess\snapshots\20260930-120000-reorder" };

        ChangePlan plan = Plan(restore);

        Assert.Equal("20260930-141502-reorder-restore", plan.SnapshotFolderName);
        Assert.Equal(@"C:\ProgramData\UefiAccess\snapshots\20260930-120000-reorder", plan.RestoredFrom);
    }

    [Fact]
    public void Plan_KeepsTheChecksAndPolicyItWasGiven()
    {
        ValidationResult validation = Validate(Reorder(UsbNumber, WindowsOnPartition1), Store());
        List<CheckResult> checks = new() { new CheckResult(CheckNames.Preconditions, true, string.Empty) };
        checks.AddRange(validation.Checks);
        PolicyFindings findings = Findings();

        ChangePlan plan = new(validation, findings, checks, PlanTime);

        Assert.Same(findings, plan.Policy);
        Assert.Equal(checks, plan.Checks);
        Assert.True(plan.Policy.MachineConfirmed);
    }

    [Fact]
    public void Plan_RefusesABlockedValidation()
    {
        ValidationResult blocked = Validate(Reorder(WindowsOnPartition1, UsbNumber), Store());
        Assert.False(blocked.Passed);

        Assert.Throws<ArgumentException>(() => new ChangePlan(blocked, Findings(), blocked.Checks, PlanTime));
    }

    [Fact]
    public void Plan_RefusesACheckRequest_WhichNeverMakesAPlan()
    {
        ValidationResult check = Validate(Check("BootOrder"), Store());
        Assert.True(check.Passed);
        Assert.False(check.IsPlan);

        Assert.Throws<ArgumentException>(() => new ChangePlan(check, Findings(), check.Checks, PlanTime));
    }

    [Fact]
    public void OperationFolderWord_NamesTheThreeOperations()
    {
        Assert.Equal("reorder", ChangePlan.OperationFolderWord(ChangeOperation.Reorder));
        Assert.Equal("bootnext", ChangePlan.OperationFolderWord(ChangeOperation.SetBootNext));
        Assert.Equal("bootnext-clear", ChangePlan.OperationFolderWord(ChangeOperation.ClearBootNext));
    }

    [Fact]
    public void VariableImage_HashesAndRendersLikeTheBaselineIndex()
    {
        VariableImage image = VariableImage.FromBytes(new byte[] { 0x02, 0x00, 0x01, 0x00 }, 7);

        Assert.True(image.Present);
        Assert.Equal("02000100", image.Hex);
        Assert.Equal(4, image.Size);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(new byte[] { 2, 0, 1, 0 })).ToLowerInvariant(), image.Sha256);
        Assert.True(image.SameBytesAs(new byte[] { 2, 0, 1, 0 }));
        Assert.False(image.SameBytesAs(new byte[] { 1, 0, 2, 0 }));
        Assert.False(image.SameBytesAs(null));

        VariableImage absent = VariableImage.Absent(7);
        Assert.False(absent.Present);
        Assert.Equal(string.Empty, absent.Hex);
        Assert.True(absent.SameBytesAs(null));
        Assert.False(absent.SameBytesAs(Array.Empty<byte>()));
    }

    [Fact]
    public void VariableImage_KeepsItsOwnCopy_SoThePlannedBytesCannotBeChangedAfterTheHash()
    {
        byte[] source = { 0x03, 0x00, 0x02, 0x00 };
        VariableImage image = VariableImage.FromBytes(source, 7);
        source[0] = 0x00;

        byte[] handedOut = image.Bytes;
        handedOut[0] = 0x07;

        Assert.Equal("03000200", image.Hex);
        Assert.Equal(new byte[] { 0x03, 0x00, 0x02, 0x00 }, image.Bytes);
        Assert.True(image.SameBytesAs(new byte[] { 0x03, 0x00, 0x02, 0x00 }));

        VariableImage built = new(true, source, "hash", "hex", 7);
        source[1] = 0x09;
        Assert.Equal(new byte[] { 0x00, 0x00, 0x02, 0x00 }, built.Bytes);
    }

    [Fact]
    public void Plan_AfterBytesHandedOut_AreACopy_AndThePlanHashStillDescribesThePlan()
    {
        ChangePlan plan = Plan(Reorder(UsbNumber, WindowsOnPartition1));
        string hash = plan.PlanHash;

        plan.After.Bytes[0] = 0x00;

        Assert.Equal("03000200", plan.After.Hex);
        Assert.Equal(new byte[] { 0x03, 0x00, 0x02, 0x00 }, plan.After.Bytes);
        Assert.Equal(hash, ChangePlan.ComputePlanHash(plan.Operation, plan.Variable.Key, plan.Before, plan.After));
    }

    [Fact]
    public void Plan_Lists_AreReadOnly()
    {
        ChangePlan plan = Plan(Reorder(UsbNumber, WindowsOnPartition1));

        Assert.IsNotType<EntryLine[]>(plan.BeforeEntries);
        Assert.IsNotType<EntryLine[]>(plan.AfterEntries);
        Assert.IsNotType<DiffRow[]>(plan.Diff);
        Assert.IsNotType<List<CheckResult>>(plan.Checks);
        Assert.IsNotType<CheckResult[]>(plan.Checks);
    }

    [Fact]
    public void Confirm_RefusesAChoiceThatIsNotOneOfTheThree()
    {
        ChangePlan plan = Plan(Reorder(UsbNumber, WindowsOnPartition1));

        Assert.Throws<ArgumentOutOfRangeException>(() => plan.Confirm((BitLockerChoice)7));
    }

    [Fact]
    public void BitLockerChoice_HasExactlyTheThreeValuesOfSection32()
    {
        Assert.Equal(new[] { "NotNeeded", "SuspendForOneRestart", "KeepProtection" }, Enum.GetNames<BitLockerChoice>());
    }

    [Fact]
    public void Plan_KeepsTheUnitsBitLockerAssessment()
    {
        ChangePlan plan = Plan(Reorder(UsbNumber, WindowsOnPartition1));

        Assert.Equal(BitLockerState.On, plan.Policy.BitLocker.State);
        Assert.True(plan.Policy.BitLocker.NeedsChoice);

        // The fixture carries the protector names the M1 inspector renders, so the unit's TPM protector is seen.
        Assert.True(BitLockerPreflight.HasTpmProtector(plan.Policy.BitLocker.ProtectorTypes));
        Assert.StartsWith("BitLocker protects drive " + BitLockerPreflight.OsDriveLetter + " with the TPM and checks the boot path (PCR 0, 2, 4, 11", plan.Policy.BitLocker.StateSentence, StringComparison.Ordinal);
    }

    [Fact]
    public void EntryLine_Flags_SpellTheEntryLikeTheDecoders()
    {
        Assert.Equal("active, category Boot", new EntryLine(1, "Boot0002", "Windows Boot Manager", true, false, "Boot", "HD(1,GPT,…)").Flags);
        Assert.Equal("inactive, hidden, category App", new EntryLine(1, "Boot0004", "Shell", false, true, "App", "FvFile(…)").Flags);
        Assert.Equal("missing", new EntryLine(1, "Boot0010", CheckSentences.MissingEntryDescription, false, false, string.Empty, string.Empty) { Present = false }.Flags);
        Assert.Equal("active", new EntryLine(1, "Boot0002", "Windows Boot Manager", true, false, string.Empty, string.Empty).Flags);
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
