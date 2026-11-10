// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Presentation.Accessibility;

/// <summary>
/// The places a view model may ask the view to move keyboard focus to. Every move is deliberate,
/// happens only in response to a user action or the completion of one, and is documented in
/// docs/Keyboard-Map.md.
/// </summary>
public enum FocusTarget
{
    /// <summary>No focus change.</summary>
    None,

    /// <summary>The first row of the variable list (after a successful load).</summary>
    ListFirstRow,

    /// <summary>The selected row of the variable list, or the first row when nothing is selected (Escape from the filter or the detail pane).</summary>
    ListSelectedRow,

    /// <summary>The filter box, with its text selected (Ctrl+F).</summary>
    Filter,

    /// <summary>The Status box (after a refusal or a failure).</summary>
    Status,

    /// <summary>The first field of the detail pane (Enter or Space on a row).</summary>
    DetailFirstField,

    /// <summary>The first button of the toolbar.</summary>
    Toolbar,

    /// <summary>The log panel.</summary>
    LogPanel,

    /// <summary>
    /// The selected row of the Boot order entries list in the Change group (Ctrl+Z puts the
    /// firmware's order back and focus on the selected row). A move never asks for it: the moved
    /// row keeps its container and its focus; the view uses this target only if WPF regenerated
    /// that container.
    /// </summary>
    BootOrderEntry,

    /// <summary>
    /// The Change group button that opened the confirmation dialog, named by
    /// <see cref="ViewModels.ChangeViewModel.LastAction"/> (after Cancel or Escape in the dialog).
    /// </summary>
    ChangeControl,

    /// <summary>
    /// The list row of the variable <see cref="ViewModels.ShellViewModel.FocusRowKey"/> names (the
    /// variable just written, after the live reload), or the first row when no visible row has
    /// that key.
    /// </summary>
    ListRowByKey,

    /// <summary>
    /// The selected row of the list of Dell BIOS settings (after a Dell read that answered, while
    /// the person has not moved on, and on arrival in the Dell settings region); the Read Dell
    /// settings button when that row cannot take focus. There is
    /// always a selected row: the rows exist from the start.
    /// </summary>
    DellSelectedRow,
}
