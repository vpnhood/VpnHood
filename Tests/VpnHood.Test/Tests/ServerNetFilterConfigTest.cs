using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Core.Common.Net;
using VpnHood.Test.Device;

namespace VpnHood.Test.Tests;

[TestClass]
public class ServerNetFilterConfigTest : TestBase
{
    [TestMethod]
    public async Task PacketCapture_Include()
    {
        var serverOptions = TestHelper.CreateFileAccessManagerOptions();
        serverOptions.NetFilterOptions.PacketCaptureIncludeIpRanges = [IpRange.Parse("230.0.0.100-230.0.0.250")];

        // Create Server
        await using var server = await TestHelper.CreateServer(serverOptions);
        var token = TestHelper.CreateAccessToken(server);

        // create client
        var clientOptions = TestHelper.CreateClientOptions();
        clientOptions.PacketCaptureIncludeIpRanges = new IpRangeOrderedList([IpRange.Parse("230.0.0.0-230.0.0.200")]);
        await using var client = await TestHelper.CreateClient(token: token, packetCapture: new TestNullPacketCapture(), clientOptions: clientOptions);

        Assert.IsFalse(client.PacketCaptureIncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.0")));
        Assert.IsFalse(client.PacketCaptureIncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.10")));
        Assert.IsTrue(client.PacketCaptureIncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.100")));
        Assert.IsTrue(client.PacketCaptureIncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.150")));
        Assert.IsTrue(client.PacketCaptureIncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.200")));
        Assert.IsFalse(client.PacketCaptureIncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.220")));

        Assert.IsTrue(server.SessionManager.NetFilter.BlockedIpRanges.IsInRange(IPAddress.Parse("230.0.0.50")));
    }

    [TestMethod]
    public async Task PacketCapture_Exclude()
    {
        var serverOptions = TestHelper.CreateFileAccessManagerOptions();
        serverOptions.NetFilterOptions.PacketCaptureExcludeIpRanges = [IpRange.Parse("230.0.0.100-230.0.0.250")];

        // Create Server
        await using var server = await TestHelper.CreateServer(serverOptions);
        var token = TestHelper.CreateAccessToken(server);

        // create client
        var clientOptions = TestHelper.CreateClientOptions();
        clientOptions.PacketCaptureIncludeIpRanges = new IpRangeOrderedList([IpRange.Parse("230.0.0.0-230.0.0.200")]);
        await using var client = await TestHelper.CreateClient(token: token, clientOptions: clientOptions, packetCapture: new TestNullPacketCapture());

        Assert.IsTrue(client.PacketCaptureIncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.0")));
        Assert.IsTrue(client.PacketCaptureIncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.10")));
        Assert.IsFalse(client.PacketCaptureIncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.100")));
        Assert.IsFalse(client.PacketCaptureIncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.150")));
        Assert.IsFalse(client.PacketCaptureIncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.200")));

        Assert.IsFalse(client.PacketCaptureIncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.220"))); //block by client
        Assert.IsFalse(server.SessionManager.NetFilter.BlockedIpRanges.IsInRange(IPAddress.Parse("230.0.0.50")));
        Assert.IsTrue(server.SessionManager.NetFilter.BlockedIpRanges.IsInRange(IPAddress.Parse("230.0.0.220")));
    }

    [TestMethod]
    public async Task PacketCapture_Include_Exclude_LocalNetwork()
    {
        var serverOptions = TestHelper.CreateFileAccessManagerOptions();
        serverOptions.NetFilterOptions.IncludeLocalNetwork = false;
        serverOptions.NetFilterOptions.PacketCaptureIncludeIpRanges = [IpRange.Parse("000.0.0.000 - 230.0.0.220")];
        serverOptions.NetFilterOptions.PacketCaptureExcludeIpRanges = [IpRange.Parse("230.0.0.100 - 230.0.0.250")];

        // Create Server
        await using var server = await TestHelper.CreateServer(serverOptions);
        var token = TestHelper.CreateAccessToken(server);

        // create client
        var clientOptions = TestHelper.CreateClientOptions();
        clientOptions.PacketCaptureIncludeIpRanges = IpNetwork.All.ToIpRanges();
        clientOptions.IncludeLocalNetwork = false;
        await using var client = await TestHelper.CreateClient(token: token, clientOptions: clientOptions, packetCapture: new TestNullPacketCapture());
        
        Assert.IsFalse(client.PacketCaptureIncludeIpRanges.IsInRange(IPAddress.Parse("192.168.0.100")),
            "LocalNetWorks failed");
        Assert.IsFalse(client.PacketCaptureIncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.110")),
            "Excludes failed");
        Assert.IsTrue(client.PacketCaptureIncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.50")), "Includes failed");
        Assert.IsFalse(client.PacketCaptureIncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.240")),
            "Includes failed");
        Assert.IsFalse(client.PacketCaptureIncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.254")),
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
        serverOptions.NetFilterOptions.IncludeIpRanges = [IpRange.Parse("000.0.0.000 - 230.0.0.220")];
        serverOptions.NetFilterOptions.ExcludeIpRanges = [IpRange.Parse("230.0.0.100 - 230.0.0.250")];

        // Create Server
        await using var server = await TestHelper.CreateServer(serverOptions);
        var token = TestHelper.CreateAccessToken(server);

        // create client
        var clientOptions = TestHelper.CreateClientOptions();
        clientOptions.PacketCaptureIncludeIpRanges = IpNetwork.All.ToIpRanges();
        await using var client = await TestHelper.CreateClient(token: token, clientOptions: clientOptions, packetCapture: new TestNullPacketCapture());

        Assert.IsFalse(client.IncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.110")), "Excludes failed");
        Assert.IsTrue(client.IncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.50")), "Includes failed");
        Assert.IsFalse(client.IncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.240")), "Includes failed");
        Assert.IsFalse(client.IncludeIpRanges.IsInRange(IPAddress.Parse("230.0.0.254")), "Includes & Excludes failed");

        Assert.IsTrue(server.SessionManager.NetFilter.BlockedIpRanges.IsInRange(IPAddress.Parse("230.0.0.110")));
        Assert.IsFalse(server.SessionManager.NetFilter.BlockedIpRanges.IsInRange(IPAddress.Parse("230.0.0.50")));
        Assert.IsTrue(server.SessionManager.NetFilter.BlockedIpRanges.IsInRange(IPAddress.Parse("230.0.0.254")));
    }
}