using System.Text.Json.Nodes;

namespace VpnHood.AppLib.Test.Tests;

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
}
