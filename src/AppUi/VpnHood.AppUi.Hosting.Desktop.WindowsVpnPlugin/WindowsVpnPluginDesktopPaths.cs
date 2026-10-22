using VpnHood.AppUi.Hosting.Desktop.Platform;
using VpnHood.Core.Client.Devices.WindowsVpnPlugin;

namespace VpnHood.AppUi.Hosting.Desktop.WindowsVpnPlugin;

// Where the window keeps things in a package: LocalState, beside the app's storage, as a debugger's run
// keeps them beside its app's. The instance is named by the app id rather than the executable, whose
// name the website's build has too: a second launch must find this edition's window, not that one's.
internal sealed class WindowsVpnPluginDesktopPaths(string appId) : IDesktopUiPaths
{
    public string InstanceName => appId;
    public string UiDataPath => WindowsVpnPluginFolders.LocalFolder;
}
