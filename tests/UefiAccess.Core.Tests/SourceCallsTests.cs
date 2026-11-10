// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The one source scanner every static guard uses: the call finder (declarations told apart from
/// calls, calls across lines and with named arguments, comments and literals of every form
/// skipped, method groups reported) and the text helpers (comments and literals blanked without
/// moving a line, method bodies told apart from calls and interface members, whole words found
/// on their lines).
/// </summary>
public sealed class SourceCallsTests
{
    private const string Source =
        "private string? RunCctk(string cctkPath, string argument, List<string> errors)\n"
        + "{\n"
        + "    a = RunCctk(cctkPath, VersionArgument, errors);\n"
        + "    b = RunCctk(cctkPath: p, argument: \"-i\", errors);\n"
        + "    c = this.RunCctk(\n"
        + "        cctkPath,\n"
        + "        Pick(first, second),\n"
        + "        errors);\n"
        + "    // RunCctk(p, Commented, errors)\n"
        + "    string url = \"http://example\"; d = RunCctk(p, AfterUrl, errors);\n"
        + "    e = RunCctk(errors: list, argument: Named, cctkPath: p);\n"
        + "}\n"
        + "private static string Other(string argument) => RunCctk(argument, argument, null);\n";

    [Fact]
    public void FindSeparatesTheDeclarationFromTheCallsAndSkipsComments()
    {
        IReadOnlyList<SourceCall> sites = SourceCalls.Find(Source, "RunCctk");

        Assert.Equal(new[] { 1, 3, 4, 5, 10, 11, 13 }, sites.Select(site => site.Line));
        Assert.Equal(new[] { true, false, false, false, false, false, false }, sites.Select(site => site.IsDeclaration));
        Assert.Equal(new[] { "string cctkPath", "string argument", "List<string> errors" }, sites[0].Arguments);
        Assert.Equal(new[] { "cctkPath", "Pick(first, second)", "errors" }, sites[3].Arguments);
    }

    [Fact]
    public void ArgumentForPrefersTheNamedArgumentThenThePosition()
    {
        string?[] passed = SourceCalls.Find(Source, "RunCctk")
            .Where(site => !site.IsDeclaration)
            .Select(site => SourceCalls.ArgumentFor(site.Arguments, "argument", 1))
            .ToArray();

        Assert.Equal(new[] { "VersionArgument", "\"-i\"", "Pick(first, second)", "AfterUrl", "Named", "argument" }, passed);
        Assert.Null(SourceCalls.ArgumentFor(new[] { "p", "errors: list" }, "argument", 1));
        Assert.Null(SourceCalls.ArgumentFor(new[] { "p" }, "argument", 1));
    }

    [Fact]
    public void FindReferencesWithoutCallReportsMethodGroups()
    {
        Assert.Empty(SourceCalls.FindReferencesWithoutCall(Source, "RunCctk"));
        Assert.Equal(
            new[] { 2 },
            SourceCalls.FindReferencesWithoutCall("int x = 0;\nFunc<string, string, List<string>, string?> run = RunCctk;\n/// <see cref=\"RunCctk\"/>\n", "RunCctk"));
    }

    [Fact]
    public void FindMatchesQualifiedNamesAndStringsWithParentheses()
    {
        SourceCall run = Assert.Single(SourceCalls.Find("x = ProcessRunner.Run(path, null, t, _logger, \"(a, b)\", Tail);", "ProcessRunner.Run"));
        Assert.Equal(new[] { "path", "null", "t", "_logger", "\"(a, b)\"", "Tail" }, run.Arguments);
        Assert.Empty(SourceCalls.Find("x = OtherProcessRunner.Run(path);", "ProcessRunner.Run"));
    }

    [Theory]
    [InlineData("", new string[0])]
    [InlineData("a", new[] { "a" })]
    [InlineData("a, f(b, c), new[] { d, e }, \"x, y\", 'z'", new[] { "a", "f(b, c)", "new[] { d, e }", "\"x, y\"", "'z'" })]
    [InlineData(" logger: _logger ,\n  \"-Command\" ", new[] { "logger: _logger", "\"-Command\"" })]
    public void SplitTopLevelArgumentsSplitsOnlyAtTopLevelCommas(string arguments, string[] expected)
    {
        Assert.Equal(expected, SourceCalls.SplitTopLevelArguments(arguments));
    }

    [Fact]
    public void FindAndItsArgumentsIgnoreCommentsAndLiterals_OfEveryForm()
    {
        const string source =
            "/* Run(block, comment) */ x = Run(a, @\"C:\a, b\\\"\"(\", \"\"\"raw, (\"\"\", ')');\n"
            + "string s = \"Run(inside, a string)\";\n"
            + "y = Run(\n"
            + "    b, // Run(in, a line comment)\n"
            + "    $\"{c}, d\");\n";

        IReadOnlyList<SourceCall> calls = SourceCalls.Find(source, "Run");

        Assert.Equal(new[] { 1, 3 }, calls.Select(call => call.Line));
        Assert.Equal(new[] { "a", "@\"C:\a, b\\\"\"(\"", "\"\"\"raw, (\"\"\"", "')'" }, calls[0].Arguments);
        Assert.Equal(new[] { "b", "$\"{c}, d\"" }, calls[1].Arguments);
        Assert.Empty(SourceCalls.FindReferencesWithoutCall("z = \"Run\"; // Run\n", "Run"));
    }
    private const string ScannerSource =
        "namespace Example;\n"
        + "\n"
        + "/// <summary>Writes once; never in a loop (for, foreach, while).</summary>\n"
        + "internal sealed class Writer\n"
        + "{\n"
        + "    private const string Label = \"retry for // not a comment\";\n"
        + "    private const string Path = @\"C:\\for\\\"\"while\"\"\";\n"
        + "    private const char Quote = '\"';\n"
        + "    private const string Nested = $\"{Label} do\";\n"
        + "\n"
        + "    /* a block comment\n"
        + "       with foreach inside */\n"
        + "    public int Commit(Plan plan) => Write(plan); // goto is not here\n"
        + "\n"
        + "    public int Prepare(Plan plan)\n"
        + "    {\n"
        + "        int n = Count(plan);\n"
        + "        if (Ready(n)) { return n; }\n"
        + "        return Write(plan);\n"
        + "    }\n"
        + "\n"
        + "    private int Write(Plan plan);\n"
        + "}\n";

    [Fact]
    public void WithoutComments_BlanksCommentsOnlyAndKeepsEveryLineBreak()
    {
        string text = SourceCalls.WithoutComments(ScannerSource);

        Assert.Equal(ScannerSource.Length, text.Length);
        Assert.Equal(ScannerSource.Count(c => c == '\n'), text.Count(c => c == '\n'));
        Assert.DoesNotContain("<summary>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("with foreach inside", text, StringComparison.Ordinal);
        Assert.DoesNotContain("goto is not here", text, StringComparison.Ordinal);
        Assert.Contains("\"retry for // not a comment\"", text, StringComparison.Ordinal);
        Assert.Contains("@\"C:\\for\\\"\"while\"\"\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutCommentsAndStrings_BlanksEveryLiteralFormToo()
    {
        string code = SourceCalls.WithoutCommentsAndStrings(ScannerSource);

        Assert.Equal(ScannerSource.Length, code.Length);
        Assert.Equal(ScannerSource.Count(c => c == '\n'), code.Count(c => c == '\n'));
        Assert.DoesNotContain("retry", code, StringComparison.Ordinal);
        Assert.DoesNotContain("while", code, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\", code, StringComparison.Ordinal);
        Assert.DoesNotContain(" do", code, StringComparison.Ordinal);
        Assert.Contains("private const char Quote = ", code, StringComparison.Ordinal);
        Assert.Contains("int n = Count(plan);", code, StringComparison.Ordinal);
        Assert.Empty(SourceCalls.LinesNaming(code, "for"));
        Assert.Empty(SourceCalls.LinesNaming(code, "foreach"));
    }

    [Fact]
    public void WithoutCommentsAndStrings_BlanksARawLiteralWhole()
    {
        const string raw = "string s = \"\"\"\n    for \"quoted\" while\n    \"\"\";\nint after = 1;";

        string code = SourceCalls.WithoutCommentsAndStrings(raw);

        Assert.DoesNotContain("for", code, StringComparison.Ordinal);
        Assert.DoesNotContain("while", code, StringComparison.Ordinal);
        Assert.Contains("int after = 1;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutCommentsAndStrings_ReadsAVerbatimLiteralThatOpensWithAnEscapedQuote()
    {
        // @"""for" is a verbatim literal holding "for, not a raw literal; @"" is an empty one.
        const string source = "string a = @\"\"\"for\";\nstring b = @\"\";\nint after = 1; // while";

        string code = SourceCalls.WithoutCommentsAndStrings(source);

        Assert.Equal(source.Length, code.Length);
        Assert.Empty(SourceCalls.LinesNaming(code, "for"));
        Assert.Empty(SourceCalls.LinesNaming(code, "while"));
        Assert.Contains("string b = ", code, StringComparison.Ordinal);
        Assert.Contains("int after = 1;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MethodBodies_ReturnsBlockAndExpressionBodiesAndSkipsCallsAndBodilessDeclarations()
    {
        Assert.Equal(new[] { " Write(plan)" }, SourceCalls.MethodBodies(ScannerSource, "Commit"));

        string prepare = Assert.Single(SourceCalls.MethodBodies(ScannerSource, "Prepare"));
        Assert.Contains("int n = Count(plan);", prepare, StringComparison.Ordinal);
        Assert.Contains("return Write(plan);", prepare, StringComparison.Ordinal);
        Assert.DoesNotContain("private int Write", prepare, StringComparison.Ordinal);

        // Write is called twice and declared without a body; Count and Ready are only called.
        Assert.Empty(SourceCalls.MethodBodies(ScannerSource, "Write"));
        Assert.Empty(SourceCalls.MethodBodies(ScannerSource, "Count"));
        Assert.Empty(SourceCalls.MethodBodies(ScannerSource, "Ready"));
    }

    [Fact]
    public void MethodBodies_EndsAnExpressionBodyAtTheStatementEndOutsideNesting()
    {
        const string source = "public Task<int> Run(int a) => Task.Run(() => { int b = a; return b; });\nint next;";

        string body = Assert.Single(SourceCalls.MethodBodies(source, "Run"));

        Assert.Equal(" Task.Run(() => { int b = a; return b; })", body);
    }

    [Fact]
    public void MethodBodies_SkipsTheObjectInitializerOfAConstructorCall()
    {
        const string source = "Plan made = new Plan(1) { Size = 2 };\nprivate Plan(int size) { Size = size; }";

        string body = Assert.Single(SourceCalls.MethodBodies(source, "Plan"));

        Assert.Equal(" Size = size; ", body);
    }

    [Fact]
    public void LinesNaming_FindsWholeWordsOnlyAndEachLineOnce()
    {
        const string text = "Kernel32.Write(a);\nWriteOnce(b); Write(c);\nRewrite(d);\n// Write(e)";

        Assert.Equal(new[] { 1, 2, 4 }, SourceCalls.LinesNaming(text, "Write"));
        Assert.Equal(new[] { 1, 2 }, SourceCalls.LinesNaming(SourceCalls.WithoutComments(text), "Write"));
        Assert.Equal(new[] { 1 }, SourceCalls.LinesNaming(text, "Kernel32.Write"));
        Assert.Empty(SourceCalls.LinesNaming(text, "Writ"));
        Assert.Equal(3, SourceCalls.LineOf(text, text.IndexOf("Rewrite", StringComparison.Ordinal)));
    }
}
