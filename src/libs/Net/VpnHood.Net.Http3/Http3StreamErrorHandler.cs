using VpnHood.Net.Quic.Abstractions;

namespace VpnHood.Net.Http3;

internal sealed class Http3StreamErrorHandler(IQuicStream quic, Http3StreamReader reader, Action<long> close,
    Action<IQuicStream, long> abortStream, CancellationToken cancellationToken)
{
    private int _handled;

    public void Handle(Exception error)
    {
        if (Interlocked.Exchange(ref _handled, 1) != 0)
            return;
        switch (error) {
            case OperationCanceledException:
                if (!cancellationToken.IsCancellationRequested)
                    abortStream(quic, quic.CanWrite ? Http3Protocol.RequestRejected : Http3Protocol.StreamCreationError);
                break;
            case Http3ProtocolException protocolError:
                if (protocolError.ConnectionError)
                    close(protocolError.ErrorCode);
                else
                    abortStream(quic, protocolError.ErrorCode);
                break;
            case Http3QpackException:
                abortStream(quic, Http3Protocol.InternalError);
                break;
            case EndOfStreamException:
                close(reader.IsCritical ? Http3Protocol.ClosedCriticalStream : Http3Protocol.FrameError);
                break;
            default:
                if (reader.IsCritical && !cancellationToken.IsCancellationRequested)
                    close(Http3Protocol.ClosedCriticalStream);
                break;
        }
    }
}
