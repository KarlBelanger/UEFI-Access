// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Presentation.Diagnostics;

/// <summary>
/// An <see cref="IInputTrace"/> for tests, in the style of the recording announcer: keeps every
/// line and every redacted value in order. It records what it is given whether or not it is
/// enabled, so a test of a disabled trace sees whether a caller wrote anyway. Thread-safe.
/// </summary>
public sealed class RecordingInputTrace : IInputTrace
{
    private readonly object _gate = new();
    private readonly List<(string Kind, string Details)> _lines = new();
    private readonly List<string> _redacted = new();
    private readonly List<string> _calls = new();

    /// <summary>Creates the trace.</summary>
    /// <param name="enabled">What <see cref="IsEnabled"/> says.</param>
    public RecordingInputTrace(bool enabled = true)
    {
        IsEnabled = enabled;
    }

    /// <inheritdoc />
    public bool IsEnabled { get; }

    /// <summary>Every line written, oldest first, as (kind, details).</summary>
    public IReadOnlyList<(string Kind, string Details)> Lines
    {
        get
        {
            lock (_gate)
            {
                return _lines.ToArray();
            }
        }
    }

    /// <summary>Every value passed to <see cref="Redact"/>, oldest first (null ones left out).</summary>
    public IReadOnlyList<string> Redacted
    {
        get
        {
            lock (_gate)
            {
                return _redacted.ToArray();
            }
        }
    }

    /// <summary>Every call in order: "Write &lt;kind&gt; &lt;details&gt;" or "Redact &lt;value&gt;".</summary>
    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_gate)
            {
                return _calls.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public void Write(string kind, string details)
    {
        lock (_gate)
        {
            _lines.Add((kind, details));
            _calls.Add("Write " + kind + " " + details);
        }
    }

    /// <inheritdoc />
    public void Redact(string? value)
    {
        if (value is null)
        {
            return;
        }

        lock (_gate)
        {
            _redacted.Add(value);
            _calls.Add("Redact " + value);
        }
    }

    /// <summary>Forgets every line, value and call.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _lines.Clear();
            _redacted.Clear();
            _calls.Clear();
        }
    }
}
