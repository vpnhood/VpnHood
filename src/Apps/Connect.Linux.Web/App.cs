using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VpnHood.App.Client;
using VpnHood.AppUi.Presentation.Classic.Avalonia;
using VpnHood.AppLib.Api.Accounts;
using VpnHood.AppLib;
using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppUi.Hosting.Avalonia.Desktop;
using VpnHood.AppLib.Api.Premium;
using VpnHood.AppLib.Linux.Common;
using VpnHood.AppLib.Portal;
using VpnHood.AppLib.Services.Updaters;
using VpnHood.AppLib.Utils;
using VpnHood.AppLib.Api.WebHost;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Assets;

namespace VpnHood.App.Connect.Linux.Web;

internal static class App
{
    public static string StoragePath => Path.Combine(
        Path.GetDirectoryName(Path.GetDirectoryName(Environment.ProcessPath)!)!, "storage");

    private static AppOptions CreateAppOptions()
    {
        var appConfigs = AppConfigs.Load();

        // The files this build's asset packages placed beside the app, read the way this
        // platform reads them: the IP-location database and the UI's store. The app extracts
        // what it must under its storage - the store once, for the in-process UI and for the
        // web host, which serves the same entries at /assets/ to a paired phone's page.
        var platformAssets = new FolderAssetProvider(AppContext.BaseDirectory);

        var appOptions = new AppOptions(appId: appConfigs.AppId, Path.GetDirectoryName(StoragePath)!, AppConfigs.IsDebugMode) {
            AppName = AppConfigs.AppName,
            CustomData = appConfigs.CustomData,
            UiTheme = "violet",
            PrivacyPolicyUrl = appConfigs.PrivacyPolicyUrl,
            TermsOfUseUrl = appConfigs.TermsOfUseUrl,
            AccessKeys = appConfigs.DefaultAccessKey != null ? [appConfigs.DefaultAccessKey] : [],
            IsAddAccessKeySupported = false,
            AllowEndPointTracker = appConfigs.AllowEndPointTracker,
            Ga4MeasurementId = appConfigs.Ga4MeasurementId,
            WebUiPort = appConfigs.WebUiPort,
            RemoteSettingsUrl = appConfigs.RemoteSettingsUrl,
            AllowRecommendUserReviewByServer = true,
            LogServiceOptions = {
                SingleLineConsole = false
            },
            Premium = new AppPremiumOptions {
                Features = ConnectAppResources.PremiumFeatures,
                // nothing forbids a typed code on this channel (App Review 3.1.1 binds the App Store head only)
                AllowImportAccessCode = true,
                // not shipped through a store, so an operator may point its buyers at its own shop
                IsPurchaseUrlSupported = true
            },
            UpdaterOptions = new AppUpdaterOptions {
                UpdateInfoUrl = appConfigs.UpdateInfoUrl,
                PromptDelay = TimeSpan.FromDays(1)
            },
            StorageFolderPath = StoragePath,
            IpLocationZipAsset = new Asset(platformAssets, "iplocations/IpLocations.zip"),
            UiZipAsset = new Asset(platformAssets, "assets/ui.zip"),
            // the page a paired phone opens, and this head's own web view: the Avalonia UI's browser build
            WebRootZipAsset = AppWebRoot.Zip,
            WebHostFactory = new VpnHoodAppWebHostFactory()
        };

        appOptions.AccountProvider = CreateAppAccountProvider(appConfigs, appOptions.StorageFolderPath);
        return appOptions;
    }

    private static IAccountProvider? CreateAppAccountProvider(AppConfigs appConfigs, string storageFolderPath)
    {
        try {
            // no Portal configured — ship without account features rather than half-wired ones
            if (appConfigs.PortalBaseUri == null) {
                VhLogger.Instance.LogWarning("PortalBaseUri is not configured. Account features are disabled.");
                return null;
            }

            // no external identity provider on this head: the portal's own password sign-in serves
            var portalAuthenticationProvider = new PortalAuthenticationProvider(storageFolderPath,
                appConfigs.PortalBaseUri, appConfigs.AppId, [],
                ignoreSslVerification: appConfigs.PortalIgnoreSslVerification);

            // the web-distribution store: plans priced by the portal, checkout in the browser.
            // xdg-open, not UseShellExecute: on Linux the latter does not resolve URLs.
            var webBillingProvider = new PortalWebBillingProvider(appConfigs.PortalBaseUri, appConfigs.AppId,
                openUrl: (_, url, _) => {
                    Process.Start(new ProcessStartInfo { FileName = "xdg-open", ArgumentList = { url.AbsoluteUri } });
                    return Task.CompletedTask;
                },
                ignoreSslVerification: appConfigs.PortalIgnoreSslVerification);

            return new PortalAccountProvider(portalAuthenticationProvider, billingProvider: webBillingProvider,
                portalBaseUrl: appConfigs.PortalBaseUri, packageName: appConfigs.AppId,
                ignoreSslVerification: appConfigs.PortalIgnoreSslVerification);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not create AccountService.");
            return null;
        }
    }

    private static async Task Main(string[] args)
    {
        Console.WriteLine($"Starting {AppConfigs.AppTitle} for linux (Beta).");
        Console.WriteLine("Only WebUI supported at this time.");
        var serviceUrlPath = Path.Combine(StoragePath, "service_url.txt");

        // init VpnHood app
        try {
            VpnHoodAppLinux.Init(CreateAppOptions, args);
            VpnHoodAppLinux.Instance.Exiting += InstanceOnExiting;
        }
        catch (GracefullyShutdownException) {
            VhLogger.Instance.LogInformation("Exit due to stop command.");
            return;
        }
        catch (AnotherInstanceIsRunningException) {
            Console.WriteLine($"An instance of {AppConfigs.AppTitle} is running.");

            // load existing url
            if (File.Exists(serviceUrlPath)) {
                var url = File.ReadAllText(serviceUrlPath);
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    OpenMainWindow(uri);
            }

            return;
        }

        // The UI here is a browser, so the host is wanted at once: its address goes in the service
        // file, which is how a second launch finds the window to open.
        var webHost = VpnHoodApp.Instance.LocalWebHost ??
                      throw new InvalidOperationException("This app was given no web host.");
        var webHostUrl = await webHost.EnsureStarted(CancellationToken.None);
        File.WriteAllText(serviceUrlPath, webHostUrl.ToString());

        // run app: the Avalonia UI in a window on this thread when the debug command forces it,
        // otherwise the web UI, in the browser
        if (VpnHoodApp.Instance.HasDebugCommand(DebugCommands.AvaloniaUi)) {
            await RunAvaloniaUi(args).Vhc();
            return;
        }

        VpnHoodAppLinux.Instance.OpenMainWindowRequested += (_, _) => OpenMainWindow(webHostUrl);
        await VpnHoodAppLinux.Instance.Run().Vhc();
    }

    // The Avalonia UI in a window: opened again by a second launch, as the browser is, and ended
    // by the stop command, with the app.
    private static Task RunAvaloniaUi(string[] args)
    {
        var appLinux = VpnHoodAppLinux.Instance;
        appLinux.OpenMainWindowRequested += (_, _) => AvaloniaDesktopHost.ShowMainWindow();
        appLinux.Exiting += (_, _) => AvaloniaDesktopHost.Shutdown();
        appLinux.PrepareAsync().GetAwaiter().GetResult();
        AvaloniaDesktopHost.Run<ClassicAvaloniaApp>(args, appLinux.ShowWindowAfterStart);
        return Task.CompletedTask;
    }

    private static void OpenMainWindow(Uri url)
    {
        VhLogger.Instance.LogInformation("Running the default browser...");
        Process.Start(new ProcessStartInfo {
            FileName = url.AbsoluteUri,
            UseShellExecute = true
        });

        VhLogger.Instance.LogInformation("To open VpnHood UI navigate to {0}",
            url);
    }

    private static void InstanceOnExiting(object? sender, EventArgs e)
    {
        // the app owns its web host and disposes it with itself
    }
}