using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using VpnHood.Client.App;

namespace VpnHood.Test.Tests;

[TestClass]
public class CheckNewVersionTest
{
    private static void SetNewRelease(Version version, DateTime releaseDate, TimeSpan? notificationDelay = default, Version? deprecatedVersion = default)
    {
        deprecatedVersion ??= new Version(1, 0, 0);
        notificationDelay ??= TimeSpan.Zero;

        var publishInfo = new PublishInfo(version,
            TestHelper.WebServer.FileHttpUrl1,
            new Uri("https://localhost/package.msi"),
            new Uri("https://localhost/page.html"),
            deprecatedVersion,
            releaseDate,
            notificationDelay.Value);

        TestHelper.WebServer.FileContent1 = JsonSerializer.Serialize(publishInfo);
    }

    private static Version CurrentAppVersion => typeof(VpnHoodApp).Assembly.GetName().Version!;

    [TestMethod]
    public async Task Remote_is_unknown_if_remote_is_unreachable()
    {
        var appOptions = TestHelper.CreateClientAppOptions();
        appOptions.UpdateInfoUrl = new Uri("https://localhost:39999");
        await using var app = TestHelper.CreateClientApp(appOptions: appOptions);

        await Task.Delay(1000);
        await TestHelper.AssertEqualsWait(app, VersionStatus.Unknown, x => x.VersionStatus);
    }


    [TestMethod]
    public async Task Current_is_latest()
    {
        SetNewRelease(CurrentAppVersion, DateTime.UtcNow, TimeSpan.Zero);

        var appOptions = TestHelper.CreateClientAppOptions();
        appOptions.UpdateInfoUrl = TestHelper.WebServer.FileHttpUrl1;
        await using var app = TestHelper.CreateClientApp(appOptions: appOptions);

        await TestHelper.AssertEqualsWait(app, VersionStatus.Latest, x => x.VersionStatus);
    }

    [TestMethod]
    public async Task Current_is_deprecated()
    {
        SetNewRelease(new Version(CurrentAppVersion.Major, CurrentAppVersion.Minor, CurrentAppVersion.Build + 1), DateTime.UtcNow,
            TimeSpan.Zero, CurrentAppVersion);

        var appOptions = TestHelper.CreateClientAppOptions();
        appOptions.UpdateInfoUrl = TestHelper.WebServer.FileHttpUrl1;
        await using var app = TestHelper.CreateClientApp(appOptions: appOptions);

        await TestHelper.AssertEqualsWait(app, VersionStatus.Deprecated, x => x.VersionStatus);
    }

    [TestMethod]
    public async Task Current_is_old_by_job()
    {
        SetNewRelease(CurrentAppVersion, DateTime.UtcNow, TimeSpan.Zero);

        // create client
        var appOptions = TestHelper.CreateClientAppOptions();
        appOptions.UpdateInfoUrl = TestHelper.WebServer.FileHttpUrl1;
        appOptions.UpdateCheckerInterval = TimeSpan.FromMilliseconds(500);
        await using var app = TestHelper.CreateClientApp(appOptions: appOptions);

        // version should be latest
        await TestHelper.AssertEqualsWait(app, VersionStatus.Latest, x => x.VersionStatus);

        // after publish a new version it should be old
        SetNewRelease(new Version(CurrentAppVersion.Major, CurrentAppVersion.Minor, CurrentAppVersion.Build + 1),
            DateTime.UtcNow, TimeSpan.Zero);
        await TestHelper.AssertEqualsWait(app, VersionStatus.Old, x => x.VersionStatus);
    }

    [TestMethod]
    public async Task Current_is_old_but_wait_for_notification_delay()
    {
        SetNewRelease(new Version(CurrentAppVersion.Major, CurrentAppVersion.Minor, CurrentAppVersion.Build + 1),
            DateTime.UtcNow, TimeSpan.FromSeconds(2));

        var appOptions = TestHelper.CreateClientAppOptions();
        appOptions.UpdateInfoUrl = TestHelper.WebServer.FileHttpUrl1;
        appOptions.UpdateCheckerInterval= TimeSpan.FromMilliseconds(500);
        await using var app = TestHelper.CreateClientApp(appOptions: appOptions);

        await TestHelper.AssertEqualsWait(app, VersionStatus.Latest, x => x.VersionStatus);
        await TestHelper.AssertEqualsWait(app, VersionStatus.Old, x => x.VersionStatus);
    }


    [TestMethod]
    public async Task Current_is_old_before_connect_to_vpn()
    {
        SetNewRelease(new Version(CurrentAppVersion.Major, CurrentAppVersion.Minor, CurrentAppVersion.Build + 1),
            DateTime.UtcNow, TimeSpan.Zero);

        var appOptions = TestHelper.CreateClientAppOptions();
        appOptions.UpdateInfoUrl = TestHelper.WebServer.FileHttpUrl1;
        await using var app = TestHelper.CreateClientApp(appOptions: appOptions);

        await TestHelper.AssertEqualsWait(app, VersionStatus.Old, x => x.VersionStatus);
    }

    [TestMethod]
    public async Task Current_is_old_after_connect_to_vpn()
    {
        // create server and token
        await using var server = TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // Set invalid file version to raise error
        TestHelper.WebServer.FileContent1 = Guid.NewGuid().ToString();

        // create client app
        var appOptions = TestHelper.CreateClientAppOptions();
        appOptions.UpdateInfoUrl = TestHelper.WebServer.FileHttpUrl1;
        await using var app = TestHelper.CreateClientApp(appOptions: appOptions);
        var clientProfile = app.ClientProfileStore.AddAccessKey(token.ToAccessKey());

        await Task.Delay(1000);
        await TestHelper.AssertEqualsWait(app, VersionStatus.Unknown, x => x.VersionStatus);

        // set new version
        SetNewRelease(new Version(CurrentAppVersion.Major, CurrentAppVersion.Minor, CurrentAppVersion.Build + 1), DateTime.UtcNow, TimeSpan.Zero);
        await app.Connect(clientProfile.ClientProfileId);
        TestHelper.WaitForClientState(app, AppConnectionState.Connected);
        await TestHelper.AssertEqualsWait(app, VersionStatus.Old, x => x.VersionStatus);
    }
}