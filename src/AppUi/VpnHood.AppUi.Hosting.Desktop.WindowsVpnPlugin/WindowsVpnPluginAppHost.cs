using VpnHood.AppLib.App;
using VpnHood.AppLib.App.WindowsVpnPlugin;
using VpnHood.AppUi.Hosting.Desktop.Platform;

namespace VpnHood.AppUi.Hosting.Desktop.WindowsVpnPlugin;

// The app in the window's process (VpnHoodWindowsVpnPluginApp). Disposing it ends the app, never the
// tunnel, which Windows' VPN host holds: the window's Exit leaves a connection up.
internal sealed class WindowsVpnPluginAppHost(AppInitParams initParams) : IAppDaemonHost
{
    private readonly VpnHoodWindowsVpnPluginApp _app = VpnHoodWindowsVpnPluginApp.Init(initParams);

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        _app.Dispose();
    }
}
