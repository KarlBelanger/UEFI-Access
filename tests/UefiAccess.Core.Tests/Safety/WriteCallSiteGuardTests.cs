// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;
using System.Text.RegularExpressions;
using UefiAccess.Core.Safety;
using Xunit;

namespace UefiAccess.Core.Tests.Safety;

/// <summary>
/// The tripwires (items 2 to 5) around the single firmware
/// write: the write import is declared once, under Native/, and called once, from the writer
/// file, never handed on as a method group; the last-NTSTATUS read is declared in NtDll and used
/// by the writer alone, and ntdll gains no setter; the writer's CommitOnce has exactly one
/// caller, WriteTransaction, and WritePlan no public way in, so nothing else can reach the
/// firmware; nothing loops or retries around the call, and the verification read after it occurs
/// once; the test project never constructs the real writer or the real suspender (one named test
/// holds a real writer made without its constructor, with no seam and never called, to pin the
/// pipeline's refusal of it over a store that is not the firmware's; that one site is allowed and
/// counted) and never names the real native write seam, which only the two composition roots
/// compose. Every check reads
/// the sources as text (comments and string literals blanked, and calls found, by the one
/// scanner, <see cref="SourceCalls"/>) or the built types by reflection: a tripwire, not a proof.
/// A name assembled at run time, reflection or dynamic in the product is beyond it, which is why
/// review of the allow-listed files (<see cref="NoWriteGuardTests.WriteSiteFiles"/>) stays the
/// control.
/// </summary>
public sealed class WriteCallSiteGuardTests
{
    private const string Kernel32File = "src/UefiAccess.Core/Native/Kernel32.cs";
    private const string NtDllFile = "src/UefiAccess.Core/Native/NtDll.cs";
    private const string WriterFile = "src/UefiAccess.Core/Safety/FirmwareVariableWriter.cs";
    private const string TransactionFile = "src/UefiAccess.Core/Safety/WriteTransaction.cs";
    private const string SafetyDirectory = "src/UefiAccess.Core/Safety/";
    private const string ThisFile = "tests/UefiAccess.Core.Tests/Safety/WriteCallSiteGuardTests.cs";

    private const string WriteImport = "SetFirmwareEnvironmentVariableExW";
    private const string LastNtStatusImport = "RtlGetLastNtStatus";
    private const string WriterMethod = "CommitOnce";
    private const string RealWriteSeam = "WindowsNativeFirmwareWriteApi";

    /// <summary>The only files that compose the real native write seam, inside their CreateWindows.</summary>
    private static readonly string[] CompositionRoots =
    {
        "src/UefiAccess.Cli/Services/CliPlatform.cs",
        "src/UefiAccess.Presentation/Services/LiveFirmwareSource.cs",
    };

    /// <summary>The extern keyword, which marks the write import's declaration.</summary>
    private static readonly Regex ExternPattern = new(@"\bextern\b", RegexOptions.CultureInvariant);

    /// <summary>An ntdll setter, which the product never imports: the write goes through the documented kernel32 function.</summary>
    private static readonly Regex NtdllSetterPattern = new(@"\b(?:Nt|Zw)Set\w*", RegexOptions.CultureInvariant);

    /// <summary>A loop or jump keyword, as code (comments and literals are blanked before the search).</summary>
    private static readonly Regex LoopKeywordPattern = new(@"\b(?:for|foreach|while|do|goto)\b", RegexOptions.CultureInvariant);

    /// <summary>Any identifier that speaks of a retry, in any casing and anywhere in the name: retry, ShouldRetry, Retries, retried.</summary>
    private static readonly Regex RetryPattern = new(@"\w*retr(?:y|ies|ied)\w*", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>The writer's one method named with an argument list: a call, or, followed by a body, a declaration.</summary>
    private static readonly Regex WriterCallPattern = new(@"(?<!\w)" + WriterMethod + @"\s*\(", RegexOptions.CultureInvariant);

    /// <summary>A declaration's parameter list of the writer's method, as an interface member (followed by ";") shows it: one WritePlan.</summary>
    private static readonly Regex WriterParameterPattern = new(@"^WritePlan\s+@?\w+$", RegexOptions.CultureInvariant);

    /// <summary>A method of the transaction whose name starts with Commit: the commit path the design names.</summary>
    private static readonly Regex CommitMethodPattern = new(@"(?<!\w)(?<name>Commit\w*)\s*\(", RegexOptions.CultureInvariant);

    /// <summary>A read of the variable store, as the verification after the write makes it.</summary>
    private static readonly Regex StoreReadPattern = new(@"\.\s*Read\s*\(", RegexOptions.CultureInvariant);

    /// <summary>A call of a method of the same class (no receiver, or this.), so its body can be searched too.</summary>
    private static readonly Regex OwnMethodCallPattern = new(@"(?<![\w.])(?:this\s*\.\s*)?(?<name>[A-Z]\w*)\s*\(", RegexOptions.CultureInvariant);

    /// <summary>A WritePlan built with new, explicit or target-typed into a variable of that type.</summary>
    private static readonly Regex WritePlanConstructionPattern = new(
        @"\bnew\s+WritePlan\s*[({]|\bWritePlan\??\s+@?\w+\s*=\s*new\b",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// The real writer or the real suspender constructed in a test: the bare type name followed by
    /// an argument list, a variable of that type assigned from new, Activator.CreateInstance, which
    /// could build either from a type, or GetUninitializedObject (RuntimeHelpers or
    /// FormatterServices), which makes an instance of any type without its constructor. A fake that
    /// implements the interface (its name has a prefix) does not match, and neither does a call of
    /// their static cores (FirmwareVariableWriter.Commit, BitLockerSuspender.Suspend) over a fake seam.
    /// </summary>
    private static readonly Regex RealWriterConstructionPattern = new(
        @"\b(?:FirmwareVariableWriter|BitLockerSuspender)\s*\("
        + @"|\b(?:FirmwareVariableWriter|BitLockerSuspender)\??\s+@?\w+\s*=\s*new\b"
        + @"|\bActivator\s*\.\s*CreateInstance\b"
        + @"|\bGetUninitializedObject\b",
        RegexOptions.CultureInvariant);

    /// <summary>An instance made without its constructor, by whatever class the method is reached through.</summary>
    private static readonly Regex UninitializedObjectPattern = new(@"\bGetUninitializedObject\b", RegexOptions.CultureInvariant);

    /// <summary>
    /// The one test file allowed to make a real writer without its constructor (no seam, never
    /// called), to pin the pipeline's refusal of the real writer over a store that is not the
    /// firmware's; the one method inside it; nowhere else, and only once there.
    /// </summary>
    private const string UninitializedWriterFile = "tests/UefiAccess.Core.Tests/Safety/DefenceInDepthTests.cs";

    /// <summary>The test method of <see cref="UninitializedWriterFile"/> that holds the one allowed site.</summary>
    private const string UninitializedWriterTest = "TheRealWriter_OverAStoreThatIsNotTheFirmwares_IsRefused";

    /// <summary>The real native write seam's shared instance.</summary>
    private static readonly Regex RealWriteSeamInstancePattern = new(@"\b" + RealWriteSeam + @"\s*\.\s*Instance\b", RegexOptions.CultureInvariant);

    [Fact]
    public void FirmwareWriteIsDeclaredOnceAndCalledOnce()
    {
        List<string> declarations = new();
        List<string> calls = new();
        List<string> mentions = new();
        List<string> methodGroups = new();
        List<string> ntStatusMentions = new();
        List<string> ntStatusCalls = new();
        foreach (string file in RepositoryPaths.Enumerate("src", "*.cs"))
        {
            string relative = RepositoryPaths.Relative(file);
            string code = SourceCalls.WithoutCommentsAndStrings(File.ReadAllText(file));
            string[] lines = code.Split('\n');

            foreach (int line in SourceCalls.LinesNaming(code, WriteImport))
            {
                mentions.Add($"{relative}:{line}");
                if (ExternPattern.IsMatch(lines[line - 1]))
                {
                    declarations.Add($"{relative}:{line}: {lines[line - 1].Trim()}");
                }
            }

            calls.AddRange(SourceCalls.Find(code, "Kernel32." + WriteImport).Where(site => !site.IsDeclaration).Select(site => $"{relative}:{site.Line}"));
            methodGroups.AddRange(SourceCalls.FindReferencesWithoutCall(code, WriteImport).Select(line => $"{relative}:{line}"));
            ntStatusMentions.AddRange(SourceCalls.LinesNaming(code, LastNtStatusImport).Select(line => $"{relative}:{line}"));
            ntStatusCalls.AddRange(SourceCalls.Find(code, "NtDll." + LastNtStatusImport).Where(site => !site.IsDeclaration).Select(site => $"{relative}:{site.Line}"));
        }

        Assert.True(
            declarations.Count == 1 && declarations[0].StartsWith(Kernel32File + ":", StringComparison.Ordinal),
            "The write import must be declared exactly once, in " + Kernel32File + ". Found:" + Environment.NewLine + string.Join(Environment.NewLine, declarations));
        Assert.Matches(@"\bstatic\s+extern\s+bool\s+" + WriteImport + @"\s*\(", declarations[0]);

        Assert.True(
            calls.Count == 1 && calls[0].StartsWith(WriterFile + ":", StringComparison.Ordinal),
            "The write import must be called exactly once, as Kernel32." + WriteImport + ", from " + WriterFile + ". Found: " + string.Join(", ", calls));
        Assert.True(methodGroups.Count == 0, "The write import is named without being called (a method group or a delegate), so no call check can see it: " + string.Join(", ", methodGroups));
        Assert.True(mentions.Count == 2, "In code (comments and strings aside) the write import may appear on exactly two lines, its declaration and its call. Found: " + string.Join(", ", mentions));

        // ntdll: no setter, and the last-NTSTATUS read is declared there and called by the writer alone.
        string ntdll = SourceCalls.WithoutCommentsAndStrings(Read(NtDllFile));
        Assert.DoesNotMatch(NtdllSetterPattern, ntdll);
        Assert.Matches(@"\bextern\b[^\n]*\b" + LastNtStatusImport + @"\s*\(", ntdll);

        string[] ntStatusFiles = ntStatusMentions.Select(FileOf).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        Assert.True(
            ntStatusFiles.SequenceEqual(new[] { NtDllFile, WriterFile }.Order(StringComparer.Ordinal), StringComparer.Ordinal),
            LastNtStatusImport + " must be named in code by " + NtDllFile + " (its declaration) and " + WriterFile + " only. Found: " + string.Join(", ", ntStatusMentions));
        Assert.True(
            ntStatusCalls.Count == 1 && ntStatusCalls[0].StartsWith(WriterFile + ":", StringComparison.Ordinal),
            LastNtStatusImport + " must be called exactly once, from " + WriterFile + ". Found: " + string.Join(", ", ntStatusCalls));
    }

    [Fact]
    public void CommitOnceHasExactlyOneCaller()
    {
        List<string> calls = new();
        List<string> methodGroups = new();
        List<string> constructions = new();
        bool transactionNamesThePlan = false;
        foreach (string file in RepositoryPaths.Enumerate("src", "*.cs"))
        {
            string relative = RepositoryPaths.Relative(file);
            string code = SourceCalls.WithoutCommentsAndStrings(File.ReadAllText(file));

            // The interface member "WriteOutcome CommitOnce(WritePlan plan);" ends in ";" like a call; its parameter list tells it apart.
            calls.AddRange(SourceCalls.Find(code, WriterMethod)
                .Where(site => !site.IsDeclaration && !(site.Arguments is [string only] && WriterParameterPattern.IsMatch(only)))
                .Select(site => $"{relative}:{site.Line}"));
            methodGroups.AddRange(SourceCalls.FindReferencesWithoutCall(code, WriterMethod).Select(line => $"{relative}:{line}"));

            if (relative == TransactionFile)
            {
                transactionNamesThePlan = SourceCalls.LinesNaming(code, nameof(WritePlan)).Count > 0;
            }
            else
            {
                constructions.AddRange(SourceCalls.LinesMatching(code, WritePlanConstructionPattern).Select(line => $"{relative}:{line}"));
            }
        }

        Assert.True(
            calls.Count == 1 && calls[0].StartsWith(TransactionFile + ":", StringComparison.Ordinal),
            WriterMethod + " must be called exactly once in src/, from " + TransactionFile + ". Found: " + string.Join(", ", calls));
        Assert.True(methodGroups.Count == 0, WriterMethod + " is named without being called (a method group or a delegate): " + string.Join(", ", methodGroups));
        Assert.True(transactionNamesThePlan, TransactionFile + " never names WritePlan, yet it is the one place a plan is built");
        Assert.True(constructions.Count == 0, "WritePlan must be built by " + TransactionFile + " only. Found: " + string.Join(", ", constructions));

        // No public way to build a plan: the CLI, the GUI and a test have no constructor, factory or setter to call.
        Assert.True(typeof(WritePlan).IsSealed, "WritePlan must be sealed");
        Assert.Empty(typeof(WritePlan).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        ConstructorInfo constructor = Assert.Single(typeof(WritePlan).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance));
        Assert.True(constructor.IsAssembly, "WritePlan's constructor must be internal");
        Assert.All(typeof(WritePlan).GetProperties(), property => Assert.True(property.SetMethod is not { IsPublic: true }, "WritePlan." + property.Name + " must not be settable"));
        string[] factories = ProductAssemblies.Types()
            .Where(type => type.IsVisible)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(candidate => candidate.ReturnType == typeof(WritePlan))
                .Select(candidate => type.Name + "." + candidate.Name))
            .ToArray();
        Assert.True(factories.Length == 0, "A public method returns a WritePlan, a public way to build one: " + string.Join(", ", factories));

        // The writer's whole surface: one method, one WritePlan in, one WriteOutcome out.
        MethodInfo method = Assert.Single(typeof(IFirmwareVariableWriter).GetMethods());
        Assert.Equal(WriterMethod, method.Name);
        ParameterInfo parameter = Assert.Single(method.GetParameters());
        Assert.Equal(typeof(WritePlan), parameter.ParameterType);
        Assert.Equal(typeof(WriteOutcome), method.ReturnType);
        Assert.Empty(typeof(IFirmwareVariableWriter).GetProperties());
        Assert.Empty(typeof(IFirmwareVariableWriter).GetEvents());
        Assert.Empty(typeof(IFirmwareVariableWriter).GetInterfaces());
    }

    [Fact]
    public void NoLoopAroundTheWriteCall()
    {
        // A tripwire, recorded as such: the writer file and the
        // commit path of the transaction (its Commit* methods) hold no loop or jump keyword and no
        // identifier named after a retry; the writer call occurs once in the transaction, inside
        // the commit path, and after it the store is read exactly once, the verification of stage
        // S16 (directly, or in a method of the transaction the rest of the commit path calls).
        string writer = SourceCalls.WithoutCommentsAndStrings(Read(WriterFile));
        Assert.True(writer.Trim().Length > 0, WriterFile + " is empty");
        Assert.Empty(Describe(writer, LoopKeywordPattern, WriterFile));
        Assert.Empty(Describe(writer, RetryPattern, WriterFile));

        string transactionSource = Read(TransactionFile);
        string transaction = SourceCalls.WithoutCommentsAndStrings(transactionSource);
        string[] commitMethods = CommitMethodPattern.Matches(transaction)
            .Select(match => match.Groups["name"].Value)
            .Where(name => name != WriterMethod)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        List<CommitBody> bodies = commitMethods
            .SelectMany(name => SourceCalls.MethodBodies(transactionSource, name).Select(body => new CommitBody(name, body)))
            .ToList();
        Assert.True(bodies.Count > 0, "no Commit* method body found in " + TransactionFile);

        List<string> problems = new();
        foreach (CommitBody commit in bodies)
        {
            problems.AddRange(LoopKeywordPattern.Matches(commit.Body).Concat(RetryPattern.Matches(commit.Body)).Select(match => $"{commit.Method}: {match.Value}"));
        }

        Assert.True(problems.Count == 0, "The commit path of " + TransactionFile + " loops, jumps or retries: " + string.Join(", ", problems));

        int writerCalls = WriterCallPattern.Matches(transaction).Count;
        Assert.True(writerCalls == 1, $"{TransactionFile} must name {WriterMethod} exactly once, in the one call; found {writerCalls}");
        CommitBody[] withCall = bodies.Where(commit => WriterCallPattern.IsMatch(commit.Body)).ToArray();
        Assert.True(withCall.Length == 1, $"The {WriterMethod} call must lie in one Commit* method of {TransactionFile}; found it in {withCall.Length}");

        Match call = WriterCallPattern.Match(withCall[0].Body);
        string rest = withCall[0].Body[(call.Index + call.Length)..];
        List<string> reads = StoreReadPattern.Matches(rest).Select(_ => withCall[0].Method + " after the call").ToList();
        foreach (Match helper in OwnMethodCallPattern.Matches(rest))
        {
            string name = helper.Groups["name"].Value;
            if (commitMethods.Contains(name, StringComparer.Ordinal))
            {
                continue;
            }

            foreach (string body in SourceCalls.MethodBodies(transactionSource, name))
            {
                reads.AddRange(StoreReadPattern.Matches(body).Select(_ => name + ", called after it"));
            }
        }

        Assert.True(
            reads.Count == 1,
            $"After the {WriterMethod} call the commit path must read the store exactly once (the verification, stage S16); found {reads.Count}: {string.Join(", ", reads)}");
    }

    [Fact]
    public void TestsNeverConstructTheRealWriter()
    {
        IReadOnlyList<string> testFiles = RepositoryPaths.Enumerate("tests", "*.cs");
        Assert.Contains(testFiles, file => RepositoryPaths.Relative(file) == ThisFile);
        Assert.Contains(testFiles, file => RepositoryPaths.Relative(file) == UninitializedWriterFile);

        // This file is scanned too: it names the real types and the seam only in strings and comments.
        List<string> violations = new();
        List<string> exempted = new();
        foreach (string file in testFiles)
        {
            string relative = RepositoryPaths.Relative(file);
            string source = File.ReadAllText(file);
            string code = SourceCalls.WithoutCommentsAndStrings(source);
            IReadOnlyList<int> constructions = SourceCalls.LinesMatching(code, RealWriterConstructionPattern);
            if (relative == UninitializedWriterFile)
            {
                int? allowed = AllowedUninitializedWriterLine(source, code);
                if (allowed is int line)
                {
                    exempted.Add($"{relative}:{line}");
                    constructions = constructions.Where(found => found != line).ToArray();
                }
            }

            violations.AddRange(constructions.Select(line => $"{relative}:{line}: constructs the real writer or suspender"));
            violations.AddRange(SourceCalls.LinesNaming(code, RealWriteSeam).Select(line => $"{relative}:{line}: names the real native write seam"));
        }

        // Exactly one site is exempted, and it is the one the constants name.
        Assert.True(exempted.Count == 1, "The one allowed real writer made without its constructor must be in " + UninitializedWriterTest + " of " + UninitializedWriterFile + ", once, alone on its line. Found: " + string.Join(", ", exempted));

        Assert.True(
            violations.Count == 0,
            "A test may only ever write through a fake behind IFirmwareVariableWriter, INativeFirmwareWriteApi or IBitLockerSuspender:" + Environment.NewLine + string.Join(Environment.NewLine, violations));

        // In the product, the real seam's Instance is referenced by the two composition roots only
        //, at most once each and then inside CreateWindows; the
        // seam is declared under Safety/ and defaults nothing to itself, so a writer built
        // anywhere else has to be handed a seam explicitly. That each root does compose the writer
        // is the business of the CLI's and the GUI's own composition tests, not of this tripwire.
        List<string> problems = new();
        foreach (string file in RepositoryPaths.Enumerate("src", "*.cs"))
        {
            string relative = RepositoryPaths.Relative(file);
            string source = File.ReadAllText(file);
            string code = SourceCalls.WithoutCommentsAndStrings(source);
            IReadOnlyList<int> mentions = SourceCalls.LinesNaming(code, RealWriteSeam);
            if (mentions.Count == 0)
            {
                continue;
            }

            if (Regex.IsMatch(code, @"\bclass\s+" + RealWriteSeam + @"\b", RegexOptions.CultureInvariant))
            {
                if (!relative.StartsWith(SafetyDirectory, StringComparison.Ordinal))
                {
                    problems.Add($"{relative}: declares {RealWriteSeam} outside {SafetyDirectory}");
                }

                foreach (int line in SourceCalls.LinesMatching(code, RealWriteSeamInstancePattern))
                {
                    problems.Add($"{relative}:{line}: uses {RealWriteSeam}.Instance as a default; only the composition roots may");
                }
            }
            else if (CompositionRoots.Contains(relative, StringComparer.Ordinal))
            {
                int inFile = SourceCalls.LinesMatching(code, RealWriteSeamInstancePattern).Count;
                int inCreateWindows = SourceCalls.MethodBodies(source, "CreateWindows").Sum(body => RealWriteSeamInstancePattern.Matches(body).Count);
                if (mentions.Count != 1 || inFile != 1 || inCreateWindows != 1)
                {
                    problems.Add($"{relative}: {RealWriteSeam}.Instance must be composed exactly once, inside CreateWindows (named on {mentions.Count} line(s), .Instance on {inFile}, inside CreateWindows {inCreateWindows})");
                }
            }
            else
            {
                problems.Add($"{relative}:{mentions[0]}: references {RealWriteSeam} outside the composition roots");
            }
        }

        Assert.True(problems.Count == 0, "The real native write seam is composed by CliPlatform.CreateWindows and LiveFirmwareSource.CreateWindows only:" + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// The line of the one allowed site in <see cref="UninitializedWriterFile"/>: the file names
    /// GetUninitializedObject exactly once, inside <see cref="UninitializedWriterTest"/>, and
    /// nothing else on that line constructs the real writer or suspender. Null otherwise, so every
    /// such line of the file is reported as a violation.
    /// </summary>
    private static int? AllowedUninitializedWriterLine(string source, string code)
    {
        MatchCollection inFile = UninitializedObjectPattern.Matches(code);
        int inTest = SourceCalls.MethodBodies(source, UninitializedWriterTest).Sum(body => UninitializedObjectPattern.Matches(body).Count);
        if (inFile.Count != 1 || inTest != 1)
        {
            return null;
        }

        int line = SourceCalls.LineOf(code, inFile[0].Index);
        string text = code.Split('\n')[line - 1];
        return RealWriterConstructionPattern.Matches(text).Count == 1 ? line : null;
    }

    /// <summary>"file:line: match" for every match of <paramref name="pattern"/> in <paramref name="code"/>.</summary>
    private static IReadOnlyList<string> Describe(string code, Regex pattern, string file) =>
        pattern.Matches(code).Select(match => $"{file}:{SourceCalls.LineOf(code, match.Index)}: {match.Value}").ToArray();

    /// <summary>The file part of a "file:line" mention.</summary>
    private static string FileOf(string mention) => mention[..mention.LastIndexOf(':')];

    private static string Read(string relative) => File.ReadAllText(Path.Combine(RepositoryPaths.Root, relative.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>One body of a Commit* method of the transaction, comments and literals blanked.</summary>
    /// <param name="Method">The method's name.</param>
    /// <param name="Body">The body's text.</param>
    private sealed record CommitBody(string Method, string Body);
}
