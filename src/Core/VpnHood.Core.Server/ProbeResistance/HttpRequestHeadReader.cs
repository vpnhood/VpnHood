using System.Text;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Server.ProbeResistance;

// reads a request's line and headers by Kestrel's rules and limits, which a probe can test one by one: a request
// Kestrel refuses throws the status it answers. Read-ahead remains in the connection's stream for its next
// request or for the tunnel if these headers belong to an app.
internal static class HttpRequestHeadReader
{
    // Kestrel's limits: the request line counts its line end, the headers theirs and the empty line's
    private const int MaxRequestLineSize = 8192;
    public const int MaxRequestHeadersTotalSize = 32768 + 2;
    public const int MaxRequestHeaderCount = 100;
    private static readonly UTF8Encoding HeaderEncoding = new(false, throwOnInvalidBytes: true);

    public static async Task<HttpRequestHead> Read(HttpReadStream stream, CancellationToken cancellationToken)
    {
        var reader = new LineReader(stream);

        var requestLine = await reader.ReadLine(MaxRequestLineSize, 414, cancellationToken, requestLine: true).Vhc();

        var (method, target, isHttp10) = ParseRequestLine(requestLine);
        var absoluteAuthority = CheckTarget(method, target);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var headerCount = 0;
        var hostCount = 0;
        var contentLengthCount = 0;
        reader.BeginSection(MaxRequestHeadersTotalSize);
        while (true) {
            var line = await reader.ReadLine(MaxRequestHeadersTotalSize, 431, cancellationToken).Vhc();
            if (line.Length == 0)
                break;

            if (++headerCount > MaxRequestHeaderCount)
                throw new HttpRequestRejectedException(431);

            var (name, value) = ParseHeader(line);
            if (name.Equals("Host", StringComparison.OrdinalIgnoreCase)) hostCount++;
            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) contentLengthCount++;
            headers[name] = headers.TryGetValue(name, out var existing) ? $"{existing}, {value}" : value;
        }

        // a host: one with HTTP/1.1, and the target's own with an absolute target
        var host = headers.GetValueOrDefault("Host");
        if (hostCount > 1 || (hostCount == 0 && !isHttp10) || (host != null && !IsValidAuthority(host)))
            throw new HttpRequestRejectedException(400);

        if (host != null && absoluteAuthority != null) {
            var matches = host.Equals(absoluteAuthority.Authority, StringComparison.Ordinal) ||
                          (absoluteAuthority.IsDefaultPort && host.Equals(
                              $"{absoluteAuthority.Authority}:{absoluteAuthority.Port}", StringComparison.Ordinal));
            if (!matches)
                throw new HttpRequestRejectedException(400);
        }
        if (host != null && method == "CONNECT" && target[0] != '/' && absoluteAuthority == null &&
            !host.Equals(target, StringComparison.Ordinal))
            throw new HttpRequestRejectedException(400);

        // the body's framing: chunks if the transfer coding ends with them, else a length of digits alone
        var isChunked = false;
        if (headers.TryGetValue("Transfer-Encoding", out var transferEncoding)) {
            var codings = ReadTokens(transferEncoding);
            if (codings.Length == 0 || !codings[^1].Equals("chunked", StringComparison.OrdinalIgnoreCase))
                throw new HttpRequestRejectedException(400);
            isChunked = true;
        }

        long? contentLength = null;
        if (contentLengthCount > 0) {
            var value = headers["Content-Length"];
            if (contentLengthCount > 1 || value.Length == 0 || !value.All(char.IsAsciiDigit) ||
                !long.TryParse(value, out var length))
                throw new HttpRequestRejectedException(400);
            contentLength = length;
        }

        var connection = ReadTokens(headers.GetValueOrDefault("Connection", ""));
        // Kestrel takes an upgrade before the HTTP/1.0 length rule. HTTP/1.1 without a length has an empty
        // body and gets the normal reply, not 411.
        if (isHttp10 && method is "POST" or "PUT" && contentLength == null && !isChunked &&
            !connection.Contains("upgrade", StringComparer.OrdinalIgnoreCase))
            throw new HttpRequestRejectedException(400);
        var keepAlive = !connection.Contains("close", StringComparer.OrdinalIgnoreCase) &&
                        (!isHttp10 || connection.Contains("keep-alive", StringComparer.OrdinalIgnoreCase));

        return new HttpRequestHead {
            Method = method,
            Target = target,
            IsHttp10 = isHttp10,
            Headers = headers,
            HeaderCount = headerCount,
            HeaderSize = reader.SectionLength,
            ContentLength = isChunked ? null : contentLength,
            IsChunked = isChunked,
            KeepAlive = keepAlive && method != "CONNECT" && !(isChunked && contentLength != null)
        };
    }

    private static string[] ReadTokens(string value)
    {
        // Kestrel skips spaces and empty elements, not arbitrary whitespace. The header reader trims
        // outer SP/HTAB; a tab within the list still prevents that token from matching.
        return value.Split(',').Select(x => x.Trim(' ')).Where(x => x.Length != 0).ToArray();
    }

    public static async Task ReadTrailers(HttpReadStream stream, HttpRequestHead request, CancellationToken cancellationToken)
    {
        // Trailers share the header budgets and line-ending rules, including bare LF. Chunk framing does not.
        var limit = MaxRequestHeadersTotalSize - request.HeaderSize;
        var reader = new LineReader(stream);
        reader.BeginSection(limit);
        var count = request.HeaderCount;
        while (true) {
            var line = await reader.ReadLine(limit, 431, cancellationToken).Vhc();
            if (line.Length == 0)
                return;
            if (++count > MaxRequestHeaderCount)
                throw new HttpRequestRejectedException(431);
            ParseHeader(line);
        }
    }

    private static (string Method, string Target, bool IsHttp10) ParseRequestLine(byte[] line)
    {
        var methodEnd = Array.IndexOf(line, (byte)' ');
        var targetEnd = methodEnd < 0 ? -1 : Array.IndexOf(line, (byte)' ', methodEnd + 1);
        if (methodEnd <= 0 || targetEnd < 0 || targetEnd == methodEnd + 1)
            throw new HttpRequestRejectedException(400);

        if (!IsToken(line.AsSpan(0, methodEnd)))
            throw new HttpRequestRejectedException(400);

        // a target takes any ASCII but NUL; raw UTF-8 needs its percent-encoding
        var target = line.AsSpan(methodEnd + 1, targetEnd - methodEnd - 1);
        if (target.ContainsAnyExceptInRange((byte)1, (byte)127))
            throw new HttpRequestRejectedException(400);

        // an unknown version of HTTP's length is one Kestrel doesn't support; any other is malformed
        var version = line.AsSpan(targetEnd + 1);
        bool isHttp10;
        if (version.SequenceEqual("HTTP/1.1"u8)) isHttp10 = false;
        else if (version.SequenceEqual("HTTP/1.0"u8)) isHttp10 = true;
        else throw new HttpRequestRejectedException(version.Length == "HTTP/1.1".Length ? 505 : 400);

        return (Encoding.ASCII.GetString(line, 0, methodEnd), Encoding.ASCII.GetString(target), isHttp10);
    }

    // the target's form, by its method; an absolute target's authority, which the Host header must repeat
    private static Uri? CheckTarget(string method, string target)
    {
        if (target[0] == '/')
            return null;

        if (target == "*")
            return method == "OPTIONS" ? null : throw new HttpRequestRejectedException(405, "OPTIONS");

        if (target.StartsWith("http://", StringComparison.Ordinal) ||
            target.StartsWith("https://", StringComparison.Ordinal)) {
            if (!Uri.TryCreate(target, UriKind.Absolute, out var uri))
                throw new HttpRequestRejectedException(400);

            return uri;
        }

        // anything else is an authority, which only CONNECT takes
        if (!target.All(c => char.IsAsciiLetterOrDigit(c) || ":.-[]@".Contains(c)))
            throw new HttpRequestRejectedException(400);

        return method == "CONNECT" ? null : throw new HttpRequestRejectedException(405, "CONNECT");
    }

    public static (string Name, string Value) ParseHeader(byte[] line)
    {
        // a folded line, a missing name and anything but a token before the colon are malformed
        var colon = Array.IndexOf(line, (byte)':');
        if (line[0] is (byte)' ' or (byte)'\t' || colon <= 0 || !IsToken(line.AsSpan(0, colon)))
            throw new HttpRequestRejectedException(400);

        // Kestrel's default decoder accepts ASCII and valid UTF-8, not arbitrary Latin-1 or replacement
        // characters for malformed sequences. This applies to trailer fields too.
        var value = line.AsSpan(colon + 1).Trim(" \t"u8);
        if (value.Contains((byte)0))
            throw new HttpRequestRejectedException(400);

        try {
            return (Encoding.ASCII.GetString(line, 0, colon), HeaderEncoding.GetString(value));
        }
        catch (DecoderFallbackException) {
            throw new HttpRequestRejectedException(400);
        }
    }

    // Match Kestrel's HttpUtilities.IsHostHeaderValid character checks, not IPAddress.TryParse: brackets
    // contain at least three hex digits, colons or dots, even when that is not an address. Empty is valid.
    private static bool IsValidAuthority(string authority)
    {
        if (authority.Length == 0)
            return true;
        if (authority.StartsWith('[')) {
            var close = authority.IndexOf(']');
            if (close < 4 || !authority[1..close].All(c => char.IsAsciiHexDigit(c) || c is ':' or '.'))
                return false;
            return close == authority.Length - 1 || IsValidPort(authority.AsSpan(close + 1));
        }

        if (authority[0] == ':')
            return false;
        for (var i = 0; i < authority.Length; i++)
            if (!(char.IsAsciiLetterOrDigit(authority[i]) || "!$&'()-._~".Contains(authority[i])))
                return IsValidPort(authority.AsSpan(i));
        return true;
    }

    private static bool IsValidPort(ReadOnlySpan<char> suffix)
    {
        return suffix.Length > 1 && suffix[0] == ':' && suffix[1..].IndexOfAnyExceptInRange('0', '9') < 0;
    }

    private static bool IsToken(ReadOnlySpan<byte> span)
    {
        foreach (var c in span)
            if (c >= 128 || !(char.IsAsciiLetterOrDigit((char)c) || "!#$%&'*+-.^_`|~".Contains((char)c)))
                return false;

        return true;
    }

    // A line ends at LF, its preceding CR is dropped, and CR elsewhere is malformed. Scan buffered spans
    // while retaining the per-line and aggregate header budgets, including their line ends.
    private sealed class LineReader(HttpReadStream stream)
    {
        private int _sectionLength;
        private int _sectionLimit = int.MaxValue;
        public int SectionLength => _sectionLength;

        public void BeginSection(int limit)
        {
            _sectionLength = 0;
            _sectionLimit = limit;
        }

        public async Task<byte[]> ReadLine(int lineLimit, int overLimitStatus,
            CancellationToken cancellationToken, bool requestLine = false)
        {
            using var line = new MemoryStream();
            while (true) {
                if (!await stream.EnsureBuffered(1, cancellationToken).Vhc())
                    throw new EndOfStreamException();
                var span = stream.BufferedSpan;
                var end = span.IndexOf((byte)'\n');
                var count = end < 0 ? span.Length : end + 1;
                count = Math.Min(count, Math.Min(lineLimit - (int)line.Length, _sectionLimit - _sectionLength) + 1);
                line.Write(span[..count]);
                stream.Consume(count);
                _sectionLength += count;
                // Only the request line recognizes a TLS handshake early. An incomplete header or trailer
                // beginning with these bytes still waits for its line end or the appropriate deadline.
                if (requestLine && line.Length >= 2 && line.GetBuffer()[0] == 0x16 && line.GetBuffer()[1] == 0x03)
                    throw new HttpRequestRejectedException(400);
                if (line.Length > lineLimit || _sectionLength > _sectionLimit)
                    throw new HttpRequestRejectedException(overLimitStatus);
                if (end >= 0)
                    break;
            }

            var result = line.GetBuffer().AsSpan(0, (int)line.Length - 1);
            if (result.Length > 0 && result[^1] == '\r')
                result = result[..^1];
            if (result.Contains((byte)'\r'))
                throw new HttpRequestRejectedException(400);
            return result.ToArray();
        }
    }
}
