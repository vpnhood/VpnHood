namespace VpnHood.AppLib.Contracts.Proxies;

// A proxy as a UI sees it, and as a UI sends one back when adding or editing. Id and Url are the
// engine's to compute - a hash of the address, and the address as a URL - so they arrive filled and
// are ignored on the way in: what the app reads from an incoming one is the protocol, the host, the
// port and the credentials.
public class ProxyEndPoint
{
    public string Id { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public required ProxyProtocol Protocol { get; init; }
    public required string Host { get; init; }
    public required int Port { get; init; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string Url { get; set; } = string.Empty;
}
