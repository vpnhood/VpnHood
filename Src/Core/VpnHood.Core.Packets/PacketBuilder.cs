using System.Buffers;
using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Packets;

public static class PacketBuilder
{
    public static IpPacket Parse(ReadOnlySpan<byte> buffer, MemoryPool<byte>? memoryPool = null)
    {
        // adjust buffer length by parsing the packet length
        var packetLength = PacketUtil.ReadPacketLength(buffer);
        buffer = buffer[..packetLength]; 

        // copy buffer to memory pool
        memoryPool ??= MemoryPool<byte>.Shared;
        var memoryOwner = memoryPool.Rent(buffer.Length);
        buffer.CopyTo(memoryOwner.Memory.Span);

        return Attach(memoryOwner);
    }

    public static IpPacket Attach(Memory<byte> buffer)
    {
        // find packet length by reading the first byte
        var version = IpPacket.GetPacketVersion(buffer.Span);
        return version switch {
            IpVersion.IPv4 => new IpV4Packet(buffer),
            IpVersion.IPv6 => new IpV6Packet(buffer),
            _ => throw new NotSupportedException($"IP version {version} not supported."),
        };
    }

    public static IpPacket Attach(IMemoryOwner<byte> memoryOwner)
    {
        var version = IpPacket.GetPacketVersion(memoryOwner.Memory.Span);
        return version switch {
            IpVersion.IPv4 => new IpV4Packet(memoryOwner),
            IpVersion.IPv6 => new IpV6Packet(memoryOwner),
            _ => throw new NotSupportedException($"IP version {version} not supported."),
        };
    }

    public static IpPacket BuildIp(IPAddress sourceAddress, IPAddress destinationAddress,
        IpProtocol protocol, int payloadLength, MemoryPool<byte>? memoryPool = null)
    {
        // validate address length
        if (sourceAddress.AddressFamily != destinationAddress.AddressFamily)
            throw new ArgumentException("SourceAddress and DestinationAddress must have a same ip version.");

        // validate payload length
        memoryPool ??= MemoryPool<byte>.Shared;

        // create IP packet
        IpPacket ipPacket = sourceAddress.AddressFamily switch {
            AddressFamily.InterNetwork => new IpV4Packet(memoryPool.Rent(20 + payloadLength), 20 + payloadLength, protocol, 0),
            AddressFamily.InterNetworkV6 => new IpV6Packet(memoryPool.Rent(40 + payloadLength), 40 + payloadLength, protocol),
            _ => throw new NotSupportedException($"{sourceAddress.AddressFamily} not supported.")
        };

        ipPacket.SourceAddress = sourceAddress;
        ipPacket.DestinationAddress = destinationAddress;
        ipPacket.TimeToLive = 64;
        return ipPacket;
    }

    public static IpPacket BuildIp(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress,
        IpProtocol protocol, int payloadLength, MemoryPool<byte>? memoryPool = null)
    {
        // validate address length
        if (sourceAddress.Length != destinationAddress.Length)
            throw new ArgumentException("SourceAddress and DestinationAddress must have a same ip version.");

        // validate payload length
        memoryPool ??= MemoryPool<byte>.Shared;

        // create IP packet
        IpPacket ipPacket = sourceAddress.Length switch {
            4 => new IpV4Packet(memoryPool.Rent(20 + payloadLength), 20 + payloadLength, protocol, 0),
            16 => new IpV6Packet(memoryPool.Rent(40 + payloadLength), 40 + payloadLength, protocol),
            _ => throw new NotSupportedException($"IP version {sourceAddress.Length} not supported.")
        };

        ipPacket.SourceAddressSpan = sourceAddress;
        ipPacket.DestinationAddressSpan = destinationAddress;
        ipPacket.TimeToLive = 64;
        return ipPacket;
    }

    public static IpPacket BuildUdp(IPEndPoint sourceEndPoint, IPEndPoint destinationEndPoint,
        ReadOnlySpan<byte> payload)
    {
        return BuildUdp(
            sourceEndPoint.Address, destinationEndPoint.Address,
            sourceEndPoint.Port, destinationEndPoint.Port,
            payload);
    }

    public static IpPacket BuildUdp(IPAddress sourceAddress, IPAddress destinationAddress,
        int sourcePort, int destinationPort, ReadOnlySpan<byte> payload)
    {
        var ipPacket = BuildIp(sourceAddress, destinationAddress, IpProtocol.Udp, 8 + payload.Length);
        var udpPacket = ipPacket.BuildUdp();
        udpPacket.SourcePort = (ushort)sourcePort;
        udpPacket.DestinationPort = (ushort)destinationPort;
        payload.CopyTo(udpPacket.Payload.Span);
        return ipPacket;
    }

    public static IpPacket BuildTcp(IPEndPoint sourceEndPoint, IPEndPoint destinationEndPoint,
        ReadOnlySpan<byte> options, ReadOnlySpan<byte> payload)
    {
        return BuildTcp(
            sourceEndPoint.Address, destinationEndPoint.Address,
            sourceEndPoint.Port, destinationEndPoint.Port,
            options, payload);
    }

    public static IpPacket BuildTcp(IPAddress sourceAddress, IPAddress destinationAddress,
        int sourcePort, int destinationPort, ReadOnlySpan<byte> options, ReadOnlySpan<byte> payload)
    {
        var ipPacket = BuildIp(sourceAddress, destinationAddress, IpProtocol.Tcp, 20 + options.Length + payload.Length);
        var udpPacket = ipPacket.BuildTcp();
        udpPacket.SourcePort = (ushort)sourcePort;
        udpPacket.DestinationPort = (ushort)destinationPort;
        payload.CopyTo(udpPacket.Payload.Span);
        options.CopyTo(udpPacket.Options.Span);
        return ipPacket;
    }

    public static IpPacket BuildIcmpV4(IPAddress sourceAddress, IPAddress destinationAddress,
        ReadOnlySpan<byte> payload)
    {
        var ipPacket = BuildIp(sourceAddress, destinationAddress, IpProtocol.IcmpV4, 8 + payload.Length);
        payload.CopyTo(ipPacket.BuildIcmpV4().Payload.Span);
        return ipPacket;
    }
    public static IpPacket BuildIcmpV6(IPAddress sourceAddress, IPAddress destinationAddress,
        ReadOnlySpan<byte> payload)
    {
        var ipPacket = BuildIp(sourceAddress, destinationAddress, IpProtocol.IcmpV6, 8 + payload.Length);
        payload.CopyTo(ipPacket.BuildIcmpV6().Payload.Span);
        return ipPacket;
    }


    public static IpPacket BuildIcmpEchoRequest(IPAddress sourceAddress, IPAddress destinationAddress,
        ReadOnlySpan<byte> payload, ushort identifier = 0, ushort sequenceNumber = 0, bool updateChecksum = true)
    {
        if (sourceAddress.AddressFamily != destinationAddress.AddressFamily)
            throw new ArgumentException("SourceAddress and DestinationAddress must have a same address family.");

        return sourceAddress.AddressFamily switch {
            AddressFamily.InterNetwork => BuildIcmpV4EchoRequest(sourceAddress, destinationAddress, payload, identifier, sequenceNumber, updateChecksum),
            AddressFamily.InterNetworkV6 => BuildIcmpV6EchoRequest(sourceAddress, destinationAddress, payload, identifier, sequenceNumber, updateChecksum),
            _ => throw new NotSupportedException($"{sourceAddress.AddressFamily} not supported.")
        };
    }
    

    public static IpPacket BuildIcmpV4EchoRequest(IPAddress sourceAddress, IPAddress destinationAddress,
         ReadOnlySpan<byte> payload, ushort identifier = 0, ushort sequenceNumber = 0, bool updateChecksum = true)
    {
        if (!sourceAddress.IsV4() || !destinationAddress.IsV4())
            throw new ArgumentException("SourceAddress and DestinationAddress must be IPv4 addresses.");

        // ICMP echo request
        var ipPacket = BuildIcmpV4(sourceAddress, destinationAddress, payload);
        var icmp = ipPacket.ExtractIcmpV4();
        icmp.Type = IcmpV4Type.EchoRequest;
        icmp.Code = 0;
        icmp.SequenceNumber = sequenceNumber;
        icmp.Identifier = identifier;
        if (updateChecksum)
            ipPacket.UpdateAllChecksums();

        return ipPacket;
    }

    public static IpPacket BuildIcmpV6EchoRequest(IPAddress sourceAddress, IPAddress destinationAddress,
        ReadOnlySpan<byte> payload, ushort identifier = 0, ushort sequenceNumber = 0, bool updateChecksum = true)
    {
        if (!sourceAddress.IsV6() || !destinationAddress.IsV6())
            throw new ArgumentException("SourceAddress and DestinationAddress must be IPv6 addresses.");

        // ICMP echo request
        var ipPacket = BuildIcmpV6(sourceAddress, destinationAddress, payload);
        var icmp = ipPacket.ExtractIcmpV6();
        icmp.Type = IcmpV6Type.EchoRequest;
        icmp.Code = 0;
        icmp.SequenceNumber = sequenceNumber;
        icmp.Identifier = identifier;
        if (updateChecksum)
            ipPacket.UpdateAllChecksums();

        return ipPacket;
    }

    public static IpPacket BuildIcmpEchoReply(
        IPAddress sourceAddress, IPAddress destinationAddress,
        ReadOnlySpan<byte> payload, ushort identifier = 0, ushort sequenceNumber = 0, bool updateChecksum = true)
    {
        if (sourceAddress.AddressFamily != destinationAddress.AddressFamily)
            throw new ArgumentException("SourceAddress and DestinationAddress must have a same ip version.");

        return sourceAddress.AddressFamily switch {
            AddressFamily.InterNetwork => BuildIcmpV4EchoReply(sourceAddress, destinationAddress, payload, identifier, sequenceNumber, updateChecksum),
            AddressFamily.InterNetworkV6 => BuildIcmpV6EchoReply(sourceAddress, destinationAddress, payload, identifier, sequenceNumber, updateChecksum),
            _ => throw new NotSupportedException($"{sourceAddress.AddressFamily} not supported.")
        };
    }

    public static IpPacket BuildIcmpV4EchoReply(
        IPAddress sourceAddress, IPAddress destinationAddress,
        ReadOnlySpan<byte> payload, ushort identifier = 0, ushort sequenceNumber = 0, bool updateChecksum = true)
    {
        if (!sourceAddress.IsV4() || !destinationAddress.IsV4())
            throw new ArgumentException("SourceAddress and DestinationAddress must be IPv4 addresses.");

        var ipPacket = BuildIcmpV4(sourceAddress, destinationAddress, payload);
        var icmp = ipPacket.ExtractIcmpV4();
        icmp.Type = IcmpV4Type.EchoReply;
        icmp.Code = 0;
        icmp.SequenceNumber = sequenceNumber;
        icmp.Identifier = identifier;
        if (updateChecksum)
            ipPacket.UpdateAllChecksums();

        return ipPacket;
    }

    public static IpPacket BuildIcmpV6EchoReply(
        IPAddress sourceAddress, IPAddress destinationAddress,
        ReadOnlySpan<byte> payload, ushort identifier = 0, ushort sequenceNumber = 0, bool updateChecksum = true)
    {
        if (!sourceAddress.IsV6() || !destinationAddress.IsV6())
            throw new ArgumentException("SourceAddress and DestinationAddress must be IPv6 addresses.");

        var ipPacket = BuildIcmpV6(sourceAddress, destinationAddress, payload);
        var icmp = ipPacket.ExtractIcmpV6();
        icmp.Type = IcmpV6Type.EchoReply;
        icmp.Code = 0;
        icmp.SequenceNumber = sequenceNumber;
        icmp.Identifier = identifier;
        if (updateChecksum)
            ipPacket.UpdateAllChecksums();

        return ipPacket;
    }

    public static IpPacket BuildTcpResetReply(IpPacket ipPacket, bool updateChecksum = true)
    {
        if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));
        if (ipPacket.Protocol != IpProtocol.Tcp)
            throw new ArgumentException("packet is not TCP!", nameof(ipPacket));

        var tcpPacketOrg = ipPacket.ExtractTcp();

        var resetIpPacket = BuildTcp(
            ipPacket.DestinationAddress, ipPacket.SourceAddress,
            tcpPacketOrg.DestinationPort, tcpPacketOrg.SourcePort, null, null);

        var resetTcpPacket = resetIpPacket.ExtractTcp();
        resetTcpPacket.Reset = true;
        resetTcpPacket.WindowSize = 0;

        if (tcpPacketOrg is { Synchronize: true, Acknowledgment: false }) {
            resetTcpPacket.Acknowledgment = true;
            resetTcpPacket.SequenceNumber = 0;
            resetTcpPacket.AcknowledgmentNumber = tcpPacketOrg.SequenceNumber + 1;
        }
        else {
            resetTcpPacket.Acknowledgment = false;
            resetTcpPacket.AcknowledgmentNumber = tcpPacketOrg.AcknowledgmentNumber;
            resetTcpPacket.SequenceNumber = tcpPacketOrg.AcknowledgmentNumber;
        }

        if (updateChecksum)
            resetIpPacket.UpdateAllChecksums();

        return resetIpPacket;
    }

    public static IpPacket BuildDns(IPAddress sourceAddress, IPAddress destinationAddress,
        int sourcePort, int destinationPort, string host, ushort? queryId = null)
    {
        queryId ??= (ushort)new Random().Next(ushort.MaxValue);
        var query = DnsResolver.BuildDnsQuery(queryId.Value, host);
        var dnsPacket = BuildUdp(
            sourceAddress: sourceAddress,
            destinationAddress: destinationAddress,
            sourcePort: sourcePort,
            destinationPort: destinationPort,
            query);

        return dnsPacket;
    }

    public static IpPacket BuildDns(IPEndPoint sourceEndPoint, IPEndPoint destinationEndPoint,
        string host, ushort? queryId = null)
    {
        return BuildDns(
            sourceAddress: sourceEndPoint.Address,
            destinationAddress: destinationEndPoint.Address,
            sourcePort: sourceEndPoint.Port,
            destinationPort: destinationEndPoint.Port,
            host,
            queryId);
    }

    public static IpPacket BuildIcmpUnreachableReply(IpPacket ipPacket, bool updateChecksum = true)
    {
        return ipPacket.Version == IpVersion.IPv6
            ? BuildIcmpV6Error(ipPacket, IcmpV6Type.DestinationUnreachable, IcmpV6Code.AddressUnreachable, 0, updateChecksum)
            : BuildIcmpV4Error(ipPacket, IcmpV4Type.DestinationUnreachable, IcmpV4Code.HostUnreachable, 0, updateChecksum);
    }

    public static IpPacket BuildIcmpUnreachablePortReply(IpPacket ipPacket, bool updateChecksum = true)
    {
        return ipPacket.Version == IpVersion.IPv6
            ? BuildIcmpV6Error(ipPacket, IcmpV6Type.DestinationUnreachable, IcmpV6Code.PortUnreachable, 0, updateChecksum)
            : BuildIcmpV4Error(ipPacket, IcmpV4Type.DestinationUnreachable, IcmpV4Code.PortUnreachable, 0, updateChecksum);
    }

    public static IpPacket BuildIcmpPacketTooBigReply(IpPacket ipPacket, int mtu, bool updateChecksum = true)
    {
        return ipPacket.Version == IpVersion.IPv6
            ? BuildIcmpV6Error(ipPacket, IcmpV6Type.PacketTooBig, IcmpV6Code.PacketTooBig,
                (ushort)mtu, updateChecksum)
            : BuildIcmpV4Error(ipPacket, IcmpV4Type.DestinationUnreachable, IcmpV4Code.FragmentationNeeded,
                (ushort)(mtu & 0xFFFF), updateChecksum);
    }

    public static IpPacket BuildIcmpV4Error(IpPacket ipPacket, IcmpV4Type icmpV4Type, IcmpV4Code code,
        uint messageSpecific, bool updateChecksum)
    {
        if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));

        // Copy original IP header (20 bytes) + 8 bytes of payload into ICMP payload
        var toCopy = Math.Min(ipPacket.Header.Length + 8, ipPacket.Buffer.Length);
        var icmpIpPacket = BuildIcmpV4(ipPacket.DestinationAddress, ipPacket.SourceAddress,
            ipPacket.Buffer.Span[..toCopy]);

        var icmpPacket = icmpIpPacket.ExtractIcmpV4();
        icmpPacket.Type = icmpV4Type;
        icmpPacket.Code = (byte)code;
        icmpPacket.MessageSpecific = messageSpecific;


        if (updateChecksum)
            icmpIpPacket.UpdateAllChecksums();

        return icmpIpPacket;
    }

    public static IpPacket BuildIcmpV6Error(IpPacket ipPacket, IcmpV6Type icmpV6Type, IcmpV6Code code, uint messageSpecific,
        bool updateChecksum = true)
    {
        if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));

        // Copy original IP header (40 bytes) + 8 bytes of payload into ICMP payload
        var toCopy = Math.Min(1280 - 40, ipPacket.Buffer.Length);
        var icmpIpPacket = BuildIcmpV6(ipPacket.DestinationAddress, ipPacket.SourceAddress,
            ipPacket.Buffer.Span[..toCopy]);

        var icmpPacket = icmpIpPacket.ExtractIcmpV6();
        icmpPacket.Type = icmpV6Type;
        icmpPacket.Code = (byte)code;
        icmpPacket.MessageSpecific = messageSpecific;

        if (updateChecksum)
            icmpIpPacket.UpdateAllChecksums();

        return icmpIpPacket;
    }
}