// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// Reading helpers for the static checks of the WPF shell (src/UefiAccess.App) that the round-2
/// test classes share: the window markup as XML, its text without comments, the code-behind and
/// the platform sources as text, and the attribute, element and setter lookups. WPF cannot run
/// under xunit on every host, so, like <see cref="WpfShellAccessibilityTests"/>, these checks
/// read the files; the behaviour behind them is unit-tested where it is policy (Presentation).
/// </summary>
internal static class MainWindowMarkup
{
    /// <summary>The WPF presentation namespace.</summary>
    public static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    /// <summary>The XAML language namespace (x:Name, x:Key).</summary>
    public static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>The application's own namespace in the markup (the splitters).</summary>
    public static readonly XNamespace Platform = "clr-namespace:UefiAccess.App.Platform";

    /// <summary>The folder of the WPF application's sources.</summary>
    public static string AppDirectory => Path.Combine(RepositoryPaths.Root, "src", "UefiAccess.App");

    /// <summary>The window element of MainWindow.xaml.</summary>
    public static XElement Load()
    {
        return Load("MainWindow.xaml");
    }

    /// <summary>The window element of a window's markup in the application (MainWindow.xaml, or ConfirmChangeWindow.xaml for the dialog's checks).</summary>
    /// <param name="fileName">The XAML file, relative to src/UefiAccess.App.</param>
    public static XElement Load(string fileName)
    {
        XDocument document = XDocument.Load(Path.Combine(AppDirectory, fileName));
        Assert.NotNull(document.Root);
        Assert.Equal(Wpf + "Window", document.Root!.Name);
        return document.Root!;
    }

    /// <summary>The markup with every XML comment removed, so substring checks see only what compiles.</summary>
    public static string WithoutComments(string path)
    {
        return Regex.Replace(File.ReadAllText(path), @"<!--.*?-->", string.Empty, RegexOptions.Singleline | RegexOptions.CultureInvariant);
    }

    /// <summary>The text of a source file of the application, relative to src/UefiAccess.App.</summary>
    public static string Source(params string[] relative)
    {
        return File.ReadAllText(Path.Combine(new[] { AppDirectory }.Concat(relative).ToArray())).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    /// <summary>The value of an attribute, or an empty string.</summary>
    public static string Attr(XElement element, XName name) => (string?)element.Attribute(name) ?? string.Empty;

    /// <summary>The single element of a type (any namespace) with the given x:Name.</summary>
    public static XElement Named(XElement window, string localName, string name)
    {
        return window.Descendants().Single(element => element.Name.LocalName == localName && Attr(element, Xaml + "Name") == name);
    }

    /// <summary>The style with the given x:Key.</summary>
    public static XElement KeyedStyle(XElement window, string key)
    {
        return window.Descendants(Wpf + "Style").Single(style => Attr(style, Xaml + "Key") == key);
    }

    /// <summary>The implicit (unkeyed) style for a target type.</summary>
    public static XElement ImplicitStyle(XElement window, string targetType)
    {
        return window.Descendants(Wpf + "Style").Single(style => Attr(style, Xaml + "Key").Length == 0 && Attr(style, "TargetType") == targetType);
    }

    /// <summary>The value of the style's own setter (not a trigger's) for a property, or an empty string.</summary>
    public static string SetterValue(XElement style, string property)
    {
        XElement? setter = style.Elements(Wpf + "Setter").FirstOrDefault(candidate => Attr(candidate, "Property") == property);
        return setter is null ? string.Empty : Attr(setter, "Value");
    }

    /// <summary>The value a trigger (or data trigger) sets for a property, or an empty string.</summary>
    public static string TriggerSetterValue(XElement trigger, string property)
    {
        XElement? setter = trigger.Elements(Wpf + "Setter").FirstOrDefault(candidate => Attr(candidate, "Property") == property);
        return setter is null ? string.Empty : Attr(setter, "Value");
    }

    /// <summary>The style's single trigger on a property with a value.</summary>
    public static XElement Trigger(XElement style, string property, string value)
    {
        return style.Descendants(Wpf + "Trigger").Single(trigger => Attr(trigger, "Property") == property && Attr(trigger, "Value") == value);
    }

    /// <summary>The text of one member: from the given signature fragment to the closing brace of the member.</summary>
    public static string Method(string source, string signatureFragment)
    {
        int start = source.IndexOf(signatureFragment, StringComparison.Ordinal);
        Assert.True(start >= 0, "member not found: " + signatureFragment);
        int end = source.IndexOf("\n    }", start, StringComparison.Ordinal);
        Assert.True(end >= 0, "end of member not found: " + signatureFragment);
        return source[start..end];
    }

    /// <summary>A dynamic reference to a SystemColors brush key: the one form of colour the window uses.</summary>
    public static string SystemBrush(string key) => "{DynamicResource {x:Static SystemColors." + key + "}}";
}
