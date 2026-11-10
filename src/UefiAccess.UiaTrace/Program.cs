// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.IO;
using Serilog;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Privileges;
using UefiAccess.UiaTrace.Recording;

namespace UefiAccess.UiaTrace;

/// <summary>
/// Entry point of uefiaccess-uia-trace, the recorder of the UI Automation events the UefiAccess
/// window raises (Alpha 2 finding A2-7, A1-6 continued): a tester starts it from an elevated prompt,
/// then starts UefiAccess (with --trace-input for the application's own trace), moves through the
/// list, and closes UefiAccess; the recorder then writes its summary and stops. It reads only:
/// it sends no input, records no key presses, sets no focus and changes nothing in UefiAccess or
/// the firmware. Its own file is the trace, uia-trace-&lt;yyyyMMdd-HHmmss&gt;.log in the UefiAccess
/// log folder or the full path given with --out (<see cref="TraceOutput"/>), in the same line
/// format as the application's trace-input file (<see cref="TraceLine"/>), so the two merge on the
/// QPC column; the Filter box's text is in it, each time it changes, and each LiveRegionChanged
/// line gives the Status sentence the event stream showed before it and whether the event was a
/// valueless copy (<see cref="ElementValues"/>). Like the command-line tool, its run is also
/// logged: its start and stop lines (never the service tag) go to the day's UefiAccess log,
/// uefiaccess-&lt;yyyyMMdd&gt;.log (the shared log folder, or the per-user one when Logging moves
/// the log there), a run refused for want of elevation included. This unit's service tag is read
/// from WMI into memory and replaced by "[service tag]" in every line and every console message
/// (<see cref="Say"/>).
/// </summary>
internal static class Program
{
    /// <summary>The sentence that refuses a run without elevation.</summary>
    internal const string NotElevatedMessage =
        "Run uefiaccess-uia-trace from an elevated prompt (Run as administrator): UefiAccess runs elevated, "
        + "and a program that is not elevated receives none of its UI Automation events. Nothing was recorded.";

    /// <summary>The open trace file, once there is one: <see cref="Say"/> redacts with it.</summary>
    private static TraceFileWriter? _writer;

    [MTAThread]
    private static int Main(string[] args)
    {
        RecorderOptions options = RecorderOptions.Parse(args);
        if (options.Help)
        {
            Say(RecorderOptions.Usage);
            return RecorderExitCodes.Success;
        }

        if (options.Error is not null)
        {
            Say(options.Error, error: true);
            return RecorderExitCodes.Usage;
        }

        ILogger logger = Logging.Create("uia-trace", console: false);
        try
        {
            return Run(options, logger);
        }
        catch (Exception ex)
        {
            Say(RecorderOptions.ToolName + " failed: " + ex.GetType().Name + ": " + ex.Message, error: true);
            logger.Error(ex, "uefiaccess-uia-trace failed");
            return RecorderExitCodes.Failed;
        }
        finally
        {
            _writer?.Dispose();
            (logger as IDisposable)?.Dispose();
        }
    }

    /// <summary>Checks elevation, opens the trace file, records, and writes the summary.</summary>
    private static int Run(RecorderOptions options, ILogger logger)
    {
        if (!new PrivilegeManager(logger).IsElevated)
        {
            Say(NotElevatedMessage, error: true);
            return RecorderExitCodes.NotElevated;
        }

        logger.Information("uefiaccess-uia-trace {Version} started; events: {Events}", ToolVersion.Current, RecorderOptions.Describe(options.Events));
        TraceOutputResult output = TraceOutput.Open(
            options.OutPath,
            DateTime.Now,
            TraceOutputFolders.Default,
            () => new MachineIdentity(logger).ReadServiceTag());
        if (output.Writer is null)
        {
            Say("No trace file was created: " + output.Refusal + " Nothing was recorded.", error: true);
            logger.Warning("uefiaccess-uia-trace created no trace file: {Reason}", output.Refusal);
            return RecorderExitCodes.NoOutput;
        }

        TraceFileWriter writer = output.Writer;
        _writer = writer;
        string path = writer.FilePath ?? options.OutPath ?? TraceOutput.FilePrefix;
        if (output.Note is not null)
        {
            Say(output.Note);
        }

        writer.Comment("UIA-RECV lines are the UI Automation events a client in another process received from the UefiAccess window, in the order they arrived; each handler takes its time stamp before anything else.");
        writer.Comment("Subscribing makes WPF raise events it otherwise skips, and UI Automation fetches each event's cached properties from the window's UI thread; compare with a run of the input trace without this recorder.");
        writer.Comment("A name is read when the event arrives, which can be later than the raise. A LiveRegionChanged line also gives sentence=, its element's value as the last PropertyChanged Value with a value before it gave it ((unknown) when none came), and copy=yes when it followed a PropertyChanged Value=(none) of the same element, a valueless copy; the summary counts those copies.");
        RecorderEvents notRecorded = RecorderEvents.All & ~options.Events;
        writer.Comment("Events recorded: " + RecorderOptions.Describe(options.Events) + "; not recorded: " + RecorderOptions.Describe(notRecorded) + ".");
        Say("Trace file: " + path);

        EventCounts counts = new();
        RecorderOutcome outcome;
        using (UiaRecorder recorder = new(writer, options.Events, counts, text => Say(text)))
        {
            outcome = recorder.Run();
        }

        IReadOnlyList<string> summary = counts.SummaryLines(outcome.Recorded);
        foreach (string line in summary)
        {
            writer.Write(TraceKind.Summary, line);
        }

        writer.Write(TraceKind.Trace, "recorder: stopped: " + outcome.StopReason);
        writer.Dispose();
        logger.Information(
            "uefiaccess-uia-trace stopped ({Reason}); {Count} events written to {Path}",
            outcome.StopReason,
            counts.Total.ToString(CultureInfo.InvariantCulture),
            path);

        Say("Stopped: " + outcome.StopReason + ".");
        foreach (string line in summary)
        {
            Say(line);
        }

        if (writer.Failure is not null)
        {
            Say("Writing the trace file failed (" + writer.Failure.GetType().Name + ": " + writer.Failure.Message + "); it holds the lines written before that.", error: true);
            return RecorderExitCodes.NoOutput;
        }

        Say("Trace written to " + path);
        return RecorderExitCodes.Success;
    }

    /// <summary>
    /// Writes one message to the console: standard output, or standard error for a problem. Every
    /// console message of the recorder goes through here, so this unit's service tag, once read, is
    /// replaced in each of them as it is in the trace file.
    /// </summary>
    /// <param name="text">The message.</param>
    /// <param name="error">True for standard error.</param>
    private static void Say(string text, bool error = false)
    {
        string line = _writer is null ? text : _writer.RedactText(text);
        try
        {
            if (error)
            {
                Console.Error.WriteLine(line);
            }
            else
            {
                Console.Out.WriteLine(line);
            }
        }
        catch (IOException)
        {
            // A closed console must not stop the recording.
        }
    }
}
