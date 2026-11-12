using System.Diagnostics.CodeAnalysis;
using System.Web;

namespace VpnHood.Core.Proxies.Management.Abstractions;

/// <summary>
/// Provides the reading of proxy endpoints from URLs, from loosely written addresses, and from text.
/// </summary>
public static class ProxyEndPointParser
{
    private const string PseudoScheme = "unknown";

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
    /// A new endpoint, with the original's user name and password, trimmed, in place of any the host held.
    /// </returns>
    /// <exception cref="ArgumentException">The host is empty or no valid address.</exception>
    /// <exception cref="NotSupportedException">The address's scheme is no proxy protocol.</exception>
    public static ProxyEndPoint Normalize(ProxyEndPoint proxyEndPoint)
    {
        var defaults = new ProxyEndPointDefaults {
            IsEnabled = proxyEndPoint.IsEnabled,
            Password = proxyEndPoint.Password,
            Port = proxyEndPoint.Port,
            Protocol = proxyEndPoint.Protocol,
            Username = proxyEndPoint.Username
        };

        var uri = ParseHostToUrl(proxyEndPoint.Host, defaults);
        var ret = FromUrl(uri);
        ret.Username = proxyEndPoint.Username?.Trim();
        ret.Password = proxyEndPoint.Password?.Trim();
        return ret;
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
    /// protocol's default (1080 for SOCKS, 8080 for HTTP, 443 for HTTPS), and <c>socks</c> and <c>socks5h</c> read as
    /// SOCKS5 for it. A port that is the scheme's standard one, as 80 for <c>http</c>, counts as missing.
    /// </param>
    /// <returns>
    /// The URL; with <c>enabled=false</c> in its query when <paramref name="defaults"/> says the endpoint is
    /// disabled and the address's query does not say otherwise.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// The address is empty or not valid, or the address and the defaults give no scheme, or the scheme is no proxy
    /// protocol and no port is given; with a port, any scheme passes.
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
        // Apply defaults (preserving original precedence & behavior)
        if (defaults?.Protocol != null)
            parts.Scheme ??= ProtocolToScheme(defaults.Protocol.Value);
        parts.User ??= defaults?.Username;
        parts.Password ??= defaults?.Password;
        parts.Port ??= defaults?.Port; // keep same timing as before (assigned before protocol default logic)

        if (string.IsNullOrWhiteSpace(parts.Scheme) || string.IsNullOrWhiteSpace(parts.Host))
            throw new ArgumentException("URL must include scheme and host.", nameof(value));

        // apply protocol default port if still missing
        if (parts.Port is null) {
            var proto = ParseProtocolLenient(parts.Scheme);
            parts.Port = GetDefaultPort(proto);
        }

        var uriBuilder = new UriBuilder {
            Scheme = parts.Scheme,
            Host = parts.Host,
            Port = parts.Port!.Value,
            Path = string.Empty
        };

        if (!string.IsNullOrEmpty(parts.User)) uriBuilder.UserName = parts.User;
        if (!string.IsNullOrEmpty(parts.Password)) uriBuilder.Password = parts.Password;

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
    /// Meant to choose HTTPS for port 443 with no other sign; it changes nothing, as port 443 is taken for HTTPS
    /// either way. The default is <see langword="true"/>.
    /// </param>
    /// <returns>The address as a URL, or <see langword="null"/> when none is found.</returns>
    public static Uri? ExtractFromText(string text,
        string defaultScheme = "http",
        bool preferHttpsWhenPort443 = true)
    {
        return ProxyEndPointExtractor.Extract(text, defaultScheme, preferHttpsWhenPort443);
    }

    /// <summary>
    /// Finds the proxy addresses in a text, as <see cref="ExtractFromText"/> does, after splitting it into pieces at
    /// line ends, spaces, commas, quotes and brackets, so an address whose port follows a space, as
    /// <c>1.2.3.4 8080</c>, is lost.
    /// </summary>
    /// <param name="content">The text.</param>
    /// <param name="defaultScheme">The scheme when nothing tells the protocol. The default is <c>http</c>.</param>
    /// <param name="preferHttpsWhenPort443">
    /// Meant to choose HTTPS for port 443 with no other sign; it changes nothing, as port 443 is taken for HTTPS
    /// either way. The default is <see langword="true"/>.
    /// </param>
    /// <returns>The addresses as URLs, in their order in the text.</returns>
    public static Uri[] ExtractFromContent(
        string content,
        string defaultScheme = "http",
        bool preferHttpsWhenPort443 = true)
    {
        // Normalize delimiters to new lines
        content = FastReplaceCharsWithNewline(content);

        // Split by new lines
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var results = new List<Uri>();

        foreach (var line in lines) {
            var uri = ProxyEndPointExtractor.Extract(line, defaultScheme, preferHttpsWhenPort443);
            if (uri != null)
                results.Add(uri);
        }

        return [.. results];
    }

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

        if (url is { Port: > 0, IsDefaultPort: false })
            parts.Port = url.Port;

        if (!string.IsNullOrEmpty(url.UserInfo)) {
            var userParts = url.UserInfo.Split(':', 2);
            parts.User = Uri.UnescapeDataString(userParts[0]);
            if (userParts.Length > 1)
                parts.Password = Uri.UnescapeDataString(userParts[1]);
        }

        return parts;
    }

    private static ProxyProtocol ParseProtocolLenient(string scheme) => scheme.ToLowerInvariant() switch {
        "socks" or "socks5" or "socks5h" => ProxyProtocol.Socks5,
        "socks4" => ProxyProtocol.Socks4,
        "http" => ProxyProtocol.Http,
        "https" => ProxyProtocol.Https,
        _ => throw new ArgumentException($"Unknown protocol: {scheme}")
    };

    private static string? ProtocolToScheme(ProxyProtocol protocol) => protocol switch {
        ProxyProtocol.Socks4 => "socks4",
        ProxyProtocol.Socks5 => "socks5",
        ProxyProtocol.Http => "http",
        ProxyProtocol.Https => "https",
        _ => null
    };

    private static int GetDefaultPort(ProxyProtocol protocol) => protocol switch {
        ProxyProtocol.Socks4 => 1080,
        ProxyProtocol.Socks5 => 1080,
        ProxyProtocol.Http => 8080,
        ProxyProtocol.Https => 443,
        _ => 8080
    };

    private static string FastReplaceCharsWithNewline(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        var chars = content.ToCharArray();

        for (var i = 0; i < chars.Length; i++) {
            if (chars[i] is '\r' or ' ' or '"' or '`' or ',' or '{' or '}' or '(' or ')' or '[' or ']')
                chars[i] = '\n';
        }

        return new string(chars);
    }
}