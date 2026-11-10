// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The input trace (UefiAccess.App --trace-input; Alpha 2 finding A2-7, the tester's question about the
/// continued A1-6 issue) as the sources compose it. The App cannot be loaded by this test project,
/// so, like <see cref="WpfShellAccessibilityTests"/>, these checks read its files; the behaviour
/// behind them (the switch, the file, the writer, the shell's lines) is unit-tested in Core and
/// Presentation. What is pinned: the application reads its command line only for the switch; the
/// window's hooks are attached only when the trace is on and come off at exit before the file is
/// closed and before the loggers; the trace's own line in the log panel is a log line, never an
/// announcement; the hooks only read and write lines (they mark nothing handled, move no focus,
/// say nothing, raise no automation event and create no peer, and read no text box or field
/// value); every handler they add is removed again; every kind the tester asked for is written where it
/// happens, in the order it happens (a row's selection from its container, before the list raises
/// SelectionChanged; the shell's SELECTION line before the scheduling it causes; the quiet period
/// in the line of the step that started it; a raise stamped before the call); the trace file is
/// closed when an unhandled exception ends the process; the trace adds no timed behaviour (its
/// writer is a blocking consumer in Core, and its idle probes are one-off dispatcher operations
/// queued only while tracing); the files the trace must leave alone do not mention it; and the
/// one UIA-RAISED "raising" line per announcement stands for the only LiveRegionChanged raise of
/// the application, made once per announcement (M4-H4.14).
/// </summary>
public sealed class InputTraceStaticTests
{
    private static readonly string[] TimedForms = { "DispatcherTimer", "System.Timers", "System.Threading.Timer", "Task.Delay", "Thread.Sleep", "PeriodicTimer" };

    [Fact]
    public void TheApplicationReadsItsCommandLineOnlyForTheSwitch()
    {
        string app = Source("src", "UefiAccess.App", "App.xaml.cs");
        Assert.Contains("InputTraceStart traceStart = InputTraceFile.Start(e.Args, _logFolder.Directory, Logger);", app, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(app, "e.Args"));
        Assert.Equal(1, CountOf(app, "InputTraceFile.Start("));

        foreach (string file in RepositoryPaths.Enumerate("src/UefiAccess.App", "*.cs").Concat(RepositoryPaths.Enumerate("src/UefiAccess.Presentation", "*.cs")))
        {
            string source = File.ReadAllText(file);
            Assert.False(source.Contains("GetCommandLineArgs", StringComparison.Ordinal), RepositoryPaths.Relative(file) + " reads the command line itself");
            bool isSwitch = file.EndsWith(Path.Combine("Diagnostics", "InputTraceSwitch.cs"), StringComparison.Ordinal);
            Assert.True(isSwitch || !source.Contains("\"--trace-input\"", StringComparison.Ordinal), RepositoryPaths.Relative(file) + " spells the switch instead of InputTraceSwitch.Name");
        }

        // The trace is composed into the announcer, the scheduler and the shell, without changing
        // the composition the accessibility tests pin.
        string startup = Method(app, "protected override void OnStartup(");
        Assert.Contains("_announcer = new WpfAnnouncer(window.StatusBox, Logger, _trace);", startup, StringComparison.Ordinal);
        Assert.Contains("new DispatcherDetailScheduler(window.Dispatcher, Logger) { InputTrace = _trace };", startup, StringComparison.Ordinal);
        Assert.Contains("ShellViewModel.CreateWindows(_announcer, focus, platform, Logger, detailScheduler);", startup, StringComparison.Ordinal);
        Assert.True(
            startup.IndexOf("ShellViewModel.CreateWindows(", StringComparison.Ordinal) < startup.IndexOf("Shell.InputTrace = _trace;", StringComparison.Ordinal),
            "the shell gets the trace right after it is composed");
        Assert.True(
            startup.IndexOf("_logFolder.WriteNote(panelOnly);", StringComparison.Ordinal) < startup.IndexOf("InputTraceFile.Start(", StringComparison.Ordinal),
            "the trace starts after the version line and the log folder's note");
        Assert.Contains("private IInputTrace _trace = NullInputTrace.Instance;", app, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHooksAreAttachedOnlyWhenTheTraceIsOn_BeforeTheWindowIsShown()
    {
        string startup = Method(Source("src", "UefiAccess.App", "App.xaml.cs"), "protected override void OnStartup(");
        Assert.Equal(1, CountOf(startup, "WpfInputTraceHooks.Attach("));
        Assert.Contains(
            "if (_trace.IsEnabled)\n        {\n            _traceHooks = WpfInputTraceHooks.Attach(window, _trace);\n        }",
            startup,
            StringComparison.Ordinal);
        Assert.True(
            startup.IndexOf("window.Attach(Shell);", StringComparison.Ordinal) < startup.IndexOf("WpfInputTraceHooks.Attach(", StringComparison.Ordinal)
            && startup.IndexOf("WpfInputTraceHooks.Attach(", StringComparison.Ordinal) < startup.IndexOf("window.Show();", StringComparison.Ordinal),
            "the hooks attach to the composed window before it is shown");
        Assert.Equal(1, CountOf(Source("src", "UefiAccess.App", "App.xaml.cs"), "_traceHooks ="));
    }

    [Fact]
    public void AtExit_TheHooksComeOff_ThenTheTraceFileIsClosed_ThenTheLoggers()
    {
        string onExit = Method(Source("src", "UefiAccess.App", "App.xaml.cs"), "protected override void OnExit(");
        int flush = onExit.IndexOf("_platform?.FlushClipboard();", StringComparison.Ordinal);
        int detach = onExit.IndexOf("_traceHooks?.Detach();", StringComparison.Ordinal);
        int close = onExit.IndexOf("(_trace as IDisposable)?.Dispose();", StringComparison.Ordinal);
        int logger = onExit.IndexOf("(Logger as IDisposable)?.Dispose();", StringComparison.Ordinal);
        int fileLogger = onExit.IndexOf("(_fileLogger as IDisposable)?.Dispose();", StringComparison.Ordinal);
        Assert.True(flush >= 0 && detach > flush && close > detach && logger > close && fileLogger > logger, "OnExit: clipboard, hooks, trace file, loggers");
    }

    [Fact]
    public void AnUnhandledExceptionThatEndsTheProcess_ClosesTheTraceFile()
    {
        // OnExit does not run then: the queued lines are written and the file flushed here.
        string handler = Method(Source("src", "UefiAccess.App", "App.xaml.cs"), "private void OnUnhandledException(");
        int terminating = handler.IndexOf("if (e.IsTerminating)", StringComparison.Ordinal);
        int guarded = handler.IndexOf("try", terminating, StringComparison.Ordinal);
        int close = handler.IndexOf("(_trace as IDisposable)?.Dispose();", StringComparison.Ordinal);
        int caught = handler.IndexOf("catch (Exception)", StringComparison.Ordinal);
        Assert.True(terminating >= 0 && guarded > terminating && close > guarded && caught > close, "a terminating exception closes the trace inside a try");
        Assert.True(handler.IndexOf("Logger.Fatal(", StringComparison.Ordinal) < terminating, "the exception is logged first");
        Assert.DoesNotContain("_traceHooks", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTracesLineInTheLogPanel_IsALogLine_NeverAnAnnouncement()
    {
        string startup = Method(Source("src", "UefiAccess.App", "App.xaml.cs"), "protected override void OnStartup(");
        Assert.Contains("Logger.Information(\"Input trace on: ", startup, StringComparison.Ordinal);
        Assert.Contains("{TracePath}\", traceStart.FilePath);", startup, StringComparison.Ordinal);
        Assert.Contains("Logger.Warning(\"Input trace not started: {Reason}\", traceStart.Refusal);", startup, StringComparison.Ordinal);
        Assert.DoesNotContain("Announce(", startup, StringComparison.Ordinal);
        Assert.DoesNotContain("_announcer.", startup, StringComparison.Ordinal);

        // The version line stays the first information line of the start-up (RunLogFolderTests).
        Assert.True(
            startup.IndexOf("UefiAccess.App {Version} starting", StringComparison.Ordinal) < startup.IndexOf("Input trace on: ", StringComparison.Ordinal),
            "the trace's line follows the version line");
    }

    [Fact]
    public void InputManagerAndTheIdleProbes_BelongToTheTraceAlone()
    {
        foreach (string file in RepositoryPaths.Enumerate("src/UefiAccess.App", "*.cs").Concat(RepositoryPaths.Enumerate("src/UefiAccess.Presentation", "*.cs")))
        {
            string relative = RepositoryPaths.Relative(file);
            string source = File.ReadAllText(file);
            if (relative == "src/UefiAccess.App/Platform/WpfInputTraceHooks.cs")
            {
                Assert.Contains("InputManager.Current", source, StringComparison.Ordinal);
                Assert.Equal(1, CountOf(source, "DispatcherPriority.ApplicationIdle"));
                continue;
            }

            Assert.False(source.Contains("InputManager", StringComparison.Ordinal), relative + " uses the InputManager");
            if (relative == "src/UefiAccess.App/Platform/DispatcherDetailScheduler.cs")
            {
                Assert.Equal(1, CountOf(source, "DispatcherPriority.ApplicationIdle"));
                Assert.Contains("DispatcherPriority.ApplicationIdle", Method(source, "private Action TraceServed("), StringComparison.Ordinal);
                continue;
            }

            Assert.False(source.Contains("DispatcherPriority.ApplicationIdle", StringComparison.Ordinal), relative + " queues work at ApplicationIdle priority");
        }

        // The probe of the hooks is one operation at a time, and the scheduler's wraps the idle turn only while tracing.
        string hooks = Source("src", "UefiAccess.App", "Platform", "WpfInputTraceHooks.cs");
        string queue = Method(hooks, "private void QueueIdleProbe(");
        Assert.True(
            queue.IndexOf("if (_idleQueued)", StringComparison.Ordinal) < queue.IndexOf("BeginInvoke(DispatcherPriority.ApplicationIdle", StringComparison.Ordinal),
            "an idle probe is queued only when none is queued");
        string scheduler = Source("src", "UefiAccess.App", "Platform", "DispatcherDetailScheduler.cs");
        Assert.Contains("work = TraceServed(work);", Method(scheduler, "private Action QueueAtIdle("), StringComparison.Ordinal);
        Assert.StartsWith(
            "private Action TraceServed(Action work)\n    {\n        if (!_inputTrace.IsEnabled)\n        {\n            return work;\n        }",
            Method(scheduler, "private Action TraceServed("),
            StringComparison.Ordinal);
    }

    [Fact]
    public void EveryHandlerTheHooksAdd_IsRemovedAgain()
    {
        string hooks = Source("src", "UefiAccess.App", "Platform", "WpfInputTraceHooks.cs");
        string subscribe = Method(hooks, "private void Subscribe(");
        string detach = Method(hooks, "public void Detach(");

        // The input manager's two events and the window's Loaded, Activated and Deactivated; the
        // shell's selection is no longer subscribed (the shell writes its own SELECTION line).
        MatchCollection added = Regex.Matches(subscribe, @"(?<target>[\w.]+)\s*\+=\s*(?<handler>\w+);", RegexOptions.CultureInvariant);
        Assert.Equal(5, added.Count);
        foreach (Match match in added)
        {
            string removal = match.Groups["target"].Value + " -= " + match.Groups["handler"].Value + ";";
            Assert.Contains(removal, detach, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("_shell", hooks, StringComparison.Ordinal);

        // Focus got and lost, and a row's container selected and unselected.
        MatchCollection handlers = Regex.Matches(subscribe, @"(?<target>[\w.]+)\.AddHandler\((?<event>[\w.]+), (?<handler>\w+), true\);", RegexOptions.CultureInvariant);
        Assert.Equal(4, handlers.Count);
        foreach (Match match in handlers)
        {
            string removal = match.Groups["target"].Value + ".RemoveHandler(" + match.Groups["event"].Value + ", " + match.Groups["handler"].Value + ");";
            Assert.Contains(removal, detach, StringComparison.Ordinal);
        }

        Assert.Equal(CountOf(hooks, "+="), CountOf(hooks, "-="));
        Assert.Equal(CountOf(hooks, ".AddHandler("), CountOf(hooks, ".RemoveHandler("));
    }

    [Theory]
    [InlineData("WpfInputTraceHooks.cs")]
    [InlineData("WpfTraceText.cs")]
    public void TheHooksOnlyReadAndWriteLines(string file)
    {
        string source = Source("src", "UefiAccess.App", "Platform", file);
        string[] forbidden =
        {
            "e.Handled =", ".Handled = true", "Focus(", "Announce(", "RaiseAutomationEvent", "RaisePropertyChangedEvent", "RaiseNotificationEvent",
            "CreatePeerForElement", "HexDump", "CopyValueText", "DetailFieldViewModel", "SetValue(", "SetCurrentValue(", "SelectedItem =",
        };
        foreach (string text in forbidden)
        {
            Assert.False(source.Contains(text, StringComparison.Ordinal), file + " contains " + text);
        }

        // No text box value is read: ".Text" as a member (".TextBox" or "TextAlignment" are no reads).
        Assert.DoesNotMatch(new Regex(@"\.Text\b", RegexOptions.CultureInvariant), source);
        Assert.DoesNotMatch(new Regex(@"\.Value\b", RegexOptions.CultureInvariant), source);
    }

    [Fact]
    public void TheHooksCatchEveryFailure()
    {
        string hooks = Source("src", "UefiAccess.App", "Platform", "WpfInputTraceHooks.cs");
        foreach (string handler in new[]
        {
            "private void OnPreNotifyInput(", "private void OnPostProcessInput(", "private void OnIdle(", "private void OnFocusGot(", "private void OnFocusLost(",
            "private void OnRowSelectionChanged(", "private void OnWindowLoaded(", "private void OnWindowActivated(", "private void OnWindowDeactivated(",
        })
        {
            string body = Method(hooks, handler);
            Assert.Contains("try", body, StringComparison.Ordinal);
            Assert.Contains("catch (Exception ex)", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheTraceAddsNoTimedBehaviour_ItsWriterIsABlockingConsumer()
    {
        string[] coreFiles = { "TraceLine.cs", "TraceRedactor.cs", "TraceFileWriter.cs" };
        foreach (string file in coreFiles)
        {
            string source = Source("src", "UefiAccess.Core", "Diagnostics", file);
            foreach (string form in TimedForms)
            {
                Assert.False(source.Contains(form, StringComparison.Ordinal), file + " uses " + form);
            }

            Assert.False(source.Contains("TryTake(", StringComparison.Ordinal), file + " takes from its queue with a time limit");
            Assert.False(source.Contains("SpinWait", StringComparison.Ordinal), file + " spins");
            Assert.DoesNotMatch(new Regex(@"(?<!string\.)\bJoin\((?!\))", RegexOptions.CultureInvariant), source);
            Assert.DoesNotMatch(new Regex(@"\bWait\(\s*[^)\s]", RegexOptions.CultureInvariant), source);
        }

        string writer = Source("src", "UefiAccess.Core", "Diagnostics", "TraceFileWriter.cs");
        Assert.Contains("_queue.GetConsumingEnumerable()", writer, StringComparison.Ordinal);
        Assert.Contains("_consumer.Join();", writer, StringComparison.Ordinal);
        Assert.Contains("IsBackground = true", writer, StringComparison.Ordinal);
        Assert.Contains("Priority = ThreadPriority.BelowNormal", writer, StringComparison.Ordinal);
        Assert.Contains("Mode = FileMode.CreateNew", writer, StringComparison.Ordinal);
        Assert.DoesNotContain("FileMode.Create,", writer, StringComparison.Ordinal);
        Assert.DoesNotContain("FileMode.Append", writer, StringComparison.Ordinal);
        Assert.DoesNotContain("FileMode.OpenOrCreate", writer, StringComparison.Ordinal);

        // The application's and the shell's side of the trace start no thread and no task.
        foreach (string file in new[]
        {
            Path.Combine("src", "UefiAccess.App", "Platform", "WpfInputTraceHooks.cs"),
            Path.Combine("src", "UefiAccess.App", "Platform", "WpfTraceText.cs"),
            Path.Combine("src", "UefiAccess.Presentation", "Diagnostics", "InputTraceFile.cs"),
            Path.Combine("src", "UefiAccess.Presentation", "Diagnostics", "RecordingInputTrace.cs"),
        })
        {
            string source = File.ReadAllText(Path.Combine(RepositoryPaths.Root, file));
            foreach (string form in TimedForms.Concat(new[] { "new Thread(", "Task.Run(", "ThreadPool." }))
            {
                Assert.False(source.Contains(form, StringComparison.Ordinal), file + " uses " + form);
            }
        }
    }

    [Fact]
    public void TheAnnouncerAndTheShellWriteOnlyWhileTheTraceIsOn()
    {
        string announcer = Source("src", "UefiAccess.App", "Platform", "WpfAnnouncer.cs");
        Assert.Equal(CountOf(announcer, "_trace.Write("), CountOf(announcer, "if (_trace.IsEnabled)"));

        // The raise is stamped before the call (when the event leaves the application; a client
        // reads the Status box when it handles the event, which can be seconds later) and its
        // duration written after it.
        string raise = Method(announcer, "private void RaiseLiveRegionChanged(");
        int stamp = raise.IndexOf("raising = Stopwatch.GetTimestamp();", StringComparison.Ordinal);
        int raising = raise.IndexOf("\"LiveRegionChanged raising on the Status box\"", StringComparison.Ordinal);
        int call = raise.IndexOf("AutomationInteropProvider.RaiseAutomationEvent(", StringComparison.Ordinal);
        int raised = raise.IndexOf("\"LiveRegionChanged raised on the Status box; the call returned after \" + TraceLine.Milliseconds(raising, Stopwatch.GetTimestamp())", StringComparison.Ordinal);
        Assert.True(stamp >= 0 && raising > stamp && call > raising && raised > call, "UIA-RAISED: stamped and written before the raise, its duration after it");
        Assert.Equal(5, CountOf(raise, "TraceKind.UiaRaised"));

        string shell = Source("src", "UefiAccess.Presentation", "ViewModels", "ShellViewModel.cs");
        Assert.Contains("public void EnsureDetailCurrent([CallerMemberName] string caller = \"\")", shell, StringComparison.Ordinal);
        string show = Method(shell, "private void ShowSelectedRow(");
        Assert.StartsWith(
            "private void ShowSelectedRow(string step)\n    {\n        if (!_inputTrace.IsEnabled)\n        {\n            Detail.Show(List.SelectedItem);\n            return;\n        }",
            show,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Detail.HexDump", show, StringComparison.Ordinal);
        Assert.DoesNotContain("CopyValueText", show, StringComparison.Ordinal);

        // A rebuild that throws ends its "started" line with a "failed" one and goes on as before.
        int failed = show.IndexOf("step + \" failed after \"", StringComparison.Ordinal);
        Assert.True(failed > show.IndexOf("catch (Exception ex)", StringComparison.Ordinal) && show.IndexOf("throw;", failed, StringComparison.Ordinal) > failed, "a failed rebuild is traced and rethrown");

        // The shell's SELECTION line comes first in its selection handler, before the scheduling.
        string selection = Method(shell, "private void OnSelectionChanged(");
        int traced = selection.IndexOf("if (_inputTrace.IsEnabled)", StringComparison.Ordinal);
        int written = selection.IndexOf("_inputTrace.Write(TraceKind.Selection, \"shell: \"", StringComparison.Ordinal);
        int scheduled = selection.IndexOf("_detailScheduler.Schedule(RefreshDetail);", StringComparison.Ordinal);
        Assert.True(traced >= 0 && written > traced && scheduled > written, "the shell's SELECTION line precedes the scheduling it causes");
        Assert.Equal(1, CountOf(shell, "List.SelectionChanged += OnSelectionChanged;"));
        string ensure = Method(shell, "public void EnsureDetailCurrent(");
        Assert.True(
            ensure.IndexOf("if (_inputTrace.IsEnabled)", StringComparison.Ordinal) < ensure.IndexOf("_inputTrace.Write(", StringComparison.Ordinal),
            "EnsureDetailCurrent writes its line only while tracing");

        // A load's service tag is registered for redaction before anything of the load is shown.
        string showResult = Method(shell, "private void ShowResult(");
        int redact = showResult.IndexOf("_inputTrace.Redact(result.Identity?.ServiceTag);", StringComparison.Ordinal);
        Assert.True(redact >= 0 && redact < showResult.IndexOf("List.Load(", StringComparison.Ordinal) && redact < showResult.IndexOf("EnsureDetailCurrent();", StringComparison.Ordinal));
    }

    [Fact]
    public void TheHooksAndTheAnnouncerWriteEveryKind()
    {
        string hooks = Source("src", "UefiAccess.App", "Platform", "WpfInputTraceHooks.cs");
        string pre = Method(hooks, "private void OnPreNotifyInput(");
        Assert.Contains("routed == Keyboard.PreviewKeyDownEvent", pre, StringComparison.Ordinal);
        Assert.Contains("OnKeyDown(keyDown);", pre, StringComparison.Ordinal);
        Assert.Contains("routed == Keyboard.PreviewKeyUpEvent", pre, StringComparison.Ordinal);
        Assert.Contains("OnKeyUp(keyUp);", pre, StringComparison.Ordinal);

        foreach ((string signature, string kind) in new[]
        {
            ("private void OnKeyDown(", "TraceKind.Key"),
            ("private void OnKeyUp(", "TraceKind.Key"),
            ("private void OnPostProcessInput(", "TraceKind.Key"),
            ("private void OnFocusGot(", "TraceKind.Focus"),
            ("private void OnFocusGot(", "TraceKind.Selection"),
            ("private void OnFocusLost(", "TraceKind.Focus"),
            ("private void OnRowSelectionChanged(", "TraceKind.Selection"),
            ("private void OnIdle(", "TraceKind.Idle"),
            ("private void WriteListeners(", "TraceKind.Listeners"),
            ("private void WriteListeners(", "AutomationPeer.ListenerExists("),
        })
        {
            Assert.Contains(kind, Method(hooks, signature), StringComparison.Ordinal);
        }

        Assert.Contains("QueueIdleProbe(press);", Method(hooks, "private void OnKeyDown("), StringComparison.Ordinal);
        Assert.Contains("WriteListeners(\"at start\", always: true);", Method(hooks, "private void Subscribe("), StringComparison.Ordinal);
        Assert.Contains("WriteListeners(\"at exit\", always: true);", Method(hooks, "public void Detach("), StringComparison.Ordinal);

        // A KEY line says how long the key's message waited before WPF took it (its message time
        // against the tick count), and the IDLE line also counts from the key's message time.
        Assert.Contains("unchecked(Environment.TickCount - e.Timestamp)", Method(hooks, "private static int? Queued("), StringComparison.Ordinal);
        Assert.Contains("\" queued=\" + QueuedText(queued)", Method(hooks, "private void OnKeyDown("), StringComparison.Ordinal);
        Assert.Contains("\" queued=\" + QueuedText(queued)", Method(hooks, "private void OnKeyUp("), StringComparison.Ordinal);
        Assert.Contains("was pressed (its message time)", Method(hooks, "private void OnIdle("), StringComparison.Ordinal);

        string announcer = Source("src", "UefiAccess.App", "Platform", "WpfAnnouncer.cs");
        Assert.Contains("TraceKind.Announce", Method(announcer, "private void Apply("), StringComparison.Ordinal);
        Assert.Equal(5, CountOf(Method(announcer, "private void RaiseLiveRegionChanged("), "TraceKind.UiaRaised"));
    }

    /// <summary>
    /// M4-H4.14 (2 October 2026): Narrator said some Status sentences two or three times and others
    /// not at all, while the input trace of the A/B runs showed one LiveRegionChanged raised per
    /// sentence (none while the Status box itself had focus, by design), on main 330d69c (which
    /// holds alpha1.4) and on m4-hw0 alike. The repeats come from the receiving side (a late event
    /// is read with a newer sentence; after the Open baseline folder dialog most events of the
    /// window, the Status box's among them, reach another process a second time, without a value).
    /// Pinned so that it stays one: the event is raised by one call, in WpfAnnouncer's
    /// RaiseLiveRegionChanged, which no loop repeats; an announcement reaches that method once
    /// (Announce runs Apply once, marshalled to the box's thread or not, and Apply calls it once);
    /// and no other file of the App or the Presentation raises LiveRegionChanged or any other
    /// automation event. A second raise per announcement fails here.
    /// </summary>
    [Fact]
    public void LiveRegionChanged_IsRaisedByOneCall_OncePerAnnouncement_AndNowhereElse()
    {
        string announcer = Source("src", "UefiAccess.App", "Platform", "WpfAnnouncer.cs");
        string raise = Method(announcer, "private void RaiseLiveRegionChanged(");
        Assert.Equal(1, CountOf(raise, "AutomationInteropProvider.RaiseAutomationEvent("));
        Assert.Equal(1, CountOf(announcer, "RaiseAutomationEvent("));
        Assert.Contains(
            "AutomationInteropProvider.RaiseAutomationEvent(\n                AutomationElementIdentifiers.LiveRegionChangedEvent,\n                provider,\n                new AutomationEventArgs(AutomationElementIdentifiers.LiveRegionChangedEvent));",
            raise,
            StringComparison.Ordinal);

        // The event and its arguments name LiveRegionChanged; nothing else in the file does.
        Assert.Equal(2, CountOf(raise, "LiveRegionChangedEvent"));
        Assert.Equal(2, CountOf(announcer, "LiveRegionChangedEvent"));
        Assert.DoesNotContain("AutomationEvents.", announcer, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"\b(for|foreach|while)\s*\(|\bdo\s*\{", RegexOptions.CultureInvariant), raise);

        // An announcement reaches the raise once.
        Assert.Equal(2, CountOf(announcer, "RaiseLiveRegionChanged("));
        Assert.Equal(1, CountOf(Method(announcer, "private void Apply("), "RaiseLiveRegionChanged(textChanged);"));
        string announce = Method(announcer, "public void Announce(");
        Assert.Contains("_liveRegion.Dispatcher.Invoke(Apply);\n            return;\n        }\n\n        Apply();", announce, StringComparison.Ordinal);
        Assert.Equal(2, CountOf(announce, "Apply"));
        Assert.Equal(1, CountOf(announcer, "Invoke(Apply)"));

        // The only raise of the application.
        string[] raises = { "LiveRegionChangedEvent", "AutomationEvents.LiveRegionChanged", "RaiseAutomationEvent(" };
        foreach (string file in RepositoryPaths.Enumerate("src/UefiAccess.App", "*.cs").Concat(RepositoryPaths.Enumerate("src/UefiAccess.Presentation", "*.cs")))
        {
            string relative = RepositoryPaths.Relative(file);
            if (relative == "src/UefiAccess.App/Platform/WpfAnnouncer.cs")
            {
                continue;
            }

            string source = File.ReadAllText(file);
            foreach (string text in raises)
            {
                Assert.False(source.Contains(text, StringComparison.Ordinal), relative + " contains " + text);
            }
        }
    }

    [Fact]
    public void ARowsSelection_IsTracedFromItsContainer_BeforeTheListRaisesSelectionChanged()
    {
        // A row's container changes IsSelected (Selector.SelectedEvent, UnselectedEvent) before the
        // list raises SelectionChanged, whose handler in the window hands the row to the shell; a
        // handler on SelectionChanged at the groups control would run after all that.
        string hooks = Source("src", "UefiAccess.App", "Platform", "WpfInputTraceHooks.cs");
        string subscribe = Method(hooks, "private void Subscribe(");
        Assert.Contains("_window.GroupsControl.AddHandler(Selector.SelectedEvent, _rowSelectionHandler, true);", subscribe, StringComparison.Ordinal);
        Assert.Contains("_window.GroupsControl.AddHandler(Selector.UnselectedEvent, _rowSelectionHandler, true);", subscribe, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectionChangedEvent", hooks, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectionChanged +=", hooks, StringComparison.Ordinal);

        string row = Method(hooks, "private void OnRowSelectionChanged(");
        Assert.Contains("e.OriginalSource is not ListViewItem { DataContext: VariableItemViewModel row } item", row, StringComparison.Ordinal);
        Assert.Contains("ItemsControl.ItemsControlFromItemContainer(item)", row, StringComparison.Ordinal);
        Assert.Contains("e.RoutedEvent == Selector.SelectedEvent ? \"selected\" : \"unselected\"", row, StringComparison.Ordinal);

        // Deactivation: the window's own handler (the release) runs first, and the line says so.
        Assert.Contains("written after the window's own handler", Method(hooks, "private void OnWindowDeactivated("), StringComparison.Ordinal);
    }

    [Fact]
    public void TheDetailScheduler_TracesEveryStepAroundTheCallItMakes()
    {
        string scheduler = Source("src", "UefiAccess.App", "Platform", "DispatcherDetailScheduler.cs");
        foreach ((string method, string call, string step) in new[]
        {
            ("public void Schedule(", "_policy.Schedule(refresh);", "\"schedule (selection changed)\""),
            ("public void Flush(", "_policy.Flush();", "\"flush\""),
            ("public void Hold(", "_policy.Hold();", "\"hold\""),
            ("public void Release(", "_policy.Release();", "\"release\""),
        })
        {
            string body = Method(scheduler, method);
            int before = body.IndexOf("SchedulingState? before = TraceBefore();", StringComparison.Ordinal);
            int made = body.IndexOf(call, StringComparison.Ordinal);
            int after = body.IndexOf("TraceAfter(" + step + ", before);", StringComparison.Ordinal);
            Assert.True(before >= 0 && made > before && after > made, method + " traces its step around " + call);
        }

        Assert.Contains("_tracedSteps++;", Method(scheduler, "private SchedulingState? TraceBefore("), StringComparison.Ordinal);

        // A post starts the quiet period inside the step's call: inside a traced step the line of
        // the step carries it, after "posted", so the effect never comes before its cause; outside
        // one it is written at once. With the trace off, the timer's state is not read.
        string restart = Method(scheduler, "private void RestartQuietPeriod(");
        Assert.Contains("bool tracing = _inputTrace.IsEnabled;", restart, StringComparison.Ordinal);
        Assert.Contains("bool running = tracing && _quietPeriod.IsEnabled;", restart, StringComparison.Ordinal);
        int traced = restart.IndexOf("if (tracing)", StringComparison.Ordinal);
        int inStep = restart.IndexOf("if (_tracedSteps > 0)", StringComparison.Ordinal);
        int kept = restart.IndexOf("_quietPeriodNote = what;", StringComparison.Ordinal);
        int written = restart.IndexOf("_inputTrace.Write(TraceKind.Detail, what);", StringComparison.Ordinal);
        Assert.True(traced >= 0 && inStep > traced && kept > inStep && written > kept, "the quiet period is kept for the step's line, or written outside a step");
        string traceAfter = Method(scheduler, "private void TraceAfter(");
        int posted = traceAfter.IndexOf("changes.Add(\"posted\");", StringComparison.Ordinal);
        Assert.True(posted >= 0 && traceAfter.IndexOf("changes.Add(quietPeriod);", StringComparison.Ordinal) > posted, "the step's line names the quiet period after the post");
        Assert.Contains("_quietPeriodNote = null;", traceAfter, StringComparison.Ordinal);

        // The tick's line follows what the poster did at the tick.
        string tick = Method(scheduler, "private void OnQuietPeriodTick(");
        int elapsed = tick.IndexOf("_poster.OnQuietPeriodElapsed();", StringComparison.Ordinal);
        Assert.True(elapsed >= 0 && tick.IndexOf("\"quiet period passed after \"", StringComparison.Ordinal) > elapsed, "the tick is written after the poster acted");

        // The IDLE line after a rebuild claims no more than the dispatcher's idle priority.
        string served = Method(scheduler, "private Action TraceServed(");
        Assert.Contains("until the dispatcher reached ApplicationIdle (a render WPF defers to the next frame can follow)", served, StringComparison.Ordinal);
        Assert.DoesNotContain("rendering included", scheduler, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("void OnWindowPreviewKeyDown(", "OnWindowPreviewKeyDown")]
    [InlineData("void OnDetailPreviewGotKeyboardFocus(", "OnDetailPreviewGotKeyboardFocus")]
    [InlineData("bool FocusFirstDetailField(", "FocusFirstDetailField")]
    public void TheWindowsFlushRequests_AreTheOnesTheTraceNames(string signature, string name)
    {
        // The trace names a flush by the method that asked (CallerMemberName); a renamed window
        // method would lose its meaning in the trace without this.
        string window = Source("src", "UefiAccess.App", "MainWindow.xaml.cs");
        Assert.Contains("EnsureDetailCurrent(", Method(window, signature), StringComparison.Ordinal);
        string requester = Method(Source("src", "UefiAccess.Presentation", "ViewModels", "ShellViewModel.cs"), "private static string FlushRequester(");
        Assert.Contains("\"" + name + "\" => ", requester, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("src/UefiAccess.App/MainWindow.xaml.cs")]
    [InlineData("src/UefiAccess.App/MainWindow.xaml")]
    [InlineData("src/UefiAccess.Presentation/Accessibility/CoalescingDetailScheduler.cs")]
    [InlineData("src/UefiAccess.Presentation/Accessibility/QuietPeriodPoster.cs")]
    [InlineData("src/UefiAccess.App/Platform/AnnouncingGridSplitter.cs")]
    [InlineData("src/UefiAccess.App/Platform/AnnouncingGridSplitterAutomationPeer.cs")]
    public void TheFilesTheTraceLeavesAlone_DoNotMentionIt(string file)
    {
        string source = File.ReadAllText(Path.Combine(RepositoryPaths.Root, file));
        foreach (string name in new[] { "InputTrace", "TraceKind", "TraceLine", "TraceFileWriter", "WpfInputTraceHooks", "trace-input" })
        {
            Assert.False(source.Contains(name, StringComparison.Ordinal), file + " mentions " + name);
        }
    }

    private static string Source(params string[] relative)
    {
        return File.ReadAllText(Path.Combine(new[] { RepositoryPaths.Root }.Concat(relative).ToArray())).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    /// <summary>The text from a method's signature to the closing brace at four spaces of indentation.</summary>
    private static string Method(string source, string signatureFragment)
    {
        int start = source.IndexOf(signatureFragment, StringComparison.Ordinal);
        Assert.True(start >= 0, "method not found: " + signatureFragment);
        int end = source.IndexOf("\n    }", start, StringComparison.Ordinal);
        Assert.True(end >= 0, "end of method not found: " + signatureFragment);
        return source[start..end];
    }

    private static int CountOf(string text, string value)
    {
        int count = 0;
        for (int index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
