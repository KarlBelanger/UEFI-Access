// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Presentation.Resources;

namespace UefiAccess.Presentation.Accessibility;

/// <summary>
/// The F6 cycle: Toolbar, Filter, List, Detail, Log, since M4 Dell settings, then Status, wrapping
/// in both directions (the seventh region sits after Log, so Shift+F6
/// from Status lands in it). <see cref="Name"/> gives the spoken region name used in the
/// "{0} region" announcement.
/// </summary>
public static class RegionOrder
{
    private static readonly Region[] Order =
    {
        Region.Toolbar,
        Region.Filter,
        Region.List,
        Region.Detail,
        Region.Log,
        Region.Dell,
        Region.Status,
    };

    /// <summary>Every region in F6 order.</summary>
    public static IReadOnlyList<Region> All => Order;

    /// <summary>The region F6 moves to from <paramref name="region"/>; Status wraps to Toolbar.</summary>
    /// <param name="region">The current region.</param>
    /// <returns>The next region.</returns>
    public static Region Next(Region region)
    {
        int index = IndexOf(region);
        return Order[(index + 1) % Order.Length];
    }

    /// <summary>The region Shift+F6 moves to from <paramref name="region"/>; Toolbar wraps to Status.</summary>
    /// <param name="region">The current region.</param>
    /// <returns>The previous region.</returns>
    public static Region Previous(Region region)
    {
        int index = IndexOf(region);
        return Order[(index + Order.Length - 1) % Order.Length];
    }

    /// <summary>The spoken name of a region ("Toolbar", "Filter", "List", "Detail", "Log", "Dell settings", "Status").</summary>
    /// <param name="region">The region.</param>
    /// <returns>The name; an unknown value returns its enum name.</returns>
    public static string Name(Region region)
    {
        return region switch
        {
            Region.Toolbar => Strings.RegionToolbar,
            Region.Filter => Strings.RegionFilter,
            Region.List => Strings.RegionList,
            Region.Detail => Strings.RegionDetail,
            Region.Log => Strings.RegionLog,
            Region.Dell => Strings.RegionDell,
            Region.Status => Strings.RegionStatus,
            _ => region.ToString(),
        };
    }

    private static int IndexOf(Region region)
    {
        int index = Array.IndexOf(Order, region);
        return index < 0 ? 0 : index;
    }
}
