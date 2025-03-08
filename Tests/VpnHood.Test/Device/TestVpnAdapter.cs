using PacketDotNet;
using VpnHood.Core.VpnAdapters.WinDivert;

namespace VpnHood.Test.Device;

public class TestVpnAdapter(TestVpnAdapterOptions vpnAdapterOptions) 
    : WinDivertVpnAdapter(new WinDivertVpnAdapterSettings {
        AdapterName = "VpnHoodTestAdapter"
    })
{
    protected override void ProcessPacketReceived(IPPacket ipPacket)
    {
        var ignore = false;

        ignore |=
            ipPacket.Extract<UdpPacket>()?.DestinationPort == 53 &&
            vpnAdapterOptions.CaptureDnsAddresses != null &&
            vpnAdapterOptions.CaptureDnsAddresses.All(x => !x.Equals(ipPacket.DestinationAddress));

        // ignore protected packets
        // todo: temporary
        if (ignore)
            SendPacket(ipPacket, true);
        else
            base.ProcessPacketReceived(ipPacket);
    }
}