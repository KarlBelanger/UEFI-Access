// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.CommandLine;
using System.Globalization;
using UefiAccess.Cli.Output;
using UefiAccess.Cli.Services;
using UefiAccess.Core.Decoders;
using UefiAccess.Core.Platform;

namespace UefiAccess.Cli.Commands;

/// <summary>The JSON document of the secureboot command.</summary>
/// <param name="Command">"secureboot".</param>
/// <param name="Ok">True when variables were enumerated.</param>
/// <param name="Enumeration">Enumeration summary.</param>
/// <param name="State">Secure Boot state decoded from the variables.</param>
/// <param name="Policy">Registry, variable and PowerShell view from the policy inspector.</param>
/// <param name="SourcesAgree">True when every available source reports the same enabled state.</param>
/// <param name="Warnings">Observations, for example a disagreement between the decoder and the inspector.</param>
internal sealed record SecureBootDocument(
    string Command,
    bool Ok,
    EnumerationSummary Enumeration,
    SecureBootState State,
    SecureBootPolicyResult Policy,
    bool SourcesAgree,
    IReadOnlyList<string> Warnings);

/// <summary>
/// "uefiaccess secureboot": Secure Boot state from the variables (SecureBoot, SetupMode,
/// AuditMode, DeployedMode, PK, KEK, db, dbx, dbt), the registry and PowerShell, with the
/// agreement flag.
/// </summary>
internal static class SecureBootCommand
{
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
        Command command = new("secureboot", "Show Secure Boot state from the variables, the registry and PowerShell, with the agreement flag.");
        command.Add(json);
        command.SetAction(parseResult => CommandRunner.Run(parseResult, verbose, json, "secureboot", servicesFactory, Execute));
        return command;
    }

    private static int Execute(CommandContext context)
    {
        VariableSnapshot snapshot = VariableSnapshot.Load(context.Services);
        if (snapshot.Failed)
        {
            return context.Fail(ExitCodes.NativeFailure, snapshot.FailureMessage);
        }

        snapshot.ReportPartial(context);
        SecureBootState state = SafeInspect.SecureBootState(context.Services, snapshot.Set);
        SecureBootPolicyResult policy = SafeInspect.SecureBootPolicy(context.Services, runPowerShellCrossCheck: true);

        List<string> warnings = new();
        if (state.SecureBoot is not null && policy.VariableSecureBoot is not null && state.SecureBoot != policy.VariableSecureBoot)
        {
            warnings.Add("The SecureBoot variable decoded as " + Format.Bool(state.SecureBoot) + " but the policy inspector read " + Format.Bool(policy.VariableSecureBoot) + ".");
        }

        foreach (string error in policy.Errors)
        {
            warnings.Add("policy: " + error);
        }

        foreach (string warning in warnings)
        {
            context.Warn(warning);
        }

        SecureBootDocument document = new("secureboot", true, snapshot.ToSummary(), state, policy, policy.SourcesAgree, warnings);
        if (context.Json)
        {
            context.WriteJson(document);
        }
        else
        {
            WriteText(context.Out, document);
        }

        if (snapshot.ExitCode != ExitCodes.Success)
        {
            return snapshot.ExitCode;
        }

        return policy.Errors.Count > 0 ? ExitCodes.Partial : ExitCodes.Success;
    }

    private static void WriteText(TextWriter output, SecureBootDocument document)
    {
        SecureBootState state = document.State;
        SecureBootPolicyResult policy = document.Policy;

        output.WriteLine("Secure Boot state from the variables:");
        TableWriter variables = new("Variable", "Value");
        variables.AddRow("SecureBoot", Mode(state.SecureBoot, "enabled", "disabled"));
        variables.AddRow("SetupMode", SetupModeText(state));
        variables.AddRow("AuditMode", Mode(state.AuditMode, "audit mode", "not audit mode"));
        variables.AddRow("DeployedMode", Mode(state.DeployedMode, "deployed mode", "not deployed mode"));
        variables.AddRow("PK", state.PkPresent ? "present, " + Format.Bytes(state.PkSize) : "absent");
        variables.AddRow("KEK", state.KekPresent ? "present, " + Format.Bytes(state.KekSize) : "absent");
        variables.AddRow("db", Database(state.Db));
        variables.AddRow("dbx", Database(state.Dbx));
        variables.AddRow("dbt", Database(state.Dbt));
        variables.Write(output);

        output.WriteLine();
        output.WriteLine("Secure Boot policy sources:");
        TableWriter sources = new("Source", "Value");
        sources.AddRow("Registry UEFISecureBootEnabled", Format.Bool(policy.RegistryEnabled, "unreadable"));
        sources.AddRow("Variable SecureBoot", Format.Bool(policy.VariableSecureBoot, "unreadable"));
        sources.AddRow("Variable SetupMode", Format.Bool(policy.VariableSetupMode, "unreadable"));
        sources.AddRow("Variable DeployedMode", Format.Bool(policy.VariableDeployedMode, "absent"));
        sources.AddRow("Variable AuditMode", Format.Bool(policy.VariableAuditMode, "absent"));
        sources.AddRow("PowerShell Confirm-SecureBootUEFI", Format.Bool(policy.PowerShellConfirm, "not available"));
        sources.AddRow("Secure Boot capable", policy.Capable);
        sources.AddRow("Sources agree", document.SourcesAgree);
        sources.Write(output);
    }

    private static string Mode(bool? value, string whenTrue, string whenFalse)
    {
        return value is null ? "absent" : (value.Value ? whenTrue + " (1)" : whenFalse + " (0)");
    }

    // SetupMode is worded with the mode the three mode variables imply (UEFI 2.10 section 32.3),
    // the same words the decoder and the application use: SetupMode=0 with DeployedMode=1 is
    // deployed mode, not user mode.
    private static string SetupModeText(SecureBootState state)
    {
        if (state.SetupMode is null)
        {
            return "absent";
        }

        string mode = SecureBootStateReader.DescribeMode(SecureBootStateReader.DeriveMode(state));
        return mode + (state.SetupMode.Value ? " (1)" : " (0)");
    }

    private static string Database(SignatureDbInfo? info)
    {
        if (info is null)
        {
            return "absent";
        }

        string text = Format.Bytes(info.Size)
            + ", " + info.ListCount.ToString(CultureInfo.InvariantCulture) + (info.ListCount == 1 ? " signature list" : " signature lists")
            + ", " + info.SignatureCount.ToString(CultureInfo.InvariantCulture) + (info.SignatureCount == 1 ? " signature" : " signatures");
        if (info.SignatureTypes is { Count: > 0 })
        {
            text += " (" + string.Join(", ", info.SignatureTypes.Distinct(StringComparer.Ordinal)) + ")";
        }

        if (!string.IsNullOrEmpty(info.Error))
        {
            text += "; error: " + info.Error;
        }

        return text;
    }
}
