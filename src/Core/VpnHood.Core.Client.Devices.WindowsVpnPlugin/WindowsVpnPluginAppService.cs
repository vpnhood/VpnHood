namespace VpnHood.Core.Client.Devices.WindowsVpnPlugin;

/// <summary>
/// Provides the names of the package's app service, the VPN host's control channel, as both its ends use them: the app's
/// client (<see cref="WindowsVpnPluginAppServiceClient"/>) and the VPN host's listener.
/// </summary>
public static class WindowsVpnPluginAppService
{
    /// <summary>The app service's name, as the package manifest's <c>uap:AppService</c> gives it: <c>VpnService</c>.</summary>
    public const string Name = "VpnService";

    /// <summary>The key of each message's one value, its bytes: <c>Message</c>.</summary>
    public const string MessageKey = "Message";
}
