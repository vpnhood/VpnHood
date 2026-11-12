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
using VpnHood.Net.Toolkit.Assets;
using VpnHood.Net.Toolkit.Graphics;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.AppLib.Abstractions.UiContexts;

namespace VpnHood.AppUi.Hosting.Avalonia.Desktop;

/// <summary>
/// Provides an Avalonia UI in a window on a desktop, with the app's icon in the desktop's tray where the host allows one.
/// </summary>
/// <remarks>
/// While the run uses a tray and the desktop shows tray icons, closing the window only hides it: the tray's Open and a
/// second launch bring it back, and the run ends with the tray's Exit or the host's shutdown. Otherwise closing the
/// window ends the run, unless the run is to keep going while the app in this process is connected: the window then
/// hides while the app's VPN can be disconnected, a connect under way included, and the run ends once it cannot, unless
/// a launch shows the window first. On Windows the window's taskbar button carries the connection's badge too. The run
/// takes the calling thread as the UI thread.
/// </remarks>
public static class AvaloniaDesktopHost
{
    // How long a start in the tray waits for the desktop's panel, which a sign-in may start after it.
    private static readonly TimeSpan TrayAreaWait = TimeSpan.FromSeconds(30);

    // How often a window hidden while the app's VPN is up reads whether it is down yet.
    private static readonly TimeSpan HiddenStateInterval = TimeSpan.FromSeconds(2);

    // A second launch's ask for the window, kept when it comes before the run has one, so the window
    // opens as the run starts rather than the ask being lost.
    private static readonly TaskCompletionSource ShowRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Access keys a launch was given before the run has its UI - this launch's own, a second one's -
    // which the UI imports as the run starts; and a Quick Launch the same way, which it connects for.
    private static readonly List<string> PendingAccessKeys = [];
    private static bool _isQuickLaunchPending;
    private static readonly Lock RunLock = new();
    private static ClassicDesktopStyleApplicationLifetime? _lifetime;
    private static Window? _window;
    private static VpnHoodAvaloniaAppBase? _ui;

    [DllImport("DwmApi")]
    private static extern int DwmSetWindowAttribute(IntPtr hWnd, int attr, int[] attrValue, int attrSize);

    /// <summary>Runs the window over the app's API until the run ends, on the calling thread.</summary>
    /// <typeparam name="TUi">The UI's application.</typeparam>
    /// <param name="args">The command-line arguments Avalonia's lifetime gets.</param>
    /// <param name="api">The app's API, over HTTP against an app in another process, or the app's own in this one.</param>
    /// <param name="uiAssetProvider">The UI's content store, which the UI reads its pictures and its words from.</param>
    /// <param name="useTray"><see langword="true"/> to put the app's icon in the desktop's tray.</param>
    /// <param name="keepRunningWhileConnected">
    /// <see langword="true"/> to hide the window, rather than end the run, while the app's VPN can be disconnected.
    /// </param>
    /// <param name="shell">The desktop's shell, or <see langword="null"/>.</param>
    /// <param name="startHidden"><see langword="true"/> to start in the tray, the window kept back until it is asked for.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests while the run starts.</param>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="uiAssetProvider"/> is <see langword="null"/>, or the UI made no main window or is not a
    /// <see cref="VpnHoodAvaloniaAppBase"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled before the window's loop started.
    /// </exception>
    /// <remarks>
    /// A run that starts in the tray waits up to 30 seconds for the desktop to show tray icons, as a sign-in may start
    /// before its panel, and ends at once where none show, unless a launch asks for the window first. A canceled token ends
    /// the run only while it starts; to have a cancel end the running window too, run it through
    /// <see cref="AvaloniaDesktopUi{TUi}"/>. The window takes the shape its UI states, fitted to its screen, with its title
    /// bar in the UI's colour where the UI names one and the platform lets it be set.
    /// </remarks>
    public static void Run<TUi>(string[] args, VpnHoodApi api, IAssetProvider? uiAssetProvider, bool useTray,
        bool keepRunningWhileConnected, IDesktopShell? shell, bool startHidden, CancellationToken cancellationToken)
        where TUi : Application, IAvaloniaUi, new()
    {
        using var trayArea = useTray ? AvaloniaTrayArea.Watch() : null;
        if (startHidden && !WaitForTray(trayArea, cancellationToken)) {
            cancellationToken.ThrowIfCancellationRequested();
            VhLogger.Instance.LogInformation("The desktop shows no tray icons, so the start in the tray ends.");
            return;
        }

        AvaloniaUiHosting.Shell = shell;
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
        AvaloniaWindowFit.Apply(window, ui.WindowFit);
        SetTitleBar(window, ui.ChromeColor);

        // A person's close only hides the window while the desktop shows the tray, which the UI waits
        // in; or, without one, while the app's VPN is up where the VPN runs only while this app does
        // (keepRunningWhileConnected), until the VPN is down. Otherwise it ends the run, as the run's own
        // end and the system's shutdown close it.
        Size? hiddenSize = null;
        window.Closing += (sender, e) => {
            // ReSharper disable once AccessToDisposedClosure
            var isToTray = trayArea?.IsShown == true;
            if (e.CloseReason != WindowCloseReason.WindowClosing || (!isToTray && !keepRunningWhileConnected))
                return;

            e.Cancel = true;
            hiddenSize = window.ClientSize;
            window.Hide();
            if (!isToTray)
                _ = EndOnceDisconnected(api, window, lifetime, CancellationToken.None);
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

        // the connection outside the window: on the tray's icon, and on the window's taskbar button
        using var stateFollower = new AvaloniaAppStateFollower(api);
        using var tray = trayArea == null
            ? null
            : new AvaloniaDesktopTray(Application.Current ?? throw new InvalidOperationException("Avalonia has no application."),
                api, stateFollower, VhApp.Descriptor.AppName, ShowMainWindow, () => lifetime.Shutdown());
        using var taskbarBadge = AvaloniaTaskbarBadge.Create(window, stateFollower, shell);

        // Start shows the main window it is given; one kept back waits for ShowMainWindow, unless a
        // launch has asked for it already
        lock (RunLock) {
            _lifetime = lifetime;
            _window = window;
            _ui = ui;
            if (startHidden && !ShowRequest.Task.IsCompleted)
                lifetime.MainWindow = null;

            // run by the dispatcher once Start runs its loop: a key before the connect, which may be for its server
            foreach (var accessKey in PendingAccessKeys)
                Dispatcher.UIThread.Post(() => _ = ui.ImportAccessKey(accessKey));
            PendingAccessKeys.Clear();
            if (_isQuickLaunchPending)
                Dispatcher.UIThread.Post(() => _ = ui.QuickLaunch());
            _isQuickLaunchPending = false;
        }

        // a stop that came while the UI was made, when Shutdown had no lifetime to end yet
        cancellationToken.ThrowIfCancellationRequested();
        lifetime.Start(args);
    }

    // A window hidden while the app's VPN is up: the run ends once the VPN is down - at once, if it was down
    // already - unless a launch shows the window again first. Read as the tray reads the app's state.
    private static async Task EndOnceDisconnected(VpnHoodApi api, Window window,
        ClassicDesktopStyleApplicationLifetime lifetime, CancellationToken cancellationToken)
    {
        try {
            while (!window.IsVisible) {
                var state = await api.App.GetState(cancellationToken);
                if (!state.CanDisconnect) {
                    lifetime.Shutdown();
                    return;
                }

                await Task.Delay(HiddenStateInterval, cancellationToken);
            }
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not read the app's state for its hidden window, so the run ends.");
            lifetime.Shutdown();
        }
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
    internal static void RunMessage<TUi>(DesktopUiMessageParams messageParams)
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
    // it reports, so nothing cancels it.
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
    internal static void ShowMainWindow()
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
    internal static void ImportAccessKey(string accessKey)
    {
        lock (RunLock) {
            if (_ui is { } ui)
                Dispatcher.UIThread.Post(() => _ = ui.ImportAccessKey(accessKey));
            else
                PendingAccessKeys.Add(accessKey);
        }
    }

    // From any thread: a Quick Launch, on which the UI connects as its Connect button does (VpnHoodAvaloniaAppBase).
    // Before the run has its UI it is kept for its start, as an access key is.
    internal static void QuickLaunch()
    {
        lock (RunLock) {
            if (_ui is { } ui)
                Dispatcher.UIThread.Post(() => _ = ui.QuickLaunch());
            else
                _isQuickLaunchPending = true;
        }
    }

    // From any thread. Before a run has a lifetime there is nothing to end, and nothing of Avalonia
    // is touched, as in ShowMainWindow.
    internal static void Shutdown()
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
            .With(new X11PlatformOptions { ForceDrawnDecorations = !IsWslg() })
            .WithChineseFontFallback();
#pragma warning restore AVALONIA_X11_FORCE_CSD
    }

    private static bool IsWslg()
    {
        return OperatingSystem.IsLinux() && Directory.Exists("/mnt/wslg");
    }
}
