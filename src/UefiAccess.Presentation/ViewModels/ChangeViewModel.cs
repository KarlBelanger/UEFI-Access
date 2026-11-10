// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Windows.Input;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Variables;
using UefiAccess.Presentation.Accessibility;
using UefiAccess.Presentation.Infrastructure;
using UefiAccess.Presentation.Services;

namespace UefiAccess.Presentation.ViewModels;

/// <summary>The Change group controls that start the checks and open the confirmation dialog.</summary>
public enum ChangeAction
{
    /// <summary>Review change (Alt+V): the proposed boot order.</summary>
    ReviewChange,

    /// <summary>Boot once from this entry (Alt+B): BootNext to the selected Boot####.</summary>
    BootOnce,

    /// <summary>Clear one-time boot (Alt+M): the removal of BootNext.</summary>
    ClearBootNext,

    /// <summary>Restore previous value (Alt+P): the newest snapshot of BootOrder or BootNext.</summary>
    RestorePrevious,
}

/// <summary>
/// The Change group of the detail pane for the selected variable: which controls it shows, the proposed boot order, and the commands of its buttons. The
/// group is shown only for the live source and only for BootOrder (the Boot order entries list,
/// Move up, Move down, Review change), an active category-Boot Boot#### (Boot once from this
/// entry) and BootNext (Clear one-time boot), each only while its Writability verdict lets this
/// application change or choose it (for BootOrder and BootNext: changeable, or a machine that is not
/// the M3 test unit, where Confirm change refuses; never Malformed or an authenticated write);
/// Restore previous value joins BootOrder's and
/// BootNext's controls when a snapshot of that variable exists. Every command is enabled while its
/// control is shown and no check or write runs (a disabled button would swallow its access key,
/// so an answer that changes nothing is a sentence, not a disabled button); the moves also need a
/// selected row. A move and Ctrl+Z are answered here, through the announcer the shell passes; the
/// four controls that open the dialog hand their action to the shell, which runs the checks, the
/// dialog and the commit (<see cref="ShellViewModel"/>). While the checks run,
/// <see cref="CancelChecks"/> (Escape) cancels them between stages.
/// </summary>
public sealed class ChangeViewModel : ObservableObject
{
    private readonly IWriteService? _writes;
    private readonly Func<ChangeAction, Task> _start;
    private readonly Action<string> _announce;
    private readonly Action<FocusTarget> _requestFocus;
    private readonly RelayCommand _moveUp;
    private readonly RelayCommand _moveDown;
    private readonly RelayCommand _resetOrder;
    private readonly AsyncRelayCommand _reviewChange;
    private readonly AsyncRelayCommand _bootOnce;
    private readonly AsyncRelayCommand _clearBootNext;
    private readonly AsyncRelayCommand _restorePrevious;
    private readonly RelayCommand _cancelChecks;
    private IVariableLookup? _variables;
    private bool _isLive;
    private VariableItemViewModel? _item;
    private bool _showsBootOrderControls;
    private bool _showsBootOnce;
    private bool _showsClearBootNext;
    private bool _showsRestore;
    private bool _isChecking;
    private bool _isWriting;
    private CancellationTokenSource? _checks;
    private ChangeAction? _lastAction;

    /// <summary>Creates a Change group that never shows: no write pipeline is composed behind it (the detail pane of a test, or a shell without the write service).</summary>
    public ChangeViewModel()
        : this(null, null, null, null)
    {
    }

    /// <summary>Creates the Change group over the write service and the shell's callbacks.</summary>
    /// <param name="writes">The write service; null means no change can be made, and the group never shows.</param>
    /// <param name="start">Runs the checks, the dialog and the commit of an action (the shell's flow); null does nothing.</param>
    /// <param name="announce">Announces a sentence once, through the shell's announcer; null says nothing.</param>
    /// <param name="requestFocus">Asks the view to move focus; null asks nothing.</param>
    public ChangeViewModel(IWriteService? writes, Func<ChangeAction, Task>? start, Action<string>? announce, Action<FocusTarget>? requestFocus)
    {
        _writes = writes;
        _start = start ?? (_ => Task.CompletedTask);
        _announce = announce ?? (_ => { });
        _requestFocus = requestFocus ?? (_ => { });
        Editor = new BootOrderEditorViewModel();
        _moveUp = new RelayCommand(MoveUp, CanMove);
        _moveDown = new RelayCommand(MoveDown, CanMove);
        _resetOrder = new RelayCommand(ResetOrder, () => _showsBootOrderControls && IsIdle);
        _reviewChange = new AsyncRelayCommand(() => _start(ChangeAction.ReviewChange), () => _showsBootOrderControls && IsIdle);
        _bootOnce = new AsyncRelayCommand(() => _start(ChangeAction.BootOnce), () => _showsBootOnce && IsIdle);
        _clearBootNext = new AsyncRelayCommand(() => _start(ChangeAction.ClearBootNext), () => _showsClearBootNext && IsIdle);
        _restorePrevious = new AsyncRelayCommand(() => _start(ChangeAction.RestorePrevious), () => _showsRestore && IsIdle);
        _cancelChecks = new RelayCommand(() => CancelChecks(), () => _isChecking);
        Editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BootOrderEditorViewModel.SelectedEntry))
            {
                _moveUp.RaiseCanExecuteChanged();
                _moveDown.RaiseCanExecuteChanged();
            }
        };
    }

    /// <summary>The proposed boot order (the Boot order entries list binds ItemsSource to <see cref="BootOrderEditorViewModel.Entries"/> and SelectedItem to <see cref="BootOrderEditorViewModel.SelectedEntry"/>).</summary>
    public BootOrderEditorViewModel Editor { get; }

    /// <summary>The variable the group is shown for, or null.</summary>
    public VariableItemViewModel? Item => _item;

    /// <summary>True when the Change group is shown: the live source, and BootOrder, an active category-Boot Boot#### or BootNext whose verdict is not read-only.</summary>
    public bool IsVisible => _showsBootOrderControls || _showsBootOnce || _showsClearBootNext;

    /// <summary>True when the Boot order entries list, Move up, Move down and Review change are shown (BootOrder on the live source, verdict ReorderOnly or WrongMachine).</summary>
    public bool ShowsBootOrderControls
    {
        get => _showsBootOrderControls;
        private set => SetProperty(ref _showsBootOrderControls, value);
    }

    /// <summary>True when Boot once from this entry is shown (an active, category-Boot Boot#### on the live source).</summary>
    public bool ShowsBootOnce
    {
        get => _showsBootOnce;
        private set => SetProperty(ref _showsBootOnce, value);
    }

    /// <summary>True when Clear one-time boot is shown (BootNext on the live source, verdict OneTimeBoot or WrongMachine).</summary>
    public bool ShowsClearBootNext
    {
        get => _showsClearBootNext;
        private set => SetProperty(ref _showsClearBootNext, value);
    }

    /// <summary>True when Restore previous value is shown (BootOrder or BootNext on the live source, with a snapshot of it).</summary>
    public bool ShowsRestore
    {
        get => _showsRestore;
        private set => SetProperty(ref _showsRestore, value);
    }

    /// <summary>True while the checks of a change run (every Change command is disabled; Escape cancels them).</summary>
    public bool IsChecking
    {
        get => _isChecking;
        private set
        {
            if (SetProperty(ref _isChecking, value))
            {
                RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>True while a confirmed change is written and until its result has been said (the shell's <see cref="ShellViewModel.IsWriting"/> or <see cref="ShellViewModel.IsFinishingChange"/>; every Change command is disabled).</summary>
    public bool IsWriting
    {
        get => _isWriting;
        internal set
        {
            if (SetProperty(ref _isWriting, value))
            {
                RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>The control that started the last checks: the button focus returns to after the dialog was cancelled (<see cref="FocusTarget.ChangeControl"/>); null before the first.</summary>
    public ChangeAction? LastAction
    {
        get => _lastAction;
        private set => SetProperty(ref _lastAction, value);
    }

    /// <summary>Move up (Alt+U): moves the selected entry one position up and announces its new position, or that it is already first.</summary>
    public ICommand MoveUpCommand => _moveUp;

    /// <summary>Move down (Alt+D): moves the selected entry one position down and announces its new position, or that it is already last.</summary>
    public ICommand MoveDownCommand => _moveDown;

    /// <summary>Ctrl+Z in the Boot order entries list: puts the firmware's order back, announces it and asks for focus on the selected row.</summary>
    public ICommand ResetOrderCommand => _resetOrder;

    /// <summary>Review change (Alt+V): the checks of the proposed order, then the dialog.</summary>
    public ICommand ReviewChangeCommand => _reviewChange;

    /// <summary>Boot once from this entry (Alt+B): the checks of a one-time boot to the shown entry, then the dialog.</summary>
    public ICommand BootOnceCommand => _bootOnce;

    /// <summary>Clear one-time boot (Alt+M): the checks of the removal of BootNext, then the dialog.</summary>
    public ICommand ClearBootNextCommand => _clearBootNext;

    /// <summary>Restore previous value (Alt+P): the checks of the newest snapshot's value, then the dialog.</summary>
    public ICommand RestorePreviousCommand => _restorePrevious;

    /// <summary>Escape while the checks run: cancels them between stages (enabled only while they run).</summary>
    public ICommand CancelChecksCommand => _cancelChecks;

    private bool IsIdle => !_isChecking && !_isWriting;

    /// <summary>
    /// Takes the variables of a new load: the proposed order becomes the firmware's BootOrder again
    /// (for the live source with a write service; a baseline or an empty list leaves the editor
    /// empty), and the group is worked out again for the variable it was shown for, until the
    /// detail pane shows the new row.
    /// </summary>
    /// <param name="variables">The variables loaded, or null for none.</param>
    /// <param name="live">True for the live source; false for a baseline folder or a load that gave nothing.</param>
    public void Load(IVariableLookup? variables, bool live)
    {
        _variables = variables;
        _isLive = live;
        if (live && variables is not null && _writes is not null)
        {
            Editor.Load(variables.Find(Whitelist.BootOrder.Name, Whitelist.BootOrder.Guid), variables);
        }
        else
        {
            Editor.Clear();
        }

        Show(_item);
    }

    /// <summary>
    /// Works out which controls to show for a variable (the detail pane calls this from its Show).
    /// Restore previous value asks the write service for the newest snapshot of the variable, which
    /// reads the snapshot root and composes nothing.
    /// </summary>
    /// <param name="item">The row shown, or null.</param>
    public void Show(VariableItemViewModel? item)
    {
        _item = item;
        bool live = _isLive && _writes is not null && item is not null;

        // BootOrder and BootNext follow their verdict, as the Boot#### entries do through
        // IsOneTimeBootTarget: a value the Writability line says cannot be changed (Malformed, or an
        // authenticated-write attribute) shows no controls; on a machine that is not the M3 test
        // unit the controls work up to Confirm change, which refuses.
        bool bootOrder = live && item is not null && Is(item, Whitelist.BootOrder) && item.Writability.Verdict is Writability.ReorderOnly or Writability.WrongMachine;
        bool bootNext = live && item is not null && Is(item, Whitelist.BootNext) && item.Writability.Verdict is Writability.OneTimeBoot or Writability.WrongMachine;
        ShowsBootOrderControls = bootOrder;
        ShowsClearBootNext = bootNext;
        ShowsBootOnce = live && item is not null && IsOneTimeBootTarget(item);
        ShowsRestore = (bootOrder || bootNext) && item is not null && _writes?.Newest(item.Key) is not null;
        OnPropertyChanged(nameof(Item));
        OnPropertyChanged(nameof(IsVisible));
        RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Escape while the checks run: cancels them. The shell hears "Cancelled; nothing changed." as
    /// soon as the write service returns; the checks themselves stop at their next stage boundary.
    /// The shell also calls this when a load replaces the list while the checks run.
    /// </summary>
    /// <returns>True when checks were running and are now cancelled; false otherwise (Escape keeps its other meaning then).</returns>
    public bool CancelChecks()
    {
        CancellationTokenSource? checks = _checks;
        if (!_isChecking || checks is null || checks.IsCancellationRequested)
        {
            return false;
        }

        checks.Cancel();
        return true;
    }

    /// <summary>The checks of <paramref name="action"/> start: every command is disabled until <see cref="EndChecking"/>.</summary>
    /// <param name="action">The control that started them.</param>
    /// <returns>The token Escape cancels.</returns>
    internal CancellationToken BeginChecking(ChangeAction action)
    {
        _checks?.Dispose();
        _checks = new CancellationTokenSource();
        LastAction = action;
        IsChecking = true;
        return _checks.Token;
    }

    /// <summary>
    /// True when the control of <paramref name="action"/> is shown for the variable shown now:
    /// Review change with BootOrder's controls, Boot once from this entry, Clear one-time boot and
    /// Restore previous value each with its own. The shell asks this after it has brought the pane
    /// up to date, because a command reached while the pane still showed an earlier row was enabled
    /// for that row, not for the selected one.
    /// </summary>
    /// <param name="action">The control that was pressed.</param>
    /// <returns>True when that control is shown.</returns>
    internal bool Offers(ChangeAction action)
    {
        return action switch
        {
            ChangeAction.ReviewChange => _showsBootOrderControls,
            ChangeAction.BootOnce => _showsBootOnce,
            ChangeAction.ClearBootNext => _showsClearBootNext,
            _ => _showsRestore,
        };
    }

    /// <summary>The checks have answered (or were cancelled): the commands are enabled again.</summary>
    internal void EndChecking()
    {
        CancellationTokenSource? checks = _checks;
        _checks = null;
        IsChecking = false;
        checks?.Dispose();
    }

    private static bool Is(VariableItemViewModel item, WhitelistEntry entry)
    {
        return item.Guid == entry.Guid && string.Equals(item.Name, entry.Name, StringComparison.Ordinal);
    }

    /// <summary>An active, category-Boot Boot#### that parses: exactly the entries Core's policy offers for one boot (the verdict on the M3 test unit).</summary>
    private bool IsOneTimeBootTarget(VariableItemViewModel item)
    {
        return item.Guid == KnownGuids.EfiGlobalVariable
            && KnownVariableNames.IsBootEntryName(item.Name, out _)
            && WritabilityPolicy.Describe(item.Variable, _variables, offline: false, machineConfirmed: true).Verdict == Writability.OneTimeBootTarget;
    }

    private bool CanMove() => _showsBootOrderControls && IsIdle && Editor.SelectedEntry is not null;

    private void MoveUp() => AnnounceIfAny(Editor.MoveUp());

    private void MoveDown() => AnnounceIfAny(Editor.MoveDown());

    private void ResetOrder()
    {
        _announce(Editor.ResetToFirmwareOrder());
        _requestFocus(FocusTarget.BootOrderEntry);
    }

    private void AnnounceIfAny(string sentence)
    {
        if (!string.IsNullOrEmpty(sentence))
        {
            _announce(sentence);
        }
    }

    private void RaiseCanExecuteChanged()
    {
        _moveUp.RaiseCanExecuteChanged();
        _moveDown.RaiseCanExecuteChanged();
        _resetOrder.RaiseCanExecuteChanged();
        _reviewChange.RaiseCanExecuteChanged();
        _bootOnce.RaiseCanExecuteChanged();
        _clearBootNext.RaiseCanExecuteChanged();
        _restorePrevious.RaiseCanExecuteChanged();
        _cancelChecks.RaiseCanExecuteChanged();
    }
}
