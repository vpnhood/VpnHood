using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.App.Utils;
using VpnHood.AppLib.Test.Providers;
using VpnHood.Core.Client.Devices.Abstractions;
using VpnHood.Test.Device;
using VpnHood.Test.Providers;

namespace VpnHood.AppLib.Test.Tests;

// The user agent the app builds from what a browser on the device sends, which Google Analytics parses:
// the trackers' names the device's model, the server's does not, as the server logs what it gets
// whatever the usage-data switch says.
[TestClass]
[DoNotParallelize] // reads the process-global recording factory
public class UserAgentTest : TestAppBase
{
    private static readonly DeviceUserAgentInfo AndroidPhone = new() {
        Platform = "Linux; Android 14",
        Model = "Pixel 5",
        Browser = DeviceUserAgentInfo.GetChromeBrowser(isMobile: true)
    };

    [TestMethod]
    public void A_user_agent_is_the_browser_s_with_the_app_after_it()
    {
        var version = new Version(8, 2, 854, 0);
        Assert.AreEqual(
            "Mozilla/5.0 (Linux; Android 14; Pixel 5) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/150.0.0.0 Mobile Safari/537.36 VpnHoodConnect/8.2.854",
            AppUtils.BuildUserAgent(AndroidPhone, "VpnHoodConnect", version, includeModel: true));
        Assert.AreEqual(
            "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/150.0.0.0 Mobile Safari/537.36 VpnHoodConnect/8.2.854",
            AppUtils.BuildUserAgent(AndroidPhone, "VpnHoodConnect", version, includeModel: false));
    }

    [TestMethod]
    public void A_model_keeps_only_what_the_trackers_header_takes()
    {
        var device = new DeviceUserAgentInfo {
            Platform = "Linux; Android 14",
            Model = "Galaxy (S24); Ультра 5G",
            Browser = DeviceUserAgentInfo.GetChromeBrowser(isMobile: true)
        };

        var userAgent = AppUtils.BuildUserAgent(device, "VpnHoodConnect", new Version(8, 2, 854), includeModel: true);
        StringAssert.Contains(userAgent, "(Linux; Android 14; Galaxy S24 5G)");

        // as the trackers send it, which refuses what it cannot parse
        using var request = new HttpRequestMessage();
        request.Headers.Add("User-Agent", userAgent);
    }

    [TestMethod]
    public async Task The_trackers_name_the_model_and_the_server_does_not()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);

        // a Release build, since a Debug build makes no tracker
        RecordingTrackerFactory.Made.Clear();
        var appOptions = TestAppHelper.CreateAppOptions(isDebugMode: false);
        appOptions.TrackerFactories = [new RecordingTrackerFactory()];
        var device = new TestDevice(TestAppHelper, _ => new TestNullVpnAdapter()) { UserAgentInfo = AndroidPhone };
        await using var app = TestAppHelper.CreateClientApp(appOptions, device);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected);

        // the app's tracker, and the VPN service's, made at the connect from the saved options
        var made = RecordingTrackerFactory.Made.ToArray();
        Assert.HasCount(2, made);
        foreach (var (_, createParams, _) in made)
            StringAssert.Contains(createParams.UserAgent, "(Linux; Android 14; Pixel 5)");

        var session = accessManager.SessionService.Sessions.Values.Single();
        StringAssert.Contains(session.ClientInfo.UserAgent, "(Linux; Android 14)");
    }
}
