// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Serilog;

namespace UefiAccess.Core.Platform;

/// <summary>
/// Reads TPM facts from root\CIMV2\Security\MicrosoftTpm Win32_Tpm (SpecVersion,
/// IsEnabled_InitialValue, IsActivated_InitialValue). An absent class or namespace means no TPM
/// is reported (Present false). Never throws; failures are collected in Errors.
/// </summary>
public sealed class TpmInfo : IPlatformInspector<TpmInfoResult>
{
    private const string Namespace = @"root\CIMV2\Security\MicrosoftTpm";
    private const string Query = "SELECT SpecVersion, IsEnabled_InitialValue, IsActivated_InitialValue FROM Win32_Tpm";
    private const string NotWindowsMessage = "TPM information requires WMI, which is only available on Windows";

    private readonly ILogger _logger;

    /// <summary>Creates the inspector without a logger.</summary>
    public TpmInfo()
        : this(null)
    {
    }

    /// <summary>Creates the inspector that logs the WMI query to <paramref name="logger"/>.</summary>
    /// <param name="logger">Optional Serilog logger; null disables logging.</param>
    public TpmInfo(ILogger? logger)
    {
        _logger = logger ?? Serilog.Core.Logger.None;
    }

    /// <inheritdoc />
    public TpmInfoResult Inspect()
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger.Debug("TpmInfo skipped: not running on Windows");
            return new TpmInfoResult(false, null, null, null, new[] { NotWindowsMessage });
        }

        List<string> errors = new();
        try
        {
            WmiQueryResult result = WmiHelper.Query(Namespace, Query, "Win32_Tpm", _logger, maxInstances: 1);
            if (result.Absent)
            {
                _logger.Information("Win32_Tpm is not available: no TPM reported");
                return new TpmInfoResult(false, null, null, null, errors);
            }

            if (result.Error is not null)
            {
                errors.Add(result.Error);
                return new TpmInfoResult(false, null, null, null, errors);
            }

            if (result.Rows.Count == 0)
            {
                _logger.Information("Win32_Tpm returned no instance: no TPM reported");
                return new TpmInfoResult(false, null, null, null, errors);
            }

            IReadOnlyDictionary<string, object?> row = result.Rows[0];
            string? specVersion = WmiValues.ToText(WmiHelper.Get(row, "SpecVersion"));
            bool? enabled = WmiValues.ToNullableBool(WmiHelper.Get(row, "IsEnabled_InitialValue"));
            bool? activated = WmiValues.ToNullableBool(WmiHelper.Get(row, "IsActivated_InitialValue"));
            _logger.Information("TPM present: spec {SpecVersion}, enabled {Enabled}, activated {Activated}", specVersion, enabled, activated);
            return new TpmInfoResult(true, specVersion, enabled, activated, errors);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "TpmInfo failed unexpectedly");
            errors.Add("Win32_Tpm: " + WmiHelper.Describe(ex));
            return new TpmInfoResult(false, null, null, null, errors);
        }
    }
}
