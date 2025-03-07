using PacketDotNet;
using VpnHood.Core.Client.Device.WinDivert;
using VpnHood.Core.VpnAdapters.WinDivert;

namespace VpnHood.Test.Device;

public class TestVpnAdapter(TestVpnAdapterOptions vpnAdapterOptions) : WinDivertVpnAdapter
{
    public override bool IsDnsServerSupported => vpnAdapterOptions.IsDnsServerSupported;

    public override bool CanSendPacketToOutbound => vpnAdapterOptions.CanSendPacketToOutbound;

    protected override void ProcessPacketReceivedFromInbound(IPPacket ipPacket)
    {
        var ignore = false;

        ignore |=
            ipPacket.Extract<UdpPacket>()?.DestinationPort == 53 &&
            vpnAdapterOptions.CaptureDnsAddresses != null &&
            vpnAdapterOptions.CaptureDnsAddresses.All(x => !x.Equals(ipPacket.DestinationAddress));

        // ignore protected packets
        if (ignore)
            SendPacketToOutbound(ipPacket);
        else
            base.ProcessPacketReceivedFromInbound(ipPacket);
    }
}