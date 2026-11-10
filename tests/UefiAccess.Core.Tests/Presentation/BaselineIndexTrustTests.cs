// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json.Nodes;
using UefiAccess.Core.Baseline;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Privileges;
using UefiAccess.Core.Variables;
using UefiAccess.Presentation.Resources;
using UefiAccess.Presentation.Services;
using UefiAccess.Presentation.ViewModels;
using Xunit;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// A baseline's index.json is untrusted input. Finding F092: its timestamp reaches the window
/// title and the Loaded sentence only when it is a valid ISO 8601 date and time, re-formatted to
/// whole seconds; anything else (not a date, hundreds of characters, line breaks) is left out with
/// one warning. Finding F094: a variable (vendor GUID and name) the index lists more than once is
/// shown once, from its first entry, with one warning that names the repeated keys.
/// </summary>
public sealed class BaselineIndexTrustTests : IDisposable
{
    private const VariableAttributes NvBsRt = VariableAttributes.NonVolatile | VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess;
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 10, 14, 30, 5, TimeSpan.FromHours(2));

    private readonly string _tempRoot;

    public BaselineIndexTrustTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "trust-" + Guid.NewGuid().ToString("N"));
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

    // ---- timestamps (F092) ----------------------------------------------------------------------

    [Theory]
    [InlineData("2026-09-10T14:30:05+02:00", "2026-09-10T12:30:05Z", "2026-09-10T14:30:05+02:00")]
    [InlineData(" 2026-09-10T14:30:05.1234567+02:00 ", "", "2026-09-10T14:30:05+02:00")]
    [InlineData("not a date", "2026-09-10T12:30:05Z", "2026-09-10T12:30:05Z")]
    [InlineData("", "2026-09-10T12:30:05.5Z", "2026-09-10T12:30:05Z")]
    [InlineData("2026-09-10 14:30", "yesterday", null)]
    [InlineData("2026-09-10T14:30:05", "2026-09-10T12:30:05", null)]
    [InlineData("2026-09-10T14:30:05+02:00\nInjected second line", "", null)]
    public void DisplayTimestamp_ShowsOnlyAValidTimestamp_ReFormatted(string local, string utc, string? expected)
    {
        BaselineIndex index = Index(local, utc);

        Assert.Equal(expected, BaselineFolderSource.DisplayTimestamp(index));
        Assert.Equal(
            expected is null ? Strings.BaselinePrefix + "D:\\b" : Strings.BaselinePrefix + "D:\\b (" + expected + ")",
            BaselineFolderSource.DescribeLoaded("D:\\b", index));
    }

    [Fact]
    public void AHugeTimestamp_NeverReachesTheDescription()
    {
        string huge = new('9', 5000);

        Assert.Equal(Strings.BaselinePrefix + "D:\\b", BaselineFolderSource.DescribeLoaded("D:\\b", Index(huge, huge)));
    }

    [Fact]
    public async Task AnUnreadableTimestamp_IsLeftOutOfTheTitleAndTheSentence_WithOneWarning()
    {
        string folder = Export(new[] { Global("BootOrder", 1, 0, 0, 0), Global("Timeout", 5, 0) });
        EditIndex(folder, index =>
        {
            index["TimestampLocal"] = "Tuesday\r\nat noon, give or take";
            index["TimestampUtc"] = "garbage";
        });

        SourceResult result = await new BaselineFolderSource(folder).LoadAsync(CancellationToken.None);

        Assert.Equal(SourceStatus.Loaded, result.Status);
        Assert.Equal(Strings.BaselinePrefix + folder, result.SourceDescription);
        Assert.Equal(new[] { Strings.BaselineTimestampUnreadable }, result.Warnings);

        ShellTestRig rig = new(null, path => new BaselineFolderSource(path));
        await rig.Shell.OpenBaselineAsync(folder);
        Assert.Equal("UefiAccess – " + Strings.BaselinePrefix + folder, rig.Shell.WindowTitle);
        Assert.Equal("Loaded 2 variables from " + Strings.BaselinePrefix + folder + " (InMemory)", rig.Shell.StatusText);
        Assert.Contains((LogViewModel.WarnLevel, Strings.BaselineTimestampUnreadable), rig.Lines);
    }

    [Fact]
    public async Task AnIndexWithoutATimestamp_LoadsWithoutAWarning()
    {
        string folder = Export(new[] { Global("BootOrder", 1, 0, 0, 0) });
        EditIndex(folder, index =>
        {
            index["TimestampLocal"] = null;
            index["TimestampUtc"] = " ";
        });

        SourceResult result = await new BaselineFolderSource(folder).LoadAsync(CancellationToken.None);

        Assert.Equal(Strings.BaselinePrefix + folder, result.SourceDescription);
        Assert.Empty(result.Warnings);
    }

    // ---- repeated keys (F094) -------------------------------------------------------------------

    [Fact]
    public async Task AKeyListedTwice_IsShownOnce_FromItsFirstEntry_WithOneWarning()
    {
        string folder = Export(new[] { Global("BootOrder", 1, 0, 0, 0), Global("Timeout", 5, 0) });
        EditIndex(folder, index =>
        {
            JsonArray variables = index["Variables"]!.AsArray();
            JsonNode timeout = variables.Single(entry => (string?)entry!["Name"] == "Timeout")!;
            variables.Add(timeout.DeepClone());
            index["VariableCount"] = variables.Count;
        });

        SourceResult result = await new BaselineFolderSource(folder).LoadAsync(CancellationToken.None);

        Assert.Equal(new[] { "BootOrder", "Timeout" }, result.Variables.Select(variable => variable.Name).OrderBy(name => name, StringComparer.Ordinal));
        string key = FirmwareVariable.MakeKey(KnownGuids.EfiGlobalVariable, "Timeout");
        Assert.Equal(new[] { "index.json lists 1 variable more than once; only the first entry of each is shown: " + key }, result.Warnings);

        ShellTestRig rig = new(null, path => new BaselineFolderSource(path));
        await rig.Shell.OpenBaselineAsync(folder);
        Assert.Equal(2, rig.Shell.List.TotalCount);
        Assert.StartsWith("Loaded 2 variables from ", rig.Shell.StatusText, StringComparison.Ordinal);
        Assert.Single(rig.Lines, line => line.Level == LogViewModel.WarnLevel);
    }

    [Fact]
    public void WithoutRepeatedKeys_KeepsTheFirstOfEach_InOrder_AndNamesAtMostFiveKeys()
    {
        List<FirmwareVariable> variables = new();
        for (int i = 0; i < 7; i++)
        {
            variables.Add(Global("Var" + i, (byte)i));
        }

        FirmwareVariable firstA = Global("A", 1);
        variables.Insert(0, firstA);
        variables.Add(Global("A", 2));
        variables.Add(Global("A", 3));
        for (int i = 0; i < 7; i++)
        {
            variables.Add(Global("Var" + i, 0xFF));
        }

        List<string> warnings = new();
        IReadOnlyList<FirmwareVariable> unique = BaselineFolderSource.WithoutRepeatedKeys(variables, warnings);

        Assert.Equal(8, unique.Count);
        Assert.Same(firstA, unique[0]);
        Assert.All(unique.Skip(1), variable => Assert.NotEqual(0xFF, variable.Data[0]));
        string warning = Assert.Single(warnings);
        Assert.StartsWith("index.json lists 8 variables more than once; only the first entry of each is shown: " + FirmwareVariable.MakeKey(KnownGuids.EfiGlobalVariable, "A") + ", ", warning, StringComparison.Ordinal);
        Assert.EndsWith(", and 3 more", warning, StringComparison.Ordinal);
        Assert.Equal(BaselineFolderSource.MaximumKeysNamed, warning.Split(", ").Length - 1);
    }

    [Fact]
    public void WithoutRepeatedKeys_TreatsNamesThatDifferInCaseAsDifferentVariables()
    {
        List<string> warnings = new();
        FirmwareVariable[] variables = { Global("Lang", 1), Global("lang", 2) };

        IReadOnlyList<FirmwareVariable> unique = BaselineFolderSource.WithoutRepeatedKeys(variables, warnings);

        Assert.Same(variables, unique);
        Assert.Empty(warnings);
    }

    private static FirmwareVariable Global(string name, params byte[] data) => new(name, KnownGuids.EfiGlobalVariable, NvBsRt, data);

    private static BaselineIndex Index(string? local, string? utc) => new("0.1.0", utc!, local!, "Uefi", "Enabled", null, 0, Array.Empty<BaselineEntry>());

    private static void EditIndex(string folder, Action<JsonObject> edit)
    {
        string path = Path.Combine(folder, BaselineExporter.IndexFileName);
        JsonObject index = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        edit(index);
        File.WriteAllText(path, index.ToJsonString());
    }

    private string Export(IReadOnlyList<FirmwareVariable> variables)
    {
        BaselineRequest request = new(
            _tempRoot,
            "run-" + Guid.NewGuid().ToString("N"),
            variables,
            FirmwareType.Uefi,
            PrivilegeState.Enabled,
            null,
            new { Note = "decoded" },
            new { Note = "platform" },
            "log line 1\n",
            "0.1.0",
            Timestamp);
        BaselineExportResult result = new BaselineExporter().Export(request);
        Assert.Empty(result.Errors);
        return result.Directory;
    }
}
