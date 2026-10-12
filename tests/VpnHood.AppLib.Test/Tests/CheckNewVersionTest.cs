using System.Net.Http.Headers;
using System.Text.Json;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.HttpClients;
using VpnHood.AppLib.Api.Updaters;
using VpnHood.AppLib.Api.WebHost;
using VpnHood.AppLib.App;
using VpnHood.AppLib.App.Services.Updaters;
using VpnHood.AppLib.Test.Providers;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Test;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppLib.Test.Tests;

[TestClass]
[DoNotParallelize] // timing-sensitive version checks; CPU contention shifts delays and stalls connects
public class CheckNewVersionTest : TestAppBase
{
    private void SetNewRelease(Version version, DateTime releaseDate, TimeSpan? notificationDelay = null,
        Version? deprecatedVersion = null)
    {
        deprecatedVersion ??= new Version(1, 0, 0);
        notificationDelay ??= TimeSpan.Zero;

        var publishInfo = new PublishInfo {
            Version = version,
            UpdateInfoUrl = TestHelper.WebServer.LocalEps.HttpUrl1,
            PackageUrl = new Uri("https://localhost/package.msi"),
            GooglePlayUrl = null,
            InstallationPageUrl = new Uri("https://localhost/page.html"),
            DeprecatedVersion = deprecatedVersion,
            ReleaseDate = releaseDate,
            NotificationDelay = notificationDelay.Value
        };

        TestHelper.WebServer.FileContent1 = JsonSerializer.Serialize(publishInfo);
    }

    private static Version CurrentAppVersion => typeof(VpnHoodApp).Assembly.GetName().Version!;

    [TestMethod]
    public async Task Remote_is_unknown_if_remote_is_unreachable()
    {
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.UpdaterOptions = new AppUpdaterOptions {
            UpdateInfoUrl = new Uri("https://localhost:39999")
        };
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);

        await Task.Delay(1000);
        await VhTestUtil.AssertEqualsWait(VersionStatus.Unknown, () => app.State.UpdaterStatus?.VersionStatus);
    }


    [TestMethod]
    public async Task Current_is_latest()
    {
        SetNewRelease(CurrentAppVersion, DateTime.UtcNow, TimeSpan.Zero);

        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.UpdaterOptions = new AppUpdaterOptions {
            UpdateInfoUrl = TestHelper.WebServer.LocalEps.HttpUrl1,
            CheckInterval = TimeSpan.FromMilliseconds(500)
        };

        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);
        await VhTestUtil.AssertEqualsWait(VersionStatus.Latest, () => app.State.UpdaterStatus?.VersionStatus);
    }

    [TestMethod]
    public async Task Current_is_deprecated()
    {
        SetNewRelease(new Version(CurrentAppVersion.Major, CurrentAppVersion.Minor, CurrentAppVersion.Build + 1),
            DateTime.UtcNow,
            notificationDelay: TimeSpan.Zero,
            deprecatedVersion: CurrentAppVersion);

        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.UpdaterOptions = new AppUpdaterOptions {
            UpdateInfoUrl = TestHelper.WebServer.LocalEps.HttpUrl1,
            CheckInterval = TimeSpan.FromMilliseconds(500)
        };

        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);
        await VhTestUtil.AssertEqualsWait(VersionStatus.Deprecated, () => app.State.UpdaterStatus?.VersionStatus);
    }

    [TestMethod]
    public async Task Current_is_old_by_connect()
    {
        SetNewRelease(CurrentAppVersion, DateTime.UtcNow, TimeSpan.Zero);

        // create server and token
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // create client
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.UpdaterOptions = new AppUpdaterOptions {
            UpdateInfoUrl = TestHelper.WebServer.LocalEps.HttpUrl1,
            CheckInterval = TimeSpan.FromMilliseconds(10)
        };
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());

        // version should be latest
        await VhTestUtil.AssertEqualsWait(VersionStatus.Latest, () => app.State.UpdaterStatus?.VersionStatus);

        // after publish a new version it should be old
        SetNewRelease(new Version(CurrentAppVersion.Major, CurrentAppVersion.Minor, CurrentAppVersion.Build + 1),
            DateTime.UtcNow, TimeSpan.Zero);

        await Task.Delay(100); // wait for CheckInterval
        await app.Connect(vpnProfile.VpnProfileId);
        await VhTestUtil.AssertEqualsWait(VersionStatus.Old, () => app.State.UpdaterStatus?.VersionStatus);
    }

    [TestMethod]
    public async Task Current_is_old_but_wait_for_notification_delay()
    {
        SetNewRelease(new Version(CurrentAppVersion.Major, CurrentAppVersion.Minor, CurrentAppVersion.Build + 1),
            DateTime.UtcNow, TimeSpan.FromSeconds(2));

        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.UpdaterOptions = new AppUpdaterOptions {
            UpdateInfoUrl = TestHelper.WebServer.LocalEps.HttpUrl1,
            CheckInterval = TimeSpan.FromMilliseconds(500)
        };
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);

        await VhTestUtil.AssertEqualsWait(VersionStatus.Latest, () => app.State.UpdaterStatus?.VersionStatus);
        await VhTestUtil.AssertEqualsWait(VersionStatus.Old, () => app.State.UpdaterStatus?.VersionStatus);
    }

    [TestMethod]
    public async Task Current_is_old_before_connect_to_vpn()
    {
        SetNewRelease(new Version(CurrentAppVersion.Major, CurrentAppVersion.Minor, CurrentAppVersion.Build + 1),
            DateTime.UtcNow, TimeSpan.Zero);

        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.UpdaterOptions = new AppUpdaterOptions {
            UpdateInfoUrl = TestHelper.WebServer.LocalEps.HttpUrl1,
            CheckInterval = TimeSpan.FromMilliseconds(500)
        };
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);

        await VhTestUtil.AssertEqualsWait(VersionStatus.Old, () => app.State.UpdaterStatus?.VersionStatus);
    }

    [TestMethod]
    public async Task Current_is_old_after_connect_to_vpn()
    {
        // create server and token
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // Set invalid file version to raise error
        TestHelper.WebServer.FileContent1 = Guid.NewGuid().ToString();

        // create client app
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.UpdaterOptions = new AppUpdaterOptions {
            UpdateInfoUrl = TestHelper.WebServer.LocalEps.HttpUrl1,
            CheckInterval = TimeSpan.FromMilliseconds(500)
        };
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());

        await Task.Delay(1000);
        await VhTestUtil.AssertEqualsWait(VersionStatus.Unknown, () => app.State.UpdaterStatus?.VersionStatus);

        // set new version
        SetNewRelease(new Version(CurrentAppVersion.Major, CurrentAppVersion.Minor, CurrentAppVersion.Build + 1),
            DateTime.UtcNow, TimeSpan.Zero);
        await app.Connect(vpnProfile.VpnProfileId);
        await app.WaitForState(AppConnectionState.Connected);
        await VhTestUtil.AssertEqualsWait(VersionStatus.Old, () => app.State.UpdaterStatus?.VersionStatus);
    }

    [TestMethod]
    public async Task A_check_with_no_window_finds_a_new_version()
    {
        SetNewRelease(CurrentAppVersion, DateTime.UtcNow, TimeSpan.Zero);

        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.UpdaterOptions = new AppUpdaterOptions {
            UpdateInfoUrl = TestHelper.WebServer.LocalEps.HttpUrl1
        };
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);
        await VhTestUtil.AssertEqualsWait(VersionStatus.Latest, () => app.State.UpdaterStatus?.VersionStatus);

        // the desktop service: its window is another process, so this one has none. Released a minute
        // ago: the status reads a coarse clock, which may not have reached "now" yet
        AppUiContext.Context = null;
        SetNewRelease(new Version(CurrentAppVersion.Major, CurrentAppVersion.Minor, CurrentAppVersion.Build + 1),
            DateTime.UtcNow.AddMinutes(-1), TimeSpan.Zero);
        await app.Api.App.VersionCheck(TestCt);

        Assert.AreEqual(VersionStatus.Old, app.State.UpdaterStatus?.VersionStatus);
    }

    [TestMethod]
    public async Task A_store_updater_with_no_window_leaves_the_check_for_one_with_a_window()
    {
        SetNewRelease(CurrentAppVersion, DateTime.UtcNow, TimeSpan.Zero);
        var updaterProvider = new TestAppUpdaterProvider();
        Directory.CreateDirectory(TestHelper.WorkingPath);
        using var updaterService = new AppUpdaterService(TestHelper.WorkingPath, CurrentAppVersion,
            new AppUpdaterOptions {
                UpdateInfoUrl = TestHelper.WebServer.LocalEps.HttpUrl1,
                UpdaterProvider = updaterProvider
            }, isConnected: () => true);

        // a store build's check after a connect made with its window closed: the feed's answer must
        // not stamp the check, or the store's own prompt would wait a whole interval
        await updaterService.CheckForUpdate(uiContext: null, force: false, TestCt);
        Assert.IsNull(updaterService.Status.CheckedTime);

        // the window's arrival checks at once, and the store's updater is asked
        await updaterService.CheckForUpdate(new TestHelper.TestAppUiContext(), force: false, TestCt);
        Assert.AreEqual(1, updaterProvider.IsUpdateAvailableCalls);
        Assert.IsNotNull(updaterService.Status.CheckedTime);
    }

    // A filtered network reaches the store only after a connect, and Play answers "none" before it: a
    // daily check that opens before the connect must not restart the wait for the store's own update,
    // or the store's prompt never comes.
    [TestMethod]
    public async Task A_store_update_a_check_could_not_confirm_keeps_its_wait()
    {
        var updaterProvider = new TestAppUpdaterProvider { IsAvailable = true };
        using var updaterService = new AppUpdaterService(CreateUpdaterFolder(), CurrentAppVersion,
            new AppUpdaterOptions {
                UpdaterProvider = updaterProvider,
                CheckInterval = TimeSpan.Zero,
                PromptDelay = TimeSpan.FromSeconds(1)
            }, isConnected: () => false);
        var uiContext = new TestHelper.TestAppUiContext();

        // offered: the store's own update has the delay first
        await updaterService.CheckForUpdate(uiContext, force: false, TestCt);
        Assert.AreEqual(0, updaterProvider.UpdateCalls);

        // before a connect, out of reach
        updaterProvider.IsAvailable = false;
        await updaterService.CheckForUpdate(uiContext, force: false, TestCt);
        Assert.AreEqual(VersionStatus.Old, updaterService.Status.VersionStatus, "the offer stands");

        // after it, past the delay, which counts from the first offer
        updaterProvider.IsAvailable = true;
        await Task.Delay(TimeSpan.FromSeconds(2.5), TestCt); // the delay, on a clock of 1 s steps
        await updaterService.CheckForUpdate(uiContext, force: false, TestCt);
        Assert.AreEqual(1, updaterProvider.UpdateCalls, "the store's prompt");
    }

    // The feed answering before a connect is no reason to wait a whole interval for a store that said
    // "none" then, out of reach: the try after the connect, through the tunnel, asks the store again.
    [TestMethod]
    public async Task A_store_none_before_a_connect_is_asked_again_after_it()
    {
        SetNewRelease(CurrentAppVersion, DateTime.UtcNow, TimeSpan.Zero);
        var isConnected = false;
        var updaterProvider = new TestAppUpdaterProvider();
        using var updaterService = new AppUpdaterService(CreateUpdaterFolder(), CurrentAppVersion,
            new AppUpdaterOptions {
                UpdateInfoUrl = TestHelper.WebServer.LocalEps.HttpUrl1,
                UpdaterProvider = updaterProvider
            }, isConnected: () => isConnected);
        var uiContext = new TestHelper.TestAppUiContext();

        // before the connect: the feed answers, the store says "none"
        await updaterService.CheckForUpdate(uiContext, force: false, TestCt);
        Assert.IsNull(updaterService.Status.CheckedTime, "left for the connect");

        // the connect's try asks the store, which has the update
        isConnected = true;
        updaterProvider.IsAvailable = true;
        await updaterService.CheckForUpdate(uiContext, force: false, TestCt);
        Assert.AreEqual(2, updaterProvider.IsUpdateAvailableCalls);
        Assert.IsNotNull(updaterService.Status.CheckedTime);
    }

    // A store out of reach answers "none", so the store's offer keeps the feed's notice away until it
    // has made none for UpdaterOfferPeriod: only then is the feed's notice the way.
    [TestMethod]
    public async Task The_feeds_notice_waits_while_the_store_offers_the_update()
    {
        SetNewRelease(new Version(CurrentAppVersion.Major, CurrentAppVersion.Minor, CurrentAppVersion.Build + 1),
            DateTime.UtcNow.AddMinutes(-1), TimeSpan.Zero);
        var updaterProvider = new TestAppUpdaterProvider { IsAvailable = true };
        using var updaterService = new AppUpdaterService(CreateUpdaterFolder(), CurrentAppVersion,
            new AppUpdaterOptions {
                UpdateInfoUrl = TestHelper.WebServer.LocalEps.HttpUrl1,
                UpdaterProvider = updaterProvider,
                CheckInterval = TimeSpan.Zero,
                UpdaterOfferPeriod = TimeSpan.FromSeconds(1)
            }, isConnected: () => false);
        var uiContext = new TestHelper.TestAppUiContext();

        // offered: the store's own update waits its delay, and the feed's notice stays away
        await updaterService.CheckForUpdate(uiContext, force: false, TestCt);
        Assert.IsFalse(updaterService.Status.Prompt);

        // out of reach: the feed answers, and its notice still waits
        updaterProvider.IsAvailable = false;
        await updaterService.CheckForUpdate(uiContext, force: false, TestCt);
        Assert.AreEqual(VersionStatus.Old, updaterService.Status.VersionStatus);
        Assert.IsFalse(updaterService.Status.Prompt);

        // no offer for the whole period
        await Task.Delay(TimeSpan.FromSeconds(2.5), TestCt); // the period, on a clock of 1 s steps
        await updaterService.CheckForUpdate(uiContext, force: false, TestCt);
        Assert.IsTrue(updaterService.Status.Prompt);
    }

    // A store screen that failed is no reason for the feed's notice while the store offers the update:
    // the next check shows the store's screen again.
    [TestMethod]
    public async Task A_store_screen_that_failed_is_shown_again_at_the_next_check()
    {
        SetNewRelease(new Version(CurrentAppVersion.Major, CurrentAppVersion.Minor, CurrentAppVersion.Build + 1),
            DateTime.UtcNow.AddMinutes(-1), TimeSpan.Zero);
        var updaterProvider = new TestAppUpdaterProvider { IsUpdateShown = false };
        using var updaterService = new AppUpdaterService(CreateUpdaterFolder(), CurrentAppVersion,
            new AppUpdaterOptions {
                UpdateInfoUrl = TestHelper.WebServer.LocalEps.HttpUrl1,
                UpdaterProvider = updaterProvider,
                CheckInterval = TimeSpan.Zero,
                PromptDelay = TimeSpan.Zero // Play's: its screen at once
            }, isConnected: () => true);
        var uiContext = new TestHelper.TestAppUiContext();

        // the store has none yet: the feed's notice
        await updaterService.CheckForUpdate(uiContext, force: false, TestCt);
        Assert.IsTrue(updaterService.Status.Prompt);

        // the store offers it, and its screen fails
        updaterProvider.IsAvailable = true;
        await updaterService.CheckForUpdate(uiContext, force: false, TestCt);
        Assert.AreEqual(1, updaterProvider.UpdateCalls);
        Assert.IsFalse(updaterService.Status.Prompt);

        await updaterService.CheckForUpdate(uiContext, force: false, TestCt);
        Assert.AreEqual(2, updaterProvider.UpdateCalls);
    }

    // The store's screen shows only over a window in front: a window in the background - one being
    // created, or one a reconnect finds there - leaves the check, and its return to the front checks again.
    [TestMethod]
    public async Task A_window_in_the_background_leaves_the_store_screen_for_its_return()
    {
        var updaterProvider = new TestAppUpdaterProvider();
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.UpdaterOptions = new AppUpdaterOptions {
            UpdaterProvider = updaterProvider,
            PromptDelay = TimeSpan.Zero
        };
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);
        await VhTestUtil.AssertEqualsWait(1, () => updaterProvider.IsUpdateAvailableCalls);

        // the store has the update, and the window is in the background
        updaterProvider.IsAvailable = true;
        var window = new TestWindowUiContext { IsInFront = false };
        AppUiContext.Context = window;
        await VhTestUtil.AssertEqualsWait(2, () => updaterProvider.IsUpdateAvailableCalls);
        Assert.AreEqual(0, updaterProvider.UpdateCalls);
        Assert.IsNull(app.State.UpdaterStatus?.CheckedTime);

        // back to the front
        window.IsInFront = true;
        AppUiContext.NotifyResumed();
        await VhTestUtil.AssertEqualsWait(1, () => updaterProvider.UpdateCalls);
    }

    // An update the person started and left before it finished comes back with the window in front,
    // whatever the interval or a postpone: it is their own choice, not a prompt.
    [TestMethod]
    public async Task An_update_in_progress_comes_back_with_the_window()
    {
        var updaterProvider = new TestAppUpdaterProvider { IsAvailable = true };
        using var updaterService = new AppUpdaterService(CreateUpdaterFolder(), CurrentAppVersion,
            new AppUpdaterOptions {
                UpdaterProvider = updaterProvider,
                PromptDelay = TimeSpan.Zero
            }, isConnected: () => true);
        var window = new TestWindowUiContext();

        // the store's screen: the person starts the update, and leaves before it finishes
        await updaterService.CheckForUpdate(window, force: false, TestCt);
        Assert.AreEqual(1, updaterProvider.UpdateCalls);
        updaterProvider.IsUpdateInProgress = true;

        // with the window in the background, nothing
        window.IsInFront = false;
        await updaterService.CheckForUpdate(window, force: false, TestCt);
        Assert.AreEqual(0, updaterProvider.ResumeUpdateCalls);

        // back in front, within the interval and the postpone: its screen again
        window.IsInFront = true;
        await updaterService.CheckForUpdate(window, force: false, TestCt);
        Assert.AreEqual(1, updaterProvider.ResumeUpdateCalls);
        Assert.AreEqual(1, updaterProvider.UpdateCalls, "no new prompt");
    }

    // A check the person asked for always answers: none while the feed cannot be read, then nothing
    // newer, then the update.
    [TestMethod]
    public async Task A_check_asked_for_always_answers()
    {
        TestHelper.WebServer.FileContent1 = Guid.NewGuid().ToString(); // no feed to read
        using var updaterService = new AppUpdaterService(CreateUpdaterFolder(), CurrentAppVersion,
            new AppUpdaterOptions {
                UpdateInfoUrl = TestHelper.WebServer.LocalEps.HttpUrl1
            }, isConnected: () => false);
        var uiContext = new TestHelper.TestAppUiContext();

        Assert.AreEqual(VersionCheckResult.Failed, await updaterService.CheckForUpdate(uiContext, force: true, TestCt));

        SetNewRelease(CurrentAppVersion, DateTime.UtcNow, TimeSpan.Zero);
        Assert.AreEqual(VersionCheckResult.Latest, await updaterService.CheckForUpdate(uiContext, force: true, TestCt));

        SetNewRelease(new Version(CurrentAppVersion.Major, CurrentAppVersion.Minor, CurrentAppVersion.Build + 1),
            DateTime.UtcNow.AddMinutes(-1), TimeSpan.Zero);
        Assert.AreEqual(VersionCheckResult.UpdateAvailable, await updaterService.CheckForUpdate(uiContext, force: true, TestCt));
    }

    // A window over a desktop service asks through the web host, and the answer comes back the same.
    [TestMethod]
    public async Task A_check_asked_for_answers_over_http()
    {
        SetNewRelease(CurrentAppVersion, DateTime.UtcNow, TimeSpan.Zero);
        var appOptions = TestAppHelper.CreateAppOptions(isDebugMode: false);
        appOptions.WebHostFactory = new VpnHoodAppWebHostFactory();
        appOptions.WebRootZipAsset = TestAppHelper.CreateWebRootZip("web-root-test");
        appOptions.UpdaterOptions = new AppUpdaterOptions {
            UpdateInfoUrl = TestHelper.WebServer.LocalEps.HttpUrl1
        };
        await using var app = TestAppHelper.CreateClientApp(appOptions);
        var localHost = app.LocalWebHost ?? throw new InvalidOperationException("The app has no local web host.");
        var localUrl = await localHost.EnsureStarted(TestCt);
        using var http = new HttpClient { BaseAddress = new Uri(localUrl.GetLeftPart(UriPartial.Authority) + "/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            LocalApiToken.Read(localUrl) ?? throw new InvalidOperationException("The local address carries no token."));
        var api = VpnHoodApiHttpFactory.Create(http);

        Assert.AreEqual(VersionCheckResult.Latest, await api.App.VersionCheck(TestCt));
    }

    // While the store offers the update, a check that cannot reach the store has no answer: not "the
    // latest", and no feed's notice either.
    [TestMethod]
    public async Task A_check_asked_for_with_the_store_out_of_reach_has_no_answer()
    {
        SetNewRelease(new Version(CurrentAppVersion.Major, CurrentAppVersion.Minor, CurrentAppVersion.Build + 1),
            DateTime.UtcNow.AddMinutes(-1), TimeSpan.Zero);
        var updaterProvider = new TestAppUpdaterProvider { IsAvailable = true };
        using var updaterService = new AppUpdaterService(CreateUpdaterFolder(), CurrentAppVersion,
            new AppUpdaterOptions {
                UpdateInfoUrl = TestHelper.WebServer.LocalEps.HttpUrl1,
                UpdaterProvider = updaterProvider
            }, isConnected: () => false);
        var uiContext = new TestHelper.TestAppUiContext();

        // offered, the store's own update waiting its delay
        Assert.AreEqual(VersionCheckResult.UpdateAvailable, await updaterService.CheckForUpdate(uiContext, force: false, TestCt));

        // out of reach before a connect, when the person asks
        updaterProvider.IsAvailable = false;
        Assert.AreEqual(VersionCheckResult.Failed, await updaterService.CheckForUpdate(uiContext, force: true, TestCt));
        Assert.IsFalse(updaterService.Status.Prompt);
    }

    // An install ends the offer the version before it had: the new version is not old for it, and an
    // update offered to the new one waits its own delay.
    [TestMethod]
    public async Task An_update_offered_to_the_version_before_an_install_is_not_this_ones()
    {
        var folder = CreateUpdaterFolder();
        var updaterProvider = new TestAppUpdaterProvider { IsAvailable = true };
        var updaterOptions = new AppUpdaterOptions {
            UpdaterProvider = updaterProvider,
            CheckInterval = TimeSpan.Zero,
            PromptDelay = TimeSpan.FromSeconds(1)
        };
        var uiContext = new TestHelper.TestAppUiContext();

        using (var earlierService = new AppUpdaterService(folder, new Version(1, 0, 0), updaterOptions,
                   isConnected: () => false))
            await earlierService.CheckForUpdate(uiContext, force: false, TestCt);

        // installed past the delay, on the same storage
        await Task.Delay(TimeSpan.FromSeconds(2.5), TestCt);
        using var updaterService = new AppUpdaterService(folder, CurrentAppVersion, updaterOptions,
            isConnected: () => false);
        Assert.AreNotEqual(VersionStatus.Old, updaterService.Status.VersionStatus);

        await updaterService.CheckForUpdate(uiContext, force: false, TestCt);
        Assert.AreEqual(0, updaterProvider.UpdateCalls, "an offer to this version, which waits its delay");
    }

    // each test's own version.json
    private string CreateUpdaterFolder()
    {
        return Directory.CreateDirectory(Path.Combine(TestHelper.WorkingPath, $"updater-{Guid.NewGuid():N}")).FullName;
    }
}