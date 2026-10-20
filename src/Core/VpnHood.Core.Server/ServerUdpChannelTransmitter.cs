using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Tunneling.Channels;

namespace VpnHood.Core.Server;

public class ServerUdpChannelTransmitter(UdpClient udpClient, SessionManager sessionManager)
    : UdpChannelTransmitter(udpClient)
{
    protected override SessionUdpTransport? SessionIdToUdpTransport(ulong sessionId)
    {
        return sessionManager.GetSessionById(sessionId)?.UdpState.GetTransport(this);
    }

    protected override void OpenSessionUdpChannel(ulong sessionId)
    {
        sessionManager.GetSessionById(sessionId)?.UdpState.OpenChannel();
    }

    public static ServerUdpChannelTransmitter Create(IPEndPoint ipEndPoint, SessionManager sessionManager)
    {
        var udpClient = new UdpClient(ipEndPoint);

        try {
            return new ServerUdpChannelTransmitter(udpClient, sessionManager);
        }
        catch {
            udpClient.Dispose();
            throw;
        }
    }
}
