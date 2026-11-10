// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Diagnostics;
using UefiAccess.Presentation.Resources;
using UefiAccess.UiaTrace.Recording;
using Xunit;

namespace UefiAccess.Core.Tests.Recorder;

/// <summary>
/// The details of the recorder's UIA-RECV lines (<see cref="UiaEventText"/>): the element as
/// "&lt;control type&gt; id=&lt;automation id or -&gt; name=&lt;quoted name&gt;", a list row's name cut at
/// 200 characters and any other at 80, control characters collapsed and inner quotes doubled; focus
/// outside UefiAccess, and in a dialog of UefiAccess that is not WPF, without a name; a new name cut
/// where the element's own name is; property values by name; a Value change as text only for the
/// Status box, the Filter box and the two splitters, so a detail field's firmware value never
/// reaches the file; a LiveRegionChanged with its sentence and copy mark before the element, the
/// sentence written as the Value line writes a value; and the text after a "Service tag:" label
/// replaced whatever the tag. Synthetic tags only.
/// </summary>
public sealed class UiaEventTextTests
{
    private static readonly ElementFacts Row = new("ListItem", null, "Boot0001, EFI global variable, NV BS RT, 62 bytes, load option: Windows Boot Manager", 4242, true);

    private static readonly ElementFacts StatusBox = new("Edit", "StatusBox", "Status", 4242, true);

    [Fact]
    public void Element_ShowsControlTypeIdAndQuotedName()
    {
        Assert.Equal(
            "ListItem id=- name=\"Boot0001, EFI global variable, NV BS RT, 62 bytes, load option: Windows Boot Manager\"",
            UiaEventText.Element(Row));
        Assert.Equal("Edit id=StatusBox name=\"Status\"", UiaEventText.Element(StatusBox));
        Assert.Equal("? id=- name=\"\"", UiaEventText.Element(new ElementFacts(null, "", null, null, true)));
    }

    [Fact]
    public void Element_ThatWasGone_SaysSo()
    {
        Assert.Equal(UiaEventText.UnavailableElement, UiaEventText.Element(ElementFacts.Unavailable));
        Assert.Equal("ElementSelected " + UiaEventText.UnavailableElement, UiaEventText.Event(UiaEventNames.ElementSelected, ElementFacts.Unavailable));
    }

    [Fact]
    public void Element_ARowNameIsCutAt200_AnyOtherAt80()
    {
        string longName = new('x', 300);

        string row = UiaEventText.Element(new ElementFacts("ListItem", null, longName, 1, true));
        string button = UiaEventText.Element(new ElementFacts("Button", "RefreshButton", longName, 1, true));

        Assert.Equal("ListItem id=- name=\"" + new string('x', 199) + "\u2026\"", row);
        Assert.Equal("Button id=RefreshButton name=\"" + new string('x', 79) + "\u2026\"", button);
    }

    [Fact]
    public void Element_CollapsesControlCharactersAndDoublesQuotes()
    {
        string text = UiaEventText.Element(new ElementFacts("Text", "Detail Heading", "Line one\r\n\tLine \"two\"", 1, true));

        Assert.Equal("Text id=\"Detail Heading\" name=\"Line one Line \"\"two\"\"\"", text);
        Assert.DoesNotContain('\t', text);
        Assert.DoesNotContain('\n', text);
    }

    [Fact]
    public void Event_PutsTheEventNameBeforeTheElement()
    {
        Assert.Equal("ElementSelected " + UiaEventText.Element(Row), UiaEventText.Event(UiaEventNames.ElementSelected, Row));
        Assert.Equal("FocusChanged " + UiaEventText.Element(Row), UiaEventText.Event(UiaEventNames.FocusChanged, Row));
        Assert.Equal("LiveRegionChanged " + UiaEventText.Element(StatusBox), UiaEventText.Event(UiaEventNames.LiveRegionChanged, StatusBox));
    }

    [Fact]
    public void FocusOutside_NamesTheProcessOnly()
    {
        Assert.Equal("FocusChanged outside UefiAccess (process 1234)", UiaEventText.FocusOutside(1234));
        Assert.Equal("FocusChanged outside UefiAccess (process unknown)", UiaEventText.FocusOutside(null));
        Assert.DoesNotContain("name=", UiaEventText.FocusOutside(1234), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("StatusBox")]
    [InlineData("FilterBox")]
    [InlineData("ListDetailSplitter")]
    [InlineData("LogSplitter")]
    public void PropertyChanged_Value_IsShownAsTextForTheAllowedElements(string automationId)
    {
        ElementFacts element = new("Edit", automationId, "Element", 1, true);

        string text = UiaEventText.PropertyChanged("Value", null, "Loaded 12 variables.", element);

        Assert.Equal("PropertyChanged Value=\"Loaded 12 variables.\" Edit id=" + automationId + " name=\"Element\"", text);
        Assert.True(UiaEventText.ShowsValueText(automationId));
    }

    [Fact]
    public void PropertyChanged_Value_OfAnyOtherElement_IsItsLengthOnly()
    {
        ElementFacts field = new("Edit", null, "Data", 1, true);

        string text = UiaEventText.PropertyChanged("Value", "AABBCCDDEEFF", "0123456789AB", field);

        Assert.Equal("PropertyChanged Value=(12 characters) (was (12 characters)) Edit id=- name=\"Data\"", text);
        Assert.DoesNotContain("0123456789AB", text, StringComparison.Ordinal);
        Assert.DoesNotContain("AABBCCDDEEFF", text, StringComparison.Ordinal);
        Assert.Contains("Value=(1 character)", UiaEventText.PropertyChanged("Value", null, "7", field), StringComparison.Ordinal);
        Assert.False(UiaEventText.ShowsValueText(null));
        Assert.False(UiaEventText.ShowsValueText("statusbox"));
        Assert.False(UiaEventText.ShowsValueText("ReadOnlyStatusBox"));
    }

    [Fact]
    public void PropertyChanged_BooleansAndEnumerationsAreShownByName()
    {
        Assert.Equal("PropertyChanged IsSelected=True " + UiaEventText.Element(Row), UiaEventText.PropertyChanged("IsSelected", null, true, Row));
        Assert.Equal("PropertyChanged HasKeyboardFocus=False " + UiaEventText.Element(Row), UiaEventText.PropertyChanged("HasKeyboardFocus", null, false, Row));
        Assert.StartsWith("PropertyChanged ExpandCollapseState=Expanded ", UiaEventText.PropertyChanged("ExpandCollapseState", null, ExpandCollapseStateForTest.Expanded, Row), StringComparison.Ordinal);
        Assert.StartsWith("PropertyChanged ExpandCollapseState=LeafNode ", UiaEventText.PropertyChanged("ExpandCollapseState", null, 3, Row), StringComparison.Ordinal);
        Assert.StartsWith("PropertyChanged ExpandCollapseState=Collapsed ", UiaEventText.PropertyChanged("ExpandCollapseState", null, 0, Row), StringComparison.Ordinal);
        Assert.StartsWith("PropertyChanged IsSelected=(none) ", UiaEventText.PropertyChanged("IsSelected", null, null, Row), StringComparison.Ordinal);
    }

    [Fact]
    public void PropertyChanged_Name_IsCutWhereTheElementsOwnNameIs()
    {
        string newName = "Boot0002, " + new string('y', 300);

        // Any element but a row and the Status box: at 80, as its name in the element text.
        string text = UiaEventText.PropertyChanged("Name", null, newName, new ElementFacts("Text", "DetailHeading", "Boot0002", 1, true));
        Assert.StartsWith("PropertyChanged Name=\"Boot0002, " + new string('y', 69) + "\u2026\" Text id=DetailHeading", text, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('y', 70), text, StringComparison.Ordinal);

        // A row, at 200 (the cap of its accessible name).
        string row = UiaEventText.PropertyChanged("Name", null, newName, Row);
        Assert.StartsWith("PropertyChanged Name=\"Boot0002, " + new string('y', 189) + "\u2026\" ListItem", row, StringComparison.Ordinal);

        // The Status box, whose name carries the announcement, at 200 like an ANNOUNCE line.
        string status = UiaEventText.PropertyChanged("Name", null, newName, StatusBox);
        Assert.StartsWith("PropertyChanged Name=\"Boot0002, " + new string('y', 189) + "\u2026\" Edit id=StatusBox", status, StringComparison.Ordinal);
    }

    [Fact]
    public void FocusChanged_NamesOnlyTheWpfContentOfUefiAccess()
    {
        const int App = 4242;
        ElementFacts row = Row with { FrameworkId = "WPF" };

        Assert.Equal(UiaEventText.Event(UiaEventNames.FocusChanged, row), UiaEventText.FocusChanged(row, App, out string key));
        Assert.Equal(UiaEventNames.FocusChanged, key);

        // The framework not cached: taken for the window's content (every dialog reports its own).
        Assert.Equal(UiaEventText.Event(UiaEventNames.FocusChanged, Row), UiaEventText.FocusChanged(Row, App, out key));
        Assert.Equal(UiaEventNames.FocusChanged, key);

        // Another process, or one that was not cached: the process only.
        ElementFacts elsewhere = new("ListItem", "42", "Inbox - a private subject", 777, true) { FrameworkId = "DirectUI" };
        Assert.Equal("FocusChanged outside UefiAccess (process 777)", UiaEventText.FocusChanged(elsewhere, App, out key));
        Assert.Equal(UiaEventNames.FocusChangedOutside, key);
        ElementFacts unknown = new("ListItem", "42", "Inbox - a private subject", null, true) { FrameworkId = "WPF" };
        Assert.Equal("FocusChanged outside UefiAccess (process unknown)", UiaEventText.FocusChanged(unknown, App, out key));
        Assert.Equal(UiaEventNames.FocusChangedOutside, key);

        // A dialog of UefiAccess (the folder picker): framework and control type, no name, no id.
        ElementFacts picker = new("ListItem", "System.ItemNameDisplay", "C:\\Users\\someone\\Documents", App, true) { FrameworkId = "DirectUI" };
        string dialog = UiaEventText.FocusChanged(picker, App, out key);
        Assert.Equal("FocusChanged in a UefiAccess dialog (framework DirectUI) ListItem", dialog);
        Assert.Equal(UiaEventNames.FocusChangedDialog, key);
        ElementFacts box = new("Text", "65535", "The baseline folder holds no index.json", App, true) { FrameworkId = "Win32" };
        Assert.Equal("FocusChanged in a UefiAccess dialog (framework Win32) Text", UiaEventText.FocusChanged(box, App, out key));

        foreach (string text in new[] { UiaEventText.FocusChanged(elsewhere, App, out _), UiaEventText.FocusChanged(unknown, App, out _), dialog, UiaEventText.FocusChanged(box, App, out _) })
        {
            Assert.DoesNotContain("name=", text, StringComparison.Ordinal);
            Assert.DoesNotContain("id=", text, StringComparison.Ordinal);
            Assert.DoesNotContain("private", text, StringComparison.Ordinal);
            Assert.DoesNotContain("someone", text, StringComparison.Ordinal);
        }

        // An element that was gone: said so, counted as a focus change, no name.
        Assert.Equal("FocusChanged " + UiaEventText.UnavailableElement, UiaEventText.FocusChanged(ElementFacts.Unavailable, App, out key));
        Assert.Equal(UiaEventNames.FocusChanged, key);
        Assert.Equal("WPF", UiaEventText.WpfFrameworkId);
    }

    [Fact]
    public void StructureChanged_ShowsTheChangeAndTheRuntimeId()
    {
        ElementFacts list = new("List", "GroupsControl", "Variables", 1, true);

        Assert.Equal("StructureChanged ChildAdded child=42.7.3 List id=GroupsControl name=\"Variables\"", UiaEventText.StructureChanged("ChildAdded", new[] { 42, 7, 3 }, list));
        Assert.Equal("StructureChanged ChildrenInvalidated List id=GroupsControl name=\"Variables\"", UiaEventText.StructureChanged("ChildrenInvalidated", null, list));
        Assert.Equal("StructureChanged ChildrenReordered List id=GroupsControl name=\"Variables\"", UiaEventText.StructureChanged("ChildrenReordered", Array.Empty<int>(), list));
    }

    [Fact]
    public void Notification_ShowsKindProcessingTextAndActivity()
    {
        string text = UiaEventText.Notification("ActionCompleted", "ImportantMostRecent", "Copied.\r\nDone", "copy", StatusBox);

        Assert.Equal("Notification kind=ActionCompleted processing=ImportantMostRecent text=\"Copied. Done\" activity=\"copy\" Edit id=StatusBox name=\"Status\"", text);
        Assert.Contains("text=\"\" activity=\"\"", UiaEventText.Notification("Other", "All", null, null, StatusBox), StringComparison.Ordinal);
    }

    [Fact]
    public void AsyncContentLoaded_ShowsStateAndPercent()
    {
        Assert.Equal("AsyncContentLoaded Completed 100% " + UiaEventText.Element(Row), UiaEventText.AsyncContentLoaded("Completed", 100.0, Row));
        Assert.StartsWith("AsyncContentLoaded Progress 43% ", UiaEventText.AsyncContentLoaded("Progress", 42.6, Row), StringComparison.Ordinal);
    }

    [Fact]
    public void ServiceTagLabel_IsTheIdentityStripsLabel()
    {
        Assert.Equal(Strings.ServiceTagLabel, UiaEventText.ServiceTagLabel);
    }

    [Fact]
    public void TheTextAfterAServiceTagLabel_IsReplacedWhateverTheTag()
    {
        ElementFacts strip = new("Text", null, "Service tag: TESTTAG7", 1, true);

        string element = UiaEventText.Element(strip);
        string nameChange = UiaEventText.PropertyChanged("Name", null, "Service tag: OTHER99", strip);
        string notification = UiaEventText.Notification("Other", "All", "service TAG:abcd123; model 7020", null, StatusBox);

        Assert.Equal("Text id=- name=\"Service tag: [service tag]\"", element);
        Assert.Contains("Name=\"Service tag: [service tag]\"", nameChange, StringComparison.Ordinal);
        Assert.Contains("text=\"service TAG:[service tag]; model 7020\"", notification, StringComparison.Ordinal);
        foreach (string text in new[] { element, nameChange, notification })
        {
            Assert.DoesNotContain("TESTTAG7", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("OTHER99", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("abcd123", text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Null(UiaEventText.RedactServiceTagLabel(null));
        Assert.Equal("Boot0001", UiaEventText.RedactServiceTagLabel("Boot0001"));
        Assert.Equal("Service tag: " + TraceRedactor.Replacement, UiaEventText.RedactServiceTagLabel("Service tag: not read yet"));
    }

    [Fact]
    public void TheAllowedValueElements_AreTheShellsOwnNames()
    {
        // The ids are the x:Name values of MainWindow.xaml, which WPF exposes as the AutomationId.
        string window = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "src", "UefiAccess.App", "MainWindow.xaml"));

        Assert.Equal(new[] { "StatusBox", "FilterBox", "ListDetailSplitter", "LogSplitter" }, UiaEventText.ValueTextAutomationIds);
        foreach (string id in UiaEventText.ValueTextAutomationIds)
        {
            Assert.Contains("x:Name=\"" + id + "\"", window, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NoDetailsTextHoldsATab()
    {
        ElementFacts tabbed = new("List\tItem", "a\tb", "x\ty", 1, true);

        string[] texts =
        {
            UiaEventText.Element(tabbed),
            UiaEventText.PropertyChanged("Name", "o\tld", "n\tew", tabbed),
            UiaEventText.StructureChanged("Child\tAdded", new[] { 1 }, tabbed),
            UiaEventText.Notification("A\tB", "C\tD", "e\tf", "g\th", tabbed),
            UiaEventText.AsyncContentLoaded("S\tT", 1, tabbed),
            UiaEventText.LiveRegionChanged(UiaEventText.ValueText("s\tentence", StatusBox), true, tabbed),
        };

        Assert.All(texts, text => Assert.DoesNotContain('\t', text));
    }

    [Fact]
    public void SummaryOrder_ListsEveryEventAndEveryWatchedProperty()
    {
        Assert.Equal(new[] { "Name", "Value", "IsSelected", "HasKeyboardFocus", "ExpandCollapseState" }, UiaEventNames.Properties);
        Assert.Equal(
            new[]
            {
                "FocusChanged",
                "FocusChanged outside UefiAccess",
                "FocusChanged UefiAccess dialog",
                "ElementSelected",
                "ElementAddedToSelection",
                "ElementRemovedFromSelection",
                "PropertyChanged Name",
                "PropertyChanged Value",
                "PropertyChanged IsSelected",
                "PropertyChanged HasKeyboardFocus",
                "PropertyChanged ExpandCollapseState",
                "StructureChanged",
                "LiveRegionChanged",
                "LiveRegionChanged copies",
                "Notification",
                "AsyncContentLoaded",
            },
            UiaEventNames.SummaryOrder);
    }

    [Fact]
    public void LiveRegionChanged_PutsTheSentenceAndTheCopyMarkBeforeTheElement()
    {
        ElementFacts status = new("Edit", "StatusBox", "Status: Detail region", 4242, true);
        string sentence = UiaEventText.ValueText("Loading variables, please wait.", status);

        Assert.Equal("\"Loading variables, please wait.\"", sentence);
        Assert.Equal(
            "LiveRegionChanged sentence=\"Loading variables, please wait.\" Edit id=StatusBox name=\"Status: Detail region\"",
            UiaEventText.LiveRegionChanged(sentence, false, status));
        Assert.Equal(
            "LiveRegionChanged sentence=\"Loading variables, please wait.\" copy=yes Edit id=StatusBox name=\"Status: Detail region\"",
            UiaEventText.LiveRegionChanged(sentence, true, status));
        Assert.Equal("LiveRegionChanged sentence=(unknown) Edit id=StatusBox name=\"Status: Detail region\"", UiaEventText.LiveRegionChanged(null, false, status));
        Assert.Equal("LiveRegionChanged sentence=(unknown) copy=yes " + UiaEventText.UnavailableElement, UiaEventText.LiveRegionChanged(string.Empty, true, ElementFacts.Unavailable));

        // The element stays last, so a reader that takes it from the end of the line still finds it.
        Assert.EndsWith(" " + UiaEventText.Element(status), UiaEventText.LiveRegionChanged(sentence, true, status), StringComparison.Ordinal);

        // A value is written as its PropertyChanged Value line writes it.
        Assert.Equal("(none)", UiaEventText.ValueText(null, status));
        Assert.Equal("(3 characters)", UiaEventText.ValueText("abc", new ElementFacts("Edit", "DellSourceBox", "Dell settings source", 1, true)));
        Assert.Equal(
            "PropertyChanged Value=" + UiaEventText.ValueText("Loaded 12 variables.", status) + " " + UiaEventText.Element(status),
            UiaEventText.PropertyChanged("Value", null, "Loaded 12 variables.", status));
    }

    /// <summary>Stands in for System.Windows.Automation.ExpandCollapseState, which the tests do not load.</summary>
    private enum ExpandCollapseStateForTest
    {
        Collapsed,
        Expanded,
    }
}
