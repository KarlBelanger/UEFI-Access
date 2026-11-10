// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Text;
using Serilog;
using UefiAccess.Core.Native;

namespace UefiAccess.Core.Platform;

/// <summary>Outcome of running a child process through <see cref="ProcessRunner"/>.</summary>
/// <param name="Started">True when the process was launched.</param>
/// <param name="ExitCode">Exit code when the process ended; null when it did not start or was killed before reporting one.</param>
/// <param name="StandardOutput">Trimmed standard output (empty when none).</param>
/// <param name="StandardError">Trimmed standard error (empty when none).</param>
/// <param name="TimedOut">True when the timeout elapsed and the process was killed.</param>
/// <param name="Error">One-line description when the process could not be run; otherwise null.</param>
/// <param name="ElapsedMilliseconds">Wall-clock time spent.</param>
internal sealed record ProcessRunResult(
    bool Started,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    string? Error,
    double ElapsedMilliseconds);

/// <summary>
/// Runs a read-only command-line tool with a timeout, capturing both output streams. Never throws
/// and never shows a window; the process tree is killed when the timeout elapses.
/// </summary>
/// <remarks>
/// A child started without a window gets a console of its own, which starts with the system's OEM
/// code page, not the code page of this process's console (which "chcp 65001" or a UTF-8 console
/// setting may have changed, and which the WPF application does not have at all). Both streams are
/// therefore decoded with an explicit encoding, the OEM code page unless the caller knows the child
/// writes something else; the parent's console encoding is never used.
/// </remarks>
internal static class ProcessRunner
{
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Runs an executable and waits for it to finish or for the timeout to elapse. Both output
    /// streams are decoded with <see cref="ChildConsoleEncoding"/>. A <paramref name="fileName"/>
    /// that is not a full path is refused without starting anything, as in the overload below.
    /// </summary>
    /// <param name="fileName">Absolute executable path. A bare name would make Windows search the current directory and PATH, so callers never pass one, and one that is passed is refused.</param>
    /// <param name="workingDirectory">Working directory, or null for the current one.</param>
    /// <param name="timeout">Maximum wall-clock time before the process is killed.</param>
    /// <param name="logger">Logger for the call trace.</param>
    /// <param name="arguments">Arguments passed individually (no shell quoting).</param>
    /// <returns>The run result; never throws.</returns>
    public static ProcessRunResult Run(string fileName, string? workingDirectory, TimeSpan timeout, ILogger logger, params string[] arguments)
    {
        return Run(fileName, workingDirectory, timeout, null, logger, arguments);
    }

    /// <summary>
    /// Runs an executable and waits for it to finish or for the timeout to elapse. A
    /// <paramref name="fileName"/> that is not a full path is refused without starting anything
    /// (<see cref="ProcessRunResult.Started"/> false, <see cref="ProcessRunResult.Error"/> says
    /// why): Windows would look a bare name up in the current directory and on PATH, and resolve
    /// a relative path against the current directory, so an elevated run could start a program
    /// that someone else put there.
    /// </summary>
    /// <param name="fileName">Absolute executable path. A bare name would make Windows search the current directory and PATH, so callers never pass one, and one that is passed is refused.</param>
    /// <param name="workingDirectory">Working directory, or null for the current one.</param>
    /// <param name="timeout">Maximum wall-clock time before the process is killed.</param>
    /// <param name="outputEncoding">The encoding the child writes both streams in, or null for <see cref="ChildConsoleEncoding"/>.</param>
    /// <param name="logger">Logger for the call trace.</param>
    /// <param name="arguments">Arguments passed individually (no shell quoting).</param>
    /// <returns>The run result; never throws.</returns>
    public static ProcessRunResult Run(string fileName, string? workingDirectory, TimeSpan timeout, Encoding? outputEncoding, ILogger logger, params string[] arguments)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        string commandLine = fileName + (arguments.Length == 0 ? string.Empty : " " + string.Join(" ", arguments));
        if (!IsFullPath(fileName))
        {
            string refusal = NotAFullPathMessage(fileName);
            logger.Warning("Process {CommandLine} not started: {Detail}", commandLine, refusal);
            return new ProcessRunResult(false, null, string.Empty, string.Empty, false, refusal, stopwatch.Elapsed.TotalMilliseconds);
        }

        try
        {
            Encoding encoding = outputEncoding ?? ChildConsoleEncoding();
            ProcessStartInfo startInfo = new(fileName)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = false,
                CreateNoWindow = true,
                StandardOutputEncoding = encoding,
                StandardErrorEncoding = encoding,
            };
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            if (!string.IsNullOrEmpty(workingDirectory))
            {
                startInfo.WorkingDirectory = workingDirectory;
            }

            using Process process = new() { StartInfo = startInfo };
            if (!process.Start())
            {
                logger.Warning("Process {CommandLine} did not start", commandLine);
                return new ProcessRunResult(false, null, string.Empty, string.Empty, false, "process did not start", stopwatch.Elapsed.TotalMilliseconds);
            }

            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
            Task<string> standardError = process.StandardError.ReadToEndAsync();

            bool timedOut = false;
            int timeoutMilliseconds = (int)Math.Clamp(timeout.TotalMilliseconds, 1, int.MaxValue - 1);
            if (process.WaitForExit(timeoutMilliseconds))
            {
                // A second wait without timeout lets the redirected streams drain.
                process.WaitForExit();
            }
            else
            {
                timedOut = true;
                TryKill(process, logger, commandLine);
            }

            try
            {
                Task.WaitAll(new Task[] { standardOutput, standardError }, DrainTimeout);
            }
            catch (AggregateException ex)
            {
                logger.Debug("Process {CommandLine}: reading redirected output faulted: {Detail}", commandLine, ex.InnerException?.Message ?? ex.Message);
            }

            string output = standardOutput.IsCompletedSuccessfully ? standardOutput.Result.Trim() : string.Empty;
            string error = standardError.IsCompletedSuccessfully ? standardError.Result.Trim() : string.Empty;
            int? exitCode = TryGetExitCode(process);

            logger.Debug(
                "Process {CommandLine} -> exit {ExitCode}, timed out {TimedOut}, {OutputLength} chars out, {ErrorLength} chars err, {Elapsed:F1} ms",
                commandLine,
                exitCode,
                timedOut,
                output.Length,
                error.Length,
                stopwatch.Elapsed.TotalMilliseconds);

            return new ProcessRunResult(true, exitCode, output, error, timedOut, null, stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            string detail = $"{ex.GetType().Name}: {ex.Message}";
            logger.Warning("Process {CommandLine} failed: {Detail}, {Elapsed:F1} ms", commandLine, detail, stopwatch.Elapsed.TotalMilliseconds);
            return new ProcessRunResult(false, null, string.Empty, string.Empty, false, detail, stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    /// <summary>
    /// The encoding a console child started without a window writes by default: the system's OEM
    /// code page (<c>GetOEMCP</c>), which is what its new console starts with. UTF-8 off Windows or
    /// when that code page is not available.
    /// </summary>
    /// <returns>The encoding; never null.</returns>
    internal static Encoding ChildConsoleEncoding()
    {
        int codePage = 0;
        if (OperatingSystem.IsWindows())
        {
            try
            {
                codePage = (int)Kernel32.GetOEMCP();
            }
            catch (Exception)
            {
                codePage = 0;
            }
        }

        return EncodingForCodePage(codePage);
    }

    /// <summary>
    /// Returns the encoding for a Windows code page, including the OEM and ANSI code pages that
    /// .NET provides only through <see cref="CodePagesEncodingProvider"/>. UTF-8 (without a byte
    /// order mark) when the code page is zero, negative or unknown. Never throws.
    /// </summary>
    /// <param name="codePage">The code page identifier, for example 437, 850 or 65001.</param>
    /// <returns>The encoding.</returns>
    internal static Encoding EncodingForCodePage(int codePage)
    {
        if (codePage > 0)
        {
            try
            {
                return CodePagesEncodingProvider.Instance.GetEncoding(codePage) ?? Encoding.GetEncoding(codePage);
            }
            catch (Exception)
            {
                // Unknown code page: fall through to UTF-8.
            }
        }

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }

    /// <summary>The <see cref="ProcessRunResult.Error"/> of a run refused because its executable is not named by a full path.</summary>
    /// <param name="fileName">The executable as the caller named it.</param>
    /// <returns>The one-line reason.</returns>
    internal static string NotAFullPathMessage(string? fileName) =>
        "'" + fileName + "' is not a full path, so it was not started";

    /// <summary>True when <paramref name="fileName"/> is a fully qualified path (a drive letter and a backslash, or a UNC or device path). Never throws.</summary>
    private static bool IsFullPath(string? fileName)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(fileName) && Path.IsPathFullyQualified(fileName);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void TryKill(Process process, ILogger logger, string commandLine)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(2000);
            logger.Warning("Process {CommandLine} timed out and was killed", commandLine);
        }
        catch (Exception ex)
        {
            logger.Warning("Process {CommandLine} timed out and could not be killed: {Detail}", commandLine, ex.Message);
        }
    }

    private static int? TryGetExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
