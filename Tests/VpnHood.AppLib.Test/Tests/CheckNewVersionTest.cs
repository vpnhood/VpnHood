using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Test.Tests;

[TestClass]
public class CheckNewVersionTest : TestAppBase
{
    private void SetNewRelease(Version version, DateTime releaseDate, TimeSpan? notificationDelay = null,
        Version? deprecatedVersion = null)
    {
        deprecatedVersion ??= new Version(1, 0, 0);
        notificationDelay ??= TimeSpan.Zero;

        var publishInfo = new PublishInfo {
            Version = version,
            UpdateInfoUrl = TestHelper.WebServer.FileHttpUrl1,
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
        appOptions.UpdateInfoUrl = "https://localhost:39999";
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);

        await Task.Delay(1000);
        await VhTestUtil.AssertEqualsWait(VersionStatus.Unknown, () => app.State.VersionStatus);
    }


    [TestMethod]
    public async Task Current_is_latest()
    {
        SetNewRelease(CurrentAppVersion, DateTime.UtcNow, TimeSpan.Zero);

        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.UpdateInfoUrl = TestHelper.WebServer.FileHttpUrl1.AbsoluteUri;
        appOptions.VersionCheckInterval = TimeSpan.FromMilliseconds(500);
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);

        await VhTestUtil.AssertEqualsWait(VersionStatus.Latest, () => app.State.VersionStatus);
    }

    [TestMethod]
    public async Task Current_is_deprecated()
    {
        SetNewRelease(new Version(CurrentAppVersion.Major, CurrentAppVersion.Minor, CurrentAppVersion.Build + 1),
            DateTime.UtcNow,
            TimeSpan.Zero, CurrentAppVersion);

        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.UpdateInfoUrl = TestHelper.WebServer.FileHttpUrl1.AbsoluteUri;
        appOptions.VersionCheckInterval = TimeSpan.FromMilliseconds(500);
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);

        await VhTestUtil.AssertEqualsWait(VersionStatus.Deprecated, () => app.State.VersionStatus);
    }

    [TestMethod]
    public async Task Current_is_old_by_job()
    {
        SetNewRelease(CurrentAppVersion, DateTime.UtcNow, TimeSpan.Zero);

        // create client
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.UpdateInfoUrl = TestHelper.WebServer.FileHttpUrl1.AbsoluteUri;
        appOptions.VersionCheckInterval = TimeSpan.FromMilliseconds(500);
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);

        // version should be latest
        await VhTestUtil.AssertEqualsWait(VersionStatus.Latest, () => app.State.VersionStatus);

        // after publish a new version it should be old
        SetNewRelease(new Version(CurrentAppVersion.Major, CurrentAppVersion.Minor, CurrentAppVersion.Build + 1),
            DateTime.UtcNow, TimeSpan.Zero);
        await VhTestUtil.AssertEqualsWait(VersionStatus.Old, () => app.State.VersionStatus);
    }

    [TestMethod]
    public async Task Current_is_old_but_wait_for_notification_delay()
    {
        SetNewRelease(new Version(CurrentAppVersion.Major, CurrentAppVersion.Minor, CurrentAppVersion.Build + 1),
            DateTime.UtcNow, TimeSpan.FromSeconds(2));

        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.UpdateInfoUrl = TestHelper.WebServer.FileHttpUrl1.AbsoluteUri;
        appOptions.VersionCheckInterval = TimeSpan.FromMilliseconds(500);
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);

        await VhTestUtil.AssertEqualsWait(VersionStatus.Latest, () => app.State.VersionStatus);
        await VhTestUtil.AssertEqualsWait(VersionStatus.Old, () => app.State.VersionStatus);
    }

    [TestMethod]
    public async Task Current_is_old_before_connect_to_vpn()
    {
        SetNewRelease(new Version(CurrentAppVersion.Major, CurrentAppVersion.Minor, CurrentAppVersion.Build + 1),
            DateTime.UtcNow, TimeSpan.Zero);

        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.UpdateInfoUrl = TestHelper.WebServer.FileHttpUrl1.AbsoluteUri;
        appOptions.VersionCheckInterval = TimeSpan.FromMilliseconds(500);
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);

        await VhTestUtil.AssertEqualsWait(VersionStatus.Old, () => app.State.VersionStatus);
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
        appOptions.UpdateInfoUrl = TestHelper.WebServer.FileHttpUrl1.AbsoluteUri;
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());

        await Task.Delay(1000);
        await VhTestUtil.AssertEqualsWait(VersionStatus.Unknown, () => app.State.VersionStatus);

        // set new version
        SetNewRelease(new Version(CurrentAppVersion.Major, CurrentAppVersion.Minor, CurrentAppVersion.Build + 1),
            DateTime.UtcNow, TimeSpan.Zero);
        await app.Connect(clientProfile.ClientProfileId);
        await app.WaitForState( AppConnectionState.Connected);
        await VhTestUtil.AssertEqualsWait(VersionStatus.Old, () => app.State.VersionStatus);
    }
}