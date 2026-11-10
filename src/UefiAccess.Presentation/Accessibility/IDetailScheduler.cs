// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Presentation.Accessibility;

/// <summary>
/// Decides when the detail pane is updated after the selection changed. Every arrow key in the
/// variable list changes the selection, and rebuilding the pane (every section, the full hex
/// dump, and the layout the view then does) on each key press kept the UI thread busy while the
/// screen reader was still fetching the new row's name, so under fast key repeat the name was
/// sometimes dropped (Alpha 1 finding A1-6). The shell therefore hands the update to a scheduler
/// instead of running it inside the selection change; the update itself is made in place
/// (<see cref="ViewModels.VariableDetailViewModel.Show"/> keeps the sections and fields both rows
/// have in the same order and changes only their values), so the view no longer removes and
/// re-creates every section, which the A16 measurement found raised a burst of UI Automation
/// events that delayed the next row's focus event. <see cref="ImmediateDetailScheduler"/> runs
/// it at once (tests, and any host without a message loop); the WPF application runs the
/// <see cref="CoalescingDetailScheduler"/> policy over its dispatcher: the update is held back
/// while the key that changed the selection is still down, then waits for a quiet period of no key
/// or mouse button activity in the list (<see cref="DetailRefreshTiming.QuietPeriod"/>, Alpha 2
/// finding A2-7), then is posted at a priority below input, so it never runs inside a key press
/// nor between two presses of a burst, and the refreshes scheduled while one is waiting are merged
/// into one that shows the row selected when it runs. The contract itself names no time: when a
/// deferred refresh runs is the scheduler's choice, and <see cref="Flush"/> lets the shell run it
/// at once when the pane is about to be read.
/// </summary>
public interface IDetailScheduler
{
    /// <summary>
    /// Runs the refresh later, or at once for an immediate scheduler. When a refresh is already
    /// pending, the new one replaces it, so at most one refresh runs once the pending work has
    /// drained: the shell's refresh reads the selection at the time it runs, which makes the
    /// replaced ones redundant.
    /// </summary>
    /// <param name="refresh">The refresh to run; it updates the detail pane from the current selection.</param>
    void Schedule(Action refresh);

    /// <summary>
    /// Runs the pending refresh now, when there is one, so the detail pane is current before
    /// keyboard focus moves into it or a command reads it; afterwards nothing is pending. Does
    /// nothing when no refresh is pending.
    /// </summary>
    void Flush();
}
