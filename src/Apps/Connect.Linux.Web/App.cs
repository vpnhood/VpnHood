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
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.App.Connect.Linux.Web;

internal static class App
{
    public static string StoragePath => Path.Combine(
        Path.GetDirectoryName(Path.GetDirectoryName(Environment.ProcessPath)!)!, "storage");

    private static AppOptions CreateAppOptions()
    {
        var appConfigs = AppConfigs.Load();
        var resources = ConnectAppResources.Resources;
        var appOptions = new AppOptions(appId: appConfigs.AppId, Path.GetDirectoryName(StoragePath)!, AppConfigs.IsDebugMode) {
            AppName = AppConfigs.AppName,
            // The listener a phone pairs with; without it IsRemoteAccessSupported is false.
            RemoteAccessHostProvider = () => VpnHoodAppWebHost.Instance,
            IpLocationZipData = ConnectAppResources.IpLocationZipData,
            CustomData = appConfigs.CustomData,
            UiName = "VpnHoodConnect",
            Resources = resources,
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
            StorageFolderPath = StoragePath
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

    private static Task Main(string[] args)
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
            return Task.CompletedTask;
        }
        catch (AnotherInstanceIsRunningException) {
            Console.WriteLine($"An instance of {AppConfigs.AppTitle} is running.");

            // load existing url
            if (File.Exists(serviceUrlPath)) {
                var url = File.ReadAllText(serviceUrlPath);
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    OpenMainWindow(uri);
            }

            return Task.CompletedTask;
        }

        // the web host, started now: the UI is a browser, and its address is written below
        VpnHoodAppWebHost.Init(VpnHoodApp.Instance, new WebHostOptions {
            WebRootZip = ConnectAppResources.GetWebRootZip(VpnHoodApp.Instance.HasDebugCommand(DebugCommands.AvaloniaUi))
        }).Start();

        // write service url
        File.WriteAllText(serviceUrlPath, VpnHoodAppWebHost.Instance.Url.ToString());

        // run app: the Avalonia UI in a window on this thread when the debug command forces it,
        // otherwise the web UI, in the browser
        if (VpnHoodApp.Instance.HasDebugCommand(DebugCommands.AvaloniaUi))
            return RunAvaloniaUi(args);

        VpnHoodAppLinux.Instance.OpenMainWindowRequested += (_, _) => OpenMainWindow(VpnHoodAppWebHost.Instance.Url);
        return VpnHoodAppLinux.Instance.Run();
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
        if (VpnHoodAppWebHost.IsInit)
            VpnHoodAppWebHost.Instance.Dispose();
    }
}