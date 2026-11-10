// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Safety;
using Xunit;

namespace UefiAccess.Core.Tests.Safety;

/// <summary>
/// The one-restart suspend of stage S13 over a fake volume seam: the
/// restart count 1 is always passed, the suspend is called exactly once whatever happens, a
/// nonzero return blocks with the HRESULT, a return of 0 must be confirmed by a re-read (the
/// protection status for On and Unknown, the protector list for Encrypting), a seam that never
/// answers gives the timeout outcome with the state Unknown, and a failure before the call leaves
/// the call unmade. No test here constructs the suspender: its behaviour is exercised through its
/// static core, so nothing in this file can reach WMI.
/// </summary>
public sealed class BitLockerSuspenderTests
{
    private const string Drive = "C:";

    /// <summary>The bound a test gives each seam call when the seam is made to hang.</summary>
    private static readonly TimeSpan ShortBound = TimeSpan.FromMilliseconds(200);

    [Fact]
    public void On_SuspendsOnce_WithRestartCountOne_AndConfirmsWithTheStatusReRead()
    {
        FakeVolume volume = new();
        volume.Statuses.Enqueue(1);
        volume.Statuses.Enqueue(0);

        SuspendResult result = BitLockerSuspender.Suspend(volume, Drive, Serilog.Core.Logger.None);

        Assert.True(result.Succeeded);
        Assert.True(result.Called);
        Assert.False(result.TimedOut);
        Assert.Equal(1u, result.DisableCount);
        Assert.Equal(0u, result.ReturnValue);
        Assert.Equal(1, result.ProtectionStatusBefore);
        Assert.Equal(0, result.ProtectionStatusAfter);
        Assert.Equal(BitLockerState.Suspended, result.StateAfter);
        Assert.Equal("0", result.ResultText);
        Assert.Null(result.Sentence);
        Assert.Equal(new[] { "ReadProtectionStatus", "Suspend 1", "ReadProtectionStatus" }, volume.Calls);
        Assert.Equal(new uint[] { 1 }, volume.DisableCounts);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(null)]
    public void Unknown_IsConfirmedLikeOn(int? before)
    {
        // Status 2, or a status the volume did not report: the suspend must turn protection off.
        FakeVolume volume = new();
        volume.Statuses.Enqueue(before);
        volume.Statuses.Enqueue(0);

        SuspendResult result = BitLockerSuspender.Suspend(volume, Drive, Serilog.Core.Logger.None);

        Assert.True(result.Succeeded);
        Assert.Equal(before, result.ProtectionStatusBefore);
        Assert.Equal(0, result.ProtectionStatusAfter);
        Assert.Equal(new[] { "ReadProtectionStatus", "Suspend 1", "ReadProtectionStatus" }, volume.Calls);
        Assert.Equal(new uint[] { 1 }, volume.DisableCounts);
    }

    [Theory]
    [InlineData(1, "1")]
    [InlineData(2, "2")]
    [InlineData(null, "unreadable")]
    public void AReturnOfZero_WhoseReReadDoesNotShowProtectionOff_Blocks(int? after, string shown)
    {
        FakeVolume volume = new();
        volume.Statuses.Enqueue(1);
        volume.Statuses.Enqueue(after);

        SuspendResult result = BitLockerSuspender.Suspend(volume, Drive, Serilog.Core.Logger.None);

        Assert.False(result.Succeeded);
        Assert.True(result.Called);
        Assert.Equal(0u, result.ReturnValue);
        Assert.Equal("0", result.ResultText);
        Assert.Equal(after, result.ProtectionStatusAfter);
        Assert.Equal(BitLockerState.Unknown, result.StateAfter);
        Assert.Equal("BitLocker could not be suspended (protection status " + shown + " after the call). Nothing was written.", result.Sentence);
        Assert.Equal(new uint[] { 1 }, volume.DisableCounts);
    }

    [Fact]
    public void ANonZeroReturn_Blocks_WithTheHresult_AndMakesNoReRead()
    {
        FakeVolume volume = new() { SuspendReturnValue = 0x80310000 };
        volume.Statuses.Enqueue(1);

        SuspendResult result = BitLockerSuspender.Suspend(volume, Drive, Serilog.Core.Logger.None);

        Assert.False(result.Succeeded);
        Assert.True(result.Called);
        Assert.False(result.TimedOut);
        Assert.Equal(0x80310000u, result.ReturnValue);
        Assert.Equal("0x80310000", result.ResultText);
        Assert.Null(result.ProtectionStatusAfter);
        Assert.Equal(BitLockerState.Unknown, result.StateAfter);
        Assert.Equal("BitLocker could not be suspended (0x80310000). Nothing was written.", result.Sentence);
        Assert.Equal(CheckSentences.Format(CheckSentences.BitLockerSuspendFailedFormat, 0x80310000u), result.Sentence);
        Assert.Equal(new[] { "ReadProtectionStatus", "Suspend 1" }, volume.Calls);
    }

    [Fact]
    public void ASeamThatNeverReturns_GivesTheTimeoutOutcome_WithTheStateUnknown()
    {
        // The suspend call hangs: the core's own bound gives up on it, reports that it may still
        // complete, and makes no further call, the re-read included.
        using ManualResetEventSlim release = new(false);
        FakeVolume volume = new() { HangSuspendUntil = release };
        volume.Statuses.Enqueue(1);

        SuspendResult result;
        try
        {
            result = BitLockerSuspender.Suspend(volume, Drive, Serilog.Core.Logger.None, ShortBound);
            Assert.True(volume.SuspendEntered.Wait(TimeSpan.FromSeconds(30)), "the suspend call was never made");
        }
        finally
        {
            release.Set();
        }

        Assert.False(result.Succeeded);
        Assert.True(result.Called);
        Assert.True(result.TimedOut);
        Assert.Null(result.ReturnValue);
        Assert.Equal(1u, result.DisableCount);
        Assert.Equal(BitLockerState.Unknown, result.StateAfter);
        Assert.Equal("timeout", result.ResultText);
        Assert.Equal(CheckSentences.BitLockerSuspendTimedOut, result.Sentence);
        Assert.Equal("The BitLocker suspend did not answer within 60 seconds and may still complete; check BitLocker's state in Windows before restarting. Nothing was written.", result.Sentence);
        Assert.Equal(new[] { "ReadProtectionStatus", "Suspend 1" }, volume.Calls);
        Assert.Equal(new uint[] { 1 }, volume.DisableCounts);
    }

    [Fact]
    public void ASeamWhoseOwnBoundExpires_GivesTheTimeoutOutcome_Too()
    {
        // The WMI implementation's bound throws TimeoutException from inside the seam.
        FakeVolume volume = new() { SuspendThrows = new TimeoutException("the seam's bound expired") };
        volume.Statuses.Enqueue(1);

        SuspendResult result = BitLockerSuspender.Suspend(volume, Drive, Serilog.Core.Logger.None);

        Assert.True(result.TimedOut);
        Assert.True(result.Called);
        Assert.Equal(BitLockerState.Unknown, result.StateAfter);
        Assert.Equal("timeout", result.ResultText);
        Assert.Equal(CheckSentences.BitLockerSuspendTimedOut, result.Sentence);
        Assert.Equal(new[] { "ReadProtectionStatus", "Suspend 1" }, volume.Calls);
    }

    [Fact]
    public void ASeamThatThrows_IsAFailedCall_NotATimeout()
    {
        FakeVolume volume = new() { SuspendThrows = new InvalidOperationException("Generic failure") };
        volume.Statuses.Enqueue(1);

        SuspendResult result = BitLockerSuspender.Suspend(volume, Drive, Serilog.Core.Logger.None);

        Assert.False(result.Succeeded);
        Assert.True(result.Called);
        Assert.False(result.TimedOut);
        Assert.Null(result.ReturnValue);
        Assert.Equal("failed", result.ResultText);
        Assert.Equal(BitLockerState.Unknown, result.StateAfter);
        Assert.Equal("BitLocker could not be suspended (InvalidOperationException: Generic failure). Nothing was written.", result.Sentence);
        Assert.Equal(new uint[] { 1 }, volume.DisableCounts);
        Assert.Equal(new[] { "ReadProtectionStatus", "Suspend 1" }, volume.Calls);
    }

    [Fact]
    public void Encrypting_ConfirmsWithTheProtectorList_InAnyOrder()
    {
        FakeVolume volume = new();
        volume.Statuses.Enqueue(0);
        volume.ProtectorLists.Enqueue(new[] { "{tpm}", "{password}" });
        volume.ProtectorLists.Enqueue(new[] { "{password}", "{tpm}" });

        SuspendResult result = BitLockerSuspender.Suspend(volume, Drive, Serilog.Core.Logger.None);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.ProtectionStatusBefore);
        Assert.Null(result.ProtectionStatusAfter);
        Assert.Equal(BitLockerState.Suspended, result.StateAfter);
        Assert.Equal("0", result.ResultText);
        Assert.Null(result.Sentence);
        Assert.Equal(new[] { "ReadProtectionStatus", "GetKeyProtectors", "Suspend 1", "GetKeyProtectors" }, volume.Calls);
        Assert.Equal(new uint[] { 1 }, volume.DisableCounts);
    }

    [Fact]
    public void Encrypting_AChangedProtectorList_Blocks()
    {
        FakeVolume volume = new();
        volume.Statuses.Enqueue(0);
        volume.ProtectorLists.Enqueue(new[] { "{tpm}", "{password}" });
        volume.ProtectorLists.Enqueue(new[] { "{tpm}" });

        SuspendResult result = BitLockerSuspender.Suspend(volume, Drive, Serilog.Core.Logger.None);

        Assert.False(result.Succeeded);
        Assert.True(result.Called);
        Assert.Equal(0u, result.ReturnValue);
        Assert.Equal("0", result.ResultText);
        Assert.Equal(BitLockerState.Unknown, result.StateAfter);
        Assert.Equal("BitLocker could not be suspended (the key protector list changed after the call). Nothing was written.", result.Sentence);
        Assert.Equal(new uint[] { 1 }, volume.DisableCounts);
    }

    [Fact]
    public void Encrypting_AProtectorListReturningAnErrorAfterTheCall_Blocks()
    {
        FakeVolume volume = new();
        volume.Statuses.Enqueue(0);
        volume.ProtectorLists.Enqueue(new[] { "{tpm}" });
        volume.ProtectorReturnValues.Enqueue(0);
        volume.ProtectorReturnValues.Enqueue(0x80070005);

        SuspendResult result = BitLockerSuspender.Suspend(volume, Drive, Serilog.Core.Logger.None);

        Assert.False(result.Succeeded);
        Assert.True(result.Called);
        Assert.Equal(BitLockerState.Unknown, result.StateAfter);
        Assert.Equal("BitLocker could not be suspended (the call returned 0 but the key protectors could not be listed again: 0x80070005). Nothing was written.", result.Sentence);
        Assert.Equal(new uint[] { 1 }, volume.DisableCounts);
    }

    [Fact]
    public void Encrypting_AFailedProtectorListBeforeTheCall_LeavesTheCallUnmade()
    {
        FakeVolume volume = new();
        volume.Statuses.Enqueue(0);
        volume.ProtectorReturnValues.Enqueue(0x80310008);

        SuspendResult result = BitLockerSuspender.Suspend(volume, Drive, Serilog.Core.Logger.None);

        Assert.False(result.Succeeded);
        Assert.False(result.Called);
        Assert.Null(result.ReturnValue);
        Assert.Equal("not called", result.ResultText);
        Assert.Equal(BitLockerState.Unknown, result.StateAfter);
        Assert.Equal("BitLocker could not be suspended (the key protectors could not be listed before the call: 0x80310008). Nothing was written.", result.Sentence);
        Assert.Empty(volume.DisableCounts);
        Assert.Equal(new[] { "ReadProtectionStatus", "GetKeyProtectors" }, volume.Calls);
    }

    [Fact]
    public void ProtectionOff_WithNoProtector_LeavesTheCallUnmade()
    {
        FakeVolume volume = new();
        volume.Statuses.Enqueue(0);
        volume.ProtectorLists.Enqueue(Array.Empty<string>());

        SuspendResult result = BitLockerSuspender.Suspend(volume, Drive, Serilog.Core.Logger.None);

        Assert.False(result.Succeeded);
        Assert.False(result.Called);
        Assert.Equal("BitLocker could not be suspended (the drive has no key protector to suspend). Nothing was written.", result.Sentence);
        Assert.Empty(volume.DisableCounts);
    }

    [Fact]
    public void AStatusReadThatFailsBeforeTheCall_LeavesTheCallUnmade()
    {
        FakeVolume volume = new() { ReadThrows = new TimeoutException("the status read did not answer") };

        SuspendResult result = BitLockerSuspender.Suspend(volume, Drive, Serilog.Core.Logger.None);

        Assert.False(result.Succeeded);
        Assert.False(result.Called);
        Assert.False(result.TimedOut);
        Assert.Null(result.ProtectionStatusBefore);
        Assert.Equal("not called", result.ResultText);
        Assert.Equal(BitLockerState.Unknown, result.StateAfter);
        Assert.Equal("BitLocker could not be suspended (the state could not be read before the call: TimeoutException: the status read did not answer). Nothing was written.", result.Sentence);
        Assert.Equal(new[] { "ReadProtectionStatus" }, volume.Calls);
        Assert.Empty(volume.DisableCounts);
    }

    [Fact]
    public void AConfirmationReadThatFails_BlocksWithTheReturnOfZeroRecorded()
    {
        FakeVolume volume = new();
        volume.Statuses.Enqueue(1);
        volume.Failures["ReadProtectionStatus 2"] = new TimeoutException("late");

        SuspendResult result = BitLockerSuspender.Suspend(volume, Drive, Serilog.Core.Logger.None);

        Assert.False(result.Succeeded);
        Assert.True(result.Called);
        Assert.False(result.TimedOut);
        Assert.Equal(0u, result.ReturnValue);
        Assert.Equal("0", result.ResultText);
        Assert.Null(result.ProtectionStatusAfter);
        Assert.Equal(BitLockerState.Unknown, result.StateAfter);
        Assert.Equal("BitLocker could not be suspended (the call returned 0 but the confirmation read failed: TimeoutException: late). Nothing was written.", result.Sentence);
        Assert.Equal(new uint[] { 1 }, volume.DisableCounts);
    }

    [Fact]
    public void AMissingReturnValue_IsAFailureForTheSuspend_AndSuccessOnlyForTheReadOnlyMethods()
    {
        // The one helper both the inspector's three readers and the suspend read ReturnValue through.
        Assert.Equal(uint.MaxValue, BitLockerSuspender.MissingReturnValue);
        Assert.Equal(BitLockerSuspender.MissingReturnValue, WmiValues.ToReturnValue(null, BitLockerSuspender.MissingReturnValue));
        Assert.Equal(0u, WmiValues.ToReturnValue(null, 0u));
        Assert.Equal(0u, WmiValues.ToReturnValue(0u, BitLockerSuspender.MissingReturnValue));
        Assert.Equal(0x80310000u, WmiValues.ToReturnValue(0x80310000u, BitLockerSuspender.MissingReturnValue));
        Assert.Equal(uint.MaxValue, WmiValues.ToReturnValue("not a number", 0u));
        Assert.Equal(uint.MaxValue, WmiValues.ToReturnValue(-1, 0u));
    }

    [Fact]
    public void TheRestartCountIsOne_AndTheMethodNameIsPinned()
    {
        Assert.Equal(1u, BitLockerSuspender.OneRestart);

        FieldInfo method = typeof(BitLockerSuspender).GetField("Disable" + "KeyProtectorsMethod", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.True(method.IsLiteral);
        Assert.Equal("Disable" + "KeyProtectors", method.GetRawConstantValue());

        FieldInfo count = typeof(BitLockerSuspender).GetField("OneRestart", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.True(count.IsLiteral);
        Assert.Equal(1u, count.GetRawConstantValue());
    }

    [Fact]
    public void TheSentencesAreTheDesignsSentences()
    {
        Assert.Equal("BitLocker could not be suspended ({0}). Nothing was written.", BitLockerSuspender.CouldNotSuspendFormat);
        Assert.Equal(
            CheckSentences.Format(CheckSentences.BitLockerSuspendFailedFormat, 0x80310000u),
            string.Format(BitLockerSuspender.CouldNotSuspendFormat, "0x80310000"));
        Assert.True(CheckSentences.BitLockerSuspendTimedOut.Length < 200);
        Assert.Equal("protection status {0} after the call", BitLockerSuspender.ProtectionStatusAfterReasonFormat);
    }

    [Theory]
    [InlineData("C:", true)]
    [InlineData("d:", true)]
    [InlineData("C", false)]
    [InlineData("C:\\", false)]
    [InlineData("", false)]
    [InlineData("1:", false)]
    [InlineData("C:'", false)]
    public void IsDriveLetter_AcceptsOnlyALetterAndAColon(string text, bool expected)
    {
        Assert.Equal(expected, BitLockerSuspender.IsDriveLetter(text));
    }

    [Fact]
    public void IBitLockerSuspender_ExposesExactlyOneMethod()
    {
        MethodInfo method = Assert.Single(typeof(IBitLockerSuspender).GetMethods());

        Assert.Equal("SuspendForOneRestart", method.Name);
        Assert.Equal(typeof(SuspendResult), method.ReturnType);
        ParameterInfo parameter = Assert.Single(method.GetParameters());
        Assert.Equal(typeof(string), parameter.ParameterType);
        Assert.Empty(typeof(IBitLockerSuspender).GetProperties());
    }

    /// <summary>
    /// One volume's methods, recorded: the protection status reads come from a queue (an empty
    /// queue reads null), the protector lists and their return values likewise (an empty queue
    /// reads an empty list and 0), and the suspend answers a fixed ReturnValue, throws, or hangs
    /// until released. Every call is recorded, the suspend with its restart count. The core makes
    /// one call at a time (the suspend on a pool thread, under its bound), so the lists need no
    /// lock; a test that makes the suspend hang reads them only after <see cref="SuspendEntered"/>.
    /// </summary>
    private sealed class FakeVolume : IEncryptableVolumeSuspendMethods
    {
        private int _statusReads;

        public Queue<int?> Statuses { get; } = new();

        public Queue<IReadOnlyList<string>> ProtectorLists { get; } = new();

        public Queue<uint> ProtectorReturnValues { get; } = new();

        public uint SuspendReturnValue { get; init; }

        public Exception? SuspendThrows { get; init; }

        public Exception? ReadThrows { get; init; }

        /// <summary>When set, the suspend waits for it (at most 30 seconds) before it answers.</summary>
        public ManualResetEventSlim? HangSuspendUntil { get; init; }

        /// <summary>Set when the suspend call has been entered and recorded.</summary>
        public ManualResetEventSlim SuspendEntered { get; } = new(false);

        /// <summary>Exceptions to throw, keyed by the call name with its ordinal among the same calls ("ReadProtectionStatus 2").</summary>
        public Dictionary<string, Exception> Failures { get; } = new(StringComparer.Ordinal);

        public List<string> Calls { get; } = new();

        public List<uint> DisableCounts { get; } = new();

        public int? ReadProtectionStatus()
        {
            Calls.Add("ReadProtectionStatus");
            _statusReads++;
            if (ReadThrows is not null)
            {
                throw ReadThrows;
            }

            if (Failures.TryGetValue("ReadProtectionStatus " + _statusReads, out Exception? failure))
            {
                throw failure;
            }

            return Statuses.Count > 0 ? Statuses.Dequeue() : null;
        }

        public VolumeMethodResult<IReadOnlyList<string>> GetKeyProtectors()
        {
            Calls.Add("GetKeyProtectors");
            IReadOnlyList<string> ids = ProtectorLists.Count > 0 ? ProtectorLists.Dequeue() : Array.Empty<string>();
            uint returnValue = ProtectorReturnValues.Count > 0 ? ProtectorReturnValues.Dequeue() : 0u;
            return new VolumeMethodResult<IReadOnlyList<string>>(returnValue, returnValue == 0 ? ids : Array.Empty<string>());
        }

        public uint SuspendKeyProtectors(uint disableCount)
        {
            Calls.Add("Suspend " + disableCount);
            DisableCounts.Add(disableCount);
            SuspendEntered.Set();
            HangSuspendUntil?.Wait(TimeSpan.FromSeconds(30));
            if (SuspendThrows is not null)
            {
                throw SuspendThrows;
            }

            return SuspendReturnValue;
        }
    }
}
