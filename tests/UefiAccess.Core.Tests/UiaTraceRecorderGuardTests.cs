// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The UI Automation recorder (src/UefiAccess.UiaTrace, uefiaccess-uia-trace), made for the tester's A2-7
/// question (A1-6 continued), only listens. The test project cannot load it (it needs the Windows
/// Desktop runtime), so its sources are scanned as text: it is a console program that references
/// Core alone and is part of the solution; it sends no input, installs no hook, sets no focus,
/// invokes no control pattern and records no key presses; its handlers read the event's cache only,
/// take their time stamp first and never subscribe or unsubscribe; it writes its file only through
/// Core's TraceFileWriter and the console only through Program.Say, which redacts; Main runs in the
/// MTA; it waits without polling or timers; and its pure Recording folder, compiled into these
/// tests, names no UI Automation type. The guards that scan all of src/ (NoWriteGuardTests,
/// ProcessLaunchGuardTests, NativeLocationGuardTests, CompositionRootLoggingTests and
/// WmiCallsAreBoundedTests) cover the recorder too; the last test here checks that they see it.
/// </summary>
public sealed class UiaTraceRecorderGuardTests
{
    private const string ProjectFolder = "src/UefiAccess.UiaTrace";

    /// <summary>What the recorder must never name: input, hooks, focus, window messages, live pattern objects, the keyboard, native code.</summary>
    private static readonly string[] ForbiddenTexts =
    {
        "SendInput",
        "keybd_event",
        "mouse_event",
        "SetWindowsHookEx",
        "SendKeys",
        "PostMessage",
        "SendMessage",
        "SetForegroundWindow",
        "SetFocus",
        "GetCurrentPattern",
        "TryGetCurrentPattern",
        "GetCachedPattern",
        "TryGetCachedPattern",
        "GetCurrentPropertyValue",
        "System.Windows.Forms",
        "System.Windows.Input",
        "Console.ReadKey",
        "DllImport",
        "LibraryImport",
        "extern ",
        "Win32_BIOS",
        "System.Management",
        "TraceKind.Key",
    };

    /// <summary>The timed forms of hard rule 4, and polling waits.</summary>
    private static readonly string[] TimedForms = { "DispatcherTimer", "System.Timers", "System.Threading.Timer", "Task.Delay", "Thread.Sleep", "PeriodicTimer", "SpinWait", "TryTake(", "WaitAny(", "WaitAll(" };

    /// <summary>The handlers UI Automation calls for the recorded events.</summary>
    private static readonly string[] EventHandlers = { "OnFocusChanged", "OnAutomationEvent", "OnStructureChanged", "OnPropertyChanged" };

    [Fact]
    public void Project_IsAConsoleProgramThatReferencesCoreOnly()
    {
        XDocument project = XDocument.Load(Path.Combine(RepositoryPaths.Root, "src", "UefiAccess.UiaTrace", "UefiAccess.UiaTrace.csproj"));

        Assert.Equal("uefiaccess-uia-trace", Single(project, "AssemblyName"));
        Assert.Equal("Exe", Single(project, "OutputType"));
        Assert.Equal("UefiAccess.UiaTrace", Single(project, "RootNamespace"));
        Assert.Equal("true", Single(project, "UseWPF"));
        Assert.False(string.IsNullOrWhiteSpace(Single(project, "Description")));
        Assert.Empty(project.Descendants("ApplicationManifest"));
        Assert.Empty(project.Descendants("PackageReference"));
        Assert.Equal(
            new[] { @"..\UefiAccess.Core\UefiAccess.Core.csproj" },
            project.Descendants("ProjectReference").Select(reference => (string?)reference.Attribute("Include")).ToArray());
    }

    [Fact]
    public void Solution_ListsTheRecorderUnderSrc()
    {
        string solution = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "UefiAccess.sln"));

        Match project = Regex.Match(solution, @"Project\(""\{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC\}""\) = ""UefiAccess\.UiaTrace"", ""src\\UefiAccess\.UiaTrace\\UefiAccess\.UiaTrace\.csproj"", ""(?<guid>\{[0-9A-F-]{36}\})""");
        Assert.True(project.Success, "UefiAccess.sln does not list src\\UefiAccess.UiaTrace\\UefiAccess.UiaTrace.csproj");
        string guid = project.Groups["guid"].Value;
        Assert.Contains(guid + ".Release|Any CPU.Build.0 = Release|Any CPU", solution, StringComparison.Ordinal);
        Assert.Contains(guid + ".Debug|Any CPU.Build.0 = Debug|Any CPU", solution, StringComparison.Ordinal);
        Assert.Contains(guid + " = {65DE07D6-14AE-4DFB-B9C8-3EBC95FFB9A3}", solution, StringComparison.Ordinal);

        byte[] bytes = File.ReadAllBytes(Path.Combine(RepositoryPaths.Root, "UefiAccess.sln"));
        Assert.True(bytes.Length > 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "UefiAccess.sln lost its UTF-8 byte order mark");
    }

    [Fact]
    public void TestProject_CompilesTheRecordingFolderAndDoesNotReferenceTheRecorder()
    {
        XDocument project = XDocument.Load(Path.Combine(RepositoryPaths.Root, "tests", "UefiAccess.Core.Tests", "UefiAccess.Core.Tests.csproj"));

        Assert.Contains(
            project.Descendants("Compile"),
            compile => (string?)compile.Attribute("Include") == @"..\..\src\UefiAccess.UiaTrace\Recording\*.cs");
        Assert.DoesNotContain(
            project.Descendants("ProjectReference"),
            reference => ((string?)reference.Attribute("Include") ?? string.Empty).Contains("UefiAccess.UiaTrace", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Recorder_SendsNoInputAndChangesNothing()
    {
        List<string> violations = new();
        foreach ((string relative, string text) in Sources())
        {
            violations.AddRange(ForbiddenTexts.Where(forbidden => text.Contains(forbidden, StringComparison.Ordinal)).Select(forbidden => relative + ": " + forbidden));
            violations.AddRange(Regex.Matches(text, @"\bKeyboard\b|\bKey(?:Down|Up)\b").Select(match => relative + ": " + match.Value));
        }

        Assert.True(violations.Count == 0, "The recorder only listens:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Recorder_ReadsTheEventCacheOnly()
    {
        string recorder = Source("UiaRecorder.cs");
        string facts = Source("UiaElementFacts.cs");

        foreach ((string relative, string text) in Sources())
        {
            Assert.False(text.Contains(".Current.", StringComparison.Ordinal), relative + " reads a live property (.Current.)");
            Assert.False(Regex.IsMatch(text, @"\.Current\s*[;)]"), relative + " reads live properties (.Current)");
        }

        // Three cache requests, all raw view: the discovery of the window, focus (Full: the managed
        // client cannot subscribe focus otherwise) and the window's own events (None: no live
        // reference at all). The focus subscription is made under the Full one, every other under None.
        Assert.Equal(3, CountOf(recorder, ".Activate()"));
        Assert.Equal(2, CountOf(recorder, "TreeFilter = Automation.RawViewCondition"));
        int full = recorder.IndexOf("using (EventCache(AutomationElementMode.Full).Activate())", StringComparison.Ordinal);
        int none = recorder.IndexOf("using (EventCache(AutomationElementMode.None).Activate())", StringComparison.Ordinal);
        int focus = recorder.IndexOf("Automation.AddAutomationFocusChangedEventHandler(", StringComparison.Ordinal);
        Assert.True(full > 0 && none > full, "the event cache requests are not made as expected");
        Assert.True(focus > full && focus < none, "the focus subscription is not made under the Full cache request");
        foreach (string windowEvent in new[] { "Automation.AddAutomationEventHandler(SelectionItemPattern", "Automation.AddStructureChangedEventHandler(", "Automation.AddAutomationPropertyChangedEventHandler(" })
        {
            Assert.True(recorder.IndexOf(windowEvent, StringComparison.Ordinal) > none, windowEvent + " is not made under the None cache request");
        }

        Assert.Contains("element.Cached", facts, StringComparison.Ordinal);
        Assert.Contains("catch (ElementNotAvailableException)", facts, StringComparison.Ordinal);
        Assert.Contains("catch (InvalidOperationException)", facts, StringComparison.Ordinal);
    }

    [Fact]
    public void Handlers_StampFirst_CatchEverything_AndNeverSubscribe()
    {
        string recorder = Source("UiaRecorder.cs");

        foreach (string handler in EventHandlers)
        {
            string body = MethodBody(recorder, "private void " + handler + "(");
            string[] statements = body.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && line != "{").ToArray();
            Assert.Equal("long timestamp = Stopwatch.GetTimestamp();", statements[0]);
            Assert.Equal("DateTime local = DateTime.Now;", statements[1]);
            Assert.Equal("int threadId = Environment.CurrentManagedThreadId;", statements[2]);
            Assert.Contains("UiaElementFacts.FromCached(sender as AutomationElement)", body, StringComparison.Ordinal);
            Assert.Contains("TraceKind.UiaReceived", body, StringComparison.Ordinal);
            Assert.Contains("catch (Exception)", body, StringComparison.Ordinal);
        }

        foreach (string handler in EventHandlers.Concat(new[] { "OnWindowOpened", "OnCancelKeyPress", "OnTargetExited" }))
        {
            string body = MethodBody(recorder, "private void " + handler + "(");
            Assert.DoesNotContain("Automation.Add", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Automation.Remove", body, StringComparison.Ordinal);
            Assert.DoesNotContain(".Wait(", body, StringComparison.Ordinal);
        }

        Assert.Equal(1, CountOf(recorder, "Automation.RemoveAllEventHandlers()"));
        Assert.Contains("Automation.RemoveAllEventHandlers()", MethodBody(recorder, "private void RemoveSubscriptions("), StringComparison.Ordinal);
    }

    [Fact]
    public void Recorder_SubscribesToTheEventsThePlanNames()
    {
        string recorder = Source("UiaRecorder.cs");

        foreach (string subscription in new[]
        {
            "Automation.AddAutomationFocusChangedEventHandler(",
            "SelectionItemPattern.ElementSelectedEvent, window, TreeScope.Subtree",
            "SelectionItemPattern.ElementAddedToSelectionEvent, window, TreeScope.Subtree",
            "SelectionItemPattern.ElementRemovedFromSelectionEvent, window, TreeScope.Subtree",
            "Automation.AddStructureChangedEventHandler(window, TreeScope.Subtree",
            "AutomationElementIdentifiers.LiveRegionChangedEvent, window, TreeScope.Subtree",
            "AutomationElement.NotificationEvent, window, TreeScope.Subtree",
            "AutomationElement.AsyncContentLoadedEvent, window, TreeScope.Subtree",
            "Automation.AddAutomationPropertyChangedEventHandler(window, TreeScope.Subtree",
            "WindowPattern.WindowOpenedEvent, AutomationElement.RootElement, TreeScope.Children",
        })
        {
            Assert.Contains(subscription, recorder, StringComparison.Ordinal);
        }

        // The watched properties in the order of UiaEventNames.Properties, whose names they are given.
        string[] properties =
        {
            "AutomationElement.NameProperty,",
            "ValuePattern.ValueProperty,",
            "SelectionItemPattern.IsSelectedProperty,",
            "AutomationElement.HasKeyboardFocusProperty,",
            "ExpandCollapsePattern.ExpandCollapseStateProperty,",
        };
        string watched = recorder[recorder.IndexOf("WatchedProperties { get; } =", StringComparison.Ordinal)..];
        int[] positions = properties.Select(property => watched.IndexOf(property, StringComparison.Ordinal)).ToArray();
        Assert.All(positions, position => Assert.True(position > 0));
        Assert.Equal(positions.OrderBy(position => position), positions);

        // The window is found by the application's own process name, and the subscriptions are
        // anchored on the window taken again from its handle: on the element WindowOpened delivers
        // no event of the window's subtree arrived (measured on 1 October 2026).
        XDocument app = XDocument.Load(Path.Combine(RepositoryPaths.Root, "src", "UefiAccess.App", "UefiAccess.App.csproj"));
        Assert.Contains("AppProcessName = \"" + Single(app, "AssemblyName") + "\";", recorder, StringComparison.Ordinal);
        Assert.Contains("AutomationElement.FromHandle(new IntPtr(handle))", recorder, StringComparison.Ordinal);
        string run = MethodBody(recorder, "public RecorderOutcome Run(");
        Assert.True(
            run.IndexOf("WindowFromHandle(discovered, windowHandle)", StringComparison.Ordinal) is int fromHandle and > 0
                && run.IndexOf("Subscribe(window)", StringComparison.Ordinal) > fromHandle,
            "Run must subscribe on the window taken from its handle");
    }

    [Fact]
    public void Recorder_WritesItsFileOnlyThroughCore()
    {
        foreach ((string relative, string text) in Sources())
        {
            Assert.False(Regex.IsMatch(text, @"(?<![\w.])File\s*\."), relative + " uses System.IO.File");
            foreach (string form in new[] { "FileStream", "StreamWriter", "Directory.Create", "Directory.Delete" })
            {
                Assert.False(text.Contains(form, StringComparison.Ordinal), relative + " uses " + form);
            }
        }

        string output = Source("Recording/TraceOutput.cs");
        Assert.Contains("TraceFileWriter.CreateInFolder(", output, StringComparison.Ordinal);
        Assert.Contains("TraceFileWriter.CreateNew(", output, StringComparison.Ordinal);
        Assert.Contains("Logging.PrepareRunFileFolder", output, StringComparison.Ordinal);
        Assert.Contains("Logging.PrepareDataFolderFor", output, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryConsoleWrite_GoesThroughSay_WhichRedacts()
    {
        Regex consoleWrite = new(@"\bConsole\s*\.\s*(?:Out|Error|Write|WriteLine|OpenStandard\w*)\b", RegexOptions.CultureInvariant);
        string program = Source("Program.cs");
        string say = MethodBody(program, "private static void Say(");

        foreach ((string relative, string text) in Sources())
        {
            int expected = relative.EndsWith("/Program.cs", StringComparison.Ordinal) ? consoleWrite.Matches(say).Count : 0;
            Assert.True(consoleWrite.Matches(text).Count == expected, relative + " writes to the console outside Program.Say");
        }

        Assert.Equal(2, consoleWrite.Matches(say).Count);
        Assert.Contains("_writer.RedactText(text)", say, StringComparison.Ordinal);
        Assert.Contains("text => Say(text)", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Program_RunsInTheMta_ChecksElevation_AndReadsTheServiceTagThroughMachineIdentity()
    {
        string program = Source("Program.cs");

        Assert.Matches(@"\[MTAThread\]\s*private static int Main\(string\[\] args\)", program);
        Assert.Contains("ILogger logger = Logging.Create(\"uia-trace\", console: false);", program, StringComparison.Ordinal);
        Assert.Contains("if (!new PrivilegeManager(logger).IsElevated)", program, StringComparison.Ordinal);
        Assert.Contains("() => new MachineIdentity(logger).ReadServiceTag()", program, StringComparison.Ordinal);
        Assert.True(
            program.IndexOf("IsElevated", StringComparison.Ordinal) < program.IndexOf("TraceOutput.Open(", StringComparison.Ordinal),
            "the trace file must not be created before elevation is checked");
        Assert.Contains("RecorderExitCodes.NotElevated", program, StringComparison.Ordinal);
        Assert.Contains("RecorderExitCodes.NoOutput", program, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordingFolder_NamesNoUiAutomationType()
    {
        IReadOnlyList<string> files = RepositoryPaths.Enumerate(ProjectFolder + "/Recording", "*.cs");
        Assert.True(files.Count >= 5, "the Recording folder has only " + files.Count + " files");
        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            Assert.False(text.Contains("System.Windows", StringComparison.Ordinal), RepositoryPaths.Relative(file) + " names System.Windows");
            Assert.False(text.Contains("AutomationElement ", StringComparison.Ordinal), RepositoryPaths.Relative(file) + " names AutomationElement");
        }
    }

    [Fact]
    public void Recorder_HasNoTimerAndNoPollingWait()
    {
        foreach ((string relative, string text) in Sources())
        {
            foreach (string form in TimedForms)
            {
                Assert.False(text.Contains(form, StringComparison.Ordinal), relative + " uses " + form);
            }

            Assert.False(Regex.IsMatch(text, @"\.\s*Wait(?:One)?\s*\(\s*[^)\s]"), relative + " waits with a time-out (a polling wait)");
        }

        Assert.Contains("_wake.Wait();", Source("UiaRecorder.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Recorder_HoldsNoSetStyleArgumentLiteral()
    {
        foreach ((string relative, string text) in Sources())
        {
            Assert.False(Regex.IsMatch(text, @"[""']--?[A-Za-z0-9_-]+="), relative + " holds a set-style argument literal");
        }
    }

    [Fact]
    public void TheSourceWideGuards_ScanTheRecorder()
    {
        string[] scanned = RepositoryPaths.Enumerate("src", "*.cs").Select(RepositoryPaths.Relative).ToArray();

        Assert.Contains(ProjectFolder + "/Program.cs", scanned);
        Assert.Contains(ProjectFolder + "/UiaRecorder.cs", scanned);
        Assert.Contains(ProjectFolder + "/UiaElementFacts.cs", scanned);
        Assert.Contains(ProjectFolder + "/Recording/TraceOutput.cs", scanned);
    }

    /// <summary>Every C# source of the recorder, build output excluded, by relative path.</summary>
    private static IEnumerable<(string Relative, string Text)> Sources()
    {
        IReadOnlyList<string> files = RepositoryPaths.Enumerate(ProjectFolder, "*.cs");
        Assert.True(files.Count >= 8, ProjectFolder + " has only " + files.Count + " source files");
        return files.Select(file => (RepositoryPaths.Relative(file), File.ReadAllText(file)));
    }

    /// <summary>One source of the recorder by its path below the project folder, "/" separated.</summary>
    private static string Source(string relative) =>
        File.ReadAllText(Path.Combine(new[] { RepositoryPaths.Root, "src", "UefiAccess.UiaTrace" }.Concat(relative.Split('/')).ToArray()));

    private static string Single(XDocument project, string element)
    {
        XElement[] found = project.Descendants(element).ToArray();
        Assert.True(found.Length == 1, "expected one " + element + ", found " + found.Length);
        return found[0].Value.Trim();
    }

    private static int CountOf(string text, string value) => Regex.Matches(text, Regex.Escape(value)).Count;

    /// <summary>The body of the first method whose declaration starts with <paramref name="signature"/>, from its "{" to the matching "}".</summary>
    private static string MethodBody(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, "method not found: " + signature);
        int open = source.IndexOf('{', start);
        int arrow = source.IndexOf("=>", start, StringComparison.Ordinal);
        if (arrow >= 0 && arrow < open)
        {
            // An expression-bodied member: up to its semicolon.
            return source[arrow..(source.IndexOf(';', arrow) + 1)];
        }

        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}' && --depth == 0)
            {
                return source[open..(i + 1)];
            }
        }

        throw new InvalidOperationException("unbalanced braces after " + signature);
    }
}
