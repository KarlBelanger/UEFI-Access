// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UefiAccess.Presentation.ViewModels;
using Xunit;
using static UefiAccess.Core.Tests.MainWindowMarkup;

namespace UefiAccess.Core.Tests;

/// <summary>
/// Fix 1 of the A16 measurement (round 4, finding A1-6), the window's half: the detail pane is
/// updated in place, so the markup must bind its sections and fields to collections that are
/// never replaced, and every text of a field box that can change with the row must come from a
/// notifying property of the field (a binding to the field object itself, such as the converter
/// the help text used until round 4, would be evaluated only when a box gets a new field, and a
/// kept box would keep the row before's help). The window starts a kept box afresh on a row change
/// without applying templates (which would realise collapsed scroll bars and raise the very
/// structure events the fix removes). The behaviour is unit-tested in
/// Presentation/DetailPaneInPlaceUpdateTests and DetailPaneReconcilerTests; WPF cannot run under
/// xunit on every host, so, like <see cref="WpfValueBoxArrivalTests"/>, these checks read the files.
/// </summary>
public sealed class DetailPaneStableBindingTests
{
    [Fact]
    public void TheDetailItemsControls_BindToCollectionsThatAreNeverReplaced()
    {
        XElement window = Load();
        XElement sections = Named(window, "ItemsControl", "SectionsControl");
        Assert.Equal("{Binding Detail.Sections, Mode=OneWay}", Attr(sections, "ItemsSource"));
        XElement fields = Assert.Single(sections.Descendants(Wpf + "ItemsControl"));
        Assert.Equal("{Binding Fields, Mode=OneWay}", Attr(fields, "ItemsSource"));

        // The properties are read-only collections that notify their own changes, with no setter,
        // over a backing collection that is assigned once.
        AssertStableCollection(typeof(VariableDetailViewModel), nameof(VariableDetailViewModel.Sections), typeof(DetailSectionViewModel));
        AssertStableCollection(typeof(DetailSectionViewModel), nameof(DetailSectionViewModel.Fields), typeof(DetailFieldViewModel));
    }

    [Fact]
    public void AFieldBox_TakesEveryChangingTextFromANotifyingProperty()
    {
        XElement window = Load();
        XElement sections = Named(window, "ItemsControl", "SectionsControl");
        XElement box = Assert.Single(sections.Descendants(Wpf + "TextBox"));
        Assert.Equal("{Binding Value, Mode=OneWay}", Attr(box, "Text"));
        Assert.Equal("{Binding HelpText, Mode=OneWay}", Attr(box, "AutomationProperties.HelpText"));
        Assert.Equal("{Binding AccessibleName, Mode=OneWay}", Attr(box, "AutomationProperties.Name"));
        Assert.Equal("{Binding Header, RelativeSource={RelativeSource AncestorType=GroupBox}}", Attr(box, "AutomationProperties.LabeledBy"));
        Assert.Equal("{StaticResource DetailFieldStyle}", Attr(box, "Style"));
        Assert.Equal("12", Attr(box, "TabIndex"));
        XElement label = Assert.Single(box.Parent!.Elements(Wpf + "TextBlock"));
        Assert.Equal("{Binding Label, Mode=OneWay}", Attr(label, "Text"));
        Assert.Equal("{Binding Label, Mode=OneWay}", Attr(label, "AutomationProperties.Name"));
        XElement group = Assert.Single(sections.Descendants(Wpf + "GroupBox"));
        Assert.Equal("{Binding Title, Mode=OneWay}", Attr(group, "AutomationProperties.Name"));

        // No binding inside the sections is to the item itself (a binding without a path is
        // evaluated again only when the data context changes, never when the field's text does),
        // and none uses a converter.
        Regex pathless = new(@"\{Binding(?:\s+Mode=OneWay)?\s*(?:,|\})", RegexOptions.CultureInvariant);
        foreach (XElement element in sections.DescendantsAndSelf())
        {
            foreach (XAttribute attribute in element.Attributes())
            {
                Assert.DoesNotMatch(pathless, attribute.Value);
                Assert.DoesNotContain("Converter=", attribute.Value, StringComparison.Ordinal);
            }
        }

        // The field notifies its changes, and its help text is the policy's help for itself.
        Assert.True(typeof(INotifyPropertyChanged).IsAssignableFrom(typeof(DetailFieldViewModel)));
        string field = PresentationSource("DetailFieldViewModel.cs");
        Assert.Contains("public sealed class DetailFieldViewModel : ObservableObject", field, StringComparison.Ordinal);
        Assert.Equal(2, CountOf(field, "VariableDetailViewModel.HelpTextFor(this)"));
        string helpText = Method(field, "public string HelpText");
        Assert.Contains("get => _helpText;", helpText, StringComparison.Ordinal);
        Assert.Contains("private set => SetProperty(ref _helpText, value);", helpText, StringComparison.Ordinal);
        string value = Method(field, "public string Value");
        Assert.Contains("private set => SetProperty(ref _value, value);", value, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDetailCollections_AreNeverClearedOrReplaced()
    {
        Regex indexerAssignment = new(@"\]\s*=(?!=)", RegexOptions.CultureInvariant);
        foreach (string file in new[] { "VariableDetailViewModel.cs", "DetailSectionViewModel.cs", "DetailPaneReconciler.cs" })
        {
            string source = PresentationSource(file);
            Assert.False(source.Contains(".Clear(", StringComparison.Ordinal), file + " clears a collection");
            Assert.False(source.Contains("Move(", StringComparison.Ordinal), file + " moves an item");
            Assert.False(source.Contains("SetItem(", StringComparison.Ordinal), file + " replaces an item");
            foreach (string line in source.Split('\n'))
            {
                string code = line.TrimStart();
                if (code.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.False(indexerAssignment.IsMatch(code) && (code.Contains("current[", StringComparison.Ordinal) || code.Contains("_sections[", StringComparison.Ordinal) || code.Contains("_fields[", StringComparison.Ordinal)), file + " assigns through an indexer: " + code.Trim());
            }
        }

        // The reconciler changes the live collection only by RemoveAt and Insert.
        string reconciler = PresentationSource("DetailPaneReconciler.cs");
        Assert.Equal(1, CountOf(reconciler, "current.RemoveAt("));
        Assert.Equal(1, CountOf(reconciler, "current.Insert("));
        Assert.DoesNotContain("current.Add(", reconciler, StringComparison.Ordinal);
        Assert.DoesNotContain("current.Remove(", reconciler, StringComparison.Ordinal);

        // The pane and the sections assign their read-only views once, in the constructor, and
        // hand their backing collections to the reconciler only. The pane's parameterless
        // constructor (a pane whose Change group never shows) chains to the one over the shell's
        // Change group (M3), so the view is assigned there, where every pane passes.
        string detail = PresentationSource("VariableDetailViewModel.cs");
        Assert.Equal(1, CountOf(detail, "Sections = "));
        Assert.Contains(": this(new ChangeViewModel())", Method(detail, "public VariableDetailViewModel()"), StringComparison.Ordinal);
        Assert.Contains("Sections = new ReadOnlyObservableCollection<DetailSectionViewModel>(_sections);", Method(detail, "public VariableDetailViewModel(ChangeViewModel change)"), StringComparison.Ordinal);
        Assert.Contains("DetailPaneReconciler.Apply(", detail, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(detail, "_sections,"));
        string section = PresentationSource("DetailSectionViewModel.cs");
        Assert.Equal(1, CountOf(section, "Fields = "));
        Assert.Contains("Fields = new ReadOnlyObservableCollection<DetailFieldViewModel>(_fields);", Method(section, "public DetailSectionViewModel(string title,"), StringComparison.Ordinal);
        Assert.Contains("DetailPaneReconciler.Apply(_fields, wanted,", section, StringComparison.Ordinal);
    }

    [Fact]
    public void ARowChange_StartsReusedValueBoxesAtTheTop_WithoutApplyingTemplates()
    {
        string code = Source("MainWindow.xaml.cs");
        string changed = Method(code, "private void OnDetailPropertyChanged(");
        int item = changed.IndexOf("e.PropertyName == nameof(VariableDetailViewModel.Item)", StringComparison.Ordinal);
        Assert.True(item >= 0, "a change of the row shown starts the kept boxes afresh");
        Assert.True(changed.IndexOf("StartValueBoxesAfresh();", item, StringComparison.Ordinal) > item);
        Assert.Contains("_defaultCopyValueText = _shell.Detail.CopyValueText;", changed, StringComparison.Ordinal);

        string afresh = Method(code, "private void StartValueBoxesAfresh(");

        // A field box remembered for a return from its context menu or another window is forgotten
        // (coming back to it is an arrival, as it was to a new box); the Writability line keeps it.
        Assert.Contains("if (_valueBoxLeft is not null && !ReferenceEquals(_valueBoxLeft, ReadOnlyStatusBox))", afresh, StringComparison.Ordinal);
        Assert.Contains("_valueBoxLeft = null;", afresh, StringComparison.Ordinal);

        // Every realised field box without keyboard focus gets the caret at the start and its top
        // in view, and only where something differs.
        Assert.Contains("foreach (TextBox box in RealisedValueBoxes(SectionsControl))", afresh, StringComparison.Ordinal);
        int focused = afresh.IndexOf("if (box.IsKeyboardFocused)", StringComparison.Ordinal);
        Assert.True(focused >= 0 && afresh.IndexOf("continue;", focused, StringComparison.Ordinal) > focused, "the box with keyboard focus is left as it is");
        int select = afresh.IndexOf("box.Select(0, 0);", StringComparison.Ordinal);
        int scroll = afresh.IndexOf("box.ScrollToHome();", StringComparison.Ordinal);
        Assert.True(select > focused && scroll > focused, "the caret and the scroll position are reset after the focus check");
        Assert.Contains("if (box.SelectionStart != 0 || box.SelectionLength != 0)", afresh, StringComparison.Ordinal);
        Assert.Contains("if (box.VerticalOffset > 0 || box.HorizontalOffset > 0)", afresh, StringComparison.Ordinal);

        // The traversal reads the visual tree as it is: no template is applied, and a text box is
        // never looked into.
        string boxes = Method(code, "private static IEnumerable<TextBox> RealisedValueBoxes(");
        Assert.DoesNotContain("ApplyTemplate", boxes, StringComparison.Ordinal);
        Assert.DoesNotContain("FindDescendants", boxes, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateLayout", boxes, StringComparison.Ordinal);
        Assert.Contains("VisualTreeHelper.GetChild(root, i)", boxes, StringComparison.Ordinal);
        Assert.Contains("if (child is TextBox box)", boxes, StringComparison.Ordinal);
        Assert.Contains("if (box.DataContext is DetailFieldViewModel)", boxes, StringComparison.Ordinal);
        Assert.True(
            boxes.IndexOf("continue;", StringComparison.Ordinal) > boxes.IndexOf("if (child is TextBox box)", StringComparison.Ordinal)
            && boxes.IndexOf("continue;", StringComparison.Ordinal) < boxes.IndexOf("RealisedValueBoxes(child)", StringComparison.Ordinal),
            "a text box ends the descent");

        // Nothing here waits, and the access-key and focus flushes stay the three they were.
        foreach (string member in new[] { changed, afresh, boxes })
        {
            Assert.DoesNotContain("Timer", member, StringComparison.Ordinal);
            Assert.DoesNotContain("BeginInvoke", member, StringComparison.Ordinal);
            Assert.DoesNotContain("EnsureDetailCurrent", member, StringComparison.Ordinal);
        }

        Assert.Equal(3, CountOf(code, "EnsureDetailCurrent("));
    }

    [Fact]
    public void DetailFieldBoxes_KeepNoUndoHistory()
    {
        XElement style = KeyedStyle(Load(), "DetailFieldStyle");
        Assert.Equal("False", SetterValue(style, "IsUndoEnabled"));
        Assert.Equal("True", SetterValue(style, "IsReadOnly"));
    }

    private static void AssertStableCollection(Type owner, string propertyName, Type itemType)
    {
        PropertyInfo property = owner.GetProperty(propertyName)!;
        Assert.Equal(typeof(ReadOnlyObservableCollection<>).MakeGenericType(itemType), property.PropertyType);
        Assert.Null(property.SetMethod);
        Assert.False(property.CanWrite);
        FieldInfo backing = Assert.Single(
            owner.GetFields(BindingFlags.Instance | BindingFlags.NonPublic),
            field => field.FieldType == typeof(ObservableCollection<>).MakeGenericType(itemType));
        Assert.True(backing.IsInitOnly, owner.Name + "." + backing.Name + " must be readonly");
        Assert.DoesNotContain(owner.GetProperties(), candidate => candidate.PropertyType == typeof(ObservableCollection<>).MakeGenericType(itemType));
    }

    private static string PresentationSource(string file)
    {
        return File.ReadAllText(Path.Combine(RepositoryPaths.Root, "src", "UefiAccess.Presentation", "ViewModels", file)).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static int CountOf(string text, string value)
    {
        int count = 0;
        for (int index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
