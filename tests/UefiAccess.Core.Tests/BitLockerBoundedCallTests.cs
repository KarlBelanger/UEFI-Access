// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Management;
using UefiAccess.Core.Platform;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// Finding R10: every WMI round trip of the BitLocker inspector is bounded, and a stuck provider
/// costs one bound, not one per call. The key protectors are read through
/// <see cref="IEncryptableVolumeMethods"/> (the WMI implementation runs GetMethodParameters and
/// InvokeMethod together inside WmiHelper.RunBounded, which <see cref="WmiCallsAreBoundedTests"/>
/// checks in the source); here a recorded fake stands in for one volume, so nothing touches WMI.
/// After one call times out, no further call is made, on that volume or the next, and each call
/// that would have followed is reported as not called. A result that WmiHelper.RunBounded gave up
/// on is handed to its release callback when it arrives late, so the WMI objects it holds are not
/// left to the finalizer.
/// </summary>
public sealed class BitLockerBoundedCallTests
{
    private const uint FveNotActivated = 0x80310008;

    [Fact]
    public void ReadProtectors_ReadsEveryProtectorType_AndTheFirstTpmProfile()
    {
        FakeVolume volume = new();
        volume.Protectors = new[] { "{tpm}", "{password}", "{tpm-pin}" };
        volume.Types["{tpm}"] = 1;
        volume.Types["{password}"] = 3;
        volume.Types["{tpm-pin}"] = 4;
        volume.Profiles["{tpm}"] = new[] { 11, 7 };
        string? timedOut = null;
        List<string> errors = new();

        (IReadOnlyList<string> types, IReadOnlyList<int> pcrs) = new BitLockerInspector().ReadProtectors(volume, "C:", ref timedOut, errors);

        Assert.Equal(new[] { "TPM", "Numerical password", "TPM+PIN" }, types);
        Assert.Equal(new[] { 11, 7 }, pcrs);
        Assert.Empty(errors);
        Assert.Null(timedOut);
        Assert.Equal(new[] { "GetKeyProtectors", "GetKeyProtectorType {tpm}", "GetKeyProtectorPlatformValidationProfile {tpm}", "GetKeyProtectorType {password}", "GetKeyProtectorType {tpm-pin}" }, volume.Calls);
    }

    [Fact]
    public void ReadProtectors_BitLockerOff_IsNoError_AndMakesNoFurtherCall()
    {
        FakeVolume volume = new() { ProtectorsReturnValue = FveNotActivated };
        string? timedOut = null;
        List<string> errors = new();

        (IReadOnlyList<string> types, IReadOnlyList<int> pcrs) = new BitLockerInspector().ReadProtectors(volume, "D:", ref timedOut, errors);

        Assert.Empty(types);
        Assert.Empty(pcrs);
        Assert.Empty(errors);
        Assert.Equal(new[] { "GetKeyProtectors" }, volume.Calls);
    }

    [Fact]
    public void ReadProtectors_ACallThatFails_IsReported_AndTheNextCallsAreStillMade()
    {
        FakeVolume volume = new();
        volume.Protectors = new[] { "{a}", "{b}" };
        volume.Types["{b}"] = 1;
        volume.Failures["GetKeyProtectorType {a}"] = new InvalidOperationException("Generic failure");
        string? timedOut = null;
        List<string> errors = new();

        (IReadOnlyList<string> types, _) = new BitLockerInspector().ReadProtectors(volume, "C:", ref timedOut, errors);

        Assert.Null(timedOut);
        Assert.Equal(new[] { "Unknown", "TPM" }, types);
        Assert.Equal(new[] { "GetKeyProtectorType on C:: InvalidOperationException: Generic failure" }, errors);
        Assert.Contains("GetKeyProtectorType {b}", volume.Calls);
    }

    [Fact]
    public void ReadProtectors_AfterATimeout_MakesNoFurtherCall_OnThisVolumeOrTheNext()
    {
        FakeVolume c = new();
        c.Protectors = new[] { "{a}", "{b}" };
        c.Failures["GetKeyProtectorType {a}"] = new TimeoutException("WMI GetKeyProtectorType on C: did not complete within 60 seconds");
        FakeVolume d = new();
        d.Protectors = new[] { "{d}" };
        BitLockerInspector inspector = new();
        string? timedOut = null;
        List<string> errors = new();

        (IReadOnlyList<string> cTypes, _) = inspector.ReadProtectors(c, "C:", ref timedOut, errors);
        (IReadOnlyList<string> dTypes, _) = inspector.ReadProtectors(d, "D:", ref timedOut, errors);

        Assert.Equal("GetKeyProtectorType on C:", timedOut);
        Assert.Equal(new[] { "GetKeyProtectors", "GetKeyProtectorType {a}" }, c.Calls);
        Assert.Empty(d.Calls);
        Assert.Equal(new[] { "Unknown", "Unknown" }, cTypes);
        Assert.Empty(dTypes);
        Assert.Equal(
            new[]
            {
                "GetKeyProtectorType on C:: TimeoutException: WMI GetKeyProtectorType on C: did not complete within 60 seconds",
                "GetKeyProtectorType on C:: not called, because GetKeyProtectorType on C: timed out",
                "GetKeyProtectors on D:: not called, because GetKeyProtectorType on C: timed out",
            },
            errors);
    }

    [Fact]
    public void ReadProtectors_ATimedOutProfile_StopsTheRemainingTypeReads()
    {
        FakeVolume volume = new();
        volume.Protectors = new[] { "{tpm}", "{password}" };
        volume.Types["{tpm}"] = 1;
        volume.Types["{password}"] = 3;
        volume.Failures["GetKeyProtectorPlatformValidationProfile {tpm}"] = new TimeoutException("late");
        string? timedOut = null;
        List<string> errors = new();

        (IReadOnlyList<string> types, IReadOnlyList<int> pcrs) = new BitLockerInspector().ReadProtectors(volume, "C:", ref timedOut, errors);

        Assert.Equal("GetKeyProtectorPlatformValidationProfile on C:", timedOut);
        Assert.Equal(new[] { "TPM", "Unknown" }, types);
        Assert.Empty(pcrs);
        Assert.Equal(new[] { "GetKeyProtectors", "GetKeyProtectorType {tpm}", "GetKeyProtectorPlatformValidationProfile {tpm}" }, volume.Calls);
        Assert.Equal("GetKeyProtectorType on C:: not called, because GetKeyProtectorPlatformValidationProfile on C: timed out", errors[^1]);
    }

    [Fact]
    public void ReadProtectors_NonZeroReturnValues_AreReported()
    {
        FakeVolume volume = new() { ProtectorsReturnValue = 0x80070005 };
        string? timedOut = null;
        List<string> errors = new();

        new BitLockerInspector().ReadProtectors(volume, "C:", ref timedOut, errors);

        Assert.Equal(new[] { "GetKeyProtectors on C: returned 0x80070005" }, errors);
    }

    // ----- WmiHelper.RunBounded with a release for late results -----

    [Fact]
    public void RunBounded_ALateResult_IsReleased_WhenItArrives()
    {
        using ManualResetEventSlim finish = new(false);
        using ManualResetEventSlim released = new(false);
        List<string> releasedResults = new();
        List<string> late = new() { "volume C:", "volume D:" };

        TimeoutException thrown = Assert.Throws<TimeoutException>(() => WmiHelper.RunBounded(
            () =>
            {
                finish.Wait(TimeSpan.FromSeconds(30));
                return late;
            },
            TimeSpan.FromMilliseconds(50),
            "query Win32_EncryptableVolume",
            result =>
            {
                lock (releasedResults)
                {
                    releasedResults.AddRange(result);
                }

                released.Set();
            }));
        finish.Set();

        Assert.Equal("WMI query Win32_EncryptableVolume did not complete within 0 seconds", thrown.Message);
        Assert.True(released.Wait(TimeSpan.FromSeconds(30)), "the late result was never released");
        lock (releasedResults)
        {
            Assert.Equal(new[] { "volume C:", "volume D:" }, releasedResults);
        }
    }

    [Fact]
    public void RunBounded_AResultInTime_IsReturned_AndNotReleased()
    {
        int releases = 0;

        List<string> result = WmiHelper.RunBounded(() => new List<string> { "volume C:" }, TimeSpan.FromSeconds(30), "query", _ => Interlocked.Increment(ref releases));

        Assert.Equal(new[] { "volume C:" }, result);
        Assert.Equal(0, Volatile.Read(ref releases));
    }

    [Fact]
    public void RunBounded_ALateFailure_IsNotReleased_AndAThrowingReleaseIsContained()
    {
        using ManualResetEventSlim finish = new(false);
        using ManualResetEventSlim done = new(false);
        int releases = 0;

        Assert.Throws<TimeoutException>(() => WmiHelper.RunBounded<List<string>>(
            () =>
            {
                try
                {
                    finish.Wait(TimeSpan.FromSeconds(30));
                    throw new ManagementException("provider failed late");
                }
                finally
                {
                    done.Set();
                }
            },
            TimeSpan.FromMilliseconds(50),
            "query",
            _ =>
            {
                Interlocked.Increment(ref releases);
                throw new InvalidOperationException("release failed");
            }));
        finish.Set();

        Assert.True(done.Wait(TimeSpan.FromSeconds(30)));
        Thread.Sleep(50);
        Assert.Equal(0, Volatile.Read(ref releases));

        // A release that throws is contained: the timeout is still the only exception the caller sees.
        using ManualResetEventSlim finishToo = new(false);
        using ManualResetEventSlim releasedToo = new(false);
        Assert.Throws<TimeoutException>(() => WmiHelper.RunBounded(
            () =>
            {
                finishToo.Wait(TimeSpan.FromSeconds(30));
                return 1;
            },
            TimeSpan.FromMilliseconds(50),
            "query",
            _ =>
            {
                releasedToo.Set();
                throw new InvalidOperationException("release failed");
            }));
        finishToo.Set();
        Assert.True(releasedToo.Wait(TimeSpan.FromSeconds(30)));
    }

    /// <summary>One volume's methods, recorded; each protector's type and profile are set per ID.</summary>
    private sealed class FakeVolume : IEncryptableVolumeMethods
    {
        public IReadOnlyList<string> Protectors { get; set; } = Array.Empty<string>();

        public uint ProtectorsReturnValue { get; init; }

        public Dictionary<string, int> Types { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, int[]> Profiles { get; } = new(StringComparer.Ordinal);

        /// <summary>Exceptions to throw, keyed like <see cref="Calls"/>.</summary>
        public Dictionary<string, Exception> Failures { get; } = new(StringComparer.Ordinal);

        public List<string> Calls { get; } = new();

        public VolumeMethodResult<IReadOnlyList<string>> GetKeyProtectors()
        {
            Record("GetKeyProtectors");
            return new VolumeMethodResult<IReadOnlyList<string>>(ProtectorsReturnValue, ProtectorsReturnValue == 0 ? Protectors : Array.Empty<string>());
        }

        public VolumeMethodResult<int?> GetKeyProtectorType(string protectorId)
        {
            Record("GetKeyProtectorType " + protectorId);
            return new VolumeMethodResult<int?>(0, Types.TryGetValue(protectorId, out int type) ? type : null);
        }

        public VolumeMethodResult<IReadOnlyList<int>> GetPlatformValidationProfile(string protectorId)
        {
            Record("GetKeyProtectorPlatformValidationProfile " + protectorId);
            return new VolumeMethodResult<IReadOnlyList<int>>(0, Profiles.TryGetValue(protectorId, out int[]? pcrs) ? pcrs : Array.Empty<int>());
        }

        private void Record(string call)
        {
            Calls.Add(call);
            if (Failures.TryGetValue(call, out Exception? failure))
            {
                throw failure;
            }
        }
    }
}
