using System.Net;
using System.Net.Sockets;
using VpnHood.Tunneling.Channels;

namespace VpnHood.Client;

public class ClientUdpChannelTransmitter : UdpChannelTransmitter
{
    private readonly UdpChannel _udpChannel;

    public ClientUdpChannelTransmitter(UdpChannel udpChannel, UdpClient udpClient, byte[] serverKey) : 
        base(udpClient, serverKey)
    {
        _udpChannel = udpChannel;
    }

    protected override void OnReceiveData(ulong sessionId, IPEndPoint remoteEndPoint, long channelCryptorPosition, byte[] buffer, int bufferIndex)
    {
        _udpChannel.OnReceiveData(channelCryptorPosition, buffer, bufferIndex);
    }
}