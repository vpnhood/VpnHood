using VpnHood.Net.Quic.Abstractions;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Http3;

internal static class Http3RequestStream
{
    public static async Task Process(Stream stream, Http3StreamReader reader, IQuicStream quic,
        Http3StreamContext context, Action<Exception> onError, IHttp3Responder responder,
        Http3ServerOptions options, CancellationToken headersToken,
        CancellationToken cancellationToken)
    {
        var headerBudget = new Http3HeaderBudget(options);
        var head = await ReadHeaders(reader, options, headerBudget, headersToken).Vhc();
        if (!head.Completed) {
            quic.CompleteWrites();
            return;
        }
        // Count every inspected byte, including zero-length DATA frame headers and QPACK trailers.
        // A continuously readable peer must not postpone the application's final response forever.
        reader.AvailableBytesLimit = options.MaxBodyInspectionBytes;
        reader.ReadAvailableOnly = true;
        using var dispatch = new Http3ResponseDispatch(stream, quic, context, responder, head.Request,
            headerBudget, options, onError, headersToken);
        try {
            if (!head.ExceedsRequestLimits) {
                try {
                    await ValidateAvailableBody(reader, head.Request.ContentLength, headerBudget,
                        dispatch, options, cancellationToken).Vhc();
                }
                catch (Exception ex) when (ex is Http3ReadPendingException or Http3ReadBudgetExceededException) {
                    // Responders do not consume bodies; inspect available bytes without waiting for more.
                }
            }
            await dispatch.Complete(headersToken).Vhc();
        }
        catch (OperationCanceledException ex) {
            dispatch.Fail(ex);
            if (cancellationToken.IsCancellationRequested)
                throw;
        }
        catch (Exception ex) {
            dispatch.Fail(ex);
        }
        finally {
            // Join dispatch before returning the reader or disposing the native stream.
            await dispatch.Join(cancellationToken).Vhc();
        }
    }

    private static async Task ValidateAvailableBody(Http3StreamReader reader, long? contentLength,
        Http3HeaderBudget headerBudget, Http3ResponseDispatch dispatch, Http3ServerOptions options,
        CancellationToken cancellationToken)
    {
        long bodyBytes = 0;
        var trailers = false;
        while (true) {
            if (dispatch.ResponseSelected)
                return;
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
                await dispatch.Start(cancellationToken).Vhc();
                if (dispatch.ResponseSelected)
                    return;
                var trailerHeaders = new Http3RequestHeaders(trailers: true, options.MaxRequestLineBytes);
                var section = await Http3Qpack.ReadSection(reader, length, trailerHeaders.Append,
                    options, headerBudget, cancellationToken).Vhc();
                if (!section.Completed)
                    return;
                trailerHeaders.Complete();
            }
            else {
                RejectUnexpected(type.Value, beforeHeaders: false);
                await reader.Discard(length, cancellationToken).Vhc();
            }
        }
    }

    private static void ValidateBodyLength(long bodyBytes, long? contentLength)
    {
        if (contentLength != null && bodyBytes != contentLength)
            throw new Http3ProtocolException(Http3Protocol.ProtocolError, connectionError: false);
    }

    private static async Task<Http3RequestHead> ReadHeaders(Http3StreamReader reader, Http3ServerOptions options,
        Http3HeaderBudget headerBudget, CancellationToken cancellationToken)
    {
        try {
            while (true) {
                var type = await reader.ReadInteger(cancellationToken).Vhc();
                var length = await reader.ReadInteger(cancellationToken).Vhc();
                if (type == 1) {
                    var headers = new Http3RequestHeaders(trailers: false, options.MaxRequestLineBytes);
                    var section = await Http3Qpack.ReadSection(reader, length, headers.Append,
                        options, headerBudget, cancellationToken).Vhc();
                    var request = new Http3Request(headers.Fields, section.Completed ? headers.Complete() : null);
                    return new Http3RequestHead(section.Completed, section.ExceedsRequestLimits, request);
                }
                RejectUnexpected(type, beforeHeaders: true);
                // RC3 discards request extensions without a size cap; the header deadline still applies.
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
}
