// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Presentation.ViewModels;

/// <summary>The root state machine of the shell.</summary>
public enum ShellState
{
    /// <summary>Nothing loaded yet.</summary>
    Idle,

    /// <summary>A load is running; the Refresh and Open baseline commands are disabled.</summary>
    Loading,

    /// <summary>A source was loaded and the list shows its variables.</summary>
    Loaded,

    /// <summary>The live source refused (not UEFI, not elevated, privilege absent); Status holds the sentence.</summary>
    Refused,

    /// <summary>The load failed for another reason; Status holds the text.</summary>
    Failed,
}
