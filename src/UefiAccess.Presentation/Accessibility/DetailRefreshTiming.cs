// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Presentation.Accessibility;

/// <summary>
/// The one timing of the application (Alpha 2 finding A2-7, the tester's A2-5, "A1-6 is not fixed"):
/// how long the detail pane's rebuild waits for the variable list to be quiet. The tester's separate
/// quick arrow presses (150 to 250 ms apart) each cost a rebuild of the pane right after the key
/// came up, which kept the UI thread busy while NVDA was still fetching or speaking the row's
/// name, so the name was cut off. The WPF scheduler now posts the rebuild only after this quiet
/// period has passed with no key or mouse button going down in the variable list
/// (<see cref="QuietPeriodPoster"/>); everything the person asks for (Enter, Space, F6, Tab or a
/// command into the pane, an Alt key and the Change group's access keys and commands, Copy value,
/// Export) runs the pending rebuild at once and never waits.
/// </summary>
public static class DetailRefreshTiming
{
    /// <summary>
    /// 300 milliseconds. The tester suggested "say 100ms"; 300 is chosen because those presses are 150 to
    /// 250 ms apart, so a quiet period longer than the longest gap puts no rebuild between two
    /// presses of a burst, and one rebuild follows the last. The one place the value is set, read
    /// by the WPF scheduler's one timer.
    /// </summary>
    public static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(300);
}
