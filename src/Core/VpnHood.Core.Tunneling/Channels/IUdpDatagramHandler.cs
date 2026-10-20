using System.Net;

namespace VpnHood.Core.Tunneling.Channels;

// takes each datagram a UDP socket receives: the server's listener host hands it to its session, and a client's
// UDP channel opens its own
public interface IUdpDatagramHandler
{
    void OnDatagramReceived(UdpChannelTransmitter transmitter, Memory<byte> datagram, IPEndPoint remoteEndPoint);
}
