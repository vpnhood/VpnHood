using System.Diagnostics;
using System.Runtime.InteropServices;
using VpnHood.AppLib.Api.App;
using Windows.UI.Notifications;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.UI.Windowing;
using VpnHood.AppLib.App.Windows;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.AppLib.App;

// ReSharper disable once CheckNamespace
namespace VpnHood.AppUi.Hosting.WebView.Maui;

internal class VpnHoodAppMauiWindows : Singleton<VpnHoodAppMauiWindows>, IVpnHoodAppMaui
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    
    protected AppWindow? AppWindow;
    private readonly WindowsAppTray _tray;

    private VpnHoodAppMauiWindows(AppInitParams initParams)
    {
        // The platform starts the app, as under any other UI - in this process, under the
        // person's storage folder, until this becomes a window over the service (hosting plan,
        // step 10) - and it disconnects on the way out.
        VpnHoodWindowsApp.Init(initParams, initParams.ResolveStoragePath());

        // the tray over the app's own API here; its requests come from its own thread
        _tray = WindowsAppTray.Start(VpnHoodApp.Instance.Api, VpnHoodApp.Instance.UiAssetProvider,
            showWindow: _ => {
                MainThread.BeginInvokeOnMainThread(ShowMainWindow);
                return Task.CompletedTask;
            },
            exit: () => MainThread.BeginInvokeOnMainThread(Exit));

        // initialize VpnHoodApp
        VpnHoodApp.Instance.ConnectionStateChanged += ConnectionStateChanged;

        // customize main window
        WindowHandler.Mapper.AppendToMapping(nameof(IWindow), MappingMethod);
    }

    public static VpnHoodAppMauiWindows Init(AppInitParams initParams)
    {
        var app = new VpnHoodAppMauiWindows(initParams);
        app.UpdateIcon();
        return app;
    }

    private void MappingMethod(IWindowHandler handler, IWindow _)
    {
        AppWindow = handler.PlatformView.GetAppWindow();

        //customize WinUI main window
        if (AppWindow != null) {
            AppWindow.TitleBar.IconShowOptions = IconShowOptions.HideIconAndSystemMenu;
            AppWindow.Closing += AppWindow_Closing;

            var bgColorResource = VpnHoodApp.Instance.Resources.Colors.WindowBackgroundColor;
            if (bgColorResource != null) {
                var bgColor = Windows.UI.Color.FromArgb(bgColorResource.Value.A, bgColorResource.Value.R, bgColorResource.Value.G, bgColorResource.Value.B);
                AppWindow.TitleBar.ButtonBackgroundColor = bgColor;
                AppWindow.TitleBar.BackgroundColor = bgColor;
                AppWindow.TitleBar.ForegroundColor = bgColor;
            }
        }
    }

    protected virtual void ShowMainWindow()
    {
        AppWindow?.Show(true);
        AppWindow?.MoveInZOrderAtTop();
        var mainWindowHandle = Process.GetCurrentProcess().MainWindowHandle;
        if (mainWindowHandle != nint.Zero)
            SetForegroundWindow(mainWindowHandle);
    }

    protected virtual void Exit()
    {
        MauiWinUIApplication.Current.Exit();
        Dispose();
    }

    protected virtual void ConnectionStateChanged(object? sender, EventArgs e)
    {
        UpdateIcon();
    }

    protected virtual void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        args.Cancel = true;
        sender.Hide();
    }

    protected virtual void UpdateIcon()
    {
        // update icon and text
        var badgeValue = VpnHoodApp.Instance.State.ConnectionState switch
        {
            AppConnectionState.Connected => "available",
            AppConnectionState.None => "none",
            _ => "activity"
        };

        // see https://learn.microsoft.com/en-us/windows/apps/design/shell/tiles-and-notifications/badges
        var badgeXml = BadgeUpdateManager.GetTemplateContent(BadgeTemplateType.BadgeGlyph);
        var badgeElement = badgeXml.SelectSingleNode("badge") as Windows.Data.Xml.Dom.XmlElement;
        if (badgeElement == null)
            return;

        badgeElement.SetAttribute("value", badgeValue);
        var badgeNotification = new BadgeNotification(badgeXml);
        var badgeUpdater = BadgeUpdateManager.CreateBadgeUpdaterForApplication();
        badgeUpdater.Update(badgeNotification);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            _tray.Dispose();
            if (VpnHoodWindowsApp.IsInit)
                VpnHoodWindowsApp.Instance.Dispose();
        }

        base.Dispose(disposing);
    }
}