// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Serilog;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Privileges;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Settings.Dell;
using UefiAccess.Core.Variables;

namespace UefiAccess.Cli.Services;

/// <summary>
/// The services a change to the firmware needs beyond the read services of <see cref="CliPlatform"/>
///: the writer that makes the one write call, the one-restart
/// BitLocker suspend, the snapshot store and the write record log, the baseline root the dry
/// run compares with, and the unit check of stage S2. Production composes the real writer and
/// suspender, and the production unit check, in <see cref="CliPlatform.CreateWindows"/> and
/// nowhere else; the tests compose fakes, a snapshot store and a record log under their own
/// temporary folder, a baseline root there too, and a unit check over a fake machine ID.
/// </summary>
internal sealed class CliWriteServices
{
    /// <summary>Creates the bundle. Every service but the baseline root is required.</summary>
    /// <param name="writer">The writer of the one write call (stage S15).</param>
    /// <param name="suspender">The one-restart BitLocker suspend (stage S13).</param>
    /// <param name="snapshotStore">
    /// Builds the snapshot store for a root: the argument is the --snapshot-root folder as a full
    /// path, or null for the default root. The store itself refuses a root outside the data folder
    /// and passes every folder through the data-folder check, so this factory adds no check.
    /// </param>
    /// <param name="recordLog">Builds the write record log (write-log.jsonl) in its folder.</param>
    /// <param name="baselineRoot">The folder whose newest baseline the dry run compares with; null selects the default baseline root.</param>
    /// <param name="unit">
    /// The unit check of stage S2: whether this Windows installation is the M3 test unit. Outside
    /// Core only the production check (the Windows machine ID against
    /// <see cref="PlatformPolicy.EnabledUnitMachineIdSha256"/>) can be built.
    /// </param>
    /// <exception cref="ArgumentNullException">A required service is null.</exception>
    public CliWriteServices(
        IFirmwareVariableWriter writer,
        IBitLockerSuspender suspender,
        Func<string?, SnapshotStore> snapshotStore,
        Func<WriteRecordLog> recordLog,
        string? baselineRoot,
        EnabledUnit unit)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(suspender);
        ArgumentNullException.ThrowIfNull(snapshotStore);
        ArgumentNullException.ThrowIfNull(recordLog);
        ArgumentNullException.ThrowIfNull(unit);
        Writer = writer;
        Suspender = suspender;
        CreateSnapshotStore = snapshotStore;
        CreateRecordLog = recordLog;
        BaselineRoot = baselineRoot;
        Unit = unit;
    }

    /// <summary>The writer of the one write call.</summary>
    public IFirmwareVariableWriter Writer { get; }

    /// <summary>The one-restart BitLocker suspend.</summary>
    public IBitLockerSuspender Suspender { get; }

    /// <summary>Builds the snapshot store for the --snapshot-root folder, or for the default root when the argument is null.</summary>
    public Func<string?, SnapshotStore> CreateSnapshotStore { get; }

    /// <summary>Builds the write record log.</summary>
    public Func<WriteRecordLog> CreateRecordLog { get; }

    /// <summary>The folder whose newest baseline the dry run compares with, or null for the default baseline root.</summary>
    public string? BaselineRoot { get; }

    /// <summary>The unit check of stage S2, which the pipeline's platform policy runs.</summary>
    public EnabledUnit Unit { get; }
}

/// <summary>
/// The machine-facing services a command body runs against: the privilege manager, the firmware
/// type detector, the variable store, the platform inspectors, the Dell settings reader and, for
/// the write commands, the write services. <see cref="CreateWindows"/> composes the real Windows
/// implementations (native calls, WMI, the registry, cctk and PowerShell, the Dell settings
/// reader, and the one firmware writer and BitLocker suspender of the product); the tests build
/// one from an in-memory or scripted store and fakes that return fixed results, so every command
/// runs on any host without firmware access and no test reads a Dell interface. A bundle composed
/// without write services refuses to build a pipeline, a snapshot store or a record log, so a read
/// command's test can never reach a write.
/// </summary>
internal sealed class CliPlatform
{
    /// <summary>The message of the exception a bundle without write services gives when a write command asks for them.</summary>
    internal const string NoWriteServicesMessage = "This platform was composed without the write services; the write commands cannot run on it.";

    /// <summary>The message the dell command fails with (exit code 5) on a bundle composed without the Dell settings reader.</summary>
    internal const string NoDellSettingsReaderMessage = "This platform was composed without the Dell settings reader.";

    /// <summary>Creates the bundle. Every read service is required; the write services and the Dell settings reader are optional.</summary>
    /// <param name="privilegeManager">Elevation and SeSystemEnvironmentPrivilege handling.</param>
    /// <param name="firmwareTypeDetector">Firmware type detection.</param>
    /// <param name="store">The read-only firmware variable store.</param>
    /// <param name="identity">Machine identity inspector.</param>
    /// <param name="tpm">TPM inspector.</param>
    /// <param name="bitLocker">BitLocker inspector.</param>
    /// <param name="dell">Dell interface probe.</param>
    /// <param name="secureBootPolicy">Factory for the Secure Boot policy inspector; the argument says whether to run the PowerShell cross-check.</param>
    /// <param name="prepareDataFolder">Prepares an elevated write into a folder under the data root; <see cref="Logging.PrepareDataFolderFor(string)"/> when null.</param>
    /// <param name="write">The write services; null composes a bundle on which every write command fails with exit code 5 before anything is prepared.</param>
    /// <param name="dellSettings">
    /// The Dell settings reader, which only reads; null composes a bundle
    /// on which the dell command fails with exit code 5 (<see cref="NoDellSettingsReaderMessage"/>)
    /// and a baseline records a Dell read that was unavailable.
    /// </param>
    /// <exception cref="ArgumentNullException">A read service is null.</exception>
    public CliPlatform(
        IPrivilegeManager privilegeManager,
        IFirmwareTypeDetector firmwareTypeDetector,
        IFirmwareVariableStore store,
        IPlatformInspector<MachineIdentityResult> identity,
        IPlatformInspector<TpmInfoResult> tpm,
        IPlatformInspector<BitLockerResult> bitLocker,
        IPlatformInspector<DellInterfaceProbeResult> dell,
        Func<bool, IPlatformInspector<SecureBootPolicyResult>> secureBootPolicy,
        Func<string, string?>? prepareDataFolder = null,
        CliWriteServices? write = null,
        IDellSettingsReader? dellSettings = null)
    {
        ArgumentNullException.ThrowIfNull(privilegeManager);
        ArgumentNullException.ThrowIfNull(firmwareTypeDetector);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(tpm);
        ArgumentNullException.ThrowIfNull(bitLocker);
        ArgumentNullException.ThrowIfNull(dell);
        ArgumentNullException.ThrowIfNull(secureBootPolicy);
        PrivilegeManager = privilegeManager;
        FirmwareTypeDetector = firmwareTypeDetector;
        Store = store;
        Identity = identity;
        Tpm = tpm;
        BitLocker = bitLocker;
        Dell = dell;
        SecureBootPolicy = secureBootPolicy;
        PrepareDataFolder = prepareDataFolder ?? Logging.PrepareDataFolderFor;
        Write = write;
        DellSettings = dellSettings;
    }

    /// <summary>Elevation and SeSystemEnvironmentPrivilege handling.</summary>
    public IPrivilegeManager PrivilegeManager { get; }

    /// <summary>Firmware type detection (GetFirmwareType).</summary>
    public IFirmwareTypeDetector FirmwareTypeDetector { get; }

    /// <summary>The read-only firmware variable store.</summary>
    public IFirmwareVariableStore Store { get; }

    /// <summary>Machine identity inspector.</summary>
    public IPlatformInspector<MachineIdentityResult> Identity { get; }

    /// <summary>TPM inspector.</summary>
    public IPlatformInspector<TpmInfoResult> Tpm { get; }

    /// <summary>BitLocker inspector.</summary>
    public IPlatformInspector<BitLockerResult> BitLocker { get; }

    /// <summary>Dell interface probe.</summary>
    public IPlatformInspector<DellInterfaceProbeResult> Dell { get; }

    /// <summary>Factory for the Secure Boot policy inspector; the argument says whether to run the PowerShell cross-check.</summary>
    public Func<bool, IPlatformInspector<SecureBootPolicyResult>> SecureBootPolicy { get; }

    /// <summary>
    /// Prepares an elevated write into a folder: under the data root (ProgramData\UefiAccess) it
    /// checks every existing folder on the way (a plain folder with a trusted owner) and creates
    /// the missing ones with the protected access list, as <see cref="Logging.PrepareDataFolderFor(string)"/>
    /// does for the baseline export. Receives the folder; returns null to go ahead or the refusal.
    /// </summary>
    public Func<string, string?> PrepareDataFolder { get; }

    /// <summary>The write services, or null when this bundle was composed for the read commands only.</summary>
    public CliWriteServices? Write { get; }

    /// <summary>
    /// The Dell settings reader: the agreed Dell BIOS settings through Dell Command | Monitor or,
    /// when the options allow it, Dell Command | Configure. It reads only and never throws. Null
    /// when this bundle was composed without it.
    /// </summary>
    public IDellSettingsReader? DellSettings { get; }

    /// <summary>
    /// Builds the snapshot store for one write command: the --snapshot-root folder, or the default
    /// root under the data folder when <paramref name="rootOverride"/> is null. A root outside the
    /// data folder is refused by the store at stage S9 (check snapshotRoot) before anything is
    /// created.
    /// </summary>
    /// <param name="rootOverride">The --snapshot-root folder as a full path, or null.</param>
    /// <returns>The store.</returns>
    /// <exception cref="InvalidOperationException">The bundle has no write services.</exception>
    public SnapshotStore CreateSnapshotStore(string? rootOverride)
    {
        return RequireWrite().CreateSnapshotStore(rootOverride);
    }

    /// <summary>Builds the write record log (write-log.jsonl) for one command.</summary>
    /// <returns>The log.</returns>
    /// <exception cref="InvalidOperationException">The bundle has no write services.</exception>
    public WriteRecordLog CreateRecordLog()
    {
        return RequireWrite().CreateRecordLog();
    }

    /// <summary>
    /// Composes the write pipeline of one command from this bundle: the firmware type detector,
    /// the privilege manager and the store; a <see cref="PlatformPolicy"/> over the bundle's
    /// identity, Secure Boot (without the PowerShell cross-check), BitLocker and TPM inspectors and
    /// the unit check of the write services; the writer and the suspender of the write services;
    /// the given snapshot store and record log; and the baseline root.
    /// </summary>
    /// <param name="snapshots">The snapshot store from <see cref="CreateSnapshotStore"/>.</param>
    /// <param name="records">The record log from <see cref="CreateRecordLog"/>.</param>
    /// <param name="logger">The run logger; null disables logging.</param>
    /// <returns>The pipeline.</returns>
    /// <exception cref="InvalidOperationException">The bundle has no write services.</exception>
    public IWritePipeline CreatePipeline(SnapshotStore snapshots, WriteRecordLog records, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(records);
        CliWriteServices write = RequireWrite();
        PlatformPolicy policy = new(Identity, SecureBootPolicy(false), BitLocker, Tpm, write.Unit, logger);
        return new WritePipeline(
            FirmwareTypeDetector,
            PrivilegeManager,
            Store,
            policy,
            write.Writer,
            write.Suspender,
            snapshots,
            records,
            write.BaselineRoot,
            logger);
    }

    /// <summary>
    /// Composes the production services: <see cref="Core.Privileges.PrivilegeManager"/>,
    /// <see cref="Core.Platform.FirmwareTypeDetector"/>, <see cref="WindowsFirmwareVariableStore"/>,
    /// <see cref="MachineIdentity"/>, <see cref="TpmInfo"/>, <see cref="BitLockerInspector"/>,
    /// <see cref="DellInterfaceProbe"/>, <see cref="SecureBootPolicyInspector"/> and the
    /// <see cref="DellSettingsReader"/> (which reads only; built, it has looked for Dell Command |
    /// Configure on disk and started nothing), every one of them logging through
    /// <paramref name="logger"/>, and the write services: the firmware writer
    /// over the Windows write seam (WindowsNativeFirmwareWriteApi.Instance) and the BitLocker
    /// suspender, each constructed here and nowhere else in the command-line tool, the snapshot store at the default root or at --snapshot-root, the write
    /// record log in its default folder, and the production unit check (the Windows machine ID
    /// against <see cref="PlatformPolicy.EnabledUnitMachineIdSha256"/>). Constructing them makes
    /// no call; only a confirmed write command reaches the writer and the suspender.
    /// </summary>
    /// <param name="logger">The run logger; null disables logging.</param>
    /// <returns>The bundle.</returns>
    public static CliPlatform CreateWindows(ILogger? logger)
    {
        ILogger log = logger ?? Serilog.Core.Logger.None;
        FirmwareTypeDetector detector = new(log);
        IFirmwareVariableStore store = new WindowsFirmwareVariableStore(detector, log);
        CliWriteServices write = new(
            new FirmwareVariableWriter(WindowsNativeFirmwareWriteApi.Instance, log),
            new BitLockerSuspender(log),
            root => root is null ? new SnapshotStore() : new SnapshotStore(root),
            () => new WriteRecordLog(),
            null,
            new EnabledUnit());
        return new CliPlatform(
            new PrivilegeManager(log),
            detector,
            store,
            new MachineIdentity(log),
            new TpmInfo(log),
            new BitLockerInspector(log),
            new DellInterfaceProbe(log),
            runPowerShellCrossCheck => new SecureBootPolicyInspector(store, runPowerShellCrossCheck, log),
            null,
            write,
            new DellSettingsReader(log));
    }

    /// <summary>The write services, or the refusal of a bundle composed for the read commands only.</summary>
    private CliWriteServices RequireWrite()
    {
        return Write ?? throw new InvalidOperationException(NoWriteServicesMessage);
    }
}
