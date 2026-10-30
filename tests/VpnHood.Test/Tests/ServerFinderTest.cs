using VpnHood.Core.Client;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Server;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Test.AccessManagers;
using VpnHood.Test.Device;
using VpnHood.Test.Extensions;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class ServerFinderTest : TestBase
{
    [TestMethod]
    public async Task Find_reachable()
    {
        var storageFolder = TestHelper.CreateAccessManagerWorkingDir();
        var servers = new List<VpnHoodServer>();
        var accessManagers = new List<TestAccessManager>();

        try {
            // create servers
            for (var i = 0; i < 5; i++) {
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
            var client = await TestHelper.CreateClient(token, vpnAdapter: new TestNullVpnAdapter());
            await client.WaitForState(ClientState.Connected);

            Assert.IsTrue(
                servers[2].ServerHost.TcpEndPoints.First().Equals(client.RequiredSession.Config.HostTcpEndPoint) ||
                servers[3].ServerHost.TcpEndPoints.First().Equals(client.RequiredSession.Config.HostTcpEndPoint) ||
                servers[4].ServerHost.TcpEndPoints.First().Equals(client.RequiredSession.Config.HostTcpEndPoint)
            );
        }
        finally {
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

        try {
            // create servers
            for (var i = 0; i < 10; i++) {
                var accessManager = TestHelper.CreateAccessManager(storagePath: storageFolder);
                var server = await TestHelper.CreateServer(accessManager);
                servers.Add(server);
                accessManagers.Add(accessManager);
            }

            var serverTokens = accessManagers.Select(x => x.CreateToken().ServerToken).ToArray();
            accessManagers[2].RedirectServerTokens = [
                serverTokens[4],
                serverTokens[5],
                serverTokens[6],
                serverTokens[7]
            ];

            // create token by server[2]
            var token = TestHelper.CreateAccessToken(servers[0]);
            var serverEndPoints = servers.SelectMany(x => x.ServerHost.TcpEndPoints).ToArray();
            token.ServerToken.HostEndPoints = [
                serverEndPoints[1],
                serverEndPoints[2]
            ];

            // stop some servers
            await servers[0].DisposeAsync();
            await servers[1].DisposeAsync();
            await servers[3].DisposeAsync();
            await servers[4].DisposeAsync();
            await servers[6].DisposeAsync();

            // connect
            var clientOptions = TestHelper.CreateClientOptions(token);
            var client =
                await TestHelper.CreateClient(clientOptions: clientOptions, vpnAdapter: new TestNullVpnAdapter());
            await client.WaitForState(ClientState.Connected);

            Assert.AreEqual(servers[5].ServerHost.TcpEndPoints.First(), client.RequiredSession.Config.HostTcpEndPoint);

            // tracker should report unreachable servers
            var testTracker = (TestTracker?)client.Tracker;
            Assert.IsNotNull(testTracker);
            Assert.IsTrue(
                testTracker.FindEvent("vh_endpoint_status", "ep", serverEndPoints[0])?.Parameters["available"] is null
                    or false);
            Assert.IsTrue(
                testTracker.FindEvent("vh_endpoint_status", "ep", serverEndPoints[1])?.Parameters["available"] is null
                    or false);
            Assert.IsTrue((bool?)testTracker.FindEvent("vh_endpoint_status", "ep", serverEndPoints[2])
                ?.Parameters["available"]);
            Assert.IsNull(testTracker.FindEvent("vh_endpoint_status", "ep", serverEndPoints[3]));
            Assert.IsFalse((bool?)testTracker.FindEvent("vh_endpoint_status", "ep", serverEndPoints[4])
                ?.Parameters["available"]);
            Assert.IsTrue((bool?)testTracker.FindEvent("vh_endpoint_status", "ep", serverEndPoints[5])
                ?.Parameters["available"]);
            Assert.IsTrue(
                testTracker.FindEvent("vh_endpoint_status", "ep", serverEndPoints[6])?.Parameters["available"] is null
                    or false);
            Assert.IsTrue(
                testTracker.FindEvent("vh_endpoint_status", "ep", serverEndPoints[7])?.Parameters["available"] is null
                    or true);
            Assert.IsNull(testTracker.FindEvent("vh_endpoint_status", "ep", serverEndPoints[8]));
            Assert.IsNull(testTracker.FindEvent("vh_endpoint_status", "ep", serverEndPoints[9]));
        }
        finally {
            await Task.WhenAll(servers.Select(x => x.DisposeAsync().AsTask()));
            foreach (var accessManager in accessManagers)
                accessManager.Dispose();
        }
    }

    [TestMethod]
    public async Task Redirect_without_secrets_uses_the_access_keys_farm_for_another_host()
    {
        var storageFolder = TestHelper.CreateAccessManagerWorkingDir();
        using var entryAccessManager = TestHelper.CreateAccessManager(storagePath: storageFolder);
        await using var entryServer = await TestHelper.CreateServer(entryAccessManager);
        using var redirectedAccessManager = TestHelper.CreateAccessManager(storagePath: storageFolder);
        await using var redirectedServer = await TestHelper.CreateServer(redirectedAccessManager);
        var token = TestHelper.CreateAccessToken(entryServer);
        var redirectedToken = JsonUtils.JsonClone(TestHelper.CreateAccessToken(redirectedServer).ServerToken);
        redirectedToken.Secret = null;
        redirectedToken.HostName = "localhost";
        redirectedToken.IsValidHostName = true;
        redirectedToken.HostEndPoints = null;
        redirectedToken.HostPort = redirectedServer.ServerHost.TcpEndPoints.First().Port;
        Assert.AreNotEqual(token.ServerToken.HostName, redirectedToken.HostName);

        // A domain-only redirect has no farm token, and one failed certificate's name must not discard the others.
        var unavailableToken = JsonUtils.JsonClone(redirectedToken);
        unavailableToken.HostName = "unavailable-redirect.invalid";
        entryAccessManager.RedirectServerTokens = [unavailableToken, redirectedToken];

        await using var client = await TestHelper.CreateClient(token, vpnAdapter: new TestNullVpnAdapter());
        await client.WaitForState(ClientState.Connected);
        Assert.AreEqual(redirectedServer.ServerHost.TcpEndPoints.First(), client.RequiredSession.Config.HostTcpEndPoint);
    }

    [TestMethod]
    public async Task Connected_server_is_kept_until_the_client_closes()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);
        var workingFolder = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
        var connectedServersFolder = Path.Combine(workingFolder, "connected-servers");

        await using var client = await TestHelper.CreateClient(TestHelper.CreateClientOptions(token),
            vpnAdapter: new TestNullVpnAdapter(), workingFolder: workingFolder);
        await client.WaitForState(ClientState.Connected);
        var connectedServer = new ConnectedServerFile(connectedServersFolder, token.TokenId).Read();
        Assert.AreEqual(client.RequiredSession.Config.HostTcpEndPoint, connectedServer?.TcpEndPoint);

        // a close, not a crash: nothing is left for the next connect
        await client.DisposeAsync();
        Assert.IsFalse(Directory.Exists(connectedServersFolder));
    }

    [TestMethod]
    [DataRow("kept-server.invalid")]
    [DataRow("redirected-server.invalid")]
    public async Task Name_that_resolves_to_nothing_connects_through_the_kept_server(string savedHostName)
    {
        // a name that never resolves (.invalid), as none does through the dead tunnel after a VPN host's crash
        await using var server = await TestHelper.CreateServer();
        var serverEndPoint = server.ServerHost.TcpEndPoints.First();
        var token = TestHelper.CreateAccessToken(server);
        token.ServerToken.HostName = "kept-server.invalid";
        token.ServerToken.IsValidHostName = true;
        token.ServerToken.HostPort = serverEndPoint.Port;
        token.ServerToken.HostEndPoints = null;
        var workingFolder = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());

        // nothing kept: the name alone reaches no server
        await using var client = await TestHelper.CreateClient(TestHelper.CreateClientOptions(token),
            vpnAdapter: new TestNullVpnAdapter(), autoConnect: false, workingFolder: workingFolder);
        await Assert.ThrowsExactlyAsync<EndPointDiscoveryException>(() => client.Connect(TestCt));

        // what a crash leaves: the server its session was on
        new ConnectedServerFile(Path.Combine(workingFolder, "connected-servers"), token.TokenId).Write(
            new VpnEndPoint(serverEndPoint, savedHostName, token.ServerToken.CertificateHash,
                token.ServerToken.PathBase));

        await using var client2 = await TestHelper.CreateClient(TestHelper.CreateClientOptions(token),
            vpnAdapter: new TestNullVpnAdapter(), workingFolder: workingFolder);
        await client2.WaitForState(ClientState.Connected);
        Assert.AreEqual(serverEndPoint, client2.RequiredSession.Config.HostTcpEndPoint);
    }

    [TestMethod]
    public async Task Unauthorized_kept_server_is_removed_before_the_next_connect()
    {
        await using var server = await TestHelper.CreateServer();
        await using var savedServer = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);
        var savedToken = TestHelper.CreateAccessToken(savedServer).ServerToken;
        var workingFolder = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
        var connectedServerFile = new ConnectedServerFile(Path.Combine(workingFolder, "connected-servers"), token.TokenId);

        // The probe proves reachability; the hello fails because the saved endpoint belongs to another farm.
        connectedServerFile.Write(
            new VpnEndPoint(savedServer.ServerHost.TcpEndPoints.First(), token.ServerToken.HostName,
                savedToken.CertificateHash, token.ServerToken.PathBase));

        await using var client = await TestHelper.CreateClient(TestHelper.CreateClientOptions(token),
            vpnAdapter: new TestNullVpnAdapter(), autoConnect: false, workingFolder: workingFolder);
        await Assert.ThrowsExactlyAsync<RequestAuthorizationException>(() => client.Connect(TestCt));
        Assert.AreEqual(ClientState.Disposed, client.State);
        Assert.IsNull(connectedServerFile.Read());

        await using var nextClient = await TestHelper.CreateClient(TestHelper.CreateClientOptions(token),
            vpnAdapter: new TestNullVpnAdapter(), workingFolder: workingFolder);
        await nextClient.WaitForState(ClientState.Connected);
        Assert.AreEqual(server.ServerHost.TcpEndPoints.First(), nextClient.RequiredSession.Config.HostTcpEndPoint);
    }
}
