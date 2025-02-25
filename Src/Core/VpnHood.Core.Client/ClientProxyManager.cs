using PacketDotNet;
using VpnHood.Core.Client.Device.Adapters;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Sockets;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Client;

internal class ClientProxyManager(
    IVpnAdapter vpnAdapter,
    ISocketFactory socketFactory,
    ProxyManagerOptions options)
    : ProxyManager(socketFactory, options)
{
    // VpnAdapter can not protect Ping so PingProxy does not work
    protected override bool IsPingSupported => false;

    public override Task OnPacketReceived(IPPacket ipPacket)
    {
        if (VhLogger.IsDiagnoseMode)
            PacketUtil.LogPacket(ipPacket, "Delegating packet to host via proxy.");

        vpnAdapter.SendPacketToInbound(ipPacket);
        return Task.FromResult(0);
    }
}