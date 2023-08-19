using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics;
using AppWindow = Microsoft.UI.Windowing.AppWindow;
using Microsoft.Maui.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace VpnHood.Client.App.Maui.WinUI;    

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetForegroundWindow(IntPtr hWnd);
    private AppWindow? _appWindow;
    private readonly SizeInt32 _defWindowSize = new(400, 700);
    private readonly WinApp _winApp;

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();
        _winApp = new WinApp();
        _winApp.OpenWindowRequested += OnOpenWindowRequested;
        _winApp.ExitRequested += ExitRequested;

        Microsoft.Maui.Handlers.WindowHandler.Mapper.AppendToMapping(nameof(IWindow), (handler, view) =>
        {
            _appWindow = handler.PlatformView.GetAppWindow();
            if (_appWindow != null)
            {
                _appWindow.Closing += AppWindow_Closing;
            }
        });

    }

    protected override MauiApp CreateMauiApp()
    {
        var mauiApp = MauiProgram.CreateMauiApp();
        return mauiApp;
    }

    private static void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        args.Cancel = true;
        sender.Hide();
    }

    private void ExitRequested(object? sender, EventArgs e)
    {
        Exit();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (!_winApp.Start(Array.Empty<string>()))
            return;

        base.OnLaunched(args);
    }

    private void OnOpenWindowRequested(object? sender, EventArgs e)
    {
        _appWindow?.Show(true);
        _appWindow?.MoveInZOrderAtTop();
        var mainWindowHandle = Process.GetCurrentProcess().MainWindowHandle;
        if (mainWindowHandle != nint.Zero)
            SetForegroundWindow(mainWindowHandle);
    }

    public void Foo()
    {

    }

}