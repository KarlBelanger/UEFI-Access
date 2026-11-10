// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Core.Platform;

/// <summary>Detects whether the machine booted through UEFI or legacy BIOS.</summary>
public interface IFirmwareTypeDetector
{
    /// <summary>Detects the firmware type. Never throws; returns <see cref="FirmwareType.Unknown"/> on failure.</summary>
    /// <returns>The detected firmware type.</returns>
    FirmwareType Detect();
}
