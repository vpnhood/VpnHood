using System.Windows;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.AppLib.Win.Common;

namespace VpnHood.AppUi.Hosting.WebView.Windows;

// The web UI in WPF's window, on the app VpnHoodAppWin.Init has started: the window, and what
// WPF answers of the tray's requests - opening the window, exiting. Called from the WPF
// application's startup, where a window can be made.
// ReSharper disable once RedundantExtendsListEntry
public class VpnHoodAppWpf : Singleton<VpnHoodAppWpf>
{
    public static VpnHoodAppWpf Init()
    {
        try {
            // create instance
            var app = new VpnHoodAppWpf();
            Application.Current.Exit += (_, _) => Exit();
            VpnHoodAppWin.Instance.ExitRequested += (_, _) => Exit();
            VpnHoodAppWin.Instance.OpenMainWindowRequested += OpenMainWindowRequested;

            // run the app
            var mainWindow = new VpnHoodWpfMainWindow();
            mainWindow.Show();

            return app;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not run the app.");
            Application.Current.Shutdown();
            throw;
        }
    }

    // Edge WebView2 is missing, so no window of this app can draw its UI. Only the main window can
    // find that out, and only this branch cares: an Avalonia head draws its own window and never has
    // this failure. From here on the tray's Open goes to the system browser.
    public static bool IsWebViewUnavailable { get; private set; }

    public static void NotifyWebViewUnavailable()
    {
        IsWebViewUnavailable = true;
    }

    // The UI in the system browser: the one way left to show this app when no window can draw it.
    // Off the caller's thread, since the host may still have to unpack its web root and bind before
    // it has an address.
    public static async Task OpenMainWindowInBrowser()
    {
        try {
            var webHost = VpnHoodApp.Instance.LocalWebHost ??
                          throw new InvalidOperationException("This app was given no web host.");
            VpnHoodAppWin.OpenUrlInExternalBrowser(await webHost.EnsureStarted(CancellationToken.None).Vhc());
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not open the main window in the system browser.");
        }
    }

    private static void OpenMainWindowRequested(object? sender, EventArgs e)
    {
        if (IsWebViewUnavailable) {
            _ = OpenMainWindowInBrowser();
            return;
        }

        Application.Current.Dispatcher.Invoke(() => {
            var mainWindow = Application.Current.MainWindow;
            if (mainWindow == null)
                return;

            mainWindow.Show();
            mainWindow.Activate();
            mainWindow.WindowState = WindowState.Normal;
        });
    }

    private static void Exit()
    {
        if (IsInit)
            Instance.Dispose();
        Application.Current.Shutdown();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && VpnHoodAppWin.IsInit)
            VpnHoodAppWin.Instance.Dispose();

        base.Dispose(disposing);
    }
}
