using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Data.Xml.Dom;
using AppWindow = Microsoft.UI.Windowing.AppWindow;
using VpnHood.Client.App.Resources;
using Windows.UI.Notifications;
using VpnHood.Client.App.UI;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

// ReSharper disable once CheckNamespace
namespace VpnHood.Client.App.Maui.WinUI;

// ReSharper disable once RedundantExtendsListEntry
public partial class App : MauiWinUIApplication
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    private AppWindow? _appWindow;

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();
        WinApp.Instance.OpenMainWindowRequested += OpenMainWindowRequested;
        WinApp.Instance.OpenMainWindowInBrowserRequested += OpenMainWindowInBrowserRequested;
        WinApp.Instance.ExitRequested += ExitRequested;

        Microsoft.Maui.Handlers.WindowHandler.Mapper.AppendToMapping(nameof(IWindow), (handler, _) =>
        {
            _appWindow = handler.PlatformView.GetAppWindow();
            if (_appWindow != null)
            {
                var bgColor = Windows.UI.Color.FromArgb(UiDefaults.WindowBackgroundColor.A, UiDefaults.WindowBackgroundColor.R, UiDefaults.WindowBackgroundColor.G, UiDefaults.WindowBackgroundColor.B);
                _appWindow.TitleBar.IconShowOptions = IconShowOptions.HideIconAndSystemMenu;
                _appWindow.TitleBar.ButtonBackgroundColor = bgColor;
                _appWindow.TitleBar.BackgroundColor = bgColor;
                _appWindow.TitleBar.ForegroundColor = bgColor;
                _appWindow.Closing += AppWindow_Closing;
            }
        });
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        WinApp.Instance.PreStart(Environment.GetCommandLineArgs());
        base.OnLaunched(args);
    }

    protected override MauiApp CreateMauiApp()
    {
        var mauiApp = MauiProgram.CreateMauiApp();
        WinApp.Instance.Start();
        VpnHoodApp.Instance.ConnectionStateChanged += ConnectionStateChanged;
        UpdateIcon();
        return mauiApp;
    }

    private static void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        args.Cancel = true;
        sender.Hide();
    }

    private void OpenMainWindowRequested(object? sender, EventArgs e)
    {
        _appWindow?.Show(true);
        _appWindow?.MoveInZOrderAtTop();
        var mainWindowHandle = Process.GetCurrentProcess().MainWindowHandle;
        if (mainWindowHandle != nint.Zero)
            SetForegroundWindow(mainWindowHandle);
    }

    private void OpenMainWindowInBrowserRequested(object? sender, EventArgs e)
    {
        Browser.Default.OpenAsync(VpnHoodAppUi.Instance.Url, BrowserLaunchMode.External);
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
    
    private void ExitRequested(object? sender, EventArgs e)
    {
        WinApp.Instance.Dispose();
        Exit();
    }
}
