using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using PacketDotNet.Utils;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;
using ProtocolType = PacketDotNet.ProtocolType;

// ReSharper disable UnusedMember.Global
namespace VpnHood.Core.Tunneling.Utils;

public static class PacketUtil
{
    public static void UpdateIpChecksum(IPPacket ipPacket)
    {
        // ICMPv6 checksum needs to be updated if IPv6 packet changes
        if (ipPacket.Protocol == ProtocolType.IcmpV6)
            UpdateIpPacket(ipPacket);

        // ipv4 packet checksum needs to be updated if IPv4 packet changes
        if (ipPacket is IPv4Packet ipV4Packet) {
            ipV4Packet.UpdateIPChecksum();
            ipPacket.UpdateCalculatedValues();
        }
    }

    public static void UpdateIpPacket(IPPacket ipPacket, bool throwIfNotSupported = true)
    {
        if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));

        if (ipPacket.Protocol == ProtocolType.Tcp) {
            var tcpPacket = ExtractTcp(ipPacket);
            tcpPacket.UpdateTcpChecksum();
        }
        else if (ipPacket.Protocol == ProtocolType.Udp) {
            var udpPacket = ExtractUdp(ipPacket);
            udpPacket.UpdateUdpChecksum();
        }
        else if (ipPacket.Protocol == ProtocolType.Icmp) {
            var icmpPacket = ExtractIcmp(ipPacket);
            icmpPacket.UpdateIcmpChecksum();
        }
        else if (ipPacket.Protocol == ProtocolType.IcmpV6) {
            var icmpV6Packet = ExtractIcmpV6(ipPacket);
            icmpV6Packet.UpdateIcmpChecksum();
        }
        else {
            if (throwIfNotSupported)
                throw new NotSupportedException("Does not support this packet!");
        }

        // update IP
        if (ipPacket is IPv4Packet ipV4Packet) {
            ipV4Packet.UpdateIPChecksum();
        }
        else if (ipPacket is IPv6Packet) {
            // do nothing
        }
        else {
            if (throwIfNotSupported)
                throw new NotSupportedException("Does not support this packet!");
        }

        ipPacket.UpdateCalculatedValues();
    }

    public static IPPacket ClonePacket(IPPacket ipPacket)
    {
        return Packet.ParsePacket(LinkLayers.Raw, ipPacket.Bytes).Extract<IPPacket>();
    }

    public static IPPacket CreateTcpResetReply(IPPacket ipPacket, bool updatePacket = false)
    {
        if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));
        if (ipPacket.Protocol != ProtocolType.Tcp)
            throw new ArgumentException("packet is not TCP!", nameof(ipPacket));

        var tcpPacketOrg = ExtractTcp(ipPacket);
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

        var resetIpPacket = CreateIpPacket(ipPacket.DestinationAddress, ipPacket.SourceAddress);
        resetIpPacket.Protocol = ProtocolType.Tcp;
        resetIpPacket.PayloadPacket = resetTcpPacket;

        if (updatePacket)
            UpdateIpPacket(resetIpPacket);
        return resetIpPacket;
    }

    public static IPPacket CreateIpPacket(IPAddress sourceAddress, IPAddress destinationAddress)
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

    public static IPPacket CreateDnsPacket(IPEndPoint sourceEndPoint, IPEndPoint destinationEndPoint, string host, ushort? queryId = null)
    {
        queryId ??= (ushort)new Random().Next(ushort.MaxValue);
        var query = DnsResolver.BuildDnsQuery(queryId.Value, host);
        var dnsPacket = CreateUdpPacket(sourceEndPoint, destinationEndPoint, query);
        return dnsPacket;
    }

    public static IPPacket CreateUdpPacket(IPEndPoint sourceEndPoint, IPEndPoint destinationEndPoint,
        byte[] payloadData, bool calculateCheckSum = true)
    {
        // create packet for audience
        var ipPacket = CreateIpPacket(sourceEndPoint.Address, destinationEndPoint.Address);
        var udpPacket = new UdpPacket((ushort)sourceEndPoint.Port, (ushort)destinationEndPoint.Port) {
            PayloadData = payloadData
        };

        ipPacket.PayloadPacket = udpPacket;
        if (calculateCheckSum)
            UpdateIpPacket(ipPacket);
        return ipPacket;
    }

    public static IPPacket CreateIcmpEchoRequestV4(IPAddress sourceAddress, IPAddress destinationAddress,
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


    public static IcmpV4Packet ExtractIcmp(IPPacket ipPacket)
    {
        return ipPacket.Extract<IcmpV4Packet>() ??
               throw new InvalidDataException($"Invalid IcmpV4 packet. It is: {ipPacket.Protocol}");
    }

    public static IcmpV6Packet ExtractIcmpV6(IPPacket ipPacket)
    {
        return ipPacket.Extract<IcmpV6Packet>() ??
               throw new InvalidDataException($"Invalid IcmpV6 packet. It is: {ipPacket.Protocol}");
    }

    public static UdpPacket ExtractUdp(IPPacket ipPacket)
    {
        return ipPacket.Extract<UdpPacket>() ??
               throw new InvalidDataException($"Invalid UDP packet. It is: {ipPacket.Protocol}");
    }

    public static TcpPacket ExtractTcp(IPPacket ipPacket)
    {
        return ipPacket.Extract<TcpPacket>() ??
               throw new InvalidDataException($"Invalid TCP packet. It is: {ipPacket.Protocol}");
    }

    public static IPPacket CreateUnreachableReply(IPPacket ipPacket)
    {
        if (ipPacket.Version == IPVersion.IPv6)
            return CreateUnreachableReplyV6(ipPacket, IcmpV6Type.DestinationUnreachable, 0, 0);
        return CreateUnreachableReplyV4(ipPacket, IcmpV4TypeCode.UnreachableHost, 0);
    }

    public static IPPacket CreateUnreachablePortReply(IPPacket ipPacket)
    {
        if (ipPacket.Version == IPVersion.IPv6)
            return CreateUnreachableReplyV6(ipPacket, IcmpV6Type.DestinationUnreachable, 4, 0);
        return CreateUnreachableReplyV4(ipPacket, IcmpV4TypeCode.UnreachablePort, 0);
    }

    public static IPPacket CreatePacketTooBigReply(IPPacket ipPacket, ushort mtu)
    {
        if (ipPacket.Version == IPVersion.IPv6)
            return CreateUnreachableReplyV6(ipPacket, IcmpV6Type.PacketTooBig, 0, mtu);
        return CreateUnreachableReplyV4(ipPacket, IcmpV4TypeCode.UnreachableFragmentationNeeded, mtu);
    }

    private static IPPacket CreateUnreachableReplyV6(IPPacket ipPacket, IcmpV6Type icmpV6Type, byte code, int reserved)
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
        UpdateIpPacket(newIpPacket);

        //var restorePacketV6 = Packet.ParsePacket(LinkLayers.Raw, newIpPacket.Bytes).Extract<IPPacket>();
        //var restoreIcmpV6 = restorePacketV6.Extract<IcmpV6Packet>();
        return newIpPacket;
    }

    private static IPPacket CreateUnreachableReplyV4(IPPacket ipPacket, IcmpV4TypeCode typeCode, ushort sequence)
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

        UpdateIpPacket(newIpPacket);
        return newIpPacket;
    }

    public static ushort ReadPacketLength(byte[] buffer, int bufferIndex)
    {
        var version = buffer[bufferIndex] >> 4;

        // v4
        if (version == 4) {
            var packetLength = (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(buffer, bufferIndex + 2));
            if (packetLength < 20)
                throw new Exception($"A packet with invalid length has been received! Length: {packetLength}");
            return packetLength;
        }

        // v6
        if (version == 6) {
            var payload = (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(buffer, bufferIndex + 4));
            return (ushort)(40 + payload); //header + payload
        }

        // unknown
        throw new Exception("Unknown packet version!");
    }

    public static IPPacket ReadNextPacket(byte[] buffer, ref int bufferIndex)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));

        var packetLength = ReadPacketLength(buffer, bufferIndex);
        var packet = Packet.ParsePacket(LinkLayers.Raw, buffer[bufferIndex..(bufferIndex + packetLength)])
            .Extract<IPPacket>();
        bufferIndex += packetLength;
        return packet;
    }

    public static void LogPackets(IList<IPPacket> ipPackets, string operation)
    {
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var i = 0; i < ipPackets.Count; i++) {
            var ipPacket = ipPackets[i];
            LogPacket(ipPacket, operation);
        }
    }

    public static void LogPacket(IPPacket ipPacket, string message, LogLevel logLevel = LogLevel.Trace,
        Exception? exception = null)
    {
        try {
            if (!VhLogger.IsDiagnoseMode) return;
            var eventId = GeneralEventId.Packet;
            var packetPayload = Array.Empty<byte>();

            switch (ipPacket.Protocol) {
                case ProtocolType.Icmp: {
                        eventId = GeneralEventId.Ping;
                        var icmpPacket = ExtractIcmp(ipPacket);
                        packetPayload = icmpPacket.PayloadData ?? [];
                        break;
                    }

                case ProtocolType.IcmpV6: {
                        eventId = GeneralEventId.Ping;
                        var icmpPacket = ExtractIcmpV6(ipPacket);
                        packetPayload = icmpPacket.PayloadData ?? [];
                        break;
                    }

                case ProtocolType.Udp: {
                        eventId = GeneralEventId.Udp;
                        var udpPacket = ExtractUdp(ipPacket);
                        packetPayload = udpPacket.PayloadData ?? [];
                        break;
                    }

                case ProtocolType.Tcp: {
                        eventId = GeneralEventId.Tcp;
                        var tcpPacket = ExtractTcp(ipPacket);
                        packetPayload = tcpPacket.PayloadData ?? [];
                        break;
                    }
            }

            VhLogger.Instance.Log(logLevel, eventId, exception,
                message + " Packet: {Packet}, PayloadLength: {PayloadLength}, Payload: {Payload}",
                Format(ipPacket), packetPayload.Length,
                BitConverter.ToString(packetPayload, 0, Math.Min(10, packetPayload.Length)));
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(GeneralEventId.Packet,
                ex, "Could not extract packet for log. Packet: {Packet}, Message: {Message}, Exception: {Exception}",
                Format(ipPacket), message, exception);
        }
    }

    public static string Format(IPPacket ipPacket)
    {
        return VhLogger.FormatIpPacket(ipPacket.ToString()!);
    }

    public static IPEndPointPair GetPacketEndPoints(IPPacket ipPacket)
    {
        if (ipPacket.Protocol == ProtocolType.Tcp) {
            var tcpPacket = ExtractTcp(ipPacket);
            return new IPEndPointPair(
                new IPEndPoint(ipPacket.SourceAddress, tcpPacket.SourcePort),
                new IPEndPoint(ipPacket.DestinationAddress, tcpPacket.DestinationPort));
        }

        if (ipPacket.Protocol == ProtocolType.Udp) {
            var udpPacket = ExtractUdp(ipPacket);
            return new IPEndPointPair(
                new IPEndPoint(ipPacket.SourceAddress, udpPacket.SourcePort),
                new IPEndPoint(ipPacket.DestinationAddress, udpPacket.DestinationPort));
        }

        return new IPEndPointPair(
            new IPEndPoint(ipPacket.SourceAddress, 0),
            new IPEndPoint(ipPacket.DestinationAddress, 0));
    }

    public static bool IsQuicPort(UdpPacket udpPacket)
    {
        // Check if the packet is targeting port 443 (common for QUIC)
        return udpPacket.DestinationPort is 80 or 443;
    }
}