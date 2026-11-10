// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Xml.Linq;
using UefiAccess.Core.Decoders;
using UefiAccess.Core.Variables;
using UefiAccess.Presentation.Services;
using UefiAccess.Presentation.ViewModels;
using Xunit;
using static UefiAccess.Core.Tests.MainWindowMarkup;

namespace UefiAccess.Core.Tests;

/// <summary>
/// Round-1 transcript, still true on main before round 2: NVDA 2026.2 reads a text box that gets
/// focus only up to the end of the line at the caret (for a WPF TextBox, the visual, wrapped
/// line), unless text is selected, which it reads whole as "selected" and the text (speech.py,
/// getObjectSpeech). Boot0000's 102-character Device path wrapped and was heard as "HD", the
/// Writability line was cut at "from this ", while the 157-character Summary, then unwrapped
/// because it was over 120 characters, was read whole; and a long unwrapped value made a
/// low-vision reader scroll a small box sideways. So every single-line value box wraps
/// (TextWrapping Wrap, no horizontal scroll bar), the Writability box too, and the window selects
/// the whole value when keyboard focus arrives on one (a click places the caret as usual), so a
/// screen reader reads all of it. Only a value that really spans lines (the hex dump, a decoded
/// value with a line break) is multi-line: unwrapped, in a monospace font, read line by line;
/// length alone no longer makes a value multi-line.
/// </summary>
public sealed class DetailFieldWrappingTests
{
    [Fact]
    public void EverySingleLineValueBoxWraps_OnlyAMultilineBoxKeepsItsLines()
    {
        XElement window = Load();
        XElement fieldStyle = KeyedStyle(window, "DetailFieldStyle");
        Assert.Equal("Wrap", SetterValue(fieldStyle, "TextWrapping"));
        Assert.Equal("Disabled", SetterValue(fieldStyle, "HorizontalScrollBarVisibility"));
        Assert.Equal("Auto", SetterValue(fieldStyle, "VerticalScrollBarVisibility"));
        Assert.Equal("True", SetterValue(fieldStyle, "IsReadOnly"));

        // The multi-line trigger is the only one: it keeps the box's own lines unwrapped (the hex
        // dump's columns) and changes the font.
        XElement multiline = Assert.Single(fieldStyle.Descendants(Wpf + "DataTrigger"));
        Assert.Equal("{Binding IsMultiline}", Attr(multiline, "Binding"));
        Assert.Equal("True", Attr(multiline, "Value"));
        Assert.Equal("NoWrap", TriggerSetterValue(multiline, "TextWrapping"));
        Assert.Equal("Auto", TriggerSetterValue(multiline, "HorizontalScrollBarVisibility"));
        Assert.Equal("True", TriggerSetterValue(multiline, "AcceptsReturn"));
        Assert.Contains("Consolas", TriggerSetterValue(multiline, "FontFamily"), StringComparison.Ordinal);
        Assert.Empty(fieldStyle.Descendants(Wpf + "Trigger"));

        XElement writability = Named(window, "TextBox", "ReadOnlyStatusBox");
        Assert.Equal("Wrap", Attr(writability, "TextWrapping"));
        Assert.Equal(string.Empty, Attr(writability, "HorizontalScrollBarVisibility"));

        // No box of the detail pane sets its own wrapping except Writability (wrapped as above);
        // the labels above the boxes wrap too (a TextBlock is read by its name, not line by line).
        XElement pane = Named(window, "ScrollViewer", "DetailPane");
        foreach (XElement box in pane.Descendants(Wpf + "TextBox").Where(box => !ReferenceEquals(box, writability)))
        {
            Assert.Equal("{StaticResource DetailFieldStyle}", Attr(box, "Style"));
            Assert.Equal(string.Empty, Attr(box, "TextWrapping"));
        }

        Assert.All(
            Named(window, "ItemsControl", "SectionsControl").Descendants(Wpf + "TextBlock").Where(block => Attr(block, "Text").Contains("Label", StringComparison.Ordinal)),
            label => Assert.Equal("Wrap", Attr(label, "TextWrapping")));
    }

    [Fact]
    public void KeyboardArrivalSelectsTheWholeSingleLineValue_AClickDoesNot()
    {
        XElement window = Load();
        Assert.Equal("OnDetailFieldGotKeyboardFocus", Attr(Named(window, "ItemsControl", "SectionsControl"), "GotKeyboardFocus"));
        Assert.Equal("OnReadOnlyStatusGotKeyboardFocus", Attr(Named(window, "TextBox", "ReadOnlyStatusBox"), "GotKeyboardFocus"));

        string code = Source("MainWindow.xaml.cs");

        // Every field box of the sections, and the Writability line, which has no field view
        // model: the arrival is decided by VariableDetailViewModel.SelectionOnKeyboardArrival
        // (unit-tested in DetailValueArrivalTests), with a click telling itself by a mouse button
        // that is down while it moves focus.
        string field = Method(code, "private void OnDetailFieldGotKeyboardFocus(");
        Assert.Contains("e.OriginalSource is TextBox { DataContext: DetailFieldViewModel field } box", field, StringComparison.Ordinal);
        Assert.Contains("AssignCopyValue(field.Value);", field, StringComparison.Ordinal);
        Assert.Contains("ArriveInValueBox(box, field.IsMultiline, e);", field, StringComparison.Ordinal);
        string writability = Method(code, "private void OnReadOnlyStatusGotKeyboardFocus(");
        Assert.Contains("ArriveInValueBox(ReadOnlyStatusBox, isMultiline: false, e);", writability, StringComparison.Ordinal);
        string arrive = Method(code, "private void ArriveInValueBox(");
        Assert.Contains("VariableDetailViewModel.SelectionOnKeyboardArrival(", arrive, StringComparison.Ordinal);
        Assert.Contains("clicked: IsMouseButtonDown(),", arrive, StringComparison.Ordinal);
        Assert.Contains("SelectWholeValueOnArrival(box);", arrive, StringComparison.Ordinal);
        string mouse = Method(code, "private static bool IsMouseButtonDown(");
        Assert.Contains("Mouse.LeftButton == MouseButtonState.Pressed", mouse, StringComparison.Ordinal);
        Assert.Contains("Mouse.RightButton == MouseButtonState.Pressed", mouse, StringComparison.Ordinal);

        // The selection itself: the whole text, made from the end back to the start (the stock
        // Ctrl+Shift+Home command), so the caret, the selection's moving end, is at the start and
        // the box shows the first line: SelectAll leaves the caret at the end, and the box scrolled
        // a 10-line Summary at 225 percent text to its end after the layout (measured on this
        // machine). SelectAll is only the fallback.
        string select = Method(code, "private static void SelectWholeValueOnArrival(");
        int caretToEnd = select.IndexOf("box.CaretIndex = box.Text.Length;", StringComparison.Ordinal);
        int selectBack = select.IndexOf("EditingCommands.SelectToDocumentStart.Execute(null, box);", StringComparison.Ordinal);
        Assert.True(caretToEnd >= 0 && caretToEnd < selectBack, "the caret goes to the end and the selection back to the start");
        Assert.Contains("if (box.SelectionLength != box.Text.Length)", select, StringComparison.Ordinal);
        Assert.Contains("box.SelectAll();", select, StringComparison.Ordinal);
        Assert.DoesNotContain("Timer", select, StringComparison.Ordinal);
        Assert.DoesNotContain(".Text =", select, StringComparison.Ordinal);

        // Up and Down right after arrival read from the top: Down reads the second visual line,
        // not the stock jump to the end of the selection. Only in a single-line box, with no modifier.
        Assert.Contains("StartLinesAtTheTop(e);", Method(code, "private void OnDetailPreviewKeyDown("), StringComparison.Ordinal);
        string lines = Method(code, "private static void StartLinesAtTheTop(");
        Assert.Contains("Keyboard.Modifiers == ModifierKeys.None", lines, StringComparison.Ordinal);
        Assert.Contains("e.OriginalSource is TextBox { AcceptsReturn: false } box", lines, StringComparison.Ordinal);
        Assert.Contains("box.SelectionLength == box.Text.Length", lines, StringComparison.Ordinal);
        Assert.Contains("box.CaretIndex = 0;", lines, StringComparison.Ordinal);
        Assert.DoesNotContain("e.Handled", lines, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyValuesWithALineBreakAreMultiline_EveryOtherIsSelectedWholeOnArrival()
    {
        Assert.Null(typeof(VariableDetailViewModel).GetField("MultilineThreshold"));

        // The round-1 values: Boot0000's device path (102 characters) and its summary are
        // single-line, selected whole on arrival; the hex dump and a value with a line break are
        // multi-line, read line by line.
        const string devicePath = "HD(2,GPT,edba284e-7747-40ae-9a8c-528d3ca4af5b,0x200800,0x82000)/File(\\EFI\\Microsoft\\Boot\\bootmgfw.efi)";
        Assert.Equal(102, devicePath.Length);
        DecodedVariable decoded = new(
            FirmwareVariable.MakeKey(KnownGuids.EfiGlobalVariable, "Boot0000"),
            "Synthetic",
            "Boot0000: Windows Boot Manager; active, category Boot; " + devicePath,
            new[] { new DecodedField("Device path", devicePath), new DecodedField("Lines", "one\r\ntwo") },
            null,
            null);
        FirmwareVariable variable = new("Boot0000", KnownGuids.EfiGlobalVariable, VariableAttributes.NonVolatile, new byte[] { 1, 0, 0, 0 });
        VariableDetailViewModel detail = new();
        detail.Show(new VariableItemViewModel(variable, NamespaceCatalog.Describe(variable.VendorGuid), decoded));

        List<DetailFieldViewModel> fields = detail.Sections.SelectMany(section => section.Fields).ToList();
        DetailFieldViewModel path = fields.Single(field => field.Label == "Device path");
        Assert.False(path.IsMultiline);
        Assert.True(path.SelectsAllOnKeyboardFocus);
        DetailFieldViewModel summary = fields.Single(field => field.Label == VariableDetailViewModel.SummaryLabel);
        Assert.False(summary.IsMultiline);
        Assert.True(summary.SelectsAllOnKeyboardFocus);
        DetailFieldViewModel lines = fields.Single(field => field.Label == "Lines");
        Assert.True(lines.IsMultiline);
        Assert.False(lines.SelectsAllOnKeyboardFocus);
        DetailFieldViewModel hex = fields.Single(field => field.Label == VariableDetailViewModel.HexDumpLabel);
        Assert.True(hex.IsMultiline);
        Assert.False(hex.SelectsAllOnKeyboardFocus);
        Assert.All(fields.Where(field => !field.IsMultiline), field => Assert.Equal(-1, field.Value.IndexOfAny(new[] { '\r', '\n' })));
        Assert.All(fields, field => Assert.Equal(!field.IsMultiline, field.SelectsAllOnKeyboardFocus));

        // The rule belongs to the field, whoever built it.
        Assert.True(new DetailFieldViewModel("GUID", "8be4df61-93ca-11d2-aa0d-00e098032b8c").SelectsAllOnKeyboardFocus);
        Assert.False(new DetailFieldViewModel("Raw", "00 01", isMultiline: true).SelectsAllOnKeyboardFocus);
    }
}
