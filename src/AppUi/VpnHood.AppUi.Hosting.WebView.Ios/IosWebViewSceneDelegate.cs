namespace VpnHood.AppUi.Hosting.WebView.Ios;

// A window scene of the web view UI: its window, with the page's controller as the root, made by
// the head's application delegate (IosWebViewAppDelegate.CreateViewController).
public class IosWebViewSceneDelegate : UIWindowSceneDelegate
{
    public override UIWindow? Window { get; set; }

    public override void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
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
