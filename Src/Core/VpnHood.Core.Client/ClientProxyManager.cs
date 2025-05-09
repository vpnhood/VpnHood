using VpnHood.Core.Packets.VhPackets;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Sockets;
using VpnHood.Core.Tunneling.Utils;
using VpnHood.Core.VpnAdapters.Abstractions;

namespace VpnHood.Core.Client;

internal class ClientProxyManager(
    IVpnAdapter vpnAdapter,
    ISocketFactory socketFactory,
    ProxyManagerOptions options)
    : ProxyManager(socketFactory, options)
{
    // VpnAdapter can not protect Ping so PingProxy does not work
    protected override bool IsPingSupported => false;

    public override void OnPacketReceived(IpPacket ipPacket)
    {
        if (Disposed) return;
        if (VhLogger.IsDiagnoseMode)
            PacketLogger.LogPacket(ipPacket, "Delegating packet to host via proxy.");

        // writing to local adapter is pretty fast so we don't need to await it
        vpnAdapter.SendPacket(ipPacket);
    }
}