using System.Net;
using System.Text;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Server.ProbeResistance;

// reads a request's line and headers by Kestrel's rules and limits, which a probe can test one by one: a request
// Kestrel refuses throws the status it answers. It reads byte by byte, so what follows the headers, a client's body
// or a pipelined request, stays in the stream
internal static class HttpRequestHeadReader
{
    // Kestrel's limits: the request line counts its line end, the headers theirs and the empty line's
    private const int MaxRequestLineSize = 8192;
    public const int MaxRequestHeadersTotalSize = 32768 + 2;
    public const int MaxRequestHeaderCount = 100;

    public static async Task<HttpRequestHead> Read(Stream stream, byte firstByte, CancellationToken cancellationToken)
    {
        var reader = new ByteReader(stream, firstByte);

        var requestLine = await reader.ReadLine(MaxRequestLineSize, 414, true, cancellationToken).Vhc();

        var (method, target, isHttp10) = ParseRequestLine(requestLine);
        var absoluteAuthority = CheckTarget(method, target);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var headerCount = 0;
        var hostCount = 0;
        var contentLengthCount = 0;
        reader.BeginSection(MaxRequestHeadersTotalSize);
        while (true) {
            var line = await reader.ReadLine(MaxRequestHeadersTotalSize, 431, false, cancellationToken).Vhc();
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
        // Compatibility (2026-10): apps before v7.1.725 omit Host; defer its absence until client classification.
        if (hostCount > 1 || (host != null && !IsValidAuthority(host)))
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
            var codings = transferEncoding.Split(',', StringSplitOptions.TrimEntries);
            if (!codings[^1].Equals("chunked", StringComparison.OrdinalIgnoreCase))
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

        if (isHttp10 && method is "POST" or "PUT" && contentLength == null && !isChunked)
            throw new HttpRequestRejectedException(400);

        var connection = headers.GetValueOrDefault("Connection", "")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var keepAlive = !connection.Contains("close", StringComparer.OrdinalIgnoreCase) &&
                        (!isHttp10 || connection.Contains("keep-alive", StringComparer.OrdinalIgnoreCase));

        return new HttpRequestHead {
            Method = method,
            Target = target,
            IsHttp10 = isHttp10,
            IsHostMissing = hostCount == 0 && !isHttp10,
            Headers = headers,
            HeaderCount = headerCount,
            HeaderSize = reader.SectionLength,
            ContentLength = isChunked ? null : contentLength,
            IsChunked = isChunked,
            KeepAlive = keepAlive && method != "CONNECT" && !(isChunked && contentLength != null)
        };
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

        if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) {
            if (!Uri.TryCreate(target, UriKind.Absolute, out var uri))
                throw new HttpRequestRejectedException(400);

            return uri;
        }

        // anything else is an authority, which only CONNECT takes
        if (!target.All(c => char.IsAsciiLetterOrDigit(c) || "-._~!$&'()*+,;=:%[]".Contains(c)))
            throw new HttpRequestRejectedException(400);

        return method == "CONNECT" ? null : throw new HttpRequestRejectedException(405, "CONNECT");
    }

    public static (string Name, string Value) ParseHeader(byte[] line)
    {
        // a folded line, a missing name and anything but a token before the colon are malformed
        var colon = Array.IndexOf(line, (byte)':');
        if (line[0] is (byte)' ' or (byte)'\t' || colon <= 0 || !IsToken(line.AsSpan(0, colon)))
            throw new HttpRequestRejectedException(400);

        // a value takes any byte but NUL, UTF-8 included
        var value = line.AsSpan(colon + 1).Trim(" \t"u8);
        if (value.Contains((byte)0))
            throw new HttpRequestRejectedException(400);

        return (Encoding.ASCII.GetString(line, 0, colon), Encoding.Latin1.GetString(value));
    }

    // host[:port], the host a name of URI characters or a bracketed IPv6 address; empty is valid
    private static bool IsValidAuthority(string authority)
    {
        var host = authority;
        if (authority.StartsWith('[')) {
            var close = authority.IndexOf(']');
            if (close < 0 || !IPAddress.TryParse(authority[1..close], out _))
                return false;
            var suffix = authority.AsSpan(close + 1);
            return suffix.IsEmpty || (suffix[0] == ':' && suffix[1..].IndexOfAnyExceptInRange('0', '9') < 0);
        }

        var colon = authority.LastIndexOf(':');
        if (colon >= 0) {
            if (!authority[(colon + 1)..].All(char.IsAsciiDigit))
                return false;
            if (host.Length > 0)
                host = authority[..colon];
        }

        return host.All(c => char.IsAsciiLetterOrDigit(c) || "-._~!$&'()*+,;=%".Contains(c));
    }

    private static bool IsToken(ReadOnlySpan<byte> span)
    {
        foreach (var c in span)
            if (c >= 128 || !(char.IsAsciiLetterOrDigit((char)c) || "!#$%&'*+-.^_`|~".Contains((char)c)))
                return false;

        return true;
    }

    // one byte at a time, the first already read; a line ends at LF, its CR before it dropped, and a CR elsewhere
    // is malformed. A section, the headers, has a limit for all its lines together
    private sealed class ByteReader(Stream stream, byte firstByte)
    {
        private readonly byte[] _buffer = new byte[1];
        private bool _hasFirstByte = true;
        private int _sectionLength;
        private int _sectionLimit = int.MaxValue;
        public int SectionLength => _sectionLength;

        public void BeginSection(int limit)
        {
            _sectionLength = 0;
            _sectionLimit = limit;
        }

        public async Task<byte[]> ReadLine(int lineLimit, int overLimitStatus, bool skipLeadingLineEnds,
            CancellationToken cancellationToken)
        {
            var line = new List<byte>();
            var lineLength = 0;
            while (true) {
                var b = await ReadByte(cancellationToken).Vhc();
                if (skipLeadingLineEnds && lineLength == 0 && b is (byte)'\r' or (byte)'\n')
                    continue;
                lineLength++;
                _sectionLength++;
                if (lineLength > lineLimit || _sectionLength > _sectionLimit)
                    throw new HttpRequestRejectedException(overLimitStatus);

                if (b == '\n')
                    break;

                line.Add(b);

                // a TLS record's start, a handshake sent where HTTP was expected, is refused at once, as Kestrel
                // refuses it, without waiting for a line end
                if (line is [0x16, 0x03])
                    throw new HttpRequestRejectedException(400);
            }

            if (line.Count > 0 && line[^1] == '\r')
                line.RemoveAt(line.Count - 1);

            if (line.Contains((byte)'\r'))
                throw new HttpRequestRejectedException(400);

            return line.ToArray();
        }

        private async ValueTask<byte> ReadByte(CancellationToken cancellationToken)
        {
            if (_hasFirstByte) {
                _hasFirstByte = false;
                return firstByte;
            }

            var read = await stream.ReadAsync(_buffer, cancellationToken).Vhc();
            return read == 0 ? throw new EndOfStreamException() : _buffer[0];
        }
    }
}
