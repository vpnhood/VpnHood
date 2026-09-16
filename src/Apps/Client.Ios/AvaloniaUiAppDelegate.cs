using Avalonia;
using Avalonia.iOS;
using Foundation;
using VpnHood.AppLib;
using VpnHood.AppLib.AvaloniaUI;
using VpnHood.AppLib.Ios.Common;
using VpnHood.AppLib.WebServer;
using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.App.Client.Ios;

// The Avalonia UI's delegate, named to UIKit by Main.cs when the app asks for that UI: Avalonia's
// own, which builds the UI as launching finishes and gives each scene its AvaloniaSceneDelegate
// in place of the SceneDelegate of Info.plist, which hosts the web view. The app is started
// first, exactly as AppDelegate starts it, at the one step of that launch this class is asked
// for - the app builder - and so is the web server, which a paired phone dials; then the UI is
// given the app's API (its own controllers in process, the same six interfaces a paired browser
// dials over HTTP). Its files are in the app bundle, where the build placed them.
[Register("AvaloniaUiAppDelegate")]
public class AvaloniaUiAppDelegate : AvaloniaAppDelegate<VpnHoodAvaloniaApp>
{
    protected override AppBuilder CreateAppBuilder()
    {
        AppDelegate.StartApp();
        if (!VpnHoodAppWebServer.IsInit)
            VpnHoodAppWebServer.Init(VpnHoodApp.Instance);
        // in process both complete at once
        AppData.Init(InProcessAppApi.Create(VpnHoodApp.Instance), CancellationToken.None).GetAwaiter().GetResult();
        AppData.Configure(CancellationToken.None).GetAwaiter().GetResult();
        AppUiContext.Context = new IosUiContext();
        return base.CreateAppBuilder();
    }
}
