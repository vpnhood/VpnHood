using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Tunneling.Channels;

namespace VpnHood.Core.Client;

public class ClientUdpChannelTransmitter(UdpClient udpClient, byte[] serverKey)
    : UdpChannelTransmitter(udpClient, serverKey)
{
    public Action<Memory<byte>, long>? DataReceivedCallback { get; set; }
    protected override void OnReceiveData(ulong sessionId, IPEndPoint remoteEndPoint,
        Memory<byte> buffer, long channelCryptorPosition)
    {
        DataReceivedCallback?.Invoke(buffer, channelCryptorPosition);
    }
}