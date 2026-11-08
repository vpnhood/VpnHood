namespace VpnHood.Net.Http3;

/// <summary>
/// Provides the means to write a request's response: one final response, headers only.
/// </summary>
public sealed class Http3ResponseWriter
{
    private readonly Http3ServerOptions _options;
    internal byte[]? BufferedResponse { get; private set; }

    internal Http3ResponseWriter(Http3ServerOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Prepares the final response, headers only. The server sends it with the stream's FIN after the responder's task
    /// completes; this method does not wait for that.
    /// </summary>
    /// <param name="statusCode">The response's status code.</param>
    /// <param name="headers">
    /// The response's header fields, sent after those <see cref="Http3ServerOptions.ResponseHeaders"/> gives.
    /// </param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that is already complete.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="InvalidOperationException">The response is written already.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="statusCode"/> is not from 100 to 999.</exception>
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
