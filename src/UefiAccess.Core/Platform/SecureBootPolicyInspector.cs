// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using Microsoft.Win32;
using Serilog;
using UefiAccess.Core.Variables;

namespace UefiAccess.Core.Platform;

/// <summary>Interpretation of one Secure Boot flag variable read through the store.</summary>
/// <param name="Value">The flag (first data byte non-zero), or null when absent or unreadable.</param>
/// <param name="Readable">True when the store returned the variable successfully (even with zero-length data).</param>
/// <param name="Note">A one-line problem description for the Errors list, or null.</param>
internal sealed record SecureBootVariableReading(bool? Value, bool Readable, string? Note);

/// <summary>Presence of a Secure Boot key variable (PK) read through the store.</summary>
/// <param name="Present">
/// True when the store returned the variable, even with zero-length data: the same rule as
/// <see cref="UefiAccess.Core.Decoders.SecureBootStateDecoder"/> applies to an enumerated PK.
/// </param>
/// <param name="Note">A one-line problem description for the Errors list, or null.</param>
internal sealed record SecureBootVariablePresence(bool Present, string? Note);

/// <summary>
/// Reads Secure Boot policy from three read-only sources: the registry value
/// HKLM\SYSTEM\CurrentControlSet\Control\SecureBoot\State\UEFISecureBootEnabled, the SecureBoot,
/// SetupMode, DeployedMode and AuditMode variables plus the presence of PK through the variable
/// store, and optionally PowerShell's Confirm-SecureBootUEFI with a 10-second timeout (Windows
/// PowerShell started only by its absolute System32 path, its output switched to UTF-8). Never
/// throws; every failure is collected in <see cref="SecureBootPolicyResult.Errors"/>.
/// </summary>
public sealed class SecureBootPolicyInspector : IPlatformInspector<SecureBootPolicyResult>
{
    private const string RegistryKeyPath = @"SYSTEM\CurrentControlSet\Control\SecureBoot\State";
    private const string RegistryValueName = "UEFISecureBootEnabled";
    private const string PowerShellCommand = "Confirm-SecureBootUEFI";

    /// <summary>
    /// The script passed to -Command: switch the child's console output to UTF-8 first, so its
    /// output and error text reach this process intact whatever the OEM code page is, then run the
    /// cmdlet. Both streams are decoded as UTF-8.
    /// </summary>
    internal const string PowerShellScript = "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; " + PowerShellCommand;

    private static readonly TimeSpan PowerShellTimeout = TimeSpan.FromSeconds(10);
    private static readonly Encoding PowerShellOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly IFirmwareVariableStore _store;
    private readonly bool _runPowerShellCrossCheck;
    private readonly ILogger _logger;

    /// <summary>Creates the inspector.</summary>
    /// <param name="store">Variable store used to read the Secure Boot variables.</param>
    /// <param name="runPowerShellCrossCheck">True to also run Confirm-SecureBootUEFI through PowerShell.</param>
    public SecureBootPolicyInspector(IFirmwareVariableStore store, bool runPowerShellCrossCheck)
        : this(store, runPowerShellCrossCheck, null)
    {
    }

    /// <summary>Creates the inspector with a logger.</summary>
    /// <param name="store">Variable store used to read the Secure Boot variables.</param>
    /// <param name="runPowerShellCrossCheck">True to also run Confirm-SecureBootUEFI through PowerShell.</param>
    /// <param name="logger">Optional Serilog logger; null disables logging.</param>
    public SecureBootPolicyInspector(IFirmwareVariableStore store, bool runPowerShellCrossCheck, ILogger? logger)
    {
        _store = store;
        _runPowerShellCrossCheck = runPowerShellCrossCheck;
        _logger = logger ?? Serilog.Core.Logger.None;
    }

    /// <inheritdoc />
    public SecureBootPolicyResult Inspect()
    {
        List<string> errors = new();

        bool? registryEnabled = null;
        if (OperatingSystem.IsWindows())
        {
            registryEnabled = ReadRegistryValue(errors);
        }
        else
        {
            _logger.Debug("Secure Boot registry read skipped: not running on Windows");
            errors.Add("registry value " + RegistryValueName + " is only available on Windows");
        }

        SecureBootVariableReading secureBoot = ReadFlag("SecureBoot", errors);
        SecureBootVariableReading setupMode = ReadFlag("SetupMode", errors);
        SecureBootVariableReading deployedMode = ReadFlag("DeployedMode", errors);
        SecureBootVariableReading auditMode = ReadFlag("AuditMode", errors);
        SecureBootVariablePresence pk = ReadPresence("PK", errors);

        bool? powerShell = null;
        if (_runPowerShellCrossCheck)
        {
            if (OperatingSystem.IsWindows())
            {
                powerShell = RunPowerShellCrossCheck(errors);
            }
            else
            {
                _logger.Debug("{Command} skipped: not running on Windows", PowerShellCommand);
                errors.Add(PowerShellCommand + " is only available on Windows");
            }
        }

        bool capable = ComputeCapable(setupMode.Readable, pk.Present);
        bool sourcesAgree = ComputeSourcesAgree(registryEnabled, secureBoot.Value, powerShell);
        _logger.Information(
            "Secure Boot policy: registry {Registry}, variable {Variable}, setup mode {SetupMode}, PK present {PkPresent}, PowerShell {PowerShell}, capable {Capable}, sources agree {Agree}",
            registryEnabled,
            secureBoot.Value,
            setupMode.Value,
            pk.Present,
            powerShell,
            capable,
            sourcesAgree);

        return new SecureBootPolicyResult(
            RegistryEnabled: registryEnabled,
            VariableSecureBoot: secureBoot.Value,
            VariableSetupMode: setupMode.Value,
            VariableDeployedMode: deployedMode.Value,
            VariableAuditMode: auditMode.Value,
            PowerShellConfirm: powerShell,
            Capable: capable,
            SourcesAgree: sourcesAgree,
            Errors: errors);
    }

    /// <summary>
    /// Interprets a store read of a single-byte Secure Boot flag variable. Success with data yields
    /// the first byte as a boolean; NotFound yields null without a note (DeployedMode and AuditMode
    /// are legitimately absent on older firmware); any other status yields null with a note.
    /// </summary>
    /// <param name="name">Variable name, for the note text.</param>
    /// <param name="read">The store's read result (null is tolerated).</param>
    /// <returns>The interpretation; never throws.</returns>
    internal static SecureBootVariableReading InterpretRead(string name, ReadResult? read)
    {
        if (read is null)
        {
            return new SecureBootVariableReading(null, false, name + ": the variable store returned no result");
        }

        if (read.Status == ReadStatus.Success)
        {
            byte[]? data = read.Variable?.Data;
            if (data is null)
            {
                return new SecureBootVariableReading(null, false, name + ": read reported success without a variable");
            }

            if (data.Length == 0)
            {
                return new SecureBootVariableReading(null, true, name + ": variable is present but has zero-length data");
            }

            string? note = data.Length == 1 ? null : $"{name}: expected 1 byte, found {data.Length}; using the first byte";
            return new SecureBootVariableReading(data[0] != 0, true, note);
        }

        if (read.Status == ReadStatus.NotFound)
        {
            return new SecureBootVariableReading(null, false, null);
        }

        string detail = string.IsNullOrWhiteSpace(read.Error) ? read.Status.ToString() : read.Error;
        return new SecureBootVariableReading(null, false, $"{name}: {detail}");
    }

    /// <summary>
    /// Interprets a store read of a Secure Boot key variable as presence. Success yields present
    /// (a zero-length variable still exists); NotFound yields absent without a note, because PK is
    /// legitimately absent while the firmware is in setup mode; any other status yields absent
    /// with a note.
    /// </summary>
    /// <param name="name">Variable name, for the note text.</param>
    /// <param name="read">The store's read result (null is tolerated).</param>
    /// <returns>The interpretation; never throws.</returns>
    internal static SecureBootVariablePresence InterpretPresence(string name, ReadResult? read)
    {
        if (read is null)
        {
            return new SecureBootVariablePresence(false, name + ": the variable store returned no result");
        }

        if (read.Status == ReadStatus.Success)
        {
            return read.Variable is null
                ? new SecureBootVariablePresence(false, name + ": read reported success without a variable")
                : new SecureBootVariablePresence(true, null);
        }

        if (read.Status == ReadStatus.NotFound)
        {
            return new SecureBootVariablePresence(false, null);
        }

        string detail = string.IsNullOrWhiteSpace(read.Error) ? read.Status.ToString() : read.Error;
        return new SecureBootVariablePresence(false, $"{name}: {detail}");
    }

    /// <summary>
    /// The Capable rule: "UEFI and PK present or SetupMode readable".
    /// The UEFI half is implied, because the variable store only returns variables on UEFI firmware.
    /// </summary>
    /// <param name="setupModeReadable">Whether the SetupMode variable could be read.</param>
    /// <param name="pkPresent">Whether the PK variable exists.</param>
    /// <returns>True when the firmware exposes Secure Boot.</returns>
    internal static bool ComputeCapable(bool setupModeReadable, bool pkPresent)
    {
        return setupModeReadable || pkPresent;
    }

    /// <summary>
    /// Parses Confirm-SecureBootUEFI output: the last non-blank line must be "True" or "False"
    /// (case-insensitive); anything else, including error text, yields null.
    /// </summary>
    /// <param name="standardOutput">The captured standard output.</param>
    /// <returns>The boolean or null.</returns>
    internal static bool? ParsePowerShellBoolean(string? standardOutput)
    {
        if (string.IsNullOrWhiteSpace(standardOutput))
        {
            return null;
        }

        string? lastLine = standardOutput
            .Split('\n')
            .Select(line => line.Trim())
            .LastOrDefault(line => line.Length > 0);
        if (lastLine is null)
        {
            return null;
        }

        if (string.Equals(lastLine, "True", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(lastLine, "False", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return null;
    }

    /// <summary>
    /// True when at least one of the three enabled-state sources is available and every available
    /// source reports the same value. With no source available the result is false, so the gate
    /// never records agreement that was not observed.
    /// </summary>
    /// <param name="registryEnabled">UEFISecureBootEnabled from the registry.</param>
    /// <param name="variableSecureBoot">The SecureBoot variable.</param>
    /// <param name="powerShellConfirm">Confirm-SecureBootUEFI.</param>
    /// <returns>Whether the available sources agree.</returns>
    internal static bool ComputeSourcesAgree(bool? registryEnabled, bool? variableSecureBoot, bool? powerShellConfirm)
    {
        bool[] available = new[] { registryEnabled, variableSecureBoot, powerShellConfirm }
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        if (available.Length == 0)
        {
            return false;
        }

        return available.All(value => value == available[0]);
    }

    /// <summary>
    /// Locates Windows PowerShell under the System folder (System32\WindowsPowerShell\v1.0).
    /// There is no fallback to a bare "powershell.exe": the elevated tool must never let Windows
    /// search the current directory or the PATH for the program it starts.
    /// </summary>
    /// <returns>The absolute path, or null when the System folder is unknown or has no powershell.exe.</returns>
    internal static string? ResolvePowerShellPath()
    {
        string? system;
        try
        {
            system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        }
        catch (Exception)
        {
            system = null;
        }

        return ResolvePowerShellPath(system);
    }

    /// <summary>
    /// Builds the Windows PowerShell path under <paramref name="systemDirectory"/> and returns it
    /// when the file exists. Never throws.
    /// </summary>
    /// <param name="systemDirectory">The absolute System folder, for example C:\Windows\System32.</param>
    /// <returns>The absolute path, or null when the folder is empty, relative, or has no powershell.exe.</returns>
    internal static string? ResolvePowerShellPath(string? systemDirectory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(systemDirectory) || !Path.IsPathFullyQualified(systemDirectory))
            {
                return null;
            }

            string candidate = Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            return File.Exists(candidate) ? candidate : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private SecureBootVariableReading ReadFlag(string name, List<string> errors)
    {
        ReadResult? read = TryRead(name);
        SecureBootVariableReading reading = InterpretRead(name, read);
        _logger.Debug("Secure Boot variable {Name}: status {Status}, value {Value}, readable {Readable}", name, read?.Status, reading.Value, reading.Readable);
        if (reading.Note is not null)
        {
            errors.Add(reading.Note);
        }

        return reading;
    }

    private SecureBootVariablePresence ReadPresence(string name, List<string> errors)
    {
        ReadResult? read = TryRead(name);
        SecureBootVariablePresence presence = InterpretPresence(name, read);
        _logger.Debug("Secure Boot variable {Name}: status {Status}, present {Present}", name, read?.Status, presence.Present);
        if (presence.Note is not null)
        {
            errors.Add(presence.Note);
        }

        return presence;
    }

    private ReadResult? TryRead(string name)
    {
        try
        {
            return _store.Read(name, KnownGuids.EfiGlobalVariable);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Variable store threw while reading {Name}", name);
            return new ReadResult(null, ReadStatus.Failed, $"{ex.GetType().Name}: {ex.Message}", 0);
        }
    }

    private bool? ReadRegistryValue(List<string> errors)
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(RegistryKeyPath, writable: false);
            if (key is null)
            {
                _logger.Debug("Registry key HKLM\\{Key} not present", RegistryKeyPath);
                return null;
            }

            object? raw = key.GetValue(RegistryValueName);
            bool? value = WmiValues.ToNullableBool(raw);
            _logger.Debug("Registry HKLM\\{Key}\\{Value} = {Raw} -> {Parsed}", RegistryKeyPath, RegistryValueName, raw, value);
            return value;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Registry read of {Value} failed", RegistryValueName);
            errors.Add($"registry {RegistryValueName}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private bool? RunPowerShellCrossCheck(List<string> errors)
    {
        string? executable = ResolvePowerShellPath();
        if (executable is null)
        {
            _logger.Warning("{Command} skipped: powershell.exe was not found under the System folder", PowerShellCommand);
            errors.Add($"{PowerShellCommand}: could not run PowerShell: powershell.exe was not found in the Windows System folder (System32\\WindowsPowerShell\\v1.0)");
            return null;
        }

        ProcessRunResult run = ProcessRunner.Run(executable, null, PowerShellTimeout, PowerShellOutputEncoding, _logger, "-NoProfile", "-NonInteractive", "-Command", PowerShellScript);
        if (run.Error is not null)
        {
            errors.Add($"{PowerShellCommand}: could not run PowerShell: {run.Error}");
            return null;
        }

        if (run.TimedOut)
        {
            errors.Add($"{PowerShellCommand}: timed out after {PowerShellTimeout.TotalSeconds:F0} seconds");
            return null;
        }

        bool? parsed = ParsePowerShellBoolean(run.StandardOutput);
        if (parsed is null)
        {
            string detail = FirstLine(run.StandardError) ?? FirstLine(run.StandardOutput) ?? "no output";
            errors.Add($"{PowerShellCommand}: did not return True or False (exit code {run.ExitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}): {detail}");
        }

        return parsed;
    }

    private static string? FirstLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return text.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0);
    }
}
