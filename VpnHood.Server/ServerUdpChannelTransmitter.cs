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
        var session = _sessionManager.GetSessionById(sessionId);
        if (session == null)
            throw new Exception($"Session does not found. SessionId: {sessionId}");

        session.UseUdpChannel = true; //make sure UDP channel is created
        session.UdpChannel2!.SetRemote(this, remoteEndPoint);
        session.UdpChannel2!.OnReceiveData(channelCryptorPosition, buffer, bufferIndex);
    }
}