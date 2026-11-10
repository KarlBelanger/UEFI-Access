// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Baseline;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Privileges;
using UefiAccess.Core.Variables;

namespace UefiAccess.Presentation.Services;

/// <summary>The result of loading a variable source. Never carries an exception; failures are text.</summary>
/// <param name="Status">Loaded, refused (precondition) or failed.</param>
/// <param name="SourceDescription">"Live firmware" or "Baseline (offline): &lt;folder&gt;" (plus the baseline timestamp when known).</param>
/// <param name="IsOffline">True for a baseline folder; false for the live machine.</param>
/// <param name="FirmwareType">The firmware type detected (or recorded in the baseline).</param>
/// <param name="Privilege">The privilege state after the enable attempt, or null when the attempt was skipped or does not apply.</param>
/// <param name="Variables">The variables; empty unless <paramref name="Status"/> is <see cref="SourceStatus.Loaded"/>.</param>
/// <param name="Method">How the variables were obtained.</param>
/// <param name="Warnings">Non-fatal observations, for example fallback reads that failed.</param>
/// <param name="Identity">The machine identity (live, or from the baseline index), or null when unavailable.</param>
/// <param name="Message">The refusal sentence or the failure text; null when loaded.</param>
public sealed record SourceResult(
    SourceStatus Status,
    string SourceDescription,
    bool IsOffline,
    FirmwareType FirmwareType,
    PrivilegeState? Privilege,
    IReadOnlyList<FirmwareVariable> Variables,
    EnumerationMethod Method,
    IReadOnlyList<string> Warnings,
    MachineIdentityResult? Identity,
    string? Message)
{
    /// <summary>
    /// True when the live machine's Windows machine ID is the M3 test unit's (Core's
    /// <see cref="UefiAccess.Core.Safety.EnabledUnit.Check"/> answered Confirmed); false, the
    /// default, for a baseline folder, for a source composed without the unit check and for an ID
    /// that could not be read. Together with the identity it decides whether BootOrder and BootNext
    /// are shown as changeable; the pipeline checks the unit again before any change.
    /// </summary>
    public bool UnitConfirmed { get; init; }

    /// <summary>
    /// The Dell BIOS settings a loaded baseline folder carries (its dell-settings.json, read by
    /// <see cref="DellSettingsFile.Read(string, Serilog.ILogger)"/> on the source's worker): not present for a folder saved without them, a problem
    /// for a file that cannot be used, which never fails the variable load. Null for the live
    /// source and for a baseline load that failed: the live Dell settings are read only by Read Dell
    /// settings (Alt+S), never by a load.
    /// </summary>
    public DellSettingsFileResult? DellBaseline { get; init; }

    /// <summary>
    /// The name of the loaded baseline folder (the last part of its path, without a trailing
    /// separator), as the Dell state line and the region's arrival clause name it; null for the
    /// live source and for a baseline load that failed.
    /// </summary>
    public string? BaselineFolderName { get; init; }
}

/// <summary>
/// Where the variables come from: the live firmware (through the Core store, after the same
/// precondition checks as the CLI) or a baseline folder written by the baseline exporter.
/// Implementations never throw from <see cref="LoadAsync"/>; they return a result whose
/// <see cref="SourceResult.Status"/> and <see cref="SourceResult.Message"/> describe the outcome.
/// </summary>
public interface IVariableSource
{
    /// <summary>Description shown in the window title and the Status box, for example "Live firmware".</summary>
    string Description { get; }

    /// <summary>True for a baseline folder (offline view), false for the live machine.</summary>
    bool IsOffline { get; }

    /// <summary>Loads the variables. Never throws.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The result.</returns>
    Task<SourceResult> LoadAsync(CancellationToken cancellationToken);
}
