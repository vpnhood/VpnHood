using System;
using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Tunneling.Channels;

namespace VpnHood.Core.Server;

public class ServerUdpChannelTransmitter(UdpClient udpClient, SessionManager sessionManager)
    : UdpChannelTransmitter2(udpClient)
{
    protected override SessionUdpTransport? SessionIdToUdpTransport(ulong sessionId)
    {
        var session = sessionManager.GetSessionById(sessionId);
        if (session is null)
            return null;

        // Currently only one UDP transport per session is supported on the server side.
        // so if we change the transmitter of the old one. We do not create a new one because its creates a cryptographic overhead.
        var udpTransport = (SessionUdpTransport)session.UseUdpTransport(()=> new SessionUdpTransport(this, sessionId, session.SessionKey, null, true));
        udpTransport.Transmitter = this;
        return udpTransport;
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