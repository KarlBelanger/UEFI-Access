// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Core.Safety;

/// <summary>
/// What the person chose about BitLocker when confirming a change. While the state is On, Encrypting or Unknown one of the two real choices is needed;
/// stage S11 refuses <see cref="NotNeeded"/> then.
/// </summary>
public enum BitLockerChoice
{
    /// <summary>No choice was needed: protection is off, or suspended already (no second suspend is offered).</summary>
    NotNeeded,

    /// <summary>Suspend BitLocker for exactly one restart before the write (the dialog's checkbox ticked, the CLI's "--bitlocker suspend").</summary>
    SuspendForOneRestart,

    /// <summary>Write with protection on, knowing the next start may ask for the recovery key (the checkbox cleared, the CLI's "--bitlocker continue").</summary>
    KeepProtection,
}

/// <summary>
/// The person's confirmation of one exact plan. It is created only by
/// <see cref="ChangePlan.Confirm"/> and carries the plan's hash, so the commit can refuse a token
/// that belongs to other bytes, and the BitLocker choice made in the same dialog or command.
/// </summary>
public sealed class ConfirmationToken
{
    /// <summary>Creates a token. Internal: <see cref="ChangePlan.Confirm"/> is its only caller.</summary>
    /// <param name="planHash">The <see cref="ChangePlan.PlanHash"/> of the plan being confirmed.</param>
    /// <param name="choice">The BitLocker choice made with the confirmation.</param>
    /// <param name="issuedAt">When the confirmation was given.</param>
    /// <exception cref="ArgumentNullException"><paramref name="planHash"/> is null.</exception>
    internal ConfirmationToken(string planHash, BitLockerChoice choice, DateTimeOffset issuedAt)
    {
        ArgumentNullException.ThrowIfNull(planHash);
        PlanHash = planHash;
        Choice = choice;
        IssuedAt = issuedAt;
    }

    /// <summary>The <see cref="ChangePlan.PlanHash"/> of the plan that was confirmed.</summary>
    public string PlanHash { get; }

    /// <summary>The BitLocker choice made with the confirmation.</summary>
    public BitLockerChoice Choice { get; }

    /// <summary>When the confirmation was given.</summary>
    public DateTimeOffset IssuedAt { get; }

    /// <summary>True when this token was issued for the given plan (the hashes are equal).</summary>
    /// <param name="plan">The plan about to be committed.</param>
    /// <returns>True when the hashes are equal, ordinally.</returns>
    public bool Matches(ChangePlan plan)
    {
        return plan is not null && string.Equals(PlanHash, plan.PlanHash, StringComparison.Ordinal);
    }
}
