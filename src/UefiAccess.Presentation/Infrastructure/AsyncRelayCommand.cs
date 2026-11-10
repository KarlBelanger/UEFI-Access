// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Windows.Input;

namespace UefiAccess.Presentation.Infrastructure;

/// <summary>
/// An <see cref="ICommand"/> over an asynchronous operation. While the operation runs,
/// <see cref="IsRunning"/> is true and <see cref="CanExecute"/> is false, so a bound button is
/// disabled and the operation cannot overlap itself. The command swallows nothing: an exception
/// thrown by the operation goes to the <c>onError</c> callback when one was given, otherwise it
/// propagates out of <see cref="ExecuteAsync"/> (and, through <see cref="Execute"/>, to the
/// caller's synchronization context, which in WPF is the application's unhandled-exception handler).
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private readonly Action<Exception>? _onError;
    private bool _isRunning;

    /// <summary>Creates the command.</summary>
    /// <param name="execute">The operation to run.</param>
    /// <param name="canExecute">Optional predicate; null means executable whenever the command is not running.</param>
    /// <param name="onError">Optional handler that receives an exception thrown by the operation; null lets it propagate.</param>
    /// <exception cref="ArgumentNullException"><paramref name="execute"/> is null.</exception>
    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null, Action<Exception>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        _canExecute = canExecute;
        _onError = onError;
    }

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <summary>True while the operation runs.</summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (_isRunning != value)
            {
                _isRunning = value;
                RaiseCanExecuteChanged();
            }
        }
    }

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => !_isRunning && (_canExecute is null || _canExecute());

    /// <summary>
    /// Starts the operation without awaiting it (the <see cref="ICommand"/> contract). Prefer
    /// <see cref="ExecuteAsync"/> from code and tests so the completion can be awaited.
    /// </summary>
    /// <param name="parameter">Ignored.</param>
    public async void Execute(object? parameter)
    {
        await ExecuteAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Runs the operation and returns its task. Does nothing when <see cref="CanExecute"/> is
    /// false. An exception is handed to the error callback when one was given, otherwise rethrown.
    /// </summary>
    /// <returns>A task that completes when the operation has finished.</returns>
    public async Task ExecuteAsync()
    {
        if (!CanExecute(null))
        {
            return;
        }

        IsRunning = true;
        try
        {
            await _execute().ConfigureAwait(true);
        }
        catch (Exception ex) when (_onError is not null)
        {
            _onError(ex);
        }
        finally
        {
            IsRunning = false;
        }
    }

    /// <summary>Raises <see cref="CanExecuteChanged"/> so bound controls re-query <see cref="CanExecute"/>.</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
