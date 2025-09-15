using VpnHood.Core.Client.Abstractions;

namespace VpnHood.AppLib;

public static class ProxyNodeConverter
{
    public static string ToUrl(ProxyNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var scheme = node.Protocol.ToString().ToLower();
        var userInfo = "";
        if (!string.IsNullOrEmpty(node.Username)) {
            var user = Uri.EscapeDataString(node.Username);
            var pass = string.IsNullOrEmpty(node.Password)
                ? ""
                : $":{Uri.EscapeDataString(node.Password)}";
            userInfo = $"{user}{pass}@";
        }

        return $"{scheme}://{userInfo}{node.Host}:{node.Port}";
    }

    public static ProxyNode FromUrl(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ArgumentException($"Invalid proxy URL format: {url}", nameof(url));

        if (!Enum.TryParse<ProxyProtocol>(uri.Scheme, ignoreCase: true, out var protocol))
            throw new NotSupportedException($"Unsupported scheme: {uri.Scheme}");

        if (string.IsNullOrWhiteSpace(uri.Host))
            throw new ArgumentException("Proxy URL must include a host.", nameof(url));

        string? username = null;
        string? password = null;
        if (!string.IsNullOrEmpty(uri.UserInfo)) {
            var parts = uri.UserInfo.Split(':', 2);
            username = Uri.UnescapeDataString(parts[0]);
            if (parts.Length > 1)
                password = Uri.UnescapeDataString(parts[1]);
        }

        var port = uri.Port > 0 ? uri.Port : GetDefaultPort(protocol);

        return new ProxyNode {
            Protocol = protocol,
            Host = uri.Host,
            Port = port,
            Username = username,
            Password = password,
            IsEnabled = true
        };
    }

    public static int GetDefaultPort(ProxyProtocol protocol) => protocol switch {
        ProxyProtocol.Socks4 => 1080,
        ProxyProtocol.Socks5 => 1080,
        ProxyProtocol.Http => 8080,
        ProxyProtocol.Https => 443,
        _ => -1
    };

}