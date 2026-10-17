using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Net.Toolkit.Generics;

namespace VpnHood.Test.Providers;

// A proxy endpoint store whose status updates can be held until released, or made to fail
public class TestProxyEndPointStore(IProxyEndPointStore store) : IProxyEndPointStore
{
    private readonly TaskCompletionSource _updatesReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public volatile bool HoldUpdates; // updates from now on wait for ReleaseUpdates
    public volatile bool FailUpdates; // updates from now on throw
    public int HeldUpdateCount;
    public bool IsDisposed { get; private set; }

    public void ReleaseUpdates() => _updatesReleased.TrySetResult();

    public async Task UpdateStatuses(IReadOnlyList<ProxyEndPointInfo> infos)
    {
        if (FailUpdates)
            throw new IOException("Test: the store could not be written.");

        if (HoldUpdates) {
            Interlocked.Increment(ref HeldUpdateCount);
            await _updatesReleased.Task;
        }

        await store.UpdateStatuses(infos);
    }

    public Task<IReadOnlyList<ProxyEndPointRecord>> List() => store.List();
    public Task<ProxyEndPointRecord?> Get(string id) => store.Get(id);
    public Task<int> Count() => store.Count();
    public Task<ListResult<ProxyEndPointRecord>> List(ProxyEndPointStoreListParams options) => store.List(options);

    public Task Upsert(IReadOnlyList<ProxyEndPointRecord> records, bool keepExistingStatus = true,
        bool keepExistingEnabled = false) => store.Upsert(records, keepExistingStatus, keepExistingEnabled);

    public Task Delete(IReadOnlyList<string> ids) => store.Delete(ids);
    public Task DeleteAll(DeleteAllOptions options) => store.DeleteAll(options);
    public Task DisableAllFailed() => store.DisableAllFailed();
    public Task SetCountryCode(string id, string? countryCode) => store.SetCountryCode(id, countryCode);
    public Task ResetStatuses() => store.ResetStatuses();

    public Task Merge(IReadOnlyList<ProxyEndPoint> newEndPoints, int? maxItemCount, int? maxPenalty,
        bool removeDuplicateIps) => store.Merge(newEndPoints, maxItemCount, maxPenalty, removeDuplicateIps);

    public Task<long> GetQueuePosition() => store.GetQueuePosition();
    public Task SetQueuePosition(long queuePosition) => store.SetQueuePosition(queuePosition);

    public void Dispose()
    {
        IsDisposed = true;
        store.Dispose();
    }
}
