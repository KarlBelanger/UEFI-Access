// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Privileges;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Variables;
using Xunit;

namespace UefiAccess.Core.Tests.Safety;

/// <summary>
/// The fakes the transaction tests share: a store whose values change only when a test or the
/// scripted writer changes them, a writer that records every call and never reaches native code,
/// a suspender that records every call, and fixed platform inspectors. Each fake notes what was
/// on disk and how many reads had happened at the moment it was called, so the order of the
/// stages is asserted from inside the call. Nothing here constructs the real writer or the real
/// suspender or names the real native seam. The types are nested so that a type of the same name
/// elsewhere in the test project cannot be mistaken for them.
/// </summary>
internal static class TransactionFakes
{
    /// <summary>The Win32_EncryptableVolume facts of the unit: C: on, TPM and numerical password, PCR 0, 2, 4, 11.</summary>
    public static BitLockerResult BitLockerOn() => new(
        new[] { new BitLockerVolume(BitLockerPreflight.OsDriveLetter, 1, "On", 1, "Encrypted", new[] { "TPM", "Numerical password" }, new[] { 0, 2, 4, 11 }, "PCR 0,2,4,11 (legacy)") },
        Array.Empty<string>());

    /// <summary>A decrypted OS volume without protectors.</summary>
    public static BitLockerResult BitLockerOff() => new(
        new[] { new BitLockerVolume(BitLockerPreflight.OsDriveLetter, 0, "Off", 0, "Decrypted", Array.Empty<string>(), Array.Empty<int>(), null) },
        Array.Empty<string>());

    /// <summary>Secure Boot on, user mode, the sources agreeing.</summary>
    public static SecureBootPolicyResult SecureBootOn() => new(true, true, false, null, null, null, true, true, Array.Empty<string>());

    /// <summary>A store with a scripted enumeration and reads that answer the current values, which change only when told to.</summary>
    internal sealed class ScriptedStore : IFirmwareVariableStore
    {
        private readonly object _sync = new();
        private readonly Dictionary<(Guid Guid, string Name), FirmwareVariable> _current = new();
        private readonly Dictionary<string, int> _readCounts = new(StringComparer.Ordinal);
        private readonly List<(string Name, int Number, FirmwareVariable? Replacement, Guid Guid)> _changes = new();
        private readonly List<string> _reads = new();

        /// <summary>Creates the store over the variables.</summary>
        /// <param name="variables">The values the firmware holds.</param>
        public ScriptedStore(IEnumerable<FirmwareVariable> variables)
        {
            foreach (FirmwareVariable variable in variables)
            {
                _current[(variable.VendorGuid, variable.Name)] = variable;
            }
        }

        /// <summary>The method the enumeration reports.</summary>
        public EnumerationMethod Method { get; set; } = EnumerationMethod.NtEnumerate;

        /// <summary>The error the enumeration reports, or null.</summary>
        public string? EnumerationError { get; set; }

        /// <summary>Names whose every read fails, with the status to return.</summary>
        public Dictionary<string, ReadStatus> Failures { get; } = new(StringComparer.Ordinal);

        /// <summary>Every name read, in call order.</summary>
        public IReadOnlyList<string> Reads
        {
            get
            {
                lock (_sync)
                {
                    return _reads.ToArray();
                }
            }
        }

        /// <inheritdoc />
        public EnumerationResult Enumerate()
        {
            lock (_sync)
            {
                FirmwareVariable[] all = _current.Values.ToArray();
                return new EnumerationResult(all, Method, all.Length, EnumerationError, Array.Empty<string>());
            }
        }

        /// <inheritdoc />
        public ReadResult Read(string name, Guid vendorGuid)
        {
            lock (_sync)
            {
                _reads.Add(name);
                int number = (_readCounts.TryGetValue(name, out int count) ? count : 0) + 1;
                _readCounts[name] = number;
                foreach ((string Name, int Number, FirmwareVariable? Replacement, Guid Guid) change in _changes.Where(change => change.Name == name && change.Number == number).ToArray())
                {
                    if (change.Replacement is null)
                    {
                        _current.Remove((change.Guid, change.Name));
                    }
                    else
                    {
                        _current[(change.Guid, change.Name)] = change.Replacement;
                    }
                }

                if (Failures.TryGetValue(name, out ReadStatus status))
                {
                    return new ReadResult(null, status, "scripted failure", 0);
                }

                return _current.TryGetValue((vendorGuid, name), out FirmwareVariable? variable)
                    ? new ReadResult(variable, ReadStatus.Success, null, 65536)
                    : new ReadResult(null, ReadStatus.NotFound, "ERROR_ENVVAR_NOT_FOUND (203): the firmware variable was not found", 65536);
            }
        }

        /// <summary>How many times <paramref name="name"/> has been read.</summary>
        /// <param name="name">The variable name.</param>
        /// <returns>The count.</returns>
        public int ReadsOf(string name)
        {
            lock (_sync)
            {
                return _readCounts.TryGetValue(name, out int count) ? count : 0;
            }
        }

        /// <summary>The value the firmware holds now, or null.</summary>
        /// <param name="name">The variable name under the EFI global GUID.</param>
        /// <returns>The variable or null.</returns>
        public FirmwareVariable? Current(string name)
        {
            lock (_sync)
            {
                return _current.TryGetValue((KnownGuids.EfiGlobalVariable, name), out FirmwareVariable? variable) ? variable : null;
            }
        }

        /// <summary>Something outside the tool changes a variable now.</summary>
        /// <param name="variable">The new value.</param>
        public void Set(FirmwareVariable variable)
        {
            lock (_sync)
            {
                _current[(variable.VendorGuid, variable.Name)] = variable;
            }
        }

        /// <summary>Something outside the tool changes a variable just before its <paramref name="readsFromNow"/>-th read from now is answered.</summary>
        /// <param name="name">The variable name, under the EFI global GUID.</param>
        /// <param name="readsFromNow">1 for the next read of the name, 2 for the one after it.</param>
        /// <param name="replacement">The new value, or null to remove it.</param>
        public void ChangeBeforeRead(string name, int readsFromNow, FirmwareVariable? replacement)
        {
            lock (_sync)
            {
                _changes.Add((name, ReadsOfUnlocked(name) + readsFromNow, replacement, KnownGuids.EfiGlobalVariable));
            }
        }

        /// <summary>What the firmware does with a successful write: the new value, or no variable for size 0.</summary>
        /// <param name="plan">The plan written.</param>
        public void Apply(WritePlan plan)
        {
            lock (_sync)
            {
                if (plan.Size == 0)
                {
                    _current.Remove((plan.Guid, plan.Name));
                }
                else
                {
                    _current[(plan.Guid, plan.Name)] = new FirmwareVariable(plan.Name, plan.Guid, (VariableAttributes)plan.Attributes, plan.Bytes);
                }
            }
        }

        private int ReadsOfUnlocked(string name) => _readCounts.TryGetValue(name, out int count) ? count : 0;
    }

    /// <summary>
    /// The writer's fake: records each plan it is handed, notes whether the snapshot folder was on
    /// disk and which reads had happened at that moment, and answers with a success or a Win32
    /// error, or throws what <see cref="Throws"/> holds. By default a success changes the store and
    /// a failure does not.
    /// </summary>
    internal sealed class ScriptedWriter : IFirmwareVariableWriter
    {
        private readonly ScriptedStore _store;
        private readonly Func<bool> _snapshotReady;
        private readonly List<WritePlan> _calls = new();

        /// <summary>Creates the writer.</summary>
        /// <param name="store">The store a successful write changes.</param>
        /// <param name="snapshotReady">Says whether a complete Pending snapshot folder is on disk.</param>
        public ScriptedWriter(ScriptedStore store, Func<bool> snapshotReady)
        {
            _store = store;
            _snapshotReady = snapshotReady;
        }

        /// <summary>The Win32 error to answer; 0 answers success.</summary>
        public int Win32Error { get; set; }

        /// <summary>The NTSTATUS observed beside a failure.</summary>
        public uint NtStatus { get; set; } = 0xC0000001;

        /// <summary>Whether the call changes the store; null changes it on success only.</summary>
        public bool? ChangesTheStore { get; set; }

        /// <summary>Runs inside the call, after the store changed.</summary>
        public Action<WritePlan>? DuringCall { get; set; }

        /// <summary>
        /// When set, the call throws it instead of answering, as the real writer passes on what its
        /// seam throws: after the call is recorded, the store changed (see <see cref="ChangesTheStore"/>)
        /// and <see cref="DuringCall"/> ran.
        /// </summary>
        public Exception? Throws { get; set; }

        /// <summary>Every plan handed to the writer.</summary>
        public IReadOnlyList<WritePlan> Calls => _calls;

        /// <summary>Whether a complete Pending snapshot folder was on disk at the first call; null before it.</summary>
        public bool? SnapshotReadyAtCall { get; private set; }

        /// <summary>The store's reads at the first call; null before it.</summary>
        public IReadOnlyList<string>? ReadsAtCall { get; private set; }

        /// <inheritdoc />
        public WriteOutcome CommitOnce(WritePlan plan)
        {
            _calls.Add(plan);
            SnapshotReadyAtCall ??= _snapshotReady();
            ReadsAtCall ??= _store.Reads;
            if (ChangesTheStore ?? Win32Error == 0)
            {
                _store.Apply(plan);
            }

            DuringCall?.Invoke(plan);
            if (Throws is Exception thrown)
            {
                throw thrown;
            }

            TimeSpan elapsed = TimeSpan.FromMilliseconds(12);
            return Win32Error == 0
                ? WriteStatusMapping.Succeeded(elapsed)
                : WriteStatusMapping.Failed(Win32Error, NtStatus, FirmwareVariableWriter.NtStatusSourceName, elapsed);
        }
    }

    /// <summary>The suspender's fake: records each call and what was on disk and read at that moment.</summary>
    internal sealed class FakeSuspender : IBitLockerSuspender
    {
        private readonly ScriptedStore _store;
        private readonly Func<bool> _snapshotReady;
        private readonly List<string> _calls = new();

        /// <summary>Creates the suspender.</summary>
        /// <param name="store">The store whose reads are noted.</param>
        /// <param name="snapshotReady">Says whether a complete Pending snapshot folder is on disk.</param>
        public FakeSuspender(ScriptedStore store, Func<bool> snapshotReady)
        {
            _store = store;
            _snapshotReady = snapshotReady;
        }

        /// <summary>What the suspend answers.</summary>
        public SuspendResult Result { get; set; } = Confirmed();

        /// <summary>Runs inside the call.</summary>
        public Action? DuringCall { get; set; }

        /// <summary>The drive letter of every call.</summary>
        public IReadOnlyList<string> Calls => _calls;

        /// <summary>Whether a complete Pending snapshot folder was on disk at the first call; null before it.</summary>
        public bool? SnapshotReadyAtCall { get; private set; }

        /// <summary>The store's reads at the first call; null before it.</summary>
        public IReadOnlyList<string>? ReadsAtCall { get; private set; }

        /// <summary>A suspend that answered 0 and whose re-read showed protection off.</summary>
        /// <returns>The result.</returns>
        public static SuspendResult Confirmed() => new(true, true, 1u, 0u, false, 1, 0, BitLockerState.Suspended, "0", null, TimeSpan.FromMilliseconds(40));

        /// <summary>A suspend that answered an HRESULT.</summary>
        /// <param name="returnValue">The HRESULT.</param>
        /// <returns>The result.</returns>
        public static SuspendResult Refused(uint returnValue)
        {
            string hex = "0x" + returnValue.ToString("X8", CultureInfo.InvariantCulture);
            return new SuspendResult(false, true, 1u, returnValue, false, 1, null, BitLockerState.Unknown, hex, CheckSentences.Format(CheckSentences.BitLockerSuspendFailedFormat, returnValue), TimeSpan.FromMilliseconds(40));
        }

        /// <summary>A suspend that did not answer within its bound.</summary>
        /// <returns>The result.</returns>
        public static SuspendResult TimedOut() => new(false, true, 1u, null, true, 1, null, BitLockerState.Unknown, BitLockerSuspender.TimeoutResultText, CheckSentences.BitLockerSuspendTimedOut, TimeSpan.FromSeconds(60));

        /// <inheritdoc />
        public SuspendResult SuspendForOneRestart(string driveLetter)
        {
            _calls.Add(driveLetter);
            SnapshotReadyAtCall ??= _snapshotReady();
            ReadsAtCall ??= _store.Reads;
            DuringCall?.Invoke();
            return Result;
        }
    }

    /// <summary>An inspector with a fixed answer, or one that throws.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    internal sealed class FixedInspector<T> : IPlatformInspector<T>
    {
        /// <summary>Creates the inspector.</summary>
        /// <param name="result">The answer.</param>
        public FixedInspector(T result)
        {
            Result = result;
        }

        /// <summary>The answer.</summary>
        public T Result { get; set; }

        /// <summary>When set, <see cref="Inspect"/> throws it instead of answering.</summary>
        public Exception? Failure { get; set; }

        /// <summary>Runs inside every call, before the answer.</summary>
        public Action? DuringInspect { get; set; }

        /// <summary>How many times the inspector was asked.</summary>
        public int Calls { get; private set; }

        /// <inheritdoc />
        public T Inspect()
        {
            Calls++;
            DuringInspect?.Invoke();
            return Failure is null ? Result : throw Failure;
        }
    }

    /// <summary>A firmware type detector with a fixed answer.</summary>
    internal sealed class FakeDetector : IFirmwareTypeDetector
    {
        /// <summary>The answer.</summary>
        public FirmwareType Type { get; set; } = FirmwareType.Uefi;

        /// <inheritdoc />
        public FirmwareType Detect() => Type;
    }

    /// <summary>A privilege manager with fixed answers.</summary>
    internal sealed class FakePrivileges : IPrivilegeManager
    {
        /// <summary>Whether the process is elevated.</summary>
        public bool Elevated { get; set; } = true;

        /// <summary>What enabling the privilege gives.</summary>
        public PrivilegeState State { get; set; } = PrivilegeState.Enabled;

        /// <inheritdoc />
        public bool IsElevated => Elevated;

        /// <inheritdoc />
        public PrivilegeState EnableSystemEnvironmentPrivilege() => State;
    }
}

/// <summary>
/// One write pipeline over <see cref="TransactionFakes"/>, with its data folder under the test's
/// own temporary folder (never ProgramData), a fixed clock (<see cref="SafetyFixtures.PlanTime"/>)
/// and a Windows start two hours before it. The folder checks can be told to refuse, to stand in
/// for a data root that became unwritable. The unit check reads <see cref="MachineId"/>, a
/// made-up machine ID that is the test unit's by default, never the registry. Disposing it
/// deletes the folder.
/// </summary>
internal sealed class TransactionRig : IDisposable
{
    /// <summary>The sentence the rig's folder check refuses with.</summary>
    public const string Refusal = "refused by the test";

    /// <summary>The fixed now of every transaction.</summary>
    public static readonly DateTimeOffset Now = SafetyFixtures.PlanTime;

    /// <summary>The fixed moment Windows last started.</summary>
    public static readonly DateTimeOffset StartOfWindows = Now.AddHours(-2);

    /// <summary>Creates the rig.</summary>
    /// <param name="set">The firmware's variables; <see cref="SafetyFixtures.Store"/> when null.</param>
    /// <param name="bitLocker">The BitLocker facts; <see cref="TransactionFakes.BitLockerOff"/> when null.</param>
    /// <param name="machineConfirmed">Whether the identity is the contracted machine.</param>
    public TransactionRig(VariableSet? set = null, BitLockerResult? bitLocker = null, bool machineConfirmed = true)
    {
        DataDirectory = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", "transaction-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DataDirectory);
        SnapshotRoot = Path.Combine(DataDirectory, SnapshotStore.FolderName);
        WritesDirectory = Path.Combine(DataDirectory, WriteRecordLog.FolderName);
        BaselineRoot = Path.Combine(DataDirectory, "baseline");

        Store = new TransactionFakes.ScriptedStore((set ?? SafetyFixtures.Store()).All);
        Writer = new TransactionFakes.ScriptedWriter(Store, SnapshotPendingOnDisk);
        Suspender = new TransactionFakes.FakeSuspender(Store, SnapshotPendingOnDisk);
        Identity = new TransactionFakes.FixedInspector<MachineIdentityResult>(SafetyFixtures.Identity(machineConfirmed));
        SecureBoot = new TransactionFakes.FixedInspector<SecureBootPolicyResult>(TransactionFakes.SecureBootOn());
        BitLocker = new TransactionFakes.FixedInspector<BitLockerResult>(bitLocker ?? TransactionFakes.BitLockerOff());
        Tpm = new TransactionFakes.FixedInspector<TpmInfoResult>(new TpmInfoResult(true, "1.2, 2, 3", true, true, Array.Empty<string>()));
        Snapshots = new SnapshotStore(SnapshotRoot, DataDirectory, _ => RefuseSnapshotFolders ? Refusal : null);
        Records = new WriteRecordLog(WritesDirectory, _ => RefuseRecordFolder ? Refusal : null, () => BootId);
        EnabledUnit unit = new(() => MachineIdFailure is Exception failure ? throw failure : MachineId, SafetyFixtures.TestUnitMachineIdSha256);
        Pipeline = new WritePipeline(
            Detector,
            Privileges,
            Store,
            new PlatformPolicy(Identity, SecureBoot, BitLocker, Tpm, Serilog.Core.Logger.None, WmiHelper.QueryTimeout, unit),
            Writer,
            Suspender,
            Snapshots,
            Records,
            BaselineRoot,
            Serilog.Core.Logger.None,
            new TransactionClock(() => ClockFailure is Exception failure ? throw failure : Now, _ => StartOfWindows));
    }

    /// <summary>When set, every reading of the transaction's clock throws it: a stage that fails unexpectedly.</summary>
    public Exception? ClockFailure { get; set; }

    /// <summary>
    /// The Windows machine ID the unit check reads: <see cref="SafetyFixtures.TestUnitMachineId"/>
    /// (the test unit) by default, <see cref="SafetyFixtures.OtherUnitMachineId"/> for another
    /// OptiPlex 7020, null or text that is not a GUID for one that could not be read.
    /// </summary>
    public string? MachineId { get; set; } = SafetyFixtures.TestUnitMachineId;

    /// <summary>When set, the machine ID read throws it instead of answering.</summary>
    public Exception? MachineIdFailure { get; set; }

    /// <summary>The boot identity the write record log reads (Windows' BootId in production); null, the default, is none.</summary>
    public uint? BootId { get; set; }

    /// <summary>The rig's data folder, under the temporary folder.</summary>
    public string DataDirectory { get; }

    /// <summary>The snapshot root under <see cref="DataDirectory"/>.</summary>
    public string SnapshotRoot { get; }

    /// <summary>The writes folder under <see cref="DataDirectory"/>.</summary>
    public string WritesDirectory { get; }

    /// <summary>The baseline root under <see cref="DataDirectory"/> (empty unless a test fills it).</summary>
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

    /// <summary>The Secure Boot inspector.</summary>
    public TransactionFakes.FixedInspector<SecureBootPolicyResult> SecureBoot { get; }

    /// <summary>The BitLocker inspector.</summary>
    public TransactionFakes.FixedInspector<BitLockerResult> BitLocker { get; }

    /// <summary>The TPM inspector.</summary>
    public TransactionFakes.FixedInspector<TpmInfoResult> Tpm { get; }

    /// <summary>The snapshot store.</summary>
    public SnapshotStore Snapshots { get; }

    /// <summary>The write record log.</summary>
    public WriteRecordLog Records { get; }

    /// <summary>The pipeline under test.</summary>
    public WritePipeline Pipeline { get; }

    /// <summary>When true, the snapshot store's folder check refuses every folder.</summary>
    public bool RefuseSnapshotFolders { get; set; }

    /// <summary>When true, the write record log's folder check refuses its folder.</summary>
    public bool RefuseRecordFolder { get; set; }

    /// <summary>The transaction folders under the snapshot root.</summary>
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

    /// <summary>Runs the dry run.</summary>
    /// <param name="request">The request.</param>
    /// <param name="choice">The BitLocker choice given with it, or null to leave it to the confirmation.</param>
    /// <returns>The result.</returns>
    public Task<PrepareResult> Prepare(ChangeRequest request, BitLockerChoice? choice = null)
    {
        return Pipeline.PrepareAsync(request, choice, CancellationToken.None);
    }

    /// <summary>Runs the dry run, asserts that it made a plan, and commits it with <paramref name="choice"/>.</summary>
    /// <param name="request">The request.</param>
    /// <param name="choice">The confirmation's BitLocker choice.</param>
    /// <returns>The plan and the commit's result.</returns>
    public async Task<(ChangePlan Plan, TransactionResult Result)> PrepareAndCommit(ChangeRequest request, BitLockerChoice choice = BitLockerChoice.NotNeeded)
    {
        PrepareResult prepared = await Prepare(request);
        if (prepared.Plan is not ChangePlan plan)
        {
            Assert.Fail("the dry run was expected to pass: " + prepared.Sentence);
            throw new InvalidOperationException(prepared.Sentence);
        }

        TransactionResult result = await Pipeline.CommitAsync(plan, plan.Confirm(choice));
        return (plan, result);
    }

    /// <summary>Appends <paramref name="count"/> records of past writes to write-log.jsonl, each at <paramref name="at"/>.</summary>
    /// <param name="count">How many.</param>
    /// <param name="at">Their timestamp.</param>
    /// <param name="apiCalled">Whether each made the native call.</param>
    /// <param name="bootId">The boot identity each carries, or null for none.</param>
    public void AddPastWrites(int count, DateTimeOffset at, bool apiCalled = true, uint? bootId = null)
    {
        for (int i = 0; i < count; i++)
        {
            WriteRecord record = new()
            {
                TimestampUtc = WriteRecord.FormatTimestamp(at),
                TransactionId = "past-" + i.ToString(CultureInfo.InvariantCulture),
                ApiCalled = apiCalled,
                BootId = bootId,
                Outcome = apiCalled ? WriteRecordOutcome.Verified : WriteRecordOutcome.Blocked,
            };
            Assert.Null(Records.Append(record));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        TestFolders.DeleteQuietly(DataDirectory);
    }
}
