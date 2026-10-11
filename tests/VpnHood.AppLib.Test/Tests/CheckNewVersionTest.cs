using System.Text.Json;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.Updaters;
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
            });

        // a store build's check after a connect made with its window closed: the feed's answer must
        // not stamp the check, or the store's own prompt would wait a whole interval
        await updaterService.CheckForUpdate(uiContext: null, force: false, TestCt);
        Assert.IsNull(updaterService.Status.CheckedTime);

        // the window's arrival checks at once, and the store's updater is asked
        await updaterService.CheckForUpdate(new TestHelper.TestAppUiContext(), force: false, TestCt);
        Assert.AreEqual(1, updaterProvider.IsUpdateAvailableCalls);
        Assert.IsNotNull(updaterService.Status.CheckedTime);
    }
}