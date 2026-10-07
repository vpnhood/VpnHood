using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Test.Providers;
using VpnHood.Core.Client.VpnServices.Abstractions.Tracking;

namespace VpnHood.AppLib.Test.Tests;

// The usage-data switch, off: every tracker is made switched off - the app's at its start, the VPN
// service's at each connect - so neither sends its session_start, nor the service its usage; and
// changed while connected, it reaches the service's tracker at once.
[TestClass]
[DoNotParallelize] // reads the process-global recording factory
public class AllowAnonymousTrackerTest : TestAppBase
{
    [TestMethod]
    public async Task Switched_off_every_tracker_is_made_switched_off()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // turned off in one run and read by the next, as after a restart
        var firstOptions = TestAppHelper.CreateAppOptions();
        await using (var firstApp = TestAppHelper.CreateClientApp(firstOptions)) {
            firstApp.UserSettings.AllowAnonymousTracker = false;
            firstApp.SettingsService.Save();
        }

        RecordingTrackerFactory.Made.Clear();
        var appOptions = TestAppHelper.CreateAppOptions(storagePath: firstOptions.StorageFolderPath);
        appOptions.TrackerFactory = new RecordingTrackerFactory();
        await using var app = TestAppHelper.CreateClientApp(appOptions);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected);

        var made = RecordingTrackerFactory.Made.ToArray();
        Assert.IsTrue(made.Length >= 2, $"the app's tracker and the VPN service's; made: {made.Length}");
        Assert.IsTrue(made.All(x => !x.CreateParams.IsEnabled && !x.Tracker.IsEnabled),
            "every tracker is made switched off");
    }

    [TestMethod]
    public async Task Changed_while_connected_the_vpn_service_follows_at_once()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        RecordingTrackerFactory.Made.Clear();
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.TrackerFactory = new RecordingTrackerFactory();
        await using var app = TestAppHelper.CreateClientApp(appOptions);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected);

        // the service's tracker: the last one made, at the connect
        var serviceTracker = RecordingTrackerFactory.Made.Last().Tracker;
        Assert.AreNotSame(app.Services.Tracker, serviceTracker);
        Assert.IsTrue(serviceTracker.IsEnabled);

        app.UserSettings.AllowAnonymousTracker = false;
        app.SettingsService.Save();
        await AssertEqualsWait(false, () => serviceTracker.IsEnabled, "the reconfigure carries the switch");
        Assert.IsFalse(app.Services.Tracker.IsEnabled);

        app.UserSettings.AllowAnonymousTracker = true;
        app.SettingsService.Save();
        await AssertEqualsWait(true, () => serviceTracker.IsEnabled, "and back on");
    }

    [TestMethod]
    public void A_ga4_tracker_is_made_switched_off()
    {
        var tracker = new BuiltInTrackerFactory().CreateTracker(new TrackerCreateParams {
            ClientId = "test",
            ClientVersion = new Version(1, 2, 3),
            Ga4MeasurementId = "G-TEST",
            IsEnabled = false
        });

        Assert.IsFalse(tracker.IsEnabled);
    }
}
