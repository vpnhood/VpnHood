using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using PacketDotNet.Utils;
using VpnHood.Common.Logging;
using ProtocolType = PacketDotNet.ProtocolType;

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
            else if (ipPacket is IPv6Packet)
            {
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
                throw new InvalidOperationException($"{nameof(sourceAddress)} and {nameof(destinationAddress)}  address family must be same!");

            return sourceAddress.AddressFamily switch
            {
                AddressFamily.InterNetwork => new IPv4Packet(sourceAddress, destinationAddress),
                AddressFamily.InterNetworkV6 => new IPv6Packet(sourceAddress, destinationAddress),
                _ => throw new NotSupportedException($"{sourceAddress.AddressFamily} is not supported!")
            };
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

        public static IPPacket CreateUnreachableReplyV6(IPPacket ipPacket, IcmpV4TypeCode typeCode, ushort mtu = 0)
        {
            if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));

            var icmpDataLen = Math.Min(ipPacket.TotalLength, 20 + 8);
            var byteArraySegment = new ByteArraySegment(new byte[16 + icmpDataLen]);
            var icmpPacket = new IcmpV6Packet(byteArraySegment, ipPacket)
            {
                Type = IcmpV6Type.PacketTooBig,
              
                Code = 0
            };

            icmpPacket.UpdateCalculatedValues();

            var newIpPacket = new IPv6Packet(ipPacket.DestinationAddress, ipPacket.SourceAddress)
            {
                PayloadPacket = icmpPacket
            };
            UpdateIpPacket(newIpPacket);
            throw new NotImplementedException("Not implemented!");
        }

        public static IPPacket CreateUnreachableReply(IPPacket ipPacket, IcmpV4TypeCode typeCode, ushort sequence = 0)
        {
            if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));

            // packet is too big
            var icmpDataLen = Math.Min(ipPacket.TotalLength, 20 + 8);
            var byteArraySegment = new ByteArraySegment(new byte[16 + icmpDataLen]);
            var icmpPacket = new IcmpV4Packet(byteArraySegment, ipPacket)
            {
                TypeCode = typeCode,
                PayloadData = ipPacket.Bytes[..icmpDataLen],
                Sequence = sequence
            };
            icmpPacket.Checksum = (ushort)ChecksumUtils.OnesComplementSum(icmpPacket.Bytes, 0, icmpPacket.Bytes.Length);
            icmpPacket.UpdateCalculatedValues();

            var newIpPacket = new IPv4Packet(ipPacket.DestinationAddress, ipPacket.SourceAddress)
            {
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
            icmpPacket.Checksum =
                buf != null ? (ushort)ChecksumUtils.OnesComplementSum(buf, 0, buf.Length) : (ushort)0;
        }

        public static ushort ReadPacketLength(byte[] buffer, int bufferIndex)
        {
            var version = buffer[bufferIndex] >> 4;

            // v4
            if (version == 4)
            {
                var ret = (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(buffer, bufferIndex + 2));
                if (ret < 20)
                    throw new Exception($"A packet with invalid length has been received! Length: {ret}");
                return ret;
            }

            // v6
            if (version == 6)
                return 40;

            // unknown
            throw new Exception("Unknown packet version!");
        }

        public static IPPacket ReadNextPacket(byte[] buffer, ref int bufferIndex)
        {
            if (buffer is null) throw new ArgumentNullException(nameof(buffer));

            var packetLength = ReadPacketLength(buffer, bufferIndex);
            var packet = Packet.ParsePacket(LinkLayers.Raw, buffer[bufferIndex..(bufferIndex + packetLength)]).Extract<IPPacket>();
            bufferIndex += packetLength;
            return packet;
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