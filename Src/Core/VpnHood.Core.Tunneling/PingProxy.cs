using System.Buffers.Binary;
using System.Net.NetworkInformation;
using VpnHood.Core.Packets.VhPackets;
using VpnHood.Core.Toolkit.Collections;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Tunneling;

public class PingProxy : ITimeoutItem
{
    private readonly Ping _ping = new();
    private readonly SemaphoreSlim _finishSemaphore = new(1, 1);

    public DateTime LastUsedTime { get; set; } = DateTime.MinValue;
    public bool IsBusy { get; private set; }
    public void Cancel() => _ping.SendAsyncCancel();
    public TimeSpan IcmpTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public bool Disposed { get; private set; }

    public async Task<IpPacket> Send(IpPacket ipPacket)
    {
        if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));

        try {
            // Ping does not support concurrent call
            await _finishSemaphore.WaitAsync().VhConfigureAwait();
            IsBusy = true;

            LastUsedTime = FastDateTime.Now;
            return ipPacket.Version == IpVersion.IPv4
                ? await SendIpV4((IpV4Packet)ipPacket).VhConfigureAwait()
                : await SendIpV6((IpV6Packet)ipPacket).VhConfigureAwait();
        }
        finally {
            IsBusy = false;
            _finishSemaphore.Release();
        }
    }

    private async Task<IpPacket> SendIpV4(IpV4Packet ipPacket)
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
            throw new Exception($"Ping Reply has been failed! Status: {pingReply.Status}");

        var replyPacket = PacketBuilder.BuildIcmpV4EchoReply(
            sourceAddress: ipPacket.DestinationAddress, destinationAddress: ipPacket.SourceAddress,
            payload: pingReply.Buffer, identifier: icmpPacket.Identifier, sequenceNumber: icmpPacket.SequenceNumber);

        return replyPacket;
    }

    private async Task<IpPacket> SendIpV6(IpV6Packet ipPacket)
    {
        if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));
        if (ipPacket.Protocol != IpProtocol.IcmpV6)
            throw new InvalidOperationException(
                $"Packet is not {IpProtocol.IcmpV6}! Packet: {PacketLogger.Format(ipPacket)}");

        var icmpPacket = ipPacket.ExtractIcmpV6();
        if (icmpPacket.Type != IcmpV6Type.EchoRequest)
            throw new InvalidOperationException(
                $"The icmp is not {IcmpV6Type.EchoRequest}! Packet: {PacketLogger.Format(ipPacket)}");

        var pingOptions = new PingOptions(ipPacket.TimeToLive - 1, true);
        var pingReply = await _ping.SendPingAsync(ipPacket.DestinationAddress, (int)IcmpTimeout.TotalMilliseconds,
            icmpPacket.Payload.ToArray(), pingOptions).VhConfigureAwait();

        if (pingReply.Status != IPStatus.Success)
            throw new Exception($"Ping Reply has been failed! Status: {pingReply.Status}");

        var replyPacket = PacketBuilder.BuildIcmpV6EchoReply(
            sourceAddress: ipPacket.DestinationAddress, destinationAddress: ipPacket.SourceAddress,
            payload: pingReply.Buffer, identifier: icmpPacket.Identifier, sequenceNumber: icmpPacket.SequenceNumber);

        return replyPacket;
    }

    public void Dispose()
    {
        if (!Disposed) return;
        Disposed = true;

        _ping.Dispose();
    }
}