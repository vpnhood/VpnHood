namespace VpnHood.Core.Proxies.EndPointManagement.Abstractions.Options;

public class ProxyOptions
{
    public ProxyMode Mode { get; init; } = ProxyMode.None;

    /// <summary>The proxy used when <see cref="Mode"/> is <see cref="ProxyMode.Simple"/>.
    /// Ignored in other modes. In Managed mode endpoints live in the shared endpoint store.</summary>
    public ProxyEndPoint? ProxyEndPoint { get; init; }

    public bool ResetStates { get; init; }
    public ProxyAutoUpdateOptions AutoUpdateOptions { get; init; } = new();
    public bool VerifyTls { get; init; } = true;
}
