// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Display;
using UefiAccess.Presentation.ViewModels;
using Xunit;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// <see cref="LogPanelMirror"/>, the rule the application's LogPanelSink mirrors Serilog events by.
/// Findings F071 and F159: a mirrored line reads like the log file ({Message:lj}), so the version
/// line has no quotes around its string values. Findings F086 and F171: events marked file-only
/// (everything the Core services log inside a load, which reaches the panel through the result)
/// stay in the file. Finding F172 and F067, which live in the WPF sink the tests cannot load,
/// are checked on its source: panel lines are queued at the load continuation's priority, and
/// the sink tells the log which line has keyboard focus.
/// </summary>
public sealed class LogPanelMirrorTests
{
    private const string StartingTemplate = "UefiAccess.App {Version} starting on {Os}, 64-bit process {Is64Bit}, log directory {LogDirectory}";

    [Fact]
    public void TheVersionLine_ReadsLikeTheLogFile_WithoutQuotesAroundStrings()
    {
        (LogEvent logEvent, _) = Capture(logger => logger.Information(
            StartingTemplate,
            "0.1.0+0f477602e29a44eb44a671522e55070ca9fc9407",
            "Microsoft Windows NT 10.0.26200.0",
            true,
            @"C:\ProgramData\UefiAccess\logs"));

        LogLineViewModel line = Assert.IsType<LogLineViewModel>(LogPanelMirror.ToLine(logEvent));

        // {Message:lj} renders non-string values as JSON, so the boolean reads "true", as in the file.
        Assert.Equal(
            @"UefiAccess.App 0.1.0+0f477602e29a44eb44a671522e55070ca9fc9407 starting on Microsoft Windows NT 10.0.26200.0, 64-bit process true, log directory C:\ProgramData\UefiAccess\logs",
            line.Message);
        Assert.DoesNotContain("\"", line.Message, StringComparison.Ordinal);
        Assert.Equal(LogViewModel.InfoLevel, line.Level);
        Assert.Equal(logEvent.Timestamp, line.Timestamp);

        // The same text the log file writes after its level and component.
        using StringWriter file = new(CultureInfo.InvariantCulture);
        new MessageTemplateTextFormatter("{Message:lj}", CultureInfo.InvariantCulture).Format(logEvent, file);
        Assert.Equal(file.ToString(), line.Message);
    }

    [Fact]
    public void AnException_IsAppendedAsTypeAndMessage()
    {
        (LogEvent logEvent, _) = Capture(logger => logger.Error(new InvalidOperationException("clipboard exploded"), "Copy log failed"));

        Assert.Equal("Copy log failed (InvalidOperationException: clipboard exploded)", LogPanelMirror.Render(logEvent));
        Assert.Equal(LogViewModel.ErrorLevel, LogPanelMirror.ToLine(logEvent)!.Level);
    }

    [Fact]
    public void DebugAndVerbose_StayInTheFile()
    {
        (LogEvent debug, _) = Capture(logger => logger.Debug("detail"));
        (LogEvent verbose, _) = Capture(logger => logger.Verbose("more detail"));

        Assert.False(LogPanelMirror.Mirrors(debug));
        Assert.False(LogPanelMirror.Mirrors(verbose));
        Assert.Null(LogPanelMirror.ToLine(debug));
        Assert.Equal(LogEventLevel.Information, LogPanelMirror.MinimumLevel);
        Assert.False(LogPanelMirror.Mirrors(null));
        Assert.Null(LogPanelMirror.ToLine(null));
        Assert.Equal(string.Empty, LogPanelMirror.Render(null));
    }

    [Fact]
    public void AFileOnlyLogger_WritesToTheSameSinks_ButItsEventsAreNotMirrored()
    {
        (LogEvent warning, ILogger logger) = Capture(root => LogPanelMirror.FileOnly(root).Warning("Enumeration failed: {Failure}", "STATUS_INVALID_PARAMETER"));
        (LogEvent plain, _) = Capture(root => root.Warning("Enumeration failed: {Failure}", "STATUS_INVALID_PARAMETER"));

        Assert.NotNull(logger);
        Assert.True(warning.Properties.ContainsKey(LogPanelMirror.FileOnlyProperty));
        Assert.False(LogPanelMirror.Mirrors(warning));
        Assert.Null(LogPanelMirror.ToLine(warning));
        Assert.True(LogPanelMirror.Mirrors(plain));
        Assert.Equal("Enumeration failed: STATUS_INVALID_PARAMETER", LogPanelMirror.ToLine(plain)!.Message);
        Assert.Equal(LogViewModel.WarnLevel, LogPanelMirror.ToLine(plain)!.Level);
        Assert.Throws<ArgumentNullException>(() => LogPanelMirror.FileOnly(null!));
    }

    [Fact]
    public void OnlyTheValueTrueMarksAnEventFileOnly()
    {
        (LogEvent marked, _) = Capture(root => root.ForContext(LogPanelMirror.FileOnlyProperty, true).Information("a"));
        (LogEvent unmarked, _) = Capture(root => root.ForContext(LogPanelMirror.FileOnlyProperty, false).Information("b"));
        (LogEvent text, _) = Capture(root => root.ForContext(LogPanelMirror.FileOnlyProperty, "yes").Information("c"));

        Assert.False(LogPanelMirror.Mirrors(marked));
        Assert.True(LogPanelMirror.Mirrors(unmarked));
        Assert.True(LogPanelMirror.Mirrors(text));
    }

    [Fact]
    public void TheWpfSink_MirrorsByThisRule_AtTheLoadContinuationsPriority_AndReportsTheFocusedLine()
    {
        string sink = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "src", "UefiAccess.App", "Platform", "LogPanelSink.cs"));

        // F071/F159 and F086/F171: one rule, the file's rendering, no RenderMessage of its own.
        Assert.Contains("LogPanelMirror.ToLine(logEvent)", sink, StringComparison.Ordinal);
        Assert.DoesNotContain("RenderMessage(", sink, StringComparison.Ordinal);

        // F172: queued at Normal, the priority the load's continuation runs at, not Background.
        Assert.Contains("BeginInvoke(DispatcherPriority.Normal,", sink, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherPriority.Background", sink, StringComparison.Ordinal);

        // F067: attaching lets the log ask which line has keyboard focus before it drops one.
        string attach = sink[sink.IndexOf("public void Attach(", StringComparison.Ordinal)..];
        attach = attach[..attach.IndexOf("\n    }", StringComparison.Ordinal)];
        Assert.Contains("target.FocusedLineProvider = FocusedLine;", attach, StringComparison.Ordinal);
        Assert.Contains("Keyboard.FocusedElement", sink, StringComparison.Ordinal);
        Assert.Contains("FocusManager.GetFocusedElement(window)", sink, StringComparison.Ordinal);
        Assert.Contains("is ListBoxItem { DataContext: LogLineViewModel line }", sink, StringComparison.Ordinal);
    }

    private static (LogEvent Event, ILogger Logger) Capture(Action<ILogger> write)
    {
        List<LogEvent> events = new();
        ILogger logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(new DelegateSink(events.Add)).CreateLogger();
        write(logger);
        return (Assert.Single(events), logger);
    }

    private sealed class DelegateSink : Serilog.Core.ILogEventSink
    {
        private readonly Action<LogEvent> _emit;

        public DelegateSink(Action<LogEvent> emit) => _emit = emit;

        public void Emit(LogEvent logEvent) => _emit(logEvent);
    }
}
