// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Variables;

namespace UefiAccess.Core.Safety;

/// <summary>
/// The facade the CLI and the GUI call: the dry run (stages S1 to S10) and the commit (stages
/// S11 to S17) A prepare never writes the firmware and creates no
/// transaction folder (the two data roots are created at S9 when missing, and one record line
/// is appended); a commit runs on a plan the same pipeline prepared, with a token whose hash
/// equals the plan's, and makes at most one native call. Every outcome, blocked or written, is
/// on disk before the caller sees it. Both calls run their stages on a worker thread.
/// </summary>
public interface IWritePipeline
{
    /// <summary>
    /// Runs stages S1 to S10 on <paramref name="request"/>: the preconditions, the machine identity
    /// and the unit check, a fresh enumeration, the whitelist and format rules, the policy findings, the BitLocker
    /// assessment, the data roots and the write budget, then the plan. Never writes the firmware.
    /// The BitLocker choice is left to the confirmation (the application's dialog). Cancellation
    /// is honoured between stages and gives a result whose record is
    /// <see cref="WriteRecordOutcome.Cancelled"/>; it never throws for a cancellation.
    /// </summary>
    /// <param name="request">The typed request; a restore is resolved to one of the three operations first (stage S0).</param>
    /// <param name="token">Checked between stages.</param>
    /// <returns>The plan when every check passed, or the check that refused and its sentence.</returns>
    Task<PrepareResult> PrepareAsync(ChangeRequest request, CancellationToken token);

    /// <summary>
    /// Runs stages S1 to S10 with the BitLocker choice given together with the request (the
    /// command-line tool's --bitlocker), so stage S8 can refuse when the state needs a choice and
    /// none was made: <see cref="BitLockerChoice.NotNeeded"/> stands for "no choice given", and a
    /// suspend where none is offered refuses too. Null leaves the choice to the confirmation,
    /// exactly as the two-argument overload does. Every implementation provides it: a default
    /// that ignored the choice would drop S8's refusal of a dry run without --bitlocker.
    /// </summary>
    /// <param name="request">The typed request.</param>
    /// <param name="bitLockerChoice">The choice made with the request, or null to leave it to the confirmation.</param>
    /// <param name="token">Checked between stages.</param>
    /// <returns>The plan when every check passed, or the check that refused and its sentence.</returns>
    Task<PrepareResult> PrepareAsync(ChangeRequest request, BitLockerChoice? bitLockerChoice, CancellationToken token);

    /// <summary>
    /// Runs stages S11 to S17 on a plan this pipeline prepared: the confirmation, the snapshot,
    /// the drift reads, the optional one-restart suspend, the one native call, the verification
    /// read and the record. Not cancellable. A plan is committed at most once; a second commit of
    /// the same plan, or a plan this pipeline did not prepare, is refused at the confirmation.
    /// </summary>
    /// <param name="plan">The plan <see cref="PrepareAsync(ChangeRequest, CancellationToken)"/> returned.</param>
    /// <param name="token">The token <c>plan.Confirm(choice)</c> issued.</param>
    /// <returns>The outcome with its record, sentence and exit code.</returns>
    Task<TransactionResult> CommitAsync(ChangePlan plan, ConfirmationToken token);

    /// <summary>
    /// Records that the person cancelled <paramref name="plan"/> at the confirmation (stage S11,
    /// "Cancel gives Cancelled (a record, no folder)"): one cancelled record line, no folder, and
    /// the plan can no longer be committed. Every implementation records it.
    /// </summary>
    /// <param name="plan">The plan the dialog showed.</param>
    /// <returns>The cancelled result with its record.</returns>
    Task<TransactionResult> CancelAsync(ChangePlan plan);
}

/// <summary>What a prepare returned (stage S10, or the stage that refused).</summary>
/// <param name="Plan">The plan when every check passed and the request was not a <see cref="CheckRequest"/>; otherwise null.</param>
/// <param name="Checks">Every check that ran, in stage order, the failed one last.</param>
/// <param name="BlockedBy">The name of the check that refused, or null.</param>
/// <param name="Sentence">The sentence the CLI prints and the shell announces (a refusal is the bare reason; the shell prefixes it).</param>
/// <param name="Record">The record that was appended to write-log.jsonl.</param>
public sealed record PrepareResult(
    ChangePlan? Plan,
    IReadOnlyList<CheckResult> Checks,
    string? BlockedBy,
    string Sentence,
    WriteRecord Record)
{
    /// <summary>True when every check passed (a plan exists, or the request was a check that passed).</summary>
    public bool Passed => BlockedBy is null && Record.Outcome == WriteRecordOutcome.Prepared;

    /// <summary>True when the caller cancelled the checks between two stages.</summary>
    public bool IsCancelled => Record.Outcome == WriteRecordOutcome.Cancelled;

    /// <summary>The exit code the CLI returns for this dry run: 0 when it passed, 7 when a check refused (3 or 4 at the preconditions).</summary>
    public int ExitCode => Record.ExitCode;

    /// <summary>The BitLocker assessment of stage S8, also when a later stage refused; null when S8 did not run.</summary>
    public BitLockerAssessment? BitLocker { get; init; }

    /// <summary>Every warning sentence the stages produced, in the order they ran (the dialog's Warnings box and the CLI's standard error).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>The Writability verdict of the variable a <see cref="CheckRequest"/> named, from the fresh read; null for other requests or an absent variable.</summary>
    public WritabilityVerdict? Writability { get; init; }
}

/// <summary>The outcomes of a transaction.</summary>
public enum TransactionOutcome
{
    /// <summary>A check refused the change before any call; nothing was written.</summary>
    Blocked,

    /// <summary>The person cancelled; nothing was written.</summary>
    Cancelled,

    /// <summary>The call succeeded and the verification read shows the proposed value.</summary>
    Verified,

    /// <summary>The call failed and the verification read shows the previous value: nothing changed.</summary>
    ApiRefused,

    /// <summary>The call succeeded and the value read back differs or could not be read, or the call failed and the value read back is neither the old nor the new one.</summary>
    Unverified,

    /// <summary>The call failed but the verification read shows the proposed value.</summary>
    ChangedDespiteError,
}

/// <summary>What the one verification read of stage S16 found.</summary>
/// <param name="ReadStatus">The read's status; <see cref="Variables.ReadStatus.NotFound"/> is the expected answer after a clear.</param>
/// <param name="MatchesAfter">True when the firmware holds the proposed value (for a clear: the variable is absent).</param>
/// <param name="MatchesBefore">True when the firmware holds the value before the change (absent when it was absent).</param>
/// <param name="Sha256">The lowercase SHA-256 of the bytes read, or null when nothing was read.</param>
/// <param name="Attributes">The attributes read, or null when nothing was read.</param>
public sealed record VerificationResult(ReadStatus ReadStatus, bool MatchesAfter, bool MatchesBefore, string? Sha256, uint? Attributes);

/// <summary>What a commit (or a cancel at the confirmation) returned.</summary>
/// <param name="Outcome">The outcome.</param>
/// <param name="Plan">The plan the commit ran on.</param>
/// <param name="SnapshotFolder">The full path of the transaction's snapshot folder, or null when stage S12 did not create one.</param>
/// <param name="Api">What the one native call returned, or null when it was not made.</param>
/// <param name="Verification">What the verification read found, or null when it was not made.</param>
/// <param name="Record">The record, as appended to write-log.jsonl.</param>
/// <param name="Sentence">The Status sentence and the CLI's line (a refusal is the bare reason; the shell prefixes it).</param>
/// <param name="ExitCode">The CLI's exit code, one of <see cref="WriteExitCodes"/>.</param>
public sealed record TransactionResult(
    TransactionOutcome Outcome,
    ChangePlan? Plan,
    string? SnapshotFolder,
    WriteOutcome? Api,
    VerificationResult? Verification,
    WriteRecord Record,
    string Sentence,
    int ExitCode)
{
    /// <summary>The name of the check that refused the change, or null.</summary>
    public string? BlockedBy => Record.BlockedBy;

    /// <summary>True when the native call was made.</summary>
    public bool ApiCalled => Record.ApiCalled;

    /// <summary>True when stage S13 suspended BitLocker for one restart (it stays suspended whatever happened after).</summary>
    public bool BitLockerSuspended { get; init; }
}

/// <summary>
/// The exit codes of a write attempt, owned by Core because the record
/// carries them; the command-line tool returns the same numbers.
/// </summary>
public static class WriteExitCodes
{
    /// <summary>The dry run passed, the change was verified, or the person cancelled: nothing failed.</summary>
    public const int Success = 0;

    /// <summary>Stage S1: the machine did not boot in UEFI mode.</summary>
    public const int NotUefi = 3;

    /// <summary>Stage S1: the process is not elevated or the privilege could not be enabled.</summary>
    public const int NotElevated = 4;

    /// <summary>A safeguard refused the change before any call; nothing was written.</summary>
    public const int Blocked = 7;

    /// <summary>The call was made, the firmware refused it and the verification read shows the old value; a snapshot exists.</summary>
    public const int ApiRefused = 8;

    /// <summary>The value read back is not what the call's result implies; a snapshot exists and a restore is advised.</summary>
    public const int Unverified = 9;
}
