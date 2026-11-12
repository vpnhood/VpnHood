namespace VpnHood.AppLib.Api.Proxies;

// A proxy as a UI sees it, and as a UI sends one back when adding or editing. Id and Url are the
// engine's to compute - a hash of the address, and the address as a URL - so they arrive filled and
// are ignored on the way in: what the app reads from an incoming one is the protocol, the host, the
// port and the credentials.

/// <summary>
/// Represents a proxy server: its protocol, address, credentials and whether it is used.
/// </summary>
public class ProxyEndPoint
{
    /// <summary>
    /// Gets the endpoint's ID: the hex MD5 hash of its protocol's name, host and port, as in
    /// <c>Socks5://host:1080</c>. The host is hashed as written, so a change of its case gives another ID.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the endpoint is used. The default is <see langword="true"/>.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Gets the proxy's protocol.</summary>
    public required ProxyProtocol Protocol { get; init; }

    /// <summary>Gets the proxy's host name or IP address.</summary>
    public required string Host { get; init; }

    /// <summary>Gets the proxy's port.</summary>
    public required int Port { get; init; }

    /// <summary>Gets or sets the user name, or <see langword="null"/>.</summary>
    public string? Username { get; set; }

    /// <summary>Gets or sets the password, or <see langword="null"/>.</summary>
    public string? Password { get; set; }

    /// <summary>
    /// Gets the endpoint's URL, credentials included, as <c>socks5://user:password@host:1080/?enabled=1</c>. The
    /// query is there only for an enabled endpoint.
    /// </summary>
    public string Url { get; set; } = string.Empty;
}
