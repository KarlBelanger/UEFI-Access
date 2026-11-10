// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Serilog;
using UefiAccess.Core.Native;
using UefiAccess.Core.Variables;

namespace UefiAccess.Core.Safety;

/// <summary>
/// The single call site of the firmware write: one native call per
/// <see cref="CommitOnce"/>, then, on failure, one read of the Win32 error and one read of the
/// thread's last NTSTATUS. There is no loop, no second attempt and no branch that calls twice. The
/// production instance is built only by the two composition roots over
/// <see cref="WindowsNativeFirmwareWriteApi.Instance"/>; a test exercises <see cref="Commit"/>
/// with a fake seam and never runs this class's constructor. One test holds an instance made
/// without it (no seam at all), only to pin <see cref="WritePipeline"/>'s refusal of the real
/// writer over a store that is not the firmware's; the guard test allows that one site.
/// </summary>
public sealed class FirmwareVariableWriter : IFirmwareVariableWriter
{
    /// <summary>The value of <see cref="WriteOutcome.NtStatusSource"/> when the NTSTATUS was read from the thread after a failed call.</summary>
    public const string NtStatusSourceName = "RtlGetLastNtStatus";

    /// <summary>The value of <see cref="WriteOutcome.NtStatusSource"/> when no NTSTATUS was read (the call succeeded).</summary>
    public const string NtStatusNotObserved = "not observed";

    private readonly INativeFirmwareWriteApi _api;
    private readonly ILogger _logger;

    /// <summary>
    /// Creates the writer over the Windows seam. Called by the composition roots only
    /// (CliPlatform.CreateWindows and LiveFirmwareSource.CreateWindows); the parameter's type is
    /// the Windows seam itself, so no other seam can be handed to a production writer and a test,
    /// which never names that type, cannot build one.
    /// </summary>
    /// <param name="api">The Windows seam, <see cref="WindowsNativeFirmwareWriteApi.Instance"/>.</param>
    /// <param name="logger">Optional Serilog logger; null disables logging.</param>
    public FirmwareVariableWriter(WindowsNativeFirmwareWriteApi api, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
        _logger = logger ?? Serilog.Core.Logger.None;
    }

    /// <inheritdoc />
    public WriteOutcome CommitOnce(WritePlan plan)
    {
        return Commit(_api, plan, _logger);
    }

    /// <summary>
    /// The writer's whole behaviour as a function of the seam and the plan: copies the plan's
    /// bytes into an unmanaged buffer, makes exactly one write call, and on failure reads the
    /// Win32 error and then the thread's NTSTATUS, each once and on the calling thread, before
    /// anything else runs. The NTSTATUS is recorded as read: it is the value kernel32 left when it
    /// converted a kernel status into the Win32 code, and is never derived from that code. Throws
    /// only what the seam throws, after its one call.
    /// </summary>
    /// <param name="api">The native seam (the Windows one in production, a fake in a test).</param>
    /// <param name="plan">The plan the transaction built and validated.</param>
    /// <param name="logger">Logger; the log names the variable key and the numbers, never the API.</param>
    /// <returns>The outcome.</returns>
    internal static WriteOutcome Commit(INativeFirmwareWriteApi api, WritePlan plan, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(logger);

        string guidText = KnownGuids.ToBracedUpper(plan.Guid);
        byte[] bytes = plan.Bytes;
        IntPtr buffer = IntPtr.Zero;
        try
        {
            // A valid pointer is passed even when the size is 0 (a deletion); the call reads no byte then.
            buffer = Marshal.AllocHGlobal(Math.Max(1, bytes.Length));
            if (bytes.Length > 0)
            {
                Marshal.Copy(bytes, 0, buffer, bytes.Length);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            bool succeeded = api.Write(plan.Name, guidText, buffer, plan.Size, plan.Attributes);
            if (succeeded)
            {
                TimeSpan written = stopwatch.Elapsed;
                logger.Debug("firmware write {Key}: {Size} bytes, attributes 0x{Attributes:X} -> success, {Elapsed} ms", plan.Key, plan.Size, plan.Attributes, written.TotalMilliseconds);
                return WriteStatusMapping.Succeeded(written);
            }

            // Read on this thread before anything else can run: the Win32 error, then the NTSTATUS.
            int error = api.GetLastError();
            uint ntStatus = api.GetLastNtStatus();
            TimeSpan elapsed = stopwatch.Elapsed;
            WriteOutcome outcome = WriteStatusMapping.Failed(error, ntStatus, NtStatusSourceName, elapsed);
            logger.Warning(
                "firmware write {Key}: {Size} bytes, attributes 0x{Attributes:X} -> failed, error {Error} ({Name}), NTSTATUS 0x{NtStatus:X8}, class {Status}, {Elapsed} ms",
                plan.Key,
                plan.Size,
                plan.Attributes,
                error,
                outcome.Win32Name,
                ntStatus,
                outcome.Status,
                elapsed.TotalMilliseconds);
            return outcome;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }
}

/// <summary>
/// Production seam: forwards to the write import in <see cref="Kernel32"/> and the NTSTATUS read
/// in <see cref="NtDll"/>. The seam's members are explicit, so the class's public surface is the
/// shared instance alone, which only the two composition roots reference.
/// </summary>
public sealed class WindowsNativeFirmwareWriteApi : INativeFirmwareWriteApi
{
    /// <summary>The shared instance.</summary>
    public static WindowsNativeFirmwareWriteApi Instance { get; } = new();

    private WindowsNativeFirmwareWriteApi()
    {
    }

    /// <inheritdoc />
    bool INativeFirmwareWriteApi.Write(string name, string guid, IntPtr buffer, uint size, uint attributes)
    {
        return Kernel32.SetFirmwareEnvironmentVariableExW(name, guid, buffer, size, attributes);
    }

    /// <inheritdoc />
    int INativeFirmwareWriteApi.GetLastError() => Marshal.GetLastWin32Error();

    /// <inheritdoc />
    uint INativeFirmwareWriteApi.GetLastNtStatus() => NtDll.RtlGetLastNtStatus();
}
