using System.Net;
using System.Net.Security;
using VpnHood.Core.Quic.Abstractions;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Jobs;
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
///   <item><term>MaxLifetimeStreamsPerConnection</term><description>Maximum total streams ever opened on a connection. Once reached the connection is retired and disposed automatically after all its active streams are closed.</description></item>
/// </list>
/// These limits are soft limits, not strict hard caps.
/// Capacity checks in <see cref="QuicStreamConnectionItem.CanOpenStream"/> are intentionally lock-free,
/// so under heavy concurrency a small temporary exceed of stream/lifetime count can happen.
/// This behavior is expected and acceptable by design.
/// Idle items are cleaned by one periodic job based on each item's <c>ZeroActiveSince</c> timestamp.
/// </remarks>
internal class QuicStreamConnectionFactory : IAsyncDisposable
{
    private readonly IQuicClient _quicClient;
    private readonly VpnEndPoint _vpnEndPoint;
    private readonly RemoteCertificateValidationCallback _certificateValidationCallback;
    private readonly List<QuicStreamConnectionItem> _items = [];
    private readonly Job _cleanupJob;
    private bool _disposed;

    public int MaxStreamsPerConnection { get; set; } = 30;
    public int MaxLifetimeStreamsPerConnection { get; set; } = 500;
    public TimeSpan IdleConnectionTimeout { get; set; } = TimeSpan.FromMinutes(2);
    public IPEndPoint? QuicEndPoint { get; set; }

    public QuicStreamConnectionFactory(
        IQuicClient quicClient,
        VpnEndPoint vpnEndPoint,
        RemoteCertificateValidationCallback certificateValidationCallback)
    {
        _quicClient = quicClient;
        _vpnEndPoint = vpnEndPoint;
        _certificateValidationCallback = certificateValidationCallback;
        _cleanupJob = new Job(Cleanup, "QuicStreamConnectionCleanup");
    }

    public Task<IStreamConnection> CreateConnection(string connectionId, CancellationToken cancellationToken)
    {
        var item = GetOrCreateConnectionItem();
        return item.OpenStreamConnection(
            _vpnEndPoint,
            _certificateValidationCallback,
            connectionId,
            cancellationToken);
    }

    private QuicStreamConnectionItem GetOrCreateConnectionItem()
    {
        lock (_items) {
            var item = _items.FirstOrDefault(c => c.CanOpenStream);
            if (item != null)
                return item;

            var quicEndPoint = QuicEndPoint
                ?? throw new InvalidOperationException("QuicEndPoint has not been set.");

            item = new QuicStreamConnectionItem(
                quicClient: _quicClient,
                maxStreamsPerConnection: MaxStreamsPerConnection,
                maxLifetimeStreamsPerConnection: MaxLifetimeStreamsPerConnection,
                quicEndPoint: quicEndPoint);

            _items.Add(item);
            return item;
        }
    }

    private ValueTask Cleanup(CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        var disposeTasks = new List<Task>();

        // Remove idle connections with no active streams if they have been idle for too long,
        // if they are already dead, or if they have reached their lifetime stream limit.
        lock (_items) {
            foreach (var item in _items.Where(x=>x.ActiveStreamCount == 0).ToArray()) {
                if (item.IsDead || item.ZeroActiveSince + IdleConnectionTimeout <= FastDateTime.Now) {
                    _items.Remove(item);
                    disposeTasks.Add(item.SafeDisposeAsync().AsTask());
                }
            }
        }

        return new ValueTask(Task.WhenAll(disposeTasks));
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, true)) return ValueTask.CompletedTask;

        _cleanupJob.Dispose();

        List<QuicStreamConnectionItem> connections;
        lock (_items) {
            connections = [.. _items];
            _items.Clear();
        }

        return new ValueTask(Task.WhenAll(connections.Select(c => c.SafeDisposeAsync().AsTask())));
    }
}
