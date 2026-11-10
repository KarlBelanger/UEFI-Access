// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Serilog;
using UefiAccess.Core.Baseline;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Privileges;
using UefiAccess.Core.Variables;

namespace UefiAccess.Core.Safety;

/// <summary>
/// The pipeline the CLI and the GUI call. Every prepare runs in a <see cref="WriteTransaction"/>
/// of its own, which keeps the fresh enumeration and the findings the commit needs; the pipeline
/// remembers the transaction by its plan (reference identity) until the plan is committed or
/// cancelled, so a plan can be committed at most once and a plan from another pipeline, or one
/// already committed, is refused at the confirmation with nothing written. At most
/// <see cref="MaximumPendingPlans"/> plans wait for a confirmation; an older one is forgotten
/// and must be checked again. The real writer and suspender are constructed only by the
/// production composition roots, which pass them in here; tests pass fakes. A pipeline over the
/// real writer refuses any store but <see cref="WindowsFirmwareVariableStore"/>, so a baseline
/// or in-memory store can never stand in for the firmware the writer writes; rule V15 refuses
/// their enumerations as well. Two commits never overlap, in this process or across the
/// application and the command-line tool: stage S12 takes the write record log's commit lock,
/// counts the write budget again under it, and holds it until S17.
/// </summary>
public sealed class WritePipeline : IWritePipeline
{
    /// <summary>How many prepared plans wait for their confirmation at most.</summary>
    public const int MaximumPendingPlans = 16;

    /// <summary>The message of the exception a pipeline over the real writer and any store but the firmware's gets.</summary>
    internal const string RealWriterNeedsTheFirmwareStoreMessage = "The real firmware writer is composed only with the Windows firmware variable store, so every read of a transaction is a read of the firmware it writes.";

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
    private readonly Dictionary<ChangePlan, WriteTransaction> _prepared = new(ReferenceEqualityComparer.Instance);
    private readonly List<ChangePlan> _order = new();

    /// <summary>Creates the pipeline over its services.</summary>
    /// <param name="firmwareTypeDetector">Firmware type detection (stage S1).</param>
    /// <param name="privilegeManager">Elevation and the privilege (stage S1).</param>
    /// <param name="store">The read-only variable store (stages S3, S12, S14 and S16).</param>
    /// <param name="platformPolicy">The identity, Secure Boot, BitLocker and TPM reads (stages S2, S7 and S8).</param>
    /// <param name="writer">The writer that makes the one native call (stage S15).</param>
    /// <param name="suspender">The one-restart BitLocker suspend (stage S13).</param>
    /// <param name="snapshots">The snapshot store (stages S0, S9, S12, S16 and S17).</param>
    /// <param name="records">The write record log (stages S9 and S17).</param>
    /// <param name="baselineRoot">The folder whose newest baseline the target is compared with at S7; null selects <see cref="BaselineExporter.DefaultRootDirectory"/>.</param>
    /// <param name="logger">Optional Serilog logger; null disables logging.</param>
    public WritePipeline(
        IFirmwareTypeDetector firmwareTypeDetector,
        IPrivilegeManager privilegeManager,
        IFirmwareVariableStore store,
        PlatformPolicy platformPolicy,
        IFirmwareVariableWriter writer,
        IBitLockerSuspender suspender,
        SnapshotStore snapshots,
        WriteRecordLog records,
        string? baselineRoot,
        ILogger? logger)
        : this(firmwareTypeDetector, privilegeManager, store, platformPolicy, writer, suspender, snapshots, records, baselineRoot, logger, TransactionClock.System)
    {
    }

    /// <summary>Creates the pipeline with an explicit clock, for tests.</summary>
    /// <param name="firmwareTypeDetector">Firmware type detection.</param>
    /// <param name="privilegeManager">Elevation and the privilege.</param>
    /// <param name="store">The read-only variable store.</param>
    /// <param name="platformPolicy">The platform reads.</param>
    /// <param name="writer">The writer.</param>
    /// <param name="suspender">The suspender.</param>
    /// <param name="snapshots">The snapshot store.</param>
    /// <param name="records">The write record log.</param>
    /// <param name="baselineRoot">The baseline root, or null for the default.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="clock">Now, and the moment Windows last started.</param>
    internal WritePipeline(
        IFirmwareTypeDetector firmwareTypeDetector,
        IPrivilegeManager privilegeManager,
        IFirmwareVariableStore store,
        PlatformPolicy platformPolicy,
        IFirmwareVariableWriter writer,
        IBitLockerSuspender suspender,
        SnapshotStore snapshots,
        WriteRecordLog records,
        string? baselineRoot,
        ILogger? logger,
        TransactionClock clock)
    {
        ArgumentNullException.ThrowIfNull(firmwareTypeDetector);
        ArgumentNullException.ThrowIfNull(privilegeManager);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(platformPolicy);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(suspender);
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(clock);
        if (writer is FirmwareVariableWriter && store is not WindowsFirmwareVariableStore)
        {
            // The drift and verification reads must read the firmware the real writer writes, never
            // a baseline folder or an in-memory store (design section 2.4, item 6).
            throw new ArgumentException(RealWriterNeedsTheFirmwareStoreMessage, nameof(store));
        }

        _firmwareTypeDetector = firmwareTypeDetector;
        _privilegeManager = privilegeManager;
        _store = store;
        _platformPolicy = platformPolicy;
        _writer = writer;
        _suspender = suspender;
        _snapshots = snapshots;
        _records = records;
        _baselineRoot = string.IsNullOrWhiteSpace(baselineRoot) ? BaselineExporter.DefaultRootDirectory : baselineRoot;
        _logger = logger ?? Serilog.Core.Logger.None;
        _clock = clock;
    }

    /// <summary>The snapshot store, for the callers that list snapshots or find the newest one of a variable.</summary>
    public SnapshotStore Snapshots => _snapshots;

    /// <summary>The write record log, for the callers that print it.</summary>
    public WriteRecordLog Records => _records;

    /// <summary>The number of plans prepared and not yet committed, cancelled or forgotten.</summary>
    public int PendingPlanCount
    {
        get
        {
            lock (_gate)
            {
                return _prepared.Count;
            }
        }
    }

    /// <inheritdoc />
    public Task<PrepareResult> PrepareAsync(ChangeRequest request, CancellationToken token)
    {
        return PrepareAsync(request, null, token);
    }

    /// <inheritdoc />
    public async Task<PrepareResult> PrepareAsync(ChangeRequest request, BitLockerChoice? bitLockerChoice, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        WriteTransaction transaction = CreateTransaction();
        PrepareResult result = await transaction.PrepareAsync(request, bitLockerChoice, token).ConfigureAwait(false);
        if (result.Plan is not null)
        {
            Remember(result.Plan, transaction);
        }

        return result;
    }

    /// <inheritdoc />
    public Task<TransactionResult> CommitAsync(ChangePlan plan, ConfirmationToken token)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(token);
        WriteTransaction transaction = Take(plan) ?? CreateTransaction();
        return transaction.CommitAsync(plan, token);
    }

    /// <inheritdoc />
    public Task<TransactionResult> CancelAsync(ChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        WriteTransaction transaction = Take(plan) ?? CreateTransaction();
        return transaction.CancelAsync(plan);
    }

    private void Remember(ChangePlan plan, WriteTransaction transaction)
    {
        lock (_gate)
        {
            _prepared[plan] = transaction;
            _order.Add(plan);
            while (_order.Count > MaximumPendingPlans)
            {
                _prepared.Remove(_order[0]);
                _order.RemoveAt(0);
            }
        }
    }

    private WriteTransaction? Take(ChangePlan plan)
    {
        lock (_gate)
        {
            _order.Remove(plan);
            return _prepared.Remove(plan, out WriteTransaction? transaction) ? transaction : null;
        }
    }

    private WriteTransaction CreateTransaction()
    {
        return new WriteTransaction(
            _firmwareTypeDetector,
            _privilegeManager,
            _store,
            _platformPolicy,
            _writer,
            _suspender,
            _snapshots,
            _records,
            _baselineRoot,
            _logger,
            _clock);
    }
}
