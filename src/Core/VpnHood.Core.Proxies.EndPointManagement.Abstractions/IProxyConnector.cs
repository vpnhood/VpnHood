using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Proxies.EndPointManagement.Abstractions.Options;
using VpnHood.Core.Toolkit.Monitoring;
using VpnHood.Core.Toolkit.Sockets;

namespace VpnHood.Core.Proxies.EndPointManagement.Abstractions;

/// <summary>
/// Connect-path abstraction over proxies. Implementations range from a single static proxy
/// (no persistence) to the managed, database-backed endpoint list.
/// </summary>
public interface IProxyConnector : IAsyncDisposable
{
    bool IsEnabled { get; }
    bool UseRecentSucceeded { get; set; }
    ProgressStatus? Progress { get; }
    ProxyConnectorStatus Status { get; }

    /// <summary>
    /// Connects to <paramref name="ipEndPoint"/> through a proxy.
    /// <paramref name="socketFactory"/> is passed per call rather than held: proxy traffic must leave the
    /// tunnel, so the sockets have to come from the caller's protected factory.
    /// </summary>
    Task<TcpClient> ConnectAsync(ISocketFactory socketFactory, IPEndPoint ipEndPoint, Action? onAttempt,
        CancellationToken cancellationToken);

    void RecordFailed(TcpClient tcpClient, Exception ex);
    Task CheckServers(ISocketFactory socketFactory, CancellationToken cancellationToken);
    Task UpdateOptions(ProxyOptions proxyOptions);

    /// <summary>Persist any pending state (no-op for non-persistent implementations).</summary>
    Task Flush();
}
