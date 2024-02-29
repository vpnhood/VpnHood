using System.Runtime.InteropServices;
using Windows.Data.Xml.Dom;
using Microsoft.UI.Xaml;
using VpnHood.Client.App.Win.Common;
using VpnHood.Client.Device.WinDivert;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

// ReSharper disable once CheckNamespace
namespace VpnHood.Client.Samples.MauiAppSpaSample.WinUI;

using Microsoft.Maui.Platform;
using Microsoft.UI.Windowing;
using System.Diagnostics;
using VpnHood.Client.App;
using VpnHood.Client.App.WebServer;
using Windows.UI.Notifications;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
// ReSharper disable once RedundantExtendsListEntry
public partial class App : MauiWinUIApplication
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private AppWindow? _appWindow;


    protected override MauiApp CreateMauiApp()
    {
        WinApp.Instance.PreStart(Environment.GetCommandLineArgs());
        return MauiProgram.CreateMauiApp(new WinDivertDevice());
    }

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        this.InitializeComponent();
        WinApp.Instance.OpenMainWindowRequested += OpenMainWindowRequested;
        WinApp.Instance.OpenMainWindowInBrowserRequested += OpenMainWindowInBrowserRequested;
        WinApp.Instance.ExitRequested += ExitRequested;

        Microsoft.Maui.Handlers.WindowHandler.Mapper.AppendToMapping(nameof(IWindow), (handler, _) =>
        {
            _appWindow = handler.PlatformView.GetAppWindow();

            //customize WinUI main window
            if (_appWindow != null)
            {
                _appWindow.TitleBar.IconShowOptions = IconShowOptions.HideIconAndSystemMenu;
                _appWindow.Closing += AppWindow_Closing;

                var bgColorResource = VpnHoodApp.Instance.Resources.Colors.WindowBackgroundColor;
                if (bgColorResource != null)
                {
                    var bgColor = Windows.UI.Color.FromArgb(bgColorResource.Value.A, bgColorResource.Value.R,
                        bgColorResource.Value.G, bgColorResource.Value.B);
                    _appWindow.TitleBar.ButtonBackgroundColor = bgColor;
                    _appWindow.TitleBar.BackgroundColor = bgColor;
                    _appWindow.TitleBar.ForegroundColor = bgColor;
                }
            }
        });
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        base.OnLaunched(args);
     
        WinApp.Instance.Start();
        VpnHoodApp.Instance.ConnectionStateChanged += ConnectionStateChanged;
        UpdateIcon();
    }

    private void OpenMainWindowRequested(object? sender, EventArgs e)
    {
        _appWindow?.Show(true);
        _appWindow?.MoveInZOrderAtTop();
        var mainWindowHandle = Process.GetCurrentProcess().MainWindowHandle;
        if (mainWindowHandle != nint.Zero)
            SetForegroundWindow(mainWindowHandle);
    }

    private static void OpenMainWindowInBrowserRequested(object? sender, EventArgs e)
    {
        Browser.Default.OpenAsync(VpnHoodAppWebServer.Instance.Url, BrowserLaunchMode.External);
    }

    private void ExitRequested(object? sender, EventArgs e)
    {
        WinApp.Instance.Dispose();
        Exit();
    }

    private static void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        args.Cancel = true;
        sender.Hide();
    }

    private static void ConnectionStateChanged(object? sender, EventArgs e)
    {
        UpdateIcon();
    }

    private static void UpdateIcon()
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
        var badgeElement = badgeXml.SelectSingleNode("badge") as XmlElement;
        if (badgeElement == null)
            return;

        badgeElement.SetAttribute("value", badgeValue);
        var badgeNotification = new BadgeNotification(badgeXml);
        var badgeUpdater = BadgeUpdateManager.CreateBadgeUpdaterForApplication();
        badgeUpdater.Update(badgeNotification);
    }
}
