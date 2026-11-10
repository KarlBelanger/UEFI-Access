// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Text;

namespace UefiAccess.Core.Text;

/// <summary>
/// Formats bytes as a classic hex dump, shared by the CLI (<c>read --hex</c>) and the GUI detail
/// pane so both render a variable's raw data identically: an eight-digit hexadecimal offset, two
/// spaces, the bytes as two-digit hexadecimal groups each followed by one space (with one extra
/// space after the first half of the line), one more space, then an ASCII column in which every
/// byte outside 0x20 to 0x7E shows as ".". A short final line keeps its full width, so the ASCII
/// column starts at the same position on every line. Sixteen bytes per line by default.
/// </summary>
public static class HexDump
{
    /// <summary>The default number of bytes per line.</summary>
    public const int DefaultBytesPerLine = 16;

    /// <summary>Largest starting capacity <see cref="Format"/> reserves: 16 Mi characters (32 MiB).</summary>
    internal const int MaximumInitialCapacity = 16 * 1024 * 1024;

    /// <summary>Renders the dump as one string per line, without line terminators. Empty input yields no lines.</summary>
    /// <param name="data">The bytes to render.</param>
    /// <param name="bytesPerLine">Bytes per line; at least 1, 16 by default.</param>
    /// <returns>The lines, in offset order.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bytesPerLine"/> is less than 1.</exception>
    public static IReadOnlyList<string> Lines(ReadOnlySpan<byte> data, int bytesPerLine = DefaultBytesPerLine)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bytesPerLine, 1);

        List<string> lines = new((int)((data.Length + (long)bytesPerLine - 1) / bytesPerLine));
        StringBuilder builder = new((int)Math.Min((bytesPerLine * 4L) + 16, MaximumInitialCapacity));
        for (int offset = 0; offset < data.Length; offset += bytesPerLine)
        {
            builder.Clear();
            AppendLine(builder, data, offset, bytesPerLine);
            lines.Add(builder.ToString());
        }

        return lines;
    }

    /// <summary>
    /// Renders the dump as one block of text in which every line ends with
    /// <see cref="Environment.NewLine"/>. Empty input renders as an empty string.
    /// </summary>
    /// <param name="data">The bytes to render.</param>
    /// <param name="bytesPerLine">Bytes per line; at least 1, 16 by default.</param>
    /// <returns>The dump text.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bytesPerLine"/> is less than 1.</exception>
    public static string Format(ReadOnlySpan<byte> data, int bytesPerLine = DefaultBytesPerLine)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bytesPerLine, 1);

        StringBuilder builder = new(InitialCapacity(data.Length, bytesPerLine));
        for (int offset = 0; offset < data.Length; offset += bytesPerLine)
        {
            AppendLine(builder, data, offset, bytesPerLine);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    /// <summary>
    /// The starting capacity for <see cref="Format"/>: the exact length of the dump, computed in
    /// 64-bit arithmetic so a large input cannot overflow it, and capped at
    /// <see cref="MaximumInitialCapacity"/> so the builder grows on demand beyond that instead of
    /// reserving everything up front.
    /// </summary>
    /// <param name="length">Number of bytes to dump.</param>
    /// <param name="bytesPerLine">Bytes per line; at least 1.</param>
    /// <returns>A capacity between 0 and <see cref="MaximumInitialCapacity"/>.</returns>
    internal static int InitialCapacity(long length, int bytesPerLine)
    {
        if (length <= 0 || bytesPerLine < 1)
        {
            return 0;
        }

        long lines = (length + bytesPerLine - 1) / bytesPerLine;
        long lineLength = 8L + 2 + (3L * bytesPerLine) + (bytesPerLine / 2 > 0 ? 1 : 0) + 1 + bytesPerLine + Environment.NewLine.Length;
        if (lines > MaximumInitialCapacity / lineLength)
        {
            return MaximumInitialCapacity;
        }

        // The ASCII column of a short final line holds only the bytes that are there.
        long missingAscii = (lines * bytesPerLine) - length;
        return (int)Math.Min((lines * lineLength) - missingAscii, MaximumInitialCapacity);
    }

    private static void AppendLine(StringBuilder builder, ReadOnlySpan<byte> data, int offset, int bytesPerLine)
    {
        int count = Math.Min(bytesPerLine, data.Length - offset);
        int gap = bytesPerLine / 2;
        builder.Append(offset.ToString("X8", CultureInfo.InvariantCulture));
        builder.Append("  ");
        for (int i = 0; i < bytesPerLine; i++)
        {
            if (gap > 0 && i == gap)
            {
                builder.Append(' ');
            }

            if (i < count)
            {
                builder.Append(data[offset + i].ToString("X2", CultureInfo.InvariantCulture));
            }
            else
            {
                builder.Append("  ");
            }

            builder.Append(' ');
        }

        builder.Append(' ');
        for (int i = 0; i < count; i++)
        {
            byte b = data[offset + i];
            builder.Append(b is >= 0x20 and <= 0x7E ? (char)b : '.');
        }
    }
}
