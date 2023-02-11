using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using PacketDotNet;
using VpnHood.Client.Device;
using VpnHood.Common.Net;

namespace VpnHood.Test;

internal class NullPacketCapture : IPacketCapture
{
    public bool Started { get; private set; }
    public bool IsDnsServersSupported => true;
    public IPAddress[]? DnsServers { get; set; }
    public bool CanExcludeApps  => true;
    public bool CanIncludeApps => true;
    public string[]? ExcludeApps { get; set; }
    public string[]? IncludeApps { get; set; }
    public IpNetwork[]? IncludeNetworks { get; set; }
    public bool IsMtuSupported => true;
    public int Mtu { get; set; }
    public bool IsAddIpV6AddressSupported => true;
    public bool AddIpV6Address { get; set; }
    public bool CanProtectSocket => true;
    public bool CanSendPacketToOutbound => true;
    public event EventHandler<PacketReceivedEventArgs>? OnPacketReceivedFromInbound;
    public event EventHandler? OnStopped;

    public void StartCapture()
    {
        Started = true;
    }

    public void StopCapture()
    {
        Started = false;
        OnStopped?.Invoke(this, EventArgs.Empty);
    }

    public void ProtectSocket(Socket socket)
    {
    }

    public void SendPacketToInbound(IPPacket ipPacket)
    {
    }

    public void SendPacketToInbound(IEnumerable<IPPacket> packets)
    {
    }

    public void SendPacketToOutbound(IPPacket ipPacket)
    {
    }

    public void SendPacketToOutbound(IEnumerable<IPPacket> ipPackets)
    {
    }
    public void Dispose()
    {
    }

}