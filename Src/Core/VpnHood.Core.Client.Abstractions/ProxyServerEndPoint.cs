namespace VpnHood.Core.Client.Abstractions;

public class ProxyServerEndPoint
{
    public required ProxyServerType Type { get; init; }
    public required string Host { get; init; }
    public required int Port { get; init; }
    public string? Username { get; set; }
    public string? Password { get; set; }
}