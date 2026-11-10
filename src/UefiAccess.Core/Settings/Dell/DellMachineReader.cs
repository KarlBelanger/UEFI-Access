// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Serilog;
using UefiAccess.Core.Platform;

namespace UefiAccess.Core.Settings.Dell;

/// <summary>
/// Reads the machine a Dell read runs on (R1): the
/// manufacturer and the model of Win32_ComputerSystem and the BIOS version of Win32_BIOS, three
/// fields and nothing else, never the serial number or the service tag. Its two queries are the
/// pinned SELECTs below (DellSettingsGuardTests), each run through <see cref="WmiHelper"/> on a
/// pool thread and waited for at most 60 seconds or what is left of the read budget. It is new
/// code, not <see cref="MachineIdentity"/>, which reads the service tag too. A query that fails,
/// times out or finds nothing leaves its part null and logs one Debug line, so an unknown
/// manufacturer is never taken for another maker's. Never throws.
/// </summary>
internal sealed class DellMachineReader
{
    /// <summary>The manufacturer and the model (R1), pinned by DellSettingsGuardTests.</summary>
    internal const string ComputerSystemQuery = "SELECT Manufacturer, Model FROM Win32_ComputerSystem";

    /// <summary>The BIOS version (R1), pinned by DellSettingsGuardTests.</summary>
    internal const string BiosQuery = "SELECT SMBIOSBIOSVersion FROM Win32_BIOS";

    /// <summary>The longest one query may wait: 60 seconds, as every WMI query of the tool, and never more than the budget leaves.</summary>
    internal static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(60);

    /// <summary>The namespace of both classes.</summary>
    private const string Cimv2Namespace = @"root\CIMV2";

    /// <summary>The part of the machine the first query reads, in its log line.</summary>
    private const string ComputerSystemPart = "manufacturer and model";

    /// <summary>The part of the machine the second query reads, in its log line.</summary>
    private const string BiosPart = "BIOS version";

    private readonly ILogger _logger;
    private readonly Func<string, TimeSpan, WmiQueryResult> _query;

    /// <summary>Composes the reader over the machine's WMI: each query bounded on a pool thread.</summary>
    /// <param name="logger">Logger for the queries and what could not be read.</param>
    public DellMachineReader(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _query = (wql, bound) => QueryBounded(wql, bound, logger);
    }

    /// <summary>The seam for tests: each query is the caller's; nothing here touches WMI.</summary>
    /// <param name="logger">Logger for what could not be read.</param>
    /// <param name="query">Runs one of the two queries within the bound it is given; it may throw <see cref="TimeoutException"/>.</param>
    internal DellMachineReader(ILogger logger, Func<string, TimeSpan, WmiQueryResult> query)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(query);
        _logger = logger;
        _query = query;
    }

    /// <summary>
    /// Reads the three fields: <see cref="ComputerSystemQuery"/>, then <see cref="BiosQuery"/>,
    /// each within <see cref="StepTimeout"/> or what is left of the budget. With nothing left, a
    /// query is not sent. Every part that could not be read is null. Never throws.
    /// </summary>
    /// <param name="budget">The time left of the whole Dell read.</param>
    /// <returns>The machine.</returns>
    public DellMachine Read(DellReadBudget budget)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(budget);
            IReadOnlyDictionary<string, object?>? system = QueryFirst(ComputerSystemQuery, ComputerSystemPart, budget);
            IReadOnlyDictionary<string, object?>? bios = QueryFirst(BiosQuery, BiosPart, budget);
            return FromRows(system, bios);
        }
        catch (Exception ex)
        {
            _logger.Debug("Dell machine not read: {Detail}", WmiHelper.Describe(ex));
            return new DellMachine(null, null, null);
        }
    }

    /// <summary>
    /// The machine made of the two rows (pure): Manufacturer and Model of the computer system's
    /// row, SMBIOSBIOSVersion of the BIOS row, each trimmed, and null when its row or its value is
    /// missing or blank. No other property of either row is read.
    /// </summary>
    /// <param name="system">The row of <see cref="ComputerSystemQuery"/>, or null.</param>
    /// <param name="bios">The row of <see cref="BiosQuery"/>, or null.</param>
    /// <returns>The machine.</returns>
    internal static DellMachine FromRows(IReadOnlyDictionary<string, object?>? system, IReadOnlyDictionary<string, object?>? bios)
    {
        return new DellMachine(
            Text(system, "Manufacturer"),
            Text(system, "Model"),
            Text(bios, "SMBIOSBIOSVersion"));
    }

    /// <summary>The first row of one query, or null (with one Debug line) when there is none: no budget left, a timeout, an absent class, an error or no instance.</summary>
    private IReadOnlyDictionary<string, object?>? QueryFirst(string wql, string part, DellReadBudget budget)
    {
        if (budget.Exhausted)
        {
            _logger.Debug("Dell machine {Part} not read: nothing was left of the read budget", part);
            return null;
        }

        WmiQueryResult result;
        try
        {
            result = _query(wql, budget.Bound(StepTimeout));
        }
        catch (Exception ex)
        {
            _logger.Debug("Dell machine {Part} not read: {Detail}", part, ex is TimeoutException ? ex.Message : WmiHelper.Describe(ex));
            return null;
        }

        if (result is null || result.Absent || result.Error is not null || result.Rows.Count == 0)
        {
            string reason = result is null ? "the query returned no result"
                : result.Absent ? "the class is not available"
                : result.Error ?? "no instance was returned";
            _logger.Debug("Dell machine {Part} not read: {Detail}", part, reason);
            return null;
        }

        return result.Rows[0];
    }

    /// <summary>One query of root\CIMV2 on a pool thread, waited for at most <paramref name="bound"/>; a timeout is thrown as <see cref="TimeoutException"/>.</summary>
    private static WmiQueryResult QueryBounded(string wql, TimeSpan bound, ILogger logger)
    {
        string label = string.Equals(wql, BiosQuery, StringComparison.Ordinal) ? "Win32_BIOS" : "Win32_ComputerSystem";
        return WmiHelper.RunBounded(() => WmiHelper.Query(Cimv2Namespace, wql, label, logger, maxInstances: 1), bound, "query of " + label);
    }

    private static string? Text(IReadOnlyDictionary<string, object?>? row, string name) => row is null ? null : WmiValues.ToText(WmiHelper.Get(row, name));
}
