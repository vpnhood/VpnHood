using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Proxies.EndPointManagement.Abstractions;
using VpnHood.Core.Proxies.EndPointManagement.Abstractions.Options;
using VpnHood.Core.Toolkit.Monitoring;

namespace VpnHood.Core.Proxies.EndPointManagement;

/// <summary>Disabled connector used when no proxy is configured; connections go direct.</summary>
public class NullProxyConnector : IProxyConnector
{
    public bool IsEnabled => false;
    public bool UseRecentSucceeded { get; set; }
    public ProgressStatus? Progress => null;

    public ProxyConnectorStatus Status => new() {
        AutoUpdate = false,
        SessionStatus = new ProxySessionStatus(),
        IsAnySucceeded = false,
        SucceededServerCount = 0,
        FailedServerCount = 0,
        UnknownServerCount = 0,
        DisabledServerCount = 0
    };

    public Task Init(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<TcpClient> ConnectAsync(IPEndPoint ipEndPoint, Action? onAttempt,
        CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("No proxy is configured.");
    }

    public void RecordFailed(TcpClient tcpClient, Exception ex)
    {
    }

    public Task CheckServers(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task UpdateOptions(ProxyOptions proxyOptions) => Task.CompletedTask;

    public Task Flush() => Task.CompletedTask;

    public ValueTask DisposeAsync() => default;
}
