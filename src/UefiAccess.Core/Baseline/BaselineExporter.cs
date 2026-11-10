// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Core.Variables;

namespace UefiAccess.Core.Baseline;

/// <summary>
/// Writes a baseline folder under RootDirectory/RunId:
/// index.json, vars/&lt;guid&gt;_&lt;name&gt;.bin (one raw file per variable), decoded.json,
/// platform.json, dell-settings.json when the request carries the Dell BIOS settings
///, and log.txt. The exporter reads nothing itself: the Dell settings
/// come with the request, read by the caller. index.json is deterministic: entries sorted by key
/// (ordinal), fixed property order, SHA-256 per variable and no per-entry timestamps, so exporting
/// the same request twice yields byte-identical index.json and decoded.json. log.txt is written
/// last and holds the request's log text followed by the exporter's own outcome (the files
/// written before it and every error), so the file reflects what the export produced.
/// <see cref="Export"/> never throws; every failure is reported in
/// <see cref="BaselineExportResult.Errors"/> and whatever was written is returned.
/// </summary>
public sealed class BaselineExporter
{
    /// <summary>Name of the index document inside the export folder.</summary>
    public const string IndexFileName = "index.json";

    /// <summary>Name of the decoded-variables document inside the export folder.</summary>
    public const string DecodedFileName = "decoded.json";

    /// <summary>Name of the platform document inside the export folder.</summary>
    public const string PlatformFileName = "platform.json";

    /// <summary>Name of the Dell BIOS settings document inside the export folder, written only when the request carries one (<see cref="BaselineRequest.DellSettings"/>).</summary>
    public const string DellSettingsFileName = "dell-settings.json";

    /// <summary>Name of the run log inside the export folder.</summary>
    public const string LogFileName = "log.txt";

    /// <summary>Name of the sub-folder that holds one raw file per variable.</summary>
    public const string VariablesDirectoryName = "vars";

    /// <summary>Extension of the raw variable files.</summary>
    public const string VariableFileExtension = ".bin";

    /// <summary>Maximum length of a sanitised file name stem (a collision suffix and the extension may follow it).</summary>
    public const int MaxFileNameLength = 120;

    /// <summary>First line of the outcome block the exporter appends to log.txt after the request's log text.</summary>
    public const string LogOutcomeHeader = "Baseline export outcome";

    private const string UtcTimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";
    private const string LocalTimestampFormat = "yyyy-MM-dd'T'HH:mm:sszzz";
    private const string RunIdFormat = "yyyyMMdd-HHmmss";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Func<string, string?> _prepareFolder;

    /// <summary>
    /// Creates an exporter. Under the data root an elevated export is prepared by
    /// <see cref="Logging.PrepareDataFolderFor(string)"/>, which refuses a folder standard users
    /// could redirect.
    /// </summary>
    public BaselineExporter()
        : this(Logging.PrepareDataFolderFor)
    {
    }

    /// <summary>Creates an exporter with a replacement for <see cref="Logging.PrepareDataFolderFor(string)"/>, for tests.</summary>
    /// <param name="prepareFolder">Called with the run folder before anything is created; returns null to go ahead or the refusal.</param>
    internal BaselineExporter(Func<string, string?> prepareFolder)
    {
        ArgumentNullException.ThrowIfNull(prepareFolder);
        _prepareFolder = prepareFolder;
    }

    /// <summary>
    /// Shared serializer settings for every JSON document the exporter writes: indented, property
    /// names as declared (no naming policy), enums as strings, nothing ignored. One shared instance
    /// keeps index.json and decoded.json byte-for-byte reproducible.
    /// </summary>
    internal static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    /// <summary>
    /// Default parent directory for exports: ProgramData\UefiAccess\baseline on Windows,
    /// LocalApplicationData/UefiAccess/baseline elsewhere.
    /// </summary>
    public static string DefaultRootDirectory => Path.Combine(Logging.DataDirectory, "baseline");

    /// <summary>
    /// Formats the conventional run folder name "yyyyMMdd-HHmmss" from a timestamp, using the
    /// timestamp's own offset (local time when the caller used <see cref="DateTimeOffset.Now"/>).
    /// </summary>
    /// <param name="timestamp">The export time.</param>
    /// <returns>The folder name, for example "20260910-143005".</returns>
    public static string DefaultRunId(DateTimeOffset timestamp) => timestamp.ToString(RunIdFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// The file name of a variable's raw bytes, without its extension: the GUID in lowercase "D"
    /// format, "_" and the name, made safe by <see cref="SanitizeFileName"/>. The export names
    /// vars\ files by it (adding a suffix when two collide) and a snapshot names its after\ file.
    /// </summary>
    /// <param name="vendorGuid">The vendor GUID.</param>
    /// <param name="name">The variable name.</param>
    /// <returns>The file name stem.</returns>
    internal static string VariableFileStem(Guid vendorGuid, string name)
    {
        return SanitizeFileName(vendorGuid.ToString("D") + "_" + name);
    }

    /// <summary>
    /// Makes a name safe for the file system: every character outside [A-Za-z0-9_.-] becomes "_",
    /// the result is trimmed to <see cref="MaxFileNameLength"/> characters, and an empty or null
    /// name becomes "_".
    /// </summary>
    /// <param name="name">The name to sanitise.</param>
    /// <returns>The sanitised name.</returns>
    public static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "_";
        }

        int length = Math.Min(name.Length, MaxFileNameLength);
        StringBuilder builder = new(length);
        for (int i = 0; i < length; i++)
        {
            char c = name[i];
            bool allowed = c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '.' or '-';
            builder.Append(allowed ? c : '_');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Builds the index document for a request without touching the file system: entries sorted
    /// by key (ordinal), one per variable, with the same file names <see cref="Export"/> would use.
    /// Null variables are skipped; a null data array counts as zero-length.
    /// </summary>
    /// <param name="request">The request to index.</param>
    /// <returns>The index document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    public static BaselineIndex BuildIndex(BaselineRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return MakeIndex(request, Prepare(request.Variables));
    }

    /// <summary>
    /// Writes the export described by <paramref name="request"/>. A null or blank
    /// <see cref="BaselineRequest.RootDirectory"/> selects <see cref="DefaultRootDirectory"/>; a null
    /// or blank <see cref="BaselineRequest.RunId"/> selects <see cref="DefaultRunId"/> of the request
    /// timestamp. Never throws.
    /// </summary>
    /// <param name="request">What to export and where.</param>
    /// <returns>The export result; <see cref="BaselineExportResult.Errors"/> lists every failure.</returns>
    public BaselineExportResult Export(BaselineRequest request)
    {
        List<string> errors = new();
        List<string> written = new();
        string directory = string.Empty;
        int variableCount = 0;

        try
        {
            if (request is null)
            {
                errors.Add("Export request is null.");
                return new BaselineExportResult(directory, variableCount, written, errors);
            }

            string root = string.IsNullOrWhiteSpace(request.RootDirectory) ? DefaultRootDirectory : request.RootDirectory;
            string runId = string.IsNullOrWhiteSpace(request.RunId) ? DefaultRunId(request.Timestamp) : request.RunId;
            directory = Path.GetFullPath(Path.Combine(root, runId));
            string variablesDirectory = Path.Combine(directory, VariablesDirectoryName);

            try
            {
                // Inside the data root, an elevated export writes only through folders standard
                // users cannot redirect, and creates the missing ones, the run folder included,
                // with the protected access list that vars then inherits.
                string? refusal = _prepareFolder(directory);
                if (refusal is not null)
                {
                    errors.Add($"Cannot create export directory '{variablesDirectory}': {refusal}");
                    return new BaselineExportResult(directory, variableCount, written, errors);
                }

                Directory.CreateDirectory(variablesDirectory);
            }
            catch (Exception ex)
            {
                errors.Add($"Cannot create export directory '{variablesDirectory}': {ex.Message}");
                return new BaselineExportResult(directory, variableCount, written, errors);
            }

            List<PreparedEntry> prepared = Prepare(request.Variables);
            variableCount = prepared.Count;
            foreach (PreparedEntry item in prepared)
            {
                WriteBytes(directory, item.Entry.File, item.Variable.Data ?? Array.Empty<byte>(), written, errors);
            }

            BaselineIndex index = MakeIndex(request, prepared);
            if (TrySerialize(index, IndexFileName, errors, out string indexJson))
            {
                WriteText(directory, IndexFileName, indexJson, written, errors);
            }

            if (TrySerialize(request.Decoded, DecodedFileName, errors, out string decodedJson))
            {
                WriteText(directory, DecodedFileName, decodedJson, written, errors);
            }

            if (TrySerialize(request.Platform, PlatformFileName, errors, out string platformJson))
            {
                WriteText(directory, PlatformFileName, platformJson, written, errors);
            }

            // A request without Dell settings (the snapshot store's export, every baseline saved
            // before Alpha 2) writes no Dell file, so its folder stays as it always was.
            if (request.DellSettings is not null && TrySerialize(request.DellSettings, DellSettingsFileName, errors, out string dellJson))
            {
                WriteText(directory, DellSettingsFileName, dellJson, written, errors);
            }

            // log.txt goes last so it can record what the export produced: the files written so
            // far and every error. Its own write outcome can only reach the result record.
            WriteText(directory, LogFileName, ComposeLogText(request.LogText, variableCount, written, errors), written, errors);
        }
        catch (Exception ex)
        {
            errors.Add("Export failed: " + ex.Message);
        }

        return new BaselineExportResult(directory, variableCount, written, errors);
    }

    /// <summary>
    /// Builds the text of log.txt: the request's log text (given a trailing line break when it
    /// lacks one), then <see cref="LogOutcomeHeader"/>, the number of variables indexed, the files
    /// written before log.txt (one per line, relative paths) and every error so far (one per line).
    /// The outcome carries no timestamps or absolute paths, so two exports of the same request
    /// produce identical log.txt files.
    /// </summary>
    /// <param name="logText">The run log supplied by the caller; null or empty writes only the outcome.</param>
    /// <param name="variableCount">Variables indexed.</param>
    /// <param name="written">Relative paths written before log.txt.</param>
    /// <param name="errors">Errors recorded before log.txt.</param>
    /// <returns>The complete log.txt text.</returns>
    internal static string ComposeLogText(string? logText, int variableCount, IReadOnlyList<string> written, IReadOnlyList<string> errors)
    {
        StringBuilder builder = new(logText ?? string.Empty);
        if (builder.Length > 0 && builder[builder.Length - 1] != '\n')
        {
            builder.Append(Environment.NewLine);
        }

        builder.Append(LogOutcomeHeader).Append(Environment.NewLine);
        builder.Append("Variables indexed: ").Append(variableCount.ToString(CultureInfo.InvariantCulture)).Append(Environment.NewLine);
        builder.Append("Files written before log.txt: ").Append(written.Count.ToString(CultureInfo.InvariantCulture)).Append(Environment.NewLine);
        foreach (string file in written)
        {
            builder.Append("  ").Append(file).Append(Environment.NewLine);
        }

        builder.Append("Errors: ").Append(errors.Count.ToString(CultureInfo.InvariantCulture)).Append(Environment.NewLine);
        foreach (string error in errors)
        {
            builder.Append("  ").Append(error).Append(Environment.NewLine);
        }

        return builder.ToString();
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            IncludeFields = false,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    /// <summary>
    /// Sorts the variables by key and assigns each a unique file name. File names are compared
    /// case-insensitively so the layout is identical on Windows and on case-sensitive file systems;
    /// a collision gets the suffix "_2", "_3", ... in key order.
    /// </summary>
    private static List<PreparedEntry> Prepare(IEnumerable<FirmwareVariable>? variables)
    {
        FirmwareVariable[] sorted = (variables ?? Enumerable.Empty<FirmwareVariable>())
            .Where(v => v is not null && v.Name is not null)
            .OrderBy(v => v.Key, StringComparer.Ordinal)
            .ToArray();

        List<PreparedEntry> prepared = new(sorted.Length);
        HashSet<string> usedStems = new(StringComparer.OrdinalIgnoreCase);
        foreach (FirmwareVariable variable in sorted)
        {
            string guid = variable.VendorGuid.ToString("D");
            string stem = VariableFileStem(variable.VendorGuid, variable.Name);
            string candidate = stem;
            int suffix = 2;
            while (!usedStems.Add(candidate))
            {
                candidate = stem + "_" + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }

            byte[] data = variable.Data ?? Array.Empty<byte>();
            BaselineEntry entry = new(
                variable.Key,
                variable.Name,
                guid,
                (uint)variable.Attributes,
                ShortFlags(variable.Attributes),
                data.Length,
                Sha256Hex(data),
                VariablesDirectoryName + "/" + candidate + VariableFileExtension);
            prepared.Add(new PreparedEntry(variable, entry));
        }

        return prepared;
    }

    private static BaselineIndex MakeIndex(BaselineRequest request, List<PreparedEntry> prepared)
    {
        BaselineEntry[] entries = new BaselineEntry[prepared.Count];
        for (int i = 0; i < entries.Length; i++)
        {
            entries[i] = prepared[i].Entry;
        }

        return new BaselineIndex(
            request.ToolVersion ?? string.Empty,
            request.Timestamp.ToUniversalTime().ToString(UtcTimestampFormat, CultureInfo.InvariantCulture),
            request.Timestamp.ToString(LocalTimestampFormat, CultureInfo.InvariantCulture),
            request.FirmwareType.ToString(),
            request.Privilege.ToString(),
            request.Identity,
            entries.Length,
            entries);
    }

    private static string ShortFlags(VariableAttributes attributes)
    {
        try
        {
            return VariableAttributeFlags.ToShortFlags(attributes) ?? string.Empty;
        }
        catch (Exception)
        {
            return "0x" + ((uint)attributes).ToString("X8", CultureInfo.InvariantCulture);
        }
    }

    private static string Sha256Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static bool TrySerialize<T>(T value, string fileName, List<string> errors, out string json)
    {
        try
        {
            json = JsonSerializer.Serialize(value, JsonOptions);
            return true;
        }
        catch (Exception ex)
        {
            errors.Add($"Cannot serialise {fileName}: {ex.Message}");
            json = string.Empty;
            return false;
        }
    }

    private static void WriteBytes(string directory, string relativePath, byte[] data, List<string> written, List<string> errors)
    {
        try
        {
            File.WriteAllBytes(ToFullPath(directory, relativePath), data);
            written.Add(relativePath);
        }
        catch (Exception ex)
        {
            errors.Add($"Cannot write '{relativePath}': {ex.Message}");
        }
    }

    private static void WriteText(string directory, string relativePath, string text, List<string> written, List<string> errors)
    {
        try
        {
            File.WriteAllText(ToFullPath(directory, relativePath), text, Utf8NoBom);
            written.Add(relativePath);
        }
        catch (Exception ex)
        {
            errors.Add($"Cannot write '{relativePath}': {ex.Message}");
        }
    }

    private static string ToFullPath(string directory, string relativePath) =>
        Path.Combine(directory, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private readonly record struct PreparedEntry(FirmwareVariable Variable, BaselineEntry Entry);
}
