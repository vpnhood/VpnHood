using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Client;
using VpnHood.Common.Net;

namespace VpnHood.Test.Tests;

[TestClass]
public class ServerNetFilterConfigTest
{
    [TestMethod]
    public async Task PacketCapture_Include()
    {
        var serverOptions = TestHelper.CreateFileAccessServerOptions();
        serverOptions.NetFilterOptions.PacketCaptureIncludeIpRanges = new[] { IpRange.Parse("230.0.0.100-230.0.0.250") };

        // Create Server
        await using var server = TestHelper.CreateServer(serverOptions);
        var token = TestHelper.CreateAccessToken(server);

        // create client
        await using var client =
            new VpnHoodClient(TestHelper.CreatePacketCapture(), Guid.NewGuid(), token, new ClientOptions
            {
                PacketCaptureIncludeIpRanges = new[]
                {
                    IpRange.Parse("230.0.0.0-230.0.0.200")
                }
            });

        await client.Connect();

        Assert.IsFalse(client.PacketCaptureIncludeIpRanges.IsInSortedRanges(IPAddress.Parse("230.0.0.0")));
        Assert.IsFalse(client.PacketCaptureIncludeIpRanges.IsInSortedRanges(IPAddress.Parse("230.0.0.10")));
        Assert.IsTrue(client.PacketCaptureIncludeIpRanges.IsInSortedRanges(IPAddress.Parse("230.0.0.100")));
        Assert.IsTrue(client.PacketCaptureIncludeIpRanges.IsInSortedRanges(IPAddress.Parse("230.0.0.150")));
        Assert.IsTrue(client.PacketCaptureIncludeIpRanges.IsInSortedRanges(IPAddress.Parse("230.0.0.200")));
        Assert.IsFalse(client.PacketCaptureIncludeIpRanges.IsInSortedRanges(IPAddress.Parse("230.0.0.220")));

        Assert.IsTrue(server.SessionManager.NetFilter.BlockedIpRanges.IsInSortedRanges(IPAddress.Parse("230.0.0.50")));
    }

    [TestMethod]
    public async Task PacketCapture_Exclude()
    {
        var serverOptions = TestHelper.CreateFileAccessServerOptions();
        serverOptions.NetFilterOptions.PacketCaptureExcludeIpRanges = new[] { IpRange.Parse("230.0.0.100-230.0.0.250") };

        // Create Server
        await using var server = TestHelper.CreateServer(serverOptions);
        var token = TestHelper.CreateAccessToken(server);

        // create client
        await using var client =
            new VpnHoodClient(TestHelper.CreatePacketCapture(), Guid.NewGuid(), token, new ClientOptions
            {
                PacketCaptureIncludeIpRanges = new[]
                {
                    IpRange.Parse("230.0.0.0-230.0.0.200")
                }
            });

        await client.Connect();

        Assert.IsTrue(client.PacketCaptureIncludeIpRanges.IsInSortedRanges(IPAddress.Parse("230.0.0.0")));
        Assert.IsTrue(client.PacketCaptureIncludeIpRanges.IsInSortedRanges(IPAddress.Parse("230.0.0.10")));
        Assert.IsFalse(client.PacketCaptureIncludeIpRanges.IsInSortedRanges(IPAddress.Parse("230.0.0.100")));
        Assert.IsFalse(client.PacketCaptureIncludeIpRanges.IsInSortedRanges(IPAddress.Parse("230.0.0.150")));
        Assert.IsFalse(client.PacketCaptureIncludeIpRanges.IsInSortedRanges(IPAddress.Parse("230.0.0.200")));

        Assert.IsFalse(client.PacketCaptureIncludeIpRanges.IsInSortedRanges(IPAddress.Parse("230.0.0.220"))); //block by client
        Assert.IsFalse(server.SessionManager.NetFilter.BlockedIpRanges.IsInSortedRanges(IPAddress.Parse("230.0.0.50")));
        Assert.IsTrue(server.SessionManager.NetFilter.BlockedIpRanges.IsInSortedRanges(IPAddress.Parse("230.0.0.220")));
    }

    [TestMethod]
    public async Task PacketCapture_Include_Exclude_LocalNetwork()
    {
        var serverOptions = TestHelper.CreateFileAccessServerOptions();
        serverOptions.NetFilterOptions.ExcludeLocalNetwork = true;
        serverOptions.NetFilterOptions.PacketCaptureIncludeIpRanges = new[] { IpRange.Parse("000.0.0.000 - 230.0.0.220") };
        serverOptions.NetFilterOptions.PacketCaptureExcludeIpRanges = new[] { IpRange.Parse("230.0.0.100 - 230.0.0.250") };

        // Create Server
        await using var server = TestHelper.CreateServer(serverOptions);
        var token = TestHelper.CreateAccessToken(server);

        // create client
        await using var client = new VpnHoodClient(TestHelper.CreatePacketCapture(), Guid.NewGuid(), token, new ClientOptions());
        await client.Connect();

        Assert.IsFalse(client.PacketCaptureIncludeIpRanges.IsInSortedRanges(IPAddress.Parse("192.168.0.100")), "LocalNetWorks failed");
        Assert.IsFalse(client.PacketCaptureIncludeIpRanges.IsInSortedRanges(IPAddress.Parse("230.0.0.110")), "Excludes failed");
        Assert.IsTrue(client.PacketCaptureIncludeIpRanges.IsInSortedRanges(IPAddress.Parse("230.0.0.50")), "Includes failed");
        Assert.IsFalse(client.PacketCaptureIncludeIpRanges.IsInSortedRanges(IPAddress.Parse("230.0.0.240")), "Includes failed");
        Assert.IsFalse(client.PacketCaptureIncludeIpRanges.IsInSortedRanges(IPAddress.Parse("230.0.0.254")), "Includes failed");

        Assert.IsTrue(server.SessionManager.NetFilter.BlockedIpRanges.IsInSortedRanges(IPAddress.Parse("192.168.0.100")));
        Assert.IsTrue(server.SessionManager.NetFilter.BlockedIpRanges.IsInSortedRanges(IPAddress.Parse("230.0.0.110")));
        Assert.IsFalse(server.SessionManager.NetFilter.BlockedIpRanges.IsInSortedRanges(IPAddress.Parse("230.0.0.50")));
        Assert.IsTrue(server.SessionManager.NetFilter.BlockedIpRanges.IsInSortedRanges(IPAddress.Parse("230.0.0.254")));
    }

    [TestMethod]
    public async Task IpRange_Include_Exclude()
    {
        var serverOptions = TestHelper.CreateFileAccessServerOptions();
        serverOptions.NetFilterOptions.IncludeIpRanges = new[] { IpRange.Parse("000.0.0.000 - 230.0.0.220") };
        serverOptions.NetFilterOptions.ExcludeIpRanges = new[] { IpRange.Parse("230.0.0.100 - 230.0.0.250") };

        // Create Server
        await using var server = TestHelper.CreateServer(serverOptions);
        var token = TestHelper.CreateAccessToken(server);

        // create client
        await using var client = new VpnHoodClient(TestHelper.CreatePacketCapture(), Guid.NewGuid(), token, new ClientOptions());
        await client.Connect();

        Assert.IsFalse(client.IncludeIpRanges.IsInSortedRanges(IPAddress.Parse("230.0.0.110")), "Excludes failed");
        Assert.IsTrue(client.IncludeIpRanges.IsInSortedRanges(IPAddress.Parse("230.0.0.50")), "Includes failed");
        Assert.IsFalse(client.IncludeIpRanges.IsInSortedRanges(IPAddress.Parse("230.0.0.240")), "Includes failed");
        Assert.IsFalse(client.IncludeIpRanges.IsInSortedRanges(IPAddress.Parse("230.0.0.254")), "Includes & Excludes failed");

        Assert.IsTrue(server.SessionManager.NetFilter.BlockedIpRanges.IsInSortedRanges(IPAddress.Parse("230.0.0.110")));
        Assert.IsFalse(server.SessionManager.NetFilter.BlockedIpRanges.IsInSortedRanges(IPAddress.Parse("230.0.0.50")));
        Assert.IsTrue(server.SessionManager.NetFilter.BlockedIpRanges.IsInSortedRanges(IPAddress.Parse("230.0.0.254")));
    }
}