// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using UefiAccess.Core.Decoders;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Settings;
using UefiAccess.Core.Settings.Dell;
using UefiAccess.Core.Variables;

namespace UefiAccess.Cli.Services;

/// <summary>
/// Runs the platform inspectors, the Dell settings reader and the decoders with a guard: an
/// inspector that throws (contrary to its contract) yields a result whose Errors (for the Dell
/// settings, whose problems) describe the exception instead of crashing the command. Every call is
/// timed and logged.
/// </summary>
internal static class SafeInspect
{
    /// <summary>Machine identity.</summary>
    /// <param name="services">The services.</param>
    /// <returns>The result, never null.</returns>
    public static MachineIdentityResult Identity(CliServices services)
    {
        return Run(services, "MachineIdentity", () => services.Identity.Inspect(), error => new MachineIdentityResult(
            null, null, null, null, null, null, null, null, null, null, null, false, error, new[] { error }));
    }

    /// <summary>TPM facts.</summary>
    /// <param name="services">The services.</param>
    /// <returns>The result, never null.</returns>
    public static TpmInfoResult Tpm(CliServices services)
    {
        return Run(services, "TpmInfo", () => services.Tpm.Inspect(), error => new TpmInfoResult(false, null, null, null, new[] { error }));
    }

    /// <summary>BitLocker volumes.</summary>
    /// <param name="services">The services.</param>
    /// <returns>The result, never null.</returns>
    public static BitLockerResult BitLocker(CliServices services)
    {
        return Run(services, "BitLockerInspector", () => services.BitLocker.Inspect(), error => new BitLockerResult(Array.Empty<BitLockerVolume>(), new[] { error }));
    }

    /// <summary>Dell interface probe.</summary>
    /// <param name="services">The services.</param>
    /// <returns>The result, never null.</returns>
    public static DellInterfaceProbeResult Dell(CliServices services)
    {
        return Run(services, "DellInterfaceProbe", () => services.Dell.Inspect(), error => new DellInterfaceProbeResult(
            false, Array.Empty<string>(), Array.Empty<DellAttributeSample>(), false, null, null, null, false, false, new[] { error }));
    }

    /// <summary>
    /// The agreed Dell BIOS settings, read once with <paramref name="options"/>. The reader never throws by contract; a reader that throws anyway, returns
    /// nothing, or is missing from the platform gives a read no interface answered, with one
    /// problem of the reader's own (<see cref="DellProblemKind.Unexpected"/>, the exception as its
    /// detail) and every agreed setting not read, so the caller always has a result to report.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="options">Which Dell interface the read may use and whether to keep the full enumeration.</param>
    /// <returns>The result, never null.</returns>
    public static DellSettingsResult DellSettings(CliServices services, DellReadOptions options)
    {
        DateTimeOffset started = DateTimeOffset.Now;
        return Run(
            services,
            "DellSettingsReader",
            () => (services.DellSettings ?? throw new InvalidOperationException(CliPlatform.NoDellSettingsReaderMessage)).Read(options),
            error => UnavailableDellSettings(started, error));
    }

    /// <summary>Secure Boot policy (registry, variables, optional PowerShell cross-check).</summary>
    /// <param name="services">The services.</param>
    /// <param name="runPowerShellCrossCheck">True to run Confirm-SecureBootUEFI.</param>
    /// <returns>The result, never null.</returns>
    public static SecureBootPolicyResult SecureBootPolicy(CliServices services, bool runPowerShellCrossCheck)
    {
        return Run(
            services,
            "SecureBootPolicyInspector",
            () => services.CreateSecureBootPolicyInspector(runPowerShellCrossCheck).Inspect(),
            error => new SecureBootPolicyResult(null, null, null, null, null, null, false, false, new[] { error }));
    }

    /// <summary>Secure Boot state from the variable snapshot.</summary>
    /// <param name="services">The services.</param>
    /// <param name="lookup">The snapshot.</param>
    /// <returns>The state, never null.</returns>
    public static SecureBootState SecureBootState(CliServices services, IVariableLookup lookup)
    {
        return Run(services, "SecureBootStateReader", () => SecureBootStateReader.Read(lookup), _ => new SecureBootState(null, null, null, null, false, 0, false, 0, null, null, null));
    }

    /// <summary>Decodes every variable the registry understands.</summary>
    /// <param name="services">The services.</param>
    /// <param name="lookup">The snapshot.</param>
    /// <returns>The decoded variables, never null.</returns>
    public static IReadOnlyList<DecodedVariable> DecodeAll(CliServices services, IVariableLookup lookup)
    {
        return Run(services, "DecoderRegistry.DecodeAll", () => services.Decoders.DecodeAll(lookup) ?? Array.Empty<DecodedVariable>(), _ => Array.Empty<DecodedVariable>());
    }

    /// <summary>Parses a load option; never throws.</summary>
    /// <param name="data">The Boot#### data.</param>
    /// <returns>The parsed option, with Error set when parsing failed.</returns>
    public static LoadOption LoadOption(byte[] data)
    {
        try
        {
            return LoadOptionParser.Parse(data ?? Array.Empty<byte>());
        }
        catch (Exception ex)
        {
            string error = "LoadOptionParser threw " + ex.GetType().Name + ": " + ex.Message;
            return new LoadOption(0, false, false, false, string.Empty, string.Empty, string.Empty, Array.Empty<string>(), 0, null, error);
        }
    }

    /// <summary>
    /// A Dell read that no interface answered because the reader itself failed: outcome
    /// Unavailable, no source, an unknown machine, every agreed setting not read (in catalog order,
    /// as the reader returns them), and the reader's one unexpected problem.
    /// </summary>
    private static DellSettingsResult UnavailableDellSettings(DateTimeOffset readAt, string error)
    {
        FirmwareSetting[] settings = DellAttributeCatalog.Default
            .Select(entry => new FirmwareSetting(
                entry.Id,
                entry.DcmAttributeName,
                entry.DisplayName,
                entry.Category,
                entry.SetupLocation,
                SettingState.NotRead,
                null,
                Array.Empty<SettingValue>(),
                PossibleValuesOrigin.Catalog,
                null,
                false,
                null))
            .ToArray();
        return new DellSettingsResult(
            DellReadOutcome.Unavailable,
            null,
            readAt,
            0,
            new DellMachine(null, null, null),
            settings,
            new[] { new DellInterfaceProblem(DellInterface.Reader, DellProblemKind.Unexpected, error) },
            Array.Empty<string>());
    }

    private static T Run<T>(CliServices services, string name, Func<T> action, Func<string, T> fallback)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            T result = action();
            if (result is null)
            {
                string error = name + " returned no result";
                services.Logger.Warning("{Inspector} returned null after {Elapsed} ms", name, stopwatch.Elapsed.TotalMilliseconds);
                return fallback(error);
            }

            services.Logger.Debug("{Inspector} completed in {Elapsed} ms", name, stopwatch.Elapsed.TotalMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            string error = name + " threw " + ex.GetType().Name + ": " + ex.Message;
            services.Logger.Error(ex, "{Inspector} threw after {Elapsed} ms", name, stopwatch.Elapsed.TotalMilliseconds);
            return fallback(error);
        }
    }
}
