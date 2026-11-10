// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UefiAccess.Presentation.Infrastructure;

/// <summary>
/// Base class for view models: implements <see cref="INotifyPropertyChanged"/> with a
/// <see cref="SetProperty{T}"/> helper that raises the event only when the value changed.
/// No WPF dependency; the type comes from System.ObjectModel.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Assigns <paramref name="value"/> to <paramref name="field"/> and raises
    /// <see cref="PropertyChanged"/> when the two differ (by <see cref="EqualityComparer{T}.Default"/>).
    /// </summary>
    /// <typeparam name="T">The property type.</typeparam>
    /// <param name="field">The backing field.</param>
    /// <param name="value">The new value.</param>
    /// <param name="name">The property name; supplied by the compiler when omitted.</param>
    /// <returns>True when the value changed and the event was raised.</returns>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }

    /// <summary>Raises <see cref="PropertyChanged"/> for one property.</summary>
    /// <param name="name">The property name; supplied by the compiler when omitted. Null or empty means "every property".</param>
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
