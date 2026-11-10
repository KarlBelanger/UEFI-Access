// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using UefiAccess.Presentation.ViewModels;
using Xunit;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// The in-place reconcile behind the detail pane's update (<see cref="DetailPaneReconciler"/>,
/// fix 1 of the A16 measurement), on plain items: any list becomes the wanted one, kept items are
/// the old objects, the insertions plus removals are the fewest an order-keeping edit can make
/// (checked against an independent longest-common-subsequence programme), the collection sees
/// only single Add and Remove notifications that replay to the same result, duplicates pair by
/// occurrence, and only kept items are updated.
/// </summary>
public sealed class DetailPaneReconcilerTests
{
    [Fact]
    public void Apply_TurnsAnyListIntoTheWantedOne_WithTheFewestInsertsAndRemoves()
    {
        Random random = new(20261001);
        int structural = 0;
        for (int round = 0; round < 2000; round++)
        {
            List<Item> old = RandomList(random, "old");
            List<Item> wanted = RandomList(random, "new");
            ObservableCollection<Item> current = new(old);
            List<NotifyCollectionChangedEventArgs> changes = new();
            current.CollectionChanged += (_, e) => changes.Add(e);
            List<Item> removedSeen = new();
            List<Item> addedSeen = new();

            DetailPaneReconciler.Counts counts = DetailPaneReconciler.Apply(current, wanted, item => item.Key, Update, removedSeen.Add, addedSeen.Add);

            string context = "round " + round.ToString(CultureInfo.InvariantCulture) + ": [" + Text(old) + "] to [" + Text(wanted) + "]";

            // The keys and the values are the wanted ones.
            Assert.True(wanted.Select(item => item.Key).SequenceEqual(current.Select(item => item.Key)), context);
            Assert.True(wanted.Select(item => item.Value).SequenceEqual(current.Select(item => item.Value)), context);

            // Every item is either an old object (kept) or the wanted object itself (inserted).
            int keptObjects = 0;
            for (int j = 0; j < wanted.Count; j++)
            {
                if (old.Any(item => ReferenceEquals(item, current[j])))
                {
                    keptObjects++;
                }
                else
                {
                    Assert.Same(wanted[j], current[j]);
                }
            }

            Assert.Equal(keptObjects, counts.Kept);
            Assert.Equal(KeyedLcs(old, wanted), counts.Kept);
            Assert.Equal(old.Count - counts.Kept, counts.Removed);
            Assert.Equal(wanted.Count - counts.Kept, counts.Added);
            Assert.Equal(counts.Removed, removedSeen.Count);
            Assert.Equal(counts.Added, addedSeen.Count);
            Assert.All(removedSeen, item => Assert.Contains(old, candidate => ReferenceEquals(candidate, item)));
            Assert.All(removedSeen, item => Assert.DoesNotContain(current, candidate => ReferenceEquals(candidate, item)));

            // Only single Add and Remove notifications, as many as the counts, all removals first,
            // and replaying them on a copy of the old list gives the same list, object for object.
            Assert.All(changes, change => Assert.True(DetailPaneRecorder.IsSingleAddOrRemove(change), context + ": " + change.Action));
            Assert.Equal(counts.Removed, changes.Count(change => change.Action == NotifyCollectionChangedAction.Remove));
            Assert.Equal(counts.Added, changes.Count(change => change.Action == NotifyCollectionChangedAction.Add));
            Assert.True(changes.SkipWhile(change => change.Action == NotifyCollectionChangedAction.Remove).All(change => change.Action == NotifyCollectionChangedAction.Add), context);
            List<Item> replay = new(old);
            foreach (NotifyCollectionChangedEventArgs change in changes)
            {
                if (change.Action == NotifyCollectionChangedAction.Remove)
                {
                    Assert.Same(replay[change.OldStartingIndex], change.OldItems![0]);
                    replay.RemoveAt(change.OldStartingIndex);
                }
                else
                {
                    replay.Insert(change.NewStartingIndex, (Item)change.NewItems![0]!);
                }
            }

            Assert.Equal(current.Count, replay.Count);
            for (int j = 0; j < replay.Count; j++)
            {
                Assert.Same(current[j], replay[j]);
            }

            // Equal key sequences make no structural change at all.
            if (old.Select(item => item.Key).SequenceEqual(wanted.Select(item => item.Key)))
            {
                Assert.Empty(changes);
            }
            else
            {
                structural++;
            }
        }

        Assert.True(structural > 1500, "only " + structural + " of the rounds changed the keys");
    }

    [Fact]
    public void Apply_PairsDuplicatesByOccurrence()
    {
        Item firstA = new("A", 1, "old");
        Item secondA = new("A", 2, "old");
        Item b = new("B", 3, "old");
        ObservableCollection<Item> current = new(new[] { firstA, b, secondA });
        List<NotifyCollectionChangedEventArgs> changes = new();
        current.CollectionChanged += (_, e) => changes.Add(e);

        // The first A of the old list pairs with the first A wanted, the second with the second;
        // B, whose old place is out of that order, is removed and its twin inserted.
        DetailPaneReconciler.Counts counts = DetailPaneReconciler.Apply(current, new[] { new Item("A", 10, "new"), new Item("A", 20, "new"), new Item("B", 3, "new") }, item => item.Key, Update);

        Assert.Same(firstA, current[0]);
        Assert.Same(secondA, current[1]);
        Assert.NotSame(b, current[2]);
        Assert.Equal(new[] { 10, 20, 3 }, current.Select(item => item.Value));
        Assert.Equal(new DetailPaneReconciler.Counts(2, 2, 1, 1), counts);
        Assert.Equal(new[] { NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Add }, changes.Select(change => change.Action));
        Assert.Equal(1, changes[0].OldStartingIndex);
        Assert.Equal(2, changes[1].NewStartingIndex);

        // A third A is a new item: inserted after the two kept ones.
        Item thirdA = new("A", 30, "new");
        changes.Clear();
        counts = DetailPaneReconciler.Apply(current, new[] { new Item("A", 10, "new"), new Item("A", 20, "new"), thirdA, new Item("B", 3, "new") }, item => item.Key, Update);
        Assert.Same(firstA, current[0]);
        Assert.Same(secondA, current[1]);
        Assert.Same(thirdA, current[2]);
        Assert.Equal(new DetailPaneReconciler.Counts(3, 0, 1, 0), counts);
        Assert.Equal(2, Assert.Single(changes).NewStartingIndex);

        // Fewer of them: the last occurrence goes, the first ones stay.
        changes.Clear();
        counts = DetailPaneReconciler.Apply(current, new[] { new Item("A", 10, "new"), new Item("B", 3, "new") }, item => item.Key, Update);
        Assert.Same(firstA, current[0]);
        Assert.Equal(new DetailPaneReconciler.Counts(2, 0, 0, 2), counts);
        Assert.Equal(new[] { 2, 1 }, changes.Select(change => change.OldStartingIndex));
        Assert.Contains(secondA, changes.SelectMany(change => change.OldItems!.Cast<Item>()));
        Assert.Contains(thirdA, changes.SelectMany(change => change.OldItems!.Cast<Item>()));
    }

    [Fact]
    public void Apply_UpdatesOnlyKeptItems_AndCountsRealChanges()
    {
        Item a = new("A", 1, "old");
        Item b = new("B", 1, "old");
        Item c = new("C", 1, "old");
        ObservableCollection<Item> current = new(new[] { a, b, c });
        Item newA = new("A", 1, "new");
        Item newB = new("B", 2, "new");
        Item newD = new("D", 1, "new");
        List<(Item Existing, Item Wanted)> calls = new();

        DetailPaneReconciler.Counts counts = DetailPaneReconciler.Apply(
            current,
            new[] { newA, newB, newD },
            item => item.Key,
            (existing, wanted) =>
            {
                calls.Add((existing, wanted));
                return Update(existing, wanted);
            });

        // A and B are kept and updated from their twins (A unchanged, B changed); C is removed; D is
        // inserted as it is and never updated.
        Assert.Equal(new[] { (a, newA), (b, newB) }, calls);
        Assert.Equal(new DetailPaneReconciler.Counts(2, 1, 1, 1), counts);
        Assert.Equal(new[] { a, b, newD }, current);
        Assert.Equal(2, b.Value);
        Assert.Equal(1, a.Value);

        // The same keys again: the fast path updates in place and changes no structure.
        List<NotifyCollectionChangedEventArgs> changes = new();
        current.CollectionChanged += (_, e) => changes.Add(e);
        counts = DetailPaneReconciler.Apply(current, new[] { new Item("A", 5, "new"), new Item("B", 2, "new"), new Item("D", 1, "new") }, item => item.Key, Update);
        Assert.Equal(new DetailPaneReconciler.Counts(3, 1, 0, 0), counts);
        Assert.Empty(changes);
        Assert.Equal(new[] { a, b, newD }, current);

        // Nothing to nothing, and everything to nothing.
        Assert.Equal(default(DetailPaneReconciler.Counts), DetailPaneReconciler.Apply(new ObservableCollection<Item>(), Array.Empty<Item>(), item => item.Key, Update));
        counts = DetailPaneReconciler.Apply(current, Array.Empty<Item>(), item => item.Key, Update);
        Assert.Equal(new DetailPaneReconciler.Counts(0, 0, 0, 3), counts);
        Assert.Empty(current);
        Assert.Equal(new[] { 2, 1, 0 }, changes.Select(change => change.OldStartingIndex));
    }

    private static bool Update(Item existing, Item wanted)
    {
        if (existing.Value == wanted.Value)
        {
            return false;
        }

        existing.Value = wanted.Value;
        return true;
    }

    private static List<Item> RandomList(Random random, string origin)
    {
        int length = random.Next(0, 13);
        List<Item> items = new(length);
        for (int i = 0; i < length; i++)
        {
            items.Add(new Item(((char)('A' + random.Next(6))).ToString(), random.Next(3), origin));
        }

        return items;
    }

    /// <summary>The length of a longest common subsequence of the two lists' keys (identity and occurrence), by the textbook O(n·m) programme.</summary>
    private static int KeyedLcs(List<Item> a, List<Item> b)
    {
        List<string> keysA = Keys(a);
        List<string> keysB = Keys(b);
        int[,] table = new int[keysA.Count + 1, keysB.Count + 1];
        for (int i = keysA.Count - 1; i >= 0; i--)
        {
            for (int j = keysB.Count - 1; j >= 0; j--)
            {
                table[i, j] = keysA[i] == keysB[j] ? table[i + 1, j + 1] + 1 : Math.Max(table[i + 1, j], table[i, j + 1]);
            }
        }

        return table[0, 0];
    }

    private static List<string> Keys(List<Item> items)
    {
        Dictionary<string, int> seen = new(StringComparer.Ordinal);
        List<string> keys = new(items.Count);
        foreach (Item item in items)
        {
            seen.TryGetValue(item.Key, out int occurrence);
            seen[item.Key] = occurrence + 1;
            keys.Add(item.Key + "#" + occurrence.ToString(CultureInfo.InvariantCulture));
        }

        return keys;
    }

    private static string Text(List<Item> items) => string.Join(" ", items.Select(item => item.Key + item.Value.ToString(CultureInfo.InvariantCulture)));

    /// <summary>A plain item: a key, a value the update copies, and where it was made.</summary>
    private sealed class Item
    {
        public Item(string key, int value, string origin)
        {
            Key = key;
            Value = value;
            Origin = origin;
        }

        public string Key { get; }

        public int Value { get; set; }

        public string Origin { get; }

        public override string ToString() => Origin + " " + Key + Value.ToString(CultureInfo.InvariantCulture);
    }
}
