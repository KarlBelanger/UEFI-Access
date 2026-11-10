// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using System.Text.Json;
using UefiAccess.Core.Baseline;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Privileges;
using UefiAccess.Core.Variables;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// Findings R13 and R19: the 1 MiB limit bounds one entry only, so a crafted baseline folder could
/// list thousands of entries that all name the same valid 1 MiB file and make the elevated
/// application hold gigabytes, or hand it an index.json of any size. A load is now bounded as a
/// whole: index.json at most <see cref="BaselineReader.MaximumIndexSize"/> (4 MiB), at most
/// <see cref="BaselineReader.MaximumEntryCount"/> (4,096) entries, and entry sizes adding up to at
/// most <see cref="BaselineReader.MaximumTotalSize"/> (64 MiB). Each limit refuses the whole folder
/// with one sentence before any variable file is read.
/// </summary>
public sealed class BaselineReaderLimitTests : IDisposable
{
    private const string RunId = "20260922-101500";
    private const VariableAttributes NvBsRt = VariableAttributes.NonVolatile | VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess;

    private readonly string _tempRoot;

    public BaselineReaderLimitTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "reader-limits-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        TestFolders.DeleteQuietly(_tempRoot);
    }

    [Fact]
    public void Limits_AreFourMebibytesOfIndex_4096Entries_And64MebibytesOfData()
    {
        Assert.Equal(4 * 1024 * 1024, BaselineReader.MaximumIndexSize);
        Assert.Equal(4096, BaselineReader.MaximumEntryCount);
        Assert.Equal(64 * 1024 * 1024, BaselineReader.MaximumTotalSize);
        Assert.True(BaselineReader.MaximumEntrySize < BaselineReader.MaximumTotalSize);
    }

    [Fact]
    public void Limits_LeaveRoomForARealExport()
    {
        // An index entry of an export takes some 400 bytes; a full index of the largest entry count
        // still fits the index limit with room to spare.
        FirmwareVariable[] variables = Enumerable.Range(0, 64)
            .Select(i => new FirmwareVariable("Boot" + i.ToString("X4", System.Globalization.CultureInfo.InvariantCulture), KnownGuids.EfiGlobalVariable, NvBsRt, new byte[] { 0x01, 0x00, 0x00, 0x00 }))
            .ToArray();
        string folder = Export(variables);
        long indexLength = new FileInfo(Path.Combine(folder, "index.json")).Length;

        Assert.True(indexLength * BaselineReader.MaximumEntryCount / variables.Length < BaselineReader.MaximumIndexSize / 2, indexLength.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Null(BaselineReader.Read(folder).Error);
    }

    [Fact]
    public void Read_IndexAboveTheLimit_IsRefusedWhole()
    {
        string folder = Export(SampleVariables());
        string indexPath = Path.Combine(folder, "index.json");
        PadTo(indexPath, BaselineReader.MaximumIndexSize + 1);

        BaselineReadResult result = BaselineReader.Read(folder);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Variables);
        Assert.Equal($"index.json in '{Path.GetFullPath(folder)}' is 4194305 bytes, more than the 4194304-byte limit for a baseline index, so nothing was read.", result.Error);
    }

    [Fact]
    public void Read_IndexExactlyAtTheLimit_ReadsBack()
    {
        string folder = Export(SampleVariables());
        PadTo(Path.Combine(folder, "index.json"), BaselineReader.MaximumIndexSize);

        BaselineReadResult result = BaselineReader.Read(folder);

        Assert.Null(result.Error);
        Assert.Empty(result.Warnings);
        Assert.Equal(3, result.Variables.Count);
    }

    [Fact]
    public void Read_IndexWithAByteOrderMark_IsStillRead()
    {
        string folder = Export(SampleVariables());
        string indexPath = Path.Combine(folder, "index.json");
        File.WriteAllText(indexPath, File.ReadAllText(indexPath), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        BaselineReadResult result = BaselineReader.Read(folder);

        Assert.Null(result.Error);
        Assert.Equal(3, result.Variables.Count);
    }

    [Fact]
    public void Read_MoreEntriesThanTheLimit_IsRefusedWhole()
    {
        string folder = Export(SampleVariables());
        RewriteIndex(folder, index =>
        {
            BaselineEntry template = index.Variables[0];
            BaselineEntry[] many = Enumerable.Range(0, BaselineReader.MaximumEntryCount + 1)
                .Select(i => template with { Name = "Var" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), Key = string.Empty })
                .ToArray();
            return index with { Variables = many, VariableCount = many.Length };
        });

        BaselineReadResult result = BaselineReader.Read(folder);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Variables);
        Assert.Equal($"index.json in '{Path.GetFullPath(folder)}' lists 4097 entries, more than the 4096 a baseline can hold, so nothing was read.", result.Error);
    }

    [Fact]
    public void Read_ManyEntriesNamingTheSameLargeFile_AreRefusedBeforeAnyIsRead()
    {
        // The crafted folder of R13: one valid 1 MiB file, and a hundred entries that each name it
        // with its correct size and hash under a different variable name.
        byte[] large = new byte[BaselineReader.MaximumEntrySize];
        new Random(7020).NextBytes(large);
        string folder = Export(new[] { new FirmwareVariable("Large", KnownGuids.EfiGlobalVariable, NvBsRt, large) });
        RewriteIndex(folder, index =>
        {
            BaselineEntry template = index.Variables[0];
            BaselineEntry[] copies = Enumerable.Range(0, 100)
                .Select(i => template with { Name = "Large" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), Key = string.Empty })
                .ToArray();
            return index with { Variables = copies, VariableCount = copies.Length };
        });

        BaselineReadResult result = BaselineReader.Read(folder);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Variables);
        Assert.Equal($"The entries in index.json in '{Path.GetFullPath(folder)}' add up to 104857600 bytes, more than the 67108864-byte limit for one baseline, so nothing was read.", result.Error);
    }

    [Fact]
    public void Read_SixtyFourEntriesOfOneMebibyte_AreRead()
    {
        // Exactly at the total limit: 64 entries of 1 MiB, all naming one file, read and kept.
        byte[] large = new byte[BaselineReader.MaximumEntrySize];
        new Random(7020).NextBytes(large);
        string folder = Export(new[] { new FirmwareVariable("Large", KnownGuids.EfiGlobalVariable, NvBsRt, large) });
        RewriteIndex(folder, index =>
        {
            BaselineEntry template = index.Variables[0];
            BaselineEntry[] copies = Enumerable.Range(0, 64)
                .Select(i => template with { Name = "Large" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), Key = FirmwareVariable.MakeKey(KnownGuids.EfiGlobalVariable, "Large" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)) })
                .ToArray();
            return index with { Variables = copies, VariableCount = copies.Length };
        });

        BaselineReadResult result = BaselineReader.Read(folder);

        Assert.Null(result.Error);
        Assert.Empty(result.Warnings);
        Assert.Equal(64, result.Variables.Count);
    }

    [Fact]
    public void CheckLoadLimits_CountsOnlyEntriesThatWouldBeRead()
    {
        string directory = Path.Combine(_tempRoot, "any");
        BaselineEntry entry = new("k", "n", Guid.Empty.ToString("D"), 7, "NV BS RT", BaselineReader.MaximumEntrySize, "00", "vars/x.bin");
        List<BaselineEntry?> entries = Enumerable.Repeat<BaselineEntry?>(entry, 64).ToList();

        Assert.Null(BaselineReader.CheckLoadLimits(entries, directory));

        // Skipped unread, so not counted: a null entry, a negative size, a size above the entry limit.
        entries.Add(null);
        entries.Add(entry with { Size = -1 });
        entries.Add(entry with { Size = BaselineReader.MaximumEntrySize + 1 });
        entries.Add(entry with { Size = int.MaxValue });
        Assert.Null(BaselineReader.CheckLoadLimits(entries, directory));

        entries.Add(entry with { Size = 1 });
        Assert.Equal($"The entries in index.json in '{directory}' add up to 67108865 bytes, more than the 67108864-byte limit for one baseline, so nothing was read.", BaselineReader.CheckLoadLimits(entries, directory));
    }

    [Fact]
    public void CheckLoadLimits_AcceptsExactlyTheMaximumEntryCount()
    {
        BaselineEntry entry = new("k", "n", Guid.Empty.ToString("D"), 7, "NV BS RT", 0, "00", "vars/x.bin");

        Assert.Null(BaselineReader.CheckLoadLimits(Enumerable.Repeat<BaselineEntry?>(entry, BaselineReader.MaximumEntryCount).ToList(), "d"));
        Assert.Equal(
            "index.json in 'd' lists 4097 entries, more than the 4096 a baseline can hold, so nothing was read.",
            BaselineReader.CheckLoadLimits(Enumerable.Repeat<BaselineEntry?>(entry, BaselineReader.MaximumEntryCount + 1).ToList(), "d"));
    }

    private static FirmwareVariable[] SampleVariables() => new[]
    {
        new FirmwareVariable("Timeout", KnownGuids.EfiGlobalVariable, NvBsRt, new byte[] { 0x05, 0x00 }),
        new FirmwareVariable("BootOrder", KnownGuids.EfiGlobalVariable, NvBsRt, new byte[] { 0x01, 0x00, 0x00, 0x00 }),
        new FirmwareVariable("SecureBoot", KnownGuids.EfiGlobalVariable, VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess, new byte[] { 0x01 }),
    };

    /// <summary>Appends spaces (valid trailing JSON whitespace) until the file is exactly <paramref name="length"/> bytes.</summary>
    private static void PadTo(string path, int length)
    {
        long current = new FileInfo(path).Length;
        Assert.True(current <= length);
        using FileStream stream = new(path, FileMode.Append, FileAccess.Write);
        byte[] spaces = new byte[length - current];
        Array.Fill(spaces, (byte)' ');
        stream.Write(spaces);
    }

    private static void RewriteIndex(string folder, Func<BaselineIndex, BaselineIndex> change)
    {
        string path = Path.Combine(folder, "index.json");
        BaselineIndex index = JsonSerializer.Deserialize<BaselineIndex>(File.ReadAllText(path))!;
        File.WriteAllText(path, JsonSerializer.Serialize(change(index)));
    }

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
}
