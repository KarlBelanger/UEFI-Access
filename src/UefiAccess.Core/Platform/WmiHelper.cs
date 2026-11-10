// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using Serilog;

namespace UefiAccess.Core.Platform;

/// <summary>What kind of failure a WMI query met, for a caller that gives each its own advice (the Dell settings provider).</summary>
internal enum WmiFailureKind
{
    /// <summary>The query did not fail: it answered, or the namespace or class is absent.</summary>
    None,

    /// <summary>The WMI provider could not be loaded (WBEM_E_PROVIDER_LOAD_FAILURE, 0x80041013).</summary>
    ProviderLoadFailure,

    /// <summary>Access was refused (WBEM_E_ACCESS_DENIED, 0x80041003; E_ACCESSDENIED, 0x80070005; an UnauthorizedAccessException).</summary>
    AccessDenied,

    /// <summary>A bound elapsed: the connect's or the whole query's (a TimeoutException), or the enumeration's own timeout (ManagementStatus.Timedout).</summary>
    TimedOut,

    /// <summary>Any other failure.</summary>
    Other,
}

/// <summary>Outcome of one WMI query run through <see cref="WmiHelper.Query(string, string, string, ILogger, int)"/>.</summary>
/// <param name="Rows">One dictionary per instance (property name to raw value); empty on failure.</param>
/// <param name="Error">A one-line description when the query failed for a reason other than absence; otherwise null.</param>
/// <param name="Absent">True when the namespace or class does not exist on this machine.</param>
internal sealed record WmiQueryResult(
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    string? Error,
    bool Absent)
{
    /// <summary>True when the query ran and the class exists (the row list may still be empty).</summary>
    public bool Succeeded => Error is null && !Absent;

    /// <summary>What kind of failure <see cref="Error"/> describes (<see cref="WmiHelper.ClassifyFailure"/>); <see cref="WmiFailureKind.None"/> when the query did not fail.</summary>
    public WmiFailureKind Failure { get; init; }
}

/// <summary>
/// Read-only WMI access shared by the platform inspectors. Every method guards
/// <see cref="OperatingSystem.IsWindows"/>, catches every exception, and reports through
/// return values so the inspectors never throw.
/// </summary>
/// <remarks>
/// Every connection and every enumeration made here is bounded at 60 seconds. The connect is
/// explicit (<see cref="ConnectBounded(string)"/>) and runs under a bounded wait, because
/// <see cref="ManagementScope.Connect"/> has no timeout of its own (ConnectionOptions.Timeout does
/// not apply to it) and the implicit connect inside ManagementObjectSearcher.Get would be just as
/// unbounded. The enumeration then runs on the connected scope, each batch bounded by
/// <see cref="ManagementOptions.Timeout"/> in the enumeration options and the whole of it by
/// <see cref="RunBounded{T}(Func{T}, TimeSpan, string)"/>.
/// </remarks>
internal static class WmiHelper
{
    private const string NotWindowsMessage = "WMI is only available on Windows";

    private const int WbemInvalidNamespace = unchecked((int)0x8004100E);
    private const int WbemInvalidClass = unchecked((int)0x80041010);
    private const int WbemNotFound = unchecked((int)0x80041002);
    private const int WbemProviderLoadFailure = unchecked((int)0x80041013);
    private const int WbemAccessDenied = unchecked((int)0x80041003);
    private const int AccessDeniedHResult = unchecked((int)0x80070005);

    /// <summary>Enumeration timeout so a stuck provider cannot hang the tool indefinitely: 60 seconds.</summary>
    internal static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Bound on <see cref="ManagementScope.Connect"/>, which has no timeout of its own; the same
    /// discipline as the enumeration timeout above.
    /// </summary>
    internal static readonly TimeSpan ConnectTimeout = QueryTimeout;

    /// <summary>
    /// Creates a scope for <paramref name="namespacePath"/> and connects it, both under
    /// <see cref="ConnectTimeout"/> (creating the scope already loads WMI's path parser). A
    /// searcher built on the returned scope does not connect again, so its Get is bounded by the
    /// enumeration options alone. Throws what the connect throws (with its own type, so
    /// <see cref="IsAbsent"/> keeps working) or <see cref="TimeoutException"/>.
    /// </summary>
    /// <param name="namespacePath">Namespace such as root\CIMV2.</param>
    /// <returns>The connected scope.</returns>
    internal static ManagementScope ConnectBounded(string namespacePath)
    {
        return ConnectBounded(
            namespacePath,
            ConnectTimeout,
            path =>
            {
                ManagementScope scope = new(path);
                scope.Connect();
                return scope;
            });
    }

    /// <summary>
    /// Testable core of <see cref="ConnectBounded(string)"/>: runs <paramref name="createAndConnect"/>
    /// under <paramref name="timeout"/> with the connect label, so a test can bound a stand-in
    /// without creating any System.Management object.
    /// </summary>
    /// <typeparam name="T">The scope type (<see cref="ManagementScope"/> in production).</typeparam>
    /// <param name="namespacePath">Namespace such as root\CIMV2.</param>
    /// <param name="timeout">Maximum wait for the connect.</param>
    /// <param name="createAndConnect">Creates and connects the scope for the namespace.</param>
    /// <returns>The connected scope.</returns>
    internal static T ConnectBounded<T>(string namespacePath, TimeSpan timeout, Func<string, T> createAndConnect)
    {
        ArgumentNullException.ThrowIfNull(createAndConnect);
        return RunBounded(() => createAndConnect(namespacePath), timeout, "connect to namespace " + namespacePath);
    }

    /// <summary>Runs a WQL query and materialises every property of every instance.</summary>
    /// <param name="namespacePath">Namespace such as root\CIMV2.</param>
    /// <param name="wql">The WQL text.</param>
    /// <param name="label">Short label for log and error messages, for example "Win32_BIOS".</param>
    /// <param name="logger">Logger for the call trace.</param>
    /// <param name="maxInstances">Stop after this many instances.</param>
    /// <returns>The rows, or the error, or an absence marker; never throws.</returns>
    public static WmiQueryResult Query(string namespacePath, string wql, string label, ILogger logger, int maxInstances = int.MaxValue)
    {
        return Query(namespacePath, wql, label, logger, maxInstances, ConnectBounded);
    }

    /// <summary>
    /// Testable core of <see cref="Query(string, string, string, ILogger, int)"/>: the scope comes
    /// from <paramref name="connect"/> (<see cref="ConnectBounded(string)"/> in production) and is
    /// used as it is, so the searcher never connects on its own.
    /// </summary>
    /// <param name="namespacePath">Namespace such as root\CIMV2.</param>
    /// <param name="wql">The WQL text.</param>
    /// <param name="label">Short label for log and error messages.</param>
    /// <param name="logger">Logger for the call trace.</param>
    /// <param name="maxInstances">Stop after this many instances.</param>
    /// <param name="connect">Returns the connected scope for a namespace.</param>
    /// <returns>The rows, or the error, or an absence marker; never throws.</returns>
    internal static WmiQueryResult Query(string namespacePath, string wql, string label, ILogger logger, int maxInstances, Func<string, ManagementScope> connect)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new WmiQueryResult(Array.Empty<IReadOnlyDictionary<string, object?>>(), label + ": " + NotWindowsMessage, false);
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            ManagementScope scope = connect(namespacePath);
            List<IReadOnlyDictionary<string, object?>> rows = RunBounded(
                () =>
                {
                    List<IReadOnlyDictionary<string, object?>> fetched = new();
                    using ManagementObjectSearcher searcher = new(scope, new ObjectQuery(wql), CreateEnumerationOptions());
                    using ManagementObjectCollection collection = searcher.Get();
                    foreach (ManagementBaseObject item in collection)
                    {
                        using (item)
                        {
                            fetched.Add(ToDictionary(item));
                        }

                        if (fetched.Count >= maxInstances)
                        {
                            break;
                        }
                    }

                    return fetched;
                },
                QueryTimeout,
                "query " + label);

            logger.Debug("WMI {Namespace}: {Query} -> {Count} instance(s), {Elapsed:F1} ms", namespacePath, wql, rows.Count, stopwatch.Elapsed.TotalMilliseconds);
            return new WmiQueryResult(rows, null, false);
        }
        catch (Exception ex) when (IsAbsent(ex))
        {
            logger.Debug("WMI {Namespace}: {Query} -> absent ({Detail}), {Elapsed:F1} ms", namespacePath, wql, Describe(ex), stopwatch.Elapsed.TotalMilliseconds);
            return new WmiQueryResult(Array.Empty<IReadOnlyDictionary<string, object?>>(), null, true);
        }
        catch (Exception ex)
        {
            string message = label + ": " + Describe(ex);
            logger.Warning("WMI {Namespace}: {Query} failed: {Detail}, {Elapsed:F1} ms", namespacePath, wql, Describe(ex), stopwatch.Elapsed.TotalMilliseconds);
            return new WmiQueryResult(Array.Empty<IReadOnlyDictionary<string, object?>>(), message, false) { Failure = ClassifyFailure(ex) };
        }
    }

    /// <summary>Lists the class names defined in a namespace (a meta_class query).</summary>
    /// <param name="namespacePath">Namespace such as root\dcim\sysman.</param>
    /// <param name="logger">Logger for the call trace.</param>
    /// <param name="error">Receives a one-line error when the listing failed; null otherwise.</param>
    /// <returns>The class names; empty when the namespace is absent or the listing failed.</returns>
    public static IReadOnlyList<string> ListClassNames(string namespacePath, ILogger logger, out string? error)
    {
        return ListClassNames(namespacePath, logger, ConnectBounded, out error);
    }

    /// <summary>
    /// Testable core of <see cref="ListClassNames(string, ILogger, out string?)"/>: the scope comes
    /// from <paramref name="connect"/> (<see cref="ConnectBounded(string)"/> in production).
    /// </summary>
    /// <param name="namespacePath">Namespace such as root\dcim\sysman.</param>
    /// <param name="logger">Logger for the call trace.</param>
    /// <param name="connect">Returns the connected scope for a namespace.</param>
    /// <param name="error">Receives a one-line error when the listing failed; null otherwise.</param>
    /// <returns>The class names; empty when the namespace is absent or the listing failed.</returns>
    internal static IReadOnlyList<string> ListClassNames(string namespacePath, ILogger logger, Func<string, ManagementScope> connect, out string? error)
    {
        error = null;
        if (!OperatingSystem.IsWindows())
        {
            error = namespacePath + ": " + NotWindowsMessage;
            return Array.Empty<string>();
        }

        List<string> names = new();
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            ManagementScope scope = connect(namespacePath);
            names = RunBounded(
                () =>
                {
                    List<string> listed = new();
                    using ManagementObjectSearcher searcher = new(scope, new ObjectQuery("SELECT * FROM meta_class"), CreateEnumerationOptions());
                    using ManagementObjectCollection collection = searcher.Get();
                    foreach (ManagementBaseObject item in collection)
                    {
                        using (item)
                        {
                            string? name = item.ClassPath?.ClassName;
                            if (!string.IsNullOrEmpty(name))
                            {
                                listed.Add(name);
                            }
                        }
                    }

                    return listed;
                },
                QueryTimeout,
                "class listing of " + namespacePath);

            logger.Debug("WMI {Namespace}: meta_class -> {Count} class(es), {Elapsed:F1} ms", namespacePath, names.Count, stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex) when (IsAbsent(ex))
        {
            logger.Debug("WMI {Namespace}: meta_class -> namespace absent ({Detail})", namespacePath, Describe(ex));
        }
        catch (Exception ex)
        {
            error = namespacePath + " class listing: " + Describe(ex);
            logger.Warning("WMI {Namespace}: meta_class failed: {Detail}", namespacePath, Describe(ex));
        }

        return names;
    }

    /// <summary>
    /// Tests whether a WMI namespace exists by connecting to it. The connect runs under
    /// <see cref="ConnectTimeout"/>: a provider that never answers yields an error and null instead
    /// of a hang.
    /// </summary>
    /// <param name="namespacePath">Namespace such as root\dcim\sysman\biosattributes.</param>
    /// <param name="logger">Logger for the call trace.</param>
    /// <param name="error">Receives a one-line error when the probe failed for a reason other than absence (including a timeout).</param>
    /// <returns>True when it exists, false when it does not, null when the probe could not be completed.</returns>
    public static bool? NamespaceExists(string namespacePath, ILogger logger, out string? error)
    {
        error = null;
        if (!OperatingSystem.IsWindows())
        {
            error = namespacePath + ": " + NotWindowsMessage;
            return null;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            _ = ConnectBounded(namespacePath);
            logger.Debug("WMI namespace {Namespace}: present, {Elapsed:F1} ms", namespacePath, stopwatch.Elapsed.TotalMilliseconds);
            return true;
        }
        catch (Exception ex) when (IsAbsent(ex))
        {
            logger.Debug("WMI namespace {Namespace}: absent ({Detail}), {Elapsed:F1} ms", namespacePath, Describe(ex), stopwatch.Elapsed.TotalMilliseconds);
            return false;
        }
        catch (Exception ex)
        {
            error = namespacePath + ": " + Describe(ex);
            logger.Warning("WMI namespace {Namespace}: probe failed: {Detail}", namespacePath, Describe(ex));
            return null;
        }
    }

    /// <summary>Copies every property (and the system properties) of an instance into a dictionary.</summary>
    /// <param name="item">The WMI object.</param>
    /// <returns>Property name to raw value.</returns>
    public static IReadOnlyDictionary<string, object?> ToDictionary(ManagementBaseObject item)
    {
        Dictionary<string, object?> values = new(StringComparer.OrdinalIgnoreCase);
        foreach (PropertyData property in item.Properties)
        {
            values[property.Name] = property.Value;
        }

        foreach (PropertyData property in item.SystemProperties)
        {
            values[property.Name] = property.Value;
        }

        return values;
    }

    /// <summary>Reads one property of a row, returning null when it is missing.</summary>
    /// <param name="row">The row.</param>
    /// <param name="name">Property name (case-insensitive).</param>
    /// <returns>The raw value or null.</returns>
    public static object? Get(IReadOnlyDictionary<string, object?> row, string name)
    {
        return row.TryGetValue(name, out object? value) ? value : null;
    }

    /// <summary>Reads one property of a live WMI object without throwing.</summary>
    /// <param name="item">The WMI object.</param>
    /// <param name="name">Property name.</param>
    /// <returns>The raw value, or null when the property is missing or unreadable.</returns>
    public static object? GetProperty(ManagementBaseObject item, string name)
    {
        try
        {
            return item.GetPropertyValue(name);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>True when the exception means "namespace or class does not exist" rather than a failure.</summary>
    /// <param name="ex">The exception thrown by System.Management.</param>
    /// <returns>True for invalid namespace, invalid class, and not found.</returns>
    public static bool IsAbsent(Exception ex)
    {
        return ex switch
        {
            ManagementException management => management.ErrorCode is ManagementStatus.InvalidNamespace or ManagementStatus.InvalidClass or ManagementStatus.NotFound,
            COMException com => com.HResult is WbemInvalidNamespace or WbemInvalidClass or WbemNotFound,
            _ => false,
        };
    }

    /// <summary>
    /// What kind of failure an exception from a query is: a provider that could not be loaded
    /// (ManagementStatus.ProviderLoadFailure, or HRESULT 0x80041013), refused access
    /// (ManagementStatus.AccessDenied, HRESULT 0x80041003 or 0x80070005, an
    /// UnauthorizedAccessException), a bound that elapsed (a TimeoutException from a bounded wait,
    /// or ManagementStatus.Timedout from the enumeration's own timeout), or anything else. Absence
    /// is not a failure and is told apart by <see cref="IsAbsent"/> first.
    /// </summary>
    /// <param name="ex">The exception the query caught.</param>
    /// <returns>The kind.</returns>
    internal static WmiFailureKind ClassifyFailure(Exception ex)
    {
        return ex switch
        {
            ManagementException management => management.ErrorCode switch
            {
                ManagementStatus.ProviderLoadFailure => WmiFailureKind.ProviderLoadFailure,
                ManagementStatus.AccessDenied => WmiFailureKind.AccessDenied,
                ManagementStatus.Timedout => WmiFailureKind.TimedOut,
                _ => WmiFailureKind.Other,
            },
            COMException com => com.HResult switch
            {
                WbemProviderLoadFailure => WmiFailureKind.ProviderLoadFailure,
                WbemAccessDenied or AccessDeniedHResult => WmiFailureKind.AccessDenied,
                _ => WmiFailureKind.Other,
            },
            UnauthorizedAccessException => WmiFailureKind.AccessDenied,
            TimeoutException => WmiFailureKind.TimedOut,
            _ => WmiFailureKind.Other,
        };
    }

    /// <summary>One-line description of an exception for the Errors list.</summary>
    /// <param name="ex">The exception.</param>
    /// <returns>Type, WMI status or HRESULT when available, and message.</returns>
    public static string Describe(Exception ex)
    {
        string message = ex.Message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return ex switch
        {
            ManagementException management => $"ManagementException {management.ErrorCode}: {message}",
            COMException com => $"COMException 0x{com.HResult:X8}: {message}",
            _ => $"{ex.GetType().Name}: {message}",
        };
    }

    /// <summary>
    /// Runs a System.Management call that has no timeout of its own on a pool thread and waits at
    /// most <paramref name="timeout"/> for it. A call that finishes in time rethrows its own
    /// exception type, so the <see cref="IsAbsent"/> filters keep working; one that does not
    /// throws <see cref="TimeoutException"/> and is left to finish on its own with its outcome
    /// observed and discarded, so a stuck provider costs the caller the timeout and nothing more.
    /// </summary>
    /// <param name="action">The call to bound.</param>
    /// <param name="timeout">Maximum wait.</param>
    /// <param name="label">What the call does, for the timeout message.</param>
    internal static void RunBounded(Action action, TimeSpan timeout, string label)
    {
        ArgumentNullException.ThrowIfNull(action);
        _ = RunBounded(
            () =>
            {
                action();
                return true;
            },
            timeout,
            label);
    }

    /// <summary>
    /// Runs a System.Management call that returns a value under the same bounded wait as
    /// <see cref="RunBounded(Action, TimeSpan, string)"/>. A read-only WMI method call (for example
    /// a Get* method on Win32_EncryptableVolume) can be bounded this way: InvokeMethodOptions.Timeout
    /// does not bound a synchronous method call.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="call">The call to bound.</param>
    /// <param name="timeout">Maximum wait.</param>
    /// <param name="label">What the call does, for the timeout message.</param>
    /// <returns>The call's result.</returns>
    internal static T RunBounded<T>(Func<T> call, TimeSpan timeout, string label)
    {
        return RunBounded(call, timeout, label, null);
    }

    /// <summary>
    /// Runs a System.Management call that returns a value under the same bounded wait as
    /// <see cref="RunBounded{T}(Func{T}, TimeSpan, string)"/>, for a call whose result holds WMI
    /// objects the caller would dispose: when the call is abandoned after the timeout and still
    /// finishes later, <paramref name="releaseLateResult"/> receives the result nobody else will
    /// see, so its objects are released then instead of waiting for the finalizer.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="call">The call to bound.</param>
    /// <param name="timeout">Maximum wait.</param>
    /// <param name="label">What the call does, for the timeout message.</param>
    /// <param name="releaseLateResult">Releases the result of a call that finished after its timeout, or null; its own exceptions are discarded.</param>
    /// <returns>The call's result.</returns>
    internal static T RunBounded<T>(Func<T> call, TimeSpan timeout, string label, Action<T>? releaseLateResult)
    {
        Task<T> task = Task.Run(call);
        if (Task.WaitAny(new Task[] { task }, timeout) < 0)
        {
            _ = task.ContinueWith(
                finished =>
                {
                    if (finished.IsFaulted)
                    {
                        _ = finished.Exception;
                    }
                    else if (finished.Status == TaskStatus.RanToCompletion && releaseLateResult is not null)
                    {
                        try
                        {
                            releaseLateResult(finished.Result);
                        }
                        catch (Exception)
                        {
                            // Releasing an abandoned result is best effort.
                        }
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            string seconds = timeout.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture);
            throw new TimeoutException("WMI " + label + " did not complete within " + seconds + " seconds");
        }

        return task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Enumeration options for every query: semi-synchronous, forward-only, and each batch bounded
    /// by <see cref="QueryTimeout"/>.
    /// </summary>
    /// <returns>New options.</returns>
    internal static System.Management.EnumerationOptions CreateEnumerationOptions()
    {
        return new System.Management.EnumerationOptions
        {
            ReturnImmediately = true,
            Rewindable = false,
            Timeout = QueryTimeout,
        };
    }
}
