// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.CommandLine;
using System.CommandLine.Parsing;
using UefiAccess.Cli.Commands;
using UefiAccess.Cli.Output;
using UefiAccess.Cli.Services;

namespace UefiAccess.Cli;

/// <summary>
/// Entry point of the uefiaccess command-line tool: builds the System.CommandLine root command
/// with the eight read-only commands (info, list, read, boot, secureboot, dell, baseline, gate), the
/// write harness (write, with bootorder, bootnext, restore, check and log) and snapshots, and the
/// global --verbose option, and maps parse errors to exit code 2 (with --json on the command
/// line, also to the one error document on standard output). <see cref="Run"/> takes the
/// services factory and the output writers so the tests drive the same code path with an
/// in-memory store, fake inspectors and captured output. <see cref="Main"/> first makes the
/// standard streams lossless (<see cref="StandardStreams"/>).
/// </summary>
internal static class Program
{
    /// <summary>The name used in the error document when the command line names no command.</summary>
    internal const string ToolName = "uefiaccess";

    private const string JsonToken = "--json";

    /// <summary>The root command's description: what the tool does, the write safeguards in short, and the exit codes 0 to 9.</summary>
    private const string RootDescription =
        "Access to UEFI firmware variables from Windows. Every command only reads, except write, which changes BootOrder (reorder) and BootNext alone: "
        + "a dry run unless --confirm is given, then one write call after a snapshot, followed by a verification read, never retried. "
        + "Exit codes: 0 success, 2 usage error, 3 not UEFI, 4 not elevated or privilege missing, "
        + "5 native call failed, 6 partial (some variables unreadable), 7 blocked by a safeguard (nothing written), "
        + "8 the firmware refused the write (nothing changed), 9 the value read back is not the expected one (restore advised).";

    private static int Main(string[] args)
    {
        StandardStreams.Configure();
        return Run(args, CliServices.Create, Console.Out, Console.Error);
    }

    /// <summary>Parses and runs one command line.</summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="servicesFactory">Creates the services for the invocation; receives the --verbose flag.</param>
    /// <param name="output">Standard output (tables, JSON documents, help).</param>
    /// <param name="error">Standard error (parse errors, precondition sentences, warnings).</param>
    /// <returns>The process exit code.</returns>
    internal static int Run(string[] args, Func<bool, CliServices> servicesFactory, TextWriter output, TextWriter error)
    {
        try
        {
            RootCommand root = BuildRootCommand(servicesFactory);
            string[] tokens = args ?? Array.Empty<string>();
            ParseResult result = root.Parse(tokens);
            if (result.Errors.Count > 0)
            {
                foreach (ParseError parseError in result.Errors)
                {
                    error.WriteLine(parseError.Message);
                }

                error.WriteLine("Run 'uefiaccess --help' for usage.");
                error.Flush();
                if (tokens.Contains(JsonToken, StringComparer.Ordinal))
                {
                    // --json promises exactly one document on standard output, failures included.
                    string command = ErrorDocumentCommand(result.CommandResult);
                    string message = string.Join(" ", result.Errors.Select(parseError => parseError.Message));
                    JsonOutput.Write(output, new ErrorDocument(command, false, ExitCodes.Usage, message, null, null, null));
                }

                return ExitCodes.Usage;
            }

            InvocationConfiguration configuration = new()
            {
                Output = output,
                Error = error,
            };
            int exitCode = result.Invoke(configuration);
            output.Flush();
            error.Flush();
            return exitCode;
        }
        catch (Exception ex)
        {
            try
            {
                error.WriteLine("uefiaccess failed: " + ex.GetType().Name + ": " + ex.Message);
                error.Flush();
            }
            catch (Exception)
            {
                // Reporting the failure must not itself fail.
            }

            return ExitCodes.NativeFailure;
        }
    }

    /// <summary>
    /// The command of the error document for a command line that does not parse: the name the
    /// command's own documents carry, so a parse error and a failed precondition name it the same
    /// way ("write" for write's bootorder, bootnext, restore and check, "write log" for its log,
    /// the command's name for every other command), or the tool's name when no command was recognised.
    /// </summary>
    /// <param name="result">The command the parser stopped in.</param>
    /// <returns>The command name.</returns>
    private static string ErrorDocumentCommand(CommandResult result)
    {
        if (result.Command is RootCommand)
        {
            return ToolName;
        }

        return WriteCommand.DocumentCommandOf(result) ?? result.Command.Name;
    }

    /// <summary>Builds the root command with every subcommand attached to the production services.</summary>
    /// <returns>The root command.</returns>
    internal static RootCommand BuildRootCommand()
    {
        return BuildRootCommand(CliServices.Create);
    }

    /// <summary>Builds the root command with every subcommand attached to <paramref name="servicesFactory"/>.</summary>
    /// <param name="servicesFactory">Creates the services for an invocation; receives the --verbose flag.</param>
    /// <returns>The root command.</returns>
    internal static RootCommand BuildRootCommand(Func<bool, CliServices> servicesFactory)
    {
        ArgumentNullException.ThrowIfNull(servicesFactory);
        Option<bool> verbose = new("--verbose", "-v")
        {
            Description = "Mirror the log (every native call with its result and timing) to standard error.",
            Recursive = true,
        };

        RootCommand root = new(RootDescription);
        root.Add(verbose);
        root.Add(InfoCommand.Create(verbose, servicesFactory));
        root.Add(ListCommand.Create(verbose, servicesFactory));
        root.Add(ReadCommand.Create(verbose, servicesFactory));
        root.Add(BootCommand.Create(verbose, servicesFactory));
        root.Add(SecureBootCommand.Create(verbose, servicesFactory));
        root.Add(DellCommand.Create(verbose, servicesFactory));
        root.Add(BaselineCommand.Create(verbose, servicesFactory));
        root.Add(GateCommand.Create(verbose, servicesFactory));
        root.Add(WriteCommand.Create(verbose, servicesFactory));
        root.Add(SnapshotsCommand.Create(verbose, servicesFactory));
        return root;
    }
}
