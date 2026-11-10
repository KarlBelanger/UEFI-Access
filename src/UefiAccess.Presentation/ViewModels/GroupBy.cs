// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Presentation.ViewModels;

/// <summary>How the variable list is grouped (the Group by combo box).</summary>
public enum GroupBy
{
    /// <summary>One group per vendor GUID, labelled by the namespace catalog.</summary>
    Namespace,

    /// <summary>One group per decoder kind ("BootOrder", "LoadOption", ..., "Raw").</summary>
    Kind,

    /// <summary>A single flat list.</summary>
    None,
}
