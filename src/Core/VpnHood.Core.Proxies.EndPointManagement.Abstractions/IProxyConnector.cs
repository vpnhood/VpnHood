using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Proxies.EndPointManagement.Abstractions.Options;
using VpnHood.Core.Toolkit.Monitoring;

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

    /// <summary>Load implementation state such as the endpoint working set. Call once before first use.</summary>
    Task Init(CancellationToken cancellationToken);

    Task<TcpClient> ConnectAsync(IPEndPoint ipEndPoint, Action? onAttempt, CancellationToken cancellationToken);
    void RecordFailed(TcpClient tcpClient, Exception ex);
    Task CheckServers(CancellationToken cancellationToken);
    Task UpdateOptions(ProxyOptions proxyOptions);

    /// <summary>Persist any pending state (no-op for non-persistent implementations).</summary>
    Task Flush();
}
