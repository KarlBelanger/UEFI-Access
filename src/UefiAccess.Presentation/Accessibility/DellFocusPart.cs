// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Presentation.Accessibility;

/// <summary>
/// The part of the Dell settings region that has keyboard focus, as the window tells the shell
/// (<c>ShellViewModel.OnDellFocus</c>): Copy value (Alt+Y) copies the
/// selected Dell row's sentence, or the state line's text, while it is not <see cref="None"/>, and
/// never a firmware variable's value there.
/// </summary>
public enum DellFocusPart
{
    /// <summary>Keyboard focus is outside the Dell settings region.</summary>
    None,

    /// <summary>The Read Dell settings button.</summary>
    Button,

    /// <summary>The state line, the box that says when and where the Dell values were loaded.</summary>
    SourceLine,

    /// <summary>The list of Dell BIOS settings.</summary>
    List,
}
