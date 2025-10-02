using System.Diagnostics.CodeAnalysis;
using System.Web;

namespace VpnHood.Core.Client.Abstractions.ProxyNodes;

public static class VhUrlParser
{
    private const string PseudoScheme = "unknown";

    public static bool TryParse(
        string value,
        ProxyProtocol? defaultProtocol,
        int? defaultPort,
        string? defaultUsername,
        string? defaultPassword,
        bool? defaultEnabled,
        [NotNullWhen(true)] out Uri? url)
    {
        url = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        value = value.Trim();

        if (!TryCreateUri(value, out var parsed))
            return false;

        var parts = ExtractParts(parsed);
        if (defaultProtocol!=null) parts.Scheme ??= ProtocolToScheme(defaultProtocol.Value);
        parts.User ??= defaultUsername;
        parts.Password ??= defaultPassword;
        parts.Port ??= defaultPort;
        
        // IMPORTANT: Do NOT apply defaultPort here; tests expect protocol default to win over passed defaultPort.
        // If we applied defaultPort here it would override the protocol's intrinsic default ports (e.g., 8080 / 1080 / 443).
        if (string.IsNullOrWhiteSpace(parts.Scheme) || string.IsNullOrWhiteSpace(parts.Host))
            return false;

        // apply protocol default port if still missing
        if (parts.Port is null) {
            var proto = ParseProtocolLenient(parts.Scheme);
            parts.Port = GetDefaultPort(proto);
        }

        // create UriBuilder to validate parts
        var uriBuilder = new UriBuilder {
            Scheme = parts.Scheme,
            Host = parts.Host,
            Port = parts.Port!.Value,
            Path = string.Empty
        };

        // add user info if any
        if (!string.IsNullOrEmpty(parts.User)) uriBuilder.UserName = parts.User;
        if (!string.IsNullOrEmpty(parts.Password)) uriBuilder.Password = parts.Password;


        // Only add enabled=false if explicitly requested and not already present
        var valueCollection = HttpUtility.ParseQueryString(parsed.Query);
        if (defaultEnabled == false && valueCollection["enabled"] == null) {
            valueCollection["enabled"] = "false";
        }
        uriBuilder.Query = valueCollection.ToString(); // serializes back to key=value&key2=value2

        try {
            url = uriBuilder.Uri;
            return true;
        }
        catch {
            return false;
        }
    }

    private static bool TryCreateUri(string input, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        var schemaIndex = input.IndexOf("://", StringComparison.Ordinal);
        if (schemaIndex == -1 || input[..schemaIndex].Contains("."))
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

        // Extract scheme (but not the pseudo scheme)
        if (!string.Equals(url.Scheme, PseudoScheme, StringComparison.OrdinalIgnoreCase))
            parts.Scheme = url.Scheme;

        // Extract host
        if (!string.IsNullOrEmpty(url.Host))
            parts.Host = url.Host;

        // Extract port (only if it's valid and not the default port for the scheme)
        if (url is { Port: > 0, IsDefaultPort: false })
            parts.Port = url.Port;

        // Extract user info
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