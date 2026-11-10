// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Presentation.Accessibility;

/// <summary>
/// The landmark regions of the window, in F6 order: since M4 seven, the Dell settings between the
/// log panel and the status bar. <see cref="RegionOrder"/> holds the
/// F6 cycle and the spoken names.
/// </summary>
public enum Region
{
    /// <summary>The toolbar of buttons.</summary>
    Toolbar,

    /// <summary>The filter box and the Group by and Sort by combo boxes.</summary>
    Filter,

    /// <summary>The variable list.</summary>
    List,

    /// <summary>The detail pane.</summary>
    Detail,

    /// <summary>The log panel.</summary>
    Log,

    /// <summary>The Dell settings: the Read Dell settings button, the state line and the list of Dell BIOS settings.</summary>
    Dell,

    /// <summary>The status bar.</summary>
    Status,
}
