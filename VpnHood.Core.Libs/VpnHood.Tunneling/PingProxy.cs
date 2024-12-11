﻿using System.Net.NetworkInformation;
using PacketDotNet;
using PacketDotNet.Utils;
using VpnHood.Common.Collections;
using VpnHood.Common.Utils;
using VpnHood.Tunneling.Utils;

namespace VpnHood.Tunneling;

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
                $"Packet is not {ProtocolType.Icmp}! Packet: {PacketUtil.Format(ipPacket)}");

        var icmpPacket = PacketUtil.ExtractIcmp(ipPacket);
        if (icmpPacket.TypeCode != IcmpV4TypeCode.EchoRequest)
            throw new InvalidOperationException(
                $"The icmp is not {IcmpV4TypeCode.EchoRequest}! Packet: {PacketUtil.Format(ipPacket)}");

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
        ipPacket.ParentPacket = icmpPacket;
        PacketUtil.UpdateIpPacket(ipPacket);

        return ipPacket;
    }

    private async Task<IPPacket> SendIpV6(IPv6Packet ipPacket)
    {
        if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));
        if (ipPacket.Protocol != ProtocolType.IcmpV6)
            throw new InvalidOperationException($"Packet is not {ProtocolType.IcmpV6}!");

        // We should not use Task due its stack usage, this method is called by many session each many times!
        var icmpPacket = PacketUtil.ExtractIcmpV6(ipPacket);
        if (icmpPacket.Type != IcmpV6Type.EchoRequest)
            throw new InvalidOperationException(
                $"The icmp is not {IcmpV6Type.EchoRequest}! Packet: {PacketUtil.Format(ipPacket)}");

        var pingOptions = new PingOptions(ipPacket.TimeToLive - 1, true);
        var pingData = icmpPacket.Bytes[8..];
        var pingReply = await _ping
            .SendPingAsync(ipPacket.DestinationAddress, (int)IcmpTimeout.TotalMilliseconds, pingData, pingOptions)
            .VhConfigureAwait();

        // IcmpV6 packet generation is not fully implemented by packetNet
        // So create all packet in buffer
        icmpPacket.Type = IcmpV6Type.EchoReply;
        icmpPacket.Code = 0;
        var buffer = new byte[pingReply.Buffer.Length + 8];
        Array.Copy(icmpPacket.Bytes, 0, buffer, 0, 8);
        Array.Copy(pingReply.Buffer, 0, buffer, 8, pingReply.Buffer.Length);
        icmpPacket = new IcmpV6Packet(new ByteArraySegment(buffer));

        ipPacket.DestinationAddress = ipPacket.SourceAddress;
        ipPacket.SourceAddress = pingReply.Address;
        ipPacket.ParentPacket = icmpPacket;
        PacketUtil.UpdateIpPacket(ipPacket);

        return ipPacket;
    }

    public void Dispose()
    {
        if (!Disposed)
            return;
        Disposed = true;

        _ping.Dispose();
    }
}