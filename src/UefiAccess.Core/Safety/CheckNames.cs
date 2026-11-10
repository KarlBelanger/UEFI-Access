// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Core.Safety;

/// <summary>
/// The identifiers of the pipeline's checks, in the order the stages run them. They are the strings the JSON documents, the write record and the log
/// carry, so they never change once recorded. The sentences are in <see cref="CheckSentences"/>.
/// </summary>
public static class CheckNames
{
    /// <summary>S1: UEFI firmware, elevated, the privilege enabled.</summary>
    public const string Preconditions = "preconditions";

    /// <summary>S2 (recorded) and S11 (refused): the machine is the 2014 OptiPlex 7020 on BIOS A18.</summary>
    public const string ExpectedModel = "expectedModel";

    /// <summary>
    /// S2 (recorded, after <see cref="ExpectedModel"/>) and S11 (refused, after it): the Windows
    /// machine ID is the one of the M3 test unit (<see cref="PlatformPolicy.EnabledUnitMachineIdSha256"/>),
    /// so another OptiPlex 7020 on BIOS A18 is refused here.
    /// </summary>
    public const string ExpectedUnit = "expectedUnit";

    /// <summary>S3 and rule V15: one fresh, complete enumeration with no error and no warning.</summary>
    public const string Enumeration = "enumeration";

    /// <summary>S4: the name and GUID are on the whitelist and the operation is the one the entry allows.</summary>
    public const string Whitelist = "whitelist";

    /// <summary>S5 and rule V9: the current attributes are exactly 0x7 and are passed back unchanged.</summary>
    public const string Attributes = "attributes";

    /// <summary>S5 and rule V10: none of the authenticated-write bits (0x10, 0x20, 0x80) is set.</summary>
    public const string AuthenticatedWrite = "authenticatedWrite";

    /// <summary>Rules V1, V2, V3 and V8: byte counts, and every referenced Boot#### parses.</summary>
    public const string Format = "format";

    /// <summary>Rule V4: every requested entry exists under the EFI global GUID.</summary>
    public const string EntriesExist = "entriesExist";

    /// <summary>Rule V5: every entry of the current BootOrder exists.</summary>
    public const string CurrentListIntact = "currentListIntact";

    /// <summary>Rule V6: the new order is a permutation of the current one.</summary>
    public const string Permutation = "permutation";

    /// <summary>Rule V7: the new order differs from the current one.</summary>
    public const string NoChange = "noChange";

    /// <summary>Rules V11, V12 and V14: the one-time boot target is active and category Boot; a clear has something to clear.</summary>
    public const string BootNextTarget = "bootNextTarget";

    /// <summary>Rule V16: BootCurrent names a fixed-disk entry, so the other-Windows-path check can run.</summary>
    public const string BootCurrent = "bootCurrent";

    /// <summary>Rule V13: no entry that may start Windows through another boot path while BitLocker protectors exist, and no reorder while a one-time boot (BootNext) is set, which the firmware starts before the order.</summary>
    public const string OtherWindowsPath = "otherWindowsPath";

    /// <summary>Rule V17 (a warning, never a refusal): an entry without a fixed-disk node ahead of the current one.</summary>
    public const string RemovableAhead = "removableAhead";

    /// <summary>S7: the Secure Boot state, recorded and never blocking.</summary>
    public const string SecureBoot = "secureBoot";

    /// <summary>S7: the target compared with the newest baseline, a warning when it differs.</summary>
    public const string Baseline = "baseline";

    /// <summary>S8: the BitLocker assessment and, when the state needs one, the person's choice; at S12, for a reorder or a one-time boot, the state read again before any suspend must still be the one the dry run showed.</summary>
    public const string BitLocker = "bitLocker";

    /// <summary>S9: the snapshot and write-record roots accepted by the data-folder rule.</summary>
    public const string SnapshotRoot = "snapshotRoot";

    /// <summary>S9: fewer than eight writes since Windows last started; counted again at S12, under the cross-process write lock held until S17.</summary>
    public const string WriteBudget = "writeBudget";

    /// <summary>S11: a token whose hash equals the plan's, with the choice the state needs.</summary>
    public const string Confirmation = "confirmation";

    /// <summary>S12: the snapshot folder written and re-read with matching hashes.</summary>
    public const string Snapshot = "snapshot";

    /// <summary>S12 and S14: the target (and BootNext beside BootOrder, and every variable the rules read) re-read byte-identical to the plan's view.</summary>
    public const string Stale = "stale";

    /// <summary>S13: the one-restart suspend answered 0 and the re-read confirmed it.</summary>
    public const string BitLockerSuspend = "bitLockerSuspend";
}
