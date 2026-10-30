using VpnHood.Net.Quic.Abstractions;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Server.Listeners.Http3;

internal static class Http3RequestStream
{
    // Unrecognized frames are discarded in bounded chunks, never allocated by their advertised size.
    // A finite budget also stops a stream of empty extension frames from keeping a request alive forever.
    private const long MaxIgnoredBytes = 64 * 1024;

    public static async Task Process(Stream stream, Http3StreamReader reader, IQuicStream quic,
        CancellationToken headersToken, CancellationToken cancellationToken)
    {
        var head = await ReadHeaders(reader, headersToken).Vhc();
        if (!head.Completed) {
            quic.CompleteWrites();
            return;
        }
        // Count every inspected byte, including zero-length DATA frame headers and QPACK trailers.
        // A continuously readable peer must not postpone the application's final response forever.
        reader.AvailableBytesLimit = 64 * 1024;
        reader.ReadAvailableOnly = true;
        if (!head.ExceedsRequestLimits) {
            try {
                await ValidateAvailableBody(reader, head.ContentLength, head.IgnoredBytes, cancellationToken).Vhc();
            }
            catch (Exception ex) when (ex is Http3ReadPendingException or Http3ReadBudgetExceededException) {
                // The protected application does not consume a request body. Inspect bytes already
                // present within a finite budget, then respond without waiting for more input.
            }
        }
        var statusCode = head.ExceedsRequestLimits ? 431 : 401;
        var response = Http3Protocol.Frame(1, Http3Qpack.EncodeResponse(statusCode, DateTimeOffset.UtcNow));
        await stream.WriteAsync(response, headersToken).Vhc();
        quic.CompleteWrites();
        // The final response is the parsing boundary. Later malformed frames must not change the
        // completed challenge into a reset or close the connection. Dispose observes any pending read.
        quic.AbortRead(Http3Protocol.NoError);
    }

    private static async Task ValidateAvailableBody(Http3StreamReader reader, long? contentLength,
        long ignored, CancellationToken cancellationToken)
    {
        long bodyBytes = 0;
        var trailers = false;
        while (true) {
            var type = await reader.ReadIntegerOrEnd(cancellationToken).Vhc();
            if (type == null) {
                ValidateBodyLength(bodyBytes, contentLength);
                return;
            }
            var length = await reader.ReadInteger(cancellationToken).Vhc();
            if (type == 0) {
                if (trailers)
                    throw new Http3ProtocolException(Http3Protocol.FrameUnexpected);
                while (length > 0) {
                    var count = await reader.DiscardSome(length, cancellationToken).Vhc();
                    if (count == 0) {
                        ValidateBodyLength(bodyBytes, contentLength);
                        return;
                    }
                    bodyBytes += count;
                    length -= count;
                    if (contentLength != null && bodyBytes > contentLength)
                        throw new Http3ProtocolException(Http3Protocol.ProtocolError, connectionError: false);
                }
            }
            else if (type == 1) {
                if (trailers)
                    throw new Http3ProtocolException(Http3Protocol.FrameUnexpected);
                trailers = true;
                var trailerHeaders = new Http3RequestHeaders(trailers: true);
                var section = await Http3Qpack.ReadSection(reader, length, trailerHeaders.Append, cancellationToken).Vhc();
                if (!section.Completed)
                    return;
                trailerHeaders.Complete();
                if (section.ExceedsRequestLimits)
                    throw new Http3ProtocolException(Http3Protocol.RequestRejected, connectionError: false);
            }
            else {
                RejectUnexpected(type.Value, beforeHeaders: false);
                ignored = AddIgnored(ignored, length);
                await reader.Discard(length, cancellationToken).Vhc();
            }
        }
    }

    private static void ValidateBodyLength(long bodyBytes, long? contentLength)
    {
        if (contentLength != null && bodyBytes != contentLength)
            throw new Http3ProtocolException(Http3Protocol.ProtocolError, connectionError: false);
    }

    private readonly record struct RequestHead(bool Completed, bool ExceedsRequestLimits,
        long? ContentLength, long IgnoredBytes);

    private static async Task<RequestHead> ReadHeaders(Http3StreamReader reader, CancellationToken cancellationToken)
    {
        long ignored = 0;
        try {
            while (true) {
                var type = await reader.ReadInteger(cancellationToken).Vhc();
                var length = await reader.ReadInteger(cancellationToken).Vhc();
                if (type == 1) {
                    var headers = new Http3RequestHeaders(trailers: false);
                    var section = await Http3Qpack.ReadSection(reader, length, headers.Append, cancellationToken).Vhc();
                    return new RequestHead(section.Completed, section.ExceedsRequestLimits,
                        section.Completed ? headers.Complete() : null, ignored);
                }
                RejectUnexpected(type, beforeHeaders: true);
                ignored = AddIgnored(ignored, length);
                await reader.Discard(length, cancellationToken).Vhc();
            }
        }
        catch (EndOfStreamException) {
            // FIN before any complete header field, or within an unknown frame before headers,
            // is a rejected request. Raw HTTP/1 bytes must take exactly this same path.
            throw new Http3ProtocolException(Http3Protocol.RequestIncomplete, connectionError: false);
        }
    }

    private static void RejectUnexpected(long type, bool beforeHeaders)
    {
        // Reference compatibility: Kestrel ignores HTTP/2-reserved types 2/6/8/9 here, just as
        // on its control stream. HTTP/3 control-only frames still cannot appear on a request.
        if (type is 3 or 4 or 5 or 7 or 13 || beforeHeaders && type == 0)
            throw new Http3ProtocolException(Http3Protocol.FrameUnexpected);
    }

    private static long AddIgnored(long previous, long length)
    {
        if (length > MaxIgnoredBytes - previous - 16)
            throw new Http3ProtocolException(Http3Protocol.ExcessiveLoad);
        return previous + length + 16;
    }
}
