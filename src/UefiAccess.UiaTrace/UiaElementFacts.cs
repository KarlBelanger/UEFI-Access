// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Windows.Automation;
using UefiAccess.UiaTrace.Recording;

namespace UefiAccess.UiaTrace;

/// <summary>
/// Reads the <see cref="ElementFacts"/> of an event's element from the cache the event arrived
/// with (<see cref="AutomationElement.Cached"/>), and from nothing else. The recorder subscribes
/// under a cache request with <see cref="AutomationElementMode.None"/>, so an event's element has
/// no live reference at all: no read here can call into the UefiAccess window and wait on its UI
/// thread. Each property is read on its own, so one that was not cached costs that property only.
/// Never throws.
/// </summary>
internal static class UiaElementFacts
{
    private const string ControlTypePrefix = "ControlType.";

    /// <summary>The facts of <paramref name="element"/>, from its cache.</summary>
    /// <param name="element">The event's sender; null gives <see cref="ElementFacts.Unavailable"/>.</param>
    /// <returns>The facts; <see cref="ElementFacts.Unavailable"/> when the element was gone.</returns>
    public static ElementFacts FromCached(AutomationElement? element)
    {
        if (element is null)
        {
            return ElementFacts.Unavailable;
        }

        try
        {
            AutomationElement.AutomationElementInformation cached = element.Cached;
            string? controlType = Read(() => cached.ControlType?.ProgrammaticName);
            if (controlType is not null && controlType.StartsWith(ControlTypePrefix, StringComparison.Ordinal))
            {
                controlType = controlType[ControlTypePrefix.Length..];
            }

            string? automationId = Read(() => cached.AutomationId);
            string? name = Read(() => cached.Name);
            int? processId = Read<int?>(() => cached.ProcessId);
            string? frameworkId = Read(() => cached.FrameworkId);
            return new ElementFacts(controlType, automationId, name, processId, true) { FrameworkId = string.IsNullOrEmpty(frameworkId) ? null : frameworkId };
        }
        catch (ElementNotAvailableException)
        {
            return ElementFacts.Unavailable;
        }
        catch (InvalidOperationException)
        {
            return ElementFacts.Unavailable;
        }
    }

    /// <summary>One cached property; default when it was not cached or the element is gone.</summary>
    private static T? Read<T>(Func<T?> read)
    {
        try
        {
            return read();
        }
        catch (ElementNotAvailableException)
        {
            return default;
        }
        catch (InvalidOperationException)
        {
            // Not in the cache (the cache request names it, so this means the provider gave none).
            return default;
        }
    }
}
