// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Core.Tests;

/// <summary>
/// Locates the repository root (the folder that holds UefiAccess.sln) from the test assembly's
/// location and enumerates source files for the source-scanning guard tests, skipping build
/// output. Shared by <see cref="NoWriteGuardTests"/> and <see cref="NativeLocationGuardTests"/>.
/// </summary>
internal static class RepositoryPaths
{
    /// <summary>The repository root: the nearest parent of the test assembly that contains UefiAccess.sln.</summary>
    public static string Root { get; } = Locate();

    /// <summary>True for a path inside a bin/ or obj/ folder.</summary>
    /// <param name="path">The path to test.</param>
    /// <returns>True when the path is build output.</returns>
    public static bool IsBuildOutput(string path)
    {
        string normalized = path.Replace('\\', '/');
        return normalized.Contains("/bin/", StringComparison.Ordinal) || normalized.Contains("/obj/", StringComparison.Ordinal);
    }

    /// <summary>The path relative to <see cref="Root"/> with forward slashes, for messages and prefix tests.</summary>
    /// <param name="path">An absolute path under the repository.</param>
    /// <returns>The relative path with "/" separators.</returns>
    public static string Relative(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    /// <summary>
    /// Enumerates the files under a repository sub-folder that match any of the patterns,
    /// excluding build output, sorted ordinally. The folder must exist.
    /// </summary>
    /// <param name="relativeDirectory">Folder relative to the root, for example "src" or "tools".</param>
    /// <param name="patterns">Search patterns such as "*.cs" or "*.ps1".</param>
    /// <returns>The absolute paths.</returns>
    public static IReadOnlyList<string> Enumerate(string relativeDirectory, params string[] patterns)
    {
        string directory = Path.Combine(Root, relativeDirectory);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(relativeDirectory + " not found under " + Root);
        }

        return patterns
            .SelectMany(pattern => Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories))
            .Where(path => !IsBuildOutput(path))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static string Locate()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "UefiAccess.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("UefiAccess.sln not found in any parent of " + AppContext.BaseDirectory);
    }
}
