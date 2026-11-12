namespace VpnHood.AppLib.Api.Proxies;

/// <summary>Represents a saved proxy: the proxy, how it has behaved, and where it is.</summary>
public class AppProxyEndPointInfo
{
    /// <summary>Gets or sets the proxy.</summary>
    public required ProxyEndPoint EndPoint { get; set; }

    /// <summary>Gets or sets how the proxy has behaved.</summary>
    public ProxyEndPointStatus Status { get; set; } = new();

    /// <summary>Gets or sets the proxy's country code, or <see langword="null"/> where it is not known.</summary>
    public required string? CountryCode { get; set; }
}
