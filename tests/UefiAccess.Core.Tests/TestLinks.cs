// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;

namespace UefiAccess.Core.Tests;

/// <summary>
/// Creates the links the safety tests plant: directory junctions, which need no privilege
/// ("mklink /J"), and file symbolic links, which need Developer Mode or the symbolic-link
/// privilege. Test helpers only; the product never creates a link.
/// </summary>
internal static class TestLinks
{
    private static readonly Lazy<bool> FileSymbolicLinksAvailable = new(ProbeFileSymbolicLinks);

    /// <summary>True when this host may create a file symbolic link (probed once in the temporary folder).</summary>
    public static bool CanCreateFileSymbolicLinks => FileSymbolicLinksAvailable.Value;

    /// <summary>Creates a directory junction at <paramref name="link"/> that points to <paramref name="target"/>.</summary>
    /// <param name="link">The junction to create; must not exist.</param>
    /// <param name="target">The folder it points to.</param>
    /// <returns>True when the junction exists afterwards.</returns>
    public static bool TryCreateJunction(string link, string target)
    {
        ProcessStartInfo start = new(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in new[] { "/d", "/c", "mklink", "/J", link, target })
        {
            start.ArgumentList.Add(argument);
        }

        using Process? process = Process.Start(start);
        if (process is null)
        {
            return false;
        }

        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit(30000);
        return Directory.Exists(link) && (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0;
    }

    /// <summary>Creates a file symbolic link at <paramref name="link"/> that points to <paramref name="target"/>.</summary>
    /// <param name="link">The link to create; must not exist.</param>
    /// <param name="target">The file it points to.</param>
    /// <returns>True when the link was created.</returns>
    public static bool TryCreateFileSymbolicLink(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool ProbeFileSymbolicLinks()
    {
        string folder = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "symlink-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            string target = Path.Combine(folder, "target.bin");
            File.WriteAllBytes(target, new byte[] { 0x01 });
            return TryCreateFileSymbolicLink(Path.Combine(folder, "link.bin"), target);
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception)
            {
                // Best effort; a leftover temp folder must not fail discovery.
            }
        }
    }
}
