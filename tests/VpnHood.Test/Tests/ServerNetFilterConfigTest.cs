using System.Net;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Test.Device;
using VpnHood.Test.Extensions;

namespace VpnHood.Test.Tests;

[TestClass]
public class ServerNetFilterConfigTest : TestBase
{

    [TestMethod]
    public async Task VpnAdapter_Include()
    {
        var serverOptions = TestHelper.CreateFileAccessManagerOptions();
        serverOptions.NetFilterOptions.VpnAdapterIncludeIpRanges = [IpRange.Parse("230.0.0.100-230.0.0.250")];

        // Create Server
        await using var server = await TestHelper.CreateServer(serverOptions);
        var token = TestHelper.CreateAccessToken(server);

        // create client
        var clientOptions = TestHelper.CreateClientOptions(token: token);
        clientOptions.IncludeIpRangesByDevice = [IpRange.Parse("230.0.0.0-230.0.0.200")];
        await using var client = await TestHelper.CreateClient(vpnAdapter: new TestNullVpnAdapter(),
            clientOptions: clientOptions);

        var includeNetworks = client.RequiredSession.Config.AdapterOptions.IncludeNetworks.ToIpRanges();
        Assert.IsNotNull(includeNetworks);
        Assert.IsFalse(includeNetworks.Contains(IPAddress.Parse("230.0.0.0")));
        Assert.IsFalse(includeNetworks.Contains(IPAddress.Parse("230.0.0.10")));
        Assert.IsTrue(includeNetworks.Contains(IPAddress.Parse("230.0.0.100")));
        Assert.IsTrue(includeNetworks.Contains(IPAddress.Parse("230.0.0.150")));
        Assert.IsTrue(includeNetworks.Contains(IPAddress.Parse("230.0.0.200")));
        Assert.IsFalse(includeNetworks.Contains(IPAddress.Parse("230.0.0.220")));
        Assert.IsTrue(server.SessionManager.NetFilter.IsIpBlocked("230.0.0.50"));
    }

    [TestMethod]
    public async Task VpnAdapter_Exclude()
    {
        var serverOptions = TestHelper.CreateFileAccessManagerOptions();
        serverOptions.NetFilterOptions.VpnAdapterExcludeIpRanges = [IpRange.Parse("230.0.0.100-230.0.0.250")];

        // Create Server
        await using var server = await TestHelper.CreateServer(serverOptions);
        var token = TestHelper.CreateAccessToken(server);

        // create client
        var clientOptions = TestHelper.CreateClientOptions(token: token);
        clientOptions.IncludeIpRangesByDevice = [IpRange.Parse("230.0.0.0-230.0.0.200")];
        await using var client = await TestHelper.CreateClient(clientOptions: clientOptions,
            vpnAdapter: new TestNullVpnAdapter());

        var includeNetworks = client.RequiredSession.Config.AdapterOptions.IncludeNetworks.ToIpRanges();
        Assert.IsNotNull(includeNetworks);
        Assert.IsTrue(includeNetworks.Contains(IPAddress.Parse("230.0.0.0")));
        Assert.IsTrue(includeNetworks.Contains(IPAddress.Parse("230.0.0.10")));
        Assert.IsFalse(includeNetworks.Contains(IPAddress.Parse("230.0.0.100")));
        Assert.IsFalse(includeNetworks.Contains(IPAddress.Parse("230.0.0.150")));
        Assert.IsFalse(includeNetworks.Contains(IPAddress.Parse("230.0.0.200")));

        Assert.IsFalse(includeNetworks.Contains(IPAddress.Parse("230.0.0.220"))); //block by client
        Assert.IsFalse(server.SessionManager.NetFilter.IsIpBlocked("230.0.0.50"));
        Assert.IsTrue(server.SessionManager.NetFilter.IsIpBlocked("230.0.0.220"));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task VpnAdapter_Include_Exclude_LocalNetwork(bool serverAllowLocalNetworks)
    {
        var serverOptions = TestHelper.CreateFileAccessManagerOptions();
        serverOptions.NetFilterOptions.IncludeLocalNetwork = serverAllowLocalNetworks;
        serverOptions.NetFilterOptions.VpnAdapterIncludeIpRanges = [IpRange.Parse("000.0.0.000 - 230.0.0.220")];
        serverOptions.NetFilterOptions.VpnAdapterExcludeIpRanges = [IpRange.Parse("230.0.0.100 - 230.0.0.250")];

        // Create Server
        await using var server = await TestHelper.CreateServer(serverOptions);
        var token = TestHelper.CreateAccessToken(server);

        // create client
        var clientOptions = TestHelper.CreateClientOptions(token: token);
        clientOptions.IncludeIpRangesByDevice = IpNetwork.All.ToIpRanges().ToArray();
        clientOptions.SplitLocalNetwork = true;
        await using var client = await TestHelper.CreateClient(clientOptions: clientOptions,
            vpnAdapter: new TestNullVpnAdapter());

        // client
        Assert.AreEqual(serverAllowLocalNetworks, client.RequiredSession.Info.IsLocalNetworkAllowed);
        Assert.AreEqual(serverAllowLocalNetworks, client.RequiredSession.Config.AdapterOptions.IncludeNetworks.ToIpRanges().Contains(IPAddress.Parse("192.168.0.100")), "LocalNetWorks failed");

        var ipRanges = client.RequiredSession.Config.AdapterOptions.IncludeNetworks.ToIpRanges();
        Assert.IsNotNull(ipRanges);
        Assert.IsFalse(ipRanges.Contains(IPAddress.Parse("230.0.0.110")), "Excludes failed");
        Assert.IsTrue(ipRanges.Contains(IPAddress.Parse("230.0.0.50")), "Includes failed");
        Assert.IsFalse(ipRanges.Contains(IPAddress.Parse("230.0.0.240")), "Includes failed");
        Assert.IsFalse(ipRanges.Contains(IPAddress.Parse("230.0.0.254")), "Includes failed");

        // server
        Assert.AreNotEqual(serverAllowLocalNetworks,server.SessionManager.NetFilter.IsIpBlocked("192.168.0.100"));
        Assert.IsTrue(server.SessionManager.NetFilter.IsIpBlocked("230.0.0.110"));
        Assert.IsFalse(server.SessionManager.NetFilter.IsIpBlocked("230.0.0.50"));
        Assert.IsTrue(server.SessionManager.NetFilter.IsIpBlocked("230.0.0.254"));
    }

    [TestMethod]
    public async Task IpRange_Include_Exclude()
    {
        var serverOptions = TestHelper.CreateFileAccessManagerOptions();
        serverOptions.NetFilterOptions.IncludeIpRanges = [IpRange.Parse("000.0.0.000 - 130.0.0.220")];
        serverOptions.NetFilterOptions.ExcludeIpRanges = [IpRange.Parse("130.0.0.100 - 130.0.0.250")];

        // Create Server
        await using var server = await TestHelper.CreateServer(serverOptions);
        var token = TestHelper.CreateAccessToken(server);

        // create client
        var clientOptions = TestHelper.CreateClientOptions(token: token);
        clientOptions.IncludeIpRangesByDevice = IpNetwork.All.ToIpRanges().ToArray();
        await using var client = await TestHelper.CreateClient(clientOptions: clientOptions,
            vpnAdapter: new TestNullVpnAdapter());

        Assert.IsFalse(client.SessionIncludeIpRangesByApp.Contains(IPAddress.Parse("130.0.0.110")), "Excludes failed");
        Assert.IsTrue(client.SessionIncludeIpRangesByApp.Contains(IPAddress.Parse("130.0.0.50")), "Includes failed");
        Assert.IsFalse(client.SessionIncludeIpRangesByApp.Contains(IPAddress.Parse("130.0.0.240")), "Includes failed");
        Assert.IsFalse(client.SessionIncludeIpRangesByApp.Contains(IPAddress.Parse("130.0.0.254")), "Includes & Excludes failed");

        Assert.IsTrue(server.SessionManager.NetFilter.IsIpBlocked("130.0.0.110"));
        Assert.IsFalse(server.SessionManager.NetFilter.IsIpBlocked("130.0.0.50"));
        Assert.IsTrue(server.SessionManager.NetFilter.IsIpBlocked("130.0.0.254"));
    }
}