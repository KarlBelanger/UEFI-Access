// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The product starts child processes in one place only: ProcessRunner in Core, which the Dell
/// interface probe calls to run cctk.exe with --version or --secureboot, the Dell settings reader
/// (Settings/Dell/CctkReader.cs, since M4) calls to run cctk.exe with one of its nine read options,
/// and the Secure Boot policy inspector calls to run powershell.exe with Confirm-SecureBootUEFI.
/// Every C# file under src/ is scanned as text, a whole file at a time, so a statement laid out
/// over several lines is read as one: a process may be created only in ProcessRunner.cs, and
/// ProcessRunner.Run may be used only from those three files. Creation means Process.Start,
/// called or named as a method group; a ProcessStartInfo or a Process built with new (explicit,
/// qualified or not, target-typed or with an object initializer); the StartInfo property;
/// "using static System.Diagnostics.Process"; a using alias of Process or ProcessStartInfo; or a
/// COM class activated by ProgID or CLSID (WScript.Shell and Shell.Application run programs).
/// Using ProcessRunner.Run means calling it or naming it as a method group, as Run or as the
/// verbatim @Run (a cref in a documentation comment excepted), "using static" of ProcessRunner, or
/// a using alias of it. No file may write a Unicode escape in code, where it can only spell an
/// identifier that no pattern here would read (ProcessRunner.Run). The built assemblies must
/// not import a process-creation API (CreateProcess*, ShellExecute*, WinExec and the ntdll
/// equivalents) either. Another tool (bcdedit, reg, wmic, a cctk run elsewhere)
/// therefore cannot be started in any of these ways without this test naming the file;
/// <see cref="NoWriteGuardTests"/> checks what the three callers pass. A text scan cannot see the Process type reached through reflection or
/// dynamic (a type name in a string, Activator.CreateInstance), so those are beyond it. The
/// matchers are exercised against synthetic lines as well.
/// </summary>
public sealed class ProcessLaunchGuardTests
{
    /// <summary>The only file that may create a process.</summary>
    private const string ProcessRunnerFile = "src/UefiAccess.Core/Platform/ProcessRunner.cs";

    /// <summary>The only files that may call ProcessRunner.Run; the Dell settings reader is the third since M4.</summary>
    private static readonly string[] ProcessRunnerCallers =
    {
        "src/UefiAccess.Core/Platform/DellInterfaceProbe.cs",
        "src/UefiAccess.Core/Platform/SecureBootPolicyInspector.cs",
        "src/UefiAccess.Core/Settings/Dell/CctkReader.cs",
    };

    /// <summary>A namespace or type qualifier before a type name, such as "System.Diagnostics." or "global::System.Diagnostics.".</summary>
    private const string Qualifier = @"(?:@?\w+\s*(?:\.|::)\s*)*";

    /// <summary>
    /// Code that creates a process directly, names Process.Start, imports the Process type in a
    /// way that hides its name (using static, a using alias), or activates a COM class that can (a
    /// comment naming ProcessStartInfo alone does not match). Whitespace includes line breaks, so a
    /// whole file can be matched at once.
    /// </summary>
    private static readonly Regex ProcessCreationPattern = new(
        @"\bProcess\s*\.\s*Start\b"
        + @"|\bnew\s+" + Qualifier + @"ProcessStartInfo\b|\bProcessStartInfo\??\s+@?\w+\s*="
        + @"|\bnew\s+" + Qualifier + @"Process\s*[({]|\bProcess\??\s+@?\w+\s*=\s*new\b"
        + @"|\bStartInfo\b"
        + @"|\busing\s+static\s+(?:global::)?System\.Diagnostics\.Process\b"
        + @"|\busing\s+@?\w+\s*=\s*" + Qualifier + @"Process(?:StartInfo)?\s*;"
        + @"|\bGetTypeFrom(?:ProgID|CLSID)\b",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// A use of the shared runner: ProcessRunner.Run called or named as a method group, the
    /// verbatim "@Run" included (a cref, followed by a quote, is not a use), "using static" of
    /// ProcessRunner, or a using alias of it.
    /// </summary>
    private static readonly Regex ProcessRunnerCallPattern = new(
        @"\bProcessRunner\s*\.\s*@?Run\b(?!\s*"")"
        + @"|\busing\s+(?:static\s+|@?\w+\s*=\s*)" + Qualifier + @"ProcessRunner\s*;",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// A Unicode escape, which in code (outside comments and literals) can only spell part of an
    /// identifier: "ProcessRunner.Run" is ProcessRunner.Run to the compiler but to no pattern
    /// here, so no file under src/ may write one in code.
    /// </summary>
    private static readonly Regex UnicodeEscapePattern = new(@"\\u[0-9A-Fa-f]{4}|\\U[0-9A-Fa-f]{8}", RegexOptions.CultureInvariant);

    /// <summary>Native entry points that start a process.</summary>
    private static readonly Regex ProcessCreationEntryPointPattern = new(
        @"^(?:CreateProcess|ShellExecute|WinExec|(?:Nt|Zw)Create(?:User)?Process|RtlCreateUserProcess)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    [Fact]
    public void ProcessesAreStartedOnlyThroughProcessRunnerByTheThreeKnownCallers()
    {
        IReadOnlyList<string> files = RepositoryPaths.Enumerate("src", "*.cs");
        Assert.NotEmpty(files);

        List<string> violations = new();
        HashSet<string> callers = new(StringComparer.Ordinal);
        bool runnerCreatesProcess = false;
        foreach (string file in files)
        {
            string relative = RepositoryPaths.Relative(file);
            string text = File.ReadAllText(file);
            string[] lines = text.Split('\n');
            foreach (int line in ProcessCreationLines(text))
            {
                if (relative == ProcessRunnerFile)
                {
                    runnerCreatesProcess = true;
                }
                else
                {
                    violations.Add($"{relative}:{line}: creates a process outside ProcessRunner: {lines[line - 1].Trim()}");
                }
            }

            foreach (int line in ProcessRunnerCallLines(text))
            {
                callers.Add(relative);
                if (!ProcessRunnerCallers.Contains(relative, StringComparer.Ordinal))
                {
                    violations.Add($"{relative}:{line}: uses ProcessRunner.Run from a file that is not a known read-only caller: {lines[line - 1].Trim()}");
                }
            }

            foreach (int line in UnicodeEscapeLines(text))
            {
                violations.Add($"{relative}:{line}: spells an identifier with a Unicode escape, which hides it from this scan: {lines[line - 1].Trim()}");
            }
        }

        Assert.True(runnerCreatesProcess, "ProcessRunner.cs no longer creates the process; the guard would be scanning for the wrong shape");
        Assert.All(ProcessRunnerCallers, caller => Assert.Contains(caller, callers));
        Assert.True(
            violations.Count == 0,
            "Child processes may only be started by ProcessRunner, for cctk and PowerShell:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void BuiltAssembliesImportNoProcessCreationApi()
    {
        IReadOnlyList<string> entryPoints = NativeLocationGuardTests.NativeEntryPoints();
        Assert.NotEmpty(entryPoints);

        string[] violations = entryPoints.Where(entry => IsProcessCreationEntryPoint(entry[(entry.IndexOf('!', StringComparison.Ordinal) + 1)..])).ToArray();
        Assert.True(
            violations.Length == 0,
            "The built assemblies import a native API that starts a process:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [InlineData("Process.Start(\"bcdedit.exe\", \"/enum\");")]
    [InlineData("using Process process = Process.Start(startInfo)!;")]
    [InlineData("ProcessStartInfo startInfo = new(fileName)")]
    [InlineData("var startInfo = new ProcessStartInfo(\"reg.exe\") { UseShellExecute = false };")]
    [InlineData("Process process = new System.Diagnostics.Process();")]
    [InlineData("using Process process = new() { StartInfo = startInfo };")]
    [InlineData("var process = new Process { StartInfo = { FileName = \"bcdedit.exe\" } };")]
    [InlineData("Process p = new() { EnableRaisingEvents = true };")]
    [InlineData("Process? child = new();")]
    [InlineData("child.StartInfo.FileName = tool;")]
    [InlineData("using static System.Diagnostics.Process;")]
    [InlineData("Type shell = Type.GetTypeFromProgID(\"WScript.Shell\")!;")]
    [InlineData("Type? application = Type.GetTypeFromCLSID(clsid);")]
    [InlineData("Func<ProcessStartInfo, Process?> start = Process.Start;")]
    [InlineData("return tools.Select(System.Diagnostics.Process.Start).ToArray();")]
    [InlineData("using Launcher = System.Diagnostics.Process;")]
    [InlineData("global using Launch = global::System.Diagnostics.ProcessStartInfo;")]
    [InlineData("var info = new global::System.Diagnostics.ProcessStartInfo(tool);")]
    [InlineData("var child = new Diagnostics.Process { EnableRaisingEvents = true };")]
    [InlineData("using Process? child = Process\n    .Start(tool);")]
    [InlineData("Process child =\n    new();")]
    public void Matcher_FlagsEveryProcessCreationForm(string line)
    {
        Assert.True(IsProcessCreation(line));
    }

    [Fact]
    public void WholeFileScan_ReportsTheLineWhereAStatementSplitOverLinesStarts()
    {
        const string source = "namespace Example;\n\ninternal static class Launcher\n{\n    public static void Go(string tool) => System.Diagnostics.Process\n        .Start(tool);\n\n    public static void Run(string tool) => UefiAccess.Core.Platform.ProcessRunner\n        .Run(tool, null, TimeSpan.FromSeconds(1), Serilog.Core.Logger.None);\n}\n";

        Assert.Equal(new[] { 5 }, ProcessCreationLines(source));
        Assert.Equal(new[] { 8 }, ProcessRunnerCallLines(source));
    }

    [Theory]
    [InlineData("/// ProcessRunner sets ProcessStartInfo.StandardOutputEncoding for the child.")]
    [InlineData("ProcessRunResult run = ProcessRunner.Run(cctkPath, workingDirectory, CctkTimeout, _logger, argument);")]
    [InlineData("int id = Environment.ProcessId;")]
    [InlineData("using Process current = Process.GetCurrentProcess();")]
    [InlineData("IntPtr handle = Kernel32.GetCurrentProcess();")]
    [InlineData("ProcessRunResult result = new(true, 0, output, error, false, null, elapsed);")]
    [InlineData("TimeSpan age = DateTime.Now - Process.GetCurrentProcess().StartTime;")]
    [InlineData("using Diagnostics = System.Diagnostics;")]
    [InlineData("using ProcessRunResult = UefiAccess.Core.Platform.ProcessRunResult;")]
    [InlineData("using System.Diagnostics;")]
    [InlineData("ProcessRunResult run = ProcessRunner.Run(executable, null, PowerShellTimeout, PowerShellOutputEncoding, _logger, \"-NoProfile\", \"-NonInteractive\", \"-Command\", PowerShellScript);")]
    [InlineData("")]
    public void Matcher_IgnoresCodeThatCreatesNoProcess(string line)
    {
        Assert.False(IsProcessCreation(line));
    }

    [Theory]
    [InlineData("ProcessRunResult run = ProcessRunner.Run(cctkPath, workingDirectory, CctkTimeout, _logger, argument);", true)]
    [InlineData("ProcessRunner.Run (executable, null, PowerShellTimeout, _logger, \"-Command\", PowerShellCommand);", true)]
    [InlineData("Func<string, string?, TimeSpan, ILogger, string[], ProcessRunResult> run = ProcessRunner.Run;", true)]
    [InlineData("ProcessRunResult run = UefiAccess.Core.Platform.ProcessRunner\n    .Run(tool, null, timeout, _logger);", true)]
    [InlineData("using static UefiAccess.Core.Platform.ProcessRunner;", true)]
    [InlineData("using Runner = UefiAccess.Core.Platform.ProcessRunner;", true)]
    [InlineData("ProcessRunner.@Run(path, null, timeout, _logger, argument);", true)]
    [InlineData("/// <summary>Outcome of running a child process through <see cref=\"ProcessRunner.Run\"/>.</summary>", false)]
    [InlineData("public static ProcessRunResult Run(string fileName, string? workingDirectory, TimeSpan timeout, ILogger logger, params string[] arguments)", false)]
    [InlineData("using UefiAccess.Core.Platform;", false)]
    [InlineData("internal static class ProcessRunner", false)]
    [InlineData("TimeSpan elapsed = ProcessRunner.RunTimeout;", false)]
    public void Matcher_RecognisesProcessRunnerCalls(string line, bool expected)
    {
        Assert.Equal(expected, IsProcessRunnerCall(line));
    }

    [Theory]
    [InlineData("ProcessRunResult run = ProcessRunner.\\u0052un(path, null, timeout, _logger, argument);", true)]
    [InlineData("var process = System.Diagnostics.Process.St\\u0061rt(tool);", true)]
    [InlineData("string ellipsis = text[..keep] + \"\\u2026\";", false)]
    [InlineData("private const char LiteralMark = '\\u0001';", false)]
    [InlineData("// ProcessRunner.\\u0052un in a comment", false)]
    public void UnicodeEscapeScan_FlagsEscapesInCodeOnly(string line, bool expected)
    {
        Assert.Equal(expected, UnicodeEscapeLines(line).Count > 0);
    }

    [Theory]
    [InlineData("CreateProcessW", true)]
    [InlineData("CreateProcessAsUserW", true)]
    [InlineData("ShellExecuteExW", true)]
    [InlineData("WinExec", true)]
    [InlineData("NtCreateUserProcess", true)]
    [InlineData("RtlCreateUserProcess", true)]
    [InlineData("GetCurrentProcess", false)]
    [InlineData("OpenProcessToken", false)]
    [InlineData("GetFirmwareEnvironmentVariableExW", false)]
    public void EntryPointCheck_FlagsTheProcessCreationApis(string entryPoint, bool expected)
    {
        Assert.Equal(expected, IsProcessCreationEntryPoint(entryPoint));
    }

    /// <summary>True when the source text creates a process.</summary>
    /// <param name="source">C# source: one line, or several.</param>
    /// <returns>True for any form <see cref="ProcessCreationPattern"/> describes.</returns>
    internal static bool IsProcessCreation(string source) => ProcessCreationPattern.IsMatch(source);

    /// <summary>True when the source text uses ProcessRunner.Run.</summary>
    /// <param name="source">C# source: one line, or several.</param>
    /// <returns>True for any form <see cref="ProcessRunnerCallPattern"/> describes.</returns>
    internal static bool IsProcessRunnerCall(string source) => ProcessRunnerCallPattern.IsMatch(source);

    /// <summary>The lines of a whole file on which a process creation starts.</summary>
    /// <param name="text">The file's text.</param>
    /// <returns>1-based line numbers, in order, each once.</returns>
    internal static IReadOnlyList<int> ProcessCreationLines(string text) => MatchLines(ProcessCreationPattern, text);

    /// <summary>The lines of a whole file on which a use of ProcessRunner.Run starts.</summary>
    /// <param name="text">The file's text.</param>
    /// <returns>1-based line numbers, in order, each once.</returns>
    internal static IReadOnlyList<int> ProcessRunnerCallLines(string text) => MatchLines(ProcessRunnerCallPattern, text);

    /// <summary>The lines of a whole file on which code, outside comments and literals, holds a Unicode escape.</summary>
    /// <param name="text">The file's text.</param>
    /// <returns>1-based line numbers, in order, each once.</returns>
    internal static IReadOnlyList<int> UnicodeEscapeLines(string text) => SourceCalls.LinesMatching(SourceCalls.WithoutCommentsAndStrings(text), UnicodeEscapePattern);

    private static IReadOnlyList<int> MatchLines(Regex pattern, string text) =>
        pattern.Matches(text).Select(match => 1 + text.AsSpan(0, match.Index).Count('\n')).Distinct().ToArray();

    /// <summary>True when a native entry point starts a process.</summary>
    /// <param name="entryPoint">The entry-point name as emitted into metadata.</param>
    /// <returns>True for CreateProcess*, ShellExecute*, WinExec and the ntdll process creators.</returns>
    internal static bool IsProcessCreationEntryPoint(string entryPoint) => ProcessCreationEntryPointPattern.IsMatch(entryPoint);
}
