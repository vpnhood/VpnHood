using System.Diagnostics.CodeAnalysis;
using System.Web;

namespace VpnHood.Core.Proxies.Management.Abstractions;

/// <summary>
/// Provides the reading of proxy endpoints from URLs, from loosely written addresses, and from text.
/// </summary>
public static class ProxyEndPointParser
{
    private const string PseudoScheme = "vh-no-scheme";

    /// <summary>
    /// Reads a proxy endpoint from a URL such as <c>socks5://user:password@host:1080</c>.
    /// </summary>
    /// <param name="uri">
    /// The URL. Its scheme names the protocol, ignoring case. A URL with no port takes the port .NET gives its scheme,
    /// 80 for <c>http</c> and 443 for <c>https</c>, or else the protocol's default, 1080 for SOCKS. A query of
    /// <c>enabled=false</c> or <c>enabled=0</c> makes the endpoint disabled.
    /// </param>
    /// <returns>The endpoint, with its escaped user name and password unescaped.</returns>
    /// <exception cref="NotSupportedException">The scheme is no proxy protocol.</exception>
    /// <exception cref="ArgumentException">The URL has no host.</exception>
    public static ProxyEndPoint FromUrl(Uri uri)
    {
        if (!Enum.TryParse<ProxyProtocol>(uri.Scheme, ignoreCase: true, out var protocol))
            throw new NotSupportedException($"Unsupported scheme: {uri.Scheme}");

        if (string.IsNullOrWhiteSpace(uri.Host))
            throw new ArgumentException("Proxy URL must include a host.", nameof(uri));

        string? username = null;
        string? password = null;
        if (!string.IsNullOrEmpty(uri.UserInfo)) {
            var parts = uri.UserInfo.Split(':', 2);
            username = Uri.UnescapeDataString(parts[0]);
            if (parts.Length > 1)
                password = Uri.UnescapeDataString(parts[1]);
        }

        var port = uri.Port > 0 ? uri.Port : GetDefaultPort(protocol);
        var valueCollection = HttpUtility.ParseQueryString(uri.Query);
        var isEnabledValue = valueCollection["enabled"]?.ToLower() ?? "true";

        return new ProxyEndPoint {
            Protocol = protocol,
            Host = uri.Host,
            Port = port,
            Username = username,
            Password = password,
            IsEnabled = isEnabledValue is not ("false" or "0")
        };
    }

    /// <summary>
    /// Normalizes an endpoint whose host may be a whole address, such as <c>socks5://proxy.example:1080</c>: its parts
    /// fill the endpoint, and the endpoint's own values fill what it leaves out.
    /// </summary>
    /// <param name="proxyEndPoint">The endpoint.</param>
    /// <returns>
    /// A new endpoint: the address's own parts, its credentials too, and the original's where the address leaves them
    /// out; the original's user name and password trimmed, the address's kept as written.
    /// </returns>
    /// <exception cref="ArgumentException">The host is empty or no valid address.</exception>
    /// <exception cref="NotSupportedException">The address's scheme is no proxy protocol.</exception>
    public static ProxyEndPoint Normalize(ProxyEndPoint proxyEndPoint)
    {
        var defaults = new ProxyEndPointDefaults {
            IsEnabled = proxyEndPoint.IsEnabled,
            Password = proxyEndPoint.Password?.Trim(),
            Port = proxyEndPoint.Port,
            Protocol = proxyEndPoint.Protocol,
            Username = proxyEndPoint.Username?.Trim()
        };

        var uri = ParseHostToUrl(proxyEndPoint.Host, defaults);
        return FromUrl(uri);
    }

    /// <summary>
    /// Reads a loosely written proxy address into a URL, as <see cref="ParseHostToUrl"/> does, or gives
    /// <see langword="null"/> where that throws.
    /// </summary>
    /// <param name="value">The address.</param>
    /// <param name="defaults">The values for the parts the address leaves out, or <see langword="null"/>.</param>
    /// <returns>The URL, or <see langword="null"/>.</returns>
    public static Uri? TryParseHostToUrl(string value, ProxyEndPointDefaults? defaults)
    {
        try {
            return ParseHostToUrl(value, defaults);
        }
        catch {
            return null;
        }
    }

    /// <summary>
    /// Reads a loosely written proxy address, such as <c>host:1080</c> or <c>user:pass@host</c>, into a URL.
    /// </summary>
    /// <param name="value">The address, with or without a scheme, a port and credentials.</param>
    /// <param name="defaults">
    /// The values for the parts the address leaves out, or <see langword="null"/>; a missing port then takes the
    /// protocol's default (1080 for SOCKS, 8080 for HTTP, 443 for HTTPS). A port written in the address is kept, the
    /// scheme's standard one too, as 80 for <c>http</c>.
    /// </param>
    /// <returns>
    /// The URL, whose scheme is one <see cref="FromUrl"/> reads: <c>socks</c> and <c>socks5h</c> become <c>socks5</c>.
    /// Its query has <c>enabled=false</c> when <paramref name="defaults"/> says the endpoint is disabled and the
    /// address's query does not say otherwise.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// The address is empty or not valid, or the address and the defaults give no scheme, or the scheme is no proxy
    /// protocol, or the address's port is 0.
    /// </exception>
    public static Uri ParseHostToUrl(
        string value,
        ProxyEndPointDefaults? defaults)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(value));
        value = value.Trim();

        if (!TryCreateUri(value, out var parsed))
            throw new ArgumentException("Invalid URL format.", nameof(value));

        var parts = ExtractParts(parsed);
        if (parts.Port is <= 0)
            throw new ArgumentException("The port must be from 1 to 65535.", nameof(value));

        // Apply defaults (preserving original precedence & behavior)
        if (defaults?.Protocol != null)
            parts.Scheme ??= ProtocolToScheme(defaults.Protocol.Value);
        parts.User ??= defaults?.Username;
        parts.Password ??= defaults?.Password;
        parts.Port ??= defaults?.Port; // keep same timing as before (assigned before protocol default logic)

        if (string.IsNullOrWhiteSpace(parts.Scheme) || string.IsNullOrWhiteSpace(parts.Host))
            throw new ArgumentException("URL must include scheme and host.", nameof(value));

        // the scheme is a proxy protocol's, with a port or not, in the form FromUrl reads
        var protocol = ParseProtocolLenient(parts.Scheme);
        parts.Port ??= GetDefaultPort(protocol);

        var uriBuilder = new UriBuilder {
            Scheme = ProtocolToScheme(protocol),
            Host = parts.Host,
            Port = parts.Port!.Value,
            Path = string.Empty
        };

        // escaped, as FromUrl reads them back: a '/', '?' or '#' in a password ended the URL's address
        if (!string.IsNullOrEmpty(parts.User)) uriBuilder.UserName = Uri.EscapeDataString(parts.User);
        if (!string.IsNullOrEmpty(parts.Password)) uriBuilder.Password = Uri.EscapeDataString(parts.Password);

        var valueCollection = HttpUtility.ParseQueryString(parsed.Query);
        if (defaults?.IsEnabled == false && valueCollection["enabled"] == null) {
            valueCollection["enabled"] = "false";
        }

        uriBuilder.Query = valueCollection.ToString();
        return uriBuilder.Uri;
    }

    /// <summary>
    /// Finds a proxy address in a line of text: a URL, an IPv4 or IPv6 address or a host name with a port, guessing
    /// the protocol from words such as <c>socks5</c> and from the port.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="defaultScheme">The scheme when nothing tells the protocol. The default is <c>http</c>.</param>
    /// <param name="preferHttpsWhenPort443">
    /// <see langword="true"/> to take port 443 with no other sign for HTTPS; <see langword="false"/> to give it
    /// <paramref name="defaultScheme"/>. The default is <see langword="true"/>.
    /// </param>
    /// <returns>The address as a URL, its query kept, or <see langword="null"/> when none is found.</returns>
    /// <remarks>
    /// An HTTPS address at an IP address with no user name comes back as HTTP, a written <c>https://</c> too: such a
    /// proxy almost never has a certificate the device trusts for its address, which an HTTPS proxy needs, and lists
    /// write HTTPS for an HTTP proxy that carries HTTPS. One with a user name stays HTTPS, so its password never travels
    /// in the clear.
    /// </remarks>
    public static Uri? ExtractFromText(string text,
        string defaultScheme = "http",
        bool preferHttpsWhenPort443 = true)
    {
        return ProxyEndPointExtractor.Extract(text, defaultScheme, preferHttpsWhenPort443);
    }

    /// <summary>
    /// Finds the proxy addresses in a text, as <see cref="ExtractFromText"/> does: the text is cut into pieces at line
    /// ends, commas, quotes, braces and parentheses, and each piece into words at white space, a word that is a port
    /// alone joining the address before it, as in <c>1.2.3.4 8080</c>, and a protocol's name alone the address after
    /// it, as in <c>SOCKS4 1.2.3.4 8080</c>, or, where none came before, the address before it, as tables give it:
    /// <c>1.2.3.4 8080 SOCKS4</c>. An HTTPS address at an IP address with no user name comes back as HTTP, as there.
    /// </summary>
    /// <param name="content">The text.</param>
    /// <param name="defaultScheme">The scheme when nothing tells the protocol. The default is <c>http</c>.</param>
    /// <param name="preferHttpsWhenPort443">
    /// <see langword="true"/> to take port 443 with no other sign for HTTPS; <see langword="false"/> to give it
    /// <paramref name="defaultScheme"/>. The default is <see langword="true"/>.
    /// </param>
    /// <returns>The addresses as URLs, in their order in the text.</returns>
    public static Uri[] ExtractFromContent(
        string content,
        string defaultScheme = "http",
        bool preferHttpsWhenPort443 = true)
    {
        var results = new List<Uri>();
        foreach (var piece in content.Split(PieceSeparators, StringSplitOptions.RemoveEmptyEntries)) {
            var words = piece.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < words.Length; i++) {
                var protocolWord = IsProtocolWord(words[i]) && i + 1 < words.Length ? words[i++] : null;
                var word = words[i];
                if (i + 1 < words.Length && IsPort(words[i + 1]) && !HasPortWritten(word))
                    word = $"{word}:{words[++i]}";

                // a protocol's name after an address, where none came before it
                if (protocolWord is null && i + 1 < words.Length && IsProtocolWord(words[i + 1]) &&
                    ProxyEndPointExtractor.Extract(word, defaultScheme, preferHttpsWhenPort443) != null)
                    protocolWord = words[++i];

                var uri = ProxyEndPointExtractor.Extract(protocolWord is null ? word : $"{protocolWord} {word}",
                    defaultScheme, preferHttpsWhenPort443);

                if (uri != null)
                    results.Add(uri);
            }
        }

        return [.. results];
    }

    // not white space, which parts an address from its port, nor brackets, which hold an IPv6 address
    private static readonly char[] PieceSeparators = ['\n', '\r', '"', '`', ',', '{', '}', '(', ')'];

    private static bool IsPort(string word) =>
        word.Length <= 5 && word.All(char.IsAsciiDigit) && int.Parse(word) is > 0 and <= 65535;

    private static bool IsProtocolWord(string word) =>
        word.TrimEnd(':').ToLowerInvariant() is "socks" or "socks4" or "socks5" or "http" or "https";

    private static bool TryCreateUri(string input, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        var schemaIndex = input.IndexOf("://", StringComparison.Ordinal);
        if (schemaIndex == -1 || input[..schemaIndex].Contains('.'))
            input = $"{PseudoScheme}://{input}";

        return Uri.TryCreate(input, UriKind.Absolute, out uri);
    }

    private sealed class UrlParts
    {
        public string? Scheme { get; set; }
        public string? Host { get; set; }
        public int? Port { get; set; }
        public string? User { get; set; }
        public string? Password { get; set; }
    }

    private static UrlParts ExtractParts(Uri url)
    {
        var parts = new UrlParts();

        if (!string.Equals(url.Scheme, PseudoScheme, StringComparison.OrdinalIgnoreCase))
            parts.Scheme = url.Scheme;

        if (!string.IsNullOrEmpty(url.Host))
            parts.Host = url.Host;

        // a port written in the address, the scheme's standard one too, which Uri's IsDefaultPort does not tell apart
        // from one left out
        if (HasPortWritten(url.OriginalString))
            parts.Port = url.Port;

        if (!string.IsNullOrEmpty(url.UserInfo)) {
            var userParts = url.UserInfo.Split(':', 2);
            parts.User = Uri.UnescapeDataString(userParts[0]);
            if (userParts.Length > 1)
                parts.Password = Uri.UnescapeDataString(userParts[1]);
        }

        return parts;
    }

    // whether a URL's authority names a port: after its user information, and after an IPv6 address's brackets
    internal static bool HasPortWritten(string url)
    {
        var start = url.IndexOf("://", StringComparison.Ordinal);
        var authority = start < 0 ? url : url[(start + 3)..];
        var end = authority.IndexOfAny(['/', '?', '#']);
        if (end >= 0)
            authority = authority[..end];

        var at = authority.LastIndexOf('@');
        if (at >= 0)
            authority = authority[(at + 1)..];

        var colon = authority.LastIndexOf(':');
        return colon > authority.LastIndexOf(']') && colon < authority.Length - 1;
    }

    private static ProxyProtocol ParseProtocolLenient(string scheme) => scheme.ToLowerInvariant() switch {
        "socks" or "socks5" or "socks5h" => ProxyProtocol.Socks5,
        "socks4" => ProxyProtocol.Socks4,
        "http" => ProxyProtocol.Http,
        "https" => ProxyProtocol.Https,
        _ => throw new ArgumentException($"Unknown protocol: {scheme}")
    };

    private static string ProtocolToScheme(ProxyProtocol protocol) => protocol switch {
        ProxyProtocol.Socks4 => "socks4",
        ProxyProtocol.Socks5 => "socks5",
        ProxyProtocol.Http => "http",
        ProxyProtocol.Https => "https",
        _ => throw new NotSupportedException($"Unsupported proxy protocol: {protocol}")
    };

    private static int GetDefaultPort(ProxyProtocol protocol) => protocol switch {
        ProxyProtocol.Socks4 => 1080,
        ProxyProtocol.Socks5 => 1080,
        ProxyProtocol.Http => 8080,
        ProxyProtocol.Https => 443,
        _ => 8080
    };
}