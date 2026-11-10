// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Globalization;
using Serilog;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Privileges;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Variables;
using UefiAccess.Presentation.Resources;
using UefiAccess.Presentation.ViewModels;

namespace UefiAccess.Presentation.Services;

/// <summary>
/// The live machine as a variable source: applies the CLI's precondition rules in the CLI's order
/// (firmware type must be UEFI, the process elevated, SeSystemEnvironmentPrivilege enabled), then
/// reads the machine identity and enumerates the Core variable store. A precondition failure
/// yields <see cref="SourceStatus.Refused"/> with the matching sentence from <see cref="Strings"/>;
/// an enumeration that produced nothing, or any exception, yields <see cref="SourceStatus.Failed"/>
/// with the text. The blocking work (native calls, WMI) runs on thread-pool threads so the UI
/// thread stays responsive; nothing here is polled. Every call is read-only.
/// </summary>
/// <remarks>
/// <para>
/// The machine identity (five WMI queries) and the enumeration (native calls) do not depend on
/// each other, so the identity is read on its own thread while the variables are enumerated, and
/// the load waits for it at most <see cref="IdentityTimeout"/> from the moment it started: the
/// bound Core puts on one WMI enumeration step. A slow or hung WMI provider therefore costs the
/// identity strip its values (the shell shows "(unknown)", and a warning says why) and never the
/// variable list, and the load ends. This is a bound on a wait inside the load the user started,
/// not a timer: nothing is scheduled, announced or focused by it.
/// </para>
/// <para>
/// Logging has one channel per event. Whatever this source finds reaches the log panel through
/// its result (the refusal sentence, the failure text, a warning, the identity), which the shell
/// writes into the panel once; so this class logs only at Debug (with the exception attached,
/// for the log file), and <see cref="CreateWindows"/> composes the Core services over a logger
/// marked file-only (<see cref="LogPanelMirror.FileOnly"/>): their record of every native call
/// and WMI query stays in the log file and does not repeat the result in the panel.
/// </para>
/// </remarks>
public sealed class LiveFirmwareSource : IVariableSource
{
    /// <summary>Failure text when the load was cancelled before it finished.</summary>
    internal const string CancelledMessage = "The load was cancelled before it finished.";

    /// <summary>Prefix of the failure text when the store enumerated nothing and reported an error.</summary>
    internal const string EnumerationFailedPrefix = "No variables could be enumerated: ";

    /// <summary>Prefix of the warning written when the store returned variables but also an error.</summary>
    internal const string EnumerationIncompletePrefix = "enumeration incomplete: ";

    /// <summary>Prefix of the warning written for each machine identity error.</summary>
    internal const string IdentityWarningPrefix = "machine identity: ";

    /// <summary>Prefix of the warning that carries the privilege manager's detail after a privilege refusal.</summary>
    internal const string PrivilegeDetailPrefix = "SeSystemEnvironmentPrivilege: ";

    /// <summary>Prefix of the warning written when the firmware type detection threw (the load is then refused as not UEFI).</summary>
    internal const string FirmwareTypeWarningPrefix = "firmware type detection: ";

    /// <summary>Prefix of the warning written when the elevation check threw (the load is then refused as not elevated).</summary>
    internal const string ElevationWarningPrefix = "elevation check: ";

    private readonly IFirmwareTypeDetector _detector;
    private readonly IPrivilegeManager _privileges;
    private readonly IFirmwareVariableStore _store;
    private readonly IPlatformInspector<MachineIdentityResult> _identity;
    private readonly ILogger _logger;
    private readonly TimeSpan _identityTimeout;

    /// <summary>Creates the source over explicit services (the real ones or fakes).</summary>
    /// <param name="detector">Firmware type detector.</param>
    /// <param name="privileges">Elevation and privilege manager.</param>
    /// <param name="store">Firmware variable store.</param>
    /// <param name="identity">Machine identity inspector.</param>
    /// <param name="logger">Optional logger; null disables logging.</param>
    /// <param name="identityTimeout">How long a load waits for the machine identity; null means <see cref="IdentityTimeout"/>. Negative values count as zero.</param>
    /// <param name="writeService">The write pipeline of this source, for the Change group; null (the default) means no change can be made through it.</param>
    /// <param name="unit">
    /// The unit check each load runs (<see cref="SourceResult.UnitConfirmed"/>); null (the default)
    /// reads no machine ID and never confirms the unit, so every row is shown as read-only here.
    /// </param>
    /// <exception cref="ArgumentNullException">A required service is null.</exception>
    public LiveFirmwareSource(
        IFirmwareTypeDetector detector,
        IPrivilegeManager privileges,
        IFirmwareVariableStore store,
        IPlatformInspector<MachineIdentityResult> identity,
        ILogger? logger = null,
        TimeSpan? identityTimeout = null,
        IWriteService? writeService = null,
        EnabledUnit? unit = null)
    {
        ArgumentNullException.ThrowIfNull(detector);
        ArgumentNullException.ThrowIfNull(privileges);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(identity);
        _detector = detector;
        _privileges = privileges;
        _store = store;
        _identity = identity;
        _logger = logger ?? Serilog.Core.Logger.None;
        TimeSpan timeout = identityTimeout ?? IdentityTimeout;
        _identityTimeout = timeout < TimeSpan.Zero ? TimeSpan.Zero : timeout;
        Writes = writeService;
        Unit = unit;
    }

    /// <summary>
    /// How long a live load waits for the machine identity, counted from the moment the identity
    /// read starts (it runs while the variables are enumerated): 60 seconds, the bound Core's WMI
    /// helper puts on one enumeration step.
    /// </summary>
    public static TimeSpan IdentityTimeout { get; } = TimeSpan.FromSeconds(60);

    /// <inheritdoc />
    public string Description => Strings.LiveSourceName;

    /// <inheritdoc />
    public bool IsOffline => false;

    /// <summary>
    /// The write pipeline of the live source, through which the Change group changes BootOrder and
    /// BootNext; null when the source was composed without one. A
    /// baseline folder has none: nothing can be changed offline.
    /// </summary>
    public IWriteService? Writes { get; }

    /// <summary>The unit check each load runs, or null when the source was composed without one (the unit is then never confirmed).</summary>
    internal EnabledUnit? Unit { get; }

    /// <summary>
    /// Composes the production services: <see cref="PrivilegeManager"/>, <see cref="FirmwareTypeDetector"/>,
    /// <see cref="WindowsFirmwareVariableStore"/> and <see cref="MachineIdentity"/>, like the CLI does,
    /// over the given logger marked file-only (see the remarks), with the production unit check
    /// (<see cref="EnabledUnit"/>: the Windows machine ID against
    /// <see cref="PlatformPolicy.EnabledUnitMachineIdSha256"/>); and the write pipeline of section
    /// 3.10, composed lazily by the <see cref="WriteService"/> class on the first change the person
    /// asks for, never here: its own detector, privilege manager and firmware store, the platform
    /// policy, the real writer over the real native write seam, the real one-restart BitLocker
    /// suspender, the snapshot store and the write record log at their default roots under the data
    /// folder, every one over the file-only logger. This method is one of the two composition roots
    /// of the real writer (section 2.2); building the source, as the tests do, builds neither it nor
    /// the suspender.
    /// </summary>
    /// <param name="logger">Optional logger shared by every service; null disables logging.</param>
    /// <returns>A ready-to-use live source.</returns>
    public static LiveFirmwareSource CreateWindows(ILogger? logger)
    {
        ILogger log = logger ?? Serilog.Core.Logger.None;
        ILogger coreLog = LogPanelMirror.FileOnly(log);
        FirmwareTypeDetector detector = new(coreLog);
        SnapshotStore snapshots = new();
        WriteService writes = new(
            () =>
            {
                FirmwareTypeDetector pipelineDetector = new(coreLog);
                WindowsFirmwareVariableStore pipelineStore = new(pipelineDetector, coreLog);
                return new WritePipeline(
                    pipelineDetector,
                    new PrivilegeManager(coreLog),
                    pipelineStore,
                    PlatformPolicy.CreateWindows(pipelineStore, coreLog),
                    new FirmwareVariableWriter(WindowsNativeFirmwareWriteApi.Instance, coreLog),
                    new BitLockerSuspender(coreLog),
                    snapshots,
                    new WriteRecordLog(),
                    baselineRoot: null,
                    logger: coreLog);
            },
            snapshots,
            log);
        return new LiveFirmwareSource(
            detector,
            new PrivilegeManager(coreLog),
            new WindowsFirmwareVariableStore(detector, coreLog),
            new MachineIdentity(coreLog),
            log,
            writeService: writes,
            unit: new EnabledUnit());
    }

    /// <summary>
    /// Runs the precondition checks, then the identity inspection and the enumeration side by
    /// side on thread-pool threads, and returns the outcome. Never throws and never returns a
    /// faulted task: a cancelled token or an exception becomes a <see cref="SourceStatus.Failed"/> result.
    /// </summary>
    /// <param name="cancellationToken">Checked between steps and while the load waits for the identity; a cancelled load fails with <see cref="CancelledMessage"/>.</param>
    /// <returns>The result.</returns>
    public Task<SourceResult> LoadAsync(CancellationToken cancellationToken)
    {
        // The token is deliberately not handed to Task.Run: that would make the returned task
        // cancelled (and awaiting it throw) instead of yielding a Failed result.
        return Task.Run(() => Load(cancellationToken));
    }

    private SourceResult Load(CancellationToken cancellationToken)
    {
        try
        {
            return LoadCore(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.Debug("Live load cancelled");
            return Failed(FirmwareType.Unknown, null, null, CancelledMessage);
        }
        catch (Exception ex)
        {
            // The failure text is the result; the shell logs it once at Error.
            _logger.Debug(ex, "Live load failed unexpectedly");
            return Failed(FirmwareType.Unknown, null, null, ex.Message);
        }
    }

    private SourceResult LoadCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Refusals, failures, warnings and the outcome are Debug here: the shell records them in
        // the log panel once (the sentence as INFO, a warning as WARN, a failure as ERROR).
        FirmwareType firmwareType = FirmwareType.Unknown;
        string[] detectionWarning = Array.Empty<string>();
        try
        {
            firmwareType = _detector.Detect();
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Firmware type detection threw");
            detectionWarning = new[] { FirmwareTypeWarningPrefix + ex.GetType().Name + ": " + ex.Message };
        }

        if (firmwareType != FirmwareType.Uefi)
        {
            _logger.Debug("Live load refused: firmware type {FirmwareType}", firmwareType);
            return Refused(firmwareType, null, Strings.NotUefiSentence, detectionWarning);
        }

        bool elevated = false;
        string[] elevationWarning = Array.Empty<string>();
        try
        {
            elevated = _privileges.IsElevated;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Elevation check threw");
            elevationWarning = new[] { ElevationWarningPrefix + ex.GetType().Name + ": " + ex.Message };
        }

        if (!elevated)
        {
            _logger.Debug("Live load refused: the process is not elevated");
            return Refused(firmwareType, PrivilegeState.NotElevated, Strings.NotElevatedSentence, elevationWarning);
        }

        PrivilegeState privilege;
        string? privilegeDetail;
        try
        {
            privilege = _privileges.EnableSystemEnvironmentPrivilege();
            privilegeDetail = (_privileges as PrivilegeManager)?.LastDetail;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Enabling SeSystemEnvironmentPrivilege threw");
            privilege = PrivilegeState.ElevatedPrivilegeAbsent;
            privilegeDetail = ex.GetType().Name + ": " + ex.Message;
        }

        if (privilege != PrivilegeState.Enabled)
        {
            _logger.Debug("Live load refused: privilege state {PrivilegeState} {Detail}", privilege, privilegeDetail ?? string.Empty);
            string sentence = privilege == PrivilegeState.NotElevated ? Strings.NotElevatedSentence : Strings.PrivilegeAbsentSentence;
            string[] detail = string.IsNullOrWhiteSpace(privilegeDetail)
                ? Array.Empty<string>()
                : new[] { PrivilegeDetailPrefix + privilegeDetail.Trim() };
            return Refused(firmwareType, privilege, sentence, detail);
        }

        cancellationToken.ThrowIfCancellationRequested();

        // The identity is read on its own thread while the variables are enumerated (remarks).
        Stopwatch identityClock = Stopwatch.StartNew();
        Task<MachineIdentityResult> identityTask = Task.Run(() => _identity.Inspect());

        EnumerationResult? enumeration;
        List<string> warnings = new();
        MachineIdentityResult? identity;
        try
        {
            enumeration = _store.Enumerate();
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Enumerate threw");
            identity = AwaitIdentity(identityTask, identityClock, warnings, cancellationToken);
            return Failed(firmwareType, PrivilegeState.Enabled, identity, "Enumerate threw " + ex.GetType().Name + ": " + ex.Message);
        }

        identity = AwaitIdentity(identityTask, identityClock, warnings, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<FirmwareVariable> variables = enumeration?.Variables ?? Array.Empty<FirmwareVariable>();
        EnumerationMethod method = enumeration?.Method ?? EnumerationMethod.None;
        string? error = enumeration?.Error;
        if (enumeration is null)
        {
            error = "Enumerate returned no result";
        }

        if (variables.Count == 0 && !string.IsNullOrEmpty(error))
        {
            _logger.Debug("Live load failed: {Error}", error);
            return Failed(firmwareType, PrivilegeState.Enabled, identity, EnumerationFailedPrefix + error.Trim());
        }

        if (!string.IsNullOrEmpty(error))
        {
            warnings.Add(EnumerationIncompletePrefix + error.Trim());
        }

        if (enumeration?.Warnings is not null)
        {
            foreach (string warning in enumeration.Warnings)
            {
                if (!string.IsNullOrWhiteSpace(warning))
                {
                    warnings.Add(warning.Trim());
                }
            }
        }

        // The unit check reads one registry value; it never throws, and without a check the unit
        // is not confirmed. What it found is logged, never the machine ID.
        UnitMatch unit = Unit?.Check() ?? UnitMatch.MachineIdNotRead;
        _logger.Debug(
            "Live load complete: {Count} variables via {Method}, {WarningCount} warnings, unit check {Unit}",
            variables.Count,
            method,
            warnings.Count,
            unit);

        return new SourceResult(
            SourceStatus.Loaded,
            Description,
            IsOffline,
            firmwareType,
            PrivilegeState.Enabled,
            variables,
            method,
            warnings,
            identity,
            null)
        {
            UnitConfirmed = unit == UnitMatch.Confirmed,
        };
    }

    /// <summary>
    /// Waits for the identity read at most what is left of <see cref="_identityTimeout"/> since it
    /// started. Adds a warning for each identity error, for an inspector that threw, and for a read
    /// that did not finish in time; the last is left to finish on its own, its outcome observed and
    /// discarded, and yields null. A cancelled token ends the wait with <see cref="OperationCanceledException"/>.
    /// </summary>
    private MachineIdentityResult? AwaitIdentity(Task<MachineIdentityResult> identityTask, Stopwatch clock, List<string> warnings, CancellationToken cancellationToken)
    {
        TimeSpan remaining = _identityTimeout - clock.Elapsed;
        bool finished;
        try
        {
            finished = identityTask.Wait(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Abandon(identityTask);
            throw;
        }
        catch (AggregateException)
        {
            // The inspector threw; GetResult below rethrows the exception itself.
            finished = true;
        }

        if (!finished)
        {
            Abandon(identityTask);
            string seconds = _identityTimeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
            _logger.Debug("Machine identity did not finish within {Seconds} s; the load goes on without it", seconds);
            warnings.Add(IdentityWarningPrefix + string.Format(CultureInfo.InvariantCulture, Strings.IdentityTimedOutFormat, seconds, Strings.Unknown));
            return null;
        }

        MachineIdentityResult? identity;
        try
        {
            identity = identityTask.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Machine identity inspection threw");
            warnings.Add(IdentityWarningPrefix + ex.GetType().Name + ": " + ex.Message);
            return null;
        }

        if (identity?.Errors is not null)
        {
            foreach (string identityError in identity.Errors)
            {
                if (!string.IsNullOrWhiteSpace(identityError))
                {
                    warnings.Add(IdentityWarningPrefix + identityError.Trim());
                }
            }
        }

        return identity;
    }

    /// <summary>Leaves an identity read that is still running to finish on its own, with a fault observed and discarded.</summary>
    private static void Abandon(Task identityTask)
    {
        _ = identityTask.ContinueWith(
            finished => _ = finished.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private SourceResult Refused(FirmwareType firmwareType, PrivilegeState? privilege, string sentence, IReadOnlyList<string> warnings)
    {
        return new SourceResult(
            SourceStatus.Refused,
            Description,
            IsOffline,
            firmwareType,
            privilege,
            Array.Empty<FirmwareVariable>(),
            EnumerationMethod.None,
            warnings,
            null,
            sentence);
    }

    private SourceResult Failed(FirmwareType firmwareType, PrivilegeState? privilege, MachineIdentityResult? identity, string message)
    {
        return new SourceResult(
            SourceStatus.Failed,
            Description,
            IsOffline,
            firmwareType,
            privilege,
            Array.Empty<FirmwareVariable>(),
            EnumerationMethod.None,
            Array.Empty<string>(),
            identity,
            message);
    }
}
