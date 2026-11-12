namespace VpnHood.AppLib.Api.Settings;

/// <summary>Represents the proxy settings.</summary>
public class AppProxySettings
{
    /// <summary>Gets or sets which proxy the VPN connects through. The default is <see cref="AppProxyMode.NoProxy"/>.</summary>
    public AppProxyMode Mode { get; set; }

    /// <summary>Gets the automatic update of the proxy list.</summary>
    public ProxyAutoUpdateOptions AutoUpdateOptions { get; init; } = new();
}