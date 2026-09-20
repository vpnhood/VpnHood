using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VpnHood.AppUi.Presentation.Classic.Avalonia;
using VpnHood.AppLib;
using VpnHood.AppUi.Hosting.Avalonia.Desktop;
using VpnHood.AppLib.Linux.Common;
using VpnHood.AppLib.Services.Updaters;
using VpnHood.AppLib.Utils;
using VpnHood.AppLib.Api.WebHost;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Assets;

// ReSharper disable LocalizableElement

namespace VpnHood.App.Client.Linux.Web;

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

        var appOptions = new AppOptions(appConfigs.AppId, "storage", AppConfigs.IsDebugMode) {
            AppName = AppConfigs.AppName,
            PrivacyPolicyUrl = appConfigs.PrivacyPolicyUrl,
            TermsOfUseUrl = appConfigs.TermsOfUseUrl,
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
            StorageFolderPath = StoragePath,
            IpLocationZipAsset = new Asset(platformAssets, "iplocations/IpLocations.zip"),
            UiZipAsset = new Asset(platformAssets, "assets/ui.zip"),
            // the page a paired phone opens, and this head's own web view: the Avalonia UI's browser build
            WebRootZipAsset = AppWebRoot.Zip,
            WebHostFactory = new VpnHoodAppWebHostFactory()
        };

        return appOptions;
    }

    private static async Task Main(string[] args)
    {
        Console.WriteLine("Starting VpnHood Client for linux (Beta).");
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
            VhLogger.Instance.LogInformation("Another instance is running.");

            // load existing url
            if (File.Exists(serviceUrlPath)) {
                var url = File.ReadAllText(serviceUrlPath);
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    OpenMainWindow(uri);
            }

            VhLogger.Instance.LogInformation("Another instance is running");
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

        VhLogger.Instance.LogInformation("To open VpnHood UI navigate to {0}", url);
    }

    private static void InstanceOnExiting(object? sender, EventArgs e)
    {
        // the app owns its web host and disposes it with itself
    }
}