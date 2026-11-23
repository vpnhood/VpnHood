using System.Text.Json.Nodes;

namespace VpnHood.AppLogic.Test.Tests;

// TCP forwarding was TcpProxy, and the settings files of older versions still hold that name. A lost UseTcpProxy
// would turn the user's switch off without a word.
[TestClass]
public class TcpForwardingSettingsTest : TestAppBase
{
    [TestMethod]
    public async Task Load_settings_saved_before_the_TcpForwarding_rename()
    {
        await using var app1 = TestAppHelper.CreateClientApp();
        app1.UserSettings.DropUdp = true;
        app1.SettingsService.Save();
        await app1.DisposeAsync();

        // the file as an older version wrote it: the old names alone
        var filePath = Path.Combine(app1.StorageFolderPath, "settings.json");
        var root = JsonNode.Parse(await File.ReadAllTextAsync(filePath, TestCt)) ?? throw new InvalidDataException("No JSON.");
        var userSettings = root["UserSettings"]?.AsObject() ?? throw new InvalidDataException("No user settings.");
        userSettings.Remove("UseTcpForwarding");
        userSettings.Remove("IsTcpForwardingPrompted");
        userSettings["UseTcpProxy"] = true;
        userSettings["IsTcpProxyPrompted"] = true;
        await File.WriteAllTextAsync(filePath, root.ToJsonString(), TestCt);

        var appOptions = TestAppHelper.CreateAppOptions(storagePath: app1.StorageFolderPath);
        await using var app2 = TestAppHelper.CreateClientApp(appOptions: appOptions);
        Assert.IsTrue(app2.UserSettings.UseTcpForwarding);
        Assert.IsTrue(app2.UserSettings.IsTcpForwardingPrompted);
        Assert.IsTrue(app2.UserSettings.DropUdp);

        // the file now holds the new names alone
        Assert.DoesNotContain("TcpProxy", await File.ReadAllTextAsync(filePath, TestCt));
    }

    // The Protocols page keeps the QUIC and UDP blocks usable in the server's Cloak Mode and locks them off where only
    // the server's adapter forces TCP forwarding: the app's session info tells the two apart
    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, false)]
    public async Task The_session_says_whether_the_server_requires_TCP_forwarding(bool allowTcpPacket, bool isRequired)
    {
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions.SessionOptions.AllowTcpPacket = allowTcpPacket;
        await using var server = await TestHelper.CreateServer(fileAccessManagerOptions);
        var token = await TestHelper.CreateAccessToken(server);
        await using var app = TestAppHelper.CreateClientApp();

        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
        Assert.AreEqual(isRequired, app.State.SessionInfo?.IsTcpForwardingRequired);
    }
}
