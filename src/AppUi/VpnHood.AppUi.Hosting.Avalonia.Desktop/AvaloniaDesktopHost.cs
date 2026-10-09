using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.App;
using VpnHood.AppLib.App.Branding;
using VpnHood.AppLib.Api;
using VpnHood.AppUi.Common;
using VpnHood.AppUi.Hosting.Abstractions;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Net.Toolkit.Assets;
using VpnHood.Net.Toolkit.Graphics;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Avalonia.Desktop;

// An Avalonia UI in a window on a desktop. Which UI is the head's to name, once, as the type
// argument of Run. Where a tray keeps the UI (Windows), the window hides rather than closes - the
// app lives on in the tray and asks for the window again through ShowMainWindow - so the run ends
// only with Shutdown, which the head calls as the app exits. Where none does (Linux, exitOnClose),
// closing the window ends the run; the service lives on either way. Run takes the calling thread
// as the UI thread until then.
public static class AvaloniaDesktopHost
{
    private static ClassicDesktopStyleApplicationLifetime? _lifetime;
    private static Window? _window;

    [DllImport("DwmApi")]
    private static extern int DwmSetWindowAttribute(IntPtr hWnd, int attr, int[] attrValue, int attrSize);

    // The window over whatever app the host hands over. Its API is the one built over HTTP
    // (VpnHoodApiHttpFactory) against an app running in another process, or an app's own controllers
    // in this one; the content store is the host's to name - a user's cache where the app's storage
    // belongs to the service. Nothing below this line knows which it was given; the pages never did.
    // A run that starts in the background keeps the window back until ShowMainWindow.
    public static void Run<TUi>(string[] args, bool showWindow, VpnHoodApi api,
        IAssetProvider? uiAssetProvider, bool exitOnClose)
        where TUi : Application, IAvaloniaUi, new()
    {
        AvaloniaUiHosting.StartAsync<TUi>(api, uiAssetProvider, CancellationToken.None).GetAwaiter().GetResult();

        // the colours the OS chrome takes, out of the UI's store, and the phone's shape the window
        // opens in
        var resources = new AppResources();
        AppBranding.LoadAsync(resources, uiAssetProvider, VhApp.Features.UiTheme).GetAwaiter().GetResult();

        var lifetime = new ClassicDesktopStyleApplicationLifetime {
            Args = args,
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };
        BuildAvaloniaApp<TUi>().SetupWithLifetime(lifetime);

        var window = lifetime.MainWindow ??
                     throw new InvalidOperationException("The UI has made no main window.");
        AvaloniaWindowFit.Apply(window, resources.WindowSize, VhApp.IsTvUi);
        if (OperatingSystem.IsWindows() && resources.Colors.WindowBackgroundColor is { } titleBarColor)
            SetTitleBarColor(window, titleBarColor);
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
        window.Activated += (_, _) => AppUiContext.NotifyResumed();
        _lifetime = lifetime;
        _window = window;

        // Start shows the main window it is given; the window it is not given waits for ShowMainWindow
        if (!showWindow)
            lifetime.MainWindow = null;
        lifetime.Start(args);
    }

    // One message and a Close button, in the UI's default look, for a host with no app to give the
    // UI: whoever runs it may not use the app, or the app could not be started or reached. The words
    // are the UI's own files in the device's language - the language a person chose is in the app's
    // settings, which are not to be had - and the rest of the UI's start does not run, since it calls
    // the app first (StartAsync).
    public static void RunMessage<TUi>(DesktopUiMessageParams messageParams)
        where TUi : Application, IAvaloniaUi, new()
    {
        // Avalonia before the content, as on Android: the UI then takes its fonts in as they load,
        // inside TryLoadStrings, so a store whose fonts cannot be read leaves the default ones rather
        // than failing the setup with no window at all.
        var lifetime = new ClassicDesktopStyleApplicationLifetime {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };
        BuildAvaloniaApp<TUi>().SetupWithLifetime(lifetime);

        var strings = messageParams.UiAssetProvider is { } assets ? TryLoadStrings<TUi>(assets) : null;
        var text = strings != null && messageParams.Kind == DesktopUiMessageKind.AdministratorsOnly
            ? strings.AdministratorsOnly
            : messageParams.Text;

        var window = new AvaloniaMessageWindow(messageParams.AppName, text, strings?.Close ?? "Close",
            strings?.IsRightToLeft ?? false);
        window.Closed += (_, _) => lifetime.Shutdown();
        lifetime.MainWindow = window;
        _lifetime = lifetime;
        _window = window;
        lifetime.Start([]);
    }

    // The words in the device's language: loading them leaves English. Null where the content cannot
    // be loaded - a broken store, fonts that cannot be read, a cache that cannot be written - and the
    // message is the host's English: it is the last word a person gets, and must not fail with what
    // it reports.
    private static Strings? TryLoadStrings<TUi>(IAssetProvider assets)
        where TUi : IAvaloniaUi
    {
        try {
            TUi.PrepareContentAsync(assets, CancellationToken.None).GetAwaiter().GetResult();
            Strings.Current.SetCultureAsync(CultureInfo.CurrentUICulture, CancellationToken.None).GetAwaiter().GetResult();
            return Strings.Current;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not load the UI's words. The message is shown in English.");
            return null;
        }
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

    // Windows 11 draws the title bar in the given colour rather than the person's accent; Windows 10
    // keeps its own.
    [SupportedOSPlatform("windows")]
    private static void SetTitleBarColor(Window window, VhColor color)
    {
        if (window.TryGetPlatformHandle() is not { } handle)
            return;

        const int captionColor = 35;
        var attrValue = new[] { (color.B << 16) | (color.G << 8) | color.R };
        DwmSetWindowAttribute(handle.Handle, captionColor, attrValue, attrValue.Length * 4);
    }

    private static AppBuilder BuildAvaloniaApp<TUi>()
        where TUi : Application, new()
    {
        return AppBuilder.Configure<TUi>().UsePlatformDetect();
    }
}
