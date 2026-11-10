// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;
using System.Text.RegularExpressions;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Core.Safety;
using Xunit;

namespace UefiAccess.Core.Tests.Safety;

/// <summary>
/// The rule (stage S9), 3.5 and 6 (item 12) for the two folders the
/// write pipeline keeps: the snapshot root and the write-record root are the "snapshots" and
/// "writes" folders directly under <see cref="Logging.DataDirectory"/> (ProgramData\UefiAccess),
/// exactly as baseline\ and gate\ are, and the production instances check them through
/// <see cref="Logging.PrepareDataFolderFor(string)"/> before anything is written. The behaviour
/// of a refused root (blocked at snapshotRoot, nothing written) is asserted by the store and
/// transaction tests; what is pinned here is where the default roots point, which folder check
/// they hold, and that the Safety code derives no folder of its own. Constructing the store and
/// the log only computes paths, so, like <see cref="DataFolderSafetyTests"/>, nothing here touches
/// the real data root.
/// </summary>
public sealed class SnapshotFolderTests
{
    private const string SafetyDirectory = "src/UefiAccess.Core/Safety/";

    /// <summary>The data-folder check every elevated write under the data root goes through.</summary>
    private static readonly MethodInfo DataFolderCheck = typeof(Logging).GetMethod(nameof(Logging.PrepareDataFolderFor), new[] { typeof(string) })!;

    /// <summary>
    /// A folder derived from anything but the data root: the temporary folder, a special folder,
    /// the process or current folder, an environment variable, or a drive-letter path literal.
    /// </summary>
    private static readonly Regex OwnFolderPattern = new(
        @"\bGetTemp(?:Path|FileName)\b|\bSpecialFolder\b|\bGetFolderPath\b|\bBaseDirectory\b|\bCurrentDirectory\b|\bGetCurrentDirectory\b"
        + @"|\bGetEnvironmentVariable\b|\bExpandEnvironmentVariables\b|[""'][A-Za-z]:\\",
        RegexOptions.CultureInvariant);

    [Fact]
    public void SnapshotRootIsTheSnapshotsFolderUnderTheDataDirectoryAndIsCheckedBeforeUse()
    {
        SnapshotStore store = new();

        Assert.Equal("snapshots", SnapshotStore.FolderName);
        Assert.Equal(UnderDataDirectory("snapshots"), store.RootDirectory);
        Assert.Equal(DataFolderCheck, FolderCheckOf(store));
    }

    [Fact]
    public void WritesRootIsTheWritesFolderUnderTheDataDirectoryAndIsCheckedBeforeUse()
    {
        WriteRecordLog log = new();

        Assert.Equal("writes", WriteRecordLog.FolderName);
        Assert.Equal(UnderDataDirectory("writes"), log.DirectoryPath);
        Assert.Equal(Path.Combine(UnderDataDirectory("writes"), WriteRecordLog.FileName), log.FilePath);
        Assert.Equal(DataFolderCheck, FolderCheckOf(log));
    }

    [Fact]
    public void SafetySourcesDeriveNoFolderOfTheirOwnAndNameTheDataFolderCheck()
    {
        string[] files = RepositoryPaths.Enumerate("src", "*.cs")
            .Where(file => RepositoryPaths.Relative(file).StartsWith(SafetyDirectory, StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(files);

        List<string> violations = new();
        bool prepares = false;
        foreach (string file in files)
        {
            string relative = RepositoryPaths.Relative(file);
            string text = SourceCalls.WithoutComments(File.ReadAllText(file));
            violations.AddRange(SourceCalls.LinesMatching(text, OwnFolderPattern).Select(line => $"{relative}:{line}: derives a folder that is not under Logging.DataDirectory"));
            prepares |= SourceCalls.LinesNaming(text, nameof(Logging.PrepareDataFolderFor)).Count > 0;
        }

        Assert.True(violations.Count == 0, "The snapshot and write-record roots come from Logging.DataDirectory alone:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
        Assert.True(prepares, "No file under " + SafetyDirectory + " names PrepareDataFolderFor; the roots must go through the data-folder check");
    }

    /// <summary>The full path of a folder directly under the data root, as the store and the log normalise it.</summary>
    private static string UnderDataDirectory(string folder) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(Logging.DataDirectory, folder)));

    /// <summary>
    /// The method behind the one folder check an instance holds: its only instance field of type
    /// Func&lt;string, string?&gt;, which must be a static method with no captured state.
    /// </summary>
    private static MethodInfo FolderCheckOf(object owner)
    {
        FieldInfo field = Assert.Single(
            owner.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            candidate => candidate.FieldType == typeof(Func<string, string>));
        Delegate? check = (Delegate?)field.GetValue(owner);
        Assert.NotNull(check);
        Assert.Null(check!.Target);
        return check.Method;
    }
}
