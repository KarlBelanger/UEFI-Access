// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Safety;

namespace UefiAccess.Cli;

/// <summary>
/// Process exit codes of the uefiaccess tool.
/// The codes a write attempt returns are defined by Core's <see cref="WriteExitCodes"/>, because
/// the write record carries them; the constants below that share a number with it are defined as
/// that constant, so each number has one source.
/// </summary>
internal static class ExitCodes
{
    /// <summary>The command completed (for a write: the dry run passed, or the change was verified).</summary>
    public const int Success = WriteExitCodes.Success;

    /// <summary>The command line could not be parsed or an option value was invalid.</summary>
    public const int Usage = 2;

    /// <summary>The machine did not boot in UEFI mode (or the firmware type is unknown), so variables are unreachable.</summary>
    public const int NotUefi = WriteExitCodes.NotUefi;

    /// <summary>The process is not elevated or SeSystemEnvironmentPrivilege could not be enabled.</summary>
    public const int NotElevated = WriteExitCodes.NotElevated;

    /// <summary>A native call (enumeration, read, export) failed.</summary>
    public const int NativeFailure = 5;

    /// <summary>The command succeeded but some variables were unreadable or some output was incomplete.</summary>
    public const int Partial = 6;

    /// <summary>A safeguard refused the change before any write call; nothing was written.</summary>
    public const int Blocked = WriteExitCodes.Blocked;

    /// <summary>The write call was made, the firmware refused it and the value read back is the old one; nothing changed and a snapshot exists.</summary>
    public const int ApiRefused = WriteExitCodes.ApiRefused;

    /// <summary>The value read back after the write call is not what the call's result implies; a snapshot exists and a restore is advised.</summary>
    public const int Unverified = WriteExitCodes.Unverified;
}
