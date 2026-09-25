using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Wpf;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.App.Windows;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.WebView.Windows;

// The SPA in WPF's window, drawn with what WpfWebViewUi read from the app before it: the name, the
// look out of the UI's store, whether it is a TV. It follows the connection over the API for its
// taskbar badge, and hides rather than closes where a tray keeps the UI.
// ReSharper disable once RedundantExtendsListEntry
public partial class VpnHoodWpfMainWindow : Window
{
    // Android TV lays out at 960x540 dp (a 1920x1080 panel at xhdpi, the size its design guidance
    // targets), and a CSS px is a dp, so a web view of this size is the TV's own viewport.
    private const int TvPanelWidth = 960;
    private const int TvPanelHeight = 540;
    private static readonly TimeSpan StatePollInterval = TimeSpan.FromSeconds(1);

    private readonly WpfWindowParams _params;
    private readonly WebViewHost _webViewHost;
    private readonly CancellationTokenSource _cancellation = new();
    private bool _isWebViewUnavailable;

    internal VpnHoodWpfMainWindow(WpfWindowParams windowParams)
    {
        InitializeComponent();
        _params = windowParams;
        var resources = windowParams.Resources;
        var backgroundColor = resources.Colors.WindowBackgroundColor;

        Title = windowParams.AppName;
        if (backgroundColor != null)
            Background = new SolidColorBrush(Color.FromArgb(backgroundColor.Value.A, backgroundColor.Value.R,
                backgroundColor.Value.G, backgroundColor.Value.B));

        // On the TV UI the window is the panel: the web view takes the TV's viewport and the
        // window wraps it, so the layout is judged here at the TV's shape and measure. Everything
        // else keeps the phone-shaped window from the resources.
        if (windowParams.IsTv) {
            SizeToContent = SizeToContent.WidthAndHeight;
            MainWebView.Width = TvPanelWidth;
            MainWebView.Height = TvPanelHeight;
        }
        else {
            Width = resources.WindowSize.Width;
            Height = resources.WindowSize.Height;
        }
        ResizeMode = ResizeMode.CanMinimize;
        StateChanged += (_, _) => {
            if (WindowState == WindowState.Minimized && !windowParams.ExitOnClose) Hide();
        };

        var hWnd = new WindowInteropHelper(this).EnsureHandle();
        if (backgroundColor != null) WindowsShell.SetTitleBarColor(hWnd, backgroundColor.Value);

        // The web view's profile is the person's own (DesktopUiParams.UiDataPath). On the TV UI the
        // arrow keys move focus: Chromium's spatial navigation, which Android's WebView turns on by
        // itself for a device without a touchscreen. With it on here too, the TV layout can be walked
        // with a keyboard exactly as a D-pad walks it on the TV.
        MainWebView.CreationProperties = new CoreWebView2CreationProperties {
            UserDataFolder = windowParams.WebViewDataPath,
            AdditionalBrowserArguments = windowParams.IsTv ? "--enable-spatial-navigation" : null
        };

        // The SPA through the shared WebViewHost (the launch URL, reload on failure), from the web
        // host it is handed: the service's address, or the app's own host in this process.
        var spaWebView = new WpfWebView(MainWebView, OnWebView2Unavailable);
        _webViewHost = new WebViewHost(spaWebView, windowParams.WebHost);
        _webViewHost.Start();

        // Forward resume to the app (installed-apps cache and the like); no server check on desktop.
        // The keyboard belongs to the web view, which is this window's whole content: WPF keeps
        // keyboard focus on the Window itself and moves it into a child only on a click or a Tab,
        // so until then the document is not focused - the page's own initial focus (Connect on the
        // TV UI) draws no ring and the arrows do nothing, which on a TV reads as an app that
        // ignores the remote until Tab is pressed (owner, 2026-09-13). Measured in a WPF copy of
        // this window: document.hasFocus() is false while the window is active and true right
        // after this call. On every activation, so the keyboard comes back after an alt-tab too.
        Activated += (_, _) => {
            _webViewHost.OnResume();
            MainWebView.Focus();
        };

        // the window's work ends with WPF's loop, which a hidden window outlives without closing
        Dispatcher.ShutdownStarted += (_, _) => _cancellation.Cancel();
        _ = FollowState(_cancellation.Token);
    }

    // Shown, or brought forward; where WebView2 is missing, the SPA in the system browser instead.
    public void ShowOrOpen()
    {
        if (_isWebViewUnavailable) {
            WindowsShell.OpenUrl(_params.WebHost.Urls[0]);
            return;
        }

        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void OnWebView2Unavailable()
    {
        // Edge WebView2 runtime missing (or the SPA host gave up): hide the window and open the SPA in
        // the system browser instead. Invoked on the UI thread from WpfWebView, maybe more than once.
        if (_isWebViewUnavailable)
            return;

        _isWebViewUnavailable = true;
        Visibility = Visibility.Hidden; // Hide() does not work properly in this state on sandbox
        if (!_params.StartHidden)
            WindowsShell.OpenUrl(_params.WebHost.Urls[0]);
    }

    // The taskbar badge, from the app's state: the service's over loopback, or this process's own.
    private async Task FollowState(CancellationToken cancellationToken)
    {
        AppConnectionState? shown = null;
        while (!cancellationToken.IsCancellationRequested) {
            try {
                var state = await _params.Api.App.GetState(cancellationToken).Vhc();
                if (state.ConnectionState != shown) {
                    shown = state.ConnectionState;
                    await Dispatcher.InvokeAsync(() => UpdateBadge(state.ConnectionState));
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested) {
                VhLogger.Instance.LogDebug(ex, "The window could not read the app's state.");
            }

            await Task.Delay(StatePollInterval, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private void UpdateBadge(AppConnectionState connectionState)
    {
        var icon = connectionState switch {
            AppConnectionState.Connected => _params.Resources.Icons.BadgeConnectedIconData,
            AppConnectionState.None => null,
            _ => _params.Resources.Icons.BadgeConnectingIconData
        };

        if (icon == null) {
            TaskbarItemInfo.Overlay = null;
            return;
        }

        using var memStream = new MemoryStream(icon.Value.ToArray());
        var bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.StreamSource = memStream;
        bitmapImage.EndInit();
        TaskbarItemInfo.Overlay = bitmapImage;
    }

    // Where a tray keeps the UI, closing only hides the window.
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_params.ExitOnClose)
            return;

        e.Cancel = true;
        Hide();
    }

    protected override void OnClosed(EventArgs e)
    {
        _cancellation.Cancel();
        _webViewHost.Dispose();
        base.OnClosed(e);
    }
}
