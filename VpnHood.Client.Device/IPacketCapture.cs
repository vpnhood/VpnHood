using System.Net;
using PacketDotNet;
using VpnHood.Common.Net;

namespace VpnHood.Client.Device;

public interface IPacketCapture : IDisposable
{
    event EventHandler<PacketReceivedEventArgs> PacketReceivedFromInbound;
    event EventHandler Stopped;
    bool Started { get; }
    bool IsDnsServersSupported { get; }
    IPAddress[]? DnsServers { get; set; }
    bool CanExcludeApps { get; }
    bool CanIncludeApps { get; }
    string[]? ExcludeApps { get; set; }
    string[]? IncludeApps { get; set; }
    IpNetwork[]? IncludeNetworks { get; set; }
    bool IsMtuSupported { get; }
    int Mtu { get; set; }
    bool IsAddIpV6AddressSupported { get; }
    bool AddIpV6Address { get; set; }
    bool CanProtectSocket { get; }
    bool CanSendPacketToOutbound { get; }
    void StartCapture();
    void StopCapture();
    void ProtectSocket(System.Net.Sockets.Socket socket);
    void SendPacketToInbound(IPPacket ipPacket);
    void SendPacketToInbound(IList<IPPacket> packets);
    void SendPacketToOutbound(IPPacket ipPacket);
    void SendPacketToOutbound(IList<IPPacket> ipPackets);
    bool? IsInProcessPacket(ProtocolType protocol, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint);
}