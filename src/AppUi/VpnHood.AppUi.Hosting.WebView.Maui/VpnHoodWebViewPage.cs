using VpnHood.AppLib.App;
using VpnHood.AppUi.Hosting.WebView;

using NativeWebView = Microsoft.Maui.Controls.WebView;

namespace VpnHood.AppUi.Hosting.WebView.Maui;

// Reusable MAUI page that hosts the VpnHood SPA via the shared WebViewHost. A MAUI app can use it
// as its MainPage; all hosting business logic is shared, and only MauiWebView is MAUI-specific.
//
// NOTE: MAUI has no reliable per-page resume callback. When this page's Window is available it hooks
// Window.Resumed to forward resume to the app; OnAppearing is used as a fallback resume signal.
public class VpnHoodWebViewPage : ContentPage
{
    private readonly WebViewHost _host;
    private Window? _hookedWindow;
    private bool _started;

    public VpnHoodWebViewPage()
    {
        var webView = new NativeWebView {
            VerticalOptions = LayoutOptions.Fill,
            HorizontalOptions = LayoutOptions.Fill
        };
        var spinner = new ActivityIndicator {
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            IsRunning = true,
            IsVisible = true
        };
        var errorLabel = new Label {
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            IsVisible = false,
            Margin = new Thickness(24)
        };

        Content = new Grid { Children = { webView, spinner, errorLabel } };

        // the app's own web host: the platform started the app in this process (VpnHoodAppMaui)
        var adapter = new MauiWebView(webView, Dispatcher, spinner, errorLabel);
        var webHost = VpnHoodApp.Instance.LocalWebHost ??
                      throw new InvalidOperationException("This app was given no web host, so its SPA cannot be shown.");
        _host = new WebViewHost(adapter, webHost);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (!_started) {
            _started = true;
            _host.Start();
        }
        else {
            // Returned to this page — forward resume to the app.
            _host.OnResume();
        }

        // Hook the app-level resume once the Window is available.
        if (_hookedWindow == null && Window != null) {
            _hookedWindow = Window;
            _hookedWindow.Resumed += OnWindowResumed;
        }
    }

    private void OnWindowResumed(object? sender, EventArgs e)
    {
        _host.OnResume();
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        // Tear down when the page's handler is detached (page destroyed).
        if (Handler == null) {
            if (_hookedWindow != null) {
                _hookedWindow.Resumed -= OnWindowResumed;
                _hookedWindow = null;
            }

            _host.Dispose();
        }
    }
}
