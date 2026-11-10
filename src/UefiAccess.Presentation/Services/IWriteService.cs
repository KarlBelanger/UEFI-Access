// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Safety;

namespace UefiAccess.Presentation.Services;

/// <summary>
/// The write pipeline as the application uses it: the dry run and the
/// commit of <see cref="IWritePipeline"/> on a worker thread, the record of a dialog that was
/// cancelled, and the newest snapshot of a variable for Restore previous value. The dry run can be
/// cancelled between its stages; the commit is never cancelled and never replaced. No member
/// throws for anything the pipeline meets: a refusal is a result, not an exception.
/// </summary>
public interface IWriteService
{
    /// <summary>The full path of the snapshot root, the folder every change's snapshot is written under.</summary>
    string SnapshotRoot { get; }

    /// <summary>
    /// Runs the dry run (stages S1 to S10) on a worker thread. It never writes the firmware. When
    /// <paramref name="token"/> is cancelled before the pipeline answers, the task completes at once
    /// with null: the checks stop at their next stage boundary in the background, where the pipeline
    /// records the cancel, and a plan they may still make is cancelled too, so it can never be committed.
    /// </summary>
    /// <param name="request">The typed request (entry numbers or a snapshot folder; never bytes).</param>
    /// <param name="token">Escape in the Change group while the checks run.</param>
    /// <returns>The pipeline's result, or null when the caller cancelled before it answered.</returns>
    Task<PrepareResult?> PrepareAsync(ChangeRequest request, CancellationToken token);

    /// <summary>Runs the commit (stages S11 to S17) on a worker thread. Not cancellable, and nothing replaces it.</summary>
    /// <param name="plan">The plan the dry run returned and the dialog showed.</param>
    /// <param name="confirmation">The token <c>plan.Confirm(choice)</c> issued for the dialog's decision.</param>
    /// <returns>The outcome with its record and sentence.</returns>
    Task<TransactionResult> CommitAsync(ChangePlan plan, ConfirmationToken confirmation);

    /// <summary>Records that the person cancelled the dialog for <paramref name="plan"/>: one cancelled record, no folder, and the plan can no longer be committed.</summary>
    /// <param name="plan">The plan the dialog showed.</param>
    /// <returns>The cancelled result.</returns>
    Task<TransactionResult> CancelAsync(ChangePlan plan);

    /// <summary>
    /// The newest snapshot of a variable that Restore previous value offers: its record says the
    /// value may have changed (Verified, Unverified, ChangedDespiteError or Pending). Reads the
    /// snapshot root only; never composes the pipeline. Never throws.
    /// </summary>
    /// <param name="key">The variable key "guid:name".</param>
    /// <returns>The snapshot, or null when there is none.</returns>
    SnapshotSummary? Newest(string key);
}
