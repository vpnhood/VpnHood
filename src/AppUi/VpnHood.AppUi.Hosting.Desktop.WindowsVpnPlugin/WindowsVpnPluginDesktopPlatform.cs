using VpnHood.AppUi.Hosting.Desktop.Platform;

namespace VpnHood.AppUi.Hosting.Desktop.WindowsVpnPlugin;

// The machine facts the Windows VPN plug-in answers: the app runs in the window's process, as whoever is
// signed in, in the package's LocalState, and Windows' own VPN host holds the tunnel, so there is no
// service to start, reach or install.
internal sealed class WindowsVpnPluginDesktopPlatform(DesktopInitParams initParams) : IInProcessDesktopPlatform
{
    public IDesktopUiPaths Paths { get; } = new WindowsVpnPluginDesktopPaths(initParams.AppId);
    public ILoopbackPeerCheck PeerCheck { get; } = new WindowsLoopbackPeerCheck();
    public IAppDaemonHost CreateApp() => new WindowsVpnPluginAppHost(initParams);
}
