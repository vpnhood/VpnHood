using System.Runtime.InteropServices;
using VpnHood.AppLib.Api.App;
using Windows.UI.Notifications;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.UI.Windowing;
using VpnHood.AppLib.App.WindowsTun;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.AppLib.App;
using VpnHood.AppUi.Hosting.WebView;

// ReSharper disable once CheckNamespace
namespace VpnHood.AppUi.Hosting.Maui;

internal class VpnHoodAppMauiWindows : Singleton<VpnHoodAppMauiWindows>, IVpnHoodAppMaui
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, IntPtr packageFullName);

    private const int AppModelErrorNoPackage = 15700;

    // The taskbar badge is a packaged app's: an unpackaged one (WindowsPackageType None) has no identity
    // to badge, and Windows refuses its updater ("Element not found").
    private static readonly bool HasPackageIdentity = GetHasPackageIdentity();

    protected AppWindow? AppWindow;

    private VpnHoodAppMauiWindows(AppInitParams initParams)
    {
        // The platform starts the app, as under any other UI - in this process, under the
        // person's storage folder, which holds its single-instance lock too - and it disconnects on
        // the way out. In this process on purpose, to keep a MAUI app simple: the tunnel runs here
        // too, so the app runs elevated. A shipped Windows app keeps the app in the machine service
        // instead (VpnHood.AppUi.Hosting.Desktop.WindowsTun), its window unelevated. It keeps no tray, so
        // the app ends with its window.
        var storagePath = initParams.ResolveStoragePath();
        VpnHoodWindowsApp.Init(initParams, storagePath, lockFolderPath: storagePath);

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

    private void MappingMethod(IWindowHandler handler, IWindow window)
    {
        AppWindow = handler.PlatformView.GetAppWindow();

        //customize WinUI main window
        if (AppWindow != null) {
            AppWindow.TitleBar.IconShowOptions = IconShowOptions.HideIconAndSystemMenu;
            // the app goes with its window, disconnecting first
            AppWindow.Closing += (_, _) => Dispose();

            _ = SetTitleBarColor(AppWindow.TitleBar);
        }
    }

    // the title bar in the theme's window background, once it is read; the wait comes back to the
    // window's thread
    private static async Task SetTitleBarColor(AppWindowTitleBar titleBar)
    {
        if ((await WebViewTheme.Colors).WindowBackgroundColor is not { } color)
            return;

        var bgColor = Windows.UI.Color.FromArgb(color.A, color.R, color.G, color.B);
        titleBar.ButtonBackgroundColor = bgColor;
        titleBar.BackgroundColor = bgColor;
        titleBar.ForegroundColor = bgColor;
    }

    protected virtual void ConnectionStateChanged(object? sender, EventArgs e)
    {
        UpdateIcon();
    }

    protected virtual void UpdateIcon()
    {
        if (!HasPackageIdentity)
            return;

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

    private static bool GetHasPackageIdentity()
    {
        var length = 0;
        return GetCurrentPackageFullName(ref length, IntPtr.Zero) != AppModelErrorNoPackage;
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