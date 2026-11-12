using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Memory;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Core.Tunneling.Channels.Streams;

namespace VpnHood.Core.Tunneling.Connections;

/// <summary>
/// Represents the method that takes a connection whose logical stream has ended cleanly, under its next logical stream,
/// to keep for another request or to close.
/// </summary>
/// <param name="reusableStreamConnection">The connection, under its next logical stream.</param>
public delegate void ReuseConnectionCallback(ReusableStreamConnection reusableStreamConnection);

/// <summary>Represents a connection that carries one logical stream of the many its transport carries in turn.</summary>
/// <remarks>
/// Its release, <see cref="DisposeAsync"/>, ends that stream with the peer and, if it ends cleanly, hands the
/// transport, under its next logical stream, to the reuse callback, which keeps or closes it.
/// <see cref="PreventReuse"/>, an unclean end or no callback closes the transport instead. Its quick close,
/// <see cref="Dispose"/>, closes the transport at once, with no end of the logical stream and no reuse.
/// </remarks>
public class ReusableStreamConnection : IStreamConnection
{
    private readonly IStreamConnection _transport;
    private readonly ChunkStream _stream;
    private readonly ReuseConnectionCallback? _reuseCallback;
    private readonly AsyncLock _disposeLock = new();
    private volatile bool _reusePrevented;
    private bool _disposed;
    private bool _transportTaken; // closed by the quick close or handed on by the release, whichever came first

    /// <summary>Initializes a new instance of the <see cref="ReusableStreamConnection"/> class.</summary>
    /// <param name="transport">The connection that carries the logical streams.</param>
    /// <param name="stream">The logical stream this connection carries.</param>
    /// <param name="reuseCallback">
    /// The method that takes the transport for another request, or <see langword="null"/> to close it at the release.
    /// </param>
    public ReusableStreamConnection(IStreamConnection transport, ChunkStream stream,
        ReuseConnectionCallback? reuseCallback)
    {
        _transport = transport;
        _stream = stream;
        _reuseCallback = reuseCallback;
        VhTypeTracker.Track(this);

        VhLogger.Instance.LogDebug(GeneralEventId.Stream,
            "A ReusableConnection has been created. ConnectionId: {ConnectionId}, StreamType: {StreamType}, LocalEp: {LocalEp}, RemoteEp: {RemoteEp}",
            ConnectionId, stream.GetType().Name, VhLogger.Format(LocalEndPoint), VhLogger.Format(RemoteEndPoint));
    }

    /// <inheritdoc/>
    public bool Connected => !_disposed && _transport.Connected;

    /// <inheritdoc/>
    /// <remarks>It is the logical stream.</remarks>
    public Stream Stream => _stream;

    /// <inheritdoc/>
    public IPEndPoint LocalEndPoint => _transport.LocalEndPoint;

    /// <inheritdoc/>
    public IPEndPoint RemoteEndPoint => _transport.RemoteEndPoint;

    /// <inheritdoc/>
    public bool IsServer => _transport.IsServer;

    /// <inheritdoc/>
    public string ConnectionName => _transport.ConnectionName;

    /// <inheritdoc/>
    /// <remarks>Setting it sets the logical stream's ID too.</remarks>
    public string ConnectionId {
        get => _transport.ConnectionId;
        set {
            _transport.ConnectionId = value;
            _stream.StreamId = value;
        }
    }

    /// <inheritdoc/>
    public bool RequireHttpResponse {
        get => _transport.RequireHttpResponse;
        set => _transport.RequireHttpResponse = value;
    }

    /// <inheritdoc/>
    public void PreventReuse() => _reusePrevented = true;

    // the transport is this release's to close or hand on, unless a quick close takes it first
    private async Task Release()
    {
        // up to the hand-off, any failure is logged and closes
        try {
            if (_reusePrevented || _reuseCallback == null) {
                await CloseAsync().Vhc();
                return;
            }

            // ends the logical stream with the peer; it throws when the stream did not end cleanly, or when a quick
            // close has closed the transport meanwhile
            var nextStream = await _stream.CreateReuse().Vhc();
            if (_reusePrevented) {
                await CloseAsync().Vhc(); // prevented while the stream was ending
                return;
            }

            // a quick close that came meanwhile has closed the transport
            if (Interlocked.Exchange(ref _transportTaken, true)) {
                await nextStream.DisposeAsync().Vhc();
                return;
            }

            VhTypeTracker.Track(nextStream, $"ReusableStreamConnection.{nextStream.GetType().Name}");
            var reusedConnection = new ReusableStreamConnection(_transport, nextStream, _reuseCallback);
            VhTypeTracker.Record("ReusableStreamConnection.reused");

            // the next request on the connection starts clean, not inside this one's log scopes, which
            // would nest deeper with each reuse
            using var suppressFlow = ExecutionContext.SuppressFlow();
            _ = Task.Run(() => _reuseCallback(reusedConnection))
                .ContinueWith(task => {
                    VhLogger.Instance.LogError(GeneralEventId.Stream, task.Exception,
                        "Reuse callback failed. ConnectionId: {ConnectionId}", reusedConnection.ConnectionId);
                    reusedConnection.PreventReuse();
                    reusedConnection.Dispose();
                }, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(GeneralEventId.Stream, ex,
                "Could not release the connection. ConnectionId: {ConnectionId}", ConnectionId);
            await CloseAsync().Vhc();
        }
    }

    // The transport's graceful close, which ends the logical stream with it; the stream's own close would first end
    // that with the peer. A quick close that comes meanwhile cuts it short
    private async Task CloseAsync()
    {
        await _transport.DisposeAsync().Vhc();
        VhTypeTracker.Record("ReusableStreamConnection.disposed");
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The quick close: it closes the transport at once, with no end of the logical stream and no reuse, unless the
    /// release has handed the transport on.
    /// </remarks>
    public void Dispose()
    {
        _disposed = true;
        if (Interlocked.Exchange(ref _transportTaken, true))
            return;

        _transport.Dispose();
        VhTypeTracker.Record("ReusableStreamConnection.disposed");
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The release: it ends the logical stream with the peer and hands the transport to the reuse callback, or closes
    /// it; a failure is logged and closes it. A second call waits for the first one's end.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        // a second close waits for the first one's end. One after a quick close returns: that close has closed the
        // transport
        using var scopeLock = await _disposeLock.LockAsync(CancellationToken.None).Vhc();
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        await Release().Vhc();
    }
}
