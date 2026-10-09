using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.App;
using VpnHood.AppLib.Test.Providers;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Tracking;
using VpnHood.Net.Toolkit.Trackers;
using VpnHood.Test.Device;
using VpnHood.Test.Providers;

namespace VpnHood.AppLib.Test.Tests;

// The usage-data switch and the first-run terms, which every tracker follows: the app's, made at its
// start, and the VPN service's, made at each connect, one per factory. A test that wants trackers runs
// as a Release build, since a Debug build makes none.
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
        var firstOptions = CreateAppOptions();
        await using (var firstApp = TestAppHelper.CreateClientApp(firstOptions)) {
            firstApp.UserSettings.IsLicenseAccepted = true;
            firstApp.UserSettings.AllowAnonymousTracker = false;
            firstApp.SettingsService.Save();
        }

        RecordingTrackerFactory.Made.Clear();
        await using var app = TestAppHelper.CreateClientApp(CreateAppOptions(firstOptions.StorageFolderPath));
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected);

        var made = RecordingTrackerFactory.Made.ToArray();
        Assert.HasCount(4, made, "the app's two trackers and the VPN service's two");
        Assert.IsTrue(made.All(x => !x.CreateParams.IsEnabled && !x.Tracker.IsEnabled),
            "every tracker is made switched off");
        Assert.IsTrue(made.All(x => x.Tracker.TrackEvents.Count == 0), "and none reports anything");
    }

    [TestMethod]
    public async Task Changed_while_connected_the_vpn_service_follows_at_once()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        RecordingTrackerFactory.Made.Clear();
        await using var app = TestAppHelper.CreateClientApp(CreateAppOptions());
        AcceptTerms(app);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected);

        // the service's trackers: the last two made, at the connect
        Assert.IsInstanceOfType<CompositeTracker>(app.Services.TrackerService.Tracker);
        var serviceTrackers = MadeTrackers()[^2..];
        Assert.IsTrue(serviceTrackers.All(x => x.IsEnabled));

        app.UserSettings.AllowAnonymousTracker = false;
        app.SettingsService.Save();
        await AssertEqualsWait(true, () => serviceTrackers.All(x => !x.IsEnabled),
            "the reconfigure carries the switch to each");
        Assert.IsFalse(app.Services.TrackerService.Tracker.IsEnabled);

        app.UserSettings.AllowAnonymousTracker = true;
        app.SettingsService.Save();
        await AssertEqualsWait(true, () => serviceTrackers.All(x => x.IsEnabled), "and back on");
    }

    [TestMethod]
    public async Task The_vpn_service_makes_each_factory_again_with_its_settings()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        RecordingTrackerFactory.Made.Clear();
        await using var app = TestAppHelper.CreateClientApp(CreateAppOptions());
        AcceptTerms(app);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected);

        // the service's two, the last made: by copies of the app's factories, each with its setting
        var made = RecordingTrackerFactory.Made.ToArray();
        Assert.HasCount(4, made);
        CollectionAssert.AreEqual(new[] { "first", "second" }, made[2..].Select(x => x.FactoryName).ToArray());
    }

    [TestMethod]
    public async Task The_first_run_terms_hold_every_tracker_until_accepted()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // not accepted yet: made switched off, in the app and at a connect in the VPN service
        RecordingTrackerFactory.Made.Clear();
        await using var app = TestAppHelper.CreateClientApp(CreateAppOptions());
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected);

        var trackers = MadeTrackers();
        Assert.HasCount(4, trackers);
        Assert.IsTrue(trackers.All(x => !x.IsEnabled), "nothing before the terms are accepted");

        // accepted: every tracker on, and the app's send the first launch once
        AcceptTerms(app);
        await AssertEqualsWait(true, () => trackers.All(x => x.IsEnabled), "the acceptance reaches each tracker");
        var appTrackers = trackers[..2];
        await AssertEqualsWait(true, () => appTrackers.All(x => CountFirstLaunches(x) == 1), "the first launch");

        // a later save sends it no more
        app.SettingsService.Save();
        await Task.Delay(TimeSpan.FromMilliseconds(500), TestCt);
        Assert.IsTrue(appTrackers.All(x => CountFirstLaunches(x) == 1), "the first launch, once");
    }

    [TestMethod]
    public async Task Switched_off_while_disconnected_a_start_from_the_saved_options_follows()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        var device = new TestDevice(TestAppHelper, _ => new TestNullVpnAdapter());
        await using var app = TestAppHelper.CreateClientApp(CreateAppOptions(), device);
        AcceptTerms(app);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected);
        await app.Disconnect();
        await app.WaitForState(AppConnectionState.None);

        // turned off with no session to reconfigure: the saved options take it all the same
        app.UserSettings.AllowAnonymousTracker = false;
        app.SettingsService.Save();
        var serviceOptionsFile = new VpnServiceOptionsFile(device.VpnServiceConfigFolder);
        await AssertEqualsWait(false, () => serviceOptionsFile.TryRead()?.ClientOptions.AllowAnonymousTracker,
            "the saved options follow the reconfigure");

        // a start from them without the app, as always-on, the tile or the system makes
        RecordingTrackerFactory.Made.Clear();
        await device.StartVpnService(TestCt);
        await AssertEqualsWait(2, () => RecordingTrackerFactory.Made.Count, "the service makes its two trackers");
        Assert.IsTrue(MadeTrackers().All(x => !x.IsEnabled), "made switched off");
    }

    [TestMethod]
    public async Task A_build_without_a_tracker_tells_the_vpn_service_off()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        var device = new TestDevice(TestAppHelper, _ => new TestNullVpnAdapter());
        var appOptions = TestAppHelper.CreateAppOptions(isDebugMode: false);
        appOptions.TrackerFactories = [];
        await using var app = TestAppHelper.CreateClientApp(appOptions, device);
        AcceptTerms(app);
        Assert.IsFalse(app.Features.IsAnonymousTrackerSupported);

        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected);

        // so the access manager's own hit stays off too
        var clientOptions = new VpnServiceOptionsFile(device.VpnServiceConfigFolder).Read().ClientOptions;
        Assert.IsFalse(clientOptions.AllowAnonymousTracker);
        Assert.IsFalse(clientOptions.AllowEndPointTracker);
        Assert.IsEmpty(clientOptions.TrackerFactoryInfos);
    }

    [TestMethod]
    public async Task A_debug_build_makes_no_tracker()
    {
        RecordingTrackerFactory.Made.Clear();
        var appOptions = TestAppHelper.CreateAppOptions(isDebugMode: true);
        appOptions.TrackerFactories = [new RecordingTrackerFactory()];
        await using var app = TestAppHelper.CreateClientApp(appOptions);
        AcceptTerms(app);

        Assert.IsEmpty(RecordingTrackerFactory.Made);
        Assert.IsInstanceOfType<NullTracker>(app.Services.TrackerService.Tracker);
        Assert.IsFalse(app.Features.IsAnonymousTrackerSupported);
        Assert.IsEmpty(app.Services.TrackerService.TrackerFactoryInfos);
    }

    [TestMethod]
    public void A_ga4_tracker_is_made_switched_off()
    {
        var tracker = new Ga4TrackerFactory { MeasurementId = "G-TEST" }.CreateTracker(new TrackerCreateParams {
            ClientId = "test",
            ClientVersion = new Version(1, 2, 3),
            IsEnabled = false
        });

        Assert.IsFalse(tracker.IsEnabled);
    }

    // A Release build with two factories, so the app and the VPN service each make a composite of two.
    private AppOptions CreateAppOptions(string? storagePath = null)
    {
        var appOptions = TestAppHelper.CreateAppOptions(isDebugMode: false, storagePath: storagePath);
        appOptions.TrackerFactories = [
            new RecordingTrackerFactory { Name = "first" },
            new RecordingTrackerFactory { Name = "second" }
        ];
        return appOptions;
    }

    // as the first-run page does
    private static void AcceptTerms(VpnHoodApp app)
    {
        app.UserSettings.IsLicenseAccepted = true;
        app.SettingsService.Save();
    }

    private static TestTracker[] MadeTrackers()
    {
        return RecordingTrackerFactory.Made.Select(x => x.Tracker).ToArray();
    }

    // a copy of the events, since the app may add one while this counts
    private static int CountFirstLaunches(TestTracker tracker)
    {
        return tracker.TrackEvents.ToArray().Count(x => x.EventName == "vh_first_launch");
    }
}
