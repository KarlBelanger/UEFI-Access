// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.IO;
using Serilog;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Core.Privileges;

namespace UefiAccess.App.Platform;

/// <summary>
/// The folder this run of the application really logs to, which the start-up log line and the
/// crash message before the window name (review finding R12).
/// <see cref="Logging.Create(string, bool)"/> writes to the shared folder
/// <see cref="Logging.LogDirectory"/> (C:\ProgramData\UefiAccess\logs), but
/// moves the log to the per-user folder <see cref="Logging.UserLogDirectory"/>
/// (%LOCALAPPDATA%\UefiAccess\logs) when an elevated run refuses the shared folder because a
/// standard user could redirect it (a data root made by an older build or test run), or when the
/// shared folder cannot be written; that move is recorded only at the top of the per-user log.
/// Logging does not say which folder it chose, so the application asks the same questions it
/// asked, with its public members and without writing anything:
/// <list type="bullet">
/// <item><description>
/// An elevated run (the normal case: the manifest requires elevation) asks
/// <see cref="Logging.PrepareDataFolderFor(string)"/> about the shared folder, the check Logging
/// made when it created the logger (it only checks now: Logging created the missing folders). A
/// refusal means the log is in the per-user folder, and the refusal is the reason.
/// </description></item>
/// <item><description>
/// Otherwise, once the first event has been written (Logging opens its file on the first write,
/// and moves to the per-user folder when that write fails), the shared folder holds this run's
/// log when today's file is there and this account can open it for writing: the open uses
/// <see cref="FileMode.Open"/>, so it never creates a file, and nothing is written. An elevated
/// run never opens a file in a refused folder.
/// </description></item>
/// </list>
/// Never throws; when a question cannot be answered the shared folder is named, as before.
/// </summary>
/// <param name="Directory">The folder that holds this run's log.</param>
/// <param name="Moved">True when that is the per-user folder instead of the shared one.</param>
/// <param name="Refusal">Why an elevated run did not use the shared folder, or null.</param>
internal sealed record RunLogFolder(string Directory, bool Moved, string? Refusal)
{
    /// <summary>The message template of the log panel's note when the log is not in the shared folder, worded as the note at the top of the per-user log.</summary>
    public const string MovedTemplate = "Note: this run logs to {LogDirectory}, not to the shared log folder {SharedLogDirectory}: {Reason}";

    /// <summary>The message template of that note when the shared folder could not be written (no refusal), worded as the note at the top of the per-user log.</summary>
    public const string UnwritableTemplate = "Note: the shared log folder {SharedLogDirectory} cannot be written, so this run logs to {LogDirectory}.";

    /// <summary>The prefix of the daily log file's name; Logging's rolling file adds yyyyMMdd and ".log" (uefiaccess-20260922.log).</summary>
    private const string DailyFilePrefix = "uefiaccess-";

    /// <summary>
    /// Finds the folder this run logs to. Call it after <see cref="Logging.Create(string, bool)"/>
    /// and before the first line that names the folder: it writes one Debug line through
    /// <paramref name="fileLogger"/> first, so Logging has opened (or failed to open) its file.
    /// </summary>
    /// <param name="fileLogger">The logger <see cref="Logging.Create(string, bool)"/> returned.</param>
    /// <returns>The folder, never null.</returns>
    public static RunLogFolder Find(ILogger fileLogger)
    {
        string shared = Logging.LogDirectory;
        try
        {
            string user = Logging.UserLogDirectory;
            fileLogger.Debug("UefiAccess.App process {ProcessId} started; finding the folder of its log", Environment.ProcessId);
            if (new PrivilegeManager(fileLogger).IsElevated)
            {
                string? refusal = Logging.PrepareDataFolderFor(shared);
                if (refusal is not null)
                {
                    return new RunLogFolder(user, true, refusal);
                }
            }

            return CanWriteTodaysLog(shared) ? new RunLogFolder(shared, false, null) : new RunLogFolder(user, true, null);
        }
        catch (Exception)
        {
            return new RunLogFolder(shared, false, null);
        }
    }

    /// <summary>
    /// Writes the note that the log is not in the shared folder, when it is not, as a warning
    /// through <paramref name="panelOnly"/> (a logger over the log panel alone: the per-user log
    /// already starts with the same note, so the file does not get it twice).
    /// </summary>
    /// <param name="panelOnly">A logger that writes into the log panel only.</param>
    public void WriteNote(ILogger panelOnly)
    {
        if (!Moved)
        {
            return;
        }

        if (Refusal is not null)
        {
            panelOnly.Warning(MovedTemplate, Directory, Logging.LogDirectory, Refusal);
        }
        else
        {
            panelOnly.Warning(UnwritableTemplate, Logging.LogDirectory, Directory);
        }
    }

    /// <summary>True when today's file of the daily log is in the folder and this account can open it for writing; never creates or writes anything.</summary>
    private static bool CanWriteTodaysLog(string folder)
    {
        string path = Path.Combine(folder, DailyFilePrefix + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
