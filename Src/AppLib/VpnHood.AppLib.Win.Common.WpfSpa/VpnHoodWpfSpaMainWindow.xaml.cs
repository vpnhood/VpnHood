using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using VpnHood.AppLib.SpaWebView;
using VpnHood.AppLib.WebServer;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Win.Common.WpfSpa;

// ReSharper disable once RedundantExtendsListEntry
public partial class VpnHoodWpfSpaMainWindow : Window
{
    private readonly SpaWebViewHost _host;

    public VpnHoodWpfSpaMainWindow()
    {
        InitializeComponent();
        var backgroundColor = VpnHoodApp.Instance.Resources.Colors.WindowBackgroundColor;

        // initialize main window
        Title = VpnHoodApp.Instance.Resources.Strings.AppName;
        if (backgroundColor != null)
            Background = new SolidColorBrush(Color.FromArgb(backgroundColor.Value.A, backgroundColor.Value.R,
                backgroundColor.Value.G, backgroundColor.Value.B));
        Visibility = VpnHoodAppWin.Instance.ShowWindowAfterStart ? Visibility.Visible : Visibility.Hidden;
        Width = VpnHoodApp.Instance.Resources.WindowSize.Width;
        Height = VpnHoodApp.Instance.Resources.WindowSize.Height;
        ResizeMode = ResizeMode.CanMinimize;
        StateChanged += (_, _) => {
            if (WindowState == WindowState.Minimized) Hide();
        };

        // set window title bar color
        var hWnd = new WindowInteropHelper(this).EnsureHandle();
        if (backgroundColor != null) VpnHoodAppWin.SetWindowTitleBarColor(hWnd, backgroundColor.Value);

        // initialize MainWebView user-data folder (the WebView2 mechanics live in WpfSpaWebView)
        MainWebView.CreationProperties = new CoreWebView2CreationProperties
            { UserDataFolder = Path.Combine(VpnHoodApp.Instance.StorageFolderPath, "Temp") };

        // initialize tray icon
        UpdateIcon();
        VpnHoodApp.Instance.ConnectionStateChanged += (_, _) =>
            VhUtils.TryInvoke("UpdatingSystemIcon", () => Dispatcher.Invoke(UpdateIcon));

        AppUiContext.Context = new WinUiContext(this);

        // Host the SPA via the shared SpaWebViewHost (server lifecycle, launch URL, self-heal reload).
        var spaWebView = new WpfSpaWebView(MainWebView, OnWebView2Unavailable);
        _host = new SpaWebViewHost(spaWebView);
        _host.Start();

        // Signal resume so the web server self-heals a torn-down listener and reloads if it restarted.
        Activated += (_, _) => _host.OnResume();
    }

    private void OnWebView2Unavailable()
    {
        // Edge WebView2 runtime missing (or the SPA host gave up): hide the window and open the SPA in
        // the system browser instead. Invoked on the UI thread from WpfSpaWebView.
        lock (MainWebView) {
            // This can be signalled more than once.
            if (!VpnHoodAppWin.Instance.EnableOpenMainWindow)
                return;

            Visibility = Visibility.Hidden; // Hide() does not work properly in this state on sandbox
            VpnHoodAppWin.Instance.EnableOpenMainWindow = false;
            if (VpnHoodAppWin.Instance.ShowWindowAfterStart)
                VpnHoodAppWin.OpenUrlInExternalBrowser(VpnHoodAppWebServer.Instance.Url);
        }
    }

    private void UpdateIcon()
    {
        // update icon and text
        var icon = VpnHoodApp.Instance.State.ConnectionState switch {
            AppConnectionState.Connected => VpnHoodApp.Instance.Resources.Icons.BadgeConnectedIcon,
            AppConnectionState.None => null,
            _ => VpnHoodApp.Instance.Resources.Icons.BadgeConnectingIcon
        };

        // remove overlay
        if (icon == null) {
            TaskbarItemInfo.Overlay = null;
            return;
        }

        // set overlay
        using var memStream = new MemoryStream(icon.Data);
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