// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Serilog;
using Serilog.Events;
using UefiAccess.Core.Baseline;
using UefiAccess.Core.Decoders;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Privileges;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Settings.Dell;
using UefiAccess.Core.Variables;

namespace UefiAccess.Cli.Services;

/// <summary>
/// Composition root of the command-line tool: the logger (file, optional console on standard
/// error, and the in-memory capture for log.txt), the output writers, the decoder registry, the
/// baseline exporter and the <see cref="CliPlatform"/> (privilege manager, firmware type detector,
/// variable store, platform inspectors, the Dell settings reader and the write services the write
/// commands compose their pipeline from). One instance is created per command invocation.
/// <see cref="Create(bool)"/> composes the production services; the second overload lets the
/// tests substitute an in-memory store, fake inspectors, captured writers and a silent logger.
/// </summary>
internal sealed class CliServices : IDisposable
{
    private readonly ILogger _fileLogger;
    private readonly CliPlatform _platform;

    /// <summary>Creates the services over explicit pieces.</summary>
    /// <param name="verbose">True when --verbose was given.</param>
    /// <param name="logger">The run logger (file plus capture).</param>
    /// <param name="fileLogger">The underlying file logger, disposed with the run logger.</param>
    /// <param name="capture">The in-memory capture of the run log.</param>
    /// <param name="output">Standard output.</param>
    /// <param name="error">Standard error.</param>
    /// <param name="platform">The machine-facing services.</param>
    internal CliServices(bool verbose, ILogger logger, ILogger fileLogger, LogCapture capture, TextWriter output, TextWriter error, CliPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(fileLogger);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(platform);
        Verbose = verbose;
        Logger = logger;
        _fileLogger = fileLogger;
        LogCapture = capture;
        Out = output;
        Error = error;
        _platform = platform;
        Decoders = DecoderRegistry.CreateDefault();
        Exporter = new BaselineExporter();
    }

    /// <summary>
    /// Version of the tool as printed by info and written into index.json and gate.json: the
    /// shared <see cref="Core.Diagnostics.ToolVersion.Current"/>, which the WPF shell shows too.
    /// </summary>
    public static string ToolVersion => Core.Diagnostics.ToolVersion.Current;

    /// <summary>True when --verbose was given (log events are mirrored to standard error).</summary>
    public bool Verbose { get; }

    /// <summary>The run logger; every native call and command step goes through it.</summary>
    public ILogger Logger { get; }

    /// <summary>In-memory copy of the run log for the baseline's log.txt.</summary>
    public LogCapture LogCapture { get; }

    /// <summary>Standard output: tables and JSON documents only.</summary>
    public TextWriter Out { get; }

    /// <summary>Standard error: precondition messages, warnings and diagnostics.</summary>
    public TextWriter Error { get; }

    /// <summary>Elevation and SeSystemEnvironmentPrivilege handling.</summary>
    public IPrivilegeManager PrivilegeManager => _platform.PrivilegeManager;

    /// <summary>Firmware type detection (GetFirmwareType).</summary>
    public IFirmwareTypeDetector FirmwareTypeDetector => _platform.FirmwareTypeDetector;

    /// <summary>The read-only firmware variable store.</summary>
    public IFirmwareVariableStore Store => _platform.Store;

    /// <summary>The M1 decoders.</summary>
    public DecoderRegistry Decoders { get; }

    /// <summary>Baseline export.</summary>
    public BaselineExporter Exporter { get; }

    /// <summary>Machine identity inspector (WMI).</summary>
    public IPlatformInspector<MachineIdentityResult> Identity => _platform.Identity;

    /// <summary>TPM inspector (WMI).</summary>
    public IPlatformInspector<TpmInfoResult> Tpm => _platform.Tpm;

    /// <summary>BitLocker inspector (WMI, read-only queries).</summary>
    public IPlatformInspector<BitLockerResult> BitLocker => _platform.BitLocker;

    /// <summary>Dell interface probe.</summary>
    public IPlatformInspector<DellInterfaceProbeResult> Dell => _platform.Dell;

    /// <summary>
    /// The Dell settings reader (read-only; see <see cref="CliPlatform.DellSettings"/>), or null
    /// when the platform was composed without it. Commands read through
    /// <see cref="SafeInspect.DellSettings"/>, which never throws.
    /// </summary>
    public IDellSettingsReader? DellSettings => _platform.DellSettings;

    /// <summary>
    /// Prepares an elevated write into a folder under the data root (see
    /// <see cref="CliPlatform.PrepareDataFolder"/>): null to go ahead, otherwise the refusal.
    /// </summary>
    public Func<string, string?> PrepareDataFolder => _platform.PrepareDataFolder;
    /// <summary>
    /// Builds the snapshot store of one write command (see <see cref="CliPlatform.CreateSnapshotStore"/>):
    /// the --snapshot-root folder, or the default root when <paramref name="rootOverride"/> is null.
    /// </summary>
    /// <param name="rootOverride">The --snapshot-root folder as a full path, or null.</param>
    /// <returns>The store.</returns>
    public SnapshotStore CreateSnapshotStore(string? rootOverride) => _platform.CreateSnapshotStore(rootOverride);

    /// <summary>Builds the write record log (write-log.jsonl) of one command (see <see cref="CliPlatform.CreateRecordLog"/>).</summary>
    /// <returns>The log.</returns>
    public WriteRecordLog CreateRecordLog() => _platform.CreateRecordLog();

    /// <summary>
    /// Composes the write pipeline of one command over the platform services, the given snapshot
    /// store and record log, and the run logger (see <see cref="CliPlatform.CreatePipeline"/>).
    /// </summary>
    /// <param name="snapshots">The snapshot store from <see cref="CreateSnapshotStore"/>.</param>
    /// <param name="records">The record log from <see cref="CreateRecordLog"/>.</param>
    /// <returns>The pipeline.</returns>
    public IWritePipeline CreateWritePipeline(SnapshotStore snapshots, WriteRecordLog records) => _platform.CreatePipeline(snapshots, records, Logger);

    /// <summary>
    /// Creates the production services for one command invocation: the daily log file, the
    /// console writers and <see cref="CliPlatform.CreateWindows"/>. Never throws.
    /// </summary>
    /// <param name="verbose">True to mirror log events to standard error.</param>
    /// <returns>The services.</returns>
    public static CliServices Create(bool verbose)
    {
        return Create(verbose, CliPlatform.CreateWindows, Console.Out, Console.Error, null);
    }

    /// <summary>
    /// Creates the services for one command invocation over explicit pieces. The run logger is
    /// <paramref name="baseLogger"/> (or the daily log file when null) combined with the in-memory
    /// capture; <paramref name="platformFactory"/> receives that logger and returns the
    /// machine-facing services. Throws only what the factory throws; <see cref="CommandRunner"/>
    /// turns that into exit code 5.
    /// </summary>
    /// <param name="verbose">True to mirror log events to standard error (only meaningful for the daily log file).</param>
    /// <param name="platformFactory">Builds the privilege manager, detector, store and inspectors.</param>
    /// <param name="output">Standard output.</param>
    /// <param name="error">Standard error.</param>
    /// <param name="baseLogger">The logger to combine with the capture, or null for the daily log file.</param>
    /// <returns>The services.</returns>
    public static CliServices Create(bool verbose, Func<ILogger, CliPlatform> platformFactory, TextWriter output, TextWriter error, ILogger? baseLogger)
    {
        ArgumentNullException.ThrowIfNull(platformFactory);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        LogCapture capture = new();
        ILogger fileLogger = baseLogger ?? Logging.Create("cli", verbose);
        ILogger logger;
        try
        {
            logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.WithProperty("Component", "cli")
                .WriteTo.Logger(fileLogger, LogEventLevel.Verbose)
                .WriteTo.Sink(capture, LogEventLevel.Verbose)
                .CreateLogger();
        }
        catch (Exception)
        {
            logger = fileLogger;
        }

        CliPlatform platform = platformFactory(logger) ?? throw new InvalidOperationException("The platform factory returned no services.");
        return new CliServices(verbose, logger, fileLogger, capture, output, error, platform);
    }

    /// <summary>Creates the Secure Boot policy inspector over the variable store.</summary>
    /// <param name="runPowerShellCrossCheck">True to also run Confirm-SecureBootUEFI.</param>
    /// <returns>The inspector.</returns>
    public IPlatformInspector<SecureBootPolicyResult> CreateSecureBootPolicyInspector(bool runPowerShellCrossCheck)
    {
        return _platform.SecureBootPolicy(runPowerShellCrossCheck);
    }

    /// <summary>Flushes and closes the loggers.</summary>
    public void Dispose()
    {
        try
        {
            (Logger as IDisposable)?.Dispose();
            if (!ReferenceEquals(Logger, _fileLogger))
            {
                (_fileLogger as IDisposable)?.Dispose();
            }
        }
        catch (Exception)
        {
            // Closing a log file must never change the exit code.
        }
    }
}
