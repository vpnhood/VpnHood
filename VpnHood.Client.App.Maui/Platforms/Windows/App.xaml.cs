using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics;
using AppWindow = Microsoft.UI.Windowing.AppWindow;
using Windows.UI.WindowManagement;
using VpnHood.Client.App.Resources;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

// ReSharper disable once CheckNamespace
namespace VpnHood.Client.App.Maui.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
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
        WinApp.Instance.OpenMainWindowInBrowserRequested += OpenMainWindowInBrowserRequested;
        WinApp.Instance.ExitRequested += ExitRequested;

        Microsoft.Maui.Handlers.WindowHandler.Mapper.AppendToMapping(nameof(IWindow), (handler, view) =>
        {
            _appWindow = handler.PlatformView.GetAppWindow();
            if (_appWindow != null)
            {
                var bgColor = UiDefaults.WindowBackgroundColor;
                _appWindow.TitleBar.BackgroundColor = Windows.UI.Color.FromArgb(bgColor.A, bgColor.R, bgColor.G, bgColor.B) ;
                _appWindow.SetIcon();
                _appWindow.Closing += AppWindow_Closing;
            }
        });
    }

    protected override MauiApp CreateMauiApp()
    {
        var mauiApp = MauiProgram.CreateMauiApp();
        WinApp.Instance.Start();
        return mauiApp;
    }

    private static void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        args.Cancel = true;
        sender.Hide();
    }

    private void ExitRequested(object? sender, EventArgs e)
    {
        WinApp.Instance.Dispose();
        Exit();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        WinApp.Instance.PreStart(Environment.GetCommandLineArgs());
        base.OnLaunched(args);
    }

    private void OpenMainWindowInBrowserRequested(object? sender, EventArgs e)
    {
        _appWindow?.Show(true);
        _appWindow?.MoveInZOrderAtTop();
        var mainWindowHandle = Process.GetCurrentProcess().MainWindowHandle;
        if (mainWindowHandle != nint.Zero)
            SetForegroundWindow(mainWindowHandle);
    }
}