using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Tunneling.Channels;

namespace VpnHood.Core.Client;

public class ClientUdpChannelFactory
{
    private class ClientUdpChannelTransmitter(UdpClient udpClient, byte[] serverKey)
        : UdpChannelTransmitter(udpClient, serverKey)
    {
        public UdpChannel? UdpChannel { get; set; }

        protected override void OnReceiveData(ulong sessionId, IPEndPoint remoteEndPoint,
            Memory<byte> buffer, long channelCryptorPosition)
        {
            UdpChannel?.OnDataReceived(buffer, channelCryptorPosition);
        }
    }

    public static UdpChannel Create(ClientUdpChannelOptions options)
    {
        var udpClient = options.SocketFactory.CreateUdpClient(options.RemoteEndPoint.AddressFamily);
        ClientUdpChannelTransmitter? transmitter = null;
        try {
            transmitter = new ClientUdpChannelTransmitter(udpClient, options.ServerKey);
            var udpChannel = new UdpChannel(transmitter, options);
            transmitter.BufferSize = options.BufferSize;
            transmitter.UdpChannel = udpChannel;
            return udpChannel;
        }
        catch {
            transmitter?.Dispose();
            udpClient.Dispose();
            throw;
        }
    }
}

