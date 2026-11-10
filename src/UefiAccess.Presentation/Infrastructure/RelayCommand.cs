// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Windows.Input;

namespace UefiAccess.Presentation.Infrastructure;

/// <summary>
/// An <see cref="ICommand"/> that forwards to delegates. The parameterless overloads ignore the
/// command parameter; the parameterised overloads receive it. <see cref="CanExecuteChanged"/> is
/// raised only through <see cref="RaiseCanExecuteChanged"/> (no CommandManager, no WPF dependency),
/// so a view model must call it after a state change that affects <see cref="CanExecute"/>.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    /// <summary>Creates a command over a parameterless action.</summary>
    /// <param name="execute">The action to run.</param>
    /// <param name="canExecute">Optional predicate; null means always executable.</param>
    /// <exception cref="ArgumentNullException"><paramref name="execute"/> is null.</exception>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = _ => execute();
        _canExecute = canExecute is null ? null : _ => canExecute();
    }

    /// <summary>Creates a command over an action that receives the command parameter.</summary>
    /// <param name="execute">The action to run.</param>
    /// <param name="canExecute">Optional predicate over the command parameter; null means always executable.</param>
    /// <exception cref="ArgumentNullException"><paramref name="execute"/> is null.</exception>
    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        _canExecute = canExecute;
    }

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => _canExecute is null || _canExecute(parameter);

    /// <inheritdoc />
    public void Execute(object? parameter) => _execute(parameter);

    /// <summary>Raises <see cref="CanExecuteChanged"/> so bound controls re-query <see cref="CanExecute"/>.</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
