using System.Diagnostics.CodeAnalysis;
using System.Web;

namespace VpnHood.Core.Client.Abstractions.ProxyNodes;

public static class ProxyNodeParser
{
    private const string PseudoScheme = "unknown";

    public static ProxyNode FromUrl(Uri uri)
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

        return new ProxyNode {
            Protocol = protocol,
            Host = uri.Host,
            Port = port,
            Username = username,
            Password = password,
            IsEnabled = isEnabledValue is not ("false" or "0")
        };
    }

    public static ProxyNode Normalize(ProxyNode proxyNode)
    {
        var defaults = new ProxyNodeDefaults {
            IsEnabled = proxyNode.IsEnabled,
            Password = proxyNode.Password,
            Port = proxyNode.Port,
            Protocol = proxyNode.Protocol,
            Username = proxyNode.Username
        };

        var ret = TryParseToUrl(proxyNode.Host, defaults, out var uri)
            ? FromUrl(uri)
            : throw new ArgumentException("Invalid proxy.");

        ret.Username = proxyNode.Username?.Trim();
        ret.Password = proxyNode.Password?.Trim();
        return ret;
    }

    public static bool TryParseToUrl(
        string value,
        ProxyNodeDefaults? defaults,
        [NotNullWhen(true)] out Uri? url)
    {
        url = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        value = value.Trim();

        if (!TryCreateUri(value, out var parsed))
            return false;

        var parts = ExtractParts(parsed);
        // Apply defaults (preserving original precedence & behavior)
        if (defaults?.Protocol != null)
            parts.Scheme ??= ProtocolToScheme(defaults.Protocol.Value);
        parts.User ??= defaults?.Username;
        parts.Password ??= defaults?.Password;
        parts.Port ??= defaults?.Port; // keep same timing as before (assigned before protocol default logic)

        if (string.IsNullOrWhiteSpace(parts.Scheme) || string.IsNullOrWhiteSpace(parts.Host))
            return false;

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

        try {
            url = uriBuilder.Uri;
            return true;
        }
        catch {
            return false;
        }
    }

    public static Uri[] ParseText(
        string text,
        string defaultScheme = "http",
        bool preferHttpsWhenPort443 = true)
    {
        // Split by new lines
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        return ProxyNodeParserBulk.Parse(lines, defaultScheme, preferHttpsWhenPort443);
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
}