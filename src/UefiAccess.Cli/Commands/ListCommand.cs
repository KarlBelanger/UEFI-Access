// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.CommandLine;
using UefiAccess.Cli.Output;
using UefiAccess.Cli.Services;
using UefiAccess.Core.Variables;

namespace UefiAccess.Cli.Commands;

/// <summary>One variable in the list command's JSON document (data is not included).</summary>
/// <param name="Key">Variable key ("guid:name").</param>
/// <param name="Name">Variable name.</param>
/// <param name="Guid">Vendor GUID (lowercase).</param>
/// <param name="Attributes">Raw attribute value.</param>
/// <param name="Flags">Short flags such as "NV BS RT".</param>
/// <param name="AttributeNames">Full attribute names.</param>
/// <param name="Size">Data size in bytes.</param>
internal sealed record ListEntry(
    string Key,
    string Name,
    string Guid,
    uint Attributes,
    string Flags,
    IReadOnlyList<string> AttributeNames,
    int Size);

/// <summary>The JSON document of the list command.</summary>
/// <param name="Command">"list".</param>
/// <param name="Ok">True when variables were enumerated.</param>
/// <param name="Enumeration">Enumeration summary.</param>
/// <param name="Filter">The --filter text, or null.</param>
/// <param name="Guid">The --guid value, or null.</param>
/// <param name="Listed">Number of variables after filtering.</param>
/// <param name="Variables">The variables after filtering, sorted by key.</param>
internal sealed record ListDocument(
    string Command,
    bool Ok,
    EnumerationSummary Enumeration,
    string? Filter,
    string? Guid,
    int Listed,
    IReadOnlyList<ListEntry> Variables);

/// <summary>"uefiaccess list": every runtime-visible variable as key, attribute flags and size.</summary>
internal static class ListCommand
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
        Option<string?> filter = new("--filter", "-f")
        {
            Description = "Only show variables whose key (guid:name) contains this text, case-insensitive.",
            HelpName = "text",
        };
        Option<string?> guid = CommonOptions.Guid("Only show variables with this vendor GUID.");
        Command command = new("list", "List every runtime-accessible variable: key, attribute flags (NV BS RT HR AW TA AP EA) and size.");
        command.Add(json);
        command.Add(filter);
        command.Add(guid);
        command.SetAction(parseResult => CommandRunner.Run(
            parseResult,
            verbose,
            json,
            "list",
            servicesFactory,
            context => Execute(context, CommandRunner.GetString(parseResult, filter), CommandRunner.GetString(parseResult, guid))));
        return command;
    }

    private static int Execute(CommandContext context, string? filter, string? guidText)
    {
        Guid? guidFilter = null;
        if (guidText is not null)
        {
            if (!Format.TryParseGuid(guidText, out Guid parsed))
            {
                return context.Fail(ExitCodes.Usage, "The value '" + guidText + "' given to --guid is not a GUID.");
            }

            guidFilter = parsed;
        }

        VariableSnapshot snapshot = VariableSnapshot.Load(context.Services);
        if (snapshot.Failed)
        {
            return context.Fail(ExitCodes.NativeFailure, snapshot.FailureMessage);
        }

        snapshot.ReportPartial(context);

        IEnumerable<FirmwareVariable> query = snapshot.Set.All;
        if (guidFilter is not null)
        {
            query = query.Where(v => v.VendorGuid == guidFilter.Value);
        }

        if (!string.IsNullOrEmpty(filter))
        {
            query = query.Where(v => v.Key.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        List<FirmwareVariable> rows = query.ToList();

        if (context.Json)
        {
            List<ListEntry> entries = rows
                .Select(v => new ListEntry(v.Key, v.Name, v.VendorGuid.ToString("D"), (uint)v.Attributes, Format.ShortFlags(v.Attributes), Format.AttributeNames(v.Attributes), v.Size))
                .ToList();
            context.WriteJson(new ListDocument("list", true, snapshot.ToSummary(), filter, guidFilter?.ToString("D"), entries.Count, entries));
        }
        else
        {
            TableWriter table = new("Key", "Flags", "Size");
            foreach (FirmwareVariable variable in rows)
            {
                table.AddRow(variable.Key, Format.ShortFlags(variable.Attributes), variable.Size);
            }

            table.Write(context.Out);
            context.Out.WriteLine();
            context.Out.WriteLine(
                rows.Count + " of " + snapshot.Set.Count + " variables listed (method " + snapshot.Enumeration.Method + ", " + snapshot.Enumeration.EntriesParsed + " entries parsed).");
        }

        return snapshot.ExitCode;
    }
}
