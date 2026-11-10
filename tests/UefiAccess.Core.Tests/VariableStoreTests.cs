// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Platform;
using UefiAccess.Core.Variables;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// Tests for the in-memory store and for the Windows store's operating-system guard. No test here
/// calls a native API: the Windows store is only exercised on a non-Windows host, where its guard
/// returns before any P/Invoke.
/// </summary>
public sealed class VariableStoreTests
{
    private static readonly Guid Global = KnownGuids.EfiGlobalVariable;
    private static readonly Guid ImageSecurity = KnownGuids.EfiImageSecurityDatabase;

    private static InMemoryFirmwareVariableStore CreateStore()
    {
        return new InMemoryFirmwareVariableStore(new[]
        {
            new FirmwareVariable("dbx", ImageSecurity, VariableAttributes.RuntimeAccess, new byte[] { 9, 9 }),
            new FirmwareVariable("BootOrder", Global, VariableAttributes.NonVolatile | VariableAttributes.RuntimeAccess, new byte[] { 1, 0 }),
            new FirmwareVariable("SetupMode", Global, VariableAttributes.RuntimeAccess, Array.Empty<byte>()),
        });
    }

    [Fact]
    public void InMemory_Enumerate_ReturnsAllSortedByKeyWithInMemoryMethod()
    {
        InMemoryFirmwareVariableStore store = CreateStore();

        EnumerationResult result = store.Enumerate();

        Assert.Equal(EnumerationMethod.InMemory, result.Method);
        Assert.Null(result.Error);
        Assert.Empty(result.Warnings);
        Assert.Equal(3, result.EntriesParsed);
        Assert.Equal(3, store.Count);
        Assert.Equal(
            new[] { Global.ToString("D") + ":BootOrder", Global.ToString("D") + ":SetupMode", ImageSecurity.ToString("D") + ":dbx" },
            result.Variables.Select(v => v.Key).ToArray());
    }

    [Fact]
    public void InMemory_Read_FindsByNameAndGuid()
    {
        InMemoryFirmwareVariableStore store = CreateStore();

        ReadResult hit = store.Read("BootOrder", Global);
        Assert.Equal(ReadStatus.Success, hit.Status);
        Assert.Null(hit.Error);
        Assert.NotNull(hit.Variable);
        Assert.Equal(new byte[] { 1, 0 }, hit.Variable.Data);
        Assert.Equal(2, hit.LastBufferSize);

        ReadResult zeroLength = store.Read("SetupMode", Global);
        Assert.Equal(ReadStatus.Success, zeroLength.Status);
        Assert.NotNull(zeroLength.Variable);
        Assert.Empty(zeroLength.Variable.Data);
    }

    [Fact]
    public void InMemory_Read_MissesAreNotFound()
    {
        InMemoryFirmwareVariableStore store = CreateStore();

        ReadResult wrongGuid = store.Read("dbx", Global);
        Assert.Equal(ReadStatus.NotFound, wrongGuid.Status);
        Assert.Null(wrongGuid.Variable);
        Assert.NotNull(wrongGuid.Error);
        Assert.Contains("dbx", wrongGuid.Error, StringComparison.Ordinal);

        ReadResult wrongCase = store.Read("bootorder", Global);
        Assert.Equal(ReadStatus.NotFound, wrongCase.Status);

        ReadResult missing = store.Read("Missing", Global);
        Assert.Equal(ReadStatus.NotFound, missing.Status);

        ReadResult nullName = store.Read(null!, Global);
        Assert.Equal(ReadStatus.NotFound, nullName.Status);
    }

    [Fact]
    public void InMemory_EmptyAndNullSequences_YieldEmptyStore()
    {
        EnumerationResult empty = new InMemoryFirmwareVariableStore(Array.Empty<FirmwareVariable>()).Enumerate();
        Assert.Empty(empty.Variables);
        Assert.Equal(EnumerationMethod.InMemory, empty.Method);

        EnumerationResult fromNull = new InMemoryFirmwareVariableStore(null!).Enumerate();
        Assert.Empty(fromNull.Variables);
        Assert.Null(fromNull.Error);
    }

    [NonWindowsFact]
    public void WindowsStore_OnNonWindowsHost_ReportsNotSupportedWithoutNativeCalls()
    {
        // The guard is only observable off Windows; on Windows this test must not touch firmware.
        CountingDetector detector = new();
        WindowsFirmwareVariableStore store = new(detector);

        EnumerationResult enumeration = store.Enumerate();
        Assert.Equal(EnumerationMethod.None, enumeration.Method);
        Assert.Empty(enumeration.Variables);
        Assert.Equal(0, enumeration.EntriesParsed);
        Assert.NotNull(enumeration.Error);
        Assert.Contains("not supported on this OS", enumeration.Error, StringComparison.Ordinal);

        ReadResult read = store.Read("BootOrder", Global);
        Assert.Equal(ReadStatus.NotSupportedOnThisOs, read.Status);
        Assert.Null(read.Variable);
        Assert.NotNull(read.Error);
        Assert.Contains("not supported on this OS", read.Error, StringComparison.Ordinal);
        Assert.Equal(0, read.LastBufferSize);

        Assert.Equal(0, detector.Calls);
    }

    [Fact]
    public void WindowsStore_ConstantsMatchTheDesign()
    {
        Assert.Equal(64 * 1024, WindowsFirmwareVariableStore.InitialReadBufferSize);
        Assert.Equal(4 * 1024 * 1024, WindowsFirmwareVariableStore.MaximumReadBufferSize);
        Assert.Equal(3, WindowsFirmwareVariableStore.BootProbeMissLimit);
    }

    private sealed class CountingDetector : IFirmwareTypeDetector
    {
        public int Calls { get; private set; }

        public FirmwareType Detect()
        {
            Calls++;
            return FirmwareType.Uefi;
        }
    }
}
