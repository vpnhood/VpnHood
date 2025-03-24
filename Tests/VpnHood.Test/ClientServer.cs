using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Core.Client;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Server;

namespace VpnHood.Test;

internal class ClientServer
{
    public VpnHoodServer Server { get; }
    public VpnHoodClient Client { get; }
    public Session ServerSession { get; }
    public long OldClientSentByteCount { get; private set; }
    public long OldClientReceivedByteCount { get; private set; }
    public long OldServerSentByteCount { get; private set; }
    public long OldServerReceivedByteCount { get; private set; }

    public ClientServer(VpnHoodServer server, VpnHoodClient client)
    {
        Server = server;
        Client = client;

        // validate session
        server.SessionManager.Sessions.TryGetValue(client.SessionId, out var serverSession);
        Assert.IsNotNull(serverSession, "Could not find session in server!");
        ServerSession = serverSession;

        Collect();
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

}