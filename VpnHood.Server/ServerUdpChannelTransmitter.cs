using System;
using System.Net;
using System.Net.Sockets;
using VpnHood.Tunneling.Channels;

namespace VpnHood.Server;

public class ServerUdpChannelTransmitter : UdpChannelTransmitter
{
    private readonly SessionManager _sessionManager;

    public ServerUdpChannelTransmitter(UdpClient udpClient, SessionManager sessionManager)
        : base(udpClient, sessionManager.ServerSecret)
    {
        _sessionManager = sessionManager;
    }

    protected override void OnReceiveData(ulong sessionId, IPEndPoint remoteEndPoint,
        long channelCryptorPosition, byte[] buffer, int bufferIndex)
    {
        var session = _sessionManager.GetSessionById(sessionId) 
            ?? throw new Exception($"Session does not found. SessionId: {sessionId}");

        //make sure UDP channel is added
        session.UseUdpChannel = true;
        session.UdpChannel?.SetRemote(this, remoteEndPoint);
        session.UdpChannel?.OnReceiveData(channelCryptorPosition, buffer, bufferIndex);
    }
}