// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Diagnostics;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The trace files go to the developer, and no line of them may carry the unit's service tag. The
/// redactor replaces every registered value, in any case, longest first, in one pass. The tags here
/// are synthetic; no test reads the machine's own.
/// </summary>
public sealed class TraceRedactorTests
{
    private const string Tag = "TESTTAG7";

    [Fact]
    public void Apply_ReplacesTheValue_InAnyCase()
    {
        TraceRedactor redactor = new();
        redactor.Add(Tag);

        Assert.Equal("tag [service tag] and [service tag], end", redactor.Apply("tag testtag7 and TESTTAG7, end"));
        Assert.Equal("[service tag]", redactor.Apply("TestTag7"));
        Assert.Equal(1, redactor.Count);
    }

    [Fact]
    public void Apply_ReplacesTheLongestValueFirst()
    {
        TraceRedactor redactor = new();
        redactor.Add("TESTTAG");
        redactor.Add("TESTTAG77");

        Assert.Equal("x[service tag]y", redactor.Apply("xTESTTAG77y"));
        Assert.Equal("x[service tag]1y", redactor.Apply("xTESTTAG1y"));
        Assert.Equal(2, redactor.Count);
    }

    [Fact]
    public void Apply_IsOnePass_SoTheReplacementIsNeverReplacedAgain()
    {
        TraceRedactor redactor = new();
        redactor.Add("SERVICE");

        Assert.Equal("[service tag]", redactor.Apply("service"));

        // With two values a second, sequential pass would replace the "service" of the first
        // replacement ("[[service tag] tag] [service tag]"); one pass leaves it alone.
        TraceRedactor two = new();
        two.Add(Tag);
        two.Add("SERVICE");
        Assert.Equal("[service tag] [service tag]", two.Apply(Tag + " service"));
    }

    [Fact]
    public void Apply_ReplacesTheStartOfAValueThatACutEnds()
    {
        TraceRedactor redactor = new();
        redactor.Add(Tag);

        // A name quoted at five characters keeps "x" and the first three of the tag, then "…".
        string cut = TraceLine.Quote("x" + Tag, 5);
        Assert.Equal("\"xTES…\"", cut);
        string redacted = redactor.Apply(cut);
        Assert.Equal("\"x[service tag]…\"", redacted);
        for (int length = TraceRedactor.MinimumCutLength; length < Tag.Length; length++)
        {
            Assert.DoesNotContain(Tag[..length] + "…", redactor.Apply("name " + Tag[..length].ToLowerInvariant() + "…"), StringComparison.OrdinalIgnoreCase);
        }

        // Six of the seven characters, at the end of a long quoted name, in any case.
        Assert.Equal("\"" + new string('a', 73) + "[service tag]…\"", redactor.Apply(TraceLine.Quote(new string('a', 73) + Tag.ToLowerInvariant(), 80)));

        // Shorter starts, a start that is not before a cut, and a whole value before a cut.
        Assert.Equal("TE…", redactor.Apply("TE…"));
        Assert.Equal("TESTTA and more", redactor.Apply("TESTTA and more"));
        Assert.Equal("[service tag]…", redactor.Apply(Tag + "…"));
        Assert.Equal("a … b", redactor.Apply("a … b"));
        Assert.Equal("[service tag]… and [service tag]…", redactor.Apply("TES… and TESTT…"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData(" ab ")]
    public void Add_IgnoresNullBlankAndShortValues(string? value)
    {
        TraceRedactor redactor = new();
        redactor.Add(value);

        Assert.Equal(0, redactor.Count);
        Assert.Equal("abc ab text", redactor.Apply("abc ab text"));
    }

    [Fact]
    public void Add_TrimsTheValue_AndKeepsOneOfEachInAnyCase()
    {
        TraceRedactor redactor = new();
        redactor.Add("  " + Tag + " ");
        redactor.Add(Tag.ToLowerInvariant());
        redactor.Add(Tag);

        Assert.Equal(1, redactor.Count);
        Assert.Equal("a [service tag] b", redactor.Apply("a " + Tag + " b"));
    }

    [Fact]
    public void Apply_LeavesTextWithoutAValueAsItIs()
    {
        TraceRedactor redactor = new();
        string text = "KEY\tdown Down #12";
        Assert.Same(text, redactor.Apply(text));

        redactor.Add(Tag);
        Assert.Same(text, redactor.Apply(text));
        Assert.Equal(string.Empty, redactor.Apply(null));
        Assert.Equal(string.Empty, redactor.Apply(string.Empty));
    }

    [Fact]
    public void Add_FromSeveralThreads_KeepsEveryValue()
    {
        TraceRedactor redactor = new();
        string[] values = Enumerable.Range(0, 200).Select(i => "TAG" + i.ToString("0000", System.Globalization.CultureInfo.InvariantCulture)).ToArray();

        Parallel.ForEach(values, value =>
        {
            redactor.Add(value);
            _ = redactor.Apply("before " + value + " after");
        });

        Assert.Equal(values.Length, redactor.Count);
        string redacted = redactor.Apply(string.Join(" ", values));
        Assert.DoesNotContain("TAG0", redacted, StringComparison.Ordinal);
        Assert.Equal(values.Length, redacted.Split(TraceRedactor.Replacement).Length - 1);
    }

    [Fact]
    public void TheReplacementAndTheMinimum_AreAsDocumented()
    {
        Assert.Equal("[service tag]", TraceRedactor.Replacement);
        Assert.Equal(4, TraceRedactor.MinimumLength);
    }
}
