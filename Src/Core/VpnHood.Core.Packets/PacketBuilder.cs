using System.Net;
using System.Net.Sockets;
using PacketDotNet;
using PacketDotNet.Utils;
using VpnHood.Core.Toolkit.Utils;
using ProtocolType = PacketDotNet.ProtocolType;

namespace VpnHood.Core.Packets;

public static class PacketBuilder
{
    public static IPPacket BuildTcpResetReply(IPPacket ipPacket, bool updateChecksum = false)
    {
        if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));
        if (ipPacket.Protocol != ProtocolType.Tcp)
            throw new ArgumentException("packet is not TCP!", nameof(ipPacket));

        var tcpPacketOrg = ipPacket.ExtractTcp();
        TcpPacket resetTcpPacket = new(tcpPacketOrg.DestinationPort, tcpPacketOrg.SourcePort) {
            Reset = true,
            WindowSize = 0
        };

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

        var resetIpPacket = BuildIpPacket(ipPacket.DestinationAddress, ipPacket.SourceAddress);
        resetIpPacket.Protocol = ProtocolType.Tcp;
        resetIpPacket.PayloadPacket = resetTcpPacket;

        if (updateChecksum)
            resetIpPacket.UpdateAllChecksums();
        return resetIpPacket;
    }

    public static IPPacket BuildIpPacket(IPAddress sourceAddress, IPAddress destinationAddress)
    {
        if (sourceAddress.AddressFamily != destinationAddress.AddressFamily)
            throw new InvalidOperationException(
                $"{nameof(sourceAddress)} and {nameof(destinationAddress)}  address family must be same!");

        return sourceAddress.AddressFamily switch {
            AddressFamily.InterNetwork => new IPv4Packet(sourceAddress, destinationAddress),
            AddressFamily.InterNetworkV6 => new IPv6Packet(sourceAddress, destinationAddress),
            _ => throw new NotSupportedException($"{sourceAddress.AddressFamily} is not supported!")
        };
    }

    public static IPPacket BuildDnsPacket(IPEndPoint sourceEndPoint, IPEndPoint destinationEndPoint, string host, ushort? queryId = null)
    {
        queryId ??= (ushort)new Random().Next(ushort.MaxValue);
        var query = DnsResolver.BuildDnsQuery(queryId.Value, host);
        var dnsPacket = BuildUdpPacket(sourceEndPoint, destinationEndPoint, query);
        return dnsPacket;
    }

    public static IPPacket BuildUdpPacket(IPEndPoint sourceEndPoint, IPEndPoint destinationEndPoint,
        byte[] payloadData, bool calculateCheckSum = true)
    {
        // create packet for audience
        var ipPacket = BuildIpPacket(sourceEndPoint.Address, destinationEndPoint.Address);
        var udpPacket = new UdpPacket((ushort)sourceEndPoint.Port, (ushort)destinationEndPoint.Port) {
            PayloadData = payloadData
        };

        ipPacket.PayloadPacket = udpPacket;
        if (calculateCheckSum)
            ipPacket.UpdateAllChecksums();
        return ipPacket;
    }

    public static IPPacket BuildIcmpEchoRequestV4(IPAddress sourceAddress, IPAddress destinationAddress,
        byte[] payloadData, ushort id = 0, ushort sequence = 0, bool calculateCheckSum = true)
    {
        // ICMP echo request
        var buffer = new byte[8 + payloadData.Length];
        var icmpPacket = new IcmpV4Packet(new ByteArraySegment(buffer)) {
            TypeCode = IcmpV4TypeCode.EchoRequest,
            Data = payloadData,
            Sequence = sequence,
            Id = id
        };

        // IP packet
        var ipPacket = new IPv4Packet(sourceAddress, destinationAddress) {
            Protocol = ProtocolType.Icmp,
            PayloadPacket = icmpPacket
        };

        // Update checksums
        if (calculateCheckSum) {
            icmpPacket.UpdateIcmpChecksum();
            ipPacket.UpdateIPChecksum();
            ipPacket.UpdateCalculatedValues();
        }

        return ipPacket;
    }

    public static IPPacket BuildUnreachableReply(IPPacket ipPacket)
    {
        if (ipPacket.Version == IPVersion.IPv6)
            return BuildUnreachableReplyV6(ipPacket, IcmpV6Type.DestinationUnreachable, 0, 0);
        return BuildUnreachableReplyV4(ipPacket, IcmpV4TypeCode.UnreachableHost, 0);
    }

    public static IPPacket BuildUnreachablePortReply(IPPacket ipPacket)
    {
        if (ipPacket.Version == IPVersion.IPv6)
            return BuildUnreachableReplyV6(ipPacket, IcmpV6Type.DestinationUnreachable, 4, 0);
        return BuildUnreachableReplyV4(ipPacket, IcmpV4TypeCode.UnreachablePort, 0);
    }

    public static IPPacket BuildPacketTooBigReply(IPPacket ipPacket, ushort mtu)
    {
        if (ipPacket.Version == IPVersion.IPv6)
            return BuildUnreachableReplyV6(ipPacket, IcmpV6Type.PacketTooBig, 0, mtu);
        return BuildUnreachableReplyV4(ipPacket, IcmpV4TypeCode.UnreachableFragmentationNeeded, mtu);
    }

    private static IPPacket BuildUnreachableReplyV6(IPPacket ipPacket, IcmpV6Type icmpV6Type, byte code, int reserved)
    {
        if (ipPacket is null)
            throw new ArgumentNullException(nameof(ipPacket));

        const int headerSize = 8;
        var icmpDataLen = Math.Min(ipPacket.TotalLength, 1300);
        var buffer = new byte[headerSize + icmpDataLen];
        Array.Copy(BitConverter.GetBytes(reserved), 0, buffer, 4, 4);
        Array.Copy(ipPacket.Bytes, 0, buffer, headerSize, icmpDataLen);

        // PacketDotNet doesn't support IcmpV6Packet properly
        var icmpPacket = new IcmpV6Packet(new ByteArraySegment(new byte[headerSize])) {
            Type = icmpV6Type,
            Code = code,
            PayloadData = buffer[headerSize..]
        };

        var newIpPacket = new IPv6Packet(ipPacket.DestinationAddress, ipPacket.SourceAddress) {
            PayloadPacket = icmpPacket,
            PayloadLength = (ushort)icmpPacket.TotalPacketLength
        };
        newIpPacket.UpdateAllChecksums();

        //var restorePacketV6 = Packet.ParsePacket(LinkLayers.Raw, newIpPacket.Bytes).Extract<IPPacket>();
        //var restoreIcmpV6 = restorePacketV6.Extract<IcmpV6Packet>();
        return newIpPacket;
    }

    private static IPPacket BuildUnreachableReplyV4(IPPacket ipPacket, IcmpV4TypeCode typeCode, ushort sequence)
    {
        if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));

        // packet is too big
        const int headerSize = 8;
        var icmpDataLen = Math.Min(ipPacket.TotalLength, 20 + 8);
        var buffer = new byte[headerSize + icmpDataLen];
        Array.Copy(ipPacket.Bytes, 0, buffer, headerSize, icmpDataLen);
        var icmpPacket = new IcmpV4Packet(new ByteArraySegment(buffer)) {
            TypeCode = typeCode,
            Sequence = sequence
        };
        icmpPacket.Checksum = (ushort)ChecksumUtils.OnesComplementSum(icmpPacket.Bytes, 0, icmpPacket.Bytes.Length);

        var newIpPacket = new IPv4Packet(ipPacket.DestinationAddress, ipPacket.SourceAddress) {
            PayloadPacket = icmpPacket,
            FragmentFlags = 0
        };

        newIpPacket.UpdateAllChecksums();
        return newIpPacket;
    }
}