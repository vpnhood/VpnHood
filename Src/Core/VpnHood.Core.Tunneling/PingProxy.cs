using System.Buffers.Binary;
using System.Net.NetworkInformation;
using PacketDotNet;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.VhPackets;
using VpnHood.Core.Toolkit.Collections;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Utils;
using IcmpV6Type = PacketDotNet.IcmpV6Type;

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

    public async Task<IPPacket> Send(IPPacket ipPacket)
    {
        if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));

        try {
            // Ping does not support concurrent call
            await _finishSemaphore.WaitAsync().VhConfigureAwait();
            IsBusy = true;

            LastUsedTime = FastDateTime.Now;
            return ipPacket.Version == IPVersion.IPv4
                ? await SendIpV4(ipPacket.Extract<IPv4Packet>()).VhConfigureAwait()
                : await SendIpV6(ipPacket.Extract<IPv6Packet>()).VhConfigureAwait();
        }
        finally {
            IsBusy = false;
            _finishSemaphore.Release();
        }
    }

    private async Task<IPPacket> SendIpV4(IPv4Packet ipPacket)
    {
        if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));
        if (ipPacket.Protocol != ProtocolType.Icmp)
            throw new InvalidOperationException(
                $"Packet is not {ProtocolType.Icmp}! Packet: {PacketLogger.Format(ipPacket)}");

        var icmpPacket = ipPacket.ExtractIcmpV4();
        if (icmpPacket.TypeCode != IcmpV4TypeCode.EchoRequest)
            throw new InvalidOperationException(
                $"The icmp is not {IcmpV4TypeCode.EchoRequest}! Packet: {PacketLogger.Format(ipPacket)}");

        var noFragment = (ipPacket.FragmentFlags & 0x2) != 0;
        var pingOptions = new PingOptions(ipPacket.TimeToLive - 1, noFragment);
        var pingReply = await _ping.SendPingAsync(ipPacket.DestinationAddress, (int)IcmpTimeout.TotalMilliseconds,
            icmpPacket.Data, pingOptions).VhConfigureAwait();

        if (pingReply.Status != IPStatus.Success)
            throw new Exception($"Ping Reply has been failed! Status: {pingReply.Status}");

        icmpPacket.TypeCode = IcmpV4TypeCode.EchoReply;
        icmpPacket.Data = pingReply.Buffer;

        ipPacket.DestinationAddress = ipPacket.SourceAddress;
        ipPacket.SourceAddress = pingReply.Address;
        ipPacket.PayloadPacket = icmpPacket;
        ipPacket.UpdateAllChecksums();
        return ipPacket;
    }

    private async Task<IPPacket> SendIpV6(IPv6Packet ipPacket)
    {
        if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));
        if (ipPacket.Protocol != ProtocolType.IcmpV6)
            throw new InvalidOperationException($"Packet is not {ProtocolType.IcmpV6}!");

        // We should not use Task due its stack usage, this method is called by many session each many times!
        var icmpPacket = ipPacket.ExtractIcmpV6();
        if (icmpPacket.Type != IcmpV6Type.EchoRequest)
            throw new InvalidOperationException(
                $"The icmp is not {IcmpV6Type.EchoRequest}! Packet: {PacketLogger.Format(ipPacket)}");

        PacketLogger.LogPacket(ipPacket, "Delegating a ping to host...");
        var pingOptions = new PingOptions(ipPacket.TimeToLive - 1, true);
        var pingData = icmpPacket.Bytes[8..];
        var pingReply = await _ping
            .SendPingAsync(ipPacket.DestinationAddress, (int)IcmpTimeout.TotalMilliseconds, pingData, pingOptions)
            .VhConfigureAwait();

        // todo: change it by VhIpPacket
        var identifier = BinaryPrimitives.ReadUInt16BigEndian(icmpPacket.Bytes.AsSpan().Slice(4, 2));
        var sequenceNumber = BinaryPrimitives.ReadUInt16BigEndian(icmpPacket.Bytes.AsSpan().Slice(6, 2));
        using var replyPacket = VhPacketBuilder.BuildIcmpV6EchoReply(
            sourceAddress: ipPacket.DestinationAddress, destinationAddress: ipPacket.SourceAddress,
            payload: pingReply.Buffer, identifier: identifier, sequenceNumber: sequenceNumber);

        ipPacket = PacketBuilder.Parse(replyPacket.Buffer.ToArray()).Extract<IPv6Packet>();
        PacketLogger.LogPacket(ipPacket, "Delegating a ping to client...");
        return ipPacket;
    }

    public void Dispose()
    {
        if (!Disposed) return;
        Disposed = true;

        _ping.Dispose();
    }
}