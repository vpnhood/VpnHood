using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Client;
using VpnHood.Server;
using VpnHood.Test.AccessManagers;
using VpnHood.Test.Device;
using VpnHood.Test.Services;

namespace VpnHood.Test.Tests;

[TestClass]
public class ServerFinderTest
{
    [TestMethod]
    public async Task Find_reachable()
    {
        var storageFolder = TestHelper.CreateAccessManagerWorkingDir();
        var servers = new List<VpnHoodServer>();
        var accessManagers = new List<TestAccessManager>();

        try
        {
            // create servers
            for (var i = 0; i < 5; i++)
            {
                var accessManager = TestHelper.CreateAccessManager(storagePath: storageFolder);
                var server = await TestHelper.CreateServer(accessManager);
                servers.Add(server);
                accessManagers.Add(accessManager);
            }

            // create token
            var token = TestHelper.CreateAccessToken(servers[0]);
            token.ServerToken.HostEndPoints = servers.SelectMany(x => x.ServerHost.TcpEndPoints).ToArray();

            // stop server1 and server2
            await servers[0].DisposeAsync();
            await servers[1].DisposeAsync();

            // connect
            var client = await TestHelper.CreateClient(token, packetCapture: new TestNullPacketCapture());
            await TestHelper.WaitForClientState(client, ClientState.Connected);

            Assert.IsTrue(
                servers[2].ServerHost.TcpEndPoints.First().Equals(client.HostTcpEndPoint) ||
                servers[3].ServerHost.TcpEndPoints.First().Equals(client.HostTcpEndPoint) ||
                servers[4].ServerHost.TcpEndPoints.First().Equals(client.HostTcpEndPoint)
            );
        }
        finally
        {
            await Task.WhenAll(servers.Select(x => x.DisposeAsync().AsTask()));
            foreach (var accessManager in accessManagers)
                accessManager.Dispose();
        }
    }

    [TestMethod]
    public async Task Find_reachable_for_redirect_in_order()
    {
        var storageFolder = TestHelper.CreateAccessManagerWorkingDir();
        var servers = new List<VpnHoodServer>();
        var accessManagers = new List<TestAccessManager>();

        try
        {
            // create servers
            for (var i = 0; i < 10; i++)
            {
                var accessManager = TestHelper.CreateAccessManager(storagePath: storageFolder);
                var server = await TestHelper.CreateServer(accessManager);
                servers.Add(server);
                accessManagers.Add(accessManager);
            }
            var serverEndPoints = servers.Select(x => x.ServerHost.TcpEndPoints.First()).ToArray();

            accessManagers[2].RedirectHostEndPoints =
            [
                serverEndPoints[4],
                serverEndPoints[5],
                serverEndPoints[6],
                serverEndPoints[7]
            ];

            // create token by server[2]
            var token = TestHelper.CreateAccessToken(servers[0]);
            token.ServerToken.HostEndPoints = [serverEndPoints[1], serverEndPoints[2]];

            // stop some servers
            await servers[0].DisposeAsync();
            await servers[1].DisposeAsync();
            await servers[3].DisposeAsync();
            await servers[4].DisposeAsync();
            await servers[6].DisposeAsync();

            // connect
            var clientOptions = TestHelper.CreateClientOptions();
            var client = await TestHelper.CreateClient(token, packetCapture: new TestNullPacketCapture(), clientOptions: clientOptions);
            await TestHelper.WaitForClientState(client, ClientState.Connected);

            Assert.AreEqual(servers[5].ServerHost.TcpEndPoints.First(), client.HostTcpEndPoint);

            // tracker should report unreachable servers
            var testTracker = (TestTracker?)clientOptions.Tracker;
            Assert.IsNotNull(testTracker);
            Assert.IsTrue(testTracker.FindEvent("vh_endpoint_status", "ep", serverEndPoints[0])?.Parameters["available"] is null or false);
            Assert.IsTrue(testTracker.FindEvent("vh_endpoint_status", "ep", serverEndPoints[1])?.Parameters["available"] is null or false);
            Assert.IsTrue(testTracker.FindEvent("vh_endpoint_status", "ep", serverEndPoints[2])?.Parameters["available"] is true);
            Assert.IsTrue(testTracker.FindEvent("vh_endpoint_status", "ep", serverEndPoints[3]) is null);
            Assert.IsTrue(testTracker.FindEvent("vh_endpoint_status", "ep", serverEndPoints[4])?.Parameters["available"] is false);
            Assert.IsTrue(testTracker.FindEvent("vh_endpoint_status", "ep", serverEndPoints[5])?.Parameters["available"] is true);
            Assert.IsTrue(testTracker.FindEvent("vh_endpoint_status", "ep", serverEndPoints[6])?.Parameters["available"] is null or false);
            Assert.IsTrue(testTracker.FindEvent("vh_endpoint_status", "ep", serverEndPoints[7])?.Parameters["available"] is null or true);
            Assert.IsTrue(testTracker.FindEvent("vh_endpoint_status", "ep", serverEndPoints[8]) is null);
            Assert.IsTrue(testTracker.FindEvent("vh_endpoint_status", "ep", serverEndPoints[9]) is null);
        }
        finally
        {
            await Task.WhenAll(servers.Select(x => x.DisposeAsync().AsTask()));
            foreach (var accessManager in accessManagers)
                accessManager.Dispose();
        }
    }
}