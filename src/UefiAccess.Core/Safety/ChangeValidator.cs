// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using UefiAccess.Core.Decoders;
using UefiAccess.Core.Variables;

namespace UefiAccess.Core.Safety;

/// <summary>
/// What <see cref="ChangeValidator.Validate"/> found: every check that ran, the first refusal, and
/// for a request that passed, the before and after images, the decoded entry lines and the diff
/// rows the plan is built from. A <see cref="CheckRequest"/> never yields images for a plan. The
/// constructor is internal and nothing is settable, so only the validator makes one and a caller
/// cannot hand <see cref="ChangePlan"/> images of its own.
/// </summary>
public sealed class ValidationResult
{
    /// <summary>Creates the result. Internal: the validator is its only caller.</summary>
    /// <param name="request">The request validated.</param>
    /// <param name="checks">The checks in the order they ran.</param>
    /// <param name="blockedBy">The name of the check that refused the request, or null.</param>
    /// <param name="blockedReason">The refusal sentence, or null.</param>
    /// <param name="operation">The operation the request maps to, or null.</param>
    /// <param name="variable">The whitelisted variable, or null.</param>
    /// <param name="before">The variable as read, or null.</param>
    /// <param name="after">The bytes that would be written, or null.</param>
    /// <param name="beforeEntries">The current list decoded.</param>
    /// <param name="afterEntries">The proposed list decoded.</param>
    /// <param name="diff">The before/after rows.</param>
    /// <param name="warnings">Every warning sentence.</param>
    /// <param name="deviceNodeTypes">The node types of every entry rules V13, V16 and V17 looked at.</param>
    /// <param name="dependencies">Every variable other than the target the rules read, with the value they saw.</param>
    internal ValidationResult(
        ChangeRequest request,
        IReadOnlyList<CheckResult> checks,
        string? blockedBy,
        string? blockedReason,
        ChangeOperation? operation,
        WhitelistEntry? variable,
        VariableImage? before,
        VariableImage? after,
        IReadOnlyList<EntryLine> beforeEntries,
        IReadOnlyList<EntryLine> afterEntries,
        IReadOnlyList<DiffRow> diff,
        IReadOnlyList<string> warnings,
        IReadOnlyDictionary<string, IReadOnlyList<string>> deviceNodeTypes,
        IReadOnlyList<VariableDependency> dependencies)
    {
        Request = request;
        Checks = checks;
        BlockedBy = blockedBy;
        BlockedReason = blockedReason;
        Operation = operation;
        Variable = variable;
        Before = before;
        After = after;
        BeforeEntries = beforeEntries;
        AfterEntries = afterEntries;
        Diff = diff;
        Warnings = warnings;
        DeviceNodeTypes = deviceNodeTypes;
        Dependencies = dependencies;
    }

    /// <summary>The request validated.</summary>
    public ChangeRequest Request { get; }

    /// <summary>The checks in the order they ran; the run stopped at the stage of the first failed one.</summary>
    public IReadOnlyList<CheckResult> Checks { get; }

    /// <summary>The name of the check that refused the request, or null when every check passed.</summary>
    public string? BlockedBy { get; }

    /// <summary>The refusal sentence, or null.</summary>
    public string? BlockedReason { get; }

    /// <summary>The operation the request maps to; null for a check request or when the whitelist refused it.</summary>
    public ChangeOperation? Operation { get; }

    /// <summary>The whitelisted variable; null when the whitelist refused the request.</summary>
    public WhitelistEntry? Variable { get; }

    /// <summary>The variable as read, once the whitelist step named it; null before that.</summary>
    public VariableImage? Before { get; }

    /// <summary>The bytes that would be written; null unless every check passed for W1, W2 or W3.</summary>
    public VariableImage? After { get; }

    /// <summary>The current list decoded, once known; empty before that.</summary>
    public IReadOnlyList<EntryLine> BeforeEntries { get; }

    /// <summary>The proposed list decoded; empty unless the request passed.</summary>
    public IReadOnlyList<EntryLine> AfterEntries { get; }

    /// <summary>The before/after rows; empty unless the request passed.</summary>
    public IReadOnlyList<DiffRow> Diff { get; }

    /// <summary>Every warning sentence (rules V7 allowed, V12 hidden, V17 removable).</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>The node types of every entry rules V13, V16 and V17 looked at, by entry name.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> DeviceNodeTypes { get; }

    /// <summary>
    /// Every variable other than the target that the validator read (the Boot#### entries the rules
    /// judged and the entry lines describe, BootCurrent, BootOrder for a one-time boot, and BootNext
    /// for a reorder while protectors may exist), with the value it saw (an absent one included),
    /// in the order first read; the drift reads of stages S12 and S14 compare each, so no verdict
    /// and no line of the dialog is stale at the call.
    /// </summary>
    public IReadOnlyList<VariableDependency> Dependencies { get; }

    /// <summary>True when no check refused the request.</summary>
    public bool Passed => BlockedBy is null;

    /// <summary>True when the request passed and is one of the three write operations, so a <see cref="ChangePlan"/> can be built from it.</summary>
    public bool IsPlan => Passed && Operation is not null && Variable is not null && Before is not null && After is not null;
}

/// <summary>
/// Stages S3 (rule V15) to S6 of the transaction over the fresh
/// variable set of stage S3: the whitelist, the attribute rules and the format rules V1 to V17
/// of section 3.4, the first failed stage ending the run with nothing built. A request that
/// passes gets its after-image from <see cref="BootListCodec.Encode"/> over the validated entry
/// numbers, so the bytes to write are the pipeline's own. The validator reads a set; it holds
/// no writer, never touches the firmware and never throws for any variable content.
/// <para>
/// The order of the checks: <c>enumeration</c> (V15: <see cref="EnumerationMethod.NtEnumerate"/>
/// only, see <see cref="IsCompleteEnumeration"/>); <c>whitelist</c> (S4); at S5 the clear's
/// <c>bootNextTarget</c> (V14), then <c>attributes</c> (V9) and <c>authenticatedWrite</c> (V10),
/// both recorded, the first failure naming the block; at S6 <c>format</c> (V1, V2, V3),
/// <c>currentListIntact</c> (V5, before V4 so a broken list is named as such),
/// <c>entriesExist</c> (V4), <c>permutation</c> (V6), <c>noChange</c> (V7),
/// <c>bootNextTarget</c> (V11, V12), <c>bootCurrent</c> (V16), <c>otherWindowsPath</c> (V13; for
/// a reorder it first refuses a one-time boot that is set, which the firmware starts before the
/// new order), <c>removableAhead</c> (V17, a warning), and last the size tripwire of V8 under
/// <c>format</c>.
/// V1 and V5 judge the current BootOrder for a reorder and a one-time boot alike (design section
/// 2.3 makes a well-formed BootOrder a pre-condition of every write, and V1 says nothing can be
/// changed until it is fixed); a clear, which only removes what the firmware would remove at the
/// next start, is judged by V14 and V2. A check request (the pre-flight of "write check") runs
/// the whitelist, V9, V10, V1, V2 and V5; for a variable that is not whitelisted it still records
/// V10, so a signed variable is reported as both.
/// </para>
/// <para>
/// Rules V13, V16 and V17 need the BitLocker state of stage S8, which the caller assesses and
/// passes in; they run while protectors may exist (every state but Off,
/// <see cref="BitLockerPreflight.ProtectorsMayExist"/>). Every variable a rule reads besides the
/// target is returned in <see cref="ValidationResult.Dependencies"/> with the value the rule saw.
/// </para>
/// </summary>
public static class ChangeValidator
{
    /// <summary>The three authenticated-write bits (AW 0x10, TA 0x20, EA 0x80) that make a variable read-only by rule V10.</summary>
    public const uint AuthenticatedAttributeMask = (uint)(VariableAttributes.AuthenticatedWriteAccess | VariableAttributes.TimeBasedAuthenticatedWriteAccess | VariableAttributes.EnhancedAuthenticatedAccess);

    /// <summary>The Windows boot manager's file path on the EFI system partition (rule V13; compared ignoring case, as FAT does).</summary>
    public const string WindowsBootManagerFilePath = @"\EFI\Microsoft\Boot\bootmgfw.efi";

    /// <summary>
    /// The removable-media fallback path, which Windows setup also writes to the system partition
    /// (rule V13). It is also the file the boot manager loads from a partition whose device path
    /// names no file, so rule V13 reads such an entry as naming it.
    /// </summary>
    public const string RemovableMediaFilePath = @"\EFI\Boot\bootx64.efi";

    /// <summary>BootCurrent's name under the EFI global GUID (rule V16, and the transaction's direct reads and snapshot).</summary>
    internal const string BootCurrentName = "BootCurrent";

    private const string BootCategory = "Boot";
    private const char PathSeparator = '\\';
    private const string InstanceSeparatorNode = ",";
    private const string PathSeparatorNode = ";";

    /// <summary>Runs the whitelist, attribute and format rules over a request.</summary>
    /// <param name="request">The request; a <see cref="RestoreRequest"/> must have been resolved at stage S0 and is refused here.</param>
    /// <param name="variables">The fresh variable set of stage S3, with the direct reads merged in.</param>
    /// <param name="enumeration">The enumeration the set came from, for rule V15.</param>
    /// <param name="bitLockerState">The state stage S8 assessed, for rules V13, V16 and V17.</param>
    /// <returns>The checks, the refusal if any, and the images of a request that passed.</returns>
    /// <exception cref="ArgumentException">The request is an unresolved <see cref="RestoreRequest"/> or of a type the validator does not know.</exception>
    public static ValidationResult Validate(ChangeRequest request, IVariableLookup variables, EnumerationResult enumeration, BitLockerState bitLockerState)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(enumeration);
        RequestTarget target = request switch
        {
            ReorderRequest reorder => new RequestTarget(Whitelist.BootOrder.Name, Whitelist.BootOrder.Guid, WritableKind.ReorderOnly, ChangeOperation.Reorder, (reorder.NewOrder ?? Array.Empty<ushort>()).ToArray()),
            BootNextRequest next => new RequestTarget(Whitelist.BootNext.Name, Whitelist.BootNext.Guid, WritableKind.OneShot, ChangeOperation.SetBootNext, new[] { next.Target }),
            ClearBootNextRequest => new RequestTarget(Whitelist.BootNext.Name, Whitelist.BootNext.Guid, WritableKind.OneShot, ChangeOperation.ClearBootNext, Array.Empty<ushort>()),
            CheckRequest check => new RequestTarget(check.Name ?? string.Empty, check.Guid, null, null, Array.Empty<ushort>()),
            RestoreRequest => throw new ArgumentException("A restore is resolved into a reorder, a one-time boot or a clear at stage S0 before validation.", nameof(request)),
            _ => throw new ArgumentException("Not a request the validator knows: " + request.GetType().Name, nameof(request)),
        };

        return new Run(request, target, variables, enumeration, bitLockerState).Execute();
    }

    /// <summary>
    /// The first problem of a whitelisted variable as read, or null, in the validator's order:
    /// rule V9 (attributes other than 0x7), then V1 or V2 (byte count), then V5 (a current
    /// BootOrder entry that does not exist). Shared with <see cref="WritabilityPolicy"/>, so the
    /// Writability line and the pipeline judge a store the same way. The authenticated-write
    /// bits of V10 are the policy's own verdict and are not judged here.
    /// </summary>
    /// <param name="entry">The whitelist entry.</param>
    /// <param name="current">The variable as read, or null when absent.</param>
    /// <param name="lookup">The store, for rule V5; null makes every referenced entry missing.</param>
    /// <returns>The reason sentence, or null when the value is well formed and writable.</returns>
    internal static string? CurrentValueProblem(WhitelistEntry entry, FirmwareVariable? current, IVariableLookup? lookup)
    {
        ArgumentNullException.ThrowIfNull(entry);
        string? problem = AttributesProblem(entry, current) ?? FormatProblem(entry, current);
        if (problem is not null)
        {
            return problem;
        }

        if (entry.Kind == WritableKind.ReorderOnly)
        {
            ushort? missing = FirstMissingEntry(BootListCodec.Decode(current?.Data).Entries, lookup);
            if (missing is not null)
            {
                return CheckSentences.Format(CheckSentences.CurrentListRefersToMissingFormat, KnownVariableNames.BootName(missing.Value));
            }
        }

        return null;
    }

    /// <summary>
    /// Rule V15 and stage S3: the enumeration is complete only when it came from
    /// <see cref="EnumerationMethod.NtEnumerate"/> with no error and no warning. The known-names
    /// fallback and <see cref="EnumerationMethod.InMemory"/> (a baseline folder, the application's
    /// offline view, or a test store) never are, so a plan cannot be made over a set whose reads
    /// would not be the firmware's. The one definition the validator and the transaction share.
    /// </summary>
    /// <param name="enumeration">The enumeration.</param>
    /// <returns>True when a change may be judged over it.</returns>
    internal static bool IsCompleteEnumeration(EnumerationResult enumeration)
    {
        ArgumentNullException.ThrowIfNull(enumeration);
        return enumeration.Method == EnumerationMethod.NtEnumerate
            && enumeration.Error is null
            && (enumeration.Warnings is null || enumeration.Warnings.Count == 0);
    }

    /// <summary>True for a load option that can be the one-time boot target: it parses, is active and is category Boot.</summary>
    /// <param name="option">The parsed Boot####.</param>
    /// <returns>True when rules V3, V11 and V12 would pass.</returns>
    internal static bool IsOneTimeBootTarget(LoadOption option)
    {
        return option is not null && option.Error is null && option.Active && string.Equals(option.Category, BootCategory, StringComparison.Ordinal);
    }

    /// <summary>Rule V1 for BootOrder (absent or empty, odd byte count) or V2 for BootNext (present and not 2 bytes).</summary>
    private static string? FormatProblem(WhitelistEntry entry, FirmwareVariable? current)
    {
        if (entry.Kind == WritableKind.ReorderOnly)
        {
            int size = current?.Size ?? 0;
            if (size == 0)
            {
                return CheckSentences.BootOrderEmpty;
            }

            return size % BootListCodec.EntrySize == 0 ? null : CheckSentences.Format(CheckSentences.OddByteCountFormat, size);
        }

        if (current is not null && current.Size != BootListCodec.EntrySize)
        {
            return CheckSentences.Format(CheckSentences.BootNextWrongSizeFormat, current.Size);
        }

        return null;
    }

    /// <summary>Rule V9: the attributes read must equal the entry's required 0x7; an absent variable has nothing to check.</summary>
    private static string? AttributesProblem(WhitelistEntry entry, FirmwareVariable? current)
    {
        if (current is null || (uint)current.Attributes == entry.RequiredAttributes)
        {
            return null;
        }

        return CheckSentences.Format(CheckSentences.AttributesNotWritableFormat, current.Name, VariableAttributeFlags.ToShortFlags(current.Attributes));
    }

    /// <summary>Rule V10: the authenticated-write bits of a variable as read, or null when it carries none or is absent.</summary>
    private static string? AuthenticatedWriteProblem(FirmwareVariable? current)
    {
        if (current is null)
        {
            return null;
        }

        uint authenticated = (uint)current.Attributes & AuthenticatedAttributeMask;
        if (authenticated == 0)
        {
            return null;
        }

        string flags = VariableAttributeFlags.ToShortFlags((VariableAttributes)authenticated);
        return CheckSentences.Format(CheckSentences.AuthenticatedWriteFormat, current.Name, flags);
    }

    /// <summary>The first entry number of a list that resolves to no Boot#### under the EFI global GUID, or null.</summary>
    private static ushort? FirstMissingEntry(IEnumerable<ushort> entries, IVariableLookup? lookup)
    {
        foreach (ushort index in entries)
        {
            if (FindBootEntry(lookup, index) is null)
            {
                return index;
            }
        }

        return null;
    }

    /// <summary>The Boot#### of an entry number under the EFI global GUID; Dell's BootFFFx under its vendor GUID is never found.</summary>
    private static FirmwareVariable? FindBootEntry(IVariableLookup? lookup, ushort index)
    {
        return lookup?.Find(KnownVariableNames.BootName(index), KnownGuids.EfiGlobalVariable);
    }

    private static LoadOption ParseEntry(FirmwareVariable? entry)
    {
        return LoadOptionParser.Parse(entry?.Data ?? Array.Empty<byte>());
    }

    /// <summary>The node types of an entry's device path, for the record: each rendered node's name before "(", separators left out.</summary>
    private static IReadOnlyList<string> NodeTypes(LoadOption option)
    {
        return option.DevicePathNodes
            .Where(node => node is not (InstanceSeparatorNode or PathSeparatorNode))
            .Select(node =>
            {
                int open = node.IndexOf('(', StringComparison.Ordinal);
                return open <= 0 ? node : node.Substring(0, open);
            })
            .ToArray();
    }

    /// <summary>
    /// Rule V13: the entry starts a Windows boot file (the boot manager or the removable-media
    /// fallback, ignoring case) and is not on BootCurrent's partition: it has no hard drive node,
    /// or its partition number, signature type or signature differs, or either node carries no
    /// signature to compare. The comparison is over the structured node, never the rendered text.
    /// </summary>
    private static bool IsOtherWindowsPath(LoadOption candidate, HardDriveNode bootCurrentNode)
    {
        if (!IsWindowsBootFile(EffectiveFilePath(candidate)))
        {
            return false;
        }

        return candidate.HardDriveNode is not { } candidateNode || !IsSamePartition(candidateNode, bootCurrentNode);
    }

    /// <summary>
    /// The file the boot manager loads for an entry: its File path, or, for a partition named
    /// without a file, the removable-media fallback on that partition (UEFI 2.10 section 3.1.2).
    /// Null when neither applies (a USB stick or a network entry names no partition).
    /// </summary>
    private static string? EffectiveFilePath(LoadOption option)
    {
        if (option.FilePath is not null)
        {
            return option.FilePath;
        }

        return option.HardDriveNode is null ? null : RemovableMediaFilePath;
    }

    private static bool IsWindowsBootFile(string? filePath)
    {
        if (filePath is null)
        {
            return false;
        }

        string normalized = NormalizeFilePath(filePath);
        return string.Equals(normalized, WindowsBootManagerFilePath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, RemovableMediaFilePath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A file path in one spelling for the comparison: forward slashes read as backslashes, repeated separators as one, a leading backslash.</summary>
    private static string NormalizeFilePath(string path)
    {
        StringBuilder text = new(path.Length + 1);
        foreach (char character in path.Trim())
        {
            char separatorOrCharacter = character == '/' ? PathSeparator : character;
            if (separatorOrCharacter == PathSeparator && text.Length > 0 && text[text.Length - 1] == PathSeparator)
            {
                continue;
            }

            text.Append(separatorOrCharacter);
        }

        if (text.Length == 0 || text[0] != PathSeparator)
        {
            text.Insert(0, PathSeparator);
        }

        return text.ToString();
    }

    private static bool IsSamePartition(HardDriveNode candidate, HardDriveNode current)
    {
        return candidate.HasSignature
            && current.HasSignature
            && candidate.SignatureType == current.SignatureType
            && candidate.Signature == current.Signature
            && candidate.PartitionNumber == current.PartitionNumber;
    }

    /// <summary>What a request asks of the whitelist: the variable, the kind it needs, the operation and the entry numbers named.</summary>
    /// <param name="Name">The variable name.</param>
    /// <param name="Guid">The vendor GUID.</param>
    /// <param name="Kind">The whitelist kind the operation needs; null for a check request.</param>
    /// <param name="Operation">The operation; null for a check request.</param>
    /// <param name="Entries">The requested order (W1), the target (W2), or nothing; a copy, so the caller's list cannot change under the run.</param>
    private sealed record RequestTarget(string Name, Guid Guid, WritableKind? Kind, ChangeOperation? Operation, IReadOnlyList<ushort> Entries);

    /// <summary>
    /// The set as the rules read it: every lookup is answered by the set and noted with the value
    /// found (or its absence), in the order first made, so the transaction can read each of them
    /// again before the call (<see cref="ValidationResult.Dependencies"/>).
    /// </summary>
    private sealed class RecordingLookup : IVariableLookup
    {
        private readonly IVariableLookup _inner;
        private readonly List<VariableDependency> _read = new();
        private readonly HashSet<(Guid Guid, string Name)> _seen = new();

        public RecordingLookup(IVariableLookup inner)
        {
            _inner = inner;
        }

        public IReadOnlyList<FirmwareVariable> All => _inner.All;

        public FirmwareVariable? Find(string name, Guid vendorGuid)
        {
            FirmwareVariable? found = _inner.Find(name, vendorGuid);
            if (name is not null && _seen.Add((vendorGuid, name)))
            {
                VariableImage image = found is null
                    ? VariableImage.Absent(0)
                    : VariableImage.FromBytes(found.Data ?? Array.Empty<byte>(), (uint)found.Attributes);
                _read.Add(new VariableDependency(name, vendorGuid, image));
            }

            return found;
        }

        /// <summary>What the rules read, the target of the request left out (the drift reads compare it on their own).</summary>
        public IReadOnlyList<VariableDependency> DependenciesBesides(string targetName, Guid targetGuid)
        {
            return Array.AsReadOnly(_read.Where(read => !(read.Guid == targetGuid && string.Equals(read.Name, targetName, StringComparison.Ordinal))).ToArray());
        }
    }

    /// <summary>One validation from the first check to the images, with the state the checks share.</summary>
    private sealed class Run
    {
        private readonly ChangeRequest _request;
        private readonly RequestTarget _target;
        private readonly RecordingLookup _variables;
        private readonly EnumerationResult _enumeration;
        private readonly BitLockerState _bitLockerState;
        private readonly List<CheckResult> _checks = new();
        private readonly List<string> _warnings = new();
        private readonly Dictionary<string, IReadOnlyList<string>> _deviceNodeTypes = new(StringComparer.Ordinal);
        private string? _blockedBy;
        private string? _blockedReason;
        private ChangeOperation? _operation;
        private WhitelistEntry? _entry;
        private FirmwareVariable? _current;
        private IReadOnlyList<ushort> _currentEntries = Array.Empty<ushort>();
        private IReadOnlyList<ushort> _bootOrderEntries = Array.Empty<ushort>();
        private VariableImage? _before;
        private VariableImage? _after;
        private IReadOnlyList<EntryLine> _beforeEntries = Array.Empty<EntryLine>();
        private IReadOnlyList<EntryLine> _afterEntries = Array.Empty<EntryLine>();
        private IReadOnlyList<DiffRow> _diff = Array.Empty<DiffRow>();
        private ushort? _bootCurrentIndex;
        private HardDriveNode? _bootCurrentNode;

        public Run(ChangeRequest request, RequestTarget target, IVariableLookup variables, EnumerationResult enumeration, BitLockerState bitLockerState)
        {
            _request = request;
            _target = target;
            _variables = new RecordingLookup(variables);
            _enumeration = enumeration;
            _bitLockerState = bitLockerState;
        }

        private IReadOnlyList<ushort> Requested => _target.Entries;

        public ValidationResult Execute()
        {
            bool passed = CheckEnumeration()
                && CheckWhitelist()
                && CheckSomethingToClear()
                && CheckAttributeRules()
                && CheckFormat()
                && CheckCurrentListIntact()
                && CheckEntriesExist()
                && CheckPermutation()
                && CheckNoChange()
                && CheckBootNextTarget()
                && CheckBootCurrent()
                && CheckOtherWindowsPath()
                && CheckRemovableAhead();
            if (passed && _operation is not null && _entry is not null)
            {
                BuildImages(_operation.Value, _entry);
            }

            return new ValidationResult(
                _request,
                _checks.AsReadOnly(),
                _blockedBy,
                _blockedReason,
                _operation,
                _entry,
                _before,
                _after,
                _beforeEntries,
                _afterEntries,
                _diff,
                _warnings.AsReadOnly(),
                _deviceNodeTypes.AsReadOnly(),
                _variables.DependenciesBesides(_target.Name, _target.Guid));
        }

        // ---- S3, rule V15 ----------------------------------------------------------------

        private bool CheckEnumeration()
        {
            return IsCompleteEnumeration(_enumeration)
                ? Pass(CheckNames.Enumeration)
                : Fail(CheckNames.Enumeration, CheckSentences.Format(CheckSentences.EnumerationIncompleteFormat, DescribeEnumeration()));
        }

        private string DescribeEnumeration()
        {
            string text = _enumeration.Method.ToString();
            if (_enumeration.Error is not null)
            {
                text = CheckSentences.Format(CheckSentences.EnumerationErrorFormat, text, _enumeration.Error);
            }

            int warnings = _enumeration.Warnings?.Count ?? 0;
            return warnings switch
            {
                0 => text,
                1 => CheckSentences.Format(CheckSentences.EnumerationOneWarningFormat, text),
                _ => CheckSentences.Format(CheckSentences.EnumerationWarningsFormat, text, warnings),
            };
        }

        // ---- S4 --------------------------------------------------------------------------

        private bool CheckWhitelist()
        {
            _current = _variables.Find(_target.Name, _target.Guid);
            if (!Whitelist.TryDescribe(_target.Name, _target.Guid, out WhitelistEntry? entry))
            {
                if (_current is not null)
                {
                    _before = VariableImage.FromBytes(_current.Data ?? Array.Empty<byte>(), (uint)_current.Attributes);
                }

                Fail(CheckNames.Whitelist, CheckSentences.Format(CheckSentences.NotWhitelistedFormat, _target.Name));

                // The pre-flight says both things about a signed variable: not whitelisted, and read-only by its attribute (V10).
                RecordAuthenticatedWrite();
                return false;
            }

            if (_target.Kind is { } kind && entry.Kind != kind)
            {
                return Fail(CheckNames.Whitelist, CheckSentences.Format(CheckSentences.WrongOperationFormat, _target.Name));
            }

            _entry = entry;
            _operation = _target.Operation;
            _before = _current is null
                ? VariableImage.Absent(entry.RequiredAttributes)
                : VariableImage.FromBytes(_current.Data ?? Array.Empty<byte>(), (uint)_current.Attributes);
            _currentEntries = BootListCodec.Decode(_current?.Data).Entries;
            _beforeEntries = Lines(_currentEntries);
            _bootOrderEntries = _operation switch
            {
                ChangeOperation.Reorder => _currentEntries,
                ChangeOperation.SetBootNext => BootListCodec.Decode(CurrentBootOrder()?.Data).Entries,
                _ => Array.Empty<ushort>(),
            };
            return Pass(CheckNames.Whitelist);
        }

        /// <summary>BootOrder as read: the target of a reorder, and for a one-time boot the list rules V1 and V5 judge beside it.</summary>
        private FirmwareVariable? CurrentBootOrder()
        {
            return _operation == ChangeOperation.Reorder ? _current : _variables.Find(Whitelist.BootOrder.Name, Whitelist.BootOrder.Guid);
        }

        // ---- S5: rule V14 (a clear needs a BootNext), then V9 and V10 ---------------------

        private bool CheckSomethingToClear()
        {
            if (_operation != ChangeOperation.ClearBootNext)
            {
                return true;
            }

            return _current is null
                ? Fail(CheckNames.BootNextTarget, CheckSentences.NoBootNextToClear)
                : Pass(CheckNames.BootNextTarget);
        }

        /// <summary>
        /// V9 then V10, both recorded: a variable with an authenticated bit fails V9 too (its
        /// attributes are not 0x7), so the block is named <c>attributes</c> and the record also
        /// carries the separate <c>authenticatedWrite</c> refusal.
        /// </summary>
        private bool CheckAttributeRules()
        {
            if (_entry is null)
            {
                return true;
            }

            string? problem = AttributesProblem(_entry, _current);
            bool attributes = problem is null ? Pass(CheckNames.Attributes) : Fail(CheckNames.Attributes, problem);
            bool authenticated = RecordAuthenticatedWrite();
            return attributes && authenticated;
        }

        private bool RecordAuthenticatedWrite()
        {
            string? problem = AuthenticatedWriteProblem(_current);
            return problem is null ? Pass(CheckNames.AuthenticatedWrite) : Fail(CheckNames.AuthenticatedWrite, problem);
        }

        // ---- S6: rules V1, V2, V3 --------------------------------------------------------

        private bool CheckFormat()
        {
            if (_entry is null)
            {
                return true;
            }

            string? problem = FormatProblem(_entry, _current);
            if (problem is null && _operation == ChangeOperation.SetBootNext)
            {
                // V1 for a one-time boot: the current BootOrder must be well formed too.
                problem = FormatProblem(Whitelist.BootOrder, CurrentBootOrder());
            }

            if (problem is not null)
            {
                return Fail(CheckNames.Format, problem);
            }

            foreach (ushort index in ReferencedEntries())
            {
                FirmwareVariable? entry = FindBootEntry(_variables, index);
                if (entry is not null && ParseEntry(entry).Error is not null)
                {
                    return Fail(CheckNames.Format, CheckSentences.Format(CheckSentences.EntryMalformedFormat, KnownVariableNames.BootName(index)));
                }
            }

            return Pass(CheckNames.Format);
        }

        /// <summary>
        /// The Boot#### entries rule V3 requires to parse: for W1 the current order and the
        /// requested one, for W2 the target. A clear refers to nothing it needs to start, so a
        /// BootNext naming a malformed entry can still be removed; BootCurrent's entry is judged
        /// by V16.
        /// </summary>
        private IEnumerable<ushort> ReferencedEntries()
        {
            return _operation switch
            {
                ChangeOperation.Reorder => _currentEntries.Concat(Requested).Distinct(),
                ChangeOperation.SetBootNext => Requested,
                _ => Array.Empty<ushort>(),
            };
        }

        // ---- rule V5 ---------------------------------------------------------------------

        /// <summary>V5 over the current BootOrder: for a reorder, for a one-time boot, and for the pre-flight of BootOrder itself.</summary>
        private bool CheckCurrentListIntact()
        {
            IReadOnlyList<ushort>? list = _operation switch
            {
                ChangeOperation.Reorder or ChangeOperation.SetBootNext => _bootOrderEntries,
                null when _entry is { Kind: WritableKind.ReorderOnly } => _currentEntries,
                _ => null,
            };
            if (list is null)
            {
                return true;
            }

            ushort? missing = FirstMissingEntry(list, _variables);
            return missing is null
                ? Pass(CheckNames.CurrentListIntact)
                : Fail(CheckNames.CurrentListIntact, CheckSentences.Format(CheckSentences.CurrentListRefersToMissingFormat, KnownVariableNames.BootName(missing.Value)));
        }

        // ---- rule V4 ---------------------------------------------------------------------

        /// <summary>
        /// V4 over the requested order or target. For a restore, an entry that is no longer in the
        /// current BootOrder is left to V6, whose restore sentence says why it cannot come back,
        /// whether or not its Boot#### still exists; the current list's own entries passed V5.
        /// </summary>
        private bool CheckEntriesExist()
        {
            if (_operation is not (ChangeOperation.Reorder or ChangeOperation.SetBootNext))
            {
                return true;
            }

            IEnumerable<ushort> named = _operation == ChangeOperation.Reorder && _request.RestoredFrom is not null
                ? Requested.Where(index => _currentEntries.Contains(index))
                : Requested;
            ushort? missing = FirstMissingEntry(named, _variables);
            return missing is null
                ? Pass(CheckNames.EntriesExist)
                : Fail(CheckNames.EntriesExist, CheckSentences.Format(CheckSentences.EntryMissingFormat, KnownVariableNames.BootName(missing.Value)));
        }

        // ---- rule V6 ---------------------------------------------------------------------

        private bool CheckPermutation()
        {
            if (_operation != ChangeOperation.Reorder)
            {
                return true;
            }

            Dictionary<ushort, int> currentCounts = Counts(_currentEntries);
            Dictionary<ushort, int> requestedCounts = Counts(Requested);
            foreach (ushort index in Requested)
            {
                if (!currentCounts.ContainsKey(index))
                {
                    string format = _request.RestoredFrom is null ? CheckSentences.OrderAddsFormat : CheckSentences.RestoreOrderAddsFormat;
                    return Fail(CheckNames.Permutation, CheckSentences.Format(format, KnownVariableNames.BootName(index)));
                }
            }

            foreach (ushort index in Requested)
            {
                if (requestedCounts[index] > currentCounts[index])
                {
                    return Fail(CheckNames.Permutation, CheckSentences.Format(CheckSentences.OrderDuplicatesFormat, KnownVariableNames.BootName(index)));
                }
            }

            foreach (ushort index in _currentEntries)
            {
                if (!requestedCounts.TryGetValue(index, out int count) || count < currentCounts[index])
                {
                    return Fail(CheckNames.Permutation, CheckSentences.Format(CheckSentences.OrderLeavesOutFormat, KnownVariableNames.BootName(index)));
                }
            }

            return Pass(CheckNames.Permutation);
        }

        private static Dictionary<ushort, int> Counts(IReadOnlyList<ushort> entries)
        {
            Dictionary<ushort, int> counts = new();
            foreach (ushort index in entries)
            {
                counts[index] = counts.TryGetValue(index, out int count) ? count + 1 : 1;
            }

            return counts;
        }

        // ---- rule V7 ---------------------------------------------------------------------

        private bool CheckNoChange()
        {
            if (_operation != ChangeOperation.Reorder)
            {
                return true;
            }

            if (!Requested.SequenceEqual(_currentEntries))
            {
                return Pass(CheckNames.NoChange);
            }

            return _request is ReorderRequest { AllowNoChange: true }
                ? Warn(CheckNames.NoChange, CheckSentences.OrderUnchangedAllowed)
                : Fail(CheckNames.NoChange, CheckSentences.OrderUnchanged);
        }

        // ---- rules V11, V12 --------------------------------------------------------------

        private bool CheckBootNextTarget()
        {
            if (_operation != ChangeOperation.SetBootNext || Requested.Count == 0)
            {
                return true;
            }

            ushort target = Requested[0];
            string name = KnownVariableNames.BootName(target);
            LoadOption option = ParseEntry(FindBootEntry(_variables, target));
            if (!option.Active)
            {
                return Fail(CheckNames.BootNextTarget, CheckSentences.Format(CheckSentences.TargetInactiveFormat, name));
            }

            if (!string.Equals(option.Category, BootCategory, StringComparison.Ordinal))
            {
                return Fail(CheckNames.BootNextTarget, CheckSentences.Format(CheckSentences.TargetIsApplicationFormat, name));
            }

            return option.Hidden
                ? Warn(CheckNames.BootNextTarget, CheckSentences.Format(CheckSentences.TargetHiddenWarningFormat, name))
                : Pass(CheckNames.BootNextTarget);
        }

        // ---- rules V16, V13, V17: W1 and W2 while protectors may exist --------------------

        private bool BootPathRulesApply()
        {
            return (_operation is ChangeOperation.Reorder or ChangeOperation.SetBootNext) && BitLockerPreflight.ProtectorsMayExist(_bitLockerState);
        }

        private bool CheckBootCurrent()
        {
            if (!BootPathRulesApply())
            {
                return true;
            }

            FirmwareVariable? bootCurrent = _variables.Find(BootCurrentName, KnownGuids.EfiGlobalVariable);
            if (bootCurrent is null || bootCurrent.Size != BootListCodec.EntrySize)
            {
                return Fail(CheckNames.BootCurrent, CheckSentences.BootCurrentUnusable);
            }

            ushort index = BootListCodec.Decode(bootCurrent.Data).Entries[0];
            FirmwareVariable? entry = FindBootEntry(_variables, index);
            if (entry is null)
            {
                return Fail(CheckNames.BootCurrent, CheckSentences.BootCurrentUnusable);
            }

            LoadOption option = ParseEntry(entry);
            RecordNodeTypes(index, option);
            if (option.Error is not null || option.HardDriveNode is null)
            {
                return Fail(CheckNames.BootCurrent, CheckSentences.BootCurrentUnusable);
            }

            _bootCurrentIndex = index;
            _bootCurrentNode = option.HardDriveNode;
            return Pass(CheckNames.BootCurrent);
        }

        /// <summary>
        /// V13. For W1 it first refuses a reorder while a one-time boot is set: the firmware starts
        /// BootNext's entry before any BootOrder entry at the next start, so the entries ahead of
        /// BootCurrent's in the new order are not the path that start takes, and a one-restart
        /// suspend would be used up on it (failure modes F7 and F15). The one-time boot is refused
        /// whatever it names, a BootNext that is not 2 bytes included; clearing it first (W3) is
        /// the way out. BootNext is read through the recording lookup, so its absence is a
        /// dependency the drift reads compare.
        /// </summary>
        private bool CheckOtherWindowsPath()
        {
            if (!BootPathRulesApply() || _bootCurrentNode is null)
            {
                return true;
            }

            if (_operation == ChangeOperation.Reorder && PendingOneTimeBoot() is string pending)
            {
                return Fail(CheckNames.OtherWindowsPath, CheckSentences.Format(CheckSentences.PendingOneTimeBootFormat, pending));
            }

            foreach (ushort index in EntriesAheadOfBootCurrent())
            {
                if (index == _bootCurrentIndex)
                {
                    // The one-time target is BootCurrent's own entry: the same load option starts the same path.
                    continue;
                }

                LoadOption option = ParseEntry(FindBootEntry(_variables, index));
                RecordNodeTypes(index, option);
                if (IsOtherWindowsPath(option, _bootCurrentNode))
                {
                    return Fail(CheckNames.OtherWindowsPath, CheckSentences.Format(CheckSentences.OtherWindowsPathFormat, KnownVariableNames.BootName(index)));
                }
            }

            return Pass(CheckNames.OtherWindowsPath);
        }

        /// <summary>
        /// V17: an entry that may be absent at the next start, ahead of BootCurrent's or as the
        /// one-time target, is warned about: one with no hard drive node (a stick named by its USB
        /// node, a network entry), and one whose path passes through a USB, SD or eMMC node even
        /// when it also names a partition (a stick written with a full path).
        /// </summary>
        private bool CheckRemovableAhead()
        {
            if (!BootPathRulesApply())
            {
                return true;
            }

            List<string> sentences = new();
            foreach (ushort index in EntriesAheadOfBootCurrent())
            {
                LoadOption option = ParseEntry(FindBootEntry(_variables, index));
                RecordNodeTypes(index, option);
                if (option.HardDriveNode is null || option.HasRemovableMessagingNode)
                {
                    sentences.Add(CheckSentences.Format(CheckSentences.RemovableAheadWarningFormat, KnownVariableNames.BootName(index)));
                }
            }

            return sentences.Count == 0
                ? Pass(CheckNames.RemovableAhead)
                : Warn(CheckNames.RemovableAhead, sentences);
        }

        /// <summary>
        /// The entries rules V13 and V17 look at: the one-time target for W2; for W1 every entry
        /// the new order places before BootCurrent's entry, which is every entry when BootCurrent's
        /// entry is not in the list at all (the firmware starts the first entry that starts, not
        /// position 1).
        /// </summary>
        private IEnumerable<ushort> EntriesAheadOfBootCurrent()
        {
            if (_operation == ChangeOperation.SetBootNext || _bootCurrentIndex is null)
            {
                return Requested;
            }

            int position = -1;
            for (int i = 0; i < Requested.Count; i++)
            {
                if (Requested[i] == _bootCurrentIndex.Value)
                {
                    position = i;
                    break;
                }
            }

            return position < 0 ? Requested : Requested.Take(position);
        }

        /// <summary>
        /// The one-time boot set now, by the entry it names ("Boot0000"), or "BootNext" when it is
        /// not one entry; null when BootNext is absent.
        /// </summary>
        private string? PendingOneTimeBoot()
        {
            FirmwareVariable? bootNext = _variables.Find(Whitelist.BootNext.Name, Whitelist.BootNext.Guid);
            if (bootNext is null)
            {
                return null;
            }

            return bootNext.Size == BootListCodec.EntrySize
                ? KnownVariableNames.BootName(BootListCodec.Decode(bootNext.Data).Entries[0])
                : Whitelist.BootNext.Name;
        }

        private void RecordNodeTypes(ushort index, LoadOption option)
        {
            _deviceNodeTypes[KnownVariableNames.BootName(index)] = NodeTypes(option);
        }

        // ---- the images, and rule V8 ------------------------------------------------------

        /// <summary>
        /// Builds the after-image from the validated entry numbers and checks rule V8 on it: the
        /// current size for a reorder, 2 bytes for a one-time boot, nothing for a clear. The rule
        /// cannot fail after V6 passed; it is kept as a tripwire and blocks under <c>format</c>.
        /// </summary>
        private void BuildImages(ChangeOperation operation, WhitelistEntry entry)
        {
            uint attributes = entry.RequiredAttributes;
            VariableImage after;
            IReadOnlyList<EntryLine> afterEntries;
            int requiredSize;
            switch (operation)
            {
                case ChangeOperation.Reorder:
                    after = VariableImage.FromBytes(BootListCodec.Encode(Requested), attributes);
                    afterEntries = Lines(Requested);
                    requiredSize = _current?.Size ?? 0;
                    break;
                case ChangeOperation.SetBootNext:
                    after = VariableImage.FromBytes(BootListCodec.Encode(Requested), attributes);
                    afterEntries = Lines(Requested);
                    requiredSize = BootListCodec.EntrySize;
                    break;
                default:
                    after = VariableImage.Absent(attributes);
                    afterEntries = Array.Empty<EntryLine>();
                    requiredSize = 0;
                    break;
            }

            if (after.Size != requiredSize)
            {
                Fail(CheckNames.Format, CheckSentences.Format(CheckSentences.ProposedSizeWrongFormat, entry.Name, after.Size, requiredSize));
                return;
            }

            _after = after;
            _afterEntries = afterEntries;
            _diff = BuildDiff(_beforeEntries, _afterEntries);
        }

        private IReadOnlyList<EntryLine> Lines(IReadOnlyList<ushort> entries)
        {
            EntryLine[] lines = new EntryLine[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                lines[i] = Line(i + 1, entries[i]);
            }

            return Array.AsReadOnly(lines);
        }

        private EntryLine Line(int position, ushort index)
        {
            string name = KnownVariableNames.BootName(index);
            FirmwareVariable? entry = FindBootEntry(_variables, index);
            if (entry is null)
            {
                return new EntryLine(position, name, CheckSentences.MissingEntryDescription, false, false, string.Empty, string.Empty) { Present = false };
            }

            LoadOption option = ParseEntry(entry);
            string description = option.Description;
            if (string.IsNullOrEmpty(description))
            {
                description = option.Error is null ? CheckSentences.NoDescription : CheckSentences.MalformedEntryDescription;
            }

            return new EntryLine(position, name, description, option.Active, option.Hidden, option.Category, option.DevicePath);
        }

        private static IReadOnlyList<DiffRow> BuildDiff(IReadOnlyList<EntryLine> before, IReadOnlyList<EntryLine> after)
        {
            int count = Math.Max(before.Count, after.Count);
            DiffRow[] rows = new DiffRow[count];
            for (int i = 0; i < count; i++)
            {
                EntryLine? beforeLine = i < before.Count ? before[i] : null;
                EntryLine? afterLine = i < after.Count ? after[i] : null;
                EntryLine? described = afterLine ?? beforeLine;
                rows[i] = new DiffRow(
                    i + 1,
                    beforeLine?.Name,
                    afterLine?.Name,
                    described?.Description ?? string.Empty,
                    described?.Flags ?? string.Empty);
            }

            return Array.AsReadOnly(rows);
        }

        // ---- recording -------------------------------------------------------------------

        private bool Pass(string check)
        {
            _checks.Add(new CheckResult(check, true, string.Empty));
            return true;
        }

        private bool Warn(string check, string sentence)
        {
            _warnings.Add(sentence);
            _checks.Add(new CheckResult(check, true, sentence, IsWarning: true));
            return true;
        }

        private bool Warn(string check, IReadOnlyList<string> sentences)
        {
            _warnings.AddRange(sentences);
            _checks.Add(new CheckResult(check, true, string.Join(" ", sentences), IsWarning: true));
            return true;
        }

        private bool Fail(string check, string reason)
        {
            _checks.Add(new CheckResult(check, false, reason));
            _blockedBy ??= check;
            _blockedReason ??= reason;
            return false;
        }
    }
}
