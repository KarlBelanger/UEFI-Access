// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Presentation.ViewModels;

/// <summary>The order of the rows inside each group (the Sort by combo box).</summary>
public enum SortBy
{
    /// <summary>By variable name (ordinal, case-insensitive), then key.</summary>
    Name,

    /// <summary>By namespace label, then name.</summary>
    Namespace,

    /// <summary>By data size, then name.</summary>
    Size,

    /// <summary>By decoder kind, then name.</summary>
    Kind,
}
