using VpnHood.AppLib.App.Android.Activities;

namespace VpnHood.AppUi.Hosting.WebView.Android;

// The web view UI's activity, as AndroidAvaloniaMainActivity is the Avalonia UI's: a head derives
// from it, names the access keys it takes, and gets this UI. The work is the handler's
// (AndroidWebViewMainActivityHandler); this names the UI, and the app's own activity base does the
// rest.
public class AndroidWebViewMainActivity : AndroidAppMainActivity
{
    // The access-key schemes and mimes a head takes, and what this UI is told besides; none by default.
    protected virtual AndroidWebViewMainActivityOptions CreateActivityOptions()
    {
        return new AndroidWebViewMainActivityOptions();
    }

    protected override AndroidAppMainActivityHandler CreateMainActivityHandler()
    {
        return new AndroidWebViewMainActivityHandler(this, CreateActivityOptions());
    }
}
