// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Core.Tests;

/// <summary>
/// Removes the temporary folders the file-system tests create. A folder that held a junction is
/// sometimes still busy for a moment after the junction is removed, so the delete is retried a
/// few times; a folder that still cannot be removed is left behind rather than failing the run.
/// </summary>
internal static class TestFolders
{
    /// <summary>Deletes <paramref name="path"/> and everything in it; junctions are removed, never followed.</summary>
    /// <param name="path">The temporary folder.</param>
    public static void DeleteQuietly(string path)
    {
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    return;
                }

                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException)
            {
                // Retried below; a leftover temp folder must not fail the test run.
            }
            catch (UnauthorizedAccessException)
            {
                // Retried below; a leftover temp folder must not fail the test run.
            }

            Thread.Sleep(100 * attempt);
        }
    }
}
