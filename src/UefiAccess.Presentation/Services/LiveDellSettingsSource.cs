// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Serilog;
using UefiAccess.Core.Settings.Dell;
using UefiAccess.Presentation.ViewModels;

namespace UefiAccess.Presentation.Services;

/// <summary>
/// This computer's Dell BIOS settings through Core's <see cref="IDellSettingsReader"/>: Dell Command Monitor, and Dell Command Configure when that gives no answer. Each
/// call of <see cref="ReadAsync"/> runs one read on a thread-pool thread, so the window stays
/// responsive; a call while a read is still in flight is handed that read, so a repeated Read Dell
/// settings never starts a second one. Nothing waits here and nothing runs on a timer: Core bounds
/// the read (120 seconds for all its steps), and the read only reads.
/// </summary>
/// <remarks>
/// <see cref="CreateWindows"/> composes the real reader over the log file only
/// (<see cref="LogPanelMirror.FileOnly"/>): its record of every query and run stays in the log file,
/// and what it found reaches the log panel once, through the shell. Tests use the internal
/// constructor with a fake reader, so no test on this PC (the unit) reads Dell Command Monitor or
/// starts Dell Command Configure (DellSettingsGuardTests.TestsNeverRunTheRealDellReader).
/// </remarks>
public sealed class LiveDellSettingsSource : IDellSettingsSource
{
    private readonly IDellSettingsReader _reader;
    private readonly object _gate = new();
    private Task<DellSettingsResult>? _inFlight;

    /// <summary>The seam for tests: reads through the given reader.</summary>
    /// <param name="reader">The reader.</param>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is null.</exception>
    internal LiveDellSettingsSource(IDellSettingsReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
    }

    /// <summary>
    /// Composes the production source: Core's <see cref="DellSettingsReader"/> over the given logger
    /// marked file-only. Composing reads nothing and runs nothing (the reader only looks, once,
    /// whether Dell Command Configure's program is on disk); the first read happens on the first
    /// press of Read Dell settings.
    /// </summary>
    /// <param name="log">Optional logger; null disables logging.</param>
    /// <returns>The source.</returns>
    public static LiveDellSettingsSource CreateWindows(ILogger? log)
    {
        ILogger logger = log ?? Serilog.Core.Logger.None;
        ILogger coreLog = LogPanelMirror.FileOnly(logger);
        return new LiveDellSettingsSource(new DellSettingsReader(coreLog));
    }

    /// <inheritdoc />
    public Task<DellSettingsResult> ReadAsync()
    {
        lock (_gate)
        {
            if (_inFlight is { IsCompleted: false } running)
            {
                return running;
            }

            Task<DellSettingsResult> started = Task.Run(() => _reader.Read(DellReadOptions.Default));
            _inFlight = started;
            return started;
        }
    }
}
