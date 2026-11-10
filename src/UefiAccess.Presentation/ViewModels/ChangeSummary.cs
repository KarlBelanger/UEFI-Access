// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Text;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Variables;
using UefiAccess.Presentation.Resources;

namespace UefiAccess.Presentation.ViewModels;

/// <summary>
/// Every text of the confirmation dialog, built once from the
/// <see cref="ChangePlan"/> the dry run returned: the one-sentence What will change (without the
/// suspend sentence, which follows the check box), the Before and After boxes one line per position
/// with moved rows marked "(was N)" after a first line that counts the positions, the three BitLocker
/// boxes, the Warnings box whose first line says how many lines follow, and the Snapshot folder box;
/// so each of the three multi-line boxes says on its first line what follows. Each single-line text is one sentence
/// a screen reader speaks whole on arrival; the multi-line texts join their lines with
/// <see cref="Environment.NewLine"/>. The texts come from <see cref="Strings"/> and from Core's own
/// sentences (<see cref="BitLockerPreflight"/>, <see cref="CheckSentences"/>, the plan's warnings);
/// nothing here reads the firmware. The suspend check box is offered exactly when the pipeline
/// offers the suspend: BitLocker On, Encrypting or Unknown, and a reorder or a one-time boot
/// (never the removal of the one-time boot choice, where nothing restarts differently).
/// </summary>
public sealed class ChangeSummary
{
    private const string BootCurrentName = "BootCurrent";

    private ChangeSummary(ChangePlan plan, string snapshotRoot, bool fromPendingSnapshot)
    {
        Plan = plan;
        FromPendingSnapshot = fromPendingSnapshot && plan.RestoredFrom is not null;
        SnapshotFolderName = plan.SnapshotFolderName;
        RestoredFromName = plan.RestoredFrom is null ? null : Path.GetFileName(Path.TrimEndingDirectorySeparator(plan.RestoredFrom));
        BitLockerAssessment bitLocker = plan.Policy.BitLocker;
        SuspendOffered = bitLocker.SuspendOffered && plan.Operation is ChangeOperation.Reorder or ChangeOperation.SetBootNext;

        WhatWillChangeText = WhatWillChange(plan, RestoredFromName);
        BeforeText = Before(plan, FromPendingSnapshot);
        AfterText = After(plan);
        BitLockerStateText = bitLocker.StateSentence;
        RiskText = Risk(plan);
        RecoveryKeyText = bitLocker.RecoveryKeySentence ?? Strings.RecoveryKeyNotNeededText;
        WarningLines = Warnings(plan);
        WarningsText = WarningLines.Count == 0
            ? Strings.NoWarningsText
            : Join(new[] { Format(Strings.WarningsHeaderFormat, Strings.Lines(WarningLines.Count)) }.Concat(WarningLines));
        SnapshotFolderText = Format(Strings.SnapshotFolderFormat, Path.Combine(snapshotRoot, plan.SnapshotFolderName));
    }

    /// <summary>The plan the texts describe.</summary>
    public ChangePlan Plan { get; }

    /// <summary>True when the change restores a snapshot whose own change did not complete (record Pending).</summary>
    public bool FromPendingSnapshot { get; }

    /// <summary>The name of the snapshot folder this change will write.</summary>
    public string SnapshotFolderName { get; }

    /// <summary>The name of the snapshot folder a restore restores, or null for a change that is not a restore.</summary>
    public string? RestoredFromName { get; }

    /// <summary>True when the dialog shows the suspend check box.</summary>
    public bool SuspendOffered { get; }

    /// <summary>What will change, without the suspend sentence (<see cref="Strings.SuspendSuffix"/> follows it while the check box is ticked).</summary>
    public string WhatWillChangeText { get; }

    /// <summary>The Before box: for a boot order "N positions; Down arrow reads each." and one line per position; for BootNext the entry or <see cref="Strings.NotSetText"/>; for a Pending snapshot's restore <see cref="Strings.PendingBeforeLine"/> first.</summary>
    public string BeforeText { get; }

    /// <summary>The After box: the same first line and lines, moved rows marked "(was N)"; <see cref="Strings.NotSetText"/> after a removal.</summary>
    public string AfterText { get; }

    /// <summary>The BitLocker state box: the assessment's sentence (section 3.8).</summary>
    public string BitLockerStateText { get; }

    /// <summary>The Risk box: the risk sentence of section 3.8 while protectors exist, <see cref="Strings.NoRiskText"/> otherwise.</summary>
    public string RiskText { get; }

    /// <summary>The Recovery key box: the reminder while protectors exist, <see cref="Strings.RecoveryKeyNotNeededText"/> otherwise.</summary>
    public string RecoveryKeyText { get; }

    /// <summary>The warning lines, without the first line that counts them.</summary>
    public IReadOnlyList<string> WarningLines { get; }

    /// <summary>The Warnings box: "N lines; Down arrow reads each." and one line per warning, or <see cref="Strings.NoWarningsText"/>.</summary>
    public string WarningsText { get; }

    /// <summary>The Snapshot folder box.</summary>
    public string SnapshotFolderText { get; }

    /// <summary>Builds the texts of a plan.</summary>
    /// <param name="plan">The plan the dry run returned.</param>
    /// <param name="snapshotRoot">The snapshot root the change's folder is written under.</param>
    /// <param name="fromPendingSnapshot">True when the plan restores a snapshot whose record is Pending.</param>
    /// <returns>The texts.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="plan"/> or <paramref name="snapshotRoot"/> is null.</exception>
    public static ChangeSummary From(ChangePlan plan, string snapshotRoot, bool fromPendingSnapshot = false)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(snapshotRoot);
        return new ChangeSummary(plan, snapshotRoot, fromPendingSnapshot);
    }

    /// <summary>An entry as a sentence names it: "Boot0003 (UEFI: SanDisk Cruzer)", the pipeline's own form.</summary>
    /// <param name="entry">The entry line.</param>
    /// <returns>The text.</returns>
    public static string DescribeEntry(EntryLine entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return CheckSentences.Format(TransactionSentences.EntryFormat, entry.Name, entry.Description);
    }

    /// <summary>
    /// The entry that moved when <paramref name="after"/> is <paramref name="before"/> with exactly
    /// one entry taken out and put back elsewhere: its 0-based positions before and after. When two
    /// entries fit (neighbours swapped), the one that moved up is named, as the Status sentence of
    /// the move up that made the order does. Null for any other order.
    /// </summary>
    /// <param name="before">The names before.</param>
    /// <param name="after">The names after.</param>
    /// <returns>The move, or null.</returns>
    public static (int From, int To)? SingleMove(IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (before.Count != after.Count || before.Count < 2)
        {
            return null;
        }

        (int From, int To)? downward = null;
        for (int from = 0; from < before.Count; from++)
        {
            for (int to = 0; to < after.Count; to++)
            {
                if (from == to || !string.Equals(before[from], after[to], StringComparison.Ordinal) || !SameWithout(before, from, after, to))
                {
                    continue;
                }

                if (to < from)
                {
                    return (from, to);
                }

                downward ??= (from, to);
            }
        }

        return downward;
    }

    private static string WhatWillChange(ChangePlan plan, string? restoredFrom)
    {
        switch (plan.Operation)
        {
            case ChangeOperation.Reorder:
                if (restoredFrom is not null)
                {
                    return Format(Strings.RestoreReorderFormat, restoredFrom);
                }

                string[] before = plan.BeforeEntries.Select(entry => entry.Name).ToArray();
                string[] after = plan.AfterEntries.Select(entry => entry.Name).ToArray();
                if (SingleMove(before, after) is { } move)
                {
                    EntryLine moved = plan.AfterEntries[move.To];
                    return Format(Strings.SingleMoveFormat, moved.Name, moved.Description, move.From + 1, move.To + 1, after.Length);
                }

                return Format(Strings.ReorderFormat, after.Length);
            case ChangeOperation.SetBootNext:
                string target = plan.AfterEntries.Count > 0 ? DescribeEntry(plan.AfterEntries[0]) : plan.After.Hex;
                return restoredFrom is null
                    ? Format(Strings.BootNextFormat, target)
                    : Format(Strings.RestoreBootNextFormat, restoredFrom, target);
            default:
                if (restoredFrom is not null)
                {
                    return Format(Strings.RestoreClearFormat, restoredFrom);
                }

                string current = plan.BeforeEntries.Count > 0 ? DescribeEntry(plan.BeforeEntries[0]) : plan.Before.Hex;
                return Format(Strings.ClearBootNextFormat, current);
        }
    }

    private static string Before(ChangePlan plan, bool fromPendingSnapshot)
    {
        IReadOnlyList<string> current = Lines(plan, plan.BeforeEntries, marks: null);
        if (!fromPendingSnapshot)
        {
            return Join(WithHeader(plan, current));
        }

        // The restored snapshot's change did not complete: its value before that change (the value
        // this restore puts back) first, then the value the firmware holds now. The first line says
        // what follows, so neither list gets a count line of its own.
        List<string> lines = new() { Strings.PendingBeforeLine };
        lines.AddRange(Lines(plan, plan.AfterEntries, marks: null));
        lines.Add(Strings.PendingNowLine);
        lines.AddRange(current);
        return Join(lines);
    }

    private static string After(ChangePlan plan)
    {
        if (plan.Operation != ChangeOperation.Reorder)
        {
            return Join(Lines(plan, plan.AfterEntries, marks: null));
        }

        // Each entry of the new order is matched with its own place in the old one (a repeated
        // name takes the next unused place), and a row whose place changed says where it was.
        bool[] used = new bool[plan.BeforeEntries.Count];
        string?[] marks = new string?[plan.AfterEntries.Count];
        for (int to = 0; to < plan.AfterEntries.Count; to++)
        {
            for (int from = 0; from < plan.BeforeEntries.Count; from++)
            {
                if (used[from] || !string.Equals(plan.BeforeEntries[from].Name, plan.AfterEntries[to].Name, StringComparison.Ordinal))
                {
                    continue;
                }

                used[from] = true;
                if (from != to)
                {
                    marks[to] = Format(Strings.MovedFromFormat, from + 1);
                }

                break;
            }
        }

        return Join(WithHeader(plan, Lines(plan, plan.AfterEntries, marks)));
    }

    /// <summary>
    /// The lines of a boot order after the first line that counts them ("2 positions; Down arrow
    /// reads each."), because a screen reader reads a multi-line box only to its first line on Tab
    ///; BootNext's one line ("Not set." or the entry) says everything itself.
    /// </summary>
    private static IEnumerable<string> WithHeader(ChangePlan plan, IReadOnlyList<string> lines)
    {
        return plan.Operation == ChangeOperation.Reorder
            ? new[] { Format(Strings.PositionsHeaderFormat, Strings.Positions(lines.Count)) }.Concat(lines)
            : lines;
    }

    /// <summary>The lines of a boot order (numbered) or of BootNext (the entry alone, or "Not set.").</summary>
    private static IReadOnlyList<string> Lines(ChangePlan plan, IReadOnlyList<EntryLine> entries, string?[]? marks)
    {
        if (plan.Operation != ChangeOperation.Reorder)
        {
            return entries.Count == 0 ? new[] { Strings.NotSetText } : entries.Select(EntryText).ToArray();
        }

        string[] lines = new string[entries.Count];
        for (int index = 0; index < entries.Count; index++)
        {
            string mark = marks is not null && index < marks.Length ? marks[index] ?? string.Empty : string.Empty;
            lines[index] = Format(Strings.PositionLineFormat, index + 1, EntryText(entries[index])) + mark;
        }

        return lines;
    }

    /// <summary>"Boot0002, Windows Boot Manager, active", with "inactive", ", hidden" or "missing" as the entry is.</summary>
    private static string EntryText(EntryLine entry)
    {
        string flags;
        if (!entry.Present)
        {
            flags = CheckSentences.EntryMissingFlag;
        }
        else
        {
            flags = entry.Active ? CheckSentences.EntryActiveFlag : CheckSentences.EntryInactiveFlag;
            if (entry.Hidden)
            {
                flags += CheckSentences.EntryFlagSeparator + CheckSentences.EntryHiddenFlag;
            }
        }

        return Format(Strings.EntryLineFormat, entry.Name, entry.Description, flags);
    }

    private static string Risk(ChangePlan plan)
    {
        BitLockerAssessment bitLocker = plan.Policy.BitLocker;
        if (!bitLocker.WarningShown)
        {
            return Strings.NoRiskText;
        }

        switch (plan.Operation)
        {
            case ChangeOperation.Reorder:
                string? current = BootCurrentEntry(plan);
                bool firstStays = current is not null
                    && plan.BeforeEntries.Count > 0
                    && plan.AfterEntries.Count > 0
                    && string.Equals(plan.BeforeEntries[0].Name, current, StringComparison.Ordinal)
                    && string.Equals(plan.AfterEntries[0].Name, current, StringComparison.Ordinal);
                return firstStays
                    ? Format(BitLockerPreflight.ReorderRiskFirstEntryStaysFormat, current)
                    : BitLockerPreflight.ReorderRiskSentence;
            case ChangeOperation.SetBootNext:
                string target = plan.AfterEntries.Count > 0 ? plan.AfterEntries[0].Name : plan.After.Hex;
                return Format(BitLockerPreflight.BootNextRiskFormat, target);
            default:
                return Strings.ClearRiskText;
        }
    }

    /// <summary>The Boot#### BootCurrent named when the rules read it, or null when they did not (it is read whenever protectors may exist).</summary>
    private static string? BootCurrentEntry(ChangePlan plan)
    {
        VariableDependency? bootCurrent = plan.Dependencies.FirstOrDefault(dependency =>
            dependency.Guid == KnownGuids.EfiGlobalVariable && string.Equals(dependency.Name, BootCurrentName, StringComparison.Ordinal));
        if (bootCurrent is null || !bootCurrent.Image.Present)
        {
            return null;
        }

        DecodeResult decoded = BootListCodec.Decode(bootCurrent.Image.Bytes);
        return decoded.ByteCount == BootListCodec.EntrySize ? KnownVariableNames.BootName(decoded.Entries[0]) : null;
    }

    private static IReadOnlyList<string> Warnings(ChangePlan plan)
    {
        List<string> lines = plan.Policy.Warnings
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Select(warning => warning.Trim())
            .ToList();
        if (plan.Operation == ChangeOperation.Reorder)
        {
            lines.Add(Strings.ReorderMayBeLostWarning);
        }

        return lines;
    }

    /// <summary>True when the two lists are equal once the entry at <paramref name="beforeIndex"/> and the one at <paramref name="afterIndex"/> are left out.</summary>
    private static bool SameWithout(IReadOnlyList<string> before, int beforeIndex, IReadOnlyList<string> after, int afterIndex)
    {
        int b = 0;
        int a = 0;
        while (b < before.Count && a < after.Count)
        {
            if (b == beforeIndex)
            {
                b++;
                continue;
            }

            if (a == afterIndex)
            {
                a++;
                continue;
            }

            if (!string.Equals(before[b], after[a], StringComparison.Ordinal))
            {
                return false;
            }

            b++;
            a++;
        }

        return true;
    }

    private static string Join(IEnumerable<string> lines)
    {
        StringBuilder text = new();
        foreach (string line in lines)
        {
            if (text.Length > 0)
            {
                text.Append(Environment.NewLine);
            }

            text.Append(line);
        }

        return text.ToString();
    }

    private static string Format(string format, params object?[] arguments) => string.Format(CultureInfo.InvariantCulture, format, arguments);
}
