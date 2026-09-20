using System.Security.Principal;
using System.Windows;
using Microsoft.Extensions.Logging;
using VpnHood.AppUi.Presentation.Classic.Avalonia;
using VpnHood.AppLib;
using VpnHood.AppUi.Hosting.Avalonia.Desktop;
using VpnHood.AppLib.Services.Updaters;
using VpnHood.AppLib.Utils;
using VpnHood.AppLib.Win.Common;
using VpnHood.AppUi.Hosting.WebView.Windows;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.AppLib.Api.WebHost;
using VpnHood.Core.Toolkit.Assets;

namespace VpnHood.App.Client.Win.Web;

public class App : Application
{
    private static AppOptions CreateAppOptions()
    {
        var appConfigs = AppConfigs.Load();

        // The files this build's asset packages placed beside the app, read the way this
        // platform reads them: the IP-location database and the UI's store. The app extracts
        // what it must under its storage - the store once, for the in-process UI and for the
        // web host, which serves the same entries at /assets/ to a paired phone's page.
        var platformAssets = new FolderAssetProvider(AppContext.BaseDirectory);

        var options = new AppOptions(appConfigs.AppId, appConfigs.StorageFolderName, AppConfigs.IsDebugMode) {
            AppName = AppConfigs.AppName,
            DeviceId = WindowsIdentity.GetCurrent().User?.Value,
            PrivacyPolicyUrl = appConfigs.PrivacyPolicyUrl,
            TermsOfUseUrl = appConfigs.TermsOfUseUrl,
            CustomData = appConfigs.CustomData,
            AccessKeys = appConfigs.DefaultAccessKey != null ? [appConfigs.DefaultAccessKey] : [],
            IsAddAccessKeySupported = true,
            RemoteSettingsUrl = appConfigs.RemoteSettingsUrl,
            AllowEndPointTracker = appConfigs.AllowEndPointTracker,
            Ga4MeasurementId = appConfigs.Ga4MeasurementId,
            WebUiPort = appConfigs.WebUiPort,
            AllowRecommendUserReviewByServer = false,
            LogServiceOptions = {
                SingleLineConsole = false
            },
            UpdaterOptions = new AppUpdaterOptions {
                UpdateInfoUrl = appConfigs.UpdateInfoUrl,
                UpdaterProvider = new AdvancedInstallerUpdaterProvider(),
                PromptDelay = TimeSpan.FromDays(1)
            },
            IpLocationZipAsset = new Asset(platformAssets, "iplocations/IpLocations.zip"),
            UiZipAsset = new Asset(platformAssets, "assets/ui.zip"),
            // the page a paired phone opens, and this head's own web view: the Avalonia UI's browser build
            WebRootZipAsset = AppWebRoot.Zip,
            WebHostFactory = new VpnHoodAppWebHostFactory()
        };
        return options;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // call base first to init app resources
        base.OnStartup(e);

        // the web UI, in this application's window
        VpnHoodAppWpf.Init();
    }

    [STAThread]
    public static void Main(string[] args)
    {
        // The app first, on its own; then the UI framework by the app's own setting: WPF hosts the
        // web UI, Avalonia the native one when the debug command forces it.
        try {
            VpnHoodAppWin.Init(CreateAppOptions, args);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not run the app.");
            return;
        }

        if (VpnHoodApp.Instance.HasDebugCommand(DebugCommands.AvaloniaUi))
            RunAvaloniaUi(args);
        else
            new App().Run();
    }

    // The Avalonia UI in its own window, opened from the tray as the web UI's is; Exit there ends
    // it, with the app.
    private static void RunAvaloniaUi(string[] args)
    {
        var appWin = VpnHoodAppWin.Instance;
        appWin.OpenMainWindowRequested += (_, _) => AvaloniaDesktopHost.ShowMainWindow();
        appWin.ExitRequested += (_, _) => AvaloniaDesktopHost.Shutdown();
        try {
            AvaloniaDesktopHost.Run<ClassicAvaloniaApp>(args, appWin.ShowWindowAfterStart);
        }
        finally {
            appWin.Dispose();
        }
    }
}