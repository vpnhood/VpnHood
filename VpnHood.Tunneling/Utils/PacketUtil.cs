﻿using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using PacketDotNet.Utils;
using VpnHood.Common.Logging;
using VpnHood.Common.Net;
using ProtocolType = PacketDotNet.ProtocolType;

// ReSharper disable UnusedMember.Global
namespace VpnHood.Tunneling.Utils;

public static class PacketUtil
{
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
            UpdateIcmpChecksum(icmpPacket);
        }
        else if (ipPacket.Protocol == ProtocolType.IcmpV6) {
            // nothing need to do due to packet.UpdateCalculatedValues
        }
        else {
            if (throwIfNotSupported)
                throw new NotSupportedException("Does not support this packet!");
        }

        // update IP
        if (ipPacket is IPv4Packet ipV4Packet) {
            ipV4Packet.UpdateIPChecksum();
            ipPacket.UpdateCalculatedValues();
        }
        else if (ipPacket is IPv6Packet) {
            ipPacket.UpdateCalculatedValues();
        }
        else {
            if (throwIfNotSupported)
                throw new NotSupportedException("Does not support this packet!");
        }
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

    public static void UpdateIcmpChecksum(IcmpV4Packet icmpPacket)
    {
        if (icmpPacket is null) throw new ArgumentNullException(nameof(icmpPacket));

        icmpPacket.Checksum = 0;
        var buf = icmpPacket.Bytes;
        icmpPacket.Checksum = buf != null
            ? (ushort)ChecksumUtils.OnesComplementSum(buf, 0, buf.Length)
            : (ushort)0;
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

    public static void LogPacket(IPPacket ipPacket, string message, LogLevel logLevel = LogLevel.Information,
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

    public static IPPacket CreateIcmpV6NeighborAdvertisement2(IPPacket ipPacket)
    {
        var buffer = new byte[24];
        buffer[4] = 0xE0;
        var target = ipPacket.DestinationAddress.GetAddressBytes();
        Array.Copy(target, 0, buffer, 8, 16);
        var icmpPacket = new IcmpV6Packet(new ByteArraySegment(buffer)) {
            Type = IcmpV6Type.NeighborAdvertisement,
            Code = 0
        };

        var newIpPacket = CreateIpPacket(IPAddress.Parse("fd00::1001"), ipPacket.SourceAddress);
        newIpPacket.PayloadPacket = icmpPacket;
        UpdateIpPacket(newIpPacket);

        return newIpPacket;
    }

    public static IPPacket CreateIcmpV6NeighborAdvertisement(IPPacket ipPacket)
    {
        var buffer = new byte[24];
        buffer[4] = 0xE0;

        //buffer[24] = 2; // option type 2
        //buffer[25] = 1; // option length
        //buffer[26] = 0x02;
        //buffer[27] = 0x1c;
        //buffer[28] = 0x93;
        //buffer[29] = 0xa9;
        //buffer[30] = 0xc8;
        //buffer[31] = 0x64;

        var target = ipPacket.SourceAddress.GetAddressBytes();
        Array.Copy(target, 0, buffer, 8, 16);
        var icmpPacket = new IcmpV6Packet(new ByteArraySegment(buffer)) {
            Type = IcmpV6Type.NeighborAdvertisement,
            Code = 0
        };

        var newIpPacket = CreateIpPacket(ipPacket.DestinationAddress, ipPacket.SourceAddress);
        newIpPacket.PayloadPacket = icmpPacket;
        UpdateIpPacket(newIpPacket);

        return newIpPacket;
    }

    public static IPPacket CreateIcmpV6RouterAdvertisement(IPPacket ipPacket)
    {
        var buffer = new byte[16];
        buffer[4] = 0xFF; // Cur Hop Limit
        buffer[6] = 0x00; // Router Lifetime 0. 0 is unspecified
        buffer[7] = 0x00; // Router Lifetime 0

        var icmpPacket = new IcmpV6Packet(new ByteArraySegment(buffer)) {
            Type = IcmpV6Type.RouterAdvertisement,
            Code = 0
        };

        var router = IPAddress.Parse("fd00::1010");
        var newIpPacket = CreateIpPacket(router, ipPacket.SourceAddress);
        newIpPacket.PayloadPacket = icmpPacket;
        UpdateIpPacket(newIpPacket);

        return newIpPacket;
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

    public static bool IsQuicHandshake(UdpPacket udpPacket)
    {
        var payload = udpPacket.PayloadData;
        // Ensure the packet has enough data to analyze for QUIC
        if (payload.Length < 6)
            return false;

        // Check the first byte to verify it’s a long header (0xC0 or above)
        var firstByte = payload[0];
        if ((firstByte & 0xC0) != 0xC0)
            return false;

        // Check if it's an Initial packet (0b1100xxx)
        if ((firstByte & 0x30) != 0x00)
            return false;

        // Optionally check Token Length and Length fields in Initial packets for further verification
        const int tokenLengthIndex = 6; // Assuming fixed-size header, adjust as needed based on your data
        int tokenLength = payload[tokenLengthIndex];

        // Extract the length field for more validation
        var lengthFieldIndex = tokenLengthIndex + tokenLength + 1;
        if (lengthFieldIndex >= payload.Length)
            return false;

        // Check QUIC version (bytes 1-4)
        var version = BitConverter.ToInt32(payload, 1);
        
        // Optional: further checks or more version conditions
        return version == 0x00000001; // Most common initial version
    }


    public static bool IsQuicPort(UdpPacket udpPacket)
    {
        // Check if the packet is targeting port 443 (common for QUIC)
        return udpPacket.DestinationPort is 80 or 443;
    }

}