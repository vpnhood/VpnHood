using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.App;
using VpnHood.Core.Client;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Test.Device;

namespace VpnHood.AppLib.Test.Tests;

// A network with the other IP version alone, which never reaches the session's server address: the VPN service
// connects again by itself, finding an address the network has, and the app sees no end.
[TestClass]
public class ServerIpVersionTest : TestAppBase
{
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

    // the network the session runs over has the other IP version alone from now on; the next connect's adapter sees
    // the machine's
    private static ClientSession LoseServerIpVersion(TestVpnService vpnService, VpnHoodClient client)
    {
        var vpnAdapter = vpnService.CurrentVpnAdapter as TestVpnAdapter ??
                         throw new InvalidOperationException("The VPN service has no test adapter.");
        var session = (ClientSession)client.RequiredSession;
        vpnAdapter.NetworkIpVersions = [
            session.VpnEndPoint.TcpEndPoint.IpVersion() == IpVersion.IPv4 ? IpVersion.IPv6 : IpVersion.IPv4
        ];
        return session;
    }

    [TestMethod]
    public async Task Service_connects_again_when_the_network_lacks_the_servers_ip_version()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var device = TestAppHelper.CreateDevice();
        await using var app = await ConnectApp(device, token.ToAccessKey());
        var vpnService = GetVpnService(device);
        var client = GetClient(vpnService);
        var session = LoseServerIpVersion(vpnService, client);

        Assert.IsTrue(await session.EndIfServerIpVersionUnsupported());

        await AssertEqualsWait(true,
            () => vpnService.LastClient != client && vpnService.LastClient?.State == ClientState.Connected);
        await app.WaitForState(AppConnectionState.Connected, TestCt);
        Assert.IsNull(app.State.LastError);
    }

    [TestMethod]
    public async Task Service_that_cannot_connect_by_itself_ends_with_the_error()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var device = TestAppHelper.CreateDevice();
        await using var app = await ConnectApp(device, token.ToAccessKey());
        var vpnService = GetVpnService(device);
        var client = GetClient(vpnService);
        vpnService.CanReconnect = false;
        var session = LoseServerIpVersion(vpnService, client);

        Assert.IsTrue(await session.EndIfServerIpVersionUnsupported());

        await app.WaitForState(AppConnectionState.None, TestCt);
        Assert.AreEqual(nameof(ServerIpVersionUnsupportedException), app.State.LastError?.TypeName);
        Assert.AreSame(client, vpnService.LastClient);
    }
}
