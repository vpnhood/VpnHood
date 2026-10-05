using VpnHood.AppLib.App;
using VpnHood.AppLib.App.Windows;
using VpnHood.AppUi.Hosting.Desktop.Abstractions;

namespace VpnHood.AppUi.Hosting.Desktop.Windows;

// The app as the Windows service runs it, or a debugger does: VpnHoodWindowsApp, which takes the
// single-instance lock on the app id before anything is built - the service's where only
// administrators reach, a debugger's in the folder it is handed.
internal sealed class WindowsDaemonHost : IAppDaemonHost
{
    private readonly VpnHoodWindowsApp _windowsApp;

    public WindowsDaemonHost(AppInitParams initParams, string storagePath, string? lockFolderPath)
    {
        // AnotherInstanceIsRunningException passes through as it is: its message is the answer.
        _windowsApp = VpnHoodWindowsApp.Init(initParams, storagePath, lockFolderPath);
    }

    // The stop: the tunnel comes down, then the app.
    public ValueTask DisposeAsync()
    {
        return _windowsApp.DisposeAsync();
    }
}
