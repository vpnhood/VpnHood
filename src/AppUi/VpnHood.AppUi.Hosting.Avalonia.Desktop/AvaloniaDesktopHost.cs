using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using VpnHood.AppLib.App;
using VpnHood.AppLib.Api;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Net.Toolkit.Assets;

namespace VpnHood.AppUi.Hosting.Avalonia.Desktop;

// An Avalonia UI in a window on a desktop: a Windows or Linux head runs it here in place of the
// web UI when the app asks (DebugCommands.AvaloniaUi). Which UI is the head's to name, once, as
// the type argument of Run. Where a tray keeps the UI (Windows), the window hides rather than
// closes - the app lives on in the tray and asks for the window again through ShowMainWindow - so
// the run ends only with Shutdown, which the head calls as the app exits. Where none does (Linux,
// exitOnClose), closing the window ends the run; the service lives on either way. Run takes the
// calling thread as the UI thread until then.
public static class AvaloniaDesktopHost
{
    private static ClassicDesktopStyleApplicationLifetime? _lifetime;
    private static Window? _window;

    // VpnHoodApp must be up; its web host comes up by itself when a phone pairs. A run that starts
    // in the background (the head's /nowindow) keeps the window back until ShowMainWindow.
    public static void Run<TUi>(string[] args, bool showWindow)
        where TUi : Application, IAvaloniaUi, new()
    {
        // The UI reaches the app through its API - the same six interfaces a paired browser dials
        // over HTTP, here the app's own controllers in process; in process both complete at once.
        Run<TUi>(args, showWindow, VpnHoodApp.Instance.Api, VpnHoodApp.Instance.UiAssetProvider,
            exitOnClose: false);
    }

    // The same window, for a head that holds no VpnHoodApp: the API is the one built over HTTP
    // (VpnHoodApiHttpFactory) against an app running in another process, and the content store is the
    // head's own - on Linux a user's cache, since the app's storage belongs to root. Nothing below
    // this line knows which of the two it was given; the pages never did.
    public static void Run<TUi>(string[] args, bool showWindow, VpnHoodApi api,
        IAssetProvider? uiAssetProvider, bool exitOnClose)
        where TUi : Application, IAvaloniaUi, new()
    {
        AvaloniaUiHosting.StartAsync<TUi>(api, uiAssetProvider, CancellationToken.None).GetAwaiter().GetResult();

        var lifetime = new ClassicDesktopStyleApplicationLifetime {
            Args = args,
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };
        BuildAvaloniaApp<TUi>().SetupWithLifetime(lifetime);

        var window = lifetime.MainWindow ??
                     throw new InvalidOperationException("The UI has made no main window.");
        if (exitOnClose) {
            window.Closed += (_, _) => lifetime.Shutdown();
        }
        else {
            window.Closing += (_, e) => {
                e.Cancel = true;
                window.Hide();
            };
        }
        AppUiContext.Context = new AvaloniaUiContext(window);
        _lifetime = lifetime;
        _window = window;

        // Start shows the main window it is given; the window it is not given waits for ShowMainWindow
        if (!showWindow)
            lifetime.MainWindow = null;
        lifetime.Start(args);
    }

    // From any thread: the tray's click, a second launch's command.
    public static void ShowMainWindow()
    {
        Dispatcher.UIThread.Post(() => {
            if (_lifetime == null || _window == null)
                return;

            _lifetime.MainWindow = _window;
            _window.Show();
            _window.WindowState = WindowState.Normal;
            _window.Activate();
        });
    }

    // From any thread. Before a run has a lifetime there is nothing to end, and nothing of Avalonia
    // is touched: its dispatcher belongs to the thread the run makes it on.
    public static void Shutdown()
    {
        if (_lifetime is not { } lifetime)
            return;

        Dispatcher.UIThread.Post(() => lifetime.Shutdown());
    }

    private static AppBuilder BuildAvaloniaApp<TUi>()
        where TUi : Application, new()
    {
        return AppBuilder.Configure<TUi>().UsePlatformDetect();
    }
}
