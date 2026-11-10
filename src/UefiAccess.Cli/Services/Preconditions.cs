// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Platform;
using UefiAccess.Core.Privileges;

namespace UefiAccess.Cli.Services;

/// <summary>Outcome of the precondition check every command runs first.</summary>
/// <param name="FirmwareType">Detected firmware type.</param>
/// <param name="IsElevated">True when the process token is elevated.</param>
/// <param name="Privilege">
/// Privilege state: null only when the firmware type check failed (the elevation and privilege
/// checks did not run); <see cref="PrivilegeState.NotElevated"/> when the process is not elevated
/// (the privilege is not attempted); otherwise the result of the enable attempt,
/// <see cref="PrivilegeState.Enabled"/> or <see cref="PrivilegeState.ElevatedPrivilegeAbsent"/>.
/// </param>
/// <param name="PrivilegeDetail">One-line reason from the privilege manager when enabling failed, or null.</param>
/// <param name="ExitCode">0 when every precondition holds; otherwise the exit code to return.</param>
/// <param name="Message">The single sentence to print when a precondition fails, or null.</param>
internal sealed record PreconditionResult(
    FirmwareType FirmwareType,
    bool IsElevated,
    PrivilegeState? Privilege,
    string? PrivilegeDetail,
    int ExitCode,
    string? Message)
{
    /// <summary>True when the command may proceed.</summary>
    public bool Ok => ExitCode == ExitCodes.Success;
}

/// <summary>
/// Shared precondition check: firmware type (exit 3 unless UEFI), elevation (exit 4) and
/// SeSystemEnvironmentPrivilege (exit 4). Never throws.
/// </summary>
internal static class Preconditions
{
    /// <summary>The sentence printed when the machine did not boot in UEFI mode or the firmware type is unknown.</summary>
    public const string NotUefiSentence =
        "This system did not boot in UEFI mode (or the OS reports no UEFI runtime services); firmware variables are not reachable from Windows.";

    /// <summary>The sentence printed when the process is not elevated.</summary>
    public const string NotElevatedSentence =
        "This command needs administrator rights; open an elevated command prompt (Run as administrator) and run uefiaccess again.";

    /// <summary>Runs the checks in order and stops at the first failure.</summary>
    /// <param name="services">The services to check with.</param>
    /// <returns>The result.</returns>
    public static PreconditionResult Check(CliServices services)
    {
        FirmwareType firmwareType = FirmwareType.Unknown;
        bool elevated = false;
        try
        {
            firmwareType = services.FirmwareTypeDetector.Detect();
        }
        catch (Exception ex)
        {
            services.Logger.Warning(ex, "Firmware type detection threw");
        }

        try
        {
            elevated = services.PrivilegeManager.IsElevated;
        }
        catch (Exception ex)
        {
            services.Logger.Warning(ex, "Elevation check threw");
        }

        services.Logger.Information("Preconditions: firmware type {FirmwareType}, elevated {Elevated}", firmwareType, elevated);

        if (firmwareType != FirmwareType.Uefi)
        {
            return new PreconditionResult(firmwareType, elevated, null, null, ExitCodes.NotUefi, NotUefiSentence);
        }

        if (!elevated)
        {
            return new PreconditionResult(firmwareType, elevated, PrivilegeState.NotElevated, null, ExitCodes.NotElevated, NotElevatedSentence);
        }

        PrivilegeState state;
        string? detail;
        try
        {
            state = services.PrivilegeManager.EnableSystemEnvironmentPrivilege();
            detail = (services.PrivilegeManager as PrivilegeManager)?.LastDetail;
        }
        catch (Exception ex)
        {
            state = PrivilegeState.ElevatedPrivilegeAbsent;
            detail = ex.GetType().Name + ": " + ex.Message;
        }

        services.Logger.Information("Preconditions: privilege state {PrivilegeState} {Detail}", state, detail ?? string.Empty);

        if (state != PrivilegeState.Enabled)
        {
            string reason = string.IsNullOrWhiteSpace(detail) ? string.Empty : " (" + detail.Trim() + ")";
            string message = state == PrivilegeState.NotElevated
                ? NotElevatedSentence
                : "The process is elevated but SeSystemEnvironmentPrivilege could not be enabled" + reason + "; run uefiaccess as administrator from an account that holds that privilege.";
            return new PreconditionResult(firmwareType, elevated, state, detail, ExitCodes.NotElevated, message);
        }

        return new PreconditionResult(firmwareType, elevated, state, null, ExitCodes.Success, null);
    }
}
