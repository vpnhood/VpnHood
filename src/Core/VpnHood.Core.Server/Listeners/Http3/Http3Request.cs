namespace VpnHood.Net.Http3;

public sealed class Http3Request
{
    internal Http3Request(IReadOnlyList<KeyValuePair<string, string>> headers, long? contentLength)
    {
        Headers = headers;
        ContentLength = contentLength;
    }

    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; }
    public long? ContentLength { get; }
}
