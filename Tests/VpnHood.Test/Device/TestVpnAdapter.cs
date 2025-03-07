using PacketDotNet;
using VpnHood.Core.VpnAdapters.WinDivert;

namespace VpnHood.Test.Device;

public class TestVpnAdapter(TestVpnAdapterOptions vpnAdapterOptions) : WinDivertVpnAdapter
{
    public override bool IsDnsServerSupported => vpnAdapterOptions.IsDnsServerSupported;
    protected override void ProcessPacketReceived(IPPacket ipPacket)
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
            base.ProcessPacketReceived(ipPacket);
    }
}