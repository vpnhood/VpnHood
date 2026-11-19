using System.Collections;
using Microsoft.Data.Sqlite;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Proxies.Management.Sqlite;

namespace VpnHood.Test.Tests;

[TestClass]
public class ProxyEndPointStoreTest : TestBase
{
    // the store's upsert with its usual flags, under the test's token
    private Task Upsert(IProxyEndPointStore store, IReadOnlyList<ProxyEndPointRecord> records,
        bool keepExistingStatus = true, bool keepExistingEnabled = false)
    {
        return store.Upsert(records, keepExistingStatus, keepExistingEnabled, TestCt);
    }

    private string NewDbPath([System.Runtime.CompilerServices.CallerMemberName] string name = "") =>
        Path.Combine(TestHelper.WorkingPath, "proxies", $"{name}.db");

    private static ProxyEndPoint CreateEndPoint(int index, ProxyProtocol protocol = ProxyProtocol.Socks5) =>
        new() {
            Protocol = protocol,
            Host = $"proxy{index}.example.com",
            Port = 1080 + index
        };

    [TestMethod]
    public async Task Roundtrip_all_fields()
    {
        using var store = new ProxyEndPointStore(NewDbPath());

        var endPoint = new ProxyEndPoint {
            Protocol = ProxyProtocol.Https,
            Host = "proxy.example.com",
            Port = 8443,
            Username = "user",
            Password = "pass",
            IsEnabled = false
        };
        var status = new ProxyEndPointStatus {
            Penalty = 5,
            SucceededCount = 7,
            FailedCount = 3,
            Latency = TimeSpan.FromMilliseconds(123),
            LastSucceeded = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            LastFailed = new DateTime(2026, 1, 1, 1, 1, 1, DateTimeKind.Utc),
            ErrorMessage = "some error",
            QueuePosition = 42
        };

        await Upsert(store, [
            new ProxyEndPointRecord { EndPoint = endPoint, Status = status, CountryCode = "US" }
        ], keepExistingStatus: false);

        var record = await store.Get(endPoint.Id, TestCt);
        Assert.IsNotNull(record);
        Assert.AreEqual(endPoint.Id, record.EndPoint.Id);
        Assert.AreEqual(ProxyProtocol.Https, record.EndPoint.Protocol);
        Assert.AreEqual("proxy.example.com", record.EndPoint.Host);
        Assert.AreEqual(8443, record.EndPoint.Port);
        Assert.AreEqual("user", record.EndPoint.Username);
        Assert.AreEqual("pass", record.EndPoint.Password);
        Assert.IsFalse(record.EndPoint.IsEnabled);
        Assert.AreEqual("US", record.CountryCode);
        Assert.AreEqual(5, record.Status.Penalty);
        Assert.AreEqual(7, record.Status.SucceededCount);
        Assert.AreEqual(3, record.Status.FailedCount);
        Assert.AreEqual(TimeSpan.FromMilliseconds(123), record.Status.Latency);
        Assert.AreEqual(status.LastSucceeded, record.Status.LastSucceeded);
        Assert.AreEqual(status.LastFailed, record.Status.LastFailed);
        Assert.AreEqual("some error", record.Status.ErrorMessage);
        Assert.AreEqual(42, record.Status.QueuePosition);
    }

    [TestMethod]
    public async Task Upsert_preserves_status_on_conflict()
    {
        using var store = new ProxyEndPointStore(NewDbPath());
        var endPoint = CreateEndPoint(1);

        await Upsert(store, [
            new ProxyEndPointRecord {
                EndPoint = endPoint,
                Status = new ProxyEndPointStatus { SucceededCount = 9, Penalty = 2 }
            }
        ], keepExistingStatus: false);

        // upsert the same endpoint with new credentials; the status must survive
        var updated = new ProxyEndPoint {
            Protocol = endPoint.Protocol,
            Host = endPoint.Host,
            Port = endPoint.Port,
            Username = "newUser"
        };
        await Upsert(store, [new ProxyEndPointRecord { EndPoint = updated }]);

        var record = await store.Get(endPoint.Id, TestCt);
        Assert.IsNotNull(record);
        Assert.AreEqual("newUser", record.EndPoint.Username);
        Assert.AreEqual(9, record.Status.SucceededCount);
        Assert.AreEqual(2, record.Status.Penalty);
    }

    [TestMethod]
    public async Task Upsert_keepExistingEnabled_preserves_enabled_state()
    {
        using var store = new ProxyEndPointStore(NewDbPath());
        var endPoint = CreateEndPoint(1);
        endPoint.IsEnabled = false;
        await Upsert(store, [new ProxyEndPointRecord { EndPoint = endPoint }]);

        var enabledAgain = CreateEndPoint(1); // same natural key, IsEnabled=true
        await Upsert(store, [new ProxyEndPointRecord { EndPoint = enabledAgain }], keepExistingEnabled: true);
        Assert.IsFalse((await store.Get(endPoint.Id, TestCt))!.EndPoint.IsEnabled);

        await Upsert(store, [new ProxyEndPointRecord { EndPoint = enabledAgain }]);
        Assert.IsTrue((await store.Get(endPoint.Id, TestCt))!.EndPoint.IsEnabled);
    }

    [TestMethod]
    public async Task UpdateStatuses_does_not_resurrect_deleted_rows()
    {
        using var store = new ProxyEndPointStore(NewDbPath());
        var endPoint1 = CreateEndPoint(1);
        var endPoint2 = CreateEndPoint(2);
        await Upsert(store, [
            new ProxyEndPointRecord { EndPoint = endPoint1 },
            new ProxyEndPointRecord { EndPoint = endPoint2 }
        ]);

        // delete one row, then update statuses of both (as the core flush would)
        await store.Delete([endPoint1.Id], TestCt);
        await store.UpdateStatuses([
            new ProxyEndPointInfo { EndPoint = endPoint1, Status = new ProxyEndPointStatus { SucceededCount = 1 } },
            new ProxyEndPointInfo { EndPoint = endPoint2, Status = new ProxyEndPointStatus { SucceededCount = 2 } }
        ], TestCt);

        Assert.IsNull(await store.Get(endPoint1.Id, TestCt));
        Assert.AreEqual(2, (await store.Get(endPoint2.Id, TestCt))!.Status.SucceededCount);
        Assert.AreEqual(1, await store.Count(TestCt));
    }

    [TestMethod]
    public async Task DeleteAll_respects_category_filters()
    {
        using var store = new ProxyEndPointStore(NewDbPath());

        var succeeded = CreateEndPoint(1);
        var failed = CreateEndPoint(2);
        var unknown = CreateEndPoint(3);
        var disabled = CreateEndPoint(4);
        disabled.IsEnabled = false;

        await Upsert(store, [
            new ProxyEndPointRecord {
                EndPoint = succeeded,
                Status = new ProxyEndPointStatus { SucceededCount = 1, LastSucceeded = DateTime.UtcNow }
            },
            new ProxyEndPointRecord {
                EndPoint = failed,
                Status = new ProxyEndPointStatus { FailedCount = 1, LastFailed = DateTime.UtcNow }
            },
            new ProxyEndPointRecord { EndPoint = unknown },
            new ProxyEndPointRecord { EndPoint = disabled }
        ], keepExistingStatus: false);

        // delete only failed; disabled (unused) also matches the unknown category rules of the old
        // filter chain, so keep unknown and disabled explicitly
        await store.DeleteAll(new DeleteAllOptions {
            DeleteSucceeded = false,
            DeleteFailed = true,
            DeleteUnknown = false,
            DeleteDisabled = false
        }, TestCt);

        Assert.IsNotNull(await store.Get(succeeded.Id, TestCt));
        Assert.IsNull(await store.Get(failed.Id, TestCt));
        Assert.IsNotNull(await store.Get(unknown.Id, TestCt));
        Assert.IsNotNull(await store.Get(disabled.Id, TestCt));

        // delete everything
        await store.DeleteAll(new DeleteAllOptions(), TestCt);
        Assert.AreEqual(0, await store.Count(TestCt));
    }

    [TestMethod]
    public async Task DisableAllFailed_disables_only_failed()
    {
        using var store = new ProxyEndPointStore(NewDbPath());
        var succeeded = CreateEndPoint(1);
        var failed = CreateEndPoint(2);
        var unknown = CreateEndPoint(3);

        await Upsert(store, [
            new ProxyEndPointRecord {
                EndPoint = succeeded,
                Status = new ProxyEndPointStatus { SucceededCount = 1, LastSucceeded = DateTime.UtcNow }
            },
            new ProxyEndPointRecord {
                EndPoint = failed,
                Status = new ProxyEndPointStatus { FailedCount = 1, LastFailed = DateTime.UtcNow }
            },
            new ProxyEndPointRecord { EndPoint = unknown }
        ], keepExistingStatus: false);

        await store.DisableAllFailed(TestCt);

        Assert.IsTrue((await store.Get(succeeded.Id, TestCt))!.EndPoint.IsEnabled);
        Assert.IsFalse((await store.Get(failed.Id, TestCt))!.EndPoint.IsEnabled);
        Assert.IsTrue((await store.Get(unknown.Id, TestCt))!.EndPoint.IsEnabled);
    }

    [TestMethod]
    public async Task ResetStatuses_zeros_all_statuses_and_queue_position()
    {
        using var store = new ProxyEndPointStore(NewDbPath());
        var endPoint = CreateEndPoint(1);
        await Upsert(store, [
            new ProxyEndPointRecord {
                EndPoint = endPoint,
                Status = new ProxyEndPointStatus {
                    Penalty = 3, SucceededCount = 5, FailedCount = 2,
                    Latency = TimeSpan.FromMilliseconds(10),
                    LastSucceeded = DateTime.UtcNow, ErrorMessage = "err", QueuePosition = 9
                }
            }
        ], keepExistingStatus: false);
        await store.SetQueuePosition(123, TestCt);

        await store.ResetStatuses(TestCt);

        var record = await store.Get(endPoint.Id, TestCt);
        Assert.IsNotNull(record);
        Assert.AreEqual(0, record.Status.Penalty);
        Assert.AreEqual(0, record.Status.SucceededCount);
        Assert.AreEqual(0, record.Status.FailedCount);
        Assert.IsNull(record.Status.Latency);
        Assert.IsNull(record.Status.LastSucceeded);
        Assert.IsNull(record.Status.ErrorMessage);
        Assert.AreEqual(0, record.Status.QueuePosition);
        Assert.AreEqual(0, await store.GetQueuePosition(TestCt));
    }

    [TestMethod]
    public async Task QueuePosition_meta_roundtrip()
    {
        using var store = new ProxyEndPointStore(NewDbPath());
        Assert.AreEqual(0, await store.GetQueuePosition(TestCt));
        await store.SetQueuePosition(1234567890123, TestCt);
        Assert.AreEqual(1234567890123, await store.GetQueuePosition(TestCt));
    }

    [TestMethod]
    public async Task Merge_keeps_status_prunes_and_removes_duplicates()
    {
        using var store = new ProxyEndPointStore(NewDbPath());

        // a good used endpoint that also exists in the incoming list
        var goodUsed = CreateEndPoint(1);
        // a bad endpoint above max penalty
        var bad = CreateEndPoint(2);
        await Upsert(store, [
            new ProxyEndPointRecord {
                EndPoint = goodUsed,
                Status = new ProxyEndPointStatus { SucceededCount = 3, LastSucceeded = DateTime.UtcNow, Penalty = 1 }
            },
            new ProxyEndPointRecord {
                EndPoint = bad,
                Status = new ProxyEndPointStatus { FailedCount = 9, LastFailed = DateTime.UtcNow, Penalty = 100 }
            }
        ], keepExistingStatus: false);

        // merge a new list: the good one (duplicate) + two new endpoints. The duplicate is the stored endpoint, one
        // slot, so cap 3 admits both new endpoints while the bad row falls off the end.
        var newEndPoints = new[] { CreateEndPoint(1), CreateEndPoint(3), CreateEndPoint(4) };
        await store.Merge(newEndPoints, maxItemCount: 3, maxPenalty: 50, removeDuplicateIps: false, cancellationToken: TestCt);

        var records = await store.List(TestCt);
        Assert.HasCount(3, records);

        // used-good comes first in the merge priority, then new ones; the bad one is pruned by the cap
        Assert.IsNotNull(await store.Get(goodUsed.Id, TestCt));
        Assert.IsNotNull(await store.Get(CreateEndPoint(3).Id, TestCt));
        Assert.IsNotNull(await store.Get(CreateEndPoint(4).Id, TestCt));
        Assert.IsNull(await store.Get(bad.Id, TestCt));

        // the surviving used endpoint keeps its status
        Assert.AreEqual(3, (await store.Get(goodUsed.Id, TestCt))!.Status.SucceededCount);
    }

    [TestMethod]
    public async Task Query_filters_orders_and_pages_in_sql()
    {
        using var store = new ProxyEndPointStore(NewDbPath());

        var succeeded = CreateEndPoint(1);
        var failed = CreateEndPoint(2);
        var unknown = CreateEndPoint(3);
        var disabled = CreateEndPoint(4);
        disabled.IsEnabled = false;

        await Upsert(store, [
            new ProxyEndPointRecord {
                EndPoint = succeeded,
                Status = new ProxyEndPointStatus { SucceededCount = 1, LastSucceeded = DateTime.UtcNow },
                CountryCode = "US"
            },
            new ProxyEndPointRecord {
                EndPoint = failed,
                Status = new ProxyEndPointStatus { FailedCount = 1, LastFailed = DateTime.UtcNow, Penalty = 200 }
            },
            new ProxyEndPointRecord { EndPoint = unknown },
            new ProxyEndPointRecord { EndPoint = disabled }
        ], keepExistingStatus: false);

        // category filters
        var result = await store.List(new ProxyEndPointStoreListParams { IncludeFailed = false, IncludeUnknown = false }, TestCt);
        Assert.AreEqual(1, result.TotalCount);
        Assert.AreEqual(succeeded.Id, result.Items.Single().EndPoint.Id);

        // disabled exclusion (the disabled endpoint is unused, so it matches the unknown category)
        result = await store.List(new ProxyEndPointStoreListParams { IncludeDisabled = false }, TestCt);
        Assert.IsFalse(result.Items.Any(x => x.EndPoint.Id == disabled.Id));

        // search by host and by country code, case-insensitive
        result = await store.List(new ProxyEndPointStoreListParams { Search = "PROXY1" }, TestCt);
        Assert.AreEqual(succeeded.Id, result.Items.Single().EndPoint.Id);
        result = await store.List(new ProxyEndPointStoreListParams { Search = "us" }, TestCt);
        Assert.AreEqual(succeeded.Id, result.Items.Single().EndPoint.Id);

        // ordering: enabled first (disabled last), then quality ascending
        // quality: unknown=0, succeeded/excellent=1, failed(penalty 200, no success)=6
        result = await store.List(new ProxyEndPointStoreListParams(), TestCt);
        Assert.AreEqual(4, result.TotalCount);
        CollectionAssert.AreEqual(
            new[] { unknown.Id, succeeded.Id, failed.Id, disabled.Id },
            result.Items.Select(x => x.EndPoint.Id).ToArray());

        // paging returns only the requested slice but reports the full count
        result = await store.List(new ProxyEndPointStoreListParams { RecordIndex = 1, RecordCount = 2 }, TestCt);
        Assert.AreEqual(4, result.TotalCount);
        CollectionAssert.AreEqual(
            new[] { succeeded.Id, failed.Id },
            result.Items.Select(x => x.EndPoint.Id).ToArray());
    }

    [TestMethod]
    public async Task Recreate_on_corrupt_file()
    {
        var dbPath = NewDbPath();

        // valid db first
        using (var store = new ProxyEndPointStore(dbPath)) {
            await Upsert(store, [new ProxyEndPointRecord { EndPoint = CreateEndPoint(1) }]);
            Assert.AreEqual(1, await store.Count(TestCt));
        }

        // corrupt the file
        await File.WriteAllTextAsync(dbPath, "this is not a sqlite database at all --------------------");

        // a new store must recreate it empty instead of failing (legacy data is disposable)
        using (var store = new ProxyEndPointStore(dbPath)) {
            Assert.AreEqual(0, await store.Count(TestCt));
            await Upsert(store, [new ProxyEndPointRecord { EndPoint = CreateEndPoint(2) }]);
            Assert.AreEqual(1, await store.Count(TestCt));
        }
    }

    [TestMethod]
    public async Task A_first_open_that_finds_the_file_locked_keeps_the_list()
    {
        var dbPath = NewDbPath();
        using (var store = new ProxyEndPointStore(dbPath))
            await Upsert(store, [new ProxyEndPointRecord { EndPoint = CreateEndPoint(1) }]);

        // The first open of another store, which the app and the VPN service each make, failed for the other process's
        // lock, as for any failure: the file was deleted and made again, empty, where the system let it be deleted
        using var secondStore = new ProxyEndPointStore(dbPath);
        await using (var holder = new SqliteConnection($"Data Source={dbPath};Pooling=False")) {
            await holder.OpenAsync(TestCt);
            await using (var command = holder.CreateCommand()) {
                command.CommandText = "PRAGMA locking_mode=EXCLUSIVE; BEGIN EXCLUSIVE;";
                await command.ExecuteNonQueryAsync(TestCt);
            }

            var ex = await Assert.ThrowsExactlyAsync<SqliteException>(() => secondStore.Count(TestCt));
            Assert.AreEqual(5, ex.SqliteErrorCode, "SQLITE_BUSY");
        }

        Assert.AreEqual(1, await secondStore.Count(TestCt));
    }

    [TestMethod]
    public async Task An_endpoint_whose_success_and_failure_came_at_once_is_a_failed_one()
    {
        // it was in no group: listed in none, and left by a deletion of every group
        using var store = new ProxyEndPointStore(NewDbPath());
        var time = DateTime.UtcNow;
        await Upsert(store, [
            new ProxyEndPointRecord {
                EndPoint = CreateEndPoint(1),
                Status = new ProxyEndPointStatus { SucceededCount = 1, FailedCount = 1, LastSucceeded = time, LastFailed = time }
            }
        ], keepExistingStatus: false);

        var failed = await store.List(new ProxyEndPointStoreListParams {
            IncludeSucceeded = false, IncludeFailed = true, IncludeUnknown = false, RecordCount = 10
        }, TestCt);

        Assert.AreEqual(1, failed.TotalCount);

        await store.DeleteAll(new DeleteAllOptions(), TestCt);
        Assert.AreEqual(0, await store.Count(TestCt));
    }

    [TestMethod]
    public async Task A_merge_of_an_endpoint_equal_to_a_stored_one_keeps_both_within_the_limit()
    {
        // the downloaded copy counted toward the limit as another endpoint, and pushed a stored one out: stored ones
        // never used come after the new ones
        using var store = new ProxyEndPointStore(NewDbPath());
        await Upsert(store, [
            new ProxyEndPointRecord { EndPoint = CreateEndPoint(1) },
            new ProxyEndPointRecord { EndPoint = CreateEndPoint(2) }
        ]);

        await store.Merge([CreateEndPoint(1)], maxItemCount: 2, maxPenalty: null, removeDuplicateIps: false, TestCt);
        Assert.AreEqual(2, await store.Count(TestCt));
    }

    // an endpoint with credentials
    private static ProxyEndPoint CreateEndPoint(int index, string username, string password)
    {
        var endPoint = CreateEndPoint(index);
        endPoint.Username = username;
        endPoint.Password = password;
        return endPoint;
    }

    [TestMethod]
    public async Task A_merge_keeps_the_stored_credentials_where_the_list_gives_none()
    {
        // the downloaded copy, with none, took the stored one's place, and its nulls were written over the stored pair
        var dbPath = NewDbPath();
        using (var store = new ProxyEndPointStore(dbPath)) {
            await Upsert(store, [new ProxyEndPointRecord { EndPoint = CreateEndPoint(1, "user", "secret") }]);
            await store.Merge([CreateEndPoint(1)], maxItemCount: null, maxPenalty: null, removeDuplicateIps: false, TestCt);
        }

        using (var store = new ProxyEndPointStore(dbPath)) {
            var record = await store.Get(CreateEndPoint(1).Id, TestCt);
            Assert.IsNotNull(record);
            Assert.AreEqual("user", record.EndPoint.Username);
            Assert.AreEqual("secret", record.EndPoint.Password);
        }
    }

    [TestMethod]
    public async Task A_merge_takes_the_lists_credentials_for_a_stored_endpoint_that_succeeded()
    {
        // the copy that brought them went as a duplicate of the stored endpoint before they were read
        using var store = new ProxyEndPointStore(NewDbPath());
        await Upsert(store, [
            new ProxyEndPointRecord {
                EndPoint = CreateEndPoint(1, "user", "old"),
                Status = new ProxyEndPointStatus { SucceededCount = 1, LastSucceeded = DateTime.UtcNow }
            }
        ], keepExistingStatus: false);

        await store.Merge([CreateEndPoint(1, "user", "new")], maxItemCount: null, maxPenalty: null,
            removeDuplicateIps: true, TestCt);

        var record = await store.Get(CreateEndPoint(1).Id, TestCt);
        Assert.IsNotNull(record);
        Assert.AreEqual("new", record.EndPoint.Password);
    }

    [TestMethod]
    public async Task The_defaults_of_DeleteAll_delete_an_endpoint_with_counts_and_no_times()
    {
        // it was in none of the three groups, so no list showed it and the defaults left it
        using var store = new ProxyEndPointStore(NewDbPath());
        await Upsert(store, [
            new ProxyEndPointRecord { EndPoint = CreateEndPoint(1), Status = new ProxyEndPointStatus { SucceededCount = 1 } }
        ], keepExistingStatus: false);

        var unknown = await store.List(new ProxyEndPointStoreListParams {
            IncludeSucceeded = false, IncludeFailed = false, IncludeUnknown = true, RecordCount = 10
        }, TestCt);

        Assert.AreEqual(1, unknown.TotalCount);

        await store.DeleteAll(new DeleteAllOptions(), TestCt);
        Assert.AreEqual(0, await store.Count(TestCt));
    }

    [TestMethod]
    public async Task A_merge_holds_the_write_lock_from_its_read_to_its_end()
    {
        // it read the list before its transaction, so another process's merge between them went past the item limit
        var dbPath = NewDbPath();
        using var store = new ProxyEndPointStore(dbPath);
        await Upsert(store, [new ProxyEndPointRecord { EndPoint = CreateEndPoint(1) }]);

        // the new endpoints are read after the stored ones: their reading holds the merge there, on a thread of its own
        var newEndPoints = new HoldingList([CreateEndPoint(2)]);
        var mergeTask = Task.Run(() => store.Merge(newEndPoints, maxItemCount: 1, maxPenalty: null,
            removeDuplicateIps: false, TestCt), TestCt);

        await newEndPoints.Reached.WaitAsync(TimeSpan.FromSeconds(10), TestCt);

        // another process's write finds the file locked
        await using (var other = new SqliteConnection($"Data Source={dbPath};Pooling=False;Default Timeout=1")) {
            await other.OpenAsync(TestCt);
            await using var command = other.CreateCommand();
            command.CommandText = "INSERT OR REPLACE INTO meta (key, value) VALUES ('test', 'other process')";
            var ex = await Assert.ThrowsExactlyAsync<SqliteException>(() => command.ExecuteNonQueryAsync(TestCt));
            Assert.AreEqual(5, ex.SqliteErrorCode, "SQLITE_BUSY");
        }

        newEndPoints.Release();
        await mergeTask.WaitAsync(TimeSpan.FromSeconds(10), TestCt);
        Assert.AreEqual(1, await store.Count(TestCt));
    }

    // a list whose first reading waits for the test's release
    private sealed class HoldingList(IReadOnlyList<ProxyEndPoint> items) : IReadOnlyList<ProxyEndPoint>
    {
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _released = new();
        public Task Reached => _reached.Task;
        public void Release() => _released.Set();
        public int Count => items.Count;

        public ProxyEndPoint this[int index] {
            get {
                Hold();
                return items[index];
            }
        }

        public IEnumerator<ProxyEndPoint> GetEnumerator()
        {
            Hold();
            return items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private void Hold()
        {
            _reached.TrySetResult();
            if (!_released.Wait(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("The test did not release the list.");
        }
    }

    [TestMethod]
    public async Task A_status_write_never_enables_an_endpoint_and_may_disable_one()
    {
        // the connector's copy, enabled, enabled again an endpoint the user had disabled meanwhile
        using var store = new ProxyEndPointStore(NewDbPath());
        var disabled = CreateEndPoint(1);
        disabled.IsEnabled = false;
        await Upsert(store, [
            new ProxyEndPointRecord { EndPoint = disabled },
            new ProxyEndPointRecord { EndPoint = CreateEndPoint(2) }
        ]);

        var enabledCopy = CreateEndPoint(1);
        var disabledCopy = CreateEndPoint(2);
        disabledCopy.IsEnabled = false;
        await store.UpdateStatuses([
            new ProxyEndPointInfo { EndPoint = enabledCopy, Status = new ProxyEndPointStatus() },
            new ProxyEndPointInfo { EndPoint = disabledCopy, Status = new ProxyEndPointStatus() }
        ], TestCt);

        var stored = await store.Get(disabled.Id, TestCt);
        Assert.IsNotNull(stored);
        Assert.IsFalse(stored.EndPoint.IsEnabled);

        var storedCopy = await store.Get(disabledCopy.Id, TestCt);
        Assert.IsNotNull(storedCopy);
        Assert.IsFalse(storedCopy.EndPoint.IsEnabled);
    }

    [TestMethod]
    public async Task Concurrent_access_from_two_store_instances()
    {
        var dbPath = NewDbPath();
        using var store1 = new ProxyEndPointStore(dbPath);
        using var store2 = new ProxyEndPointStore(dbPath);

        var endPoints = Enumerable.Range(0, 50).Select(i => CreateEndPoint(i)).ToArray();
        await Upsert(store1, endPoints.Select(x => new ProxyEndPointRecord { EndPoint = x }).ToArray());

        // one instance hammers status updates (core flush) while the other does CRUD (app)
        var statusTask = Task.Run(async () => {
            for (var round = 0; round < 20; round++) {
                await store1.UpdateStatuses(endPoints
                    .Select(x => new ProxyEndPointInfo {
                        EndPoint = x,
                        Status = new ProxyEndPointStatus { SucceededCount = round + 1 }
                    })
                    .ToArray(), TestCt);
            }
        });

        var crudTask = Task.Run(async () => {
            for (var i = 0; i < 20; i++) {
                await Upsert(store2, [new ProxyEndPointRecord { EndPoint = CreateEndPoint(1000 + i) }]);
                _ = await store2.List(TestCt);
                await store2.Delete([CreateEndPoint(1000 + i).Id], TestCt);
            }
        });

        await Task.WhenAll(statusTask, crudTask);

        Assert.AreEqual(50, await store1.Count(TestCt));
        Assert.IsTrue((await store2.List(TestCt)).All(x => x.Status.SucceededCount == 20));
    }
}
