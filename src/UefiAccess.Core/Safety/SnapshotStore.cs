// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Text;
using System.Text.Json;
using UefiAccess.Core.Baseline;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Privileges;
using UefiAccess.Core.Variables;

namespace UefiAccess.Core.Safety;

/// <summary>The firmware block of snapshot.json.</summary>
/// <param name="FirmwareType">The firmware type name.</param>
/// <param name="PrivilegeState">The privilege state name.</param>
/// <param name="EnumerationMethod">The enumeration method name of the S3 enumeration.</param>
/// <param name="VariableCount">The number of variables in <c>before\</c>.</param>
public sealed record SnapshotFirmware(string FirmwareType, string PrivilegeState, string EnumerationMethod, int VariableCount);

/// <summary>The target block of snapshot.json.</summary>
/// <param name="Key">The variable key ("guid:name").</param>
/// <param name="Name">The variable name.</param>
/// <param name="Guid">The vendor GUID in lowercase "D" format.</param>
public sealed record SnapshotTarget(string Key, string Name, string Guid);

/// <summary>The value of the target before the change.</summary>
/// <param name="Present">False when the variable was absent (an unset BootNext).</param>
/// <param name="Attributes">The raw attributes; 0 when absent.</param>
/// <param name="AttributeNames">The short flag text, such as "NV BS RT"; empty when absent.</param>
/// <param name="Size">The size in bytes; 0 when absent.</param>
/// <param name="Hex">The bytes as lowercase hex with no separators; empty when absent.</param>
/// <param name="Sha256">The lowercase SHA-256 of the bytes (of no bytes when absent), the same value index.json carries.</param>
/// <param name="File">The raw file inside the folder, "before/vars/&lt;guid&gt;_&lt;name&gt;.bin", or null when absent.</param>
/// <param name="Entries">The entries the bytes encode.</param>
public sealed record SnapshotBefore(
    bool Present,
    uint Attributes,
    string AttributeNames,
    int Size,
    string Hex,
    string Sha256,
    string? File,
    IReadOnlyList<EntryLine> Entries);

/// <summary>The proposed value of the target.</summary>
/// <param name="Attributes">The attributes passed (7).</param>
/// <param name="Size">The size in bytes; 0 for a clear.</param>
/// <param name="Hex">The bytes as lowercase hex; empty for a clear.</param>
/// <param name="Sha256">The lowercase SHA-256 of the bytes.</param>
/// <param name="Entries">The entries the bytes encode; empty for a clear.</param>
public sealed record SnapshotProposed(uint Attributes, int Size, string Hex, string Sha256, IReadOnlyList<EntryLine> Entries);

/// <summary>The value read back at stage S16; null in snapshot.json until then.</summary>
/// <param name="Present">Whether the variable exists after the call.</param>
/// <param name="Attributes">The attributes read, or null when nothing was read.</param>
/// <param name="Size">The size read, or null.</param>
/// <param name="Hex">The bytes read as lowercase hex, or null.</param>
/// <param name="Sha256">The SHA-256 of the bytes read, or null.</param>
/// <param name="File">The raw file inside the folder, "after/&lt;guid&gt;_&lt;name&gt;.bin", or null when nothing was read.</param>
/// <param name="ReadStatus">The read status name.</param>
/// <param name="MatchesProposed">Whether the value read equals the proposed value.</param>
/// <param name="MatchesBefore">Whether the value read equals the previous value.</param>
public sealed record SnapshotAfter(
    bool Present,
    uint? Attributes,
    int? Size,
    string? Hex,
    string? Sha256,
    string? File,
    string ReadStatus,
    bool MatchesProposed,
    bool MatchesBefore);

/// <summary>One Boot#### of the S3 enumeration, as the context block lists it.</summary>
/// <param name="Key">The variable key.</param>
/// <param name="Size">The size in bytes.</param>
/// <param name="Sha256">The lowercase SHA-256 of the bytes.</param>
/// <param name="Description">The load option's description.</param>
/// <param name="Active">LOAD_OPTION_ACTIVE.</param>
/// <param name="Hidden">LOAD_OPTION_HIDDEN.</param>
/// <param name="Category">"Boot", "App" or the hexadecimal category.</param>
/// <param name="DevicePath">The device path text.</param>
public sealed record SnapshotLoadOption(string Key, int Size, string Sha256, string Description, bool Active, bool Hidden, string Category, string DevicePath);

/// <summary>The boot context of the S3 enumeration.</summary>
/// <param name="BootCurrent">The entry BootCurrent names, or null when absent or malformed.</param>
/// <param name="BootNext">The entry BootNext names, or null when absent or malformed.</param>
/// <param name="Timeout">The Timeout variable in seconds, or null.</param>
/// <param name="LoadOptions">Every Boot#### of the enumeration, sorted by key.</param>
public sealed record SnapshotContext(string? BootCurrent, string? BootNext, int? Timeout, IReadOnlyList<SnapshotLoadOption> LoadOptions);

/// <summary>The baseline comparison of stage S7.</summary>
/// <param name="Folder">The newest baseline folder, or null when none exists.</param>
/// <param name="TargetSha256">The target's SHA-256 in that baseline, or null when absent there.</param>
/// <param name="Matches">Whether the value before the change equals the baseline's; null when not compared.</param>
public sealed record SnapshotBaseline(string? Folder, string? TargetSha256, bool? Matches);

/// <summary>The BitLocker block of snapshot.json. Never key material.</summary>
/// <param name="State">The assessed state.</param>
/// <param name="ProtectionStatus">The volume's ProtectionStatus, or null when no volume was read.</param>
/// <param name="ConversionStatus">The volume's ConversionStatus, or null.</param>
/// <param name="ProtectorTypes">The protector type names.</param>
/// <param name="PcrProfileText">The PCR profile text, or null.</param>
/// <param name="SuspendOffered">Whether the one-restart suspend was offered.</param>
/// <param name="SuspendChosen">Whether it was chosen with the confirmation.</param>
/// <param name="DisableCount">The count passed to the suspend (always 1), or null when none was attempted.</param>
/// <param name="ReturnValue">"0", the HRESULT in hex, "timeout", or null when none was attempted.</param>
/// <param name="ProtectionStatusAfterSuspend">The ProtectionStatus re-read after the suspend, or null.</param>
public sealed record SnapshotBitLocker(
    BitLockerState State,
    int? ProtectionStatus,
    int? ConversionStatus,
    IReadOnlyList<string> ProtectorTypes,
    string? PcrProfileText,
    bool SuspendOffered,
    bool SuspendChosen,
    uint? DisableCount,
    string? ReturnValue,
    int? ProtectionStatusAfterSuspend);

/// <summary>The Secure Boot block of snapshot.json.</summary>
/// <param name="Enabled">The enabled state, or null when not read.</param>
/// <param name="SetupMode">The SetupMode variable, or null when not read.</param>
/// <param name="SourcesAgree">Whether the available sources agreed.</param>
public sealed record SnapshotSecureBoot(bool? Enabled, bool? SetupMode, bool SourcesAgree);

/// <summary>
/// The snapshot.json document: PascalCase, fixed property order, enums
/// as strings, like index.json. <see cref="After"/> is null from stage S12 until S16.
/// </summary>
/// <param name="SchemaVersion">1.</param>
/// <param name="TransactionId">The folder name.</param>
/// <param name="ToolVersion">The tool version.</param>
/// <param name="TimestampUtc">The transaction's start, UTC.</param>
/// <param name="TimestampLocal">The transaction's start with its offset.</param>
/// <param name="Host">"cli" or "app".</param>
/// <param name="Operation">"Reorder", "SetBootNext" or "ClearBootNext".</param>
/// <param name="IsRestore">Whether the request was a restore.</param>
/// <param name="RestoredFrom">The snapshot folder a restore was derived from, or null.</param>
/// <param name="Identity">The machine identity (includes the service tag; the folder stays on the unit).</param>
/// <param name="Firmware">The firmware block.</param>
/// <param name="Target">The target block.</param>
/// <param name="Before">The value before the change.</param>
/// <param name="Proposed">The proposed value.</param>
/// <param name="After">The value read back, or null until S16.</param>
/// <param name="Context">The boot context.</param>
/// <param name="Baseline">The baseline comparison.</param>
/// <param name="BitLocker">The BitLocker block.</param>
/// <param name="SecureBoot">The Secure Boot block.</param>
/// <param name="PlanHash">The plan's hash.</param>
public sealed record SnapshotDocument(
    int SchemaVersion,
    string TransactionId,
    string ToolVersion,
    string TimestampUtc,
    string TimestampLocal,
    string Host,
    string Operation,
    bool IsRestore,
    string? RestoredFrom,
    MachineIdentityResult? Identity,
    SnapshotFirmware Firmware,
    SnapshotTarget Target,
    SnapshotBefore Before,
    SnapshotProposed Proposed,
    SnapshotAfter? After,
    SnapshotContext Context,
    SnapshotBaseline Baseline,
    SnapshotBitLocker BitLocker,
    SnapshotSecureBoot SecureBoot,
    string PlanHash);

/// <summary>Everything <see cref="SnapshotStore.Create"/> needs to write one snapshot folder at stage S12.</summary>
/// <param name="TransactionId">The folder name, a plain name without separators (the plan's snapshot folder name).</param>
/// <param name="Host">"cli" or "app".</param>
/// <param name="Operation">"Reorder", "SetBootNext" or "ClearBootNext".</param>
/// <param name="IsRestore">Whether the request was a restore.</param>
/// <param name="RestoredFrom">The snapshot folder a restore was derived from, or null.</param>
/// <param name="Timestamp">The transaction's start.</param>
/// <param name="ToolVersion">The tool version.</param>
/// <param name="Identity">The machine identity, or null when not read.</param>
/// <param name="FirmwareType">The firmware type.</param>
/// <param name="Privilege">The privilege state.</param>
/// <param name="EnumerationMethod">The S3 enumeration method.</param>
/// <param name="Variables">The S3 enumeration with the direct reads merged in; exported whole into <c>before\</c>.</param>
/// <param name="Target">The target block.</param>
/// <param name="Before">The value before the change; <see cref="SnapshotBefore.File"/> is filled by the store.</param>
/// <param name="Proposed">The proposed value.</param>
/// <param name="Context">The boot context.</param>
/// <param name="Baseline">The baseline comparison.</param>
/// <param name="BitLocker">The BitLocker block.</param>
/// <param name="SecureBoot">The Secure Boot block.</param>
/// <param name="PlanHash">The plan's hash.</param>
/// <param name="Decoded">The object serialised to <c>before\decoded.json</c>.</param>
/// <param name="Platform">The object serialised to <c>before\platform.json</c>.</param>
/// <param name="LogText">The pipeline's log lines so far, written at the start of <c>before\log.txt</c>.</param>
/// <param name="Record">The record written as record.json with outcome Pending.</param>
public sealed record SnapshotRequest(
    string TransactionId,
    string Host,
    string Operation,
    bool IsRestore,
    string? RestoredFrom,
    DateTimeOffset Timestamp,
    string ToolVersion,
    MachineIdentityResult? Identity,
    FirmwareType FirmwareType,
    PrivilegeState Privilege,
    EnumerationMethod EnumerationMethod,
    IReadOnlyList<FirmwareVariable> Variables,
    SnapshotTarget Target,
    SnapshotBefore Before,
    SnapshotProposed Proposed,
    SnapshotContext Context,
    SnapshotBaseline Baseline,
    SnapshotBitLocker BitLocker,
    SnapshotSecureBoot SecureBoot,
    string PlanHash,
    object? Decoded,
    object? Platform,
    string? LogText,
    WriteRecord Record);

/// <summary>What <see cref="SnapshotStore.Create"/> produced.</summary>
/// <param name="Folder">The full path of the folder, or null when it was not created.</param>
/// <param name="Document">The snapshot.json that was written, or null on failure.</param>
/// <param name="Error">Null on success; otherwise the sentence that says why the snapshot was not written.</param>
public sealed record SnapshotCreateResult(string? Folder, SnapshotDocument? Document, string? Error)
{
    /// <summary>True when the folder, <c>before\</c>, snapshot.json and record.json were written.</summary>
    public bool Succeeded => Error is null && Folder is not null && Document is not null;
}

/// <summary>What <see cref="SnapshotStore.Read"/> produced.</summary>
/// <param name="Folder">The full path of the folder that was read.</param>
/// <param name="Document">The parsed snapshot.json, or null on failure.</param>
/// <param name="Record">The parsed record.json, or null when it is missing or not a record.</param>
/// <param name="Before">The <c>before\</c> folder read through <see cref="BaselineReader"/>, or null on failure.</param>
/// <param name="TargetBefore">The target as <c>before\</c> holds it, or null when it was absent before the change.</param>
/// <param name="Error">Null on success; otherwise the sentence that says why the snapshot cannot be trusted.</param>
public sealed record SnapshotReadResult(
    string Folder,
    SnapshotDocument? Document,
    WriteRecord? Record,
    BaselineReadResult? Before,
    FirmwareVariable? TargetBefore,
    string? Error)
{
    /// <summary>True when snapshot.json parsed, every entry of <c>before\</c> matched its index and the target agrees with the document.</summary>
    public bool Succeeded => Error is null && Document is not null && Before is not null;
}

/// <summary>One snapshot folder as <see cref="SnapshotStore.List"/> and <see cref="SnapshotStore.Newest"/> see it, from its record.json.</summary>
/// <param name="Folder">The full path of the folder.</param>
/// <param name="Name">The folder name (the transaction id).</param>
/// <param name="Operation">The record's operation, or null when record.json is missing or not a record.</param>
/// <param name="TimestampUtc">The record's timestamp, or null.</param>
/// <param name="TargetKey">The record's variable key, or null.</param>
/// <param name="Outcome">The record's outcome, or null.</param>
public sealed record SnapshotSummary(string Folder, string Name, string? Operation, string? TimestampUtc, string? TargetKey, WriteRecordOutcome? Outcome);

/// <summary>A restore resolved to one of the three operations, or refused.</summary>
/// <param name="Request">The reorder, one-time boot or clear request the restore became, carrying the folder in <see cref="ChangeRequest.RestoredFrom"/>; null when refused.</param>
/// <param name="Refusal">Null when resolved; otherwise the sentence that says why the snapshot cannot be restored.</param>
public sealed record RestoreDerivation(ChangeRequest? Request, string? Refusal);

/// <summary>
/// The snapshot store of task 3.5: one folder per transaction under the <c>snapshots</c> data
/// root, named by the transaction id, holding <c>snapshot.json</c>, <c>before\</c> (a complete
/// baseline export of the S3 enumeration, readable by <see cref="BaselineReader"/> and by the
/// application's Open baseline folder), <c>after\</c> (the target's bytes read back at S16),
/// <c>record.json</c> and <c>log.txt</c>. The root must lie under the data folder and is
/// prepared through <see cref="Logging.PrepareDataFolderFor(string)"/>, as the baseline root is.
/// Every file is flushed to the disk as it is written, and a file rewritten after the call
/// (snapshot.json at S16, record.json and log.txt at S17) replaces the old one only once it is
/// whole, so a crash or a full disk never leaves the S12 files empty or cut short.
/// A folder is untrusted input when it is read back: links are refused, sizes are capped, and
/// the target's bytes in <c>before\</c> must match the document's hash. No public member throws.
/// </summary>
public sealed class SnapshotStore
{
    /// <summary>The folder name under the data root.</summary>
    public const string FolderName = "snapshots";

    /// <summary>The document name inside a snapshot folder.</summary>
    public const string SnapshotFileName = "snapshot.json";

    /// <summary>The record name inside a snapshot folder.</summary>
    public const string RecordFileName = "record.json";

    /// <summary>The log name inside a snapshot folder.</summary>
    public const string LogFileName = "log.txt";

    /// <summary>The sub-folder that holds the baseline export of the value before the change.</summary>
    public const string BeforeDirectoryName = "before";

    /// <summary>The sub-folder that holds the target's bytes read back after the call.</summary>
    public const string AfterDirectoryName = "after";

    /// <summary>The value of <see cref="SnapshotDocument.SchemaVersion"/>.</summary>
    public const int SchemaVersion = 1;

    /// <summary>The sentence when the root is not under the data folder; {0} the root, {1} the data folder.</summary>
    public const string OutsideDataFolderFormat = "The snapshot root {0} is outside the data folder {1}; nothing was created.";

    /// <summary>The sentence when a folder's path is a file; {0} the path.</summary>
    public const string FolderIsAFileFormat = "{0} is a file, not a folder; nothing was written there.";

    /// <summary>The sentence when a folder cannot be prepared; {0} the path, {1} the reason.</summary>
    public const string CannotPrepareFormat = "{0} cannot be prepared: {1}";

    /// <summary>The sentence when the transaction id is not a plain folder name; {0} the id.</summary>
    public const string BadTransactionIdFormat = "The snapshot name {0} is not a plain folder name; nothing was written.";

    /// <summary>The sentence when the transaction's folder already exists; {0} the folder.</summary>
    public const string FolderExistsFormat = "The snapshot folder {0} already exists; nothing was written to it.";

    /// <summary>The sentence when the before folder cannot be indexed or written; {0} the reason.</summary>
    public const string BeforeNotWrittenFormat = "The before folder of the snapshot could not be written: {0}";

    /// <summary>The sentence when snapshot.json cannot be serialised; {0} the reason.</summary>
    public const string CannotSerialiseFormat = "snapshot.json could not be serialised: {0}";

    /// <summary>The sentence when a file of the folder cannot be written; {0} the file, {1} the folder, {2} the reason.</summary>
    public const string CannotWriteFormat = "{0} could not be written in {1}: {2}";

    /// <summary>The sentence when a file cannot be written because its folder is missing; {0} the folder, {1} the file.</summary>
    public const string FolderMissingFormat = "The snapshot folder {0} does not exist, so {1} could not be written.";

    /// <summary>The sentence when no folder was given.</summary>
    public const string NoFolderGiven = "No snapshot folder was given.";

    /// <summary>The sentence when the folder to read does not exist; {0} the folder.</summary>
    public const string NoSuchFolderFormat = "The snapshot folder {0} does not exist.";

    /// <summary>The sentence when the folder to read is not one of the root's own folders; {0} the folder, {1} the root.</summary>
    public const string OutsideRootFormat = "The folder {0} is not a snapshot folder of {1}; only a folder the write pipeline created there is read.";

    /// <summary>The sentence when snapshot.json's target key is not the one its name and GUID make; {0} the folder, {1} the key, {2} the name, {3} the GUID.</summary>
    public const string TargetKeyMismatchFormat = "snapshot.json in {0} names the target {1}, which is not the key of {2} under {3}; nothing is read from it.";

    /// <summary>The sentence when record.json and snapshot.json name different variables; {0} the folder, {1} the record's key, {2} the snapshot's key.</summary>
    public const string RecordKeyMismatchFormat = "record.json in {0} is about {1}, but snapshot.json is about {2}; nothing is read from it.";

    /// <summary>The sentence when the folder has no snapshot.json; {0} the folder.</summary>
    public const string NoDocumentFormat = "The folder {0} has no snapshot.json; choose a snapshot folder the write pipeline created.";

    /// <summary>The sentence when a snapshot folder holds a link; {0} the link, {1} the folder.</summary>
    public const string LinkFormat = "{0} in {1} is a symbolic link or junction, which a snapshot folder must not contain.";

    /// <summary>The sentence when snapshot.json is too large; {0} the folder, {1} the limit in bytes.</summary>
    public const string DocumentTooLargeFormat = "snapshot.json in {0} is larger than the {1}-byte limit, so nothing was read.";

    /// <summary>The sentence when snapshot.json does not parse; {0} the folder, {1} the reason.</summary>
    public const string NotADocumentFormat = "snapshot.json in {0} is not a snapshot document: {1}";

    /// <summary>The reason when the document lacks its target or before block.</summary>
    public const string MissingBlocksReason = "it lacks the target or the before block";

    /// <summary>The sentence when the schema version is not this build's; {0} the folder, {1} the version found, {2} the version read.</summary>
    public const string SchemaVersionFormat = "snapshot.json in {0} has schema version {1}; this build reads version {2}.";

    /// <summary>The sentence when the before folder cannot be read; {0} the reason.</summary>
    public const string BeforeUnreadableFormat = "the before folder could not be read: {0}";

    /// <summary>The sentence when an entry of the before folder does not match its index; {0} the first mismatch.</summary>
    public const string BeforeMismatchFormat = "the before folder does not match its index: {0}";

    /// <summary>The sentence when the document and the before folder disagree on whether the target existed; {0} the variable name.</summary>
    public const string PresenceMismatchFormat = "snapshot.json and the before folder disagree on whether {0} existed before the change.";

    /// <summary>The sentence when the target's hash in the before folder is not the document's; {0} the variable name.</summary>
    public const string HashMismatchFormat = "the SHA-256 of {0} in the before folder does not match snapshot.json.";

    /// <summary>The sentence when reading failed unexpectedly; {0} the reason.</summary>
    public const string ReadFailedFormat = "Reading the snapshot failed: {0}";

    /// <summary>The sentence when a snapshot names a variable the whitelist does not hold; {0} the folder, {1} the variable key.</summary>
    public const string NotWhitelistedFormat = "The snapshot {0} records {1}, which this application cannot change; nothing was restored.";

    /// <summary>The sentence when a snapshot cannot be read for a restore; {0} the folder, {1} the reason.</summary>
    public const string UnreadableFormat = "The snapshot {0} could not be read: {1}";

    /// <summary>The sentence when a snapshot's value cannot be turned into a request; {0} the folder, {1} the variable name, {2} the reason.</summary>
    public const string MalformedFormat = "The snapshot {0} records a {1} that cannot be restored: {2}";

    /// <summary>The restore reason when the snapshot holds no BootOrder value.</summary>
    public const string NoBootOrderReason = "the snapshot records no BootOrder value";

    /// <summary>The restore reason when BootOrder's bytes are not a list of entries; {0} the byte count.</summary>
    public const string NotAListReasonFormat = "{0} bytes is not a list of entries";

    /// <summary>The restore reason when BootNext's bytes are not one entry; {0} the byte count.</summary>
    public const string NotOneEntryReasonFormat = "{0} bytes is not one entry";

    /// <summary>The reason, inside <see cref="BeforeNotWrittenFormat"/>, when a file of <c>before\</c> could not be flushed to the disk; {0} the file, {1} the reason.</summary>
    public const string NotFlushedFormat = "{0} could not be flushed to the disk: {1}";

    /// <summary>The suffix of the temporary file a snapshot file is written to before it replaces the file (see <see cref="WriteDurably"/>).</summary>
    public const string TemporaryFileSuffix = ".tmp";

    /// <summary>The reason when no other reason is known.</summary>
    public const string UnknownReason = "unknown reason";

    private const string UtcTimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";
    private const string LocalTimestampFormat = "yyyy-MM-dd'T'HH:mm:sszzz";
    private const char RelativeSeparator = '/';

    /// <summary>The outcomes a folder may carry and still be the one Restore previous value offers: the value may have changed.</summary>
    private static readonly WriteRecordOutcome[] RestorableOutcomes =
    {
        WriteRecordOutcome.Verified,
        WriteRecordOutcome.Unverified,
        WriteRecordOutcome.ChangedDespiteError,
        WriteRecordOutcome.Pending,
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _dataDirectory;
    private readonly Func<string, string?> _prepareFolder;

    /// <summary>Creates the store at <see cref="DefaultRootDirectory"/>.</summary>
    public SnapshotStore()
        : this(DefaultRootDirectory)
    {
    }

    /// <summary>Creates the store at an explicit root (the CLI's <c>--snapshot-root</c>), which must still lie under the data folder.</summary>
    /// <param name="rootDirectory">The root folder.</param>
    public SnapshotStore(string rootDirectory)
        : this(rootDirectory, Logging.DataDirectory, Logging.PrepareDataFolderFor)
    {
    }

    /// <summary>Creates the store with an explicit data folder and a replacement for <see cref="Logging.PrepareDataFolderFor(string)"/>, for tests.</summary>
    /// <param name="rootDirectory">The root folder.</param>
    /// <param name="dataDirectory">The data folder the root must lie under.</param>
    /// <param name="prepareFolder">Called with each folder before it is created or written; returns null to go ahead or the refusal.</param>
    internal SnapshotStore(string rootDirectory, string dataDirectory, Func<string, string?> prepareFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(prepareFolder);
        RootDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        _dataDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory));
        _prepareFolder = prepareFolder;
    }

    /// <summary>The default root: <c>snapshots</c> directly under <see cref="Logging.DataDirectory"/>.</summary>
    public static string DefaultRootDirectory => Path.Combine(Logging.DataDirectory, FolderName);

    /// <summary>The full path of the root.</summary>
    public string RootDirectory { get; }

    /// <summary>
    /// Prepares the root: it must lie under the data folder, pass the elevated-write check, and
    /// be a folder (created when missing). Nothing is created for a root outside the data folder.
    /// </summary>
    /// <returns>Null when the root may be written; otherwise the refusal.</returns>
    public string? PrepareRoot()
    {
        if (!IsUnderDataDirectory(RootDirectory))
        {
            return CheckSentences.Format(OutsideDataFolderFormat, RootDirectory, _dataDirectory);
        }

        return PrepareFolder(RootDirectory);
    }

    /// <summary>
    /// Writes the folder of one transaction (stage S12): the folder, <c>before\</c> through
    /// <see cref="BaselineExporter"/>, then snapshot.json and record.json with outcome Pending.
    /// Every file is flushed to the disk before the result reports success, so a hang, a reset or
    /// a power loss during or just after the call finds them whole. An existing folder of the same
    /// name is refused, since the name was announced before the folder existed and must stay the
    /// transaction's.
    /// </summary>
    /// <param name="request">What to write.</param>
    /// <returns>The folder and the document, or the error.</returns>
    public SnapshotCreateResult Create(SnapshotRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string? rootRefusal = PrepareRoot();
        if (rootRefusal is not null)
        {
            return new SnapshotCreateResult(null, null, rootRefusal);
        }

        if (!IsPlainFolderName(request.TransactionId))
        {
            return new SnapshotCreateResult(null, null, CheckSentences.Format(BadTransactionIdFormat, request.TransactionId ?? string.Empty));
        }

        string folder = Path.Combine(RootDirectory, request.TransactionId);
        try
        {
            if (Directory.Exists(folder) || File.Exists(folder))
            {
                return new SnapshotCreateResult(null, null, CheckSentences.Format(FolderExistsFormat, folder));
            }
        }
        catch (Exception ex)
        {
            return new SnapshotCreateResult(null, null, CheckSentences.Format(CannotPrepareFormat, folder, ex.Message));
        }

        string? folderRefusal = PrepareFolder(folder);
        if (folderRefusal is not null)
        {
            return new SnapshotCreateResult(null, null, folderRefusal);
        }

        BaselineRequest export = new(
            folder,
            BeforeDirectoryName,
            request.Variables,
            request.FirmwareType,
            request.Privilege,
            request.Identity,
            request.Decoded,
            request.Platform,
            request.LogText,
            request.ToolVersion,
            request.Timestamp);

        string? targetFile = null;
        try
        {
            BaselineIndex index = BaselineExporter.BuildIndex(export);
            BaselineEntry? entry = index.Variables.FirstOrDefault(candidate => string.Equals(candidate.Key, request.Target.Key, StringComparison.Ordinal));
            targetFile = entry is null ? null : BeforeDirectoryName + RelativeSeparator + entry.File;
        }
        catch (Exception ex)
        {
            return new SnapshotCreateResult(folder, null, CheckSentences.Format(BeforeNotWrittenFormat, ex.Message));
        }

        BaselineExportResult exported = new BaselineExporter(_prepareFolder).Export(export);
        if (exported.Errors.Count > 0)
        {
            return new SnapshotCreateResult(folder, null, CheckSentences.Format(BeforeNotWrittenFormat, string.Join("; ", exported.Errors)));
        }

        // before\ is the only record of the value before the write when the call hangs or the power
        // fails: every file of it is on the disk before the snapshot is reported written, so a file
        // that cannot be flushed blocks the write at S12 like one that could not be written.
        string? unflushed = FlushToDisk(exported.Directory, exported.FilesWritten);
        if (unflushed is not null)
        {
            return new SnapshotCreateResult(folder, null, CheckSentences.Format(BeforeNotWrittenFormat, unflushed));
        }

        SnapshotDocument document = new(
            SchemaVersion,
            request.TransactionId,
            request.ToolVersion,
            request.Timestamp.ToUniversalTime().ToString(UtcTimestampFormat, CultureInfo.InvariantCulture),
            request.Timestamp.ToString(LocalTimestampFormat, CultureInfo.InvariantCulture),
            request.Host,
            request.Operation,
            request.IsRestore,
            request.RestoredFrom,
            request.Identity,
            new SnapshotFirmware(request.FirmwareType.ToString(), request.Privilege.ToString(), request.EnumerationMethod.ToString(), exported.VariableCount),
            request.Target,
            request.Before with { File = request.Before.Present ? targetFile : null },
            request.Proposed,
            null,
            request.Context,
            request.Baseline,
            request.BitLocker,
            request.SecureBoot,
            request.PlanHash);

        string? error = WriteDocument(folder, document) ?? WriteRecordFile(folder, request.Record);
        return error is null
            ? new SnapshotCreateResult(folder, document, null)
            : new SnapshotCreateResult(folder, null, error);
    }

    /// <summary>Writes or rewrites record.json in <paramref name="folder"/>.</summary>
    /// <param name="folder">The full path of the snapshot folder.</param>
    /// <param name="record">The record.</param>
    /// <returns>Null when written; otherwise the sentence that says why not.</returns>
    public string? WriteRecordFile(string folder, WriteRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        string json;
        try
        {
            json = record.ToJson();
        }
        catch (Exception ex)
        {
            return CheckSentences.Format(CannotWriteFormat, RecordFileName, folder, ex.Message);
        }

        return WriteText(folder, RecordFileName, json);
    }

    /// <summary>Writes or rewrites log.txt in <paramref name="folder"/>.</summary>
    /// <param name="folder">The full path of the snapshot folder.</param>
    /// <param name="text">The pipeline's log lines of this transaction.</param>
    /// <returns>Null when written; otherwise the sentence that says why not.</returns>
    public string? WriteLog(string folder, string text)
    {
        return WriteText(folder, LogFileName, text ?? string.Empty);
    }

    /// <summary>
    /// Records what stage S16 read back: the target's bytes into <c>after\&lt;guid&gt;_&lt;name&gt;.bin</c>
    /// (no file after a clear or a failed read) and snapshot.json rewritten with
    /// <paramref name="document"/>, whose <see cref="SnapshotDocument.After"/> gets the file name.
    /// The rewrite replaces the S12 snapshot.json only once the new one is whole on the disk; when
    /// it fails, the S12 document stays as it was and can still be restored from.
    /// </summary>
    /// <param name="folder">The full path of the snapshot folder.</param>
    /// <param name="document">The document with its After and BitLocker blocks filled.</param>
    /// <param name="readBack">The variable read at S16, or null when it was absent or unreadable.</param>
    /// <returns>Null when written; otherwise the sentence that says why not.</returns>
    public string? WriteAfter(string folder, SnapshotDocument document, FirmwareVariable? readBack)
    {
        ArgumentNullException.ThrowIfNull(document);
        string? file = null;
        if (readBack is not null)
        {
            string fileName = BaselineExporter.VariableFileStem(readBack.VendorGuid, readBack.Name) + BaselineExporter.VariableFileExtension;
            string relative = AfterDirectoryName + RelativeSeparator + fileName;
            string afterDirectory = Path.Combine(folder, AfterDirectoryName);
            string? refusal = PrepareFolder(afterDirectory);
            if (refusal is not null)
            {
                return refusal;
            }

            try
            {
                WriteDurably(Path.Combine(afterDirectory, fileName), readBack.Data ?? Array.Empty<byte>());
                file = relative;
            }
            catch (Exception ex)
            {
                return CheckSentences.Format(CannotWriteFormat, relative, folder, ex.Message);
            }
        }

        SnapshotDocument updated = document.After is null ? document : document with { After = document.After with { File = file } };
        return WriteDocument(folder, updated);
    }

    /// <summary>
    /// Reads a snapshot folder back and verifies it: the folder is one of the root's own (a direct
    /// child of <see cref="RootDirectory"/> and not a link), snapshot.json parses and carries this
    /// schema, its target key is the one its name and GUID make, record.json (when present) names
    /// the same variable, <c>before\</c> is a plain folder accepted by <see cref="BaselineReader"/>
    /// with every entry matching its index, and the target in <c>before\</c> agrees with the
    /// document (present or absent, and the same SHA-256). Links inside the folder are refused;
    /// snapshot.json is read up to <see cref="BaselineReader.MaximumIndexSize"/> bytes. So a restore
    /// never reads a folder a standard user could have written, nor another variable's bytes.
    /// Never throws.
    /// </summary>
    /// <param name="folder">The full path of the folder, or its name under the root.</param>
    /// <returns>The result.</returns>
    public SnapshotReadResult Read(string folder)
    {
        string directory = folder ?? string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                return Failure(directory, NoFolderGiven);
            }

            directory = ResolveFolder(folder);
            if (!string.Equals(Path.GetDirectoryName(directory), RootDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return Failure(directory, CheckSentences.Format(OutsideRootFormat, directory, RootDirectory));
            }

            if (!Directory.Exists(directory))
            {
                return Failure(directory, CheckSentences.Format(NoSuchFolderFormat, directory));
            }

            if (BaselineReader.TryFindLink(RootDirectory, directory, out string? folderLink))
            {
                return Failure(directory, CheckSentences.Format(LinkFormat, folderLink, RootDirectory));
            }

            string snapshotPath = Path.Combine(directory, SnapshotFileName);
            if (!File.Exists(snapshotPath))
            {
                return Failure(directory, CheckSentences.Format(NoDocumentFormat, directory));
            }

            string beforeDirectory = Path.Combine(directory, BeforeDirectoryName);
            if (BaselineReader.TryFindLink(directory, snapshotPath, out string? link) || BaselineReader.TryFindLink(directory, beforeDirectory, out link))
            {
                return Failure(directory, CheckSentences.Format(LinkFormat, link, directory));
            }

            string? json = ReadTextBounded(snapshotPath, BaselineReader.MaximumIndexSize);
            if (json is null)
            {
                return Failure(directory, CheckSentences.Format(DocumentTooLargeFormat, directory, BaselineReader.MaximumIndexSize));
            }

            SnapshotDocument? document;
            try
            {
                document = JsonSerializer.Deserialize<SnapshotDocument>(json, BaselineReader.ReadOptions);
            }
            catch (Exception ex)
            {
                return Failure(directory, CheckSentences.Format(NotADocumentFormat, directory, ex.Message));
            }

            if (document is null || document.Target is null || document.Before is null)
            {
                return Failure(directory, CheckSentences.Format(NotADocumentFormat, directory, MissingBlocksReason));
            }

            if (document.SchemaVersion != SchemaVersion)
            {
                return Failure(directory, CheckSentences.Format(SchemaVersionFormat, directory, document.SchemaVersion, SchemaVersion));
            }

            SnapshotTarget targetBlock = document.Target;
            if (!Guid.TryParse(targetBlock.Guid, out Guid targetGuid)
                || string.IsNullOrEmpty(targetBlock.Name)
                || !string.Equals(targetBlock.Key, FirmwareVariable.MakeKey(targetGuid, targetBlock.Name), StringComparison.Ordinal))
            {
                return Failure(directory, CheckSentences.Format(TargetKeyMismatchFormat, directory, targetBlock.Key, targetBlock.Name, targetBlock.Guid));
            }

            WriteRecord? record = TryReadRecord(directory);
            if (record is not null && !string.Equals(record.Variable?.Key, targetBlock.Key, StringComparison.Ordinal))
            {
                return Failure(directory, CheckSentences.Format(RecordKeyMismatchFormat, directory, record.Variable?.Key, targetBlock.Key));
            }

            BaselineReadResult before = BaselineReader.Read(beforeDirectory);
            if (before.Error is not null)
            {
                return Failure(directory, CheckSentences.Format(BeforeUnreadableFormat, before.Error));
            }

            if (before.Warnings.Count > 0)
            {
                return Failure(directory, CheckSentences.Format(BeforeMismatchFormat, before.Warnings[0]));
            }

            FirmwareVariable? target = before.Variables.FirstOrDefault(variable => string.Equals(variable.Key, document.Target.Key, StringComparison.Ordinal));
            if (document.Before.Present != (target is not null))
            {
                return Failure(directory, CheckSentences.Format(PresenceMismatchFormat, document.Target.Name));
            }

            if (target is not null && !string.Equals(VariableImage.Sha256Of(target.Data), document.Before.Sha256?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return Failure(directory, CheckSentences.Format(HashMismatchFormat, document.Target.Name));
            }

            return new SnapshotReadResult(directory, document, record, before, target, null);
        }
        catch (Exception ex)
        {
            return Failure(directory, CheckSentences.Format(ReadFailedFormat, ex.Message));
        }
    }

    /// <summary>
    /// The newest folder for <paramref name="key"/> whose record.json outcome is Verified,
    /// Unverified, ChangedDespiteError or Pending (the value may have changed; a Pending folder is
    /// a transaction interrupted between S12 and S17, and its <c>before\</c> is the only record of
    /// the value before it). Blocked, Cancelled and ApiRefused folders are skipped: nothing was
    /// written. Newest in the order of <see cref="List"/>: by the record's UTC time, then by folder
    /// name, so the hour repeated when daylight saving time ends does not put an older change
    /// first (the folder name is local time). A backward step of the clock itself can still do
    /// so; the dialog names the folder it offers. Never throws.
    /// </summary>
    /// <param name="key">The variable key ("guid:name").</param>
    /// <returns>The summary, or null when no such folder exists.</returns>
    public SnapshotSummary? Newest(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return List().FirstOrDefault(summary =>
            string.Equals(summary.TargetKey, key, StringComparison.OrdinalIgnoreCase)
            && summary.Outcome is WriteRecordOutcome outcome
            && Array.IndexOf(RestorableOutcomes, outcome) >= 0);
    }

    /// <summary>
    /// Every folder under the root, newest first, with what its record.json says: by the record's
    /// UTC timestamp, then by folder name (ordinal, descending); a folder whose record has no
    /// readable timestamp comes after every one that has, by name. Never throws.
    /// </summary>
    /// <returns>The summaries; empty when the root does not exist.</returns>
    public IReadOnlyList<SnapshotSummary> List()
    {
        List<(SnapshotSummary Summary, DateTimeOffset? At)> found = new();
        try
        {
            if (!Directory.Exists(RootDirectory))
            {
                return Array.Empty<SnapshotSummary>();
            }

            foreach (string folder in Directory.EnumerateDirectories(RootDirectory))
            {
                WriteRecord? record = TryReadRecord(folder);
                SnapshotSummary summary = new(
                    folder,
                    Path.GetFileName(folder),
                    record?.Operation,
                    record?.TimestampUtc,
                    record?.Variable?.Key,
                    record?.Outcome);
                found.Add((summary, record?.Timestamp));
            }
        }
        catch (Exception)
        {
            // A root that cannot be listed offers nothing to restore.
            return Array.Empty<SnapshotSummary>();
        }

        return found
            .OrderByDescending(item => item.At.HasValue)
            .ThenByDescending(item => item.At ?? DateTimeOffset.MinValue)
            .ThenByDescending(item => item.Summary.Name, StringComparer.Ordinal)
            .Select(item => item.Summary)
            .ToArray();
    }

    /// <summary>
    /// Resolves a restore (stage S0): the snapshot's target must be on the whitelist; a BootOrder
    /// snapshot becomes a reorder to the recorded entries, a BootNext snapshot with a value
    /// becomes a one-time boot to that entry, and one that recorded "not set" becomes a clear.
    /// The derived request carries the folder in <see cref="ChangeRequest.RestoredFrom"/>, runs
    /// every stage against the current firmware and makes its own snapshot.
    /// </summary>
    /// <param name="read">What <see cref="Read"/> returned.</param>
    /// <param name="host">"cli" or "app", carried by the derived request.</param>
    /// <returns>The derived request, or the refusal.</returns>
    public static RestoreDerivation DeriveRestore(SnapshotReadResult read, string host)
    {
        ArgumentNullException.ThrowIfNull(read);
        if (!read.Succeeded || read.Document is null)
        {
            return new RestoreDerivation(null, CheckSentences.Format(UnreadableFormat, read.Folder, read.Error ?? UnknownReason));
        }

        SnapshotTarget target = read.Document.Target;
        if (!Guid.TryParse(target.Guid, out Guid guid) || !Whitelist.TryDescribe(target.Name ?? string.Empty, guid, out WhitelistEntry? entry))
        {
            return new RestoreDerivation(null, CheckSentences.Format(NotWhitelistedFormat, read.Folder, target.Key ?? target.Name ?? string.Empty));
        }

        FirmwareVariable? before = read.TargetBefore;
        if (entry.Kind == WritableKind.ReorderOnly)
        {
            if (before is null)
            {
                return new RestoreDerivation(null, CheckSentences.Format(MalformedFormat, read.Folder, entry.Name, NoBootOrderReason));
            }

            DecodeResult order = BootListCodec.Decode(before.Data);
            if (order.HasOddByteCount || order.IsEmpty)
            {
                return new RestoreDerivation(null, CheckSentences.Format(MalformedFormat, read.Folder, entry.Name, CheckSentences.Format(NotAListReasonFormat, order.ByteCount)));
            }

            return new RestoreDerivation(new ReorderRequest(host, order.Entries) { RestoredFrom = read.Folder }, null);
        }

        if (before is null)
        {
            return new RestoreDerivation(new ClearBootNextRequest(host) { RestoredFrom = read.Folder }, null);
        }

        DecodeResult next = BootListCodec.Decode(before.Data);
        if (next.ByteCount != BootListCodec.EntrySize)
        {
            return new RestoreDerivation(null, CheckSentences.Format(MalformedFormat, read.Folder, entry.Name, CheckSentences.Format(NotOneEntryReasonFormat, next.ByteCount)));
        }

        return new RestoreDerivation(new BootNextRequest(host, next.Entries[0]) { RestoredFrom = read.Folder }, null);
    }

    /// <summary>The full path of a folder given by path or by name under the root.</summary>
    /// <param name="folderOrName">A full path, or a folder name such as "20260930-141502-reorder".</param>
    /// <returns>The full path.</returns>
    public string ResolveFolder(string folderOrName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderOrName);
        return Path.IsPathRooted(folderOrName)
            ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderOrName))
            : Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(RootDirectory, folderOrName)));
    }

    /// <summary>True for a name the file system takes as one plain folder name: not empty, not "." or "..", and only [A-Za-z0-9_.-].</summary>
    private static bool IsPlainFolderName(string? name)
    {
        return !string.IsNullOrEmpty(name)
            && name != "."
            && name != ".."
            && string.Equals(BaselineExporter.SanitizeFileName(name), name, StringComparison.Ordinal);
    }

    private bool IsUnderDataDirectory(string folder)
    {
        return string.Equals(folder, _dataDirectory, StringComparison.OrdinalIgnoreCase)
            || folder.StartsWith(_dataDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private string? PrepareFolder(string folder)
    {
        try
        {
            string? refusal = _prepareFolder(folder);
            if (refusal is not null)
            {
                return refusal;
            }

            if (File.Exists(folder))
            {
                return CheckSentences.Format(FolderIsAFileFormat, folder);
            }

            Directory.CreateDirectory(folder);
            return null;
        }
        catch (Exception ex)
        {
            return CheckSentences.Format(CannotPrepareFormat, folder, ex.Message);
        }
    }

    private static string? WriteDocument(string folder, SnapshotDocument document)
    {
        string json;
        try
        {
            json = JsonSerializer.Serialize(document, BaselineExporter.JsonOptions);
        }
        catch (Exception ex)
        {
            return CheckSentences.Format(CannotSerialiseFormat, ex.Message);
        }

        return WriteText(folder, SnapshotFileName, json);
    }

    private static string? WriteText(string folder, string fileName, string text)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                return CheckSentences.Format(FolderMissingFormat, folder ?? string.Empty, fileName);
            }

            WriteDurably(Path.Combine(folder, fileName), Utf8NoBom.GetBytes(text));
            return null;
        }
        catch (Exception ex)
        {
            return CheckSentences.Format(CannotWriteFormat, fileName, folder, ex.Message);
        }
    }

    /// <summary>
    /// Writes a file of the snapshot folder so that a crash, a full disk or a failure at any point
    /// leaves either the file as it was or the new file whole, never an empty or partial one: the
    /// bytes go to <c>&lt;file&gt;.tmp</c> beside it, are flushed to the disk, and the temporary file
    /// then replaces the file by a rename (a journaled file-system operation). On a failure the
    /// temporary file is removed and the file is left alone, and the exception is passed on.
    /// Temporary files never go into <c>before\</c>, which the reader checks against its index.
    /// </summary>
    /// <param name="path">The full path of the file.</param>
    /// <param name="bytes">Its new content.</param>
    internal static void WriteDurably(string path, byte[] bytes)
    {
        string temporary = path + TemporaryFileSuffix;
        try
        {
            using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception)
        {
            DeleteQuietly(temporary);
            throw;
        }
    }

    /// <summary>
    /// Flushes files already written to the disk (FlushFileBuffers, through a handle opened for
    /// writing, which flushes whatever handle wrote them); nothing in them changes.
    /// </summary>
    /// <param name="directory">The folder the paths are relative to.</param>
    /// <param name="relativePaths">The files, relative to <paramref name="directory"/>, with "/" or "\" separators.</param>
    /// <returns>Null when every file is on the disk; otherwise which one is not, and why.</returns>
    private static string? FlushToDisk(string directory, IEnumerable<string> relativePaths)
    {
        foreach (string relative in relativePaths)
        {
            try
            {
                string path = Path.Combine(directory, relative.Replace(RelativeSeparator, Path.DirectorySeparatorChar));
                using FileStream stream = new(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                stream.Flush(flushToDisk: true);
            }
            catch (Exception ex)
            {
                return CheckSentences.Format(NotFlushedFormat, relative, ex.Message);
            }
        }

        return null;
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // The temporary file stays; nothing reads it, and the next write of the same file replaces it.
        }
    }

    private static WriteRecord? TryReadRecord(string folder)
    {
        try
        {
            string recordPath = Path.Combine(folder, RecordFileName);
            if (!File.Exists(recordPath) || BaselineReader.TryFindLink(folder, recordPath, out _))
            {
                return null;
            }

            return WriteRecord.TryParse(ReadTextBounded(recordPath, BaselineReader.MaximumIndexSize));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a text file as UTF-8, or returns null when it is larger than <paramref name="limit"/>
    /// bytes or grew while it was read, so no more than the length checked first (and one byte to
    /// see the growth) is ever read, through the baseline reader's own bounded read.
    /// </summary>
    private static string? ReadTextBounded(string path, int limit)
    {
        long length = new FileInfo(path).Length;
        if (length > limit)
        {
            return null;
        }

        byte[] bytes = BaselineReader.ReadAtMost(path, (int)length + 1);
        if (bytes.Length > length)
        {
            return null;
        }

        using MemoryStream memory = new(bytes, writable: false);
        using StreamReader reader = new(memory, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static SnapshotReadResult Failure(string directory, string error)
    {
        return new SnapshotReadResult(directory, null, null, null, null, error);
    }
}
