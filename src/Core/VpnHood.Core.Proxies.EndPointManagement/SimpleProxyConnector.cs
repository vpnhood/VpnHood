using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Proxies.EndPointManagement.Abstractions;
using VpnHood.Core.Proxies.EndPointManagement.Abstractions.Options;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Monitoring;
using VpnHood.Core.Toolkit.Sockets;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Proxies.EndPointManagement;

/// <summary>
/// Lightweight connector for a single proxy endpoint (device proxy or a library-supplied proxy).
/// No persistence, no auto-update, no rating/rotation — just connect through the given proxy.
/// </summary>
public class SimpleProxyConnector(ProxyEndPoint proxyEndPoint)
    : IProxyConnector
{
    private readonly ProxySessionStatus _sessionStatus = new();
    private ProxyEndPoint _proxyEndPoint = proxyEndPoint; // mutable to support UpdateOptions

    public bool IsEnabled => true;
    public bool UseRecentSucceeded { get; set; } // single endpoint; ordering does not apply
    public ProgressStatus? Progress => null;

    public ProxyConnectorStatus Status {
        get {
            lock (_sessionStatus)
                return new ProxyConnectorStatus {
                    AutoUpdate = false,
                    SessionStatus = _sessionStatus,
                    IsAnySucceeded = _sessionStatus.ErrorMessage is null,
                    SucceededServerCount = _sessionStatus.IsLastUsedSucceeded ? 1 : 0,
                    FailedServerCount = _sessionStatus.IsLastUsedFailed ? 1 : 0,
                    UnknownServerCount = _sessionStatus.HasUsed ? 0 : 1,
                    DisabledServerCount = 0
                };
        }
    }

    public async Task<TcpClient> ConnectAsync(ISocketFactory socketFactory, IPEndPoint ipEndPoint,
        Action? onAttempt, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug(
            "Connecting via a {ProxyType} proxy server {ProxyServer}...",
            _proxyEndPoint.Protocol, VhLogger.FormatHostName(_proxyEndPoint.Host));

        var tickCount = Environment.TickCount64;
        TcpClient? tcpClient = null;
        try {
            var proxyClient = await ProxyClientFactory.CreateProxyClient(_proxyEndPoint, cancellationToken).Vhc();
            tcpClient = socketFactory.CreateTcpClient(proxyClient.ProxyEndPoint);
            await proxyClient.ConnectAsync(tcpClient, ipEndPoint, cancellationToken).Vhc();

            RecordSuccess(TimeSpan.FromMilliseconds(Environment.TickCount64 - tickCount));
            onAttempt?.Invoke();
            return tcpClient;
        }
        catch (Exception ex) {
            // let's not assume bad server if caller cancelled the operation
            if (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested) {
                RecordFailed(ex);
                onAttempt?.Invoke();
            }

            tcpClient?.Dispose();
            throw;
        }
    }

    public void RecordFailed(TcpClient tcpClient, Exception ex)
    {
        RecordFailed(ex);
    }

    private void RecordSuccess(TimeSpan latency)
    {
        lock (_sessionStatus) {
            _sessionStatus.SucceededCount++;
            _sessionStatus.LastSucceeded = FastDateTime.UtcNow;
            _sessionStatus.Latency = latency;
            _sessionStatus.ErrorMessage = null;
        }
    }

    private void RecordFailed(Exception ex)
    {
        lock (_sessionStatus) {
            _sessionStatus.FailedCount++;
            _sessionStatus.LastFailed = FastDateTime.UtcNow;
            _sessionStatus.Latency = null;
            _sessionStatus.ErrorMessage = ex.Message;
        }
    }

    public Task CheckServers(ISocketFactory socketFactory, CancellationToken cancellationToken)
    {
        // a single proxy is used as-is; failures surface on connect
        return Task.CompletedTask;
    }

    public Task UpdateOptions(ProxyOptions proxyOptions)
    {
        if (proxyOptions.ProxyEndPoint != null)
            _proxyEndPoint = proxyOptions.ProxyEndPoint;

        return Task.CompletedTask;
    }

    public Task Flush()
    {
        // nothing is persisted
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return default;
    }
}
