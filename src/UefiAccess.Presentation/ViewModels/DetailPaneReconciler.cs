// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.ObjectModel;

namespace UefiAccess.Presentation.ViewModels;

/// <summary>
/// Turns a live collection of the detail pane (its sections, or the fields of one section) into
/// the list a fresh pane would have, in place, with the fewest insertions and removals: the
/// update of the detail pane after a row change (finding A1-6, fix 1 of the A16 measurement). An
/// item is identified by a key, its identity and its occurrence among the items with that
/// identity (the first "Dup" pairs with the first "Dup", the second with the second). An item
/// whose key both lists have is kept, as the same object, at the position the wanted list gives
/// it, and is updated from its wanted twin; every other old item is removed, and every other
/// wanted item is inserted as it is, at its position. The kept items are a longest common
/// subsequence of the two key sequences, so insertions plus removals are as few as any edit that
/// keeps the order can make; an item whose key both lists have but whose place is out of that
/// order is removed and its twin inserted, never moved.
/// </summary>
/// <remarks>
/// The collection only ever sees single removals (RemoveAt) and single insertions (Insert), each
/// one Remove or Add notification with its index; it is never cleared, no item is replaced
/// through the indexer and none is moved, so a view bound to it keeps the controls of every kept
/// item and creates or drops only those of the items that really come or go. When the two key
/// sequences are equal (two rows of the same kind), nothing is inserted or removed at all.
/// </remarks>
internal static class DetailPaneReconciler
{
    /// <summary>
    /// Makes <paramref name="current"/> hold the items of <paramref name="wanted"/>, in place (see
    /// the class remarks).
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <typeparam name="TId">The identity type: equal identities (by the default comparer) are the same item.</typeparam>
    /// <param name="current">The live collection; changed in place.</param>
    /// <param name="wanted">The items a fresh build would hold, in order; inserted as they are where no old item has their key.</param>
    /// <param name="identity">The identity of an item.</param>
    /// <param name="update">Updates a kept item (first argument) from its wanted twin (second argument); returns true when anything changed.</param>
    /// <param name="onRemoved">Called with each removed item after its removal; may be null.</param>
    /// <param name="onAdded">Called with each inserted item after its insertion; may be null.</param>
    /// <returns>How many items were kept, how many of those the update changed, how many were inserted and how many removed.</returns>
    internal static Counts Apply<T, TId>(
        ObservableCollection<T> current,
        IReadOnlyList<T> wanted,
        Func<T, TId> identity,
        Func<T, T, bool> update,
        Action<T>? onRemoved = null,
        Action<T>? onAdded = null)
        where T : class
        where TId : notnull
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(wanted);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(update);

        (TId, int)[] oldKeys = Keys(current, identity);
        (TId, int)[] newKeys = Keys(wanted, identity);
        int updated = 0;

        // Two rows of the same kind: the same keys in the same order, so every item is kept where
        // it is and only updated.
        if (oldKeys.AsSpan().SequenceEqual(newKeys))
        {
            for (int j = 0; j < newKeys.Length; j++)
            {
                if (update(current[j], wanted[j]))
                {
                    updated++;
                }
            }

            return new Counts(newKeys.Length, updated, 0, 0);
        }

        bool[] keepOld = new bool[oldKeys.Length];
        bool[] keepNew = new bool[newKeys.Length];
        int kept = MarkKept(oldKeys, newKeys, keepOld, keepNew);

        // Removals first, from the last to the first, so every index is the item's own.
        int removed = 0;
        for (int i = oldKeys.Length - 1; i >= 0; i--)
        {
            if (keepOld[i])
            {
                continue;
            }

            T item = current[i];
            current.RemoveAt(i);
            removed++;
            onRemoved?.Invoke(item);
        }

        // What is left are the kept items, in the wanted order; walking the wanted list, a kept
        // item is already at its position, and every other one is inserted there.
        int added = 0;
        for (int j = 0; j < newKeys.Length; j++)
        {
            if (keepNew[j])
            {
                if (update(current[j], wanted[j]))
                {
                    updated++;
                }

                continue;
            }

            T item = wanted[j];
            current.Insert(j, item);
            added++;
            onAdded?.Invoke(item);
        }

        return new Counts(kept, updated, added, removed);
    }

    /// <summary>Each item's key: its identity and how many items with that identity come before it.</summary>
    private static (TId, int)[] Keys<T, TId>(IReadOnlyList<T> items, Func<T, TId> identity)
        where TId : notnull
    {
        Dictionary<TId, int> seen = new();
        (TId, int)[] keys = new (TId, int)[items.Count];
        for (int i = 0; i < keys.Length; i++)
        {
            TId id = identity(items[i]);
            seen.TryGetValue(id, out int occurrence);
            seen[id] = occurrence + 1;
            keys[i] = (id, occurrence);
        }

        return keys;
    }

    /// <summary>
    /// Marks the items to keep: for each wanted position the old index with the same key (keys are
    /// unique within a list), and of those the longest run whose old indices strictly increase,
    /// the earliest when several are as long. With unique keys this is a longest common
    /// subsequence of the two key sequences. A plain quadratic search: a pane has a few dozen
    /// items at most.
    /// </summary>
    /// <returns>The number of items kept.</returns>
    private static int MarkKept<TId>((TId, int)[] oldKeys, (TId, int)[] newKeys, bool[] keepOld, bool[] keepNew)
        where TId : notnull
    {
        Dictionary<(TId, int), int> oldIndex = new(oldKeys.Length);
        for (int i = 0; i < oldKeys.Length; i++)
        {
            oldIndex[oldKeys[i]] = i;
        }

        int[] match = new int[newKeys.Length];
        for (int j = 0; j < newKeys.Length; j++)
        {
            match[j] = oldIndex.TryGetValue(newKeys[j], out int i) ? i : -1;
        }

        // length[j]: the longest strictly increasing run of matched old indices that ends at j;
        // previous[j]: the position before j in that run (the earliest of equal choices), or -1.
        int[] length = new int[newKeys.Length];
        int[] previous = new int[newKeys.Length];
        int best = -1;
        for (int j = 0; j < newKeys.Length; j++)
        {
            previous[j] = -1;
            if (match[j] < 0)
            {
                continue;
            }

            length[j] = 1;
            for (int p = 0; p < j; p++)
            {
                if (match[p] >= 0 && match[p] < match[j] && length[p] + 1 > length[j])
                {
                    length[j] = length[p] + 1;
                    previous[j] = p;
                }
            }

            if (best < 0 || length[j] > length[best])
            {
                best = j;
            }
        }

        int kept = 0;
        for (int j = best; j >= 0; j = previous[j])
        {
            keepNew[j] = true;
            keepOld[match[j]] = true;
            kept++;
        }

        return kept;
    }

    /// <summary>What <see cref="Apply{T, TId}"/> did.</summary>
    /// <param name="Kept">The items both lists have, kept as the same objects.</param>
    /// <param name="Updated">The kept items whose update reported a change.</param>
    /// <param name="Added">The wanted items inserted.</param>
    /// <param name="Removed">The old items removed.</param>
    internal readonly record struct Counts(int Kept, int Updated, int Added, int Removed);
}
