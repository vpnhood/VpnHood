namespace VpnHood.Net.Http3;

public sealed class Http3ResponseWriter
{
    private readonly Http3ServerOptions _options;
    internal byte[]? BufferedResponse { get; private set; }

    internal Http3ResponseWriter(Http3ServerOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Prepares the final header-only response. The dispatch sends its bytes and FIN together after
    /// the responder returns; this operation does not wait for native transmission.
    /// </summary>
    public Task WriteHeaders(int statusCode, IReadOnlyList<KeyValuePair<string, string>> headers,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (BufferedResponse != null)
            throw new InvalidOperationException("The final response headers have already been written.");
        var commonHeaders = _options.ResponseHeaders(statusCode);
        IReadOnlyList<KeyValuePair<string, string>> responseHeaders = headers.Count == 0
            ? commonHeaders
            : [.. commonHeaders, .. headers];
        // The dispatch submits these headers and FIN together after the application completes.
        BufferedResponse = Http3Protocol.Frame(1, Http3Qpack.EncodeResponse(statusCode, responseHeaders));
        return Task.CompletedTask;
    }
}
