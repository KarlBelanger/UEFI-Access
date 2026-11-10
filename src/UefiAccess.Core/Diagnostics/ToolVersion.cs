// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;

namespace UefiAccess.Core.Diagnostics;

/// <summary>
/// The one version string every executable shows and writes: the informational version of the
/// build, "0.1.0+&lt;commit&gt;" (the version from Directory.Build.props followed by the git commit
/// the build was made from, which the SDK appends as the source revision). The CLI prints it as
/// "Tool version" in info, as toolVersion in the info document and gate.json and as ToolVersion in
/// index.json; the WPF shell shows it in its ready message and logs it at start-up. Both read
/// <see cref="Current"/>, so the two always agree. Core, the CLI and the shell share
/// Directory.Build.props and are built together, so the Core assembly's version is the tool's.
/// No member throws.
/// </summary>
public static class ToolVersion
{
    /// <summary>The text returned when no version attribute can be read from an assembly.</summary>
    public const string Unknown = "0.0.0";

    /// <summary>The tool version: the informational version of the Core assembly.</summary>
    public static string Current { get; } = FromAssembly(typeof(ToolVersion).Assembly);

    /// <summary>
    /// Reads the version of <paramref name="assembly"/>: its informational version when present
    /// (the "0.1.0+&lt;commit&gt;" form), otherwise its assembly version as major.minor.patch,
    /// otherwise <see cref="Unknown"/>. Never throws.
    /// </summary>
    /// <param name="assembly">The assembly to read; null yields <see cref="Unknown"/>.</param>
    /// <returns>The version text.</returns>
    public static string FromAssembly(Assembly? assembly)
    {
        if (assembly is null)
        {
            return Unknown;
        }

        try
        {
            string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
            {
                return informational.Trim();
            }

            return assembly.GetName().Version?.ToString(3) ?? Unknown;
        }
        catch (Exception)
        {
            return Unknown;
        }
    }
}
