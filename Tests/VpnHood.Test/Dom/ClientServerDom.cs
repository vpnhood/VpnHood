using System.Net;
using VpnHood.Core.Client;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Server;
using VpnHood.Core.Server.Access.Managers.FileAccessManagement;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Test.AccessManagers;

namespace VpnHood.Test.Dom;

internal class ClientServerDom : IAsyncDisposable
{
    public VpnHoodServer Server { get; }
    public VpnHoodClient Client { get; }
    public TestAccessManager AccessManager { get; }
    public FileAccessManagerOptions AccessManagerOptions { get; }
    public Token Token { get; }
    public Session ServerSession { get; }
    public long OldClientSentByteCount { get; private set; }
    public long OldClientReceivedByteCount { get; private set; }
    public long OldServerSentByteCount { get; private set; }
    public long OldServerReceivedByteCount { get; private set; }

    private ClientServerDom(Token token,
        VpnHoodClient client,
        VpnHoodServer server,
        TestAccessManager accessManager, 
        FileAccessManagerOptions fileAccessManagerOptions)
    {
        Token = token;
        Server = server;
        Client = client;
        AccessManager = accessManager;
        AccessManagerOptions = fileAccessManagerOptions;

        // validate session
        server.SessionManager.Sessions.TryGetValue(client.SessionId, out var serverSession);
        Assert.IsNotNull(serverSession, "Could not find session in server!");
        ServerSession = serverSession;

        // check HostEndPoint in server
        accessManager.SessionService.Sessions.TryGetValue(client.SessionId, out var session);
        Assert.IsTrue(token.ServerToken.HostEndPoints?.Any(x => x.Equals(session?.HostEndPoint)));

        // check UserAgent in server
        Assert.AreEqual(client.Settings.UserAgent, session?.ClientInfo.UserAgent);

        // check ClientPublicAddress in server
        Assert.AreEqual(fileAccessManagerOptions.TcpEndPointsValue.First().Address, client.SessionInfo?.ClientPublicIpAddress);

        Collect();
    }

    public static async Task<ClientServerDom> Create(TestHelper testHelper, ClientOptions? clientOptions = null)
    {
        TestAccessManager? accessManager = null;
        VpnHoodServer? server = null;
        VpnHoodClient? client = null;
        clientOptions ??= testHelper.CreateClientOptions();
        if (!string.IsNullOrWhiteSpace(clientOptions.AccessKey))
            throw new ArgumentException("AccessCode must left empty.", nameof(clientOptions.AccessKey));

        try {
            var clientVpnAdapter = testHelper.CreateTestVpnAdapter();
            var testSocketFactory = testHelper.CreateTestSocketFactory(clientVpnAdapter);

            // Create Server
            var serverEp = VhUtils.GetFreeTcpEndPoint(IPAddress.IPv6Loopback);
            var fileAccessManagerOptions = testHelper.CreateFileAccessManagerOptions();
            fileAccessManagerOptions.TcpEndPoints = [serverEp];
            fileAccessManagerOptions.PublicEndPoints = [serverEp];
            fileAccessManagerOptions.UdpEndPoints = [VhUtils.GetFreeUdpEndPoint(IPAddress.IPv6Loopback)];

            accessManager = testHelper.CreateAccessManager(fileAccessManagerOptions);
            server = await testHelper.CreateServer(accessManager, socketFactory: testSocketFactory);
            var token = testHelper.CreateAccessToken(server);

            // Create Client
            clientOptions.AccessKey = token.ToAccessKey();

            client = await testHelper.CreateClient(clientOptions: clientOptions, vpnAdapter: clientVpnAdapter);
            var clientServerDom = new ClientServerDom(token, client, server, accessManager, fileAccessManagerOptions);
            return clientServerDom;

        }
        catch {
            if (client != null) await client.DisposeAsync();
            if (server != null) await server.DisposeAsync();
            accessManager?.Dispose();
            throw;
        }
    }


    public void Collect()
    {
        Assert.AreEqual(ServerState.Ready, Server.State);
        Assert.AreEqual(ClientState.Connected, Client.State);

        OldClientSentByteCount = Client.GetSessionStatus().SessionTraffic.Sent;
        OldClientReceivedByteCount = Client.GetSessionStatus().SessionTraffic.Received;
        OldServerSentByteCount = ServerSession.Tunnel.Traffic.Sent;
        OldServerReceivedByteCount = ServerSession.Tunnel.Traffic.Received;
    }

    public void AssertClientTransfer(int minTunnelSendData = 100, int minTunnelReceivedData = 500)
    {
        Assert.AreNotEqual(OldClientSentByteCount, Client.GetSessionStatus().SessionTraffic.Sent, 
            delta: minTunnelSendData,
            "Not enough data has been sent through the client.");
        Assert.AreNotEqual(OldClientReceivedByteCount, Client.GetSessionStatus().SessionTraffic.Received, 
            delta: minTunnelReceivedData, 
            "Not enough data has been received through the client.");
    }

    public void AssertServerTransfer(int minTunnelSendData = 500, int minTunnelReceivedData = 100)
    {
        Assert.AreNotEqual(OldServerSentByteCount, ServerSession.Tunnel.Traffic.Sent, 
            delta: minTunnelSendData,
            "Not enough data has been sent through the server.");
        Assert.AreNotEqual(OldServerReceivedByteCount, ServerSession.Tunnel.Traffic.Received, 
            delta: minTunnelReceivedData,
            "Not enough data has been received through the server.");
    }

    public void AssertTransfer(int minTunnelSendData = 100, int minTunnelReceivedData = 500)
    {
        AssertClientTransfer(minTunnelSendData: minTunnelSendData, minTunnelReceivedData: minTunnelReceivedData);
        AssertServerTransfer(minTunnelSendData: minTunnelReceivedData, minTunnelReceivedData: minTunnelSendData);
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await Server.DisposeAsync();
    }
}