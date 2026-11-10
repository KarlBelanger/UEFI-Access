// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Reflection;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;
using UefiAccess.Core.Settings;
using UefiAccess.Core.Settings.Dell;
using UefiAccess.Core.Tests.Fixtures;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The Dell Command | Configure provider, the fallback: the parser of the one value line in both vocabularies, the catalog's possible
/// values, the runs in catalog order under the read budget, and what every exit code does (DF14 to
/// DF21). The provider is composed through its seam with the test's runner, so no process starts
/// and nothing here runs Dell Command | Configure on this PC, the OptiPlex 7020 itself
/// (DellSettingsGuardTests.TestsNeverRunTheRealDellReader). A timed-out run sets a latch for the
/// rest of the process's life, which the product never clears; the two tests that set it clear it
/// through reflection when they end, so every other test starts as a new process would.
/// </summary>
public sealed class CctkSettingsProviderTests
{
    private static readonly IReadOnlyList<DellCatalogEntry> Catalog = DellAttributeCatalog.Default;

    /// <summary>The nine read options, in catalog order.</summary>
    private static readonly CctkReadOption[] CatalogOptions = Catalog.Where(entry => entry.CctkOption is not null).Select(entry => entry.CctkOption!.Value).ToArray();

    [Fact]
    public void FindValue_AcceptsNewAndLegacyNames()
    {
        IReadOnlyList<string> secureBoot = Entry("SecureBoot").CctkOutputNames;

        Assert.Equal("Enabled", CctkSettingsProvider.FindValue(CctkOutputFixtures.SecureBootNew, secureBoot, out int matches));
        Assert.Equal(1, matches);
        Assert.Equal("enable", CctkSettingsProvider.FindValue(CctkOutputFixtures.SecureBootLegacy, secureBoot, out matches));
        Assert.Equal(1, matches);
        Assert.Equal("Enabled", CctkSettingsProvider.FindValue("  SECUREBOOT = Enabled  ", secureBoot, out matches));
        Assert.Equal("Enabled", CctkSettingsProvider.FindValue(CctkOutputFixtures.NumLockWithLineBreak, Entry("NumLock").CctkOutputNames, out matches));
        Assert.Equal(1, matches);
        Assert.Equal("Enabled", CctkSettingsProvider.FindValue(CctkOutputFixtures.TpmActivationEnabled, Entry("TpmActivation").CctkOutputNames, out _));
        Assert.Equal("enable", CctkSettingsProvider.FindValue(CctkOutputFixtures.WakeOnLanLegacyEnable, Entry("WakeOnLan").CctkOutputNames, out _));
        Assert.Equal("on", CctkSettingsProvider.FindValue(CctkOutputFixtures.NicOn, Entry("IntegratedNic").CctkOutputNames, out _));

        // The value is everything after the first "=", trimmed.
        Assert.Equal("a=b", CctkSettingsProvider.FindValue("NumLock = a=b ", Entry("NumLock").CctkOutputNames, out _));

        // Every entry's line is found under each of its two names, in any casing.
        foreach (DellCatalogEntry entry in Catalog.Where(entry => entry.CctkOption is not null))
        {
            Assert.Equal(2, entry.CctkOutputNames.Count);
            foreach (string name in entry.CctkOutputNames.SelectMany(name => new[] { name, name.ToUpperInvariant(), name.ToLowerInvariant() }))
            {
                Assert.Equal("x", CctkSettingsProvider.FindValue(name + "=x", entry.CctkOutputNames, out matches));
                Assert.Equal(1, matches);
            }
        }
    }

    [Fact]
    public void FindValue_IgnoresOtherLinesAndBanners()
    {
        IReadOnlyList<string> numLock = Entry("NumLock").CctkOutputNames;

        Assert.Equal("Enabled", CctkSettingsProvider.FindValue(CctkOutputFixtures.BannerThenNumLock, numLock, out int matches));
        Assert.Equal(1, matches);

        // An empty output, lines that only look alike, and another option's line name nothing.
        Assert.Null(CctkSettingsProvider.FindValue(CctkOutputFixtures.Empty, numLock, out matches));
        Assert.Equal(0, matches);
        Assert.Null(CctkSettingsProvider.FindValue(null!, numLock, out matches));
        Assert.Equal(0, matches);
        Assert.Null(CctkSettingsProvider.FindValue("NumLockLed=On\r\n=Enabled\r\nNumLock Enabled\r\nNum Lock=Enabled\r\nNumLock: Enabled", numLock, out matches));
        Assert.Equal(0, matches);
        Assert.Null(CctkSettingsProvider.FindValue(CctkOutputFixtures.SecureBootNew, numLock, out matches));
        Assert.Equal(0, matches);
        Assert.Null(CctkSettingsProvider.FindValue("NumLock=Enabled", Array.Empty<string>(), out matches));
        Assert.Equal(0, matches);

        // Through the row: no line that names the setting is an answer this version does not recognise.
        FirmwareSetting row = CctkSettingsProvider.ParseRow(Entry("NumLock"), CctkOutputFixtures.Answer(CctkReadOption.NumLock, CctkOutputFixtures.Empty), out DellInterfaceProblem? problem);
        Assert.Equal(SettingState.NotReported, row.State);
        AssertProblem(problem, DellProblemKind.UnrecognisedOutput, CctkReader.NumLockArgument + ": no line names NumLock or numlock");
    }

    [Fact]
    public void FindValue_TwoMatchingLines_IsNoValue()
    {
        IReadOnlyList<string> numLock = Entry("NumLock").CctkOutputNames;

        Assert.Null(CctkSettingsProvider.FindValue(CctkOutputFixtures.TwoNumLockLines, numLock, out int matches));
        Assert.Equal(2, matches);

        // Even two lines that agree are no value.
        Assert.Null(CctkSettingsProvider.FindValue("NumLock=Enabled\nNumLock=Enabled", numLock, out matches));
        Assert.Equal(2, matches);

        FirmwareSetting row = CctkSettingsProvider.ParseRow(Entry("NumLock"), CctkOutputFixtures.Answer(CctkReadOption.NumLock, CctkOutputFixtures.TwoNumLockLines), out DellInterfaceProblem? problem);
        Assert.Equal(SettingState.NotReported, row.State);
        Assert.Null(row.Current);
        Assert.Empty(row.PossibleValues);
        Assert.Equal("Num Lock at boot: not reported.", DellSentences.RowSentence(row));
        AssertProblem(problem, DellProblemKind.UnrecognisedOutput, CctkReader.NumLockArgument + ": 2 lines name NumLock or numlock");
        Assert.Equal("Dell Command Configure answered in a form this version does not recognise.", DellSentences.Cause(problem!));

        // One line without a value is no value either.
        Assert.Equal(string.Empty, CctkSettingsProvider.FindValue("NumLock=", numLock, out matches));
        Assert.Equal(1, matches);
        row = CctkSettingsProvider.ParseRow(Entry("NumLock"), CctkOutputFixtures.Answer(CctkReadOption.NumLock, "NumLock= "), out problem);
        Assert.Equal(SettingState.NotReported, row.State);
        AssertProblem(problem, DellProblemKind.UnrecognisedOutput, CctkReader.NumLockArgument + ": the line that names NumLock or numlock holds no value");
    }

    [Fact]
    public void ParseRow_MapsTokensIgnoringCase()
    {
        int checkedTokens = 0;
        foreach (DellCatalogEntry entry in Catalog.Where(entry => entry.CctkOption is not null))
        {
            CctkReadOption option = entry.CctkOption!.Value;
            foreach (DellValueText value in entry.Values)
            {
                Assert.NotEmpty(value.CctkTokens);
                foreach (string printed in value.CctkTokens.SelectMany(token => new[] { token, token.ToUpperInvariant(), token.ToLowerInvariant() }))
                {
                    FirmwareSetting row = CctkSettingsProvider.ParseRow(entry, CctkOutputFixtures.Answer(option, entry.CctkOutputNames[0] + "=" + printed), out DellInterfaceProblem? problem);

                    Assert.Null(problem);
                    Assert.Equal(SettingState.Read, row.State);
                    Assert.Equal(new SettingValue(printed, null, value.Text), row.Current);
                    Assert.False(row.DisplayFromVendor);
                    Assert.Equal(value.Text, DellSentences.ValueText(row));
                    checkedTokens++;
                }
            }
        }

        Assert.True(checkedTokens >= 3 * 2 * 2 * CatalogOptions.Length, "every word of every value of the nine options was checked in three casings");

        // The name in any casing too: 5.2.2's AHCI word, written in capitals under its alias.
        FirmwareSetting sata = CctkSettingsProvider.ParseRow(Entry("SataOperation"), CctkOutputFixtures.Answer(CctkReadOption.SataOperation, "EMBSATARAID=AHCI"), out _);
        Assert.Equal(new SettingValue("AHCI", null, "AHCI"), sata.Current);
        Assert.Equal("SataOperation", sata.Id);
        Assert.Equal("Embedded SATA Controller", sata.Name);
    }

    [Fact]
    public void ParseRow_NicOnMeansEnabledWithPxe()
    {
        DellCatalogEntry nic = Entry("IntegratedNic");

        FirmwareSetting on = CctkSettingsProvider.ParseRow(nic, CctkOutputFixtures.Answer(CctkReadOption.IntegratedNic, CctkOutputFixtures.NicOn), out DellInterfaceProblem? problem);

        Assert.Null(problem);
        Assert.Equal(new SettingValue("on", null, "Enabled with PXE"), on.Current);
        Assert.Equal("Integrated NIC: Enabled with PXE. Possible values: Disabled, Enabled with PXE, Enabled, Enabled with image server.", DellSentences.RowSentence(on));

        // "on" is not plain Enabled: that one is onnopxe in the legacy words.
        Assert.Equal("Enabled", NicText("embnic1=onnopxe"));
        Assert.Equal("Enabled", NicText("EmbNic1=Enabled"));
        Assert.Equal("Enabled with PXE", NicText("EmbNic1=EnabledPxe"));
        Assert.Equal("Disabled", NicText("embnic1=off"));
        Assert.Equal("Disabled", NicText("EmbNic1=Disabled"));
        Assert.Equal("Enabled with image server", NicText("EmbNic1=OnWithImageServerBoot"));

        string NicText(string output) => DellSentences.ValueText(CctkSettingsProvider.ParseRow(nic, CctkOutputFixtures.Answer(CctkReadOption.IntegratedNic, output), out _));
    }

    [Fact]
    public void ParseRow_TpmActivationEnabledIsActivated()
    {
        DellCatalogEntry activation = Entry("TpmActivation");

        FirmwareSetting row = CctkSettingsProvider.ParseRow(activation, CctkOutputFixtures.Answer(CctkReadOption.TpmActivation, CctkOutputFixtures.TpmActivationEnabled), out DellInterfaceProblem? problem);

        // 5.2.2 prints Enabled for an activated TPM; the row says the setup screen's state, never a command.
        Assert.Null(problem);
        Assert.Equal(new SettingValue("Enabled", null, "Activated"), row.Current);
        Assert.Equal("TPM activation: Activated. Possible values: Deactivated, Activated.", DellSentences.RowSentence(row));
        Assert.Equal("Activated", Text("tpmactivation=activate"));
        Assert.Equal("Activated", Text("TpmActivation=Activated"));
        Assert.Equal("Deactivated", Text("TpmActivation=Disabled"));
        Assert.Equal("Deactivated", Text("tpmactivation=deactivated"));
        Assert.Equal("Deactivated", Text("TpmActivation=Deactivate"));

        string Text(string output) => DellSentences.ValueText(CctkSettingsProvider.ParseRow(activation, CctkOutputFixtures.Answer(CctkReadOption.TpmActivation, output), out _));
    }

    [Fact]
    public void ParseRow_WakeOnLanLegacyEnableIsLanOnly()
    {
        DellCatalogEntry wakeOnLan = Entry("WakeOnLan");

        FirmwareSetting row = CctkSettingsProvider.ParseRow(wakeOnLan, CctkOutputFixtures.Answer(CctkReadOption.WakeOnLan, CctkOutputFixtures.WakeOnLanLegacyEnable), out DellInterfaceProblem? problem);

        Assert.Null(problem);
        Assert.Equal(new SettingValue("enable", null, "LAN Only"), row.Current);
        Assert.Equal("Wake on LAN: LAN Only. Possible values: Disabled, LAN Only, LAN with PXE Boot.", DellSentences.RowSentence(row));
        Assert.Equal("LAN Only", Text("WakeOnLan=LanOnly"));
        Assert.Equal("LAN with PXE Boot", Text("WakeOnLan=LanWithPxeBoot"));
        Assert.Equal("Disabled", Text("wakeonlan=disable"));
        Assert.Equal("Disabled", Text("WakeOnLan=Disabled"));

        string Text(string output) => DellSentences.ValueText(CctkSettingsProvider.ParseRow(wakeOnLan, CctkOutputFixtures.Answer(CctkReadOption.WakeOnLan, output), out _));
    }

    [Fact]
    public void ParseRow_UnknownToken_ShowsTheTokenAndIsMarked()
    {
        FirmwareSetting row = CctkSettingsProvider.ParseRow(Entry("NumLock"), CctkOutputFixtures.Answer(CctkReadOption.NumLock, "NumLock=Blinking"), out DellInterfaceProblem? problem);

        Assert.Null(problem);
        Assert.Equal(SettingState.Read, row.State);
        Assert.True(row.DisplayFromVendor);
        Assert.Equal(new SettingValue("Blinking", null, "Blinking"), row.Current);
        Assert.Equal("Num Lock at boot: Blinking. Possible values: Disabled, Enabled.", DellSentences.RowSentence(row));

        // Through a read: the row is read and marked, the others are not, and one Warning names it.
        Capture capture = new();
        DellProviderRead read = new Fake(option => option == CctkReadOption.NumLock ? CctkOutputFixtures.Answer(option, "NumLock=Blinking") : null, logger: capture.Logger).Provider.Read(Catalog, Budget(), false);
        Assert.True(read.Answered);
        Assert.Empty(read.Problems);
        Assert.True(Setting(read, "NumLock").DisplayFromVendor);
        Assert.All(read.Settings.Where(setting => setting.Id != "NumLock"), setting => Assert.False(setting.DisplayFromVendor));
        string warning = Assert.Single(capture.Lines, line => line.Level == LogEventLevel.Warning).Text;
        Assert.Equal("Dell setting NumLock (Num Lock): Dell Command Configure's word Blinking is not in the catalog, so it is shown as printed", warning);
    }

    [Fact]
    public void PossibleValues_ComeFromTheCatalog()
    {
        DellProviderRead read = new Fake().Provider.Read(Catalog, Budget(), false);

        foreach (FirmwareSetting setting in read.Settings.Where(setting => setting.State == SettingState.Read))
        {
            DellCatalogEntry entry = Entry(setting.Id);
            Assert.Equal(PossibleValuesOrigin.Catalog, setting.PossibleValuesOrigin);
            Assert.Equal(entry.Values.Select(value => new SettingValue(value.CctkTokens[0], null, value.Text)), setting.PossibleValues);
            Assert.Null(setting.VendorMarksReadOnly);
            Assert.Null(setting.Note);
            Assert.Null(setting.Current!.VendorName);
        }

        // Unlike Dell Command | Monitor, which lists only Enabled, the catalog offers both Secure Boot values, and no note says otherwise.
        Assert.Equal("Secure Boot: Enabled. Possible values: Disabled, Enabled.", DellSentences.RowSentence(Setting(read, "SecureBoot")));
        Assert.Equal("SATA operation: AHCI. Possible values: Disabled, ATA, AHCI.", DellSentences.RowSentence(Setting(read, "SataOperation")));
        Assert.Equal(9, read.Settings.Count(setting => setting.State == SettingState.Read));
    }

    [Fact]
    public void BootListOption_IsNeverRunAndSaysSo()
    {
        Fake fake = new();

        DellProviderRead read = fake.Provider.Read(Catalog, Budget(), false);

        // Its only read form shares the boot-order option, which no member of the enumeration names.
        DellCatalogEntry bootList = Entry("BootListOption");
        Assert.Null(bootList.CctkOption);
        Assert.Empty(bootList.CctkOutputNames);
        Assert.Equal(9, Enum.GetValues<CctkReadOption>().Length);
        Assert.Equal(Enum.GetValues<CctkReadOption>().Order(), fake.RunOptions.Order());

        FirmwareSetting row = Setting(read, "BootListOption");
        Assert.Equal(SettingState.NotReported, row.State);
        Assert.Null(row.Current);
        Assert.Empty(row.PossibleValues);
        Assert.Equal(PossibleValuesOrigin.Catalog, row.PossibleValuesOrigin);
        Assert.Equal(DellSentences.NotReadByFallbackNote, row.Note);
        Assert.Equal("Boot list option: not reported. This version reads it through Dell Command Monitor only.", DellSentences.RowSentence(row));

        // That row is expected, so it is no problem, and the read is complete otherwise.
        Assert.Empty(read.Problems);
        Assert.True(read.Answered);
    }

    [Fact]
    public void Exit95_StopsWithNeedsAdministrator()
    {
        Fake fake = new(option => option == CctkReadOption.SecureBoot ? CctkOutputFixtures.Exit(option, 95) : null);

        DellProviderRead read = fake.Provider.Read(Catalog, Budget(), false);

        Assert.Equal(new[] { CctkReadOption.SecureBoot }, fake.RunOptions);
        Assert.False(read.Answered);
        DellInterfaceProblem problem = Assert.Single(read.Problems);
        AssertProblem(problem, DellProblemKind.NeedsAdministrator, CctkReader.SecureBootArgument + ": exit code 95");
        Assert.Equal("Dell Command Configure needs administrator rights.", DellSentences.Cause(problem));
        AssertStoppedAt(read, 2);

        // Later in the list: the rows read before stay read, and nothing more runs.
        Fake later = new(option => option == CctkReadOption.TpmSecurity ? CctkOutputFixtures.Exit(option, 95) : null);
        DellProviderRead partly = later.Provider.Read(Catalog, Budget(), false);
        Assert.Equal(new[] { CctkReadOption.SecureBoot, CctkReadOption.SataOperation, CctkReadOption.TpmSecurity }, later.RunOptions);
        Assert.True(partly.Answered);
        Assert.Equal(DellProblemKind.NeedsAdministrator, Assert.Single(partly.Problems).Kind);
        AssertStoppedAt(partly, 4);
    }

    [Fact]
    public void Exit12And140_StopWithNoBiosInterface()
    {
        foreach (int code in new[] { 12, 140 })
        {
            Fake fake = new(option => option == CctkReadOption.SataOperation ? CctkOutputFixtures.Exit(option, code) : null);

            DellProviderRead read = fake.Provider.Read(Catalog, Budget(), false);

            Assert.Equal(new[] { CctkReadOption.SecureBoot, CctkReadOption.SataOperation }, fake.RunOptions);
            DellInterfaceProblem problem = Assert.Single(read.Problems);
            AssertProblem(problem, DellProblemKind.NoBiosInterface, CctkReader.SataOperationArgument + ": exit code " + code.ToString(CultureInfo.InvariantCulture));
            Assert.Equal("Dell Command Configure found no Dell BIOS interface on this computer.", DellSentences.Cause(problem));
            AssertStoppedAt(read, 3);
        }
    }

    [Fact]
    public void Exit118And42And96_MakeOnlyThatRowNotReported()
    {
        Dictionary<CctkReadOption, int> refused = new()
        {
            [CctkReadOption.SataOperation] = 118,
            [CctkReadOption.Virtualization] = 42,
            [CctkReadOption.WakeOnLan] = 96,
        };
        Fake fake = new(option => refused.TryGetValue(option, out int code) ? CctkOutputFixtures.Exit(option, code) : null);

        DellProviderRead read = fake.Provider.Read(Catalog, Budget(), false);

        // Every option still runs; only the three refused rows are not reported.
        Assert.Equal(CatalogOptions, fake.RunOptions);
        Assert.True(read.Answered);
        Assert.Equal(
            new[] { "BootListOption", "SataOperation", "Virtualization", "WakeOnLan" },
            read.Settings.Where(setting => setting.State == SettingState.NotReported).Select(setting => setting.Id));
        Assert.Equal(6, read.Settings.Count(setting => setting.State == SettingState.Read));
        Assert.All(read.Problems, problem => Assert.Equal(DellProblemKind.OptionNotSupported, problem.Kind));
        Assert.Equal(
            new[] { CctkReader.SataOperationArgument + ": exit code 118", CctkReader.VirtualizationArgument + ": exit code 42", CctkReader.WakeOnLanArgument + ": exit code 96" },
            read.Problems.Select(problem => problem.Detail));
        Assert.Equal("Dell Command Configure does not offer these settings on this computer.", DellSentences.Cause(read.Problems[0]));
        foreach (CctkReadOption option in refused.Keys)
        {
            FirmwareSetting setting = Setting(read, Entry(option).Id);
            Assert.Null(setting.Current);
            Assert.Null(setting.Note);
            Assert.Empty(setting.PossibleValues);
        }
    }

    [Fact]
    public void Timeout_StopsAndLeavesTheRestNotRead()
    {
        Assert.False(CctkSettingsProvider.EarlierRunTimedOut);
        try
        {
            Fake fake = new(option => option == CctkReadOption.TpmSecurity ? CctkOutputFixtures.TimedOut(option) : null);

            DellProviderRead read = fake.Provider.Read(Catalog, Budget(), false);

            Assert.Equal(new[] { CctkReadOption.SecureBoot, CctkReadOption.SataOperation, CctkReadOption.TpmSecurity }, fake.RunOptions);
            Assert.True(read.Answered);
            DellInterfaceProblem problem = Assert.Single(read.Problems);
            AssertProblem(problem, DellProblemKind.TimedOut, CctkReader.TpmSecurityArgument + ": did not finish within 15 seconds");
            Assert.Equal("Dell Command Configure did not answer within 15 seconds.", DellSentences.Cause(problem));
            AssertStoppedAt(read, 4);
            Assert.True(CctkSettingsProvider.EarlierRunTimedOut);
        }
        finally
        {
            ForgetTheTimedOutRun();
        }
    }

    [Fact]
    public void AfterATimeout_NoLaterReadStartsCctk()
    {
        Assert.False(CctkSettingsProvider.EarlierRunTimedOut);
        try
        {
            Fake first = new(option => CctkOutputFixtures.TimedOut(option));
            DellProviderRead timedOut = first.Provider.Read(Catalog, Budget(), false);
            Assert.Equal(new[] { CctkReadOption.SecureBoot }, first.RunOptions);
            Assert.False(timedOut.Answered);

            // Neither the same provider nor a new one starts another run in this process.
            Fake second = new();
            foreach (Fake fake in new[] { first, second })
            {
                DellProviderRead read = fake.Provider.Read(Catalog, Budget(), false);
                Assert.False(read.Answered);
                AssertProblem(Assert.Single(read.Problems), DellProblemKind.TimedOut, "an earlier Dell Command Configure run timed out; restart the application");
                Assert.Equal(Catalog.Select(entry => entry.Id), read.Settings.Select(setting => setting.Id));
                Assert.All(read.Settings, setting => Assert.Equal(SettingState.NotRead, setting.State));
            }

            Assert.Single(first.RunOptions);
            Assert.Empty(second.RunOptions);
        }
        finally
        {
            ForgetTheTimedOutRun();
        }

        // In a new process (the latch cleared), the same runner reads again.
        Fake fresh = new();
        Assert.True(fresh.Provider.Read(Catalog, Budget(), false).Answered);
        Assert.Equal(CatalogOptions, fresh.RunOptions);
    }

    [Theory]
    [InlineData(43, true)]
    [InlineData(62, true)]
    [InlineData(87, true)]
    [InlineData(94, true)]
    [InlineData(109, true)]
    [InlineData(119, true)]
    [InlineData(128, true)]
    [InlineData(0, false)]
    public void OtherExitCode_StopsAndLeavesTheRestNotRead(int exitCode, bool started)
    {
        Capture capture = new();
        Fake fake = new(
            option => option != CctkReadOption.SataOperation ? null : started ? CctkOutputFixtures.Exit(option, exitCode, "EmbSataRaid=Ahci") : CctkOutputFixtures.NotStarted(option),
            logger: capture.Logger);

        DellProviderRead read = fake.Provider.Read(Catalog, Budget(), false);

        // Even with a value line printed, a code that may mean a change was attempted ends the read.
        Assert.Equal(new[] { CctkReadOption.SecureBoot, CctkReadOption.SataOperation }, fake.RunOptions);
        Assert.True(read.Answered);
        string detail = CctkReader.SataOperationArgument + (started ? ": exit code " + exitCode.ToString(CultureInfo.InvariantCulture) : ": not started: process did not start");
        DellInterfaceProblem problem = Assert.Single(read.Problems);
        AssertProblem(problem, DellProblemKind.RunFailed, detail);
        Assert.Equal("Dell Command Configure stopped with an error; the log has the details.", DellSentences.Cause(problem));
        AssertStoppedAt(read, 3);

        string warning = Assert.Single(capture.Lines, line => line.Level == LogEventLevel.Warning).Text;
        Assert.StartsWith("Dell Command Configure run failed: " + detail + ";", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void CctkNotFound_IsProgramNotFound()
    {
        Fake fake = new(found: false);

        DellProviderRead read = fake.Provider.Read(Catalog, Budget(), true);

        Assert.Empty(fake.RunOptions);
        Assert.False(read.Answered);
        Assert.Null(read.AllAttributes);
        DellInterfaceProblem problem = Assert.Single(read.Problems);
        AssertProblem(problem, DellProblemKind.ProgramNotFound, "cctk.exe was not found in the Program Files folders");
        Assert.Equal("Dell Command Configure was not found.", DellSentences.Cause(problem));
        Assert.Equal(Catalog.Select(entry => entry.Id), read.Settings.Select(setting => setting.Id));
        Assert.All(read.Settings, setting =>
        {
            Assert.Equal(SettingState.NotRead, setting.State);
            Assert.Null(setting.Current);
            Assert.Empty(setting.PossibleValues);
        });
    }

    [Fact]
    public void Budget_StopsBeforeTheNextRun()
    {
        // Each run takes 25 seconds of the 120: five runs start (the fifth with 20 seconds left), the sixth would have 0.
        TimeSpan elapsed = TimeSpan.Zero;
        DellReadBudget budget = new(TimeSpan.FromSeconds(120), () => elapsed);
        List<TimeSpan> remainingAtRun = new();
        Fake fake = new(option =>
        {
            remainingAtRun.Add(budget.Remaining);
            elapsed += TimeSpan.FromSeconds(25);
            return null;
        });

        DellProviderRead read = fake.Provider.Read(Catalog, budget, false);

        Assert.Equal(CatalogOptions.Take(5), fake.RunOptions);
        Assert.Equal(new[] { 120, 95, 70, 45, 20 }.Select(seconds => TimeSpan.FromSeconds(seconds)), remainingAtRun);
        Assert.True(read.Answered);
        DellInterfaceProblem problem = Assert.Single(read.Problems);

        // The budget is the whole read's, so its problem is the reader's (DF21).
        Assert.Equal(new DellInterfaceProblem(DellInterface.Reader, DellProblemKind.BudgetExhausted, "less than one run timeout was left of the read budget"), problem);
        Assert.Equal("Reading stopped at the time limit of 120 seconds.", DellSentences.Cause(problem));
        AssertNotRunFrom(read, 7);
        Assert.Equal(SettingState.Read, Setting(read, "Virtualization").State);
        Assert.Equal(SettingState.NotRead, Setting(read, "VtForDirectIo").State);
    }

    [Fact]
    public void Budget_BelowOneRunTimeout_StartsNoRun()
    {
        foreach (TimeSpan left in new[] { TimeSpan.FromSeconds(14), TimeSpan.FromSeconds(15) - TimeSpan.FromTicks(1), TimeSpan.Zero })
        {
            Fake fake = new();

            DellProviderRead read = fake.Provider.Read(Catalog, Budget(TimeSpan.FromSeconds(120) - left), false);

            // A run is never started only to be killed.
            Assert.Empty(fake.RunOptions);
            Assert.False(read.Answered);
            DellInterfaceProblem problem = Assert.Single(read.Problems);
            Assert.Equal((DellInterface.Reader, DellProblemKind.BudgetExhausted), (problem.Interface, problem.Kind));
            Assert.Equal(SettingState.NotReported, Setting(read, "BootListOption").State);
            Assert.Equal(DellSentences.NotReadByFallbackNote, Setting(read, "BootListOption").Note);
            Assert.All(read.Settings.Skip(1), setting => Assert.Equal(SettingState.NotRead, setting.State));
        }

        // Exactly one run timeout left: every run may start.
        Fake enough = new();
        Assert.True(enough.Provider.Read(Catalog, Budget(TimeSpan.FromSeconds(105)), false).Answered);
        Assert.Equal(CatalogOptions, enough.RunOptions);
    }

    [Fact]
    public void RunsAreAlwaysAskedForTheRunTimeout()
    {
        Assert.Equal(TimeSpan.FromSeconds(15), CctkSettingsProvider.RunTimeout);

        // With the whole budget left and with 20 seconds left: always the run timeout, never the budget's remainder.
        foreach (TimeSpan elapsed in new[] { TimeSpan.Zero, TimeSpan.FromSeconds(100) })
        {
            Fake fake = new();

            fake.Provider.Read(Catalog, Budget(elapsed), false);

            Assert.Equal(9, fake.Runs.Count);
            Assert.All(fake.Runs, run => Assert.Equal(CctkSettingsProvider.RunTimeout, run.Bound));
        }
    }

    [Fact]
    public void EveryOtherLine_IsLoggedCutAt200_AtMostTenPerRun()
    {
        string longLine = new('x', 250);
        string output = string.Join("\r\n", "Synthetic banner", string.Empty, longLine, "Mode=read", "NumLock=Enabled", "after 1", "after 2", "after 3", "after 4", "after 5");
        string error = string.Join("\n", "error 1", "error 2", "bell\aring", "error 4");
        Capture capture = new();
        Fake fake = new(
            option => option switch
            {
                CctkReadOption.NumLock => CctkOutputFixtures.Exit(option, 0, output, error),
                CctkReadOption.WakeOnLan => CctkOutputFixtures.Answer(option, "Something else"),
                _ => null,
            },
            logger: capture.Logger);

        DellProviderRead read = fake.Provider.Read(Catalog, Budget(), false);

        Assert.Equal("Enabled", DellSentences.ValueText(Setting(read, "NumLock")));
        string[] debug = capture.Lines.Where(line => line.Level == LogEventLevel.Debug).Select(line => line.Text).ToArray();
        string[] numLock = debug.Where(line => line.StartsWith("cctk " + CctkReader.NumLockArgument, StringComparison.Ordinal)).ToArray();
        Assert.Equal(
            new[]
            {
                "cctk --numlock: exit 0 in 12 ms; matched NumLock=Enabled; 8 other line(s); 4 standard error line(s)",
                "cctk --numlock standard output line 1: Synthetic banner",
                "cctk --numlock standard output line 3: " + new string('x', 200),
                "cctk --numlock standard output line 4: Mode=read",
                "cctk --numlock standard output line 6: after 1",
                "cctk --numlock standard output line 7: after 2",
                "cctk --numlock standard output line 8: after 3",
                "cctk --numlock standard output line 9: after 4",
                "cctk --numlock standard output line 10: after 5",
                "cctk --numlock standard error line 1: error 1",
                "cctk --numlock standard error line 2: error 2",
                "cctk --numlock: 2 more line(s) not logged",
            },
            numLock);

        // A run that printed only its value line logs one line; one whose line names nothing says so.
        Assert.Equal(new[] { "cctk --secureboot: exit 0 in 12 ms; matched SecureBoot=Enabled; 0 other line(s); 0 standard error line(s)" }, debug.Where(line => line.StartsWith("cctk --secureboot", StringComparison.Ordinal)));
        Assert.Equal(
            new[] { "cctk --wakeonlan: exit 0 in 12 ms; matched no line; 1 other line(s); 0 standard error line(s)", "cctk --wakeonlan standard output line 1: Something else" },
            debug.Where(line => line.StartsWith("cctk --wakeonlan", StringComparison.Ordinal)));

        // A control character is never written to the log as it came.
        Fake bell = new(option => option == CctkReadOption.SecureBoot ? CctkOutputFixtures.Exit(option, 0, CctkOutputFixtures.SecureBootNew, "bell\aring") : null, logger: capture.Logger);
        bell.Provider.Read(Catalog, Budget(), false);
        Assert.Contains("cctk --secureboot standard error line 1: bell ring", capture.Lines.Select(line => line.Text));
        Assert.DoesNotContain(capture.Lines, line => line.Text.Contains('\a', StringComparison.Ordinal));
    }

    [Fact]
    public void RunsInCatalogOrder_OneAtATime()
    {
        Fake fake = new();

        DellProviderRead read = fake.Provider.Read(Catalog, Budget(), false);

        Assert.Equal(
            new[] { CctkReadOption.SecureBoot, CctkReadOption.SataOperation, CctkReadOption.TpmSecurity, CctkReadOption.TpmActivation, CctkReadOption.Virtualization, CctkReadOption.VtForDirectIo, CctkReadOption.IntegratedNic, CctkReadOption.WakeOnLan, CctkReadOption.NumLock },
            fake.RunOptions);
        Assert.Equal(CatalogOptions, fake.RunOptions);
        Assert.Equal(1, fake.MostInFlight);
        Assert.Equal(fake.RunOptions.SelectMany(option => new[] { "start " + option, "end " + option }), fake.Events);
        Assert.Equal(Catalog.Select(entry => entry.Id), read.Settings.Select(setting => setting.Id));
    }

    [Fact]
    public void Answered_OnlyWhenARowWasRead()
    {
        // Every option refused: nothing read, nine problems.
        DellProviderRead refused = new Fake(option => CctkOutputFixtures.Exit(option, 118)).Provider.Read(Catalog, Budget(), false);
        Assert.False(refused.Answered);
        Assert.Equal(9, refused.Problems.Count);
        Assert.All(refused.Problems, problem => Assert.Equal(DellProblemKind.OptionNotSupported, problem.Kind));

        // Every answer unrecognised: nothing read.
        DellProviderRead unrecognised = new Fake(option => CctkOutputFixtures.Answer(option, CctkOutputFixtures.Empty)).Provider.Read(Catalog, Budget(), false);
        Assert.False(unrecognised.Answered);
        Assert.All(unrecognised.Problems, problem => Assert.Equal(DellProblemKind.UnrecognisedOutput, problem.Kind));
        Assert.DoesNotContain(unrecognised.Settings, setting => setting.State == SettingState.Read);

        // One row read is an answer, and a word the catalog does not know is still a read value.
        DellProviderRead one = new Fake(option => option == CctkReadOption.NumLock ? null : CctkOutputFixtures.Exit(option, 96)).Provider.Read(Catalog, Budget(), false);
        Assert.True(one.Answered);
        Assert.Equal("NumLock", Assert.Single(one.Settings, setting => setting.State == SettingState.Read).Id);
        DellProviderRead vendorWords = new Fake(option => CctkOutputFixtures.Answer(option, Entry(option).CctkOutputNames[0] + "=Unheard")).Provider.Read(Catalog, Budget(), false);
        Assert.True(vendorWords.Answered);
        Assert.Equal(9, vendorWords.Settings.Count(setting => setting.State == SettingState.Read && setting.DisplayFromVendor));
    }

    [Fact]
    public void Read_UnitA18_GivesTheNineExpectedTexts()
    {
        Fake fake = new();

        DellProviderRead read = fake.Provider.Read(Catalog, Budget(), true);

        Assert.Equal(DellSettingsSource.DellCommandConfigure, fake.Provider.Source);
        Assert.True(read.Answered);
        Assert.Empty(read.Problems);
        Assert.Null(read.AllAttributes);
        Assert.Equal(Catalog.Select(entry => entry.Id), read.Settings.Select(setting => setting.Id));
        Assert.Equal(new[] { DellSentences.NotReportedValue }.Concat(DcmRowFixtures.UnitA18Texts.Skip(1)), read.Settings.Select(DellSentences.ValueText));
        Assert.All(read.Settings, setting =>
        {
            DellCatalogEntry entry = Entry(setting.Id);
            Assert.Equal((entry.DcmAttributeName, entry.DisplayName, entry.Category, entry.SetupLocation), (setting.Name, setting.DisplayName, setting.Category, setting.SetupLocation));
            Assert.False(setting.DisplayFromVendor);
        });
        Assert.Equal("Num Lock at boot: Enabled. Possible values: Disabled, Enabled.", DellSentences.RowSentence(read.Settings[9]));
    }

    [Fact]
    public void StopKind_ClassifiesEveryExitCode()
    {
        foreach (int code in new[] { 0, 42, 96, 118 })
        {
            Assert.Null(CctkSettingsProvider.StopKind(CctkOutputFixtures.Exit(CctkReadOption.NumLock, code)));
        }

        Assert.Equal(DellProblemKind.NeedsAdministrator, CctkSettingsProvider.StopKind(CctkOutputFixtures.Exit(CctkReadOption.NumLock, 95)));
        Assert.Equal(DellProblemKind.NoBiosInterface, CctkSettingsProvider.StopKind(CctkOutputFixtures.Exit(CctkReadOption.NumLock, 12)));
        Assert.Equal(DellProblemKind.NoBiosInterface, CctkSettingsProvider.StopKind(CctkOutputFixtures.Exit(CctkReadOption.NumLock, 140)));
        Assert.Equal(DellProblemKind.TimedOut, CctkSettingsProvider.StopKind(CctkOutputFixtures.TimedOut(CctkReadOption.NumLock)));
        Assert.Equal(DellProblemKind.TimedOut, CctkSettingsProvider.StopKind(CctkOutputFixtures.TimedOut(CctkReadOption.NumLock) with { ExitCode = 0 }));
        Assert.Equal(DellProblemKind.RunFailed, CctkSettingsProvider.StopKind(CctkOutputFixtures.NotStarted(CctkReadOption.NumLock)));
        Assert.Equal(DellProblemKind.RunFailed, CctkSettingsProvider.StopKind(CctkOutputFixtures.Exit(CctkReadOption.NumLock, 0) with { ExitCode = null }));
        foreach (int code in new[] { 1, -1, 16, 43, 57, 62, 87, 94, 109, 119, 120, 121, 128, 255 })
        {
            Assert.Equal(DellProblemKind.RunFailed, CctkSettingsProvider.StopKind(CctkOutputFixtures.Exit(CctkReadOption.NumLock, code)));
        }

        // A stopping run is never parsed, even when it printed a value line.
        FirmwareSetting row = CctkSettingsProvider.ParseRow(Entry("NumLock"), CctkOutputFixtures.Exit(CctkReadOption.NumLock, -1, "NumLock=Enabled"), out DellInterfaceProblem? problem);
        Assert.Equal(SettingState.NotReported, row.State);
        AssertProblem(problem, DellProblemKind.RunFailed, CctkReader.NumLockArgument + ": exit code -1");
    }

    [Fact]
    public void Read_NeverThrows()
    {
        CctkSettingsProvider throwingRun = new(Logger.None, () => true, (_, _) => throw new InvalidOperationException("scripted failure"));
        CctkSettingsProvider throwingFound = new(Logger.None, () => throw new InvalidOperationException("scripted failure"), (option, _) => CctkOutputFixtures.Answer(option, CctkOutputFixtures.UnitA18Output(option)));

        foreach (CctkSettingsProvider provider in new[] { throwingRun, throwingFound })
        {
            DellProviderRead read = provider.Read(Catalog, Budget(), false);
            Assert.False(read.Answered);
            AssertProblem(Assert.Single(read.Problems), DellProblemKind.Unexpected, "InvalidOperationException: scripted failure");
            Assert.Equal(Catalog.Select(entry => entry.Id), read.Settings.Select(setting => setting.Id));
            Assert.DoesNotContain(read.Settings, setting => setting.State == SettingState.Read);
        }

        Assert.Equal(DellProblemKind.Unexpected, Assert.Single(new Fake().Provider.Read(Catalog, null!, false).Problems).Kind);

        DellProviderRead withoutCatalog = new Fake().Provider.Read(null!, Budget(), false);
        Assert.False(withoutCatalog.Answered);
        Assert.Empty(withoutCatalog.Settings);
        Assert.Empty(withoutCatalog.Problems);
        Assert.False(CctkSettingsProvider.EarlierRunTimedOut);
    }

    private static DellReadBudget Budget() => Budget(TimeSpan.Zero);

    private static DellReadBudget Budget(TimeSpan elapsed) => new(TimeSpan.FromSeconds(120), () => elapsed);

    private static DellCatalogEntry Entry(string id) => DellAttributeCatalog.Find(id)!;

    private static DellCatalogEntry Entry(CctkReadOption option) => Catalog.Single(entry => entry.CctkOption == option);

    private static FirmwareSetting Setting(DellProviderRead read, string id) => Assert.Single(read.Settings, setting => setting.Id == id);

    private static void AssertProblem(DellInterfaceProblem? problem, DellProblemKind kind, string detail)
    {
        Assert.NotNull(problem);
        Assert.Equal(new DellInterfaceProblem(DellInterface.DellCommandConfigure, kind, detail), problem);
    }

    /// <summary>
    /// The states of a read whose run for catalog row <paramref name="row"/> (1-based) ended it:
    /// Boot list option not reported with its note, the rows before read, that row not reported,
    /// and every row after it not read, without a value, possible values or a note.
    /// </summary>
    private static void AssertStoppedAt(DellProviderRead read, int row)
    {
        AssertStates(read, row, SettingState.NotReported);
    }

    /// <summary>The states of a read that started no run for catalog row <paramref name="row"/> (1-based) or after it: that row and every row after it not read.</summary>
    private static void AssertNotRunFrom(DellProviderRead read, int row)
    {
        AssertStates(read, row, SettingState.NotRead);
    }

    private static void AssertStates(DellProviderRead read, int row, SettingState stopped)
    {
        SettingState[] expected = Enumerable.Range(1, Catalog.Count)
            .Select(number => number == 1 ? SettingState.NotReported : number < row ? SettingState.Read : number == row ? stopped : SettingState.NotRead)
            .ToArray();
        Assert.Equal(expected, read.Settings.Select(setting => setting.State));
        Assert.Equal(DellSentences.NotReadByFallbackNote, read.Settings[0].Note);
        Assert.All(read.Settings.Skip(row - 1), setting =>
        {
            Assert.Null(setting.Current);
            Assert.Empty(setting.PossibleValues);
            Assert.Null(setting.Note);
        });
    }

    /// <summary>
    /// Clears the latch a timed-out run sets (<see cref="CctkSettingsProvider.EarlierRunTimedOut"/>),
    /// which the product never clears, so the tests after this one start as a new process would.
    /// </summary>
    private static void ForgetTheTimedOutRun()
    {
        FieldInfo? latch = typeof(CctkSettingsProvider).GetField("s_runTimedOut", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(latch);
        latch!.SetValue(null, 0);
        Assert.False(CctkSettingsProvider.EarlierRunTimedOut);
    }

    /// <summary>
    /// The provider composed through its seam over scripted runs: each option answers what
    /// <c>answer</c> gives, or, when that is null, the unit's A18 line. Every run is recorded with
    /// its bound, its start and its end, and how many ran at once.
    /// </summary>
    private sealed class Fake
    {
        private int _inFlight;

        public Fake(Func<CctkReadOption, CctkRunOutcome?>? answer = null, bool found = true, ILogger? logger = null)
        {
            Provider = new(
                logger ?? Logger.None,
                () => found,
                (option, bound) =>
                {
                    MostInFlight = Math.Max(MostInFlight, Interlocked.Increment(ref _inFlight));
                    Events.Add("start " + option);
                    Runs.Add((option, bound));
                    try
                    {
                        return answer?.Invoke(option) ?? CctkOutputFixtures.Answer(option, CctkOutputFixtures.UnitA18Output(option));
                    }
                    finally
                    {
                        Events.Add("end " + option);
                        Interlocked.Decrement(ref _inFlight);
                    }
                });
        }

        public CctkSettingsProvider Provider { get; }

        public List<(CctkReadOption Option, TimeSpan Bound)> Runs { get; } = new();

        public IReadOnlyList<CctkReadOption> RunOptions => Runs.Select(run => run.Option).ToArray();

        public List<string> Events { get; } = new();

        public int MostInFlight { get; private set; }
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
