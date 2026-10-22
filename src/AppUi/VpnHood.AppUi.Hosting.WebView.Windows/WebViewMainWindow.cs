using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Api.App;
using VpnHood.AppUi.Hosting.WebView.Windows.WinNative;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.WebView.Windows;

// The SPA in a window of its own, drawn with what WindowsWebViewUi read from the app before it: the
// name, the look out of the UI's store, whether it is a TV. It follows the connection over the API
// for its taskbar badge, fits its screen again when that changes, and ends its thread's message loop
// when it is gone.
internal sealed class WebViewMainWindow
{
    private const uint WsCaption = 0x00C00000;
    private const uint WsSysMenu = 0x00080000;
    private const uint WsMinimizeBox = 0x00020000;
    private const uint WsClipChildren = 0x02000000;

    // a title bar, its menu and Minimize: neither resizing nor maximizing, the window's size being its
    // screen's fit (DesktopWindowFit)
    private const uint WindowStyle = WsCaption | WsSysMenu | WsMinimizeBox | WsClipChildren;
    private const uint WmDestroy = 0x0002;
    private const uint WmSize = 0x0005;
    private const uint WmActivate = 0x0006;
    private const uint WmSettingChange = 0x001A;
    private const uint WmDisplayChange = 0x007E;
    private const int SizeRestored = 0;
    private const int SpiSetWorkArea = 0x002F;
    private static readonly TimeSpan StatePollInterval = TimeSpan.FromSeconds(1);

    private readonly WebViewWindowParams _params;
    private readonly WebView2Window _window;
    private readonly WebViewHost _webViewHost;
    private readonly TaskbarOverlay _taskbarOverlay;
    private readonly IntPtr _connectedIcon;
    private readonly IntPtr _connectingIcon;
    private readonly CancellationTokenSource _cancellation = new();
    private bool _isWebViewUnavailable;
    private bool _isFitPending;

    // On the thread whose loop will run it (Win32Window.RunMessageLoop).
    public WebViewMainWindow(WebViewWindowParams windowParams)
    {
        _params = windowParams;
        var backgroundColor = windowParams.Colors.WindowBackgroundColor;

        // The TV's viewport, or the phone's shape where the screen has room for it, from the frame
        // this style gives the window.
        _window = new WebView2Window(windowParams.AppName, WindowStyle, owner: IntPtr.Zero, backgroundColor, OnMessage);
        Win32WindowFit.Apply(_window.Window, windowParams.WindowFit, windowParams.IsTv);
        if (backgroundColor != null)
            WindowsShell.SetTitleBarColor(_window.Window.Handle, backgroundColor.Value);

        _taskbarOverlay = new TaskbarOverlay(_window.Window.Handle);
        _connectedIcon = LoadIcon("BadgeConnected.ico");
        _connectingIcon = LoadIcon("BadgeConnecting.ico");

        // The SPA through the shared WebViewHost (the launch URL, reload on failure), at the address
        // the host hands the window: the service's, or in dev the app's own in this process.
        var webView = new WindowsWebView(_window, windowParams.WebViewDataPath, windowParams.IsTv, OnWebView2Unavailable);
        _webViewHost = new WebViewHost(webView, new DesktopWebViewPageHost(windowParams.ApiUrlProvider));
        _webViewHost.Start();
        _ = FollowState(_cancellation.Token);
    }

    public Win32Window Window => _window.Window;

    // Shown, or brought forward; where WebView2 is missing, the SPA in the system browser instead.
    public void ShowOrOpen()
    {
        if (_isWebViewUnavailable) {
            WindowsShell.OpenUrl(_params.ApiUrlProvider.Current);
            return;
        }

        _window.Window.BringToFront();
    }

    // On the window's thread: gone, and its loop with it.
    public void Close()
    {
        _window.Window.Destroy();
    }

    private IntPtr? OnMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg) {
            case WmSize when wParam == SizeRestored && _isFitPending:
                Fit();
                break;

            // Forward resume to the app (installed-apps cache and the like); no server check on desktop.
            case WmActivate when (wParam & 0xFFFF) != 0:
                _webViewHost.OnResume();
                break;

            case WmDestroy:
                _cancellation.Cancel();
                _webViewHost.Dispose();
                WinIcon.DestroyIcon(_connectedIcon);
                WinIcon.DestroyIcon(_connectingIcon);
                Win32Window.Quit();
                break;

            // a new resolution, and the taskbar's new place after it
            case WmDisplayChange:
            case WmSettingChange when wParam == SpiSetWorkArea:
                _window.Window.Post(Fit);
                break;
        }

        if (msg == TaskbarOverlay.ButtonCreatedMessage)
            _taskbarOverlay.OnButtonCreated();

        return null;
    }

    // a minimized window is fitted once restored
    private void Fit()
    {
        _isFitPending = _window.Window.IsMinimized;
        if (!_isFitPending)
            Win32WindowFit.Apply(_window.Window, _params.WindowFit, _params.IsTv);
    }

    private void OnWebView2Unavailable()
    {
        // Edge WebView2 runtime missing (or the SPA host gave up): hide the window and open the SPA in
        // the system browser instead. Invoked on the window's thread, maybe more than once.
        if (_isWebViewUnavailable)
            return;

        _isWebViewUnavailable = true;
        _window.Window.Hide();
        WindowsShell.OpenUrl(_params.ApiUrlProvider.Current);
    }

    // The taskbar badge, from the app's state: the service's over loopback, or this process's own.
    private async Task FollowState(CancellationToken cancellationToken)
    {
        AppConnectionState? shown = null;
        while (!cancellationToken.IsCancellationRequested) {
            try {
                var state = await _params.Api.App.GetState(cancellationToken).ConfigureAwait(false);
                if (state.ConnectionState != shown) {
                    shown = state.ConnectionState;
                    _window.Window.Post(() => _taskbarOverlay.SetIcon(BadgeOf(state.ConnectionState)));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            }
            catch (Exception ex) {
                VhLogger.Instance.LogDebug(ex, "The window could not read the app's state.");
            }

            await Task.Delay(StatePollInterval, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private IntPtr BadgeOf(AppConnectionState connectionState)
    {
        return connectionState switch {
            AppConnectionState.Connected => _connectedIcon,
            AppConnectionState.None => IntPtr.Zero,
            _ => _connectingIcon
        };
    }

    // the badge's pictures, this library's own (Resources\*.ico, embedded by name)
    private static IntPtr LoadIcon(string name)
    {
        using var stream = typeof(WebViewMainWindow).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The badge icon is not embedded. Name: {name}");
        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        return WinIcon.LoadIconFromBytes(memoryStream.ToArray());
    }
}
