using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using PacketDotNet.Utils;
using VpnHood.Common.Logging;

namespace VpnHood.Tunneling
{
    public static class PacketUtil
    {
        public static void UpdateIpPacket(IPPacket ipPacket, bool throwIfNotSupported = true)
        {
            if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));

            if (ipPacket.Protocol == ProtocolType.Tcp)
            {
                var tcpPacket = ExtractTcp(ipPacket);
                tcpPacket.UpdateTcpChecksum();
                tcpPacket.UpdateCalculatedValues();
            }
            else if (ipPacket.Protocol == ProtocolType.Udp)
            {
                var udpPacket = ExtractUdp(ipPacket);
                udpPacket.UpdateUdpChecksum();
                udpPacket.UpdateCalculatedValues();
            }
            else if (ipPacket.Protocol == ProtocolType.Icmp)
            {
                var icmpPacket = ExtractIcmp(ipPacket);
                UpdateIcmpChecksum(icmpPacket);
                icmpPacket.UpdateCalculatedValues();
            }
            else
            {
                if (throwIfNotSupported)
                    throw new NotSupportedException("Does not support this packet!");
            }

            // update IP
            if (ipPacket is IPv4Packet ipV4Packet)
            {
                ipV4Packet.UpdateIPChecksum();
                ipPacket.UpdateCalculatedValues();
            }
            else
            {
                if (throwIfNotSupported)
                    throw new NotSupportedException("Does not support this packet!");
            }
        }

        public static IPPacket CreateTcpResetReply(IPPacket ipPacket, bool updatePacket = false)
        {
            if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));
            if (ipPacket.Protocol != ProtocolType.Tcp)
                throw new ArgumentException("packet is not TCP!", nameof(ipPacket));

            var tcpPacketOrg = ExtractTcp(ipPacket);
            TcpPacket resetTcpPacket = new(tcpPacketOrg.DestinationPort, tcpPacketOrg.SourcePort)
            {
                Reset = true
            };

            if (tcpPacketOrg.Synchronize && !tcpPacketOrg.Acknowledgment)
            {
                resetTcpPacket.Acknowledgment = true;
                resetTcpPacket.SequenceNumber = 0;
                resetTcpPacket.AcknowledgmentNumber = tcpPacketOrg.SequenceNumber + 1;
            }
            else
            {
                resetTcpPacket.Acknowledgment = false;
                resetTcpPacket.AcknowledgmentNumber = tcpPacketOrg.AcknowledgmentNumber;
                resetTcpPacket.SequenceNumber = tcpPacketOrg.AcknowledgmentNumber;
            }


            IPv4Packet resetIpPacket = new(ipPacket.DestinationAddress, ipPacket.SourceAddress)
            {
                Protocol = ProtocolType.Tcp,
                PayloadPacket = resetTcpPacket
            };

            if (updatePacket)
                UpdateIpPacket(resetIpPacket);
            return resetIpPacket;
        }

        public static IcmpV4Packet ExtractIcmp(IPPacket ipPacket)
        {
            return ipPacket.Extract<IcmpV4Packet>() ??
                   throw new InvalidDataException($"Invalid {ipPacket.Protocol} packet!");
        }

        public static UdpPacket ExtractUdp(IPPacket ipPacket)
        {
            return ipPacket.Extract<UdpPacket>() ??
                   throw new InvalidDataException($"Invalid {ipPacket.Protocol} packet!");
        }

        public static TcpPacket ExtractTcp(IPPacket ipPacket)
        {
            return ipPacket.Extract<TcpPacket>() ??
                   throw new InvalidDataException($"Invalid {ipPacket.Protocol} packet!");
        }

        public static IPPacket CreateUnreachableReply(IPPacket ipPacket, IcmpV4TypeCode typeCode, ushort sequence = 0)
        {
            if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));

            // packet is too big
            var icmpDataLen = Math.Min(ipPacket.TotalLength, 20 + 8);
            var byteArraySegment = new ByteArraySegment(new byte[16 + icmpDataLen]);
            var icmpV4Packet = new IcmpV4Packet(byteArraySegment, ipPacket)
            {
                TypeCode = typeCode,
                PayloadData = ipPacket.Bytes[..icmpDataLen],
                Sequence = sequence
            };
            icmpV4Packet.Checksum =
                (ushort) ChecksumUtils.OnesComplementSum(icmpV4Packet.Bytes, 0, icmpV4Packet.Bytes.Length);
            icmpV4Packet.UpdateCalculatedValues();

            var newIpPacket = new IPv4Packet(ipPacket.DestinationAddress, ipPacket.SourceAddress)
            {
                PayloadPacket = icmpV4Packet,
                FragmentFlags = 0
            };
            newIpPacket.UpdateIPChecksum();
            newIpPacket.UpdateCalculatedValues();
            return newIpPacket;
        }

        public static void UpdateIcmpChecksum(IcmpV4Packet icmpPacket)
        {
            if (icmpPacket is null) throw new ArgumentNullException(nameof(icmpPacket));

            icmpPacket.Checksum = 0;
            var buf = icmpPacket.Bytes;
            icmpPacket.Checksum =
                buf != null ? (ushort) ChecksumUtils.OnesComplementSum(buf, 0, buf.Length) : (ushort) 0;
        }

        public static IPPacket ReadNextPacket(byte[] buffer, ref int bufferIndex)
        {
            if (buffer is null) throw new ArgumentNullException(nameof(buffer));

            var packetLength = IPAddress.NetworkToHostOrder(BitConverter.ToInt16(buffer, bufferIndex + 2));
            if (packetLength < IPv4Packet.HeaderMinimumLength)
                throw new Exception($"A packet with invalid length has been received! Length: {packetLength}");

            var segment = new ByteArraySegment(buffer, bufferIndex, packetLength);
            bufferIndex += packetLength;
            return new IPv4Packet(segment);
        }

        public static void LogPackets(IEnumerable<IPPacket> ipPackets, string operation)
        {
            foreach (var ipPacket in ipPackets)
                LogPacket(ipPacket, operation);
        }

        public static void LogPacket(IPPacket ipPacket, string operation)
        {
            if (!VhLogger.IsDiagnoseMode) return;

            // log ICMP
            if (ipPacket.Protocol == ProtocolType.Icmp)
            {
                var icmpPacket = ipPacket.Extract<IcmpV4Packet>();
                if (icmpPacket != null)
                {
                    var payload = icmpPacket.PayloadData ?? Array.Empty<byte>();
                    VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Ping,
                        $"{ipPacket.Protocol} has been {operation}. DestAddress: {ipPacket.DestinationAddress}, DataLen: {payload.Length}, Data: {BitConverter.ToString(payload, 0, Math.Min(10, payload.Length))}.");
                }
                else
                {
                    VhLogger.Instance.Log(LogLevel.Warning, GeneralEventId.Ping,
                        $"Invalid {ipPacket.Protocol} packet has been {operation}! DestAddress: {ipPacket.DestinationAddress}, PacketLength: {ipPacket.TotalLength}.");
                }
            }

            // log Udp
            if (ipPacket.Protocol == ProtocolType.Udp)
            {
                var udpPacket = ipPacket.Extract<UdpPacket>();
                if (udpPacket != null)
                {
                    var payload = udpPacket.PayloadData ?? Array.Empty<byte>();
                    VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Udp,
                        $"{ipPacket.Protocol} has been {operation}. DestAddress: {ipPacket.DestinationAddress}:{udpPacket.DestinationPort}, DataLen: {payload.Length}, Data: {BitConverter.ToString(payload, 0, Math.Min(10, payload.Length))}.");
                }
                else
                {
                    VhLogger.Instance.Log(LogLevel.Warning, GeneralEventId.Udp,
                        $"Invalid {ipPacket.Protocol} has been {operation}! DestAddress: {ipPacket.DestinationAddress}, PacketLength: {ipPacket.TotalLength}.");
                }
            }
        }
    }
}