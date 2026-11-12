using VpnHood.AppLib.Abstractions.Device;

namespace VpnHood.AppLib.App.WindowsTun;

/// <summary>Provides what the app's UI asks of a Windows device: the system's proxy, on top of what <see cref="NullDeviceUiProvider"/> gives.</summary>
internal class WindowsDeviceUiProvider : NullDeviceUiProvider
{
    /// <inheritdoc/>
    /// <remarks>It is <see langword="true"/>.</remarks>
    public override bool IsProxySettingsSupported => true;
    private readonly Uri _internetUrl = new("https://www.microsoft.com");

    /// <inheritdoc/>
    /// <remarks>
    /// It reads the proxy the process's default proxy gives for one internet address, which may come from the system's
    /// settings or from the environment, or <see langword="null"/> where it gives none.
    /// </remarks>
    public override DeviceProxySettings? GetProxySettings()
    {
        var proxyUrl = HttpClient.DefaultProxy.GetProxy(_internetUrl);
        if (proxyUrl == null)
            return null;

        return new DeviceProxySettings {
            ProxyUrl = proxyUrl,
            PacFileUrl = null,
            ExcludeDomains = []
        };
    }
}