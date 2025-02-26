using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.UI.Notifications;
using Microsoft.Maui.Platform;
using Microsoft.UI.Windowing;
using VpnHood.AppLib.Win.Common;
using VpnHood.Core.Client.Device.WinDivert;

// ReSharper disable once CheckNamespace
namespace VpnHood.AppLib.Maui.Common;

internal class VpnHoodMauiWinUiApp : IVpnHoodMauiApp
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    
    protected AppWindow? AppWindow;
    
    public VpnHoodApp Init(AppOptions options)
    {
        var device = new WinDevice(options.StorageFolderPath, options.IsDebugMode);
        var vpnHoodApp = VpnHoodApp.Init(device, options);

        VpnHoodWinApp.Init(vpnHoodApp.Features.AppId, vpnHoodApp.StorageFolderPath);
        VpnHoodWinApp.Instance.PreStart(Environment.GetCommandLineArgs());
        VpnHoodWinApp.Instance.OpenMainWindowRequested += OpenMainWindowRequested;
        VpnHoodWinApp.Instance.OpenMainWindowInBrowserRequested += OpenMainWindowInBrowserRequested;
        VpnHoodWinApp.Instance.ExitRequested += ExitRequested;
        VpnHoodWinApp.Instance.Start();
        vpnHoodApp.ConnectionStateChanged += ConnectionStateChanged;
        UpdateIcon();

        Microsoft.Maui.Handlers.WindowHandler.Mapper.AppendToMapping(nameof(IWindow), (handler, _) =>
        {
            AppWindow = handler.PlatformView.GetAppWindow();

            //customize WinUI main window
            if (AppWindow != null)
            {
                AppWindow.TitleBar.IconShowOptions = IconShowOptions.HideIconAndSystemMenu;
                AppWindow.Closing += AppWindow_Closing;

                var bgColorResource = vpnHoodApp.Resources.Colors.WindowBackgroundColor;
                if (bgColorResource != null)
                {
                    var bgColor = Windows.UI.Color.FromArgb(bgColorResource.Value.A, bgColorResource.Value.R,
                        bgColorResource.Value.G, bgColorResource.Value.B);
                    AppWindow.TitleBar.ButtonBackgroundColor = bgColor;
                    AppWindow.TitleBar.BackgroundColor = bgColor;
                    AppWindow.TitleBar.ForegroundColor = bgColor;
                }
            }
        });

        return vpnHoodApp;
    }

    protected virtual void OpenMainWindowRequested(object? sender, EventArgs e)
    {
        AppWindow?.Show(true);
        AppWindow?.MoveInZOrderAtTop();
        var mainWindowHandle = Process.GetCurrentProcess().MainWindowHandle;
        if (mainWindowHandle != nint.Zero)
            SetForegroundWindow(mainWindowHandle);
    }

    protected virtual void OpenMainWindowInBrowserRequested(object? sender, EventArgs e)
    {
        //Browser.Default.OpenAsync(VpnHoodAppWebServer.Instance.Url, BrowserLaunchMode.External);
        throw new NotSupportedException();
    }

    protected virtual void ExitRequested(object? sender, EventArgs e)
    {
        VpnHoodWinApp.Instance.Dispose();
        MauiWinUIApplication.Current.Exit();
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
}