using System.Net;
using System.Net.Security;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Quic.Abstractions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Connections;

namespace VpnHood.Core.Client.ConnectorServices;

/// <summary>
/// Represents a single QUIC connection in the <see cref="QuicStreamConnectionFactory"/> pool,
/// tracking active/lifetime streams and idle state.
/// </summary>
/// <remarks>
/// <c>maxStreamsPerConnection</c> and <c>maxLifetimeStreamsPerConnection</c> are soft limits.
/// <see cref="CanOpenStream"/> is intentionally lock-free to keep allocation path lightweight,
/// therefore under race a small temporary exceed is acceptable by design.
/// </remarks>
internal class QuicStreamConnectionItem(
    IQuicClient quicClient,
    int maxStreamsPerConnection,
    int maxLifetimeStreamsPerConnection,
    IPEndPoint quicEndPoint) : IAsyncDisposable
{
    private IQuicConnection? _connection;
    private readonly AsyncLock _connectLock = new();
    private readonly Lock _usageLock = new();

    public bool IsDead { get; private set; }

    /// <summary>Number of streams currently open.</summary>
    public int ActiveStreamCount { get; private set; }

    /// <summary>Total streams ever opened successfully on this connection.</summary>
    public int TotalStreamCount { get; private set; }

    /// <summary>True when open failed and this connection should no longer accept new streams.</summary>

    /// <summary>
    /// Timestamp of when active stream count reached zero last time.
    /// Null means the item is currently active or has not become idle yet.
    /// </summary>
    public DateTime? ZeroActiveSince { get; private set; }

    public bool CanOpenStream => !IsDead &&
        ActiveStreamCount < maxStreamsPerConnection && 
        TotalStreamCount < maxLifetimeStreamsPerConnection;

    public IQuicConnection Connection =>
        _connection ?? throw new InvalidOperationException("QUIC connection has not been established yet.");

    public async Task<IStreamConnection> OpenStreamConnection(
        VpnEndPoint vpnEndPoint,
        RemoteCertificateValidationCallback certificateValidationCallback,
        string connectionId,
        CancellationToken cancellationToken)
    {
        using var asyncLock = await _connectLock.LockAsync(cancellationToken).Vhc();

        try {
            _connection ??= await ConnectAsync(vpnEndPoint, certificateValidationCallback, cancellationToken).Vhc();
            var stream = await _connection.OpenOutboundStreamAsync(cancellationToken).Vhc();

            lock (_usageLock) {
                TotalStreamCount++;
                ActiveStreamCount++;
                ZeroActiveSince = null;
            }

            var connection = new QuicStreamConnection(stream,
                localEndPoint: Connection.LocalEndPoint,
                remoteEndPoint: Connection.RemoteEndPoint,
                connectionName: "tunnel",
                isServer: false,
                connectionId: connectionId);

            connection.Disposed += Connection_Disposed;

            return connection;
        }
        catch {
            IsDead = true; // already protected by asyncLock
            throw;
        }
    }

    private void Connection_Disposed(object? sender, EventArgs e)
    {
        lock (_usageLock) {
            ActiveStreamCount--;
            if (ActiveStreamCount == 0)
                ZeroActiveSince = FastDateTime.Now;
        }
    }

    private ValueTask<IQuicConnection> ConnectAsync(
        VpnEndPoint vpnEndPoint,
        RemoteCertificateValidationCallback certificateValidationCallback,
        CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug(GeneralEventId.Request,
            "Establishing a new QUIC connection to the Server... EndPoint: {EndPoint}",
            VhLogger.Format(quicEndPoint));

        var options = new QuicClientConnectOptions {
            RemoteEndPoint = quicEndPoint,
            TargetHost = vpnEndPoint.HostName,
            CertificateValidationCallback = certificateValidationCallback
        };

        return quicClient.ConnectAsync(options, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        return _connection?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
