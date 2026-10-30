using Microsoft.Extensions.Logging;
using VpnHood.Core.Server.ProbeResistance;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Net.Quic.Abstractions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Core.Server.Listeners.Http3;

// The listener owns transport acceptance; this feature owns HTTP/3, proof admission and connection
// lifetime. No HTTP/3 types or dependencies reach the native mobile client providers.
internal sealed class Http3ConnectionFront(IQuicServerConnection connection, SessionManager sessionManager,
    UnauthenticatedConnections unauthenticatedConnections,
    Func<IStreamConnection, CancellationToken, Task> processNewConnection) : IDisposable
{
    private readonly CancellationTokenSource _abort = new();
    private readonly CancellationTokenSource _idle = new();
    private readonly Lock _stateLock = new();
    private Stream? _outboundControl;
    private Task? _closeTask;
    private Task? _gracefulCloseTask;
    private int _closeCode;
    private int _activeRequests;
    private long _highestRequestStreamId = -4;
    private bool _proofAccepted;
    private bool _controlSettingsSent;
    private bool _gracefulCloseRequested;
    private bool _controlInputPending;

    public async Task Run(CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _abort.Token, _idle.Token);
        var token = lifetime.Token;
        _idle.CancelAfter(Http3Protocol.KeepAliveTimeout);
        // Native VPN clients may grant no incoming uni-stream credit. This single pending open must
        // not block incoming requests, nor allocate a pump or a buffer while waiting for that credit.
        var controlTask = Task.CompletedTask;
        var control = new Http3ControlStreams(BeginGracefulClose, OnControlInputPending);
        var streams = new List<Task>();
        try {
            // Count the whole transport once, including idle connections and streams that never prove
            // a key. This identity is separate from TCP and other QUIC connections using the same tuple.
            unauthenticatedConnections.Add(new IPEndPointPair(connection.LocalEndPoint, connection.RemoteEndPoint),
                this, this);
            controlTask = SendControl(token);
            while (!token.IsCancellationRequested) {
                streams.RemoveAll(task => task.IsCompleted);
                if (streams.Count >= Http3Protocol.MaxConcurrentStreams)
                    await Task.WhenAny(streams).WaitAsync(token).Vhc();
                var stream = await connection.AcceptInboundStreamAsync(token).Vhc();
                streams.Add(ProcessStream(stream, control, token));
            }
        }
        catch (OperationCanceledException) {
            // Shutdown, idle expiry and protocol failure all end this connection's accept loop.
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "HTTP/3 connection ended. RemoteEp: {RemoteEp}",
                VhLogger.Format(connection.RemoteEndPoint));
        }
        finally {
            unauthenticatedConnections.Remove(this, this);
            RequestClose(Http3Protocol.NoError);
            await lifetime.TryCancelAsync().Vhc();
            if (_gracefulCloseTask != null)
                await _gracefulCloseTask.Vhc();
            if (_closeTask != null)
                await _closeTask.Vhc();
            await controlTask.Vhc();
            try {
                await Task.WhenAll(streams).Vhc();
            }
            catch (Exception ex) {
                // A faulty provider or stream callback must not skip native connection cleanup.
                VhLogger.Instance.LogError(ex, "Error completing HTTP/3 streams during shutdown.");
            }
            if (_outboundControl != null)
                await _outboundControl.TryDisposeAsync(Http3Protocol.CloseTimeout).Vhc();
            await connection.TryDisposeAsync(Http3Protocol.CloseTimeout).Vhc();
            _abort.Dispose();
            _idle.Dispose();
        }
    }

    private async Task SendControl(CancellationToken cancellationToken)
    {
        try {
            _outboundControl = await connection.OpenOutboundUnidirectionalStreamAsync(cancellationToken).Vhc();
            await _outboundControl.WriteAsync(Http3Protocol.ControlStream, cancellationToken).Vhc();
            Volatile.Write(ref _controlSettingsSent, true);
            // Zero dynamic QPACK tables need no outbound encoder/decoder streams. A reset of the
            // one critical control stream still closes the connection, even after SETTINGS was sent.
            await ((IQuicStream)_outboundControl).WritesClosed.WaitAsync(cancellationToken).Vhc();
            RequestClose(Http3Protocol.ClosedCriticalStream);
        }
        catch (OperationCanceledException) {
            if (!cancellationToken.IsCancellationRequested)
                RequestClose(Http3Protocol.ClosedCriticalStream);
        }
        catch (Exception) {
            if (!cancellationToken.IsCancellationRequested)
                RequestClose(Http3Protocol.ClosedCriticalStream);
        }
    }

    private async Task ProcessStream(Stream stream, Http3ControlStreams control, CancellationToken cancellationToken)
    {
        var quic = stream as IQuicStream;
        var bidirectional = false;
        Http3StreamReader? reader = null;
        Stream? replay = null;
        try {
            if (quic == null)
                throw new NotSupportedException("The server QUIC stream must expose stream control.");
            if (quic.CanWrite) {
                if (!TryStartRequest(quic.StreamId)) {
                    AbortStream(quic, Http3Protocol.RequestRejected);
                    return;
                }
                bidirectional = true;
            }
            if (!bidirectional) {
                reader = new Http3StreamReader(stream);
                await control.Process(reader, quic, cancellationToken).Vhc();
                return;
            }
            using var headers = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            headers.CancelAfter(Http3Protocol.HeadersTimeout);
            var admission = await Http3Admission.Read(stream, sessionManager, headers.Token).Vhc();
            replay = admission.Stream;
            if (admission.Authenticated) {
                if (!Interlocked.Exchange(ref _proofAccepted, true))
                    unauthenticatedConnections.Remove(this, this);
                headers.CancelAfter(Timeout.InfiniteTimeSpan);
                await RunTunnel(replay, cancellationToken).Vhc();
            }
            else {
                reader = new Http3StreamReader(replay);
                await Http3RequestStream.Process(stream, reader, quic, headers.Token, cancellationToken).Vhc();
            }
        }
        catch (OperationCanceledException) {
            if (!cancellationToken.IsCancellationRequested)
                AbortStream(quic, bidirectional ? Http3Protocol.RequestRejected : Http3Protocol.StreamCreationError);
        }
        catch (Http3ProtocolException ex) {
            if (ex.ConnectionError)
                RequestClose(ex.ErrorCode);
            else
                AbortStream(quic, ex.ErrorCode);
        }
        catch (Http3QpackException) {
            // Reference compatibility: Kestrel reports malformed QPACK as H3_INTERNAL_ERROR on
            // the request stream. Keep the codec's decompression error distinct from this wire policy.
            AbortStream(quic, Http3Protocol.InternalError);
        }
        catch (Http3HeaderLimitException) {
            AbortStream(quic, Http3Protocol.ExcessiveLoad);
        }
        catch (EndOfStreamException) {
            RequestClose(reader is { IsCritical: true } ? Http3Protocol.ClosedCriticalStream : Http3Protocol.FrameError);
        }
        catch (Exception ex) {
            if (quic == null)
                RequestClose(Http3Protocol.InternalError);
            if (reader is { IsCritical: true } && !cancellationToken.IsCancellationRequested)
                RequestClose(Http3Protocol.ClosedCriticalStream);
            VhLogger.Instance.LogDebug(ex, "HTTP/3 stream ended. RemoteEp: {RemoteEp}",
                VhLogger.Format(connection.RemoteEndPoint));
        }
        finally {
            if (reader != null)
                await reader.TryDisposeAsync(Http3Protocol.CloseTimeout).Vhc();
            if (replay != null)
                await replay.TryDisposeAsync().Vhc();
            await stream.TryDisposeAsync(Http3Protocol.CloseTimeout).Vhc();
            if (bidirectional)
                EndRequest();
        }
    }

    private async Task RunTunnel(Stream stream, CancellationToken cancellationToken)
    {
        var tunnel = new QuicStreamConnection(stream, connection.LocalEndPoint, connection.RemoteEndPoint,
            "tunnel", isServer: true);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        tunnel.Disposed += (_, _) => closed.TrySetResult();
        try {
            await processNewConnection(tunnel, cancellationToken).Vhc();
            // Ownership can pass to a proxy channel or reuse pool after the handshake callback.
            // Keep this request active until that owner closes it; busy tunnels must outlive HTTP idle expiry.
            await closed.Task.WaitAsync(cancellationToken).Vhc();
        }
        finally {
            await tunnel.DisposeAsync().Vhc();
        }
    }

    private void RequestClose(long errorCode)
    {
        lock (_stateLock) {
            if (_closeCode != 0)
                return;
            _closeCode = (int)errorCode;
            // Start the native close before cancellation lets stream disposal send a default reset.
            // Otherwise a probe can observe that reset instead of the intended HTTP/3 connection error.
            _closeTask = CloseTransport(errorCode);
            _abort.TryCancel();
        }
    }

    private async Task CloseTransport(long errorCode)
    {
        using var timeout = new CancellationTokenSource(Http3Protocol.CloseTimeout);
        try {
            await connection.CloseAsync(errorCode, timeout.Token).Vhc();
        }
        catch (OperationCanceledException) {
            // A peer that stops acknowledging cannot extend server shutdown.
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "Error closing HTTP/3 connection.");
        }
    }

    private void BeginGracefulClose()
    {
        lock (_stateLock)
            _gracefulCloseRequested = true;
    }

    private void OnControlInputPending()
    {
        bool close;
        lock (_stateLock) {
            _controlInputPending = true;
            close = _activeRequests == 0;
        }
        if (close)
            FinishGracefulClose();
    }

    private void FinishGracefulClose()
    {
        lock (_stateLock) {
            if (_closeCode != 0 || _gracefulCloseTask != null)
                return;
            // Publish the task before it can enter RequestClose on another thread. Shutdown joins
            // it, including when a protocol error interrupts the final control-stream write.
            _gracefulCloseTask = Task.Run(SendFinalGoAwayAndClose);
        }
    }

    private async Task SendFinalGoAwayAndClose()
    {
        Task? write = null;
        try {
            // A peer granting no uni-stream credit must not delay close. A completed SETTINGS write
            // also guarantees this write cannot overlap it; only this task sends the final GOAWAY.
            if (Volatile.Read(ref _controlSettingsSent)) {
                long nextStreamId;
                lock (_stateLock)
                    nextStreamId = _highestRequestStreamId + 4;
                using var payload = new MemoryStream(8);
                Http3Protocol.WriteInteger(payload, nextStreamId);
                var frame = Http3Protocol.Frame(7, payload.GetBuffer().AsSpan(0, (int)payload.Length));
                write = _outboundControl!.WriteAsync(frame, _abort.Token).AsTask();
                // Time out this wait, not the native write. Canceling a QUIC write first would reset
                // the critical control stream before the normal connection close reaches the peer.
                await write.WaitAsync(Http3Protocol.CloseTimeout).Vhc();
            }
        }
        catch (TimeoutException) {
            // RequestClose below starts the native close before canceling the unfinished write.
        }
        catch (OperationCanceledException) {
            // An earlier connection close can end this optional final write.
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "Could not send the final HTTP/3 GOAWAY.");
        }
        finally {
            // Keep native close before cancellation, as on error paths. An earlier protocol error
            // keeps its code; the graceful task never reserves NoError before its write completes.
            RequestClose(Http3Protocol.NoError);
            if (write != null) {
                try { await write.Vhc(); }
                catch (Exception) {
                    // Observe the original operation after native close cancels it, before Run
                    // disposes the control stream. Its failure was handled by the close above.
                }
            }
        }
    }

    private bool TryStartRequest(long streamId)
    {
        lock (_stateLock) {
            // Existing requests (including admitted tunnels) drain normally after client GOAWAY.
            // New streams must not prolong that drain or reach the authentication callback.
            if (_gracefulCloseRequested)
                return false;
            _highestRequestStreamId = Math.Max(_highestRequestStreamId, streamId);
            _activeRequests++;
            _idle.CancelAfter(Timeout.InfiniteTimeSpan);
            return true;
        }
    }

    private void EndRequest()
    {
        var close = false;
        lock (_stateLock) {
            if (--_activeRequests == 0) {
                close = _gracefulCloseRequested && _controlInputPending;
                if (!close)
                    _idle.CancelAfter(Http3Protocol.KeepAliveTimeout);
            }
        }
        if (close)
            FinishGracefulClose();
    }

    private static void AbortStream(IQuicStream? stream, long errorCode)
    {
        try {
            if (stream is { CanRead: true })
                stream.AbortRead(errorCode);
            if (stream is { CanWrite: true })
                stream.AbortWrite(errorCode);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "Error aborting HTTP/3 stream.");
        }
    }

    // The admission budget evicts synchronously. Start the close without waiting; Run owns all
    // asynchronous cleanup. Proving one stream only releases the budget, never later streams' checks.
    public void Dispose() => RequestClose(Http3Protocol.ExcessiveLoad);
}
