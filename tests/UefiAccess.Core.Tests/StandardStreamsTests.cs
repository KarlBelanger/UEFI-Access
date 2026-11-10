// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using System.Text.Json;
using UefiAccess.Cli.Output;
using UefiAccess.Core.Tests.Fixtures;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The writer the CLI puts on a redirected standard output or standard error: UTF-8 without a
/// byte order mark, lossless for names outside any OEM code page (the parser fixture's Japanese
/// variable name), and a --json document written through it parses back unchanged. The console
/// half of <see cref="StandardStreams.Configure"/> (UTF-16 through WriteConsoleW) needs a real
/// console and is not exercised here.
/// </summary>
public sealed class StandardStreamsTests
{
    /// <summary>The Japanese fixture name and a French access-denied text (accented letters outside ASCII).</summary>
    private static readonly string Sample = EnumerationBuffers.JapaneseName + " Acc" + (char)0xE8 + "s refus" + (char)0xE9 + ".";

    [Fact]
    public void RedirectedWriter_IsUtf8WithoutAByteOrderMark()
    {
        using MemoryStream stream = new();
        using (TextWriter writer = StandardStreams.CreateRedirectedWriter(stream))
        {
            writer.Write(Sample);
        }

        byte[] bytes = stream.ToArray();
        Assert.Empty(StandardStreams.Utf8NoBom.GetPreamble());
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "no byte order mark");
        Assert.Equal(Encoding.UTF8.GetBytes(Sample), bytes);
    }

    [Fact]
    public void RedirectedWriter_LeavesTheStreamOpen_AndFlushesEveryWrite()
    {
        using MemoryStream stream = new();
        TextWriter writer = StandardStreams.CreateRedirectedWriter(stream);
        writer.Write("a");
        Assert.Equal(1, stream.Length);
        writer.Dispose();
        Assert.True(stream.CanWrite);
    }

    [Fact]
    public void JsonDocumentThroughTheRedirectedWriter_RoundTripsANonAsciiName()
    {
        using MemoryStream stream = new();
        using (TextWriter writer = StandardStreams.CreateRedirectedWriter(stream))
        {
            JsonOutput.Write(writer, new { key = "8be4df61-93ca-11d2-aa0d-00e098032b8c:" + EnumerationBuffers.JapaneseName });
        }

        using JsonDocument document = JsonDocument.Parse(stream.ToArray());
        Assert.Equal("8be4df61-93ca-11d2-aa0d-00e098032b8c:" + EnumerationBuffers.JapaneseName, document.RootElement.GetProperty("key").GetString());
    }
}
