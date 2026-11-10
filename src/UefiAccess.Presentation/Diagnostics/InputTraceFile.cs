// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Concurrent;
using Serilog;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Core.Platform;
using UefiAccess.Presentation.ViewModels;

namespace UefiAccess.Presentation.Diagnostics;

/// <summary>
/// How <see cref="InputTraceFile.Start"/> went: the trace to use (<see cref="NullInputTrace"/>
/// unless a file was created), whether the switch was given, the file, and why it was not created.
/// </summary>
/// <param name="Trace">The trace the application composes; <see cref="NullInputTrace.Instance"/> when no file was created.</param>
/// <param name="Requested">True when the command line held <see cref="InputTraceSwitch.Name"/>.</param>
/// <param name="FilePath">The trace file, or null.</param>
/// <param name="Refusal">Why the trace was requested but not started, or null.</param>
public sealed record InputTraceStart(IInputTrace Trace, bool Requested, string? FilePath, string? Refusal);

/// <summary>
/// The input trace that is on: one plain-text file per run, trace-input-&lt;yyyyMMdd-HHmmss&gt;.log,
/// in the folder of the run's log (normally C:\ProgramData\UefiAccess\logs), written by a Core
/// <see cref="TraceFileWriter"/> in the <see cref="TraceLine"/> format the UI Automation recorder
/// (uefiaccess-uia-trace) shares, so the two files merge on their QPC column. A line is stamped on
/// the calling thread and only queued there; the file is written by the writer's own thread, which
/// first reads the service tag and replaces it in every line. <see cref="Dispose"/> writes a count
/// per kind (SUMMARY lines) and "input trace: stopped", then flushes and closes the file; the
/// application calls it at exit.
/// </summary>
public sealed class InputTraceFile : IInputTrace, IDisposable
{
    /// <summary>The start of the file's name; the date, the time and ".log" follow.</summary>
    public const string FilePrefix = "trace-input-";

    /// <summary>The first line of the file's header.</summary>
    public const string Title = "UefiAccess input trace";

    /// <summary>
    /// The comment line after the header that explains the KEY lines' queued= value and the IDLE
    /// lines' time since a key was pressed, which come from the key's message time.
    /// </summary>
    public const string KeyTimingComment =
        "KEY lines: queued= is how long the key's message waited before the window took it (its message time against the tick count, so in steps of about 15.6 ms; ? when unknown);"
        + " the line itself is stamped when the window took the key. IDLE lines also count from the moment the key was pressed, with the same resolution.";

    private readonly TraceFileWriter _writer;
    private readonly ConcurrentDictionary<string, int> _counts = new(StringComparer.Ordinal);
    private int _disposed;

    private InputTraceFile(TraceFileWriter writer)
    {
        _writer = writer;
    }

    /// <summary>The full path of the trace file.</summary>
    public string FilePath => _writer.FilePath ?? string.Empty;

    /// <summary>True until <see cref="Dispose"/>.</summary>
    public bool IsEnabled => Volatile.Read(ref _disposed) == 0;

    /// <summary>
    /// Starts the input trace when the command line asks for it (<see cref="InputTraceSwitch"/>).
    /// Without the switch nothing is touched: no folder is checked, no file created, no thread
    /// started, and the trace is <see cref="NullInputTrace.Instance"/>. With it, the folder is
    /// checked as Logging checks its own (<see cref="Logging.PrepareRunFileFolder(string)"/>), the
    /// file is created there without overwriting anything, and its writer reads the service tag
    /// (MachineIdentity.ReadServiceTag, over a logger whose lines stay in the log file and out of
    /// the log panel). Never throws: a trace that cannot start is returned as a refusal, and the
    /// application carries on without it.
    /// </summary>
    /// <param name="args">The application's command-line arguments.</param>
    /// <param name="logDirectory">The folder this run logs to (the application's RunLogFolder).</param>
    /// <param name="logger">The application's logger.</param>
    /// <returns>The outcome; <see cref="InputTraceStart.Trace"/> is the trace to compose.</returns>
    public static InputTraceStart Start(IReadOnlyList<string>? args, string logDirectory, ILogger logger)
    {
        if (!InputTraceSwitch.IsRequested(args))
        {
            return new InputTraceStart(NullInputTrace.Instance, false, null, null);
        }

        ILogger fileLogger = LogPanelMirror.FileOnly(logger ?? Serilog.Core.Logger.None);
        return TryStart(logDirectory, DateTime.Now, Logging.PrepareRunFileFolder, () => new MachineIdentity(fileLogger).ReadServiceTag());
    }

    /// <summary>Testable core of <see cref="Start"/> once the switch was given: checks the folder and creates the file. Never throws.</summary>
    /// <param name="logDirectory">The folder.</param>
    /// <param name="now">The local time the file's name is made from.</param>
    /// <param name="prepareFolder">Checks the folder; null to go ahead, otherwise the refusal.</param>
    /// <param name="readServiceTag">Reads the service tag on the writer thread.</param>
    /// <returns>The outcome, with <see cref="InputTraceStart.Requested"/> true.</returns>
    internal static InputTraceStart TryStart(string logDirectory, DateTime now, Func<string, string?> prepareFolder, Func<string?> readServiceTag)
    {
        string? refusal;
        try
        {
            refusal = prepareFolder(logDirectory);
        }
        catch (Exception ex)
        {
            refusal = $"{logDirectory} cannot be checked: {ex.Message}";
        }

        if (refusal is not null)
        {
            return new InputTraceStart(NullInputTrace.Instance, true, null, refusal);
        }

        try
        {
            TraceFileWriter writer = TraceFileWriter.CreateInFolder(logDirectory, FilePrefix, now, Title, readServiceTag);
            writer.Comment(KeyTimingComment);
            return new InputTraceStart(new InputTraceFile(writer), true, writer.FilePath, null);
        }
        catch (Exception ex)
        {
            // IOException and UnauthorizedAccessException above all; nothing may stop the application.
            return new InputTraceStart(NullInputTrace.Instance, true, null, $"no trace file can be created in {logDirectory}: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public void Write(string kind, string details)
    {
        if (!IsEnabled)
        {
            return;
        }

        _counts.AddOrUpdate(kind ?? string.Empty, 1, static (_, count) => count + 1);
        _writer.Write(kind ?? string.Empty, details);
    }

    /// <inheritdoc />
    public void Redact(string? value)
    {
        _writer.Redact(value);
    }

    /// <summary>
    /// Writes one SUMMARY line per kind ("input trace: &lt;kind&gt; &lt;count&gt;", in ordinal order)
    /// and "input trace: stopped", then lets the writer write everything queued, flush and close
    /// the file. Later lines are ignored. Idempotent; never throws.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (KeyValuePair<string, int> count in _counts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            _writer.Write(TraceKind.Summary, "input trace: " + count.Key + " " + count.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        _writer.Write(TraceKind.Trace, "input trace: stopped");
        _writer.Dispose();
    }
}
