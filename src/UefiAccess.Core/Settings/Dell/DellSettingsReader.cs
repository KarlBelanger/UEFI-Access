// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Globalization;
using Serilog;
using UefiAccess.Core.Platform;

namespace UefiAccess.Core.Settings.Dell;

/// <summary>
/// Reads the agreed Dell BIOS settings once:
/// the machine first (R1), which ends the read on a computer whose manufacturer is known and is
/// not Dell; then Dell Command | Monitor (R2, R3) and, when the preference allows it and Dell
/// Command | Monitor gives no answer, Dell Command | Configure (R5), the fallback; and, when
/// nothing answered because Dell Command | Monitor is not installed, whether the built-in Dell
/// BIOS interface of newer models is present (R4), which this version detects and never reads.
/// Every step shares one read budget of <see cref="ReadBudget"/>, and a step is skipped when
/// nothing is left of it. The settings of a result all come from the one interface that
/// answered, one per catalog entry in catalog order; the sources are never mixed. The reader
/// keeps nothing between reads: the session cache is the window's, and the command line reads
/// once per run. What must not happen twice in a process after a timeout (a second query of a
/// hung Dell Command | Monitor, a run after a timed-out Dell Command | Configure run) is kept by
/// the providers. Only the agreed rows are logged, never another attribute's value. It reads
/// only and never throws: every failure is a problem of the result.
/// </summary>
public sealed class DellSettingsReader : IDellSettingsReader
{
    /// <summary>The time one read may take, its steps together (R1 to R5): 120 seconds. A timed-out Dell Command | Configure run can add the few seconds its kill takes.</summary>
    public static readonly TimeSpan ReadBudget = TimeSpan.FromSeconds(120);

    /// <summary>The detail of the problem off Windows (DF1).</summary>
    private const string NotWindowsDetail = "the Dell settings are read through WMI and Windows programs, which are only available on Windows";

    /// <summary>The detail of the problem when a step was skipped because nothing was left of the budget (DF21); {0} the step.</summary>
    private const string BudgetDetailFormat = "nothing was left of the read budget for {0}";

    /// <summary>The detail of the problem when the preference is no member of <see cref="DellSourcePreference"/>; {0} its number.</summary>
    private const string UnknownPreferenceDetailFormat = "the source preference {0} is not one this version knows, so no interface was tried";

    /// <summary>The detail of the problem when an interface returned no read at all, after its name.</summary>
    private const string NoResultDetail = "returned no result";

    /// <summary>The detail of the problem when only the built-in interface of newer models is present (DF22).</summary>
    private const string NativeDetail = "the namespace " + DellInterfaceProbe.BiosAttributesNamespace + " exists; this version does not read it";

    /// <summary>The step names of <see cref="BudgetDetailFormat"/>.</summary>
    private const string MachineStep = "the machine";

    private const string NativeStep = "the check of the built-in Dell BIOS interface";

    /// <summary>The source named in the summary line when no interface answered.</summary>
    private const string NoSource = "no interface";

    /// <summary>The BIOS version named in the summary line when it could not be read.</summary>
    private const string UnknownBios = "not known";

    private static readonly DellMachine UnknownMachine = new(null, null, null);

    private readonly ILogger _logger;
    private readonly IDellSettingsProvider _dcm;
    private readonly IDellSettingsProvider _cctk;
    private readonly Func<DellReadBudget, DellMachine> _machine;
    private readonly Func<DellReadBudget, bool?> _nativeNamespacePresent;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<TimeSpan> _elapsed;
    private readonly Func<bool> _isWindows;

    /// <summary>
    /// Composes the reader over this computer: the machine reader, Dell Command | Monitor's
    /// provider, Dell Command | Configure's provider (which looks for the program once, now, under
    /// the two Program Files known folders) and the bounded check of the native namespace. Nothing
    /// is read and nothing is run here.
    /// </summary>
    /// <param name="logger">Optional Serilog logger for every query, run and outcome; null disables logging.</param>
    public DellSettingsReader(ILogger? logger)
    {
        _logger = logger ?? Serilog.Core.Logger.None;
        ILogger log = _logger;
        DellMachineReader machine = new(log);
        _dcm = new DcmSettingsProvider(log);
        _cctk = new CctkSettingsProvider(log);
        _machine = machine.Read;
        _nativeNamespacePresent = budget => NativeNamespaceExists(budget, log);
        _clock = () => DateTimeOffset.Now;
        long origin = Stopwatch.GetTimestamp();
        _elapsed = () => Stopwatch.GetElapsedTime(origin);
        _isWindows = OperatingSystem.IsWindows;
    }

    /// <summary>The seam for tests: every interface, the clock, the elapsed time and the platform are the caller's; nothing here touches the machine.</summary>
    /// <param name="logger">Logger for the outcome.</param>
    /// <param name="dcm">Dell Command | Monitor's provider.</param>
    /// <param name="cctk">Dell Command | Configure's provider.</param>
    /// <param name="machine">Reads the machine within the budget it is given.</param>
    /// <param name="nativeNamespacePresent">Checks within the budget it is given whether the native namespace exists: true, false, or null when that could not be told.</param>
    /// <param name="clock">The time a read starts.</param>
    /// <param name="elapsed">A running time that never goes back (a stopwatch); a read measures its budget and its length from the value it finds at its start.</param>
    /// <param name="isWindows">True on Windows.</param>
    internal DellSettingsReader(
        ILogger logger,
        IDellSettingsProvider dcm,
        IDellSettingsProvider cctk,
        Func<DellReadBudget, DellMachine> machine,
        Func<DellReadBudget, bool?> nativeNamespacePresent,
        Func<DateTimeOffset> clock,
        Func<TimeSpan> elapsed,
        Func<bool> isWindows)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(dcm);
        ArgumentNullException.ThrowIfNull(cctk);
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(nativeNamespacePresent);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(elapsed);
        ArgumentNullException.ThrowIfNull(isWindows);
        _logger = logger;
        _dcm = dcm;
        _cctk = cctk;
        _machine = machine;
        _nativeNamespacePresent = nativeNamespacePresent;
        _clock = clock;
        _elapsed = elapsed;
        _isWindows = isWindows;
    }

    /// <summary>
    /// Reads the agreed settings once. Off Windows nothing is read
    /// (DF1). A known manufacturer that does not start with "Dell" ends the read before either
    /// interface (DF2). The preference decides the interfaces: Dell Command | Monitor only, Dell
    /// Command | Configure only, or Automatic, Dell Command | Monitor and, when it gives no answer
    /// and the budget is not used up, Dell Command | Configure, whose answer then carries the
    /// fallback sentence; a value of no member tries neither. When nothing answered and Dell
    /// Command | Monitor is not installed, the native namespace is checked (DF22). A usable result
    /// has the dependency rule of the catalog applied, and a BIOS other than the verified one adds
    /// a warning (DF23). Never throws: an interface that throws or returns nothing is a problem,
    /// and so is anything else unexpected (DF24).
    /// </summary>
    /// <param name="options">Which interface to use and whether to keep the full enumeration.</param>
    /// <returns>The result; its settings are always one per catalog entry, in catalog order.</returns>
    public DellSettingsResult Read(DellReadOptions options)
    {
        IReadOnlyList<DellCatalogEntry> catalog = DellAttributeCatalog.Default;
        List<DellInterfaceProblem> problems = new();
        DellMachine machine = UnknownMachine;
        DateTimeOffset readAt = default;
        TimeSpan? start = null;
        try
        {
            readAt = _clock();
            start = _elapsed();
            TimeSpan origin = start.Value;
            DellReadBudget budget = new(ReadBudget, () => _elapsed() - origin);
            ArgumentNullException.ThrowIfNull(options);

            if (!_isWindows())
            {
                problems.Add(new DellInterfaceProblem(DellInterface.Reader, DellProblemKind.NotWindows, NotWindowsDetail));
                return Finish(Unavailable(catalog, readAt, origin, machine, problems));
            }

            if (budget.Exhausted)
            {
                AddBudgetProblem(problems, MachineStep);
            }
            else
            {
                machine = _machine(budget) ?? UnknownMachine;
            }

            if (OtherManufacturer(machine.Manufacturer) is string manufacturer)
            {
                problems.Add(new DellInterfaceProblem(DellInterface.Machine, DellProblemKind.NotDell, manufacturer));
                return Finish(Unavailable(catalog, readAt, origin, machine, problems));
            }

            // Each interface only for the preferences that name it, so a value of no member tries
            // neither, and never the one that starts a process.
            bool tryDcm = options.Preference is DellSourcePreference.Automatic or DellSourcePreference.DellCommandMonitor;
            bool tryCctk = options.Preference is DellSourcePreference.Automatic or DellSourcePreference.DellCommandConfigure;
            if (!tryDcm && !tryCctk)
            {
                problems.Add(new DellInterfaceProblem(DellInterface.Reader, DellProblemKind.Unexpected, string.Format(CultureInfo.InvariantCulture, UnknownPreferenceDetailFormat, (int)options.Preference)));
            }

            DellProviderRead? answered = null;
            IDellSettingsProvider? answeredBy = null;
            List<DellInterfaceProblem> dcmProblems = new();
            string? fallback = null;

            if (tryDcm)
            {
                int before = problems.Count;
                DellProviderRead? read = ReadThrough(_dcm, catalog, budget, options.IncludeAllAttributes, problems);
                dcmProblems.AddRange(problems.Skip(before));
                if (read is { Answered: true })
                {
                    (answered, answeredBy) = (read, _dcm);
                }
            }

            if (answered is null && tryCctk)
            {
                DellProviderRead? read = ReadThrough(_cctk, catalog, budget, options.IncludeAllAttributes, problems);
                if (read is { Answered: true })
                {
                    (answered, answeredBy) = (read, _cctk);
                    if (tryDcm)
                    {
                        fallback = FallbackSentence(dcmProblems);
                    }
                }
            }

            if (answered is null || answeredBy is null)
            {
                bool native = false;
                if (dcmProblems.Any(problem => problem.Interface == DellInterface.DellCommandMonitor && problem.Kind == DellProblemKind.NotInstalled))
                {
                    native = CheckNativeNamespace(budget, problems);
                }

                return Finish(Unavailable(catalog, readAt, origin, machine, problems) with { NativeInterfacePresent = native });
            }

            IReadOnlyList<FirmwareSetting> settings = ApplyDependencies(catalog, InCatalogOrder(catalog, answered.Settings));
            DellReadOutcome outcome = settings.All(setting => setting.State is SettingState.Read or SettingState.NotApplicable) ? DellReadOutcome.Complete : DellReadOutcome.Partial;
            List<string> warnings = new();
            if (OtherBios(machine.BiosVersion) is string bios)
            {
                warnings.Add(string.Format(CultureInfo.InvariantCulture, DellSentences.OtherBiosFormat, DellAttributeCatalog.VerifiedBiosVersion, bios));
            }

            DellSettingsResult result = new(outcome, answeredBy.Source, readAt, ElapsedMs(origin), machine, settings, problems.ToArray(), warnings.ToArray())
            {
                FallbackSentence = fallback,
                AllAttributes = options.IncludeAllAttributes ? answered.AllAttributes : null,
            };
            return Finish(result);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Dell settings read failed unexpectedly");
            problems.Add(new DellInterfaceProblem(DellInterface.Reader, DellProblemKind.Unexpected, WmiHelper.Describe(ex)));
            return Finish(Unavailable(catalog, readAt, start, machine, problems));
        }
    }

    /// <summary>
    /// The manufacturer when it is known and does not start with "Dell", compared ordinally and
    /// case-insensitively (DF2); null when it is unknown or Dell's, so an unread manufacturer
    /// never stops the read.
    /// </summary>
    /// <param name="manufacturer">The manufacturer as read, or null.</param>
    /// <returns>The trimmed manufacturer, or null.</returns>
    internal static string? OtherManufacturer(string? manufacturer)
    {
        if (string.IsNullOrWhiteSpace(manufacturer))
        {
            return null;
        }

        string trimmed = manufacturer.Trim();
        return trimmed.StartsWith("Dell", StringComparison.OrdinalIgnoreCase) ? null : trimmed;
    }

    /// <summary>The BIOS version when it is known and is not <see cref="DellAttributeCatalog.VerifiedBiosVersion"/>, compared ordinally (DF23); null otherwise.</summary>
    /// <param name="biosVersion">The BIOS version as read, or null.</param>
    /// <returns>The trimmed version, or null.</returns>
    internal static string? OtherBios(string? biosVersion)
    {
        if (string.IsNullOrWhiteSpace(biosVersion))
        {
            return null;
        }

        string trimmed = biosVersion.Trim();
        return string.Equals(trimmed, DellAttributeCatalog.VerifiedBiosVersion, StringComparison.Ordinal) ? null : trimmed;
    }

    /// <summary>
    /// The settings of the catalog's entries in catalog order, one each (pure): an entry's setting
    /// is the first one the interface returned with its id, compared ordinally; an entry the
    /// interface returned none for is not read; a setting of no catalog entry is dropped.
    /// </summary>
    /// <param name="catalog">The agreed settings.</param>
    /// <param name="settings">The settings the interface returned.</param>
    /// <returns>One setting per entry, in catalog order.</returns>
    internal static IReadOnlyList<FirmwareSetting> InCatalogOrder(IReadOnlyList<DellCatalogEntry> catalog, IReadOnlyList<FirmwareSetting>? settings)
    {
        IReadOnlyList<FirmwareSetting> returned = settings ?? Array.Empty<FirmwareSetting>();
        return catalog
            .Select(entry => returned.FirstOrDefault(setting => setting is not null && string.Equals(setting.Id, entry.Id, StringComparison.Ordinal)) ?? DellSettingMapper.NotRead(entry, null))
            .ToArray();
    }

    /// <summary>
    /// The dependency rule of the catalog: an entry that depends
    /// on another is shown only while that one reads the entry's text, because the BIOS setup
    /// hides it otherwise. While the setting it depends on reads, by the catalog's words, the
    /// other value (TPM security Disabled), a setting with a value read (TPM activation) becomes
    /// <see cref="SettingState.NotApplicable"/>, its value kept for the JSON. A setting it depends
    /// on that was not read, or reads a word of Dell's own, decides nothing, so the row never says
    /// "not applicable while TPM security is Disabled" unless TPM security reads Disabled.
    /// </summary>
    /// <param name="catalog">The agreed settings.</param>
    /// <param name="settings">One setting per entry, in catalog order.</param>
    /// <returns>The settings with the rule applied.</returns>
    internal static IReadOnlyList<FirmwareSetting> ApplyDependencies(IReadOnlyList<DellCatalogEntry> catalog, IReadOnlyList<FirmwareSetting> settings)
    {
        FirmwareSetting[] result = settings.ToArray();
        for (int i = 0; i < result.Length && i < catalog.Count; i++)
        {
            DellCatalogEntry entry = catalog[i];
            if (entry.DependsOnId is not string dependsOnId || entry.DependsOnText is not string shownWhile)
            {
                continue;
            }

            int index = IndexOf(catalog, dependsOnId);
            DellCatalogEntry? dependsOn = index < 0 ? null : catalog[index];
            FirmwareSetting? governing = index < 0 || index >= result.Length ? null : result[index];
            if (dependsOn is null || governing is not { State: SettingState.Read, DisplayFromVendor: false, Current: { } current })
            {
                continue;
            }

            string[] hiding = dependsOn.Values.Select(value => value.Text).Where(text => !string.Equals(text, shownWhile, StringComparison.Ordinal)).ToArray();
            bool hidden = hiding.Length == 1 && string.Equals(current.Text, hiding[0], StringComparison.Ordinal);
            if (hidden && result[i] is { State: SettingState.Read or SettingState.Unnamed, Current: not null })
            {
                result[i] = result[i] with { State = SettingState.NotApplicable };
            }
        }

        return result;
    }

    /// <summary>R4: whether the native namespace exists, within the budget; null when that could not be told in time.</summary>
    private static bool? NativeNamespaceExists(DellReadBudget budget, ILogger logger)
    {
        try
        {
            return WmiHelper.RunBounded(
                () => WmiHelper.NamespaceExists(DellInterfaceProbe.BiosAttributesNamespace, logger, out _),
                budget.Bound(DcmSettingsProvider.StepTimeout),
                "check of namespace " + DellInterfaceProbe.BiosAttributesNamespace);
        }
        catch (TimeoutException ex)
        {
            logger.Warning("WMI namespace {Namespace}: check timed out: {Detail}", DellInterfaceProbe.BiosAttributesNamespace, ex.Message);
            return null;
        }
    }

    /// <summary>The fallback sentence: the cause of Dell Command | Monitor's first problem, then the fallback clause.</summary>
    private static string FallbackSentence(IReadOnlyList<DellInterfaceProblem> dcmProblems)
    {
        string cause = dcmProblems.Count == 0 ? string.Empty : DellSentences.Cause(dcmProblems[0]);
        return string.Format(CultureInfo.InvariantCulture, DellSentences.FallbackFormat, cause).Trim();
    }

    /// <summary>Adds the reader's budget problem, once per read, naming the first step it skipped (DF21).</summary>
    private static void AddBudgetProblem(List<DellInterfaceProblem> problems, string step)
    {
        if (!problems.Any(problem => problem.Interface == DellInterface.Reader && problem.Kind == DellProblemKind.BudgetExhausted))
        {
            problems.Add(new DellInterfaceProblem(DellInterface.Reader, DellProblemKind.BudgetExhausted, string.Format(CultureInfo.InvariantCulture, BudgetDetailFormat, step)));
        }
    }

    private static int IndexOf(IReadOnlyList<DellCatalogEntry> catalog, string id)
    {
        for (int i = 0; i < catalog.Count; i++)
        {
            if (string.Equals(catalog[i].Id, id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// One interface's read: skipped, with the reader's budget problem, when nothing is left of the
    /// budget; otherwise its read, whose problems join the result's. An interface that throws, or
    /// returns no read at all, gave no answer, and that is the reader's unexpected problem (DF24),
    /// so a failure never goes without a cause. Null when it gave none.
    /// </summary>
    private DellProviderRead? ReadThrough(IDellSettingsProvider provider, IReadOnlyList<DellCatalogEntry> catalog, DellReadBudget budget, bool includeAllAttributes, List<DellInterfaceProblem> problems)
    {
        DellSettingsSource source = provider.Source;
        if (budget.Exhausted)
        {
            AddBudgetProblem(problems, DellSentences.SourceName(source));
            return null;
        }

        try
        {
            DellProviderRead? read = provider.Read(catalog, budget, includeAllAttributes);
            if (read is null)
            {
                _logger.Warning("Dell settings: {Source} returned no result", DellSentences.SourceName(source));
                problems.Add(new DellInterfaceProblem(DellInterface.Reader, DellProblemKind.Unexpected, DellSentences.SourceName(source) + ": " + NoResultDetail));
                return null;
            }

            if (read.Problems is { } found)
            {
                problems.AddRange(found.Where(problem => problem is not null));
            }

            return read;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Dell settings: {Source} failed unexpectedly", DellSentences.SourceName(source));
            problems.Add(new DellInterfaceProblem(DellInterface.Reader, DellProblemKind.Unexpected, DellSentences.SourceName(source) + ": " + WmiHelper.Describe(ex)));
            return null;
        }
    }

    /// <summary>R4, when nothing is left of the budget skipped with the reader's budget problem: true, with the native interface's problem, when the namespace exists.</summary>
    private bool CheckNativeNamespace(DellReadBudget budget, List<DellInterfaceProblem> problems)
    {
        if (budget.Exhausted)
        {
            AddBudgetProblem(problems, NativeStep);
            return false;
        }

        bool? present = _nativeNamespacePresent(budget);
        _logger.Debug("Built-in Dell BIOS interface of newer models: {Present}", present switch { true => "present", false => "absent", null => "could not be told" });
        if (present != true)
        {
            return false;
        }

        problems.Add(new DellInterfaceProblem(DellInterface.NativeBiosInterface, DellProblemKind.NativeInterfaceOnly, NativeDetail));
        return true;
    }

    /// <summary>A result no interface answered: every setting not read, no source, no warning.</summary>
    private DellSettingsResult Unavailable(IReadOnlyList<DellCatalogEntry> catalog, DateTimeOffset readAt, TimeSpan? start, DellMachine machine, List<DellInterfaceProblem> problems)
    {
        return new DellSettingsResult(
            DellReadOutcome.Unavailable,
            null,
            readAt,
            ElapsedMs(start),
            machine,
            catalog.Select(entry => DellSettingMapper.NotRead(entry, null)).ToArray(),
            problems.ToArray(),
            Array.Empty<string>());
    }

    /// <summary>The milliseconds since <paramref name="start"/>, never below zero; 0 when the start is unknown or the time cannot be read.</summary>
    private long ElapsedMs(TimeSpan? start)
    {
        if (start is not TimeSpan origin)
        {
            return 0;
        }

        try
        {
            return Math.Max(0, (long)Math.Round((_elapsed() - origin).TotalMilliseconds));
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>
    /// Logs a result and returns it: at Debug one line per agreed row,
    /// with its token, Dell's name, its text and its state, and the count of the other attributes
    /// kept for the full enumeration, never their values; at Warning one line per problem; and at
    /// Information one summary line. Logging never fails a read.
    /// </summary>
    private DellSettingsResult Finish(DellSettingsResult result)
    {
        try
        {
            foreach (FirmwareSetting setting in result.Settings)
            {
                _logger.Debug(
                    "Dell setting {Id} ({Name}) = {Token} ({VendorName}) -> {Text} [{State}]",
                    setting.Id,
                    setting.Name,
                    setting.Current?.Token ?? "none",
                    setting.Current?.VendorName ?? "no name",
                    DellSentences.ValueText(setting),
                    setting.State.ToString());
            }

            if (result.AllAttributes is { } all)
            {
                _logger.Debug("Dell settings: {Count} attribute(s) kept for the full enumeration; only the agreed ones are logged", all.Count);
            }

            foreach (DellInterfaceProblem problem in result.Problems)
            {
                _logger.Warning("Dell settings problem: {Interface} {Kind}: {Detail}", problem.Interface.ToString(), problem.Kind.ToString(), problem.Detail);
            }

            _logger.Information(
                "Dell settings: {Outcome} from {Source} in {ElapsedMs} ms; {Usable} of {Agreed} read; {Problems} problem(s); BIOS {Bios}",
                result.Outcome.ToString(),
                result.Source is DellSettingsSource source ? DellSentences.SourceName(source) : NoSource,
                result.ElapsedMs,
                result.Settings.Count(setting => setting.State is SettingState.Read or SettingState.NotApplicable),
                result.Settings.Count,
                result.Problems.Count,
                result.Machine.BiosVersion ?? UnknownBios);
        }
        catch (Exception)
        {
            // Logging is best effort; the result stands.
        }

        return result;
    }
}
