// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using UefiAccess.Core.Variables;

namespace UefiAccess.Cli.Services;

/// <summary>Small text formatting helpers shared by the commands.</summary>
internal static class Format
{
    /// <summary>Renders attributes as "0x00000007 NV BS RT".</summary>
    /// <param name="attributes">The attributes.</param>
    /// <returns>The text.</returns>
    public static string Attributes(VariableAttributes attributes)
    {
        string flags = ShortFlags(attributes);
        string hex = "0x" + ((uint)attributes).ToString("X8", CultureInfo.InvariantCulture);
        return flags.Length == 0 ? hex : hex + " " + flags;
    }

    /// <summary>Short flag tokens ("NV BS RT"); never throws.</summary>
    /// <param name="attributes">The attributes.</param>
    /// <returns>The tokens, or an empty string.</returns>
    public static string ShortFlags(VariableAttributes attributes)
    {
        try
        {
            return VariableAttributeFlags.ToShortFlags(attributes) ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>Full attribute names; never throws.</summary>
    /// <param name="attributes">The attributes.</param>
    /// <returns>The names, possibly empty.</returns>
    public static IReadOnlyList<string> AttributeNames(VariableAttributes attributes)
    {
        try
        {
            return VariableAttributeFlags.Names(attributes) ?? Array.Empty<string>();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>"1 byte" or "N bytes".</summary>
    /// <param name="count">The byte count.</param>
    /// <returns>The text.</returns>
    public static string Bytes(long count)
    {
        return count == 1 ? "1 byte" : count.ToString(CultureInfo.InvariantCulture) + " bytes";
    }

    /// <summary>"true", "false" or the null text.</summary>
    /// <param name="value">The value.</param>
    /// <param name="whenNull">Text for null.</param>
    /// <returns>The text.</returns>
    public static string Bool(bool? value, string whenNull = "unknown")
    {
        return value is null ? whenNull : value.Value ? "true" : "false";
    }

    /// <summary>The text, or "(none)" when null or blank.</summary>
    /// <param name="value">The value.</param>
    /// <param name="whenEmpty">Text for null or blank.</param>
    /// <returns>The text.</returns>
    public static string Text(string? value, string whenEmpty = "(none)")
    {
        return string.IsNullOrWhiteSpace(value) ? whenEmpty : value;
    }

    /// <summary>Joins values with ", " or returns the empty text.</summary>
    /// <param name="values">The values.</param>
    /// <param name="whenEmpty">Text when there are no values.</param>
    /// <returns>The text.</returns>
    public static string Join(IEnumerable<string>? values, string whenEmpty = "(none)")
    {
        if (values is null)
        {
            return whenEmpty;
        }

        string joined = string.Join(", ", values);
        return joined.Length == 0 ? whenEmpty : joined;
    }

    /// <summary>Parses a GUID in any of the usual forms (with or without braces or hyphens). Never throws.</summary>
    /// <param name="text">The text.</param>
    /// <param name="guid">Receives the GUID.</param>
    /// <returns>True when parsed.</returns>
    public static bool TryParseGuid(string? text, out Guid guid)
    {
        guid = Guid.Empty;
        return !string.IsNullOrWhiteSpace(text) && Guid.TryParse(text.Trim(), out guid);
    }

    /// <summary>Parses a decimal or 0x-prefixed hexadecimal non-negative integer. Never throws.</summary>
    /// <param name="text">The text.</param>
    /// <param name="value">Receives the value.</param>
    /// <returns>True when parsed.</returns>
    public static bool TryParseInt(string? text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(trimmed.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value) && value >= 0;
        }

        return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 0;
    }

    /// <summary>Local timestamp "yyyyMMdd-HHmmss" used as the baseline run id.</summary>
    /// <param name="timestamp">The time.</param>
    /// <returns>The run id.</returns>
    public static string RunId(DateTimeOffset timestamp)
    {
        return timestamp.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
    }
}
