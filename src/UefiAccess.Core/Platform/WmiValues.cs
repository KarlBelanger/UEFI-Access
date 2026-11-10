// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;

namespace UefiAccess.Core.Platform;

/// <summary>
/// Pure conversions for values as WMI returns them (strings, numbers, booleans, arrays and
/// CIM_DATETIME text). No WMI dependency, so the methods run and are testable on any host.
/// </summary>
internal static class WmiValues
{
    /// <summary>Renders a WMI value as trimmed text; arrays are joined with ", ". Null or blank becomes null.</summary>
    /// <param name="value">The raw property value.</param>
    /// <returns>The text, or null when there is nothing to show.</returns>
    public static string? ToText(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case string text:
                {
                    string trimmed = text.Trim();
                    return trimmed.Length == 0 ? null : trimmed;
                }

            case Array array:
                {
                    string[] parts = array.Cast<object?>().Select(ToText).Where(p => p is not null).Select(p => p!).ToArray();
                    return parts.Length == 0 ? null : string.Join(", ", parts);
                }

            case bool flag:
                return flag ? "True" : "False";
            case IFormattable formattable:
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            default:
                return value.ToString();
        }
    }

    /// <summary>Renders a WMI value as a list of trimmed strings: one per array element, or one entry for a scalar.</summary>
    /// <param name="value">The raw property value.</param>
    /// <returns>The list; empty when the value is null or blank.</returns>
    public static IReadOnlyList<string> ToTextList(object? value)
    {
        if (value is null)
        {
            return Array.Empty<string>();
        }

        if (value is Array array && value is not string)
        {
            return array.Cast<object?>().Select(ToText).Where(p => p is not null).Select(p => p!).ToArray();
        }

        string? text = ToText(value);
        return text is null ? Array.Empty<string>() : new[] { text };
    }

    /// <summary>Interprets a WMI value as a boolean: bool, any integer (non-zero is true), or "True"/"False"/number text.</summary>
    /// <param name="value">The raw property value.</param>
    /// <returns>The boolean, or null when the value is null or not interpretable.</returns>
    public static bool? ToNullableBool(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case bool flag:
                return flag;
            case byte or sbyte or short or ushort or int or uint or long or ulong:
                return Convert.ToDecimal(value, CultureInfo.InvariantCulture) != 0m;
            case string text:
                {
                    string trimmed = text.Trim();
                    if (bool.TryParse(trimmed, out bool parsed))
                    {
                        return parsed;
                    }

                    if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number))
                    {
                        return number != 0;
                    }

                    return null;
                }

            default:
                return null;
        }
    }

    /// <summary>Interprets a WMI value as an integer (any integral type or digit text that fits in an int).</summary>
    /// <param name="value">The raw property value.</param>
    /// <returns>The integer, or null when the value is null, not numeric, or out of range.</returns>
    public static int? ToNullableInt(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case int number:
                return number;
            case byte or sbyte or short or ushort or uint or long or ulong:
                {
                    decimal wide = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                    return wide >= int.MinValue && wide <= int.MaxValue ? (int)wide : null;
                }

            case string text:
                return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : null;
            default:
                return null;
        }
    }

    /// <summary>
    /// Interprets a method's ReturnValue output (0 on success, an HRESULT otherwise). A value that
    /// is not a number fitting a UINT32 is a failure (<see cref="uint.MaxValue"/>); a missing value
    /// is <paramref name="whenMissing"/>, which the caller chooses: a read-only method treats it as
    /// success, as M1 did, and the one state-changing call as a failure.
    /// </summary>
    /// <param name="value">The raw ReturnValue property, or null when it was not reported.</param>
    /// <param name="whenMissing">The value for a missing ReturnValue.</param>
    /// <returns>The return value.</returns>
    public static uint ToReturnValue(object? value, uint whenMissing)
    {
        if (value is null)
        {
            return whenMissing;
        }

        try
        {
            return Convert.ToUInt32(value, CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return uint.MaxValue;
        }
    }

    /// <summary>
    /// Formats a CIM_DATETIME value such as "20140723000000.000000+000" as "2014-07-23".
    /// Text that does not start with eight digits is returned trimmed and unchanged.
    /// </summary>
    /// <param name="value">The raw CIM_DATETIME text.</param>
    /// <returns>The date text, or null when the input is null or blank.</returns>
    public static string? FormatCimDateTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        if (trimmed.Length >= 8 && trimmed.Take(8).All(char.IsAsciiDigit))
        {
            return trimmed[..4] + "-" + trimmed[4..6] + "-" + trimmed[6..8];
        }

        return trimmed;
    }
}
