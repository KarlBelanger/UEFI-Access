// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.IO;
using System.Security;
using UefiAccess.Core.Diagnostics;

namespace UefiAccess.UiaTrace.Recording;

/// <summary>
/// The folder checks <see cref="TraceOutput"/> makes, injectable so the tests use temporary
/// folders. <see cref="Default"/> is the real one: the UefiAccess log folders and Logging's checks.
/// </summary>
/// <param name="SharedLogDirectory">The shared log folder (normally C:\ProgramData\UefiAccess\logs).</param>
/// <param name="UserLogDirectory">The per-user log folder, used when the shared one is refused or cannot be written.</param>
/// <param name="PrepareRunFileFolder">Checks (and creates) one of the two log folders before a run file is created in it; null to go ahead, otherwise the refusal.</param>
/// <param name="PrepareDataFolderFor">Checks (and creates) a folder under the data root before an elevated write; null to go ahead (always for a folder outside the data root), otherwise the refusal.</param>
/// <param name="DirectoryExists">Whether a path is an existing folder.</param>
internal sealed record TraceOutputFolders(
    string SharedLogDirectory,
    string UserLogDirectory,
    Func<string, string?> PrepareRunFileFolder,
    Func<string, string?> PrepareDataFolderFor,
    Func<string, bool> DirectoryExists)
{
    /// <summary>The real folders and checks: Logging's log folders, PrepareRunFileFolder and PrepareDataFolderFor.</summary>
    public static TraceOutputFolders Default => new(
        Logging.LogDirectory,
        Logging.UserLogDirectory,
        Logging.PrepareRunFileFolder,
        Logging.PrepareDataFolderFor,
        Directory.Exists);
}

/// <summary>The trace file the recorder writes to, or why there is none.</summary>
/// <param name="Writer">The open trace file, or null when none could be created.</param>
/// <param name="Refusal">Why no file was created, or null.</param>
/// <param name="Note">A sentence for the console when the file is not where it would normally be (the per-user folder), or null.</param>
internal sealed record TraceOutputResult(TraceFileWriter? Writer, string? Refusal, string? Note);

/// <summary>
/// Creates the recorder's trace file. By default it is uia-trace-&lt;yyyyMMdd-HHmmss&gt;.log in the
/// shared log folder, next to the application's trace-input file, after Logging.PrepareRunFileFolder
/// has checked the folder; when that folder is refused or cannot be written, the same name in the
/// per-user log folder. With --out, which must be a full path (an elevated prompt starts in
/// C:\Windows\System32, where a relative name would land), an existing folder gets the default name
/// inside it and any other path is the file itself; a folder under the data root is checked by
/// Logging.PrepareDataFolderFor first. Every file is created new (TraceFileWriter.CreateNew and
/// CreateInFolder): an existing file is never overwritten or followed, and the default name takes
/// "-2" to "-9" when a file of that second already exists. Nothing here throws for a folder or file
/// problem; it is returned as the refusal. Pure (Core and the base library only), so the tests
/// compile it as a linked file.
/// </summary>
internal static class TraceOutput
{
    /// <summary>The start of the default file name.</summary>
    public const string FilePrefix = "uia-trace-";

    /// <summary>The title in the file's first header line.</summary>
    public const string Title = "UefiAccess UI Automation trace";

    /// <summary>
    /// The comment line of the recorder's file when the service tag could not be read: the
    /// recorder registers no other value, so only a tag after its label is still replaced
    /// (UiaEventText.RedactServiceTagLabel).
    /// </summary>
    public const string ServiceTagUnreadComment = "The service tag could not be read; only the text after a \"Service tag:\" label is replaced in this file.";

    /// <summary>Creates the trace file.</summary>
    /// <param name="outPath">The path given with --out, or null for the default.</param>
    /// <param name="now">The local time the default name is made from.</param>
    /// <param name="folders">The folders and their checks.</param>
    /// <param name="readServiceTag">Reads this unit's service tag on the writer's thread, so the writer can replace it in every line.</param>
    /// <returns>The writer, or the refusal.</returns>
    public static TraceOutputResult Open(string? outPath, DateTime now, TraceOutputFolders folders, Func<string?> readServiceTag)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(readServiceTag);
        return string.IsNullOrWhiteSpace(outPath)
            ? OpenDefault(now, folders, readServiceTag)
            : OpenGiven(outPath.Trim(), now, folders, readServiceTag);
    }

    private static TraceOutputResult OpenDefault(DateTime now, TraceOutputFolders folders, Func<string?> readServiceTag)
    {
        string shared = folders.SharedLogDirectory;
        string user = folders.UserLogDirectory;
        string? sharedProblem = TryCreateInFolder(shared, now, folders.PrepareRunFileFolder, readServiceTag, out TraceFileWriter? writer);
        if (writer is not null)
        {
            return new TraceOutputResult(writer, null, null);
        }

        if (SameFolder(shared, user))
        {
            return new TraceOutputResult(null, "The trace file could not be created in " + shared + ": " + sharedProblem, null);
        }

        string? userProblem = TryCreateInFolder(user, now, folders.PrepareRunFileFolder, readServiceTag, out writer);
        if (writer is not null)
        {
            return new TraceOutputResult(writer, null, "The shared log folder " + shared + " was not used (" + sharedProblem + "), so the trace is in " + user + ".");
        }

        return new TraceOutputResult(
            null,
            "The trace file could be created neither in " + shared + " (" + sharedProblem + ") nor in " + user + " (" + userProblem + ").",
            null);
    }

    private static TraceOutputResult OpenGiven(string outPath, DateTime now, TraceOutputFolders folders, Func<string?> readServiceTag)
    {
        if (!Path.IsPathFullyQualified(outPath))
        {
            // Resolved against the current folder, a relative path from an elevated prompt would
            // create the file in C:\Windows\System32, as administrator.
            return new TraceOutputResult(null, outPath + " is not a full path: give one such as C:\\Temp\\uia-trace.log (an elevated prompt starts in C:\\Windows\\System32).", null);
        }

        string full;
        try
        {
            full = Path.GetFullPath(outPath);
        }
        catch (Exception ex) when (IsPathProblem(ex))
        {
            return new TraceOutputResult(null, outPath + " is not a usable path: " + ex.Message, null);
        }

        if (folders.DirectoryExists(full))
        {
            string? problem = TryCreateInFolder(full, now, folders.PrepareDataFolderFor, readServiceTag, out TraceFileWriter? inFolder);
            return inFolder is not null
                ? new TraceOutputResult(inFolder, null, null)
                : new TraceOutputResult(null, "The trace file could not be created in " + full + ": " + problem, null);
        }

        string? parent = Path.GetDirectoryName(full);
        if (string.IsNullOrEmpty(parent))
        {
            return new TraceOutputResult(null, full + " names no file.", null);
        }

        string? refusal = folders.PrepareDataFolderFor(parent);
        if (refusal is not null)
        {
            return new TraceOutputResult(null, refusal, null);
        }

        try
        {
            return new TraceOutputResult(TraceFileWriter.CreateNew(full, Title, readServiceTag, ServiceTagUnreadComment), null, null);
        }
        catch (Exception ex) when (IsPathProblem(ex))
        {
            return new TraceOutputResult(null, "The trace file " + full + " could not be created (an existing file is never overwritten): " + ex.Message, null);
        }
    }

    /// <summary>Checks a folder and creates the default-named file in it.</summary>
    /// <returns>Null when <paramref name="writer"/> is set; otherwise why not.</returns>
    private static string? TryCreateInFolder(string directory, DateTime now, Func<string, string?> prepare, Func<string?> readServiceTag, out TraceFileWriter? writer)
    {
        writer = null;
        string? refusal = prepare(directory);
        if (refusal is not null)
        {
            return refusal;
        }

        try
        {
            writer = TraceFileWriter.CreateInFolder(directory, FilePrefix, now, Title, readServiceTag, ServiceTagUnreadComment);
            return null;
        }
        catch (Exception ex) when (IsPathProblem(ex))
        {
            return ex.Message;
        }
    }

    private static bool SameFolder(string first, string second)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (IsPathProblem(ex))
        {
            return false;
        }
    }

    /// <summary>The exceptions a path or a file creation can raise for a reason the user can act on.</summary>
    private static bool IsPathProblem(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or SecurityException;
}
