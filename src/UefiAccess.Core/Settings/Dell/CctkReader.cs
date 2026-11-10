// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Serilog;
using UefiAccess.Core.Platform;

namespace UefiAccess.Core.Settings.Dell;

/// <summary>Outcome of one run of Dell Command | Configure for one read option, through <see cref="CctkReader.RunCctk"/>.</summary>
/// <param name="Option">The setting that was asked for.</param>
/// <param name="Argument">The one argument the run passed: <see cref="CctkReader.ArgumentFor"/> of <paramref name="Option"/>.</param>
/// <param name="Started">True when the process was launched; false when the program was not found or could not be started.</param>
/// <param name="ExitCode">The exit code when the process ended; null when it did not start or was killed before reporting one.</param>
/// <param name="StandardOutput">Trimmed standard output (empty when none).</param>
/// <param name="StandardError">Trimmed standard error (empty when none).</param>
/// <param name="TimedOut">True when the bound elapsed and the process tree was killed.</param>
/// <param name="Error">One-line description when nothing was run; otherwise null.</param>
/// <param name="ElapsedMilliseconds">Wall-clock time spent.</param>
internal sealed record CctkRunOutcome(
    CctkReadOption Option,
    string Argument,
    bool Started,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    string? Error,
    double ElapsedMilliseconds);

/// <summary>
/// The only launcher of Dell Command | Configure for the Dell settings, and the third and last file that may call ProcessRunner.Run. It passes
/// exactly one argument per run, one of the nine read options below, given without a value, which
/// asks the program to print the setting rather than change it (seen on the unit for Secure Boot;
/// the other eight run there first in step M4-H2); <see cref="ArgumentFor"/> maps each
/// <see cref="CctkReadOption"/> to its constant, so a caller can hand over neither free text, nor
/// a value, nor a second argument, nor a file to apply or to write. The program is the one
/// <see cref="DellInterfaceProbe.FindCctk"/> finds under the two Program Files known folders when
/// the reader is constructed, kept in a read-only field: there is no path parameter, and no
/// environment variable decides which program runs. Every bound is clamped to
/// <see cref="CctkSettingsProvider.RunTimeout"/> (15 seconds) before the one call, so no caller
/// can wait longer. ProcessRunner adds the rest: the full path or nothing, no shell, no window,
/// both streams redirected and decoded with the OEM code page, and the process tree killed when
/// the bound elapses. The nine constants are pinned by reflection, the source of
/// <see cref="RunCctk"/> by a launch check, and the code of this file as a whole by a digest, in
/// NoWriteGuardTests, so any change to the code here is a guard change, reviewed at gate G-A; the
/// guard's line scan still allows only the probe's two options after the program's name on any
/// line, so no comment in this file names an option next to it. Runs are sequential: the settings
/// provider makes one at a time, and it is the only caller.
/// </summary>
internal sealed class CctkReader
{
    /// <summary>The read option of Secure Boot, the one the M1 probe passes too.</summary>
    internal const string SecureBootArgument = "--secureboot";

    /// <summary>The read option of SATA operation (the alias of EmbSataRaid).</summary>
    internal const string SataOperationArgument = "--embsataraid";

    /// <summary>The read option of TPM security.</summary>
    internal const string TpmSecurityArgument = "--tpm";

    /// <summary>The read option of TPM activation.</summary>
    internal const string TpmActivationArgument = "--tpmactivation";

    /// <summary>The read option of Virtualization.</summary>
    internal const string VirtualizationArgument = "--virtualization";

    /// <summary>The read option of VT for Direct I/O.</summary>
    internal const string VtForDirectIoArgument = "--vtfordirectio";

    /// <summary>The read option of the integrated NIC (the alias of EmbNic1).</summary>
    internal const string IntegratedNicArgument = "--embnic1";

    /// <summary>The read option of Wake on LAN.</summary>
    internal const string WakeOnLanArgument = "--wakeonlan";

    /// <summary>The read option of Num Lock at boot.</summary>
    internal const string NumLockArgument = "--numlock";

    /// <summary>The <see cref="CctkRunOutcome.Error"/> of a run that was not started because the program was not found.</summary>
    private const string NotFoundError = "cctk.exe was not found in the Program Files folders, so nothing was started";

    private readonly ILogger _logger;

    /// <summary>The full path of the program, found once by the constructor; null when it is not installed.</summary>
    private readonly string? _cctkPath;

    /// <summary>
    /// Creates the reader and looks for the program once, under the two Program Files known
    /// folders only (<see cref="DellInterfaceProbe.FindCctk"/>). Nothing is run here.
    /// </summary>
    /// <param name="logger">Optional Serilog logger for the search and every run; null disables logging.</param>
    public CctkReader(ILogger? logger)
    {
        _logger = logger ?? Serilog.Core.Logger.None;
        _cctkPath = DellInterfaceProbe.FindCctk(_logger);
    }

    /// <summary>True when the constructor found the program; when false, <see cref="RunCctk"/> starts nothing.</summary>
    internal bool Found => _cctkPath is not null;

    /// <summary>The one argument for a read option: one of the nine constants of this class.</summary>
    /// <param name="option">The setting to read.</param>
    /// <returns>The option's constant.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a member of <see cref="CctkReadOption"/>.</exception>
    internal static string ArgumentFor(CctkReadOption option) => option switch
    {
        CctkReadOption.SecureBoot => SecureBootArgument,
        CctkReadOption.SataOperation => SataOperationArgument,
        CctkReadOption.TpmSecurity => TpmSecurityArgument,
        CctkReadOption.TpmActivation => TpmActivationArgument,
        CctkReadOption.Virtualization => VirtualizationArgument,
        CctkReadOption.VtForDirectIo => VtForDirectIoArgument,
        CctkReadOption.IntegratedNic => IntegratedNicArgument,
        CctkReadOption.WakeOnLan => WakeOnLanArgument,
        CctkReadOption.NumLock => NumLockArgument,
        _ => throw new ArgumentOutOfRangeException(nameof(option)),
    };

    /// <summary>
    /// Runs the program once with the read option's constant as its only argument, from the
    /// program's own folder, and waits at most <paramref name="bound"/>, clamped to
    /// <see cref="CctkSettingsProvider.RunTimeout"/>. When the program was not found nothing runs
    /// and the outcome says it was not started. Never retries.
    /// </summary>
    /// <param name="option">The setting to read.</param>
    /// <param name="bound">The longest the caller can wait; anything above the run timeout is cut to it.</param>
    /// <returns>The run's outcome.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a member of <see cref="CctkReadOption"/>; nothing is started.</exception>
    internal CctkRunOutcome RunCctk(CctkReadOption option, TimeSpan bound)
    {
        if (_cctkPath is null)
        {
            return new CctkRunOutcome(option, ArgumentFor(option), false, null, string.Empty, string.Empty, false, NotFoundError, 0);
        }

        TimeSpan limit = bound < CctkSettingsProvider.RunTimeout ? bound : CctkSettingsProvider.RunTimeout;
        string? workingDirectory = null;
        try
        {
            workingDirectory = Path.GetDirectoryName(_cctkPath);
        }
        catch (Exception)
        {
            // Run from the current directory instead.
        }

        ProcessRunResult run = ProcessRunner.Run(_cctkPath, workingDirectory, limit, _logger, ArgumentFor(option));
        return new CctkRunOutcome(option, ArgumentFor(option), run.Started, run.ExitCode, run.StandardOutput, run.StandardError, run.TimedOut, run.Error, run.ElapsedMilliseconds);
    }
}
