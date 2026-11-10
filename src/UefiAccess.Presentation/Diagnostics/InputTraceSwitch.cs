// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Presentation.Diagnostics;

/// <summary>
/// The command-line switch that turns the input trace on: UefiAccess.App.exe --trace-input. The
/// application reads no other argument, and this one only through <see cref="IsRequested"/>: an
/// argument that is not exactly the switch (in any case, surrounding white space ignored) changes
/// nothing, so a start without arguments, or with any other one, behaves as it always has.
/// </summary>
public static class InputTraceSwitch
{
    /// <summary>The switch.</summary>
    public const string Name = "--trace-input";

    /// <summary>True when one of the arguments is <see cref="Name"/>; the arguments are not changed.</summary>
    /// <param name="args">The application's command-line arguments; null means none.</param>
    /// <returns>True to start the input trace.</returns>
    public static bool IsRequested(IEnumerable<string>? args)
    {
        if (args is null)
        {
            return false;
        }

        foreach (string? argument in args)
        {
            if (argument is not null && string.Equals(argument.Trim(), Name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
