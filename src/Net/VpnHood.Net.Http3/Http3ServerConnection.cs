using Microsoft.Extensions.Logging;
using VpnHood.Net.Quic.Abstractions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Net.Http3;

public sealed class Http3ServerConnection(IQuicServerConnection connection, Http3ServerOptions options,
    IHttp3StreamFilter filter, IHttp3StreamRunner runner, IHttp3Responder responder)
{
    private readonly Http3ServerOptions _options = options.Validate();
    private readonly IHttp3TrailerScheduleSampler _trailerScheduleSampler = Http3TrailerScheduleSampler.Shared;
    private readonly CancellationTokenSource _abort = new();
    private readonly CancellationTokenSource _idle = new();
    private readonly Lock _stateLock = new();
    private readonly Http3StreamContext _context = new(connection.LocalEndPoint, connection.RemoteEndPoint);
    private Stream? _outboundControl;
    private Task? _closeTask;
    private Task? _gracefulCloseTask;
    private bool _closeRequested;
    private int _activeRequests;
    private long _highestRequestStreamId = -4;
    private bool _controlSettingsSent;
    private bool _gracefulCloseRequested;
    private bool _controlInputPending;

    internal Http3ServerConnection(IQuicServerConnection connection, Http3ServerOptions options,
        IHttp3StreamFilter filter, IHttp3StreamRunner runner, IHttp3Responder responder,
        IHttp3TrailerScheduleSampler trailerScheduleSampler) : this(connection, options, filter, runner, responder)
    {
        _trailerScheduleSampler = trailerScheduleSampler;
    }

    public async Task Run(CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _abort.Token, _idle.Token);
        var token = lifetime.Token;
        _idle.CancelAfter(_options.KeepAliveTimeout);
        var control = new Http3ControlStreams(_options, BeginGracefulClose, OnControlInputPending);
        var streams = new List<Task>();
        // A peer granting no incoming uni-stream credit must not block request acceptance.
        var controlTask = SendControl(token);
        try {
            while (!token.IsCancellationRequested) {
                streams.RemoveAll(task => task.IsCompleted);
                if (streams.Count >= _options.MaxConcurrentStreams)
                    await Task.WhenAny(streams).WaitAsync(token).Vhc();
                var stream = await connection.AcceptInboundStreamAsync(token).Vhc();
                streams.Add(ProcessStream(stream, control, token));
                if (IsGracefulDrainComplete())
                    FinishGracefulClose();
            }
        }
        catch (OperationCanceledException) {
            // Shutdown, idle expiry and protocol failure end the accept loop.
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "HTTP/3 connection ended. RemoteEp: {RemoteEp}",
                VhLogger.Format(connection.RemoteEndPoint));
        }
        finally {
            Close(Http3Protocol.NoError);
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
                // A faulty provider must not skip connection cleanup.
                VhLogger.Instance.LogError(ex, "Error completing HTTP/3 streams during shutdown.");
            }
            if (_outboundControl != null)
                await _outboundControl.TryDisposeAsync(_options.CloseTimeout).Vhc();
            await connection.TryDisposeAsync(_options.CloseTimeout).Vhc();
            _abort.Dispose();
            _idle.Dispose();
        }
    }

    private async Task SendControl(CancellationToken cancellationToken)
    {
        try {
            var stream = await connection.OpenOutboundUnidirectionalStreamAsync(cancellationToken).Vhc();
            _outboundControl = stream;
            await stream.WriteAsync(Http3Protocol.CreateControlStream(_options.MaxHeaderBytes), cancellationToken).Vhc();
            Volatile.Write(ref _controlSettingsSent, true);
            // Zero dynamic QPACK tables need no outbound encoder/decoder streams.
            await ((IQuicStream)stream).WritesClosed.WaitAsync(cancellationToken).Vhc();
            Close(Http3Protocol.ClosedCriticalStream);
        }
        catch (OperationCanceledException) {
            if (!cancellationToken.IsCancellationRequested)
                Close(Http3Protocol.ClosedCriticalStream);
        }
        catch (Exception) {
            if (!cancellationToken.IsCancellationRequested)
                Close(Http3Protocol.ClosedCriticalStream);
        }
    }

    private async Task ProcessStream(Stream stream, Http3ControlStreams control, CancellationToken cancellationToken)
    {
        var quic = stream as IQuicStream;
        var requestStarted = quic is { CanWrite: true } && TryStartRequest(quic.StreamId);
        try {
            if (quic == null) {
                Close(Http3Protocol.InternalError);
                throw new NotSupportedException("The server QUIC stream must expose stream control.");
            }
            if (!quic.CanWrite) {
                await ProcessHttp3(stream, stream, quic, control, cancellationToken, cancellationToken).Vhc();
                return;
            }
            if (!requestStarted) {
                AbortStream(quic, Http3Protocol.RequestRejected);
                return;
            }
            await ProcessRequest(stream, quic, control, cancellationToken).Vhc();
        }
        catch (OperationCanceledException) {
            // Each protocol handles its own deadlines; connection shutdown only releases the stream.
        }
        catch (Exception ex) {
            // Claimed streams never enter HTTP/3 error mapping, even when their runner ends at FIN.
            VhLogger.Instance.LogDebug(ex, "QUIC stream ended. RemoteEp: {RemoteEp}",
                VhLogger.Format(connection.RemoteEndPoint));
        }
        finally {
            await stream.TryDisposeAsync(_options.CloseTimeout).Vhc();
            if (requestStarted)
                EndRequest();
        }
    }

    private async Task ProcessRequest(Stream stream, IQuicStream quic, Http3ControlStreams control,
        CancellationToken cancellationToken)
    {
        using var headers = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        headers.CancelAfter(_options.HeadersTimeout);
        var admission = await ReadAdmission(stream, quic, headers.Token, cancellationToken).Vhc();
        await using var replay = admission.Stream;
        if (admission.Claimed) {
            headers.CancelAfter(Timeout.InfiniteTimeSpan);
            await runner.Run(_context, replay, cancellationToken).Vhc();
        }
        else {
            await ProcessHttp3(replay, stream, quic, control, headers.Token, cancellationToken).Vhc();
        }
    }

    private async Task<(Http3PrefixStream Stream, bool Claimed)> ReadAdmission(Stream stream, IQuicStream quic,
        CancellationToken headersToken, CancellationToken cancellationToken)
    {
        try {
            return await Http3Admission.Read(stream, _context, filter, _options.MaxFilterBytes, headersToken).Vhc();
        }
        catch (OperationCanceledException) {
            if (!cancellationToken.IsCancellationRequested)
                AbortStream(quic, Http3Protocol.RequestRejected);
            throw;
        }
    }

    private async Task ProcessHttp3(Stream input, Stream output, IQuicStream quic, Http3ControlStreams control,
        CancellationToken headersToken, CancellationToken cancellationToken)
    {
        var reader = new Http3StreamReader(input);
        var errors = new Http3StreamErrorHandler(quic, reader, Close, AbortStream, cancellationToken);
        try {
            if (quic.CanWrite)
                await Http3RequestStream.Process(output, reader, quic, errors.Handle, responder,
                    _options, _trailerScheduleSampler, headersToken, cancellationToken).Vhc();
            else
                await control.Process(reader, quic, cancellationToken).Vhc();
        }
        catch (OperationCanceledException ex) {
            errors.Handle(ex);
        }
        catch (Exception ex) {
            errors.Handle(ex);
            if (ex is not (Http3ProtocolException or Http3QpackException or EndOfStreamException))
                throw;
        }
        finally {
            await reader.TryDisposeAsync(_options.CloseTimeout).Vhc();
        }
    }

    // Native close starts synchronously; Run owns and joins all asynchronous cleanup.
    public void Close(long errorCode)
    {
        lock (_stateLock) {
            if (_closeRequested)
                return;
            _closeRequested = true;
            // Native close must precede cancellation, or stream disposal can expose a default reset.
            _closeTask = CloseTransport(errorCode);
            _abort.TryCancel();
        }
    }

    private async Task CloseTransport(long errorCode)
    {
        using var timeout = new CancellationTokenSource(_options.CloseTimeout);
        try {
            await connection.CloseAsync(errorCode, timeout.Token).Vhc();
        }
        catch (OperationCanceledException) {
            // A peer that stops acknowledging cannot extend shutdown.
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "Error closing HTTP/3 connection.");
        }
    }

    private void BeginGracefulClose()
    {
        lock (_stateLock) {
            _gracefulCloseRequested = true;
            _idle.CancelAfter(Timeout.InfiniteTimeSpan);
        }
    }

    private void OnControlInputPending()
    {
        if (MarkControlInputPending())
            FinishGracefulClose();
    }

    private bool MarkControlInputPending()
    {
        lock (_stateLock) {
            if (_controlInputPending)
                return false;
            _controlInputPending = true;
            return _activeRequests == 0;
        }
    }

    private bool IsGracefulDrainComplete()
    {
        lock (_stateLock)
            return _gracefulCloseRequested && _controlInputPending && _activeRequests == 0;
    }

    private void FinishGracefulClose()
    {
        lock (_stateLock) {
            if (_closeRequested || _gracefulCloseTask != null)
                return;
            // Publish before the task can enter Close; Run must join even an interrupted write.
            _gracefulCloseTask = Task.Run(() => SendFinalGoAwayAndClose(_abort.Token));
        }
    }

    private async Task SendFinalGoAwayAndClose(CancellationToken cancellationToken)
    {
        var write = SendFinalGoAway(cancellationToken);
        try {
            // Time out the wait, not the native write: a canceled write resets the control stream.
            await write.WaitAsync(_options.CloseTimeout, cancellationToken).Vhc();
        }
        catch (TimeoutException) {
            // Close below starts native close before canceling the unfinished write.
        }
        catch (OperationCanceledException) {
            // An earlier connection close can end this optional final write.
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "Could not send the final HTTP/3 GOAWAY.");
        }
        finally {
            Close(Http3Protocol.NoError);
            try { await write.Vhc(); }
            catch (Exception) {
                // Observe the write before Run disposes the stream; its failure is handled above.
            }
        }
    }

    private async Task SendFinalGoAway(CancellationToken cancellationToken)
    {
        // SETTINGS completion excludes concurrent writes and a pending uni-stream open.
        if (!Volatile.Read(ref _controlSettingsSent) || _outboundControl is not { } control)
            return;
        using var payload = new MemoryStream(8);
        Http3Protocol.WriteInteger(payload, GetNextRequestStreamId());
        var frame = Http3Protocol.Frame(7, payload.GetBuffer().AsSpan(0, (int)payload.Length));
        await control.WriteAsync(frame, cancellationToken).Vhc();
    }

    private long GetNextRequestStreamId()
    {
        lock (_stateLock)
            return _highestRequestStreamId + 4;
    }

    private bool TryStartRequest(long streamId)
    {
        lock (_stateLock) {
            // Requests already running drain normally; new streams cannot prolong that drain.
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
        lock (_stateLock) {
            if (--_activeRequests != 0)
                return;
            // RC3 resumes a completed GOAWAY drain only when its accept loop next wakes.
            if (!_gracefulCloseRequested)
                _idle.CancelAfter(_options.KeepAliveTimeout);
        }
    }

    private static void AbortStream(IQuicStream stream, long errorCode)
    {
        try {
            if (stream.CanRead)
                stream.AbortRead(errorCode);
            if (stream.CanWrite)
                stream.AbortWrite(errorCode);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "Error aborting HTTP/3 stream.");
        }
    }
}
