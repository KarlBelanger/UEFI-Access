// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.CommandLine;

namespace UefiAccess.Cli.Commands;

/// <summary>Options shared by several commands. Each command gets its own instances.</summary>
internal static class CommonOptions
{
    /// <summary>The --json flag.</summary>
    /// <returns>A new option.</returns>
    public static Option<bool> Json()
    {
        return new Option<bool>("--json")
        {
            Description = "Print one JSON document on standard output instead of text (diagnostics still go to standard error).",
        };
    }

    /// <summary>The --guid option.</summary>
    /// <param name="description">Help text.</param>
    /// <returns>A new option.</returns>
    public static Option<string?> Guid(string description)
    {
        return new Option<string?>("--guid", "-g")
        {
            Description = description,
            HelpName = "guid",
        };
    }
}
