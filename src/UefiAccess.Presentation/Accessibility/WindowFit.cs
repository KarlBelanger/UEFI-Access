// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Presentation.Accessibility;

/// <summary>
/// The window's size and the height of its list/detail row, so that the window fits the screen
/// and the Status region (the identity strip and the only live region) is never pushed off it
/// (Alpha 1 round 2). The window asks for <see cref="PreferredWidth"/> by
/// <see cref="PreferredHeight"/> and a minimum of <see cref="MinimumWidth"/> by
/// <see cref="MinimumHeight"/> device-independent pixels; at a high display scale, or on a small
/// screen, that is more than the work area (the screen without the taskbar), so every one of the
/// four is reduced to the work area at start-up (<see cref="ToWorkArea"/>). Inside the window the
/// list/detail row keeps <see cref="PreferredListDetailHeight"/> only while there is room for it
/// below the toolbar and filter and above the log panel's minimum; when there is not, it gives
/// way first, down to <see cref="SmallestListDetailHeight"/> (<see cref="ListDetailMinimum"/>),
/// and only then does the part of the window above the Status region scroll. Since M4 the list of
/// Dell BIOS settings is capped as well (<see cref="DellListMaximum"/>), so it scrolls inside
/// instead of pushing the Status region down. The WPF window (UefiAccess.App) applies all three;
/// nothing here depends on WPF.
/// </summary>
public static class WindowFit
{
    /// <summary>The width the window asks for at start-up.</summary>
    public const double PreferredWidth = 1200;

    /// <summary>The height the window asks for at start-up.</summary>
    public const double PreferredHeight = 800;

    /// <summary>The smallest width the window can be given, when the work area allows it.</summary>
    public const double MinimumWidth = 900;

    /// <summary>The smallest height the window can be given, when the work area allows it.</summary>
    public const double MinimumHeight = 600;

    /// <summary>The height the list/detail row keeps whenever the window has room for it.</summary>
    public const double PreferredListDetailHeight = 160;

    /// <summary>The height the list/detail row never gives up: below it, the part above Status scrolls instead.</summary>
    public const double SmallestListDetailHeight = 80;

    /// <summary>The share of the panes' viewport the list of Dell BIOS settings may take before it scrolls inside.</summary>
    public const double DellListShare = 0.25;

    /// <summary>The lines the list of Dell BIOS settings may always show, however small the viewport: the column header and three rows.</summary>
    public const int DellListMinimumLines = 4;

    /// <summary>
    /// The window's start-up size and minimum size, each reduced to the work area when it is
    /// larger. A work area that is not a positive size (not known) reduces nothing.
    /// </summary>
    /// <param name="workAreaWidth">The width of the work area, in device-independent pixels.</param>
    /// <param name="workAreaHeight">The height of the work area, in device-independent pixels.</param>
    /// <returns>The size to open at and the minimum size.</returns>
    public static WindowSize ToWorkArea(double workAreaWidth, double workAreaHeight)
    {
        double width = workAreaWidth > 0 ? workAreaWidth : double.PositiveInfinity;
        double height = workAreaHeight > 0 ? workAreaHeight : double.PositiveInfinity;
        return new WindowSize(
            Math.Min(PreferredWidth, width),
            Math.Min(PreferredHeight, height),
            Math.Min(MinimumWidth, width),
            Math.Min(MinimumHeight, height));
    }

    /// <summary>
    /// The minimum height of the list/detail row: <see cref="PreferredListDetailHeight"/>, or the
    /// room there is when that is less, but never below <see cref="SmallestListDetailHeight"/>, so
    /// the log panel with its Copy log button keeps its place below it (when even the smallest
    /// height does not fit, the part above the Status region scrolls).
    /// </summary>
    /// <param name="room">The height left for the row: what the Status region leaves, minus the toolbar, the filter row, the splitter row and the log panel's minimum.</param>
    /// <returns>The minimum height, a whole number of device-independent pixels.</returns>
    public static double ListDetailMinimum(double room)
    {
        if (double.IsNaN(room))
        {
            return PreferredListDetailHeight;
        }

        return Math.Clamp(Math.Floor(room), SmallestListDetailHeight, PreferredListDetailHeight);
    }

    /// <summary>
    /// The largest height of the list of Dell BIOS settings: <see cref="DellListShare"/> of the
    /// panes' viewport, in whole pixels, but never less than <see cref="DellListMinimumLines"/>
    /// lines. The Dell row of the panes grid is sized to its content, so without this cap its ten
    /// rows would push the log panel and the Status region down at a large text size; with it, the
    /// list scrolls inside, and its rows stay in the UI Automation tree. A negative or unknown
    /// (NaN) height counts as zero.
    /// </summary>
    /// <param name="viewportHeight">The height of the panes' scroll viewer, in device-independent pixels.</param>
    /// <param name="lineHeight">The height of one line of the list at its text size, in device-independent pixels.</param>
    /// <returns>The list's maximum height, in device-independent pixels.</returns>
    public static double DellListMaximum(double viewportHeight, double lineHeight)
    {
        double viewport = double.IsNaN(viewportHeight) ? 0 : viewportHeight;
        double line = double.IsNaN(lineHeight) ? 0 : lineHeight;
        return Math.Max(DellListMinimumLines * Math.Max(line, 0), Math.Floor(Math.Max(viewport, 0) * DellListShare));
    }
}

/// <summary>The window's start-up size and its minimum size, in device-independent pixels.</summary>
/// <param name="Width">The width to open at.</param>
/// <param name="Height">The height to open at.</param>
/// <param name="MinWidth">The smallest width.</param>
/// <param name="MinHeight">The smallest height.</param>
public readonly record struct WindowSize(double Width, double Height, double MinWidth, double MinHeight);
