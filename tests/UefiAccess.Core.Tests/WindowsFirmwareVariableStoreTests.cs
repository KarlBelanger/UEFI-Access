// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Runtime.InteropServices;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Core.Native;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Tests.Fixtures;
using UefiAccess.Core.Variables;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// Exercises <see cref="WindowsFirmwareVariableStore"/> through a fake <see cref="INativeFirmwareApi"/>
/// on any host: the two-call enumeration pattern, the grow-once retry, NTSTATUS mapping, the
/// known-names fallback with its Boot#### probe, the 64 KiB to 4 MiB read buffer strategy, Win32
/// error mapping and zero-length reads. No test here reaches a native API: the fake reports itself
/// available, so the operating-system guard is bypassed, and every byte the store sees comes from
/// the fake.
/// </summary>
public sealed class WindowsFirmwareVariableStoreTests
{
    private static readonly Guid Global = KnownGuids.EfiGlobalVariable;
    private static readonly Guid ImageSecurity = KnownGuids.EfiImageSecurityDatabase;
    private static readonly string GlobalText = KnownGuids.ToBracedUpper(Global);
    private static readonly string ImageSecurityText = KnownGuids.ToBracedUpper(ImageSecurity);

    private const uint NvBsRt = 0x7;

    // ---- enumeration ----------------------------------------------------------------------

    [Fact]
    public void Enumerate_TwoCallPattern_ParsesEntries()
    {
        byte[] buffer = EnumerationBuffers.ThreeEntries();
        FakeNativeFirmwareApi api = new();
        api.EnumerateSteps.Enqueue(new EnumerateStep(NativeConstants.STATUS_BUFFER_TOO_SMALL, null, (uint)buffer.Length));
        api.EnumerateSteps.Enqueue(new EnumerateStep(NativeConstants.STATUS_SUCCESS, buffer, null));
        StubDetector detector = new(FirmwareType.Uefi);

        EnumerationResult result = CreateStore(api, detector).Enumerate();

        Assert.Equal(EnumerationMethod.NtEnumerate, result.Method);
        Assert.Null(result.Error);
        Assert.Empty(result.Warnings);
        Assert.Equal(3, result.EntriesParsed);
        Assert.Equal(new[] { "BootOrder", EnumerationBuffers.JapaneseName, "dbx" }, result.Variables.Select(v => v.Name).ToArray());
        Assert.Equal(EnumerationBuffers.BootOrderValue, result.Variables[0].Data);
        Assert.Equal((VariableAttributes)0x7, result.Variables[0].Attributes);
        Assert.Empty(result.Variables[1].Data);
        Assert.Equal(ImageSecurity, result.Variables[2].VendorGuid);
        Assert.Equal(EnumerationBuffers.DbxValue, result.Variables[2].Data);

        Assert.Equal(2, api.EnumerateCalls.Count);
        Assert.All(api.EnumerateCalls, call => Assert.Equal(NativeConstants.SYSTEM_ENVIRONMENT_VALUE_INFORMATION, call.InformationClass));
        Assert.False(api.EnumerateCalls[0].HasBuffer);
        Assert.Equal(0u, api.EnumerateCalls[0].LengthIn);
        Assert.True(api.EnumerateCalls[1].HasBuffer);
        Assert.Equal((uint)buffer.Length, api.EnumerateCalls[1].LengthIn);
        Assert.Empty(api.ReadCalls);
        Assert.Equal(0, detector.Calls);
    }

    [Fact]
    public void Enumerate_SecondCallStillTooSmall_GrowsOnceAndSucceeds()
    {
        byte[] buffer = EnumerationBuffers.ThreeEntries();
        uint underReported = (uint)buffer.Length - 16;
        FakeNativeFirmwareApi api = new();
        api.EnumerateSteps.Enqueue(new EnumerateStep(NativeConstants.STATUS_BUFFER_TOO_SMALL, null, underReported));
        api.EnumerateSteps.Enqueue(new EnumerateStep(NativeConstants.STATUS_BUFFER_TOO_SMALL, null, (uint)buffer.Length));
        api.EnumerateSteps.Enqueue(new EnumerateStep(NativeConstants.STATUS_SUCCESS, buffer, null));
        StubDetector detector = new(FirmwareType.Uefi);

        EnumerationResult result = CreateStore(api, detector).Enumerate();

        Assert.Equal(EnumerationMethod.NtEnumerate, result.Method);
        Assert.Null(result.Error);
        Assert.Equal(3, result.EntriesParsed);
        Assert.Equal(3, api.EnumerateCalls.Count);
        Assert.Equal(underReported, api.EnumerateCalls[1].LengthIn);
        Assert.Equal(Math.Max((uint)buffer.Length, underReported * 2), api.EnumerateCalls[2].LengthIn);
        Assert.Equal(0, detector.Calls);
    }

    [Fact]
    public void Enumerate_StillTooSmallAfterRetry_FailsWithNtStatusText()
    {
        FakeNativeFirmwareApi api = new();
        api.EnumerateSteps.Enqueue(new EnumerateStep(NativeConstants.STATUS_BUFFER_TOO_SMALL, null, 100));
        api.EnumerateSteps.Enqueue(new EnumerateStep(NativeConstants.STATUS_BUFFER_TOO_SMALL, null, 200));
        api.EnumerateSteps.Enqueue(new EnumerateStep(NativeConstants.STATUS_BUFFER_TOO_SMALL, null, 300));
        StubDetector detector = new(FirmwareType.LegacyBios);

        EnumerationResult result = CreateStore(api, detector).Enumerate();

        Assert.Equal(EnumerationMethod.None, result.Method);
        Assert.Empty(result.Variables);
        Assert.NotNull(result.Error);
        Assert.Contains("still reported STATUS_BUFFER_TOO_SMALL after growing the buffer to 200 bytes (required 300)", result.Error, StringComparison.Ordinal);
        Assert.Contains("LegacyBios", result.Error, StringComparison.Ordinal);
        Assert.Equal(3, api.EnumerateCalls.Count);
        Assert.Empty(api.ReadCalls);
    }

    [Theory]
    [InlineData(0xC0000061u, "STATUS_PRIVILEGE_NOT_HELD (0xC0000061)")]
    [InlineData(0xC0000002u, "STATUS_NOT_IMPLEMENTED (0xC0000002)")]
    [InlineData(0xC000000Du, "STATUS_INVALID_PARAMETER (0xC000000D)")]
    [InlineData(0xC00000BBu, "NTSTATUS 0xC00000BB")]
    public void Enumerate_FailureAfterRetry_MapsNtStatusText(uint status, string expectedText)
    {
        FakeNativeFirmwareApi api = new();
        api.EnumerateSteps.Enqueue(new EnumerateStep(NativeConstants.STATUS_BUFFER_TOO_SMALL, null, 100));
        api.EnumerateSteps.Enqueue(new EnumerateStep(NativeConstants.STATUS_BUFFER_TOO_SMALL, null, 200));
        api.EnumerateSteps.Enqueue(new EnumerateStep(status, null, 0));
        StubDetector detector = new(FirmwareType.Unknown);

        EnumerationResult result = CreateStore(api, detector).Enumerate();

        Assert.Equal(EnumerationMethod.None, result.Method);
        Assert.NotNull(result.Error);
        Assert.StartsWith("NtEnumerateSystemEnvironmentValuesEx failed: " + NativeError.DescribeNtStatus(status), result.Error, StringComparison.Ordinal);
        Assert.Contains(expectedText, result.Error, StringComparison.Ordinal);
        Assert.Equal(3, api.EnumerateCalls.Count);
    }

    [Theory]
    [InlineData(0xC0000061u, "STATUS_PRIVILEGE_NOT_HELD")]
    [InlineData(0xC0000002u, "STATUS_NOT_IMPLEMENTED")]
    [InlineData(0xC000000Du, "STATUS_INVALID_PARAMETER")]
    [InlineData(0xC00000BBu, "NTSTATUS 0xC00000BB")]
    public void Enumerate_FirstCallFailure_MapsNtStatusText(uint status, string expectedText)
    {
        FakeNativeFirmwareApi api = new();
        api.EnumerateSteps.Enqueue(new EnumerateStep(status, null, 0));
        StubDetector detector = new(FirmwareType.LegacyBios);

        EnumerationResult result = CreateStore(api, detector).Enumerate();

        Assert.Equal(EnumerationMethod.None, result.Method);
        Assert.NotNull(result.Error);
        Assert.Contains(expectedText, result.Error, StringComparison.Ordinal);
        Assert.Single(api.EnumerateCalls);
        Assert.Equal(1, detector.Calls);
    }

    [Fact]
    public void Enumerate_EmptyStore_SuccessWithNoBuffer()
    {
        FakeNativeFirmwareApi api = new();
        api.EnumerateSteps.Enqueue(new EnumerateStep(NativeConstants.STATUS_SUCCESS, null, 0));
        StubDetector detector = new(FirmwareType.Uefi);

        EnumerationResult result = CreateStore(api, detector).Enumerate();

        Assert.Equal(EnumerationMethod.NtEnumerate, result.Method);
        Assert.Empty(result.Variables);
        Assert.Null(result.Error);
        string warning = Assert.Single(result.Warnings);
        Assert.Contains("zero-length buffer", warning, StringComparison.Ordinal);
        Assert.Single(api.EnumerateCalls);
        Assert.Empty(api.ReadCalls);
        Assert.Equal(0, detector.Calls);
    }

    [Fact]
    public void Enumerate_RequiredLengthZero_FailsWithoutAllocating()
    {
        FakeNativeFirmwareApi api = new();
        api.EnumerateSteps.Enqueue(new EnumerateStep(NativeConstants.STATUS_BUFFER_TOO_SMALL, null, 0));

        EnumerationResult result = CreateStore(api, new StubDetector(FirmwareType.LegacyBios)).Enumerate();

        Assert.Equal(EnumerationMethod.None, result.Method);
        Assert.Contains("required length of 0", result.Error, StringComparison.Ordinal);
        Assert.Single(api.EnumerateCalls);
    }

    [Fact]
    public void Enumerate_RequiredLengthAboveSanityCeiling_FailsWithoutAllocating()
    {
        FakeNativeFirmwareApi api = new();
        api.EnumerateSteps.Enqueue(new EnumerateStep(NativeConstants.STATUS_BUFFER_TOO_SMALL, null, 512u * 1024 * 1024));

        EnumerationResult result = CreateStore(api, new StubDetector(FirmwareType.LegacyBios)).Enumerate();

        Assert.Equal(EnumerationMethod.None, result.Method);
        Assert.Contains("sanity ceiling", result.Error, StringComparison.Ordinal);
        Assert.Single(api.EnumerateCalls);
    }

    [Fact]
    public void Enumerate_PartialParse_KeepsGoodEntriesAndWarns()
    {
        byte[] buffer = EnumerationBuffers.CorruptedValueOffset();
        FakeNativeFirmwareApi api = new();
        api.EnumerateSteps.Enqueue(new EnumerateStep(NativeConstants.STATUS_BUFFER_TOO_SMALL, null, (uint)buffer.Length));
        api.EnumerateSteps.Enqueue(new EnumerateStep(NativeConstants.STATUS_SUCCESS, buffer, null));
        StubDetector detector = new(FirmwareType.Uefi);

        EnumerationResult result = CreateStore(api, detector).Enumerate();

        Assert.Equal(EnumerationMethod.NtEnumerate, result.Method);
        Assert.Equal(1, result.EntriesParsed);
        Assert.Equal("BootOrder", Assert.Single(result.Variables).Name);
        Assert.NotNull(result.Error);
        string warning = Assert.Single(result.Warnings);
        Assert.Contains("stopped early", warning, StringComparison.Ordinal);
        Assert.Equal(0, detector.Calls);
        Assert.Empty(api.ReadCalls);
    }

    [Fact]
    public void Enumerate_UnparseableBuffer_FallsBack()
    {
        byte[] garbage = { 1, 2, 3, 4, 5, 6, 7, 8 };
        FakeNativeFirmwareApi api = new();
        api.EnumerateSteps.Enqueue(new EnumerateStep(NativeConstants.STATUS_BUFFER_TOO_SMALL, null, (uint)garbage.Length));
        api.EnumerateSteps.Enqueue(new EnumerateStep(NativeConstants.STATUS_SUCCESS, garbage, null));

        EnumerationResult result = CreateStore(api, new StubDetector(FirmwareType.Uefi)).Enumerate();

        Assert.Equal(EnumerationMethod.KnownNamesFallback, result.Method);
        Assert.Null(result.Error);
        Assert.Contains("could not be parsed", result.Warnings[0], StringComparison.Ordinal);
        Assert.NotEmpty(api.ReadCalls);
    }

    [Fact]
    public void Enumerate_WhenSeamThrows_FallsBackWithoutThrowing()
    {
        FakeNativeFirmwareApi api = new() { EnumerateThrows = new InvalidOperationException("boom") };
        WindowsFirmwareVariableStore store = CreateStore(api, new StubDetector(FirmwareType.Uefi));

        EnumerationResult? result = null;
        Exception? exception = Record.Exception(() => result = store.Enumerate());

        Assert.Null(exception);
        Assert.NotNull(result);
        Assert.Equal(EnumerationMethod.KnownNamesFallback, result.Method);
        Assert.Contains("threw InvalidOperationException: boom", result.Warnings[0], StringComparison.Ordinal);
    }

    // ---- known-names fallback --------------------------------------------------------------

    [Theory]
    [InlineData(FirmwareType.LegacyBios)]
    [InlineData(FirmwareType.Unknown)]
    public void Fallback_DoesNotRun_UnlessDetectorReportsUefi(FirmwareType firmwareType)
    {
        FakeNativeFirmwareApi api = new();
        api.EnumerateSteps.Enqueue(new EnumerateStep(NativeConstants.STATUS_NOT_IMPLEMENTED, null, 0));
        api.Add("BootOrder", Global, NvBsRt, new byte[] { 1, 0 });
        StubDetector detector = new(firmwareType);

        EnumerationResult result = CreateStore(api, detector).Enumerate();

        Assert.Equal(EnumerationMethod.None, result.Method);
        Assert.Empty(result.Variables);
        Assert.Equal(0, result.EntriesParsed);
        Assert.NotNull(result.Error);
        Assert.Contains("the firmware type is " + firmwareType + ", so the known-names fallback does not apply", result.Error, StringComparison.Ordinal);
        Assert.Empty(api.ReadCalls);
        Assert.Equal(1, detector.Calls);
    }

    [Fact]
    public void Fallback_ReadsEveryFixedName_ThenProbesBootEntriesUntilThreeConsecutiveMisses()
    {
        FakeNativeFirmwareApi api = new();
        api.EnumerateSteps.Enqueue(new EnumerateStep(NativeConstants.STATUS_NOT_IMPLEMENTED, null, 0));
        api.Add("BootOrder", Global, NvBsRt, new byte[] { 0, 0, 1, 0, 3, 0 });
        api.Add("SecureBoot", Global, 0x6, new byte[] { 1 });
        api.Add("dbx", ImageSecurity, NvBsRt | 0x20, new byte[] { 0xAA });
        api.Add("Boot0000", Global, NvBsRt, new byte[] { 1, 0, 0, 0 });
        api.Add("Boot0001", Global, NvBsRt, new byte[] { 1, 0, 0, 0 });
        api.Add("Boot0003", Global, NvBsRt, new byte[] { 1, 0, 0, 0 });
        api.Add("Boot0007", Global, NvBsRt, new byte[] { 9 });
        StubDetector detector = new(FirmwareType.Uefi);

        EnumerationResult result = CreateStore(api, detector).Enumerate();

        Assert.Equal(EnumerationMethod.KnownNamesFallback, result.Method);
        Assert.Null(result.Error);
        Assert.Equal(6, result.EntriesParsed);
        Assert.Equal(
            new[] { "BootOrder", "SecureBoot", "dbx", "Boot0000", "Boot0001", "Boot0003" },
            result.Variables.Select(v => v.Name).ToArray());
        Assert.Equal(ImageSecurity, result.Variables[2].VendorGuid);
        Assert.Equal((VariableAttributes)(NvBsRt | 0x20), result.Variables[2].Attributes);

        (string Name, string Guid)[] expectedFixed = KnownVariableNames.Fixed
            .Select(entry => (entry.Name, KnownGuids.ToBracedUpper(entry.Guid)))
            .ToArray();
        Assert.Equal(19, expectedFixed.Length);
        Assert.Equal(expectedFixed, api.ReadCalls.Take(expectedFixed.Length).Select(call => (call.Name, call.Guid)).ToArray());

        // Boot0000 hit, Boot0001 hit, Boot0002 miss, Boot0003 hit (resets the count), Boot0004..0006 miss, stop.
        string[] bootProbes = api.ReadCalls.Skip(expectedFixed.Length).Select(call => call.Name).ToArray();
        Assert.Equal(new[] { "Boot0000", "Boot0001", "Boot0002", "Boot0003", "Boot0004", "Boot0005", "Boot0006" }, bootProbes);
        Assert.All(api.ReadCalls.Skip(expectedFixed.Length), call => Assert.Equal(GlobalText, call.Guid));
        Assert.DoesNotContain(api.ReadCalls, call => call.Name == "Boot0007");
        Assert.All(api.ReadCalls, call => Assert.Equal((uint)WindowsFirmwareVariableStore.InitialReadBufferSize, call.Size));

        Assert.Equal(2, result.Warnings.Count);
        Assert.Contains("falling back to reading the curated KnownVariableNames list", result.Warnings[0], StringComparison.Ordinal);
        Assert.Contains("vendor-specific variables are not listed", result.Warnings[1], StringComparison.Ordinal);
        Assert.Equal(1, detector.Calls);
    }

    [Fact]
    public void Fallback_WithNoBootEntries_ProbesExactlyThree()
    {
        FakeNativeFirmwareApi api = new();
        api.EnumerateSteps.Enqueue(new EnumerateStep(NativeConstants.STATUS_NOT_IMPLEMENTED, null, 0));

        EnumerationResult result = CreateStore(api, new StubDetector(FirmwareType.Uefi)).Enumerate();

        Assert.Equal(EnumerationMethod.KnownNamesFallback, result.Method);
        Assert.Empty(result.Variables);
        Assert.Null(result.Error);
        Assert.Equal(KnownVariableNames.Fixed.Count + WindowsFirmwareVariableStore.BootProbeMissLimit, api.ReadCalls.Count);
        Assert.Equal(new[] { "Boot0000", "Boot0001", "Boot0002" }, api.ReadCalls.Skip(KnownVariableNames.Fixed.Count).Select(call => call.Name).ToArray());
    }

    [Theory]
    [InlineData(NativeConstants.ERROR_PRIVILEGE_NOT_HELD)]
    [InlineData(NativeConstants.ERROR_INVALID_FUNCTION)]
    public void Fallback_AbortsOnFatalReadError(int error)
    {
        FakeNativeFirmwareApi api = new();
        api.EnumerateSteps.Enqueue(new EnumerateStep(NativeConstants.STATUS_NOT_IMPLEMENTED, null, 0));
        api.Add("BootOrder", Global, NvBsRt, new byte[] { 1, 0 });
        api.ReadErrorsByName["Timeout"] = error;

        EnumerationResult result = CreateStore(api, new StubDetector(FirmwareType.Uefi)).Enumerate();

        Assert.Equal(EnumerationMethod.KnownNamesFallback, result.Method);
        Assert.NotNull(result.Error);
        Assert.StartsWith("known-names fallback aborted while reading Timeout: " + NativeError.DescribeWin32(error), result.Error, StringComparison.Ordinal);
        Assert.Equal("BootOrder", Assert.Single(result.Variables).Name);
        Assert.Equal(new[] { "BootOrder", "BootNext", "BootCurrent", "Timeout" }, api.ReadCalls.Select(call => call.Name).ToArray());
    }

    [Fact]
    public void Fallback_NonFatalReadError_BecomesWarningAndContinues()
    {
        FakeNativeFirmwareApi api = new();
        api.EnumerateSteps.Enqueue(new EnumerateStep(NativeConstants.STATUS_NOT_IMPLEMENTED, null, 0));
        api.Add("KEK", Global, NvBsRt, new byte[] { 2 });
        api.ReadErrorsByName["PK"] = NativeConstants.ERROR_ACCESS_DENIED;

        EnumerationResult result = CreateStore(api, new StubDetector(FirmwareType.Uefi)).Enumerate();

        Assert.Equal(EnumerationMethod.KnownNamesFallback, result.Method);
        Assert.Null(result.Error);
        Assert.Equal("KEK", Assert.Single(result.Variables).Name);
        Assert.Contains(result.Warnings, warning => warning.StartsWith("PK: " + NativeError.DescribeWin32(NativeConstants.ERROR_ACCESS_DENIED), StringComparison.Ordinal));
        Assert.DoesNotContain(result.Warnings, warning => warning.StartsWith("BootOrder:", StringComparison.Ordinal));
    }

    // ---- read -----------------------------------------------------------------------------

    [Fact]
    public void Read_DoublesBufferFrom64KiBTo4MiB_ThenReportsTooLarge()
    {
        FakeNativeFirmwareApi api = new() { ReadErrorForAll = NativeConstants.ERROR_INSUFFICIENT_BUFFER };

        ReadResult result = CreateStore(api, new StubDetector(FirmwareType.Uefi)).Read("dbx", ImageSecurity);

        Assert.Equal(ReadStatus.TooLarge, result.Status);
        Assert.Null(result.Variable);
        Assert.Equal(WindowsFirmwareVariableStore.MaximumReadBufferSize, result.LastBufferSize);
        Assert.NotNull(result.Error);
        Assert.Contains("exceeds the 4194304-byte buffer ceiling (last size tried 4194304)", result.Error, StringComparison.Ordinal);
        Assert.Contains("ERROR_INSUFFICIENT_BUFFER (122)", result.Error, StringComparison.Ordinal);
        Assert.Equal(
            new uint[] { 65536, 131072, 262144, 524288, 1048576, 2097152, 4194304 },
            api.ReadCalls.Select(call => call.Size).ToArray());
        Assert.All(api.ReadCalls, call => Assert.Equal(ImageSecurityText, call.Guid));
    }

    [Fact]
    public void Read_LargeVariable_IsReadAfterGrowingOnce()
    {
        byte[] data = new byte[100_000];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i * 7);
        }

        FakeNativeFirmwareApi api = new();
        api.Add("dbx", ImageSecurity, NvBsRt, data);

        ReadResult result = CreateStore(api, new StubDetector(FirmwareType.Uefi)).Read("dbx", ImageSecurity);

        Assert.Equal(ReadStatus.Success, result.Status);
        Assert.Null(result.Error);
        Assert.NotNull(result.Variable);
        Assert.Equal(data, result.Variable.Data);
        Assert.Equal(131072, result.LastBufferSize);
        Assert.Equal(new uint[] { 65536, 131072 }, api.ReadCalls.Select(call => call.Size).ToArray());
    }

    [Fact]
    public void Read_SmallVariable_SucceedsOnFirstCallWithBracedUppercaseGuid()
    {
        FakeNativeFirmwareApi api = new();
        api.Add("BootOrder", Global, NvBsRt, new byte[] { 1, 0, 2, 0 });

        ReadResult result = CreateStore(api, new StubDetector(FirmwareType.Uefi)).Read("BootOrder", Global);

        Assert.Equal(ReadStatus.Success, result.Status);
        Assert.NotNull(result.Variable);
        Assert.Equal("BootOrder", result.Variable.Name);
        Assert.Equal(Global, result.Variable.VendorGuid);
        Assert.Equal(new byte[] { 1, 0, 2, 0 }, result.Variable.Data);
        Assert.Equal((VariableAttributes)NvBsRt, result.Variable.Attributes);
        Assert.Equal(WindowsFirmwareVariableStore.InitialReadBufferSize, result.LastBufferSize);
        ReadCall call = Assert.Single(api.ReadCalls);
        Assert.Equal("{8BE4DF61-93CA-11D2-AA0D-00E098032B8C}", call.Guid);
    }

    [Theory]
    [InlineData(1, ReadStatus.NotUefi, "ERROR_INVALID_FUNCTION (1)")]
    [InlineData(203, ReadStatus.NotFound, "ERROR_ENVVAR_NOT_FOUND (203)")]
    [InlineData(1314, ReadStatus.PrivilegeNotHeld, "ERROR_PRIVILEGE_NOT_HELD (1314)")]
    [InlineData(5, ReadStatus.AccessDenied, "ERROR_ACCESS_DENIED (5)")]
    [InlineData(31, ReadStatus.Failed, "Win32 error 31 (0x0000001F)")]
    public void Read_MapsWin32Errors(int error, ReadStatus expectedStatus, string expectedText)
    {
        FakeNativeFirmwareApi api = new() { ReadErrorForAll = error };

        ReadResult result = CreateStore(api, new StubDetector(FirmwareType.Uefi)).Read("SecureBoot", Global);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Null(result.Variable);
        Assert.NotNull(result.Error);
        Assert.StartsWith(expectedText, result.Error, StringComparison.Ordinal);
        Assert.Equal(NativeError.DescribeWin32(error), result.Error);
        Assert.Equal(WindowsFirmwareVariableStore.InitialReadBufferSize, result.LastBufferSize);
        Assert.Single(api.ReadCalls);
    }

    [Fact]
    public void Read_ZeroLengthVariable_SucceedsWithAttributesAndNoData()
    {
        FakeNativeFirmwareApi api = new();
        api.Add("SetupMode", Global, NvBsRt, Array.Empty<byte>());

        ReadResult result = CreateStore(api, new StubDetector(FirmwareType.Uefi)).Read("SetupMode", Global);

        Assert.Equal(ReadStatus.Success, result.Status);
        Assert.Null(result.Error);
        Assert.NotNull(result.Variable);
        Assert.Empty(result.Variable.Data);
        Assert.Equal(0, result.Variable.Size);
        Assert.Equal(VariableAttributes.NonVolatile | VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess, result.Variable.Attributes);
        Assert.Equal(WindowsFirmwareVariableStore.InitialReadBufferSize, result.LastBufferSize);
        Assert.Single(api.ReadCalls);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Read_EmptyName_FailsWithoutNativeCall(string? name)
    {
        FakeNativeFirmwareApi api = new();

        ReadResult result = CreateStore(api, new StubDetector(FirmwareType.Uefi)).Read(name!, Global);

        Assert.Equal(ReadStatus.Failed, result.Status);
        Assert.Equal("a variable name is required", result.Error);
        Assert.Empty(api.ReadCalls);
    }

    [Fact]
    public void Read_WhenSeamThrows_ReportsFailureWithoutThrowing()
    {
        FakeNativeFirmwareApi api = new() { ReadThrows = new InvalidOperationException("bad pointer") };
        WindowsFirmwareVariableStore store = CreateStore(api, new StubDetector(FirmwareType.Uefi));

        ReadResult? result = null;
        Exception? exception = Record.Exception(() => result = store.Read("BootOrder", Global));

        Assert.Null(exception);
        Assert.NotNull(result);
        Assert.Equal(ReadStatus.Failed, result.Status);
        Assert.Contains("threw InvalidOperationException: bad pointer", result.Error, StringComparison.Ordinal);
    }

    // ---- operating-system guard --------------------------------------------------------------

    [Fact]
    public void Store_WhenSeamIsUnavailable_ReportsNotSupportedWithoutAnyCall()
    {
        FakeNativeFirmwareApi api = new() { IsAvailable = false };
        api.EnumerateSteps.Enqueue(new EnumerateStep(NativeConstants.STATUS_SUCCESS, null, 0));
        api.Add("BootOrder", Global, NvBsRt, new byte[] { 1, 0 });
        StubDetector detector = new(FirmwareType.Uefi);
        WindowsFirmwareVariableStore store = CreateStore(api, detector);

        EnumerationResult enumeration = store.Enumerate();
        ReadResult read = store.Read("BootOrder", Global);

        Assert.Equal(EnumerationMethod.None, enumeration.Method);
        Assert.Contains("not supported on this OS", enumeration.Error, StringComparison.Ordinal);
        Assert.Equal(ReadStatus.NotSupportedOnThisOs, read.Status);
        Assert.Contains("not supported on this OS", read.Error, StringComparison.Ordinal);
        Assert.Equal(0, read.LastBufferSize);
        Assert.Empty(api.EnumerateCalls);
        Assert.Empty(api.ReadCalls);
        Assert.Equal(0, detector.Calls);
    }

    [Fact]
    public void ProductionSeam_IsAvailableOnlyOnWindows()
    {
        Assert.Equal(OperatingSystem.IsWindows(), WindowsNativeFirmwareApi.Instance.IsAvailable);
        Assert.Same(WindowsNativeFirmwareApi.Instance, WindowsNativeFirmwareApi.Instance);
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static WindowsFirmwareVariableStore CreateStore(FakeNativeFirmwareApi api, StubDetector detector)
    {
        return new WindowsFirmwareVariableStore(detector, null, api);
    }

    /// <summary>One scripted answer of the fake enumeration call.</summary>
    /// <param name="Status">NTSTATUS to return.</param>
    /// <param name="Data">Bytes copied into the caller's buffer (as many as fit); null copies nothing.</param>
    /// <param name="ReportedLength">Value stored in the length argument; null reports the data length.</param>
    private sealed record EnumerateStep(uint Status, byte[]? Data, uint? ReportedLength);

    private sealed record EnumerateCall(uint InformationClass, bool HasBuffer, uint LengthIn, uint LengthOut, uint Status);

    private sealed record ReadCall(string Name, string Guid, uint Size);

    private sealed class StubDetector : IFirmwareTypeDetector
    {
        private readonly FirmwareType _type;

        public StubDetector(FirmwareType type)
        {
            _type = type;
        }

        public int Calls { get; private set; }

        public FirmwareType Detect()
        {
            Calls++;
            return _type;
        }
    }

    /// <summary>
    /// Fake seam: enumeration answers come from a script, reads come from a dictionary keyed by
    /// name and braced-uppercase GUID (a variable larger than the offered buffer answers
    /// ERROR_INSUFFICIENT_BUFFER, an unknown one ERROR_ENVVAR_NOT_FOUND), and every call is recorded.
    /// </summary>
    private sealed class FakeNativeFirmwareApi : INativeFirmwareApi
    {
        private int _lastError;

        public bool IsAvailable { get; set; } = true;

        public Queue<EnumerateStep> EnumerateSteps { get; } = new();

        public List<EnumerateCall> EnumerateCalls { get; } = new();

        public List<ReadCall> ReadCalls { get; } = new();

        public Dictionary<(string Name, string Guid), (byte[] Data, uint Attributes)> Variables { get; } = new();

        public Dictionary<string, int> ReadErrorsByName { get; } = new(StringComparer.Ordinal);

        public int? ReadErrorForAll { get; set; }

        public Exception? EnumerateThrows { get; set; }

        public Exception? ReadThrows { get; set; }

        public void Add(string name, Guid guid, uint attributes, byte[] data)
        {
            Variables[(name, KnownGuids.ToBracedUpper(guid))] = (data, attributes);
        }

        public uint EnumerateValues(uint informationClass, IntPtr buffer, ref uint length)
        {
            if (EnumerateThrows is not null)
            {
                throw EnumerateThrows;
            }

            uint lengthIn = length;
            EnumerateStep step = EnumerateSteps.Count > 0
                ? EnumerateSteps.Dequeue()
                : new EnumerateStep(NativeConstants.STATUS_INVALID_PARAMETER, null, 0);

            if (step.Data is not null && buffer != IntPtr.Zero)
            {
                int count = (int)Math.Min((uint)step.Data.Length, lengthIn);
                if (count > 0)
                {
                    Marshal.Copy(step.Data, 0, buffer, count);
                }
            }

            length = step.ReportedLength ?? (uint)(step.Data?.Length ?? 0);
            EnumerateCalls.Add(new EnumerateCall(informationClass, buffer != IntPtr.Zero, lengthIn, length, step.Status));
            return step.Status;
        }

        public uint ReadVariable(string name, string guid, IntPtr buffer, uint size, out uint attributes)
        {
            ReadCalls.Add(new ReadCall(name, guid, size));
            attributes = 0;
            if (ReadThrows is not null)
            {
                throw ReadThrows;
            }

            if (ReadErrorForAll is int forced)
            {
                _lastError = forced;
                return 0;
            }

            if (ReadErrorsByName.TryGetValue(name, out int perName))
            {
                _lastError = perName;
                return 0;
            }

            if (!Variables.TryGetValue((name, guid), out (byte[] Data, uint Attributes) variable))
            {
                _lastError = NativeConstants.ERROR_ENVVAR_NOT_FOUND;
                return 0;
            }

            if ((uint)variable.Data.Length > size)
            {
                _lastError = NativeConstants.ERROR_INSUFFICIENT_BUFFER;
                return 0;
            }

            attributes = variable.Attributes;
            _lastError = 0;
            if (variable.Data.Length > 0)
            {
                Marshal.Copy(variable.Data, 0, buffer, variable.Data.Length);
            }

            return (uint)variable.Data.Length;
        }

        public int GetLastError() => _lastError;
    }
}
