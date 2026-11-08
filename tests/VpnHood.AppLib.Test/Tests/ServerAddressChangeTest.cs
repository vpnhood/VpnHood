using System.Net;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.App;
using VpnHood.Core.Client;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Server;
using VpnHood.Net.VpnAdapters.Abstractions;
using VpnHood.Test.Device;
using VpnHood.Test.Providers;

namespace VpnHood.AppLib.Test.Tests;

// A server that moved from the address its name gave the session: the VPN service connects again by itself, and the
// app sees no end. The move is played by a name that answers without the session's address.
[TestClass]
public class ServerAddressChangeTest : TestAppBase
{
    // vpnhood.com never answers with a documentation address
    private static ServerAddressCheck CreateMovedServerCheck(IVpnAdapter vpnAdapter) =>
        new("vpnhood.com", IPAddress.Parse("192.0.2.1"), vpnAdapter);

    private async Task<VpnHoodApp> ConnectApp(TestDevice device, string accessKey)
    {
        var app = TestAppHelper.CreateClientApp(device: device);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(accessKey);
        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected, TestCt);
        return app;
    }

    private static TestVpnService GetVpnService(TestDevice device) =>
        device.VpnService ?? throw new InvalidOperationException("The VPN service has not started.");

    private static VpnHoodClient GetClient(TestVpnService vpnService) =>
        vpnService.LastClient ?? throw new InvalidOperationException("The VPN service made no client.");

    // the session's server stops answering, as one that moved does: a request fails and the session is unstable,
    // which a move's check needs, as it leaves a session that answers alone
    private async Task StopAnswering(VpnHoodServer server, VpnHoodClient client)
    {
        await server.DisposeAsync();
        await Assert.ThrowsAsync<Exception>(() => client.UpdateSessionStatus(TestCt));
        Assert.AreEqual(ClientState.Unstable, client.State);
    }

    [TestMethod]
    public async Task Service_connects_again_after_the_server_moved()
    {
        using var accessManager = await TestHelper.CreateAccessManager();
        var token = await TestHelper.CreateAccessToken(accessManager);
        var device = TestAppHelper.CreateDevice();
        await using var server = await TestHelper.CreateServer(accessManager);
        await using var app = await ConnectApp(device, token.ToAccessKey());
        var vpnService = GetVpnService(device);
        var client = GetClient(vpnService);
        await StopAnswering(server, client);

        // where the reconnect finds it: the token's endpoints play the name's new answer
        await using var movedServer = await TestHelper.CreateServer(accessManager);
        using var vpnAdapter = new TestNullVpnAdapter();
        await ((ClientSession)client.RequiredSession).CheckServerAddress(CreateMovedServerCheck(vpnAdapter), TestCt);

        await AssertEqualsWait(true,
            () => vpnService.LastClient != client && vpnService.LastClient?.State == ClientState.Connected);
        await app.WaitForState(AppConnectionState.Connected, TestCt);
        Assert.IsNull(app.State.LastError);
    }

    [TestMethod]
    public async Task Service_that_cannot_connect_by_itself_ends_with_the_move()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var device = TestAppHelper.CreateDevice();
        await using var app = await ConnectApp(device, token.ToAccessKey());
        var vpnService = GetVpnService(device);
        var client = GetClient(vpnService);
        vpnService.CanReconnect = false;
        await StopAnswering(server, client);

        using var vpnAdapter = new TestNullVpnAdapter();
        await ((ClientSession)client.RequiredSession).CheckServerAddress(CreateMovedServerCheck(vpnAdapter), TestCt);

        await app.WaitForState(AppConnectionState.None, TestCt);
        Assert.AreEqual(nameof(ServerAddressChangedException), app.State.LastError?.TypeName);
        Assert.AreSame(client, vpnService.LastClient);
    }
}
