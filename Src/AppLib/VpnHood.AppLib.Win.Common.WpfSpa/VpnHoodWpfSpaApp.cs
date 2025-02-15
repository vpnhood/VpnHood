using System.IO;
using System.Net;
using System.Security.Principal;
using System.Windows;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.WebServer;
using VpnHood.Core.Client.Device.WinDivert;
using VpnHood.Core.Common.Logging;

namespace VpnHood.AppLib.Win.Common.WpfSpa;

// ReSharper disable once RedundantExtendsListEntry
public abstract class VpnHoodWpfSpaApp : Application
{
    protected abstract AppOptions CreateAppOptions();
    public abstract bool SpaListenToAllIps { get; }
    public abstract int? SpaDefaultPort { get; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try {
            // initialize Ain App
            var appOptions = CreateAppOptions();
            appOptions.DeviceId ??= WindowsIdentity.GetCurrent().User?.Value;

            VpnHoodWinApp.Init(appOptions.AppId, appOptions.StorageFolderPath);

            // check command line
            VpnHoodWinApp.Instance.PreStart(e.Args);

            // initialize VpnHoodApp
            VpnHoodApp.Init(new WinDevice(appOptions.StorageFolderPath, appOptions.Tracker, appOptions.IsDebugMode), appOptions);

            // initialize SPA
            ArgumentNullException.ThrowIfNull(VpnHoodApp.Instance.Resource.SpaZipData);
            using var spaResource = new MemoryStream(VpnHoodApp.Instance.Resource.SpaZipData);
            var localSpaUrl = !string.IsNullOrEmpty(appOptions.LocalSpaHostName)
                ? VpnHoodWinApp.RegisterLocalDomain(
                    new IPEndPoint(IPAddress.Parse("127.10.10.10"), SpaDefaultPort ?? 80), appOptions.LocalSpaHostName)
                : null;

            VpnHoodAppWebServer.Init(new WebServerOptions {
                SpaZipStream = spaResource,
                DefaultPort = SpaDefaultPort,
                ListenOnAllIps = SpaListenToAllIps,
                Url = localSpaUrl
            });

            // initialize Win
            VpnHoodWinApp.Instance.ExitRequested += (_, _) => Shutdown();
            VpnHoodWinApp.Instance.OpenMainWindowInBrowserRequested += (_, _) =>
                VpnHoodWinApp.OpenUrlInExternalBrowser(VpnHoodAppWebServer.Instance.Url);
            VpnHoodWinApp.Instance.OpenMainWindowRequested += OpenMainWindowRequested;
            VpnHoodWinApp.Instance.Start();

            // run the app
            var mainWindow = new VpnHoodWpfSpaMainWindow();
            mainWindow.Show();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not run the app.");
            Shutdown();
            throw;
        }
    }

    private void OpenMainWindowRequested(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() => {
            if (MainWindow == null)
                return;

            MainWindow.Show();
            MainWindow.Activate();
            MainWindow.WindowState = WindowState.Normal;
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        VpnHoodWinApp.Instance.Dispose();
        if (VpnHoodAppWebServer.IsInit) VpnHoodAppWebServer.Instance.Dispose();
        if (VpnHoodApp.IsInit) VpnHoodApp.Instance.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));

        base.OnExit(e);
    }
}