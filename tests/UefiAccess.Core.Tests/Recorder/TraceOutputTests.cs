// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Diagnostics;
using UefiAccess.UiaTrace.Recording;
using Xunit;

namespace UefiAccess.Core.Tests.Recorder;

/// <summary>
/// Where the recorder's trace file goes (<see cref="TraceOutput"/>), over temporary folders and
/// injected folder checks: by default uia-trace-&lt;yyyyMMdd-HHmmss&gt;.log in the shared log folder
/// once Logging.PrepareRunFileFolder has passed it, the per-user log folder when the shared one is
/// refused or cannot be written, and a refusal (nothing created) when neither can take it; with
/// --out an existing folder gets the default name inside it, "-2" when that name is taken, and any
/// other path is the file itself, never an existing file overwritten. A folder given with --out is
/// checked with Logging.PrepareDataFolderFor first. Synthetic tags only.
/// </summary>
public sealed class TraceOutputTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 1, 14, 30, 7, DateTimeKind.Local);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "uefiaccess-recorder-" + Guid.NewGuid().ToString("N"));
    private readonly string _shared;
    private readonly string _user;
    private readonly List<string> _runFileChecks = new();
    private readonly List<string> _dataFolderChecks = new();

    public TraceOutputTests()
    {
        _shared = Path.Combine(_root, "ProgramData", "UefiAccess", "logs");
        _user = Path.Combine(_root, "User", "UefiAccess", "logs");
        Directory.CreateDirectory(_shared);
        Directory.CreateDirectory(_user);
    }

    public void Dispose()
    {
        TestFolders.DeleteQuietly(_root);
    }

    [Fact]
    public void Constants_AreTheDocumentedOnes()
    {
        Assert.Equal("uia-trace-", TraceOutput.FilePrefix);
        Assert.Equal("UefiAccess UI Automation trace", TraceOutput.Title);
        Assert.Equal("uia-trace-20261001-143007.log", TraceFileWriter.FileName(TraceOutput.FilePrefix, Now, 1));
    }

    [Fact]
    public void Default_IsTheUefiAccessLogFolders()
    {
        TraceOutputFolders folders = TraceOutputFolders.Default;

        Assert.Equal(Logging.LogDirectory, folders.SharedLogDirectory);
        Assert.Equal(Logging.UserLogDirectory, folders.UserLogDirectory);
    }

    [Fact]
    public void ByDefault_TheFileIsInTheSharedLogFolder_WithTheDefaultName()
    {
        TraceOutputResult result = TraceOutput.Open(null, Now, Folders(), () => "TESTTAG7");

        string path = Finish(result);
        Assert.Null(result.Refusal);
        Assert.Null(result.Note);
        Assert.Equal(Path.Combine(_shared, "uia-trace-20261001-143007.log"), path);
        Assert.Equal(new[] { _shared }, _runFileChecks);
        Assert.Empty(_dataFolderChecks);
        Assert.Empty(Directory.GetFiles(_user));
        Assert.Equal("# UefiAccess UI Automation trace", File.ReadLines(path).First());
    }

    [Fact]
    public void ByDefault_TheServiceTagIsReplacedInTheFile()
    {
        TraceOutputResult result = TraceOutput.Open("  ", Now, Folders(), () => "TESTTAG7");
        result.Writer!.Write(TraceKind.UiaReceived, "FocusChanged Text id=- name=\"testtag7\"");

        string text = File.ReadAllText(Finish(result));

        Assert.DoesNotContain("TESTTAG7", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("name=\"" + TraceRedactor.Replacement + "\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnreadServiceTag_IsSaidInTheRecordersOwnWords()
    {
        // The recorder registers no tag of a load, so the application's sentence would promise
        // more than its file does: only the text after a "Service tag:" label is still replaced.
        TraceOutputResult byDefault = TraceOutput.Open(null, Now, Folders(), () => null);
        TraceOutputResult given = TraceOutput.Open(Path.Combine(_root, "karl-uia.log"), Now, Folders(), () => null);

        foreach (string text in new[] { File.ReadAllText(Finish(byDefault)), File.ReadAllText(Finish(given)) })
        {
            Assert.Contains("# " + TraceOutput.ServiceTagUnreadComment, text, StringComparison.Ordinal);
            Assert.DoesNotContain(TraceFileWriter.ServiceTagUnreadComment, text, StringComparison.Ordinal);
        }

        Assert.Contains("\"" + UiaEventText.ServiceTagLabel + ":\"", TraceOutput.ServiceTagUnreadComment, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("uia.log")]
    [InlineData("-x")]
    [InlineData(@"traces\uia.log")]
    [InlineData(@"\uia.log")]
    [InlineData(@"C:uia.log")]
    public void Out_APathThatIsNotFull_IsRefused_AndNothingIsCreated(string outPath)
    {
        // An elevated prompt starts in C:\Windows\System32, where a relative path would land.
        string current = Path.GetFullPath(outPath);
        bool existedBefore = File.Exists(current);

        TraceOutputResult result = TraceOutput.Open(outPath, Now, Folders(), () => null);

        Assert.Null(result.Writer);
        Assert.NotNull(result.Refusal);
        Assert.StartsWith(outPath + " is not a full path: ", result.Refusal, StringComparison.Ordinal);
        Assert.Contains(@"C:\Windows\System32", result.Refusal, StringComparison.Ordinal);
        Assert.Empty(_dataFolderChecks);
        Assert.Empty(_runFileChecks);
        Assert.Equal(existedBefore, File.Exists(current));
    }

    [Fact]
    public void WhenTheSharedFolderIsRefused_TheFileGoesToTheUserFolder()
    {
        TraceOutputResult result = TraceOutput.Open(null, Now, Folders(_shared), () => null);

        string path = Finish(result);
        Assert.Equal(Path.Combine(_user, "uia-trace-20261001-143007.log"), path);
        Assert.Equal(new[] { _shared, _user }, _runFileChecks);
        Assert.Empty(Directory.GetFiles(_shared));
        Assert.NotNull(result.Note);
        Assert.Contains(_shared, result.Note, StringComparison.Ordinal);
        Assert.Contains("refused for the test", result.Note, StringComparison.Ordinal);
        Assert.Contains(_user, result.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void WhenTheSharedFolderCannotBeWritten_TheFileGoesToTheUserFolder()
    {
        // The check passes, but the folder is gone by the time the file is created.
        TestFolders.DeleteQuietly(_shared);

        TraceOutputResult result = TraceOutput.Open(null, Now, Folders(), () => null);

        Assert.Equal(Path.Combine(_user, "uia-trace-20261001-143007.log"), Finish(result));
        Assert.NotNull(result.Note);
    }

    [Fact]
    public void WhenNeitherFolderCanTakeIt_NothingIsCreated()
    {
        TraceOutputResult result = TraceOutput.Open(null, Now, Folders(_shared, _user), () => null);

        Assert.Null(result.Writer);
        Assert.NotNull(result.Refusal);
        Assert.Contains(_shared, result.Refusal, StringComparison.Ordinal);
        Assert.Contains(_user, result.Refusal, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void Out_AnExistingFolder_GetsTheDefaultName()
    {
        string folder = Path.Combine(_root, "Desktop");
        Directory.CreateDirectory(folder);

        TraceOutputResult result = TraceOutput.Open(folder, Now, Folders(), () => null);

        Assert.Equal(Path.Combine(folder, "uia-trace-20261001-143007.log"), Finish(result));
        Assert.Equal(new[] { folder }, _dataFolderChecks);
        Assert.Empty(_runFileChecks);
    }

    [Fact]
    public void Out_AnExistingFolder_WhereTheNameIsTaken_TakesTheNextName()
    {
        string folder = Path.Combine(_root, "Desktop");
        Directory.CreateDirectory(folder);
        string taken = Path.Combine(folder, "uia-trace-20261001-143007.log");
        File.WriteAllText(taken, "an earlier trace");

        TraceOutputResult result = TraceOutput.Open(folder, Now, Folders(), () => null);

        Assert.Equal(Path.Combine(folder, "uia-trace-20261001-143007-2.log"), Finish(result));
        Assert.Equal("an earlier trace", File.ReadAllText(taken));
    }

    [Fact]
    public void Out_AFilePath_IsTheFileItself()
    {
        string path = Path.Combine(_root, "karl-uia.log");

        TraceOutputResult result = TraceOutput.Open(path, Now, Folders(), () => null);

        Assert.Equal(path, Finish(result));
        Assert.Equal(new[] { _root }, _dataFolderChecks);
    }

    [Fact]
    public void Out_AnExistingFile_IsRefusedAndKept()
    {
        string path = Path.Combine(_root, "karl-uia.log");
        File.WriteAllText(path, "keep me");

        TraceOutputResult result = TraceOutput.Open(path, Now, Folders(), () => null);

        Assert.Null(result.Writer);
        Assert.NotNull(result.Refusal);
        Assert.Contains(path, result.Refusal, StringComparison.Ordinal);
        Assert.Contains("never overwritten", result.Refusal, StringComparison.Ordinal);
        Assert.Equal("keep me", File.ReadAllText(path));
    }

    [Fact]
    public void Out_InAFolderThatDoesNotExist_IsRefused()
    {
        string path = Path.Combine(_root, "missing", "karl-uia.log");

        TraceOutputResult result = TraceOutput.Open(path, Now, Folders(), () => null);

        Assert.Null(result.Writer);
        Assert.NotNull(result.Refusal);
        Assert.False(Directory.Exists(Path.Combine(_root, "missing")));
    }

    [Fact]
    public void Out_WhereTheDataFolderCheckRefuses_NothingIsCreated()
    {
        string folder = Path.Combine(_root, "Desktop");
        Directory.CreateDirectory(folder);

        TraceOutputResult inFolder = TraceOutput.Open(folder, Now, Folders(folder), () => null);
        TraceOutputResult asFile = TraceOutput.Open(Path.Combine(folder, "x.log"), Now, Folders(folder), () => null);

        Assert.Null(inFolder.Writer);
        Assert.Contains("refused for the test", inFolder.Refusal, StringComparison.Ordinal);
        Assert.Null(asFile.Writer);
        Assert.Equal(folder + " refused for the test", asFile.Refusal);
        Assert.Empty(Directory.GetFiles(folder));
    }

    /// <summary>The temporary folders with checks that record their calls and refuse the folders named.</summary>
    private TraceOutputFolders Folders(params string[] refuse)
    {
        string? Check(List<string> calls, string folder)
        {
            calls.Add(folder);
            return refuse.Contains(folder, StringComparer.OrdinalIgnoreCase) ? folder + " refused for the test" : null;
        }

        return new TraceOutputFolders(
            _shared,
            _user,
            folder => Check(_runFileChecks, folder),
            folder => Check(_dataFolderChecks, folder),
            Directory.Exists);
    }

    /// <summary>Closes the writer of a successful result and returns its file, which must exist.</summary>
    private static string Finish(TraceOutputResult result)
    {
        Assert.NotNull(result.Writer);
        string? path = result.Writer!.FilePath;
        result.Writer.Dispose();
        Assert.NotNull(path);
        Assert.True(File.Exists(path), path);
        return path!;
    }
}
