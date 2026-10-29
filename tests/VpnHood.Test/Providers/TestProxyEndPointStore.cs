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

    public async Task UpdateStatuses(IReadOnlyList<ProxyEndPointInfo> infos, CancellationToken cancellationToken)
    {
        if (FailUpdates)
            throw new IOException("Test: the store could not be written.");

        if (HoldUpdates) {
            Interlocked.Increment(ref HeldUpdateCount);
            await _updatesReleased.Task.WaitAsync(cancellationToken);
        }

        await store.UpdateStatuses(infos, cancellationToken);
    }

    public Task<IReadOnlyList<ProxyEndPointRecord>> List(CancellationToken cancellationToken) => store.List(cancellationToken);
    public Task<ProxyEndPointRecord?> Get(string id, CancellationToken cancellationToken) => store.Get(id, cancellationToken);
    public Task<int> Count(CancellationToken cancellationToken) => store.Count(cancellationToken);
    public Task<ListResult<ProxyEndPointRecord>> List(ProxyEndPointStoreListParams options, CancellationToken cancellationToken) =>
        store.List(options, cancellationToken);

    public Task Upsert(IReadOnlyList<ProxyEndPointRecord> records, bool keepExistingStatus,
        bool keepExistingEnabled, CancellationToken cancellationToken) => store.Upsert(records, keepExistingStatus, keepExistingEnabled, cancellationToken);

    public Task Delete(IReadOnlyList<string> ids, CancellationToken cancellationToken) => store.Delete(ids, cancellationToken);
    public Task DeleteAll(DeleteAllOptions options, CancellationToken cancellationToken) => store.DeleteAll(options, cancellationToken);
    public Task DisableAllFailed(CancellationToken cancellationToken) => store.DisableAllFailed(cancellationToken);
    public Task SetCountryCode(string id, string? countryCode, CancellationToken cancellationToken) =>
        store.SetCountryCode(id, countryCode, cancellationToken);
    public Task ResetStatuses(CancellationToken cancellationToken) => store.ResetStatuses(cancellationToken);

    public Task Merge(IReadOnlyList<ProxyEndPoint> newEndPoints, int? maxItemCount, int? maxPenalty,
        bool removeDuplicateIps, CancellationToken cancellationToken) =>
        store.Merge(newEndPoints, maxItemCount, maxPenalty, removeDuplicateIps, cancellationToken);

    public Task<long> GetQueuePosition(CancellationToken cancellationToken) => store.GetQueuePosition(cancellationToken);
    public Task SetQueuePosition(long queuePosition, CancellationToken cancellationToken) =>
        store.SetQueuePosition(queuePosition, cancellationToken);

    public void Dispose()
    {
        IsDisposed = true;
        store.Dispose();
    }
}
