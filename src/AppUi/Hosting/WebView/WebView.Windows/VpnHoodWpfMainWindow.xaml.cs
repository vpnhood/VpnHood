using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Wpf;
using VpnHood.AppLib.Api.App;
using VpnHood.AppUi.Hosting.WebView;
using VpnHood.AppLib.Api.WebHost;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.AppLib;
using VpnHood.AppLib.Win.Common;

namespace VpnHood.AppUi.Hosting.WebView.Windows;

// ReSharper disable once RedundantExtendsListEntry
public partial class VpnHoodWpfMainWindow : Window
{
    // Android TV lays out at 960x540 dp (a 1920x1080 panel at xhdpi, the size its design guidance
    // targets), and a CSS px is a dp, so a web view of this size is the TV's own viewport.
    private const int TvPanelWidth = 960;
    private const int TvPanelHeight = 540;

    public VpnHoodWpfMainWindow()
    {
        InitializeComponent();
        var backgroundColor = VpnHoodApp.Instance.Resources.Colors.WindowBackgroundColor;

        // initialize main window
        Title = VpnHoodApp.Instance.Features.AppName;
        if (backgroundColor != null)
            Background = new SolidColorBrush(Color.FromArgb(backgroundColor.Value.A, backgroundColor.Value.R,
                backgroundColor.Value.G, backgroundColor.Value.B));
        Visibility = VpnHoodAppWin.Instance.ShowWindowAfterStart ? Visibility.Visible : Visibility.Hidden;
        // On the TV UI the window is the panel: the web view takes the TV's viewport and the
        // window wraps it, so the layout is judged here at the TV's shape and measure. Everything
        // else keeps the phone-shaped window from the resources.
        if (VpnHoodApp.Instance.Features.IsTv) {
            SizeToContent = SizeToContent.WidthAndHeight;
            MainWebView.Width = TvPanelWidth;
            MainWebView.Height = TvPanelHeight;
        }
        else {
            Width = VpnHoodApp.Instance.Resources.WindowSize.Width;
            Height = VpnHoodApp.Instance.Resources.WindowSize.Height;
        }
        ResizeMode = ResizeMode.CanMinimize;
        StateChanged += (_, _) => {
            if (WindowState == WindowState.Minimized) Hide();
        };

        // set window title bar color
        var hWnd = new WindowInteropHelper(this).EnsureHandle();
        if (backgroundColor != null) VpnHoodAppWin.SetWindowTitleBarColor(hWnd, backgroundColor.Value);

        // initialize MainWebView user-data folder (the WebView2 mechanics live in WpfWebView).
        // On the TV UI the arrow keys move focus: Chromium's spatial navigation, which Android's
        // WebView turns on by itself for a device without a touchscreen. With it on here too, the
        // TV layout can be walked with a keyboard exactly as a D-pad walks it on the TV.
        MainWebView.CreationProperties = new CoreWebView2CreationProperties {
            UserDataFolder = Path.Combine(VpnHoodApp.Instance.StorageFolderPath, "Temp"),
            AdditionalBrowserArguments = VpnHoodApp.Instance.Features.IsTv ? "--enable-spatial-navigation" : null
        };

        // initialize tray icon
        UpdateIcon();
        VpnHoodApp.Instance.ConnectionStateChanged += (_, _) =>
            VhUtils.TryInvoke("UpdatingSystemIcon", () => Dispatcher.Invoke(UpdateIcon));

        AppUiContext.Context = new WinUiContext(this);

        // Host the SPA via the shared WebViewHost (server lifecycle, launch URL, reload on failure).
        // The Activated handler keeps the host alive for the window's lifetime.
        var spaWebView = new WpfWebView(MainWebView, OnWebView2Unavailable);
        var host = new WebViewHost(spaWebView);
        host.Start();

        // Forward resume to the app (installed-apps cache and the like); no server check on desktop.
        // The keyboard belongs to the web view, which is this window's whole content: WPF keeps
        // keyboard focus on the Window itself and moves it into a child only on a click or a Tab,
        // so until then the document is not focused - the page's own initial focus (Connect on the
        // TV UI) draws no ring and the arrows do nothing, which on a TV reads as an app that
        // ignores the remote until Tab is pressed (owner, 2026-09-13). Measured in a WPF copy of
        // this window: document.hasFocus() is false while the window is active and true right
        // after this call. On every activation, so the keyboard comes back after an alt-tab too.
        Activated += (_, _) => {
            host.OnResume();
            MainWebView.Focus();
        };
    }

    private void OnWebView2Unavailable()
    {
        // Edge WebView2 runtime missing (or the SPA host gave up): hide the window and open the SPA in
        // the system browser instead. Invoked on the UI thread from WpfWebView.
        lock (MainWebView) {
            // This can be signalled more than once.
            if (!VpnHoodAppWin.Instance.EnableOpenMainWindow)
                return;

            Visibility = Visibility.Hidden; // Hide() does not work properly in this state on sandbox
            VpnHoodAppWin.Instance.EnableOpenMainWindow = false;
            if (VpnHoodAppWin.Instance.ShowWindowAfterStart)
                VpnHoodAppWin.OpenUrlInExternalBrowser(VpnHoodAppWebHost.Instance.Url);
        }
    }

    private void UpdateIcon()
    {
        // update icon and text
        var icon = VpnHoodApp.Instance.State.ConnectionState switch {
            AppConnectionState.Connected => VpnHoodApp.Instance.Resources.Icons.BadgeConnectedIconData,
            AppConnectionState.None => null,
            _ => VpnHoodApp.Instance.Resources.Icons.BadgeConnectingIconData
        };

        // remove overlay
        if (icon == null) {
            TaskbarItemInfo.Overlay = null;
            return;
        }

        // set overlay
        using var memStream = new MemoryStream(icon.Value.ToArray());
        var bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.StreamSource = memStream;
        bitmapImage.EndInit();
        TaskbarItemInfo.Overlay = bitmapImage;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}