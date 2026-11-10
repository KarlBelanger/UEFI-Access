// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Presentation.Services;

/// <summary>A vendor GUID with its human label.</summary>
/// <param name="Guid">The vendor GUID.</param>
/// <param name="Label">The label shown in the Namespace column and group headers, for example "EFI global variable".</param>
/// <param name="IsKnown">True when the catalog knows the GUID; false for the "Unknown vendor" fallback.</param>
public sealed record NamespaceInfo(Guid Guid, string Label, bool IsKnown);
