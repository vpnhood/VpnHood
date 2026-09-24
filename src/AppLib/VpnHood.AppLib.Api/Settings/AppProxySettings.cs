namespace VpnHood.AppLib.Api.Settings;

public class AppProxySettings
{
    public AppProxyMode Mode { get; set; }
    public ProxyAutoUpdateOptions AutoUpdateOptions { get; init; } = new();
}