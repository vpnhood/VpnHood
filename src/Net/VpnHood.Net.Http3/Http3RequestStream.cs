using VpnHood.Net.Quic.Abstractions;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Http3;

internal sealed class Http3RequestStream(Http3StreamReader reader, IQuicStream quic,
    Action<Exception> onError, IHttp3Responder responder, Http3ServerOptions options) : IAsyncDisposable
{
    private readonly Http3HeaderBudget _headerBudget = new(options);
    private Http3ResponseDispatch? _responseDispatch;

    public static async Task Process(Http3StreamReader reader, IQuicStream quic,
        Action<Exception> onError, IHttp3Responder responder,
        Http3ServerOptions options, CancellationToken headersToken,
        CancellationToken cancellationToken)
    {
        await using var request = new Http3RequestStream(reader, quic, onError, responder, options);
        await request.Process(headersToken, cancellationToken).Vhc();
    }

    private async Task Process(CancellationToken headersToken, CancellationToken cancellationToken)
    {
        var head = await ReadHeaders(headersToken).Vhc();
        if (!head.Completed) {
            quic.CompleteWrites();
            return;
        }
        var dispatch = _responseDispatch ??
            throw new InvalidOperationException("A completed request has no response dispatch.");
        try {
            if (!head.ExceedsRequestLimits) {
                try {
                    await ValidateAvailableBody(reader, head.Request.ContentLength, _headerBudget,
                        dispatch, options, headersToken).Vhc();
                }
                catch (Exception ex) when (ex is Http3ReadBudgetExceededException or
                                           Http3InspectionCompleteException) {
                    // The byte bound or completed application ends inspection.
                }
            }
        }
        catch (OperationCanceledException ex) {
            dispatch.Fail(ex);
            if (cancellationToken.IsCancellationRequested)
                throw;
        }
        catch (Exception ex) {
            dispatch.Fail(ex);
        }
        await dispatch.Join(cancellationToken).Vhc();
        // A bounded inspection may end without a pending read or peer FIN. Send H3_NO_ERROR only
        // after Join confirms that no parser error needs a different stream error code.
        reader.FinishInspection();
    }

    private void StartResponse(Http3RequestHead head, CancellationToken headersToken)
    {
        // Count every inspected byte, including zero-length DATA frame headers and QPACK trailers.
        // A continuously readable peer must not postpone the application's final response forever.
        reader.BeginBodyInspection(options.MaxBodyInspectionBytes);
        _responseDispatch = new Http3ResponseDispatch(quic, responder, head.Request,
            _headerBudget, options, onError, reader, head.Headers);
        _responseDispatch.Start(headersToken);
    }

    private static async Task ValidateAvailableBody(Http3StreamReader reader, long? contentLength,
        Http3HeaderBudget headerBudget, Http3ResponseDispatch dispatch, Http3ServerOptions options,
        CancellationToken cancellationToken)
    {
        long bodyBytes = 0;
        var trailersSeen = false;
        try {
            while (true) {
                var type = await reader.ReadIntegerOrEnd(cancellationToken).Vhc();
                if (type == null) {
                    ValidateBodyLength(bodyBytes, contentLength);
                    return;
                }
                var length = await reader.ReadInteger(cancellationToken).Vhc();
                if (trailersSeen && type == 0)
                    throw new Http3ProtocolException(Http3Protocol.FrameUnexpected);
                if (type == 0) {
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
                    if (!await ReadTrailers(reader, length, trailersSeen, headerBudget, options,
                            cancellationToken).Vhc())
                        return;
                    trailersSeen = true;
                }
                else {
                    RejectUnexpected(type.Value, beforeHeaders: false);
                    await reader.Discard(length, cancellationToken).Vhc();
                }
            }
        }
        catch (Http3ProtocolException ex) {
            // Report protocol failures before unwinding the parser task. The response is independent
            // and can otherwise commit its FIN during that unwind.
            dispatch.Fail(ex);
            throw;
        }
        catch (Http3QpackException ex) {
            dispatch.Fail(ex);
            throw;
        }
    }

    private static async Task<bool> ReadTrailers(Http3StreamReader reader, long length, bool trailersSeen,
        Http3HeaderBudget headerBudget, Http3ServerOptions options,
        CancellationToken cancellationToken)
    {
        if (trailersSeen)
            throw new Http3ProtocolException(Http3Protocol.FrameUnexpected);
        var trailerHeaders = new Http3RequestHeaders(trailers: true, options.MaxRequestLineBytes);
        var section = await Http3Qpack.ReadSection(reader, length, trailerHeaders.Append,
            options, headerBudget, cancellationToken).Vhc();
        if (!section.Completed)
            return false;
        trailerHeaders.Complete();
        return true;
    }

    private static void ValidateBodyLength(long bodyBytes, long? contentLength)
    {
        if (contentLength != null && bodyBytes != contentLength)
            throw new Http3ProtocolException(Http3Protocol.ProtocolError, connectionError: false);
    }

    private async Task<Http3RequestHead> ReadHeaders(CancellationToken cancellationToken)
    {
        try {
            while (true) {
                var type = await reader.ReadInteger(cancellationToken).Vhc();
                var length = await reader.ReadInteger(cancellationToken).Vhc();
                if (type == 1) {
                    var headers = new Http3RequestHeaders(trailers: false, options.MaxRequestLineBytes);
                    var section = await Http3Qpack.ReadSection(reader, length, headers.Append,
                        options, _headerBudget, cancellationToken).Vhc();
                    if (section.Completed)
                        headers.ValidateRequiredPseudoHeaders();
                    var request = new Http3Request(headers.Fields, headers.ContentLength);
                    var head = new Http3RequestHead(section.Completed, section.ExceedsRequestLimits,
                        request, headers);
                    if (head.Completed)
                        // Kestrel's completed HEADERS callback queues the application before its
                        // header-reading task unwinds, so buffered body/trailers race the response.
                        StartResponse(head, cancellationToken);
                    return head;
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

    public async ValueTask DisposeAsync()
    {
        if (_responseDispatch != null)
            await _responseDispatch.DisposeAsync().Vhc();
    }
}
