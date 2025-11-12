using System.Text.RegularExpressions;

namespace VpnHood.Core.Proxies.EndPointManagement.Abstractions;

// Vibe: Extract a single proxy URI from heterogeneous text.
internal static partial class ProxyEndPointExtractor
{
    /// <summary>
    /// Extract a single proxy URI from heterogeneous text.
    /// Tries to intelligently detect protocol based on port numbers and keywords.
    /// </summary>
    /// <param name="text">Raw text that may contain proxy information.</param>
    /// <param name="defaultScheme">Scheme to use when none can be determined (default "http").</param>
    /// <param name="preferHttpsWhenPort443">If true and no scheme present but port == 443, use https.</param>
    /// <returns>Parsed Uri object or null if no valid proxy found.</returns>
    public static Uri? Extract(
        string text,
        string defaultScheme = "http",
        bool preferHttpsWhenPort443 = true)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        text = text.Trim();

        // First, try to find a complete URL in the text
        var urlMatch = UrlRegex().Match(text);
        if (urlMatch.Success) {
            var urlString = urlMatch.Value;
            if (Uri.TryCreate(urlString, UriKind.Absolute, out var uri) && IsValidProxyScheme(uri.Scheme)) {
                return NormalizeUri(uri);
            }
        }

        // Try to find protocol keywords in the text
        var detectedProtocol = DetectProtocolFromKeywords(text);

        // Try to extract host and port patterns
        // Pattern 1: IPv6 with port: [2001:db8::1]:1080
        var ipv6Match = Ipv6WithPortRegex().Match(text);
        if (ipv6Match.Success) {
            var host = ipv6Match.Groups["host"].Value;
            if (!int.TryParse(ipv6Match.Groups["port"].Value, out var port) || port < 1 || port > 65535)
                return null;
            var scheme = DetermineScheme(detectedProtocol, port, defaultScheme, preferHttpsWhenPort443);
            return BuildUri(scheme, host, port);
        }

        // Pattern 2: IPv4 with port (look for IP address specifically)
        // Matches: "143.198.147.156 8888" or "192.168.1.1:9999"  
        var ipv4Match = Ipv4PortRegex().Match(text);
        if (ipv4Match.Success) {
            var host = ipv4Match.Groups["host"].Value;
            if (!IsValidIpv4(host))
                return null;
            if (!int.TryParse(ipv4Match.Groups["port"].Value, out var port) || port < 1 || port > 65535)
                return null;
            var scheme = DetermineScheme(detectedProtocol, port, defaultScheme, preferHttpsWhenPort443);
            return BuildUri(scheme, host, port);
        }

        // Pattern 3: hostname with port (more lenient)
        // Must NOT match partial IP addresses like "71.67" - require letters in hostname
        // Matches: "proxy.com:1080" or "proxy.example.com 9999"
        var hostPortMatch = HostPortRegex().Match(text);
        if (hostPortMatch.Success) {
            var host = hostPortMatch.Groups["host"].Value;
            var portStr = hostPortMatch.Groups["port"].Value;

            // Validate port is in valid range
            if (!int.TryParse(portStr, out var port) || port < 1 || port > 65535)
                return null;

            var scheme = DetermineScheme(detectedProtocol, port, defaultScheme, preferHttpsWhenPort443);
            return BuildUri(scheme, host, port);
        }

        // Pattern 4: Just IPv6 without port
        var ipv6OnlyMatch = Ipv6OnlyRegex().Match(text);
        if (ipv6OnlyMatch.Success) {
            var host = ipv6OnlyMatch.Groups["host"].Value;
            var scheme = detectedProtocol ?? defaultScheme;
            var port = GetDefaultPortForScheme(scheme);
            return BuildUri(scheme, host, port);
        }

        // Pattern 5: Just IPv4 without explicit port
        var ipv4OnlyMatch = Ipv4OnlyRegex().Match(text);
        if (ipv4OnlyMatch.Success) {
            var host = ipv4OnlyMatch.Groups["host"].Value;
            if (!IsValidIpv4(host))
                return null;

            // Only return if protocol was explicitly detected
            if (detectedProtocol == null)
                return null;

            var scheme = detectedProtocol;
            var port = GetDefaultPortForScheme(scheme);
            return BuildUri(scheme, host, port);
        }

        return null;
    }

    private static bool IsValidIpv4(string ip)
    {
        var parts = ip.Split('.');
        if (parts.Length != 4)
            return false;

        foreach (var part in parts) {
            if (!int.TryParse(part, out var num) || num < 0 || num > 255)
                return false;
        }

        return true;
    }

    private static string? DetectProtocolFromKeywords(string text)
    {
        var lowerText = text.ToLowerInvariant();

        // Check for protocol keywords (order matters - be specific first)
        if (lowerText.Contains("socks5") || lowerText.Contains("socks 5"))
            return "socks5";
        if (lowerText.Contains("socks4") || lowerText.Contains("socks 4"))
            return "socks4";
        if (lowerText.Contains("socks"))
            return "socks5"; // default socks to socks5
        if (lowerText.Contains("https"))
            return "https";
        if (lowerText.Contains("http"))
            return "http";

        return null;
    }

    private static string DetermineScheme(string? detectedProtocol, int port, string defaultScheme,
        bool preferHttpsWhenPort443)
    {
        // If protocol was detected from keywords, use it
        if (!string.IsNullOrEmpty(detectedProtocol))
            return detectedProtocol;

        // Otherwise, guess from port
        return port switch {
            1080 => "socks5", // Common SOCKS port
            1081 => "socks5", // Alternative SOCKS port
            9050 => "socks5", // Tor SOCKS port
            443 when preferHttpsWhenPort443 => "https",
            443 => "https",
            80 => "http",
            8080 => "http",
            3128 => "http", // Common HTTP proxy port (Squid)
            8888 => "http", // Common HTTP proxy port
            _ => defaultScheme
        };
    }

    private static int GetDefaultPortForScheme(string scheme)
    {
        return scheme.ToLowerInvariant() switch {
            "socks4" => 1080,
            "socks5" => 1080,
            "socks" => 1080,
            "http" => 8080,
            "https" => 443,
            _ => 8080
        };
    }

    private static bool IsValidProxyScheme(string scheme)
    {
        var lower = scheme.ToLowerInvariant();
        return lower is "http" or "https" or "socks4" or "socks5" or "socks" or "socks5h";
    }

    private static Uri NormalizeUri(Uri uri)
    {
        // Normalize scheme (e.g., socks -> socks5)
        var scheme = uri.Scheme.ToLowerInvariant() switch {
            "socks" => "socks5",
            "socks5h" => "socks5",
            _ => uri.Scheme.ToLowerInvariant()
        };

        // Ensure port is set
        var port = uri is { Port: > 0, IsDefaultPort: false }
            ? uri.Port
            : GetDefaultPortForScheme(scheme);

        return BuildUri(scheme, uri.Host, port, uri.UserInfo);
    }

    private static Uri BuildUri(string scheme, string host, int port, string? userInfo = null)
    {
        var ub = new UriBuilder {
            Scheme = scheme.ToLowerInvariant(),
            Host = host,
            Port = port
        };

        if (!string.IsNullOrEmpty(userInfo)) {
            var parts = userInfo.Split(':', 2);
            ub.UserName = Uri.UnescapeDataString(parts[0]);
            if (parts.Length > 1)
                ub.Password = Uri.UnescapeDataString(parts[1]);
        }

        return ub.Uri;
    }

    // Complete URL pattern: scheme://[user:pass@]host[:port]
    [GeneratedRegex(
        @"(?<url>(?:https?|socks5?h?|socks)://(?:[^:@\s]+(?::[^@\s]*)?@)?(?:\[[^\]]+\]|[a-zA-Z0-9\.\-]+)(?::\d{1,5})?)",
        RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    // IPv6 with port: [2001:db8::1]:1080
    [GeneratedRegex(@"\[(?<host>[0-9a-fA-F:]+)\]:(?<port>\d{1,5})\b")]
    private static partial Regex Ipv6WithPortRegex();

    // IPv4 with port (strict - must be valid IPv4 address)
    // Matches: "143.198.147.156 8888" or "192.168.1.1:9999"
    [GeneratedRegex(@"\b(?<host>\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})[\s:]+(?<port>\d{1,5})\b")]
    private static partial Regex Ipv4PortRegex();

    // Hostname with port (more lenient, but requires valid hostname format with at least one dot)
    // Must NOT match partial IP addresses like "71.67" - require letters in hostname
    // Matches: "proxy.com:1080" or "proxy.example.com 9999"
    [GeneratedRegex(
        @"\b(?<host>(?=.*[a-zA-Z])[a-zA-Z0-9](?:[a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?)+)[\s:]+(?<port>\d{1,5})\b")]
    private static partial Regex HostPortRegex();

    // Just IPv6: [2001:db8::1]
    [GeneratedRegex(@"\[(?<host>[0-9a-fA-F:]+)\]")]
    private static partial Regex Ipv6OnlyRegex();

    // Just IPv4 (strict)
    [GeneratedRegex(@"\b(?<host>\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\b")]
    private static partial Regex Ipv4OnlyRegex();
}