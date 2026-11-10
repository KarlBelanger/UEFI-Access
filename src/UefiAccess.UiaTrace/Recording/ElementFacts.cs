// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.UiaTrace.Recording;

/// <summary>
/// What the recorder knows about the element an event came from, taken from the UI Automation
/// cache the event arrived with and nothing else: the recorder never asks the UefiAccess window for
/// a live property, so a handler cannot wait on the window's UI thread. This type is pure (no UI
/// Automation types) so the test project, which must not load the Windows Desktop runtime, compiles
/// it as a linked file.
/// </summary>
/// <param name="ControlType">The control type's programmatic name without its "ControlType." prefix, for example "ListItem"; null when it was not cached.</param>
/// <param name="AutomationId">The automation id; null or empty when the element has none (WPF uses the x:Name when no id is set).</param>
/// <param name="Name">The accessible name, as a screen reader receives it; null when it was not cached.</param>
/// <param name="ProcessId">The process that owns the element; null when it was not cached.</param>
/// <param name="Available">False when the element was gone before its cache could be read.</param>
internal sealed record ElementFacts(string? ControlType, string? AutomationId, string? Name, int? ProcessId, bool Available)
{
    /// <summary>The facts of an element that was no longer available when the event arrived.</summary>
    public static ElementFacts Unavailable { get; } = new(null, null, null, null, false);

    /// <summary>The UI framework that provides the element, for example "WPF", "Win32" or "DirectUI"; null when it was not cached.</summary>
    public string? FrameworkId { get; init; }
}
