using Microsoft.Extensions.Logging;
using System.Diagnostics;
using VpnHood.App.Client;
using VpnHood.AppLib;
using VpnHood.AppLib.Linux.Common;
using VpnHood.AppLib.Services.Updaters;
using VpnHood.AppLib.WebServer;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.App.Connect.Linux.Web;

internal class App
{
    public static string StoragePath => Path.Combine(
        Path.GetDirectoryName(Path.GetDirectoryName(Environment.ProcessPath)!)!, "storage");

    private static AppOptions CreateAppOptions()
    {
        var appConfigs = AppConfigs.Load();
        var resources = ConnectAppResources.Resources;
        resources.Strings.AppName = AppConfigs.AppName;
        return new AppOptions(appId: appConfigs.AppId, Path.GetDirectoryName(StoragePath)!, AppConfigs.IsDebugMode) {
            CustomData = appConfigs.CustomData,
            UiName = "VpnHoodConnect",
            Resources = resources,
            AccessKeys = appConfigs.DefaultAccessKey != null ? [appConfigs.DefaultAccessKey] : [],
            IsAddAccessKeySupported = false,
            AllowEndPointTracker = appConfigs.AllowEndPointTracker,
            Ga4MeasurementId = appConfigs.Ga4MeasurementId,
            WebUiPort = appConfigs.WebUiPort,
            RemoteSettingsUrl = appConfigs.RemoteSettingsUrl,
            PremiumFeatures = ConnectAppResources.PremiumFeatures,
            AllowRecommendUserReviewByServer = true,
            LogServiceOptions = {
                SingleLineConsole = false
            },
            UpdaterOptions = new AppUpdaterOptions {
                UpdateInfoUrl = appConfigs.UpdateInfoUrl,
                PromptDelay = TimeSpan.FromDays(1)
            },
            StorageFolderPath = StoragePath
        };
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

        // init webserver
        VpnHoodAppWebServer.Init(new WebServerOptions());
        VpnHoodAppLinux.Instance.OpenMainWindowRequested += (_, _) => OpenMainWindow(VpnHoodAppWebServer.Instance.Url);

        // write service url
        File.WriteAllText(serviceUrlPath, VpnHoodAppWebServer.Instance.Url.ToString());

        // run app
        return VpnHoodAppLinux.Instance.Run();
    }

    private static void OpenMainWindow(Uri url)
    {
        VhLogger.Instance.LogInformation("Running the default browser...");
        Process.Start(new ProcessStartInfo {
            FileName = url.AbsoluteUri,
            UseShellExecute = true,
        });

        VhLogger.Instance.LogInformation("To open VpnHood UI navigate to {0}",
            url);
    }

    private static void InstanceOnExiting(object? sender, EventArgs e)
    {
        if (VpnHoodAppWebServer.IsInit)
            VpnHoodAppWebServer.Instance.Dispose();
    }
}