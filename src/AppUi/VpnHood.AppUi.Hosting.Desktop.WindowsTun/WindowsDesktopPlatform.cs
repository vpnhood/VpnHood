using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.App.WindowsTun;
using VpnHood.AppUi.Hosting.Desktop.Platform;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Desktop.WindowsTun;

// The machine facts Windows answers. The app runs in a LocalSystem service, "daemon" under the
// service control manager, which "service install" registers and which stops it with a call; the
// window runs as whoever is signed in, not elevated, and reaches the service over loopback; the
// service's warnings and the window's go to the Application log under the service's name.
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

    public bool IsAdministrator() => WindowsAdministrators.IsCurrentUser();

    public ILoggerProvider CreateConsoleLoggerProvider() => new ConsoleLoggerProvider();

    public ILoggerProvider CreateSystemLogLoggerProvider() => new WinEventLogLoggerProvider(_paths.InstanceName);

    public Task<int> HostDaemon(DaemonRun run, CancellationToken cancellationToken) =>
        WindowsDaemonService.Host(_paths.InstanceName, run, cancellationToken);

    // The root's launcher, over "current". An update's setup closes the window before it switches the version, and
    // the new service opens it again (WindowsUiRelaunch); this is for a window that outlived one nonetheless.
    public void StartUiAgain(bool startHidden)
    {
        if (_paths.InstalledLauncherPath is not { } launcherPath)
            return;

        var startInfo = new ProcessStartInfo { FileName = launcherPath, UseShellExecute = false };
        startInfo.ArgumentList.Add("ui");
        if (startHidden)
            startInfo.ArgumentList.Add("--tray");
        Process.Start(startInfo)?.Dispose();
    }
}
