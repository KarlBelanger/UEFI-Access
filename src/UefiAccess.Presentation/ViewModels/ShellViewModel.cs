// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Input;
using Serilog;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Settings;
using UefiAccess.Core.Settings.Dell;
using UefiAccess.Core.Variables;
using UefiAccess.Presentation.Accessibility;
using UefiAccess.Presentation.Diagnostics;
using UefiAccess.Presentation.Infrastructure;
using UefiAccess.Presentation.Resources;
using UefiAccess.Presentation.Services;

namespace UefiAccess.Presentation.ViewModels;

/// <summary>
/// The root of the GUI: the state machine (Idle, Loading, Loaded, Refused, Failed), the commands
/// (Refresh, Open baseline folder, Clear filter, Copy value, Export visible list, Copy log, and
/// since M4 Read Dell settings), the source description and window title, the machine identity
/// strings, and the child view models for the list, the detail pane, the log and the Dell settings
/// region. Every announcement goes through the
/// <see cref="IAnnouncer"/> and is mirrored into the log as an INFO line, except a splitter's
/// position (<see cref="OnSplitterMoved"/>), which is said after every arrow press and is logged
/// at Debug only, so a resize does not fill the log panel; every focus move goes
/// through the <see cref="IFocusSink"/>; every dialog and clipboard access through the
/// <see cref="IPlatformInteractions"/>; the update of the detail pane after a selection change
/// through the <see cref="IDetailScheduler"/>. Nothing here reads the firmware except through the
/// <see cref="IVariableSource"/>, nothing writes it except the write pipeline behind the
/// <see cref="IWriteService"/> after the person confirmed a plan in the Confirm change dialog, and
/// nothing happens on a timer: every state change, announcement and focus request is the direct
/// consequence of a user action or of the completion of a load, a check or a write the user started
/// (the WPF scheduler's quiet period decides only when a selection change updates the detail pane,
/// and that update says nothing and moves no focus).
/// </summary>
/// <remarks>
/// <para>
/// Transitions: Idle → Loading on Refresh or Open baseline folder; Loading → Loaded, Refused or
/// Failed when the source answers. A Loaded result replaces the list, the detail pane, the
/// identity strings and the source description, announces "Loaded N variables from &lt;source&gt;
/// (&lt;method&gt;)" (followed by "Showing N of M variables" when a filter is in effect) and asks
/// for focus on the first row. A Refused result empties the list,
/// announces the precondition sentence and asks for focus on the Status box. A Failed result
/// does the same with "Loading failed: &lt;text&gt;". Source warnings become WARN lines in the log.
/// </para>
/// <para>
/// While a load runs, Refresh is disabled, but Open baseline folder is not: a folder picked then
/// starts its own load, which replaces the running one (that load is cancelled, and its result is
/// dropped unseen when it arrives), so a live read that is slow to finish is never a dead end.
/// </para>
/// <para>
/// A load can finish while the save dialog of Export visible list or the folder dialog of Open
/// baseline folder is open (the modal dialog's message loop runs the load's completion). The
/// list, the title and the identity then change at once, but the load's sentence and its focus
/// request wait for the dialog to close, where the command announces its own result followed by
/// the load's sentence and repeats the load's focus request; so the sentence is neither spoken
/// behind the dialog nor replaced by "Cancelled; nothing changed.", a folder dialog that fails
/// says "Open baseline folder failed: &lt;reason&gt;" without "Nothing changed.", and the export
/// writes the rows that passed the filter when Export visible list was pressed. A folder picked
/// in that dialog replaces the load that finished behind it: that load's sentence is never
/// spoken, and its focus request becomes one for the Status box while the baseline loads.
/// </para>
/// <para>
/// The log panel has one channel per event. The shell writes what the user should read directly
/// into <see cref="Log"/>: every announcement but a splitter's position as INFO, every source warning as WARN, the expected
/// model notes as INFO or WARN. Failures are logged once through the <see cref="ILogger"/> at
/// Error, with the exception when there is one; the application mirrors the logger (Information
/// and above, by the <see cref="LogPanelMirror"/> rule) into the same panel, so that line is the
/// panel's ERROR line. Everything else the shell logs is Debug, which stays in the log file and
/// never reaches the panel, and the sources log only at Debug and compose their Core services over
/// a file-only logger, because their findings reach the panel through the result; so no event
/// appears twice.
/// </para>
/// <para>
/// Changes. The Change group (<see cref="VariableDetailViewModel.Change"/>)
/// answers a move and Ctrl+Z itself; Review change, Boot once from this entry, Clear one-time boot
/// and Restore previous value hand their action to this shell, which says "Checking the change,
/// please wait.", runs the dry run on a worker thread (Escape cancels it between stages: "Cancelled;
/// nothing changed."), says a refusal as "Change blocked: &lt;reason&gt; Nothing was written." with
/// focus on the Status box, or opens the Confirm change dialog inside the dialog guard, so a load
/// that finishes behind it holds its sentence as in M2. While the checks run, Open baseline folder
/// and Export visible list are disabled as well (their dialogs would otherwise be open when the
/// checks answer, and Confirm change would open on top of them); Refresh stays available. A load
/// that replaces the list while the checks run (a Refresh, or a baseline folder picked before them)
/// cancels them: its sentence starts with "Cancelled; nothing changed.", and a plan that arrives
/// after it opens no dialog and is recorded as cancelled. The dialog never opens on top of another
/// one, nor for a live plan while a baseline is shown, nor for a proposed order that is no longer
/// the one shown; such a plan is cancelled too. Cancel records the cancel and returns focus
/// to the button that opened the dialog; Confirm change turns the dialog's decision into
/// <c>plan.Confirm(choice)</c>, so the commit is bound to the plan the dialog showed, and commits it
/// with <see cref="IsWriting"/> set: Refresh, Open baseline folder, Clear filter, Export visible
/// list, the filter and every Change command are disabled, the window does not close
/// (<see cref="TryClose"/>) and Status says "Writing the change, please wait. Do not restart the
/// machine." Those controls stay disabled (<see cref="IsFinishingChange"/>) until the result is
/// said, through the live reload after the write; the window may close again once the commit has
/// returned. The outcome is applied here, on the caller's context, whatever happened meanwhile. A
/// load started during the checks that finishes while the change is written holds its sentence and
/// focus request as it would behind a dialog, so Status keeps the Writing sentence until the result
/// is in. After a write call was made the live source is loaded again (replacing what such a load
/// showed) and the write's sentence and the Loaded sentence are one announcement; a verified change
/// puts focus on the written variable's row (<see cref="FocusTarget.ListRowByKey"/>), anything else
/// on the Status box. A load that drops a proposed order the firmware does not hold says "Proposed
/// order discarded." before its own sentence, after the write's when it is the reload after one.
/// </para>
/// <para>
/// The detail pane follows the selection through the scheduler (Alpha 1 finding A1-6): a
/// selection change schedules one refresh, <see cref="VariableDetailViewModel.Show"/> of the row
/// selected when the refresh runs, and the WPF scheduler holds it while the key that changed the
/// selection is down and runs it once the list has been quiet for
/// <see cref="DetailRefreshTiming.QuietPeriod"/> after that (Alpha 2 finding A2-7), so neither a
/// held arrow key nor a burst of quick presses updates the pane on every row while the screen
/// reader is still fetching the row's name. The refresh runs earlier, through
/// <see cref="EnsureDetailCurrent"/>, before focus moves into the pane, before an access key acts
/// (the Change group's among them), and before Copy value, Export visible list and every Change
/// command, and a completed load shows the pane at once. With the default
/// <see cref="ImmediateDetailScheduler"/> the pane follows the selection synchronously. The
/// refresh updates the pane in place (<see cref="VariableDetailViewModel.Show"/>): the sections and
/// fields both rows have in the same order stay, and only their values change, so the view keeps
/// their controls instead of removing and re-creating every section, which the A16 measurement
/// found raised a burst of about 150 UI Automation events that delayed the next row's focus event.
/// </para>
/// <para>
/// When the application was started with --trace-input (Alpha 2 finding A2-7, the tester's question
/// about the continued A1-6 issue), <see cref="InputTrace"/> receives a SELECTION line for every
/// change of the selected row (written before the change schedules the pane's update), a DETAIL
/// line for every request to bring the pane up to date (with the caller that asked) and two for
/// every update of the pane, which the trace still calls a rebuild (the row's variable name, how
/// long the update took, how many sections the pane has, how many sections it kept, added and
/// removed, and how many fields it kept, updated, added and removed), and every load registers
/// its identity's service tag for redaction before anything of it is traced.
/// Off, the trace is <see cref="NullInputTrace"/> and nothing is built for it.
/// </para>
/// <para>
/// The Dell BIOS settings are read only by Read Dell settings
/// (Alt+S, <see cref="ReadDellSettingsCommand"/>), never at start-up, on Refresh or on Open
/// baseline folder, through the <see cref="IDellSettingsSource"/>, which only reads. The command is
/// never disabled, so Alt+S always answers: "Loading the Dell BIOS settings, please wait.", or why
/// not now (a read already runs, a change is checked or written, the variables load, a baseline
/// folder is shown). <see cref="Dell"/> holds the region: rows built once and updated in place, the
/// state line and the session cache. The answer is said once and asks for focus on the selected
/// Dell row (a success) or the Status box (no answer) under the two-part quiet rule: when a load,
/// a dialog or the checks of a change started after Alt+S, the answer is taken quietly (no
/// sentence, no focus request; a read that gave no answer leaves one WARN line in the log panel);
/// when the person moved on (F6 to a region, Tab, a click or any other move of keyboard focus, a
/// move in the variable list, typing in the filter box), the sentence is said but focus stays. A baseline
/// folder shows the Dell settings it records, and the live view comes back from the cache, without
/// a read, with the next live load. Copy value (Alt+Y) copies the selected Dell row's sentence, or
/// the state line, while focus is in the Dell region (<see cref="OnDellFocus"/>).
/// </para>
/// </remarks>
public sealed class ShellViewModel : ObservableObject
{
    /// <summary>Suggested file name for Export visible list.</summary>
    internal const string ExportDefaultFileName = "uefiaccess-visible-variables.txt";

    /// <summary>File-type filter for Export visible list: plain text, JSON, or any name.</summary>
    internal const string ExportFilter = "Text files (*.txt)|*.txt|JSON files (*.json)|*.json|All files (*.*)|*.*";

    /// <summary>Status format when the export file could not be written: {0} the failure text.</summary>
    internal const string ExportFailedFormat = "Export failed: {0}";

    /// <summary>Failure text when a source returned no result at all.</summary>
    internal const string NoResultMessage = "the source returned no result";

    /// <summary>The start of Core's restore refusal, which is a whole blocked sentence of its own (<see cref="CheckSentences.RestoreOrderAddsFormat"/>).</summary>
    internal const string RestoreBlockedPrefix = "Restore blocked: ";

    /// <summary>A reason that says this already needs no "Nothing was written." after it.</summary>
    private const string NothingWasWrittenPhrase = "nothing was written";

    /// <summary>The same, in the words of the confirmation's refusals.</summary>
    private const string NothingIsWrittenPhrase = "nothing is written";

    /// <summary>The separator between two sentences that each end with their own period.</summary>
    private const string SentenceSpace = " ";

    /// <summary>The input trace's name for the scheduled update of the detail pane: still "rebuild", so traces from before and after the in-place update compare.</summary>
    internal const string RebuildStep = "rebuild";

    /// <summary>The input trace's name for the update <see cref="EnsureDetailCurrent"/> makes itself when nothing was pending but the pane shows another row: still "direct rebuild".</summary>
    internal const string DirectRebuildStep = "direct rebuild";

    private readonly IVariableSource _liveSource;
    private readonly IWriteService? _writes;
    private readonly Func<string, IVariableSource> _baselineSourceFactory;
    private readonly IAnnouncer _announcer;
    private readonly IFocusSink _focus;
    private readonly IPlatformInteractions _platform;
    private readonly IDetailScheduler _detailScheduler;
    private readonly IDellSettingsSource _dellSource;
    private readonly ILogger _logger;
    private ShellState _state = ShellState.Idle;
    private string _sourceDescription;
    private bool _isOffline;
    private bool _sourceShown;
    private bool _isBusy;
    private string _manufacturer = Label(Strings.ManufacturerLabel, Strings.NotReadYet);
    private string _model = Label(Strings.ModelLabel, Strings.NotReadYet);
    private string _biosVersion = Label(Strings.BiosVersionLabel, Strings.NotReadYet);
    private string _serviceTag = Label(Strings.ServiceTagLabel, Strings.NotReadYet);
    private string _cpu = Label(Strings.CpuLabel, Strings.NotReadYet);
    private string _operatingSystem = Label(Strings.OperatingSystemLabel, Strings.NotReadYet);
    private string _expectedModelText = Label(Strings.ExpectedModelLabel, Strings.NotReadYet);
    private FilterState? _appliedFilterState;
    private int _loadGeneration;
    private CancellationTokenSource? _loadCancellation;
    private bool _dialogOpen;
    private HeldOutcome? _heldOutcome;
    private bool _isWriting;
    private bool _isFinishingChange;
    private bool _checksCancelledByLoad;
    private string? _focusRowKey;
    private IInputTrace _inputTrace = NullInputTrace.Instance;
    private bool _dellQuiet;
    private bool _dellKeepFocus;
    private DellFocusPart _dellFocusPart;

    /// <summary>
    /// Creates the shell over explicit services (the real ones or fakes). The constructor announces
    /// nothing and requests no focus: the window calls <see cref="AnnounceReady"/> once it is on
    /// screen, so the Ready sentence reaches a live region whose automation peer exists. Nothing
    /// is loaded until the user presses Refresh or Open baseline folder.
    /// </summary>
    /// <param name="liveSource">The live firmware source.</param>
    /// <param name="baselineSourceFactory">Creates a baseline source for a folder the user picked.</param>
    /// <param name="announcer">Receives every announcement.</param>
    /// <param name="focus">Receives every focus request.</param>
    /// <param name="platform">Dialogs, the clipboard and the confirmation dialog.</param>
    /// <param name="logger">Optional logger; null disables logging.</param>
    /// <param name="detailScheduler">Decides when the detail pane is updated after a selection change; null means at once (<see cref="ImmediateDetailScheduler"/>).</param>
    /// <param name="writeService">The write pipeline of the live source; null (the default) means the Change group never shows and nothing can be changed.</param>
    /// <param name="dellSource">Where Read Dell settings reads this computer's Dell BIOS settings; null (the default) means <see cref="UnavailableDellSettingsSource"/>, which reads nothing and answers that they cannot be read.</param>
    /// <exception cref="ArgumentNullException">A required service is null.</exception>
    public ShellViewModel(
        IVariableSource liveSource,
        Func<string, IVariableSource> baselineSourceFactory,
        IAnnouncer announcer,
        IFocusSink focus,
        IPlatformInteractions platform,
        ILogger? logger = null,
        IDetailScheduler? detailScheduler = null,
        IWriteService? writeService = null,
        IDellSettingsSource? dellSource = null)
    {
        ArgumentNullException.ThrowIfNull(liveSource);
        ArgumentNullException.ThrowIfNull(baselineSourceFactory);
        ArgumentNullException.ThrowIfNull(announcer);
        ArgumentNullException.ThrowIfNull(focus);
        ArgumentNullException.ThrowIfNull(platform);
        _liveSource = liveSource;
        _baselineSourceFactory = baselineSourceFactory;
        _announcer = announcer;
        _focus = focus;
        _platform = platform;
        _writes = writeService;
        _detailScheduler = detailScheduler ?? new ImmediateDetailScheduler();
        _dellSource = dellSource ?? new UnavailableDellSettingsSource();
        _logger = logger ?? Serilog.Core.Logger.None;
        _sourceDescription = liveSource.Description;

        List = new VariableListViewModel();
        Detail = new VariableDetailViewModel(new ChangeViewModel(writeService, RunChangeAsync, Announce, focus.RequestFocus));
        Log = new LogViewModel();
        Dell = new DellSettingsViewModel();
        List.SelectionChanged += OnSelectionChanged;
        _appliedFilterState = FilterState.Of(List);

        // Open baseline folder and Export visible list open a dialog, so they also wait for the
        // checks of a change: the checks could otherwise answer while that dialog is open.
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy && !IsWriting, OnCommandError);
        OpenBaselineCommand = new AsyncRelayCommand(OpenBaselineFromDialogAsync, () => !IsWriting && !IsFinishingChange && !Detail.Change.IsChecking, OnCommandError);
        ClearFilterCommand = new RelayCommand(ClearFilter, () => !IsWriting && !IsFinishingChange);
        CopyValueCommand = new RelayCommand(CopyValue);
        ExportVisibleListCommand = new RelayCommand(ExportVisibleList, () => !IsWriting && !IsFinishingChange && !Detail.Change.IsChecking);
        CopyLogCommand = new RelayCommand(CopyLog);

        // A RelayCommand without a predicate is always executable: the button is never disabled
        // (a disabled button swallows its access key, and loses focus to WPF while it has it), so
        // Alt+S always answers, also with "still loading" while a read runs.
        ReadDellSettingsCommand = new RelayCommand(() => _ = RunReadDellSettingsAsync());
        Detail.Change.PropertyChanged += OnChangePropertyChanged;
    }

    /// <summary>The current state.</summary>
    public ShellState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(StateText));
            }
        }
    }

    /// <summary>The state as text ("Idle", "Loading", "Loaded", "Refused", "Failed").</summary>
    public string StateText => State switch
    {
        ShellState.Loading => Strings.StateLoading,
        ShellState.Loaded => Strings.StateLoaded,
        ShellState.Refused => Strings.StateRefused,
        ShellState.Failed => Strings.StateFailed,
        _ => Strings.StateIdle,
    };

    /// <summary>The description of the source shown: "Live firmware" or "Baseline (offline): &lt;folder&gt;".</summary>
    public string SourceDescription
    {
        get => _sourceDescription;
        private set
        {
            if (SetProperty(ref _sourceDescription, value))
            {
                OnPropertyChanged(nameof(WindowTitle));
            }
        }
    }

    /// <summary>True while a baseline folder is shown instead of the live machine.</summary>
    public bool IsOffline
    {
        get => _isOffline;
        private set
        {
            if (SetProperty(ref _isOffline, value))
            {
                OnPropertyChanged(nameof(WindowTitle));
            }
        }
    }

    /// <summary>
    /// "UefiAccess" before any source has answered; afterwards "UefiAccess – &lt;source&gt;" for
    /// the source that was asked for last: "UefiAccess – Live firmware" after a live load,
    /// "UefiAccess – Baseline (offline): &lt;folder&gt; (&lt;timestamp&gt;)" while a baseline is
    /// shown. A refused or failed load names its source too, so the title always says which source
    /// the (possibly empty) list belongs to.
    /// </summary>
    public string WindowTitle => _sourceShown ? Strings.WindowTitle + Strings.WindowTitleSeparator + SourceDescription : Strings.WindowTitle;

    /// <summary>The Status box text: mirrors the announcer's current text.</summary>
    public string StatusText => _announcer.Current;

    /// <summary>True while a load runs; Refresh is disabled meanwhile (Open baseline folder is not: a folder picked replaces the running load).</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                (RefreshCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// True while a confirmed change is written (stages S11 to S17).
    /// Refresh, Open baseline folder, Clear filter, Export visible list, the filter
    /// (<see cref="IsFilterEnabled"/>) and every Change command are disabled meanwhile, and
    /// <see cref="TryClose"/> keeps the window open. The write is never cancelled and never replaced.
    /// </summary>
    public bool IsWriting
    {
        get => _isWriting;
        private set
        {
            if (SetProperty(ref _isWriting, value))
            {
                OnChangeLockChanged();
            }
        }
    }

    /// <summary>
    /// True from the moment a confirmed change is committed until its result has been said: through
    /// the write (<see cref="IsWriting"/>) and the live reload after it, which says nothing until it
    /// ends (the machine identity alone may take up to a minute), so Status keeps "Writing the
    /// change, please wait. Do not restart the machine." meanwhile. Open baseline folder, Clear
    /// filter, Export visible list, the filter (<see cref="IsFilterEnabled"/>) and every Change
    /// command stay disabled until then, so nothing can start before the change's result is said
    /// (Refresh is disabled by the reload itself). It holds no sentence (a load's outcome is held
    /// only while <see cref="IsWriting"/>), and the window may close once the commit has returned.
    /// </summary>
    public bool IsFinishingChange
    {
        get => _isFinishingChange;
        private set
        {
            if (SetProperty(ref _isFinishingChange, value))
            {
                OnChangeLockChanged();
            }
        }
    }

    /// <summary>False while a change is written or its result is still being read back (<see cref="IsFinishingChange"/>): the filter box, Group by and Sort by bind their IsEnabled to it.</summary>
    public bool IsFilterEnabled => !IsWriting && !IsFinishingChange;

    /// <summary>
    /// The key of the row a <see cref="FocusTarget.ListRowByKey"/> request names: the variable a
    /// change was just written to, after the live reload. Null before the first such request.
    /// </summary>
    public string? FocusRowKey
    {
        get => _focusRowKey;
        private set => SetProperty(ref _focusRowKey, value);
    }

    /// <summary>"Manufacturer: &lt;value&gt;", ready for AutomationProperties.Name.</summary>
    public string Manufacturer
    {
        get => _manufacturer;
        private set => SetProperty(ref _manufacturer, value);
    }

    /// <summary>"Model: &lt;value&gt;".</summary>
    public string Model
    {
        get => _model;
        private set => SetProperty(ref _model, value);
    }

    /// <summary>"BIOS version: &lt;value&gt;".</summary>
    public string BiosVersion
    {
        get => _biosVersion;
        private set => SetProperty(ref _biosVersion, value);
    }

    /// <summary>"Service tag: &lt;value&gt;".</summary>
    public string ServiceTag
    {
        get => _serviceTag;
        private set => SetProperty(ref _serviceTag, value);
    }

    /// <summary>"CPU: &lt;value&gt;".</summary>
    public string Cpu
    {
        get => _cpu;
        private set => SetProperty(ref _cpu, value);
    }

    /// <summary>"Operating system: &lt;caption version (build N)&gt;".</summary>
    public string OperatingSystem
    {
        get => _operatingSystem;
        private set => SetProperty(ref _operatingSystem, value);
    }

    /// <summary>"Expected model confirmed: Yes" or "Expected model confirmed: No"; the reasoning behind the answer is one log line.</summary>
    public string ExpectedModelText
    {
        get => _expectedModelText;
        private set => SetProperty(ref _expectedModelText, value);
    }

    /// <summary>The variable list.</summary>
    public VariableListViewModel List { get; }

    /// <summary>The detail pane.</summary>
    public VariableDetailViewModel Detail { get; }

    /// <summary>The log panel.</summary>
    public LogViewModel Log { get; }

    /// <summary>The Dell settings region: its rows, its state line and the session cache.</summary>
    public DellSettingsViewModel Dell { get; }

    /// <summary>
    /// Read Dell settings (Alt+S): reads this computer's Dell BIOS settings (<see cref="ReadDellSettingsAsync"/>).
    /// Never disabled: while it cannot read, it says why.
    /// </summary>
    public ICommand ReadDellSettingsCommand { get; }

    /// <summary>Refresh (Alt+R): reloads the live source. Disabled while a load runs or a change is written.</summary>
    public ICommand RefreshCommand { get; }

    /// <summary>Open baseline folder (Alt+O): picks a folder and loads it offline, replacing a load that is still running. Disabled while its own dialog or load runs, while the checks of a change run, and while a change is written until its result is said.</summary>
    public ICommand OpenBaselineCommand { get; }

    /// <summary>Clear filter (Alt+C or the button): empties the filter and announces the count; focus stays where it is (Escape in the filter box is the view's own move to the list). Disabled while a change is written until its result is said.</summary>
    public ICommand ClearFilterCommand { get; }

    /// <summary>Copy value (Alt+Y): copies the detail pane's <see cref="VariableDetailViewModel.CopyValueText"/>, else the selected row's summary, else its key; while focus is in the Dell region (<see cref="OnDellFocus"/>), the selected Dell row's sentence, or the state line's text when the state line has focus.</summary>
    public ICommand CopyValueCommand { get; }

    /// <summary>Export visible list (Alt+X): writes every row that passes the filter when it is pressed, rows in collapsed groups included, to a file the user picks (JSON for a .json name, plain text otherwise). Disabled while the checks of a change run, and while a change is written until its result is said.</summary>
    public ICommand ExportVisibleListCommand { get; }

    /// <summary>Copy log (Alt+L): copies every log line.</summary>
    public ICommand CopyLogCommand { get; }

    /// <summary>
    /// The input trace (<see cref="IInputTrace"/>): <see cref="NullInputTrace.Instance"/> unless the
    /// application set it, once at start-up, because it was started with --trace-input. A null
    /// value sets <see cref="NullInputTrace.Instance"/>.
    /// </summary>
    public IInputTrace InputTrace
    {
        get => _inputTrace;
        set => _inputTrace = value ?? NullInputTrace.Instance;
    }

    /// <summary>
    /// Composes the production shell: the Windows live source with its write pipeline (composed
    /// lazily, on the first change the person asks for), baseline folder sources, the Dell settings
    /// source of this computer (<see cref="LiveDellSettingsSource"/>, which reads nothing until Read
    /// Dell settings is pressed), and the given platform services (the WPF window supplies its
    /// announcer, focus sink and dialogs).
    /// </summary>
    /// <param name="announcer">Receives every announcement.</param>
    /// <param name="focus">Receives every focus request.</param>
    /// <param name="platform">Dialogs, the clipboard and the confirmation dialog.</param>
    /// <param name="logger">Optional logger; null disables logging.</param>
    /// <param name="detailScheduler">Decides when the detail pane is updated after a selection change (the window supplies its dispatcher scheduler); null means at once.</param>
    /// <returns>A ready-to-use shell.</returns>
    public static ShellViewModel CreateWindows(IAnnouncer announcer, IFocusSink focus, IPlatformInteractions platform, ILogger? logger, IDetailScheduler? detailScheduler = null)
    {
        ILogger log = logger ?? Serilog.Core.Logger.None;
        LiveFirmwareSource live = LiveFirmwareSource.CreateWindows(log);
        return new ShellViewModel(
            live,
            folder => new BaselineFolderSource(folder, log),
            announcer,
            focus,
            platform,
            log,
            detailScheduler,
            live.Writes,
            LiveDellSettingsSource.CreateWindows(log));
    }

    /// <summary>
    /// Announces <see cref="Strings.IdleStatus"/>, the Ready sentence, so the Status box and the
    /// log start with it. The window calls this once from its Loaded handler, after the window is
    /// on screen and the Status box has an automation peer to raise the live-region event on; it
    /// is the one-off consequence of the user opening the application, not a timed event, and it
    /// requests no focus (the window puts the initial focus on the first toolbar button itself).
    /// </summary>
    public void AnnounceReady()
    {
        _logger.Debug("Window loaded; announcing the Ready sentence");
        Announce(Strings.IdleStatus);
    }

    /// <summary>
    /// Loads the live source: Loading, then Loaded, Refused or Failed with the matching announcement
    /// and focus request. Never throws. Ignored (with a log line) while another load runs.
    /// </summary>
    /// <returns>A task that completes when the load has finished.</returns>
    public Task RefreshAsync()
    {
        if (IsBusy)
        {
            _logger.Information("Load of {Source} ignored: another load is running", _liveSource.Description);
            return Task.CompletedTask;
        }

        if (IsWriting)
        {
            _logger.Information("Load of {Source} ignored: a change is being written", _liveSource.Description);
            return Task.CompletedTask;
        }

        return LoadAsync(_liveSource);
    }

    /// <summary>
    /// Loads a baseline folder offline through the baseline source factory: Loading, then Loaded,
    /// Refused or Failed with the matching announcement and focus request. Never throws. A load
    /// that is still running is replaced: it is cancelled and its result, when it arrives, is
    /// dropped without being shown or announced.
    /// </summary>
    /// <param name="folder">The baseline folder.</param>
    /// <returns>A task that completes when the load has finished.</returns>
    public async Task OpenBaselineAsync(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            _logger.Debug("OpenBaselineAsync ignored: no folder");
            return;
        }

        if (IsWriting)
        {
            _logger.Information("Open baseline folder ignored: a change is being written");
            return;
        }

        IVariableSource source;
        try
        {
            source = _baselineSourceFactory(folder) ?? throw new InvalidOperationException("The baseline source factory returned null for " + folder);
        }
        catch (Exception ex)
        {
            QuietDellAnswer("a baseline folder load started");
            ReplaceRunningLoad();
            string description = Strings.BaselinePrefix + folder;
            ShowResult(
                new SourceResult(
                    SourceStatus.Failed,
                    description,
                    true,
                    FirmwareType.Unknown,
                    null,
                    Array.Empty<FirmwareVariable>(),
                    EnumerationMethod.None,
                    Array.Empty<string>(),
                    null,
                    ex.Message),
                description,
                ex);
            return;
        }

        await LoadAsync(source).ConfigureAwait(true);
    }

    /// <summary>
    /// F6 or Shift+F6 arrived in a region: announces "&lt;Region&gt; region", except for the Status
    /// region, where nothing is announced so the Status box keeps the last message and focus
    /// landing on it reads that message (an announcement would overwrite it with "Status region").
    /// The Dell settings region adds its clause where it changes what the rows mean
    /// (<see cref="DellSettingsViewModel.ArrivalClause"/>): "Dell settings region, from baseline
    /// folder &lt;name&gt;" or "Dell settings region, could not be loaded"; otherwise it is the bare
    /// "Dell settings region". No focus request; the view already moved focus. A Dell read in flight
    /// then asks for no focus when it answers: the person has moved on.
    /// </summary>
    /// <param name="region">The region that received focus.</param>
    public void OnRegionArrived(Region region)
    {
        KeepFocusFromDellAnswer("the person moved to another region");
        if (region == Region.Status)
        {
            _logger.Debug("Arrived in the Status region; the Status box keeps its text");
            return;
        }

        if (region == Region.Dell && Dell.ArrivalClause is string clause)
        {
            Announce(string.Format(CultureInfo.InvariantCulture, Strings.DellRegionArrivedFormat, RegionOrder.Name(region), clause));
            return;
        }

        Announce(string.Format(CultureInfo.InvariantCulture, Strings.RegionArrivedFormat, RegionOrder.Name(region)));
    }

    /// <summary>
    /// Keyboard focus moved in the window, by whatever path: Tab or Shift+Tab into another control
    /// or region, a click, an arrow key that moves focus, a screen reader's focus request, a context
    /// menu opened, a switch to another window. The window calls this for every move (a Dell answer never takes focus away from where the person went, and
    /// never moves focus while the person is reading), so a Dell read in flight then asks for no
    /// focus when it answers; its sentence is still said once. Every move while a read runs is the
    /// person's own, or follows a load, a dialog or the checks of a change, which already make the
    /// answer quiet; the answer's own focus move comes after the read ended, so it does not count.
    /// Nothing is said, no focus is requested, and nothing is logged above Debug.
    /// </summary>
    public void OnKeyboardFocusMoved()
    {
        KeepFocusFromDellAnswer("keyboard focus moved");
    }

    /// <summary>
    /// The view applied the filter, grouping or sort order: announces "Showing N of M variables"
    /// when the filter text, the visible count or the total count differs from the list the view
    /// applied last; focus stays where it is (no request). A change of Group by or Sort by alone
    /// changes none of them and says nothing (Alpha 2 finding A2-9, the tester's A2-7): the combo box
    /// already speaks the new choice, and the count it would repeat is the one heard before. Every
    /// call is remembered, whatever it said, so the next filter change is judged against the
    /// grouping and sort order in effect then. What counts is the list the view applied, not what
    /// was announced last: another announcement in between does not make the same count news
    /// again. A load remembers the list it shows (its sentence gives the count), and
    /// <see cref="ClearFilterCommand"/> forgets it, so every press of Clear filter says the count
    /// once, also when the view's filter box echoes the emptied text back through this method.
    /// A Dell read in flight then asks for no focus when it answers: the person is typing.
    /// </summary>
    public void OnFilterApplied()
    {
        KeepFocusFromDellAnswer("the filter, grouping or sort order was applied");
        FilterState state = FilterState.Of(List);
        FilterState? applied = _appliedFilterState;
        _appliedFilterState = state;
        if (applied is not null && applied.ShowsTheSameRows(state))
        {
            _logger.Debug("Filter announcement skipped: {State} shows the rows {Applied} showed", state, applied);
            return;
        }

        Announce(List.ShowingText);
    }

    /// <summary>
    /// A splitter moved: after every arrow press that moved it, once at the end of a drag that
    /// changed its share, and after an arrow press that could not move it further that way (Alpha 2
    /// finding A2-8, the tester's A2-6, which also answers the developer's A2-5). Says its position as a
    /// sentence that names both panes, "List 63 percent of the width, details 37 percent." or "Log
    /// panel 30 percent of the height, list and details 70 percent.", with "Limit reached: " before
    /// it after a press at a limit (<see cref="SplitterPosition.Announcement"/>), through the
    /// <see cref="IAnnouncer"/>: the Status box is the one live region NVDA and Narrator both speak
    /// (A2-2), and the splitter raises no value change of its own, so the sentence is heard once.
    /// Unlike every other announcement it is not written to the log panel, only logged at Debug, so
    /// twenty presses do not fill the panel. No focus request: focus stays on the splitter.
    /// </summary>
    /// <param name="kind">Which splitter moved.</param>
    /// <param name="share">The share, in whole percent, of the pane the splitter speaks for (the list's, or the log panel's).</param>
    /// <param name="atLimit">True when an arrow press could not move the splitter.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a defined splitter.</exception>
    public void OnSplitterMoved(SplitterKind kind, int share, bool atLimit)
    {
        string sentence = SplitterPosition.Announcement(kind, share, atLimit);
        _logger.Debug("Splitter {Kind}: {Sentence}", kind, sentence);
        _announcer.Announce(sentence);
        OnPropertyChanged(nameof(StatusText));
    }

    /// <summary>
    /// Ctrl+F could not put focus in the filter box. While a change is written or its result is
    /// still being read back the box is disabled (<see cref="IsFilterEnabled"/>), and Status says
    /// "The filter is unavailable while a change is written; wait for its result." instead of
    /// nothing; focus stays where it is (no request). Any other time nothing is said.
    /// </summary>
    public void OnFilterUnavailable()
    {
        if (IsFilterEnabled)
        {
            _logger.Debug("Ctrl+F could not focus the filter box, which is enabled");
            return;
        }

        Announce(Strings.FilterUnavailableStatus);
    }

    /// <summary>
    /// Ctrl+C on a list row: copies the row's key ("&lt;guid&gt;:&lt;name&gt;") to the clipboard.
    /// Success is silent, like Ctrl+C in any Windows list (the row keeps focus and the key is on
    /// the clipboard); a failure is announced as "Copy failed: &lt;reason&gt;" so it cannot be
    /// mistaken for a successful copy, and logged once at Error. Never throws.
    /// </summary>
    /// <param name="item">The row whose key is copied; null does nothing.</param>
    public void CopyKey(VariableItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            _platform.SetClipboardText(item.Key);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Copy key failed");
            Announce(string.Format(CultureInfo.InvariantCulture, Strings.CopyFailedFormat, ex.Message));
            return;
        }

        _logger.Debug("Copied the key of {Key} to the clipboard", item.Key);
    }

    /// <summary>
    /// Makes the detail pane show the selected row now. A selection change only schedules the
    /// update of the pane (see the class remarks); the view calls this before it moves keyboard
    /// focus into the pane (Enter or Space on a row, F6 to the Detail region), so the first field
    /// focus lands on belongs to the selected row, and before an access key, the Change group's among
    /// them, can act (Alt going down, anywhere in the window); the shell calls it itself before Copy value,
    /// Export visible list and every Change command, and after every load. Runs the pending
    /// refresh when there is one; otherwise, when the pane already shows the selected row, nothing
    /// happens. No announcement, no focus request. Never throws. With the input trace on, the
    /// request is traced with the method that made it (<paramref name="caller"/>, filled in by the
    /// compiler, so no call site names it) and what that method stands for: an Alt key, Enter,
    /// Space or F6 into the pane, any focus move into or within the pane, Copy value, Export
    /// visible list, a Change command or a load. A call for a move into the pane (the window's
    /// FocusFirstDetailField or OnDetailPreviewGotKeyboardFocus) also means the person moved on, so
    /// a Dell read in flight asks for no focus when it answers.
    /// </summary>
    /// <param name="caller">The calling method; left to the compiler.</param>
    public void EnsureDetailCurrent([CallerMemberName] string caller = "")
    {
        if (_inputTrace.IsEnabled)
        {
            _inputTrace.Write(TraceKind.Detail, "flush requested by " + FlushRequester(caller));
        }

        _detailScheduler.Flush();
        if (!ReferenceEquals(Detail.Item, List.SelectedItem))
        {
            ShowSelectedRow(DirectRebuildStep);
        }

        if (IsDetailPaneArrival(caller))
        {
            KeepFocusFromDellAnswer("focus moved into the detail pane");
        }
    }

    /// <summary>
    /// The window is asked to close (its Closing handler calls this), or Windows is ending the
    /// session (the application's SessionEnding handler calls this, because WPF does not honour a
    /// cancelled Closing then): true lets it close; while a change is written (<see cref="IsWriting"/>)
    /// the answer is false, the window stays open (the session end is cancelled) and Status says "A
    /// change is being written; wait for its result." Focus stays where it is.
    /// </summary>
    /// <returns>True when the window may close now.</returns>
    public bool TryClose()
    {
        if (!IsWriting)
        {
            return true;
        }

        _logger.Debug("Closing refused: a change is being written");
        Announce(Strings.ChangeBeingWrittenStatus);
        return false;
    }

    /// <summary>
    /// Read Dell settings (Alt+S): reads this computer's Dell
    /// BIOS settings through the <see cref="IDellSettingsSource"/>, which only reads, and shows them
    /// in <see cref="Dell"/>. Refused with one sentence, and focus left where it is, while a Dell
    /// read runs ("The Dell BIOS settings are still loading; please wait."), while a change is
    /// checked or written or its result is not said yet, while the variables load, and while a
    /// baseline folder is shown, in that order. Otherwise it says "Loading the Dell BIOS settings,
    /// please wait." and leaves focus where it is. The answer replaces the session cache when it
    /// answered, updates the rows in place, and is said once: "Loaded 10 Dell BIOS settings from
    /// Dell Command Monitor." (or the partial sentence, followed by the fallback sentence when Dell
    /// Command Configure was used instead), "Loaded the Dell BIOS settings again: nothing
    /// changed.", "Loaded the Dell BIOS settings again; changed: ...", or the problem sentence
    /// (followed by "The list still shows the values loaded at ..." when earlier values are kept),
    /// with a request for focus on the selected Dell row after a success and on the Status box
    /// otherwise; each warning of the result is one WARN line. When a load, a dialog or the checks of
    /// a change started meanwhile, the answer is taken quietly (nothing said, no focus request; one
    /// WARN line with the problem sentence when nothing answered), and when the person moved on, it
    /// is said without a focus request. Never throws for what the source does: a read that throws is
    /// answered as one that could not be made.
    /// </summary>
    /// <returns>A task that completes when the read was refused or its answer was taken.</returns>
    public async Task ReadDellSettingsAsync()
    {
        string? refusal = Dell.IsLoading ? Strings.DellAlreadyLoadingStatus
            : IsWriting || IsFinishingChange || Detail.Change.IsChecking ? Strings.DellWaitForChangeStatus
            : IsBusy ? Strings.DellWaitForVariablesStatus
            : Dell.Mode == DellViewMode.Baseline ? Strings.DellBaselineShownStatus
            : null;
        if (refusal is not null)
        {
            _logger.Debug("Read Dell settings refused: {Sentence}", refusal);
            Announce(refusal);
            return;
        }

        _dellQuiet = false;
        _dellKeepFocus = false;
        Dell.IsLoading = true;
        _logger.Debug("Reading the Dell BIOS settings");
        Announce(Strings.DellLoadingStatus);
        DellSettingsResult result;
        try
        {
            result = await _dellSource.ReadAsync().ConfigureAwait(true) ?? UnavailableDellSettingsSource.Unavailable(NoResultMessage);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Reading the Dell BIOS settings failed");
            result = UnavailableDellSettingsSource.Unavailable(ex.Message);
        }
        finally
        {
            Dell.IsLoading = false;
        }

        ShowDellAnswer(result);
    }

    /// <summary>
    /// Ctrl+C on a Dell row: copies the row's sentence (<see cref="DellSettingRowViewModel.AccessibleName"/>)
    /// to the clipboard. Success is silent, as Ctrl+C on a variable row; a failure is announced as
    /// "Copy failed: &lt;reason&gt;" and logged once at Error. Never throws.
    /// </summary>
    /// <param name="row">The row whose sentence is copied; null does nothing.</param>
    public void CopyDellRow(DellSettingRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        try
        {
            _platform.SetClipboardText(row.AccessibleName);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Copy of a Dell row failed");
            Announce(string.Format(CultureInfo.InvariantCulture, Strings.CopyFailedFormat, ex.Message));
            return;
        }

        _logger.Debug("Copied the sentence of the Dell row {Id} to the clipboard", row.Id);
    }

    /// <summary>
    /// The window tells which part of the Dell settings region has keyboard focus, or
    /// <see cref="DellFocusPart.None"/> when focus left it: while it is not
    /// None, Copy value (Alt+Y) copies the selected Dell row's sentence, or the state line's text
    /// when the state line has focus, never a firmware variable's value. A move while a Dell read is
    /// in flight means the person moved on, so its answer asks for no focus. Nothing is said.
    /// </summary>
    /// <param name="part">The part that has focus now.</param>
    public void OnDellFocus(DellFocusPart part)
    {
        if (part != _dellFocusPart)
        {
            KeepFocusFromDellAnswer("focus moved within or out of the Dell settings region");
        }

        _dellFocusPart = part;
    }

    /// <summary>
    /// The Status sentence of a refusal: "Change blocked: &lt;reason&gt; Nothing was written." for
    /// the pipeline's bare reason. A reason that already says
    /// nothing was or is written gets the prefix alone, and one that is a blocked sentence of its
    /// own (Core's restore refusal) is said as it is, so neither the prefix nor the closing sentence
    /// is said twice; a sentence that ends by saying BitLocker stays suspended keeps that ending last.
    /// </summary>
    /// <param name="reason">The pipeline's sentence of the refusal.</param>
    /// <returns>The Status sentence.</returns>
    internal static string BlockedSentence(string reason)
    {
        string text = (reason ?? string.Empty).Trim();
        string suspended = string.Empty;
        if (text.EndsWith(CheckSentences.BitLockerLeftSuspended, StringComparison.Ordinal))
        {
            text = text[..^CheckSentences.BitLockerLeftSuspended.Length].TrimEnd();
            suspended = SentenceSpace + CheckSentences.BitLockerLeftSuspended;
        }

        string sentence;
        if (text.StartsWith(Strings.ChangeBlockedPrefix, StringComparison.Ordinal) || text.StartsWith(RestoreBlockedPrefix, StringComparison.Ordinal))
        {
            sentence = text;
        }
        else if (text.Contains(NothingWasWrittenPhrase, StringComparison.OrdinalIgnoreCase) || text.Contains(NothingIsWrittenPhrase, StringComparison.OrdinalIgnoreCase))
        {
            sentence = Strings.ChangeBlockedPrefix + text;
        }
        else
        {
            sentence = string.Format(CultureInfo.InvariantCulture, Strings.ChangeBlockedFormat, WithClosingPeriod(text));
        }

        return sentence + suspended;
    }

    // ---- loading -----------------------------------------------------------------------------

    private async Task LoadAsync(IVariableSource source, WriteFollowUp? followUp = null)
    {
        QuietDellAnswer("a load started");
        ReplaceRunningLoad();
        int generation = _loadGeneration;
        using CancellationTokenSource cancellation = new();
        _loadCancellation = cancellation;
        IsBusy = true;
        try
        {
            State = ShellState.Loading;
            _logger.Debug("Loading {Source}", source.Description);

            // The reload after a write says nothing until it is done: the write's sentence and the
            // Loaded sentence are then one announcement.
            if (followUp is null)
            {
                Announce(Strings.LoadingStatus);
            }

            SourceResult? result;
            Exception? exception = null;
            try
            {
                result = await source.LoadAsync(cancellation.Token).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                exception = ex;
                result = FailedResult(source, ex.Message);
            }

            if (generation != _loadGeneration)
            {
                // A later load replaced this one while it ran; that load shows its own result. The
                // write this reload followed is still said: its outcome is never dropped.
                _logger.Debug("The result of {Source} was dropped: a later load replaced it", source.Description);
                if (followUp is not null)
                {
                    Announce(followUp.Sentence);
                }

                return;
            }

            result ??= FailedResult(source, NoResultMessage);
            ShowResult(result, source.Description, exception, followUp);
        }
        catch (Exception ex) when (generation == _loadGeneration)
        {
            // Showing a result must not leave the shell stuck in Loading; report and recover.
            _logger.Error(ex, "Showing the result of {Source} threw", source.Description);
            State = ShellState.Failed;
            IsBusy = false;
            Announce(Lead(followUp?.Sentence, string.Format(CultureInfo.InvariantCulture, Strings.FailedFormat, ex.Message)));
            _focus.RequestFocus(FocusTarget.Status);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "A replaced load of {Source} threw", source.Description);
        }
        finally
        {
            if (generation == _loadGeneration)
            {
                _loadCancellation = null;
                IsBusy = false;
            }
        }
    }

    /// <summary>
    /// The disabling of the toolbar commands, the filter and the Change group follows
    /// <see cref="IsWriting"/> and <see cref="IsFinishingChange"/>: both are re-queried whenever
    /// either changes.
    /// </summary>
    private void OnChangeLockChanged()
    {
        OnPropertyChanged(nameof(IsFilterEnabled));
        Detail.Change.IsWriting = IsWriting || IsFinishingChange;
        RaiseToolbarCanExecuteChanged();
    }

    /// <summary>Open baseline folder and Export visible list are disabled while the checks of a change run: they are re-queried when the checks start and end.</summary>
    private void OnChangePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChangeViewModel.IsChecking))
        {
            RaiseToolbarCanExecuteChanged();
        }
    }

    private void RaiseToolbarCanExecuteChanged()
    {
        (RefreshCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (OpenBaselineCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ClearFilterCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ExportVisibleListCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Makes the load that is running, when there is one, a replaced load: it is cancelled, and
    /// when its result arrives it is dropped, and it no longer ends the busy state.
    /// </summary>
    private void ReplaceRunningLoad()
    {
        _loadGeneration++;
        CancellationTokenSource? running = _loadCancellation;
        _loadCancellation = null;
        if (running is not null)
        {
            _logger.Debug("A new load replaces the one that is running");
            running.Cancel();
        }
    }

    /// <summary>
    /// Shows a source result in every observable part of the shell, announces it and requests focus.
    /// The reload after a write starts its sentence with the write's and asks for the focus the
    /// write's outcome sets. A load that drops a proposed order the firmware does not hold (it now
    /// reads another order, or nothing was read and the order was not the one just written) says
    /// "Proposed order discarded." before its own sentence, after the write's when it follows one:
    /// after a one-time boot, a clear, a restore, or a reorder the firmware refused or that could
    /// not be verified, as after Refresh (docs/Keyboard-Map.md section 7). A load that replaces the
    /// list while the checks of a change run cancels them (Escape's cancel), because what they were
    /// started from is gone (the proposed order is dropped, or a baseline is shown): its sentence
    /// starts with "Cancelled; nothing changed.", and the plan that may still arrive opens no dialog
    /// (<see cref="RunChangeAsync"/>).
    /// </summary>
    /// <param name="result">The result.</param>
    /// <param name="fallbackDescription">The source description used when the result carries none.</param>
    /// <param name="exception">The exception behind a failed result, when there is one; logged once with the failure.</param>
    /// <param name="followUp">The write this load follows, or null for any other load.</param>
    private void ShowResult(SourceResult result, string fallbackDescription, Exception? exception = null, WriteFollowUp? followUp = null)
    {
        // The input trace replaces this identity's service tag (a baseline names its own unit's)
        // before any row of this load can reach a trace line.
        _inputTrace.Redact(result.Identity?.ServiceTag);
        IReadOnlyList<FirmwareVariable> variables = result.Variables ?? Array.Empty<FirmwareVariable>();
        IReadOnlyList<string> warnings = result.Warnings ?? Array.Empty<string>();

        // False when no checks run, or Escape has cancelled them already (it says so itself).
        bool checksCancelled = Detail.Change.CancelChecks();
        if (checksCancelled)
        {
            _checksCancelledByLoad = true;
            _logger.Debug("A load replaced the list while the checks of a change ran; the checks are cancelled");
        }

        // The proposed order this load replaces, when one was arranged; whether it is dropped is
        // known once the new content is loaded (a reorder just written and verified is not).
        ushort[]? proposed = Detail.Change.Editor.HasChanges ? Detail.Change.Editor.Entries.Select(entry => entry.Number).ToArray() : null;

        SourceDescription = string.IsNullOrWhiteSpace(result.SourceDescription) ? fallbackDescription : result.SourceDescription;
        IsOffline = result.IsOffline;
        if (!_sourceShown)
        {
            // From the first answer on, the title names the source (docs/Keyboard-Map.md section 1).
            _sourceShown = true;
            OnPropertyChanged(nameof(WindowTitle));
        }

        foreach (string warning in warnings)
        {
            if (!string.IsNullOrWhiteSpace(warning))
            {
                _logger.Debug("Source warning: {Warning}", warning);
                Log.Warn(warning.Trim());
            }
        }

        string? lead;
        if (result.Status == SourceStatus.Loaded)
        {
            // Every row is judged once by the writability policy: offline for a baseline, and on the
            // live source against the identity and the unit check this load read (the M3 test unit,
            // the contracted model on A18 with the enabled machine ID, or not).
            bool live = !result.IsOffline;
            List.Load(variables, result.IsOffline, live && PlatformPolicy.IsContractedMachine(result.Identity) && result.UnitConfirmed);
            _appliedFilterState = FilterState.Of(List);
            Detail.Change.Load(List.Variables, live);

            // The Dell rows switch in place: to the baseline's own file, or back to the session
            // cache, without a read and without a sentence of their own.
            if (live)
            {
                Dell.ShowLive();
            }
            else
            {
                Dell.ShowBaseline(result.DellBaseline, result.BaselineFolderName ?? string.Empty);
            }

            // Dropped unless the firmware now holds exactly the proposed order.
            lead = LeadOf(followUp, proposed is not null && !proposed.SequenceEqual(Detail.Change.Editor.FirmwareOrder), checksCancelled);
            FocusTarget focus = FocusTargetAfter(followUp);
            EnsureDetailCurrent();
            ShowIdentity(result.Identity, Strings.Unknown);
            State = ShellState.Loaded;
            IsBusy = false;
            _logger.Debug("Loaded {Count} variables from {Source} via {Method}", variables.Count, SourceDescription, result.Method);
            string loaded = string.Format(CultureInfo.InvariantCulture, Strings.LoadedFormat, Strings.Variables(variables.Count), SourceDescription, result.Method);

            // A filter in effect when the load was started (white space alone is no filter) still
            // applies to the new content: the user hears the total and the visible count in one
            // sentence, and the list counts as applied (above), so the view's echo does not repeat it.
            AnnounceOutcome(Lead(lead, List.IsFiltered ? loaded + Strings.SentenceSeparator + List.ShowingText : loaded), focus);
            return;
        }

        // Refused or Failed: nothing from this source is shown, so the previous content goes too
        // (the title now names the source that was asked for, and an empty list cannot be mistaken
        // for data from it). The Dell region shows no baseline's settings and never live ones
        // beside a baseline that was asked for; after a live load it shows the session cache.
        List.Load(Array.Empty<FirmwareVariable>());
        _appliedFilterState = FilterState.Of(List);
        Detail.Change.Load(null, live: false);
        if (result.IsOffline)
        {
            Dell.ShowBaselineNotLoaded();
        }
        else
        {
            Dell.ShowLive();
        }

        EnsureDetailCurrent();
        ShowIdentity(null, Strings.NotReadYet);
        string? message = string.IsNullOrWhiteSpace(result.Message) ? null : result.Message.Trim();

        // Nothing was read, so what the firmware holds is not known: the proposed order counts as
        // dropped unless the write this load follows wrote it and read it back.
        lead = LeadOf(followUp, proposed is not null && followUp?.ProposedOrderWritten != true, checksCancelled);

        if (result.Status == SourceStatus.Refused)
        {
            State = ShellState.Refused;
            IsBusy = false;
            _logger.Debug("Refused: {Message}", message ?? Strings.StateRefused);
            AnnounceOutcome(Lead(lead, message ?? Strings.StateRefused), FocusTarget.Status);
        }
        else
        {
            State = ShellState.Failed;
            IsBusy = false;
            _logger.Error(exception, "Failed: {Message:l}", message ?? Strings.StateFailed);
            AnnounceOutcome(Lead(lead, string.Format(CultureInfo.InvariantCulture, Strings.FailedFormat, message ?? Strings.StateFailed)), FocusTarget.Status);
        }
    }

    /// <summary>
    /// Where a successful load sends focus: the first row, except after a verified change, whose
    /// variable's row is selected and focused (<see cref="FocusTarget.ListRowByKey"/>, or the first
    /// row when no visible row has its key), and after any other write, which sends it to Status.
    /// </summary>
    private FocusTarget FocusTargetAfter(WriteFollowUp? followUp)
    {
        if (followUp is null)
        {
            return FocusTarget.ListFirstRow;
        }

        if (followUp.Focus != FocusTarget.ListRowByKey || followUp.RowKey is not string key)
        {
            return followUp.Focus;
        }

        FocusRowKey = key;
        return List.TrySelectByKey(key) ? FocusTarget.ListRowByKey : FocusTarget.ListFirstRow;
    }

    /// <summary>
    /// Announces the sentence of a load's outcome and requests its focus target; while a dialog
    /// is open (its message loop ran the load's completion) both wait for the dialog to close,
    /// where <see cref="AnnounceAfterDialog"/> delivers them (see the class remarks). While a change
    /// is written (a load started during the checks finished during the write) both wait too, so
    /// Status keeps "Writing the change, please wait. Do not restart the machine." and focus stays
    /// on the Status box until the result is in: the write then says the
    /// held sentence after its own when the firmware was not called, and drops it when the live
    /// reload after a write call replaces that load.
    /// </summary>
    /// <param name="sentence">The Loaded, refusal or Loading failed sentence.</param>
    /// <param name="focus">Where the outcome sends focus.</param>
    private void AnnounceOutcome(string sentence, FocusTarget focus)
    {
        if (_dialogOpen || IsWriting)
        {
            // A change refused behind the same dialog stays in front of the load's sentence.
            _logger.Debug("A load finished while a dialog was open or a change was written; its sentence and focus request wait");
            string? change = _heldOutcome?.ChangeSentence;
            _heldOutcome = new HeldOutcome(Lead(change, sentence), focus, change);
            return;
        }

        _heldOutcome = null;
        Announce(sentence);
        _focus.RequestFocus(focus);
    }

    /// <summary>Formats the identity strings; a null identity shows <paramref name="placeholder"/> in every field.</summary>
    private void ShowIdentity(MachineIdentityResult? identity, string placeholder)
    {
        if (identity is null)
        {
            Manufacturer = Label(Strings.ManufacturerLabel, placeholder);
            Model = Label(Strings.ModelLabel, placeholder);
            BiosVersion = Label(Strings.BiosVersionLabel, placeholder);
            ServiceTag = Label(Strings.ServiceTagLabel, placeholder);
            Cpu = Label(Strings.CpuLabel, placeholder);
            OperatingSystem = Label(Strings.OperatingSystemLabel, placeholder);
            ExpectedModelText = Label(Strings.ExpectedModelLabel, placeholder);
            return;
        }

        Manufacturer = Label(Strings.ManufacturerLabel, Text(identity.Manufacturer));
        Model = Label(Strings.ModelLabel, Text(identity.Model));
        BiosVersion = Label(Strings.BiosVersionLabel, Text(identity.BiosVersion));
        ServiceTag = Label(Strings.ServiceTagLabel, Text(identity.ServiceTag));
        Cpu = Label(Strings.CpuLabel, Text(identity.Cpu));
        OperatingSystem = Label(Strings.OperatingSystemLabel, FormatOs(identity));
        ExpectedModelText = Label(Strings.ExpectedModelLabel, identity.ExpectedModelConfirmed ? Strings.YesText : Strings.NoText);

        // The strip says Yes or No; the reasoning (which signals matched, and the relaunch warning
        // when the unit looks like the 2024 OptiPlex 7020) is one log line, copyable with the log.
        if (!string.IsNullOrWhiteSpace(identity.ExpectedModelNotes))
        {
            string notes = identity.ExpectedModelNotes.Trim();
            if (identity.ExpectedModelConfirmed)
            {
                Log.Info(notes);
            }
            else
            {
                Log.Warn(notes);
            }
        }
    }

    private static SourceResult FailedResult(IVariableSource source, string message)
    {
        return new SourceResult(
            SourceStatus.Failed,
            source.Description,
            source.IsOffline,
            FirmwareType.Unknown,
            null,
            Array.Empty<FirmwareVariable>(),
            EnumerationMethod.None,
            Array.Empty<string>(),
            null,
            message);
    }

    // ---- commands ----------------------------------------------------------------------------

    private async Task OpenBaselineFromDialogAsync()
    {
        string? folder;
        try
        {
            folder = InDialog(() => _platform.PickFolder(Strings.OpenBaselineName));
        }
        catch (Exception ex)
        {
            // The dialog loaded nothing, and the sentence says so instead of "Loading failed". When
            // no load finished behind the dialog, the state, the list, the title and the focus stay
            // as they are: "Nothing changed." When one did, it has already replaced the list, the
            // title and the identity, so "Nothing changed." would be false: the failure is followed
            // by that load's own sentence, and its focus request is repeated (AnnounceAfterDialog).
            _logger.Error(ex, "Open baseline folder failed");
            string format = LoadHeldBehindDialog ? Strings.OpenBaselineFailedAfterLoadFormat : Strings.OpenBaselineFailedFormat;
            AnnounceAfterDialog(string.Format(CultureInfo.InvariantCulture, format, WithoutClosingPeriod(ex.Message)));
            return;
        }

        if (string.IsNullOrWhiteSpace(folder))
        {
            _logger.Debug("Open baseline folder cancelled");
            AnnounceAfterDialog(LoadHeldBehindDialog ? null : Strings.DialogCancelledStatus);
            return;
        }

        HeldOutcome? held = TakeHeldOutcome();
        bool replacesHeldOutcome = held?.OfLoad == true;
        if (replacesHeldOutcome)
        {
            // A load finished behind the dialog; the folder picked replaces what it showed, so its
            // sentence is never spoken.
            _logger.Debug("The outcome of the load that finished behind the folder dialog is replaced by the folder picked");
        }

        if (held?.ChangeSentence is string change)
        {
            // A change refused behind the dialog is still said, before the baseline load's sentences.
            Announce(change);
        }

        Task load = OpenBaselineAsync(folder);
        if (replacesHeldOutcome && !load.IsCompleted)
        {
            // Its focus request is not dropped with it: that load rebuilt the list behind the
            // dialog, so the element the dialog gives focus back to may be gone, and WPF would put
            // focus on the window until the baseline load asks for its own target. Focus goes to
            // the Status box, which reads "Loading variables, please wait." while the baseline
            // loads. A baseline load that has already finished (or failed at once) made its own
            // request, so nothing is added then.
            _logger.Debug("The folder picked replaced a load that finished behind the dialog; focus goes to the Status box while the baseline loads");
            _focus.RequestFocus(FocusTarget.Status);
        }

        await load.ConfigureAwait(true);
    }

    private void ClearFilter()
    {
        // No focus request: Alt+C and the button leave focus where it is (docs/Keyboard-Map.md
        // section 9); Escape in the filter box is the view's own, documented move to the list.
        // The count is announced exactly once per press, even when the press changes nothing: the
        // filter box, bound TwoWay to FilterText, may echo the emptied text back through
        // OnFilterApplied while the list clears; forgetting the list the view applied last lets
        // that echo be the one announcement, and the call below then announces only when no echo did.
        _appliedFilterState = null;
        List.ClearFilter();
        OnFilterApplied();
    }

    private void CopyValue()
    {
        string? text;
        if (_dellFocusPart != DellFocusPart.None)
        {
            // In the Dell region Alt+Y copies what is there, never a firmware variable's value:
            // the state line's text while it has focus, the selected Dell row's sentence otherwise.
            text = _dellFocusPart == DellFocusPart.SourceLine ? Dell.StateText : Dell.SelectedRow?.AccessibleName;
        }
        else
        {
            // The pane's copy text must belong to the selected row, not to the row before it.
            EnsureDetailCurrent();
            text = Detail.CopyValueText;
            if (string.IsNullOrEmpty(text))
            {
                text = List.SelectedItem?.Summary;
            }

            if (string.IsNullOrEmpty(text))
            {
                text = List.SelectedItem?.Key;
            }
        }

        if (string.IsNullOrEmpty(text))
        {
            Announce(Strings.NothingToCopyStatus);
            return;
        }

        // The clipboard can be held by another process; that is a copy failure, not a load failure.
        try
        {
            _platform.SetClipboardText(text);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Copy value failed");
            Announce(string.Format(CultureInfo.InvariantCulture, Strings.CopyFailedFormat, ex.Message));
            return;
        }

        _logger.Debug("Copied a value of {Length} characters to the clipboard", text.Length);
        Announce(Strings.CopiedValueStatus);
    }

    private void ExportVisibleList()
    {
        // The save dialog blocks the shell; the pane settles before it opens, not underneath it.
        // The rows are the ones that pass the filter now (collapsed groups included, as the count
        // of "Showing N of M variables"); a load that finishes behind the dialog does not change them.
        EnsureDetailCurrent();
        IReadOnlyList<VariableItemViewModel> items = List.VisibleItems;
        string? path = null;
        string sentence;
        try
        {
            path = InDialog(() => _platform.PickSaveFile(Strings.ExportVisibleListName, ExportDefaultFileName, ExportFilter));
            if (string.IsNullOrWhiteSpace(path))
            {
                _logger.Debug("Export visible list cancelled");
                AnnounceAfterDialog(LoadHeldBehindDialog ? null : Strings.DialogCancelledStatus);
                return;
            }

            string content = IsJsonPath(path) ? VisibleListExporter.ToJson(items) : VisibleListExporter.ToPlainText(items);
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            _logger.Debug("Exported {Count} visible variables to {Path}", items.Count, path);
            sentence = string.Format(CultureInfo.InvariantCulture, Strings.ExportedFormat, Strings.VisibleVariables(items.Count), path);
        }
        catch (Exception ex)
        {
            // The save dialog or the file write failed; the state and the list are untouched.
            _logger.Error(ex, "Export visible list to {Path:l} failed", path ?? "(no file chosen)");
            sentence = string.Format(CultureInfo.InvariantCulture, ExportFailedFormat, ex.Message);
        }

        AnnounceAfterDialog(sentence);
    }

    private void CopyLog()
    {
        string text = Log.CopyText();
        try
        {
            _platform.SetClipboardText(text);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Copy log failed");
            Announce(string.Format(CultureInfo.InvariantCulture, Strings.CopyFailedFormat, ex.Message));
            return;
        }

        _logger.Debug("Copied {Count} log lines to the clipboard", Log.Count);
        Announce(Strings.CopiedLogStatus);
    }

    private void OnCommandError(Exception exception)
    {
        _logger.Error(exception, "A shell command failed");
        State = ShellState.Failed;
        IsBusy = false;
        Announce(string.Format(CultureInfo.InvariantCulture, Strings.FailedFormat, exception.Message));
        _focus.RequestFocus(FocusTarget.Status);
    }

    private void OnSelectionChanged(object? sender, VariableItemViewModel? item)
    {
        // The input trace's SELECTION line of the shell comes first, before the scheduling it causes
        // (this handler is the first the list's SelectionChanged has: the constructor adds it).
        if (_inputTrace.IsEnabled)
        {
            _inputTrace.Write(TraceKind.Selection, "shell: " + (item is null ? "no row" : "row " + TraceLine.Token(item.Name)));
        }

        // The item is not passed on: the refresh reads the selection when it runs, so the
        // refreshes a burst of arrow keys schedules all describe the same, latest row and the
        // scheduler may keep one of them.
        _detailScheduler.Schedule(RefreshDetail);
        KeepFocusFromDellAnswer("the selection of the variable list changed");
    }

    /// <summary>The scheduled refresh: shows the row selected now.</summary>
    private void RefreshDetail()
    {
        ShowSelectedRow(RebuildStep);
    }

    /// <summary>
    /// Brings the detail pane to the row selected now (<see cref="VariableDetailViewModel.Show"/>,
    /// which updates the pane in place). The trace still calls this step a rebuild, so traces
    /// before and after the in-place update compare line by line. With the input trace on, it is
    /// traced before and after: the row's variable name (never a value), how long it took, how
    /// many sections the pane has, how many sections it kept, added and removed, and how many
    /// fields it kept, updated, added and removed (<see cref="DetailPaneUpdate"/>, numbers only),
    /// or, when it throws, how long it ran
    /// and the exception's type (the exception goes on as before: the scheduler's policy logs it).
    /// Off, it is the update alone.
    /// </summary>
    /// <param name="step">What the trace calls the step: <see cref="RebuildStep"/> or <see cref="DirectRebuildStep"/>.</param>
    private void ShowSelectedRow(string step)
    {
        if (!_inputTrace.IsEnabled)
        {
            Detail.Show(List.SelectedItem);
            return;
        }

        VariableItemViewModel? row = List.SelectedItem;
        string name = row is null ? "(no row)" : TraceLine.Token(row.Name);
        _inputTrace.Write(TraceKind.Detail, step + " started: row " + name);
        long started = Stopwatch.GetTimestamp();
        DetailPaneUpdate update;
        try
        {
            update = Detail.Show(row);
        }
        catch (Exception ex)
        {
            _inputTrace.Write(TraceKind.Detail, step + " failed after " + TraceLine.Milliseconds(started, Stopwatch.GetTimestamp()) + " ms: row " + name + ", " + ex.GetType().Name);
            throw;
        }

        string elapsed = TraceLine.Milliseconds(started, Stopwatch.GetTimestamp());
        _inputTrace.Write(TraceKind.Detail, step + " finished in " + elapsed + " ms: row " + name + ", " + Detail.Sections.Count.ToString(CultureInfo.InvariantCulture) + " sections; " + update.ToString());
    }

    /// <summary>The method that asked for <see cref="EnsureDetailCurrent"/>, with what it stands for when it is one the trace knows.</summary>
    private static string FlushRequester(string caller)
    {
        string meaning = caller switch
        {
            "OnWindowPreviewKeyDown" => "an Alt key: an access key acts on the selected row",
            "FocusFirstDetailField" => "Enter, Space or F6 into the detail pane",
            "OnDetailPreviewGotKeyboardFocus" => "focus moving into or within the detail pane (Tab, a click, a screen reader's SetFocus, or the focus move after Enter, Space or F6)",
            nameof(CopyValue) => "Copy value",
            nameof(ExportVisibleList) => "Export visible list",
            nameof(RunChangeAsync) => "a Change command",
            nameof(ShowResult) => "a load finished",
            _ => string.Empty,
        };
        string name = string.IsNullOrEmpty(caller) ? "(unknown caller)" : caller;
        return meaning.Length == 0 ? name : name + " (" + meaning + ")";
    }

    // ---- Dell settings -------------------------------------

    /// <summary>The command's action: the read, with anything it throws reported as a command failure.</summary>
    private async Task RunReadDellSettingsAsync()
    {
        try
        {
            await ReadDellSettingsAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            OnCommandError(ex);
        }
    }

    /// <summary>
    /// The answer of a Dell read: the session cache and, in the live view, the rows and the state
    /// line take it; each warning of the result becomes one WARN line; then the sentence and the
    /// focus request of section 5.6, unless the quiet rule holds them back (see
    /// <see cref="ReadDellSettingsAsync"/>).
    /// </summary>
    private void ShowDellAnswer(DellSettingsResult result)
    {
        bool quiet = _dellQuiet;
        bool keepFocus = _dellKeepFocus;
        _dellQuiet = false;
        _dellKeepFocus = false;
        DellApplyOutcome outcome = Dell.ApplyLive(result);
        foreach (string warning in result.Warnings ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(warning))
            {
                Log.Warn(warning.Trim());
            }
        }

        string? problem = DellSentences.UnavailableSentence(result);
        if (quiet)
        {
            // Something started after Alt+S: its own sentence and focus stand. A read that gave no
            // answer still leaves a trace in the log panel.
            _logger.Debug("The Dell BIOS settings answered ({Outcome}) after a load, a dialog or the checks of a change started; taken quietly", result.Outcome);
            if (problem is not null)
            {
                Log.Warn(problem);
            }

            return;
        }

        (string sentence, FocusTarget focus) = outcome.Kind switch
        {
            DellApplyKind.Unavailable => (problem ?? DellSentences.UnavailableLead, FocusTarget.Status),
            DellApplyKind.UnavailableKept => (string.Format(CultureInfo.InvariantCulture, Strings.DellKeptFormat, problem ?? DellSentences.UnavailableLead, outcome.KeptReadTime), FocusTarget.Status),
            DellApplyKind.Unchanged => (Strings.DellUnchangedStatus, FocusTarget.DellSelectedRow),
            DellApplyKind.Changed => (string.Format(CultureInfo.InvariantCulture, Strings.DellChangedFormat, string.Join(DellSettingsViewModel.ChangeSeparator, outcome.Changes)), FocusTarget.DellSelectedRow),
            _ => (DellLoadedSentence(result), FocusTarget.DellSelectedRow),
        };
        Announce(sentence);
        if (keepFocus)
        {
            _logger.Debug("The person moved on while the Dell BIOS settings were read; focus stays where it is");
            return;
        }

        _focus.RequestFocus(focus);
    }

    /// <summary>
    /// The sentence of a first read that answered: "Loaded 10 Dell BIOS settings from Dell Command
    /// Monitor." when every setting was read, "Loaded 9 of 10 Dell BIOS settings from ...; not
    /// reported: ..." otherwise, followed by the fallback sentence when Dell Command Configure was
    /// used because Dell Command Monitor gave no answer.
    /// </summary>
    private static string DellLoadedSentence(DellSettingsResult result)
    {
        IReadOnlyList<FirmwareSetting> settings = result.Settings ?? Array.Empty<FirmwareSetting>();
        string[] missing = settings
            .Where(setting => setting.State is not (SettingState.Read or SettingState.NotApplicable))
            .Select(setting => setting.DisplayName)
            .ToArray();
        string source = DellSentences.SourceName(result.Source ?? DellSettingsSource.DellCommandMonitor);
        string loaded = missing.Length == 0
            ? string.Format(CultureInfo.InvariantCulture, Strings.DellLoadedFormat, Strings.DellSettings(settings.Count), source)
            : string.Format(CultureInfo.InvariantCulture, Strings.DellLoadedPartialFormat, settings.Count - missing.Length, settings.Count, source, string.Join(", ", missing));
        return string.IsNullOrWhiteSpace(result.FallbackSentence) ? loaded : loaded + SentenceSpace + result.FallbackSentence.Trim();
    }

    /// <summary>The speech part of the quiet rule: a Dell read in flight will be answered without a sentence and without a focus request.</summary>
    /// <param name="reason">What started, for the Debug line.</param>
    private void QuietDellAnswer(string reason)
    {
        if (Dell.IsLoading && !_dellQuiet)
        {
            _dellQuiet = true;
            _logger.Debug("The Dell read in flight will be answered quietly: {Reason}", reason);
        }
    }

    /// <summary>The focus part of the quiet rule: a Dell read in flight will be announced, but will ask for no focus.</summary>
    /// <param name="reason">How the person moved on, for the Debug line.</param>
    private void KeepFocusFromDellAnswer(string reason)
    {
        if (Dell.IsLoading && !_dellKeepFocus)
        {
            _dellKeepFocus = true;
            _logger.Debug("The Dell read in flight will ask for no focus: {Reason}", reason);
        }
    }

    /// <summary>True for the window's two ways into the detail pane that bring it up to date first (see <see cref="FlushRequester"/>).</summary>
    private static bool IsDetailPaneArrival(string caller) => caller is "FocusFirstDetailField" or "OnDetailPreviewGotKeyboardFocus";

    // ---- changes ------------------------------------------

    /// <summary>
    /// Review change, Boot once from this entry, Clear one-time boot or Restore previous value: the
    /// request, the checks on a worker thread (cancellable by Escape between stages), the dialog
    /// inside the dialog guard, and on Confirm change the commit, never cancelled, then the outcome
    /// and, after a write call, the live reload. Every sentence is said once; nothing throws.
    /// Checks cancelled by Escape or by a load that replaced the list (<see cref="ShowResult"/>)
    /// open no dialog, and a plan that arrives with the cancel is recorded as cancelled. A plan that
    /// arrives while another dialog is open never opens Confirm change on top of it: it is recorded
    /// as cancelled, and "Change blocked: Another dialog was open when the checks finished. Nothing
    /// was written." waits for that dialog to close, like a load's sentence. (The window is disabled
    /// under a modal dialog, and Open baseline folder and Export visible list are disabled while the
    /// checks run, so neither order of events can come from the keyboard; this is the second guard.)
    /// A plan for the live firmware while a baseline is shown, or for a proposed order that is no
    /// longer the one shown, is cancelled with "Cancelled; nothing changed." the same way. Before
    /// anything else the pane is brought up to date (<see cref="EnsureDetailCurrent"/>): the request
    /// is built from the variable the pane shows, and after a selection change that is still waiting
    /// out the quiet period (Alpha 2 finding A2-7) that would be the row shown before. When the row
    /// now shown does not offer the action (the command was enabled for that earlier row), nothing
    /// is checked and nothing is said, as for the access key of a hidden button.
    /// </summary>
    /// <param name="action">The control that was pressed.</param>
    /// <returns>A task that completes when the change has been checked, cancelled or written and announced.</returns>
    private async Task RunChangeAsync(ChangeAction action)
    {
        IWriteService? writes = _writes;
        if (writes is null || IsWriting || IsFinishingChange || Detail.Change.IsChecking)
        {
            _logger.Debug("{Action} ignored: no write pipeline, or a change is being checked or written, or its result is not said yet", action);
            return;
        }

        // A selection change may still be waiting out the quiet period (finding A2-7): the command
        // acts on the selected row, so the pane is brought up to date first, and an action the row
        // now shown does not offer does nothing, as the key of a hidden button does.
        EnsureDetailCurrent();
        if (!Detail.Change.Offers(action))
        {
            _logger.Debug("{Action} ignored: the variable now shown does not offer it", action);
            return;
        }

        ChangeRequest? request = RequestFor(action, writes, out bool fromPendingSnapshot);
        if (request is null)
        {
            return;
        }

        _logger.Debug("{Action}: checking a {Request}", action, request.GetType().Name);
        QuietDellAnswer("the checks of a change started");
        Announce(Strings.CheckingStatus);
        _checksCancelledByLoad = false;
        CancellationToken token = Detail.Change.BeginChecking(action);
        PrepareResult? prepared;
        try
        {
            prepared = await writes.PrepareAsync(request, token).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            bool replaced = TakeChecksCancelledByLoad();
            Detail.Change.EndChecking();
            _logger.Error(ex, "The checks of a change stopped");
            if (replaced)
            {
                // A load cancelled these checks and has said so before its own sentence, which
                // stays in Status; nothing was written.
                return;
            }

            Announce(BlockedSentence(ex.Message));
            _focus.RequestFocus(FocusTarget.Status);
            return;
        }

        bool cancelled = token.IsCancellationRequested;
        bool cancelledByLoad = TakeChecksCancelledByLoad();
        Detail.Change.EndChecking();
        if (prepared is null || prepared.IsCancelled || cancelled)
        {
            // Escape: "Cancelled; nothing changed.", and focus stays where it is. A load that
            // replaced the list cancelled them instead: it said "Cancelled; nothing changed." before
            // its own sentence and made its own focus request, so nothing more is said here. A plan
            // that arrived with either cancel is cancelled too.
            if (cancelledByLoad)
            {
                _logger.Debug("The checks of a change were cancelled by a load that replaced the list");
            }
            else
            {
                _logger.Debug("The checks of a change were cancelled");
                Announce(Strings.DialogCancelledStatus);
            }

            if (prepared?.Plan is ChangePlan unwanted)
            {
                await RecordCancelAsync(writes, unwanted).ConfigureAwait(true);
            }

            return;
        }

        if (prepared.Plan is not ChangePlan plan)
        {
            _logger.Debug("The checks blocked the change at {Check}", prepared.BlockedBy);
            Announce(BlockedSentence(prepared.Sentence));
            _focus.RequestFocus(FocusTarget.Status);
            return;
        }

        if (_dialogOpen)
        {
            // Never a dialog on top of another one: the plan is cancelled, and the sentence waits
            // for that dialog to close (AnnounceAfterDialog), where focus goes to the Status box.
            _logger.Debug("The plan {TransactionId} arrived while another dialog was open; it is cancelled", plan.SnapshotFolderName);
            HoldChangeBehindDialog(BlockedSentence(Strings.AnotherDialogOpenReason));
            await RecordCancelAsync(writes, plan).ConfigureAwait(true);
            return;
        }

        if (IsOffline || (action == ChangeAction.ReviewChange && request is ReorderRequest asked && !asked.NewOrder.SequenceEqual(Detail.Change.Editor.Entries.Select(entry => entry.Number))))
        {
            // What the checks were started from is no longer shown (a load cancels them, so this is
            // the second guard): no dialog for a plan nobody is looking at.
            _logger.Debug("The plan {TransactionId} no longer fits what the window shows; it is cancelled", plan.SnapshotFolderName);
            Announce(Strings.DialogCancelledStatus);
            await RecordCancelAsync(writes, plan).ConfigureAwait(true);
            return;
        }

        await ConfirmAndCommitAsync(writes, plan, fromPendingSnapshot, action == ChangeAction.ReviewChange).ConfigureAwait(true);
    }

    /// <summary>Whether a load cancelled the checks that just answered; the flag is cleared for the next checks.</summary>
    private bool TakeChecksCancelledByLoad()
    {
        bool cancelledByLoad = _checksCancelledByLoad;
        _checksCancelledByLoad = false;
        return cancelledByLoad;
    }

    /// <summary>The dialog for a plan that passed the checks, then the commit or the recorded cancel.</summary>
    /// <param name="writes">The write service.</param>
    /// <param name="plan">The plan the checks made.</param>
    /// <param name="fromPendingSnapshot">True when the plan restores a snapshot whose own change did not complete.</param>
    /// <param name="ofProposedOrder">True when the plan is the proposed order's (Review change).</param>
    private async Task ConfirmAndCommitAsync(IWriteService writes, ChangePlan plan, bool fromPendingSnapshot, bool ofProposedOrder)
    {
        ConfirmChangeViewModel dialog = new(plan, writes.SnapshotRoot, fromPendingSnapshot);
        ConfirmDecision decision;
        try
        {
            decision = InDialog(() => _platform.ConfirmChange(dialog));
        }
        catch (Exception ex)
        {
            // The dialog could not be shown or failed: nothing was confirmed, so nothing is written.
            _logger.Error(ex, "The confirmation dialog failed");
            HeldOutcome? heldBehindFailure = TakeHeldOutcome();
            Announce(Joined(BlockedSentence(ex.Message), heldBehindFailure));
            _focus.RequestFocus(FocusTarget.Status);
            await RecordCancelAsync(writes, plan).ConfigureAwait(true);
            return;
        }

        if (!decision.Confirmed)
        {
            // Cancel or Escape: focus back to the button that opened the dialog. A load that
            // finished behind the dialog changed the list, so its sentence and focus are the news.
            _logger.Debug("The confirmation of {TransactionId} was cancelled", plan.SnapshotFolderName);
            if (_heldOutcome is null)
            {
                Announce(Strings.DialogCancelledStatus);
                _focus.RequestFocus(FocusTarget.ChangeControl);
            }
            else
            {
                AnnounceAfterDialog(null);
            }

            await RecordCancelAsync(writes, plan).ConfigureAwait(true);
            return;
        }

        // The decision is bound to exactly the plan the dialog showed. From here until the result
        // is said, through the live reload after the write, nothing else can start.
        ConfirmationToken confirmation = plan.Confirm(dialog.ChoiceFor(decision));
        HeldOutcome? heldBehindDialog = TakeHeldOutcome();
        IsFinishingChange = true;
        try
        {
            await CommitAndReportAsync(writes, plan, confirmation, heldBehindDialog, ofProposedOrder).ConfigureAwait(true);
        }
        finally
        {
            IsFinishingChange = false;
        }
    }

    /// <summary>
    /// The commit of a confirmed plan with <see cref="IsWriting"/> set, then its outcome: after a
    /// write call the live reload, whose Loaded sentence follows the write's; otherwise the refusal.
    /// </summary>
    /// <param name="writes">The write service.</param>
    /// <param name="plan">The confirmed plan.</param>
    /// <param name="confirmation">The confirmation of exactly that plan.</param>
    /// <param name="heldBehindDialog">The outcome of a load that finished behind the dialog, or null.</param>
    /// <param name="ofProposedOrder">True when the plan is the proposed order's (Review change).</param>
    private async Task CommitAndReportAsync(IWriteService writes, ChangePlan plan, ConfirmationToken confirmation, HeldOutcome? heldBehindDialog, bool ofProposedOrder)
    {
        IsWriting = true;
        Announce(Strings.WritingStatus);
        _focus.RequestFocus(FocusTarget.Status);
        TransactionResult? result = null;
        Exception? failure = null;
        try
        {
            result = await writes.CommitAsync(plan, confirmation).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            failure = ex;
            _logger.Error(ex, "The commit of {TransactionId} stopped", plan.SnapshotFolderName);
        }
        finally
        {
            IsWriting = false;
        }

        // A load started during the checks that finished while the change was written: its
        // sentence and focus were held (AnnounceOutcome), so Status kept the Writing sentence.
        HeldOutcome? heldDuringWrite = TakeHeldOutcome();
        if (result is null)
        {
            // Whether the call was made is not known: say so and read the firmware again, which
            // replaces what a load that finished during the write showed.
            string unknown = CheckSentences.Format(CheckSentences.RecordNotSavedUnknownFormat, failure?.Message ?? NoResultMessage);
            await LoadAsync(_liveSource, new WriteFollowUp(unknown, FocusTarget.Status, null)).ConfigureAwait(true);
            return;
        }

        string sentence = OutcomeSentence(result);
        _logger.Debug("The change {TransactionId} ended {Outcome}", plan.SnapshotFolderName, result.Outcome);
        if (result.ApiCalled)
        {
            // The firmware may hold new bytes: the list and the pane are read again (which replaces
            // what a load that finished during the write showed), and the write's sentence and the
            // Loaded sentence are one announcement.
            FocusTarget focus = result.Outcome == TransactionOutcome.Verified ? FocusTarget.ListRowByKey : FocusTarget.Status;
            bool proposedOrderWritten = ofProposedOrder && result.Outcome is TransactionOutcome.Verified or TransactionOutcome.ChangedDespiteError;
            await LoadAsync(_liveSource, new WriteFollowUp(sentence, focus, plan.Variable.Key, proposedOrderWritten)).ConfigureAwait(true);
            return;
        }

        // Nothing was written (a refusal from S11 to S15): no reload; a load that finished during
        // the write, or else behind the dialog, is said after the refusal.
        Announce(Joined(sentence, heldDuringWrite ?? heldBehindDialog));
        _focus.RequestFocus(FocusTarget.Status);
    }

    /// <summary>
    /// The request of an action, or null after saying why there is none: Review change with the
    /// firmware's order says "Nothing to change: ...", Restore previous value without a snapshot
    /// says "No snapshot to restore: ...". Entry numbers or a snapshot folder; never bytes.
    /// </summary>
    private ChangeRequest? RequestFor(ChangeAction action, IWriteService writes, out bool fromPendingSnapshot)
    {
        fromPendingSnapshot = false;
        VariableItemViewModel? item = Detail.Change.Item;
        switch (action)
        {
            case ChangeAction.ReviewChange:
                if (!Detail.Change.Editor.HasChanges)
                {
                    Announce(Strings.NothingToChangeStatus);
                    return null;
                }

                return Detail.Change.Editor.BuildRequest();
            case ChangeAction.BootOnce:
                if (item is null || !KnownVariableNames.IsBootEntryName(item.Name, out int number))
                {
                    _logger.Debug("Boot once ignored: the row shown is not a Boot#### entry");
                    return null;
                }

                return new BootNextRequest(ChangeRequest.AppHost, (ushort)number);
            case ChangeAction.ClearBootNext:
                return new ClearBootNextRequest(ChangeRequest.AppHost);
            default:
                if (item is null)
                {
                    _logger.Debug("Restore previous value ignored: no row is shown");
                    return null;
                }

                SnapshotSummary? newest = writes.Newest(item.Key);
                if (newest is null)
                {
                    Announce(string.Format(CultureInfo.InvariantCulture, Strings.NoSnapshotFormat, item.Name));
                    return null;
                }

                fromPendingSnapshot = newest.Outcome == WriteRecordOutcome.Pending;
                return new RestoreRequest(ChangeRequest.AppHost, newest.Folder);
        }
    }

    /// <summary>Records the cancel of a plan in the pipeline (one cancelled record, no folder); a failure is logged, never thrown.</summary>
    private async Task RecordCancelAsync(IWriteService writes, ChangePlan plan)
    {
        try
        {
            await writes.CancelAsync(plan).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "The cancel of {TransactionId} could not be recorded", plan.SnapshotFolderName);
        }
    }

    /// <summary>The Status sentence of a transaction's outcome: a refusal as <see cref="BlockedSentence"/> says it, anything else in the pipeline's own words.</summary>
    private static string OutcomeSentence(TransactionResult result)
    {
        return result.Outcome switch
        {
            TransactionOutcome.Blocked => BlockedSentence(result.Sentence),
            TransactionOutcome.Cancelled => Strings.DialogCancelledStatus,
            _ => result.Sentence,
        };
    }

    /// <summary>Takes the outcome of a load that finished behind a dialog, so nothing else delivers it.</summary>
    private HeldOutcome? TakeHeldOutcome()
    {
        HeldOutcome? held = _heldOutcome;
        _heldOutcome = null;
        return held;
    }

    /// <summary>A sentence followed by the sentence of a load that finished behind the dialog, as one announcement.</summary>
    private static string Joined(string sentence, HeldOutcome? held)
    {
        return held is null ? sentence : WithoutClosingPeriod(sentence) + Strings.SentenceSeparator + held.Sentence;
    }

    /// <summary>A sentence preceded by another (the write's, or "Proposed order discarded."), joined by <see cref="Strings.SentenceSeparator"/>; the sentence alone when there is none.</summary>
    private static string Lead(string? lead, string sentence)
    {
        return lead is null ? sentence : WithoutClosingPeriod(lead) + Strings.SentenceSeparator + sentence;
    }

    /// <summary>
    /// What a load's own sentence is preceded by: "Cancelled; nothing changed." when it cancelled
    /// the checks of a change, the sentence of the write it follows, then "Proposed order discarded."
    /// when it dropped a proposed order; null when none applies.
    /// </summary>
    /// <param name="followUp">The write the load follows, or null.</param>
    /// <param name="discarded">True when the load dropped a proposed order the firmware does not hold.</param>
    /// <param name="checksCancelled">True when the load cancelled the checks of a change that were running.</param>
    private static string? LeadOf(WriteFollowUp? followUp, bool discarded, bool checksCancelled)
    {
        string? dropped = discarded ? Strings.ProposedOrderDiscarded : null;
        string? lead = followUp is null ? dropped : dropped is null ? followUp.Sentence : Lead(followUp.Sentence, dropped);
        if (!checksCancelled)
        {
            return lead;
        }

        return lead is null ? Strings.DialogCancelledStatus : Lead(Strings.DialogCancelledStatus, lead);
    }

    /// <summary>The text with a closing period added when it ends without one.</summary>
    private static string WithClosingPeriod(string text)
    {
        string trimmed = (text ?? string.Empty).Trim();
        return trimmed.Length == 0 || trimmed.EndsWith('.') || trimmed.EndsWith('?') || trimmed.EndsWith('!') ? trimmed : trimmed + ".";
    }

    // ---- helpers -----------------------------------------------------------------------------

    /// <summary>Announces the text, mirrors it into the log as an INFO line and refreshes <see cref="StatusText"/>.</summary>
    private void Announce(string text)
    {
        string value = text ?? string.Empty;
        _announcer.Announce(value);
        Log.Info(value);
        OnPropertyChanged(nameof(StatusText));
    }

    /// <summary>
    /// Shows a modal dialog through the platform, noting that it is open so a load finishing
    /// meanwhile holds its announcement (<see cref="AnnounceOutcome"/>). A dialog shown while
    /// another is open (which the window, disabled under a modal dialog, does not allow) gives back
    /// the outer dialog's state when it closes, so the outer dialog still holds what finishes behind it.
    /// A Dell read in flight is then answered quietly.
    /// </summary>
    private T InDialog<T>(Func<T> show)
    {
        QuietDellAnswer("a dialog opened");
        bool outerDialogOpen = _dialogOpen;
        _dialogOpen = true;
        try
        {
            return show();
        }
        finally
        {
            _dialogOpen = outerDialogOpen;
        }
    }

    /// <summary>True when a load finished behind the open dialog and its outcome is held (not only the sentence of a change refused behind it).</summary>
    private bool LoadHeldBehindDialog => _heldOutcome?.OfLoad == true;

    /// <summary>
    /// A change refused because another dialog was open when its checks answered: its sentence waits
    /// for that dialog to close, in front of the sentence of a load that finished behind the dialog,
    /// and asks for focus on the Status box unless such a load asks for its own target.
    /// </summary>
    /// <param name="sentence">The blocked sentence.</param>
    private void HoldChangeBehindDialog(string sentence)
    {
        HeldOutcome? held = _heldOutcome;
        _heldOutcome = held is null
            ? new HeldOutcome(sentence, FocusTarget.Status, sentence, OfLoad: false)
            : held with { Sentence = Lead(sentence, held.Sentence), ChangeSentence = sentence };
    }

    /// <summary>
    /// A command's dialog has closed: announces the command's own sentence followed by the sentence
    /// of a load that finished while the dialog was open, and requests that load's focus target,
    /// which the open dialog would have swallowed. Without such a load, announces the sentence
    /// alone and requests nothing. A null sentence says nothing of its own (a cancelled dialog
    /// behind which a load finished: the load's sentence is the news). A change refused behind the
    /// dialog (<see cref="HoldChangeBehindDialog"/>) is said the same way, before such a load's sentence.
    /// </summary>
    /// <param name="sentence">The command's own sentence, or null.</param>
    private void AnnounceAfterDialog(string? sentence)
    {
        HeldOutcome? held = _heldOutcome;
        _heldOutcome = null;
        if (held is null)
        {
            if (sentence is not null)
            {
                Announce(sentence);
            }

            return;
        }

        Announce(sentence is null ? held.Sentence : WithoutClosingPeriod(sentence) + Strings.SentenceSeparator + held.Sentence);
        _focus.RequestFocus(held.Focus);
    }

    /// <summary>The text without surrounding white space and without one closing period, so it can be followed by ". " or by a format's own period.</summary>
    private static string WithoutClosingPeriod(string text)
    {
        string trimmed = (text ?? string.Empty).Trim();
        return trimmed.EndsWith('.') ? trimmed[..^1] : trimmed;
    }

    private static bool IsJsonPath(string path)
    {
        return string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase);
    }

    private static string Label(string label, string value) => label + ": " + value;

    private static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? Strings.Unknown : value.Trim();

    private static string FormatOs(MachineIdentityResult identity)
    {
        List<string> parts = new();
        if (!string.IsNullOrWhiteSpace(identity.OsCaption))
        {
            parts.Add(identity.OsCaption.Trim());
        }

        if (!string.IsNullOrWhiteSpace(identity.OsVersion))
        {
            parts.Add(identity.OsVersion.Trim());
        }

        if (!string.IsNullOrWhiteSpace(identity.OsBuild))
        {
            parts.Add("(build " + identity.OsBuild.Trim() + ")");
        }

        return parts.Count == 0 ? Strings.Unknown : string.Join(' ', parts);
    }

    /// <summary>The list as the view applied it: the filter text, grouping, sort order and the two counts.</summary>
    private sealed record FilterState(string FilterText, GroupBy GroupBy, SortBy SortBy, int VisibleCount, int TotalCount)
    {
        public static FilterState Of(VariableListViewModel list) => new(list.FilterText, list.GroupBy, list.SortBy, list.VisibleCount, list.TotalCount);

        /// <summary>True when the filter text and both counts are the same, so "Showing N of M variables" would tell nothing new; the grouping and the sort order may differ.</summary>
        public bool ShowsTheSameRows(FilterState other)
        {
            return string.Equals(FilterText, other.FilterText, StringComparison.Ordinal) && VisibleCount == other.VisibleCount && TotalCount == other.TotalCount;
        }
    }

    /// <summary>The announcement and focus request of a load that finished while a dialog was open, or of a change refused behind it.</summary>
    /// <param name="Sentence">Everything that waits: the refused change's sentence, when there is one, then the load's.</param>
    /// <param name="Focus">Where focus goes when the dialog has closed.</param>
    /// <param name="ChangeSentence">The sentence of a change refused behind the dialog (it leads <paramref name="Sentence"/>), or null.</param>
    /// <param name="OfLoad">True when a load finished behind the dialog; false when only a change was refused behind it.</param>
    private sealed record HeldOutcome(string Sentence, FocusTarget Focus, string? ChangeSentence = null, bool OfLoad = true);

    /// <summary>The write a live reload follows: its sentence, said before the load's, and where focus goes when the load succeeds.</summary>
    /// <param name="Sentence">The write's Status sentence.</param>
    /// <param name="Focus">Where a successful reload sends focus: <see cref="FocusTarget.ListRowByKey"/> after a verified change, the Status box otherwise.</param>
    /// <param name="RowKey">The key of the written variable, for <see cref="FocusTarget.ListRowByKey"/>.</param>
    /// <param name="ProposedOrderWritten">True when the write was the proposed order's (Review change) and the firmware was read back holding it (Verified or ChangedDespiteError), so a reload that reads nothing has not dropped it.</param>
    private sealed record WriteFollowUp(string Sentence, FocusTarget Focus, string? RowKey, bool ProposedOrderWritten = false);
}
