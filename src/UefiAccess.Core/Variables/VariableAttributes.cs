// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;

namespace UefiAccess.Core.Variables;

/// <summary>EFI variable attributes (UEFI 2.10, section 8.2).</summary>
[Flags]
public enum VariableAttributes : uint
{
    /// <summary>No attributes.</summary>
    None = 0,

    /// <summary>EFI_VARIABLE_NON_VOLATILE: the variable survives a reset.</summary>
    NonVolatile = 0x1,

    /// <summary>EFI_VARIABLE_BOOTSERVICE_ACCESS: readable before ExitBootServices.</summary>
    BootServiceAccess = 0x2,

    /// <summary>EFI_VARIABLE_RUNTIME_ACCESS: readable from the operating system; only these are visible to M1.</summary>
    RuntimeAccess = 0x4,

    /// <summary>EFI_VARIABLE_HARDWARE_ERROR_RECORD.</summary>
    HardwareErrorRecord = 0x8,

    /// <summary>EFI_VARIABLE_AUTHENTICATED_WRITE_ACCESS (deprecated in UEFI 2.7 but still reported).</summary>
    AuthenticatedWriteAccess = 0x10,

    /// <summary>EFI_VARIABLE_TIME_BASED_AUTHENTICATED_WRITE_ACCESS.</summary>
    TimeBasedAuthenticatedWriteAccess = 0x20,

    /// <summary>EFI_VARIABLE_APPEND_WRITE.</summary>
    AppendWrite = 0x40,

    /// <summary>EFI_VARIABLE_ENHANCED_AUTHENTICATED_ACCESS.</summary>
    EnhancedAuthenticatedAccess = 0x80,
}

/// <summary>
/// Renders and parses variable attributes as the short flag tokens "NV BS RT HR AW TA AP EA".
/// Bits outside the eight defined flags are never dropped by <see cref="ToShortFlags"/>: they are
/// rendered as one trailing hexadecimal token (for example "0x00000100") that
/// <see cref="TryParse"/> understands, so rendering and parsing round-trip for every value.
/// </summary>
public static class VariableAttributeFlags
{
    private static readonly char[] Separators = { ' ', ',', '\t', '|', '+', ';', '\r', '\n' };

    /// <summary>The eight defined flags in ascending bit order with their short token and full name.</summary>
    private static readonly (VariableAttributes Flag, string Short, string Name)[] Table =
    {
        (VariableAttributes.NonVolatile, "NV", nameof(VariableAttributes.NonVolatile)),
        (VariableAttributes.BootServiceAccess, "BS", nameof(VariableAttributes.BootServiceAccess)),
        (VariableAttributes.RuntimeAccess, "RT", nameof(VariableAttributes.RuntimeAccess)),
        (VariableAttributes.HardwareErrorRecord, "HR", nameof(VariableAttributes.HardwareErrorRecord)),
        (VariableAttributes.AuthenticatedWriteAccess, "AW", nameof(VariableAttributes.AuthenticatedWriteAccess)),
        (VariableAttributes.TimeBasedAuthenticatedWriteAccess, "TA", nameof(VariableAttributes.TimeBasedAuthenticatedWriteAccess)),
        (VariableAttributes.AppendWrite, "AP", nameof(VariableAttributes.AppendWrite)),
        (VariableAttributes.EnhancedAuthenticatedAccess, "EA", nameof(VariableAttributes.EnhancedAuthenticatedAccess)),
    };

    /// <summary>Mask of the eight defined flags.</summary>
    private const uint DefinedMask = 0xFF;

    /// <summary>
    /// Renders attributes as space-separated short tokens in the fixed order NV BS RT HR AW TA AP EA
    /// (only the set flags appear; an empty string for <see cref="VariableAttributes.None"/>).
    /// Undefined bits are appended as a single "0x" hexadecimal token so no information is lost.
    /// </summary>
    /// <param name="attributes">The attributes to render.</param>
    /// <returns>The short flag text.</returns>
    public static string ToShortFlags(VariableAttributes attributes)
    {
        uint value = (uint)attributes;
        List<string> tokens = new(Table.Length + 1);
        foreach ((VariableAttributes flag, string shortToken, _) in Table)
        {
            if ((value & (uint)flag) != 0)
            {
                tokens.Add(shortToken);
            }
        }

        uint undefined = value & ~DefinedMask;
        if (undefined != 0)
        {
            tokens.Add("0x" + undefined.ToString("X8", CultureInfo.InvariantCulture));
        }

        return string.Join(' ', tokens);
    }

    /// <summary>
    /// Parses short tokens (space or comma separated, any case) or a hexadecimal (0x prefix) or
    /// decimal number into attributes. Full enum names such as "NonVolatile" are accepted as well,
    /// and numeric tokens may be mixed with flag tokens; all tokens are combined with bitwise OR.
    /// Empty or whitespace-only text parses as <see cref="VariableAttributes.None"/>. Never throws.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="attributes">Receives the parsed attributes, or <see cref="VariableAttributes.None"/> on failure.</param>
    /// <returns>True when the whole text was understood.</returns>
    public static bool TryParse(string text, out VariableAttributes attributes)
    {
        attributes = VariableAttributes.None;
        if (text is null)
        {
            return false;
        }

        uint value = 0;
        string[] tokens = text.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string token in tokens)
        {
            if (!TryParseToken(token, out uint tokenValue))
            {
                attributes = VariableAttributes.None;
                return false;
            }

            value |= tokenValue;
        }

        attributes = (VariableAttributes)value;
        return true;
    }

    /// <summary>Returns the full enum names of every set defined flag, in ascending bit order.</summary>
    /// <param name="attributes">The attributes to name.</param>
    /// <returns>The names; empty for <see cref="VariableAttributes.None"/>. Undefined bits produce no name.</returns>
    public static IReadOnlyList<string> Names(VariableAttributes attributes)
    {
        uint value = (uint)attributes;
        List<string> names = new(Table.Length);
        foreach ((VariableAttributes flag, _, string name) in Table)
        {
            if ((value & (uint)flag) != 0)
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static bool TryParseToken(string token, out uint value)
    {
        value = 0;
        foreach ((VariableAttributes flag, string shortToken, string name) in Table)
        {
            if (string.Equals(token, shortToken, StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, name, StringComparison.OrdinalIgnoreCase))
            {
                value = (uint)flag;
                return true;
            }
        }

        if (string.Equals(token, nameof(VariableAttributes.None), StringComparison.OrdinalIgnoreCase))
        {
            value = 0;
            return true;
        }

        if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.TryParse(token.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
        }

        return uint.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
