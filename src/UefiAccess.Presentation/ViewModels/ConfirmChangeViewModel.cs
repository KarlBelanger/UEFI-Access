// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Safety;
using UefiAccess.Presentation.Infrastructure;
using UefiAccess.Presentation.Resources;

namespace UefiAccess.Presentation.ViewModels;

/// <summary>
/// What the confirmation dialog returned: whether the person pressed
/// Confirm change, and whether the suspend check box was ticked then. The shell turns it into
/// <c>plan.Confirm(choice)</c>, so the commit is bound to the plan the dialog showed.
/// </summary>
/// <param name="Confirmed">True for Confirm change; false for Cancel, Escape or the close button.</param>
/// <param name="SuspendChosen">True when the suspend check box was offered and ticked.</param>
public sealed record ConfirmDecision(bool Confirmed, bool SuspendChosen)
{
    /// <summary>The decision of a dialog that was cancelled, or of a platform without dialogs.</summary>
    public static ConfirmDecision Cancelled { get; } = new(false, false);
}

/// <summary>
/// The confirmation dialog <c>Confirm change</c>: its read-only boxes
/// in tab order (What will change, Before, After, BitLocker state, Risk, Recovery key, Warnings,
/// Snapshot folder), the suspend check box, present only when the pipeline offers the suspend and
/// ticked by default then, and the confirm button, whose automation name names the suspend while
/// the box is ticked. What will change ends with <see cref="Strings.SuspendSuffix"/> while the box
/// is ticked, so what is confirmed names the suspend; neither text changes anything else, and the
/// dialog announces nothing (it has no live region). <see cref="Confirm"/> and <see cref="Cancel"/>
/// give the <see cref="ConfirmDecision"/> the dialog returns; the dialog itself only shows.
/// </summary>
public sealed class ConfirmChangeViewModel : ObservableObject
{
    private bool _suspendChosen;

    /// <summary>Creates the dialog's view model for a plan.</summary>
    /// <param name="plan">The plan the dry run returned.</param>
    /// <param name="snapshotRoot">The snapshot root the change's folder is written under.</param>
    /// <param name="fromPendingSnapshot">True when the plan restores a snapshot whose own change did not complete.</param>
    /// <exception cref="ArgumentNullException"><paramref name="plan"/> or <paramref name="snapshotRoot"/> is null.</exception>
    public ConfirmChangeViewModel(ChangePlan plan, string snapshotRoot, bool fromPendingSnapshot = false)
    {
        Summary = ChangeSummary.From(plan, snapshotRoot, fromPendingSnapshot);
        _suspendChosen = Summary.SuspendOffered;
    }

    /// <summary>The plan the dialog shows, and the one a confirmation is bound to.</summary>
    public ChangePlan Plan => Summary.Plan;

    /// <summary>Every text of the dialog.</summary>
    public ChangeSummary Summary { get; }

    /// <summary>The window title.</summary>
    public string Title => Strings.ConfirmChangeTitle;

    /// <summary>The What will change box (tab 1, focus on open, selected whole): one sentence, ending with the suspend sentence while the check box is ticked.</summary>
    public string WhatWillChangeText => SuspendOffered && _suspendChosen ? Summary.WhatWillChangeText + Strings.SuspendSuffix : Summary.WhatWillChangeText;

    /// <summary>The Before box (tab 2, multi-line).</summary>
    public string BeforeText => Summary.BeforeText;

    /// <summary>The After box (tab 3, multi-line).</summary>
    public string AfterText => Summary.AfterText;

    /// <summary>The BitLocker state box (tab 4).</summary>
    public string BitLockerStateText => Summary.BitLockerStateText;

    /// <summary>The Risk box (tab 5).</summary>
    public string RiskText => Summary.RiskText;

    /// <summary>The Recovery key box (tab 6).</summary>
    public string RecoveryKeyText => Summary.RecoveryKeyText;

    /// <summary>The Warnings box (tab 7, multi-line; its first line says how many lines follow).</summary>
    public string WarningsText => Summary.WarningsText;

    /// <summary>The Snapshot folder box (tab 8).</summary>
    public string SnapshotFolderText => Summary.SnapshotFolderText;

    /// <summary>True when the suspend check box (tab 9) is shown: BitLocker On, Encrypting or Unknown and a reorder or a one-time boot.</summary>
    public bool SuspendOffered => Summary.SuspendOffered;

    /// <summary>
    /// The suspend check box, two-way: ticked by default when offered. Changing it changes
    /// <see cref="WhatWillChangeText"/> and <see cref="ConfirmButtonName"/>; it stays false when the
    /// suspend is not offered.
    /// </summary>
    public bool SuspendChosen
    {
        get => _suspendChosen;
        set
        {
            if (SetProperty(ref _suspendChosen, value && SuspendOffered))
            {
                OnPropertyChanged(nameof(WhatWillChangeText));
                OnPropertyChanged(nameof(ConfirmButtonName));
            }
        }
    }

    /// <summary>The confirm button's automation name (tab 10): <see cref="Strings.ConfirmChangeAndSuspendName"/> while the check box is ticked, <see cref="Strings.ConfirmChangeName"/> otherwise.</summary>
    public string ConfirmButtonName => SuspendOffered && _suspendChosen ? Strings.ConfirmChangeAndSuspendName : Strings.ConfirmChangeName;

    /// <summary>The decision of Confirm change (Alt+F, or Space or Enter on the button).</summary>
    /// <returns>Confirmed, with the check box as it stands.</returns>
    public ConfirmDecision Confirm() => new(true, SuspendOffered && _suspendChosen);

    /// <summary>The decision of Cancel, Escape or the close button.</summary>
    /// <returns><see cref="ConfirmDecision.Cancelled"/>.</returns>
    public ConfirmDecision Cancel() => ConfirmDecision.Cancelled;

    /// <summary>
    /// The BitLocker choice a decision stands for: when the suspend was offered, the one-restart
    /// suspend for a ticked box and going on with protection for a cleared one; otherwise no
    /// choice is needed. This is the rule the pipeline checks at stage S11.
    /// </summary>
    /// <param name="decision">The dialog's decision.</param>
    /// <returns>The choice to confirm the plan with.</returns>
    public BitLockerChoice ChoiceFor(ConfirmDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (!SuspendOffered)
        {
            return BitLockerChoice.NotNeeded;
        }

        return decision.SuspendChosen ? BitLockerChoice.SuspendForOneRestart : BitLockerChoice.KeepProtection;
    }
}
