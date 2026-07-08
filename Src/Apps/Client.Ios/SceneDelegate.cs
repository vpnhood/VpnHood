using VpnHood.AppLib.Ios.Common.SpaWebView;

namespace VpnHood.App.Client.Ios;

[Register("SceneDelegate")]
public class SceneDelegate : UIResponder, IUIWindowSceneDelegate
{
    [Export("window")]
    public UIWindow? Window { get; set; }

    [Export("scene:willConnectToSession:options:")]
    public void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        if (scene is not UIWindowScene windowScene)
            return;

        var window = new UIWindow(windowScene) {
            // Host the VpnHood SPA (the same web UI used by the Android client) in a WKWebView.
            // The controller starts the in-process web server and loads it.
            RootViewController = new IosSpaWebViewController()
        };
        window.MakeKeyAndVisible();
        Window = window;
    }
}
