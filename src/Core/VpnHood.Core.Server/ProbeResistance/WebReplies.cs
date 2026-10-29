using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Unicode;

namespace VpnHood.Core.Server.ProbeResistance;

// the replies of Kestrel behind a JWT bearer check and HTTPS redirection, its headers in its order: a probe compares
// a reply byte for byte
internal static class WebReplies
{
    public static byte[] Unauthorized(HttpRequestHead request) =>
        Reply(401, "Unauthorized", request.IsHead, ConnectionHeader(request), "WWW-Authenticate: Bearer");

    // for a request that looked like a client's and failed past its headers, whose connection then closes
    public static byte[] UnauthorizedThenClose() =>
        Reply(401, "Unauthorized", isHead: false, "close", "WWW-Authenticate: Bearer");

    public static byte[] RedirectToHttps(HttpRequestHead request, int httpsPort) =>
        Reply(307, "Temporary Redirect", request.IsHead, ConnectionHeader(request),
            $"Location: {HttpsLocation(request, httpsPort)}");

    // Kestrel's own answer to a request it refuses, a HEAD's included, which always closes the connection
    public static byte[] Rejected(int statusCode, string? allow) =>
        Reply(statusCode, ReasonPhrase(statusCode), isHead: false, "close", allow != null ? $"Allow: {allow}" : null);

    // a downloaded file's 200 in Kestrel's order: the length, Connection, the type, Date, Server, then the header
    // Kestrel doesn't know; the connection closes after the file
    public static byte[] Download(long contentLength, string contentType, string fileName)
    {
        var disposition = new ContentDispositionHeaderValue("attachment") { FileName = fileName };
        var reply = new StringBuilder();
        reply.Append(CultureInfo.InvariantCulture, $"HTTP/1.1 200 OK\r\nContent-Length: {contentLength}\r\n");
        reply.Append("Connection: close\r\n");
        reply.Append($"Content-Type: {contentType}\r\n");
        reply.Append(CultureInfo.InvariantCulture, $"Date: {DateTime.UtcNow:r}\r\n");
        reply.Append("Server: Kestrel\r\n");
        reply.Append($"Content-Disposition: {disposition}\r\n\r\n");
        return Encoding.ASCII.GetBytes(reply.ToString());
    }

    private static string? ConnectionHeader(HttpRequestHead request) =>
        !request.KeepAlive ? "close" : request.IsHttp10 ? "keep-alive" : null;

    private static byte[] Reply(int statusCode, string reason, bool isHead, string? connection, string? header)
    {
        var reply = new StringBuilder();
        reply.Append(CultureInfo.InvariantCulture, $"HTTP/1.1 {statusCode} {reason}\r\n");
        if (!isHead) reply.Append("Content-Length: 0\r\n");
        if (connection != null) reply.Append($"Connection: {connection}\r\n");
        reply.Append(CultureInfo.InvariantCulture, $"Date: {DateTime.UtcNow:r}\r\n");
        reply.Append("Server: Kestrel\r\n");
        if (header != null) reply.Append(header).Append("\r\n");
        reply.Append("\r\n");
        return Encoding.ASCII.GetBytes(reply.ToString());
    }

    private static string ReasonPhrase(int statusCode) => statusCode switch {
        400 => "Bad Request",
        405 => "Method Not Allowed",
        408 => "Request Timeout",
        414 => "URI Too Long",
        431 => "Request Header Fields Too Large",
        505 => "HTTP Version Not Supported",
        _ => throw new ArgumentOutOfRangeException(nameof(statusCode), statusCode, null)
    };

    // as ASP.NET Core's HTTPS redirection builds it: the Host header's host, the port unless 443, the path as Kestrel
    // hands it over, re-escaped, and the query as sent
    private static string HttpsLocation(HttpRequestHead request, int httpsPort)
    {
        var host = HostWithoutPort(request.Headers.GetValueOrDefault("Host", ""));
        var port = httpsPort == 443 ? "" : $":{httpsPort.ToString(CultureInfo.InvariantCulture)}";
        var (_, query) = PathAndQuery(request.Target);
        var path = ToUriComponent(RequestPath(request.Target));
        return $"https://{host}{port}{(path.Length == 0 ? "/" : path)}{query}";
    }

    private static string HostWithoutPort(string host)
    {
        if (host.StartsWith('[')) {
            var close = host.IndexOf(']');
            return close < 0 ? host : host[..(close + 1)];
        }

        var colon = host.IndexOf(':');
        return colon < 0 ? host : host[..colon];
    }

    // an origin target's or an absolute one's; an asterisk or an authority has neither
    private static (string Path, string Query) PathAndQuery(string target)
    {
        if (!target.StartsWith('/')) {
            var scheme = target.IndexOf("://", StringComparison.Ordinal);
            var pathStart = scheme < 0 ? -1 : target.IndexOfAny(['/', '?'], scheme + 3);
            if (pathStart < 0)
                return ("", "");
            target = target[pathStart..];
        }

        var question = target.IndexOf('?');
        return question < 0 ? (target, "") : (target[..question], target[question..]);
    }

    public static string RequestPath(string target) => RemoveDotSegments(DecodePath(PathAndQuery(target).Path));

    // Kestrel's: every escape decoded but an escaped slash, and an escaped UTF-8 sequence only when it is valid;
    // anything else stays as sent
    private static string DecodePath(string path)
    {
        var decoded = new StringBuilder(path.Length);
        for (var i = 0; i < path.Length; i++) {
            if (path[i] != '%' || !TryReadEscape(path, i, out var first) || first == '/') {
                decoded.Append(path[i]);
                continue;
            }

            if (first < 0x80) {
                decoded.Append((char)first);
                i += 2;
                continue;
            }

            var length = first switch { >= 0xF0 => 4, >= 0xE0 => 3, >= 0xC0 => 2, _ => 0 };
            var bytes = new byte[Math.Max(length, 1)];
            bytes[0] = first;
            var isValid = length > 1;
            for (var k = 1; isValid && k < length; k++)
                isValid = TryReadEscape(path, i + 3 * k, out bytes[k]);

            if (isValid && Utf8.IsValid(bytes)) {
                decoded.Append(Encoding.UTF8.GetString(bytes));
                i += 3 * length - 1;
            }
            else
                decoded.Append('%');
        }

        return decoded.ToString();
    }

    private static bool TryReadEscape(string path, int index, out byte value)
    {
        value = 0;
        return index + 2 < path.Length && path[index] == '%' &&
               byte.TryParse(path.AsSpan(index + 1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture,
                   out value);
    }

    // RFC 3986's, as Kestrel removes . and .. before a handler sees the path
    private static string RemoveDotSegments(string input)
    {
        var output = new StringBuilder(input.Length);
        var index = 0;
        while (index < input.Length) {
            var remaining = input.AsSpan(index);
            if (remaining.StartsWith("../", StringComparison.Ordinal)) index += 3;
            else if (remaining.StartsWith("./", StringComparison.Ordinal)) index += 2;
            else if (remaining.StartsWith("/./", StringComparison.Ordinal)) index += 2;
            else if (remaining.SequenceEqual("/.")) {
                output.Append('/');
                break;
            }
            else if (remaining.StartsWith("/../", StringComparison.Ordinal) || remaining.SequenceEqual("/..")) {
                var lastSlash = output.Length - 1;
                while (lastSlash >= 0 && output[lastSlash] != '/')
                    lastSlash--;
                output.Length = Math.Max(lastSlash, 0);
                if (remaining.Length == 3) {
                    output.Append('/');
                    break;
                }
                index += 3;
            }
            else if (remaining.SequenceEqual(".") || remaining.SequenceEqual("..")) break;
            else {
                var next = input.IndexOf('/', index + 1);
                if (next < 0) next = input.Length;
                output.Append(input, index, next - index);
                index = next;
            }
        }

        return output.ToString();
    }

    // what a path can't hold escaped as UTF-8, as PathString does; an escape already there stays
    private static string ToUriComponent(string path)
    {
        var escaped = new StringBuilder(path.Length);
        for (var i = 0; i < path.Length; i++) {
            var c = path[i];
            if (IsPathChar(c) || (c == '%' && i + 2 < path.Length && char.IsAsciiHexDigit(path[i + 1]) &&
                                  char.IsAsciiHexDigit(path[i + 2]))) {
                escaped.Append(c);
                continue;
            }

            var length = char.IsSurrogatePair(path, i) ? 2 : 1;
            foreach (var b in Encoding.UTF8.GetBytes(path.Substring(i, length)))
                escaped.Append(CultureInfo.InvariantCulture, $"%{b:X2}");
            i += length - 1;
        }

        return escaped.ToString();
    }

    private static bool IsPathChar(char c) => char.IsAsciiLetterOrDigit(c) || "-._~!$&'()*+,;=:@/".Contains(c);
}
