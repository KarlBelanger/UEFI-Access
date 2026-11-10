// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Serilog;
using UefiAccess.Core.Platform;

namespace UefiAccess.Core.Settings.Dell;

/// <summary>
/// Reads the agreed Dell BIOS settings through Dell Command | Monitor: one fixed SELECT on its enumeration class in root\dcim\sysman (R2),
/// through <see cref="WmiHelper"/> and under a bounded wait, and, only when that class is absent,
/// whether the namespace exists (R3), so that a missing tool and a broken one are told apart. The
/// catalog's rows are picked from the answer in memory; the other rows are kept only when the full
/// enumeration is asked for, and their values are never logged. It queries no other class, invokes
/// no method and starts no process. A query that timed out keeps running on its pool thread, so
/// while one of this process has not finished no second query is sent: a hung provider never gets
/// another. A query that WmiHelper's own bound gave up on came back, but left its connect or its
/// enumeration running where nothing reports its end, so it never counts as finished.
/// </summary>
internal sealed class DcmSettingsProvider : IDellSettingsProvider
{
    /// <summary>The one query (R2), pinned by DellSettingsGuardTests: five properties of the enumeration class, never an identity.</summary>
    internal const string SettingsQuery = "SELECT AttributeName, CurrentValue, PossibleValues, PossibleValuesDescription, IsReadOnly FROM DCIM_BIOSEnumeration";

    /// <summary>The query's label in the log and in its errors.</summary>
    internal const string QueryLabel = "Dell BIOS settings";

    /// <summary>The longest one query, or one namespace check, may wait: 60 seconds, the M1 probe's bound, and never more than the budget leaves.</summary>
    internal static readonly TimeSpan StepTimeout = DellInterfaceProbe.DcmQueryTimeout;

    /// <summary>The detail of the problem when an earlier query of this process has not finished, or timed out inside WmiHelper and so may never be seen to finish.</summary>
    private const string EarlierQueryDetail = "an earlier query has not finished";

    /// <summary>The detail of the problem when nothing was left of the budget.</summary>
    private const string BudgetDetail = "nothing was left of the read budget";

    /// <summary>
    /// The number of queries of this process still running, counted from the moment one is handed
    /// to its pool thread until it returns, whether the bounded wait for it timed out or not. A
    /// query that returns timed out by WmiHelper's own bound is never counted down: its connect or
    /// its enumeration was abandoned on a pool thread that reports no end, so it counts for the
    /// rest of the process's life, as the timed-out run of Dell Command | Configure does.
    /// </summary>
    private static int s_queriesInFlight;

    private readonly ILogger _logger;
    private readonly Func<TimeSpan, DcmQueryOutcome> _query;
    private readonly Func<TimeSpan, bool?> _sysmanNamespaceExists;

    /// <summary>Composes the provider over the machine's WMI: the bounded query of the enumeration class and the bounded namespace check.</summary>
    /// <param name="logger">Logger for the queries and the outcome.</param>
    public DcmSettingsProvider(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _query = bound => QueryBounded(() => WmiHelper.Query(DellInterfaceProbe.SysmanNamespace, SettingsQuery, QueryLabel, logger), bound, logger);
        _sysmanNamespaceExists = bound => SysmanNamespaceExists(bound, logger);
    }

    /// <summary>The seam for tests: the query and the namespace check are the caller's; nothing here touches WMI.</summary>
    /// <param name="logger">Logger for the outcome.</param>
    /// <param name="query">Runs the query within the bound it is given.</param>
    /// <param name="sysmanNamespaceExists">Checks within the bound it is given whether root\dcim\sysman exists: true, false, or null when that could not be told.</param>
    internal DcmSettingsProvider(ILogger logger, Func<TimeSpan, DcmQueryOutcome> query, Func<TimeSpan, bool?> sysmanNamespaceExists)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(sysmanNamespaceExists);
        _logger = logger;
        _query = query;
        _sysmanNamespaceExists = sysmanNamespaceExists;
    }

    /// <inheritdoc />
    public DellSettingsSource Source => DellSettingsSource.DellCommandMonitor;

    /// <summary>The number of queries of this process still running (for tests that wait for a released one).</summary>
    internal static int QueriesInFlight => Volatile.Read(ref s_queriesInFlight);

    /// <summary>
    /// Reads the catalog's settings once. With the budget used up, nothing is queried
    /// (<see cref="DellProblemKind.BudgetExhausted"/>, the reader's problem, DF21). While an
    /// earlier query of this process has not finished, or timed out inside WmiHelper, nothing is
    /// queried either (<see cref="DellProblemKind.TimedOut"/>). Otherwise
    /// the query runs within <see cref="StepTimeout"/> or what is left of the budget, and when the
    /// class is absent, the namespace check within what is left. Never throws.
    /// </summary>
    /// <param name="catalog">The agreed settings.</param>
    /// <param name="budget">The time left of the whole read.</param>
    /// <param name="includeAllAttributes">True to keep every attribute the answer holds.</param>
    /// <returns>The read.</returns>
    public DellProviderRead Read(IReadOnlyList<DellCatalogEntry> catalog, DellReadBudget budget, bool includeAllAttributes)
    {
        IReadOnlyList<DellCatalogEntry> entries = catalog ?? Array.Empty<DellCatalogEntry>();
        try
        {
            ArgumentNullException.ThrowIfNull(budget);
            if (budget.Exhausted)
            {
                return NotAnswered(entries, new DellInterfaceProblem(DellInterface.Reader, DellProblemKind.BudgetExhausted, BudgetDetail));
            }

            if (QueriesInFlight > 0)
            {
                _logger.Warning("Dell Command Monitor not queried: {Detail}", EarlierQueryDetail);
                return NotAnswered(entries, new DellInterfaceProblem(DellInterface.DellCommandMonitor, DellProblemKind.TimedOut, EarlierQueryDetail));
            }

            DcmQueryOutcome outcome = _query(budget.Bound(StepTimeout));
            bool? namespaceExists = null;
            if (outcome.Result.Absent && !outcome.TimedOut && !budget.Exhausted)
            {
                namespaceExists = _sysmanNamespaceExists(budget.Remaining);
            }

            DellProviderRead read = FromOutcome(outcome, namespaceExists, entries, includeAllAttributes);
            if (read.Answered)
            {
                int agreed = read.Settings.Count(setting => setting.State != SettingState.NotReported);
                _logger.Debug(
                    "Dell Command Monitor answered with {Rows} attribute(s): {Agreed} of the {Total} agreed settings, and {Other} other attribute(s) whose values are not logged",
                    outcome.Result.Rows.Count,
                    agreed,
                    entries.Count,
                    outcome.Result.Rows.Count - agreed);
                foreach (FirmwareSetting setting in read.Settings.Where(setting => setting.DisplayFromVendor))
                {
                    _logger.Warning("Dell setting {Id} ({Name}): Dell Command Monitor's name {VendorName} is not in the catalog, so Dell's own word is shown", setting.Id, setting.Name, setting.Current?.VendorName);
                }
            }
            else
            {
                _logger.Debug("Dell Command Monitor gave no answer: {Kind}", read.Problems.FirstOrDefault()?.Kind);
            }

            return read;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Dell Command Monitor read failed unexpectedly");
            return NotAnswered(entries, new DellInterfaceProblem(DellInterface.DellCommandMonitor, DellProblemKind.Unexpected, WmiHelper.Describe(ex)));
        }
    }

    /// <summary>
    /// The read made of a query's outcome and, when the class was absent, the namespace check
    /// (pure). Answered means rows were returned: each catalog entry is then mapped from its row
    /// (<see cref="ToSetting"/>), or is <see cref="SettingState.NotReported"/> without one (DF10).
    /// Rows are keyed by AttributeName, ordinal first and then case-insensitive. Otherwise the one
    /// problem of <see cref="Classify"/> and every setting not read.
    /// </summary>
    /// <param name="outcome">The bounded query's outcome.</param>
    /// <param name="namespaceExists">Whether root\dcim\sysman exists; null when it was not checked or could not be told.</param>
    /// <param name="catalog">The agreed settings.</param>
    /// <param name="includeAllAttributes">True to keep every row as a <see cref="DellVendorAttribute"/>.</param>
    /// <returns>The read.</returns>
    internal static DellProviderRead FromOutcome(DcmQueryOutcome outcome, bool? namespaceExists, IReadOnlyList<DellCatalogEntry> catalog, bool includeAllAttributes)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(catalog);
        if (Classify(outcome, namespaceExists) is DellProblemKind kind)
        {
            return NotAnswered(catalog, new DellInterfaceProblem(DellInterface.DellCommandMonitor, kind, Detail(kind, outcome)));
        }

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = outcome.Result.Rows;
        FirmwareSetting[] settings = catalog
            .Select(entry => FindRow(rows, entry.DcmAttributeName) is { } row ? ToSetting(row, entry) : DellSettingMapper.NotReported(entry, PossibleValuesOrigin.Interface, null))
            .ToArray();
        DellVendorAttribute[]? all = includeAllAttributes ? rows.Select(ToVendorAttribute).ToArray() : null;
        return new DellProviderRead(true, settings, Array.Empty<DellInterfaceProblem>(), all);
    }

    /// <summary>
    /// Why the query gave no usable answer (pure; null when it answered): a timeout, the bounded
    /// wait's or the enumeration's own, is <see cref="DellProblemKind.TimedOut"/>; an absent class
    /// is <see cref="DellProblemKind.NotInstalled"/> without the namespace,
    /// <see cref="DellProblemKind.ClassMissing"/> with it, and
    /// <see cref="DellProblemKind.QueryFailed"/> when that could not be told; a provider that did
    /// not load and refused access are their own kinds; any other error is QueryFailed; and zero
    /// rows are <see cref="DellProblemKind.NoInstances"/>, never an empty list.
    /// </summary>
    /// <param name="outcome">The bounded query's outcome.</param>
    /// <param name="namespaceExists">Whether root\dcim\sysman exists; null when it was not checked or could not be told.</param>
    /// <returns>The kind, or null when rows were returned.</returns>
    internal static DellProblemKind? Classify(DcmQueryOutcome outcome, bool? namespaceExists)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        WmiQueryResult result = outcome.Result;
        if (outcome.TimedOut || result.Failure == WmiFailureKind.TimedOut)
        {
            return DellProblemKind.TimedOut;
        }

        if (result.Absent)
        {
            return namespaceExists switch
            {
                false => DellProblemKind.NotInstalled,
                true => DellProblemKind.ClassMissing,
                null => DellProblemKind.QueryFailed,
            };
        }

        if (result.Error is not null)
        {
            return result.Failure switch
            {
                WmiFailureKind.ProviderLoadFailure => DellProblemKind.ProviderLoadFailure,
                WmiFailureKind.AccessDenied => DellProblemKind.AccessDenied,
                _ => DellProblemKind.QueryFailed,
            };
        }

        return result.Rows.Count == 0 ? DellProblemKind.NoInstances : null;
    }

    /// <summary>
    /// One catalog entry from its row (pure): the probe's alignment rule
    /// (<see cref="DellInterfaceProbe.AlignedTextList"/>, blank elements kept so the codes and the
    /// names stay aligned), each current code named through
    /// <see cref="DellInterfaceProbe.DescribeValues"/>, then <see cref="DellSettingMapper.FromDcm"/>.
    /// </summary>
    /// <param name="row">The row as <see cref="WmiHelper.Query(string, string, string, ILogger, int)"/> returns it.</param>
    /// <param name="entry">The catalog entry.</param>
    /// <returns>The setting.</returns>
    internal static FirmwareSetting ToSetting(IReadOnlyDictionary<string, object?> row, DellCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(entry);
        return DellSettingMapper.FromDcm(
            entry,
            DellInterfaceProbe.AlignedTextList(WmiHelper.Get(row, "CurrentValue")),
            DellInterfaceProbe.AlignedTextList(WmiHelper.Get(row, "PossibleValues")),
            DellInterfaceProbe.AlignedTextList(WmiHelper.Get(row, "PossibleValuesDescription")),
            WmiValues.ToNullableBool(WmiHelper.Get(row, "IsReadOnly")));
    }

    /// <summary>
    /// Runs one query on a pool thread and waits at most <paramref name="bound"/> for it, the
    /// probe's bounded query with the count of queries in flight: raised before the query is handed
    /// to its thread and lowered when it returns, so a query that timed out still counts until it
    /// really ends. A query that returns timed out (<see cref="WmiFailureKind.TimedOut"/>: WmiHelper
    /// gave up on its own connect or enumeration, which may still be running) is not lowered at all.
    /// A timeout comes back as an outcome that says so, never as an exception.
    /// </summary>
    /// <param name="query">The query (WmiHelper.Query in production).</param>
    /// <param name="bound">The longest to wait.</param>
    /// <param name="logger">Logger for a timeout.</param>
    /// <returns>The query's result, or a timed-out outcome.</returns>
    internal static DcmQueryOutcome QueryBounded(Func<WmiQueryResult> query, TimeSpan bound, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(logger);
        Interlocked.Increment(ref s_queriesInFlight);
        try
        {
            WmiQueryResult result = WmiHelper.RunBounded(
                () =>
                {
                    bool abandoned = false;
                    try
                    {
                        WmiQueryResult answer = query();
                        abandoned = answer is { Failure: WmiFailureKind.TimedOut };
                        return answer;
                    }
                    finally
                    {
                        // Nothing reports when an abandoned connect or enumeration ends, so it stays counted.
                        if (!abandoned)
                        {
                            Interlocked.Decrement(ref s_queriesInFlight);
                        }
                    }
                },
                bound,
                "query of " + QueryLabel);
            return new DcmQueryOutcome(result, false);
        }
        catch (TimeoutException ex)
        {
            logger.Warning("WMI {Namespace}: {Query} timed out: {Detail}", DellInterfaceProbe.SysmanNamespace, SettingsQuery, ex.Message);
            return new DcmQueryOutcome(new WmiQueryResult(Array.Empty<IReadOnlyDictionary<string, object?>>(), QueryLabel + ": " + ex.Message, false), true);
        }
    }

    /// <summary>R3: whether root\dcim\sysman exists, within the bound; null when that could not be told in time.</summary>
    private static bool? SysmanNamespaceExists(TimeSpan bound, ILogger logger)
    {
        try
        {
            return WmiHelper.RunBounded(() => WmiHelper.NamespaceExists(DellInterfaceProbe.SysmanNamespace, logger, out _), bound, "check of namespace " + DellInterfaceProbe.SysmanNamespace);
        }
        catch (TimeoutException ex)
        {
            logger.Warning("WMI namespace {Namespace}: check timed out: {Detail}", DellInterfaceProbe.SysmanNamespace, ex.Message);
            return null;
        }
    }

    /// <summary>The read of a provider that gave no answer: the one problem, and every setting not read.</summary>
    private static DellProviderRead NotAnswered(IReadOnlyList<DellCatalogEntry> catalog, DellInterfaceProblem problem)
    {
        return new DellProviderRead(false, catalog.Select(entry => DellSettingMapper.NotRead(entry, null)).ToArray(), new[] { problem }, null);
    }

    /// <summary>The detail of a problem: the WMI error as reported, or what was found.</summary>
    private static string Detail(DellProblemKind kind, DcmQueryOutcome outcome)
    {
        string? error = outcome.Result.Error;
        return kind switch
        {
            DellProblemKind.NotInstalled => "the namespace " + DellInterfaceProbe.SysmanNamespace + " is absent",
            DellProblemKind.ClassMissing => "the namespace " + DellInterfaceProbe.SysmanNamespace + " exists, but DCIM_BIOSEnumeration does not",
            DellProblemKind.QueryFailed when outcome.Result.Absent => "DCIM_BIOSEnumeration is absent, and whether the namespace " + DellInterfaceProbe.SysmanNamespace + " exists could not be told",
            DellProblemKind.NoInstances => "DCIM_BIOSEnumeration answered with no instances",
            DellProblemKind.TimedOut => error ?? "the query did not finish in time",
            _ => error ?? kind.ToString(),
        };
    }

    /// <summary>The row whose AttributeName is <paramref name="name"/>: an ordinal match first, then a case-insensitive one; null when none.</summary>
    private static IReadOnlyDictionary<string, object?>? FindRow(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, string name)
    {
        return rows.FirstOrDefault(row => string.Equals(AttributeName(row), name, StringComparison.Ordinal))
            ?? rows.FirstOrDefault(row => string.Equals(AttributeName(row), name, StringComparison.OrdinalIgnoreCase));
    }

    private static string? AttributeName(IReadOnlyDictionary<string, object?> row) => WmiValues.ToText(WmiHelper.Get(row, "AttributeName"));

    /// <summary>One row exactly as Dell reports it, for the full enumeration.</summary>
    private static DellVendorAttribute ToVendorAttribute(IReadOnlyDictionary<string, object?> row)
    {
        IReadOnlyList<string> current = DellInterfaceProbe.AlignedTextList(WmiHelper.Get(row, "CurrentValue"));
        IReadOnlyList<string> possible = DellInterfaceProbe.AlignedTextList(WmiHelper.Get(row, "PossibleValues"));
        IReadOnlyList<string> names = DellInterfaceProbe.AlignedTextList(WmiHelper.Get(row, "PossibleValuesDescription"));
        return new DellVendorAttribute(
            AttributeName(row) ?? string.Empty,
            current,
            DellInterfaceProbe.DescribeValues(current, possible, names),
            possible,
            names,
            WmiValues.ToNullableBool(WmiHelper.Get(row, "IsReadOnly")));
    }
}
