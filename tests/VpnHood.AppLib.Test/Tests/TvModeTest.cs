using VpnHood.AppLib.App.Utils;

namespace VpnHood.AppLib.Test.Tests;

// What a desktop window's switch to the TV layout reads: the running app follows the /tv-mode debug
// command as its settings are saved, through the API the window reads it by.
[TestClass]
public class TvModeTest : TestAppBase
{
    [TestMethod]
    public async Task Debug_command_switches_the_running_app_and_back()
    {
        await using var app = TestAppHelper.CreateClientApp();
        Assert.IsFalse(app.Descriptor.IsTv);

        app.UserSettings.DebugData1 = DebugCommands.TvMode;
        await app.Api.App.SetUserSettings(app.UserSettings, TestCt);
        Assert.IsTrue((await app.Api.App.GetInfo(TestCt)).Descriptor.IsTv);

        app.UserSettings.DebugData1 = null;
        await app.Api.App.SetUserSettings(app.UserSettings, TestCt);
        Assert.IsFalse((await app.Api.App.GetInfo(TestCt)).Descriptor.IsTv);
    }
}
