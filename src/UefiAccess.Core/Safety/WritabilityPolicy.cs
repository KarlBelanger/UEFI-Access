// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Decoders;
using UefiAccess.Core.Variables;

namespace UefiAccess.Core.Safety;

/// <summary>What the application may do with one variable.</summary>
public enum Writability
{
    /// <summary>BootOrder on a live source, attributes 0x7, well formed, on the M3 test unit: its entries can be reordered.</summary>
    ReorderOnly,

    /// <summary>BootNext likewise: the one-time boot choice can be removed or put back from a snapshot.</summary>
    OneTimeBoot,

    /// <summary>An active, category-Boot Boot#### on a live source: it can be chosen for one boot.</summary>
    OneTimeBootTarget,

    /// <summary>An authenticated-write bit (0x10, 0x20 or 0x80) is set: only a signed update can change it.</summary>
    AuthenticatedWrite,

    /// <summary>Any other variable: not on the whitelist.</summary>
    NotWhitelisted,

    /// <summary>BootOrder or BootNext on a live source that fails rule V1, V2, V5 or V9: it cannot be changed until fixed.</summary>
    Malformed,

    /// <summary>Shown from a baseline folder: nothing can be changed offline.</summary>
    Offline,

    /// <summary>
    /// The machine is not the M3 test unit (another model or BIOS version, or another OptiPlex 7020
    /// on BIOS A18, or a Windows machine ID that could not be read): the controls work up to the
    /// confirmation, which refuses.
    /// </summary>
    WrongMachine,
}

/// <summary>The Writability line and the row's item status for one variable.</summary>
/// <param name="Verdict">The verdict.</param>
/// <param name="Sentence">The Writability line, one of <see cref="WritabilitySentences"/>.</param>
/// <param name="ItemStatus">
/// The row's item status: <see cref="WritabilitySentences.ItemStatusReorder"/> or
/// <see cref="WritabilitySentences.ItemStatusOneTimeBoot"/> while changeable,
/// <see cref="WritabilitySentences.ItemStatusElsewhere"/> for BootOrder and BootNext under
/// <see cref="Writability.WrongMachine"/>, otherwise empty.
/// </param>
/// <param name="FirmwareSetupHintApplies">
/// True for SecureBoot, SetupMode, PK, KEK, db and dbx with <see cref="Writability.AuthenticatedWrite"/>:
/// the application appends its firmware-setup instruction to the line (section 3.7).
/// </param>
public sealed record WritabilityVerdict(Writability Verdict, string Sentence, string ItemStatus, bool FirmwareSetupHintApplies)
{
    /// <summary>True for the two verdicts under which this application can change the variable.</summary>
    public bool IsChangeable => Verdict is Writability.ReorderOnly or Writability.OneTimeBoot;
}

/// <summary>
/// The texts of section 3.6 (the Writability line, tab stop 13) and of section 5.4 (the row's
/// item status), owned by Core so the application quotes them and the command-line tool prints
/// them from one place. The Writability line is a read-only text box, whose state the screen
/// reader says itself ("edit, read only"), so no sentence begins with "Read-only": one that the
/// application cannot change begins "Cannot be changed", and the state is heard once (Alpha 2
/// finding A2-6, the tester's A2-4, resolved on 28 September 2026).
/// </summary>
public static class WritabilitySentences
{
    /// <summary><see cref="Writability.ReorderOnly"/>.</summary>
    public const string ReorderOnly = "Changeable: the order of its entries can be changed from this application (Move up, Move down, Review change). Nothing else about it can be changed.";

    /// <summary><see cref="Writability.OneTimeBoot"/>.</summary>
    public const string OneTimeBoot = "Changeable: this one-time boot choice can be removed (Clear one-time boot) or put back from a snapshot (Restore previous value).";

    /// <summary><see cref="Writability.OneTimeBootTarget"/>.</summary>
    public const string OneTimeBootTarget = "Cannot be changed. This entry can be chosen for one boot (Boot once from this entry).";

    /// <summary><see cref="Writability.AuthenticatedWrite"/>. {0} is the authenticated flags (AW, TA or EA).</summary>
    public const string AuthenticatedWriteFormat = "Cannot be changed: authenticated write ({0}); only a signed update can change it. This application never writes it.";

    /// <summary><see cref="Writability.NotWhitelisted"/>.</summary>
    public const string NotWhitelisted = "Cannot be changed: this variable is not on the whitelist of this application. Only BootOrder (reorder) and BootNext can be changed.";

    /// <summary><see cref="Writability.Malformed"/>. {0} is the V1, V2, V5 or V9 reason.</summary>
    public const string MalformedFormat = "Cannot be changed until fixed: {0}";

    /// <summary><see cref="Writability.Offline"/>.</summary>
    public const string Offline = "Cannot be changed: shown from a baseline folder; nothing can be changed offline.";

    /// <summary><see cref="Writability.WrongMachine"/>: one sentence for another model or BIOS version and for another OptiPlex 7020.</summary>
    public const string WrongMachine = "Cannot be changed on this machine: changes are enabled only on one specific OptiPlex 7020 with BIOS A18 (the enabled unit). The Change controls and the dialog work up to Confirm change, which refuses; nothing is written.";

    /// <summary>The item status of the BootOrder row while it is changeable (section 5.4).</summary>
    public const string ItemStatusReorder = "Changeable: order of entries";

    /// <summary>The item status of the BootNext row while it is changeable (section 5.4).</summary>
    public const string ItemStatusOneTimeBoot = "Changeable: one-time boot";

    /// <summary>
    /// The item status of the BootOrder and BootNext rows on a machine that is not the M3 test unit
    /// (<see cref="Writability.WrongMachine"/>), decided on 25 September 2026: the two rows carry a
    /// status there too, so a tester on another machine (round 2, on another OptiPlex 7020)
    /// can report whether a screen reader speaks an item status on a list row. The words say first
    /// that the row cannot be changed here, and the Writability line (<see cref="WrongMachine"/>)
    /// says where it can. A Boot#### entry under the same verdict carries none
    /// (<see cref="ItemStatusNone"/>). The words of 25 September, "Changeable on the M3 test unit
    /// only", began with "Changeable", the first word of the two statuses of a changeable row, so a
    /// listener who heard only the start could take a refused row for a changeable one; since
    /// 7 October 2026 (Milestone 5) they begin with what holds for
    /// this machine.
    /// </summary>
    public const string ItemStatusElsewhere = "Not changeable on this machine";

    /// <summary>The item status of every other row, live or baseline, a Boot#### entry on another machine included: nothing is spoken.</summary>
    public const string ItemStatusNone = "";
}

/// <summary>
/// Task 3.6's policy for one variable: which of the eight verdicts of section 3.6 applies, from
/// the variable as read (name, GUID, attributes, bytes), the store it was read with (rule V5 needs
/// the Boot#### entries, so the store is a parameter beside the three inputs section 3.1 names),
/// whether the source is a baseline folder and whether the machine is the M3 test unit. The
/// attribute rule V9 and the format rules V1, V2 and V5 run over the fresh read in the
/// validator's order, through the validator's own code, so a store where every write would be
/// blocked is never called changeable and the Malformed line quotes the reason the dry run would
/// give. The policy reads; it never writes and never throws for any variable content.
/// </summary>
public static class WritabilityPolicy
{
    private static readonly string[] SecureBootGlobalNames = { "SecureBoot", "SetupMode", "PK", "KEK" };
    private static readonly string[] SecureBootDatabaseNames = { "db", "dbx" };

    /// <summary>Describes a variable's writability.</summary>
    /// <param name="variable">The variable as read.</param>
    /// <param name="lookup">The store it came from, for rule V5 (a null lookup makes every referenced entry missing).</param>
    /// <param name="offline">True when the variable comes from a baseline folder.</param>
    /// <param name="machineConfirmed">True when the machine is the M3 test unit (stage S2): the 2014 OptiPlex 7020 on BIOS A18 whose Windows machine ID is the enabled one.</param>
    /// <returns>The verdict, its sentence and the row's item status.</returns>
    public static WritabilityVerdict Describe(FirmwareVariable variable, IVariableLookup? lookup, bool offline, bool machineConfirmed)
    {
        ArgumentNullException.ThrowIfNull(variable);
        uint attributes = (uint)variable.Attributes;
        uint authenticated = attributes & ChangeValidator.AuthenticatedAttributeMask;

        if (Whitelist.TryDescribe(variable.Name, variable.VendorGuid, out WhitelistEntry? entry))
        {
            if (offline)
            {
                return Verdict(Writability.Offline, WritabilitySentences.Offline);
            }

            if (authenticated != 0)
            {
                return AuthenticatedWrite(authenticated, hintApplies: false);
            }

            string? problem = ChangeValidator.CurrentValueProblem(entry, variable, lookup);
            if (problem is not null)
            {
                return Verdict(Writability.Malformed, CheckSentences.Format(WritabilitySentences.MalformedFormat, problem));
            }

            if (!machineConfirmed)
            {
                // BootOrder and BootNext only: the status says where they can be changed.
                return new WritabilityVerdict(Writability.WrongMachine, WritabilitySentences.WrongMachine, WritabilitySentences.ItemStatusElsewhere, false);
            }

            return entry.Kind == WritableKind.ReorderOnly
                ? new WritabilityVerdict(Writability.ReorderOnly, WritabilitySentences.ReorderOnly, WritabilitySentences.ItemStatusReorder, false)
                : new WritabilityVerdict(Writability.OneTimeBoot, WritabilitySentences.OneTimeBoot, WritabilitySentences.ItemStatusOneTimeBoot, false);
        }

        if (variable.VendorGuid == KnownGuids.EfiGlobalVariable && KnownVariableNames.IsBootEntryName(variable.Name, out _))
        {
            if (authenticated != 0)
            {
                return AuthenticatedWrite(authenticated, hintApplies: false);
            }

            LoadOption option = LoadOptionParser.Parse(variable.Data ?? Array.Empty<byte>());
            if (!ChangeValidator.IsOneTimeBootTarget(option))
            {
                return Verdict(Writability.NotWhitelisted, WritabilitySentences.NotWhitelisted);
            }

            if (offline)
            {
                return Verdict(Writability.Offline, WritabilitySentences.Offline);
            }

            if (!machineConfirmed)
            {
                return Verdict(Writability.WrongMachine, WritabilitySentences.WrongMachine);
            }

            return Verdict(Writability.OneTimeBootTarget, WritabilitySentences.OneTimeBootTarget);
        }

        if (authenticated != 0)
        {
            return AuthenticatedWrite(authenticated, IsSecureBootVariable(variable.Name, variable.VendorGuid));
        }

        return Verdict(Writability.NotWhitelisted, WritabilitySentences.NotWhitelisted);
    }

    /// <summary>
    /// True for the six Secure Boot variables whose Writability line carries the firmware-setup
    /// instruction: SecureBoot, SetupMode, PK and KEK under the EFI global GUID, db and dbx under
    /// the image security database GUID.
    /// </summary>
    /// <param name="name">The variable name (case-sensitive).</param>
    /// <param name="guid">The vendor GUID.</param>
    /// <returns>True for one of the six.</returns>
    public static bool IsSecureBootVariable(string name, Guid guid)
    {
        if (name is null)
        {
            return false;
        }

        if (guid == KnownGuids.EfiGlobalVariable)
        {
            return Array.IndexOf(SecureBootGlobalNames, name) >= 0;
        }

        return guid == KnownGuids.EfiImageSecurityDatabase && Array.IndexOf(SecureBootDatabaseNames, name) >= 0;
    }

    private static WritabilityVerdict Verdict(Writability verdict, string sentence) => new(verdict, sentence, WritabilitySentences.ItemStatusNone, false);

    private static WritabilityVerdict AuthenticatedWrite(uint authenticatedBits, bool hintApplies)
    {
        string flags = VariableAttributeFlags.ToShortFlags((VariableAttributes)authenticatedBits);
        return new WritabilityVerdict(
            Writability.AuthenticatedWrite,
            CheckSentences.Format(WritabilitySentences.AuthenticatedWriteFormat, flags),
            WritabilitySentences.ItemStatusNone,
            hintApplies);
    }
}
