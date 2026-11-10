// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Serilog;
using UefiAccess.Cli.Output;

namespace UefiAccess.Cli.Services;

/// <summary>The JSON document written when a command fails before producing its normal output.</summary>
/// <param name="Command">Command name ("uefiaccess" when a command line that does not parse names no command).</param>
/// <param name="Ok">Always false.</param>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="Error">The message that was also written to standard error.</param>
/// <param name="FirmwareType">Detected firmware type name, or null when the checks did not run (a command line that does not parse, or a command that threw).</param>
/// <param name="Elevated">Whether the process was elevated, or null when the checks did not run.</param>
/// <param name="PrivilegeState">
/// Privilege state name: null when the firmware type check failed or the checks did not run;
/// "NotElevated" when the process is not elevated (the privilege is not attempted);
/// "ElevatedPrivilegeAbsent" when enabling it failed; "Enabled" when the failure came after the
/// checks passed.
/// </param>
internal sealed record ErrorDocument(
    string Command,
    bool Ok,
    int ExitCode,
    string Error,
    string? FirmwareType,
    bool? Elevated,
    string? PrivilegeState);

/// <summary>Per-invocation state handed to a command body: services, output mode and the precondition result.</summary>
internal sealed class CommandContext
{
    private bool _jsonWritten;

    /// <summary>Creates the context.</summary>
    /// <param name="services">Composed services.</param>
    /// <param name="commandName">Name of the command being run.</param>
    /// <param name="json">True when --json was given.</param>
    /// <param name="preconditions">Result of the shared precondition check.</param>
    public CommandContext(CliServices services, string commandName, bool json, PreconditionResult preconditions)
    {
        Services = services;
        CommandName = commandName;
        Json = json;
        Preconditions = preconditions;
    }

    /// <summary>Composed services.</summary>
    public CliServices Services { get; }

    /// <summary>Name of the command being run.</summary>
    public string CommandName { get; }

    /// <summary>True when --json was given: standard output receives exactly one JSON document.</summary>
    public bool Json { get; }

    /// <summary>Result of the shared precondition check.</summary>
    public PreconditionResult Preconditions { get; }

    /// <summary>Standard output.</summary>
    public TextWriter Out => Services.Out;

    /// <summary>Standard error.</summary>
    public TextWriter Error => Services.Error;

    /// <summary>The run logger.</summary>
    public ILogger Logger => Services.Logger;

    /// <summary>True once a JSON document has been written to standard output.</summary>
    public bool JsonWritten => _jsonWritten;

    /// <summary>Writes the command's single JSON document to standard output.</summary>
    /// <param name="document">The document.</param>
    public void WriteJson(object? document)
    {
        WriteJson(document, JsonOutput.Options);
    }

    /// <summary>Writes the command's single JSON document to standard output with the given serializer settings.</summary>
    /// <param name="document">The document.</param>
    /// <param name="options"><see cref="JsonOutput.Options"/> or <see cref="JsonOutput.KeyPreservingOptions"/>.</param>
    public void WriteJson(object? document, JsonSerializerOptions options)
    {
        _jsonWritten = true;
        JsonOutput.Write(Out, document, options);
    }

    /// <summary>Writes a warning line to standard error and the log.</summary>
    /// <param name="message">The warning.</param>
    public void Warn(string message)
    {
        Logger.Warning("{Warning}", message);
        Error.WriteLine("Warning: " + message);
    }

    /// <summary>Writes an informational diagnostic line to standard error (not to standard output) and the log.</summary>
    /// <param name="message">The message.</param>
    public void Diagnostic(string message)
    {
        Logger.Information("{Diagnostic}", message);
        Error.WriteLine(message);
    }

    /// <summary>
    /// Reports a failure: the message goes to standard error and the log, and in JSON mode an
    /// error document goes to standard output (unless one was already written).
    /// </summary>
    /// <param name="exitCode">The exit code to return.</param>
    /// <param name="message">The failure message.</param>
    /// <returns><paramref name="exitCode"/>, so callers can write "return context.Fail(...)".</returns>
    public int Fail(int exitCode, string message)
    {
        Logger.Error("{Command} failed with exit code {ExitCode}: {Message}", CommandName, exitCode, message);
        Error.WriteLine(message);
        if (Json && !_jsonWritten)
        {
            WriteJson(new ErrorDocument(
                CommandName,
                false,
                exitCode,
                message,
                Preconditions.FirmwareType.ToString(),
                Preconditions.IsElevated,
                Preconditions.Privilege?.ToString()));
        }

        return exitCode;
    }
}
