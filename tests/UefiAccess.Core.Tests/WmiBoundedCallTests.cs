// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using Serilog.Core;
using UefiAccess.Core.Platform;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The bounded waits <see cref="WmiHelper"/> runs every connect and enumeration under, and the
/// 60-second bounds themselves. Query and ListClassNames take their scope from a connect delegate
/// (<see cref="WmiHelper.ConnectBounded(string)"/> in production) and never connect on their own,
/// so a connect that hangs costs the bound and comes back as an error, and one that reports a
/// missing namespace still reads as absent. Exercised with plain delegates: nothing here creates a
/// System.Management scope or touches WMI (a delegate that fails means no searcher ever runs).
/// </summary>
public sealed class WmiBoundedCallTests
{
    [Fact]
    public void RunBounded_ReturnsTheCallsResult()
    {
        IReadOnlyList<string> rows = WmiHelper.RunBounded<IReadOnlyList<string>>(() => new[] { "Win32_BIOS" }, TimeSpan.FromSeconds(10), "query Win32_BIOS");

        Assert.Equal(new[] { "Win32_BIOS" }, rows);
    }

    [Fact]
    public void RunBounded_WithAResult_RethrowsTheOriginalExceptionType()
    {
        COMException failure = new("invalid class", unchecked((int)0x80041010));

        COMException thrown = Assert.Throws<COMException>(() => WmiHelper.RunBounded<int>(() => throw failure, TimeSpan.FromSeconds(10), "query Win32_Missing"));

        Assert.Same(failure, thrown);
        Assert.True(WmiHelper.IsAbsent(thrown));
    }

    [Fact]
    public void RunBounded_WithAResult_TimesOutWithTheQueryInTheMessage()
    {
        using ManualResetEventSlim release = new(false);

        TimeoutException thrown = Assert.Throws<TimeoutException>(() => WmiHelper.RunBounded(
            () => release.Wait(TimeSpan.FromSeconds(30)) ? 1 : 0,
            TimeSpan.FromMilliseconds(100),
            "query Win32_Tpm"));
        release.Set();

        Assert.Equal("WMI query Win32_Tpm did not complete within 0 seconds", thrown.Message);
    }

    [Fact]
    public void Bounds_AreSixtySecondsForConnectsAndEnumerations()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), WmiHelper.QueryTimeout);
        Assert.Equal(TimeSpan.FromSeconds(60), WmiHelper.ConnectTimeout);

        System.Management.EnumerationOptions options = WmiHelper.CreateEnumerationOptions();
        Assert.Equal(TimeSpan.FromSeconds(60), options.Timeout);
        Assert.True(options.ReturnImmediately);
        Assert.False(options.Rewindable);
    }

    [Fact]
    public void ConnectBounded_ReturnsWhatTheConnectReturnsForTheNamespace()
    {
        object scope = new();
        List<string> asked = new();

        object connected = WmiHelper.ConnectBounded(@"root\uefiaccess-test", TimeSpan.FromSeconds(10), ns =>
        {
            asked.Add(ns);
            return scope;
        });

        Assert.Same(scope, connected);
        Assert.Equal(new[] { @"root\uefiaccess-test" }, asked);
    }

    [Fact]
    public void ConnectBounded_RethrowsTheConnectsOwnExceptionType()
    {
        COMException failure = new("invalid namespace", unchecked((int)0x8004100E));

        COMException thrown = Assert.Throws<COMException>(() => WmiHelper.ConnectBounded<object>(@"root\dcim\sysman", TimeSpan.FromSeconds(10), _ => throw failure));

        Assert.Same(failure, thrown);
        Assert.True(WmiHelper.IsAbsent(thrown));
    }

    [Fact]
    public void ConnectBounded_ConnectThatNeverReturns_TimesOutWithTheNamespaceInTheMessage()
    {
        using ManualResetEventSlim release = new(false);
        Stopwatch stopwatch = Stopwatch.StartNew();

        TimeoutException thrown = Assert.Throws<TimeoutException>(() => WmiHelper.ConnectBounded(
            @"root\uefiaccess-test",
            TimeSpan.FromMilliseconds(100),
            _ => release.Wait(TimeSpan.FromSeconds(30))));
        release.Set();

        Assert.Equal(@"WMI connect to namespace root\uefiaccess-test did not complete within 0 seconds", thrown.Message);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(20), "the wait was not bounded");
    }

    [Fact]
    public void Query_UsesItsConnect_AndReportsAConnectThatTimedOutAsAnError()
    {
        using ManualResetEventSlim release = new(false);
        List<string> connected = new();

        WmiQueryResult result = WmiHelper.Query(@"root\CIMV2", "SELECT * FROM Win32_BIOS", "Win32_BIOS", Logger.None, int.MaxValue, ns =>
        {
            connected.Add(ns);
            return WmiHelper.ConnectBounded<ManagementScope>(ns, TimeSpan.FromMilliseconds(100), _ =>
            {
                release.Wait(TimeSpan.FromSeconds(30));
                return null!;
            });
        });
        release.Set();

        Assert.Equal(new[] { @"root\CIMV2" }, connected);
        Assert.False(result.Absent);
        Assert.Empty(result.Rows);
        Assert.Equal(@"Win32_BIOS: TimeoutException: WMI connect to namespace root\CIMV2 did not complete within 0 seconds", result.Error);
    }

    [Fact]
    public void Query_ConnectReportsAMissingNamespace_IsAbsentNotAnError()
    {
        WmiQueryResult result = WmiHelper.Query(
            @"root\dcim\sysman",
            "SELECT * FROM DCIM_BIOSEnumeration",
            "DCIM_BIOSEnumeration",
            Logger.None,
            int.MaxValue,
            _ => throw new COMException("invalid namespace", unchecked((int)0x8004100E)));

        Assert.True(result.Absent);
        Assert.Null(result.Error);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void ListClassNames_UsesItsConnect_AndReportsATimeoutAsAnError()
    {
        List<string> connected = new();

        IReadOnlyList<string> names = WmiHelper.ListClassNames(@"root\dcim\sysman", Logger.None, ns =>
        {
            connected.Add(ns);
            throw new TimeoutException("WMI connect to namespace " + ns + " did not complete within 60 seconds");
        }, out string? error);

        Assert.Equal(new[] { @"root\dcim\sysman" }, connected);
        Assert.Empty(names);
        Assert.Equal(@"root\dcim\sysman class listing: TimeoutException: WMI connect to namespace root\dcim\sysman did not complete within 60 seconds", error);
    }

    [Fact]
    public void ListClassNames_ConnectReportsAMissingNamespace_IsEmptyWithoutAnError()
    {
        IReadOnlyList<string> names = WmiHelper.ListClassNames(
            @"root\dcim\sysman",
            Logger.None,
            _ => throw new COMException("invalid namespace", unchecked((int)0x8004100E)),
            out string? error);

        Assert.Empty(names);
        Assert.Null(error);
    }
}
