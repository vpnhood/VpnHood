namespace VpnHood.Core.Client.Abstractions.ProxyNodes;

using System;
using System.Diagnostics.CodeAnalysis;

public static class VhUrlParser
{
    // tries many methods to parse the value. 
    // isEnabled is query string and only set if it is false, such as http://user:pass@host:port?enabled=false
    // default values only set if they all not null and not empty
    public static bool TryParse(
        string value,
        ProxyProtocol? defaultProtocol,
        string? defaultUsername,
        string? defaultPassword,
        string? defaultHost,
        int? defaultPort,
        bool? isEnabled,
        [NotNullWhen(true)] out Uri? url)
    {
        url = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        value = value.Trim();

        // Extract parts from input
        Parts parts;
        if (Uri.TryCreate(value, UriKind.Absolute, out var asUri)) {
            parts = ExtractFromUri(asUri);
        }
        else if (TryParseHostPort(value, out var host2, out var port2)) {
            parts = new Parts { Host = host2, Port = port2 };
        }
        else if (!value.Contains(':') && !value.Contains('/')) {
            parts = new Parts { Host = value };
        }
        else {
            return false;
        }

        // Determine if all defaults are present & non-empty
        var allDefaultsPresent =
            defaultProtocol.HasValue &&
            !string.IsNullOrWhiteSpace(defaultHost) &&
            defaultPort.HasValue &&
            !string.IsNullOrWhiteSpace(defaultUsername) &&
            !string.IsNullOrWhiteSpace(defaultPassword);

        // Apply defaults only if ALL are present and only for missing fields
        if (allDefaultsPresent) {
            parts.Scheme ??= ProtocolToScheme(defaultProtocol!.Value);
            parts.Host ??= defaultHost!;
            parts.Port ??= defaultPort!.Value;
            parts.User ??= defaultUsername!;
            parts.Pass ??= defaultPassword!;
        }

        // If still missing a scheme, try to use provided defaultProtocol alone (if present)
        if (parts.Scheme is null && defaultProtocol.HasValue) {
            parts.Scheme = ProtocolToScheme(defaultProtocol.Value);
        }

        // If still missing required fields, fail
        if (string.IsNullOrEmpty(parts.Scheme) || string.IsNullOrEmpty(parts.Host))
            return false;

        // Port: if missing, use protocol default
        if (!parts.Port.HasValue) {
            var proto = ParseProtocolLenient(parts.Scheme);
            parts.Port = GetDefaultPort(proto);
        }

        if (!IsValidPort(parts.Port!.Value))
            return false;

        // Build final Uri
        var ub = new UriBuilder {
            Scheme = parts.Scheme!,
            Host = TrimIpv6Brackets(parts.Host!),
            Port = parts.Port.Value,
            Path = string.Empty // avoid trailing slash if possible
        };

        // Credentials (percent-encode safely)
        if (!string.IsNullOrEmpty(parts.User))
            ub.UserName = parts.User!;
        if (!string.IsNullOrEmpty(parts.Pass))
            ub.Password = parts.Pass!;

        // enabled=false only when explicitly false
        if (isEnabled == false)
            ub.Query = "enabled=false";

        try {
            url = ub.Uri;
            return true;
        }
        catch {
            return false;
        }
    }

    /* ----------------- helpers ----------------- */
    
    private sealed class Parts
    {
        public string? Scheme { get; set; }
        public string? Host { get; set; }
        public int? Port { get; set; }
        public string? User { get; set; }
        public string? Pass { get; set; }
    }

    private static Parts ExtractFromUri(Uri uri)
    {
        var p = new Parts();

        if (!string.IsNullOrEmpty(uri.Scheme))
            p.Scheme = uri.Scheme;

        if (!string.IsNullOrEmpty(uri.Host))
            p.Host = TrimIpv6Brackets(uri.Host);

        if (!uri.IsDefaultPort)
            p.Port = uri.Port;

        if (!string.IsNullOrEmpty(uri.UserInfo)) {
            var up = uri.UserInfo.Split(':', 2);
            p.User = Uri.UnescapeDataString(up[0]);
            if (up.Length > 1)
                p.Pass = Uri.UnescapeDataString(up[1]);
        }

        return p;
    }

    // Parses "[::1]:1080" or "host:8080"
    private static bool TryParseHostPort(string value, out string host, out int port)
    {
        host = ""; port = 0;

        if (value.StartsWith("[", StringComparison.Ordinal)) {
            var i = value.IndexOf(']');
            if (i <= 0 || i + 2 > value.Length || value[i + 1] != ':')
                return false;

            host = value.Substring(1, i - 1);
            return int.TryParse(value.AsSpan(i + 2), out port);
        }

        var sep = value.LastIndexOf(':');
        if (sep <= 0) return false;

        host = value[..sep];
        return int.TryParse(value.AsSpan(sep + 1), out port);
    }

    private static string TrimIpv6Brackets(string host)
        => host.Length > 1 && host[0] == '[' && host[^1] == ']' ? host[1..^1] : host;

    private static bool IsValidPort(int port) => port is >= 1 and <= 65535;

    private static ProxyProtocol ParseProtocolLenient(string scheme) => scheme.ToLowerInvariant() switch {
        "socks" or "socks5" or "socks5h" => ProxyProtocol.Socks5,
        "socks4" => ProxyProtocol.Socks4,
        "http" => ProxyProtocol.Http,
        "https" => ProxyProtocol.Https,
        _ => throw new ArgumentException($"Unknown protocol: {scheme}")
    };

    private static string ProtocolToScheme(ProxyProtocol p) => p switch {
        ProxyProtocol.Socks4 => "socks4",
        ProxyProtocol.Socks5 => "socks5",
        ProxyProtocol.Http => "http",
        ProxyProtocol.Https => "https",
        _ => "http"
    };

    private static int GetDefaultPort(ProxyProtocol protocol) => protocol switch {
        ProxyProtocol.Socks4 => 1080,
        ProxyProtocol.Socks5 => 1080,
        ProxyProtocol.Http => 8080, // keep your project convention
        ProxyProtocol.Https => 443,
        _ => 8080
    };
}
