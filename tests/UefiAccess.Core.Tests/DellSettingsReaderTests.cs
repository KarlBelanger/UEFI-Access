// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Reflection;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Settings;
using UefiAccess.Core.Settings.Dell;
using UefiAccess.Core.Tests.Fixtures;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The Dell settings reader: the machine check, the
/// order of the interfaces and the fallback, the read budget every step shares, the check of the
/// native namespace, the outcome and the dependency rule, the BIOS warning, the logging of the
/// agreed rows only, and that it never throws and keeps nothing between reads. The reader is
/// composed through its seam with hand-written providers, machine, native check, clock, elapsed
/// time and platform; the providers' answers are made by their pure functions over the fixtures.
/// No real provider takes part (each keeps per-process state that its own tests check), so nothing
/// here reads WMI or Dell Command | Monitor or starts Dell Command | Configure on this PC, the
/// OptiPlex 7020 itself (DellSettingsGuardTests.TestsNeverRunTheRealDellReader).
/// </summary>
public sealed class DellSettingsReaderTests
{
    private const DellSettingsSource DcmSource = DellSettingsSource.DellCommandMonitor;
    private const DellSettingsSource CctkSource = DellSettingsSource.DellCommandConfigure;

    private const string Lead = "Dell BIOS settings cannot be read. ";
    private const string Install = "Install the Microsoft Visual C++ 2015-2022 x64 runtime, then Dell Command Monitor, and try again; the Dell interface setup page of the documentation has the steps.";
    private const string Retry = "Try again; if it fails again, restart Windows and try again.";

    private static readonly IReadOnlyList<DellCatalogEntry> Catalog = DellAttributeCatalog.Default;

    private static readonly DateTimeOffset StartedAt = new(2026, 10, 5, 9, 12, 44, TimeSpan.FromHours(2));

    private static readonly DellMachine Unit = new("Dell Inc.", "OptiPlex 7020", "A18");

    private static readonly IReadOnlyDictionary<string, object?>[] NoRows = Array.Empty<IReadOnlyDictionary<string, object?>>();

    [Fact]
    public void Automatic_UsesDcmWhenItAnswers_AndNeverRunsCctk()
    {
        Rig rig = new();

        DellSettingsResult result = rig.Read();

        Assert.Equal(DellReadOutcome.Complete, result.Outcome);
        Assert.Equal(DcmSource, result.Source);
        Assert.Equal(StartedAt, result.ReadAt);
        Assert.Equal(Unit, result.Machine);
        Assert.Equal(DcmRowFixtures.UnitA18Texts, result.Settings.Select(DellSentences.ValueText));
        Assert.Equal("Num Lock at boot: Enabled. Possible values: Disabled, Enabled.", DellSentences.RowSentence(result.Settings[9]));
        Assert.All(result.Settings, setting => Assert.Equal(PossibleValuesOrigin.Interface, setting.PossibleValuesOrigin));
        Assert.Empty(result.Problems);
        Assert.Empty(result.Warnings);
        Assert.Null(result.FallbackSentence);
        Assert.Null(result.AllAttributes);
        Assert.False(result.NativeInterfacePresent);
        Assert.Null(DellSentences.UnavailableSentence(result));

        Assert.Single(rig.Dcm.Reads);
        Assert.Same(Catalog, rig.Dcm.Reads[0].Catalog);
        Assert.False(rig.Dcm.Reads[0].IncludeAll);
        Assert.Empty(rig.Cctk.Reads);
        Assert.Empty(rig.NativeRemaining);
        Assert.Single(rig.MachineRemaining);
    }

    [Fact]
    public void Automatic_DcmNotInstalled_FallsBackToCctk_WithTheFallbackSentence()
    {
        Rig rig = new() { Dcm = Provider(DcmSource, DcmNotInstalled()) };

        DellSettingsResult result = rig.Read();

        Assert.Single(rig.Dcm.Reads);
        Assert.Single(rig.Cctk.Reads);
        Assert.Equal(DellReadOutcome.Partial, result.Outcome);
        Assert.Equal(CctkSource, result.Source);
        Assert.Equal("Dell Command Monitor is not installed. Dell Command Configure was used instead.", result.FallbackSentence);
        Assert.Equal(new[] { new DellInterfaceProblem(DellInterface.DellCommandMonitor, DellProblemKind.NotInstalled, @"the namespace root\dcim\sysman is absent") }, result.Problems);

        // The fallback sentence is not a warning, and the result is usable: no problem sentence.
        Assert.Empty(result.Warnings);
        Assert.Null(DellSentences.UnavailableSentence(result));

        // Every row is the fallback's: Boot list option not reported with its note, the other nine read with the catalog's possible values.
        Assert.Equal(new[] { DellSentences.NotReportedValue }.Concat(DcmRowFixtures.UnitA18Texts.Skip(1)), result.Settings.Select(DellSentences.ValueText));
        Assert.Equal("Boot list option: not reported. This version reads it through Dell Command Monitor only.", DellSentences.RowSentence(result.Settings[0]));
        Assert.All(result.Settings, setting => Assert.Equal(PossibleValuesOrigin.Catalog, setting.PossibleValuesOrigin));
        Assert.All(result.Settings, setting => Assert.Null(setting.VendorMarksReadOnly));

        // Something answered, so the native namespace is not checked.
        Assert.Empty(rig.NativeRemaining);
        Assert.False(result.NativeInterfacePresent);
    }

    [Fact]
    public void Automatic_ProviderLoadFailure_FallsBack()
    {
        Rig rig = new() { Dcm = Provider(DcmSource, DcmFailure(WmiFailureKind.ProviderLoadFailure, "Dell BIOS settings: COMException 0x80041013: Provider load failure")) };

        DellSettingsResult result = rig.Read();

        Assert.Single(rig.Cctk.Reads);
        Assert.Equal(CctkSource, result.Source);
        Assert.Equal("Dell Command Monitor is installed, but its WMI provider did not load. Dell Command Configure was used instead.", result.FallbackSentence);
        Assert.Equal(DellProblemKind.ProviderLoadFailure, Assert.Single(result.Problems).Kind);
        Assert.Empty(rig.NativeRemaining);
    }

    [Fact]
    public void Automatic_NoInstances_FallsBack()
    {
        Rig rig = new() { Dcm = Provider(DcmSource, DcmSettingsProvider.FromOutcome(DcmRowFixtures.Answer(NoRows), null, Catalog, false)) };

        DellSettingsResult result = rig.Read();

        Assert.Single(rig.Cctk.Reads);
        Assert.Equal(CctkSource, result.Source);
        Assert.Equal("Dell Command Monitor answered with no BIOS settings. Dell Command Configure was used instead.", result.FallbackSentence);
        Assert.Equal(DellProblemKind.NoInstances, Assert.Single(result.Problems).Kind);
        Assert.Equal(DellReadOutcome.Partial, result.Outcome);
    }

    [Fact]
    public void Automatic_DcmTimesOut_FallsBackWithinTheBudget()
    {
        // Dell Command Monitor hangs for its whole 60-second bound: the fallback gets what is left.
        Rig rig = new();
        rig.Dcm = Provider(DcmSource, _ =>
        {
            rig.Now += TimeSpan.FromSeconds(60);
            return DcmTimedOut();
        });
        rig.Cctk = Provider(CctkSource, _ =>
        {
            rig.Now += TimeSpan.FromSeconds(2);
            return CctkUnitA18();
        });

        DellSettingsResult result = rig.Read();

        Assert.Equal(TimeSpan.FromSeconds(60), Assert.Single(rig.Cctk.Reads).Remaining);
        Assert.Same(rig.Dcm.Reads[0].Budget, rig.Cctk.Reads[0].Budget);
        Assert.Equal(CctkSource, result.Source);
        Assert.Equal("Dell Command Monitor did not answer within 60 seconds. Dell Command Configure was used instead.", result.FallbackSentence);
        Assert.Equal(DellProblemKind.TimedOut, Assert.Single(result.Problems).Kind);
        Assert.Equal(62000, result.ElapsedMs);

        // An earlier query of this process still running is a timeout at once (DF8): the fallback
        // gets the whole budget, and nothing waits for the hung query.
        Rig hung = new()
        {
            Dcm = Provider(DcmSource, NotAnswered(new DellInterfaceProblem(DellInterface.DellCommandMonitor, DellProblemKind.TimedOut, "an earlier query has not finished"))),
        };
        DellSettingsResult again = hung.Read();
        Assert.Equal(TimeSpan.FromSeconds(120), Assert.Single(hung.Cctk.Reads).Remaining);
        Assert.Equal(CctkSource, again.Source);
        Assert.Equal("Dell Command Monitor did not answer within 60 seconds. Dell Command Configure was used instead.", again.FallbackSentence);

        // A hang that used up the whole budget leaves nothing for the fallback.
        Rig spent = new();
        spent.Dcm = Provider(DcmSource, _ =>
        {
            spent.Now += TimeSpan.FromSeconds(120);
            return DcmTimedOut();
        });
        DellSettingsResult unavailable = spent.Read();
        Assert.Empty(spent.Cctk.Reads);
        Assert.Equal(DellReadOutcome.Unavailable, unavailable.Outcome);
        Assert.Equal(
            new[] { (DellInterface.DellCommandMonitor, DellProblemKind.TimedOut), (DellInterface.Reader, DellProblemKind.BudgetExhausted) },
            unavailable.Problems.Select(problem => (problem.Interface, problem.Kind)));
        Assert.Equal(Lead + "Dell Command Monitor did not answer within 60 seconds. Reading stopped at the time limit of 120 seconds. " + Retry, DellSentences.UnavailableSentence(unavailable));
    }

    [Fact]
    public void Automatic_NothingAnswers_IsUnavailable_WithBothCauses()
    {
        Rig rig = new() { Dcm = Provider(DcmSource, DcmNotInstalled()), Cctk = Provider(CctkSource, CctkNotFound()) };

        DellSettingsResult result = rig.Read();

        Assert.Equal(DellReadOutcome.Unavailable, result.Outcome);
        Assert.Null(result.Source);
        Assert.Null(result.FallbackSentence);
        Assert.Null(result.AllAttributes);
        Assert.Empty(result.Warnings);
        Assert.Equal(
            new[] { (DellInterface.DellCommandMonitor, DellProblemKind.NotInstalled), (DellInterface.DellCommandConfigure, DellProblemKind.ProgramNotFound) },
            result.Problems.Select(problem => (problem.Interface, problem.Kind)));
        AssertEveryRowNotRead(result);

        // the tester's likely case, the documented example of section 2.4.
        Assert.Equal(
            "Dell BIOS settings cannot be read. Dell Command Monitor is not installed. Dell Command Configure was not found. Install the Microsoft Visual C++ 2015-2022 x64 runtime, then Dell Command Monitor, and try again; the Dell interface setup page of the documentation has the steps.",
            DellSentences.UnavailableSentence(result));
        Assert.Single(rig.NativeRemaining);
    }

    [Fact]
    public void Automatic_DoesNotMixSources()
    {
        // Dell Command Monitor answers without Num Lock: the row stays not reported; the fallback, which would read it, never runs.
        Rig partial = new() { Dcm = Provider(DcmSource, DcmRead(DcmRowFixtures.WithoutNumLock())) };
        DellSettingsResult result = partial.Read();
        Assert.Empty(partial.Cctk.Reads);
        Assert.Equal(DcmSource, result.Source);
        Assert.Equal(DellReadOutcome.Partial, result.Outcome);
        Assert.Equal(SettingState.NotReported, result.Settings[9].State);
        Assert.All(result.Settings.Take(9), setting => Assert.Equal(PossibleValuesOrigin.Interface, setting.PossibleValuesOrigin));

        // A provider that did not answer gives no row, even one it calls read: every row is the fallback's.
        DellProviderRead unanswered = DcmRead(DcmRowFixtures.UnitA18()) with
        {
            Answered = false,
            Problems = new[] { new DellInterfaceProblem(DellInterface.DellCommandMonitor, DellProblemKind.QueryFailed, "scripted") },
        };
        Rig fallback = new() { Dcm = Provider(DcmSource, unanswered) };
        DellSettingsResult fromCctk = fallback.Read();
        Assert.Equal(CctkSource, fromCctk.Source);
        Assert.Equal(SettingState.NotReported, fromCctk.Settings[0].State);
        Assert.All(fromCctk.Settings, setting => Assert.Equal(PossibleValuesOrigin.Catalog, setting.PossibleValuesOrigin));
        Assert.Equal("Dell Command Monitor could not be queried. Dell Command Configure was used instead.", fromCctk.FallbackSentence);

        // When neither answered, no row of either is shown.
        Rig neither = new()
        {
            Dcm = Provider(DcmSource, unanswered),
            Cctk = Provider(CctkSource, CctkUnitA18() with { Answered = false }),
        };
        DellSettingsResult unavailable = neither.Read();
        Assert.Equal(DellReadOutcome.Unavailable, unavailable.Outcome);
        AssertEveryRowNotRead(unavailable);
    }

    [Fact]
    public void PreferenceDcm_NeverRunsCctk()
    {
        DellReadOptions dcmOnly = new(DellSourcePreference.DellCommandMonitor, false);

        Rig missing = new() { Dcm = Provider(DcmSource, DcmNotInstalled()) };
        DellSettingsResult unavailable = missing.Read(dcmOnly);
        Assert.Empty(missing.Cctk.Reads);
        Assert.Equal(DellReadOutcome.Unavailable, unavailable.Outcome);
        Assert.Equal(DellProblemKind.NotInstalled, Assert.Single(unavailable.Problems).Kind);
        Assert.Equal(Lead + "Dell Command Monitor is not installed. " + Install, DellSentences.UnavailableSentence(unavailable));

        // The full enumeration is passed through when asked for, and only then.
        Rig all = new();
        DellSettingsResult enumerated = all.Read(new DellReadOptions(DellSourcePreference.DellCommandMonitor, true));
        Assert.Empty(all.Cctk.Reads);
        Assert.True(Assert.Single(all.Dcm.Reads).IncludeAll);
        Assert.Equal(DcmSource, enumerated.Source);
        Assert.Equal(72, enumerated.AllAttributes!.Count);

        Rig unasked = new() { Dcm = Provider(DcmSource, DcmRead(DcmRowFixtures.UnitA18(), includeAll: true)) };
        Assert.Null(unasked.Read(dcmOnly).AllAttributes);
    }

    [Fact]
    public void PreferenceCctk_NeverQueriesDcm()
    {
        DellReadOptions cctkOnly = new(DellSourcePreference.DellCommandConfigure, false);

        Rig rig = new();
        DellSettingsResult result = rig.Read(cctkOnly);
        Assert.Empty(rig.Dcm.Reads);
        Assert.Single(rig.Cctk.Reads);
        Assert.Equal(CctkSource, result.Source);
        Assert.Equal(DellReadOutcome.Partial, result.Outcome);
        Assert.Null(result.FallbackSentence);
        Assert.Empty(result.Problems);

        // Without the program: unavailable, and no advice and no native check, since Dell Command Monitor was not tried.
        Rig missing = new() { Cctk = Provider(CctkSource, CctkNotFound()) };
        DellSettingsResult unavailable = missing.Read(cctkOnly);
        Assert.Empty(missing.Dcm.Reads);
        Assert.Empty(missing.NativeRemaining);
        Assert.Equal(DellReadOutcome.Unavailable, unavailable.Outcome);
        Assert.Equal("Dell BIOS settings cannot be read. Dell Command Configure was not found.", DellSentences.UnavailableSentence(unavailable));
    }

    [Fact]
    public void UnknownPreference_TriesNeitherInterface()
    {
        // Each interface is tried only for the preferences that name it, so a value of no member
        // never reaches the one that starts a process.
        foreach (DellSourcePreference unknown in new[] { (DellSourcePreference)7, (DellSourcePreference)(-1) })
        {
            Rig rig = new() { Dcm = Provider(DcmSource, DcmNotInstalled()), NativePresent = true };

            DellSettingsResult result = rig.Read(new DellReadOptions(unknown, false));

            Assert.Empty(rig.Dcm.Reads);
            Assert.Empty(rig.Cctk.Reads);
            Assert.Empty(rig.NativeRemaining);
            Assert.Equal(DellReadOutcome.Unavailable, result.Outcome);
            AssertEveryRowNotRead(result);
            Assert.Equal(
                new DellInterfaceProblem(DellInterface.Reader, DellProblemKind.Unexpected, "the source preference " + ((int)unknown).ToString(CultureInfo.InvariantCulture) + " is not one this version knows, so no interface was tried"),
                Assert.Single(result.Problems));
            Assert.Equal(Lead + "An unexpected error occurred; the log has the details. " + Retry, DellSentences.UnavailableSentence(result));
        }
    }

    [Fact]
    public void NotWindows_IsUnavailable()
    {
        Rig rig = new() { IsWindows = false };

        DellSettingsResult result = rig.Read();

        Assert.Equal(DellReadOutcome.Unavailable, result.Outcome);
        Assert.Equal(new DellInterfaceProblem(DellInterface.Reader, DellProblemKind.NotWindows, "the Dell settings are read through WMI and Windows programs, which are only available on Windows"), Assert.Single(result.Problems));
        Assert.Equal(new DellMachine(null, null, null), result.Machine);
        Assert.Equal(StartedAt, result.ReadAt);
        AssertEveryRowNotRead(result);
        Assert.Equal("Dell BIOS settings cannot be read. They can be read on Windows only.", DellSentences.UnavailableSentence(result));

        // Nothing is read: not the machine, not an interface, not the namespace.
        Assert.Empty(rig.MachineRemaining);
        Assert.Empty(rig.Dcm.Reads);
        Assert.Empty(rig.Cctk.Reads);
        Assert.Empty(rig.NativeRemaining);
    }

    [Fact]
    public void NotADell_RunsNeitherInterface()
    {
        Rig rig = new() { Machine = new DellMachine(" LENOVO ", "ThinkCentre M93p", "FBKTB4AUS") };

        DellSettingsResult result = rig.Read();

        Assert.Empty(rig.Dcm.Reads);
        Assert.Empty(rig.Cctk.Reads);
        Assert.Empty(rig.NativeRemaining);
        Assert.Equal(DellReadOutcome.Unavailable, result.Outcome);
        Assert.Equal(new DellInterfaceProblem(DellInterface.Machine, DellProblemKind.NotDell, "LENOVO"), Assert.Single(result.Problems));
        Assert.Equal("Dell BIOS settings cannot be read. This computer's manufacturer is LENOVO, not Dell, so there are no Dell BIOS settings to show.", DellSentences.UnavailableSentence(result));
        Assert.Empty(result.Warnings);
        AssertEveryRowNotRead(result);

        // Dell in any casing is Dell; an unknown manufacturer is never taken for another maker's.
        foreach (string? manufacturer in new[] { "Dell Inc.", "DELL INC.", "dell", " Dell Inc. ", null, string.Empty, "   " })
        {
            Assert.Null(DellSettingsReader.OtherManufacturer(manufacturer));
            Rig dell = new() { Machine = new DellMachine(manufacturer, null, null) };
            Assert.Equal(DellReadOutcome.Complete, dell.Read().Outcome);
            Assert.Single(dell.Dcm.Reads);
        }

        Assert.Equal("HP", DellSettingsReader.OtherManufacturer(" HP "));
        Assert.Equal("Microsoft Corporation", DellSettingsReader.OtherManufacturer("Microsoft Corporation"));
    }

    [Fact]
    public void NativeNamespace_IsCheckedOnlyWhenDcmIsNotInstalledAndNothingAnswered()
    {
        // Nothing answered and Dell Command Monitor is not installed: checked, within what is left of the budget.
        Rig native = new() { Dcm = Provider(DcmSource, DcmNotInstalled()), Cctk = Provider(CctkSource, CctkNotFound()), NativePresent = true, MachineTakes = TimeSpan.FromSeconds(5) };
        DellSettingsResult present = native.Read();
        Assert.Equal(new[] { TimeSpan.FromSeconds(115) }, native.NativeRemaining);
        Assert.True(present.NativeInterfacePresent);
        Assert.Equal(DellReadOutcome.Unavailable, present.Outcome);
        Assert.Equal(new DellInterfaceProblem(DellInterface.NativeBiosInterface, DellProblemKind.NativeInterfaceOnly, @"the namespace root\dcim\sysman\biosattributes exists; this version does not read it"), present.Problems[^1]);
        Assert.Equal(
            "Dell BIOS settings cannot be read. Dell Command Monitor is not installed. Dell Command Configure was not found. This computer has the built-in Dell BIOS interface of newer models, which this version does not read.",
            DellSentences.UnavailableSentence(present));

        // Absent, or not to be told: no clause, the install advice.
        foreach (bool? answer in new bool?[] { false, null })
        {
            Rig absent = new() { Dcm = Provider(DcmSource, DcmNotInstalled()), Cctk = Provider(CctkSource, CctkNotFound()), NativePresent = answer };
            DellSettingsResult result = absent.Read();
            Assert.Single(absent.NativeRemaining);
            Assert.False(result.NativeInterfacePresent);
            Assert.DoesNotContain(result.Problems, problem => problem.Kind == DellProblemKind.NativeInterfaceOnly);
            Assert.EndsWith(Install, DellSentences.UnavailableSentence(result), StringComparison.Ordinal);
        }

        // With Dell Command Monitor alone, not installed: checked too.
        Rig dcmOnly = new() { Dcm = Provider(DcmSource, DcmNotInstalled()), NativePresent = true };
        Assert.True(dcmOnly.Read(new DellReadOptions(DellSourcePreference.DellCommandMonitor, false)).NativeInterfacePresent);
        Assert.Single(dcmOnly.NativeRemaining);

        // Never when something answered, when Dell Command Monitor failed otherwise, or when it was not tried.
        Rig answered = new() { Dcm = Provider(DcmSource, DcmNotInstalled()), NativePresent = true };
        Assert.False(answered.Read().NativeInterfacePresent);
        Rig classMissing = new() { Dcm = Provider(DcmSource, DcmSettingsProvider.FromOutcome(Absent(), true, Catalog, false)), Cctk = Provider(CctkSource, CctkNotFound()), NativePresent = true };
        Assert.False(classMissing.Read().NativeInterfacePresent);
        Rig loadFailure = new() { Dcm = Provider(DcmSource, DcmFailure(WmiFailureKind.ProviderLoadFailure, "Dell BIOS settings: COMException 0x80041013: Provider load failure")), Cctk = Provider(CctkSource, CctkNotFound()), NativePresent = true };
        Assert.False(loadFailure.Read().NativeInterfacePresent);
        Rig cctkOnly = new() { Cctk = Provider(CctkSource, CctkNotFound()), NativePresent = true };
        Assert.False(cctkOnly.Read(new DellReadOptions(DellSourcePreference.DellCommandConfigure, false)).NativeInterfacePresent);
        Assert.All(new[] { answered, classMissing, loadFailure, cctkOnly }, rig => Assert.Empty(rig.NativeRemaining));
    }

    [Fact]
    public void Result_AlwaysOneSettingPerCatalogEntryInOrder()
    {
        // A provider's rows out of order, one twice, one missing and one of no catalog entry.
        DellProviderRead unit = DcmRead(DcmRowFixtures.UnitA18());
        FirmwareSetting stranger = unit.Settings[0] with { Id = "NotInTheCatalog" };
        FirmwareSetting second = unit.Settings[9] with { Current = unit.Settings[9].Current! with { Text = "Second" } };
        FirmwareSetting[] scrambled = unit.Settings.Reverse().Where(setting => setting.Id != "WakeOnLan").Append(stranger).Append(second).ToArray();
        Rig rig = new() { Dcm = Provider(DcmSource, unit with { Settings = scrambled }) };

        DellSettingsResult result = rig.Read();

        AssertOnePerEntry(result);
        Assert.Equal(SettingState.NotRead, result.Settings[8].State);
        Assert.Equal("Enabled", DellSentences.ValueText(result.Settings[9]));
        Assert.Equal(DellReadOutcome.Partial, result.Outcome);
        Assert.All(DellSettingsReader.InCatalogOrder(Catalog, null), setting => Assert.Equal(SettingState.NotRead, setting.State));

        // Every kind of result holds the ten rows in catalog order.
        AssertOnePerEntry(new Rig().Read());
        AssertOnePerEntry(new Rig { Dcm = Provider(DcmSource, DcmNotInstalled()) }.Read());
        AssertOnePerEntry(new Rig { Dcm = Provider(DcmSource, DcmNotInstalled()), Cctk = Provider(CctkSource, CctkNotFound()) }.Read());
        AssertOnePerEntry(new Rig { IsWindows = false }.Read());
        AssertOnePerEntry(new Rig { Machine = new DellMachine("LENOVO", null, null) }.Read());
        AssertOnePerEntry(new Rig { Dcm = Provider(DcmSource, _ => throw new InvalidOperationException("scripted failure")), Cctk = Provider(CctkSource, _ => throw new InvalidOperationException("scripted failure")) }.Read());
        AssertOnePerEntry(new Rig().Read(null!));
    }

    [Fact]
    public void TpmActivation_IsNotApplicableWhileTpmSecurityIsDisabled()
    {
        DellSettingsResult result = new Rig { Dcm = Provider(DcmSource, DcmRead(DcmRowFixtures.WithTpmDisabled())) }.Read();

        FirmwareSetting security = Setting(result, "TpmSecurity");
        FirmwareSetting activation = Setting(result, "TpmActivation");
        Assert.Equal("Disabled", DellSentences.ValueText(security));
        Assert.Equal(SettingState.NotApplicable, activation.State);
        Assert.Equal(new SettingValue("2", "Activate", "Activated"), activation.Current);
        Assert.Equal("not applicable while TPM security is Disabled", DellSentences.ValueText(activation));
        Assert.Equal("TPM activation: not applicable while TPM security is Disabled. Possible values: Deactivated, Activated.", DellSentences.RowSentence(activation));
        Assert.Equal(DellReadOutcome.Complete, result.Outcome);

        // TPM security Enabled: the row is read as it is.
        Assert.Equal(SettingState.Read, Setting(new Rig().Read(), "TpmActivation").State);

        // The same rule in the fallback's words.
        Rig fallback = new()
        {
            Dcm = Provider(DcmSource, DcmNotInstalled()),
            Cctk = Provider(CctkSource, CctkRead(option => option == CctkReadOption.TpmSecurity ? "TpmSecurity=Disabled" : CctkOutputFixtures.UnitA18Output(option))),
        };
        Assert.Equal(SettingState.NotApplicable, Setting(fallback.Read(), "TpmActivation").State);

        // TPM security not read, or read as a word of Dell's own: nothing is decided, the row stays as read.
        IReadOnlyList<FirmwareSetting> unit = DcmRead(DcmRowFixtures.UnitA18()).Settings;
        FirmwareSetting[] notReported = unit.Select(setting => setting.Id == "TpmSecurity" ? DellSettingMapper.NotReported(Entry("TpmSecurity"), PossibleValuesOrigin.Interface, null) : setting).ToArray();
        Assert.Equal(SettingState.Read, Setting(DellSettingsReader.ApplyDependencies(Catalog, notReported), "TpmActivation").State);
        FirmwareSetting[] vendorWord = unit.Select(setting => setting.Id == "TpmSecurity" ? setting with { Current = new SettingValue("3", "Disabled", "Disabled"), DisplayFromVendor = true } : setting).ToArray();
        Assert.Equal(SettingState.Read, Setting(DellSettingsReader.ApplyDependencies(Catalog, vendorWord), "TpmActivation").State);

        // A row without a value read stays as it is.
        FirmwareSetting[] noValue = DcmRead(DcmRowFixtures.WithTpmDisabled()).Settings
            .Select(setting => setting.Id == "TpmActivation" ? DellSettingMapper.NotReported(Entry("TpmActivation"), PossibleValuesOrigin.Interface, null) : setting)
            .ToArray();
        Assert.Equal(SettingState.NotReported, Setting(DellSettingsReader.ApplyDependencies(Catalog, noValue), "TpmActivation").State);
    }

    [Fact]
    public void Outcome_IsCompletePartialOrUnavailable()
    {
        Assert.Equal(DellReadOutcome.Complete, new Rig().Read().Outcome);
        Assert.Equal(DellReadOutcome.Complete, new Rig { Dcm = Provider(DcmSource, DcmRead(DcmRowFixtures.WithTpmDisabled())) }.Read().Outcome);
        Assert.Equal(DellReadOutcome.Partial, new Rig { Dcm = Provider(DcmSource, DcmRead(DcmRowFixtures.WithoutNumLock())) }.Read().Outcome);
        Assert.Equal(DellReadOutcome.Partial, new Rig { Dcm = Provider(DcmSource, DcmRead(DcmRowFixtures.WithUnknownCode())) }.Read().Outcome);
        Assert.Equal(DellReadOutcome.Partial, new Rig { Dcm = Provider(DcmSource, DcmRead(DcmRowFixtures.WithoutSecureBootCurrent())) }.Read().Outcome);
        Assert.Equal(DellReadOutcome.Partial, new Rig { Dcm = Provider(DcmSource, DcmNotInstalled()) }.Read().Outcome);
        Assert.Equal(DellReadOutcome.Unavailable, new Rig { Dcm = Provider(DcmSource, DcmNotInstalled()), Cctk = Provider(CctkSource, CctkNotFound()) }.Read().Outcome);

        // The fallback does not by itself make a result partial: a fallback that read every row is complete.
        DellProviderRead everyRow = CctkUnitA18() with { Settings = DcmRead(DcmRowFixtures.UnitA18()).Settings };
        DellSettingsResult complete = new Rig { Dcm = Provider(DcmSource, DcmNotInstalled()), Cctk = Provider(CctkSource, everyRow) }.Read();
        Assert.Equal(CctkSource, complete.Source);
        Assert.Equal(DellReadOutcome.Complete, complete.Outcome);
    }

    [Fact]
    public void OtherBios_AddsTheWarning()
    {
        const string Warning = "These names and values were checked against BIOS A18 only; this computer runs BIOS A12.";

        DellSettingsResult result = new Rig { Machine = Unit with { BiosVersion = "A12" } }.Read();
        Assert.Equal(new[] { Warning }, result.Warnings);
        Assert.Equal(DellReadOutcome.Complete, result.Outcome);

        // After the fallback the warning stands alone: the fallback sentence is never a warning.
        DellSettingsResult fallback = new Rig { Machine = Unit with { BiosVersion = "A12" }, Dcm = Provider(DcmSource, DcmNotInstalled()) }.Read();
        Assert.Equal(new[] { Warning }, fallback.Warnings);
        Assert.NotNull(fallback.FallbackSentence);

        // The verified BIOS, an unknown one, or a result that is not usable: no warning.
        Assert.Empty(new Rig().Read().Warnings);
        Assert.Empty(new Rig { Machine = Unit with { BiosVersion = " A18 " } }.Read().Warnings);
        Assert.Empty(new Rig { Machine = Unit with { BiosVersion = null } }.Read().Warnings);
        Assert.Empty(new Rig { Machine = Unit with { BiosVersion = "A12" }, Dcm = Provider(DcmSource, DcmNotInstalled()), Cctk = Provider(CctkSource, CctkNotFound()) }.Read().Warnings);
        Assert.Null(DellSettingsReader.OtherBios("A18"));
        Assert.Equal("A12", DellSettingsReader.OtherBios(" A12 "));
    }

    [Fact]
    public void ProviderThrows_ReadNeverThrows()
    {
        // Dell Command Monitor throws: the reader's unexpected problem, and the fallback as for any failure.
        Rig dcmThrows = new() { Dcm = Provider(DcmSource, _ => throw new InvalidOperationException("scripted failure")) };
        DellSettingsResult fallback = dcmThrows.Read();
        Assert.Equal(CctkSource, fallback.Source);
        Assert.Equal(new DellInterfaceProblem(DellInterface.Reader, DellProblemKind.Unexpected, "Dell Command Monitor: InvalidOperationException: scripted failure"), Assert.Single(fallback.Problems));
        Assert.Equal("An unexpected error occurred; the log has the details. Dell Command Configure was used instead.", fallback.FallbackSentence);

        // Both throw: unavailable, one cause said once.
        Rig bothThrow = new()
        {
            Dcm = Provider(DcmSource, _ => throw new InvalidOperationException("scripted failure")),
            Cctk = Provider(CctkSource, _ => throw new InvalidOperationException("scripted failure")),
        };
        DellSettingsResult unavailable = bothThrow.Read();
        Assert.Equal(DellReadOutcome.Unavailable, unavailable.Outcome);
        Assert.Equal(2, unavailable.Problems.Count);
        Assert.All(unavailable.Problems, problem => Assert.Equal((DellInterface.Reader, DellProblemKind.Unexpected), (problem.Interface, problem.Kind)));
        Assert.Equal(Lead + "An unexpected error occurred; the log has the details. " + Retry, DellSentences.UnavailableSentence(unavailable));

        // A provider that returns nothing gave no answer, and that is said, never left without a cause.
        DellSettingsResult nothing = new Rig { Dcm = Provider(DcmSource, _ => null!), Cctk = Provider(CctkSource, _ => null!) }.Read();
        Assert.Equal(DellReadOutcome.Unavailable, nothing.Outcome);
        Assert.Equal(
            new[]
            {
                new DellInterfaceProblem(DellInterface.Reader, DellProblemKind.Unexpected, "Dell Command Monitor: returned no result"),
                new DellInterfaceProblem(DellInterface.Reader, DellProblemKind.Unexpected, "Dell Command Configure: returned no result"),
            },
            nothing.Problems);
        Assert.Equal(Lead + "An unexpected error occurred; the log has the details. " + Retry, DellSentences.UnavailableSentence(nothing));
        AssertEveryRowNotRead(nothing);

        // The machine, the native check, the clock, the elapsed time or the options failing: unavailable, never a throw.
        Rig machineThrows = new() { MachineFails = true };
        DellSettingsResult machine = machineThrows.Read();
        Assert.Equal(DellReadOutcome.Unavailable, machine.Outcome);
        Assert.Equal(new DellInterfaceProblem(DellInterface.Reader, DellProblemKind.Unexpected, "InvalidOperationException: scripted machine failure"), Assert.Single(machine.Problems));
        Assert.Empty(machineThrows.Dcm.Reads);

        Rig nativeThrows = new() { Dcm = Provider(DcmSource, DcmNotInstalled()), Cctk = Provider(CctkSource, CctkNotFound()), NativeFails = true };
        DellSettingsResult native = nativeThrows.Read();
        Assert.Equal(
            new[] { DellProblemKind.NotInstalled, DellProblemKind.ProgramNotFound, DellProblemKind.Unexpected },
            native.Problems.Select(problem => problem.Kind));
        Assert.Equal(Unit, native.Machine);

        DellSettingsResult noOptions = new Rig().Read(null!);
        Assert.Equal(DellProblemKind.Unexpected, Assert.Single(noOptions.Problems).Kind);

        DellSettingsResult noClock = new Rig { ClockFails = true }.Read();
        Assert.Equal(DellProblemKind.Unexpected, Assert.Single(noClock.Problems).Kind);
        Assert.Equal(default, noClock.ReadAt);
        Assert.Equal(0, noClock.ElapsedMs);

        DellSettingsResult noTime = new Rig { ElapsedFails = true }.Read();
        Assert.Equal(DellProblemKind.Unexpected, Assert.Single(noTime.Problems).Kind);
        Assert.Equal(0, noTime.ElapsedMs);
        AssertOnePerEntry(noTime);
    }

    [Fact]
    public void Budget_IsSharedAcrossSteps()
    {
        Assert.Equal(TimeSpan.FromSeconds(120), DellSettingsReader.ReadBudget);
        Assert.Equal("Reading stopped at the time limit of 120 seconds.", DellSentences.Cause(new DellInterfaceProblem(DellInterface.Reader, DellProblemKind.BudgetExhausted, "budget")));

        // The machine takes 30 seconds, Dell Command Monitor 60 and times out, the fallback 20: one budget for all three.
        Rig rig = new() { MachineTakes = TimeSpan.FromSeconds(30) };
        rig.Dcm = Provider(DcmSource, _ =>
        {
            rig.Now += TimeSpan.FromSeconds(60);
            return DcmTimedOut();
        });
        rig.Cctk = Provider(CctkSource, _ =>
        {
            rig.Now += TimeSpan.FromSeconds(20);
            return CctkUnitA18();
        });
        DellSettingsReader reader = rig.Reader();

        DellSettingsResult result = reader.Read(DellReadOptions.Default);

        Assert.Equal(new[] { TimeSpan.FromSeconds(120) }, rig.MachineRemaining);
        Assert.Equal(TimeSpan.FromSeconds(90), rig.Dcm.Reads[0].Remaining);
        Assert.Equal(TimeSpan.FromSeconds(30), rig.Cctk.Reads[0].Remaining);
        DellReadBudget budget = rig.MachineBudgets[0];
        Assert.Same(budget, rig.Dcm.Reads[0].Budget);
        Assert.Same(budget, rig.Cctk.Reads[0].Budget);
        Assert.Equal(DellSettingsReader.ReadBudget, budget.Total);
        Assert.Equal(110000, result.ElapsedMs);
        Assert.Equal(CctkSource, result.Source);

        // The next read has a budget of its own, measured from its own start.
        reader.Read(DellReadOptions.Default);
        Assert.Equal(TimeSpan.FromSeconds(120), rig.MachineRemaining[1]);
        Assert.NotSame(budget, rig.MachineBudgets[1]);

        // The machine uses the whole budget: no interface is tried.
        Rig slowMachine = new() { MachineTakes = TimeSpan.FromSeconds(120) };
        DellSettingsResult skipped = slowMachine.Read();
        Assert.Empty(slowMachine.Dcm.Reads);
        Assert.Empty(slowMachine.Cctk.Reads);
        Assert.Equal(new DellInterfaceProblem(DellInterface.Reader, DellProblemKind.BudgetExhausted, "nothing was left of the read budget for Dell Command Monitor"), Assert.Single(skipped.Problems));
        Assert.Equal(Lead + "Reading stopped at the time limit of 120 seconds. " + Retry, DellSentences.UnavailableSentence(skipped));
        AssertEveryRowNotRead(skipped);

        // Dell Command Monitor uses the rest: neither the fallback nor the native check runs, and the budget is one problem.
        Rig slowDcm = new() { NativePresent = true };
        slowDcm.Dcm = Provider(DcmSource, _ =>
        {
            slowDcm.Now += TimeSpan.FromSeconds(125);
            return DcmNotInstalled();
        });
        DellSettingsResult spent = slowDcm.Read();
        Assert.Empty(slowDcm.Cctk.Reads);
        Assert.Empty(slowDcm.NativeRemaining);
        Assert.False(spent.NativeInterfacePresent);
        Assert.Equal(
            new[] { (DellInterface.DellCommandMonitor, DellProblemKind.NotInstalled), (DellInterface.Reader, DellProblemKind.BudgetExhausted) },
            spent.Problems.Select(problem => (problem.Interface, problem.Kind)));
        Assert.Equal(Lead + "Dell Command Monitor is not installed. Reading stopped at the time limit of 120 seconds. " + Install, DellSentences.UnavailableSentence(spent));
        Assert.Equal(125000, spent.ElapsedMs);

        // Ten seconds left: the fallback is still asked, and decides itself that no run fits
        // (CctkSettingsProviderTests); its stop is the reader's budget problem (DF21), said once.
        Rig tenLeft = new() { Cctk = Provider(CctkSource, NotAnswered(new DellInterfaceProblem(DellInterface.Reader, DellProblemKind.BudgetExhausted, "less than one run timeout was left of the read budget"))) };
        tenLeft.Dcm = Provider(DcmSource, _ =>
        {
            tenLeft.Now += TimeSpan.FromSeconds(110);
            return DcmTimedOut();
        });
        DellSettingsResult stopped = tenLeft.Read();
        Assert.Equal(TimeSpan.FromSeconds(10), Assert.Single(tenLeft.Cctk.Reads).Remaining);
        Assert.Equal(
            new[] { (DellInterface.DellCommandMonitor, DellProblemKind.TimedOut), (DellInterface.Reader, DellProblemKind.BudgetExhausted) },
            stopped.Problems.Select(problem => (problem.Interface, problem.Kind)));
        Assert.Equal(Lead + "Dell Command Monitor did not answer within 60 seconds. Reading stopped at the time limit of 120 seconds. " + Retry, DellSentences.UnavailableSentence(stopped));
    }

    [Fact]
    public void DellMachine_HasExactlyManufacturerModelAndBiosVersion()
    {
        string[] expected = { "Manufacturer", "Model", "BiosVersion" };
        PropertyInfo[] properties = typeof(DellMachine).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.Equal(expected.Order(StringComparer.Ordinal), properties.Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.All(properties, property => Assert.Equal(typeof(string), property.PropertyType));
        Assert.Equal(expected, Assert.Single(typeof(DellMachine).GetConstructors(), constructor => constructor.GetParameters().Length == 3).GetParameters().Select(parameter => parameter.Name));
        string[] members = typeof(DellMachine).GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Select(member => member.Name).ToArray();
        Assert.DoesNotContain(members, name => name.Contains("Serial", StringComparison.OrdinalIgnoreCase) || name.Contains("Tag", StringComparison.OrdinalIgnoreCase) || name.Contains("Asset", StringComparison.OrdinalIgnoreCase));

        // The reader passes the machine through as read.
        DellMachine other = new("Dell Inc.", "OptiPlex 9020", "A25");
        Assert.Equal(other, new Rig { Machine = other }.Read().Machine);
    }

    [Fact]
    public void Read_LogsTheAgreedRowsOnly()
    {
        // A row outside the catalog with tag-like values (synthetic) must reach the enumeration and never the log.
        const string Tag = "TESTTAG7";
        IReadOnlyDictionary<string, object?>[] rows = DcmRowFixtures.UnitA18()
            .Append(DcmRowFixtures.Row("Synthetic Attribute", DcmRowFixtures.Texts(Tag), DcmRowFixtures.Texts(Tag, "1"), DcmRowFixtures.Texts(Tag + "NAME", "Other")))
            .ToArray();
        Capture capture = new();
        Rig rig = new() { Dcm = Provider(DcmSource, DcmRead(rows, includeAll: true)) };

        DellSettingsResult result = rig.Read(new DellReadOptions(DellSourcePreference.Automatic, true), capture.Logger);

        Assert.Equal(73, result.AllAttributes!.Count);
        Assert.Contains(result.AllAttributes, attribute => attribute.CurrentNames == Tag + "NAME");

        string[] rowLines = capture.Lines.Where(line => line.Text.StartsWith("Dell setting ", StringComparison.Ordinal)).Select(line => line.Text).ToArray();
        Assert.Equal(Catalog.Count, rowLines.Length);
        Assert.All(capture.Lines.Where(line => line.Text.StartsWith("Dell setting ", StringComparison.Ordinal)), line => Assert.Equal(LogEventLevel.Debug, line.Level));
        Assert.Equal(Catalog.Select(entry => "Dell setting " + entry.Id + " (" + entry.DcmAttributeName + ") = "), rowLines.Select(line => line[..(line.IndexOf(" = ", StringComparison.Ordinal) + 3)]));
        Assert.Equal("Dell setting BootListOption (Boot Mode) = 2 (UEFI) -> UEFI [Read]", rowLines[0]);
        Assert.Equal("Dell setting NumLock (Num Lock) = 2 (Enable) -> Enabled [Read]", rowLines[9]);
        Assert.Contains((LogEventLevel.Debug, "Dell settings: 73 attribute(s) kept for the full enumeration; only the agreed ones are logged"), capture.Lines);
        Assert.Equal(
            (LogEventLevel.Information, "Dell settings: Complete from Dell Command Monitor in 0 ms; 10 of 10 read; 0 problem(s); BIOS A18"),
            Assert.Single(capture.Lines, line => line.Level == LogEventLevel.Information));
        Assert.DoesNotContain(capture.Lines, line => line.Level >= LogEventLevel.Warning);

        // No other attribute is named, and no value of the synthetic row appears, in any line or property.
        string[] others = rows.Select(row => (string)row["AttributeName"]!).Where(name => !Catalog.Any(entry => entry.DcmAttributeName == name)).ToArray();
        Assert.Equal(63, others.Length);
        Assert.All(capture.Rendered, text =>
        {
            Assert.DoesNotContain(Tag, text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(others, name => text.Contains(name, StringComparison.Ordinal));
        });

        // One warning line per problem, and the rows not read are logged as such.
        Capture failure = new();
        new Rig { Dcm = Provider(DcmSource, DcmNotInstalled()), Cctk = Provider(CctkSource, CctkNotFound()) }.Read(DellReadOptions.Default, failure.Logger);
        Assert.Equal(
            new[]
            {
                @"Dell settings problem: DellCommandMonitor NotInstalled: the namespace root\dcim\sysman is absent",
                "Dell settings problem: DellCommandConfigure ProgramNotFound: cctk.exe was not found in the Program Files folders",
            },
            failure.Lines.Where(line => line.Level == LogEventLevel.Warning).Select(line => line.Text));
        Assert.Contains((LogEventLevel.Debug, "Dell setting NumLock (Num Lock) = none (no name) -> not read [NotRead]"), failure.Lines);
        Assert.Contains((LogEventLevel.Information, "Dell settings: Unavailable from no interface in 0 ms; 0 of 10 read; 2 problem(s); BIOS A18"), failure.Lines);
    }

    [Fact]
    public void Read_KeepsNothingBetweenReads()
    {
        // The session cache is the window's: a failed read after a good one shows nothing of the first.
        Rig rig = new();
        DellProviderRead answer = DcmRead(DcmRowFixtures.UnitA18());
        rig.Dcm = Provider(DcmSource, _ => answer);
        rig.Cctk = Provider(CctkSource, CctkNotFound());
        DellSettingsReader reader = rig.Reader();

        DellSettingsResult first = reader.Read(DellReadOptions.Default);
        answer = DcmNotInstalled();
        DellSettingsResult second = reader.Read(DellReadOptions.Default);

        Assert.Equal(DellReadOutcome.Complete, first.Outcome);
        Assert.Equal(DellReadOutcome.Unavailable, second.Outcome);
        AssertEveryRowNotRead(second);
        Assert.Equal(2, rig.Dcm.Reads.Count);
    }

    private static DellProviderRead DcmRead(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, bool includeAll = false) =>
        DcmSettingsProvider.FromOutcome(DcmRowFixtures.Answer(rows), null, Catalog, includeAll);

    private static DcmQueryOutcome Absent() => new(new WmiQueryResult(NoRows, null, true), false);

    private static DellProviderRead DcmNotInstalled() => DcmSettingsProvider.FromOutcome(Absent(), false, Catalog, false);

    private static DellProviderRead DcmFailure(WmiFailureKind kind, string error) =>
        DcmSettingsProvider.FromOutcome(new DcmQueryOutcome(new WmiQueryResult(NoRows, error, false) { Failure = kind }, false), null, Catalog, false);

    private static DellProviderRead DcmTimedOut() =>
        DcmSettingsProvider.FromOutcome(new DcmQueryOutcome(new WmiQueryResult(NoRows, "Dell BIOS settings: WMI query of Dell BIOS settings did not complete within 60 seconds", false), true), null, Catalog, false);

    /// <summary>The fallback's read of the unit, each row parsed from the line its option prints on A18.</summary>
    private static DellProviderRead CctkUnitA18() => CctkRead(CctkOutputFixtures.UnitA18Output);

    /// <summary>The fallback's read with each option's output as given, made by the provider's pure parser.</summary>
    private static DellProviderRead CctkRead(Func<CctkReadOption, string> output)
    {
        FirmwareSetting[] settings = Catalog
            .Select(entry => entry.CctkOption is CctkReadOption option
                ? CctkSettingsProvider.ParseRow(entry, CctkOutputFixtures.Answer(option, output(option)), out _)
                : DellSettingMapper.NotReported(entry, PossibleValuesOrigin.Catalog, DellSentences.NotReadByFallbackNote))
            .ToArray();
        return new DellProviderRead(settings.Any(setting => setting.State == SettingState.Read), settings, Array.Empty<DellInterfaceProblem>(), null);
    }

    private static DellProviderRead CctkNotFound() =>
        NotAnswered(new DellInterfaceProblem(DellInterface.DellCommandConfigure, DellProblemKind.ProgramNotFound, "cctk.exe was not found in the Program Files folders"));

    private static DellProviderRead NotAnswered(DellInterfaceProblem problem) =>
        new(false, Catalog.Select(entry => DellSettingMapper.NotRead(entry, null)).ToArray(), new[] { problem }, null);

    private static FakeProvider Provider(DellSettingsSource source, DellProviderRead read) => new(source, _ => read);

    private static FakeProvider Provider(DellSettingsSource source, Func<DellReadBudget, DellProviderRead> read) => new(source, read);

    private static DellCatalogEntry Entry(string id) => DellAttributeCatalog.Find(id)!;

    private static FirmwareSetting Setting(DellSettingsResult result, string id) => Assert.Single(result.Settings, setting => setting.Id == id);

    private static FirmwareSetting Setting(IReadOnlyList<FirmwareSetting> settings, string id) => Assert.Single(settings, setting => setting.Id == id);

    private static void AssertOnePerEntry(DellSettingsResult result)
    {
        Assert.Equal(Catalog.Select(entry => entry.Id), result.Settings.Select(setting => setting.Id));
        Assert.All(result.Settings, setting => Assert.Equal(Entry(setting.Id).DisplayName, setting.DisplayName));
    }

    /// <summary>A result no interface answered: the ten rows in catalog order, none read, without a value or possible values.</summary>
    private static void AssertEveryRowNotRead(DellSettingsResult result)
    {
        Assert.Null(result.Source);
        AssertOnePerEntry(result);
        Assert.All(result.Settings, setting =>
        {
            Assert.Equal(SettingState.NotRead, setting.State);
            Assert.Null(setting.Current);
            Assert.Empty(setting.PossibleValues);
            Assert.Equal(DellSentences.NotReadValue, DellSentences.ValueText(setting));
        });
    }

    /// <summary>
    /// The reader composed through its seam: by default a Windows Dell OptiPlex 7020 on BIOS A18
    /// whose Dell Command Monitor answers with the unit's rows and whose Dell Command Configure
    /// would print the unit's lines, without the native namespace. The elapsed time is a running
    /// value the test and the fakes move on; each step records what was left of the budget.
    /// </summary>
    private sealed class Rig
    {
        public Rig()
        {
            Dcm = Provider(DellSettingsSource.DellCommandMonitor, (_, includeAll) => DcmRead(DcmRowFixtures.UnitA18(), includeAll));
            Cctk = Provider(DellSettingsSource.DellCommandConfigure, CctkUnitA18());
        }

        public TimeSpan Now { get; set; } = TimeSpan.FromSeconds(1000);

        public bool IsWindows { get; set; } = true;

        public DellMachine Machine { get; set; } = Unit;

        public TimeSpan MachineTakes { get; set; }

        public bool? NativePresent { get; set; } = false;

        public bool MachineFails { get; set; }

        public bool NativeFails { get; set; }

        public bool ClockFails { get; set; }

        public bool ElapsedFails { get; set; }

        public FakeProvider Dcm { get; set; }

        public FakeProvider Cctk { get; set; }

        public List<TimeSpan> MachineRemaining { get; } = new();

        public List<DellReadBudget> MachineBudgets { get; } = new();

        public List<TimeSpan> NativeRemaining { get; } = new();

        public DellSettingsReader Reader(ILogger? logger = null) => new(
            logger ?? Logger.None,
            Dcm,
            Cctk,
            budget =>
            {
                MachineRemaining.Add(budget.Remaining);
                MachineBudgets.Add(budget);
                Now += MachineTakes;
                return MachineFails ? throw new InvalidOperationException("scripted machine failure") : Machine;
            },
            budget =>
            {
                NativeRemaining.Add(budget.Remaining);
                return NativeFails ? throw new InvalidOperationException("scripted native failure") : NativePresent;
            },
            () => ClockFails ? throw new InvalidOperationException("scripted clock failure") : StartedAt,
            () => ElapsedFails ? throw new InvalidOperationException("scripted elapsed failure") : Now,
            () => IsWindows);

        public DellSettingsResult Read() => Read(DellReadOptions.Default);

        public DellSettingsResult Read(DellReadOptions options, ILogger? logger = null) => Reader(logger).Read(options);

        private static FakeProvider Provider(DellSettingsSource source, Func<DellReadBudget, bool, DellProviderRead> read) => new(source, read);

        private static FakeProvider Provider(DellSettingsSource source, DellProviderRead read) => new(source, _ => read);
    }

    /// <summary>A provider that answers what the test scripts and records every read: the catalog, the budget, what was left of it and whether the enumeration was asked for.</summary>
    private sealed class FakeProvider : IDellSettingsProvider
    {
        private readonly Func<DellReadBudget, bool, DellProviderRead> _read;

        public FakeProvider(DellSettingsSource source, Func<DellReadBudget, DellProviderRead> read)
            : this(source, (budget, _) => read(budget))
        {
        }

        public FakeProvider(DellSettingsSource source, Func<DellReadBudget, bool, DellProviderRead> read)
        {
            Source = source;
            _read = read;
        }

        public DellSettingsSource Source { get; }

        public List<(IReadOnlyList<DellCatalogEntry> Catalog, DellReadBudget Budget, TimeSpan Remaining, bool IncludeAll)> Reads { get; } = new();

        public DellProviderRead Read(IReadOnlyList<DellCatalogEntry> catalog, DellReadBudget budget, bool includeAllAttributes)
        {
            Reads.Add((catalog, budget, budget.Remaining, includeAllAttributes));
            return _read(budget, includeAllAttributes);
        }
    }

    /// <summary>Keeps every event, each rendered as the log file writes its message ({Message:lj}: strings without quotes).</summary>
    private sealed class Capture : ILogEventSink
    {
        private static readonly MessageTemplateTextFormatter Formatter = new("{Message:lj}", CultureInfo.InvariantCulture);

        private readonly List<LogEvent> _events = new();

        public Capture()
        {
            Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(this).CreateLogger();
        }

        public ILogger Logger { get; }

        public IReadOnlyList<(LogEventLevel Level, string Text)> Lines
        {
            get
            {
                lock (_events)
                {
                    return _events.Select(logEvent => (logEvent.Level, Render(logEvent))).ToArray();
                }
            }
        }

        /// <summary>Every event as one text: its message, each property and its exception.</summary>
        public IEnumerable<string> Rendered
        {
            get
            {
                lock (_events)
                {
                    return _events.Select(logEvent => Render(logEvent) + " " + string.Join(" ", logEvent.Properties.Select(pair => pair.Key + "=" + pair.Value)) + " " + logEvent.Exception).ToArray();
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

        private static string Render(LogEvent logEvent)
        {
            using StringWriter writer = new(CultureInfo.InvariantCulture);
            Formatter.Format(logEvent, writer);
            return writer.ToString();
        }
    }
}
