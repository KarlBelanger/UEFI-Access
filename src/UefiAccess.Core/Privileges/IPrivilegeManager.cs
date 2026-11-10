// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Core.Privileges;

/// <summary>
/// Reports elevation and enables the privilege required for firmware variable access.
/// Consumed through this interface so callers can be tested without a Windows token.
/// </summary>
public interface IPrivilegeManager
{
    /// <summary>True when the current process runs with an elevated (administrator) token.</summary>
    bool IsElevated { get; }

    /// <summary>
    /// Attempts to enable SeSystemEnvironmentPrivilege and reports the resulting state.
    /// Never throws.
    /// </summary>
    /// <returns>The privilege state after the attempt.</returns>
    PrivilegeState EnableSystemEnvironmentPrivilege();
}
