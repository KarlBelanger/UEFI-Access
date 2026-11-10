// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// A fact that runs only on a non-Windows host. On Windows it is reported as skipped rather than
/// passed, because the code under test would otherwise reach WMI, the registry, PowerShell, or a
/// native firmware call, none of which a unit test may touch. Off Windows every assertion runs.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class NonWindowsFactAttribute : FactAttribute
{
    /// <summary>Marks the fact as skipped when the host is Windows.</summary>
    public NonWindowsFactAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "Runs only off Windows: the Windows path would call WMI, the registry, PowerShell, or native firmware APIs.";
        }
    }
}
