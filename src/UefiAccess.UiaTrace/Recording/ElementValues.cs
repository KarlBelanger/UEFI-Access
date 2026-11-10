// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;

namespace UefiAccess.UiaTrace.Recording;

/// <summary>
/// What the recorder's own event stream shows of each element's value, for its LiveRegionChanged
/// lines. A LiveRegionChanged event carries no text: a client reads the element's name when it
/// handles the event, and so does the recorder's cache, so the name on the line is the one the
/// element had when the event arrived. After a variable list load the window's events reach other
/// processes late, by up to about six seconds in the A/B runs of 2 October 2026, so that name can
/// be a later sentence (late reads, measured in M4-H4). A PropertyChanged Value event does carry
/// its value. So, per element, this keeps the last value received in a PropertyChanged Value
/// event that carried one, and gives it with each LiveRegionChanged of that element as
/// sentence="...": the sentence the element held when the event was raised, as far as the event
/// stream shows. The name stays on the line as before.
/// </summary>
/// <remarks>
/// <para>
/// Once the Open baseline folder dialog has been opened in UefiAccess, even if cancelled, most UI
/// Automation events of the window reach another process a second time, the copy without a
/// value: PropertyChanged Value=(none), then LiveRegionChanged (measured in M4-H4 on 2 October
/// 2026, on the build before M4 as on M4's; the Status box's value, name and live-region events
/// were copied, the focus events and the baseline load's own Loaded sentence were not). A
/// LiveRegionChanged whose element's previous event here was a PropertyChanged Value=(none) is a
/// copy (copy=yes on its line, counted as
/// <see cref="UiaEventNames.LiveRegionChangedCopies"/>). Any other property change of the element
/// ends the mark first, so a Value=(none) that no LiveRegionChanged follows (UefiAccess raises none
/// while the Status box has keyboard focus and its text changes) marks no later event.
/// </para>
/// <para>
/// An element is told apart by its process, control type and automation id. One without an
/// automation id (a list row) is not followed: its LiveRegionChanged gets
/// <see cref="UiaEventText.UnknownSentence"/>, as does one whose element had no Value with a value
/// yet (with --events without properties, for example), and is never marked a copy. A value is
/// kept as the PropertyChanged Value line wrote it (<see cref="UiaEventText.ValueText"/>): as text
/// for the Status box, the Filter box and the two splitters, with the text after a "Service tag:"
/// label replaced, and as its length for any other element; the trace file's writer replaces this
/// unit's service tag in it as in the rest of the line.
/// </para>
/// <para>
/// UI Automation calls the handlers on threads of its own, so every member is thread-safe; the
/// order is the order the handlers reach this object. Pure (no UI Automation types), so the tests
/// compile it as a linked file.
/// </para>
/// </remarks>
internal sealed class ElementValues
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Remembered> _elements = new(StringComparer.Ordinal);

    /// <summary>
    /// A property change: notes it for the element and returns the details text, the same as
    /// <see cref="UiaEventText.PropertyChanged"/>. A Value with a value becomes the element's
    /// sentence and ends a copy mark; a Value without one, "(none)", marks the element's next
    /// event a copy; any other property ends the mark.
    /// </summary>
    /// <param name="property">The property name, one of <see cref="UiaEventNames.Properties"/>.</param>
    /// <param name="oldValue">The old value, when UI Automation supplied it.</param>
    /// <param name="newValue">The new value; null for a change that carried none.</param>
    /// <param name="facts">The element whose property changed, from the event's cache.</param>
    /// <returns>The details text.</returns>
    public string PropertyChanged(string property, object? oldValue, object? newValue, ElementFacts facts)
    {
        string? key = Key(facts);
        if (key is not null)
        {
            bool value = string.Equals(property, UiaEventNames.ValueProperty, StringComparison.Ordinal);
            string? sentence = value && newValue is not null ? UiaEventText.ValueText(newValue, facts) : null;
            lock (_gate)
            {
                if (value)
                {
                    if (!_elements.TryGetValue(key, out Remembered? element))
                    {
                        element = new Remembered();
                        _elements.Add(key, element);
                    }

                    if (sentence is null)
                    {
                        element.AfterValueless = true;
                    }
                    else
                    {
                        element.Sentence = sentence;
                        element.AfterValueless = false;
                    }
                }
                else if (_elements.TryGetValue(key, out Remembered? known))
                {
                    known.AfterValueless = false;
                }
            }
        }

        return UiaEventText.PropertyChanged(property, oldValue, newValue, facts);
    }

    /// <summary>
    /// A live region change: the details text with the element's last value as its sentence
    /// (<see cref="UiaEventText.LiveRegionChanged"/>), and whether it is a copy, that is whether
    /// the element's previous event here was a PropertyChanged Value=(none). Ends the copy mark.
    /// </summary>
    /// <param name="facts">The element the event came from, from the event's cache.</param>
    /// <param name="copy">True when the event is a copy.</param>
    /// <returns>The details text.</returns>
    public string LiveRegionChanged(ElementFacts facts, out bool copy)
    {
        string? sentence = null;
        copy = false;
        string? key = Key(facts);
        if (key is not null)
        {
            lock (_gate)
            {
                if (_elements.TryGetValue(key, out Remembered? known))
                {
                    sentence = known.Sentence;
                    copy = known.AfterValueless;
                    known.AfterValueless = false;
                }
            }
        }

        return UiaEventText.LiveRegionChanged(sentence, copy, facts);
    }

    /// <summary>The element's key: its process, control type and automation id; null for an element without an automation id, or one that was gone.</summary>
    private static string? Key(ElementFacts? facts)
    {
        if (facts is null || !facts.Available || string.IsNullOrWhiteSpace(facts.AutomationId))
        {
            return null;
        }

        return (facts.ProcessId?.ToString(CultureInfo.InvariantCulture) ?? "?") + "|" + facts.ControlType + "|" + facts.AutomationId;
    }

    /// <summary>What is kept of one element.</summary>
    private sealed class Remembered
    {
        /// <summary>The last value with a value, as its PropertyChanged Value line wrote it; null before the first.</summary>
        public string? Sentence { get; set; }

        /// <summary>True when the element's last event here was a PropertyChanged Value=(none).</summary>
        public bool AfterValueless { get; set; }
    }
}
