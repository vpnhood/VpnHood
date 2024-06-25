using PacketDotNet;
using VpnHood.Client.Device;
using VpnHood.Common.Logging;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Factory;
using VpnHood.Tunneling.Utils;

namespace VpnHood.Client;

internal class ClientProxyManager(IPacketCapture packetCapture,ISocketFactory socketFactory, ProxyManagerOptions options)
    : ProxyManager(socketFactory, options)
{
    // PacketCapture can not protect Ping so PingProxy does not work
    protected override bool IsPingSupported => false;

    public override Task OnPacketReceived(IPPacket ipPacket)
    {
        if (VhLogger.IsDiagnoseMode)
            PacketUtil.LogPacket(ipPacket, "Delegating packet to host via proxy.");

        packetCapture.SendPacketToInbound(ipPacket);
        return Task.FromResult(0);
    }

}