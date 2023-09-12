using System;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Windows;
using VpnHood.Client.App.Resources;
using VpnHood.Client.App.UI;
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
            WinApp.Instance.PreStart(e.Args);

            // initialize VpnHoodApp
            var appProvider = new VpnHoodAppProvider();
            using var spaResource = new MemoryStream(UiResource.SPA);
            VpnHoodApp.Init(appProvider);
            VpnHoodAppUi.Init(spaResource, url2: appProvider.AdditionalUiUrl);

            // initialize Win
            WinApp.Instance.ExitRequested += (_, _) => Shutdown();
            WinApp.Instance.OpenMainWindowInBrowserRequested += (_, _) => WinApp.OpenUrlInExternalBrowser(VpnHoodAppUi.Instance.Url);
            WinApp.Instance.OpenMainWindowRequested += OpenMainWindowRequested;
            WinApp.Instance.Start();
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Could not run the app.");
            Shutdown();
        }
    }

    private void OpenMainWindowRequested(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            MainWindow?.Show();
            MainWindow?.Activate();
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        WinApp.Instance.Dispose();
        if (VpnHoodAppUi.IsInit) VpnHoodAppUi.Instance.Dispose();
        if (VpnHoodApp.IsInit) VpnHoodApp.Instance.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));

        base.OnExit(e);
    }
}