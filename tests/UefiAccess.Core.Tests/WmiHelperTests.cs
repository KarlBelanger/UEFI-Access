// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using Serilog.Core;
using UefiAccess.Core.Platform;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// Tests for the bounded-wait discipline <see cref="WmiHelper"/> applies to ManagementScope.Connect,
/// which has no timeout of its own, and for the failure kinds a query reports since M4
/// (a provider that did not load, refused access, a timeout, anything
/// else), which the Dell settings provider gives their own advice. The helper is exercised with
/// plain delegates and exceptions built in the test, so no test here touches WMI.
/// </summary>
public sealed class WmiHelperTests
{
    [Fact]
    public void RunBounded_CompletesInTime_ReturnsNormally()
    {
        bool ran = false;

        Exception? exception = Record.Exception(() => WmiHelper.RunBounded(() => ran = true, TimeSpan.FromSeconds(10), "probe"));

        Assert.Null(exception);
        Assert.True(ran);
    }

    [Fact]
    public void RunBounded_RethrowsTheOriginalExceptionType()
    {
        COMException failure = new("invalid namespace", unchecked((int)0x8004100E));

        COMException thrown = Assert.Throws<COMException>(() => WmiHelper.RunBounded(() => throw failure, TimeSpan.FromSeconds(10), "probe"));

        Assert.Same(failure, thrown);
        Assert.True(WmiHelper.IsAbsent(thrown));
    }

    [Fact]
    public void RunBounded_TimesOut_ThrowsTimeoutExceptionAndDoesNotHang()
    {
        using ManualResetEventSlim release = new(false);
        TimeSpan timeout = TimeSpan.FromMilliseconds(100);
        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();

        TimeoutException thrown = Assert.Throws<TimeoutException>(() => WmiHelper.RunBounded(() => release.Wait(TimeSpan.FromSeconds(30)), timeout, "connect to namespace root\\stuck"));
        stopwatch.Stop();

        Assert.Contains("connect to namespace root\\stuck", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("did not complete within 0 seconds", thrown.Message, StringComparison.Ordinal);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), "the bounded wait took " + stopwatch.Elapsed);
        release.Set();
    }

    [Fact]
    public void RunBounded_LateFailureAfterTimeout_IsObservedNotRethrown()
    {
        using ManualResetEventSlim release = new(false);
        using ManualResetEventSlim finished = new(false);
        List<string> unobserved = new();
        EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, args) =>
        {
            lock (unobserved)
            {
                unobserved.Add(args.Exception.ToString());
            }
        };

        TaskScheduler.UnobservedTaskException += handler;
        try
        {
            Assert.Throws<TimeoutException>(() => WmiHelper.RunBounded(
                () =>
                {
                    try
                    {
                        release.Wait(TimeSpan.FromSeconds(30));
                        throw new InvalidOperationException("late failure from a stuck WMI call");
                    }
                    finally
                    {
                        finished.Set();
                    }
                },
                TimeSpan.FromMilliseconds(50),
                "probe"));

            // Releasing the stuck call makes it fault after the caller has moved on. The helper
            // observes that fault, so finalising the task must not raise UnobservedTaskException.
            release.Set();
            Assert.True(finished.Wait(TimeSpan.FromSeconds(10)));
            Thread.Sleep(100);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= handler;
        }

        lock (unobserved)
        {
            Assert.DoesNotContain(unobserved, text => text.Contains("late failure from a stuck WMI call", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ClassifyFailure_ProviderLoadFailureHResult_IsProviderLoadFailure()
    {
        // What a query sees without the Visual C++ runtime that Dell Command Monitor's provider needs.
        Assert.Equal(WmiFailureKind.ProviderLoadFailure, WmiHelper.ClassifyFailure(new COMException("Provider load failure", unchecked((int)0x80041013))));
        Assert.Equal(WmiFailureKind.ProviderLoadFailure, WmiHelper.ClassifyFailure(ManagementFailure(ManagementStatus.ProviderLoadFailure)));
    }

    [Fact]
    public void ClassifyFailure_AccessDeniedHResults()
    {
        Assert.Equal(WmiFailureKind.AccessDenied, WmiHelper.ClassifyFailure(new COMException("Access denied", unchecked((int)0x80041003))));
        Assert.Equal(WmiFailureKind.AccessDenied, WmiHelper.ClassifyFailure(new COMException("Access is denied.", unchecked((int)0x80070005))));
        Assert.Equal(WmiFailureKind.AccessDenied, WmiHelper.ClassifyFailure(new UnauthorizedAccessException("Access is denied.")));
        Assert.Equal(WmiFailureKind.AccessDenied, WmiHelper.ClassifyFailure(ManagementFailure(ManagementStatus.AccessDenied)));
    }

    [Fact]
    public void ClassifyFailure_TimeoutException_IsTimedOut()
    {
        Assert.Equal(WmiFailureKind.TimedOut, WmiHelper.ClassifyFailure(new TimeoutException("WMI query DCIM_BIOSEnumeration did not complete within 60 seconds")));
    }

    [Fact]
    public void ClassifyFailure_ManagementTimedout_IsTimedOut()
    {
        // The enumeration's own timeout (EnumerationOptions.Timeout, 60 seconds) elapsing first.
        Assert.Equal(WmiFailureKind.TimedOut, WmiHelper.ClassifyFailure(ManagementFailure(ManagementStatus.Timedout)));
    }

    [Fact]
    public void ClassifyFailure_OtherException_IsOther()
    {
        Assert.Equal(WmiFailureKind.Other, WmiHelper.ClassifyFailure(new InvalidOperationException("scripted failure")));
        Assert.Equal(WmiFailureKind.Other, WmiHelper.ClassifyFailure(new COMException("The RPC server is unavailable.", unchecked((int)0x800706BA))));
        Assert.Equal(WmiFailureKind.Other, WmiHelper.ClassifyFailure(new COMException("invalid namespace", unchecked((int)0x8004100E))));
        Assert.Equal(WmiFailureKind.Other, WmiHelper.ClassifyFailure(ManagementFailure(ManagementStatus.InvalidQuery)));
        Assert.Equal(WmiFailureKind.Other, WmiHelper.ClassifyFailure(new ManagementException("provider failed")));
    }

    [Fact]
    public void Query_CarriesTheFailureKindOnTheResult()
    {
        WmiQueryResult result = WmiHelper.Query(
            @"root\dcim\sysman",
            "SELECT AttributeName FROM DCIM_BIOSEnumeration",
            "DCIM_BIOSEnumeration",
            Logger.None,
            int.MaxValue,
            _ => throw new COMException("Provider load failure", unchecked((int)0x80041013)));

        Assert.False(result.Absent);
        Assert.Empty(result.Rows);
        Assert.Equal("DCIM_BIOSEnumeration: COMException 0x80041013: Provider load failure", result.Error);
        Assert.Equal(WmiFailureKind.ProviderLoadFailure, result.Failure);
    }

    [Fact]
    public void Query_Absent_KeepsFailureNone()
    {
        WmiQueryResult result = WmiHelper.Query(
            @"root\dcim\sysman",
            "SELECT AttributeName FROM DCIM_BIOSEnumeration",
            "DCIM_BIOSEnumeration",
            Logger.None,
            int.MaxValue,
            _ => throw new COMException("invalid namespace", unchecked((int)0x8004100E)));

        Assert.True(result.Absent);
        Assert.Null(result.Error);
        Assert.Equal(WmiFailureKind.None, result.Failure);
        Assert.Equal(WmiFailureKind.None, new WmiQueryResult(Array.Empty<IReadOnlyDictionary<string, object?>>(), null, false).Failure);
    }

    [NonWindowsFact]
    public void NamespaceExists_OffWindows_ReportsErrorAndNull()
    {
        bool? exists = WmiHelper.NamespaceExists(@"root\dcim\sysman", Serilog.Core.Logger.None, out string? error);

        Assert.Null(exists);
        Assert.NotNull(error);
        Assert.Contains("only available on Windows", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// A ManagementException with a WMI status, as System.Management throws it. Its public
    /// constructors set no status, so the internal one that does is called by reflection; nothing
    /// connects to WMI.
    /// </summary>
    private static ManagementException ManagementFailure(ManagementStatus status)
    {
        ConstructorInfo? constructor = typeof(ManagementException).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            new[] { typeof(ManagementStatus), typeof(string), typeof(ManagementBaseObject) });
        Assert.True(constructor is not null, "System.Management no longer has ManagementException(ManagementStatus, string, ManagementBaseObject)");
        ManagementException exception = (ManagementException)constructor!.Invoke(new object?[] { status, "scripted " + status, null });
        Assert.Equal(status, exception.ErrorCode);
        return exception;
    }
}
