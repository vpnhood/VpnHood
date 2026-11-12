using Foundation;

namespace VpnHood.AppUi.Hosting.WebView.Ios;

/// <summary>
/// Represents a window scene of the web view UI: its window, with the page's controller as its root, which the head's
/// application delegate makes.
/// </summary>
/// <remarks>
/// It is a responder that implements the scene delegate's protocol and exports its members, as UIKit refuses a scene
/// delegate class that does not declare the protocol.
/// </remarks>
public class IosWebViewSceneDelegate : UIResponder, IUIWindowSceneDelegate
{
    /// <summary>Gets or sets the scene's window.</summary>
    [Export("window")]
    public UIWindow? Window { get; set; }

    /// <summary>Makes the scene's window, with the page's controller as its root, and shows it.</summary>
    /// <param name="scene">The scene; only a window scene gets a window.</param>
    /// <param name="session">The scene's session.</param>
    /// <param name="connectionOptions">The connection's options.</param>
    /// <exception cref="InvalidOperationException">
    /// The application delegate does not derive from <see cref="IosWebViewAppDelegate"/>.
    /// </exception>
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
