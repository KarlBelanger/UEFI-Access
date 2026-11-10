// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The CLI and the WPF shell must hand their run logger to every Core service they compose. The
/// platform inspectors log every WMI query, BitLocker InvokeMethod call and cctk or PowerShell
/// launch, and the privilege manager, the firmware type detector and the variable store log every
/// native call, only through the logger they are given; built with a logger-less constructor they
/// log nothing, and the ProgramData log and the baseline's log.txt lose the trace of what ran
/// (design section 11). The composition roots are CliPlatform.CreateWindows in UefiAccess.Cli and,
/// for the WPF shell, LiveFirmwareSource.CreateWindows in UefiAccess.Presentation, which App.xaml.cs
/// reaches through ShellViewModel.CreateWindows. The App cannot be referenced from the test
/// project, so every C# file under src/ is scanned as text: every construction of one of the
/// services, written "new Type(...)" or as a target-typed "new(...)" for a declared Type, and every
/// call of the two WPF factories must pass a logger as one of its top-level arguments. Each project
/// must still construct or call what its part of the chain needs, so the check is not vacuous for
/// either shell. The matcher is exercised against synthetic lines as well.
/// </summary>
public sealed class CompositionRootLoggingTests
{
    /// <summary>
    /// The Core services that log their native, WMI, registry or process calls only through the
    /// logger they are given: the five platform inspectors, the privilege manager, the firmware type
    /// detector, the variable store and, since M4, the Dell settings reader, whose queries and runs would otherwise be missing from the log.
    /// </summary>
    private static readonly string[] Services =
    {
        "MachineIdentity",
        "TpmInfo",
        "BitLockerInspector",
        "DellInterfaceProbe",
        "SecureBootPolicyInspector",
        "PrivilegeManager",
        "FirmwareTypeDetector",
        "WindowsFirmwareVariableStore",
        "DellSettingsReader",
    };

    /// <summary>The static factories that carry the WPF shell's logger from App.xaml.cs down to the services.</summary>
    private static readonly string[] Factories = { "ShellViewModel.CreateWindows", "LiveFirmwareSource.CreateWindows", "LiveDellSettingsSource.CreateWindows" };

    /// <summary>
    /// What each project must construct or call. The CLI composes every service in
    /// CliPlatform.CreateWindows. The App calls ShellViewModel.CreateWindows, which calls
    /// LiveFirmwareSource.CreateWindows, which composes the four services the live source uses,
    /// and, since M4, LiveDellSettingsSource.CreateWindows, which composes the Dell settings reader
    ///.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> RequiredPerProject = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["UefiAccess.Cli"] = Services,
        ["UefiAccess.App"] = new[] { "ShellViewModel.CreateWindows" },
        ["UefiAccess.Presentation"] = new[] { "LiveFirmwareSource.CreateWindows", "FirmwareTypeDetector", "PrivilegeManager", "WindowsFirmwareVariableStore", "MachineIdentity", "LiveDellSettingsSource.CreateWindows", "DellSettingsReader" },
    };

    /// <summary>An argument list in parentheses, nested parentheses balanced.</summary>
    private const string BalancedArguments = @"\((?<arguments>(?>[^()]+|\((?<depth>)|\)(?<-depth>))*(?(depth)(?!)))\)";

    /// <summary>"new Type(...)", the type optionally namespace-qualified.</summary>
    private static readonly Regex ExplicitConstruction = new(
        @"\b(?<at>new)\s+(?:[A-Za-z_]\w*\.)*(?<type>" + string.Join("|", Services) + @")\s*" + BalancedArguments,
        RegexOptions.CultureInvariant);

    /// <summary>"Type name = new(...)": a local, field or property of a service type initialised with a target-typed new.</summary>
    private static readonly Regex TargetTypedConstruction = new(
        @"(?<![\w.])(?:[A-Za-z_]\w*\.)*(?<type>" + string.Join("|", Services) + @")\??\s+@?[A-Za-z_]\w*\s*(?:\{[^{}]*\}\s*)?=\s*(?<at>new)\s*" + BalancedArguments,
        RegexOptions.CultureInvariant);

    /// <summary>A call of one of the WPF factories.</summary>
    private static readonly Regex FactoryCall = new(
        @"(?<![\w.])(?:[A-Za-z_]\w*\.)*(?<at>(?<type>" + string.Join("|", Factories.Select(Regex.Escape)) + @"))\s*" + BalancedArguments,
        RegexOptions.CultureInvariant);

    /// <summary>
    /// A top-level argument that is a logger: optionally named ("logger: log"), optionally a member
    /// access ("services.Logger"), ending in log, logger, _log or _logger, or in Log or Logger after
    /// other letters (fileLogger, Logger). Serilog.Core.Logger.None, null and catalog do not match.
    /// </summary>
    private static readonly Regex LoggerArgument = new(
        @"^(?:[A-Za-z_]\w*\s*:\s*)?(?:(?:this|base|[A-Za-z_]\w*)\.)*(?:_?log(?:ger)?|\w*Log(?:ger)?)$",
        RegexOptions.CultureInvariant);

    [Fact]
    public void CompositionRootsPassTheirLoggerToEveryServiceTheyCompose()
    {
        IReadOnlyList<string> files = RepositoryPaths.Enumerate("src", "*.cs");
        Assert.NotEmpty(files);

        List<string> violations = new();
        Dictionary<string, HashSet<string>> constructedPerProject = new(StringComparer.Ordinal);
        foreach (string file in files)
        {
            string relative = RepositoryPaths.Relative(file);
            string project = relative.Split('/')[1];
            foreach (ConstructionSite site in FindConstructions(File.ReadAllText(file)))
            {
                if (!constructedPerProject.TryGetValue(project, out HashSet<string>? constructed))
                {
                    constructed = new HashSet<string>(StringComparer.Ordinal);
                    constructedPerProject.Add(project, constructed);
                }

                constructed.Add(site.Type);
                if (!site.PassesLogger)
                {
                    violations.Add(relative + ":" + site.Line + ": " + site.Describe() + " does not pass the run logger");
                }
            }
        }

        foreach ((string project, string[] required) in RequiredPerProject)
        {
            constructedPerProject.TryGetValue(project, out HashSet<string>? constructed);
            string[] missing = required.Where(type => constructed?.Contains(type) != true).ToArray();
            Assert.True(
                missing.Length == 0,
                "src/" + project + " no longer constructs or calls " + string.Join(", ", missing) + ", so this guard would be vacuous for that composition root. If the composition moved, update RequiredPerProject.");
        }

        Assert.True(
            violations.Count == 0,
            "Service constructions without the run logger (native calls, WMI queries and process launches would be lost from the log):" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [InlineData("Identity = new MachineIdentity();", "MachineIdentity", false)]
    [InlineData("Identity = new MachineIdentity(logger);", "MachineIdentity", true)]
    [InlineData("new MachineIdentity(log),", "MachineIdentity", true)]
    [InlineData("Tpm = new TpmInfo(_logger);", "TpmInfo", true)]
    [InlineData("Tpm = new TpmInfo(Serilog.Core.Logger.None);", "TpmInfo", false)]
    [InlineData("BitLocker = new BitLockerInspector( );", "BitLockerInspector", false)]
    [InlineData("Dell = new DellInterfaceProbe(fileLogger);", "DellInterfaceProbe", true)]
    [InlineData("return new SecureBootPolicyInspector(Store, runPowerShellCrossCheck);", "SecureBootPolicyInspector", false)]
    [InlineData("return new SecureBootPolicyInspector(Store, runPowerShellCrossCheck, Logger);", "SecureBootPolicyInspector", true)]
    [InlineData("return new SecureBootPolicyInspector(\n    Store,\n    runPowerShellCrossCheck,\n    Logger);", "SecureBootPolicyInspector", true)]
    [InlineData("new PrivilegeManager(log),", "PrivilegeManager", true)]
    [InlineData("new PrivilegeManager(),", "PrivilegeManager", false)]
    [InlineData("private readonly PrivilegeManager _privileges = new();", "PrivilegeManager", false)]
    [InlineData("FirmwareTypeDetector detector = new(log);", "FirmwareTypeDetector", true)]
    [InlineData("FirmwareTypeDetector detector = new();", "FirmwareTypeDetector", false)]
    [InlineData("public FirmwareTypeDetector Detector { get; } = new(logger);", "FirmwareTypeDetector", true)]
    [InlineData("new WindowsFirmwareVariableStore(detector, log),", "WindowsFirmwareVariableStore", true)]
    [InlineData("new WindowsFirmwareVariableStore(detector, logger: _logger),", "WindowsFirmwareVariableStore", true)]
    [InlineData("IFirmwareVariableStore store = new WindowsFirmwareVariableStore(detector);", "WindowsFirmwareVariableStore", false)]
    [InlineData("IFirmwareVariableStore store = new UefiAccess.Core.Variables.WindowsFirmwareVariableStore(detector, services.Logger);", "WindowsFirmwareVariableStore", true)]
    [InlineData("Identity = new MachineIdentity(catalog);", "MachineIdentity", false)]
    [InlineData("Identity = new MachineIdentity(logger ?? Serilog.Core.Logger.None);", "MachineIdentity", false)]
    [InlineData("new DellSettingsReader(null),", "DellSettingsReader", false)]
    [InlineData("new DellSettingsReader(log),", "DellSettingsReader", true)]
    [InlineData("Shell = ShellViewModel.CreateWindows(_announcer, focus, platform, Logger, detailScheduler);", "ShellViewModel.CreateWindows", true)]
    [InlineData("Shell = ShellViewModel.CreateWindows(_announcer, focus, platform, null, detailScheduler);", "ShellViewModel.CreateWindows", false)]
    [InlineData("LiveFirmwareSource.CreateWindows(log),", "LiveFirmwareSource.CreateWindows", true)]
    [InlineData("LiveFirmwareSource.CreateWindows(null),", "LiveFirmwareSource.CreateWindows", false)]
    [InlineData("LiveDellSettingsSource.CreateWindows(log));", "LiveDellSettingsSource.CreateWindows", true)]
    [InlineData("LiveDellSettingsSource.CreateWindows(null));", "LiveDellSettingsSource.CreateWindows", false)]
    [InlineData("return new LiveDellSettingsSource(new DellSettingsReader(coreLog));", "DellSettingsReader", true)]
    public void MatcherRecognisesLoggerArguments(string source, string expectedType, bool expectedPassesLogger)
    {
        ConstructionSite site = Assert.Single(FindConstructions(source));
        Assert.Equal(expectedType, site.Type);
        Assert.Equal(expectedPassesLogger, site.PassesLogger);
    }

    [Fact]
    public void MatcherLooksAtTopLevelArgumentsOfNestedConstructions()
    {
        List<ConstructionSite> sites = FindConstructions("Store = new WindowsFirmwareVariableStore(new FirmwareTypeDetector(log));");
        Assert.Equal(2, sites.Count);
        Assert.Equal(("WindowsFirmwareVariableStore", false), (sites[0].Type, sites[0].PassesLogger));
        Assert.Equal(("FirmwareTypeDetector", true), (sites[1].Type, sites[1].PassesLogger));

        ConstructionSite wrapped = Assert.Single(FindConstructions("return new MachineIdentity(Wrap(log));"));
        Assert.False(wrapped.PassesLogger);
    }

    [Fact]
    public void MatcherIgnoresUnrelatedConstructionsAndReportsLines()
    {
        // Before the WPF chain was covered this asserted that PrivilegeManager and
        // WindowsFirmwareVariableStore were ignored; they are services now, so unrelated types stand in.
        Assert.Empty(FindConstructions(
            "Exporter = new BaselineExporter();\n"
            + "LogCapture capture = new();\n"
            + "MachineIdentityResult identity = new(\"Dell Inc.\", \"OptiPlex 7020\");\n"
            + "IFirmwareTypeDetector detector = Create();\n"
            + "return Create(verbose, CliPlatform.CreateWindows, Console.Out, Console.Error, null);\n"
            + "public static LiveFirmwareSource CreateWindows(ILogger? logger)\n"
            + "/// <see cref=\"ShellViewModel.CreateWindows\"/>"));

        List<ConstructionSite> sites = FindConstructions("line one\nIdentity = new MachineIdentity();\n\nDell = new DellInterfaceProbe(logger);\nFirmwareTypeDetector detector = new(log);");
        Assert.Equal(3, sites.Count);
        Assert.Equal(2, sites[0].Line);
        Assert.Equal(4, sites[1].Line);
        Assert.Equal(5, sites[2].Line);
    }

    /// <summary>
    /// Finds every service construction and factory call in <paramref name="source"/> with its line
    /// number and whether a logger is passed as a top-level argument.
    /// </summary>
    /// <param name="source">C# source text.</param>
    /// <returns>The sites in document order.</returns>
    internal static List<ConstructionSite> FindConstructions(string source)
    {
        return new[] { ExplicitConstruction, TargetTypedConstruction, FactoryCall }
            .SelectMany(pattern => OverlappingMatches(pattern, source))
            .OrderBy(match => match.Groups["at"].Index)
            .Select(match =>
            {
                string arguments = match.Groups["arguments"].Value.Trim();
                bool passesLogger = SourceCalls.SplitTopLevelArguments(arguments).Any(argument => LoggerArgument.IsMatch(argument));
                int line = 1 + source.AsSpan(0, match.Groups["at"].Index).Count('\n');
                return new ConstructionSite(line, match.Groups["type"].Value, arguments, passesLogger);
            })
            .ToList();
    }

    /// <summary>
    /// Every match of <paramref name="pattern"/>, including one that starts inside the argument
    /// list of another, so "new A(new B(log))" yields both constructions.
    /// </summary>
    private static IEnumerable<Match> OverlappingMatches(Regex pattern, string source)
    {
        for (Match match = pattern.Match(source); match.Success; match = pattern.Match(source, match.Index + 1))
        {
            yield return match;
        }
    }

    /// <summary>One service construction or factory call found in source text.</summary>
    /// <param name="Line">1-based line of the "new" keyword or the factory name.</param>
    /// <param name="Type">Service type name, or the factory as "Type.CreateWindows".</param>
    /// <param name="Arguments">Trimmed argument list text.</param>
    /// <param name="PassesLogger">True when a top-level argument is a real logger identifier.</param>
    internal sealed record ConstructionSite(int Line, string Type, string Arguments, bool PassesLogger)
    {
        /// <summary>The site as code, for the failure message.</summary>
        /// <returns>"new Type(arguments)" or "Type.CreateWindows(arguments)".</returns>
        public string Describe() => (Type.Contains('.', StringComparison.Ordinal) ? string.Empty : "new ") + Type + "(" + Arguments + ")";
    }
}
