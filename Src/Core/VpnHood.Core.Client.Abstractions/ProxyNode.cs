namespace VpnHood.Core.Client.Abstractions;

public class ProxyNode
{
    public bool IsEnabled { get; set; } = true;
    public required ProxyProtocol Protocol { get; init; }
    public required string Host { get; init; }
    public required int Port { get; init; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    
    public string GetId() => $"{Protocol}:{Host}:{Port}";
}
