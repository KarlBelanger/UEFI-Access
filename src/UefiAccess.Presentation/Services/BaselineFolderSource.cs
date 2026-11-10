// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using Serilog;
using UefiAccess.Core.Baseline;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Variables;
using UefiAccess.Presentation.Resources;

namespace UefiAccess.Presentation.Services;

/// <summary>
/// A baseline folder (index.json plus vars/*.bin, as written by the Core baseline exporter) as a
/// read-only offline variable source. <see cref="LoadAsync"/> hands the folder to
/// <see cref="BaselineReader"/> on a worker thread and maps its result: every variable whose raw
/// file exists and matches the index (size and SHA-256) is reconstructed with its attributes; an
/// entry that fails a check becomes one warning and is skipped; a missing folder, a missing
/// index.json or an unparsable one yields <see cref="SourceStatus.Failed"/> with a clear message.
/// The result's description is "Baseline (offline): &lt;folder&gt; (&lt;timestamp&gt;)" so the
/// offline view is never mistaken for the live machine; the firmware type and the machine identity
/// come from the index, the privilege state is null (it does not apply offline) and the method is
/// <see cref="EnumerationMethod.InMemory"/>. decoded.json and platform.json are neither required
/// nor read. dell-settings.json, the Dell BIOS settings the folder may carry, is read on the same worker once the variables loaded and handed on as
/// <see cref="SourceResult.DellBaseline"/>, with the folder's name; a folder without it, or with
/// one that cannot be used, loads all the same and without a warning, because the Dell state line
/// says so. Never throws.
/// </summary>
/// <remarks>
/// The index is untrusted input (it may have been edited by hand or merged from two runs), so two
/// things are checked before they reach the list, the title or the Loaded sentence: the timestamp
/// is shown only when it is a valid ISO 8601 date and time, re-formatted, never as written (an
/// unreadable one is left out with one warning); and a variable (vendor GUID and name) listed more
/// than once is shown once, from its first entry, like every lookup of the list, with one warning
/// that names the repeated keys.
/// </remarks>
public sealed class BaselineFolderSource : IVariableSource
{
    /// <summary>How many repeated keys the duplicate warning names before it says how many more there are.</summary>
    internal const int MaximumKeysNamed = 5;

    private const string LocalDisplayFormat = "yyyy-MM-dd'T'HH:mm:sszzz";
    private const string UtcDisplayFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    private static readonly string[] LocalFormats = { "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz" };
    private static readonly string[] UtcFormats = { "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'" };

    private readonly ILogger _logger;

    /// <summary>Creates the source over a folder path.</summary>
    /// <param name="folder">The baseline folder (the one that contains index.json).</param>
    /// <param name="logger">Optional logger; null disables logging.</param>
    /// <exception cref="ArgumentNullException"><paramref name="folder"/> is null.</exception>
    public BaselineFolderSource(string folder, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(folder);
        Folder = folder;
        _logger = logger ?? Serilog.Core.Logger.None;
    }

    /// <summary>The baseline folder path as given.</summary>
    public string Folder { get; }

    /// <inheritdoc />
    public string Description => Strings.BaselinePrefix + Folder;

    /// <inheritdoc />
    public bool IsOffline => true;

    /// <summary>
    /// Builds the description of a loaded baseline: <see cref="Strings.BaselinePrefix"/>, the
    /// folder, and the index timestamp in parentheses when the index carries a valid one: the
    /// local timestamp with its offset ("2026-09-10T14:30:05+02:00"), or the UTC one
    /// ("2026-09-10T12:30:05Z") when only that is valid. A timestamp is valid when it is an ISO
    /// 8601 date and time with an offset (local) or a Z (UTC), with or without fractions of a
    /// second; it is shown re-formatted to whole seconds, never as written, so a corrupted index
    /// cannot put arbitrary text into the title or the Loaded sentence.
    /// </summary>
    /// <param name="folder">The folder path as given by the user.</param>
    /// <param name="index">The parsed index, or null.</param>
    /// <returns>The description text.</returns>
    public static string DescribeLoaded(string folder, BaselineIndex? index)
    {
        string description = Strings.BaselinePrefix + (folder ?? string.Empty);
        string? timestamp = DisplayTimestamp(index);
        return timestamp is null ? description : description + " (" + timestamp + ")";
    }

    /// <summary>
    /// The timestamp <see cref="DescribeLoaded"/> shows: the valid local timestamp, else the valid
    /// UTC one, re-formatted; null when the index carries neither.
    /// </summary>
    /// <param name="index">The parsed index, or null.</param>
    /// <returns>The timestamp text, or null.</returns>
    internal static string? DisplayTimestamp(BaselineIndex? index)
    {
        if (TryParse(index?.TimestampLocal, LocalFormats, DateTimeStyles.None, out DateTimeOffset local))
        {
            return local.ToString(LocalDisplayFormat, CultureInfo.InvariantCulture);
        }

        if (TryParse(index?.TimestampUtc, UtcFormats, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset utc))
        {
            return utc.ToString(UtcDisplayFormat, CultureInfo.InvariantCulture);
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<SourceResult> LoadAsync(CancellationToken cancellationToken)
    {
        // Outcomes and warnings are Debug here: the shell records them in the log panel once
        // (the announcement as INFO, each warning as WARN, a failure as ERROR); the file keeps these.
        _logger.Debug("Loading baseline folder {Folder}", Folder);
        BaselineReadResult read;
        DellSettingsFileResult? dell;
        try
        {
            // The Dell file is read on the same worker, and only from a folder whose variables
            // loaded; a missing or unusable one never fails the load (the Dell state line says so).
            (read, dell) = await Task.Run(() =>
            {
                BaselineReadResult variables = BaselineReader.Read(Folder, cancellationToken);
                return (variables, variables.Error is null ? DellSettingsFile.Read(Folder, _logger) : null);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // BaselineReader.Read never throws; this guards the task machinery itself.
            _logger.Debug(ex, "Baseline folder {Folder} could not be read", Folder);
            return Failed(Description, "Reading the baseline failed: " + ex.Message);
        }

        if (read.Error is not null)
        {
            _logger.Debug("Baseline folder {Folder} not loaded: {Error}", Folder, read.Error);
            return Failed(Description, read.Error);
        }

        List<string> warnings = new(read.Warnings);
        IReadOnlyList<FirmwareVariable> variables = WithoutRepeatedKeys(read.Variables, warnings);
        string description = DescribeLoaded(Folder, read.Index);
        if (DisplayTimestamp(read.Index) is null
            && (!string.IsNullOrWhiteSpace(read.Index?.TimestampLocal) || !string.IsNullOrWhiteSpace(read.Index?.TimestampUtc)))
        {
            warnings.Add(Strings.BaselineTimestampUnreadable);
        }

        foreach (string warning in warnings)
        {
            _logger.Debug("Baseline folder {Folder}: {Warning}", Folder, warning);
        }

        _logger.Debug(
            "Loaded {Count} variables from {Description} with {WarningCount} warnings",
            variables.Count,
            description,
            warnings.Count);
        _logger.Debug(
            "Baseline folder {Folder}: Dell settings file {Presence}{Problem}",
            Folder,
            dell?.Present == true ? "present" : "absent",
            dell?.Problem is string problem ? ", which cannot be used: " + problem : string.Empty);

        return new SourceResult(
            SourceStatus.Loaded,
            description,
            IsOffline,
            read.FirmwareType,
            null,
            variables,
            EnumerationMethod.InMemory,
            warnings,
            read.Identity,
            null)
        {
            DellBaseline = dell,
            BaselineFolderName = FolderName(Folder),
        };
    }

    /// <summary>
    /// The name of a baseline folder as the Dell state line says it: the last part of the path,
    /// without a trailing separator ("sample-baseline" for "tools\sample-baseline\"); the path as
    /// given when it has no last part (a drive's root).
    /// </summary>
    /// <param name="folder">The folder path as given.</param>
    /// <returns>The name.</returns>
    internal static string FolderName(string folder)
    {
        string path = folder ?? string.Empty;
        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? path : name;
    }

    /// <summary>
    /// Keeps the first entry of every key (vendor GUID and name, the name compared ordinally as
    /// the firmware does) and drops the later ones, adding one warning that names the repeated keys.
    /// </summary>
    /// <param name="variables">The variables in index order.</param>
    /// <param name="warnings">Receives the warning when a key repeats.</param>
    /// <returns>The variables with every key once, in index order; the same list when nothing repeats.</returns>
    internal static IReadOnlyList<FirmwareVariable> WithoutRepeatedKeys(IReadOnlyList<FirmwareVariable> variables, List<string> warnings)
    {
        HashSet<(Guid VendorGuid, string Name)> seen = new();
        HashSet<(Guid VendorGuid, string Name)> named = new();
        List<string> repeated = new();
        List<FirmwareVariable> unique = new(variables.Count);
        foreach (FirmwareVariable variable in variables)
        {
            if (seen.Add((variable.VendorGuid, variable.Name)))
            {
                unique.Add(variable);
            }
            else if (named.Add((variable.VendorGuid, variable.Name)))
            {
                repeated.Add(variable.Key);
            }
        }

        if (repeated.Count == 0)
        {
            return variables;
        }

        string keys = string.Join(", ", repeated.Take(MaximumKeysNamed));
        if (repeated.Count > MaximumKeysNamed)
        {
            keys += ", " + string.Format(CultureInfo.InvariantCulture, Strings.MoreKeysFormat, repeated.Count - MaximumKeysNamed);
        }

        warnings.Add(string.Format(CultureInfo.InvariantCulture, Strings.BaselineDuplicateKeysFormat, Strings.Variables(repeated.Count), keys));
        return unique;
    }

    private static bool TryParse(string? text, string[] formats, DateTimeStyles styles, out DateTimeOffset value)
    {
        value = default;
        return !string.IsNullOrWhiteSpace(text)
            && DateTimeOffset.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture, styles, out value);
    }

    private SourceResult Failed(string description, string message) => new(
        SourceStatus.Failed,
        description,
        IsOffline,
        FirmwareType.Unknown,
        null,
        Array.Empty<FirmwareVariable>(),
        EnumerationMethod.None,
        Array.Empty<string>(),
        null,
        message);
}
