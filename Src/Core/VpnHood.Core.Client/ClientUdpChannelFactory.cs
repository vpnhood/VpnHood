using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Tunneling.Channels;

namespace VpnHood.Core.Client;

public static class ClientUdpChannelFactory
{
    public static UdpChannel Create(ClientUdpChannelOptions options)
    {
        var udpClient = options.SocketFactory.CreateUdpClient(options.RemoteEndPoint.AddressFamily);
        UdpChannelTransmitter2? transmitter = null;
        IUdpTransport? udpTransport = null;
        try {
            transmitter = new UdpChannelTransmitter2(udpClient);
            udpTransport = transmitter.CreateTransport(options.SessionId, options.ServerKey, options.RemoteEndPoint);
            var udpChannel = new UdpChannel(udpTransport, options);
            transmitter.BufferSize = options.BufferSize;
            transmitter.UdpChannel = udpChannel;
            return udpChannel;
        }
        catch {
            udpTransport?.Dispose();
            transmitter?.Dispose();
            udpClient.Dispose();
            throw;
        }
    }
}