// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Presentation.Accessibility;

/// <summary>
/// Receives focus requests from view models. The WPF window implements it by moving keyboard
/// focus to the matching control; tests use <see cref="RecordingFocusSink"/> and assert on the
/// recorded requests instead of on real focus.
/// </summary>
public interface IFocusSink
{
    /// <summary>Asks the view to move keyboard focus to the target. Never throws.</summary>
    /// <param name="target">Where focus should go; <see cref="FocusTarget.None"/> is ignored.</param>
    void RequestFocus(FocusTarget target);
}
