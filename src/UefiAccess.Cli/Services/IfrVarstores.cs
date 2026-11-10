// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;

namespace UefiAccess.Cli.Services;

/// <summary>One varstore from an IFR extraction, read from a "Name;GUID;Size" line.</summary>
/// <param name="Line">1-based line number in the file.</param>
/// <param name="Name">Varstore name (the UEFI variable name).</param>
/// <param name="Guid">Varstore GUID (the vendor GUID).</param>
/// <param name="Size">Varstore size in bytes as declared by the IFR.</param>
internal sealed record IfrVarstore(int Line, string Name, Guid Guid, int Size);

/// <summary>
/// Reads the ifr-varstores file used by the gate: one "Name;GUID;Size" per line, blank lines and
/// lines starting with "#" skipped, size decimal or 0x-hexadecimal. A name may be written with the
/// double quotes IFRExtractor-RS prints around it ("Setup"); one surrounding pair is removed.
/// Never throws; bad lines are reported with their line number.
/// </summary>
internal static class IfrVarstores
{
    /// <summary>Reads and parses the file.</summary>
    /// <param name="path">Path of the file.</param>
    /// <param name="errors">Receives one message per unreadable line (or one for an unreadable file).</param>
    /// <returns>The varstores in file order.</returns>
    public static IReadOnlyList<IfrVarstore> Parse(string path, out IReadOnlyList<string> errors)
    {
        if (!TryReadLines(path, out IReadOnlyList<string> lines, out string? fileError))
        {
            errors = new[] { "cannot read '" + path + "': " + fileError };
            return Array.Empty<IfrVarstore>();
        }

        return ParseLines(lines, out errors);
    }

    /// <summary>Reads every line of the file. Never throws.</summary>
    /// <param name="path">Path of the file.</param>
    /// <param name="lines">Receives the lines; empty when the file could not be read.</param>
    /// <param name="error">Receives "&lt;exception&gt;: &lt;message&gt;" when the file could not be read; otherwise null.</param>
    /// <returns>True when the file was read.</returns>
    public static bool TryReadLines(string path, out IReadOnlyList<string> lines, [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out string? error)
    {
        try
        {
            lines = File.ReadAllLines(path);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            lines = Array.Empty<string>();
            error = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    /// <summary>Parses the lines of an ifr-varstores file.</summary>
    /// <param name="lines">The lines, in file order (line numbers are 1-based positions in this list).</param>
    /// <param name="errors">Receives one message per unreadable line.</param>
    /// <returns>The varstores in file order.</returns>
    public static IReadOnlyList<IfrVarstore> ParseLines(IReadOnlyList<string> lines, out IReadOnlyList<string> errors)
    {
        List<string> problems = new();
        List<IfrVarstore> entries = new();
        for (int i = 0; i < lines.Count; i++)
        {
            int lineNumber = i + 1;
            string line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            string[] parts = line.Split(';');
            if (parts.Length < 3)
            {
                problems.Add("line " + lineNumber.ToString(CultureInfo.InvariantCulture) + ": expected 'Name;GUID;Size' but found " + parts.Length.ToString(CultureInfo.InvariantCulture) + " field(s)");
                continue;
            }

            string name = Unquote(parts[0].Trim());
            if (name.Length == 0)
            {
                problems.Add("line " + lineNumber.ToString(CultureInfo.InvariantCulture) + ": empty varstore name");
                continue;
            }

            if (!Format.TryParseGuid(parts[1], out Guid guid))
            {
                problems.Add("line " + lineNumber.ToString(CultureInfo.InvariantCulture) + ": '" + parts[1].Trim() + "' is not a GUID");
                continue;
            }

            if (!Format.TryParseInt(parts[2], out int size))
            {
                problems.Add("line " + lineNumber.ToString(CultureInfo.InvariantCulture) + ": '" + parts[2].Trim() + "' is not a size (decimal or 0x hexadecimal)");
                continue;
            }

            entries.Add(new IfrVarstore(lineNumber, name, guid, size));
        }

        errors = problems;
        return entries;
    }

    /// <summary>Removes one pair of surrounding double quotes ("Setup" becomes Setup) and trims what is inside.</summary>
    /// <param name="name">The trimmed name field.</param>
    /// <returns>The name without the quotes.</returns>
    internal static string Unquote(string name)
    {
        return name.Length >= 2 && name[0] == '"' && name[^1] == '"' ? name[1..^1].Trim() : name;
    }
}
