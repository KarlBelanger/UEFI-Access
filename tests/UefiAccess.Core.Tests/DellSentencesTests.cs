// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;
using System.Text.RegularExpressions;
using UefiAccess.Core.Settings;
using UefiAccess.Core.Settings.Dell;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The Dell sentences, which the command line prints
/// and the window says: a cause for every problem, the problem sentence built from its parts in
/// their order with the advice of the first problem, and the row sentence that starts with the
/// name and fits 200 characters. None holds "|", an en dash, a code or the words "read-only".
/// </summary>
public sealed class DellSentencesTests
{
    private const DellInterface Dcm = DellInterface.DellCommandMonitor;
    private const DellInterface Cctk = DellInterface.DellCommandConfigure;
    private const DellInterface Reader = DellInterface.Reader;
    private const DellInterface Machine = DellInterface.Machine;

    private const string Lead = "Dell BIOS settings cannot be read. ";
    private const string Install = "Install the Microsoft Visual C++ 2015-2022 x64 runtime, then Dell Command Monitor, and try again; the Dell interface setup page of the documentation has the steps.";
    private const string Retry = "Try again; if it fails again, restart Windows and try again.";

    /// <summary>The documented example of section 2.4: a computer with neither Dell tool, the tester's likely case.</summary>
    private const string NothingInstalled = "Dell BIOS settings cannot be read. Dell Command Monitor is not installed. Dell Command Configure was not found. Install the Microsoft Visual C++ 2015-2022 x64 runtime, then Dell Command Monitor, and try again; the Dell interface setup page of the documentation has the steps.";

    [Fact]
    public void EveryProblemKindHasACause()
    {
        foreach (DellProblemKind kind in Enum.GetValues<DellProblemKind>())
        {
            foreach (DellInterface where in Enum.GetValues<DellInterface>())
            {
                string cause = DellSentences.Cause(new DellInterfaceProblem(where, kind, "LENOVO"));
                Assert.False(string.IsNullOrWhiteSpace(cause), kind + " on " + where);
                Assert.EndsWith(".", cause, StringComparison.Ordinal);
                AssertSpeakable(cause);
            }
        }

        Assert.Equal(DellSentences.DcmNotInstalled, DellSentences.Cause(new DellInterfaceProblem(Dcm, DellProblemKind.NotInstalled, "absent")));
        Assert.Equal(DellSentences.CctkNotFound, DellSentences.Cause(new DellInterfaceProblem(Cctk, DellProblemKind.ProgramNotFound, "absent")));
        Assert.Equal("Dell Command Monitor did not answer within 60 seconds.", DellSentences.Cause(new DellInterfaceProblem(Dcm, DellProblemKind.TimedOut, "timeout")));
        Assert.Equal("Dell Command Configure did not answer within 15 seconds.", DellSentences.Cause(new DellInterfaceProblem(Cctk, DellProblemKind.TimedOut, "timeout")));
        Assert.Equal("Reading stopped at the time limit of 120 seconds.", DellSentences.Cause(new DellInterfaceProblem(Reader, DellProblemKind.BudgetExhausted, "budget")));
        Assert.Equal(
            "This computer's manufacturer is LENOVO, not Dell, so there are no Dell BIOS settings to show.",
            DellSentences.Cause(new DellInterfaceProblem(Machine, DellProblemKind.NotDell, " LENOVO ")));
        Assert.Equal(DellSentences.NativeInterfaceClause, DellSentences.Cause(new DellInterfaceProblem(DellInterface.NativeBiosInterface, DellProblemKind.NativeInterfaceOnly, "present")));
    }

    [Theory]
    [InlineData(Dcm, DellProblemKind.NotInstalled, Cctk, DellProblemKind.ProgramNotFound, false, NothingInstalled)]
    [InlineData(Dcm, DellProblemKind.NotInstalled, Cctk, DellProblemKind.ProgramNotFound, true, "Dell BIOS settings cannot be read. Dell Command Monitor is not installed. Dell Command Configure was not found. This computer has the built-in Dell BIOS interface of newer models, which this version does not read.")]
    [InlineData(Dcm, DellProblemKind.ProviderLoadFailure, Cctk, DellProblemKind.ProgramNotFound, false, Lead + "Dell Command Monitor is installed, but its WMI provider did not load. Dell Command Configure was not found. Install the Microsoft Visual C++ 2015-2022 x64 runtime, then try again.")]
    [InlineData(Dcm, DellProblemKind.NoInstances, Cctk, DellProblemKind.ProgramNotFound, false, Lead + "Dell Command Monitor answered with no BIOS settings. Dell Command Configure was not found. Its data service, DSM SA Data Manager, is probably not running; restart Windows, then try again. This application never starts it.")]
    [InlineData(Dcm, DellProblemKind.AccessDenied, Cctk, DellProblemKind.NeedsAdministrator, false, Lead + "Windows refused access to Dell Command Monitor. Dell Command Configure needs administrator rights. Run it as administrator, then try again.")]
    [InlineData(Cctk, DellProblemKind.NeedsAdministrator, null, null, false, Lead + "Dell Command Configure needs administrator rights. Run it as administrator, then try again.")]
    [InlineData(Dcm, DellProblemKind.ClassMissing, Cctk, DellProblemKind.ProgramNotFound, false, Lead + "Dell Command Monitor is installed, but its BIOS settings class is missing. Dell Command Configure was not found. Repair or reinstall Dell Command Monitor, then try again.")]
    [InlineData(Dcm, DellProblemKind.TimedOut, Cctk, DellProblemKind.TimedOut, false, Lead + "Dell Command Monitor did not answer within 60 seconds. Dell Command Configure did not answer within 15 seconds. " + Retry)]
    [InlineData(Dcm, DellProblemKind.QueryFailed, Cctk, DellProblemKind.NoBiosInterface, false, Lead + "Dell Command Monitor could not be queried. Dell Command Configure found no Dell BIOS interface on this computer. " + Retry)]
    [InlineData(Dcm, DellProblemKind.TimedOut, Reader, DellProblemKind.BudgetExhausted, false, Lead + "Dell Command Monitor did not answer within 60 seconds. Reading stopped at the time limit of 120 seconds. " + Retry)]
    [InlineData(Reader, DellProblemKind.BudgetExhausted, null, null, false, Lead + "Reading stopped at the time limit of 120 seconds. " + Retry)]
    [InlineData(Cctk, DellProblemKind.RunFailed, null, null, false, Lead + "Dell Command Configure stopped with an error; the log has the details. " + Retry)]
    [InlineData(Cctk, DellProblemKind.UnrecognisedOutput, null, null, false, Lead + "Dell Command Configure answered in a form this version does not recognize. " + Retry)]
    [InlineData(Reader, DellProblemKind.Unexpected, Cctk, DellProblemKind.ProgramNotFound, false, Lead + "An unexpected error occurred; the log has the details. Dell Command Configure was not found. " + Retry)]
    [InlineData(Dcm, DellProblemKind.Unexpected, null, null, false, Lead + "An unexpected error occurred; the log has the details. " + Retry)]
    [InlineData(Cctk, DellProblemKind.ProgramNotFound, null, null, false, "Dell BIOS settings cannot be read. Dell Command Configure was not found.")]
    [InlineData(Cctk, DellProblemKind.NoBiosInterface, null, null, false, "Dell BIOS settings cannot be read. Dell Command Configure found no Dell BIOS interface on this computer.")]
    [InlineData(Cctk, DellProblemKind.OptionNotSupported, Cctk, DellProblemKind.OptionNotSupported, false, "Dell BIOS settings cannot be read. Dell Command Configure does not offer these settings on this computer.")]
    [InlineData(Reader, DellProblemKind.NotWindows, null, null, false, "Dell BIOS settings cannot be read. They can be read on Windows only.")]
    [InlineData(Machine, DellProblemKind.NotDell, null, null, false, "Dell BIOS settings cannot be read. This computer's manufacturer is LENOVO, not Dell, so there are no Dell BIOS settings to show.")]
    public void UnavailableSentence_ForEveryFirstProblem(DellInterface firstInterface, DellProblemKind firstKind, DellInterface? secondInterface, DellProblemKind? secondKind, bool nativeInterfacePresent, string expected)
    {
        List<DellInterfaceProblem> problems = new() { new DellInterfaceProblem(firstInterface, firstKind, firstKind == DellProblemKind.NotDell ? "LENOVO" : "detail") };
        if (secondInterface is DellInterface where && secondKind is DellProblemKind kind)
        {
            problems.Add(new DellInterfaceProblem(where, kind, "detail"));
        }

        string? sentence = DellSentences.UnavailableSentence(Unavailable(problems, nativeInterfacePresent));

        Assert.Equal(expected, sentence);
        AssertSpeakable(sentence!);
    }

    [Fact]
    public void UnavailableSentence_NothingInstalled_IsTheDocumentedExample()
    {
        DellSettingsResult result = Unavailable(new[]
        {
            new DellInterfaceProblem(Dcm, DellProblemKind.NotInstalled, @"the namespace root\dcim\sysman is absent"),
            new DellInterfaceProblem(Cctk, DellProblemKind.ProgramNotFound, "cctk.exe was not found in the Program Files folders, so nothing was started"),
        });

        Assert.Equal(NothingInstalled, DellSentences.UnavailableSentence(result));

        // The parts are the constants, joined with one space.
        Assert.Equal(
            string.Join(" ", DellSentences.UnavailableLead, DellSentences.DcmNotInstalled, DellSentences.CctkNotFound, DellSentences.InstallAdvice),
            DellSentences.UnavailableSentence(result));
        Assert.Equal(Install, DellSentences.InstallAdvice);
    }

    [Fact]
    public void UnavailableSentence_IsNullForAUsableRead_AndUsesTheMachinesManufacturer()
    {
        DellSettingsResult unavailable = Unavailable(new[] { new DellInterfaceProblem(Machine, DellProblemKind.NotDell, "LENOVO") });

        Assert.Null(DellSentences.UnavailableSentence(unavailable with { Outcome = DellReadOutcome.Complete, Source = DellSettingsSource.DellCommandMonitor }));
        Assert.Null(DellSentences.UnavailableSentence(unavailable with { Outcome = DellReadOutcome.Partial, Source = DellSettingsSource.DellCommandConfigure }));
        Assert.Equal(
            "Dell BIOS settings cannot be read. This computer's manufacturer is Hewlett-Packard, not Dell, so there are no Dell BIOS settings to show.",
            DellSentences.UnavailableSentence(unavailable with { Machine = new DellMachine("Hewlett-Packard", "HP Compaq", "L01") }));

        // A read stopped by the machine says nothing of the interfaces.
        DellSettingsResult notWindows = Unavailable(new[] { new DellInterfaceProblem(Reader, DellProblemKind.NotWindows, "not Windows"), new DellInterfaceProblem(Dcm, DellProblemKind.NotInstalled, "absent") });
        Assert.Equal("Dell BIOS settings cannot be read. They can be read on Windows only.", DellSentences.UnavailableSentence(notWindows));
    }

    [Theory]
    [InlineData(nameof(DellSentences.SourceDcm), "Dell Command Monitor")]
    [InlineData(nameof(DellSentences.SourceCctk), "Dell Command Configure")]
    [InlineData(nameof(DellSentences.UnavailableLead), "Dell BIOS settings cannot be read.")]
    [InlineData(nameof(DellSentences.NotWindowsCause), "They can be read on Windows only.")]
    [InlineData(nameof(DellSentences.NotDellCauseFormat), "This computer's manufacturer is {0}, not Dell, so there are no Dell BIOS settings to show.")]
    [InlineData(nameof(DellSentences.DcmNotInstalled), "Dell Command Monitor is not installed.")]
    [InlineData(nameof(DellSentences.DcmClassMissing), "Dell Command Monitor is installed, but its BIOS settings class is missing.")]
    [InlineData(nameof(DellSentences.DcmProviderLoadFailure), "Dell Command Monitor is installed, but its WMI provider did not load.")]
    [InlineData(nameof(DellSentences.DcmAccessDenied), "Windows refused access to Dell Command Monitor.")]
    [InlineData(nameof(DellSentences.DcmNoInstances), "Dell Command Monitor answered with no BIOS settings.")]
    [InlineData(nameof(DellSentences.DcmTimedOutFormat), "Dell Command Monitor did not answer within {0} seconds.")]
    [InlineData(nameof(DellSentences.DcmQueryFailed), "Dell Command Monitor could not be queried.")]
    [InlineData(nameof(DellSentences.CctkNotFound), "Dell Command Configure was not found.")]
    [InlineData(nameof(DellSentences.CctkNeedsAdministrator), "Dell Command Configure needs administrator rights.")]
    [InlineData(nameof(DellSentences.CctkNoBiosInterface), "Dell Command Configure found no Dell BIOS interface on this computer.")]
    [InlineData(nameof(DellSentences.CctkTimedOutFormat), "Dell Command Configure did not answer within {0} seconds.")]
    [InlineData(nameof(DellSentences.CctkRunFailed), "Dell Command Configure stopped with an error; the log has the details.")]
    [InlineData(nameof(DellSentences.CctkUnrecognisedOutput), "Dell Command Configure answered in a form this version does not recognize.")]
    [InlineData(nameof(DellSentences.CctkOptionsNotSupported), "Dell Command Configure does not offer these settings on this computer.")]
    [InlineData(nameof(DellSentences.BudgetExhaustedFormat), "Reading stopped at the time limit of {0} seconds.")]
    [InlineData(nameof(DellSentences.UnexpectedCause), "An unexpected error occurred; the log has the details.")]
    [InlineData(nameof(DellSentences.NativeInterfaceClause), "This computer has the built-in Dell BIOS interface of newer models, which this version does not read.")]
    [InlineData(nameof(DellSentences.InstallAdvice), Install)]
    [InlineData(nameof(DellSentences.RuntimeAdvice), "Install the Microsoft Visual C++ 2015-2022 x64 runtime, then try again.")]
    [InlineData(nameof(DellSentences.ServiceAdvice), "Its data service, DSM SA Data Manager, is probably not running; restart Windows, then try again. This application never starts it.")]
    [InlineData(nameof(DellSentences.AdministratorAdvice), "Run it as administrator, then try again.")]
    [InlineData(nameof(DellSentences.RepairAdvice), "Repair or reinstall Dell Command Monitor, then try again.")]
    [InlineData(nameof(DellSentences.RetryAdvice), Retry)]
    [InlineData(nameof(DellSentences.FallbackFormat), "{0} Dell Command Configure was used instead.")]
    [InlineData(nameof(DellSentences.OtherBiosFormat), "These names and values were checked against BIOS {0} only; this computer runs BIOS {1}.")]
    [InlineData(nameof(DellSentences.NotReportedValue), "not reported")]
    [InlineData(nameof(DellSentences.NotReadValue), "not read")]
    [InlineData(nameof(DellSentences.NoValueReportedValue), "no value reported")]
    [InlineData(nameof(DellSentences.UnnamedValue), "a value Dell does not name")]
    [InlineData(nameof(DellSentences.NotApplicableFormat), "not applicable while {0} is {1}")]
    [InlineData(nameof(DellSentences.RowFormat), "{0}: {1}. Possible values: {2}.")]
    [InlineData(nameof(DellSentences.RowWithoutPossibleFormat), "{0}: {1}.")]
    [InlineData(nameof(DellSentences.MoreValuesFormat), "and {0} more")]
    [InlineData(nameof(DellSentences.SecureBootDcmNote), "Dell Command Monitor lists only Enabled; the BIOS setup also offers Disabled.")]
    [InlineData(nameof(DellSentences.SecureBootNoValueNote), "Dell Command Monitor reports no value while Secure Boot is off.")]
    [InlineData(nameof(DellSentences.NotReadByFallbackNote), "This version reads it through Dell Command Monitor only.")]
    public void EverySentence_IsTheDesignsText(string name, string text)
    {
        // Word for word.
        FieldInfo? field = typeof(DellSentences).GetField(name, BindingFlags.Public | BindingFlags.Static);
        Assert.True(field is { IsLiteral: true } && field.FieldType == typeof(string), name + " is not a public string constant");
        Assert.Equal(text, (string?)field!.GetRawConstantValue());
    }

    [Fact]
    public void NoSentenceHoldsAPipeAnEnDashACodeOrReadOnly()
    {
        FieldInfo[] constants = typeof(DellSentences).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .ToArray();
        Assert.Equal(41, constants.Length);
        foreach (FieldInfo field in constants)
        {
            string text = (string)field.GetRawConstantValue()!;
            Assert.False(string.IsNullOrWhiteSpace(text), field.Name);
            AssertSpeakable(text);

            // The only digits are the placeholders, the runtime's name and its years.
            string rest = Regex.Replace(text, @"\{\d\}", string.Empty).Replace("2015-2022", string.Empty, StringComparison.Ordinal).Replace("x64", string.Empty, StringComparison.Ordinal);
            Assert.False(rest.Any(char.IsDigit), field.Name + " holds a number: " + text);
        }

        Assert.Equal(200, DellSentences.RowSentenceLimit);
        Assert.Equal("Dell Command Monitor", DellSentences.SourceName(DellSettingsSource.DellCommandMonitor));
        Assert.Equal("Dell Command Configure", DellSentences.SourceName(DellSettingsSource.DellCommandConfigure));
    }

    [Fact]
    public void RowSentence_StartsWithTheNameAndIsCappedAt200()
    {
        Assert.Equal("Num Lock at boot: Enabled. Possible values: Disabled, Enabled.", DellSentences.RowSentence("Num Lock at boot", "Enabled", new[] { "Disabled", "Enabled" }, null));
        Assert.Equal("Boot list option: not read.", DellSentences.RowSentence("Boot list option", DellSentences.NotReadValue, Array.Empty<string>(), null));

        // Too many possible values: the ones that do not fit are counted, the name stays first.
        string[] many = Enumerable.Range(1, 40).Select(n => "Value number " + new string((char)('a' + (n % 26)), 3)).ToArray();
        string capped = DellSentences.RowSentence("Integrated NIC", "Enabled", many, null);
        Assert.True(capped.Length <= 200, capped.Length + ": " + capped);
        Assert.StartsWith("Integrated NIC: Enabled. Possible values: Value number", capped, StringComparison.Ordinal);
        Match more = Regex.Match(capped, @", and (\d+) more\.$");
        Assert.True(more.Success, capped);
        int shown = capped.Split(", ").Count(part => part.StartsWith("Value number", StringComparison.Ordinal) || part.Contains("Possible values: Value number", StringComparison.Ordinal));
        Assert.Equal(40, shown + int.Parse(more.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));

        // A name, value and note longer than the limit: cut at the limit, still starting with the name.
        string cut = DellSentences.RowSentence("Secure Boot", new string('x', 150), new[] { "Enabled" }, new string('y', 100));
        Assert.Equal(200, cut.Length);
        Assert.StartsWith("Secure Boot: ", cut, StringComparison.Ordinal);
    }

    [Fact]
    public void RowSentence_AppendsTheNote()
    {
        Assert.Equal(
            "Secure Boot: Enabled. Possible values: Enabled. Dell Command Monitor lists only Enabled; the BIOS setup also offers Disabled.",
            DellSentences.RowSentence("Secure Boot", "Enabled", new[] { "Enabled" }, DellSentences.SecureBootDcmNote));
        Assert.Equal(
            "Boot list option: not reported. This version reads it through Dell Command Monitor only.",
            DellSentences.RowSentence("Boot list option", DellSentences.NotReportedValue, Array.Empty<string>(), DellSentences.NotReadByFallbackNote));

        // The note survives a cut of the possible values.
        string[] many = Enumerable.Range(1, 30).Select(n => "Possible value " + n.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        string sentence = DellSentences.RowSentence("Secure Boot", "Enabled", many, DellSentences.SecureBootDcmNote);
        Assert.True(sentence.Length <= 200, sentence);
        Assert.EndsWith(" more. " + DellSentences.SecureBootDcmNote, sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void ValueText_GivesTheWordsOfEachState()
    {
        DellCatalogEntry activation = DellAttributeCatalog.Find("TpmActivation")!;
        SettingValue activated = new("2", "Activate", "Activated");

        Assert.Equal("Activated", DellSentences.ValueText(Make(activation, SettingState.Read, activated)));
        Assert.Equal("not applicable while TPM security is Disabled", DellSentences.ValueText(Make(activation, SettingState.NotApplicable, activated)));
        Assert.Equal("no value reported", DellSentences.ValueText(Make(activation, SettingState.NoValueReported, null)));
        Assert.Equal("a value Dell does not name", DellSentences.ValueText(Make(activation, SettingState.Unnamed, new SettingValue("7", null, DellSentences.UnnamedValue))));
        Assert.Equal("not reported", DellSentences.ValueText(Make(activation, SettingState.NotReported, null)));
        Assert.Equal("not read", DellSentences.ValueText(Make(activation, SettingState.NotRead, null)));

        // The row of a setting the BIOS setup hides keeps its possible values (section 5.4).
        FirmwareSetting hidden = Make(activation, SettingState.NotApplicable, activated) with
        {
            PossibleValues = new[] { new SettingValue("1", "Deactivate", "Deactivated"), activated },
        };
        Assert.Equal("TPM activation: not applicable while TPM security is Disabled. Possible values: Deactivated, Activated.", DellSentences.RowSentence(hidden));

        // A setting without a dependency in the catalog keeps its value's words.
        Assert.Equal("Enabled", DellSentences.ValueText(Make(DellAttributeCatalog.Find("NumLock")!, SettingState.NotApplicable, new SettingValue("2", "Enable", "Enabled"))));
    }

    private static DellSettingsResult Unavailable(IReadOnlyList<DellInterfaceProblem> problems, bool nativeInterfacePresent = false)
    {
        return new DellSettingsResult(
            DellReadOutcome.Unavailable,
            null,
            new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero),
            42,
            new DellMachine(null, null, null),
            DellAttributeCatalog.Default.Select(entry => Make(entry, SettingState.NotRead, null)).ToArray(),
            problems,
            Array.Empty<string>())
        {
            NativeInterfacePresent = nativeInterfacePresent,
        };
    }

    private static FirmwareSetting Make(DellCatalogEntry entry, SettingState state, SettingValue? current)
    {
        return new FirmwareSetting(entry.Id, entry.DcmAttributeName, entry.DisplayName, entry.Category, entry.SetupLocation, state, current, Array.Empty<SettingValue>(), PossibleValuesOrigin.Interface, null, false, null);
    }

    /// <summary>What no Dell sentence holds: "|", an en dash, a hexadecimal code, an exit or error number, or the words "read-only".</summary>
    private static void AssertSpeakable(string text)
    {
        Assert.DoesNotContain("|", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\u2013", text, StringComparison.Ordinal);
        Assert.DoesNotContain("read-only", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("read only", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(@"0x[0-9A-Fa-f]", text);
        Assert.DoesNotMatch(@"(?i)\b(?:code|exit|error|HRESULT)\s+\d", text);
    }
}
