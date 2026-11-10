// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Presentation.ViewModels;

namespace UefiAccess.App.Platform;

/// <summary>
/// How the input trace (<see cref="WpfInputTraceHooks"/>) names an element: its UI Automation
/// control type, its WPF type, its AutomationId (or x:Name, which WPF exposes as the AutomationId
/// when none is set), the variable name of a list row or the label of a group header, and its
/// accessible name. Only what a screen reader already receives is read: the AutomationId and Name
/// attached properties, and the control type of the element's automation peer when one exists
/// (an element no UI Automation client has asked for has none, shown as "?"; a peer is never
/// created here). No value of a text box or a detail field is read. A row's accessible name is
/// cut at <see cref="TraceLine.RowNameLength"/> characters, any other at
/// <see cref="TraceLine.ShortNameLength"/>. UI thread only; never throws for an element of the
/// window.
/// </summary>
internal static class WpfTraceText
{
    /// <summary>How far <see cref="DescribeTarget"/> walks up from the element under the mouse.</summary>
    private const int MaxTargetDepth = 32;

    /// <summary>
    /// "&lt;control type&gt;/&lt;WPF type&gt; id=&lt;AutomationId, x:Name or -&gt;", then " row=&lt;variable
    /// name&gt;" for a list row or " header=&lt;group label&gt;" for a group header, then
    /// " name=&lt;quoted accessible name&gt;" when <paramref name="withName"/> is true and there is one;
    /// "(nothing)" for null.
    /// </summary>
    /// <param name="element">The element, typically the focused one.</param>
    /// <param name="withName">True to add the accessible name.</param>
    /// <returns>The description.</returns>
    public static string Describe(object? element, bool withName = true)
    {
        if (element is null)
        {
            return "(nothing)";
        }

        if (element is not DependencyObject target)
        {
            return element.GetType().Name;
        }

        string automationId = AutomationProperties.GetAutomationId(target);
        if (string.IsNullOrEmpty(automationId) && target is FrameworkElement { Name.Length: > 0 } named)
        {
            automationId = named.Name;
        }

        string described = ControlType(target) + "/" + target.GetType().Name + " id=" + (string.IsNullOrEmpty(automationId) ? "-" : TraceLine.Token(automationId));
        bool isRow = false;
        if (target is ListViewItem { DataContext: VariableItemViewModel row })
        {
            described += " row=" + TraceLine.Token(row.Name);
            isRow = true;
        }
        else if (target is ToggleButton { DataContext: VariableGroupViewModel group })
        {
            described += " header=" + TraceLine.Token(group.Label);
        }

        if (withName)
        {
            string name = AutomationProperties.GetName(target);
            if (!string.IsNullOrEmpty(name))
            {
                described += " name=" + TraceLine.Quote(name, isRow ? TraceLine.RowNameLength : TraceLine.ShortNameLength);
            }
        }

        return described;
    }

    /// <summary>
    /// The element under the mouse described as the control it belongs to: the nearest
    /// <see cref="Control"/> up the tree (the row of a cell's text, the button of its label), or the
    /// element itself when there is none.
    /// </summary>
    /// <param name="element">The element directly under the mouse.</param>
    /// <returns>The description, with the accessible name.</returns>
    public static string DescribeTarget(object? element)
    {
        DependencyObject? current = element as DependencyObject;
        for (int depth = 0; current is not null && depth < MaxTargetDepth; depth++)
        {
            if (current is Control)
            {
                return Describe(current);
            }

            current = current is Visual or Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }

        return Describe(element);
    }

    /// <summary>The control type of the element's existing automation peer, or "?" when it has none (no client asked for it yet).</summary>
    private static string ControlType(DependencyObject target)
    {
        if (target is not UIElement element)
        {
            return "?";
        }

        try
        {
            AutomationPeer? peer = UIElementAutomationPeer.FromElement(element);
            return peer is null ? "?" : peer.GetAutomationControlType().ToString();
        }
        catch (Exception)
        {
            return "?";
        }
    }
}
