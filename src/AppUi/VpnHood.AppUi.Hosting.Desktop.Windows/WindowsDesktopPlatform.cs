using Microsoft.Extensions.Logging;
using VpnHood.AppLib.App.Windows;
using VpnHood.AppUi.Hosting.Desktop.Abstractions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Desktop.Windows;

// The machine facts Windows answers. The app runs in a LocalSystem service, "daemon" under the
// service control manager, which "service install" registers and which stops it with a call; the
// window and its tray run as whoever is signed in, not elevated, and reach the service over loopback;
// the service's warnings and the window's go to the Application log under the service's name.
public sealed class WindowsDesktopPlatform : IDesktopPlatform
{
    private readonly WindowsDesktopPaths _paths;

    public WindowsDesktopPlatform(DesktopInitParams initParams)
    {
        _paths = new WindowsDesktopPaths(initParams.AppId);
        var setup = new WindowsServiceSetup(_paths, initParams.AppName);
        var channel = new WindowsDaemonChannel(_paths);
        Instance = new WindowsInstanceController(_paths, setup, channel);
        Channel = channel;
        InstanceSetup = setup;
        DaemonHostFactory = new WindowsDaemonHostFactory(initParams, _paths);
    }

    public IAppDesktopPaths Paths => _paths;
    public IAppInstanceController Instance { get; }
    public IDaemonChannel Channel { get; }
    public ILoopbackPeerCheck PeerCheck { get; } = new WindowsLoopbackPeerCheck();
    public IAppInstanceSetup InstanceSetup { get; }
    public IAppDaemonHostFactory DaemonHostFactory { get; }
    public bool IsTraySupported => true;

    public bool IsAdministrator() => WindowsAdministrators.IsCurrentUser();

    public IDisposable CreateTray(DesktopTrayParams trayParams) =>
        WindowsAppTray.Start(trayParams.Api, trayParams.UiAssets, trayParams.ShowWindow, trayParams.Exit);

    public ILoggerProvider CreateConsoleLoggerProvider() => new ConsoleLoggerProvider();

    public ILoggerProvider CreateSystemLogLoggerProvider() => new WinEventLogLoggerProvider(_paths.InstanceName);

    public Task<int> HostDaemon(Func<CancellationToken, Task<int>> run, CancellationToken cancellationToken) =>
        WindowsDaemonService.Host(_paths.InstanceName, run, cancellationToken);
}
