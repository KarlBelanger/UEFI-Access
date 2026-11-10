// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using UefiAccess.Presentation.Resources;
using UefiAccess.Presentation.ViewModels;

namespace UefiAccess.Presentation.Services;

/// <summary>
/// Renders the rows that pass the filter (rows in collapsed groups included) as plain text
/// (aligned columns, no box-drawing characters, no truncation) or JSON for the Export visible
/// list command. Writes nothing itself;
/// the shell writes the returned text to the file the user picked. The plain-text layout follows
/// the CLI's table convention: a header row, columns left-aligned and padded so the widest cell is
/// followed by exactly one space, trailing spaces trimmed, every value on one line.
/// </summary>
public static class VisibleListExporter
{
    /// <summary>The plain-text column headers, in order: Name, Namespace, Flags, Size, Kind, Summary.</summary>
    public static IReadOnlyList<string> Headers { get; } = new[]
    {
        Strings.ColumnName,
        Strings.ColumnNamespace,
        Strings.ColumnFlags,
        Strings.ColumnSize,
        Strings.ColumnKind,
        Strings.ColumnSummary,
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Renders the rows as a plain-text table (header row first, one line per row, each line ending with a newline).</summary>
    /// <param name="items">The visible rows; null is treated as empty.</param>
    /// <returns>The text; empty when there are no rows.</returns>
    public static string ToPlainText(IEnumerable<VariableItemViewModel> items)
    {
        List<string[]> rows = Rows(items);
        if (rows.Count == 0)
        {
            return string.Empty;
        }

        int columns = Headers.Count;
        int[] widths = new int[columns];
        for (int i = 0; i < columns; i++)
        {
            widths[i] = Headers[i].Length;
        }

        foreach (string[] row in rows)
        {
            for (int i = 0; i < columns; i++)
            {
                widths[i] = Math.Max(widths[i], row[i].Length);
            }
        }

        StringBuilder text = new();
        AppendLine(text, Headers, widths);
        foreach (string[] row in rows)
        {
            AppendLine(text, row, widths);
        }

        return text.ToString();
    }

    /// <summary>
    /// Renders the rows as an indented JSON array of objects with the properties key, name, guid,
    /// namespace, flags, size (a number), kind and summary.
    /// </summary>
    /// <param name="items">The visible rows; null is treated as empty.</param>
    /// <returns>The JSON text; "[]" when there are no rows.</returns>
    public static string ToJson(IEnumerable<VariableItemViewModel> items)
    {
        List<ExportRow> rows = (items ?? Enumerable.Empty<VariableItemViewModel>())
            .Where(item => item is not null)
            .Select(item => new ExportRow(item.Key, item.Name, item.GuidText, item.NamespaceLabel, item.Flags, item.Size, item.Kind, item.Summary))
            .ToList();
        return JsonSerializer.Serialize(rows, JsonOptions);
    }

    private static List<string[]> Rows(IEnumerable<VariableItemViewModel> items)
    {
        List<string[]> rows = new();
        foreach (VariableItemViewModel item in items ?? Enumerable.Empty<VariableItemViewModel>())
        {
            if (item is null)
            {
                continue;
            }

            rows.Add(new[]
            {
                OneLine(item.Name),
                OneLine(item.NamespaceLabel),
                OneLine(item.Flags),
                item.Size.ToString(CultureInfo.InvariantCulture),
                OneLine(item.Kind),
                OneLine(item.Summary),
            });
        }

        return rows;
    }

    private static void AppendLine(StringBuilder text, IReadOnlyList<string> cells, int[] widths)
    {
        StringBuilder line = new();
        for (int i = 0; i < widths.Length; i++)
        {
            string cell = i < cells.Count ? cells[i] : string.Empty;
            line.Append(cell);
            if (i < widths.Length - 1)
            {
                line.Append(' ', widths[i] - cell.Length + 1);
            }
        }

        text.Append(line.ToString().TrimEnd()).Append(Environment.NewLine);
    }

    private static string OneLine(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
    }

    /// <summary>One exported row; the property order is the JSON property order.</summary>
    private sealed record ExportRow(string Key, string Name, string Guid, string Namespace, string Flags, int Size, string Kind, string Summary);
}
