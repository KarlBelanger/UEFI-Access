// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Concurrent;
using System.Globalization;

namespace UefiAccess.UiaTrace.Recording;

/// <summary>
/// How many events of each kind the recorder received: per event, per watched property for
/// property changes, and focus that moved outside UefiAccess on its own; and, within the
/// LiveRegionChanged events, how many were copies (copy=yes, <see cref="ElementValues"/>). UI
/// Automation calls the handlers on threads of its own, several at once, so every member is
/// thread-safe. The summary lists the known events in a fixed order
/// (<see cref="UiaEventNames.SummaryOrder"/>), zeros included, so two runs compare line by line;
/// anything else follows in ordinal order.
/// </summary>
internal sealed class EventCounts
{
    /// <summary>The key under which a handler that failed is counted.</summary>
    public const string HandlerErrors = "handler errors";

    private readonly ConcurrentDictionary<string, long> _counts = new(StringComparer.Ordinal);

    /// <summary>The number of events received, handler errors not included, and the copies only once, as LiveRegionChanged events.</summary>
    public long Total => _counts.Where(pair => IsEventCount(pair.Key)).Sum(pair => pair.Value);

    /// <summary>Counts one event.</summary>
    /// <param name="key">One of the <see cref="UiaEventNames"/>, or any other key.</param>
    public void Add(string key)
    {
        _counts.AddOrUpdate(string.IsNullOrEmpty(key) ? "?" : key, 1, (_, count) => count + 1);
    }

    /// <summary>Counts one change of a watched property.</summary>
    /// <param name="property">The property name, for example "IsSelected".</param>
    public void AddPropertyChange(string property) => Add(UiaEventNames.PropertyChangedKey(property));

    /// <summary>Counts focus that moved to an element of another process.</summary>
    public void AddFocusOutside() => Add(UiaEventNames.FocusChangedOutside);

    /// <summary>Counts a handler that could not write its event (it never throws back into UI Automation).</summary>
    public void AddHandlerError() => Add(HandlerErrors);

    /// <summary>Counts a LiveRegionChanged written with copy=yes; the event itself is counted as LiveRegionChanged as well.</summary>
    public void AddLiveRegionCopy() => Add(UiaEventNames.LiveRegionChangedCopies);

    /// <summary>The count of one key; 0 when none was received.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The count.</returns>
    public long Get(string key) => _counts.TryGetValue(key, out long count) ? count : 0;

    /// <summary>
    /// The summary, one line per entry, each starting with "recorder:" so it stays recognisable
    /// after the recorder's file is merged with the application's: first the time recorded and the
    /// total, then every key of <see cref="UiaEventNames.SummaryOrder"/> with its count (the
    /// copies right after LiveRegionChanged, of which they are a part), then any other key in
    /// ordinal order, and the handler errors last when there were any.
    /// </summary>
    /// <param name="recorded">How long the recorder was subscribed.</param>
    /// <returns>The lines, for SUMMARY lines of the trace and for the console.</returns>
    public IReadOnlyList<string> SummaryLines(TimeSpan recorded)
    {
        KeyValuePair<string, long>[] snapshot = _counts.ToArray();
        Dictionary<string, long> counts = snapshot.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        long total = snapshot.Where(pair => IsEventCount(pair.Key)).Sum(pair => pair.Value);

        List<string> lines = new()
        {
            "recorder: recorded "
                + Math.Max(0, recorded.TotalSeconds).ToString("F1", CultureInfo.InvariantCulture)
                + " s, " + total.ToString(CultureInfo.InvariantCulture) + " events received",
        };

        foreach (string key in UiaEventNames.SummaryOrder)
        {
            lines.Add(Line(key, counts.TryGetValue(key, out long count) ? count : 0));
        }

        foreach (string key in counts.Keys
            .Where(key => key != HandlerErrors && !UiaEventNames.SummaryOrder.Contains(key, StringComparer.Ordinal))
            .OrderBy(key => key, StringComparer.Ordinal))
        {
            lines.Add(Line(key, counts[key]));
        }

        if (counts.TryGetValue(HandlerErrors, out long errors) && errors > 0)
        {
            lines.Add(Line(HandlerErrors, errors));
        }

        return lines;
    }

    private static string Line(string key, long count) => "recorder: " + key + " " + count.ToString(CultureInfo.InvariantCulture);

    /// <summary>True for a key that counts events received: not the handler errors, and not the copies, which are LiveRegionChanged events counted there already.</summary>
    private static bool IsEventCount(string key) => key != HandlerErrors && key != UiaEventNames.LiveRegionChangedCopies;
}
