// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Presentation.Services;

/// <summary>Outcome of loading a variable source.</summary>
public enum SourceStatus
{
    /// <summary>The variables were loaded (possibly zero of them).</summary>
    Loaded,

    /// <summary>A precondition failed (not UEFI, not elevated, privilege absent); the message holds the one-sentence reason.</summary>
    Refused,

    /// <summary>Loading failed for another reason (unreadable folder, enumeration error); the message holds the text.</summary>
    Failed,
}
