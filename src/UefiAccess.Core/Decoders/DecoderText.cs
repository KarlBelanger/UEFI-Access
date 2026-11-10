// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Text;

namespace UefiAccess.Core.Decoders;

/// <summary>Culture-invariant text helpers shared by the decoders.</summary>
internal static class DecoderText
{
    /// <summary>Formats an interpolated string with the invariant culture.</summary>
    /// <param name="text">The interpolated string.</param>
    /// <returns>The formatted text.</returns>
    public static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    /// <summary>Renders "1 list" or "3 lists" style counts.</summary>
    /// <param name="count">The count.</param>
    /// <param name="singular">The singular noun.</param>
    /// <returns>The count followed by the noun, pluralised with "s" when the count is not 1.</returns>
    public static string Plural(int count, string singular)
    {
        return count == 1
            ? Invariant($"{count} {singular}")
            : Invariant($"{count} {singular}s");
    }

    /// <summary>Renders bytes as lowercase hexadecimal without separators.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The hexadecimal text; empty for no bytes.</returns>
    public static string Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    /// <summary>Renders bytes as lowercase hexadecimal pairs separated by colons, the MAC address form ("00:11:22:33:44:55").</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The colon-separated text; empty for no bytes.</returns>
    public static string ColonHex(ReadOnlySpan<byte> bytes)
    {
        StringBuilder builder = new(bytes.Length * 3);
        for (int i = 0; i < bytes.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(':');
            }

            builder.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
