using System.Net;
using System.Net.Sockets;
using PacketDotNet;
using VpnHood.Client.Device;
using VpnHood.Common.Net;

namespace VpnHood.Test;

internal class TestNullPacketCapture : IPacketCapture
{
    public event EventHandler<PacketReceivedEventArgs>? PacketReceivedFromInbound;
    public event EventHandler? Stopped;
    public bool Started { get; set; }
    public bool IsDnsServersSupported { get; set; }
    public IPAddress[]? DnsServers { get; set; }
    public bool CanExcludeApps { get; set; } = true;
    public bool CanIncludeApps { get; set; } = true;
    public string[]? ExcludeApps { get; set; }
    public string[]? IncludeApps { get; set; }
    public IpNetwork[]? IncludeNetworks { get; set; }
    public bool IsMtuSupported { get; set; } = true;
    public int Mtu { get; set; }
    public bool IsAddIpV6AddressSupported { get; set; } = true;
    public bool AddIpV6Address { get; set; } = true;
    public bool CanProtectSocket { get; set; } = true;
    public bool CanSendPacketToOutbound { get; set; } = false;
    public void StartCapture()
    {
        Started = true;
        _ = PacketReceivedFromInbound; //prevent not used warning
    }

    public void StopCapture()
    {
        Started = false;
        Stopped?.Invoke(this, EventArgs.Empty);
    }

    public void ProtectSocket(Socket socket)
    {
        // nothing

    }

    public void SendPacketToInbound(IPPacket ipPacket)
    {
        // nothing
    }

    public void SendPacketToInbound(IList<IPPacket> packets)
    {
        // nothing
    }

    public void SendPacketToOutbound(IPPacket ipPacket)
    {
        // nothing
    }

    public void SendPacketToOutbound(IList<IPPacket> ipPackets)
    {
        // nothing
    }

    public void Dispose()
    {
        StopCapture();
    }

}