// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.CommandLine;
using UefiAccess.Cli.Output;
using UefiAccess.Cli.Services;
using UefiAccess.Core.Baseline;
using UefiAccess.Core.Decoders;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Privileges;
using UefiAccess.Core.Settings.Dell;

namespace UefiAccess.Cli.Commands;

/// <summary>The JSON document of the baseline command.</summary>
/// <param name="Command">"baseline".</param>
/// <param name="Ok">True when the export wrote its files without errors.</param>
/// <param name="Directory">The export folder.</param>
/// <param name="RunId">The folder name (local timestamp).</param>
/// <param name="Enumeration">Enumeration summary.</param>
/// <param name="VariableCount">Variables exported.</param>
/// <param name="DecodedCount">Variables the decoders understood.</param>
/// <param name="FilesWritten">Relative paths of every file written.</param>
/// <param name="Errors">Export errors.</param>
internal sealed record BaselineDocument(
    string Command,
    bool Ok,
    string Directory,
    string RunId,
    EnumerationSummary Enumeration,
    int VariableCount,
    int DecodedCount,
    IReadOnlyList<string> FilesWritten,
    IReadOnlyList<string> Errors);

/// <summary>
/// "uefiaccess baseline": exports index.json, vars/*.bin, decoded.json, platform.json,
/// dell-settings.json and log.txt into &lt;root&gt;/&lt;yyyyMMdd-HHmmss&gt; and prints the folder and
/// counts. The Dell BIOS settings are read once, through Dell Command | Monitor only: the Dell settings read of a baseline, and so of the gate script that runs one,
/// never starts Dell Command | Configure and never waits for the fallback. The Dell probe of
/// platform.json is unchanged since Milestone 1: when the program is installed, it still runs
/// only its two read options, the version and the Secure Boot state. A Dell read that did not
/// answer is recorded as such; its sentences are warnings and never change the exit code.
/// </summary>
internal static class BaselineCommand
{
    /// <summary>The prefix of each Dell sentence the baseline prints as a warning.</summary>
    private const string DellWarningPrefix = "dell settings: ";

    /// <summary>
    /// What a baseline reads the Dell settings with: Dell Command | Monitor only, without the full
    /// enumeration, so the Dell settings read of a baseline never starts a Dell Command | Configure
    /// process (the Dell probe keeps its own two read options).
    /// </summary>
    internal static readonly DellReadOptions DellOptions = new(DellSourcePreference.DellCommandMonitor, false);

    /// <summary>Builds the command over the production services.</summary>
    /// <param name="verbose">The global --verbose option.</param>
    /// <returns>The command.</returns>
    public static Command Create(Option<bool> verbose)
    {
        return Create(verbose, CliServices.Create);
    }

    /// <summary>Builds the command over the services from <paramref name="servicesFactory"/>.</summary>
    /// <param name="verbose">The global --verbose option.</param>
    /// <param name="servicesFactory">Creates the services for an invocation; receives the --verbose flag.</param>
    /// <returns>The command.</returns>
    public static Command Create(Option<bool> verbose, Func<bool, CliServices> servicesFactory)
    {
        Option<bool> json = CommonOptions.Json();
        Option<string?> outDir = new("--out", "-o")
        {
            Description = "Parent directory for the export; the run folder yyyyMMdd-HHmmss is created inside it. Default: " + BaselineExporter.DefaultRootDirectory,
            HelpName = "dir",
        };
        Command command = new("baseline", "Export a timestamped baseline of every variable (index.json, vars/*.bin, decoded.json, platform.json, dell-settings.json, log.txt).");
        command.Add(json);
        command.Add(outDir);
        command.SetAction(parseResult => CommandRunner.Run(
            parseResult,
            verbose,
            json,
            "baseline",
            servicesFactory,
            context => Execute(context, CommandRunner.GetString(parseResult, outDir))));
        return command;
    }

    private static int Execute(CommandContext context, string? outDir)
    {
        DateTimeOffset timestamp = DateTimeOffset.Now;
        string runId = Format.RunId(timestamp);
        string root;
        try
        {
            root = Path.GetFullPath(string.IsNullOrWhiteSpace(outDir) ? BaselineExporter.DefaultRootDirectory : outDir);
        }
        catch (Exception ex)
        {
            return context.Fail(ExitCodes.Usage, "The value '" + outDir + "' given to --out is not a valid directory path: " + ex.Message);
        }

        VariableSnapshot snapshot = VariableSnapshot.Load(context.Services);
        if (snapshot.Failed)
        {
            return context.Fail(ExitCodes.NativeFailure, snapshot.FailureMessage);
        }

        snapshot.ReportPartial(context);

        MachineIdentityResult identity = SafeInspect.Identity(context.Services);
        PlatformSnapshot platform = new(
            identity,
            SafeInspect.Tpm(context.Services),
            SafeInspect.SecureBootPolicy(context.Services, runPowerShellCrossCheck: true),
            SafeInspect.BitLocker(context.Services),
            SafeInspect.Dell(context.Services));
        DellSettingsResult dell = SafeInspect.DellSettings(context.Services, DellOptions);
        foreach (string sentence in DellSentencesOf(dell))
        {
            context.Warn(DellWarningPrefix + sentence);
        }

        IReadOnlyList<DecodedVariable> decoded = SafeInspect.DecodeAll(context.Services, snapshot.Set);
        context.Logger.Information("baseline {RunId}: {VariableCount} variables, {DecodedCount} decoded, exporting to {Root}", runId, snapshot.Set.Count, decoded.Count, root);

        BaselineRequest request = new(
            root,
            runId,
            snapshot.Set.All,
            context.Preconditions.FirmwareType,
            context.Preconditions.Privilege ?? PrivilegeState.NotElevated,
            identity,
            decoded,
            platform,
            context.Services.LogCapture.Text,
            CliServices.ToolVersion,
            timestamp)
        {
            DellSettings = DellSettingsFile.ToDocument(dell),
        };

        BaselineExportResult result;
        try
        {
            result = context.Services.Exporter.Export(request)
                ?? new BaselineExportResult(Path.Combine(root, runId), 0, Array.Empty<string>(), new[] { "Export returned no result" });
        }
        catch (Exception ex)
        {
            result = new BaselineExportResult(Path.Combine(root, runId), 0, Array.Empty<string>(), new[] { "Export threw " + ex.GetType().Name + ": " + ex.Message });
        }

        IReadOnlyList<string> files = result.FilesWritten ?? Array.Empty<string>();
        IReadOnlyList<string> errors = result.Errors ?? Array.Empty<string>();
        foreach (string error in errors)
        {
            context.Error.WriteLine("Export error: " + error);
            context.Logger.Error("export error: {Error}", error);
        }

        if (context.Json)
        {
            context.WriteJson(new BaselineDocument("baseline", errors.Count == 0, result.Directory, runId, snapshot.ToSummary(), result.VariableCount, decoded.Count, files, errors));
        }
        else
        {
            TableWriter table = new("Field", "Value");
            table.AddRow("Baseline directory", result.Directory);
            table.AddRow("Run id", runId);
            table.AddRow("Variables enumerated", snapshot.Set.Count);
            table.AddRow("Variables exported", result.VariableCount);
            table.AddRow("Variables decoded", decoded.Count);
            table.AddRow("Files written", files.Count);
            table.AddRow("Export errors", errors.Count);
            table.Write(context.Out);
        }

        if (errors.Count > 0 && files.Count == 0)
        {
            return ExitCodes.NativeFailure;
        }

        return errors.Count > 0 || snapshot.Partial ? ExitCodes.Partial : ExitCodes.Success;
    }

    /// <summary>
    /// The sentences a Dell read gives the baseline's warnings: the
    /// problem sentence when nothing answered, the fallback sentence, and each warning, in that
    /// order. None of them changes the exit code.
    /// </summary>
    /// <param name="dell">The Dell read.</param>
    /// <returns>The sentences; empty for a complete read on the verified BIOS.</returns>
    internal static IReadOnlyList<string> DellSentencesOf(DellSettingsResult dell)
    {
        List<string> sentences = new();
        if (DellSentences.UnavailableSentence(dell) is string unavailable)
        {
            sentences.Add(unavailable);
        }

        if (!string.IsNullOrWhiteSpace(dell.FallbackSentence))
        {
            sentences.Add(dell.FallbackSentence);
        }

        sentences.AddRange((dell.Warnings ?? Array.Empty<string>()).Where(warning => !string.IsNullOrWhiteSpace(warning)));
        return sentences;
    }
}
