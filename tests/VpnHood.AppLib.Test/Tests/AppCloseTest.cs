using VpnHood.AppLib.Api.App;

namespace VpnHood.AppLib.Test.Tests;

// The app's two closes on a connected app: the graceful one disconnects first (the test app's DisconnectOnDispose),
// the quick one does not wait.
[TestClass]
public class AppCloseTest : TestAppBase
{
    [TestMethod]
    public async Task A_second_graceful_close_waits_for_the_first_ones_end()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);
        await using var app = TestAppHelper.CreateClientApp();
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected);

        // the second close comes while the first disconnects
        var firstClose = app.DisposeAsync().AsTask();
        // ReSharper disable once DisposeOnUsingVariable
        await app.DisposeAsync();
        Assert.IsTrue(firstClose.IsCompleted, "The second close should end after the first one.");
    }

    // as the desktop daemon's stop deadline does
    [TestMethod]
    public async Task A_quick_close_ends_a_graceful_ones_wait_for_the_vpn_service()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);
        await using var app = TestAppHelper.CreateClientApp();
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected);

        // the graceful close waits for the VPN service to stop when the quick one comes
        var gracefulClose = app.DisposeAsync().AsTask();
        // ReSharper disable once DisposeOnUsingVariable
        app.Dispose();

        // well before the stop's own wait gives up, after 5 s
        await gracefulClose.WaitAsync(TimeSpan.FromSeconds(3), TestCt);
    }
}
