namespace VpnHood.Core.Proxies.EndPointManagement.Abstractions.Options;

public class ProxyOptions
{
    public ProxyEndPoint[] ProxyEndPoints { get; init; } = [];
    public bool ResetStates { get; set; }
    public ProxyAutoUpdateOptions EndPointProxyAutoUpdateOptions { get; init; } = new();
}
