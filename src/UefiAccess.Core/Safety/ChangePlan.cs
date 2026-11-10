// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UefiAccess.Core.Baseline;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Variables;

namespace UefiAccess.Core.Safety;

/// <summary>One check of the pipeline as it ran.</summary>
/// <param name="Name">The check, one of <see cref="CheckNames"/>.</param>
/// <param name="Passed">True when the check passed (a warning passes).</param>
/// <param name="Reason">The refusal or warning sentence; empty when the check passed silently.</param>
/// <param name="IsWarning">True when the check passed with something to say.</param>
public sealed record CheckResult(string Name, bool Passed, string Reason, bool IsWarning = false);

/// <summary>
/// A variable's value at one moment: the bytes, their hash and hex, and the attributes. The image
/// keeps its own copy of the bytes and hands out a copy, so nothing that holds a plan can change
/// the bytes its hash was taken over.
/// </summary>
/// <param name="Present">False when the variable does not exist (a BootNext not set, or the after-image of a clear).</param>
/// <param name="Bytes">The bytes; empty when absent.</param>
/// <param name="Sha256">The lowercase SHA-256 of the bytes, the value index.json carries.</param>
/// <param name="Hex">The bytes as lowercase hexadecimal without separators ("02000100" for Boot0002, Boot0001).</param>
/// <param name="Attributes">The attributes read, or the attributes to pass for an image that is not yet on the firmware.</param>
public sealed record VariableImage(bool Present, byte[] Bytes, string Sha256, string Hex, uint Attributes)
{
    private readonly byte[] _bytes = (Bytes ?? Array.Empty<byte>()).ToArray();

    /// <summary>A copy of the bytes; empty when absent.</summary>
    public byte[] Bytes
    {
        get => _bytes.ToArray();
        init => _bytes = (value ?? Array.Empty<byte>()).ToArray();
    }

    /// <summary>The number of bytes.</summary>
    public int Size => _bytes.Length;

    /// <summary>An image of bytes that exist (or will exist) on the firmware.</summary>
    /// <param name="bytes">The bytes; copied.</param>
    /// <param name="attributes">The attributes.</param>
    /// <returns>The image.</returns>
    public static VariableImage FromBytes(byte[] bytes, uint attributes)
    {
        byte[] copy = (bytes ?? Array.Empty<byte>()).ToArray();
        return new VariableImage(true, copy, Sha256Of(copy), HexOf(copy), attributes);
    }

    /// <summary>An image of a variable that does not exist.</summary>
    /// <param name="attributes">The attributes the pipeline would pass when creating it.</param>
    /// <returns>The image, with no bytes and the hash of no bytes.</returns>
    public static VariableImage Absent(uint attributes)
    {
        byte[] none = Array.Empty<byte>();
        return new VariableImage(false, none, Sha256Of(none), HexOf(none), attributes);
    }

    /// <summary>True when the bytes given equal this image's bytes exactly.</summary>
    /// <param name="bytes">The bytes to compare, or null for "not present".</param>
    /// <returns>True for equal bytes; an absent image matches only null.</returns>
    public bool SameBytesAs(byte[]? bytes)
    {
        if (!Present || bytes is null)
        {
            return !Present && bytes is null;
        }

        return _bytes.AsSpan().SequenceEqual(bytes);
    }

    /// <summary>The lowercase SHA-256 of bytes, the same rendering the baseline index uses.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>64 lowercase hexadecimal characters.</returns>
    public static string Sha256Of(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes ?? Array.Empty<byte>())).ToLowerInvariant();

    /// <summary>Lowercase hexadecimal without separators.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The text; empty for no bytes.</returns>
    public static string HexOf(byte[] bytes) => Convert.ToHexString(bytes ?? Array.Empty<byte>()).ToLowerInvariant();
}

/// <summary>One entry of a boot list, decoded for the diff, the dialog and the record.</summary>
/// <param name="Position">The 1-based position in the list.</param>
/// <param name="Name">The Boot#### name.</param>
/// <param name="Description">The load option's description, or a placeholder for a missing or malformed entry.</param>
/// <param name="Active">LOAD_OPTION_ACTIVE.</param>
/// <param name="Hidden">LOAD_OPTION_HIDDEN.</param>
/// <param name="Category">"Boot", "App" or the hexadecimal category; empty for a missing entry.</param>
/// <param name="DevicePath">The device path as text; empty for a missing entry.</param>
public sealed record EntryLine(int Position, string Name, string Description, bool Active, bool Hidden, string Category, string DevicePath)
{
    /// <summary>False when the list names the entry but the firmware does not hold it.</summary>
    public bool Present { get; init; } = true;

    /// <summary>The flags as the decoders spell them: "active, category Boot", "inactive, hidden, category App"; "missing" for an absent entry.</summary>
    public string Flags
    {
        get
        {
            if (!Present)
            {
                return CheckSentences.EntryMissingFlag;
            }

            List<string> parts = new() { Active ? CheckSentences.EntryActiveFlag : CheckSentences.EntryInactiveFlag };
            if (Hidden)
            {
                parts.Add(CheckSentences.EntryHiddenFlag);
            }

            if (!string.IsNullOrEmpty(Category))
            {
                parts.Add(CheckSentences.Format(CheckSentences.EntryCategoryFormat, Category));
            }

            return string.Join(CheckSentences.EntryFlagSeparator, parts);
        }
    }
}

/// <summary>
/// A variable the validator read to judge a change, other than the target itself: the Boot####
/// entries the rules judged and the entry lines describe, BootCurrent and, for a one-time boot,
/// BootOrder. Stages S12 and S14 read each again and refuse the write when one no longer has this
/// value, so no rule's verdict is stale at the call.
/// </summary>
/// <param name="Name">The variable name.</param>
/// <param name="Guid">The vendor GUID.</param>
/// <param name="Image">The value the rules saw; <see cref="VariableImage.Present"/> is false for a variable they found absent.</param>
public sealed record VariableDependency(string Name, Guid Guid, VariableImage Image)
{
    /// <summary>The display key "guid:name".</summary>
    public string Key => FirmwareVariable.MakeKey(Guid, Name);
}

/// <summary>One row of the decoded before/after diff by entry name (task 3.3).</summary>
/// <param name="Position">The 1-based position.</param>
/// <param name="Before">The entry at this position before the change, or null when there is none (a BootNext not set).</param>
/// <param name="After">The entry at this position after the change, or null when there is none (a cleared BootNext).</param>
/// <param name="Description">The description of the entry after the change, or of the one before it when nothing follows.</param>
/// <param name="Flags">The flags of that entry, as <see cref="EntryLine.Flags"/> spells them.</param>
public sealed record DiffRow(int Position, string? Before, string? After, string Description, string Flags)
{
    /// <summary>True when the entry at this position changes.</summary>
    public bool Changed => !string.Equals(Before, After, StringComparison.Ordinal);
}

/// <summary>The platform facts of stages S2, S7 and S8 as the plan and the snapshot record them.</summary>
/// <param name="SecureBoot">The Secure Boot policy, or null when it could not be read; informational, never blocking.</param>
/// <param name="Identity">The machine identity as read at S2.</param>
/// <param name="MachineConfirmed">
/// True when the machine is the M3 test unit: the 2014 OptiPlex 7020 on BIOS A18
/// (<see cref="ModelConfirmed"/>) and the unit check confirmed (<see cref="Unit"/>); S11 refuses
/// otherwise, at expectedModel for the model or BIOS and at expectedUnit for the unit.
/// </param>
/// <param name="BitLocker">The BitLocker assessment of S8.</param>
/// <param name="BaselineFolder">The newest baseline folder compared at S7, or null when there is none.</param>
/// <param name="BaselineMatches">True when the target's hash equals the baseline's; null when not compared.</param>
/// <param name="Warnings">Every warning sentence the stages produced, for the dialog's Warnings box and the record.</param>
public sealed record PolicyFindings(
    SecureBootPolicyResult? SecureBoot,
    MachineIdentityResult Identity,
    bool MachineConfirmed,
    BitLockerAssessment BitLocker,
    string? BaselineFolder,
    bool? BaselineMatches,
    IReadOnlyList<string> Warnings)
{
    /// <summary>What the unit check of S2 found; <see cref="UnitMatch.MachineIdNotRead"/> unless set, so findings without it never confirm the unit.</summary>
    public UnitMatch Unit { get; init; } = UnitMatch.MachineIdNotRead;

    /// <summary>True when the identity is the 2014 OptiPlex 7020 on BIOS A18 (<see cref="PlatformPolicy.IsContractedMachine"/>), whatever the unit.</summary>
    public bool ModelConfirmed => PlatformPolicy.IsContractedMachine(Identity);
}

/// <summary>
/// The dry-run result of stage S10: what would be written, shown as before/after images, decoded
/// entry lines and diff rows, with the policy findings and every check that ran. The plan is the
/// only thing a person can confirm, and <see cref="Confirm"/> is the only way to get a
/// <see cref="ConfirmationToken"/>; the token carries <see cref="PlanHash"/>, so a commit can refuse
/// a confirmation that belongs to other bytes. The constructor is internal, like those of
/// <see cref="ValidationResult"/> and <see cref="WritePlan"/>: only the pipeline builds a plan, so
/// a caller cannot bring a plan with bytes of its own to the confirmation.
/// </summary>
public sealed class ChangePlan
{
    /// <summary>The suffix of the snapshot folder name of a restore ("-restore").</summary>
    internal const string RestoreSuffix = "-restore";

    /// <summary>The separator between the timestamp and the word of a snapshot folder name.</summary>
    private const string FolderWordSeparator = "-";

    /// <summary>Builds the plan from a validation that passed and the findings of stages S2, S7 and S8. Internal: only the transaction builds one.</summary>
    /// <param name="validation">The validator's result; it must have passed and carry images (a check request never makes a plan).</param>
    /// <param name="policy">The policy findings.</param>
    /// <param name="checks">Every check that ran, in stage order (the transaction's stages and the validator's rules together).</param>
    /// <param name="createdAt">When the plan was made; the snapshot folder is named from it.</param>
    /// <exception cref="ArgumentException">The validation did not pass or produced no images.</exception>
    internal ChangePlan(ValidationResult validation, PolicyFindings policy, IReadOnlyList<CheckResult> checks, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(validation);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(checks);
        if (!validation.IsPlan || validation.Operation is null || validation.Variable is null || validation.Before is null || validation.After is null)
        {
            throw new ArgumentException("A plan needs a validation that passed with a before-image and an after-image; a blocked request or a check request has none.", nameof(validation));
        }

        Request = validation.Request;
        Operation = validation.Operation.Value;
        Variable = validation.Variable;
        Before = validation.Before;
        After = validation.After;
        BeforeEntries = Array.AsReadOnly(validation.BeforeEntries.ToArray());
        AfterEntries = Array.AsReadOnly(validation.AfterEntries.ToArray());
        Diff = Array.AsReadOnly(validation.Diff.ToArray());
        DeviceNodeTypes = validation.DeviceNodeTypes;
        Dependencies = Array.AsReadOnly(validation.Dependencies.ToArray());
        Policy = policy;
        Checks = Array.AsReadOnly(checks.ToArray());
        RestoredFrom = validation.Request.RestoredFrom;
        CreatedAt = createdAt;
        PlanHash = ComputePlanHash(Operation, Variable.Key, Before, After);
        SnapshotFolderName = FolderName(createdAt, OperationFolderWord(Operation), RestoredFrom is not null);
    }

    /// <summary>The request the plan answers (host, restore origin, the entries asked for).</summary>
    public ChangeRequest Request { get; }

    /// <summary>The operation: W1 reorder, W2 one-time boot, W3 clear.</summary>
    public ChangeOperation Operation { get; }

    /// <summary>The whitelisted variable.</summary>
    public WhitelistEntry Variable { get; }

    /// <summary>The variable as read in this transaction.</summary>
    public VariableImage Before { get; }

    /// <summary>The bytes to write; <see cref="VariableImage.Present"/> is false for a clear.</summary>
    public VariableImage After { get; }

    /// <summary>The entries of the current list, decoded.</summary>
    public IReadOnlyList<EntryLine> BeforeEntries { get; }

    /// <summary>The entries of the proposed list, decoded.</summary>
    public IReadOnlyList<EntryLine> AfterEntries { get; }

    /// <summary>The before/after diff by entry name, one row per position.</summary>
    public IReadOnlyList<DiffRow> Diff { get; }

    /// <summary>The node types of every entry rules V13 and V17 looked at, by entry name, for the record.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> DeviceNodeTypes { get; }

    /// <summary>Every variable other than the target that the rules read, with the value they saw; the drift reads of S12 and S14 compare each.</summary>
    public IReadOnlyList<VariableDependency> Dependencies { get; }

    /// <summary>The findings of stages S2, S7 and S8.</summary>
    public PolicyFindings Policy { get; }

    /// <summary>Every check that ran, in stage order.</summary>
    public IReadOnlyList<CheckResult> Checks { get; }

    /// <summary>
    /// The lowercase SHA-256 over the operation, the variable key, the before bytes, the after
    /// bytes and the attributes; see <see cref="ComputePlanHash"/> for the exact layout.
    /// </summary>
    public string PlanHash { get; }

    /// <summary>The snapshot folder a restore was resolved from, or null.</summary>
    public string? RestoredFrom { get; }

    /// <summary>When the plan was made.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// The name of the snapshot folder this transaction will use, "yyyyMMdd-HHmmss-reorder",
    /// "-bootnext" or "-bootnext-clear", with "-restore" appended for a restore. It is known before
    /// the folder exists, so the dialog can name it.
    /// </summary>
    public string SnapshotFolderName { get; }

    /// <summary>
    /// Issues the confirmation token for exactly this plan. Whether the choice suits the BitLocker
    /// state is judged at stage S11, where a missing choice is refused; this method only binds it.
    /// </summary>
    /// <param name="choice">The BitLocker choice made with the confirmation.</param>
    /// <returns>A token carrying <see cref="PlanHash"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The choice is not one of the three values.</exception>
    public ConfirmationToken Confirm(BitLockerChoice choice)
    {
        if (!Enum.IsDefined(choice))
        {
            throw new ArgumentOutOfRangeException(nameof(choice), choice, "Not one of the three BitLocker choices.");
        }

        return new ConfirmationToken(PlanHash, choice, DateTimeOffset.Now);
    }

    /// <summary>
    /// The plan hash: the lowercase SHA-256 of the UTF-8 text made of five lines joined by "\n",
    /// the operation name ("Reorder", "SetBootNext", "ClearBootNext"), the variable key, the
    /// before bytes as lowercase hex or "absent", the after bytes as lowercase hex or "absent",
    /// and the attributes to pass as a decimal number. Two plans with the same bytes hash the same;
    /// a plan for other bytes, another operation or another variable does not.
    /// </summary>
    /// <param name="operation">The operation.</param>
    /// <param name="key">The variable key "guid:name".</param>
    /// <param name="before">The before-image.</param>
    /// <param name="after">The after-image; its attributes are the ones passed.</param>
    /// <returns>64 lowercase hexadecimal characters.</returns>
    public static string ComputePlanHash(ChangeOperation operation, string key, VariableImage before, VariableImage after)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        string text = string.Join(
            "\n",
            operation.ToString(),
            key,
            ImageText(before),
            ImageText(after),
            after.Attributes.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    /// <summary>The operation word of the snapshot folder name: "reorder", "bootnext" or "bootnext-clear".</summary>
    /// <param name="operation">The operation.</param>
    /// <returns>The word.</returns>
    public static string OperationFolderWord(ChangeOperation operation)
    {
        return operation switch
        {
            ChangeOperation.Reorder => "reorder",
            ChangeOperation.SetBootNext => "bootnext",
            ChangeOperation.ClearBootNext => "bootnext-clear",
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Not one of the three operations."),
        };
    }

    /// <summary>
    /// The name of a transaction's snapshot folder, which is the transaction id:
    /// "yyyyMMdd-HHmmss-" and the word, with <see cref="RestoreSuffix"/> for a restore. The plan
    /// and the transaction (before a plan exists) both name it through this method.
    /// </summary>
    /// <param name="createdAt">The start of the transaction.</param>
    /// <param name="word">The operation word (<see cref="OperationFolderWord"/>), or the transaction's word for a request that made no plan.</param>
    /// <param name="restore">True for a restore.</param>
    /// <returns>The folder name.</returns>
    internal static string FolderName(DateTimeOffset createdAt, string word, bool restore)
    {
        return BaselineExporter.DefaultRunId(createdAt) + FolderWordSeparator + word + (restore ? RestoreSuffix : string.Empty);
    }

    private static string ImageText(VariableImage image) => image.Present ? image.Hex : "absent";
}
