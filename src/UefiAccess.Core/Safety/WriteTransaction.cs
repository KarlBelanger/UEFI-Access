// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using Serilog;
using Serilog.Events;
using UefiAccess.Core.Baseline;
using UefiAccess.Core.Decoders;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Privileges;
using UefiAccess.Core.Variables;

namespace UefiAccess.Core.Safety;

/// <summary>
/// The sentences of the transaction's own stages and outcomes that <see cref="CheckSentences"/> and <see cref="WriteStatusMapping"/> do not hold:
/// the preconditions of stage S1, the snapshot and drift stages, the confirmation, and the result
/// of a written change. Core owns them because the command-line tool prints them and the record
/// carries them; the application quotes them. A refusal is the bare reason: the application's
/// Status box prefixes "Change blocked: ". The S1 sentences are the M1 ones, which the application
/// already shows, and the Unverified sentences name both hosts' remedies, as section 5.6 has them.
/// </summary>
public static class TransactionSentences
{
    /// <summary>S1: the machine did not boot in UEFI mode (the M1 sentence, verbatim).</summary>
    public const string NotUefi = "This system did not boot in UEFI mode (or the OS reports no UEFI runtime services); firmware variables are not reachable from Windows.";

    /// <summary>S1: the process is not elevated (the M1 sentence, verbatim).</summary>
    public const string NotElevated = "This command needs administrator rights; open an elevated command prompt (Run as administrator) and run uefiaccess again.";

    /// <summary>S1: the privilege could not be enabled; {0} is empty or " (the privilege manager's reason)".</summary>
    public const string PrivilegeAbsentFormat = "The process is elevated but SeSystemEnvironmentPrivilege could not be enabled{0}; start UefiAccess as administrator from an account that holds that privilege.";

    /// <summary>S3: a direct read failed; {0} the variable name, {1} the read status. Used inside <see cref="CheckSentences.EnumerationIncompleteFormat"/>.</summary>
    public const string DirectReadFailedFormat = "{0} could not be read directly: {1}";

    /// <summary>S7: there is no baseline folder to compare with.</summary>
    public const string BaselineNotCompared = "No baseline folder to compare with; not compared.";

    /// <summary>S7: the newest baseline could not be read; {0} the folder name, {1} the reason.</summary>
    public const string BaselineUnreadableFormat = "The baseline {0} could not be read ({1}); not compared.";

    /// <summary>S10: every check of a dry run passed.</summary>
    public const string ChecksPassed = "Every check passed; nothing has been written.";

    /// <summary>The checks or the confirmation were cancelled (the application's existing sentence, verbatim).</summary>
    public const string Cancelled = "Cancelled; nothing changed.";

    /// <summary>S8 or S11: the suspend was chosen for a change or a state where it is not offered.</summary>
    public const string SuspendNotOffered = "The one-restart BitLocker suspend is not offered for this change; nothing is written.";

    /// <summary>S11: the confirmation's BitLocker choice is not the one the checks ran with.</summary>
    public const string ChoiceDiffersFromChecks = "The BitLocker choice of the confirmation is not the one the checks ran with; nothing is written.";

    /// <summary>S11: the plan was not prepared by this pipeline, or was already committed or cancelled.</summary>
    public const string PlanNotPrepared = "This change was not checked in this session, or its confirmation was already used; check it again. Nothing is written.";

    /// <summary>S11: the plan's bytes no longer match its hash.</summary>
    public const string PlanAltered = "The change was altered after it was checked; nothing is written.";

    /// <summary>S12: the record of the change cannot be saved; {0} the refusal.</summary>
    public const string RecordRootRefusedFormat = "The record of the change cannot be saved, so nothing was written: {0}";

    /// <summary>S12: the snapshot could not be written; {0} the store's sentence.</summary>
    public const string SnapshotNotSavedFormat = "The snapshot could not be saved, so nothing was written: {0}";

    /// <summary>S12: the snapshot did not read back as written; {0} the folder, {1} the reason.</summary>
    public const string SnapshotNotConfirmedFormat = "The snapshot {0} could not be read back as written, so nothing was written: {1}";

    /// <summary>S12: the reason when the snapshot's plan hash is not the plan's.</summary>
    public const string SnapshotPlanHashReason = "its plan hash is not the plan's";

    /// <summary>S12: the reason when the snapshot's target is not the value the checks read; {0} the variable name.</summary>
    public const string SnapshotTargetReasonFormat = "{0} in the snapshot is not the value the checks read";

    /// <summary>S12 or S14: the drift read itself failed; {0} the variable name, {1} the read status.</summary>
    public const string DriftReadFailedFormat = "{0} could not be read again before the write ({1}); nothing was written.";

    /// <summary>A stage stopped on an unexpected error; {0} the check name, {1} the error.</summary>
    public const string StageFailedFormat = "The {0} stage stopped on an unexpected error ({1}); nothing was written.";

    /// <summary>S15: the plan failed the last check before the call; {0} the reason.</summary>
    public const string WritePlanRefusedFormat = "The change no longer has the shape the checks approved ({0}); nothing was written.";

    /// <summary>S15 reason: the variable is not on the whitelist for this operation.</summary>
    public const string NotWhitelistedReason = "the variable is not on the whitelist for this operation";

    /// <summary>S15 reason: the attributes are not NV BS RT.</summary>
    public const string AttributesReason = "the attributes are not NV BS RT";

    /// <summary>S15 reason: a reorder of another size than the order read.</summary>
    public const string ReorderSizeReason = "the new order is not the size of the order read";

    /// <summary>S15 reason: a one-time boot that is not 2 bytes.</summary>
    public const string BootNextSizeReason = "a one-time boot is not 2 bytes";

    /// <summary>S15 reason: a clear that is not the removal of an existing one-time boot.</summary>
    public const string ClearReason = "a clear is not the removal of an existing one-time boot";

    /// <summary>S15 reason: the bytes to write are not the plan's.</summary>
    public const string BytesReason = "the bytes to write are not the plan's";

    /// <summary>S15: the text recorded when the writer ended in an exception; {0} the exception type, {1} its message.</summary>
    public const string WriterEndedFormat = "the call ended in {0}: {1}";

    /// <summary>
    /// S16 after a write call that ended in an exception, not in a firmware answer, when the value
    /// read back is the old one (outcome ApiRefused, exit 8); {0} the exception as "type: message",
    /// {1} the snapshot name.
    /// </summary>
    public const string WriterEndedUnchangedFormat = "The write call ended unexpectedly ({0}) and the value read back is the old one. Nothing was changed. Snapshot {1}.";

    /// <summary>
    /// S16 after a write call that ended in an exception when the value read back is the new one
    /// (outcome ChangedDespiteError, exit 9); {0} the exception as "type: message", {1} the snapshot name.
    /// </summary>
    public const string WriterEndedChangedFormat = "The write call ended unexpectedly ({0}) but the change is present. Snapshot {1}.";

    /// <summary>
    /// S16 after a write call that ended in an exception when the value read back is neither the
    /// old nor the new one, or could not be read (outcome Unverified, exit 9); {0} the exception as
    /// "type: message", {1} the reason, {2} the snapshot name. It does not say the change was written.
    /// </summary>
    public const string WriterEndedUnverifiedFormat = "The write call ended unexpectedly ({0}) and the change could not be verified: {1}. Snapshot {2}. Use Restore previous value (Alt+P) or check with uefiaccess boot.";

    /// <summary>The sentence of the record.json written at S12, replaced at S17.</summary>
    public const string Pending = "The change is being written; this record is replaced when the result is known.";

    /// <summary>Verified reorder; {0} the new order as "Boot0003 (description), Boot0002 (description)", {1} the snapshot name.</summary>
    public const string VerifiedReorderFormat = "Boot order changed to {0}. Restart to use the new order. Snapshot {1}.";

    /// <summary>Verified one-time boot; {0} the entry with its description, {1} the snapshot name.</summary>
    public const string VerifiedBootNextFormat = "One-time boot: {0} will start once at the next restart. Snapshot {1}.";

    /// <summary>Verified clear, and a verified restore that cleared; {0} the snapshot name.</summary>
    public const string VerifiedClearFormat = "One-time boot choice removed. Snapshot {0}.";

    /// <summary>Verified restore of a boot order; {0} the order, {1} the snapshot name.</summary>
    public const string RestoredReorderFormat = "Boot order restored to {0}. Restart to use it. Snapshot {1}.";

    /// <summary>Verified restore of a one-time boot; {0} the entry with its description, {1} the snapshot name.</summary>
    public const string RestoredBootNextFormat = "One-time boot restored: {0} will start once at the next restart. Snapshot {1}.";

    /// <summary>Appended to a verified change after a confirmed suspend.</summary>
    public const string SuspendedForOneRestart = "BitLocker is suspended for one restart and turns back on by itself at the next start.";

    /// <summary>Appended to a refusal of the firmware; {0} the snapshot name.</summary>
    public const string SnapshotNameFormat = "Snapshot {0}.";

    /// <summary>Unverified after a call that succeeded (the design's sentence, section 5.6); {0} the reason, {1} the snapshot name.</summary>
    public const string UnverifiedFormat = "The change was written but could not be verified: {0}. Snapshot {1}. Use Restore previous value (Alt+P) or check with uefiaccess boot.";

    /// <summary>
    /// Unverified after a call that failed: the value read back is neither the old nor the new one,
    /// or could not be read, so whether anything changed is not known and the sentence does not say
    /// the change was written; {0} the Win32 error, {1} the reason, {2} the snapshot name.
    /// </summary>
    public const string FailedUnverifiedFormat = "The firmware reported error {0} and the change could not be verified: {1}. Snapshot {2}. Use Restore previous value (Alt+P) or check with uefiaccess boot.";

    /// <summary>Unverified reason: the call succeeded and the value read back is not the value written.</summary>
    public const string ReadBackDiffers = "the value read back is not the value written";

    /// <summary>Unverified reason: the verification read failed; {0} the read status.</summary>
    public const string ReadBackFailedFormat = "the value could not be read back ({0})";

    /// <summary>Unverified reason after a failed call: the value read back is neither the old nor the new one.</summary>
    public const string NeitherValue = "the value read back is neither the old nor the new one";

    /// <summary>S17: a refused change whose record could not be saved; {0} the reason.</summary>
    public const string RecordNotSavedRefusedFormat = "Nothing was changed; the record could not be saved: {0}";

    /// <summary>One entry in a sentence; {0} the Boot#### name, {1} its description.</summary>
    public const string EntryFormat = "{0} ({1})";

    /// <summary>The error text of every platform fact the pipeline does not read, in before\platform.json.</summary>
    public const string NotReadByThePipeline = "not read by the write pipeline";

    /// <summary>The error text when the store answered with nothing.</summary>
    public const string NoStoreResult = "the variable store returned no result";

    /// <summary>An exception as a reason; {0} its type, {1} its message.</summary>
    public const string ExceptionFormat = "{0}: {1}";

    /// <summary>The separator between the entries of an order in a sentence.</summary>
    public const string EntrySeparator = ", ";
}

/// <summary>
/// The two times a transaction reads: now, and the moment Windows last started (the start of the
/// write budget of stage S9). Production uses the system clock and the kernel's tick count; a
/// test passes fixed values. Nothing here schedules anything.
/// </summary>
internal sealed class TransactionClock
{
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<DateTimeOffset, DateTimeOffset> _startOf;

    /// <summary>Creates a clock.</summary>
    /// <param name="now">Returns the current time.</param>
    /// <param name="startOf">Returns the moment Windows last started, given the current time.</param>
    internal TransactionClock(Func<DateTimeOffset> now, Func<DateTimeOffset, DateTimeOffset> startOf)
    {
        ArgumentNullException.ThrowIfNull(now);
        ArgumentNullException.ThrowIfNull(startOf);
        _now = now;
        _startOf = startOf;
    }

    /// <summary>The system clock and <see cref="WriteRecordLog.CurrentStartTime"/>.</summary>
    internal static TransactionClock System { get; } = new(() => DateTimeOffset.Now, WriteRecordLog.CurrentStartTime);

    /// <summary>The current time.</summary>
    /// <returns>The time.</returns>
    internal DateTimeOffset Now() => _now();

    /// <summary>The moment Windows last started.</summary>
    /// <param name="now">The current time.</param>
    /// <returns>The start time.</returns>
    internal DateTimeOffset StartOf(DateTimeOffset now) => _startOf(now);
}

/// <summary>
/// One transaction of the write pipeline: <see cref="PrepareAsync"/>
/// runs stages S0 to S10 and never writes the firmware; <see cref="CommitAsync"/> runs S11 to S17
/// on the plan this transaction prepared and is not cancellable. The order of S12 to S14 is the
/// design's: the snapshot and the first drift read before any BitLocker suspend, the suspend
/// only after both passed and only when chosen, the second drift read immediately before the
/// one call. The writer's one method is called from exactly one line of this file and nothing
/// here loops around it or calls it again, whatever it returns. Every outcome is recorded before
/// the result is returned: one line in write-log.jsonl, and record.json in the snapshot folder
/// once S12 created it. A failure after the call never changes the outcome or the exit code.
/// Stage S12 first takes the write record log's cross-process commit lock and counts the write
/// budget again under it; the lock is held until S17, so no other commit, in this process or
/// another, counts the same budget or writes at the same time. It then appends the transaction's
/// Pending intent line to write-log.jsonl, which the budget counts until the transaction's own
/// line follows it, and refuses when that line cannot be appended. Before any suspend, S12 also
/// reads BitLocker's state again and refuses a state other than the one the dry run showed; the
/// drift reads of S12 and S14 cover the target, BootNext beside BootOrder, and every variable
/// the rules read (<see cref="ChangePlan.Dependencies"/>).
/// </summary>
internal sealed class WriteTransaction
{
    private const string CheckWord = "check";
    private const string RestoreWord = "restore";
    private const string CheckOperation = "Check";
    private const string RestoreOperation = "Restore";
    private const string LogLineFormat = "{0:yyyy-MM-dd HH:mm:ss.fff zzz} {1}";
    private const string LogCheckFormat = "{0} {1}{2}";
    private const string LogPassed = "passed";
    private const string LogWarned = "passed with a warning";
    private const string LogFailed = "failed";
    private const string LogReasonFormat = ": {0}";
    private const string LogRestoreFormat = "restore of {0} resolved to {1}";
    private const string LogOutcomeFormat = "outcome {0}, exit {1}: {2}";
    private const string LogCallFormat = "firmware write {0}: {1}, Win32 {2}";
    private const string AlreadyPreparedMessage = "A transaction prepares one request; create a new transaction for another.";
    private const string DetailFormat = " ({0})";
    private const string SentenceSeparator = " ";

    private static readonly Guid Global = KnownGuids.EfiGlobalVariable;

    private readonly IFirmwareTypeDetector _firmwareTypeDetector;
    private readonly IPrivilegeManager _privilegeManager;
    private readonly IFirmwareVariableStore _store;
    private readonly PlatformPolicy _platformPolicy;
    private readonly IFirmwareVariableWriter _writer;
    private readonly IBitLockerSuspender _suspender;
    private readonly SnapshotStore _snapshots;
    private readonly WriteRecordLog _records;
    private readonly string _baselineRoot;
    private readonly ILogger _logger;
    private readonly TransactionClock _clock;
    private readonly object _gate = new();
    private readonly List<CheckResult> _checks = new();
    private readonly List<string> _log = new();
    private readonly List<string> _validationWarnings = new();

    private bool _prepareStarted;
    private bool _planUsed;
    private string _stage = CheckNames.Preconditions;
    private ChangeRequest? _originalRequest;
    private ChangeRequest? _request;
    private BitLockerChoice? _preparedChoice;
    private DateTimeOffset _startedAt;
    private string _transactionId = string.Empty;
    private FirmwareType _firmwareType = FirmwareType.Unknown;
    private PrivilegeState _privilege = PrivilegeState.NotElevated;
    private MachineIdentityResult? _identity;
    private bool _modelConfirmed;
    private UnitMatch? _unit;
    private bool _machineConfirmed;
    private string? _machineWarning;
    private EnumerationResult? _enumeration;
    private VariableSet? _variables;
    private ValidationResult? _validation;
    private VariableImage? _bootNextBefore;
    private bool _secureBootRead;
    private SecureBootPolicyResult? _secureBoot;
    private string? _secureBootSentence;
    private string? _baselineFolder;
    private string? _baselineTargetSha256;
    private bool? _baselineMatches;
    private string? _baselineWarning;
    private BitLockerResult? _bitLockerRead;
    private TpmInfoResult? _tpm;
    private BitLockerAssessment? _bitLocker;
    private int? _writesThisStart;
    private bool _bootIdRead;
    private uint? _bootId;
    private ChangePlan? _plan;

    /// <summary>Creates a transaction over the pipeline's services.</summary>
    /// <param name="firmwareTypeDetector">Firmware type detection (S1).</param>
    /// <param name="privilegeManager">Elevation and the privilege (S1).</param>
    /// <param name="store">The read-only store (S3, S12, S14, S16).</param>
    /// <param name="platformPolicy">Identity, Secure Boot, BitLocker and TPM reads (S2, S7, S8).</param>
    /// <param name="writer">The writer of the one call (S15).</param>
    /// <param name="suspender">The one-restart suspend (S13).</param>
    /// <param name="snapshots">The snapshot store (S0, S9, S12, S16, S17).</param>
    /// <param name="records">The write record log (S9, S17).</param>
    /// <param name="baselineRoot">The folder whose newest baseline S7 compares with.</param>
    /// <param name="logger">Logger; the log names the variable key, never the write API.</param>
    /// <param name="clock">Now, and the moment Windows last started.</param>
    internal WriteTransaction(
        IFirmwareTypeDetector firmwareTypeDetector,
        IPrivilegeManager privilegeManager,
        IFirmwareVariableStore store,
        PlatformPolicy platformPolicy,
        IFirmwareVariableWriter writer,
        IBitLockerSuspender suspender,
        SnapshotStore snapshots,
        WriteRecordLog records,
        string baselineRoot,
        ILogger logger,
        TransactionClock clock)
    {
        _firmwareTypeDetector = firmwareTypeDetector;
        _privilegeManager = privilegeManager;
        _store = store;
        _platformPolicy = platformPolicy;
        _writer = writer;
        _suspender = suspender;
        _snapshots = snapshots;
        _records = records;
        _baselineRoot = baselineRoot;
        _logger = logger;
        _clock = clock;
    }

    /// <summary>
    /// Runs stages S0 to S10 on a worker thread. Never writes the firmware and creates no
    /// transaction folder; appends one record line. A cancellation between stages gives a
    /// cancelled result, never an exception.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="bitLockerChoice">The BitLocker choice given with the request, or null to leave it to the confirmation.</param>
    /// <param name="token">Checked between stages.</param>
    /// <returns>The result.</returns>
    public Task<PrepareResult> PrepareAsync(ChangeRequest request, BitLockerChoice? bitLockerChoice, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            if (_prepareStarted)
            {
                throw new InvalidOperationException(AlreadyPreparedMessage);
            }

            _prepareStarted = true;
        }

        return Task.Run(() => Prepare(request, bitLockerChoice, token), CancellationToken.None);
    }

    /// <summary>Runs stages S11 to S17 on a worker thread. Not cancellable; never throws for anything the stages meet.</summary>
    /// <param name="plan">The plan this transaction prepared.</param>
    /// <param name="token">The confirmation.</param>
    /// <returns>The result, recorded before it is returned.</returns>
    public Task<TransactionResult> CommitAsync(ChangePlan plan, ConfirmationToken token)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(token);
        return Task.Run(() => Commit(plan, token), CancellationToken.None);
    }

    /// <summary>Records a cancel at the confirmation (S11): one cancelled record line, no folder; the plan can no longer be committed.</summary>
    /// <param name="plan">The plan the dialog showed.</param>
    /// <returns>The cancelled result.</returns>
    public Task<TransactionResult> CancelAsync(ChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Task.Run(() => Cancel(plan), CancellationToken.None);
    }

    // ---- S0 to S10 ----------------------------------------------------------------------

    private PrepareResult Prepare(ChangeRequest request, BitLockerChoice? bitLockerChoice, CancellationToken token)
    {
        _originalRequest = request;
        _request = request;
        _preparedChoice = bitLockerChoice;
        _startedAt = _clock.Now();
        _transactionId = TransactionIdFor(request);
        try
        {
            return RunStages(request, bitLockerChoice, token);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "write pipeline {TransactionId}: the checks stopped at {Stage}", _transactionId, _stage);
            return PrepareBlocked(_stage, CheckSentences.Format(TransactionSentences.StageFailedFormat, _stage, ex.Message), WriteExitCodes.Blocked, recordCheck: true);
        }
    }

    private PrepareResult RunStages(ChangeRequest request, BitLockerChoice? bitLockerChoice, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return PrepareCancelled();
        }

        // S0: a restore becomes one of the three operations, against the current firmware.
        if (request is RestoreRequest restore)
        {
            _stage = CheckNames.Snapshot;
            RestoreDerivation derived = SnapshotStore.DeriveRestore(_snapshots.Read(restore.SnapshotFolder), restore.Host);
            if (derived.Request is not ChangeRequest resolved)
            {
                return PrepareBlocked(CheckNames.Snapshot, derived.Refusal ?? SnapshotStore.UnknownReason, WriteExitCodes.Blocked, recordCheck: true);
            }

            request = resolved;
            _request = resolved;
            _transactionId = TransactionIdFor(resolved);
            Note(CheckSentences.Format(LogRestoreFormat, restore.SnapshotFolder, OperationName(null, resolved)));
        }

        // S1
        _stage = CheckNames.Preconditions;
        PrepareResult? refused = CheckPreconditions();
        if (refused is not null)
        {
            return refused;
        }

        if (token.IsCancellationRequested)
        {
            return PrepareCancelled();
        }

        // S2: recorded, never refused here; S11 refuses a machine that is not the M3 test unit:
        // the model and BIOS version first (expectedModel), then this Windows installation
        // (expectedUnit), both recorded whatever the first found.
        _stage = CheckNames.ExpectedModel;
        MachineIdentityResult identity = _platformPolicy.ReadIdentity();
        _identity = identity;
        _modelConfirmed = PlatformPolicy.IsContractedMachine(identity);
        if (_modelConfirmed)
        {
            Pass(CheckNames.ExpectedModel);
        }
        else
        {
            _machineWarning = WritabilitySentences.WrongMachine;
            Warn(CheckNames.ExpectedModel, _machineWarning);
        }

        _stage = CheckNames.ExpectedUnit;
        UnitMatch unit = _platformPolicy.ReadUnit();
        _unit = unit;
        _machineConfirmed = PlatformPolicy.IsEnabledMachine(identity, unit);
        if (unit == UnitMatch.Confirmed)
        {
            Pass(CheckNames.ExpectedUnit);
        }
        else
        {
            // The unit's own sentence says "This OptiPlex 7020", so another model gets the
            // Writability sentence here; an unreadable machine ID says so on any machine.
            _machineWarning = WritabilitySentences.WrongMachine;
            Warn(CheckNames.ExpectedUnit, unit == UnitMatch.MachineIdNotRead || _modelConfirmed ? UnitRefusal(unit) : WritabilitySentences.WrongMachine);
        }

        if (token.IsCancellationRequested)
        {
            return PrepareCancelled();
        }

        // S3: one fresh enumeration, then direct reads of the variables the change touches.
        _stage = CheckNames.Enumeration;
        EnumerationResult enumeration = Enumerate();
        _enumeration = enumeration;
        VariableSet variables = ReadVariables(request, enumeration, out refused);
        _variables = variables;
        if (refused is not null)
        {
            return refused;
        }

        if (token.IsCancellationRequested)
        {
            return PrepareCancelled();
        }

        // S4 to S6. Rules V13, V16 and V17 need the BitLocker state, so a reorder or a one-time
        // boot reads it here; the bitLocker check itself is recorded at S8, in stage order.
        _stage = CheckNames.Whitelist;
        BitLockerState stateForRules = BitLockerState.Off;
        if (request is ReorderRequest or BootNextRequest)
        {
            stateForRules = AssessBitLocker().State;
        }

        ValidationResult validation = ChangeValidator.Validate(request, variables, enumeration, stateForRules);
        _validation = validation;
        foreach (CheckResult check in validation.Checks)
        {
            Record(check);
        }

        _validationWarnings.AddRange(validation.Warnings);
        if (!validation.Passed)
        {
            return PrepareBlocked(validation.BlockedBy ?? CheckNames.Whitelist, validation.BlockedReason ?? string.Empty, WriteExitCodes.Blocked, recordCheck: false);
        }

        _bootNextBefore = ImageOf(variables.Find(Whitelist.BootNext.Name, Whitelist.BootNext.Guid), Whitelist.BootNext.RequiredAttributes);
        if (token.IsCancellationRequested)
        {
            return PrepareCancelled();
        }

        // S7: informational.
        _stage = CheckNames.SecureBoot;
        _secureBoot = _platformPolicy.ReadSecureBoot();
        _secureBootRead = true;
        _secureBootSentence = PlatformPolicy.DescribeSecureBoot(_secureBoot);
        Warn(CheckNames.SecureBoot, _secureBootSentence);
        _stage = CheckNames.Baseline;
        CompareWithBaseline(request, validation);
        if (token.IsCancellationRequested)
        {
            return PrepareCancelled();
        }

        // S8
        _stage = CheckNames.BitLocker;
        refused = CheckBitLocker(validation, bitLockerChoice);
        if (refused is not null)
        {
            return refused;
        }

        if (token.IsCancellationRequested)
        {
            return PrepareCancelled();
        }

        // S9: both data roots (created here when missing), then the write budget; a list of
        // writes that cannot be read whole refuses, since the writes of this start are then unknown.
        _stage = CheckNames.SnapshotRoot;
        string? rootRefusal = _snapshots.PrepareRoot() ?? _records.PrepareRoot();
        if (rootRefusal is not null)
        {
            return PrepareBlocked(CheckNames.SnapshotRoot, rootRefusal, WriteExitCodes.Blocked, recordCheck: true);
        }

        Pass(CheckNames.SnapshotRoot);
        _stage = CheckNames.WriteBudget;
        string? budgetRefusal = CountWriteBudget();
        if (budgetRefusal is not null)
        {
            return PrepareBlocked(CheckNames.WriteBudget, budgetRefusal, WriteExitCodes.Blocked, recordCheck: true);
        }

        Pass(CheckNames.WriteBudget);
        if (token.IsCancellationRequested)
        {
            return PrepareCancelled();
        }

        // S10
        return PreparePassed(identity);
    }

    /// <summary>
    /// The write budget (S9, and S12 under the commit lock): the writes made since Windows last
    /// started, counted in write-log.jsonl by the clock and by the boot identity
    /// (<see cref="WriteRecordLog.CountSinceStart(DateTimeOffset, uint?)"/>), recorded as
    /// <see cref="WriteRecord.WritesThisStart"/>. Null when one more write fits; otherwise the
    /// refusal, which is also the answer when the list exists but cannot be read whole. A refusal
    /// whose count includes lines that are not whole records says how many.
    /// </summary>
    private string? CountWriteBudget()
    {
        WriteBudgetCount count = _records.CountSinceStart(_clock.StartOf(_clock.Now()), BootId());
        if (count.Problem is string problem)
        {
            return CheckSentences.Format(CheckSentences.WriteLogUnreadableFormat, problem);
        }

        _writesThisStart = count.Count;
        if (WriteRecordLog.IsWithinBudget(count.Count))
        {
            return null;
        }

        string refusal = CheckSentences.Format(CheckSentences.WriteBudgetExceededFormat, count.Count);
        return count.NotRecords > 0
            ? refusal + SentenceSeparator + CheckSentences.Format(CheckSentences.WriteLogFragmentsFormat, count.NotRecords)
            : refusal;
    }

    /// <summary>
    /// The current boot's identity as the write record log reads it, read once per transaction and
    /// kept, so every record of the transaction carries the same one and the last-resort record
    /// after the call needs no read. Null when it cannot be read; never throws.
    /// </summary>
    private uint? BootId()
    {
        if (!_bootIdRead)
        {
            _bootId = _records.CurrentBootId();
            _bootIdRead = true;
        }

        return _bootId;
    }

    private PrepareResult? CheckPreconditions()
    {
        try
        {
            _firmwareType = _firmwareTypeDetector.Detect();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "write pipeline: firmware type detection threw");
            _firmwareType = FirmwareType.Unknown;
        }

        if (_firmwareType != FirmwareType.Uefi)
        {
            return PrepareBlocked(CheckNames.Preconditions, TransactionSentences.NotUefi, WriteExitCodes.NotUefi, recordCheck: true);
        }

        bool elevated;
        try
        {
            elevated = _privilegeManager.IsElevated;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "write pipeline: the elevation check threw");
            elevated = false;
        }

        if (!elevated)
        {
            _privilege = PrivilegeState.NotElevated;
            return PrepareBlocked(CheckNames.Preconditions, TransactionSentences.NotElevated, WriteExitCodes.NotElevated, recordCheck: true);
        }

        string? detail = null;
        try
        {
            _privilege = _privilegeManager.EnableSystemEnvironmentPrivilege();
            detail = (_privilegeManager as PrivilegeManager)?.LastDetail;
        }
        catch (Exception ex)
        {
            _privilege = PrivilegeState.ElevatedPrivilegeAbsent;
            detail = CheckSentences.Format(TransactionSentences.ExceptionFormat, ex.GetType().Name, ex.Message);
        }

        if (_privilege == PrivilegeState.NotElevated)
        {
            return PrepareBlocked(CheckNames.Preconditions, TransactionSentences.NotElevated, WriteExitCodes.NotElevated, recordCheck: true);
        }

        if (_privilege != PrivilegeState.Enabled)
        {
            string reason = string.IsNullOrWhiteSpace(detail) ? string.Empty : CheckSentences.Format(DetailFormat, detail.Trim());
            return PrepareBlocked(CheckNames.Preconditions, CheckSentences.Format(TransactionSentences.PrivilegeAbsentFormat, reason), WriteExitCodes.NotElevated, recordCheck: true);
        }

        Pass(CheckNames.Preconditions);
        return null;
    }

    private EnumerationResult Enumerate()
    {
        try
        {
            return _store.Enumerate() ?? new EnumerationResult(Array.Empty<FirmwareVariable>(), EnumerationMethod.None, 0, TransactionSentences.NoStoreResult, Array.Empty<string>());
        }
        catch (Exception ex)
        {
            return new EnumerationResult(Array.Empty<FirmwareVariable>(), EnumerationMethod.None, 0, CheckSentences.Format(TransactionSentences.ExceptionFormat, ex.GetType().Name, ex.Message), Array.Empty<string>());
        }
    }

    /// <summary>
    /// The set the rules judge: the enumeration with the direct reads of stage S3 merged in. A
    /// direct read that fails gives the set read so far and the refusal in <paramref name="refused"/>.
    /// </summary>
    private VariableSet ReadVariables(ChangeRequest request, EnumerationResult enumeration, out PrepareResult? refused)
    {
        refused = null;
        Dictionary<(Guid Guid, string Name), FirmwareVariable> merged = new();
        foreach (FirmwareVariable variable in enumeration.Variables ?? Array.Empty<FirmwareVariable>())
        {
            if (variable is not null)
            {
                merged.TryAdd((variable.VendorGuid, variable.Name), variable);
            }
        }

        // An incomplete enumeration is refused by the validator's first rule (V15); reading
        // directly after it would only hide what the list lacked.
        if (ChangeValidator.IsCompleteEnumeration(enumeration))
        {
            foreach ((string name, Guid guid) in DirectReadNames(request))
            {
                ReadResult read = SafeRead(name, guid);
                if (read.Status == ReadStatus.Success && read.Variable is not null)
                {
                    merged[(guid, name)] = read.Variable;
                }
                else if (read.Status == ReadStatus.NotFound)
                {
                    merged.Remove((guid, name));
                }
                else
                {
                    _variables = new VariableSet(merged.Values);
                    string reason = CheckSentences.Format(TransactionSentences.DirectReadFailedFormat, name, read.Status);
                    refused = PrepareBlocked(CheckNames.Enumeration, CheckSentences.Format(CheckSentences.EnumerationIncompleteFormat, reason), WriteExitCodes.Blocked, recordCheck: true);
                    return _variables;
                }
            }
        }

        return new VariableSet(merged.Values);
    }

    private void CompareWithBaseline(ChangeRequest request, ValidationResult validation)
    {
        (string name, Guid guid) = TargetOf(request, validation);
        VariableImage current = validation.Before ?? VariableImage.Absent(0);
        string? newest = NewestBaseline();
        if (newest is null)
        {
            Record(new CheckResult(CheckNames.Baseline, true, TransactionSentences.BaselineNotCompared));
            return;
        }

        _baselineFolder = newest;
        BaselineReadResult read = BaselineReader.Read(newest);
        if (read.Error is not null)
        {
            Record(new CheckResult(CheckNames.Baseline, true, CheckSentences.Format(TransactionSentences.BaselineUnreadableFormat, Path.GetFileName(newest), read.Error)));
            return;
        }

        FirmwareVariable? target = read.Variables.FirstOrDefault(variable => variable.VendorGuid == guid && string.Equals(variable.Name, name, StringComparison.Ordinal));
        _baselineTargetSha256 = target is null ? null : VariableImage.Sha256Of(target.Data);
        bool matches = current.Present
            ? string.Equals(_baselineTargetSha256, current.Sha256, StringComparison.OrdinalIgnoreCase)
            : target is null;
        _baselineMatches = matches;
        if (matches)
        {
            Pass(CheckNames.Baseline);
            return;
        }

        _baselineWarning = CheckSentences.Format(CheckSentences.BaselineDiffersFormat, name, Path.GetFileName(newest));
        Warn(CheckNames.Baseline, _baselineWarning);
    }

    private string? NewestBaseline()
    {
        try
        {
            if (!Directory.Exists(_baselineRoot))
            {
                return null;
            }

            return Directory.EnumerateDirectories(_baselineRoot)
                .Where(folder => File.Exists(Path.Combine(folder, BaselineExporter.IndexFileName)))
                .OrderByDescending(folder => Path.GetFileName(folder), StringComparer.Ordinal)
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "write pipeline: the baseline root {Root} could not be listed", _baselineRoot);
            return null;
        }
    }

    private BitLockerAssessment AssessBitLocker()
    {
        if (_bitLocker is null)
        {
            _bitLockerRead = _platformPolicy.ReadBitLocker();
            _tpm = _platformPolicy.ReadTpm();
            _bitLocker = BitLockerPreflight.Assess(_bitLockerRead, BitLockerPreflight.OsDriveLetter, _tpm);
        }

        return _bitLocker;
    }

    private PrepareResult? CheckBitLocker(ValidationResult validation, BitLockerChoice? bitLockerChoice)
    {
        BitLockerAssessment assessment = AssessBitLocker();
        bool isChange = validation.IsPlan;
        bool offered = isChange && SuspendOffered(assessment, validation.Operation);
        if (isChange && bitLockerChoice is BitLockerChoice given)
        {
            if (offered && given == BitLockerChoice.NotNeeded)
            {
                return PrepareBlocked(CheckNames.BitLocker, CheckSentences.BitLockerChoiceMissing, WriteExitCodes.Blocked, recordCheck: true);
            }

            if (!offered && given == BitLockerChoice.SuspendForOneRestart)
            {
                return PrepareBlocked(CheckNames.BitLocker, TransactionSentences.SuspendNotOffered, WriteExitCodes.Blocked, recordCheck: true);
            }
        }

        Record(new CheckResult(CheckNames.BitLocker, true, assessment.StateSentence, assessment.WarningShown));
        return null;
    }

    private PrepareResult PreparePassed(MachineIdentityResult identity)
    {
        // A check makes no plan, so no S11 follows to refuse it on a machine that is not the M3
        // test unit: it is refused here, after S9, with the Writability sentence, so the record
        // says what the check reports (exit 7, blocked by expectedModel for another model or
        // BIOS version, by expectedUnit for another OptiPlex 7020 on A18; design 4.1, 4.2).
        if (_request is CheckRequest && !_machineConfirmed)
        {
            string machineCheck = _modelConfirmed ? CheckNames.ExpectedUnit : CheckNames.ExpectedModel;
            return PrepareBlocked(machineCheck, WritabilitySentences.WrongMachine, WriteExitCodes.Blocked, recordCheck: true);
        }

        ChangePlan? plan = null;
        string sentence = TransactionSentences.ChecksPassed;
        WritabilityVerdict? verdict = CheckVerdict();
        if (_validation is { IsPlan: true } validation)
        {
            PolicyFindings findings = new(
                _secureBoot,
                identity,
                _machineConfirmed,
                AssessBitLocker(),
                _baselineFolder,
                _baselineMatches,
                Warnings())
            {
                Unit = _unit ?? UnitMatch.MachineIdNotRead,
            };
            plan = new ChangePlan(validation, findings, _checks.ToArray(), _startedAt);
            _plan = plan;
            _transactionId = plan.SnapshotFolderName;
        }
        else if (verdict is not null)
        {
            sentence = verdict.Sentence;
        }

        WriteRecord record = MakeRecord(WriteRecordOutcome.Prepared, WriteExitCodes.Success, sentence, null, null, plan);
        AppendPrepareRecord(record);
        return new PrepareResult(plan, _checks.ToArray(), null, sentence, record)
        {
            BitLocker = _bitLocker,
            Warnings = Warnings(),
            Writability = verdict,
        };
    }

    private PrepareResult PrepareBlocked(string check, string reason, int exitCode, bool recordCheck)
    {
        if (recordCheck)
        {
            Record(new CheckResult(check, false, reason));
        }

        WriteRecord record = MakeRecord(WriteRecordOutcome.Blocked, exitCode, reason, check, null, null);
        AppendPrepareRecord(record);
        return new PrepareResult(null, _checks.ToArray(), check, reason, record)
        {
            BitLocker = _bitLocker,
            Warnings = Warnings(),
            Writability = CheckVerdict(),
        };
    }

    private PrepareResult PrepareCancelled()
    {
        WriteRecord record = MakeRecord(WriteRecordOutcome.Cancelled, WriteExitCodes.Success, TransactionSentences.Cancelled, null, null, null);
        AppendPrepareRecord(record);
        return new PrepareResult(null, _checks.ToArray(), null, TransactionSentences.Cancelled, record)
        {
            BitLocker = _bitLocker,
            Warnings = Warnings(),
        };
    }

    private void AppendPrepareRecord(WriteRecord record)
    {
        string? problem = SafeAppend(record);
        if (problem is not null)
        {
            _logger.Warning("write pipeline {TransactionId}: the record line could not be appended: {Problem}", record.TransactionId, problem);
        }

        LogEvent(record);
    }

    private WritabilityVerdict? CheckVerdict()
    {
        if (_request is not CheckRequest check || _variables is null)
        {
            return null;
        }

        FirmwareVariable? variable = _variables.Find(check.Name, check.Guid);
        return variable is null ? null : WritabilityPolicy.Describe(variable, _variables, offline: false, _machineConfirmed);
    }

    /// <summary>The dialog's Warnings, in stage order: Secure Boot, the rules' warnings, the baseline, the machine.</summary>
    private IReadOnlyList<string> Warnings()
    {
        List<string> warnings = new();
        if (_secureBootSentence is not null)
        {
            warnings.Add(_secureBootSentence);
        }

        warnings.AddRange(_validationWarnings);
        if (_baselineWarning is not null)
        {
            warnings.Add(_baselineWarning);
        }

        if (_machineWarning is not null)
        {
            warnings.Add(_machineWarning);
        }

        return warnings;
    }

    // ---- S11 to S17 ---------------------------------------------------------------------

    private TransactionResult Commit(ChangePlan plan, ConfirmationToken token)
    {
        WriteProgress progress = new(plan, token.Choice);
        Adopt(plan);
        try
        {
            TransactionResult? stopped;
            try
            {
                stopped = Confirm(plan, token, progress)
                    ?? TakeSnapshot(plan, progress)
                    ?? RecheckBitLocker(plan, progress)
                    ?? SuspendIfChosen(plan, progress)
                    ?? CheckDrift(plan, progress);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "write pipeline {TransactionId}: the commit stopped at {Stage}", _transactionId, progress.Stage);
                stopped = Stop(progress.Stage, CheckSentences.Format(TransactionSentences.StageFailedFormat, progress.Stage, ex.Message), progress);
            }

            return stopped ?? CommitWriteAndVerify(plan, progress);
        }
        finally
        {
            // S17 is over, whatever the outcome: the commit lock S12 took is released, on this thread.
            progress.CommitLock?.Dispose();
        }
    }

    private TransactionResult Cancel(ChangePlan plan)
    {
        WriteProgress progress = new(plan, null);
        Adopt(plan);
        lock (_gate)
        {
            if (ReferenceEquals(_plan, plan))
            {
                _planUsed = true;
            }
        }

        return Finish(TransactionOutcome.Cancelled, WriteExitCodes.Success, TransactionSentences.Cancelled, null, progress);
    }

    /// <summary>A transaction that did not prepare this plan still records what the plan says about itself.</summary>
    private void Adopt(ChangePlan plan)
    {
        if (_request is null)
        {
            _request = plan.Request;
            _startedAt = plan.CreatedAt;
            _checks.AddRange(plan.Checks);
        }

        if (string.IsNullOrEmpty(_transactionId))
        {
            _transactionId = plan.SnapshotFolderName;
        }
    }

    // S11
    private TransactionResult? Confirm(ChangePlan plan, ConfirmationToken token, WriteProgress progress)
    {
        progress.Stage = CheckNames.Confirmation;
        bool prepared;
        lock (_gate)
        {
            prepared = ReferenceEquals(_plan, plan) && !_planUsed;
            _planUsed |= prepared;
        }

        if (!prepared)
        {
            return Stop(CheckNames.Confirmation, TransactionSentences.PlanNotPrepared, progress);
        }

        if (!token.Matches(plan))
        {
            return Stop(CheckNames.Confirmation, CheckSentences.TokenDoesNotMatchPlan, progress);
        }

        if (!IsIntact(plan))
        {
            return Stop(CheckNames.Confirmation, TransactionSentences.PlanAltered, progress);
        }

        if (_preparedChoice is BitLockerChoice given && given != token.Choice)
        {
            return Stop(CheckNames.Confirmation, TransactionSentences.ChoiceDiffersFromChecks, progress);
        }

        bool offered = SuspendOffered(plan.Policy.BitLocker, plan.Operation);
        if (offered && token.Choice == BitLockerChoice.NotNeeded)
        {
            return Stop(CheckNames.Confirmation, CheckSentences.BitLockerChoiceMissing, progress);
        }

        if (!offered && token.Choice == BitLockerChoice.SuspendForOneRestart)
        {
            return Stop(CheckNames.Confirmation, TransactionSentences.SuspendNotOffered, progress);
        }

        Pass(CheckNames.Confirmation);

        // The machine: the model and BIOS version first, then the unit, so the refusal names the
        // one that failed. Only the M3 test unit passes both.
        progress.Stage = CheckNames.ExpectedModel;
        if (!plan.Policy.ModelConfirmed)
        {
            return Stop(CheckNames.ExpectedModel, CheckSentences.WrongMachine, progress);
        }

        Pass(CheckNames.ExpectedModel);
        progress.Stage = CheckNames.ExpectedUnit;
        if (!plan.Policy.MachineConfirmed || plan.Policy.Unit != UnitMatch.Confirmed)
        {
            return Stop(CheckNames.ExpectedUnit, UnitRefusal(plan.Policy.Unit), progress);
        }

        Pass(CheckNames.ExpectedUnit);
        progress.Confirmed = true;
        return null;
    }

    /// <summary>The refusal of check expectedUnit: the machine ID could not be read, or it is another unit's.</summary>
    private static string UnitRefusal(UnitMatch unit)
    {
        return unit == UnitMatch.MachineIdNotRead ? CheckSentences.MachineIdNotRead : CheckSentences.NotEnabledUnit;
    }

    // S12: the record root, the commit lock and the budget counted again under it, the intent line
    // appended to write-log.jsonl, the folder written and read back, then the first drift read,
    // all before BitLocker is touched.
    private TransactionResult? TakeSnapshot(ChangePlan plan, WriteProgress progress)
    {
        progress.Stage = CheckNames.Snapshot;
        string? recordRefusal = _records.PrepareRoot();
        if (recordRefusal is not null)
        {
            return Stop(CheckNames.Snapshot, CheckSentences.Format(TransactionSentences.RecordRootRefusedFormat, recordRefusal), progress);
        }

        progress.Stage = CheckNames.WriteBudget;
        progress.CommitLock = _records.TryEnterCommit(out string? lockRefusal);
        if (progress.CommitLock is null)
        {
            return Stop(CheckNames.WriteBudget, lockRefusal ?? SnapshotStore.UnknownReason, progress);
        }

        string? budgetRefusal = CountWriteBudget();
        if (budgetRefusal is not null)
        {
            return Stop(CheckNames.WriteBudget, budgetRefusal, progress);
        }

        // The intent of the write, before the folder, the suspend and the call: the budget counts
        // this Pending line as a write until the transaction's own line follows it, so a write whose
        // S17 line is never appended (a killed process, a list another program holds open) still
        // counts. A list that cannot take this line cannot take the outcome either: nothing is written.
        string? intentProblem = SafeAppend(PendingRecord(plan, progress));
        if (intentProblem is not null)
        {
            return Stop(CheckNames.WriteBudget, CheckSentences.Format(TransactionSentences.RecordRootRefusedFormat, intentProblem), progress);
        }

        Pass(CheckNames.WriteBudget);
        progress.Stage = CheckNames.Snapshot;
        SnapshotCreateResult created = _snapshots.Create(BuildSnapshotRequest(plan, progress));
        progress.SnapshotFolder = created.Folder;
        if (!created.Succeeded || created.Folder is not string folder || created.Document is not SnapshotDocument document)
        {
            return Stop(CheckNames.Snapshot, CheckSentences.Format(TransactionSentences.SnapshotNotSavedFormat, created.Error ?? SnapshotStore.UnknownReason), progress);
        }

        progress.Document = document;
        string? mismatch = SnapshotMismatch(plan, _snapshots.Read(folder));
        if (mismatch is not null)
        {
            return Stop(CheckNames.Snapshot, CheckSentences.Format(TransactionSentences.SnapshotNotConfirmedFormat, Path.GetFileName(folder), mismatch), progress);
        }

        Pass(CheckNames.Snapshot);
        return CheckDrift(plan, progress);
    }

    // S12, last: BitLocker's state read again before any suspend, for the two operations whose
    // rules (V13, V16, V17) and whose choice depend on it. A state other than the one the dry run
    // showed refuses: a volume suspended meanwhile would get a second suspend, and one whose
    // protection came on would be written without its checks and without a choice.
    private TransactionResult? RecheckBitLocker(ChangePlan plan, WriteProgress progress)
    {
        if (plan.Operation is not (ChangeOperation.Reorder or ChangeOperation.SetBootNext))
        {
            return null;
        }

        progress.Stage = CheckNames.BitLocker;
        BitLockerAssessment shown = plan.Policy.BitLocker;
        BitLockerAssessment now = BitLockerPreflight.Assess(_platformPolicy.ReadBitLocker(), shown.DriveLetter, _tpm);
        progress.BitLockerNow = now;
        if (now.State != shown.State)
        {
            return Stop(CheckNames.BitLocker, CheckSentences.Format(CheckSentences.BitLockerStateChangedFormat, StateWord(shown.State), StateWord(now.State)), progress);
        }

        Record(new CheckResult(CheckNames.BitLocker, true, now.StateSentence, now.WarningShown));
        return null;
    }

    // S13: only when chosen, and only after the snapshot and the first drift read passed.
    private TransactionResult? SuspendIfChosen(ChangePlan plan, WriteProgress progress)
    {
        if (progress.Choice != BitLockerChoice.SuspendForOneRestart)
        {
            return null;
        }

        progress.Stage = CheckNames.BitLockerSuspend;
        SuspendResult result;
        try
        {
            result = _suspender.SuspendForOneRestart(plan.Policy.BitLocker?.DriveLetter ?? BitLockerPreflight.OsDriveLetter);
        }
        catch (Exception ex)
        {
            string reason = CheckSentences.Format(TransactionSentences.ExceptionFormat, ex.GetType().Name, ex.Message);
            result = new SuspendResult(false, true, BitLockerSuspender.OneRestart, null, false, null, null, BitLockerState.Unknown, BitLockerSuspender.FailedResultText, CheckSentences.Format(BitLockerSuspender.CouldNotSuspendFormat, reason), TimeSpan.Zero);
        }

        progress.Suspend = result;
        if (!result.Succeeded)
        {
            return Stop(CheckNames.BitLockerSuspend, result.Sentence ?? CheckSentences.Format(BitLockerSuspender.CouldNotSuspendFormat, SnapshotStore.UnknownReason), progress);
        }

        progress.Suspended = true;
        Pass(CheckNames.BitLockerSuspend);
        return null;
    }

    // S12 (first) and S14 (second): the target, BootNext beside BootOrder, and every variable the
    // rules read (the Boot#### entries, BootCurrent, BootOrder beside BootNext), byte-identical to
    // the plan's view, so no rule's verdict is stale at the call. The first difference refuses.
    private TransactionResult? CheckDrift(ChangePlan plan, WriteProgress progress)
    {
        progress.Stage = CheckNames.Stale;
        string? drift = DriftOf(plan.Variable.Name, plan.Variable.Guid, plan.Before);
        if (drift is null && plan.Variable.Kind == WritableKind.ReorderOnly && _bootNextBefore is not null)
        {
            drift = DriftOf(Whitelist.BootNext.Name, Whitelist.BootNext.Guid, _bootNextBefore);
        }

        drift ??= plan.Dependencies
            .Select(dependency => DriftOf(dependency.Name, dependency.Guid, dependency.Image))
            .FirstOrDefault(found => found is not null);
        if (drift is not null)
        {
            return Stop(CheckNames.Stale, drift, progress);
        }

        Pass(CheckNames.Stale);
        return null;
    }

    // S15 to S17: the last check of the plan's shape, the one call, the one verification read,
    // the record. Nothing here throws: the check before the call stops like the stages before it
    // (a Blocked record, the suspend reported), and nothing calls the writer a second time.
    private TransactionResult CommitWriteAndVerify(ChangePlan plan, WriteProgress progress)
    {
        progress.Stage = CheckNames.Format;
        WritePlan? writePlan;
        string? problem;
        try
        {
            writePlan = BuildWritePlan(plan, out problem);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "write pipeline {TransactionId}: the last check before the call stopped", _transactionId);
            return Stop(CheckNames.Format, CheckSentences.Format(TransactionSentences.StageFailedFormat, CheckNames.Format, ex.Message), progress);
        }

        if (writePlan is null)
        {
            return Stop(CheckNames.Format, CheckSentences.Format(TransactionSentences.WritePlanRefusedFormat, problem ?? SnapshotStore.UnknownReason), progress);
        }

        progress.ApiCalled = true;
        Stopwatch stopwatch = Stopwatch.StartNew();
        WriteOutcome api;
        try
        {
            api = _writer.CommitOnce(writePlan);
        }
        catch (Exception ex)
        {
            api = WriterEnded(writePlan, ex, stopwatch.Elapsed, progress);
        }

        progress.Api = api;
        try
        {
            // S16: the one read after the call.
            ReadResult read = SafeRead(plan.Variable.Name, plan.Variable.Guid);
            VerificationResult verification = VerificationOf(plan, read);
            FirmwareVariable? readBack = read.Status == ReadStatus.Success ? read.Variable : null;
            progress.Verification = verification;
            Note(CheckSentences.Format(LogCallFormat, writePlan.Key, api.Status, api.Win32Error));
            (TransactionOutcome outcome, int exitCode, string sentence) = Classify(plan, api, verification, progress);
            progress.AfterProblem = RecordReadBack(plan, progress, verification, readBack);
            return Finish(outcome, exitCode, sentence, null, progress);
        }
        catch (Exception ex)
        {
            return AfterCallFailure(plan, progress, ex);
        }
    }

    /// <summary>
    /// The last resort when something after the call threw: the result says the change is not
    /// verified (exit 9, never 5), and, when the call itself failed, does not claim it was written;
    /// after a confirmed suspend it says BitLocker stays suspended until the next restart, as every
    /// other outcome after the call does. The record line is attempted once more, with what is
    /// already known (the call's answer, the verification read, the checks, the BitLocker facts)
    /// but without the snapshot folder's files, and only when the S17 line was not appended.
    /// </summary>
    private TransactionResult AfterCallFailure(ChangePlan plan, WriteProgress progress, Exception ex)
    {
        _logger.Error(ex, "write pipeline {TransactionId}: the stages after the firmware write stopped", _transactionId);
        string reason = CheckSentences.Format(TransactionSentences.ExceptionFormat, ex.GetType().Name, ex.Message);
        string snapshot = SnapshotName(plan, progress);
        bool callFailed = progress.Api is { Succeeded: false };
        string outcomeSentence = !callFailed
            ? CheckSentences.Format(TransactionSentences.UnverifiedFormat, reason, snapshot)
            : progress.WriterEndedReason is string ended
                ? CheckSentences.Format(TransactionSentences.WriterEndedUnverifiedFormat, ended, reason, snapshot)
                : CheckSentences.Format(TransactionSentences.FailedUnverifiedFormat, progress.Api?.Win32Error, reason, snapshot);
        string left = progress.Suspended ? SentenceSeparator + CheckSentences.BitLockerLeftSuspended : string.Empty;
        string sentence = outcomeSentence
            + left
            + SentenceSeparator
            + CheckSentences.Format(callFailed ? CheckSentences.RecordNotSavedUnknownFormat : CheckSentences.RecordNotSavedUnverifiedFormat, reason);
        WriteRecord record = new()
        {
            TimestampUtc = WriteRecord.FormatTimestamp(DateTimeOffset.Now),
            TransactionId = plan.SnapshotFolderName,
            ToolVersion = ToolVersion.Current,
            Host = plan.Request.Host,
            Operation = plan.Operation.ToString(),
            IsRestore = plan.RestoredFrom is not null,
            RestoredFrom = plan.RestoredFrom,
            Variable = VariableOf(plan.Variable, plan.Request),
            Confirmed = true,
            ApiCalled = true,
            SnapshotFolder = progress.SnapshotFolder,
            Outcome = WriteRecordOutcome.Unverified,
            ExitCode = WriteExitCodes.Unverified,
            Sentence = sentence,
        };
        try
        {
            // What is already known, built without the clock or MakeRecord, either of which may be
            // what threw; a failure here keeps the record above.
            record = record with
            {
                BootId = _bootId,
                AfterSha256 = progress.Verification?.Sha256,
                Checks = _checks.ToArray(),
                BitLockerChoice = progress.Choice,
                BitLocker = BitLockerRecord(plan.Policy.BitLocker, progress, plan.Operation, progress.Choice),
                Api = ApiRecord(progress.Api),
                Verification = VerificationRecord(progress.Verification),
            };
        }
        catch (Exception fillFailure)
        {
            _logger.Error(fillFailure, "write pipeline {TransactionId}: the last-resort record could not be completed", _transactionId);
        }

        if (!progress.RecordAppended)
        {
            _ = SafeAppend(record);
        }

        return new TransactionResult(TransactionOutcome.Unverified, plan, progress.SnapshotFolder, progress.Api, progress.Verification, record, sentence, WriteExitCodes.Unverified)
        {
            BitLockerSuspended = progress.Suspended,
        };
    }

    /// <summary>
    /// The outcome of a writer that ended in an exception. Whether the firmware saw the call is not
    /// known, so the class is FirmwareError and the verification read decides the outcome; the
    /// exception is kept in <paramref name="progress"/>, so the sentence names it and does not say
    /// the firmware reported an error.
    /// </summary>
    private WriteOutcome WriterEnded(WritePlan writePlan, Exception ex, TimeSpan elapsed, WriteProgress progress)
    {
        _logger.Error(ex, "firmware write {Key}: the writer ended in an exception", writePlan.Key);
        progress.WriterEndedReason = CheckSentences.Format(TransactionSentences.ExceptionFormat, ex.GetType().Name, ex.Message);
        string text = CheckSentences.Format(TransactionSentences.WriterEndedFormat, ex.GetType().Name, ex.Message);
        return new WriteOutcome(false, 0, string.Empty, text, null, null, FirmwareVariableWriter.NtStatusNotObserved, WriteStatus.FirmwareError, elapsed);
    }

    /// <summary>What the verification read shows: the after-image, the before-image, or neither.</summary>
    private static VerificationResult VerificationOf(ChangePlan plan, ReadResult read)
    {
        FirmwareVariable? readBack = read.Status == ReadStatus.Success ? read.Variable : null;
        string? sha256 = readBack is null ? null : VariableImage.Sha256Of(readBack.Data);
        uint? attributes = readBack is null ? null : (uint)readBack.Attributes;
        return new VerificationResult(read.Status, ReadMatches(plan.After, read), ReadMatches(plan.Before, read), sha256, attributes);
    }

    private static (TransactionOutcome Outcome, int ExitCode, string Sentence) Classify(ChangePlan plan, WriteOutcome api, VerificationResult verification, WriteProgress progress)
    {
        string snapshot = SnapshotName(plan, progress);
        string left = progress.Suspended ? " " + CheckSentences.BitLockerLeftSuspended : string.Empty;
        if (api.Succeeded)
        {
            if (verification.MatchesAfter)
            {
                string suspended = progress.Suspended ? " " + TransactionSentences.SuspendedForOneRestart : string.Empty;
                return (TransactionOutcome.Verified, WriteExitCodes.Success, VerifiedSentence(plan, snapshot) + suspended);
            }

            return (TransactionOutcome.Unverified, WriteExitCodes.Unverified, UnverifiedSentence(api, verification, snapshot) + left);
        }

        if (progress.WriterEndedReason is string ended)
        {
            // The writer threw: no firmware answer exists, so no sentence may name one.
            if (verification.MatchesBefore)
            {
                return (TransactionOutcome.ApiRefused, WriteExitCodes.ApiRefused, CheckSentences.Format(TransactionSentences.WriterEndedUnchangedFormat, ended, snapshot) + left);
            }

            if (verification.MatchesAfter)
            {
                return (TransactionOutcome.ChangedDespiteError, WriteExitCodes.Unverified, CheckSentences.Format(TransactionSentences.WriterEndedChangedFormat, ended, snapshot) + left);
            }

            return (TransactionOutcome.Unverified, WriteExitCodes.Unverified, CheckSentences.Format(TransactionSentences.WriterEndedUnverifiedFormat, ended, UnverifiedReason(api, verification), snapshot) + left);
        }

        if (verification.MatchesBefore)
        {
            return (TransactionOutcome.ApiRefused, WriteExitCodes.ApiRefused, RefusalSentence(api) + SentenceSeparator + CheckSentences.Format(TransactionSentences.SnapshotNameFormat, snapshot) + left);
        }

        if (verification.MatchesAfter)
        {
            return (TransactionOutcome.ChangedDespiteError, WriteExitCodes.Unverified, WriteStatusMapping.ChangedDespiteErrorSentence(api.Win32Error, snapshot) + left);
        }

        return (TransactionOutcome.Unverified, WriteExitCodes.Unverified, UnverifiedSentence(api, verification, snapshot) + left);
    }

    // S16: the read-back bytes into after\ and snapshot.json completed; the one place they are written down.
    private string? RecordReadBack(ChangePlan plan, WriteProgress progress, VerificationResult verification, FirmwareVariable? readBack)
    {
        if (progress.SnapshotFolder is null || progress.Document is null)
        {
            return null;
        }

        try
        {
            SnapshotAfter after = new(
                readBack is not null,
                verification.Attributes,
                readBack?.Size,
                readBack is null ? null : VariableImage.HexOf(readBack.Data),
                verification.Sha256,
                null,
                verification.ReadStatus.ToString(),
                verification.MatchesAfter,
                verification.MatchesBefore);
            SnapshotDocument document = progress.Document with { After = after, BitLocker = SnapshotBitLockerBlock(plan, progress) };
            return _snapshots.WriteAfter(progress.SnapshotFolder, document, readBack);
        }
        catch (Exception ex)
        {
            return CheckSentences.Format(TransactionSentences.ExceptionFormat, ex.GetType().Name, ex.Message);
        }
    }

    /// <summary>A refusal from stage S11 to S15: nothing written; after a confirmed suspend the sentence says BitLocker stays suspended.</summary>
    private TransactionResult Stop(string check, string reason, WriteProgress progress)
    {
        Record(new CheckResult(check, false, reason));
        string sentence = progress.Suspended ? reason + " " + CheckSentences.BitLockerLeftSuspended : reason;
        return Finish(TransactionOutcome.Blocked, WriteExitCodes.Blocked, sentence, check, progress);
    }

    // S17: record.json rewritten, log.txt, one JSON line, one log event, then the result.
    private TransactionResult Finish(TransactionOutcome outcome, int exitCode, string sentence, string? blockedBy, WriteProgress progress)
    {
        Note(CheckSentences.Format(LogOutcomeFormat, outcome, exitCode, sentence));
        string finalSentence = sentence;
        WriteRecord record = MakeRecord(ToRecordOutcome(outcome), exitCode, sentence, blockedBy, progress, progress.Plan);
        string? problem = progress.AfterProblem;
        if (progress.SnapshotFolder is not null)
        {
            try
            {
                string? recordProblem = _snapshots.WriteRecordFile(progress.SnapshotFolder, record);
                string? logProblem = _snapshots.WriteLog(progress.SnapshotFolder, LogText());
                problem ??= recordProblem ?? logProblem;
            }
            catch (Exception ex)
            {
                problem ??= CheckSentences.Format(TransactionSentences.ExceptionFormat, ex.GetType().Name, ex.Message);
            }
        }

        if (problem is not null)
        {
            _logger.Error("write pipeline {TransactionId}: the snapshot folder could not be completed: {Problem}", record.TransactionId, problem);
            if (progress.ApiCalled)
            {
                finalSentence = sentence + SentenceSeparator + RecordNotSaved(outcome, progress.Api, problem);
                record = record with { Sentence = finalSentence };
            }
        }

        string? appendProblem = SafeAppend(record);
        progress.RecordAppended = appendProblem is null;
        if (appendProblem is not null)
        {
            _logger.Error("write pipeline {TransactionId}: the record line could not be appended: {Problem}", record.TransactionId, appendProblem);
            if (progress.ApiCalled && problem is null)
            {
                finalSentence = sentence + SentenceSeparator + RecordNotSaved(outcome, progress.Api, appendProblem);
                record = record with { Sentence = finalSentence };
            }
        }

        LogEvent(record);
        return new TransactionResult(outcome, progress.Plan, progress.SnapshotFolder, progress.Api, progress.Verification, record, finalSentence, exitCode)
        {
            BitLockerSuspended = progress.Suspended,
        };
    }

    // ---- the pieces ---------------------------------------------------------------------

    /// <summary>S3's direct reads: BootOrder, BootNext, BootCurrent, and every variable the request names.</summary>
    private static IReadOnlyList<(string Name, Guid Guid)> DirectReadNames(ChangeRequest request)
    {
        List<(string Name, Guid Guid)> names = new()
        {
            (Whitelist.BootOrder.Name, Whitelist.BootOrder.Guid),
            (Whitelist.BootNext.Name, Whitelist.BootNext.Guid),
            (ChangeValidator.BootCurrentName, Global),
        };
        IEnumerable<ushort> entries = request switch
        {
            ReorderRequest reorder => reorder.NewOrder ?? Array.Empty<ushort>(),
            BootNextRequest next => new[] { next.Target },
            _ => Array.Empty<ushort>(),
        };
        names.AddRange(entries.Select(index => (KnownVariableNames.BootName(index), Global)));
        if (request is CheckRequest check && !string.IsNullOrEmpty(check.Name))
        {
            names.Add((check.Name, check.Guid));
        }

        return names.Distinct().ToArray();
    }

    private static bool SuspendOffered(BitLockerAssessment? assessment, ChangeOperation? operation)
    {
        return assessment is not null && assessment.SuspendOffered && operation is ChangeOperation.Reorder or ChangeOperation.SetBootNext;
    }

    private static bool IsIntact(ChangePlan plan)
    {
        return ImageIntact(plan.Before)
            && ImageIntact(plan.After)
            && string.Equals(ChangePlan.ComputePlanHash(plan.Operation, plan.Variable.Key, plan.Before, plan.After), plan.PlanHash, StringComparison.Ordinal);
    }

    private static bool ImageIntact(VariableImage image)
    {
        return string.Equals(VariableImage.HexOf(image.Bytes), image.Hex, StringComparison.Ordinal)
            && string.Equals(VariableImage.Sha256Of(image.Bytes), image.Sha256, StringComparison.Ordinal);
    }

    /// <summary>
    /// Stage S12's comparison of the snapshot as read back with the plan: the read must have
    /// succeeded, the document's plan hash must be the plan's, and the target in <c>before\</c>
    /// must be the value the checks read (present with the same hash, or absent for an absent
    /// before-image). Internal for the tests: nothing runs between the store's write and its read
    /// inside the stage, so only a direct call reaches the last two comparisons.
    /// </summary>
    /// <param name="plan">The plan being committed.</param>
    /// <param name="read">What <see cref="SnapshotStore.Read"/> returned for the new folder.</param>
    /// <returns>Null when the snapshot is the plan's; otherwise the reason.</returns>
    internal static string? SnapshotMismatch(ChangePlan plan, SnapshotReadResult read)
    {
        if (!read.Succeeded || read.Document is null)
        {
            return read.Error ?? SnapshotStore.UnknownReason;
        }

        if (!string.Equals(read.Document.PlanHash, plan.PlanHash, StringComparison.Ordinal))
        {
            return TransactionSentences.SnapshotPlanHashReason;
        }

        bool sameValue = read.TargetBefore is FirmwareVariable targetBefore
            ? plan.Before.Present && string.Equals(VariableImage.Sha256Of(targetBefore.Data), plan.Before.Sha256, StringComparison.Ordinal)
            : !plan.Before.Present;
        return sameValue ? null : CheckSentences.Format(TransactionSentences.SnapshotTargetReasonFormat, plan.Variable.Name);
    }

    /// <summary>
    /// The last check before the call: the whitelist entry, the attributes, the size rule of
    /// each operation and the bytes, all from the plan the person confirmed. The plan is built
    /// from the whitelist entry's own name and GUID. Internal for the tests: every plan the
    /// pipeline confirms passed the validator's same rules, so only a direct call reaches a refusal.
    /// </summary>
    /// <param name="plan">The confirmed plan.</param>
    /// <param name="problem">Null when the write plan was built; otherwise the reason it was not.</param>
    /// <returns>The write plan, or null.</returns>
    internal static WritePlan? BuildWritePlan(ChangePlan plan, out string? problem)
    {
        problem = null;
        if (!Whitelist.TryDescribe(plan.Variable.Name, plan.Variable.Guid, out WhitelistEntry? entry)
            || entry.Kind != (plan.Operation == ChangeOperation.Reorder ? WritableKind.ReorderOnly : WritableKind.OneShot))
        {
            problem = TransactionSentences.NotWhitelistedReason;
            return null;
        }

        if (plan.After.Attributes != entry.RequiredAttributes
            || entry.RequiredAttributes != WritePlan.RequiredAttributes
            || (plan.Before.Present && plan.Before.Attributes != entry.RequiredAttributes))
        {
            problem = TransactionSentences.AttributesReason;
            return null;
        }

        problem = plan.Operation switch
        {
            ChangeOperation.Reorder when !(plan.Before.Present && plan.After.Present && plan.After.Size == plan.Before.Size && plan.After.Size > 0 && plan.After.Size % BootListCodec.EntrySize == 0) => TransactionSentences.ReorderSizeReason,
            ChangeOperation.SetBootNext when !(plan.After.Present && plan.After.Size == BootListCodec.EntrySize) => TransactionSentences.BootNextSizeReason,
            ChangeOperation.ClearBootNext when !(plan.Before.Present && !plan.After.Present && plan.After.Size == 0) => TransactionSentences.ClearReason,
            _ => null,
        };
        if (problem is not null)
        {
            return null;
        }

        WritePlan writePlan = new(entry.Name, entry.Guid, entry.RequiredAttributes, plan.After.Present ? plan.After.Bytes : Array.Empty<byte>());
        if (!string.Equals(VariableImage.HexOf(writePlan.Bytes), plan.After.Hex, StringComparison.Ordinal))
        {
            problem = TransactionSentences.BytesReason;
            return null;
        }

        return writePlan;
    }

    private string? DriftOf(string name, Guid guid, VariableImage expected)
    {
        ReadResult read = SafeRead(name, guid);
        if (ReadMatches(expected, read))
        {
            return null;
        }

        return read.Status is ReadStatus.Success or ReadStatus.NotFound
            ? CheckSentences.Format(CheckSentences.StaleReadFormat, name)
            : CheckSentences.Format(TransactionSentences.DriftReadFailedFormat, name, read.Status);
    }

    /// <summary>True when a read shows exactly the image: the same bytes and attributes, or NotFound for an absent image.</summary>
    private static bool ReadMatches(VariableImage image, ReadResult read)
    {
        if (read.Status == ReadStatus.NotFound)
        {
            return !image.Present;
        }

        if (read.Status != ReadStatus.Success || read.Variable is null || !image.Present)
        {
            return false;
        }

        return image.SameBytesAs(read.Variable.Data ?? Array.Empty<byte>()) && (uint)read.Variable.Attributes == image.Attributes;
    }

    private ReadResult SafeRead(string name, Guid guid)
    {
        try
        {
            ReadResult? read = _store.Read(name, guid);
            if (read is null)
            {
                return new ReadResult(null, ReadStatus.Failed, TransactionSentences.NoStoreResult, 0);
            }

            return read.Status == ReadStatus.Success && read.Variable is null
                ? new ReadResult(null, ReadStatus.Failed, TransactionSentences.NoStoreResult, read.LastBufferSize)
                : read;
        }
        catch (Exception ex)
        {
            return new ReadResult(null, ReadStatus.Failed, CheckSentences.Format(TransactionSentences.ExceptionFormat, ex.GetType().Name, ex.Message), 0);
        }
    }

    private static VariableImage ImageOf(FirmwareVariable? variable, uint absentAttributes)
    {
        return variable is null
            ? VariableImage.Absent(absentAttributes)
            : VariableImage.FromBytes(variable.Data ?? Array.Empty<byte>(), (uint)variable.Attributes);
    }

    /// <summary>
    /// The Unverified sentence: after a call that succeeded, the design's "written but could not be
    /// verified"; after a call that failed, the firmware's error first and no claim that anything
    /// was written, since the value read back is neither the old nor the new one, or none.
    /// </summary>
    private static string UnverifiedSentence(WriteOutcome api, VerificationResult verification, string snapshot)
    {
        string reason = UnverifiedReason(api, verification);
        return api.Succeeded
            ? CheckSentences.Format(TransactionSentences.UnverifiedFormat, reason, snapshot)
            : CheckSentences.Format(TransactionSentences.FailedUnverifiedFormat, api.Win32Error, reason, snapshot);
    }

    /// <summary>Why a change is not verified: the read-back failed, or it shows another value than the call's result implies.</summary>
    private static string UnverifiedReason(WriteOutcome api, VerificationResult verification)
    {
        return verification.ReadStatus is not (ReadStatus.Success or ReadStatus.NotFound)
            ? CheckSentences.Format(TransactionSentences.ReadBackFailedFormat, verification.ReadStatus)
            : api.Succeeded ? TransactionSentences.ReadBackDiffers : TransactionSentences.NeitherValue;
    }

    private static string RefusalSentence(WriteOutcome api)
    {
        WriteStatus status = api.Status == WriteStatus.Success ? WriteStatus.FirmwareError : api.Status;
        return WriteStatusMapping.Sentence(status, api.Win32Error, api.Win32Text ?? string.Empty);
    }

    private static string VerifiedSentence(ChangePlan plan, string snapshot)
    {
        bool restore = plan.RestoredFrom is not null;
        switch (plan.Operation)
        {
            case ChangeOperation.Reorder:
                string order = string.Join(TransactionSentences.EntrySeparator, plan.AfterEntries.Select(DescribeEntry));
                return CheckSentences.Format(restore ? TransactionSentences.RestoredReorderFormat : TransactionSentences.VerifiedReorderFormat, order, snapshot);
            case ChangeOperation.SetBootNext:
                string target = plan.AfterEntries.Count > 0 ? DescribeEntry(plan.AfterEntries[0]) : plan.After.Hex;
                return CheckSentences.Format(restore ? TransactionSentences.RestoredBootNextFormat : TransactionSentences.VerifiedBootNextFormat, target, snapshot);
            default:
                return CheckSentences.Format(TransactionSentences.VerifiedClearFormat, snapshot);
        }
    }

    /// <summary>A BitLocker state as a sentence names it: "on", "suspended", "off", "encrypting", "unknown".</summary>
    private static string StateWord(BitLockerState state) => state.ToString().ToLowerInvariant();

    private static string DescribeEntry(EntryLine entry) => CheckSentences.Format(TransactionSentences.EntryFormat, entry.Name, entry.Description);

    private static string SnapshotName(ChangePlan plan, WriteProgress progress)
    {
        return progress.SnapshotFolder is null ? plan.SnapshotFolderName : Path.GetFileName(progress.SnapshotFolder);
    }

    private static string RecordNotSaved(TransactionOutcome outcome, WriteOutcome? api, string problem)
    {
        string format = outcome switch
        {
            TransactionOutcome.Verified => CheckSentences.RecordNotSavedVerifiedFormat,
            TransactionOutcome.ApiRefused => TransactionSentences.RecordNotSavedRefusedFormat,
            TransactionOutcome.Unverified when api is { Succeeded: false } => CheckSentences.RecordNotSavedUnknownFormat,
            _ => CheckSentences.RecordNotSavedUnverifiedFormat,
        };
        return CheckSentences.Format(format, problem);
    }

    private static WriteRecordOutcome ToRecordOutcome(TransactionOutcome outcome)
    {
        return outcome switch
        {
            TransactionOutcome.Blocked => WriteRecordOutcome.Blocked,
            TransactionOutcome.Cancelled => WriteRecordOutcome.Cancelled,
            TransactionOutcome.Verified => WriteRecordOutcome.Verified,
            TransactionOutcome.ApiRefused => WriteRecordOutcome.ApiRefused,
            TransactionOutcome.ChangedDespiteError => WriteRecordOutcome.ChangedDespiteError,
            _ => WriteRecordOutcome.Unverified,
        };
    }

    private string? SafeAppend(WriteRecord record)
    {
        try
        {
            return _records.Append(record);
        }
        catch (Exception ex)
        {
            return CheckSentences.Format(TransactionSentences.ExceptionFormat, ex.GetType().Name, ex.Message);
        }
    }

    // ---- the snapshot request -----------------------------------------------------------

    /// <summary>
    /// The Pending record of stage S12: the intent line of write-log.jsonl and the first
    /// record.json of the snapshot folder, naming the folder the transaction creates.
    /// </summary>
    private WriteRecord PendingRecord(ChangePlan plan, WriteProgress progress)
    {
        return MakeRecord(WriteRecordOutcome.Pending, WriteExitCodes.Success, TransactionSentences.Pending, null, progress, plan)
            with { SnapshotFolder = Path.Combine(_snapshots.RootDirectory, plan.SnapshotFolderName) };
    }

    private SnapshotRequest BuildSnapshotRequest(ChangePlan plan, WriteProgress progress)
    {
        VariableSet set = _variables ?? VariableSet.Empty;
        VariableImage before = plan.Before;
        WriteRecord pending = PendingRecord(plan, progress);
        return new SnapshotRequest(
            plan.SnapshotFolderName,
            plan.Request.Host,
            plan.Operation.ToString(),
            plan.RestoredFrom is not null,
            plan.RestoredFrom,
            plan.CreatedAt,
            ToolVersion.Current,
            _identity ?? plan.Policy.Identity,
            _firmwareType,
            _privilege,
            _enumeration?.Method ?? EnumerationMethod.None,
            set.All,
            new SnapshotTarget(plan.Variable.Key, plan.Variable.Name, plan.Variable.Guid.ToString("D")),
            new SnapshotBefore(
                before.Present,
                before.Present ? before.Attributes : 0u,
                before.Present ? VariableAttributeFlags.ToShortFlags((VariableAttributes)before.Attributes) : string.Empty,
                before.Size,
                before.Hex,
                before.Sha256,
                null,
                plan.BeforeEntries),
            new SnapshotProposed(plan.After.Attributes, plan.After.Size, plan.After.Hex, plan.After.Sha256, plan.AfterEntries),
            BuildContext(set),
            new SnapshotBaseline(plan.Policy.BaselineFolder, _baselineTargetSha256, plan.Policy.BaselineMatches),
            SnapshotBitLockerBlock(plan, progress),
            new SnapshotSecureBoot(PlatformPolicy.SecureBootEnabled(plan.Policy.SecureBoot), plan.Policy.SecureBoot?.VariableSetupMode, plan.Policy.SecureBoot?.SourcesAgree ?? false),
            plan.PlanHash,
            Decode(set),
            PlatformBlock(plan),
            LogText(),
            pending);
    }

    private static SnapshotContext BuildContext(VariableSet set)
    {
        FirmwareVariable? timeout = set.Find(BootOrderDecoder.TimeoutName, Global);
        int? timeoutSeconds = timeout is { Size: 2 } ? BootOrderDecoder.ReadTimeoutSeconds(timeout.Data) : null;
        SnapshotLoadOption[] options = set.All
            .Where(variable => variable.VendorGuid == Global && KnownVariableNames.IsBootEntryName(variable.Name, out _))
            .OrderBy(variable => variable.Key, StringComparer.Ordinal)
            .Select(DescribeLoadOption)
            .ToArray();
        return new SnapshotContext(
            SingleEntryName(set.Find(ChangeValidator.BootCurrentName, Global)),
            SingleEntryName(set.Find(Whitelist.BootNext.Name, Whitelist.BootNext.Guid)),
            timeoutSeconds,
            options);
    }

    private static string? SingleEntryName(FirmwareVariable? variable)
    {
        if (variable is null || variable.Size != BootListCodec.EntrySize)
        {
            return null;
        }

        return BootListCodec.Decode(variable.Data).Names[0];
    }

    private static SnapshotLoadOption DescribeLoadOption(FirmwareVariable variable)
    {
        byte[] data = variable.Data ?? Array.Empty<byte>();
        string sha256 = VariableImage.Sha256Of(data);
        try
        {
            LoadOption option = LoadOptionParser.Parse(data);
            return new SnapshotLoadOption(variable.Key, data.Length, sha256, option.Description, option.Active, option.Hidden, option.Category, option.DevicePath);
        }
        catch (Exception)
        {
            return new SnapshotLoadOption(variable.Key, data.Length, sha256, CheckSentences.MalformedEntryDescription, false, false, string.Empty, string.Empty);
        }
    }

    private object? Decode(VariableSet set)
    {
        try
        {
            return DecoderRegistry.CreateDefault().DecodeAll(set);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "write pipeline {TransactionId}: the variables could not be decoded for the snapshot", _transactionId);
            return null;
        }
    }

    private PlatformSnapshot PlatformBlock(ChangePlan plan)
    {
        string[] notRead = { TransactionSentences.NotReadByThePipeline };
        return new PlatformSnapshot(
            _identity ?? plan.Policy.Identity,
            _tpm ?? new TpmInfoResult(false, null, null, null, notRead),
            _secureBoot ?? new SecureBootPolicyResult(null, null, null, null, null, null, false, false, notRead),
            _bitLockerRead ?? new BitLockerResult(Array.Empty<BitLockerVolume>(), notRead),
            new DellInterfaceProbeResult(false, Array.Empty<string>(), Array.Empty<DellAttributeSample>(), false, null, null, null, false, false, notRead));
    }

    private static SnapshotBitLocker SnapshotBitLockerBlock(ChangePlan plan, WriteProgress progress)
    {
        BitLockerAssessment? assessment = plan.Policy.BitLocker;
        SuspendResult? suspend = progress.Suspend;
        BitLockerState state = suspend is { Succeeded: false } ? suspend.StateAfter : progress.BitLockerNow?.State ?? assessment?.State ?? BitLockerState.Unknown;
        return new SnapshotBitLocker(
            state,
            assessment?.ProtectionStatus,
            assessment?.ConversionStatus,
            assessment?.ProtectorTypes ?? Array.Empty<string>(),
            assessment?.PcrProfileText,
            SuspendOffered(assessment, plan.Operation),
            progress.Choice == BitLockerChoice.SuspendForOneRestart,
            suspend?.DisableCount,
            suspend?.ResultText,
            suspend?.ProtectionStatusAfter);
    }

    // ---- the record ---------------------------------------------------------------------

    private WriteRecord MakeRecord(WriteRecordOutcome outcome, int exitCode, string sentence, string? blockedBy, WriteProgress? progress, ChangePlan? plan)
    {
        ChangeRequest? request = _request ?? plan?.Request;
        ChangeOperation? operation = plan?.Operation ?? _validation?.Operation;
        WhitelistEntry? entry = plan?.Variable ?? _validation?.Variable ?? EntryOf(request);
        VariableImage? before = plan?.Before ?? _validation?.Before;
        VariableImage? after = plan?.After ?? _validation?.After;
        PolicyFindings? findings = plan?.Policy;
        MachineIdentityResult? identity = _identity ?? findings?.Identity;
        bool modelConfirmed = _identity is not null ? _modelConfirmed : findings?.ModelConfirmed ?? false;
        bool unitConfirmed = (_unit ?? findings?.Unit) == UnitMatch.Confirmed;
        BitLockerAssessment? bitLocker = _bitLocker ?? findings?.BitLocker;
        SecureBootPolicyResult? secureBoot = _secureBoot ?? findings?.SecureBoot;
        bool secureBootKnown = _secureBootRead || findings is not null;
        string? restoredFrom = request?.RestoredFrom ?? (_originalRequest as RestoreRequest)?.SnapshotFolder;
        BitLockerChoice? choice = progress?.Choice ?? _preparedChoice;
        WriteOutcome? api = progress?.Api;
        VerificationResult? verification = progress?.Verification;

        return new WriteRecord
        {
            TimestampUtc = WriteRecord.FormatTimestamp(_clock.Now()),
            TransactionId = string.IsNullOrEmpty(_transactionId) ? plan?.SnapshotFolderName ?? string.Empty : _transactionId,
            ToolVersion = ToolVersion.Current,
            Host = request?.Host ?? string.Empty,
            Operation = OperationName(operation, request),
            IsRestore = restoredFrom is not null,
            RestoredFrom = restoredFrom,
            Variable = VariableOf(entry, request),
            AttributesBefore = before is { Present: true } ? before.Attributes : null,
            AttributesRequested = operation is not null && entry is not null ? entry.RequiredAttributes : null,
            AttributeNames = before is { Present: true } ? VariableAttributeFlags.ToShortFlags((VariableAttributes)before.Attributes) : null,
            SizeBefore = before?.Size,
            SizeRequested = operation is not null ? after?.Size : null,
            BeforeSha256 = before is { Present: true } ? before.Sha256 : null,
            ProposedSha256 = after is { Present: true } ? after.Sha256 : null,
            AfterSha256 = verification?.Sha256,
            Before = entry is null ? null : ImageRecord(before),
            After = operation is null ? null : ImageRecord(after),
            Checks = _checks.ToArray(),
            BlockedBy = blockedBy,
            Confirmed = progress?.Confirmed ?? false,
            BitLockerChoice = choice,
            BitLocker = BitLockerRecord(bitLocker, progress, operation, choice),
            WritesThisStart = _writesThisStart,
            BootId = BootId(),
            DeviceNodeTypes = plan?.DeviceNodeTypes ?? _validation?.DeviceNodeTypes ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
            SecureBoot = secureBootKnown ? new WriteRecordSecureBoot(PlatformPolicy.SecureBootEnabled(secureBoot), secureBoot?.VariableSetupMode, secureBoot?.SourcesAgree ?? false) : null,
            Machine = identity is null ? null : new WriteRecordMachine(identity.Model, identity.BiosVersion, modelConfirmed, unitConfirmed),
            ApiCalled = progress?.ApiCalled ?? false,
            Api = ApiRecord(api),
            Verification = VerificationRecord(verification),
            SnapshotFolder = progress?.SnapshotFolder,
            Outcome = outcome,
            ExitCode = exitCode,
            Sentence = sentence,
        };
    }

    /// <summary>The record's BitLocker block: the assessment, the state read again at S12 or left by a failed suspend, and what the suspend returned.</summary>
    private static WriteRecordBitLocker? BitLockerRecord(BitLockerAssessment? bitLocker, WriteProgress? progress, ChangeOperation? operation, BitLockerChoice? choice)
    {
        if (bitLocker is null)
        {
            return null;
        }

        SuspendResult? suspend = progress?.Suspend;
        return new WriteRecordBitLocker(
            suspend is { Succeeded: false } ? suspend.StateAfter : progress?.BitLockerNow?.State ?? bitLocker.State,
            bitLocker.ProtectionStatus,
            bitLocker.ConversionStatus,
            bitLocker.ProtectorTypes,
            bitLocker.PcrProfileText,
            SuspendOffered(bitLocker, operation),
            choice == BitLockerChoice.SuspendForOneRestart,
            suspend?.ResultText,
            suspend?.ProtectionStatusAfter);
    }

    /// <summary>What the one call returned, as the record keeps it; null when it was not made.</summary>
    private static WriteRecordApi? ApiRecord(WriteOutcome? api)
    {
        return api is null
            ? null
            : new WriteRecordApi(api.Succeeded, api.Win32Error, api.Win32Name ?? string.Empty, api.Win32Text ?? string.Empty, api.NtStatus, api.NtStatusText, api.NtStatusSource ?? FirmwareVariableWriter.NtStatusNotObserved, (long)api.Elapsed.TotalMilliseconds);
    }

    /// <summary>What the one verification read returned, as the record keeps it; null when it was not made.</summary>
    private static WriteRecordVerification? VerificationRecord(VerificationResult? verification)
    {
        return verification is null
            ? null
            : new WriteRecordVerification(verification.ReadStatus, verification.MatchesAfter, verification.MatchesBefore, verification.Sha256, verification.Attributes);
    }

    private static WriteRecordImage? ImageRecord(VariableImage? image)
    {
        if (image is null)
        {
            return null;
        }

        IReadOnlyList<string> entries = image.Present ? BootListCodec.Decode(image.Bytes).Names : Array.Empty<string>();
        return new WriteRecordImage(image.Present, image.Hex, entries);
    }

    private static WriteRecordVariable? VariableOf(WhitelistEntry? entry, ChangeRequest? request)
    {
        if (entry is not null)
        {
            return new WriteRecordVariable(entry.Key, entry.Name, entry.Guid.ToString("D"));
        }

        return request is CheckRequest check
            ? new WriteRecordVariable(FirmwareVariable.MakeKey(check.Guid, check.Name), check.Name, check.Guid.ToString("D"))
            : null;
    }

    private static WhitelistEntry? EntryOf(ChangeRequest? request)
    {
        return request switch
        {
            ReorderRequest => Whitelist.BootOrder,
            BootNextRequest or ClearBootNextRequest => Whitelist.BootNext,
            CheckRequest check when Whitelist.TryDescribe(check.Name, check.Guid, out WhitelistEntry? entry) => entry,
            _ => null,
        };
    }

    private static (string Name, Guid Guid) TargetOf(ChangeRequest request, ValidationResult? validation)
    {
        if (validation?.Variable is WhitelistEntry entry)
        {
            return (entry.Name, entry.Guid);
        }

        if (request is CheckRequest check)
        {
            return (check.Name, check.Guid);
        }

        WhitelistEntry? typed = EntryOf(request);
        return typed is null ? (string.Empty, Guid.Empty) : (typed.Name, typed.Guid);
    }

    private static string OperationName(ChangeOperation? operation, ChangeRequest? request)
    {
        if (operation is ChangeOperation known)
        {
            return known.ToString();
        }

        return request switch
        {
            ReorderRequest => ChangeOperation.Reorder.ToString(),
            BootNextRequest => ChangeOperation.SetBootNext.ToString(),
            ClearBootNextRequest => ChangeOperation.ClearBootNext.ToString(),
            CheckRequest => CheckOperation,
            RestoreRequest => RestoreOperation,
            _ => string.Empty,
        };
    }

    /// <summary>The transaction id before a plan exists, built as <see cref="ChangePlan.SnapshotFolderName"/> is, so the two agree.</summary>
    private string TransactionIdFor(ChangeRequest request)
    {
        string word = request switch
        {
            ReorderRequest => ChangePlan.OperationFolderWord(ChangeOperation.Reorder),
            BootNextRequest => ChangePlan.OperationFolderWord(ChangeOperation.SetBootNext),
            ClearBootNextRequest => ChangePlan.OperationFolderWord(ChangeOperation.ClearBootNext),
            RestoreRequest => RestoreWord,
            _ => CheckWord,
        };
        return ChangePlan.FolderName(_startedAt, word, request.RestoredFrom is not null && request is not RestoreRequest);
    }

    // ---- the log ------------------------------------------------------------------------

    private void Pass(string check) => Record(new CheckResult(check, true, string.Empty));

    private void Warn(string check, string reason) => Record(new CheckResult(check, true, reason, IsWarning: true));

    private void Record(CheckResult check)
    {
        _checks.Add(check);
        string state = !check.Passed ? LogFailed : check.IsWarning ? LogWarned : LogPassed;
        string reason = string.IsNullOrEmpty(check.Reason) ? string.Empty : CheckSentences.Format(LogReasonFormat, check.Reason);
        Note(CheckSentences.Format(LogCheckFormat, check.Name, state, reason));
    }

    private void Note(string text)
    {
        lock (_gate)
        {
            _log.Add(CheckSentences.Format(LogLineFormat, _clock.Now(), text));
        }

        _logger.Debug("write pipeline {TransactionId}: {Note}", _transactionId, text);
    }

    private string LogText()
    {
        lock (_gate)
        {
            return string.Join(Environment.NewLine, _log) + Environment.NewLine;
        }
    }

    private void LogEvent(WriteRecord record)
    {
        LogEventLevel level = record.Outcome switch
        {
            WriteRecordOutcome.Blocked or WriteRecordOutcome.Cancelled => LogEventLevel.Warning,
            WriteRecordOutcome.ApiRefused or WriteRecordOutcome.Unverified or WriteRecordOutcome.ChangedDespiteError => LogEventLevel.Error,
            _ => LogEventLevel.Information,
        };
        _logger.Write(
            level,
            "write record {TransactionId} {Operation} {Key}: outcome {Outcome}, exit {ExitCode}, blocked by {BlockedBy}, API called {ApiCalled}, Win32 {Win32Error}, NTSTATUS {NtStatus}, before {BeforeSha256}, after {AfterSha256}, snapshot {SnapshotFolder}: {Sentence}",
            record.TransactionId,
            record.Operation,
            record.Variable?.Key,
            record.Outcome,
            record.ExitCode,
            record.BlockedBy,
            record.ApiCalled,
            record.Api?.Win32Error,
            record.Api?.NtStatus,
            record.BeforeSha256,
            record.AfterSha256,
            record.SnapshotFolder,
            record.Sentence);
    }

    /// <summary>What stages S11 to S17 learnt, for the record and the result.</summary>
    private sealed class WriteProgress
    {
        public WriteProgress(ChangePlan plan, BitLockerChoice? choice)
        {
            Plan = plan;
            Choice = choice;
        }

        public ChangePlan Plan { get; }

        public BitLockerChoice? Choice { get; }

        public string Stage { get; set; } = CheckNames.Confirmation;

        public bool Confirmed { get; set; }

        public string? SnapshotFolder { get; set; }

        public SnapshotDocument? Document { get; set; }

        public SuspendResult? Suspend { get; set; }

        public bool Suspended { get; set; }

        public bool ApiCalled { get; set; }

        public WriteOutcome? Api { get; set; }

        public VerificationResult? Verification { get; set; }

        public string? AfterProblem { get; set; }

        /// <summary>The exception the writer ended in, as "type: message"; null when the call returned.</summary>
        public string? WriterEndedReason { get; set; }

        /// <summary>True once S17 appended the transaction's line to write-log.jsonl, so the last resort does not append a second one.</summary>
        public bool RecordAppended { get; set; }

        /// <summary>The write record log's commit lock, taken at S12 and released when the commit ends.</summary>
        public IDisposable? CommitLock { get; set; }

        /// <summary>BitLocker's state read again at S12, before any suspend; null when it was not read.</summary>
        public BitLockerAssessment? BitLockerNow { get; set; }
    }
}
