// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using Serilog;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Presentation.Accessibility;
using UefiAccess.Presentation.Diagnostics;

namespace UefiAccess.App.Platform;

/// <summary>
/// The WPF <see cref="IAnnouncer"/>: writes every announcement into the Status box (the only
/// polite live region of the window) and raises the UI Automation LiveRegionChanged event for
/// it, because WPF does not raise that event for a <see cref="TextBox"/> on its own. The text is
/// pushed through the box's binding when it has one (so the binding to
/// <c>ShellViewModel.StatusText</c> stays alive) and set as the current value otherwise; either
/// way the box shows the text synchronously, before the event is raised. Announcements happen
/// only in direct response to a user action or the completion of one; nothing here runs on a
/// timer. Calls from a non-UI thread are marshalled synchronously to the box's dispatcher.
/// </summary>
/// <remarks>
/// Alpha 1 round 2, finding A2-2: the event is handed to UI Automation directly
/// (<see cref="AutomationInteropProvider.RaiseAutomationEvent"/>) and not through
/// <see cref="AutomationPeer.RaiseAutomationEvent"/>, which drops it unless WPF has been told
/// that a client subscribed to LiveRegionChanged. NVDA 2026.2 subscribes in a way WPF does not
/// see, so with the peer method none of the Status changes made while focus was elsewhere
/// reached it (filter counts, copy confirmations, region names). While the Status box itself has
/// keyboard focus and its text changes, the event is not raised: the screen reader reads the
/// focused box's name change ("Status: &lt;text&gt;"), and the event would make it read the text
/// twice. The same text announced again changes no name, so it is raised then.
/// <para>
/// With the input trace on (--trace-input, Alpha 2 finding A2-7), every announcement is traced as
/// an ANNOUNCE line (the text, whether the Status box had keyboard focus and whether its text
/// changed), followed by UIA-RAISED lines: when LiveRegionChanged is raised, one stamped just
/// before the call ("raising", when the event leaves the application) and one after it with how
/// long the call took; when it is not raised, one that says why. The trace adds nothing that is
/// spoken.
/// </para>
/// <para>
/// LiveRegionChanged carries no text. A UI Automation client reads the Status box's name when it
/// handles the event, and that can be later than the raise: measured on 2 October 2026 (M4-H4.14),
/// after a variable list load, seconds later. A late event is then read with a newer sentence, the
/// one the box holds by then, so a screen reader can say one sentence twice and an earlier one not
/// at all although each was raised once. This is one more reason the Status box stays the single
/// live region of the window and each sentence is raised once: every extra event would be one more
/// late read of whatever the box holds then.
/// </para>
/// </remarks>
public sealed class WpfAnnouncer : IAnnouncer
{
    private readonly TextBox _liveRegion;
    private readonly ProviderLookup _providers;
    private readonly ILogger _logger;
    private readonly IInputTrace _trace;

    /// <summary>Creates the announcer over the Status box.</summary>
    /// <param name="liveRegion">The read-only Status box marked <c>AutomationProperties.LiveSetting="Polite"</c>.</param>
    /// <param name="logger">Optional logger; null disables logging.</param>
    /// <param name="inputTrace">The input trace; null means <see cref="NullInputTrace"/> (off).</param>
    /// <exception cref="ArgumentNullException"><paramref name="liveRegion"/> is null.</exception>
    public WpfAnnouncer(TextBox liveRegion, ILogger? logger = null, IInputTrace? inputTrace = null)
    {
        ArgumentNullException.ThrowIfNull(liveRegion);
        _liveRegion = liveRegion;
        _providers = new ProviderLookup(liveRegion);
        _logger = logger ?? Serilog.Core.Logger.None;
        _trace = inputTrace ?? NullInputTrace.Instance;
    }

    /// <inheritdoc />
    public string Current { get; private set; } = string.Empty;

    /// <inheritdoc />
    public void Announce(string text)
    {
        Current = text ?? string.Empty;
        if (!_liveRegion.Dispatcher.CheckAccess())
        {
            _liveRegion.Dispatcher.Invoke(Apply);
            return;
        }

        Apply();
    }

    private void Apply()
    {
        bool textChanged = true;
        try
        {
            string before = _liveRegion.Text;
            BindingExpression? binding = _liveRegion.GetBindingExpression(TextBox.TextProperty);
            binding?.UpdateTarget();
            if (!string.Equals(_liveRegion.Text, Current, StringComparison.Ordinal))
            {
                // SetCurrentValue keeps a binding on Text alive; a plain assignment would replace it.
                _liveRegion.SetCurrentValue(TextBox.TextProperty, Current);
            }

            textChanged = !string.Equals(_liveRegion.Text, before, StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Updating the Status box failed");
        }

        if (_trace.IsEnabled)
        {
            _trace.Write(
                TraceKind.Announce,
                TraceLine.Quote(Current, TraceLine.RowNameLength) + " (status box focused: " + YesNo(_liveRegion.IsKeyboardFocused) + ", text changed: " + YesNo(textChanged) + ")");
        }

        RaiseLiveRegionChanged(textChanged);
    }

    private void RaiseLiveRegionChanged(bool textChanged)
    {
        try
        {
            // A2-2: the focused box is read through its name change; the event would repeat it.
            // The same text again changes no name, so then the event is what gets it read.
            if ((_liveRegion.IsKeyboardFocused && textChanged) || !AutomationInteropProvider.ClientsAreListening)
            {
                if (_trace.IsEnabled)
                {
                    _trace.Write(
                        TraceKind.UiaRaised,
                        _liveRegion.IsKeyboardFocused && textChanged
                            ? "LiveRegionChanged not raised: the focused Status box is read through its name change"
                            : "LiveRegionChanged not raised: no UI Automation client is listening");
                }

                return;
            }

            AutomationPeer? peer = UIElementAutomationPeer.FromElement(_liveRegion) ?? UIElementAutomationPeer.CreatePeerForElement(_liveRegion);
            IRawElementProviderSimple? provider = peer is null ? null : _providers.ProviderFor(peer);
            if (provider is null)
            {
                if (_trace.IsEnabled)
                {
                    _trace.Write(TraceKind.UiaRaised, "LiveRegionChanged not raised: the Status box has no UI Automation provider");
                }

                return;
            }

            // The trace stamps the raise before the call, when the event leaves the application. A
            // client reads the Status box's name when it handles the event, which can be later than
            // the raise (measured on 2 October 2026: after a list load, seconds later), so a late
            // event is read with a newer sentence; one more reason the box stays the single live
            // region and each sentence is raised once, by this one call.
            long raising = 0;
            if (_trace.IsEnabled)
            {
                raising = Stopwatch.GetTimestamp();
                _trace.Write(TraceKind.UiaRaised, "LiveRegionChanged raising on the Status box");
            }

            // A2-2: straight to UI Automation; AutomationPeer.RaiseAutomationEvent would drop it.
            AutomationInteropProvider.RaiseAutomationEvent(
                AutomationElementIdentifiers.LiveRegionChangedEvent,
                provider,
                new AutomationEventArgs(AutomationElementIdentifiers.LiveRegionChangedEvent));
            if (_trace.IsEnabled)
            {
                _trace.Write(TraceKind.UiaRaised, "LiveRegionChanged raised on the Status box; the call returned after " + TraceLine.Milliseconds(raising, Stopwatch.GetTimestamp()) + " ms");
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Raising LiveRegionChanged failed");
            if (_trace.IsEnabled)
            {
                _trace.Write(TraceKind.UiaRaised, "LiveRegionChanged failed: " + ex.GetType().Name);
            }
        }
    }

    private static string YesNo(bool value) => value ? "yes" : "no";

    /// <summary>
    /// Reaches <c>AutomationPeer.ProviderFromPeer</c>, which WPF offers only to peer classes: the
    /// UI Automation provider of the Status box's own peer (the one screen readers already hold),
    /// the same provider WPF itself raises events for. This peer is never attached to the box.
    /// </summary>
    private sealed class ProviderLookup : TextBoxAutomationPeer
    {
        public ProviderLookup(TextBox owner)
            : base(owner)
        {
        }

        public IRawElementProviderSimple? ProviderFor(AutomationPeer peer) => ProviderFromPeer(peer);
    }
}
