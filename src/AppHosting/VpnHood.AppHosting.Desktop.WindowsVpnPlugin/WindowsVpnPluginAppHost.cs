using VpnHood.AppLogic.App;
using VpnHood.AppLogic.App.WindowsVpnPlugin;
using VpnHood.AppHosting.Desktop.Platform;

namespace VpnHood.AppHosting.Desktop.WindowsVpnPlugin;

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
