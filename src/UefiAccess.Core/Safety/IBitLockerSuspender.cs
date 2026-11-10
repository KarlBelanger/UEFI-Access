// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Core.Safety;

/// <summary>
/// What the one-restart suspend of stage S13 did and found. Three
/// outcomes block the transaction with nothing written: a nonzero return, a return of 0 whose
/// confirmation re-read does not show the suspend, and a call that did not answer within its
/// bound (the call keeps running and may still complete). A read before the call that fails
/// blocks too, with the call not made. <see cref="Sentence"/> is null only when
/// <see cref="Succeeded"/> is true.
/// </summary>
/// <param name="Succeeded">True when the call returned 0 and the re-read confirmed the suspend.</param>
/// <param name="Called">True when the suspend call was made (a failed read before it leaves it false).</param>
/// <param name="DisableCount">The restart count passed to the call: always 1.</param>
/// <param name="ReturnValue">The call's ReturnValue; null when it was not made, threw or did not answer.</param>
/// <param name="TimedOut">True when the suspend call did not answer within its bound, so it may still complete.</param>
/// <param name="ProtectionStatusBefore">ProtectionStatus read immediately before the call; null when unreadable or not read.</param>
/// <param name="ProtectionStatusAfter">ProtectionStatus re-read after a return of 0 (the confirmation of an On or Unknown state); null when not read.</param>
/// <param name="StateAfter">The state to record: <see cref="BitLockerState.Suspended"/> when confirmed, otherwise <see cref="BitLockerState.Unknown"/>.</param>
/// <param name="ResultText">The record's suspendResult: "0", the HRESULT in hex, "timeout", "failed" or "not called".</param>
/// <param name="Sentence">The blocking sentence; null when the suspend succeeded.</param>
/// <param name="Elapsed">How long the whole suspend took, the reads before and after the call included.</param>
public sealed record SuspendResult(
    bool Succeeded,
    bool Called,
    uint DisableCount,
    uint? ReturnValue,
    bool TimedOut,
    int? ProtectionStatusBefore,
    int? ProtectionStatusAfter,
    BitLockerState StateAfter,
    string ResultText,
    string? Sentence,
    TimeSpan Elapsed);

/// <summary>
/// The one BitLocker write of the pipeline: a suspend of the OS volume's key protectors for one
/// restart, made only at stage S13 after the person chose it, after the snapshot exists and after
/// the first drift read passed. There is no resume: Windows resumes protection at the restart.
/// <see cref="BitLockerSuspender"/> is the production implementation, constructed only by the
/// composition roots; a test passes a fake.
/// </summary>
public interface IBitLockerSuspender
{
    /// <summary>
    /// Suspends every key protector of the volume for exactly one restart, with one call, and
    /// confirms the suspend with a re-read. Never throws; every failure is in the result.
    /// </summary>
    /// <param name="driveLetter">The OS volume's drive letter, for example "C:".</param>
    /// <returns>The result, with the blocking sentence when the suspend is not confirmed.</returns>
    SuspendResult SuspendForOneRestart(string driveLetter);
}
