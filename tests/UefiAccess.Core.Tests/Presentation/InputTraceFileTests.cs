// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;
using Serilog.Core;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Presentation.Diagnostics;
using Xunit;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// The application's input trace file (UefiAccess.App --trace-input): without the switch nothing is
/// checked, created or started and the trace is the null one; with it, a folder that is not a log
/// folder of UefiAccess is refused before anything is created; in an accepted folder exactly one
/// file, trace-input-&lt;yyyyMMdd-HHmmss&gt;.log, is created, its lines have the service tag
/// replaced, and closing it writes a count per kind and the stop line. The folder check and the
/// service tag reader are injected (no test touches the real log folders or WMI); the tags are
/// synthetic.
/// </summary>
public sealed class InputTraceFileTests : IDisposable
{
    private const string Tag = "TESTTAG7";
    private static readonly DateTime Now = new(2026, 10, 1, 14, 30, 5, DateTimeKind.Local);

    private readonly string _tempRoot;

    public InputTraceFileTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "input-trace-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        TestFolders.DeleteQuietly(_tempRoot);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void WithoutTheSwitch_TheTraceIsTheNullOne_AndNothingIsCreated(int arguments)
    {
        string[]? args = arguments switch
        {
            0 => null,
            1 => Array.Empty<string>(),
            _ => new[] { "--verbose", "/trace-input", "C:\\baselines\\optiplex" },
        };

        InputTraceStart start = InputTraceFile.Start(args, _tempRoot, Logger.None);

        Assert.Same(NullInputTrace.Instance, start.Trace);
        Assert.False(start.Trace.IsEnabled);
        Assert.False(start.Requested);
        Assert.Null(start.FilePath);
        Assert.Null(start.Refusal);
        Assert.False(Directory.Exists(_tempRoot));
    }

    [Fact]
    public void WithTheSwitch_AFolderThatIsNotALogFolder_IsRefused_AndNothingIsCreated()
    {
        InputTraceStart start = InputTraceFile.Start(new[] { "--trace-input" }, _tempRoot, Logger.None);

        Assert.True(start.Requested);
        Assert.Same(NullInputTrace.Instance, start.Trace);
        Assert.Null(start.FilePath);
        Assert.NotNull(start.Refusal);
        Assert.Contains("is not a log folder of UefiAccess", start.Refusal, StringComparison.Ordinal);
        Assert.False(Directory.Exists(_tempRoot));
    }

    [Fact]
    public void TryStart_InAnAcceptedFolder_CreatesExactlyOneFile_WithTheServiceTagReplaced()
    {
        Directory.CreateDirectory(_tempRoot);
        List<string> checkedFolders = new();
        InputTraceStart start = InputTraceFile.TryStart(
            _tempRoot,
            Now,
            folder =>
            {
                checkedFolders.Add(folder);
                return null;
            },
            () => Tag);

        Assert.True(start.Requested);
        Assert.Null(start.Refusal);
        InputTraceFile trace = Assert.IsType<InputTraceFile>(start.Trace);
        Assert.True(trace.IsEnabled);
        Assert.Equal(new[] { _tempRoot }, checkedFolders);
        string expected = Path.Combine(_tempRoot, "trace-input-20261001-143005.log");
        Assert.Equal(expected, start.FilePath);
        Assert.Equal(expected, trace.FilePath);

        trace.Write(TraceKind.Key, "down Down #1 repeat=no modifiers=None focus=ListItem/ListViewItem id=- row=Boot0001");
        trace.Write(TraceKind.Focus, "a name holding testtag7");
        trace.Write(TraceKind.Key, "up Down");
        trace.Redact("BASELINE9");
        trace.Write(TraceKind.Announce, "\"Loaded 3 variables from a baseline of BASELINE9\"");
        trace.Dispose();
        trace.Dispose();

        Assert.False(trace.IsEnabled);
        string file = Assert.Single(Directory.GetFiles(_tempRoot));
        Assert.Matches(new Regex(@"trace-input-\d{8}-\d{6}\.log$", RegexOptions.CultureInvariant), file);
        string text = File.ReadAllText(file);
        Assert.StartsWith("# " + InputTraceFile.Title, text, StringComparison.Ordinal);
        Assert.Contains("# " + InputTraceFile.KeyTimingComment + "\r\n", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf(InputTraceFile.KeyTimingComment, StringComparison.Ordinal) < text.IndexOf("\tKEY\t", StringComparison.Ordinal), "the KEY legend comes before the first event");
        Assert.Contains("15.6 ms", InputTraceFile.KeyTimingComment, StringComparison.Ordinal);
        Assert.DoesNotContain(Tag, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BASELINE9", text, StringComparison.Ordinal);
        Assert.Contains("a name holding [service tag]", text, StringComparison.Ordinal);

        string[] events = text.Split("\r\n").Where(line => line.Length > 0 && !line.StartsWith("# ", StringComparison.Ordinal)).ToArray();
        Assert.All(events, line => Assert.Equal(6, line.Split('\t').Length));
        string[] details = events.Select(line => line.Split('\t')[4] + " " + line.Split('\t')[5]).ToArray();
        Assert.Equal(
            new[]
            {
                "KEY down Down #1 repeat=no modifiers=None focus=ListItem/ListViewItem id=- row=Boot0001",
                "FOCUS a name holding [service tag]",
                "KEY up Down",
                "ANNOUNCE \"Loaded 3 variables from a baseline of [service tag]\"",
                "SUMMARY input trace: ANNOUNCE 1",
                "SUMMARY input trace: FOCUS 1",
                "SUMMARY input trace: KEY 2",
                "TRACE input trace: stopped",
            },
            details);
    }

    [Fact]
    public void TryStart_WhenTheFolderCheckRefuses_NothingIsCreated()
    {
        bool read = false;
        InputTraceStart start = InputTraceFile.TryStart(
            _tempRoot,
            Now,
            _ => "the folder is a junction",
            () =>
            {
                read = true;
                return Tag;
            });

        Assert.True(start.Requested);
        Assert.Same(NullInputTrace.Instance, start.Trace);
        Assert.Equal("the folder is a junction", start.Refusal);
        Assert.Null(start.FilePath);
        Assert.False(read);
        Assert.False(Directory.Exists(_tempRoot));
    }

    [Fact]
    public void TryStart_WhenTheFileCannotBeCreated_IsARefusal_NotAThrow()
    {
        InputTraceStart start = InputTraceFile.TryStart(Path.Combine(_tempRoot, "missing"), Now, _ => null, () => Tag);

        Assert.True(start.Requested);
        Assert.Same(NullInputTrace.Instance, start.Trace);
        Assert.NotNull(start.Refusal);
        Assert.StartsWith("no trace file can be created in ", start.Refusal, StringComparison.Ordinal);

        InputTraceStart thrown = InputTraceFile.TryStart(_tempRoot, Now, _ => throw new UnauthorizedAccessException("denied"), () => Tag);
        Assert.Same(NullInputTrace.Instance, thrown.Trace);
        Assert.Contains("denied", thrown.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void TryStart_TwiceInTheSameSecond_CreatesTwoFiles()
    {
        Directory.CreateDirectory(_tempRoot);
        InputTraceStart first = InputTraceFile.TryStart(_tempRoot, Now, _ => null, () => null);
        InputTraceStart second = InputTraceFile.TryStart(_tempRoot, Now, _ => null, () => null);
        (first.Trace as IDisposable)?.Dispose();
        (second.Trace as IDisposable)?.Dispose();

        Assert.Equal(Path.Combine(_tempRoot, "trace-input-20261001-143005.log"), first.FilePath);
        Assert.Equal(Path.Combine(_tempRoot, "trace-input-20261001-143005-2.log"), second.FilePath);
    }

    [Fact]
    public void TheNullTrace_IsOff_AndDoesNothing()
    {
        IInputTrace trace = NullInputTrace.Instance;

        Assert.False(trace.IsEnabled);
        trace.Write(TraceKind.Key, "down Down #1");
        trace.Redact(Tag);
        Assert.Same(trace, NullInputTrace.Instance);
    }

    [Fact]
    public void ThePrefix_IsTheDocumentedOne()
    {
        Assert.Equal("trace-input-", InputTraceFile.FilePrefix);
        Assert.Equal("trace-input-20261001-143005.log", TraceFileWriter.FileName(InputTraceFile.FilePrefix, Now, 1));
        Assert.Equal("UefiAccess input trace", InputTraceFile.Title);
    }
}
