namespace VpnHood.Core.Client.Abstractions.ProxyNodes;

public class ProxyNode
{
    public string Id => $"{Protocol}-{Host}-{Port}";
    public bool IsEnabled { get; set; } = true;
    public required ProxyProtocol Protocol { get; init; }
    public required string Host { get; init; }
    public required int Port { get; init; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public Uri Url =>
        new UriBuilder {
            Scheme = Protocol.ToString().ToLowerInvariant(),
            Host = Host,
            Port = Port,
            UserName = Username,
            Password = Password,
            Query = IsEnabled ? "enabled=1" : null
        }.Uri;
}