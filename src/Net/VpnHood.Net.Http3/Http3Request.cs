namespace VpnHood.Net.Http3;

/// <summary>
/// Represents an HTTP/3 request no filter claimed, as <see cref="IHttp3Responder"/> gets it.
/// </summary>
public sealed class Http3Request
{
    internal Http3Request(IReadOnlyList<KeyValuePair<string, string>> headers, long? contentLength)
    {
        Headers = headers;
        ContentLength = contentLength;
    }

    /// <summary>
    /// Gets the request's header fields in the order received, the pseudo-headers such as <c>:method</c> and
    /// <c>:path</c> first.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; }

    /// <summary>Gets the value of the request's content-length header, or <see langword="null"/> when it has none.</summary>
    public long? ContentLength { get; }
}
