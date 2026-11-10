// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Presentation.Accessibility;
using Xunit;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// The window-size policy the WPF window applies (MainWindow.FitToWorkArea and FitPanesToWindow):
/// the start-up size (1200 × 800) and the minimum size (900 × 600) are reduced to the work area,
/// so at 150 or 200 percent display scale, or on a small screen, the window and its Status region
/// start inside the screen; and the list/detail row keeps 160 only while there is room, gives
/// way first, down to 80, before the part above Status scrolls. Work areas are in
/// device-independent pixels: a 1920 × 1080 screen less the 48-pixel Windows 11 taskbar is
/// 1280 × 672 at 150 percent display scale (the contracted OptiPlex 7020 runs at 150 percent),
/// 1097 × 569 at 175 percent and 960 × 492 at 200 percent.
/// </summary>
public sealed class WindowFitTests
{
    [Fact]
    public void TheWindowAsksForTheDocumentedSizes()
    {
        Assert.Equal(1200, WindowFit.PreferredWidth);
        Assert.Equal(800, WindowFit.PreferredHeight);
        Assert.Equal(900, WindowFit.MinimumWidth);
        Assert.Equal(600, WindowFit.MinimumHeight);
        Assert.Equal(160, WindowFit.PreferredListDetailHeight);
        Assert.Equal(80, WindowFit.SmallestListDetailHeight);
    }

    [Theory]
    [InlineData(1920, 1032, 1200, 800, 900, 600)] // 100 percent: everything fits as asked
    [InlineData(1280, 672, 1200, 672, 900, 600)] // 150 percent: the start-up height is reduced
    [InlineData(1097, 569, 1097, 569, 900, 569)] // 175 percent: the minimum height is too
    [InlineData(960, 492, 960, 492, 900, 492)] // 200 percent
    [InlineData(800, 552, 800, 552, 800, 552)] // a small screen: the minimum width as well
    public void StartUpAndMinimumSizesAreReducedToTheWorkArea(double workWidth, double workHeight, double width, double height, double minWidth, double minHeight)
    {
        WindowSize size = WindowFit.ToWorkArea(workWidth, workHeight);

        Assert.Equal(new WindowSize(width, height, minWidth, minHeight), size);
        Assert.True(size.Width <= workWidth && size.Height <= workHeight);
        Assert.True(size.MinWidth <= size.Width && size.MinHeight <= size.Height);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, double.NaN)]
    public void AnUnknownWorkAreaReducesNothing(double workWidth, double workHeight)
    {
        Assert.Equal(new WindowSize(1200, 800, 900, 600), WindowFit.ToWorkArea(workWidth, workHeight));
    }

    [Theory]
    [InlineData(400, 160)] // plenty of room: the preferred minimum
    [InlineData(160, 160)]
    [InlineData(123.6, 123)] // 200 percent text in the OptiPlex's default window: the row gives way, in whole pixels
    [InlineData(80.9, 80)]
    [InlineData(79, 80)] // never below the smallest height: the part above Status scrolls instead
    [InlineData(-150, 80)]
    [InlineData(double.NaN, 160)]
    public void TheListDetailRowGivesWayFirst_DownToItsSmallestHeight(double room, double expected)
    {
        Assert.Equal(expected, WindowFit.ListDetailMinimum(room));
    }

    [Fact]
    public void DellListMaximum_TakesAQuarterOfTheViewport_AndAlwaysFourLines()
    {
        Assert.Equal(0.25, WindowFit.DellListShare);
        Assert.Equal(4, WindowFit.DellListMinimumLines);
    }

    [Theory]
    [InlineData(800, 24, 200)] // a large viewport: a quarter of it
    [InlineData(803, 24, 200)] // in whole pixels
    [InlineData(430, 24, 107)] // 150 percent display scale (the unit): the panes below the toolbar and filter
    [InlineData(200, 24, 96)] // a small viewport: the header and three rows still fit
    [InlineData(430, 58, 232)] // 200 percent text: four lines are more than the quarter
    [InlineData(0, 24, 96)]
    [InlineData(-50, 24, 96)]
    [InlineData(800, 0, 200)]
    [InlineData(800, -5, 200)]
    [InlineData(double.NaN, 24, 96)]
    [InlineData(800, double.NaN, 200)]
    public void DellListMaximum_IsAQuarterOfTheViewport_ButNeverLessThanFourLines(double viewport, double lineHeight, double expected)
    {
        double maximum = WindowFit.DellListMaximum(viewport, lineHeight);

        Assert.Equal(expected, maximum);
        Assert.True(maximum >= 0);
        Assert.Equal(Math.Floor(maximum), maximum);
    }
}
