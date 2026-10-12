using System.Net;
using System.Net.Quic;
using VpnHood.Core.Client;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Proxies.Management.Abstractions.Options;
using VpnHood.Core.Server;
using VpnHood.Core.Server.Access.Managers.FileAccessManagers;
using VpnHood.Net.Toolkit.Sockets;
using VpnHood.Test.AccessManagers;
using VpnHood.Test.Device;
using VpnHood.Test.Extensions;

namespace VpnHood.Test.Dom;

internal class ClientServerDom : IAsyncDisposable
{
    public VpnHoodServer Server { get; }
    public VpnHoodClient Client { get; }
    public TestVpnAdapter ClientVpnAdapter { get; }
    public TestAccessManager AccessManager { get; }
    public FileAccessManagerOptions AccessManagerOptions { get; }
    public Token Token { get; }
    public Session ServerSession { get; }
    public long OldClientSentByteCount { get; private set; }
    public long OldClientReceivedByteCount { get; private set; }
    public long OldServerSentByteCount { get; private set; }
    public long OldServerReceivedByteCount { get; private set; }

    private ClientServerDom(
        Token token,
        VpnHoodClient client,
        TestVpnAdapter clientVpnAdapter,
        VpnHoodServer server,
        TestAccessManager accessManager,
        FileAccessManagerOptions fileAccessManagerOptions)
    {
        Token = token;
        Server = server;
        Client = client;
        ClientVpnAdapter = clientVpnAdapter;
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
        Assert.AreEqual(client.Config.UserAgent, session?.ClientInfo.UserAgent);

        // check ClientPublicAddress in server
        Assert.AreEqual(fileAccessManagerOptions.TcpEndPointsValue.First().Address,
            client.RequiredSession.Info.ClientPublicIpAddress);

        Collect();
    }

    public static async Task<ClientServerDom> Create(
        TestHelper testHelper,
        ClientOptions? clientOptions = null,
        Traffic? maxSpeedMbps = null,
        ProxyOptions? proxyOptions = null,
        ISocketFactory? clientSocketFactory = null)
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
            var serverEp = TestWebServerLocalEps.AllocateFreeTcpEndPoint(IPAddress.IPv6Loopback);
            var fileAccessManagerOptions = testHelper.CreateFileAccessManagerOptions();
            fileAccessManagerOptions.TcpEndPoints = [serverEp];
            fileAccessManagerOptions.PublicEndPoints = [serverEp];
            fileAccessManagerOptions.UdpEndPoints = [TestWebServerLocalEps.AllocateFreeUdpEndPoint(IPAddress.IPv6Loopback)];
            if (QuicListener.IsSupported && QuicConnection.IsSupported)
                fileAccessManagerOptions.QuicEndPoints = [
                    TestWebServerLocalEps.AllocateFreeUdpEndPoint(IPAddress.Any),
                    TestWebServerLocalEps.AllocateFreeUdpEndPoint(IPAddress.IPv6Any)
                ];

            accessManager = testHelper.CreateAccessManager(fileAccessManagerOptions);
            server = await testHelper.CreateServer(accessManager, socketFactory: testSocketFactory);
            var token = testHelper.CreateAccessToken(server, maxSpeedMbps: maxSpeedMbps);

            // Create Client
            clientOptions.AccessKey = token.ToAccessKey();

            client = await testHelper.CreateClient(clientOptions: clientOptions, vpnAdapter: clientVpnAdapter,
                proxyOptions: proxyOptions, socketFactory: clientSocketFactory);
            var clientServerDom = new ClientServerDom(token, client, clientVpnAdapter, server, accessManager,
                fileAccessManagerOptions);
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

        OldClientSentByteCount = Client.RequiredSession.Status.SessionTraffic.Sent;
        OldClientReceivedByteCount = Client.RequiredSession.Status.SessionTraffic.Received;
        OldServerSentByteCount = ServerSession.TrafficMeter.Traffic.Sent;
        OldServerReceivedByteCount = ServerSession.TrafficMeter.Traffic.Received;
    }

    public async Task AssertClientSent(int minData = 100)
    {
        await WaitForCount(() => Client.RequiredSession.Status.SessionTraffic.Sent, OldClientSentByteCount + minData);
        Assert.AreNotEqual(OldClientSentByteCount, Client.RequiredSession.Status.SessionTraffic.Sent,
            delta: minData,
            "Not enough data has been sent through the client.");
    }

    public async Task AssertClientReceived(int minData = 500)
    {
        await WaitForCount(() => Client.RequiredSession.Status.SessionTraffic.Received,
            OldClientReceivedByteCount + minData);
        Assert.AreNotEqual(OldClientReceivedByteCount, Client.RequiredSession.Status.SessionTraffic.Received,
            delta: minData,
            "Not enough data has been received through the client.");
    }

    public async Task AssertServerSent(int minData = 500)
    {
        await WaitForCount(() => ServerSession.TrafficMeter.Traffic.Sent, OldServerSentByteCount + minData);
        Assert.AreNotEqual(OldServerSentByteCount, ServerSession.TrafficMeter.Traffic.Sent,
            delta: minData,
            "Not enough data has been sent through the server.");
    }

    public async Task AssertServerReceived(int minData = 100)
    {
        await WaitForCount(() => ServerSession.TrafficMeter.Traffic.Received, OldServerReceivedByteCount + minData);
        Assert.AreNotEqual(OldServerReceivedByteCount, ServerSession.TrafficMeter.Traffic.Received,
            delta: minData,
            "Not enough data has been received through the server.");
    }

    public async Task AssertClientTransfer(int minTunnelSendData = 100, int minTunnelReceivedData = 500)
    {
        await AssertClientSent(minTunnelSendData);
        await AssertClientReceived(minTunnelReceivedData);
    }

    public async Task AssertServerTransfer(int minTunnelSendData = 500, int minTunnelReceivedData = 100)
    {
        await AssertServerSent(minTunnelSendData);
        await AssertServerReceived(minTunnelReceivedData);
    }

    public async Task AssertTransfer(int minTunnelSendData = 100, int minTunnelReceivedData = 500)
    {
        await AssertClientTransfer(minTunnelSendData: minTunnelSendData, minTunnelReceivedData: minTunnelReceivedData);
        await AssertServerTransfer(minTunnelSendData: minTunnelReceivedData, minTunnelReceivedData: minTunnelSendData);
    }

    // A proxied stream counts its bytes once its write returns, which can be after the other end has them and
    // the test's request is done: a count gets a moment to arrive before it is asserted
    private static async Task WaitForCount(Func<long> count, long exceeded)
    {
        for (var i = 0; i < 50 && count() <= exceeded; i++)
            await Task.Delay(100);
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await Server.DisposeAsync();
    }
}