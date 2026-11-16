using VpnHood.AppHosting.Desktop.Platform;
using VpnHood.Core.Client.Devices.WindowsVpnPlugin;

namespace VpnHood.AppHosting.Desktop.WindowsVpnPlugin;

// Where the window keeps things in a package: LocalState, beside the app's storage, as a debugger's run
// keeps them beside its app's. The instance is named by the app id rather than the executable, whose
// name the website's build has too.
internal sealed class WindowsVpnPluginDesktopPaths(string appId) : IDesktopUiPaths
{
    public string AppId => appId;
    public string InstanceName => appId;
    public string UiDataPath => WindowsVpnPluginFolders.LocalFolder;
}
