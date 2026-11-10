// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Xml.Linq;
using Xunit;
using static UefiAccess.Core.Tests.MainWindowMarkup;

namespace UefiAccess.Core.Tests;

/// <summary>
/// Round 2 low-vision findings (F064, F161, F163, F164, F165, F166, F170), all fixed with style
/// setters and triggers on the stock controls and SystemColors brush keys only: no control
/// template, no FocusVisualStyle (WPF draws a focus visual only once a key has been pressed), no
/// colour of its own. Style triggers outrank the triggers of a stock control template, so each
/// indicator below shows in the default theme and in the Classic theme WPF uses for every contrast
/// theme. The exact brushes are asserted because the contrast figures in the XAML comments depend
/// on them.
/// </summary>
public sealed class WpfShellLowVisionTests
{
    [Fact]
    public void ButtonsShowFocusWithAThickerBorderFromTheStart()
    {
        // F163: Refresh is focused when the window opens, before any key press, so the stock
        // dotted rectangle is not drawn; the trigger depends on keyboard focus alone.
        XElement window = Load();
        XElement style = KeyedStyle(window, "ToolbarButtonStyle");
        XElement focused = Trigger(style, "IsKeyboardFocused", "True");
        Assert.Equal("2", TriggerSetterValue(focused, "BorderThickness"));
        Assert.Equal(SystemBrush("ControlTextBrushKey"), TriggerSetterValue(focused, "BorderBrush"));

        // The padding gives back the extra pixel on each side, so the label does not move.
        Assert.Equal("12,6", SetterValue(style, "Padding"));
        Assert.Equal("11,5", TriggerSetterValue(focused, "Padding"));
        Assert.All(window.Descendants(Wpf + "Button"), button => Assert.Equal("{StaticResource ToolbarButtonStyle}", Attr(button, "Style")));
    }

    [Fact]
    public void EveryTextBoxShowsFocusWithAThreePixelBorder()
    {
        // F161: the stock default theme only changes the colour of a one-pixel border (1.27:1).
        XElement window = Load();
        XElement textBoxes = ImplicitStyle(window, "TextBox");
        XElement focused = Trigger(textBoxes, "IsKeyboardFocused", "True");
        Assert.Equal("3", TriggerSetterValue(focused, "BorderThickness"));
        Assert.Equal("2", SetterValue(textBoxes, "Padding"));
        Assert.Equal("0", TriggerSetterValue(focused, "Padding"));

        // Every text box gets it: the detail fields and the filter box through styles based on
        // it, the Writability and Status boxes as the implicit style; none sets its own padding
        // or border, which would outrank the trigger.
        Assert.Equal("{StaticResource {x:Type TextBox}}", Attr(KeyedStyle(window, "DetailFieldStyle"), "BasedOn"));
        XElement filterStyle = KeyedStyle(window, "FilterBoxStyle");
        Assert.Equal("{StaticResource {x:Type TextBox}}", Attr(filterStyle, "BasedOn"));
        Assert.Equal("4,3", SetterValue(filterStyle, "Padding"));
        Assert.Equal("2,1", TriggerSetterValue(Trigger(filterStyle, "IsKeyboardFocused", "True"), "Padding"));
        Assert.Equal("{StaticResource FilterBoxStyle}", Attr(Named(window, "TextBox", "FilterBox"), "Style"));
        foreach (XElement box in window.Descendants(Wpf + "TextBox"))
        {
            Assert.Equal(string.Empty, Attr(box, "Padding"));
            Assert.Equal(string.Empty, Attr(box, "BorderThickness"));
            Assert.Equal(string.Empty, Attr(box, "BorderBrush"));
        }

        Assert.Equal(string.Empty, Attr(Named(window, "TextBox", "ReadOnlyStatusBox"), "Style"));
        Assert.Equal(string.Empty, Attr(Named(window, "TextBox", "StatusBox"), "Style"));
    }

    /// <summary>
    /// The variable rows, since M3 the rows of the boot order list in the Change group, and since
    /// M4 the rows of the list of Dell BIOS settings, each with a keyed row style of its own
    ///: without these triggers a
    /// focused boot order row or Dell row would have no focus rule at all.
    /// </summary>
    [Fact]
    public void TheFocusedRowAndAnInactiveSelectedRowAreOutlinedInEveryTheme()
    {
        XElement window = Load();
        AssertRowsAreOutlined(window.Descendants(Wpf + "Style").Single(style => Attr(style, "TargetType") == "ListViewItem" && Attr(style, Xaml + "Key").Length == 0));
        XElement bootOrderRows = KeyedStyle(window, "BootOrderRowStyle");
        Assert.Equal("ListBoxItem", Attr(bootOrderRows, "TargetType"));
        AssertRowsAreOutlined(bootOrderRows);
        Assert.Equal("{StaticResource BootOrderRowStyle}", Attr(Named(window, "ListBox", "BootOrderList"), "ItemContainerStyle"));
        XElement dellRows = KeyedStyle(window, "DellSettingRowStyle");
        Assert.Equal("ListViewItem", Attr(dellRows, "TargetType"));
        AssertRowsAreOutlined(dellRows);
        Assert.Equal("{StaticResource DellSettingRowStyle}", Attr(Named(window, "ListView", "DellSettingsList"), "ItemContainerStyle"));

        // Still no template and no focus visual of our own.
        string markup = WithoutComments(Path.Combine(AppDirectory, "MainWindow.xaml"));
        Assert.DoesNotContain("FocusVisualStyle", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("ControlTemplate", markup, StringComparison.Ordinal);
    }

    /// <summary>The three row triggers of a row style, with the exact brushes the contrast figures in the XAML comments depend on.</summary>
    private static void AssertRowsAreOutlined(XElement rows)
    {
        Assert.Equal("2", SetterValue(rows, "BorderThickness"));
        List<XElement> triggers = rows.Element(Wpf + "Style.Triggers")!.Elements().ToList();
        Assert.Equal(3, triggers.Count);

        // F166: a selected row keeps a dark outline while the list does not have focus (the stock
        // default theme greys it out to 1.2 to 1.6:1).
        XElement inactive = triggers[0];
        Assert.Equal(Wpf + "MultiTrigger", inactive.Name);
        List<XElement> inactiveConditions = inactive.Descendants(Wpf + "Condition").ToList();
        Assert.Contains(inactiveConditions, condition => Attr(condition, "Property") == "IsSelected" && Attr(condition, "Value") == "True");
        Assert.Contains(inactiveConditions, condition => Attr(condition, "Property") == "Selector.IsSelectionActive" && Attr(condition, "Value") == "False");
        Assert.Equal(SystemBrush("ControlDarkDarkBrushKey"), TriggerSetterValue(inactive, "BorderBrush"));

        // The focused row: Highlight in the default theme; HighlightText when it is selected in a
        // contrast theme, where every selected row is already filled with Highlight, focused or
        // not (F064). The contrast-theme trigger comes last, so it wins when both apply. It needs
        // the row to be selected as well: a focused row that is not selected (Ctrl+Up, Ctrl+Down)
        // has no Highlight fill, and HighlightText is black on the black or near-black Window
        // colour of Aquatic and Night sky, so that row keeps the Highlight outline.
        XElement focused = triggers[1];
        Assert.Equal(Wpf + "Trigger", focused.Name);
        Assert.Equal("IsKeyboardFocused", Attr(focused, "Property"));
        Assert.Equal(SystemBrush("HighlightBrushKey"), TriggerSetterValue(focused, "BorderBrush"));
        XElement contrast = triggers[2];
        Assert.Equal(Wpf + "MultiDataTrigger", contrast.Name);
        List<XElement> contrastConditions = contrast.Descendants(Wpf + "Condition").ToList();
        Assert.Equal(3, contrastConditions.Count);
        Assert.Contains(contrastConditions, condition => Attr(condition, "Binding") == "{Binding Path=(SystemParameters.HighContrast)}" && Attr(condition, "Value") == "True");
        Assert.Contains(contrastConditions, condition => Attr(condition, "Binding") == "{Binding IsKeyboardFocused, RelativeSource={RelativeSource Self}}" && Attr(condition, "Value") == "True");
        Assert.Contains(contrastConditions, condition => Attr(condition, "Binding") == "{Binding IsSelected, RelativeSource={RelativeSource Self}}" && Attr(condition, "Value") == "True");
        Assert.Equal(SystemBrush("HighlightTextBrushKey"), TriggerSetterValue(contrast, "BorderBrush"));
    }

    [Fact]
    public void SplittersStandOutAndAreWideEnoughToGrab()
    {
        // F165: ControlDark on the Control background was 2.29:1 in the default theme;
        // ControlDarkDark is 4.8:1 there and white or black on the contrast themes' backgrounds.
        XElement window = Load();
        XElement style = window.Descendants(Wpf + "Style").Single(candidate => Attr(candidate, "TargetType") == "platform:AnnouncingGridSplitter");
        Assert.Equal(SystemBrush("ControlDarkDarkBrushKey"), SetterValue(style, "Background"));
        XElement focused = Trigger(style, "IsKeyboardFocused", "True");
        Assert.Equal(SystemBrush("HighlightBrushKey"), TriggerSetterValue(focused, "Background"));
        Assert.Equal(SystemBrush("HighlightTextBrushKey"), TriggerSetterValue(focused, "BorderBrush"));
        Assert.Equal("2", TriggerSetterValue(focused, "BorderThickness"));

        Assert.Equal(10, int.Parse(Attr(Named(window, "AnnouncingGridSplitter", "ListDetailSplitter"), "Width"), CultureInfo.InvariantCulture));
        Assert.Equal(10, int.Parse(Attr(Named(window, "AnnouncingGridSplitter", "LogSplitter"), "Height"), CultureInfo.InvariantCulture));
    }

    [Fact]
    public void TextOnTheWindowBackgroundUsesTheWindowsOwnColourPair()
    {
        // F170: the stock contrast-theme Expander draws its header in WindowText on no background
        // of its own, that is on the window's Control background; ControlText belongs to it.
        XElement window = Load();
        Assert.Equal(SystemBrush("ControlBrushKey"), Attr(window, "Background"));
        Assert.Equal(SystemBrush("ControlTextBrushKey"), Attr(window, "Foreground"));
        Assert.Equal(SystemBrush("ControlTextBrushKey"), SetterValue(ImplicitStyle(window, "Expander"), "Foreground"));
        Assert.Equal(string.Empty, Attr(Assert.Single(window.Descendants(Wpf + "Expander")), "Foreground"));
    }

    [Fact]
    public void ColumnHeadersFitAtTheLargestTextSize()
    {
        // F164: at 225 percent text (27-pixel Segoe UI, the Windows maximum) "Namespace" needs
        // 139 device-independent pixels and "Flags" 61, plus the header's 6-pixel padding on each
        // side; the 140 and 70 wide columns cut both. Measured with WPF on this machine.
        XElement window = Load();
        Dictionary<string, double> widths = window.Descendants(Wpf + "GridViewColumn")
            .ToDictionary(column => Attr(column, "Header"), column => double.Parse(Attr(column, "Width"), CultureInfo.InvariantCulture));
        Assert.True(widths["{x:Static res:Strings.ColumnNamespace}"] >= 139 + 12 + 8, "Namespace column is " + widths["{x:Static res:Strings.ColumnNamespace}"]);
        Assert.True(widths["{x:Static res:Strings.ColumnFlags}"] >= 61 + 12 + 8, "Flags column is " + widths["{x:Static res:Strings.ColumnFlags}"]);
        Assert.Equal(170, widths["{x:Static res:Strings.ColumnNamespace}"]);
        Assert.Equal(90, widths["{x:Static res:Strings.ColumnFlags}"]);
        Assert.Equal("6,2", SetterValue(ImplicitStyle(window, "GridViewColumnHeader"), "Padding"));

        // M4: the three columns of the list of Dell BIOS settings
        // are at least 200 wide (220, 220 and 420; section 5.2); their cells wrap.
        foreach (string column in new[] { "ColumnDellSetting", "ColumnDellCurrent", "ColumnDellPossible" })
        {
            double width = widths["{x:Static res:Strings." + column + "}"];
            Assert.True(width >= 200, column + " is " + width);
        }
    }
}
