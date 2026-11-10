// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Serilog;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Core.Native;

namespace UefiAccess.Core.Platform;

/// <summary>
/// Detects the firmware type through kernel32 GetFirmwareType (1 legacy BIOS, 2 UEFI).
/// On a non-Windows host it returns <see cref="FirmwareType.Unknown"/>. Never throws.
/// </summary>
public sealed class FirmwareTypeDetector : IFirmwareTypeDetector
{
    private readonly ILogger _logger;

    /// <summary>Creates a detector without a logger.</summary>
    public FirmwareTypeDetector()
        : this(null)
    {
    }

    /// <summary>Creates a detector that logs the native call to <paramref name="logger"/>.</summary>
    /// <param name="logger">Optional Serilog logger; null disables logging.</param>
    public FirmwareTypeDetector(ILogger? logger)
    {
        _logger = logger ?? Serilog.Core.Logger.None;
    }

    /// <inheritdoc />
    public FirmwareType Detect()
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger.Debug("GetFirmwareType skipped: not running on Windows");
            return FirmwareType.Unknown;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            bool ok = Kernel32.GetFirmwareType(out uint rawType);
            int error = Marshal.GetLastWin32Error();
            _logger.Debug("GetFirmwareType() -> {Result}, type {Type}, error {Error}, {Elapsed} ms", ok, rawType, error, stopwatch.Elapsed.TotalMilliseconds);
            if (!ok)
            {
                _logger.Warning("GetFirmwareType failed: {Error}", NativeError.DescribeWin32(error));
                return FirmwareType.Unknown;
            }

            return rawType switch
            {
                NativeConstants.FIRMWARE_TYPE_BIOS => FirmwareType.LegacyBios,
                NativeConstants.FIRMWARE_TYPE_UEFI => FirmwareType.Uefi,
                _ => FirmwareType.Unknown,
            };
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "GetFirmwareType threw");
            return FirmwareType.Unknown;
        }
    }
}
