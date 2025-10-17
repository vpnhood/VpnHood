using System.Net;
using System.Security.Principal;
using System.Windows;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.WebServer;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Win.Common.WpfSpa;

// ReSharper disable once RedundantExtendsListEntry
public class VpnHoodAppWpfSpa : Singleton<VpnHoodAppWpfSpa>
{
    public static VpnHoodAppWpfSpa Init(Func<AppOptions> optionsFactory, string[] args)
    {
        try {
            // create instance
            var app = new VpnHoodAppWpfSpa();
            Application.Current.Exit += (_, _) => Exit();

            // initialize Win App
            var appOptions = optionsFactory();
            appOptions.DeviceId ??= WindowsIdentity.GetCurrent().User?.Value;
            appOptions.DeviceUiProvider = new WinDeviceUiProvider();
            appOptions.EventWatcherInterval ??= TimeSpan.FromSeconds(1);

            // register local domain if needed
            var alternativeUrl = string.IsNullOrEmpty(appOptions.WebUiHostName)
                ? null : VpnHoodAppWin.RegisterLocalDomain(IPEndPoint.Parse("127.10.10.10:80"), appOptions.WebUiHostName);

            // initialize VpnHoodWinApp
            VpnHoodAppWin.Init(appOptions, args: args);
            VpnHoodAppWebServer.Init(new WebServerOptions { Url = alternativeUrl });

            // initialize Win
            VpnHoodAppWin.Instance.ExitRequested += (_, _) => Exit();
            VpnHoodAppWin.Instance.OpenMainWindowInBrowserRequested += (_, _) =>
                VpnHoodAppWin.OpenUrlInExternalBrowser(VpnHoodAppWebServer.Instance.Url);
            VpnHoodAppWin.Instance.OpenMainWindowRequested += OpenMainWindowRequested;
            VpnHoodAppWin.Instance.Start();

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
        if (VpnHoodAppWpfSpa.IsInit)
            VpnHoodAppWpfSpa.Instance.Dispose();
        Application.Current.Shutdown();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            if (VpnHoodAppWebServer.IsInit)
                VpnHoodAppWebServer.Instance.Dispose();

            if (VpnHoodAppWin.IsInit)
                VpnHoodAppWin.Instance.Dispose();
        }

        base.Dispose(disposing);
    }
}