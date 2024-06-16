using System.Net;
using System.Net.Sockets;
using PacketDotNet;
using VpnHood.Client.Device.WinDivert;
using VpnHood.Test.Services;

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
    public override bool CanProtectSocket => !deviceOptions.CanSendPacketToOutbound;

    protected override void ProcessPacketReceivedFromInbound(IPPacket ipPacket)
    {
        var ignore = false;

        ignore |=
            ipPacket.Extract<UdpPacket>()?.DestinationPort == 53 &&
            deviceOptions.CaptureDnsAddresses != null &&
            deviceOptions.CaptureDnsAddresses.All(x => !x.Equals(ipPacket.DestinationAddress));

        ignore |= TestSocketProtector.IsProtectedPacket(ipPacket);

        // ignore protected packets
        if (ignore)
            SendPacketToOutbound(ipPacket);
        else
            base.ProcessPacketReceivedFromInbound(ipPacket);
    }

    public override void ProtectSocket(Socket socket)
    {
        if (CanProtectSocket)
            TestSocketProtector.ProtectSocket(socket);
        else
            base.ProtectSocket(socket);
    }
}