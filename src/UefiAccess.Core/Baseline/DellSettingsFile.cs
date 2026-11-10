// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Text;
using System.Text.Json;
using Serilog;
using UefiAccess.Core.Settings;
using UefiAccess.Core.Settings.Dell;

namespace UefiAccess.Core.Baseline;

/// <summary>
/// The document of dell-settings.json, the Dell BIOS settings of a baseline folder: one read's agreed settings in the interface's tokens and Dell's names only. It
/// holds no display text, no note and no problem detail, because
/// <see cref="DellSettingsFile.Read(string)"/> derives every text again through the current
/// catalog. The machine is its three fields, never a serial number or service tag.
/// </summary>
/// <param name="Format">Always <see cref="DellSettingsFile.FormatName"/>.</param>
/// <param name="ReadAtUtc">When the read started, in UTC, "yyyy-MM-ddTHH:mm:ssZ".</param>
/// <param name="Outcome">The read's outcome, a <see cref="DellReadOutcome"/> name.</param>
/// <param name="Source">The interface that answered, a <see cref="DellSettingsSource"/> name; null when the outcome is Unavailable.</param>
/// <param name="Machine">The machine's manufacturer, model and BIOS version.</param>
/// <param name="Settings">One entry per agreed setting, in catalog order.</param>
/// <param name="Problems">The read's problems, in the order met, without their details.</param>
public sealed record DellSettingsFileDocument(
    string Format,
    string ReadAtUtc,
    string Outcome,
    string? Source,
    DellMachine Machine,
    IReadOnlyList<DellSettingsFileSetting> Settings,
    IReadOnlyList<DellSettingsFileProblem> Problems);

/// <summary>One agreed setting in dell-settings.json: its catalog id, its state, and the interface's tokens and names.</summary>
/// <param name="Id">The catalog id, for example "NumLock".</param>
/// <param name="State">The setting's state as read, a <see cref="SettingState"/> name.</param>
/// <param name="CurrentToken">The current value's token (Dell Command Monitor's code, Dell Command Configure's word); null when there is none.</param>
/// <param name="CurrentVendorName">Dell Command Monitor's name for the current value; null when the interface gives none.</param>
/// <param name="PossibleValues">The possible values' tokens and names, in the interface's order.</param>
/// <param name="PossibleValuesOrigin">Where the possible values come from, a <see cref="UefiAccess.Core.Settings.PossibleValuesOrigin"/> name.</param>
/// <param name="VendorMarksReadOnly">Dell's read-only flag as reported; null when the interface reports none.</param>
public sealed record DellSettingsFileSetting(
    string Id,
    string State,
    string? CurrentToken,
    string? CurrentVendorName,
    IReadOnlyList<DellSettingsFileValue> PossibleValues,
    string PossibleValuesOrigin,
    bool? VendorMarksReadOnly);

/// <summary>One possible value in dell-settings.json.</summary>
/// <param name="Token">The interface's token for the value.</param>
/// <param name="VendorName">Dell Command Monitor's name for it; null when the interface gives none.</param>
public sealed record DellSettingsFileValue(string Token, string? VendorName);

/// <summary>One problem of the read in dell-settings.json: where it lay and its kind, never its detail.</summary>
/// <param name="Interface">A <see cref="DellInterface"/> name.</param>
/// <param name="Kind">A <see cref="DellProblemKind"/> name.</param>
public sealed record DellSettingsFileProblem(string Interface, string Kind);

/// <summary>What <see cref="DellSettingsFile.Read(string)"/> found in a baseline folder.</summary>
/// <param name="Present">False when the folder holds no dell-settings.json, which is not an error (folders saved before Alpha 2 never do).</param>
/// <param name="Settings">The recorded read with every text derived again through the current catalog; null when the file is missing or cannot be used.</param>
/// <param name="Problem">Why a file that is present cannot be used, one of the problem constants of <see cref="DellSettingsFile"/>; null otherwise.</param>
public sealed record DellSettingsFileResult(bool Present, DellSettingsResult? Settings, string? Problem);

/// <summary>
/// Writes and reads dell-settings.json, the Dell BIOS settings a baseline folder carries
///. <see cref="ToDocument"/> keeps of a read only what the file holds:
/// the states, the tokens, Dell's names, Dell's flag, the machine's three fields and the
/// problems' kinds; the exporter writes it after platform.json and before log.txt. A baseline
/// folder is untrusted input that the elevated application opens, so <see cref="Read(string)"/>
/// takes the file as the baseline reader takes its files: a link or junction is refused, no more
/// than <see cref="MaximumFileSize"/> bytes are ever read, and a file over a limit of its own
/// (<see cref="MaximumSettings"/>, <see cref="MaximumPossibleValues"/>,
/// <see cref="MaximumProblems"/>, <see cref="MaximumTextLength"/>, <see cref="MaximumDepth"/>) is
/// refused whole, never cut. Control characters are stripped. Every text the window shows is
/// derived again through the current catalog (<see cref="DellSettingMapper"/>), so a crafted file
/// can put no text in the window but Dell's names, tokens and the machine's three fields, each
/// capped. Nothing here reads a Dell interface or starts a process, and reading never throws.
/// </summary>
public static class DellSettingsFile
{
    /// <summary>The value of <see cref="DellSettingsFileDocument.Format"/>; any other value is refused.</summary>
    public const string FormatName = "uefiaccess-dell-settings/1";

    /// <summary>Largest file the reader accepts, in bytes: 256 KiB, many times the few kilobytes the ten agreed settings take; a larger file is refused before it is read.</summary>
    public const int MaximumFileSize = 256 * 1024;

    /// <summary>Most settings a file may list.</summary>
    public const int MaximumSettings = 64;

    /// <summary>Most possible values one setting may list.</summary>
    public const int MaximumPossibleValues = 32;

    /// <summary>Most problems a file may list.</summary>
    public const int MaximumProblems = 32;

    /// <summary>Most characters one text of the file may hold; a longer text refuses the file, it is never cut.</summary>
    public const int MaximumTextLength = 64;

    /// <summary>Deepest nesting of the JSON the reader accepts; the document needs five levels.</summary>
    public const int MaximumDepth = 8;

    /// <summary>The problem of a file larger than <see cref="MaximumFileSize"/>.</summary>
    public const string TooLargeProblem = "it is larger than 256 KiB";

    /// <summary>The problem of a file that is a symbolic link or a junction.</summary>
    public const string LinkProblem = "it is a link or a junction";

    /// <summary>The problem of a file that is not JSON, is nested too deeply, has another format or holds a value this version does not know.</summary>
    public const string UnknownFormatProblem = "it is not a Dell settings file this version can read";

    /// <summary>The problem of a file over <see cref="MaximumSettings"/>, <see cref="MaximumPossibleValues"/> or <see cref="MaximumProblems"/>.</summary>
    public const string TooManyEntriesProblem = "it holds more entries than a Dell settings file can";

    /// <summary>The problem of a file with a text longer than <see cref="MaximumTextLength"/>.</summary>
    public const string TextTooLongProblem = "it holds a text longer than 64 characters";

    /// <summary>The problem of a file the file system would not let the reader read.</summary>
    public const string UnreadableProblem = "it could not be read";

    private const string UtcTimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    private static readonly DellSettingsFileResult NotPresent = new(false, null, null);

    /// <summary>The baseline reader's options (case-insensitive names, trailing commas and comments allowed) with <see cref="MaximumDepth"/>.</summary>
    private static readonly JsonSerializerOptions FileReadOptions = new(BaselineReader.ReadOptions) { MaxDepth = MaximumDepth };

    /// <summary>
    /// The document of one read (pure): the outcome, the source, the read time in UTC to the
    /// second, the machine's three fields, one entry per setting with its state, its tokens,
    /// Dell's names and Dell's flag, and each problem's interface and kind. No display text, no
    /// note and no problem detail is kept.
    /// </summary>
    /// <param name="result">The read, as the settings reader returned it.</param>
    /// <returns>The document the exporter writes as dell-settings.json.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is null.</exception>
    public static DellSettingsFileDocument ToDocument(DellSettingsResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        DellMachine machine = result.Machine ?? new DellMachine(null, null, null);
        return new DellSettingsFileDocument(
            FormatName,
            result.ReadAt.ToUniversalTime().ToString(UtcTimestampFormat, CultureInfo.InvariantCulture),
            result.Outcome.ToString(),
            result.Source?.ToString(),
            new DellMachine(machine.Manufacturer, machine.Model, machine.BiosVersion),
            (result.Settings ?? Array.Empty<FirmwareSetting>()).Where(setting => setting is not null).Select(ToFileSetting).ToArray(),
            (result.Problems ?? Array.Empty<DellInterfaceProblem>()).Where(problem => problem is not null).Select(problem => new DellSettingsFileProblem(problem.Interface.ToString(), problem.Kind.ToString())).ToArray());
    }

    /// <summary>Reads dell-settings.json of a baseline folder without logging; see <see cref="Read(string, ILogger)"/>.</summary>
    /// <param name="folder">The baseline folder.</param>
    /// <returns>What was found; never null.</returns>
    public static DellSettingsFileResult Read(string folder) => Read(folder, null);

    /// <summary>
    /// Reads dell-settings.json of a baseline folder (R6). A missing file is not present, not an
    /// error. A file that is a link or junction, larger than <see cref="MaximumFileSize"/>, not
    /// JSON, of another format, over a limit, or holding a value this version does not know is
    /// refused whole with one of the problem constants. Otherwise every agreed setting is derived
    /// again through the current catalog from the file's tokens and names: an id the catalog does
    /// not know is ignored (one Debug line), a catalog id the file lacks is not read, the
    /// dependency rule is applied, and the outcome, the BIOS warning, the fallback sentence and the
    /// native interface flag are worked out again, as the settings reader would. Never throws.
    /// </summary>
    /// <param name="folder">The baseline folder.</param>
    /// <param name="logger">Optional logger for the Debug lines; null disables logging.</param>
    /// <returns>What was found; never null.</returns>
    public static DellSettingsFileResult Read(string folder, ILogger? logger)
    {
        ILogger log = logger ?? Serilog.Core.Logger.None;
        string path = BaselineExporter.DellSettingsFileName;
        try
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                return NotPresent;
            }

            string directory = Path.GetFullPath(folder);
            if (!Directory.Exists(directory))
            {
                return NotPresent;
            }

            if (!BaselineReader.TryResolveFile(directory, BaselineExporter.DellSettingsFileName, out path))
            {
                return Refused(log, path, UnreadableProblem, "the path could not be resolved");
            }

            if (BaselineReader.TryFindLink(directory, path, out _))
            {
                return Refused(log, path, LinkProblem, null);
            }

            if (!File.Exists(path))
            {
                return Directory.Exists(path) ? Refused(log, path, UnreadableProblem, "it is a folder") : NotPresent;
            }

            if (new FileInfo(path).Length > MaximumFileSize)
            {
                return Refused(log, path, TooLargeProblem, null);
            }

            byte[] bytes = BaselineReader.ReadAtMost(path, MaximumFileSize + 1);
            if (bytes.Length > MaximumFileSize)
            {
                return Refused(log, path, TooLargeProblem, null);
            }

            using MemoryStream stream = new(bytes, writable: false);
            using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return FromText(reader.ReadToEnd(), path, log);
        }
        catch (Exception ex)
        {
            return Refused(log, path, UnreadableProblem, ex.Message);
        }
    }

    private static DellSettingsFileSetting ToFileSetting(FirmwareSetting setting)
    {
        return new DellSettingsFileSetting(
            setting.Id,
            setting.State.ToString(),
            setting.Current?.Token,
            setting.Current?.VendorName,
            (setting.PossibleValues ?? Array.Empty<SettingValue>()).Where(value => value is not null).Select(value => new DellSettingsFileValue(value.Token, value.VendorName)).ToArray(),
            setting.PossibleValuesOrigin.ToString(),
            setting.VendorMarksReadOnly);
    }

    /// <summary>The file's text checked against the format and the limits, then derived again through the current catalog.</summary>
    private static DellSettingsFileResult FromText(string json, string path, ILogger log)
    {
        DellSettingsFileDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<DellSettingsFileDocument>(json, FileReadOptions);
        }
        catch (Exception ex)
        {
            return Refused(log, path, UnknownFormatProblem, ex.Message);
        }

        if (document is null || !string.Equals(document.Format, FormatName, StringComparison.Ordinal))
        {
            return Refused(log, path, UnknownFormatProblem, "the format is not " + FormatName);
        }

        IReadOnlyList<DellSettingsFileSetting?> fileSettings = document.Settings ?? Array.Empty<DellSettingsFileSetting>();
        IReadOnlyList<DellSettingsFileProblem?> fileProblems = document.Problems ?? Array.Empty<DellSettingsFileProblem>();
        if (fileSettings.Count > MaximumSettings
            || fileProblems.Count > MaximumProblems
            || fileSettings.Any(setting => setting?.PossibleValues is { Count: > MaximumPossibleValues }))
        {
            return Refused(log, path, TooManyEntriesProblem, null);
        }

        if (Texts(document).Any(text => text is not null && text.Length > MaximumTextLength))
        {
            return Refused(log, path, TextTooLongProblem, null);
        }

        if (!DateTimeOffset.TryParseExact(document.ReadAtUtc, UtcTimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset readAt)
            || !TryParseName(document.Outcome, out DellReadOutcome outcome)
            || !TryParseSource(document.Source, out DellSettingsSource? source)
            || (outcome != DellReadOutcome.Unavailable && source is null)
            || !TryParseSettings(fileSettings, out List<(DellSettingsFileSetting Setting, SettingState State)> rows)
            || !TryParseProblems(fileProblems, out List<(DellInterface Interface, DellProblemKind Kind)> kinds))
        {
            return Refused(log, path, UnknownFormatProblem, "a value is missing or not one this version knows");
        }

        DellMachine machine = new(Clean(document.Machine?.Manufacturer), Clean(document.Machine?.Model), Clean(document.Machine?.BiosVersion));
        DellInterfaceProblem[] problems = kinds
            .Select(item => new DellInterfaceProblem(item.Interface, item.Kind, item.Kind == DellProblemKind.NotDell ? machine.Manufacturer ?? string.Empty : string.Empty))
            .ToArray();
        IReadOnlyList<DellCatalogEntry> catalog = DellAttributeCatalog.Default;

        if (outcome == DellReadOutcome.Unavailable || source is not DellSettingsSource answeredBy)
        {
            DellSettingsResult unavailable = new(
                DellReadOutcome.Unavailable,
                null,
                readAt,
                0,
                machine,
                catalog.Select(entry => DellSettingMapper.NotRead(entry, null)).ToArray(),
                problems,
                Array.Empty<string>())
            {
                NativeInterfacePresent = problems.Any(problem => problem.Kind == DellProblemKind.NativeInterfaceOnly),
            };
            return new DellSettingsFileResult(true, unavailable, null);
        }

        Dictionary<string, (DellSettingsFileSetting Setting, SettingState State)> byId = new(StringComparer.Ordinal);
        foreach ((DellSettingsFileSetting setting, SettingState state) in rows)
        {
            string id = Clean(setting.Id) ?? string.Empty;
            if (DellAttributeCatalog.Find(id) is null)
            {
                log.Debug("Dell settings file {Path}: the id {Id} is not an agreed setting; it was ignored", path, id);
            }
            else if (!byId.TryAdd(id, (setting, state)))
            {
                log.Debug("Dell settings file {Path}: the id {Id} is listed more than once; the first is used", path, id);
            }
        }

        FirmwareSetting[] mapped = catalog
            .Select(entry => byId.TryGetValue(entry.Id, out (DellSettingsFileSetting Setting, SettingState State) row) ? Map(entry, row.Setting, row.State, answeredBy) : DellSettingMapper.NotRead(entry, null))
            .ToArray();
        IReadOnlyList<FirmwareSetting> settings = DellSettingsReader.ApplyDependencies(catalog, mapped);
        DellReadOutcome usable = settings.All(setting => setting.State is SettingState.Read or SettingState.NotApplicable) ? DellReadOutcome.Complete : DellReadOutcome.Partial;
        string[] warnings = DellSettingsReader.OtherBios(machine.BiosVersion) is string bios
            ? new[] { string.Format(CultureInfo.InvariantCulture, DellSentences.OtherBiosFormat, DellAttributeCatalog.VerifiedBiosVersion, bios) }
            : Array.Empty<string>();

        DellSettingsResult result = new(usable, answeredBy, readAt, 0, machine, settings, problems, warnings)
        {
            FallbackSentence = FallbackSentence(answeredBy, problems),
        };
        return new DellSettingsFileResult(true, result, null);
    }

    /// <summary>
    /// One agreed setting from its entry in the file, through the mapper the providers use. A
    /// setting not read stays not read. From Dell Command Configure, a setting the fallback never
    /// reads (Boot list option) is not reported with its note whatever the file says, one without
    /// a word is not reported, and a word is named as the provider names it, the possible values
    /// being the catalog's. From Dell Command Monitor, a setting not reported stays so, and every
    /// other one is named from the file's codes and Dell's names, so its state, its texts and its
    /// note follow from them and the catalog, never from the file's state.
    /// </summary>
    private static FirmwareSetting Map(DellCatalogEntry entry, DellSettingsFileSetting setting, SettingState state, DellSettingsSource source)
    {
        if (state == SettingState.NotRead)
        {
            return DellSettingMapper.NotRead(entry, null);
        }

        string? token = Clean(setting.CurrentToken);
        if (source == DellSettingsSource.DellCommandConfigure)
        {
            if (entry.CctkOption is null)
            {
                return DellSettingMapper.NotReported(entry, PossibleValuesOrigin.Catalog, DellSentences.NotReadByFallbackNote);
            }

            return state == SettingState.NotReported || string.IsNullOrWhiteSpace(token)
                ? DellSettingMapper.NotReported(entry, PossibleValuesOrigin.Catalog, null)
                : DellSettingMapper.FromCctk(entry, token);
        }

        if (state == SettingState.NotReported)
        {
            return DellSettingMapper.NotReported(entry, PossibleValuesOrigin.Interface, null);
        }

        // Dell Command Monitor's codes are numbers; several current codes were joined with ", ".
        string[] codes = token?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
        IReadOnlyList<DellSettingsFileValue> values = setting.PossibleValues ?? Array.Empty<DellSettingsFileValue>();
        string[] possible = values.Select(value => Clean(value.Token) ?? string.Empty).ToArray();
        string[] names = values.Select(value => Clean(value.VendorName) ?? string.Empty).ToArray();
        return DellSettingMapper.FromDcm(entry, codes, possible, names, setting.VendorMarksReadOnly);
    }

    /// <summary>
    /// The fallback sentence of a recorded read, as the settings reader
    /// makes it: when Dell Command Configure answered and the first problem met was not its own
    /// (so Dell Command Monitor was tried first and gave no answer), that problem's cause followed
    /// by the fallback clause; otherwise null.
    /// </summary>
    private static string? FallbackSentence(DellSettingsSource source, IReadOnlyList<DellInterfaceProblem> problems)
    {
        if (source != DellSettingsSource.DellCommandConfigure || problems.Count == 0 || problems[0].Interface == DellInterface.DellCommandConfigure)
        {
            return null;
        }

        return string.Format(CultureInfo.InvariantCulture, DellSentences.FallbackFormat, DellSentences.Cause(problems[0])).Trim();
    }

    /// <summary>Every text of the document but its format, which must match exactly: the texts <see cref="MaximumTextLength"/> applies to.</summary>
    private static IEnumerable<string?> Texts(DellSettingsFileDocument document)
    {
        yield return document.ReadAtUtc;
        yield return document.Outcome;
        yield return document.Source;
        yield return document.Machine?.Manufacturer;
        yield return document.Machine?.Model;
        yield return document.Machine?.BiosVersion;
        foreach (DellSettingsFileSetting? setting in document.Settings ?? Array.Empty<DellSettingsFileSetting>())
        {
            if (setting is null)
            {
                continue;
            }

            yield return setting.Id;
            yield return setting.State;
            yield return setting.CurrentToken;
            yield return setting.CurrentVendorName;
            yield return setting.PossibleValuesOrigin;
            foreach (DellSettingsFileValue? value in setting.PossibleValues ?? Array.Empty<DellSettingsFileValue>())
            {
                yield return value?.Token;
                yield return value?.VendorName;
            }
        }

        foreach (DellSettingsFileProblem? problem in document.Problems ?? Array.Empty<DellSettingsFileProblem>())
        {
            yield return problem?.Interface;
            yield return problem?.Kind;
        }
    }

    /// <summary>Every setting with its state; false when a setting, its id, its origin or one of its possible values or tokens is missing, or a name is not one this version knows.</summary>
    private static bool TryParseSettings(IReadOnlyList<DellSettingsFileSetting?> settings, out List<(DellSettingsFileSetting Setting, SettingState State)> rows)
    {
        rows = new List<(DellSettingsFileSetting Setting, SettingState State)>(settings.Count);
        foreach (DellSettingsFileSetting? setting in settings)
        {
            if (setting is null
                || setting.Id is null
                || !TryParseName(setting.State, out SettingState state)
                || !TryParseName(setting.PossibleValuesOrigin, out PossibleValuesOrigin _)
                || (setting.PossibleValues is { } values && values.Any(value => value?.Token is null)))
            {
                return false;
            }

            rows.Add((setting, state));
        }

        return true;
    }

    /// <summary>Every problem's interface and kind; false when a problem is missing or a name is not one this version knows.</summary>
    private static bool TryParseProblems(IReadOnlyList<DellSettingsFileProblem?> problems, out List<(DellInterface Interface, DellProblemKind Kind)> kinds)
    {
        kinds = new List<(DellInterface Interface, DellProblemKind Kind)>(problems.Count);
        foreach (DellSettingsFileProblem? problem in problems)
        {
            if (problem is null || !TryParseName(problem.Interface, out DellInterface where) || !TryParseName(problem.Kind, out DellProblemKind kind))
            {
                return false;
            }

            kinds.Add((where, kind));
        }

        return true;
    }

    /// <summary>The source: null stands for none; any other text must be a source's name.</summary>
    private static bool TryParseSource(string? text, out DellSettingsSource? source)
    {
        source = null;
        if (text is null)
        {
            return true;
        }

        if (!TryParseName(text, out DellSettingsSource parsed))
        {
            return false;
        }

        source = parsed;
        return true;
    }

    /// <summary>A member of <typeparamref name="TEnum"/> by its name, compared case-insensitively; a number, which Enum.TryParse would take, is refused.</summary>
    private static bool TryParseName<TEnum>(string? text, out TEnum value)
        where TEnum : struct, Enum
    {
        value = default;
        if (text is null)
        {
            return false;
        }

        foreach (string name in Enum.GetNames<TEnum>())
        {
            if (string.Equals(name, text, StringComparison.OrdinalIgnoreCase))
            {
                value = Enum.Parse<TEnum>(name);
                return true;
            }
        }

        return false;
    }

    /// <summary>The text without its control characters; null stays null.</summary>
    private static string? Clean(string? text)
    {
        return text is null || !text.Any(char.IsControl) ? text : new string(text.Where(character => !char.IsControl(character)).ToArray());
    }

    private static DellSettingsFileResult Refused(ILogger log, string path, string problem, string? detail)
    {
        log.Debug("Dell settings file {Path} cannot be used: {Problem} ({Detail})", path, problem, detail ?? "no detail");
        return new DellSettingsFileResult(true, null, problem);
    }
}
