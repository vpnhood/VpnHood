using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Server.Access;
using VpnHood.Core.Server.Access.Managers.HttpAccessManagers;
using VpnHood.Test.AccessManagers;

namespace VpnHood.Test.Tests;

[TestClass]
public class StatusAddressFamilyHandlerTest : TestBase
{
    private static readonly IReadOnlyList<AddressFamily> BothFamilies =
        [AddressFamily.InterNetwork, AddressFamily.InterNetworkV6];

    // the access manager sees both of a server's addresses, from the calls that come over each
    [TestMethod]
    public async Task Status_calls_take_turns_over_the_address_families()
    {
        await AssertIpV6Loopback();
        using var accessManagerServer = new TestDualStackAccessManagerServer(listenIpV6: true);
        using var accessManager = CreateAccessManager(accessManagerServer, BothFamilies);

        await SendStatuses(accessManager, count: 4);

        CollectionAssert.AreEqual(
            new[] {
                AddressFamily.InterNetwork, AddressFamily.InterNetworkV6,
                AddressFamily.InterNetwork, AddressFamily.InterNetworkV6
            },
            accessManagerServer.GetCallFamilies("status").ToArray());
    }

    // a family that does not connect costs a status nothing but the wait
    [TestMethod]
    public async Task A_status_goes_over_the_other_family_when_its_own_does_not_connect()
    {
        using var accessManagerServer = new TestDualStackAccessManagerServer(listenIpV6: false);
        using var accessManager = CreateAccessManager(accessManagerServer, BothFamilies);

        await SendStatuses(accessManager, count: 4);

        CollectionAssert.AreEqual(
            Enumerable.Repeat(AddressFamily.InterNetwork, 4).ToArray(),
            accessManagerServer.GetCallFamilies("status").ToArray());
    }

    [TestMethod]
    public async Task Status_calls_given_ipv4_alone_keep_to_it()
    {
        await AssertIpV6Loopback();
        using var accessManagerServer = new TestDualStackAccessManagerServer(listenIpV6: true);
        using var accessManager = CreateAccessManager(accessManagerServer, [AddressFamily.InterNetwork]);

        await SendStatuses(accessManager, count: 4);

        CollectionAssert.AreEqual(
            Enumerable.Repeat(AddressFamily.InterNetwork, 4).ToArray(),
            accessManagerServer.GetCallFamilies("status").ToArray());
    }

    [TestMethod]
    public async Task Other_calls_keep_one_family()
    {
        await AssertIpV6Loopback();
        using var accessManagerServer = new TestDualStackAccessManagerServer(listenIpV6: true);
        using var accessManager = CreateAccessManager(accessManagerServer, BothFamilies);

        for (var i = 0; i < 4; i++)
            await accessManager.Server_Configure(new ServerInfo {
                Version = new Version(1, 0),
                EnvironmentVersion = Environment.Version,
                PrivateIpAddresses = [],
                PublicIpAddresses = [],
                Status = new ServerStatus()
            }, CancellationToken.None);

        var families = accessManagerServer.GetCallFamilies("configure");
        Assert.HasCount(4, families);
        Assert.HasCount(1, families.Distinct().ToArray());
    }

    private static HttpAccessManager CreateAccessManager(TestDualStackAccessManagerServer accessManagerServer,
        IReadOnlyList<AddressFamily> addressFamilies)
    {
        return new HttpAccessManager(new HttpClient(new StatusAddressFamilyHandler(addressFamilies)),
            new HttpAccessManagerOptions(accessManagerServer.BaseUri, "Bearer test"));
    }

    private static async Task SendStatuses(HttpAccessManager accessManager, int count)
    {
        for (var i = 0; i < count; i++)
            await accessManager.Server_UpdateStatus(new ServerStatus(), CancellationToken.None);
    }

    private static async Task AssertIpV6Loopback()
    {
        var addresses = await Dns.GetHostAddressesAsync("localhost");
        if (!Socket.OSSupportsIPv6 || !addresses.Contains(IPAddress.IPv6Loopback))
            Assert.Inconclusive("localhost has no IPv6 loopback on this machine.");
    }
}
