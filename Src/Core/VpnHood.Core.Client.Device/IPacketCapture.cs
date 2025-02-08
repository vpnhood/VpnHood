using System.Net;
using PacketDotNet;

namespace VpnHood.Core.Client.Device;

public interface IPacketCapture : IDisposable
{
    event EventHandler<PacketReceivedEventArgs> PacketReceivedFromInbound;
    event EventHandler Stopped;
    bool Started { get; }
    bool IsDnsServersSupported { get; }
    bool IsMtuSupported { get; }
    bool CanProtectSocket { get; }
    bool CanSendPacketToOutbound { get; }
    bool CanDetectInProcessPacket { get; }
    void StartCapture(VpnAdapterOptions adapterOptions);
    void StopCapture();
    void ProtectSocket(System.Net.Sockets.Socket socket);
    void SendPacketToInbound(IPPacket ipPacket);
    void SendPacketToInbound(IList<IPPacket> packets);
    void SendPacketToOutbound(IPPacket ipPacket);
    void SendPacketToOutbound(IList<IPPacket> ipPackets);
    bool IsInProcessPacket(ProtocolType protocol, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint);
}