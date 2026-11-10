// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Core.Platform;

/// <summary>Firmware type of the local computer as reported by GetFirmwareType.</summary>
public enum FirmwareType
{
    /// <summary>The firmware type could not be determined (call failed or not running on Windows).</summary>
    Unknown,

    /// <summary>The machine booted through legacy BIOS (CSM); firmware variables are unavailable.</summary>
    LegacyBios,

    /// <summary>The machine booted through UEFI; runtime variable services are available.</summary>
    Uefi,
}
