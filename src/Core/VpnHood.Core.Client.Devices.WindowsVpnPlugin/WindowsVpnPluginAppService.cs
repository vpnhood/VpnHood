namespace VpnHood.Core.Client.Devices.WindowsVpnPlugin;

// The package's app service, the VPN host's control channel, as both its ends name it: the app's client
// (WindowsVpnPluginAppServiceClient) and the VPN host's listener (WindowsVpnPluginAppServiceListener).
public static class WindowsVpnPluginAppService
{
    // the package manifest's uap:AppService Name
    public const string Name = "VpnService";

    // the one value of each message
    public const string MessageKey = "Message";
}
