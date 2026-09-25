using ObjCRuntime;
using VpnHood.AppLib.App.Ios;

namespace VpnHood.AppUi.Hosting.WebView.Ios;

// The web view UI's application delegate on iOS, as AndroidWebViewApplication is its Application on
// Android: a head's AppDelegate derives from this, says how its app starts (CreateInitParams), and
// carries only what the OS reads - its [Register] name. The app starts as launching finishes; every
// window scene then gets IosWebViewSceneDelegate, which shows the page in the controller this makes
// (CreateViewController) - which is why a head's Info.plist names no scene delegate, only the
// scene manifest's default configuration.
public abstract class IosWebViewAppDelegate : UIApplicationDelegate
{
    // The head's init params: the app's, and the two iOS facts the platform builds its device from.
    protected abstract IosInitParams CreateInitParams();

    // The page's controller for a scene that connects: portrait, unless a head allows rotation.
    protected internal virtual IosWebViewController CreateViewController()
    {
        return new IosWebViewController();
    }

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        VpnHoodIosApp.Init(CreateInitParams());
        return true;
    }

    public override UISceneConfiguration GetConfiguration(UIApplication application,
        UISceneSession connectingSceneSession, UISceneConnectionOptions options)
    {
        return new UISceneConfiguration("Default Configuration", connectingSceneSession.Role) {
            DelegateClass = new Class(typeof(IosWebViewSceneDelegate))
        };
    }
}
