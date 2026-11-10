// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;

namespace UefiAccess.Cli.Output;

/// <summary>
/// Makes standard output and standard error lossless. By default .NET writes to the console in
/// its OEM code page (437 or 850), which turns every character outside that page into "?", and
/// writes the same code page into a redirected file. <see cref="Configure"/> changes that:
/// a stream that is redirected (to a file or a pipe) gets UTF-8 without a byte order mark, so
/// "uefiaccess list --json &gt; list.json" in cmd.exe or Start-Process -RedirectStandardOutput
/// yields a UTF-8 JSON file; a stream that is a real console is written with WriteConsoleW
/// (UTF-16), which shows every character and leaves the console's code page unchanged, so
/// programs started later from the same console are not affected.
/// </summary>
internal static class StandardStreams
{
    /// <summary>UTF-8 without a byte order mark, as RFC 8259 asks for JSON.</summary>
    public static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Replaces <see cref="Console.Out"/> and <see cref="Console.Error"/> as described on the
    /// class. Never throws: on any failure the default writers stay in place.
    /// </summary>
    public static void Configure()
    {
        try
        {
            bool outputRedirected = Console.IsOutputRedirected;
            bool errorRedirected = Console.IsErrorRedirected;
            if (!outputRedirected || !errorRedirected)
            {
                // UTF-16 is the one encoding for which .NET calls WriteConsoleW and does not call
                // SetConsoleOutputCP, so the shared console keeps its code page.
                Console.OutputEncoding = Encoding.Unicode;
            }

            if (outputRedirected)
            {
                Console.SetOut(CreateRedirectedWriter(Console.OpenStandardOutput()));
            }

            if (errorRedirected)
            {
                Console.SetError(CreateRedirectedWriter(Console.OpenStandardError()));
            }
        }
        catch (Exception)
        {
            // Output in the console's code page is better than no output.
        }
    }

    /// <summary>A writer for a redirected standard stream: UTF-8 without a byte order mark, flushed after every write.</summary>
    /// <param name="stream">The standard output or standard error stream.</param>
    /// <returns>The writer; it does not close the stream.</returns>
    public static TextWriter CreateRedirectedWriter(Stream stream)
    {
        return new StreamWriter(stream, Utf8NoBom, bufferSize: 4096, leaveOpen: true) { AutoFlush = true };
    }
}
