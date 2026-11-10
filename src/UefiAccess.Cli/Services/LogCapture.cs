// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Text;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace UefiAccess.Cli.Services;

/// <summary>
/// Serilog sink that keeps the formatted text of every event of the current run in memory so the
/// baseline export can write it to log.txt. Thread-safe; never throws from <see cref="Emit"/>.
/// </summary>
internal sealed class LogCapture : ILogEventSink
{
    private const string Template = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{Component}] {Message:lj}{NewLine}{Exception}";

    private readonly MessageTemplateTextFormatter _formatter = new(Template, CultureInfo.InvariantCulture);
    private readonly StringBuilder _buffer = new();
    private readonly object _sync = new();

    /// <summary>The captured log text so far.</summary>
    public string Text
    {
        get
        {
            lock (_sync)
            {
                return _buffer.ToString();
            }
        }
    }

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        if (logEvent is null)
        {
            return;
        }

        try
        {
            using StringWriter writer = new(CultureInfo.InvariantCulture);
            _formatter.Format(logEvent, writer);
            lock (_sync)
            {
                _buffer.Append(writer.ToString());
            }
        }
        catch (Exception)
        {
            // Capturing the log must never interfere with the command.
        }
    }
}
