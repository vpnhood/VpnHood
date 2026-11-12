using VpnHood.AppLib.Api.Proxies;
using VpnHood.Net.Toolkit.Generics;

namespace VpnHood.AppLib.Api;

/// <summary>
/// Represents the app's API for its proxy servers: the list the VPN may reach its server through, with each one's state.
/// </summary>
/// <remarks>A failure comes as <see cref="IAppApi"/> describes, in process and over HTTP.</remarks>
public interface IProxyEndPointsApi
{
    /// <summary>Gets a proxy.</summary>
    /// <param name="proxyEndPointId">The proxy's ID.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the proxy, with its state.</returns>
    /// <exception cref="KeyNotFoundException">No proxy has <paramref name="proxyEndPointId"/>.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task<AppProxyEndPointInfo> Get(string proxyEndPointId, CancellationToken cancellationToken);

    /// <summary>Adds a proxy, or updates the saved one with the same address, keeping how it has behaved.</summary>
    /// <param name="proxyEndPoint">The proxy.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the proxy, with its state.</returns>
    /// <exception cref="NotSupportedException">The scheme names no proxy protocol the app speaks.</exception>
    /// <exception cref="ArgumentException">The proxy's address is empty, not valid, or has no scheme or no host.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task<AppProxyEndPointInfo> Add(ProxyEndPoint proxyEndPoint, CancellationToken cancellationToken);

    /// <summary>Changes a proxy.</summary>
    /// <param name="proxyEndPointId">The proxy's ID.</param>
    /// <param name="proxyEndPoint">The proxy as it is to be.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the proxy, with its state.</returns>
    /// <exception cref="NotSupportedException">The scheme names no proxy protocol the app speaks.</exception>
    /// <exception cref="ArgumentException">The proxy's address is empty, not valid, or has no scheme or no host.</exception>
    /// <exception cref="KeyNotFoundException">No proxy has <paramref name="proxyEndPointId"/>.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task<AppProxyEndPointInfo> Update(string proxyEndPointId, ProxyEndPoint proxyEndPoint, CancellationToken cancellationToken);

    /// <summary>Deletes a proxy.</summary>
    /// <param name="proxyEndPointId">The proxy's ID.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task Delete(string proxyEndPointId, CancellationToken cancellationToken);

    /// <summary>Deletes the proxies in the given states.</summary>
    /// <param name="deleteSucceeded"><see langword="true"/> to delete the proxies that last succeeded.</param>
    /// <param name="deleteFailed"><see langword="true"/> to delete the proxies that last failed.</param>
    /// <param name="deleteUnknown"><see langword="true"/> to delete the proxies not tried yet.</param>
    /// <param name="deleteDisabled"><see langword="true"/> to delete the disabled proxies.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task DeleteAll(bool deleteSucceeded, bool deleteFailed, bool deleteUnknown, bool deleteDisabled, CancellationToken cancellationToken);

    /// <summary>Disables every proxy that last failed.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task DisableAllFailed(CancellationToken cancellationToken);

    /// <summary>Forgets every proxy's state, so each is tried anew.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    /// <remarks>A disabled proxy stays disabled.</remarks>
    Task ResetStates(CancellationToken cancellationToken);

    /// <summary>Gets the device's own proxy, as its settings tell it.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the proxy, or <see langword="null"/> where the device has none.</returns>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task<AppProxyEndPointInfo?> GetDevice(CancellationToken cancellationToken);

    /// <summary>Lists the proxies in the given states, a page at a time.</summary>
    /// <param name="search">A text the proxies must contain, or <see langword="null"/> for all.</param>
    /// <param name="includeSucceeded"><see langword="true"/> to include the proxies that last succeeded.</param>
    /// <param name="includeFailed"><see langword="true"/> to include the proxies that last failed.</param>
    /// <param name="includeUnknown"><see langword="true"/> to include the proxies not tried yet.</param>
    /// <param name="includeDisabled"><see langword="true"/> to include the disabled proxies.</param>
    /// <param name="recordIndex">The index of the page's first proxy, or <see langword="null"/> for the first.</param>
    /// <param name="recordCount">How many proxies the page holds at most, or <see langword="null"/> for all.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the page, with the count of all the proxies that match.</returns>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task<ListResult<AppProxyEndPointInfo>> List(
        string? search,
        bool includeSucceeded,
        bool includeFailed,
        bool includeUnknown,
        bool includeDisabled,
        int? recordIndex,
        int? recordCount,
        CancellationToken cancellationToken);

    /// <summary>Adds the proxies a text lists.</summary>
    /// <param name="content">The text, one proxy per line.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentException">The settings' most proxies to keep is 0 or less.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task Import(string content, CancellationToken cancellationToken);

    /// <summary>Reads the proxy list again from the address the settings' automatic update names.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="System.Net.Http.HttpRequestException">The list could not be read.</exception>
    /// <exception cref="InvalidOperationException">The settings name no address to read the list from.</exception>
    /// <exception cref="ArgumentException">The settings' most proxies to keep is 0 or less.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task ReloadUrl(CancellationToken cancellationToken);

    /// <summary>Reads a proxy from its text, without adding it.</summary>
    /// <param name="text">The proxy's address, as a URL or as a host and port.</param>
    /// <param name="defaults">What the text leaves out, as the protocol and port.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the proxy, without a state.</returns>
    /// <exception cref="NotSupportedException">The scheme names no proxy protocol the app speaks.</exception>
    /// <exception cref="ArgumentException">
    /// The text is empty, not valid, or has no host; or it names no scheme and no port the defaults can place.
    /// </exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task<AppProxyEndPointInfo> Parse(string text, ProxyEndPointDefaults defaults, CancellationToken cancellationToken);
}