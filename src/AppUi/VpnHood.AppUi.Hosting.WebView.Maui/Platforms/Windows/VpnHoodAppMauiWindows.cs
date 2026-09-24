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

    private VpnHoodAppMauiWindows(AppOptions appOptions)
    {
        // initialize Win App
        appOptions.DisconnectOnDispose = true;
        VpnHoodWindowsApp.Init(appOptions, args: Environment.GetCommandLineArgs());
        VpnHoodWindowsApp.Instance.OpenMainWindowRequested += OpenMainWindowRequested;
        VpnHoodWindowsApp.Instance.ExitRequested += ExitRequested;
        VpnHoodWindowsApp.Instance.Start();

        // initialize VpnHoodApp
        VpnHoodApp.Instance.ConnectionStateChanged += ConnectionStateChanged;

        // customize main window
        WindowHandler.Mapper.AppendToMapping(nameof(IWindow), MappingMethod);

    }

    public static VpnHoodAppMauiWindows Init(Func<AppOptions> optionsFactory)
    {
        var appOptions = optionsFactory();
        var app = new VpnHoodAppMauiWindows(appOptions);
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

    protected virtual void OpenMainWindowRequested(object? sender, EventArgs e)
    {
        AppWindow?.Show(true);
        AppWindow?.MoveInZOrderAtTop();
        var mainWindowHandle = Process.GetCurrentProcess().MainWindowHandle;
        if (mainWindowHandle != nint.Zero)
            SetForegroundWindow(mainWindowHandle);
    }

    protected virtual void ExitRequested(object? sender, EventArgs e)
    {
        MauiWinUIApplication.Current.Exit();
        if (VpnHoodWindowsApp.IsInit)
            VpnHoodWindowsApp.Instance.Dispose();
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
            if (VpnHoodWindowsApp.IsInit)
                VpnHoodWindowsApp.Instance.Dispose();
        }

        base.Dispose(disposing);
    }
}