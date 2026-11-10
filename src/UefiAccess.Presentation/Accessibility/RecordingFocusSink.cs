// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Presentation.Accessibility;

/// <summary>An <see cref="IFocusSink"/> that records every request in order, for tests and as a default.</summary>
public sealed class RecordingFocusSink : IFocusSink
{
    private readonly List<FocusTarget> _requests = new();

    /// <summary>Every request so far, oldest first (including <see cref="FocusTarget.None"/> when it was requested).</summary>
    public IReadOnlyList<FocusTarget> Requests => _requests;

    /// <summary>The most recent request, or <see cref="FocusTarget.None"/> when none was made.</summary>
    public FocusTarget Last => _requests.Count == 0 ? FocusTarget.None : _requests[^1];

    /// <inheritdoc />
    public void RequestFocus(FocusTarget target) => _requests.Add(target);

    /// <summary>Forgets the recorded requests.</summary>
    public void Clear() => _requests.Clear();
}
