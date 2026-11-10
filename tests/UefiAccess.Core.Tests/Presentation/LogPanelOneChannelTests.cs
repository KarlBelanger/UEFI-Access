// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Serilog;
using Serilog.Events;
using UefiAccess.Core.Native;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Privileges;
using UefiAccess.Core.Variables;
using UefiAccess.Presentation.Services;
using UefiAccess.Presentation.ViewModels;
using Xunit;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// Findings F086 and F171: with the real services the panel showed one event twice, once as the
/// line a Core service (or the live source) logged and once as the line the shell wrote from the
/// result. Here the shell runs over the real <see cref="LiveFirmwareSource"/> and the real
/// <see cref="WindowsFirmwareVariableStore"/> (over a fake native seam that fails the enumeration
/// and one fallback read), with an identity that logs the way MachineIdentity and WmiHelper do,
/// composed as <see cref="LiveFirmwareSource.CreateWindows"/> composes them: the Core services over
/// the file-only logger. Every event reaches the panel once, and the log file still has the Core
/// services' own lines.
/// </summary>
public sealed class LogPanelOneChannelTests
{
    [Fact]
    public async Task AFallbackLoadWithAnIdentityError_ShowsEveryEventOnce_AndTheFileKeepsTheCoreLines()
    {
        ShellTestRig rig = LiveRig(null);

        await rig.Shell.RefreshAsync();

        // Ready, Loading, then one WARN per warning of the result, the expected model notes and the
        // Loaded sentence; nothing the Core services logged themselves.
        (string Level, string Message)[] lines = rig.Lines;
        Assert.Equal(lines.Length, lines.Distinct().Count());
        Assert.Equal(0, rig.Mirror.Mirrored);
        Assert.Equal(8, lines.Length);
        Assert.Equal((LogViewModel.WarnLevel, "machine identity: Win32_BIOS: ManagementException Timedout"), lines[2]);
        Assert.Equal(LogViewModel.WarnLevel, lines[3].Level);
        Assert.StartsWith("NtEnumerateSystemEnvironmentValuesEx failed: ", lines[3].Message, StringComparison.Ordinal);
        Assert.EndsWith("; falling back to reading the curated KnownVariableNames list one by one", lines[3].Message, StringComparison.Ordinal);
        Assert.Equal((LogViewModel.WarnLevel, "the fallback only sees the curated names and Boot#### entries; vendor-specific variables are not listed by this path"), lines[4]);
        Assert.Equal(LogViewModel.WarnLevel, lines[5].Level);
        Assert.StartsWith("Timeout: ", lines[5].Message, StringComparison.Ordinal);
        Assert.StartsWith("Expected model confirmed", lines[6].Message, StringComparison.Ordinal);
        Assert.Equal((LogViewModel.InfoLevel, "Loaded 1 variable from Live firmware (KnownNamesFallback)"), lines[7]);

        // The file has what the Core services said themselves, marked file-only.
        IReadOnlyList<LogEvent> file = rig.Events.Snapshot();
        Assert.Contains(file, e => e.Level == LogEventLevel.Warning && e.MessageTemplate.Text.StartsWith("Enumeration failed", StringComparison.Ordinal) && !LogPanelMirror.Mirrors(e));
        Assert.Contains(file, e => e.Level == LogEventLevel.Warning && e.MessageTemplate.Text.StartsWith("Reading {Name}", StringComparison.Ordinal) && !LogPanelMirror.Mirrors(e));
        Assert.Contains(file, e => e.Level == LogEventLevel.Information && e.MessageTemplate.Text.StartsWith("Machine identity:", StringComparison.Ordinal) && !LogPanelMirror.Mirrors(e));
        Assert.Contains(file, e => e.Level == LogEventLevel.Warning && e.MessageTemplate.Text.StartsWith("WMI {Namespace}", StringComparison.Ordinal) && !LogPanelMirror.Mirrors(e));
    }

    [Fact]
    public async Task AStoreThatThrows_GivesOneErrorLine()
    {
        ShellTestRig rig = LiveRig(new ThrowingStore());

        await rig.Shell.RefreshAsync();

        (string Level, string Message) error = Assert.Single(rig.Lines, line => line.Level == LogViewModel.ErrorLevel);
        Assert.Equal("Failed: Enumerate threw InvalidOperationException: native seam exploded", error.Message);
        Assert.Equal("Loading failed: Enumerate threw InvalidOperationException: native seam exploded", rig.Shell.StatusText);
        Assert.Equal(rig.Lines.Length, rig.Lines.Distinct().Count());
    }

    [Fact]
    public void CreateWindows_ComposesTheCoreServicesOverTheFileOnlyLogger()
    {
        string source = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "src", "UefiAccess.Presentation", "Services", "LiveFirmwareSource.cs"));
        string body = source[source.IndexOf("public static LiveFirmwareSource CreateWindows(", StringComparison.Ordinal)..];
        body = body[..body.IndexOf("\n    }", StringComparison.Ordinal)];

        Assert.Contains("ILogger coreLog = LogPanelMirror.FileOnly(log);", body, StringComparison.Ordinal);
        Assert.Contains("FirmwareTypeDetector detector = new(coreLog);", body, StringComparison.Ordinal);
        Assert.Contains("new PrivilegeManager(coreLog)", body, StringComparison.Ordinal);
        Assert.Contains("new WindowsFirmwareVariableStore(detector, coreLog)", body, StringComparison.Ordinal);
        Assert.Contains("new MachineIdentity(coreLog)", body, StringComparison.Ordinal);

        // M3 (design section 3.10): the write pipeline is composed in the same method, lazily, over
        // the same file-only logger: its detector, store, privilege manager, platform policy, writer
        // and suspender (six more Core services), and the pipeline itself.
        Assert.Contains("FirmwareTypeDetector pipelineDetector = new(coreLog);", body, StringComparison.Ordinal);
        Assert.Contains("WindowsFirmwareVariableStore pipelineStore = new(pipelineDetector, coreLog);", body, StringComparison.Ordinal);
        Assert.Contains("PlatformPolicy.CreateWindows(pipelineStore, coreLog)", body, StringComparison.Ordinal);
        Assert.Contains("logger: coreLog)", body, StringComparison.Ordinal);
        Assert.Equal(10, Regex.Matches(body, @"\(coreLog\)|, coreLog\)").Count);
    }

    /// <summary>The shell over the real live source, composed like CreateWindows: the Core services over the file-only logger.</summary>
    /// <param name="store">The store; null means the real store over the fake native seam.</param>
    private static ShellTestRig LiveRig(IFirmwareVariableStore? store)
    {
        LateSource live = new();
        ShellTestRig rig = new(live);
        ILogger coreLog = LogPanelMirror.FileOnly(rig.Logger);
        UefiDetector detector = new();
        live.Inner = new LiveFirmwareSource(
            detector,
            new ElevatedPrivileges(),
            store ?? new WindowsFirmwareVariableStore(detector, coreLog, new FakeNativeApi()),
            new LoggingIdentity(coreLog),
            rig.Logger);
        return rig;
    }

    private sealed class ThrowingStore : IFirmwareVariableStore
    {
        public EnumerationResult Enumerate() => throw new InvalidOperationException("native seam exploded");

        public ReadResult Read(string name, Guid vendorGuid) => throw new InvalidOperationException("native seam exploded");
    }

    /// <summary>The live source, supplied after the rig (and so its logger) exists.</summary>
    private sealed class LateSource : IVariableSource
    {
        public IVariableSource? Inner { get; set; }

        public string Description => Inner?.Description ?? UefiAccess.Presentation.Resources.Strings.LiveSourceName;

        public bool IsOffline => false;

        public Task<SourceResult> LoadAsync(CancellationToken cancellationToken) => Inner!.LoadAsync(cancellationToken);
    }

    private sealed class UefiDetector : IFirmwareTypeDetector
    {
        public FirmwareType Detect() => FirmwareType.Uefi;
    }

    private sealed class ElevatedPrivileges : IPrivilegeManager
    {
        public bool IsElevated => true;

        public PrivilegeState EnableSystemEnvironmentPrivilege() => PrivilegeState.Enabled;
    }

    /// <summary>Logs the way MachineIdentity and WmiHelper do, and reports the failed query as an error.</summary>
    private sealed class LoggingIdentity : IPlatformInspector<MachineIdentityResult>
    {
        private readonly ILogger _logger;

        public LoggingIdentity(ILogger logger) => _logger = logger;

        public MachineIdentityResult Inspect()
        {
            _logger.Warning("WMI {Namespace}: {Query} failed: {Detail}, {Elapsed:F1} ms", @"root\CIMV2", "SELECT SMBIOSBIOSVersion FROM Win32_BIOS", "ManagementException Timedout", 60000.0);
            _logger.Information("Machine identity: {Manufacturer} {Model}, BIOS {Bios}, CPU {Cpu}; expected model confirmed: {Confirmed}", "Dell Inc.", "OptiPlex 7020", null, "i7-4790", false);
            return LiveFirmwareSourceTests.Identity() with { Errors = new[] { "Win32_BIOS: ManagementException Timedout" } };
        }
    }

    /// <summary>
    /// The native seam: the enumeration fails with STATUS_INVALID_PARAMETER (so the store falls back
    /// to the known names), BootOrder reads, Timeout is denied, everything else is not found.
    /// </summary>
    private sealed class FakeNativeApi : INativeFirmwareApi
    {
        private int _lastError;

        public bool IsAvailable => true;

        public uint EnumerateValues(uint informationClass, IntPtr buffer, ref uint length)
        {
            length = 0;
            return NativeConstants.STATUS_INVALID_PARAMETER;
        }

        public uint ReadVariable(string name, string guid, IntPtr buffer, uint size, out uint attributes)
        {
            attributes = 0;
            if (name == "BootOrder" && guid == KnownGuids.ToBracedUpper(KnownGuids.EfiGlobalVariable))
            {
                byte[] data = { 0x01, 0x00, 0x00, 0x00 };
                Marshal.Copy(data, 0, buffer, data.Length);
                attributes = 7;
                _lastError = 0;
                return (uint)data.Length;
            }

            _lastError = name == "Timeout" ? NativeConstants.ERROR_ACCESS_DENIED : NativeConstants.ERROR_ENVVAR_NOT_FOUND;
            return 0;
        }

        public int GetLastError() => _lastError;
    }
}
