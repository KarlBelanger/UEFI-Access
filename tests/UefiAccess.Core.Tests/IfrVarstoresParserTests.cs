// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Cli.Services;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The ifr-varstores line parser: names are accepted with or without the double quotes
/// IFRExtractor-RS prints, sizes as decimal or as the 0x hexadecimal IFRExtractor-RS prints, and a
/// file that cannot be read is reported as one file-level problem.
/// </summary>
public sealed class IfrVarstoresParserTests
{
    [Theory]
    [InlineData("Setup", "Setup")]
    [InlineData("\"Setup\"", "Setup")]
    [InlineData("\" Setup \"", "Setup")]
    [InlineData("\"Setup", "\"Setup")]
    [InlineData("\"", "\"")]
    public void Unquote_RemovesOneSurroundingPairOnly(string name, string expected)
    {
        Assert.Equal(expected, IfrVarstores.Unquote(name));
    }

    [Fact]
    public void ParseLines_AcceptsQuotedNamesAndHexadecimalSizes()
    {
        IReadOnlyList<IfrVarstore> entries = IfrVarstores.ParseLines(
            new[]
            {
                "# copied from IFRExtractor-RS",
                "\"Setup\";EC87D643-EBA4-4BB5-A1E5-3F3E36B20DA9;0x2E5",
                "PchSetup;{4570B7F1-ADE8-4943-8DC3-406472842384};51",
                string.Empty,
                "SaSetup;72C5E28C-7783-43A1-8767-FAD73FCCAFA4;2E5",
                "\"\";72C5E28C-7783-43A1-8767-FAD73FCCAFA4;44",
            },
            out IReadOnlyList<string> errors);

        Assert.Equal(new[] { "Setup", "PchSetup" }, entries.Select(e => e.Name).ToArray());
        Assert.Equal(new[] { 741, 51 }, entries.Select(e => e.Size).ToArray());
        Assert.Equal(new[] { 2, 3 }, entries.Select(e => e.Line).ToArray());
        Assert.Equal(
            new[]
            {
                "line 5: '2E5' is not a size (decimal or 0x hexadecimal)",
                "line 6: empty varstore name",
            },
            errors);
    }

    [Fact]
    public void Parse_UnreadableFile_IsOneFileLevelProblem()
    {
        string path = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "no-such-folder-" + Guid.NewGuid().ToString("N"), "ifr-varstores.txt");

        IReadOnlyList<IfrVarstore> entries = IfrVarstores.Parse(path, out IReadOnlyList<string> errors);

        Assert.Empty(entries);
        string error = Assert.Single(errors);
        Assert.StartsWith("cannot read '" + path + "': DirectoryNotFoundException: ", error, StringComparison.Ordinal);
        Assert.False(IfrVarstores.TryReadLines(path, out IReadOnlyList<string> lines, out string? readError));
        Assert.Empty(lines);
        Assert.StartsWith("DirectoryNotFoundException: ", readError, StringComparison.Ordinal);
    }
}
