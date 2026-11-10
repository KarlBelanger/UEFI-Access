// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;
using System.Text.RegularExpressions;
using UefiAccess.Cli.Services;
using UefiAccess.Core.Diagnostics;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The CLI and the WPF shell must show one and the same version string, the informational
/// version of the build ("0.1.0+&lt;commit&gt;"), through <see cref="ToolVersion.Current"/>. The
/// shell cannot be referenced from the test project, so its two version members are checked as
/// text: they must read the shared helper and no executable may compute a version of its own.
/// </summary>
public sealed class ToolVersionTests
{
    /// <summary>The version from Directory.Build.props, optionally followed by the git commit the SDK appends.</summary>
    private static readonly Regex VersionShape = new(@"^\d+\.\d+\.\d+(\+[0-9a-fA-F]{7,40})?$", RegexOptions.CultureInvariant);

    [Fact]
    public void Current_IsTheInformationalVersionOfTheBuild()
    {
        Assert.Matches(VersionShape, ToolVersion.Current);
        Assert.StartsWith("0.1.0", ToolVersion.Current, StringComparison.Ordinal);
        Assert.Equal(ToolVersion.FromAssembly(typeof(ToolVersion).Assembly), ToolVersion.Current);
    }

    [Fact]
    public void FromAssembly_PrefersTheInformationalVersion()
    {
        Assembly assembly = typeof(ToolVersionTests).Assembly;
        string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Assert.False(string.IsNullOrWhiteSpace(informational), "the test assembly carries no informational version");
        Assert.Equal(informational!.Trim(), ToolVersion.FromAssembly(assembly));
    }

    [Fact]
    public void FromAssembly_NullYieldsUnknown()
    {
        Assert.Equal(ToolVersion.Unknown, ToolVersion.FromAssembly(null));
        Assert.Matches(VersionShape, ToolVersion.Unknown);
    }

    [Fact]
    public void CliReportsTheSharedVersion()
    {
        Assert.Equal(ToolVersion.Current, CliServices.ToolVersion);
    }

    [Fact]
    public void WpfShellReadsTheSharedVersion()
    {
        string appXaml = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "src", "UefiAccess.App", "App.xaml.cs"));
        string window = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "src", "UefiAccess.App", "MainWindow.xaml.cs"));
        Assert.Contains("ToolVersion.Current", appXaml, StringComparison.Ordinal);
        Assert.Contains("ToolVersion.Current", window, StringComparison.Ordinal);
    }

    [Fact]
    public void NoExecutableComputesItsOwnVersion()
    {
        string[] privateVersionForms = { "GetName().Version", "AssemblyInformationalVersionAttribute", "AssemblyVersionAttribute" };
        List<string> violations = new();
        int fileCount = 0;
        foreach (string project in new[] { "UefiAccess.Cli", "UefiAccess.App" })
        {
            foreach (string file in RepositoryPaths.Enumerate(Path.Combine("src", project), "*.cs"))
            {
                fileCount++;
                string text = File.ReadAllText(file);
                foreach (string form in privateVersionForms)
                {
                    if (text.Contains(form, StringComparison.Ordinal))
                    {
                        violations.Add(RepositoryPaths.Relative(file) + ": " + form);
                    }
                }
            }
        }

        Assert.True(fileCount > 0, "no C# files found under the executable projects");
        Assert.True(violations.Count == 0, "Executables must read ToolVersion.Current instead of computing a version:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }
}
