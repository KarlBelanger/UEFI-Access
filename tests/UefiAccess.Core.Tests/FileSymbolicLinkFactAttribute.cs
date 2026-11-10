// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// A fact that needs file symbolic links. Where the host cannot create one (no Developer Mode and
/// no symbolic-link privilege, as on a standard administrator's unelevated prompt) it is reported
/// as skipped rather than passed, because there would be nothing to test.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class FileSymbolicLinkFactAttribute : FactAttribute
{
    /// <summary>Marks the fact as skipped when the host cannot create a file symbolic link.</summary>
    public FileSymbolicLinkFactAttribute()
    {
        if (!TestLinks.CanCreateFileSymbolicLinks)
        {
            Skip = "Needs file symbolic links: turn on Developer Mode or run the tests elevated to include this check.";
        }
    }
}
