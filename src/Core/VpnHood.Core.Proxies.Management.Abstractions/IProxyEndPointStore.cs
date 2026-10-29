using VpnHood.Net.Toolkit.Generics;

namespace VpnHood.Core.Proxies.Management.Abstractions;

/// <summary>
/// Shared persistent store for the managed proxy endpoint list. Implementations must be safe for
/// concurrent use from multiple processes (app and VPN service) on the same store file.
/// </summary>
public interface IProxyEndPointStore : IDisposable
{
    Task<IReadOnlyList<ProxyEndPointRecord>> List(CancellationToken cancellationToken);
    Task<ProxyEndPointRecord?> Get(string id, CancellationToken cancellationToken);
    Task<int> Count(CancellationToken cancellationToken);

    /// <summary>Filter, order (enabled desc, quality asc) and page inside the store, so callers
    /// never need to load the whole table.</summary>
    Task<ListResult<ProxyEndPointRecord>> List(ProxyEndPointStoreListParams options, CancellationToken cancellationToken);

    /// <summary>Insert or update endpoints by id. Endpoint fields always overwrite;
    /// status columns are preserved on conflict when <paramref name="keepExistingStatus"/> is set,
    /// and is_enabled is preserved when <paramref name="keepExistingEnabled"/> is set.</summary>
    Task Upsert(IReadOnlyList<ProxyEndPointRecord> records, bool keepExistingStatus, bool keepExistingEnabled,
        CancellationToken cancellationToken);

    /// <summary>Update status and is_enabled of existing rows by id. Never inserts, so it can not
    /// resurrect rows deleted by the other process.</summary>
    Task UpdateStatuses(IReadOnlyList<ProxyEndPointInfo> infos, CancellationToken cancellationToken);

    Task Delete(IReadOnlyList<string> ids, CancellationToken cancellationToken);
    Task DeleteAll(DeleteAllOptions options, CancellationToken cancellationToken);
    Task DisableAllFailed(CancellationToken cancellationToken);
    Task SetCountryCode(string id, string? countryCode, CancellationToken cancellationToken);
    Task ResetStatuses(CancellationToken cancellationToken);

    /// <summary>Merge a new endpoint list into the store using the standard priority rules
    /// (see ProxyEndPointUpdater.Merge): upsert survivors, delete pruned rows, single transaction.</summary>
    Task Merge(IReadOnlyList<ProxyEndPoint> newEndPoints, int? maxItemCount, int? maxPenalty,
        bool removeDuplicateIps, CancellationToken cancellationToken);

    Task<long> GetQueuePosition(CancellationToken cancellationToken);
    Task SetQueuePosition(long queuePosition, CancellationToken cancellationToken);
}
