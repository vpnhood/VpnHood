using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.App.Windows;
using VpnHood.Net.Toolkit.Graphics;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.WebView.Windows;

// Windows' IWebView adapter, WebView2 in a window of its own (WebView2Window): the only
// WebView2-specific SPA-hosting code. When the Edge WebView2 runtime is unavailable it invokes the
// window-provided fallback (open the SPA in the system browser).
internal sealed class WindowsWebView(WebView2Window window, string userDataPath, bool isTv,
    Action onWebView2Unavailable) : IWebView
{
    private const uint WsOverlappedWindow = 0x00CF0000;
    private const uint WsClipChildren = 0x02000000;

    // a loopback link's window: resizable, the size WPF's had
    private const uint PageWindowStyle = WsOverlappedWindow | WsClipChildren;
    private static readonly VhSize PageWindowSize = new(900, 700);

    private CoreWebView2Environment? _environment;
    private CoreWebView2? _coreWebView;
    private Uri? _pendingUrl;
    private string? _localToken;

    public event EventHandler? PageLoaded;
    public event EventHandler? LoadFailed;
    public event EventHandler? ContentProcessGone;

    public void Initialize()
    {
        _ = InitializeAsync();
    }

    // On the window's thread throughout (Win32SynchronizationContext): WebView2's objects are that
    // thread's.
    private async Task InitializeAsync()
    {
        try {
            // The web view's profile is the person's own (DesktopUiParams.UiDataPath). On the TV UI the
            // arrow keys move focus: Chromium's spatial navigation, which Android's WebView turns on by
            // itself for a device without a touchscreen. With it on here too, the TV layout can be
            // walked with a keyboard exactly as a D-pad walks it on the TV.
            var options = new CoreWebView2EnvironmentOptions(isTv ? "--enable-spatial-navigation" : null);
            var environment = await CoreWebView2Environment.CreateAsync(null, userDataPath, options);
            var controller = await window.CreateController(environment);
            var coreWebView = controller.CoreWebView2;
            coreWebView.NewWindowRequested += OnNewWindowRequested;
            coreWebView.NavigationCompleted += OnNavigationCompleted;
            coreWebView.ProcessFailed += OnProcessFailed;
            _environment = environment;
            _coreWebView = coreWebView;

            if (_pendingUrl != null) {
                coreWebView.Navigate(_pendingUrl.ToString());
                _pendingUrl = null;
            }
        }
        catch (Exception ex) {
            // Edge WebView2 runtime missing / failed to initialize — fall back to the external browser.
            VhLogger.Instance.LogError(ex, "WebView2 initialization failed.");
            onWebView2Unavailable();
        }
    }

    // A loopback link - the log - asks for the token, which no browser can send, so it opens in a
    // window of its own, loaded with the header. Any other link goes to the system browser.
    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        var uri = new Uri(e.Uri);
        if (uri.IsLoopback)
            _ = ShowLoopbackWindow(uri);
        else
            WindowsShell.OpenUrl(uri);
    }

    private async Task ShowLoopbackWindow(Uri uri)
    {
        try {
            // the page's own environment, so this window keeps to the same user-data folder
            var environment = _environment ?? throw new InvalidOperationException("WebView2 has not started.");
            var page = new WebView2Window(Path.GetFileName(uri.LocalPath), PageWindowStyle, owner: window.Window.Handle,
                background: null, onMessage: null);
            var scale = page.Window.Scale;
            page.Window.SetSize((int)Math.Round(PageWindowSize.Width * scale), (int)Math.Round(PageWindowSize.Height * scale));
            page.Window.Show();

            var controller = await page.CreateController(environment);
            var headers = _localToken != null ? $"Authorization: Bearer {_localToken}" : "";
            controller.CoreWebView2.NavigateWithWebResourceRequest(
                environment.CreateWebResourceRequest(uri.ToString(), "GET", null, headers));
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not open a loopback link in a window of its own.");
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess) {
            PageLoaded?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Only connection-level failures mean the loopback server is unreachable. OperationCanceled
        // is our own superseding Navigate (the WebView2 twin of iOS NSUrlError.Cancelled) and must
        // not trigger a reload, or the reload would keep cancelling itself.
        VhLogger.Instance.LogWarning("WebView2 navigation failed: {Status}", e.WebErrorStatus);
        if (e.WebErrorStatus is CoreWebView2WebErrorStatus.ServerUnreachable
            or CoreWebView2WebErrorStatus.Timeout
            or CoreWebView2WebErrorStatus.ConnectionAborted
            or CoreWebView2WebErrorStatus.ConnectionReset
            or CoreWebView2WebErrorStatus.Disconnected
            or CoreWebView2WebErrorStatus.CannotConnect
            or CoreWebView2WebErrorStatus.HostNameNotResolved)
            LoadFailed?.Invoke(this, EventArgs.Empty);
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        VhLogger.Instance.LogWarning("WebView2 process failed: {Kind}", e.ProcessFailedKind);
        ContentProcessGone?.Invoke(this, EventArgs.Empty);
    }

    public void Load(Uri url)
    {
        // the token the page reads after "#", which a loopback link it opens needs too
        _localToken = LocalApiToken.Read(url);
        if (_coreWebView != null)
            _coreWebView.Navigate(url.ToString());
        else
            _pendingUrl = url; // navigate once WebView2 has started
    }

    public void SetLoading(bool isLoading)
    {
        // The window shows no separate loading indicator.
    }

    public void ShowError(string message)
    {
        VhLogger.Instance.LogError("SPA host error: {Message}", message);
        onWebView2Unavailable();
    }

    public void Post(Action action)
    {
        window.Window.Post(action);
    }
}
