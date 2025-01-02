using System.Net;
using System.Net.Sockets;
using PacketDotNet;
using VpnHood.Core.Tunneling.Utils;
using ProtocolType = PacketDotNet.ProtocolType;

namespace VpnHood.Core.Tunneling;

// not read
public class PingV4ProxyRaw : IDisposable
{
    private readonly Socket _socket;
    public PingV4ProxyRaw()
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, System.Net.Sockets.ProtocolType.Icmp);
        _socket.Bind(new IPEndPoint(IPAddress.Any, 0));
        _ = PingListener();
    }

    private async Task PingListener()
    {
        var buffer = new byte[2000];
        while (_socket.Connected) {
            await _socket.ReceiveAsync(buffer, SocketFlags.None);
            //var ipPacket = Packet.ParsePacket(LinkLayers.Raw, buffer).Extract<IPPacket>();
        }
    }
    public void Send(IPPacket ipPacket)
    {
        if (ipPacket.Protocol != ProtocolType.Icmp)
            throw new InvalidOperationException("Invalid ICMP packet.");

        var icmpV4Packet = PacketUtil.ExtractIcmp(ipPacket);
        _socket.SendTo(icmpV4Packet.Bytes, new IPEndPoint(ipPacket.DestinationAddress, 0));
    }

    public void Dispose()
    {
        _socket.Dispose();
    }
}