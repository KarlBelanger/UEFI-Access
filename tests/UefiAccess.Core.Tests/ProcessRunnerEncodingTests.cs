// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UefiAccess.Core.Native;
using UefiAccess.Core.Platform;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// How <see cref="ProcessRunner"/> decodes a child's output and how the Secure Boot cross-check
/// starts PowerShell. A child started without a window writes in the system's OEM code page (its
/// own new console starts with it), so the runner decodes with that code page and never with the
/// parent's console encoding, which "chcp 65001" or a UTF-8 console setting may have changed.
/// PowerShell is switched to UTF-8 inside its script and decoded as UTF-8, and it is started by
/// its absolute System32 path only, never by a bare name that Windows would look up in the
/// current directory first. Both output streams are checked, each with its own text. Only cmd.exe
/// is started here (to echo text and to report its console's code page); PowerShell never is. The
/// decoding check needs a word outside ASCII that the OEM code page can write, and is reported as
/// skipped, not passed, where there is none.
/// </summary>
public sealed class ProcessRunnerEncodingTests : IDisposable
{
    private readonly string _tempRoot;

    public ProcessRunnerEncodingTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "runner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // Best effort; a leftover temp folder must not fail the test run.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort; a leftover temp folder must not fail the test run.
        }
    }

    [Theory]
    [InlineData(437)]
    [InlineData(850)]
    [InlineData(1252)]
    [InlineData(65001)]
    public void EncodingForCodePage_ReturnsThatCodePage(int codePage)
    {
        Assert.Equal(codePage, ProcessRunner.EncodingForCodePage(codePage).CodePage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(123456)]
    public void EncodingForCodePage_UnknownCodePage_IsUtf8WithoutPreamble(int codePage)
    {
        Encoding encoding = ProcessRunner.EncodingForCodePage(codePage);

        Assert.Equal(65001, encoding.CodePage);
        Assert.Empty(encoding.GetPreamble());
    }

    [Fact]
    public void ChildConsoleEncoding_IsTheSystemOemCodePage()
    {
        Assert.Equal((int)Kernel32.GetOEMCP(), ProcessRunner.ChildConsoleEncoding().CodePage);
    }

    [OemCodePageWordFact]
    public void ChildOutput_IsDecodedWithTheCodePageTheChildWrites()
    {
        // A word outside ASCII that the OEM code page can write (the fact is skipped where there is
        // none), echoed once to each stream: "echo word& echo word>&2".
        string word = OemCodePageWordFactAttribute.Word!;
        int oemCodePage = ProcessRunner.ChildConsoleEncoding().CodePage;

        // The runner's premise, checked first so that a machine where it does not hold says so
        // instead of showing a garbled word: a console child starts with the OEM code page. A
        // console setting for new consoles (a CodePage value under HKCU\Console) would change that.
        ProcessRunResult chcp = RunCmd(null, "chcp");
        Assert.True(chcp.Started, chcp.Error);
        Match active = Regex.Match(chcp.StandardOutput, @"(\d+)\D*$");
        Assert.True(
            active.Success && active.Groups[1].Value == oemCodePage.ToString(CultureInfo.InvariantCulture),
            $"cmd.exe in a new console reports \"{chcp.StandardOutput}\", not the OEM code page {oemCodePage}: on this machine ProcessRunner decodes a child's output with the wrong code page.");

        ProcessRunResult run = RunCmd(null, "echo", word + "&", "echo", word + ">&2");

        Assert.True(run.Started, run.Error);
        Assert.False(run.TimedOut);
        Assert.Equal(word, run.StandardOutput);
        Assert.Equal(word, run.StandardError);
    }

    [Fact]
    public void ExplicitEncoding_IsUsedForBothStreams()
    {
        // cmd.exe writes "abcd" to standard output and "wxyz" to standard error, one byte per
        // letter in any OEM code page, each followed by CR LF. Read as UTF-16, every two bytes
        // become one character, so each stream reads as exactly that garbled text only when the
        // explicit encoding, not a default, reached its reader.
        ProcessRunResult run = RunCmd(Encoding.Unicode, "echo", "abcd&", "echo", "wxyz>&2");

        Assert.True(run.Started, run.Error);
        Assert.False(run.TimedOut);
        Assert.Equal(AsUtf16("abcd\r\n"), run.StandardOutput);
        Assert.Equal(AsUtf16("wxyz\r\n"), run.StandardError);
    }

    [Fact]
    public void OemCodePageWord_IsOnlyAWordTheCodePageWritesUnchanged()
    {
        // The attribute behind the test above: a word the code page only approximates ("cafe" or
        // "caf?" for "café" in the Cyrillic and Greek OEM code pages) does not count, and an
        // ASCII-only encoding has no word at all.
        Assert.Equal("café", OemCodePageWordFactAttribute.FindWord(ProcessRunner.EncodingForCodePage(850)));
        Assert.Equal("Привет", OemCodePageWordFactAttribute.FindWord(ProcessRunner.EncodingForCodePage(866)));
        Assert.Equal("Ωμεγα", OemCodePageWordFactAttribute.FindWord(ProcessRunner.EncodingForCodePage(737)));
        Assert.Null(OemCodePageWordFactAttribute.FindWord(Encoding.ASCII));
    }

    [Fact]
    public void PowerShellPath_IsAbsoluteUnderSystem32()
    {
        string? path = SecureBootPolicyInspector.ResolvePowerShellPath();

        Assert.NotNull(path);
        Assert.True(Path.IsPathFullyQualified(path), path);
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"), path, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("System32")]
    [InlineData(@"relative\System32")]
    public void PowerShellPath_WithoutAnAbsoluteSystemFolder_IsNullNeverABareName(string? systemDirectory)
    {
        Assert.Null(SecureBootPolicyInspector.ResolvePowerShellPath(systemDirectory));
    }

    [Fact]
    public void PowerShellPath_MissingFromTheSystemFolder_IsNull()
    {
        Assert.Null(SecureBootPolicyInspector.ResolvePowerShellPath(_tempRoot));

        string folder = Path.Combine(_tempRoot, "WindowsPowerShell", "v1.0");
        Directory.CreateDirectory(folder);
        string executable = Path.Combine(folder, "powershell.exe");
        File.WriteAllBytes(executable, Array.Empty<byte>());

        Assert.Equal(executable, SecureBootPolicyInspector.ResolvePowerShellPath(_tempRoot));
    }

    [Fact]
    public void PowerShellScript_SwitchesToUtf8BeforeTheCmdlet()
    {
        Assert.Equal("[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; Confirm-SecureBootUEFI", SecureBootPolicyInspector.PowerShellScript);
    }

    /// <summary>Runs "cmd.exe /d /c" with the given command words through the runner, decoding with the given encoding or, when null, through the overload that uses the runner's default.</summary>
    private static ProcessRunResult RunCmd(Encoding? encoding, params string[] command)
    {
        string cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        string[] arguments = new[] { "/d", "/c" }.Concat(command).ToArray();
        return encoding is null
            ? ProcessRunner.Run(cmd, null, TimeSpan.FromSeconds(30), Serilog.Core.Logger.None, arguments)
            : ProcessRunner.Run(cmd, null, TimeSpan.FromSeconds(30), encoding, Serilog.Core.Logger.None, arguments);
    }

    /// <summary>What text written one byte per character reads as when decoded as UTF-16, trimmed as the runner trims.</summary>
    private static string AsUtf16(string asciiText) => Encoding.Unicode.GetString(Encoding.ASCII.GetBytes(asciiText)).Trim();
}
