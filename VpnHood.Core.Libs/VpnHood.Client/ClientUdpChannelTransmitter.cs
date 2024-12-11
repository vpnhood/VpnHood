using System.Net;
using System.Net.Sockets;
using VpnHood.Tunneling.Channels;

namespace VpnHood.Client;

public class ClientUdpChannelTransmitter(UdpChannel udpChannel, UdpClient udpClient, byte[] serverKey)
    : UdpChannelTransmitter(udpClient, serverKey)
{
    protected override void OnReceiveData(ulong sessionId, IPEndPoint remoteEndPoint, long channelCryptorPosition,
        byte[] buffer, int bufferIndex)
    {
        udpChannel.OnReceiveData(channelCryptorPosition, buffer, bufferIndex);
    }
}