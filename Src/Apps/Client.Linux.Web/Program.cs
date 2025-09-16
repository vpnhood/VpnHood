using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib;
using VpnHood.AppLib.Services.Updaters;
using VpnHood.AppLib.WebServer;
using VpnHood.Core.Proxies;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.App.Client.Linux.Web;

internal class Program
{
    private static AppOptions CreateAppOptions()
    {
        var appConfigs = AppConfigs.Load();
        var resources = ClientAppResources.Resources;
        resources.Strings.AppName = AppConfigs.AppName;
        var appOptions = new AppOptions(appConfigs.AppId, AppConfigs.AppName, AppConfigs.IsDebugMode) {
            Resources = resources,
            AccessKeys = AppConfigs.IsDebug ? [appConfigs.DefaultAccessKey] : [],
            IsAddAccessKeySupported = true,
            IsLocalNetworkSupported = true,
            AllowEndPointStrategy = true,
            DisconnectOnDispose = true,
            AllowRecommendUserReviewByServer = false,
            UpdaterOptions = new AppUpdaterOptions {
                UpdateInfoUrl = appConfigs.UpdateInfoUrl,
                PromptDelay = TimeSpan.Zero,
            },
            LogServiceOptions = {
                SingleLineConsole = false
            }
        };

        return appOptions;
    }

    private static Task Main(string[] args)
    {
        Console.WriteLine("Starting VpnHood Client for linux (Beta).");
        Console.WriteLine("Only WebUI supported at this time.");

        // init VpnHood app
        VpnHoodAppLinux.Init(CreateAppOptions, args);

        // init webserver
        VpnHoodAppWebServer.Init(new WebServerOptions());
        VpnHoodAppLinux.Instance.OpenMainWindowRequested += (_, _) => OpenMainWindow();

        // run app
        return VpnHoodAppLinux.Instance.Run();
    }

    private static void OpenMainWindow()
    {
        if (!VpnHoodAppWebServer.IsInit) {
            VhLogger.Instance.LogWarning("VpnHood Web server is not initialized to show its web UI.");
            return;
        }

        VhLogger.Instance.LogInformation("Running the default browser...");
        Process.Start(new ProcessStartInfo {
            FileName = "xdg-open",
            ArgumentList = { VpnHoodAppWebServer.Instance.Url.AbsoluteUri },
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        });

        VhLogger.Instance.LogInformation("To open VpnHood UI navigate to {0}", 
            VpnHoodAppWebServer.Instance.Url);
    }
}