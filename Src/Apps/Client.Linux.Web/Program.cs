using Microsoft.Extensions.Logging;
using VpnHood.AppLib;
using VpnHood.AppLib.WebServer;
using VpnHood.Core.Client.Device.Linux;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.App.Client.Linux.Web;

internal class Program
{
    private static async Task Main(string[] args)
    {
        _ = args;
        try
        {
            Console.WriteLine("Starting VpnHood Client for linux (Beta).");
            Console.WriteLine("On UI supported at this time.");
            
            var appConfigs = AppConfigs.Load();

            // try to remove the old adapter, as previous route may till be active
            await VhUtils.TryInvokeAsync("Remove the old adapter", () =>
                ExecuteCommandAsync($"ip link delete {AppConfigs.AppName}", CancellationToken.None));

            // ReSharper disable once UseObjectOrCollectionInitializer
            var resources = ClientAppResources.Resources;
            resources.Strings.AppName = AppConfigs.AppName;

            // check command line
            // initialize VpnHoodApp
            var appOptions = new AppOptions(appConfigs.AppId, AppConfigs.AppName, AppConfigs.IsDebugMode)
            {
                Resources = resources,
                AccessKeys = AppConfigs.IsDebug ? [appConfigs.DefaultAccessKey] : [],
                UpdateInfoUrl = appConfigs.UpdateInfoUrl,
                IsAddAccessKeySupported = true,
                IsLocalNetworkSupported = true,
                LocalSpaHostName = "my-vpnhood",
                AllowEndPointStrategy = true,
                LogServiceOptions = {
                    SingleLineConsole = false
                }
            };
            VpnHoodApp.Init(new LinuxDevice(appOptions.StorageFolderPath), appOptions);

            // initialize SPA
            ArgumentNullException.ThrowIfNull(VpnHoodApp.Instance.Resources.SpaZipData);
            using var spaZipStream = new MemoryStream(VpnHoodApp.Instance.Resources.SpaZipData);
            var webServer = VpnHoodAppWebServer.Init(new WebServerOptions
            {
                SpaZipStream = spaZipStream
            });

            Console.WriteLine($"Navigate {webServer.Url} in your browser to open VpnHood Client.");
            await Task.Delay(TimeSpan.FromMinutes(60));
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Could not run the app.");
            throw;
        }
    }

    private static Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken)
    {
        return OsUtils.ExecuteCommandAsync("/bin/bash", $"-c \"{command}\"", cancellationToken);
    }
}