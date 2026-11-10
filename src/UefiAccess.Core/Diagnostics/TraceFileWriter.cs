// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace UefiAccess.Core.Diagnostics;

/// <summary>
/// Writes one trace file in the <see cref="TraceLine"/> format: the input trace of UefiAccess.App
/// (started with --trace-input) and the UI Automation recorder's trace share it, so their lines
/// merge exactly. The trace must not change the timing it measures, so a caller only captures the
/// event's QPC value (<see cref="Stopwatch.GetTimestamp"/>), local time and thread id and puts them
/// in a queue; formatting, redaction and the file itself are left to one background thread
/// ("UefiAccess trace writer", below normal priority), which takes the queue in order with a
/// blocking consumer (<see cref="BlockingCollection{T}.GetConsumingEnumerable()"/>; nothing here
/// waits for a time or polls) and flushes the file whenever the queue is empty, so a crash loses
/// at most what had not been written yet.
/// </summary>
/// <remarks>
/// <para>
/// Before its first line the writer thread reads the service tag (the reader the caller passes, in
/// the application and the recorder Win32_BIOS.SerialNumber through the bounded WMI query of
/// MachineIdentity.ReadServiceTag), and the text of every line it writes afterwards goes through a
/// <see cref="TraceRedactor"/> that replaces it with "[service tag]": the kind and the details of
/// an event line, the text of a comment, and the title and tool version of the header. The
/// numbers (the time, the milliseconds and the QPC value of an event line, and the header's) are
/// not redacted: a tag of digits that happened to occur inside one would otherwise be given away,
/// and the line would no longer merge. <see cref="Redact"/> adds further values (the tag of a
/// baseline the application shows). A tag that cannot be read is said in a comment line (the
/// caller's sentence, which says what is still replaced in its file), and the trace goes on.
/// </para>
/// <para>
/// The file is created with <see cref="FileMode.CreateNew"/>: an existing file, or a link by that
/// name, is never overwritten or followed (<see cref="CreateInFolder"/> then tries the next name).
/// The folder is the caller's to check (Logging.PrepareRunFileFolder). An I/O error stops the
/// writing (<see cref="Failure"/>) without reaching the caller; the queue is still drained.
/// <see cref="Write(string, string?)"/> never throws and is ignored after <see cref="Dispose"/>,
/// which writes what is queued, flushes and closes the file, waiting for the writer thread to
/// finish (with no time limit: it ends once the queue is written).
/// </para>
/// </remarks>
public sealed class TraceFileWriter : IDisposable
{
    /// <summary>The name of the background thread that writes the file.</summary>
    public const string ThreadName = "UefiAccess trace writer";

    /// <summary>
    /// The comment line written when the service tag could not be read, unless the caller gives
    /// its own: the sentence of the application's input trace, where the identity of a load
    /// registers its tag.
    /// </summary>
    public const string ServiceTagUnreadComment = "The service tag could not be read; a value the application registers later (the identity of a load) is still replaced.";

    /// <summary>How many names <see cref="CreateInFolder"/> tries: the plain name, then "-2" to "-9".</summary>
    private const int MaxAttempts = 9;

    private readonly BlockingCollection<Entry> _queue = new(new ConcurrentQueue<Entry>());
    private readonly TraceRedactor _redactor = new();
    private readonly TextWriter _output;
    private readonly Func<string?> _readServiceTag;
    private readonly string _title;
    private readonly string _toolVersion;
    private readonly string _serviceTagUnreadComment;
    private readonly int _processId;
    private readonly Thread _consumer;
    private int _disposed;
    private long _linesWritten;
    private Exception? _failure;

    /// <summary>Creates the writer over a text writer and starts its thread; the tests' constructor (the file is the factories').</summary>
    /// <param name="output">Receives the lines; disposed by <see cref="Dispose"/>.</param>
    /// <param name="title">The first header line, for example "UefiAccess input trace".</param>
    /// <param name="readServiceTag">Reads the service tag; called once, on the writer thread, before the first line.</param>
    /// <param name="toolVersion">The tool version named in the header.</param>
    /// <param name="startTimestamp">The QPC value the trace starts at; the second column counts from it.</param>
    /// <param name="startLocal">The local date and time the trace starts.</param>
    /// <param name="processId">The process named in the header.</param>
    /// <param name="serviceTagUnreadComment">The comment written when the service tag cannot be read; null for <see cref="ServiceTagUnreadComment"/>.</param>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    internal TraceFileWriter(TextWriter output, string title, Func<string?> readServiceTag, string toolVersion, long startTimestamp, DateTime startLocal, int processId, string? serviceTagUnreadComment = null)
        : this(output, null, title, readServiceTag, toolVersion, startTimestamp, startLocal, processId, serviceTagUnreadComment)
    {
    }

    private TraceFileWriter(TextWriter output, string? filePath, string title, Func<string?> readServiceTag, string toolVersion, long startTimestamp, DateTime startLocal, int processId, string? serviceTagUnreadComment)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(readServiceTag);
        ArgumentNullException.ThrowIfNull(toolVersion);
        _output = output;
        FilePath = filePath;
        _title = title;
        _readServiceTag = readServiceTag;
        _toolVersion = toolVersion;
        _serviceTagUnreadComment = string.IsNullOrWhiteSpace(serviceTagUnreadComment) ? ServiceTagUnreadComment : serviceTagUnreadComment;
        _processId = processId;
        StartTimestamp = startTimestamp;
        StartLocal = startLocal;
        _consumer = new Thread(Consume)
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = ThreadName,
        };
        _consumer.Start();
    }

    /// <summary>The full path of the file, or null for a writer over a text writer.</summary>
    public string? FilePath { get; }

    /// <summary>The QPC value (<see cref="Stopwatch.GetTimestamp"/>) the trace started at.</summary>
    public long StartTimestamp { get; }

    /// <summary>The local date and time the trace started.</summary>
    public DateTime StartLocal { get; }

    /// <summary>How many lines (header and comments included) have been written so far.</summary>
    public long LinesWritten => Interlocked.Read(ref _linesWritten);

    /// <summary>The error that stopped the writing, or null while the file is written.</summary>
    public Exception? Failure => Volatile.Read(ref _failure);

    /// <summary>
    /// Creates the file at <paramref name="path"/> (<see cref="FileMode.CreateNew"/>, write access,
    /// others may read; UTF-8 without a byte order mark, CRLF line ends) and starts the writer.
    /// </summary>
    /// <param name="path">The file to create; it must not exist.</param>
    /// <param name="title">The first header line.</param>
    /// <param name="readServiceTag">Reads the service tag on the writer thread before the first line.</param>
    /// <param name="serviceTagUnreadComment">The comment written when the service tag cannot be read; null for <see cref="ServiceTagUnreadComment"/>.</param>
    /// <returns>The writer.</returns>
    /// <exception cref="IOException">The file exists, or cannot be created.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder cannot be written.</exception>
    public static TraceFileWriter CreateNew(string path, string title, Func<string?> readServiceTag, string? serviceTagUnreadComment = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(readServiceTag);
        string fullPath = Path.GetFullPath(path);
        FileStream stream = new(fullPath, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.Read });
        try
        {
            StreamWriter writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { NewLine = "\r\n" };
            return new TraceFileWriter(writer, fullPath, title, readServiceTag, ToolVersion.Current, Stopwatch.GetTimestamp(), DateTime.Now, Environment.ProcessId, serviceTagUnreadComment);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Creates a new file named <see cref="FileName"/> in <paramref name="directory"/>: the plain
    /// name first, and "-2" to "-9" after it while a file (or anything else) by the name before
    /// exists. Nothing is overwritten.
    /// </summary>
    /// <param name="directory">The folder, already checked by the caller.</param>
    /// <param name="prefix">The start of the file name, for example "trace-input-".</param>
    /// <param name="now">The local time the name is made from.</param>
    /// <param name="title">The first header line.</param>
    /// <param name="readServiceTag">Reads the service tag on the writer thread before the first line.</param>
    /// <param name="serviceTagUnreadComment">The comment written when the service tag cannot be read; null for <see cref="ServiceTagUnreadComment"/>.</param>
    /// <returns>The writer.</returns>
    /// <exception cref="IOException">No name was free, or the file cannot be created.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder cannot be written.</exception>
    public static TraceFileWriter CreateInFolder(string directory, string prefix, DateTime now, string title, Func<string?> readServiceTag, string? serviceTagUnreadComment = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentNullException.ThrowIfNull(prefix);
        for (int attempt = 1; ; attempt++)
        {
            string path = Path.Combine(directory, FileName(prefix, now, attempt));
            try
            {
                return CreateNew(path, title, readServiceTag, serviceTagUnreadComment);
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < MaxAttempts && EntryExists(path))
            {
                // Another trace started in the same second (or anything else, a folder, holds the name): the next name.
            }
        }
    }

    /// <summary>The file name for a trace started at <paramref name="now"/>: "&lt;prefix&gt;yyyyMMdd-HHmmss.log", with "-&lt;attempt&gt;" before ".log" from the second attempt on.</summary>
    /// <param name="prefix">The start of the name, for example "trace-input-" or "uia-trace-".</param>
    /// <param name="now">The local time.</param>
    /// <param name="attempt">1 for the plain name, 2 and up for the alternatives.</param>
    /// <returns>The file name.</returns>
    public static string FileName(string prefix, DateTime now, int attempt)
    {
        string name = (prefix ?? string.Empty) + now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        if (attempt >= 2)
        {
            name += "-" + attempt.ToString(CultureInfo.InvariantCulture);
        }

        return name + ".log";
    }

    /// <summary>
    /// Queues an event line stamped now: the QPC value, the local time and the managed thread id
    /// are captured on the calling thread; everything else happens on the writer thread.
    /// Thread-safe; never throws; ignored after <see cref="Dispose"/>.
    /// </summary>
    /// <param name="kind">The event kind (<see cref="TraceKind"/>).</param>
    /// <param name="details">The details.</param>
    public void Write(string kind, string? details)
    {
        long timestamp = Stopwatch.GetTimestamp();
        Write(timestamp, DateTime.Now, Environment.CurrentManagedThreadId, kind, details);
    }

    /// <summary>Queues an event line with a stamp the caller captured when the event happened. Thread-safe; never throws; ignored after <see cref="Dispose"/>.</summary>
    /// <param name="timestamp">The QPC value of the event.</param>
    /// <param name="localTime">The local time of the event.</param>
    /// <param name="threadId">The managed id of the event's thread.</param>
    /// <param name="kind">The event kind (<see cref="TraceKind"/>).</param>
    /// <param name="details">The details.</param>
    public void Write(long timestamp, DateTime localTime, int threadId, string kind, string? details)
    {
        Enqueue(new Entry(timestamp, localTime, threadId, kind ?? string.Empty, details, IsComment: false));
    }

    /// <summary>Queues a comment line ("# " and the text), kept in order with the event lines. Thread-safe; never throws.</summary>
    /// <param name="text">The text.</param>
    public void Comment(string text)
    {
        Enqueue(new Entry(0, default, 0, string.Empty, text, IsComment: true));
    }

    /// <summary>Adds a value to replace with "[service tag]" in every line written from now on, those already queued included. Thread-safe.</summary>
    /// <param name="value">The value; null, blank and short values are ignored (<see cref="TraceRedactor.Add"/>).</param>
    public void Redact(string? value)
    {
        _redactor.Add(value);
    }

    /// <summary>The text with every value known to this writer replaced, for text written elsewhere (the recorder's console).</summary>
    /// <param name="text">The text.</param>
    /// <returns>The redacted text.</returns>
    public string RedactText(string? text)
    {
        return _redactor.Apply(text);
    }

    /// <summary>
    /// Stops taking lines, lets the writer thread write every line queued, then flushes and closes
    /// the file. Waits for the thread with no time limit. Idempotent; never throws.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _queue.CompleteAdding();
        }
        catch (ObjectDisposedException)
        {
            // Nothing more can be queued either way.
        }

        _consumer.Join();
        try
        {
            _output.Flush();
        }
        catch (Exception ex)
        {
            RecordFailure(ex);
        }

        try
        {
            _output.Dispose();
        }
        catch (Exception ex)
        {
            RecordFailure(ex);
        }

        _queue.Dispose();
    }

    /// <summary>True when anything (a file, a folder, a link) exists at the path; a link is not followed.</summary>
    private static bool EntryExists(string path)
    {
        try
        {
            File.GetAttributes(path);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void Enqueue(Entry entry)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            _queue.TryAdd(entry);
        }
        catch (InvalidOperationException)
        {
            // Dispose completed or disposed the queue meanwhile (ObjectDisposedException is one too); the line comes too late.
        }
    }

    /// <summary>The writer thread: reads the service tag, writes the header, then every queued line in order until the queue is completed.</summary>
    private void Consume()
    {
        try
        {
            bool tagRead = ReadServiceTag();
            foreach (string line in TraceLine.Header(_redactor.Apply(_title), _redactor.Apply(_toolVersion), StartLocal, StartTimestamp, _processId))
            {
                WriteLine(line);
            }

            if (!tagRead)
            {
                WriteLine(TraceLine.CommentPrefix + TraceLine.Clean(_serviceTagUnreadComment));
            }

            Flush();
            foreach (Entry entry in _queue.GetConsumingEnumerable())
            {
                WriteEntry(entry);
                if (_queue.Count == 0)
                {
                    Flush();
                }
            }

            Flush();
        }
        catch (Exception ex)
        {
            // The writer thread must never end the process; what stopped it is kept.
            RecordFailure(ex);
        }
    }

    /// <summary>Calls the service tag reader once and registers what it returns; false when it threw or returned nothing.</summary>
    private bool ReadServiceTag()
    {
        try
        {
            string? tag = _readServiceTag();
            if (string.IsNullOrWhiteSpace(tag))
            {
                return false;
            }

            _redactor.Add(tag);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Formats one queued line with its text fields redacted (the kind and the details, or the comment's text); the numbers are left as they are.</summary>
    private void WriteEntry(Entry entry)
    {
        try
        {
            string line = entry.IsComment
                ? TraceLine.CommentPrefix + _redactor.Apply(TraceLine.Clean(entry.Details))
                : TraceLine.Format(entry.LocalTime, entry.Timestamp, StartTimestamp, entry.ThreadId, _redactor.Apply(TraceLine.Clean(entry.Kind)), _redactor.Apply(TraceLine.Clean(entry.Details)));
            WriteLine(line);
        }
        catch (Exception ex)
        {
            RecordFailure(ex);
        }
    }

    /// <summary>Writes one line as it is (its text was redacted when it was made), unless an earlier write failed.</summary>
    private void WriteLine(string line)
    {
        if (Volatile.Read(ref _failure) is not null)
        {
            return;
        }

        try
        {
            _output.WriteLine(line);
            Interlocked.Increment(ref _linesWritten);
        }
        catch (Exception ex)
        {
            RecordFailure(ex);
        }
    }

    private void Flush()
    {
        if (Volatile.Read(ref _failure) is not null)
        {
            return;
        }

        try
        {
            _output.Flush();
        }
        catch (Exception ex)
        {
            RecordFailure(ex);
        }
    }

    private void RecordFailure(Exception ex)
    {
        Interlocked.CompareExchange(ref _failure, ex, null);
    }

    /// <summary>One queued line: the stamp captured by the caller, the kind and the details, or a comment.</summary>
    private readonly record struct Entry(long Timestamp, DateTime LocalTime, int ThreadId, string Kind, string? Details, bool IsComment);
}
