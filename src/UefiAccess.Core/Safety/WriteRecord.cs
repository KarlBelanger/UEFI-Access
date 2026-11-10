// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Core.Variables;

namespace UefiAccess.Core.Safety;

/// <summary>
/// The outcome a record carries. It is wider than the transaction outcomes: a dry run that
/// passed every check is <see cref="Prepared"/>, and the record.json in a snapshot folder is
/// <see cref="Pending"/> from stage S12 until S17, so a folder left by a killed process or a
/// power loss is recognisable as a transaction that was interrupted between them. Stage S12 also
/// appends a <see cref="Pending"/> line to write-log.jsonl before the folder, the suspend and the
/// call (the intent of the write): the write budget counts it as a write until a later line of
/// the same transaction says what happened.
/// </summary>
public enum WriteRecordOutcome
{
    /// <summary>Stages S1 to S10 passed; nothing was written and no folder exists (a dry run, or the plan before its confirmation).</summary>
    Prepared,

    /// <summary>The transaction reached S12 and has not reached S17: the record.json of its snapshot folder, and its intent line in write-log.jsonl.</summary>
    Pending,

    /// <summary>A check refused the change before any call; nothing was written.</summary>
    Blocked,

    /// <summary>The person cancelled the checks or the confirmation; nothing was written.</summary>
    Cancelled,

    /// <summary>The call succeeded and the verification read shows the proposed value.</summary>
    Verified,

    /// <summary>The call failed and the verification read shows the previous value.</summary>
    ApiRefused,

    /// <summary>The value read back is not what the call's result implies, or could not be read.</summary>
    Unverified,

    /// <summary>The call failed but the verification read shows the proposed value.</summary>
    ChangedDespiteError,
}

/// <summary>The variable a record concerns.</summary>
/// <param name="Key">The variable key ("guid:name", the GUID in lowercase "D" format).</param>
/// <param name="Name">The variable name.</param>
/// <param name="Guid">The vendor GUID in lowercase "D" format.</param>
public sealed record WriteRecordVariable(string Key, string Name, string Guid);

/// <summary>A value of the target as the record describes it: present or absent, its bytes as hex, and its entries by name.</summary>
/// <param name="Present">False when the variable is absent (an unset BootNext, or the state after a clear).</param>
/// <param name="Hex">The bytes as lowercase hex with no separators; empty when absent.</param>
/// <param name="Entries">The Boot#### names the bytes encode, in order; empty when absent.</param>
public sealed record WriteRecordImage(bool Present, string Hex, IReadOnlyList<string> Entries);

/// <summary>The BitLocker facts of the attempt (task 3.7). Never key material.</summary>
/// <param name="State">The assessed state; Unknown after a suspend that failed or did not answer in time.</param>
/// <param name="ProtectionStatus">The volume's ProtectionStatus (0 off, 1 on, 2 unknown), or null when no volume was read.</param>
/// <param name="ConversionStatus">The volume's ConversionStatus (0 to 5), or null when no volume was read.</param>
/// <param name="ProtectorTypes">The protector type names; empty when none or not read.</param>
/// <param name="PcrProfileText">The PCR profile text of the TPM protector, or null.</param>
/// <param name="SuspendOffered">Whether the one-restart suspend was offered.</param>
/// <param name="SuspendChosen">Whether the suspend was chosen with the confirmation.</param>
/// <param name="SuspendResult">"0", the HRESULT in hex, "timeout", "failed", "not called", or null when no suspend was attempted.</param>
/// <param name="ProtectionStatusAfterSuspend">The ProtectionStatus re-read after the suspend, or null.</param>
public sealed record WriteRecordBitLocker(
    BitLockerState State,
    int? ProtectionStatus,
    int? ConversionStatus,
    IReadOnlyList<string> ProtectorTypes,
    string? PcrProfileText,
    bool SuspendOffered,
    bool SuspendChosen,
    string? SuspendResult,
    int? ProtectionStatusAfterSuspend);

/// <summary>The Secure Boot facts of the attempt, informational.</summary>
/// <param name="Enabled">The enabled state (the SecureBoot variable, else the registry), or null when not read.</param>
/// <param name="SetupMode">The SetupMode variable, or null when not read.</param>
/// <param name="SourcesAgree">Whether the available sources agreed.</param>
public sealed record WriteRecordSecureBoot(bool? Enabled, bool? SetupMode, bool SourcesAgree);

/// <summary>
/// The machine the attempt ran on. Carries no service tag, and never the Windows machine ID or its
/// hash: only whether the unit check confirmed the M3 test unit. A change is enabled only when
/// both flags are true.
/// </summary>
/// <param name="Model">The model name, or null when the identity was not read.</param>
/// <param name="BiosVersion">The BIOS version, or null when the identity was not read.</param>
/// <param name="Confirmed">Whether the machine is the contracted OptiPlex 7020 on BIOS A18 (check expectedModel).</param>
/// <param name="UnitConfirmed">Whether its Windows machine ID is the M3 test unit's (check expectedUnit); false when it could not be read.</param>
public sealed record WriteRecordMachine(string? Model, string? BiosVersion, bool Confirmed, bool UnitConfirmed);

/// <summary>What the one native call returned (task 3.8). The NTSTATUS is what was observed, never derived from the Win32 code.</summary>
/// <param name="Succeeded">Whether the call returned success.</param>
/// <param name="Win32Error">The Win32 error after a failed call; 0 on success.</param>
/// <param name="Win32Name">The winerror.h name of the code, or empty.</param>
/// <param name="Win32Text">The winerror.h text of the code, or empty.</param>
/// <param name="NtStatus">The thread's last NTSTATUS after a failed call, or null when not observed.</param>
/// <param name="NtStatusText">The ntstatus.h text, or null.</param>
/// <param name="NtStatusSource">Where the NTSTATUS came from ("RtlGetLastNtStatus", with a "may be stale" remark when it is not the status behind the Win32 code), or "not observed".</param>
/// <param name="ElapsedMs">How long the call took, in milliseconds.</param>
public sealed record WriteRecordApi(
    bool Succeeded,
    int Win32Error,
    string Win32Name,
    string Win32Text,
    uint? NtStatus,
    string? NtStatusText,
    string NtStatusSource,
    long ElapsedMs);

/// <summary>What the one verification read returned.</summary>
/// <param name="ReadStatus">The read's status.</param>
/// <param name="MatchesAfter">Whether the value read equals the proposed value (absent for a clear).</param>
/// <param name="MatchesBefore">Whether the value read equals the previous value (absent when it was absent).</param>
/// <param name="Sha256">The SHA-256 of the bytes read, or null when nothing was read.</param>
/// <param name="Attributes">The attributes read, or null when nothing was read.</param>
public sealed record WriteRecordVerification(ReadStatus ReadStatus, bool MatchesAfter, bool MatchesBefore, string? Sha256, uint? Attributes);

/// <summary>
/// The structured record of one write attempt (task 3.8): blocked, cancelled or written, the
/// same object in three places (one line of write-log.jsonl, record.json in the snapshot folder
/// when stage S12 was reached, one log event); a commit that reached S12 also appended its Pending
/// intent line to write-log.jsonl before the call. Every field is filled with what the transaction
/// knew when the record was made; a field that was never learnt is null. The JSON is camelCase
/// with enums as strings, in the property order below. No field ever holds key material, and
/// the machine block carries no service tag and no Windows machine ID.
/// </summary>
public sealed record WriteRecord
{
    /// <summary>The value of <see cref="Schema"/>.</summary>
    public const string SchemaId = "uefiaccess-write-record/1";

    private const string UtcTimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    private static readonly JsonSerializerOptions LineOptions = CreateOptions(indented: false);
    private static readonly JsonSerializerOptions DocumentOptions = CreateOptions(indented: true);

    /// <summary>The schema marker, <see cref="SchemaId"/>.</summary>
    public string Schema { get; init; } = SchemaId;

    /// <summary>When the record was made, UTC, "yyyy-MM-ddTHH:mm:ssZ".</summary>
    public string TimestampUtc { get; init; } = string.Empty;

    /// <summary>The transaction id: the snapshot folder name, or the name the folder would have had for an attempt that made none.</summary>
    public string TransactionId { get; init; } = string.Empty;

    /// <summary>The tool version.</summary>
    public string ToolVersion { get; init; } = string.Empty;

    /// <summary>"cli" or "app".</summary>
    public string Host { get; init; } = string.Empty;

    /// <summary>"Reorder", "SetBootNext", "ClearBootNext", "Check", or "Restore" for a restore refused before it was resolved.</summary>
    public string Operation { get; init; } = string.Empty;

    /// <summary>Whether the request was a restore.</summary>
    public bool IsRestore { get; init; }

    /// <summary>The snapshot folder a restore was derived from, or null.</summary>
    public string? RestoredFrom { get; init; }

    /// <summary>The target variable, or null when the attempt was refused before it was known.</summary>
    public WriteRecordVariable? Variable { get; init; }

    /// <summary>The attributes read before the change, or null when absent or not read.</summary>
    public uint? AttributesBefore { get; init; }

    /// <summary>The attributes the change passes (always 7 for a whitelisted variable), or null when not known.</summary>
    public uint? AttributesRequested { get; init; }

    /// <summary>The short flag text of <see cref="AttributesBefore"/>, such as "NV BS RT", or null.</summary>
    public string? AttributeNames { get; init; }

    /// <summary>The size read before the change (0 when absent), or null when not read.</summary>
    public int? SizeBefore { get; init; }

    /// <summary>The size the change writes (0 for a clear), or null when not known.</summary>
    public int? SizeRequested { get; init; }

    /// <summary>The SHA-256 of the bytes before the change, or null when absent or not read.</summary>
    public string? BeforeSha256 { get; init; }

    /// <summary>The SHA-256 of the proposed bytes, or null when the change is a clear or not known.</summary>
    public string? ProposedSha256 { get; init; }

    /// <summary>The SHA-256 of the bytes read back after the call, or null when nothing was read.</summary>
    public string? AfterSha256 { get; init; }

    /// <summary>The value before the change, or null when not known.</summary>
    public WriteRecordImage? Before { get; init; }

    /// <summary>The proposed value, or null when not known.</summary>
    public WriteRecordImage? After { get; init; }

    /// <summary>Every check that ran, in stage order, the failed one last.</summary>
    public IReadOnlyList<CheckResult> Checks { get; init; } = Array.Empty<CheckResult>();

    /// <summary>The name of the check that refused the change, or null.</summary>
    public string? BlockedBy { get; init; }

    /// <summary>Whether the person confirmed the plan (stage S11 passed).</summary>
    public bool Confirmed { get; init; }

    /// <summary>The BitLocker choice of the confirmation (or the one given with the request), or null before it.</summary>
    public BitLockerChoice? BitLockerChoice { get; init; }

    /// <summary>The BitLocker facts, or null when they were not read.</summary>
    public WriteRecordBitLocker? BitLocker { get; init; }

    /// <summary>The number of writes made since Windows last started before this attempt: counted at S9, and again at S12 under the write lock, which replaces the S9 count; null when S9 did not run.</summary>
    public int? WritesThisStart { get; init; }

    /// <summary>
    /// Windows' boot counter when the record was made (the read-only BootId value of the Memory
    /// Management prefetch parameters), or null when it could not be read. The write budget counts
    /// a record of the current boot whatever its timestamp, so a change of the Windows clock
    /// cannot move a write of this start out of the count.
    /// </summary>
    public uint? BootId { get; init; }

    /// <summary>The device path node types of every entry the other-Windows-path and removable checks looked at, by entry name.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> DeviceNodeTypes { get; init; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

    /// <summary>The Secure Boot facts, or null when they were not read.</summary>
    public WriteRecordSecureBoot? SecureBoot { get; init; }

    /// <summary>The machine, or null when the identity was not read.</summary>
    public WriteRecordMachine? Machine { get; init; }

    /// <summary>Whether the native call was made. The write budget counts records with this true, once per transaction, and a transaction's Pending intent line until a later line of it follows.</summary>
    public bool ApiCalled { get; init; }

    /// <summary>What the call returned, or null when it was not made.</summary>
    public WriteRecordApi? Api { get; init; }

    /// <summary>What the verification read returned, or null when it was not made.</summary>
    public WriteRecordVerification? Verification { get; init; }

    /// <summary>The full path of the snapshot folder, or null when none was created.</summary>
    public string? SnapshotFolder { get; init; }

    /// <summary>The outcome.</summary>
    public WriteRecordOutcome Outcome { get; init; }

    /// <summary>The exit code the CLI returns: 0, 3, 4, 7, 8 or 9 (see <see cref="WriteExitCodes"/>).</summary>
    public int ExitCode { get; init; }

    /// <summary>The sentence the CLI prints and the shell announces.</summary>
    public string Sentence { get; init; } = string.Empty;

    /// <summary><see cref="TimestampUtc"/> parsed, or null when it does not parse.</summary>
    [JsonIgnore]
    public DateTimeOffset? Timestamp
    {
        get
        {
            if (string.IsNullOrWhiteSpace(TimestampUtc))
            {
                return null;
            }

            return DateTimeOffset.TryParse(TimestampUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsed)
                ? parsed
                : null;
        }
    }

    /// <summary>Formats a timestamp the way <see cref="TimestampUtc"/> holds it.</summary>
    /// <param name="timestamp">The time.</param>
    /// <returns>The UTC text "yyyy-MM-ddTHH:mm:ssZ".</returns>
    public static string FormatTimestamp(DateTimeOffset timestamp)
    {
        return timestamp.ToUniversalTime().ToString(UtcTimestampFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>The record as one line of JSON (no line breaks), for write-log.jsonl.</summary>
    /// <returns>The JSON text.</returns>
    public string ToJsonLine()
    {
        return JsonSerializer.Serialize(this, LineOptions);
    }

    /// <summary>The record as indented JSON, for record.json.</summary>
    /// <returns>The JSON text.</returns>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, DocumentOptions);
    }

    /// <summary>Parses one record from its JSON. Property names are matched case-insensitively. Never throws.</summary>
    /// <param name="json">The JSON text of one record.</param>
    /// <returns>The record, or null when the text is not one.</returns>
    public static WriteRecord? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            WriteRecord? record = JsonSerializer.Deserialize<WriteRecord>(json, LineOptions);
            return record is not null && string.Equals(record.Schema, SchemaId, StringComparison.Ordinal) ? record : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static JsonSerializerOptions CreateOptions(bool indented)
    {
        JsonSerializerOptions options = new()
        {
            WriteIndented = indented,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            IncludeFields = false,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

/// <summary>
/// What <see cref="WriteRecordLog.CountSinceStart(DateTimeOffset, uint?)"/> found: the writes made since Windows last
/// started, or why they cannot be counted. A count with a problem is never a number to trust:
/// the budget check refuses the write.
/// </summary>
/// <param name="Count">The writes counted; meaningful only when <paramref name="Problem"/> is null.</param>
/// <param name="Problem">Null when the log was read whole (or does not exist yet); otherwise why it could not be.</param>
public sealed record WriteBudgetCount(int Count, string? Problem)
{
    /// <summary>True when the log was read whole and the count can be trusted.</summary>
    public bool IsKnown => Problem is null;

    /// <summary>How many of <see cref="Count"/> are lines that are not whole records, counted as possible writes.</summary>
    public int NotRecords { get; init; }
}

/// <summary>
/// The append-only list of every write attempt, <c>write-log.jsonl</c> under the <c>writes</c>
/// data folder: one <see cref="WriteRecord"/> per line, blocked attempts included (they have no
/// snapshot folder), so this file, not the daily log, is the reliable list. It also counts the
/// writes made since Windows last started, which is the write budget of stages S9 and S12, per
/// transaction: a record with <see cref="WriteRecord.ApiCalled"/> true of this start, and the
/// intent line S12 appends before the call (<see cref="WriteRecordOutcome.Pending"/>) while no
/// later line of its transaction follows it, so a write whose S17 line was never appended (a
/// killed process, a list that could not be appended) still counts. A line is of this start when
/// it carries the current boot's <see cref="WriteRecord.BootId"/>, or when its timestamp is after
/// the start or cannot be read; either rule counts it, so a boot counter that cannot be read never
/// lowers the count. The count fails closed: a file that exists but cannot be read whole gives no
/// count, and a line that is not a record (one cut short by a crash during its append) counts as a
/// write unless it shows, or the next line that shows one gives, a timestamp before the start.
/// Every appended line is flushed to the disk before the append returns. Two named mutexes,
/// derived from the file's path and so shared by the application and the command-line tool, keep
/// processes apart: one around every append, so no line is lost to a sharing violation, and one
/// that a commit holds from S12 to S17, so two commits never count the same budget or write at
/// once. The folder is prepared through <see cref="Logging.PrepareDataFolderFor(string)"/> before
/// the first append, which creates it with the protected access list under the data root. No
/// member throws.
/// </summary>
public sealed class WriteRecordLog
{
    /// <summary>The folder name under the data root.</summary>
    public const string FolderName = "writes";

    /// <summary>The file name inside the folder.</summary>
    public const string FileName = "write-log.jsonl";

    /// <summary>
    /// The write budget per Windows start: the eighth write since Windows last started is refused
    /// with a restart sentence (design section 3.3). Eight is a budget, not a measurement: every
    /// write leaves a dead record in the variable store until the firmware reclaims, and whether
    /// this firmware reclaims at run time is not known.
    /// </summary>
    public const int WriteBudget = 8;

    /// <summary>The refusal when the folder's path is a file; {0} the path.</summary>
    public const string FolderIsAFileFormat = "{0} is a file, not a folder; nothing was recorded.";

    /// <summary>The refusal when write-log.jsonl is a link; {0} the path.</summary>
    public const string FileIsALinkFormat = "{0} is a symbolic link or other reparse point, not a plain file; rename or delete it from an administrator prompt.";

    /// <summary>The refusal when the folder cannot be prepared; {0} the path, {1} the reason.</summary>
    public const string CannotPrepareFormat = "{0} cannot be prepared: {1}";

    /// <summary>The refusal when the line cannot be appended; {0} the file, {1} the reason.</summary>
    public const string CannotAppendFormat = "{0} could not be appended: {1}";

    /// <summary>The reason when write-log.jsonl exists but cannot be read; {0} the error.</summary>
    public const string CannotReadReasonFormat = "the file exists but could not be read: {0}";

    /// <summary>The reason when another process held the append lock for longer than <see cref="AppendLockWait"/>.</summary>
    public const string AppendLockBusyReason = "another process kept the list locked";

    /// <summary>How long an append waits for another process's append to finish.</summary>
    public static readonly TimeSpan AppendLockWait = TimeSpan.FromSeconds(10);

    private const string MutexNamespace = @"Global\";
    private const string MutexPrefix = "UefiAccess-";
    private const string AppendPurpose = "append-";
    private const string CommitPurpose = "commit-";
    private const int MutexHashLength = 32;
    private const char LineBreak = '\n';

    /// <summary>The key, under HKEY_LOCAL_MACHINE, of Windows' boot counter; read only, never opened for writing.</summary>
    private const string BootIdKeyPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters";

    /// <summary>The DWORD value of Windows' boot counter, which Windows raises at every start.</summary>
    private const string BootIdValueName = "BootId";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The timestamp of a record line, found even in a line cut short after it.</summary>
    private static readonly Regex TimestampPattern = new("\"timestampUtc\"\\s*:\\s*\"(?<value>[^\"]*)\"", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly Func<string, string?> _prepareFolder;
    private readonly Func<uint?> _bootId;
    private readonly object _sync = new();

    /// <summary>
    /// Creates the log in <see cref="DefaultDirectory"/>, prepared by
    /// <see cref="Logging.PrepareDataFolderFor(string)"/>, with Windows' boot counter
    /// (<see cref="ReadWindowsBootId"/>) as the boot of each record.
    /// </summary>
    public WriteRecordLog()
        : this(DefaultDirectory, Logging.PrepareDataFolderFor, ReadWindowsBootId)
    {
    }

    /// <summary>Creates the log in an explicit folder with a replacement for <see cref="Logging.PrepareDataFolderFor(string)"/>, for tests.</summary>
    /// <param name="directory">The folder that holds the file.</param>
    /// <param name="prepareFolder">Called with the folder before it is created or written; returns null to go ahead or the refusal.</param>
    /// <param name="bootId">Returns the current boot's identity; null (the default) gives none, so only the clock rule places a record in this start.</param>
    internal WriteRecordLog(string directory, Func<string, string?> prepareFolder, Func<uint?>? bootId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(prepareFolder);
        DirectoryPath = Path.GetFullPath(directory);
        FilePath = Path.Combine(DirectoryPath, FileName);
        _prepareFolder = prepareFolder;
        _bootId = bootId ?? NoBootId;
    }

    /// <summary>The default folder: <c>writes</c> directly under <see cref="Logging.DataDirectory"/>.</summary>
    public static string DefaultDirectory => Path.Combine(Logging.DataDirectory, FolderName);

    /// <summary>The full path of the folder.</summary>
    public string DirectoryPath { get; }

    /// <summary>The full path of write-log.jsonl.</summary>
    public string FilePath { get; }

    /// <summary>
    /// Whether one more write fits the budget: true while fewer than <see cref="WriteBudget"/>
    /// writes would have been made after it, so the eighth write of a start is refused.
    /// </summary>
    /// <param name="writesThisStart">The count <see cref="CountSinceStart(DateTimeOffset, uint?)"/> returned.</param>
    /// <returns>True when the next write may go ahead.</returns>
    public static bool IsWithinBudget(int writesThisStart)
    {
        return writesThisStart + 1 < WriteBudget;
    }

    /// <summary>
    /// The moment Windows last started: <paramref name="now"/> minus the kernel's tick count
    /// (design section 3.3). A shut-down-and-start with fast startup keeps the tick count, so the
    /// budget then counts across a POST, which is the safe direction.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <returns>The start time.</returns>
    public static DateTimeOffset CurrentStartTime(DateTimeOffset now)
    {
        return now - TimeSpan.FromMilliseconds(Environment.TickCount64);
    }

    /// <summary>
    /// Windows' boot counter: the DWORD BootId under
    /// HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters,
    /// which Windows raises at every start, read with the key opened read-only. It does not depend
    /// on the clock, so a record that carries it stays in the count of its start when the clock is
    /// changed. Null when the value cannot be read; never throws.
    /// </summary>
    /// <returns>The counter, or null.</returns>
    public static uint? ReadWindowsBootId()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(BootIdKeyPath, writable: false);
            return key?.GetValue(BootIdValueName) is int value ? unchecked((uint)value) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The identity of the current boot the records of this log carry, or null when there is none; never throws.</summary>
    /// <returns>The boot identity, or null.</returns>
    public uint? CurrentBootId()
    {
        try
        {
            return _bootId();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Prepares the folder: the elevated-write check of the data root, then the folder itself is
    /// created when missing. A folder that is a file, or a log file that is a link, is refused.
    /// </summary>
    /// <returns>Null when the log may be written; otherwise the refusal.</returns>
    public string? PrepareRoot()
    {
        try
        {
            string? refusal = _prepareFolder(DirectoryPath);
            if (refusal is not null)
            {
                return refusal;
            }

            if (File.Exists(DirectoryPath))
            {
                return CheckSentences.Format(FolderIsAFileFormat, DirectoryPath);
            }

            Directory.CreateDirectory(DirectoryPath);
            if (File.Exists(FilePath) && (File.GetAttributes(FilePath) & FileAttributes.ReparsePoint) != 0)
            {
                return CheckSentences.Format(FileIsALinkFormat, FilePath);
            }

            return null;
        }
        catch (Exception ex)
        {
            return CheckSentences.Format(CannotPrepareFormat, DirectoryPath, ex.Message);
        }
    }

    /// <summary>
    /// Appends one record as one line, under the append lock that the application and the
    /// command-line tool share, so a line is never lost because another process was appending.
    /// A file whose last line was cut short (a crash during an append) gets a line break first, so
    /// the new record stays a line of its own. Prepares the folder first.
    /// </summary>
    /// <param name="record">The record.</param>
    /// <returns>Null when the line was written; otherwise the sentence that says why not.</returns>
    public string? Append(WriteRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        string? refusal = PrepareRoot();
        if (refusal is not null)
        {
            return refusal;
        }

        try
        {
            string line = record.ToJsonLine();
            lock (_sync)
            {
                using NamedLock? appendLock = NamedLock.TryEnter(MutexName(AppendPurpose), AppendLockWait, out bool busy, out string? lockProblem);
                if (appendLock is null)
                {
                    return CheckSentences.Format(CannotAppendFormat, FilePath, busy ? AppendLockBusyReason : lockProblem);
                }

                using FileStream stream = new(FilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
                bool needsBreak = false;
                if (stream.Length > 0)
                {
                    stream.Seek(-1, SeekOrigin.End);
                    needsBreak = stream.ReadByte() != LineBreak;
                }

                stream.Seek(0, SeekOrigin.End);
                using StreamWriter writer = new(stream, Utf8NoBom);
                if (needsBreak)
                {
                    writer.Write(LineBreak);
                }

                writer.Write(line);
                writer.Write(LineBreak);

                // The write budget and the evidence of a write rest on this line: it reaches the
                // disk before the append reports success, not when the cache is written back.
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            return null;
        }
        catch (Exception ex)
        {
            return CheckSentences.Format(CannotAppendFormat, FilePath, ex.Message);
        }
    }

    /// <summary>
    /// Takes the commit lock that a transaction holds from stage S12 to S17, without waiting: while
    /// another commit, in this process or another, holds it, the answer is null and
    /// <paramref name="problem"/> says so. Disposing the lock releases it, on the thread that took it.
    /// </summary>
    /// <param name="problem">Null when the lock was taken; otherwise the refusal.</param>
    /// <returns>The lock, or null.</returns>
    public IDisposable? TryEnterCommit(out string? problem)
    {
        NamedLock? commitLock = NamedLock.TryEnter(MutexName(CommitPurpose), TimeSpan.Zero, out bool busy, out string? lockProblem);
        problem = commitLock is not null
            ? null
            : busy ? CheckSentences.AnotherChangeBeingWritten : CheckSentences.Format(CheckSentences.WriteLockUnavailableFormat, lockProblem);
        return commitLock;
    }

    /// <summary>
    /// Counts the writes made since <paramref name="start"/>, with <see cref="CurrentBootId"/> as
    /// the current boot. See <see cref="CountSinceStart(DateTimeOffset, uint?)"/>. Never throws.
    /// </summary>
    /// <param name="start">The moment Windows last started (<see cref="CurrentStartTime"/>).</param>
    /// <returns>The count, or why there is none.</returns>
    public WriteBudgetCount CountSinceStart(DateTimeOffset start)
    {
        return CountSinceStart(start, CurrentBootId());
    }

    /// <summary>
    /// Counts the writes made since Windows last started, once per transaction. A line is of this
    /// start when it carries <paramref name="bootId"/>, or when its timestamp is after
    /// <paramref name="start"/> or cannot be read; either rule is enough. What counts:
    /// <list type="bullet">
    /// <item><description>a record of this start with <see cref="WriteRecord.ApiCalled"/> true;</description></item>
    /// <item><description>the intent line of stage S12 (<see cref="WriteRecordOutcome.Pending"/>, the
    /// call not yet made) of this start while no later line of its transaction follows it: the
    /// transaction's next line replaces it, so a commit blocked after S12 counts nothing and a
    /// write whose S17 line is missing counts once;</description></item>
    /// <item><description>a line that is not a record (it may be a write cut short while it was
    /// appended), judged by the timestamp it shows or, when it shows none (cut before it, or the
    /// run of zero bytes a power loss can leave), by the timestamp of the next line that shows one,
    /// since it was appended before that line; with no such line it counts.</description></item>
    /// </list>
    /// A missing file counts zero; a file that exists but cannot be read whole gives no count, only
    /// the problem. Never throws.
    /// </summary>
    /// <param name="start">The moment Windows last started (<see cref="CurrentStartTime"/>).</param>
    /// <param name="bootId">The current boot's identity (<see cref="ReadWindowsBootId"/>), or null when it is not known.</param>
    /// <returns>The count, or why there is none.</returns>
    public WriteBudgetCount CountSinceStart(DateTimeOffset start, uint? bootId)
    {
        string? text;
        try
        {
            text = ReadText();
        }
        catch (Exception ex)
        {
            return new WriteBudgetCount(0, CheckSentences.Format(CannotReadReasonFormat, ex.Message));
        }

        int count = 0;
        int notRecords = 0;
        HashSet<string> openIntents = new(StringComparer.Ordinal);
        foreach (LogLine line in ParseLines(text))
        {
            bool thisStart = line.IsOfThisStart(start, bootId);
            if (line.Record is not WriteRecord record)
            {
                if (thisStart)
                {
                    count++;
                    notRecords++;
                }

                continue;
            }

            string id = record.TransactionId ?? string.Empty;
            if (IsIntent(record))
            {
                if (thisStart && (id.Length == 0 || openIntents.Add(id)))
                {
                    count++;
                }

                continue;
            }

            // The transaction's own line replaces its intent; a call it records belongs to the start
            // its intent was counted in, whatever time the line shows.
            bool replacesIntent = id.Length > 0 && openIntents.Remove(id);
            if (replacesIntent)
            {
                count--;
            }

            if (record.ApiCalled && (thisStart || replacesIntent))
            {
                count++;
            }
        }

        return new WriteBudgetCount(count, null) { NotRecords = notRecords };
    }

    /// <summary>
    /// The number of lines of the file that are not whole records (<see cref="ReadAll"/> skips
    /// them), whatever their time: zero for a missing file, null when the file exists but cannot
    /// be read. Never throws.
    /// </summary>
    /// <returns>The number, or null.</returns>
    public int? CountLinesThatAreNotRecords()
    {
        try
        {
            return Lines(ReadText()).Count(line => WriteRecord.TryParse(line) is null);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The last <paramref name="count"/> records, oldest first. Never throws.</summary>
    /// <param name="count">How many to return; zero or less returns none.</param>
    /// <returns>The records.</returns>
    public IReadOnlyList<WriteRecord> ReadLast(int count)
    {
        if (count <= 0)
        {
            return Array.Empty<WriteRecord>();
        }

        IReadOnlyList<WriteRecord> all = ReadAll();
        return all.Count <= count ? all : all.Skip(all.Count - count).ToArray();
    }

    /// <summary>Every record in the file, oldest first; lines that are not records are skipped. Never throws.</summary>
    /// <returns>The records; empty when the file is missing or unreadable.</returns>
    public IReadOnlyList<WriteRecord> ReadAll()
    {
        List<WriteRecord> records = new();
        try
        {
            foreach (string line in Lines(ReadText()))
            {
                WriteRecord? record = WriteRecord.TryParse(line);
                if (record is not null)
                {
                    records.Add(record);
                }
            }
        }
        catch (Exception)
        {
            // A list that cannot be read shows nothing here; the budget count reports it as a problem.
        }

        return records;
    }

    /// <summary>The file's text, or null when it does not exist. Throws when it exists and cannot be read.</summary>
    private string? ReadText()
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        lock (_sync)
        {
            // Another process may hold the file open for its own append; reading alongside it is
            // safe because a line is written whole, and a line cut short is judged by the caller.
            using FileStream stream = new(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using StreamReader reader = new(stream, Utf8NoBom, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
    }

    /// <summary>The lines of the text that hold anything, without their line breaks.</summary>
    private static IEnumerable<string> Lines(string? text)
    {
        return (text ?? string.Empty)
            .Split(LineBreak)
            .Select(line => line.TrimEnd('\r'))
            .Where(line => !string.IsNullOrWhiteSpace(line));
    }

    /// <summary>The timestamp a line that is not a whole record shows, or null.</summary>
    private static DateTimeOffset? TimestampOf(string line)
    {
        Match match = TimestampPattern.Match(line);
        return match.Success ? new WriteRecord { TimestampUtc = match.Groups["value"].Value }.Timestamp : null;
    }

    /// <summary>
    /// The lines of the text, each parsed, with the time it is judged by: its own timestamp, or,
    /// for a line that is not a record and shows none, the timestamp of the next line that shows
    /// one (lines are appended in order under the append lock, so it was written no later), found
    /// by walking from the end. A record whose timestamp cannot be read keeps none, so it counts.
    /// </summary>
    private static LogLine[] ParseLines(string? text)
    {
        string[] lines = Lines(text).ToArray();
        LogLine[] parsed = new LogLine[lines.Length];
        DateTimeOffset? later = null;
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            WriteRecord? record = WriteRecord.TryParse(lines[i]);
            DateTimeOffset? shown = record is not null ? record.Timestamp : TimestampOf(lines[i]);
            parsed[i] = new LogLine(record, record is null && shown is null ? later : shown);
            if (shown is not null)
            {
                later = shown;
            }
        }

        return parsed;
    }

    /// <summary>The line stage S12 appends before the call: outcome Pending, the call not made.</summary>
    private static bool IsIntent(WriteRecord record) => record.Outcome == WriteRecordOutcome.Pending && !record.ApiCalled;

    /// <summary>The boot identity of a log built without one: none, so only the clock rule applies.</summary>
    private static uint? NoBootId() => null;

    /// <summary>One line of the file as the count judges it.</summary>
    /// <param name="Record">The record, or null for a line that is not one.</param>
    /// <param name="JudgedAt">The time the line is judged by, or null when none is known.</param>
    private readonly record struct LogLine(WriteRecord? Record, DateTimeOffset? JudgedAt)
    {
        /// <summary>True when the line belongs to the start that began at <paramref name="start"/>: the same boot, or a time after the start or none known.</summary>
        public bool IsOfThisStart(DateTimeOffset start, uint? bootId)
        {
            if (bootId is uint current && Record?.BootId == current)
            {
                return true;
            }

            return JudgedAt is not DateTimeOffset at || at > start;
        }
    }

    /// <summary>A mutex name in the global namespace derived from the file's full path, so every process that writes this list shares it.</summary>
    private string MutexName(string purpose)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(FilePath.ToUpperInvariant()));
        return MutexNamespace + MutexPrefix + purpose + Convert.ToHexString(hash)[..MutexHashLength];
    }

    /// <summary>
    /// A named mutex held by the calling thread; disposing it releases it. A mutex whose last
    /// owner ended without releasing it (a killed process) is taken over, which is what
    /// <see cref="AbandonedMutexException"/> reports.
    /// </summary>
    private sealed class NamedLock : IDisposable
    {
        private readonly Mutex _mutex;
        private bool _released;

        private NamedLock(Mutex mutex)
        {
            _mutex = mutex;
        }

        public static NamedLock? TryEnter(string name, TimeSpan wait, out bool busy, out string? problem)
        {
            busy = false;
            problem = null;
            Mutex? mutex = null;
            try
            {
                mutex = new Mutex(initiallyOwned: false, name);
                bool owned;
                try
                {
                    owned = mutex.WaitOne(wait);
                }
                catch (AbandonedMutexException)
                {
                    owned = true;
                }

                if (!owned)
                {
                    busy = true;
                    mutex.Dispose();
                    return null;
                }

                return new NamedLock(mutex);
            }
            catch (Exception ex)
            {
                mutex?.Dispose();
                problem = ex.Message;
                return null;
            }
        }

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (Exception)
            {
                // Released on another thread than the one that took it: the mutex is abandoned when
                // that thread ends, and the next owner takes it over; the caller never sees a throw.
            }
            finally
            {
                _mutex.Dispose();
            }
        }
    }
}
