// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Presentation.Accessibility;

/// <summary>
/// The <see cref="IDetailScheduler"/> without a message loop: every refresh runs synchronously
/// inside <see cref="Schedule"/>, so the detail pane follows the selection at once and nothing is
/// ever pending. The default of a shell created without a scheduler, used by the tests that
/// assert on the pane right after a selection change.
/// </summary>
public sealed class ImmediateDetailScheduler : IDetailScheduler
{
    /// <summary>Runs the refresh now.</summary>
    /// <param name="refresh">The refresh to run.</param>
    /// <exception cref="ArgumentNullException"><paramref name="refresh"/> is null.</exception>
    public void Schedule(Action refresh)
    {
        ArgumentNullException.ThrowIfNull(refresh);
        refresh();
    }

    /// <summary>Does nothing: no refresh is ever pending.</summary>
    public void Flush()
    {
    }
}
