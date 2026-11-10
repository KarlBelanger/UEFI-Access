// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.CommandLine;
using UefiAccess.Cli.Output;
using UefiAccess.Cli.Services;
using UefiAccess.Core.Platform;

namespace UefiAccess.Cli.Commands;

/// <summary>The JSON document of the info command.</summary>
/// <param name="Command">"info".</param>
/// <param name="Ok">True when the preconditions held.</param>
/// <param name="ToolVersion">Tool version.</param>
/// <param name="FirmwareType">Detected firmware type name.</param>
/// <param name="Elevated">True when the process is elevated.</param>
/// <param name="PrivilegeState">Privilege state name.</param>
/// <param name="Identity">Machine identity with the expected-model check.</param>
/// <param name="Os">Operating system caption, version and build on one line.</param>
internal sealed record InfoDocument(
    string Command,
    bool Ok,
    string ToolVersion,
    string FirmwareType,
    bool Elevated,
    string PrivilegeState,
    MachineIdentityResult Identity,
    string Os);

/// <summary>
/// "uefiaccess info": firmware type, elevation and privilege state, machine identity, the
/// expected-model confirmation for the 2014 OptiPlex 7020, and the OS version.
/// </summary>
internal static class InfoCommand
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
        Command command = new("info", "Show firmware type, elevation and privilege state, machine identity, the expected-model check and the OS version.");
        command.Add(json);
        command.SetAction(parseResult => CommandRunner.Run(parseResult, verbose, json, "info", servicesFactory, Execute));
        return command;
    }

    private static int Execute(CommandContext context)
    {
        PreconditionResult pre = context.Preconditions;
        MachineIdentityResult identity = SafeInspect.Identity(context.Services);
        string os = string.Join(" ", new[] { identity.OsCaption, identity.OsVersion, string.IsNullOrWhiteSpace(identity.OsBuild) ? null : "build " + identity.OsBuild }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
        string privilege = (pre.Privilege ?? Core.Privileges.PrivilegeState.NotElevated).ToString();

        foreach (string error in identity.Errors)
        {
            context.Warn("machine identity: " + error);
        }

        if (context.Json)
        {
            context.WriteJson(new InfoDocument("info", true, CliServices.ToolVersion, pre.FirmwareType.ToString(), pre.IsElevated, privilege, identity, os));
        }
        else
        {
            TableWriter table = new("Field", "Value");
            table.AddRow("Tool version", CliServices.ToolVersion);
            table.AddRow("Firmware type", pre.FirmwareType.ToString());
            table.AddRow("Elevated", pre.IsElevated);
            table.AddRow("Privilege state", privilege);
            table.AddRow("Manufacturer", Format.Text(identity.Manufacturer, "(unknown)"));
            table.AddRow("Model", Format.Text(identity.Model, "(unknown)"));
            table.AddRow("System SKU", Format.Text(identity.SystemSku, "(unknown)"));
            table.AddRow("BIOS version", Format.Text(identity.BiosVersion, "(unknown)"));
            table.AddRow("BIOS release date", Format.Text(identity.BiosReleaseDate, "(unknown)"));
            table.AddRow("Service tag", Format.Text(identity.ServiceTag, "(unknown)"));
            table.AddRow("Base board", Format.Text(identity.BaseBoard, "(unknown)"));
            table.AddRow("CPU", Format.Text(identity.Cpu, "(unknown)"));
            table.AddRow("OS", Format.Text(identity.OsCaption, "(unknown)"));
            table.AddRow("OS version", Format.Text(identity.OsVersion, "(unknown)"));
            table.AddRow("OS build", Format.Text(identity.OsBuild, "(unknown)"));
            table.AddRow("Expected model confirmed", identity.ExpectedModelConfirmed);
            table.AddRow("Expected model notes", Format.Text(identity.ExpectedModelNotes, "(none)"));
            table.Write(context.Out);
        }

        return identity.Errors.Count > 0 ? ExitCodes.Partial : ExitCodes.Success;
    }
}
