using System.Net;
using System.Net.Security;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Quic.Abstractions;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Memory;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
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
        // 2 s cadence: jammed-item disposal (the panic recycle has its own faster watchdog below).
        _cleanupJob = new Job(Cleanup, TimeSpan.FromSeconds(2), "QuicStreamConnectionCleanup");
        StartMemoryWatchdog();
    }

    // PANIC RECYCLE (memory-capped hosts): when the process footprint is within a few MB of the platform
    // kill limit (iOS extension jetsam ≈ 52 MB), dispose EVERY pooled connection. Network.framework grants
    // QUIC flow-control credit as IT buffers (not as the app reads), so under fast downloads or a stalled
    // path it hoards inbound/unacked frames in native malloc (device-measured 2026-07-12: malloc_small
    // 5 → 34-39 MB) that no managed budget can see or bound; disposing the connections is the only
    // guaranteed release, and flows re-establish over fresh connections. A brief tunnel blip beats a
    // jetsam kill. Threshold + urgency are device-measured: a speed-test burst climbs up to ~3 MB/s, and a
    // 2 s detection cadence lost that race by ~2 MB (run 9, died at 49.8 three seconds after a passing
    // 46.0 reading) — hence a dedicated 250 ms watchdog rather than the cleanup job.
    private const double PanicFootprintMb = 44.0;

    // iOS-only: the threshold is scaled to the ~52 MB Network Extension jetsam limit — on desktop-class
    // hosts a 45 MB footprint is normal and recycling there would be destructive. (Today only iOS installs
    // a footprint-reporting VhMemory, but guard by platform so a future desktop reader stays safe.)
    private void StartMemoryWatchdog()
    {
        if (!OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst())
            return;

        new Thread(() => {
            while (!_disposed) {
                try {
                    if (VhMemory.Instance.GetInfo().ProcessFootprintMb >= PanicFootprintMb) {
                        RecycleAll();
                        Thread.Sleep(2000); // let disposal free native buffers and the footprint recede
                        continue;
                    }
                }
                catch { /* the watchdog must never die */ }
                Thread.Sleep(250);
            }
        }) { IsBackground = true, Name = "QuicPoolMemoryWatchdog" }.Start();
    }

    public event EventHandler? PanicRecycled;

    private void RecycleAll()
    {
        List<QuicStreamConnectionItem> items;
        lock (_items) {
            items = [.. _items];
            _items.Clear();
        }

        if (items.Count > 0) {
            VhLogger.Instance.LogWarning(GeneralEventId.Request,
                "QUIC pool panic recycle: process footprint is near the platform kill limit. " +
                "Disposing all {Count} pooled connections to release native buffers.", items.Count);

            foreach (var item in items)
                _ = item.TryDisposeAsync();
        }

        PanicRecycled?.Invoke(this, EventArgs.Empty);
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

        // Remove connections that are jammed (bad connection — disposed IMMEDIATELY, zombie streams and
        // all, to free the native buffers nw hoards on a stalled path) or that drained naturally (dead or
        // idle-expired with no active streams). The footprint panic recycle runs in its own watchdog.
        lock (_items) {
            foreach (var item in _items.ToArray()) {
                var drained = item.ActiveStreamCount == 0 &&
                              (item.IsDead || item.ZeroActiveSince + IdleConnectionTimeout <= FastDateTime.Now);
                if (item.IsJammed || drained) {
                    _items.Remove(item);
                    disposeTasks.Add(item.TryDisposeAsync().AsTask());
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

        return new ValueTask(Task.WhenAll(connections.Select(c => c.TryDisposeAsync().AsTask())));
    }
}
