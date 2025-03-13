using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Assets.Ip2LocationLite;
using VpnHood.AppLib.Services.Ads;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Client.Device.UiContexts;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Test;
using VpnHood.Test.Device;
using VpnHood.Test.Providers;

namespace VpnHood.AppLib.Test;

public class TestAppHelper : TestHelper
{
    public AppOptions CreateAppOptions()
    {
        var appOptions = new AppOptions("com.vpnhood.client.test", "VpnHoodClient.Test", isDebugMode: true) {
            StorageFolderPath = Path.Combine(WorkingPath, "AppData_" + Guid.CreateVersion7()),
            SessionTimeout = TimeSpan.FromSeconds(2),
            EventWatcherInterval = TimeSpan.FromMilliseconds(200), // no SPA in test, so we need to use event watcher
            Ga4MeasurementId = null,
            TrackerFactory = new TestTrackerFactory(),
            UseInternalLocationService = false,
            UseExternalLocationService = false,
            AllowEndPointTracker = true,
            ServerQueryTimeout = TimeSpan.FromSeconds(2),
            AutoDiagnose = false,
            CanExtendByRewardedAdThreshold = TimeSpan.Zero,
            DisconnectOnDispose = true,
            ConnectTimeout = Debugger.IsAttached ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(5),
            Resources = new AppResources(),
            AdOptions = new AppAdOptions {
                ShowAdPostDelay = TimeSpan.Zero,
                LoadAdPostDelay = TimeSpan.Zero
            },
            LogServiceOptions = {
                LogLevel = LogVerbose ? LogLevel.Trace : LogLevel.Debug,
                SingleLineConsole = false
            }
        };

        appOptions.Resources.IpLocationZipData = Ip2LocationLiteDb.ZipData;
        return appOptions;
    }

    public VpnHoodApp CreateClientApp(AppOptions? appOptions = null, IDevice? device = null)
    {
        appOptions ??= CreateAppOptions();
        device ??= new TestDevice(this, _ => new TestNullVpnAdapter());

        //create app
        var clientApp = VpnHoodApp.Init(device, appOptions);
        clientApp.Diagnoser.HttpTimeout = 2000;
        clientApp.Diagnoser.NsTimeout = 2000;
        clientApp.UserSettings.UseVpnAdapterIpFilter = true;
        clientApp.UserSettings.UseAppIpFilter = true;
        clientApp.SettingsService.IpFilterSettings.AdapterIpFilterIncludes =
            TestIpAddresses.Select(x => new IpRange(x)).ToText();
        clientApp.UserSettings.LogAnonymous = false;
        clientApp.TcpTimeout = TimeSpan.FromSeconds(2);
        AppUiContext.Context = new TestAppUiContext();

        return clientApp;
    }

    private static string GenerateSecureRandomDigits(int length)
    {
        var result = new StringBuilder(length);
        using var rng = RandomNumberGenerator.Create();
        var buffer = new byte[1];
        while (result.Length < length) {
            rng.GetBytes(buffer);
            var digit = buffer[0] % 10;
            result.Append(digit);
        }

        return result.ToString();
    }

    public string BuildAccessCode()
    {
        return AccessCodeUtils.Build(GenerateSecureRandomDigits(18));
    }
}