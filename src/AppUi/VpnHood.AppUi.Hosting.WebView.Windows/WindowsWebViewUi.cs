using System.IO;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Api;
using VpnHood.AppLib.Api.App;
using VpnHood.AppUi.Common;
using VpnHood.AppUi.Hosting.Desktop.Abstractions;
using VpnHood.AppUi.Hosting.WebView.Windows.WinNative;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.AppLib.Abstractions.UiContexts;

namespace VpnHood.AppUi.Hosting.WebView.Windows;

/// <summary>
/// Provides the app's web UI in a Win32 window over WebView2, as a desktop head names its UI: the page the app's web
/// host serves, at the address the host hands over, with the window's look read from the app and the UI's content.
/// </summary>
/// <remarks>
/// It runs the window's message loop on the host's STA main thread until the window closes or the run is canceled. It
/// keeps no tray, so a start in the tray ends at once. It uses no UI framework: Windows' own window, message box and
/// taskbar.
/// </remarks>
/// <param name="windowFit">The shape the web UI's layout needs of the window, which a page cannot state.</param>
public class WindowsWebViewUi(DesktopWindowFitOptions windowFit) : IDesktopUi
{
    // the running window, which BringToFront reaches from another thread; or the thread showing the
    // message in its place
    private volatile WebViewMainWindow? _window;
    private volatile uint _messageThreadId;
    private volatile VpnHoodApi? _api;

    /// <inheritdoc/>
    /// <remarks>
    /// A start in the tray, or a token canceled before the call, returns at once, as it keeps no tray. An access key the
    /// launch was given is imported before the window is made, and a Quick Launch connects beside it; a failure of either
    /// is logged. A cancel closes the window.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The window's icon could not be made.</exception>
    /// <exception cref="System.ComponentModel.Win32Exception">Windows refused the window.</exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled while the launch's access key was imported or the window's look
    /// was read.
    /// </exception>
    public void Run(DesktopUiParams uiParams, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || uiParams.StartHidden)
            return;

        _api = uiParams.Api;
        if (uiParams.AccessKey is { } accessKey)
            ImportAccessKey(uiParams.Api, accessKey, cancellationToken).GetAwaiter().GetResult();

        // beside the window, which a connect would keep waiting for its whole length
        if (uiParams.IsQuickLaunch)
            _ = QuickLaunch(uiParams.Api, cancellationToken);

        // What the window draws with, read before it is made: a window that changes colour under the
        // person is seen, one that appears a moment later is not. The calls do not come back to this
        // thread, which has no loop running yet.
        var info = uiParams.Api.App.GetInfo(cancellationToken).GetAwaiter().GetResult();
        var colors = ThemeColors.LoadAsync(uiParams.UiAssetProvider, info.Descriptor.UiTheme, cancellationToken).GetAwaiter().GetResult();

        var window = new WebViewMainWindow(new WebViewWindowParams {
            Api = uiParams.Api,
            ApiUrlProvider = uiParams.ApiUrlProvider,
            AppName = info.Descriptor.AppName,
            IsTv = info.Descriptor.IsTv,
            Colors = colors,
            WindowFit = windowFit,
            WebViewDataPath = Path.Combine(uiParams.UiDataPath, "WebView2")
        });

        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new Win32SynchronizationContext(window.Window));
        AppUiContext.Context = new Win32UiContext(window.Window);
        _window = window;
        try {
            using var registration = cancellationToken.Register(() => window.Window.Post(window.Close));
            window.Window.Show();
            Win32Window.RunMessageLoop();
        }
        finally {
            _window = null;
            AppUiContext.Context = null;
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It shows the host's text in a Windows message box with an OK button, as the web UI's words are its page's, which the
    /// app serves. A token canceled before the call returns at once.
    /// </remarks>
    public void RunMessage(DesktopUiMessageParams messageParams, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        var threadId = Win32MessageBox.CurrentThreadId;
        _messageThreadId = threadId;
        try {
            using var registration = cancellationToken.Register(() => Win32MessageBox.Close(threadId));
            Win32MessageBox.Show(messageParams.AppName, messageParams.Text, messageParams.Kind);
        }
        finally {
            _messageThreadId = 0;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It asks for the window, or the message box in its place; with neither, it does nothing. Where WebView2 is missing,
    /// the page opens in the browser. The task is already complete, and the token is not read.
    /// </remarks>
    public Task BringToFront(CancellationToken cancellationToken)
    {
        var messageThreadId = _messageThreadId;
        if (_window is { } window)
            window.Window.Post(window.ShowOrOpen);
        else if (messageThreadId != 0)
            Win32MessageBox.BringToFront(messageThreadId);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It asks the app to import the key and make its server the chosen one, which the page then shows; before a window
    /// runs, it does nothing. A key the app refuses, or a call that fails, is logged only.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public Task ImportAccessKey(string accessKey, CancellationToken cancellationToken)
    {
        return _api is { } api ? ImportAccessKey(api, accessKey, cancellationToken) : Task.CompletedTask;
    }

    // Through the app, the key's server made the chosen one: the page has the words, and it shows the server as it
    // reads the configuration again. One the app refuses is in the log only, as the page has no way to be told.
    private static async Task ImportAccessKey(VpnHoodApi api, string accessKey, CancellationToken cancellationToken)
    {
        try {
            var profile = await api.VpnProfiles.AddByAccessKey(accessKey.Trim(), cancellationToken).ConfigureAwait(false);
            var info = await api.App.GetInfo(cancellationToken).ConfigureAwait(false);
            var userSettings = info.UserSettings;
            userSettings.VpnProfileId = profile.VpnProfileId;
            await api.App.SetUserSettings(userSettings, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            VhLogger.Instance.LogError(ex, "Could not import the access key the app was opened with.");
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It asks the app to connect with its chosen location where it can connect; unlike the page's Connect, it does not
    /// adjust the location first. Before a window runs, it does nothing. A failure is logged, and the page shows it where
    /// the app keeps it as the state's last error.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public Task QuickLaunch(CancellationToken cancellationToken)
    {
        return _api is { } api ? QuickLaunch(api, cancellationToken) : Task.CompletedTask;
    }

    // Through the app, with its chosen server, as the page's Connect does when it has nothing to ask; the page shows
    // the connect as it reads the state. A failure is in the log, and in the state's last error the page shows.
    private static async Task QuickLaunch(VpnHoodApi api, CancellationToken cancellationToken)
    {
        try {
            var state = await api.App.GetState(cancellationToken).ConfigureAwait(false);
            if (state.CanConnect)
                await api.App.Connect(null, null, ConnectPlanId.Normal, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            VhLogger.Instance.LogError(ex, "Could not connect for a Quick Launch.");
        }
    }
}
