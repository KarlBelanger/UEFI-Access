// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.CommandLine;
using System.CommandLine.Parsing;
using UefiAccess.Cli.Output;

namespace UefiAccess.Cli.Services;

/// <summary>
/// Runs a command body with the shared plumbing: creates the services, logs the invocation,
/// runs the precondition check, converts every exception into an exit code, and disposes the
/// services. Nothing escapes as an exception.
/// </summary>
internal static class CommandRunner
{
    /// <summary>Runs <paramref name="body"/> for a parsed command line with the production services.</summary>
    /// <param name="parseResult">The parse result handed to the action.</param>
    /// <param name="verboseOption">The global --verbose option.</param>
    /// <param name="jsonOption">The command's --json option.</param>
    /// <param name="commandName">The command name, used in logs and error documents.</param>
    /// <param name="body">The command body; returns the exit code.</param>
    /// <returns>The process exit code.</returns>
    public static int Run(ParseResult parseResult, Option<bool> verboseOption, Option<bool> jsonOption, string commandName, Func<CommandContext, int> body)
    {
        return Run(parseResult, verboseOption, jsonOption, commandName, CliServices.Create, body);
    }

    /// <summary>Runs <paramref name="body"/> for a parsed command line with services from <paramref name="servicesFactory"/>.</summary>
    /// <param name="parseResult">The parse result handed to the action.</param>
    /// <param name="verboseOption">The global --verbose option.</param>
    /// <param name="jsonOption">The command's --json option.</param>
    /// <param name="commandName">The command name, used in logs and error documents.</param>
    /// <param name="servicesFactory">Creates the services for this invocation; receives the --verbose flag.</param>
    /// <param name="body">The command body; returns the exit code.</param>
    /// <returns>The process exit code.</returns>
    public static int Run(
        ParseResult parseResult,
        Option<bool> verboseOption,
        Option<bool> jsonOption,
        string commandName,
        Func<bool, CliServices> servicesFactory,
        Func<CommandContext, int> body)
    {
        bool verbose = GetFlag(parseResult, verboseOption);
        bool json = GetFlag(parseResult, jsonOption);
        CliServices? services = null;
        CommandContext? context = null;
        try
        {
            services = servicesFactory(verbose) ?? throw new InvalidOperationException("The services factory returned no services.");
            services.Logger.Information(
                "uefiaccess {Version} command {Command} started: {Arguments}",
                CliServices.ToolVersion,
                commandName,
                DescribeTokens(parseResult));

            PreconditionResult preconditions = Preconditions.Check(services);
            context = new CommandContext(services, commandName, json, preconditions);
            int exitCode = preconditions.Ok
                ? body(context)
                : context.Fail(preconditions.ExitCode, preconditions.Message ?? Preconditions.NotUefiSentence);

            services.Logger.Information("command {Command} finished with exit code {ExitCode}", commandName, exitCode);
            services.Out.Flush();
            services.Error.Flush();
            return exitCode;
        }
        catch (Exception ex)
        {
            string message = "uefiaccess " + commandName + " failed: " + ex.GetType().Name + ": " + ex.Message;
            try
            {
                services?.Logger.Error(ex, "command {Command} threw", commandName);
                TextWriter error = services?.Error ?? Console.Error;
                error.WriteLine(message);
                error.Flush();
                if (json && (context is null || !context.JsonWritten))
                {
                    JsonOutput.Write(services?.Out ?? Console.Out, new ErrorDocument(commandName, false, ExitCodes.NativeFailure, message, null, null, null));
                }
            }
            catch (Exception)
            {
                // Reporting the failure must not itself fail.
            }

            return ExitCodes.NativeFailure;
        }
        finally
        {
            services?.Dispose();
        }
    }

    /// <summary>Reads a boolean option from the parse result; false when absent or unreadable.</summary>
    /// <param name="parseResult">The parse result.</param>
    /// <param name="option">The option.</param>
    /// <returns>The flag value.</returns>
    public static bool GetFlag(ParseResult parseResult, Option<bool> option)
    {
        try
        {
            return parseResult.GetValue(option);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Reads a string option from the parse result; null when absent or unreadable.</summary>
    /// <param name="parseResult">The parse result.</param>
    /// <param name="option">The option.</param>
    /// <returns>The value, or null.</returns>
    public static string? GetString(ParseResult parseResult, Option<string?> option)
    {
        try
        {
            return parseResult.GetValue(option);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The parsed tokens joined with spaces, for the "command started" log line; never throws.</summary>
    /// <param name="parseResult">The parse result.</param>
    /// <returns>The token text.</returns>
    private static string DescribeTokens(ParseResult parseResult)
    {
        try
        {
            return string.Join(" ", parseResult.Tokens.Select(token => token.Value));
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
