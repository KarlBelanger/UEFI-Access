// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Serilog;

namespace UefiAccess.Core.Platform;

/// <summary>Outcome of the expected-model check for the 2014 Dell OptiPlex 7020.</summary>
/// <param name="Confirmed">True when model, BIOS version and CPU generation all match the planned unit.</param>
/// <param name="Notes">Human-readable explanation of which signals matched, with a warning when the unit looks like the 2024 relaunch.</param>
/// <param name="ModelMatched">True when the model name contains "7020".</param>
/// <param name="BiosMatched">True when the BIOS version is a Dell A-series version (A followed by two digits).</param>
/// <param name="CpuMatched">True when the CPU is a 4th-generation (Haswell) Intel part.</param>
/// <param name="LooksLikeRelaunch">True when the BIOS version or the CPU generation points at the 2024 OptiPlex 7020 relaunch.</param>
internal sealed record ExpectedModelEvaluation(
    bool Confirmed,
    string Notes,
    bool ModelMatched,
    bool BiosMatched,
    bool CpuMatched,
    bool LooksLikeRelaunch);

/// <summary>
/// Reads machine identity from WMI (Win32_ComputerSystem, Win32_BIOS, Win32_BaseBoard,
/// Win32_Processor, Win32_OperatingSystem) and checks it against the expected 2014 Dell OptiPlex 7020.
/// Never throws: every WMI failure is collected in <see cref="MachineIdentityResult.Errors"/>, and
/// on a non-Windows host every identity field is null.
/// </summary>
public sealed class MachineIdentity : IPlatformInspector<MachineIdentityResult>
{
    private const string Cimv2 = @"root\CIMV2";
    private const string NotWindowsMessage = "machine identity requires WMI, which is only available on Windows";

    /// <summary>The one query of <see cref="ReadServiceTag()"/>.</summary>
    internal const string ServiceTagQuery = "SELECT SerialNumber FROM Win32_BIOS";

    private static readonly Regex ASeriesBiosPattern = new(@"^A\d\d$", RegexOptions.CultureInvariant);
    private static readonly Regex RelaunchBiosPattern = new(@"^\d+\.\d+\.\d+$", RegexOptions.CultureInvariant);
    private static readonly Regex HaswellCorePattern = new(@"i[3579]-4\d{3}(?!\d)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex HaswellPentiumCeleronPattern = new(@"\b(?:Pentium|Celeron)\b.*\bG(?:18\d{2}|3[2-4]\d{2})(?!\d)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex OrdinalGenerationPattern = new(@"\b(\d{1,2})(?:st|nd|rd|th)\s+Gen\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex CoreIPattern = new(@"\bi[3579]-(\d{3,5})", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex CoreUltraPattern = new(@"\bCore(?:\(TM\)|™)?\s+Ultra\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex CoreSeriesPattern = new(@"\bCore(?:\(TM\)|™)?\s+(?:Ultra\s+)?[3579]\s+(?:processor\s+)?(\d{3,5})(?!\d)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly ILogger _logger;

    /// <summary>Creates the inspector without a logger.</summary>
    public MachineIdentity()
        : this(null)
    {
    }

    /// <summary>Creates the inspector that logs every WMI query to <paramref name="logger"/>.</summary>
    /// <param name="logger">Optional Serilog logger; null disables logging.</param>
    public MachineIdentity(ILogger? logger)
    {
        _logger = logger ?? Serilog.Core.Logger.None;
    }

    /// <inheritdoc />
    public MachineIdentityResult Inspect()
    {
        List<string> errors = new();
        string? manufacturer = null;
        string? model = null;
        string? systemSku = null;
        string? biosVersion = null;
        string? biosReleaseDate = null;
        string? serviceTag = null;
        string? baseBoard = null;
        string? cpu = null;
        string? osCaption = null;
        string? osVersion = null;
        string? osBuild = null;

        if (!OperatingSystem.IsWindows())
        {
            _logger.Debug("MachineIdentity skipped: not running on Windows");
            errors.Add(NotWindowsMessage);
        }
        else
        {
            try
            {
                IReadOnlyDictionary<string, object?>? computerSystem = QueryFirst("SELECT Manufacturer, Model, SystemSKUNumber FROM Win32_ComputerSystem", "Win32_ComputerSystem", errors);
                if (computerSystem is not null)
                {
                    manufacturer = WmiValues.ToText(WmiHelper.Get(computerSystem, "Manufacturer"));
                    model = WmiValues.ToText(WmiHelper.Get(computerSystem, "Model"));
                    systemSku = WmiValues.ToText(WmiHelper.Get(computerSystem, "SystemSKUNumber"));
                }

                IReadOnlyDictionary<string, object?>? bios = QueryFirst("SELECT SMBIOSBIOSVersion, ReleaseDate, SerialNumber FROM Win32_BIOS", "Win32_BIOS", errors);
                if (bios is not null)
                {
                    biosVersion = WmiValues.ToText(WmiHelper.Get(bios, "SMBIOSBIOSVersion"));
                    biosReleaseDate = WmiValues.FormatCimDateTime(WmiValues.ToText(WmiHelper.Get(bios, "ReleaseDate")));
                    serviceTag = WmiValues.ToText(WmiHelper.Get(bios, "SerialNumber"));
                }

                IReadOnlyDictionary<string, object?>? board = QueryFirst("SELECT Product FROM Win32_BaseBoard", "Win32_BaseBoard", errors);
                if (board is not null)
                {
                    baseBoard = WmiValues.ToText(WmiHelper.Get(board, "Product"));
                }

                IReadOnlyDictionary<string, object?>? processor = QueryFirst("SELECT Name FROM Win32_Processor", "Win32_Processor", errors);
                if (processor is not null)
                {
                    cpu = WmiValues.ToText(WmiHelper.Get(processor, "Name"));
                }

                IReadOnlyDictionary<string, object?>? operatingSystem = QueryFirst("SELECT Caption, Version, BuildNumber FROM Win32_OperatingSystem", "Win32_OperatingSystem", errors);
                if (operatingSystem is not null)
                {
                    osCaption = WmiValues.ToText(WmiHelper.Get(operatingSystem, "Caption"));
                    osVersion = WmiValues.ToText(WmiHelper.Get(operatingSystem, "Version"));
                    osBuild = WmiValues.ToText(WmiHelper.Get(operatingSystem, "BuildNumber"));
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "MachineIdentity failed unexpectedly");
                errors.Add("machine identity: " + WmiHelper.Describe(ex));
            }
        }

        ExpectedModelEvaluation evaluation = EvaluateExpectedModel(model, biosVersion, cpu);
        _logger.Information("Machine identity: {Manufacturer} {Model}, BIOS {Bios}, CPU {Cpu}; expected model confirmed: {Confirmed}", manufacturer, model, biosVersion, cpu, evaluation.Confirmed);

        return new MachineIdentityResult(
            Manufacturer: manufacturer,
            Model: model,
            SystemSku: systemSku,
            BiosVersion: biosVersion,
            BiosReleaseDate: biosReleaseDate,
            ServiceTag: serviceTag,
            BaseBoard: baseBoard,
            Cpu: cpu,
            OsCaption: osCaption,
            OsVersion: osVersion,
            OsBuild: osBuild,
            ExpectedModelConfirmed: evaluation.Confirmed,
            ExpectedModelNotes: evaluation.Notes,
            Errors: errors);
    }

    /// <summary>
    /// Reads the service tag alone (Win32_BIOS.SerialNumber), so that a trace file can replace it
    /// in every line it writes (the input trace of UefiAccess.App and the UI Automation recorder's
    /// trace; TraceFileWriter in Core.Diagnostics). One query, bounded like every WMI query here
    /// (<see cref="WmiHelper"/>, 60 seconds). The query and its outcome are logged; the value
    /// never is. Never throws.
    /// </summary>
    /// <returns>The service tag, trimmed; null when it cannot be read, is empty, or the host is not Windows.</returns>
    public string? ReadServiceTag()
    {
        return ReadServiceTag(wql => WmiHelper.Query(Cimv2, wql, "Win32_BIOS", _logger, maxInstances: 1), OperatingSystem.IsWindows());
    }

    /// <summary>Testable core of <see cref="ReadServiceTag()"/>: the query is the caller's, so no test reaches WMI.</summary>
    /// <param name="query">Runs a WQL query on root\CIMV2 (<see cref="WmiHelper.Query(string, string, string, ILogger, int)"/> in production).</param>
    /// <param name="onWindows">Whether the host is Windows; off Windows nothing is queried.</param>
    /// <returns>The service tag, trimmed, or null.</returns>
    internal string? ReadServiceTag(Func<string, WmiQueryResult> query, bool onWindows)
    {
        if (!onWindows)
        {
            _logger.Debug("Service tag not read: not running on Windows");
            return null;
        }

        try
        {
            WmiQueryResult result = query(ServiceTagQuery);
            if (result.Absent || result.Error is not null || result.Rows.Count == 0)
            {
                _logger.Debug("Service tag not read: {Reason}", result.Absent ? "Win32_BIOS is not available" : result.Error ?? "Win32_BIOS returned no instance");
                return null;
            }

            string? serviceTag = WmiValues.ToText(WmiHelper.Get(result.Rows[0], "SerialNumber"));
            _logger.Debug("Service tag {Outcome} (the value is not logged)", serviceTag is null ? "empty" : "read");
            return serviceTag;
        }
        catch (Exception ex)
        {
            _logger.Debug("Service tag not read: {Reason}", WmiHelper.Describe(ex));
            return null;
        }
    }

    /// <summary>
    /// Checks identity strings against the planned 2014 Dell OptiPlex 7020: the model contains
    /// "7020" (case-insensitive), the BIOS version matches ^A\d\d$ (for example A18), and the CPU
    /// is a 4th-generation part (i3/i5/i7-4xxx, or Pentium/Celeron G18xx, G32xx-G34xx). When the
    /// BIOS version uses the x.y.z scheme or the CPU is 12th generation or newer, the notes warn
    /// that the unit looks like the 2024 relaunch, not the planned unit.
    /// </summary>
    /// <param name="model">Win32_ComputerSystem.Model.</param>
    /// <param name="biosVersion">Win32_BIOS.SMBIOSBIOSVersion.</param>
    /// <param name="cpu">Win32_Processor.Name.</param>
    /// <returns>The evaluation with a human-readable note; never throws.</returns>
    internal static ExpectedModelEvaluation EvaluateExpectedModel(string? model, string? biosVersion, string? cpu)
    {
        string? modelText = Clean(model);
        string? biosText = Clean(biosVersion);
        string? cpuText = Clean(cpu);

        bool modelMatch = modelText is not null && modelText.Contains("7020", StringComparison.OrdinalIgnoreCase);
        bool biosMatch = biosText is not null && ASeriesBiosPattern.IsMatch(biosText);
        bool cpuMatch = cpuText is not null && (HaswellCorePattern.IsMatch(cpuText) || HaswellPentiumCeleronPattern.IsMatch(cpuText));
        bool relaunchBios = biosText is not null && RelaunchBiosPattern.IsMatch(biosText);
        bool relaunchCpu = IsTwelfthGenOrNewer(cpuText);
        int? generation = TryGetIntelCoreGeneration(cpuText);
        bool confirmed = modelMatch && biosMatch && cpuMatch;

        StringBuilder notes = new();

        if (modelText is null)
        {
            notes.Append("Model: not available.");
        }
        else
        {
            notes.Append(CultureInfo.InvariantCulture, $"Model \"{modelText}\": {(modelMatch ? "contains 7020 (match)" : "does not contain 7020 (no match)")}.");
        }

        notes.Append(' ');
        if (biosText is null)
        {
            notes.Append("BIOS version: not available.");
        }
        else
        {
            string biosDetail = biosMatch
                ? "Dell A-series version (match)"
                : relaunchBios
                    ? "x.y.z scheme, not a Dell A-series version (no match)"
                    : "not a Dell A-series version (no match)";
            notes.Append(CultureInfo.InvariantCulture, $"BIOS version \"{biosText}\": {biosDetail}.");
        }

        notes.Append(' ');
        if (cpuText is null)
        {
            notes.Append("CPU: not available.");
        }
        else
        {
            string cpuDetail = cpuMatch
                ? "4th-generation Intel (Haswell) part (match)"
                : generation is int detected
                    ? $"{Ordinal(detected)}-generation Intel Core, not 4th generation (no match)"
                    : "not a recognised 4th-generation Intel part (no match)";
            notes.Append(CultureInfo.InvariantCulture, $"CPU \"{cpuText}\": {cpuDetail}.");
        }

        notes.Append(' ');
        if (confirmed)
        {
            notes.Append("Expected model confirmed: 2014 Dell OptiPlex 7020 (model, BIOS version and CPU generation all match).");
        }
        else
        {
            string summary = string.Join(
                ", ",
                Signal("model", modelText, modelMatch),
                Signal("BIOS version", biosText, biosMatch),
                Signal("CPU", cpuText, cpuMatch));
            notes.Append(CultureInfo.InvariantCulture, $"Expected model not confirmed: {summary}.");
        }

        if (relaunchBios || relaunchCpu)
        {
            List<string> reasons = new();
            if (relaunchBios)
            {
                reasons.Add("the BIOS version uses the x.y.z scheme of the 2024 OptiPlex 7020");
            }

            if (relaunchCpu)
            {
                reasons.Add(generation is int g
                    ? $"the CPU is {Ordinal(g)} generation (12th generation or newer)"
                    : "the CPU is 12th generation or newer");
            }

            notes.Append(" Warning: this looks like the 2024 relaunch, not the planned unit (the 2014 OptiPlex 7020): ");
            notes.Append(string.Join(" and ", reasons));
            notes.Append(". Record this in the gate memo.");
        }

        return new ExpectedModelEvaluation(confirmed, notes.ToString(), modelMatch, biosMatch, cpuMatch, relaunchBios || relaunchCpu);
    }

    /// <summary>
    /// Extracts the Intel Core generation from a processor name: "12th Gen" prefixes,
    /// i3/i5/i7/i9 model numbers, and the newer "Core 5 processor 14500" naming with a five-digit
    /// number. Model numbers: 3 digits (i7-920) are 1st generation; 4 digits starting with 2 to 9
    /// (i7-4790) give the first digit; 4 digits starting with 1 (i7-1065G7, i7-1165G7, i5-1235U)
    /// and 5 digits (i7-10700, i7-14700) give the first two digits, because Intel used 3-digit
    /// numbers for the 1st generation and two-digit generation prefixes from the 10th onwards.
    /// </summary>
    /// <param name="cpu">Win32_Processor.Name.</param>
    /// <returns>The generation, or null when it cannot be determined.</returns>
    internal static int? TryGetIntelCoreGeneration(string? cpu)
    {
        if (string.IsNullOrWhiteSpace(cpu))
        {
            return null;
        }

        Match ordinal = OrdinalGenerationPattern.Match(cpu);
        if (ordinal.Success && int.TryParse(ordinal.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int explicitGeneration) && explicitGeneration > 0)
        {
            return explicitGeneration;
        }

        Match core = CoreIPattern.Match(cpu);
        if (core.Success)
        {
            return GenerationFromModelNumber(core.Groups[1].Value);
        }

        Match series = CoreSeriesPattern.Match(cpu);
        if (series.Success && series.Groups[1].Value.Length == 5)
        {
            return GenerationFromModelNumber(series.Groups[1].Value);
        }

        return null;
    }

    /// <summary>
    /// True when the processor is an Intel Core of the 12th generation or newer, including the
    /// "Core Ultra" and "Core 5/7 processor" brands introduced in 2023.
    /// </summary>
    /// <param name="cpu">Win32_Processor.Name.</param>
    /// <returns>True for 12th generation or newer; false when older or unknown.</returns>
    internal static bool IsTwelfthGenOrNewer(string? cpu)
    {
        if (string.IsNullOrWhiteSpace(cpu))
        {
            return false;
        }

        if (CoreUltraPattern.IsMatch(cpu))
        {
            return true;
        }

        int? generation = TryGetIntelCoreGeneration(cpu);
        if (generation is >= 12)
        {
            return true;
        }

        // "Core 5 processor 120U" style (Series 1 and 2, 2023 and later) uses three-digit numbers.
        Match series = CoreSeriesPattern.Match(cpu);
        return series.Success && series.Groups[1].Value.Length == 3;
    }

    private static int? GenerationFromModelNumber(string digits)
    {
        return digits.Length switch
        {
            3 when digits[0] != '0' => 1,
            4 when digits[0] == '1' => int.Parse(digits.AsSpan(0, 2), NumberStyles.None, CultureInfo.InvariantCulture),
            4 when digits[0] != '0' => digits[0] - '0',
            5 => int.Parse(digits.AsSpan(0, 2), NumberStyles.None, CultureInfo.InvariantCulture),
            _ => null,
        };
    }

    private static string Ordinal(int number)
    {
        int lastTwo = number % 100;
        int last = number % 10;
        string suffix = lastTwo is >= 11 and <= 13
            ? "th"
            : last switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th",
            };
        return number.ToString(CultureInfo.InvariantCulture) + suffix;
    }

    private static string Signal(string label, string? value, bool matched)
    {
        if (value is null)
        {
            return label + " not available";
        }

        return label + (matched ? " matched" : " did not match");
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private IReadOnlyDictionary<string, object?>? QueryFirst(string wql, string label, List<string> errors)
    {
        WmiQueryResult result = WmiHelper.Query(Cimv2, wql, label, _logger, maxInstances: 1);
        if (result.Absent)
        {
            errors.Add(label + ": class not available on this machine");
            return null;
        }

        if (result.Error is not null)
        {
            errors.Add(result.Error);
            return null;
        }

        if (result.Rows.Count == 0)
        {
            errors.Add(label + ": no instance returned");
            return null;
        }

        return result.Rows[0];
    }
}
