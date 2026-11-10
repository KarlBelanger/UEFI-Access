// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using Serilog;

namespace UefiAccess.Core.Settings.Dell;

/// <summary>
/// Reads the agreed Dell BIOS settings through Dell Command | Configure, the fallback of plan task
/// 4.3: one bounded run per catalog entry that has a read
/// option, in catalog order and one at a time, each through <see cref="CctkReader"/>, the only
/// launcher, which passes the option alone. The program prints only the current value, as one line
/// of the form Name=Value in the words of version 5.2.2 or in the older words; the parser accepts
/// both, case-insensitively, and the possible values shown are the catalog's. Boot list option is
/// never read this way. The exit code decides what comes next (DF14 to DF21): administrator rights
/// missing, no BIOS interface, a timeout, a run that did not start and every exit code a read does
/// not expect stop the read, because the program's own table gives several codes the meaning that
/// a change was attempted or failed; an option this computer does not offer makes only its own
/// row not reported. A run starts only while one whole run timeout is left of the read budget, so
/// none is started only to be killed. The kill of a timed-out run can fail, so after one, this
/// process starts no further run for the rest of its life.
/// </summary>
internal sealed class CctkSettingsProvider : IDellSettingsProvider
{
    /// <summary>
    /// The longest one run may take: 15 seconds, the bound the M1 probe gives its own two runs.
    /// <see cref="CctkReader.RunCctk"/> never waits longer, whatever bound it is handed, and a run
    /// starts only while this much of the read budget is left.
    /// </summary>
    internal static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(15);

    /// <summary>The exit code with which the program asks for administrator rights (DF15).</summary>
    private const int NeedsAdministratorExitCode = 95;

    /// <summary>The most lines of one run, other than its value line, that are logged.</summary>
    private const int MostLoggedLines = 10;

    /// <summary>The most characters of one logged line.</summary>
    private const int MostLoggedCharacters = 200;

    /// <summary>The detail of the problem when the program was not found.</summary>
    private const string NotFoundDetail = "cctk.exe was not found in the Program Files folders";

    /// <summary>The detail of the problem when an earlier run of this process timed out.</summary>
    private const string EarlierTimeoutDetail = "an earlier Dell Command Configure run timed out; restart the application";

    /// <summary>The detail of the problem when less than one run timeout was left of the read budget.</summary>
    private const string BudgetDetail = "less than one run timeout was left of the read budget";

    /// <summary>The exit codes with which the program says it found no Dell BIOS interface (DF16).</summary>
    private static readonly int[] NoBiosInterfaceExitCodes = { 12, 140 };

    /// <summary>The exit codes with which the program says this computer does not offer an option (DF17).</summary>
    private static readonly int[] OptionNotSupportedExitCodes = { 118, 42, 96 };

    /// <summary>
    /// 1 once a run of this process has timed out, and never 0 again: the kill of its process tree
    /// can fail, and ProcessRunner then only logs it, so a later run could find it still running.
    /// </summary>
    private static int s_runTimedOut;

    private readonly ILogger _logger;
    private readonly Func<bool> _found;
    private readonly Func<CctkReadOption, TimeSpan, CctkRunOutcome> _run;

    /// <summary>
    /// Composes the provider over this computer's Dell Command | Configure: one
    /// <see cref="CctkReader"/>, which looks for the program once, now, under the two Program Files
    /// known folders. Nothing is run here.
    /// </summary>
    /// <param name="logger">Logger for the search, every run and the outcome.</param>
    public CctkSettingsProvider(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        CctkReader reader = new(logger);
        _logger = logger;
        _found = () => reader.Found;
        _run = (option, bound) => reader.RunCctk(option, bound);
    }

    /// <summary>The seam for tests: whether the program was found and every run are the caller's; nothing here starts a process.</summary>
    /// <param name="logger">Logger for every run and the outcome.</param>
    /// <param name="found">True when the program was found.</param>
    /// <param name="run">Runs one read option and waits at most the bound it is given.</param>
    internal CctkSettingsProvider(ILogger logger, Func<bool> found, Func<CctkReadOption, TimeSpan, CctkRunOutcome> run)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(found);
        ArgumentNullException.ThrowIfNull(run);
        _logger = logger;
        _found = found;
        _run = run;
    }

    /// <inheritdoc />
    public DellSettingsSource Source => DellSettingsSource.DellCommandConfigure;

    /// <summary>True once a run of this process has timed out; no run starts after that (for tests).</summary>
    internal static bool EarlierRunTimedOut => Volatile.Read(ref s_runTimedOut) != 0;

    /// <summary>
    /// Reads the catalog's settings once. Without the program nothing runs
    /// (<see cref="DellProblemKind.ProgramNotFound"/>), and after a timed-out run of this process
    /// nothing runs either (<see cref="DellProblemKind.TimedOut"/>); both leave every setting not
    /// read. Otherwise each entry in catalog order: one without a read option is not reported, with
    /// <see cref="DellSentences.NotReadByFallbackNote"/>; when less than <see cref="RunTimeout"/> is
    /// left of the budget, it and the rest are not read (<see cref="DellProblemKind.BudgetExhausted"/>,
    /// the reader's problem, DF21) and no run starts; otherwise one run, asked for
    /// <see cref="RunTimeout"/>, whose row <see cref="ParseRow"/> makes and whose outcome
    /// <see cref="StopKind"/> may end the read, leaving the rows not yet run not read. Answered
    /// means at least one row was read. Never throws.
    /// </summary>
    /// <param name="catalog">The agreed settings.</param>
    /// <param name="budget">The time left of the whole read.</param>
    /// <param name="includeAllAttributes">Ignored: the program lists no enumeration.</param>
    /// <returns>The read.</returns>
    public DellProviderRead Read(IReadOnlyList<DellCatalogEntry> catalog, DellReadBudget budget, bool includeAllAttributes)
    {
        IReadOnlyList<DellCatalogEntry> entries = catalog ?? Array.Empty<DellCatalogEntry>();
        List<FirmwareSetting> settings = new();
        List<DellInterfaceProblem> problems = new();
        try
        {
            ArgumentNullException.ThrowIfNull(budget);
            if (!_found())
            {
                problems.Add(Problem(DellProblemKind.ProgramNotFound, NotFoundDetail));
                return Finish(entries, settings, problems);
            }

            if (EarlierRunTimedOut)
            {
                _logger.Warning("Dell Command Configure not run: {Detail}", EarlierTimeoutDetail);
                problems.Add(Problem(DellProblemKind.TimedOut, EarlierTimeoutDetail));
                return Finish(entries, settings, problems);
            }

            foreach (DellCatalogEntry entry in entries)
            {
                if (entry.CctkOption is not CctkReadOption option)
                {
                    settings.Add(DellSettingMapper.NotReported(entry, PossibleValuesOrigin.Catalog, DellSentences.NotReadByFallbackNote));
                    continue;
                }

                if (budget.Remaining < RunTimeout)
                {
                    // The budget is the whole read's, so its problem is the reader's (DF21).
                    problems.Add(new DellInterfaceProblem(DellInterface.Reader, DellProblemKind.BudgetExhausted, BudgetDetail));
                    break;
                }

                CctkRunOutcome run = _run(option, RunTimeout);
                if (run.TimedOut)
                {
                    Interlocked.Exchange(ref s_runTimedOut, 1);
                }

                LogRun(run, entry.CctkOutputNames);
                FirmwareSetting setting = ParseRow(entry, run, out DellInterfaceProblem? problem);
                settings.Add(setting);
                if (setting.DisplayFromVendor)
                {
                    _logger.Warning("Dell setting {Id} ({Name}): Dell Command Configure's word {Token} is not in the catalog, so it is shown as printed", setting.Id, setting.Name, setting.Current?.Token);
                }

                if (problem is not null)
                {
                    problems.Add(problem);
                }

                if (StopKind(run) is DellProblemKind stop)
                {
                    if (stop == DellProblemKind.RunFailed && run.Started)
                    {
                        _logger.Warning("Dell Command Configure run failed: {Detail}; nothing more runs in this read, because the program's table gives some exit codes the meaning that a change was attempted or failed", problem?.Detail);
                    }
                    else if (stop == DellProblemKind.RunFailed)
                    {
                        _logger.Warning("Dell Command Configure run failed: {Detail}; nothing more runs in this read", problem?.Detail);
                    }

                    break;
                }
            }

            return Finish(entries, settings, problems);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Dell Command Configure read failed unexpectedly");
            problems.Add(Problem(DellProblemKind.Unexpected, ex.GetType().Name + ": " + ex.Message.Replace('\r', ' ').Replace('\n', ' ').Trim()));
            return Finish(entries, settings, problems);
        }
    }

    /// <summary>
    /// The value of the one line of <paramref name="output"/> that names the setting (pure): each
    /// line, trimmed, is split at its first "="; the name before it, trimmed, is compared
    /// case-insensitively with <paramref name="outputNames"/>, the 5.2.2 name and the older one.
    /// Every other line, a banner among them, is ignored.
    /// </summary>
    /// <param name="output">The run's standard output.</param>
    /// <param name="outputNames">The names the setting's line may carry.</param>
    /// <param name="matches">How many lines named the setting.</param>
    /// <returns>The value after the "=", trimmed, when exactly one line named the setting; otherwise null.</returns>
    internal static string? FindValue(string output, IReadOnlyList<string> outputNames, out int matches)
    {
        matches = 0;
        if (outputNames is null)
        {
            return null;
        }

        string? value = null;
        foreach (string line in Lines(output))
        {
            if (ValueOf(line, outputNames) is string found)
            {
                matches++;
                value = found;
            }
        }

        return matches == 1 ? value : null;
    }

    /// <summary>
    /// The row of one run (pure). A run that stops the read (<see cref="StopKind"/>) and an option
    /// this computer does not offer (exit code 118, 42 or 96) make the row not reported, with that
    /// problem. Otherwise the value of the one line that names the setting
    /// (<see cref="FindValue"/>) is mapped through the entry's words, case-insensitively
    /// (<see cref="DellSettingMapper.FromCctk"/>): a word the catalog lists gives the catalog's
    /// text, any other is shown as printed and marked <see cref="FirmwareSetting.DisplayFromVendor"/>;
    /// no such line, two of them or an empty value make the row not reported, with
    /// <see cref="DellProblemKind.UnrecognisedOutput"/>. The possible values are the catalog's, and
    /// there is no vendor read-only flag.
    /// </summary>
    /// <param name="entry">The catalog entry the run read.</param>
    /// <param name="run">The run's outcome.</param>
    /// <param name="problem">The row's problem, or null when it was read.</param>
    /// <returns>The setting.</returns>
    internal static FirmwareSetting ParseRow(DellCatalogEntry entry, CctkRunOutcome run, out DellInterfaceProblem? problem)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(run);
        if (StopKind(run) is DellProblemKind stop)
        {
            problem = Problem(stop, StopDetail(stop, run));
            return DellSettingMapper.NotReported(entry, PossibleValuesOrigin.Catalog, null);
        }

        if (run.ExitCode is int code && OptionNotSupportedExitCodes.Contains(code))
        {
            problem = Problem(DellProblemKind.OptionNotSupported, ExitDetail(run.Argument, code));
            return DellSettingMapper.NotReported(entry, PossibleValuesOrigin.Catalog, null);
        }

        string? value = FindValue(run.StandardOutput, entry.CctkOutputNames, out int matches);
        if (string.IsNullOrEmpty(value))
        {
            problem = Problem(DellProblemKind.UnrecognisedOutput, UnrecognisedDetail(run.Argument, entry.CctkOutputNames, matches));
            return DellSettingMapper.NotReported(entry, PossibleValuesOrigin.Catalog, null);
        }

        problem = null;
        return DellSettingMapper.FromCctk(entry, value);
    }

    /// <summary>
    /// Whether a run ends the read, and why (pure): a run that did not start is
    /// <see cref="DellProblemKind.RunFailed"/>; a timeout is <see cref="DellProblemKind.TimedOut"/>;
    /// exit code 95 is <see cref="DellProblemKind.NeedsAdministrator"/>; 12 and 140 are
    /// <see cref="DellProblemKind.NoBiosInterface"/>; and every exit code but 0, 42, 96 and 118, or
    /// none, is RunFailed (DF20: the program's table gives several codes the meaning that a change
    /// was attempted or failed, the strongest sign that a read was taken for a change).
    /// </summary>
    /// <param name="run">The run's outcome.</param>
    /// <returns>The kind of the problem that ends the read, or null when the next option may run.</returns>
    internal static DellProblemKind? StopKind(CctkRunOutcome run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (!run.Started)
        {
            return DellProblemKind.RunFailed;
        }

        if (run.TimedOut)
        {
            return DellProblemKind.TimedOut;
        }

        return run.ExitCode switch
        {
            0 => null,
            NeedsAdministratorExitCode => DellProblemKind.NeedsAdministrator,
            int code when NoBiosInterfaceExitCodes.Contains(code) => DellProblemKind.NoBiosInterface,
            int code when OptionNotSupportedExitCodes.Contains(code) => null,
            _ => DellProblemKind.RunFailed,
        };
    }

    /// <summary>The read made of the rows settled so far: the rows not yet run not read, answered when at least one row was read.</summary>
    private DellProviderRead Finish(IReadOnlyList<DellCatalogEntry> entries, List<FirmwareSetting> settings, List<DellInterfaceProblem> problems)
    {
        for (int i = settings.Count; i < entries.Count; i++)
        {
            settings.Add(DellSettingMapper.NotRead(entries[i], null));
        }

        int read = settings.Count(setting => setting.State == SettingState.Read);
        if (read > 0)
        {
            _logger.Debug("Dell Command Configure answered: {Read} of the {Total} agreed settings read; {Problems} problem(s)", read, entries.Count, problems.Count);
        }
        else
        {
            _logger.Debug("Dell Command Configure gave no answer: {Kind}", problems.FirstOrDefault()?.Kind);
        }

        return new DellProviderRead(read > 0, settings.ToArray(), problems.ToArray(), null);
    }

    /// <summary>
    /// Logs one run at Debug: its exit code, its time, the value line it
    /// matched and how many other lines each stream held; then every other line of standard output
    /// and every line of standard error, one log line each, at most ten per run, and how many more
    /// were not logged. Lines are trimmed, blank ones skipped, control characters made spaces and
    /// each cut at 200 characters. Protocol step M4-H2 judges its stop condition on these lines.
    /// </summary>
    private void LogRun(CctkRunOutcome run, IReadOnlyList<string> outputNames)
    {
        string[] output = Lines(run.StandardOutput);
        string[] errors = Lines(run.StandardError);
        int matched = -1;
        int matches = 0;
        for (int i = 0; i < output.Length; i++)
        {
            if (ValueOf(output[i], outputNames) is not null)
            {
                matches++;
                matched = i;
            }
        }

        if (matches != 1)
        {
            matched = -1;
        }

        List<(string Stream, int Number, string Text)> others = new();
        for (int i = 0; i < output.Length; i++)
        {
            if (i != matched && output[i].Length > 0)
            {
                others.Add(("standard output", i + 1, output[i]));
            }
        }

        int otherOutput = others.Count;
        for (int i = 0; i < errors.Length; i++)
        {
            if (errors[i].Length > 0)
            {
                others.Add(("standard error", i + 1, errors[i]));
            }
        }

        _logger.Debug(
            "cctk {Argument}: exit {ExitCode} in {ElapsedMs} ms; matched {Line}; {Other} other line(s); {Errors} standard error line(s)",
            run.Argument,
            run.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "none",
            (long)Math.Round(run.ElapsedMilliseconds),
            matched < 0 ? "no line" : ForLog(output[matched]),
            otherOutput,
            others.Count - otherOutput);
        foreach ((string stream, int number, string text) in others.Take(MostLoggedLines))
        {
            _logger.Debug("cctk {Argument} {Stream} line {n}: {Text}", run.Argument, stream, number, ForLog(text));
        }

        if (others.Count > MostLoggedLines)
        {
            _logger.Debug("cctk {Argument}: {k} more line(s) not logged", run.Argument, others.Count - MostLoggedLines);
        }
    }

    /// <summary>The value of a line that names the setting (after its first "=", trimmed); null for any other line.</summary>
    private static string? ValueOf(string line, IReadOnlyList<string> outputNames)
    {
        int separator = line.IndexOf('=', StringComparison.Ordinal);
        if (separator <= 0)
        {
            return null;
        }

        string name = line[..separator].Trim();
        return outputNames.Any(candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)) ? line[(separator + 1)..].Trim() : null;
    }

    /// <summary>The lines of a stream, each trimmed (a line break of either kind removed); none for an empty stream.</summary>
    private static string[] Lines(string? text)
    {
        return string.IsNullOrEmpty(text) ? Array.Empty<string>() : text.Split('\n').Select(line => line.Trim()).ToArray();
    }

    /// <summary>A line as it is logged: control characters made spaces, trimmed, cut at <see cref="MostLoggedCharacters"/>.</summary>
    private static string ForLog(string line)
    {
        string text = new string(line.Select(character => char.IsControl(character) ? ' ' : character).ToArray()).Trim();
        if (text.Length <= MostLoggedCharacters)
        {
            return text;
        }

        return text[..(char.IsHighSurrogate(text[MostLoggedCharacters - 1]) ? MostLoggedCharacters - 1 : MostLoggedCharacters)];
    }

    /// <summary>The detail of a problem that ends the read: why the run did not start, the bound it did not finish in, or its exit code.</summary>
    private static string StopDetail(DellProblemKind kind, CctkRunOutcome run)
    {
        if (!run.Started)
        {
            return run.Argument + ": not started: " + (string.IsNullOrWhiteSpace(run.Error) ? "the process did not start" : run.Error.Trim());
        }

        if (kind == DellProblemKind.TimedOut)
        {
            return run.Argument + ": did not finish within " + Number((int)Math.Round(RunTimeout.TotalSeconds)) + " seconds";
        }

        return run.ExitCode is int code ? ExitDetail(run.Argument, code) : run.Argument + ": ended without an exit code";
    }

    /// <summary>The detail that names a run's exit code.</summary>
    private static string ExitDetail(string argument, int code) => argument + ": exit code " + Number(code);

    /// <summary>The detail of an answer the parser does not recognise: no line named the setting, several did, or the one that did holds no value.</summary>
    private static string UnrecognisedDetail(string argument, IReadOnlyList<string> outputNames, int matches)
    {
        string names = string.Join(" or ", outputNames);
        return matches switch
        {
            0 => argument + ": no line names " + names,
            1 => argument + ": the line that names " + names + " holds no value",
            _ => argument + ": " + Number(matches) + " lines name " + names,
        };
    }

    private static DellInterfaceProblem Problem(DellProblemKind kind, string detail) => new(DellInterface.DellCommandConfigure, kind, detail);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
