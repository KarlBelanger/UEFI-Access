// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Presentation.Diagnostics;

/// <summary>
/// The input trace of the application (Alpha 2 finding A2-7, the continued A1-6 issue): with
/// <see cref="InputTraceSwitch.Name"/> on its command line, UefiAccess.App writes the key presses,
/// focus moves, selection changes, every step of the detail pane's scheduling and the UI Automation
/// events it raises itself into one file (<see cref="InputTraceFile"/>), in the order they happen,
/// so the tester can see whether everything fired while moving through the list, and when. Off by
/// default: <see cref="NullInputTrace"/>, whose <see cref="IsEnabled"/> is false. A caller builds
/// the details of a line only when <see cref="IsEnabled"/> is true, so a trace that is off costs
/// one property read. The trace never speaks, moves no focus and changes nothing the application
/// does.
/// </summary>
public interface IInputTrace
{
    /// <summary>True when lines are written; false for <see cref="NullInputTrace"/>.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Writes one event line, stamped now on the calling thread (the QPC value, the local time and
    /// the thread id; Core.Diagnostics.TraceLine gives the format). Never throws.
    /// </summary>
    /// <param name="kind">The event kind (Core.Diagnostics.TraceKind).</param>
    /// <param name="details">The details: names, steps and counts, never a firmware value beyond a row's accessible name.</param>
    void Write(string kind, string details);

    /// <summary>Registers a value the trace replaces with "[service tag]" in every line (the service tag of a load's identity). Never throws.</summary>
    /// <param name="value">The value; null and blank values are ignored.</param>
    void Redact(string? value);
}
