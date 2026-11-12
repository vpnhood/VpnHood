using VpnHood.AppLib.App.Android.Activities;

namespace VpnHood.AppUi.Hosting.WebView.Android;

/// <summary>
/// Represents the web view UI's main activity on Android: a head derives from it, names the access keys it takes, and
/// gets the web UI.
/// </summary>
/// <remarks>
/// The app must have started with its local web host, which needs the app's web content, as the page comes from it.
/// </remarks>
public class AndroidWebViewMainActivity : AndroidAppMainActivity
{
    /// <summary>
    /// Creates the activity's options: the access-key schemes and file types a head takes, and the system web view's
    /// version check. The default takes no access keys.
    /// </summary>
    /// <returns>The options.</returns>
    protected virtual AndroidWebViewMainActivityOptions CreateActivityOptions()
    {
        return new AndroidWebViewMainActivityOptions();
    }

    /// <summary>Creates the handler that does the activity's work: the web UI's, with the activity's options.</summary>
    /// <returns>The handler.</returns>
    protected override AndroidAppMainActivityHandler CreateMainActivityHandler()
    {
        return new AndroidWebViewMainActivityHandler(this, CreateActivityOptions());
    }
}
