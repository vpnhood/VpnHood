using System.Net.NetworkInformation;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.PacketTransports;
using VpnHood.Core.Toolkit.Collections;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Utils;
namespace VpnHood.Core.Tunneling.Proxies;

public class PingProxy(bool autoDisposePackets) 
    : SinglePacketTransport(new PacketTransportOptions {
                AutoDisposePackets = autoDisposePackets,
                Blocking = false, 
                QueueCapacity = 1})
    , ITimeoutItem
{
    private readonly Ping _ping = new();
    public void Cancel() => _ping.SendAsyncCancel();
    public TimeSpan PingTimeout { get; set; } = TunnelDefaults.PingTimeout;
    public DateTime LastUsedTime { get; set; }
    public new bool IsDisposed => base.IsDisposed;

    protected override ValueTask SendPacketAsync(IpPacket ipPacket)
    {
        return ipPacket.Version == IpVersion.IPv4
            ? SendIpV4((IpV4Packet)ipPacket)
            : SendIpV6((IpV6Packet)ipPacket);
    }

    private async ValueTask SendIpV4(IpV4Packet ipPacket)
    {
        if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));
        if (ipPacket.Protocol != IpProtocol.IcmpV4)
            throw new InvalidOperationException(
                $"Packet is not {IpProtocol.IcmpV4}! Packet: {PacketLogger.Format(ipPacket)}");

        var icmpPacket = ipPacket.ExtractIcmpV4();
        if (icmpPacket.Type != IcmpV4Type.EchoRequest)
            throw new InvalidOperationException(
                $"The icmp is not {IcmpV4Type.EchoRequest}! Packet: {PacketLogger.Format(ipPacket)}");

        var noFragment = (ipPacket.FragmentFlags & 0x2) != 0;
        var pingOptions = new PingOptions(ipPacket.TimeToLive - 1, noFragment);
        var pingReply = await _ping.SendPingAsync(ipPacket.DestinationAddress, (int)PingTimeout.TotalMilliseconds,
            icmpPacket.Payload.ToArray(), pingOptions).Vhc();

        if (pingReply.Status != IPStatus.Success)
            throw new Exception($"Ping Reply has been failed! Status: {pingReply.Status}. Packet: {PacketLogger.Format(ipPacket)}");

        var replyPacket = PacketBuilder.BuildIcmpV4EchoReply(
            sourceAddress: ipPacket.DestinationAddress, destinationAddress: ipPacket.SourceAddress,
            payload: pingReply.Buffer, identifier: icmpPacket.Identifier, sequenceNumber: icmpPacket.SequenceNumber,
            updateChecksum: false);

        replyPacket.UpdateAllChecksums();
        OnPacketReceived(replyPacket);
    }

    private async ValueTask SendIpV6(IpV6Packet ipPacket)
    {
        var icmpPacket = ipPacket.ExtractIcmpV6();
        if (icmpPacket.Type != IcmpV6Type.EchoRequest)
            throw new InvalidOperationException(
                $"The icmp is not {IcmpV6Type.EchoRequest}! Packet: {PacketLogger.Format(ipPacket)}");

        var pingOptions = new PingOptions(ipPacket.TimeToLive - 1, true);
        var pingReply = await _ping.SendPingAsync(ipPacket.DestinationAddress, (int)PingTimeout.TotalMilliseconds,
            icmpPacket.Payload.ToArray(), pingOptions).Vhc();

        if (pingReply.Status != IPStatus.Success)
            throw new Exception($"Ping Reply has been failed. Status: {pingReply.Status}. Packet: {PacketLogger.Format(ipPacket)}");

        var replyPacket = PacketBuilder.BuildIcmpV6EchoReply(
            sourceAddress: ipPacket.DestinationAddress, destinationAddress: ipPacket.SourceAddress,
            payload: pingReply.Buffer, identifier: icmpPacket.Identifier, sequenceNumber: icmpPacket.SequenceNumber,
            updateChecksum: false);

        replyPacket.UpdateAllChecksums();
        OnPacketReceived(replyPacket);
    }

    protected override void DisposeManaged()
    {
        _ping.Dispose();

        base.DisposeManaged();
    }
}