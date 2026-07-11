using Android.Runtime;
using Android.Views;
using VpnHood.AppLib.Droid.Common.Activities;
using VpnHood.AppLib.Droid.Common.Utils;
using VpnHood.AppLib.SpaWebView;
using VpnHood.Core.Client.Devices.Droid.ActivityEvents;
using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppLib.Droid.Common.SpaWebView;

// Android host for the VpnHood SPA. All hosting business logic lives in the shared SpaWebViewHost;
// this handler only supplies the Android chrome (content-view timing, hardware back) and forwards the
// activity lifecycle to the host. The WebView mechanics live in AndroidSpaWebView.
public class AndroidSpaWebViewMainActivityHandler(
    IActivityEvent activityEvent,
    AndroidSpaWebViewMainActivityOptions options)
    : AndroidAppMainActivityHandler(activityEvent, options)
{
    private AndroidSpaWebView? _spaWebView;
    private SpaWebViewHost? _host;
    private AndroidBackInvokedCallback? _backInvokedCallback;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Experimental. Fixing: Window couldn't find content container view.
        // Some OEMs are subject to this issue, so postpone the content setup.
        ActivityEvent.Activity.Window?.DecorView.Post(() => {
            _spaWebView = new AndroidSpaWebView(ActivityEvent, options);
            _host = new SpaWebViewHost(_spaWebView);
            _host.Start();

            // Register back callback for Android 13+ (API 33+) with default priority (0).
            if (OperatingSystem.IsAndroidVersionAtLeast(33)) {
                _backInvokedCallback = new AndroidBackInvokedCallback(HandleBackInvoked);
                ActivityEvent.Activity.OnBackInvokedDispatcher.RegisterOnBackInvokedCallback(priority: 0,
                    _backInvokedCallback);
            }
        });
    }

    protected override bool OnKeyDown([GeneratedEnum] Keycode keyCode, KeyEvent? keyEvent)
    {
        // For Android versions prior to 13 (API 33), handle the back button via OnKeyDown.
        if (!OperatingSystem.IsAndroidVersionAtLeast(33) && keyCode == Keycode.Back && _spaWebView?.CanGoBack() == true) {
            _spaWebView.GoBack();
            return true;
        }

        return base.OnKeyDown(keyCode, keyEvent);
    }

    protected override void OnPause()
    {
        base.OnPause();

        if (!AppUiContext.IsPartialIntentRunning)
            _spaWebView?.OnActivityPause();
    }

    protected override void OnResume()
    {
        // Let the web server self-heal a listener that was torn down while the app was in the
        // background; if it had to restart, the host reloads the WebView.
        _host?.OnResume();
        _spaWebView?.OnActivityResume();
        base.OnResume();
    }

    protected override void OnDestroy()
    {
        // Unregister back callback if registered.
        if (_backInvokedCallback != null) {
            ActivityEvent.Activity.OnBackInvokedDispatcher.UnregisterOnBackInvokedCallback(_backInvokedCallback);
            _backInvokedCallback.Dispose();
            _backInvokedCallback = null;
        }

        _host?.Dispose();
        _host = null;

        base.OnDestroy();
    }

    private void HandleBackInvoked()
    {
        if (_spaWebView?.CanGoBack() == true) {
            _spaWebView.GoBack();
        }
        else {
            // Let the system handle the back action (minimize/close the app).
            ActivityEvent.Activity.Finish();
        }
    }
}
