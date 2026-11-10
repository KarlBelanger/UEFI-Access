// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Presentation.Diagnostics;
using Xunit;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// The input trace is off by default and is turned on only by the exact switch --trace-input (in
/// any case) on UefiAccess.App's command line; every other argument behaves as it always has (the
/// application reads no other), and the arguments are not changed.
/// </summary>
public sealed class InputTraceSwitchTests
{
    [Fact]
    public void TheSwitch_IsTraceInput()
    {
        Assert.Equal("--trace-input", InputTraceSwitch.Name);
    }

    [Fact]
    public void NoArguments_IsOff()
    {
        Assert.False(InputTraceSwitch.IsRequested(null));
        Assert.False(InputTraceSwitch.IsRequested(Array.Empty<string>()));
    }

    [Theory]
    [InlineData("--trace-inputs")]
    [InlineData("-trace-input")]
    [InlineData("trace-input")]
    [InlineData("/trace-input")]
    [InlineData("--trace")]
    [InlineData("--trace-input=yes")]
    [InlineData("--trace input")]
    [InlineData("C:\\baselines\\optiplex")]
    [InlineData("")]
    public void AnythingButTheExactSwitch_IsOff(string argument)
    {
        Assert.False(InputTraceSwitch.IsRequested(new[] { argument }));
        Assert.False(InputTraceSwitch.IsRequested(new[] { "--verbose", argument, "/s" }));
    }

    [Theory]
    [InlineData("--trace-input")]
    [InlineData("--TRACE-INPUT")]
    [InlineData("--Trace-Input")]
    [InlineData("  --trace-input ")]
    public void TheSwitch_InAnyCase_IsOn(string argument)
    {
        Assert.True(InputTraceSwitch.IsRequested(new[] { argument }));
    }

    [Fact]
    public void TheSwitchAmongOtherArguments_IsOn_AndTheArgumentsAreNotChanged()
    {
        string[] args = { "--verbose", "--trace-input", "C:\\baselines\\optiplex" };
        string[] before = (string[])args.Clone();

        Assert.True(InputTraceSwitch.IsRequested(args));

        Assert.Equal(before, args);
    }

    [Fact]
    public void ANullArgument_IsSkipped()
    {
        Assert.False(InputTraceSwitch.IsRequested(new string[] { null! }));
        Assert.True(InputTraceSwitch.IsRequested(new string[] { null!, "--trace-input" }));
    }
}
