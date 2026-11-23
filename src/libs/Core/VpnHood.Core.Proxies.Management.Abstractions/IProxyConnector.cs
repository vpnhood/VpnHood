using System.Net;
using System.Net.Security;
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
    /// <summary>Gets a value indicating whether the connector has any proxy endpoint, in use or turned off.</summary>
    /// <remarks>
    /// It never tells a caller to connect directly: a connector with none to use still takes every connection, which then
    /// waits for a list's first download (<see cref="WaitForEndPoints"/>) or fails.
    /// </remarks>
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
    /// Waits until the connector has an endpoint to connect through: a managed list with no enabled endpoint waits for
    /// its first download from its URL. It returns at once where there is one, or where none can come, as for a list with
    /// no URL. A caller waits here under its own time, before connections whose own may be shorter.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task whose result is whether there is an endpoint to connect through: without one, no connection can be made.
    /// </returns>
    Task<bool> WaitForEndPoints(CancellationToken cancellationToken);

    /// <summary>
    /// Connects to an endpoint through a proxy.
    /// </summary>
    /// <param name="socketFactory">The factory of the sockets to the proxies: the caller's, which keeps them out of the tunnel.</param>
    /// <param name="ipEndPoint">The endpoint to reach through the proxy: the VPN server's.</param>
    /// <param name="hostName">
    /// The name the endpoint's TLS takes, as the connection's own does, for a check of the proxies on the way, or
    /// <see langword="null"/> for none.
    /// </param>
    /// <param name="serverCertificateValidation">
    /// The check of the endpoint's certificate in such a check, the connection's own, or <see langword="null"/> to take
    /// any certificate.
    /// </param>
    /// <param name="onAttempt">Called after each proxy the connector tried, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task whose result is the connection, through the proxy, which the caller owns: its stream carries the
    /// destination's bytes, through an HTTPS proxy's TLS session too.
    /// </returns>
    Task<ProxyConnection> ConnectAsync(ISocketFactory socketFactory, IPEndPoint ipEndPoint, string? hostName,
        RemoteCertificateValidationCallback? serverCertificateValidation, Action? onAttempt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records a failure of a connection <see cref="ConnectAsync"/> made, after it was made, as in a TLS handshake
    /// over it, against the proxy that made it.
    /// </summary>
    /// <param name="tcpClient">The connection's socket, <see cref="ProxyConnection.TcpClient"/>.</param>
    /// <param name="ex">The failure.</param>
    void RecordFailed(TcpClient tcpClient, Exception ex);

    /// <summary>
    /// Checks the endpoints before a connect, where the connector has a list to check: it connects through them to an
    /// endpoint, the VPN server's, as the VPN would, and records what it finds. The connect waits for the first that
    /// passes alone, and the check may go on behind it through the rest. The device reaches no one else through them.
    /// </summary>
    /// <param name="socketFactory">The factory of the sockets to the proxies, which keeps them out of the tunnel.</param>
    /// <param name="ipEndPoint">The endpoint to reach through each proxy: the VPN server's.</param>
    /// <param name="hostName">The name the endpoint's TLS takes, as the VPN's own connection does, or <see langword="null"/>.</param>
    /// <param name="serverCertificateValidation">
    /// The check of the endpoint's certificate, the VPN connection's own, or <see langword="null"/> to take any
    /// certificate.
    /// </param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that ends at the first endpoint that passes, or at the check's end where none does.</returns>
    Task CheckServers(ISocketFactory socketFactory, IPEndPoint ipEndPoint, string? hostName,
        RemoteCertificateValidationCallback? serverCertificateValidation, CancellationToken cancellationToken);

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
