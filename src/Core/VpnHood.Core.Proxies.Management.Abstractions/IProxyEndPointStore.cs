using VpnHood.Core.Toolkit.Generics;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Proxies.Management.Abstractions;

/// <summary>
/// Shared persistent store for the managed proxy endpoint list. Implementations must be safe for
/// concurrent use from multiple processes (app and VPN service) on the same store file.
/// </summary>
public interface IProxyEndPointStore : IDisposable
{
    Task<IReadOnlyList<ProxyEndPointRecord>> List();
    Task<ProxyEndPointRecord?> Get(string id);
    Task<int> Count();

    /// <summary>Filter, order (enabled desc, quality asc) and page inside the store, so callers
    /// never need to load the whole table.</summary>
    Task<ListResult<ProxyEndPointRecord>> List(ProxyEndPointStoreListParams options);

    /// <summary>Insert or update endpoints by id. Endpoint fields always overwrite;
    /// status columns are preserved on conflict when <paramref name="keepExistingStatus"/> is set,
    /// and is_enabled is preserved when <paramref name="keepExistingEnabled"/> is set.</summary>
    Task Upsert(IReadOnlyList<ProxyEndPointRecord> records, bool keepExistingStatus = true,
        bool keepExistingEnabled = false);

    /// <summary>Update status and is_enabled of existing rows by id. Never inserts, so it can not
    /// resurrect rows deleted by the other process.</summary>
    Task UpdateStatuses(IReadOnlyList<ProxyEndPointInfo> infos);

    Task Delete(IReadOnlyList<string> ids);
    Task DeleteAll(DeleteAllOptions options);
    Task DisableAllFailed();
    Task SetCountryCode(string id, string? countryCode);
    Task ResetStatuses();

    /// <summary>Merge a new endpoint list into the store using the standard priority rules
    /// (see ProxyEndPointUpdater.Merge): upsert survivors, delete pruned rows, single transaction.</summary>
    Task Merge(IReadOnlyList<ProxyEndPoint> newEndPoints, int? maxItemCount, int? maxPenalty,
        bool removeDuplicateIps);

    Task<long> GetQueuePosition();
    Task SetQueuePosition(long queuePosition);
}
