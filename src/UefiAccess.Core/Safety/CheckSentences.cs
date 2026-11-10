// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;

namespace UefiAccess.Core.Safety;

/// <summary>
/// The sentences of the checks: every rule sentence of
/// <see cref="ChangeValidator"/> (V1 to V17, one or more per rule; V15 is the S3 sentence) and the
/// check sentences of the stages the command-line tool and the application both quote (S4, S9,
/// S11 to S14, S17). Core owns them because the command-line tool prints them and the application
/// quotes them; the record and the Status box carry the same text (Status prefixes it with
/// "Change blocked: "). Formats take their arguments through <see cref="Format"/>, which uses the
/// invariant culture and is the one formatting helper of the Safety folder. The other user-visible
/// sentences of the pipeline are constants too, each beside the code that produces it:
/// <see cref="TransactionSentences"/> (the preconditions, the stage details and the results of a
/// written change), <see cref="WriteStatusMapping"/> (the API errors of section 3.6),
/// <see cref="BitLockerPreflight"/> (the dialog's three BitLocker boxes),
/// <see cref="BitLockerSuspender"/>, <see cref="SnapshotStore"/> and <see cref="WriteRecordLog"/>
/// (their own refusals), <see cref="PlatformPolicy"/> (Secure Boot) and
/// <see cref="WritabilitySentences"/>.
/// </summary>
public static class CheckSentences
{
    /// <summary>V1: the current BootOrder has an odd byte count. {0} is the byte count.</summary>
    public const string OddByteCountFormat = "BootOrder has an odd byte count ({0}); the firmware's list is malformed. Nothing can be changed until it is fixed in BIOS Setup.";

    /// <summary>V1: the current BootOrder is absent or holds no bytes at all.</summary>
    public const string BootOrderEmpty = "BootOrder is absent or empty; the firmware's list has no entries. Nothing can be changed until it is fixed in BIOS Setup.";

    /// <summary>V2: the current BootNext is not exactly 2 bytes. {0} is the byte count.</summary>
    public const string BootNextWrongSizeFormat = "BootNext holds {0} bytes, not 2; it cannot be changed by this tool. Restart to let the firmware remove it.";

    /// <summary>V3: a referenced Boot#### exists but does not parse. {0} is the entry name.</summary>
    public const string EntryMalformedFormat = "{0} exists but cannot be decoded as a load option; the firmware's entry is malformed.";

    /// <summary>V4: a requested entry does not exist. {0} is the entry name.</summary>
    public const string EntryMissingFormat = "{0} does not exist on this machine.";

    /// <summary>V5: the current BootOrder refers to a missing entry. {0} is the entry name.</summary>
    public const string CurrentListRefersToMissingFormat = "BootOrder refers to {0}, which does not exist; the firmware may rewrite the list at the next start. Fix it in BIOS Setup first.";

    /// <summary>V6: the new order names an entry the current list does not hold. {0} is the entry name.</summary>
    public const string OrderAddsFormat = "The new order adds {0}, which is not in the firmware's list.";

    /// <summary>V6: the new order lacks an entry the current list holds. {0} is the entry name.</summary>
    public const string OrderLeavesOutFormat = "The new order leaves out {0}.";

    /// <summary>V6: an entry appears more than once in the new order. {0} is the entry name.</summary>
    public const string OrderDuplicatesFormat = "{0} appears twice.";

    /// <summary>V6 for a restore (section 3.5): the snapshot's order names an entry that is no longer in BootOrder. {0} is the entry name.</summary>
    public const string RestoreOrderAddsFormat = "Restore blocked: the snapshot's order lists {0}, which is no longer in BootOrder; the list was changed by Windows or the firmware since the snapshot. Nothing was written.";

    /// <summary>V7: the new order equals the current one.</summary>
    public const string OrderUnchanged = "The order is unchanged; nothing to write.";

    /// <summary>V7 turned off (the command-line tool's allow-no-change option, the first-write drill): the order equals the current one and is written as it is; recorded as a warning.</summary>
    public const string OrderUnchangedAllowed = "The order is unchanged; it is written as it is, because an unchanged order was allowed for this run.";

    /// <summary>
    /// V8, a tripwire that never fires on its own: the bytes built from the entry numbers do not
    /// have the size the operation needs (the current size for a reorder, 2 for a one-time boot, 0
    /// for a clear). {0} is the variable name, {1} the size built, {2} the size required.
    /// </summary>
    public const string ProposedSizeWrongFormat = "The new value of {0} came out at {1} bytes where {2} are required; nothing is written.";

    /// <summary>V9: the current attributes are not NV BS RT. {0} is the variable name, {1} the flags read.</summary>
    public const string AttributesNotWritableFormat = "{0} carries attributes {1}, not NV BS RT; it is not writable from Windows.";

    /// <summary>V10: an authenticated-write bit is set. {0} is the variable name, {1} the authenticated flags (AW, TA or EA).</summary>
    public const string AuthenticatedWriteFormat = "{0} carries an authenticated-write attribute ({1}): only a signed update can change it.";

    /// <summary>V11: the one-time boot target is not LOAD_OPTION_ACTIVE. {0} is the entry name.</summary>
    public const string TargetInactiveFormat = "{0} is inactive; the boot manager would not start it.";

    /// <summary>V12: the one-time boot target is category App. {0} is the entry name.</summary>
    public const string TargetIsApplicationFormat = "{0} is an application entry, not a boot entry.";

    /// <summary>V12 (a warning): the one-time boot target is hidden from the firmware's menu. {0} is the entry name.</summary>
    public const string TargetHiddenWarningFormat = "{0} is hidden from the firmware's menu; it can still be started once.";

    /// <summary>V13: the entry may start Windows through another boot path while BitLocker protectors exist. {0} is the entry name.</summary>
    public const string OtherWindowsPathFormat = "{0} may start Windows through a different boot path. A one-restart suspend would seal BitLocker to that path and the following normal start would ask for the recovery key. Not offered while BitLocker protectors exist.";

    /// <summary>
    /// V13 for a reorder while BitLocker protectors may exist: a one-time boot is set, and the
    /// firmware starts it at the next start before any entry of the order. {0} is the entry it
    /// names ("Boot0000"), or "BootNext" when it is not one entry.
    /// </summary>
    public const string PendingOneTimeBootFormat = "A one-time boot ({0}) is set, and the firmware starts it at the next start before the boot order; a one-restart suspend would be used on that start. Clear the one-time boot first, then check the new order again.";

    /// <summary>V14: a clear was asked for while no BootNext exists.</summary>
    public const string NoBootNextToClear = "No one-time boot is set; nothing to clear.";

    /// <summary>V15 and S3: the enumeration was not complete. {0} describes the method and its error.</summary>
    public const string EnumerationIncompleteFormat = "The variable list could not be read completely ({0}); a change is not safe on an incomplete list.";

    /// <summary>V15: the method and the enumeration's error, inside <see cref="EnumerationIncompleteFormat"/>. {0} is the method, {1} the error.</summary>
    public const string EnumerationErrorFormat = "{0}: {1}";

    /// <summary>V15: the description so far and one warning, inside <see cref="EnumerationIncompleteFormat"/>. {0} is the method (and error).</summary>
    public const string EnumerationOneWarningFormat = "{0}, 1 warning";

    /// <summary>V15: the description so far and its warnings, inside <see cref="EnumerationIncompleteFormat"/>. {0} is the method (and error), {1} the count.</summary>
    public const string EnumerationWarningsFormat = "{0}, {1} warnings";

    /// <summary>V16: BootCurrent is absent, malformed, names a missing or malformed entry, or names no fixed disk.</summary>
    public const string BootCurrentUnusable = "The current boot entry (BootCurrent) could not be read or names no fixed disk, so the other-Windows-path check cannot run. Nothing was written.";

    /// <summary>V17 (a warning): an entry without a fixed-disk node is ahead of the current one, or is the one-time target. {0} is the entry name.</summary>
    public const string RemovableAheadWarningFormat = "If {0} is not present at the next start, the firmware goes on to the normal list in that start; with the suspend ticked BitLocker is then sealed to that start, and a later start may ask for the recovery key.";

    /// <summary>S4: the variable is not one of the two. {0} is the variable name.</summary>
    public const string NotWhitelistedFormat = "{0} is not on the whitelist of this application; only BootOrder (reorder) and BootNext can be changed.";

    /// <summary>S4: the variable is whitelisted but not for this operation (unreachable through the typed requests; kept as a tripwire). {0} is the variable name.</summary>
    public const string WrongOperationFormat = "{0} does not allow this operation.";

    /// <summary>S9 and S12: the write budget of one Windows start is used up. {0} is the number of writes made.</summary>
    public const string WriteBudgetExceededFormat = "This start has already made {0} changes to the firmware. Restart Windows before more changes.";

    /// <summary>
    /// S9 and S12, after <see cref="WriteBudgetExceededFormat"/> when the count includes lines of
    /// write-log.jsonl that are not whole records (a crash while a line was appended), each counted
    /// as a possible change. {0} is the number of such lines.
    /// </summary>
    public const string WriteLogFragmentsFormat = "{0} of them are lines of write-log.jsonl that are not whole records (a crash while a line was written), each counted as a possible change.";

    /// <summary>S9 and S12: write-log.jsonl exists but could not be read whole, so the writes of this start cannot be counted. {0} is the reason.</summary>
    public const string WriteLogUnreadableFormat = "The list of changes already made (write-log.jsonl) could not be read ({0}), so the changes of this start cannot be counted; nothing is written.";

    /// <summary>S12: another commit, in this process or another one (the application and the command-line tool), holds the write lock.</summary>
    public const string AnotherChangeBeingWritten = "Another change to the firmware is being written by this application or the command-line tool; wait for its result, then check this change again. Nothing was written.";

    /// <summary>S12: the write lock could not be taken for another reason. {0} is the reason.</summary>
    public const string WriteLockUnavailableFormat = "The lock that keeps two changes to the firmware apart could not be taken ({0}); nothing was written.";

    /// <summary>S12: BitLocker's state read again before any suspend is not the one the dry run showed. {0} is the state then, {1} the state now (lowercase).</summary>
    public const string BitLockerStateChangedFormat = "BitLocker's state changed after the dry run (it was {0}, it is now {1}); check the change again. Nothing was written.";

    /// <summary>S11, check expectedModel: the machine is another model, or the OptiPlex 7020 on another BIOS version.</summary>
    public const string WrongMachine = "This machine is not the supported OptiPlex 7020 with BIOS A18; nothing is written.";

    /// <summary>S11, check expectedUnit: the machine is the contracted model on BIOS A18, but its Windows machine ID is not the M3 test unit's.</summary>
    public const string NotEnabledUnit = "This OptiPlex 7020 is not the enabled unit; nothing is written.";

    /// <summary>S11, check expectedUnit: the Windows machine ID could not be read (or was empty or not a GUID), so the unit is not recognised.</summary>
    public const string MachineIdNotRead = "The Windows machine ID could not be read, so this unit cannot be recognised as the enabled unit; nothing is written.";

    /// <summary>S11: the token's hash is not the plan's.</summary>
    public const string TokenDoesNotMatchPlan = "The confirmation does not belong to this change; nothing is written.";

    /// <summary>S11: the BitLocker state needs a choice and none was made.</summary>
    public const string BitLockerChoiceMissing = "BitLocker protection is on and no choice was made about it; nothing is written.";

    /// <summary>S12 and S14: the target changed between the dry run and the write. {0} is the variable name.</summary>
    public const string StaleReadFormat = "{0} changed after the dry run; nothing was written. Read the variables again and start over.";

    /// <summary>S13: the suspend returned a failure code. {0} is the HRESULT.</summary>
    public const string BitLockerSuspendFailedFormat = "BitLocker could not be suspended (0x{0:X8}). Nothing was written.";

    /// <summary>S13: the suspend did not answer within its bound and may still complete.</summary>
    public const string BitLockerSuspendTimedOut = "The BitLocker suspend did not answer within 60 seconds and may still complete; check BitLocker's state in Windows before restarting. Nothing was written.";

    /// <summary>S14 or S15 after a successful suspend: BitLocker stays suspended until the next restart.</summary>
    public const string BitLockerLeftSuspended = "BitLocker is suspended until the next restart, which seals it to that start's boot path.";

    /// <summary>S7 (a warning): the target differs from the newest baseline. {0} is the variable name, {1} the baseline's timestamp.</summary>
    public const string BaselineDiffersFormat = "{0} differs from the baseline of {1}; the firmware or Windows changed it since.";

    /// <summary>S17: the write was verified but the record could not be saved. {0} is the reason.</summary>
    public const string RecordNotSavedVerifiedFormat = "The change is written and verified; the record could not be saved: {0}";

    /// <summary>S17: the write was made but not verified, and the record could not be saved. {0} is the reason.</summary>
    public const string RecordNotSavedUnverifiedFormat = "The change is written but not verified; the record could not be saved: {0}";

    /// <summary>S17: the call failed, the value could not be verified, and the record could not be saved. {0} is the reason.</summary>
    public const string RecordNotSavedUnknownFormat = "Whether the change is written is not known; the record could not be saved: {0}";

    /// <summary>Entry line text for an entry the current list names but the firmware does not hold.</summary>
    public const string MissingEntryDescription = "missing: no such Boot#### variable";

    /// <summary>Entry line text for a load option without a description.</summary>
    public const string NoDescription = "(no description)";

    /// <summary>Entry line text for a load option that does not parse.</summary>
    public const string MalformedEntryDescription = "(malformed load option)";

    /// <summary>Entry flag of an entry the list names but the firmware does not hold.</summary>
    public const string EntryMissingFlag = "missing";

    /// <summary>Entry flag of a LOAD_OPTION_ACTIVE entry.</summary>
    public const string EntryActiveFlag = "active";

    /// <summary>Entry flag of an entry without LOAD_OPTION_ACTIVE.</summary>
    public const string EntryInactiveFlag = "inactive";

    /// <summary>Entry flag of a LOAD_OPTION_HIDDEN entry.</summary>
    public const string EntryHiddenFlag = "hidden";

    /// <summary>Entry flag naming the category. {0} is "Boot", "App" or the hexadecimal category.</summary>
    public const string EntryCategoryFormat = "category {0}";

    /// <summary>The separator between entry flags, as the decoders write them ("active, hidden, category Boot").</summary>
    public const string EntryFlagSeparator = ", ";

    /// <summary>Formats a sentence with the invariant culture.</summary>
    /// <param name="format">One of the *Format constants.</param>
    /// <param name="arguments">The values for its placeholders.</param>
    /// <returns>The sentence.</returns>
    public static string Format(string format, params object?[] arguments) => string.Format(CultureInfo.InvariantCulture, format, arguments);
}
