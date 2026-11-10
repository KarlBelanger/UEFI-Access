// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using System.Text.RegularExpressions;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The one C# source scanner of the test project, shared by every static guard: the source with
/// its comments, or its comments and its string and character literals (plain, verbatim,
/// interpolated, raw), blanked to spaces of the same length, so a keyword or name search sees
/// code only and every line number is unchanged; the calls of a named method with their
/// argument lists split at the top-level commas, however the call is laid out (across lines,
/// with nested calls, or with named arguments); the references to a method that are not calls;
/// the bodies of one named method; and the lines that name a whole word or match a pattern.
/// Every search runs over the blanked text, and every bracket and comma is judged there, so a
/// comment or a literal never counts as code. The blanking is a scanner, not a parser: a quote
/// nested inside an interpolation hole ends the string early and lets the rest of the hole
/// through as code, which is the safe direction for a search that must find a token.
/// </summary>
internal static class SourceCalls
{
    /// <summary>
    /// Every occurrence of <paramref name="name"/> followed by an argument list in code (not in a
    /// comment or a literal), in document order. An occurrence whose closing parenthesis is
    /// followed by "{" or "=>" is the method's declaration; every other occurrence is a call.
    /// </summary>
    /// <param name="source">C# source text.</param>
    /// <param name="name">The method name, optionally qualified, for example "RunCctk" or "ProcessRunner.Run".</param>
    /// <returns>The declarations and calls found.</returns>
    public static IReadOnlyList<SourceCall> Find(string source, string name)
    {
        string text = WithoutComments(source);
        string code = WithoutCommentsAndStrings(source);
        List<SourceCall> found = new();
        foreach (Match match in new Regex(@"(?<!\w)" + Regex.Escape(name) + @"\s*\(", RegexOptions.CultureInvariant).Matches(code))
        {
            int open = match.Index + match.Length - 1;
            int close = MatchingClose(code, open, '(', ')');
            if (close < 0)
            {
                continue;
            }

            ReadOnlySpan<char> following = code.AsSpan(close + 1).TrimStart();
            bool isDeclaration = following.StartsWith("{", StringComparison.Ordinal) || following.StartsWith("=>", StringComparison.Ordinal);
            found.Add(new SourceCall(LineOf(code, match.Index), isDeclaration, SplitTopLevelArguments(text[(open + 1)..close])));
        }

        return found;
    }

    /// <summary>
    /// Every occurrence of <paramref name="name"/> as a whole word in code (not in a comment or a
    /// literal) that is not followed by an argument list: a method group handed to a delegate,
    /// which a call check would not see.
    /// </summary>
    /// <param name="source">C# source text.</param>
    /// <param name="name">The method name.</param>
    /// <returns>The 1-based lines of those occurrences.</returns>
    public static IReadOnlyList<int> FindReferencesWithoutCall(string source, string name)
    {
        string code = WithoutCommentsAndStrings(source);
        return new Regex(@"(?<!\w)" + Regex.Escape(name) + @"(?!\w)(?!\s*\()", RegexOptions.CultureInvariant)
            .Matches(code)
            .Select(match => LineOf(code, match.Index))
            .ToArray();
    }

    /// <summary>
    /// The value passed for a parameter: the argument named "<paramref name="parameter"/>:" when
    /// there is one, otherwise the unnamed argument at <paramref name="position"/>.
    /// </summary>
    /// <param name="arguments">The call's top-level arguments.</param>
    /// <param name="parameter">The parameter's name.</param>
    /// <param name="position">The parameter's zero-based position.</param>
    /// <returns>The argument text, or null when the call passes nothing recognisable for it.</returns>
    public static string? ArgumentFor(IReadOnlyList<string> arguments, string parameter, int position)
    {
        Regex named = new(@"^" + Regex.Escape(parameter) + @"\s*:(?!:)\s*(?<value>.*)$", RegexOptions.CultureInvariant | RegexOptions.Singleline);
        foreach (string argument in arguments)
        {
            Match match = named.Match(argument);
            if (match.Success)
            {
                return match.Groups["value"].Value.Trim();
            }
        }

        if (position < arguments.Count && !Regex.IsMatch(arguments[position], @"^@?[A-Za-z_]\w*\s*:(?!:)", RegexOptions.CultureInvariant))
        {
            return arguments[position];
        }

        return null;
    }

    /// <summary>
    /// Splits an argument list at the commas outside parentheses, brackets, braces, comments and
    /// literals: the commas and brackets are judged on the blanked text, and the pieces are cut
    /// from the text as given.
    /// </summary>
    /// <param name="arguments">The text between the call's parentheses.</param>
    /// <returns>The trimmed arguments; empty for an empty list.</returns>
    public static IReadOnlyList<string> SplitTopLevelArguments(string arguments)
    {
        string code = WithoutCommentsAndStrings(arguments);
        List<string> parts = new();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < code.Length; i++)
        {
            switch (code[i])
            {
                case '(':
                case '[':
                case '{':
                    depth++;
                    break;
                case ')':
                case ']':
                case '}':
                    depth--;
                    break;
                case ',' when depth == 0:
                    parts.Add(arguments[start..i].Trim());
                    start = i + 1;
                    break;
            }
        }

        string last = arguments[start..].Trim();
        if (last.Length > 0 || parts.Count > 0)
        {
            parts.Add(last);
        }

        return parts;
    }

    /// <summary>The source with every line and block comment replaced by spaces; string and character literals are kept.</summary>
    /// <param name="source">C# source text.</param>
    /// <returns>Text of the same length with the same line breaks.</returns>
    public static string WithoutComments(string source) => Blank(source, blankLiterals: false);

    /// <summary>
    /// The source with every comment and every string or character literal (plain, verbatim,
    /// interpolated, raw) replaced by spaces, so a search sees code only.
    /// </summary>
    /// <param name="source">C# source text.</param>
    /// <returns>Text of the same length with the same line breaks.</returns>
    public static string WithoutCommentsAndStrings(string source) => Blank(source, blankLiterals: true);

    /// <summary>
    /// The bodies of every declaration of <paramref name="name"/> in the source, comments and
    /// literals blanked: the text between the braces, or after "=>" up to the ";" that ends an
    /// expression-bodied member. A call, an extern declaration or an interface member (followed
    /// by ";") has no body and is not returned, and neither is the object initializer after
    /// "new Name(...)".
    /// </summary>
    /// <param name="source">C# source text.</param>
    /// <param name="name">The method name, unqualified.</param>
    /// <returns>The bodies in document order; empty when the method is not declared.</returns>
    public static IReadOnlyList<string> MethodBodies(string source, string name)
    {
        string code = WithoutCommentsAndStrings(source);
        List<string> bodies = new();
        foreach (Match match in new Regex(@"(?<!\bnew\s+)(?<!\w)" + Regex.Escape(name) + @"\s*\(", RegexOptions.CultureInvariant).Matches(code))
        {
            int open = match.Index + match.Length - 1;
            int close = MatchingClose(code, open, '(', ')');
            if (close < 0)
            {
                continue;
            }

            int next = close + 1;
            while (next < code.Length && char.IsWhiteSpace(code[next]))
            {
                next++;
            }

            if (next < code.Length && code[next] == '{')
            {
                int end = MatchingClose(code, next, '{', '}');
                if (end > next)
                {
                    bodies.Add(code[(next + 1)..end]);
                }
            }
            else if (code.AsSpan(next).StartsWith("=>", StringComparison.Ordinal))
            {
                int end = StatementEnd(code, next + 2);
                if (end > next)
                {
                    bodies.Add(code[(next + 2)..end]);
                }
            }
        }

        return bodies;
    }

    /// <summary>The 1-based lines of <paramref name="text"/> on which <paramref name="word"/> occurs as a whole word (letters, digits and "_" on neither side).</summary>
    /// <param name="text">Source text, usually already blanked by <see cref="WithoutComments"/> or <see cref="WithoutCommentsAndStrings"/>.</param>
    /// <param name="word">The identifier to find.</param>
    /// <returns>The lines, each once, in order.</returns>
    public static IReadOnlyList<int> LinesNaming(string text, string word) =>
        LinesMatching(text, new Regex(@"(?<!\w)" + Regex.Escape(word) + @"(?!\w)", RegexOptions.CultureInvariant));

    /// <summary>The 1-based lines of <paramref name="text"/> on which <paramref name="pattern"/> matches.</summary>
    /// <param name="text">Source text.</param>
    /// <param name="pattern">The pattern.</param>
    /// <returns>The lines, each once, in order.</returns>
    public static IReadOnlyList<int> LinesMatching(string text, Regex pattern) =>
        pattern.Matches(text).Select(match => LineOf(text, match.Index)).Distinct().ToArray();

    /// <summary>The 1-based line of <paramref name="text"/> that holds <paramref name="index"/>.</summary>
    /// <param name="text">Source text.</param>
    /// <param name="index">A character index.</param>
    /// <returns>The line number.</returns>
    public static int LineOf(string text, int index) => 1 + text.AsSpan(0, index).Count('\n');

    private static string Blank(string source, bool blankLiterals)
    {
        StringBuilder result = new(source.Length);
        int i = 0;
        while (i < source.Length)
        {
            char c = source[i];
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                int end = source.IndexOf('\n', i);
                end = end < 0 ? source.Length : end;
                AppendBlanked(result, source, i, end);
                i = end;
            }
            else if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                int end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? source.Length : end + 2;
                AppendBlanked(result, source, i, end);
                i = end;
            }
            else if (c == '"')
            {
                int end = StringLiteralEnd(source, i);
                if (blankLiterals)
                {
                    AppendBlanked(result, source, i, end);
                }
                else
                {
                    result.Append(source, i, end - i);
                }

                i = end;
            }
            else if (c == '\'')
            {
                int end = CharacterLiteralEnd(source, i);
                if (blankLiterals)
                {
                    AppendBlanked(result, source, i, end);
                }
                else
                {
                    result.Append(source, i, end - i);
                }

                i = end;
            }
            else
            {
                result.Append(c);
                i++;
            }
        }

        return result.ToString();
    }

    /// <summary>Appends <paramref name="source"/>[<paramref name="start"/>..<paramref name="end"/>) with every character but a line break replaced by a space.</summary>
    private static void AppendBlanked(StringBuilder result, string source, int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            result.Append(source[i] is '\n' or '\r' ? source[i] : ' ');
        }
    }

    /// <summary>
    /// The index just past the string literal that opens at <paramref name="start"/> (a quote),
    /// verbatim and raw forms included. A verbatim literal is told apart first, so one that opens
    /// with an escaped quote (@"""a") is not read as a raw literal.
    /// </summary>
    private static int StringLiteralEnd(string source, int start)
    {
        bool verbatim = IsVerbatim(source, start);
        int quotes = 0;
        while (start + quotes < source.Length && source[start + quotes] == '"')
        {
            quotes++;
        }

        if (!verbatim && quotes >= 3)
        {
            // A raw literal ends at the first run of at least as many quotes.
            string closing = new('"', quotes);
            int rawEnd = source.IndexOf(closing, start + quotes, StringComparison.Ordinal);
            return rawEnd < 0 ? source.Length : rawEnd + quotes;
        }

        if (!verbatim && quotes == 2)
        {
            return start + 2;
        }

        int i = start + 1;
        while (i < source.Length)
        {
            char c = source[i];
            if (verbatim)
            {
                if (c == '"')
                {
                    if (i + 1 < source.Length && source[i + 1] == '"')
                    {
                        i += 2;
                        continue;
                    }

                    return i + 1;
                }
            }
            else
            {
                if (c == '\\')
                {
                    i += 2;
                    continue;
                }

                if (c == '"' || c == '\n')
                {
                    return c == '"' ? i + 1 : i;
                }
            }

            i++;
        }

        return source.Length;
    }

    /// <summary>True when the quote at <paramref name="quote"/> is preceded by "@" (with or without "$", in either order).</summary>
    private static bool IsVerbatim(string source, int quote)
    {
        int i = quote - 1;
        bool verbatim = false;
        while (i >= 0 && (source[i] == '@' || source[i] == '$'))
        {
            verbatim |= source[i] == '@';
            i--;
        }

        return verbatim;
    }

    /// <summary>The index just past the character literal that opens at <paramref name="start"/>.</summary>
    private static int CharacterLiteralEnd(string source, int start)
    {
        int i = start + 1;
        while (i < source.Length)
        {
            char c = source[i];
            if (c == '\\')
            {
                i += 2;
                continue;
            }

            if (c == '\'' || c == '\n')
            {
                return c == '\'' ? i + 1 : i;
            }

            i++;
        }

        return source.Length;
    }

    /// <summary>The index of the <paramref name="close"/> that balances the <paramref name="open"/> at <paramref name="index"/> in blanked text; -1 when unbalanced.</summary>
    private static int MatchingClose(string code, int index, char open, char close)
    {
        int depth = 0;
        for (int i = index; i < code.Length; i++)
        {
            if (code[i] == open)
            {
                depth++;
            }
            else if (code[i] == close)
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    /// <summary>The index of the ";" that ends the statement starting at <paramref name="index"/> in blanked text, outside parentheses, brackets and braces; the text's length when there is none.</summary>
    private static int StatementEnd(string code, int index)
    {
        int depth = 0;
        for (int i = index; i < code.Length; i++)
        {
            switch (code[i])
            {
                case '(':
                case '[':
                case '{':
                    depth++;
                    break;
                case ')':
                case ']':
                case '}':
                    depth--;
                    break;
                case ';' when depth == 0:
                    return i;
            }
        }

        return code.Length;
    }
}

/// <summary>One declaration or call found by <see cref="SourceCalls.Find"/>.</summary>
/// <param name="Line">1-based line of the method name.</param>
/// <param name="IsDeclaration">True for the method's own declaration.</param>
/// <param name="Arguments">The trimmed top-level arguments (parameters, for a declaration).</param>
internal sealed record SourceCall(int Line, bool IsDeclaration, IReadOnlyList<string> Arguments);
