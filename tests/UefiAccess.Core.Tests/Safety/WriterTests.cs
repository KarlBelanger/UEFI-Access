// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;
using System.Runtime.InteropServices;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Variables;
using Xunit;

namespace UefiAccess.Core.Tests.Safety;

/// <summary>
/// The single call site over a fake native seam: one write call per
/// commit with exactly the plan's name, braced GUID, bytes, size and attributes; on success no
/// error read at all; on failure one read of the Win32 error and one of the thread's NTSTATUS,
/// recorded as observed and attributed to their source (with a "may be stale" remark when the
/// status is not the one behind the code); never a second call, whatever the seam answers; and
/// the plan's own rules (its bytes copied, attributes 0x7 only). That no public constructor or
/// factory builds a plan, and that the writer's interface has its one method, is pinned once, in
/// <see cref="WriteCallSiteGuardTests.CommitOnceHasExactlyOneCaller"/>. No test here constructs
/// the writer or names the Windows seam: the writer's behaviour is exercised through its static
/// core, so nothing in this file can reach native code.
/// </summary>
public sealed class WriterTests
{
    private static readonly Guid Global = KnownGuids.EfiGlobalVariable;
    private static readonly byte[] TwoEntries = { 0x02, 0x00, 0x01, 0x00 };

    [Fact]
    public void Commit_MakesExactlyOneCall_WithThePlansNameGuidBytesSizeAndAttributes()
    {
        FakeWriteApi api = new();
        WritePlan plan = new("BootOrder", Global, WritePlan.RequiredAttributes, TwoEntries);

        WriteOutcome outcome = FirmwareVariableWriter.Commit(api, plan, Serilog.Core.Logger.None);

        WriteCall call = Assert.Single(api.Calls);
        Assert.Equal("BootOrder", call.Name);
        Assert.Equal("{8BE4DF61-93CA-11D2-AA0D-00E098032B8C}", call.Guid);
        Assert.Equal(TwoEntries, call.Bytes);
        Assert.Equal(4u, call.Size);
        Assert.Equal(0x7u, call.Attributes);
        Assert.True(outcome.Succeeded);
    }

    [Fact]
    public void Commit_Success_ReadsNoErrorAndReportsNoNtStatus()
    {
        FakeWriteApi api = new();
        WritePlan plan = new("BootNext", Global, WritePlan.RequiredAttributes, new byte[] { 0x00, 0x00 });

        WriteOutcome outcome = FirmwareVariableWriter.Commit(api, plan, Serilog.Core.Logger.None);

        Assert.True(outcome.Succeeded);
        Assert.Equal(WriteStatus.Success, outcome.Status);
        Assert.Equal(0, outcome.Win32Error);
        Assert.Equal(string.Empty, outcome.Win32Name);
        Assert.Equal(string.Empty, outcome.Win32Text);
        Assert.Null(outcome.NtStatus);
        Assert.Null(outcome.NtStatusText);
        Assert.Equal(FirmwareVariableWriter.NtStatusNotObserved, outcome.NtStatusSource);
        Assert.True(outcome.Elapsed >= TimeSpan.Zero);
        Assert.Equal(0, api.LastErrorReads);
        Assert.Equal(0, api.LastNtStatusReads);
        Assert.Single(api.Calls);
    }

    [Theory]
    [InlineData(19, 0xC00000A2u, WriteStatus.WriteProtected, true)]
    [InlineData(5, 0xC0000022u, WriteStatus.AccessDenied, true)]
    [InlineData(50, 0xC00000BBu, WriteStatus.NotSupported, true)]
    [InlineData(87, 0xC000000Du, WriteStatus.InvalidParameter, true)]
    [InlineData(203, 0xC0000100u, WriteStatus.NotFound, true)]
    [InlineData(1470, 0xC0000454u, WriteStatus.NoNvramRoom, true)]
    [InlineData(8, 0xC0000017u, WriteStatus.SystemResources, true)]
    [InlineData(14, 0xC0000017u, WriteStatus.SystemResources, false)]
    [InlineData(1450, 0xC000009Au, WriteStatus.SystemResources, true)]
    [InlineData(1314, 0xC0000061u, WriteStatus.PrivilegeNotHeld, true)]
    [InlineData(1, 0xC0000002u, WriteStatus.NotImplemented, true)]
    [InlineData(998, 0xC0000001u, WriteStatus.FirmwareError, false)]
    [InlineData(31, 0xC0000001u, WriteStatus.FirmwareError, true)]
    [InlineData(1117, 0xC0000185u, WriteStatus.FirmwareError, true)]
    [InlineData(4242, 0xC00000FFu, WriteStatus.FirmwareError, false)]
    public void Commit_Failure_ReadsTheWin32ErrorAndTheNtStatusOnceEach_AndNeverCallsAgain(int error, uint ntStatus, WriteStatus expected, bool statusBelongsToTheCode)
    {
        FakeWriteApi api = new() { Error = error, NtStatus = ntStatus };
        WritePlan plan = new("BootOrder", Global, WritePlan.RequiredAttributes, TwoEntries);

        WriteOutcome outcome = FirmwareVariableWriter.Commit(api, plan, Serilog.Core.Logger.None);

        Assert.Single(api.Calls);
        Assert.Equal(1, api.LastErrorReads);
        Assert.Equal(1, api.LastNtStatusReads);
        Assert.False(outcome.Succeeded);
        Assert.Equal(expected, outcome.Status);
        Assert.Equal(error, outcome.Win32Error);
        Assert.Equal(NativeError.Win32Name(error), outcome.Win32Name);
        Assert.Equal(NativeError.Win32WriteText(error), outcome.Win32Text);
        Assert.Equal(ntStatus, outcome.NtStatus);
        Assert.Equal(NativeError.DescribeWriteNtStatus(ntStatus), outcome.NtStatusText);
        Assert.Equal(statusBelongsToTheCode ? "RtlGetLastNtStatus" : "RtlGetLastNtStatus; may be stale: not known to be the status behind error " + error, outcome.NtStatusSource);
    }

    [Fact]
    public void Commit_RecordsTheNtStatusAsRead_NeverDerivedFromTheWin32Code()
    {
        // The seam answers error 19 with an NTSTATUS that does not correspond to it (the thread's
        // last status is not cleared before the call, so it may be left over from an earlier
        // failure): the outcome keeps the value as read, says which read produced it, and says it
        // may be stale; the class still comes from the Win32 code alone.
        FakeWriteApi api = new() { Error = 19, NtStatus = 0xC0000022 };
        WritePlan plan = new("BootOrder", Global, WritePlan.RequiredAttributes, TwoEntries);

        WriteOutcome outcome = FirmwareVariableWriter.Commit(api, plan, Serilog.Core.Logger.None);

        Assert.Equal(0xC0000022u, outcome.NtStatus);
        Assert.Equal("RtlGetLastNtStatus; may be stale: not known to be the status behind error 19", outcome.NtStatusSource);
        Assert.Equal(WriteStatus.WriteProtected, outcome.Status);
    }

    [Fact]
    public void Commit_ADeletionPassesSizeZero_InOneCall()
    {
        FakeWriteApi api = new();
        WritePlan plan = new("BootNext", Global, WritePlan.RequiredAttributes, Array.Empty<byte>());

        WriteOutcome outcome = FirmwareVariableWriter.Commit(api, plan, Serilog.Core.Logger.None);

        WriteCall call = Assert.Single(api.Calls);
        Assert.Equal(0u, call.Size);
        Assert.Empty(call.Bytes);
        Assert.Equal("BootNext", call.Name);
        Assert.True(outcome.Succeeded);
    }

    [Fact]
    public void Commit_TwoCommitsAreTwoCalls_NeverMoreForOne()
    {
        FakeWriteApi api = new() { Error = 1470, NtStatus = 0xC0000454 };
        WritePlan plan = new("BootOrder", Global, WritePlan.RequiredAttributes, TwoEntries);

        WriteOutcome first = FirmwareVariableWriter.Commit(api, plan, Serilog.Core.Logger.None);
        Assert.Single(api.Calls);
        Assert.Equal(WriteStatus.NoNvramRoom, first.Status);

        WriteOutcome second = FirmwareVariableWriter.Commit(api, plan, Serilog.Core.Logger.None);
        Assert.Equal(2, api.Calls.Count);
        Assert.Equal(WriteStatus.NoNvramRoom, second.Status);
    }

    [Fact]
    public void Commit_ThrowsWhatTheSeamThrows_AfterExactlyOneCall()
    {
        FakeWriteApi api = new() { Throws = new InvalidOperationException("seam failed") };
        WritePlan plan = new("BootOrder", Global, WritePlan.RequiredAttributes, TwoEntries);

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => FirmwareVariableWriter.Commit(api, plan, Serilog.Core.Logger.None));

        Assert.Equal("seam failed", thrown.Message);
        Assert.Single(api.Calls);
        Assert.Equal(0, api.LastErrorReads);
        Assert.Equal(0, api.LastNtStatusReads);
    }

    [Fact]
    public void Commit_RefusesANullPlanBeforeAnyCall()
    {
        FakeWriteApi api = new();

        Assert.Throws<ArgumentNullException>(() => FirmwareVariableWriter.Commit(api, null!, Serilog.Core.Logger.None));
        Assert.Empty(api.Calls);
    }

    // ----- the plan -----

    [Fact]
    public void WritePlan_CopiesItsBytes_SoNeitherSideCanChangeTheOther()
    {
        byte[] input = { 0x02, 0x00, 0x01, 0x00 };
        WritePlan plan = new("BootOrder", Global, WritePlan.RequiredAttributes, input);
        input[0] = 0xFF;

        byte[] first = plan.Bytes;
        first[1] = 0xEE;

        Assert.Equal(new byte[] { 0x02, 0x00, 0x01, 0x00 }, plan.Bytes);
        Assert.Equal(4u, plan.Size);
        Assert.Equal("BootOrder", plan.Name);
        Assert.Equal(Global, plan.Guid);
        Assert.Equal(0x7u, plan.Attributes);
        Assert.Equal("8be4df61-93ca-11d2-aa0d-00e098032b8c:BootOrder", plan.Key);
    }

    [Theory]
    [InlineData(0x3u)]
    [InlineData(0x6u)]
    [InlineData(0x27u)]
    [InlineData(0x17u)]
    [InlineData(0x0u)]
    public void WritePlan_RefusesAttributesOtherThanNvBsRt(uint attributes)
    {
        ArgumentException thrown = Assert.Throws<ArgumentException>(() => new WritePlan("BootOrder", Global, attributes, TwoEntries));

        Assert.Equal("attributes", thrown.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void WritePlan_RefusesAnEmptyName(string? name)
    {
        Assert.Throws<ArgumentException>(() => new WritePlan(name!, Global, WritePlan.RequiredAttributes, TwoEntries));
    }

    [Fact]
    public void WritePlan_RefusesNullBytes()
    {
        Assert.Throws<ArgumentNullException>(() => new WritePlan("BootOrder", Global, WritePlan.RequiredAttributes, null!));
    }

    [Fact]
    public void TheWindowsSeam_ExposesOnlyItsInstance_AndIsTheOnlySeamAWriterAccepts()
    {
        // The product's one implementation of the write seam, found through the interface so this
        // file never names it. Its members are explicit interface implementations, so nothing but
        // the shared instance is callable on the public surface; the instance itself is never
        // touched here.
        Type seam = Assert.Single(
            typeof(INativeFirmwareWriteApi).Assembly.GetTypes(),
            type => type.IsClass && typeof(INativeFirmwareWriteApi).IsAssignableFrom(type));

        Assert.True(seam.IsPublic);
        Assert.True(seam.IsSealed);
        Assert.Empty(seam.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        string[] members = seam.GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(member => member.MemberType is MemberTypes.Method or MemberTypes.Property)
            .Select(member => member.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[] { "Instance", "get_Instance" }, members);

        // The writer's only public constructor takes that type, not the interface: no fake can be
        // handed to a production writer, and a test cannot build one without naming the real seam.
        ConstructorInfo constructor = Assert.Single(typeof(FirmwareVariableWriter).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Equal(seam, constructor.GetParameters()[0].ParameterType);
        Assert.Empty(typeof(FirmwareVariableWriter).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance));
    }

    [Fact]
    public void TheWriterSeam_IsInternal_AndExposesTheWriteAndTheTwoErrorReadsOnly()
    {
        Assert.False(typeof(INativeFirmwareWriteApi).IsPublic);
        string[] methods = typeof(INativeFirmwareWriteApi).GetMethods().Select(method => method.Name).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(new[] { "GetLastError", "GetLastNtStatus", "Write" }, methods);
        Assert.Empty(typeof(INativeFirmwareWriteApi).GetProperties());
    }

    // ----- helpers -----

    /// <summary>One recorded write: the arguments as the seam received them, the bytes copied out of the buffer.</summary>
    private sealed record WriteCall(string Name, string Guid, byte[] Bytes, uint Size, uint Attributes);

    /// <summary>
    /// Fake seam: succeeds unless <see cref="Error"/> is set, in which case the write fails with
    /// that Win32 code and <see cref="NtStatus"/> as the thread's status; records every call and
    /// counts the error reads.
    /// </summary>
    private sealed class FakeWriteApi : INativeFirmwareWriteApi
    {
        public int Error { get; init; }

        public uint NtStatus { get; init; }

        public Exception? Throws { get; init; }

        public List<WriteCall> Calls { get; } = new();

        public int LastErrorReads { get; private set; }

        public int LastNtStatusReads { get; private set; }

        public bool Write(string name, string guid, IntPtr buffer, uint size, uint attributes)
        {
            byte[] bytes = new byte[size];
            if (size > 0)
            {
                Marshal.Copy(buffer, bytes, 0, (int)size);
            }

            Calls.Add(new WriteCall(name, guid, bytes, size, attributes));
            if (Throws is not null)
            {
                throw Throws;
            }

            return Error == 0;
        }

        public int GetLastError()
        {
            LastErrorReads++;
            return Error;
        }

        public uint GetLastNtStatus()
        {
            LastNtStatusReads++;
            return NtStatus;
        }
    }
}
