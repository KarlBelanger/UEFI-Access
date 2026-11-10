// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Presentation.Diagnostics;

/// <summary>
/// The input trace that is off, the default everywhere: <see cref="IsEnabled"/> is false, and
/// <see cref="Write"/> and <see cref="Redact"/> do nothing. The application uses it unless it was
/// started with <see cref="InputTraceSwitch.Name"/>; then no hook is attached, no file is created
/// and no thread is started.
/// </summary>
public sealed class NullInputTrace : IInputTrace
{
    private NullInputTrace()
    {
    }

    /// <summary>The one instance.</summary>
    public static IInputTrace Instance { get; } = new NullInputTrace();

    /// <inheritdoc />
    public bool IsEnabled => false;

    /// <inheritdoc />
    public void Write(string kind, string details)
    {
    }

    /// <inheritdoc />
    public void Redact(string? value)
    {
    }
}
