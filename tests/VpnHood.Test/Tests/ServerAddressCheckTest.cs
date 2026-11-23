using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Client;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.VpnAdapters.Abstractions;
using VpnHood.Test.Device;
using VpnHood.Test.Extensions;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

// The name is asked of a resolver of the test's own, as the network's DNS server, over a socket the adapter protects:
// a name asked of this machine's DNS servers got no answer where they were out of reach, which is no move, so a check
// that should have found none passed whatever it did
[TestClass]
public class ServerAddressCheckTest : TestBase
{
    private const string HostName = "server.example";

    // documentation addresses, which no real name gives
    private static readonly IPAddress SessionAddress = IPAddress.Parse("192.0.2.1");
    private static readonly IPAddress MovedAddress = IPAddress.Parse("192.0.2.2");
    private static readonly IPAddress SessionAddressIpV6 = IPAddress.Parse("2001:db8::1");
    private static readonly IPAddress MovedAddressIpV6 = IPAddress.Parse("2001:db8::2");

    [TestMethod]
    public async Task An_address_the_name_still_gives_is_no_move()
    {
        using var resolver = new TestDnsResolver([SessionAddress, SessionAddressIpV6]);
        using var vpnAdapter = new TestDnsVpnAdapter([resolver.EndPoint.Address]);
        var check = CreateCheck(resolver, SessionAddress, vpnAdapter);

        Assert.IsNull(await check.TryGetNewAddresses(TestCt));
        CollectionAssert.AreEqual(new[] { AddressFamily.InterNetwork }, resolver.Queries.ToArray());
    }

    // A session at an IPv6 address had no check, and the check asked for the name's IPv4 addresses alone, which never
    // hold an IPv6 one. It asks for the session's version
    [TestMethod]
    public async Task An_IPv6_address_the_name_still_gives_is_no_move()
    {
        using var resolver = new TestDnsResolver([SessionAddress, SessionAddressIpV6]);
        using var vpnAdapter = new TestDnsVpnAdapter([resolver.EndPoint.Address]);
        var check = CreateCheck(resolver, SessionAddressIpV6, vpnAdapter);

        Assert.IsNull(await check.TryGetNewAddresses(TestCt));
        CollectionAssert.AreEqual(new[] { AddressFamily.InterNetworkV6 }, resolver.Queries.ToArray());
    }

    [TestMethod]
    [DataRow("192.0.2.1", "192.0.2.2")]
    [DataRow("2001:db8::1", "2001:db8::2")]
    public async Task An_address_the_name_no_longer_gives_is_a_move(string sessionAddress, string nameAddress)
    {
        using var resolver = new TestDnsResolver([IPAddress.Parse(nameAddress)]);
        using var vpnAdapter = new TestDnsVpnAdapter([resolver.EndPoint.Address]);
        var check = CreateCheck(resolver, IPAddress.Parse(sessionAddress), vpnAdapter);

        var newAddresses = await check.TryGetNewAddresses(TestCt);
        Assert.AreEqual(HostName, newAddresses?.HostName);
        CollectionAssert.AreEqual(new[] { IPAddress.Parse(nameAddress) }, newAddresses?.Addresses.ToArray());
    }

    // A name that moved to the other IP version alone gave the session's version an answer with no address, which was
    // read as no answer, so the session waited for good. An answer with none has the other version asked
    [TestMethod]
    [DataRow("2001:db8::1", "192.0.2.2")]
    [DataRow("192.0.2.1", "2001:db8::2")]
    public async Task A_name_that_moved_to_the_other_IP_version_is_a_move(string sessionAddress, string nameAddress)
    {
        using var resolver = new TestDnsResolver([IPAddress.Parse(nameAddress)]);
        using var vpnAdapter = new TestDnsVpnAdapter([resolver.EndPoint.Address]);
        var check = CreateCheck(resolver, IPAddress.Parse(sessionAddress), vpnAdapter);

        var newAddresses = await check.TryGetNewAddresses(TestCt);
        CollectionAssert.AreEqual(new[] { IPAddress.Parse(nameAddress) }, newAddresses?.Addresses.ToArray());
    }

    // a name with no address of either version, as one about to be published again, is no move
    [TestMethod]
    public async Task A_name_with_no_address_is_no_move()
    {
        using var resolver = new TestDnsResolver([]);
        using var vpnAdapter = new TestDnsVpnAdapter([resolver.EndPoint.Address]);
        var check = CreateCheck(resolver, SessionAddressIpV6, vpnAdapter);

        Assert.IsNull(await check.TryGetNewAddresses(TestCt));
    }

    // as in an outage of the device's own network, which leaves the name with no answer too: a server that failed the
    // query has the other version not asked
    [TestMethod]
    public async Task A_name_with_no_answer_is_no_move()
    {
        using var resolver = new TestDnsResolver([MovedAddress]) { ResponseCode = 2 };
        using var vpnAdapter = new TestDnsVpnAdapter([resolver.EndPoint.Address]);
        var check = CreateCheck(resolver, SessionAddressIpV6, vpnAdapter);

        Assert.IsNull(await check.TryGetNewAddresses(TestCt));
        CollectionAssert.AreEqual(new[] { AddressFamily.InterNetworkV6 }, resolver.Queries.ToArray());
    }

    // On a NAT64 network whose DNS servers make no AAAA answer, the device makes the server's IPv6 address from its IPv4
    // one, and the name's IPv4 address read as a move away from it. It is the session's own
    [TestMethod]
    public async Task A_name_whose_IPv4_address_the_session_reaches_by_NAT64_is_no_move()
    {
        using var resolver = new TestDnsResolver([SessionAddress]);
        using var vpnAdapter = new TestDnsVpnAdapter([resolver.EndPoint.Address]);
        var check = CreateCheck(resolver, IPAddress.Parse("64:ff9b::c000:201"), vpnAdapter);

        Assert.IsNull(await check.TryGetNewAddresses(TestCt));
        CollectionAssert.AreEqual(new[] { AddressFamily.InterNetworkV6, AddressFamily.InterNetwork },
            resolver.Queries.ToArray());
    }

    // On a network that carries IPv6 alone, behind NAT64, a session at an address made from the name's IPv4 one got
    // the name's new IPv4 address as a move, which its reconnect, with no IPv4, could not reach. It is no move there
    [TestMethod]
    public async Task A_name_that_moved_to_IPv4_is_no_move_for_a_device_without_it()
    {
        // an IPv6 DNS server, as the network has no other within reach
        using var resolver = new TestDnsResolver([MovedAddress], new IPEndPoint(IPAddress.IPv6Loopback, 0));
        using var vpnAdapter = new TestDnsVpnAdapter([resolver.EndPoint.Address]) {
            NetworkIpVersions = [IpVersion.IPv6]
        };

        var check = CreateCheck(resolver, IPAddress.Parse("64:ff9b::c000:201"), vpnAdapter);

        Assert.IsNull(await check.TryGetNewAddresses(TestCt));
        CollectionAssert.AreEqual(new[] { AddressFamily.InterNetworkV6 }, resolver.Queries.ToArray());
    }

    // a move to IPv6 alone is none for a device without IPv6, whose reconnect would find no server
    [TestMethod]
    public async Task A_name_that_moved_to_IPv6_is_no_move_for_a_device_without_it()
    {
        using var resolver = new TestDnsResolver([IPAddress.Parse("2001:db8::2")]);
        using var vpnAdapter = new TestDnsVpnAdapter([resolver.EndPoint.Address]) {
            NetworkIpVersions = [IpVersion.IPv4]
        };

        var check = CreateCheck(resolver, SessionAddress, vpnAdapter);

        Assert.IsNull(await check.TryGetNewAddresses(TestCt));
        CollectionAssert.AreEqual(new[] { AddressFamily.InterNetwork }, resolver.Queries.ToArray());
    }

    // A DNS server of an IP version the network lacks is out of reach: where an adapter protects any socket, as
    // Android's does, its query held the next server's back until it timed out, up to 5 seconds a check
    [TestMethod]
    [DataRow(IpVersion.IPv4)]
    [DataRow(IpVersion.IPv6)]
    public async Task A_DNS_server_of_an_IP_version_the_network_lacks_is_not_asked(IpVersion networkIpVersion)
    {
        // the check asks every DNS server at one port
        using var resolverIpV4 = new TestDnsResolver([MovedAddress, MovedAddressIpV6]);
        using var resolverIpV6 = new TestDnsResolver([MovedAddress, MovedAddressIpV6],
            new IPEndPoint(IPAddress.IPv6Loopback, resolverIpV4.EndPoint.Port));

        var isIpV4 = networkIpVersion == IpVersion.IPv4;
        var resolver = isIpV4 ? resolverIpV4 : resolverIpV6;
        var unreachedResolver = isIpV4 ? resolverIpV6 : resolverIpV4;
        using var vpnAdapter = new TestDnsVpnAdapter([unreachedResolver.EndPoint.Address, resolver.EndPoint.Address]) {
            NetworkIpVersions = [networkIpVersion]
        };

        var check = CreateCheck(resolver, isIpV4 ? SessionAddress : SessionAddressIpV6, vpnAdapter);

        var newAddresses = await check.TryGetNewAddresses(TestCt);
        Assert.IsEmpty(unreachedResolver.Queries);
        CollectionAssert.AreEqual(new[] { isIpV4 ? MovedAddress : MovedAddressIpV6 },
            newAddresses?.Addresses.ToArray());
    }

    // its lookup would go into the tunnel it checks
    [TestMethod]
    public async Task An_adapter_that_cannot_protect_a_socket_asks_no_name()
    {
        using var vpnAdapter = new TestUnprotectedVpnAdapter();
        var check = new ServerAddressCheck("vpnhood.com", MovedAddress, vpnAdapter);

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
        using var resolver = new TestDnsResolver([MovedAddress]);
        using var vpnAdapter = new TestDnsVpnAdapter([resolver.EndPoint.Address]);
        var check = CreateCheck(resolver, SessionAddress, vpnAdapter);
        Assert.IsNotNull(await check.TryGetNewAddresses(TestCt), "The check found no move to recover from.");

        await ((ClientSession)client.RequiredSession).CheckServerAddress(check, TestCt);

        Assert.AreEqual(ClientState.Connected, client.State);
    }

    // a check of the session's address that asks the resolver, at its own port
    private static ServerAddressCheck CreateCheck(TestDnsResolver resolver, IPAddress sessionAddress,
        IVpnAdapter vpnAdapter)
    {
        return new ServerAddressCheck(HostName, sessionAddress, vpnAdapter) { DnsPort = resolver.EndPoint.Port };
    }
}
