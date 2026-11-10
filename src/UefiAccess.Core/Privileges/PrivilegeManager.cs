// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Serilog;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Core.Native;

namespace UefiAccess.Core.Privileges;

/// <summary>
/// Enables SeSystemEnvironmentPrivilege through OpenProcessToken, LookupPrivilegeValueW and
/// AdjustTokenPrivileges. On a non-Windows host <see cref="IsElevated"/> is false and
/// <see cref="EnableSystemEnvironmentPrivilege"/> returns <see cref="PrivilegeState.NotElevated"/>.
/// No member throws.
/// </summary>
public sealed class PrivilegeManager : IPrivilegeManager
{
    private readonly ILogger _logger;

    /// <summary>Creates a privilege manager without a logger.</summary>
    public PrivilegeManager()
        : this(null)
    {
    }

    /// <summary>Creates a privilege manager that logs every native call to <paramref name="logger"/>.</summary>
    /// <param name="logger">Optional Serilog logger; null disables logging.</param>
    public PrivilegeManager(ILogger? logger)
    {
        _logger = logger ?? Serilog.Core.Logger.None;
    }

    /// <inheritdoc />
    public bool IsElevated => ComputeIsElevated();

    /// <summary>
    /// A one-line explanation of the last failed step (for example which API failed and its Win32
    /// error), or null when the last call succeeded or has not run yet.
    /// </summary>
    public string? LastDetail { get; private set; }

    /// <inheritdoc />
    public PrivilegeState EnableSystemEnvironmentPrivilege()
    {
        if (!OperatingSystem.IsWindows())
        {
            LastDetail = "Not running on Windows; firmware privileges are unavailable on this host";
            _logger.Debug("EnableSystemEnvironmentPrivilege skipped: {Detail}", LastDetail);
            return PrivilegeState.NotElevated;
        }

        if (!IsElevated)
        {
            LastDetail = "The process is not elevated; SeSystemEnvironmentPrivilege exists only in an administrator token";
            _logger.Information("EnableSystemEnvironmentPrivilege: {Detail}", LastDetail);
            return PrivilegeState.NotElevated;
        }

        try
        {
            return EnableCore();
        }
        catch (Exception ex)
        {
            LastDetail = "Enabling SeSystemEnvironmentPrivilege failed: " + ex.GetType().Name + ": " + ex.Message;
            _logger.Warning(ex, "EnableSystemEnvironmentPrivilege threw");
            return PrivilegeState.ElevatedPrivilegeAbsent;
        }
    }

    /// <summary>
    /// True when the process token is in the Administrators role, which a UAC-filtered token of an
    /// administrator is not until it is elevated. Also used by <see cref="Diagnostics.Logging"/> to
    /// decide who may create the shared data folder. Never throws; false off Windows.
    /// </summary>
    /// <returns>True when elevated.</returns>
    internal static bool ComputeIsElevated()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private PrivilegeState EnableCore()
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        IntPtr token = IntPtr.Zero;
        try
        {
            uint access = NativeConstants.TOKEN_ADJUST_PRIVILEGES | NativeConstants.TOKEN_QUERY;
            bool opened = AdvApi32.OpenProcessToken(Kernel32.GetCurrentProcess(), access, out token);
            int openError = Marshal.GetLastWin32Error();
            _logger.Debug("OpenProcessToken(access=0x{Access:X}) -> {Result}, error {Error}, {Elapsed} ms", access, opened, openError, stopwatch.Elapsed.TotalMilliseconds);
            if (!opened)
            {
                LastDetail = "OpenProcessToken failed: " + NativeError.DescribeWin32(openError);
                return PrivilegeState.ElevatedPrivilegeAbsent;
            }

            bool found = AdvApi32.LookupPrivilegeValueW(null, NativeConstants.SE_SYSTEM_ENVIRONMENT_NAME, out LUID luid);
            int lookupError = Marshal.GetLastWin32Error();
            _logger.Debug("LookupPrivilegeValueW({Name}) -> {Result}, error {Error}, {Elapsed} ms", NativeConstants.SE_SYSTEM_ENVIRONMENT_NAME, found, lookupError, stopwatch.Elapsed.TotalMilliseconds);
            if (!found)
            {
                LastDetail = "LookupPrivilegeValueW failed: " + NativeError.DescribeWin32(lookupError);
                return PrivilegeState.ElevatedPrivilegeAbsent;
            }

            TOKEN_PRIVILEGES newState = new()
            {
                PrivilegeCount = 1,
                Privileges = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = NativeConstants.SE_PRIVILEGE_ENABLED },
            };
            bool adjusted = AdjustTokenPrivileges(token, ref newState);
            int adjustError = Marshal.GetLastWin32Error();
            _logger.Debug("AdjustTokenPrivileges({Name}, SE_PRIVILEGE_ENABLED) -> {Result}, error {Error}, {Elapsed} ms", NativeConstants.SE_SYSTEM_ENVIRONMENT_NAME, adjusted, adjustError, stopwatch.Elapsed.TotalMilliseconds);
            if (!adjusted)
            {
                LastDetail = "AdjustTokenPrivileges failed: " + NativeError.DescribeWin32(adjustError);
                return PrivilegeState.ElevatedPrivilegeAbsent;
            }

            if (adjustError == NativeConstants.ERROR_NOT_ALL_ASSIGNED)
            {
                LastDetail = "AdjustTokenPrivileges reported " + NativeError.DescribeWin32(adjustError);
                return PrivilegeState.ElevatedPrivilegeAbsent;
            }

            LastDetail = null;
            _logger.Information("SeSystemEnvironmentPrivilege enabled in {Elapsed} ms", stopwatch.Elapsed.TotalMilliseconds);
            return PrivilegeState.Enabled;
        }
        finally
        {
            if (token != IntPtr.Zero)
            {
                Kernel32.CloseHandle(token);
            }
        }
    }

    private static bool AdjustTokenPrivileges(IntPtr token, ref TOKEN_PRIVILEGES newState)
    {
        return AdvApi32.AdjustTokenPrivileges(token, false, ref newState, 0, IntPtr.Zero, IntPtr.Zero);
    }
}
