// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// Findings F075, R10, R20 and R22: the reports say every WMI connection, enumeration and method
/// call is bounded at 60 seconds. That holds only while every WMI round trip in src/ goes through
/// <c>WmiHelper</c>. BitLockerInspector once created its own scope and called its three Get*
/// methods directly, and later still fetched each method's parameter definition
/// (GetMethodParameters, a WMI round trip on first use) and enumerated its volumes outside any
/// bound, so a stuck BitLocker provider could hang the gate. Every C# file under src/ is scanned
/// with its comments and the contents of its string literals blanked out, and three rules are
/// enforced:
/// <list type="bullet">
/// <item>A ManagementScope is created, and a scope's Connect() called, only inside the argument
/// list of WmiHelper.ConnectBounded in WmiHelper.cs, which runs the connect under the bound.</item>
/// <item>A ManagementObjectSearcher, ManagementClass, ManagementObject or ManagementEventWatcher
/// is constructed only on a ManagementScope variable or straight on WmiHelper.ConnectBounded(...).
/// Built on a namespace or path string, on a query alone or on nothing, such an object connects on
/// its own when first used, without a bound.</item>
/// <item>Every WMI round trip (InvokeMethod, GetMethodParameters, Get(), GetInstances,
/// GetSubclasses, GetRelated, GetRelationships, GetRelatedClasses, GetRelationshipClasses,
/// WaitForNextEvent) lies inside the argument list of a WmiHelper.RunBounded call, that is in the
/// delegate the call bounds. A RunBounded elsewhere in the statement or on the line before does
/// not count.</item>
/// </list>
/// The rules with generic method names (Get(), Connect() and the Get* enumerators) apply to files
/// that name System.Management, the only files that can use those types while src/ has no global
/// using of the namespace (asserted below). The scanner is exercised against the code it replaced
/// and against synthetic lines as well.
/// </summary>
public sealed class WmiCallsAreBoundedTests
{
    private const string WmiHelperFile = "src/UefiAccess.Core/Platform/WmiHelper.cs";
    private const string BitLockerFile = "src/UefiAccess.Core/Platform/BitLockerInspector.cs";

    /// <summary>The types whose objects connect on their own when they are not built on a connected scope.</summary>
    private const string ImplicitConnectTypes = "ManagementObjectSearcher|ManagementClass|ManagementObject|ManagementEventWatcher";

    /// <summary>An optional "System.Management." qualifier, with or without "global::".</summary>
    private const string Qualifier = @"(?:(?:global\s*::\s*)?System\s*\.\s*Management\s*\.\s*)?";

    /// <summary>Optional explicit type arguments, nested to any depth: RunBounded&lt;IReadOnlyList&lt;string&gt;&gt;(.</summary>
    private const string TypeArguments = @"(?:\s*<(?>[^<>();]+|<(?<depth>)|>(?<-depth>))*(?(depth)(?!))>)?";

    private static readonly Regex NamesSystemManagement = new(@"\bSystem\s*\.\s*Management\b", RegexOptions.CultureInvariant);

    private static readonly Regex ScopeConstruction = new(
        @"\bnew\s+" + Qualifier + @"ManagementScope\s*\(|(?<![\w.])" + Qualifier + @"ManagementScope\??\s+@?[A-Za-z_]\w*\s*=\s*new\s*\(",
        RegexOptions.CultureInvariant);

    private static readonly Regex ScopeVariable = new(
        @"(?<![\w.])" + Qualifier + @"ManagementScope\??\s+@?(?<name>[A-Za-z_]\w*)\b",
        RegexOptions.CultureInvariant);

    private static readonly Regex ImplicitConnectConstruction = new(
        @"\bnew\s+" + Qualifier + @"(?<type>" + ImplicitConnectTypes + @")\s*\("
        + @"|(?<![\w.])" + Qualifier + @"(?<type>" + ImplicitConnectTypes + @")\??\s+@?[A-Za-z_]\w*\s*=\s*new\s*\(",
        RegexOptions.CultureInvariant);

    private static readonly Regex AcceptedScopeArgument = new(@"^(?:WmiHelper\s*\.\s*)?ConnectBounded\s*\(", RegexOptions.CultureInvariant);

    /// <summary>The method calls that are a WMI round trip whatever file they are in.</summary>
    private static readonly Regex DistinctRoundTrip = new(@"\.\s*(?<call>InvokeMethod|GetMethodParameters)\s*\(", RegexOptions.CultureInvariant);

    /// <summary>The method calls that are a WMI round trip in a file that uses System.Management.</summary>
    private static readonly Regex ManagementRoundTrip = new(
        @"\.\s*(?:(?<call>GetInstances|GetSubclasses|GetRelated|GetRelationships|GetRelatedClasses|GetRelationshipClasses|WaitForNextEvent)\s*\(|(?<call>Get)\s*\(\s*\))",
        RegexOptions.CultureInvariant);

    private static readonly Regex ConnectCall = new(@"\.\s*Connect\s*\(\s*\)", RegexOptions.CultureInvariant);

    private static readonly Regex QualifiedRunBounded = new(@"(?<![\w.])WmiHelper\s*\.\s*RunBounded" + TypeArguments + @"\s*\(", RegexOptions.CultureInvariant);

    private static readonly Regex AnyRunBounded = new(@"(?<![\w])RunBounded" + TypeArguments + @"\s*\(", RegexOptions.CultureInvariant);

    private static readonly Regex ConnectBoundedCall = new(@"(?<![\w])ConnectBounded" + TypeArguments + @"\s*\(", RegexOptions.CultureInvariant);

    [Fact]
    public void EveryWmiRoundTripInSrcIsBounded()
    {
        List<string> violations = new();
        foreach (string path in RepositoryPaths.Enumerate("src", "*.cs"))
        {
            string relative = RepositoryPaths.Relative(path);
            foreach (string violation in FindUnboundedWmiCalls(File.ReadAllText(path), relative == WmiHelperFile))
            {
                violations.Add(relative + ":" + violation);
            }
        }

        Assert.True(violations.Count == 0, "Run every WMI round trip through WmiHelper (ConnectBounded, RunBounded):" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void SrcHasNoGlobalUsingOfSystemManagement()
    {
        // The rules with generic method names rely on this: without a global using, only a file
        // that names System.Management can use its types.
        List<string> hits = new();
        foreach (string path in RepositoryPaths.Enumerate("src", "*.cs"))
        {
            if (Regex.IsMatch(MaskCommentsAndStrings(File.ReadAllText(path)), @"\bglobal\s+using\s+(?:static\s+)?(?:\w+\s*=\s*)?(?:global\s*::\s*)?System\s*\.\s*Management\b"))
            {
                hits.Add(RepositoryPaths.Relative(path));
            }
        }

        Assert.Empty(hits);
    }

    [Fact]
    public void TheScan_SeesTheWmiCallsItChecks()
    {
        // A scan that found none of these would pass vacuously.
        string bitLocker = MaskCommentsAndStrings(File.ReadAllText(Path.Combine(RepositoryPaths.Root, BitLockerFile)));
        Assert.Matches(NamesSystemManagement, bitLocker);
        Assert.Equal(3, Regex.Matches(bitLocker, @"\.\s*GetMethodParameters\s*\(").Count);
        Assert.Equal(3, Regex.Matches(bitLocker, @"\.\s*InvokeMethod\s*\(").Count);
        Assert.Single(ImplicitConnectConstruction.Matches(bitLocker));
        Assert.Single(ManagementRoundTrip.Matches(bitLocker), match => match.Groups["call"].Value == "Get");

        string helper = MaskCommentsAndStrings(File.ReadAllText(Path.Combine(RepositoryPaths.Root, WmiHelperFile)));
        Assert.Single(ScopeConstruction.Matches(helper));
        Assert.Single(ConnectCall.Matches(helper));
        Assert.Equal(2, ImplicitConnectConstruction.Matches(helper).Count);
        Assert.Equal(2, ManagementRoundTrip.Matches(helper).Count(match => match.Groups["call"].Value == "Get"));
    }

    // ----- the scanner against the code it replaced -----

    [Fact]
    public void Scanner_FlagsTheOldBitLockerEnumeration_OutsideAnyBound()
    {
        // BitLockerInspector.Inspect before R10: bounded connect, then the enumeration and the
        // per-volume method calls straight in the loop.
        const string Old =
            "using System.Management;\n"
            + "ManagementScope scope = WmiHelper.ConnectBounded(Namespace);\n"
            + "System.Management.EnumerationOptions options = WmiHelper.CreateEnumerationOptions();\n"
            + "using ManagementObjectSearcher searcher = new(scope, new ObjectQuery(Query), options);\n"
            + "using ManagementObjectCollection collection = searcher.Get();\n"
            + "foreach (ManagementBaseObject item in collection)\n"
            + "{\n"
            + "    volumes.Add(InspectVolume((ManagementObject)item, errors));\n"
            + "}\n";

        Assert.Equal(new[] { 5 }, ViolationLines(FindUnboundedWmiCalls(Old, isWmiHelper: false)));
    }

    [Fact]
    public void Scanner_FlagsTheOldGetMethodParameters_BeforeTheBoundedInvokeMethod()
    {
        // GetKeyProtectorIds before R10: the InvokeMethod was bounded, the fetch of its parameter
        // definition on the line before was not.
        const string Old =
            "using System.Management;\n"
            + "using ManagementBaseObject inParameters = volume.GetMethodParameters(GetKeyProtectorsMethod);\n"
            + "inParameters[\"KeyProtectorType\"] = 0u;\n"
            + "using ManagementBaseObject outParameters = WmiHelper.RunBounded(\n"
            + "    () => volume.InvokeMethod(GetKeyProtectorsMethod, inParameters, new InvokeMethodOptions()),\n"
            + "    WmiHelper.QueryTimeout,\n"
            + "    GetKeyProtectorsMethod + \" on \" + driveLetter);\n";

        IReadOnlyList<string> violations = FindUnboundedWmiCalls(Old, isWmiHelper: false);

        string violation = Assert.Single(violations);
        Assert.StartsWith("2: GetMethodParameters is a WMI round trip outside WmiHelper.RunBounded", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void Scanner_NoLongerTrustsARunBoundedOnThePreviousLine()
    {
        // The old scan accepted an InvokeMethod line whenever the line above contained
        // "WmiHelper.RunBounded(".
        const string Code =
            "using System.Management;\n"
            + "int warmUp = WmiHelper.RunBounded(() => 0, WmiHelper.QueryTimeout, \"warm-up\");\n"
            + "using ManagementBaseObject outParameters = volume.InvokeMethod(GetKeyProtectorsMethod, inParameters, new InvokeMethodOptions());\n"
            + "int n = WmiHelper.RunBounded(() => 0, WmiHelper.QueryTimeout, \"x\"); ManagementBaseObject o = volume.InvokeMethod(GetKeyProtectorTypeMethod, p, null);\n";

        Assert.Equal(new[] { 3, 4 }, ViolationLines(FindUnboundedWmiCalls(Code, isWmiHelper: false)));
    }

    // ----- the scanner against synthetic lines -----

    [Theory]
    [InlineData("using ManagementObjectSearcher searcher = new(\"root\\\\cimv2\", wql);")]
    [InlineData("ManagementObjectSearcher searcher = new ManagementObjectSearcher(\"SELECT * FROM Win32_BIOS\");")]
    [InlineData("var searcher = new ManagementObjectSearcher(new ObjectQuery(wql));")]
    [InlineData("var searcher = new ManagementObjectSearcher();")]
    [InlineData("ManagementClass tpm = new(@\"root\\cimv2\\Security\\MicrosoftTpm:Win32_Tpm\");")]
    [InlineData("ManagementObject volume = new ManagementObject(\"Win32_EncryptableVolume.DeviceID='x'\");")]
    [InlineData("var watcher = new System.Management.ManagementEventWatcher(\"SELECT * FROM __InstanceCreationEvent\");")]
    [InlineData("System.Management.ManagementClass bios = new(path);")]
    [InlineData("ManagementScope scope = new(@\"\\\\.\\root\\cimv2\");")]
    [InlineData("var scope = new ManagementScope(path);")]
    [InlineData("scope.Connect();")]
    [InlineData("using ManagementObjectCollection rows = searcher.Get();")]
    [InlineData("tpmClass.Get();")]
    [InlineData("foreach (ManagementObject tpm in tpmClass.GetInstances()) { }")]
    [InlineData("var related = volume.GetRelated(\"Win32_DiskPartition\");")]
    [InlineData("ManagementBaseObject e = watcher.WaitForNextEvent();")]
    [InlineData("using ManagementBaseObject p = volume.GetMethodParameters(GetKeyProtectorTypeMethod);")]
    [InlineData("var o = volume.InvokeMethod(GetKeyProtectorsMethod, p, null);")]
    [InlineData("int n = WmiHelper.RunBounded<int>(() => 0, t, \"x\") + volume.InvokeMethod(GetKeyProtectorsMethod, p, null).GetHashCode();")]
    [InlineData("int n = RunBounded(() => volume.InvokeMethod(GetKeyProtectorsMethod, p, null), t, \"x\");")]
    public void Scanner_FlagsAnUnboundedRoundTripOrImplicitConnect(string line)
    {
        Assert.NotEmpty(FindUnboundedWmiCalls("using System.Management;\n" + line, isWmiHelper: false));
    }

    [Theory]
    [InlineData("// volume.InvokeMethod(GetKeyProtectorsMethod, null, null); searcher.Get();")]
    [InlineData("/// <see cref=\"ManagementObject.GetMethodParameters(string)\"/> and new ManagementScope(path)")]
    [InlineData("/* new ManagementObjectSearcher(\"root\\\\cimv2\", wql).Get(); */")]
    [InlineData("string text = \"volume.GetMethodParameters(x); new ManagementClass(path); scope.Connect();\";")]
    [InlineData("string text = @\"new ManagementObject(\"\"x\"\").Get()\";")]
    [InlineData("object? value = WmiHelper.Get(row, \"AttributeName\");")]
    [InlineData("ManagementScope scope = WmiHelper.ConnectBounded(Namespace);")]
    [InlineData("ManagementScope scope = WmiHelper.ConnectBounded(Namespace); var rows = WmiHelper.RunBounded(() => { using ManagementObjectSearcher searcher = new(scope, new ObjectQuery(Query), options); using ManagementObjectCollection c = searcher.Get(); return c.Count; }, t, \"query\");")]
    [InlineData("var rows = WmiHelper.RunBounded(() => { using var s = new ManagementObjectSearcher(WmiHelper.ConnectBounded(ns), new ObjectQuery(q)); return s.Get().Count; }, t, \"query\");")]
    [InlineData("var r = WmiHelper.RunBounded<VolumeMethodResult<IReadOnlyList<string>>>(() => { using ManagementBaseObject p = _volume.GetMethodParameters(GetKeyProtectorsMethod); using ManagementBaseObject o = _volume.InvokeMethod(GetKeyProtectorsMethod, p, new InvokeMethodOptions()); return Read(o); }, t, \"label\");")]
    [InlineData("ManagementScope scope = connect(namespacePath); ManagementClass c = new(scope, new ManagementPath(\"Win32_Tpm\"), null); WmiHelper.RunBounded(() => c.Get(), t, \"class\");")]
    public void Scanner_AcceptsBoundedCallsCommentsAndStrings(string line)
    {
        Assert.Empty(FindUnboundedWmiCalls("using System.Management;\n" + line, isWmiHelper: false));
    }

    [Fact]
    public void Scanner_InWmiHelper_AcceptsTheConnectInsideConnectBoundedAndAnUnqualifiedRunBounded()
    {
        const string Helper =
            "using System.Management;\n"
            + "return ConnectBounded(namespacePath, ConnectTimeout, path => { ManagementScope scope = new(path); scope.Connect(); return scope; });\n"
            + "ManagementScope scope = connect(namespacePath);\n"
            + "var rows = RunBounded(() => { using ManagementObjectSearcher searcher = new(scope, new ObjectQuery(wql), CreateEnumerationOptions()); using ManagementObjectCollection c = searcher.Get(); return c.Count; }, QueryTimeout, \"query\");\n";

        Assert.Empty(FindUnboundedWmiCalls(Helper, isWmiHelper: true));

        // The same lines anywhere else: a scope created and connected outside WmiHelper, and a
        // RunBounded that is not WmiHelper's.
        Assert.Equal(new[] { 2, 2, 4 }, ViolationLines(FindUnboundedWmiCalls(Helper, isWmiHelper: false)));
    }

    [Fact]
    public void Scanner_GenericNamesCountOnlyInFilesThatUseSystemManagement()
    {
        // Get() and Connect() are common names; a file without System.Management cannot mean WMI.
        Assert.Empty(FindUnboundedWmiCalls("var value = lazy.Get();\nclient.Connect();\n", isWmiHelper: false));
        Assert.Equal(new[] { 2, 3 }, ViolationLines(FindUnboundedWmiCalls("using System.Management;\nvar value = lazy.Get();\nclient.Connect();\n", isWmiHelper: false)));
    }

    /// <summary>
    /// What is wrong with the way a C# source file reaches WMI, one line per violation, each
    /// starting with the 1-based line number and ": ". See the class summary for the rules.
    /// </summary>
    /// <param name="source">The C# source text.</param>
    /// <param name="isWmiHelper">True for WmiHelper.cs, which may create and connect a scope inside ConnectBounded and call RunBounded unqualified.</param>
    /// <returns>The violations, in the order the rules are checked.</returns>
    internal static IReadOnlyList<string> FindUnboundedWmiCalls(string source, bool isWmiHelper)
    {
        string code = MaskCommentsAndStrings(source);
        bool usesManagement = NamesSystemManagement.IsMatch(code);
        List<(int Open, int Close)> bounded = ArgumentSpans(code, isWmiHelper ? AnyRunBounded : QualifiedRunBounded);
        List<(int Open, int Close)> connecting = isWmiHelper ? ArgumentSpans(code, ConnectBoundedCall) : new();
        HashSet<string> scopes = new(ScopeVariable.Matches(code).Select(match => match.Groups["name"].Value), StringComparer.Ordinal);
        List<string> violations = new();

        foreach (Match match in ScopeConstruction.Matches(code))
        {
            if (!IsInside(connecting, match.Index))
            {
                violations.Add(LineOf(code, match.Index) + ": creates a ManagementScope outside WmiHelper.ConnectBounded; connect through WmiHelper.ConnectBounded");
            }
        }

        if (usesManagement)
        {
            foreach (Match match in ConnectCall.Matches(code))
            {
                if (!IsInside(connecting, match.Index))
                {
                    violations.Add(LineOf(code, match.Index) + ": Connect() has no timeout of its own; connect through WmiHelper.ConnectBounded");
                }
            }
        }

        foreach (Match match in ImplicitConnectConstruction.Matches(code))
        {
            int open = match.Index + match.Length - 1;
            int close = MatchingParenthesis(code, open);
            IReadOnlyList<string> arguments = SourceCalls.SplitTopLevelArguments(close < 0 ? code[(open + 1)..] : code[(open + 1)..close]);
            string first = arguments.Count == 0 ? string.Empty : arguments[0];
            if (!scopes.Contains(first) && !AcceptedScopeArgument.IsMatch(first))
            {
                string type = match.Groups["type"].Value;
                violations.Add(LineOf(code, match.Index) + $": a {type} built on '{first}' rather than on a scope from WmiHelper.ConnectBounded connects on its own, without a bound");
            }
        }

        IEnumerable<Match> roundTrips = DistinctRoundTrip.Matches(code);
        if (usesManagement)
        {
            roundTrips = roundTrips.Concat(ManagementRoundTrip.Matches(code));
        }

        foreach (Match match in roundTrips.OrderBy(match => match.Index))
        {
            if (!IsInside(bounded, match.Index))
            {
                violations.Add(LineOf(code, match.Index) + $": {match.Groups["call"].Value} is a WMI round trip outside WmiHelper.RunBounded; call it inside the delegate RunBounded bounds");
            }
        }

        return violations;
    }

    /// <summary>
    /// A copy of <paramref name="source"/> in which comments and the contents of string and
    /// character literals are blanked out (line breaks kept, so positions and line numbers stay
    /// the same). The quotes themselves stay, so a literal still reads as an argument.
    /// Interpolation holes are blanked with their string; no WMI call belongs in one.
    /// </summary>
    /// <param name="source">C# source text.</param>
    /// <returns>The masked text, as long as the source.</returns>
    internal static string MaskCommentsAndStrings(string source)
    {
        StringBuilder code = new(source);
        int i = 0;
        while (i < source.Length)
        {
            char c = source[i];
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                int end = source.IndexOf('\n', i);
                end = end < 0 ? source.Length : end;
                Blank(code, i, end);
                i = end;
            }
            else if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                int end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? source.Length : end + 2;
                Blank(code, i, end);
                i = end;
            }
            else if (c == '"' && !IsVerbatimPrefix(source, i) && i + 2 < source.Length && source[i + 1] == '"' && source[i + 2] == '"')
            {
                // A raw string literal: as many quotes close it as opened it.
                int quotes = 0;
                while (i + quotes < source.Length && source[i + quotes] == '"')
                {
                    quotes++;
                }

                string fence = new('"', quotes);
                int end = source.IndexOf(fence, i + quotes, StringComparison.Ordinal);
                end = end < 0 ? source.Length : end;
                Blank(code, i + quotes, end);
                i = Math.Min(source.Length, end + quotes);
            }
            else if (c == '"')
            {
                int end = EndOfString(source, i);
                Blank(code, i + 1, end);
                i = end + 1;
            }
            else if (c == '\'')
            {
                int end = EndOfCharacter(source, i);
                Blank(code, i + 1, end);
                i = end + 1;
            }
            else
            {
                i++;
            }
        }

        return code.ToString();
    }

    /// <summary>The index of the quote that closes the string literal opening at <paramref name="quote"/> (verbatim and interpolated forms included, and strings nested in interpolation holes skipped); the end of the text when it is unterminated.</summary>
    private static int EndOfString(string source, int quote)
    {
        bool verbatim = IsVerbatimPrefix(source, quote);
        bool interpolated = IsInterpolatedPrefix(source, quote);
        int j = quote + 1;
        while (j < source.Length)
        {
            char c = source[j];
            if (interpolated && c == '{')
            {
                if (j + 1 < source.Length && source[j + 1] == '{')
                {
                    j += 2;
                    continue;
                }

                j = EndOfHole(source, j + 1);
                continue;
            }

            if (verbatim)
            {
                if (c == '"')
                {
                    if (j + 1 < source.Length && source[j + 1] == '"')
                    {
                        j += 2;
                        continue;
                    }

                    return j;
                }

                j++;
                continue;
            }

            if (c == '\\')
            {
                j += 2;
                continue;
            }

            if (c == '"' || c == '\n')
            {
                return j;
            }

            j++;
        }

        return source.Length;
    }

    /// <summary>The index just after the "}" that closes the interpolation hole whose code starts at <paramref name="start"/>.</summary>
    private static int EndOfHole(string source, int start)
    {
        int depth = 1;
        int j = start;
        while (j < source.Length)
        {
            char c = source[j];
            if (c == '"')
            {
                j = EndOfString(source, j) + 1;
            }
            else if (c == '\'')
            {
                j = EndOfCharacter(source, j) + 1;
            }
            else if (c == '{')
            {
                depth++;
                j++;
            }
            else if (c == '}' && --depth == 0)
            {
                return j + 1;
            }
            else
            {
                j++;
            }
        }

        return source.Length;
    }

    /// <summary>The index of the quote that closes the character literal opening at <paramref name="quote"/>.</summary>
    private static int EndOfCharacter(string source, int quote)
    {
        int j = quote + 1;
        while (j < source.Length && source[j] != '\'' && source[j] != '\n')
        {
            j += source[j] == '\\' ? 2 : 1;
        }

        return Math.Min(j, source.Length);
    }

    /// <summary>The line numbers of the violations, in ascending order.</summary>
    private static IReadOnlyList<int> ViolationLines(IReadOnlyList<string> violations) =>
        violations.Select(violation => int.Parse(violation[..violation.IndexOf(':', StringComparison.Ordinal)], System.Globalization.CultureInfo.InvariantCulture)).Order().ToArray();

    /// <summary>The text between the parentheses of every call <paramref name="call"/> matches (the pattern ends with the opening parenthesis).</summary>
    private static List<(int Open, int Close)> ArgumentSpans(string code, Regex call)
    {
        List<(int Open, int Close)> spans = new();
        foreach (Match match in call.Matches(code))
        {
            int open = match.Index + match.Length - 1;
            int close = MatchingParenthesis(code, open);
            spans.Add((open, close < 0 ? code.Length : close));
        }

        return spans;
    }

    private static bool IsInside(List<(int Open, int Close)> spans, int index) =>
        spans.Any(span => index > span.Open && index < span.Close);

    private static int LineOf(string code, int index) => 1 + code.AsSpan(0, index).Count('\n');

    /// <summary>The index of the ")" that closes the "(" at <paramref name="open"/> in masked code; -1 when unbalanced.</summary>
    private static int MatchingParenthesis(string code, int open)
    {
        int depth = 0;
        for (int i = open; i < code.Length; i++)
        {
            if (code[i] == '(')
            {
                depth++;
            }
            else if (code[i] == ')' && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>True when the quote at <paramref name="quote"/> opens a verbatim string: @"...", $@"..." or @$"...".</summary>
    private static bool IsVerbatimPrefix(string source, int quote) => HasPrefix(source, quote, '@');

    /// <summary>True when the quote at <paramref name="quote"/> opens an interpolated string: $"...", $@"..." or @$"...".</summary>
    private static bool IsInterpolatedPrefix(string source, int quote) => HasPrefix(source, quote, '$');

    private static bool HasPrefix(string source, int quote, char marker)
    {
        for (int i = quote - 1; i >= 0 && quote - i <= 2; i--)
        {
            if (source[i] == marker)
            {
                return true;
            }

            if (source[i] != '$' && source[i] != '@')
            {
                return false;
            }
        }

        return false;
    }

    private static void Blank(StringBuilder code, int start, int end)
    {
        for (int i = start; i < end && i < code.Length; i++)
        {
            if (code[i] != '\n' && code[i] != '\r')
            {
                code[i] = ' ';
            }
        }
    }
}
