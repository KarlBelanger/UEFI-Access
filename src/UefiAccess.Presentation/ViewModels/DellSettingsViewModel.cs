// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.ObjectModel;
using System.Globalization;
using UefiAccess.Core.Baseline;
using UefiAccess.Core.Settings;
using UefiAccess.Core.Settings.Dell;
using UefiAccess.Presentation.Infrastructure;
using UefiAccess.Presentation.Resources;

namespace UefiAccess.Presentation.ViewModels;

/// <summary>Which Dell BIOS settings the region shows.</summary>
public enum DellViewMode
{
    /// <summary>This computer's: the session cache, the last live read that answered (idle and after a live load).</summary>
    Live,

    /// <summary>The ones the baseline folder shown records (after a baseline load, whether it loaded, was refused or failed); live values are never shown beside baseline variables.</summary>
    Baseline,
}

/// <summary>What a live read did to the session cache, for the shell's Status sentence.</summary>
internal enum DellApplyKind
{
    /// <summary>The first read that answered, or one that answered from another interface than the cached read.</summary>
    FirstLoad,

    /// <summary>A later read from the same interface that found every value as before.</summary>
    Unchanged,

    /// <summary>A later read from the same interface that found values changed.</summary>
    Changed,

    /// <summary>No interface answered, and no earlier read is kept.</summary>
    Unavailable,

    /// <summary>No interface answered; the values of an earlier read are kept.</summary>
    UnavailableKept,
}

/// <summary>What a live read did, as <see cref="DellSettingsViewModel.ApplyLive"/> reports it.</summary>
/// <param name="Kind">The kind.</param>
/// <param name="Changes">For <see cref="DellApplyKind.Changed"/>, each changed setting in catalog order as "&lt;name&gt;, now &lt;value&gt;" (<see cref="Strings.DellChangeItemFormat"/>); empty otherwise.</param>
/// <param name="KeptReadTime">For <see cref="DellApplyKind.UnavailableKept"/>, the time of the kept read as the state line says it (HH:mm:ss, local); null otherwise.</param>
internal sealed record DellApplyOutcome(DellApplyKind Kind, IReadOnlyList<string> Changes, string? KeptReadTime);

/// <summary>
/// The Dell settings region: the list of the agreed Dell
/// BIOS settings, one row each, and the state line that says when and where they were loaded, or
/// why they could not be. The rows are built from the catalog when the window starts, the first
/// one selected, so Tab and F6 always land on a row; the collection is never cleared, reset,
/// replaced or added to, and every read, baseline folder or return to the live view changes the
/// rows' texts in place (<see cref="DellSettingRowViewModel.Update"/>), so an unchanged read raises
/// no event on the rows (Alpha 1 finding A1-6, Alpha 2 finding A2-7). It holds the session cache,
/// the last live read that answered, for the life of the window: never written to disk, never read
/// at start-up or by Refresh, replaced only by the next read that answers. It reads nothing itself
/// and exposes no command: Read Dell settings is the shell's (<c>ShellViewModel.ReadDellSettingsCommand</c>),
/// and nothing here can change a setting.
/// </summary>
public sealed class DellSettingsViewModel : ObservableObject
{
    /// <summary>The read time on the live state line and in the kept sentence: local, to the second.</summary>
    internal const string LiveTimeFormat = "HH:mm:ss";

    /// <summary>The read time on the baseline state line: UTC, as the file records it.</summary>
    internal const string BaselineTimeFormat = "d MMMM yyyy HH:mm 'UTC'";

    /// <summary>How the changes of one read are joined in <see cref="Strings.DellChangedFormat"/>.</summary>
    internal const string ChangeSeparator = "; ";

    private readonly ObservableCollection<DellSettingRowViewModel> _rows;
    private DellSettingRowViewModel? _selectedRow;
    private string _stateText = Strings.DellNotLoadedLine;
    private bool _isLoading;
    private DellViewMode _mode = DellViewMode.Live;
    private string? _arrivalClause;
    private DellSettingsResult? _cache;
    private string? _failure;

    /// <summary>Creates the region before any read: one row per agreed setting, each "not loaded yet", the first selected, and the state line <see cref="Strings.DellNotLoadedLine"/>.</summary>
    public DellSettingsViewModel()
    {
        _rows = new ObservableCollection<DellSettingRowViewModel>(DellAttributeCatalog.Default.Select(entry => new DellSettingRowViewModel(entry.Id, entry.DisplayName)));
        Rows = new ReadOnlyObservableCollection<DellSettingRowViewModel>(_rows);
        _selectedRow = _rows.Count > 0 ? _rows[0] : null;
    }

    /// <summary>The rows, one per agreed setting in catalog order; the same objects for the life of the window.</summary>
    public ReadOnlyObservableCollection<DellSettingRowViewModel> Rows { get; }

    /// <summary>The selected row (two-way bound to the list); the first row from the start.</summary>
    public DellSettingRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set => SetProperty(ref _selectedRow, value);
    }

    /// <summary>The state line (one line): when and where the values shown were loaded, or why they could not be.</summary>
    public string StateText
    {
        get => _stateText;
        private set => SetProperty(ref _stateText, value);
    }

    /// <summary>True while a read of this computer's Dell BIOS settings runs; the shell sets it.</summary>
    public bool IsLoading
    {
        get => _isLoading;
        internal set => SetProperty(ref _isLoading, value);
    }

    /// <summary>Whether the region shows this computer's settings or a baseline folder's.</summary>
    public DellViewMode Mode
    {
        get => _mode;
        private set => SetProperty(ref _mode, value);
    }

    /// <summary>
    /// What the region's arrival announcement says after "Dell settings region, " when the rows need
    /// it: "from baseline folder &lt;name&gt;" while a baseline folder is shown, "could not be loaded"
    /// while there are no values to show because a read or a baseline load gave none; null otherwise
    /// (before the first read and after any read that answered, the bare region name: the read time
    /// is on the state line; A2-9).
    /// </summary>
    public string? ArrivalClause
    {
        get => _arrivalClause;
        private set => SetProperty(ref _arrivalClause, value);
    }

    /// <summary>
    /// Takes the result of a read of this computer. A read that answered
    /// replaces the session cache; one that did not keeps it and is remembered, so the live view
    /// says why. The rows and the state line change only in <see cref="DellViewMode.Live"/>: while a
    /// baseline folder is shown only the cache takes the result, and the live view shows it after
    /// the next live load. Never reads anything.
    /// </summary>
    /// <param name="result">The read's result.</param>
    /// <returns>What the read did, for the shell's Status sentence.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is null.</exception>
    internal DellApplyOutcome ApplyLive(DellSettingsResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        DellApplyOutcome outcome;
        if (result.Outcome == DellReadOutcome.Unavailable)
        {
            _failure = DellSentences.UnavailableSentence(result) ?? DellSentences.UnavailableLead;
            outcome = _cache is null
                ? new DellApplyOutcome(DellApplyKind.Unavailable, Array.Empty<string>(), null)
                : new DellApplyOutcome(DellApplyKind.UnavailableKept, Array.Empty<string>(), LiveTime(_cache));
        }
        else
        {
            DellSettingsResult? earlier = _cache;
            _cache = result;
            _failure = null;
            outcome = earlier is null || earlier.Source != result.Source
                ? new DellApplyOutcome(DellApplyKind.FirstLoad, Array.Empty<string>(), null)
                : Compare(earlier, result);
        }

        if (Mode == DellViewMode.Live)
        {
            ShowLive();
        }

        return outcome;
    }

    /// <summary>
    /// Shows this computer's settings from the session cache, without reading (after every live
    /// load, Refresh after a baseline folder among them): the cached rows with the live state line,
    /// or the kept sentence after a read that gave no answer; "not loaded" and the problem sentence
    /// when no read answered yet but one failed; "not loaded yet" before any read.
    /// </summary>
    internal void ShowLive()
    {
        Mode = DellViewMode.Live;
        if (_cache is null)
        {
            bool failed = _failure is not null;
            string value = failed ? Strings.DellNotLoadedValue : Strings.DellNotLoadedYetValue;
            ShowRows(_ => (value, string.Empty, null));
            StateText = _failure ?? Strings.DellNotLoadedLine;
            ArrivalClause = failed ? Strings.DellArrivalUnavailable : null;
            return;
        }

        IReadOnlyDictionary<string, FirmwareSetting> settings = ById(_cache);
        ShowRows(row => LiveTexts(settings.GetValueOrDefault(row.Id)));
        StateText = _failure is null ? LiveLine(_cache) : Format(Strings.DellKeptFormat, _failure, LiveTime(_cache));
        ArrivalClause = null;
    }

    /// <summary>
    /// Shows the Dell BIOS settings a loaded baseline folder records (its dell-settings.json as
    /// <see cref="DellSettingsFile"/> read it), labelled as recorded, never as live: the recorded
    /// rows with the baseline state line when the file holds a read that answered; otherwise
    /// every row "not recorded in this baseline folder" and the state line of a folder without the
    /// file, of a file that cannot be used, or of a read that gave no answer when the folder was saved.
    /// </summary>
    /// <param name="file">What the baseline source found; null counts as no file.</param>
    /// <param name="folderName">The baseline folder's name.</param>
    internal void ShowBaseline(DellSettingsFileResult? file, string folderName)
    {
        string name = folderName ?? string.Empty;
        Mode = DellViewMode.Baseline;
        ArrivalClause = Format(Strings.DellArrivalBaselineFormat, name);
        DellSettingsResult? recorded = file?.Settings;
        if (file is null || !file.Present)
        {
            ShowRows(_ => (Strings.DellNotRecordedValue, string.Empty, null));
            StateText = Format(Strings.DellBaselineNoneLineFormat, name);
            return;
        }

        if (recorded is null)
        {
            ShowRows(_ => (Strings.DellNotRecordedValue, string.Empty, null));
            StateText = Format(Strings.DellBaselineUnusableLineFormat, name, (file.Problem ?? DellSettingsFile.UnreadableProblem).TrimEnd('.'));
            return;
        }

        if (recorded.Outcome == DellReadOutcome.Unavailable || recorded.Source is null)
        {
            ShowRows(_ => (Strings.DellNotRecordedValue, string.Empty, null));
            string causes = string.Join(" ", (recorded.Problems ?? Array.Empty<DellInterfaceProblem>()).Select(DellSentences.Cause).Distinct(StringComparer.Ordinal));
            StateText = Format(Strings.DellBaselineUnavailableLineFormat, name, causes).TrimEnd();
            return;
        }

        IReadOnlyDictionary<string, FirmwareSetting> settings = ById(recorded);
        ShowRows(row => BaselineTexts(settings.GetValueOrDefault(row.Id)));
        StateText = Format(
            Strings.DellBaselineLineFormat,
            recorded.ReadAt.ToUniversalTime().ToString(BaselineTimeFormat, CultureInfo.InvariantCulture),
            DellSentences.SourceName(recorded.Source.Value),
            name,
            Bios(recorded.Machine));
    }

    /// <summary>A baseline folder load was refused or failed: no Dell BIOS settings are shown (live ones never beside a baseline's), every row "not loaded".</summary>
    internal void ShowBaselineNotLoaded()
    {
        Mode = DellViewMode.Baseline;
        ShowRows(_ => (Strings.DellNotLoadedValue, string.Empty, null));
        StateText = Strings.DellBaselineNotLoadedLine;
        ArrivalClause = Strings.DellArrivalUnavailable;
    }

    /// <summary>The live state line of a read that answered: when, from which interface, the BIOS version, then the fallback's note and the result's warnings.</summary>
    private static string LiveLine(DellSettingsResult result)
    {
        string source = DellSentences.SourceName(result.Source ?? DellSettingsSource.DellCommandMonitor);
        string line = Format(Strings.DellLiveLineFormat, LiveTime(result), source, Bios(result.Machine));
        if (result.Source == DellSettingsSource.DellCommandConfigure)
        {
            line += Strings.DellFallbackLineSuffix;
        }

        foreach (string warning in result.Warnings ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(warning))
            {
                line += " " + warning.Trim();
            }
        }

        return line;
    }

    private static string LiveTime(DellSettingsResult result) => result.ReadAt.ToLocalTime().ToString(LiveTimeFormat, CultureInfo.InvariantCulture);

    private static string Bios(DellMachine? machine) => string.IsNullOrWhiteSpace(machine?.BiosVersion) ? Strings.DellBiosUnknown : machine.BiosVersion.Trim();

    /// <summary>A setting's texts in the live view: Core's words for anything read, the window's "not loaded" for a setting nothing was read for.</summary>
    private static (string Current, string Possible, string? Note) LiveTexts(FirmwareSetting? setting)
    {
        return setting is null || setting.State == SettingState.NotRead
            ? (Strings.DellNotLoadedValue, PossibleText(setting), setting?.Note)
            : (DellSentences.ValueText(setting), PossibleText(setting), setting.Note);
    }

    /// <summary>A setting's texts in the baseline view: Core's words for anything recorded, "not recorded in this baseline folder" for a setting the file lacks or did not read.</summary>
    private static (string Current, string Possible, string? Note) BaselineTexts(FirmwareSetting? setting)
    {
        return setting is null || setting.State == SettingState.NotRead
            ? (Strings.DellNotRecordedValue, PossibleText(setting), setting?.Note)
            : (DellSentences.ValueText(setting), PossibleText(setting), setting.Note);
    }

    private static string PossibleText(FirmwareSetting? setting)
    {
        return setting is null ? string.Empty : string.Join(DellSettingRowViewModel.PossibleSeparator, (setting.PossibleValues ?? Array.Empty<SettingValue>()).Select(value => value.Text).Where(text => !string.IsNullOrEmpty(text)));
    }

    /// <summary>The changes between two reads of the same interface, by the words of each current value in the live view.</summary>
    private static DellApplyOutcome Compare(DellSettingsResult earlier, DellSettingsResult now)
    {
        IReadOnlyDictionary<string, FirmwareSetting> before = ById(earlier);
        IReadOnlyDictionary<string, FirmwareSetting> after = ById(now);
        List<string> changes = new();
        foreach (DellCatalogEntry entry in DellAttributeCatalog.Default)
        {
            string was = LiveTexts(before.GetValueOrDefault(entry.Id)).Current;
            string isNow = LiveTexts(after.GetValueOrDefault(entry.Id)).Current;
            if (!string.Equals(was, isNow, StringComparison.Ordinal))
            {
                changes.Add(Format(Strings.DellChangeItemFormat, entry.DisplayName, isNow));
            }
        }

        return changes.Count == 0
            ? new DellApplyOutcome(DellApplyKind.Unchanged, Array.Empty<string>(), null)
            : new DellApplyOutcome(DellApplyKind.Changed, changes, null);
    }

    /// <summary>A result's settings by catalog id, the first of each id.</summary>
    private static IReadOnlyDictionary<string, FirmwareSetting> ById(DellSettingsResult result)
    {
        Dictionary<string, FirmwareSetting> settings = new(StringComparer.Ordinal);
        foreach (FirmwareSetting setting in result.Settings ?? Array.Empty<FirmwareSetting>())
        {
            if (setting?.Id is string id)
            {
                settings.TryAdd(id, setting);
            }
        }

        return settings;
    }

    /// <summary>Updates every row in place with its texts; a row whose texts did not change raises nothing.</summary>
    private void ShowRows(Func<DellSettingRowViewModel, (string Current, string Possible, string? Note)> texts)
    {
        foreach (DellSettingRowViewModel row in _rows)
        {
            (string current, string possible, string? note) = texts(row);
            row.Update(current, possible, note);
        }
    }

    private static string Format(string format, params object[] arguments) => string.Format(CultureInfo.InvariantCulture, format, arguments);
}
