using VpnHood.AppLib.App;
using VpnHood.AppLib.App.Linux;
using VpnHood.AppUi.Hosting.Cli.Abstractions;

namespace VpnHood.AppUi.Hosting.Cli.Linux;

// The app as Linux hosts a headless one, or a debugger does: VpnHoodLinuxApp, which takes the
// single-instance lock on the app id before anything is built and clears what a previous run left.
internal sealed class LinuxDaemonHost : IAppDaemonHost
{
    private readonly VpnHoodLinuxApp _linuxApp;

    public LinuxDaemonHost(AppInitParams initParams, string storagePath, string lockFolderPath)
    {
        // AnotherInstanceIsRunningException passes through as it is: its message is the answer.
        _linuxApp = VpnHoodLinuxApp.Init(initParams, storagePath, lockFolderPath);
    }

    // The stop: the tunnel comes down, then the app.
    public ValueTask DisposeAsync()
    {
        return _linuxApp.DisposeAsync();
    }
}
