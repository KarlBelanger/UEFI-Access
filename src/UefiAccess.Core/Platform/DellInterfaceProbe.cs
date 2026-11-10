// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Serilog;

namespace UefiAccess.Core.Platform;

/// <summary>Outcome of one bounded Dell Command | Monitor instance query.</summary>
/// <param name="Result">The query result (rows, error or absence).</param>
/// <param name="TimedOut">True when the query did not finish within the probe's time limit; Result then carries the timeout as its error.</param>
internal sealed record DcmQueryOutcome(WmiQueryResult Result, bool TimedOut);

/// <summary>
/// Probes the Dell BIOS settings interfaces for gate question (b), read-only:
/// the Dell Command | Monitor classes DCIM_BIOSEnumeration, DCIM_BIOSString, DCIM_BIOSInteger and
/// DCIM_BIOSService in root\dcim\sysman (which of them are defined, and which of them answer an
/// instance query; three sample attributes with their value names come from DCIM_BIOSEnumeration),
/// the native namespaces root\dcim\sysman\biosattributes and root\dcim\sysman\wmisecurity
/// (expected absent on the 2014 unit), and Dell Command | Configure's cctk.exe under the two
/// Program Files known folders (never under a folder an environment variable names), run by
/// full path only with "--version" and "--secureboot".
/// Never throws; failures are collected in Errors.
/// </summary>
public sealed class DellInterfaceProbe : IPlatformInspector<DellInterfaceProbeResult>
{
    /// <summary>Dell Command | Monitor namespace.</summary>
    internal const string SysmanNamespace = @"root\dcim\sysman";

    /// <summary>Native BIOS attribute namespace of 2018-and-later Dell platforms.</summary>
    internal const string BiosAttributesNamespace = @"root\dcim\sysman\biosattributes";

    /// <summary>Native BIOS security namespace of 2018-and-later Dell platforms.</summary>
    internal const string WmiSecurityNamespace = @"root\dcim\sysman\wmisecurity";

    /// <summary>The Dell Command | Monitor classes that answer gate question (b).</summary>
    internal static readonly string[] DcmClassNames = { "DCIM_BIOSEnumeration", "DCIM_BIOSString", "DCIM_BIOSInteger", "DCIM_BIOSService" };

    private const string SampleSource = "DCM";
    private const string SampleQuery = "SELECT AttributeName, CurrentValue, PossibleValues, PossibleValuesDescription FROM DCIM_BIOSEnumeration";
    private const int SampleCount = 3;

    /// <summary>
    /// Bound on each Dell Command | Monitor instance query, so a stopped or hung provider costs at
    /// most this long per class instead of hanging the gate.
    /// </summary>
    internal static readonly TimeSpan DcmQueryTimeout = TimeSpan.FromSeconds(60);

    /// <summary>The first of the only two cctk arguments M1 ever passes: prints the tool version.</summary>
    internal const string VersionArgument = "--version";

    /// <summary>The second of the only two cctk arguments M1 ever passes: without "=value" cctk reports the setting instead of changing it.</summary>
    internal const string SecureBootArgument = "--secureboot";

    private const string NotWindowsMessage = "the Dell interface probe requires WMI and Windows executables, which are only available on Windows";
    private static readonly TimeSpan CctkTimeout = TimeSpan.FromSeconds(15);

    private readonly ILogger _logger;

    /// <summary>Creates the probe without a logger.</summary>
    public DellInterfaceProbe()
        : this(null)
    {
    }

    /// <summary>Creates the probe that logs every WMI query and process run to <paramref name="logger"/>.</summary>
    /// <param name="logger">Optional Serilog logger; null disables logging.</param>
    public DellInterfaceProbe(ILogger? logger)
    {
        _logger = logger ?? Serilog.Core.Logger.None;
    }

    /// <inheritdoc />
    public DellInterfaceProbeResult Inspect()
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger.Debug("DellInterfaceProbe skipped: not running on Windows");
            return Empty(new[] { NotWindowsMessage });
        }

        List<string> errors = new();
        List<string> classesFound = new();
        List<string> classesResponding = new();
        List<DellAttributeSample> samples = new();
        int dcimClassCount = 0;
        bool biosAttributesPresent = false;
        bool wmiSecurityPresent = false;
        string? cctkPath = null;
        string? cctkVersion = null;
        string? cctkSecureBoot = null;
        bool? cctkSecureBootEnabled = null;

        try
        {
            ProbeDcmClasses(classesFound, classesResponding, samples, ref dcimClassCount, errors);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Dell Command | Monitor probe failed unexpectedly");
            errors.Add("Dell Command | Monitor probe: " + WmiHelper.Describe(ex));
        }

        try
        {
            biosAttributesPresent = ProbeNamespace(BiosAttributesNamespace, errors);
            wmiSecurityPresent = ProbeNamespace(WmiSecurityNamespace, errors);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "native Dell namespace probe failed unexpectedly");
            errors.Add("native Dell namespace probe: " + WmiHelper.Describe(ex));
        }

        try
        {
            cctkPath = FindCctk(_logger);
            if (cctkPath is not null)
            {
                cctkVersion = RunCctk(cctkPath, VersionArgument, errors);
                cctkSecureBoot = RunCctk(cctkPath, SecureBootArgument, errors);
                cctkSecureBootEnabled = ParseCctkSecureBoot(cctkSecureBoot);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Dell Command | Configure probe failed unexpectedly");
            errors.Add("Dell Command | Configure probe: " + ex.GetType().Name + ": " + ex.Message);
        }

        _logger.Information(
            "Dell interface probe: DCM classes defined [{Classes}], responding [{Responding}] ({DcimCount} DCIM classes), cctk {Cctk}, native biosattributes {BiosAttributes}, wmisecurity {WmiSecurity}",
            string.Join(", ", classesFound),
            string.Join(", ", classesResponding),
            dcimClassCount,
            cctkPath ?? "not found",
            biosAttributesPresent,
            wmiSecurityPresent);

        return new DellInterfaceProbeResult(
            DcmClassesPresent: classesFound.Count > 0,
            DcmClassesFound: classesFound,
            Samples: samples,
            CctkFound: cctkPath is not null,
            CctkPath: cctkPath,
            CctkVersion: cctkVersion,
            CctkSecureBootOutput: cctkSecureBoot,
            NativeBiosAttributesNamespacePresent: biosAttributesPresent,
            NativeWmiSecurityNamespacePresent: wmiSecurityPresent,
            Errors: errors)
        {
            DcimClassCount = dcimClassCount,
            CctkSecureBootEnabled = cctkSecureBootEnabled,
            DcmClassesResponding = classesResponding,
        };
    }

    /// <summary>
    /// Queries each defined Dell Command | Monitor class for instances, in the order given, and
    /// returns the classes that answered with at least one instance. DCIM_BIOSEnumeration is read
    /// with <see cref="SampleQuery"/> (up to three rows, which become <paramref name="samples"/>);
    /// every other class with "SELECT * FROM class" and one row. A class that is absent, fails,
    /// or returns no instance is reported in <paramref name="errors"/>. After a query times out
    /// the remaining classes are not queried (the provider is stuck), and each is reported as
    /// skipped. Pure apart from <paramref name="query"/>; never throws.
    /// </summary>
    /// <param name="classesDefined">The DCM class names the class listing found, in probe order.</param>
    /// <param name="query">Runs one bounded query: the WQL text, a label, and the maximum number of instances.</param>
    /// <param name="samples">Receives the sample attributes from DCIM_BIOSEnumeration.</param>
    /// <param name="errors">Receives one message per class that did not answer.</param>
    /// <returns>The classes that returned at least one instance.</returns>
    internal static IReadOnlyList<string> QueryDcmClasses(
        IReadOnlyList<string> classesDefined,
        Func<string, string, int, DcmQueryOutcome> query,
        List<DellAttributeSample> samples,
        List<string> errors)
    {
        List<string> responding = new();
        string? timedOutClass = null;
        foreach (string className in classesDefined)
        {
            if (timedOutClass is not null)
            {
                errors.Add(className + ": not queried, because the query of " + timedOutClass + " timed out");
                continue;
            }

            bool isEnumeration = string.Equals(className, DcmClassNames[0], StringComparison.OrdinalIgnoreCase);
            string wql = isEnumeration ? SampleQuery : "SELECT * FROM " + className;
            DcmQueryOutcome outcome;
            try
            {
                outcome = query(wql, className, isEnumeration ? SampleCount : 1);
            }
            catch (Exception ex)
            {
                errors.Add(className + ": " + WmiHelper.Describe(ex));
                continue;
            }

            WmiQueryResult result = outcome.Result;
            if (outcome.TimedOut)
            {
                timedOutClass = className;
            }

            if (result.Error is not null)
            {
                errors.Add(result.Error);
                continue;
            }

            if (result.Absent)
            {
                errors.Add(className + ": the class is defined but could not be queried");
                continue;
            }

            if (result.Rows.Count == 0)
            {
                errors.Add(className + ": the class is defined but returned no instances");
                continue;
            }

            responding.Add(className);
            if (isEnumeration)
            {
                samples.AddRange(result.Rows.Select(ToSample));
            }
        }

        return responding;
    }

    /// <summary>
    /// Builds a sample from one DCIM_BIOSEnumeration row. CurrentValue and PossibleValues are the
    /// codes Dell Command | Monitor returns; PossibleValuesDescription holds their names index for
    /// index, and the current value's name is taken from the index where PossibleValues equals
    /// CurrentValue (several current values, which DCM allows, are named one by one and joined
    /// with ", "). Blank array elements are kept as empty strings so the two lists stay aligned.
    /// </summary>
    /// <param name="row">The row as WmiHelper.Query returns it.</param>
    /// <returns>The sample.</returns>
    internal static DellAttributeSample ToSample(IReadOnlyDictionary<string, object?> row)
    {
        string name = WmiValues.ToText(WmiHelper.Get(row, "AttributeName")) ?? "(unnamed attribute)";
        IReadOnlyList<string> currentValues = AlignedTextList(WmiHelper.Get(row, "CurrentValue"));
        IReadOnlyList<string> possibleValues = AlignedTextList(WmiHelper.Get(row, "PossibleValues"));
        IReadOnlyList<string> descriptions = AlignedTextList(WmiHelper.Get(row, "PossibleValuesDescription"));
        string? currentValue = WmiValues.ToText(WmiHelper.Get(row, "CurrentValue"));
        return new DellAttributeSample(name, currentValue, DescribeValues(currentValues, possibleValues, descriptions), possibleValues, descriptions, SampleSource);
    }

    /// <summary>
    /// Names the current value(s): for each non-blank current value, the description at the index
    /// where the possible values hold the same text (ordinal comparison, then case-insensitive).
    /// </summary>
    /// <param name="currentValues">The current value codes.</param>
    /// <param name="possibleValues">The possible value codes.</param>
    /// <param name="descriptions">The possible value names, index for index.</param>
    /// <returns>The names joined with ", ", or null when no current value could be named.</returns>
    internal static string? DescribeValues(IReadOnlyList<string> currentValues, IReadOnlyList<string> possibleValues, IReadOnlyList<string> descriptions)
    {
        List<string> names = new();
        foreach (string code in currentValues)
        {
            if (code.Length == 0)
            {
                continue;
            }

            int index = IndexOf(possibleValues, code, StringComparison.Ordinal);
            if (index < 0)
            {
                index = IndexOf(possibleValues, code, StringComparison.OrdinalIgnoreCase);
            }

            if (index < 0 || index >= descriptions.Count || descriptions[index].Length == 0)
            {
                return null;
            }

            names.Add(descriptions[index]);
        }

        return names.Count == 0 ? null : string.Join(", ", names);
    }

    /// <summary>
    /// Parses the Secure Boot line of Dell Command | Configure output. The tool prints a line of
    /// the form "secureboot=enable" or "secureboot=disable" (case-insensitive; "enabled", "on",
    /// "disabled" and "off" are accepted too). Any other text yields null.
    /// </summary>
    /// <param name="output">The captured output, possibly several lines.</param>
    /// <returns>True when enabled, false when disabled, null when the line is missing or unrecognised.</returns>
    internal static bool? ParseCctkSecureBoot(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        foreach (string rawLine in output.Split('\n'))
        {
            string line = rawLine.Trim();
            int separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            string key = line[..separator].Trim();
            if (!string.Equals(key, "secureboot", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string value = line[(separator + 1)..].Trim().ToLowerInvariant();
            switch (value)
            {
                case "enable":
                case "enabled":
                case "on":
                case "1":
                    return true;
                case "disable":
                case "disabled":
                case "off":
                case "0":
                    return false;
                default:
                    return null;
            }
        }

        return null;
    }

    /// <summary>
    /// The cctk.exe locations to check: the Program Files (x86) folder, then the Program Files
    /// folder, each with Dell\Command Configure\X86_64\cctk.exe appended. Both folders come from
    /// <see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/>, which asks Windows for
    /// the known folder and never reads the %ProgramFiles(x86)% or %ProgramFiles% environment
    /// variables: the environment is set by whoever starts the process (per-user variables reach
    /// an elevated process of the same account too), so it must not decide which program an
    /// elevated run starts. See <see cref="CctkCandidatePaths(IEnumerable{string?})"/> for the
    /// folders that are skipped.
    /// </summary>
    /// <returns>The candidate paths in probe order; every one is a full path.</returns>
    internal static IReadOnlyList<string> CctkCandidatePaths()
    {
        return CctkCandidatePaths(new[]
        {
            ReadSpecialFolder(Environment.SpecialFolder.ProgramFilesX86),
            ReadSpecialFolder(Environment.SpecialFolder.ProgramFiles),
        });
    }

    /// <summary>
    /// Testable core of <see cref="CctkCandidatePaths()"/>: the cctk.exe path under each Program
    /// Files folder, in the order given. A folder that is null, blank or not a full path (a relative
    /// path, "C:folder" or "\folder", which Windows would resolve against the current directory or
    /// drive) is skipped, and so is a path already listed (compared case-insensitively). Never throws.
    /// </summary>
    /// <param name="programFilesRoots">The Program Files folders, in probe order.</param>
    /// <returns>The candidate paths in probe order; every one is a full path.</returns>
    internal static IReadOnlyList<string> CctkCandidatePaths(IEnumerable<string?> programFilesRoots)
    {
        List<string> candidates = new();
        foreach (string? root in programFilesRoots)
        {
            AddCandidate(candidates, root);
        }

        return candidates;
    }

    /// <summary>Builds the cctk.exe path under a Program Files root.</summary>
    /// <param name="programFilesRoot">A Program Files directory.</param>
    /// <returns>The full path of cctk.exe under Dell\Command Configure\X86_64.</returns>
    internal static string CctkPathUnder(string programFilesRoot)
    {
        return Path.Combine(programFilesRoot, "Dell", "Command Configure", "X86_64", "cctk.exe");
    }

    private static DellInterfaceProbeResult Empty(IReadOnlyList<string> errors)
    {
        return new DellInterfaceProbeResult(
            DcmClassesPresent: false,
            DcmClassesFound: Array.Empty<string>(),
            Samples: Array.Empty<DellAttributeSample>(),
            CctkFound: false,
            CctkPath: null,
            CctkVersion: null,
            CctkSecureBootOutput: null,
            NativeBiosAttributesNamespacePresent: false,
            NativeWmiSecurityNamespacePresent: false,
            Errors: errors);
    }

    /// <summary>
    /// The elements of a WMI array value as text, index for index: a blank or null element is kept
    /// as an empty string, so the codes and the names of a DCIM_BIOSEnumeration row stay aligned.
    /// A single value becomes a list of one; null becomes an empty list.
    /// </summary>
    /// <param name="value">The property value as WmiHelper.Query returns it.</param>
    /// <returns>The texts; never null.</returns>
    internal static IReadOnlyList<string> AlignedTextList(object? value)
    {
        if (value is null)
        {
            return Array.Empty<string>();
        }

        if (value is Array array && value is not string)
        {
            return array.Cast<object?>().Select(element => WmiValues.ToText(element) ?? string.Empty).ToArray();
        }

        string? text = WmiValues.ToText(value);
        return text is null ? Array.Empty<string>() : new[] { text };
    }

    private static int IndexOf(IReadOnlyList<string> values, string value, StringComparison comparison)
    {
        for (int i = 0; i < values.Count; i++)
        {
            if (string.Equals(values[i], value, comparison))
            {
                return i;
            }
        }

        return -1;
    }

    private void ProbeDcmClasses(List<string> classesFound, List<string> classesResponding, List<DellAttributeSample> samples, ref int dcimClassCount, List<string> errors)
    {
        bool? namespaceExists = WmiHelper.NamespaceExists(SysmanNamespace, _logger, out string? namespaceError);
        if (namespaceError is not null)
        {
            errors.Add(namespaceError);
        }

        if (namespaceExists != true)
        {
            _logger.Information("Namespace {Namespace} not present: Dell Command | Monitor is not installed or not responding", SysmanNamespace);
            return;
        }

        IReadOnlyList<string> classNames = WmiHelper.ListClassNames(SysmanNamespace, _logger, out string? listError);
        if (listError is not null)
        {
            errors.Add(listError);
        }

        HashSet<string> names = new(classNames, StringComparer.OrdinalIgnoreCase);
        dcimClassCount = classNames.Count(name => name.StartsWith("DCIM_", StringComparison.OrdinalIgnoreCase));
        foreach (string className in DcmClassNames)
        {
            if (names.Contains(className))
            {
                classesFound.Add(className);
            }
        }

        classesResponding.AddRange(QueryDcmClasses(classesFound, BoundedQuery, samples, errors));
        foreach (DellAttributeSample sample in samples)
        {
            _logger.Debug(
                "DCM sample {Name} = {Value} ({Description}); possible: {Possible} ({Descriptions})",
                sample.Name,
                sample.CurrentValue,
                sample.CurrentValueDescription ?? "no name",
                string.Join(", ", sample.PossibleValues),
                string.Join(", ", sample.PossibleValuesDescription));
        }
    }

    /// <summary>
    /// Runs one instance query in root\dcim\sysman under <see cref="DcmQueryTimeout"/>. A query
    /// that does not finish in time is reported as an error and left to finish on its own.
    /// </summary>
    private DcmQueryOutcome BoundedQuery(string wql, string label, int maxInstances)
    {
        WmiQueryResult? result = null;
        try
        {
            WmiHelper.RunBounded(() => result = WmiHelper.Query(SysmanNamespace, wql, label, _logger, maxInstances), DcmQueryTimeout, "query of " + label);
        }
        catch (TimeoutException ex)
        {
            _logger.Warning("WMI {Namespace}: {Query} timed out: {Detail}", SysmanNamespace, wql, ex.Message);
            return new DcmQueryOutcome(new WmiQueryResult(Array.Empty<IReadOnlyDictionary<string, object?>>(), label + ": " + ex.Message, false), true);
        }

        return new DcmQueryOutcome(result ?? new WmiQueryResult(Array.Empty<IReadOnlyDictionary<string, object?>>(), label + ": the query returned no result", false), false);
    }

    private bool ProbeNamespace(string namespacePath, List<string> errors)
    {
        bool? exists = WmiHelper.NamespaceExists(namespacePath, _logger, out string? error);
        if (error is not null)
        {
            errors.Add(error);
        }

        return exists == true;
    }

    /// <summary>
    /// The first of <see cref="CctkCandidatePaths()"/> that exists, checked in order: only the two
    /// Program Files known folders, never a folder an environment variable names. Shared by the
    /// probe and the Dell settings reader (Settings/Dell/CctkReader.cs), which runs no other
    /// program. Each candidate is logged at Debug. Never throws.
    /// </summary>
    /// <param name="logger">Logger for the candidates checked.</param>
    /// <returns>The full path of cctk.exe, or null when neither folder holds it.</returns>
    internal static string? FindCctk(ILogger logger)
    {
        foreach (string candidate in CctkCandidatePaths())
        {
            bool exists;
            try
            {
                exists = File.Exists(candidate);
            }
            catch (Exception)
            {
                exists = false;
            }

            logger.Debug("cctk candidate {Path}: {Exists}", candidate, exists ? "present" : "absent");
            if (exists)
            {
                return candidate;
            }
        }

        return null;
    }

    private string? RunCctk(string cctkPath, string argument, List<string> errors)
    {
        string? workingDirectory = null;
        try
        {
            workingDirectory = Path.GetDirectoryName(cctkPath);
        }
        catch (Exception)
        {
            // Run from the current directory instead.
        }

        ProcessRunResult run = ProcessRunner.Run(cctkPath, workingDirectory, CctkTimeout, _logger, argument);
        string label = "cctk " + argument;
        if (run.Error is not null)
        {
            errors.Add(label + ": could not run: " + run.Error);
            return null;
        }

        if (run.TimedOut)
        {
            errors.Add(label + ": timed out after " + CctkTimeout.TotalSeconds.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + " seconds");
            return CombineOutput(run);
        }

        if (run.ExitCode is int code && code != 0)
        {
            errors.Add(label + ": exited with code " + code.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return CombineOutput(run);
    }

    private static string? CombineOutput(ProcessRunResult run)
    {
        if (run.StandardOutput.Length == 0 && run.StandardError.Length == 0)
        {
            return null;
        }

        if (run.StandardError.Length == 0)
        {
            return run.StandardOutput;
        }

        if (run.StandardOutput.Length == 0)
        {
            return "stderr: " + run.StandardError;
        }

        return run.StandardOutput + Environment.NewLine + "stderr: " + run.StandardError;
    }

    private static void AddCandidate(List<string> candidates, string? programFilesRoot)
    {
        if (string.IsNullOrWhiteSpace(programFilesRoot))
        {
            return;
        }

        string path;
        try
        {
            path = CctkPathUnder(programFilesRoot.Trim());
            if (!Path.IsPathFullyQualified(path))
            {
                return;
            }
        }
        catch (Exception)
        {
            return;
        }

        if (!candidates.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            candidates.Add(path);
        }
    }

    private static string? ReadSpecialFolder(Environment.SpecialFolder folder)
    {
        try
        {
            return Environment.GetFolderPath(folder);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
