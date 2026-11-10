// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace UefiAccess.Presentation.ViewModels;

/// <summary>
/// The rule by which the application's Serilog events reach the log panel, shared by the WPF
/// sink (UefiAccess.App's LogPanelSink) and the tests' mirror, so both show exactly what the user
/// sees. An event is mirrored when it is at <see cref="MinimumLevel"/> or above and is not marked
/// <see cref="FileOnlyProperty"/>. The Core services the GUI composes (firmware type detector,
/// privilege manager, variable store, machine identity) log through a logger made by
/// <see cref="FileOnly"/>: the log file keeps every native call and WMI query they log, but what
/// they find already reaches the panel through the source result (a warning, the identity, the
/// failure), so mirroring their own lines would show one event twice. The message is rendered
/// the way the log file renders it ({Message:lj}: string values without quotes), followed by
/// " (Type: message)" when the event carries an exception. Never throws.
/// </summary>
public static class LogPanelMirror
{
    /// <summary>The event property that keeps an event in the log file and out of the panel.</summary>
    public const string FileOnlyProperty = "UefiAccessFileOnly";

    private static readonly MessageTemplateTextFormatter MessageFormatter = new("{Message:lj}", CultureInfo.InvariantCulture);

    /// <summary>The lowest level mirrored into the panel; Debug and Verbose stay in the file only.</summary>
    public static LogEventLevel MinimumLevel => LogEventLevel.Information;

    /// <summary>A logger that writes wherever <paramref name="logger"/> writes, with every event marked <see cref="FileOnlyProperty"/>.</summary>
    /// <param name="logger">The application logger.</param>
    /// <returns>The marked logger.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is null.</exception>
    public static ILogger FileOnly(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        return logger.ForContext(FileOnlyProperty, true);
    }

    /// <summary>True when the event belongs in the panel: at <see cref="MinimumLevel"/> or above and not marked <see cref="FileOnlyProperty"/>.</summary>
    /// <param name="logEvent">The event; null is never mirrored.</param>
    /// <returns>True to mirror it.</returns>
    public static bool Mirrors(LogEvent? logEvent)
    {
        if (logEvent is null || logEvent.Level < MinimumLevel)
        {
            return false;
        }

        return !(logEvent.Properties.TryGetValue(FileOnlyProperty, out LogEventPropertyValue? marker) && marker is ScalarValue { Value: true });
    }

    /// <summary>The panel line for an event, or null when <see cref="Mirrors"/> says it stays in the file.</summary>
    /// <param name="logEvent">The event.</param>
    /// <returns>The line, stamped with the event's own timestamp.</returns>
    public static LogLineViewModel? ToLine(LogEvent? logEvent)
    {
        return logEvent is not null && Mirrors(logEvent)
            ? new LogLineViewModel(logEvent.Timestamp, logEvent.Level.ToString(), Render(logEvent))
            : null;
    }

    /// <summary>The event's message as the log file shows it, plus " (Type: message)" for an attached exception.</summary>
    /// <param name="logEvent">The event.</param>
    /// <returns>The text; empty for a null event.</returns>
    public static string Render(LogEvent? logEvent)
    {
        if (logEvent is null)
        {
            return string.Empty;
        }

        string message;
        try
        {
            using StringWriter writer = new(CultureInfo.InvariantCulture);
            MessageFormatter.Format(logEvent, writer);
            message = writer.ToString();
        }
        catch (Exception)
        {
            // Rendering must never cost the line; the default rendering is the fallback.
            message = logEvent.RenderMessage(CultureInfo.InvariantCulture);
        }

        return logEvent.Exception is null
            ? message
            : message + " (" + logEvent.Exception.GetType().Name + ": " + logEvent.Exception.Message + ")";
    }
}
