using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Client;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Test.Device;
using VpnHood.Test.Extensions;

namespace VpnHood.Test.Tests;

// Asks a real name of this machine's DNS servers, as EndPointResolverTests do, over a socket the adapter protects.
[TestClass]
public class ServerAddressCheckTest : TestBase
{
    private const string HostName = "vpnhood.com";

    // a documentation address, which vpnhood.com never gives
    private static readonly IPAddress MovedAddress = IPAddress.Parse("192.0.2.1");

    [TestMethod]
    public async Task An_address_the_name_still_gives_is_no_move()
    {
        var addresses = await Dns.GetHostAddressesAsync(HostName, AddressFamily.InterNetwork, TestCt);
        using var vpnAdapter = new TestNullVpnAdapter();
        var check = new ServerAddressCheck(HostName, addresses.First(), vpnAdapter);

        Assert.IsNull(await check.TryGetNewAddresses(TestCt));
    }

    [TestMethod]
    public async Task An_address_the_name_no_longer_gives_is_a_move()
    {
        using var vpnAdapter = new TestNullVpnAdapter();
        var check = new ServerAddressCheck(HostName, MovedAddress, vpnAdapter);

        var newAddresses = await check.TryGetNewAddresses(TestCt);
        Assert.AreEqual(HostName, newAddresses?.HostName);
        Assert.IsNotEmpty(newAddresses?.Addresses ?? []);
    }

    // as in an outage of the device's own network, which leaves the name with no answer too
    [TestMethod]
    public async Task A_name_with_no_answer_is_no_move()
    {
        using var vpnAdapter = new TestNullVpnAdapter();
        var check = new ServerAddressCheck("no-such-name.invalid", MovedAddress, vpnAdapter);

        Assert.IsNull(await check.TryGetNewAddresses(TestCt));
    }

    // its lookup would go into the tunnel it checks
    [TestMethod]
    public async Task An_adapter_that_cannot_protect_a_socket_asks_no_name()
    {
        using var vpnAdapter = new TestUnprotectedVpnAdapter();
        var check = new ServerAddressCheck(HostName, MovedAddress, vpnAdapter);

        Assert.IsNull(await check.TryGetNewAddresses(TestCt));
    }

    // a request answered meanwhile: the server is still where the session is, whatever its name says
    [TestMethod]
    public async Task A_session_that_recovered_is_not_ended()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(token, vpnAdapter: new TestNullVpnAdapter());
        await client.WaitForState(ClientState.Connected, TestCt);
        using var vpnAdapter = new TestNullVpnAdapter();

        await ((ClientSession)client.RequiredSession).CheckServerAddress(
            new ServerAddressCheck(HostName, MovedAddress, vpnAdapter), TestCt);

        Assert.AreEqual(ClientState.Connected, client.State);
    }
}
