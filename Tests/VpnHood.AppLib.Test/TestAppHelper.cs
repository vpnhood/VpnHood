using VpnHood.AppLib.Services.Ads;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Common.Net;
using VpnHood.Core.Common.Utils;
using VpnHood.Test;
using VpnHood.Test.Device;
using VpnHood.Test.Providers;

namespace VpnHood.AppLib.Test;

public class TestAppHelper : TestHelper
{
    public static Task WaitForAppState(VpnHoodApp app, AppConnectionState connectionSate, int timeout = 5000)
    {
        return VhTestUtil.AssertEqualsWait(connectionSate, () => app.State.ConnectionState,
            "App state didn't reach the expected value.", timeout);
    }

    public static AppOptions CreateAppOptions()
    {
        var tracker = new TestTrackerProvider();
        var appOptions = new AppOptions("com.vpnhood.client.test", isDebugMode: true) {
            StorageFolderPath = Path.Combine(WorkingPath, "AppData_" + Guid.CreateVersion7()),
            SessionTimeout = TimeSpan.FromSeconds(2),
            Ga4MeasurementId = null,
            Tracker = tracker,
            UseInternalLocationService = false,
            UseExternalLocationService = false,
            AllowEndPointTracker = true,
            LogVerbose = LogVerbose,
            ServerQueryTimeout = TimeSpan.FromSeconds(2),
            AutoDiagnose = false,
            SingleLineConsoleLog = false,
            CanExtendByRewardedAdThreshold = TimeSpan.Zero,
            AdOptions = new AppAdOptions {
                ShowAdPostDelay = TimeSpan.Zero,
                LoadAdPostDelay = TimeSpan.Zero
            }
        };
        return appOptions;
    }

    public static VpnHoodApp CreateClientApp(AppOptions? appOptions = default, IDevice? device = default)
    {
        appOptions ??= CreateAppOptions();
        device ??= new TestDevice(() => new TestNullPacketCapture());

        //create app
        var clientApp = VpnHoodApp.Init(device, appOptions);
        clientApp.Diagnoser.HttpTimeout = 2000;
        clientApp.Diagnoser.NsTimeout = 2000;
        clientApp.UserSettings.PacketCaptureIncludeIpRanges = TestIpAddresses.Select(x => new IpRange(x)).ToArray();
        clientApp.UserSettings.LogAnonymous = false;
        clientApp.TcpTimeout = TimeSpan.FromSeconds(2);
        ActiveUiContext.Context = new TestAppUiContext();

        return clientApp;
    }
}