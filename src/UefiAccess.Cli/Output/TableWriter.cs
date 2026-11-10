// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Text;

namespace UefiAccess.Cli.Output;

/// <summary>
/// Renders plain-text tables for screen readers: a header row followed by data rows, columns
/// left-aligned and padded with spaces, one space between columns, no box-drawing characters,
/// and no truncation of wide values.
/// </summary>
internal sealed class TableWriter
{
    private readonly string[] _headers;
    private readonly List<string[]> _rows = new();

    /// <summary>Creates a table with the given column headers.</summary>
    /// <param name="headers">Column headers, left to right.</param>
    public TableWriter(params string[] headers)
    {
        _headers = headers is null ? Array.Empty<string>() : headers.Select(Clean).ToArray();
    }

    /// <summary>Number of data rows added so far.</summary>
    public int RowCount => _rows.Count;

    /// <summary>Adds one row. Missing cells are blank; extra cells are kept and widen the table.</summary>
    /// <param name="cells">Cell values; null renders as an empty cell, other values through ToString.</param>
    public void AddRow(params object?[] cells)
    {
        object?[] source = cells ?? Array.Empty<object?>();
        _rows.Add(source.Select(CellText).ToArray());
    }

    /// <summary>Renders the table as text with a trailing newline after every line.</summary>
    /// <returns>The rendered table; an empty string when there are no headers and no rows.</returns>
    public string Render()
    {
        StringBuilder builder = new();
        Write(new StringWriter(builder, CultureInfo.InvariantCulture));
        return builder.ToString();
    }

    /// <summary>Writes the table to <paramref name="writer"/>.</summary>
    /// <param name="writer">Destination.</param>
    public void Write(TextWriter writer)
    {
        int columns = Math.Max(_headers.Length, _rows.Count == 0 ? 0 : _rows.Max(r => r.Length));
        if (columns == 0)
        {
            return;
        }

        int[] widths = new int[columns];
        for (int i = 0; i < _headers.Length; i++)
        {
            widths[i] = Math.Max(widths[i], _headers[i].Length);
        }

        foreach (string[] row in _rows)
        {
            for (int i = 0; i < row.Length; i++)
            {
                widths[i] = Math.Max(widths[i], row[i].Length);
            }
        }

        if (_headers.Length > 0)
        {
            WriteLine(writer, _headers, widths);
        }

        foreach (string[] row in _rows)
        {
            WriteLine(writer, row, widths);
        }
    }

    private static void WriteLine(TextWriter writer, string[] cells, int[] widths)
    {
        StringBuilder line = new();
        for (int i = 0; i < widths.Length; i++)
        {
            string cell = i < cells.Length ? cells[i] : string.Empty;
            bool last = i == widths.Length - 1;
            line.Append(cell);
            if (!last)
            {
                line.Append(' ', widths[i] - cell.Length + 1);
            }
        }

        writer.WriteLine(line.ToString().TrimEnd());
    }

    private static string CellText(object? value)
    {
        return value switch
        {
            null => string.Empty,
            string s => Clean(s),
            bool b => b ? "true" : "false",
            IFormattable f => Clean(f.ToString(null, CultureInfo.InvariantCulture)),
            _ => Clean(value.ToString() ?? string.Empty),
        };
    }

    private static string Clean(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // Keep every row on one line so column alignment survives.
        return text.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
    }
}
