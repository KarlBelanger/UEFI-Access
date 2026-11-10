// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Tests.Safety;
using UefiAccess.Core.Variables;
using UefiAccess.Presentation.Resources;
using UefiAccess.Presentation.ViewModels;
using Xunit;
using static UefiAccess.Core.Tests.Fixtures.LoadOptionFixtures;
using static UefiAccess.Core.Tests.MainWindowMarkup;
using static UefiAccess.Core.Tests.Safety.SafetyFixtures;

namespace UefiAccess.Core.Tests;

/// <summary>
/// Static checks of the Confirm change dialog (src/UefiAccess.App/ConfirmChangeWindow.xaml). WPF cannot run under xunit on every host, so, like
/// <see cref="WpfShellAccessibilityTests"/>, these tests read the markup and the code-behind and
/// apply the main window's rules to the dialog: an automation name on every control from Strings or
/// a binding, help text on every input, LabeledBy only in addition to a name, stock controls without
/// template overrides, colours only through SystemColors and the system message font, no label that
/// implies a change, no live region and nothing on a timer. The dialog's own rules on top: a stock
/// window titled Confirm change, no default button, Cancel the cancel button, every box read-only
/// and named by its label alone with the same help text, tab indices 1 to 11 in reading order,
/// focus on What will change at open, the arrival rule of the main window's value boxes, Confirm
/// change's two names and access key Alt+F, and no check box for a removal of the one-time boot
/// choice or a restore that removes it. The texts are built through the Presentation view model
/// from real plans of the write pipeline over the Safety test fakes (never the real writer), with
/// the longest fixture the sample baseline holds: a three-entry order whose moved entry has the
/// longest description in tools/sample-baseline; every single-line box stays under 200 characters.
/// </summary>
public sealed class ConfirmChangeWindowTests
{
    private const string DialogFile = "ConfirmChangeWindow.xaml";

    /// <summary>The layout-only scroll viewer around the boxes: not focusable and unnamed, like the main window's PanesScroller.</summary>
    private const string LayoutScroller = "BoxesScroller";

    /// <summary>The eight read-only boxes in tab order: element name, Strings name constant, the view model's text, multi-line.</summary>
    private static readonly (string Element, string Name, string Text, bool Multiline)[] Boxes =
    {
        ("WhatWillChangeBox", nameof(Strings.WhatWillChangeName), nameof(ConfirmChangeViewModel.WhatWillChangeText), false),
        ("BeforeBox", nameof(Strings.BeforeName), nameof(ConfirmChangeViewModel.BeforeText), true),
        ("AfterBox", nameof(Strings.AfterName), nameof(ConfirmChangeViewModel.AfterText), true),
        ("BitLockerStateBox", nameof(Strings.BitLockerStateName), nameof(ConfirmChangeViewModel.BitLockerStateText), false),
        ("RiskBox", nameof(Strings.RiskName), nameof(ConfirmChangeViewModel.RiskText), false),
        ("RecoveryKeyBox", nameof(Strings.RecoveryKeyName), nameof(ConfirmChangeViewModel.RecoveryKeyText), false),
        ("WarningsBox", nameof(Strings.WarningsName), nameof(ConfirmChangeViewModel.WarningsText), true),
        ("SnapshotFolderBox", nameof(Strings.SnapshotFolderName), nameof(ConfirmChangeViewModel.SnapshotFolderText), false),
    };

    /// <summary>Elements that must carry an automation name.</summary>
    private static readonly string[] NamedElementTypes = { "Window", "GroupBox", "Button", "TextBox", "CheckBox", "TextBlock", "ScrollViewer" };

    /// <summary>Interactive elements that must carry help text and a tab index.</summary>
    private static readonly string[] InputElementTypes = { "Button", "TextBox", "CheckBox" };

    [Fact]
    public void TheDialogIsAStockWindowTitledConfirmChange_CentredOnItsOwner()
    {
        XElement dialog = Load(DialogFile);
        Assert.Equal("UefiAccess.App.ConfirmChangeWindow", Attr(dialog, Xaml + "Class"));
        Assert.Equal("{x:Static res:Strings.ConfirmChangeTitle}", Attr(dialog, "Title"));
        Assert.Equal("{x:Static res:Strings.ConfirmChangeTitle}", Attr(dialog, "AutomationProperties.Name"));
        Assert.Equal("Confirm change", Strings.ConfirmChangeTitle);
        Assert.Equal("CenterOwner", Attr(dialog, "WindowStartupLocation"));
        Assert.Equal("False", Attr(dialog, "ShowInTaskbar"));
        Assert.Equal("Cycle", Attr(dialog, "KeyboardNavigation.TabNavigation"));
        Assert.Equal("OnLoaded", Attr(dialog, "Loaded"));

        // Modal to the main window: the platform sets the owner and shows it as a dialog.
        string platform = Source("Platform", "WpfPlatformInteractions.cs");
        Assert.Contains("dialog.Owner = owner;", platform, StringComparison.Ordinal);
        Assert.Contains("dialog.ShowDialog()", platform, StringComparison.Ordinal);
        Assert.Contains("IsLoaded", platform, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryControlIsNamed_EveryInputHasHelpText_AndLabeledByIsOnlyInAddition()
    {
        XElement dialog = Load(DialogFile);
        List<XElement> controls = dialog.DescendantsAndSelf()
            .Where(element => NamedElementTypes.Contains(element.Name.LocalName, StringComparer.Ordinal) && Attr(element, Xaml + "Name") != LayoutScroller)
            .ToList();
        Assert.True(controls.Count >= 20, "expected the full dialog; found only " + controls.Count + " named element candidates");
        List<string> unnamed = controls.Where(element => string.IsNullOrWhiteSpace(Attr(element, "AutomationProperties.Name"))).Select(Describe).ToList();
        Assert.True(unnamed.Count == 0, "Elements without AutomationProperties.Name:" + Environment.NewLine + string.Join(Environment.NewLine, unnamed));

        List<string> withoutHelp = Inputs(dialog).Where(element => string.IsNullOrWhiteSpace(Attr(element, "AutomationProperties.HelpText"))).Select(Describe).ToList();
        Assert.True(withoutHelp.Count == 0, "Inputs without AutomationProperties.HelpText:" + Environment.NewLine + string.Join(Environment.NewLine, withoutHelp));

        // The layout scroll viewer is layout only: no name, no focus, like the main window's.
        XElement scroller = Named(dialog, "ScrollViewer", LayoutScroller);
        Assert.Equal("False", Attr(scroller, "Focusable"));
        Assert.Equal(string.Empty, Attr(scroller, "AutomationProperties.Name"));

        List<XElement> labelled = dialog.Descendants().Where(element => element.Attribute("AutomationProperties.LabeledBy") is not null).ToList();
        Assert.Equal(Boxes.Length, labelled.Count);
        foreach (XElement element in labelled)
        {
            Assert.False(string.IsNullOrWhiteSpace(Attr(element, "AutomationProperties.Name")), Describe(element) + " has LabeledBy without an AutomationProperties.Name");
            Match reference = Regex.Match(Attr(element, "AutomationProperties.LabeledBy"), @"ElementName\s*=\s*(\w+)", RegexOptions.CultureInvariant);
            Assert.True(reference.Success, Describe(element) + " is labelled by something other than a named element");
            XElement label = Named(dialog, "TextBlock", reference.Groups[1].Value);
            Assert.Equal(Attr(element, "AutomationProperties.Name"), Attr(label, "AutomationProperties.Name"));
            Assert.Equal(Attr(element, "AutomationProperties.Name"), Attr(label, "Text"));
        }
    }

    /// <summary>
    /// Every box is a read-only TextBox named by its label alone (the box speaks its value through
    /// the Value pattern, so a name that carried the value would be heard twice), with the help text
    /// "Escape cancels; nothing is written." (since A2-6 without "Read-only.", which the screen
    /// reader says from the box's state), in the order of section 5.5; What will
    /// change, BitLocker state, Risk, Recovery key and Snapshot folder are single-line boxes, Before,
    /// After and Warnings the three multi-line ones.
    /// </summary>
    [Fact]
    public void EveryBoxIsReadOnly_NamedByItsLabelAlone_InTheOrderOfTheDesign()
    {
        XElement dialog = Load(DialogFile);
        List<XElement> boxes = dialog.Descendants(Wpf + "TextBox").ToList();
        Assert.Equal(Boxes.Select(box => box.Element), boxes.Select(box => Attr(box, Xaml + "Name")));
        Assert.Equal("Escape cancels; nothing is written.", Strings.ConfirmBoxHelp);

        XElement singleLine = KeyedStyle(dialog, "SingleLineBoxStyle");
        XElement multiLine = KeyedStyle(dialog, "MultiLineBoxStyle");
        foreach (XElement style in new[] { singleLine, multiLine })
        {
            Assert.Equal("TextBox", Attr(style, "TargetType"));
            Assert.Equal("{StaticResource {x:Type TextBox}}", Attr(style, "BasedOn"));
            Assert.Equal("True", SetterValue(style, "IsReadOnly"));
            Assert.Equal("True", SetterValue(style, "IsReadOnlyCaretVisible"));
        }

        Assert.Equal("Wrap", SetterValue(singleLine, "TextWrapping"));
        Assert.Equal("Disabled", SetterValue(singleLine, "HorizontalScrollBarVisibility"));
        Assert.Equal(string.Empty, SetterValue(singleLine, "AcceptsReturn"));
        Assert.Equal("NoWrap", SetterValue(multiLine, "TextWrapping"));
        Assert.Equal("Auto", SetterValue(multiLine, "HorizontalScrollBarVisibility"));
        Assert.Equal("True", SetterValue(multiLine, "AcceptsReturn"));

        for (int index = 0; index < Boxes.Length; index++)
        {
            (string element, string name, string text, bool multiline) = Boxes[index];
            XElement box = boxes[index];
            Assert.Equal("{x:Static res:Strings." + name + "}", Attr(box, "AutomationProperties.Name"));
            Assert.Equal("{x:Static res:Strings.ConfirmBoxHelp}", Attr(box, "AutomationProperties.HelpText"));
            Assert.Equal("{Binding " + text + ", Mode=OneWay}", Attr(box, "Text"));
            Assert.Equal(multiline ? "{StaticResource MultiLineBoxStyle}" : "{StaticResource SingleLineBoxStyle}", Attr(box, "Style"));
            foreach (string own in new[] { "IsReadOnly", "AcceptsReturn", "TextWrapping", "Padding", "BorderThickness", "BorderBrush" })
            {
                Assert.True(string.IsNullOrEmpty(Attr(box, own)), element + " sets " + own + " itself");
            }

            Assert.Equal(index + 1, TabIndexOf(box));
        }

        Assert.Equal(new[] { "What will change", "Before", "After", "BitLocker state", "Risk", "Recovery key", "Warnings", "Snapshot folder" }, Boxes.Select(box => StringConstant(box.Name)));
    }

    [Fact]
    public void TabIndicesAreOneToElevenInReadingOrder_AndFocusStartsOnWhatWillChange()
    {
        XElement dialog = Load(DialogFile);
        List<XElement> inputs = Inputs(dialog).ToList();
        Assert.All(inputs, input => Assert.NotNull(TabIndexOf(input)));
        Assert.Equal(Enumerable.Range(1, 11), inputs.Select(input => TabIndexOf(input) ?? 0));
        List<int> assigned = dialog.Descendants()
            .SelectMany(element => element.Attributes())
            .Where(attribute => attribute.Name.LocalName is "TabIndex" or "KeyboardNavigation.TabIndex")
            .Select(attribute => int.Parse(attribute.Value, CultureInfo.InvariantCulture))
            .ToList();
        Assert.Equal(Enumerable.Range(1, 11), assigned);

        Assert.Equal(9, TabIndexOf(Named(dialog, "CheckBox", "SuspendCheckBox")));
        Assert.Equal(10, TabIndexOf(Named(dialog, "Button", "ConfirmChangeButton")));
        Assert.Equal(11, TabIndexOf(Named(dialog, "Button", "CancelButton")));

        string code = Source("ConfirmChangeWindow.xaml.cs");
        Assert.Contains("WhatWillChangeBox.Focus();", Method(code, "private void OnLoaded("), StringComparison.Ordinal);
    }

    /// <summary>
    /// No default button, so Enter in a box confirms nothing; Cancel is the cancel button (Escape
    /// and the close button cancel too) and reports Escape as its AcceleratorKey; Confirm change has the access key Alt+F, reported as
    /// "Alt+F" in both UI Automation shortcut properties, its name bound to the view model's
    /// two-name property, and its click only closes the dialog with a true result.
    /// </summary>
    [Fact]
    public void NoDefaultButton_CancelIsTheCancelButton_AndConfirmChangeHasAltF()
    {
        XElement dialog = Load(DialogFile);
        Assert.DoesNotContain(dialog.DescendantsAndSelf(), element => element.Attribute("IsDefault") is not null);
        Assert.DoesNotContain("IsDefault", WithoutComments(Path.Combine(AppDirectory, DialogFile)), StringComparison.Ordinal);
        Assert.DoesNotContain("IsDefault", Source("ConfirmChangeWindow.xaml.cs"), StringComparison.Ordinal);

        XElement cancel = Named(dialog, "Button", "CancelButton");
        Assert.Equal("True", Attr(cancel, "IsCancel"));
        Assert.Equal("{x:Static res:Strings.CancelName}", Attr(cancel, "Content"));
        Assert.Equal("{x:Static res:Strings.CancelName}", Attr(cancel, "AutomationProperties.Name"));
        Assert.Equal("{x:Static res:Strings.CancelHelp}", Attr(cancel, "AutomationProperties.HelpText"));

        // Cancel reports its shortcut as every other button does (NVDA speaks the AcceleratorKey as
        // the shortcut); it has no access key of its own, so AccessKey stays unset.
        Assert.Equal("{x:Static res:Strings.CancelAcceleratorKey}", Attr(cancel, "AutomationProperties.AcceleratorKey"));
        Assert.Equal("Escape", Strings.CancelAcceleratorKey);
        Assert.Equal(string.Empty, Attr(cancel, "AutomationProperties.AccessKey"));
        Assert.Contains("Escape", Strings.CancelHelp, StringComparison.Ordinal);
        Assert.Equal(string.Empty, Attr(cancel, "Click"));
        Assert.Single(dialog.Descendants(), element => element.Attribute("IsCancel") is not null);

        XElement confirm = Named(dialog, "Button", "ConfirmChangeButton");
        Assert.Equal("{x:Static res:Strings.ConfirmChangeLabel}", Attr(confirm, "Content"));
        Assert.Equal("{Binding ConfirmButtonName, Mode=OneWay, FallbackValue={x:Static res:Strings.ConfirmChangeName}}", Attr(confirm, "AutomationProperties.Name"));
        Assert.Equal("{x:Static res:Strings.ConfirmChangeHelp}", Attr(confirm, "AutomationProperties.HelpText"));
        Assert.Equal("{x:Static res:Strings.ConfirmChangeAccessKey}", Attr(confirm, "AutomationProperties.AccessKey"));
        Assert.Equal("{x:Static res:Strings.ConfirmChangeAccessKey}", Attr(confirm, "AutomationProperties.AcceleratorKey"));
        Match key = WpfShellAccessibilityTests.AccessKey.Match(Strings.ConfirmChangeLabel);
        Assert.True(key.Success, "Confirm change has no access key marker");
        Assert.Equal(Strings.ConfirmChangeAccessKey, "Alt+" + char.ToUpperInvariant(key.Groups[1].Value[0]));
        Assert.Equal("Alt+F", Strings.ConfirmChangeAccessKey);
        Assert.Equal(Strings.ConfirmChangeName, Strings.ConfirmChangeLabel.Replace("_", string.Empty, StringComparison.Ordinal));
        Assert.Contains("Alt+F", Strings.ConfirmChangeHelp, StringComparison.Ordinal);
        Assert.Equal(string.Empty, Attr(confirm, "IsCancel"));

        // The only access key in the dialog is Alt+F; the check box and Cancel have none.
        List<string> markers = dialog.Descendants()
            .Select(element => WpfShellAccessibilityTests.ResolveStrings(Attr(element, "Content")))
            .Where(content => WpfShellAccessibilityTests.AccessKey.IsMatch(content))
            .ToList();
        Assert.Equal(new[] { Strings.ConfirmChangeLabel }, markers);

        string code = Source("ConfirmChangeWindow.xaml.cs");
        Assert.Equal("OnConfirmChangeClick", Attr(confirm, "Click"));
        string click = Method(code, "private void OnConfirmChangeClick(");
        Assert.Contains("DialogResult = true;", click, StringComparison.Ordinal);
        Assert.DoesNotContain("Confirm(", click, StringComparison.Ordinal);
    }

    /// <summary>
    /// The check box: named and labelled by the Strings constant, its help text saying what the tick
    /// does, bound two-way to the choice, and collapsed (so no tab stop) unless the checks offer the
    /// suspend, which they never do for the removal of the one-time boot choice or a restore that
    /// removes it.
    /// </summary>
    [Fact]
    public void TheCheckBoxIsTheSuspendChoice_ShownOnlyWhenTheChecksOfferIt()
    {
        XElement dialog = Load(DialogFile);
        XElement box = Named(dialog, "CheckBox", "SuspendCheckBox");
        Assert.Equal("{x:Static res:Strings.SuspendCheckBoxName}", Attr(box, "Content"));
        Assert.Equal("{x:Static res:Strings.SuspendCheckBoxName}", Attr(box, "AutomationProperties.Name"));
        Assert.Equal("{x:Static res:Strings.SuspendCheckBoxHelp}", Attr(box, "AutomationProperties.HelpText"));
        Assert.Equal("{Binding SuspendChosen, Mode=TwoWay}", Attr(box, "IsChecked"));
        Assert.Equal("{StaticResource SuspendCheckBoxStyle}", Attr(box, "Style"));
        Assert.Equal(string.Empty, Attr(box, "Visibility"));

        XElement style = KeyedStyle(dialog, "SuspendCheckBoxStyle");
        Assert.Equal("Collapsed", SetterValue(style, "Visibility"));
        XElement offered = Assert.Single(style.Descendants(Wpf + "DataTrigger"));
        Assert.Equal("{Binding SuspendOffered, Mode=OneWay}", Attr(offered, "Binding"));
        Assert.Equal("True", Attr(offered, "Value"));
        Assert.Equal("Visible", TriggerSetterValue(offered, "Visibility"));
        Assert.Equal("Suspend BitLocker for one restart", Strings.SuspendCheckBoxName);
        Assert.True(Strings.SuspendCheckBoxHelp.Length < 200, Strings.SuspendCheckBoxHelp);
    }

    [Fact]
    public void StockControlsOnly_SystemColoursAndFontOnly_NoLiveRegion_NoTimer()
    {
        string path = Path.Combine(AppDirectory, DialogFile);
        string markup = WithoutComments(path);
        foreach (string forbidden in new[] { "ControlTemplate", "ContentPresenter", "FocusVisualStyle", "Property=\"Template\"", "DispatcherTimer", "LiveSetting", "Assertive", "SolidColorBrush", "GradientBrush", "LandmarkType", "RangeValue" })
        {
            Assert.DoesNotContain(forbidden, markup, StringComparison.Ordinal);
        }

        Assert.DoesNotMatch(WpfShellAccessibilityTests.BareTemplateAttribute, markup);
        Assert.DoesNotMatch(WpfShellAccessibilityTests.HexColour, markup);

        XElement dialog = Load(DialogFile);
        foreach (XElement element in dialog.DescendantsAndSelf().Where(element => !element.Name.LocalName.Contains('.', StringComparison.Ordinal)))
        {
            Assert.Contains(element.Name.LocalName, WpfShellAccessibilityTests.AllowedElementTypes);
            Assert.Equal(Wpf, element.Name.Namespace);
            Assert.NotEqual("ItemsControl", element.Name.LocalName);
        }

        // Colours: every brush a dynamic SystemColors brush key, in an attribute or a setter.
        List<string> colours = new();
        int dynamicBrushes = 0;
        foreach (XElement element in dialog.DescendantsAndSelf())
        {
            foreach (XAttribute attribute in element.Attributes().Where(attribute => WpfShellAccessibilityTests.IsBrushProperty(attribute.Name.LocalName)))
            {
                dynamicBrushes += CheckBrush(Describe(element) + " " + attribute.Name.LocalName, attribute.Value, colours);
            }

            if (element.Name == Wpf + "Setter" && WpfShellAccessibilityTests.IsBrushProperty(Attr(element, "Property")))
            {
                dynamicBrushes += CheckBrush("Setter " + Attr(element, "Property"), Attr(element, "Value"), colours);
            }
        }

        Assert.True(colours.Count == 0, "Colours outside dynamic SystemColors brush keys:" + Environment.NewLine + string.Join(Environment.NewLine, colours));
        Assert.True(dynamicBrushes >= 4, "expected the dialog's system-colour brushes; found " + dynamicBrushes);
        Assert.Equal(SystemBrush("ControlBrushKey"), Attr(dialog, "Background"));
        Assert.Equal(SystemBrush("ControlTextBrushKey"), Attr(dialog, "Foreground"));

        // Font: the system message font, which the Windows text size scales; no size of its own.
        Assert.Equal("{DynamicResource {x:Static SystemFonts.MessageFontSizeKey}}", Attr(dialog, "FontSize"));
        Assert.DoesNotContain(dialog.Descendants(), element => element.Attribute("FontSize") is not null || element.Attribute("FontFamily") is not null);
        Assert.DoesNotContain(dialog.Descendants(Wpf + "Setter"), setter => Attr(setter, "Property") is "FontSize" or "FontFamily");

        // Focus is shown by style triggers from the first moment: the buttons' two-pixel border,
        // the boxes' three-pixel border, the check box's two-pixel border.
        XElement buttons = KeyedStyle(dialog, "DialogButtonStyle");
        XElement focusedButton = Trigger(buttons, "IsKeyboardFocused", "True");
        Assert.Equal("2", TriggerSetterValue(focusedButton, "BorderThickness"));
        Assert.Equal(SystemBrush("ControlTextBrushKey"), TriggerSetterValue(focusedButton, "BorderBrush"));
        Assert.All(dialog.Descendants(Wpf + "Button"), button => Assert.Equal("{StaticResource DialogButtonStyle}", Attr(button, "Style")));
        Assert.Equal("3", TriggerSetterValue(Trigger(ImplicitStyle(dialog, "TextBox"), "IsKeyboardFocused", "True"), "BorderThickness"));
        XElement focusedCheckBox = Trigger(KeyedStyle(dialog, "SuspendCheckBoxStyle"), "IsKeyboardFocused", "True");
        Assert.Equal("2", TriggerSetterValue(focusedCheckBox, "BorderThickness"));
        Assert.Equal(SystemBrush("ControlTextBrushKey"), TriggerSetterValue(focusedCheckBox, "BorderBrush"));

        // Nothing waits: no timer, no deferred step, no clipboard in the dialog's code.
        string code = Source("ConfirmChangeWindow.xaml.cs");
        foreach (string forbidden in new[] { "DispatcherTimer", "System.Timers", "Task.Delay", "Thread.Sleep", "BeginInvoke", "Clipboard", "Announce" })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }
    }

    /// <summary>Every shown or spoken string is a Strings constant or a binding, every Strings reference resolves, and no label implies a change.</summary>
    [Fact]
    public void EveryUserVisibleStringComesFromStrings_AndNoLabelImpliesAChange()
    {
        XElement dialog = Load(DialogFile);
        List<string> literals = new();
        List<string> changeWords = new();
        foreach (XElement element in dialog.DescendantsAndSelf())
        {
            foreach (XAttribute attribute in element.Attributes())
            {
                string local = attribute.Name.LocalName;
                if (WpfShellAccessibilityTests.UserVisibleAttributes.Contains(local, StringComparer.Ordinal)
                    && (!attribute.Value.StartsWith("{", StringComparison.Ordinal) || attribute.Value.StartsWith("{}", StringComparison.Ordinal)))
                {
                    literals.Add(Describe(element) + " " + local + "=\"" + attribute.Value + "\"");
                }

                bool isLabel = WpfShellAccessibilityTests.LabelAttributes.Contains(local, StringComparer.Ordinal) || attribute.Name == Xaml + "Name";
                if (isLabel && WpfShellAccessibilityTests.WriteImplyingWord.IsMatch(WpfShellAccessibilityTests.ResolveStrings(attribute.Value)))
                {
                    changeWords.Add(Describe(element) + " " + local + "=" + attribute.Value);
                }
            }
        }

        Assert.True(literals.Count == 0, "Hard-coded user-visible strings:" + Environment.NewLine + string.Join(Environment.NewLine, literals));
        Assert.True(changeWords.Count == 0, "Labels that imply a change:" + Environment.NewLine + string.Join(Environment.NewLine, changeWords));

        MatchCollection references = WpfShellAccessibilityTests.StringsReference.Matches(File.ReadAllText(Path.Combine(AppDirectory, DialogFile)));
        Assert.True(references.Count >= 30, "expected the dialog to read its strings from Strings; found " + references.Count);
        foreach (Match reference in references)
        {
            FieldInfo? field = typeof(Strings).GetField(reference.Groups[1].Value, BindingFlags.Public | BindingFlags.Static);
            Assert.True(field is not null && field.IsLiteral && field.FieldType == typeof(string), "Strings." + reference.Groups[1].Value + " is referenced by the dialog but is not a public string constant");
        }

        foreach (string name in new[] { Strings.ConfirmChangeName, Strings.ConfirmChangeAndSuspendName, Strings.CancelName, Strings.SuspendCheckBoxName, Strings.ConfirmChangeTitle })
        {
            Assert.DoesNotMatch(WpfShellAccessibilityTests.WriteImplyingWord, name);
        }
    }

    /// <summary>
    /// The main window's arrival rule, in the dialog: a single-line box is selected whole when
    /// keyboard focus arrives from another control (VariableDetailViewModel.SelectionOnKeyboardArrival,
    /// unit-tested in DetailValueArrivalTests), from its end back to its start; a multi-line box is
    /// left alone; a return from the box's context menu or another window keeps the person's own
    /// selection; Up and Down read a box still selected whole from its top.
    /// </summary>
    [Fact]
    public void SingleLineBoxesAreSelectedWholeOnArrival_MultiLineBoxesAreLeftAlone()
    {
        XElement panel = Named(Load(DialogFile), "StackPanel", "BoxesPanel");
        Assert.Equal("OnBoxGotKeyboardFocus", Attr(panel, "GotKeyboardFocus"));
        Assert.Equal("OnBoxLostKeyboardFocus", Attr(panel, "LostKeyboardFocus"));
        Assert.Equal("OnBoxesPreviewKeyDown", Attr(panel, "PreviewKeyDown"));

        string code = Source("ConfirmChangeWindow.xaml.cs");
        string arrive = Method(code, "private void ArriveInBox(");
        Assert.Contains("bool returning = ReferenceEquals(box, _boxLeft) && IsOffTheDialog(e.OldFocus);", arrive, StringComparison.Ordinal);
        Assert.Contains("VariableDetailViewModel.SelectionOnKeyboardArrival(", arrive, StringComparison.Ordinal);
        Assert.Contains("box.AcceptsReturn,", arrive, StringComparison.Ordinal);
        Assert.Contains("clicked: IsMouseButtonDown(),", arrive, StringComparison.Ordinal);
        Assert.Contains("SelectWholeValue(box);", arrive, StringComparison.Ordinal);
        Assert.True(
            arrive.IndexOf("_boxLeft = null;", StringComparison.Ordinal) < arrive.IndexOf("SelectionOnKeyboardArrival(", StringComparison.Ordinal),
            "the remembered box is forgotten before the selection is decided");

        string select = Method(code, "private static void SelectWholeValue(");
        int caretToEnd = select.IndexOf("box.CaretIndex = box.Text.Length;", StringComparison.Ordinal);
        int selectBack = select.IndexOf("EditingCommands.SelectToDocumentStart.Execute(null, box);", StringComparison.Ordinal);
        Assert.True(caretToEnd >= 0 && caretToEnd < selectBack, "the caret goes to the end and the selection back to the start");
        Assert.Contains("box.SelectAll();", select, StringComparison.Ordinal);

        string remember = Method(code, "private void RememberBoxLeft(");
        Assert.Contains("if (IsOffTheDialog(e.NewFocus))", remember, StringComparison.Ordinal);
        Assert.Contains("_boxLeftSelectionStart = box.SelectionStart;", remember, StringComparison.Ordinal);
        Assert.Contains("_boxLeftSelectionLength = box.SelectionLength;", remember, StringComparison.Ordinal);

        string lines = Method(code, "private void OnBoxesPreviewKeyDown(");
        Assert.Contains("e.OriginalSource is TextBox { AcceptsReturn: false } box", lines, StringComparison.Ordinal);
        Assert.Contains("box.SelectionLength == box.Text.Length", lines, StringComparison.Ordinal);
        Assert.Contains("box.CaretIndex = 0;", lines, StringComparison.Ordinal);
        Assert.DoesNotContain("e.Handled", lines, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every single-line box stays under 200 characters (NVDA reads a selection under 512 whole, and
    /// a box heard on every Tab must stay short) and on one line, over the longest fixture: a
    /// three-entry order whose moved entry has the longest description in tools/sample-baseline
    /// (the PXE entry, "UEFI: IP4 Intel(R) Ethernet Connection I217-LM"), with BitLocker on so every
    /// BitLocker box has its longest sentence, the suspend sentence ticked, and the production
    /// snapshot root in the Snapshot folder box. The plans are the write pipeline's own, from the
    /// Safety test fakes, and every text is the Presentation view model's.
    /// </summary>
    [Fact]
    public async Task EverySingleLineBoxStaysUnder200Characters_OverTheLongestFixture()
    {
        Assert.Contains(PxeDescription, File.ReadAllText(Path.Combine(RepositoryPaths.Root, "tools", "sample-baseline", "decoded.json")), StringComparison.Ordinal);
        List<ConfirmChangeViewModel> dialogs = new();
        using (TransactionRig rig = new(ThreeEntryStore(), TransactionFakes.BitLockerOn()))
        {
            dialogs.Add(Dialog(await rig.Prepare(new ReorderRequest(ChangeRequest.AppHost, new[] { NetworkNumber, WindowsOnPartition1, UsbNumber }))));
            dialogs.Add(Dialog(await rig.Prepare(new ReorderRequest(ChangeRequest.AppHost, new[] { NetworkNumber, UsbNumber, WindowsOnPartition1 }))));
            dialogs.Add(Dialog(await rig.Prepare(new BootNextRequest(ChangeRequest.AppHost, NetworkNumber))));

            // A restore of the first reorder, through the same pipeline.
            (_, TransactionResult written) = await rig.PrepareAndCommit(new ReorderRequest(ChangeRequest.AppHost, new[] { NetworkNumber, WindowsOnPartition1, UsbNumber }), BitLockerChoice.KeepProtection);
            Assert.Equal(TransactionOutcome.Verified, written.Outcome);
            SnapshotSummary newest = Assert.IsType<SnapshotSummary>(rig.Snapshots.Newest(Whitelist.BootOrder.Key));
            dialogs.Add(Dialog(await rig.Prepare(new RestoreRequest(ChangeRequest.AppHost, newest.Folder))));
        }

        using (TransactionRig rig = new(ThreeEntryStore(Var("BootNext", U16(NetworkNumber))), TransactionFakes.BitLockerOn()))
        {
            dialogs.Add(Dialog(await rig.Prepare(new ClearBootNextRequest(ChangeRequest.AppHost))));
        }

        // The longest What will change: the restore of a snapshot with the longest folder name,
        // "-bootnext-clear-restore", which starts the PXE entry once, with the suspend sentence
        // ticked. A one-time boot to the PXE entry is written and then restored (its snapshot recorded
        // no BootNext, so the restore clears it and leaves "-bootnext-clear-restore"), and that
        // folder's restore puts the PXE entry back.
        using (TransactionRig rig = new(ThreeEntryStore(), TransactionFakes.BitLockerOn()))
        {
            (_, TransactionResult oneTimeWritten) = await rig.PrepareAndCommit(new BootNextRequest(ChangeRequest.AppHost, NetworkNumber), BitLockerChoice.KeepProtection);
            Assert.Equal(TransactionOutcome.Verified, oneTimeWritten.Outcome);
            SnapshotSummary oneTime = Assert.IsType<SnapshotSummary>(rig.Snapshots.Newest(Whitelist.BootNext.Key));
            (ChangePlan clearPlan, TransactionResult cleared) = await rig.PrepareAndCommit(new RestoreRequest(ChangeRequest.AppHost, oneTime.Folder));
            Assert.Equal(TransactionOutcome.Verified, cleared.Outcome);
            Assert.EndsWith("-bootnext-clear-restore", clearPlan.SnapshotFolderName, StringComparison.Ordinal);
            SnapshotSummary clearRestore = Assert.IsType<SnapshotSummary>(rig.Snapshots.Newest(Whitelist.BootNext.Key));
            dialogs.Add(Dialog(await rig.Prepare(new RestoreRequest(ChangeRequest.AppHost, clearRestore.Folder))));
        }

        Assert.Contains(PxeDescription, dialogs[0].WhatWillChangeText, StringComparison.Ordinal);
        Assert.Contains(PxeDescription, dialogs[2].WhatWillChangeText, StringComparison.Ordinal);
        Assert.NotNull(dialogs[3].Summary.RestoredFromName);
        ConfirmChangeViewModel longest = dialogs[^1];
        Assert.Equal(ChangeOperation.SetBootNext, longest.Plan.Operation);
        Assert.EndsWith("-bootnext-clear-restore", longest.Summary.RestoredFromName, StringComparison.Ordinal);
        Assert.Contains(PxeDescription, longest.WhatWillChangeText, StringComparison.Ordinal);
        Assert.EndsWith(Strings.SuspendSuffix, longest.WhatWillChangeText, StringComparison.Ordinal);
        foreach (ConfirmChangeViewModel dialog in dialogs)
        {
            foreach (string box in new[] { dialog.WhatWillChangeText, dialog.BitLockerStateText, dialog.RiskText, dialog.RecoveryKeyText, dialog.SnapshotFolderText })
            {
                Assert.True(box.Length < 200, box.Length + ": " + box);
                Assert.Equal(-1, box.IndexOfAny(new[] { '\r', '\n' }));
            }

            // The multi-line boxes say on their first line what follows: Before and After of a boot
            // order count its positions, BootNext's one line is the whole value, and Warnings counts
            // its lines.
            foreach (string box in new[] { dialog.BeforeText, dialog.AfterText })
            {
                Assert.False(string.IsNullOrWhiteSpace(box));
                string[] lines = box.Split(Environment.NewLine);
                if (dialog.Plan.Operation == ChangeOperation.Reorder)
                {
                    Assert.Equal(string.Format(CultureInfo.InvariantCulture, Strings.PositionsHeaderFormat, Strings.Positions(lines.Length - 1)), lines[0]);
                    Assert.EndsWith("; Down arrow reads each.", lines[0], StringComparison.Ordinal);
                }
                else
                {
                    Assert.Single(lines);
                }
            }

            Assert.True(dialog.WarningsText == Strings.NoWarningsText || dialog.WarningsText.Split(Environment.NewLine)[0].EndsWith("; Down arrow reads each.", StringComparison.Ordinal), dialog.WarningsText);
        }
    }

    /// <summary>
    /// The two names of Confirm change (section 5.5): "Confirm change and suspend BitLocker for one
    /// restart" while the check box is ticked, which it is by default whenever it is offered, and
    /// "Confirm change" otherwise; What will change names the suspend exactly while the box is ticked.
    /// </summary>
    [Fact]
    public async Task ConfirmChangeHasTwoNames_AndWhatWillChangeNamesTheSuspendWhileTicked()
    {
        using TransactionRig rig = new(ThreeEntryStore(), TransactionFakes.BitLockerOn());
        ConfirmChangeViewModel dialog = Dialog(await rig.Prepare(new ReorderRequest(ChangeRequest.AppHost, new[] { NetworkNumber, WindowsOnPartition1, UsbNumber })));

        Assert.True(dialog.SuspendOffered);
        Assert.True(dialog.SuspendChosen);
        Assert.Equal("Confirm change and suspend BitLocker for one restart", dialog.ConfirmButtonName);
        Assert.Equal(Strings.ConfirmChangeAndSuspendName, dialog.ConfirmButtonName);
        Assert.EndsWith(Strings.SuspendSuffix, dialog.WhatWillChangeText, StringComparison.Ordinal);

        dialog.SuspendChosen = false;

        Assert.Equal("Confirm change", dialog.ConfirmButtonName);
        Assert.Equal(Strings.ConfirmChangeName, dialog.ConfirmButtonName);
        Assert.DoesNotContain(Strings.SuspendSuffix, dialog.WhatWillChangeText, StringComparison.Ordinal);
        Assert.Equal(new ConfirmDecision(true, false), dialog.Confirm());
        Assert.Equal(ConfirmDecision.Cancelled, dialog.Cancel());
    }

    /// <summary>
    /// No check box for the removal of the one-time boot choice, nor for a restore that removes it
    /// (a snapshot that recorded no one-time boot): nothing restarts differently, so the checks do
    /// not offer the suspend, the box stays collapsed, and Confirm change has its plain name.
    /// </summary>
    [Fact]
    public async Task NoCheckBox_ForARemovalOfTheOneTimeBoot_OrARestoreThatRemovesIt()
    {
        List<ConfirmChangeViewModel> dialogs = new();
        using (TransactionRig rig = new(ThreeEntryStore(Var("BootNext", U16(NetworkNumber))), TransactionFakes.BitLockerOn()))
        {
            dialogs.Add(Dialog(await rig.Prepare(new ClearBootNextRequest(ChangeRequest.AppHost))));
        }

        using (TransactionRig rig = new(ThreeEntryStore(), TransactionFakes.BitLockerOn()))
        {
            (_, TransactionResult written) = await rig.PrepareAndCommit(new BootNextRequest(ChangeRequest.AppHost, NetworkNumber), BitLockerChoice.KeepProtection);
            Assert.Equal(TransactionOutcome.Verified, written.Outcome);
            SnapshotSummary newest = Assert.IsType<SnapshotSummary>(rig.Snapshots.Newest(Whitelist.BootNext.Key));
            ConfirmChangeViewModel restore = Dialog(await rig.Prepare(new RestoreRequest(ChangeRequest.AppHost, newest.Folder)));
            Assert.Equal(ChangeOperation.ClearBootNext, restore.Plan.Operation);
            Assert.NotNull(restore.Plan.RestoredFrom);
            dialogs.Add(restore);
        }

        foreach (ConfirmChangeViewModel dialog in dialogs)
        {
            Assert.False(dialog.SuspendOffered);
            Assert.False(dialog.SuspendChosen);
            dialog.SuspendChosen = true;
            Assert.False(dialog.SuspendChosen);
            Assert.Equal(Strings.ConfirmChangeName, dialog.ConfirmButtonName);
            Assert.DoesNotContain(Strings.SuspendSuffix, dialog.WhatWillChangeText, StringComparison.Ordinal);
            Assert.Equal(BitLockerChoice.NotNeeded, dialog.ChoiceFor(dialog.Confirm()));
        }
    }

    // ---- helpers ---------------------------------------------------------------------------------

    /// <summary>The default fixture store with BootOrder Boot0002, Boot0003, Boot000D: the Windows Boot Manager (BootCurrent), the USB stick and the PXE entry.</summary>
    private static VariableSet ThreeEntryStore(params FirmwareVariable[] overrides)
    {
        return Store(new[] { Var("BootOrder", BootList(WindowsOnPartition1, UsbNumber, NetworkNumber)) }.Concat(overrides).ToArray());
    }

    /// <summary>The dialog's view model for a dry run that made a plan, over the production snapshot root the Snapshot folder box names.</summary>
    private static ConfirmChangeViewModel Dialog(PrepareResult prepared)
    {
        ChangePlan? plan = prepared.Plan;
        Assert.True(plan is not null, "the dry run was expected to make a plan: " + prepared.Sentence);
        return new ConfirmChangeViewModel(plan, SnapshotStore.DefaultRootDirectory);
    }

    private static IEnumerable<XElement> Inputs(XElement dialog)
    {
        return dialog.Descendants().Where(element => InputElementTypes.Contains(element.Name.LocalName, StringComparer.Ordinal));
    }

    private static int? TabIndexOf(XElement element)
    {
        string value = Attr(element, "TabIndex");
        return value.Length == 0 ? null : int.Parse(value, CultureInfo.InvariantCulture);
    }

    private static string StringConstant(string name)
    {
        return Assert.IsType<string>(typeof(Strings).GetField(name, BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue());
    }

    /// <summary>Records a brush value that is not a dynamic SystemColors brush key; returns 1 when the value is one.</summary>
    private static int CheckBrush(string where, string value, List<string> violations)
    {
        if (WpfShellAccessibilityTests.DynamicSystemBrush.IsMatch(value))
        {
            return 1;
        }

        violations.Add(where + "=" + value);
        return 0;
    }

    private static string Describe(XElement element)
    {
        string name = Attr(element, Xaml + "Name");
        return element.Name.LocalName + (name.Length == 0 ? string.Empty : " x:Name=" + name);
    }
}
