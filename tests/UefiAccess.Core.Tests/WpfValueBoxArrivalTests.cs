// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Xml.Linq;
using Xunit;
using static UefiAccess.Core.Tests.MainWindowMarkup;

namespace UefiAccess.Core.Tests;

/// <summary>
/// Final review, findings R4 and R14, the window's half (the policy is unit-tested in
/// Presentation/DetailValueArrivalTests). R4: a WPF text box's stock context menu takes keyboard
/// focus while it is open and gives it back when it closes (a menu item clicked on mouse-up, or
/// Shift+F10, the Applications key, Escape, Enter), and Alt+Tab back gives it back too; the window
/// treated every such return as a keyboard arrival and replaced a partial selection with the
/// whole value, so the context menu's Copy copied the whole value. Now the window remembers the
/// value box focus left for a context menu or another window, with its selection, and on the
/// return to that same box keeps a selection of the user's own. R14: a single-line value of 512
/// characters or more is not selected on arrival (NVDA would say only "selected N characters"):
/// the caret goes to its start, and its help text, bound to the field's notifying HelpText
/// property (a converter until round 4; the pane is now updated in place, and a converter bound to
/// the field itself would leave a kept box with the help of the row before), gives its length and
/// says that Down reads it line by line. WPF cannot run under xunit on every host, so, like
/// <see cref="DetailFieldWrappingTests"/>, these checks read the files.
/// </summary>
public sealed class WpfValueBoxArrivalTests
{
    [Fact]
    public void BothKindsOfValueBox_RememberWhereFocusWentWhenItLeft()
    {
        XElement window = Load();
        Assert.Equal("OnDetailFieldLostKeyboardFocus", Attr(Named(window, "ItemsControl", "SectionsControl"), "LostKeyboardFocus"));
        Assert.Equal("OnReadOnlyStatusLostKeyboardFocus", Attr(Named(window, "TextBox", "ReadOnlyStatusBox"), "LostKeyboardFocus"));

        string code = Source("MainWindow.xaml.cs");
        string fieldLost = Method(code, "private void OnDetailFieldLostKeyboardFocus(");
        Assert.Contains("e.OriginalSource is TextBox { DataContext: DetailFieldViewModel } box", fieldLost, StringComparison.Ordinal);
        Assert.Contains("AssignCopyValue(_defaultCopyValueText);", fieldLost, StringComparison.Ordinal);
        Assert.Contains("RememberValueBoxLeft(box, e);", fieldLost, StringComparison.Ordinal);
        string statusLost = Method(code, "private void OnReadOnlyStatusLostKeyboardFocus(");
        Assert.Contains("ReferenceEquals(e.OriginalSource, ReadOnlyStatusBox)", statusLost, StringComparison.Ordinal);
        Assert.Contains("RememberValueBoxLeft(ReadOnlyStatusBox, e);", statusLost, StringComparison.Ordinal);

        // Remembered (box and selection) only when focus went off the window's own elements: to
        // the box's context menu, which is a popup, or to no element (another window, a dialog).
        // Focus that went to another element of the window forgets it.
        string remember = Method(code, "private void RememberValueBoxLeft(");
        int check = remember.IndexOf("if (IsOffTheWindow(e.NewFocus))", StringComparison.Ordinal);
        Assert.True(check >= 0, "the box is remembered only for focus that left the window's own elements");
        Assert.Contains("_valueBoxLeft = box;", remember, StringComparison.Ordinal);
        Assert.Contains("_valueBoxLeftSelectionStart = box.SelectionStart;", remember, StringComparison.Ordinal);
        Assert.Contains("_valueBoxLeftSelectionLength = box.SelectionLength;", remember, StringComparison.Ordinal);
        Assert.Contains("_valueBoxLeft = null;", remember[check..], StringComparison.Ordinal);

        string offWindow = Method(code, "private bool IsOffTheWindow(");
        Assert.Contains("element is not DependencyObject target", offWindow, StringComparison.Ordinal);
        Assert.Contains("FindAncestor<ContextMenu>(target) is not null", offWindow, StringComparison.Ordinal);
        Assert.Contains("!ReferenceEquals(Window.GetWindow(target), this)", offWindow, StringComparison.Ordinal);
    }

    /// <summary>
    /// The Dell state line (Dell settings source) is a wrapped
    /// single-line read-only box like the Writability line, so it follows the same arrival rule:
    /// selected whole when keyboard focus arrives from another element (without it NVDA reads a
    /// wrapped value only to the end of its first line, round 1), and remembered when focus goes to
    /// its context menu or another window, so a selection of the user's own is kept on the return.
    /// </summary>
    [Fact]
    public void TheDellStateLine_ArrivesAndIsRememberedLikeEveryValueBox()
    {
        XElement window = Load();
        XElement source = Named(window, "TextBox", "DellSourceBox");
        Assert.Equal("OnDellSourceGotKeyboardFocus", Attr(source, "GotKeyboardFocus"));
        Assert.Equal("OnDellSourceLostKeyboardFocus", Attr(source, "LostKeyboardFocus"));
        Assert.Equal("Wrap", Attr(source, "TextWrapping"));
        Assert.Equal(string.Empty, Attr(source, "AcceptsReturn"));

        string code = Source("MainWindow.xaml.cs");
        string got = Method(code, "private void OnDellSourceGotKeyboardFocus(");
        Assert.Contains("ReferenceEquals(e.OriginalSource, DellSourceBox)", got, StringComparison.Ordinal);
        Assert.Contains("ArriveInValueBox(DellSourceBox, isMultiline: false, e);", got, StringComparison.Ordinal);
        string lost = Method(code, "private void OnDellSourceLostKeyboardFocus(");
        Assert.Contains("ReferenceEquals(e.OriginalSource, DellSourceBox)", lost, StringComparison.Ordinal);
        Assert.Contains("RememberValueBoxLeft(DellSourceBox, e);", lost, StringComparison.Ordinal);
    }

    [Fact]
    public void AReturnIsFocusBackOnTheSameBoxFromOffTheWindow_AndThePolicyDecidesWithTheRememberedSelection()
    {
        string code = Source("MainWindow.xaml.cs");
        string arrive = Method(code, "private void ArriveInValueBox(");

        // Same box, and the old focus is off the window (the context menu, or none): a return.
        Assert.Contains("bool returning = ReferenceEquals(box, _valueBoxLeft) && IsOffTheWindow(e.OldFocus);", arrive, StringComparison.Ordinal);

        // Consumed at once, so a later arrival from another element is never taken for a return.
        Assert.True(
            arrive.IndexOf("_valueBoxLeft = null;", StringComparison.Ordinal) < arrive.IndexOf("SelectionOnKeyboardArrival(", StringComparison.Ordinal),
            "the remembered box is forgotten before the selection is decided");
        Assert.Contains("_valueBoxLeftSelectionStart,", arrive, StringComparison.Ordinal);
        Assert.Contains("_valueBoxLeftSelectionLength);", arrive, StringComparison.Ordinal);

        // Null leaves the box as it is; the whole value is selected from the end back; anything
        // else (a long value) puts the caret at the start without a selection.
        Assert.Contains("if (selection is null)", arrive, StringComparison.Ordinal);
        Assert.Contains("if (selection.Value.Length == box.Text.Length)", arrive, StringComparison.Ordinal);
        Assert.Contains("SelectWholeValueOnArrival(box);", arrive, StringComparison.Ordinal);
        Assert.Contains("PutCaretAtTheStart(box);", arrive, StringComparison.Ordinal);

        string caret = Method(code, "private static void PutCaretAtTheStart(");
        Assert.Contains("box.Select(0, 0);", caret, StringComparison.Ordinal);
        Assert.Contains("box.ScrollToHome();", caret, StringComparison.Ordinal);
        Assert.DoesNotContain(".Text =", caret, StringComparison.Ordinal);

        // Nothing here waits: no timer and no deferred selection that could land after the context
        // menu's Copy.
        foreach (string member in new[] { arrive, caret, Method(code, "private void RememberValueBoxLeft(") })
        {
            Assert.DoesNotContain("Timer", member, StringComparison.Ordinal);
            Assert.DoesNotContain("BeginInvoke", member, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The help text comes from the field's own HelpText property, which is
    /// VariableDetailViewModel.HelpTextFor of the field, computed again whenever the value changes
    /// and notified when it differs (unit-tested in Presentation/DetailPaneInPlaceUpdateTests). Until
    /// round 4 a converter bound to the field object gave it; with the pane updated in place that
    /// binding would be evaluated only when a box gets a new field, so a kept box would keep the
    /// help, and a long value's character count, of the row shown before. The converter is gone.
    /// </summary>
    [Fact]
    public void AFieldsHelpText_ComesFromItsFieldsHelpTextProperty()
    {
        XElement window = Load();
        XElement field = Assert.Single(Named(window, "ItemsControl", "SectionsControl").Descendants(Wpf + "TextBox"));
        Assert.Equal("{Binding HelpText, Mode=OneWay}", Attr(field, "AutomationProperties.HelpText"));

        // The Writability line is short and keeps the ordinary help text.
        Assert.Equal("{x:Static res:Strings.DetailFieldHelp}", Attr(Named(window, "TextBox", "ReadOnlyStatusBox"), "AutomationProperties.HelpText"));

        // The property is the policy's help text of the field itself, cached when the field is made
        // and computed again by every change of its value.
        string presentation = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "src", "UefiAccess.Presentation", "ViewModels", "DetailFieldViewModel.cs")).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains("public sealed class DetailFieldViewModel : ObservableObject", presentation, StringComparison.Ordinal);
        Assert.Contains("_helpText = VariableDetailViewModel.HelpTextFor(this);", Method(presentation, "public DetailFieldViewModel(string label,"), StringComparison.Ordinal);
        string update = Method(presentation, "internal bool UpdateValue(");
        Assert.True(
            update.IndexOf("Value = text;", StringComparison.Ordinal) >= 0
            && update.IndexOf("Value = text;", StringComparison.Ordinal) < update.IndexOf("HelpText = VariableDetailViewModel.HelpTextFor(this);", StringComparison.Ordinal),
            "a new value computes the help text again, after the value is set");
        Assert.Contains("private set => SetProperty(ref _helpText, value);", Method(presentation, "public string HelpText"), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(AppDirectory, "Platform", "DetailFieldHelpTextConverter.cs")), "the converter bound to the field object is gone");
        Assert.DoesNotContain("DetailFieldHelpTextConverter", File.ReadAllText(Path.Combine(AppDirectory, "MainWindow.xaml")), StringComparison.Ordinal);
    }

    [Fact]
    public void TheMarkupExplainsTheLongValueAndTheReturnRules()
    {
        string markup = File.ReadAllText(Path.Combine(AppDirectory, "MainWindow.xaml"));
        int style = markup.IndexOf("<Style x:Key=\"DetailFieldStyle\"", StringComparison.Ordinal);
        int note = markup.LastIndexOf("<!--", style, StringComparison.Ordinal);
        string comment = markup[note..style];
        Assert.Contains("512 characters or more", comment, StringComparison.Ordinal);
        Assert.Contains("context", comment, StringComparison.Ordinal);
        Assert.Contains("SelectionOnKeyboardArrival", comment, StringComparison.Ordinal);
    }
}
