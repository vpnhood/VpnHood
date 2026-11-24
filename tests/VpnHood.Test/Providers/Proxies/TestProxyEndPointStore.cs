using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Net.Toolkit.Generics;

namespace VpnHood.Test.Providers.Proxies;

// A proxy endpoint store whose status updates can be held until released, or made to fail, as can its resets and its
// list's reads; its list's reads, a reload's first step, and its reads of the queue position, the last, can be held too
public class TestProxyEndPointStore(IProxyEndPointStore store) : IProxyEndPointStore
{
    private readonly TaskCompletionSource _updatesReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _listsReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _resetsReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _queuePositionReadsReleased =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public volatile bool HoldUpdates; // updates from now on wait for ReleaseUpdates
    public volatile bool HoldAfterUpdates; // updates from now on wait for ReleaseUpdates after their write
    private int _holdNextUpdate;
    public void HoldNextUpdate() => Volatile.Write(ref _holdNextUpdate, 1); // the next update alone waits
    public volatile bool FailUpdates; // updates from now on throw
    public volatile bool FailResets; // resets of the statuses from now on throw
    public volatile bool FailLists; // reads of the whole list from now on throw
    public volatile bool HoldLists; // reads of the whole list wait for ReleaseLists
    public volatile bool HoldResets; // resets of the statuses wait for ReleaseResets
    public volatile bool HoldQueuePositionReads; // reads of the queue position wait for ReleaseQueuePositionReads
    public int HeldUpdateCount;
    public int HeldListCount;
    public int HeldResetCount;
    public int HeldQueuePositionReadCount;
    public bool IsDisposed { get; private set; }

    public void ReleaseUpdates() => _updatesReleased.TrySetResult();
    public void ReleaseLists() => _listsReleased.TrySetResult();
    public void ReleaseResets() => _resetsReleased.TrySetResult();
    public void ReleaseQueuePositionReads() => _queuePositionReadsReleased.TrySetResult();

    public async Task UpdateStatuses(IReadOnlyList<ProxyEndPointInfo> infos, CancellationToken cancellationToken)
    {
        if (FailUpdates)
            throw new IOException("Test: the store could not be written.");

        if (HoldUpdates || Interlocked.Exchange(ref _holdNextUpdate, 0) == 1) {
            Interlocked.Increment(ref HeldUpdateCount);
            await _updatesReleased.Task.WaitAsync(cancellationToken);
        }

        await store.UpdateStatuses(infos, cancellationToken);

        if (HoldAfterUpdates) {
            Interlocked.Increment(ref HeldUpdateCount);
            await _updatesReleased.Task.WaitAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ProxyEndPointRecord>> List(CancellationToken cancellationToken)
    {
        if (FailLists)
            throw new IOException("Test: the store could not be read.");

        if (HoldLists) {
            Interlocked.Increment(ref HeldListCount);
            await _listsReleased.Task.WaitAsync(cancellationToken);
        }

        return await store.List(cancellationToken);
    }

    public Task<ProxyEndPointRecord?> Get(string id, CancellationToken cancellationToken) =>
        store.Get(id, cancellationToken);

    public Task<int> Count(CancellationToken cancellationToken) => store.Count(cancellationToken);
    public Task<ListResult<ProxyEndPointRecord>> List(ProxyEndPointStoreListParams options,
        CancellationToken cancellationToken) => store.List(options, cancellationToken);

    public Task Upsert(IReadOnlyList<ProxyEndPointRecord> records, bool keepExistingStatus,
        bool keepExistingEnabled, CancellationToken cancellationToken) =>
        store.Upsert(records, keepExistingStatus, keepExistingEnabled, cancellationToken);

    public Task Delete(IReadOnlyList<string> ids, CancellationToken cancellationToken) =>
        store.Delete(ids, cancellationToken);

    public Task DeleteAll(DeleteAllOptions options, CancellationToken cancellationToken) =>
        store.DeleteAll(options, cancellationToken);

    public Task DisableAllFailed(CancellationToken cancellationToken) => store.DisableAllFailed(cancellationToken);
    public Task SetCountryCode(string id, string? countryCode, CancellationToken cancellationToken) =>
        store.SetCountryCode(id, countryCode, cancellationToken);

    public async Task ResetStatuses(CancellationToken cancellationToken)
    {
        if (FailResets)
            throw new IOException("Test: the store could not be reset.");

        if (HoldResets) {
            Interlocked.Increment(ref HeldResetCount);
            await _resetsReleased.Task.WaitAsync(cancellationToken);
        }

        await store.ResetStatuses(cancellationToken);
    }

    public Task Merge(IReadOnlyList<ProxyEndPoint> newEndPoints, int? maxItemCount, int? maxPenalty,
        bool removeDuplicateIps, CancellationToken cancellationToken) =>
        store.Merge(newEndPoints, maxItemCount, maxPenalty, removeDuplicateIps, cancellationToken);

    public async Task<long> GetQueuePosition(CancellationToken cancellationToken)
    {
        if (HoldQueuePositionReads) {
            Interlocked.Increment(ref HeldQueuePositionReadCount);
            await _queuePositionReadsReleased.Task.WaitAsync(cancellationToken);
        }

        return await store.GetQueuePosition(cancellationToken);
    }

    public Task SetQueuePosition(long queuePosition, CancellationToken cancellationToken) =>
        store.SetQueuePosition(queuePosition, cancellationToken);

    public void Dispose()
    {
        IsDisposed = true;
        store.Dispose();
    }
}
