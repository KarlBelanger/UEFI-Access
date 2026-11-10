// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Runtime.CompilerServices;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Variables;
using Xunit;
using static UefiAccess.Core.Tests.Fixtures.LoadOptionFixtures;
using static UefiAccess.Core.Tests.Safety.SafetyFixtures;

namespace UefiAccess.Core.Tests.Safety;

/// <summary>
/// The refusals behind the first line of defence, which no plan the pipeline confirms can reach
/// (the validator enforces the same rules first), called directly so that deleting or weakening
/// one of them fails the suite: the last check of the plan's shape before the call (stage S15,
/// <c>WriteTransaction.BuildWritePlan</c>, design 2.4 items 1 and 3), the comparison of the
/// snapshot read back with the plan (stage S12, <c>WriteTransaction.SnapshotMismatch</c>; nothing
/// runs between the store's write and its read inside the stage), and the pipeline's refusal of
/// the real writer over a store that is not the firmware's (design 2.4, item 6). Nothing here
/// writes: no writer is called, and the one real-writer instance is made without its constructor,
/// has no seam, and is never called (WriteCallSiteGuardTests allows exactly that site).
/// </summary>
public sealed class DefenceInDepthTests : IDisposable
{
    private const string BootOrder = "BootOrder";
    private const string BootNext = "BootNext";

    private readonly string _dataDirectory;
    private readonly SnapshotStore _store;

    public DefenceInDepthTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "defence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDirectory);
        _store = new SnapshotStore(Path.Combine(_dataDirectory, SnapshotStore.FolderName), _dataDirectory, _ => null);
    }

    public void Dispose()
    {
        TestFolders.DeleteQuietly(_dataDirectory);
    }

    // ---- S15: the last check of the plan's shape ----------------------------------------------

    [Theory]
    [InlineData(ChangeOperation.Reorder, "02000300", "03000200", "03000200")]
    [InlineData(ChangeOperation.SetBootNext, null, "0200", "0200")]
    [InlineData(ChangeOperation.ClearBootNext, "0300", null, "")]
    public void BuildWritePlan_OfAPlanOfTheRightShape_IsTheWhitelistEntrysOwnWrite(ChangeOperation operation, string? beforeHex, string? afterHex, string writtenHex)
    {
        WhitelistEntry entry = operation == ChangeOperation.Reorder ? Whitelist.BootOrder : Whitelist.BootNext;
        ChangePlan plan = PlanOf(operation, entry, Image(beforeHex), Image(afterHex));

        WritePlan? written = WriteTransaction.BuildWritePlan(plan, out string? problem);

        Assert.Null(problem);
        WritePlan call = Assert.IsType<WritePlan>(written);
        Assert.Equal(entry.Name, call.Name);
        Assert.Equal(entry.Guid, call.Guid);
        Assert.Equal(0x7u, call.Attributes);
        Assert.Equal(writtenHex, Convert.ToHexString(call.Bytes).ToLowerInvariant());
    }

    [Theory]
    [InlineData("notWhitelisted", TransactionSentences.NotWhitelistedReason)]
    [InlineData("reorderOfBootNext", TransactionSentences.NotWhitelistedReason)]
    [InlineData("oneTimeBootOfBootOrder", TransactionSentences.NotWhitelistedReason)]
    [InlineData("attributesToPass", TransactionSentences.AttributesReason)]
    [InlineData("attributesRead", TransactionSentences.AttributesReason)]
    [InlineData("reorderOfAnotherSize", TransactionSentences.ReorderSizeReason)]
    [InlineData("reorderOfNoBytes", TransactionSentences.ReorderSizeReason)]
    [InlineData("reorderOfAnOddSize", TransactionSentences.ReorderSizeReason)]
    [InlineData("reorderOfAnAbsentOrder", TransactionSentences.ReorderSizeReason)]
    [InlineData("reorderToNothing", TransactionSentences.ReorderSizeReason)]
    [InlineData("oneTimeBootOfFourBytes", TransactionSentences.BootNextSizeReason)]
    [InlineData("oneTimeBootToNothing", TransactionSentences.BootNextSizeReason)]
    [InlineData("clearOfAnAbsentBootNext", TransactionSentences.ClearReason)]
    [InlineData("clearThatWrites", TransactionSentences.ClearReason)]
    [InlineData("bytesNotThePlans", TransactionSentences.BytesReason)]
    public void BuildWritePlan_RefusesAPlanOfAnotherShape_WithItsReason(string scenario, string reason)
    {
        ChangePlan plan = scenario switch
        {
            "notWhitelisted" => PlanOf(ChangeOperation.Reorder, new WhitelistEntry("Timeout", Global, WritableKind.ReorderOnly, 7), Image("0500"), Image("0500")),
            "reorderOfBootNext" => PlanOf(ChangeOperation.Reorder, Whitelist.BootNext, Image("0200"), Image("0300")),
            "oneTimeBootOfBootOrder" => PlanOf(ChangeOperation.SetBootNext, Whitelist.BootOrder, Image(null), Image("0200")),
            "attributesToPass" => PlanOf(ChangeOperation.Reorder, Whitelist.BootOrder, Image("02000300"), Image("03000200", 0x27)),
            "attributesRead" => PlanOf(ChangeOperation.Reorder, Whitelist.BootOrder, Image("02000300", 0x6), Image("03000200")),
            "reorderOfAnotherSize" => PlanOf(ChangeOperation.Reorder, Whitelist.BootOrder, Image("02000300"), Image("030002000000")),
            "reorderOfNoBytes" => PlanOf(ChangeOperation.Reorder, Whitelist.BootOrder, Image(string.Empty), Image(string.Empty)),
            "reorderOfAnOddSize" => PlanOf(ChangeOperation.Reorder, Whitelist.BootOrder, Image("020003"), Image("030002")),
            "reorderOfAnAbsentOrder" => PlanOf(ChangeOperation.Reorder, Whitelist.BootOrder, Image(null), Image("03000200")),
            "reorderToNothing" => PlanOf(ChangeOperation.Reorder, Whitelist.BootOrder, Image("02000300"), Image(null)),
            "oneTimeBootOfFourBytes" => PlanOf(ChangeOperation.SetBootNext, Whitelist.BootNext, Image(null), Image("02000300")),
            "oneTimeBootToNothing" => PlanOf(ChangeOperation.SetBootNext, Whitelist.BootNext, Image("0300"), Image(null)),
            "clearOfAnAbsentBootNext" => PlanOf(ChangeOperation.ClearBootNext, Whitelist.BootNext, Image(null), Image(null)),
            "clearThatWrites" => PlanOf(ChangeOperation.ClearBootNext, Whitelist.BootNext, Image("0300"), Image("0200")),

            // The bytes are one order and the hex the plan's hash was taken over is another: the
            // plan's integrity check at S11 would stop it, so only a direct call reaches this clause.
            _ => PlanOf(ChangeOperation.Reorder, Whitelist.BootOrder, Image("02000300"), Mislabelled("03000200", "02000300")),
        };

        WritePlan? written = WriteTransaction.BuildWritePlan(plan, out string? problem);

        Assert.Null(written);
        Assert.Equal(reason, problem);
    }

    // ---- S12: the snapshot read back, compared with the plan -----------------------------------

    [Fact]
    public void SnapshotMismatch_OfTheSnapshotAsWritten_IsNone_AndEachComparisonRefusesItsOwnDifference()
    {
        ChangePlan plan = PlanOf(ChangeOperation.Reorder, Whitelist.BootOrder, Image("02000300"), Image("03000200"));
        SnapshotReadResult read = CreateAndRead("20260930-141502-reorder", Whitelist.BootOrder, ChangeOperation.Reorder, plan.PlanHash);
        Assert.True(read.Succeeded, read.Error);
        Assert.NotNull(read.TargetBefore);
        string targetReason = CheckSentences.Format(TransactionSentences.SnapshotTargetReasonFormat, BootOrder);

        Assert.Null(WriteTransaction.SnapshotMismatch(plan, read));
        Assert.Equal(TransactionSentences.SnapshotPlanHashReason, WriteTransaction.SnapshotMismatch(plan, read with { Document = read.Document! with { PlanHash = new string('b', 64) } }));
        Assert.Equal(targetReason, WriteTransaction.SnapshotMismatch(plan, read with { TargetBefore = Var(BootOrder, BootList(UsbNumber, WindowsOnPartition1)) }));
        Assert.Equal(targetReason, WriteTransaction.SnapshotMismatch(plan, read with { TargetBefore = null }));
    }

    [Fact]
    public void SnapshotMismatch_OfAPlanWhoseTargetWasAbsent_RefusesASnapshotThatHoldsOne()
    {
        ChangePlan plan = PlanOf(ChangeOperation.SetBootNext, Whitelist.BootNext, Image(null), Image("0200"));
        SnapshotReadResult read = CreateAndRead("20260930-141502-bootnext", Whitelist.BootNext, ChangeOperation.SetBootNext, plan.PlanHash);
        Assert.True(read.Succeeded, read.Error);
        Assert.Null(read.TargetBefore);

        Assert.Null(WriteTransaction.SnapshotMismatch(plan, read));
        Assert.Equal(
            CheckSentences.Format(TransactionSentences.SnapshotTargetReasonFormat, BootNext),
            WriteTransaction.SnapshotMismatch(plan, read with { TargetBefore = Var(BootNext, U16(UsbNumber)) }));
    }

    [Fact]
    public void SnapshotMismatch_OfASnapshotThatDidNotReadBack_IsTheReadersReason()
    {
        ChangePlan plan = PlanOf(ChangeOperation.Reorder, Whitelist.BootOrder, Image("02000300"), Image("03000200"));
        string folder = Path.Combine(_store.RootDirectory, "20260930-141502-reorder");

        Assert.Equal("the reader's reason", WriteTransaction.SnapshotMismatch(plan, new SnapshotReadResult(folder, null, null, null, null, "the reader's reason")));
        Assert.Equal(SnapshotStore.UnknownReason, WriteTransaction.SnapshotMismatch(plan, new SnapshotReadResult(folder, null, null, null, null, null)));
    }

    // ---- the pipeline: the real writer only over the firmware's store --------------------------

    [Fact]
    public void TheRealWriter_OverAStoreThatIsNotTheFirmwares_IsRefused()
    {
        using TransactionRig rig = new();
        InMemoryFirmwareVariableStore inMemory = new(Store().All);
        PlatformPolicy policy = new(rig.Identity, rig.SecureBoot, rig.BitLocker, rig.Tpm, Serilog.Core.Logger.None);

        // Made without its constructor: no seam, so even a call would stop at the writer's own
        // argument check before any native code. The pipeline is only constructed, never run.
        FirmwareVariableWriter realWriter = (FirmwareVariableWriter)RuntimeHelpers.GetUninitializedObject(typeof(FirmwareVariableWriter));

        ArgumentException refused = Assert.Throws<ArgumentException>(
            () => new WritePipeline(rig.Detector, rig.Privileges, inMemory, policy, realWriter, rig.Suspender, rig.Snapshots, rig.Records, rig.BaselineRoot, Serilog.Core.Logger.None));

        Assert.Equal("store", refused.ParamName);
        Assert.StartsWith(WritePipeline.RealWriterNeedsTheFirmwareStoreMessage, refused.Message, StringComparison.Ordinal);

        // The condition, not only the throw: a writer behind the interface over the same store is accepted.
        WritePipeline withAFake = new(rig.Detector, rig.Privileges, inMemory, policy, rig.Writer, rig.Suspender, rig.Snapshots, rig.Records, rig.BaselineRoot, Serilog.Core.Logger.None);
        Assert.Equal(0, withAFake.PendingPlanCount);
        Assert.Empty(rig.Writer.Calls);
    }

    // ---- helpers ---------------------------------------------------------------------------------

    /// <summary>
    /// A plan of any shape, built through the internal constructors as the transaction builds one
    /// from a validation, so the checks behind the validator can be handed what it never passes.
    /// </summary>
    private static ChangePlan PlanOf(ChangeOperation operation, WhitelistEntry variable, VariableImage before, VariableImage after)
    {
        ChangeRequest request = operation switch
        {
            ChangeOperation.Reorder => Reorder(UsbNumber, WindowsOnPartition1),
            ChangeOperation.SetBootNext => Next(WindowsOnPartition1),
            _ => Clear(),
        };
        ValidationResult validation = new(
            request,
            Array.Empty<CheckResult>(),
            null,
            null,
            operation,
            variable,
            before,
            after,
            Array.Empty<EntryLine>(),
            Array.Empty<EntryLine>(),
            Array.Empty<DiffRow>(),
            Array.Empty<string>(),
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
            Array.Empty<VariableDependency>());
        return new ChangePlan(validation, Findings(), Array.Empty<CheckResult>(), PlanTime);
    }

    /// <summary>An image of the bytes in <paramref name="hex"/>, or an absent one for null.</summary>
    private static VariableImage Image(string? hex, uint attributes = 0x7)
    {
        return hex is null ? VariableImage.Absent(attributes) : VariableImage.FromBytes(Convert.FromHexString(hex), attributes);
    }

    /// <summary>An image whose bytes are one value and whose hex text says another.</summary>
    private static VariableImage Mislabelled(string bytesHex, string labelHex)
    {
        byte[] bytes = Convert.FromHexString(bytesHex);
        return new VariableImage(true, bytes, VariableImage.Sha256Of(bytes), labelHex, 0x7);
    }

    /// <summary>A snapshot of the default store written by the store and read back, as stage S12 does, carrying <paramref name="planHash"/>.</summary>
    private SnapshotReadResult CreateAndRead(string id, WhitelistEntry target, ChangeOperation operation, string planHash)
    {
        SnapshotCreateResult created = _store.Create(SnapshotStoreTests.Request(id, Store(), target, operation, planHash: planHash));
        Assert.True(created.Succeeded, created.Error);
        return _store.Read(Assert.IsType<string>(created.Folder));
    }
}
