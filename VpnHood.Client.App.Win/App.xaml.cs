using Microsoft.Extensions.Logging;
using System.IO;
using System.Windows;
using VpnHood.Client.App.Resources;
using VpnHood.Client.App.WebServer;
using VpnHood.Client.App.Win.Common;
using VpnHood.Common.Logging;
using System.Net;
using VpnHood.Client.Device.WinDivert;

namespace VpnHood.Client.App.Win;

// ReSharper disable once RedundantExtendsListEntry
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            // check command line
            WinApp.Instance.PreStart(e.Args);

            // initialize VpnHoodApp
            VpnHoodApp.Init(new WinDivertDevice(), new AppOptions
            {
                Resources = VpnHoodAppResource.Resources,
                UpdateInfoUrl = new Uri("https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodClient-win-x64.json")
            });

            // initialize SPA
            ArgumentNullException.ThrowIfNull(VpnHoodAppResource.Resources.SpaZipData);
            using var spaResource = new MemoryStream(VpnHoodAppResource.Resources.SpaZipData);
            VpnHoodAppWebServer.Init(spaResource, url2: WinApp.RegisterLocalDomain(new IPEndPoint(IPAddress.Parse("127.10.10.10"), 80), "myvpnhood"));

            // initialize Win
            WinApp.Instance.ExitRequested += (_, _) => Shutdown();
            WinApp.Instance.OpenMainWindowInBrowserRequested += (_, _) => WinApp.OpenUrlInExternalBrowser(VpnHoodAppWebServer.Instance.Url);
            WinApp.Instance.OpenMainWindowRequested += OpenMainWindowRequested;
            WinApp.Instance.Start();
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Could not run the app.");
            Shutdown();
            throw;
        }
    }

    private void OpenMainWindowRequested(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (MainWindow == null)
                return;

            MainWindow.Show();
            MainWindow.Activate();
            MainWindow.WindowState = WindowState.Normal;
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        WinApp.Instance.Dispose();
        if (VpnHoodAppWebServer.IsInit) VpnHoodAppWebServer.Instance.Dispose();
        if (VpnHoodApp.IsInit) VpnHoodApp.Instance.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));

        base.OnExit(e);
    }
}