using System.Security.Cryptography;
using System.Text;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Assets.Ip2LocationLite;
using VpnHood.AppLib.Services.Ads;
using VpnHood.AppLib.Test.Providers;
using VpnHood.Core.Client.Devices;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Toolkit.Logging;
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
            IsSingleton = false, // tests run many concurrent apps in one process
            StorageFolderPath = Path.Combine(WorkingPath, "AppData_" + Guid.CreateVersion7()),
            SessionTimeout = TimeSpan.FromSeconds(2),
            DeviceUiProvider = new TestDeviceUiProvider(),
            EventWatcherInterval = TimeSpan.FromMilliseconds(200), // no SPA in test, so we need to use event watcher
            Ga4MeasurementId = null,
            TrackerFactory = new TestTrackerFactory(),
            AllowEndPointTracker = true,
            ServerQueryTimeout = TimeSpan.FromSeconds(2),
            AutoDiagnose = false,
            DisconnectOnDispose = true,
            ConnectTimeout = TimeSpan.FromSeconds(5).WhenNoDebugger(),
            TcpTimeout = TimeSpan.FromSeconds(2).WhenNoDebugger(),
            Resources = new AppResources(),
            AdOptions = new AppAdOptions {
                ShowAdPostDelay = TimeSpan.Zero,
                LoadAdPostDelay = TimeSpan.Zero,
                ExtendByRewardedAdThreshold = TimeSpan.Zero,
                RejectAdBlocker = true,
                AllowedPrivateDnsProviders = ["dns.google", "dns.test"]
            },
            LogServiceOptions = {
                // apps would fight over the process-wide VhLogger; tests asserting State.LogExists opt back in
                Enabled = false,
            }
        };

        appOptions.Resources.IpLocationZipData = new Lazy<byte[]>(() => Ip2LocationLiteDb.ZipData);
        return appOptions;
    }

    public VpnHoodApp CreateClientApp(AppOptions? appOptions = null, IDevice? device = null)
    {
        appOptions ??= CreateAppOptions();
        device ??= new TestDevice(this, _ => new TestNullVpnAdapter());

        //create app; not registered as the singleton, so keep the returned instance
        var clientApp = VpnHoodApp.Init(device, appOptions);
        clientApp.Diagnoser.HttpTimeout = TimeSpan.FromSeconds(2);
        clientApp.Diagnoser.NsTimeout = TimeSpan.FromSeconds(2);
        clientApp.UserSettings.UseSplitIpViaDevice = true;
        clientApp.UserSettings.UseSplitIpViaApp = true;
        clientApp.UserSettings.UseTcpProxy = true;
        clientApp.SettingsService.SplitIpSettings.DeviceIncludes = TestIps.AllRemoteTestIps.ToOrderedIpRanges().ToText();
        clientApp.UserSettings.LogAnonymous = false;

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

    public override void Dispose()
    {
        // AppRegionInfo is process-global; don't let a test's country override leak into the next test
        AppRegionInfo.Reset();
        base.Dispose();
    }
}