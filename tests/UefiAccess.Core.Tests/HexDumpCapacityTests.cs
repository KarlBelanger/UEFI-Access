// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Text;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The starting capacity of <see cref="HexDump.Format"/>: it used to be data.Length * 5 in 32-bit
/// arithmetic, which overflows above about 429 MB and threw instead of dumping. It is now the exact
/// dump length computed in 64 bits and capped, so no input length can make it throw.
/// </summary>
public sealed class HexDumpCapacityTests
{
    [Theory]
    [InlineData(1, 16)]
    [InlineData(16, 16)]
    [InlineData(17, 16)]
    [InlineData(100, 16)]
    [InlineData(33, 8)]
    [InlineData(5, 1)]
    public void InitialCapacity_IsTheExactLengthOfTheDump(int length, int bytesPerLine)
    {
        byte[] data = new byte[length];
        new Random(length).NextBytes(data);

        Assert.Equal(HexDump.Format(data, bytesPerLine).Length, HexDump.InitialCapacity(length, bytesPerLine));
    }

    [Theory]
    [InlineData(429_496_730L)]
    [InlineData(int.MaxValue)]
    [InlineData(long.MaxValue / 2)]
    public void InitialCapacity_ForHugeInputs_IsCappedInsteadOfOverflowing(long length)
    {
        int capacity = 0;
        Exception? exception = Record.Exception(() => capacity = HexDump.InitialCapacity(length, HexDump.DefaultBytesPerLine));

        Assert.Null(exception);
        Assert.Equal(HexDump.MaximumInitialCapacity, capacity);
    }

    [Fact]
    public void InitialCapacity_EmptyInputOrWideLines_StaysInRange()
    {
        Assert.Equal(0, HexDump.InitialCapacity(0, HexDump.DefaultBytesPerLine));
        Assert.Equal(0, HexDump.InitialCapacity(10, 0));
        Assert.Equal(HexDump.MaximumInitialCapacity, HexDump.InitialCapacity(3, int.MaxValue));
    }

    [Fact]
    public void Format_OfTheLargestReadableVariable_Completes()
    {
        // 4 MiB is the largest variable the live store reads (the baseline reader stops at 1 MiB).
        byte[] data = new byte[4 * 1024 * 1024];

        string dump = HexDump.Format(data);

        // 76 characters per line (offset, hex, gap, ASCII column) plus the line break; the dump is
        // larger than the capped starting capacity, so the builder grew on demand.
        Assert.Equal((data.Length / 16) * (76 + Environment.NewLine.Length), dump.Length);
        Assert.True(dump.Length > HexDump.MaximumInitialCapacity);
        Assert.StartsWith("00000000  00 00", dump, StringComparison.Ordinal);
    }
}
