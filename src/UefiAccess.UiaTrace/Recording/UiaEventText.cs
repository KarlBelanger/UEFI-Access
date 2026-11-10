// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Text.RegularExpressions;
using UefiAccess.Core.Diagnostics;

namespace UefiAccess.UiaTrace.Recording;

/// <summary>
/// The names the recorder gives the UI Automation events it receives, as they appear in the
/// details of a UIA-RECV line and in the summary.
/// </summary>
internal static class UiaEventNames
{
    /// <summary>The focus moved to an element of UefiAccess.</summary>
    public const string FocusChanged = "FocusChanged";

    /// <summary>The focus moved to an element of another process; counted, written without a name.</summary>
    public const string FocusChangedOutside = "FocusChanged outside UefiAccess";

    /// <summary>
    /// The focus moved to an element of UefiAccess that is not WPF content: a Win32 dialog of the
    /// application (the folder picker of Open baseline folder, a message box); counted, written
    /// without a name or an automation id, because a folder picker's items are the person's own
    /// folder and file names.
    /// </summary>
    public const string FocusChangedDialog = "FocusChanged UefiAccess dialog";

    /// <summary>SelectionItem pattern: an item was selected and the selection now holds it alone.</summary>
    public const string ElementSelected = "ElementSelected";

    /// <summary>SelectionItem pattern: an item was added to the selection.</summary>
    public const string ElementAddedToSelection = "ElementAddedToSelection";

    /// <summary>SelectionItem pattern: an item was removed from the selection.</summary>
    public const string ElementRemovedFromSelection = "ElementRemovedFromSelection";

    /// <summary>A property the recorder watches changed; the summary counts each property on its own.</summary>
    public const string PropertyChanged = "PropertyChanged";

    /// <summary>Children were added, removed, reordered or invalidated.</summary>
    public const string StructureChanged = "StructureChanged";

    /// <summary>A live region's content changed.</summary>
    public const string LiveRegionChanged = "LiveRegionChanged";

    /// <summary>
    /// The summary's count of the LiveRegionChanged events written with copy=yes
    /// (<see cref="ElementValues"/>): part of the LiveRegionChanged count, so not added to the
    /// total again.
    /// </summary>
    public const string LiveRegionChangedCopies = "LiveRegionChanged copies";

    /// <summary>The watched property whose change carries the element's value (the Status box's sentence).</summary>
    public const string ValueProperty = "Value";

    /// <summary>A notification (text a provider asks the screen reader to speak).</summary>
    public const string Notification = "Notification";

    /// <summary>Asynchronous content (a list filling, for example) was loaded.</summary>
    public const string AsyncContentLoaded = "AsyncContentLoaded";

    /// <summary>The automation property names the recorder watches, as written after "PropertyChanged".</summary>
    public static IReadOnlyList<string> Properties { get; } = new[] { "Name", "Value", "IsSelected", "HasKeyboardFocus", "ExpandCollapseState" };

    /// <summary>The key under which <see cref="EventCounts"/> counts a change of <paramref name="property"/>.</summary>
    /// <param name="property">The property name, for example "IsSelected".</param>
    /// <returns>"PropertyChanged IsSelected".</returns>
    public static string PropertyChangedKey(string property) => PropertyChanged + " " + property;

    /// <summary>
    /// Every event key in the order the summary lists it: focus first, then selection, the
    /// property changes one by one, structure, live region and how many of those were copies,
    /// notification and asynchronous content.
    /// </summary>
    public static IReadOnlyList<string> SummaryOrder { get; } = new[]
    {
        FocusChanged,
        FocusChangedOutside,
        FocusChangedDialog,
        ElementSelected,
        ElementAddedToSelection,
        ElementRemovedFromSelection,
    }
    .Concat(Properties.Select(PropertyChangedKey))
    .Concat(new[] { StructureChanged, LiveRegionChanged, LiveRegionChangedCopies, Notification, AsyncContentLoaded })
    .ToArray();
}

/// <summary>
/// The details text of the recorder's UIA-RECV lines: one event, the element it came from (control
/// type, automation id and accessible name, read from the event's cache) and the event's own data.
/// Pure (no UI Automation types), so the tests compile it as a linked file.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here may put a firmware value into the file beyond what the accessible name of a row
/// already holds. A list row's name (a ListItem) is the row sentence, which UefiAccess itself caps
/// at <see cref="TraceLine.RowNameLength"/> characters, and is written up to that length; every
/// other element's name is cut at <see cref="TraceLine.ShortNameLength"/>. A detail field's value
/// box exposes its value through the Value property, so a Value change is written as text only for
/// the four elements whose value is no firmware data (<see cref="ValueTextAutomationIds"/>: the
/// Status box, the Filter box and the two splitters) and as its length, "(12 characters)", for
/// every other element. A new name is cut where the element's own name would be, except that the
/// Status box's, which carries the announcement, is cut at <see cref="TraceLine.RowNameLength"/>
/// like the application's own ANNOUNCE lines; an allowed value and a notification's text are cut at
/// that length too. The sentence= of a LiveRegionChanged line is a value as its PropertyChanged
/// Value line wrote it (<see cref="ValueText"/>), so it holds nothing that line did not.
/// </para>
/// <para>
/// Focus is subscribed for the whole desktop, so a focus change is written with the element's name
/// only when the element is UefiAccess's own WPF content (<see cref="FocusChanged"/>): focus in
/// another process is written as that process's id, and focus in a dialog of UefiAccess that is
/// not WPF (the folder picker, a message box) as its framework and control type, without a name.
/// </para>
/// <para>
/// The identity strip shows "Service tag: &lt;tag&gt;" for this unit or for a baseline made on
/// another one. The trace file's writer replaces this unit's tag in every line; the text after the
/// "Service tag:" label is replaced here as well (<see cref="RedactServiceTagLabel"/>), so a
/// baseline's tag from another unit never reaches the file either.
/// </para>
/// </remarks>
internal static class UiaEventText
{
    /// <summary>The automation id of the Status box (the announcer's live region).</summary>
    public const string StatusBoxId = "StatusBox";

    /// <summary>The automation id of the Filter box.</summary>
    public const string FilterBoxId = "FilterBox";

    /// <summary>The automation id of the splitter between the list and the detail pane.</summary>
    public const string ListDetailSplitterId = "ListDetailSplitter";

    /// <summary>The automation id of the splitter above the log panel.</summary>
    public const string LogSplitterId = "LogSplitter";

    /// <summary>
    /// The label in front of the service tag in the identity strip, the same as
    /// UefiAccess.Presentation's Strings.ServiceTagLabel (a test keeps the two equal; the recorder
    /// references Core only).
    /// </summary>
    public const string ServiceTagLabel = "Service tag";

    /// <summary>Written in place of the element when it was gone before its cache could be read.</summary>
    public const string UnavailableElement = "(element no longer available)";

    /// <summary>The UI Automation framework id of WPF, the framework of every element of the UefiAccess window.</summary>
    public const string WpfFrameworkId = "WPF";

    /// <summary>The sentence= of a LiveRegionChanged whose element's value is not known: no PropertyChanged Value with a value of it arrived first.</summary>
    public const string UnknownSentence = "(unknown)";

    /// <summary>What a LiveRegionChanged line adds when the event is a copy (<see cref="ElementValues"/>).</summary>
    public const string CopyMark = "copy=yes";

    /// <summary>The control type of a list row.</summary>
    private const string ListItemControlType = "ListItem";

    /// <summary>The label, a colon and the text after it, up to a comma, semicolon or line break.</summary>
    private static readonly Regex ServiceTagLabelPattern = new(
        @"(?<label>" + ServiceTagLabel + @"\s*:\s*)(?<value>[^,;\r\n\t]+)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>The automation ids whose Value changes are written as text; every other element's as its length.</summary>
    public static IReadOnlyList<string> ValueTextAutomationIds { get; } = new[] { StatusBoxId, FilterBoxId, ListDetailSplitterId, LogSplitterId };

    /// <summary>
    /// The element as the line shows it: "&lt;control type&gt; id=&lt;automation id or -&gt;
    /// name=&lt;quoted accessible name&gt;", for example <c>ListItem id=- name="Boot0001, ..."</c>.
    /// </summary>
    /// <param name="facts">What the event's cache held about the element.</param>
    /// <returns>The element text, or <see cref="UnavailableElement"/>.</returns>
    public static string Element(ElementFacts facts)
    {
        if (facts is null || !facts.Available)
        {
            return UnavailableElement;
        }

        string controlType = Word(facts.ControlType);
        string id = string.IsNullOrWhiteSpace(facts.AutomationId) ? "-" : TraceLine.Token(facts.AutomationId);
        int nameLength = IsRow(facts) ? TraceLine.RowNameLength : TraceLine.ShortNameLength;
        return controlType + " id=" + id + " name=" + TraceLine.Quote(RedactServiceTagLabel(facts.Name), nameLength);
    }

    /// <summary>An event with no data of its own: "&lt;event&gt; &lt;element&gt;".</summary>
    /// <param name="eventName">One of the <see cref="UiaEventNames"/>.</param>
    /// <param name="facts">The element the event came from.</param>
    /// <returns>The details text.</returns>
    public static string Event(string eventName, ElementFacts facts) => eventName + " " + Element(facts);

    /// <summary>
    /// A focus change, from the global focus subscription: with the element's name only when it
    /// is UefiAccess's own WPF content. An element that was gone is written as such. An element of
    /// another process, or one whose process was not cached, is written as
    /// <see cref="FocusOutside"/>. An element of UefiAccess whose framework is known and is not WPF
    /// (a Win32 dialog: the folder picker, a message box) is written as
    /// <see cref="FocusInDialog"/>, without its name or automation id. An element of UefiAccess
    /// whose framework was not cached is taken for the window's content, since every dialog's
    /// framework is reported.
    /// </summary>
    /// <param name="facts">The focused element, from the event's cache.</param>
    /// <param name="targetProcessId">The process of the UefiAccess window.</param>
    /// <param name="countKey">The key <see cref="EventCounts"/> counts the event under.</param>
    /// <returns>The details text.</returns>
    public static string FocusChanged(ElementFacts facts, int targetProcessId, out string countKey)
    {
        if (facts is null || !facts.Available)
        {
            countKey = UiaEventNames.FocusChanged;
            return Event(UiaEventNames.FocusChanged, ElementFacts.Unavailable);
        }

        if (facts.ProcessId is not int processId || processId != targetProcessId)
        {
            countKey = UiaEventNames.FocusChangedOutside;
            return FocusOutside(facts.ProcessId);
        }

        if (facts.FrameworkId is not null && !string.Equals(facts.FrameworkId, WpfFrameworkId, StringComparison.Ordinal))
        {
            countKey = UiaEventNames.FocusChangedDialog;
            return FocusInDialog(facts);
        }

        countKey = UiaEventNames.FocusChanged;
        return Event(UiaEventNames.FocusChanged, facts);
    }

    /// <summary>Focus moved to another process: its id only, never the name of what is focused there.</summary>
    /// <param name="processId">The process that owns the focused element, when it was cached.</param>
    /// <returns>"FocusChanged outside UefiAccess (process 1234)".</returns>
    public static string FocusOutside(int? processId) =>
        UiaEventNames.FocusChangedOutside + " (process " + (processId?.ToString(CultureInfo.InvariantCulture) ?? "unknown") + ")";

    /// <summary>Focus moved into a dialog of UefiAccess that is not WPF: its framework and control type only, never a name or an automation id.</summary>
    /// <param name="facts">The focused element.</param>
    /// <returns>"FocusChanged in a UefiAccess dialog (framework Win32) ListItem".</returns>
    public static string FocusInDialog(ElementFacts facts) =>
        "FocusChanged in a UefiAccess dialog (framework " + Word(facts?.FrameworkId) + ") " + Word(facts?.ControlType);

    /// <summary>
    /// A property change: "PropertyChanged &lt;property&gt;=&lt;new value&gt; [(was &lt;old
    /// value&gt;)] &lt;element&gt;". Booleans and enumeration values are written by name, text in
    /// quotes, and a Value change as its length unless the element is one of
    /// <see cref="ValueTextAutomationIds"/>.
    /// </summary>
    /// <param name="property">The property name, one of <see cref="UiaEventNames.Properties"/>.</param>
    /// <param name="oldValue">The old value, when UI Automation supplied it (usually not).</param>
    /// <param name="newValue">The new value.</param>
    /// <param name="facts">The element whose property changed.</param>
    /// <returns>The details text.</returns>
    public static string PropertyChanged(string property, object? oldValue, object? newValue, ElementFacts facts)
    {
        string text = UiaEventNames.PropertyChanged + " " + property + "=" + Value(property, newValue, facts);
        if (oldValue is not null)
        {
            text += " (was " + Value(property, oldValue, facts) + ")";
        }

        return text + " " + Element(facts);
    }

    /// <summary>
    /// A live region change: "LiveRegionChanged sentence=&lt;value&gt; [copy=yes] &lt;element&gt;".
    /// The event carries no text. <paramref name="sentence"/> is the element's value as the last
    /// PropertyChanged Value with a value before the event gave it, already written as that line
    /// wrote it (<see cref="ValueText"/>), or null for <see cref="UnknownSentence"/>; the element's
    /// name, last, is the one UI Automation read when the event arrived, which can be a later
    /// sentence. The element stays last, as on every other line.
    /// </summary>
    /// <param name="sentence">The element's last value as <see cref="ValueText"/> wrote it, or null when none is known.</param>
    /// <param name="copy">True when the event followed a PropertyChanged Value=(none) of the same element.</param>
    /// <param name="facts">The element the event came from.</param>
    /// <returns>The details text.</returns>
    public static string LiveRegionChanged(string? sentence, bool copy, ElementFacts facts) =>
        UiaEventNames.LiveRegionChanged
        + " sentence=" + (string.IsNullOrEmpty(sentence) ? UnknownSentence : sentence)
        + (copy ? " " + CopyMark : string.Empty)
        + " " + Element(facts);

    /// <summary>
    /// A value as a PropertyChanged Value line writes it: quoted text, the text after a "Service
    /// tag:" label replaced and cut at <see cref="TraceLine.RowNameLength"/>, for the elements of
    /// <see cref="ValueTextAutomationIds"/>; "(12 characters)" for any other; "(none)" for null.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="facts">The element whose value it is.</param>
    /// <returns>The value text.</returns>
    public static string ValueText(object? value, ElementFacts facts) => Value(UiaEventNames.ValueProperty, value, facts);

    /// <summary>A structure change: "StructureChanged &lt;change&gt; [child=&lt;runtime id&gt;] &lt;element&gt;".</summary>
    /// <param name="changeType">The StructureChangeType by name, for example "ChildAdded".</param>
    /// <param name="runtimeId">The runtime id UI Automation supplied with the change (the child's, or the parent's for a bulk change), or null.</param>
    /// <param name="facts">The element the event came from.</param>
    /// <returns>The details text.</returns>
    public static string StructureChanged(string changeType, IReadOnlyList<int>? runtimeId, ElementFacts facts)
    {
        string child = runtimeId is { Count: > 0 }
            ? " child=" + string.Join(".", runtimeId.Select(part => part.ToString(CultureInfo.InvariantCulture)))
            : string.Empty;
        return UiaEventNames.StructureChanged + " " + Word(changeType) + child + " " + Element(facts);
    }

    /// <summary>A notification: "Notification kind=&lt;kind&gt; processing=&lt;processing&gt; text=&lt;quoted&gt; activity=&lt;quoted&gt; &lt;element&gt;".</summary>
    /// <param name="kind">The notification kind by name.</param>
    /// <param name="processing">The notification processing by name.</param>
    /// <param name="displayString">The text to speak.</param>
    /// <param name="activityId">The activity id.</param>
    /// <param name="facts">The element the event came from.</param>
    /// <returns>The details text.</returns>
    public static string Notification(string kind, string processing, string? displayString, string? activityId, ElementFacts facts) =>
        UiaEventNames.Notification
        + " kind=" + Word(kind)
        + " processing=" + Word(processing)
        + " text=" + TraceLine.Quote(RedactServiceTagLabel(displayString), TraceLine.RowNameLength)
        + " activity=" + TraceLine.Quote(activityId, TraceLine.ShortNameLength)
        + " " + Element(facts);

    /// <summary>Asynchronous content: "AsyncContentLoaded &lt;state&gt; &lt;percent&gt;% &lt;element&gt;".</summary>
    /// <param name="state">The AsyncContentLoadedState by name.</param>
    /// <param name="percentComplete">The percentage complete.</param>
    /// <param name="facts">The element the event came from.</param>
    /// <returns>The details text.</returns>
    public static string AsyncContentLoaded(string state, double percentComplete, ElementFacts facts) =>
        UiaEventNames.AsyncContentLoaded + " " + Word(state) + " " + percentComplete.ToString("0", CultureInfo.InvariantCulture) + "% " + Element(facts);

    /// <summary>True when a Value change of the element is written as text (<see cref="ValueTextAutomationIds"/>).</summary>
    /// <param name="automationId">The element's automation id.</param>
    /// <returns>True for the Status box, the Filter box and the two splitters.</returns>
    public static bool ShowsValueText(string? automationId) =>
        automationId is not null && ValueTextAutomationIds.Contains(automationId, StringComparer.Ordinal);

    /// <summary>
    /// Replaces the text after every "Service tag:" label with <see cref="TraceRedactor.Replacement"/>,
    /// whatever the tag, so a tag shown from a baseline of another unit is never written.
    /// </summary>
    /// <param name="text">The text, or null.</param>
    /// <returns>The text with the labelled tag replaced; null stays null.</returns>
    public static string? RedactServiceTagLabel(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf(ServiceTagLabel, StringComparison.OrdinalIgnoreCase) < 0)
        {
            return text;
        }

        return ServiceTagLabelPattern.Replace(text, match => match.Groups["label"].Value + TraceRedactor.Replacement);
    }

    /// <summary>A property value as the line shows it.</summary>
    private static string Value(string property, object? value, ElementFacts facts)
    {
        switch (value)
        {
            case null:
                return "(none)";
            case string text when string.Equals(property, "Value", StringComparison.Ordinal) && !ShowsValueText(facts.AutomationId):
                return "(" + text.Length.ToString(CultureInfo.InvariantCulture) + (text.Length == 1 ? " character)" : " characters)");
            case string text when string.Equals(property, "Name", StringComparison.Ordinal):
                // Cut where the element's own name is (the Status box's carries the announcement).
                bool longName = IsRow(facts) || string.Equals(facts.AutomationId, StatusBoxId, StringComparison.Ordinal);
                return TraceLine.Quote(RedactServiceTagLabel(text), longName ? TraceLine.RowNameLength : TraceLine.ShortNameLength);
            case string text:
                return TraceLine.Quote(RedactServiceTagLabel(text), TraceLine.RowNameLength);
            case bool flag:
                return flag ? "True" : "False";
            case Enum named:
                return Word(named.ToString());
            case int number when string.Equals(property, "ExpandCollapseState", StringComparison.Ordinal):
                return ExpandCollapseStateName(number);
            case IFormattable formattable:
                return Word(formattable.ToString(null, CultureInfo.InvariantCulture));
            default:
                // Not text and not a number: never the Value of a detail field, which is always text.
                return Word(value.GetType().Name);
        }
    }

    /// <summary>True for a list row (a ListItem), whose name is the row sentence UefiAccess caps at <see cref="TraceLine.RowNameLength"/>.</summary>
    private static bool IsRow(ElementFacts facts) => string.Equals(facts.ControlType, ListItemControlType, StringComparison.Ordinal);

    /// <summary>The ExpandCollapseState names, for a value that arrives as its number.</summary>
    private static string ExpandCollapseStateName(int value) => value switch
    {
        0 => "Collapsed",
        1 => "Expanded",
        2 => "PartiallyExpanded",
        3 => "LeafNode",
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>A single word (an enumeration name, a control type): cleaned, inner spaces kept out.</summary>
    private static string Word(string? text)
    {
        string cleaned = TraceLine.Clean(text);
        return cleaned.Length == 0 ? "?" : Cut(cleaned.Replace(' ', '_'), TraceLine.ShortNameLength);
    }

    /// <summary>Cuts text to <paramref name="maxLength"/> characters with a final ellipsis, never inside a surrogate pair.</summary>
    private static string Cut(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        int keep = Math.Max(0, maxLength - 1);
        if (keep > 0 && char.IsHighSurrogate(text[keep - 1]))
        {
            keep--;
        }

        return text[..keep] + "\u2026";
    }
}
