using ObjCRuntime;
using VpnHood.AppLib.App.Ios;

namespace VpnHood.AppUi.Hosting.WebView.Ios;

/// <summary>
/// Represents the web view UI's application delegate on iOS: a head's application delegate derives from it, says how
/// its app starts (<see cref="CreateInitParams"/>), and carries only what the system reads, its <c>[Register]</c> name.
/// </summary>
/// <remarks>
/// The app starts as the launch finishes; every window scene then gets <see cref="IosWebViewSceneDelegate"/>, which shows
/// the page in the controller this makes (<see cref="CreateViewController"/>), so a head's Info.plist names no scene
/// delegate, only the scene manifest's default configuration.
/// </remarks>
public abstract class IosWebViewAppDelegate : UIApplicationDelegate
{
    /// <summary>Creates the head's init params: the app's, and the two iOS facts the platform builds its device from.</summary>
    /// <returns>The init params.</returns>
    protected abstract IosInitParams CreateInitParams();

    /// <summary>Creates the page's controller for a scene that connects: portrait, unless the head allows rotation.</summary>
    /// <returns>The controller.</returns>
    protected internal virtual IosWebViewController CreateViewController()
    {
        return new IosWebViewController();
    }

    /// <inheritdoc/>
    /// <remarks>It starts the app. An ad provider that names countries both to include and to exclude fails the start.</remarks>
    /// <exception cref="OverflowException">A duration in a built-in access key is out of range.</exception>
    /// <exception cref="System.IO.InvalidDataException">A built-in access key decodes to JSON <c>null</c>.</exception>
    /// <exception cref="System.Text.Json.JsonException">A built-in access key holds no valid access key.</exception>
    /// <exception cref="NotSupportedException">A built-in access key's version is not one the app reads.</exception>
    /// <exception cref="FormatException">
    /// A built-in access key is no valid Base64, or an endpoint or a duration in it is not valid.
    /// </exception>
    /// <exception cref="ArgumentException">The app's options name an empty app name.</exception>
    /// <exception cref="ArgumentNullException">The app's options name no app name.</exception>
    /// <exception cref="InvalidOperationException">
    /// The init params name no storage folder, or an App Group without a container; the platform has no local app data
    /// folder; or the app's storage has no parent folder.
    /// </exception>
    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        VpnHoodIosApp.Init(CreateInitParams());
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>Each scene gets <see cref="IosWebViewSceneDelegate"/>.</remarks>
    public override UISceneConfiguration GetConfiguration(UIApplication application,
        UISceneSession connectingSceneSession, UISceneConnectionOptions options)
    {
        return new UISceneConfiguration("Default Configuration", connectingSceneSession.Role) {
            DelegateClass = new Class(typeof(IosWebViewSceneDelegate))
        };
    }
}
