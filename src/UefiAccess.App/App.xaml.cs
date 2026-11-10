// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using Serilog;
using UefiAccess.App.Platform;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Presentation.Diagnostics;
using UefiAccess.Presentation.Resources;
using UefiAccess.Presentation.ViewModels;

namespace UefiAccess.App;

/// <summary>
/// WPF application shell for UefiAccess (Alpha 1). The manifest requests administrator
/// elevation. The application owns the Serilog logger (ProgramData\UefiAccess\logs, or the
/// per-user %LOCALAPPDATA%\UefiAccess\logs when Logging moved the log there; no console;
/// mirrored into the log panel from Information upwards), names the folder the log really is in
/// (<see cref="RunLogFolder"/>) in its start-up line and in the crash message before the window
/// is shown, with a WRN line in the log panel when that is the per-user folder, creates the main
/// window, composes the
/// <see cref="ShellViewModel"/> over the WPF platform services (announcer, focus sink, dialogs and
/// clipboard, and the dispatcher scheduler of the detail pane) and attaches it as the window's
/// data context. Nothing is loaded at start: the window opens idle, announces the Ready sentence
/// once from its Loaded handler, and the first load is the user's Refresh or Open baseline folder
/// (hard rule 4: focus moves and announcements only follow a user action; opening the application
/// is that action for the Ready sentence).
/// Unhandled UI-thread exceptions are logged and announced in the Status box instead of crashing;
/// one that happens before the window is on screen ends the process, so no elevated process is
/// left running without a window.
/// </summary>
/// <remarks>
/// The input trace (Alpha 2 finding A2-7, the tester's question about the continued A1-6 issue) is off
/// unless the application is started with --trace-input, the one command-line argument it reads
/// (<see cref="InputTraceSwitch"/>; any other argument changes nothing). With it, the key presses,
/// focus moves, selection changes, the detail pane's scheduling and the UI Automation events the
/// application raises itself are written to trace-input-&lt;yyyyMMdd-HHmmss&gt;.log in the folder of
/// this run's log, and an INFO line in the log panel names the file (a log line, not an
/// announcement: the trace adds nothing that is spoken). The file is flushed and closed at exit,
/// or when an unhandled exception ends the process.
/// Without the switch no hook is attached, no file is created and no thread is started.
/// </remarks>
public partial class App : Application
{
    private readonly ILogger _fileLogger;
    private readonly LogPanelSink _panelSink;
    private readonly RunLogFolder _logFolder;
    private WpfAnnouncer? _announcer;
    private WpfPlatformInteractions? _platform;
    private bool _mainWindowShown;
    private IInputTrace _trace = NullInputTrace.Instance;
    private WpfInputTraceHooks? _traceHooks;

    /// <summary>
    /// Access keys need Alt. By default WPF's AccessKeyManager also fires a mnemonic on a plain
    /// letter whenever the focused control leaves the text input unhandled (a group header, a
    /// button, a splitter, the focusable detail heading, every read-only text box), so typing
    /// "r" on a group header would run Refresh. The compatibility switch makes the window scope
    /// require Alt, as docs/Keyboard-Map.md section 3 promises; it must be set before WPF reads
    /// it, which is why it is in the type initializer.
    /// </summary>
    static App()
    {
        CoreCompatibilityPreferences.IsAltKeyRequiredInAccessKeyDefaultScope = true;
    }

    /// <summary>
    /// Creates the application and its logger, and finds the folder the log is in (the shared
    /// folder, or the per-user one when Logging moved it there). Neither step throws.
    /// </summary>
    public App()
    {
        _fileLogger = Logging.Create("app", console: false);
        _logFolder = RunLogFolder.Find(_fileLogger);
        _panelSink = new LogPanelSink(Dispatcher);
        Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Logger(_fileLogger)
            .WriteTo.Sink(_panelSink)
            .CreateLogger();
    }

    /// <summary>Logger shared by the window, the shell and the Core services it composes: the daily log file plus the log panel.</summary>
    public ILogger Logger { get; }

    /// <summary>The composed shell, or null before startup.</summary>
    public ShellViewModel? Shell { get; private set; }

    /// <summary>
    /// Tool version: the shared <see cref="ToolVersion.Current"/> ("0.1.0+&lt;commit&gt;"), the same
    /// string the CLI prints as "Tool version" and writes into index.json and gate.json.
    /// </summary>
    public static string Version => ToolVersion.Current;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        Logger.Information(
            "UefiAccess.App {Version} starting on {Os}, 64-bit process {Is64Bit}, log directory {LogDirectory}",
            Version,
            Environment.OSVersion.VersionString,
            Environment.Is64BitProcess,
            _logFolder.Directory);

        // When the log is in the per-user folder, the log panel says why right after the version
        // line. The note goes to the panel only: the per-user log already starts with it. The
        // panel-only logger is not disposed, because disposing a logger disposes its sinks, and
        // the application's logger keeps writing to this one.
        ILogger panelOnly = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(_panelSink).CreateLogger();
        _logFolder.WriteNote(panelOnly);

        // The input trace, only with --trace-input: one file next to this run's log, named in the
        // log panel by an INFO line (never announced: the trace must add no speech).
        InputTraceStart traceStart = InputTraceFile.Start(e.Args, _logFolder.Directory, Logger);
        _trace = traceStart.Trace;
        if (traceStart.FilePath is not null)
        {
            Logger.Information("Input trace on: key presses, focus, selection, the detail pane's scheduling and the UI Automation events this application raises are written to {TracePath}", traceStart.FilePath);
        }
        else if (traceStart.Requested)
        {
            Logger.Warning("Input trace not started: {Reason}", traceStart.Refusal);
        }

        base.OnStartup(e);

        MainWindow window = new();
        _announcer = new WpfAnnouncer(window.StatusBox, Logger, _trace);
        WpfFocusSink focus = new WpfFocusSink(window, Logger);
        WpfPlatformInteractions platform = new WpfPlatformInteractions(() => this.MainWindow, Logger);
        _platform = platform;
        DispatcherDetailScheduler detailScheduler = new DispatcherDetailScheduler(window.Dispatcher, Logger) { InputTrace = _trace };

        // The shell announces nothing yet: the window's Loaded handler calls AnnounceReady once
        // the window is on screen, so the Ready sentence reaches a Status box whose automation
        // peer exists. Attaching the panel sink writes the lines logged so far (the "starting"
        // line above) into the log panel before anything else reaches it. The detail scheduler
        // takes the rebuild of the detail pane out of the key press that changed the selection
        // (A1-6); the window holds it while a key is down in the list, so it gets it too.
        Shell = ShellViewModel.CreateWindows(_announcer, focus, platform, Logger, detailScheduler);
        Shell.InputTrace = _trace;
        _panelSink.Attach(Shell.Log);
        window.DetailScheduler = detailScheduler;
        window.Attach(Shell);
        MainWindow = window;
        if (_trace.IsEnabled)
        {
            _traceHooks = WpfInputTraceHooks.Attach(window, _trace);
        }

        window.Show();
        _mainWindowShown = true;
    }

    /// <summary>
    /// Windows is ending the session (log off, shut down or restart). WPF does not let a cancelled
    /// Closing keep the window open then, so the window's own refusal (<see cref="ShellViewModel.TryClose"/>
    /// from its Closing handler) would not hold: while a change is written the session end is
    /// cancelled here instead and Status says "A change is being written; wait for its result.", so
    /// Windows asks the person before it goes on rather than ending the process between the suspend
    /// and the record. Any other time the session ends as usual.
    /// </summary>
    /// <param name="e">The session-ending event; cancelled while a change is written.</param>
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (Shell is not null && !Shell.TryClose())
        {
            e.Cancel = true;
            Logger.Debug("Windows asked to end the session ({Reason}) while a change was written; the end of the session was cancelled", e.ReasonSessionEnding);
        }

        base.OnSessionEnding(e);
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        Logger.Information("UefiAccess.App exiting with code {ExitCode}", e.ApplicationExitCode);

        // A2-1: copies are placed unflushed; the last one is rendered now, so it outlives the process.
        _platform?.FlushClipboard();

        // The input trace's hooks come off first, then its file is completed, flushed and closed.
        _traceHooks?.Detach();
        (_trace as IDisposable)?.Dispose();
        (Logger as IDisposable)?.Dispose();
        (_fileLogger as IDisposable)?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// An exception reached the dispatcher. Once the window is on screen it is logged and
    /// announced in Status, and the application carries on (the shell's state and list are
    /// untouched). Before that (the window's constructor, the composition or Show threw) there is
    /// no usable window and nothing would ever close the elevated process, so the failure is shown
    /// in a message box and the application shuts down with exit code 1.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Error(e.Exception, "Unhandled exception on the UI thread");
        e.Handled = true;
        AnnounceFailure(e.Exception);
        if (!_mainWindowShown)
        {
            Logger.Fatal("The main window could not be shown; shutting down");
            Shutdown(1);
        }
    }

    /// <summary>
    /// An exception reached no handler on some thread. It is logged; when it ends the process,
    /// OnExit never runs, so the input trace (when it is on) says so and is closed here: what its
    /// writer has queued is written and flushed before the process ends. Closing it only joins the
    /// writer's thread, so it is safe on any thread, and a second close at exit does nothing.
    /// </summary>
    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            Logger.Fatal(exception, "Unhandled exception (terminating: {IsTerminating})", e.IsTerminating);
        }
        else
        {
            Logger.Fatal("Unhandled non-exception object {Object} (terminating: {IsTerminating})", e.ExceptionObject, e.IsTerminating);
        }

        if (e.IsTerminating)
        {
            try
            {
                if (_trace.IsEnabled)
                {
                    _trace.Write(TraceKind.Trace, "input trace: an unhandled " + (e.ExceptionObject?.GetType().Name ?? "exception") + " ends the process");
                }

                (_trace as IDisposable)?.Dispose();
            }
            catch (Exception)
            {
                // Nothing may stand in the way of the process ending.
            }
        }
    }

    /// <summary>
    /// Announces a failure as "Unexpected error: &lt;message&gt;. Details are in the log." in the
    /// Status box (the polite live region) so it is heard without a focus move; before the window
    /// is on screen a stock message box is the only channel left. Not "Loading failed": the
    /// exceptions that reach here are never a load (the shell catches those itself), and the
    /// wording must not send the tester looking for a load problem that did not happen.
    /// </summary>
    private void AnnounceFailure(Exception exception)
    {
        string text = string.Format(CultureInfo.InvariantCulture, Strings.UnexpectedErrorFormat, exception.Message);
        try
        {
            if (_announcer is not null && _mainWindowShown)
            {
                _announcer.Announce(text);
                return;
            }

            MessageBox.Show(text + Environment.NewLine + Environment.NewLine + _logFolder.Directory, Strings.WindowTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Announcing the failure failed");
        }
    }
}
