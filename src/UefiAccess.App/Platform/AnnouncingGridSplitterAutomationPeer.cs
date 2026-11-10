// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using UefiAccess.Presentation.Accessibility;

namespace UefiAccess.App.Platform;

/// <summary>
/// The automation peer of <see cref="AnnouncingGridSplitter"/> (Alpha 1 round 1, finding A1-5;
/// Alpha 2 finding A2-8): the stock <see cref="GridSplitterAutomationPeer"/> (control type Thumb,
/// the Transform pattern that moves the splitter) plus the Value pattern, read-only. Value is the
/// splitter's position as a sentence that names both panes (<see cref="SplitterPosition.ValueText"/>):
/// "List 63 percent of the width, details 37 percent" for the list/detail splitter, "Log panel 30
/// percent of the height, list and details 70 percent" for the log splitter; NVDA and Narrator read
/// it on arrival after the name and "thumb". alpha1.2 exposed the list's or the log panel's share as
/// a bare range value, which NVDA 2026.2 spoke as the number alone ("63", the tester's A2-6) and Narrator
/// did not speak after a press (the developer's A2-5); there is no range value any more. The value
/// cannot be set (IsReadOnly; SetValue throws <see cref="InvalidOperationException"/>): the arrow
/// keys, a drag and the stock Transform pattern move the splitter. The peer raises no property
/// change for the value: after a press or a drag the shell says the sentence through Status, the
/// one live region both screen readers speak, so it is heard once. The pattern is offered only
/// while the splitter sits between two definitions of a Grid.
/// </summary>
public sealed class AnnouncingGridSplitterAutomationPeer : GridSplitterAutomationPeer, IValueProvider
{
    /// <summary>Creates the peer for a splitter.</summary>
    /// <param name="owner">The splitter.</param>
    public AnnouncingGridSplitterAutomationPeer(AnnouncingGridSplitter owner)
        : base(owner)
    {
    }

    private AnnouncingGridSplitter Splitter => (AnnouncingGridSplitter)Owner;

    /// <summary>The Value pattern (this peer) when the splitter has a range; every other pattern is the stock peer's.</summary>
    /// <param name="patternInterface">The pattern asked for.</param>
    /// <returns>The provider, or null when the pattern is not supported.</returns>
    public override object? GetPattern(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.Value)
        {
            return Splitter.ValueText is null ? null : this;
        }

        return base.GetPattern(patternInterface);
    }

    string IValueProvider.Value => Splitter.ValueText ?? string.Empty;

    bool IValueProvider.IsReadOnly => true;

    void IValueProvider.SetValue(string value)
    {
        throw new InvalidOperationException();
    }
}
