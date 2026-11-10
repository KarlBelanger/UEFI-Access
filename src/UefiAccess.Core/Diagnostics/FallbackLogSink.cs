// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

namespace UefiAccess.Core.Diagnostics;

/// <summary>
/// The file sink behind <see cref="Logging"/>: writes every event to the shared daily log and,
/// from the first write that fails (or from the start when there is no usable shared log), to the
/// per-user daily log for the rest of the run. The switch is recorded once, as a warning at the
/// top of the per-user log, so a run's lines are never lost silently. It is not written to the
/// notice writer (standard error): a run without elevation always switches, and a notice there
/// would come before the command's own first message. Only when the per-user log fails too, or an
/// elevated run refuses the per-user folder (<see cref="LogFolderRefusedException"/>, whose reason
/// the notice carries), is that announced once on the notice writer; later events are then
/// dropped. <see cref="Emit"/> never throws.
/// </summary>
/// <remarks>
/// Serilog's rolling file sink opens its file on the first event and throws from Emit when it
/// cannot; the logger would swallow that exception into SelfLog, and the sink would not retry
/// until the next day. Catching it here is what makes the fallback possible.
/// </remarks>
internal sealed class FallbackLogSink : ILogEventSink, IDisposable
{
    private static readonly MessageTemplate NoticeTemplate = new MessageTemplateParser().Parse("{Notice}");

    private readonly object _gate = new();
    private readonly Func<ILogEventSink>? _createFallback;
    private readonly TextWriter _notices;
    private readonly string _sharedDirectory;
    private readonly string? _userDirectory;
    private readonly string? _unusedReason;
    private ILogEventSink? _primary;
    private ILogEventSink? _fallback;
    private bool _switched;
    private bool _stopped;

    /// <summary>Creates the sink.</summary>
    /// <param name="primary">The shared log sink, or null when the shared log folder is missing or unusable.</param>
    /// <param name="createFallback">Opens the per-user log sink on first use, or null when there is no separate per-user folder.</param>
    /// <param name="notices">Receives the one-line notice when no log file can be kept (standard error in production).</param>
    /// <param name="sharedDirectory">The shared log folder, for the notices.</param>
    /// <param name="userDirectory">The per-user log folder, for the notices; null when <paramref name="createFallback"/> is null.</param>
    /// <param name="unusedReason">Why the shared log is not used from the start (an elevated run's refusal of a folder standard users could redirect), or null.</param>
    internal FallbackLogSink(ILogEventSink? primary, Func<ILogEventSink>? createFallback, TextWriter notices, string sharedDirectory, string? userDirectory, string? unusedReason = null)
    {
        _primary = primary;
        _createFallback = createFallback;
        _notices = notices ?? TextWriter.Null;
        _sharedDirectory = sharedDirectory ?? string.Empty;
        _userDirectory = createFallback is null ? null : userDirectory;
        _unusedReason = primary is null ? unusedReason : null;
    }

    /// <summary>True once events go to the per-user log (or nowhere) instead of the shared log.</summary>
    internal bool UsesFallback
    {
        get
        {
            lock (_gate)
            {
                return _switched;
            }
        }
    }

    /// <summary>The warning written once at the top of the per-user log when a run's log moves there from the shared folder.</summary>
    /// <param name="sharedDirectory">The shared log folder.</param>
    /// <param name="userDirectory">The per-user log folder.</param>
    /// <param name="reason">Why an elevated run did not use the shared folder, or null when it cannot be written.</param>
    /// <returns>The notice line.</returns>
    internal static string MovedNotice(string sharedDirectory, string userDirectory, string? reason = null) =>
        reason is null
            ? $"Note: the shared log folder {sharedDirectory} cannot be written, so this run logs to {userDirectory}."
            : $"Note: this run logs to {userDirectory}, not to the shared log folder {sharedDirectory}: {reason}";

    /// <summary>The notice written once when no log folder can be written at all, or when the per-user folder was refused.</summary>
    /// <param name="sharedDirectory">The shared log folder.</param>
    /// <param name="userDirectory">The per-user log folder, or null when there is none.</param>
    /// <param name="refusal">Why an elevated run may not use the per-user folder (<see cref="LogFolderRefusedException"/>), or null.</param>
    /// <returns>The notice line.</returns>
    internal static string NoLogNotice(string sharedDirectory, string? userDirectory, string? refusal = null) =>
        userDirectory is null
            ? $"Note: the log folder {sharedDirectory} cannot be written, so this run keeps no log file."
            : refusal is null
                ? $"Note: neither the shared log folder {sharedDirectory} nor {userDirectory} can be written, so this run keeps no log file."
                : $"Note: neither the shared log folder {sharedDirectory} nor {userDirectory} is used, so this run keeps no log file: {refusal}";

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        if (logEvent is null)
        {
            return;
        }

        lock (_gate)
        {
            if (_stopped)
            {
                return;
            }

            if (_primary is not null)
            {
                try
                {
                    _primary.Emit(logEvent);
                    return;
                }
                catch (Exception ex)
                {
                    DisposeQuietly(_primary);
                    _primary = null;
                    SwitchToFallback(logEvent, ex);
                }
            }
            else if (!_switched)
            {
                SwitchToFallback(logEvent, null);
            }

            EmitToFallback(logEvent);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            DisposeQuietly(_primary);
            DisposeQuietly(_fallback);
            _primary = null;
            _fallback = null;
            _stopped = true;
        }
    }

    private void SwitchToFallback(LogEvent trigger, Exception? failure)
    {
        _switched = true;
        if (_createFallback is null || _userDirectory is null)
        {
            Stop();
            return;
        }

        string? refusal = null;
        try
        {
            _fallback = _createFallback();
        }
        catch (LogFolderRefusedException ex)
        {
            _fallback = null;
            refusal = ex.Message;
        }
        catch (Exception)
        {
            _fallback = null;
        }

        if (_fallback is null)
        {
            Stop(refusal);
            return;
        }

        // Recorded in the per-user log only; the class summary says why not on standard error.
        string notice = MovedNotice(_sharedDirectory, _userDirectory, failure is null ? _unusedReason : null);
        List<LogEventProperty> properties = trigger.Properties
            .Select(pair => new LogEventProperty(pair.Key, pair.Value))
            .Where(property => property.Name != "Notice")
            .ToList();
        properties.Add(new LogEventProperty("Notice", new ScalarValue(notice)));
        EmitToFallback(new LogEvent(trigger.Timestamp, LogEventLevel.Warning, failure, NoticeTemplate, properties));
    }

    private void EmitToFallback(LogEvent logEvent)
    {
        if (_stopped || _fallback is null)
        {
            return;
        }

        try
        {
            _fallback.Emit(logEvent);
        }
        catch (Exception)
        {
            DisposeQuietly(_fallback);
            _fallback = null;
            Stop();
        }
    }

    private void Stop(string? refusal = null)
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        WriteNotice(NoLogNotice(_sharedDirectory, _userDirectory, refusal));
    }

    private void WriteNotice(string notice)
    {
        try
        {
            _notices.WriteLine(notice);
            _notices.Flush();
        }
        catch (Exception)
        {
            // Standard error may be closed; the notice is best effort.
        }
    }

    private static void DisposeQuietly(ILogEventSink? sink)
    {
        try
        {
            (sink as IDisposable)?.Dispose();
        }
        catch (Exception)
        {
            // Closing a log file must never fail the run.
        }
    }
}

/// <summary>
/// Thrown by the per-user log factory of an elevated run when the per-user log folder was refused
/// (<see cref="Logging.PrepareUserLogFolderForElevatedRun"/>): the message is the refusal, which
/// <see cref="FallbackLogSink"/> puts into its one-line notice.
/// </summary>
internal sealed class LogFolderRefusedException : IOException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="refusal">The sentences that say why the folder is not used and what to do.</param>
    public LogFolderRefusedException(string refusal)
        : base(refusal)
    {
    }
}
