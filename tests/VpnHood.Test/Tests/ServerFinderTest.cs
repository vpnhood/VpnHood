using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using VpnHood.Core.Client;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Common.Tokens;
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
                var accessManager = await TestHelper.CreateAccessManager(storagePath: storageFolder);
                var server = await TestHelper.CreateServer(accessManager);
                servers.Add(server);
                accessManagers.Add(accessManager);
            }

            // create token
            var token = await TestHelper.CreateAccessToken(servers[0]);
            token.ServerToken.HostEndPoints = servers.SelectMany(x => x.ServerHost.TcpEndPoints).ToArray();

            // stop server1 and server2
            await servers[0].DisposeAsync();
            await servers[1].DisposeAsync();

            // connect
            var client = await TestHelper.CreateClient(token, vpnAdapter: new TestNullVpnAdapter());
            await client.WaitForState(ClientState.Connected, TestCt);

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
                var accessManager = await TestHelper.CreateAccessManager(storagePath: storageFolder);
                var server = await TestHelper.CreateServer(accessManager);
                servers.Add(server);
                accessManagers.Add(accessManager);
            }

            var serverTokens = (await Task.WhenAll(accessManagers.Select(x => x.CreateToken(TestCt))))
                .Select(x => x.ServerToken).ToArray();
            accessManagers[2].RedirectServerTokens = [
                serverTokens[4],
                serverTokens[5],
                serverTokens[6],
                serverTokens[7]
            ];

            // create token by server[2]
            var token = await TestHelper.CreateAccessToken(servers[0]);
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
            await client.WaitForState(ClientState.Connected, TestCt);

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
        using var entryAccessManager = await TestHelper.CreateAccessManager(storagePath: storageFolder);
        await using var entryServer = await TestHelper.CreateServer(entryAccessManager);
        using var redirectedAccessManager = await TestHelper.CreateAccessManager(storagePath: storageFolder);
        await using var redirectedServer = await TestHelper.CreateServer(redirectedAccessManager);
        var token = await TestHelper.CreateAccessToken(entryServer);
        var redirectedToken = JsonUtils.JsonClone((await TestHelper.CreateAccessToken(redirectedServer)).ServerToken);
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
        await client.WaitForState(ClientState.Connected, TestCt);
        Assert.AreEqual(redirectedServer.ServerHost.TcpEndPoints.First(), client.RequiredSession.Config.HostTcpEndPoint);
    }

    [TestMethod]
    public async Task Connected_server_is_kept_until_the_client_closes()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var workingFolder = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
        var connectedServersFolder = Path.Combine(workingFolder, "connected-servers");

        await using var client = await TestHelper.CreateClient(TestHelper.CreateClientOptions(token),
            vpnAdapter: new TestNullVpnAdapter(), workingFolder: workingFolder);
        await client.WaitForState(ClientState.Connected, TestCt);
        var connectedServer = await new ConnectedServerFile(connectedServersFolder, token.TokenId).Read(TestCt);
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
        var token = await TestHelper.CreateAccessToken(server);
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
        await new ConnectedServerFile(Path.Combine(workingFolder, "connected-servers"), token.TokenId).Write(
            new VpnEndPoint(serverEndPoint, savedHostName, token.ServerToken.CertificateHash,
                token.ServerToken.PathBase), TestCt);

        await using var client2 = await TestHelper.CreateClient(TestHelper.CreateClientOptions(token),
            vpnAdapter: new TestNullVpnAdapter(), workingFolder: workingFolder);
        await client2.WaitForState(ClientState.Connected, TestCt);
        Assert.AreEqual(serverEndPoint, client2.RequiredSession.Config.HostTcpEndPoint);
    }

    [TestMethod]
    public async Task Unauthorized_kept_server_is_removed_before_the_next_connect()
    {
        await using var server = await TestHelper.CreateServer();
        await using var savedServer = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var savedToken = (await TestHelper.CreateAccessToken(savedServer)).ServerToken;
        var workingFolder = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
        var connectedServerFile = new ConnectedServerFile(Path.Combine(workingFolder, "connected-servers"), token.TokenId);

        // The probe proves reachability; the hello fails because the saved endpoint belongs to another farm.
        await connectedServerFile.Write(
            new VpnEndPoint(savedServer.ServerHost.TcpEndPoints.First(), token.ServerToken.HostName,
                savedToken.CertificateHash, token.ServerToken.PathBase), TestCt);

        await using var client = await TestHelper.CreateClient(TestHelper.CreateClientOptions(token),
            vpnAdapter: new TestNullVpnAdapter(), autoConnect: false, workingFolder: workingFolder);
        await Assert.ThrowsExactlyAsync<RequestAuthorizationException>(() => client.Connect(TestCt));
        Assert.AreEqual(ClientState.Disposed, client.State);
        Assert.IsNull(await connectedServerFile.Read(TestCt));

        await using var nextClient = await TestHelper.CreateClient(TestHelper.CreateClientOptions(token),
            vpnAdapter: new TestNullVpnAdapter(), workingFolder: workingFolder);
        await nextClient.WaitForState(ClientState.Connected, TestCt);
        Assert.AreEqual(server.ServerHost.TcpEndPoints.First(), nextClient.RequiredSession.Config.HostTcpEndPoint);
    }

    // only an address the name gave may move with the name; asks a real name, as EndPointResolverTests do
    [TestMethod]
    public async Task Only_the_names_addresses_are_by_host_name()
    {
        var tokenEndPoint = IPEndPoint.Parse("192.0.2.1:443");
        var finder = CreateServerFinder(EndPointStrategy.DnsFirst);
        var endpoints = await finder.ResolveVpnEndPoints([CreateNamedServerToken(tokenEndPoint)], false, TestCt);

        Assert.IsFalse(finder.IsByHostName(endpoints.Single(x => x.TcpEndPoint.Equals(tokenEndPoint))));
        var nameEndPoints = endpoints.Where(x => !x.TcpEndPoint.Equals(tokenEndPoint)).ToArray();
        Assert.IsNotEmpty(nameEndPoints);
        Assert.IsTrue(nameEndPoints.All(finder.IsByHostName));
    }

    // the name's address on a port of the token's own is the token's endpoint, which never moves with the name
    [TestMethod]
    public async Task The_names_address_on_another_port_is_not_by_host_name()
    {
        var nameAddresses = await Dns.GetHostAddressesAsync("vpnhood.com", AddressFamily.InterNetwork, TestCt);
        var tokenEndPoint = new IPEndPoint(nameAddresses.First(), 8443);
        var serverToken = CreateNamedServerToken(tokenEndPoint);
        serverToken.HostPort = 443;
        var finder = CreateServerFinder(EndPointStrategy.DnsFirst);
        var endpoints = await finder.ResolveVpnEndPoints([serverToken], false, TestCt);

        Assert.IsFalse(finder.IsByHostName(endpoints.Single(x => x.TcpEndPoint.Equals(tokenEndPoint))));
    }

    // after a server's move, the connect takes the addresses its check found, never asking the name: a name that never
    // resolves (.invalid) proves it
    [TestMethod]
    public async Task Known_addresses_of_the_name_are_taken_without_asking_it()
    {
        var serverToken = CreateNamedServerToken(IPEndPoint.Parse("192.0.2.1:443"));
        serverToken.HostName = "moved-server.invalid";
        serverToken.HostEndPoints = null;
        var knownAddress = IPAddress.Parse("192.0.2.7");
        var finder = CreateServerFinder(EndPointStrategy.DnsFirst, new HostNameAddresses {
            HostName = serverToken.HostName,
            Addresses = [knownAddress]
        });

        var endpoints = await finder.ResolveVpnEndPoints([serverToken], false, TestCt);

        Assert.AreEqual(new IPEndPoint(knownAddress, 443), endpoints.Single().TcpEndPoint);
        Assert.IsTrue(finder.IsByHostName(endpoints.Single()));
    }

    // another name's addresses are never taken for the token's: a redirect's, say
    [TestMethod]
    public async Task Known_addresses_of_another_name_are_not_taken()
    {
        var serverToken = CreateNamedServerToken(IPEndPoint.Parse("192.0.2.1:443"));
        serverToken.HostName = "moved-server.invalid";
        serverToken.HostEndPoints = null;
        var finder = CreateServerFinder(EndPointStrategy.DnsFirst, new HostNameAddresses {
            HostName = "another-server.invalid",
            Addresses = [IPAddress.Parse("192.0.2.7")]
        });

        await Assert.ThrowsExactlyAsync<EndPointDiscoveryException>(() =>
            finder.ResolveVpnEndPoints([serverToken], false, TestCt));
    }

    [TestMethod]
    public async Task No_address_is_by_host_name_when_the_name_is_not_asked()
    {
        var tokenEndPoint = IPEndPoint.Parse("192.0.2.1:443");
        var finder = CreateServerFinder(EndPointStrategy.IpOnly);
        var endpoints = await finder.ResolveVpnEndPoints([CreateNamedServerToken(tokenEndPoint)], false, TestCt);

        Assert.IsFalse(finder.IsByHostName(endpoints.Single()));
    }

    private static ServerFinder CreateServerFinder(EndPointStrategy endPointStrategy,
        HostNameAddresses? knownHostAddresses = null) =>
        new(new TestSocketFactory(), RandomNumberGenerator.GetBytes(16), null, TimeSpan.FromSeconds(10),
            endPointStrategy, [], null, null, false, useKillSwitch: false, knownHostAddresses: knownHostAddresses);

    private static ServerToken CreateNamedServerToken(IPEndPoint tokenEndPoint) => new() {
        CreatedTime = DateTime.UtcNow,
        HostName = "vpnhood.com",
        HostPort = tokenEndPoint.Port,
        IsValidHostName = true,
        Secret = RandomNumberGenerator.GetBytes(16),
        HostEndPoints = [tokenEndPoint]
    };
}
