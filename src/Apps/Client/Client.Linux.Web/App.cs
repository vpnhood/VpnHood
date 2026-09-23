using VpnHood.AppLib;
using VpnHood.AppLib.Api.WebHost;
using VpnHood.AppUi.Hosting.Cli;
using VpnHood.AppUi.Hosting.Cli.Linux;
using VpnHood.AppLib.Services.Updaters;
using VpnHood.AppUi.Hosting.Avalonia.Desktop;
using VpnHood.AppUi.Presentation.Classic.Avalonia;
using VpnHood.Core.Toolkit.Assets;

// ReSharper disable LocalizableElement

namespace VpnHood.App.Client.Linux.Web;

// The Linux head, which is the two answers CliHost cannot give: the app to build when this
// process is the service, and the UI to show when it is the window. Everything else - which of the
// three this run is, the commands, the service - is the same on both Linux heads and lives in Hosting/Cli.
internal static class App
{
    private static Task<int> Main(string[] args)
    {
        return LinuxCliHost.Run(args, new CliHeadParams {
            AppOptionsFactory = CreateAppOptions,
            // this head takes access keys, so its profiles are the person's to manage
            IsAddAccessKeySupported = true,
            RunUi = (uiArgs, api, uiAssets) =>
                AvaloniaDesktopHost.Run<ClassicAvaloniaApp>(uiArgs, showWindow: true, api, uiAssets)
        }, CancellationToken.None);
    }

    private static AppOptions CreateAppOptions()
    {
        var appConfigs = AppConfigs.Load();

        // The files this build's asset packages placed beside the app, read the way this
        // platform reads them: the IP-location database and the UI's store. The app extracts
        // what it must under its storage - the store once, for the web host, which serves the
        // same entries at /assets/ to a paired phone's page and to the window beside it.
        var platformAssets = new FolderAssetProvider(AppContext.BaseDirectory);

        var appOptions = new AppOptions(appConfigs.AppId, "storage", AppConfigs.IsDebugMode) {
            AppName = AppConfigs.AppName,
            PrivacyPolicyUrl = appConfigs.PrivacyPolicyUrl,
            TermsOfUseUrl = appConfigs.TermsOfUseUrl,
            LogoAssetPath = appConfigs.LogoAssetPath,
            PrivacyConsentAssetName = appConfigs.PrivacyConsentAssetName,
            CompanyName = appConfigs.CompanyName,
            AccessKeys = appConfigs.DefaultAccessKey != null ? [appConfigs.DefaultAccessKey] : [],
            IsAddAccessKeySupported = true,
            AllowEndPointStrategy = true,
            DisconnectOnDispose = true,
            Ga4MeasurementId = appConfigs.Ga4MeasurementId,
            WebUiPort = appConfigs.WebUiPort,
            RemoteSettingsUrl = appConfigs.RemoteSettingsUrl,
            CustomData = appConfigs.CustomData,
            AllowEndPointTracker = appConfigs.AllowEndPointTracker,
            AllowRecommendUserReviewByServer = false,
            UpdaterOptions = new AppUpdaterOptions {
                UpdateInfoUrl = appConfigs.UpdateInfoUrl,
                PromptDelay = TimeSpan.Zero
            },
            LogServiceOptions = {
                SingleLineConsole = false
            },
            StorageFolderPath = new LinuxCliPaths().StoragePath,
            IpLocationZipAsset = new Asset(platformAssets, "iplocations/IpLocations.zip"),
            UiZipAssets = [new Asset(platformAssets, "assets/ui.zip")],
            // the page a paired phone opens: this same UI, as its browser build
            WebRootZipAsset = new Asset(platformAssets, "assets/web-root.zip"),
            WebHostFactory = new VpnHoodAppWebHostFactory()
        };

        return appOptions;
    }
}
