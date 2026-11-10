// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Privileges;
using UefiAccess.Core.Settings.Dell;
using UefiAccess.Core.Tests.Fixtures;
using UefiAccess.Core.Variables;
using UefiAccess.Presentation.Accessibility;
using UefiAccess.Presentation.Resources;
using UefiAccess.Presentation.Services;
using UefiAccess.Presentation.ViewModels;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// The shell as the application composes it, for the tests of the round-2 fixes: recording
/// announcer and focus sink, a <see cref="DialogPlatform"/> whose dialogs can run code while they
/// are "open" (as a modal dialog's message loop runs a load's completion), a live and a baseline
/// <see cref="GatedSource"/>, and the application's logger mirrored into the log panel by the
/// <see cref="LogPanelMirror"/> rule, with every event also kept in <see cref="Events"/> (the log
/// file). The window's one <see cref="ShellViewModel.AnnounceReady"/> call has been made.
/// </summary>
internal sealed class ShellTestRig
{
    public const string BaselineFolder = "/baselines/optiplex-20260922";
    public const string BaselineDescription = Strings.BaselinePrefix + BaselineFolder;
    public const VariableAttributes NvBsRt = VariableAttributes.NonVolatile | VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess;

    public static readonly FirmwareVariable BootOrder = new("BootOrder", KnownGuids.EfiGlobalVariable, NvBsRt, new byte[] { 0x01, 0x00, 0x00, 0x00 });
    public static readonly FirmwareVariable Timeout = new("Timeout", KnownGuids.EfiGlobalVariable, NvBsRt, new byte[] { 0x05, 0x00 });
    public static readonly FirmwareVariable Vendor = new("AcpiGlobalVariable", Guid.Parse("af9ffd67-ec10-488a-9dfc-6cbf5ee22c2e"), NvBsRt, new byte[] { 0x10, 0x20, 0x30 });

    public ShellTestRig(IVariableSource? live = null, Func<string, IVariableSource>? baselineFactory = null)
    {
        Mirror = new PanelMirror();
        Logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(Events).WriteTo.Sink(Mirror).CreateLogger();
        LiveSource = live ?? Live;
        Shell = new ShellViewModel(
            LiveSource,
            baselineFactory ?? (folder =>
            {
                FactoryFolders.Add(folder);
                return Baseline;
            }),
            Announcer,
            Focus,
            Platform,
            Logger,
            dellSource: Dell);
        Mirror.Attach(Shell.Log);
        Shell.AnnounceReady();
    }

    public RecordingAnnouncer Announcer { get; } = new();

    public RecordingFocusSink Focus { get; } = new();

    public DialogPlatform Platform { get; } = new();

    public RecordedEvents Events { get; } = new();

    public PanelMirror Mirror { get; }

    public ILogger Logger { get; }

    public GatedSource Live { get; } = new(Strings.LiveSourceName, isOffline: false);

    public GatedSource Baseline { get; } = new(BaselineDescription, isOffline: true);

    /// <summary>This computer's Dell BIOS settings as the shell reads them; it reads nothing of this PC.</summary>
    public GatedDellSource Dell { get; } = new();

    public IVariableSource LiveSource { get; }

    public List<string> FactoryFolders { get; } = new();

    public ShellViewModel Shell { get; }

    /// <summary>The panel's lines as (level, message) pairs.</summary>
    public (string Level, string Message)[] Lines => Shell.Log.Lines.Select(line => (line.Level, line.Message)).ToArray();

    public static SourceResult Loaded(IReadOnlyList<FirmwareVariable> variables, string description = Strings.LiveSourceName, bool isOffline = false)
    {
        return new SourceResult(
            SourceStatus.Loaded,
            description,
            isOffline,
            FirmwareType.Uefi,
            isOffline ? null : PrivilegeState.Enabled,
            variables,
            EnumerationMethod.InMemory,
            Array.Empty<string>(),
            LiveFirmwareSourceTests.Identity(),
            null);
    }

    public static SourceResult Refused(string sentence)
    {
        return new SourceResult(
            SourceStatus.Refused,
            Strings.LiveSourceName,
            false,
            FirmwareType.Uefi,
            PrivilegeState.NotElevated,
            Array.Empty<FirmwareVariable>(),
            EnumerationMethod.None,
            Array.Empty<string>(),
            null,
            sentence);
    }
}

/// <summary>
/// A source that answers at once with <see cref="Result"/>, or, when <see cref="Gate"/> is set,
/// only when the test completes the gate; it records the cancellation token of every load.
/// </summary>
internal sealed class GatedSource : IVariableSource
{
    public GatedSource(string description, bool isOffline)
    {
        Description = description;
        IsOffline = isOffline;
    }

    public string Description { get; }

    public bool IsOffline { get; }

    public SourceResult? Result { get; set; }

    public TaskCompletionSource<SourceResult>? Gate { get; set; }

    public int LoadCalls { get; private set; }

    public List<CancellationToken> Tokens { get; } = new();

    public Task<SourceResult> LoadAsync(CancellationToken cancellationToken)
    {
        LoadCalls++;
        Tokens.Add(cancellationToken);
        return Gate is not null ? Gate.Task : Task.FromResult(Result!);
    }
}

/// <summary>
/// A Dell settings source that answers at once with <see cref="Result"/>, or, when <see cref="Gate"/>
/// is set, only when the test completes the gate; it counts its reads. It never reads this PC (the
/// unit): no test composes the real Dell reader.
/// </summary>
internal sealed class GatedDellSource : IDellSettingsSource
{
    /// <summary>The answer of a read when no gate is set; a read without one fails, as a broken source would.</summary>
    public DellSettingsResult? Result { get; set; }

    /// <summary>When set, a read answers only when the test completes it.</summary>
    public TaskCompletionSource<DellSettingsResult>? Gate { get; set; }

    /// <summary>When set, a read throws it instead of answering.</summary>
    public Exception? Throws { get; set; }

    public int ReadCalls { get; private set; }

    public Task<DellSettingsResult> ReadAsync()
    {
        ReadCalls++;
        if (Throws is not null)
        {
            return Task.FromException<DellSettingsResult>(Throws);
        }

        if (Gate is not null)
        {
            return Gate.Task;
        }

        return Result is not null ? Task.FromResult(Result) : Task.FromException<DellSettingsResult>(new InvalidOperationException("the test scripted no Dell answer"));
    }
}

/// <summary>
/// Dell reads as Core's reader would return them, for the Presentation tests, made by the
/// providers' and the reader's own pure functions over the fixtures (<see cref="CliTestHarness"/>):
/// the unit on A18, the unit with one value changed, the fallback, and a computer without the Dell tools.
/// </summary>
internal static class DellResults
{
    /// <summary>The unit's ten settings through Dell Command Monitor, outcome Complete.</summary>
    public static DellSettingsResult CompleteA18() => CliTestHarness.DellCompleteA18(DellReadOptions.Default);

    /// <summary>The unit's settings with Num Lock at boot disabled, read at <paramref name="readAt"/>.</summary>
    public static DellSettingsResult NumLockDisabled(DateTimeOffset readAt)
    {
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = DcmRowFixtures.UnitA18()
            .Select(row => string.Equals((string?)row["AttributeName"], "Num Lock", StringComparison.Ordinal)
                ? DcmRowFixtures.Row("Num Lock", DcmRowFixtures.Texts("1"), DcmRowFixtures.Texts("1", "2"), DcmRowFixtures.Texts("Disable", "Enable"))
                : row)
            .ToArray();
        return CliTestHarness.DellFromDcm(rows, DellReadOptions.Default) with { ReadAt = readAt };
    }

    /// <summary>Dell Command Configure read instead of a missing Dell Command Monitor: nine settings, Boot list option not reported, with the fallback sentence.</summary>
    public static DellSettingsResult Fallback() => CliTestHarness.DellFallback();

    /// <summary>Neither Dell tool installed: nothing answered.</summary>
    public static DellSettingsResult NothingInstalled() => CliTestHarness.DellNothingInstalled(DellReadOptions.Default);

    /// <summary>The sentence of <see cref="NothingInstalled"/>.</summary>
    public const string NothingInstalledSentence = "Dell BIOS settings cannot be read. Dell Command Monitor is not installed. Dell Command Configure was not found. Install the Microsoft Visual C++ 2015-2022 x64 runtime, then Dell Command Monitor, and try again; the Dell interface setup page of the documentation has the steps.";

    /// <summary>The read time as the live state line and the kept sentence say it.</summary>
    public static string LiveTime(DellSettingsResult result) => result.ReadAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
}

/// <summary>
/// Dialogs whose answer is set by the test and that can run code while they are "open", the way
/// a modal dialog's message loop runs the completion of a load; or that throw. The confirmation
/// dialog of a change answers through <see cref="ConfirmAnswer"/> (cancelled when it is null, as
/// Escape does) and keeps every view model it was shown.
/// </summary>
internal sealed class DialogPlatform : IPlatformInteractions
{
    public string? FolderToReturn { get; set; }

    public string? FileToReturn { get; set; }

    public Exception? DialogThrows { get; set; }

    /// <summary>Runs while a dialog is open, before it returns.</summary>
    public Action? WhileOpen { get; set; }

    /// <summary>The decision of the confirmation dialog; null cancels.</summary>
    public Func<ConfirmChangeViewModel, ConfirmDecision>? ConfirmAnswer { get; set; }

    /// <summary>Every confirmation dialog shown, oldest first.</summary>
    public List<ConfirmChangeViewModel> ConfirmDialogs { get; } = new();

    public int DialogsShown { get; private set; }

    public string? LastClipboardText { get; private set; }

    public string? PickFolder(string title) => Show(FolderToReturn);

    public string? PickSaveFile(string title, string defaultFileName, string filter) => Show(FileToReturn);

    public void SetClipboardText(string text) => LastClipboardText = text;

    public ConfirmDecision ConfirmChange(ConfirmChangeViewModel viewModel)
    {
        ConfirmDialogs.Add(viewModel);
        Show(null);
        return ConfirmAnswer?.Invoke(viewModel) ?? ConfirmDecision.Cancelled;
    }

    private string? Show(string? answer)
    {
        DialogsShown++;
        WhileOpen?.Invoke();
        if (DialogThrows is not null)
        {
            throw DialogThrows;
        }

        return answer;
    }
}

/// <summary>The application's LogPanelSink without the dispatcher: the <see cref="LogPanelMirror"/> rule over the shell's log.</summary>
internal sealed class PanelMirror : ILogEventSink
{
    private LogViewModel? _target;

    /// <summary>Events that were added to the panel.</summary>
    public int Mirrored { get; private set; }

    public void Attach(LogViewModel target) => _target = target;

    public void Emit(LogEvent logEvent)
    {
        LogLineViewModel? line = LogPanelMirror.ToLine(logEvent);
        if (line is null || _target is null)
        {
            return;
        }

        Mirrored++;
        _target.Add(line);
    }
}

/// <summary>Keeps every event, at any level, as the log file would.</summary>
internal sealed class RecordedEvents : ILogEventSink
{
    private readonly object _gate = new();
    private readonly List<LogEvent> _events = new();

    public IReadOnlyList<LogEvent> Snapshot()
    {
        lock (_gate)
        {
            return _events.ToArray();
        }
    }

    public void Emit(LogEvent logEvent)
    {
        lock (_gate)
        {
            _events.Add(logEvent);
        }
    }
}
