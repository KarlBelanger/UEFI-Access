// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Core.Safety;

/// <summary>The three operations, and no other.</summary>
public enum ChangeOperation
{
    /// <summary>W1: BootOrder rewritten as a permutation of its current entries, same byte count, attributes 0x7.</summary>
    Reorder,

    /// <summary>W2: BootNext written with one existing, active, category-Boot entry, 2 bytes, attributes 0x7.</summary>
    SetBootNext,

    /// <summary>W3: BootNext deleted (a write of size 0), the restore of a snapshot that recorded "not set".</summary>
    ClearBootNext,
}

/// <summary>
/// What a caller may ask the pipeline to do. A request names Boot#### entries by number or a
/// snapshot folder; no request type carries a byte array, so "binary blobs are never writable"
/// is a property of the types, not a check. The pipeline builds every byte itself.
/// </summary>
/// <param name="Host">The caller, "cli" or "app"; recorded in the snapshot and the write record.</param>
public abstract record ChangeRequest(string Host)
{
    /// <summary>The <see cref="Host"/> value of the command-line tool.</summary>
    public const string CliHost = "cli";

    /// <summary>The <see cref="Host"/> value of the application.</summary>
    public const string AppHost = "app";

    /// <summary>
    /// The snapshot folder this request was resolved from at stage S0, or null for a request the
    /// caller made directly. A resolved restore runs every stage against the current firmware like
    /// any other request; the validator uses the restore wording when the snapshot's order is no
    /// longer a permutation of the current list, and the plan records the folder.
    /// </summary>
    public string? RestoredFrom { get; init; }
}

/// <summary>W1: put BootOrder's current entries in a new order.</summary>
/// <param name="Host">The caller, "cli" or "app".</param>
/// <param name="NewOrder">The entry numbers in the requested order; each must be a current entry, none twice.</param>
/// <param name="AllowNoChange">
/// True turns rule V7 off so an order equal to the current one is written (the first-write drill
/// of the hardware protocol, CLI --allow-no-change only; the application never sets it).
/// </param>
public sealed record ReorderRequest(string Host, IReadOnlyList<ushort> NewOrder, bool AllowNoChange = false) : ChangeRequest(Host);

/// <summary>W2: boot once from an existing entry at the next start.</summary>
/// <param name="Host">The caller, "cli" or "app".</param>
/// <param name="Target">The entry number of the Boot#### to start once.</param>
public sealed record BootNextRequest(string Host, ushort Target) : ChangeRequest(Host);

/// <summary>W3: remove the one-time boot choice before the restart that would consume it.</summary>
/// <param name="Host">The caller, "cli" or "app".</param>
public sealed record ClearBootNextRequest(string Host) : ChangeRequest(Host);

/// <summary>
/// Restore the value a snapshot recorded. Resolved at stage S0 into a <see cref="ReorderRequest"/>,
/// a <see cref="BootNextRequest"/> or a <see cref="ClearBootNextRequest"/> carrying
/// <see cref="ChangeRequest.RestoredFrom"/>; the validator refuses an unresolved restore.
/// </summary>
/// <param name="Host">The caller, "cli" or "app".</param>
/// <param name="SnapshotFolder">The snapshot folder to restore from.</param>
public sealed record RestoreRequest(string Host, string SnapshotFolder) : ChangeRequest(Host);

/// <summary>The pre-flight alone for any variable (CLI "write check"); it never produces a plan.</summary>
/// <param name="Host">The caller, "cli" or "app".</param>
/// <param name="Name">The variable name.</param>
/// <param name="Guid">The vendor GUID.</param>
public sealed record CheckRequest(string Host, string Name, Guid Guid) : ChangeRequest(Host);
