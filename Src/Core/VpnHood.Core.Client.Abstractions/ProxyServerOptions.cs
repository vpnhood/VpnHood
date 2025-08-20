namespace VpnHood.Core.Client.Abstractions;

public class ProxyServerOptions
{
    public ProxyServerType ProxyServerType { get; init; }
    public string? Address { get; init; }
    public int Port { get; init; }
    public string? Username { get; set; }
    public string? Password { get; set; }
}