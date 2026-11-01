using VpnHood.Net.Quic.Abstractions;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Http3;

// One instance per connection. Control and QPACK streams may arrive concurrently, but each type
// has exactly one owner. The front advertises zero QPACK capacity and never sends dynamic references.
internal sealed class Http3ControlStreams(Http3ServerOptions options,
    IHttp3GracefulCloseCallbacks gracefulCloseCallbacks) : IHttp3ReadPendingCallbacks
{
    private int _controlOpened;
    private int _encoderOpened;
    private int _decoderOpened;
    private bool _gracefulCloseRequested;
    private Http3StreamReader? _controlReader;

    void IHttp3ReadPendingCallbacks.OnReadPending(Http3StreamReader reader)
    {
        // Only the control reader can finish GOAWAY validation; another critical stream waiting for
        // input says nothing about coalesced control frames that have not yet been checked.
        if (ReferenceEquals(_controlReader, reader) && _gracefulCloseRequested)
            gracefulCloseCallbacks.OnControlInputPending();
    }

    public async Task Process(Http3StreamReader reader, IQuicStream stream, CancellationToken cancellationToken)
    {
        var type = await ReadStreamType(reader, cancellationToken).Vhc();
        if (type is null)
            return;

        reader.IsCritical = type is 0 or 2 or 3;
        try {
            switch (type) {
                case 0:
                    if (Interlocked.Exchange(ref _controlOpened, 1) != 0)
                        throw new Http3ProtocolException(Http3Protocol.StreamCreationError);
                    _controlReader = reader;
                    await ProcessControl(reader, cancellationToken).Vhc();
                    break;
                case 2:
                    if (Interlocked.Exchange(ref _encoderOpened, 1) != 0)
                        throw new Http3ProtocolException(Http3Protocol.StreamCreationError);
                    await DrainQpack(reader, cancellationToken).Vhc();
                    break;
                case 3:
                    if (Interlocked.Exchange(ref _decoderOpened, 1) != 0)
                        throw new Http3ProtocolException(Http3Protocol.StreamCreationError);
                    await DrainQpack(reader, cancellationToken).Vhc();
                    break;
                default:
                    // Reference compatibility: unsupported types, including client push streams, reset
                    // only that stream. They do not end an otherwise usable connection.
                    stream.AbortRead(Http3Protocol.StreamCreationError);
                    break;
            }
        }
        catch (EndOfStreamException) {
            throw new Http3ProtocolException(Http3Protocol.ClosedCriticalStream);
        }
    }

    private async Task<long?> ReadStreamType(Http3StreamReader reader, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.HeadersTimeout);
        try {
            return await reader.ReadIntegerOrEnd(timeout.Token).Vhc();
        }
        catch (EndOfStreamException) {
            // RFC 9114 permits a stream to end before its complete type is received.
            return null;
        }
    }

    private async Task ProcessControl(Http3StreamReader reader, CancellationToken cancellationToken)
    {
        var settingsReceived = false;
        while (true) {
            var type = await reader.ReadInteger(cancellationToken).Vhc();
            var length = await reader.ReadInteger(cancellationToken).Vhc();
            // Kestrel RC3 limits SETTINGS and unknown control payloads, not the frame envelope.
            // Its frame reader waits for a first payload byte before dispatching a nonempty frame.
            var limitsPayload = type is not (0 or 1 or 3 or 5 or 7 or 13);
            if (limitsPayload && length >= options.MaxControlFrameBytes) {
                if (await reader.ReadByte(cancellationToken).Vhc() < 0)
                    throw new EndOfStreamException();
                throw new Http3ProtocolException(Http3Protocol.FrameError);
            }
            if (!settingsReceived) {
                if (type is 0 or 1 or 5)
                    throw new Http3ProtocolException(Http3Protocol.FrameUnexpected);
                if (type != 4)
                    throw new Http3ProtocolException(Http3Protocol.MissingSettings);
                var settings = await reader.ReadPayload(length, options.MaxControlFrameBytes - 1,
                    cancellationToken).Vhc();
                ValidateSettings(settings);
                settingsReceived = true;
                continue;
            }

            switch (type) {
                case 0: // DATA
                case 1: // HEADERS
                case 4: // A second SETTINGS frame
                case 5: // PUSH_PROMISE
                    throw new Http3ProtocolException(Http3Protocol.FrameUnexpected);
                case 3: // CANCEL_PUSH
                case 0x0d: // MAX_PUSH_ID
                    // The reference checks integer framing without retaining push IDs or their order.
                    await ReadSingleInteger(reader, length, cancellationToken).Vhc();
                    break;
                case 7: // Client GOAWAY requests a graceful close; its push ID is otherwise unused.
                    await ReadSingleInteger(reader, length, cancellationToken).Vhc();
                    gracefulCloseCallbacks.BeginGracefulClose();
                    // Keep validating coalesced frames first: GOAWAY followed by an invalid DATA
                    // frame must still report that protocol error, not an early normal close.
                    _gracefulCloseRequested = true;
                    break;
                default:
                    // The reference also ignores HTTP/2-reserved frame types on its control stream.
                    // Discard accepted payloads in bounded chunks; never allocate by their declared size.
                    await reader.Discard(length, cancellationToken).Vhc();
                    break;
            }
        }
    }

    private static void ValidateSettings(ReadOnlySpan<byte> payload)
    {
        var offset = 0;
        while (offset < payload.Length) {
            var identifier = Http3Protocol.ReadInteger(payload, ref offset);
            Http3Protocol.ReadInteger(payload, ref offset);
            // RFC 9114 permits rejecting duplicate IDs but does not require it. The reference accepts
            // them; this front retains no peer dynamic-table state, so repeated values cost no storage.
            if (identifier is 0 or >= 2 and <= 5)
                throw new Http3ProtocolException(Http3Protocol.SettingsError);
            // The reference accepts extension values without boolean checks. This front has no
            // WebTransport or datagram application, so those peer values do not enable any feature.
        }
    }

    private static async Task<long> ReadSingleInteger(Http3StreamReader reader, long length,
        CancellationToken cancellationToken)
    {
        if (length is not (1 or 2 or 4 or 8))
            throw new Http3ProtocolException(Http3Protocol.FrameError);
        var payload = await reader.ReadPayload(length, 8, cancellationToken).Vhc();
        var offset = 0;
        var value = Http3Protocol.ReadInteger(payload, ref offset);
        if (offset != payload.Length)
            throw new Http3ProtocolException(Http3Protocol.FrameError);
        return value;
    }

    private static async Task DrainQpack(Http3StreamReader reader, CancellationToken cancellationToken)
    {
        // The reference drains zero-capacity instruction streams without interpreting them.
        // HEADERS rejects dynamic references separately.
        while (await reader.DiscardSome(4096, cancellationToken).Vhc() != 0) {
        }
        throw new EndOfStreamException(); // Closing either critical QPACK stream still ends the connection.
    }
}
