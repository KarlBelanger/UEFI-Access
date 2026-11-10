// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Presentation.ViewModels;

namespace UefiAccess.Presentation.Services;

/// <summary>
/// An <see cref="IPlatformInteractions"/> without a desktop: every dialog returns null (as if
/// cancelled), the confirmation dialog of a change is cancelled, so nothing is ever confirmed
/// through it, and the clipboard text is kept in <see cref="LastClipboardText"/> for tests.
/// </summary>
public sealed class NullPlatformInteractions : IPlatformInteractions
{
    /// <summary>The text most recently placed on the clipboard, or null when none was.</summary>
    public string? LastClipboardText { get; private set; }

    /// <summary>The folder every <see cref="PickFolder"/> call returns; null (the default) means cancelled.</summary>
    public string? FolderToReturn { get; set; }

    /// <summary>The path every <see cref="PickSaveFile"/> call returns; null (the default) means cancelled.</summary>
    public string? FileToReturn { get; set; }

    /// <summary>The number of confirmation dialogs asked for, each of them cancelled.</summary>
    public int ConfirmDialogsShown { get; private set; }

    /// <inheritdoc />
    public string? PickFolder(string title) => FolderToReturn;

    /// <inheritdoc />
    public string? PickSaveFile(string title, string defaultFileName, string filter) => FileToReturn;

    /// <inheritdoc />
    public void SetClipboardText(string text) => LastClipboardText = text;

    /// <inheritdoc />
    public ConfirmDecision ConfirmChange(ConfirmChangeViewModel viewModel)
    {
        ConfirmDialogsShown++;
        return ConfirmDecision.Cancelled;
    }
}
