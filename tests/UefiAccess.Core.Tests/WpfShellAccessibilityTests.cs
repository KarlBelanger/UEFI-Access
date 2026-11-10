// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Settings.Dell;
using UefiAccess.Presentation.Accessibility;
using UefiAccess.Presentation.Resources;
using UefiAccess.Presentation.Services;
using UefiAccess.Presentation.ViewModels;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// Static checks of the Alpha 1 WPF shell markup (src/UefiAccess.App). WPF cannot run under xunit
/// on every host, so these tests parse MainWindow.xaml, App.xaml, app.manifest and the code-behind
/// as text and XML and assert the accessibility contract of M2: an AutomationProperties.Name on
/// every control, row and group header (bound to the view model's accessible-name sentences),
/// HelpText on every input, LabeledBy only next to a Name, exactly one polite live region (the
/// Status box, whose name carries its text) and the log marked Off, explicit unique tab indices,
/// the regions as named groups in F6 order (seven since M4), headings in the detail pane, stock controls
/// without template overrides, colours only through SystemColors, no label that implies a
/// change, every user-visible string from the Strings class, the access keys, no timers in the
/// code-behind or in the Presentation view models and services (one pinned exception since Alpha 2,
/// A2-7: the detail scheduler's one quiet-period timer), and the requireAdministrator manifest. The Strings constants the XAML references
/// are resolved through reflection over the Presentation assembly, so a renamed constant fails here
/// before it fails the XAML compiler on Windows. The Alpha 1 round 1 fixes (the tester's findings A1-1
/// to A1-5 and A1-7) are pinned as well: no generated item container is an automation element
/// (the runtime switch that removes WPF's per-item data items, and no container style naming
/// one), the list help spoken once, the two splitters as the announcing subclass (since Alpha 2,
/// A2-8, a read-only value sentence and no range value), and the Copy log button free of any fixed
/// height with a log row that keeps room for it at any text size; and from Alpha 2, no read-only
/// text box that says "read-only" in its help text or name, or in the labels, status texts and
/// values bound to its name and value, and no Writability sentence that begins with it (A2-6). M3 adds the Change
/// group of the detail pane: its tab stops 14 to 20 (Status becomes 24), its six buttons with the
/// access keys U, D, V, B, M and P, the boot order list with a keyed row style of its own, the
/// rows' item status, and the code-behind's part of the change flows; the Confirm change dialog
/// has a test class of its own (ConfirmChangeWindowTests). M4 adds the seventh region, Dell settings, after Log: its tab stops 24 to 26 (Status
/// becomes 27), the Read Dell settings button with the access key S, never disabled, the state line
/// (a read-only box, selected whole on arrival), the list of Dell BIOS settings with a keyed row
/// style of its own and no item status, and the code-behind's part of the region.
/// </summary>
public sealed class WpfShellAccessibilityTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>The application's own namespace in the markup: the two splitters (src/UefiAccess.App/Platform) live there.</summary>
    private static readonly XNamespace Platform = "clr-namespace:UefiAccess.App.Platform";

    /// <summary>The documented WPF runtime switch that gives a plain ItemsControl no automation peer, so its items get no data-item element in front of their controls.</summary>
    private const string ItemsControlSwitch = "Switch.System.Windows.Controls.ItemsControlDoesNotSupportAutomation";

    /// <summary>Elements that must carry an automation name (control types plus the text and pane elements the window uses).</summary>
    private static readonly string[] NamedElementTypes =
    {
        "Window", "GroupBox", "Button", "TextBox", "ComboBox", "ComboBoxItem", "ListView", "ListBox", "AnnouncingGridSplitter", "Expander", "TextBlock", "ScrollViewer",
    };

    /// <summary>Interactive elements that must carry help text.</summary>
    private static readonly string[] InputElementTypes = { "Button", "TextBox", "ComboBox", "ListView", "ListBox", "AnnouncingGridSplitter", "Expander" };

    /// <summary>Focusable controls that must carry an explicit tab index.</summary>
    private static readonly string[] FocusableElementTypes = { "Button", "TextBox", "ComboBox", "ListView", "ListBox", "AnnouncingGridSplitter", "Expander" };

    /// <summary>
    /// Every element type the window may use: stock WPF controls, panels, templates and binding
    /// helpers. M3: ListBox (the log, and the boot order list of
    /// the Change group, a list with item peers of its own) and CheckBox (the Confirm change
    /// dialog's suspend check box; ConfirmChangeWindowTests applies these rules to the dialog).
    /// </summary>
    internal static readonly string[] AllowedElementTypes =
    {
        "Window", "Grid", "RowDefinition", "ColumnDefinition", "GroupBox", "WrapPanel", "StackPanel", "Button", "TextBlock", "TextBox",
        "ComboBox", "ComboBoxItem", "ScrollViewer", "ItemsControl", "DataTemplate", "ItemsPanelTemplate", "VirtualizingStackPanel",
        "Expander", "ListView", "ListBox", "CheckBox", "GridView", "GridViewColumn", "AnnouncingGridSplitter", "Style", "Setter", "DataTrigger", "MultiBinding", "Binding",
        "ScaleTransform", "Trigger", "MultiTrigger", "MultiDataTrigger", "Condition",
    };

    /// <summary>
    /// Attributes whose value is shown or spoken and must therefore come from Strings or a binding;
    /// since M3 the rows' item status as well (deviation D3).
    /// </summary>
    internal static readonly string[] UserVisibleAttributes =
    {
        "Content", "Header", "Text", "Title", "ToolTip", "AutomationProperties.Name", "AutomationProperties.HelpText", "AutomationProperties.LocalizedControlType",
        "AutomationProperties.AccessKey", "AutomationProperties.AcceleratorKey", "AutomationProperties.ItemStatus",
    };

    /// <summary>Attributes whose value names or labels a control and must not imply a change.</summary>
    internal static readonly string[] LabelAttributes = { "Content", "Header", "Text", "ToolTip", "AutomationProperties.Name", "Command", "Click" };

    /// <summary>Properties that take a brush, under their own name or type-qualified ("TextElement.Foreground", "Control.Background").</summary>
    internal static readonly string[] BrushAttributes = { "Foreground", "Background", "BorderBrush", "Fill", "Stroke", "SelectionBrush", "CaretBrush", "OpacityMask", "SelectionTextBrush" };

    /// <summary>The one form a colour may take: a dynamic reference to a SystemColors brush key, which follows a live switch to a contrast theme.</summary>
    internal static readonly Regex DynamicSystemBrush = new(@"^\{DynamicResource \{x:Static SystemColors\.\w+BrushKey\}\}$", RegexOptions.CultureInvariant);

    internal static readonly Regex StringsReference = new(@"\{x:Static\s+res:Strings\.(\w+)\}", RegexOptions.CultureInvariant);
    internal static readonly Regex WriteImplyingWord = new(@"\b(Apply|Save|Toggle|Edit|Write|Set)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    internal static readonly Regex HexColour = new(@"#[0-9A-Fa-f]{6,8}\b", RegexOptions.CultureInvariant);
    internal static readonly Regex BareTemplateAttribute = new(@"(?<![A-Za-z.])Template\s*=", RegexOptions.CultureInvariant);
    internal static readonly Regex AccessKey = new(@"_(\w)", RegexOptions.CultureInvariant);

    [Fact]
    public void WindowBindsItsTitleToTheShellAndHasTheMinimumSize()
    {
        XElement window = LoadMainWindow();
        string title = Attr(window, "Title");
        Assert.Contains("WindowTitle", title, StringComparison.Ordinal);
        Assert.Contains("res:Strings.WindowTitle", title, StringComparison.Ordinal);
        Assert.Contains("WindowTitle", NameOf(window), StringComparison.Ordinal);
        Assert.Equal(900, int.Parse(Attr(window, "MinWidth"), CultureInfo.InvariantCulture));
        Assert.Equal(600, int.Parse(Attr(window, "MinHeight"), CultureInfo.InvariantCulture));
        Assert.Equal("Cycle", Attr(window, "KeyboardNavigation.TabNavigation"));
        Assert.False(string.IsNullOrWhiteSpace(Attr(window, "PreviewKeyDown")), "the window needs a PreviewKeyDown handler for F6 and Ctrl+F");
        Assert.False(string.IsNullOrWhiteSpace(Attr(window, "Loaded")), "the window needs a Loaded handler for the initial focus");
    }

    /// <summary>
    /// The Windows "Text size" accessibility setting scales the system message font, which is
    /// WPF's default FontSize; a hard-coded size opts the whole window out of it. So the window
    /// binds its FontSize to SystemFonts.MessageFontSizeKey and nothing sets a numeric FontSize;
    /// the detail heading is larger through a LayoutTransform, which scales with the same metric.
    /// </summary>
    [Fact]
    public void TextFollowsTheSystemMessageFontSize()
    {
        XElement window = LoadMainWindow();
        Assert.Contains("SystemFonts.MessageFontSizeKey", Attr(window, "FontSize"), StringComparison.Ordinal);
        Assert.Contains("DynamicResource", Attr(window, "FontSize"), StringComparison.Ordinal);

        List<string> numericSizes = window.DescendantsAndSelf()
            .SelectMany(element => element.Attributes().Where(attribute => attribute.Name.LocalName == "FontSize"))
            .Where(attribute => !attribute.Value.StartsWith("{", StringComparison.Ordinal))
            .Select(attribute => Describe(attribute.Parent!) + " FontSize=" + attribute.Value)
            .ToList();
        Assert.True(numericSizes.Count == 0, "Hard-coded font sizes:" + Environment.NewLine + string.Join(Environment.NewLine, numericSizes));
        Assert.DoesNotContain(window.Descendants(Presentation + "Setter"), setter => Attr(setter, "Property") == "FontSize");

        XElement heading = Named(window, "TextBlock", "DetailHeading");
        XElement scale = Assert.Single(heading.Descendants(Presentation + "ScaleTransform"));
        Assert.Equal(Attr(scale, "ScaleX"), Attr(scale, "ScaleY"));
        Assert.True(double.Parse(Attr(scale, "ScaleX"), CultureInfo.InvariantCulture) > 1.0);
    }

    /// <summary>
    /// A2-4 (Axe.Windows 2.4.2 on the OptiPlex 7020, 24 September 2026): the items of an open ComboBox
    /// list carried no PositionInSet/SizeOfSet, because the stock peer of a dropdown item gives none,
    /// so a screen reader could not say "1 of 3" and the scan reported an error per item. Every
    /// ComboBoxItem states its position, 1..n in document order, and the size of its list, n.
    /// </summary>
    [Fact]
    public void EveryComboBoxItemStatesItsPositionAndTheSizeOfItsList()
    {
        XElement window = LoadMainWindow();
        List<XElement> boxes = window.Descendants(Presentation + "ComboBox").ToList();
        Assert.NotEmpty(boxes);
        foreach (XElement box in boxes)
        {
            List<XElement> items = box.Elements(Presentation + "ComboBoxItem").ToList();
            Assert.True(items.Count > 0, Describe(box) + " has no ComboBoxItem children");
            for (int index = 0; index < items.Count; index++)
            {
                string where = Describe(box) + ", item " + (index + 1);
                Assert.True(int.TryParse(Attr(items[index], "AutomationProperties.PositionInSet"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int position), where + " has no numeric AutomationProperties.PositionInSet");
                Assert.True(int.TryParse(Attr(items[index], "AutomationProperties.SizeOfSet"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int size), where + " has no numeric AutomationProperties.SizeOfSet");
                Assert.True(position == index + 1, where + ": PositionInSet " + position + ", expected " + (index + 1));
                Assert.True(size == items.Count, where + ": SizeOfSet " + size + ", expected " + items.Count);
            }
        }
    }

    /// <summary>
    /// The spec requires AutomationProperties.Name on every control, row and group header. LabeledBy
    /// is not a substitute: WPF resolves a name from AutomationProperties.Name, then the LabeledBy
    /// element's name, and only then the element's text.
    /// </summary>
    [Fact]
    public void EveryControlHasAnAutomationName()
    {
        XElement window = LoadMainWindow();

        // One deliberate exception: PanesScroller, the layout-only scroll viewer round 2 put
        // around the five regions above Status so they scroll instead of being cut in a window
        // too small for its text size. It is not focusable and holds the named regions; a name
        // would only add a spoken container in front of them (WpfShellLayoutTests).
        List<XElement> controls = Elements(window, NamedElementTypes).Where(element => Attr(element, Xaml + "Name") != "PanesScroller").ToList();
        Assert.True(controls.Count >= 40, "expected the full Alpha 1 layout; found only " + controls.Count + " named element candidates");

        List<string> unnamed = controls.Where(element => string.IsNullOrWhiteSpace(NameOf(element))).Select(Describe).ToList();
        Assert.True(unnamed.Count == 0, "Elements without AutomationProperties.Name:" + Environment.NewLine + string.Join(Environment.NewLine, unnamed));

        // Rows and log lines get their names through the item container styles. Two lists hold
        // ListBoxItems since M3 (the log and the boot order list of the Change group), so each has
        // a keyed style of its own and each list names its own.
        // Since M4 two lists hold ListViewItems as well: the variable rows keep their unkeyed style,
        // and the list of Dell BIOS settings names its rows through its own keyed style.
        XElement rowStyle = VariableRowStyle(window);
        Assert.Contains("AccessibleName", SetterValue(rowStyle, "AutomationProperties.Name"), StringComparison.Ordinal);
        Assert.Equal(2, window.Descendants(Presentation + "Style").Count(style => Attr(style, "TargetType") == "ListViewItem"));
        XElement dellRowStyle = global::UefiAccess.Core.Tests.MainWindowMarkup.KeyedStyle(window, "DellSettingRowStyle");
        Assert.Equal("ListViewItem", Attr(dellRowStyle, "TargetType"));
        Assert.Equal("{Binding AccessibleName, Mode=OneWay}", SetterValue(dellRowStyle, "AutomationProperties.Name"));
        Assert.Equal("{StaticResource DellSettingRowStyle}", Attr(Named(window, "ListView", "DellSettingsList"), "ItemContainerStyle"));
        Assert.Equal(2, window.Descendants(Presentation + "Style").Count(style => Attr(style, "TargetType") == "ListBoxItem"));
        XElement logLineStyle = global::UefiAccess.Core.Tests.MainWindowMarkup.KeyedStyle(window, "LogLineStyle");
        Assert.Equal("ListBoxItem", Attr(logLineStyle, "TargetType"));
        Assert.Contains("AccessibleName", SetterValue(logLineStyle, "AutomationProperties.Name"), StringComparison.Ordinal);
        Assert.Equal("{StaticResource LogLineStyle}", Attr(Named(window, "ListBox", "LogList"), "ItemContainerStyle"));
        XElement bootOrderRowStyle = global::UefiAccess.Core.Tests.MainWindowMarkup.KeyedStyle(window, "BootOrderRowStyle");
        Assert.Equal("ListBoxItem", Attr(bootOrderRowStyle, "TargetType"));
        Assert.Equal("{Binding AccessibleName, Mode=OneWay}", SetterValue(bootOrderRowStyle, "AutomationProperties.Name"));
        Assert.Equal("{StaticResource BootOrderRowStyle}", Attr(Named(window, "ListBox", "BootOrderList"), "ItemContainerStyle"));
    }

    /// <summary>
    /// Every input carries help text, with one deliberate exception: the per-group rows grid
    /// inside each Expander (finding A1-2). Its help is the List region's, spoken once on arrival;
    /// a help text on the grid was heard again on every entry into a group.
    /// </summary>
    [Fact]
    public void EveryInputHasHelpText()
    {
        XElement window = LoadMainWindow();
        List<XElement> inputs = Elements(window, InputElementTypes).Where(element => !IsGroupRowsGrid(element)).ToList();
        Assert.NotEmpty(inputs);
        Assert.Single(Elements(window, InputElementTypes), IsGroupRowsGrid);

        List<string> missing = inputs.Where(element => string.IsNullOrWhiteSpace(Attr(element, "AutomationProperties.HelpText"))).Select(Describe).ToList();
        Assert.True(missing.Count == 0, "Inputs without AutomationProperties.HelpText:" + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    /// <summary>
    /// NVDA reads UI Automation HelpText as the object description on focus by default, so a help
    /// text on every list row would be spoken after every row sentence, and a long one on every
    /// detail field after every field name. The rows carry none (the list's help is on the List
    /// region, finding A1-2), the group headers and the detail fields carry short, dedicated texts, and the
    /// texts that are heard on every arrow key or Tab stay short.
    /// </summary>
    [Fact]
    public void HelpTextIsNotRepeatedOnEveryRowAndStaysShortWhereItIsHeardOften()
    {
        XElement window = LoadMainWindow();
        XElement rowStyle = VariableRowStyle(window);
        Assert.Equal(string.Empty, SetterValue(rowStyle, "AutomationProperties.HelpText"));

        XElement expander = Assert.Single(window.Descendants(Presentation + "Expander"));
        Assert.Equal("{x:Static res:Strings.GroupHeaderHelp}", Attr(expander, "AutomationProperties.HelpText"));

        // Keyboard focus lands on the header toggle button inside the expander's stock template,
        // not on the expander, so the same help text is set on that button when the expander loads.
        Assert.Equal("OnGroupHeaderLoaded", Attr(expander, "Loaded"));
        string codeBehind = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "src", "UefiAccess.App", "MainWindow.xaml.cs"));
        Assert.Contains("void OnGroupHeaderLoaded(", codeBehind, StringComparison.Ordinal);
        Assert.Contains("FindDescendant<ToggleButton>(expander)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetHelpText(header, Strings.GroupHeaderHelp)", codeBehind, StringComparison.Ordinal);

        XElement sections = Named(window, "ItemsControl", "SectionsControl");
        XElement field = Assert.Single(sections.Descendants(Presentation + "TextBox"));

        // DetailFieldHelp, or for a value too long to be read as a selection the same help with its
        // length and how to read on (VariableDetailViewModel.HelpTextFor; DetailValueArrivalTests),
        // through the field's notifying HelpText property: the pane is updated in place, and a
        // converter bound to the field itself would keep a kept box's help from the row before.
        Assert.Equal("{Binding HelpText, Mode=OneWay}", Attr(field, "AutomationProperties.HelpText"));
        Assert.Equal("{x:Static res:Strings.DetailFieldHelp}", Attr(Named(window, "TextBox", "ReadOnlyStatusBox"), "AutomationProperties.HelpText"));

        Assert.True(Strings.VariableListHelp.Length < 200, "VariableListHelp is " + Strings.VariableListHelp.Length + " characters");
        Assert.True(Strings.GroupHeaderHelp.Length < 120, "GroupHeaderHelp is " + Strings.GroupHeaderHelp.Length + " characters");
        Assert.True(Strings.DetailFieldHelp.Length < 80, "DetailFieldHelp is " + Strings.DetailFieldHelp.Length + " characters");

        // M3: the boot order list's help is heard on every Tab into the
        // list, so it stays under 200 characters, like each Change button's; the Change group's
        // header help under 120, like a group header's. The rows of the boot order list carry none:
        // the list's help is heard once, on arrival.
        Assert.True(Strings.BootOrderEntriesHelp.Length < 200, "BootOrderEntriesHelp is " + Strings.BootOrderEntriesHelp.Length + " characters");
        Assert.True(Strings.ChangeGroupHelp.Length < 120, "ChangeGroupHelp is " + Strings.ChangeGroupHelp.Length + " characters");
        foreach (string help in new[] { Strings.MoveUpHelp, Strings.MoveDownHelp, Strings.ReviewChangeHelp, Strings.BootOnceHelp, Strings.ClearBootNextHelp, Strings.RestorePreviousHelp })
        {
            Assert.True(help.Length < 200, help.Length + " characters: " + help);
        }

        Assert.Equal(string.Empty, SetterValue(global::UefiAccess.Core.Tests.MainWindowMarkup.KeyedStyle(window, "BootOrderRowStyle"), "AutomationProperties.HelpText"));
        Assert.Equal("{x:Static res:Strings.BootOrderEntriesHelp}", Attr(Named(window, "ListBox", "BootOrderList"), "AutomationProperties.HelpText"));
        Assert.Equal("{x:Static res:Strings.ChangeGroupHelp}", Attr(Named(window, "GroupBox", "ChangeGroup"), "AutomationProperties.HelpText"));

        // M4: the state line's help is heard on every Tab onto it, so it
        // stays under 80 characters, like a detail field's; the Dell list's is heard on every entry
        // into the list and the button's on every Tab onto it, so both stay under 200. The Dell rows
        // carry none, and neither does the region (it would be heard on every F6).
        Assert.True(Strings.DellSourceHelp.Length < 80, "DellSourceHelp is " + Strings.DellSourceHelp.Length + " characters");
        Assert.True(Strings.DellSettingsListHelp.Length < 200, "DellSettingsListHelp is " + Strings.DellSettingsListHelp.Length + " characters");
        Assert.True(Strings.ReadDellSettingsHelp.Length < 200, "ReadDellSettingsHelp is " + Strings.ReadDellSettingsHelp.Length + " characters");
        Assert.Equal(string.Empty, SetterValue(global::UefiAccess.Core.Tests.MainWindowMarkup.KeyedStyle(window, "DellSettingRowStyle"), "AutomationProperties.HelpText"));
        Assert.Equal("{x:Static res:Strings.DellSourceHelp}", Attr(Named(window, "TextBox", "DellSourceBox"), "AutomationProperties.HelpText"));
        Assert.Equal("{x:Static res:Strings.DellSettingsListHelp}", Attr(Named(window, "ListView", "DellSettingsList"), "AutomationProperties.HelpText"));
        Assert.Equal("{x:Static res:Strings.ReadDellSettingsHelp}", Attr(Named(window, "Button", "ReadDellSettingsButton"), "AutomationProperties.HelpText"));
        Assert.Equal(string.Empty, Attr(Named(window, "GroupBox", "DellRegion"), "AutomationProperties.HelpText"));

        // The list help is the List region's alone (finding A1-2); the scroll viewer and the
        // per-group rows grid carry none, so it is heard once, on arrival.
        Assert.Equal("{x:Static res:Strings.VariableListHelp}", Attr(Named(window, "GroupBox", "ListRegion"), "AutomationProperties.HelpText"));
        Assert.Equal(string.Empty, Attr(Named(window, "ScrollViewer", "ListScroller"), "AutomationProperties.HelpText"));
        Assert.Equal(string.Empty, Attr(Assert.Single(expander.Descendants(Presentation + "ListView")), "AutomationProperties.HelpText"));
    }

    /// <summary>
    /// Alpha 2 finding A2-6 (the tester's A2-4, "Read Only Read Only" on every box of the detail pane):
    /// the screen reader says a read-only text box's state itself (NVDA: "edit", "read only"), so
    /// no read-only TextBox of either window says "read-only" or "read only" again in its help text
    /// or its name, and neither do the help texts they take from Strings: the detail fields' two
    /// (DetailFieldHelp, and LongDetailFieldHelpFormat for a long value, both through the field's
    /// HelpText property), the Writability line's (DetailFieldHelp), the Status box's
    /// (StatusHelp), the dialog boxes' (ConfirmBoxHelp) and, since M4, the Dell state line's
    /// (DellSourceHelp), whose texts and the Dell rows' words are checked too. A TextBox is read-only by its own
    /// IsReadOnly or by the style it uses. The help texts of groups, panes and buttons, which no
    /// screen reader calls read-only, keep their words.
    /// The names and values the markup takes from bindings are checked at their source: no label a
    /// detail field is named by (the labels and the attributes' UEFI names in
    /// VariableDetailViewModel, HexDumpName) and no status text or format the Status box can be
    /// named by and hold (the Status, Format, Sentence, Prefix, State and Region constants of
    /// Strings, the shell's and the live source's failure texts) contains the words anywhere, and
    /// no fixed value a detail field can hold (the attribute meanings and the other constant values
    /// of VariableDetailViewModel, the namespace labels, the Writability line's sentences in Core's
    /// WritabilitySentences and its "No variable selected") and no text literal of a decoder
    /// begins with them.
    /// </summary>
    [Fact]
    public void NoReadOnlyTextBoxSaysReadOnlyInItsHelpTextOrName()
    {
        Regex readOnly = new(@"read[\s-]only", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        string longValueHelp = VariableDetailViewModel.HelpTextFor(new DetailFieldViewModel("Long", new string('v', VariableDetailViewModel.LongValueLength)));
        Assert.NotEqual(Strings.DetailFieldHelp, longValueHelp);
        foreach (string help in new[] { Strings.DetailFieldHelp, Strings.LongDetailFieldHelpFormat, longValueHelp, Strings.StatusHelp, Strings.ConfirmBoxHelp, Strings.DellSourceHelp })
        {
            Assert.DoesNotMatch(readOnly, help);
        }

        // Since M4 the main window has a fourth read-only box, the Dell state line.
        List<string> problems = new();
        foreach ((string file, int expected) in new[] { ("MainWindow.xaml", 4), ("ConfirmChangeWindow.xaml", 8) })
        {
            XElement window = global::UefiAccess.Core.Tests.MainWindowMarkup.Load(file);
            List<XElement> boxes = window.Descendants(Presentation + "TextBox").Where(box => IsReadOnlyTextBox(window, box)).ToList();
            Assert.True(boxes.Count == expected, file + ": expected " + expected + " read-only text boxes, found " + boxes.Count);
            foreach (XElement box in boxes)
            {
                string help = ResolveStrings(Attr(box, "AutomationProperties.HelpText"));
                string name = ResolveStrings(NameOf(box));
                if (readOnly.IsMatch(help))
                {
                    problems.Add(file + ": " + Describe(box) + " help text \"" + help + "\"");
                }

                if (readOnly.IsMatch(name))
                {
                    problems.Add(file + ": " + Describe(box) + " name \"" + name + "\"");
                }
            }
        }

        Assert.True(problems.Count == 0, "Read-only text boxes that say so again:" + Environment.NewLine + string.Join(Environment.NewLine, problems));

        // The three kinds of the main window: a detail field (read-only by its style, help from the
        // field's HelpText property), the Writability line and the Status box (read-only
        // themselves); the filter box is not read-only.
        XElement main = LoadMainWindow();
        XElement field = Assert.Single(Named(main, "ItemsControl", "SectionsControl").Descendants(Presentation + "TextBox"));
        Assert.True(IsReadOnlyTextBox(main, field));
        Assert.Equal("{Binding HelpText, Mode=OneWay}", Attr(field, "AutomationProperties.HelpText"));
        Assert.True(IsReadOnlyTextBox(main, Named(main, "TextBox", "ReadOnlyStatusBox")));
        Assert.Equal("{x:Static res:Strings.DetailFieldHelp}", Attr(Named(main, "TextBox", "ReadOnlyStatusBox"), "AutomationProperties.HelpText"));
        Assert.True(IsReadOnlyTextBox(main, Named(main, "TextBox", "StatusBox")));
        Assert.Equal("{x:Static res:Strings.StatusHelp}", Attr(Named(main, "TextBox", "StatusBox"), "AutomationProperties.HelpText"));
        Assert.False(IsReadOnlyTextBox(main, Named(main, "TextBox", "FilterBox")));

        // M4: the Dell state line, read-only itself, named and helped without the words (A2-6:
        // that nothing can be changed there is said once, in its text and on the button's help).
        XElement dellSource = Named(main, "TextBox", "DellSourceBox");
        Assert.True(IsReadOnlyTextBox(main, dellSource));
        Assert.Equal("{x:Static res:Strings.DellSourceName}", NameOf(dellSource));
        Assert.Equal("{x:Static res:Strings.DellSourceHelp}", Attr(dellSource, "AutomationProperties.HelpText"));

        // The pattern catches both spellings the finding heard, and the pane's own help keeps its words.
        Assert.Matches(readOnly, "Read-only. Escape returns to the list at the same row.");
        Assert.Matches(readOnly, "edit  read only");
        Assert.StartsWith("Read-only fields for the selected variable.", Strings.DetailPaneHelp, StringComparison.Ordinal);

        // The names and values the markup binds, which its attributes do not show: a detail field
        // is named by its label (DetailFieldViewModel.AccessibleName; the hex dump box by
        // HexDumpName) and holds its value, an attribute field is named by the attribute's UEFI
        // name and holds its meaning, the Writability line holds the selected variable's sentence
        // from Core's WritabilitySentences (NoSelectionHeading with nothing selected), and the
        // Status box is named "Status: " and its message and holds the message. The screen reader
        // says "read only" after the name and before the value: no label and no status text or
        // format says it anywhere, and no fixed value begins with it.
        Regex readOnlyFirst = new(@"^\s*read[\s-]only", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        List<(string Name, string Text)> detailConstants = LiteralStrings(typeof(VariableDetailViewModel)).ToList();
        List<(string Name, string Text)> labels = detailConstants
            .Where(constant => constant.Name.EndsWith("Label", StringComparison.Ordinal) || constant.Name.EndsWith("Name", StringComparison.Ordinal))
            .Concat(LiteralStrings(typeof(Strings)).Where(constant => constant.Name is "Strings.HexDumpName" or "Strings.ReadOnlyStatusName" or "Strings.StatusName"))
            .ToList();
        List<(string Name, string Text)> values = detailConstants
            .Except(labels)
            .Concat(LiteralStrings(typeof(NamespaceCatalog)))
            .Concat(LiteralStrings(typeof(Strings)).Where(constant => constant.Name is "Strings.NoSelectionHeading" or "Strings.UnknownVendorLabel"))
            .Concat(LiteralStrings(typeof(WritabilitySentences)))
            .ToList();
        string[] statusSuffixes = { "Status", "Format", "Sentence", "Prefix", "Separator" };
        List<(string Name, string Text)> statusTexts = LiteralStrings(typeof(Strings))
            .Where(constant => statusSuffixes.Any(suffix => constant.Name.EndsWith(suffix, StringComparison.Ordinal)) || constant.Name.StartsWith("Strings.State", StringComparison.Ordinal) || constant.Name.StartsWith("Strings.Region", StringComparison.Ordinal))
            .Concat(new[]
            {
                ("ShellViewModel." + nameof(ShellViewModel.ExportFailedFormat), ShellViewModel.ExportFailedFormat),
                ("ShellViewModel." + nameof(ShellViewModel.NoResultMessage), ShellViewModel.NoResultMessage),
                ("LiveFirmwareSource." + nameof(LiveFirmwareSource.CancelledMessage), LiveFirmwareSource.CancelledMessage),
                ("LiveFirmwareSource." + nameof(LiveFirmwareSource.EnumerationFailedPrefix), LiveFirmwareSource.EnumerationFailedPrefix),
            })
            .ToList();

        // Each set holds what it is meant to, so a renamed constant cannot empty it unnoticed.
        Assert.Contains(("VariableDetailViewModel." + nameof(VariableDetailViewModel.NameLabel), VariableDetailViewModel.NameLabel), labels);
        Assert.Contains(("VariableDetailViewModel." + nameof(VariableDetailViewModel.NonVolatileName), VariableDetailViewModel.NonVolatileName), labels);
        Assert.Contains(("Strings." + nameof(Strings.HexDumpName), Strings.HexDumpName), labels);
        Assert.Contains(("VariableDetailViewModel." + nameof(VariableDetailViewModel.NonVolatileMeaning), VariableDetailViewModel.NonVolatileMeaning), values);
        Assert.Contains(("VariableDetailViewModel." + nameof(VariableDetailViewModel.EmptyDataText), VariableDetailViewModel.EmptyDataText), values);
        Assert.Contains(("Strings." + nameof(Strings.NoSelectionHeading), Strings.NoSelectionHeading), values);
        foreach (string name in new[] { nameof(WritabilitySentences.ReorderOnly), nameof(WritabilitySentences.OneTimeBoot), nameof(WritabilitySentences.OneTimeBootTarget), nameof(WritabilitySentences.AuthenticatedWriteFormat), nameof(WritabilitySentences.NotWhitelisted), nameof(WritabilitySentences.MalformedFormat), nameof(WritabilitySentences.Offline), nameof(WritabilitySentences.WrongMachine) })
        {
            Assert.Contains("WritabilitySentences." + name, values.Select(constant => constant.Name));
        }

        foreach (string name in new[] { nameof(Strings.IdleStatus), nameof(Strings.LoadingStatus), nameof(Strings.LoadedFormat), nameof(Strings.FailedFormat), nameof(Strings.ShowingFormat), nameof(Strings.RegionArrivedFormat), nameof(Strings.NotElevatedSentence), nameof(Strings.StateRefused), nameof(Strings.ListDetailSplitterValueFormat), nameof(Strings.SplitterLimitPrefix) })
        {
            Assert.Contains("Strings." + name, statusTexts.Select(constant => constant.Name));
        }

        List<string> said = labels.Where(constant => readOnly.IsMatch(constant.Text)).Select(constant => constant.Name + " (a field's name) \"" + constant.Text + "\"")
            .Concat(statusTexts.Where(constant => readOnly.IsMatch(constant.Text)).Select(constant => constant.Name + " (the Status box's name and value) \"" + constant.Text + "\""))
            .Concat(values.Where(constant => readOnlyFirst.IsMatch(constant.Text)).Select(constant => constant.Name + " (a field's value) \"" + constant.Text + "\""))
            .ToList();

        // M4: the Dell state line holds Strings' Dell lines and Core's Dell sentences, and the Dell
        // rows are named by Core's words; none of them says the words anywhere (A2-6: no row and no sentence speaks Dell's read-only flag).
        List<(string Name, string Text)> dellTexts = LiteralStrings(typeof(Strings))
            .Where(constant => constant.Name.StartsWith("Strings.Dell", StringComparison.Ordinal) || constant.Name.StartsWith("Strings.ReadDell", StringComparison.Ordinal))
            .Concat(LiteralStrings(typeof(DellSentences)))
            .ToList();
        Assert.Contains(("Strings." + nameof(Strings.DellLiveLineFormat), Strings.DellLiveLineFormat), dellTexts);
        Assert.Contains(("Strings." + nameof(Strings.ReadDellSettingsHelp), Strings.ReadDellSettingsHelp), dellTexts);
        Assert.Contains(("DellSentences." + nameof(DellSentences.UnavailableLead), DellSentences.UnavailableLead), dellTexts);
        said.AddRange(dellTexts.Where(constant => readOnly.IsMatch(constant.Text)).Select(constant => constant.Name + " (the Dell state line or a Dell row) \"" + constant.Text + "\""));

        // The decoders' field labels and values are text literals in their source.
        Regex literalFirst = new(@"[$@]*""\s*read[\s-]only", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        foreach (string file in RepositoryPaths.Enumerate("src/UefiAccess.Core/Decoders", "*.cs"))
        {
            string[] lines = File.ReadAllLines(file);
            for (int index = 0; index < lines.Length; index++)
            {
                if (!lines[index].TrimStart().StartsWith("//", StringComparison.Ordinal) && literalFirst.IsMatch(lines[index]))
                {
                    said.Add(RepositoryPaths.Relative(file) + " line " + (index + 1) + " (a decoder's text) " + lines[index].Trim());
                }
            }
        }

        Assert.True(said.Count == 0, "Labels, values and status texts that say \"read-only\" where the screen reader says it:" + Environment.NewLine + string.Join(Environment.NewLine, said));
        Assert.Matches(readOnlyFirst, "Read-only in this version: no setting can be changed from this application.");
        Assert.Matches(literalFirst, "Summary = \"Read only\";");
    }

    [Fact]
    public void LabeledByIsOnlyEverInAdditionToAName()
    {
        XElement window = LoadMainWindow();
        List<XElement> labelled = window.Descendants().Where(element => element.Attribute("AutomationProperties.LabeledBy") is not null).ToList();
        Assert.NotEmpty(labelled);
        foreach (XElement element in labelled)
        {
            Assert.False(string.IsNullOrWhiteSpace(NameOf(element)), Describe(element) + " has LabeledBy without an AutomationProperties.Name");
        }

        HashSet<string> names = window.Descendants().Select(element => Attr(element, Xaml + "Name")).Where(name => name.Length > 0).ToHashSet(StringComparer.Ordinal);
        foreach (XElement element in labelled)
        {
            string binding = Attr(element, "AutomationProperties.LabeledBy");
            Match reference = Regex.Match(binding, @"ElementName\s*=\s*(\w+)");
            if (reference.Success)
            {
                Assert.Contains(reference.Groups[1].Value, names);
                XElement label = window.Descendants().Single(candidate => Attr(candidate, Xaml + "Name") == reference.Groups[1].Value);
                Assert.False(string.IsNullOrWhiteSpace(NameOf(label)), "Label " + reference.Groups[1].Value + " needs an AutomationProperties.Name");
            }
            else
            {
                Assert.Contains("RelativeSource", binding, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void ExactlyOneLiveRegionIsPoliteAndTheLogIsOff()
    {
        XElement window = LoadMainWindow();
        List<XElement> live = window.Descendants().Where(element => element.Attribute("AutomationProperties.LiveSetting") is not null).ToList();
        Assert.Equal(2, live.Count);

        XElement polite = Assert.Single(live, element => Attr(element, "AutomationProperties.LiveSetting") == "Polite");
        Assert.Equal(Presentation + "TextBox", polite.Name);
        Assert.Equal("StatusBox", Attr(polite, Xaml + "Name"));

        XElement off = Assert.Single(live, element => Attr(element, "AutomationProperties.LiveSetting") == "Off");
        Assert.Equal(Presentation + "ListBox", off.Name);
        Assert.Equal("LogList", Attr(off, Xaml + "Name"));

        Assert.DoesNotContain("Assertive", MainWindowMarkup(), StringComparison.Ordinal);
    }

    /// <summary>
    /// NVDA and Narrator speak a live region's name when LiveRegionChanged is raised, and a
    /// TextBox's name does not follow its text, so the Status box's name is "Status: &lt;text&gt;",
    /// bound to its own Text so it changes in the same instant as the text. Naming the box
    /// "Status" alone would turn every announcement into the word "Status". This is the one
    /// TextBox whose name carries its value (the detail fields are named by their labels alone);
    /// The double reading on focus it causes is a known limitation.
    /// </summary>
    [Fact]
    public void StatusBoxIsAReadOnlyPoliteLiveRegionWhoseNameCarriesItsText()
    {
        XElement window = LoadMainWindow();
        XElement status = Named(window, "TextBox", "StatusBox");

        Assert.Equal("True", Attr(status, "IsReadOnly"));
        Assert.Equal("Polite", Attr(status, "AutomationProperties.LiveSetting"));
        Assert.False(string.IsNullOrWhiteSpace(Attr(status, "TabIndex")));
        Assert.Contains("StatusText", Attr(status, "Text"), StringComparison.Ordinal);
        Assert.Contains("res:Strings.StatusHelp", Attr(status, "AutomationProperties.HelpText"), StringComparison.Ordinal);

        XElement name = status.Element(Presentation + "AutomationProperties.Name") ?? throw new Xunit.Sdk.XunitException("StatusBox needs an AutomationProperties.Name property element");
        XElement multi = name.Element(Presentation + "MultiBinding") ?? throw new Xunit.Sdk.XunitException("the StatusBox name must be a MultiBinding of the label and the text");
        List<XElement> parts = multi.Elements(Presentation + "Binding").ToList();
        Assert.Equal(2, parts.Count);
        Assert.Contains("res:Strings.StatusName", Attr(parts[0], "Source"), StringComparison.Ordinal);
        Assert.Equal("Text", Attr(parts[1], "Path"));
        Assert.Contains("Self", Attr(parts[1], "RelativeSource"), StringComparison.Ordinal);
    }

    /// <summary>
    /// Every focusable control carries an explicit tab index. Outside templates the indices are
    /// unique and increase in document order, so the tab order is the reading order; inside a
    /// template (rows, group headers, detail fields) one index is shared by every instance.
    /// </summary>
    [Fact]
    public void TabIndicesAreExplicitAndUniqueOutsideTemplates()
    {
        XElement window = LoadMainWindow();
        List<XElement> focusable = window.DescendantsAndSelf()
            .Where(IsShellElement)
            .Where(element => FocusableElementTypes.Contains(element.Name.LocalName, StringComparer.Ordinal)
                || (element.Name.LocalName == "TextBlock" && Attr(element, "Focusable") == "True")
                || (element.Name.LocalName == "ItemsControl" && Attr(element, "KeyboardNavigation.TabNavigation") == "Once"))
            .ToList();
        Assert.True(focusable.Count >= 17, "expected at least 17 focusable controls; found " + focusable.Count);

        List<string> missing = focusable.Where(element => TabIndexOf(element) is null).Select(Describe).ToList();
        Assert.True(missing.Count == 0, "Focusable controls without TabIndex:" + Environment.NewLine + string.Join(Environment.NewLine, missing));

        List<int> outsideTemplates = focusable.Where(element => !IsInsideTemplate(element)).Select(element => TabIndexOf(element)!.Value).ToList();
        Assert.Equal(outsideTemplates.Count, outsideTemplates.Distinct().Count());
        Assert.Equal(outsideTemplates.OrderBy(index => index).ToList(), outsideTemplates);

        Assert.Equal(1, TabIndexOf(Named(window, "Button", "RefreshButton")));
        Assert.Equal(6, TabIndexOf(Named(window, "TextBox", "FilterBox")));
        Assert.Equal(9, TabIndexOf(Named(window, "ItemsControl", "GroupsControl")));
        Assert.Equal(13, TabIndexOf(Named(window, "TextBox", "ReadOnlyStatusBox")));

        // The Change group follows the Writability line, 14 to 20, and
        // the log splitter, the log and Copy log move to 21 to 23. The
        // Dell settings region follows Copy log, 24 to 26, and Status moves from 24 to 27.
        Assert.Equal(14, TabIndexOf(Named(window, "ListBox", "BootOrderList")));
        Assert.Equal(15, TabIndexOf(Named(window, "Button", "MoveUpButton")));
        Assert.Equal(16, TabIndexOf(Named(window, "Button", "MoveDownButton")));
        Assert.Equal(17, TabIndexOf(Named(window, "Button", "ReviewChangeButton")));
        Assert.Equal(18, TabIndexOf(Named(window, "Button", "BootOnceButton")));
        Assert.Equal(19, TabIndexOf(Named(window, "Button", "ClearBootNextButton")));
        Assert.Equal(20, TabIndexOf(Named(window, "Button", "RestorePreviousButton")));
        Assert.Equal(21, TabIndexOf(Named(window, "AnnouncingGridSplitter", "LogSplitter")));
        Assert.Equal(22, TabIndexOf(Named(window, "ListBox", "LogList")));
        Assert.Equal(23, TabIndexOf(Named(window, "Button", "CopyLogButton")));
        Assert.Equal(24, TabIndexOf(Named(window, "Button", "ReadDellSettingsButton")));
        Assert.Equal(25, TabIndexOf(Named(window, "TextBox", "DellSourceBox")));
        Assert.Equal(26, TabIndexOf(Named(window, "ListView", "DellSettingsList")));
        Assert.Equal(27, TabIndexOf(Named(window, "TextBox", "StatusBox")));
        Assert.True(TabIndexOf(Named(window, "TextBox", "FilterBox")) < TabIndexOf(Named(window, "ItemsControl", "GroupsControl")));
        Assert.True(TabIndexOf(Named(window, "ListBox", "LogList")) < TabIndexOf(Named(window, "TextBox", "StatusBox")));
    }

    [Fact]
    public void ShellUsesStockControlsWithoutTemplateOverrides()
    {
        string xaml = MainWindowMarkup();
        Assert.DoesNotContain("<ControlTemplate", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ControlTemplate", xaml, StringComparison.Ordinal);
        Assert.DoesNotMatch(BareTemplateAttribute, xaml);
        Assert.DoesNotContain("Property=\"Template\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherTimer", xaml, StringComparison.Ordinal);

        XElement window = LoadMainWindow();
        foreach (XElement element in window.DescendantsAndSelf().Where(element => !element.Name.LocalName.Contains('.', StringComparison.Ordinal)))
        {
            Assert.Contains(element.Name.LocalName, AllowedElementTypes);

            // Every element is a stock control from the presentation namespace, except the two
            // splitters: the documented Platform subclass of the stock GridSplitter
            // (SplittersSayTheirPositionAsAReadOnlyValueSentence), a control subclass with the stock template.
            Assert.Equal(element.Name.LocalName == "AnnouncingGridSplitter" ? Platform : Presentation, element.Name.Namespace);
        }
    }

    /// <summary>
    /// Colours only as dynamic references to SystemColors brush keys, so a switch to a contrast
    /// theme repaints at once, in every file that can set one: MainWindow.xaml and App.xaml (every
    /// brush property, also type-qualified as in TextElement.Foreground or a Setter on
    /// Control.Background, and never as a property element holding a colour name or a brush), and
    /// the C# of the application (no Brushes, Colors, Color.FromRgb/FromArgb, new SolidColorBrush,
    /// and no SystemColors brush captured by value, which does not follow a theme switch).
    /// Round 2 (finding F168): the check once matched exact attribute names only and accepted any
    /// value that merely mentioned SystemColors.
    /// </summary>
    [Fact]
    public void NoHardCodedColours()
    {
        string appDirectory = Path.Combine(RepositoryPaths.Root, "src", "UefiAccess.App");
        List<string> violations = new();
        int dynamicBrushes = 0;
        foreach (string file in new[] { MainWindowPath(), Path.Combine(appDirectory, "App.xaml") })
        {
            string name = Path.GetFileName(file);
            string xaml = Regex.Replace(File.ReadAllText(file), @"<!--.*?-->", string.Empty, RegexOptions.Singleline | RegexOptions.CultureInvariant);
            foreach (string forbidden in new[] { "SolidColorBrush", "GradientBrush", @"\bColor=", @"\bColors\.", @"<Color\b" })
            {
                if (Regex.IsMatch(xaml, forbidden, RegexOptions.CultureInvariant))
                {
                    violations.Add(name + " contains " + forbidden);
                }
            }

            if (HexColour.IsMatch(xaml))
            {
                violations.Add(name + " contains a hexadecimal colour");
            }

            foreach (XElement element in XDocument.Load(file).Root!.DescendantsAndSelf())
            {
                if (IsBrushProperty(element.Name.LocalName) && element.Name.LocalName.Contains('.', StringComparison.Ordinal))
                {
                    violations.Add(name + ": property element <" + element.Name.LocalName + "> (a colour must be a dynamic SystemColors reference in an attribute)");
                }

                foreach (XAttribute attribute in element.Attributes().Where(attribute => IsBrushProperty(attribute.Name.LocalName)))
                {
                    dynamicBrushes += CheckBrush(name + ": " + Describe(element) + " " + attribute.Name.LocalName, attribute.Value, violations);
                }

                if (element.Name == Presentation + "Setter" && IsBrushProperty(Attr(element, "Property")))
                {
                    XElement? valueElement = element.Element(Presentation + "Setter.Value");
                    if (valueElement is not null)
                    {
                        violations.Add(name + ": Setter " + Attr(element, "Property") + " with a property-element value");
                    }
                    else
                    {
                        dynamicBrushes += CheckBrush(name + ": Setter " + Attr(element, "Property"), Attr(element, "Value"), violations);
                    }
                }
            }
        }

        Regex codeColour = new(@"\bBrushes\.|\bColors\.|Color\.From(Rgb|Argb|ScRgb)|new\s+SolidColorBrush|new\s+\w*GradientBrush|SystemColors\.\w+Brush\b(?!Key)|SystemColors\.\w+Color\b(?!Key)", RegexOptions.CultureInvariant);
        foreach (string file in RepositoryPaths.Enumerate("src/UefiAccess.App", "*.cs"))
        {
            string code = File.ReadAllText(file);
            Match match = codeColour.Match(code);
            if (match.Success)
            {
                violations.Add(RepositoryPaths.Relative(file) + " sets a colour in code: " + match.Value);
            }
        }

        Assert.True(violations.Count == 0, "Colours outside dynamic SystemColors brush keys:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
        Assert.True(dynamicBrushes >= 10, "expected the window's system-colour brushes; found " + dynamicBrushes);

        // The guard's own coverage: type-qualified names, captured brushes and literal colours are caught.
        Assert.True(IsBrushProperty("TextElement.Foreground") && IsBrushProperty("Control.Background") && IsBrushProperty("Border.BorderBrush"));
        Assert.False(IsBrushProperty("BorderThickness") || IsBrushProperty("Foregrounds"));
        List<string> sample = new();
        Assert.Equal(0, CheckBrush("sample", "{x:Static SystemColors.ControlDarkBrush}", sample));
        Assert.Equal(0, CheckBrush("sample", "Red", sample));
        Assert.Equal(0, CheckBrush("sample", "{DynamicResource {x:Static SystemColors.ControlDarkBrush}}", sample));
        Assert.Equal(3, sample.Count);
        Assert.Matches(codeColour, "var brush = SystemColors.HighlightBrush;");
        Assert.Matches(codeColour, "Background = Brushes.White;");
        Assert.DoesNotMatch(codeColour, "SetResourceReference(BackgroundProperty, SystemColors.HighlightBrushKey);");
    }

    /// <summary>No control may be labelled Apply, Save, Toggle, Edit, Write or Set (whole words, any case), neither in XAML nor in the Strings constants the XAML uses as labels.</summary>
    [Fact]
    public void NoControlLabelImpliesAChange()
    {
        XElement window = LoadMainWindow();
        List<string> violations = new();
        foreach (XElement element in window.DescendantsAndSelf())
        {
            foreach (XAttribute attribute in element.Attributes())
            {
                string local = attribute.Name.LocalName;
                bool isLabel = LabelAttributes.Contains(local, StringComparer.Ordinal) || attribute.Name == Xaml + "Name";
                if (!isLabel)
                {
                    continue;
                }

                string text = ResolveStrings(attribute.Value);
                if (WriteImplyingWord.IsMatch(text))
                {
                    violations.Add(Describe(element) + " " + local + "=" + attribute.Value);
                }
            }
        }

        Assert.True(violations.Count == 0, "Labels that imply a change:" + Environment.NewLine + string.Join(Environment.NewLine, violations));

        foreach (FieldInfo field in LabelConstants())
        {
            string value = (string)field.GetRawConstantValue()!;
            Assert.False(WriteImplyingWord.IsMatch(value), "Strings." + field.Name + " = \"" + value + "\" implies a change");
        }
    }

    /// <summary>
    /// WPF on .NET 8 has no AutomationProperties.LandmarkType, so the regions are exposed as
    /// named groups (GroupBox) in F6 order, each named with the region name RegionOrder speaks.
    /// Since M4 there are seven: Dell settings between Log and Status.
    /// </summary>
    [Fact]
    public void SevenRegionsAreNamedGroupsInF6Order()
    {
        XElement window = LoadMainWindow();
        string[] expectedNames = { "ToolbarRegion", "FilterRegion", "ListRegion", "DetailRegion", "LogRegion", "DellRegion", "StatusRegion" };
        List<XElement> regions = window.Descendants(Presentation + "GroupBox")
            .Where(element => Attr(element, Xaml + "Name").EndsWith("Region", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(expectedNames, regions.Select(element => Attr(element, Xaml + "Name")).ToArray());
        Assert.Equal(regions.Count, RegionOrder.All.Count);

        for (int i = 0; i < regions.Count; i++)
        {
            Region region = RegionOrder.All[i];
            string constant = "res:Strings.Region" + region;
            Assert.Contains(constant, Attr(regions[i], "Header"), StringComparison.Ordinal);
            Assert.Contains(constant, NameOf(regions[i]), StringComparison.Ordinal);
            Assert.Equal(RegionOrder.Name(region), ResolveStrings(NameOf(regions[i])));
        }

        Assert.DoesNotContain("LandmarkType", MainWindowMarkup(), StringComparison.Ordinal);

        // The list region can take focus itself so F6 always lands somewhere when the list is empty, without becoming a tab stop.
        XElement list = regions[2];
        Assert.Equal("True", Attr(list, "Focusable"));
        Assert.Equal("False", Attr(list, "IsTabStop"));
    }

    [Fact]
    public void DetailPaneUsesHeadingsAndReadOnlyFields()
    {
        XElement window = LoadMainWindow();

        XElement heading = Named(window, "TextBlock", "DetailHeading");
        Assert.Equal("Level1", Attr(heading, "AutomationProperties.HeadingLevel"));
        Assert.Equal("True", Attr(heading, "Focusable"));
        Assert.Contains("Detail.Heading", Attr(heading, "Text"), StringComparison.Ordinal);
        Assert.Contains("Detail.Heading", NameOf(heading), StringComparison.Ordinal);

        XElement sections = Named(window, "ItemsControl", "SectionsControl");
        Assert.Contains("Detail.Sections", Attr(sections, "ItemsSource"), StringComparison.Ordinal);
        XElement sectionHeading = Assert.Single(sections.Descendants(Presentation + "TextBlock"), element => Attr(element, "AutomationProperties.HeadingLevel") == "Level2");
        Assert.Contains("Title", Attr(sectionHeading, "Text"), StringComparison.Ordinal);
        Assert.Contains("Title", NameOf(sectionHeading), StringComparison.Ordinal);
        Assert.Equal(Presentation + "GroupBox.Header", sectionHeading.Parent!.Name);

        XElement field = Assert.Single(sections.Descendants(Presentation + "TextBox"));
        Assert.Contains("AccessibleName", NameOf(field), StringComparison.Ordinal);
        Assert.Contains("Mode=OneWay", Attr(field, "Text"), StringComparison.Ordinal);
        Assert.Contains("Header", Attr(field, "AutomationProperties.LabeledBy"), StringComparison.Ordinal);
        Assert.Contains("GroupBox", Attr(field, "AutomationProperties.LabeledBy"), StringComparison.Ordinal);
        Assert.Contains("DetailFieldStyle", Attr(field, "Style"), StringComparison.Ordinal);

        // The box is named by the label alone (DetailFieldViewModel.AccessibleName, never the
        // value): the read-only TextBox exposes the value through the Value pattern, and a name
        // of "label: value" would make a screen reader speak the value twice on every Tab.
        Assert.DoesNotContain("Value", NameOf(field), StringComparison.Ordinal);
        Assert.DoesNotContain("MultiBinding", NameOf(field), StringComparison.Ordinal);
        Assert.Equal("Label", new DetailFieldViewModel("Label", "value").AccessibleName);
        Assert.Equal("Label", new DetailFieldViewModel("Label", "value", isMultiline: true).AccessibleName);
        Assert.Equal(Strings.HexDumpName, new DetailFieldViewModel("Hex dump", "00", isMultiline: true, accessibleName: Strings.HexDumpName).AccessibleName);

        // The field label sits above its value box, each the full width of the pane (a label as
        // long as a UEFI attribute name wraps instead of squeezing the value), and is named by the
        // label alone; the box below it carries the same label and the value itself.
        XElement fieldLabel = Assert.Single(field.Parent!.Elements(Presentation + "TextBlock"));
        Assert.Equal(Presentation + "StackPanel", field.Parent!.Name);
        Assert.Contains("Label", Attr(fieldLabel, "Text"), StringComparison.Ordinal);
        Assert.Contains("Label", NameOf(fieldLabel), StringComparison.Ordinal);
        Assert.DoesNotContain("AccessibleName", NameOf(fieldLabel), StringComparison.Ordinal);
        Assert.Equal("Wrap", Attr(fieldLabel, "TextWrapping"));
        Assert.DoesNotContain("SharedSizeGroup", MainWindowMarkup(), StringComparison.Ordinal);
        Assert.DoesNotContain("IsSharedSizeScope", MainWindowMarkup(), StringComparison.Ordinal);

        XElement fieldStyle = window.Descendants(Presentation + "Style").Single(style => Attr(style, Xaml + "Key") == "DetailFieldStyle");
        Assert.Equal("TextBox", Attr(fieldStyle, "TargetType"));
        Assert.Equal("True", SetterValue(fieldStyle, "IsReadOnly"));

        // A single-line value wraps and is selected whole when keyboard focus arrives: NVDA reads
        // a focused text box up to the end of the visual line at the caret unless text is
        // selected, and a wrapped value was heard only to its first visual line (round 1: Device
        // path heard as "HD"; DetailFieldWrappingTests). Only a multi-line value stays unwrapped.
        Assert.Equal("Wrap", SetterValue(fieldStyle, "TextWrapping"));
        XElement multiline = Assert.Single(fieldStyle.Descendants(Presentation + "DataTrigger"));
        Assert.Contains("IsMultiline", Attr(multiline, "Binding"), StringComparison.Ordinal);
        Assert.Equal("NoWrap", SetterValue(multiline, "TextWrapping"));
        Assert.Equal("True", SetterValue(multiline, "AcceptsReturn"));
        Assert.Contains("Consolas", SetterValue(multiline, "FontFamily"), StringComparison.Ordinal);

        XElement readOnlyStatus = Named(window, "TextBox", "ReadOnlyStatusBox");
        Assert.Equal("True", Attr(readOnlyStatus, "IsReadOnly"));
        Assert.Contains("Detail.ReadOnlyStatus", Attr(readOnlyStatus, "Text"), StringComparison.Ordinal);
        Assert.Contains("res:Strings.ReadOnlyStatusName", NameOf(readOnlyStatus), StringComparison.Ordinal);
        Assert.DoesNotMatch(WriteImplyingWord, Strings.ReadOnlyStatusName);

        XElement pane = Named(window, "ScrollViewer", "DetailPane");
        Assert.Contains("res:Strings.DetailPaneName", NameOf(pane), StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(Attr(pane, "PreviewKeyDown")), "the detail pane needs a PreviewKeyDown handler for Escape");
    }

    /// <summary>
    /// The Change group is a stock GroupBox inside the Detail
    /// region, right after the Writability line (no seventh region), named Change with its header
    /// help, collapsed unless the Presentation surface shows it, and inside it each control
    /// collapsed unless the variable shown needs it (a collapsed control is no tab stop). The boot
    /// order list is a single-selection ListBox over the proposed order, its selection two-way, its
    /// rows styled by their own keyed style, and its keys handled by the code-behind; nothing in the
    /// group is a live region or a range value. The variable rows carry their item status, and the
    /// Writability line falls back to the no-selection text, never to a sentence about every
    /// variable.
    /// </summary>
    [Fact]
    public void ChangeGroupFollowsTheWritabilityLineAndShowsOnlyWhatTheVariableNeeds()
    {
        XElement window = LoadMainWindow();
        XElement detail = Named(window, "GroupBox", "DetailRegion");
        XElement change = Named(window, "GroupBox", "ChangeGroup");
        XElement writability = Named(window, "TextBox", "ReadOnlyStatusBox");
        Assert.Contains(detail, change.Ancestors());
        Assert.Same(writability.Parent, change.Parent);
        Assert.Same(writability, change.ElementsBeforeSelf().Last());
        Assert.Equal("{x:Static res:Strings.ChangeGroupName}", Attr(change, "Header"));
        Assert.Equal("{x:Static res:Strings.ChangeGroupName}", NameOf(change));
        Assert.Equal("Change", Strings.ChangeGroupName);
        Assert.Equal("{Binding Detail.ReadOnlyStatus, Mode=OneWay, FallbackValue={x:Static res:Strings.NoSelectionHeading}}", Attr(writability, "Text"));

        // Collapsed unless shown; each control of the group collapsed unless the variable needs it.
        const string Converter = ", Mode=OneWay, Converter={x:Static app:MainWindow.VisibleWhenTrue}}";
        Assert.Equal("{Binding Detail.Change.IsVisible" + Converter, Attr(change, "Visibility"));
        Assert.Equal("{Binding Detail.Change.ShowsBootOrderControls" + Converter, Attr(Named(window, "StackPanel", "BootOrderControls"), "Visibility"));
        Assert.Equal("{Binding Detail.Change.ShowsBootOnce" + Converter, Attr(Named(window, "Button", "BootOnceButton"), "Visibility"));
        Assert.Equal("{Binding Detail.Change.ShowsClearBootNext" + Converter, Attr(Named(window, "Button", "ClearBootNextButton"), "Visibility"));
        Assert.Equal("{Binding Detail.Change.ShowsRestore" + Converter, Attr(Named(window, "Button", "RestorePreviousButton"), "Visibility"));
        XElement bootOrderControls = Named(window, "StackPanel", "BootOrderControls");
        foreach (string name in new[] { "BootOrderList", "MoveUpButton", "MoveDownButton", "ReviewChangeButton" })
        {
            Assert.Contains(bootOrderControls, window.Descendants().Single(element => Attr(element, Xaml + "Name") == name).Ancestors());
        }

        string code = global::UefiAccess.Core.Tests.MainWindowMarkup.Source("MainWindow.xaml.cs");
        Assert.Contains("public static IValueConverter VisibleWhenTrue { get; } = new BooleanToVisibilityConverter();", code, StringComparison.Ordinal);

        // The boot order list: the proposed order, single selection, the selection two-way, its own
        // keyed row style, the list help, and the keys stock WPF does not give.
        XElement list = Named(window, "ListBox", "BootOrderList");
        Assert.Equal("{Binding Detail.Change.Editor.Entries, Mode=OneWay}", Attr(list, "ItemsSource"));
        Assert.Equal("{Binding Detail.Change.Editor.SelectedEntry, Mode=TwoWay}", Attr(list, "SelectedItem"));
        Assert.Equal("Single", Attr(list, "SelectionMode"));
        Assert.Equal("{x:Static res:Strings.BootOrderEntriesName}", NameOf(list));
        Assert.Equal("OnBootOrderListPreviewKeyDown", Attr(list, "PreviewKeyDown"));
        Assert.Equal(string.Empty, Attr(list, "KeyboardNavigation.TabNavigation"));
        Assert.Equal(Presentation + "ListBox", list.Name);
        Assert.Equal(3, window.Descendants(Presentation + "ItemsControl").Count());
        XElement rowText = Assert.Single(list.Descendants(Presentation + "TextBlock"));
        Assert.Equal("{Binding AccessibleName, Mode=OneWay}", NameOf(rowText));
        Assert.Equal("Wrap", Attr(rowText, "TextWrapping"));

        // Nothing in the group is a live region, a range value or on a timer; the Status box stays
        // the one polite live region (ExactlyOneLiveRegionIsPoliteAndTheLogIsOff).
        Assert.DoesNotContain(change.DescendantsAndSelf(), element => element.Attribute("AutomationProperties.LiveSetting") is not null);
        Assert.DoesNotContain(
            change.DescendantsAndSelf().SelectMany(element => element.Attributes()),
            attribute => attribute.Name.LocalName.Contains("RangeValue", StringComparison.Ordinal) || attribute.Value.Contains("RangeValue", StringComparison.Ordinal));
        Assert.Empty(change.Descendants(Presentation + "Slider"));
        Assert.Empty(change.Descendants(Presentation + "TextBox"));

        // D3: the variable rows carry their item status, bound to the row's own value; since M4 the
        // Dell rows carry none (no Dell row can change).
        XElement rowStyle = VariableRowStyle(window);
        Assert.Equal("{Binding ItemStatus, Mode=OneWay}", SetterValue(rowStyle, "AutomationProperties.ItemStatus"));
        Assert.DoesNotContain(window.Descendants(), element => element.Attribute("AutomationProperties.ItemStatus") is not null);
        Assert.Single(window.Descendants(Presentation + "Setter"), setter => Attr(setter, "Property") == "AutomationProperties.ItemStatus");

        // The filter box and the two combo boxes are disabled while a change is written.
        foreach ((string type, string name) in new[] { ("TextBox", "FilterBox"), ("ComboBox", "GroupByBox"), ("ComboBox", "SortByBox") })
        {
            Assert.Equal("{Binding IsFilterEnabled, Mode=OneWay}", Attr(Named(window, type, name), "IsEnabled"));
        }
    }

    /// <summary>
    /// The window's half (the shell's half is
    /// ShellWriteFlowTests): the window does not close while a change is written, and the
    /// application refuses the end of the Windows session meanwhile; Escape while the
    /// checks run cancels them before any other Escape rule; Ctrl+Z in the boot order list runs the
    /// reset command and Enter and Space on its rows do nothing; a Change button disabled under the
    /// focus it holds hands focus to Status only if it is still disabled after the key press; the
    /// three new focus targets reach the moved row, the button that opened the dialog (waiting for
    /// it to be enabled again, by its IsEnabledChanged event, never a timer) and the written
    /// variable's row or the first row; a moved row that lost its container gets focus back; and
    /// the platform opens the Confirm change dialog modal to the main window.
    /// </summary>
    [Fact]
    public void CodeBehindKeepsTheWindowOpenWhileWritingAndHandlesTheChangeKeysAndFocusTargets()
    {
        string code = global::UefiAccess.Core.Tests.MainWindowMarkup.Source("MainWindow.xaml.cs");

        string closing = Method(code, "protected override void OnClosing(");
        Assert.Contains("if (_shell is not null && !_shell.TryClose())", closing, StringComparison.Ordinal);
        Assert.Contains("e.Cancel = true;", closing, StringComparison.Ordinal);
        Assert.Contains("base.OnClosing(e);", closing, StringComparison.Ordinal);

        // A log off, shut down or restart does not honour a cancelled Closing, so the application
        // refuses the end of the session through the same TryClose while a change is written.
        string app = global::UefiAccess.Core.Tests.MainWindowMarkup.Source("App.xaml.cs");
        string sessionEnding = Method(app, "protected override void OnSessionEnding(");
        Assert.Contains("if (Shell is not null && !Shell.TryClose())", sessionEnding, StringComparison.Ordinal);
        Assert.Contains("e.Cancel = true;", sessionEnding, StringComparison.Ordinal);
        Assert.Contains("base.OnSessionEnding(e);", sessionEnding, StringComparison.Ordinal);

        string windowKeys = Method(code, "private void OnWindowPreviewKeyDown(");
        int cancel = windowKeys.IndexOf("_shell.Detail.Change.CancelChecks()", StringComparison.Ordinal);
        Assert.True(cancel >= 0 && cancel < windowKeys.IndexOf("Key.F6", StringComparison.Ordinal), "Escape cancels the running checks before any other key rule");
        Assert.Contains("e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None", windowKeys, StringComparison.Ordinal);

        string listKeys = Method(code, "private void OnBootOrderListPreviewKeyDown(");
        Assert.Contains("e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control", listKeys, StringComparison.Ordinal);
        Assert.Contains("_shell.Detail.Change.ResetOrderCommand", listKeys, StringComparison.Ordinal);
        Assert.Contains("e.Key is Key.Enter or Key.Space", listKeys, StringComparison.Ordinal);
        Assert.DoesNotContain("MoveUp", listKeys, StringComparison.Ordinal);

        string disabled = Method(code, "private void OnChangeButtonIsEnabledChanged(");
        Assert.Contains("Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => MoveFocusOffDisabledChangeButton(button)))", disabled, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(button, _changeControlWaiting)", disabled, StringComparison.Ordinal);
        Assert.Contains("!button.IsEnabled && button.IsKeyboardFocused && FocusElement(StatusBox)", Method(code, "private void MoveFocusOffDisabledChangeButton("), StringComparison.Ordinal);
        Assert.Contains("button.IsEnabledChanged += OnChangeButtonIsEnabledChanged;", code, StringComparison.Ordinal);
        Assert.Contains("new[] { MoveUpButton, MoveDownButton, ReviewChangeButton, BootOnceButton, ClearBootNextButton, RestorePreviousButton }", code, StringComparison.Ordinal);

        string moveFocus = Method(code, "public bool MoveFocusTo(");
        Assert.Contains("FocusTarget.BootOrderEntry => FocusBootOrderRow(_shell?.Detail.Change.Editor.SelectedEntry)", moveFocus, StringComparison.Ordinal);
        Assert.Contains("FocusTarget.ChangeControl => FocusChangeControl()", moveFocus, StringComparison.Ordinal);
        Assert.Contains("FocusTarget.ListRowByKey => FocusListRow(RowOfFocusKey()) || FocusListRow(FirstVisibleItem())", moveFocus, StringComparison.Ordinal);
        Assert.Contains("_shell.List.FindVisible(key)", Method(code, "private VariableItemViewModel? RowOfFocusKey("), StringComparison.Ordinal);

        string changeControl = Method(code, "private bool FocusChangeControl(");
        foreach (string pair in new[] { "ChangeAction.ReviewChange => ReviewChangeButton", "ChangeAction.BootOnce => BootOnceButton", "ChangeAction.ClearBootNext => ClearBootNextButton", "ChangeAction.RestorePrevious => RestorePreviousButton" })
        {
            Assert.Contains(pair, changeControl, StringComparison.Ordinal);
        }

        Assert.Contains("_changeControlWaiting = button;", changeControl, StringComparison.Ordinal);

        // A move keeps the row's container; if WPF regenerated it, one deferred step puts focus back.
        Assert.Contains("Editor.Entries.CollectionChanged += OnBootOrderEntriesCollectionChanged", code, StringComparison.Ordinal);
        Assert.Contains("Editor.Entries.CollectionChanged -= OnBootOrderEntriesCollectionChanged", code, StringComparison.Ordinal);
        string moved = Method(code, "private void OnBootOrderEntriesCollectionChanged(");
        Assert.Contains("NotifyCollectionChangedAction.Move", moved, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(KeepBootOrderRowFocused))", moved, StringComparison.Ordinal);
        Assert.Contains("FocusBootOrderRow(row)", Method(code, "private void KeepBootOrderRowFocused("), StringComparison.Ordinal);

        // The dialog: the application's own window, modal to the main window, and the decision the
        // view model gives; the window itself names no dialog and no pipeline.
        string platform = global::UefiAccess.Core.Tests.MainWindowMarkup.Source("Platform", "WpfPlatformInteractions.cs");
        string confirm = Method(platform, "public ConfirmDecision ConfirmChange(");
        Assert.Contains("ConfirmChangeWindow dialog = new(viewModel, _logger);", confirm, StringComparison.Ordinal);
        Assert.Contains("dialog.Owner = owner;", confirm, StringComparison.Ordinal);
        Assert.Contains("dialog.ShowDialog() == true", confirm, StringComparison.Ordinal);
        Assert.Contains("return confirmed ? viewModel.Confirm() : viewModel.Cancel();", confirm, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfirmChangeWindow", code, StringComparison.Ordinal);
        Assert.DoesNotContain("CommitAsync", code, StringComparison.Ordinal);
        Assert.DoesNotContain("PrepareAsync", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// M3 review, the window's half (the shell's half is ShellWriteFlowTests): while a change is
    /// written the Filter region has no enabled control, so F6 and Shift+F6 go on in their direction
    /// to the next region that can take focus, and only the region focus lands in is announced
    /// (the Status box can always take focus, so the walk ends within one round); Ctrl+F on the
    /// disabled filter box has the shell say so instead of nothing; and every toolbar button the
    /// shell can disable under the focus it holds (Open baseline folder and Export visible list while
    /// the checks run, Clear filter while a change is written) hands focus to the Status box, as the
    /// load buttons do, instead of leaving it to WPF.
    /// </summary>
    [Fact]
    public void F6SkipsARegionThatCannotTakeFocus_CtrlFSaysWhyItCannot_AndDisabledToolbarButtonsHandFocusToStatus()
    {
        string code = global::UefiAccess.Core.Tests.MainWindowMarkup.Source("MainWindow.xaml.cs");

        string windowKeys = Method(code, "private void OnWindowPreviewKeyDown(");
        Assert.Contains("FocusFirstRegionFrom(target, backwards);", windowKeys, StringComparison.Ordinal);
        Assert.DoesNotContain("if (FocusRegion(target))", windowKeys, StringComparison.Ordinal);
        Assert.DoesNotContain("OnRegionArrived(", windowKeys, StringComparison.Ordinal);
        Assert.Contains("if (!FocusFilter(selectAll: true))", windowKeys, StringComparison.Ordinal);
        Assert.Contains("_shell?.OnFilterUnavailable();", windowKeys, StringComparison.Ordinal);

        string walk = Method(code, "private void FocusFirstRegionFrom(");
        Assert.Contains("for (int step = 0; step < RegionOrder.All.Count; step++)", walk, StringComparison.Ordinal);
        Assert.Contains("if (FocusRegion(candidate))", walk, StringComparison.Ordinal);
        Assert.Contains("_shell?.OnRegionArrived(candidate);", walk, StringComparison.Ordinal);
        Assert.Contains("candidate = backwards ? RegionOrder.Previous(candidate) : RegionOrder.Next(candidate);", walk, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(walk, "OnRegionArrived("));
        Assert.Contains("Region.Status => FocusElement(StatusBox)", Method(code, "private bool FocusRegion("), StringComparison.Ordinal);
        Assert.Contains("if (!FocusElement(FilterBox))", Method(code, "private bool FocusFilter("), StringComparison.Ordinal);

        foreach (string button in new[] { "RefreshButton", "OpenBaselineButton", "ClearFilterButton", "ExportVisibleListButton" })
        {
            Assert.Contains(button + ".IsEnabledChanged += OnLoadButtonIsEnabledChanged;", code, StringComparison.Ordinal);
        }

        Assert.Contains("e.NewValue is false && sender is UIElement { IsKeyboardFocused: true } && FocusElement(StatusBox)", Method(code, "private void OnLoadButtonIsEnabledChanged("), StringComparison.Ordinal);
    }

    /// <summary>
    /// The seventh region, Dell settings, is a stock GroupBox
    /// after the Log region in the panes grid's Auto row, named by its header and with no help text
    /// (it would be heard on every F6), whose two keyboard focus handlers tell the shell which part
    /// has focus. Inside it, in a grid of three Auto rows and in document order: the Read Dell
    /// settings button (24, the shell's command, never disabled: no IsEnabled binding, not a default
    /// button), the state line (25, read-only, wrapping, its value boxes' arrival handlers) and the
    /// list of Dell BIOS settings (26): a ListView in GridView mode, single selection bound two-way
    /// to the selected row, not virtualised (all ten rows always in the UI Automation tree), letter
    /// search by the setting's name, the keys handled by the code-behind, and three columns whose
    /// cells are wrapping text blocks named by their text. The rows' keyed style names each row by
    /// its sentence and carries no item status and no help text (D3, A2-6). Nothing in the region is
    /// a live region or a range value.
    /// </summary>
    [Fact]
    public void DellRegionHoldsTheButtonTheSourceLineAndTheList()
    {
        XElement window = LoadMainWindow();
        XElement region = Named(window, "GroupBox", "DellRegion");
        Assert.Equal("{x:Static res:Strings.RegionDell}", Attr(region, "Header"));
        Assert.Equal("{x:Static res:Strings.RegionDell}", NameOf(region));
        Assert.Equal("Dell settings", Strings.RegionDell);
        Assert.Equal(string.Empty, Attr(region, "AutomationProperties.HelpText"));
        Assert.Equal("OnDellRegionGotKeyboardFocus", Attr(region, "GotKeyboardFocus"));
        Assert.Equal("OnDellRegionLostKeyboardFocus", Attr(region, "LostKeyboardFocus"));
        Assert.Equal("OnLayoutPartSizeChanged", Attr(region, "SizeChanged"));
        Assert.Same(Named(window, "Grid", "PanesGrid"), region.Parent);
        Assert.Equal("5", Attr(region, "Grid.Row"));
        Assert.Same(Named(window, "GroupBox", "LogRegion"), region.ElementsBeforeSelf().Last());

        // Three Auto rows, the three stops in document order, one per row.
        XElement grid = Assert.Single(region.Elements(Presentation + "Grid"));
        Assert.Equal(new[] { "Auto", "Auto", "Auto" }, grid.Element(Presentation + "Grid.RowDefinitions")!.Elements(Presentation + "RowDefinition").Select(row => Attr(row, "Height")));
        string[] controls = { "Button", "TextBox", "ListView", "ListBox", "ComboBox", "AnnouncingGridSplitter", "Expander" };
        List<XElement> stops = region.Descendants().Where(element => controls.Contains(element.Name.LocalName, StringComparer.Ordinal)).ToList();
        Assert.Equal(new[] { "ReadDellSettingsButton", "DellSourceBox", "DellSettingsList" }, stops.Select(element => Attr(element, Xaml + "Name")));
        Assert.Equal(new[] { "0", "1", "2" }, stops.Select(element => Attr(element, "Grid.Row")));
        Assert.Equal(new int?[] { 24, 25, 26 }, stops.Select(TabIndexOf));

        // The button: the shell's command, never disabled and never the default button.
        XElement button = stops[0];
        Assert.Equal("{Binding " + nameof(ShellViewModel.ReadDellSettingsCommand) + "}", Attr(button, "Command"));
        Assert.Equal("{x:Static res:Strings.ReadDellSettingsHelp}", Attr(button, "AutomationProperties.HelpText"));
        Assert.Equal("{StaticResource ToolbarButtonStyle}", Attr(button, "Style"));
        foreach (string attribute in new[] { "IsEnabled", "IsDefault", "Click", "Visibility" })
        {
            Assert.Equal(string.Empty, Attr(button, attribute));
        }

        // The state line: read-only, wrapping, the implicit text box style, the arrival handlers.
        XElement source = stops[1];
        Assert.Equal("{Binding Dell.StateText, Mode=OneWay}", Attr(source, "Text"));
        Assert.Equal("{x:Static res:Strings.DellSourceName}", NameOf(source));
        Assert.Equal("True", Attr(source, "IsReadOnly"));
        Assert.Equal("True", Attr(source, "IsReadOnlyCaretVisible"));
        Assert.Equal("Wrap", Attr(source, "TextWrapping"));
        Assert.Equal(string.Empty, Attr(source, "Style"));
        Assert.Equal(string.Empty, Attr(source, "AcceptsReturn"));
        Assert.Equal("OnDellSourceGotKeyboardFocus", Attr(source, "GotKeyboardFocus"));
        Assert.Equal("OnDellSourceLostKeyboardFocus", Attr(source, "LostKeyboardFocus"));

        // The list.
        XElement list = stops[2];
        Assert.Equal("{Binding Dell.Rows, Mode=OneWay}", Attr(list, "ItemsSource"));
        Assert.Equal("{Binding Dell.SelectedRow, Mode=TwoWay}", Attr(list, "SelectedItem"));
        Assert.Equal("Single", Attr(list, "SelectionMode"));
        Assert.Equal("{x:Static res:Strings.DellSettingsListName}", NameOf(list));
        Assert.Equal("{x:Static res:Strings.DellSettingsListHelp}", Attr(list, "AutomationProperties.HelpText"));
        Assert.Equal("False", Attr(list, "VirtualizingPanel.IsVirtualizing"));
        Assert.Empty(list.Descendants(Presentation + "VirtualizingStackPanel"));
        Assert.Null(list.Element(Presentation + "ListView.ItemsPanel"));
        Assert.Equal("True", Attr(list, "IsTextSearchEnabled"));
        Assert.Equal(nameof(DellSettingRowViewModel.DisplayName), Attr(list, "TextSearch.TextPath"));
        Assert.Equal("{StaticResource DellSettingRowStyle}", Attr(list, "ItemContainerStyle"));
        Assert.Equal("OnDellListPreviewKeyDown", Attr(list, "PreviewKeyDown"));
        Assert.Equal(string.Empty, Attr(list, "MaxHeight"));
        Assert.Equal(string.Empty, Attr(list, "Height"));

        // Three columns, each cell a wrapping text block named by its text.
        List<XElement> columns = list.Descendants(Presentation + "GridViewColumn").ToList();
        Assert.Equal(new[] { "{x:Static res:Strings.ColumnDellSetting}", "{x:Static res:Strings.ColumnDellCurrent}", "{x:Static res:Strings.ColumnDellPossible}" }, columns.Select(column => Attr(column, "Header")));
        Assert.Equal(new[] { Strings.ColumnDellSetting, Strings.ColumnDellCurrent, Strings.ColumnDellPossible }, new[] { "Setting", "Current value", "Possible values" });
        Assert.Equal(new[] { "220", "220", "420" }, columns.Select(column => Attr(column, "Width")));
        string[] paths = { nameof(DellSettingRowViewModel.DisplayName), nameof(DellSettingRowViewModel.CurrentText), nameof(DellSettingRowViewModel.PossibleText) };
        for (int index = 0; index < columns.Count; index++)
        {
            XElement cell = Assert.Single(columns[index].Descendants(Presentation + "TextBlock"));
            string binding = "{Binding " + paths[index] + ", Mode=OneWay}";
            Assert.Equal(binding, Attr(cell, "Text"));
            Assert.Equal(binding, NameOf(cell));
            Assert.Equal("Wrap", Attr(cell, "TextWrapping"));
        }

        Assert.Equal("False", Attr(Assert.Single(list.Descendants(Presentation + "GridView")), "AllowsColumnReorder"));

        // The rows' style: keyed, not based on the variable rows' style, the sentence as the name,
        // no item status, no help text, no template, the system's colours and font.
        XElement rowStyle = global::UefiAccess.Core.Tests.MainWindowMarkup.KeyedStyle(window, "DellSettingRowStyle");
        Assert.Equal("ListViewItem", Attr(rowStyle, "TargetType"));
        Assert.Equal(string.Empty, Attr(rowStyle, "BasedOn"));
        Assert.Equal("{Binding " + nameof(DellSettingRowViewModel.AccessibleName) + ", Mode=OneWay}", SetterValue(rowStyle, "AutomationProperties.Name"));
        Assert.Equal("Stretch", SetterValue(rowStyle, "HorizontalContentAlignment"));
        Assert.Equal("2", SetterValue(rowStyle, "BorderThickness"));
        foreach (string property in new[] { "AutomationProperties.ItemStatus", "AutomationProperties.HelpText", "Template", "FontSize", "Foreground", "Background" })
        {
            Assert.DoesNotContain(rowStyle.Descendants(Presentation + "Setter"), setter => Attr(setter, "Property") == property);
        }

        // The view models behind the bindings: the row exposes no command and no item status.
        Assert.Null(typeof(DellSettingRowViewModel).GetProperty("ItemStatus"));
        Assert.DoesNotContain(typeof(DellSettingRowViewModel).GetProperties(), property => typeof(System.Windows.Input.ICommand).IsAssignableFrom(property.PropertyType));
        Assert.NotNull(typeof(DellSettingsViewModel).GetProperty(nameof(DellSettingsViewModel.StateText)));

        // Nothing in the region is a live region, a range value or an item status.
        Assert.DoesNotContain(region.DescendantsAndSelf(), element => element.Attribute("AutomationProperties.LiveSetting") is not null);
        Assert.DoesNotContain(region.DescendantsAndSelf(), element => element.Attribute("AutomationProperties.ItemStatus") is not null);
        Assert.DoesNotContain(
            region.DescendantsAndSelf().SelectMany(element => element.Attributes()),
            attribute => attribute.Name.LocalName.Contains("RangeValue", StringComparison.Ordinal) || attribute.Value.Contains("RangeValue", StringComparison.Ordinal));
    }

    /// <summary>
    /// The window's half of the Dell settings region (the
    /// shell's half is ShellDellTests): the focus target of a Dell answer and of F6 is the selected
    /// Dell row, after a layout pass so its container exists, with the Read Dell settings button as
    /// the fallback; the region is known to RegionOf, so F6 and Shift+F6 continue from it; Ctrl+C on
    /// a Dell row goes through the shell (which says a failed copy) and Enter and Space on it do
    /// nothing, with Ctrl or Shift down too; the region's two keyboard focus handlers tell the shell
    /// which part has focus, and None once focus left it, so Copy value (Alt+Y) copies the Dell row
    /// or the state line there, and a Dell row that gets focus unselected is selected first; every
    /// move of keyboard focus in the window tells the shell the person moved on (section 5.7); the
    /// button is never disabled, so nothing hands focus away from it. The window itself never reads
    /// the Dell settings and touches no clipboard.
    /// </summary>
    [Fact]
    public void CodeBehindFocusesTheSelectedDellRowAndTellsTheShellWhichDellPartHasFocus()
    {
        string code = global::UefiAccess.Core.Tests.MainWindowMarkup.Source("MainWindow.xaml.cs");

        string moveFocus = Method(code, "public bool MoveFocusTo(");
        Assert.Contains("FocusTarget.DellSelectedRow => FocusDellRow(_shell?.Dell.SelectedRow", moveFocus, StringComparison.Ordinal);
        Assert.Contains("|| FocusElement(ReadDellSettingsButton),", moveFocus, StringComparison.Ordinal);
        Assert.Contains("Region.Dell => MoveFocusTo(FocusTarget.DellSelectedRow)", Method(code, "private bool FocusRegion("), StringComparison.Ordinal);
        string regionOf = Method(code, "private Region? RegionOf(");
        Assert.Contains("if (ReferenceEquals(current, DellRegion))", regionOf, StringComparison.Ordinal);
        Assert.Contains("return Region.Dell;", regionOf, StringComparison.Ordinal);

        // The splitters still sit between the regions they separate; the Dell region has none.
        Assert.Contains("(Region.Detail, Region.Log)", Method(code, "private (Region Before, Region After)? SplitterRegions("), StringComparison.Ordinal);
        Assert.DoesNotContain("Region.Dell", Method(code, "private (Region Before, Region After)? SplitterRegions("), StringComparison.Ordinal);

        string focusRow = Method(code, "private bool FocusDellRow(");
        int layout = focusRow.IndexOf("DellSettingsList.UpdateLayout();", StringComparison.Ordinal);
        Assert.True(layout >= 0 && layout < focusRow.IndexOf("ContainerFromItem(row)", StringComparison.Ordinal), "the row's container is looked up after a layout pass");
        Assert.Contains("FocusElement(container)", focusRow, StringComparison.Ordinal);
        Assert.DoesNotContain("ScrollIntoView", focusRow, StringComparison.Ordinal);

        string keys = Method(code, "private void OnDellListPreviewKeyDown(");
        Assert.Contains("e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control", keys, StringComparison.Ordinal);
        Assert.Contains("_shell.CopyDellRow(", keys, StringComparison.Ordinal);
        Assert.Contains("e.Key is Key.Enter or Key.Space", keys, StringComparison.Ordinal);
        Assert.DoesNotContain("Key.Escape", keys, StringComparison.Ordinal);

        // Ctrl+Space cannot take the selection off a row: Enter and Space are handled with Ctrl or
        // Shift down too, only Alt is left alone (the window menu).
        Assert.Contains("(Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.None && e.Key is Key.Enter or Key.Space && row is not null", keys, StringComparison.Ordinal);

        foreach (string handler in new[] { "private void OnDellRegionGotKeyboardFocus(", "private void OnDellRegionLostKeyboardFocus(" })
        {
            Assert.Contains("_shell?.OnDellFocus(DellFocusPartOf(e.NewFocus));", Method(code, handler), StringComparison.Ordinal);
        }

        // A Dell row that gets focus unselected (Ctrl+Up, Ctrl+Down, a screen reader's focus
        // request) is selected before the shell is told, so Alt+Y, F6 and a Dell answer act on the
        // row focus is on; a click stays the stock list's.
        string dellGot = Method(code, "private void OnDellRegionGotKeyboardFocus(");
        Assert.Contains("e.NewFocus is ListViewItem { IsSelected: false } item", dellGot, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(item), DellSettingsList)", dellGot, StringComparison.Ordinal);
        Assert.Contains("Mouse.LeftButton == MouseButtonState.Released", dellGot, StringComparison.Ordinal);
        int select = dellGot.IndexOf("item.IsSelected = true;", StringComparison.Ordinal);
        Assert.True(select >= 0 && select < dellGot.IndexOf("_shell?.OnDellFocus(", StringComparison.Ordinal), "the focused Dell row is selected before the shell is told where focus is");

        // Every move of keyboard focus in the window (Tab, a click,
        // a context menu, F6), also one a control marked handled, tells the shell the person moved
        // on, so a Dell answer then asks for no focus; the handler says nothing and moves nothing.
        string constructor = Method(code, "public MainWindow()");
        Assert.Contains("AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnWindowKeyboardFocusChanged), true);", constructor, StringComparison.Ordinal);
        Assert.Contains("AddHandler(Keyboard.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnWindowKeyboardFocusChanged), true);", constructor, StringComparison.Ordinal);
        string moved = Method(code, "private void OnWindowKeyboardFocusChanged(");
        Assert.Contains("if (!ReferenceEquals(e.OldFocus, e.NewFocus))", moved, StringComparison.Ordinal);
        Assert.Contains("_shell?.OnKeyboardFocusMoved();", moved, StringComparison.Ordinal);
        foreach (string forbidden in new[] { "Announce", "Focus(", "MoveFocusTo", "e.Handled", "IsSelected", "BeginInvoke", "Timer" })
        {
            Assert.DoesNotContain(forbidden, moved, StringComparison.Ordinal);
        }

        Assert.Equal(1, CountOf(code, "_shell?.OnKeyboardFocusMoved()"));

        string part = Method(code, "private DellFocusPart DellFocusPartOf(");
        Assert.True(part.IndexOf("IsOffTheWindow(element)", StringComparison.Ordinal) >= 0 && part.IndexOf("IsOffTheWindow(element)", StringComparison.Ordinal) < part.IndexOf("return DellFocusPart.Button;", StringComparison.Ordinal), "focus off the window (a context menu, another window) is no part of the region");
        foreach (string pair in new[] { "ReferenceEquals(current, ReadDellSettingsButton)", "return DellFocusPart.Button;", "ReferenceEquals(current, DellSourceBox)", "return DellFocusPart.SourceLine;", "ReferenceEquals(current, DellSettingsList)", "return DellFocusPart.List;", "return DellFocusPart.None;" })
        {
            Assert.Contains(pair, part, StringComparison.Ordinal);
        }

        // Never disabled: no handler hands focus away from the button, and the window never reads.
        Assert.DoesNotContain("ReadDellSettingsButton.IsEnabledChanged", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadDellSettings(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SetClipboardText(", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VariableListRowsAndGroupsAreNamedSentences()
    {
        XElement window = LoadMainWindow();
        XElement groups = Named(window, "ItemsControl", "GroupsControl");
        Assert.Contains("List.Groups", Attr(groups, "ItemsSource"), StringComparison.Ordinal);
        Assert.Equal("Once", Attr(groups, "KeyboardNavigation.TabNavigation"));
        Assert.False(string.IsNullOrWhiteSpace(Attr(groups, "PreviewKeyDown")), "the list needs a PreviewKeyDown handler for Enter, Space, Ctrl+C and the group keys");

        XElement expander = Assert.Single(groups.Descendants(Presentation + "Expander"));
        Assert.Contains("AccessibleName", NameOf(expander), StringComparison.Ordinal);
        Assert.Contains("AccessibleName", Attr(expander, "Header"), StringComparison.Ordinal);
        Assert.Contains("IsExpanded", Attr(expander, "IsExpanded"), StringComparison.Ordinal);
        Assert.Contains("TwoWay", Attr(expander, "IsExpanded"), StringComparison.Ordinal);

        XElement listView = Assert.Single(expander.Descendants(Presentation + "ListView"));

        // The rows grid is named by the group label alone ("EFI global variable rows"), never by
        // the header sentence the expander has just spoken (finding A1-1).
        Assert.Equal("{Binding RowsAccessibleName, Mode=OneWay}", NameOf(listView));
        VariableGroupViewModel group = new("8be4df61-93ca-11d2-aa0d-00e098032b8c", "EFI global variable", Array.Empty<VariableItemViewModel>());
        Assert.Equal("EFI global variable rows", group.RowsAccessibleName);
        Assert.Equal("EFI global variable, 0 items", group.AccessibleName);
        Assert.DoesNotContain(group.AccessibleName, group.RowsAccessibleName, StringComparison.Ordinal);

        Assert.Equal("Single", Attr(listView, "SelectionMode"));
        Assert.Contains("Items", Attr(listView, "ItemsSource"), StringComparison.Ordinal);
        Assert.Contains("List.SelectedItem", Attr(listView, "SelectedItem"), StringComparison.Ordinal);
        Assert.Contains("Mode=OneWay", Attr(listView, "SelectedItem"), StringComparison.Ordinal);
        Assert.Equal("Name", Attr(listView, "TextSearch.TextPath"));
        Assert.False(string.IsNullOrWhiteSpace(Attr(listView, "SelectionChanged")), "the per-group lists need a SelectionChanged handler that forwards the selection to the shell");
        Assert.Single(listView.Descendants(Presentation + "VirtualizingStackPanel"));

        XElement rowStyle = Assert.Single(listView.Descendants(Presentation + "Style"), style => Attr(style, "TargetType") == "ListViewItem");
        Assert.Same(VariableRowStyle(window), rowStyle);
        Assert.Contains("AccessibleName", SetterValue(rowStyle, "AutomationProperties.Name"), StringComparison.Ordinal);

        string[] columns = listView.Descendants(Presentation + "GridViewColumn").Select(column => ResolveStrings(Attr(column, "Header"))).ToArray();
        Assert.Equal(new[] { Strings.ColumnName, Strings.ColumnNamespace, Strings.ColumnFlags, Strings.ColumnSize, Strings.ColumnKind, Strings.ColumnSummary }, columns);
        foreach (XElement cell in listView.Descendants(Presentation + "GridViewColumn").SelectMany(column => column.Descendants(Presentation + "TextBlock")))
        {
            Assert.Equal("Wrap", Attr(cell, "TextWrapping"));
        }

        Assert.DoesNotContain("CollectionViewSource", MainWindowMarkup(), StringComparison.Ordinal);
        Assert.DoesNotContain("GroupStyle", MainWindowMarkup(), StringComparison.Ordinal);
    }

    [Fact]
    public void ToolbarButtonsCarryUniqueAccessKeysBoundToCommands()
    {
        XElement window = LoadMainWindow();
        (string Element, string Label, string Name, string Command)[] expected =
        {
            ("RefreshButton", "RefreshLabel", "RefreshName", "RefreshCommand"),
            ("OpenBaselineButton", "OpenBaselineLabel", "OpenBaselineName", "OpenBaselineCommand"),
            ("ClearFilterButton", "ClearFilterLabel", "ClearFilterName", "ClearFilterCommand"),
            ("CopyValueButton", "CopyValueLabel", "CopyValueName", "CopyValueCommand"),
            ("ExportVisibleListButton", "ExportVisibleListLabel", "ExportVisibleListName", "ExportVisibleListCommand"),
            ("CopyLogButton", "CopyLogLabel", "CopyLogName", "CopyLogCommand"),
            ("MoveUpButton", "MoveUpLabel", "MoveUpName", "Detail.Change.MoveUpCommand"),
            ("MoveDownButton", "MoveDownLabel", "MoveDownName", "Detail.Change.MoveDownCommand"),
            ("ReviewChangeButton", "ReviewChangeLabel", "ReviewChangeName", "Detail.Change.ReviewChangeCommand"),
            ("BootOnceButton", "BootOnceLabel", "BootOnceName", "Detail.Change.BootOnceCommand"),
            ("ClearBootNextButton", "ClearBootNextLabel", "ClearBootNextName", "Detail.Change.ClearBootNextCommand"),
            ("RestorePreviousButton", "RestorePreviousLabel", "RestorePreviousName", "Detail.Change.RestorePreviousCommand"),
            ("ReadDellSettingsButton", "ReadDellSettingsLabel", "ReadDellSettingsName", "ReadDellSettingsCommand"),
        };

        List<char> accessKeys = new();
        foreach ((string element, string label, string name, string command) in expected)
        {
            XElement button = Named(window, "Button", element);
            Assert.Equal("{x:Static res:Strings." + label + "}", Attr(button, "Content"));
            Assert.Equal("{x:Static res:Strings." + name + "}", NameOf(button));
            Assert.Contains(command, Attr(button, "Command"), StringComparison.Ordinal);
            Assert.True(string.IsNullOrWhiteSpace(Attr(button, "Click")), element + " must be bound to a command, not a click handler");

            string labelText = ResolveStrings(Attr(button, "Content"));
            Match key = AccessKey.Match(labelText);
            Assert.True(key.Success, label + " has no access key marker");
            accessKeys.Add(char.ToUpperInvariant(key.Groups[1].Value[0]));
            Assert.Equal(labelText.Replace("_", string.Empty, StringComparison.Ordinal), ResolveStrings(NameOf(button)));

            // Round 2 (finding F135): screen readers announced the bare letter ("R"), which does
            // nothing because the window requires Alt. Both UI Automation shortcut properties say
            // "Alt+<letter>": WPF 8 answers an empty AccessKey once AutomationProperties.AccessKey
            // is set (UIElementAutomationPeer.GetAccessKeyCore), which silences the bare letter,
            // and NVDA speaks the AcceleratorKey as the shortcut.
            string spokenKey = "{x:Static res:Strings." + name[..^"Name".Length] + "AccessKey}";
            Assert.Equal(spokenKey, Attr(button, "AutomationProperties.AccessKey"));
            Assert.Equal(spokenKey, Attr(button, "AutomationProperties.AcceleratorKey"));
            Assert.Equal("Alt+" + char.ToUpperInvariant(key.Groups[1].Value[0]), ResolveStrings(spokenKey));
        }

        // The Change group's six buttons join the toolbar's five and Copy
        // log, each with a letter of its own; the toolbar itself is unchanged. Read Dell settings, Alt+S, in the Dell settings region; still five in the toolbar.
        Assert.Equal(new[] { 'R', 'O', 'C', 'Y', 'X', 'L', 'U', 'D', 'V', 'B', 'M', 'P', 'S' }, accessKeys);
        Assert.Equal(accessKeys.Count, accessKeys.Distinct().Count());
        Assert.Equal(13, window.Descendants(Presentation + "Button").Count());
        Assert.Equal(5, Named(window, "WrapPanel", "ToolbarPanel").Elements(Presentation + "Button").Count());
        foreach (XElement button in window.Descendants(Presentation + "Button"))
        {
            Assert.True(string.IsNullOrWhiteSpace(Attr(button, "IsDefault")), Describe(button) + " is a default button");
        }
    }

    /// <summary>Nothing user-visible is hard-coded in XAML: every label, text and help text is a Strings reference or a binding, and every Strings reference resolves.</summary>
    [Fact]
    public void EveryUserVisibleStringComesFromStringsOrABinding()
    {
        XElement window = LoadMainWindow();
        List<string> literals = new();
        foreach (XElement element in window.DescendantsAndSelf())
        {
            foreach (XAttribute attribute in element.Attributes().Where(attribute => UserVisibleAttributes.Contains(attribute.Name.LocalName, StringComparer.Ordinal)))
            {
                if (!attribute.Value.StartsWith("{", StringComparison.Ordinal) || attribute.Value.StartsWith("{}", StringComparison.Ordinal))
                {
                    literals.Add(Describe(element) + " " + attribute.Name.LocalName + "=\"" + attribute.Value + "\"");
                }
            }
        }

        Assert.True(literals.Count == 0, "Hard-coded user-visible strings:" + Environment.NewLine + string.Join(Environment.NewLine, literals));

        string xaml = MainWindowMarkup();
        MatchCollection references = StringsReference.Matches(xaml);
        Assert.True(references.Count >= 60, "expected the XAML to read its strings from Strings; found " + references.Count + " references");
        foreach (Match reference in references)
        {
            FieldInfo? field = typeof(Strings).GetField(reference.Groups[1].Value, BindingFlags.Public | BindingFlags.Static);
            Assert.True(field is not null && field.IsLiteral && field.FieldType == typeof(string), "Strings." + reference.Groups[1].Value + " is referenced by the XAML but is not a public string constant");
        }
    }

    /// <summary>
    /// Hard rule 4 (no timed behaviour) is guarded over every folder that holds shell logic: the
    /// WPF code-behind and platform classes in src/UefiAccess.App, and the view models and
    /// services in src/UefiAccess.Presentation (where, by the agreed deviation, the commands,
    /// filtering, announcements, focus requests and the asynchronous load path live). Neither
    /// folder may reference the CLI project either. One exception is pinned (Alpha 2 finding A2-7,
    /// the tester's A2-5): the WPF detail scheduler, and no other file, may name a DispatcherTimer, and
    /// it constructs exactly one, the quiet period the detail pane's rebuild waits for
    /// (DetailRefreshTiming.QuietPeriod). Every other timed form stays forbidden everywhere, that
    /// file included, and the XAML check (<see cref="ShellUsesStockControlsWithoutTemplateOverrides"/>) stays.
    /// </summary>
    [Theory]
    [InlineData("src/UefiAccess.App")]
    [InlineData("src/UefiAccess.Presentation")]
    public void NoTimedBehaviourAndNoCliReference(string folder)
    {
        const string QuietPeriodTimerFile = "src/UefiAccess.App/Platform/DispatcherDetailScheduler.cs";
        IReadOnlyList<string> files = RepositoryPaths.Enumerate(folder, "*.cs");
        Assert.True(files.Count >= 4, folder + " has only " + files.Count + " source files");
        string[] timedForms = { "DispatcherTimer", "System.Timers", "System.Threading.Timer", "Task.Delay", "Thread.Sleep", "PeriodicTimer" };
        List<string> timerFiles = new();
        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            foreach (string form in timedForms)
            {
                if (form == "DispatcherTimer" && RepositoryPaths.Relative(file) == QuietPeriodTimerFile)
                {
                    timerFiles.Add(RepositoryPaths.Relative(file));
                    continue;
                }

                Assert.False(text.Contains(form, StringComparison.Ordinal), RepositoryPaths.Relative(file) + " uses " + form);
            }

            Assert.False(text.Contains("UefiAccess.Cli", StringComparison.Ordinal), RepositoryPaths.Relative(file) + " references the CLI project");
        }

        if (folder != "src/UefiAccess.App")
        {
            Assert.Empty(timerFiles);
            return;
        }

        // The exception is that one file, and in it one timer, whose interval is the one quiet period.
        Assert.Equal(new[] { QuietPeriodTimerFile }, timerFiles);
        string scheduler = File.ReadAllText(Path.Combine(RepositoryPaths.Root, QuietPeriodTimerFile));
        Assert.Single(Regex.Matches(scheduler, @"new\s+DispatcherTimer\b", RegexOptions.CultureInvariant));
        Assert.Equal(1, CountOf(scheduler, "new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = DetailRefreshTiming.QuietPeriod };"));
        Assert.Equal(1, CountOf(scheduler, "private readonly DispatcherTimer _quietPeriod;"));
        string[] code = scheduler.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, code.Sum(line => CountOf(line, "DispatcherTimer")));
    }

    [Fact]
    public void CodeBehindHasNoTimersAndComposesTheShell()
    {
        string appDirectory = Path.Combine(RepositoryPaths.Root, "src", "UefiAccess.App");
        Assert.False(File.Exists(Path.Combine(appDirectory, "MainViewModel.cs")), "the M1 MainViewModel must be gone; the shell lives in Presentation");

        string app = File.ReadAllText(Path.Combine(appDirectory, "App.xaml.cs"));
        Assert.Contains("ShellViewModel.CreateWindows(", app, StringComparison.Ordinal);
        Assert.Contains("new WpfAnnouncer(", app, StringComparison.Ordinal);
        Assert.Contains("new WpfFocusSink(", app, StringComparison.Ordinal);
        Assert.Contains("new WpfPlatformInteractions(", app, StringComparison.Ordinal);
        Assert.Contains(".Attach(", app, StringComparison.Ordinal);
        XElement application = XDocument.Load(Path.Combine(appDirectory, "App.xaml")).Root!;
        Assert.Null(application.Attribute("StartupUri"));

        // Hard rule 4 and the documents handed to the tester: the window opens idle with the Ready
        // sentence; nothing loads, announces or moves focus until Refresh or Open baseline folder.
        // Since M4 the Dell settings are read only by Read Dell settings (Alt+S): neither the
        // application nor the window starts a read; the button's command is the shell's.
        Assert.DoesNotContain(".RefreshAsync(", app, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenBaselineAsync(", app, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadDellSettingsAsync", app, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadDellSettingsCommand", app, StringComparison.Ordinal);
        Assert.DoesNotContain("Loaded +=", app, StringComparison.Ordinal);
        Assert.DoesNotContain("Announce(Strings.IdleStatus)", app, StringComparison.Ordinal);

        // The Ready sentence is announced once, from the window's Loaded handler, when the Status
        // box has an automation peer to raise LiveRegionChanged on; not from the shell's
        // constructor (no peer yet) and not from the application (before the window is shown).
        Assert.DoesNotContain("AnnounceReady(", app, StringComparison.Ordinal);

        // The "starting" line is logged before the shell and its log panel exist; the panel sink
        // holds such lines back and writes them into the panel when it is attached.
        Assert.Contains("UefiAccess.App {Version} starting", app, StringComparison.Ordinal);
        Assert.True(app.IndexOf("starting", StringComparison.Ordinal) < app.IndexOf("_panelSink.Attach(Shell.Log)", StringComparison.Ordinal));

        // Access keys need Alt (a bare letter on a group header or in a read-only box must not run a button).
        Assert.Contains("CoreCompatibilityPreferences.IsAltKeyRequiredInAccessKeyDefaultScope = true", app, StringComparison.Ordinal);

        // An unhandled exception is announced as an unexpected error, never as a failed load, and
        // one before the window is on screen shuts the elevated process down.
        Assert.Contains("Strings.UnexpectedErrorFormat", app, StringComparison.Ordinal);
        Assert.DoesNotContain("Strings.FailedFormat", app, StringComparison.Ordinal);
        Assert.Contains("Shutdown(1)", app, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true", app, StringComparison.Ordinal);

        string window = File.ReadAllText(Path.Combine(appDirectory, "MainWindow.xaml.cs"));
        Assert.DoesNotContain("ReadDellSettingsAsync", window, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadDellSettingsCommand", window, StringComparison.Ordinal);
        string onLoaded = window[window.IndexOf("void OnLoaded(", StringComparison.Ordinal)..];
        onLoaded = onLoaded[..onLoaded.IndexOf("\n    }", StringComparison.Ordinal)];
        Assert.Contains("_shell?.AnnounceReady()", onLoaded, StringComparison.Ordinal);
        Assert.Contains("FocusToolbar()", onLoaded, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(window, "AnnounceReady("));
        Assert.Equal("OnLoaded", Attr(LoadMainWindow(), "Loaded"));

        // A rebuild of the groups (Clear filter, Group by, Sort by, a load) replaces the containers
        // under the focus; the window notices the Reset, remembers where focus was and puts it
        // back once the new containers are rendered (one deferred dispatcher call, no timer).
        Assert.Contains("Groups.CollectionChanged += OnGroupsCollectionChanged", window, StringComparison.Ordinal);
        Assert.Contains("Groups.CollectionChanged -= OnGroupsCollectionChanged", window, StringComparison.Ordinal);
        Assert.Contains("void OnGroupsCollectionChanged(", window, StringComparison.Ordinal);
        Assert.Contains("NotifyCollectionChangedAction.Reset", window, StringComparison.Ordinal);
        Assert.Contains("GroupsControl.IsKeyboardFocusWithin", window, StringComparison.Ordinal);
        Assert.Contains("void RefocusListAfterRebuild(", window, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(RefocusListAfterRebuild))", window, StringComparison.Ordinal);
        Assert.Contains("MoveFocusTo(FocusTarget.ListSelectedRow)", window, StringComparison.Ordinal);

        Assert.Contains("Key.F6", window, StringComparison.Ordinal);
        Assert.Contains("RegionOrder.Next(", window, StringComparison.Ordinal);
        Assert.Contains("RegionOrder.Previous(", window, StringComparison.Ordinal);
        Assert.Contains("OnRegionArrived(", window, StringComparison.Ordinal);
        Assert.Contains("OnFilterApplied()", window, StringComparison.Ordinal);
        Assert.Contains("Key.Escape", window, StringComparison.Ordinal);
        Assert.Contains("Key.Enter", window, StringComparison.Ordinal);
        Assert.Contains("Key.Space", window, StringComparison.Ordinal);
        Assert.Contains("ModifierKeys.Control", window, StringComparison.Ordinal);
        Assert.Contains("Keyboard.Focus(", window, StringComparison.Ordinal);
        Assert.Contains("ItemContainerGenerator", window, StringComparison.Ordinal);
        Assert.Contains("CopyValueText", window, StringComparison.Ordinal);

        // Ctrl+C goes through the shell so a failed copy is announced; the window touches no clipboard itself.
        Assert.Contains(".CopyKey(", window, StringComparison.Ordinal);
        Assert.DoesNotContain("SetClipboardText(", window, StringComparison.Ordinal);
        Assert.DoesNotContain("Clipboard.", window, StringComparison.Ordinal);

        // Home and End span the groups; Ctrl+Left and Ctrl+Right scroll the columns; a splitter
        // counts as sitting between the regions it separates for F6; a load button disabled under
        // focus hands focus to the Status box instead of leaving it to WPF.
        Assert.Contains("Key.Home", window, StringComparison.Ordinal);
        Assert.Contains("Key.End", window, StringComparison.Ordinal);
        Assert.Contains("PageLeft()", window, StringComparison.Ordinal);
        Assert.Contains("PageRight()", window, StringComparison.Ordinal);
        Assert.Contains("SplitterRegions(", window, StringComparison.Ordinal);
        Assert.Contains("(Region.List, Region.Detail)", window, StringComparison.Ordinal);
        Assert.Contains("(Region.Detail, Region.Log)", window, StringComparison.Ordinal);
        Assert.Contains("IsEnabledChanged +=", window, StringComparison.Ordinal);
        Assert.Contains("IsKeyboardFocused: true", window, StringComparison.Ordinal);

        string focusSink = File.ReadAllText(Path.Combine(appDirectory, "Platform", "WpfFocusSink.cs"));
        Assert.Contains("DispatcherPriority.Loaded", focusSink, StringComparison.Ordinal);
        Assert.Contains("BeginInvoke(", focusSink, StringComparison.Ordinal);
        Assert.Contains("MoveFocusTo(", focusSink, StringComparison.Ordinal);

        string announcer = File.ReadAllText(Path.Combine(appDirectory, "Platform", "WpfAnnouncer.cs"));
        Assert.Contains("UIElementAutomationPeer.FromElement(", announcer, StringComparison.Ordinal);
        Assert.Contains("CreatePeerForElement(", announcer, StringComparison.Ordinal);

        // A2-2: the event goes to UI Automation directly. AutomationPeer.RaiseAutomationEvent (and
        // the ListenerExists check) drop it unless WPF saw a client subscribe, and NVDA 2026.2
        // subscribes in a way WPF does not see: none of the Status changes made while focus was
        // elsewhere was spoken. The focused Status box is read through its name change, so the
        // event is not raised when its text changes (it would be read twice); the same text again
        // changes no name, so it is.
        Assert.Contains("AutomationInteropProvider.RaiseAutomationEvent(", announcer, StringComparison.Ordinal);
        Assert.Contains("AutomationElementIdentifiers.LiveRegionChangedEvent,", announcer, StringComparison.Ordinal);
        Assert.Contains("ProviderFromPeer(peer)", announcer, StringComparison.Ordinal);
        Assert.Contains("(_liveRegion.IsKeyboardFocused && textChanged)", announcer, StringComparison.Ordinal);
        Assert.Contains("textChanged = !string.Equals(_liveRegion.Text, before, StringComparison.Ordinal);", announcer, StringComparison.Ordinal);
        Assert.Contains("AutomationInteropProvider.ClientsAreListening", announcer, StringComparison.Ordinal);
        Assert.DoesNotContain("ListenerExists(", announcer, StringComparison.Ordinal);
        Assert.DoesNotContain("peer?.RaiseAutomationEvent(", announcer, StringComparison.Ordinal);
        Assert.DoesNotContain("peer.RaiseAutomationEvent(", announcer, StringComparison.Ordinal);

        string sink = File.ReadAllText(Path.Combine(appDirectory, "Platform", "LogPanelSink.cs"));
        Assert.Contains("ILogEventSink", sink, StringComparison.Ordinal);
        Assert.Contains("LogEventLevel.Information", sink, StringComparison.Ordinal);
        Assert.Contains("BeginInvoke(", sink, StringComparison.Ordinal);
        Assert.Contains("_panelSink.Attach(Shell.Log)", app, StringComparison.Ordinal);
        Assert.Contains("Queue<LogLineViewModel> _pending", sink, StringComparison.Ordinal);
        Assert.Contains("_pending.Enqueue(line)", sink, StringComparison.Ordinal);
        string attach = sink[sink.IndexOf("public void Attach(", StringComparison.Ordinal)..];
        attach = attach[..attach.IndexOf("\n    }", StringComparison.Ordinal)];
        Assert.Contains("_pending.ToArray()", attach, StringComparison.Ordinal);
        Assert.Contains("Deliver(target, line)", attach, StringComparison.Ordinal);

        string platform = File.ReadAllText(Path.Combine(appDirectory, "Platform", "WpfPlatformInteractions.cs"));
        Assert.Contains("OpenFolderDialog", platform, StringComparison.Ordinal);
        Assert.Contains("SaveFileDialog", platform, StringComparison.Ordinal);

        // A2-1: the clipboard text is placed without the flush (copy: false). Clipboard.SetText
        // and SetDataObject(..., copy: true) flush while the UI thread sleeps, which fails while a
        // clipboard listener waits for this thread, and the shell said "Copy failed" after a copy
        // that worked (WpfPlatformInteractions.SetClipboardText remarks).
        Assert.Contains("Clipboard.SetDataObject(text ?? string.Empty, copy: false);", platform, StringComparison.Ordinal);

        // The last copy is flushed once, at exit, so it outlives the application; before the
        // logger is disposed, so a failed flush is still logged.
        Assert.Contains("Clipboard.Flush();", platform, StringComparison.Ordinal);
        string onExit = app[app.IndexOf("protected override void OnExit(", StringComparison.Ordinal)..];
        onExit = onExit[..onExit.IndexOf("\n    }", StringComparison.Ordinal)];
        int flush = onExit.IndexOf("_platform?.FlushClipboard();", StringComparison.Ordinal);
        Assert.True(flush >= 0, "App.OnExit does not flush the clipboard");
        Assert.True(flush < onExit.IndexOf("(Logger as IDisposable)?.Dispose();", StringComparison.Ordinal), "App.OnExit flushes the clipboard after disposing the logger");
        Assert.Contains("_platform = platform;", app, StringComparison.Ordinal);
        foreach (string file in RepositoryPaths.Enumerate("src/UefiAccess.App", "*.cs"))
        {
            string source = File.ReadAllText(file);
            Assert.False(source.Contains("Clipboard.SetText(", StringComparison.Ordinal), RepositoryPaths.Relative(file) + " calls Clipboard.SetText, which flushes");
            Assert.False(source.Contains("copy: true", StringComparison.Ordinal), RepositoryPaths.Relative(file) + " flushes the clipboard (copy: true)");
            Assert.Equal(file.EndsWith("WpfPlatformInteractions.cs", StringComparison.Ordinal) ? 1 : 0, Regex.Matches(source, @"Clipboard\.SetDataObject\(").Count);
        }
    }

    /// <summary>
    /// Alpha 1 round 1, findings A1-1, A1-3 and A1-4: NVDA read a group header three times (the
    /// generated container as a data item, then the Expander, then the per-group ListView) and
    /// every section title and field label twice (the container, then the control). WPF's own
    /// ItemsControl peer creates that data item per item and names it from the container, or from
    /// the item's ToString() when the container is unnamed; a container without a peer is given a
    /// default one, so nothing in XAML can remove the element. The application opts out of that
    /// peer with the documented runtime switch, declared for UefiAccess.App only in
    /// Directory.Build.props (the file this repository keeps every switch in), and no container
    /// is named any more: the three plain ItemsControls carry no ItemContainerStyle, so the
    /// controls inside their templates are the only elements a screen reader meets.
    /// </summary>
    [Fact]
    public void GeneratedItemContainersAreNotAutomationElements()
    {
        XElement window = LoadMainWindow();
        List<XElement> itemsControls = window.Descendants(Presentation + "ItemsControl").ToList();
        Assert.Equal(3, itemsControls.Count);
        Assert.Contains(itemsControls, element => Attr(element, Xaml + "Name") == "GroupsControl");
        Assert.Contains(itemsControls, element => Attr(element, Xaml + "Name") == "SectionsControl");
        Assert.Contains(itemsControls, element => Attr(element, "ItemsSource").Contains("Fields", StringComparison.Ordinal));
        foreach (XElement itemsControl in itemsControls)
        {
            Assert.Null(itemsControl.Element(Presentation + "ItemsControl.ItemContainerStyle"));
            Assert.Equal(string.Empty, Attr(itemsControl, "ItemContainerStyle"));
        }

        // No style names a generated container; the only item container styles are the ones on
        // the controls whose items are real rows (the per-group ListView and the log ListBox).
        Assert.DoesNotContain(window.Descendants(Presentation + "Style"), style => Attr(style, "TargetType") == "ContentPresenter");
        Assert.DoesNotContain("ContentPresenter", MainWindowMarkup(), StringComparison.Ordinal);
        foreach (XElement containerStyle in window.Descendants().Where(element => element.Name.LocalName.EndsWith(".ItemContainerStyle", StringComparison.Ordinal)))
        {
            Assert.Contains(containerStyle.Name.LocalName, new[] { "ListView.ItemContainerStyle", "ListBox.ItemContainerStyle" });
        }

        // The runtime switch, for the App project only, under its exact documented name, in the
        // props file (implementers do not edit project files), and explained in the markup.
        XDocument props = XDocument.Load(Path.Combine(RepositoryPaths.Root, "Directory.Build.props"));
        XElement option = Assert.Single(props.Descendants("RuntimeHostConfigurationOption"));
        Assert.Equal(ItemsControlSwitch, (string?)option.Attribute("Include"));
        Assert.Equal("true", (string?)option.Attribute("Value"));
        Assert.Contains("'UefiAccess.App'", (string?)option.Parent!.Attribute("Condition"), StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeHostConfigurationOption", File.ReadAllText(Path.Combine(RepositoryPaths.Root, "src", "UefiAccess.App", "UefiAccess.App.csproj")), StringComparison.Ordinal);
        Assert.Contains(ItemsControlSwitch, File.ReadAllText(MainWindowPath()), StringComparison.Ordinal);

        // The controls inside the templates keep their own names: what is spoken is spoken once.
        XElement expander = Assert.Single(window.Descendants(Presentation + "Expander"));
        Assert.Equal("{Binding AccessibleName, Mode=OneWay}", NameOf(expander));
        XElement sections = Named(window, "ItemsControl", "SectionsControl");
        XElement section = Assert.Single(sections.Descendants(Presentation + "GroupBox"));
        Assert.Equal("{Binding Title, Mode=OneWay}", NameOf(section));
        XElement field = Assert.Single(sections.Descendants(Presentation + "TextBox"));
        Assert.Equal("{Binding AccessibleName, Mode=OneWay}", NameOf(field));
    }

    /// <summary>
    /// Finding A1-2: the list help was heard twice on the way into the list, on the List region
    /// and again on the per-group ListView (the scroll viewer between them carried it too, unheard
    /// because it is not focusable). It is spoken once, on arrival in the List region, and sits
    /// nowhere else in the markup.
    /// </summary>
    [Fact]
    public void VariableListHelpIsSpokenOnceOnArrivalInTheListRegion()
    {
        XElement window = LoadMainWindow();
        Assert.Equal(1, CountOf(MainWindowMarkup(), "res:Strings.VariableListHelp"));
        Assert.Equal("{x:Static res:Strings.VariableListHelp}", Attr(Named(window, "GroupBox", "ListRegion"), "AutomationProperties.HelpText"));
        Assert.Equal(string.Empty, Attr(Named(window, "ScrollViewer", "ListScroller"), "AutomationProperties.HelpText"));

        XElement listView = Assert.Single(Named(window, "ItemsControl", "GroupsControl").Descendants(Presentation + "ListView"));
        Assert.Equal(string.Empty, Attr(listView, "AutomationProperties.HelpText"));
        Assert.Null(listView.Element(Presentation + "AutomationProperties.HelpText"));
        Assert.True(IsGroupRowsGrid(listView));

        // The group header keeps its own short help; nothing else in the list carries any.
        XElement expander = Assert.Single(window.Descendants(Presentation + "Expander"));
        Assert.Equal("{x:Static res:Strings.GroupHeaderHelp}", Attr(expander, "AutomationProperties.HelpText"));
        Assert.Equal(1, CountOf(MainWindowMarkup(), "res:Strings.GroupHeaderHelp"));
    }

    /// <summary>
    /// Finding A1-5: a keyboard resize of a splitter gave no accessible feedback. Both splitters
    /// are AnnouncingGridSplitter (src/UefiAccess.App/Platform): the stock GridSplitter with a peer
    /// derived from the stock GridSplitterAutomationPeer. Each arrow press moves the splitter one
    /// whole percent of the space the two panes share (SplitterPosition, unit-tested in
    /// SplitterPositionTests), and the limits are kept rather than widened. Alpha 2 finding A2-8
    /// (the tester's A2-6: "Raw values as the size or position changes") and the developer's A2-5
    /// (Narrator silent after a press): the peer no longer exposes a RangeValue, whose bare number
    /// NVDA 2026.2 spoke as "63", but the Value pattern, read-only (SetValue throws
    /// InvalidOperationException), whose value is a sentence naming both panes
    /// (SplitterPosition.ValueText); the splitter raises no property change of that value (NVDA
    /// would speak it once from the event and once from Status) and instead raises its Moved event
    /// after every arrow press along its axis (at a limit too) and once at the end of a drag that
    /// changed the share; the window hands that to ShellViewModel.OnSplitterMoved, which says the
    /// sentence through Status. Names, tab indices and focusability are unchanged, each splitter
    /// names its sentence through Kind, the help texts say the share is announced as a percentage,
    /// the implicit style targets the subclass (an implicit style applies to its exact type only),
    /// the Transform pattern is the stock peer's, and this is the one custom peer in the
    /// application: a control subclass, not a template override. These are checks of the markup
    /// and the source text: the test project never loads the WPF App, so no test runs the
    /// splitter. One Moved event per press, the limit found after a real layout, and a UI
    /// Automation client that finds the Value pattern and no RangeValue are checked only by
    /// hearing the splitters (the A2-8 re-test); the policy
    /// (SplitterPositionTests) and the shell's Status sentence (ShellSplitterAnnouncementTests)
    /// are run.
    /// </summary>
    [Fact]
    public void SplittersSayTheirPositionAsAReadOnlyValueSentence()
    {
        XElement window = LoadMainWindow();
        Assert.Empty(window.Descendants(Presentation + "GridSplitter"));
        List<XElement> splitters = window.Descendants(Platform + "AnnouncingGridSplitter").ToList();
        Assert.Equal(new[] { "ListDetailSplitter", "LogSplitter" }, splitters.Select(element => Attr(element, Xaml + "Name")).ToArray());

        XElement listDetail = splitters[0];
        Assert.Equal("Columns", Attr(listDetail, "ResizeDirection"));
        Assert.Equal("PreviousAndNext", Attr(listDetail, "ResizeBehavior"));
        Assert.Equal("{x:Static res:Strings.ListDetailSplitterName}", NameOf(listDetail));
        Assert.Equal("{x:Static res:Strings.ListDetailSplitterHelp}", Attr(listDetail, "AutomationProperties.HelpText"));
        Assert.Equal(10, TabIndexOf(listDetail));
        Assert.Equal(string.Empty, Attr(listDetail, "TracksNextDefinition"));
        Assert.Equal(nameof(SplitterKind.ListAndDetails), Attr(listDetail, "Kind"));

        XElement log = splitters[1];
        Assert.Equal("Rows", Attr(log, "ResizeDirection"));
        Assert.Equal("PreviousAndNext", Attr(log, "ResizeBehavior"));
        Assert.Equal("{x:Static res:Strings.LogSplitterName}", NameOf(log));
        Assert.Equal("{x:Static res:Strings.LogSplitterHelp}", Attr(log, "AutomationProperties.HelpText"));
        Assert.Equal(21, TabIndexOf(log));
        Assert.Equal("True", Attr(log, "TracksNextDefinition"));
        Assert.Equal(nameof(SplitterKind.LogPanel), Attr(log, "Kind"));

        XElement style = Assert.Single(window.Descendants(Presentation + "Style"), candidate => Attr(candidate, "TargetType").Contains("GridSplitter", StringComparison.Ordinal));
        Assert.Equal("platform:AnnouncingGridSplitter", Attr(style, "TargetType"));
        Assert.Equal("True", SetterValue(style, "Focusable"));
        Assert.Equal("False", SetterValue(style, "ShowsPreview"));

        Assert.Equal("Drag, or focus and press Left or Right, to change the width of the list and the detail pane. The list's share of the width is announced as a percentage.", Strings.ListDetailSplitterHelp);
        Assert.Equal("Drag, or focus and press Up or Down, to change the height of the log panel. The log panel's share of the height is announced as a percentage.", Strings.LogSplitterHelp);

        // The splitter: the stock keyboard move one percent per press, a report after every press
        // along the axis (at a limit too) and after a drag that changed the share, and no
        // automation event of its own.
        string platformDirectory = Path.Combine(RepositoryPaths.Root, "src", "UefiAccess.App", "Platform");
        string splitter = File.ReadAllText(Path.Combine(platformDirectory, "AnnouncingGridSplitter.cs"));
        Assert.Contains("public sealed class AnnouncingGridSplitter : GridSplitter", splitter, StringComparison.Ordinal);
        Assert.Contains("protected override AutomationPeer OnCreateAutomationPeer()", splitter, StringComparison.Ordinal);
        Assert.Contains("new AnnouncingGridSplitterAutomationPeer(this)", splitter, StringComparison.Ordinal);
        Assert.Contains("protected override void OnKeyDown(KeyEventArgs e)", splitter, StringComparison.Ordinal);
        Assert.Contains("base.OnKeyDown(e)", splitter, StringComparison.Ordinal);
        Assert.Contains("DragCompleted +=", splitter, StringComparison.Ordinal);
        Assert.Contains("mover.Move(", splitter, StringComparison.Ordinal);
        Assert.Contains("public bool TracksNextDefinition { get; set; }", splitter, StringComparison.Ordinal);
        Assert.Contains("public SplitterKind Kind { get; set; }", splitter, StringComparison.Ordinal);
        Assert.Contains("public event EventHandler<SplitterMovedEventArgs>? Moved;", splitter, StringComparison.Ordinal);
        Assert.Contains("SplitterPosition.Percent(tracked.Actual, total)", splitter, StringComparison.Ordinal);
        Assert.Contains("internal string? ValueText => Range is SplitterRange range ? SplitterPosition.ValueText(Kind, range.Share) : null;", splitter, StringComparison.Ordinal);
        string keys = Method(splitter, "protected override void OnKeyDown(");
        Assert.Contains("SplitterPosition.Step(", keys, StringComparison.Ordinal);
        Assert.Contains("StepDirection(e.Key, range)", keys, StringComparison.Ordinal);
        Assert.Contains("SplitterPosition.Direction(pressed, range.Direction == GridResizeDirection.Columns, range.TracksFirstDefinition, FlowDirection == FlowDirection.RightToLeft)", Method(splitter, "private int StepDirection("), StringComparison.Ordinal);
        Assert.Contains("!IsDragging", keys, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true;", keys, StringComparison.Ordinal);
        Assert.Contains("ReportPress(range.Share);", keys, StringComparison.Ordinal);
        Assert.True(keys.IndexOf("SplitterPosition.Step(", StringComparison.Ordinal) < keys.IndexOf("ReportPress(range.Share);", StringComparison.Ordinal), "a press is reported after the step, whether or not it moved the splitter");
        Assert.DoesNotContain("KeyboardIncrement", keys, StringComparison.Ordinal);
        string press = Method(splitter, "private void ReportPress(");
        Assert.True(press.IndexOf("UpdateLayout();", StringComparison.Ordinal) >= 0 && press.IndexOf("UpdateLayout();", StringComparison.Ordinal) < press.IndexOf("Moved?.Invoke(", StringComparison.Ordinal), "the share is read after the layout has run");
        Assert.Contains("Moved?.Invoke(this, new SplitterMovedEventArgs(Kind, range.Share, atLimit: range.Share == before));", press, StringComparison.Ordinal);
        string drag = Method(splitter, "private void OnDragCompletedReport(");
        Assert.Contains("UpdateLayout();", drag, StringComparison.Ordinal);
        Assert.Contains("if (Range is SplitterRange range && range.Share != start)", drag, StringComparison.Ordinal);
        Assert.Contains("Moved?.Invoke(this, new SplitterMovedEventArgs(Kind, range.Share, atLimit: false));", drag, StringComparison.Ordinal);
        Assert.Equal(2, CountOf(splitter, "Moved?.Invoke("));

        // No automation event or property change is raised by the splitter or its peer, and no
        // range value is left anywhere in the application.
        string peer = File.ReadAllText(Path.Combine(platformDirectory, "AnnouncingGridSplitterAutomationPeer.cs"));
        foreach ((string file, string source) in new[] { ("AnnouncingGridSplitter.cs", splitter), ("AnnouncingGridSplitterAutomationPeer.cs", peer) })
        {
            foreach (string forbidden in new[] { "RaisePropertyChangedEvent", "RaiseAutomationEvent", "RaiseNotificationEvent", "ValuePatternIdentifiers", "IRangeValueProvider", "PatternInterface.RangeValue" })
            {
                Assert.False(source.Contains(forbidden, StringComparison.Ordinal), file + " contains " + forbidden);
            }
        }

        foreach (string file in RepositoryPaths.Enumerate("src/UefiAccess.App", "*.cs"))
        {
            string source = File.ReadAllText(file);
            foreach (string rangeValue in new[] { "IRangeValueProvider", "RangeValuePatternIdentifiers", "PatternInterface.RangeValue" })
            {
                Assert.False(source.Contains(rangeValue, StringComparison.Ordinal), RepositoryPaths.Relative(file) + " uses " + rangeValue);
            }
        }

        Assert.DoesNotContain("RangeValue", MainWindowMarkup(), StringComparison.Ordinal);

        Assert.DoesNotContain("internal void MoveTo(", splitter, StringComparison.Ordinal);
        Assert.DoesNotContain("Math.Max(maximum, value)", splitter, StringComparison.Ordinal);
        Assert.DoesNotContain("Math.Min(minimum, value)", splitter, StringComparison.Ordinal);
        Assert.DoesNotContain("ControlTemplate", splitter, StringComparison.Ordinal);
        Assert.DoesNotContain("OnApplyTemplate", splitter, StringComparison.Ordinal);
        Assert.DoesNotContain("TemplateProperty", splitter, StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultStyleKey", splitter, StringComparison.Ordinal);

        // The peer: the stock GridSplitter peer (Thumb, Transform) plus the Value pattern, read-only.
        Assert.Contains("public sealed class AnnouncingGridSplitterAutomationPeer : GridSplitterAutomationPeer, IValueProvider", peer, StringComparison.Ordinal);
        string pattern = Method(peer, "public override object? GetPattern(");
        Assert.Contains("if (patternInterface == PatternInterface.Value)", pattern, StringComparison.Ordinal);
        Assert.Contains("return Splitter.ValueText is null ? null : this;", pattern, StringComparison.Ordinal);
        Assert.Contains("return base.GetPattern(patternInterface);", pattern, StringComparison.Ordinal);
        Assert.Contains("string IValueProvider.Value => Splitter.ValueText ?? string.Empty;", peer, StringComparison.Ordinal);
        Assert.Contains("bool IValueProvider.IsReadOnly => true;", peer, StringComparison.Ordinal);
        Assert.Contains("throw new InvalidOperationException();", Method(peer, "void IValueProvider.SetValue(string value)"), StringComparison.Ordinal);
        Assert.DoesNotContain("Splitter.Move", peer, StringComparison.Ordinal);

        // The window hands every report to the shell, which says it through Status (ShellSplitterAnnouncementTests).
        string code = global::UefiAccess.Core.Tests.MainWindowMarkup.Source("MainWindow.xaml.cs");
        Assert.Contains("ListDetailSplitter.Moved += OnSplitterMoved;", code, StringComparison.Ordinal);
        Assert.Contains("LogSplitter.Moved += OnSplitterMoved;", code, StringComparison.Ordinal);
        Assert.Contains("_shell?.OnSplitterMoved(e.Kind, e.Share, e.AtLimit);", Method(code, "private void OnSplitterMoved("), StringComparison.Ordinal);
        Assert.Equal(2, CountOf(code, "+= OnSplitterMoved;"));

        // The one custom peer: no other file of the App project declares an automation peer that a
        // control uses. The exception is WpfAnnouncer's private ProviderLookup (A2-2), a peer class
        // only so that it may call AutomationPeer.ProviderFromPeer; it is never attached to a
        // control, and the Status box keeps the stock TextBox peer.
        foreach (string file in RepositoryPaths.Enumerate("src/UefiAccess.App", "*.cs"))
        {
            string source = File.ReadAllText(file);
            MatchCollection peers = Regex.Matches(source, @"class\s+\w+\s*:\s*\w*AutomationPeer\b", RegexOptions.CultureInvariant);
            if (file.EndsWith("AnnouncingGridSplitterAutomationPeer.cs", StringComparison.Ordinal))
            {
                continue;
            }

            if (file.EndsWith("WpfAnnouncer.cs", StringComparison.Ordinal))
            {
                Assert.Equal("class ProviderLookup : TextBoxAutomationPeer", Assert.Single(peers).Value);
                Assert.Contains("private sealed class ProviderLookup : TextBoxAutomationPeer", source, StringComparison.Ordinal);
                Assert.DoesNotContain("OnCreateAutomationPeer", source, StringComparison.Ordinal);
                continue;
            }

            Assert.True(peers.Count == 0, RepositoryPaths.Relative(file) + " declares an automation peer");
        }
    }

    /// <summary>
    /// Finding A1-7: at 200 percent text size the Copy log button's descenders were clipped. The
    /// button sits in the resizable log row, whose fixed minimum height (72) held the group header
    /// and the button at 100 percent only, so a row shrunk with the splitter cut the button off at
    /// the bottom; the toolbar buttons sit in an Auto row and were fine. No button carries a height
    /// of its own (every one sizes to its text through the same style), and the log row's minimum
    /// height is bound to the Toolbar region's actual height: the same group header chrome and a
    /// button of the same style in an Auto row, so at any text size the row can hold the header,
    /// the padding and the button. That equality holds because the vertical space around the Copy
    /// log button is the space around a toolbar button. The Status box has no fixed height either:
    /// its MinHeight and MaxHeight bound a box that scrolls, they clip nothing.
    /// </summary>
    [Fact]
    public void CopyLogButtonSizesToItsTextAtAnyTextSize()
    {
        XElement window = LoadMainWindow();
        XElement buttonStyle = window.Descendants(Presentation + "Style").Single(style => Attr(style, Xaml + "Key") == "ToolbarButtonStyle");
        foreach (string property in new[] { "Height", "MinHeight", "MaxHeight" })
        {
            Assert.Equal(string.Empty, SetterValue(buttonStyle, property));
            foreach (XElement button in window.Descendants(Presentation + "Button"))
            {
                Assert.True(string.IsNullOrEmpty(Attr(button, property)), Describe(button) + " has a fixed " + property);
                Assert.Equal("{StaticResource ToolbarButtonStyle}", Attr(button, "Style"));
            }
        }

        XElement toolbar = Named(window, "GroupBox", "ToolbarRegion");
        XElement log = Named(window, "GroupBox", "LogRegion");
        XElement copyLog = Named(window, "Button", "CopyLogButton");
        Assert.Same(log, copyLog.Ancestors(Presentation + "GroupBox").First());

        // The regions above Status share one grid (round 2: the Status region has a row of its own
        // below it, WpfShellLayoutTests); the log row is a star row sharing the height with the
        // list/detail row, so the stock splitter keeps both minimums. Since M4 the grid has a sixth
        // row, the Dell settings region's Auto row.
        XElement panes = Named(window, "Grid", "PanesGrid");
        List<XElement> rows = panes.Element(Presentation + "Grid.RowDefinitions")!.Elements(Presentation + "RowDefinition").ToList();
        Assert.Equal(6, rows.Count);
        Assert.Same(panes, toolbar.Parent);
        Assert.Same(panes, log.Parent);
        XElement toolbarRow = rows[int.Parse(Attr(toolbar, "Grid.Row"), CultureInfo.InvariantCulture)];
        XElement logRow = rows[int.Parse(Attr(log, "Grid.Row"), CultureInfo.InvariantCulture)];
        Assert.Equal("Auto", Attr(toolbarRow, "Height"));
        Assert.EndsWith("*", Attr(logRow, "Height"), StringComparison.Ordinal);
        Assert.Equal(string.Empty, Attr(logRow, "MaxHeight"));
        string minHeight = Attr(logRow, "MinHeight");
        Assert.Contains("ElementName=ToolbarRegion", minHeight, StringComparison.Ordinal);
        Assert.Contains("ActualHeight", minHeight, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"^\d", RegexOptions.CultureInvariant), minHeight);

        // Same chrome above and below a button in both regions: the toolbar's height is exactly what the log region needs for its header and the button.
        (double top, double bottom) toolbarPadding = VerticalThickness(Attr(toolbar, "Padding"));
        (double top, double bottom) toolbarButtonMargin = VerticalThickness(SetterValue(buttonStyle, "Margin"));
        (double top, double bottom) logPadding = VerticalThickness(Attr(log, "Padding"));
        (double top, double bottom) copyLogMargin = VerticalThickness(Attr(copyLog, "Margin"));
        double aroundToolbarButton = toolbarPadding.top + toolbarPadding.bottom + toolbarButtonMargin.top + toolbarButtonMargin.bottom;
        double aroundCopyLog = logPadding.top + logPadding.bottom + copyLogMargin.top + copyLogMargin.bottom;
        Assert.Equal(aroundToolbarButton, aroundCopyLog);
        Assert.Equal("Top", Attr(copyLog, "VerticalAlignment"));
        foreach (XElement region in new[] { toolbar, log })
        {
            Assert.Equal(string.Empty, Attr(region, "Height"));
            Assert.Equal(string.Empty, Attr(region, "MinHeight"));
            Assert.Equal(string.Empty, Attr(region, "MaxHeight"));
        }

        XElement status = Named(window, "TextBox", "StatusBox");
        Assert.Equal(string.Empty, Attr(status, "Height"));
        Assert.Equal("Auto", Attr(status, "VerticalScrollBarVisibility"));
        List<XElement> outerRows = Named(window, "Grid", "LayoutRoot").Element(Presentation + "Grid.RowDefinitions")!.Elements(Presentation + "RowDefinition").ToList();
        Assert.Equal("Auto", Attr(outerRows[int.Parse(Attr(Named(window, "GroupBox", "StatusRegion"), "Grid.Row"), CultureInfo.InvariantCulture)], "Height"));
    }

    /// <summary>
    /// Finding A1-6: under fast arrow-key repeat NVDA sometimes dropped the row's name, because
    /// every SelectionChanged rebuilt the whole detail pane synchronously on the UI thread while
    /// the reader was still fetching the name. The selection is still set at once (the row is
    /// selected, Ctrl+C and Copy value see it); the pane's rebuild goes through the shell's
    /// IDetailScheduler, and the application's scheduler is a thin adapter over the
    /// CoalescingDetailScheduler policy (unit-tested in CoalescingDetailSchedulerTests): the window
    /// holds it from a key or mouse button going down in the variable list until it comes up
    /// anywhere in the window (or the window is deactivated). Alpha 2 finding A2-7 (the tester's A2-5):
    /// the release no longer posts straight to the dispatcher's first idle gap, which separate quick
    /// presses left after every key; it posts through QuietPeriodPoster (unit-tested in
    /// QuietPeriodPosterTests), which starts the scheduler's one timer with the interval
    /// DetailRefreshTiming.QuietPeriod, or starts it again from zero, and when that passes with no
    /// hold in between, stops it in its tick and posts one Dispatcher.BeginInvoke at Background
    /// priority, below input, which runs the newest refresh; the hold withdraws by stopping the
    /// timer and aborting that operation. An Alt key anywhere in the window runs the pending
    /// refresh too, so every access key, the Change group's among them, acts on the selected row.
    /// Every entry of
    /// keyboard focus into the pane runs the pending refresh first: the deliberate paths through
    /// FocusFirstDetailField, and every other path (Tab, a click, a screen reader's SetFocus)
    /// through PreviewGotKeyboardFocus on the Detail region, which delegates a focus change headed
    /// for a field of the pane as it was before the refresh (a field the refresh removed, or, the
    /// pane being updated in place since round 4, one it kept for the new row) to the first field
    /// instead of letting WPF drop it or land on it. The
    /// fields keep the stock StackPanel: a VirtualizingStackPanel virtualises only inside its own
    /// ItemsControl's scroll viewer, which a plain ItemsControl has not, and would hide unrealised
    /// fields from Tab, from FocusFirstDetailField and from a screen reader; so the pane is made
    /// cheaper by refreshing it less often, not by virtualising.
    /// </summary>
    [Fact]
    public void DetailPaneRefreshIsHeldWhileAKeyIsDownAndRunsBeforeFocusEntersThePane()
    {
        string appDirectory = Path.Combine(RepositoryPaths.Root, "src", "UefiAccess.App");

        // The application composes the dispatcher scheduler into the shell and hands it to the window.
        string app = File.ReadAllText(Path.Combine(appDirectory, "App.xaml.cs"));
        Assert.Contains("new DispatcherDetailScheduler(window.Dispatcher, Logger)", app, StringComparison.Ordinal);
        Assert.Contains("ShellViewModel.CreateWindows(_announcer, focus, platform, Logger, detailScheduler)", app, StringComparison.Ordinal);
        Assert.Contains("window.DetailScheduler = detailScheduler;", app, StringComparison.Ordinal);
        Assert.True(app.IndexOf("window.DetailScheduler = detailScheduler;", StringComparison.Ordinal) < app.IndexOf("window.Attach(Shell);", StringComparison.Ordinal));

        // The adapter: the policy over the quiet-period poster over the dispatcher, one timer of the
        // quiet period restarted by every post and stopped by its own tick and by every withdrawal,
        // a Background post after it, Abort as its withdrawal, hold and release on the UI thread.
        string scheduler = File.ReadAllText(Path.Combine(appDirectory, "Platform", "DispatcherDetailScheduler.cs"));
        Assert.Contains("public sealed class DispatcherDetailScheduler : IDetailScheduler", scheduler, StringComparison.Ordinal);
        Assert.Contains("_quietPeriod = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = DetailRefreshTiming.QuietPeriod };", scheduler, StringComparison.Ordinal);
        Assert.Contains("_quietPeriod.Tick += OnQuietPeriodTick;", scheduler, StringComparison.Ordinal);
        Assert.Contains("_poster = new QuietPeriodPoster(RestartQuietPeriod, _quietPeriod.Stop, QueueAtIdle, logger);", scheduler, StringComparison.Ordinal);
        Assert.Contains("new CoalescingDetailScheduler(_poster.Post, logger)", scheduler, StringComparison.Ordinal);
        string restart = Method(scheduler, "void RestartQuietPeriod(");
        Assert.True(restart.IndexOf("_quietPeriod.Stop();", StringComparison.Ordinal) >= 0 && restart.IndexOf("_quietPeriod.Stop();", StringComparison.Ordinal) < restart.IndexOf("_quietPeriod.Start();", StringComparison.Ordinal), "a post starts the quiet period again from zero: Stop, then Start");
        Assert.Contains("_poster.OnQuietPeriodElapsed();", Method(scheduler, "void OnQuietPeriodTick("), StringComparison.Ordinal);
        string queue = Method(scheduler, "Action QueueAtIdle(");
        Assert.Contains("_dispatcher.BeginInvoke(DispatcherPriority.Background, work)", queue, StringComparison.Ordinal);
        Assert.Contains("return () => operation.Abort();", queue, StringComparison.Ordinal);
        Assert.Contains("_policy.Schedule(refresh);", Method(scheduler, "void Schedule("), StringComparison.Ordinal);
        Assert.Contains("_policy.Flush();", Method(scheduler, "void Flush("), StringComparison.Ordinal);
        Assert.Contains("_policy.Hold();", Method(scheduler, "void Hold("), StringComparison.Ordinal);
        Assert.Contains("_policy.Release();", Method(scheduler, "void Release("), StringComparison.Ordinal);
        Assert.DoesNotContain("_pending", scheduler, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherPriority.Send", scheduler, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherPriority.Normal", scheduler, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherPriority.Input", scheduler, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(scheduler, "Interval"));
        Assert.Equal(TimeSpan.FromMilliseconds(300), DetailRefreshTiming.QuietPeriod);

        // The window: the selection is set at once; a key or button down in the list holds, a key or
        // button up anywhere (and deactivation) releases; every entry into the pane flushes.
        string window = File.ReadAllText(Path.Combine(appDirectory, "MainWindow.xaml.cs"));
        string selection = Method(window, "void OnGroupListSelectionChanged(");
        Assert.Contains("_shell.List.SelectedItem = item;", selection, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginInvoke", selection, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureDetailCurrent", selection, StringComparison.Ordinal);
        Assert.DoesNotContain("Hold()", selection, StringComparison.Ordinal);
        string listKeys = Method(window, "void OnGroupsPreviewKeyDown(");
        Assert.StartsWith("void OnGroupsPreviewKeyDown(object sender, KeyEventArgs e)\n    {\n        //", listKeys.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.True(
            listKeys.IndexOf("DetailScheduler?.Hold();", StringComparison.Ordinal) < listKeys.IndexOf("if (_shell is null)", StringComparison.Ordinal),
            "the hold must be the first statement of OnGroupsPreviewKeyDown, before any early return");
        Assert.Contains("DetailScheduler?.Hold();", Method(window, "void OnGroupsPreviewMouseDown("), StringComparison.Ordinal);
        Assert.Contains("DetailScheduler?.Release();", Method(window, "void OnWindowPreviewKeyUp("), StringComparison.Ordinal);
        Assert.Contains("DetailScheduler?.Release();", Method(window, "void OnWindowPreviewMouseUp("), StringComparison.Ordinal);
        Assert.Contains("DetailScheduler?.Release();", Method(window, "void OnDeactivated("), StringComparison.Ordinal);
        Assert.Equal(2, CountOf(window, "DetailScheduler?.Hold();"));
        Assert.Equal(3, CountOf(window, "DetailScheduler?.Release();"));
        Assert.Contains("public Platform.DispatcherDetailScheduler? DetailScheduler { get; set; }", window, StringComparison.Ordinal);
        string focusFirstField = Method(window, "bool FocusFirstDetailField(");
        Assert.True(
            focusFirstField.IndexOf("_shell?.EnsureDetailCurrent();", StringComparison.Ordinal) < focusFirstField.IndexOf("SectionsControl.UpdateLayout();", StringComparison.Ordinal),
            "FocusFirstDetailField must run the pending detail refresh before it looks for the first field");
        string gotFocus = Method(window, "void OnDetailPreviewGotKeyboardFocus(");
        Assert.Contains("_shell.EnsureDetailCurrent();", gotFocus, StringComparison.Ordinal);
        Assert.Contains("e.NewFocus is not FrameworkElement { DataContext: DetailFieldViewModel field } target", gotFocus, StringComparison.Ordinal);
        Assert.Contains("(target.IsVisible && IsShown(field))", gotFocus, StringComparison.Ordinal);
        Assert.Contains("_delegatingDetailFocus", gotFocus, StringComparison.Ordinal);

        // The pane is updated in place, so a box focus was aimed at can outlive the row it showed:
        // focus aimed at the pane as it was before a refresh that changed the row still goes to
        // the first field.
        Assert.Contains("VariableItemViewModel? shown = _shell.Detail.Item;", gotFocus, StringComparison.Ordinal);
        Assert.Contains("bool rowChanged = !ReferenceEquals(shown, _shell.Detail.Item);", gotFocus, StringComparison.Ordinal);
        Assert.Contains("(!rowChanged && (target.IsVisible && IsShown(field)))", gotFocus, StringComparison.Ordinal);
        Assert.True(
            gotFocus.IndexOf("VariableItemViewModel? shown = _shell.Detail.Item;", StringComparison.Ordinal) < gotFocus.IndexOf("_shell.EnsureDetailCurrent();", StringComparison.Ordinal)
            && gotFocus.IndexOf("_shell.EnsureDetailCurrent();", StringComparison.Ordinal) < gotFocus.IndexOf("bool rowChanged", StringComparison.Ordinal),
            "the row shown is taken before the pending refresh runs and compared after it");
        Assert.True(
            gotFocus.IndexOf("e.Handled = true;", StringComparison.Ordinal) < gotFocus.IndexOf("FocusFirstDetailField();", StringComparison.Ordinal),
            "a focus change headed for a field the refresh removed or kept for another row is cancelled, then delegated to the first field");

        // An Alt key anywhere in the window runs the pending refresh before the access key is
        // resolved (finding A2-7 and its review): the access key clicks its button without moving
        // focus into the pane, Copy value's reads the pane, and the Change group's buttons and
        // their variable are the pane's.
        string windowKeys = Method(window, "void OnWindowPreviewKeyDown(");
        int altKey = windowKeys.IndexOf("if (e.Key == Key.System)", StringComparison.Ordinal);
        Assert.True(altKey >= 0, "OnWindowPreviewKeyDown must look for an Alt key (Key.System)");
        Assert.True(
            windowKeys.IndexOf("_shell?.EnsureDetailCurrent();", altKey, StringComparison.Ordinal) > altKey,
            "an Alt key must run the pending detail refresh");
        Assert.True(
            altKey < windowKeys.IndexOf("if (e.Key == Key.Escape", StringComparison.Ordinal),
            "the Alt key's refresh comes before any branch that returns");
        Assert.Equal(3, CountOf(window, "EnsureDetailCurrent("));
        Assert.Contains("Region.Detail => FocusFirstDetailField()", window, StringComparison.Ordinal);
        Assert.Contains("FocusTarget.DetailFirstField => FocusFirstDetailField()", window, StringComparison.Ordinal);
        Assert.Contains("case Key.Enter when plain:", listKeys, StringComparison.Ordinal);
        Assert.Contains("case Key.Space when plain:", listKeys, StringComparison.Ordinal);
        Assert.Contains("FocusFirstDetailField();", listKeys, StringComparison.Ordinal);
        Assert.Contains("_shell.List.SelectedItem ?? _shell.Detail.Item ?? FirstVisibleItem()", Method(window, "void OnDetailPreviewKeyDown("), StringComparison.Ordinal);

        // The handlers are wired in the XAML on the elements the summary names.
        XElement xaml = LoadMainWindow();
        Assert.Equal("OnWindowPreviewKeyUp", Attr(xaml, "PreviewKeyUp"));
        Assert.Equal("OnWindowPreviewMouseUp", Attr(xaml, "PreviewMouseUp"));
        Assert.Equal("OnDeactivated", Attr(xaml, "Deactivated"));
        XElement groups = Named(xaml, "ItemsControl", "GroupsControl");
        Assert.Equal("OnGroupsPreviewKeyDown", Attr(groups, "PreviewKeyDown"));
        Assert.Equal("OnGroupsPreviewMouseDown", Attr(groups, "PreviewMouseDown"));
        Assert.Equal("OnDetailPreviewGotKeyboardFocus", Attr(Named(xaml, "GroupBox", "DetailRegion"), "PreviewGotKeyboardFocus"));
        XElement onlyFocusHandler = Assert.Single(xaml.DescendantsAndSelf(), element => element.Attribute("PreviewGotKeyboardFocus") is not null);
        Assert.Equal("DetailRegion", Attr(onlyFocusHandler, Xaml + "Name"));

        // The shell: selection changes only schedule; Copy value, Export and every load settle the pane first.
        string shell = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "src", "UefiAccess.Presentation", "ViewModels", "ShellViewModel.cs"));
        string onSelectionChanged = Method(shell, "void OnSelectionChanged(");
        Assert.Contains("_detailScheduler.Schedule(RefreshDetail);", onSelectionChanged, StringComparison.Ordinal);
        Assert.DoesNotContain("Detail.Show(", onSelectionChanged, StringComparison.Ordinal);
        Assert.Contains("EnsureDetailCurrent();", Method(shell, "void CopyValue("), StringComparison.Ordinal);
        Assert.Contains("EnsureDetailCurrent();", Method(shell, "void ExportVisibleList("), StringComparison.Ordinal);
        Assert.Equal(2, CountOf(Method(shell, "void ShowResult("), "EnsureDetailCurrent();"));
        Assert.Contains("detailScheduler ?? new ImmediateDetailScheduler()", shell, StringComparison.Ordinal);

        // The sections and the fields are not virtualised (see the summary); the rows grid still is.
        XElement sections = Named(xaml, "ItemsControl", "SectionsControl");
        Assert.Empty(sections.Descendants(Presentation + "VirtualizingStackPanel"));
        Assert.Empty(sections.Descendants(Presentation + "ItemsControl.ItemsPanel"));
        Assert.Equal(string.Empty, Attr(sections, "ItemsPanel"));
        Assert.Equal(string.Empty, Attr(sections, "VirtualizingPanel.IsVirtualizing"));
        Assert.Single(groups.Descendants(Presentation + "VirtualizingStackPanel"));
        Assert.Contains("DispatcherDetailScheduler", File.ReadAllText(MainWindowPath()), StringComparison.Ordinal);
    }

    [Fact]
    public void AppReferencesCoreAndPresentationOnly()
    {
        string project = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "src", "UefiAccess.App", "UefiAccess.App.csproj"));
        XDocument document = XDocument.Parse(project);
        string[] references = document.Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(((string?)reference.Attribute("Include") ?? string.Empty).Replace('\\', '/')))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[] { "UefiAccess.Core", "UefiAccess.Presentation" }, references);
        Assert.Empty(document.Descendants("PackageReference"));
        Assert.Equal("true", (string?)document.Descendants("UseWPF").SingleOrDefault());
    }

    [Fact]
    public void ManifestRequiresAdministrator()
    {
        string manifestPath = Path.Combine(RepositoryPaths.Root, "src", "UefiAccess.App", "app.manifest");
        Assert.True(File.Exists(manifestPath), "app.manifest missing at " + manifestPath);

        XDocument manifest = XDocument.Load(manifestPath);
        XElement? level = manifest.Descendants().SingleOrDefault(element => element.Name.LocalName == "requestedExecutionLevel");
        Assert.NotNull(level);
        Assert.Equal("requireAdministrator", (string?)level!.Attribute("level"));
    }

    // ---- Helpers ----------------------------------------------------------------------------

    private static IEnumerable<XElement> Elements(XElement window, IEnumerable<string> localNames)
    {
        HashSet<string> names = localNames.ToHashSet(StringComparer.Ordinal);
        return window.DescendantsAndSelf().Where(element => IsShellElement(element) && names.Contains(element.Name.LocalName));
    }

    /// <summary>A stock control (presentation namespace) or one of the application's own splitters (Platform namespace).</summary>
    private static bool IsShellElement(XElement element)
    {
        return element.Name.Namespace == Presentation || element.Name.Namespace == Platform;
    }

    /// <summary>
    /// The variable rows' style: the one ListViewItem style without an x:Key (since M4 the list of
    /// Dell BIOS settings has a keyed ListViewItem style of its own, DellSettingRowStyle).
    /// </summary>
    private static XElement VariableRowStyle(XElement window)
    {
        return window.Descendants(Presentation + "Style").Single(style => Attr(style, "TargetType") == "ListViewItem" && Attr(style, Xaml + "Key").Length == 0);
    }

    /// <summary>The ListView of rows inside a group's Expander: the one input that carries no help text of its own.</summary>
    private static bool IsGroupRowsGrid(XElement element)
    {
        return element.Name == Presentation + "ListView" && element.Parent?.Name == Presentation + "Expander";
    }

    /// <summary>
    /// True for a read-only TextBox: by its own IsReadOnly, or by the style it uses (the keyed style
    /// its Style names, or the window's implicit TextBox style when it names none), following each
    /// style's BasedOn until one sets IsReadOnly.
    /// </summary>
    private static bool IsReadOnlyTextBox(XElement window, XElement box)
    {
        string own = Attr(box, "IsReadOnly");
        if (own.Length > 0)
        {
            return own == "True";
        }

        string reference = Attr(box, "Style");
        XElement? style = StyleOf(window, reference.Length == 0 ? "{StaticResource {x:Type TextBox}}" : reference);
        for (int depth = 0; style is not null && depth < 8; depth++)
        {
            XElement? setter = style.Elements(Presentation + "Setter").FirstOrDefault(candidate => Attr(candidate, "Property") == "IsReadOnly");
            if (setter is not null)
            {
                return Attr(setter, "Value") == "True";
            }

            style = StyleOf(window, Attr(style, "BasedOn"));
        }

        return false;
    }

    /// <summary>The style a "{StaticResource Key}" or "{StaticResource {x:Type T}}" reference names in the window's markup, or null.</summary>
    private static XElement? StyleOf(XElement window, string reference)
    {
        Match key = Regex.Match(reference, @"^\{StaticResource\s+(\w+)\}$", RegexOptions.CultureInvariant);
        if (key.Success)
        {
            return window.Descendants(Presentation + "Style").SingleOrDefault(style => Attr(style, Xaml + "Key") == key.Groups[1].Value);
        }

        Match type = Regex.Match(reference, @"^\{StaticResource\s+\{x:Type\s+(\w+)\}\}$", RegexOptions.CultureInvariant);
        return type.Success
            ? window.Descendants(Presentation + "Style").SingleOrDefault(style => Attr(style, Xaml + "Key").Length == 0 && Attr(style, "TargetType") == type.Groups[1].Value)
            : null;
    }

    private static XElement Named(XElement window, string localName, string name)
    {
        return window.Descendants().Single(element => IsShellElement(element) && element.Name.LocalName == localName && Attr(element, Xaml + "Name") == name);
    }

    /// <summary>The top and bottom of a XAML Thickness ("4", "12,6" or "0,0,8,4").</summary>
    private static (double Top, double Bottom) VerticalThickness(string thickness)
    {
        double[] parts = thickness.Split(',').Select(part => double.Parse(part.Trim(), CultureInfo.InvariantCulture)).ToArray();
        return parts.Length switch
        {
            1 => (parts[0], parts[0]),
            2 => (parts[1], parts[1]),
            4 => (parts[1], parts[3]),
            _ => throw new Xunit.Sdk.XunitException("not a Thickness: " + thickness),
        };
    }

    private static string Attr(XElement element, XName name) => (string?)element.Attribute(name) ?? string.Empty;

    /// <summary>True for a brush property under its own name or type-qualified ("TextElement.Foreground").</summary>
    internal static bool IsBrushProperty(string name)
    {
        string property = name[(name.LastIndexOf('.') + 1)..];
        return BrushAttributes.Contains(property, StringComparer.Ordinal);
    }

    /// <summary>Records a brush value that is not a dynamic SystemColors brush key; returns 1 when the value is one.</summary>
    private static int CheckBrush(string where, string value, List<string> violations)
    {
        if (DynamicSystemBrush.IsMatch(value))
        {
            return 1;
        }

        violations.Add(where + "=" + value);
        return 0;
    }

    /// <summary>The automation name as attribute text or, for the property-element form, the serialized child element.</summary>
    private static string NameOf(XElement element)
    {
        string attribute = Attr(element, "AutomationProperties.Name");
        if (attribute.Length > 0)
        {
            return attribute;
        }

        XElement? child = element.Element(Presentation + "AutomationProperties.Name");
        return child is null ? string.Empty : child.ToString();
    }

    private static int? TabIndexOf(XElement element)
    {
        string value = Attr(element, "TabIndex");
        if (value.Length == 0)
        {
            value = Attr(element, "KeyboardNavigation.TabIndex");
        }

        return value.Length == 0 ? null : int.Parse(value, CultureInfo.InvariantCulture);
    }

    private static bool IsInsideTemplate(XElement element)
    {
        return element.Ancestors().Any(ancestor => ancestor.Name == Presentation + "DataTemplate" || ancestor.Name == Presentation + "ItemsPanelTemplate" || ancestor.Name == Presentation + "Style");
    }

    private static string SetterValue(XElement style, string property)
    {
        XElement? setter = style.Elements(Presentation + "Setter").FirstOrDefault(candidate => Attr(candidate, "Property") == property)
            ?? style.Descendants(Presentation + "Setter").FirstOrDefault(candidate => Attr(candidate, "Property") == property);
        return setter is null ? string.Empty : Attr(setter, "Value");
    }

    /// <summary>The public and internal string constants of a type, each as ("Type.Constant", its text).</summary>
    private static IEnumerable<(string Name, string Text)> LiteralStrings(Type type)
    {
        return type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string) && (field.IsPublic || field.IsAssembly))
            .Select(field => (type.Name + "." + field.Name, (string)field.GetRawConstantValue()!));
    }

    /// <summary>Replaces every {x:Static res:Strings.X} in a value with the constant's text.</summary>
    internal static string ResolveStrings(string value)
    {
        return StringsReference.Replace(value, match =>
        {
            FieldInfo? field = typeof(Strings).GetField(match.Groups[1].Value, BindingFlags.Public | BindingFlags.Static);
            return field?.GetRawConstantValue() as string ?? match.Value;
        });
    }

    /// <summary>Every Strings constant used as a label, name, heading, column or option.</summary>
    private static IEnumerable<FieldInfo> LabelConstants()
    {
        string[] prefixes = { "Column", "Section", "Region", "GroupBy", "SortBy" };
        return typeof(Strings).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Where(field => field.Name.EndsWith("Label", StringComparison.Ordinal) || field.Name.EndsWith("Name", StringComparison.Ordinal) || prefixes.Any(prefix => field.Name.StartsWith(prefix, StringComparison.Ordinal)));
    }

    /// <summary>The text of one method: from the given signature fragment to the closing brace of the member.</summary>
    private static string Method(string source, string signatureFragment)
    {
        int start = source.IndexOf(signatureFragment, StringComparison.Ordinal);
        Assert.True(start >= 0, "method not found: " + signatureFragment);
        int end = source.IndexOf("\n    }", start, StringComparison.Ordinal);
        Assert.True(end >= 0, "end of method not found: " + signatureFragment);
        return source[start..end];
    }

    private static int CountOf(string text, string value)
    {
        int count = 0;
        for (int index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static string Describe(XElement element)
    {
        string name = Attr(element, Xaml + "Name");
        return element.Name.LocalName + (name.Length == 0 ? string.Empty : " x:Name=" + name);
    }

    private static XElement LoadMainWindow()
    {
        XDocument document = XDocument.Load(MainWindowPath());
        Assert.NotNull(document.Root);
        Assert.Equal(Presentation + "Window", document.Root!.Name);
        return document.Root;
    }

    /// <summary>The markup with every XML comment removed, so substring checks see only what compiles.</summary>
    private static string MainWindowMarkup()
    {
        return Regex.Replace(File.ReadAllText(MainWindowPath()), @"<!--.*?-->", string.Empty, RegexOptions.Singleline | RegexOptions.CultureInvariant);
    }

    private static string MainWindowPath()
    {
        string path = Path.Combine(RepositoryPaths.Root, "src", "UefiAccess.App", "MainWindow.xaml");
        Assert.True(File.Exists(path), "MainWindow.xaml missing at " + path);
        return path;
    }
}
