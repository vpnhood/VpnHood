using VpnHood.Net.Toolkit.Generics;

namespace VpnHood.Core.Proxies.Management.Abstractions;

/// <summary>
/// Defines the persistent store of a managed proxy list, which the app and the VPN service share.
/// </summary>
/// <remarks>
/// An implementation must be safe to use at once from several processes on the same store. Endpoints are known by
/// their <see cref="ProxyEndPoint.Id"/>.
/// </remarks>
public interface IProxyEndPointStore : IDisposable
{
    /// <summary>Lists every endpoint.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the endpoints, in no set order.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<IReadOnlyList<ProxyEndPointRecord>> List(CancellationToken cancellationToken);

    /// <summary>Gets an endpoint by its ID.</summary>
    /// <param name="id">The endpoint's <see cref="ProxyEndPoint.Id"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the endpoint, or <see langword="null"/> when there is none of the ID.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<ProxyEndPointRecord?> Get(string id, CancellationToken cancellationToken);

    /// <summary>Counts the endpoints.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the number of endpoints.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<int> Count(CancellationToken cancellationToken);

    /// <summary>
    /// Lists a page of the endpoints that match a filter, enabled ones first, then by quality, best first, all within
    /// the store, so the caller never loads the whole list.
    /// </summary>
    /// <param name="options">The filter and the page.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the page, with the number of all the matching endpoints.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<ListResult<ProxyEndPointRecord>> List(ProxyEndPointStoreListParams options, CancellationToken cancellationToken);

    /// <summary>
    /// Adds endpoints, or updates those of the same IDs: their protocol, host, port, user name and password, and their
    /// country code where the record gives one.
    /// </summary>
    /// <param name="records">The endpoints.</param>
    /// <param name="keepExistingStatus"><see langword="true"/> to keep a stored endpoint's status.</param>
    /// <param name="keepExistingEnabled"><see langword="true"/> to keep whether a stored endpoint is enabled.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task Upsert(IReadOnlyList<ProxyEndPointRecord> records, bool keepExistingStatus, bool keepExistingEnabled,
        CancellationToken cancellationToken);

    /// <summary>
    /// Updates the status and the enabled state of stored endpoints, by ID. It adds none, so it cannot bring back an
    /// endpoint the other process has deleted.
    /// </summary>
    /// <param name="infos">The endpoints and their statuses.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task UpdateStatuses(IReadOnlyList<ProxyEndPointInfo> infos, CancellationToken cancellationToken);

    /// <summary>Deletes endpoints by ID; an ID not stored is skipped.</summary>
    /// <param name="ids">The IDs.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task Delete(IReadOnlyList<string> ids, CancellationToken cancellationToken);

    /// <summary>Deletes the endpoints the options select.</summary>
    /// <param name="options">The groups of endpoints to delete.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task DeleteAll(DeleteAllOptions options, CancellationToken cancellationToken);

    /// <summary>Disables every endpoint used whose last use did not succeed.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task DisableAllFailed(CancellationToken cancellationToken);

    /// <summary>Sets the country code of a stored endpoint.</summary>
    /// <param name="id">The endpoint's ID; an ID not stored changes nothing.</param>
    /// <param name="countryCode">The country code, or <see langword="null"/> to clear it.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task SetCountryCode(string id, string? countryCode, CancellationToken cancellationToken);

    /// <summary>Clears every endpoint's status, and the queue position.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task ResetStatuses(CancellationToken cancellationToken);

    /// <summary>
    /// Merges a new list of endpoints into the store, by <see cref="ProxyEndPointUpdater.Merge"/>'s order: it deletes
    /// the endpoints the merge leaves out and adds the new ones, in one transaction, and a stored endpoint keeps its
    /// status and enabled state.
    /// </summary>
    /// <param name="newEndPoints">The new list.</param>
    /// <param name="maxItemCount">How many endpoints to keep, or <see langword="null"/> for the default.</param>
    /// <param name="maxPenalty">The penalty above which a used endpoint goes after the new ones, or <see langword="null"/> for the default.</param>
    /// <param name="removeDuplicateIps">
    /// <see langword="true"/> to drop a new endpoint whose host and protocol match a stored one whose last use
    /// succeeded.
    /// </param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentException"><paramref name="maxItemCount"/> is 0 or less.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task Merge(IReadOnlyList<ProxyEndPoint> newEndPoints, int? maxItemCount, int? maxPenalty,
        bool removeDuplicateIps, CancellationToken cancellationToken);

    /// <summary>Gets the queue position, the count of connections the endpoints' rotation has reached.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the queue position; 0 when none is stored.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<long> GetQueuePosition(CancellationToken cancellationToken);

    /// <summary>Sets the queue position, the count of connections the endpoints' rotation has reached.</summary>
    /// <param name="queuePosition">The queue position.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task SetQueuePosition(long queuePosition, CancellationToken cancellationToken);
}
