// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Core.Privileges;

/// <summary>
/// Outcome of attempting to enable SeSystemEnvironmentPrivilege in the current process token.
/// </summary>
public enum PrivilegeState
{
    /// <summary>The process is not elevated (or is not running on Windows), so the privilege cannot be present.</summary>
    NotElevated,

    /// <summary>The process is elevated but the privilege is absent from its token or could not be enabled.</summary>
    ElevatedPrivilegeAbsent,

    /// <summary>The privilege is enabled; firmware variable calls may proceed.</summary>
    Enabled,
}
