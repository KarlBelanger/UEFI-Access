// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Presentation.ViewModels;

namespace UefiAccess.Presentation.Services;

/// <summary>
/// The few things a view model needs from the desktop: the standard folder and save-file dialogs,
/// the clipboard, and the confirmation dialog of a change. The WPF App implements it with
/// Microsoft.Win32 dialogs, System.Windows Clipboard and its own Confirm change window; tests use
/// <see cref="NullPlatformInteractions"/>. None of these touch the firmware: the confirmation
/// dialog only shows the plan and returns the person's decision.
/// </summary>
public interface IPlatformInteractions
{
    /// <summary>Shows the standard folder picker.</summary>
    /// <param name="title">The dialog title.</param>
    /// <returns>The chosen folder, or null when the user cancelled.</returns>
    string? PickFolder(string title);

    /// <summary>Shows the standard save-file dialog.</summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="defaultFileName">The suggested file name.</param>
    /// <param name="filter">The file-type filter in the "Description|*.ext|..." form.</param>
    /// <returns>The chosen path, or null when the user cancelled.</returns>
    string? PickSaveFile(string title, string defaultFileName, string filter);

    /// <summary>Places text on the clipboard.</summary>
    /// <param name="text">The text.</param>
    void SetClipboardText(string text);

    /// <summary>
    /// Shows the confirmation dialog <c>Confirm change</c> modally and
    /// returns what the person decided: <see cref="ConfirmChangeViewModel.Confirm"/> for Confirm
    /// change, <see cref="ConfirmChangeViewModel.Cancel"/> for Cancel, Escape or the close button.
    /// </summary>
    /// <param name="viewModel">The dialog's texts and its check box.</param>
    /// <returns>The decision.</returns>
    ConfirmDecision ConfirmChange(ConfirmChangeViewModel viewModel);
}
