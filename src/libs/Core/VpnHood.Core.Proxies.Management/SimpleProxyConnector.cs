using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Proxies.Management.Abstractions.Options;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Monitoring;
using VpnHood.Net.Toolkit.Sockets;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Proxies.Management;

/// <summary>
/// Provides the VPN's connections through one given proxy, such as the device's: nothing is saved, rated or updated.
/// </summary>
/// <param name="proxyEndPoint">The proxy.</param>
public class SimpleProxyConnector(ProxyEndPoint proxyEndPoint)
    : IProxyConnector
{
    private readonly ProxySessionStatus _sessionStatus = new();
    private ProxyEndPoint _proxyEndPoint = proxyEndPoint; // mutable to support UpdateOptions

    /// <inheritdoc/>
    /// <value>Always <see langword="true"/>.</value>
    public bool IsEnabled => true;

    /// <inheritdoc/>
    /// <remarks>It changes nothing here: there is one proxy.</remarks>
    public bool UseRecentSucceeded { get; set; }

    /// <inheritdoc/>
    /// <value>Always <see langword="null"/>: there is no check.</value>
    public ProgressStatus? Progress => null;

    /// <inheritdoc/>
    /// <remarks>It counts the one proxy by the outcome of its last connection.</remarks>
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

    /// <inheritdoc/>
    /// <remarks>
    /// A failure is counted and thrown; a cancellation of <paramref name="cancellationToken"/> is not counted.
    /// </remarks>
    /// <exception cref="VpnHood.Net.Proxies.ProxyClientException">
    /// The proxy refused the connection, or answered in a way the client does not take.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">A SOCKS5 proxy refused the credentials.</exception>
    /// <exception cref="ProtocolViolationException">The proxy's answer broke its protocol.</exception>
    /// <exception cref="ArgumentException">The endpoint's credentials are too long for its protocol.</exception>
    /// <exception cref="System.Security.Authentication.AuthenticationException">The TLS handshake with an HTTPS proxy failed.</exception>
    /// <exception cref="TimeoutException">The proxy took too long.</exception>
    /// <exception cref="IOException">The connection to the proxy failed.</exception>
    /// <exception cref="SocketException">The proxy's host could not be resolved or reached.</exception>
    /// <exception cref="Exception">DNS gave no address for the proxy's host.</exception>
    /// <exception cref="NotSupportedException">
    /// The proxy's protocol, a SOCKS5 proxy's authentication method, or its reply's address type is not supported.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
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

    /// <inheritdoc/>
    /// <remarks>It counts a failure, whatever the connection.</remarks>
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

    /// <inheritdoc/>
    /// <remarks>It does nothing: a connection's failure tells.</remarks>
    public Task CheckServers(ISocketFactory socketFactory, CancellationToken cancellationToken)
    {
        // a single proxy is used as-is; failures surface on connect
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>It takes the options' proxy, when they give one.</remarks>
    public Task UpdateOptions(ProxyOptions proxyOptions, CancellationToken cancellationToken)
    {
        if (proxyOptions.ProxyEndPoint != null)
            _proxyEndPoint = proxyOptions.ProxyEndPoint;

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>It does nothing: nothing is saved.</remarks>
    public Task Flush(CancellationToken cancellationToken)
    {
        // nothing is persisted
        return Task.CompletedTask;
    }

    /// <summary>Does nothing: the connector holds nothing to release.</summary>
    /// <returns>A completed task.</returns>
    public ValueTask DisposeAsync()
    {
        return default;
    }
}
