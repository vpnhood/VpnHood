using Android.Runtime;
using Android.Views;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.App;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.AppLib.App.Android.Activities;
using VpnHood.AppUi.Hosting.WebView;
using VpnHood.Core.Client.Devices.Android.ActivityEvents;
using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppUi.Hosting.WebView.Android;

// Android host for the VpnHood SPA. All hosting business logic lives in the shared WebViewHost;
// this handler only supplies the Android chrome (content-view timing, hardware back) and forwards the
// activity lifecycle to the host. The WebView mechanics live in AndroidWebView.
public class AndroidWebViewMainActivityHandler(
    IActivityEvent activityEvent,
    AndroidWebViewMainActivityOptions options)
    : AndroidAppMainActivityHandler(activityEvent, options)
{
    private AndroidWebView? _spaWebView;
    private WebViewHost? _host;
    private AndroidBackInvokedCallback? _backInvokedCallback;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _ = CreateContentWhenReady();
    }

    // The loading screen and the web view draw with the app's look, so they are made once it is
    // final - nothing shows before, where a colour that changed under the user would be seen. The
    // wait comes back to the main looper, where views are made.
    private async Task CreateContentWhenReady()
    {
        try {
            await VpnHoodApp.Instance.ResourcesLoaded;
            if (ActivityEvent.Activity.IsDestroyed)
                return; // gone while the look was read

            CreateContent();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not create the app's web view.");
        }
    }

    private void CreateContent()
    {
        // Experimental. Fixing: Window couldn't find content container view.
        // Some OEMs are subject to this issue, so postpone the content setup.
        ActivityEvent.Activity.Window?.DecorView.Post(() => {
            _spaWebView = new AndroidWebView(ActivityEvent, options);
            _host = new WebViewHost(_spaWebView);
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
