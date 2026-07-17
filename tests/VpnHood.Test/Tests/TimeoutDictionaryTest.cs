using System.Collections.Concurrent;
using VpnHood.Core.Toolkit.Collections;
using VpnHood.Core.Toolkit.Utils;
// ReSharper disable AccessToDisposedClosure

namespace VpnHood.Test.Tests;

[TestClass]
public class TimeoutDictionaryTest
{
    private static readonly TimeSpan LongTimeout = TimeSpan.FromMinutes(10);

    // dispose counting uses Interlocked and the timestamp uses volatile ticks, so the stress
    // oracles below reason over well-ordered reads instead of racy plain fields
    private class TrackedItem : ITimeoutItem
    {
        private int _disposeCount;
        private long _lastUsedTicks;
        public volatile bool WasBackdated;

        public DateTime LastUsedTime {
            get => new(Volatile.Read(ref _lastUsedTicks));
            set => Volatile.Write(ref _lastUsedTicks, value.Ticks);
        }

        public int DisposeCount => Volatile.Read(ref _disposeCount);
        public bool IsDisposed => DisposeCount > 0;

        public virtual void Dispose()
        {
            Interlocked.Increment(ref _disposeCount);
        }

        public void Backdate()
        {
            // WasBackdated must be visible before the expired timestamp, so an eviction implies
            // the flag is observable to whoever sees the disposal
            WasBackdated = true;
            LastUsedTime = FastDateTime.Now - LongTimeout - TimeSpan.FromHours(1);
        }
    }

    // equality is deliberately broken to prove eviction goes by instance identity, not Equals
    private class AlwaysEqualItem : TrackedItem
    {
        public override bool Equals(object? obj) => obj is AlwaysEqualItem;
        public override int GetHashCode() => 42;
    }

    // dispose still counts, then throws; the dictionary must swallow it and keep draining
    private class ThrowingDisposeItem : TrackedItem
    {
        public override void Dispose()
        {
            base.Dispose();
            throw new InvalidOperationException("dispose failed on purpose");
        }
    }

    [TestMethod]
    public void GetOrAdd_should_return_existing_live_item()
    {
        using var dictionary = new TimeoutDictionary<int, TrackedItem>(LongTimeout);
        var item = dictionary.GetOrAdd(1, _ => new TrackedItem());
        var again = dictionary.GetOrAdd(1, _ => throw new AssertFailedException("factory must not run for a live item"));

        Assert.AreSame(item, again);
        Assert.AreEqual(0, item.DisposeCount);
    }

    [TestMethod]
    public void GetOrAdd_should_replace_expired_item_and_dispose_old_exactly_once()
    {
        using var dictionary = new TimeoutDictionary<int, TrackedItem>(LongTimeout) { AutoCleanup = false };
        var oldItem = dictionary.GetOrAdd(1, _ => new TrackedItem());
        oldItem.Backdate();

        var newItem = dictionary.GetOrAdd(1, _ => new TrackedItem());
        Assert.AreNotSame(oldItem, newItem);
        Assert.AreEqual(1, oldItem.DisposeCount);
        Assert.AreEqual(0, newItem.DisposeCount);
        Assert.IsTrue(dictionary.TryGetValue(1, out var current));
        Assert.AreSame(newItem, current);
    }

    [TestMethod]
    public void GetOrAdd_should_not_dispose_item_when_factory_returns_the_mapped_instance()
    {
        using var dictionary = new TimeoutDictionary<int, TrackedItem>(LongTimeout) { AutoCleanup = false };
        var item = dictionary.GetOrAdd(1, _ => new TrackedItem());
        item.LastUsedTime = FastDateTime.Now - LongTimeout - TimeSpan.FromHours(1);

        var returned = dictionary.GetOrAdd(1, _ => item);
        Assert.AreSame(item, returned);
        Assert.AreEqual(0, item.DisposeCount);
        Assert.IsTrue(dictionary.TryGetValue(1, out _), "the refreshed instance must be live again");
    }

    [TestMethod]
    public void TryGetValue_should_evict_expired_item_and_dispose_exactly_once()
    {
        using var dictionary = new TimeoutDictionary<int, TrackedItem>(LongTimeout) { AutoCleanup = false };
        var item = dictionary.GetOrAdd(1, _ => new TrackedItem());
        item.Backdate();

        Assert.IsFalse(dictionary.TryGetValue(1, out _));
        Assert.AreEqual(1, item.DisposeCount);
        Assert.AreEqual(0, dictionary.Count);
    }

    [TestMethod]
    public void TryAdd_should_fail_on_live_item_and_leave_the_incoming_value_untouched()
    {
        using var dictionary = new TimeoutDictionary<int, TrackedItem>(LongTimeout);
        var mapped = dictionary.GetOrAdd(1, _ => new TrackedItem());

        var incoming = new TrackedItem();
        Assert.IsFalse(dictionary.TryAdd(1, incoming));
        Assert.AreEqual(0, incoming.DisposeCount);
        Assert.AreEqual(default, incoming.LastUsedTime, "a rejected value must not be stamped");
        Assert.IsTrue(dictionary.TryGetValue(1, out var current));
        Assert.AreSame(mapped, current);
    }

    [TestMethod]
    public void TryAdd_should_replace_expired_item()
    {
        using var dictionary = new TimeoutDictionary<int, TrackedItem>(LongTimeout) { AutoCleanup = false };
        var oldItem = dictionary.GetOrAdd(1, _ => new TrackedItem());
        oldItem.Backdate();

        var incoming = new TrackedItem();
        Assert.IsTrue(dictionary.TryAdd(1, incoming));
        Assert.AreEqual(1, oldItem.DisposeCount);
        Assert.IsTrue(dictionary.TryGetValue(1, out var current));
        Assert.AreSame(incoming, current);
    }

    [TestMethod]
    public void AddOrUpdate_should_replace_and_dispose_the_old_item()
    {
        using var dictionary = new TimeoutDictionary<int, TrackedItem>(LongTimeout);
        var oldItem = dictionary.AddOrUpdate(1, new TrackedItem());
        var newItem = new TrackedItem();

        Assert.AreSame(newItem, dictionary.AddOrUpdate(1, newItem));
        Assert.AreEqual(1, oldItem.DisposeCount);
        Assert.AreEqual(0, newItem.DisposeCount);
    }

    [TestMethod]
    public void AddOrUpdate_with_the_same_instance_should_not_dispose_it()
    {
        using var dictionary = new TimeoutDictionary<int, TrackedItem>(LongTimeout);
        var item = new TrackedItem();
        dictionary.AddOrUpdate(1, item);
        dictionary.AddOrUpdate(1, item);

        Assert.AreEqual(0, item.DisposeCount);
        Assert.IsTrue(dictionary.TryGetValue(1, out var current));
        Assert.AreSame(item, current);
    }

    [TestMethod]
    public void TryRemove_should_dispose_the_removed_value()
    {
        using var dictionary = new TimeoutDictionary<int, TrackedItem>(LongTimeout);
        var item = dictionary.GetOrAdd(1, _ => new TrackedItem());

        Assert.IsTrue(dictionary.TryRemove(1, out var removed));
        Assert.AreSame(item, removed);
        Assert.AreEqual(1, item.DisposeCount);
        Assert.IsFalse(dictionary.TryRemove(1, out _));
    }

    [TestMethod]
    public void Eviction_should_use_instance_identity_even_when_equals_is_overridden()
    {
        using var dictionary = new TimeoutDictionary<int, AlwaysEqualItem>(LongTimeout) { AutoCleanup = false };
        var oldItem = dictionary.GetOrAdd(1, _ => new AlwaysEqualItem());
        oldItem.Backdate();

        var newItem = dictionary.GetOrAdd(1, _ => new AlwaysEqualItem());
        Assert.AreEqual(1, oldItem.DisposeCount);
        Assert.AreEqual(0, newItem.DisposeCount);
        Assert.IsTrue(dictionary.TryGetValue(1, out var current));
        Assert.AreSame(newItem, current);
    }

    [TestMethod]
    public void Insert_should_stamp_LastUsedTime_before_publication()
    {
        using var dictionary = new TimeoutDictionary<int, TrackedItem>(LongTimeout);

        // an unstamped item reads as expired (LastUsedTime == default); it must be stamped before
        // it becomes observable, or a concurrent reader could evict it right away
        var item = new TrackedItem();
        Assert.IsTrue(dictionary.TryAdd(1, item));
        Assert.AreNotEqual(default, item.LastUsedTime);
        Assert.IsTrue(dictionary.TryGetValue(1, out _));
        Assert.AreEqual(0, item.DisposeCount);
    }

    [TestMethod]
    public void Cleanup_force_should_sweep_only_expired_items()
    {
        using var dictionary = new TimeoutDictionary<int, TrackedItem>(LongTimeout) { AutoCleanup = false };
        var live = dictionary.GetOrAdd(1, _ => new TrackedItem());
        var expired = dictionary.GetOrAdd(2, _ => new TrackedItem());
        expired.Backdate();

        dictionary.Cleanup(force: true);
        Assert.AreEqual(1, dictionary.Count);
        Assert.AreEqual(0, live.DisposeCount);
        Assert.AreEqual(1, expired.DisposeCount);
    }

    [TestMethod]
    public void Cleanup_unforced_should_be_gated_after_the_first_sweep()
    {
        using var dictionary = new TimeoutDictionary<int, TrackedItem>(LongTimeout) { AutoCleanup = false };
        dictionary.Cleanup(); // consume the initially open gate

        var expired = dictionary.GetOrAdd(1, _ => new TrackedItem());
        expired.Backdate();

        dictionary.Cleanup();
        Assert.AreEqual(1, dictionary.Count, "an unforced cleanup within the gate window must not sweep");
        dictionary.Cleanup(force: true);
        Assert.AreEqual(0, dictionary.Count);
    }

    [TestMethod]
    public async Task Count_should_run_the_gated_cleanup()
    {
        // pins the retained Count semantics: reading Count is enough to eventually sweep expired
        // items without any explicit Cleanup call
        using var dictionary = new TimeoutDictionary<int, TrackedItem>(TimeSpan.FromMilliseconds(90));
        var item = dictionary.GetOrAdd(1, _ => new TrackedItem());
        item.WasBackdated = true;
        item.LastUsedTime = FastDateTime.Now - TimeSpan.FromHours(1);

        for (var i = 0; i < 300 && dictionary.Count > 0; i++)
            await Task.Delay(10);

        Assert.AreEqual(0, dictionary.Count);
        Assert.AreEqual(1, item.DisposeCount);
    }

    [TestMethod]
    public void Null_timeout_should_never_expire_items_but_disposed_items_are_still_evicted()
    {
        using var dictionary = new TimeoutDictionary<int, TrackedItem>();
        var item = dictionary.GetOrAdd(1, _ => new TrackedItem());
        item.LastUsedTime = FastDateTime.Now - TimeSpan.FromDays(365);
        Assert.IsTrue(dictionary.TryGetValue(1, out _), "without a timeout an old item must stay live");

        item.Dispose();
        Assert.IsFalse(dictionary.TryGetValue(1, out _), "a disposed item must be treated as expired");
        Assert.AreEqual(0, dictionary.Count);
    }

    [TestMethod]
    public void Dispose_should_dispose_all_items_and_mutators_must_fail_afterward()
    {
        var dictionary = new TimeoutDictionary<int, TrackedItem>(LongTimeout);
        var item1 = dictionary.GetOrAdd(1, _ => new TrackedItem());
        var item2 = dictionary.GetOrAdd(2, _ => new TrackedItem());

        dictionary.Dispose();
        dictionary.Dispose(); // must be idempotent
        Assert.AreEqual(1, item1.DisposeCount);
        Assert.AreEqual(1, item2.DisposeCount);
        Assert.AreEqual(0, dictionary.Count);

        Assert.ThrowsExactly<ObjectDisposedException>(() => dictionary.GetOrAdd(3, _ => new TrackedItem()));
        Assert.ThrowsExactly<ObjectDisposedException>(() => dictionary.AddOrUpdate(3, new TrackedItem()));

        var incoming = new TrackedItem();
        Assert.IsFalse(dictionary.TryAdd(3, incoming));
        Assert.AreEqual(0, incoming.DisposeCount, "a rejected value stays caller-owned");
        Assert.IsFalse(dictionary.TryGetValue(1, out _));
    }

    [TestMethod]
    public void Dispose_should_drain_every_entry_even_when_a_value_dispose_throws()
    {
        var dictionary = new TimeoutDictionary<int, TrackedItem>(LongTimeout);
        var items = new List<TrackedItem>();
        for (var i = 0; i < 10; i++) {
            var j = i;
            items.Add(dictionary.GetOrAdd(i, _ => j % 2 == 0 
                ? new ThrowingDisposeItem() : new TrackedItem()));
        }

        dictionary.Dispose(); // must neither throw nor stop at the first throwing value
        Assert.AreEqual(0, dictionary.Count);
        Assert.IsTrue(items.All(x => x.DisposeCount == 1), "every entry must be drained and disposed once");
    }

    [TestMethod]
    public void Replacement_should_succeed_even_when_the_old_value_dispose_throws()
    {
        using var dictionary = new TimeoutDictionary<int, TrackedItem>(LongTimeout) { AutoCleanup = false };
        var oldItem = dictionary.GetOrAdd(1, _ => new ThrowingDisposeItem());
        oldItem.Backdate();

        var newItem = dictionary.GetOrAdd(1, _ => new TrackedItem());
        Assert.AreEqual(1, oldItem.DisposeCount);
        Assert.AreEqual(0, newItem.DisposeCount);
        Assert.IsTrue(dictionary.TryGetValue(1, out var current));
        Assert.AreSame(newItem, current);
    }

    [TestMethod]
    public void RemoveAll_should_dispose_all_items_and_keep_the_dictionary_usable()
    {
        using var dictionary = new TimeoutDictionary<int, TrackedItem>(LongTimeout);
        var item = dictionary.GetOrAdd(1, _ => new TrackedItem());

        dictionary.RemoveAll();
        Assert.AreEqual(1, item.DisposeCount);
        Assert.AreEqual(0, dictionary.Count);

        var newItem = dictionary.GetOrAdd(1, _ => new TrackedItem());
        Assert.AreEqual(0, newItem.DisposeCount);
        Assert.AreEqual(1, dictionary.Count);
    }

    [TestMethod]
    public void Stress_a_value_may_be_disposed_only_after_it_was_backdated()
    {
        // the eviction-race oracle: with a huge timeout, an item can only expire by an explicit
        // backdate, so a disposed value that was never backdated proves an eviction removed a
        // fresh replacement (the old key-only removal bug)
        using var dictionary = new TimeoutDictionary<int, TrackedItem>(LongTimeout);
        var ledger = new ConcurrentBag<TrackedItem>();
        var violations = new ConcurrentBag<string>();

        RunThreads(threadCount: 8, iterations: 30_000, () => {
            var key = Random.Shared.Next(8);
            switch (Random.Shared.Next(3)) {
                case 0:
                    var value = dictionary.GetOrAdd(key, _ => {
                        var item = new TrackedItem();
                        ledger.Add(item);
                        return item;
                    });
                    if (value is { DisposeCount: > 0, WasBackdated: false })
                        violations.Add("GetOrAdd returned a disposed value that was never backdated");
                    break;

                case 1:
                    if (dictionary.TryGetValue(key, out var live) && live is { DisposeCount: > 0, WasBackdated: false })
                        violations.Add("TryGetValue returned a disposed value that was never backdated");
                    break;

                case 2:
                    if (dictionary.TryGetValue(key, out var victim))
                        victim.Backdate();
                    break;
            }
        });

        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations.Distinct()));
        dictionary.Dispose();
        AssertAllDisposedExactlyOnce(ledger);
    }

    [TestMethod]
    public void Stress_every_created_item_should_be_disposed_exactly_once()
    {
        using var dictionary = new TimeoutDictionary<int, TrackedItem>(LongTimeout);
        var ledger = new ConcurrentBag<TrackedItem>();

        RunThreads(threadCount: 8, iterations: 30_000, () => {
            var key = Random.Shared.Next(8);
            switch (Random.Shared.Next(5)) {
                case 0:
                    dictionary.GetOrAdd(key, _ => {
                        var item = new TrackedItem();
                        ledger.Add(item);
                        return item;
                    });
                    break;

                case 1:
                    dictionary.TryGetValue(key, out _);
                    break;

                case 2:
                    dictionary.TryRemove(key, out _);
                    break;

                case 3:
                    var updated = new TrackedItem();
                    ledger.Add(updated);
                    dictionary.AddOrUpdate(key, updated);
                    break;

                case 4:
                    var added = new TrackedItem();
                    ledger.Add(added);
                    if (!dictionary.TryAdd(key, added))
                        added.Dispose(); // rejected values stay caller-owned
                    break;
            }
        });

        dictionary.Dispose();
        AssertAllDisposedExactlyOnce(ledger);
    }

    [TestMethod]
    public void Stress_dispose_racing_mutators_should_not_leak_any_item()
    {
        var dictionary = new TimeoutDictionary<int, TrackedItem>(LongTimeout);
        var ledger = new ConcurrentBag<TrackedItem>();
        using var disposeSignal = new ManualResetEventSlim();

        var threads = RunThreadsAsync(threadCount: 8, iterations: 20_000, index => {
            if (index == 5_000)
                disposeSignal.Set();

            var key = Random.Shared.Next(8);
            try {
                switch (Random.Shared.Next(3)) {
                    case 0:
                        dictionary.GetOrAdd(key, _ => {
                            var item = new TrackedItem();
                            ledger.Add(item);
                            return item;
                        });
                        break;

                    case 1:
                        var added = new TrackedItem();
                        ledger.Add(added);
                        if (!dictionary.TryAdd(key, added))
                            added.Dispose();
                        break;

                    case 2:
                        var updated = new TrackedItem();
                        ledger.Add(updated);
                        try {
                            dictionary.AddOrUpdate(key, updated);
                        }
                        catch (ObjectDisposedException) {
                            updated.Dispose(); // ownership never transferred
                            throw;
                        }

                        break;
                }
            }
            catch (ObjectDisposedException) {
                // expected while racing Dispose
            }
        });

        disposeSignal.Wait(TimeSpan.FromSeconds(30));
        dictionary.Dispose();
        Task.WaitAll(threads);

        Assert.AreEqual(0, dictionary.Count);
        AssertAllDisposedExactlyOnce(ledger);
    }

    [TestMethod]
    public void Stress_concurrent_forced_cleanups_should_keep_disposal_exactly_once()
    {
        using var dictionary = new TimeoutDictionary<int, TrackedItem>(LongTimeout) { AutoCleanup = false };
        var ledger = new ConcurrentBag<TrackedItem>();

        RunThreads(threadCount: 8, iterations: 5_000, () => {
            var key = Random.Shared.Next(32);
            switch (Random.Shared.Next(3)) {
                case 0:
                    var value = dictionary.GetOrAdd(key, _ => {
                        var item = new TrackedItem();
                        ledger.Add(item);
                        return item;
                    });
                    if (Random.Shared.Next(4) == 0)
                        value.Backdate();
                    break;

                case 1:
                    dictionary.Cleanup(force: true);
                    break;

                case 2:
                    dictionary.TryGetValue(key, out _);
                    break;
            }
        });

        dictionary.Dispose();
        AssertAllDisposedExactlyOnce(ledger);
    }

    private static void RunThreads(int threadCount, int iterations, Action action)
    {
        Task.WaitAll(RunThreadsAsync(threadCount, iterations, _ => action()));
    }

    private static Task[] RunThreadsAsync(int threadCount, int iterations, Action<int> action)
    {
        var tasks = new Task[threadCount];
        for (var i = 0; i < threadCount; i++)
            tasks[i] = Task.Run(() => {
                for (var j = 0; j < iterations; j++)
                    action(j);
            });

        return tasks;
    }

    private static void AssertAllDisposedExactlyOnce(ConcurrentBag<TrackedItem> ledger)
    {
        Assert.AreNotEqual(0, ledger.Count, "the stress loop must have created items");
        var notDisposed = ledger.Count(x => x.DisposeCount == 0);
        var multiDisposed = ledger.Count(x => x.DisposeCount > 1);
        Assert.AreEqual(0, notDisposed, $"{notDisposed} of {ledger.Count} items were never disposed (leak)");
        Assert.AreEqual(0, multiDisposed, $"{multiDisposed} of {ledger.Count} items were disposed more than once");
    }
}
