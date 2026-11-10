// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;

namespace UefiAccess.Presentation.Resources;

/// <summary>
/// Every user-visible string of the GUI in one place (no localization yet, but nothing is
/// hard-coded in XAML). XAML reads them with <c>{x:Static res:Strings.Name}</c>. Button labels
/// carry their access-key marker ("_Refresh" is Alt+R); the matching <c>*Name</c> constants are
/// the same labels without the marker, for <c>AutomationProperties.Name</c>. The two precondition
/// sentences are copied verbatim from the CLI's Preconditions class so the GUI and the CLI refuse
/// with identical words (the GUI must not reference the CLI project). Counts are spoken text, so
/// the count helpers (<see cref="Items"/>, <see cref="Variables"/>, <see cref="VisibleVariables"/>)
/// choose the singular for 1 ("1 item", "1 variable") the way the row sentence says "1 byte".
/// </summary>
public static class Strings
{
    // ---- Window and sources --------------------------------------------------------------

    /// <summary>The window title and the product name.</summary>
    public const string WindowTitle = "UefiAccess";

    /// <summary>Description of the live firmware source.</summary>
    public const string LiveSourceName = "Live firmware";

    /// <summary>Prefix of every description of an offline baseline source; the folder path follows.</summary>
    public const string BaselinePrefix = "Baseline (offline): ";

    /// <summary>Separator between <see cref="WindowTitle"/> and the source description in the window title.</summary>
    public const string WindowTitleSeparator = " – ";

    // ---- Refusal and status sentences ----------------------------------------------------

    /// <summary>The sentence shown when the machine did not boot in UEFI mode or the firmware type is unknown (verbatim from the CLI).</summary>
    public const string NotUefiSentence =
        "This system did not boot in UEFI mode (or the OS reports no UEFI runtime services); firmware variables are not reachable from Windows.";

    /// <summary>The sentence shown when the process is not elevated (verbatim from the CLI).</summary>
    public const string NotElevatedSentence =
        "This command needs administrator rights; open an elevated command prompt (Run as administrator) and run uefiaccess again.";

    /// <summary>The sentence shown when the process is elevated but SeSystemEnvironmentPrivilege could not be enabled.</summary>
    public const string PrivilegeAbsentSentence =
        "The process is elevated but SeSystemEnvironmentPrivilege could not be enabled; start UefiAccess as administrator from an account that holds that privilege.";

    /// <summary>Status text while a source is loading.</summary>
    public const string LoadingStatus = "Loading variables, please wait.";

    /// <summary>Status text before the first load.</summary>
    public const string IdleStatus = "Ready. Press Refresh (Alt+R) to read the firmware variables, or Open baseline folder (Alt+O) to view a baseline offline.";

    /// <summary>Status format after a successful load: {0} the count as text (<see cref="Variables"/>: "1 variable", "63 variables"), {1} source description, {2} enumeration method.</summary>
    public const string LoadedFormat = "Loaded {0} from {1} ({2})";

    /// <summary>Status format after a failed load: {0} the failure text.</summary>
    public const string FailedFormat = "Loading failed: {0}";

    /// <summary>Status format for an unhandled exception anywhere in the application (the application's dispatcher handler): {0} the exception message. Not a load failure: the state and the list are untouched.</summary>
    public const string UnexpectedErrorFormat = "Unexpected error: {0}. Details are in the log.";

    /// <summary>Status format after filtering: {0} visible count, {1} the total count as text (<see cref="Variables"/>: "Showing 1 of 1 variable", "Showing 12 of 63 variables").</summary>
    public const string ShowingFormat = "Showing {0} of {1}";

    /// <summary>Joins two status sentences into one announcement: the Loaded sentence and the Showing sentence when a filter was in effect during the load ("Loaded 63 variables from Live firmware (NtEnumerate). Showing 7 of 63 variables").</summary>
    public const string SentenceSeparator = ". ";

    /// <summary>Announcement format when F6 or Shift+F6 arrives in a region: {0} the region name.</summary>
    public const string RegionArrivedFormat = "{0} region";

    /// <summary>Status text after a value was copied to the clipboard.</summary>
    public const string CopiedValueStatus = "Value copied to the clipboard.";

    /// <summary>Status text after the log was copied to the clipboard.</summary>
    public const string CopiedLogStatus = "Log copied to the clipboard.";

    /// <summary>Status text when Copy value was invoked without a value.</summary>
    public const string NothingToCopyStatus = "Nothing to copy: select a variable first.";

    /// <summary>Status format after the visible list was exported: {0} the row count as text (<see cref="VisibleVariables"/>: "1 visible variable", "12 visible variables"), {1} file path.</summary>
    public const string ExportedFormat = "Exported {0} to {1}";

    /// <summary>Status format when a clipboard copy (Copy value, Copy log) failed: {0} the failure text.</summary>
    public const string CopyFailedFormat = "Copy failed: {0}";

    /// <summary>Status text when a folder or file dialog was cancelled.</summary>
    public const string DialogCancelledStatus = "Cancelled; nothing changed.";

    /// <summary>Status format when the folder picker of Open baseline folder failed: {0} the failure text without its closing period. Nothing was loaded: the state, the list, the title and the focus stay as they were.</summary>
    public const string OpenBaselineFailedFormat = "Open baseline folder failed: {0}. Nothing changed.";

    /// <summary>
    /// Status format when the folder picker of Open baseline folder failed after a load finished
    /// behind it: {0} the failure text without its closing period. That load did change the list,
    /// the title and the identity, so there is no "Nothing changed."; the load's own sentence
    /// follows (". Loaded N variables from ...").
    /// </summary>
    public const string OpenBaselineFailedAfterLoadFormat = "Open baseline folder failed: {0}";

    // ---- Shell state names ---------------------------------------------------------------

    /// <summary>State text for <c>ShellState.Idle</c>.</summary>
    public const string StateIdle = "Idle";

    /// <summary>State text for <c>ShellState.Loading</c>.</summary>
    public const string StateLoading = "Loading";

    /// <summary>State text for <c>ShellState.Loaded</c>.</summary>
    public const string StateLoaded = "Loaded";

    /// <summary>State text for <c>ShellState.Refused</c>.</summary>
    public const string StateRefused = "Refused";

    /// <summary>State text for <c>ShellState.Failed</c>.</summary>
    public const string StateFailed = "Failed";

    // ---- Toolbar buttons: label with access key, automation name, help text ---------------

    /// <summary>Refresh button label (Alt+R).</summary>
    public const string RefreshLabel = "_Refresh";

    /// <summary>Refresh button automation name.</summary>
    public const string RefreshName = "Refresh";

    /// <summary>Refresh button help text.</summary>
    public const string RefreshHelp = "Reads the firmware variables from the live machine again. Access key Alt+R. When it finishes, focus moves to the first row of the list and Status announces the count; if the machine refuses or the read fails, focus moves to Status, which says why.";

    /// <summary>Open baseline folder button label (Alt+O).</summary>
    public const string OpenBaselineLabel = "_Open baseline folder";

    /// <summary>Open baseline folder button automation name.</summary>
    public const string OpenBaselineName = "Open baseline folder";

    /// <summary>Open baseline folder button help text.</summary>
    public const string OpenBaselineHelp = "Opens a baseline folder captured with the uefiaccess baseline command and shows its variables offline, read-only. Also works while Refresh is still reading; the baseline then replaces that read. Access key Alt+O.";

    /// <summary>Clear filter button label (Alt+C).</summary>
    public const string ClearFilterLabel = "_Clear filter";

    /// <summary>Clear filter button automation name.</summary>
    public const string ClearFilterName = "Clear filter";

    /// <summary>Clear filter button help text.</summary>
    public const string ClearFilterHelp = "Empties the filter box so every variable is listed again. Access key Alt+C.";

    /// <summary>Copy value button label (Alt+Y).</summary>
    public const string CopyValueLabel = "Cop_y value";

    /// <summary>Copy value button automation name.</summary>
    public const string CopyValueName = "Copy value";

    /// <summary>Copy value button help text.</summary>
    public const string CopyValueHelp = "Copies the selected detail field, or the selected variable's summary, to the clipboard. Access key Alt+Y.";

    /// <summary>Export visible list button label (Alt+X).</summary>
    public const string ExportVisibleListLabel = "E_xport visible list";

    /// <summary>Export visible list button automation name.</summary>
    public const string ExportVisibleListName = "Export visible list";

    /// <summary>Export visible list button help text.</summary>
    public const string ExportVisibleListHelp = "Exports every row that passes the filter when you press it, rows in collapsed groups included, as plain text or JSON to a file you choose. The firmware is never touched. Access key Alt+X.";

    /// <summary>Copy log button label (Alt+L).</summary>
    public const string CopyLogLabel = "Copy _log";

    /// <summary>Copy log button automation name.</summary>
    public const string CopyLogName = "Copy log";

    /// <summary>Copy log button help text.</summary>
    public const string CopyLogHelp = "Copies every line of the log panel, each with its full date and time, to the clipboard. Access key Alt+L.";

    // ---- Access keys as screen readers speak them (AutomationProperties.AccessKey and AcceleratorKey)
    // Without these WPF reports the bare letter ("R"), which does nothing alone: the window requires Alt.

    /// <summary>Refresh button access key as spoken.</summary>
    public const string RefreshAccessKey = "Alt+R";

    /// <summary>Open baseline folder button access key as spoken.</summary>
    public const string OpenBaselineAccessKey = "Alt+O";

    /// <summary>Clear filter button access key as spoken.</summary>
    public const string ClearFilterAccessKey = "Alt+C";

    /// <summary>Copy value button access key as spoken.</summary>
    public const string CopyValueAccessKey = "Alt+Y";

    /// <summary>Export visible list button access key as spoken.</summary>
    public const string ExportVisibleListAccessKey = "Alt+X";

    /// <summary>Copy log button access key as spoken.</summary>
    public const string CopyLogAccessKey = "Alt+L";

    // ---- Filter row ----------------------------------------------------------------------

    /// <summary>Filter box automation name.</summary>
    public const string FilterName = "Filter variables";

    /// <summary>Filter box help text.</summary>
    public const string FilterHelp = "Type to narrow the list. Matches the name, GUID, namespace label, kind and decoded summary, case-insensitive, as you type. Ctrl+F focuses this box; Escape clears it and returns to the list.";

    /// <summary>Group by combo box automation name.</summary>
    public const string GroupByName = "Group by";

    /// <summary>Group by combo box help text.</summary>
    public const string GroupByHelp = "Chooses how the list is grouped: by namespace, by kind, or not at all. Group headers announce their item count.";

    /// <summary>Sort by combo box automation name.</summary>
    public const string SortByName = "Sort by";

    /// <summary>Sort by combo box help text.</summary>
    public const string SortByHelp = "Chooses the order of the rows inside each group: by name, namespace, size or kind.";

    /// <summary>Group by option: namespace.</summary>
    public const string GroupByNamespace = "Namespace";

    /// <summary>Group by option: kind.</summary>
    public const string GroupByKind = "Kind";

    /// <summary>Group by option: none.</summary>
    public const string GroupByNone = "None";

    /// <summary>Sort by option: name.</summary>
    public const string SortByNameOption = "Name";

    /// <summary>Sort by option: namespace.</summary>
    public const string SortByNamespace = "Namespace";

    /// <summary>Sort by option: size.</summary>
    public const string SortBySize = "Size";

    /// <summary>Sort by option: kind.</summary>
    public const string SortByKind = "Kind";

    // ---- Variable list -------------------------------------------------------------------

    /// <summary>Variable list automation name.</summary>
    public const string VariableListName = "Variables";

    /// <summary>
    /// Variable list help text (on the list containers, not on the rows: a screen reader that
    /// reads help text on focus would otherwise repeat it after every row sentence). Kept under
    /// 200 characters. Page Up, Page Down and type-ahead are the stock control's and work inside
    /// the current group; Home and End span the whole list (code-behind).
    /// </summary>
    public const string VariableListHelp = "One row per firmware variable. Arrows move, Home and End jump to the first and last row, Page Up, Page Down and typing a name work inside the group, Enter opens the details, Ctrl+C copies the key.";

    /// <summary>Group header help text (on each Expander): short, because a screen reader may read it after the header name on every arrival.</summary>
    public const string GroupHeaderHelp = "Group header. Left collapses, Right expands, Down moves to the first row, Up to the previous group.";

    /// <summary>Column header: name.</summary>
    public const string ColumnName = "Name";

    /// <summary>Column header: namespace.</summary>
    public const string ColumnNamespace = "Namespace";

    /// <summary>Column header: flags.</summary>
    public const string ColumnFlags = "Flags";

    /// <summary>Column header: size.</summary>
    public const string ColumnSize = "Size";

    /// <summary>Column header: kind.</summary>
    public const string ColumnKind = "Kind";

    /// <summary>Column header: summary.</summary>
    public const string ColumnSummary = "Summary";

    /// <summary>Group header format: {0} label, {1} the item count as text (<see cref="Items"/>: "EFI global variable, 41 items", "Raw, 1 item").</summary>
    public const string GroupHeaderFormat = "{0}, {1}";

    /// <summary>
    /// Automation name of the grid of rows inside a group, by the label alone: {0} label ("EFI
    /// global variable rows"). The header sentence with the count belongs to the group header,
    /// spoken just before it, and is not repeated.
    /// </summary>
    public const string GroupRowsFormat = "{0} rows";

    /// <summary>Kind shown for a variable no decoder accepts.</summary>
    public const string RawKind = "Raw";

    /// <summary>Namespace label for a vendor GUID the catalog does not know; the GUID follows.</summary>
    public const string UnknownVendorLabel = "Unknown vendor";

    // ---- Detail pane ---------------------------------------------------------------------

    /// <summary>Detail pane automation name.</summary>
    public const string DetailPaneName = "Variable details";

    /// <summary>Detail pane help text (on the pane and its heading).</summary>
    public const string DetailPaneHelp = "Read-only fields for the selected variable. Tab moves from field to field; Escape returns to the list at the same row.";

    /// <summary>
    /// Help text of every detail field but a long value (<see cref="LongDetailFieldHelpFormat"/>) and
    /// of the Writability line: short, because a screen reader may read it after the field name on
    /// every Tab. It does not say that the box is read-only: the screen reader says so from the box's
    /// own state, and the word in the help text was heard a second time ("read only Read-only";
    /// Alpha 2 finding A2-6, the tester's A2-4).
    /// </summary>
    public const string DetailFieldHelp = "Escape returns to the list at the same row.";

    /// <summary>
    /// Help text of a single-line detail field whose value is too long for a screen reader to read
    /// as a selection (<c>VariableDetailViewModel.LongValueLength</c> characters or more, so it is
    /// not selected on arrival): {0} its length. Said before the value's first line on screen, so
    /// the listener knows the value goes on and how to hear the rest. Like <see cref="DetailFieldHelp"/>,
    /// it leaves the read-only state to the screen reader (A2-6).
    /// </summary>
    public const string LongDetailFieldHelpFormat = "Long value, {0} characters: Down arrow reads it line by line. Escape returns to the list at the same row.";

    /// <summary>Detail heading when nothing is selected.</summary>
    public const string NoSelectionHeading = "No variable selected";

    /// <summary>Detail section title: identity.</summary>
    public const string SectionIdentity = "Identity";

    /// <summary>Detail section title: attributes.</summary>
    public const string SectionAttributes = "Attributes";

    /// <summary>Detail section title: size.</summary>
    public const string SectionSize = "Size";

    /// <summary>Detail section title: decoded fields.</summary>
    public const string SectionDecoded = "Decoded";

    /// <summary>Detail section title: raw hex dump.</summary>
    public const string SectionRaw = "Raw";

    /// <summary>Automation name of the hex dump box.</summary>
    public const string HexDumpName = "Raw data, hex dump";

    /// <summary>Automation name of the read-only status line in the detail pane.</summary>
    public const string ReadOnlyStatusName = "Writability";

    // ---- Writability line and row status ----------------

    /// <summary>
    /// The firmware-setup hand-off of section 3.7 (deviation D4): appended to the Writability line
    /// of SecureBoot, SetupMode, PK, KEK, db and dbx, and quoted in the keyboard map. Text only; the
    /// application launches nothing and writes no variable for it.
    /// </summary>
    public const string FirmwareSetupHint = "To change Secure Boot: suspend BitLocker first (the recovery key must be at hand), restart, press F2 at the Dell logo, then open Security, Secure Boot. This application only reports it.";

    /// <summary>The words the BootOrder row's sentence carries right after its name while it is changeable (Core's item status, as the sentence continues it).</summary>
    public const string RowStatusOrderOfEntries = "changeable: order of entries";

    /// <summary>The words the BootNext row's sentence carries right after its name while it is changeable (Core's item status, as the sentence continues it).</summary>
    public const string RowStatusOneTimeBoot = "changeable: one-time boot";

    // ---- Change group in the detail pane --------------------------

    /// <summary>Header and automation name of the Change group.</summary>
    public const string ChangeGroupName = "Change";

    /// <summary>Help text of the Change group header: under 120 characters, because it may be read after the header on every arrival.</summary>
    public const string ChangeGroupHelp = "Controls that change this variable through the write pipeline. Nothing is written until you confirm in a dialog.";

    /// <summary>Automation name of the list of boot order entries (the proposed order).</summary>
    public const string BootOrderEntriesName = "Boot order entries";

    /// <summary>Help text of the list of boot order entries: under 200 characters, because it is heard on every Tab into the list.</summary>
    public const string BootOrderEntriesHelp = "One row per entry, in the proposed order. Up and Down select. Alt+U and Alt+D move the selected entry. Ctrl+Z restores the firmware's order. Nothing is written until you confirm.";

    /// <summary>A row of the list of boot order entries: {0} the Boot#### name, {1} its description. The position is the item peer's, not part of the name.</summary>
    public const string BootOrderEntryFormat = "{0}, {1}";

    /// <summary>Appended to a row of the list of boot order entries whose load option is not active.</summary>
    public const string BootOrderEntryInactive = ", inactive";

    /// <summary>Appended to a row of the list of boot order entries whose load option is hidden.</summary>
    public const string BootOrderEntryHidden = ", hidden";

    /// <summary>Move up button label (Alt+U).</summary>
    public const string MoveUpLabel = "Move _up";

    /// <summary>Move up button automation name.</summary>
    public const string MoveUpName = "Move up";

    /// <summary>Move up button help text.</summary>
    public const string MoveUpHelp = "Moves the selected boot entry one position up in the proposed order and announces its new position. Nothing is written yet. Access key Alt+U.";

    /// <summary>Move up button access key as spoken.</summary>
    public const string MoveUpAccessKey = "Alt+U";

    /// <summary>Move down button label (Alt+D).</summary>
    public const string MoveDownLabel = "Move _down";

    /// <summary>Move down button automation name.</summary>
    public const string MoveDownName = "Move down";

    /// <summary>Move down button help text.</summary>
    public const string MoveDownHelp = "Moves the selected boot entry one position down in the proposed order and announces its new position. Nothing is written yet. Access key Alt+D.";

    /// <summary>Move down button access key as spoken.</summary>
    public const string MoveDownAccessKey = "Alt+D";

    /// <summary>Review change button label (Alt+V).</summary>
    public const string ReviewChangeLabel = "Re_view change";

    /// <summary>Review change button automation name.</summary>
    public const string ReviewChangeName = "Review change";

    /// <summary>Review change button help text (section 5.2's text, shortened to stay under 200 characters).</summary>
    public const string ReviewChangeHelp = "Checks the proposed order, then opens the confirmation dialog: order before and after, BitLocker warning, snapshot folder. Nothing is written before Confirm change. Access key Alt+V.";

    /// <summary>Review change button access key as spoken.</summary>
    public const string ReviewChangeAccessKey = "Alt+V";

    /// <summary>Boot once from this entry button label (Alt+B).</summary>
    public const string BootOnceLabel = "_Boot once from this entry";

    /// <summary>Boot once from this entry button automation name.</summary>
    public const string BootOnceName = "Boot once from this entry";

    /// <summary>Boot once from this entry button help text.</summary>
    public const string BootOnceHelp = "Asks the firmware to start this entry once, at the next restart only, after you confirm in a dialog. Access key Alt+B.";

    /// <summary>Boot once from this entry button access key as spoken.</summary>
    public const string BootOnceAccessKey = "Alt+B";

    /// <summary>Clear one-time boot button label (Alt+M).</summary>
    public const string ClearBootNextLabel = "Clear one-ti_me boot";

    /// <summary>Clear one-time boot button automation name.</summary>
    public const string ClearBootNextName = "Clear one-time boot";

    /// <summary>Clear one-time boot button help text.</summary>
    public const string ClearBootNextHelp = "Removes the one-time boot choice so the next restart uses the normal order, after you confirm in a dialog. Access key Alt+M.";

    /// <summary>Clear one-time boot button access key as spoken.</summary>
    public const string ClearBootNextAccessKey = "Alt+M";

    /// <summary>Restore previous value button label (Alt+P).</summary>
    public const string RestorePreviousLabel = "Restore _previous value";

    /// <summary>Restore previous value button automation name.</summary>
    public const string RestorePreviousName = "Restore previous value";

    /// <summary>Restore previous value button help text.</summary>
    public const string RestorePreviousHelp = "Puts back the value recorded in the newest snapshot of this variable, through the same checks and the same dialog as any change. Access key Alt+P.";

    /// <summary>Restore previous value button access key as spoken.</summary>
    public const string RestorePreviousAccessKey = "Alt+P";

    // ---- Change status sentences --------------------------

    /// <summary>Status format after a move: {0} the Boot#### name, {1} its description, {2} its new position (1-based), {3} the number of entries.</summary>
    public const string MovedFormat = "{0} ({1}) moved to position {2} of {3}.";

    /// <summary>Status format when Move up is pressed on the first entry: {0} the Boot#### name, {1} its description. Nothing moves.</summary>
    public const string AlreadyFirstFormat = "{0} ({1}) is already first.";

    /// <summary>Status format when Move down is pressed on the last entry: {0} the Boot#### name, {1} its description. Nothing moves.</summary>
    public const string AlreadyLastFormat = "{0} ({1}) is already last.";

    /// <summary>Status text after Ctrl+Z in the list of boot order entries.</summary>
    public const string OrderResetStatus = "Order reset to the firmware's current order; nothing was written.";

    /// <summary>Status text when Review change is pressed while the proposed order is the firmware's.</summary>
    public const string NothingToChangeStatus = "Nothing to change: the proposed order is the firmware's order.";

    /// <summary>Status text while the checks of a change (the dry run) run.</summary>
    public const string CheckingStatus = "Checking the change, please wait.";

    /// <summary>Status text while a confirmed change is written.</summary>
    public const string WritingStatus = "Writing the change, please wait. Do not restart the machine.";

    /// <summary>Status text when the window is asked to close while a change is written; the window stays open.</summary>
    public const string ChangeBeingWrittenStatus = "A change is being written; wait for its result.";

    /// <summary>Status text when Ctrl+F is pressed while a change is written or its result is still being read back: the filter box is disabled then, and focus stays where it is.</summary>
    public const string FilterUnavailableStatus = "The filter is unavailable while a change is written; wait for its result.";

    /// <summary>
    /// The reason of <see cref="ChangeBlockedFormat"/> when the checks of a change answered while
    /// another dialog was open: Confirm change never opens on top of another dialog, and the plan is
    /// cancelled. The window is disabled under a modal dialog and the two toolbar commands that open
    /// one are disabled while the checks run, so this is a second guard.
    /// </summary>
    public const string AnotherDialogOpenReason = "Another dialog was open when the checks finished.";

    /// <summary>Status format when a check refused the change: {0} the pipeline's reason, ending with its period.</summary>
    public const string ChangeBlockedFormat = "Change blocked: {0} Nothing was written.";

    /// <summary>The start of <see cref="ChangeBlockedFormat"/>, alone before a reason that already says nothing was written, so the sentence does not say it twice.</summary>
    public const string ChangeBlockedPrefix = "Change blocked: ";

    /// <summary>Status format when Restore previous value finds no snapshot: {0} the variable name.</summary>
    public const string NoSnapshotFormat = "No snapshot to restore: this application has not changed {0} on this machine.";

    /// <summary>Said before the Loaded sentence (or the refusal) when a load dropped a proposed order that was not written.</summary>
    public const string ProposedOrderDiscarded = "Proposed order discarded.";

    // ---- Confirm change dialog --------------------------------------

    /// <summary>The title of the confirmation dialog.</summary>
    public const string ConfirmChangeTitle = "Confirm change";

    /// <summary>Automation name of the box that says in one sentence what will change.</summary>
    public const string WhatWillChangeName = "What will change";

    /// <summary>Automation name of the box with the value before the change.</summary>
    public const string BeforeName = "Before";

    /// <summary>Automation name of the box with the value after the change.</summary>
    public const string AfterName = "After";

    /// <summary>Automation name of the box with the BitLocker state.</summary>
    public const string BitLockerStateName = "BitLocker state";

    /// <summary>Automation name of the box with the BitLocker risk.</summary>
    public const string RiskName = "Risk";

    /// <summary>Automation name of the box with the recovery-key reminder.</summary>
    public const string RecoveryKeyName = "Recovery key";

    /// <summary>Automation name of the box with the warnings.</summary>
    public const string WarningsName = "Warnings";

    /// <summary>Automation name of the box that names the snapshot folder.</summary>
    public const string SnapshotFolderName = "Snapshot folder";

    /// <summary>Help text of every read-only box of the dialog; the screen reader says the read-only state itself, so the help text does not (A2-6).</summary>
    public const string ConfirmBoxHelp = "Escape cancels; nothing is written.";

    /// <summary>Label and automation name of the suspend check box (no access key: only the confirm button has one).</summary>
    public const string SuspendCheckBoxName = "Suspend BitLocker for one restart";

    /// <summary>Help text of the suspend check box (section 5.5's text, shortened to stay under 200 characters).</summary>
    public const string SuspendCheckBoxHelp = "Checked: BitLocker is suspended for one restart, then turns back on sealed to that start's boot path. Unchecked: protection stays on and the next start may ask for the recovery key.";

    /// <summary>Confirm change button label (Alt+F).</summary>
    public const string ConfirmChangeLabel = "Con_firm change";

    /// <summary>Confirm change button automation name while the suspend check box is cleared or not offered.</summary>
    public const string ConfirmChangeName = "Confirm change";

    /// <summary>Confirm change button automation name while the suspend check box is ticked, so the press that confirms names the suspend.</summary>
    public const string ConfirmChangeAndSuspendName = "Confirm change and suspend BitLocker for one restart";

    /// <summary>Confirm change button help text.</summary>
    public const string ConfirmChangeHelp = "Writes the change exactly once, after a snapshot, then reads it back. Space or Enter here, and nowhere else, confirms. Access key Alt+F.";

    /// <summary>Confirm change button access key as spoken.</summary>
    public const string ConfirmChangeAccessKey = "Alt+F";

    /// <summary>Cancel button label and automation name (Escape).</summary>
    public const string CancelName = "Cancel";

    /// <summary>Cancel button help text.</summary>
    public const string CancelHelp = "Closes this dialog; nothing is written. Escape does the same.";

    /// <summary>Cancel button shortcut as UI Automation reports it (its AcceleratorKey): Escape presses it, as every other button of the dialog and the window reports its own key.</summary>
    public const string CancelAcceleratorKey = "Escape";

    /// <summary>What will change, a reorder that moves one entry: {0} the Boot#### name, {1} its description, {2} its old position, {3} its new position, {4} the number of entries.</summary>
    public const string SingleMoveFormat = "Change the boot order: {0} ({1}) moves from position {2} to {3} of {4}.";

    /// <summary>What will change, any other reorder: {0} the number of entries.</summary>
    public const string ReorderFormat = "Change the boot order of {0} entries; the Before and After boxes list it.";

    /// <summary>What will change, a one-time boot: {0} the entry as "Boot0003 (description)".</summary>
    public const string BootNextFormat = "Start {0} once at the next restart, then use the normal order.";

    /// <summary>What will change, the removal of the one-time boot choice: {0} the entry as "Boot0003 (description)".</summary>
    public const string ClearBootNextFormat = "Remove the one-time boot choice {0}.";

    /// <summary>What will change, the restore of a boot order: {0} the snapshot folder name.</summary>
    public const string RestoreReorderFormat = "Restore the boot order recorded in snapshot {0}; the After box lists it.";

    /// <summary>What will change, the restore of a snapshot that recorded no one-time boot: {0} the snapshot folder name.</summary>
    public const string RestoreClearFormat = "Restore snapshot {0}: remove the one-time boot choice.";

    /// <summary>
    /// What will change, the restore of a snapshot that recorded a one-time boot: {0} the snapshot
    /// folder name, {1} the entry as "Boot0003 (description)". Shorter than <see cref="BootNextFormat"/>
    /// (no "then use the normal order"): with the longest folder name ("-bootnext-clear-restore"),
    /// the longest fixture description and <see cref="SuspendSuffix"/> the box stays under 200 characters.
    /// </summary>
    public const string RestoreBootNextFormat = "Restore snapshot {0}: start {1} once at the next restart.";

    /// <summary>Appended to What will change while the suspend check box is ticked, so what is confirmed names the suspend.</summary>
    public const string SuspendSuffix = " And suspend BitLocker for one restart.";

    /// <summary>One entry in the Before and After boxes: {0} the Boot#### name, {1} its description, {2} its flags ("active", "inactive, hidden", "missing").</summary>
    public const string EntryLineFormat = "{0}, {1}, {2}";

    /// <summary>One position of a boot order in the Before and After boxes: {0} the position (1-based), {1} the entry line.</summary>
    public const string PositionLineFormat = "{0}. {1}";

    /// <summary>
    /// The first line of the Before and After boxes of a boot order, which says what follows as the
    /// Warnings box does (a screen reader reads a multi-line box only to its first line on Tab):
    /// {0} the number of positions as text (<see cref="Positions"/>: "1 position", "2 positions").
    /// </summary>
    public const string PositionsHeaderFormat = "{0}; Down arrow reads each.";

    /// <summary>Appended to a row of the After box whose entry moved: {0} its position before the change.</summary>
    public const string MovedFromFormat = " (was {0})";

    /// <summary>The Before or After box of BootNext when no one-time boot is set.</summary>
    public const string NotSetText = "Not set.";

    /// <summary>The first line of the Before box when the restored snapshot is of a change that did not complete; the snapshot's value before that change follows.</summary>
    public const string PendingBeforeLine = "This change did not complete; the value before it was:";

    /// <summary>The line of the Before box, after the value of an incomplete change's snapshot, that introduces the value the firmware holds now.</summary>
    public const string PendingNowLine = "The firmware holds now:";

    /// <summary>The Risk box while BitLocker protection is off.</summary>
    public const string NoRiskText = "None: BitLocker protection is off.";

    /// <summary>The Risk box of the removal of the one-time boot choice while BitLocker protectors exist.</summary>
    public const string ClearRiskText = "Removing the one-time boot choice lets the next start use the normal order, the boot path BitLocker already expects.";

    /// <summary>The Recovery key box while BitLocker protection is off.</summary>
    public const string RecoveryKeyNotNeededText = "Not needed: BitLocker protection is off.";

    /// <summary>The first line of the Warnings box: {0} the number of lines that follow as text (<see cref="Lines"/>: "1 line", "3 lines").</summary>
    public const string WarningsHeaderFormat = "{0}; Down arrow reads each.";

    /// <summary>The Warnings box when the checks gave no warning.</summary>
    public const string NoWarningsText = "No warnings.";

    /// <summary>The last line of the Warnings box of a reorder.</summary>
    public const string ReorderMayBeLostWarning = "A Windows feature update or Startup Repair puts Windows Boot Manager back in first place; a lost reorder is expected, not a fault.";

    /// <summary>The Snapshot folder box: {0} the full path of the snapshot folder the change will write.</summary>
    public const string SnapshotFolderFormat = "Before writing, a snapshot is saved to {0}. Restore previous value (Alt+P) puts it back.";

    // ---- Log panel -----------------------------------------------------------------------

    /// <summary>Log panel automation name.</summary>
    public const string LogPanelName = "Log";

    /// <summary>Log panel help text.</summary>
    public const string LogPanelHelp = "The last 200 log lines: level, time and message; a line from an earlier day also gives its date. This list never interrupts; press Copy log (Alt+L) to copy all of it.";

    /// <summary>Log panel warning, after "machine identity: ", when the WMI queries of the machine identity did not finish in time: {0} the seconds waited, {1} what the identity fields show instead (<see cref="Unknown"/>).</summary>
    public const string IdentityTimedOutFormat = "the WMI queries did not answer within {0} seconds, so the identity fields show {1}";

    /// <summary>Log panel warning when a baseline index lists one variable (vendor GUID and name) more than once: {0} how many (<see cref="Variables"/>: "1 variable", "2 variables"), {1} their keys, comma-separated.</summary>
    public const string BaselineDuplicateKeysFormat = "index.json lists {0} more than once; only the first entry of each is shown: {1}";

    /// <summary>The end of the key list in <see cref="BaselineDuplicateKeysFormat"/> when it is cut short: {0} how many keys are not listed.</summary>
    public const string MoreKeysFormat = "and {0} more";

    /// <summary>Log panel warning when a baseline index carries a timestamp that is not an ISO 8601 date and time.</summary>
    public const string BaselineTimestampUnreadable = "index.json carries a timestamp that is not a valid date and time; the title and the Loaded sentence leave it out";

    // ---- Status bar ----------------------------------------------------------------------

    /// <summary>Status box automation name.</summary>
    public const string StatusName = "Status";

    /// <summary>Status box help text; the screen reader says the box's read-only state itself, so the help text does not (A2-6).</summary>
    public const string StatusHelp = "Announces every state change, filter result, refusal and error.";

    // ---- Splitters -----------------------------------------------------------------------

    /// <summary>Automation name of the splitter between the list and the detail pane.</summary>
    public const string ListDetailSplitterName = "Resize list and details";

    /// <summary>Help text of the splitter between the list and the detail pane.</summary>
    public const string ListDetailSplitterHelp = "Drag, or focus and press Left or Right, to change the width of the list and the detail pane. The list's share of the width is announced as a percentage.";

    /// <summary>Automation name of the splitter above the log panel.</summary>
    public const string LogSplitterName = "Resize log panel";

    /// <summary>Help text of the splitter above the log panel.</summary>
    public const string LogSplitterHelp = "Drag, or focus and press Up or Down, to change the height of the log panel. The log panel's share of the height is announced as a percentage.";

    /// <summary>
    /// The position of the splitter between the list and the detail pane as a sentence (Alpha 2
    /// finding A2-8, the tester's A2-6: a bare number relates to nothing): {0} the list's share of the
    /// width the two share, in whole percent, {1} the detail pane's, what is left of 100. The
    /// splitter's value, and, with a closing period, what Status says after it moved.
    /// </summary>
    public const string ListDetailSplitterValueFormat = "List {0} percent of the width, details {1} percent";

    /// <summary>
    /// The position of the splitter above the log panel as a sentence (A2-8): {0} the log panel's
    /// share of the height it shares with the list and the detail pane, in whole percent, {1} the
    /// list and the detail pane's, what is left of 100.
    /// </summary>
    public const string LogSplitterValueFormat = "Log panel {0} percent of the height, list and details {1} percent";

    /// <summary>Said before a splitter's position when an arrow press could not move it further that way.</summary>
    public const string SplitterLimitPrefix = "Limit reached: ";

    // ---- Dell settings region ------------------------
    // The words of the Dell problems, causes and rows are Core's (DellSentences): the command line
    // prints them and the window says them. Nothing here says that a row is read-only (Alpha 2
    // finding A2-6): that nothing can be changed here is said on the button's help and on the
    // state line, never on a row or the list.

    /// <summary>Read Dell settings button label (Alt+S).</summary>
    public const string ReadDellSettingsLabel = "Read Dell _settings";

    /// <summary>Read Dell settings button automation name.</summary>
    public const string ReadDellSettingsName = "Read Dell settings";

    /// <summary>Read Dell settings button access key as spoken.</summary>
    public const string ReadDellSettingsAccessKey = "Alt+S";

    /// <summary>Read Dell settings button help text (under 200 characters).</summary>
    public const string ReadDellSettingsHelp = "Loads the agreed Dell BIOS settings from this computer through Dell Command Monitor, or Dell Command Configure when that is missing. Changes nothing. Access key Alt+S.";

    /// <summary>Automation name of the state line, the box that says when and where the Dell values were loaded.</summary>
    public const string DellSourceName = "Dell settings source";

    /// <summary>Help text of the state line (under 80 characters); the screen reader says the box's read-only state itself (A2-6).</summary>
    public const string DellSourceHelp = "When and where the Dell values were loaded, or why they could not be.";

    /// <summary>Automation name of the list of Dell BIOS settings.</summary>
    public const string DellSettingsListName = "Dell BIOS settings";

    /// <summary>
    /// Help text of the list of Dell BIOS settings (under 200 characters): heard on every entry
    /// into the list, so it says nothing the state line says, and it is on the list, never on a row.
    /// </summary>
    public const string DellSettingsListHelp = "One row per Dell BIOS setting: its name, current value and possible values. A letter jumps to a name; Ctrl+C copies the row.";

    /// <summary>Column header of the Dell list: the setting's name.</summary>
    public const string ColumnDellSetting = "Setting";

    /// <summary>Column header of the Dell list: the current value.</summary>
    public const string ColumnDellCurrent = "Current value";

    /// <summary>Column header of the Dell list: the possible values.</summary>
    public const string ColumnDellPossible = "Possible values";

    /// <summary>A Dell row's value before any read of this computer (the live view).</summary>
    public const string DellNotLoadedYetValue = "not loaded yet";

    /// <summary>A Dell row's value when the last read of this computer gave no answer and nothing earlier is kept, or when nothing was read for the row.</summary>
    public const string DellNotLoadedValue = "not loaded";

    /// <summary>A Dell row's value when the baseline folder shown records nothing for it.</summary>
    public const string DellNotRecordedValue = "not recorded in this baseline folder";

    /// <summary>The BIOS version on the state line when it could not be read.</summary>
    public const string DellBiosUnknown = "not known";

    /// <summary>The state line before any read of this computer.</summary>
    public const string DellNotLoadedLine = "Not loaded yet. Read Dell settings (Alt+S) loads them from this computer. This application shows Dell BIOS settings and never changes them.";

    /// <summary>The state line after a read of this computer that answered: {0} the read time (HH:mm:ss, local), {1} the source's name, {2} the BIOS version or <see cref="DellBiosUnknown"/>.</summary>
    public const string DellLiveLineFormat = "Loaded at {0} from {1} on this computer, BIOS {2}. This application shows Dell BIOS settings and never changes them; they are changed in the BIOS setup (F2).";

    /// <summary>Appended to the state line when Dell Command Configure answered, which reports current values only.</summary>
    public const string DellFallbackLineSuffix = " Dell Command Configure reports current values only, so the possible values shown are those of the attribute map for BIOS A18.";

    /// <summary>The state line of a baseline folder with a usable Dell file: {0} the read time ("d MMMM yyyy HH:mm 'UTC'"), {1} the source's name, {2} the folder's name, {3} the BIOS version or <see cref="DellBiosUnknown"/>.</summary>
    public const string DellBaselineLineFormat = "Recorded at {0} from {1} in baseline folder {2}, BIOS {3}. Offline: nothing is loaded from this computer.";

    /// <summary>The state line of a baseline folder without a Dell file: {0} the folder's name.</summary>
    public const string DellBaselineNoneLineFormat = "Baseline folder {0} holds no Dell BIOS settings; folders saved before Alpha 2 never do.";

    /// <summary>The state line of a baseline folder whose Dell file recorded a read that gave no answer: {0} the folder's name, {1} the recorded causes, in Core's words.</summary>
    public const string DellBaselineUnavailableLineFormat = "Baseline folder {0} holds no Dell BIOS settings: they could not be read when it was saved. {1}";

    /// <summary>The state line of a baseline folder whose Dell file cannot be used: {0} the folder's name, {1} the reader's problem (for example "it is larger than 256 KiB").</summary>
    public const string DellBaselineUnusableLineFormat = "Baseline folder {0} has a Dell settings file that cannot be used: {1}.";

    /// <summary>The state line after a baseline folder load that was refused or failed.</summary>
    public const string DellBaselineNotLoadedLine = "No Dell BIOS settings are shown: the baseline folder was not loaded.";

    /// <summary>Status text while the Dell BIOS settings are read.</summary>
    public const string DellLoadingStatus = "Loading the Dell BIOS settings, please wait.";

    /// <summary>Status format after a first read that read every setting: {0} the count as text (<see cref="DellSettings"/>: "10 Dell BIOS settings"), {1} the source's name.</summary>
    public const string DellLoadedFormat = "Loaded {0} from {1}.";

    /// <summary>Status format after a first read that did not read every setting: {0} how many were read, {1} how many are agreed, {2} the source's name, {3} the names of the others, comma-separated.</summary>
    public const string DellLoadedPartialFormat = "Loaded {0} of {1} Dell BIOS settings from {2}; not reported: {3}.";

    /// <summary>Status text after a later read that found every value as before.</summary>
    public const string DellUnchangedStatus = "Loaded the Dell BIOS settings again: nothing changed.";

    /// <summary>Status format after a later read that found values changed: {0} the changes (<see cref="DellChangeItemFormat"/>), joined with "; ".</summary>
    public const string DellChangedFormat = "Loaded the Dell BIOS settings again; changed: {0}.";

    /// <summary>One change in <see cref="DellChangedFormat"/>: {0} the setting's name, {1} its value now.</summary>
    public const string DellChangeItemFormat = "{0}, now {1}";

    /// <summary>Status format and state line when a read gave no answer while earlier values are kept: {0} the problem sentence, {1} the time of the read the list still shows (HH:mm:ss, local).</summary>
    public const string DellKeptFormat = "{0} The list still shows the values loaded at {1}.";

    /// <summary>Status text when Read Dell settings is pressed while a read of them runs.</summary>
    public const string DellAlreadyLoadingStatus = "The Dell BIOS settings are still loading; please wait.";

    /// <summary>Status text when Read Dell settings is pressed while the variables load.</summary>
    public const string DellWaitForVariablesStatus = "Wait for the variables to finish loading, then press Read Dell settings (Alt+S) again.";

    /// <summary>Status text when Read Dell settings is pressed while a change is checked or written, or until its result is said.</summary>
    public const string DellWaitForChangeStatus = "Wait for the change to finish, then press Read Dell settings (Alt+S) again.";

    /// <summary>Status text when Read Dell settings is pressed while a baseline folder is shown: Dell BIOS settings are read from this computer only.</summary>
    public const string DellBaselineShownStatus = "The window shows a baseline folder, and Dell BIOS settings are loaded from this computer only. Press Refresh (Alt+R) first, then Read Dell settings (Alt+S).";

    /// <summary>Announcement format when F6 or Shift+F6 arrives in the Dell region and the rows need a word of explanation: {0} the region name, {1} the clause.</summary>
    public const string DellRegionArrivedFormat = "{0} region, {1}";

    /// <summary>The arrival clause while a baseline folder is shown: {0} the folder's name.</summary>
    public const string DellArrivalBaselineFormat = "from baseline folder {0}";

    /// <summary>The arrival clause while the last read of this computer gave no answer and nothing earlier is kept.</summary>
    public const string DellArrivalUnavailable = "could not be loaded";

    // ---- Regions (F6 landmarks) ----------------------------------------------------------

    /// <summary>Region name: toolbar.</summary>
    public const string RegionToolbar = "Toolbar";

    /// <summary>Region name: filter row.</summary>
    public const string RegionFilter = "Filter";

    /// <summary>Region name: variable list.</summary>
    public const string RegionList = "List";

    /// <summary>Region name: detail pane.</summary>
    public const string RegionDetail = "Detail";

    /// <summary>Region name: log panel.</summary>
    public const string RegionLog = "Log";

    /// <summary>Region name: the Dell settings; also the region's header and automation name.</summary>
    public const string RegionDell = "Dell settings";

    /// <summary>Region name: status bar.</summary>
    public const string RegionStatus = "Status";

    // ---- Machine identity labels ---------------------------------------------------------

    /// <summary>Identity label: manufacturer.</summary>
    public const string ManufacturerLabel = "Manufacturer";

    /// <summary>Identity label: model.</summary>
    public const string ModelLabel = "Model";

    /// <summary>Identity label: BIOS version.</summary>
    public const string BiosVersionLabel = "BIOS version";

    /// <summary>Identity label: service tag.</summary>
    public const string ServiceTagLabel = "Service tag";

    /// <summary>Identity label: CPU.</summary>
    public const string CpuLabel = "CPU";

    /// <summary>Identity label: operating system.</summary>
    public const string OperatingSystemLabel = "Operating system";

    /// <summary>Identity label: expected model confirmed.</summary>
    public const string ExpectedModelLabel = "Expected model confirmed";

    /// <summary>Value of <see cref="ExpectedModelLabel"/> when the model, BIOS version and CPU generation match the planned unit.</summary>
    public const string YesText = "Yes";

    /// <summary>Value of <see cref="ExpectedModelLabel"/> when they do not; the reasoning is in the log.</summary>
    public const string NoText = "No";

    /// <summary>Placeholder for a value that has not been read yet.</summary>
    public const string NotReadYet = "(not read yet)";

    /// <summary>Placeholder for a value that was queried but is not available.</summary>
    public const string Unknown = "(unknown)";

    // ---- Count helpers (spoken text needs the singular) ----------------------------------

    /// <summary>Renders a row count for a group header: "1 item" or "N items" (invariant culture).</summary>
    /// <param name="count">The count.</param>
    /// <returns>The text.</returns>
    public static string Items(int count) => Count(count, "item", "items");

    /// <summary>Renders a variable count: "1 variable" or "N variables" (invariant culture).</summary>
    /// <param name="count">The count.</param>
    /// <returns>The text.</returns>
    public static string Variables(int count) => Count(count, "variable", "variables");

    /// <summary>Renders an exported row count: "1 visible variable" or "N visible variables" (invariant culture).</summary>
    /// <param name="count">The count.</param>
    /// <returns>The text.</returns>
    public static string VisibleVariables(int count) => Count(count, "visible variable", "visible variables");

    /// <summary>Renders the number of lines of the Warnings box: "1 line" or "N lines" (invariant culture).</summary>
    /// <param name="count">The count.</param>
    /// <returns>The text.</returns>
    public static string Lines(int count) => Count(count, "line", "lines");

    /// <summary>Renders the number of positions of a boot order in the Before and After boxes: "1 position" or "N positions" (invariant culture).</summary>
    /// <param name="count">The count.</param>
    /// <returns>The text.</returns>
    public static string Positions(int count) => Count(count, "position", "positions");

    /// <summary>Renders a count of Dell BIOS settings for <see cref="DellLoadedFormat"/>: "1 Dell BIOS setting" or "N Dell BIOS settings" (invariant culture).</summary>
    /// <param name="count">The count.</param>
    /// <returns>The text.</returns>
    public static string DellSettings(int count) => Count(count, "Dell BIOS setting", "Dell BIOS settings");

    /// <summary>Renders a count with its noun, singular for exactly 1 (invariant culture).</summary>
    /// <param name="count">The count.</param>
    /// <param name="singular">The noun for a count of 1.</param>
    /// <param name="plural">The noun for every other count.</param>
    /// <returns>"1 &lt;singular&gt;" or "&lt;count&gt; &lt;plural&gt;".</returns>
    public static string Count(int count, string singular, string plural)
    {
        ArgumentNullException.ThrowIfNull(singular);
        ArgumentNullException.ThrowIfNull(plural);
        return count.ToString(CultureInfo.InvariantCulture) + " " + (count == 1 ? singular : plural);
    }
}
