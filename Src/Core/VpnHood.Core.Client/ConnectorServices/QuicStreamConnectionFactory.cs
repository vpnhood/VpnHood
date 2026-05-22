using System.Net;
using System.Net.Security;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Connections;

namespace VpnHood.Core.Client.ConnectorServices;

/// <summary>
/// Manages a pool of QUIC connections to the server, multiplexing streams across them.
/// </summary>
/// <remarks>
/// Unlike TCP where each tunnel request gets its own connection, QUIC uses a single underlying
/// <see cref="System.Net.Quic.QuicConnection"/> and opens lightweight bidirectional streams over it.
/// To avoid overwhelming a single connection, this factory enforces two limits per connection:
/// <list type="bullet">
///   <item><term>MaxStreamsPerConnection</term><description>Maximum simultaneously open streams. When reached, a new QUIC connection is established.</description></item>
///   <item><term>MaxLifetimeStreamsPerConnection</term><description>Maximum total streams ever opened on a connection. Once reached the connection is "retired" — no new streams are opened on it, and it is disposed automatically after all its active streams are closed.</description></item>
/// </list>
/// Slot reservation (incrementing active/total counters) happens under the factory lock before the async
/// stream-open call, preventing race conditions where two callers both see capacity and both proceed.
/// </remarks>
internal class QuicStreamConnectionFactory(
    VpnEndPoint vpnEndPoint,
    RemoteCertificateValidationCallback certificateValidationCallback)
    : IAsyncDisposable
{
    private readonly List<QuicStreamConnectionItem> _connections = [];
    private int _isDisposed;

    public int MaxStreamsPerConnection { get; set; } = 30;
    public int MaxLifetimeStreamsPerConnection { get; set; } = 500;
    public IPEndPoint? QuicEndPoint { get; set; }

    public async Task<IStreamConnection> CreateConnection(string connectionId, CancellationToken cancellationToken)
    {
        var streamConnectionItem = GetOrCreateConnectionItem();

        try {
            var stream = await streamConnectionItem.OpenStreamAsync(vpnEndPoint, certificateValidationCallback, cancellationToken).Vhc();
            var connection = new QuicStreamConnection(stream,
                localEndPoint: streamConnectionItem.Connection.LocalEndPoint,
                remoteEndPoint: streamConnectionItem.Connection.RemoteEndPoint,
                connectionName: "tunnel",
                isServer: false,
                connectionId: connectionId);
            connection.Disposed += (_, _) => OnStreamDisposed(streamConnectionItem);
            return connection;
        }
        catch {
            // Revert reservation on failure
            lock (_connections) {
                streamConnectionItem.ActiveStreamCount--;
                streamConnectionItem.TotalStreamCount--;
            }
            throw;
        }
    }

    private void OnStreamDisposed(QuicStreamConnectionItem streamConnectionItem)
    {
        lock (_connections) {
            streamConnectionItem.ActiveStreamCount--;

            // If retired and no active streams, dispose and remove
            if (streamConnectionItem is { IsRetired: true, ActiveStreamCount: 0 }) {
                _connections.Remove(streamConnectionItem);
                _ = streamConnectionItem.SafeDisposeAsync();
            }
        }
    }

    private QuicStreamConnectionItem GetOrCreateConnectionItem()
    {
        lock (_connections) {
            // Find a connection with available capacity
            var item = _connections.FirstOrDefault(c => c.CanOpenStream);
            if (item != null) 
                return item;

            // Reserve the slot under lock to prevent over-subscription
            item = new QuicStreamConnectionItem(this);
            item.ActiveStreamCount++;
            item.TotalStreamCount++;
            _connections.Add(item);
            return item;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return;

        List<QuicStreamConnectionItem> connections;
        lock (_connections) {
            connections = [.. _connections];
            _connections.Clear();
        }

        // dispose all connections outside the lock with parallelism
        await Task.WhenAll(connections.Select(c => c.SafeDisposeAsync().AsTask())).Vhc();
    }
}