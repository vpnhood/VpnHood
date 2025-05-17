using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Tunneling.Channels;

namespace VpnHood.Core.Server;

public class ServerUdpChannelTransmitter(UdpClient udpClient, SessionManager sessionManager)
    : UdpChannelTransmitter(udpClient, sessionManager.ServerSecret)
{
    protected override void OnReceiveData(ulong sessionId, IPEndPoint remoteEndPoint,
        long channelCryptorPosition, Span<byte> buffer)
    {
        var session = sessionManager.GetSessionById(sessionId)
                      ?? throw new Exception($"Session does not found. SessionId: {sessionId}");

        //make sure UDP channel is added
        session.UseUdpChannel = true;
        session.UdpChannel?.SetRemote(this, remoteEndPoint);
        session.UdpChannel?.OnReceiveData(buffer, channelCryptorPosition);
    }
}