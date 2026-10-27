using Microsoft.Extensions.Logging;
using VpnHood.AppUi.Hosting.Desktop.Abstractions;
using VpnHood.AppUi.Hosting.Desktop.Platform;
using VpnHood.Core.Client.Devices.WindowsVpnPlugin;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Desktop.WindowsVpnPlugin;

// The machine facts the Windows VPN plug-in answers: the app runs in the window's process, as whoever is
// signed in, in the package's LocalState, and Windows' own VPN host holds the tunnel, so there is no
// service to start, reach or install. The package's sandbox (an AppContainer) shapes the rest: the
// window's pipe in the app's own namespace, no tray icon, the loopback check of its own, and links and
// the clipboard by Windows' own APIs.
internal sealed class WindowsVpnPluginDesktopPlatform(DesktopInitParams initParams) : IInProcessDesktopPlatform
{
    private const string LogFileName = "window.log";

    public IDesktopUiPaths Paths { get; } = new WindowsVpnPluginDesktopPaths(initParams.AppId);
    public ILoopbackPeerCheck PeerCheck { get; } = new WindowsVpnPluginLoopbackPeerCheck();
    public IDesktopSession DesktopSession { get; } = new WindowsDesktopSession { IsSandboxed = true };
    public bool UseTray => false;
    public IDesktopShell? Shell { get; } = new WindowsVpnPluginDesktopShell();

    // A file of the run's own, as the VPN host keeps one: the window has no console, and the Event Log
    // no source here, which only an administrator registers.
    public ILoggerProvider CreateLoggerProvider()
    {
        var fileLogger = new FileLogger(Path.Combine(WindowsVpnPluginFolders.LocalFolder, LogFileName)) { AutoFlush = true };
        fileLogger.Open(deleteOld: true);
        return new TextLoggerProvider(fileLogger);
    }

    public IAppDaemonHost CreateApp() => new WindowsVpnPluginAppHost(initParams);
}
