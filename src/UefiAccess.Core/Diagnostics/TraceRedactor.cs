// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;

namespace UefiAccess.Core.Diagnostics;

/// <summary>
/// Replaces the machine's service tag in trace lines with <see cref="Replacement"/>. The trace
/// files are sent to the developer, and a Dell service tag identifies one unit; it reaches a trace
/// line only by accident (an accessible name, a window title, a Status sentence), but nothing a
/// trace writes may carry it. The values come from Win32_BIOS.SerialNumber (read once when a trace
/// starts) and from the identity of every load the application shows (a baseline names the tag of
/// the unit it was taken on). Every value is matched without regard to case, longest first, in one
/// pass, so a value inside a longer one is not left half replaced and the replacement is never
/// matched again. A name a trace line quotes is cut with "…" before the line reaches the writer
/// (<see cref="TraceLine.Quote"/>), which may cut a value too; so the start of a value, at least
/// <see cref="MinimumCutLength"/> characters of it, that ends right before a "…" is replaced as
/// well. Values shorter than <see cref="MinimumLength"/> are ignored: they would replace ordinary
/// words. Thread-safe: <see cref="Add"/> replaces the list as a whole (copy on write), and
/// <see cref="Apply"/> reads whichever list is current.
/// </summary>
public sealed class TraceRedactor
{
    /// <summary>What every value is replaced with.</summary>
    public const string Replacement = "[service tag]";

    /// <summary>The shortest value that is replaced; a Dell service tag has seven characters.</summary>
    public const int MinimumLength = 4;

    /// <summary>The shortest start of a value that is replaced when a cut ("…") follows it.</summary>
    public const int MinimumCutLength = 3;

    /// <summary>The character <see cref="TraceLine.Quote"/> ends a cut text with.</summary>
    private const char Ellipsis = '…';

    private readonly object _gate = new();
    private string[] _values = Array.Empty<string>();

    /// <summary>How many distinct values are replaced.</summary>
    public int Count => Volatile.Read(ref _values).Length;

    /// <summary>
    /// Adds a value to replace. The value is trimmed; null, blank, a value shorter than
    /// <see cref="MinimumLength"/> and one already known (in any case) are ignored. Never throws.
    /// </summary>
    /// <param name="value">The value, typically a service tag.</param>
    public void Add(string? value)
    {
        string trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length < MinimumLength)
        {
            return;
        }

        lock (_gate)
        {
            string[] current = _values;
            foreach (string known in current)
            {
                if (string.Equals(known, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            string[] next = current
                .Append(trimmed)
                .OrderByDescending(known => known.Length)
                .ThenBy(known => known, StringComparer.Ordinal)
                .ToArray();
            Volatile.Write(ref _values, next);
        }
    }

    /// <summary>
    /// The text with every known value replaced by <see cref="Replacement"/>, compared without
    /// regard to case (ordinal), the longest value first at every position; then, before every
    /// "…", the longest start of a value (at least <see cref="MinimumCutLength"/> characters, short
    /// of the whole value) that ends there. Never throws.
    /// </summary>
    /// <param name="text">The text; null gives an empty string.</param>
    /// <returns>The redacted text; the same text when it holds no value.</returns>
    public string Apply(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        string[] values = Volatile.Read(ref _values);
        string whole = ReplaceWholeValues(text, values);
        return whole.Contains(Ellipsis) ? ReplaceCutValues(whole, values) : whole;
    }

    /// <summary>Every whole value replaced, longest first at every position, in one pass.</summary>
    private static string ReplaceWholeValues(string text, string[] values)
    {
        bool found = false;
        foreach (string value in values)
        {
            if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                found = true;
                break;
            }
        }

        if (!found)
        {
            return text;
        }

        StringBuilder redacted = new(text.Length);
        int index = 0;
        while (index < text.Length)
        {
            string? match = null;
            foreach (string value in values)
            {
                if (value.Length <= text.Length - index && string.Compare(text, index, value, 0, value.Length, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    match = value;
                    break;
                }
            }

            if (match is null)
            {
                redacted.Append(text[index]);
                index++;
            }
            else
            {
                redacted.Append(Replacement);
                index += match.Length;
            }
        }

        return redacted.ToString();
    }

    /// <summary>
    /// Before every "…", the longest start of a value (from <see cref="MinimumCutLength"/>
    /// characters up to one short of the whole value) that ends there, replaced; never reaching back
    /// over a replacement made before it.
    /// </summary>
    private static string ReplaceCutValues(string text, string[] values)
    {
        StringBuilder? redacted = null;
        int copied = 0;
        for (int cut = text.IndexOf(Ellipsis); cut >= 0; cut = text.IndexOf(Ellipsis, cut + 1))
        {
            int longest = 0;
            foreach (string value in values)
            {
                for (int length = Math.Min(value.Length - 1, cut - copied); length >= MinimumCutLength && length > longest; length--)
                {
                    if (string.Compare(text, cut - length, value, 0, length, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        longest = length;
                        break;
                    }
                }
            }

            if (longest == 0)
            {
                continue;
            }

            redacted ??= new StringBuilder(text.Length + Replacement.Length);
            redacted.Append(text, copied, cut - longest - copied).Append(Replacement);
            copied = cut;
        }

        if (redacted is null)
        {
            return text;
        }

        redacted.Append(text, copied, text.Length - copied);
        return redacted.ToString();
    }
}
