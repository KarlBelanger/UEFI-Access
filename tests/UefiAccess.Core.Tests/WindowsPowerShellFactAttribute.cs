// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// A fact that runs functions of tools/gate/Run-Gate.ps1 in Windows PowerShell 5.1, the version
/// the script is written for. Where powershell.exe is not in the System folder (a Windows
/// installation without it) the fact is reported as skipped rather than passed, because there
/// would be nothing to run the script's code with.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class WindowsPowerShellFactAttribute : FactAttribute
{
    /// <summary>The full path of Windows PowerShell 5.1: never a bare name, which Windows would look up in the current directory first.</summary>
    public static string PowerShellPath { get; } = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    /// <summary>Marks the fact as skipped when Windows PowerShell 5.1 is not installed.</summary>
    public WindowsPowerShellFactAttribute()
    {
        if (!File.Exists(PowerShellPath))
        {
            Skip = "Needs Windows PowerShell 5.1 (" + PowerShellPath + "), which runs tools/gate/Run-Gate.ps1.";
        }
    }
}
