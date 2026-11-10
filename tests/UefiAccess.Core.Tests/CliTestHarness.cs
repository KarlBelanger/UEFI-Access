// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using UefiAccess.Cli;
using UefiAccess.Cli.Services;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Privileges;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Settings;
using UefiAccess.Core.Settings.Dell;
using UefiAccess.Core.Tests.Fixtures;
using UefiAccess.Core.Tests.Safety;
using UefiAccess.Core.Variables;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// Shared plumbing for the CLI test classes that run commands through <see cref="Program.Run"/>:
/// a <see cref="CliPlatform"/> over any store with fakes for the firmware type detector, the
/// privilege manager (elevated, privilege enabled), the platform inspectors and the Dell settings
/// reader (<see cref="ScriptedDellSettingsReader"/>, with the scripted reads of a computer without
/// the Dell tools, of the unit through Dell Command | Monitor and of the fallback), captured
/// standard output and standard error, and a <see cref="ScriptedStore"/> whose enumeration and
/// single reads are set per test (the known-names fallback, an enumeration that stopped early, a
/// read that fails). For the write commands, <see cref="WriteRig"/> adds the write services over the
/// transaction fakes, with every folder under the test's own temporary folder and a unit check
/// over a made-up machine ID (the test unit's by default; <see cref="AnotherOptiPlexOnA18"/> is
/// the tester's case). Nothing here touches WMI, the registry, a child process, a native call, the
/// firmware, BitLocker or ProgramData.
/// </summary>
internal static class CliTestHarness
{
    /// <summary>The detail Dell Command | Monitor's provider gives a namespace that is absent.</summary>
    private const string NotInstalledDetail = "the namespace root\\dcim\\sysman is absent";

    /// <summary>When the scripted Dell reads started: 5 October 2026, 09:12:44 at UTC+2.</summary>
    public static readonly DateTimeOffset DellReadAt = new(2026, 10, 5, 9, 12, 44, TimeSpan.FromHours(2));

    /// <summary>The machine of the scripted Dell reads: the unit's three fields, never a service tag.</summary>
    public static readonly DellMachine DellUnit = new("Dell Inc.", "OptiPlex 7020", "A18");

    /// <summary>Runs one command line against <paramref name="platform"/>; line endings are normalised to "\n".</summary>
    /// <param name="platform">The machine-facing services.</param>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The exit code and both streams.</returns>
    public static CliRun Run(CliPlatform platform, params string[] args)
    {
        using StringWriter output = new(CultureInfo.InvariantCulture);
        using StringWriter error = new(CultureInfo.InvariantCulture);
        int exitCode = Program.Run(
            args,
            verbose => CliServices.Create(verbose, _ => platform, output, error, Serilog.Core.Logger.None),
            output,
            error);
        return new CliRun(exitCode, Normalize(output.ToString()), Normalize(error.ToString()));
    }

    /// <summary>A platform over <paramref name="store"/> with fixed inspector results.</summary>
    /// <param name="store">The variable store.</param>
    /// <param name="bitLocker">BitLocker result; default one encrypted C: with TPM and PCR 7+11.</param>
    /// <param name="dell">Dell probe result; default no Dell interface at all.</param>
    /// <param name="policy">Secure Boot policy; default every source reports enabled.</param>
    /// <param name="dellSettings">
    /// The Dell settings reader; default a <see cref="ScriptedDellSettingsReader"/> that answers
    /// <see cref="DellNothingInstalled"/>, as a computer without Dell Command | Monitor and Dell
    /// Command | Configure would. Never the real reader.
    /// </param>
    /// <returns>The platform.</returns>
    public static CliPlatform CreatePlatform(
        IFirmwareVariableStore store,
        BitLockerResult? bitLocker = null,
        DellInterfaceProbeResult? dell = null,
        SecureBootPolicyResult? policy = null,
        IDellSettingsReader? dellSettings = null)
    {
        SecureBootPolicyResult policyResult = policy ?? new SecureBootPolicyResult(true, true, false, null, null, true, true, true, Array.Empty<string>());
        return new CliPlatform(
            new FakePrivilegeManager(),
            new FakeFirmwareTypeDetector(),
            store,
            new FixedInspector<MachineIdentityResult>(new MachineIdentityResult(
                "Dell Inc.", "OptiPlex 7020", "OptiPlex 7020", "A18", "2019-04-05", "ABC1234", "0N4YC8",
                "Intel(R) Core(TM) i7-4790 CPU @ 3.60GHz", "Microsoft Windows 11 Pro", "10.0.26200", "26200", true,
                "Expected model confirmed.", Array.Empty<string>())),
            new FixedInspector<TpmInfoResult>(new TpmInfoResult(true, "1.2, 2, 3", true, true, Array.Empty<string>())),
            new FixedInspector<BitLockerResult>(bitLocker ?? new BitLockerResult(
                new[] { new BitLockerVolume("C:", 1, "On", 1, "Encrypted", new[] { "TPM" }, new[] { 7, 11 }, "PCR 7+11") },
                Array.Empty<string>())),
            new FixedInspector<DellInterfaceProbeResult>(dell ?? new DellInterfaceProbeResult(
                false, Array.Empty<string>(), Array.Empty<DellAttributeSample>(), false, null, null, null, false, false, Array.Empty<string>())),
            _ => new FixedInspector<SecureBootPolicyResult>(policyResult),
            null,
            null,
            dellSettings ?? new ScriptedDellSettingsReader(DellNothingInstalled));
    }

    /// <summary>
    /// A computer without the Dell tools, as the reader reports it:
    /// nothing answered; Dell Command | Monitor not installed when the options let it be tried, Dell
    /// Command | Configure not found when they let it be tried; every agreed setting not read.
    /// </summary>
    /// <param name="options">The options the read was given.</param>
    /// <returns>The result.</returns>
    public static DellSettingsResult DellNothingInstalled(DellReadOptions options)
    {
        List<DellInterfaceProblem> problems = new();
        if (options.Preference != DellSourcePreference.DellCommandConfigure)
        {
            problems.Add(new DellInterfaceProblem(DellInterface.DellCommandMonitor, DellProblemKind.NotInstalled, NotInstalledDetail));
        }

        if (options.Preference != DellSourcePreference.DellCommandMonitor)
        {
            problems.Add(new DellInterfaceProblem(DellInterface.DellCommandConfigure, DellProblemKind.ProgramNotFound, "cctk.exe was not found in the Program Files folders"));
        }

        return new DellSettingsResult(
            DellReadOutcome.Unavailable,
            null,
            DellReadAt,
            40,
            DellUnit,
            DellAttributeCatalog.Default.Select(entry => DellSettingMapper.NotRead(entry, null)).ToArray(),
            problems,
            Array.Empty<string>());
    }

    /// <summary>
    /// The unit as Dell Command | Monitor reads it (the 72 rows of the A18 export,
    /// <see cref="DcmRowFixtures.UnitA18"/>): every agreed setting read, outcome Complete, at
    /// <see cref="DellReadAt"/> in 1180 ms; the full enumeration kept when the options ask for it.
    /// </summary>
    /// <param name="options">The options the read was given.</param>
    /// <returns>The result.</returns>
    public static DellSettingsResult DellCompleteA18(DellReadOptions options) => DellFromDcm(DcmRowFixtures.UnitA18(), options);

    /// <summary>
    /// Dell Command | Monitor's answer over <paramref name="rows"/>, made by the provider's and the
    /// reader's own pure functions: one setting per agreed entry, the dependency rule applied,
    /// Complete when every one was read and Partial otherwise.
    /// </summary>
    /// <param name="rows">The DCIM_BIOSEnumeration rows.</param>
    /// <param name="options">The options the read was given.</param>
    /// <returns>The result.</returns>
    public static DellSettingsResult DellFromDcm(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, DellReadOptions options)
    {
        DellProviderRead read = DcmSettingsProvider.FromOutcome(DcmRowFixtures.Answer(rows), true, DellAttributeCatalog.Default, options.IncludeAllAttributes);
        IReadOnlyList<FirmwareSetting> settings = DellSettingsReader.ApplyDependencies(DellAttributeCatalog.Default, DellSettingsReader.InCatalogOrder(DellAttributeCatalog.Default, read.Settings));
        DellReadOutcome outcome = settings.All(setting => setting.State is SettingState.Read or SettingState.NotApplicable) ? DellReadOutcome.Complete : DellReadOutcome.Partial;
        return new DellSettingsResult(outcome, DellSettingsSource.DellCommandMonitor, DellReadAt, 1180, DellUnit, settings, Array.Empty<DellInterfaceProblem>(), Array.Empty<string>())
        {
            AllAttributes = options.IncludeAllAttributes ? read.AllAttributes : null,
        };
    }

    /// <summary>
    /// The fallback on a computer without Dell Command | Monitor: Dell Command | Configure read the
    /// nine settings it reads, with the unit's values in its 5.2.2 words; Boot list option is not
    /// reported, with its note; outcome Partial, with the fallback sentence.
    /// </summary>
    /// <returns>The result.</returns>
    public static DellSettingsResult DellFallback()
    {
        string[] printed = { string.Empty, "Enabled", "Ahci", "Enabled", "Enabled", "Enabled", "Enabled", "Enabled", "Disabled", "Enabled" };
        IReadOnlyList<DellCatalogEntry> catalog = DellAttributeCatalog.Default;
        FirmwareSetting[] settings = catalog
            .Select((entry, index) => entry.CctkOption is null
                ? DellSettingMapper.NotReported(entry, PossibleValuesOrigin.Catalog, DellSentences.NotReadByFallbackNote)
                : DellSettingMapper.FromCctk(entry, printed[index]))
            .ToArray();
        return new DellSettingsResult(
            DellReadOutcome.Partial,
            DellSettingsSource.DellCommandConfigure,
            DellReadAt,
            3460,
            DellUnit,
            DellSettingsReader.ApplyDependencies(catalog, settings),
            new[] { new DellInterfaceProblem(DellInterface.DellCommandMonitor, DellProblemKind.NotInstalled, NotInstalledDetail) },
            Array.Empty<string>())
        {
            FallbackSentence = "Dell Command Monitor is not installed. Dell Command Configure was used instead.",
        };
    }

    /// <summary>Parses standard output as exactly one JSON document; trailing content fails the parse.</summary>
    /// <param name="text">Standard output.</param>
    /// <returns>The root element.</returns>
    public static JsonElement ParseSingleDocument(string text)
    {
        Assert.False(string.IsNullOrWhiteSpace(text), "expected one JSON document on standard output");
        using JsonDocument document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    /// <summary>Asserts a two-column table row "field   value" (value given as a regular expression).</summary>
    /// <param name="output">Standard output.</param>
    /// <param name="field">The first column, literally.</param>
    /// <param name="valuePattern">The second column as a regular expression.</param>
    public static void AssertRow(string output, string field, string valuePattern)
    {
        Assert.Matches("(?m)^" + Regex.Escape(field) + " +" + valuePattern + "$", output);
    }

    /// <summary>The OS volume suspended: protection off, fully encrypted, the unit's protectors and PCR profile still there.</summary>
    /// <returns>The inspector's result.</returns>
    public static BitLockerResult BitLockerSuspended() => new(
        new[] { new BitLockerVolume(BitLockerPreflight.OsDriveLetter, 0, "Off", 1, "Encrypted", new[] { "TPM", "Numerical password" }, new[] { 0, 2, 4, 11 }, "PCR 0,2,4,11 (legacy)") },
        Array.Empty<string>());

    /// <summary>The OS volume still being encrypted with the unit's protectors: protection turns on by itself when it finishes.</summary>
    /// <returns>The inspector's result.</returns>
    public static BitLockerResult BitLockerEncrypting() => new(
        new[] { new BitLockerVolume(BitLockerPreflight.OsDriveLetter, 0, "Pending until encryption completes", 2, "Encrypting", new[] { "TPM", "Numerical password" }, new[] { 0, 2, 4, 11 }, "PCR 0,2,4,11 (legacy)") },
        Array.Empty<string>());

    /// <summary>A machine that is not the 2014 OptiPlex 7020: another model and BIOS version, no service tag.</summary>
    /// <returns>The identity.</returns>
    public static MachineIdentityResult OtherMachine() => SafetyFixtures.Identity(confirmed: false);

    /// <summary>The OptiPlex 7020 on a BIOS version other than A18: the model is confirmed, the contract is not.</summary>
    /// <returns>The identity.</returns>
    public static MachineIdentityResult OptiPlexOnA17() => SafetyFixtures.Identity(confirmed: true) with { BiosVersion = "A17" };

    /// <summary>
    /// the tester's case: another OptiPlex 7020 on BIOS A18, the contracted model and BIOS version, whose
    /// Windows machine ID is not the M3 test unit's (<see cref="SafetyFixtures.OtherUnitMachineId"/>).
    /// </summary>
    /// <param name="set">The firmware's variables; <see cref="SafetyFixtures.Store"/> when null.</param>
    /// <returns>The rig.</returns>
    public static WriteRig AnotherOptiPlexOnA18(VariableSet? set = null) => new(set) { MachineId = SafetyFixtures.OtherUnitMachineId };

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>
    /// A platform with the write services over the transaction fakes: a scripted store shaped like
    /// the unit (<see cref="SafetyFixtures.Store"/>, BootOrder Boot0002 and Boot0003, BootCurrent
    /// Boot0002, BootNext not set), a scripted writer and a fake suspender that record every call
    /// and never reach native code or WMI, fixed inspectors (the contracted machine, BitLocker off
    /// unless given, Secure Boot on, TPM 1.2), a unit check over a made-up machine ID
    /// (<see cref="MachineId"/>, the test unit's unless set; never the registry), and a data folder
    /// under the test's own temporary folder holding the snapshot root, the writes folder and an
    /// empty baseline root. The folder check accepts every folder unless <see cref="RefuseFolders"/>
    /// is set, and runs <see cref="OnPrepareFolder"/> first, so a test can act at the moment stage
    /// S12 creates the transaction folder. Disposing it deletes the data folder.
    /// </summary>
    internal sealed class WriteRig : IDisposable
    {
        /// <summary>The sentence the folder check refuses with.</summary>
        public const string Refusal = "refused by the test";

        /// <summary>Creates the rig.</summary>
        /// <param name="set">The firmware's variables; <see cref="SafetyFixtures.Store"/> when null.</param>
        /// <param name="bitLocker">The BitLocker facts; <see cref="TransactionFakes.BitLockerOff"/> when null.</param>
        /// <param name="identity">The machine identity; the contracted OptiPlex 7020 on A18 when null.</param>
        public WriteRig(VariableSet? set = null, BitLockerResult? bitLocker = null, MachineIdentityResult? identity = null)
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "cli-write-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
            SnapshotRoot = Path.Combine(DataDirectory, SnapshotStore.FolderName);
            WritesDirectory = Path.Combine(DataDirectory, WriteRecordLog.FolderName);
            BaselineRoot = Path.Combine(DataDirectory, "baseline");
            Store = new TransactionFakes.ScriptedStore((set ?? SafetyFixtures.Store()).All);
            Writer = new TransactionFakes.ScriptedWriter(Store, SnapshotPendingOnDisk);
            Suspender = new TransactionFakes.FakeSuspender(Store, SnapshotPendingOnDisk);
            Identity = new TransactionFakes.FixedInspector<MachineIdentityResult>(identity ?? SafetyFixtures.Identity(confirmed: true));
            BitLocker = new TransactionFakes.FixedInspector<BitLockerResult>(bitLocker ?? TransactionFakes.BitLockerOff());
            SecureBoot = new TransactionFakes.FixedInspector<SecureBootPolicyResult>(TransactionFakes.SecureBootOn());
            Tpm = new TransactionFakes.FixedInspector<TpmInfoResult>(new TpmInfoResult(true, "1.2, 2, 3", true, true, Array.Empty<string>()));
            Dell = new TransactionFakes.FixedInspector<DellInterfaceProbeResult>(new DellInterfaceProbeResult(
                false, Array.Empty<string>(), Array.Empty<DellAttributeSample>(), false, null, null, null, false, false, Array.Empty<string>()));
            Platform = new CliPlatform(
                Privileges,
                Detector,
                Store,
                Identity,
                Tpm,
                BitLocker,
                Dell,
                _ => SecureBoot,
                PrepareFolder,
                new CliWriteServices(
                    Writer,
                    Suspender,
                    root => new SnapshotStore(root ?? SnapshotRoot, DataDirectory, PrepareFolder),
                    () => new WriteRecordLog(WritesDirectory, PrepareFolder),
                    BaselineRoot,
                    new EnabledUnit(() => MachineId, SafetyFixtures.TestUnitMachineIdSha256)));
        }

        /// <summary>
        /// The Windows machine ID the unit check reads: <see cref="SafetyFixtures.TestUnitMachineId"/>
        /// (the test unit) by default, <see cref="SafetyFixtures.OtherUnitMachineId"/> for another
        /// OptiPlex 7020 (<see cref="AnotherOptiPlexOnA18"/>), null for one that could not be read.
        /// </summary>
        public string? MachineId { get; set; } = SafetyFixtures.TestUnitMachineId;

        /// <summary>The rig's data folder, under the temporary folder.</summary>
        public string DataDirectory { get; }

        /// <summary>The default snapshot root under <see cref="DataDirectory"/>.</summary>
        public string SnapshotRoot { get; }

        /// <summary>The writes folder under <see cref="DataDirectory"/>, which holds write-log.jsonl.</summary>
        public string WritesDirectory { get; }

        /// <summary>The baseline root under <see cref="DataDirectory"/>; it does not exist, so nothing is compared.</summary>
        public string BaselineRoot { get; }

        /// <summary>The store.</summary>
        public TransactionFakes.ScriptedStore Store { get; }

        /// <summary>The writer.</summary>
        public TransactionFakes.ScriptedWriter Writer { get; }

        /// <summary>The suspender.</summary>
        public TransactionFakes.FakeSuspender Suspender { get; }

        /// <summary>The firmware type detector.</summary>
        public TransactionFakes.FakeDetector Detector { get; } = new();

        /// <summary>The privilege manager.</summary>
        public TransactionFakes.FakePrivileges Privileges { get; } = new();

        /// <summary>The identity inspector.</summary>
        public TransactionFakes.FixedInspector<MachineIdentityResult> Identity { get; }

        /// <summary>The BitLocker inspector.</summary>
        public TransactionFakes.FixedInspector<BitLockerResult> BitLocker { get; }

        /// <summary>The Secure Boot inspector.</summary>
        public TransactionFakes.FixedInspector<SecureBootPolicyResult> SecureBoot { get; }

        /// <summary>The TPM inspector.</summary>
        public TransactionFakes.FixedInspector<TpmInfoResult> Tpm { get; }

        /// <summary>The Dell probe.</summary>
        public TransactionFakes.FixedInspector<DellInterfaceProbeResult> Dell { get; }

        /// <summary>The platform the command lines run against.</summary>
        public CliPlatform Platform { get; }

        /// <summary>When true, the folder check refuses every folder.</summary>
        public bool RefuseFolders { get; set; }

        /// <summary>Runs with every folder the check is asked about, before it answers.</summary>
        public Action<string>? OnPrepareFolder { get; set; }

        /// <summary>The full path of write-log.jsonl.</summary>
        public string WriteLogPath => Path.Combine(WritesDirectory, WriteRecordLog.FileName);

        /// <summary>Runs one command line against the rig's platform.</summary>
        /// <param name="args">The command-line arguments.</param>
        /// <returns>The exit code and both streams.</returns>
        public CliRun Run(params string[] args) => CliTestHarness.Run(Platform, args);

        /// <summary>Every record in write-log.jsonl, oldest first.</summary>
        /// <returns>The records; empty when the file does not exist.</returns>
        public IReadOnlyList<WriteRecord> Records() => new WriteRecordLog(WritesDirectory, PrepareFolder).ReadAll();

        /// <summary>The transaction folders under the default snapshot root.</summary>
        /// <returns>The full paths; empty when the root does not exist.</returns>
        public string[] TransactionFolders() => Directory.Exists(SnapshotRoot) ? Directory.GetDirectories(SnapshotRoot) : Array.Empty<string>();

        /// <summary>True when a transaction folder holds snapshot.json, before\index.json and a Pending record.json.</summary>
        /// <returns>Whether such a folder exists now.</returns>
        public bool SnapshotPendingOnDisk()
        {
            return TransactionFolders().Any(folder =>
                File.Exists(Path.Combine(folder, SnapshotStore.SnapshotFileName))
                && File.Exists(Path.Combine(folder, SnapshotStore.BeforeDirectoryName, "index.json"))
                && File.Exists(Path.Combine(folder, SnapshotStore.RecordFileName))
                && WriteRecord.TryParse(File.ReadAllText(Path.Combine(folder, SnapshotStore.RecordFileName)))?.Outcome == WriteRecordOutcome.Pending);
        }

        /// <summary>Appends <paramref name="count"/> records of past writes to write-log.jsonl, each at <paramref name="at"/>.</summary>
        /// <param name="count">How many.</param>
        /// <param name="at">Their timestamp.</param>
        public void AddPastWrites(int count, DateTimeOffset at)
        {
            WriteRecordLog log = new(WritesDirectory, PrepareFolder);
            for (int i = 0; i < count; i++)
            {
                WriteRecord record = new()
                {
                    TimestampUtc = WriteRecord.FormatTimestamp(at),
                    TransactionId = "past-" + i.ToString(CultureInfo.InvariantCulture),
                    ApiCalled = true,
                    Outcome = WriteRecordOutcome.Verified,
                };
                Assert.Null(log.Append(record));
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            TestFolders.DeleteQuietly(DataDirectory);
        }

        private string? PrepareFolder(string folder)
        {
            OnPrepareFolder?.Invoke(folder);
            return RefuseFolders ? Refusal : null;
        }
    }

    /// <summary>Captured outcome of one command line.</summary>
    /// <param name="ExitCode">The exit code Program.Run returned.</param>
    /// <param name="Output">Standard output.</param>
    /// <param name="Error">Standard error.</param>
    internal sealed record CliRun(int ExitCode, string Output, string Error);

    /// <summary>
    /// A store with a scripted enumeration and scripted single reads: <see cref="Enumerate"/>
    /// returns the given result, <see cref="Read"/> returns the variable from <see cref="Readable"/>,
    /// the failure from <see cref="Failures"/>, or ERROR_ENVVAR_NOT_FOUND, and records every name read.
    /// </summary>
    internal sealed class ScriptedStore : IFirmwareVariableStore
    {
        private readonly EnumerationResult _enumeration;

        /// <summary>Creates the store.</summary>
        /// <param name="enumeration">What <see cref="Enumerate"/> returns.</param>
        public ScriptedStore(EnumerationResult enumeration)
        {
            _enumeration = enumeration;
        }

        /// <summary>Variables a direct read finds, whether or not the enumeration listed them.</summary>
        public List<FirmwareVariable> Readable { get; } = new();

        /// <summary>Names whose direct read fails, with the status to return.</summary>
        public Dictionary<string, ReadStatus> Failures { get; } = new(StringComparer.Ordinal);

        /// <summary>Every name passed to <see cref="Read"/>, in call order.</summary>
        public List<string> Reads { get; } = new();

        /// <summary>A store whose enumeration is the known-names fallback over <paramref name="listed"/>.</summary>
        /// <param name="listed">The variables the fallback found.</param>
        /// <returns>The store; every listed variable is also readable.</returns>
        public static ScriptedStore Fallback(params FirmwareVariable[] listed)
        {
            ScriptedStore store = new(new EnumerationResult(
                listed,
                EnumerationMethod.KnownNamesFallback,
                listed.Length,
                null,
                new[]
                {
                    "NtEnumerateSystemEnvironmentValuesEx failed: STATUS_INVALID_PARAMETER (0xC000000D); falling back to reading the curated KnownVariableNames list one by one",
                    "the fallback only sees the curated names and Boot#### entries; vendor-specific variables are not listed by this path",
                }));
            store.Readable.AddRange(listed);
            return store;
        }

        /// <inheritdoc />
        public EnumerationResult Enumerate() => _enumeration;

        /// <inheritdoc />
        public ReadResult Read(string name, Guid vendorGuid)
        {
            Reads.Add(name);
            if (Failures.TryGetValue(name, out ReadStatus status))
            {
                return new ReadResult(null, status, status == ReadStatus.AccessDenied ? "ERROR_ACCESS_DENIED (5): access denied" : "scripted failure", 0);
            }

            FirmwareVariable? variable = Readable.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.Ordinal) && v.VendorGuid == vendorGuid);
            return variable is null
                ? new ReadResult(null, ReadStatus.NotFound, "ERROR_ENVVAR_NOT_FOUND (203): the firmware variable was not found", 65536)
                : new ReadResult(variable, ReadStatus.Success, null, 65536);
        }
    }

    /// <summary>
    /// A Dell settings reader that answers what the test scripts and records every read's options;
    /// it reads nothing, so no test reads Dell Command | Monitor or starts Dell Command | Configure
    /// on this PC (DellSettingsGuardTests.TestsNeverRunTheRealDellReader).
    /// </summary>
    internal sealed class ScriptedDellSettingsReader : IDellSettingsReader
    {
        private readonly Func<DellReadOptions, DellSettingsResult> _answer;

        /// <summary>Creates the reader.</summary>
        /// <param name="answer">What a read answers, given its options; it may throw, as a broken reader would.</param>
        public ScriptedDellSettingsReader(Func<DellReadOptions, DellSettingsResult> answer)
        {
            _answer = answer;
        }

        /// <summary>The options of every read, in call order.</summary>
        public List<DellReadOptions> Reads { get; } = new();

        /// <inheritdoc />
        public DellSettingsResult Read(DellReadOptions options)
        {
            Reads.Add(options);
            return _answer(options);
        }
    }

    private sealed class FakePrivilegeManager : IPrivilegeManager
    {
        public bool IsElevated => true;

        public PrivilegeState EnableSystemEnvironmentPrivilege() => PrivilegeState.Enabled;
    }

    private sealed class FakeFirmwareTypeDetector : IFirmwareTypeDetector
    {
        public FirmwareType Detect() => FirmwareType.Uefi;
    }

    private sealed class FixedInspector<T> : IPlatformInspector<T>
    {
        private readonly T _result;

        public FixedInspector(T result)
        {
            _result = result;
        }

        public T Inspect() => _result;
    }
}
