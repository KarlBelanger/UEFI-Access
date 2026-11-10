// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Variables;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>Tests for the short attribute flag rendering and parsing.</summary>
public sealed class AttributeTests
{
    private const VariableAttributes NvBsRt = VariableAttributes.NonVolatile | VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess;

    [Fact]
    public void ToShortFlags_RendersInFixedOrder()
    {
        Assert.Equal("NV BS RT", VariableAttributeFlags.ToShortFlags(NvBsRt));
        Assert.Equal("NV BS RT HR AW TA AP EA", VariableAttributeFlags.ToShortFlags((VariableAttributes)0xFF));
        Assert.Equal("BS RT TA", VariableAttributeFlags.ToShortFlags(VariableAttributes.TimeBasedAuthenticatedWriteAccess | VariableAttributes.RuntimeAccess | VariableAttributes.BootServiceAccess));
        Assert.Equal("EA", VariableAttributeFlags.ToShortFlags(VariableAttributes.EnhancedAuthenticatedAccess));
        Assert.Equal("HR AP", VariableAttributeFlags.ToShortFlags(VariableAttributes.AppendWrite | VariableAttributes.HardwareErrorRecord));
    }

    [Fact]
    public void ToShortFlags_NoneIsEmpty()
    {
        Assert.Equal(string.Empty, VariableAttributeFlags.ToShortFlags(VariableAttributes.None));
    }

    [Fact]
    public void ToShortFlags_UndefinedBitsAppearAsHexToken()
    {
        Assert.Equal("NV RT 0x00000100", VariableAttributeFlags.ToShortFlags((VariableAttributes)0x105));
        Assert.Equal("0x80000000", VariableAttributeFlags.ToShortFlags((VariableAttributes)0x80000000));
    }

    [Theory]
    [InlineData("NV BS RT", 0x7u)]
    [InlineData("nv bs rt", 0x7u)]
    [InlineData("NV,BS,RT", 0x7u)]
    [InlineData("nv, bs ,rt", 0x7u)]
    [InlineData("RT NV", 0x5u)]
    [InlineData("  NV\tBS  ", 0x3u)]
    [InlineData("NV|BS|RT", 0x7u)]
    [InlineData("0x7", 0x7u)]
    [InlineData("0X27", 0x27u)]
    [InlineData("7", 0x7u)]
    [InlineData("39", 0x27u)]
    [InlineData("0", 0u)]
    [InlineData("NonVolatile RuntimeAccess", 0x5u)]
    [InlineData("nonvolatile,TimeBasedAuthenticatedWriteAccess", 0x21u)]
    [InlineData("None", 0u)]
    [InlineData("NV 0x100", 0x101u)]
    [InlineData("NV RT 0x00000100", 0x105u)]
    [InlineData("", 0u)]
    [InlineData("   ", 0u)]
    public void TryParse_AcceptsTokensAndNumbers(string text, uint expected)
    {
        bool ok = VariableAttributeFlags.TryParse(text, out VariableAttributes attributes);

        Assert.True(ok);
        Assert.Equal((VariableAttributes)expected, attributes);
    }

    [Theory]
    [InlineData("bogus")]
    [InlineData("NV XX")]
    [InlineData("0xZZ")]
    [InlineData("-1")]
    [InlineData("4294967296")]
    [InlineData("N V")]
    [InlineData("NVBS")]
    public void TryParse_RejectsUnknownText(string text)
    {
        bool ok = VariableAttributeFlags.TryParse(text, out VariableAttributes attributes);

        Assert.False(ok);
        Assert.Equal(VariableAttributes.None, attributes);
    }

    [Fact]
    public void TryParse_NullIsRejectedWithoutThrowing()
    {
        bool ok = false;
        VariableAttributes attributes = VariableAttributes.RuntimeAccess;
        Exception? exception = Record.Exception(() => ok = VariableAttributeFlags.TryParse(null!, out attributes));

        Assert.Null(exception);
        Assert.False(ok);
        Assert.Equal(VariableAttributes.None, attributes);
    }

    [Fact]
    public void RoundTrip_EveryDefinedCombination()
    {
        for (uint value = 0; value <= 0xFF; value++)
        {
            VariableAttributes original = (VariableAttributes)value;
            string text = VariableAttributeFlags.ToShortFlags(original);

            Assert.True(VariableAttributeFlags.TryParse(text, out VariableAttributes parsed), text);
            Assert.Equal(original, parsed);
        }
    }

    [Fact]
    public void RoundTrip_IncludesUndefinedBits()
    {
        VariableAttributes original = (VariableAttributes)0x8000_0107;
        string text = VariableAttributeFlags.ToShortFlags(original);

        Assert.True(VariableAttributeFlags.TryParse(text, out VariableAttributes parsed));
        Assert.Equal(original, parsed);
    }

    [Fact]
    public void RoundTrip_NumericInput()
    {
        Assert.True(VariableAttributeFlags.TryParse("0x7", out VariableAttributes parsed));
        Assert.Equal("NV BS RT", VariableAttributeFlags.ToShortFlags(parsed));
    }

    [Fact]
    public void Names_ReturnsFullEnumNamesInBitOrder()
    {
        Assert.Equal(new[] { "NonVolatile", "RuntimeAccess" }, VariableAttributeFlags.Names(VariableAttributes.RuntimeAccess | VariableAttributes.NonVolatile));
        Assert.Equal(
            new[] { "NonVolatile", "BootServiceAccess", "RuntimeAccess", "HardwareErrorRecord", "AuthenticatedWriteAccess", "TimeBasedAuthenticatedWriteAccess", "AppendWrite", "EnhancedAuthenticatedAccess" },
            VariableAttributeFlags.Names((VariableAttributes)0xFF));
        Assert.Empty(VariableAttributeFlags.Names(VariableAttributes.None));
        Assert.Equal(new[] { "NonVolatile" }, VariableAttributeFlags.Names((VariableAttributes)0x101));
    }

    [Fact]
    public void Names_ParseBackThroughTryParse()
    {
        VariableAttributes original = (VariableAttributes)0xFF;
        string text = string.Join(", ", VariableAttributeFlags.Names(original));

        Assert.True(VariableAttributeFlags.TryParse(text, out VariableAttributes parsed));
        Assert.Equal(original, parsed);
    }
}
