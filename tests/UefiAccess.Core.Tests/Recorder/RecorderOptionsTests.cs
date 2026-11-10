// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.UiaTrace.Recording;
using Xunit;

namespace UefiAccess.Core.Tests.Recorder;

/// <summary>
/// The command line of the UI Automation recorder (uefiaccess-uia-trace), for the tester's A2-7 question
/// (A1-6 continued): no arguments record every event family into the default file; --out and
/// --events take their value as the next argument; the joined "option=value" form, an unknown
/// argument or event family, a missing value and a repeated option are errors that end with the
/// usage hint; --help, -h and /? ask for the help whatever else is given. The parser is the
/// recorder's own source, compiled into the tests as a linked file.
/// </summary>
public sealed class RecorderOptionsTests
{
    private const string AllFamilies = "focus, selection, structure, live, notification, async, properties";

    [Fact]
    public void NoArguments_RecordEveryFamilyIntoTheDefaultFile()
    {
        foreach (RecorderOptions options in new[] { RecorderOptions.Parse(null), RecorderOptions.Parse(Array.Empty<string>()) })
        {
            Assert.Null(options.Error);
            Assert.False(options.Help);
            Assert.Null(options.OutPath);
            Assert.Equal(RecorderEvents.All, options.Events);
        }

        Assert.Equal(AllFamilies, RecorderOptions.Describe(RecorderEvents.All));
        Assert.Equal("none", RecorderOptions.Describe(RecorderEvents.None));
    }

    [Fact]
    public void Out_TakesThePathFromTheNextArgument()
    {
        RecorderOptions options = RecorderOptions.Parse(new[] { "--out", @"C:\Temp\karl uia.log" });

        Assert.Null(options.Error);
        Assert.Equal(@"C:\Temp\karl uia.log", options.OutPath);
        Assert.Equal(RecorderEvents.All, options.Events);

        Assert.Equal(@"D:\traces", RecorderOptions.Parse(new[] { "--OUT", @"D:\traces" }).OutPath);
    }

    [Theory]
    [InlineData("--out")]
    [InlineData("--out", "--events", "focus")]
    [InlineData("--out", "")]
    [InlineData("--out", "--help-me")]
    public void Out_WithoutAPath_IsAnError(params string[] args)
    {
        RecorderOptions options = RecorderOptions.Parse(args);

        Assert.NotNull(options.Error);
        Assert.Contains("--out needs a path", options.Error, StringComparison.Ordinal);
        Assert.EndsWith(RecorderOptions.UsageHint, options.Error, StringComparison.Ordinal);
        Assert.Null(options.OutPath);
    }

    [Theory]
    [InlineData("--out=C:\\Temp\\uia.log")]
    [InlineData("--events=focus")]
    [InlineData("-out=x")]
    public void TheJoinedForm_IsRefused(string argument)
    {
        RecorderOptions options = RecorderOptions.Parse(new[] { argument });

        Assert.NotNull(options.Error);
        Assert.Contains("two arguments", options.Error, StringComparison.Ordinal);
        Assert.Contains(argument, options.Error, StringComparison.Ordinal);
        Assert.Null(options.OutPath);
    }

    [Theory]
    [InlineData("focus", "focus")]
    [InlineData("focus,selection", "focus, selection")]
    [InlineData(" Focus , PROPERTIES ", "focus, properties")]
    [InlineData("async,notification,live,structure", "structure, live, notification, async")]
    [InlineData("all", AllFamilies)]
    [InlineData("selection,selection", "selection")]
    public void Events_SubscribesToTheNamedFamiliesOnly(string list, string expected)
    {
        RecorderOptions options = RecorderOptions.Parse(new[] { "--events", list });

        Assert.Null(options.Error);
        Assert.Equal(expected, RecorderOptions.Describe(options.Events));
    }

    [Fact]
    public void Events_MapsEachNameToItsFamily()
    {
        Assert.Equal(RecorderEvents.Focus, RecorderOptions.Parse(new[] { "--events", "focus" }).Events);
        Assert.Equal(RecorderEvents.Selection, RecorderOptions.Parse(new[] { "--events", "selection" }).Events);
        Assert.Equal(RecorderEvents.Structure, RecorderOptions.Parse(new[] { "--events", "structure" }).Events);
        Assert.Equal(RecorderEvents.Live, RecorderOptions.Parse(new[] { "--events", "live" }).Events);
        Assert.Equal(RecorderEvents.Notification, RecorderOptions.Parse(new[] { "--events", "notification" }).Events);
        Assert.Equal(RecorderEvents.Async, RecorderOptions.Parse(new[] { "--events", "async" }).Events);
        Assert.Equal(RecorderEvents.Properties, RecorderOptions.Parse(new[] { "--events", "properties" }).Events);
        Assert.Equal(RecorderEvents.All, RecorderOptions.EventFamilies.Aggregate(RecorderEvents.None, (all, family) => all | family.Value));
    }

    [Fact]
    public void Events_AndOut_Combine()
    {
        RecorderOptions options = RecorderOptions.Parse(new[] { "--events", "focus,selection", "--out", "trace.log" });

        Assert.Null(options.Error);
        Assert.Equal("trace.log", options.OutPath);
        Assert.Equal(RecorderEvents.Focus | RecorderEvents.Selection, options.Events);
        Assert.Equal("focus, selection", RecorderOptions.Describe(options.Events));
    }

    [Theory]
    [InlineData("keys")]
    [InlineData("focus,input")]
    [InlineData(",")]
    public void Events_WithAnUnknownOrEmptyList_IsAnError(string list)
    {
        RecorderOptions options = RecorderOptions.Parse(new[] { "--events", list });

        Assert.NotNull(options.Error);
        Assert.StartsWith("--events", options.Error, StringComparison.Ordinal);
        Assert.Contains(AllFamilies, options.Error, StringComparison.Ordinal);
        Assert.EndsWith(RecorderOptions.UsageHint, options.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Events_WithoutAList_IsAnError()
    {
        RecorderOptions options = RecorderOptions.Parse(new[] { "--events" });

        Assert.NotNull(options.Error);
        Assert.Contains("--events needs a list", options.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--out", "a.log", "--out", "b.log")]
    [InlineData("--events", "focus", "--events", "selection")]
    public void ARepeatedOption_IsAnError(params string[] args)
    {
        RecorderOptions options = RecorderOptions.Parse(args);

        Assert.NotNull(options.Error);
        Assert.Contains("more than once", options.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--trace-input")]
    [InlineData("trace.log")]
    [InlineData("/out")]
    [InlineData("-o")]
    public void AnUnknownArgument_IsAnError(string argument)
    {
        RecorderOptions options = RecorderOptions.Parse(new[] { argument });

        Assert.NotNull(options.Error);
        Assert.Equal("Unknown argument: " + argument + ". " + RecorderOptions.UsageHint, options.Error);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("/?")]
    [InlineData("--HELP")]
    [InlineData("-H")]
    public void Help_IsRecognisedAlone(string argument)
    {
        RecorderOptions options = RecorderOptions.Parse(new[] { argument });

        Assert.True(options.Help);
        Assert.Null(options.Error);
    }

    [Fact]
    public void Help_WinsOverOtherArguments_EvenWrongOnes()
    {
        Assert.True(RecorderOptions.Parse(new[] { "--out", "x.log", "--help" }).Help);
        Assert.True(RecorderOptions.Parse(new[] { "--bogus", "/?" }).Help);
    }

    [Fact]
    public void Usage_NamesEveryOptionAndFamily()
    {
        string usage = RecorderOptions.Usage;

        Assert.StartsWith("Usage: uefiaccess-uia-trace [--out <path>] [--events <list>]", usage, StringComparison.Ordinal);
        Assert.Contains("--out <path>", usage, StringComparison.Ordinal);
        Assert.Contains("--events <list>", usage, StringComparison.Ordinal);
        Assert.Contains("--help, -h, /?", usage, StringComparison.Ordinal);
        Assert.Contains(AllFamilies, usage, StringComparison.Ordinal);
        Assert.Contains("elevated", usage, StringComparison.Ordinal);
        Assert.Contains("uia-trace-<yyyyMMdd-HHmmss>.log", usage, StringComparison.Ordinal);
        Assert.Contains("never overwritten", usage, StringComparison.Ordinal);
        Assert.Contains("sends no input", usage, StringComparison.Ordinal);

        // What the files hold besides the events: the Filter box's text, and the recorder's own
        // lines in the day's UefiAccess log; and --out takes a full path.
        Assert.Contains("The Filter box's text is", usage, StringComparison.Ordinal);
        Assert.Contains("uefiaccess-<yyyyMMdd>.log", usage, StringComparison.Ordinal);
        Assert.Contains("full path such as C:\\Temp\\uia-trace.log", usage, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_LeavesTheArgumentsAsTheyWere()
    {
        string[] args = { " --out ", " a.log ", "--events", "focus" };
        string[] copy = (string[])args.Clone();

        RecorderOptions options = RecorderOptions.Parse(args);

        Assert.Null(options.Error);
        Assert.Equal("a.log", options.OutPath);
        Assert.Equal(copy, args);
    }

    [Fact]
    public void ExitCodes_AreDistinct()
    {
        int[] codes =
        {
            RecorderExitCodes.Success,
            RecorderExitCodes.Failed,
            RecorderExitCodes.Usage,
            RecorderExitCodes.NotElevated,
            RecorderExitCodes.NoOutput,
        };

        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, codes);
    }
}
