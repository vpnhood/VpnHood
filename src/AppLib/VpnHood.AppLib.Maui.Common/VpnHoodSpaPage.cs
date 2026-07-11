using VpnHood.AppLib.SpaWebView;

namespace VpnHood.AppLib.Maui.Common;

// Reusable MAUI page that hosts the VpnHood SPA via the shared SpaWebViewHost. A MAUI app can use it
// as its MainPage; all hosting business logic is shared, and only MauiSpaWebView is MAUI-specific.
//
// NOTE: MAUI has no reliable per-page resume callback, so foreground recovery is driven primarily by
// the web server's 1s health monitor. When this page's Window is available it also hooks
// Window.Resumed for a prompt re-check; OnAppearing is used as a fallback resume signal.
public class VpnHoodSpaPage : ContentPage
{
    private readonly SpaWebViewHost _host;
    private Window? _hookedWindow;
    private bool _started;

    public VpnHoodSpaPage()
    {
        var webView = new WebView {
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

        var adapter = new MauiSpaWebView(webView, Dispatcher, spinner, errorLabel);
        _host = new SpaWebViewHost(adapter);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (!_started) {
            _started = true;
            _host.Start();
        }
        else {
            // Returned to this page — re-check the server (also covered by the health monitor).
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
