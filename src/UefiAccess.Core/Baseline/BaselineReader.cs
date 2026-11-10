// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Privileges;
using UefiAccess.Core.Variables;

namespace UefiAccess.Core.Baseline;

/// <summary>Outcome of reading a baseline folder with <see cref="BaselineReader"/>.</summary>
/// <param name="Directory">The full path of the folder that was read (as resolved by the reader).</param>
/// <param name="Index">The parsed index.json, or null when it was missing or could not be parsed (<paramref name="Error"/> says why).</param>
/// <param name="Variables">Every variable whose raw file existed and matched the index (size and SHA-256), in index order; empty on failure.</param>
/// <param name="FirmwareType">The firmware type recorded in the index; <see cref="Platform.FirmwareType.Unknown"/> when absent or not recognised.</param>
/// <param name="Privilege">The privilege state recorded in the index, or null when absent or not recognised.</param>
/// <param name="Timestamp">The export time parsed from the index (local timestamp first, UTC second), or null when neither parses.</param>
/// <param name="Warnings">One line per entry that was skipped (missing file, size or hash mismatch, size above <see cref="BaselineReader.MaximumEntrySize"/>, invalid GUID, symbolic link or junction) and per inconsistency in the index; empty when everything matched.</param>
/// <param name="Error">Null on success; otherwise one sentence saying why nothing could be read (folder missing, index.json missing, unparsable, without a Variables list or a link, index.json above <see cref="BaselineReader.MaximumIndexSize"/>, more entries than <see cref="BaselineReader.MaximumEntryCount"/>, entries adding up to more than <see cref="BaselineReader.MaximumTotalSize"/>, cancelled).</param>
public sealed record BaselineReadResult(
    string Directory,
    BaselineIndex? Index,
    IReadOnlyList<FirmwareVariable> Variables,
    FirmwareType FirmwareType,
    PrivilegeState? Privilege,
    DateTimeOffset? Timestamp,
    IReadOnlyList<string> Warnings,
    string? Error)
{
    /// <summary>True when index.json was read and parsed; individual entries may still have been skipped (see <see cref="Warnings"/>).</summary>
    public bool Succeeded => Error is null && Index is not null;

    /// <summary>The identity recorded in the index, or null when the index is absent or carries none.</summary>
    public MachineIdentityResult? Identity => Index?.Identity;
}

/// <summary>
/// Reads a baseline folder written by <see cref="BaselineExporter"/> back into
/// <see cref="FirmwareVariable"/> records: parses index.json, loads the raw file each entry names
/// (relative to the folder), and accepts an entry only when the file exists, its length equals the
/// entry's <see cref="BaselineEntry.Size"/> and its SHA-256 equals <see cref="BaselineEntry.Sha256"/>.
/// Every rejected entry produces one warning and reading continues; a missing or unparsable
/// index.json, or one without a Variables list, produces an error and no variables. decoded.json,
/// platform.json and log.txt are never required and never read. Nothing here writes to the file
/// system, and <see cref="Read"/> never throws.
/// </summary>
/// <remarks>
/// A baseline folder is untrusted input that the elevated application opens, so the reader also
/// refuses what a real export never contains: an entry larger than <see cref="MaximumEntrySize"/>,
/// and an index.json, vars sub-folder or .bin file that is a symbolic link or junction (which
/// would let a path that looks inside the folder read a file elsewhere). The folder the user
/// chose may itself be reached through a link; only what lies inside it is checked. Three limits
/// bound what one load reads and keeps in memory, whatever the index says (entries that all name
/// the same file included), and each refuses the whole folder before a single variable file is
/// opened: index.json may be at most <see cref="MaximumIndexSize"/> bytes, list at most
/// <see cref="MaximumEntryCount"/> entries, and the sizes of its entries may add up to at most
/// <see cref="MaximumTotalSize"/> bytes. A load therefore reads at most 4 MiB of index text and
/// 64 MiB of variable data.
/// </remarks>
public static class BaselineReader
{
    /// <summary>Message used when reading stopped because the caller cancelled.</summary>
    public const string CancelledMessage = "Reading the baseline was cancelled.";

    /// <summary>
    /// Largest entry the reader accepts, in bytes: 1 MiB (1,048,576). Real firmware variables are
    /// far smaller: a full dbx is about 25 KB and most NVRAM stores hold 64 to 128 KB in total, so
    /// no export of a real machine comes near it, although the live store's own ceiling for one
    /// variable (<see cref="WindowsFirmwareVariableStore.MaximumReadBufferSize"/>) is 4 MiB. The
    /// cap keeps a crafted entry from making the elevated application read a large file, and
    /// bounds the hex dump the detail pane builds for one entry at about 5 million characters.
    /// What a whole load reads is bounded by <see cref="MaximumEntryCount"/> and
    /// <see cref="MaximumTotalSize"/>.
    /// </summary>
    public const int MaximumEntrySize = 1024 * 1024;

    /// <summary>
    /// Largest index.json the reader accepts, in bytes: 4 MiB (4,194,304). An export writes about
    /// 425 bytes of index per variable (32,295 bytes for the 76 variables of the OptiPlex 7020),
    /// so the limit holds some 9,800 entries, more than <see cref="MaximumEntryCount"/>. A larger
    /// file is refused before it is read.
    /// </summary>
    public const int MaximumIndexSize = 4 * 1024 * 1024;

    /// <summary>
    /// Most entries an index may list: 4,096. Every variable costs a header and its name in a
    /// firmware store of 64 KB to a few MB, so real machines hold far fewer (76 on the OptiPlex
    /// 7020, a few hundred on the largest servers). An index that lists more is refused whole.
    /// </summary>
    public const int MaximumEntryCount = 4096;

    /// <summary>
    /// Most bytes of variable data one load may read, the sizes the index gives its entries added
    /// up: 64 MiB (67,108,864), some 500 times the 64 to 128 KB most firmware stores hold in total.
    /// Only entries within <see cref="MaximumEntrySize"/> count, since the others are skipped
    /// unread. An index whose entries add up to more is refused whole, so many entries that each
    /// name a file just under the entry limit cannot make the elevated application hold gigabytes.
    /// </summary>
    public const int MaximumTotalSize = 64 * 1024 * 1024;

    /// <summary>The options every JSON document of a baseline or a snapshot folder is read with: case-insensitive names, trailing commas and comments allowed, enums as strings.</summary>
    internal static readonly JsonSerializerOptions ReadOptions = CreateReadOptions();

    /// <summary>
    /// Reads the baseline folder <paramref name="folder"/>. Never throws: every problem is reported
    /// through <see cref="BaselineReadResult.Error"/> (nothing readable) or
    /// <see cref="BaselineReadResult.Warnings"/> (an entry was skipped or the index is inconsistent).
    /// Cancellation is reported as an error carrying <see cref="CancelledMessage"/>.
    /// </summary>
    /// <param name="folder">The folder that contains index.json and the vars sub-folder.</param>
    /// <param name="cancellationToken">Checked before each raw file is read.</param>
    /// <returns>The read result.</returns>
    public static BaselineReadResult Read(string folder, CancellationToken cancellationToken = default)
    {
        string directory = folder ?? string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                return Failure(directory, "No baseline folder was given.");
            }

            directory = Path.GetFullPath(folder);
            if (!Directory.Exists(directory))
            {
                return Failure(directory, $"The baseline folder '{directory}' does not exist.");
            }

            string indexPath = Path.Combine(directory, BaselineExporter.IndexFileName);
            if (!File.Exists(indexPath))
            {
                return Failure(directory, $"The folder '{directory}' has no {BaselineExporter.IndexFileName}; choose the run folder the baseline command created (it contains {BaselineExporter.IndexFileName} and a {BaselineExporter.VariablesDirectoryName} sub-folder).");
            }

            if (IsLink(indexPath))
            {
                return Failure(directory, $"{BaselineExporter.IndexFileName} in '{directory}' is a symbolic link or junction, which a baseline folder must not contain; open the original folder instead.");
            }

            string json;
            try
            {
                string? tooLarge = ReadIndexText(indexPath, directory, out json);
                if (tooLarge is not null)
                {
                    return Failure(directory, tooLarge);
                }
            }
            catch (Exception ex)
            {
                return Failure(directory, $"{BaselineExporter.IndexFileName} in '{directory}' could not be read: {ex.Message}");
            }

            if (!TryParseIndex(json, out BaselineIndex? index, out string? parseError))
            {
                return Failure(directory, $"{BaselineExporter.IndexFileName} in '{directory}' is not a baseline index: {parseError}");
            }

            // TryParseIndex rejects an index without a Variables list, so this is never null here.
            IReadOnlyList<BaselineEntry> entries = index.Variables ?? Array.Empty<BaselineEntry>();
            string? overLimit = CheckLoadLimits(entries, directory);
            if (overLimit is not null)
            {
                return Failure(directory, overLimit);
            }

            List<string> warnings = new();
            FirmwareType firmwareType = ParseFirmwareType(index.FirmwareType, warnings);
            PrivilegeState? privilege = ParsePrivilege(index.PrivilegeState, warnings);
            DateTimeOffset? timestamp = ParseTimestamp(index.TimestampLocal) ?? ParseTimestamp(index.TimestampUtc);

            if (index.VariableCount != entries.Count)
            {
                warnings.Add($"{BaselineExporter.IndexFileName} says VariableCount {index.VariableCount.ToString(CultureInfo.InvariantCulture)} but lists {entries.Count.ToString(CultureInfo.InvariantCulture)} entries; the entries are used.");
            }

            List<FirmwareVariable> variables = new(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return Failure(directory, CancelledMessage);
                }

                FirmwareVariable? variable = LoadEntry(directory, entries[i], i, warnings);
                if (variable is not null)
                {
                    variables.Add(variable);
                }
            }

            return new BaselineReadResult(directory, index, variables, firmwareType, privilege, timestamp, warnings, null);
        }
        catch (Exception ex)
        {
            return Failure(directory, "Reading the baseline failed: " + ex.Message);
        }
    }

    /// <summary>
    /// Parses the text of an index.json document. Property names are matched case-insensitively.
    /// The Variables list is required: index.json has no format marker, and the list is what
    /// tells a baseline index apart from any other JSON object (another tool's index.json, "{}").
    /// Other missing properties become null or zero and are reported by <see cref="Read"/> as
    /// warnings. Never throws.
    /// </summary>
    /// <param name="json">The document text.</param>
    /// <param name="index">Receives the index on success; null on failure.</param>
    /// <param name="error">Receives one sentence on failure; null on success.</param>
    /// <returns>True when the text is a JSON object with a Variables list that deserialises as a <see cref="BaselineIndex"/>.</returns>
    public static bool TryParseIndex(string json, [NotNullWhen(true)] out BaselineIndex? index, [NotNullWhen(false)] out string? error)
    {
        index = null;
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "the file is empty.";
            return false;
        }

        try
        {
            index = JsonSerializer.Deserialize<BaselineIndex>(json, ReadOptions);
        }
        catch (JsonException ex)
        {
            error = "the JSON could not be parsed (" + ex.Message + ").";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        if (index is null)
        {
            error = "the document is the JSON literal null.";
            return false;
        }

        if (index.Variables is null)
        {
            index = null;
            error = "it has no Variables list.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Resolves an entry's relative file path against the baseline folder. The path must be
    /// relative, use "/" or the platform separator, and stay inside the folder; anything else
    /// (an empty path, an absolute path, or a path with ".." that escapes the folder) is rejected.
    /// Never throws.
    /// </summary>
    /// <param name="directory">The full path of the baseline folder.</param>
    /// <param name="relativeFile">The entry's <see cref="BaselineEntry.File"/>.</param>
    /// <param name="fullPath">Receives the full path on success.</param>
    /// <returns>True when the path is acceptable.</returns>
    public static bool TryResolveFile(string directory, string? relativeFile, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(relativeFile))
        {
            return false;
        }

        string normalized = relativeFile.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized))
        {
            return false;
        }

        string root;
        string candidate;
        try
        {
            root = Path.GetFullPath(directory);
            candidate = Path.GetFullPath(Path.Combine(root, normalized));
        }
        catch (Exception)
        {
            return false;
        }

        string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(rootWithSeparator, StringComparison.Ordinal) || candidate.Length <= rootWithSeparator.Length)
        {
            return false;
        }

        fullPath = candidate;
        return true;
    }

    /// <summary>
    /// Checks the limits a whole load must stay within, before any variable file is opened: at
    /// most <see cref="MaximumEntryCount"/> entries, and the sizes of the entries that would be
    /// read (null entries and sizes outside 0 to <see cref="MaximumEntrySize"/> are skipped unread)
    /// adding up to at most <see cref="MaximumTotalSize"/> bytes. Never throws.
    /// </summary>
    /// <param name="entries">The index entries.</param>
    /// <param name="directory">The full path of the baseline folder, for the message.</param>
    /// <returns>Null when the load may go ahead; otherwise the sentence that says why nothing is read.</returns>
    internal static string? CheckLoadLimits(IReadOnlyList<BaselineEntry?> entries, string directory)
    {
        if (entries.Count > MaximumEntryCount)
        {
            return $"{BaselineExporter.IndexFileName} in '{directory}' lists {entries.Count.ToString(CultureInfo.InvariantCulture)} entries, more than the {MaximumEntryCount.ToString(CultureInfo.InvariantCulture)} a baseline can hold, so nothing was read.";
        }

        long total = 0;
        foreach (BaselineEntry? entry in entries)
        {
            if (entry is not null && entry.Size >= 0 && entry.Size <= MaximumEntrySize)
            {
                total += entry.Size;
            }
        }

        if (total > MaximumTotalSize)
        {
            return $"The entries in {BaselineExporter.IndexFileName} in '{directory}' add up to {total.ToString(CultureInfo.InvariantCulture)} bytes, more than the {MaximumTotalSize.ToString(CultureInfo.InvariantCulture)}-byte limit for one baseline, so nothing was read.";
        }

        return null;
    }

    /// <summary>
    /// Reads index.json as text (UTF-8 unless a byte order mark says otherwise, as
    /// <see cref="File.ReadAllText(string)"/> does), reading at most one byte more than
    /// <see cref="MaximumIndexSize"/> however large the file is or grows. Throws what reading
    /// the file throws.
    /// </summary>
    /// <param name="indexPath">The full path of index.json.</param>
    /// <param name="directory">The full path of the baseline folder, for the message.</param>
    /// <param name="json">Receives the text; empty when the file is too large.</param>
    /// <returns>Null when the text was read; otherwise the sentence that refuses a file above the limit.</returns>
    private static string? ReadIndexText(string indexPath, string directory, out string json)
    {
        json = string.Empty;
        long length = new FileInfo(indexPath).Length;
        byte[] bytes = length > MaximumIndexSize ? Array.Empty<byte>() : ReadAtMost(indexPath, (int)length + 1);
        if (length > MaximumIndexSize || bytes.Length > MaximumIndexSize)
        {
            long size = Math.Max(length, bytes.Length);
            return $"{BaselineExporter.IndexFileName} in '{directory}' is {size.ToString(CultureInfo.InvariantCulture)} bytes, more than the {MaximumIndexSize.ToString(CultureInfo.InvariantCulture)}-byte limit for a baseline index, so nothing was read.";
        }

        using MemoryStream stream = new(bytes, writable: false);
        using StreamReader reader = new(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        json = reader.ReadToEnd();
        return null;
    }

    private static FirmwareVariable? LoadEntry(string directory, BaselineEntry? entry, int position, List<string> warnings)
    {
        string label = "Entry " + (position + 1).ToString(CultureInfo.InvariantCulture);
        if (entry is null)
        {
            warnings.Add($"{label}: the entry is null; it was skipped.");
            return null;
        }

        if (!string.IsNullOrEmpty(entry.Key))
        {
            label = entry.Key;
        }

        if (string.IsNullOrEmpty(entry.Name))
        {
            warnings.Add($"{label}: the name is empty; the entry was skipped.");
            return null;
        }

        if (!Guid.TryParseExact(entry.Guid, "D", out Guid guid) && !Guid.TryParse(entry.Guid, out guid))
        {
            warnings.Add($"{label}: the GUID '{entry.Guid}' is not valid; the entry was skipped.");
            return null;
        }

        string computedKey = FirmwareVariable.MakeKey(guid, entry.Name);
        if (!string.Equals(entry.Key, computedKey, StringComparison.Ordinal))
        {
            warnings.Add($"{label}: the index key does not match the GUID and name ('{computedKey}'); the computed key is used.");
        }

        if (entry.Size < 0)
        {
            warnings.Add($"{label}: the index size {entry.Size.ToString(CultureInfo.InvariantCulture)} is negative; the entry was skipped.");
            return null;
        }

        if (entry.Size > MaximumEntrySize)
        {
            warnings.Add($"{label}: the index size {entry.Size.ToString(CultureInfo.InvariantCulture)} bytes is above the {MaximumEntrySize.ToString(CultureInfo.InvariantCulture)}-byte limit for one firmware variable; the entry was skipped.");
            return null;
        }

        if (!TryResolveFile(directory, entry.File, out string fullPath))
        {
            warnings.Add($"{label}: the file path '{entry.File}' is missing or leaves the baseline folder; the entry was skipped.");
            return null;
        }

        if (TryFindLink(directory, fullPath, out string? link))
        {
            warnings.Add($"{label}: '{link}' is a symbolic link or junction, which a baseline folder must not contain; the entry was skipped.");
            return null;
        }

        FileInfo info = new(fullPath);
        if (!info.Exists)
        {
            warnings.Add($"{label}: the file '{entry.File}' is missing; the variable was skipped.");
            return null;
        }

        if (info.Length != entry.Size)
        {
            warnings.Add($"{label}: the file '{entry.File}' is {info.Length.ToString(CultureInfo.InvariantCulture)} bytes but the index says {entry.Size.ToString(CultureInfo.InvariantCulture)}; the variable was skipped.");
            return null;
        }

        byte[] data;
        try
        {
            data = ReadAtMost(fullPath, entry.Size + 1);
        }
        catch (Exception ex)
        {
            warnings.Add($"{label}: the file '{entry.File}' could not be read: {ex.Message}; the variable was skipped.");
            return null;
        }

        if (data.Length != entry.Size)
        {
            warnings.Add($"{label}: the file '{entry.File}' is {data.Length.ToString(CultureInfo.InvariantCulture)} bytes but the index says {entry.Size.ToString(CultureInfo.InvariantCulture)}; the variable was skipped.");
            return null;
        }

        string actualHash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        if (!string.Equals(actualHash, entry.Sha256?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"{label}: the SHA-256 of '{entry.File}' does not match the index; the variable was skipped.");
            return null;
        }

        return new FirmwareVariable(entry.Name, guid, (VariableAttributes)entry.Attributes, data);
    }

    /// <summary>
    /// Finds a symbolic link or junction on the way from <paramref name="directory"/> to
    /// <paramref name="fullPath"/>: the file itself and every folder between it and the baseline
    /// folder (the baseline folder itself is not checked). Other reparse points, such as cloud
    /// placeholders, are not links and pass. Never throws.
    /// </summary>
    /// <param name="directory">The full path of the baseline folder.</param>
    /// <param name="fullPath">A path inside it, as returned by <see cref="TryResolveFile"/>.</param>
    /// <param name="link">Receives the offending path relative to the folder, with "/" separators.</param>
    /// <returns>True when a link was found.</returns>
    internal static bool TryFindLink(string directory, string fullPath, [NotNullWhen(true)] out string? link)
    {
        link = null;
        try
        {
            string root = Path.GetFullPath(directory);
            string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
            string? current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(fullPath));
            while (current is not null && current.StartsWith(rootWithSeparator, StringComparison.Ordinal))
            {
                if (IsLink(current))
                {
                    link = Path.GetRelativePath(root, current).Replace(Path.DirectorySeparatorChar, '/');
                    return true;
                }

                current = Path.GetDirectoryName(current);
            }

            return false;
        }
        catch (Exception)
        {
            link = fullPath;
            return true;
        }
    }

    /// <summary>
    /// True when <paramref name="path"/> is a symbolic link or junction. A path that does not exist
    /// is not a link (the caller reports it as missing). A reparse point whose target cannot be
    /// read counts as a link, so a doubtful entry is refused rather than followed.
    /// </summary>
    private static bool IsLink(string path)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (Exception)
        {
            return false;
        }

        if ((attributes & FileAttributes.ReparsePoint) == 0)
        {
            return false;
        }

        try
        {
            FileSystemInfo info = (attributes & FileAttributes.Directory) != 0 ? new DirectoryInfo(path) : new FileInfo(path);
            return info.LinkTarget is not null;
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>Reads at most <paramref name="limit"/> bytes from the start of a file, so a file that grew after its length was checked cannot force a larger read.</summary>
    internal static byte[] ReadAtMost(string path, int limit)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] buffer = new byte[limit];
        int total = 0;
        int read;
        while (total < limit && (read = stream.Read(buffer, total, limit - total)) > 0)
        {
            total += read;
        }

        return total == limit ? buffer : buffer.AsSpan(0, total).ToArray();
    }

    private static FirmwareType ParseFirmwareType(string? text, List<string> warnings)
    {
        if (!string.IsNullOrWhiteSpace(text) && Enum.TryParse(text, ignoreCase: true, out FirmwareType parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        warnings.Add($"{BaselineExporter.IndexFileName} records the firmware type '{text}', which is not recognised; it is shown as {FirmwareType.Unknown}.");
        return FirmwareType.Unknown;
    }

    private static PrivilegeState? ParsePrivilege(string? text, List<string> warnings)
    {
        if (!string.IsNullOrWhiteSpace(text) && Enum.TryParse(text, ignoreCase: true, out PrivilegeState parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        warnings.Add($"{BaselineExporter.IndexFileName} records the privilege state '{text}', which is not recognised.");
        return null;
    }

    private static DateTimeOffset? ParseTimestamp(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed) ? parsed : null;
    }

    private static BaselineReadResult Failure(string directory, string error) =>
        new(directory, null, Array.Empty<FirmwareVariable>(), FirmwareType.Unknown, null, null, Array.Empty<string>(), error);

    private static JsonSerializerOptions CreateReadOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
