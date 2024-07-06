using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using VpnHood.Client.App.WebServer;
using VpnHood.Client.App.Win.Common;
using VpnHood.Client.Device;
using VpnHood.Client.Device.WinDivert;

namespace VpnHood.Client.App.Win;

// ReSharper disable once RedundantExtendsListEntry
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var backgroundColor = VpnHoodApp.Instance.Resource.Colors.WindowBackgroundColor;

        // initialize main window
        Title = VpnHoodApp.Instance.Resource.Strings.AppName;
        if (backgroundColor != null) Background = new SolidColorBrush(Color.FromArgb(backgroundColor.Value.A, backgroundColor.Value.R, backgroundColor.Value.G, backgroundColor.Value.B));
        Visibility = VpnHoodWinApp.Instance.ShowWindowAfterStart ? Visibility.Visible : Visibility.Hidden;
        Width = VpnHoodApp.Instance.Resource.WindowSize.Width;
        Height = VpnHoodApp.Instance.Resource.WindowSize.Height;
        ResizeMode = ResizeMode.CanMinimize;
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) Hide(); };

        // set window title bar color
        var hWnd = new WindowInteropHelper(this).EnsureHandle();
        if (backgroundColor != null) VpnHoodWinApp.SetWindowTitleBarColor(hWnd, backgroundColor.Value);

        // initialize MainWebView
        MainWebView.CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = Path.Combine(VpnHoodApp.Instance.StorageFolderPath, "Temp") };
        MainWebView.CoreWebView2InitializationCompleted += MainWebView_CoreWebView2InitializationCompleted;
        MainWebView.Source = VpnHoodAppWebServer.Instance.Url;
        if (backgroundColor != null) MainWebView.DefaultBackgroundColor = backgroundColor.Value;
        _ = MainWebView.EnsureCoreWebView2Async(null);

        // initialize tray icon
        VpnHoodApp.Instance.ConnectionStateChanged += (_, _) => Dispatcher.Invoke(UpdateIcon);
        ActiveUiContext.Context = new WinUiContext();
    }


    private static void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        VpnHoodWinApp.OpenUrlInExternalBrowser(new Uri(e.Uri));
        e.Handled = true;
    }

    private void MainWebView_CoreWebView2InitializationCompleted(object? sender, CoreWebView2InitializationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            MainWebView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
            return;
        }

        // hide window if edge is not installed and open browser instead
        lock (MainWebView)
        {
            // MainWebView_CoreWebView2InitializationCompleted is called many times
            if (VpnHoodWinApp.Instance.EnableOpenMainWindow)
            {
                Visibility = Visibility.Hidden; // Hide() does not work properly in this state on sandbox
                VpnHoodWinApp.Instance.EnableOpenMainWindow = false;
                if (VpnHoodWinApp.Instance.ShowWindowAfterStart)
                    VpnHoodWinApp.OpenUrlInExternalBrowser(VpnHoodAppWebServer.Instance.Url);
            }
        }
    }

    private void UpdateIcon()
    {
        // update icon and text
        var icon = VpnHoodApp.Instance.State.ConnectionState switch
        {
            AppConnectionState.Connected => VpnHoodApp.Instance.Resource.Icons.BadgeConnectedIcon,
            AppConnectionState.None => null,
            _ => VpnHoodApp.Instance.Resource.Icons.BadgeConnectingIcon
        };

        // remove overlay
        if (icon == null)
        {
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
