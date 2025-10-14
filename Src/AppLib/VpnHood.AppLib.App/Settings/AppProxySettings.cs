using VpnHood.Core.Client.Abstractions.ProxyNodes;

namespace VpnHood.AppLib.Settings;

public class AppProxySettings
{
    public AppProxyMode Mode { get; set; }
    public Uri? RemoteNotesUrl { get; set; }
}