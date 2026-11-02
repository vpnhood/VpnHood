using Foundation;

namespace VpnHood.AppUi.Hosting.WebView.Ios;

// A window scene of the web view UI: its window, with the page's controller as the root, made by
// the head's application delegate (IosWebViewAppDelegate.CreateViewController). A responder that
// implements the protocol and exports its members, as Avalonia's scene delegate is: a subclass of
// UIKit's model class UIWindowSceneDelegate registers without declaring the protocol, and UIKit
// refuses it ("delegateClass must conform to UISceneDelegate protocol"), so no window ever opens.
public class IosWebViewSceneDelegate : UIResponder, IUIWindowSceneDelegate
{
    [Export("window")]
    public UIWindow? Window { get; set; }

    [Export("scene:willConnectToSession:options:")]
    public void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        if (scene is not UIWindowScene windowScene)
            return;

        var appDelegate = UIApplication.SharedApplication.Delegate as IosWebViewAppDelegate ??
                          throw new InvalidOperationException(
                              $"The application delegate must derive from {nameof(IosWebViewAppDelegate)}.");

        Window = new UIWindow(windowScene) { RootViewController = appDelegate.CreateViewController() };
        Window.MakeKeyAndVisible();
    }
}
