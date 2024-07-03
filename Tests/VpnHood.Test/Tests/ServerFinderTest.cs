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

        try
        {
            // create servers
            for (var i = 0; i < 5; i++)
            {
                var accessManager = TestHelper.CreateAccessManager(storagePath: storageFolder);
                var server = await TestHelper.CreateServer(accessManager);
                servers.Add(server);
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

            Assert.AreEqual(
                servers[2].ServerHost.TcpEndPoints.FirstOrDefault(), 
                client.HostTcpEndPoint);
        }
        finally
        {
            foreach (var server in servers)
            {
                server.AccessManager.Dispose();
                await server.DisposeAsync();
            }
        }
    }
}