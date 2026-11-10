// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.CommandLine;
using System.Globalization;
using UefiAccess.Cli.Output;
using UefiAccess.Cli.Services;
using UefiAccess.Core.Decoders;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Privileges;
using UefiAccess.Core.Variables;

namespace UefiAccess.Cli.Commands;

/// <summary>Gate question (a): boot mode and Secure Boot availability.</summary>
/// <param name="FirmwareType">Detected firmware type name.</param>
/// <param name="PrivilegeState">Privilege state name.</param>
/// <param name="SecureBootCapable">Capable flag from the policy inspector.</param>
/// <param name="SecureBootEnabled">Enabled state from the SecureBoot variable, or null.</param>
/// <param name="SetupMode">SetupMode variable, or null.</param>
/// <param name="DeployedMode">DeployedMode variable, or null.</param>
/// <param name="AuditMode">AuditMode variable, or null.</param>
/// <param name="SourcesAgree">True when the registry, the variable and PowerShell agree.</param>
/// <param name="VariableState">Full Secure Boot state decoded from the variables.</param>
/// <param name="EnumerationMethod">How the variables were obtained.</param>
/// <param name="VariableCount">Number of runtime-visible variables.</param>
/// <param name="EntriesParsed">Entries parsed from the native buffer.</param>
/// <param name="EnumerationError">Enumeration error, or null.</param>
/// <param name="EnumerationWarnings">Non-fatal enumeration warnings.</param>
internal sealed record GateBootMode(
    string FirmwareType,
    string PrivilegeState,
    bool SecureBootCapable,
    bool? SecureBootEnabled,
    bool? SetupMode,
    bool? DeployedMode,
    bool? AuditMode,
    bool SourcesAgree,
    SecureBootState VariableState,
    string EnumerationMethod,
    int VariableCount,
    int EntriesParsed,
    string? EnumerationError,
    IReadOnlyList<string> EnumerationWarnings);

/// <summary>One IFR varstore matched against the runtime variable store (gate question c).</summary>
/// <param name="Line">Line number in the ifr-varstores file.</param>
/// <param name="Name">Varstore name.</param>
/// <param name="Guid">Varstore GUID (lowercase).</param>
/// <param name="IfrSize">Size declared by the IFR.</param>
/// <param name="Present">True when a runtime variable with that name and GUID exists, false when it does not, null when that could not be determined (the enumeration was incomplete and the direct read failed).</param>
/// <param name="Status">The same finding in one word: "present", "absent" or "unknown".</param>
/// <param name="Key">Variable key when present.</param>
/// <param name="Attributes">Raw attributes when present.</param>
/// <param name="AttributeFlags">Short flags when present.</param>
/// <param name="AttributeNames">Full attribute names when present.</param>
/// <param name="RuntimeSize">Runtime size when present.</param>
/// <param name="SizeMatches">True when the runtime size equals the IFR size; null when absent or unknown.</param>
/// <param name="Note">Human-readable finding, including how it was established and, for "unknown", why.</param>
internal sealed record GateVarstoreEntry(
    int Line,
    string Name,
    string Guid,
    int IfrSize,
    bool? Present,
    string Status,
    string? Key,
    uint? Attributes,
    string? AttributeFlags,
    IReadOnlyList<string>? AttributeNames,
    int? RuntimeSize,
    bool? SizeMatches,
    string Note);

/// <summary>The varstore match section of gate.json.</summary>
/// <param name="File">The ifr-varstores file.</param>
/// <param name="EntryCount">Varstores read from the file.</param>
/// <param name="PresentCount">Varstores present at runtime.</param>
/// <param name="AbsentCount">Varstores definitely absent at runtime.</param>
/// <param name="UnknownCount">Varstores whose presence could not be determined; outcome C cannot be ruled out while this is above zero.</param>
/// <param name="SizeMismatchCount">Present varstores whose runtime size differs from the IFR size.</param>
/// <param name="EnumerationComplete">True when the enumeration listed every runtime-visible variable, so a name it did not list is absent; false when names it did not list were read directly.</param>
/// <param name="IncompleteReason">Why the enumeration was incomplete, or null when it was complete.</param>
/// <param name="Entries">Every varstore.</param>
/// <param name="ParseErrors">Lines that could not be read.</param>
internal sealed record GateVarstoreMatch(
    string File,
    int EntryCount,
    int PresentCount,
    int AbsentCount,
    int UnknownCount,
    int SizeMismatchCount,
    bool EnumerationComplete,
    string? IncompleteReason,
    IReadOnlyList<GateVarstoreEntry> Entries,
    IReadOnlyList<string> ParseErrors);

/// <summary>The gate.json document. The recommendation is left empty for a human.</summary>
/// <param name="ToolVersion">Tool version.</param>
/// <param name="TimestampUtc">When the gate ran (UTC).</param>
/// <param name="TimestampLocal">When the gate ran (local time with offset).</param>
/// <param name="Identity">Machine identity with ExpectedModelConfirmed.</param>
/// <param name="BootMode">Gate question (a).</param>
/// <param name="DellInterface">Gate question (b).</param>
/// <param name="VarstoreMatch">Gate question (c), or null when --ifr-varstores was not given.</param>
/// <param name="BitLocker">Gate question (d), BitLocker part.</param>
/// <param name="SecureBootPolicy">Gate question (d), Secure Boot policy part.</param>
/// <param name="Tpm">TPM facts.</param>
/// <param name="Recommendation">Empty; the memo author fills in outcome A, B, C or none.</param>
internal sealed record GateReport(
    string ToolVersion,
    string TimestampUtc,
    string TimestampLocal,
    MachineIdentityResult Identity,
    GateBootMode BootMode,
    DellInterfaceProbeResult DellInterface,
    GateVarstoreMatch? VarstoreMatch,
    BitLockerResult BitLocker,
    SecureBootPolicyResult SecureBootPolicy,
    TpmInfoResult Tpm,
    string Recommendation);

/// <summary>
/// "uefiaccess gate": runs the feasibility probes (a) boot mode, (b) Dell interface, (d) BitLocker
/// and Secure Boot policy, and the varstore match for (c) when --ifr-varstores is given, and
/// writes gate.json.
/// </summary>
internal static class GateCommand
{
    private const string DefaultOutFile = "gate.json";

    private const string AbsentReason = "boot-service only (no EFI_VARIABLE_RUNTIME_ACCESS) or not stored as a UEFI variable";

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
        Option<string?> ifrFile = new("--ifr-varstores")
        {
            Description = "Text file with one IFR varstore per line as Name;GUID;Size (blank lines and lines starting with # are skipped).",
            HelpName = "file",
        };
        Option<string?> outFile = new("--out", "-o")
        {
            Description = "Where to write the gate report. Default: ./" + DefaultOutFile,
            HelpName = "file",
        };
        Command command = new("gate", "Run the feasibility gate probes and write gate.json (identity, boot mode, Dell interface, varstore match, BitLocker, Secure Boot policy).");
        command.Add(json);
        command.Add(ifrFile);
        command.Add(outFile);
        command.SetAction(parseResult => CommandRunner.Run(
            parseResult,
            verbose,
            json,
            "gate",
            servicesFactory,
            context => Execute(context, CommandRunner.GetString(parseResult, ifrFile), CommandRunner.GetString(parseResult, outFile))));
        return command;
    }

    private static int Execute(CommandContext context, string? ifrFile, string? outFile)
    {
        string outPath;
        try
        {
            outPath = Path.GetFullPath(string.IsNullOrWhiteSpace(outFile) ? DefaultOutFile : outFile);
        }
        catch (Exception ex)
        {
            return context.Fail(ExitCodes.Usage, "The value '" + outFile + "' given to --out is not a valid path: " + ex.Message);
        }

        IReadOnlyList<string>? ifrLines = null;
        if (ifrFile is not null)
        {
            if (!File.Exists(ifrFile))
            {
                return context.Fail(ExitCodes.Usage, "The file '" + ifrFile + "' given to --ifr-varstores does not exist.");
            }

            if (!IfrVarstores.TryReadLines(ifrFile, out IReadOnlyList<string> lines, out string? readError))
            {
                return context.Fail(ExitCodes.Usage, "The file '" + ifrFile + "' given to --ifr-varstores could not be read: " + readError);
            }

            ifrLines = lines;
        }

        DateTimeOffset timestamp = DateTimeOffset.Now;
        VariableSnapshot snapshot = VariableSnapshot.Load(context.Services);
        if (snapshot.Failed)
        {
            context.Error.WriteLine(snapshot.FailureMessage);
        }

        snapshot.ReportPartial(context);

        MachineIdentityResult identity = SafeInspect.Identity(context.Services);
        SecureBootState state = SafeInspect.SecureBootState(context.Services, snapshot.Set);
        SecureBootPolicyResult policy = SafeInspect.SecureBootPolicy(context.Services, runPowerShellCrossCheck: true);
        DellInterfaceProbeResult dell = SafeInspect.Dell(context.Services);
        BitLockerResult bitLocker = SafeInspect.BitLocker(context.Services);
        TpmInfoResult tpm = SafeInspect.Tpm(context.Services);
        GateVarstoreMatch? varstores = ifrFile is null || ifrLines is null ? null : MatchVarstores(context.Services, ifrFile, ifrLines, snapshot);

        GateBootMode bootMode = new(
            context.Preconditions.FirmwareType.ToString(),
            (context.Preconditions.Privilege ?? PrivilegeState.NotElevated).ToString(),
            policy.Capable,
            state.SecureBoot ?? policy.VariableSecureBoot,
            state.SetupMode ?? policy.VariableSetupMode,
            state.DeployedMode ?? policy.VariableDeployedMode,
            state.AuditMode ?? policy.VariableAuditMode,
            policy.SourcesAgree,
            state,
            snapshot.Enumeration.Method.ToString(),
            snapshot.Set.Count,
            snapshot.Enumeration.EntriesParsed,
            snapshot.Enumeration.Error,
            snapshot.Enumeration.Warnings);

        GateReport report = new(
            CliServices.ToolVersion,
            timestamp.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            timestamp.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture),
            identity,
            bootMode,
            dell,
            varstores,
            bitLocker,
            policy,
            tpm,
            string.Empty);

        string? writeError = null;
        try
        {
            // Inside the data root (Run-Gate.ps1 writes to ProgramData\UefiAccess\gate\<run>), an
            // elevated run writes only through folders standard users cannot redirect, and
            // creates the missing ones with the protected access list, like the baseline export.
            string? directory = Path.GetDirectoryName(outPath);
            string? refusal = string.IsNullOrEmpty(directory) ? null : context.Services.PrepareDataFolder(directory);
            if (refusal is not null)
            {
                writeError = "Writing '" + outPath + "' failed: " + refusal;
                context.Logger.Error("writing gate report to {Path} refused: {Refusal}", outPath, refusal);
            }
            else
            {
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(outPath, JsonOutput.Serialize(report) + Environment.NewLine);
                context.Logger.Information("gate report written to {Path}", outPath);
            }
        }
        catch (Exception ex)
        {
            writeError = "Writing '" + outPath + "' failed: " + ex.GetType().Name + ": " + ex.Message;
            context.Logger.Error(ex, "writing gate report to {Path} failed", outPath);
        }

        foreach (string error in identity.Errors)
        {
            context.Warn("identity: " + error);
        }

        foreach (string error in policy.Errors)
        {
            context.Warn("secure boot policy: " + error);
        }

        foreach (string error in dell.Errors)
        {
            context.Warn("dell probe: " + error);
        }

        foreach (string error in bitLocker.Errors)
        {
            context.Warn("bitlocker: " + error);
        }

        foreach (string error in tpm.Errors)
        {
            context.Warn("tpm: " + error);
        }

        if (varstores is not null)
        {
            foreach (string error in varstores.ParseErrors)
            {
                context.Warn("ifr-varstores: " + error);
            }

            if (varstores.UnknownCount > 0)
            {
                context.Warn(
                    "ifr-varstores: " + varstores.UnknownCount.ToString(CultureInfo.InvariantCulture)
                    + (varstores.UnknownCount == 1 ? " varstore could not be checked" : " varstores could not be checked")
                    + " because " + varstores.IncompleteReason + " and the direct read failed; their status is unknown");
            }
        }

        if (context.Json)
        {
            context.WriteJson(report);
        }
        else
        {
            WriteText(context.Out, report, outPath, writeError is null);
        }

        if (writeError is not null)
        {
            context.Error.WriteLine(writeError);
            return ExitCodes.NativeFailure;
        }

        return snapshot.ExitCode;
    }

    /// <summary>
    /// Matches every varstore line against the snapshot. When the snapshot is complete, a name it
    /// does not list is absent. When it is not (known-names fallback, an enumeration that stopped
    /// early, or none at all), such a name is read directly through the store: found means
    /// present, ERROR_ENVVAR_NOT_FOUND means absent, and any other failure leaves the varstore
    /// "unknown" with the reason, never "absent".
    /// </summary>
    private static GateVarstoreMatch MatchVarstores(CliServices services, string path, IReadOnlyList<string> lines, VariableSnapshot snapshot)
    {
        IReadOnlyList<IfrVarstore> varstores = IfrVarstores.ParseLines(lines, out IReadOnlyList<string> parseErrors);
        string? incompleteReason = snapshot.IncompleteReason;
        List<GateVarstoreEntry> entries = new(varstores.Count);
        int present = 0;
        int absent = 0;
        int mismatches = 0;
        foreach (IfrVarstore varstore in varstores)
        {
            string guid = varstore.Guid.ToString("D");
            FirmwareVariable? variable = snapshot.Set.Find(varstore.Name, varstore.Guid);
            bool readDirectly = false;
            if (variable is null && incompleteReason is not null)
            {
                ReadResult read = VariableSnapshot.ReadDirect(services, varstore.Name, varstore.Guid);
                if (read.Status == ReadStatus.Success && read.Variable is not null)
                {
                    variable = read.Variable;
                    readDirectly = true;
                }
                else if (read.Status != ReadStatus.NotFound)
                {
                    string failure = read.Status + (string.IsNullOrEmpty(read.Error) ? string.Empty : ": " + read.Error);
                    entries.Add(new GateVarstoreEntry(
                        varstore.Line, varstore.Name, guid, varstore.Size, null, "unknown", null, null, null, null, null, null,
                        "unknown: the enumeration was incomplete (" + incompleteReason + ") and a direct read failed (" + failure + ")"));
                    continue;
                }
            }

            if (variable is null)
            {
                absent++;
                string absentNote = incompleteReason is null
                    ? "absent at runtime: " + AbsentReason
                    : "absent at runtime (confirmed by a direct read, because the enumeration was incomplete): " + AbsentReason;
                entries.Add(new GateVarstoreEntry(varstore.Line, varstore.Name, guid, varstore.Size, false, "absent", null, null, null, null, null, null, absentNote));
                continue;
            }

            present++;
            bool sizeMatches = variable.Size == varstore.Size;
            if (!sizeMatches)
            {
                mismatches++;
            }

            string how = readDirectly ? "present at runtime (found by a direct read; the incomplete enumeration did not list it)" : "present at runtime";
            string note = sizeMatches
                ? how + "; size matches the IFR"
                : how + "; runtime size " + variable.Size.ToString(CultureInfo.InvariantCulture) + " differs from IFR size " + varstore.Size.ToString(CultureInfo.InvariantCulture);
            entries.Add(new GateVarstoreEntry(
                varstore.Line,
                varstore.Name,
                guid,
                varstore.Size,
                true,
                "present",
                variable.Key,
                (uint)variable.Attributes,
                Format.ShortFlags(variable.Attributes),
                Format.AttributeNames(variable.Attributes),
                variable.Size,
                sizeMatches,
                note));
        }

        return new GateVarstoreMatch(
            Path.GetFullPath(path),
            entries.Count,
            present,
            absent,
            entries.Count - present - absent,
            mismatches,
            incompleteReason is null,
            incompleteReason,
            entries,
            parseErrors);
    }

    private static void WriteText(TextWriter output, GateReport report, string outPath, bool written)
    {
        MachineIdentityResult identity = report.Identity;
        GateBootMode boot = report.BootMode;
        DellInterfaceProbeResult dell = report.DellInterface;

        TableWriter table = new("Finding", "Value");
        table.AddRow("Model", Format.Text(identity.Model, "(unknown)"));
        table.AddRow("BIOS version", Format.Text(identity.BiosVersion, "(unknown)"));
        table.AddRow("CPU", Format.Text(identity.Cpu, "(unknown)"));
        table.AddRow("Expected model confirmed", identity.ExpectedModelConfirmed);
        table.AddRow("Expected model notes", Format.Text(identity.ExpectedModelNotes, "(none)"));
        table.AddRow("(a) Firmware type", boot.FirmwareType);
        table.AddRow("(a) Secure Boot capable", boot.SecureBootCapable);
        table.AddRow("(a) Secure Boot enabled", Format.Bool(boot.SecureBootEnabled));
        table.AddRow("(a) Setup mode", Format.Bool(boot.SetupMode));
        table.AddRow("(a) Sources agree", boot.SourcesAgree);
        table.AddRow("(a) Runtime variables", boot.VariableCount.ToString(CultureInfo.InvariantCulture) + " (" + boot.EnumerationMethod + ")");
        table.AddRow("(b) Dell Command Monitor classes defined", Format.Join(dell.DcmClassesFound, "none"));
        table.AddRow("(b) Dell Command Monitor classes responding", Format.Join(dell.DcmClassesResponding, "none"));
        table.AddRow("(b) Dell sample attributes", dell.Samples.Count);
        table.AddRow("(b) cctk found", dell.CctkFound ? Format.Text(dell.CctkPath, "yes") : "no");
        table.AddRow("(b) cctk version", Format.Text(dell.CctkVersion, "(not run)"));
        table.AddRow("(b) Native biosattributes namespace", dell.NativeBiosAttributesNamespacePresent);
        table.AddRow("(b) Native wmisecurity namespace", dell.NativeWmiSecurityNamespacePresent);
        if (report.VarstoreMatch is null)
        {
            table.AddRow("(c) Varstore match", "not run (no --ifr-varstores file)");
        }
        else
        {
            GateVarstoreMatch match = report.VarstoreMatch;
            table.AddRow("(c) Varstores in file", match.EntryCount);
            table.AddRow("(c) Present at runtime", match.PresentCount);
            table.AddRow("(c) Absent at runtime", match.AbsentCount);
            table.AddRow("(c) Status unknown", match.UnknownCount);
            table.AddRow("(c) Size mismatches", match.SizeMismatchCount);
            table.AddRow("(c) Enumeration complete", match.EnumerationComplete ? "true" : "false: " + match.IncompleteReason + "; unlisted names were read directly");
        }

        table.AddRow("(d) BitLocker volumes", report.BitLocker.Volumes.Count);
        foreach (BitLockerVolume volume in report.BitLocker.Volumes)
        {
            table.AddRow("(d) " + volume.DriveLetter, volume.ConversionStatusText + "; protection " + volume.ProtectionStatusText + "; protectors " + Format.Join(volume.ProtectorTypes) + "; " + Format.Text(volume.PcrProfileText, "no TPM profile"));
        }

        table.AddRow("(d) Registry Secure Boot enabled", Format.Bool(report.SecureBootPolicy.RegistryEnabled, "unreadable"));
        table.AddRow("(d) PowerShell Confirm-SecureBootUEFI", Format.Bool(report.SecureBootPolicy.PowerShellConfirm, "not available"));
        table.AddRow("TPM present", report.Tpm.Present);
        table.AddRow("TPM spec version", Format.Text(report.Tpm.SpecVersion, "(unknown)"));
        table.AddRow("Recommendation", "(left for the memo author)");
        table.Write(output);

        if (report.VarstoreMatch is { Entries.Count: > 0 })
        {
            output.WriteLine();
            output.WriteLine("Varstore match:");
            TableWriter varstores = new("Name", "GUID", "IFR size", "Present", "Runtime size", "Flags", "Size matches");
            foreach (GateVarstoreEntry entry in report.VarstoreMatch.Entries)
            {
                varstores.AddRow(
                    entry.Name,
                    entry.Guid,
                    entry.IfrSize,
                    entry.Present switch { true => "yes", false => "no", null => "unknown" },
                    entry.RuntimeSize is null ? "-" : entry.RuntimeSize.Value.ToString(CultureInfo.InvariantCulture),
                    entry.AttributeFlags ?? "-",
                    entry.SizeMatches is null ? "-" : (entry.SizeMatches.Value ? "yes" : "no"));
            }

            varstores.Write(output);
        }

        output.WriteLine();
        output.WriteLine(written ? "Gate report written to " + outPath : "Gate report could not be written to " + outPath);
    }
}
