﻿using System;
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
            }
            else if (ipPacket.Protocol == ProtocolType.Udp)
            {
                var udpPacket = ExtractUdp(ipPacket);
                udpPacket.UpdateUdpChecksum();
            }
            else if (ipPacket.Protocol == ProtocolType.Icmp)
            {
                var icmpPacket = ExtractIcmp(ipPacket);
                UpdateIcmpChecksum(icmpPacket);
            }
            else if (ipPacket.Protocol == ProtocolType.IcmpV6)
            {
                // nothing need to do due to packet.UpdateCalculatedValues
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
                   throw new InvalidDataException($"Invalid IcmpV4 packet! It is: {ipPacket.Protocol}");
        }

        public static IcmpV6Packet ExtractIcmpV6(IPPacket ipPacket)
        {
            return ipPacket.Extract<IcmpV6Packet>() ??
                   throw new InvalidDataException($"Invalid IcmpV6 packet! It is: {ipPacket.Protocol}");
        }

        public static UdpPacket ExtractUdp(IPPacket ipPacket)
        {
            return ipPacket.Extract<UdpPacket>() ??
                   throw new InvalidDataException($"Invalid UDP packet! It is: {ipPacket.Protocol}");
        }

        public static TcpPacket ExtractTcp(IPPacket ipPacket)
        {
            return ipPacket.Extract<TcpPacket>() ??
                   throw new InvalidDataException($"Invalid TCP packet! It is: {ipPacket.Protocol}");
        }

        public static IPPacket CreateUnreachableReply(IPPacket ipPacket)
        {
            if (ipPacket.Version == IPVersion.IPv6)
                return CreateUnreachableReplyV6(ipPacket, IcmpV6Type.DestinationUnreachable, 0, 0);
            else
                return CreateUnreachableReplyV4(ipPacket, IcmpV4TypeCode.UnreachableHost, 0);
        }

        public static IPPacket CreateUnreachablePortReply(IPPacket ipPacket)
        {
            if (ipPacket.Version == IPVersion.IPv6)
                return CreateUnreachableReplyV6(ipPacket, IcmpV6Type.DestinationUnreachable, 4, 0);
            else
                return CreateUnreachableReplyV4(ipPacket, IcmpV4TypeCode.UnreachablePort, 0);
        }

        public static IPPacket CreatePacketTooBigReply(IPPacket ipPacket, ushort mtu)
        {
            if (ipPacket.Version == IPVersion.IPv6)
                return CreateUnreachableReplyV6(ipPacket, IcmpV6Type.PacketTooBig, 0, mtu);
            else
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
            var icmpPacket = new IcmpV6Packet(new ByteArraySegment(new byte[headerSize])) //todo: PacketDotNet bug
            {
                Type = icmpV6Type,
                Code = code,
                PayloadData = buffer[headerSize..]
            };

            var newIpPacket = new IPv6Packet(ipPacket.DestinationAddress, ipPacket.SourceAddress)
            {
                PayloadPacket = icmpPacket
            };
            newIpPacket.PayloadLength = (ushort)icmpPacket.TotalPacketLength;
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
            var icmpPacket = new IcmpV4Packet(new ByteArraySegment(buffer))
            {
                TypeCode = typeCode,
                Sequence = sequence
            };
            icmpPacket.Checksum = (ushort)ChecksumUtils.OnesComplementSum(icmpPacket.Bytes, 0, icmpPacket.Bytes.Length);

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
            icmpPacket.Checksum = buf != null
                ? (ushort)ChecksumUtils.OnesComplementSum(buf, 0, buf.Length)
                : (ushort)0;
        }

        public static ushort ReadPacketLength(byte[] buffer, int bufferIndex)
        {
            var version = buffer[bufferIndex] >> 4;

            // v4
            if (version == 4)
            {
                var packetLength = (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(buffer, bufferIndex + 2));
                if (packetLength < 20)
                    throw new Exception($"A packet with invalid length has been received! Length: {packetLength}");
                return packetLength;
            }

            // v6
            if (version == 6)
            {
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

            if (ipPacket.Protocol == ProtocolType.IcmpV6)
            {
                var icmpPacket = ipPacket.Extract<IcmpV6Packet>();
                if (icmpPacket != null)
                {
                    var payload = icmpPacket.Bytes[8..] ?? Array.Empty<byte>();
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