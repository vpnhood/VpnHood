using System.IO;
using System.Net;
using System.Windows;
using Microsoft.Extensions.Logging;
using VpnHood.Client.App.Resources;
using VpnHood.Client.App.WebServer;
using VpnHood.Client.App.Win.Common;
using VpnHood.Client.Device.WinDivert;
using VpnHood.Common.Logging;

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
            VpnHoodWinApp.Instance.PreStart(e.Args);

            // initialize VpnHoodApp
            VpnHoodApp.Init(new WinDivertDevice(), new AppOptions
            {
                Resource = DefaultAppResource.Resource,
                UpdateInfoUrl = new Uri("https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodClient-win-x64.json"),
                IsAddAccessKeySupported = true,
                UpdaterService = new WinAppUpdaterService(),
                SingleLineConsoleLog = false,
                LogAnonymous = !IsDebugMode
            });

            // initialize SPA
            ArgumentNullException.ThrowIfNull(DefaultAppResource.Resource.SpaZipData);
            using var spaResource = new MemoryStream(DefaultAppResource.Resource.SpaZipData);
            VpnHoodAppWebServer.Init(spaResource, url: VpnHoodWinApp.RegisterLocalDomain(new IPEndPoint(IPAddress.Parse("127.10.10.10"), 80), "myvpnhood"));

            // initialize Win
            VpnHoodWinApp.Instance.ExitRequested += (_, _) => Shutdown();
            VpnHoodWinApp.Instance.OpenMainWindowInBrowserRequested += (_, _) => VpnHoodWinApp.OpenUrlInExternalBrowser(VpnHoodAppWebServer.Instance.Url);
            VpnHoodWinApp.Instance.OpenMainWindowRequested += OpenMainWindowRequested;
            VpnHoodWinApp.Instance.Start();
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
        VpnHoodWinApp.Instance.Dispose();
        if (VpnHoodAppWebServer.IsInit) VpnHoodAppWebServer.Instance.Dispose();
        if (VpnHoodApp.IsInit) VpnHoodApp.Instance.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));

        base.OnExit(e);
    }

    public static bool IsDebugMode
    {
        get
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }
}