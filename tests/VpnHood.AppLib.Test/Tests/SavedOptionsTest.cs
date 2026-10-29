using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.SplitTunneling;
using VpnHood.AppLib.Api.VpnProfiles;
using VpnHood.AppLib.App;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Server;
using VpnHood.Test.Device;

namespace VpnHood.AppLib.Test.Tests;

// The options a start without the app runs with - the system's VPN settings, the tile, always-on, the
// system restarting the service - follow what the app shows, connected or not: its settings, its server
// and the server's location; with no server chosen there are none. Only a connect makes the first.
[TestClass]
public class SavedOptionsTest : TestAppBase
{
    [TestMethod]
    public async Task A_change_made_while_disconnected_reaches_the_saved_options()
    {
        await using var server = await TestHelper.CreateServer();
        var device = new TestDevice(TestAppHelper, _ => new TestNullVpnAdapter());
        await using var app = await CreateConnectedOnceApp(server, device);
        var serviceOptionsFile = new VpnServiceOptionsFile(device.VpnServiceConfigFolder);

        // a setting
        var useLocalNetwork = !app.UserSettings.SplitTunneling.UseLocalNetwork;
        app.UserSettings.SplitTunneling.UseLocalNetwork = useLocalNetwork;
        app.SettingsService.Save();
        await AssertEqualsWait(useLocalNetwork,
            () => serviceOptionsFile.TryRead(TestCt)?.ClientOptions.SplitLocalNetwork,
            "the saved options take Split Local Network");

        // a split list of its own file, which only flags a running session for a reconnect
        app.SettingsService.SplitIpViaDeviceSettings.Set(new SplitIpsViaDevice {
            Includes = "10.20.30.0/24",
            Excludes = ""
        });
        await AssertEqualsWait("10.20.30.0-10.20.30.255",
            () => string.Join(",", serviceOptionsFile.TryRead(TestCt)?.ClientOptions.IncludeIpRangesByDevice ?? []),
            "the saved options take the device's split IPs");
    }

    [TestMethod]
    public async Task Another_location_or_server_reaches_the_saved_options()
    {
        await using var server = await TestHelper.CreateServer();
        var device = new TestDevice(TestAppHelper, _ => new TestNullVpnAdapter());
        var token = TestHelper.CreateAccessToken(server);
        token.ServerToken.ServerLocations = ["US", "FR/Paris"];
        await using var app = await CreateConnectedOnceApp(server, device, token.ToAccessKey());
        var serviceOptionsFile = new VpnServiceOptionsFile(device.VpnServiceConfigFolder);

        // a location chosen in the server's list
        var vpnProfileId = app.UserSettings.VpnProfileId ?? throw new InvalidOperationException("No server chosen.");
        app.VpnProfileService.Update(vpnProfileId, new VpnProfileUpdateParams { SelectedLocation = "FR/Paris" });
        await AssertEqualsWait("FR/Paris", () => serviceOptionsFile.TryRead(TestCt)?.ClientOptions.ServerLocation,
            "the saved options take the location");

        // another server chosen
        var otherToken = TestHelper.CreateAccessToken(server);
        var otherProfile = app.VpnProfileService.ImportAccessKey(otherToken.ToAccessKey());
        app.UserSettings.VpnProfileId = otherProfile.VpnProfileId;
        app.SettingsService.Save();
        await AssertEqualsWait(otherToken.ToAccessKey(),
            () => serviceOptionsFile.TryRead(TestCt)?.ClientOptions.AccessKey,
            "the saved options take the server");
    }

    [TestMethod]
    public async Task With_its_server_removed_the_saved_options_go()
    {
        await using var server = await TestHelper.CreateServer();
        var device = new TestDevice(TestAppHelper, _ => new TestNullVpnAdapter());
        await using var app = await CreateConnectedOnceApp(server, device);
        var serviceOptionsFile = new VpnServiceOptionsFile(device.VpnServiceConfigFolder);
        Assert.IsTrue(serviceOptionsFile.Exists);

        var vpnProfileId = app.UserSettings.VpnProfileId ?? throw new InvalidOperationException("No server chosen.");
        app.VpnProfileService.Delete(vpnProfileId);
        await AssertEqualsWait(false, () => serviceOptionsFile.Exists, "no saved options with no server");
    }

    [TestMethod]
    public async Task Before_the_first_connect_a_change_saves_no_options()
    {
        await using var server = await TestHelper.CreateServer();
        var device = new TestDevice(TestAppHelper, _ => new TestNullVpnAdapter());
        await using var app = TestAppHelper.CreateClientApp(device: device);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(TestHelper.CreateAccessToken(server).ToAccessKey());
        app.UserSettings.VpnProfileId = vpnProfile.VpnProfileId;
        app.UserSettings.SplitTunneling.UseLocalNetwork = !app.UserSettings.SplitTunneling.UseLocalNetwork;
        app.SettingsService.Save();

        // one at a time, so this one comes after those the change set off
        await app.ReconfigureVpnService(TestCt);
        Assert.IsFalse(new VpnServiceOptionsFile(device.VpnServiceConfigFolder).Exists);
    }

    // the app's own connect makes the saved options; the changes after come with it disconnected
    private async Task<VpnHoodApp> CreateConnectedOnceApp(VpnHoodServer server, TestDevice device,
        string? accessKey = null)
    {
        var app = TestAppHelper.CreateClientApp(device: device);
        try {
            var vpnProfile = app.VpnProfileService.ImportAccessKey(
                accessKey ?? TestHelper.CreateAccessToken(server).ToAccessKey());
            await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
            await app.WaitForState(AppConnectionState.Connected);
            await app.Disconnect();
            await app.WaitForState(AppConnectionState.None);
            return app;
        }
        catch {
            await app.DisposeAsync();
            throw;
        }
    }
}
