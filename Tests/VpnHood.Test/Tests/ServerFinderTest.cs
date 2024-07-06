using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Client;
using VpnHood.Server;
using VpnHood.Test.AccessManagers;
using VpnHood.Test.Device;

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

            accessManagers[2].RedirectHostEndPoints =
                [
                    servers[4].ServerHost.TcpEndPoints.First(), 
                    servers[5].ServerHost.TcpEndPoints.First(),
                    servers[6].ServerHost.TcpEndPoints.First(),
                    servers[7].ServerHost.TcpEndPoints.First()
                ];

            // create token by server[2]
            var token = TestHelper.CreateAccessToken(servers[0]);
            token.ServerToken.HostEndPoints = [servers[2].ServerHost.TcpEndPoints.First()];

            // stop some servers
            await servers[0].DisposeAsync();
            await servers[1].DisposeAsync();
            await servers[3].DisposeAsync();
            await servers[4].DisposeAsync();
            await servers[6].DisposeAsync();

            // connect
            var client = await TestHelper.CreateClient(token, packetCapture: new TestNullPacketCapture());
            await TestHelper.WaitForClientState(client, ClientState.Connected);

            Assert.AreEqual(servers[5].ServerHost.TcpEndPoints.First(), client.HostTcpEndPoint);
        }
        finally
        {
            await Task.WhenAll(servers.Select(x => x.DisposeAsync().AsTask()));
            foreach (var accessManager in accessManagers)
                accessManager.Dispose();
        }
    }
}