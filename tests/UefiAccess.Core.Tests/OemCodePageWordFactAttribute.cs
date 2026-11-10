// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using UefiAccess.Core.Platform;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// A fact that needs a word outside ASCII which the system's OEM code page can write, so that a
/// child's output shows which code page it was decoded with. <see cref="Word"/> is the first of a
/// few words in different scripts that the OEM code page writes and reads back unchanged. Where
/// none does, the fact is reported as skipped rather than passed, because there would be nothing
/// to test.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class OemCodePageWordFactAttribute : FactAttribute
{
    /// <summary>One word per script: Latin with an accent, Cyrillic, Greek, Hebrew, Arabic, Japanese and Chinese, Thai.</summary>
    private static readonly string[] Candidates = { "café", "Привет", "Ωμεγα", "שלום", "مرحبا", "日本", "ไทย" };

    /// <summary>Marks the fact as skipped when the OEM code page can write none of the candidate words.</summary>
    public OemCodePageWordFactAttribute()
    {
        if (Word is null)
        {
            Skip = "Needs a word outside ASCII that the OEM code page (" + ProcessRunner.ChildConsoleEncoding().CodePage + ") can write; it writes none of the candidates.";
        }
    }

    /// <summary>The first candidate the OEM code page writes and reads back unchanged, or null when there is none.</summary>
    public static string? Word { get; } = FindWord(ProcessRunner.ChildConsoleEncoding());

    /// <summary>The first candidate an encoding writes and reads back unchanged (a best-fit substitute such as "cafe" for "café" does not count).</summary>
    /// <param name="encoding">The code page's encoding.</param>
    /// <returns>The word, or null when the encoding writes none of the candidates.</returns>
    internal static string? FindWord(Encoding encoding) =>
        Candidates.FirstOrDefault(word => string.Equals(encoding.GetString(encoding.GetBytes(word)), word, StringComparison.Ordinal));
}
