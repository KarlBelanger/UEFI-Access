// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Settings.Dell;

namespace UefiAccess.Presentation.Services;

/// <summary>
/// Where the window's Dell BIOS settings come from: this computer,
/// read once per press of Read Dell settings (Alt+S) and never at start-up, on Refresh or on Open
/// baseline folder. A read changes nothing: Core's reader only reads, and nothing here can change a
/// Dell BIOS setting. The session cache is the window's (<c>DellSettingsViewModel</c>), not the
/// source's.
/// </summary>
public interface IDellSettingsSource
{
    /// <summary>
    /// Reads the agreed Dell BIOS settings of this computer off the caller's thread. Core bounds
    /// the read (its read budget), so the task always ends; a read still in flight is handed to a
    /// second caller instead of starting another.
    /// </summary>
    /// <returns>The read's result; its settings are always one per agreed setting.</returns>
    Task<DellSettingsResult> ReadAsync();
}
