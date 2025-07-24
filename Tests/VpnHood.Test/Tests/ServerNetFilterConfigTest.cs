using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Test.Device;

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
        clientOptions.VpnAdapterIncludeIpRanges = [IpRange.Parse("230.0.0.0-230.0.0.200")];
        await using var client = await TestHelper.CreateClient(vpnAdapter: new TestNullVpnAdapter(),
            clientOptions: clientOptions);

        Assert.IsFalse(client.VpnAdapterIncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.0")));
        Assert.IsFalse(client.VpnAdapterIncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.10")));
        Assert.IsTrue(client.VpnAdapterIncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.100")));
        Assert.IsTrue(client.VpnAdapterIncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.150")));
        Assert.IsTrue(client.VpnAdapterIncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.200")));
        Assert.IsFalse(client.VpnAdapterIncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.220")));

        Assert.IsTrue(server.SessionManager.NetFilter.BlockedIpRanges.IsInRange(IPAddress.Parse("230.0.0.50")));
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
        clientOptions.VpnAdapterIncludeIpRanges = [IpRange.Parse("230.0.0.0-230.0.0.200")];
        await using var client = await TestHelper.CreateClient(clientOptions: clientOptions,
            vpnAdapter: new TestNullVpnAdapter());

        Assert.IsTrue(client.VpnAdapterIncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.0")));
        Assert.IsTrue(client.VpnAdapterIncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.10")));
        Assert.IsFalse(client.VpnAdapterIncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.100")));
        Assert.IsFalse(client.VpnAdapterIncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.150")));
        Assert.IsFalse(client.VpnAdapterIncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.200")));

        Assert.IsFalse(client.VpnAdapterIncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.220"))); //block by client
        Assert.IsFalse(server.SessionManager.NetFilter.BlockedIpRanges.IsInRange(IPAddress.Parse("230.0.0.50")));
        Assert.IsTrue(server.SessionManager.NetFilter.BlockedIpRanges.IsInRange(IPAddress.Parse("230.0.0.220")));
    }

    [TestMethod]
    public async Task VpnAdapter_Include_Exclude_LocalNetwork()
    {
        var serverOptions = TestHelper.CreateFileAccessManagerOptions();
        serverOptions.NetFilterOptions.IncludeLocalNetwork = false;
        serverOptions.NetFilterOptions.VpnAdapterIncludeIpRanges = [IpRange.Parse("000.0.0.000 - 230.0.0.220")];
        serverOptions.NetFilterOptions.VpnAdapterExcludeIpRanges = [IpRange.Parse("230.0.0.100 - 230.0.0.250")];

        // Create Server
        await using var server = await TestHelper.CreateServer(serverOptions);
        var token = TestHelper.CreateAccessToken(server);

        // create client
        var clientOptions = TestHelper.CreateClientOptions(token: token);
        clientOptions.VpnAdapterIncludeIpRanges = IpNetwork.All.ToIpRanges().ToArray();
        clientOptions.IncludeLocalNetwork = false;
        await using var client = await TestHelper.CreateClient(clientOptions: clientOptions,
            vpnAdapter: new TestNullVpnAdapter());

        Assert.IsFalse(client.VpnAdapterIncludeIpRanges.IsInRange(IPAddress.Parse("192.168.0.100")),
            "LocalNetWorks failed");
        Assert.IsFalse(client.VpnAdapterIncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.110")),
            "Excludes failed");
        Assert.IsTrue(client.VpnAdapterIncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.50")), "Includes failed");
        Assert.IsFalse(client.VpnAdapterIncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.240")),
            "Includes failed");
        Assert.IsFalse(client.VpnAdapterIncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.254")),
            "Includes failed");

        Assert.IsTrue(server.SessionManager.NetFilter.BlockedIpRanges.IsInRange(IPAddress.Parse("192.168.0.100")));
        Assert.IsTrue(server.SessionManager.NetFilter.BlockedIpRanges.IsInRange(IPAddress.Parse("230.0.0.110")));
        Assert.IsFalse(server.SessionManager.NetFilter.BlockedIpRanges.IsInRange(IPAddress.Parse("230.0.0.50")));
        Assert.IsTrue(server.SessionManager.NetFilter.BlockedIpRanges.IsInRange(IPAddress.Parse("230.0.0.254")));
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
        clientOptions.VpnAdapterIncludeIpRanges = IpNetwork.All.ToIpRanges().ToArray();
        await using var client = await TestHelper.CreateClient(clientOptions: clientOptions,
            vpnAdapter: new TestNullVpnAdapter());

        Assert.IsFalse(client.IncludeIpRanges.IsInRange(IPAddress.Parse("130.0.0.110")), "Excludes failed");
        Assert.IsTrue(client.IncludeIpRanges.IsInRange(IPAddress.Parse("130.0.0.50")), "Includes failed");
        Assert.IsFalse(client.IncludeIpRanges.IsInRange(IPAddress.Parse("130.0.0.240")), "Includes failed");
        Assert.IsFalse(client.IncludeIpRanges.IsInRange(IPAddress.Parse("130.0.0.254")), "Includes & Excludes failed");

        Assert.IsTrue(server.SessionManager.NetFilter.BlockedIpRanges.IsInRange(IPAddress.Parse("130.0.0.110")));
        Assert.IsFalse(server.SessionManager.NetFilter.BlockedIpRanges.IsInRange(IPAddress.Parse("130.0.0.50")));
        Assert.IsTrue(server.SessionManager.NetFilter.BlockedIpRanges.IsInRange(IPAddress.Parse("130.0.0.254")));
    }
}