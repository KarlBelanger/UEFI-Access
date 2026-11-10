// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Presentation.Accessibility;

/// <summary>
/// The single path for every screen-reader announcement. The WPF implementation writes the text
/// into the Status box (the only polite live region) and raises LiveRegionChanged; tests use
/// <see cref="RecordingAnnouncer"/>. Announcements happen only in direct response to a user
/// action or the completion of an action the user started, never on a timer.
/// </summary>
public interface IAnnouncer
{
    /// <summary>The text most recently announced, or an empty string before the first announcement.</summary>
    string Current { get; }

    /// <summary>Announces the text: it becomes <see cref="Current"/> and reaches the live region.</summary>
    /// <param name="text">The text; null is treated as an empty string.</param>
    void Announce(string text);
}
