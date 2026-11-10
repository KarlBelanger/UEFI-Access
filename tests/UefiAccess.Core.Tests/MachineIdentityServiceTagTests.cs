// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Runtime.InteropServices;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using UefiAccess.Core.Platform;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// MachineIdentity.ReadServiceTag, which the trace files use to replace the service tag in every
/// line: one Win32_BIOS query through the bounded WmiHelper, the value trimmed, null for anything
/// that is not a value, never a throw, and the value never logged. No test here reaches WMI (the
/// query is the test's own, as in WmiHelperTests); the real query runs only on a host that is not
/// Windows, where it must give null without querying. The tags are synthetic.
/// </summary>
public sealed class MachineIdentityServiceTagTests
{
    private const string Tag = "TESTTAG7";

    [Fact]
    public void TheValue_IsReadFromWin32Bios_AndTrimmed()
    {
        List<string> queries = new();
        string? tag = new MachineIdentity().ReadServiceTag(
            wql =>
            {
                queries.Add(wql);
                return Rows(new Dictionary<string, object?> { ["SerialNumber"] = "  " + Tag + "  " });
            },
            onWindows: true);

        Assert.Equal(Tag, tag);
        Assert.Equal(new[] { "SELECT SerialNumber FROM Win32_BIOS" }, queries);
        Assert.Equal("SELECT SerialNumber FROM Win32_BIOS", MachineIdentity.ServiceTagQuery);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyValue_IsNull(string? value)
    {
        Assert.Null(new MachineIdentity().ReadServiceTag(_ => Rows(new Dictionary<string, object?> { ["SerialNumber"] = value }), onWindows: true));
    }

    [Fact]
    public void NoInstance_AMissingProperty_AnAbsentClassOrAnError_IsNull()
    {
        MachineIdentity identity = new();

        Assert.Null(identity.ReadServiceTag(_ => new WmiQueryResult(Array.Empty<IReadOnlyDictionary<string, object?>>(), null, false), onWindows: true));
        Assert.Null(identity.ReadServiceTag(_ => Rows(new Dictionary<string, object?> { ["SMBIOSBIOSVersion"] = "A18" }), onWindows: true));
        Assert.Null(identity.ReadServiceTag(_ => new WmiQueryResult(Array.Empty<IReadOnlyDictionary<string, object?>>(), null, true), onWindows: true));
        Assert.Null(identity.ReadServiceTag(_ => new WmiQueryResult(Array.Empty<IReadOnlyDictionary<string, object?>>(), "Win32_BIOS: query timed out", false), onWindows: true));
    }

    [Fact]
    public void AQueryThatThrows_IsNull_NotAThrow()
    {
        string? tag = "unset";
        Exception? thrown = Record.Exception(() => tag = new MachineIdentity().ReadServiceTag(_ => throw new COMException("RPC server unavailable"), onWindows: true));

        Assert.Null(thrown);
        Assert.Null(tag);
    }

    [Fact]
    public void OffWindows_NothingIsQueried()
    {
        bool queried = false;

        Assert.Null(new MachineIdentity().ReadServiceTag(
            _ =>
            {
                queried = true;
                return Rows(new Dictionary<string, object?> { ["SerialNumber"] = Tag });
            },
            onWindows: false));
        Assert.False(queried);
    }

    [Fact]
    public void TheValue_IsNeverLogged()
    {
        Capture capture = new();
        ILogger logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(capture).CreateLogger();

        string? tag = new MachineIdentity(logger).ReadServiceTag(_ => Rows(new Dictionary<string, object?> { ["SerialNumber"] = Tag }), onWindows: true);

        Assert.Equal(Tag, tag);
        Assert.NotEmpty(capture.Rendered);
        Assert.All(capture.Rendered, line => Assert.DoesNotContain(Tag, line, StringComparison.OrdinalIgnoreCase));
    }

    [NonWindowsFact]
    public void ReadServiceTag_OnANonWindowsHost_IsNull()
    {
        Assert.Null(new MachineIdentity().ReadServiceTag());
    }

    private static WmiQueryResult Rows(IReadOnlyDictionary<string, object?> row)
    {
        return new WmiQueryResult(new[] { row }, null, false);
    }

    /// <summary>Keeps every event rendered with its properties, as a log file would hold it.</summary>
    private sealed class Capture : ILogEventSink
    {
        public List<string> Rendered { get; } = new();

        public void Emit(LogEvent logEvent)
        {
            Rendered.Add(logEvent.RenderMessage() + " " + string.Join(" ", logEvent.Properties.Select(pair => pair.Key + "=" + pair.Value)));
        }
    }
}
