// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Xml.Linq;
using UefiAccess.Presentation.Accessibility;
using Xunit;
using static UefiAccess.Core.Tests.MainWindowMarkup;

namespace UefiAccess.Core.Tests;

/// <summary>
/// Round 2 (findings F021 and F022): the Status region, the machine identity strip and the only
/// live region, must stay in view whatever the window size, text size or display scale. At the
/// 900 × 600 minimum with 150 or 200 percent text the fixed row minimums used to add up to more
/// than the window, and the last row, Status, went below the bottom edge; a log panel grown with
/// the keyboard could push it out as well, because the stock splitter ignored the star row's
/// minimum. Now the Status region has a row of its own at the bottom, the five regions above it
/// sit in a scroll viewer that scrolls only when they cannot fit, the list/detail row and the log
/// row are star rows (so the stock splitter keeps both minimums), the list/detail row gives way
/// first (<see cref="WindowFit.ListDetailMinimum"/>), and the start-up and minimum sizes are
/// reduced to the work area (<see cref="WindowFit.ToWorkArea"/>). Since M4 a sixth region sits
/// above Status, Dell settings, in an Auto row after the log row, and its list is capped
/// (<see cref="WindowFit.DellListMaximum"/>) so it scrolls inside instead of pushing Status down.
/// The policies are unit-tested in WindowFitTests; these checks pin the markup and the
/// code-behind that apply them.
/// </summary>
public sealed class WpfShellLayoutTests
{
    [Fact]
    public void TheStatusRegionHasARowOfItsOwnBelowEverythingElse()
    {
        XElement window = Load();
        XElement root = Assert.Single(window.Elements(Wpf + "Grid"));
        Assert.Equal("LayoutRoot", Attr(root, Xaml + "Name"));
        List<XElement> rows = root.Element(Wpf + "Grid.RowDefinitions")!.Elements(Wpf + "RowDefinition").ToList();
        Assert.Equal(new[] { "*", "Auto" }, rows.Select(row => Attr(row, "Height")));
        Assert.All(rows, row => Assert.Equal(string.Empty, Attr(row, "MinHeight")));

        XElement status = Named(window, "GroupBox", "StatusRegion");
        Assert.Same(root, status.Parent);
        Assert.Equal("1", Attr(status, "Grid.Row"));

        XElement scroller = Named(window, "ScrollViewer", "PanesScroller");
        Assert.Same(root, scroller.Parent);
        Assert.Equal("0", Attr(scroller, "Grid.Row"));

        // The UI Automation order: Toolbar, Filter, List, Detail, Log, since M4 Dell settings, then
        // Status; every region but Status inside the scroll viewer.
        string[] regions = window.Descendants(Wpf + "GroupBox").Select(element => Attr(element, Xaml + "Name")).Where(name => name.EndsWith("Region", StringComparison.Ordinal)).ToArray();
        Assert.Equal(new[] { "ToolbarRegion", "FilterRegion", "ListRegion", "DetailRegion", "LogRegion", "DellRegion", "StatusRegion" }, regions);
        foreach (string region in regions.Take(6))
        {
            Assert.Contains(scroller, Named(window, "GroupBox", region).Ancestors());
        }

        Assert.DoesNotContain(scroller, status.Ancestors());
    }

    [Fact]
    public void ThePartAboveStatusScrollsOnlyWhenItCannotFit()
    {
        XElement window = Load();
        XElement scroller = Named(window, "ScrollViewer", "PanesScroller");
        Assert.Equal("Auto", Attr(scroller, "VerticalScrollBarVisibility"));
        Assert.Equal("Disabled", Attr(scroller, "HorizontalScrollBarVisibility"));
        Assert.Equal("False", Attr(scroller, "Focusable"));
        Assert.Equal("OnLayoutPartSizeChanged", Attr(scroller, "SizeChanged"));

        // Layout only: no name, so a screen reader passes over it like any unnamed pane.
        Assert.Equal(string.Empty, Attr(scroller, "AutomationProperties.Name"));
        Assert.Equal(string.Empty, Attr(scroller, "AutomationProperties.HelpText"));

        // The grid is exactly as high as the viewer (so the star rows share its height) and never
        // lower than its rows need (FitPanesToWindow sets its MinHeight): a scroll bar appears
        // only when that minimum is more than the viewer's height.
        XElement panes = Assert.Single(scroller.Elements(Wpf + "Grid"));
        Assert.Equal("PanesGrid", Attr(panes, Xaml + "Name"));
        Assert.Equal("{Binding ViewportHeight, ElementName=PanesScroller, Mode=OneWay}", Attr(panes, "Height"));

        string code = Source("MainWindow.xaml.cs");
        string fit = Method(code, "private void FitPanesToWindow(");
        Assert.Contains("WindowFit.ListDetailMinimum(PanesScroller.ActualHeight - fixedRows - LogRow.MinHeight)", fit, StringComparison.Ordinal);
        Assert.Contains("ListDetailRow.MinHeight = listDetail;", fit, StringComparison.Ordinal);
        Assert.Contains("double panes = fixedRows + ListDetailRow.MinHeight + LogRow.MinHeight;", fit, StringComparison.Ordinal);
        Assert.Contains("PanesGrid.MinHeight = panes;", fit, StringComparison.Ordinal);
        Assert.DoesNotContain("Math.Ceiling", fit, StringComparison.Ordinal);
        Assert.Contains("FitPanesToWindow();", Method(code, "private void OnLayoutPartSizeChanged("), StringComparison.Ordinal);

        // The list of Dell BIOS settings is capped first, from the
        // viewer's height and the list's line height, which a method of its own computes (the
        // rounding up is kept out of FitPanesToWindow); the Dell row is an Auto row, so it is
        // counted among the fixed rows.
        Assert.Contains("WindowFit.DellListMaximum(PanesScroller.ActualHeight, DellLineHeight())", fit, StringComparison.Ordinal);
        Assert.Contains("DellSettingsList.MaxHeight = ", fit, StringComparison.Ordinal);
        Assert.True(fit.IndexOf("DellSettingsList.MaxHeight = ", StringComparison.Ordinal) < fit.IndexOf("double fixedRows = 0;", StringComparison.Ordinal), "the Dell list's cap is set before the rows are fitted");
        Assert.Contains("!ReferenceEquals(row, ListDetailRow) && !ReferenceEquals(row, LogRow)", fit, StringComparison.Ordinal);
        Assert.Contains("return Math.Ceiling(DellSettingsList.FontSize * DellSettingsList.FontFamily.LineSpacing) + 8;", Method(code, "private double DellLineHeight("), StringComparison.Ordinal);

        // Refitted whenever the room or the fixed rows change: the viewer (window resize, a new
        // status sentence), the toolbar and the filter row (a text size change, the toolbar
        // wrapping), and since M4 the Dell settings region (a longer state line).
        foreach (string region in new[] { "ToolbarRegion", "FilterRegion", "DellRegion" })
        {
            Assert.Equal("OnLayoutPartSizeChanged", Attr(Named(window, "GroupBox", region), "SizeChanged"));
        }
    }

    [Fact]
    public void TheListDetailRowAndTheLogRowShareTheHeightAsStarRows()
    {
        XElement window = Load();
        XElement panes = Named(window, "Grid", "PanesGrid");
        List<XElement> rows = panes.Element(Wpf + "Grid.RowDefinitions")!.Elements(Wpf + "RowDefinition").ToList();
        Assert.Equal(6, rows.Count);
        XElement listDetail = Named(window, "RowDefinition", "ListDetailRow");
        XElement log = Named(window, "RowDefinition", "LogRow");
        Assert.Equal(2, rows.IndexOf(listDetail));
        Assert.Equal(4, rows.IndexOf(log));

        // M4: the Dell settings region's row comes after the log row and sizes to its content, so
        // the two star rows still share the height and the log splitter keeps its 30 percent.
        XElement dell = Named(window, "RowDefinition", "DellRow");
        Assert.Equal(5, rows.IndexOf(dell));
        Assert.Equal("Auto", Attr(dell, "Height"));
        Assert.Equal(string.Empty, Attr(dell, "MinHeight"));
        Assert.Equal(string.Empty, Attr(dell, "MaxHeight"));
        Assert.Equal(2, rows.Count(row => Attr(row, "Height").EndsWith("*", StringComparison.Ordinal)));
        Assert.EndsWith("*", Attr(listDetail, "Height"), StringComparison.Ordinal);
        Assert.EndsWith("*", Attr(log, "Height"), StringComparison.Ordinal);
        Assert.Equal(WindowFit.PreferredListDetailHeight, double.Parse(Attr(listDetail, "MinHeight"), CultureInfo.InvariantCulture));
        Assert.Contains("ElementName=ToolbarRegion", Attr(log, "MinHeight"), StringComparison.Ordinal);
        Assert.Equal(string.Empty, Attr(log, "MaxHeight"));

        // Three tenths of the shared height for the log panel at start (about 110 device-independent
        // pixels in the OptiPlex's default window at 150 percent display scale): the log splitter starts at 30.
        double listStars = double.Parse(Attr(listDetail, "Height").TrimEnd('*'), CultureInfo.InvariantCulture);
        double logStars = double.Parse(Attr(log, "Height").TrimEnd('*'), CultureInfo.InvariantCulture);
        Assert.Equal(30, SplitterPosition.Percent(logStars, listStars + logStars));

        // The splitter between them sits in the Auto row in between, and the list/detail splitter's columns are star columns too.
        XElement logSplitter = Named(window, "AnnouncingGridSplitter", "LogSplitter");
        Assert.Equal("3", Attr(logSplitter, "Grid.Row"));
        Assert.Equal("Auto", Attr(rows[3], "Height"));
        XElement columns = Named(window, "AnnouncingGridSplitter", "ListDetailSplitter").Parent!.Element(Wpf + "Grid.ColumnDefinitions")!;
        List<XElement> definitions = columns.Elements(Wpf + "ColumnDefinition").ToList();
        Assert.EndsWith("*", Attr(definitions[0], "Width"), StringComparison.Ordinal);
        Assert.EndsWith("*", Attr(definitions[2], "Width"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheWindowStartsInsideTheWorkArea()
    {
        XElement window = Load();
        Assert.Equal(WindowFit.PreferredWidth, double.Parse(Attr(window, "Width"), CultureInfo.InvariantCulture));
        Assert.Equal(WindowFit.PreferredHeight, double.Parse(Attr(window, "Height"), CultureInfo.InvariantCulture));
        Assert.Equal(WindowFit.MinimumWidth, double.Parse(Attr(window, "MinWidth"), CultureInfo.InvariantCulture));
        Assert.Equal(WindowFit.MinimumHeight, double.Parse(Attr(window, "MinHeight"), CultureInfo.InvariantCulture));
        Assert.Equal(string.Empty, Attr(window, "WindowStartupLocation"));
        Assert.Equal(string.Empty, Attr(window, "SizeToContent"));

        string code = Source("MainWindow.xaml.cs");
        string constructor = Method(code, "public MainWindow()");
        Assert.True(
            constructor.IndexOf("InitializeComponent();", StringComparison.Ordinal) < constructor.IndexOf("FitToWorkArea(SystemParameters.WorkArea);", StringComparison.Ordinal),
            "the constructor fits the window to the work area after the XAML has set its sizes");

        string fit = Method(code, "private void FitToWorkArea(");
        Assert.Contains("WindowFit.ToWorkArea(workArea.Width, workArea.Height)", fit, StringComparison.Ordinal);
        foreach (string assignment in new[] { "MinWidth = size.MinWidth;", "MinHeight = size.MinHeight;", "Width = size.Width;", "Height = size.Height;", "Left = workArea.Left + ((workArea.Width - size.Width) / 2);", "Top = workArea.Top + ((workArea.Height - size.Height) / 2);" })
        {
            Assert.Contains(assignment, fit, StringComparison.Ordinal);
        }
    }
}
