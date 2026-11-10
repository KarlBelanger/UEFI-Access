// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using UefiAccess.Core.Baseline;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Privileges;
using UefiAccess.Core.Variables;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The limits <see cref="BaselineReader"/> puts on an untrusted baseline folder: an entry above
/// <see cref="BaselineReader.MaximumEntrySize"/> is skipped before its file is opened, and an
/// index.json, vars folder or .bin file that is a junction or symbolic link is refused, so a path
/// that looks inside the folder cannot read a file elsewhere. Junctions need no privilege and are
/// created with "mklink /J"; file symbolic links need Developer Mode or the symbolic-link
/// privilege, so that check is reported as skipped where the host cannot create one.
/// </summary>
public sealed class BaselineReaderSafetyTests : IDisposable
{
    private const string RunId = "20260922-101500";
    private const VariableAttributes NvBsRt = VariableAttributes.NonVolatile | VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess;

    private static readonly Guid Global = KnownGuids.EfiGlobalVariable;

    private readonly string _tempRoot;

    public BaselineReaderSafetyTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "reader-safety-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        TestFolders.DeleteQuietly(_tempRoot);
    }

    [Fact]
    public void MaximumEntrySize_IsOneMebibyte_BelowTheLiveStoresReadCeiling()
    {
        Assert.Equal(1024 * 1024, BaselineReader.MaximumEntrySize);
        Assert.True(BaselineReader.MaximumEntrySize < WindowsFirmwareVariableStore.MaximumReadBufferSize);
    }

    [Theory]
    [InlineData(1024 * 1024 + 1)]
    [InlineData(4 * 1024 * 1024)]
    [InlineData(int.MaxValue)]
    public void Read_EntryAboveTheSizeLimit_IsSkippedWithoutReadingIt(int declaredSize)
    {
        string folder = Export(SampleVariables());
        RewriteIndex(folder, index => index with
        {
            Variables = index.Variables.Select(e => e.Name == "Timeout" ? e with { Size = declaredSize } : e).ToArray(),
        });

        BaselineReadResult result = BaselineReader.Read(folder);

        Assert.Null(result.Error);
        Assert.Equal(2, result.Variables.Count);
        Assert.DoesNotContain(result.Variables, v => v.Name == "Timeout");
        string warning = Assert.Single(result.Warnings);
        Assert.Contains($"the index size {declaredSize} bytes is above the 1048576-byte limit for one firmware variable; the entry was skipped.", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_EntryExactlyAtTheSizeLimit_ReadsBack()
    {
        byte[] large = new byte[BaselineReader.MaximumEntrySize];
        new Random(7020).NextBytes(large);
        FirmwareVariable[] variables = { new("Large", Global, NvBsRt, large) };
        string folder = Export(variables);

        BaselineReadResult result = BaselineReader.Read(folder);

        Assert.Null(result.Error);
        Assert.Empty(result.Warnings);
        FirmwareVariable read = Assert.Single(result.Variables);
        Assert.Equal(large, read.Data);
    }

    [Fact]
    public void Read_VarsFolderIsAJunction_EveryEntryIsSkipped()
    {
        string folder = Export(SampleVariables());
        string vars = Path.Combine(folder, "vars");
        string outside = Path.Combine(_tempRoot, "outside-vars");
        Directory.Move(vars, outside);
        Assert.True(TestLinks.TryCreateJunction(vars, outside), "mklink /J could not create the junction");

        BaselineReadResult result = BaselineReader.Read(folder);

        Assert.Null(result.Error);
        Assert.Empty(result.Variables);
        Assert.Equal(3, result.Warnings.Count);
        Assert.All(result.Warnings, warning => Assert.Contains("'vars' is a symbolic link or junction, which a baseline folder must not contain; the entry was skipped.", warning, StringComparison.Ordinal));
    }

    [Fact]
    public void Read_JunctionDeeperInsideTheFolder_IsFoundOnTheWayToTheFile()
    {
        string folder = Export(SampleVariables());
        string outside = Path.Combine(_tempRoot, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllBytes(Path.Combine(outside, "t.bin"), new byte[] { 0x05, 0x00 });
        string nested = Path.Combine(folder, "vars", "nested");
        Assert.True(TestLinks.TryCreateJunction(nested, outside), "mklink /J could not create the junction");
        RewriteIndex(folder, index => index with
        {
            Variables = index.Variables.Select(e => e.Name == "Timeout" ? e with { File = "vars/nested/t.bin" } : e).ToArray(),
        });

        BaselineReadResult result = BaselineReader.Read(folder);

        Assert.Null(result.Error);
        Assert.Equal(2, result.Variables.Count);
        string warning = Assert.Single(result.Warnings);
        Assert.Contains("'vars/nested' is a symbolic link or junction", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_FolderReachedThroughAJunction_IsReadNormally()
    {
        // Only what lies inside the chosen folder is checked; the folder itself may be a link.
        string folder = Export(SampleVariables());
        string alias = Path.Combine(_tempRoot, "alias");
        Assert.True(TestLinks.TryCreateJunction(alias, folder), "mklink /J could not create the junction");

        BaselineReadResult result = BaselineReader.Read(alias);

        Assert.Null(result.Error);
        Assert.Empty(result.Warnings);
        Assert.Equal(3, result.Variables.Count);
    }

    [FileSymbolicLinkFact]
    public void Read_FileSymbolicLinks_AreRefused()
    {
        string folder = Export(SampleVariables());
        string outsideFile = Path.Combine(_tempRoot, "outside.bin");
        File.WriteAllBytes(outsideFile, new byte[] { 0x05, 0x00 });
        BaselineEntry timeout = EntryFor(folder, "Timeout");
        string timeoutPath = Path.Combine(folder, timeout.File.Replace('/', Path.DirectorySeparatorChar));
        File.Delete(timeoutPath);
        Assert.True(TestLinks.TryCreateFileSymbolicLink(timeoutPath, outsideFile), "the file symbolic link could not be created");

        BaselineReadResult result = BaselineReader.Read(folder);

        Assert.Null(result.Error);
        Assert.Equal(2, result.Variables.Count);
        string warning = Assert.Single(result.Warnings);
        Assert.Contains($"'{timeout.File}' is a symbolic link or junction", warning, StringComparison.Ordinal);

        string indexPath = Path.Combine(folder, "index.json");
        string realIndex = Path.Combine(_tempRoot, "real-index.json");
        File.Move(indexPath, realIndex);
        Assert.True(TestLinks.TryCreateFileSymbolicLink(indexPath, realIndex));

        BaselineReadResult linked = BaselineReader.Read(folder);

        Assert.False(linked.Succeeded);
        Assert.Equal($"index.json in '{Path.GetFullPath(folder)}' is a symbolic link or junction, which a baseline folder must not contain; open the original folder instead.", linked.Error);
    }

    [Fact]
    public void TryFindLink_PlainFolder_FindsNothing()
    {
        string folder = Export(SampleVariables());
        BaselineEntry entry = EntryFor(folder, "BootOrder");
        Assert.True(BaselineReader.TryResolveFile(folder, entry.File, out string fullPath));

        Assert.False(BaselineReader.TryFindLink(folder, fullPath, out string? link));
        Assert.Null(link);
        Assert.False(BaselineReader.TryFindLink(folder, Path.Combine(folder, "vars", "missing.bin"), out _));
    }

    private static FirmwareVariable[] SampleVariables() => new[]
    {
        new FirmwareVariable("Timeout", Global, NvBsRt, new byte[] { 0x05, 0x00 }),
        new FirmwareVariable("BootOrder", Global, NvBsRt, new byte[] { 0x01, 0x00, 0x00, 0x00 }),
        new FirmwareVariable("SecureBoot", Global, VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess, new byte[] { 0x01 }),
    };

    private string Export(IReadOnlyList<FirmwareVariable> variables)
    {
        BaselineRequest request = new(
            _tempRoot,
            RunId + "-" + Guid.NewGuid().ToString("N").Substring(0, 8),
            variables,
            FirmwareType.Uefi,
            PrivilegeState.Enabled,
            null,
            null,
            null,
            null,
            "0.1.0",
            new DateTimeOffset(2026, 9, 22, 10, 15, 0, TimeSpan.FromHours(2)));
        BaselineExportResult result = new BaselineExporter().Export(request);
        Assert.Empty(result.Errors);
        return result.Directory;
    }

    private static BaselineEntry EntryFor(string folder, string name)
    {
        BaselineIndex index = JsonSerializer.Deserialize<BaselineIndex>(File.ReadAllText(Path.Combine(folder, "index.json")))!;
        return Assert.Single(index.Variables, e => e.Name == name);
    }

    private static void RewriteIndex(string folder, Func<BaselineIndex, BaselineIndex> change)
    {
        string path = Path.Combine(folder, "index.json");
        BaselineIndex index = JsonSerializer.Deserialize<BaselineIndex>(File.ReadAllText(path))!;
        File.WriteAllText(path, JsonSerializer.Serialize(change(index), new JsonSerializerOptions { WriteIndented = true }));
    }
}
