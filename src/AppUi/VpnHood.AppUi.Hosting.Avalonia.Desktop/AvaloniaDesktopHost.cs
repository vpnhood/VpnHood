using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Api;
using VpnHood.AppUi.Common;
using VpnHood.AppUi.Hosting.Desktop.Abstractions;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Net.Toolkit.Assets;
using VpnHood.Net.Toolkit.Graphics;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Avalonia.Desktop;

// An Avalonia UI in a window on a desktop. Which UI is the head's to name, once, as the type
// argument of Run. With a tray, the app's icon sits in the desktop's tray beside the window, and
// while the desktop shows tray icons - Windows always, Linux where its panel does - closing the
// window only hides it: the tray's Open and a second launch bring it back, and the run ends with the
// tray's Exit or Shutdown. Without one, or where the desktop shows none, closing the window ends the
// run. The service lives on either way. Run takes the calling thread as the UI thread until then.
public static class AvaloniaDesktopHost
{
    // How long a start in the tray waits for the desktop's panel, which a sign-in may start after it.
    private static readonly TimeSpan TrayAreaWait = TimeSpan.FromSeconds(30);

    // A second launch's ask for the window, kept when it comes before the run has one, so the window
    // opens as the run starts rather than the ask being lost.
    private static readonly TaskCompletionSource ShowRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Access keys a launch was given before the run has its UI - this launch's own, a second one's -
    // which the UI imports as the run starts.
    private static readonly List<string> PendingAccessKeys = [];
    private static readonly Lock RunLock = new();
    private static ClassicDesktopStyleApplicationLifetime? _lifetime;
    private static Window? _window;
    private static VpnHoodAvaloniaAppBase? _ui;

    [DllImport("DwmApi")]
    private static extern int DwmSetWindowAttribute(IntPtr hWnd, int attr, int[] attrValue, int attrSize);

    // The window over whatever app the host hands over. Its API is the one built over HTTP
    // (VpnHoodApiHttpFactory) against an app running in another process, or an app's own controllers
    // in this one; the content store is the host's to name - a user's cache where the app's storage
    // belongs to the service. Nothing below this line knows which it was given; the pages never did.
    // A run that starts in the tray (startHidden) keeps the window back until ShowMainWindow, and with
    // no tray to wait in, ends at once. Cancelling ends the run: before the window exists, here, with
    // OperationCanceledException; after, through Shutdown, which the caller registers. The window takes
    // the shape its UI states, fitted to its screen (DesktopWindowFit), its title bar in the UI's colour.
    public static void Run<TUi>(string[] args, VpnHoodApi api, IAssetProvider? uiAssetProvider, bool useTray,
        bool startHidden, CancellationToken cancellationToken)
        where TUi : Application, IAvaloniaUi, new()
    {
        using var trayArea = useTray ? AvaloniaTrayArea.Watch() : null;
        if (startHidden && !WaitForTray(trayArea, cancellationToken)) {
            cancellationToken.ThrowIfCancellationRequested();
            VhLogger.Instance.LogInformation("The desktop shows no tray icons, so the start in the tray ends.");
            return;
        }

        AvaloniaUiHosting.StartAsync<TUi>(api, uiAssetProvider, cancellationToken).GetAwaiter().GetResult();

        var lifetime = new ClassicDesktopStyleApplicationLifetime {
            Args = args,
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };
        BuildAvaloniaApp<TUi>().SetupWithLifetime(lifetime);

        var window = lifetime.MainWindow ??
                     throw new InvalidOperationException("The UI has made no main window.");
        var ui = Application.Current as VpnHoodAvaloniaAppBase ??
                 throw new InvalidOperationException($"The UI is no {nameof(VpnHoodAvaloniaAppBase)}, so it states no window shape.");
        AvaloniaWindowFit.Apply(window, ui.WindowFit, VhApp.IsTvUi);
        SetTitleBar(window, ui.ChromeColor);

        // A person's close only hides the window while the desktop shows the tray, which the UI waits
        // in; otherwise it ends the run, as the run's own end and the system's shutdown close it.
        Size? hiddenSize = null;
        window.Closing += (_, e) => {
            if (e.CloseReason != WindowCloseReason.WindowClosing || trayArea?.IsShown != true)
                return;

            e.Cancel = true;
            hiddenSize = window.ClientSize;
            window.Hide();
        };

        // Avalonia 12 on X11 draws the frame itself, and the window manager (KWin and Mutter alike) maps
        // a hidden window again with the whole window at its content's old size, which Avalonia takes
        // the frame off: each show would shrink the window. The platform's resizes after a show are
        // watched until that one comes, which is undone; other resizes may come first (a frame-extents
        // change is raised at once), and a person cannot resize this window to look like it.
        window.Resized += (_, e) => {
            if (e.Reason != WindowResizeReason.Unspecified || !window.IsVisible || hiddenSize is not { } size)
                return;

            if (window.FrameSize is not { } frameSize || !IsSameSize(frameSize, size))
                return;

            hiddenSize = null;
            window.Width = size.Width;
            window.Height = size.Height;
        };
        window.Closed += (_, _) => lifetime.Shutdown();
        AppUiContext.Context = new AvaloniaUiContext(window);
        window.Activated += (_, _) => AppUiContext.NotifyResumed();

        using var tray = trayArea == null
            ? null
            : new AvaloniaDesktopTray(Application.Current ?? throw new InvalidOperationException("Avalonia has no application."),
                api, VhApp.Features.AppName, ShowMainWindow, () => lifetime.Shutdown());

        // Start shows the main window it is given; one kept back waits for ShowMainWindow, unless a
        // launch has asked for it already
        lock (RunLock) {
            _lifetime = lifetime;
            _window = window;
            _ui = ui;
            if (startHidden && !ShowRequest.Task.IsCompleted)
                lifetime.MainWindow = null;

            // run by the dispatcher once Start runs its loop
            foreach (var accessKey in PendingAccessKeys)
                Dispatcher.UIThread.Post(() => _ = ui.ImportAccessKey(accessKey));
            PendingAccessKeys.Clear();
        }

        // a stop that came while the UI was made, when Shutdown had no lifetime to end yet
        cancellationToken.ThrowIfCancellationRequested();
        lifetime.Start(args);
    }

    // Within a pixel: a size read back from the platform at a fractional scaling is rounded.
    private static bool IsSameSize(Size a, Size b)
    {
        return Math.Abs(a.Width - b.Width) < 1 && Math.Abs(a.Height - b.Height) < 1;
    }

    // Whether a start in the tray goes on: once the desktop shows tray icons, or a launch asks for the
    // window. A sign-in's start may come before the desktop's panel, so it waits a while; with neither,
    // there is no tray to wait in. A stop ends the wait at once.
    private static bool WaitForTray(AvaloniaTrayArea? trayArea, CancellationToken cancellationToken)
    {
        if (trayArea == null)
            return ShowRequest.Task.IsCompleted;

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellation.CancelAfter(TrayAreaWait);
        var shown = trayArea.WaitShownAsync(cancellation.Token);
        Task.WaitAny(shown, ShowRequest.Task);
        cancellation.Cancel();
        return !cancellationToken.IsCancellationRequested && (ShowRequest.Task.IsCompleted || trayArea.IsShown);
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
        SetTitleBar(window, color: null);
        window.Closed += (_, _) => lifetime.Shutdown();
        lifetime.MainWindow = window;
        lock (RunLock) {
            _lifetime = lifetime;
            _window = window;
        }

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

    // From any thread: the tray's click, a second launch's command. Before the run has a window the
    // ask is kept for its start, and nothing of Avalonia is touched: its dispatcher belongs to the
    // thread the run makes it on.
    public static void ShowMainWindow()
    {
        lock (RunLock) {
            ShowRequest.TrySetResult();
            if (_lifetime is not { } lifetime || _window is not { } window)
                return;

            Dispatcher.UIThread.Post(() => {
                lifetime.MainWindow = window;
                window.Show();
                window.WindowState = WindowState.Normal;
                window.Activate();
            });
        }
    }

    // From any thread: an access key a launch was given, which the UI imports and says so (VpnHoodAvaloniaAppBase).
    // Before the run has its UI the key is kept for its start, as in ShowMainWindow; a run that shows a message in
    // the window's place has no app to import it into.
    public static void ImportAccessKey(string accessKey)
    {
        lock (RunLock) {
            if (_ui is { } ui)
                Dispatcher.UIThread.Post(() => _ = ui.ImportAccessKey(accessKey));
            else
                PendingAccessKeys.Add(accessKey);
        }
    }

    // From any thread. Before a run has a lifetime there is nothing to end, and nothing of Avalonia
    // is touched, as in ShowMainWindow.
    public static void Shutdown()
    {
        lock (RunLock) {
            if (_lifetime is { } lifetime)
                Dispatcher.UIThread.Post(() => lifetime.Shutdown());
        }
    }

    // Windows draws its own title bar; X11's is the one Avalonia draws (BuildAvaloniaApp).
    private static void SetTitleBar(Window window, VhColor? color)
    {
        if (!OperatingSystem.IsWindows())
            AvaloniaDrawnTitleBar.Apply(window, color);
        else if (color is { } captionColor)
            SetWindowsCaptionColor(window, captionColor);
    }

    // Windows 11 draws the title bar in the given colour rather than the person's accent; Windows 10
    // keeps its own.
    [SupportedOSPlatform("windows")]
    private static void SetWindowsCaptionColor(Window window, VhColor color)
    {
        if (window.TryGetPlatformHandle() is not { } handle)
            return;

        const int captionColor = 35;
        var attrValue = new[] { (color.B << 16) | (color.G << 8) | color.R };
        DwmSetWindowAttribute(handle.Handle, captionColor, attrValue, attrValue.Length * 4);
    }

    // An X11 window manager frames a window in its own colours, which the window cannot change, so
    // Avalonia draws the title bar instead and it takes the app's (AvaloniaDrawnTitleBar). Avalonia 12
    // marks the option experimental. WSLg shows such a window as its shadow alone, the content
    // see-through, so there WSLg keeps framing it.
    private static AppBuilder BuildAvaloniaApp<TUi>()
        where TUi : Application, new()
    {
#pragma warning disable AVALONIA_X11_FORCE_CSD
        return AppBuilder.Configure<TUi>().UsePlatformDetect()
            .With(new X11PlatformOptions { ForceDrawnDecorations = !IsWslg() });
#pragma warning restore AVALONIA_X11_FORCE_CSD
    }

    private static bool IsWslg()
    {
        return OperatingSystem.IsLinux() && Directory.Exists("/mnt/wslg");
    }
}
