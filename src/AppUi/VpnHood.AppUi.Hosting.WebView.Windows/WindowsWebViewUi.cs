using System.IO;
using VpnHood.AppLib.App;
using VpnHood.AppLib.App.Branding;
using VpnHood.AppUi.Hosting.Abstractions;
using VpnHood.AppUi.Hosting.WebView.Windows.WinNative;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;

namespace VpnHood.AppUi.Hosting.WebView.Windows;

// The SPA in a Win32 window over WebView2, as a desktop head names its UI (DesktopInitParams.Ui): the
// page the app's web host serves - the service's, at the address the host hands over - with the
// window's look read from the app and from the UI's store. It runs the window's message loop on the
// host's STA main thread until the run is cancelled, or, where no tray keeps it, until the window
// closes. No UI framework: Windows' own window, message box and taskbar.
public class WindowsWebViewUi : IDesktopUi
{
    // the running window, which BringToFront reaches from another thread; or the thread showing the
    // message in its place
    private volatile WebViewMainWindow? _window;
    private volatile uint _messageThreadId;

    public void Run(DesktopUiParams uiParams, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        // What the window draws with, read before it is made: a window that changes colour under the
        // person is seen, one that appears a moment later is not. The calls do not come back to this
        // thread, which has no loop running yet.
        var info = uiParams.Api.App.GetInfo(cancellationToken).GetAwaiter().GetResult();
        var resources = new AppResources();
        if (uiParams.UiAssetProvider != null)
            AppBranding.LoadAsync(resources, uiParams.UiAssetProvider, info.Features.UiTheme).GetAwaiter().GetResult();

        var window = new WebViewMainWindow(new WebViewWindowParams {
            Api = uiParams.Api,
            WebHost = new ExternalAppWebHost(uiParams.ApiUrlProvider),
            AppName = info.Features.AppName,
            IsTv = info.Features.IsTv,
            Resources = resources,
            WebViewDataPath = Path.Combine(uiParams.UiDataPath, "WebView2"),
            ExitOnClose = uiParams.ExitOnClose,
            StartHidden = uiParams.StartHidden
        });

        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new Win32SynchronizationContext(window.Window));
        AppUiContext.Context = new Win32UiContext(window.Window);
        _window = window;
        try {
            using var registration = cancellationToken.Register(() => window.Window.Post(window.Close));
            if (!uiParams.StartHidden)
                window.Window.Show();

            Win32Window.RunMessageLoop();
        }
        finally {
            _window = null;
            AppUiContext.Context = null;
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    // In the host's own English: the web UI's words are its page's, which the app serves.
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

    public Task BringToFront(CancellationToken cancellationToken)
    {
        var messageThreadId = _messageThreadId;
        if (_window is { } window)
            window.Window.Post(window.ShowOrOpen);
        else if (messageThreadId != 0)
            Win32MessageBox.BringToFront(messageThreadId);

        return Task.CompletedTask;
    }
}
