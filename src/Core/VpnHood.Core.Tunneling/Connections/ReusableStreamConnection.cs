using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Memory;
using VpnHood.Core.Tunneling.Channels.Streams;

namespace VpnHood.Core.Tunneling.Connections;

public delegate void ReuseConnectionCallback(ReusableStreamConnection reusableStreamConnection);

// Carries one logical stream of the many its transport carries in turn. Its release ends that stream with the peer
// and, if it ends cleanly, hands the transport, under its next logical stream, to the reuse callback, which keeps or
// closes it. PreventReuse, an unclean end or no callback closes the transport instead
public class ReusableStreamConnection : IStreamConnection
{
    private readonly IStreamConnection _transport;
    private readonly ChunkStream _stream;
    private readonly ReuseConnectionCallback? _reuseCallback;
    private volatile bool _reusePrevented;
    private bool _disposed;

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

    public bool Connected => !_disposed && _transport.Connected;
    public Stream Stream => _stream;
    public IPEndPoint LocalEndPoint => _transport.LocalEndPoint;
    public IPEndPoint RemoteEndPoint => _transport.RemoteEndPoint;
    public bool IsServer => _transport.IsServer;
    public string ConnectionName => _transport.ConnectionName;

    public string ConnectionId {
        get => _transport.ConnectionId;
        set {
            _transport.ConnectionId = value;
            _stream.StreamId = value;
        }
    }

    public bool RequireHttpResponse {
        get => _transport.RequireHttpResponse;
        set => _transport.RequireHttpResponse = value;
    }

    public void PreventReuse() => _reusePrevented = true;

    // from here on the transport is this release's alone: a Dispose meanwhile neither closes nor hands it on
    private async Task Release()
    {
        // up to the hand-off, any failure is logged and closes: nothing awaits a Dispose's release, and a Dispose
        // after this one would find nothing left to do
        try {
            if (_reusePrevented || _reuseCallback == null) {
                await CloseAsync().Vhc();
                return;
            }

            // ends the logical stream with the peer; it throws when the stream did not end cleanly
            var nextStream = await _stream.CreateReuse().Vhc();
            if (_reusePrevented) {
                await CloseAsync().Vhc(); // prevented while the stream was ending
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

    // The transport's async close: its sync one may wait for the peer, as a QUIC stream's does. It ends the logical
    // stream with it; the stream's own close would first end that with the peer
    private async Task CloseAsync()
    {
        await _transport.DisposeAsync().Vhc();
        VhTypeTracker.Record("ReusableStreamConnection.disposed");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true)) return;
        _ = Release();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, true)) return;
        await Release().Vhc();
    }
}
