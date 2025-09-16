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
    public static VpnHoodAppWpfSpa Init(Func<AppOptions> optionsFactory,
        bool spaListenToAllIps,
        int? spaDefaultPort,
        string[] args)
    {
        try {
            // create instance
            var app = new VpnHoodAppWpfSpa();
            Application.Current.Exit += (_, _) => app.Dispose(false);

            // initialize Win App
            var appOptions = optionsFactory();
            appOptions.DeviceId ??= WindowsIdentity.GetCurrent().User?.Value;
            appOptions.EventWatcherInterval ??= TimeSpan.FromSeconds(1);

            // initialize VpnHoodWinApp
            VpnHoodAppWin.Init(appOptions, args: args ?? []);

            // register local domain & initialize SPA
            var localSpaUrl = !string.IsNullOrEmpty(appOptions.LocalSpaHostName)
                ? VpnHoodAppWin.RegisterLocalDomain(new IPEndPoint(IPAddress.Parse("127.10.10.10"), spaDefaultPort ?? 80), appOptions.LocalSpaHostName)
                : null;

            VpnHoodAppWebServer.Init(new WebServerOptions {
                DefaultPort = spaDefaultPort,
                ListenOnAllIps = spaListenToAllIps,
                Url = localSpaUrl
            });

            // initialize Win
            VpnHoodAppWin.Instance.ExitRequested += (_, _) => Application.Current.Shutdown();
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

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            if (VpnHoodAppWin.IsInit)
                VpnHoodAppWin.Instance.Dispose();

            if (VpnHoodAppWebServer.IsInit)
                VpnHoodAppWebServer.Instance.Dispose();
        }

        base.Dispose(disposing);
    }
}