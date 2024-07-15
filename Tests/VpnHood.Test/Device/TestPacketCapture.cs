using System.Net;
using PacketDotNet;
using VpnHood.Client.Device.WinDivert;

namespace VpnHood.Test.Device;

internal class TestPacketCapture(TestDeviceOptions deviceOptions) : WinDivertPacketCapture
{
    private IPAddress[]? _dnsServers;

    public override bool IsDnsServersSupported => deviceOptions.IsDnsServerSupported;

    public override IPAddress[]? DnsServers
    {
        get => IsDnsServersSupported ? _dnsServers : base.DnsServers;
        set
        {
            if (IsDnsServersSupported)
                _dnsServers = value;
            else
                base.DnsServers = value;
        }
    }

    public override bool CanSendPacketToOutbound => deviceOptions.CanSendPacketToOutbound;

    protected override void ProcessPacketReceivedFromInbound(IPPacket ipPacket)
    {
        var ignore = false;

        ignore |=
            ipPacket.Extract<UdpPacket>()?.DestinationPort == 53 &&
            deviceOptions.CaptureDnsAddresses != null &&
            deviceOptions.CaptureDnsAddresses.All(x => !x.Equals(ipPacket.DestinationAddress));

        // ignore protected packets
        if (ignore)
            SendPacketToOutbound(ipPacket);
        else
            base.ProcessPacketReceivedFromInbound(ipPacket);
    }
}