using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Proxies.Management.Abstractions.Options;
using VpnHood.Net.Toolkit.Monitoring;
using VpnHood.Net.Toolkit.Sockets;

namespace VpnHood.Core.Proxies.Management.Abstractions;

/// <summary>
/// Defines how the VPN connects to its servers through proxies: through one given proxy, or through a managed list
/// that rates and rotates its endpoints.
/// </summary>
public interface IProxyConnector : IAsyncDisposable
{
    /// <summary>Gets a value indicating whether the connector has a proxy to connect through.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Gets or sets a value indicating whether connections use only the endpoints that have succeeded since the
    /// connector was made, where there is a list to choose from.
    /// </summary>
    bool UseRecentSucceeded { get; set; }

    /// <summary>Gets the progress of a check of the endpoints under way, or <see langword="null"/>.</summary>
    ProgressStatus? Progress { get; }

    /// <summary>Gets the connector's state, made at each read.</summary>
    ProxyConnectorStatus Status { get; }

    /// <summary>
    /// Connects to an endpoint through a proxy.
    /// </summary>
    /// <param name="socketFactory">The factory of the sockets to the proxies: the caller's, which keeps them out of the tunnel.</param>
    /// <param name="ipEndPoint">The endpoint to reach through the proxy.</param>
    /// <param name="onAttempt">Called after each proxy the connector tried, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task whose result is the connection, through the proxy, which the caller owns. Its stream is the raw TCP one,
    /// not an HTTPS proxy's TLS tunnel.
    /// </returns>
    Task<TcpClient> ConnectAsync(ISocketFactory socketFactory, IPEndPoint ipEndPoint, Action? onAttempt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records a failure of a connection <see cref="ConnectAsync"/> made, after it was made, as in a TLS handshake
    /// over it, against the proxy that made it.
    /// </summary>
    /// <param name="tcpClient">The connection.</param>
    /// <param name="ex">The failure.</param>
    void RecordFailed(TcpClient tcpClient, Exception ex);

    /// <summary>Checks the endpoints, where the connector has a list to check, and records what it finds.</summary>
    /// <param name="socketFactory">The factory of the sockets to the proxies, which keeps them out of the tunnel.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task CheckServers(ISocketFactory socketFactory, CancellationToken cancellationToken);

    /// <summary>Takes new options.</summary>
    /// <param name="proxyOptions">The options.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task UpdateOptions(ProxyOptions proxyOptions, CancellationToken cancellationToken);

    /// <summary>Saves the endpoints' statuses not saved yet, where the connector keeps any.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task Flush(CancellationToken cancellationToken);
}
