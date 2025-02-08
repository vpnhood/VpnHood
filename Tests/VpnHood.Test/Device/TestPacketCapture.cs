using PacketDotNet;
using VpnHood.Core.Client.Device.WinDivert;

namespace VpnHood.Test.Device;

public class TestPacketCapture(TestPacketCaptureOptions packetCaptureOptions) : WinDivertPacketCapture
{
    public override bool IsDnsServersSupported => packetCaptureOptions.IsDnsServerSupported;

    public override bool CanSendPacketToOutbound => packetCaptureOptions.CanSendPacketToOutbound;

    protected override void ProcessPacketReceivedFromInbound(IPPacket ipPacket)
    {
        var ignore = false;

        ignore |=
            ipPacket.Extract<UdpPacket>()?.DestinationPort == 53 &&
            packetCaptureOptions.CaptureDnsAddresses != null &&
            packetCaptureOptions.CaptureDnsAddresses.All(x => !x.Equals(ipPacket.DestinationAddress));

        // ignore protected packets
        if (ignore)
            SendPacketToOutbound(ipPacket);
        else
            base.ProcessPacketReceivedFromInbound(ipPacket);
    }
}