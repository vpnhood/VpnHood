using System.Net.NetworkInformation;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.VhPackets;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Utils;
namespace VpnHood.Core.Tunneling;

public class PingProxy() : PacketProxy(1)
{
    private readonly Ping _ping = new();
    public void Cancel() => _ping.SendAsyncCancel();
    public TimeSpan IcmpTimeout { get; set; } = TimeSpan.FromSeconds(5);

    protected override Task SendPacketAsync(IpPacket ipPacket)
    {
        return ipPacket.Version == IpVersion.IPv4
            ? SendIpV4((IpV4Packet)ipPacket)
            : SendIpV6((IpV6Packet)ipPacket);
    }

    private async Task SendIpV4(IpV4Packet ipPacket)
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
        var pingReply = await _ping.SendPingAsync(ipPacket.DestinationAddress, (int)IcmpTimeout.TotalMilliseconds,
            icmpPacket.Payload.ToArray(), pingOptions).VhConfigureAwait();

        if (pingReply.Status != IPStatus.Success)
            throw new Exception($"Ping Reply has been failed! Status: {pingReply.Status}. Packet: {PacketLogger.Format(ipPacket)}");

        var replyPacket = PacketBuilder.BuildIcmpV4EchoReply(
            sourceAddress: ipPacket.DestinationAddress, destinationAddress: ipPacket.SourceAddress,
            payload: pingReply.Buffer, identifier: icmpPacket.Identifier, sequenceNumber: icmpPacket.SequenceNumber,
            updateChecksum: false);

        replyPacket.UpdateAllChecksums();
        OnPacketReceived(replyPacket);
    }

    private async Task SendIpV6(IpV6Packet ipPacket)
    {
        var icmpPacket = ipPacket.ExtractIcmpV6();
        if (icmpPacket.Type != IcmpV6Type.EchoRequest)
            throw new InvalidOperationException(
                $"The icmp is not {IcmpV6Type.EchoRequest}! Packet: {PacketLogger.Format(ipPacket)}");

        var pingOptions = new PingOptions(ipPacket.TimeToLive - 1, true);
        var pingReply = await _ping.SendPingAsync(ipPacket.DestinationAddress, (int)IcmpTimeout.TotalMilliseconds,
            icmpPacket.Payload.ToArray(), pingOptions).VhConfigureAwait();

        if (pingReply.Status != IPStatus.Success)
            throw new Exception($"Ping Reply has been failed. Status: {pingReply.Status}. Packet: {PacketLogger.Format(ipPacket)}");

        var replyPacket = PacketBuilder.BuildIcmpV6EchoReply(
            sourceAddress: ipPacket.DestinationAddress, destinationAddress: ipPacket.SourceAddress,
            payload: pingReply.Buffer, identifier: icmpPacket.Identifier, sequenceNumber: icmpPacket.SequenceNumber,
            updateChecksum: false);

        replyPacket.UpdateAllChecksums();
        OnPacketReceived(replyPacket);
    }

    protected override void Dispose(bool disposing)
    {
        if (Disposed) 
            return;

        if (disposing) {
            // Dispose managed resources
            _ping.Dispose();
        }

        // Dispose unmanaged resources
        base.Dispose(disposing);
    }
}