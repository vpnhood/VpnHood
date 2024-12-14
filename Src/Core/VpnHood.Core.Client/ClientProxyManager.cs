using PacketDotNet;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Factory;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Client;

internal class ClientProxyManager(
    IPacketCapture packetCapture,
    ISocketFactory socketFactory,
    ProxyManagerOptions options)
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