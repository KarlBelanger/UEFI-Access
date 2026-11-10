// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using UefiAccess.Presentation.ViewModels;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// Records every change a view bound to a <see cref="VariableDetailViewModel"/> would see, in
/// order: the pane's own property changes ("Detail.Heading"), the changes of its sections
/// collection ("Sections Add 3", "Sections Remove 3", or a Reset, Replace or Move, which the
/// in-place update must never make), the changes of every section's fields collection ("Fields of
/// Attributes Add 3") and the property changes of every field ("Decoded/Signature types.Value",
/// "Decoded/Signature types.HelpText"; a field is named by its section and its label, because a
/// label such as "Size" occurs in two sections). Sections and fields added later are followed from
/// the moment they come in, and removed ones are no longer followed. Nothing here changes the pane.
/// </summary>
internal sealed class DetailPaneRecorder : IDisposable
{
    private readonly VariableDetailViewModel _detail;
    private readonly Dictionary<DetailSectionViewModel, bool> _sections = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<DetailFieldViewModel, string> _sectionOfField = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, DetailSectionViewModel> _sectionOfFields = new(ReferenceEqualityComparer.Instance);
    private readonly List<string> _events = new();
    private readonly List<NotifyCollectionChangedEventArgs> _sectionChanges = new();
    private readonly List<(DetailSectionViewModel Section, NotifyCollectionChangedEventArgs Change)> _fieldCollectionChanges = new();
    private readonly List<(DetailFieldViewModel Field, string Property)> _fieldChanges = new();
    private readonly List<string> _paneProperties = new();

    /// <summary>Starts recording the pane's changes.</summary>
    /// <param name="detail">The pane.</param>
    public DetailPaneRecorder(VariableDetailViewModel detail)
    {
        _detail = detail;
        _detail.PropertyChanged += OnPaneChanged;
        ((INotifyCollectionChanged)_detail.Sections).CollectionChanged += OnSectionsChanged;
        foreach (DetailSectionViewModel section in _detail.Sections)
        {
            Follow(section);
        }
    }

    /// <summary>Every change in order, as text.</summary>
    public IReadOnlyList<string> Events => _events;

    /// <summary>The pane's own property changes, by property name.</summary>
    public IReadOnlyList<string> PaneProperties => _paneProperties;

    /// <summary>The changes of the sections collection.</summary>
    public IReadOnlyList<NotifyCollectionChangedEventArgs> SectionChanges => _sectionChanges;

    /// <summary>The changes of the fields collections, with the section each belongs to.</summary>
    public IReadOnlyList<(DetailSectionViewModel Section, NotifyCollectionChangedEventArgs Change)> FieldCollectionChanges => _fieldCollectionChanges;

    /// <summary>The property changes of the fields, with the field that raised each.</summary>
    public IReadOnlyList<(DetailFieldViewModel Field, string Property)> FieldChanges => _fieldChanges;

    /// <summary>Every change of a collection (sections or fields) that is not a single Add or Remove: what the in-place update must never make.</summary>
    public IEnumerable<NotifyCollectionChangedEventArgs> ForbiddenCollectionChanges =>
        _sectionChanges.Concat(_fieldCollectionChanges.Select(change => change.Change)).Where(change => !IsSingleAddOrRemove(change));

    /// <summary>Forgets what was recorded; the pane, its sections and its fields stay followed.</summary>
    public void Clear()
    {
        _events.Clear();
        _sectionChanges.Clear();
        _fieldCollectionChanges.Clear();
        _fieldChanges.Clear();
        _paneProperties.Clear();
    }

    /// <summary>Stops recording.</summary>
    public void Dispose()
    {
        _detail.PropertyChanged -= OnPaneChanged;
        ((INotifyCollectionChanged)_detail.Sections).CollectionChanged -= OnSectionsChanged;
        foreach (DetailSectionViewModel section in _sections.Keys.ToArray())
        {
            Unfollow(section);
        }
    }

    /// <summary>True for a change that adds or removes exactly one item.</summary>
    public static bool IsSingleAddOrRemove(NotifyCollectionChangedEventArgs change)
    {
        return change.Action switch
        {
            NotifyCollectionChangedAction.Add => change.NewItems is { Count: 1 } && change.NewStartingIndex >= 0,
            NotifyCollectionChangedAction.Remove => change.OldItems is { Count: 1 } && change.OldStartingIndex >= 0,
            _ => false,
        };
    }

    private static string Describe(string collection, NotifyCollectionChangedEventArgs change)
    {
        return change.Action switch
        {
            NotifyCollectionChangedAction.Add => collection + " Add " + change.NewStartingIndex.ToString(CultureInfo.InvariantCulture),
            NotifyCollectionChangedAction.Remove => collection + " Remove " + change.OldStartingIndex.ToString(CultureInfo.InvariantCulture),
            _ => collection + " " + change.Action,
        };
    }

    private void OnPaneChanged(object? sender, PropertyChangedEventArgs e)
    {
        string name = e.PropertyName ?? string.Empty;
        _paneProperties.Add(name);
        _events.Add("Detail." + name);
    }

    private void OnSectionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _sectionChanges.Add(e);
        _events.Add(Describe("Sections", e));
        if (e.OldItems is not null)
        {
            foreach (DetailSectionViewModel section in e.OldItems)
            {
                Unfollow(section);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (DetailSectionViewModel section in e.NewItems)
            {
                Follow(section);
            }
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (DetailSectionViewModel section in _sections.Keys.ToArray())
            {
                Unfollow(section);
            }

            foreach (DetailSectionViewModel section in _detail.Sections)
            {
                Follow(section);
            }
        }
    }

    private void OnFieldsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DetailSectionViewModel section = _sectionOfFields[sender!];
        _fieldCollectionChanges.Add((section, e));
        _events.Add(Describe("Fields of " + section.Title, e));
        if (e.OldItems is not null)
        {
            foreach (DetailFieldViewModel field in e.OldItems)
            {
                Unfollow(field);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (DetailFieldViewModel field in e.NewItems)
            {
                Follow(field, section.Title);
            }
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (DetailFieldViewModel field in _sectionOfField.Where(pair => pair.Value == section.Title).Select(pair => pair.Key).ToArray())
            {
                Unfollow(field);
            }

            foreach (DetailFieldViewModel field in section.Fields)
            {
                Follow(field, section.Title);
            }
        }
    }

    private void OnFieldChanged(object? sender, PropertyChangedEventArgs e)
    {
        DetailFieldViewModel field = (DetailFieldViewModel)sender!;
        string name = e.PropertyName ?? string.Empty;
        _fieldChanges.Add((field, name));
        _events.Add(_sectionOfField[field] + "/" + field.Label + "." + name);
    }

    private void Follow(DetailSectionViewModel section)
    {
        if (!_sections.TryAdd(section, true))
        {
            return;
        }

        _sectionOfFields[section.Fields] = section;
        ((INotifyCollectionChanged)section.Fields).CollectionChanged += OnFieldsChanged;
        foreach (DetailFieldViewModel field in section.Fields)
        {
            Follow(field, section.Title);
        }
    }

    private void Unfollow(DetailSectionViewModel section)
    {
        if (!_sections.Remove(section))
        {
            return;
        }

        ((INotifyCollectionChanged)section.Fields).CollectionChanged -= OnFieldsChanged;
        _sectionOfFields.Remove(section.Fields);
        foreach (DetailFieldViewModel field in section.Fields)
        {
            Unfollow(field);
        }
    }

    private void Follow(DetailFieldViewModel field, string sectionTitle)
    {
        if (_sectionOfField.TryAdd(field, sectionTitle))
        {
            field.PropertyChanged += OnFieldChanged;
        }
    }

    private void Unfollow(DetailFieldViewModel field)
    {
        if (_sectionOfField.Remove(field))
        {
            field.PropertyChanged -= OnFieldChanged;
        }
    }
}
