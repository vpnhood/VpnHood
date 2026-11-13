using System.Buffers;
using System.Text;

namespace VpnHood.Net.Http3;

// Validate HTTP message semantics separately from QPACK compression. The request handler maps each
// failure to the measured reference's HTTP/3 error code and stream/connection scope.
internal sealed class Http3RequestHeaders(bool trailers, int maxRequestLineSize)
{
    private static readonly UTF8Encoding HeaderEncoding = new(false, throwOnInvalidBytes: true);
    private static readonly SearchValues<char> InvalidValueCharacters = SearchValues.Create(
        "\u0000\u0001\u0002\u0003\u0004\u0005\u0006\u0007\u0008\u000a\u000b\u000c\u000d\u000e\u000f" +
        "\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001a\u001b\u001c\u001d\u001e\u001f\u007f");
    private static readonly SearchValues<char> LowerTokenCharacters = SearchValues.Create(
        "abcdefghijklmnopqrstuvwxyz0123456789!#$%&'*+-.^_`|~");
    private static readonly SearchValues<char> TokenCharacters = SearchValues.Create(
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!#$%&'*+-.^_`|~");
    private static readonly SearchValues<char> HostCharacters = SearchValues.Create(
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!$&'()-._~");
    private readonly Dictionary<string, string> _pseudo = new(StringComparer.Ordinal);
    private readonly List<KeyValuePair<string, string>> _fields = [];
    private bool _regularSeen;
    private string? _host;
    private int _hostCount;
    private long? _contentLength;

    public IReadOnlyList<KeyValuePair<string, string>> Fields => _fields;
    public long? ContentLength => _contentLength;

    public void Append(KeyValuePair<string, string> field)
    {
        var (name, value) = field;
        value = DecodeValue(value);
        // Validate each decoded field immediately. A bad field followed by FIN must remain an
        // error even when the peer's declared HEADERS frame never finishes.
        // Preserve surrounding SP/HTAB as the reference does.
        if (name.Length == 0 || value.AsSpan().ContainsAny(InvalidValueCharacters))
            Reject();
        if (name[0] == ':') {
            // The reference accepts :protocol on any method, not only on an extended CONNECT.
            if (trailers || _regularSeen || name is not (":method" or ":scheme" or ":path" or ":authority" or ":protocol") ||
                !_pseudo.TryAdd(name, value))
                Reject();
            _fields.Add(new KeyValuePair<string, string>(name, value));
            return;
        }
        _regularSeen = true;
        // The reference takes te's "trailers" case-sensitively.
        if (name.AsSpan().ContainsAnyExcept(LowerTokenCharacters) || name is "connection" or "proxy-connection" or "keep-alive" or
            "transfer-encoding" or "upgrade" || name == "te" && value != "trailers")
            Reject();
        if (name == "host") {
            _hostCount++;
            _host = value;
        }
        if (name == "content-length") {
            if (trailers || _contentLength != null || value.Length == 0 || !value.All(char.IsAsciiDigit) ||
                !long.TryParse(value, out var parsed))
                Reject();
            else
                _contentLength = parsed;
        }
        _fields.Add(new KeyValuePair<string, string>(name, value));
    }

    public long? Complete()
    {
        if (trailers)
            return null;
        ValidateRequiredPseudoHeaders();
        ValidateRequest();
        return _contentLength;
    }

    public void ValidateRequiredPseudoHeaders()
    {
        if (!_pseudo.TryGetValue(":method", out var method) || method != "CONNECT" &&
            (!_pseudo.ContainsKey(":scheme") || !_pseudo.ContainsKey(":path")))
            Reject();
    }

    public void ValidateRequest()
    {
        var method = _pseudo[":method"];
        if (method.Length == 0 || method.AsSpan().ContainsAnyExcept(TokenCharacters))
            Reject(Http3Protocol.ProtocolError);

        var pseudoAuthority = _pseudo.GetValueOrDefault(":authority", "");
        var hasAuthority = pseudoAuthority.Length > 0;
        var authority = hasAuthority ? pseudoAuthority : _host ?? "";
        // Match the reference's authority precedence, including mismatched or repeated Host fields.
        if (!hasAuthority && _hostCount > 1 || !IsValidAuthority(authority))
            Reject(Http3Protocol.ProtocolError);
        if (method == "CONNECT" && !_pseudo.ContainsKey(":protocol")) {
            // The measured reference accepts Host fallback and an omitted port for CONNECT. It
            // rejects nonempty scheme/path values, but this front never establishes an HTTP tunnel.
            if (_pseudo.TryGetValue(":scheme", out var connectScheme) && connectScheme.Length > 0 ||
                _pseudo.TryGetValue(":path", out var connectPath) && connectPath.Length > 0)
                Reject(Http3Protocol.ProtocolError);
        }
        else {
            var path = _pseudo.GetValueOrDefault(":path", "");
            var scheme = _pseudo.GetValueOrDefault(":scheme", "");
            if (!scheme.Equals("https", StringComparison.OrdinalIgnoreCase) || path.Length == 0 ||
                path[0] != '/' && !(path == "*" && method == "OPTIONS") ||
                path.AsSpan().ContainsAnyInRange('\0', ' ') || path.Contains('#'))
                Reject(Http3Protocol.ProtocolError);
            // Kestrel approximates the request line with these decoded pseudo-header values.
            if (!(path == "*" && method == "OPTIONS") &&
                (long)method.Length + scheme.Length + authority.Length + path.Length > maxRequestLineSize)
                Reject(Http3Protocol.RequestRejected);
            // The reference preserves a plain UTF-8 raw path, but its unescaped path-with-query
            // conversion requires ASCII. Keep the measured /é and /é?q outcomes distinct.
            var queryIndex = path.IndexOf('?');
            var pathSegment = queryIndex < 0 ? path.AsSpan() : path.AsSpan(0, queryIndex);
            if (pathSegment.IndexOfAnyExceptInRange('\0', '\xff') >= 0 || pathSegment.Contains("%00", StringComparison.Ordinal))
                Reject(Http3Protocol.ProtocolError);
            if (queryIndex >= 0 && !path.AsSpan(0, queryIndex).Contains('%') &&
                !Ascii.IsValid(path.AsSpan(0, queryIndex)))
                Reject(Http3Protocol.ProtocolError);
        }
    }

    private static string DecodeValue(string octets)
    {
        // QPACK keeps octets in Latin-1 so its limits count wire bytes. HTTP field values instead use
        // the reference's strict UTF-8 decoding; ordinary ASCII values need no conversion or buffer.
        if (Ascii.IsValid(octets))
            return octets;
        if (octets.Any(c => c > byte.MaxValue))
            Reject();

        var rented = octets.Length > 256 ? ArrayPool<byte>.Shared.Rent(octets.Length) : null;
        Span<byte> bytes = rented ?? stackalloc byte[octets.Length];
        try {
            var count = Encoding.Latin1.GetBytes(octets.AsSpan(), bytes);
            return HeaderEncoding.GetString(bytes[..count]);
        }
        catch (DecoderFallbackException) {
            throw new Http3ProtocolException(Http3Protocol.MessageError, connectionError: false);
        }
        finally {
            if (rented != null)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static bool IsValidAuthority(string authority)
    {
        if (authority.Length == 0)
            return true;

        // Use the same host characters as the HTTP/1 web front. In particular, do not pass an entire
        // authority to Uri/IPAddress parsers: they normalize text that the reference rejects on wire.
        if (authority[0] == '[') {
            var close = authority.IndexOf(']');
            if (close < 4 || !authority[1..close].All(c => char.IsAsciiHexDigit(c) || c is ':' or '.'))
                return false;
            return close == authority.Length - 1 || IsValidPort(authority.AsSpan(close + 1));
        }
        if (authority[0] == ':')
            return false;
        var suffix = authority.AsSpan().IndexOfAnyExcept(HostCharacters);
        return suffix < 0 || IsValidPort(authority.AsSpan(suffix));
    }

    private static bool IsValidPort(ReadOnlySpan<char> suffix) =>
        suffix.Length > 1 && suffix[0] == ':' && suffix[1..].IndexOfAnyExceptInRange('0', '9') < 0;

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Reject(long errorCode = Http3Protocol.MessageError) =>
        throw new Http3ProtocolException(errorCode, connectionError: false);
}
