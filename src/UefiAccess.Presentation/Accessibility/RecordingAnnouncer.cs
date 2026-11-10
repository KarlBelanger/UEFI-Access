// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Presentation.Accessibility;

/// <summary>
/// An <see cref="IAnnouncer"/> that keeps every announcement in order. The default announcer of a
/// view model created without a platform, and the one tests assert against.
/// </summary>
public sealed class RecordingAnnouncer : IAnnouncer
{
    private readonly List<string> _history = new();

    /// <summary>Every announcement so far, oldest first.</summary>
    public IReadOnlyList<string> History => _history;

    /// <inheritdoc />
    public string Current { get; private set; } = string.Empty;

    /// <inheritdoc />
    public void Announce(string text)
    {
        Current = text ?? string.Empty;
        _history.Add(Current);
    }

    /// <summary>Forgets the history and resets <see cref="Current"/> to an empty string.</summary>
    public void Clear()
    {
        _history.Clear();
        Current = string.Empty;
    }
}
