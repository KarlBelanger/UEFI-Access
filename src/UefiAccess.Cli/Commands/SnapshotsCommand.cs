// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.CommandLine;
using UefiAccess.Cli.Output;
using UefiAccess.Cli.Services;
using UefiAccess.Core.Safety;

namespace UefiAccess.Cli.Commands;

/// <summary>The JSON document of the snapshots command.</summary>
/// <param name="Command">"snapshots".</param>
/// <param name="Ok">Always true: a missing root lists no folder.</param>
/// <param name="Root">The full path of the snapshot root.</param>
/// <param name="Count">The number of folders listed.</param>
/// <param name="Snapshots">Every folder under the root, newest first, with what its record.json says.</param>
internal sealed record SnapshotsDocument(string Command, bool Ok, string Root, int Count, IReadOnlyList<SnapshotSummary> Snapshots);

/// <summary>
/// "uefiaccess snapshots": lists the folders under the snapshot root,
/// newest first, with the operation, the time, the target and the outcome their record.json
/// holds. A folder whose record.json is missing or unreadable is listed with those fields empty.
/// Reads only; the root is not created.
/// </summary>
internal static class SnapshotsCommand
{
    /// <summary>The command name, and the command of the document.</summary>
    internal const string CommandName = "snapshots";

    /// <summary>The line when the root holds no folder; {0} the root.</summary>
    internal const string NoSnapshotsFormat = "No snapshot folders under {0}.";

    /// <summary>The last line; {0} the number of folders, {1} the root.</summary>
    internal const string CountFormat = "{0} snapshot folders under {1}.";

    /// <summary>The target and outcome cells of a folder whose record.json could not be read.</summary>
    internal const string NoRecordText = "(no record)";

    private const string Description = "List the snapshot folders of the write pipeline, newest first, with operation, time, target and outcome.";
    private const string FolderHeader = "Folder";
    private const string OperationHeader = "Operation";
    private const string TimeHeader = "Time (UTC)";
    private const string TargetHeader = "Target";
    private const string OutcomeHeader = "Outcome";
    private const char KeySeparator = ':';

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
        Command command = new(CommandName, Description);
        command.Add(json);
        command.SetAction(parseResult => CommandRunner.Run(
            parseResult,
            verbose,
            json,
            CommandName,
            servicesFactory,
            Execute));
        return command;
    }

    /// <summary>Lists the folders under the default snapshot root as a table or the document.</summary>
    private static int Execute(CommandContext context)
    {
        SnapshotStore store = context.Services.CreateSnapshotStore(null);
        IReadOnlyList<SnapshotSummary> snapshots = store.List();
        context.Logger.Information("snapshots: {Count} folders under {Root}", snapshots.Count, store.RootDirectory);
        if (context.Json)
        {
            context.WriteJson(new SnapshotsDocument(CommandName, true, store.RootDirectory, snapshots.Count, snapshots));
            return ExitCodes.Success;
        }

        if (snapshots.Count == 0)
        {
            context.Out.WriteLine(CheckSentences.Format(NoSnapshotsFormat, store.RootDirectory));
            return ExitCodes.Success;
        }

        TableWriter table = new(FolderHeader, OperationHeader, TimeHeader, TargetHeader, OutcomeHeader);
        foreach (SnapshotSummary snapshot in snapshots)
        {
            table.AddRow(
                snapshot.Name,
                Format.Text(snapshot.Operation, NoRecordText),
                Format.Text(snapshot.TimestampUtc, NoRecordText),
                TargetName(snapshot.TargetKey),
                snapshot.Outcome?.ToString() ?? NoRecordText);
        }

        table.Write(context.Out);
        context.Out.WriteLine();
        context.Out.WriteLine(CheckSentences.Format(CountFormat, snapshots.Count, store.RootDirectory));
        return ExitCodes.Success;
    }

    /// <summary>The variable name of a key "guid:name", or the placeholder when the record named none.</summary>
    private static string TargetName(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return NoRecordText;
        }

        int separator = key.LastIndexOf(KeySeparator);
        return separator < 0 ? key : key[(separator + 1)..];
    }
}
