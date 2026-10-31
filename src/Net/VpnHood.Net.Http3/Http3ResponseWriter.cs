using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Http3;

public sealed class Http3ResponseWriter
{
    private readonly Stream _stream;
    private readonly Http3ServerOptions _options;

    internal Http3ResponseWriter(Stream stream, Http3ServerOptions options)
    {
        _stream = stream;
        _options = options;
    }

    public async Task WriteHeaders(int statusCode, IReadOnlyList<KeyValuePair<string, string>> headers,
        CancellationToken cancellationToken)
    {
        var commonHeaders = _options.ResponseHeaders(statusCode);
        IReadOnlyList<KeyValuePair<string, string>> responseHeaders = headers.Count == 0
            ? commonHeaders
            : [.. commonHeaders, .. headers];
        var response = Http3Protocol.Frame(1, Http3Qpack.EncodeResponse(statusCode, responseHeaders));
        await _stream.WriteAsync(response, cancellationToken).Vhc();
    }
}
