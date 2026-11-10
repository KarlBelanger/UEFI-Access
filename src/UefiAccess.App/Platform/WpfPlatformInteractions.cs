// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Windows;
using Microsoft.Win32;
using Serilog;
using UefiAccess.Presentation.Services;
using UefiAccess.Presentation.ViewModels;

namespace UefiAccess.App.Platform;

/// <summary>
/// The WPF <see cref="IPlatformInteractions"/>: the standard folder picker
/// (<see cref="OpenFolderDialog"/>), the standard save-file dialog (<see cref="SaveFileDialog"/>),
/// the Windows clipboard, and the application's own Confirm change dialog
/// (<see cref="ConfirmChangeWindow"/>). Every dialog is modal to the main window; the two standard
/// ones return null when cancelled, the confirmation returns the person's decision. None of these
/// touch the firmware: the only things ever written here are a file the user chose and the
/// clipboard, and the confirmation dialog only shows the plan the checks made and says whether the
/// person confirmed it (the shell then commits it through the write pipeline).
/// </summary>
public sealed class WpfPlatformInteractions : IPlatformInteractions
{
    private readonly Func<Window?> _owner;
    private readonly ILogger _logger;

    /// <summary>Creates the platform services.</summary>
    /// <param name="owner">Returns the window that owns the dialogs, or null before it exists.</param>
    /// <param name="logger">Optional logger; null disables logging.</param>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> is null.</exception>
    public WpfPlatformInteractions(Func<Window?> owner, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
        _logger = logger ?? Serilog.Core.Logger.None;
    }

    /// <inheritdoc />
    public string? PickFolder(string title)
    {
        OpenFolderDialog dialog = new()
        {
            Title = title ?? string.Empty,
            Multiselect = false,
        };
        bool? result = Show(dialog);
        string? folder = result == true ? dialog.FolderName : null;
        _logger.Debug("Folder dialog {Outcome}", folder is null ? "cancelled" : "chose " + folder);
        return string.IsNullOrWhiteSpace(folder) ? null : folder;
    }

    /// <inheritdoc />
    public string? PickSaveFile(string title, string defaultFileName, string filter)
    {
        SaveFileDialog dialog = new()
        {
            Title = title ?? string.Empty,
            FileName = defaultFileName ?? string.Empty,
            Filter = filter ?? string.Empty,
            AddExtension = true,
            OverwritePrompt = true,
        };
        bool? result = Show(dialog);
        string? file = result == true ? dialog.FileName : null;
        _logger.Debug("Save dialog {Outcome}", file is null ? "cancelled" : "chose " + file);
        return string.IsNullOrWhiteSpace(file) ? null : file;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Not flushed at once (copy: false), finding A2-1 (23 September on the OptiPlex 7020):
    /// Clipboard.SetText flushes the data object right away, retrying while the UI thread sleeps; a
    /// program that reads every clipboard change at once (the Chrome Remote Desktop host there, and
    /// any remote-desktop clipboard sync or clipboard manager) asks this UI thread for the text
    /// meanwhile, which it cannot answer, so the flush failed and the shell said "Copy failed"
    /// after a copy that worked. Unflushed, the text is handed over from the dispatcher when it is
    /// asked for, and the application flushes it when it exits (<see cref="FlushClipboard"/>), so
    /// the last copy outlives it. A failure left here is a real one: the clipboard could not be
    /// opened at all.
    /// </remarks>
    public void SetClipboardText(string text)
    {
        Clipboard.SetDataObject(text ?? string.Empty, copy: false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The dialog is modal to the main window (centred on it) once that window is on screen. Confirm
    /// change gives <see cref="ConfirmChangeViewModel.Confirm"/>, with the check box as it stands
    /// then; Cancel, Escape and the close button give <see cref="ConfirmChangeViewModel.Cancel"/>.
    /// </remarks>
    public ConfirmDecision ConfirmChange(ConfirmChangeViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ConfirmChangeWindow dialog = new(viewModel, _logger);
        Window? owner = _owner();
        if (owner is not null && owner.IsLoaded)
        {
            dialog.Owner = owner;
        }

        bool confirmed = dialog.ShowDialog() == true;
        _logger.Debug("Confirm change dialog {Outcome} for {TransactionId}", confirmed ? "confirmed" : "cancelled", viewModel.Plan.SnapshotFolderName);
        return confirmed ? viewModel.Confirm() : viewModel.Cancel();
    }

    /// <summary>
    /// Renders the last copy onto the clipboard for good, so it stays there after the application
    /// exits (<see cref="SetClipboardText"/> places it unflushed). Called once from App.OnExit, long
    /// after the copy, when no program is waiting for this thread to render it. Best effort: a
    /// failure is logged, never announced (the window is gone), and never stops the exit.
    /// </summary>
    public void FlushClipboard()
    {
        try
        {
            Clipboard.Flush();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Flushing the clipboard at exit failed; the last copy may not stay on the clipboard");
        }
    }

    private bool? Show(CommonDialog dialog)
    {
        Window? owner = _owner();
        return owner is not null && owner.IsLoaded ? dialog.ShowDialog(owner) : dialog.ShowDialog();
    }
}
