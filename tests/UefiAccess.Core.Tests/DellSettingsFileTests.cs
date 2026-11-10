// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using UefiAccess.Core.Baseline;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Privileges;
using UefiAccess.Core.Settings;
using UefiAccess.Core.Settings.Dell;
using UefiAccess.Core.Tests.Fixtures;
using UefiAccess.Core.Variables;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// dell-settings.json, the Dell BIOS settings of a baseline folder: what <see cref="DellSettingsFile.ToDocument"/> keeps of a read (tokens, Dell's
/// names, the machine's three fields, states and problem kinds; no display text, no note, no
/// detail), and how <see cref="DellSettingsFile.Read(string)"/> takes the file back as untrusted
/// input: a missing file is not an error; a link or junction, a file over 256 KiB, another format,
/// malformed JSON and a file over one of its limits are refused whole, never cut, and never throw;
/// control characters are stripped; and every text is derived again through the current catalog.
/// The reads are made by the providers' pure functions over the fixtures, so nothing here reads
/// WMI or Dell Command | Monitor or starts Dell Command | Configure on this PC, the OptiPlex 7020
/// itself (DellSettingsGuardTests.TestsNeverRunTheRealDellReader). The files go to a temporary
/// folder that is deleted afterwards. File symbolic links need Developer Mode or the
/// symbolic-link privilege, so that check is reported as skipped where the host cannot create one;
/// junctions need no privilege.
/// </summary>
public sealed class DellSettingsFileTests : IDisposable
{
    private const DellSettingsSource DcmSource = DellSettingsSource.DellCommandMonitor;
    private const DellSettingsSource CctkSource = DellSettingsSource.DellCommandConfigure;

    private static readonly IReadOnlyList<DellCatalogEntry> Catalog = DellAttributeCatalog.Default;

    private static readonly DateTimeOffset StartedAt = new(2026, 10, 5, 9, 12, 44, TimeSpan.FromHours(2));

    private static readonly DellMachine Unit = new("Dell Inc.", "OptiPlex 7020", "A18");

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly DellSettingsFileResult NotPresent = new(false, null, null);

    private readonly string _tempRoot;

    public DellSettingsFileTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "dell-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        TestFolders.DeleteQuietly(_tempRoot);
    }

    [Theory]
    [InlineData("UnitA18")]
    [InlineData("TpmDisabled")]
    [InlineData("UnknownCode")]
    [InlineData("SeveralCodes")]
    [InlineData("UnmappedName")]
    [InlineData("WithoutSecureBootCurrent")]
    [InlineData("WithoutNumLock")]
    [InlineData("OtherBios")]
    [InlineData("Fallback")]
    [InlineData("NothingInstalled")]
    [InlineData("NotADell")]
    public void RoundTrip_ToDocumentThenRead(string read)
    {
        DellSettingsResult original = read switch
        {
            "UnitA18" => DcmResult(DcmRowFixtures.UnitA18()),
            "TpmDisabled" => DcmResult(DcmRowFixtures.WithTpmDisabled()),
            "UnknownCode" => DcmResult(DcmRowFixtures.WithUnknownCode()),
            "SeveralCodes" => DcmResult(DcmRowFixtures.WithSeveralCodes()),
            "UnmappedName" => DcmResult(DcmRowFixtures.WithUnmappedName()),
            "WithoutSecureBootCurrent" => DcmResult(DcmRowFixtures.WithoutSecureBootCurrent()),
            "WithoutNumLock" => DcmResult(DcmRowFixtures.WithoutNumLock()),
            "OtherBios" => DcmResult(DcmRowFixtures.UnitA18(), new DellMachine("Dell Inc.", "OptiPlex 7020", "A17")),
            "Fallback" => FallbackResult(),
            "NothingInstalled" => NothingInstalledResult(),
            "NotADell" => NotADellResult(),
            _ => throw new ArgumentOutOfRangeException(nameof(read)),
        };

        // Written by the exporter as a baseline would be, then read back from its folder.
        BaselineExportResult export = new BaselineExporter().Export(Request(DellSettingsFile.ToDocument(original)));
        Assert.Empty(export.Errors);
        DellSettingsFileResult file = DellSettingsFile.Read(export.Directory);

        Assert.True(file.Present);
        Assert.Null(file.Problem);
        DellSettingsResult copy = Assert.IsType<DellSettingsResult>(file.Settings);
        Assert.Equal(original.Outcome, copy.Outcome);
        Assert.Equal(original.Source, copy.Source);
        Assert.Equal(original.ReadAt, copy.ReadAt);
        Assert.Equal(TimeSpan.Zero, copy.ReadAt.Offset);
        Assert.Equal(0, copy.ElapsedMs);
        Assert.Equal(original.Machine, copy.Machine);
        Assert.Equal(original.Settings.Count, copy.Settings.Count);
        for (int i = 0; i < original.Settings.Count; i++)
        {
            AssertSameSetting(original.Settings[i], copy.Settings[i]);
        }

        Assert.Equal(original.Problems.Select(problem => (problem.Interface, problem.Kind)), copy.Problems.Select(problem => (problem.Interface, problem.Kind)));
        Assert.Equal(original.Problems.Select(DellSentences.Cause), copy.Problems.Select(DellSentences.Cause));
        Assert.Equal(original.Warnings, copy.Warnings);
        Assert.Equal(original.FallbackSentence, copy.FallbackSentence);
        Assert.Equal(original.NativeInterfacePresent, copy.NativeInterfacePresent);
        Assert.Equal(DellSentences.UnavailableSentence(original), DellSentences.UnavailableSentence(copy));
        Assert.Null(copy.AllAttributes);
        if (read == "SeveralCodes")
        {
            // The joined codes are split again, and each is named as before.
            Assert.Equal(new SettingValue("1, 2", "Disable, Enable", "Disabled, Enabled"), Setting(copy, "NumLock").Current);
        }
    }

    [Fact]
    public void Missing_IsNotPresentNotAnError()
    {
        string empty = NewFolder();
        string baselineWithoutIt = NewFolder();
        File.WriteAllText(Path.Combine(baselineWithoutIt, BaselineExporter.IndexFileName), "{ \"Variables\": [] }", Utf8NoBom);

        Assert.Equal(NotPresent, DellSettingsFile.Read(empty));
        Assert.Equal(NotPresent, DellSettingsFile.Read(baselineWithoutIt));
        Assert.Equal(NotPresent, DellSettingsFile.Read(Path.Combine(_tempRoot, "no-such-folder")));
        Assert.Equal(NotPresent, DellSettingsFile.Read(" "));
        Assert.Equal(NotPresent, DellSettingsFile.Read(null!));
    }

    [FileSymbolicLinkFact]
    public void RefusesALinkOrJunction()
    {
        // A file symbolic link that would make the elevated application read a file elsewhere.
        string outside = NewFolder();
        string target = Path.Combine(outside, "elsewhere.json");
        File.WriteAllText(target, Json(DellSettingsFile.ToDocument(DcmResult(DcmRowFixtures.UnitA18()))), Utf8NoBom);
        string folder = NewFolder();
        Assert.True(TestLinks.TryCreateFileSymbolicLink(Path.Combine(folder, BaselineExporter.DellSettingsFileName), target), "the file symbolic link could not be created");

        Assert.Equal(new DellSettingsFileResult(true, null, DellSettingsFile.LinkProblem), DellSettingsFile.Read(folder));

        // A link whose target is gone is refused as a link, not taken as a missing file.
        File.Delete(target);
        Assert.Equal(new DellSettingsFileResult(true, null, DellSettingsFile.LinkProblem), DellSettingsFile.Read(folder));

        // A junction in the file's place.
        string junctionFolder = NewFolder();
        Assert.True(TestLinks.TryCreateJunction(Path.Combine(junctionFolder, BaselineExporter.DellSettingsFileName), outside), "mklink /J could not create the junction");
        Assert.Equal(new DellSettingsFileResult(true, null, DellSettingsFile.LinkProblem), DellSettingsFile.Read(junctionFolder));
    }

    [Fact]
    public void RefusesAJunctionInPlaceOfTheFile()
    {
        // Junctions need no privilege, so this part of the link check runs on every host.
        string outside = NewFolder();
        File.WriteAllText(Path.Combine(outside, BaselineExporter.DellSettingsFileName), Json(DellSettingsFile.ToDocument(DcmResult(DcmRowFixtures.UnitA18()))), Utf8NoBom);
        string folder = NewFolder();
        Assert.True(TestLinks.TryCreateJunction(Path.Combine(folder, BaselineExporter.DellSettingsFileName), outside), "mklink /J could not create the junction");

        Assert.Equal(new DellSettingsFileResult(true, null, DellSettingsFile.LinkProblem), DellSettingsFile.Read(folder));

        // A plain folder of that name is no link, and no file either.
        string plain = NewFolder();
        Directory.CreateDirectory(Path.Combine(plain, BaselineExporter.DellSettingsFileName));
        Assert.Equal(new DellSettingsFileResult(true, null, DellSettingsFile.UnreadableProblem), DellSettingsFile.Read(plain));
    }

    [Fact]
    public void RefusesAFileOver256KiB()
    {
        Assert.Equal(256 * 1024, DellSettingsFile.MaximumFileSize);
        string json = Json(DellSettingsFile.ToDocument(DcmResult(DcmRowFixtures.UnitA18())));
        int padding = DellSettingsFile.MaximumFileSize - Encoding.UTF8.GetByteCount(json);
        Assert.True(padding > 0);

        // Whitespace after the document is valid JSON, so exactly the limit is read.
        DellSettingsFileResult atLimit = ReadText(json + new string(' ', padding));
        Assert.Null(atLimit.Problem);
        Assert.Equal(DellReadOutcome.Complete, atLimit.Settings!.Outcome);

        Assert.Equal(new DellSettingsFileResult(true, null, DellSettingsFile.TooLargeProblem), ReadText(json + new string(' ', padding + 1)));
        Assert.Equal(new DellSettingsFileResult(true, null, DellSettingsFile.TooLargeProblem), ReadText(json + new string(' ', 4 * DellSettingsFile.MaximumFileSize)));
    }

    [Fact]
    public void RefusesMoreThan64Settings()
    {
        Assert.Equal(64, DellSettingsFile.MaximumSettings);
        JsonObject root = UnitA18Node();
        JsonArray settings = root["Settings"]!.AsArray();
        Assert.Equal(10, settings.Count);
        for (int i = settings.Count; i < DellSettingsFile.MaximumSettings; i++)
        {
            settings.Add(OtherSetting("Extra" + i.ToString("D2", CultureInfo.InvariantCulture)));
        }

        DellSettingsFileResult atLimit = ReadText(root.ToJsonString());
        Assert.Null(atLimit.Problem);
        Assert.Equal(DellReadOutcome.Complete, atLimit.Settings!.Outcome);
        Assert.Equal(DcmRowFixtures.UnitA18Texts, atLimit.Settings.Settings.Select(DellSentences.ValueText));

        settings.Add(OtherSetting("Extra64"));
        Assert.Equal(new DellSettingsFileResult(true, null, DellSettingsFile.TooManyEntriesProblem), ReadText(root.ToJsonString()));
    }

    [Fact]
    public void RefusesMoreThan32PossibleValues()
    {
        Assert.Equal(32, DellSettingsFile.MaximumPossibleValues);
        JsonObject root = UnitA18Node();
        JsonArray possible = SettingNode(root, "NumLock")["PossibleValues"]!.AsArray();
        possible.Clear();
        for (int i = 1; i <= DellSettingsFile.MaximumPossibleValues; i++)
        {
            possible.Add(Value(i.ToString(CultureInfo.InvariantCulture), i == 2 ? "Enable" : "Choice " + i.ToString(CultureInfo.InvariantCulture)));
        }

        DellSettingsFileResult atLimit = ReadText(root.ToJsonString());
        Assert.Null(atLimit.Problem);
        FirmwareSetting numLock = Setting(atLimit, "NumLock");
        Assert.Equal("Enabled", numLock.Current!.Text);
        Assert.Equal(32, numLock.PossibleValues.Count);

        possible.Add(Value("33", "Choice 33"));
        Assert.Equal(new DellSettingsFileResult(true, null, DellSettingsFile.TooManyEntriesProblem), ReadText(root.ToJsonString()));
    }

    [Fact]
    public void RefusesMoreThan32Problems()
    {
        Assert.Equal(32, DellSettingsFile.MaximumProblems);
        JsonObject root = Node(NothingInstalledResult());
        JsonArray problems = root["Problems"]!.AsArray();
        problems.Clear();
        for (int i = 0; i < DellSettingsFile.MaximumProblems; i++)
        {
            problems.Add(new JsonObject { ["Interface"] = "DellCommandMonitor", ["Kind"] = "NotInstalled" });
        }

        DellSettingsFileResult atLimit = ReadText(root.ToJsonString());
        Assert.Null(atLimit.Problem);
        Assert.Equal(32, atLimit.Settings!.Problems.Count);

        problems.Add(new JsonObject { ["Interface"] = "DellCommandMonitor", ["Kind"] = "NotInstalled" });
        Assert.Equal(new DellSettingsFileResult(true, null, DellSettingsFile.TooManyEntriesProblem), ReadText(root.ToJsonString()));
    }

    [Fact]
    public void RefusesATextOver64Characters()
    {
        Assert.Equal(64, DellSettingsFile.MaximumTextLength);
        string sixtyFour = new('x', 64);
        string sixtyFive = new('x', 65);
        DellSettingsFileResult tooLong = new(true, null, DellSettingsFile.TextTooLongProblem);

        // A name of exactly 64 characters is shown whole, as Dell's own word.
        JsonObject atLimit = UnitA18Node();
        SettingNode(atLimit, "NumLock")["PossibleValues"]![1]!["VendorName"] = sixtyFour;
        FirmwareSetting numLock = Setting(ReadText(atLimit.ToJsonString()), "NumLock");
        Assert.Equal(sixtyFour, numLock.Current!.Text);
        Assert.True(numLock.DisplayFromVendor);

        // One character more anywhere refuses the whole file; nothing is cut.
        foreach (Action<JsonObject> change in new Action<JsonObject>[]
        {
            root => SettingNode(root, "NumLock")["PossibleValues"]![1]!["VendorName"] = sixtyFive,
            root => SettingNode(root, "NumLock")["PossibleValues"]![1]!["Token"] = sixtyFive,
            root => SettingNode(root, "NumLock")["CurrentToken"] = sixtyFive,
            root => SettingNode(root, "NumLock")["CurrentVendorName"] = sixtyFive,
            root => SettingNode(root, "NumLock")["Id"] = sixtyFive,
            root => SettingNode(root, "NumLock")["State"] = sixtyFive,
            root => root["Machine"]!["Model"] = sixtyFive,
            root => root["Machine"]!["Manufacturer"] = sixtyFive,
            root => root["Machine"]!["BiosVersion"] = sixtyFive,
            root => root["Outcome"] = sixtyFive,
            root => root["Problems"]!.AsArray().Add(new JsonObject { ["Interface"] = "DellCommandMonitor", ["Kind"] = sixtyFive }),

            // The limit counts the characters as written, control characters included.
            root => SettingNode(root, "NumLock")["PossibleValues"]![1]!["VendorName"] = new string('y', 60) + "\u0001\u0002\u0003\u0004\u0005",
        })
        {
            JsonObject root = UnitA18Node();
            change(root);
            Assert.Equal(tooLong, ReadText(root.ToJsonString()));
        }
    }

    [Theory]
    [InlineData("uefiaccess-dell-settings/2")]
    [InlineData("UEFIACCESS-DELL-SETTINGS/1")]
    [InlineData("uefiaccess-dell-settings/1 ")]
    [InlineData("")]
    [InlineData(null)]
    public void RefusesAnotherFormat(string? format)
    {
        Assert.Equal("uefiaccess-dell-settings/1", DellSettingsFile.FormatName);
        JsonObject root = UnitA18Node();
        root["Format"] = format;

        Assert.Equal(new DellSettingsFileResult(true, null, DellSettingsFile.UnknownFormatProblem), ReadText(root.ToJsonString()));

        root.Remove("Format");
        Assert.Equal(new DellSettingsFileResult(true, null, DellSettingsFile.UnknownFormatProblem), ReadText(root.ToJsonString()));
    }

    [Fact]
    public void RefusesAValueThisVersionDoesNotKnow()
    {
        DellSettingsFileResult unknown = new(true, null, DellSettingsFile.UnknownFormatProblem);
        foreach (Action<JsonObject> change in new Action<JsonObject>[]
        {
            root => root["Outcome"] = "Finished",
            root => root["Outcome"] = "0",
            root => root["Outcome"] = null,
            root => root["Source"] = "cctk",
            root => root["Source"] = "1",
            root => root["Source"] = null,
            root => root["ReadAtUtc"] = "yesterday",
            root => root["ReadAtUtc"] = "2026-10-05T09:12:44+02:00",
            root => root.Remove("ReadAtUtc"),
            root => SettingNode(root, "NumLock")["State"] = "Written",
            root => SettingNode(root, "NumLock")["State"] = "4",
            root => SettingNode(root, "NumLock")["PossibleValuesOrigin"] = "Elsewhere",
            root => SettingNode(root, "NumLock")["Id"] = null,
            root => SettingNode(root, "NumLock")["PossibleValues"]![0]!["Token"] = null,
            root => SettingNode(root, "NumLock")["PossibleValues"]!.AsArray().Add(null),
            root => SettingNode(root, "NumLock")["CurrentToken"] = 2,
            root => root["Settings"]!.AsArray().Add(null),
            root => root["Problems"]!.AsArray().Add(new JsonObject { ["Interface"] = "Firmware", ["Kind"] = "NotInstalled" }),
            root => root["Problems"]!.AsArray().Add(new JsonObject { ["Interface"] = "DellCommandMonitor", ["Kind"] = "Exploded" }),
            root => root["Problems"]!.AsArray().Add(null),
            root => root["Machine"] = "Dell Inc.",
            root => root["Settings"] = "all of them",
        })
        {
            JsonObject root = UnitA18Node();
            change(root);
            Assert.Equal(unknown, ReadText(root.ToJsonString()));
        }

        // Names are compared without regard to case, as the baseline reader compares its own.
        JsonObject lower = UnitA18Node();
        lower["Outcome"] = "complete";
        lower["Source"] = "dellcommandmonitor";
        SettingNode(lower, "NumLock")["State"] = "read";
        Assert.Null(ReadText(lower.ToJsonString()).Problem);
    }

    [Fact]
    public void MalformedJson_IsAProblemNotAThrow()
    {
        // A whole document nested one level deeper than MaximumDepth allows: the document and
        // eight arrays in an unknown property are nine levels.
        Assert.Equal(8, DellSettingsFile.MaximumDepth);
        string deep = WithExtra(UnitA18Node(), new string('[', 8) + new string(']', 8));
        string[] texts =
        {
            string.Empty,
            "   ",
            "{",
            "{ \"Format\": ",
            "null",
            "[]",
            "42",
            "\"uefiaccess-dell-settings/1\"",
            "{ \"Variables\": [] }",
            "<settings/>",
            deep,
        };

        foreach (string text in texts)
        {
            DellSettingsFileResult? result = null;
            Exception? exception = Record.Exception(() => result = ReadText(text));
            Assert.Null(exception);
            Assert.Equal(new DellSettingsFileResult(true, null, DellSettingsFile.UnknownFormatProblem), result);
        }

        // Bytes that are not text at all.
        string folder = NewFolder();
        File.WriteAllBytes(Path.Combine(folder, BaselineExporter.DellSettingsFileName), new byte[] { 0xFF, 0xFE, 0x00, 0xD8, 0x00, 0x00, 0x7B, 0xC3 });
        Assert.Equal(new DellSettingsFileResult(true, null, DellSettingsFile.UnknownFormatProblem), DellSettingsFile.Read(folder));

        // Eight levels, an unknown property included, are accepted.
        DellSettingsFileResult atLimit = ReadText(WithExtra(UnitA18Node(), new string('[', 7) + new string(']', 7)));
        Assert.Null(atLimit.Problem);
        Assert.Equal(DcmRowFixtures.UnitA18Texts, atLimit.Settings!.Settings.Select(DellSentences.ValueText));
    }

    [Fact]
    public void IgnoresUnknownIds_AndMarksMissingIdsNotRead()
    {
        JsonObject root = UnitA18Node();
        JsonArray settings = root["Settings"]!.AsArray();
        settings.Remove(SettingNode(root, "NumLock"));
        settings.Add(OtherSetting("Bogus"));
        settings.Add(OtherSetting("numlock"));
        JsonNode firstWake = SettingNode(root, "WakeOnLan");
        JsonNode secondWake = firstWake.DeepClone();
        secondWake["CurrentToken"] = "4";
        settings.Add(secondWake);
        Capture capture = new();

        DellSettingsFileResult file = DellSettingsFile.Read(FolderWith(root.ToJsonString()), capture.Logger);

        Assert.Null(file.Problem);
        DellSettingsResult result = file.Settings!;
        Assert.Equal(Catalog.Select(entry => entry.Id), result.Settings.Select(setting => setting.Id));
        Assert.Equal(DellReadOutcome.Partial, result.Outcome);
        FirmwareSetting numLock = Setting(file, "NumLock");
        Assert.Equal(SettingState.NotRead, numLock.State);
        Assert.Null(numLock.Current);
        Assert.Empty(numLock.PossibleValues);
        Assert.Equal(DcmRowFixtures.UnitA18Texts.Take(9).Append(DellSentences.NotReadValue), result.Settings.Select(DellSentences.ValueText));

        // The first entry of an id is used; ids are compared ordinally, so "numlock" is unknown.
        Assert.Equal("Disabled", Setting(file, "WakeOnLan").Current!.Text);
        Assert.Contains(capture.Messages, message => message.Contains("the id \"Bogus\" is not an agreed setting; it was ignored", StringComparison.Ordinal));
        Assert.Contains(capture.Messages, message => message.Contains("the id \"numlock\" is not an agreed setting; it was ignored", StringComparison.Ordinal));
        Assert.Contains(capture.Messages, message => message.Contains("the id \"WakeOnLan\" is listed more than once; the first is used", StringComparison.Ordinal));
        Assert.All(capture.Levels, level => Assert.Equal(LogEventLevel.Debug, level));

        // A file that lists no setting at all reads as ten rows not read.
        JsonObject none = UnitA18Node();
        none["Settings"] = new JsonArray();
        DellSettingsResult empty = ReadText(none.ToJsonString()).Settings!;
        Assert.Equal(DellReadOutcome.Partial, empty.Outcome);
        Assert.All(empty.Settings, setting => Assert.Equal(SettingState.NotRead, setting.State));
    }

    [Fact]
    public void RemapsTextsThroughTheCurrentCatalog()
    {
        // Texts the file has no place for are ignored wherever they are put.
        const string Injected = "INJECTED";
        JsonObject root = UnitA18Node();
        root["FallbackSentence"] = Injected;
        root["Warnings"] = new JsonArray(Injected);
        root["NativeInterfacePresent"] = true;
        foreach (JsonNode? setting in root["Settings"]!.AsArray())
        {
            setting!["Name"] = Injected;
            setting["DisplayName"] = Injected;
            setting["Category"] = Injected;
            setting["SetupLocation"] = Injected;
            setting["Text"] = Injected;
            setting["Note"] = Injected;
            setting["DisplayFromVendor"] = true;
            setting["Sentence"] = Injected;
        }

        root["Problems"]!.AsArray().Add(new JsonObject { ["Interface"] = "DellCommandMonitor", ["Kind"] = "NotInstalled", ["Detail"] = Injected });

        // A recorded state is not taken as such: TPM activation recorded as not applicable while
        // TPM security reads Enabled is read again, and Secure Boot recorded as read without a
        // value is the no-value state with its note.
        SettingNode(root, "TpmActivation")["State"] = "NotApplicable";
        SettingNode(root, "SecureBoot")["CurrentToken"] = null;

        DellSettingsResult result = ReadText(root.ToJsonString()).Settings!;

        Assert.Equal(DellReadOutcome.Partial, result.Outcome);
        Assert.Equal(new[] { "UEFI", DellSentences.NoValueReportedValue, "AHCI", "Enabled", "Activated", "Enabled", "Enabled", "Enabled", "Disabled", "Enabled" }, result.Settings.Select(DellSentences.ValueText));
        Assert.Equal(DellSentences.SecureBootNoValueNote, Setting(result, "SecureBoot").Note);
        Assert.Equal(SettingState.Read, Setting(result, "TpmActivation").State);
        Assert.Equal(new[] { "Disabled", "LAN Only", "LAN with PXE Boot" }, Setting(result, "WakeOnLan").PossibleValues.Select(value => value.Text));
        Assert.Equal(new[] { "Disabled", "Enabled with PXE", "Enabled", "Enabled with image server" }, Setting(result, "IntegratedNic").PossibleValues.Select(value => value.Text));
        Assert.Equal("Num Lock at boot: Enabled. Possible values: Disabled, Enabled.", DellSentences.RowSentence(Setting(result, "NumLock")));
        Assert.All(result.Settings, setting =>
        {
            DellCatalogEntry entry = DellAttributeCatalog.Find(setting.Id)!;
            Assert.Equal(entry.DcmAttributeName, setting.Name);
            Assert.Equal(entry.DisplayName, setting.DisplayName);
            Assert.Equal(entry.Category, setting.Category);
            Assert.Equal(entry.SetupLocation, setting.SetupLocation);
            Assert.False(setting.DisplayFromVendor);
        });
        Assert.False(result.NativeInterfacePresent);
        Assert.Null(result.FallbackSentence);
        Assert.Empty(result.Warnings);
        Assert.Equal(string.Empty, Assert.Single(result.Problems).Detail);
        Assert.DoesNotContain(AllTexts(result), text => text.Contains(Injected, StringComparison.Ordinal));

        // The same file from Dell Command Configure: the words are the fallback's, the possible
        // values the catalog's, and Boot list option, which the fallback never reads, is not
        // reported whatever the file says.
        JsonObject cctk = UnitA18Node();
        cctk["Source"] = "DellCommandConfigure";
        SettingNode(cctk, "BootListOption")["CurrentToken"] = "UEFI";
        SettingNode(cctk, "NumLock")["CurrentToken"] = "on";
        SettingNode(cctk, "WakeOnLan")["CurrentToken"] = "enable";
        SettingNode(cctk, "SataOperation")["CurrentToken"] = "Raid";

        DellSettingsResult fallback = ReadText(cctk.ToJsonString()).Settings!;

        Assert.Equal(CctkSource, fallback.Source);
        Assert.Equal(DellReadOutcome.Partial, fallback.Outcome);
        FirmwareSetting boot = Setting(fallback, "BootListOption");
        Assert.Equal(SettingState.NotReported, boot.State);
        Assert.Equal("Boot list option: not reported. This version reads it through Dell Command Monitor only.", DellSentences.RowSentence(boot));
        Assert.Equal("Enabled", Setting(fallback, "NumLock").Current!.Text);
        Assert.Equal(new[] { "Disabled", "Enabled" }, Setting(fallback, "NumLock").PossibleValues.Select(value => value.Text));
        Assert.Equal("LAN Only", Setting(fallback, "WakeOnLan").Current!.Text);
        FirmwareSetting sata = Setting(fallback, "SataOperation");
        Assert.Equal("Raid", sata.Current!.Text);
        Assert.True(sata.DisplayFromVendor);
        Assert.All(fallback.Settings, setting => Assert.Equal(PossibleValuesOrigin.Catalog, setting.PossibleValuesOrigin));
        Assert.All(fallback.Settings, setting => Assert.Null(setting.VendorMarksReadOnly));
    }

    [Fact]
    public void StripsControlCharacters()
    {
        JsonObject root = UnitA18Node();
        root["Machine"]!["Model"] = "Opti\u0007Plex\u001B 7020\r\n";
        root["Machine"]!["BiosVersion"] = "A1\u00008";
        JsonNode numLock = SettingNode(root, "NumLock");
        numLock["Id"] = "Num\u0000Lock";
        numLock["CurrentToken"] = "2\t";
        numLock["PossibleValues"]![1]!["VendorName"] = "Ena\r\nble";
        JsonNode nic = SettingNode(root, "IntegratedNic");
        nic["PossibleValues"]![2]!["VendorName"] = "Fancy\u0000\u0085Name";

        DellSettingsFileResult file = ReadText(root.ToJsonString());

        Assert.Null(file.Problem);
        DellSettingsResult result = file.Settings!;
        Assert.Equal(new DellMachine("Dell Inc.", "OptiPlex 7020", "A18"), result.Machine);
        Assert.Empty(result.Warnings);
        FirmwareSetting numLockSetting = Setting(file, "NumLock");
        Assert.Equal(new SettingValue("2", "Enable", "Enabled"), numLockSetting.Current);
        Assert.False(numLockSetting.DisplayFromVendor);
        FirmwareSetting nicSetting = Setting(file, "IntegratedNic");
        Assert.Equal(new SettingValue("3", "FancyName", "FancyName"), nicSetting.Current);
        Assert.True(nicSetting.DisplayFromVendor);
        Assert.DoesNotContain(AllTexts(result), text => text.Any(char.IsControl));
    }

    [Fact]
    public void WritesNoDetailText()
    {
        DellSettingsResult result = DcmResult(DcmRowFixtures.UnitA18()) with
        {
            Problems = new[] { new DellInterfaceProblem(DellInterface.Reader, DellProblemKind.Unexpected, "DETAIL: an unexpected error with a path C:\\x") },
            Warnings = new[] { "These names and values were checked against BIOS A18 only; this computer runs BIOS A17." },
            FallbackSentence = "Dell Command Monitor is not installed. Dell Command Configure was used instead.",
        };

        DellSettingsFileDocument document = DellSettingsFile.ToDocument(result);
        string json = Json(document);

        using JsonDocument parsed = JsonDocument.Parse(json);
        JsonElement root = parsed.RootElement;
        Assert.Equal(new[] { "Format", "ReadAtUtc", "Outcome", "Source", "Machine", "Settings", "Problems" }, Names(root));
        Assert.Equal("uefiaccess-dell-settings/1", root.GetProperty("Format").GetString());
        Assert.Equal("2026-10-05T07:12:44Z", root.GetProperty("ReadAtUtc").GetString());
        Assert.Equal("Complete", root.GetProperty("Outcome").GetString());
        Assert.Equal("DellCommandMonitor", root.GetProperty("Source").GetString());
        Assert.Equal(new[] { "Manufacturer", "Model", "BiosVersion" }, Names(root.GetProperty("Machine")));
        Assert.Equal(Catalog.Select(entry => entry.Id), root.GetProperty("Settings").EnumerateArray().Select(setting => setting.GetProperty("Id").GetString()));
        Assert.All(root.GetProperty("Settings").EnumerateArray(), setting =>
        {
            Assert.Equal(new[] { "Id", "State", "CurrentToken", "CurrentVendorName", "PossibleValues", "PossibleValuesOrigin", "VendorMarksReadOnly" }, Names(setting));
            Assert.All(setting.GetProperty("PossibleValues").EnumerateArray(), value => Assert.Equal(new[] { "Token", "VendorName" }, Names(value)));
        });
        JsonElement problem = Assert.Single(root.GetProperty("Problems").EnumerateArray());
        Assert.Equal(new[] { "Interface", "Kind" }, Names(problem));
        Assert.Equal("Reader", problem.GetProperty("Interface").GetString());
        Assert.Equal("Unexpected", problem.GetProperty("Kind").GetString());

        // The example row, as written.
        JsonElement numLock = root.GetProperty("Settings")[9];
        Assert.Equal("NumLock", numLock.GetProperty("Id").GetString());
        Assert.Equal("Read", numLock.GetProperty("State").GetString());
        Assert.Equal("2", numLock.GetProperty("CurrentToken").GetString());
        Assert.Equal("Enable", numLock.GetProperty("CurrentVendorName").GetString());
        Assert.Equal(new[] { "1=Disable", "2=Enable" }, numLock.GetProperty("PossibleValues").EnumerateArray().Select(value => value.GetProperty("Token").GetString() + "=" + value.GetProperty("VendorName").GetString()));
        Assert.Equal("Interface", numLock.GetProperty("PossibleValuesOrigin").GetString());
        Assert.False(numLock.GetProperty("VendorMarksReadOnly").GetBoolean());

        // No display text, note, sentence, warning or problem detail is written.
        string[] neverWritten =
        {
            "DETAIL",
            "unexpected error",
            "Num Lock at boot",
            "Boot list option",
            "POST Behavior",
            "Numlock LED",
            "Possible values",
            "lists only Enabled",
            "checked against BIOS",
            "was used instead",
            "\"Activated\"",
            "\"LAN Only\"",
            "\"Enabled with PXE\"",
            "\"Enabled\"",
            "Text",
            "Note",
            "Detail",
            "DisplayName",
            "DisplayFromVendor",
        };
        Assert.All(neverWritten, text => Assert.DoesNotContain(text, json, StringComparison.Ordinal));
    }

    /// <summary>A read through Dell Command Monitor of the given rows, as the settings reader would return it.</summary>
    private static DellSettingsResult DcmResult(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, DellMachine? machine = null)
    {
        DellProviderRead read = DcmSettingsProvider.FromOutcome(DcmRowFixtures.Answer(rows), null, Catalog, false);
        Assert.True(read.Answered);
        DellMachine readOn = machine ?? Unit;
        string[] warnings = DellSettingsReader.OtherBios(readOn.BiosVersion) is string bios
            ? new[] { string.Format(CultureInfo.InvariantCulture, DellSentences.OtherBiosFormat, DellAttributeCatalog.VerifiedBiosVersion, bios) }
            : Array.Empty<string>();
        return Usable(DcmSource, read.Settings, readOn, Array.Empty<DellInterfaceProblem>(), warnings, null);
    }

    /// <summary>Dell Command Monitor not installed and Dell Command Configure read instead, each row parsed from the line its option prints on A18.</summary>
    private static DellSettingsResult FallbackResult()
    {
        FirmwareSetting[] settings = Catalog
            .Select(entry => entry.CctkOption is CctkReadOption option
                ? CctkSettingsProvider.ParseRow(entry, CctkOutputFixtures.Answer(option, CctkOutputFixtures.UnitA18Output(option)), out _)
                : DellSettingMapper.NotReported(entry, PossibleValuesOrigin.Catalog, DellSentences.NotReadByFallbackNote))
            .ToArray();
        DellInterfaceProblem[] problems = { new(DellInterface.DellCommandMonitor, DellProblemKind.NotInstalled, @"the namespace root\dcim\sysman is absent") };
        return Usable(CctkSource, settings, Unit, problems, Array.Empty<string>(), "Dell Command Monitor is not installed. Dell Command Configure was used instead.");
    }

    /// <summary>Neither tool installed, with the native namespace present.</summary>
    private static DellSettingsResult NothingInstalledResult()
    {
        DellInterfaceProblem[] problems =
        {
            new(DellInterface.DellCommandMonitor, DellProblemKind.NotInstalled, @"the namespace root\dcim\sysman is absent"),
            new(DellInterface.DellCommandConfigure, DellProblemKind.ProgramNotFound, "cctk.exe was not found in the Program Files folders"),
            new(DellInterface.NativeBiosInterface, DellProblemKind.NativeInterfaceOnly, @"the namespace root\dcim\sysman\biosattributes exists; this version does not read it"),
        };
        return Unavailable(new DellMachine("Dell Inc.", "OptiPlex 7020", null), problems) with { NativeInterfacePresent = true };
    }

    /// <summary>Another manufacturer: neither interface tried.</summary>
    private static DellSettingsResult NotADellResult()
    {
        return Unavailable(new DellMachine("Contoso", "Desk 1", "1.0"), new[] { new DellInterfaceProblem(DellInterface.Machine, DellProblemKind.NotDell, "Contoso") });
    }

    private static DellSettingsResult Usable(DellSettingsSource source, IReadOnlyList<FirmwareSetting> fromProvider, DellMachine machine, IReadOnlyList<DellInterfaceProblem> problems, IReadOnlyList<string> warnings, string? fallback)
    {
        IReadOnlyList<FirmwareSetting> settings = DellSettingsReader.ApplyDependencies(Catalog, DellSettingsReader.InCatalogOrder(Catalog, fromProvider));
        DellReadOutcome outcome = settings.All(setting => setting.State is SettingState.Read or SettingState.NotApplicable) ? DellReadOutcome.Complete : DellReadOutcome.Partial;
        return new DellSettingsResult(outcome, source, StartedAt, 1180, machine, settings, problems, warnings) { FallbackSentence = fallback };
    }

    private static DellSettingsResult Unavailable(DellMachine machine, IReadOnlyList<DellInterfaceProblem> problems)
    {
        return new DellSettingsResult(
            DellReadOutcome.Unavailable,
            null,
            StartedAt,
            900,
            machine,
            Catalog.Select(entry => DellSettingMapper.NotRead(entry, null)).ToArray(),
            problems,
            Array.Empty<string>());
    }

    private static void AssertSameSetting(FirmwareSetting expected, FirmwareSetting actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.DisplayName, actual.DisplayName);
        Assert.Equal(expected.Category, actual.Category);
        Assert.Equal(expected.SetupLocation, actual.SetupLocation);
        Assert.Equal(expected.State, actual.State);
        Assert.Equal(expected.Current, actual.Current);
        Assert.Equal(expected.PossibleValues, actual.PossibleValues);
        Assert.Equal(expected.PossibleValuesOrigin, actual.PossibleValuesOrigin);
        Assert.Equal(expected.VendorMarksReadOnly, actual.VendorMarksReadOnly);
        Assert.Equal(expected.DisplayFromVendor, actual.DisplayFromVendor);
        Assert.Equal(expected.Note, actual.Note);
        Assert.Equal(DellSentences.RowSentence(expected), DellSentences.RowSentence(actual));
    }

    /// <summary>Every text of a result a person could hear or read.</summary>
    private static IEnumerable<string> AllTexts(DellSettingsResult result)
    {
        IEnumerable<string?> texts = new[] { result.Machine.Manufacturer, result.Machine.Model, result.Machine.BiosVersion, result.FallbackSentence, DellSentences.UnavailableSentence(result) }
            .Concat(result.Warnings)
            .Concat(result.Problems.Select(problem => problem.Detail))
            .Concat(result.Problems.Select(DellSentences.Cause))
            .Concat(result.Settings.SelectMany(setting => new[] { setting.Id, setting.Name, setting.DisplayName, setting.Category, setting.SetupLocation, setting.Note, DellSentences.ValueText(setting), DellSentences.RowSentence(setting) }))
            .Concat(result.Settings.SelectMany(setting => setting.PossibleValues.Append(setting.Current).OfType<SettingValue>().SelectMany(value => new[] { value.Token, value.VendorName, value.Text })));
        return texts.OfType<string>();
    }

    private static FirmwareSetting Setting(DellSettingsFileResult file, string id) => Setting(file.Settings!, id);

    private static FirmwareSetting Setting(DellSettingsResult result, string id) => Assert.Single(result.Settings, setting => setting.Id == id);

    private static string[] Names(JsonElement element) => element.EnumerateObject().Select(property => property.Name).ToArray();

    private static string Json(DellSettingsFileDocument document) => JsonSerializer.Serialize(document, BaselineExporter.JsonOptions);

    private static JsonObject Node(DellSettingsResult result) => JsonNode.Parse(Json(DellSettingsFile.ToDocument(result)))!.AsObject();

    private static JsonObject UnitA18Node() => Node(DcmResult(DcmRowFixtures.UnitA18()));

    /// <summary>The document's text with one more property, "Extra", whose value is the JSON text given.</summary>
    private static string WithExtra(JsonObject root, string extraJson)
    {
        string json = root.ToJsonString();
        Assert.EndsWith("}", json, StringComparison.Ordinal);
        return json[..^1] + ", \"Extra\": " + extraJson + " }";
    }

    private static JsonNode SettingNode(JsonObject root, string id) => root["Settings"]!.AsArray().Single(setting => (string?)setting!["Id"] == id)!;

    /// <summary>An entry under an id the catalog does not know, with a plausible read value.</summary>
    private static JsonObject OtherSetting(string id) => new()
    {
        ["Id"] = id,
        ["State"] = "Read",
        ["CurrentToken"] = "1",
        ["CurrentVendorName"] = "Off",
        ["PossibleValues"] = new JsonArray(Value("1", "Off"), Value("2", "On")),
        ["PossibleValuesOrigin"] = "Interface",
        ["VendorMarksReadOnly"] = false,
    };

    private static JsonObject Value(string token, string? vendorName) => new() { ["Token"] = token, ["VendorName"] = vendorName };

    /// <summary>A baseline request for the exporter with no variables and the given Dell settings.</summary>
    private BaselineRequest Request(DellSettingsFileDocument document) => new BaselineRequest(
        _tempRoot,
        "20261005-091244-" + Guid.NewGuid().ToString("N").Substring(0, 8),
        Array.Empty<FirmwareVariable>(),
        FirmwareType.Uefi,
        PrivilegeState.Enabled,
        null,
        null,
        null,
        null,
        "0.1.0",
        StartedAt) with { DellSettings = document };

    private string NewFolder()
    {
        string folder = Path.Combine(_tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private string FolderWith(string json)
    {
        string folder = NewFolder();
        File.WriteAllText(Path.Combine(folder, BaselineExporter.DellSettingsFileName), json, Utf8NoBom);
        return folder;
    }

    private DellSettingsFileResult ReadText(string json) => DellSettingsFile.Read(FolderWith(json));

    /// <summary>Keeps every event's level and its message rendered with quoted strings.</summary>
    private sealed class Capture : ILogEventSink
    {
        private readonly List<LogEvent> _events = new();

        public Capture()
        {
            Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(this).CreateLogger();
        }

        public ILogger Logger { get; }

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_events)
                {
                    return _events.Select(logEvent => logEvent.RenderMessage(CultureInfo.InvariantCulture)).ToArray();
                }
            }
        }

        public IReadOnlyList<LogEventLevel> Levels
        {
            get
            {
                lock (_events)
                {
                    return _events.Select(logEvent => logEvent.Level).ToArray();
                }
            }
        }

        public void Emit(LogEvent logEvent)
        {
            lock (_events)
            {
                _events.Add(logEvent);
            }
        }
    }
}
