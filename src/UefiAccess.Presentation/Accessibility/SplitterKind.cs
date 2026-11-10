// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Presentation.Accessibility;

/// <summary>The two splitters of the window, by the panes their sentences name (<see cref="SplitterPosition.ValueText"/>).</summary>
public enum SplitterKind
{
    /// <summary>"Resize list and details", between the list and the detail pane: its share is the list's share of the width.</summary>
    ListAndDetails,

    /// <summary>"Resize log panel", above the log panel: its share is the log panel's share of the height.</summary>
    LogPanel,
}
