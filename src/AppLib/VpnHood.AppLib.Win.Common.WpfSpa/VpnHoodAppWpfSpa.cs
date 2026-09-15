using System.Windows;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Win.Common.WpfSpa;

// The web UI in WPF's window, on the app VpnHoodAppWin.Init has started: the window, and what
// WPF answers of the tray's requests - opening the window, exiting. Called from the WPF
// application's startup, where a window can be made.
// ReSharper disable once RedundantExtendsListEntry
public class VpnHoodAppWpfSpa : Singleton<VpnHoodAppWpfSpa>
{
    public static VpnHoodAppWpfSpa Init()
    {
        try {
            // create instance
            var app = new VpnHoodAppWpfSpa();
            Application.Current.Exit += (_, _) => Exit();
            VpnHoodAppWin.Instance.ExitRequested += (_, _) => Exit();
            VpnHoodAppWin.Instance.OpenMainWindowRequested += OpenMainWindowRequested;

            // run the app
            var mainWindow = new VpnHoodWpfSpaMainWindow();
            mainWindow.Show();

            return app;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not run the app.");
            Application.Current.Shutdown();
            throw;
        }
    }

    private static void OpenMainWindowRequested(object? sender, EventArgs e)
    {
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
