namespace VpnHood.Core.Proxies.EndPointManagement.Abstractions.Options;

public class ProxyOptions
{
    public ProxyEndPoint[] ProxyEndPoints { get; init; } = [];
    public bool ResetStates { get; init; }
    public ProxyAutoUpdateOptions AutoUpdateOptions { get; init; } = new();
    public bool VerifyTls { get; init; } = true;
}