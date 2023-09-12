using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using VpnHood.Client.App.Resources;
using VpnHood.Client.App.UI;

namespace VpnHood.Client.App.Win;

// ReSharper disable once RedundantExtendsListEntry
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // initialize main window
        Background = new SolidColorBrush(Color.FromArgb(UiDefaults.WindowBackgroundColor.A, UiDefaults.WindowBackgroundColor.R, UiDefaults.WindowBackgroundColor.G, UiDefaults.WindowBackgroundColor.B));
        Visibility = WinApp.Instance.ShowWindowAfterStart ? Visibility.Visible : Visibility.Hidden;
        Width = UiDefaults.WindowSize.Width;
        Height = UiDefaults.WindowSize.Height;
        Title = UiResource.AppName;
        UpdateIcon();

        // initialize MainWebView
        MainWebView.DefaultBackgroundColor = UiDefaults.WindowBackgroundColor;
        MainWebView.CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = Path.Combine(VpnHoodApp.Instance.AppDataFolderPath, "Temp") };
        MainWebView.CoreWebView2InitializationCompleted += MainWebView_CoreWebView2InitializationCompleted;
        MainWebView.Source = VpnHoodAppUi.Instance.Url;
        _ = MainWebView.EnsureCoreWebView2Async(null);

        var hWnd = new WindowInteropHelper(this).EnsureHandle();
        WinApp.SetWindowTitleBarColor(hWnd, UiDefaults.WindowBackgroundColor);
        VpnHoodApp.Instance.ConnectionStateChanged += (_, _) => Dispatcher.Invoke(UpdateIcon);
    }

    private static void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        WinApp.OpenUrlInExternalBrowser(new Uri(e.Uri));
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
            if (WinApp.Instance.EnableOpenMainWindow)
            {
                Visibility = Visibility.Hidden; // Hide() does not work properly in this state on sandbox
                WinApp.Instance.EnableOpenMainWindow = false;
                if (WinApp.Instance.ShowWindowAfterStart)
                    WinApp.OpenUrlInExternalBrowser(VpnHoodAppUi.Instance.Url);
            }
        }
    }

    private void UpdateIcon()
    {
        // update icon and text
        var icon = VpnHoodApp.Instance.State.ConnectionState switch
        {
            AppConnectionState.Connected => UiResource.VpnConnectedIcon,
            AppConnectionState.None => UiResource.VpnDisconnectedIcon,
            _ => UiResource.VpnConnectingIcon
        };

        using var memStream = new MemoryStream(icon);
        var bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.StreamSource = memStream;
        bitmapImage.EndInit();
        Icon = bitmapImage;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}